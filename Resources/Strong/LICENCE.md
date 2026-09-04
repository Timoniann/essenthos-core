# StrongGreek.xml, StrongHebrew.xml — Strong's dictionaries, and a third work inside one of them

**Two files, three works, two sets of terms.** The scoping is stated in the OSIS headers
and nowhere else, so it is repeated here: a reader who is told only that Strong's is
public domain has been told something false about part of `StrongHebrew.xml`.

Read in the files' own headers on 2026-09-03.

## StrongGreek.xml — public domain

> Dictionary of Greek Words taken from Strong's Exhaustive Concordance by James Strong,
> S.T.D., LL.D. 1890 Public Domain -- Copy Freely

The XML was prepared in 2006 by **Ulrik Petersen** (<http://ulrikp.org>) from the ASCII
e-text, converting the transliteration to UTF-8 Greek. The header carries no separate
terms for that work.

## StrongHebrew.xml — public domain, except the glosses

Its OSIS header declares three works and says which rows are whose:

> `<work osisWork="Strong">` — Strong's dictionary, public domain
>
> `<work osisWork="TWOT">` — Theological Wordbook of the Old Testament
>
> `<rights type="x-copyright">Copyright © 1980 by the Moody Bible Iinstitute.</rights>`

and then scopes them:

> `<workPrefix path="//w/@ID" osisWork="Strong"/>`
>
> `<workPrefix path="//w/@src" osisWork="Strong"/>`
>
> `<workPrefix path="//w/@gloss" osisWork="TWOT"/>`

So the entry is Strong's and the **gloss is TWOT's**, under a 1980 Moody Bible Institute
copyright — 6,070 rows of this file. The typo in "Iinstitute" is the source's.

## What this means for the corpus

`Datasets.cs` declares the lexicon as two works rather than one for exactly this reason:
flattening it to a single author and a single licence would put the wrong name on those
rows and the wrong terms on all of them. The TWOT gloss is not public domain and is not
served as though it were.
