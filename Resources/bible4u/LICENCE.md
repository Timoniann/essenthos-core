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

RUL-0105 says to take the most restrictive statement actually attached to the bytes. For **UKR**
that question is settled (PRB-0325): the Ohienko translation is served under **CC BY-SA 4.0**, the one
grant there is, given through Wikimedia VRT (ticket 2013112610015211) for printings before 1991. Our
copy was shown by its orthography to be such a printing, and the owner kept the text under that licence. The text's own
declaration (`Bible4uTextSource`) carries it, with the credit and the share-alike term, and the
`<rights>` line above is recorded only as what this file says of itself.

**KJV** is out of copyright in its text; the typesetting of this particular XML is what
bible4u's line covers. **RUSV**, the Russian Synodal Version of 1876, is likewise out of
copyright in its text.

## What the corpus corrects in them, and why that is within the line

The line permits modifying the file so long as the Biblical content is unchanged, and the corrections
the corpus makes restore the content rather than change it. Each is a word the file prints that no
printing of the translation has, put back as a second witness to the same text prints it:

- **KJV.xml** against eBible's standardised 1769 text (Resources/KingJames2006): the capitals of the
  divine name and of the inscriptions, which the file lowercases, and its possessive, which it prints
  apart as *Lord 's*; 27 garbled readings; and the fourteen epistle subscriptions, which it omits.
- **RUSV.xml** against the Strong-tagged digitisation of the same 1876 translation
  (Resources/SynodalStrong): 209 tokens — words run together in Joshua 4, 1 Kings 7, Esther 6 and
  Isaiah 28 and 51 and once in Mark 10:28, a dash or hyphen written as the letter *г*, six misspelt
  words and four names in lower case. The list is `Essenthos.Forge/Loading/SynodalCorrections.json`,
  drawn by `scripts/synodal-corrections.py`. Only letters and spaces are taken from that edition;
  its Strong numbering is used for the links alone, as before, and stored nowhere.

Each text's row says so in its rights note, and the King James names the edition the corrections come from.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader has to be able to tell
what rests on someone's testimony and what rests on our inference, and a text printed
without a name has quietly been claimed as ours. RUL-0181.
