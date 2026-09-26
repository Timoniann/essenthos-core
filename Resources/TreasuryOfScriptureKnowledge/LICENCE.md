# TSK — the Treasury of Scripture Knowledge

**The Treasury of Scripture Knowledge**: "five-hundred thousand scripture references and parallel
passages by Canne, Browne, Blayney, Scott, and others about 1880", as the module describes itself.
Published by Samuel Bagster and Sons in the 1830s and reissued with R. A. Torrey's introduction in
the 1880s; every reference is filed under the word of its verse it bears on.

From CrossWire's SWORD packages, read at the source on **2026-09-27**:
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/TSK.zip` (SHA-256
`6784c7099465995a8e66f02ead82b0bca66603c1bdeaf8332949774b7bfd4293`), module version 1.4
("minor corrections to scripture references"). `scripts/fetch-tsk.ps1` fetches it, checks that hash
and re-reads the configuration's licence line before it replaces anything.

## What it is used under: Public Domain

## Every statement attached to these bytes

| Where | What it says |
|---|---|
| `mods.d/tsk.conf`, the module's own configuration | `DistributionLicense=Public Domain` |
| CrossWire's module page, <https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=TSK> | "Distribution License: Public Domain" |

The work itself is out of copyright by age: its compilers died in the eighteenth and nineteenth
centuries and it was printed before 1900. The module carries no transcriber's claim, and none of
the statements above adds a condition. It is credited anyway (RUL-0181).

## What the corpus does with it

The module stores, for each verse, the catchwords of the verse — *beginning*, *God*, *created* — each
followed by the references filed under it, as ThML `scripRef` elements in the abbreviations the
Treasury prints (`Pr 8:22-24; 16:4; Mr 13:19`). A reference without a book continues the book and
chapter before it, as the printed page does. The chapter summaries that open each chapter's first
verse (`1 God creates heaven and earth`) point into the chapter itself and are not references.

The numbering is the King James's, which is the shared frame's; the module names no versification
and SWORD's default for that is KJV.

## Attribution as shown to readers

> The Treasury of Scripture Knowledge (about 1880), public domain, from CrossWire's SWORD module.
