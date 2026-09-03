# xml/, metadata.txt — the GLAUx treebank, Septuagint books only

**GLAUx — the Greek Language Automated corpus**, by **Alek Keersmaekers**.
<https://github.com/perseids-publications/glaux-trees>.

**Licence: CC BY-SA 3.0.** Share-alike, and the only share-alike annotation in the
corpus.

## Where that reading comes from

`metadata.txt` carries a `SOURCE_LICENSE` column, one row per text. Every one of the 57
Septuagint books this folder holds states the same thing — counted on 2026-09-03, all 57
rows:

> `720  0527-001  Septuaginta  Genesis  https://el.wikisource.org  CC-BY-SA 3.0`

GLAUx makes three statements about itself and they are not the same; CC BY-SA 3.0 is the
most restrictive, and it is the one attached to these particular bytes rather than to the
project as a whole. That is the reading RUL-0105 asks for. DOC-0161 has it in full.

Fetched by `scripts/fetch-glaux.ps1`, 111 MB, not committed.

## What share-alike means here, and why it was accepted

The owner accepted share-alike for this source on **2026-09-03**. The line RUL-0183 draws
is share-alike on the *annotation*, and this is exactly that case, taken deliberately:

**GLAUx's own Greek text is never loaded.** The corpus serves Brenton's Septuagint, which
is public domain, and uses GLAUx purely as a form-to-lemma dictionary against it — 99.4%
of Brenton's tokens are spelled the same way somewhere in GLAUx. So what enters the
corpus from here is lemmas, and `/v1/datasets` attributes them to GLAUx separately from
everything around them, because a page that printed one licence over the whole dataset
would be asserting what none of the sources says.

The upstream source for these books is Greek Wikisource, which is where the CC BY-SA
comes from.
