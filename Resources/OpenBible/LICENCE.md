# OpenBible.info Bible Geocoding

**Bible Geocoding Data**, by Stephen Smith of OpenBible.info,
<https://github.com/openbibleinfo/Bible-Geocoding-Data>, browsable at
<https://www.openbible.info/geo/>.

Taken from github.com/openbibleinfo/Bible-Geocoding-Data at
7eb18a5ee62f27b9b93bd6689ea272d76dd23b8f on 2026-09-03 by `scripts/fetch-openbible.ps1`;
`modern.jsonl` from the same commit on 2026-09-18, by the same script.

## What it is used under: CC BY 4.0

<https://creativecommons.org/licenses/by/4.0/>

## Every statement attached to these bytes

Four statements, and the three that name a version agree.

| Where | What it says |
|---|---|
| `license.txt` in the release | the full text of Creative Commons **Attribution 4.0 International** |
| `readme.md`, *License* | "This data is licensed under a [Creative Commons Attribution 4.0] license." |
| the GitHub repository record | `CC-BY-4.0` |
| openbible.info's own pages | "Creative Commons Attribution license" — no version named |

The site's unversioned wording is the loosest of the four and contradicts none of them, so the
release's own `license.txt` governs: **Attribution 4.0, with no ShareAlike and no NonCommercial
clause.** That is the clause that decided this dataset over the alternative — the other candidate
for the place layer, Theographic, states 7,310 references under CC BY-SA 4.0, and share-alike at
that scale reaches everything the corpus builds on top of it.

## What is not covered by that, and is therefore not here

The readme names one exception to the licence: "OpenStreetMap data is licensed under ODbL 1.0,
which is similar to CC-BY-SA." ODbL is share-alike, so nothing this corpus holds may be
OpenStreetMap's. What the repository holds that is left behind:

- **the geometry** — thousands of GeoJSON and KML files for rivers, regions and roads, partly
  derived from OpenStreetMap. Not fetched. Only points are ever taken, never a line or a shape;
- **`geometry.jsonl` and `source.jsonl`** — the geometry index and the citations. Not fetched.

The images are fetched, since 2026-09-23, under their own terms rather than the dataset's; see the
next section.

## Coordinates: one point per place, and never an OpenStreetMap one

Decided 2026-09-18, when a map of the places was asked for.

`modern.jsonl` is fetched, because it is the only file that says **whose** each point is. Every
modern location carries a `coordinates_source` whose `type` names where its coordinates came from
(`wikidata` 938 locations, `daahl` 198, `megajordan` 113, `geonames` 84, `iaa` 63, `osm` 51,
`palopenmaps` 43, and some twenty more), and a `geometry_credit` of `osm` on the coordinate source
and on each drawn role (`point`, `representative_point`, `precise`, `local`) wherever that part is
OpenStreetMap's. The readme's field notes say the same in words: `precise` and `local` geometry is
"from OpenStreetMap", and a source of type `osm` "links to a way or relation at OpenStreetMap.org".
So the OpenStreetMap values are marked one by one, and can be left out one by one.

The point for a place is the one `ancient.jsonl` already gives the resolution its
`modern_associations` scores highest (`lonlat`, `lonlat_type`, `modern_basis_id`); it equals the
`lonlat` of that modern location in all 1,335 cases. It is **not taken** when:

- the modern location's `coordinates_source` is of type `osm`, or credits `osm`, or the drawn role
  the point is (`point`, or `representative_point` for a representative point) credits `osm` — 20
  places;
- its coordinates were read off Google Maps (`google_maps`), whose terms are Google's; for readings
  off a commercial source the gazetteer supplies its own independent `custom_lonlat` "nearby", and
  for these four it supplies none — 4 places;
- the gazetteer does not identify the place with any modern location — 7 places.

Where a `custom_lonlat` is given (a point read off a commercial source, Amud Anan in these cases),
that independently created point is the one taken instead — 5 places. A place whose best point is
left out gets **no** point: falling back to the second-best identification would put it where its
own source thinks it probably is not.

That leaves 1,311 of the 1,342 places with a point, each held with the modern location it was read
from and the source the gazetteer credits for it, so any row can be traced back to the credit that
let it in. OpenStreetMap geometry that merely sits beside a point credited to someone else — a
river's course beside a Wikidata point for the river — is not taken, and does not make the point
OpenStreetMap's.

What is relied on for the rest is the statement attached to these bytes: the repository licenses
its data as CC BY 4.0 and names OpenStreetMap as the only exception. The points it credits to
Wikidata (CC0), GeoNames (CC BY 4.0), Pleiades (CC BY) and to surveys, atlases and gazetteers
without a stated licence of their own are therefore used as the repository states them, and
attributed to it.

## The place pictures: every one under its own licence, and always credited

Decided 2026-09-23, when the owner allowed the thumbnail archive to be downloaded for the place pages.

`scripts/fetch-images.ps1` takes `image.jsonl` from the same commit as `ancient.jsonl` and the
512x512 archive `https://a.openbible.info/geo/thumbnails.zip` (1,650 files, 184 MB), kept here as
`thumbnails.zip` and unpacked into `Resources/Images/openbible/`. The dataset's CC BY 4.0 does not
reach the pictures: the readme says their licences "vary depending on the image", and `image.jsonl`
states one per image — about three in five are photographs from Wikimedia Commons contributors under
CC BY, CC BY-SA, GFDL, CC0 or the public domain, and the rest are satellite views credited
"Contains modified Copernicus Sentinel data" under the Sentinel data terms. The archive's files are
OpenBible.info's own crops and colour corrections of those originals, so each is credited to its
photographer, linked to its Commons page, and shown as taken from OpenBible.info.

One picture per place, the one the readme recommends: the place's own `media.thumbnail`, else that
of the identification its highest-scored association resolves through (the one its point comes
from), else that identification's resolution's. A picture is not taken when `image.jsonl` gives it
no licence this corpus reads — `copyright` above all — or nobody to credit. At this commit that
leaves none out: 1,335 of the 1,343 places carry a picture.

Showing a picture unchanged beside an entry is not an adaptation of it, so a ShareAlike licence on a
photograph asks for the credit and the licence named with it, which every picture carries, and
reaches nothing else the corpus holds.

## Attribution

Credited at `/v1/datasets` whether or not the licence demands it, so that a reader can tell what
rests on someone else's work and what is ours. The map endpoint names the dataset its points came
from, so a map can carry the credit beside it.

> Stephen Smith. *Bible Geocoding Data*. OpenBible.info.
> https://github.com/openbibleinfo/Bible-Geocoding-Data

The dataset itself cites over 400 works — commentaries, dictionaries, encyclopedias and atlases —
in `source.jsonl`, which is not fetched. The per-source votes are not loaded; the one score taken is
the gazetteer's own summary of them for the identification a place's point comes from.

## What is loaded and what is not

From `ancient.jsonl`, per place: its identifier, its name, the kind of thing it is, the list of
verses that name it, and — through the rule above — one point, what kind of point it is
(`point`, `representative point`, `center` or `settlement`) and the score of the identification
it belongs to. From `modern.jsonl`: only each location's credit, and an independent point where
one is given. The other identifications, the linked-data cross-references and the per-source votes
are read past.

`license.txt` and `readme.md` are carried beside the data and never loaded. They are the record.