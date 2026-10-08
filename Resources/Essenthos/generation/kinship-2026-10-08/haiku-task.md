You are checking the family relationships a Bible research site has recorded from one genealogical chapter. 

You get the chapter's King James and original-language text verse by verse, the records of every person the site has placed in it (slug, name, distinguisher, sex), every person-to-person relationship row read from a verse of this chapter (id, "from type to", verse, who recorded it), and the rows between these same people that were read from other chapters (recorded_from_other_chapters; not yours to judge, but they count as recorded). Direction: "isaac son-of abraham".

Read the original language first, then say:
- wrong: rows this chapter contradicts. kind "delete" (the chapter does not say it; NOT because the same relation is also recorded the other way round — the site records each relation from both sides on purpose, e.g. "a father-of b" with "b son-of a", "a brother-of b" with "b brother-of a", a husband-of with its wife-of, and an ancestor-of with its descendant-of: those pairs are by design and never wrong for that reason), "retype" (new_type = the right type), "retarget" (new_to = the slug of the person the verse means, one of the records given — typically a namesake picked wrongly). Not wrong: a relation another book states differently (that is a variant, kept); "son of" skipping generations when the text itself says "son" (keep it unless the chapter itself names the generation between).
- missing: family statements the chapter makes plainly (X begat Y, the sons of X: Y, Y his son, X's wife Z, X's brother Y) between two people who are both among the records given, and that no row records (here or in recorded_from_other_chapters) in either direction or with an equivalent type (father-of = son-of reversed). Only plain statements, not inferences; give the verse.
- Also not wrong: a row that is true but stated more loosely than another (descendant-of beside son-of). Judge only whether the chapter supports what the row says, and whether it names the right person.
- A row recorded "by the project owner" is his ruling: never list it as wrong; if it looks wrong, say so in note.
- Types: father-of, mother-of, son-of, daughter-of, brother-of, sister-of, half-brother-of, half-sister-of, husband-of, wife-of, concubine-of, grandfather-of, grandson-of, descendant-of, ancestor-of, father-in-law-of, son-in-law-of, brother-in-law-of, adoptive-mother-of. Slugs exactly as given; verses as "BOOK c:v" within this chapter.
- Each item: why = one sentence from the text; confidence 0..1. Prefer missing nothing to inventing something.
- note: one or two sentences, including any owner row that looks wrong.


## Output

Write one JSON file per chapter with the Write tool, exactly this shape (fill every field; "" or [] where a kind does not use one):

{"wrong": [{"kind": "delete|retype|retarget", "rows": [<row ids>], "new_type": "", "new_to": "", "verse": "BOOK c:v", "why": "...", "confidence": 0.0}],
 "missing": [{"from": "<slug>", "type": "<type>", "to": "<slug>", "verse": "BOOK c:v", "why": "...", "confidence": 0.0}],
 "note": "...", "model": "<your exact model id>"}
