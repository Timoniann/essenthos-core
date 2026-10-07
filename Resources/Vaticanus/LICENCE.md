# Codex Vaticanus — the text `VAT`

Vatican City, Biblioteca Apostolica Vaticana, Vat. gr. 1209: Gregory–Aland **03**, B, copied in the
fourth century; its New Testament is pages 1235–1518. The manuscript is out of copyright everywhere.
What carries a licence is a transcription of it. `scripts/fetch-alexandrinus.ps1 -Manuscripts 03`
fetches it and re-reads the statement below before it replaces anything. Read at the source on
**2026-10-07**.

## `ntvmr-03.xml` — the New Testament (loaded)

The transcription of 03 by the **Institut für Neutestamentliche Textforschung**, Münster, for the
New Testament Virtual Manuscript Room: one TEI document, 3,455,586 bytes, from
<https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=20003&pageID=ALL&format=teiraw>.

The file's own `<availability>`:

> (C) 2026 Institut für Neutestamentliche Textforschung.
>
> This work is licensed under a Creative Commons Attribution 4.0 Unported License

**Creative Commons Attribution 4.0** — credit, no NonCommercial clause, no ShareAlike clause.

**Modified by Essenthos:** verses the file labels in another form than its own `B..K..V..` are
placed by what they hold, checked verse for verse against CNTR's keys: `Matt.16.16`–`Matt.17.9`,
`3John.1.*` and `Jude.1.*`. The first hand is read as the text and each correction becomes a note on
its verse naming its corrector as the file does (`corr.1`, `corr.2`, …); where the first hand was
erased past reading, the earliest correction stands in the text. Titles and colophons are not read.

The codex's last surviving leaf ends at Hebrews 9:14. Nothing after it is in the file or in the
text: the rest of Hebrews, 1–2 Timothy, Titus, Philemon and Revelation are absent because the
manuscript has lost them, which is not an omission. The Revelation now bound with the codex is a
fifteenth-century supplement, another manuscript, and is not here.

## `cntr-03.txt` — the cross-check (not loaded)

The **Center for New Testament Restoration**'s independent transcription of the same manuscript, by
Alan Bunning, from
<https://github.com/Center-for-New-Testament-Restoration/transcriptions/blob/main/class%201/03.txt>
(commit `4c0e9f94117ec3dc4ae40094aec044bb7a416a53`). Its README, kept beside it as `cntr-README.md`:

> Copyright © 2020-2026 by Alan Bunning. All rights reserved. Released under the Creative Commons
> Attribution-ShareAlike 4.0 International License (CC BY-SA 4.0)

It is kept only to check the verse labels of INTF's file against: the tests require the two to key
exactly the same verses. Nothing of it is loaded, so its ShareAlike clause binds nothing served.
