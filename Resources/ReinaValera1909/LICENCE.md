# *.usfm — the Reina-Valera of 1909, and the Strong tagging that is refused

**Santa Biblia, Antigua versión de Casiodoro de Reina revisada por Cipriano de Valera**, in the
revision of **1909** — the last Reina-Valera that is out of copyright. Reina (1520–1594) printed the
whole Bible in Spanish at Basel in 1569; Valera (1531–1602) revised it in 1602; the British and
Foreign Bible Society's revisers produced the 1909.

From <https://ebible.org/spaRV1909/>, read at the source on **2026-09-06** and re-read on
**2026-09-20**. The archive taken is `https://ebible.org/Scriptures/spaRV1909_usfm.zip`;
`scripts/fetch-ebible.ps1 -Only ReinaValera1909` is what fetches it, and it re-reads every statement
below before it replaces anything.

**66 books, 31,084 verses, 703,737 words.** The archive carries **390,758 Strong tags** standing
over 671,228 words, and **3,501 `\add` spans** — the italics of a printed Reina-Valera, the marks
the translators themselves made for words they supplied. The spans are loaded. **The Strong tags are
not**, and the rest of this file is why.

## The licence of the text, in three statements that agree

**1. `copr.htm`, which ships inside the archive and is kept beside these files** — the same page
eBible serves at <https://ebible.org/spaRV1909/copyright.htm>:

> Santa Biblia — Reina Valera 1909
>
> The Holy Bible in Spanish, Reina Valera translation of 1909
>
> **Public Domain** / **Dominio Público**
>
> Language: Español (Spanish) · Dialect: Castellano 1909
>
> Translation by: Reina y Valera

**2. That page as served on the web**, read on 2026-09-20 and identical to the copy in the archive.

**3. eBible's catalogue**, `https://ebible.org/Scriptures/translations.csv`, row `spaRV1909`, read
on 2026-09-20:

> `"Copyright"` = `public domain`, `"Redistributable"` = `True`, `"downloadable"` = `True`,
> `"sourceDate"` = `2015-08-10`, `"swordName"` = `spaRV1909eb`

Valera died in 1602 and the 1909 revisers' work is more than a century old, so the text is out of
copyright by any arithmetic as well as by all three statements.

## The tagging is Rubén Gómez's, and eBible's public-domain line does not reach it

Established by comparison on 2026-09-06, not suspected. PRB-0375 is the finding.

This archive tags 3 John 1:12 as `Todos|strong="G5259,G3956"` and `misma|strong="G5259,G0846"`, and
1:1 as `EL|G3588`, `anciano|G4245`, `muy amado|G0027`. `https://bibliaparalela.com/rvs/3_john/1.htm`
prints the identical numbers on the identical phrases, under the line:

> Reina-Valera 1909 con números de Strong. Cortesía de Rubén Gómez. Utilizado con permiso.

**G5259 is ὑπό and it belongs on neither *Todos* nor *misma*.** The same mistake in the same two
places is a copy, not a coincidence; the only systematic difference between the two is eBible's
zero-padding, which its publisher documents as its own normalisation.

**Gómez's own terms**, from his announcement of 2012-03-28 at
`bsreview.org/blog/2012/03/reina-valera-1909-con-numeros-de-strong.html`: the 1909 *text* is public
domain and the tagging is his work; *"no envío los archivos a particulares"* — he gives the files to
software publishers and not to individuals; and the general condition is *"que el módulo esté
bloqueado"*, that the module be locked so only he can change it. CrossWire's `sparv1909.conf`
matches: `DistributionLicense=Copyrighted; Permission to distribute granted to CrossWire`,
`CipherKey=SpaRV1909`, `TextSource=http://www.BSReview.org`.

eBible carries the same layer **unlocked, editable, uncredited and under a Public Domain line**, and
Debian has taken it on from there as `sword-text-sparv` with `License: public-domain`.

**So the most restrictive statement attached to these bytes is Gómez's, not eBible's** (RUL-0105),
and the loader refuses the tagging by name: `Essenthos.Forge/Loading/EbibleTextSource.cs` lists this
text among those whose tagging is not ours to take, and a test pins the refusal so that a later pass
cannot quietly read the numbers back off the same file.

**The owner's ruling, 2026-09-20:** the numbers may be used *for the mapping only* and shown
nowhere. That is the position the Russian Synodal's Bob Jones numbering is already loaded under
(`../SynodalStrong/LICENCE.md`) — the numbering is read for the length of a run, the corpus keeps
only the correspondences drawn from it, and no word ever carries the number. Nothing stores or
serves a Spanish Strong number today: the words are loaded without them, so the API sends none and
the reader sees none. The pass that would read them in memory and draw links from them has not been
written.

The text loses less than it looks. The Spanish reaches the originals through `Clear-Bible/Alignments`
— a hand-made whole-Bible alignment of this exact file, CC BY 4.0, recorded in
`../ClearBible/LICENCE.md` — which is a better answer than a lemma-level tag anyway.

## Attribution

Not required by the text's own terms, and recorded because RUL-0181 asks for it whatever the licence
says: the translation is Casiodoro de Reina's, revised by Cipriano de Valera and by the British and
Foreign Bible Society's revisers of 1909; this digital edition is **eBible.org**'s; and the Strong
tagging that arrives with it — and is not loaded — is **Rubén Gómez**'s.

Cite as: *Santa Biblia, Antigua versión de Casiodoro de Reina revisada por Cipriano de Valera,
revisión de 1909, in the digital edition eBible.org publishes as spaRV1909.*
