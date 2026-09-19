# *.usfm — Brenton's Septuagint, and the modules that say Public Domain and are not

**Η ΠΑΛΑΙΑ ΔΙΑΘΗΚΗ ΚΑΤΑ ΤΟΥΣ ΕΒΔΟΜΗΚΟΝΤΑ**, the Greek **Sir Lancelot Charles Lee Brenton**
(1807–1862) printed facing his English translation — the Sixtine edition of 1587 as Valpy reprinted
it, which follows Codex Vaticanus without being a transcript of it. Samuel Bagster and Sons
published it in London in 1844 and added the Apocrypha in 1851.

From <https://ebible.org/grcbrent/>, taken on **2026-09-02** and the statements re-read at the
source on **2026-09-20**. The archive is `https://ebible.org/Scriptures/grcbrent_usfm.zip`; the
archive itself was unpacked and discarded, and `copr.htm` is kept beside the data.

**52 files, of which the corpus loads 51 as 51 books, 1,041 chapters, 27,244 verses, 543,397
words.** Accented polytonic Greek — Genesis 1:1 reads `ἘΝ ἀρχῇ ἐποίησεν ὁ Θεὸς τὸν οὐρανὸν καὶ τὴν
γῆν`. It arrived with **no annotation of any kind**: no morphology, no lemmas, no Strong numbers and
no alignment to anything. Its lemmas come from GLAUx (`../Glaux/LICENCE.md`), and its links to
Swete's edition are this project's own, drawn from the letters both editions print.

## The licence, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside these files** — the same page
eBible serves at <https://ebible.org/grcbrent/copyright.htm>. It says the whole of it twice, under
the title and again in the footer:

> μετάφραση των εβδομήκοντα
>
> The Greek Septuagint with Apocrypha, compiled by Sir Lancelot C. L. Brenton
>
> **Public Domain**
>
> Language: Ἑλληνική (Greek, Ancient) · Dialect: Koine

**2. That page as served on the web**, read on 2026-09-20.

**3. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `grcbrent`, read on
2026-09-20:

> `"Copyright"` = `public domain`, `"Redistributable"` = `True`, `"downloadable"` = `True`,
> `"sourceDate"` = `2026-04-08`, `"swordName"` = `grclxxbrenteb`

Brenton died in 1862 and the edition was printed between 1844 and 1851, so the arithmetic agrees
with all three. No rights holder is named anywhere, and none is owed.

## Why this text and not an annotated one

Recorded here because the reason is a licence reading and belongs beside the data (DOC-0085,
DOC-0082). **Every morphologically tagged Septuagint in existence descends from CATSS**, the
Computer Assisted Tools for Septuagint Studies text at the University of Pennsylvania, which is
CC BY-NC-SA. NonCommercial does not bar a dataset here (RUL-0183); **ShareAlike is the clause that
does the work**, and CATSS carries it. Swete's two machine-readable copies are CC BY-SA 4.0
(`nathans/lxx-swete`, which is `../Swete/`) and GPL-3.0 (`eliranwong/LXX-Swete-1930`) — copyleft
over data in both cases. Brenton from eBible is the one Septuagint that is public domain outright,
which is why the corpus's unannotated Greek Old Testament is this one.

## Four modules that declare Public Domain and are not (PRB-0164)

Kept beside the data so that the next person weighing a Septuagint module does not have to find it
again. Read on 2026-09-03; each declares `DistributionLicense=Public Domain` in the field a loader
would read, and each contradicts it in its own prose:

    LXX_th (theWord)              "Public Domain" in the field
    CrossWire "lxx" (attic)       "Public Domain" in the field
    Xiphos 2TGreek                "Public Domain" in the field
    theWord "ALXX+ Analytic Septuagint Strongs (PD).ot"   asserts it in the filename, and carries
                                  no creator, no source and no licence text at all

Three of them carry, in their own `About`, the notice the fourth leaves out:

> The Analytic Septuagint is not to be used, either directly or indirectly, for commercial purposes
> without prior written consent of Steve Amato and the legal authors and developers of the
> morphology of the LXXM

and trace the chain onward — LXXM to *"CATSS under the direction of R. Kraft"* to *"the computer
form prepared by the TLG Project"*. So the bytes are a CATSS derivative, whose clause 3 requires
controlling access and registering every downstream recipient, which no public API can do.

**None of the four is loaded, and none may be**, whatever the licence field says. CrossWire's
current `LXX` module, read at
<https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=LXX> on 2026-09-20, no longer claims
the public domain at all — it now reads `Copyrighted; Free non-commercial distribution` over a text
it says was built from CCAT's files, which is the same chain stated honestly.

The general lesson is worth more than the four names: **a licence field is a claim by the packager,
not by the rights holder.** Where a module carries prose about its own permissions, that prose is
the licence and the field is a summary somebody typed (RUL-0105).

## Attribution

Not required, and recorded because RUL-0181 asks for it whatever the licence says: the Greek is the
Sixtine edition of 1587 as reprinted by Valpy, compiled and printed by **Sir Lancelot C. L.
Brenton**, and this digital edition is **eBible.org**'s.
