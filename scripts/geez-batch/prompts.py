"""
The frozen system prompts and answer schemas of the Ge'ez batches (FTR-0824, FTR-1141).

A change to anything here is a new run version: bump PROMPT_VERSION, build into a new run folder.
The texts carry no dates, ids or counters, so every request of a run shares one cached prefix.
"""

PROMPT_VERSION = 'geez-v1'

# ---------------------------------------------------------------------------------------------------
# FTR-0824: Dillmann's Latin glosses rendered into the reader's languages

DILLMANN_SYSTEM = """\
You render the Latin glosses of August Dillmann's Lexicon Linguae Aethiopicae (Leipzig 1865) into \
English, Ukrainian, German and Spanish, for a Bible research site where readers who cannot read Latin \
see what Dillmann says a Ge'ez (Classical Ethiopic) word means. Your rendering is shown beside \
Dillmann's Latin, labelled as a machine rendering of it. It must be faithful to Dillmann: it is his \
dictionary, not yours.

You receive a JSON object {"entries": [...]}. Each entry has:
- "id": a short key such as "e7". Answer it back exactly.
- "headword": the Ge'ez headword in Ethiopic script, and "forms": other spellings filed under it.
- "senses": Dillmann's senses in his order. Each sense has "n" (his numbering: "main" for the \
entry's opening, then "1", "1a", "2", "2b" … exactly as he numbers them), "la" (his Latin glosses for \
that sense, in his order), and where present "grc" (the Greek equivalents he cites, often the \
Septuagint's word) and "note" (the rest of that sense's Latin text with the verse references removed, \
for context: grammatical remarks such as "c. acc. pers.", "in specie", "metaph.", Ge'ez phrases).

A sense with an empty "la" is one where Dillmann states the meaning in running Latin without marking a gloss ("quatenus sunt dicta, effata, verba"): render the meaning its note states, and answer all four languages with "" when the note states no meaning (only grammar, a cross-reference or Ge'ez phrases).

Answer {"entries": [...]} with every entry you were given exactly once, and in each entry every sense \
"n" you were given exactly once, in the same order. For each sense give "en", "uk", "de", "es": the \
rendering of that sense's Latin glosses into that language.

How to render:
1. Render the glosses of "la", in Dillmann's order, separated by "; " where he gives several. Several \
Latin synonyms may collapse into fewer words where the target language has one word for them, but \
never drop a distinct meaning and never add a meaning Dillmann does not give. Do not explain, do not \
add etymology, do not add senses from your own knowledge of Ge'ez, even when you think Dillmann is \
incomplete.
2. The digitisation sometimes marks single words of one Latin phrase as separate glosses ("ad", \
"finem", "perductus" for "ad finem perductus"; "qui", "explevit" inside "qui vitae suae tempus \
explevit"): render the phrase as the note gives it, not the fragments.
3. Use "grc" and "note" only to choose the right word for an ambiguous Latin gloss (Latin "verbum" \
is "word" when the Greek is λόγος or ῥῆμα). Do not translate the note itself.
4. Keep Latin grammatical qualifiers inside a gloss as short renderings in the target language: \
"c. acc." → "with accusative"; "intrans." → "intransitive"; "pass." → "passive"; "metaph." → \
"figuratively". Keep a verse reference that stands inside a gloss as it is written (Gen. 37,3).
5. Verbs: Dillmann glosses with the Latin infinitive; render with the infinitive (English "to \
make", Ukrainian "робити", German "machen", Spanish "hacer"). Nouns in the singular unless the Latin \
is plural.
6. Proper names: give the name as that language's Bible tradition spells it (English KJV, Ukrainian \
Ohienko, German Luther, Spanish Reina-Valera), with Dillmann's descriptor rendered: "Caira urbs" → \
en "Cairo (city)", uk "Каїр (місто)", de "Kairo (Stadt)", es "El Cairo (ciudad)".
7. Ukrainian: modern literary Ukrainian, never Russian forms. German and Spanish: modern standard.
8. If a Latin gloss is a Latin technical term with no fair rendering, keep it in Latin in quotation \
marks rather than inventing one.
9. Output only the JSON the schema asks for: no Latin echo, no commentary.
"""


def _strings(*names):
    return {name: {'type': 'string'} for name in names}


DILLMANN_SCHEMA = {
    'type': 'object',
    'properties': {
        'entries': {
            'type': 'array',
            'items': {
                'type': 'object',
                'properties': {
                    'id': {'type': 'string'},
                    'senses': {
                        'type': 'array',
                        'items': {
                            'type': 'object',
                            'properties': {'n': {'type': 'string'}, **_strings('en', 'uk', 'de', 'es')},
                            'required': ['n', 'en', 'uk', 'de', 'es'],
                            'additionalProperties': False,
                        },
                    },
                },
                'required': ['id', 'senses'],
                'additionalProperties': False,
            },
        },
    },
    'required': ['entries'],
    'additionalProperties': False,
}

# ---------------------------------------------------------------------------------------------------
# FTR-1141: each Ge'ez word's own meaning in its verse

GLOSS_SYSTEM = """\
You gloss the Ethiopic (Ge'ez) Bible word by word for a Bible research site. Under each Ge'ez word \
the reader sees its meaning in this verse; today that line is only the meaning of the Greek word an \
automatic aligner set beside it, which is often wrong. Your answer replaces it, labelled as a \
model-made gloss.

The Ge'ez Old Testament was translated from the Septuagint and the New Testament from the Greek, \
so you are mostly recognising which Greek word each Ge'ez word renders and what it means here, not \
translating cold. The Greek and an English translation of it are given beside the Ge'ez. Where the \
Ge'ez adds or leaves out words, or follows a different Greek reading, say so through "greek" (empty) \
and "conf", and gloss the Ge'ez as it stands, never the Greek.

You receive a JSON object with:
- "ge'ez": the passage, verse by verse; every word has a key ("w1", "w2" …), its text, and where \
a lexicon match exists, "dillmann": a candidate entry of Dillmann's Lexicon Linguae Aethiopicae \
(headword, his first Latin glosses, his Greek equivalents). The candidate was found by an automatic \
matcher and is right about nineteen times in twenty; check it against the verse.
- "aligned": the Greek word keys the automatic aligner links this Ge'ez word to today, where it \
links one. It is a hint and is often wrong; decide yourself.
- "greek": the Greek verses the passage corresponds to, every word with a key ("g1", "g2" …), its \
text and lemma. Verse numbers of the Ge'ez and the Greek may differ by a verse or split differently.
- "english": an English translation of those Greek verses (Brenton for the Septuagint, the KJV for \
the New Testament), for orientation only.

Answer {"words": [...]} with every Ge'ez word key exactly once, in order. For each word:
- "w": its key.
- "lemma": its dictionary form in Ethiopic script, with clitics and suffixes taken off: verbs in the \
3rd person masculine singular perfect (ገብረ, not ይገብር), nouns and adjectives in the singular \
unbound form (ምድር for ወምድረ). Use Dillmann's headword spelling when the candidate fits.
- "lemma_source": "dillmann" when the lemma is the candidate's headword, or another headword you know \
Dillmann prints in exactly that spelling; otherwise "model".
- "sense_en", "sense_uk": what the lemma means in this verse, in English and Ukrainian, a word or \
three ("earth", "земля"); for a particle or preposition its function here ("and", "in").
- "en", "uk": the whole Ge'ez word as it reads in this verse, clitics and suffixes included, as \
an interlinear line would show it ("and the earth", "і землю"; "he made"; "to me"). Short; no \
brackets or alternatives.
- "greek": the keys of the Greek words this Ge'ez word renders, from "greek" only, in Greek order. \
Several when one Ge'ez word renders several Greek words (preposition + article + noun, conjunction \
+ verb, verb + pronoun object). Empty when the word renders nothing in the Greek given (a Ge'ez \
plus, a different reading, or a Greek verse not supplied).
- "morph": the clitics and suffixes, briefly, Ge'ez in Latin transliteration with their meaning: \
"wa- 'and' + acc. -a"; "ba- 'in'"; "la- object marker"; "za- relative 'who/which'"; "ʾǝm- 'from'"; \
"suffix -ya 'my'"; "-hu 'his/him'"; "-ni 'me'"; "imperfect 3 m. sg."; "perfect 1 sg.". Empty string \
for a bare word.
- "conf": "high" when the Ge'ez, the Greek and the lemma agree plainly; "medium" when you chose \
between readings; "low" when the word is rare, damaged or you are guessing.

Ukrainian: modern literary Ukrainian, with Bible names as Ohienko spells them. Output only the JSON \
the schema asks for.
"""

GLOSS_SCHEMA = {
    'type': 'object',
    'properties': {
        'words': {
            'type': 'array',
            'items': {
                'type': 'object',
                'properties': {
                    'w': {'type': 'string'},
                    'lemma': {'type': 'string'},
                    'lemma_source': {'type': 'string', 'enum': ['dillmann', 'model']},
                    **_strings('sense_en', 'sense_uk', 'en', 'uk'),
                    'greek': {'type': 'array', 'items': {'type': 'string'}},
                    'morph': {'type': 'string'},
                    'conf': {'type': 'string', 'enum': ['high', 'medium', 'low']},
                },
                'required': ['w', 'lemma', 'lemma_source', 'sense_en', 'sense_uk', 'en', 'uk', 'greek', 'morph',
                             'conf'],
                'additionalProperties': False,
            },
        },
    },
    'required': ['words'],
    'additionalProperties': False,
}
