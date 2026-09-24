# *.usfm — Ohienko's Ukrainian Bible, as Ukrainian Wikisource transcribes it

**Біблія, або Книги Святого Письма Старого і Нового Заповіту**, translated by **Ivan Ohienko,
Metropolitan Ilarion** (1882–1972), proofread page by page against a scan of the **1988 printing**.

From <https://uk.wikisource.org/wiki/Біблія_(Огієнко)>, read at the source on **2026-09-20**.
`scripts/fetch-ohienko-wikisource.ps1` is what fetches it, through the MediaWiki API, re-reading
every statement below before it replaces anything. **66 books, 31,168 verses, 6.5 MB.**

## Why it is here

The Ohienko the corpus serves comes from bible4u, and that file belongs to a digitisation family
that lost text. Its Psalm 7 has seventeen verses where Ohienko printed eighteen, and the second —
*Господи, Боже мій, — я до Тебе вдаюся* — is in no verse of it. The families can be told apart by a
letter: every copy missing the line spells the psalm's title *Жал**і**бна … Ку**щ**а*, every complete
one spells it *Жал**о**бна … Ку**ш**а*. This copy spells it the second way and has the line.

**One verse is taken from it** — that one — and it is written into the end of the verse holding the
title, which is where every other psalm of this text keeps its first line. The stress marks the
transcription carries are dropped with it: they are an apparatus for saying a word aloud, and no
other Ukrainian word in this corpus has one.

**The ends of seven more verses are taken from it, on the owner's decision of 2026-09-24.** The same
file cuts them short: Genesis 22:19, 44:26 and 50:11, 2 Samuel 17:20, Job 2:2, Isaiah 50:9 and
Habakkuk 1:8 each stop where this copy goes on — Job 2:2 at *А сатана відповів Господеві й сказав:*,
with the answer missing. They were found by comparing every verse outside the Psalms: in these seven
and no others the loaded verse is the head of this one, the rest stands in no following verse of
the loaded file, and the King James reads the same sense there. The rest of each verse is appended
to the loaded one, written as bible4u writes Ohienko — without stress marks, quotation marks or
dashes, none of which that file prints — and with no Strong number and no link, since nothing states
what the words render. `Essenthos.Forge/Loading/LostVerseEndings.cs` lists them with the uk.wikisource
page each was read from.

## Why the whole text is *not* taken, which the numbers below are the answer to

Every verse of both was compared, 2026-09-20, over the 65 books outside the Psalms, where the two
use the same chapter numbers:

| | verses |
|---|---:|
| the same, once stress marks and curly quotes are normalised | 12,851 |
| the same words, different punctuation | 13,258 |
| the words differ | 2,401 |
| at an address the other edition does not use | 131 each way |

Three things make it a different edition rather than a better copy of ours, and each of them is a
piece of work rather than a swap:

1. **The Old Testament follows the Hebrew divisions, not the English ones.** 131 verses stand where
   the loaded text puts another — its Genesis ends chapter 31 at verse 54, its Exodus 8 begins where
   the English has 8:5, and Leviticus, Numbers and eleven more books do the same.
2. **The Psalms are numbered as the Septuagint numbers them.** 137 of the 150 pages carry the
   Masoretic number in brackets — `90 (91)` — one page, `113 (114, 115)`, is two psalms of the
   Masoretic text, and psalm 9 is the Masoretic 9 and 10 together. 2,527 verses against the loaded
   text's 2,461.
3. **The wording differs in 2,401 verses.** Most is capitalisation the two copies treat differently
   — *Край*/*край*, *Ангол*/*ангол* — but some is lexical: *правди* here is *праведности*, in the
   pre-1928 orthography, and *воскресення* is *воскресіння*. Those are two states of the
   translation, not one text scanned twice.

And 162 verses of this transcription carry a Latin letter where a Cyrillic one belongs — `I` for
`І`, 153 times — which is a defect of the transcription and would have to be repaired before it
could be loaded.

Swapping the loaded Ohienko for this one would also cascade away 595,497 words, 1,080,457 links and
33,641 entity annotations, and would need a versification for an edition the frame has no rules for.
That is a decision recorded on the board, not one taken here.

**Nothing else may be read out of these files by address.** The chapter numbers are the pages' own.
Psalm 7 is taken because both traditions call it 7; every other psalm would need the mapping first. The seven
verse endings are the one other exception, and each is taken only where the loaded verse is the head of
the one at the same address here — a verse the two numberings place differently would not begin with the
same words, and the loader stops rather than write it.

## The licence — CC BY-SA 4.0, and it has conditions

This is the one source in `Resources/` that is neither public domain nor permissive. Two statements
were read, and the script checks both before it writes anything.

**1. The scan, on Wikimedia Commons** — `File:Ivan Ohienko Bible.djvu`, which every page of the
transcription is proofread against:

> `LicenseShortName` = **CC BY-SA 4.0** · `UsageTerms` = *Creative Commons Attribution-Share Alike
> 4.0* · `AttributionRequired` = **true** · `LicenseUrl` = `https://creativecommons.org/licenses/by-sa/4.0`
>
> Categories include **Items with VRTS permission confirmed**

The translation is in copyright — Ohienko died in 1972 — so that permission ticket is what makes any
of this lawful. It is the same grant `Essenthos.Core/Loading/Bible4uTextSource.cs` already records
for the loaded text: released through Wikimedia VRT ticket 2013112610015211, covering printings
before 1991, which the 1988 printing is.

**2. Ukrainian Wikisource's own terms**, `action=query&meta=siteinfo&siprop=rightsinfo`:

> `text` = *Creative Commons Attribution-Share Alike 4.0* ·
> `url` = `https://creativecommons.org/licenses/by-sa/4.0/deed.uk`

That second one is over the **transcription**, which is a work of its own: the proofreading, the
verse anchors and the footnotes are Wikisource contributors', not Ohienko's.

### What it obliges, read against what this project does

- **Attribution and a licence notice.** Naming Ivan Ohienko, saying CC BY-SA 4.0 and linking it.
  The corpus does this on the text's own row and the interface renders it, so this is discharged for
  the translation. It is **not** discharged for the transcribers of the one restored verse: nothing
  on screen names Ukrainian Wikisource. Filed rather than fixed here.
- **Indicating a modification.** 4.0 requires that whoever passes the material on says it was
  changed. Our copy now is bible4u's file with one verse and the ends of seven restored from this one, so the loader
  writes that sentence onto the text's `rights_note`, where the interface shows it beside the
  licence.
- **ShareAlike.** It binds **Adapted Material** — a work based on this one and modified. Our copy of
  the *text* is modified, and it is already served under CC BY-SA 4.0, so that is satisfied. It does
  **not** reach the corpus that holds this text beside sixteen others, nor the links, annotations and
  descriptions this project makes: those sit alongside it rather than adapting it. That is the
  reading RUL-0183 records, and it is the reason this text could be kept at all.
- **No further restrictions.** Nothing here is served under terms narrower than BY-SA.

Nothing in the licence requires the project's own code or annotation to be licensed under BY-SA, and
nothing in it restricts the corpus's non-commercial or commercial use.

## Attribution

Translated by **Ivan Ohienko, Metropolitan Ilarion**, from the Hebrew and the Greek; begun in 1917,
the complete text finished in 1940 and first printed in London in 1962 by the British and Foreign
Bible Society. Scanned from the 1988 printing, Internet Archive identifier `BibleOhienko`, and
transcribed by the contributors to **Ukrainian Wikisource** at `Біблія (Огієнко)`, under
**CC BY-SA 4.0**. RUL-0181.
