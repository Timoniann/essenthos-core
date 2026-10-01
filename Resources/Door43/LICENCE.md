# Door43 — unfoldingWord USFM 3.0 word alignment

Eighteen sources live here, all translations with each of their words tied by hand to the original
word it renders, all fetched from `git.door43.org`. They arrive in the same format and are read
by the same reader, and their terms are **not** the same. Read every section.

Everything quoted below was read from the files themselves, on 2026-09-04 for `ru_rsb` and on
2026-09-02 for `uk_ubio` (DOC-0003).

---

## `uk_ubio` — Ukrainian Bible Interlinear Ogienko

- **Where** <https://git.door43.org/uk_ts/uk_ubio>, branch `master`.
- **What** twelve books of the Ohienko 1962 Ukrainian translation, aligned to unfoldingWord's own
  Hebrew (`hbo/uhb` 2.1.26) and Greek (`el-x-koine/ugnt` 0.26). The corpus keeps 7,109 stated
  links from it, and it is the standard every model here is calibrated against.
- **Rights holder** unfoldingWord, with the per-book work done by the Door43 Ukrainian community.
- **Licence: CC BY-SA 4.0.** `manifest.yaml` states `rights: CC BY-SA 4.0`, and `LICENSE.md`
  reads, verbatim:

  > This is a human-readable summary of (and not a substitute for) the full license found at
  > http://creativecommons.org/licenses/by-sa/4.0/.

  The Door43 licence file the ecosystem carries adds, verbatim:

  > **Adapt** — remix, transform, and build upon the material, for any purpose, **even
  > commercially**.
  > **ShareAlike** — If you remix, transform, or build upon the material, you must distribute your
  > contributions under the same license as the original.

  unfoldingWord's own statement adds a trademark condition, verbatim:

  > If you modify a copy or translate this work, thereby creating a derivative work, you must
  > remove the unfoldingWord® trademark.

- **What that obliges, plainly.** Re-serialising these alignments into the corpus is a derivative
  work, so the corpus's published form of *these links* has to be offered under CC BY-SA 4.0 and
  must not carry the unfoldingWord mark. That is the heaviest condition on any source in this
  tree, and it was taken when this file was loaded rather than decided; RUL-0183 names ShareAlike
  as the clause to weigh, and nothing in the store weighs it. It is on the owner's desk, not
  settled here.

- **A separate question, about the text rather than the alignment.** Sixteen of these USFM files
  head every book `\rem Copyright British and Foreign Bible Society`, while `LICENSE.md` says
  CC BY-SA 4.0 and our own copy of the Ohienko text asserts public domain. Ohienko died in 1972.
  The `text` row for `ukr` carries this in its `rights_note`; it is not settled either.

---

## `ru_rsb` — Russian Synodal Bible, three books

- **Where** three per-book repositories on the same host, branch `master`:
  - <https://git.door43.org/Anna/ru_rsb_tit_book> → `57-TIT.usfm`, 672 milestones
  - <https://git.door43.org/Anna/ru_rsb_phm_book> → `58-PHM.usfm`, 340 milestones
  - <https://git.door43.org/Anna/ru_rsb_2jn_book> → `64-2JN.usfm`, 254 milestones
- **What** Titus, Philemon and 2 John of the 1876 Russian Synodal translation, aligned word by
  word to the Greek in translationCore. **This is the whole of what exists**: the Door43 catalogue
  was paged in full and no other Russian Synodal book is aligned by anybody. Several other uploads
  of these same three books exist and are equal or shorter; Anna's are the fullest of each.
- **Rights holder** none asserted. The 1876 Synodal text is out of copyright by age; the alignment
  is dedicated to the public domain by the person who made it.
- **Licence: CC0 1.0.** Each repository's `manifest.json` states `"license": "CC0 1.0 Public
  Domain"`, and each `LICENSE.md` — byte-identical across the three — reads, verbatim:

  > ### Public Domain
  > No known copyright
  >
  > # CC0 License
  > ## Creative Commons CC0 1.0 Universal (CC0 1.0) Public Domain Dedication
  >
  > The person who associated a work with this deed has **dedicated** the work to the public domain
  > by waiving all of his or her rights to the work worldwide under copyright law, including all
  > related and neighboring rights, to the extent allowed by law.
  >
  > You can copy, modify, distribute and perform the work, even for commercial purposes, all
  > without asking permission.

  The file each was read under is kept beside the data as `LICENSE-57-TIT.md`,
  `LICENSE-58-PHM.md` and `LICENSE-64-2JN.md`.

- **The same three books are also published under a different licence, and that is worth knowing
  before anyone repeats a claim about them.** `BSA/ru_rsb`, `Door43-Catalog/ru_rsb`, `STR/ru_rsb`
  and `IvanFedorovPress/ru_rsb` are complete 66-book Synodal repositories whose only alignment is
  these three books, exactly these milestone counts — and their `manifest.yaml` states
  `rights: 'CC BY-SA 4.0'`, with a `LICENSE.md` reading:

  > This work is made available under the Creative Commons Attribution-ShareAlike 4.0
  > International License. […] The original work of the Russian Synodal Bible is in the public
  > domain.

  So one alignment, two statements. The copies taken here are the per-book ones, whose CC0 is the
  statement attached to the bytes this corpus holds (RUL-0105) and which is also the more
  permissive of the two — and where two statements disagree that combination is worth stating
  rather than assuming. Nothing about the aggregate is relied on.

- **Attribution**, which CC0 does not require and RUL-0181 does: the Russian Synodal alignment of
  Titus, Philemon and 2 John was made in translationCore by contributors to the Door43 World
  Missions Community and published at `git.door43.org` under CC0 1.0.

---

## `en_ult` — unfoldingWord® Literal Text, release 90

- **Where** <https://git.door43.org/unfoldingWord/en_ult>, tag `v90` (2026-08-17), archive
  `archive/v90.zip`, SHA-256 `a89587587717d1cb30d1f9d4727a206e2ee5488291a3c2425cc0529ceecad509`,
  fetched by `scripts/fetch-door43-ult.ps1` on 2026-09-25 with the owner's approval (NOT-0195,
  DOC-0207 §6 item 9). `LICENSE.md` and `manifest.yaml` of the release are kept beside the books.
- **What** the English ULT, an open revision of the ASV 1901, in the 56 books unfoldingWord has
  finished checking (Numbers, 1-2 Chronicles, Ecclesiastes, Isaiah, Jeremiah, Ezekiel, Daniel, Amos
  and Zechariah are not in the release). Every English word stands in a `\zaln-s` milestone naming
  the word of unfoldingWord's Hebrew Bible (`hbo/uhb` 2.1.26) or Greek New Testament
  (`el-x-koine/ugnt` 0.26) it renders: 357,632 milestones. The same files are loaded as the text
  `ULT` (23,186 verses, 568,381 words) and read for its links.
- **Licence: CC BY-SA 4.0.** `manifest.yaml` states `rights: CC BY-SA 4.0`; `LICENSE.md` reads:

  > This work is made available under the Creative Commons Attribution-ShareAlike 4.0 International
  > License. … Under the terms of the CC BY-SA license, you may copy and redistribute this unmodified
  > work as long as you keep the unfoldingWord® trademark intact. If you modify a copy or translate
  > this work, thereby creating a derivative work, you must remove the unfoldingWord® trademark. On
  > the derivative work, you must indicate what changes you have made and attribute the work as
  > follows: "The original work by unfoldingWord is available from unfoldingword.org/ult". You must
  > also make your derivative work available under the same license (CC BY-SA).

- **What that obliges.** The text row carries `share-alike` and is served unchanged under
  unfoldingWord's name. The links are re-serialised from the alignment, the same position as
  `uk_ubio` above: their published form is CC BY-SA 4.0, credited as the licence asks
  (`Datasets.cs`, `unfoldingword-ult`).
- **How it is joined.** unfoldingWord's Hebrew and Greek are not BHSA and Nestle 1904. A span is
  joined where its original word is spelled, morpheme by morpheme, in the same canonical verse of
  ours with the occurrence it states; otherwise by its Strong number only where that lemma stands
  once in the source verse and once in ours, with any prefix spelled by the words before it; anything
  else is refused. Measured on a scratch copy on 2026-09-25: 331,393 of 338,583 spans joined
  (97.9%, 808 of them by number), 98.0% of the Old and 98.4% of the New Testament's words linked.

---

## The Door43 "Aligned Bible" catalogue — fifteen releases taken 2026-10-01

Taken on 2026-10-01 with the owner's approval of that day (TSK-0911, NOT-0204), by
`scripts/fetch-door43-aligned.ps1`, each from a tagged release pinned by the SHA-256 of its archive.
The catalogue was read at `https://git.door43.org/api/v1/catalog/search?subject=Aligned%20Bible&stage=prod`
(67 entries); its licence fields are empty, so every release's own licence file and manifest were read
at the tag before anything was kept, and both are kept beside its books.

| folder | repository, tag | archive SHA-256 | books | milestones | text it ties |
|---|---|---|---:|---:|---|
| `ar_avd` | `BSOJ/ar_avd` v6.9 | `17d9a3a0f921b649bfb9faae802bbd5f62c9f1b124f266068f129055fb95d944` | 66 | 448,187 | `AVD1865`, held from eBible |
| `hi_irv` | `Door43-Catalog/hi_irv` v12 | `7668204fa80ca72323b3be8c5ea13b24cfba0571a35ae348a4a4ddc83db7c931` | 66 | 430,532 | `IRV2019`, held from eBible |
| `fr_lsg` | `fr_gl/fr-textTranslation-FR_LSG` v1 | `9cda1da7f077c1072aed3375309c12a3785c4782edd6cfd1d3717c8ab2ae850d` | 27 | 116,124 | `LSG1910`, held from eBible |
| `bn_irv` | `Door43-Catalog/bn_irv` v5 | `98ab617112e05416f17e7a7a269fa83160d25d6923a205c5fd873e6df5eca4ac` | 66 | 101,374 | `IRVBEN`, loaded from it |
| `as_irv` | `Door43-Catalog/as_irv` v3 | `017d4634b1459adf2088a717dfc23c99ccb98906068543c3136fa4f7dde0b3ca` | 66 | 118,110 | `IRVASM`, loaded from it |
| `gu_irv` | `Door43-Catalog/gu_irv` v4 | `aba5949bc5c59a77c8bfd204b1da176bf871e64662c3fc602d9411c655f249cd` | 27 | 108,616 | `IRVGUJ`, loaded from it |
| `kn_irv` | `Door43-Catalog/kn_irv` v5 | `74037257ec8801a852a37b3e77fc58f95fea032036474610d8d093319e1342d8` | 27 | 93,859 | `IRVKAN`, loaded from it |
| `ml_irv` | `Door43-Catalog/ml_irv` v5 | `ef2b4f702ebab5970f06c3621e7c196325aeaa792f7289e1fff39c55ac75bbef` | 66 | 79,528 | `IRVMAL`, loaded from it |
| `mr_irv` | `Door43-Catalog/mr_irv` v4 | `1446aaf6262a2aea46cd43ad6f5312727e77b721fd0efc219d6632435e7b832b` | 27 | 113,022 | `IRVMAR`, loaded from it |
| `pa_irv` | `Door43-Catalog/pa_irv` v3 | `e14a6fa3aa084195fc3f36fe65bdf3e87f7cafe97c5071bba4b23f24dba3a38e` | 27 | 128,991 | `IRVPAN`, loaded from it |
| `ta_irv` | `Door43-Catalog/ta_irv` v3 | `61ab8aaa43020343d06faee82e0c656a0f92385a4f9b128da7a19c8d29224c9b` | 66 | 102,597 | `IRVTAM`, loaded from it |
| `te_irv` | `Door43-Catalog/te_irv` v2 | `b8be7a8e6483da9ff4b541cc7b09e6402c559c68ab8bc323ff9ccd43e29226bd` | 27 | 96,367 | `IRVTEL`, loaded from it |
| `ur-deva_irv` | `Door43-Catalog/ur-deva_irv` v2 | `1c54f758117f89a60811c5ba443a8eafb34c6fddb00007f67490f93505b11920` | 66 | 119,570 | `IRVURD`, loaded from it |
| `en_ust` | `unfoldingWord/en_ust` v91 | `0c7c3f9747f58a08a9901d1acfc26cd1356aebdb161671a3becfc403dca3d28c` | 59 | 397,226 | `UST`, loaded from it |
| `vi_glt` | `vi_gl/vi_glt` v1 | `a3c056905e1d7a17285479e336425c196e628200bebb1d9f76277127f1444a4f` | 28 | 123,933 | `VIGLT`, loaded from it |

All align to unfoldingWord's Hebrew Bible and Greek New Testament and are joined to BHSA and Nestle
1904 the way `en_ult` is. `ar_avd`, `hi_irv` and `en_ust` align both testaments, `vi_glt` the New
Testament and Ruth, every other the New Testament only — though `bn_irv`, `as_irv`, `ml_irv`, `ta_irv`
and `ur-deva_irv` carry the whole Bible's text, and it is loaded.

**The older exports name many original words by spelling alone.** `x-strong` is empty on 58,973 of
`hi_irv`'s milestones, 32,520 of `bn_irv`'s, 29,491 of `as_irv`'s, and tens of thousands of each other
IRV's (`gu` 67,415, `pa` 70,526, `mr` 65,199, `ur-deva` 62,466, `ta` 55,957, `kn` 46,627, `te` 46,184,
`ml` 28,678); never on `ar_avd`, `en_ust`, `vi_glt` or `fr_lsg`. The spelling and its occurrence, which
is what the join places a word by, are always there. These releases are read accepting such a
milestone; `uk_ubio`, `ru_rsb` and `en_ult` are read as before.

**Two releases need a word about their files.** `ar_avd` ships Second Chronicles twice, as
`13-2CH.usfm` and `14-2CH.usfm`, byte for byte; the fetch keeps `14-2CH.usfm`. `fr_lsg` is a Scripture
Burrito rather than a resource container: its books are `ingredients/MAT.usfm` and so on, its licence
`ingredients/license.md` and its metadata `metadata.json`, which names that file as the licence; the
fetch takes the books and both files flat into the folder.

### Terms — all CC BY-SA 4.0

- **`ar_avd`.** `manifest.yaml`: `rights: CC BY-SA 4.0`, creator and publisher `BSOJ`. Its
  `LICENSE.md` is the unfoldingWord Literal Text's, copied with the template — it opens
  *unfoldingWord® Literal Text, Copyright © 2022 by unfoldingWord* — and says CC BY-SA 4.0 with the
  trademark conditions quoted under `en_ult` above. The two agree on the licence. The text under the
  alignment is the Van Dyck, public domain; nothing of the release's text is loaded.
- **The ten IRV releases.** Each `manifest.yaml` states `rights: 'CC BY-SA 4.0'`, creator *Door43 World
  Missions Community*, publisher `BCS` (Bridge Connectivity Solutions) — `pa_irv`'s says `Door43` —
  checking level 3. Each `LICENSE.md` is the Creative Commons BY-SA 4.0 summary; several ask for the
  attribution *"Original work available at https://door43.org/."* Clear Bible's metadata for the
  Bengali and Assamese text, at the Digital Bible Library, says CC BY 4.0; the more restrictive
  statement attached to the bytes loaded here is CC BY-SA 4.0, and it is the one taken.
- **`en_ust`.** `manifest.yaml`: `rights: CC BY-SA 4.0`, publisher unfoldingWord; `LICENSE.md`:
  *unfoldingWord® Simplified Text, Copyright © 2022 by unfoldingWord*, CC BY-SA 4.0 with the same
  trademark conditions as the Literal Text and the credit *"The original work by unfoldingWord is
  available from unfoldingword.org/ust"*.
- **`vi_glt`.** `manifest.yaml`: `rights: CC BY-SA 4.0`, publisher *Far East Broadcasting Company*;
  `LICENSE.md` the CC BY-SA 4.0 summary.
- **`fr_lsg`.** `ingredients/license.md` is the full legal code of CC BY-SA 4.0, and `metadata.json`
  names it as the licence; there is no `LICENSE.md` at the repository root (404). The text under the
  alignment is the Segond of 1910, public domain; nothing of the release's text is loaded.

**What that obliges.** As for `uk_ubio` and `en_ult`: a text loaded from these releases is served
unchanged under ShareAlike; the links re-serialised from all of them are offered under CC BY-SA 4.0 in
turn (`Datasets.cs`: `door43-avd`, `door43-irv`, `unfoldingword-ust`, `door43-vi`, `door43-lsg`).
RUL-0183: ShareAlike binds those adaptations, not the texts read beside them.

### Measured on a scratch copy (2026-10-01, `essenthos_core_s38`, dropped)

Each alignment loaded after Clear Bible's set for the same text, as the load runs them. *Words linked*
is the share of the text's words in that testament some stated link names.

| text | Door43 links | verses refused | of them, exactly a Clear Bible link's words | words linked by Door43 | by either source |
|---|---:|---:|---:|---|---|
| `AVD1865` | 376,089 | 22 | 209,691 | OT 97.8%, NT 98.1% | OT 99.2%, NT 98.8% |
| `IRV2019` | 398,183 | 26 | 224,105 | OT 69.9%, NT 90.9% | OT 93.6%, NT 96.2% |
| `LSG1910` | 105,176 | 16 | 59,604 | NT 73.5% | OT 33.6%, NT 79.4% |
| `IRVBEN` | 95,399 | 9 | 54,060 | NT 70.3% | NT 89.8% |
| `IRVASM` | 109,262 | 26 | 71,289 | NT 91.1% | NT 96.5% |
| `IRVGUJ` | 101,901 | 19 | — | NT 81.3% | |
| `IRVKAN` | 81,810 | 18 | — | NT 78.5% | |
| `IRVMAL` | 69,752 | 8 | — | NT 72.5% | |
| `IRVMAR` | 108,196 | 15 | — | NT 83.4% | |
| `IRVPAN` | 118,816 | 19 | — | NT 91.4% | |
| `IRVTAM` | 77,901 | 19 | — | NT 80.0% | |
| `IRVTEL` | 86,696 | 20 | — | NT 85.8% | |
| `IRVURD` | 110,492 | 188 | — | NT 83.7% | |
| `UST` | 302,273 | 21 | — | OT 97.9%, NT 98.2% | |
| `VIGLT` | 117,836 | 37 | — | Ruth 91.8%, NT 95.3% | |

Where the two Van Dyck alignments name the same Arabic word and different Hebrew words, it is mostly
granularity rather than disagreement: BSOJ puts وَٱلْأَرْضُ on the whole of וְ־הָ־אָרֶץ, Clear Bible's team
on אֶרֶץ alone (Genesis 1:2).

### Read and not taken (2026-10-01)

- **`BSOJ/ar_arst` v4.9, "Arabic Simplified Text" — not Arabic.** 869,458 of its 891,928 aligned
  words are English: the release holds the unfoldingWord Simplified Text under an Arabic name, with a
  handful of Arabic words. The English is `en_ust`, taken above.
- **Not aligned, or hardly:** `Door43-Catalog/vi_glt` v5.2 (0 milestones), `translationCore-Create-BCS/gu_irv`
  v1 (0; `Door43-Catalog/gu_irv` v4 is taken), `DevleskoDrom/rmy_srb` v2 (0), `MVHS/fr_glt` v0.0.1 (687 in
  27 books), `DevleskoDrom/rml_rhb` v3.2 (750 in 66 books), `Door43-Catalog/tg_ult` v1.1 (236, 3 John alone).
- **`MVHS/fr_lsg` v0.0.2** — the same Segond New Testament with 89,466 milestones, an older and smaller
  alignment than `fr_lsg` above.
- **Partial gateway texts, licence read and not yet loaded** — all `rights: CC BY-SA 4.0` with the CC
  BY-SA 4.0 summary as `LICENSE.md`; each would be a new text of a few books: `es-419_gl/es-419_glt` v42
  (6 books, 6,205 milestones), `es-419_gl/es-419_gst` v40 (4, 3,118), `es-419_obt/es-419_glt` v0.0.5 (4,
  3,023), `bahtraku/id_glt` v105 (9, 9,272), `bahtraku/id_gst` v16 (9, 10,381), `fa_gl/fa_glt` v11 (8,
  16,775), `fa_gl/fa_gst` v11 (7, 7,374), `fa_gl/fa_gbt` v8.6 (1, 252), `rm_ol/rml_rbs` v11 (11, 17,652).
  The `translationCore-Create-BCS` literal and simplified texts (`bn`, `gu`, `hi`, `kn`, `mr`, `ne`, `or`,
  `te` — 16 to 33 books each) and `Door43-Catalog/hi_iev`, `kn_iev` and `kn_ust` were listed and not read.
- The Russian, English ULT and BSB entries are outside this list: `ru_rsb` and `en_ult` are already
  here, and the BSB is aligned by its own tables.
