# Clear Bible Alignments — complete `data-latest` snapshot

Downloaded from [Clear-Bible/Alignments](https://github.com/Clear-Bible/Alignments), release
[`data-latest`](https://github.com/Clear-Bible/Alignments/releases/tag/data-latest), on
2026-09-14. The eleven archives held here are:

`arb`, `asm`, `ben`, `eng`, `fra`, `hau`, `hin`, `legacy`, `por`, `rus`, `spa`.

## What the repository states about itself, measured on 2026-09-16

Three different kinds of claim, checked separately because RUL-0105 says they routinely disagree.
Each was requested again on **2026-09-16** rather than taken from the previous version of this file:

| checked | result |
|---|---|
| `LICENSE` at the repository root | **404** |
| `COPYING` at the repository root | **404** |
| `LICENSE.md` at the repository root | **200** — present on `main` continuously since 2023-05-12 |
| GitHub API `license` field | `{"key": "other", "name": "Other", "spdx_id": "NOASSERTION"}` |
| `README.md` | "All alignment data is licensed under a Creative Commons Attribution 4.0 International License"; code MIT, © 2024 Biblica, Inc. |
| `LICENSE.md` | code MIT, © 2023 Clear Bible, Inc.; data — "Bible Word Alignments © 2022 by Clear Bible, Inc is licensed under CC BY 4.0" |

**An earlier version of this file recorded that `LICENSE`, `LICENSE.md` and `COPYING` all 404 and
that the GitHub API reported `license: null`. Two thirds of that does not reproduce and should not
be repeated.** `LICENSE.md` exists and has since 2023-05-12 — the most recent upstream commit
touching that path is 2023-05-30 and the file is still there — and the API reports `NOASSERTION`,
which is GitHub saying it found a licence file it could not identify, not that none exists. What
survives of the old measurement, and it is the part that matters, is that **GitHub recognises no
licence at the repository level**: `NOASSERTION` is not a grant.

So the repository does make a blanket statement, in two places, and it says CC BY 4.0 over the data.
It is still not used as a blanket licence here, for the reason RUL-0105 gives rather than for want
of a statement: **the TOML file adjacent to every alignment is the record closest to the individual
bytes, it is the more restrictive where they differ, and they do differ.** `SBLGNT-ONAV-manual.toml`
declares `license = "CC-BY-SA-4.0"` on its alignment, its target and its target metadata, against a
repository README and a `LICENSE.md` that both say CC BY 4.0 over everything. Every TOML is retained
with the archive's extracted data.

## Inventory read from the TOML records

| language / target | source text | records | alignment terms | process | acquisition status |
|---|---|---:|---|---|---|
| Arabic AVD | SBLGNT, WLC | 2 | CC BY 4.0 | manual | candidate |
| Arabic ONAV | SBLGNT | 1 | **CC BY-SA 4.0** | manual | retained, excluded from loading |
| Assamese IRVAsm | SBLGNT | 1 | CC BY 4.0 | manual | candidate |
| Bengali IRVBen | SBLGNT | 1 | CC BY 4.0 | manual | candidate |
| English BSB | BGNT, SBLGNT, WLCM | 3 | CC BY 4.0 | manual | current calibration source |
| English YLT | SBLGNT, WLC | 2 | CC BY 4.0 | manual | candidate |
| French LSG | SBLGNT, WLCM | 2 | CC BY 4.0 | one `manual`, one process unstated | candidate; inspect before load |
| Hausa OHCB | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| Hindi IRVHin | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| Portuguese JFA11 | SBLGNT | 1 | CC BY 4.0 | **transfer from Spanish RVR09** | retained; never call it manual |
| Russian RUSSYN | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | blocked by token mismatch; see below |
| Spanish RV09 | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| `legacy` | sample config only | 1 | CC BY 4.0 | manual | format reference, not an alignment set |

`ONAV` is retained because the owner asked for the available sources to be acquired, but its
alignment and target declare CC BY-SA 4.0; RUL-0183 excludes it from corpus use. Source and target
licences are recorded separately in each TOML and may impose attribution or text-use conditions
even where the alignment itself is CC BY.

## Russian warning — before using the Russian set, read PRB-0185

Do not load `RUSSYN` from this release yet. Measured against its own target token file on
2026-09-03: 12,550 of 89,248 records name punctuation as the Russian word. The English BSB control
had zero such records in 171,172. The archive is evidence for an upstream repair, not mapping input
until its target tokenization matches the release's alignment records. What was decided about it is
on **PRB-0185**.

## Spanish RV09 — records that pair verses by number

In the chapters where the Spanish and the Hebrew number their verses differently, some of the
WLCM-RV09 records link a Spanish verse to the Hebrew verse that carries the same number, printed or
in the English count, rather than to the one it renders — while the release's own `ot_RV09.tsv`
names the right one in its `source_verse` column. Spanish Numbers 13:19 is listed against the
Hebrew 13:18 and its words are linked to the Hebrew 13:19; Daniel 6 and 4, Job 39-40, 1 Chronicles
21 and 2 Chronicles 33 are the worst. Measured on 2026-09-23: 725 of the 257,188 Old Testament
records, none of the New Testament's. The loader refuses a record only where the token file and the
canonical frame both put the verses apart and the record's Hebrew verse is the one numbered like the
Spanish; a Spanish verse that opens with the last words of the Hebrew verse before it keeps its
links (PRB-0694).

## Spanish RV09 — records on the *y* before the word that renders them

Some WLCM-RV09 and SBLGNT-RV09 records put a Hebrew or Greek word on the Spanish *y* that stands
before the word rendering it, and leave that word unaligned: Genesis 1:4's וַיַּרְא is on the *Y* of
*Y vió*, and *vió* is on nothing. The set almost never aligns the Hebrew ו itself (14 records), so
this is not the conjunction misread. Measured on 2026-09-24: 4,974 Old Testament and 163 New
Testament records have a lone *y*/*e* as their Spanish side, no source word that is a conjunction or
גַּם/אַף/καί, and an unaligned word after the *y*; in hand-checked samples of 140 and 21, none of the
*y* renders the source word. The loader refuses these records and does not move them: which word
after the *y* renders the source word is not stated by the record (PRB-0715).

## WLCM morphemes and BHSA words

The loader keys WLCM by morpheme, not by word: a record naming the prefix of *מֵעֵץ* is stored
against BHSA's separate מִן, not against עֵץ. It lays each verse's morphemes in the order their ids
number them (the file lists 781 verses out of that order) and compares a pronominal suffix as part
of its word, since BHSA never divides one (PRB-0716).

## What these data mean

An alignment is a publisher's claim relating word **occurrences** in one named translation to a
particular original-language source text. It may be imported only after its target text is matched
to a loaded corpus text at token level. It is not a general bilingual dictionary, and none of these
records authorizes an inferred link into a differently tokenized translation.

## Attribution

Retain the individual TOML copyright holder and attribution. The repository says all alignment
data is CC BY 4.0; each link imported from a set must identify the specific set and its stated
copyright holder rather than crediting “Clear Bible” generically.
