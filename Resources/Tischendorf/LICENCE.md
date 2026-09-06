# word-per-line/2.8/Unicode/*.txt — Tischendorf's eighth edition

**Novum Testamentum Graece, editio octava critica maior** (1869–1872), edited by **Constantin von
Tischendorf**; digital edition **release 2.8** (2019), edited by **Dr Ulrik Sandborg-Petersen**,
from **G. Clint Yale's** accented Tischendorf text and **Dr Maurice A. Robinson's** parsed
Westcott-Hort.

From <https://github.com/morphgnt/tischendorf-data> at commit
`795f2f4f9fe7cb98bf8736b0c5cb59c43aa9c32e`, read at the source on **2026-09-06**.

## Where the licence actually is

**The repository has no `LICENSE` file and its top-level `README.md` states no terms at all** — it
says what the data is and where it came from and stops. So the statement closest to the bytes is
the one *inside* the release, and there are three of them, which agree. All are kept verbatim
beside this file.

`word-per-line/2.8/README-short.txt`, on its title page:

> The text and its analysis
> are in the Public Domain.
> Copy freely.

`word-per-line/2.8/README.txt`, the same words, and then where each part of the grant came from:

> Based on G. Clint Yale's Tischendorf text and on Dr. Maurice A. Robinson's Public Domain
> Westcott-Hort text

> Mr. Yale very graciously permitted me to distribute, in the Public Domain, an accentuated version
> based on his later Tischendorf, for which I am very grateful.

And the OSIS export's own header, `OSIS-XML/2.8/tischendorfmorph.OSIS.xml`, which is a fourth file
saying it a third way:

> <rights>Public Domain. Copy freely.</rights>

Note what is being claimed and by whom. **The analysis as well as the text**, by the people who made
the analysis, each of whom is named. There is no ShareAlike, no NonCommercial and no attribution
condition — RUL-0181 is why the three names are recorded here and in the `text` row regardless.

## What is taken

- `word-per-line/2.8/Unicode/*.txt` — the release in NFC UTF-8, one word to a line. This is what is
  loaded.
- `word-per-line/2.8/README.txt`, `README-short.txt`, `parsing.txt` — the grant, and the parse-code
  key.
- `README-upstream.md` — the repository's own README, kept because *what it does not say* is part of
  the reasoning above.

The BETA-encoded copy of the same words and the OSIS-XML export are left where they are; nothing
here reads them.

## Which recension this is

**The text of the eighth edition, without its apparatus.** The apparatus is the reason the eighth
edition is the one anybody cites, and it is in no free machine-readable copy this project could
find, so the corpus can show what Tischendorf read and never his evidence for it. `README.txt` is
explicit about the base: Yale's first Tischendorf (1997) carried no diacritics, punctuation or
apparatus; his later one carried diacritics, punctuation and the apparatus; this release is an
accented text based on the later one. Only the text came across.

That it is the **eighth** and not an earlier Tischendorf is readable off the text, and was checked
rather than taken from the filename. The eighth is where Tischendorf weighed the Sinaiticus he had
found, and this file reads accordingly, against the Received Text *and* against Westcott and Hort:

| Place | This file | Westcott–Hort | Received Text |
|---|---|---|---|
| Mark 1:1 | *(no* υἱοῦ θεοῦ*)* | `[υἱοῦ θεοῦ]` | υἱοῦ τοῦ θεοῦ |
| John 1:18 | ὁ μονογενὴς **υἱός** | μονογενὴς **θεός** | ὁ μονογενὴς υἱός |
| Luke 22:43–44 | printed | printed in double brackets | printed |
| Acts 20:28 | τὴν ἐκκλησίαν τοῦ **κυρίου** | τοῦ **θεοῦ** | τοῦ θεοῦ |
| 1 Thessalonians 2:7 | **ἤπιοι** | **νήπιοι** | ἤπιοι |
| Matthew 1:7–8 | Ἀσάφ | Ἀσάφ | Ἀσά |
| 1 Timothy 3:16 | ὅς | ὅς | θεός |
| Matthew 6:13, Acts 8:37, 1 John 5:7 comma, Luke 24:12 | absent | absent (24:12 double-bracketed) | present |

The last row says it is a critical text at all; the rows above it say it is Tischendorf's and not
Westcott and Hort's.

## The pericope adulterae is printed twice

Tischendorf gives John 7:53–8:11 in two forms, and this file carries both **at the same verse
numbers** — first the divergent text of Codex Bezae, unaccented and with its itacisms
(`παραγεινεται`, `κατακρεινεν`), then the ordinary form, accented like the rest of the edition.
That is 166 words and 154 addresses written twice, and it is the only place in the 27 books where
any address repeats.

The corpus holds one verse per address, so **the second, ordinary form is loaded** and the Bezan one
is not. The reader refuses any other doubled address rather than making the same choice somewhere
nobody has looked.

## The tagging is Robinson's, ported — which is not independent testimony

`README.txt` says the morphology, Strong numbers and lemmas were carried over from Robinson's
Westcott-Hort by a program, with about 10,740 words left over and about 900 analysed by hand. The
corpus can see the join: σου, σε, σοί and σύ all carry **G4771** here exactly as they do in
Robinson's Westcott-Hort, his two Textus Receptus editions and the Byzantine Textform — including
the same four exceptions at σοί = G4674 — where Strong's own dictionary numbers them G4675, G4571
and G4671. That is a shared idiosyncrasy, not a coincidence.

So where this edition and the Westcott-Hort agree about a *parse*, one of them was copied from the
other, and the agreement is not two witnesses. Where they disagree about a *word*, they are two
editions, and that is what the corpus loads them for.

The port was not blind. The plural pronouns were left on Strong's own numbering — **G5210** ὑμεῖς
and **G2249** ἡμεῖς where every other Greek text here writes G4771 and G1473 — along with a dozen
comparatives and adverbs Strong entered separately from their positive. That is 2,760 words whose
number nothing else in the corpus states; `Strong/GreekLemmaNumbers.cs` records the twelve pairs the
corpus itself corroborated and deliberately leaves three unmapped.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader of this corpus has to be able to tell
what rests on somebody's testimony and what rests on our inference, and a fact printed without a
name has quietly been claimed as ours. RUL-0181.
