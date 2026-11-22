# Brown-Driver-Briggs: the Open Scriptures Hebrew Lexicon

Fetched by `scripts/fetch-bdb.ps1` (action `fetch-bdb`) from <https://github.com/openscriptures/HebrewLexicon>, commit
`21c9add13bc727d3a951361778e97e3ff7afd1ce` (2019-09-02), on 2026-11-22.

| stored file | what it is | SHA-256 |
|---|---|---|
| `BrownDriverBriggs.xml` | Brown, Driver and Briggs, *A Hebrew and English Lexicon of the Old Testament* (1906), marked up article by article | `2b52658a4323d91674cda4090ab8b3ebddfff640f4f18143c28300e80b2c38f8` |
| `LexicalIndex.xml` | the index joining each BDB article to its Strong number and TWOT number | `8f7a605c58899d2f44430149c143c00903976e1e91232476677972a69e5bc85f` |
| `readme.md` | the repository's readme, which states the terms | `9a129c25674387c494571c3828aa3a8eb78459c165e275c313ae26994ce8ff22` |

## Terms read from the source

The readme, read at that commit on 2026-11-22:

> These files are released under the Creative Commons Attribution 4.0 International license. The actual text of Brown,
> Driver, Briggs and Strong's Hebrew dictionary remain in the public domain. For attribution purposes, credit the Open
> Scriptures Hebrew Bible Project.

The repository's `index.html` carries the same CC BY 4.0 badge and statement. The readme also says the TWOT numbers in
the index are "for reference purposes only"; they are not read.

**Served:** each BDB article under the Strong numbers `LexicalIndex.xml` files it at (`lexicon_article`, dictionary
`bdb`), credited to the Open Scriptures Hebrew Bible Project under CC BY 4.0 (dataset `bdb`).

## Not the Online Bible's abridged BDB

STEPBible's TBESH, quarantined in `STEPBibleLexicons/`, is built on the Online Bible's *Abridged* BDB, which needs the
Online Bible's permission. This is a different text: compared on 2026-11-22, the Open Scriptures articles follow the
printed BDB (its abbreviations, parts of speech, daggers, references and senses: `†[אֵ֫בֶה] n.[m.] reed, papyrus Jb 9:26`),
where TBESH's abridgement rewords them (`reed, papyrus`; for Abiathar, BDB "a priest, son of Ahimelech" against TBESH's
"priest, son of Ahitub (Ahimelech), faithful to David, but later rebelled with Adonijah").

## The edition is unfinished

The readme calls the BDB file "a work in progress", and each article states how far it has got (`status`):

| status | meaning (HebrewLexicon.pdf) | articles at this commit |
|---|---|---|
| `base` | basic definitions from BDB | 6,395 |
| `ref` | scripture references added, but incomplete | 2,312 |
| `done` | fully represents the BDB data | 560 |
| `made` | a cross-reference to the article holding the definition | 2,123 |
| `new` | a root with no Strong entry | 453 |
| `added` | a root added for BDB's internal references | 2 |

The status is kept on every served article, so the page can say when an article gives only the basic definitions.
