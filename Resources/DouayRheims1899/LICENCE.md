# *.usfm — the Douay-Rheims Bible, 1899 American edition, as eBible publishes it

**The Holy Bible, translated from the Latin Vulgate**, by the English College at Douai and Rheims
(New Testament 1582, Old Testament 1609–1610), in **Bishop Richard Challoner's** revision of 1749–1752,
as the John Murphy Company printed it at Baltimore in **1899**. Loaded as the text `DRA`.

From <https://ebible.org/find/details.php?id=engDRA>, read at the source on **2026-09-25**. The archive
taken is `https://ebible.org/Scriptures/engDRA_usfm.zip` (2,937,906 bytes per DOC-0209), whose
`sourceDate` in eBible's catalogue is **2022-11-03**; `scripts/fetch-ebible.ps1 -Only DouayRheims1899`
fetches it. **73 books, 1,334 chapters, 35,811 verses — 23,487 + 7,951 in the Testaments and 4,373 in
the seven deuterocanonical books — 17.6 MB;** loaded as 75, with Susanna and Bel apart from Daniel.

## What it is

It translates the Vulgate, so it is numbered as the Vulgate is and the corpus places it by the
versification data's Latin scheme: the Psalms in the Greek numbering, Esther with the Greek additions
gathered at its end in chapters 11–16 (16 chapters, 275 verses), Daniel with the Song of the Three at
3:24–90 (14 chapters, 531 verses as printed), and 1 and 2 Esdras for Ezra and Nehemiah.

**Susanna and Bel are read as books of their own**, as the owner decided on TSK-0405 and as Brenton and
Swete print them, though the file prints them as Daniel 13 and 14. Where each verse goes is the
versification data's Latin rule: 13:1–64 is Susanna 1–64, 13:65 is Bel 1, 14:1–40 is Bel 2–41, and
14:41 and 14:42 are the two halves of Bel 42, the second lettered `a`. Every verse keeps the number it is
printed under as the edition's own, so the reader can say *printed as Daniel 13:5*. Daniel is left with
its twelve chapters. The Song of the Three and Esther 11–16 have no book of their own in the corpus and
stay where the Vulgate prints them. The seven further books stand where the Vulgate puts them:

| book | ordinal | chapters | verses |
|---|---:|---:|---:|
| Tobit | 70 | 14 | 298 |
| Judith | 71 | 16 | 345 |
| Wisdom | 75 | 19 | 439 |
| Sirach (Ecclesiasticus) | 72 | 51 | 1,591 |
| Baruch, with the Letter of Jeremiah as chapter 6 | 67 | 6 | 213 |
| 1 Maccabees | 73 | 16 | 929 |
| 2 Maccabees | 74 | 15 | 558 |

Tobit and Judith are Jerome's own translations and differ from the Greek in length and division, and
Sirach is the longer Latin text; the Maccabees are the Old Latin and are divided as the Greek is but in
six chapters, which the versification data's Latin rules place.

## What is not taken

The file tags 644,946 words with Strong numbers that eBible names nobody for, and they are not
word-level: Genesis 2:24 gives *and* and *shall* the number for *man* (H0376). They are a verse's
numbers spread over its words and are not loaded.

## The licence

**Public domain**, in all three of eBible's statements:

1. `copr.htm`, shipped in the archive and kept beside this file: *"The Holy Bible in English,
   Douay-Rheims American Edition of 1899, translated from the Latin Vulgate — Public Domain"*.
2. eBible's catalogue: `Copyright` = `public domain`, `Redistributable` = `True`.
3. The copyright page on the web, <https://ebible.org/engDRA/copyright.htm>.

Challoner died in 1781 and the 1899 printing is well over a century old.
