# The SBL Greek New Testament — the text `SBLGNT`

Michael W. Holmes (ed.), *The Greek New Testament: SBL Edition*, Society of Biblical Literature and
Logos Bible Software, 2010; version 1.2 (10 July 2023, which adds John 7:53–8:11). Read from the
publisher's repository <https://github.com/Faithlife/SBLGNT> (formerly `LogosBible/SBLGNT`) at commit
`c4d241a9c1c479a55b989ba35a4976c1d0b8052c`. `scripts/fetch-sblgnt.ps1` fetches exactly these files,
checks each against its SHA-256 and re-reads both statements below before it installs anything. Read
at the source on **2026-10-07**.

## What is here

`README.md`, `LICENSE` and `About.md` from the repository root, kept as fetched, and `text/` — the
27 files of `data/sblgnt/text/`, one book each: a title line, then one verse per line,
`Matt 1:1<TAB>Βίβλος γενέσεως …`. The apparatus (`data/sblgntapp/`) and the XML are not fetched.

## The licence

The repository's `README.md`:

> The SBLGNT is licensed under a Creative Commons Attribution 4.0 International License.
>
> Copyright 2010 by the Society of Biblical Literature and Logos Bible Software.

Its version history records the change: *v1.1, 2022-12-19, "Update public version and license on
github"*. Its `LICENSE` is the full legal code of **Creative Commons Attribution 4.0 International**,
and GitHub reports `CC-BY-4.0` for the repository. `sblgnt.com/license/` serves the same CC BY 4.0
text under a page title still reading *End User License Agreement*; the title is stale, the terms
are the Creative Commons ones.

**Creative Commons Attribution 4.0** — credit, no NonCommercial clause, no ShareAlike clause.

## What is not taken

MorphGNT's parsing and lemmatisation of this edition (`morphgnt/sblgnt`) is a separate work, under
**Creative Commons Attribution-ShareAlike 3.0**: its README says *"the morphological parsing and
lemmatization is made available under a CC-BY-SA License"*, and calls the text itself subject to the
SBLGNT EULA — a statement about the text that is out of date since 2022. That parsing is read
elsewhere in this corpus against Nestle 1904's words (`../MorphGnt/LICENCE.md`); nothing of it is
attached to this text, which therefore carries no lemma, no parse and no Strong number.

## Modified by Essenthos

The signs that point into the apparatus (`⸀ ⸁ ⸂ ⸃ ⸄ ⸅`, and the number a repeated one carries) are
left out of the words. Holmes's double brackets `⟦ ⟧` and single brackets `[ ]` are kept as a mark on
every word inside them rather than as characters of the word. A parenthesis or dash standing before
a word is kept between it and the word before. The book titles (`ΚΑΤΑ ΜΑΘΘΑΙΟΝ`) are the books'
Greek names.
