# The Dead Sea Scrolls, in Text-Fabric (fetched, not loaded)

From <https://github.com/ETCBC/dss>, read at the source on **2026-11-24** at commit
**0b7774a72b6d0e91712aa2156d2a1d354f1f5e6d** (2026-11-10, "release-2.0.5-config"), data version
**2.0.5** under `tf/`, the latest. All 79 feature files are taken; the repository's `README.md`,
`docs/about.md` and `LICENSE` are kept beside them under `upstream/`, exactly as read.
`scripts/fetch-etcbc.ps1` fetches them, holds every file to its SHA-256 in `scripts/fetch-etcbc.sha256`,
and re-reads the sentences quoted below before it installs anything.

From `otype.tf`: **1,430,241 signs, 500,995 words, 1,001 scrolls.** The slot is the sign (one letter),
not the word. Biblical and non-biblical scrolls are one corpus, told apart by the `biblical` feature.

Nothing reads these files yet: there is no reader and no text. TSK-0232 is the work that would load the
biblical scrolls, and its own description says why that is the hardest witness to place (fragments,
lacunae that must never read as omissions).

## Licence: CC BY-NC 4.0, on the data and now on the repository's LICENSE file too

**1. `README.md`, "License":**

> This dataset is licensed under a [Creative Commons Attribution-NonCommercial 4.0 International License (CC BY-NC 4.0)](https://creativecommons.org/licenses/by-nc/4.0/).

**2. `LICENSE`**: the full legal text of Creative Commons Attribution-NonCommercial 4.0 International
(318 lines). TSK-0232, read in September 2026, found an MIT `LICENSE` ("Copyright (c) 2019 Dirk Roorda")
there; at this commit the file is the CC BY-NC 4.0 text, and GitHub reports `NOASSERTION`.

**3. Every feature file's header** (all 79):

> @license=Creative Commons Attribution-NonCommercial 4.0 International License
>
> @licenseUrl=http://creativecommons.org/licenses/by-nc/4.0/

**4. `docs/about.md`**, which says where the grant comes from:

> Upon learning of the current project, Martin Abegg graciously gave permission
> to Jarod Jacobs to use his data and
> to distribute the results under a CC-BY-NC license.

and, in its own "License" section: "The program code in this repo is freely available under the MIT
license. The data in this repo, notably the contents of its `.tf` subdirectory, is available under a
CC-BY-NC license".

**Not ShareAlike; NonCommercial is acceptable (RUL-0183).** The grant is Abegg's, passed on through
Jacobs, so attribution is owed to Martin Abegg and Jarod Jacobs as well as to the ETCBC (Dirk Roorda's
conversion).
