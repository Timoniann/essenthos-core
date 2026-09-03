# SF_2009-01-20_ENG_KJV_(KJV+).xml — the Zefania KJV+ module

**KJV+**, the King James Bible with Strong's numbers, in Zefania XML. Its own `<INFORMATION>`
block, verbatim from the file beside this one, is all the provenance there is:

```xml
<title>KJV+</title>
<publisher>Free Bible Software Group</publisher>
<date>2009-01-20</date>
<creator>Theologische Initative Freiburg</creator>
<description>King James Bible with Strongs</description>
<contributors>e-Sword</contributors>
<rights>
</rights>
```

## No licence is stated, anywhere

**The `<rights>` element is present and empty.** The format has a place to declare the terms, the
packager opened it, and left it blank — which is not the same as a module that omits the element,
and not the same as one that says *Public Domain*. The format's own worked example fills it in
(`<rights>Public Domain</rights>`, in the documentation carried by
<https://github.com/biblenerd/Zefania-XML-Preservation>), so the blank is a packager who had the
field in front of them. What other distributed modules put there was not checked and does not
matter: a term stated on one module is not a term on this one.

**The SourceForge project declares none either.** `zefania-sharp`, the project that distributes it,
returns an empty licence list from its own project record — `"categories" -> "license": []` at
<https://sourceforge.net/rest/p/zefania-sharp> — and neither the project page nor the
`Bibles/ENG/King James/KJV+/` directory that holds the file shows any licence or copyright text.
Read at the source on 2026-09-03.

**The GPL statement that turns up in search results is about the format, not the modules.** The
Zefania XML markup language and its tooling are GPL; the preservation repository that mirrors the
distribution says so of itself and says nothing about the rights in the 100-odd Bible modules it
holds. A licence on a schema does not travel to a text encoded in it.

So the honest answer, and the one recorded in `Endpoints/Datasets.cs`, is **no licence stated**.
RUL-0105 says to believe the statement closest to the bytes; here the statement closest to the
bytes is silence, and silence is what gets reported rather than the most convenient reading
available.

## What is nevertheless true about the contents

This is our reading of what the file contains, and it is deliberately kept apart from the licence
field, because a conclusion of ours must never be printed as somebody's grant.

- The **King James text** is the 1611 translation in its 1769 Oxford revision. It is out of
  copyright by age everywhere the question is settled by age. In the **United Kingdom** it is not:
  Cambridge and Oxford hold the Crown's letters patent to print it, which is perpetual and is not a
  copyright term that runs out. Nothing this project does is UK printing, and the text itself is
  read from elsewhere in any case — see below.
- **Strong's numbering** comes from James Strong's *Exhaustive Concordance of the Bible*, 1890. Out
  of copyright by age.
- The **tagging** — which English word carries which number — is the packager's work, derived from
  the e-Sword module named as contributor. It is that layer, and only that layer, for which no
  grant exists. It is also 2009 work over a 19th-century apparatus, and it is what this file is
  used for.

If somebody establishes terms for it, they belong here and in the dataset declaration. Until then
the endpoint says *No licence stated*, which is the fact.

## What is taken, and what is not

Only the **Strong numbers**. The King James text served by this corpus is loaded from a different
source; nothing of this module's wording reaches a `text` row.

## What it contributes

The link rows whose `source` begins `Zefania KJV+ Strong numbers, matched within the verse
against` — 112,722 against Scrivener 1894 and 108,213 against Nestle 1904.

**The numbers are theirs; the pairing is ours.** Which tagged English word corresponds to which
Greek word is matched inside the verse by this project, at a confidence, and no part of that
pairing is stated by anyone. The dataset entry says so, so that a reader cannot read 220,935 links
as somebody's testimony.

## Why it is attributed at all, given that nothing requires it

Nothing here requires anything, because nothing here says anything. That is precisely the case
RUL-0181 was written for: a reader of this corpus has to be able to tell what rests on somebody
else's work and what rests on ours, and a file whose terms nobody could establish is the one most
likely to be quietly absorbed. PRB-0180.
