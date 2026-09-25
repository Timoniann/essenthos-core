# Codex Alexandrinus — the text `ALEX`

London, British Library, Royal MS 1 D V–VIII: Gregory–Aland **02**, Rahlfs **A**, copied in the
fifth century. The manuscript is out of copyright everywhere. What carries a licence is each
transcription of it, and there are two, under different terms. `scripts/fetch-alexandrinus.ps1`
fetches both and re-reads each statement below before it replaces anything. Read at the source on
**2026-09-25**.

## `ntvmr-02.xml` — the New Testament (loaded)

The transcription of 02 by the **Institut für Neutestamentliche Textforschung**, Münster, for the
New Testament Virtual Manuscript Room: one TEI document, 3,019,369 bytes, folios 26r–158v, from
<https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=20002&pageID=ALL&format=teiraw>.

The file's own `<availability>`:

> (C) 2026 Institut für Neutestamentliche Textforschung.
>
> This work is licensed under a Creative Commons Attribution 4.0 Unported License

**Creative Commons Attribution 4.0** — credit, no NonCommercial clause, no ShareAlike clause.

**Modified by Essenthos:** 69 verses are labelled in the file in another form than its own
`B..K..V..` (`.1.1` for 3 John, `Jude.1.1`, `Heb.13.22`, and `Heb.1.1`–`Heb.1.15` for 1 Timothy
1:1–15); they are placed by what they hold, checked verse for verse against CNTR's keys. The first
hand is read as the text and each correction becomes a note on its verse; where the first hand was
erased past reading, the correction stands in the text. Titles and colophons are not read.

## `cntr-02.txt` — the cross-check (not loaded)

The **Center for New Testament Restoration**'s independent transcription of the same manuscript,
by Alan Bunning, from
<https://github.com/Center-for-New-Testament-Restoration/transcriptions/blob/main/class%201/02.txt>
(commit `4c0e9f94117ec3dc4ae40094aec044bb7a416a53`). Its README:

> Copyright © 2020-2026 by Alan Bunning. All rights reserved. Released under the Creative Commons
> Attribution-ShareAlike 4.0 International License (CC BY-SA 4.0)

It is kept only to check the verse labels of INTF's file against: the tests require the two to
agree on every verse the first hand wrote. Nothing of it is loaded, so its ShareAlike clause binds
nothing served.

## `swete/` — Genesis to 46:28 and 1–4 Maccabees (loaded), the Odes (not loaded)

Swete's *The Old Testament in Greek according to the Septuagint* (Cambridge, 1887–1894) prints
Codex Alexandrinus as his text in these books. In the transcription Open Greek and Latin made for
**First1KGreek**, `data/tlg0527/tlg001`, `tlg023`–`tlg026` and `tlg028`, `*.1st1K-grc1.xml`, at
commit `8ee111eb44ecef4120c844e10749178d95d1f30c`:
<https://github.com/OpenGreekAndLatin/First1KGreek/tree/8ee111eb44ecef4120c844e10749178d95d1f30c/data/tlg0527>.

First1KGreek's `license.md` is the full legal code of **Creative Commons Attribution-ShareAlike 4.0
International**, and its `.zenodo.json` says `"license": "CC-BY-SA-4.0"`; both are kept beside
Swete's other books in `../Swete/first1kgreek-license.md` and described in `../Swete/LICENCE.md`.

**Modified by Essenthos:** Genesis is read only as far as 46:28, where Swete's text passes to Codex
Vaticanus; letters he brackets as lost in Alexandrinus are left out, with any word none of whose
letters survive; and the four passages of the manuscript's torn leaf (14:14–17, 15:1–5, 15:16–19,
16:6–9), which he fills from other manuscripts between ¶ and §, are left out whole. The Odes are
fetched and not read: Swete numbers two of their chapters `iva` and `ivb`.

## Isaiah

Ottley's Isaiah, read from `../Swete/First1KGreek/isaiah-ottley-1904.xml` exactly as the text
`OTTLEY` is, under the licence recorded in `../Swete/LICENCE.md` (CC BY-SA 4.0).

## What is not here, and why

Every other Old Testament book the codex holds survives typed only as readings in Swete's
apparatus. A text rebuilt from that apparatus was measured against Ottley's Isaiah on 2026-09-25 and
closed about a third of the difference between Vaticanus and Alexandrinus, with some 3.8% of words
still differing in substance, so it was not made for the other books.
