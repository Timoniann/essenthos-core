# Door43 — unfoldingWord USFM 3.0 word alignment

Two sources live here, both translations with each of their words tied by hand to the original
word it renders, both fetched from `git.door43.org`. They arrive in the same format and are read
by the same reader, and their terms are **not** the same. Read both sections.

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
