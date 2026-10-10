#!/usr/bin/env python3
"""Pre-recorded German speech for Survival German.

Synthesises every German sentence the game can speak with neural Piper voices (via sherpa-onnx) and writes
  src/Nelt.Web/wwwroot/game/audio/<id>.mp3           one file per (voice, text), shared between missions
  src/Nelt.Web/wwwroot/game/missions/m<N>.<lv>.audio.json   { "<who>|<raw text>[|<country>]": "<id>" }
The engine plays these files and falls back to the browser's speech synthesis for anything missing
(e.g. sentences containing the player's own name).

Requirements: pip install sherpa-onnx soundfile num2words ; ffmpeg with libmp3lame.
Voices (CC0 / M-AILABS), from https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models :
  vits-piper-de_DE-thorsten-high, vits-piper-de_DE-kerstin-low, vits-piper-de_DE-ramona-low
Usage: python3 tools/survival-german/make_audio.py <voices-dir> [--only m1.a1] [--dry-run]
"""
import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

from num2words import num2words

ROOT = Path(__file__).resolve().parents[2]
WWW = ROOT / "src" / "Nelt.Web" / "wwwroot" / "game"
MISSIONS = WWW / "missions"
AUDIO = WWW / "audio"

CHALLENGES = {"choice", "listen", "build", "fill", "match", "write", "find"}

# Must match COUNTRIES in survival-german.js (German name, "aus …").
COUNTRIES = [
    ("Libyen", "aus Libyen"), ("Ägypten", "aus Ägypten"), ("Algerien", "aus Algerien"), ("Bahrain", "aus Bahrain"),
    ("Irak", "aus dem Irak"), ("Jemen", "aus dem Jemen"), ("Jordanien", "aus Jordanien"), ("Katar", "aus Katar"),
    ("Kuwait", "aus Kuwait"), ("Libanon", "aus dem Libanon"), ("Marokko", "aus Marokko"), ("Mauretanien", "aus Mauretanien"),
    ("Oman", "aus dem Oman"), ("Palästina", "aus Palästina"), ("Saudi-Arabien", "aus Saudi-Arabien"), ("Somalia", "aus Somalia"),
    ("Sudan", "aus dem Sudan"), ("Syrien", "aus Syrien"), ("Tunesien", "aus Tunesien"),
    ("Vereinigte Arabische Emirate", "aus den Vereinigten Arabischen Emiraten"), ("Dschibuti", "aus Dschibuti"), ("Komoren", "aus den Komoren"),
]

VOICES = {
    # key: (kind, model folder, onnx name, length_scale, noise_scale). Changing a voice or its settings needs a new key
    # (the key is part of the clip id). Chosen and tuned by intelligibility (Whisper round-trip) among the German voices
    # available for sherpa-onnx; the female voices speak a little slower and steadier, which is also kinder to beginners.
    "m": ("piper", "vits-piper-de_DE-thorsten-high", "de_DE-thorsten-high", 1.0, 0.6),   # Thorsten (CC0)
    "fc2": ("coqui", "vits-coqui-de-css10", "model", 1.15, 0.4),                         # CSS10 German, female
    "fe2": ("piper", "vits-piper-de_DE-eva_k-x_low", "de_DE-eva_k-x_low", 1.15, 0.4),    # Eva K (M-AILABS), female
}

FEMALE_NAMES = {"lina", "lina hoffmann", "mia"}


def voice_for(who, cast):
    """Thorsten for men, the player and teaching material; Ramona for Lina; Kerstin for other women and announcements."""
    if who in ("_ann", "_listen"):
        return "fc2"
    if who in ("_", "me"):
        return "m"
    c = cast.get(who, {})
    name = c.get("name", "").strip()
    avatar = c.get("avatar", "")
    if name.lower().startswith("lina"):
        return "fe2"
    female = name.startswith("Frau") or name.lower() in FEMALE_NAMES or any(x in avatar for x in ("♀", "👩", "👵", "💁"))
    return "fc2" if female else "m"


# ------------------------------------------------------------------ what the engine speaks

def spoken(m):
    """Yields (who, raw text) exactly as survival-german.js passes them to speak()."""
    def line(l):
        if l and l.get("de"):
            yield (l.get("who", "_"), l["de"])

    def steps(seq):
        for s in seq:
            t = s["type"]
            if t == "line":
                yield from line(s)
            elif t == "narration" and s.get("de"):
                yield ("_", s["de"])
            elif t == "doc" and s.get("kind") == "announcement":
                yield ("_ann", " ".join(s.get("lines", [])))
            elif t == "learn":
                yield ("_", s["de"])
                yield ("_", s["example"])
            elif t == "grammar":
                for e in s.get("examples", []):
                    yield ("_", e["de"])
            elif t == "decision":
                for o in s["options"]:
                    yield from steps(o.get("then", []))
            elif t in CHALLENGES:
                yield from line(s.get("npc"))
                yield from line(s.get("reply"))
                if t == "listen":
                    yield ("_listen", s["audio"])
                d = s.get("doc")
                if d and d.get("kind") == "announcement":
                    yield ("_ann", " ".join(d.get("lines", [])))

    for sc in m["scenes"]:
        yield from steps(sc["steps"])
    for v in m.get("vocab", []):
        yield ("_", v["de"])
    for e in m.get("expressions", []):
        yield ("_", e["de"])
        yield ("_", e["example"])


# ------------------------------------------------------------------ text normalisation for TTS

def n(x):
    return num2words(int(x), lang="de")


def ordinal(x, suffix="e"):
    w = num2words(int(x), lang="de", to="ordinal")  # "zweite"
    return w[:-1] + suffix if suffix != "e" else w


MONTHS = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"]
DAYS = {"Mo": "Montag", "Di": "Dienstag", "Mi": "Mittwoch", "Do": "Donnerstag", "Fr": "Freitag", "Sa": "Samstag", "So": "Sonntag"}
ABBR = [
    (r"\bHbf\b\.?", "Hauptbahnhof"), (r"\bGl\.", "Gleis"), (r"\bca\.", "circa"), (r"\bz\. ?B\.", "zum Beispiel"),
    (r"\bNr\.", "Nummer"), (r"\bStr\.", "Straße"), (r"\bDr\.", "Doktor"), (r"\bTel\.", "Telefon"), (r"\binkl\.", "inklusive"),
    (r"\bzzgl\.", "zuzüglich"), (r"\bMin\.", "Minuten"), (r"\bStd\.", "Stunden"), (r"\bmax\.", "maximal"), (r"\bmin\.", "mindestens"),
    (r"\bu\. a\.", "unter anderem"), (r"\busw\.", "und so weiter"), (r"\bbzw\.", "beziehungsweise"), (r"\bevtl\.", "eventuell"),
    (r"\bNK\b", "Nebenkosten"), (r"\bWG\b", "W G"), (r"\bICE\b", "I C E"), (r"\bIC\b", "I C"), (r"\bRE\b", "R E"),
    (r"\bFEX\b", "F E X"), (r"\bBER\b", "B E R"), (r"\bDB\b", "D B"), (r"\bEG\b", "Erdgeschoss"), (r"\bOG\b", "Obergeschoss"),
    (r"\bGmbH\b", "G m b H"), (r"\bWLAN\b", "W-LAN"), (r"\bPLZ\b", "Postleitzahl"), (r"\bBVG\b", "B V G"), (r"\bMVG\b", "M V G"),
    (r"\bqm\b", "Quadratmeter"), (r"m²", " Quadratmeter"), (r"\bkg\b", "Kilo"), (r"\bml\b", "Milliliter"), (r"(?<=\d) ?g\b", " Gramm"),
    (r"(?<=\d) ?l\b", " Liter"), (r"\bkm\b", "Kilometer"), (r"°C", " Grad"), (r"(?<=\d) ?%", " Prozent"), (r"\bSt\.", "Stück"),
    (r"\bTbl\.", "Tabletten"), (r"\bh\b(?=\s*$|\s*[,;)])", "Stunden"),
]


def money(m):
    return money_words(m.group(1), m.group(2))


def money_words(euros, cents):
    euros = euros.replace(".", "")
    if cents is None or int(cents) == 0:
        return f"{n(euros)} Euro "
    c = cents.ljust(2, "0")
    if int(euros) == 0:
        return f"{n(c)} Cent "
    return f"{n(euros)} Euro {n(c)} "


ORD_NOUNS = r"(?:Klasse|Stock|Etage|Obergeschoss|Untergeschoss|Mal|Tag|Woche|Monat|Platz|Reihe|" + "|".join(MONTHS) + r")\b"
LETTERS = {"PIN": "Pin", "KM": "Kaltmiete", "ID": "I D", "WC": "W C", "EC": "E C", "TV": "T V", "OK": "okay"}


def normalize(text):
    t = text
    # abbreviation-only brackets add nothing when spoken: "(EBK)", "(Pl.)", "(m² / qm)"
    t = re.sub(r"\s*\((?:\s*(?:[A-ZÄÖÜ]{2,5}|m²|qm|Pl\.|Sg\.|[a-z]{1,4}\.)\s*/?)+\)", "", t)
    t = re.sub(r"\b1-Zi\.-Whg\.", "Ein-Zimmer-Wohnung", t)
    t = re.sub(r"\bZi\.", "Zimmer", t)
    t = re.sub(r"\b1-Zimmer", "Ein-Zimmer", t)
    t = re.sub(r"\bWhg\.", "Wohnung", t)
    t = re.sub(r"\b(\d{1,3})(?:\.(\d{3}))+\b", lambda m: m.group(0).replace(".", ""), t)   # 1.280 -> 1280
    t = re.sub(r"\bdie 112\b", "die eins eins zwei", t)
    t = t.replace(" × ", " mal ").replace("×", " mal ").replace(" = ", " gleich ").replace(" + ", " plus ")
    t = re.sub(r"\s/\s", ", ", t)
    t = re.sub(r"\s*\|\s*", ", ", t)                                    # board columns
    t = re.sub(r"[\[\]▶▢☐☑✓✔→←↑↓➜•·*_#@<>]+", " ", t)                     # symbols
    t = re.sub(r"[\U0001F300-\U0001FAFF☀-➿️‍]", " ", t)  # emoji
    t = t.replace("…", " ").replace("„", "").replace("“", "").replace("”", "").replace("’", "'")
    for k, v in DAYS.items():
        t = re.sub(rf"\b{k}\.(?=[\s,–-])", v, t)
    # dates 09.10.2026 / 09.10.
    t = re.sub(r"\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b", lambda m: f"{ordinal(m.group(1), 'er')} {MONTHS[int(m.group(2)) - 1]} {n(m.group(3))}", t)
    t = re.sub(r"\b(\d{1,2})\.(\d{1,2})\.(?!\d)", lambda m: f"{ordinal(m.group(1), 'er')} {MONTHS[int(m.group(2)) - 1]}", t)
    # durations 4:05 h
    t = re.sub(r"\b(\d{1,2}):(\d{2})\s*h\b", lambda m: f"{n(m.group(1))} Stunden {n(m.group(2))} Minuten", t)
    # times 14:36 (Uhr)
    def time(m):
        h, mi = int(m.group(1)), int(m.group(2))
        return f"{n(h)} Uhr" + (f" {n(mi)}" if mi else "")
    t = re.sub(r"\b(\d{1,2}):(\d{2})(?:\s*Uhr\b)?", time, t)
    t = re.sub(r"\b(\d{1,2})(?:–|-)(\d{1,2}) Uhr\b", lambda m: f"{n(m.group(1))} bis {n(m.group(2))} Uhr", t)
    # money
    t = re.sub(r"(\d[\d.]*)(?:,(\d{1,2}))?\s*€", money, t)
    t = re.sub(r"€\s*(\d[\d.]*)(?:,(\d{1,2}))?", money, t)
    t = re.sub(r"(\d+),-", lambda m: f"{n(m.group(1))} Euro", t)
    t = re.sub(r"(\d+),(\d{2})\s*Euro\b", lambda m: money_words(m.group(1), m.group(2)), t)
    t = t.replace("€", " Euro ")
    # ordinals: "2. Klasse", "im 3. Stock"
    t = re.sub(r"\b(im|am|vom|zum|beim|dem|den|bis zum|ab dem)\s+(\d{1,2})\.\s+(?=" + ORD_NOUNS + ")", lambda m: f"{m.group(1)} {ordinal(m.group(2), 'en')} ", t, flags=re.I)
    t = re.sub(r"\b(\d{1,2})\.\s+(?=" + ORD_NOUNS + ")", lambda m: f"{ordinal(m.group(1))} ", t)
    # phone numbers: read digit groups one by one
    t = re.sub(r"\b0\d{2,5}[ /-]\d[\d ]{3,}\d\b", lambda m: " ".join(n(d) for d in re.sub(r"\D", "", m.group(0))), t)
    # decimal commas
    t = re.sub(r"\b(\d+),(\d+)\b", lambda m: f"{n(m.group(1))} Komma {' '.join(n(d) for d in m.group(2))}", t)
    # ranges 8–12
    t = re.sub(r"\b(\d+)\s*[–-]\s*(\d+)\b", lambda m: f"{n(m.group(1))} bis {n(m.group(2))}", t)
    for pat, rep in ABBR:
        t = re.sub(pat, rep, t)
    t = re.sub(r"\b[A-ZÄÖÜ]{2,4}\b", lambda m: LETTERS.get(m.group(0), " ".join(m.group(0))), t)
    t = re.sub(r"\bEuro\s+Euro\b", "Euro", t)
    t = re.sub(r"(?<=\w)/(?=\w)", " oder ", t)
    t = re.sub(r"\s+([,.!?;:])", r"\1", t)
    t = re.sub(r"([,;:])(?=\S)", r"\1 ", t)
    t = re.sub(r"(,\s*)+", ", ", t)
    t = re.sub(r"\s+", " ", t).strip(" ,;:–-")
    if t and t[-1] not in ".!?":
        t += "."
    return t


# ------------------------------------------------------------------ synthesis

class Synth:
    def __init__(self, voices_dir):
        import sherpa_onnx
        self.engines = {}
        self.voices_dir = Path(voices_dir)
        self.sherpa = sherpa_onnx

    def get(self, key):
        if key not in self.engines:
            kind, folder, name, length, noise = VOICES[key]
            d = self.voices_dir / folder
            extra = {"data_dir": str(d / "espeak-ng-data")} if kind == "piper" else {}
            cfg = self.sherpa.OfflineTtsConfig(model=self.sherpa.OfflineTtsModelConfig(
                vits=self.sherpa.OfflineTtsVitsModelConfig(model=str(d / f"{name}.onnx"), tokens=str(d / "tokens.txt"),
                                                           noise_scale=noise, noise_scale_w=0.8, length_scale=length, **extra),
                num_threads=2))
            self.engines[key] = self.sherpa.OfflineTts(cfg)
        return self.engines[key]

    def render(self, voice, text, out_mp3):
        import soundfile as sf
        if VOICES[voice][0] == "coqui":
            text = graphemes(text)
        a = self.get(voice).generate(text, sid=0, speed=1.0)
        with tempfile.NamedTemporaryFile(suffix=".wav") as wav:
            sf.write(wav.name, a.samples, a.sample_rate)
            # light loudness normalisation, a short lead-in so playback never clips the first syllable
            subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", wav.name,
                            "-af", "adelay=120,loudnorm=I=-18:TP=-1.5:LRA=11,aresample=22050",
                            "-ac", "1", "-codec:a", "libmp3lame", "-b:a", "48k", str(out_mp3)], check=True)


def without_name(raw):
    """Recordings can't say the player's name. Where it is only a form of address ("Hallo {name}!", "Freut mich, {name}!")
    the clip leaves it out; elsewhere (e.g. "Mein Name ist {name}.") None is returned and the browser voice is used."""
    if "{name}" not in raw:
        return raw
    t = re.sub(r",\s*\{name\}(?=\s*[!,.?])", "", raw)
    t = re.sub(r"^\{name\},\s*(\w)", lambda m: m.group(1).upper(), t)
    t = re.sub(r"\b(Hallo|Hi|Hey|Morgen|Guten Morgen|Guten Tag|Guten Abend)\s+\{name\}(?=\s*[!,.?])", r"\1", t)
    return None if "{name}" in t else t


def graphemes(text):
    """The CSS10 model reads lower-case letters only: spell out digits, drop anything outside its alphabet."""
    t = re.sub(r"\d+", lambda m: f" {n(m.group(0))} ", text)
    t = t.replace("–", ",").replace("—", ",").replace('"', "").lower()
    t = re.sub(r"[^a-zäöüß!'(),\-.:;? ]", " ", t)
    return re.sub(r"\s+", " ", t).strip()


def audio_id(voice, text):
    return hashlib.sha1(f"{voice}|{text}".encode("utf-8")).hexdigest()[:14]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("voices")
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    AUDIO.mkdir(parents=True, exist_ok=True)
    synth = None if args.dry_run else Synth(args.voices)
    files = sorted(MISSIONS.glob("m[1-7].a[12].json"))
    jobs = {}
    manifests = {}
    for f in files:
        if args.only and f.stem not in args.only:
            continue
        m = json.loads(f.read_text(encoding="utf-8"))
        cast = m.get("cast", {})
        manifest = {}
        for who, raw in spoken(m):
            said = without_name(raw)
            if said is None:
                continue  # the player's name is part of the sentence: the browser voice says it
            voice = voice_for(who, cast)
            variants = [(None, said)]
            if re.search(r"\{(country|fromCountry)\}", raw):
                variants = [(c, said.replace("{fromCountry}", frm).replace("{country}", c)) for c, frm in COUNTRIES]
            for country, text in variants:
                spoken_text = normalize(text)
                aid = audio_id(voice, spoken_text)
                key = f"{who}|{raw}" + (f"|{country}" if country else "")
                manifest[key] = aid
                jobs[aid] = (voice, spoken_text)
        manifests[f] = manifest

    todo = [(aid, v, t) for aid, (v, t) in jobs.items() if not (AUDIO / f"{aid}.mp3").exists()]
    print(f"{len(jobs)} clips, {len(todo)} to render", file=sys.stderr)
    if args.dry_run:
        for aid, v, t in sorted(todo, key=lambda x: x[2]):
            print(f"{v}\t{t}")
        return
    for i, (aid, v, t) in enumerate(todo, 1):
        synth.render(v, t, AUDIO / f"{aid}.mp3")
        if i % 50 == 0:
            print(f"  {i}/{len(todo)}", file=sys.stderr)

    for f, manifest in manifests.items():
        out = f.with_name(f"{f.stem}.audio.json")
        out.write_text(json.dumps(manifest, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")

    # remove clips no mission uses any more (only on a full run)
    if not args.only:
        used = set(jobs)
        for p in AUDIO.glob("*.mp3"):
            if p.stem not in used:
                p.unlink()
    print("done", file=sys.stderr)


if __name__ == "__main__":
    main()
