# PolGdanska — Biblia Gdańska (1632), edition of 1881 — Public Domain

**Biblia Gdańska**, the Polish Protestant Bible of 1632 (Daniel Mikołajewski, Jan Turnowski), in the edition of
1881. Loaded as `BG1881`, language `pol`: the historical Polish text beside the Updated Gdańsk Bible (`UBG2018`),
which the owner made the main Polish text (2026-11-24). eBible has no USFM of it (its catalogue, read 2026-11-24,
holds only `polubg` and `polsz` in Polish).

The SWORD module `PolGdanska` 1.0 as CrossWire publishes it, read at the source on **2026-11-24**:
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/PolGdanska.zip` (SHA-256
`7ff221fb7638b42cead7f24283e9bc5c58570b674c49132f1c057ff8be0a716d`, module files dated 2013-07-17).
`scripts/fetch-west-slavic.ps1` fetches it into `Gdansk1881/PolGdanska`, holds the archive and every file to
`scripts/fetch-west-slavic.sha256` and re-reads the conf first. Taken on the owner's approval of 2026-11-24
(TSK-1997).

**66 books, 31,073 verses, 600,825 words** as read. The text carries no markup at all: no titles, notes or
supplied words.

## The licence

1. **The module's conf**, `mods.d/polgdanska.conf`, kept beside the module: `Description=Polish Biblia Gdanska
   (1881)`, `DistributionLicense=Public Domain`, `TextSource=http://www.biblia.elblag.net/`.
2. Out of copyright by age: the translators died in 1629 and 1633 and the edition is of 1881.

## The numbering of the module, and the 72 divisions

The module is in SWORD's King James versification, but in 70 chapters (62 psalms, Numbers 13 and 30, 1 Samuel 24,
1 Kings 22, Ecclesiastes 4, Jonah 2 and others) its text is numbered as the Hebrew is: a psalm's title is its verse 1,
and the verse the Hebrew counts beyond the English stands inside the chapter's last verse with the one before it (Psalm
3:8 held the Hebrew's 3:8 and 3:9). Measured against `UBG2018` at the same canonical address (TSK-1997): 70 chapters
read one verse off. `Essenthos/texts/BG1881/corrections.tsv` divides each such last verse where the next Hebrew verse
begins (72 `divide` rows, each boundary read against UBG2018; the words and their order are untouched, the text is
public domain), and the text is placed as `original`. Measured again after: 6 chapters still read a verse off (from
70), 338 verses share little with UBG2018's at the same address (from 1,136), most of them its free rendering. Left as
they are: Psalm 14 (its title numbered apart, which the Hebrew does not), Job 40-41 (nine of the Hebrew's verses run into
40:24), Haggai 2 (numbered as the Latin is), and 1 Samuel 15, Ezra 2 and Job 37, where the module runs two verses into
one.
