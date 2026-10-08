# The Bible's persons, places and things, from Wikidata

The Wikidata items a record can be matched to, so that a person, place or record page can link to
its Wikipedia article in the reader's language. The load step `wikipedia` reads this folder (and the
Wikidata item OpenBible gives each ancient place) and writes the table `entity_wikipedia`; see "How
the records are matched" below. **CC0** (see `LICENCE.md`).

Fetched **27 September 2026** by `scripts/fetch-wikidata-biblical.ps1` (avioniq action
`fetch-wikidata-biblical`), from `https://query.wikidata.org/sparql` and the Wikibase API
`wbgetentities`. The items and their labels are not committed; running the script puts them back.

## Which items

An item is taken if any of these holds. The first three are the `.rq` files here.

| Seed | Query | Items | What it asks |
|---|---|---|---|
| `present-in-work` | `present-in-work.rq` | 2,306 | *present in work* (P1441) is the Bible (Q1845), either Testament, the Tanakh (Q83367), a *book of the Bible*, a *chapter of the Bible*, a *Bible translation*, or anything *part of* or an *edition of* those — 5,980 works |
| `described-by-source` | `described-by-source.rq` | 2,236 | *described by source* (P1343) is one of those works, or Archimandrite Nicephorus's Bible Encyclopedia, Easton's, the ISBE or either Encyclopaedia Biblica |
| `class` | `class.rq` | 2,891 | an instance of *human biblical figure* (Q20643955), *biblical character* (Q12405827) or *biblical place* (Q12404340), or of any class below them (angels, biblical city, Levitical city…) |
| `openbible` | — | 1,626 | every QID in `Resources/OpenBible/ancient.jsonl` and `modern.jsonl` (1,638 QIDs named; 12 answered as an item another of them already names) |

The Jewish Encyclopedias (Q653922, Brockhaus–Efron's Q4173137) are deliberately not sources: they
describe Jewish persons of every century. Their identifiers are still kept on the items that have
them (P8590 on 666 items, P1438 on 412).

**5,622 items** in the union. 46 of the QIDs asked for have since been merged into another item and
answered under it; `redirectedFrom` names them, and all 46 come from OpenBible. None were deleted.

## What each item holds

`items.jsonl`, one item per line, sorted by QID:

- `lastrevid` and `modified` — the revision read. `Special:EntityData/<id>.json?revision=<lastrevid>`
  returns exactly that item, however Wikidata changes afterwards.
- `kind` — `person`, `place` or `other`, a convenience: *person* if the item has a sex, a father or a
  mother, or is a human or biblical figure; else *place* if it has coordinates, is a biblical place
  or came from OpenBible; else *other*. The item's own P31 is beside it.
- `seeds` — which of the four above found it.
- `labels`, `descriptions`, `aliases` in en, uk, de, es, he, el, ru and `mul` (Wikidata's
  language-neutral label, which many names now carry instead of an English one).
- `sitelinks` — article titles on enwiki, ukwiki, dewiki, eswiki, ruwiki, hewiki.
- `claims` — non-deprecated statements, with qualifiers, for the properties the script lists in
  `$KeptProperties`: family (P22 father, P25 mother, P3373 sibling, P26 spouse, P40 child, P1038
  relative), sex (P21), class (P31, P279), names (P1449, P735, P1559, P1705, P2561, P4970),
  P1441 and P1343 (whose qualifiers often give the chapter or the article's heading), place
  (P625 coordinates, P131, P276, P706, P17, P361), P460 *said to be the same as*, P1889 *different
  from*, P973 *described at URL*, dates and a few more.
- `identifiers` — every external identifier, as property → values. Wikidata has no property for
  Theographic, BibleData or OpenBible ids; the biblical ones it does have are ISBE (P9069, 199
  items), McClintock and Strong (P8636, 239), Jewish Encyclopedia (P8590, 666) and its Russian
  counterpart (P1438, 412), Catholic Encyclopedia (P3241, 359), Daat (P3710, 122) and Pleiades
  (P1584, 370 places).

`referenced.jsonl` holds labels and descriptions, in the same languages, of the 7,340 items those
statements point at and that are not items above — the classes, regions, encyclopedia volumes.
`summary.json` holds the counts below.

## Counts

| Kind | Items | enwiki | ukwiki | dewiki | eswiki | ruwiki | hewiki |
|---|---|---|---|---|---|---|---|
| person | 2,331 | 870 | 494 | 678 | 557 | 627 | 796 |
| place | 2,236 | 1,354 | 538 | 707 | 724 | 693 | 1,140 |
| other | 1,055 | 826 | 562 | 578 | 635 | 634 | 584 |

Of the persons, 1,982 are *human biblical figures* and 221 are plain *human*; 1,033 have a father
stated. Of the places, 1,480 have coordinates.

## What the matching has to know

- **Most persons have no article in any language.** Wikidata holds an item for nearly every named
  person — 27 persons labelled *Zechariah*, 26 *Shemaiah*, 17 *Azariah*, 283 labels shared by two
  or more — but only 870 of 2,331 have an English article and 494 a Ukrainian one. The minor
  namesakes are mostly the items without one, so a wrong match mostly costs a missing link, not a wrong one;
  the dangerous case is a minor namesake matched to the famous item that does have an article.
- **The descriptions carry verses.** Minor figures are typically described as *male human biblical
  figure in 2 Kings 18:2, father of Abijah*, and P1343/P1441 qualifiers name chapters. With the
  family statements, that is what separates namesakes.
- **P1889 *different from* is on 408 persons and P460 *said to be the same as* on 219.** The first
  is a direct statement that two namesakes are not one; the second marks identifications scholars
  argue about, which should go to review rather than be linked.
- **OpenBible's modern QIDs are not the ancient place.** 1,205 of the places came only from
  OpenBible, most of them the modern site a place is identified with (a tell, a village, a river).
  Linking an ancient place to them would send a reader to the wrong article.
- **Some items are groups**, not one person or place: 25 are a *group of humans* (tribes,
  peoples), and are `other`.
- **177 persons and places have no English or `mul` label.** 91 of them have a Hebrew one, the
  name to match on; the rest have only another language's.
- `described-by-source` brings 672 things that are neither person nor place (Nicephorus has
  articles on taxa, concepts, the Earth); they are `other` and harmless.

## How the records are matched

`forge wikipedia` (the load step of the same name, `forge load --from wikipedia`) ties a record to an
item only where nothing else could be the item, and leaves the rest for the owner:

- the candidates are the items of the right kind (a person's, a place's, a thing's) that go by one of the
  record's names in any language, aliases included; an item that is a Wikimedia page, a people when the
  record is a place, an angel when the line does not speak of one, or the other sex is not a candidate;
- a tie is made at the first of these that picks out exactly one candidate, and no other record has an
  equal or stronger claim on that item: the gazetteer's own Wikidata item for the ancient place
  (`identification`); a verse the item's description cites that names the record (`verse`); kin the item's
  family statements or description share with the record's line (`kin`); a chapter the item is present
  in (`chapter`);
- failing evidence, to the only candidate by every spelling of the name when no other record could be that
  item and Wikidata states no namesake it is to be told from (P1889 to a person or a place of the same
  name, any P460) — for a person, or a place that goes by the item's own label (`name`). The name alone
  never ties an object, a feast, a people or a title;
- everything else is ambiguous. Those with a candidate that has an article are listed in
  `Resources/Essenthos/review/wikipedia-matches.json` for the owner's console; his answer there (an item,
  or `none`) overrides the matching on every later load.

Items counted as candidates include those without any article, because the dangerous case is the minor
man of the famous man's name, and the minor namesakes are mostly items with none. A record is given a row
for each of English, Ukrainian, German and Spanish in which the item has an article, and none for a
language it has not.

## Re-running

The Query Service answers in seconds. The API is read fifty items a request and ten requests a
minute, the most Wikimedia allows a client whose User-Agent gives no contact address, so a full run
takes about thirty minutes. A run stopped part-way keeps the batches it read (for a day, in the
system temporary folder) and resumes after them.
