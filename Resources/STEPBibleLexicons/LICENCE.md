# STEPBible Strong-indexed lexicons

Fetched from <https://github.com/STEPBible/STEPBible-Data>, commit `ae39711d7843b2902d54993e432de9c12d6a4b9a`, on 2026-09-14.

| stored file | upstream dataset | status |
|---|---|---|
| `TBESG.txt` | Translators Brief lexicon of Extended Strongs for Greek | **loaded**, the Gloss column only, into `lexicon_gloss` (2026-09-20) |
| `TBESH.txt` | Translators Brief lexicon of Extended Strongs for Hebrew | **quarantined**, not loaded |
| `TFLSJ-0-5624.txt` | Translators Formatted full LSJ Bible lexicon 0–5624 | candidate, not loaded |
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
(FTR-0616). Only the one-word *Gloss* column is taken, with each entry's number and Greek form; the
Abbott-Smith entry beside it is not. Every row carries the source string "STEPBible TBESG, the
Translators Brief lexicon of Extended Strongs for Greek, by Tyndale House, Cambridge, CC BY 4.0,
read from STEPBible/STEPBible-Data at ae39711", and the dataset is declared as `stepbible-tbesg`, so
the sources page credits STEP Bible / Tyndale House under CC BY 4.0 with a link to the repository.
The header re-read at the source on that day says "Data created by www.STEPBible.org based on work
at Tyndale House Cambridge (CC BY 4.0)" and asks that others be referred to github.com/STEPBible
rather than sent a copy: the glosses are served a word at a time, and a download of the corpus
should point there rather than carry the table.

These files describe lemmas and Strong-compatible identifiers. They do not assert an alignment
between individual words in a translation and original-language words.
