# *.usfm — the World English Bible Classic, with the deuterocanon, as eBible publishes it

**The World English Bible**, Classic edition, by **Michael Paul Johnson** and the volunteers of
eBible.org, with its fifteen deuterocanonical books. From <https://ebible.org/find/details.php?id=eng-web>,
read at the source on **2026-09-25**. The archive taken is `https://ebible.org/Scriptures/eng-web_usfm.zip`,
whose `sourceDate` in eBible's catalogue is **2026-09-22**; `scripts/fetch-ebible.ps1 -Only WorldEnglishClassic`
fetches it. **81 books, 38,058 verses: 23,145 + 7,958 in the Testaments and 6,955 in the deuterocanon,
19.0 MB.**

## Not the edition the corpus serves as WEB, and what follows from that

The corpus's `WEB` is the **updated** edition, protocanon only (`engwebp`, see
`Resources/WorldEnglish/LICENCE.md`). This package is the **Classic**, which prints the divine name as
*Yahweh* where the updated one prints *LORD*: compared verse by verse on 2026-09-25, **25,276 of the
31,103 verses of the sixty-six read the same**, and the rest differ mostly in the divine name and in
places in wording (Genesis 2:5 *in the earth* against *on the earth*). So its sixty-six are not read at
all.

**Only its deuterocanonical books are read**, into `WEB` as books that text gains: this is the only
World English deuterocanon the owner approved fetching (NOT-0197). None of the thirteen books taken
prints *Yahweh* or *LORD* as the divine name, so the one difference the two editions are known by does
not touch them; that they read word for word as the updated edition with the deuterocanon (`engwebu`)
prints them is likely and **not established** — `engwebu` was not fetched. Taking it instead would
settle it.

| book | ordinal | chapters | verses |
|---|---:|---:|---:|
| Tobit | 70 | 14 | 244 |
| Judith | 71 | 16 | 339 |
| Wisdom of Solomon | 75 | 19 | 436 |
| Sirach | 72 | 51 | 1,383 |
| Baruch, with the Letter of Jeremy as chapter 6 | 67 | 6 | 213 |
| 1 Maccabees | 73 | 16 | 924 |
| 2 Maccabees | 74 | 15 | 555 |
| 1 Esdras | 68 | 9 | 448 |
| Prayer of Manasseh | 79 | 1 | 15 |
| Psalm 151 | 82 | 1 | 7 |
| 3 Maccabees | 80 | 7 | 228 |
| 2 Esdras | 69 | 16 | 944 |
| 4 Maccabees | 81 | 18 | 484 |

**Thirteen books, 6,220 verses**, standing after Malachi. Its 2 Esdras prints the seventy verses found
in the Latin in the nineteenth century, so its chapter 7 runs to 140 where the King James's and the
Synodal's run to 70. Sirach's prologue is printed as an introduction and is not read. Its Strong tags
are not read, as for every English text here.

**Not taken:** *Esther (Greek)* (10 chapters, 205 verses) and *Daniel (Greek)* (14 chapters, 530
verses). They are whole books repeating the Hebrew Esther and Daniel the corpus already serves from the
updated edition, and a text holds one book at an ordinal.

## The licence

**Public domain**, dedicated to it deliberately, in all three of eBible's statements:

1. `copr.htm`, shipped in the archive and kept beside this file: *"The World English Bible is in the
   Public Domain. That means that it is not copyrighted. However, "World English Bible" is a Trademark
   of eBible.org."* — with the request that a changed text not be called the World English Bible.
2. eBible's catalogue: `Copyright` = `public domain`, `Redistributable` = `True`.
3. The copyright page on the web, <https://ebible.org/eng-web/copyright.htm>.
