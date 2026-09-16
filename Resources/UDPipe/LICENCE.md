# UDPipe — the local analyser and its three models

Four separate works under three different licences, fetched by `scripts/fetch-udpipe.ps1` and never
by the application. Read every section: the tool is permissive, the English model is share-alike,
and the Ukrainian and Russian models are share-alike **and** non-commercial.

This file is the only thing under `Resources/UDPipe/` that a clone gets — `.gitignore` carries
`!Resources/*/LICENCE.md` and nothing else — so what the folder's `PROVENANCE.md` used to say on one
machine is folded in here. The three verbatim legal texts (`CC-BY-NC-SA-4.0.txt`, `CC-BY-SA-4.0.txt`,
`MPL-2.0.txt`) are fetched beside the bytes by the same script, from the canonical URLs given below.

| file | work | rights holder | licence |
|---|---|---|---|
| `models/ukrainian-iu-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Ukrainian-IU | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/russian-syntagrus-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Russian-SynTagRus | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/english-ud-2.1-20180111.udpipe` | udpipe.models.ud, UD_English | bnosac, over the UD_English treebank | **CC BY-SA 4.0** |
| `bin/udpipe.exe` | UDPipe 1.4.0, Windows x64 build | Institute of Formal and Applied Linguistics, Charles University | MPL-2.0 |

---

## The Ukrainian and Russian models

- **Where** LINDAT/CLARIAH-CZ item <http://hdl.handle.net/11234/1-3131>, *Universal Dependencies 2.5
  Models for UDPipe (2019-12-06)*, date issued 2019-12-06. The two model files are bitstreams of
  that item and are downloaded from
  `https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/…/content`, pinned by MD5 and by
  byte count in the fetch script.
- **What** tokeniser, tagger, lemmatiser and dependency parser trained solely on UD 2.5 data
  (<http://hdl.handle.net/11234/1-3105>). The Ukrainian model is trained on UD_Ukrainian-IU, the
  Russian on UD_Russian-SynTagRus.
- **Rights holder** Straka, Milan and Straková, Jana; published by the LINDAT/CLARIAH-CZ digital
  library at the Institute of Formal and Applied Linguistics (ÚFAL), Charles University.
- **Licence: CC BY-NC-SA 4.0.** Read at the item page,
  <https://lindat.mff.cuni.cz/repository/xmlui/handle/11234/1-3131>, on **2026-09-16**: the item
  declares
  <https://creativecommons.org/licenses/by-nc-sa/4.0/> and nothing more permissive appears anywhere
  on it. That is the statement attached to these bytes, and it is the most restrictive one
  (RUL-0105).
- **Citation the depositor asks for**, verbatim from the item page:

  > Straka, Milan and Straková, Jana, 2019, *Universal Dependencies 2.5 Models for UDPipe
  > (2019-12-06)*, LINDAT/CLARIAH-CZ digital library at the Institute of Formal and Applied
  > Linguistics (ÚFAL), Faculty of Mathematics and Physics, Charles University,
  > <http://hdl.handle.net/11234/1-3131>.

## The English model

- **Where** <https://github.com/bnosac/udpipe.models.ud>, branch `master`, file
  `models/english-ud-2.1-20180111.udpipe`.
- **What** the same four tasks for English, built with the `udpipe` R package over the UD 2.1
  UD_English treebank and fastText vectors.
- **Rights holder** bnosac, over the work of the UD_English treebank contributors.
- **Licence: CC BY-SA 4.0.** Read at the source on **2026-09-16**, and three statements were checked
  rather than one, because they are not the same kind of claim:
  - `src/english/LICENSE`, the statement the repository itself points at for this model and the one
    closest to these bytes: *"The model is licensed under the Creative Commons License
    Attribution-ShareAlike 4.0 International (http://creativecommons.org/licenses/by-sa/4.0)."*
  - the repository README's model table: `english-ud-2.1-20180111.udpipe` … `CC BY-SA 4.0`.
  - the GitHub repository record, which reports `license: {"key": "other", "spdx_id":
    "NOASSERTION"}` — GitHub recognises no repository-level licence, which is why the per-model
    `LICENSE` governs rather than merely agrees.

  The repository as a whole calls itself *"liberal udpipe models"* and contrasts itself with
  `jwijffels/udpipe.models.ud.2.0`, which is non-commercial. That is a claim about commercial use,
  not about ShareAlike: the English model carries ShareAlike either way.

## The executable

- **Where** <https://github.com/ufal/udpipe>, release `v1.4.0`, `udpipe-1.4.0-bin.zip`, the
  `bin-win64` build.
- **Licence: MPL-2.0**, from `LICENSE` on tag `v1.4.0`, kept here as `MPL-2.0.txt`
  (<https://www.mozilla.org/en-US/MPL/2.0/>). The tool is run as an external process and is never
  linked into this project, so the file-level copyleft reaches nothing here.

---

## What this folder holds, checked on 2026-09-16

| file | bytes | MD5 | SHA-256 |
|---|---:|---|---|
| `models/ukrainian-iu-ud-2.5-191206.udpipe` | 16,929,888 | `c134ee9fad7989636fc0b6499d0c5e1e` | `ea48223c000cf6b6a33f981fbca63b645fdbc191d76eea0b35df286719d965cd` |
| `models/russian-syntagrus-ud-2.5-191206.udpipe` | 45,859,472 | `8a985b9b7c3902bf76e55f0806ccbbd2` | `16894d425ee328ace668a9d2f7ea5ff945468247d146333e8688f9200fa114e2` |
| `models/english-ud-2.1-20180111.udpipe` | 16,368,326 | `a5e99059a91f04740e1a588732a8c27c` | `20432a6f87b1f258927207b8fbd2dc21ebff9722b381b7a36cef34c8c9a380dc` |
| `bin/udpipe.exe` | 1,527,808 | — | `bced0354b326ab84402aadf860799eff892b805ad80ec051727286cb8a01e797` |

The MD5s are what `scripts/fetch-udpipe.ps1` pins and refuses to merge around; the SHA-256s were
measured here so a second machine can check it holds the same bytes.

## What the corpus does with this, and what it does not

**Today: nothing is stored.** The models produce a lemma, a universal part of speech, morphological
features and a dependency parse, in memory, for one verse at a time, as candidate evidence inside
EVIDENTIA's preview. No UDPipe output reaches Postgres, no `word` column, no `link` row, no
`source` string. Nothing is published, and ShareAlike bites only on what is publicly shared.

**That is the whole of the permission this file records.** The moment a UDPipe-derived lemma, part
of speech or parse becomes a stored column, or the evidence a persisted link rests on, that stored
thing is arguably an adaptation of a ShareAlike corpus — and for Ukrainian and Russian, of a
NonCommercial-ShareAlike one. RUL-0183 draws the line exactly there and weighs it per artefact.
**It has not been weighed.** The question, with the options, is open on **PRB-0518**, and no write
path may be built until the owner has answered it.

## Attribution, owed regardless (RUL-0181)

Ukrainian and Russian analysis: *Universal Dependencies 2.5 Models for UDPipe (2019-12-06)* by Milan
Straka and Jana Straková, LINDAT/CLARIAH-CZ, ÚFAL, Charles University, CC BY-NC-SA 4.0, over the
UD_Ukrainian-IU and UD_Russian-SynTagRus treebanks and their contributors.

English analysis: `udpipe.models.ud` by bnosac, CC BY-SA 4.0, over the UD_English treebank and its
contributors.

Tooling: UDPipe 1.4.0 by the Institute of Formal and Applied Linguistics, Charles University,
MPL-2.0.
