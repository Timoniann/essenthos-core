# SweteScans/ — the Internet Archive's scans of Swete's three volumes, and their OCR

**The Old Testament in Greek according to the Septuagint**, edited by **Henry Barclay Swete**
(1835–1917), Cambridge University Press — the printed edition `Resources/Swete/` is a transcription
of. Held here only to read the page where that transcription lost words: nothing in this folder is
loaded, and no text of it is served. What it settles is written, word by word with the volume and
page, into `Essenthos.Forge/Swete/SwetePage.json` by `scripts/swete-scans.py`.

Downloaded on **2026-09-24** from the Internet Archive, with the owner's approval of that day, one
item at a time and nothing else:

| Volume | Printing | Item | Source |
|---|---|---|---|
| 1 — Genesis to 4 Kingdoms | third edition, 1901 | `oldtestamentingr0001henr` | <https://archive.org/details/oldtestamentingr0001henr> |
| 2 — 1 Chronicles to Tobit | second edition, 1896 | `oldtestamentingr02swetuoft` | <https://archive.org/details/oldtestamentingr02swetuoft> |
| 3 — Hosea to 4 Maccabees | third edition, 1905 | `theoldtestamenti03swetuoft` | <https://archive.org/details/theoldtestamenti03swetuoft> |

The printing each is, and its edition's date, are read off the title pages in the OCR.

## What is here

For each item, from `https://archive.org/download/<item>/<file>`:

- `<item>_djvu.xml` — the archive's OCR of every page, word by word with its place on the page
  (Tesseract with Greek: `-l eng+ell+Latin+Greek` on volume 1, `grc+eng` and `eng+grc` on 2 and 3).
  This is what the script reads. 43.6, 35.3 and 45.8 MB.
- `<item>_djvu.txt` — the same OCR as plain text. 4.3, 3.5 and 4.4 MB.
- `<item>_page_numbers.json` — the printed page number of each scanned leaf, which is how a
  restoration cites its page.
- `pages/<item>_NNNN.jp2` — single page images, fetched one by one from the item's
  `<item>_jp2.zip` (`https://archive.org/download/<item>/<item>_jp2.zip/<item>_jp2%2F<item>_NNNN.jp2`)
  only for the leaves where the OCR had to be checked by eye. About 400 KB each.
- `oldtestamentingr0001henr.pdf` — volume 1 as the archive's PDF, 44,939,567 bytes. Not used: its
  page images are JBIG2, which nothing here decodes, so the single JP2 pages are read instead and the
  other two volumes' PDFs were not fetched.

The OCR owes nothing to First1KGreek's transcription — a different reading of the same printed page
by a different engine — which is what makes it a witness to what the page prints.

## Rights

**Public domain.** Swete died in 1917 and the three printings are of 1896, 1901 and 1905: out of
copyright on life-plus-seventy everywhere, and before the United States' 1929 line. The archive's
metadata says so for volumes 2 and 3 (`possible-copyright-status: NOT_IN_COPYRIGHT`, read in each
item's `https://archive.org/metadata/<item>` on 2026-09-24; digitised by the University of Toronto,
Robarts Library, collection `university_of_toronto`). Volume 1's item, scanned by the archive itself
in 2026 (`collection: internetarchivebooks`), states no copyright status and no licence at all; the
book it scans is the 1901 printing of the same public-domain edition, so it is public domain for the
same reason. Neither item attaches any licence or terms of its own to the scans or the OCR, and none
is recorded here because none is stated.

Attribution, which nothing requires and this project gives anyway (RUL-0181): the scans and OCR are
the Internet Archive's, volumes 2 and 3 from the University of Toronto's copy.

Not committed, 130 MB and more; this file is.
