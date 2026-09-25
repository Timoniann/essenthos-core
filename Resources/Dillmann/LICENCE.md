# Dillmann — the Lexicon Linguae Aethiopicae, as Beta maṣāḥǝft digitised it

**August Dillmann, *Lexicon linguae aethiopicae, cum indice latino*** (Leipzig: T. O. Weigel, 1865),
the standard dictionary of Ge'ez: headwords, Latin glosses, biblical citations and the Greek of the
Septuagint and the New Testament set beside them. Dillmann died in 1894 and the book is in the public
domain.

The **digital edition** is the Hiob-Ludolf-Zentrum für Äthiopistik's (Universität Hamburg), made in
the ERC project **TraCES** (Grant Agreement 338756) and kept by **Beta maṣāḥǝft** (general editor
Alessandro Bausi; the data by Liuzzo, Ellwardt, Krzyżanowska, Hummel, Pisani, Dickhut and others, as
the project credits them): <https://github.com/BetaMasaheft/DillmannData>, dataset DOI
10.25592/uhhfdm.130, shown at <https://betamasaheft.eu/Dillmann/>.

Pinned to commit `44f2da86568c4094917d8374b9fe61288715e606` (3 September 2026) and read at the source
on **2026-09-25**. The owner approved the download and accepted its ShareAlike on the Ge'ez meaning
layer only on 2026-09-25 (NOT-0197, DOC-0209 part B). `scripts/fetch-dillmann.ps1` fetches the
repository archive at that commit, keeps the thirteen entry folders (`1`–`11`, `new`, `new1`) and the
`README.md` and `repo.xml` beside them, checks the count and size of the entries and refuses any entry
whose header no longer states the licence below.

**13,727 entries, one TEI file each, 72.8 MiB.** The corpus reads Dillmann's own sense of each
(`source="#dillmann"`) — his Latin glosses and his Greek — and not the English senses and later
entries the TraCES project added, several of which are glossed from Leslau's dictionary. Entries that
only send the reader to another headword become spellings of that headword's entry; entries with
neither a gloss nor Greek, and entries that repeat another under the same headword, are not loaded:
**9,910 headwords** are. Which Ge'ez word is a form of which entry is concluded when the word is read
and is not stored.

## The licence

Three statements are attached to the data, and they disagree.

- **Every entry's TEI header** (all 13,727, checked by the fetch script):

  > This file is licensed under the Creative Commons Attribution-ShareAlike Non Commercial 4.0.

  with `<licence target="http://creativecommons.org/licenses/by-sa-nc/4.0/">` — a malformed address
  for CC BY-NC-SA 4.0 (<https://creativecommons.org/licenses/by-nc-sa/4.0/>).
- **`repo.xml`**, the eXist-db package descriptor: `<license>GNU-LGPL</license>`.
- **No licence file** at the repository's root; the `README.md` says the data is the digitisation of
  Dillmann's lexicon for the Dillmann application.

**How it is read (RUL-0105, RUL-0183).** The most restrictive statement on the bytes is CC BY-NC-SA
4.0, and that is the one taken. NonCommercial suits this project. ShareAlike binds what is derived
from the data: which Ge'ez word is taken as a form of which entry, and any rendering of Dillmann's
Latin — so that layer is shared under CC BY-NC-SA 4.0 as well. That is the owner's explicit exception
for this one layer (NOT-0197) and extends to nothing else. No English rendering of the Latin is made;
the reader shows the Latin as Dillmann wrote it.

**Attribution, as the reader and /v1/datasets give it:** Dillmann, *Lexicon Linguae Aethiopicae*
(1865); digital edition by Beta maṣāḥǝft and the TraCES project, Hiob-Ludolf-Zentrum für
Äthiopistik, Universität Hamburg, CC BY-NC-SA 4.0.
