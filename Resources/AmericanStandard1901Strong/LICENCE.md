# ASV.bbl — Wade Maxfield's Strong numbers on the American Standard Version, laid and never loaded as a text

**ASV.bbl** from Wade Maxfield's repository **CWP-Desktop-QT**, the desktop program of his *Cross Word Project*
(www.crosswordproject.com): the American Standard Version of 1901 as one SQLite database, a table per book and a
row per English word, with the Strong number of the Hebrew or Greek word it renders (`OrigWordLanguage` H or G,
`DictEntryNumber`) or none. The corpus already loads the ASV from eBible (`../AmericanStandard1901`, slug `ASV`);
this file is another digitisation of the same text and is read only for its numbers.

Source: `https://github.com/wmaxfield/CWP-Desktop-QT/blob/efd6ffc8264aca410c203bf5dc175b3d01863cda/CWPfiles/Bibles/ASV.bbl`,
pinned to commit **efd6ffc8264aca410c203bf5dc175b3d01863cda** (2020-12-01, the repository's head when read; the file
last changed in commit 897f0faa99f8594bf1f4d1c98ccc908a838d0c15, 2020-09-15, "inital commit"). 34,416,640 bytes,
SHA-256 `d997dd6c6533a12fcb84eb052d02774452c145bc3e404cf10a66ca418e6e7e88`. First read on **2026-11-19** (TSK-1890);
`scripts/fetch-asv-strong.ps1` fetches it from that commit and checks the hash, and the reader
(`MaxfieldAsvEdition`) refuses a file whose `copyright` table no longer states the terms below.

## The terms, as the file states them

The database's own table `copyright`, column `CopyrightInfo`, its first two sentences, verbatim (the double space is
the file's):

> The American Standard Version was published in 1901 and is in the public domain in the United States.
>
> Applying Strong's to the text was done as part of the "Cross Word Project" (www.crosswordproject.com) and is  copyright Wade Maxfield and released by him for any purpose.

The rest of the table is the American Revisers' preface of 1901.

Elsewhere, read the same day:
- The repository has no licence file (GitHub's licence endpoint answers 404 for it). Its README describes only how
  to build the program.
- crosswordproject.com says the program is released under the GPL and that Strong's numbers are included in all its
  Bibles; it says nothing further about the data.

## What that permits, and how the numbers are used

"Released by him for any purpose" permits use, adaptation and redistribution; nothing in it is NonCommercial or
ShareAlike (RUL-0183). The owner approved loading the layer on 2026-11-19 (TSK-1890), as a **supporting** one:

- The file is laid onto the eBible ASV's words verse by verse and word by word (`SynodalStrongLayer`, through
  `SideStrongNumbers`), and the numbers are matched within the verse against BHSA and Nestle 1904
  (`StrongNumberSource`), each link credited *Wade Maxfield's Strong numbering of the American Standard Version
  (Cross Word Project), read from CWP-Desktop-QT's ASV.bbl* and declared in `Essenthos.Corpus/Corpus/Datasets.cs`
  (`maxfield-asv-strong`).
- They rank 55, below the ASV's composed links (60) and above the aligner (40): where the two agree the number adds
  a claim, where the ASV had no link it adds one, and it never takes a word from a composed link.
- Left out: a Greek conjunction's or particle's number on an English content word (the file puts it on the last word
  of the clause, Luke 4:12 *God* G3754), a number naming several Hebrew or Greek words of the verse that the order does
  not pair off, and links of their own from function words' numbers, which may only add a claim to another method's link.
- The numbers are not served on the ASV's words (`texts/strong-terms.tsv`, served `no`): the terms would allow it, but
  served numbers would become the ASV's own marks ahead of its links.

## Attribution

The translation is the **American Revision Committee**'s, published by Thomas Nelson & Sons in 1901. The Strong
numbering is **Wade Maxfield**'s, made for the **Cross Word Project**.
