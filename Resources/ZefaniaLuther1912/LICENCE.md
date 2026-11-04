# SF_2022-02-27_GER_LUTH1912_(LUTHER_1912_mit_Strongs).xml — the Zefania Luther 1912 with Strong numbers

**Luther 1912 mit Strongs**, the Lutherbibel of 1912 in modern spelling with Strong numbers, in Zefania XML,
as toledot.info prepared it. Fetched on **2026-11-04** from the Zefania distribution on SourceForge:

- folder: <https://sourceforge.net/projects/zefania-sharp/files/Bibles/GER/Lutherbibel/Luther%201912/>
- file: `SF_2022-02-27_GER_LUTH1912_Strongs_xml_220227.zip` (uploaded 2023-07-31, 2,228,377 bytes,
  SHA-256 `65f8c8f3c2163888e28acf5ac24a0da86a8d9937a46339516a13806d6ee46b10`), taken from
  `https://downloads.sourceforge.net/project/zefania-sharp/Bibles/GER/Lutherbibel/Luther%201912/SF_2022-02-27_GER_LUTH1912_Strongs_xml_220227.zip`
- it holds one file, the XML beside this one (12,179,419 bytes, SHA-256
  `cfa85f9c33b7a6ed039ed62a0978b77c5d4175567176ebdbc29378df76a706ff`): 66 books, 31,172 verses,
  350,406 `<gr str="…">` elements, all but 40 of them on a single word.

The same folder also offers `SF_2022-02-27_GER_LUTH1912_xml_220227.zip` (the text without numbers) and
`SF_2009-01-20_GER_LUTH1912_(LUTHER 1912).zip`; neither was taken.

## What the file says of itself

Its `<INFORMATION>` block, verbatim:

```xml
<format>Zefania XML Bible Markup Language</format>
<date>2022-02-27</date>
<title>Luther 1912</title>
<creator>www.toledot.info</creator>
<subject>Die Heilige Schrift</subject>
<description>Luther-Bibel von 1912 (in neuer Rechtschreibung)</description>
<publisher>www.toledot.info</publisher>
<contributors></contributors>
<type>bible</type>
<identifier>luth1912</identifier>
<source>Jens Grabner, unbekannt, J. Barkowsky</source>
<language>DE</language>
<coverage>provide the Bible to the nations of the world</coverage>
<rights>This Text is in the Public Domain</rights>
```

## What its maker says, and where the two part

**The distribution states no licence.** `zefania-sharp`'s project record lists none
(`"categories" -> "license": []` at <https://sourceforge.net/rest/p/zefania-sharp>, read 2026-11-04), as
`../Zefania/LICENCE.md` found for the KJV+.

**toledot.info, the creator and publisher**, read at the source on 2026-11-04:

- <https://www.toledot.info/die-welt-der-bibel.php?t=impressum/copyright>, under *Bibelübersetzungen*:
  > Auf den Seiten www.toledot.info biete ich die Bibelübersetzungen von Martin Luther von 1912 (in einer
  > wiederhergestellten Fassung), von Franz Eugen Schlachter von 1951, von Herrmann Menge von 1939 und die
  > Elberfelder Bibelübersetzung von 1905 an. Soweit mir nach eingehender Recherche bekannt ist, unterliegen
  > diese Bibelfassungen keinem Urheberecht und sind copyrightfrei. Die Texte wurden dem Zefanja-Projekt entnommen.

  and under *Allgemein*:
  > Alle von mir erstellten Inhalte und Werke auf diesen Seiten unterliegen dem deutschen Urheberrecht. Die
  > Vervielfältigung, Bearbeitung, Verbreitung und jede Art der Verwertung außerhalb der Grenzen des
  > Urheberrechtes bedürfen meiner schriftlichen Zustimmung. Downloads und Kopien dieser Seite sind nur für den
  > privaten, nicht kommerziellen Gebrauch gestattet.

  and under *Selbst erstellte Inhalte und Bilder*:
  > Für alle von mir selbst erstellten Inhalte bleiben alle Rechte, wie oben beschrieben, vorbehalten.

- <https://www.toledot.info/die-welt-der-bibel.php?t=strongs/inhalt>, of the numbers:
  > Die Strong-Zuordnung zu den Bibeln Luther 1912, Elberfelder 1905 und Schlachter 1951 ist unvollständig und
  > mit Fehlern behaftet. Diese Website bietet die Strong-Zuordnung nur für die Luther-Bibel 1912 trotzdem an,
  > weil sie trotz allen Unzulänglichkeiten eine Hilfe darstellt, Wortzusammenhänge in der Bibel besser zu verstehen.

**So the statements cover the text and leave the numbers open.** The module's `<rights>` speaks of *this
Text*; toledot calls the translations copyright-free and taken from the Zefania project, and reserves every
right in what it made itself. Nobody says whether the Strong layer, and toledot's corrections to it
(`../Luther1912/LICENCE.md`: Genesis 1:2's *Tiefe* changed from H6440 to H8415, H853 added), are the text
or toledot's own work. Read the narrow way, which is the one RUL-0105 asks for when two statements could
be meant either way, the numbers are toledot's and not free to copy.

## How it is used

**As a check only.** The module is compared with LUTH1912's own numbers (eBible's file, the uncorrected 2005
layer) to find the words where the two place a number differently; each such place is then read against
BHSA and Nestle 1904, and what is fixed is fixed by our reading, as a rule of the converter's
`MisplacedNumbers` stage or a `strong` row in `Essenthos/texts/LUTH1912/corrections.tsv`. **No number of
this module, and none of its text, reaches a product, a link or a served table.** Its modern spelling is not
the 1912 orthography the corpus serves in any case.

## Attribution

Not required by anything above, and recorded because RUL-0181 asks for it whatever the licence says: the
module is toledot.info's, its sources named in it as Jens Grabner, an unknown hand and J. Barkowsky, by way
of the Zefania project.
