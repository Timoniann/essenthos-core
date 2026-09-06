# parsed/*.UWH — Robinson's Westcott-Hort

**The New Testament in the Original Greek** (1881), edited by **Brooke Foss Westcott** and
**Fenton John Anthony Hort**, in the parsed transcription of **Dr Maurice A. Robinson** (Wake
Forest, North Carolina), with morphological parsing tags and Strong's numbers. The repository is
maintained by **Dr Ulrik Sandborg-Petersen**, Scripture Systems ApS, Denmark.

From <https://github.com/byztxt/greektext-westcott-hort>, whose `README.md` is the whole of the
licence and is kept verbatim beside this file as `README-upstream.md`:

> ## License?
>
> Public Domain.  Copy freely.

Read at the source on **2026-09-06**, at commit
`91473892d7f36f1227e5c24f8d994d1da40311ad`. There is **no `LICENSE` file** in the repository — the
tree holds `.gitignore`, `README.md`, `parsed/` and `textonly/` and nothing else — so the README
sentence is not merely the closest statement to the bytes, it is the only one. It is the same
one-sentence licence, over the same editor's work, in the same organisation's repository, that
`../TextusReceptus/LICENCE.md` already sets out at length. RUL-0105.

The same README names who is responsible, which is why they are named here and in the `text` row:

> - Dr. Maurice A. Robinson, Wake Forest, North Carolina, USA is the primary author.
>
> - Dr. Ulrik Sandborg-Petersen, Scripture Systems ApS, Denmark, is the maintainer of this repo.

The 1881 edition itself is long out of copyright; what a licence could bind is the transcription,
the parsing and the numbers, and this is that grant.

## Two copies of this edition that were refused

Both are more restrictive than the original, and a re-wrapping cannot add a term to somebody else's
public-domain release. They are listed because whoever next wants an *accented* Westcott-Hort will
find them first.

**CrossWire's `WHNU` SWORD module is CC BY-NC-SA 4.0**, read from its module info page at
<https://crosswire.org/sword/modules/ModInfo.jsp?modName=WHNU>. ShareAlike, so RUL-0183 rules it out
on its own. It is also **not plain Westcott-Hort**: the module description says it carries readings
of Nestle-Aland 27 and UBS4, so it is a hybrid wearing a nineteenth-century name — exactly the
failure this corpus refuses, a text whose row would say 1881 and whose words would be from 1993.

**An accented Westcott-Hort edited by Joshua Grauman is reported to be CC BY-SA 3.0 US.** That
statement was **not** read at the source and is recorded here so nobody reaches for the accented
copy without checking. If accents are ever wanted, this is the file whose licence to read first,
and to expect ShareAlike from.

The consequence of taking the original is that **this text carries no accents or breathings**.
Robinson's transcription is in the beta code the Online Bible's Greek texts have always been
distributed in, one Latin letter per Greek letter and nothing else, and the loader converts the
letters and adds nothing.

## What is taken

- `parsed/*.UWH` — Robinson's parsed transcription, 27 files, one verse per record. This is what is
  loaded.
- `textonly/*.WH` and `textonly/TITLES.W-H` — the repository's own plain transcription of the same
  text. **Not loaded.** It is the answer key, and it settles the one question this file cannot
  answer about itself (below).

## Which edition this actually is, and how that was established

The parsed file is **not** a single edition. Westcott and Hort printed a second set of readings in
their margin, and Robinson carries both inline in the notation his Textus Receptus uses for
Stephanus against Scrivener: `| «one» | «other» |`, at **1,653 places**. A transcription that
silently preferred one side is a different edition from one that preferred the other, so the side
had to be proved rather than picked.

Taking the **first** alternative reproduces **7,887 of the 7,940** verses of `textonly/*.WH` word
for word; taking the second reproduces **6,774**. So the first alternative is the printed text and
the second is the margin, and only the text is loaded. The named readings agree: Matthew 27:16 has
`Βαραββᾶν` in the text and `[Ἰησοῦν] Βαραββᾶν` in the margin, Romans 5:1 `ἔχωμεν` against `ἔχομεν`,
1 Corinthians 1:1 `Ἰησοῦ Χριστοῦ` against `Χριστοῦ Ἰησοῦ`. `WestcottHortEditionTests` asserts the
measurement, so a release whose sides were ever swapped fails rather than loading the margin under
the text's name.

The editors' own marks are kept. Their **double brackets** — the long ending of Mark, the woman
taken in adultery, the sweat like blood at Luke 22:43, Luke 24:12 and the rest of the Western
non-interpolations — are the passages they judged no part of the original and printed anyway, and
their **single brackets** are words they doubted. Both are recorded on the word rather than dropped
with the punctuation, because a reader asking why Nestle prints something the modern critical text
does not is asking about exactly these.

Twenty-eight words stand in angle brackets in the parsed file and in no bracket at all in the plain
one. Nothing in the repository says what the sign means, so the corpus records that those words are
marked and does not say by whom or for what. Guessing would put a claim about Westcott and Hort's
judgement on Acts 16:12 and 1 Corinthians 2:4 that nobody made.

## One upstream error, repaired at load

Romans 10:5 is written `| [tou 3588] {T-GSM}` where every other bracketed word in the file closes
after the letters. Left alone, `3588]` is not a number any reader recognises and becomes a word of
Romans, shifting the parse of everything after it. It is the only occurrence in the 27 books and is
moved back onto the word it belongs to before parsing.

## Why it is attributed anyway

Public domain removes the obligation, not the reason. A reader of this corpus has to be able to
tell what rests on somebody's testimony and what rests on our inference, and a fact printed without
a name has quietly been claimed as ours. RUL-0181.
