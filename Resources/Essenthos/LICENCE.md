# Resources/Essenthos

Everything under this folder was made by this project. It is the one folder under `Resources/` that
is not somebody else's data, which is why it is the one folder the repository carries in full.

## descriptors/

The claims the encyclopedia says under a name — *son of Reuel*, *Moses' father-in-law*, *нащадки
Моава* — as structure rather than prose, with the verse each was read from and the name forms each
language needs. One newline-delimited JSON file per batch, in the shape DOC-0191 fixes.

- **Made by** Essenthos, by `scripts/descriptors.py` against the `essenthos_core` database. Each
  object carries the model that produced it and the date it was asked, so the whole pass is
  identifiable and removable.
- **Read from** the King James Version (public domain) of the verses this corpus already attests the
  entity in, with the Ohienko and Synodal texts shown for the name forms. No third-party
  description was shown to the model and none is reproduced here: replacing BibleData's imported
  English sentence is the entire point of the pass (NOT-0171).
- **Measured against** BibleData's `entity_relationship` rows, which are the answer key and not the
  source. That dataset is CC BY 4.0 and is attributed where it is loaded; nothing of it is copied
  into these files.
- **Terms.** Ours, under the project's own licence. Facts about who a person's father was are not
  anyone's property; the wording, the structure and the readings here are this project's.

## name-forms/

The same names in the cases a rendered line puts them in — *Мойсея*, *Левитів*, *Moses*,
*Moisés* — for the entities somebody else's clause names rather than for the entity a pass
described. Newline-delimited JSON, one object per entity, read by `EntityNameFormLoader`.

- **Made by** Essenthos, by `scripts/name-forms.py` against the `essenthos_core` database. Each
  object carries the model that produced it and the date it was asked, so the whole pass is
  identifiable and removable, and a deterministic check refuses a form that is not one before it is
  written.
- **Read from** the King James Version (public domain), the Ohienko Ukrainian, the Luther 1912
  German and the Reina-Valera 1909 Spanish — each attributed where it is loaded — so the spelling
  asked for is the spelling that language's own Bible uses rather than a transliteration. Only the
  verses this corpus already attests the entity in were shown, and no verse text is reproduced here.
- **Terms.** Ours, under the project's own licence. How a name declines in a language is not
  anyone's property; the selection, the check and the readings here are this project's.

## places/

Which Strong numbers name a place, and why — one line per lexicon entry the pass considered, the
refusals with the records, because what a register is asked next is why something is not in it.
Newline-delimited JSON, read by `PlaceRegisterLoader`.

- **Made by** Essenthos, by `scripts/places.py` against the `essenthos_core` database. Each line
  carries the tier the free pass put the entry in, the name type BHSA marks on the word, and the
  readings — each with the model that produced it and the date it was asked, so the whole pass is
  identifiable and removable.
- **Read from** Strong's Dictionary, whose entries are public domain and are quoted here in the
  `definition` field for exactly that reason: what established a record has to be readable beside
  it. The name types are BHSA's, which is attributed where it is loaded, and no annotation of it is
  reproduced — only which of four verdicts it amounts to for a lexeme.
- **Not read from** OpenBible.info. The register is built without the gazetteer and then meets it;
  the link between the two is established by matching the names, is recorded on the entity it
  reaches, and OpenBible keeps the credit for what it supplies, which is the coordinates.
- **Terms.** Ours, under the project's own licence. That a nineteenth-century dictionary heads a
  place name is not anyone's property; the net, the classification, the readings and the decision
  here are this project's.
