# The Peshitta: the Syriac Old Testament, in Text-Fabric

From <https://github.com/ETCBC/peshitta>, read at the source on **2026-11-24** at commit
**9850f5addade26f681334aa475570bef9b0b440a** (the repository's last, 2024-01-23), data version
**0.2** under `tf/`. All 13 feature files and `__checkout__.txt` are taken; the repository's
`README.md`, `docs/about.md` and `LICENSE` are kept beside them under `upstream/`, exactly as read.
`scripts/fetch-etcbc.ps1` fetches them, holds every file to its SHA-256 in
`scripts/fetch-etcbc.sha256`, and re-reads the sentences quoted below before it installs anything.

**65 books, 1,269 chapters, 31,341 verses, 426,835 words** (from `otype.tf`). Bare text: Syriac
script, one word per slot, a trailer after it; no lemma, no morphology, no vowels. Five books come in
two witnesses, A and B, which the dataset keeps as separate books (`Mc1_A`/`Mc1_B`, `Tb_A`/`Tb_B`,
`OrM_A`/`OrM_B`, `ApcPs_A`/`ApcPs_B`, `EpBar_A`/`EpBar_B`).

## Licence: CC BY-NC 4.0 on the data, MIT on the converter

**1. `docs/about.md`, "License and citation"**, the statement about the data:

> The plain text of the Peshitta, its conversion to Text-Fabric format,
> is subject to the
> [CC-BY-NC license](https://creativecommons.org/licenses/by-nc/4.0/)
>
> That means that you may use this data as you whish if you do not use it commercially.

> If you would like to use the textual data commercially, contact the ETCBC or Brill.

> The conversion program itself it subject the liberal [MIT license](https://mit-license.org).

**2. `LICENSE`**, which is the converter's: "MIT License — Copyright (c) 2019 Dirk Roorda". GitHub's
sidebar reports MIT from this file. It covers the code, not the text: about.md says so in the sentence
above.

**3. The feature files' headers** carry no licence line at all. Every one says

> @source=SEDRA
>
> @sourceUrl=https://sedra.bethmardutho.org/about/contributors

and `@encoders=Eep Talstra Centre for Bible and Computer. LinkSyr project (CLARIAH research pilot).
Hannes Vlaardingerbroek, Constantijn Sikkel (PIL) and Dirk Roorda (TF)`.

**4. Brill's position**, in about.md:

> The printed edition contains a critical apparatus.
> That is copyrighted by Brill and not included in this repo.

**Not ShareAlike, and NonCommercial is acceptable (RUL-0183).** The corpus holds the text under
CC BY-NC 4.0 and credits the ETCBC and the Peshitta Institute.

## The provenance disagrees with itself, recorded rather than resolved

about.md says the text is the *Vetus Testamentum Syriace* of the Peshitta Institute, published by
Brill, "obtained by OCR from the printed edition: The Old Testament in Syriac - according to the
Peshitta version by The Peshitta Institute Leiden, published by E.J. Brill, 1987", with the
OCR program `syrocr` written for it by Hannes Vlaardingerbroek, and Codex Ambrosianus "for the
remaining books". The `.tf` headers name SEDRA (Beth Mardutho) as `@source`. SEDRA's site reads
"Copyright © 2011-2026 by Beth Mardutho The Syriac Institute All Rights Reserved" while describing
SEDRA III as a non-commercial open-source database (read 2026-09, TSK-0230). Both claims cannot
describe the same bytes; the licence the repository states for its data is about.md's, and that is
the one the corpus holds it under. If SEDRA is in fact the source of the bytes, its terms are the
question, and nobody has asked Beth Mardutho.

## Attribution, as the repository asks

> Willem Th. van Peursen, Geert Jan Veldman, Constantijn Sikkel, Hannes Vlaardingerbroek en Dirk Roorda.
> (2018-10-17). ETCBC/peshitta: (Version 0.3). Zenodo. http://doi.org/10.5281/zenodo.1464757

The citation names the Zenodo release 0.3 of the repository; the data read is `tf/0.2`, the version
the 2021-03-11 conversion wrote. The general editors of the Peshitta Project are Bas ter Haar Romeny
and Willem van Peursen.

## What is loaded, and what is not

The text `PESHITTA`: the 39 books of the Hebrew Bible and the deuterocanonical books the corpus has
a book for (Wisdom, Sirach, Tobit, Judith, Baruch, the Letter of Jeremiah, Susanna, Bel and the
Dragon, 1 Esdras, 2 Esdras, 1-4 Maccabees, the Prayer of Manasseh, Psalm 151, the Odes and the
Psalms of Solomon). Where a book comes in two witnesses, the one that holds the whole book is read:
`Tb_A` (Tobit B has only chapters 7-14), `Mc1_B` (1 Maccabees A stops at 14:25); for the Prayer of
Manasseh and Psalm 151, both whole, witness A. Not loaded: the Apocalypse of Baruch and the Letter of
Baruch (`ApBar`, `EpBar_A/B`) and Psalms 152-155 (`ApcPs`), which have no book in the corpus yet.

`ETCBC/syriac` (tf 0.92), the annotated successor with lexemes and morphology over about 40% of the
text, is not taken: it is a later enrichment, not the text.
