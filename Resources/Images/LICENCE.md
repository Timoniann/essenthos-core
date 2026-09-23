# Pictures of people and places

The pictures the encyclopedia shows on a person's or a place's page. The files are not in git; a row
in `entity_image` names each by its path under this folder, and every row carries its own author,
source page and licence, because no two pictures here need share them.

| Folder | What | Terms | Put here by |
|---|---|---|---|
| `openbible/` | OpenBible.info's 512x512 place thumbnails | per picture, from its `image.jsonl`; see `../OpenBible/LICENCE.md` | `scripts/fetch-images.ps1` |
| `commons/` | public works chosen for people and things, listed in `Essenthos.Forge/Loading/Encyclopedia/PublicImages.json` | per entry, read on each file's Wikimedia Commons page | `scripts/fetch-images.ps1` |
| `generated/` | our own identity portraits, one per person, with a `manifest.json` | ours; the manifest states the credit and licence shown | by hand |

## The rules every picture is loaded under

- **Never without a credit.** A picture with nobody to credit or no licence stated is not loaded.
  A picture with nothing under it reads as ours, and most of these are somebody else's work.
- **Ours is shown bare.** A generated portrait goes out with no caption, credit or marker: it is
  plainly a picture, not a photograph. What its manifest entry states — caption, credit, licence, and
  the tool, model, date and prompt it was made with — is our own record, stored and never served.
- **God is never given a face or a figure.** The one picture a record of God may have is ours, of the
  glory as light with nothing in it to see (Exodus 24:10; Ezekiel 1:27–28), listed in
  `generated/manifest.json` with `"glory": true`. Any other picture of YHVH, and any picture of one
  of the words the text uses of God, is refused by the loader whoever listed it.
- **Places are real, present-day photographs** or satellite views, never paintings of how a place
  might have looked.

## The curated public works

Chosen one by one, and only where the title names exactly one person the encyclopedia holds: at
present fifteen of James Tissot's Old Testament watercolours and gouaches (about 1896–1902, most
of them at the Jewish Museum, New York), public domain since the painter died in 1902. Each entry
records its Commons page, which is where its credit and status were read on 2026-09-23.

One is of a thing, and is not a picture of it: Zorka Sojka's photograph of the Durupınar formation
near Doğubayazıt, Turkey (2009, CC BY-SA 4.0, shown as published), on Noah's ark's record. Some hold
the formation to be the ark's remains; its origin is disputed and geologists have described it as
natural, and its caption says so in each of the reader's languages (`captions` in the entry) — the
text itself says only *the mountains of Ararat* (Genesis 8:4).
The file is Commons' own 1920-pixel rendering of the 3872-pixel original, which is what `download`
names.

## A generated portrait

Drop the file into `generated/` and list it in `generated/manifest.json`:

```json
{
  "source": "Essenthos",
  "credit": "Essenthos",
  "licence": "…",
  "images": [
    { "entity": "david", "file": "generated/david.webp", "caption": "David in his prime", "focus": [0.5, 0.25] }
  ]
}
```

`entity` is the person's slug and `file` the path under this folder; `focus` is where the face is,
from 0 to 1 across and down, so a small crop keeps it. `glory` marks the picture of God's glory described
above, and is read on no other entry. Then
`dotnet run --project Essenthos.Forge -c Release -- images`.
