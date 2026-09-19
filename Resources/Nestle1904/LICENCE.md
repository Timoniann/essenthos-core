# Nestle1904.xml, berean-interlinear-glosses.xml

**Nestle 1904 Greek New Testament**, from **biblicalhumanities.org**,
<https://github.com/biblicalhumanities/Nestle1904>. The 1904 edition is out of copyright; the
transcription, the morphology and the Strong numbers are modern work, and each component of that
repository carries its own terms.

## The repository declines a licence, so the component was identified

`README.md` at the top of the repository, read at the source on **2026-09-20**, verbatim:

> Licensing and copyright terms differ among the components in this repo. See the README
> associated with each subdirectory.

So a repository-level CC0 is precisely what upstream does not state, and the four components state
four different things:

| component | what it holds | terms, read at the source on 2026-09-20 |
|---|---|---|
| `morph/` | the text with morphology, lemmas and Strong numbers — **the file loaded here** | CC0 1.0 |
| `xml/` | Jonathan Robie's XML markup of the same text | CC BY-SA 4.0 |
| `xhtml/` | Diego Santos's base text as extracted from his site | "The site declares that its content is public domain." |
| `glosses/` | the Berean interlinear glosses — **the second file loaded here** | see below |

**`Nestle1904.xml` in this folder is `morph/Nestle1904.xml`, and that is measured rather than
assumed.** With the line endings normalised to LF, this folder's copy hashes to
`8b7dc2eeb14e5d7ee9e07ad906e4fe83381a2a18`, which is the git blob id GitHub reports for
`morph/Nestle1904.xml` on `master` — 25,769,502 bytes, byte for byte the same file. The `xml/`
directory's `nestle1904.xml` is 1,149 bytes, an XInclude wrapper around the 27 book files, and is
not what is here. The distinction matters: the markup in that directory is share-alike and the
morphology file is not.

### `morph/README.md`, which is the licence of the file loaded

> To the extent possible under law, biblicalhumanities.org has waived all copyright and related or
> neighboring rights to the biblicalhumanities.org Nestle 1904 Morphology.

under CC0 1.0, <https://creativecommons.org/publicdomain/zero/1.0/>. The same readme names the work
it rests on:

> The text has been augmented with morphological tags, lemmatization, and Strong's numbers by
> Dr. Ulrik Sandborg-Petersen of Scripture Systems, Denmark.

and *"The present morphological edition is largely the work of Dr. Robinson, re-purposed for a
different text."* Its version 1.3 of 15 April 2017 records the text as corrected to version 2.12 of
Diego Santos's upstream Nestle 1904 project.

So the corpus records this text as `CC0-1.0` with no rights holder, and that reading is now attached
to a component rather than to a repository.

## berean-interlinear-glosses.xml

English glosses keyed to the same `osisId` word identifiers, from the Berean interlinear, served on
every word of the Greek New Testament. `glosses/README.md` states two things in sequence, and both
are recorded because they are not the same claim:

> The Holy Bible, Berean Interlinear Bible, BIB
> Copyright ©2016 by Bible Hub
> Used by Permission. All Rights Reserved Worldwide.
>
> This is now in the public domain:
> http://berean.bible/terms.htm

That URL now redirects to <https://berean.bible/licensing.htm>, which says — read at the source on
2026-09-20, and recorded in full in `../Berean/LICENCE.md` — *"The Berean Bible and Majority Bible
texts are officially placed into the public domain as of April 30, 2023"* and *"Licensing is not
required for any use."* The page names the Berean Interlinear Bible only in its navigation, so the
sentence that covers these glosses is the one about "the Berean Bible … texts", and the page's own
footer still reads *"Copyright © 2021 Berean Standard Bible. All rights reserved."* Clear Bible read
the same statement the same way and say so in `../Macula/LICENSE-upstream.md`: the Berean
Interlinear gloss column there is marked *"officially placed into the public domain as of April 30,
2023"*.

**The owner ruled on 2026-09-20 that the glosses stay**: the publisher's own file says they are in
the public domain, and the licensing page places the Berean texts there. Recorded here because a
ruling that lives only in a ticket is one the next reader of this folder will not find. The corpus
credits them on the sources page as a dataset of their own — *Berean Interlinear Bible*, Bible Hub /
Berean Bible — counted by the 137,623 Greek words that carry one, which is how a source that
annotates a text rather than contributing rows is credited here.

**This folder's copy is not identical to the one on `master`.** With line endings normalised it is
17,157,974 bytes against the 17,157,952 GitHub reports — 22 bytes apart — so it was taken at a
different state of the file, and which one was not established. The upstream file has five commits,
the last of them `ff726671` of 2017-11-15, *"Fixed several well-formedness errors."* Nothing about
the terms turns on which: the glosses' own readme has not changed with them.

Unlike `../Macula/` and `../MorphGnt/`, this folder keeps no copy of the upstream readmes: nothing
fetches it — there is no `scripts/fetch-nestle.ps1` — and the two files predate the convention. The
statements are quoted above instead, with the date each was read, which is the part a reader offline
needs.

## Attribution

Required by nothing above and recorded anyway (RUL-0181). The names owed:

- **Nestle 1904 Greek New Testament**, Eberhard Nestle's edition, digitised by
  biblicalhumanities.org — transcribed by **Diego Renato dos Santos**, morphology, lemmas and Strong
  numbers by **Dr Ulrik Sandborg-Petersen** after **Dr Maurice A. Robinson**, the repository
  assembled by **Jonathan Robie**. CC0 1.0 for the file loaded.
- **Berean Interlinear Bible**, Bible Hub / Berean Bible, <https://berean.bible>, for the English
  gloss on each word.

Anyone re-checking should read the readme of the subdirectory the file came from rather than the
GitHub licence badge — for the neighbouring `etcbc` folder those two disagree, and a badge is not a
licence statement.
