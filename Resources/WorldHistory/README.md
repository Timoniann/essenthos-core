# World history, from Wikidata

What was happening elsewhere while the text was being written. Three queries against the Wikidata
Query Service, saved exactly as they came back, so the rows can be checked against the queries that
produced them and re-run when they go stale.

**Licence: CC0.** Wikidata releases all its data into the public domain, which is why it is here
rather than a general encyclopedia — the ones with better prose are CC BY-SA or BY-NC-SA, and a
share-alike condition on the world layer would reach the corpus it sits beside. Nothing here
carries a condition at all. Attribution is still recorded on every row, because a date a reader
cannot check is not worth drawing.

Fetched **2 September 2026** from `https://query.wikidata.org/sparql`.

| File | Query | Rows | What it is |
|---|---|---|---|
| `wikidata-events.csv` | `wd_events.rq` | 1,497 | Things with a *point in time* — mostly battles, sieges, treaties, eruptions |
| `wikidata-inception.csv` | `wd_inception.rq` | 1,293 | Things with an *inception* — cities founded, dynasties begun, works written |
| `wikidata-spans.csv` | `wd_spans.rq` | 349 | Things with a *start* and an *end* — wars, empires, dynasties, archaeological ages |

Rows outnumber items: an item with three `instance of` values and two countries comes back six
times. The loader keeps the first of each and counts the rest as nothing.

## What the queries do, and why

**Precision ≥ 9.** Wikidata records how precisely a date is known, and a great deal of ancient
history is known only to the century. A century-precision date arrives looking like a year —
`-0500-01-01` for *the 5th century BC* — and drawing that as the year 500 BCE would be inventing
four significant figures. Only year-or-better dates are asked for.

**A sitelink floor.** Wikidata holds every identified potsherd. The number of Wikipedias that
wrote an article about something is a crude measure of whether it belongs on a timeline of world
history, and it is the only one available without judging the content. Fourteen for events,
eighteen for spans, twenty-five for inceptions — the last is higher because inception is a property
of every building and every village.

**No year items.** Wikidata has an item for *500 BC*, and it has a point in time. Two thirds of the
first query's rows are these; the loader drops anything whose type is `year` or `year BC`.

## The year convention

**Astronomical, with a year zero**, because the RDF these queries return is XSD `dateTime` and XSD
has one. Marathon comes back as `-0489` and is 490 BCE; the Great Pyramid as `-2559` for 2560 BCE.

This is not Wikidata's internal convention, which has no year zero and writes Marathon `-0490`. The
difference is one year, it is invisible on a six-thousand-year axis, and it would be wrong in every
citation — so it was checked against Marathon, Thermopylae, Gaugamela and Actium rather than read
off the documentation.
