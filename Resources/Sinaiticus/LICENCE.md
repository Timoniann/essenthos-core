# Codex Sinaiticus — the text `SIN`

London, British Library, Add. MS 43725 (the New Testament, with the rest of the codex in Leipzig,
St Petersburg and at Saint Catherine's on Sinai): Gregory–Aland **01**, ℵ, copied in the fourth
century. The manuscript is out of copyright everywhere. What carries a licence is a transcription
of it. `scripts/fetch-alexandrinus.ps1 -Manuscripts 01` fetches it and re-reads the statement below
before it replaces anything. Read at the source on **2026-10-07**.

## `ntvmr-01.xml` — the New Testament (loaded)

The transcription of 01 by the **Institut für Neutestamentliche Textforschung**, Münster, for the
New Testament Virtual Manuscript Room: one TEI document, 3,924,089 bytes, from
<https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=20001&pageID=ALL&format=teiraw>.

The file's own `<availability>`:

> (C) 2026 Institut für Neutestamentliche Textforschung.
>
> This work is licensed under a Creative Commons Attribution 4.0 Unported License

**Creative Commons Attribution 4.0** — credit, no NonCommercial clause, no ShareAlike clause.

**Modified by Essenthos:** verses the file labels in another form than its own `B..K..V..` are
placed by what they hold, checked verse for verse against CNTR's keys: `1Thess.2.14`–`1Thess.4.13`,
`Heb.8.1`–`Heb.9.*`, `2John.1.7`–`2John.1.13`, `XXX.1.*` (3 John) and `Jude.1.*`. The first hand is
read as the text and each correction becomes a note on its verse naming its corrector as the file
does (`corr.1`, `corr.2a`, …); where the first hand was erased past reading, the earliest correction
stands in the text. `OM`, which a few leaves of 1 Thessalonians type where a hand wrote nothing, is
read as nothing; zero-width spaces and typed overlines are not letters. Titles and colophons are not
read. Barnabas and Hermas are not in the file.

## `cntr-01.txt` — the cross-check (not loaded)

The **Center for New Testament Restoration**'s independent transcription of the same manuscript, by
Alan Bunning, from
<https://github.com/Center-for-New-Testament-Restoration/transcriptions/blob/main/class%201/01.txt>
(commit `4c0e9f94117ec3dc4ae40094aec044bb7a416a53`). Its README, kept beside it as `cntr-README.md`:

> Copyright © 2020-2026 by Alan Bunning. All rights reserved. Released under the Creative Commons
> Attribution-ShareAlike 4.0 International License (CC BY-SA 4.0)

It is kept only to check the verse labels of INTF's file against: the tests require every verse the
first hand wrote to be a verse CNTR keys, and name the nine CNTR has that only the first corrector
wrote. Nothing of it is loaded, so its ShareAlike clause binds nothing served.

## Why not the Codex Sinaiticus Project's transcription

codexsinaiticus.org publishes the same manuscript on worse terms, and its own pages disagree:
`copyright.aspx` allows only "non-commercial personal and educational use" without written
permission, `transcription_download.aspx` and `itsee-birmingham/codex-sinaiticus`'s `license.md` say
Creative Commons Attribution-NonCommercial-ShareAlike 3.0. ShareAlike over the data is the line this
project does not cross (RUL-0183), and the same manuscript is available from INTF under plain
attribution, so the project's transcription is not used.
