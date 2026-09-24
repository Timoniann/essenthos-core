# KorRV — 성경전서 개역한글판, the Korean Revised Version of 1961

**개역한글**, the Korean Bible Society's 1961 revision of its 개역 of 1938, and the Korean
Protestant Bible until the 개역개정 of 1998 replaced it. Loaded as `KRV`.

From CrossWire's SWORD package, read at the source on **2026-09-25**:
`https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/KorRV.zip` (SHA-256
`194dd52c…4ecd7`), module version 2.0.1 of 2019-01-07, whose history says it was switched to the
Korean Wikisource text in 2013. `scripts/fetch-crosswire.ps1` fetches it, checks the hash and
re-reads the Bible Society's statement before it replaces anything.

**66 books, 31,083 verses, 464,079 words.** Nineteen verses of the King James's numbering have no
text of their own, and nothing is supplied for them:

- eleven the critical Greek lacks — Matthew 18:11; Mark 9:44, 9:46, 11:26, 15:28; Luke 17:36,
  23:17; Acts 8:37, 15:34, 28:29; Romans 16:24;
- seven where the Korean sentence runs two verses into one and the module leaves the second empty —
  Psalm 72:20, Isaiah 30:2 and 48:2, Jeremiah 21:2, Ezekiel 24:5, Acts 15:26, Romans 9:2;
- 2 Corinthians 13:12. The Korean prints that chapter in thirteen verses, the holy kiss inside
  verse 11, and the module keeps its numbers in a fourteen-verse chapter. So its 13:12 (*all the
  saints salute you*) and 13:13 (the benediction) are loaded at the King James's 13:13 and 13:14,
  each recording the edition's own number.

The module numbers by SWORD's NRSV scheme; 3 John 1:15 is joined to 1:14 as the King James prints
it, and the verse records both numbers.

## Which source, and why this one

DOC-0207 names two places for this text: the Korean Bible Society's own reader, which offers no
download and refused a fetch, and CrossWire's `KorRV`, taken from Korean Wikisource. eBible's `kor`
is not this text — it is the Korean Bible of 1910 (the correction on DOC-0110). So `KorRV` is the one
digital copy with a stated source that needs nobody contacted.

## The licence, in three statements that agree

**1. The Korean Bible Society**, copyright FAQ, 『성경전서 개역한글판』은 저작권 사용허가없이 사용
가능한가요?, posted 2017-10-27, read 2026-09-25
(https://www.bskorea.or.kr/bbs/board.php?bo_table=copyright_faq&wr_id=5; a copy is kept here as
`kbs-copyright-faq.html`):

> 『성경전서 개역한글판』의 저작재산권 보호기간은 50년이 경과되어 저작권료 지급없이 사용
> 가능합니다.(2013년 이후부터 70년 존속) 다만, '동일성유지권'과 '성명표시권'의 인격저작권을
> 준수하셔서 사용하셔야 합니다.
>
> (The economic rights in the 개역한글판 have passed their fifty-year term, so it may be used without
> paying a royalty (the term became seventy years from 2013). But the moral rights — the right of
> integrity and the right of attribution — must be respected.)

**2. Korean Wikisource**, page 개역한글판: author 대한성서공회, *"2012년 저작권이 만료되었다"*
(its copyright expired in 2012), licence template `PD-old-60`.

**3. CrossWire's configuration**: `DistributionLicense=Public Domain`, `TextSource=Wikisource`.

## What the moral rights require here

- **Attribution** (성명표시권): the Korean Bible Society is named as the translator on the text's
  row and in its citation.
- **Integrity** (동일성유지권 — no alteration of content, form or title): the words are loaded as
  printed. A Korean word is an eojeol, the space-separated unit with its particles attached, and it
  is not divided into stem and particle, corrected or re-spelled. Every verse reads back character
  for character as the module prints it (`SwordTextTests`). The text is served under its own title,
  성경전서 개역한글판.

## Attribution

The translation and revision are the **Korean Bible Society (대한성서공회)**'s, 1961. The digital text is
**Korean Wikisource**'s transcription, as packaged by **CrossWire Bible Society**.

Cite as: *성경전서 개역한글판, 대한성서공회 1961, in the transcription of Korean Wikisource as
CrossWire publishes it in the SWORD module KorRV.*
