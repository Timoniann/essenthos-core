# Commentaries — what someone wrote about a verse

Three commentaries, one folder each: two SWORD commentary modules from CrossWire, read at the source on
**2026-11-16** (`scripts/fetch-commentaries.ps1` fetches both, checks each archive's hash and re-reads each
configuration's licence line before it installs anything), and Lopukhin's Explanatory Bible from the
Christian Classics Ethereal Library's Russian collection (below).

## MHC — Matthew Henry's Complete Commentary on the Whole Bible

Matthew Henry (1662-1714), *An Exposition of the Old and New Testament*, 1706-1721; the epistles from
Romans on were finished from his notes by other ministers after his death.

- Archive: `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/MHC.zip`, SHA-256
  `6bcb936873ca144e317805e5c1677940fd86e2403f7c14517752e44f25c8882b`, module version 2.2 (2022-08-29),
  driver zCom4, OSIS markup, KJV versification.

### What it is used under: Public Domain

| Where | What it says |
|---|---|
| `MHC/mods.d/mhc.conf`, the module's own configuration | `DistributionLicense=Public Domain`; `TextSource=http://www.ccel.org/` |
| the same file, `About` | "Public Domain--Copy Freely. This text was prepared from the Christian Classics Ethereal Library located at www.ccel.org. Thanks also to Logos Research, Inc. for providing CCEL with their text of Matthew Henrey's Complete Commentary, from which this edition has been prepared." |
| CrossWire's module page, <https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=MHC> | "Distribution License: Public Domain" |
| CCEL's pages for the work, <https://ccel.org/ccel/henry/mhc> | no rights statement shown |

The work is out of copyright by age. The module's own statement adds no condition, and no statement
attached to these bytes claims a transcriber's right. It is credited anyway (RUL-0181).

## Rieger — Carl Heinrich Riegers Betrachtungen

Carl Heinrich Rieger (1726-1791), *Betrachtungen über das Neue Testament* (Stuttgart 1833, second
unchanged edition) and *Kurze Betrachtungen über die Psalmen und über die zwölf kleinen Propheten*
(Stuttgart 1835). The New Testament, the Psalms and the twelve minor prophets only.

- Archive: `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/Rieger.zip`, SHA-256
  `b7bacef4f009a9f7c13a2ea7b5181f7f442be4b498125b92d477e4a0198af119`, module version 1.0 (2000-08-15),
  driver RawCom, HTML markup (`SourceType=ThML`), no versification named (SWORD's default, KJV).
- `Rieger.Autobiografie.html` in the module folder is not read.

### What it is used under: Public Domain

| Where | What it says |
|---|---|
| `Rieger/mods.d/rieger.conf`, the module's own configuration | `DistributionLicense=Public Domain`; `TextSource=Matthias Ansorg` |
| the same file, `About` | "License: Public Domain -- copy freely"; "Made available in electronic format by Michael Bolsinger … at http://www.luther-bibel-1545.de … It was converted to SWORD format by Matthias Ansorg" |
| CrossWire's module page, <https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=Rieger> | "Distribution License: Public Domain" |

Rieger died in 1791 and both editions were printed in the 1830s. The digitiser's and the converter's
statement is the one in the configuration, and it places the module in the public domain.

## What the corpus does with them

Each module stores, for each verse of the King James numbering, the comment written on it; a comment
written over several verses is one entry every verse of the run points at. The corpus keeps each
comment once, over the run of verses it was written on, with the chapter and book introductions the
module stores before a chapter's first verse, in the table `commentary_entry`. Every word of the
comment is kept; only presentation (fonts and colours) is dropped, and the markup becomes headings,
paragraphs, italics, small capitals and references to verses.

## Attribution as shown to readers

> Matthew Henry, Complete Commentary on the Whole Bible (1706-1721), public domain, from CrossWire's
> SWORD module as prepared from the Christian Classics Ethereal Library.

> Carl Heinrich Rieger, Betrachtungen (Stuttgart 1833 and 1835), public domain, digitised by Michael
> Bolsinger and converted by Matthias Ansorg, from CrossWire's SWORD module.

## Lopukhin — Толковая Библия (the Explanatory Bible of Lopukhin and his successors)

*Толковая Библия, или Комментарий на все книги Священного Писания Ветхого и Нового Завета*, Saint
Petersburg 1904-1913, eleven volumes and a supplementary 7A, a free supplement to the journal
«Странник». Alexander Pavlovich Lopukhin (1852-1904) edited only the first volume (the Pentateuch,
1904); the rest are «издание преемников А. П. Лопухина», written by professors of the four
theological academies. Re-checked on **2026-11-16** at the owner's word (FTR-0286: «можна брати.
Ще раз цю інфу перевір»).

- Archives: 54 FictionBook 2 files in zip, `https://www.ccel.org/contrib/ru/xml/<name>.zip`, the
  Christian Classics Ethereal Library's Russian collection («Библиотечка православной литературы
  для КПК», compiled by Владимир Шнейдер). Optical reading Олеся Маслова, proofreading Игорь Грушин,
  2004-2007, from the second edition (Институт перевода Библии, Stockholm 1987, a photographic
  reprint of 1904-1913 in three volumes), in modern spelling. Each archive is pinned by its SHA-256
  in `scripts/fetch-commentaries.ps1` and kept as downloaded.

### Who wrote it, and when they died

Signed in the books themselves (the reader moves each signature to the end of its book's
introduction), and named for the rest in the list of contributors (прот. А. Мень, *Библиологический
словарь*, «Толковые Библии»):

| Part | Author as signed or listed | Died |
|---|---|---|
| vol. 1, the Pentateuch | А. П. Лопухин (editor) | 1904 |
| Joshua | проф. Ф. Г. Елеонский (signed) | 1906 |
| Job; 1 Chronicles; Daniel | свящ. А. В. Петровский (signed) | 1929 |
| Ecclesiastes | проф. В. Н. Мышцын (signed) | 1936 |
| Isaiah | проф. А. И. Покровский (signed) | 1940 |
| Jeremiah, Malachi, Baruch, John, Romans, Colossians | Н. П. Розанов (signed) | 1941 (or 1938: two men of the name; either way before 1942) |
| Ezekiel | М. Н. Скабалланович (signed «Н. Скабалланович») | 1931 |
| Amos | проф. В. П. Рыбинский (signed) | 1944 |
| Zechariah | свящ. Д. В. Рождественский (signed) | 1926 |
| Matthew | свящ. М. Фивейский (signed) | 1919 |
| Revelation | прот. Н. М. Орлов (signed) | 1924 |
| 3 Maccabees | еп. Иосиф (Петровых) (signed) | shot 1937, rehabilitated |
| Ruth, 3-4 Kings, Tobit, Proverbs, Song, Nahum, Habakkuk, Zephaniah, Haggai, catholic letters (unsigned) | свящ. А. А. Глаголев (listed) | died in prison 1937, rehabilitated 1989 |
| unsigned elsewhere | В. И. Протопопов (listed) | 1920s or 1938 |
| unsigned elsewhere | В. М. Гавриловский (listed; born 1864) | not before 1923; not known |
| **Jonah** | П. Тычинин (signed) | **not known** |
| **2 Thessalonians** | К. Н. Фаминский (signed) | **not known** |
| **3 Ezra** | Н. Аболенский (signed; Мень's «Н. Оболенский») | **not known** |

### Terms

- **Russia.** Published in the Russian Empire before 7 November 1917: not protected by the Civil Code
  of the Russian Federation (art. 1256; ru.wikisource's PD-RusEmpire reading and the letter of the MVD
  of 6 April 2006 No 3/5862, which Wikisource cites). Every author with a known date also died before
  1943 but Rybinsky (January 1944: life + 70 ended in 2014).
- **Ukraine.** Life + 70 (Law 2811-IX art. 31; an anonymous work 70 years from publication). Art. 31(6)
  runs a posthumously rehabilitated author's term from the year of rehabilitation (Glagolev 1989, which
  would reach 2060), but the transitional rules stop it: the 2022 law applies its terms only where the
  term before it had not ended (Final Provisions 3.1), and the 1994 law (3792-XII, Final Provisions 4)
  applied its terms only where the 50-year term after the author's death had not ended by 1994. For
  Glagolev and Iosif (both died 1937) it ended in 1987, so their parts were free before either law.
  Every author with a known date is free in Ukraine.
- **United States.** Published 1904-1913, before 1930: public domain.
- **European Union.** Life + 70; every author with a known date died before 1956.
- **The three unknowns.** Tychinin (Jonah), Faminsky (2 Thessalonians) and Abolensky (3 Ezra) published
  between 1909 and 1913, and no death year was found for them. Free in Russia and the United States in
  any case; in Ukraine and the EU only if each died before 1956, which is not established. They were
  held back until the owner ruled on FTR-0286 (2026-11-16, «можна грузити»): published 1909-1913, the
  risk is negligible, so they load with the rest.

### The transcription

| Source | What it holds | Terms stated |
|---|---|---|
| ru.wikisource.org, «Толковая Библия Лопухина» | scans only (DjVu on Commons, PD-RusEmpire) and links out | the scans: public domain; no typed text |
| azbyka.ru | typed text of the modern edition («Даръ», 2009), still edited | none for the text; not reachable from Ukraine (blocked) |
| lopbible.ru (lopbible.narod.ru) | was a typed text in modern spelling | the domain now serves casino advertising |
| studylight.org | the Russian text | the site refuses programs that copy it (its bot check says so) |
| **ccel.org/contrib/ru** | **every book, FictionBook, 2004-2007, contributors named** | **none; «сверстаны, главным образом, для личного прочтения» describes, it does not restrict** |

A typed transcription of a public-domain text is not a new work: optical reading and proofreading are
not creative choices (Russian Civil Code art. 1257, Ukrainian Law 2811-IX, the EU's "own intellectual
creation", the US *Feist* rule). The database right (Russian Civil Code arts. 1333-1336, EU Directive
96/9/EC, the Ukrainian sui generis right of 2811-IX) protects a collection of independent items made with
substantial investment for 15 years from its making; a commentary is one work, not a collection of
independent items, and the CCEL files were made in 2004-2007, so any such right would have ended by 2022.
The CCEL files are the source with the clearest standing: fixed, dated, credited, served from the United
States, and the work's own text. Unknown terms are not permission (guide Sources), but here nobody holds a
right to grant: the work is free by age and the typing carries none.

### What the corpus does with it

The books print the Synodal's verses a few at a time with the comment below; a comment opens with the
verses it explains. The corpus keeps each comment over those verses, placed in the shared frame by
finding each printed verse among the corpus's Synodal (RUSV) by its words, so the Synodal's numbering
(the Psalms, Romans 14:24-26, Jonah 2:1 and the rest) never decides a place. The printed verses are the
Synodal's text, which the corpus holds already; they are not written into the comments. Book
introductions, chapter outlines, the author's notes (where the text points at them) and the signatures
are kept; title pages and chapter titles are not. Every word is accounted for (`LopukhinCorpusTests`).

### Attribution as shown to readers

> Толковая Библия, или Комментарий на все книги Священного Писания Ветхого и Нового Завета — А. П.
> Лопухин и его преемники, Санкт-Петербург 1904-1913; распознавание Олеся Маслова, вычитка Игорь Грушин,
> вёрстка Владимир Шнейдер, по собранию Christian Classics Ethereal Library. Public domain.
