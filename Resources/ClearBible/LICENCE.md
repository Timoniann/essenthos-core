# Clear Bible Alignments — complete `data-latest` snapshot

Downloaded from [Clear-Bible/Alignments](https://github.com/Clear-Bible/Alignments), release
[`data-latest`](https://github.com/Clear-Bible/Alignments/releases/tag/data-latest), on
2026-09-14. The eleven archives held here are:

`arb`, `asm`, `ben`, `eng`, `fra`, `hau`, `hin`, `legacy`, `por`, `rus`, `spa`.

The repository's current README says the alignment data is CC BY 4.0. That is helpful context but
is not used as a blanket licence: the TOML file adjacent to every alignment is the controlling
record for the individual bytes and is retained with the archive's extracted data.

## Inventory read from the TOML records

| language / target | source text | records | alignment terms | process | acquisition status |
|---|---|---:|---|---|---|
| Arabic AVD | SBLGNT, WLC | 2 | CC BY 4.0 | manual | candidate |
| Arabic ONAV | SBLGNT | 1 | **CC BY-SA 4.0** | manual | retained, excluded from loading |
| Assamese IRVAsm | SBLGNT | 1 | CC BY 4.0 | manual | candidate |
| Bengali IRVBen | SBLGNT | 1 | CC BY 4.0 | manual | candidate |
| English BSB | BGNT, SBLGNT, WLCM | 3 | CC BY 4.0 | manual | current calibration source |
| English YLT | SBLGNT, WLC | 2 | CC BY 4.0 | manual | candidate |
| French LSG | SBLGNT, WLCM | 2 | CC BY 4.0 | one `manual`, one process unstated | candidate; inspect before load |
| Hausa OHCB | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| Hindi IRVHin | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| Portuguese JFA11 | SBLGNT | 1 | CC BY 4.0 | **transfer from Spanish RVR09** | retained; never call it manual |
| Russian RUSSYN | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | blocked by token mismatch; see below |
| Spanish RV09 | SBLGNT, WLCM | 2 | CC BY 4.0 | manual | candidate |
| `legacy` | sample config only | 1 | CC BY 4.0 | manual | format reference, not an alignment set |

`ONAV` is retained because the owner asked for the available sources to be acquired, but its
alignment and target declare CC BY-SA 4.0; RUL-0183 excludes it from corpus use. Source and target
licences are recorded separately in each TOML and may impose attribution or text-use conditions
even where the alignment itself is CC BY.

## Russian warning

Do not load `RUSSYN` from this release yet. Measured against its own target token file on
2026-09-03: 12,550 of 89,248 records name punctuation as the Russian word. The English BSB control
had zero such records in 171,172. The archive is evidence for an upstream repair, not mapping input
until its target tokenization matches the release's alignment records.

## What these data mean

An alignment is a publisher's claim relating word **occurrences** in one named translation to a
particular original-language source text. It may be imported only after its target text is matched
to a loaded corpus text at token level. It is not a general bilingual dictionary, and none of these
records authorizes an inferred link into a differently tokenized translation.

## Attribution

Retain the individual TOML copyright holder and attribution. The repository says all alignment
data is CC BY 4.0; each link imported from a set must identify the specific set and its stated
copyright holder rather than crediting “Clear Bible” generically.
