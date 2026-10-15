# UDPipe — the local analyser and its eighteen models

Nineteen separate works under three different licences, fetched by `scripts/fetch-udpipe.ps1` and
never by the application. Read every section: the tool is permissive, the English model is
share-alike, and every model from LINDAT is share-alike **and** non-commercial.

This file is the only thing under `Resources/UDPipe/` that a clone gets — `.gitignore` carries
`!Resources/*/LICENCE.md` and nothing else — so what the folder's `PROVENANCE.md` used to say on one
machine is folded in here. The three verbatim legal texts (`CC-BY-NC-SA-4.0.txt`, `CC-BY-SA-4.0.txt`,
`MPL-2.0.txt`) are fetched beside the bytes by the same script, from the canonical URLs given below.

| file | work | rights holder | licence |
|---|---|---|---|
| `models/ukrainian-iu-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Ukrainian-IU | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/russian-syntagrus-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Russian-SynTagRus | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/english-ud-2.1-20180111.udpipe` | udpipe.models.ud, UD_English | bnosac, over the UD_English treebank | **CC BY-SA 4.0** |
| `models/german-hdt-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, German-HDT | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/spanish-ancora-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Spanish-AnCora | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/arabic-padt-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Arabic-PADT | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/ancient_greek-proiel-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Ancient Greek-PROIEL | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/latin-proiel-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Latin-PROIEL | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/french-gsd-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, French-GSD | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/portuguese-bosque-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Portuguese-Bosque | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/hindi-hdtb-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Hindi-HDTB | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/marathi-ufal-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Marathi-UFAL | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/tamil-ttb-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Tamil-TTB | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/telugu-mtg-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Telugu-MTG | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/korean-kaist-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Korean-Kaist | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/vietnamese-vtb-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Vietnamese-VTB | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/chinese-gsd-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Chinese-GSD | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `models/chinese-gsdsimp-ud-2.5-191206.udpipe` | UD 2.5 Models for UDPipe, Chinese-GSDSimp | Milan Straka and Jana Straková, ÚFAL, Charles University | **CC BY-NC-SA 4.0** |
| `bin/udpipe.exe` | UDPipe 1.4.0, Windows x64 build | Institute of Formal and Applied Linguistics, Charles University | MPL-2.0 |

---

## The models from LINDAT (every model but the English one)

- **Where** LINDAT/CLARIAH-CZ item <http://hdl.handle.net/11234/1-3131>, *Universal Dependencies 2.5
  Models for UDPipe (2019-12-06)*, date issued 2019-12-06. Each model file is a bitstream of that item
  and is downloaded from
  `https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/…/content`, pinned by MD5 and by
  byte count in the fetch script.
- **What** tokeniser, tagger, lemmatiser and dependency parser trained solely on UD 2.5 data
  (<http://hdl.handle.net/11234/1-3105>), each on the treebank its name gives. Where the item has more
  than one model for a language the one kept is the one closest to what we hold, said per language in
  the table below.
- **Why UD 2.5.** It is the last release ÚFAL trained for UDPipe 1, which is the executable here. The
  later items (UD 2.6, 2.10, 2.12, 2.15 and 2.17, all CC BY-NC-SA 4.0) are models for UDPipe 2, a
  Python and TensorFlow tool that this binary cannot load; they are the only route to a language
  UD 2.5 lacks (Ancient Hebrew, for one), and none of them is fetched.
- **Rights holder** Straka, Milan and Straková, Jana; published by the LINDAT/CLARIAH-CZ digital
  library at the Institute of Formal and Applied Linguistics (ÚFAL), Charles University.
- **Licence: CC BY-NC-SA 4.0.** Read at the item page,
  <https://lindat.mff.cuni.cz/repository/xmlui/handle/11234/1-3131>, on **2026-09-16**: the item
  declares
  <https://creativecommons.org/licenses/by-nc-sa/4.0/> and nothing more permissive appears anywhere
  on it. That is the statement attached to these bytes, and it is the most restrictive one
  (RUL-0105). Read again on **2026-09-28** from the item record the repository serves
  (`dc.rights`: *Creative Commons - Attribution-NonCommercial-ShareAlike 4.0 International*,
  `dc.rights.uri` the same licence) before the German and Spanish models were added, and a third
  time on **2026-10-15** before the thirteen added that day: `dc.rights` and `dc.rights.uri` unchanged,
  `dc.rights.label` PUB, and the item's only other licence file (`license.txt` in its LICENSE
  bundle) is the depositor's non-exclusive distribution licence to Charles University, which grants
  nothing to a user. The MD5 and byte count pinned for each model are the ones the item's bitstream
  record states.
- **Upstream: the treebanks.** Each model is trained on one UD treebank, whose own licence was read
  on 2026-10-15 from its `LICENSE.txt` at tag `r2.5` of `github.com/UniversalDependencies/UD_<name>`.
  None is more permissive than the model's, so the model's CC BY-NC-SA 4.0 is the term that binds;
  they are recorded because attribution is owed to the treebank as well (RUL-0181).

| model | held text it is for | treebank licence (r2.5) |
|---|---|---|
| Arabic-PADT | Van Dyck (AVD1865) | CC BY-NC-SA 3.0 (*same terms as PADT 1.0*) |
| Ancient Greek-PROIEL | every Greek witness; PROIEL holds the Greek New Testament | CC BY-NC-SA: the text says 3.0 Generic, its link is 4.0 |
| Latin-PROIEL | Clementine Vulgate (VULGCLEM); PROIEL holds Jerome's New Testament | CC BY-NC-SA 4.0 |
| French-GSD | Darby (JND2024), Louis Segond (LSG1910) | CC BY-SA 4.0 |
| Portuguese-Bosque | Almeida 1911 (ALM1911) | CC BY-SA 4.0 |
| Hindi-HDTB | IRV Hindi (IRV2019) and IRV Urdu (IRVURD), which is printed in Devanagari | CC BY-NC-SA 4.0 |
| Marathi-UFAL | IRV Marathi (IRVMAR); a small treebank, so expect a weak model | CC BY-SA 4.0 |
| Tamil-TTB | IRV Tamil (IRVTAM); a small treebank as well | CC BY-NC-SA 3.0 Unported |
| Telugu-MTG | IRV Telugu (IRVTEL); the treebank has no lemmas, so neither has the model | CC BY-SA 4.0 |
| Korean-Kaist | Korean Revised Version (KRV); the larger of the two Korean treebanks | CC BY-SA 4.0 |
| Vietnamese-VTB | Vietnamese Literal Text (VIGLT) | CC BY-SA 4.0 |
| Chinese-GSD | Chinese Union Version, traditional characters (CUV) | CC BY-SA 4.0 |
| Chinese-GSDSimp | Chinese Union Version, simplified characters (CUVS) | CC BY-SA 4.0 |

The item's Urdu model (Urdu-UDTB) is not fetched: its treebank is in the Arabic script and the one
Urdu text held is printed in Devanagari, which the Hindi model reads. Its Old Church Slavonic-PROIEL
and Old Russian-TOROT models were fetched and read against the Elizabeth Bible on 2026-10-15 and not
kept: that Bible is printed in the civil alphabet, and the Russian SynTagRus model above reads it
better than either.

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

## What this folder holds, checked on 2026-09-16, 2026-09-28 and 2026-10-15

| file | bytes | MD5 | SHA-256 |
|---|---:|---|---|
| `models/ukrainian-iu-ud-2.5-191206.udpipe` | 16,929,888 | `c134ee9fad7989636fc0b6499d0c5e1e` | `ea48223c000cf6b6a33f981fbca63b645fdbc191d76eea0b35df286719d965cd` |
| `models/russian-syntagrus-ud-2.5-191206.udpipe` | 45,859,472 | `8a985b9b7c3902bf76e55f0806ccbbd2` | `16894d425ee328ace668a9d2f7ea5ff945468247d146333e8688f9200fa114e2` |
| `models/english-ud-2.1-20180111.udpipe` | 16,368,326 | `a5e99059a91f04740e1a588732a8c27c` | `20432a6f87b1f258927207b8fbd2dc21ebff9722b381b7a36cef34c8c9a380dc` |
| `models/german-hdt-ud-2.5-191206.udpipe` | 62,982,275 | `7f0a828e795c0f0c630629b4ab1a017d` | `881bda50fb1e72f88d5765ad5e670af8ceb203c8bd128a6d783ad8de7b4af43a` |
| `models/spanish-ancora-ud-2.5-191206.udpipe` | 20,400,504 | `08e63ea07501b2917d29c011276a499a` | `eb7c37977de18072d1f63740c06c588620d48b51dc0c0d0a3604a21db12600fc` |
| `models/arabic-padt-ud-2.5-191206.udpipe` | 19,752,087 | `4c4e36160d4d62fb096857aceed0a9a4` | `5637b73cc30ebd231ccad7ee4ee57d7c8367f6b1883aae09128e656b760e7be3` |
| `models/ancient_greek-proiel-ud-2.5-191206.udpipe` | 22,597,368 | `26ba3e4b8f9b0b1dc0d853841ace3e3c` | `e0885e3171713613789546c81016e54eef1f92386be4a119dc1d8bc9951c386f` |
| `models/latin-proiel-ud-2.5-191206.udpipe` | 23,839,923 | `9418fc6a1c750e7d94791dc19d32f068` | `5ed3f15764b8d1f811528a17498878a50dcc508561753ff06c3dfd0dd6decd69` |
| `models/french-gsd-ud-2.5-191206.udpipe` | 23,811,948 | `8b558d0398b6d6fabeddc0cf4454f950` | `3b10b4c87e7667ac4cf5b301fa075720303c6a1d101cd11a62abde670bebea30` |
| `models/portuguese-bosque-ud-2.5-191206.udpipe` | 16,974,480 | `a6d426f6e49b95f8425bc5cc546c4c77` | `6d6ff79c2388347426e8dc29c61b759a437ee55789f5925fe21a6df955aac4af` |
| `models/hindi-hdtb-ud-2.5-191206.udpipe` | 25,857,814 | `31c61702c78985b6720a9f6ecce6557b` | `d7a77399e6eccee9103d8df9b441ec25ba6ba9f4db453eed3f4ddb77acdd7f2a` |
| `models/marathi-ufal-ud-2.5-191206.udpipe` | 2,382,087 | `d6ac0c0cae7c5b9216bfa1622cd3e6f3` | `dd794535428fd56a57c80955a36eff2ae7deb2ded2cb9aef31305ccd1e3ddbe5` |
| `models/tamil-ttb-ud-2.5-191206.udpipe` | 3,201,866 | `c5e4ff1566798f96905eaa01e70e1732` | `0d08ce5183e6385693d597b3c901769fee7a961ea8ca46ac0c4fdb2b632a3783` |
| `models/telugu-mtg-ud-2.5-191206.udpipe` | 2,168,122 | `7bcfa7add2d74fa49cb51f2b1cad5b0e` | `3cfdd2aab9a1c7b69f796b57f9fc7c7694d83b14211474c258cf905d1ba6a17c` |
| `models/korean-kaist-ud-2.5-191206.udpipe` | 41,461,296 | `b49f974d6886fb24fccd129e4ed9d2a9` | `4f5f11fba8a3d3ae98308be55f3174632b3c0a32c467fe29944e686a4dbdf691` |
| `models/vietnamese-vtb-ud-2.5-191206.udpipe` | 4,673,798 | `2f029f63a9ddc4f95d3154ef30434007` | `d9c0c03c9775391ab42c37beb2f35b529b0432b0a3a94a035ece0363e3e07ee5` |
| `models/chinese-gsd-ud-2.5-191206.udpipe` | 13,863,905 | `6268b716f0be98553c73815593d72ef1` | `08fdd26fcfa0634fa6b81d84eda51a7ca8ed88024dd5accf75c256c85793a001` |
| `models/chinese-gsdsimp-ud-2.5-191206.udpipe` | 11,864,178 | `4db292a2b8d8bb88b962589a5d6a82bf` | `2138c2efe2426f8a48c767ad3a35629a565e0f0fd37d13c3a007903236c6736d` |
| `bin/udpipe.exe` | 1,527,808 | — | `bced0354b326ab84402aadf860799eff892b805ad80ec051727286cb8a01e797` |

The MD5s are what `scripts/fetch-udpipe.ps1` pins and refuses to merge around; the SHA-256s were
measured here so a second machine can check it holds the same bytes.

## Languages held with no model

No UDPipe 1 model exists in UD 2.5, the last release for this executable, for: Assamese (IRVASM),
Bengali (IRVBEN; UD 2.5's Bengali treebank is a test set with nothing to train on), Gujarati
(IRVGUJ), Kannada (IRVKAN), Malayalam (IRVMAL), Punjabi (IRVPAN), Hausa (OHCB), Ge'ez (GEEZ81) and
Ancient Hebrew (BHSA, SP; BHSA states its own analysis, and UD 2.5's Hebrew model is of the modern
language). Church Slavonic (CSLELIZABETH) has no model of its own kept; see above.

## What the corpus does with this, and what it does not

**Nothing a model produces is stored as a column.** The models give a lemma, a universal part of
speech, morphological features and a dependency parse, in memory, for one chapter at a time, as
evidence inside EVIDENTIA. No `word` column holds UDPipe output.

**What it can stand behind is a link.** The owner decided on PRB-0518 (2026-09-16) that a
UDPipe-derived analysis may be the evidence under a persisted link, accepting ShareAlike: those links
are published under CC BY-SA 4.0, and anything resting on a LINDAT model under CC BY-NC-SA 4.0 as
well. The German and Spanish models were fetched for TSK-0717 with the owner's permission for that
use. What they stand behind is narrow: a German or Spanish article marked *supplied* (an `expands`
link naming that one word) because the parse reads it as the article of a noun the original writes
without one. The rule's rationale, which every such link's claim carries in its note, names the
model and its licence, so the obligation can be traced link by link rather than assumed over the
corpus. RUL-0183 weighs it per artefact: the link is a statement about one word of one translation,
and nothing of the model or its treebank is reproduced.

The thirteen models added on 2026-10-15 were fetched with the owner's permission (NOT-0201, and in
chat that day for every language held) and stand behind no link yet: EVIDENTIA reads a model only
for a language it has been given one for in code, and none of these has been.

## Attribution, owed regardless (RUL-0181)

Ukrainian and Russian analysis: *Universal Dependencies 2.5 Models for UDPipe (2019-12-06)* by Milan
Straka and Jana Straková, LINDAT/CLARIAH-CZ, ÚFAL, Charles University, CC BY-NC-SA 4.0, over the
UD_Ukrainian-IU and UD_Russian-SynTagRus treebanks and their contributors.

German and Spanish analysis: the same item's German-HDT and Spanish-AnCora models, CC BY-NC-SA 4.0,
over the UD_German-HDT and UD_Spanish-AnCora treebanks and their contributors.

Arabic, Ancient Greek, Latin, French, Portuguese, Hindi, Marathi, Tamil, Telugu, Korean, Vietnamese
and Chinese analysis: the same item's Arabic-PADT, Ancient Greek-PROIEL, Latin-PROIEL, French-GSD,
Portuguese-Bosque, Hindi-HDTB, Marathi-UFAL, Tamil-TTB, Telugu-MTG, Korean-Kaist, Vietnamese-VTB,
Chinese-GSD and Chinese-GSDSimp models, CC BY-NC-SA 4.0, over the UD treebanks of those names and
their contributors.

English analysis: `udpipe.models.ud` by bnosac, CC BY-SA 4.0, over the UD_English treebank and its
contributors.

Tooling: UDPipe 1.4.0 by the Institute of Formal and Applied Linguistics, Charles University,
MPL-2.0.
