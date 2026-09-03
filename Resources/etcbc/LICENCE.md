# The .tf files — BHSA, the Hebrew text the whole corpus rests on

**Biblia Hebraica Stuttgartensia Amstelodamensis**, by the **Eep Talstra Centre for Bible
and Computer**, VU University Amsterdam. <https://github.com/ETCBC/bhsa>,
<https://shebanq.ancient-data.org>.

**Licence: CC BY-NC 4.0** — <https://creativecommons.org/licenses/by-nc/4.0/>.
NonCommercial, and the only text in this corpus that is.

The README of `ETCBC/bhsa` states it in as many words:

> Attribution-NonCommercial 4.0 International (CC BY-NC 4.0) — do not use the data for
> commercial applications without consent

with commercial enquiries directed to the **Deutsche Bibelgesellschaft**, which holds the
rights in the underlying BHS text.

Read on 2026-09-02, against the repository README.

## The badge says something else, and the badge is wrong

`ETCBC/bhsa` shows an **MIT** badge on GitHub. It covers the conversion code, not the
data; the data is separately CC BY-NC. Believing the badge would put a permissive licence
on the Hebrew text of the entire Old Testament. This is the case RUL-0105 was written
about: a badge, a README and a registry field are three different claims, and here they
disagree.

## Which release this is

The Text-Fabric files declare it themselves, in every feature header:

> `@dataset=BHSA`, `@datasetName=Biblia Hebraica Stuttgartensia Amstelodamensis`,
> `@author=Eep Talstra Centre for Bible and Computer`, `@version=2021`,
> `@encoders=Constantijn Sikkel (QDF), Ulrik Petersen (MQL) and Dirk Roorda (TF)`,
> `@dateWritten=2021-12-09T14:17:55Z`

They state the dataset, the version and the encoders. They do **not** state the licence —
which is why this file is here.

## What the terms require of this project

**Attribution**, which `BhsaTextSource` carries as a citation on the text row and
`/v1/datasets` prints. **NonCommercial**, which the corpus is: the owner has accepted
NonCommercial as a standing constraint (RUL-0183), and `Redistribution.NonCommercialOnly`
records it on the row so nothing downstream can quietly assume otherwise.

The Old Testament word mapping in `../mapping/` is CC BY-NC for the same reason and says
so — it maps onto this text and inherits its terms.
