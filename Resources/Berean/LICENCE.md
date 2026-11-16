# bsb_tables.tsv — the Berean Standard Bible translation tables

**Berean Standard Bible**, published by Bible Hub / Berean Bible, <https://berean.bible>.

From the project's own licensing page, <https://berean.bible/licensing.htm>, verbatim:

> Licensing: The Berean Bible and Majority Bible texts are officially placed into the
> public domain as of April 30, 2023. See terms and conditions. **Licensing is not
> required for any use.**

The site's page footer still reads `Copyright © 2021 Berean Standard Bible. All rights
reserved.` That is template chrome on every page of the site; the licensing page is the
specific and deliberate statement, and it is the more permissive of the two. Both are
recorded here because RUL-0105 says to read all of them and RUL-0181 says to attribute
whatever the answer is.

Taken from <https://bereanbible.com/bsb_tables.tsv> on 2026-09-03, 85,525,373 bytes,
754,648 rows.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader of this corpus has to be
able to tell what rests on somebody's testimony and what rests on our inference, and a
fact printed without a name has quietly been claimed as ours. RUL-0181.

## What it contains

One row per original-language word — 529,439 Hebrew, 7,506 Aramaic, 217,701 Greek — each
carrying its Strong number, its parsing, and the English phrase of the Berean Standard
Bible that renders it. `BSB Sort` gives the English word order independently of the
original's, so it is a two-directional word mapping and not merely an interlinear gloss.

# bhsa-words.tsv — not the Berean Bible's

This file is **not from the Berean Bible** and is not covered by the statement above. It holds one
line per Old Testament verse (23,213 lines, `book:chapter:verse`, the words joined by `|`), and its
words and pointing are those of ETCBC's BHSA, split at BHSA's word boundaries (`וַ|יָּ֕מָת`). Where it
was made and by whom is not recorded: it was in the folder when the corpus moved to this repository on
2026-09-03, it matches no file of bereanbible.com, and no script in either repository writes it.

Being BHSA's text, it stands under BHSA's terms, CC BY-NC 4.0 (`etcbc/LICENCE.md`). **It is read by no
code and nothing of it is served**; `build/licences.tsv` declares it `never`, so no product may read it.
