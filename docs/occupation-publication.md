# Occupations

What the text says people did or were — a trade, an office, a military, religious or heavenly role, or a
designation the text gives them (*the harlot*, *a thief*) — is held in four tables: `occupation` (with its
`strong_numbers`), `occupation_label` (four languages, a masculine and a feminine form), `person_occupation`
(who decided it, in `source`, never served) and `occupation_witness` (the quotations). `occupation_word` holds
the words that name an occupation in the verses that say it.

## The file and the load step

`Resources/Essenthos/person-occupations.json` is the reviewed inventory, in git. The load step `occupations`
(`core-load --set only=occupations`, or the Forge verb `occupations`) reads it; a second load writes nothing.

```json
{
  "version": 1,
  "source": "who read and reviewed the file",
  "occupations": [{
    "slug": "physician",
    "category": "trade",
    "strongNumbers": ["G2395"],
    "labels": {
      "eng": {"name": "Physician", "masculine": "Physician", "feminine": "Physician"},
      "ukr": {"name": "Лікар", "masculine": "Лікар", "feminine": "Лікарка"},
      "deu": {"name": "Arzt", "masculine": "Arzt", "feminine": "Ärztin"},
      "spa": {"name": "Médico", "masculine": "Médico", "feminine": "Médica"}
    }
  }],
  "assertions": [{
    "person": "luke",
    "occupation": "physician",
    "source": "optional: who decided this one, where it is not the file's",
    "witnesses": [
      {"kind": "profession", "text": "NESTLE1904", "reference": "COL 4:14", "quote": "Λουκᾶς ὁ ἰατρὸς ὁ ἀγαπητὸς"},
      {"kind": "profession", "text": "KJV", "reference": "COL 4:14", "quote": "Luke, the beloved physician"}
    ]
  }]
}
```

- `reference` is the verse as that text numbers it (BHSA's `NEH 10:3` is the English 10:2). The step finds the
  verse in the corpus and requires the quotation in it letter for letter, as the corpus spells it (BHSA is not
  NFC). A quotation that is not there stops the whole load and names it. The canonical address is the one the
  corpus gives the verse.
- A person is named by slug; a folded record's old slug reaches the kept one through `merged_record`. A slug
  the encyclopedia does not hold stops the load.
- Categories: `trade`, `office`, `military_role`, `religious_role`, `heavenly_role`, `designation`.
  Heavenly roles are the owner's ruling of 2026-10-27 (TSK-1254); designations the text states of a person
  (Rahab the harlot, Judas a thief and the traitor, Elymas the sorcerer) his ruling of 2026-10-28 (TSK-1218).
- `strongNumbers` are the numbers of the words that name the occupation in the originals. Empty where the text
  says it with no word of its own (*over the vineyards*); nothing is marked then.

## What the line says

A person's trades and designations are said in their line after the offices (`OccupationLines`), each a link to
the occupation's page, cited by the first verse saying it. Offices and other roles stay in the person's section.

## The words marked

`occupation_word` is written by the same step: in each profession quotation the words carrying one of the
occupation's numbers; then, in every other text, a word of a Renders or Equals link with such a word that carries
the number itself, or is the link's only word on its side, or (of several) is among the three commonest
renderings of the number in its text. A pronoun is never marked; a word occurring 300 times or more in its text
only where it is among those renderings ten times or more. `/v1/text`, `/v1/parallel` send them as a word's
`roles`; `/v1/verses?entity=<person>&occupation=<slug>` marks them in a verse list.

The admitted generation folders under `Resources/Essenthos/generation/occupation-*` are the record of how the
file was made (Claude Sonnet 5.5 readings, Codex's source review, TSK-1250/1251/1254); requests never read them.
