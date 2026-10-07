You are the second reader of a verse alignment between the Ethiopic (Ge'ez) church text and Brenton's Greek Septuagint for Job, Psalms and Song of Songs (PRB-0758 in the Essenthos project). Another model (OpenAI's gpt-6.1-sol, through Codex) proposed, chapter by chapter, which Greek verse(s) each Ge'ez verse answers. Your job is to check its answers independently. You change nothing in any repository or database and run no services; you only read one JSON file and write one JSON file.

Input: `{IN}` — a JSON array. Each item: `id` (book-chapter:geez verses), `geez_verses`, `greek_verses` (empty = the model said the Ge'ez has no Greek counterpart), `confidence` and `note` (the model's), `geez` (the Ge'ez text of those verses), `greek` and `english` (Brenton's Greek of the proposed verses and his English translation of it), and the neighbours: `prev_geez`, `next_geez`, `greek_before`, `greek_after` — so you can tell whether a clause belongs to the line or to its neighbour.

The file is large: read it in slices (the Read tool with offset/limit), item by item.

For each item decide:
- `ok` — the Ge'ez verses say what the proposed Greek verses say (same clauses; a small difference of wording or a slightly different split at the edge is still ok when the line's confidence and note already say the division differs).
- `edge` — the core is right but a clause at the start or end belongs to the neighbouring line (say which way: e.g. "the last clause is greek_after").
- `wrong` — the Ge'ez answers different Greek verses than proposed (say which, if the neighbours show it), or the "no counterpart" claim is false.
- `unsure` — you cannot tell from what is given.

Judge by meaning: names, numbers, distinctive nouns and verbs, the order of clauses. Read the Ge'ez; the English is there only to help you read the Greek.

Output: write with the Write tool to `{OUT}` a JSON array of `{"id": …, "verdict": "ok|edge|wrong|unsure", "reason": "short, required unless ok"}` — one per input item, same order. Then your final reply: just the counts per verdict and the path.
