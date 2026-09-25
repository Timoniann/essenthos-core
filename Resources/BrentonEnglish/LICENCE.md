# *.usfm — Brenton's English Septuagint, as eBible publishes it

**The Septuagint Version of the Old Testament and Apocrypha, with an English translation**, by **Sir
Lancelot Charles Lee Brenton** (1807–1862): the English of 1844 with the Apocrypha of 1851, Samuel
Bagster and Sons, London. Loaded as the text `BRENTON`, beside his Greek, `GRCBRENT`.

From <https://ebible.org/find/details.php?id=eng-Brenton>, read at the source on **2026-09-25**. The
archive taken is `https://ebible.org/Scriptures/eng-Brenton_usfm.zip` (1,438,240 bytes), whose
`sourceDate` in eBible's catalogue is **2024-10-01**; `scripts/fetch-ebible.ps1 -Only BrentonEnglish`
fetches it. **53 books, 1,117 chapters, 29,005 verses, 4.3 MB.**

## What it is

Brenton translated the Vatican text in Valpy's reprint of the Sixtine edition of 1587, the Greek the
corpus holds as `GRCBRENT`. The two are divided alike: over the 1,104 chapters of the Greek, the
English prints the same verses in all but five. Four are Nehemiah 3, 4, 9 and 10, which the English
numbers as the English Bibles do and the Greek as the Hebrew (the frame places both at the same
rows; the English has no 4:6, as the Greek has none, and a supplement in `TvtmsSupplements` says
so). The fifth is 1 Samuel 17, into which eBible sets as supplied words the twenty verses the
Vatican text lacks (17:12–31), which Brenton translated from Codex Alexandrinus in an appendix.

Its thirty-seven books of the Hebrew canon include Esther and Daniel only in their Greek form (`ESG`,
`DAG`, loaded at the ordinals of Esther and Daniel, as the Greek's are). Its sixteen further books are
Tobit, Judith, Wisdom, Sirach, Baruch, the Letter of Jeremiah, Susanna, Bel, 1–4 Maccabees, 1 Esdras
and the Prayer of Manasseh. **1 Maccabees 16 chapters/924 verses, 2 Maccabees 15/555, 3 Maccabees
7/228, 4 Maccabees 18/483**, all at the Greek's numbers.

Brenton's preface to the Apocrypha (eBible's `OTH`) says the Apocrypha follows the Authorized Version
and that *"the third and fourth books of the Maccabees have been translated for this edition"*;
eBible's errata (`XXC`) say its corrections were checked against Brenton's 1844 text and, for the
Apocrypha, the Authorized Version, *"which Brenton adapted for his translation"*. So 4 Maccabees is the
Bagster edition's own and is labelled Brenton's with that qualification.

## What is taken, and what is not

The 53 scripture books. The words printed as supplied, 9,070 `\add` spans, load as supplied words;
the 2,596 footnotes and 150 cross-references load as the edition's notes. The Apocrypha's preface
(`OTH`), Brenton's table of Jeremiah's chapters (`XXA`), his preface of 1844 (`XXB`) and eBible's
errata (`XXC`) are the edition's apparatus, not scripture; the fetch leaves them out and this note
records what they say.

## The licence

**Public domain**, in all three of eBible's statements, and Brenton died in 1862:

1. `copr.htm`, shipped in the archive and kept beside this file: *"Translation of the Greek Septuagint
   into English by Sir Lancelot Charles Lee Brenton — Published in 1851, and now in the Public
   Domain."*
2. eBible's catalogue (`translations.csv`): `Copyright` = `public domain`, `Redistributable` = `True`.
3. The copyright page on the web, <https://ebible.org/eng-Brenton/copyright.htm>, which says the same.

The fetch reads the second and third before it replaces anything.
