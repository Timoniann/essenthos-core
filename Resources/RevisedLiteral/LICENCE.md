# RLT — the Revised Literal Translation, with the King James's Strong numbers

CrossWire's SWORD module **RLT**, version 1.0 of 2022-11-12, loaded as the text `RLT2018`.

Fetched on **2026-10-01** from
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/RLT.zip`
(SHA-256 `958924773e81162f753e671c9fa505fc64a247f9910451e51f9543fc74e80f63`) by
`scripts/fetch-crosswire-strong.ps1`, which checks that hash and re-reads the module's
`DistributionLicense` before it replaces anything. Taken on the owner's approval of 2026-10-01,
TSK-0909.

## The terms, read in the module's own configuration

`mods.d/rlt.conf`, inside the archive and the same as
`https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/rlt.conf` (read 2026-10-01). Verbatim:

> DistributionLicense=GPL

> TextSource=Michael W. Jones, Sr.

From the About, the passages that bear on the terms:

> The embedded Strong's Numbers is from the KJV2003 Project (KJV2013010915). The rights to the base
> text are held by the Crown of England. The Strong's numbers in the Old Testament were obtained from
> The Bible Foundation: http://www.bf.org. The New Testament Strong's data was obtained from The
> KJV2003 Project at CrossWire: http://www.crosswire.org.

> It is in this spirit that we in turn offer the Revised Literal Translation (RLT) text freely for
> any purpose. Any copyright that might be obtained for this effort is held by Michael W. Jones, Sr.
> who hereby grants a general public license to use this text for any purpose.

The conf has no `Copyright=` line.

## What that permits

The GPL permits copying and serving the text; anything published as an adaptation of it carries the
GPL in turn, which is ShareAlike in effect, and `text.redistribution` records it so. The King James
base is the Crown's in the United Kingdom, as for the King James the corpus already holds. The
numbers are read by `crosswire-strong` for one run and drawn into links (`rlt-strong`), not stored.

## How it is read

`Versification=KJV`, loaded in the English frame. A `<w>` may hold a phrase — *In the beginning*
under H7225 — and every word of it is one rendering of that number. The words the King James prints
in italics (`transChange`) are kept as text with no number. The study notes and their literal
readings are notes, never words of the verse.

The New Testament elements also carry `src`, the position of each Greek word in the KJV2003
project's Textus Receptus verse — a word alignment rather than a lemma match. It is not read yet.

## Measured (TSK-0909)

31,102 verses, 787,361 words, 742,047 of them inside a numbered element; every verse reads back as
the module prints it. On a slim copy of the corpus: 559,179 of 565,711 numbered Old Testament words
reach a BHSA word (98.8%), 176,115 of 176,336 New Testament words reach Scrivener's Textus Receptus
(99.9%) and 176,242 reach Stephanus's (99.9%).

## Attribution

The revision is **Michael W. Jones, Sr.**'s, of the King James Version (1769). The Old Testament
Strong numbers are **the Bible Foundation**'s (bf.org), the New Testament's the **KJV2003 project**'s
at CrossWire, with Maurice Robinson's Greek. The SWORD module is **CrossWire Bible Society**'s.
