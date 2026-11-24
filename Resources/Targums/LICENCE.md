# The Targums: Onkelos, Jonathan on the Prophets, and the Targums of the Writings, from Sefaria

From Sefaria's public export, the bucket `https://storage.googleapis.com/sefaria-export/json/…`,
read on **2026-11-24** (objects dated 2026-11-01). 34 JSON files, one per book and version, installed as
`Targums/<onkelos|jonathan|writings>/<book>.json` (the build refuses paths with spaces); each file's pin in
`scripts/fetch-sefaria-targums.sha256` names the export object it came from, below `json/Tanakh/Targum/`. `scripts/fetch-sefaria-targums.ps1` fetches
them, holds every file to its SHA-256 in `scripts/fetch-sefaria-targums.sha256`, and refuses any file
whose `license` field is not "Public Domain".

## The licence is per version, and Sefaria says so

Sefaria-Export's `LICENSE.md` (github.com/Sefaria/Sefaria-Export at 32d7bcf7, read 2026-11-24):

> Each text is licensed separately, so there is no overall license for this repository.
> You can find the license for each text in their JSON versions under the `license` field.

So every file was read, and its own field is what it is held under:

| Work | Files (book, version) | `versionSource` | `license` |
|---|---|---|---|
| Targum Onkelos, Torah | `Onkelos/Torah/Onkelos <Genesis…Deuteronomy>/Hebrew/Onkelos <book>.json` (5) | http://toratemetfreeware.com/ | **Public Domain** |
| Targum Jonathan, Prophets | `Targum Jonathan/Prophets/Targum Jonathan on <Joshua…Malachi>/Hebrew/Mikraot Gedolot.json` (21) | https://www.sefaria.org (Zechariah: "Mikraot Gedolot") | **Public Domain** |
| Targums of the Writings | `Aramaic Targum/Writings/Aramaic Targum to <Psalms, Proverbs, Job, Song of Songs, Ruth, Lamentations, Ecclesiastes, Esther>/Hebrew/Mikraot Gedolot.json` (8) | https://www.sefaria.org | **Public Domain** |

Sefaria files the Aramaic under "Hebrew" (`language: he`); the folder name is the export's.

## Left at the source, deliberately

| Version | `license` | Why not |
|---|---|---|
| Targum Onkelos, vocalized according to the Yemenite Taj (he.wikisource) | CC-BY-SA | ShareAlike, and a second vocalisation of a text already taken vocalised |
| Sifsei Chachomim Chumash, Metsudah Publications, 2009 (Onkelos) | CC-BY-NC | a printed edition's copy of the same Onkelos; nothing it adds is wanted |
| Targum Sheni on Esther, Berlin 1898 | unknown | unknown terms are not permission |
| The Targum to Canticles according to six Yemen MSS, Melamed 1921 | Public Domain | a second text of a book already taken from the Mikraot Gedolot |
| "Targum Jonathan on Genesis…Deuteronomy" | — | this is Pseudo-Jonathan, a different work, despite the name |
| J. W. Etheridge's English of Onkelos (1862) | Public Domain | Sefaria holds Genesis 1-14 only, 223 verses; not worth a text |

No Targum of Chronicles, Ezra-Nehemiah or Daniel is in the export.

## What it is, and what is loaded

Three texts, each named for what it is:

- `ONKELOS` — Targum Onkelos, the Babylonian Aramaic rendering of the Torah, vocalised. Genesis 1:1
  reads בְּקַדְמִין בְּרָא יְיָ יָת שְׁמַיָּא וְיָת אַרְעָא.
- `TGJONATHAN` — Targum Jonathan on the Former and Latter Prophets, as the Rabbinic Bible (Mikraot
  Gedolot) prints it.
- `TGWRITINGS` — the Targums of the Writings the Rabbinic Bible prints: Psalms, Proverbs, Job, the five
  Scrolls. They are several works of different date and character, later than Onkelos and Jonathan and
  often expansive (the Song of Songs and Esther above all).

The text of each verse is Sefaria's string with its markup taken out; `<small>` runs (a handful of
editorial notes in Job, Esther and Psalms) are not read as words.

## Attribution

Sefaria (www.sefaria.org), from its public export; Onkelos as Sefaria received it from Torat Emet
(toratemetfreeware.com); Jonathan and the Writings as Sefaria transcribed them from the Mikraot
Gedolot.
