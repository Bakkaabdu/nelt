#!/usr/bin/env python3
"""Validates Survival German mission files (see SCHEMA.md).

Usage: python3 -I tools/survival-german/validate.py [file ...]
With no arguments every src/Nelt.Web/wwwroot/game/missions/*.json file is checked.
Exit code 1 when any error is found. Warnings do not fail.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MISSIONS = ROOT / "src" / "Nelt.Web" / "wwwroot" / "game" / "missions"

STORY = {"narration", "line", "doc", "learn", "grammar", "decision"}
CHALLENGES = {"choice", "listen", "build", "fill", "match", "write", "find"}
DOC_KINDS = {"sign", "board", "ticket", "ad", "menu", "receipt", "message", "list", "form", "announcement", "screen", "letter"}
SKILLS = {"dialogue", "reading", "listening", "vocab", "grammar", "numbers", "writing"}
PLACEHOLDERS = {"name", "country", "fromCountry", "countryAr"}
ARABIC = re.compile(r"[؀-ۿ]")


class Report:
    def __init__(self, path):
        self.path = path
        self.errors = []
        self.warnings = []

    def err(self, where, msg):
        self.errors.append(f"{where}: {msg}")

    def warn(self, where, msg):
        self.warnings.append(f"{where}: {msg}")


def text(v):
    return isinstance(v, str) and v.strip() != ""


def check_placeholders(rep, where, value):
    if isinstance(value, str):
        for ph in re.findall(r"\{(\w+)\}", value):
            if ph not in PLACEHOLDERS:
                rep.err(where, f"unknown placeholder {{{ph}}}")
    elif isinstance(value, list):
        for i, v in enumerate(value):
            check_placeholders(rep, f"{where}[{i}]", v)
    elif isinstance(value, dict):
        for k, v in value.items():
            check_placeholders(rep, f"{where}.{k}", v)


def need(rep, where, obj, *keys, arabic=()):
    for k in keys:
        if not text(obj.get(k)):
            rep.err(where, f"missing or empty '{k}'")
    for k in arabic:
        if text(obj.get(k)) and not ARABIC.search(obj[k]):
            rep.warn(where, f"'{k}' should be Arabic")


def check_options(rep, where, ch):
    opts = ch.get("options")
    if not isinstance(opts, list) or len(opts) < 2:
        rep.err(where, "needs at least 2 options")
        return
    if len(opts) > 5:
        rep.warn(where, "more than 5 options")
    correct = 0
    seen = set()
    for i, o in enumerate(opts):
        label = o.get("de") or o.get("ar")
        if not text(label):
            rep.err(f"{where}.options[{i}]", "option needs 'de' or 'ar'")
            continue
        if label in seen:
            rep.err(f"{where}.options[{i}]", f"duplicate option '{label}'")
        seen.add(label)
        if o.get("correct") is True:
            correct += 1
        elif not text(o.get("why")):
            rep.err(f"{where}.options[{i}]", "wrong option needs 'why' (Arabic)")
    if correct == 0:
        rep.err(where, "no option is marked correct")
    if correct == len(opts):
        rep.err(where, "every option is correct")


def check_line(rep, where, line, cast, need_ar=True):
    if line.get("who") != "me" and line.get("who") not in cast:
        rep.err(where, f"unknown speaker '{line.get('who')}'")
    need(rep, where, line, "de", *(["ar"] if need_ar else []), arabic=("ar",))


def check_challenge(rep, where, ch, cast, ids, counts):
    cid = ch.get("id")
    if not text(cid):
        rep.err(where, "challenge needs an 'id'")
    elif cid in ids:
        rep.err(where, f"duplicate id '{cid}'")
    else:
        ids.add(cid)
    need(rep, where, ch, "hint", "explain", arabic=("hint", "explain"))
    if ch.get("skill") is not None and ch["skill"] not in SKILLS:
        rep.err(where, f"unknown skill '{ch['skill']}'")
    t = ch["type"]
    if t in ("choice", "listen"):
        if t == "listen":
            need(rep, where, ch, "audio")
        if "npc" in ch:
            check_line(rep, f"{where}.npc", ch["npc"], cast)
        if "doc" in ch:
            check_step(rep, f"{where}.doc", dict(ch["doc"], type="doc"), cast, ids, counts)
        if "reply" in ch:
            check_line(rep, f"{where}.reply", ch["reply"], cast)
        need(rep, where, ch, "prompt")
        check_options(rep, where, ch)
    elif t == "build":
        need(rep, where, ch, "prompt")
        tiles = ch.get("tiles")
        answers = ch.get("answers")
        if not isinstance(tiles, list) or len(tiles) < 3:
            rep.err(where, "needs 'tiles' (≥3)")
            return
        if not isinstance(answers, list) or not answers:
            rep.err(where, "needs 'answers'")
            return
        pool = Counter(tiles)
        for i, a in enumerate(answers):
            if not isinstance(a, list) or not a:
                rep.err(f"{where}.answers[{i}]", "must be a list of tiles")
                continue
            if Counter(a) - pool:
                rep.err(f"{where}.answers[{i}]", f"uses words that are not tiles: {sorted((Counter(a) - pool).elements())}")
        if all(len(a) == len(tiles) for a in answers if isinstance(a, list)):
            rep.warn(where, "no distractor tiles")
        if len(tiles) > 14:
            rep.warn(where, "more than 14 tiles is hard on mobile")
    elif t == "fill":
        s = ch.get("sentence", "")
        if s.count("___") != 1:
            rep.err(where, "sentence must contain ___ exactly once")
        opts = ch.get("options") or []
        ans = ch.get("answers") or []
        if len(opts) < 2:
            rep.err(where, "needs ≥2 options")
        if not ans or any(a not in opts for a in ans):
            rep.err(where, "answers must be non-empty and a subset of options")
        if len(set(opts)) != len(opts):
            rep.err(where, "duplicate options")
        if len(ans) == len(opts):
            rep.err(where, "every option is an answer")
    elif t == "match":
        need(rep, where, ch, "prompt")
        pairs = ch.get("pairs") or []
        if not 3 <= len(pairs) <= 7:
            rep.err(where, "needs 3–7 pairs")
        left = [p[0] for p in pairs if isinstance(p, list) and len(p) == 2]
        right = [p[1] for p in pairs if isinstance(p, list) and len(p) == 2]
        if len(left) != len(pairs):
            rep.err(where, "each pair must be [de, ar]")
        if len(set(left)) != len(left) or len(set(right)) != len(right):
            rep.err(where, "pair sides must be unique")
    elif t == "write":
        need(rep, where, ch, "prompt")
        if not ch.get("answers"):
            rep.err(where, "needs 'answers'")
    elif t == "find":
        need(rep, where, ch, "prompt")
        items = ch.get("items") or []
        names = [i.get("de") for i in items]
        targets = ch.get("targets") or []
        if len(items) < 4:
            rep.err(where, "needs ≥4 items")
        if len(set(names)) != len(names):
            rep.err(where, "duplicate item names")
        if not targets or any(tg not in names for tg in targets):
            rep.err(where, "targets must be non-empty and name existing items")
        if len(targets) == len(items):
            rep.err(where, "every item is a target")


def check_step(rep, where, step, cast, ids, counts):
    t = step.get("type")
    counts["steps"][t] += 1
    if t in CHALLENGES:
        counts["challenges"].append(step)
        check_challenge(rep, where, step, cast, ids, counts)
    elif t == "narration":
        need(rep, where, step, "ar", arabic=("ar",))
    elif t == "line":
        check_line(rep, where, step, cast)
    elif t == "doc":
        if step.get("kind") not in DOC_KINDS:
            rep.err(where, f"unknown doc kind '{step.get('kind')}'")
        if not isinstance(step.get("lines"), list) or not step["lines"]:
            rep.err(where, "doc needs 'lines'")
    elif t == "learn":
        need(rep, where, step, "de", "ar", "use", "example", "exampleAr", arabic=("ar", "use", "exampleAr"))
    elif t == "grammar":
        need(rep, where, step, "title", "body", arabic=("title", "body"))
        if not step.get("examples"):
            rep.err(where, "grammar needs 'examples'")
    elif t == "decision":
        need(rep, where, step, "prompt")
        opts = step.get("options") or []
        if len(opts) < 2:
            rep.err(where, "decision needs ≥2 options")
        for i, o in enumerate(opts):
            need(rep, f"{where}.options[{i}]", o, "ar")
            for j, s in enumerate(o.get("then") or []):
                check_step(rep, f"{where}.options[{i}].then[{j}]", s, cast, ids, counts)
    else:
        rep.err(where, f"unknown step type '{t}'")


def validate(path):
    rep = Report(path)
    try:
        m = json.loads(Path(path).read_text(encoding="utf-8"))
    except Exception as e:  # noqa: BLE001
        rep.err("file", f"invalid JSON: {e}")
        return rep, None
    name = Path(path).name
    mm = re.fullmatch(r"(m[1-7])\.(a1|a2)\.json", name)
    if not mm:
        rep.err("file", "name must be m<1-7>.<a1|a2>.json")
    elif m.get("id") != mm.group(1) or m.get("level") != mm.group(2).upper():
        rep.err("file", "id/level do not match the file name")
    for k in ("place", "intro", "objective", "outro"):
        if not text(m.get(k)):
            rep.err("mission", f"missing '{k}'")
    for k in ("de", "ar"):
        if not text((m.get("title") or {}).get(k)):
            rep.err("mission", f"missing title.{k}")
    cast = m.get("cast") or {}
    for key, c in cast.items():
        need(rep, f"cast.{key}", c, "name", "role")
    vocab = m.get("vocab") or []
    if len(vocab) < 18:
        rep.warn("vocab", f"only {len(vocab)} items (aim for 18–35)")
    for i, v in enumerate(vocab):
        need(rep, f"vocab[{i}]", v, "de", "ar", arabic=("ar",))
    exps = m.get("expressions") or []
    if len(exps) < 6:
        rep.warn("expressions", f"only {len(exps)} (aim for 6–12)")
    for i, e in enumerate(exps):
        need(rep, f"expressions[{i}]", e, "de", "ar", "use", "example", "exampleAr")
    if len(m.get("summary") or []) < 4:
        rep.warn("summary", "fewer than 4 bullets")

    ids = set()
    counts = {"steps": Counter(), "challenges": []}
    scenes = m.get("scenes") or []
    if not 4 <= len(scenes) <= 8:
        rep.warn("scenes", f"{len(scenes)} scenes (aim for 4–7)")
    scene_ids = set()
    for si, sc in enumerate(scenes):
        w = f"scenes[{si}]"
        if not text(sc.get("id")) or sc["id"] in scene_ids:
            rep.err(w, "scene needs a unique 'id'")
        scene_ids.add(sc.get("id"))
        for k in ("de", "ar"):
            if not text((sc.get("title") or {}).get(k)):
                rep.err(w, f"missing title.{k}")
        steps = sc.get("steps") or []
        if not steps:
            rep.err(w, "scene has no steps")
        for i, st in enumerate(steps):
            check_step(rep, f"{w}.steps[{i}]", st, cast, ids, counts)

    chs = counts["challenges"]
    finals = [c for c in chs if c.get("final")]
    if len(finals) != 1:
        rep.err("mission", f"needs exactly one final challenge, found {len(finals)}")
    elif scenes and finals[0] not in [s for s in scenes[-1].get("steps", [])]:
        rep.warn("mission", "the final challenge should be in the last scene")
    if sum(1 for c in chs if c.get("bonus")) > 2:
        rep.warn("mission", "more than 2 bonus challenges")
    # Count challenges on the main path (decision branches count once: the longest branch).
    n = len(chs)
    if n < 15:
        rep.err("mission", f"only {n} challenges (need 15–25)")
    elif n > 30:
        rep.warn("mission", f"{n} challenges (aim for 15–25)")
    types = Counter(c["type"] for c in chs)
    if len(types) < 6:
        rep.warn("mission", f"uses only {len(types)} challenge types: {dict(types)}")
    st = counts["steps"]
    for t, minimum in (("decision", 1), ("learn", 2), ("grammar", 1), ("doc", 2), ("listen", 2)):
        if st[t] < minimum:
            rep.warn("mission", f"has {st[t]} '{t}' steps (want ≥{minimum})")
    check_placeholders(rep, "mission", m)
    return rep, {"challenges": n, "types": dict(types), "scenes": len(scenes)}


def main(argv):
    files = [Path(a) for a in argv] or sorted(MISSIONS.glob("m[1-7].a[12].json"))
    failed = False
    for f in files:
        rep, stats = validate(f)
        status = "FAIL" if rep.errors else "ok"
        print(f"[{status}] {f.name}" + (f"  {stats['scenes']} scenes, {stats['challenges']} challenges, {stats['types']}" if stats else ""))
        for e in rep.errors:
            print(f"   error: {e}")
        for w in rep.warnings:
            print(f"   warn:  {w}")
        failed |= bool(rep.errors)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
