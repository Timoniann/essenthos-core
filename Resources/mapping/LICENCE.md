# KJV-OT-mapped-to-BHS-full-mapping.csv

**Open Hebrew Bible Project** by **Eliran Wong**, licensed under a
[Creative Commons Attribution-NonCommercial 4.0 International License](http://creativecommons.org/licenses/by-nc/4.0/).

Based on a work at <https://github.com/eliranwong/OpenHebrewBible>.
Permissions beyond the scope of this licence may be available at
<https://marvel.bible/contact/contactform.php>.

Taken from `008-BHS-mapping-KJV/template/KJV-OT-mapped-to-BHS-full-mapping.csv.zip`.

## Why this file exists

The CSV beside it is redistributed in this repository, and CC BY-NC requires attribution
from anyone who redistributes it. Until 2026-09-03 nothing in either repository, in the
resource folder or in the project's own store named Eliran Wong at all — and this is the
single most load-bearing file in the corpus. Every `stated-by-source` link between the
King James and the Hebrew comes from it: 301,652 links, the only word-level testimony the
Old Testament has, and the calibration set every inferred method is measured against.
PRB-0179.

The **NonCommercial** term is not a new constraint. The Hebrew text this maps onto is
ETCBC's BHSA, which is itself strictly non-commercial, so the corpus already stood there
(NOT-0014). **Attribution is** the term that was being broken.

The project's own README adds:

> Please note that the use of the text of BHS here is under the same license of its
> original source at <https://github.com/ETCBC/bhsa#license>, which is strictly
> non-commercial.

## What the API says

`link.source` on every link from this file names the project rather than this filename, so
a reader asking where a claim came from is answered with a source and not a path.

# BHS-with-Strong-no-extended.csv

From the same **Open Hebrew Bible Project** by **Eliran Wong**, under the same CC BY-NC 4.0 licence
quoted above (the project README's licence section covers every work in the repository, and the BHS
text in it stays under ETCBC's own strictly non-commercial terms). Taken from
`002-BHS-with-Strong-no/BHS-with-Strong-no-extended.csv`; the file here is byte for byte the one at
commit 28ae9b2b (git blob hash compared on 2026-11-16). One row per BHSA word: the word, its
`E`-numbered BHSA word id and its Strong number, extended numbers included.

**It is read by no code and nothing of it is served.** `build/licences.tsv` declares it `never`, so
no product may read it. It is kept as the candidate a Strong number per Hebrew word could be taken
from, and would be credited to the project and to Eliran Wong if it ever is.
