# *.usfm — the Lutherbibel of 1912

**Die Bibel nach der Übersetzung Martin Luthers**, in the revised text of **1912**. Luther
(1483–1546) translated the New Testament in 1522 and the whole Bible with the Wittenberg circle by
1534; the German churches revised the text in 1892 and again in 1912, and the 1912 revision is the
last one that is out of copyright.

From <https://ebible.org/deu1912/>, read at the source on **2026-09-06** and re-read on
**2026-09-20**. The archive taken is `https://ebible.org/Scriptures/deu1912_usfm.zip`;
`scripts/fetch-ebible.ps1 -Only Luther1912` is what fetches it, and it re-reads every statement
below before it replaces anything.

**66 books, 31,102 verses, 696,963 words**, of which **365,353 carry a Strong number**. It also
states its own verse numbering in 357 places, where eBible has renumbered the text to the English
scheme and recorded what the verse is called at home.

## The licence of the text, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside these files** — the same page
eBible serves at <https://ebible.org/deu1912/copyright.htm>. The whole statement is two words:

> Lutherbibel 1912
>
> The Holy Bible in German, Luther 1912
>
> **Public Domain**
>
> Language: Deutsch (German, Standard)
>
> Translation by: Martin Luther

**2. That page as served on the web**, read on 2026-09-20 and identical to the copy in the archive.

**3. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `deu1912`, read on
2026-09-20:

> `"Copyright"` = `public domain`, `"Redistributable"` = `True`, `"downloadable"` = `True`,
> `"sourceDate"` = `2025-12-29`, `"swordName"` = `deu1912eb`

The arithmetic agrees with all three: Luther died in 1546, and the 1912 revisers were a church
commission whose work is more than a century old.

## The Strong tagging is a different question, and nobody answers it

The 365,353 tags are not part of any of those statements. **None of the three says who assigned the
Strong numbers or on what terms eBible obtained them**, and a tagging layer is somebody's work even
where the text under it is nobody's.

What can be said about it was established under PRB-0375, by comparison rather than assumption. The
layer descends from the **Zefania XML Strong module of December 2005**. toledot.info, which
publishes a corrected version of that same layer, states the ancestry itself and calls the family
*"unvollständig und mit Fehlern behaftet"*. The descent shows in a shared error: Genesis 1:5 of this
file tags the conjunction *und* with H430, *Elohim*, and toledot's page tags it H430 too — toledot's
copy differs only where it corrected, adding H853 and changing H6440 to H8415 on *Tiefe*. So eBible
carries the uncorrected 2005 layer.

**Nobody names the author of that 2005 module or states its rights.** The corpus therefore records
it the way it already records the Zefania KJV+ tagging it loads — *no licence stated*, said plainly
in `Essenthos.Corpus/Corpus/Datasets.cs` — and the German tags are loaded on the same footing. The
Spanish tagging that arrives with the Reina-Valera in the same catalogue is a different case and is
refused: see `../ReinaValera1909/LICENCE.md`.

## Attribution

Not required by anything above. Recorded because RUL-0181 asks for it whatever the licence says: the
translation is Martin Luther's with the Wittenberg circle, the text served is the 1912 revision made
by the German evangelical church conferences' commission, this digital edition is **eBible.org**'s,
and the Strong tagging on it is an unnamed hand's, by way of the Zefania project.

Cite as: *Die Bibel nach der Übersetzung Martin Luthers, revised text of 1912, in the digital
edition eBible.org publishes as deu1912.*
