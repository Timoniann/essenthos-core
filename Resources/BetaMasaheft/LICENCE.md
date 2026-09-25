# LIT*.xml — the Ethiopic (Ge'ez) Bible, all 81 books, from Beta maṣāḥǝft

**Beta maṣāḥǝft: Manuscripts of Ethiopia and Eritrea**, the Hiob-Ludolf-Zentrum für Äthiopistik of
the University of Hamburg, funded by the **Akademie der Wissenschaften in Hamburg** (general editor
Alessandro Bausi), keeps every Ethiopic work it describes as one TEI file in
<https://github.com/BetaMasaheft/Works>. These are the 81 files that carry the books of the Ethiopian
Orthodox Tewahedo canon — the narrow reckoning every printed 81-book Bible follows, 54 books of the
Old Testament and 27 of the New.

Pinned to commit `1d96713016d8c11becc50a3287c955b4cb67a5d1` (24 September 2026) and read at the
source on **2026-09-25**; the owner approved this download of 81 single files on 2026-09-25
(FTR-0751). `scripts/fetch-betamasaheft.ps1` fetches them, checks each file's size at the commit, and
refuses a file whose header no longer states the licence below. Nothing else from the repository is
taken: it holds some six thousand other works, and it has no licence file at its root — the licence
is stated in each file.

**81 files, 14.3 MB. The corpus reads them as 81 books, 1,557 chapters, 38,484 verses, 506,545
words** (the text `GEEZ81`). No annotation of any kind: no lemmas, no morphology, no alignment to
anything. Research and sources: DOC-0208, §7.

## The licence

**Every file** states, in its `<availability><licence>`:

> This file is licensed under the Creative Commons Attribution-ShareAlike 4.0.

Fifteen of the files read add a statement about the transcription inside them, quoted here as each
file gives it, because the notices ask to be kept with every copy.

**Ran HaCohen's**, on Genesis to Ruth, the four books of Kingdoms and the Psalter (and, in the
Jubilees and Ecclesiastes files, on the other edition, which is not read):

> The copyright of the text transcription is of Ran HaCohen and is published also at B I B L I A -
> VETERIS TESTAMENTI AETHIOPICA

HaCohen's own site (<https://www.tau.ac.il/~hacohen/Information.html>, read 2026-09-24 for DOC-0208)
states the terms: *"permission to use, copy, and distribute for any NONCOMMERCIAL purpose is granted
without fee, provided that the copyright notice Ran HaCohen and this permission notice appear in all
copies"*. The `editionStmt` of these files reads *"OCTATEUCHUS © Digitalizavit Ran HaCohen"*,
*"LIBRI REGUM © Digitalizavit Ran HaCohen"* and *"PSALTERIUM DAVIDIS Ed. Hiob Ludolf ©
Digitalizavit Ran HaCohen"*.

**Michal Jerabek's**, on Wisdom and 4 Baruch (and, in the Enoch file, on the other edition, which is
not read):

> The text within has the following copyright. 1995 Library of Ethiopian Texts, created and
> maintained by Michal Jerabek, Prague. Permission to use, copy, and distribute this text, for any
> NONCOMMERCIAL purpose is hereby granted without fee, provided that the copyright notice and this
> permission notice appear in all copies of this text. This text cannot be sold under any
> circumstances.

**How it is read (RUL-0105, RUL-0183).** The most restrictive statement on the bytes of the church's
text is CC BY-SA 4.0. On the fifteen older books it is the non-commercial notice, which RUL-0183
accepts; ShareAlike binds an adaptation, and serving the text beside the others is not one. The text
row records `CC-BY-SA-4.0` and ShareAlike, and its rights note says which books carry the
non-commercial notices. The editions themselves are out of copyright: Ludolf died in 1704 and
Dillmann in 1894.

**The church's rights.** The files for the other 66 books say only that the text is *"of the EOTC
printed Bible"* or was *"digitized … from modern Vulgata printed editions"*. None names the printing,
and none carries a statement from the Ethiopian Orthodox Tewahedo Church or the Bible Society of
Ethiopia. Nobody has asked them. Ethiopia is not party to the Berne Convention; see DOC-0208 §7.5.
Eight files say nothing about their source, in the edition or in the header — 1 and 2 Chronicles,
Job, Isaiah, Jeremiah, 2 and 3 John and Jude — and are taken to be the same printing as their
neighbours, which is inferred, not stated.

## What is read, and how

Each book is one file. Where a file holds two editions, the church's is read and the other is not:
VanderKam's Jubilees (copyrighted) and Mercer's Ecclesiastes (copyrighted) are left out, and so are
Dillmann's Enoch, which the file holds only as whole chapters with no verses, and a Song of Songs
divided into five parts with no verses. Four books expected from the older editions are the church's
text here, because that is all their files hold: Sirach, Amos, Joel and Obadiah. So the text is
**66 books of the church's printed Bible and 15 of the older editions**, not 61 and 20.

- **Only a chapter's numbered lines are verses.** The New Testament files set the printed Bible's
  section headings between verses as unnumbered lines, and carry the prefaces, chapter lists and
  subscriptions of the manuscripts in parts that are not chapters (Acts opens with a preface, a list
  of 68 titles and a note on Luke). None of it is read. In the Psalter an unnumbered line is the
  second half of the verse before and is kept there, and a psalm's title opens its first verse.
- **The numbering is read as a sequence**, because it was typed by hand: a number repeated, or a
  stray lower number on the second half of a broken line, continues the verse before (57 lines in
  all); a line whose neighbours say exactly one verse is missing is that verse (3 lines); a forward
  jump the next line carries on from is verses the file lacks. Every word of every line is kept; a
  line holding only a full stop or two dots — 1 Esdras 8:20 and two verses of Ezra Sutuel, a verse
  the typist did not reach — is read as absent.
- **Two lines are moved**, each checked against its opening words: the last line of 1 Samuel 31 is
  2 Samuel 1:1, which the 2 Samuel file lacks, and the last line of Matthew 7, numbered 24, is 7:29.
- **Where the file's numbers are not the frame's**, the verse stands at the frame's address and says
  what the edition numbers it: 3 Ezra (the Greek 1 Esdras) and Ezra Sutuel (the Latin 4 Ezra) leave
  their first chapter empty and begin at 2, so they are moved one chapter down and one up; the Letter
  of Jeremiah and Bel (Daniel 14) count from verse 0 and are counted from 1.
- **Chapters only.** 4 Baruch is typed as nine chapters with no verse numbers; each chapter is one
  passage. Jubilees' prologue has no chapter number and is left out, as Swete's preface to Sirach is.
- **Divided in its own way, joined to nothing verse by verse:** Esther (13 chapters, in no numbering
  the versification data knows), the Letter of Jeremiah (43 church verses for the Greek 72; the text
  is complete, but no verse map exists and none is invented) and Dillmann's Wisdom (321 verses for the
  Greek 436). They are placed at their own numbers. Enoch, Jubilees, 1–3 Meqabyan, 4 Baruch, Messale
  and Tägsas have ordinals of their own (85–92) and no counterpart in the corpus.
- **Proverbs** is two books, as the church counts it. By their chapter openings Messale is Proverbs
  1–24 and 30 (its chapters 25 and 26 open at Proverbs 30:4 and 30:15) and Tägsas is Proverbs 25–29
  and 31:10–31; verse by verse this is not yet established, so neither is placed at Proverbs.
- **Typing errors are in the text**, and are left as typed: DOC-0208 §7.3 counts four slips in the
  first twelve verses of Jeremiah. Daniel 13 (Susanna) holds 10 verses and Daniel 14 (Bel) 2, which is
  as far as the file goes.

## Per book

"Edition read" is the `xml:id` of the `<div type="edition">` loaded. "Note in the edition" is the
`<note>` inside that div; "`editionStmt`" is the file header's; both are as the file gives them, cut
at 70 characters.

| # | Book | File | Edition read | Source | Note in the edition | `editionStmt` | Beyond CC BY-SA |
|---|---|---|---|---|---|---|---|
| 1 | Genesis | `LIT1546Genesi.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 2 | Exodus | `LIT1367Exodus.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 3 | Leviticus | `LIT1793Leviti.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 4 | Numbers | `LIT2075Number.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 5 | Deuteronomy | `LIT2637Deuteronomy.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 6 | Joshua | `LIT1696Joshua.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 7 | Judges | `LIT1700Judges.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 8 | Ruth | `LIT2229RuthBo.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 9 | 1 Samuel (1 Kingdoms) | `LIT2697Sam.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | LIBRI REGUM © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 10 | 2 Samuel (2 Kingdoms) | `LIT2698Sam.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | LIBRI REGUM © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 11 | 1 Kings (3 Kingdoms) | `LIT2699Kings.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | LIBRI REGUM © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 12 | 2 Kings (4 Kingdoms) | `LIT2700Kings.xml` | the only one | Dillmann 1853–71, digitised by Ran HaCohen | — | LIBRI REGUM © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 13 | 1 Chronicles | `LIT3499Chroni.xml` | the only one | church's printed Bible | — | — | — |
| 14 | 2 Chronicles | `LIT3500Chroni.xml` | the only one | church's printed Bible | — | — | — |
| 15 | Jubilees | `LIT1697Jubilees.xml` | `EOTCed` (of 2) | church's printed Bible | standard printed EOTC Bible edition | OCTATEUCHUS © Digitalizavit Ran HaCohen | HaCohen's copyright, on the other edition |
| 16 | 1 Enoch | `LIT1340EnochE.xml` | `EOTCed` (of 2) | church's printed Bible | Text of the EOTC printed Bible | Text in Edition 1 follows August Dillmann’s 1851 critical edition, dig… | Jerabek's notice, on the other edition |
| 17 | Ezra | `LIT3581Bookof.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 18 | Nehemiah | `LIT1374Bookof.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 19 | 3 Ezra (1 Esdras) | `LIT1376Apocal.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 20 | Ezra Sutuel (4 Ezra) | `LIT1377Bookof.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 21 | Tobit | `LIT2473TobitB.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 22 | Judith | `LIT1701Judith.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 23 | Esther | `LIT1362Esther.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized by partner project… | — |
| 24 | 1 Meqabyan | `LIT1819Maccab.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 25 | 2 Meqabyan | `LIT5840SecondEthioMaccabees.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 26 | 3 Meqabyan | `LIT5839ThirdEthioMaccabees.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 27 | Job | `LIT1688Job.xml` | the only one | church's printed Bible | — | — | — |
| 28 | Psalms | `LIT2000Mazmur.xml` | the only one | Ludolf 1701, digitised by Ran HaCohen | — | PSALTERIUM DAVIDIS Ed. Hiob Ludolf © Digitalizavit Ran HaCohen | HaCohen's copyright on the transcription |
| 29 | Messale | `LIT3927Messale.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 30 | Tägsas | `LIT2396Tagsas.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 31 | Wisdom | `LIT2516Wisdom.xml` | `ed1` (of 2) | Dillmann, typed by Michal Jerabek 1995 | — | Text in Edition 1 follows August Dillmann’s 1851 critical edition, dig… | Jerabek's non-commercial notice |
| 32 | Ecclesiastes | `LIT1320Eccles.xml` | `EOTCed` (of 2) | church's printed Bible | Text of the EOTC printed Bible | The text reproduced in Edition 1: This version follows Mercer's editio… | HaCohen's copyright, on the other edition |
| 33 | Song of Songs | `LIT2362Songof.xml` | `EOTCed` (of 2) | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 34 | Sirach | `LIT2358Sirach.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 35 | Isaiah | `LIT1672Isaiah.xml` | the only one | church's printed Bible | — | — | — |
| 36 | Jeremiah | `LIT1685Bookof.xml` | the only one | church's printed Bible | — | — | — |
| 37 | Baruch | `LIT1202Bookof.xml` | `edEOTC` | church's printed Bible | This text is from the standard EOTC printed Bible edition | The text provided below has been digitized by partner projects from mo… | — |
| 38 | Lamentations | `LIT1753Lament.xml` | the only one | church's printed Bible | the text of the printed EOTC Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 39 | Letter of Jeremiah | `LIT1686Epistl.xml` | `edEOTC` | church's printed Bible | This text is taken from the modern standard Ethiopian printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 40 | 4 Baruch | `LIT2167Parali.xml` | `MJer` | Dillmann, typed by Michal Jerabek 1995 | Transcription by Michael Jerzabek, see | The text was digitized in 1995 by Michal Jerabek, Prague, in the “Libr… | Jerabek's non-commercial notice |
| 41 | Ezekiel | `LIT5802EzekII.xml` | the only one | church's printed Bible | the printed EOTC Bible version | The text provided in this record has been digitized from modern Vulgat… | — |
| 42 | Daniel | `LIT3529Daniel.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible; cp. also http://mermru.com/bibles/42/4… | The text provided below has been digitized by partner projects from mo… | — |
| 43 | Hosea | `LIT3144Hosea.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 44 | Amos | `LIT3145Amos.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 45 | Micah | `LIT3146Micah.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 46 | Joel | `LIT1689Joel.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | — | — |
| 47 | Obadiah | `LIT3147Obadiah.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 48 | Jonah | `LIT1694Jonah.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 49 | Nahum | `LIT2057Bookof.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 50 | Habakkuk | `LIT1567Bookof.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 51 | Zephaniah | `LIT3148Zephan.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 52 | Haggai | `LIT3149Haggai.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 53 | Zechariah | `LIT3150Zechar.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 54 | Malachi | `LIT3151Malachi.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 55 | Matthew | `LIT2709Matthew.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 56 | Mark | `LIT2711Mark.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 57 | Luke | `LIT2713Luke.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 58 | John | `LIT2715John.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 59 | Acts | `LIT1019Actsof.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 60 | Romans | `LIT3515Epistle.xml` | the only one | church's printed Bible | The following is a preliminary division of the Epistle to the Romans i… | The text provided below has been digitized by partner projects from mo… | — |
| 61 | 1 Corinthians | `LIT3516Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 62 | 2 Corinthians | `LIT3517Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 63 | Galatians | `LIT3518Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 64 | Ephesians | `LIT3519Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 65 | Philippians | `LIT3520Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 66 | Colossians | `LIT3521Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 67 | 1 Thessalonians | `LIT3522Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 68 | 2 Thessalonians | `LIT3523Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 69 | 1 Timothy | `LIT3525Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 70 | 2 Timothy | `LIT3526Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 71 | Titus | `LIT3527Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 72 | Philemon | `LIT3528Epistle.xml` | the only one | church's printed Bible | All excerpts are taken from . | — | — |
| 73 | Hebrews | `LIT3524Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 74 | James | `LIT3512Epistle.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 75 | 1 Peter | `LIT3507Epistle.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided in this record has been digitized from modern Vulgat… | — |
| 76 | 2 Peter | `LIT3508Epistle.xml` | the only one | church's printed Bible | Text of the EOTC printed Bible | The text provided below has been digitized by partner projects from mo… | — |
| 77 | 1 John | `LIT3509Epistle.xml` | the only one | church's printed Bible | — | The text provided below has been digitized by partner projects from mo… | — |
| 78 | 2 John | `LIT3510Epistle.xml` | the only one | church's printed Bible | — | — | — |
| 79 | 3 John | `LIT3511Epistle.xml` | the only one | church's printed Bible | — | — | — |
| 80 | Jude | `LIT3513Epistle.xml` | the only one | church's printed Bible | — | — | — |
| 81 | Revelation | `LIT3179Revela.xml` | the only one | church's printed Bible | — | The text provided in this record has been digitized from modern Vulgat… | — |
