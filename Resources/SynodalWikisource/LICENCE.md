# *.usfm — the Synodal's eleven non-canonical books, and the Greek additions it prints inside three canonical ones, as Russian Wikisource transcribes them

**Библия, или книги Священного Писания Ветхого и Нового Завета, в русском переводе** — the Russian
Synodal Bible (1876), the eleven books it prints and marks as non-canonical, transcribed on
<https://ru.wikisource.org/wiki/Библия_(Синодальный_перевод)> from the **Moscow Patriarchate's
edition of 2000** (*"Синодальный перевод РПЦ МП, редакция от 2000 года"*, on every book's page).

Read at the source on **2026-09-25**. `scripts/fetch-synodal-wikisource.ps1` fetches it through the
MediaWiki API, each page **at the revision read that day**, and re-reads the licence and the
non-canonical marking below before it replaces anything. **11 books, 5,737 verses, 1.1 MB.** The
additions inside Daniel, Esther and 2 Chronicles were read on **2026-09-27**, on the owner's word,
from those books' pages, each at the revision read that day (see below).

| file | page | revision | chapters | verses | ordinal, and what it is |
|---|---|---:|---:|---:|---|
| 68-1ES | Вторая книга Ездры | 3826849 | 9 | 442 | 68, **1 Esdras** — the Greek book |
| 70-TOB | Книга Товита | 3679976 | 14 | 244 | 70, Tobit |
| 71-JDT | Книга Иудифи | 3679968 | 16 | 340 | 71, Judith |
| 75-WIS | Книга Премудрости Соломона | 4586674 | 19 | 440 | 75, Wisdom |
| 72-SIR | Книга Премудрости Иисуса, сына Сирахова | 3679970 | 51 | 1,523 | 72, Sirach |
| 76-LJE | Послание Иеремии | 3680014 | 1 | 72 | 76, Letter of Jeremiah |
| 67-BAR | Книга пророка Варуха | 3679981 | 5 | 141 | 67, Baruch |
| 73-1MA | Первая книга Маккавейская | 3680002 | 16 | 924 | 73, 1 Maccabees |
| 74-2MA | Вторая книга Маккавейская | 3679930 | 15 | 556 | 74, 2 Maccabees |
| 80-3MA | Третья книга Маккавейская | 3680028 | 7 | 180 | 80, 3 Maccabees |
| 69-2ES | Третья книга Ездры | 3826850 | 16 | 875 | 69, **2 Esdras** — the Latin 4 Ezra |

## The two Ezras

The Synodal's names and the ordinals do not match, and the ordinal is what is right. Its **second
book of Ezra** is the Greek 1 Esdras (it opens with Josiah's passover, *И совершил Иосия пасху*), and
stands at ordinal 68 with Brenton's and the King James's 1 Esdras. Its **third book of Ezra** is the
Latin apocalypse the King James calls 2 Esdras and the Vulgate 4 Ezra (it opens *Вторая книга Ездры
пророка*, as the Latin does), translated from the Latin because no Greek survives, and stands at 69.
Its first book of Ezra is the canonical Ezra, from bible4u. The page for the third book notes that
the seventy verses the Latin found in the nineteenth century between 7:35 and 7:36 are not in the
Synodal; they are not here either, and its chapter 7 has 70 verses as the King James's has.

## What is taken, and how it is read

The verse templates become `\v`, a blank line between verses becomes `\p` (the pages say their
paragraphs follow the printed edition), and the words set in italics — the translators' own, added
for the sense, 273 spans — become `\add`, which the corpus loads as supplied words, as it loads the
Synodal's square brackets elsewhere. Sirach's prologue has no verse number and stands at the head of
1:1, as the King James sets its own; its title *Предисловие* is the page's heading and is dropped. The
margin cross-references the pages draw beside verses, the chapter headings and the footnote in
Daniel are the transcribers' furniture and are dropped, and so are the stress marks set on a few
words, which bible4u's Synodal prints on none.

The numbering is the Synodal's own and is not the Greek's everywhere. Measured against Brenton's
Greek chapter by chapter: 1 Maccabees agrees in all 16 chapters; 2 Maccabees in all but chapter 2,
which ends at verse 33 as the Latin's does and which the versification data places as the Latin's;
Tobit, Judith, Wisdom, Baruch, the Letter and 1 Esdras differ in one to seven chapters; Sirach in 45
of 51 and 3 Maccabees in all 7. The corpus joins the Synodal verse by verse to the Greek only in the
chapters where the two print the same verses.

## The additions inside canonical books

The Synodal prints the Greek additions inside Daniel, Esther and 2 Chronicles, in square brackets
where they stand inside a verse of the Hebrew; bible4u's Synodal, which is where those books come
from, leaves all of them out (its Daniel has 12 chapters and 3:30 ends the chapter, its Esther 10
chapters and 167 verses). They are taken from those books' own pages, and nothing else of the pages:

| file | page | revision | what is taken |
|---|---|---:|---|
| 27-DAG | Книга пророка Даниила | 3679983 | 3:24-90, the Prayer of Azariah and the Song of the Three; 13:1-64, Susanna; 14:1-42, Bel |
| 17-ESG | Книга Есфири | 3679964 | the bracketed words of 1:1 (a verse of its own before the Hebrew's 1:1), 3:13, 4:17, 5:1, 5:2, 8:12 and 10:3, each written as the verse's piece `a` |
| 79-MAN | Вторая книга Паралипоменон | 4588701 | the Prayer of Manasseh after 36:23, twelve verses the page letters а to м, numbered 1 to 12 |

The song is one bracketed passage and every word of it is marked as the Greek's, as the brackets
are elsewhere; so are the additions to Esther. The short bracketed phrases the Synodal adds inside
other verses of Esther are not taken, as bible4u does not print them. Susanna and Bel are Daniel 13
and 14 in the Synodal and books of their own in the corpus, each verse keeping the address it is
printed under as its stated one; the song is held as a book of its own the same way, numbered as the
King James's Song of the Three is, and stands in Daniel 3 as well.

## What is not taken

- **Nothing Ukrainian.** No Ukrainian translation of these books is openly licensed; see DOC-0209.

## The licence

**Public domain.** Stated on the Synodal's own page on ru.wikisource, and read there by the fetch:

> Это произведение перешло в общественное достояние в России согласно ст. 1281 ГК РФ, и в странах,
> где срок охраны авторского права действует на протяжении жизни автора плюс 70 лет или менее […]
> Если произведение является переводом, или иным производным произведением, или создано в
> соавторстве, то срок действия исключительного авторского права истёк для всех авторов оригинала и
> перевода.

The translation was made by the four theological academies between 1815 and 1875 and first printed
in full in 1876–1877; the non-canonical books were translated from the Greek, the third book of Ezra
from the Latin. ru.wikisource's own site licence, which covers the transcription as a contribution,
is Creative Commons Attribution-ShareAlike 4.0 (`siteinfo.rightsinfo`, read the same day); a
transcription of a public-domain text adds no authorship of its own, and the site states the text
itself public domain. Attributed to **Russian Wikisource and its contributors**, with the page and
revision in each file's `\rem` line.
