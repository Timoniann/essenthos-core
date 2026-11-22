# CATSS: Aligned texts of Hebrew and Greek Jewish Scriptures

**What it is.** The parallel aligned text of the Masoretic Hebrew (BHS, Michigan-Claremont consonantal transcription) and
the Septuagint (Rahlfs, in the Thesaurus Linguae Graecae's betacode), word against word, compiled by the Computer
Assisted Tools for Septuagint Studies project (CATSS) under **Emanuel Tov** (Jerusalem) and **Robert A. Kraft**
(University of Pennsylvania).

**The copy used here, and the only one that may be used.** Oxford Text Archive (now the Literary and Linguistic Data
Service, University of Oxford), item 1111: "Aligned texts of Hebrew and Greek Jewish Scriptures : (CATSS database : TLG
format) / compiled by the Computer Assisted Tools for Septuagint Studies project under Emanuel Tov and Robert A. Kraft".

- Item page: https://llds.ling-phil.ox.ac.uk/llds/xmlui/handle/20.500.14106/1111 (`?show=full` for the record)
- Handle: https://hdl.handle.net/20.500.14106/1111 (submitted 2018-07-27; repository "Literary and Linguistic Data Service")
- Former address: https://ota.bodleian.ox.ac.uk/repository/xmlui/handle/20.500.12024/1111

**Licence, as the item page states it: Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0)**,
https://creativecommons.org/licenses/by-nc-sa/3.0/ — publicly downloadable, no user declaration. Read at the item page
on 2026-09-01 (DOC-0010) and again on 2026-11-22 (TSK-1940, `?show=full`). On 2026-11-22 the site put every page
behind a bot check (Anubis) that only a browser passes, so a program can no longer read the record or the files; the
handle record (read 2026-11-22) confirms the title, compilers and repository.

**Never CCAT's copy.** The same data is served by CCAT at the University of Pennsylvania
(ccat.sas.upenn.edu/gopher/text/religion/biblical/parallel/) under CCAT's user declaration, which requires every third
party supplied with the material to register a signed user agreement with CCAT: impossible for a public site. Take
only the Oxford bytes. Derivatives that descend from CCAT's copy (eliranwong/LXX-Rahlfs-1935, CenterBLC/LXX, the Greek
columns of MACULA Hebrew: PRB-1941) are not this copy either. CCAT's 1991 readme (`00.ReadReParallel.txt`) and Cody
Kingham's notes (github.com/codykingham/CATSS_parsers, `parallel_readme.md`) were read only as documentation of the
notation.

## Files (SHA-256, as measured; `scripts/fetch-catss.ps1` checks them)

| File | Bytes | SHA-256 |
|---|---|---|
| file01-1111.txt | 3,342,040 | 39b0eb8157acf509e1f7635095015e86ee110f13abfd429c9833f03f2a335cc8 |
| file02-1111.txt | 3,419,143 | 0dc6b00716de5b2b61a0cf2384243bef24d85b0349f7a0f55e57c826af22087a |
| file03-1111.txt | 3,123,143 | 57fcb29df38746d69f559c650ed5c3df3728a608f787c4b404bc6ae0df40be2b |
| file04-1111.txt | 3,327,096 | 3c923b7dde8bfe0076f4a6eb0d08fc216b0033e32b02c55d42ebd522e8797af2 |

13,211,422 bytes in all (the item page says 12.6 MB). These are the bytes of the TSK-1940 research download of
2026-11-22 from the item page; this agent's own download the same day was refused by the bot check. When the owner
downloads the four files in a browser, `scripts/fetch-catss.ps1 -From <folder>` says whether they are the same bytes.

## What the copy holds, and does not

Genesis to Deuteronomy, Joshua (B, with Joshua A's chapters apart), Judges A and B, 1-4 Kingdoms, 1-2 Chronicles, Ezra,
Nehemiah, 1 Esdras, Ruth, Song of Songs, Ecclesiastes, Lamentations, Isaiah to Malachi, Daniel in the Old Greek and in
Theodotion, Sirach and Baruch.

Not in it: **Psalms, Psalm 151, Proverbs and Job** (the archive's own list names a file it does not serve), and
**Esther**: the section headed `Esth` in file04 is a second copy of Ezra, byte for byte. Sirach, Baruch and 1 Esdras
have no Masoretic text in the corpus to join, and Judges A, Joshua A and the Old Greek of Daniel are not Brenton's text
(Brenton prints Judges B, Joshua B and Theodotion's Daniel), so none of those is read for GRCBRENT.

## Conditions on what is built from it

- **Non-commercial.** NC binds wherever the data or anything derived from it appears, aggregated or not (RUL-0183,
  NOT-0014): the site stays free.
- **ShareAlike on the derived link rows and their export.** The links the corpus draws from CATSS (GRCBRENT-BHSA,
  method stated-by-source) are an adaptation of this work and carry CC BY-NC-SA 3.0 (or a compatible later version)
  wherever they are published, an export included. They do not reach the texts beside them (a collection, not an
  adaptation: PRB-0344).
- **Tagged by source.** Every such row's source begins with "CATSS, the parallel aligned Hebrew and Greek texts (Tov
  and Kraft), Oxford Text Archive 1111" (`Sources.CatssCredit`), so an export can tell them apart and license them so.
- **Attribution** (RUL-0181): Emanuel Tov, Robert A. Kraft and the CATSS project; the Oxford Text Archive (Literary and
  Linguistic Data Service, University of Oxford) as the distributor; and the licence, with a link to it.

This is a reading of the licence, not legal advice.
