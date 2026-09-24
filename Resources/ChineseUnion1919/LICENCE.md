# ChiUn, ChiUns — the Chinese Union Version of 1919

**和合本**, the Chinese Union Version, published in 1919 by the missionary societies working in China
after the 1890 conference agreed on one Mandarin translation. It is the Bible most Chinese Protestants
still read. Loaded twice, as `CUV` (traditional characters, `ChiUn/`) and `CUVS` (simplified,
`ChiUns/`).

From CrossWire's SWORD packages, read at the source on **2026-09-25**:
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/ChiUn.zip` (SHA-256
`f03e7e5f…38676`) and `…/ChiUns.zip` (`37fcef08…4857e`), module version 3.1 of 2023-10-28.
`scripts/fetch-crosswire.ps1` fetches both, checks those hashes and re-reads FHL's statement
before it replaces anything.

**66 books, 31,101 verses, 405,940 words** in each script, of which **376,226** carry a Strong
number in the module. John 7:53 has no text of its own: FHL prints it at the head of 8:1.

## Which text this is, and which it is not

Two digital Union Versions call themselves public domain, and only one is the 1919 text.

- **eBible `cmn-cu89s` / `cmn-cu89t`** is titled 新标点和合本 / 新標點和合本: the Hong Kong Bible
  Society's *new punctuation* edition of 1988, which is © that society (its 〈聖經版權使用及申請〉
  allows 500 verses without asking). The text is that edition's, not the 1919 printing's: Genesis 49:3
  reads **吕便**, the 1988 edition's name for Reuben, where the 1919 has **流便** — FHL's own list of
  differences quotes exactly this verse. eBible's "Public Domain" is a packager's line over a text
  whose publisher says otherwise, and the most restrictive statement attached to the bytes is the
  society's (RUL-0105). **Not taken.**
- **CrossWire `ChiUn` / `ChiUns`**, rebuilt in 2021 from bible.fhl.net (`TextSource=http://bible.fhl.net`,
  `DistributionLicense=Public Domain`). **Taken.**

FHL's page 信望愛聖經網站版權宣告 (https://www.fhl.net/gb/fhl/fhl8.html, 2025-06-08, a copy kept here
as `fhl-copyright.html`) says of its text, in summary:

> 《FHL和合本》 was typed in Big5 in 1995 by a volunteer from the 1919 printing of the Union Version
> (1919年4月22日出版), which is past its term of protection. In the CBOL project it was put into a
> database and Strong's numbers were added; old place names were replaced with the forms of the
> National Institute for Compilation and Translation (大马色 → 大马士革); commas, full stops and other
> punctuation were added for reading; personal names keep the 1919 forms. FHL's text and the Bible
> Society's 1988 新标点和合本 differ in at least 2,203 places beyond 着/里, headings and format.

So the words are the 1919 translation's and the typography is FHL's. Two things are not the 1919
printing and should not be presented as it: the punctuation and modern place names are FHL's, and
the text writes **她** (1,567 times) and **牠** (256) for the feminine and the animal, which the 1919
edition could not have — 她 was coined in 1920. That is FHL's modernisation; nothing here says
otherwise and nothing here changes it.

## The Strong numbers

FHL's page, same place:

> 信望爱的《和合本》 Strong's numbers 着作权为本会所有，采用open source FDL授权，本文件包含Strong's
> numbers标记，请勿任意移除。
>
> (The copyright in the Strong's numbers of FHL's Union Version belongs to FHL and is licensed under
> the open-source FDL; this file contains Strong's number markup, please do not remove it arbitrarily.)

The **GNU Free Documentation License** (the page names no version) permits copying and modifying
the numbered text, including commercially, provided the copy stays under the FDL and keeps the
notices. So the numbers may be used without asking anyone, which is what the owner's approval of
2026-09-25 was conditioned on (NOT-0195).

**How they are used.** As the Synodal's numbering is: read from the module for the length of one
run by `Essenthos.Forge union-strong`, laid onto the words the corpus loaded from the same module,
and never stored on a word or served. What reaches the database is the links matched on them,
each credited *the Faith Hope Love foundation's Strong numbering of the Chinese Union Version, read
from CrossWire's ChiUn and ChiUns* and declared in `Essenthos.Corpus/Corpus/Datasets.cs`
(`fhl-cuv-strong`). The corpus copies neither FHL's file nor its markup, so the request not to
strip the markup from the file is not engaged; whether links matched on FDL numbers are themselves
a modified version the FDL reaches is the owner's to weigh under RUL-0183, and is asked on FTR-0731.

Three kinds of number share the `lemma` attribute, and only lexemes are read:

- Strong's TVM parsing codes (`H8804`, `G5656`; anything above H8674 or G5624) name no word and are
  dropped.
- FHL's own numbers for the Hebrew prefixes are **not** STEPBible's, which BHSA carries. Measured by
  co-occurrence over the Old Testament verses both hold: FHL H9001 = לְ (STEP H9005, lift 1.85),
  H9002 = בְּ (H9003, 2.16), H9003 = כְּ (H9004, 8.95). They are translated; the single H9004 is dropped.

## Segmentation

Chinese prints no spaces, and no segmenter was downloaded or run. A word is the span FHL tagged —
each `<w>` element — and a run no element claims is one word up to the next punctuation mark. The
spaces CrossWire writes between elements in `ChiUn` are markup and are dropped; the ideographic
space the edition prints before 神 is kept. Every verse reads back character for character as the
module prints it (`SwordTextTests`).

## Attribution

Not required by anything above for the text; recorded because RUL-0181 asks for it. The translation
is the Union Version committees' (1919). The transcription, punctuation, simplified conversion and
Strong numbers are the **Faith Hope Love foundation's (財團法人信望愛資訊文化藝術基金會,
bible.fhl.net)**. The SWORD modules are **CrossWire Bible Society**'s build of FHL's text.

Cite as: *和合本 (1919), in the transcription of the Faith Hope Love foundation, bible.fhl.net, as
CrossWire publishes it in the SWORD module ChiUn (or ChiUns).*
