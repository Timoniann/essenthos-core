# *.tf — the Text-Fabric dataset of the Samaritan Pentateuch

The **Samaritan Pentateuch**: the Torah as the Samaritan community has transmitted it, which is not
the Masoretic text with mistakes in it but a third Hebrew textual family beside the Masoretic and
the Qumran scrolls.

From <https://github.com/DT-UCPH/sp>, read at the source on **2026-09-04** at commit
**2f2120286ac48d4ff3d04e0107e33efd864aa9e1**, dataset version **7.1.3** under `tf/`. All 41 feature
files are taken; `scripts/fetch-samaritan.ps1` is what fetches them and it re-reads every statement
below before it replaces anything.

**5 books, 187 chapters, 5,841 verses, 114,889 words, 399,392 signs.** The words are morphemes,
segmented the way BHSA segments the Masoretic text, which is what lets the two be compared word for
word rather than only verse by verse.

## The text, and the manuscripts behind it

Stated in the header of 38 of the 41 feature files:

> @source=Stefan Schorch in colloboration with Evelyn Burkhardt, Ulrike Hirschfelder, Irina Wandrey
> and József Zsengellér
>
> @manuscripts=MS Dublin Chester Beatty Library 751 (Gen 1-Deut 32:36) + MS Garizim 1 (Deut
> 32:36b-34)

and in the README:

> The text was provided by the Samaritanus-project based at Martin-Luther-Universität
> Halle-Wittenberg, directed by Stefan Schorch, and is based on a transcription MS Dublin Chester
> Beatty Library 751 (Gen 1-Deut 32:36) + MS Garizim 1 (Deut 32:36b-34), cf. Stefan Schorch (ed.),
> The Samaritan Pentateuch: A critical editio maior. Berlin: de Gruyter, 2018-.

The encoding is the CACCHT project's:

> @convertedToTextFabricBy=Martijn Naaijer and Christian Canu Højgaard
>
> @encodedBy=Christian Canu Højgaard, Saulo de Oliveira Cantanhêde, and Martijn Naaijer

## The licence, and there are four statements of it that do not all agree

**1. The header of the feature files**, which is the statement inside the bytes:

> @licence=Creative Commons Attribution-NonCommercial 4.0 International License
>
> @licenceUrl=http://creativecommons.org/licenses/by-nc/4.0/

38 of the 41 files carry it. `ETCBC_parsing.tf`, `gloss.tf` and `typ.tf` carry no licence line at
all; no file carries a different one.

**2. The README badge:**

> [![License: CC BY-NC 4.0](https://img.shields.io/badge/License-CC_BY--NC_4.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc/4.0/)

**3. The README prose**, which is a grant rather than a licence name:

> You can use the dataset freely for research and education. If you do so, please refer to the
> papers.

**4. The Zenodo deposit**, DOI [10.5281/zenodo.7734632](https://doi.org/10.5281/zenodo.7734632),
whose record for the current version (21350038, July 2026) reports `"license": {"id": "cc-by-4.0"}`
— **plain Attribution 4.0**, with no NonCommercial clause.

**And there is no LICENSE file.** `LICENSE`, `LICENSE.md`, `LICENSE.txt` and `LICENCE` all return
HTTP 404 at the commit above, and the GitHub repository record reports `"license": null`.

### What is believed, and why

**CC BY-NC 4.0**, from the file headers. RUL-0105: read every statement, believe the one closest to
the bytes, and where they disagree take the most restrictive one actually attached to the data. The
header is inside the files this corpus reads; Zenodo's is metadata on a deposit page. The header
is also the stricter, so believing it costs nothing and risks nothing.

**Nothing in the repository claims ShareAlike.** The string does not appear in any of the four
statements. RUL-0183 is what clears this dataset: NonCommercial is acceptable to this project and
ShareAlike on the annotation is the line, and this is on the right side of it.

`Redistribution` on the `text` row is therefore `NonCommercialOnly`.

## What the authors ask to be cited

The README asks for the deposit **and** the papers, and a licence name and a URL cannot carry that,
so both are on the `text` row's `citation` column:

> Christian Canu Højgaard, Martijn Naaijer, & Stefan Schorch. (2023). Text-Fabric Dataset of the
> Samaritan Pentateuch. Zenodo. https://doi.org/10.5281/zenodo.7734632

> Naaijer, M., Højgaard, C. C., Schorch, S., & Ehrensvärd, M. (2024). Text-Fabric Dataset of the
> Samaritan Pentateuch. Research Data Journal for the Humanities and Social Sciences, 9(1), 1-13.
> https://doi.org/10.1163/24523666-bja10051

> Cantanhêde, S. d. O., Naaijer, M., Højgaard, C. C., & Glanz, O. (2026). Identifying Phrase
> Boundaries in the Samaritan Pentateuch with Machine Learning. Religions, 17(2), 192.
> https://doi.org/10.3390/rel17020192

The third paper covers the phrase boundaries, which this corpus does not load yet.

## Two things about the data that a reader should know before trusting a number

**The verse division is the editors' own in one place.** The README:

> We have made a small change in the original verse division. Instead of assigning the additions
> after Genesis 30:36 to the verse numbers 36a, 36b, and 36 c, we group these under verse 36.

**`mt_feat` says which words were parsed from the Masoretic text rather than from this one** —
`@description=features imposed from MT`. It is stated per word and is carried per word, because it
is the difference between annotation that is evidence about this witness and annotation that is
evidence about the other one.

## What is not loaded

`ETCBC_parsing`, `prediction`, `sign`, `typ`, and the phrase and clause node types. The syntax
layer is a second piece of work; the transliterated forms duplicate the Hebrew ones this corpus
already reads; and `prediction` is a neural network's output, which is not a witness to anything.
The files are kept because a copy of a dataset is the dataset.

## Why it is attributed at all

CC BY-NC requires attribution and this would be attributed either way. A reader of this corpus has
to be able to tell what rests on somebody's testimony and what rests on our inference, and a fact
printed without a name has quietly been claimed as ours. RUL-0181.
