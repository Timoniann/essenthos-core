# FreJND — J. N. Darby's French Bible, 2024 revision, with Strong numbers

CrossWire's SWORD module **FreJND**, version 3.3 of 2026-02-17, loaded as the text `JND2024`.

Fetched on **2026-10-01** from
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/FreJND.zip`
(SHA-256 `0c08f4ca5e06247c77359bcc3a27a21552fa4d2c338a802e3935b774db676b7b`) by
`scripts/fetch-crosswire-strong.ps1`, which checks that hash and re-reads the module's
`DistributionLicense` before it replaces anything. Taken on the owner's approval of 2026-10-01,
TSK-0909.

## The terms, read in the module's own configuration

`mods.d/frejnd.conf`, inside the archive and the same as
`https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/frejnd.conf` (read 2026-10-01). Verbatim:

> DistributionLicense=Public Domain

> DistributionNotes=Report errors at <contact(a)editeurbpc.com> or <contact@concordance.bible>

> TextSource=https://concordance.bible/media/download/Darby-osis.zip

> About=French translation by John Nelson Darby (JND).\par\
> First edition of the complete Bible in 1885, reissued in 1916, then in 2024.\par\
> Text in Public Domain 2024 (provided by BPC, revision JND v2.0)\par\
> With Strong numbers affected by "Concordances and Bible Translations" (https://concordance.bible/).\par\
> The numbering of the verses follows the Hebrew Bible for the Old Testament.\par\
> You can buy a printed version at:\par\par\
> editeurbpc.com – Bibles et Publications Chrétiennes\par\
> 30 rue Châteauvert – CS 40335\par\
> 26003 VALENCE CEDEX FRANCE

> History_3.0=(2024-03-08) Update text (from BPC editor JND v1.3) and add Strong numbers (from concordance.bible)
> History_3.3=(2026-02-17) Some corrections to Strongʼs numbers. Also we added for the first time locutions.

The conf has no `Copyright=` line. Every statement attached to the bytes says public domain, and
the About says the 2024 text is provided free of rights by BPC; that is what `text.licence` records.

## How it is read

`Versification=German`: SWORD's German scheme, which is the Hebrew's numbering in the Old
Testament (Joel four chapters, Malachi three, a psalm's title its first verse) and the critical
Greek's in the New. The text is loaded as `Versification.Original` and placed in the shared frame
like every other Hebrew-numbered text. The asterisk the edition prints at the head of a paragraph
is kept as printed. The Strong numbers are read by `crosswire-strong` for one run and drawn into
links credited to concordance.bible (`concordance-darby-strong`); they are not stored on the words.

## Measured (TSK-0909)

31,167 verses, 814,481 words, 420,415 of them under a Strong number; every verse reads back as the
module prints it (`SwordTextTests`). On a slim copy of the corpus: 287,148 of 289,557 numbered Old
Testament words reach a BHSA word (99.2%), 128,110 of 130,858 New Testament words reach Nestle 1904
(97.9%) and 127,963 reach Westcott and Hort (97.8%).

## Attribution

The translation is **John Nelson Darby**'s (1885). The 2024 revision (JND v2.0) is **Bibles et
Publications Chrétiennes**' (editeurbpc.com). The Strong numbers are **Concordances et Traductions
de la Bible**'s (concordance.bible). The SWORD module is **CrossWire Bible Society**'s.
