# Place coordinates from sources other than the gazetteer

Read on 26 October 2026 into `2026-10-26/`, with each file's URL, size and SHA-256 in
`2026-10-26/manifest.json`. The coordinates taken from them, and only those, are listed in
`Essenthos.Forge/Loading/Encyclopedia/LocationSupplements.json`; each is credited on the map and on
the place's page to the dataset named below, which the API declares in `Essenthos.Corpus/Corpus/Datasets.cs`.

## `pcgn-jordan-2025.pdf` — dataset `pcgn`

UK Permanent Committee on Geographical Names, *Jordan Toponymic Factfile* (2025),
<https://assets.publishing.service.gov.uk/media/694152af5431f4f94d7f0aca/Jordan_Toponymic_Factfile.pdf>.
Taken: the coordinates of Jabal Nabā (Mount Nebo) from the gazetteer table on page 8,
31°45′44″N 035°44′46″E, written as decimal degrees.

© Crown copyright 2025. The publication is released under the
[Open Government Licence v3.0](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/)
(`govuk-reuse.html`, <https://www.gov.uk/help/reuse-govuk-content>, kept beside it). The licence asks
for the attribution statement wherever the information is used, and a link to the licence where
possible:

> Contains public sector information licensed under the Open Government Licence v3.0.

No logo, crest or third-party map from the publication is used.

## `drews-han-2010.html`, `drews-han-2010.xml` — dataset `drews-han-2010`

Carl Drews and Weiqing Han, *Dynamics of Wind Setdown at Suez and the Eastern Nile Delta*,
PLOS ONE 5(8): e12481 (2010), <https://doi.org/10.1371/journal.pone.0012481>, the article page and its
manuscript XML. Taken: the coordinates of Tell Abu Sefeh, 30.8606 N 32.3543 E, from Table 1.

The manuscript's own licence statement, by © 2010 Drews, Han: "This is an open-access article
distributed under the terms of the Creative Commons Attribution License, which permits unrestricted
use, distribution, and reproduction in any medium, provided the original author and source are
credited." It names no version; every version asks only for the credit, which the place's page gives.
The coordinates are used as printed.

## Wikidata — dataset `wikidata`

Sarepta (Q616837, revision 2521611391) and the Judaean Desert (Q2046004, revision 2549290202) are read
from the held Wikidata snapshot, `Resources/Wikidata/items.jsonl`, under CC0 1.0 as
`Resources/Wikidata/LICENCE.md` records. Nothing for them is kept in this folder.
