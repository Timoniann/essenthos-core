# KJV.xml, RUSV.xml, UKR.xml — three texts from bible4u.net

**bible4u.net**, <https://bible4u.net>. Fetched as
`https://bible4u.net/static/bible_files/xml/{KJV,RUSV,UKR}_xml.tar.gz`; each file's
`<date>` is `2022-11-21`.

All three carry the same two-line claim in their own `<INFORMATION>` block, which is the
statement closest to these bytes. Verbatim:

> `<publisher>Public Domain</publisher>`
>
> `<rights>Everyone is permitted to copy, modify and distribute copies of this document
> for free as long as it's Biblical content remains unchanged.</rights>`

Read in the files themselves on 2026-09-03.

The corpus records that `<rights>` line as each text's licence rather than the word
"Public Domain", because the two do not say the same thing: an unmodified-content
condition is a term, and public domain has none.

## What contradicts it, and where

**UKR.xml is the Ohienko translation of 1962, and another copy of that translation in
this same Resources tree carries a copyright notice.** Every USFM file under
`Door43/uk_ubio/` opens with:

> `\id LUK EN_UBIO uk_українська⋅мова_ltr Біблія в пер. Івана Огієнка, 1962`
>
> `\rem Copyright British and Foreign Bible Society`

— while its `LICENSE.md` states CC BY-SA 4.0 for the file as distributed. So for the
Ukrainian text there are three statements in this tree: public-domain-with-a-condition
from bible4u, a British and Foreign Bible Society copyright from the Door43 header, and
CC BY-SA 4.0 from the Door43 licence file. They are not reconcilable from here, and
bible4u's is the least supported of the three: it names no translator, no date and no
rights holder for a translation that has all three.

RUL-0105 says to believe the statement closest to the bytes and to take the most
restrictive one actually attached to them. For **UKR** that reading is unresolved and is
the open question on it; it is recorded here so that a reader of `UKR.xml` finds the
contradiction beside the file rather than in a folder three directories away.

**KJV** is out of copyright in its text; the typesetting of this particular XML is what
bible4u's line covers. **RUSV**, the Russian Synodal Version of 1876, is likewise out of
copyright in its text.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader has to be able to tell
what rests on someone's testimony and what rests on our inference, and a text printed
without a name has quietly been claimed as ours. RUL-0181.
