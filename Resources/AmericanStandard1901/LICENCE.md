# *.usfm — the American Standard Version, 1901

**The Holy Bible, American Standard Version**, the Standard American Edition of the Revised Version,
newly edited by the **American Revision Committee** and published by **Thomas Nelson & Sons** in
1901. The King James line re-based on the nineteenth-century critical Greek.

From <https://ebible.org/eng-asv/>, read at the source on **2026-09-06**. The archive taken is
`https://ebible.org/Scriptures/eng-asv_usfm.zip`, whose `sourceDate` in eBible's catalogue is
**2026-08-08**; `scripts/fetch-ebible.ps1 -Only AmericanStandard1901` is what fetches it.

**66 books, 1,189 chapters, 31,102 verse slots of which 31,086 hold words, 784,713 words.** Sixteen
slots stand empty, and every one of them is the critical text speaking rather than a short download:
Matthew 17:21, 18:11 and 23:14; Mark 7:16, 9:44, 9:46, 11:26 and 15:28; Luke 17:36 and 23:17; John
5:4; Acts 8:37, 15:34, 24:7 and 28:29; Romans 16:24. The heavenly witnesses of 1 John 5:7 are absent
and so is the doxology of the Lord's Prayer. It renders the divine name as **Jehovah** throughout.

The archive also carries a **title page** and the **American Revisers' preface** as USFM files under
the codes `FRT` and `INT`. They are not loaded — they are the publisher's apparatus rather than
scripture, and they use a dozen markers that appear nowhere in the text — but they are the strongest
provenance this file has, so what they say is quoted below.

## What is loaded from the annotation, and what is refused

**Loaded: 4,316 supplied spans.** `\add` marks the words the revisers supplied that their base text
does not have — the italics of the printed edition. That is the edition's own claim about its own
words and it reaches the same column the Reina-Valera's italics and the Synodal's square brackets
do.

**Refused: 705,378 Strong tags.** The file tags its words `\w word|strong="H…"\w*`, which in USFM
means *this word renders the original word with this number*. It does not mean that here, and the
layer is not loaded. What was measured, on 2026-09-06:

- Genesis 1:1 tags *In*, *God*, *and* and *earth* all with **H8064**, the Hebrew for *heavens*, and
  does not use H430 (God) anywhere in the verse.
- Across the Old Testament it puts **23.26 tags on an average verse drawn from a pool of only 8.44
  distinct numbers**. Luther 1912, whose tagging was made by hand, puts 10.73 tags from 9.53 distinct
  numbers on the same verses. So each Hebrew word's number is smeared over about three English
  tokens.
- **59.1% of the tags stand on an English function word.** *and* carries H1121 (*son*) 7,038 times;
  *the* carries H5921 (*upon*) 7,079 times. In Luther the figure is 1.8%.
- The verse-level pool is broadly right — 80.2% of these tags name a number Luther also uses
  somewhere in the same verse — and proper nouns land correctly (*Jehovah* → H3068 6,870 times out
  of 6,871). That is what makes the layer look usable until it is measured.
- The same layer is under eBible's World English Bible: their per-verse number sets agree 95.3% of
  the time, and where the two tag an identical sequence of words the sequence of numbers is
  identical 82.6% of the time.

So it is a verse-level list of the right numbers distributed over the English tokens, not a
word-level correspondence, and storing it would put an inference where a reader takes a sourced
claim to be — RUL-0024. eBible names no tagger and states no terms for it anywhere, which is the
same silence the Spanish tagging came under and where the layer turned out to be a named third
party's; here it fails on its merits before the question of whose it is arises.

## The licence, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/eng-asv/copyright.htm>:

> American Standard Version (1901)
>
> The American Standard Version of the Holy Bible, first published in 1901.
>
> **Public Domain**
>
> Language: English
>
> Dialect: Archaic American
>
> This public domain Bible translation is brought to you courtesy of eBible.org.

and in the footer:

> The American Standard Version of the Holy Bible is in the Public Domain. Copy freely.

**2. That page as served on the web**, fetched on 2026-09-06 and identical to the copy in the
archive.

**3. eBible's catalogue**, row `eng-asv`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`, `"downloadable"` = `True`

`swordName` = `engASV1901eb`, `FCBHID` = `ENGASV`, `PODISBN` = `978-1-5313-0210-8`.

**4. The file itself.** Its front matter reproduces the 1901 title page — "BEING THE VERSION SET
FORTH A.D. 1611, COMPARED WITH THE MOST ANCIENT AUTHORITIES AND REVISED A.D. 1881-1885, Newly Edited
by the American Revision Committee A.D. 1901, STANDARD EDITION", "Copyright, 1901, By THOMAS NELSON
& SONS" — and adds eBible's own note:

> This Bible translation is now in the PUBLIC DOMAIN (no longer copyrighted) due to copyright
> expiration.

That is four statements about the text and none about the tagging.

## Why the statement is believable

A 1901 copyright registered in the United States expired long before the 1929 line that settles the
question there, and every member of the American Revision Committee has been dead for more than a
century. CrossWire distributes the same edition as `ASV` with `DistributionLicense=Public Domain`,
and its module also carries `Feature=StrongsNumbers` — a *different* tagging from this one, which is
worth noting only so that a later pass does not read CrossWire's public-domain line as covering
eBible's layer or the reverse.

## Attribution

Not required. Recorded per RUL-0181: the translation is the American Revision Committee's, the 1901
edition Thomas Nelson & Sons', and this digital edition eBible.org's.
