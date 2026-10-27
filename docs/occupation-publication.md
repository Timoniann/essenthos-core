# Occupation publications

Occupation concepts, four language labels, person assertions and their witnesses are held in dedicated tables. They do not change titles, descriptors or personal names. Request handlers read only these tables; generation folders are never request inputs.

Admission is an explicit Forge operation: `occupations <publication.json> <expected-sha256>`. It reads exactly one file, checks its UTF-8 byte SHA-256 against the independently supplied hash, validates the entire inventory and resolves person slugs before writing. A named source reviewer must have set `review.status` to `reviewed`; a model's acceptance is not publication review. This status can record an agent's source review for a private preview and does not imply human acceptance or authorization to apply it live.

```json
{
  "schemaVersion": 1,
  "publicationId": "reviewed-occupations-v1",
  "review": {
    "status": "reviewed",
    "reviewedBy": "named source reviewer",
    "reviewedAt": "2026-10-27T10:00:00Z"
  },
  "source": {
    "attribution": "source attribution",
    "license": "source licence",
    "uri": "immutable source location",
    "sha256": "64 hexadecimal characters binding the reviewed source inventory"
  },
  "occupations": [{
    "slug": "physician",
    "category": "trade",
    "labels": {
      "eng": {"name": "Physician", "masculine": "physician", "feminine": "physician", "description": null},
      "ukr": {"name": "Лікар", "masculine": "лікар", "feminine": "лікарка", "description": null},
      "deu": {"name": "Arzt", "masculine": "Arzt", "feminine": "Ärztin", "description": null},
      "spa": {"name": "Médico", "masculine": "médico", "feminine": "médica", "description": null}
    }
  }],
  "assertions": [{
    "personSlug": "luke",
    "occupationSlug": "physician",
    "witnesses": [{
      "kind": "profession",
      "text": "KJV",
      "reference": "COL 4:14",
      "canonicalReference": "COL 4:14",
      "quote": "beloved physician",
      "sourceText": "Luke, the beloved physician, and Demas, greet you.",
      "sourceSha256": "SHA-256 of the exact UTF-8 sourceText"
    }],
    "origin": {"model": "provider model", "readingRun": "frozen reading run", "jobId": "original job", "draftMessageId": "draft", "checkMessageId": "check", "reviewedBy": "source reviewer"}
  }]
}
```

Categories are `trade`, `office`, `military_role`, `religious_role`. Every concept has all four labels. Every assertion has a profession witness; separate identity witnesses may establish which person a profession verse concerns. Both printed-edition and canonical single-verse addresses are retained, and each exact quote must occur in its hash-verified source verse. The loader validates syntax and byte provenance; the independent reviewer establishes source identity, verse mapping and semantic support. Working notes and origin metadata are stored privately and never served as reader copy.

Repeated assertions for one person and occupation collapse into one row with distinct combined witnesses. Replacement removes only assertions owned by the same publication ID and commits labels, assertions and witnesses together. It refuses occupation pairs owned by another publication. An identical pinned publication writes nothing and preserves row IDs. Previously published occupations with no remaining assertions are absent from the catalogue. Admission is deliberately separate from the default corpus load and must be repeated explicitly when provisioning a new corpus; raw generated results are never admitted automatically.

The three read routes are `/v1/occupations`, `/v1/occupations/{slug}` and `/v1/entities/{slug}/occupations`. Catalogue and practitioners paging clamps `skip` to zero and `take` to 1–100 (default 40). Search accepts up to 120 characters and matches the selected prose language's name and gendered forms. Catalogue categories reflect the search before the selected category filter, with distinct people across occupations. Occupation words follow `prose`, person names follow `language`. Canonical reference responses use the existing `{bookOrdinal,book,slug,chapter,verse}` shape. Assertion `verses` contain profession references; `witnesses` retain both profession and identity evidence.

Private previews use `occupation-preview <publication.json> <expected-sha256> <people.json>`. The helper refuses all databases except a local server on 5439 with a database name starting `essenthos_occupation_preview`. It creates/migrates that disposable database, seeds only the supplied persons, name forms and exact source verse fixtures, then calls the same publication loader. The people sidecar is `{ "people": [{ "slug": "luke", "name": "Luke", "sex": "male", "localNames": {"ukr":"Лука"} }] }`. This helper neither reads the live corpus nor changes source files, recipes, model checkpoints or owner services. It is a preview fixture rather than a corpus completeness assertion.
