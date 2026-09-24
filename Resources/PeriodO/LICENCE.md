# d.json — PeriodO, a gazetteer of period definitions

**PeriodO**, <https://perio.do>, led by Adam Rabinowitz (University of Texas at Austin) and Ryan
Shaw (University of North Carolina at Chapel Hill), with Patrick Golden as lead developer, and its
contributors. Each period in it is defined by a published work — a handbook, an
excavation report, a museum's thesaurus — and PeriodO records that work as the period's
*authority*. The authority is who dated the period; PeriodO is who gathered it.

Taken from <https://data.perio.do/d.json> on 2026-09-24: 7,806,282 bytes, dataset version 301
(`ETag: W/"periodo-dataset-version-301"`, last modified 2026-09-24 15:08:53 GMT), 501 authorities
and 9,446 period definitions. `scripts/fetch-periodo.ps1` fetches it again and checks both
statements below before it replaces anything.

## The terms, read at the source

From PeriodO's licence page, <https://perio.do/license/>, verbatim, where *public domain* links to
Creative Commons CC0 1.0 (<http://creativecommons.org/publicdomain/zero/1.0/>):

> To the extent allowed by law, the contributors have dedicated the PeriodO dataset to the public
> domain by waiving all of their rights to the work worldwide under copyright law, including all
> related and neighboring rights. You can copy, modify, and distribute this dataset, even for
> commercial purposes, all without asking permission. However, if you desire our respect and
> cooperation, then you will clearly attribute your use of our work and share your own work in
> kind (even though you are not required to in any way).

The `periodo-data` repository, <https://github.com/periodo/periodo-data>, carries the Unlicense,
another public-domain dedication. Its README now says the repository only tracks data-quality
issues, so the licence page is the statement that governs the dataset; the two agree. A copy of
each is kept beside the data (`licence-at-source.txt`, `periodo-data-LICENSE`), fetched with it.

**One authority states terms of its own.** THANADOS's *Stylistic Classification* carries "Licensed
under a Creative Commons Attribution 4.0 International License" in its citation. A contributor
cannot dedicate what it did not hold, so the loader leaves out any authority whose source names a
licence of its own. THANADOS's periods are Central European and fall outside the selection anyway.

## What is taken, and what is ours

The loader (`Essenthos.Forge/Loading/Encyclopedia/PeriodOLoader.cs`) takes the periods that touch
4000 BCE to AD 150 and whose places are, at least half of them, in the lands the timeline draws:
the Levant, Egypt, Mesopotamia, Anatolia, Persia, the Aegean and Rome. Measured on this version:
1,420 periods under 71 authorities.

Each keeps its PeriodO identifier (resolvable under `http://n2t.net/ark:/99152/`), its authority
and that authority's citation, its places as Wikidata items, its own words for where, its labels
in every language PeriodO has them in, and its start and stop exactly as given — astronomical
years with a year zero (`-1549` is 1550 BCE), and a range where the source gives one. Nothing is
averaged: the same period under five authorities is five rows, and the timeline shows all five.

Ours is only the choice of what to take and the region each period is drawn under: the land the
source's own words for where name, where they name one, and otherwise the land most of its places
belong to.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader has to be able to tell whose dates
a band shows, and a band with no name on it has quietly been claimed as ours. Every band the
timeline draws names its authority. RUL-0181.
