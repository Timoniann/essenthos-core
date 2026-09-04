# parsed/*.UTR — Robinson's Textus Receptus

**The New Testament Greek text edition of Textus Receptus**, edited by **Dr Maurice A. Robinson**
(Wake Forest, North Carolina), with morphological parsing tags and Strong's numbers. The
repository is maintained by **Dr Ulrik Sandborg-Petersen**, Scripture Systems ApS, Denmark.

From <https://github.com/byztxt/greektext-textus-receptus>, whose `README.md` is the whole of the
licence and is kept verbatim beside this file as `README-upstream.md`:

> ## License?
>
> Public Domain.  Copy freely.

Read at the source on 2026-09-03. There is **no `LICENSE` file** in the repository — the tree holds
`.gitignore`, `README.md` and `parsed/` and nothing else — and the GitHub repository record reports
`"license": null`, so the README sentence is not merely the closest statement to the bytes, it is
the only one. RUL-0105.

## The other statements, which is why this file lists them

RUL-0105 says to read all of them and believe the one closest to the bytes. Three others exist and
none of them displaces the README.

**Robinson's own permission notice**, given for the sibling *Byzantine Textform* edition at
<https://byzantinetext.com/study/editions/robinson-pierpont/>, is the fullest statement the editor
has written, and it contradicts itself in its own header:

> This Compilation is Copyright © 2005 by Robinson and Pierpont.

and then, of the same text:

> Anyone is permitted to copy and distribute this text or any portion of this text.

> All rights to this text are released to everyone and no one can reduce these rights at any time.

> Copyright is not claimed nor asserted for the new and revised form of the Greek NT text of this
> edition.

The release is the operative half; the copyright line is the publisher's boilerplate on a
compilation that the next paragraph gives away. It also carries a request, which is a request and
not a condition:

> the present editors' names and the title associated with this text as well as this disclaimer be
> retained in any subsequent reproduction

That request is honoured here: the editor is named in `Endpoints/Datasets.cs`, in the two
`text` rows for Stephanus 1550 and Scrivener 1894, and in this file.

**The re-wrappings are more restrictive than the original.** The CrossWire SWORD module and the
Zefania build of the same text both carry CC BY-NC-SA over data that is public domain at source.
Neither is what is loaded here, and neither can add a term to somebody else's public domain
release. Take the original.

**byzantinetext.com's own front page** states nothing about licensing at all, so it neither adds
nor removes.

## What is taken

`parsed/*.UTR`, Robinson's composite: one token stream carrying **both** printed editions, with a
variant group at each of the places they differ. Two texts and one file.

## What it contributes

The link rows whose `source` reads `byztxt/greektext-textus-receptus, the variant groups of the
composite` — 140,602 of them, every one `stated-by-source` with no confidence, because nothing here
is aligned or guessed. The file itself says which reading belongs to which edition, including the
52 groups where one edition has a word and the other has none.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader of this corpus has to be able to
tell what rests on somebody's testimony and what rests on our inference, and a fact printed without
a name has quietly been claimed as ours. RUL-0181, PRB-0180.
