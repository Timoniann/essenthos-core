# GerSch — the Schlachter Bible of 1951, with Strong numbers

CrossWire's SWORD module **GerSch**, version 2.1 of 2024-07-16, loaded as the text `SCH1951`.

Fetched on **2026-10-01** from
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/GerSch.zip`
(SHA-256 `af612223a93f67f4c17c1638982310378e82520dcfb046e92027356e4fc6a9e7`) by
`scripts/fetch-crosswire-strong.ps1`, which checks that hash and re-reads the module's
`DistributionLicense` before it replaces anything. Taken on the owner's approval of 2026-10-01,
TSK-0909. The SourceForge zefania-sharp copy was not needed.

## The terms, read in the module's own configuration

`mods.d/gersch.conf`, inside the archive and the same as
`https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/gersch.conf` (read 2026-10-01). Verbatim:

> DistributionLicense=Copyrighted; Free non-commercial distribution

> TextSource=Genfer Bibelgeschellschaft. Geneva Bible Society

> About=SCHLACHTER BIBEL 1951\par\par Die Heilige Schrift des Alten und Neuen Testaments nach dem
> Urtext üersetzt von F.E. SCHLACHTER Neue Uberarbeitung 1951\par Genfer Bibelgesellschaft\par\par
> Copyright (c) 1951 Genfer Bibelgeschellschaft.

The conf has no `Copyright=` line of its own; the copyright is the About's.

## What that permits

The 1951 revision is the Geneva Bible Society's and in copyright. The statement attached to the
bytes permits free non-commercial distribution, which this project is (RUL-0183), so the text is
loaded with `Redistribution.NonCommercialOnly` and may be served for that use only. If the project
ever becomes commercial, this text has to go. Nobody is named for the Strong numbers; they are read
by `crosswire-strong` for one run and drawn into links (`schlachter-strong`), never stored.

## How it is read

`Versification=German`: the Hebrew's numbering in the Old Testament, the critical Greek's in the
New; loaded as `Versification.Original`. The tagging is sparse and sometimes loose — Genesis 1:1
puts H853, the object marker, on *den* and on *und* — which is what the confidence on every link
drawn from it is for.

## Measured (TSK-0909)

31,170 verses, 709,051 words, 293,949 of them under a Strong number; every verse reads back as the
module prints it. On a slim copy of the corpus: 188,020 of 190,583 numbered Old Testament words
reach a BHSA word (98.7%), 103,053 of 103,366 New Testament words reach Scrivener's Textus Receptus
(99.7%) and 100,528 reach Nestle 1904 (97.3%). About three hundred numbers written without H or G
name no dictionary and are read as nothing.

## Attribution

The translation is **Franz Eugen Schlachter**'s (1905); the 1951 revision is the **Genfer
Bibelgesellschaft**'s. The SWORD module is **CrossWire Bible Society**'s.
