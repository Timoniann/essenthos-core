# *.usfm — the King James Version with its Apocrypha, as eBible publishes it

**The Holy Bible, Authorized (King James) Version**, the standardised text of 1769, **with the
Apocrypha** the 1611 printed between the Testaments. From <https://ebible.org/find/details.php?id=eng-kjv>,
read at the source on **2026-09-25**. The archive taken is `https://ebible.org/Scriptures/eng-kjv_usfm.zip`,
whose `sourceDate` in eBible's catalogue is **2026-09-17**; `scripts/fetch-ebible.ps1 -Only KingJamesApocrypha`
fetches it. **80 books, 36,822 verses: 23,145 + 7,957 in the Testaments and 5,720 in the Apocrypha,
12.7 MB.**

## What is taken: the Apocrypha, into the King James already loaded

**Only the Apocrypha is read**, and it is written into the text `KJV` as books that text gains, not
loaded as a second King James. That rests on its being the same edition, which was checked: the
sixty-six books of this archive are verse for verse the sixty-six of eBible's `eng-kjv2006`, all
31,102 verses reading the same once markup is set aside (compared 2026-09-25), and
`Resources/KingJames2006/LICENCE.md` sets out the comparison of that edition with the loaded bible4u
file — the same 1769 text transcribed twice, differing in casing, pilcrows, hyphens and a few
spellings, and in twenty-eight errors of the loaded file.

| book | ordinal | chapters | verses |
|---|---:|---:|---:|
| 1 Esdras | 68 | 9 | 448 |
| 2 Esdras | 69 | 16 | 874 |
| Tobit | 70 | 14 | 244 |
| Judith | 71 | 16 | 339 |
| Wisdom of Solomon | 75 | 19 | 436 |
| Sirach (Ecclesiasticus) | 72 | 51 | 1,393 |
| Baruch, with the Epistle of Jeremy as chapter 6 | 67 | 6 | 213 |
| Susanna | 77 | 1 | 64 |
| Bel and the Dragon | 78 | 1 | 42 |
| Prayer of Manasses | 79 | 1 | 15 |
| 1 Maccabees | 73 | 16 | 924 |
| 2 Maccabees | 74 | 15 | 555 |

**Twelve books, 5,547 verses.** They stand after Malachi, as the 1611 printed them. The King James's
1 Esdras is the Greek book and its 2 Esdras the Latin apocalypse, the same two the Synodal calls its
second and third books of Ezra. Sirach's two prologues have no verse number and stand at the head of
1:1. No word of the Apocrypha carries a Strong number in this file, and none of the Testaments' are read.

**Not taken:** the *Rest of Esther* (7 chapters, 105 verses, numbered 10:4–16:24) and the *Song of the
Three Children* (68 verses). They are additions to Esther and Daniel, which the corpus already holds
from bible4u as the Hebrew books; the model gives an addition no book of its own, and a loaded book
is not written into.

## The licence

**Public domain outside the United Kingdom**, in all three of eBible's statements:

1. `copr.htm`, shipped in the archive and kept beside this file: *"The King James Version or Authorized
   Version of the Holy Bible, using the standardized text of 1769, with Apocrypha/Deuterocanon —
   Public Domain"*, and: *"Letters patent issued by King James with no expiration date means that to
   print this translation in the United Kingdom or import printed copies into the UK, you need
   permission. […] This royal decree has no effect outside of the UK, where this work is firmly in
   the Public Domain."*
2. eBible's catalogue: `Copyright` = `public domain`, `Redistributable` = `True`.
3. The copyright page on the web, <https://ebible.org/eng-kjv/copyright.htm>.

The Crown's patent is a right to print in the United Kingdom, not a copyright; it is recorded on the
King James's row, as it is for the sixty-six.
