# Clear-Bible/Alignments

Word alignments between Hebrew and Greek source texts and translations, made by hand.

**The repository carries no licence file.** `LICENSE`, `LICENSE.md` and `COPYING` all return 404
and the GitHub API reports `license: null`. Every statement about terms is per alignment set, in its
TOML, which is the statement closest to the bytes and therefore the one to believe (RUL-0105).

From `data/rus/alignments/RUSSYN/WLCM-RUSSYN-manual.toml`, verbatim:

    [alignment]
    identifier = "WLCM-RUSSYN-manual"
    format = "Scripture Burrito v0.3"
    copyright = "Copyright © 2024 by BiblioNexus"
    license = "CC-BY-4.0"
    process = "manual"

    [target]
    identifier = "RUSSYN"
    license = "Public domain"
    name.eng = "Russian Synodal Bible"

    [source]
    identifier = "WLC"
    copyright = "© 2023 The J. Alan Groves Center for Advanced Biblical Research"
    license = "Custom"
    licensenotes = "From http://tanach.us/License.html: 'All biblical Hebrew text, in any format,
    may be viewed or copied without restriction.'"

So: **CC BY 4.0 on the alignment**, attribution to BiblioNexus; the sources carry their own terms and
the Hebrew's is the Groves Center's custom permission. Read every set's TOML before using it — they
are not all the same, and one of them (`por`) records `process = "transfer from Spanish RVR09"`
rather than manual.

Downloaded 2026-09-03 from the `data-latest` release:
`alignments-rus.zip` (34,811,372 bytes), `alignments-eng.zip` (50,686,384 bytes).

## Before using the Russian set, read PRB-0185

Its alignment records do not correspond to the target token file shipped beside them in this
release. Measured: 12,550 of its 89,248 records name a punctuation mark as the Russian word. The
English set, checked the same way, lands on punctuation 0 times in 171,172. The data is not the
problem; that release's Russian pairing is.
