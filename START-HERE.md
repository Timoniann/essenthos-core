# essenthos-core — start here

This project exists because the old schema says every translated word points at *the* original word, and there is no such thing.

Read this file, then the four documents it names, then start with **TSK-0007**. Nothing here is a suggestion: every number in it was measured this week against the running database, and every design claim has a document behind it.

## What you are building

A Bible research platform whose claim is that its comparisons can be cited. Two features define the product: **word-level comparison of translations**, and **entity pages** for the people and places the text names. `avioniq show NOT-0004` is the owner's own statement of it.

What is new here is the foundation underneath the first of those. Instead of *original text* and *translation*, there are **texts** — BHSA, Nestle 1904, the Textus Receptus, the Septuagint, the King James, the Synodal, all one kind of thing — and **links** between the words of one text and the words of another.

## Why the old one had to be replaced

Measured, not asserted:

    select count(*) from original_books;                       -- 66, one slot per canonical book
    select language_iso, count(distinct book_id) from original_words group by 1;
    -- arc 5 · GR 27 · hbo 39

Exactly sixty-six original books, and a corpus identified only by the language on its words. The Septuagint is Greek, which collides with Nestle, and it is the Old Testament, which collides with BHSA's book rows. **It cannot be loaded.** Neither can the Textus Receptus, nor the Samaritan Pentateuch.

Meanwhile `Endpoints/Corpora.cs:68` in the old project reads `Originals = [Hebrew, Greek]` — the API had already invented the abstraction the database lacked, and hardcoded two instances of it.

## The four documents

Read them in this order. They are in avioniq: `avioniq show DOC-0005`.

- **DOC-0005** — why there are witnesses rather than originals, and why a text's role belongs to its relationships rather than to the text.
- **DOC-0006** — why a shared word identifier across texts cannot work. This one argues against the owner's first proposal and he accepted the argument; do not re-propose it. The short version: correspondence is not transitive, so splits and merges collapse the equivalence classes, and the collapse is unrecoverable because the claim is stored *as* the identifier.
- **DOC-0007** — the schema. Tables, columns, keys, indexes. This is the specification.
- **DOC-0008** — what it changes for the reader, and therefore for essenthos-web.

Also worth reading before you touch a loader: **DOC-0004** (what LLM alignment costs, measured — the conclusion is that generation is affordable and verification must be SQL), and **DOC-0003** (Strong-tagged Russian and Ukrainian texts: what exists).

## The rules

`avioniq rules list`, and they are binding. Four decide most of what you will do here:

- **RUL-0024** — never persist or serve a guess as though it were sourced. This project's whole claim rests on it, and the link's `method` and `confidence` columns exist to satisfy it.
- **RUL-0027** — the owner is obsessive about performance, and at this scale he is right to be. Project into the shape you return, one query rather than one per row, index what you filter on, measure before and after.
- **RUL-0028** — comments explain the code, never the change and never a ticket id. Do not write PRB-0063 in a comment; write what the ticket would have told the reader.
- **RUL-0019** — **essenthos-core is reviewed: do not commit without the owner's permission.** essenthos-web you may commit freely. And close work to `review`, never to `fixed` or `done` (RUL-0010).

## The state you are starting from

**The folder** is a copy of essenthos-api with build output and git history stripped and its own git repository initialised. `Resources/` is its own again — see below — and `.gitignore` keeps the gigabyte of corpus out of every commit while admitting the licence beside each folder and `Resources/WorldHistory`.

**The database** `essenthos_core` exists on the same Postgres container as the old one, owned by `essenthos`, with `pg_trgm` enabled. It is empty. The old database is untouched and still serving the old API.

    docker exec essenthos-api-db-1 psql -U essenthos -d essenthos_core -c "..."

**Ports:** the old API holds 5277 and the web client 5278. Take **5279**.

**Nothing in `Resources/` is committed except the licences and `WorldHistory`,** so a fresh clone has the folder and not the corpus. GLAUx, the Berean and ClearBible have fetch scripts under `scripts/`; the rest were fetched by hand years ago and live only on this machine and in `essenthos-api/Resources`, which is where a new checkout should copy them from until each one has a script of its own.

**Resources are this project's own.** They live in `essenthos-core/Resources` and are read through configuration — `Dataset:ResourcesPath`, defaulting to `../Resources`, resolved from the content root, which is the project folder `essenthos-core/Essenthos.Core`. Nothing here reads `essenthos-api` any more; a bare `dotnet test` needs no environment variable.

`essenthos-api/Resources` still holds its own copy and the frozen API still runs on it. The two are now separate trees: a correction or a licence note made here does not reach it, which is the point.

**In a git worktree** the corpus is not there — it is ignored, so a worktree starts with only `Resources/WorldHistory`. Junction the rest to the main checkout rather than copying a gigabyte per worktree:

```powershell
$src = "..\..\essenthos-core\Resources"
Get-ChildItem -Directory $src | Where-Object Name -ne 'WorldHistory' | ForEach-Object {
    New-Item -ItemType Junction -Path (Join-Path 'Resources' $_.Name) -Value $_.FullName
}
```

## What carries over, and what does not

**Carry unchanged** — they read sources and know nothing about the schema: `TextFabric/`, `Bhsa/`, `Nestle/`, `Zefania/`, `XmlBible/`, `Csv/`, `Strong/`, `Utils/`.

They also carry three repairs made on 2026-08-28, each of which fixed a real corruption, so do not "simplify" them:

- the Nestle tokeniser dropped the final letter of every Greek word followed by punctuation — 19,740 words, and in Greek that letter is the case ending;
- trailers lost the space after punctuation in 72,277 words, from two unrelated causes in two loaders;
- diacritic folding has to happen in Postgres, because the build uses `InvariantGlobalization` under which `String.Normalize` silently does nothing.

**Rewrite** — they wrote the old shape: the fillers and the two mapping services. Their logic is the reference, particularly the KJV-to-BHS file reader and the Strong-number matching. What they write changes.

**Adapt** — the `/v1` endpoints and DOC-0002. The shapes were built for two corpora and generalise.

**Do not carry** the suppression list in `OriginalGreekMappingService`. It silences fifty-five English words and four Matthew passages; the passages are Textus Receptus expansions rather than mapping failures, the checks carry no book so they silence fifteen unintended verses in fourteen other books, and the word list hides 1,144 of 4,057 unmapped words.

## The order of work

`avioniq tree MLS-0009` shows it. TSK-0007 through TSK-0017, chained, each blocking the next:

1. **TSK-0007** — stand the project up: rename, own database, own port, resources by configuration.
2. **TSK-0008** — the schema, and nothing else. Write the tests for two-to-one, one-to-none, none-to-one and a cross-verse link *before* any loader. If the schema cannot hold one of those it is wrong, and that is the cheapest hour of the project in which to discover it.
3. **TSK-0009** — parsers over, plus a round-trip assertion on every load.
4. **TSK-0010** — BHSA and Nestle as texts.
5. **TSK-0011** — the canonical reference frame, from imported versification data.
6. **TSK-0012** — Old Testament links from the mapping file, fixing the prefix defect rather than reproducing it.
7. **TSK-0013** — New Testament links, every one labelled with how it was made.
8. **TSK-0014** — the verification pass.
9. **TSK-0015** — the read API.
10. **TSK-0016** — the Textus Receptus, which is the proof the model works.
11. **TSK-0017** — the Synodal brackets.

## Numbers you will want, measured against the old database

| | |
|---|---|
| original words | 564,369 — BHSA 426,590, Nestle 137,779 |
| translation words | KJV 787,737 · Synodal 566,244 · Ukrainian 600,498 |
| verses | 31,101 |
| KJV words linked to an original | 402,232 — 282,052 stated by a source, 120,180 inferred |
| Greek words claimed by more than one English word | **995**, against **30** in the whole Old Testament |
| KJV words carrying a Strong number and linked to nothing | 4,057 |
| lexical Hebrew words unreached, in a 65-verse sample | 50 of 599 |
| two-hop composition over a chapter, old array column | **429.8 ms** |
| the same, over a normalised link table | **2.8 ms** |
| three-hop chain over the link table | **2.3 ms** |

That last pair is the measurement the whole design rests on: composition through a hub is cheap, and the array was the expensive thing.

## How to know you are done

The milestone's condition: two ancient witnesses and two translations loaded, any pair readable side by side, every word-level correspondence carrying its relation, method and provenance, every absence saying which of its three kinds it is, and the verification pass reporting coverage, reach and contention on every load.

The real test is simpler. **Add a fifth text and see whether the schema had to change.** The current design answers that with no, which is why it is being replaced.

## Two habits that will save you

**Disbelieve numbers on screens.** Every serious defect found this week — the Greek text missing its case endings, the split view pairing the wrong passages, the land of Canaan filed as a person — was found by looking at output, doubting it, and checking it against the database and then against the source file. None was found by reading code, and none would have been caught by the tests.

**File it, do not narrate it.** Findings go into avioniq with a location and the query that established them. Your reply is thrown away; the store is not.
