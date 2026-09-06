# *.usfm — the Kulish–Nechui-Levytsky–Puliui Bible, 1903

**Сьвяте письмо Старого і Нового Завіту**, translated by **Panteleimon Kulish** (1819–1897) with
**Ivan Puliui** (1845–1918) and **Ivan Nechui-Levytsky** (1838–1918). The first complete Bible in
modern literary Ukrainian, published by the **British and Foreign Bible Society** in 1903.

From <https://ebible.org/ukr1871/>, read at the source on **2026-09-06**. eBible generated the files
on 2 September 2026 from source files of the same date, which is the `sourceDate` its catalogue
carries for this text; the archive taken is `https://ebible.org/Scriptures/ukr1871_usfm.zip` and
`scripts/fetch-kulish.ps1` is what fetches it, re-reading every statement below before it replaces
anything.

**66 books, 1,189 chapters, 31,082 verses, 584,659 words** — 23,127 verses in the Old Testament and
7,955 in the New, which is what eBible's catalogue states for it and what the files hold. Chapter
and verse numbering is the English one throughout: 150 psalms with 9 and 10 apart, Malachi in four
chapters, Joel in three. The files carry 204 footnotes and 2,079 spans marked as spoken by Jesus;
neither is annotation, and there is none of any other kind — no lemmas, no morphology, no Strong
numbers, no alignment to anything.

## The licence, and there are two statements of it that agree

**1. `copr.htm`, which ships inside the archive and is kept beside this file** — the same page eBible
serves at <https://ebible.org/ukr1871/copyright.htm>. It states the terms twice, once under the
title and once in the footer, and both times the whole statement is two words:

> Біблія в пер. П.Куліша та І.Пулюя, 1905
>
> The Holy Bible in Ukrainian, translated by P. Kulish and I. Pulyu in 1905
>
> **Public Domain**
>
> Language: Українська (Ukrainian)
>
> Translation by: Panteleimon Kulish
>
> Contributor: Ivan Semenovych Nechui-Levytsky, Ivan Pavlovych Puluj

**2. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `ukr1871`:

> `"Redistributable"` = `True`, `"Copyright"` = `public domain`, `"downloadable"` = `True`

No ShareAlike, no NonCommercial, no attribution condition, no field anywhere that names a rights
holder. The catalogue also carries `swordName` = `ukr1871eb`, `FCBHID` = `UKRPAN` and
`PODISBN` = `978-1-5313-0677-9` — the print-on-demand number, which eBible prints identically on
its other Ukrainian row and which therefore identifies the language edition rather than this text.

## Why the statement is believable, which is not the same as it being made

Public domain is a claim about the law and not a grant anyone can issue, so a publisher saying it is
worth exactly as much as the facts underneath. Here they hold in both of the jurisdictions that
matter. Kulish died in 1897, Nechui-Levytsky and Puliui both in 1918, so the longest of the three
terms ran out in 1989 on life-plus-seventy. And the complete Bible was published in 1903, decades
before the 1929 line that puts a work in the public domain in the United States whatever its
authors' dates.

This is the whole reason the text is here. DOC-0189 catalogues roughly twenty Ukrainian
translations, and every one made after 1918 is owned by a Bible society, a religious order or a
mission, or is offered only under CC BY-SA, which RUL-0183 bars.

## What is not taken

eBible publishes a second Ukrainian text, **`ukrfb`, «Біблія свободи»**, under the same Public
Domain statement, and its catalogue describes it as *"Freedom Bible updated from translation by P.
Kulish and I. Pulyu"*. It is not an update. Both archives were fetched and compared line by line on
**2026-09-06**: of 33,887 lines, **33,855 are identical**, and of the 32 that differ most are damage
rather than revision — `візьмуть` for `пізьмуть`, `Господь` for `Гоеподь`, `знаменнєм` for
`зваменнєм`, `Нехай же` broken as `Нех ай же`, `Кругом` as `Кр угом`, and the footnote on
Ecclesiastes 12:8 dropped. A handful go the other way and fix a letter. Loading it would put a
second Ukrainian witness in the corpus that witnesses nothing, so it is recorded here and left
where it is.

## Attribution, which public domain does not require

Panteleimon Kulish began the translation in the 1860s and published the New Testament with Ivan
Puliui in Vienna in 1871; the Old Testament manuscript burned at his farm at Motronivka in November
1885 and he began it again. He died in 1897 with it unfinished. Puliui and Ivan Nechui-Levytsky
completed it, and the British and Foreign Bible Society printed the whole Bible in 1903 — the
accounts differ on whether the imprint reads Vienna or London. eBible titles the file for the 1905
printing.

The corpus names all three on the `text` row, in `Essenthos.Core/Loading/KulishTextSource.cs`, and
here. A fact printed without a name has quietly been claimed as ours. RUL-0181.
