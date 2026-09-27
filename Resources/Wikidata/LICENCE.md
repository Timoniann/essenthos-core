# Wikidata: the Bible's persons, places and things

Source: the Wikidata Query Service <https://query.wikidata.org/sparql> and the Wikibase API <https://www.wikidata.org/w/api.php>,
fetched on 2026-09-27 by `scripts/fetch-wikidata-biblical.ps1`. The three queries that chose the
items, besides the QIDs OpenBible names, are the `.rq` files beside this one; each item in `items.jsonl` records the `lastrevid` it was read at, so
<https://www.wikidata.org/wiki/Special:EntityData/Q1.json?revision=N> returns exactly what was read.

## Licence: CC0 1.0

Wikidata's licensing policy (read 2026-09-27) places all structured data in the main, Property and
Lexeme namespaces under **Creative Commons CC0 1.0 Universal**: no rights reserved, no attribution required. The
item data here (labels, aliases, descriptions, statements, sitelink titles) is that structured data.
It is attributed anyway, because a reader should be able to see where a link came from.

- <https://www.wikidata.org/wiki/Wikidata:Licensing>
- <https://creativecommons.org/publicdomain/zero/1.0/>

Text in Wikidata's other namespaces, and the Wikipedia articles the sitelinks name, are CC BY-SA
4.0 and are **not** covered. None of it is in this folder: an article is only ever linked to.
