# Commentaries — what someone wrote about a verse

Two commentaries, each a SWORD commentary module from CrossWire, one folder per module. Read at the
source on **2026-11-16**; `scripts/fetch-commentaries.ps1` fetches both, checks each archive's hash and
re-reads each configuration's licence line before it installs anything.

## MHC — Matthew Henry's Complete Commentary on the Whole Bible

Matthew Henry (1662-1714), *An Exposition of the Old and New Testament*, 1706-1721; the epistles from
Romans on were finished from his notes by other ministers after his death.

- Archive: `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/MHC.zip`, SHA-256
  `6bcb936873ca144e317805e5c1677940fd86e2403f7c14517752e44f25c8882b`, module version 2.2 (2022-08-29),
  driver zCom4, OSIS markup, KJV versification.

### What it is used under: Public Domain

| Where | What it says |
|---|---|
| `MHC/mods.d/mhc.conf`, the module's own configuration | `DistributionLicense=Public Domain`; `TextSource=http://www.ccel.org/` |
| the same file, `About` | "Public Domain--Copy Freely. This text was prepared from the Christian Classics Ethereal Library located at www.ccel.org. Thanks also to Logos Research, Inc. for providing CCEL with their text of Matthew Henrey's Complete Commentary, from which this edition has been prepared." |
| CrossWire's module page, <https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=MHC> | "Distribution License: Public Domain" |
| CCEL's pages for the work, <https://ccel.org/ccel/henry/mhc> | no rights statement shown |

The work is out of copyright by age. The module's own statement adds no condition, and no statement
attached to these bytes claims a transcriber's right. It is credited anyway (RUL-0181).

## Rieger — Carl Heinrich Riegers Betrachtungen

Carl Heinrich Rieger (1726-1791), *Betrachtungen über das Neue Testament* (Stuttgart 1833, second
unchanged edition) and *Kurze Betrachtungen über die Psalmen und über die zwölf kleinen Propheten*
(Stuttgart 1835). The New Testament, the Psalms and the twelve minor prophets only.

- Archive: `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/Rieger.zip`, SHA-256
  `b7bacef4f009a9f7c13a2ea7b5181f7f442be4b498125b92d477e4a0198af119`, module version 1.0 (2000-08-15),
  driver RawCom, HTML markup (`SourceType=ThML`), no versification named (SWORD's default, KJV).
- `Rieger.Autobiografie.html` in the module folder is not read.

### What it is used under: Public Domain

| Where | What it says |
|---|---|
| `Rieger/mods.d/rieger.conf`, the module's own configuration | `DistributionLicense=Public Domain`; `TextSource=Matthias Ansorg` |
| the same file, `About` | "License: Public Domain -- copy freely"; "Made available in electronic format by Michael Bolsinger … at http://www.luther-bibel-1545.de … It was converted to SWORD format by Matthias Ansorg" |
| CrossWire's module page, <https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=Rieger> | "Distribution License: Public Domain" |

Rieger died in 1791 and both editions were printed in the 1830s. The digitiser's and the converter's
statement is the one in the configuration, and it places the module in the public domain.

## What the corpus does with them

Each module stores, for each verse of the King James numbering, the comment written on it; a comment
written over several verses is one entry every verse of the run points at. The corpus keeps each
comment once, over the run of verses it was written on, with the chapter and book introductions the
module stores before a chapter's first verse, in the table `commentary_entry`. Every word of the
comment is kept; only presentation (fonts and colours) is dropped, and the markup becomes headings,
paragraphs, italics, small capitals and references to verses.

## Attribution as shown to readers

> Matthew Henry, Complete Commentary on the Whole Bible (1706-1721), public domain, from CrossWire's
> SWORD module as prepared from the Christian Classics Ethereal Library.

> Carl Heinrich Rieger, Betrachtungen (Stuttgart 1833 and 1835), public domain, digitised by Michael
> Bolsinger and converted by Matthias Ansorg, from CrossWire's SWORD module.
