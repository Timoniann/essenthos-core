# FreSegond1910 — the Strong numbers on Louis Segond 1910, read and never loaded as a text

CrossWire's SWORD module **FreSegond1910**, version 4.0 of 2026-02-17: Louis Segond's 1910 Bible
with Strong numbers on its words. The corpus already loads the 1910 Segond from eBible
(`../Segond1910`, slug `LSG1910`); this module is a different digitisation of the same text
(Richard Lemay's), and is read only for its numbers.

Fetched on **2026-10-01** from
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/FreSegond1910.zip`
(SHA-256 `fa6b94c5355353bc3b76971947d0053ccf027e7cf58828c165df1098533c2d43`) by
`scripts/fetch-crosswire-strong.ps1`, which checks that hash and re-reads the module's
`DistributionLicense` before it replaces anything. Taken on the owner's approval of 2026-10-01
("можеш запустити агента шоб всі ці тексти для нас закачав і налаштував мапування"), TSK-0909.

## The terms, read in the module's own configuration

`mods.d/fresegond1910.conf`, which ships inside the archive and is the same file as
`https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/fresegond1910.conf` (read 2026-10-01).
Verbatim:

> DistributionLicense=Copyrighted; Permission to distribute granted to CrossWire

> TextSource=https://concordance.bible/media/download/Sg1910-osis_v11n.zip

> About=En 1874, suite à une commande de la Compagnie des Pasteurs de Genève, parait la traduction de
> l'Ancien Testament par Louis Segond, pasteur et théologien (1810-1885). [...] Source:
> http://richardlemay.com, avec lʼautorisation de Richard Lemay. \par\parNuméros Strong affectés en
> 2026 par « Concordances et Traductions de la Bible ».

> History_3.2=(2025-09-23) Added Strongʼs numbers
> History_4.0=(2026-02-17) Some corrections to Strongʼs numbers. Also we added for the first time locutions.

The conf has no `Copyright=` line.

## What that permits, and how the numbers are used

The text is Segond's, out of copyright since his revisers' work is more than a century old. What is
copyrighted is the module — Lemay's typing and the 2026 numbering by *Concordances et Traductions
de la Bible* — and the permission named is CrossWire's, to distribute it. Nothing grants this
project a right to redistribute the module or its numbers, and nothing here does: as with the
Synodal's numbering (PRB-0264) and FHL's (`../ChineseUnion1919/LICENCE.md`), the numbers are read
by `Essenthos.Forge crosswire-strong FreSegond1910` for the length of one run, laid onto the words
of the eBible Segond the corpus already holds, and never stored on a word or served. What reaches
the database is the links matched on them, each credited *the Strong numbering of Louis Segond
1910 by Concordances et Traductions de la Bible, read from CrossWire's FreSegond1910*, and
declared in `Essenthos.Corpus/Corpus/Datasets.cs` (`concordance-segond-strong`).

`../Segond1910/LICENCE.md` refuses eBible's own Strong tags because nobody is named for them;
these are the numbers whose author and terms are stated.

## Measured (TSK-0909)

Laid onto the eBible Segond's 31,170 verses: 31,169 placed (54 together with a neighbour the two
digitisations divide differently), 1 refused, 405,740 words given a number. Matched on a slim copy
of the corpus: 282,441 of 284,888 numbered Old Testament words reach a BHSA word (99.1%), 118,326
of 120,852 New Testament words reach Nestle 1904 (97.9%) and 120,044 reach Scrivener's Textus
Receptus (99.3%).

## Attribution

The translation is **Louis Segond**'s, in the revision of 1910. The digitisation is **Richard
Lemay**'s (richardlemay.com). The Strong numbers are **Concordances et Traductions de la Bible**'s
(concordance.bible), 2026. The SWORD module is **CrossWire Bible Society**'s.
