# *.usfm — the Bible in Basic English, 1965

**The Bible in Basic English**, translated from the Hebrew and the Greek by a committee under
**Samuel Henry Hooke** (1874–1968) into C. K. Ogden's Basic English — 850 words, with a hundred more
for poetry and fifty for the Bible — and published whole by **Cambridge University Press** in 1965:
the New Testament of 1941 and the Old Testament of 1949 in one volume.

From <https://ebible.org/engBBE/>, read at the source on **2026-09-24**, when the owner approved it
for the corpus. The archive taken is `https://ebible.org/Scriptures/engBBE_usfm.zip` (2,853,316
bytes, last modified 2026-08-08), whose `sourceDate` in eBible's catalogue is **2018-08-29**;
`scripts/fetch-ebible.ps1 -Only BasicEnglish` is what fetches it.

**66 books, 1,189 chapters, 31,102 verse numbers of which 31,067 hold words, 840,285 words.** The 35
empty ones are the edition speaking: the same sixteen New Testament slots the American Standard
leaves empty where the critical Greek lacks the verse (Acts 8:37, Matthew 17:21, John 5:4 …), printed
here as `[]`; eighteen Old Testament verses printed as `...`, where the translators judged the Hebrew
too uncertain to put into Basic English (Job 34:29-33 and 36:16-20 among them); and 1 Samuel 13:1,
where the number of Saul's years is printed as `***`.

Its New Testament is the critical text's: no heavenly witnesses at 1 John 5:7 and no doxology to the
Lord's Prayer. The divine name is **the Lord**; *Yahweh* stands only where the Hebrew explains the
name (Exodus 6:2-8, Psalm 83:18) and in three place names (Genesis 22:14, Exodus 17:15, Judges 6:24).
The psalm superscriptions are `\d`, 138 of them.

## What arrives with it and is not loaded

**727,511 Strong tags**, `\w word|strong="H0430"\w*`. eBible names no tagger and no terms for them,
and they fail the same count the American Standard's and the World English Bible's fail: Genesis 1:1
tags *At*, *God* and *and* all with H0430. They are verse-level lists smeared across the English
words, not a claim that a word renders a lemma, so `StrongTagging.IsWordLevel` refuses them and the
reader loads none.

## The licence, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/engBBE/copyright.htm>:

> The Bible in Basic English
>
> **Public Domain**
>
> Language: English — Dialect: simple British — Translation by: Samuel Henry Hooke
>
> The Bible In Basic English was printed in 1965 by Cambridge Press in England. Published without
> any copyright notice and distributed in America, this work fell immediately and irretrievably into
> the Public Domain in the United States according to the UCC convention of that time. A call to
> Cambridge prior to placing this work in etext resulted in an admission of this fact.

**2. That page as served on the web**, fetched on 2026-09-24 and identical to the copy in the archive.

**3. eBible's catalogue**, row `engBBE`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`, `"downloadable"` = `True`,
> `swordName` = `engBBE1964eb`, `FCBHID` = `ENGBBE`

## What the statement covers, and what it does not

The public-domain claim is **about the United States**, and it rests on the missing notice rather than
on age: an American publication without notice before 1978 entered the public domain there. Hooke
died in 1968, so in the United Kingdom and anywhere counting life plus seventy the translation is in
copyright until 2038, and nobody has claimed otherwise. This corpus serves from a server and to
readers the owner chose to serve it to on the strength of eBible's three statements; that choice was
his, made on 2026-09-24, and this note is here so the basis of it is not forgotten.

## Attribution

Not required. Recorded per RUL-0181: the translation is S. H. Hooke's committee's, published by
Cambridge University Press in 1965, and this digital edition is eBible.org's.
