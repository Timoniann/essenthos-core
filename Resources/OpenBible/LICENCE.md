# OpenBible.info Bible Geocoding

**Bible Geocoding Data**, by Stephen Smith of OpenBible.info,
<https://github.com/openbibleinfo/Bible-Geocoding-Data>, browsable at
<https://www.openbible.info/geo/>.

Taken from github.com/openbibleinfo/Bible-Geocoding-Data at
7eb18a5ee62f27b9b93bd6689ea272d76dd23b8f on 2026-09-03 by `scripts/fetch-openbible.ps1`.

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

The repository holds three things this fetch deliberately leaves behind, because they are under
other terms:

- **the geometry** — thousands of GeoJSON and KML files for rivers, regions and roads, partly
  derived from OpenStreetMap, which the readme states is **ODbL 1.0**, "similar to CC-BY-SA";
- **the images** — 512x512 thumbnails whose terms "vary depending on the image", from Wikimedia
  Commons contributors and from Sentinel-2;
- **`modern.jsonl`, `geometry.jsonl`, `image.jsonl` and `source.jsonl`** — the modern
  identifications, their coordinates and their citations, which is where the OpenStreetMap-derived
  values live.

Only `data/ancient.jsonl` is fetched and only it is loaded, so nothing under ODbL reaches the
corpus. Taking coordinates later is a separate decision with a separate licence to read, not a
consequence of already having the places.

## Attribution

Credited at `/v1/datasets` whether or not the licence demands it, so that a reader can tell what
rests on someone else's work and what is ours.

> Stephen Smith. *Bible Geocoding Data*. OpenBible.info.
> https://github.com/openbibleinfo/Bible-Geocoding-Data

The dataset itself cites over 400 works — commentaries, dictionaries, encyclopedias and atlases —
in `source.jsonl`, which is not fetched. The votes and confidence scores those sources produce are
not loaded either; what is loaded is which verses name which place.

## What is loaded and what is not

From `ancient.jsonl`, per place: its identifier, its name, the kind of thing it is, and the list of
verses that name it. The confidence scores, the modern identifications, the linked-data
cross-references and the per-source votes are read past.

`license.txt` and `readme.md` are carried beside the data and never loaded. They are the record.
