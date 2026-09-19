# *.usfm — the King James Version, standardised text of 1769, as eBible publishes it

**The Holy Bible, Authorized (King James) Version**, translated 1604–1611 by the six companies
appointed by James VI and I, in the standardised text the Oxford revision of 1769 settled. Sixty-six
books, protocanon only.

From <https://ebible.org/eng-kjv2006/>, read at the source on **2026-09-20**. eBible generated the
files on 17 September 2026 from source files of the same date, which is the `sourceDate` its
catalogue carries; the archive taken is `https://ebible.org/Scriptures/eng-kjv2006_usfm.zip`
(2,461,781 bytes) and `scripts/fetch-ebible.ps1` is what fetches it, re-reading every statement
below before it replaces anything.

**66 books, 31,102 verses — 23,145 in the Old Testament and 7,957 in the New — and 349,308 Strong
tags.** Numbering is the English one throughout, which is the whole reason this edition could be
used here at all: it is verse for verse the scheme the corpus is already aligned on.

## What is taken from it, and what is not

**Only the 116 psalm superscriptions.** The King James the corpus serves comes from bible4u, whose
Zefania XML has no element for a superscription and therefore prints none — *Psalm 51:1* there is
*Have mercy upon me*, with *To the chief Musician, A Psalm of David, when Nathan the prophet came
unto him* nowhere in the file. This edition writes all 116 of them as USFM `\d` lines, 1,034 words,
each word tagged with the Hebrew word it renders. Those lines, and nothing else, are read into the
head of the psalm's first verse, which is where the six other English texts here already carry
theirs.

**No text of its own is loaded.** The corpus holds one King James, under the slug `KJV`, with
789,806 words, 2,348,651 links, 123,762 Strong tags and 33,184 entity annotations hanging off them.
Loading this edition as a second King James would put two of them side by side; replacing the loaded
one with it would cascade all of that away. The edition is fetched whole because the whole of it is
the evidence for the paragraph below, not because the whole of it is wanted.

## What the comparison found, which is why this file is worth keeping

Every verse of both was rebuilt and compared, 2026-09-20. They agree on the address of all 31,102
verses: neither has one the other lacks.

| | verses |
|---|---:|
| the same, character for character | 22,154 |
| differ by the casing of the divine name alone (`LORD`, `GOD`) | 5,610 |
| differ by the pilcrow this edition prints and bible4u does not | 2,051 |
| differ by a hyphen inside a proper name — *Beth-el*, *Tubal-cain* | 652 |
| differ by punctuation, with or without casing | 246 |
| differ in the words themselves | 389 |

Of the last 389: 178 are a different spelling of the same word — this edition keeps *asswaged*,
*throughly*, *Judæa*, *Cæsar*, *their's*, where bible4u modernises them — 149 are hyphenation inside
a longer difference, and 62 are a word one edition has and the other has not. Twenty of those 62 are
Psalm 119's Hebrew-letter headings, which are a heading in this edition and not a verse, so they are
not a textual difference at all. Fourteen are the subscriptions the 1769 prints after the epistles —
*Written to the Romans from Corinthus, and sent by Phebe servant of the church at Cenchrea* — which
bible4u omits.

**The remaining twenty-eight are errors in the file the corpus serves.** They were read one by one
and in every one this edition carries the 1769 reading:

> *Am I am not an apostle?* for *Am I not an apostle?* (1 Corinthians 9:1)
> *Bezaleel the son Uri* for *the son of Uri* (Exodus 38:22)
> *Mordecai, who spoken good for the king* for *who had spoken good* (Esther 7:9)
> *if I have favour in his sight* for *if I have found favour* (Esther 8:5)
> *And I set my tabernacle among you* for *And I will set* (Leviticus 26:11)
> *a tumultuous city, joyous city* for *a joyous city* (Isaiah 22:2)

That is a finding about the loaded text, not about this one, and it is recorded on the board rather
than acted on here: swapping the edition is a decision about two and a half million links, and this
file is what a decision like that would be made from.

## The licence, and there are three statements of it that agree

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/eng-kjv2006/copyright.htm>:

> King James (Authorized) Version
>
> The King James Version or Authorized Version of the Holy Bible, using the standardized text of
> 1769, protocanon only, with Strong's numbers added.
>
> **Public Domain**
>
> Language: English — Dialect: archaic British
>
> This free text of the King James Version of the Holy Bible is brought to you courtesy of the
> Crosswire Bible Society and eBible.org.

**2. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `eng-kjv2006`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`

**3. The copyright page as served on the web**, fetched separately by the script and checked for the
same two words before anything is unpacked.

## The letters patent, which is a restriction and not a licence condition

The same page carries one qualification, and it is worth stating exactly because it is routinely
mis-stated:

> Letters patent issued by King James with no expiration date means that to print this translation
> in the United Kingdom or import printed copies into the UK, you need permission. Currently, the
> Cambridge University Press, the Oxford University Press, and Collins have the exclusive right to
> print this Bible translation in the UK. This royal decree has no effect outside of the UK, where
> this work is firmly in the Public Domain.

It is a Crown prerogative over **printing and importing printed copies inside the United Kingdom**.
It is not copyright, it grants nobody a right over an electronic text, and it imposes no condition
on redistribution. Nothing this project does is printing in the UK.

## The Strong tagging, which is not the text

349,308 of this edition's words carry a Strong number, and the terms over that layer are eBible's
own sentence: *brought to you courtesy of the Crosswire Bible Society*. CrossWire publishes its
`KJV` module — the same tagging, from the same lineage as the Zefania `KJV+` file this corpus
already reads its 123,762 King James Strong numbers from — as Public Domain. The tags on the 1,034
superscription words are loaded on that footing, and this paragraph is here so that a reader can see
it was a footing rather than an assumption. It is the distinction `Resources/ReinaValera1909` is
about, where eBible's public-domain line stood over a named third party's tagging and the tagging
was refused.

## Attribution, which public domain does not require

The translation is the work of the six companies at Westminster, Oxford and Cambridge, 1604–1611;
the text served is the Oxford standardisation of 1769, chiefly Benjamin Blayney's. The digital
edition is CrossWire Bible Society's, republished by eBible.org, generated with Haiola. RUL-0181.
