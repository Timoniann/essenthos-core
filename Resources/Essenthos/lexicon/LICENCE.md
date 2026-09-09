# Strong's dictionary in Ukrainian -- a machine translation, and whose is which

**This is not Strong's dictionary in Ukrainian. It is one model's reading of Strong's
dictionary, rendered into Ukrainian on a date, and it says so on every row.**

## The English underneath

*A Concise Dictionary of the Words in the Hebrew Bible* and *...in the Greek New Testament*, by
**James Strong, LL.D., S.T.D.**, Hunt & Eaton, 1890. **Public Domain**, declared in the OSIS header
of the files this was made from -- see `Resources/Strong/LICENCE.md`, which also records the one
part of those files that is *not* public domain and is not translated here: the TWOT reference on
6,070 Hebrew entries, under a 1980 Moody Bible Institute copyright. The TWOT reference is an
identifier and this file does not carry it.

Because Strong's own text is public domain, translating it needs nobody's permission. That is a
statement about the English and not about this file.

## This file

- **Produced by:** the Essenthos project, `scripts/lexicon.py`.
- **Method:** `model-translation` -- a language model, given the English fields and a glossary of
  Strong's fixed qualifiers, asked for Ukrainian.
- **Model, prompt version and date:** on every row, in `model`, `prompt_version` and
  `translated_at`. They are not metadata about the file; they are part of the claim.
- **Not translated, because they are identifiers and not language:** the lemma, the transliteration,
  the pronunciation, the morphology code, the see-also numbers and the TWOT reference. A translated
  identifier breaks a lookup and nothing says it has.
- **Rendered rather than translated:** `kjv_definition`. The English is the list of words the King
  James actually uses, in Strong's affix notation; what is here is the senses those renderings
  carry. It does not point into the King James text and the English must be shown beside it.

## What a reader is owed

The English, next to it. A Ukrainian gloss standing alone reads as Strong saying it, and
Strong did not say it -- a machine did, once, and nobody has checked that row.
