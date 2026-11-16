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

## How the results would be loaded and shown (not built yet)

The checked lines of a finished run are committed as record files the build reads, never written to the
database alone: the Dillmann renderings as one file keyed by Dillmann's entry id and sense number beside
`geez_lexicon_entry`, the contextual glosses as one file per book keyed by GEEZ81 word id. The reader
would show a Ge'ez word's own gloss line from the contextual file, marked as made by a model (credited
with model, date and run, CC BY-NC-SA like the Dillmann layer it builds on), and fall back to today's
≈ line through the aligned Greek where a word has none; the word card would show Dillmann's Latin with
the rendering in the reader's language under it, labelled as a machine rendering of his Latin. Where the
model's lemma and the read-time Dillmann match disagree, the word goes on a review list rather than
either one being silently preferred. Raw run folders go to the archive afterwards
(`scripts/archive_generation.py`, guide `generated-corpus-data`).
