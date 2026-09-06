# parsing/\*-morphgnt.txt — MorphGNT's analysis of the SBL Greek New Testament

**MorphGNT: SBLGNT Edition**, edited by **James K. Tauber**. Morphological parsing and lemmatisation
of the New Testament, one line per word, seven tab-free columns.

From <https://github.com/morphgnt/sblgnt>, read at the source on **2026-09-06** at commit
**aaed91e57c8e4a8dc9a2383e129ca5e75fe6393d**, which is the head of `master` and the state of version
**6.12**. 137,554 words in 27 books.

The citation the project asks for, verbatim from its README:

> Tauber, J. K., ed. (2017) *MorphGNT: SBLGNT Edition*. Version 6.12 [Data set].
> https://github.com/morphgnt/sblgnt DOI: 10.5281/zenodo.376200

## Two works, two licences, and only one of them is exercised

**The repository carries no `LICENSE` file.** `LICENSE`, `LICENSE.md` and `COPYING` are all absent
from the tree, and the GitHub API reports `license: null`. The only statement attached to these
bytes is in `README.md`, kept beside this file as `README-upstream.md`, and it says two different
things about two different works in one sentence:

> The SBLGNT text itself is subject to the [SBLGNT EULA](http://sblgnt.com/license/)
> and the morphological parsing and lemmatization is made available under a
> [CC-BY-SA License](http://creativecommons.org/licenses/by-sa/3.0/).

### The parsing — CC BY-SA 3.0

Columns 2 and 3 (part of speech, parsing code) and column 7 (lemma) are Tauber's work, under
**Creative Commons Attribution-ShareAlike 3.0 Unported**, <https://creativecommons.org/licenses/by-sa/3.0/>,
read at the source on 2026-09-06:

> **Share** — copy and redistribute the material in any medium or format
>
> **Adapt** — remix, transform, and build upon the material for any purpose, even commercially.
>
> **Attribution** — You must give appropriate credit, provide a link to the license, and indicate
> if changes were made.
>
> **ShareAlike** — If you remix, transform, or build upon the material, you must distribute your
> contributions under the same license as the original.

**This is share-alike, and it is taken deliberately.** RUL-0183 names ShareAlike as the clause that
reaches this corpus, and PRB-0344 is the finding that the reach it fears is the case Creative
Commons explicitly excludes: the condition binds an *adaptation*, and a collection of separate works
is not one. The owner's decision, recorded in that finding's last comment, names this dataset by
name as one to take. What is stored from it is a parsing per word — no text — and what obligation
that carries is per artefact, not corpus-wide.

### The text — CC BY 4.0, and the README's link for it is stale

Columns 4, 5 and 6 (the printed word, the word with punctuation stripped, the normalised form) are
the **SBL Greek New Testament**, edited by Michael W. Holmes, © 2010 the Society of Biblical
Literature and Logos Bible Software. That is a different work with its own terms, and the terms
changed after this README was written.

The README calls the destination the *SBLGNT EULA*. The URL it points at now serves something else.
Read at the source on 2026-09-06:

- **<https://sblgnt.com/license/>** still carries the page title *SBL Greek New Testament — End User
  License Agreement*, and the body served there is the full text of the **Creative Commons
  Attribution 4.0 International Public License**: *"reproduce and Share the Licensed Material, in
  whole or in part; and produce, reproduce, and Share Adapted Material."* The old restrictive EULA
  is served nowhere that could be found.
- **<https://sblgnt.com>** says it plainly in its own prose: *"The SBLGNT is licensed freely under
  the Creative Commons Attribution 4.0 International Public License."*
- **<https://github.com/Faithlife/SBLGNT>** — `LICENSE` is the CC BY 4.0 text; `README.md` says
  *"The SBLGNT is licensed under a Creative Commons Attribution 4.0 International License"* and
  *"Copyright 2010 by the Society of Biblical Literature and Logos Bible Software"*; GitHub reports
  `license.spdx_id = CC-BY-4.0`. The repository's own version log records the event:
  *v1.1, 2022-12-19, "Update public version and license on github"*.

One page contradicts them, and it is worth writing down rather than leaving for the next reader to
find: **<https://sblgnt.com/about/>** still says the text may be downloaded *"for personal study
and research as well as for limited use in scholarly publications (see the End-User License
Agreement)"*. That is prose from before the relicensing, on a page nobody updated, and it points at
the URL that now serves CC BY 4.0. RUL-0105 says to take the most restrictive statement actually
attached to the bytes; the bytes are in the Faithlife repository and the statement attached to them
is CC BY 4.0.

TSK-0223 read all of this independently on 2026-09-03 and reached the same reading.

## What is taken and what is left

**Only the parsing is loaded.** Columns 2, 3 and 7 become a `word_parsing` row against a Nestle 1904
word. The four text columns are read to work out *which* Nestle word each parsing belongs to and are
then dropped: no SBLGNT word is stored, no `text` row for the SBLGNT is created, and nothing this
corpus serves reproduces that edition. Taking the share-alike layer without taking the text is the
shape this was deliberately given — the text is the more permissively licensed of the two and is
still not taken, because loading a second Greek witness is a different piece of work (TSK-0223) with
its own linking cost.

The files kept beside this one hold all seven columns, because that is how the source publishes them
and a copy that has been edited is no longer the thing whose licence was read. They are the local
working copy the loader reads; they are not redistributed.

## Attribution, which is a condition here and not a courtesy

Both licences require it, and RUL-0181 would anyway. Every row written from this data names
`morphgnt/sblgnt` and its version in the `source` column, so a reader who asks where a parsing came
from is told, per word; which commit of that version was read is here, once, rather than 136,404
times in the database. The names owed:

- **MorphGNT: SBLGNT Edition**, ed. James K. Tauber, CC BY-SA 3.0,
  <https://github.com/morphgnt/sblgnt>.
- **The SBL Greek New Testament**, ed. Michael W. Holmes, © 2010 the Society of Biblical Literature
  and Logos Bible Software, CC BY 4.0, <https://sblgnt.com>. Named because its text is what the
  parsing was made against and what the join was measured on, even though none of it is stored.

## What this data does not contain, so that nobody looks twice

**No proper-noun class.** All 137,554 words carry one of thirteen part-of-speech codes and `N-` is
the only noun among them; the eight-slot parsing code is person, tense, voice, mood, case, number,
gender and degree, and has no nominal type. The README says why: *"The part of speech and parsing
codes were inherited from the CCAT tagging."* PRB-0346 is the finding, and names the dataset that
does state it.

**No Strong numbers.** Every other Greek witness here carries one on every word and joins to the
rest for nothing. This does not, which is why the join to Nestle is an alignment and not a lookup.

**No vocative in the README, but a vocative in the data.** The parsing code's case slot is
documented as N, G, D or A. The files write `V` as well, 668 times. Of the 588 words Nestle 1904
also calls vocative and this join reaches, MorphGNT agrees on 585 — so the undocumented letter is a
gap in the README rather than a defect in the data, and a reader that dropped it would read 668
words as having no case at all.
