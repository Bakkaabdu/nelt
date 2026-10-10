# Survival German — mission content format

Each mission × level is one JSON file in `src/Nelt.Web/wwwroot/game/missions/`, named `m<N>.<level>.json`
(e.g. `m1.a1.json`, `m1.a2.json`). The game engine (`wwwroot/js/survival-german.js`) plays the file;
`python3 tools/survival-german/validate.py` checks it.

The audience is **Arabic-speaking beginners**. Game UI and explanations are Arabic (`ar` fields);
learning content is German (`de` fields). Arabic must be Modern Standard Arabic, clear and short.

## Placeholders (allowed in any German or Arabic string)

| Placeholder | Example value |
|---|---|
| `{name}` | the player's first name, e.g. `Omar` |
| `{country}` | German country name, e.g. `Libyen`, `der Irak` → engine gives `Libyen` / `Irak` |
| `{fromCountry}` | "aus …" with the correct article, e.g. `aus Libyen`, `aus dem Irak`, `aus dem Sudan` |
| `{countryAr}` | the country in Arabic, e.g. `ليبيا` |

The player picks name and country (all Arabic-speaking countries) on the start screen. Never hard-code the
player's own name or nationality. The player's mother tongue is always Arabic (`Arabisch`).

## Shared story (keep continuity!)

The player has just landed at **Flughafen BER** (Berlin). They will live in **München** for a while:
their friend **Lina Hoffmann** (German, ~28, speaks a little Arabic, warm and funny) lets them stay in
her WG (shared flat) in **München-Schwabing, Belgradstraße 14** until the end of the month. Lina often sends
short German text messages (`doc` kind `message`) — great reading practice.

1. **m1 Airport** — BER, Terminal 1. Ends at the airport train station (Flughafen BER, platform underground).
2. **m2 Train** — Berlin Hauptbahnhof (they rode the regional train FEX/RE from BER); buy a ticket and board the ICE to München Hbf.
3. **m3 Supermarket** — first morning in Munich; Lina left a shopping list. Supermarket "Frischmarkt" (fictional) near the flat.
4. **m4 Restaurant** — evening; café/restaurant "Gasthaus Zum Goldenen Hirsch" (fictional) near Marienplatz. Lina may join late or not.
5. **m5 Doctor** — a few days later the player feels unwell; Hausarztpraxis Dr. Keller (fictional), Leopoldstraße.
6. **m6 Job interview** — invitation from "Café Sonnenschein GmbH" (fictional small bakery-café chain, Munich) for a job (A1: Aushilfe in der Küche / Servicekraft mini-job; A2: Servicekraft, Teilzeit). Interviewer: Herr Brandt (Filialleiter); maybe Frau Yilmaz from HR.
7. **m7 Accommodation** — Lina's flatmate returns at the end of the month; the player must find their own room. Landlady Frau Wagner and/or a WG; real-looking ads (fictional names).

Use only fictional businesses and people (no real brands except place names and generic terms like ICE, S-Bahn, Hauptbahnhof).
Reuse **core survival phrases** across missions so they get reinforced: *Entschuldigung*, *Wie bitte?*,
*Können Sie das bitte wiederholen?*, *Können Sie bitte langsamer sprechen?*, *Ich verstehe nicht.*,
*Ich spreche nur ein bisschen Deutsch.*, *Danke schön / Bitte schön*, numbers, prices (`3,49 €`), times (`14:35 Uhr`).
Each mission should also reuse a few words from earlier missions (e.g. m4 re-uses prices/paying from m3, m6 re-uses greetings from m1).

## Levels

- **A1 — Beginner Survival**: short sentences (≈ 3–8 words), present tense, basic word order, articles, *möchten/können*,
  W-questions, numbers, prices, times. NPCs speak simply. Arabic help is generous.
- **A2 — Everyday Life**: longer dialogues, Perfekt (*Ich habe … gekauft*, *Ich bin … gefahren*), Präteritum of *sein/haben*,
  modal verbs, *weil/dass/wenn* clauses, separable verbs, dative after common prepositions, polite *Könnten Sie …? / Ich hätte gern …*.
  Never B1 (no Konjunktiv I, no passive beyond trivial set phrases, no Plusquamperfekt, no complex relative-clause chains).

## File structure

```jsonc
{
  "id": "m1",                       // m1..m7
  "level": "A1",                    // "A1" | "A2"
  "title":    { "de": "Ankunft am Flughafen", "ar": "الوصول إلى المطار" },
  "place":    "Flughafen BER, Terminal 1",
  "intro":    "Arabic story intro, 2–4 sentences, continues from the previous mission.",
  "objective":"Arabic: what the player must achieve.",
  "outro":    "Arabic: 2–3 sentences closing the chapter and teasing the next one.",
  "cast": {                          // everyone who speaks; "me" is the player and is added automatically
    "staff": { "name": "Frau Neumann", "role": "موظفة الاستعلامات", "avatar": "👩‍💼" }
  },
  "vocab":       [ { "de": "der Ausgang", "ar": "المخرج" } ],            // 18–35 items, with article for nouns
  "expressions": [ { "de": "Wo ist …?", "ar": "أين …؟", "use": "Arabic: when to use", "example": "Wo ist der Ausgang?", "exampleAr": "أين المخرج؟" } ], // 6–12
  "summary":     [ "Arabic bullet: what the student learned" ],          // 5–8 bullets
  "scenes": [
    { "id": "arrival", "title": { "de": "Die Ankunftshalle", "ar": "صالة الوصول" }, "steps": [ /* steps */ ] }
  ]
}
```

A mission has **4–7 scenes** and **15–25 challenges** in total (A2 can be at the upper end), mixed with
dialogue, narration, documents and learning cards so it plays like a story (≈ 10–20 minutes).

## Step types

### Story / non-scored

```jsonc
{ "type": "narration", "ar": "Arabic narration of what happens." , "de": "optional German line (e.g. something the player reads)" }
{ "type": "line", "who": "staff", "de": "Guten Tag! Kann ich Ihnen helfen?", "ar": "مرحبًا! هل يمكنني مساعدتك؟" }
{ "type": "line", "who": "me", "de": "Danke schön!", "ar": "شكرًا جزيلًا!" }
{ "type": "doc", "kind": "sign", "title": "optional", "lines": ["Ausgang", "Gepäckausgabe →"], "ar": "translation, hidden until revealed" }
{ "type": "learn", "de": "Wie bitte?", "ar": "عفوًا؟ / ماذا قلت؟", "use": "Arabic: when it is used", "example": "Wie bitte? Ich verstehe nicht.", "exampleAr": "..." }
{ "type": "grammar", "title": "Arabic title", "body": "Arabic explanation (2–5 short sentences, can use **bold**)", "examples": [ { "de": "Ich komme aus Syrien.", "ar": "أنا من سوريا." } ] }
{ "type": "decision", "prompt": "Arabic: what do you do?", "options": [
    { "ar": "Arabic option label", "de": "optional German", "then": [ /* steps that play if chosen (may include challenges) */ ] }
] }
```

`doc.kind` ∈ `sign | board | ticket | ad | menu | receipt | message | list | form | announcement | screen | letter`.
`announcement` is read aloud (text-to-speech) and its text is hidden until the player reveals it.
`board` = departures board; `lines` can use ` | ` to separate columns: `"14:35 | ICE 1007 | München Hbf | Gl. 7"`.
`line.ar` is hidden until the player taps "ترجمة".

### Challenges (scored)

Every challenge has: `"id"` (unique in the file), `"hint"` (Arabic, nudges without giving away),
`"explain"` (Arabic, why the right answer is right — 1–2 sentences, shown after success),
optional `"skill"` ∈ `dialogue | reading | listening | vocab | grammar | numbers | writing`,
optional `"bonus": true` (extra points, max 2 per mission), and exactly **one** challenge per mission has `"final": true`
(the last one; combines what the mission taught).

```jsonc
// Multiple choice / dialogue response / reading comprehension.
// "npc" = a line said right before the question (shown as dialogue). "doc" = an inline document to read.
{ "type": "choice", "id": "c1", "skill": "dialogue",
  "npc": { "who": "staff", "de": "Guten Tag! Kann ich Ihnen helfen?", "ar": "مرحبًا! هل يمكنني مساعدتك؟" },
  "prompt": "Arabic question (or German for A2 when appropriate)",
  "options": [
    { "de": "Ja, bitte. Wo ist der Bahnhof?", "correct": true },
    { "de": "Ja, gern. Ich suche den Bahnhof.", "correct": true },        // more than one can be correct!
    { "de": "Ich bin dreiundzwanzig Jahre alt.", "why": "Arabic: why this does not fit" },
    { "de": "Ich esse gern Pizza.", "why": "..." }
  ],
  "reply": { "who": "staff", "de": "Der Bahnhof ist unten, im Untergeschoss.", "ar": "..." },   // optional: NPC reacts after a correct answer
  "hint": "...", "explain": "..." }
// options may use "ar" instead of "de" when the answer is a meaning (e.g. "What does the sign mean?").

// Listening: the engine speaks "audio" (German TTS). Text is hidden until revealed.
{ "type": "listen", "id": "c2", "audio": "Der Zug nach München fährt heute von Gleis 7.", "audioAr": "...",
  "prompt": "Arabic question", "options": [ ...same as choice... ], "hint": "...", "explain": "..." }

// Sentence building from tiles. "tiles" = all tiles shown (correct words + 1–3 distractors).
// "answers" = every acceptable order (each must use only tiles, each tile at most once). Punctuation is a separate tile.
{ "type": "build", "id": "c3", "prompt": "Arabic: build the sentence 'where is the exit?'",
  "tiles": ["Wo", "ist", "der", "Ausgang", "?", "die", "bist"],
  "answers": [["Wo", "ist", "der", "Ausgang", "?"]], "hint": "...", "explain": "..." }

// Fill one blank (___ exactly once). "answers" = all acceptable options (subset of options).
{ "type": "fill", "id": "c4", "sentence": "Ich ___ nur ein bisschen Deutsch.", "ar": "أتحدث القليل فقط من الألمانية.",
  "options": ["spreche", "sprichst", "spricht"], "answers": ["spreche"], "hint": "...", "explain": "..." }

// Matching German ↔ Arabic (4–6 pairs).
{ "type": "match", "id": "c5", "prompt": "Arabic instruction", "pairs": [["der Ausgang", "المخرج"], ["das Gepäck", "الأمتعة"]], "hint": "...", "explain": "..." }

// Short typed answer. Comparison ignores case, punctuation, extra spaces; ä=ae, ö=oe, ü=ue, ß=ss are accepted.
// List every reasonable correct variant.
{ "type": "write", "id": "c6", "prompt": "Arabic: write in German 'I don't understand'",
  "answers": ["Ich verstehe nicht", "Ich verstehe das nicht", "Ich verstehe Sie nicht"], "hint": "...", "explain": "..." }

// Find/select items in a grid (e.g. shopping, signs, body parts). The player selects exactly the targets.
{ "type": "find", "id": "c7", "prompt": "Arabic instruction", "clue": "optional German clue text (e.g. Lina's list)",
  "items": [ { "de": "die Milch", "emoji": "🥛", "ar": "الحليب" }, { "de": "das Brot", "emoji": "🍞", "ar": "الخبز" } ],
  "targets": ["die Milch"], "hint": "...", "explain": "..." }
```

## Quality rules

- German must be natural, correct (articles, cases, word order, punctuation, „ “ not needed — plain quotes fine) and level-appropriate.
- Every correct answer must really be correct; every wrong option must be clearly wrong *in context* and have a `why`.
  If a grammatically correct and contextually fine alternative exists, mark it correct too.
- Vary challenge types: no more than 2 identical types in a row; each mission uses at least 6 of the 7 challenge types.
- Each scene mixes story (narration/lines/docs) with 2–5 challenges. Include at least one `decision`, one unexpected
  event/misunderstanding, at least 2 `learn` cards, at least 1 `grammar` card, at least 2 `doc`s and at least 2 `listen`.
- Don't put the Arabic translation of an NPC line inside the prompt of the challenge about that line.
- Prices use German format: `2,49 €`. Times: `14:35 Uhr`. Use realistic Munich/Berlin details.
- Keep JSON valid UTF-8, 2-space indent.
