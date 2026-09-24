# *.txt — Swete's Septuagint, Cambridge 1887–1894

**The Old Testament in Greek according to the Septuagint**, edited by **Henry Barclay Swete**
(1835–1917), Regius Professor of Divinity at Cambridge, published by **Cambridge University Press**
in three volumes between 1887 and 1894. A *diplomatic* edition: Codex Vaticanus reproduced as it
stands, its losses supplied from Sinaiticus and Alexandrinus, and the readings of the other uncials
kept in an apparatus at the foot of the page rather than allowed into the text. The apparatus is
not in these files; only the text is.

From <https://github.com/nathans/lxx-swete>, pinned to commit
`26bad3eb42bba98471d154c954e36a6f30a0279d` (18 December 2025) and read at the source on
**2026-09-06**. `scripts/fetch-swete.ps1` is what fetches it, and it re-reads all three statements
below before it replaces anything.

The files are one token per line — `10.1.1 ΚΑΙ` — where the number is the work's number in the TLG
catalogue of the Septuagint, not a canonical ordinal. **55 files, 588,579 tokens, 11.3 MB.** The
corpus loads 51 of them, and Isaiah from First1KGreek's own TEI (below), as **52 books, 1,107
chapters, 28,533 verses, 570,385 words**. No annotation of any kind: no morphology, no lemmas, no
Strong numbers, no alignment to anything.

## The licence: on the transcription, not on the text

Two questions with different answers, and this is the one place they should not be run together.

**Swete's edition is out of copyright everywhere.** He died in 1917 and the volumes were printed
between 1887 and 1905, so the text is free of anyone's permission on life-plus-seventy and on the
United States' 1929 line alike. Nobody holds it.

**The digitisation is licensed.** Open Greek and Latin photographed and corrected the pages for its
First1KGreek project, and Nathan D. Smith converted that TEI into these files. Both state Creative
Commons Attribution-ShareAlike 4.0, and both statements are kept beside this file.

**1. `README.md`, from the repository the files come from**, kept beside the data:

> The Greek text and its annotations in the data directory are published under the terms of the
> [Creative Commons Attribution-ShareAlike 4.0
> International](https://creativecommons.org/licenses/by-sa/4.0/) (CC BY-SA 4.0) license.
>
> The source code for building the data is published under the terms of the MIT License (see
> "COPYING-Code").

`COPYING-Code` is kept beside the data too, and is the MIT licence over the build scripts —
`Copyright 2015, 2017, 2019 Nathan D. Smith <nathan@smithfam.info>`. **It does not cover the text**,
and reading a repository's only `LICENSE`-shaped file as the licence of its data is the single most
common way to get this wrong: DOC-0181 records six ETCBC repositories where exactly that mistake is
available.

**2. First1KGreek's own `license.md`**, the full CC BY-SA 4.0 legal code, kept beside the data as
`first1kgreek-license.md` and read at
<https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/master/license.md>. Its heading:

> ## creative commons
>
> # Attribution-ShareAlike 4.0 International

**3. First1KGreek's Zenodo record**, `.zenodo.json` in the same repository:

> `"license": "CC-BY-SA-4.0"`, `"title": "First1KGreek"`, creators Gregory R. Crane, Leonard
> Muellner, Bruce Robertson, Alison Babeu, Lisa Cerrato, Thomas Koentges, Rhea Lesage, Lucie
> Stylianopoulos, James Tauber, all of the Open Greek and Latin Steering Committee.

All three agree, which is not the usual outcome and is worth recording as such.

## ShareAlike, and why this is here anyway

RUL-0183 makes ShareAlike the line this project does not cross, and this text is on the far side of
it. It is loaded on the owner's decision of 2026-09-06, recorded in the last comment of PRB-0344:
the rule rests on a reading of ShareAlike that Creative Commons contradicts — the condition attaches
to *adaptations* and expressly not to *collections* — and the owner ruled that what was excluded on
that reading may come back. The rule text is unedited and the decision is the owner's; this file
records the licence exactly as it arrives and does not soften it.

What the obligation actually is: **credit, and share alike anything derived from these files.** It
attaches per artefact. A page showing this text beside another is a collection and not an
adaptation; the folded search form of *these* words is an adaptation of *this* text and of no
other; a link row holding two word ids and a method contains no text at all.

## Attribution

- **Henry Barclay Swete**, editor, *The Old Testament in Greek according to the Septuagint*,
  Cambridge University Press, 1887–1894. Transcribed from the later printings the First1KGreek
  files name: volume 1 (Genesis–4 Kings) 1901, volume 2 (1 Chronicles–Tobit) 1896, volume 3
  (Hosea–4 Maccabees, Psalms of Solomon, Enoch, the Odes) 1905.
- **Open Greek and Latin / First1KGreek** (`tlg0527`), the transcription. CC BY-SA 4.0.
- **Nathan D. Smith** (`nathans/lxx-swete`), the one-token-per-line edition loaded here.
  CC BY-SA 4.0.
- **Richard Rusden Ottley**, editor, *The Book of Isaiah according to the Septuagint (Codex
  Alexandrinus)*, volume 2, Cambridge University Press, 1904 — the text `OTTLEY` — in the same
  First1KGreek transcription (`tlg0527.tlg048.1st1K-grc2`, University of Leipzig). CC BY-SA 4.0.

The `text` rows carry the same, in `Essenthos.Forge/Loading/SweteTextSource.cs` and
`OttleyTextSource.cs`. RUL-0181: a fact
printed without a name has quietly been claimed as ours.

## What is in the folder and not in the corpus

**`48.Isaias.txt` is not Swete's, and nothing in the repository says so.** First1KGreek holds two
Greek editions of Isaiah — Swete's (`grc1`) and **Richard Rusden Ottley**'s *The Book of Isaiah
according to the Septuagint (Codex Alexandrinus)*, Cambridge 1904 (`grc2`) — and `utils/build.sh`
globs `tlg0527*grc*xml` and names its output after the *book*, so for the one work with two Greek
editions the second overwrote the first. The file is Ottley's: it opens with the manuscript's title
as Ottley prints it (`προφήτης ιγ΄`, which is in `grc2` and not in `grc1`) and spells Uzziah `Ὀζίου`
where Swete spells him `Ὀζείου`. Reading it would put Codex Alexandrinus into the corpus under
Vaticanus's name. It is fetched so a reader can check this, and it is not read: both Isaiahs are
read from First1KGreek's TEI instead (next section). Sirach is the only other work with two Greek
editions and there the ordering favours us: `grc2` is Swete's and `grc1` is Hart's 1909 edition, so
the file that survives is the right one.

**`28.Odae.txt`** — the Odes, whose chapters this edition numbers `iva` and `ivb` where the corpus
addresses a chapter by an integer, and whose verses keep the numbering of the passages they are
taken from (Ode 11 runs from verse 46 because it is Luke 1:46–55). Renumbering them would be
inventing an arrangement Swete does not print.

**`54.Susanna_translatio_Graeca.txt`, `56.Daniel_translatio_Graeca.txt`,
`58.Bel_et_Draco_translatio_Graeca.txt`** — the Old Greek of the three books Swete prints in two
versions. Vaticanus reads Theodotion in all three and Vaticanus is what this edition is, so the
Theodotion files are the text of this witness and the Old Greek is another manuscript's reading:
a second witness, which the model holds as a text of its own rather than as a second book at one
ordinal. 12,759 tokens, waiting for a text row of their own.

**Sirach's preface**, 237 tokens, printed before chapter 1 and numbered as neither a chapter nor a
verse. The corpus addresses text by chapter and verse and has no address for a page outside the
book's numbering; Brenton omits it as well.

**Ecclesiastes does not exist upstream.** First1KGreek's `tlg0527` has no `tlg030`, so this edition
supplies 38 of the 39 books of the Hebrew canon. The corpus reads Greek Ecclesiastes from Brenton.

## `First1KGreek/` — Isaiah, twice, from the transcription itself

Two files fetched from <https://github.com/OpenGreekAndLatin/First1KGreek>, pinned to commit
`b67137e6b82669d08fe6ad1c225999ca6aca362c` (19 December 2024, the last commit to change either) and
read at the source on **2026-09-24**, on the owner's approval of that day. They are kept under this
project's own names, in a folder of their own, so that neither the clearing of this folder by
`scripts/fetch-swete.ps1` nor a file named by the upstream build can overwrite them; the script
fetches them again, checking the size and the licence below.

| Kept as | Upstream | Bytes | What it is |
|---|---|---|---|
| `isaiah-swete-1905.xml` | `data/tlg0527/tlg048/tlg0527.tlg048.1st1K-grc1.xml` | 688,809 | Swete, volume 3, Cambridge University Press 1905 |
| `isaiah-ottley-1904.xml` | `data/tlg0527/tlg048/tlg0527.tlg048.1st1K-grc2.xml` | 520,194 | Ottley, volume 2, Cambridge University Press 1904 |

**Each states its licence in its own TEI header**, which is the statement attached to these bytes
and the one read here (RUL-0105). Both read, in `publicationStmt/availability/licence`, with a target
of `https://creativecommons.org/licenses/by-sa/4.0/`:

> Available under a Creative Commons Attribution-ShareAlike 4.0 International License

That agrees with First1KGreek's `license.md` and Zenodo record quoted above. Swete's file names
Harvard College Library as publisher (2017) and Digital Divide Data as having corrected and encoded
the text, under Gregory Crane; Ottley's names the University of Leipzig (2014), the same encoders, and
Gregory R. Crane and Monica Berti as principals. The printed texts are out of copyright: Swete's for
the reason given above, Ottley's because it was printed in 1904.

**Swete's Isaiah** is read into the edition as its fifty-second book, after the Twelve where Swete
prints it. **Ottley's is a text of its own**, `OTTLEY`, on the owner's decision of 2026-09-24: it is
Codex Alexandrinus, a different manuscript from the Vaticanus Swete prints, and the only book of that
codex the corpus holds.

**What this project changes, and why ShareAlike does not reach further than that.** The conversion
from TEI to words is this project's (`Essenthos.Forge/Swete/First1KGreekReader.cs`) and reproduces
the upstream converter's output for `grc2` token for token apart from its brackets. On top of it a
short list of repairs is made, each with what establishes it, in `SweteIsaiah.cs` and
`OttleyIsaiah.cs` beside the reader: verse divisions the transcription lost (thirteen in Swete, ten
in Ottley — at the verse number the page prints, which the transcription let into the text or kept
as a line mark), two misnumbered verses of Swete's 38 renumbered and the end of his 31:9 given back
to it, Latin letters standing for the identical Greek ones, two placeholders taken out of Ottley and
his manuscript's title and colophon kept out of 1:1 and 66:24, and one line of Ottley's 2:20 read in
its place. Misread letters are left as the transcription reads them. That is a modification of a
CC BY-SA text, so each text's rights note says it is modified, and the modified text is shared under
the same licence — which is all ShareAlike asks of it. It does not reach the annotation: a letter
link or a verse link holds two row ids and a method and no text, and a folded search form is derived
from these words and of no other text (RUL-0183).

**Two verses Ottley does not number, and nothing supplies them.** Ottley numbers by the Hebrew and
gives no number where the Greek has no counterpart: there is no 38:15 (its few words close 38:14)
and no 40:7 (the Greek has the Hebrew's 40:7–8 once, as 40:8). Neither the page nor the file's line
marks number them, where every division repaired above is numbered, so they are the edition's own
omissions and are left. Every Greek Isaiah here lacks 2:22 and 56:12.

## How it is numbered, and where that differs from Brenton

The verse division is Swete's own and not Brenton's, which is the point of holding both. Of the 50
books the two editions share, **43 divide their text differently somewhere** and 7 agree exactly.
The structural ones:

| | Swete | Brenton |
|---|---|---|
| Joel | 3 chapters | 4 — Swete runs 2:28–32 into chapter 2, Brenton gives them a chapter |
| Malachi | 4 chapters | 3 — and the other way round |
| Proverbs | 29 chapters, the last of 49 verses | 31; 10 of the shared chapters divided differently |
| Sirach | 51 chapters, 1,369 verses | 51, 1,370; 11 chapters divided differently |
| Psalms | 151, the title unnumbered | 151, the title numbered as verse 1; 12 psalms divided differently |
| Epistle of Jeremiah | 72 verses, the superscription unnumbered | 73, numbering it |
| Bel and the Dragon | 36 verses | 42 |
| 1 Esdras | 430 verses | 448 |

Esdras B is Ezra and Nehemiah under one heading in both, twenty-three chapters, and both are split
on load at chapter 10.

**The one repair made on the way in.** The converter that produced these files carries a current
chapter and a current verse and changes neither until a numbered division opens, so text printed
inside a chapter but before its first numbered verse — a psalm's title, the prologue of Lamentations,
the heading of Obadiah — comes out under the last verse number of the chapter before. 107 places.
Each is put back at the head of the verse it stands before, which is what the TEI encodes and what
the page shows; checked against Brenton, who numbers a psalm's title as verse 1, and after the
repair the two editions hold the same words at the same address.
