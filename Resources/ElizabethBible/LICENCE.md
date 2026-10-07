# The Elizabeth Bible (Church Slavonic) — the text `CSLELIZABETH`

The Church Slavonic Bible in the recension prepared under Elizabeth of Russia, first printed in 1751.
Read from CrossWire's SWORD module **CSlElizabeth 1.5.2** (`CSlElizabeth/`), the archive
<https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/CSlElizabeth.zip>, SHA-256
`96705c572eda109fa42203dfd0fbcece54ecbe8a940e568455f4f4051e698652`. `scripts/fetch-elizabeth.ps1`
fetches exactly these bytes and re-reads the licence line below before it installs anything. Read
at the source on **2026-10-07**.

## The licence

The module's own configuration, `mods.d/cslelizabeth.conf`, as fetched:

> Description=1757 Church Slavonic Elizabeth Bible
>
> About=1757 Church Slavonic Elizabeth Bible\par This electronic edition comes from rusbible.ru and
> features modernized spelling.
>
> DistributionLicense=Public Domain
>
> TextSource=rusbible.ru

CrossWire's module page (<https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=CSlElizabeth>)
says the same: Distribution License Public Domain. The 1751 text has been out of copyright for more
than two centuries. rusbible.ru, named as the source, did not answer when its own terms were looked
for on 2026-10-07; nothing found states terms over the typing other than CrossWire's.

getBible serves a JSON copy of the same module (`api.getbible.net/v2/csielizabeth.json`) whose file
repeats `"distribution_license": "Public Domain"`; its repository is badged GPL-3.0, which is that
project's code licence. That copy is not what is read here.

## What it is, and what it is not

- **Modernised spelling.** The words are Church Slavonic, written in the civil Russian alphabet:
  Genesis 1:1 reads `В начале сотвори Бог небо и землю.` No titla, no accents, no ѣ or ѡ. It is not a
  facsimile of the printed page and does not show what Slavonic printing looks like.
- **Numbered in SWORD's Synodal versification.** Read with that layout (the book order and verse
  counts of `canon_synodal.h` in CrossWire's SWORD source), every book begins where it begins and the
  index is filled exactly. In the corpus it is placed by the Septuagint's rules, except 1 Kings and
  Malachi, which it divides as the King James does, and the books the versification data has no
  column for (2 Ezra, Tobit, Judith, Wisdom, Sirach), which stand at their own numbers.
- **76 books, 36,000 verses.** The 66 of the Protestant canon, Psalm 151 as Psalm 151, Daniel in 14
  chapters with Susanna and Bel, Esther in 10, and Baruch, 2 Ezra (the Greek 1 Esdras), Tobit,
  Judith, Sirach, 1 and 2 Maccabees, Wisdom, the Letter of Jeremiah and the Prayer of Manasseh.
- **Not in this electronic text:** 3 Maccabees and 3 Ezra (the Latin 4 Ezra), which the printed
  Elizabeth Bible has. They are absent, not filled from another source. An introduction SWORD keeps
  apart from the verses — the Synodal layout puts the prologue to Sirach there — is not read.

## Modified by Essenthos

Nothing in the words. Verses are read as the module stores them; a word is a run of letters, and
what stands between two words is the first one's trailer.
