# TAHOT — Translators Amalgamated Hebrew Old Testament

**TAHOT**, created by **STEPBible.org** based on work by scholars at **Tyndale House, Cambridge**.
<https://github.com/STEPBible/STEPBible-Data>, folder `Translators Amalgamated OT+NT/`.

Four files, about 70 MB, split by the publisher **"because a single file too large for Github"**:
`TAHOT Gen-Deu`, `TAHOT Jos-Est`, `TAHOT Job-Sng`, `TAHOT Isa-Mal`. They are one dataset and the
loader reads all four.

Read at commit **`89ece29525e3c51d61850b28b4d4cf27ef9cd321`** on **2026-09-03**.

## Licence: CC BY-NC 3.0

That is the publisher's own licence page, and it is the statement this project loads under. It is
not the only statement STEPBible makes, and the three do not agree, so all of them are below with
where each was read.

## The three statements, in full

### 1. The publisher's licence page — CC BY-NC 3.0

<https://stepbibleguide.blogspot.com/p/copyrights-licences.html>, read 2026-09-03. It separates the
software from the data and licenses them differently:

> **Tyndale House STEPBible datasets — CC BY-NC 3.0 Licence**
> These datasets may be freely used in other free projects. Profit-making projects are invited to
> ask for an individual licence. This data may be freely distributed, with attribution to Tyndale
> House, Cambridge UK

This is the only one of the three that speaks to redistribution as a permission rather than as a
request, and it grants it explicitly: *may be freely distributed, with attribution*.

### 2. The repository README — CC BY 4.0

`README.md` at the commit above, read 2026-09-03:

> # STEPBible Data Repository **CC BY 4.0**
> Data created initially by Tyndale House Cambridge, now curated by www.STEPBible.org
>
> This public licence allows you to:
> * **Include any part of STEPBible-Data in any software or publications** without requesting
>   permission
> * **Make changes to the data and record the differences**
>   You can make corrections or report possible errors to be checked at STEPBibleATgmail.com
>   Any changes made to data should be recorded and made available to subsequent users.
> * **Refer others to this repository as the source of the data.**
>   Updates or corrections are easier to implement when the data is distributed from a single
>   source. You are welcome to make a mirror, so long as it is kept up-to-date and has a link back
>   here.
>
> And you should:
> * **Credit it** to "STEP Bible" linked to www.STEPBible.org

### 3. The data files' own header — CC BY 4.0, and two clauses that are not in it

The block at the top of every TAHOT file, read from the bytes at the commit above:

> Data created by www.STEPBible.org based on work at Tyndale House Cambridge (CC BY 4.0)
>
> This licence allows you to:
> * Include any part of this data in software or publications without requesting permission
> * Download the data and reformat it for your application, **without changing the data**
> * Send any proposed corrections to STEPBibleATGmail.com. to be verified
>   (You MAY make changes yourself, but you should include a note of changes that can be viewed by
>   those who use your new data)
> * Refer others to github.com/STEPBible as the source of the data. **Please do not redistribute it
>   yourself.**
>   (Updates or corrections are easier to implement when the data is distributed from a single
>   source)
> * We'd love to hear about your project when you make it available. Email us at
>   STEPBibleATGmail.com..

## Why CC BY-NC 3.0 and not one of the other two

The three disagree on two points, and neither is left to inference here.

**Redistribution.** The header asks *"please do not redistribute it yourself"*, which CC BY 4.0 does
not permit a licensor to withdraw and which the licence page contradicts outright: *"This data may
be freely distributed, with attribution to Tyndale House, Cambridge UK"*. A database-backed site
that serves these words is redistributing them, so this is the clause that decides whether the data
can be loaded at all. The publisher's own licence page grants it.

**Changing the data.** The header's *"without changing the data"* is qualified in the very next line
of the same block — *"You MAY make changes yourself, but you should include a note of changes"* —
and the README at this commit no longer asks for it at all, saying instead *"Make changes to the
data and record the differences… Any changes made to data should be recorded and made available to
subsequent users."* This corpus does not change the data: TAHOT is read and everything derived from
it is written beside it as a derived value, with the method and the source on every row.

**NonCommercial** is the more restrictive of the two licence names and is the one taken. It is not a
new constraint here: BHSA, the Hebrew text these morphemes are joined to, is CC BY-NC 4.0 itself,
and the Open Hebrew Bible mapping beside it is CC BY-NC 4.0. **ShareAlike is the clause that would
have reached the corpus, and none of the three statements carries it.**

## Attribution

Credited as **"STEP Bible"**, linked to <https://www.STEPBible.org>, with the data attributed to
**Tyndale House, Cambridge UK**, which is what both the README and the licence page ask for.
`link_claim.source` on every claim this data stands behind names STEPBible and the licence, and
readers are pointed at github.com/STEPBible as the source rather than at a copy of ours.

**The files are not committed.** `.gitignore` keeps the corpus out and `scripts/fetch-stepbible.ps1`
fetches them from STEPBible at a named commit, which is also what the header asks for.

## What it is for

TAHOT segments every Hebrew word of the Leningrad codex into its morphemes and gives each one a
disambiguated Strong number and an English gloss — including the prefixes and suffixes that Strong's
concordance never numbered. Two things follow from that, and the corpus had neither.

**Which morphemes are prefixes.** The Open Hebrew Bible mapping the corpus already loads numbers a
prefixed מ with H4480, the same number as the free-standing preposition מִן, so nothing in it could
tell a prefix from a word.

**What each prefix means where it stands.** This is the one that moved the number. Before, the
project kept a list of five Strong numbers it treated as prefixes and, for each, the English words
it might render — twenty-three words in all, written by us. TAHOT prints a gloss per occurrence, so
*of*, *when*, *according*, *on*, *against*, *over*, *into*, *about* and a dozen more now reach the
morpheme they render, and 50,665 of the 69,141 matches rest on a gloss a source printed rather than
on that list.

The pairing of an English word to a Hebrew morpheme is still this project's inference either way.
What TAHOT changes is what the inference stands on, and every match it supports carries a second
claim naming it.
