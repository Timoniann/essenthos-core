# STEPBible Strong-indexed lexicons

Fetched from <https://github.com/STEPBible/STEPBible-Data>, commit `ae39711d7843b2902d54993e432de9c12d6a4b9a`, on 2026-09-14.

| stored file | upstream dataset | status |
|---|---|---|
| `TBESG.txt` | Translators Brief lexicon of Extended Strongs for Greek | **loaded**: the Gloss column into `lexicon_gloss` (2026-09-20); the Morph column, as each Greek entry's part of speech and gender, into `strong_profile` (2026-10-29) |
| `TBESH.txt` | Translators Brief lexicon of Extended Strongs for Hebrew | **quarantined**, not loaded |
| `TFLSJ-0-5624.txt` | Translators Formatted full LSJ Bible lexicon 0–5624 | **loaded** (2026-11-22, TSK-1901): each LSJ article under its Strong number into `lexicon_article` |
| `TFLSJ-extra.txt` | Translators Formatted full LSJ Bible lexicon extra | candidate, not loaded |

## Terms read from the source

The repository README and the headers in all four files say **CC BY 4.0** and ask for credit to
STEP Bible / Tyndale House, Cambridge. `README-upstream.md` is the source README copied with
these bytes. The individual file headers also ask readers not to redistribute their copies, while
the README permits mirroring with a link back; the project records the more restrictive practical
condition by keeping the upstream URL and attribution beside every future derivative.

**TBESH has an additional, controlling warning.** Its header says its Abridged BDB-derived
definitions are "for guidance only" and that permission should be gained from Online Bible before
they are applied in any project. This copy is retained solely for evaluation. No TBESH definition,
meaning, or derived data may be loaded or published until that permission is obtained and recorded.

**What is loaded from TBESG, and on what terms.** The owner approved loading it on 2026-09-20
(FTR-0616). The one-word *Gloss* column is taken, with each entry's number and Greek form, and since
2026-10-29 (PRB-1124) the *Morph* column of each number's first entry, read as a part of speech and
gender for the Greek entry page, because Strong's Greek dictionary gives none; the Abbott-Smith
entry beside them is not. Every row carries the source string "STEPBible TBESG, the
Translators Brief lexicon of Extended Strongs for Greek, by Tyndale House, Cambridge, CC BY 4.0,
read from STEPBible/STEPBible-Data at ae39711", and the dataset is declared as `stepbible-tbesg`, so
the sources page credits STEP Bible / Tyndale House under CC BY 4.0 with a link to the repository.
The header re-read at the source on that day says "Data created by www.STEPBible.org based on work
at Tyndale House Cambridge (CC BY 4.0)" and asks that others be referred to github.com/STEPBible
rather than sent a copy: the glosses are served a word at a time, and a download of the corpus
should point there rather than carry the table.

**What is loaded from TFLSJ, and on what terms (2026-11-22).** The owner asked for LSJ on the Strong
pages on 2026-11-22 (TSK-1901). Each row's *LSJ Meaning* is taken under the Strong number of its first
column; rows whose meaning says "LSJ has no entry" (539, Abbott-Smith's article copied in) or "Not in
LSJ" (14, STEP's own note) are left out, and an article STEP repeats on several parts of one number is
kept once. The header, re-read on that day, says CC BY 4.0, and its own field description says the
full lexicon is "Liddell-Scott-Jones - from Perseus, with additional features and corrections by
Tyndale House". The Perseus Digital Library publishes its LSJ under **CC BY-SA 4.0** (github.com/PerseusDL/lexica,
read 2026-11-22: "Available under a Creative Commons Attribution-ShareAlike (CC BY-SA) license. Reuse must
credit the Perseus Digital Library"). Taking the more restrictive statement attached to the bytes
(RUL-0105), the articles are served under ShareAlike: dataset `stepbible-tflsj` credits Perseus and
STEP Bible / Tyndale House, licence CC BY-SA 4.0. Showing an article beside the corpus is a collection,
not an adaptation of anything else (RUL-0183), so the ShareAlike reaches the articles only.
`TFLSJ-extra.txt` (numbers above Strong's 5624, which no Strong page has) stays a candidate.

These files describe lemmas and Strong-compatible identifiers. They do not assert an alignment
between individual words in a translation and original-language words.
