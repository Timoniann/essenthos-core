# RSTE_verse_words.xml — the Russian Synodal with Bob Jones University's Strong numbering

**Used only as an input to the mapping.** The corpus does not load this edition as a text, does not
store its Strong numbers on any word, and does not serve them. What it holds and serves is the links
between the Synodal words it already carries (bible4u's RUSV) and the Hebrew and Greek words, drawn
by matching these numbers within each verse. The basis for that is the owner's decision recorded
below, not the notice, whose conditions this use does not meet.

## What the file is

From <https://github.com/swmail/RST>, commit **`78d6bbf343de07e06d07cf8bd32b7a38d85aea36`** (3
January 2015, "Fix punctuation in Prov.4.27"), fetched **2026-09-13** by
`scripts/fetch-synodal-strong.ps1`, which checks both hashes below before it replaces anything.

| file | bytes | SHA-256 |
|---|---|---|
| `RSTE_verse_words.xml` | 25,105,695 | `e82a07241baecf08e7d94628a858c17c7cb675b2c712b65318f56d64513e13ca` |
| `rsthcs.conf` | 1,093 | `7757641a5d0c59aeca367612f09a6b96b360be6a2a9348b04ff75f2a78b2008c` |

One OSIS file: the whole Synodal, 66 books, 31,163 verses, 347,264 `<w>` elements, each word or
phrase carrying `lemma="strong:H####"` and, for verbs, a Tense-Voice-Mood code
(`morph="strongMorph:TH8804"`). Section headings and cross-references are in the file and are not
read. The repository also holds a README, a Makefile and a punctuation script; none of them states
terms, and there is no LICENSE file on that commit.

**Versification.** The header declares `<refSystem>Bible.KJV</refSystem>`; the verses are not in it.
Psalm 22 is *Господь – Пастырь мой*, Numbers 13:1 is the King James's 12:16, Job 40:20 is its 41:1,
the Romans doxology stands at 14:24–26, 3 John runs to verse 15, and the cross-references cite
`Ps.32.6` for the King James's 33:6. That is the Synodal numbering the module's own
`Versification=Synodal` names. The edition is placed onto bible4u's renumbered file through the
addresses bible4u prints — `Essenthos.Core/Loading/Links/SynodalStrongLayer.cs` — and measured on
2026-09-13 every one of the 31,163 tagged verses found a verse and all 31,102 corpus verses agreed
(NOT-0183).

## The only statement of terms: Bob Jones University, 1996

The Tense-Voice-Mood codes identify the numbering as the *Pierce-Strong's Number Code System with
Tense-Voice-Mood Verb Parsings* of Bob Jones University. Its notice is printed in full on two pages.
Both were read raw on **2026-09-12** (attached to PRB-0264) and fetched again on **2026-09-13**,
byte-identical. Quoted verbatim, including the pages' own spelling.

**1.** <http://www.clavmon.cz/ultranet/bw/bwpopisVerzi.htm>, entry 40 (the page is windows-1250; the
`0` after *Copyright* is on the page, where a copyright sign has been lost):

> 40. RST - The Russian Synodal Text (RST) of the Bible (Orthodox Synodal Edition 1917), with
> Russian Lexicons (abridged Thayer s Greek-Russian and Hebrew Russian, see above Lexicon section),
> and with every word/phrase keyed to the complete Pierce-Strong's Number Code System with
> Tense-Voice-Mood Verb Parsings, Copyright 0 1996 Bob Jones University, Use of the work for profit
> making purposes is expressly forbidden. This work may not be copied or reproduced in any way for
> the purpose of profitable business ventures. Its publication has been financed on a nonprofit
> basis for the express purpose of propagating the gospel of the Lord Jesus Christ. Nonprofit uses
> of the material for the exclusive purpose of propagating the gospel of the Lord Jesus Christ are
> expressly permitted provided that the work is in no way modified and provided that those who use
> it hold strongly to the doctrines of the verbal plenary inspiration of the Bible, the deity and
> humanity of the virgin-born Son of God, the Lord Jesus Christ, the physical bodily resurrection of
> our Lord, and otherwise hold to the fundamental doctrines of the Christian faith.

**2.** <http://www.biblelinguistics.co.uk/pages/copyrightinfo.html>, printed under the Ukrainian
entry rather than the Russian one, and without the sentence about how publication was financed:

> UKR, Ukrainian Bible 1996 (Copyright Bob Jones University: Use of the work for profit making
> purposes is expressly forbidden. This work may not be copied or reproduced in any way for the
> purpose of profitable business ventures.
> Non profit uses of the material for the exclusive purpose of propagating the gospel of the Lord
> Jesus Christ are expressly permitted provided that the work is in no way modified and provided that
> those who use it hold strongly to the doctrines of the verbal plenary inspiration of the Bible, the
> deity and humanity of the virgin-born Son of God, the Lord Jesus Christ, the physical bodily
> resurrection of our Lord, and otherwise hold to the fundamental doctrines of the Christian faith)

The same page lists the plain Synodal separately: *"RUS, Russian Orthodox Synodal Edition 1917 (out
of copyright)"*. The text is not what the notice claims; the numbering keyed to it is.

**What it permits, read plainly.** Non-profit use, and only for the exclusive purpose of propagating
the gospel, only of the work unmodified, and only by users who hold the named doctrines. Essenthos is
non-profit (RUL-0183). Extracting the numbers and matching them against other texts is a
modification, a research corpus does not claim that purpose, and the project cannot assert anything
about its users' doctrines. **This use does not meet the notice's conditions**, and nothing here
pretends it does.

## What the downloaded copy says about itself

`rsthcs.conf`, verbatim, the only terms attached to these bytes:

```
About=Русский синодальный перевод (1876) с номерами Стронга, оглавлением и параллельными местами\par\par <hr width=50%> Russian Synodal Translation (1876) with Strong's Numbers, Headings and Scripture Cross-references\par\par Based on the "Holy Bible" (http://www.ibt.org.ru), "BibleQuote" (http://jesuschrist.ru/software/) and "Slavic Bible for Windows" (http://www.sbible.boom.ru), prepared by Wjatscheslaw Stoljarski (wjastl#gmail.com)
DistributionLicense=Public Domain
Versification=Synodal
```

It names no Bob Jones University. The TVM codes on its words are the layer the notice describes, so
this is that layer passed on without its notice, and under RUL-0105 the more restrictive statement
governs: the notice, not `Public Domain`.

## The basis: the owner's decision of 2026-09-13

Recorded on PRB-0264, in answer to its question on whether to proceed given the full notice:

> Давай використати синодальний стронга лише для мапінга. Нам треба сильний мапінг!

— *use the Strong-tagged Synodal only for the mapping; a strong mapping is what is needed.* Read, and
implemented, as follows:

- the tagged edition is an internal input to one batch command, `synodal-strong`, and is never a
  text of the corpus;
- its numbers live in memory for the length of that run — `word.strong_number` and `word_strong`
  hold nothing from it, and no endpoint serves them;
- the corpus holds and serves only the resulting Synodal-to-original links, method `strong-number`,
  each with a confidence, their source beginning *"the Bob Jones University Strong numbering of the
  Synodal (1996), read from swmail/RST"*;
- `Essenthos.Core/Endpoints/Datasets.cs` declares them under `bju-synodal-strong` with a
  NonCommercial obligation, which binds the project to non-commercial use for as long as these links
  are in the corpus.

## Attribution

The numbering and its Tense-Voice-Mood parsings are Bob Jones University's, 1996. The digitised
Synodal and its tagging were assembled for BibleQuote and the Slavic Bible for Windows and prepared as
this OSIS file by Wjatscheslaw Stoljarski. The Synodal translation itself (1876) is out of copyright.
The links drawn from all of this carry the credit above on every row. RUL-0181.
