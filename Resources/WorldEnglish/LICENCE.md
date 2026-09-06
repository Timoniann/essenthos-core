# *.usfm — the World English Bible, updated edition

**The World English Bible**, a revision of the American Standard Version of 1901 by **Michael Paul
Johnson** and the volunteers of **eBible.org**, with its New Testament conformed in places to the
**Byzantine Majority Text**. The only modern English translation in this corpus and the only English
text in it whose New Testament comes from the Byzantine tradition.

From <https://ebible.org/engwebp/>, read at the source on **2026-09-06**. The archive taken is
`https://ebible.org/Scriptures/engwebp_usfm.zip`, whose `sourceDate` in eBible's catalogue is
**2026-08-26**; `scripts/fetch-ebible.ps1 -Only WorldEnglish` is what fetches it.

**66 books, 1,189 chapters, 31,103 verse slots of which 31,098 hold words, 756,240 words.** The five
empty slots are Luke 17:36, Acts 8:37, Acts 15:34, Acts 24:7 and Romans 16:25.

## Which World English Bible this is, because there are five

eBible publishes `eng-web` (Classic, with the deuterocanon), `engwebu` (Updated, with the
deuterocanon), `engwebp` (Updated, protocanon only — this one), `engwebpb` (British spelling) and
`engwmb` (Messianic). They are not one text. Established by fetching the first three on 2026-09-06
and counting:

| package | *Yahweh* | *LORD* | files |
|---|---|---|---|
| `eng-web` Classic | 6,902 | 47 | 83 |
| `engwebu` Updated | 111 | 6,696 | 83 |
| `engwebp` (this) | 110 | 6,687 | 68 |

So this package is the **Updated** edition with the fifteen deuterocanonical books removed, and it
renders the divine name as **LORD** where the Classic renders it **Yahweh**. Its own preface says so
in as many words: *The Classic World English Bible translates God's Proper Name in the Old Testament
as "Yahweh." All other editions of the World English Bible translate the same name as "LORD".*

One caution for whoever fetches this next: the `copr.htm` inside the archive still carries the line
**"2020 stable text edition"**, while the catalogue row gives `swordName` = `engweb2025peb` and
`sourceDate` = 2026-08-26, and the web page footer says *"This is the updated World English Bible
with the 66-book protocanon only."* eBible's own statements about which edition this is do not
agree; the text is what settles it, and the counts above are how.

## Which Greek, read out of the file

The preface says the New Testament was *"updated in places to conform to the Byzantine Majority Text
reconstruction"*, and the file bears it out in a way that distinguishes the Byzantine text from both
the Textus Receptus and the critical text:

- **Acts 8:37 and the heavenly witnesses of 1 John 5:7 are absent** — the Textus Receptus prints
  both.
- **Matthew 17:21, John 5:4 and the doxology of the Lord's Prayer are present** — the critical text
  prints none of them.
- **The Romans doxology stands at 14:24–26**, where the Byzantine manuscripts put it, with 16:25
  left empty. That is the only place in the whole Bible where this text's verse addresses differ
  from the American Standard's.

The preface is also candid that this is a **revision of the American Standard Version**, updated by
a program and then proofread, so the two are not independent witnesses to each other.

## The Strong tagging is not loaded

The file carries **683,868** `\w …|strong="…"\w*` tags. They are not loaded, for the same reason and
on the same measurements as the American Standard's — see `Resources/AmericanStandard1901/LICENCE.md`
for the numbers. In short: it is a verse-level list of the right numbers spread over every English
token in the verse, 58.9% of the tags stand on English function words, and the layer under this text
and the one under the American Standard agree on 95.3% of per-verse number sets, so it is one
generated layer over both. eBible names no tagger and states no terms for it. RUL-0024.

What is loaded from the annotation is nothing at all: this file marks no supplied spans, and its
2,290 `\wj` spans and 1,232 footnotes are read and dropped as they are in every other text here.

## The licence, in three statements that agree — and one trademark condition

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/engwebp/copyright.htm>:

> The World English Bible is in the Public Domain. That means that it is not copyrighted. However,
> "World English Bible" is a Trademark of eBible.org.
>
> You may copy, publish, proclaim, distribute, redistribute, sell, give away, quote, memorize, read
> publicly, broadcast, transmit, share, back up, post on the Internet, print, reproduce, preach,
> teach from, and use the World English Bible as much as you want, and others may also do so. All we
> ask is that if you CHANGE the actual text of the World English Bible in any way, you not call the
> result the World English Bible any more. This is to avoid confusion, not to limit your freedom.
>
> The master copy of this Bible translation is posted on https://eBible.org/web/ and at
> https://WorldEnglish.Bible .

**2. That page as served on the web**, fetched on 2026-09-06 and identical to the copy in the
archive, with the footer adding: *"The World English Bible is in the Public Domain. You may copy and
share it freely."*

**3. eBible's catalogue**, row `engwebp`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`, `"downloadable"` = `True`

`swordName` = `engweb2025peb`, `FCBHID` = `ENGWEBP`, `PODISBN` = `978-1-5313-0226-9`.

**The qualification is a trademark and not a copyright**, and it binds a name rather than a use.
Tokenising the text and normalising its punctuation is a change to the actual text, so nothing this
corpus serves from these rows *is* the World English Bible — it is this corpus's reading of it, which
is what the row says of every text here. The requirement is met by not presenting our derived rows
as the translation itself.

## Why the statement is believable

Unlike every other text in this folder, this one is public domain by deliberate dedication rather
than by expiry, and the dedication is the reason the translation exists at all — its preface says
so: *"There are already many good translations ... Unfortunately, almost all of them are restricted
by copyright."* The dedicator is also the publisher, so there is no upstream party whose terms could
differ.

## Attribution

Not required. Recorded per RUL-0181: the translation is Michael Paul Johnson's and eBible.org's
volunteers', revising the American Standard Version of 1901; "World English Bible" is a trademark of
eBible.org.
