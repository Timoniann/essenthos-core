# CUV-OT-mapped-to-BHS.csv — the Open Hebrew Bible's mapping of the Chinese Union Version

**Open Hebrew Bible Project** by **Eliran Wong**: `009-BHS-mapping-CUV/CUV-OT-mapped-to-BHS.csv`,
the Old Testament of the Chinese Union Version in FHL's Strong-numbered spans, each number followed
by the running number of the BHS word it stands for — the same numbers the King James mapping in
`../mapping` writes, from the same project.

Fetched on **2026-10-01** from `https://github.com/eliranwong/OpenHebrewBible` at commit
`28ae9b2bd340eed4c483482852f2ed3b2bd07919` (2023-05-31, the repository's last), SHA-256
`ecd3bff9e49f9d7cb21ef08579e5c8cbd2695ca4d8b5e369699932916e5aa9a1`, by
`scripts/fetch-openhebrewbible.ps1`, which checks that hash and re-reads the README's terms before it
replaces anything. Taken on the owner's approval of 2026-10-01, TSK-0909. `github.com/eliranwong/OpenHB`
does not exist; the data is in this repository.

## The terms

The repository has **no LICENSE file** (GitHub's licence API answers *Not Found*). The terms are
stated in its `README.md`, which is kept beside the data. Verbatim, read at that commit:

> Major works in this repository:
> - mapping data between ETCBC's Biblia Hebraica Stuttgartensia (Amstelodamensis), OpenScriptures'
>   Strong's numbers and Berean Study Bible
> - [...]
> - data on aligning the text of BHS with bible translations: Berean Study Bible, King James Version, Chinese Union Version
> - [...]
>
> All works listed above are released under the following license:
>
> Open Hebrew Bible Project by Eliran Wong is licensed under a Creative Commons
> Attribution-NonCommercial 4.0 International License. Based on a work at
> https://github.com/eliranwong/OpenHebrewBible. Permissions beyond the scope of this license may be
> available at https://marvel.bible/contact/contactform.php.

> Please note that the use of the text of BHS here is under the same license of its original source
> at https://github.com/ETCBC/bhsa#license, which is strictly non-commercial.

And of the Chinese text the file carries:

> Chinese Union Version is in public domain. Source (GNU Free Documentation License Version 1.1):
> https://bible.fhl.net/public/

The folder `009-BHS-mapping-CUV/CUV/` adds: "Files uploaded here are formatted for iOS app
'BibleBento Plus'. Source: https://bible.fhl.net/public/".

**CC BY-NC 4.0** is the most restrictive statement on the mapping; NonCommercial does not bar it
(RUL-0183), and it is the King James mapping's licence already. The Chinese characters in the file
are FHL's text under the FDL, as `../ChineseUnion1919/LICENCE.md` sets out; the corpus takes nothing
of them from this file — it lays the file's spans onto the words it loaded from CrossWire's module,
by their letters, and keeps only which word renders which.

## How it is used

`Essenthos.Forge ohb-cuv` writes one `stated-by-source` link from the CUV (and CUVS, by the same
spans) to BHSA per span, credited *the Open Hebrew Bible's mapping of the Chinese Union Version to
BHS (Eliran Wong), 009-BHS-mapping-CUV*, declared in `Essenthos.Corpus/Corpus/Datasets.cs`
(`ohb-cuv`). Where a link of FHL's numbers already names the same words it becomes a claim on that
link instead. `--replace` removes the links FHL's numbers drew for the pair first and matches the
numbers again after, so that they leave the words the mapping states to it.

## Measured (TSK-0909)

276,638 mapped spans over 23,145 verses; 23,045 verses print the same letters as CrossWire's module,
the rest differ by the notes the file prints inside the verse. On a slim copy of the corpus, 271,108
spans (98.0%) were placed on both sides, 7 name no BHSA word, 5,523 no word of ours. Matched before
FHL's numbers (`--replace`): 271,108 stated links per script, 200,551 of them named exactly by the
numbers as well; the numbers add 4,472 links for words the mapping does not state. Matched after
them instead: 199,544 corroborate an FHL link and 66,002 differ from one — mostly the mapping naming
one occurrence of a word the verse repeats, where the numbers had linked every occurrence.

## Attribution

The mapping is **Eliran Wong**'s, Open Hebrew Bible Project (marvel.bible). The spans and their
Strong numbers are the **Faith Hope Love foundation**'s (bible.fhl.net). The Hebrew is ETCBC's BHSA.
