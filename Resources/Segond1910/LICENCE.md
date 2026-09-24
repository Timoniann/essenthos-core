# *.usfm — the Louis Segond Bible of 1910, and the Strong tagging that is refused

**La Sainte Bible, traduction de Louis Segond**, in the revision of **1910**. Segond (1810–1885), a
pastor of Geneva, published the Old Testament in 1874 and the New Testament in 1880; the 1910
revision is the text French Protestants read for most of the twentieth century and the last Segond
that is out of copyright. The Segond 21 and the Nouvelle Segond Révisée are later and are not.

From <https://ebible.org/find/details.php?id=fraLSG>, read at the source on **2026-09-25**. The
archive taken is `https://ebible.org/Scriptures/fraLSG_usfm.zip` (3,467,489 bytes, last modified
2026-08-08); `scripts/fetch-ebible.ps1 -Only Segond1910` is what fetches it, and it re-reads every
statement below before it replaces anything. Downloaded with the owner's approval of 2026-09-25
(NOT-0195, DOC-0207).

**66 books, 31,170 verses, 722,591 words** as loaded. It numbers most of the Old Testament as the
Hebrew does — 23,211 verses, a psalm's title counted as its first verse — which is why it holds 66
more than the English count.

## The licence of the text, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside these files**:

> Louis Segond 1910 — The Holy Bible in French, Louis Segond version of 1910 — **Public Domain**
>
> Translation by: Louis Segond · Contributor: Public Domain
>
> Cette Bible est dans le domaine public. Il n'est pas protégé par copyright. This Bible is in the
> Public Domain. It is not copyrighted.

**2. That page as served on the web**, <https://ebible.org/fraLSG/copyright.htm>, read on
2026-09-25: the same words, "HTML generated … 8 Aug 2026 from source files dated 8 Aug 2026".

**3. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `fraLSG`, read on
2026-09-25: `Copyright` = `public domain`, `Redistributable` = `True`, `downloadable` = `True`,
`sourceDate` = `2026-08-08`, `swordName` = `fraLSG1910eb`.

Segond died in 1885 and the 1910 revision is more than a century old, so the text is out of
copyright by the arithmetic as well.

## What arrives with it and is not loaded

- **705,728 Strong tags**, which the 2026-08-08 revision of the file added. eBible names nobody for
  them. They are not word-level — Genesis 1:1 tags *créa* and *les* both H1254 and *et*, *la* and
  *terre* all H8064 — and the one Strong-numbered Segond that says whose its numbers are,
  CrossWire's `FreSegond1910`, credits them to *Concordances et Traductions de la Bible*
  (concordance.bible), 2026, under `DistributionLicense=Copyrighted; Permission to distribute
  granted to CrossWire` (read at crosswire.org on 2026-09-25). Whether eBible's are those is not
  established, and the most restrictive statement that could be attached to them is CrossWire's
  (RUL-0105). `EbibleTextSource` refuses them by name, and a test pins it.
- **The book introductions, outlines and tables** (`\ip`, `\io1`, `\tr` …) and **the section
  headings and parallel references** (`\s1`, `\ms1`, `\r`, `\mr`), which are the digital edition's
  editors' and not the translation's. The `\id` lines credit the file to "Moon Sun Kim … Updated
  for DBL by E. Canales, September 2012".
- **9,751 cross references** (`\x`) are kept as the edition's notes beside their verses, not as words.

The French reaches the Hebrew and the Greek through Clear Bible's hand-made alignment instead —
see `../ClearBible/LICENCE.md` for what had to be done to join it.

## Attribution

Not required by the text's terms, and recorded because RUL-0181 asks for it whatever the licence
says: the translation is Louis Segond's, in the revision of 1910; this digital edition is
**eBible.org**'s.

Cite as: *La Sainte Bible, traduction de Louis Segond, révision de 1910, in the digital edition
eBible.org publishes as fraLSG.*
