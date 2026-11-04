# ESSX: the product file format

ESSX is the binary format of the corpus build's machine products (NOT-0216). It holds one or
more tables of typed columns, written in row groups so that any group is read without the rest,
with every string column through a dictionary so that a word a text prints sixty thousand times
is stored once, and with a SHA-256 of the whole file at its end. It is designed to be read in a
browser: little-endian integers, UTF-8, a JSON footer, and raw deflate (`deflate-raw` in
`DecompressionStream`) as the only codec. The reference implementation is
`Essenthos.Products/Essx/` (writer, reader) and the tests in `Essenthos.Core.Tests/Products/Essx*Tests.cs`.
Every reader is held to `docs/essx-sample/` (§8). Version 2, 2026-11-04 (version 1, 2026-11-03, differs only
in having no per-chunk `encoding`; readers read both).

## 1. File layout

```
offset 0      "ESSX"              4 bytes   magic
       4      version             u16       2 (1 is read too)
       6      flags               u16       0
       8      sections …                    chunks and dictionaries, in the order the writer produced them
       F      footer              JSON, UTF-8, no BOM
       F+L    footerOffset        u64       F
       F+L+8  footerLength        u64       L
       F+L+16 "ESSX"              4 bytes   magic again, so a tail read confirms the file
       F+L+20 sha256              32 bytes  over bytes [0, F+L+20)
```

A reader reads the last 52 bytes, checks both magics, reads the footer at `footerOffset`, and
then seeks to whatever it needs. Nothing between the preamble and the footer is parsed except
through the footer's offsets. To verify, hash everything before the last 32 bytes and compare.

## 2. The footer

JSON with camelCase names; absent means null.

```json
{
  "format": "essx",
  "version": 1,
  "product": {
    "product": "Essenthos/texts/KJV/words.essx",
    "inputs": [{"path": "bible4u/kjv.zip", "sha256": "…"}],
    "producer": "forge convert KJV   (Essenthos.Forge 1.0+2b87c172)",
    "rows": 932131,
    "notes": [["tradition", "protestant"]],
    "comments": ["free text"]
  },
  "tables": [
    {
      "name": "rows",
      "schemaVersion": 1,
      "rows": 932131,
      "rowGroupRows": 65536,
      "columns":  [ {"name": "own", "type": "wordkey", "nullable": false,
                     "physical": ["own.book", "own.chapter", "own.chapter_label", "own.verse", "own.verse_label", "own.position"]},
                    {"name": "surface", "type": "text", "physical": ["surface"]}, … ],
      "physical": [ {"name": "own.book", "encoding": "int"},
                    {"name": "own.chapter_label", "encoding": "dict",
                     "dictionary": {"offset": 1234, "length": 20, "rawLength": 20, "codec": "none", "count": 1}}, … ],
      "rowGroups": [ {"rows": 65536, "chunks": [ {"offset": 8, "length": 0, "rawLength": 0, "codec": "none",
                                                   "nulls": 0, "width": 0, "base": 1, "min": 1, "max": 1}, … ]}, … ]
      // a chunk of a dict column past its dictionary's cap carries "encoding": "text"
    }
  ]
}
```

- `product` is the product header (§ Products guide): the same four fields every TSV product
  carries, plus the producer's notes and free comments.
- A **table** has logical `columns` (what a schema declares) and `physical` stored columns. A
  plain column is one physical column of the same name; a key column is several (§4).
- `rowGroups[g].chunks[c]` is the chunk of physical column `c` in row group `g`, in the order of
  `physical`. Every chunk of a group holds `rows` values.
- `schemaVersion` is the producer's own version of the table's shape (`ProductSchema.Versioned`).
- A chunk may carry `encoding` (version 2): absent means its physical column's encoding; `text`
  marks a chunk of a `dict` column written as text after the column's dictionary reached its cap
  (§4). A reader decodes each chunk by its own encoding.

## 3. Chunks

A chunk is `length` bytes at `offset`. If `codec` is `deflate`, those bytes are raw deflate and
inflate to `rawLength` bytes; if `none`, `length == rawLength`. The raw bytes are, by `encoding`:

| encoding | raw bytes | notes |
|---|---|---|
| `int` | `[validity] values[rows × width]` | value = `base` + unsigned little-endian of `width` bytes (0, 1, 2, 3, 4 or 8). `width` 0 means every value is `base` and the chunk has no value bytes. `min`/`max` are over the present values and are absent when all are null. |
| `bool` | `[validity] bits[⌈rows/8⌉]` | bit `i` of the bitmap is row `i` (`byte i>>3`, bit `i&7`). `min` carries the count of true values. |
| `dict` | `ids[rows × width]` | unsigned little-endian ids into the column's dictionary; 0 is null; `max` is the largest id in the chunk. |
| `text` | `[validity] offsets[(rows+1) × u32] bytes` | string `i` is `bytes[offsets[i] .. offsets[i+1])` where offsets count from the start of the raw chunk; a null row has an empty range and a cleared validity bit. |

`validity` is present only when `nulls > 0`: `⌈rows/8⌉` bytes, bit set = value present.

## 4. Dictionaries

A `dict` physical column has one dictionary for the whole table, written after the last row
group: `u32 count, u32 offsets[count+1], bytes`, offsets from the start of the raw section; id
`k` (1-based) is `bytes[offsets[k-1] .. offsets[k])`. It may be deflated like a chunk
(`codec`, `rawLength`). A reader decodes it once and hands every row the same string instance.

The writer chooses `dict` or `text` for a string column on its first row group: `dict` when at
most half the values are distinct or the average length is at most 16 characters; `text` otherwise.
A dictionary is capped (by default 1,048,576 strings or 32 Mi characters, `EssxOptions.DictionaryLimit`
and `DictionaryCharacterLimit`): the group that would pass the cap and every group after it are
written as `text` chunks marked `"encoding": "text"`, and the dictionary holds only the strings the
earlier chunks use. A column over the cap in its first group is a plain `text` column. So the writer's
memory stays bounded whatever a column holds. A reader does not care which.

## 5. Column types and keys

Logical types: `text`, `json` (text the writer passes through; a loader may type it `jsonb`),
`integer` (i64), `boolean`, `versekey`, `wordkey`.

A **verse key** `book.chapter[label]:verse[label]` is stored as five physical columns
`<name>.book` (int), `.chapter` (int), `.chapter_label` (dict), `.verse` (int), `.verse_label`
(dict); a **word key** adds `.position` (int). A null key is a null `.book`; the other parts hold
0 or the empty label. The text form is rebuilt as `{book}.{chapter}{chapter_label}:{verse}{verse_label}`
and, for a word key, `.{position}`. The text is the file's own and is never stored.

Because a text's words come in print order, the parts are constant or rise by one within a group,
so the key costs a few bytes per row uncompressed and almost nothing deflated; and the `min`/`max`
of `.book` and `.chapter` per chunk let a reader find a verse's group without reading the others.

## 6. Reading a part of a file

A reader decodes only what it is asked for: `EssxTable.ReadGroup(g, columns)` and `Rows(columns)` read
and decode the named logical columns and leave the others unread, and a verse is found by the key's
chunk statistics and then by comparing its decoded integer parts, never by formatting keys as text.

## 7. Reading in a browser

1. `fetch` with `Range: bytes=-52` → trailer; check magics; `Range` the footer; `JSON.parse`.
2. For table `t`, column `c` and the verse wanted: pick the groups whose `.book`/`.chapter`
   chunks have `min ≤ wanted ≤ max`.
3. `Range` each needed chunk (and each needed dictionary once); if `codec == "deflate"`, pipe
   through `new DecompressionStream("deflate-raw")`; decode as §3 with a `DataView`
   (little-endian) and `TextDecoder("utf-8")`.
4. Verify when the whole file is at hand: `crypto.subtle.digest("SHA-256", bytes[0..-32])`.

## 8. The sample file

`docs/essx-sample/sample.essx` is the file every reader of the format is tested against, and
`sample.json` beside it is what it holds: the footer's product, and each table's columns and values
(keys as their text form, integers as JSON numbers, null as `null`). It has two tables, row groups of
four, nulls in every type, an integer chunk of width 8 and one of width 0, a `text` column, deflated
and stored chunks, and a dictionary that reaches its cap. `EssxSampleTests` holds the C# reader and
writer to it; the console's browser reader is tested against a copy. The file is `EssxSampleTests.Write`'s
output; regenerate both files from it if the format changes.

## 9. Conventions

- A product file holds one table named `rows` unless a producer writes several with `EssxWriter`.
- The extension is `.essx` and the magic `ESSX` (the owner's choice, 2026-11-04; `.esw`, the first name, is taken by other programs). They are `EssxLayout.Extension` and `EssxLayout.Magic`. Hand-edited files stay `.tsv` and never become ESSX.
- Integers are never floats; a product that needs a decimal writes an integer in a stated unit or text.
- A reader refuses a `version` newer than it knows; within a version, unknown footer fields are ignored.
