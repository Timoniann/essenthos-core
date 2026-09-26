# OpenBible.info Cross References

**Cross References**, by Stephen Smith of OpenBible.info, <https://www.openbible.info/labs/cross-references/>.
344,799 pairs of verses, each with the votes the site's readers have given it.

Taken from `https://a.openbible.info/data/cross-references.zip` on 2026-09-27 by
`scripts/fetch-openbible-cross-references.ps1`: SHA-256
`83e9db0a08054ed99848531512729f0190dbbac85416f408f2362b5dc36d421d`, its header stamped
**2026-09-21**. The archive is rebuilt as votes arrive and carries no version, so the hash is the
version: a new one is taken deliberately, and recorded here with its date.

## What it is used under: CC BY

<https://creativecommons.org/licenses/by/4.0/>

## Every statement attached to these bytes

| Where | What it says |
|---|---|
| the first line of `cross_references.txt` | "From Verse	To Verse	Votes	#www.openbible.info CC-BY 2026-09-21" |
| the page the file is offered on (`labs-cross-references.html`, kept here) | "Unless otherwise indicated, all content is licensed under a Creative Commons Attribution License." |

Neither names a version, and neither contradicts the other: Attribution, with no ShareAlike and no
NonCommercial clause. Every version of CC BY asks the same thing of us — credit the author, link the
licence, say if we changed it — so the credit is given against the current version, 4.0, and the
link on the sources page points there.

The page also says where the pairs come from: "This data draws primarily from public-domain sources,
especially the Treasury of Scripture Knowledge, which provides most of the data. It also includes
data (to seed the initial votes) from my Topical Bible and Twitter Bible Search." The votes are the
site's readers'.

The page quotes Scripture from the ESV, which is copyright Crossway. Nothing of that is in the file:
it holds addresses and numbers only, and no verse text is taken from the page.

## What the corpus does with it

`cross_references.txt` is read as it is — `From Verse`, `To Verse` (a verse or a range, which may
cross into the next book), `Votes` — in OSIS book names and the English numbering, which is the
shared frame's. Every pair is loaded, including the 3,517 whose votes are below zero; the reader
ranks each verse's references by their votes and leaves those out.

## Attribution as shown to readers

> Cross references by Stephen Smith, OpenBible.info, licensed under CC BY. Ranked by the votes of the
> site's readers.
