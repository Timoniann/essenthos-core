# R. H. Charles, 1 Enoch and Jubilees, translated from the Ethiopic

Fetched by `scripts/fetch-charles.ps1` from CrossWire's SWORD repository, each archive pinned by SHA-256 and its
configuration re-read for its licence line before it is installed. Read at the source on 2026-11-16.

| Module | Archive | SHA-256 | Version | What it is |
|---|---|---|---|---|
| `Enoch` | `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/Enoch.zip` | `13f31fa8360e21ac7ceec6d47eb2af7f8ba743011afabdc47aba4a4d622f8731` | 2.0 (2023-07-29) | 1 Enoch, Charles's translation as printed in *The Apocrypha and Pseudepigrapha of the Old Testament in English*, vol. II (Oxford: Clarendon Press, 1913), the translation of his second edition of *The Book of Enoch* (1912) |
| `Jubilees` | `https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/Jubilees.zip` | `c613b0a23e3d084e8eee89b550ce98ecd051c3cba85d18869f3c44659997a734` | 1.0 (2022-09-10) | Jubilees, Charles's translation as printed in the same volume (1913) |

## Every statement attached to the bytes

- **The modules' configurations** (`mods.d/enoch.conf`, `mods.d/jubilees.conf`, kept beside the data) both say
  `DistributionLicense=Public Domain`.
  - Enoch: `TextSource=https://ccel.org/c/charles/otpseudepig/enoch/ENOCH_1.HTM + https://archive.org/details/apocryphapseudep02charuoft`.
    The text says it was "Scanned and Edited by Joshua Williams, Northwest Nazarene College, 1995" and "Edited by:
    Marjan Šavli in Barbara Hojkar Šavli, 2023" (History 2.0: compared with en.wikisource.org's text, typing
    errors removed, references and notes added).
  - Jubilees: `TextSource=https://www.ccel.org/c/charles/otpseudepig/jubilee/index.htm`; "Text scanned and corrected
    by Joshua Williams, Northwest Nazarene College, c. 1995".
- **The Christian Classics Ethereal Library**, the modules' stated source, states no rights on the pages of these
  two texts or on the collection page (`ccel.org/ccel/charles/otpseudepig`). Its site-wide notice
  (`ccel.org/about/copyright.html`) says most of its editions are of books "public domain in the United States",
  that they may be "used for personal, educational, or non-profit purposes", and to contact it for permission to
  republish its works or use them commercially. These pages are the Wesley Center's scan as CCEL holds it, not an
  edition of CCEL's own; Essenthos is non-commercial in any case (NOT-0014, RUL-0183).
- **The Wesley Center Online** at Northwest Nazarene, where Williams's scans were first published, no longer
  resolves (`wesley.nnu.edu`, 2026-11-16), so its notice could not be read.

## Why the text is free

- **The work.** Robert Henry Charles (1855-1931) made both translations; they were published in 1912 and 1913,
  before 1931, so they are public domain in the United States, and Charles died more than seventy years ago, so
  they are public domain wherever the term is life plus seventy years or less (Ukraine, the EU, the UK).
- **The transcription.** Williams's 1995 scan and the Šavlis' 2023 corrections type out a public-domain text; a
  typed transcription is not a work of its own (guide Sources, "Transcriptions of public-domain texts"). A database
  right on Williams's scan, where one could arise, would have run fifteen years and ended in 2010; the Šavlis'
  corrections are given away by the module's own Public Domain statement.
- **What is not taken.** The modules' notes and cross-references (Charles's footnotes as the Šavlis keyed them,
  their references), Charles's chapter summaries, section headings, the prologue of Jubilees, the anno mundi dates
  Charles sets in square brackets, and the translations of the Greek and Latin fragments Charles prints beside the
  Ethiopic in Enoch (marked `)Gg`, `)Gᵍ`, `)Gr`, `)L` in the module), whose Greek and English the module's editors
  supplied in places, are left out: the text loaded is Charles's translation of the Ethiopic alone.

## Credit

R. H. Charles (trans.), in R. H. Charles (ed.), *The Apocrypha and Pseudepigrapha of the Old Testament in English*,
vol. II (Oxford: Clarendon Press, 1913); scanned by Joshua Williams, Northwest Nazarene College (1995); Enoch
corrected by Marjan Šavli and Barbara Hojkar Šavli (2023); CrossWire Bible Society's SWORD modules `Enoch` 2.0 and
`Jubilees` 1.0.

## Other sources read and not taken

- **Project Gutenberg ebook 77935**, *The Book of Enoch* (SPCK, 1917, tenth impression 1962), the same 1912
  translation with W. O. E. Oesterley's introduction, in Project Gutenberg's licence. Clean and proofread, and kept
  in mind as the text to check Enoch's words against; not the source, so that both books come from one edition
  through one reader. gutenberg.org was down for maintenance on 2026-11-16; its mirror `gutenberg.pglaf.org` served
  `77935-0.txt` (SHA-256 `54ef78037e6ff4fb9831a5278c8c0e0d77622e4edc5325a080808efa66605c37`).
- **Wikisource** holds Enoch without page scans, copied from sacred-texts.com, and no transcription of Jubilees;
  the scanned volume (`Index:Apocrypha-and-Pseudepigrapha-Charles-B.djvu`) is not transcribed.
- **sacred-texts.com** refuses programs (a Cloudflare block), which is a refusal, not a hurdle.
- **The King James 2 Esdras** is not fetched again: the corpus serves it already in the KJV (eBible's King James
  with Apocrypha), and the gloss packet reads it from there.
