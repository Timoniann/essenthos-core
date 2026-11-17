# The Ge'ez layers a language model made -- what they are and whose

**These files are not Dillmann and not the Ethiopic Bible. They are one model's reading, made on a date,
and they say so on every row.**

## What is here

- `dillmann-renderings` -- each sense of August Dillmann's *Lexicon Linguae Aethiopicae* (Leipzig 1865)
  rendered from his Latin into English, Ukrainian, German and Spanish, sense by sense in his order and
  numbering. Only Dillmann's own senses: the TraCES project's additions inside the digital edition (an
  `xml:id` starting with T, among them Leslau's English) were never given to the model.
- `glosses/<book>` -- each word of the Ethiopic Bible (GEEZ81) as the model read it in its verse, with the
  verse's Greek (Brenton's Septuagint, Nestle 1904 for the New Testament) and an English translation beside
  it: its dictionary form (Dillmann's headword where one fits), its sense there, the whole word as a line
  under it in English and Ukrainian, the Greek words it renders, and how sure the model was.

## The sources underneath

- Dillmann's book is in the public domain. The digital edition it is read from is not: **Beta maṣāḥǝft,
  DillmannData**, Hiob-Ludolf-Zentrum für Äthiopistik, Universität Hamburg, under **CC BY-NC-SA 4.0** (see
  `Resources/Dillmann/LICENCE.md`). Both layers are made from it -- the renderings translate its Latin, the
  glosses name their dictionary forms by its headwords -- so its ShareAlike reaches them.
- The Ge'ez text is the Ethiopic Bible as GEEZ81 holds it (`Resources/Essenthos/build/licences.tsv`); the
  Greek and English the model read beside it are public domain.

## These files

- **Produced by:** the Essenthos project, `scripts/geez-batch` (the runs) and `forge geez-layers` (taken
  into the store from a run's checked lines).
- **Method:** a language model through the Claude API, in batches; every answer checked by the run's rules
  (every word answered once, Greek only from the verse, no Latin letters inside a Ukrainian word) and a
  sample read by hand. Nobody read every row.
- **Model, run, prompt and day:** on every row and in each file's header.
- **Licence:** **CC BY-NC-SA 4.0** -- attribution, non-commercial use only, share alike. Credit: Dillmann,
  Beta maṣāḥǝft, and "machine rendering / reading by a language model for Essenthos".

## What a reader is owed

To be told it is a machine's: a rendering shown under Dillmann's Latin, never in its place, and a reading in
context marked as a model's wherever it stands.
