# *.usfm — the Clementine Vulgate, as eBible publishes it

**Biblia Sacra Vulgatae Editionis**, Sixti V Pontificis Maximi iussu recognita et Clementis VIII
auctoritate edita — the Latin Bible in the edition Clement VIII issued in 1592, here in the 1598
reprint from **Migne's edition of 1880**. Loaded as the text `VULGCLEM`.

From <https://ebible.org/find/details.php?id=latVUC>, read at the source on **2026-09-25**. The archive
taken is `https://ebible.org/Scriptures/latVUC_usfm.zip` (3,549,517 bytes per DOC-0209), whose
`sourceDate` in eBible's catalogue is **2020-10-09**; `scripts/fetch-ebible.ps1 -Only VulgataClementina`
fetches it. **73 books, 1,334 chapters, 35,809 verses — 23,483 + 7,951 in the Testaments and 4,375 in
the seven deuterocanonical books — 9.4 MB.**

## What it is

The seventy-three books of the Catholic canon, numbered as the Vulgate numbers them, which the corpus
places by the versification data's Latin scheme. The Prayer of Manasseh and 3 and 4 Esdras, which
Clement's edition prints in an appendix, are not in the file.

| book | ordinal | chapters | verses |
|---|---:|---:|---:|
| Tobias | 70 | 14 | 298 |
| Judith | 71 | 16 | 346 |
| Sapientia | 75 | 19 | 439 |
| Ecclesiasticus | 72 | 51 | 1,592 |
| Baruch, with the Letter of Jeremiah as chapter 6 | 67 | 6 | 213 |
| I Machabæorum | 73 | 16 | 929 |
| II Machabæorum | 74 | 15 | 558 |

Esther runs to 16 chapters (275 verses) and Daniel to 14 (531 verses), as in the Douay-Rheims, which
was translated from this text. As there, Susanna and Bel (Daniel 13 and 14) are read as books of their
own, placed verse by verse by the versification data's Latin rule and keeping the numbers they are
printed under; see `Resources/DouayRheims1899/LICENCE.md`. So the text loads as 75 books.

## What is not taken

**The Glossa Ordinaria.** eBible's title for this package is *"Clementine Vulgate of 1598 with Glossa
Ordinaria Migne edition 1880"*: the medieval commentary Migne printed round the text is in the file as
13,775 footnotes. It is a commentary on the Vulgate and not the Vulgate, so it is not loaded; the
footnotes stay here in the files.

## The licence

**Public domain** by age, in all three of eBible's statements:

1. `copr.htm`, shipped in the archive and kept beside this file: *"Clementine Vulgate of 1598 with
   Glossa Ordinaria Migne edition 1880 in Latin — Public Domain — Translation by: Jerome"*.
2. eBible's catalogue: `Copyright` = `public domain`, `Redistributable` = `True`.
3. The copyright page on the web, <https://ebible.org/latVUC/copyright.htm>.
