# Ge'ez dictionary batches (FTR-0824, FTR-1141)

Two Claude runs on the owner's API key (NOT-0230), through the project's client `scripts/claude_api.py`
(guide `claude-api`): Message Batches at half price, one cached system prompt per run, a JSON-schema
answer, and a cap that no send may pass (RUL-0199, RUL-0200).

- **Dillmann (FTR-0824):** every loaded entry of Dillmann's *Lexicon Linguae Aethiopicae* with Latin
  glosses (9,685 of 9,910), sense by sense in his numbering, rendered into en, uk, de and es. About
  30 entries a request, 676 requests. Only Dillmann's own senses: TraCES' blocks inside an entry
  (`xml:id` starting with T, among them Leslau's English) are left out even where the edition marks
  them `source="#dillmann"` (PRB-1794).
- **Contextual gloss (FTR-1141):** each Ge'ez word of GEEZ81 in its verse: lemma (Dillmann's headword
  where it fits), its sense here (en, uk), the whole word as an interlinear line (en, uk), the Greek
  words it renders (keys from the request only), clitics and suffixes, confidence. Whole verses, about
  90 Ge'ez words a request, with the Greek (Brenton's LXX Greek, Nestle 1904 for the NT), an English
  translation (Brenton, KJV), today's aligner links and the read-time Dillmann match per word.

## The key

The client reads `ANTHROPIC_API_KEY` from the environment, or else the `ANTHROPIC_API_KEY=` line of
`essenthos-core/.env` (git-ignored). The owner sets it himself; nobody pastes it into a chat, a ticket
or a command line. For one PowerShell session, without it reaching the history:

```powershell
$env:ANTHROPIC_API_KEY = Read-Host -MaskInput 'Anthropic key'
```

Agents never read, print or ask for it.

## Commands

Run from the main checkout (its `.claude-api/usage.jsonl` is the ledger, its `.env` the key); a run
folder given as a bare name lands in `Resources/Essenthos/generation/<name>/` (gitignored, backed up).
`--since` is when the owner's budget started (counted over every `geez-*` label in the ledger), and
`--budget` his cap in dollars: each batch gets `cap_usd` = what is left of it, so all the Ge'ez runs
together never pass it.

```bash
cd essenthos-core
G="python -X utf8 scripts/geez-batch/geez_batch.py"
B="--budget 125 --since 2026-11-16T00:00:00+00:00"

# pilot, the same packets on both models
$G export-gloss --run geez-gloss-pilot-sonnet-v1 --chapters GEN:1,PSA:22,JHN:1
$G build        --run geez-gloss-pilot-sonnet-v1 --job gloss --model claude-sonnet-5-5 --effort high
$G build        --run geez-gloss-pilot-opus-v1   --job gloss --model claude-opus-5-5   --effort high \
                --from Resources/Essenthos/generation/geez-gloss-pilot-sonnet-v1
$G estimate     --run geez-gloss-pilot-sonnet-v1
$G submit       --run geez-gloss-pilot-sonnet-v1 $B
$G submit       --run geez-gloss-pilot-opus-v1   $B
$G collect      --run geez-gloss-pilot-sonnet-v1 --wait
$G collect      --run geez-gloss-pilot-opus-v1   --wait

# Dillmann: about 100 entries first, then the rest
$G export-dillmann --run geez-dillmann-v1
$G build    --run geez-dillmann-v1 --job dillmann --model claude-sonnet-5-5 --effort high
$G estimate --run geez-dillmann-v1
$G submit   --run geez-dillmann-v1 $B --sample 4
$G collect  --run geez-dillmann-v1 --wait
$G submit   --run geez-dillmann-v1 $B            # every request not yet sent, and the rejects
$G collect  --run geez-dillmann-v1 --wait
$G report   --run geez-dillmann-v1                # the ledger summary for the entity
```

PSA 22 is the shepherd psalm: GEEZ81 numbers the Psalms as the Septuagint does.

The books Brenton's Greek does not hold are read beside what there is (`chapter_sources`):

- **Messale and Tägsas** (91, 92) are Proverbs as the church divides it (Messale 1-24 with Proverbs 30 and
  31:1-9 in the Greek order, Tägsas 25-29 and 31:10-31). Their Greek and English are Brenton's Proverbs set at
  the Ge'ez verse each answers by `Resources/Essenthos/versification/geez-verse-map.tsv`, the hand-read map the
  build places them by, read to the frame's rows; each Greek and English verse keeps its own address as its ref
  (`24:22f`). The export stops if a loaded Ge'ez verse has no line in the map, and names in
  `unanswered_by_the_verse_map` the verses whose rows hold no verse of Brenton's (Messale 11:4, 13:6, 15:33-34,
  16:1, 16:3, 20:10-12 and Tägsas 1:20 on 2026-11-16: Brenton's Greek lacks those lines).
- **2 Esdras** (69) is 4 Ezra, chapters 3-14, numbered as the King James numbers it; the packet carries the KJV's
  2 Esdras as its English and no Greek. The Ge'ez 7:35 also holds the passage the 1611 translation lacks (7:36-105
  of modern editions), which has no English beside it.
- **1 Enoch and Jubilees** (85, 86) carry R. H. Charles's translation from the Ethiopic (text `CHARLES1913`), a
  verse either side of the passage, since his verses divide a verse apart from the Ge'ez in places.
- **1-3 Meqabyan and 4 Baruch** have neither and go with the Ge'ez alone.

Those packets say where their English comes from in `english_from`.

The full contextual run is `export-gloss --all-chapters` into a new run folder (every GEEZ81 chapter in
GEEZ81's own book order, `book.position`: Jubilees and Enoch after Chronicles, Meqabyan after Esther; one
read statement per book), `build` with the settings the pilot chose, then `submit --next N` in tranches
(requests are `gl-<canonical ordinal>-<chapter>-<n>`, sent in the file's order).

`build --keys short` asks for one- and two-letter answer keys (`l` lemma, `ls` lemma source, `se`/`su`
sense, `e`/`u` line, `g` Greek, `mo` morph, `c` conf; `d`/`m` and `h`/`m`/`l` as values); the prompt names
each short key with its meaning once, and `collect` stores the long names, so the clean file is the same
either way. `--keys long` is v1's prompt byte for byte. `--out-per-word N` (default 400) and `--min-out`
(default 16000) set `max_tokens` = max(min, N × words): the reserve each submit holds against the cap is
that output in full, so a tighter allowance lets more requests into a tranche.
A submit stops before sending when the batch's upper bound would pass the cap (`BudgetExceeded`, nothing
sent); the next tranche resumes with the first request never sent.

Safety: `submit` refuses while a batch of the run is not collected, and refuses a request already sent
unless it is in `rejects.jsonl` (or `--force`). `collect` downloads a batch once (`raw/<batch>.jsonl`, the
ledger is written then), rejects an answer from any model but the run's, a cut-off or refused answer,
invalid JSON, and any answer that leaves out an item, answers one twice, names an unknown id or a Greek
word not in its packet; it rewrites `clean.jsonl` and `rejects.jsonl` from every raw file of the run.
A Ukrainian word with a Latin letter inside it is rejected too (Sonnet wrote "panувати" in 33 of 17,868
senses). `collect --fake <rows.jsonl>` runs the same checks on hand-written rows (`fixtures/`). A
request cut off at `max_tokens` (a long thinking run) is sent again with `submit --max-tokens 32000`.

Tests: `python -X utf8 scripts/geez-batch/test_geez_batch.py` (offline).

## How the results are loaded and shown

A finished run's checked lines (`clean.jsonl`) are taken into the build store by hand with
`forge geez-layers --gloss <run> --dillmann <run>`: `geez/glosses/<book>` (keyed by GEEZ81 word key, the
Greek by its own keys) and `geez/dillmann-renderings` (entry id and sense number). Every line must land on
the word or entry it names today, or nothing is written. They are `kept` products: out of git like the
Strong translations (the owner's word, NOT-0209), kept by the store and the backup, with their run, model,
prompt and day in each header and `geez/LICENCE.md` beside them. The build makes `tables/geez_word_gloss` and
`tables/geez_lexicon_rendering` from them. The reader shows a Ge'ez word's own line from the gloss, dotted and
said to be a model's, and falls back to the ≈ line through the aligned Greek where a word has none; the word
card shows Dillmann's Latin with the rendering in the reader's language under it, labelled as a machine
rendering (`/v1/geez/dillmann/{entry}`). Both layers are credited CC BY-NC-SA 4.0. A later tranche of the run
is taken in the same way, then `forge build` and `build --into`. Not built: a review list where the model's
lemma and the read-time Dillmann match disagree. Raw run folders go to the archive afterwards
(`scripts/archive_generation.py`, guide `generated-corpus-data`).
