# tsv/macula-hebrew.tsv — MACULA Hebrew's annotation of the Westminster Leningrad Codex

**MACULA Hebrew Linguistic Datasets**, © 2022-2024 **Biblica, Inc.** Syntax trees, morphology, word
senses, semantic frames and participant referents for the Hebrew Bible, published by Clear Bible.

From <https://github.com/Clear-Bible/macula-hebrew>, read at the source on **2026-11-16** at commit
**47db250bd55d0d8577f2a94fba114ef16c35b23c** (2026-04-24). What is kept here is one file:
`WLC/tsv/macula-hebrew.tsv`, 84,382,719 bytes, 475,911 rows, one per morph of the Westminster
Leningrad Codex. It is stored under git LFS upstream, so `scripts/fetch-macula-hebrew.ps1` reads it
from media.githubusercontent.com at that commit. `LICENSE-upstream.md` and `README-upstream.md` beside
this file are the repository's own, as fetched.

## The licence, verbatim

`LICENSE.md` opens with the grant:

> [MACULA Hebrew Linguistic Datasets](http://github.com/Clear-Bible/macula-hebrew/) © 2022-2024 by
> [Biblica, Inc](http://biblica.com) is licensed under [CC BY 4.0 ](http://creativecommons.org/licenses/by/4.0/).

lists among Clear's own datasets under it

> 8. Participant referents (see `@subjref`, `@participantref`)

and gives the one condition:

> * **Attribution** — You must attribute the work as follows: "MACULA Hebrew Linguistic Datasets,
>   available at https://github.com/Clear-Bible/macula-hebrew/". You may do so in any reasonable
>   manner, but not in any way that suggests the licensor endorses you or your use.

No ShareAlike and no NonCommercial.

## The works underneath it, and what is taken from each

`LICENSE.md` names five datasets bound into the repository beside Clear's own:

1. **The Westminster Leningrad Codex** (tanach.us): "All biblical Hebrew text, in any format, may be
   viewed or copied without restriction." MACULA's `text` column is read only to place each morph on
   BHSA's word by its letters, and then dropped. No Hebrew text of this file is stored or served.
2. **Westminster Hebrew Syntax without Morphology**, the Groves Center, CC BY 4.0. The syntax trees.
   **Not loaded.**
3. **The Open Scriptures Hebrew Bible**, CC BY 4.0, "Original work of the Open Scriptures Hebrew
   Bible available at https://github.com/openscriptures/morphhb". The morphology under the columns
   `class`, `pos`, `type`, `lemma`, `person`, `gender` and `number`, which are read (below).
4. **The Semantic Dictionary of Biblical Hebrew**, © 2000-2021 United Bible Societies, **"Used with
   permission"**, which is no licence anybody else can rely on: the columns `sdbh`, `lexdomain`,
   `contextualdomain` and `coredomain`. **Never read.**
5. **Cherith Glosses for the Hebrew Old Testament**, Andi Wu, CC BY 4.0: the `gloss`, `english` and
   `mandarin` columns. **Not loaded.**

## What is taken

Into `word_referent`, on BHSA's words:

| column | why |
|---|---|
| `participantref` | which word a pronoun, a demonstrative or a pronoun suffix refers back to; the reader shows the record that word names |

Into `word_parsing`, one row per BHSA word a morph was placed on:

| column | why |
|---|---|
| `class`, `pos`, `type` | of the word's main morph: whether it is a pronoun, which the reader follows a reference through |
| `person`, `gender`, `number` | of a pronoun suffix on the word, as `3ms`: how the reader tells which translation word renders the suffix |
| `lemma` | of the main morph |
| `lang` | Hebrew or Aramaic, so a reader is told which |

Left: `subjref` (the subject of a verb's clause; a translation's words for the verb include the verb
itself and its object, the reason it is left on the Greek too), `frame`, `stem`, `morph`, `greek`,
`greekstrong`, `strongnumberx`, `transliteration`, `extends`, and everything named not loaded above.

## The join to BHSA, and it is checked rather than claimed

MACULA's rows are morphs of the Leningrad Codex (`o010010010011 GEN 1:1!1 בְּ`,
`o010010010012 GEN 1:1!1 רֵאשִׁית`). BHSA prints the same manuscript, but writes the prefixes as words
of their own and keeps a pronoun suffix on its host (`מְלַאכְתּוֹ` is one word). So the loader
(`MaculaHebrewJoin`) places a morph on the BHSA word whose consonants hold its consonants, verse by
verse. Measured on 2026-11-16 (PRB-1727): 22,114 of 23,213 verses have the same consonants on both
sides; in the 1,099 others, nearly all where MACULA writes the qere and BHSA the ketiv (GEN 8:17
הוצא against היצא), the two are aligned letter by letter. A morph whose letters fall in two BHSA words
is refused (70, the article written with `לזה`), as is a verse whose letters agree in less than nine of
ten. Where a verse was aligned past a difference, the parsing's `note` says what each side prints.

## Attribution

Every row written from this data carries the licence's string in its `source` column:

> MACULA Hebrew Linguistic Datasets, available at https://github.com/Clear-Bible/macula-hebrew/

The names owed:

- **MACULA Hebrew Linguistic Datasets**, © 2022-2024 Biblica, Inc, CC BY 4.0,
  <https://github.com/Clear-Bible/macula-hebrew/>.
- **The Open Scriptures Hebrew Bible**, CC BY 4.0, original work available at
  <https://github.com/openscriptures/morphhb>, for the morphology under the parsing.
