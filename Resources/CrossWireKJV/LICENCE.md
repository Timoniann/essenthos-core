# KJV — CrossWire's King James Version 3.1 (SWORD module)

**King James Version (1769) with Strongs Numbers and Morphology**, CrossWire Bible Society's KJV2003
project, as the SWORD module `KJV`, version 3.1 (SwordVersionDate 2023-07-19). Fetched by
`scripts/fetch-crosswire-kjv.ps1` into `KJV/`, with the CrossWire wiki's page on it beside it.

| file | URL | version / date | sha256 |
|---|---|---|---|
| KJV.zip (the module) | https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/KJV.zip | Last-Modified 2023-07-27, 4,026,621 bytes | 873815aa4b4123025616d1f41eae75f412111275f4c3884e36f92d4f46dcba1d |
| mods.d/kjv.conf (inside it) | https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/kjv.conf | Version=3.1 | a9d97ba99e04722fe516327ca5fbda13498b554ecb972b11007467323d71608c |
| kjv.osis.xml (the conf's TextSource) | gitlab.com/crosswire-bible-society/kjv @ d490be7e3476, "KJV Version=3.1 released on 26 July 2023" | 28,043,075 bytes | 4bcbfe45722fca396adbeac9c81025bbb173cb1e30296da90864f0400cb0e3f5 |
| crosswire-kjv-wiki.html | https://wiki.crosswire.org/CrossWire_KJV | read 2026-11-13 | — |

Read at the source on 2026-11-13 (PRB-1332). The module and the OSIS hold the same data: every one of
the 227,196 Old Testament and 128,654 New Testament `<w>` elements has the same `src`, `lemma` and
English in both.

## The grant

`kjv.conf`, its `About`:

> It is in this spirit that we in turn offer the KJV2003 Project text freely for any purpose.

> Any copyright that might be obtained for this effort is held by CrossWire Bible Society © 2003-2023
> and CrossWire Bible Society hereby grants a general public license to use this text for any purpose.

and `DistributionLicense=GPL`. The repository has no LICENSE file, the OSIS header no `<rights>`, and
the GitLab project declares no licence.

The wiki, section Copyright:

> CrossWire's KJV module is an amalgamation of different source material. Each has its own copyright or
> is in the public domain.

> The Strong's numbers in the OT are from The Bible Foundation.

> The Strong's numbers in the NT are from Dr Maurice A. Robinson

> The tagging of Strong's numbers to the NT text is a unique effort of our KJV2003 project – Copyright
> © 2003-2023 The CrossWire Bible Society.

and, under Distribution License, that the old key "General public license for distribution for any
purpose" "did not match any of the defined options", so "Modules version 3.1 and thereafter now has
DistributionLicense=GPL".

## Whose work is what

- **The New Testament's word positions and the tagging** — which English words render which Greek
  word — are the KJV2003 project's own work, held by CrossWire and granted "for any purpose".
- **The Greek forms and Strong numbers beside them** are Maurice A. Robinson's Received Text, the
  same source the corpus's TR1550 and TR1894 come from ("Public Domain. Copy freely.").
- **The Old Testament's numbers** are the Bible Foundation's, which states no terms for its tagging.
  The corpus takes the Old Testament's numbers from eBible's eng-kjv2006 (`KingJames2006`), the same
  tagging, under eBible's Public Domain statement; nothing of this module's Old Testament is read.

## What was decided

Owner, 2026-11-13 (PRB-1332): «Тоді використовуємо GPL і показуємо анотації від нього». The layer is
recorded as GPL (share-alike) in `Essenthos/build/licences.tsv`, credited to the KJV2003 project,
CrossWire Bible Society, with Robinson's Received Text data and the Bible Foundation named
(`Datasets` id `crosswire-kjv`). The GPL binds this data layer and what is derived from it directly
(the numbers on the words, the links drawn from the positions), and any download that contains it
carries the GPL for that layer; it does not bind the code that reads it (NOT-0226).

## What is taken, and what is not

Only the New Testament's statements about words, laid onto the King James the corpus loads from
bible4u by their letters, verse by verse (`CrossWireKingJames`):

- the Strong numbers of each element, served on the KJV words (as the word's own the article's for
  *the* and otherwise the element's first number that is not the article; the rest in `word_strong`);
- the places of the Greek words each element renders, as `stated-by-source` links to Scrivener 1894
  and Stephanus 1550 where the edition holds the named word at the place, carried to Nestle 1904, the
  Byzantine Textform, Westcott-Hort and Tischendorf over those editions' pairing with Scrivener (not
  stated: labelled as carried);
- the elements that print no word, as Greek words the King James leaves untranslated;
- the italics (`<transChange type="added">`), as the words the translators supplied.

The module's own English is not loaded: the words are bible4u's.

## What it replaced

The Zefania KJV+ layer (`SF_2009-01-20_ENG_KJV_(KJV+).xml`), whose `<rights>` element was empty and
whose project stated no licence (PRB-1295). Measured on 2026-11-13: KJV New Testament words carrying a
Strong number 68.5% → 97.4%, words reaching Nestle 1904 74.9% → 94.6%; of 200 disagreeing words read
by hand, about 91.5% right against Zefania's 63.5% (PRB-1332).

RUL-0105, RUL-0181.
