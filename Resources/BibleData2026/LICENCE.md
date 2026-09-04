# BibleData

**BibleData: Structured Datasets from the Holy Bible**, by Brady Stephenson,
<https://github.com/BradyStephenson/bible-data>, also on Zenodo as
<https://doi.org/10.5281/zenodo.19539956>.

Taken from github.com/BradyStephenson/bible-data at 8799b409c82a5d4acebba3be5107d6eff7c85d78 on 2026-09-04 by `scripts/fetch-bibledata.ps1`.

## What it is used under: CC BY 4.0

<https://creativecommons.org/licenses/by/4.0/>

## Every statement attached to these bytes, and they now agree

On current `main` all three files in the release say the same thing:

| Where | What it says |
|---|---|
| `LICENSE` | Creative Commons Attribution 4.0 International |
| `README.md` badge and Licence section | CC BY 4.0, adding "including for commercial purposes" |
| `CITATION.cff` | `CC-BY-4.0` |
| the GitHub repository record | Creative Commons Attribution 4.0 International |

That was not always true. The repository was CC BY-NC-SA 3.0 from 2021 and CC BY-NC-SA 4.0 from
April 2026, and its `CITATION.cff` went on saying `CC-BY-NC-SA-4.0` after the LICENSE file had
changed. The author settled that in his own issue tracker — the LICENSE file governs, the CITATION
file was wrong — and has since corrected the CITATION file to match. Nothing in the release
contradicts anything else any more.

So this copy is Attribution 4.0: **no ShareAlike obligation reaches anything derived from it.**
That is the clause worth being sure about, because the annotation this corpus builds on top of the
dataset would inherit it.

**The version matters, and Kaggle is behind.** The `v1.0.0` tag, the Zenodo release, and the
Kaggle dataset page are all still ShareAlike, and the copy Kaggle serves carries the old
`CITATION.cff` saying `CC-BY-NC-SA-4.0` while its fifteen loaded data files are byte-identical
to `main`. Its dataset page states CC BY-NC-SA 3.0 IGO. That is why this script fetches from
GitHub by default: same data, current terms, and a commit that can be recorded. A copy taken from
anywhere else would bind the corpus to ShareAlike without anyone deciding to.

## Attribution

Credited at `/v1/datasets` whether or not the licence demands it, so that a reader can tell what
rests on someone else's work and what is ours.

> Brady Stephenson. (2026). *BibleData: Structured Datasets from the Holy Bible* (Version 1.0)
> [Data set]. Zenodo. https://doi.org/10.5281/zenodo.19539956

Contributors named in the release's own README: Brady Stephenson for all but two files, Dan Raby
for the person labels, Fernando Falci for the person relationships.

## What is loaded and what is not

Sixteen files are read by the encyclopedia loader. Six are carried and never loaded: the
release's own `README.md` and `CITATION.cff`, and four datasets that are separate works —
the Alamo Polyglot, Strong's Hebrew concordance, Naves Topical Dictionary and Hitchcock's Bible
Names Dictionary. Loading any of those is a corpus decision and not a consequence of downloading
them.

None of the four is Stephenson's own composition, and their underlying works are out of copyright
rather than licensed by him: Hitchcock (1869), Naves (1897) and Strong (1890) are public domain,
and the Polyglot's ten component texts each carry their own terms — the World English Bible and
the King James are free, but Brenton, the Leningrad Codex and the JPS 1917 have to be read one by
one before any of them is served. What CC BY 4.0 covers is his transcription and structuring of
them, which is a real contribution and is what the credit above is for.

## Ussher's *Annals of the World*, which is loaded and is not Stephenson's work

`Ussher-AnnalsOfTheWorld.csv` is the sixteenth loaded file and the only one of the separate
works the corpus reads, so its terms are worth stating on their own rather than inside the
paragraph above.

**Two layers, and both are clear.** James Ussher's *Annales Veteris Testamenti* is 1650 and its
English translation by Edmund Pierce is 1658; both are out of copyright by age everywhere, and no
licence of Stephenson's could take that away or add to it. What he contributes is the
transcription into 7,000 numbered paragraphs with a year in four reckonings against each, and
that structuring is his — covered by the CC BY 4.0 above, with no ShareAlike obligation to carry.
He names himself for this file in the release's own contributor list.

Nothing in the release says anything narrower about it. The `LICENSE` file, the `README`
badge and licence section, the corrected `CITATION.cff` and the repository record are the four
statements read above; none of them carves any file out, and there is no per-file notice beside
this one. So the most restrictive statement actually attached to these bytes is Attribution 4.0,
over a public-domain work.

**Credited as Ussher's, not as ours.** Every row the corpus writes from it carries him as the
author, the paragraph number it came from, and Stephenson's transcription as the route — and
where an event's title had to be made rather than quoted, the row says which and by what. That
last part is not a licence obligation. It is the same rule the rest of the corpus keeps: a reader
has to be able to tell what a source said from what this project did with it.
