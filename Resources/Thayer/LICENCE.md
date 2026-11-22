# Thayer, A Greek-English Lexicon of the New Testament (1889), as the Every Promise project published it

Fetched by `scripts/fetch-thayer.ps1` (action `fetch-thayer`) from <https://github.com/imbennyhim/Every-Promise-Thayers>,
commit `a31ff38c40a681ef1029dcc51eb7e7ee3f2ea409` (2026-08-04), on 2026-11-22.

| stored file | what it is | SHA-256 |
|---|---|---|
| `book/01-G1-G224.md` to `book/27-G5525-G5624.md` | the 5,521 entries G1 to G5624 in Strong's-number order, 27 files of Markdown: a heading with the Greek headword and a transliteration, the line `Strong's G<n>`, then the article | listed in `scripts/fetch-thayer.ps1` |
| `LICENSE` | the CC0 1.0 Universal legal code | `a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499` |
| `NOTICE` | the statement of what the text is and under what terms | `c1c8c8d53d412e4b207d397b5f11ad6c4c2ba73a47859c1b1c52718a00e51aab` |
| `README.md` | the repository's README | `03f61f08df7674ff438c24819a8b22672a289efc8a4dffbe99b0fef41c859698` |

The repository's `thayers.html` (the same text on one page) and `book/README.md` (a table of contents) are not taken.

## Terms read from the source

The NOTICE, read at that commit on 2026-11-22:

> The underlying lexicon and the Strong's numbering are both well over a century old and are in the public domain
> worldwide. This edition is a faithful digital reproduction of that public-domain text.
>
> To the extent that any rights subsist in the compilation, formatting, or markup added in producing this edition, they
> are dedicated to the public domain under the Creative Commons CC0 1.0 Universal Public Domain Dedication.

The README says the same ("offered in the public domain (CC0) ... Use it freely, including commercially"), and GitHub
reads the repository's licence as CC0-1.0.

## What it is, and what is not known about it

- The work is Joseph Henry Thayer, *A Greek-English Lexicon of the New Testament* (the corrected edition, 1889), a
  translation and enlargement of Wilibald Grimm's *Clavis Novi Testamenti*. Thayer died in 1901; Strong's numbering is of
  1890. Both are public domain.
- The digital text is **decoded from an e-Sword module**, a community digitisation, not typed or scanned by this
  edition's author. The README says so ("decoded from a community digitization"); the project's own tool
  (`tools/build_thayer.py` in `imbennyhim/Every-Promise-Bible`) reads the module `Thayers Unabridged.dctx`, an SQLite
  file. **The terms of that module, and who typed it, are not stated anywhere this edition or its tool says.** The
  owner accepted the edition on these terms on 2026-11-22 («Тоді давай e-Sword») rather than have the 1889 print
  scanned. What the edition adds beyond the public-domain text (its cleaning into Unicode, its Markdown) is CC0 as its
  author states.
- The text carries the artefacts of that digitisation, which the edition itself admits ("transcription artifacts
  survive from the source"): Thayer's italics are lost (`Latinin-` for *Latin* in-), an iota subscript or other mark
  sometimes stands as `<` (`πρωι<`), a few Hebrew and Syriac words are in the Online Bible's old encoding (`)rYha`), and
  some articles open with the module's own label `STRONGS NT <n>:` and the headword twice over.

## What is served

Each article under its Strong number (`lexicon_article`, dictionary `thayer`), credited to Thayer and the Every Promise
edition (dataset `thayer`). The article is shown as the edition has it, except:

- the label `STRONGS NT <n>:` and the second copy of the headword that follows it, which are the module's wrapper, not
  Thayer's text;
- **107 entries are left out.** 97 of them are not Thayer's articles but a card of Strong's-style fields (`- Original:`,
  `- Transliteration:`, `- Phonetic:`, `- Definition:` with a brief numbered gloss, `- Origin:`, `- TDNT entry:`,
  `- Part(s) of speech:`), mostly for inflected forms and phrases Thayer has no article on (G149 αἰσχρόν, G1511 εἶναι,
  G5124 τοῦτο). A brief gloss under Thayer's name would misattribute it, so those numbers have no Thayer tab. The other 10 (G68, G671, G676, G1463, G2252, G3190, G3991, G4877, G5104, G5163) were read against Strong's lemma of each number on 2026-11-22 and hold the article of a neighbouring word, the module repeating the previous number's article or filing an alphabetical neighbour's: G671 ἀπόχρησις holds ἀποχωρέω's, G3190 Μελεᾶς holds μέλας's. A number has no Thayer article rather than another word's;
- the module also files, under a Strong number, entries it numbers between Strong's numbers (`STRONGS NT 255a:`, 140 of them, for words Strong's numbering lacks, such as ἄλφα after G255). They belong to no number and are left out;
- verses in the New Testament (`Mat 20:8`) are linked to the reader; those in the Old Testament and the Apocrypha are not,
  because the edition does not say whether it numbers them as the Hebrew, the Septuagint or the English Bible does.
