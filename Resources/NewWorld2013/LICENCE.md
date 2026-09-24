# nwt_E.epub — New World Translation of the Holy Scriptures, 2013 revision (English)

**Copyrighted. Personal, local test use only. Not redistributable. Never served.**

**© 2013 Watch Tower Bible and Tract Society of Pennsylvania.** Published by Watchtower Bible and
Tract Society of New York, Inc.; translated by the New World Bible Translation Committee. The file
says so on its own publishers' page.

## Why it is here

The owner authorised it on **2026-09-24** for his **personal, local testing** of EVIDENTIA, the
aligner: a modern English translation made independently of the King James line, measured against
the Hebrew and Greek. That is the whole of the authorisation.

## Where it came from

Taken once, on 2026-09-24, from the publisher's own download service and nowhere else — no page was
scraped or crawled:

- `https://b.jw-cdn.org/apis/pub-media/GETPUBMEDIALINKS?pub=nwt&langwritten=E&fileformat=EPUB`
  names one file, *New World Translation of the Holy Scriptures (2013 Revision)*, EPUB, 15,652,378
  bytes, modified 2026-04-09 13:35:42, MD5 `62ee49a00cc9b298505ed9b224635fc5`.
  (`pub=nwtsty`, the study edition, answers 404 for EPUB; `nwt` is the plain 2013 Bible text.)
- `https://cfp2.jw-cdn.org/a/8b30f9/4/o/nwt_E.epub` is the file it names, and the copy here has that
  MD5.

`terms-of-use.html` beside it is jw.org's terms of use as served that day.

## The terms it is held under

jw.org's terms of use allow a visitor to *view, download, and print* its electronic publications
*for your own personal and non-commercial purposes*. They forbid posting them on the internet, and
distributing them with or as part of a software application — *including uploading such materials to
a server for use by a software application* — and any commercial use.

So:

- **It stays with the owner.** `Resources/*/*` keeps it out of git; only this note is committed.
  The backup action mirrors `Resources/` to his own drive, which is still his own copy; a corpus
  release is a dump of the database and the deploy copies only `Resources/Images`, so neither
  carries it. Do not copy the folder to any other machine.
- **It is never a text of the corpus.** Its definition (`NewWorldTextSource`) says
  `Redistribution.Prohibited`, and `TextDefinition.Validate` refuses to load any such text, so it
  cannot reach the database — and so no endpoint, search, concordance, entity page, console view or
  corpus release can carry it, because all of those read the database. The tests that prove it are
  in `CorpusLoaderTests` and `NewWorldTextSourceTests`.
- **The only reader is the benchmark**, `evidentia-measure-book NWT2013 … --source-from-files`,
  which reads the EPUB in its own process and writes reports to the temp folder.
- **No wording of it is committed.** The reader's tests use invented sentences in its markup.
- **The reports are private too.** A benchmark report or hand-check sample quotes verses; keep it on
  this machine.

The reader in `Essenthos.Forge/Loading/NewWorldTextSource.cs` reads a file already downloaded; it
does not touch the website. If this repository is ever published, weigh it against the terms'
clause on distributing tools that extract text from the site.

## What the reader does to it

Footnotes and their markers, the acrostic headings of Psalm 119 and the editorial line closing
Malachi are dropped; the stress marks printed inside names (Abʹsa·lom) are removed; a verse the
edition prints as a dash (Matthew 17:21, Acts 8:37 and fourteen more) is empty, and Mark 16:9-20 and
John 7:53-8:11 are not numbered at all. **66 books, 1,189 chapters, 31,062 verses.**
