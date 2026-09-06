# *.usfm — the Geneva Bible, 1599

**The Bible and Holy Scriptures conteyned in the Olde and Newe Testament**, made at Geneva by the
Marian exiles — **William Whittingham** (c. 1524–1579), **Anthony Gilby** (c. 1510–1585) and others
— from the Hebrew and the Greek. The English Bible of England and Scotland for two generations
before the King James, and the one the Pilgrims carried to Massachusetts.

From <https://ebible.org/enggnv/>, read at the source on **2026-09-06**. The archive taken is
`https://ebible.org/Scriptures/enggnv_usfm.zip`, whose `sourceDate` in eBible's catalogue is
**2024-03-16**; `scripts/fetch-ebible.ps1 -Only Geneva1599` is what fetches it.

**66 books, 1,189 chapters, 31,090 verses, 783,932 words** — 23,137 in the Old Testament and 7,953
in the New, which is what eBible's catalogue states and what the files hold. That is 66 verse
addresses fewer than the King James has, and it is the edition's own division rather than anything
missing: the Geneva keeps Hebrew seams the King James later smoothed, printing Daniel 3 to 33 verses
where the English numbering opens chapter 4, Job 39 to 38 verses, Ecclesiastes 4 to 17 and Hosea 14
to 10.

**The spelling is the original and has not been modernised**, which eBible's copyright page says in
as many words and the text confirms. Genesis 3:7 ends *and made them selues breeches* — the reading
the edition is nicknamed for, and one no other English version has — in a verse that also writes
*knewe* and *figge tree leaues*.

The marginal notes, which are the other half of what the Geneva was famous and hated for, are **not**
in this file. That is the edition eBible publishes and not a loss in transit; what is here is the
translation.

The file carries **no annotation of any kind** and uses nine USFM markers, all already known to the
reader.

## The licence, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/enggnv/copyright.htm>:

> Geneva Bible 1599
>
> The Geneva Bible in Old English of 1599
>
> **Public Domain**
>
> Language: English
>
> Dialect: Old
>
> This digital copy is freely available world-wide, with no copyright restrictions, courtesy of
> eBible.org and many others. Note that the spelling used is the original spelling, which is not
> modern English.

**2. That page as served on the web**, fetched on 2026-09-06 and identical to the copy in the
archive.

**3. eBible's catalogue**, row `enggnv`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`, `"downloadable"` = `True`

No condition of any kind and no rights holder named. `swordName` = `enggnv1599eb`,
`FCBHID` = `ENGGNV`.

## Why the statement is believable

The translators were dead by the seventeenth century and the 1599 printing is four hundred years
old, so the only live question is the transcription, and eBible asserts that on its own behalf —
"courtesy of eBible.org and many others", with nobody named as holding anything.

**CrossWire's `geneva` module is not this text.** Its configuration reads `ModDrv=zCom` and
`Description=Geneva Bible Translation Notes`: it is the marginal commentary, not the Bible, and it
carries **no `DistributionLicense` line at all**. A missing licence field is unknown and never
permissive. CrossWire's Bible is `Geneva1599`. Recorded here so nobody takes the wrong one.

## Attribution

Not required. Recorded per RUL-0181: the translation is the Geneva exiles' and this digital edition
is eBible.org's.
