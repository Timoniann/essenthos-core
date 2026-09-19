"""
Which Jericho is this verse about? Assign every occurrence of a place name several sites bear to one
of the sites, and publish the result as a register.

The place register gave every place of the lexicon its Strong number, and where that number is borne
by two records the gazetteer puts at two sites -- Jericho at Tell es Sultan and Jericho at Tell el
Alayiq, four kilometres apart -- it was settled that the number resolves to neither. That is right,
and it leaves those pages with nothing: 87 numbers, 1,188 occurrences and, for Jericho, sixty-three
references and not one annotated word.

    python scripts/sites.py census                            # the population, no model in it
    python scripts/sites.py extract --out .sites/run          # one payload per number
    python scripts/sites.py ask    --dir .sites/run --effort medium --workers 5
    python scripts/sites.py check  --dir .sites/run --effort medium --workers 5
    python scripts/sites.py register --dir .sites/run         # the occurrences decided
    python scripts/sites.py publish  --dir .sites/run --to Resources/Essenthos/sites
    python scripts/sites.py score    --dir .sites/run         # agreement, cost, wall clock

Five things about the design are load-bearing:

**The gazetteer already answers most of it, and it is a source rather than a guess.** OpenBible does
not only place each record at a site; it says, verse by verse, which of them is named there. Over
this population that settles 1,115 of 1,188 occurrences on its own. So the model is not being asked
to do work a dataset has already done -- it is being asked the same question independently, and
where the two agree the annotation stands on two statements rather than one.

**Which is why the gazetteer's answer is kept out of the prompt.** Each candidate is shown the verses
it is attested in with every verse being asked about struck out first -- the sense pass's discipline,
and here it removes nearly the whole list, because nearly every verse of a group is being asked
about. What is left is the evidence the brief names: the King James verse and its neighbours, the
site each record sits at, this corpus's own clauses about each -- *Mizpah is a city in Gilead*,
*Mizpah 3 is near Ebenezer* -- and what else the gazetteer places in the same verse.

**A disagreement is read a second time before anything is written.** The person split's second
reading threw out nineteen bearers the first had admitted. Here the second reading is over the
occurrences where the reading and the gazetteer do not say the same thing, and over the ones the
gazetteer cannot settle at all: it is shown both answers and asked which the verse bears out, under
a prompt that may answer neither.

**Where nothing decides, nothing is written.** An occurrence the gazetteer does not place and the
readings will not settle is carried in the register as unsettled, with the reason. A page that names
forty of its sixty-three verses is worth more than one that names sixty-three with a guess in it.

**The model never touches the database.** It is given a payload and returns JSON. Every answer is
written to a file with the model, the effort level, the prompt version and the date, because it is a
claim about a reading and not a fact about the corpus.

A run leaves:

    manifest.json           every number, its candidates and what its payload holds
    batches/<number>.json   the prompt payload, exactly as the model saw it
    answers/<number>.json   the reading, with the harness's own cost and wall clock beside it
    check/<number>.json     the second reading of that number's contested occurrences
    register.json           every occurrence decided or left open, with what decided it
    register.md             what the register holds, and what it does to the corpus
    score.md                agreement, cost, wall clock, payload composition
    disagreements.json      every occurrence the reading and the gazetteer answer differently

The run directory is not committed -- it is reproducible from here, and what it is kept for is
published under `Resources/Essenthos/sites` instead.
"""

import argparse
import collections
import concurrent.futures
import datetime
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import threading
import time

sys.stdout.reconfigure(encoding='utf-8')

CONTAINER = 'essenthos-api-db-1'
DATABASE = 'essenthos_core'
USER = 'essenthos'

WITNESS = 'BHSA'
RENDERING = 'KJV'

# The Greek witnesses, which are four texts and not one. Every one of them states a Strong number on
# every word it prints, so each is read rather than reached through the others -- the same rule
# `EntityCandidates.GreekWitnesses` states on the loader's side.
GREEK_WITNESSES = ('NESTLE1904', 'RP2018', 'TR1894', 'TR1550')

PROMPT_VERSION = 'sites-2'
CHECK_VERSION = 'sites-check-1'

# The joins over `word` run out of shared memory with a gather on this machine, and the planner picks
# one for anything touching the seven million rows.
NO_GATHER = 'set max_parallel_workers_per_gather=0;\n'

# The dataset whose verse lists are the second witness. It is the gazetteer that placed these records,
# and its per-verse statement is one of the three the corpus's own place join already rests on -- but
# it is a statement about the verse and never about the word.
GAZETTEER = 'OpenBible%'

# The other dataset, read at scoring time only. It speaks about a twentieth of this population.
OTHER_DATASET = 'BibleData%'

ANSWER_UNCLEAR = 'unclear'

BOOKS = [
    'GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH',
    '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK',
    'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL',
    'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH',
    '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD',
    'REV',
]

# The gazetteer's own file, read for the two fields the corpus does not keep: the region a place
# is in -- *in Benjamin*, *of Gilead*, *in Asher* -- and the whole of its identification list rather
# than the first line of it. Both are properties of the record and neither says anything about a
# verse, so reading them here does not put the answer in the prompt.
#
# The list is what settles an alias. `OpenBiblePlaceLoader` strips the markup and keeps the words,
# so a record that is *another name for <ancient id="a6d57ed">Ramah 1</ancient>* reaches the corpus
# as prose naming a catalogue index the corpus does not hold — and a reading that knows a verse
# means Samuel's Ramah still cannot tell which of nine records that is. The id is the corpus's own
# `open_bible_id`, so read from the file it resolves exactly.
GAZETTEER_FILE = os.path.join('Resources', 'OpenBible', 'ancient.jsonl')

ANCIENT = re.compile(r'<ancient id="([^"]+)">(.*?)</ancient>', re.S)

MARKUP = re.compile(r'<[^>]+>')

# How many verses of prior attestation a candidate is shown. Almost every one is struck out as a
# verse being asked about, so the cap bites only on a record whose list reaches beyond the name --
# and a hundredth verse adds nothing the tenth did not.
ATTESTATION_SHOWN = 24

# The size bands cost is reported over, by occurrences at the number.
BANDS = [('1-4', 1, 4), ('5-9', 5, 9), ('10-19', 10, 19), ('20-49', 20, 49), ('50+', 50, None)]

SYSTEM_PROMPT = """\
You are a Biblical geographer deciding, for each occurrence of one place name, which of the places
that bear the name the verse is speaking of.

The name is borne by two or more places a gazetteer puts at two or more different sites: Jericho at
Tell es Sultan and Jericho at Tell el Alayiq are four kilometres apart, and the Mizpah of Gilead is
not the Mizpah of Benjamin. You are given the lexicon entry the name is headed under; every
candidate, with the site the gazetteer puts it at, the region it puts it in, everything else it says
the place is identified with, what kind of place it is, the clauses this corpus has read about it,
and the verses it is attested in that are not being asked about; and every occurrence of the name,
with the word marked in its own verse, the King James rendering of that verse with the verse before
and after it, and what other places the gazetteer says are named in the same verse.

Read "identifiedAs" carefully. Where it says a candidate is another name for another candidate, the
two are one place under two names and neither is a rival site to the other; pick whichever the
verse's own usage answers to. Where it names modern towns, those are where the scholarship puts the
place, in descending order of how well it is attested.

Decide each occurrence on its own evidence. What usually settles it is the neighbouring places, the
route or the boundary the verse is tracing, the tribe or the region it has just named, and what the
line before says.

"unclear" is an answer and you must use it rather than guess. Many verses genuinely do not say which
of two towns of one name is meant, and a confident wrong site is worse here than no answer, because
a reader cannot tell it from scholarship. A page that names forty of its sixty-three verses is what
this register is for.

Answer with a single JSON object and nothing else:

{
  "number":      the Strong number you were given, unchanged
  "occurrences": an array, one object per occurrence you were given, in the order given:
                 {
                   "word_id":    the integer you were given, unchanged
                   "referent":   a candidate's "key", or "unclear"
                   "confidence": "high", "medium" or "low"
                   "reason":     one line, under 25 words, saying what decided it
                 }
  "note":        one line, under 30 words, on what was hard about this name, or null
}

Every occurrence you were given must appear exactly once.
"""

CHECK_PROMPT = """\
You are reading a second time, over the occurrences of one place name where two accounts of it do
not agree, or where only one of them speaks at all.

For each occurrence you are given the verse, the candidates with the site each sits at, what a first
reading answered and why, and what a geocoding dataset's verse list says -- one of them, both of
them, or neither. The two were arrived at independently: the dataset places a name in a verse
without reading the sentence, and the reading read the sentence without seeing the dataset.

Say which of them the verse bears out, and say "unclear" wherever it bears out neither. Do not break
a tie for the sake of an answer, and do not prefer the dataset because it is a dataset: its verse
lists are known to include verses that never name the place, and it cannot see a verse that names
two of these places at once.

Answer with a JSON array, one object per occurrence, in the order given, and nothing else:

  {"word_id": 123, "referent": "...", "why": "..."}

  referent -- the key of the place the verse bears out, or "unclear" where the verse does not
              decide. It must be one of the keys you were given for that occurrence, or "unclear".
  why      -- one short clause quoting what in the verse decides it, or saying what is missing.
"""


def psql(sql):
    """One JSON value out of the live database. Read-only by construction: nothing here writes."""
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', CONTAINER,
         'psql', '-U', USER, '-d', DATABASE, '-Aqt', '-v', 'ON_ERROR_STOP=1', '-f', '-'],
        input=(NO_GATHER + sql).encode('utf-8'),
        stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if process.returncode != 0:
        raise SystemExit(
            f"the query failed against {DATABASE} in container {CONTAINER}:\n"
            f"{process.stderr.decode('utf-8', 'replace')}\n"
            f"Check that the container is running -- docker ps -- and that the SQL is valid.")
    out = process.stdout.decode('utf-8').strip()
    return json.loads(out) if out else None


def reference(book, chapter, verse):
    code = BOOKS[book - 1] if 1 <= book <= len(BOOKS) else f'book{book}'
    return f'{code} {chapter}:{verse}'


def quoted(values):
    return ', '.join("'" + str(value).replace("'", "''") + "'" for value in sorted(values))


# The site the gazetteer puts a record at, written here and in `OpenBiblePlaceLoader.Site`, because
# the harness and the loader have to be answering the same question. Three things look like a site
# and are not: silence, the catalogue entry echoed back -- Samaria 2 identified as *Samaria 2* -- and
# a note that the entry is another name for something, which claims no site of its own.
SITE = r"""
    CASE
        WHEN e.kind <> 'place' THEN NULL
        WHEN btrim(coalesce(e.modern_equivalent, e.distinguisher, '')) = '' THEN NULL
        WHEN lower(coalesce(e.modern_equivalent, e.distinguisher)) LIKE 'another name for%' THEN NULL
        WHEN regexp_replace(lower(regexp_replace(
                 coalesce(e.modern_equivalent, e.distinguisher), '\s+\d+$', '')),
                 '[^a-z0-9]', '', 'g')
             = regexp_replace(lower(e.name), '[^a-z0-9]', '', 'g') THEN NULL
        ELSE coalesce(e.modern_equivalent, e.distinguisher)
    END
"""

# Every record a Strong number names after the place register's aspect rule, and the numbers whose
# records are several places at several sites. A name row saying it bears another record's name --
# Mount Zion bearing Zion's -- is that record seen again and not a rival to it, so it is no bearer
# here either.
BEARERS = f"""
    WITH bearer AS (
        SELECT DISTINCT num.number, e.id AS entity_id, e.slug, e.kind, e.name, {SITE} AS site
        FROM entity_name n
        JOIN entity e ON e.id = n.entity_id
        CROSS JOIN LATERAL (VALUES (n.hebrew_strong_number), (n.greek_strong_number)) AS num(number)
        WHERE num.number IS NOT NULL
          AND position(',' IN num.number) = 0
          AND n.aspect_of_entity_id IS NULL
    ),
    grouped AS (
        SELECT number
        FROM bearer
        GROUP BY number
        HAVING count(*) FILTER (WHERE kind = 'place') > 1
           AND count(DISTINCT regexp_replace(lower(site), '[^a-z0-9]', '', 'g'))
               FILTER (WHERE site IS NOT NULL) > 1
    ),
    member AS (
        SELECT b.* FROM bearer b JOIN grouped g ON g.number = b.number WHERE b.kind = 'place'
    )
"""

# Every occurrence of one of those numbers, in the texts that state a number on a word. The Hebrew is
# gated on BHSA's proper-noun marking and the Greek on the lexicon's capital and the witness's own
# part of speech, which is the pair of gates `EntityAnnotationLoader` puts an annotation behind.
OCCURRENCES = f"""
    occurrence AS (
        SELECT w.id AS word_id, w.strong_number AS number, t.slug AS text, w.text AS spelling,
               w.verse_id, w.position, w.morphology->>'nameType' AS name_type,
               r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v
        FROM word w
        JOIN text t ON t.id = w.text_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        JOIN grouped g ON g.number = w.strong_number
        WHERE (t.slug = '{WITNESS}' AND w.morphology->>'nameType' IS NOT NULL)
           OR (t.slug IN ({quoted(GREEK_WITNESSES)})
               AND (w.morphology->>'pos' = 'noun' OR w.morphology->>'robinson' LIKE 'N-%')
               AND EXISTS (SELECT 1 FROM strong_entry l
                           WHERE l.strong_number = w.strong_number
                             AND lower(left(l.lemma, 1)) <> left(l.lemma, 1)))
    )
"""


def population():
    """Every number several sites bear, with its records and how many occurrences stand at it."""
    return psql(BEARERS + f""" , {OCCURRENCES}
        SELECT coalesce(json_agg(json_build_object(
                   'number', p.number, 'name', p.name, 'records', p.records, 'sites', p.sites,
                   'occurrences', p.occurrences, 'annotated', p.annotated, 'texts', p.texts)
                   ORDER BY p.number), '[]')
        FROM (
            SELECT m.number,
                   min(m.name) AS name,
                   count(*) AS records,
                   count(DISTINCT regexp_replace(lower(m.site), '[^a-z0-9]', '', 'g'))
                       FILTER (WHERE m.site IS NOT NULL) AS sites,
                   (SELECT count(*) FROM occurrence o WHERE o.number = m.number) AS occurrences,
                   (SELECT count(*) FROM occurrence o
                    WHERE o.number = m.number
                      AND EXISTS (SELECT 1 FROM word_entity we WHERE we.word_id = o.word_id))
                       AS annotated,
                   (SELECT coalesce(json_agg(DISTINCT o.text), '[]')
                    FROM occurrence o WHERE o.number = m.number) AS texts
            FROM member m GROUP BY m.number
        ) p
    """)


def candidates():
    """
    Every record of every group, as the model is shown it: the site, the kind of place, this corpus's
    own clauses about it, and the verses the gazetteer attests it in.

    The attestation is the gazetteer's alone. The corpus also derives a verse list from the words it
    annotates, and offering that back as evidence would be the pass reading its own output.
    """
    return psql(BEARERS + f"""
        SELECT coalesce(json_agg(json_build_object(
                   'number', c.number, 'entityId', c.entity_id, 'key', c.slug, 'name', c.name,
                   'site', c.site, 'placeKind', c.place_kind, 'openBibleId', c.open_bible_id,
                   'clauses', c.clauses, 'attested', c.attested) ORDER BY c.number, c.slug), '[]')
        FROM (
            SELECT m.number, m.entity_id, m.slug, m.name, m.site, e.place_kind, e.open_bible_id,
                   (SELECT coalesce(json_agg(jsonb_build_array(
                        d.relation, target.name, d.canonical_book, d.canonical_chapter,
                        d.canonical_verse, d.confidence)), '[]')
                    FROM entity_descriptor d
                    JOIN entity target ON target.id = d.target_entity_id
                    WHERE d.entity_id = m.entity_id) AS clauses,
                   (SELECT coalesce(json_agg(jsonb_build_array(
                        ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)), '[]')
                    FROM entity_verse ev
                    WHERE ev.entity_id = m.entity_id AND ev.source LIKE '{GAZETTEER}') AS attested
            FROM member m JOIN entity e ON e.id = m.entity_id
        ) c
    """)


def occurrences():
    """
    Every occurrence, with what a reader would need beside it: the word marked in its own verse, the
    King James rendering of that verse and its neighbours, and the other places the gazetteer says
    are named in the same verse.

    The neighbouring places are the evidence the brief names -- a verse that also names Gilgal and
    the Jordan is not speaking of the Jericho four kilometres the other way -- and this number's own
    candidates are struck out of that list, because for them the list is the answer.
    """
    return psql(BEARERS + f""" , {OCCURRENCES},
        rendered AS (
            SELECT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v,
                   string_agg(w.text || w.trailer, '' ORDER BY w.position) AS line
            FROM verse ve
            JOIN verse_reference r ON r.verse_id = ve.id AND r.is_primary
            JOIN word w ON w.verse_id = ve.id
            WHERE ve.text_id = (SELECT id FROM text WHERE slug = '{RENDERING}')
              AND EXISTS (SELECT 1 FROM occurrence o
                          WHERE o.b = r.canonical_book AND o.c = r.canonical_chapter
                            AND abs(o.v - r.canonical_verse) <= 1)
            GROUP BY 1, 2, 3
        )
        SELECT coalesce(json_agg(json_build_object(
                   'wordId', o.word_id, 'number', o.number, 'witness', o.text,
                   'spelling', o.spelling,
                   'nameType', o.name_type, 'book', o.b, 'chapter', o.c, 'verse', o.v,
                   'position', o.position,
                   'original', marked.line,
                   'before', (SELECT line FROM rendered x
                              WHERE x.b = o.b AND x.c = o.c AND x.v = o.v - 1),
                   'rendering', (SELECT line FROM rendered x
                                 WHERE x.b = o.b AND x.c = o.c AND x.v = o.v),
                   'after', (SELECT line FROM rendered x
                             WHERE x.b = o.b AND x.c = o.c AND x.v = o.v + 1),
                   'alsoPlacedHere', neighbours.names)
                   ORDER BY o.b, o.c, o.v, o.position), '[]')
        FROM occurrence o
        CROSS JOIN LATERAL (
            SELECT string_agg(
                       CASE WHEN w.position = o.position THEN '<<' || w.text || '>>' ELSE w.text END,
                       ' ' ORDER BY w.position) AS line
            FROM word w WHERE w.verse_id = o.verse_id) marked
        CROSS JOIN LATERAL (
            SELECT coalesce(json_agg(DISTINCT e.name), '[]') AS names
            FROM entity_verse ev
            JOIN entity e ON e.id = ev.entity_id AND e.kind = 'place'
            WHERE ev.source LIKE '{GAZETTEER}'
              AND ev.canonical_book = o.b AND ev.canonical_chapter = o.c
              AND ev.canonical_verse = o.v
              AND NOT EXISTS (SELECT 1 FROM member m
                              WHERE m.number = o.number
                                AND m.entity_id = ev.entity_id)) neighbours
    """)


def witnesses():
    """
    What each dataset's verse list says about each occurrence, and what the corpus already annotates
    the word with. Never shown to the model: it is read at scoring time, from the database, against
    answers already written.
    """
    return psql(BEARERS + f""" , {OCCURRENCES}
        SELECT coalesce(json_agg(json_build_object(
                   'wordId', o.word_id, 'gazetteer', gazetteer.keys, 'other', other.keys,
                   'annotated', annotated.keys)), '[]')
        FROM occurrence o
        CROSS JOIN LATERAL (
            SELECT coalesce(json_agg(DISTINCT m.slug), '[]') AS keys
            FROM member m
            JOIN entity_verse ev ON ev.entity_id = m.entity_id AND ev.source LIKE '{GAZETTEER}'
                 AND ev.canonical_book = o.b AND ev.canonical_chapter = o.c
                 AND ev.canonical_verse = o.v
            WHERE m.number = o.number) gazetteer
        CROSS JOIN LATERAL (
            SELECT coalesce(json_agg(DISTINCT m.slug), '[]') AS keys
            FROM member m
            JOIN entity_verse ev ON ev.entity_id = m.entity_id AND ev.source LIKE '{OTHER_DATASET}'
                 AND ev.canonical_book = o.b AND ev.canonical_chapter = o.c
                 AND ev.canonical_verse = o.v
            WHERE m.number = o.number) other
        CROSS JOIN LATERAL (
            SELECT coalesce(json_agg(DISTINCT e.slug), '[]') AS keys
            FROM word_entity we JOIN entity e ON e.id = we.entity_id
            WHERE we.word_id = o.word_id) annotated
    """)


def lexicon(numbers):
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'number', strong_number, 'lemma', lemma, 'transliteration', transliteration,
                   'definition', definition, 'derivation', derivation,
                   'detailedDefinition', detailed_definition)), '[]')
        FROM strong_entry WHERE strong_number IN ({quoted(numbers)})
    """)


def gazetteer_entries(path):
    """
    The gazetteer's own entries, keyed by the id the corpus stores as `open_bible_id`: the region it
    puts each place in, and every identification it offers rather than the first.
    """
    entries = {}
    if not os.path.exists(path):
        raise SystemExit(
            f'the gazetteer is not at {path}. It is the source the place records were made from; '
            'fetch it with scripts/fetch-openbible.ps1, or pass --gazetteer with its path.')

    with open(path, encoding='utf-8') as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            entry = json.loads(line)
            if not entry.get('id'):
                continue
            entries[entry['id']] = {
                'region': entry.get('comment'),
                'identifications': [
                    said['description'] for said in (entry.get('identifications') or [])
                    if said.get('description')],
            }
    return entries


def identified(descriptions, by_open_bible_id):
    """
    The identifications as the model is shown them: the markup off, and a reference to another entry
    replaced by that record's own key where this corpus holds it.

    That substitution is the whole point of reading the file. *Another name for Ramah 1* is a
    catalogue index and nothing a reading can act on; *another name for `ramah`* says that two of the
    candidates in front of it are one place.
    """
    said = []
    for description in descriptions:
        resolved = ANCIENT.sub(
            lambda match: by_open_bible_id.get(match.group(1)) or match.group(2), description)
        stripped = MARKUP.sub('', resolved).strip()
        if stripped:
            said.append(stripped)
    return said


def band_of(size):
    for name, low, high in BANDS:
        if size >= low and (high is None or size <= high):
            return name
    return BANDS[0][0]


def read_manifest(directory):
    with open(os.path.join(directory, 'manifest.json'), encoding='utf-8') as handle:
        return json.load(handle)


def read_json(directory, folder, label):
    path = os.path.join(directory, folder, label + '.json')
    if not os.path.exists(path):
        return None
    with open(path, encoding='utf-8') as handle:
        return json.load(handle)


def extract(args):
    """One payload per number: the lexicon entry, the candidates, and every occurrence of the name."""
    groups = population()
    entries = {entry['number']: entry for entry in lexicon(g['number'] for g in groups)}

    gazetteer = gazetteer_entries(args.gazetteer)

    held = collections.defaultdict(list)
    keys = {}
    for candidate in candidates():
        held[candidate['number']].append(candidate)
        if candidate['openBibleId']:
            keys[candidate['openBibleId']] = candidate['key']

    standing = collections.defaultdict(list)
    for occurrence in occurrences():
        standing[occurrence['number']].append(occurrence)

    os.makedirs(os.path.join(args.out, 'batches'), exist_ok=True)
    written = []
    for group in groups:
        number = group['number']
        mine = standing[number]
        if not mine:
            continue

        # The verses being asked about, struck out of every candidate's attestation. Left in, the
        # gazetteer's own answer would be in the prompt and the agreement figure would measure
        # nothing but the model's ability to copy it.
        asked = {(o['book'], o['chapter'], o['verse']) for o in mine}
        payload = {
            'prompt_version': PROMPT_VERSION,
            'number': number,
            'name': group['name'],
            'lexicon': entries.get(number),
            'candidates': [{
                'key': candidate['key'],
                'name': candidate['name'],
                'site': candidate['site'],
                'placeKind': candidate['placeKind'],
                'region': (gazetteer.get(candidate['openBibleId']) or {}).get('region'),
                'identifiedAs': identified(
                    (gazetteer.get(candidate['openBibleId']) or {}).get('identifications', []),
                    keys),
                'clauses': sorted(
                    ({'relation': relation, 'target': target,
                      'reference': reference(book, chapter, verse), 'confidence': confidence}
                     for relation, target, book, chapter, verse, confidence
                     in candidate['clauses']),
                    key=lambda clause: clause['reference']),
                'attestedElsewhere': [
                    reference(*address) for address in
                    sorted({tuple(a) for a in candidate['attested']} - asked)[:ATTESTATION_SHOWN]],
            } for candidate in held[number]],
            'occurrences': [{
                'word_id': o['wordId'],
                'reference': reference(o['book'], o['chapter'], o['verse']),
                'witness': o['witness'],
                'spelling': o['spelling'],
                'nameType': o['nameType'],
                'original': o['original'],
                'renderingBefore': o['before'],
                'rendering': o['rendering'],
                'renderingAfter': o['after'],
                'alsoPlacedHere': sorted(o['alsoPlacedHere']),
            } for o in mine],
        }
        with open(os.path.join(args.out, 'batches', number + '.json'), 'w',
                  encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)

        group['band'] = band_of(len(mine))
        group['shown'] = len(mine)
        group['candidates'] = [candidate['key'] for candidate in held[number]]
        written.append(group)

    manifest = {
        'prompt_version': PROMPT_VERSION,
        'population': {name: sum(1 for g in written if g['band'] == name) for name, _, _ in BANDS},
        'groups': [{k: g[k] for k in ('number', 'name', 'band', 'records', 'sites', 'candidates',
                                      'occurrences', 'annotated', 'shown', 'texts')}
                   for g in written],
    }
    with open(os.path.join(args.out, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    print(f'{len(written)} numbers of {len(groups)} carry occurrences, '
          f'{sum(g["shown"] for g in written):,} of them, '
          f'{sum(g["annotated"] for g in written):,} already annotated')
    print(f'{sum(len(g["candidates"]) for g in written):,} candidate records, '
          f'{len(written)} payloads written to {os.path.join(args.out, "batches")}')


def executable():
    found = shutil.which('claude')
    if found:
        return found
    fallback = os.path.expanduser(r'~\.local\bin\claude.exe')
    if os.path.exists(fallback):
        return fallback
    raise SystemExit(
        'the claude CLI is not on PATH and is not at ~/.local/bin/claude.exe. '
        'Install it, or pass its path in the CLAUDE environment variable.')


CALL_TIMEOUT = 900
CALL_ATTEMPTS = 2


def call(prompt, model, effort, system):
    """
    One number, one call, one turn, no tools and no session. `--effort` is passed always and named in
    the answer file: the CLI thinks by default, and a bill nobody chose is a bill nobody can read.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', system, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1', '--effort', effort,
    ]
    for attempt in range(CALL_ATTEMPTS):
        last = None
        try:
            process = subprocess.run(
                command, input=prompt.encode('utf-8'),
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=CALL_TIMEOUT)
        except subprocess.TimeoutExpired:
            last = f'the harness answered nothing in {CALL_TIMEOUT}s and was killed'
        else:
            if process.returncode != 0:
                said = process.stderr.decode('utf-8', 'replace').strip()
                last = said or f'the harness exited {process.returncode} and said nothing'
            else:
                try:
                    return json.loads(process.stdout.decode('utf-8', 'replace')), None
                except json.JSONDecodeError as broken:
                    last = f'the harness did not return JSON: {broken}'
        if attempt + 1 == CALL_ATTEMPTS:
            return None, last
        time.sleep(5)
    return None, 'unreachable'


OBJECT = re.compile(r'\{.*\}', re.S)
ARRAY = re.compile(r'\[.*\]', re.S)


def parse(result, shape=OBJECT, kind=dict):
    text = (result or '').strip()
    if text.startswith('```'):
        text = text.strip('`')
        text = text[text.find('\n') + 1:] if '\n' in text else text
        if text.lstrip().startswith('json'):
            text = text.lstrip()[4:]
    match = shape.search(text)
    if not match:
        return None
    try:
        answer = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    return answer if isinstance(answer, kind) else None


def ask(args):
    manifest = read_manifest(args.dir)
    answers = os.path.join(args.dir, 'answers')
    os.makedirs(answers, exist_ok=True)

    pending = [g for g in manifest['groups']
               if (not args.only or g['number'] in args.only)
               and (args.again
                    or not os.path.exists(os.path.join(answers, g['number'] + '.json')))]
    if not pending:
        print('every number already has an answer. Pass --again to run them anyway.')
        return

    lock = threading.Lock()
    totals = {'calls': 0, 'cost': 0.0, 'seconds': 0.0, 'failed': 0, 'done': 0}
    asked = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    began = time.time()

    def run(group):
        number = group['number']
        with open(os.path.join(args.dir, 'batches', number + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        offered = {o['word_id'] for o in payload['occurrences']}
        keys = {c['key'] for c in payload['candidates']}

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort, SYSTEM_PROMPT)
        elapsed = time.time() - started
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{number}: {failed}', flush=True)
            return

        answer = parse(outcome.get('result'))
        if answer is None:
            with lock:
                totals['failed'] += 1
                print(f'{number}: no JSON object in the reply', flush=True)
            return

        read, seen, invalid = [], set(), 0
        for occurrence in answer.get('occurrences') or []:
            if not isinstance(occurrence, dict):
                continue
            word = occurrence.get('word_id')
            referent = occurrence.get('referent')
            if word not in offered or word in seen:
                invalid += 1
                continue
            seen.add(word)
            if referent not in keys and referent != ANSWER_UNCLEAR:
                invalid += 1
                referent = ANSWER_UNCLEAR
            read.append({'wordId': word, 'referent': referent,
                         'confidence': occurrence.get('confidence'),
                         'reason': occurrence.get('reason')})

        usage = outcome.get('usage') or {}
        row = {
            'number': number,
            'occurrences': read,
            'note': answer.get('note'),
            'offered': len(offered),
            'answered': len(seen),
            'unclear': sum(1 for o in read if o['referent'] == ANSWER_UNCLEAR),
            'invalid': invalid,
            'prompt_version': payload['prompt_version'],
            'model': next(iter(outcome.get('modelUsage') or {'unknown': None})),
            'effort': args.effort, 'asked': asked, 'method': 'model-reading',
            'cost_usd': outcome.get('total_cost_usd') or 0.0,
            'seconds': round(elapsed, 1),
            'usage': {k: usage.get(k) for k in
                      ('input_tokens', 'output_tokens', 'cache_read_input_tokens',
                       'cache_creation_input_tokens')},
        }
        with open(os.path.join(answers, number + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(row, handle, ensure_ascii=False, indent=1)

        with lock:
            totals['calls'] += 1
            totals['done'] += 1
            totals['cost'] += row['cost_usd']
            totals['seconds'] += elapsed
            print(f'[{totals["done"]}/{len(pending)}] {number}: {len(seen)}/{len(offered)} answered, '
                  f'{row["unclear"]} unclear'
                  + (f', {invalid} invalid' if invalid else '')
                  + f', {elapsed:.0f}s, ${row["cost_usd"]:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    print(f'{totals["calls"]} calls, {totals["failed"]} failed, '
          f'${totals["cost"]:.4f} reported by the harness, {totals["seconds"]:.0f}s of model time '
          f'in {time.time() - began:.0f}s of wall clock at {args.workers} workers')


# ---------------------------------------------------------------------------- the second reading


def gazetteer_answer(said):
    """
    The gazetteer's answer for one occurrence: the record it places in that verse where it places
    exactly one of them, and nothing where it places none or several.

    Several is a real case and not a fault. A verse can name two towns of one name -- Joshua's
    boundary lists do -- and a verse list keyed to the verse cannot say which word is which, so it
    answers nothing here and the reading is on its own.
    """
    return said[0] if len(said) == 1 else None


def contested(reading, said):
    """
    Whether an occurrence goes to the second reading: the two accounts disagree, or only one of them
    speaks. An occurrence they both settle the same way needs nobody else.
    """
    answered = gazetteer_answer(said)
    if answered is None:
        return True
    return reading is None or reading == ANSWER_UNCLEAR or reading != answered


def check(args):
    """
    The second reading, over every occurrence the first reading and the gazetteer do not settle the
    same way.

    The person split read its lexicon-only bearers again under a prompt that asked the opposite
    question, and that reading threw out nineteen the first had admitted. This is the same move on a
    different axis: the first reading was made without the gazetteer, so where the two disagree there
    are two independent accounts and neither of them has seen the other. The second reading is shown
    both and may answer neither.
    """
    manifest = read_manifest(args.dir)
    said = {row['wordId']: row for row in witnesses()}

    folder = os.path.join(args.dir, 'check')
    os.makedirs(folder, exist_ok=True)

    wanted = {}
    for group in manifest['groups']:
        answer = read_json(args.dir, 'answers', group['number'])
        if not answer:
            continue
        open_ones = [o for o in answer['occurrences']
                     if contested(o['referent'], said.get(o['wordId'], {}).get('gazetteer', []))]
        if open_ones:
            wanted[group['number']] = (group, answer, open_ones)

    pending = [number for number in sorted(wanted)
               if (not args.only or number in args.only)
               and (args.again or not os.path.exists(os.path.join(folder, number + '.json')))]
    print(f'{sum(len(row[2]) for row in wanted.values())} occurrences in {len(wanted)} numbers are '
          f'settled by one account or by neither; {len(pending)} numbers to ask.')
    if not pending:
        return

    lock = threading.Lock()
    totals = {'calls': 0, 'cost': 0.0, 'seconds': 0.0, 'failed': 0, 'done': 0}
    asked = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    began = time.time()

    def run(number):
        group, answer, open_ones = wanted[number]
        with open(os.path.join(args.dir, 'batches', number + '.json'), encoding='utf-8') as handle:
            batch = json.load(handle)
        verse = {o['word_id']: o for o in batch['occurrences']}

        payload = {
            'prompt_version': CHECK_VERSION,
            'number': number,
            'name': batch['name'],
            'candidates': [{'key': c['key'], 'name': c['name'], 'site': c['site'],
                            'placeKind': c['placeKind'], 'clauses': c['clauses']}
                           for c in batch['candidates']],
            'occurrences': [{
                'word_id': o['wordId'],
                'reference': verse[o['wordId']]['reference'],
                'renderingBefore': verse[o['wordId']]['renderingBefore'],
                'rendering': verse[o['wordId']]['rendering'],
                'renderingAfter': verse[o['wordId']]['renderingAfter'],
                'alsoPlacedHere': verse[o['wordId']]['alsoPlacedHere'],
                'theReadingSays': o['referent'],
                'becauseTheReadingSays': o['reason'],
                'theDatasetPlacesHere': said.get(o['wordId'], {}).get('gazetteer', []),
            } for o in open_ones],
        }

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort, CHECK_PROMPT)
        elapsed = time.time() - started
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{number}: {failed}', flush=True)
            return

        verdicts = parse(outcome.get('result'), ARRAY, list)
        if verdicts is None:
            with lock:
                totals['failed'] += 1
                print(f'{number}: the reply held no JSON array', flush=True)
            return

        keys = {c['key'] for c in batch['candidates']}
        by_word = {v.get('word_id'): v for v in verdicts if isinstance(v, dict)}
        row = {
            'number': number,
            'verdicts': [{
                'wordId': o['wordId'],
                'referent': (lambda said_key: said_key if said_key in keys else ANSWER_UNCLEAR)(
                    (by_word.get(o['wordId']) or {}).get('referent')),
                'why': (by_word.get(o['wordId']) or {}).get('why'),
                'answered': o['wordId'] in by_word,
            } for o in open_ones],
            'prompt_version': CHECK_VERSION,
            'model': next(iter(outcome.get('modelUsage') or {'unknown': None})),
            'effort': args.effort, 'asked': asked, 'method': 'model-reading',
            'cost_usd': outcome.get('total_cost_usd') or 0.0,
            'seconds': round(elapsed, 1),
        }
        with open(os.path.join(folder, number + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(row, handle, ensure_ascii=False, indent=1)

        with lock:
            totals['calls'] += 1
            totals['done'] += 1
            totals['cost'] += row['cost_usd']
            totals['seconds'] += elapsed
            settled = sum(1 for v in row['verdicts'] if v['referent'] != ANSWER_UNCLEAR)
            print(f'[{totals["done"]}/{len(pending)}] {number}: {settled}/{len(row["verdicts"])} '
                  f'settled, {elapsed:.0f}s, ${row["cost_usd"]:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    print(f'{totals["calls"]} calls, {totals["failed"]} failed, ${totals["cost"]:.4f}, '
          f'{totals["seconds"]:.0f}s of model time in {time.time() - began:.0f}s of wall clock at '
          f'{args.workers} workers')


# ---------------------------------------------------------------------------- the register itself


# What settled an occurrence, and the difference a reader is owed.
#
# `agreed` is the gazetteer's verse list and a reading of the verse, arrived at independently, naming
# the same record. `gazetteer` is the list alone, the reading having declined. `reading` is the
# sentence alone, upheld by a second reading, where the list places nobody or places two. Nothing
# else is written, and an occurrence no account settles stays unsettled and says so.
BY_BOTH = 'agreed'
BY_THE_GAZETTEER = 'gazetteer'
BY_THE_READING = 'reading'


def decide(reading, said, verdict):
    """
    Whether one occurrence becomes an annotation, and on what it stands.

    Written as one function because every count in the report is this rule applied, and a rule stated
    in two places is a rule that disagrees with itself.
    """
    answered = gazetteer_answer(said)
    first = None if reading is None or reading['referent'] == ANSWER_UNCLEAR else reading['referent']

    if answered is not None and first == answered:
        return answered, BY_BOTH, (
            'the gazetteer places this verse at it and a reading of the verse, made without seeing '
            'that, says the same')

    if verdict is None or verdict['referent'] == ANSWER_UNCLEAR:
        if answered is not None and first is None:
            return answered, BY_THE_GAZETTEER, (
                'the gazetteer states that this verse names it, and the reading would not choose')
        if answered is None and first is None:
            return None, None, 'neither the gazetteer nor a reading of the verse names one of them'
        if answered is None:
            return None, None, (
                'the gazetteer places nobody here and a second reading would not uphold the first')
        return None, None, (
            'the gazetteer and a reading of the verse disagree and a second reading settled neither')

    if answered is not None and verdict['referent'] == answered:
        return answered, BY_THE_GAZETTEER, (
            'the gazetteer states that this verse names it, and a second reading, shown both '
            'answers, agrees: ' + (verdict['why'] or 'the verse bears it out'))

    return verdict['referent'], BY_THE_READING, (
        'a reading of the verse, upheld on a second reading: '
        + (verdict['why'] or 'the verse bears it out'))


def register(args):
    """
    The register, one line per occurrence -- the unsettled ones with the rest, because what a register
    is asked next is why a verse is not on the page.
    """
    manifest = read_manifest(args.dir)
    said = {row['wordId']: row for row in witnesses()}

    standing = collections.defaultdict(dict)
    for occurrence in occurrences():
        standing[occurrence['number']][occurrence['wordId']] = occurrence

    records, missing = [], []
    for group in manifest['groups']:
        number = group['number']
        answer = read_json(args.dir, 'answers', number)
        if not answer:
            missing.append(number)
            continue

        checked = read_json(args.dir, 'check', number) or {'verdicts': []}
        verdicts = {v['wordId']: v for v in checked['verdicts']}
        read = {o['wordId']: o for o in answer['occurrences']}

        for word, occurrence in sorted(
                standing[number].items(),
                key=lambda pair: (pair[1]['book'], pair[1]['chapter'], pair[1]['verse'],
                                  pair[1]['witness'], pair[1]['position'])):
            witness = said.get(word, {})
            reading = read.get(word)
            verdict = verdicts.get(word)
            referent, standing_on, why = decide(reading, witness.get('gazetteer', []), verdict)
            records.append({
                'number': number,
                'name': group['name'],
                'wordId': word,
                'witness': occurrence['witness'],
                'reference': reference(
                    occurrence['book'], occurrence['chapter'], occurrence['verse']),
                'spelling': occurrence['spelling'],
                'referent': referent,
                'standing': standing_on,
                'why': why,
                'candidates': group['candidates'],
                'gazetteer': witness.get('gazetteer', []),
                'otherDataset': witness.get('other', []),
                'annotated': witness.get('annotated', []),
                'reading': None if reading is None else {
                    'referent': reading['referent'],
                    'confidence': reading['confidence'],
                    'reason': reading['reason'],
                    'model': answer['model'],
                    'effort': answer['effort'],
                    'promptVersion': answer['prompt_version'],
                    'askedAt': answer['asked'],
                    'note': answer['note']},
                'check': None if verdict is None else {
                    'referent': verdict['referent'],
                    'why': verdict['why'],
                    'model': checked['model'],
                    'promptVersion': checked['prompt_version'],
                    'askedAt': checked['asked']},
            })

    with open(os.path.join(args.dir, 'register.json'), 'w', encoding='utf-8') as handle:
        json.dump(records, handle, ensure_ascii=False, indent=1)

    report = register_report(records, manifest, missing)
    with open(os.path.join(args.dir, 'register.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    print(report)


def register_report(records, manifest, missing):
    out = []
    write = out.append
    settled = [r for r in records if r['referent']]
    open_ones = [r for r in records if not r['referent']]
    fresh = [r for r in settled if not r['annotated']]

    write('# The site register\n')
    write(f'{len(settled):,} of {len(records):,} occurrences assigned to one of the places that bear '
          f'the name, over {len({r["number"] for r in records})} Strong numbers.\n')
    write('\n```')
    for standing_on, title in ((BY_BOTH, 'the gazetteer and a reading agreeing'),
                               (BY_THE_GAZETTEER, 'the gazetteer, the reading declining'),
                               (BY_THE_READING, 'a reading, upheld on a second reading')):
        write(f'  {title:<44} {sum(1 for r in settled if r["standing"] == standing_on):>6}')
    write(f'  {"unsettled, and saying so":<44} {len(open_ones):>6}')
    write('')
    write(f'  {"of the assigned, words nothing yet annotates":<44} {len(fresh):>6}')
    write(f'  {"of the assigned, words already annotated":<44} {len(settled) - len(fresh):>6}')
    write('```\n')

    if missing:
        write(f'\n{len(missing)} numbers have no answer and are unsettled throughout: '
              + ', '.join(missing[:20]) + ('...' if len(missing) > 20 else '') + '\n')

    write('\n## Why the unsettled ones are unsettled\n')
    write('```')
    for why, count in collections.Counter(r['why'] for r in open_ones).most_common():
        write(f'  {count:>4}   {why}')
    write('```\n')

    write('\n## Per number\n')
    write('| number | name | records | occurrences | assigned | unsettled |')
    write('|---|---|---:|---:|---:|---:|')
    per = collections.defaultdict(list)
    for record in records:
        per[record['number']].append(record)
    for group in manifest['groups']:
        mine = per[group['number']]
        if not mine:
            continue
        write(f'| {group["number"]} | {group["name"]} | {group["records"]} | {len(mine)} '
              f'| {sum(1 for r in mine if r["referent"])} '
              f'| {sum(1 for r in mine if not r["referent"])} |')

    write('\n## What each place gains\n')
    gained = collections.Counter(r['referent'] for r in settled if r['referent'])
    write('| place | words |')
    write('|---|---:|')
    for key, count in gained.most_common(40):
        write(f'| {key} | {count} |')
    return '\n'.join(out) + '\n'


REGISTER_BATCH = 200


def publish(args):
    """
    The register as the corpus carries it: newline-delimited JSON under this project's own folder, the
    way every other pass that cost a model run is published.
    """
    with open(os.path.join(args.dir, 'register.json'), encoding='utf-8') as handle:
        records = json.load(handle)

    os.makedirs(args.to, exist_ok=True)
    for stale in os.listdir(args.to):
        if re.fullmatch(rf'{args.prefix}-\d+\.jsonl', stale):
            os.remove(os.path.join(args.to, stale))

    written = 0
    for at in range(0, len(records), REGISTER_BATCH):
        target = os.path.join(args.to, f'{args.prefix}-{written:04d}.jsonl')
        with open(target, 'w', encoding='utf-8') as handle:
            for record in records[at:at + REGISTER_BATCH]:
                handle.write(json.dumps(record, ensure_ascii=False) + '\n')
        written += 1

    print(f'{written} files, {len(records):,} occurrences, '
          f'{sum(1 for r in records if r["referent"]):,} of them assigned -> {args.to}')


# ---------------------------------------------------------------------------- scoring the run


def sections(payload):
    """What one prompt is made of, in bytes, so the first thing to cut is measured and not guessed."""
    size = lambda value: len(json.dumps(value, ensure_ascii=False))
    return {
        'occurrences': size(payload['occurrences']),
        'candidates': size(payload['candidates']),
        'lexicon': size(payload['lexicon']),
        'name and number': size(payload['name']) + size(payload['number']),
    }


def agreement(pairs):
    decided = [p for p in pairs if p[0] is not None and p[1] is not None]
    if not decided:
        return 0, 0, 0.0
    same = sum(1 for a, b in decided if a == b)
    return same, len(decided), 100.0 * same / len(decided)


def score(args):
    manifest = read_manifest(args.dir)
    with open(os.path.join(args.dir, 'register.json'), encoding='utf-8') as handle:
        records = json.load(handle)

    rows, disagreements = [], []
    for group in manifest['groups']:
        answer = read_json(args.dir, 'answers', group['number'])
        if not answer:
            continue
        checked = read_json(args.dir, 'check', group['number'])
        mine = [r for r in records if r['number'] == group['number']]
        row = {k: group[k] for k in ('number', 'name', 'band', 'records', 'sites', 'occurrences')}
        row['assigned'] = sum(1 for r in mine if r['referent'])
        row['unsettled'] = sum(1 for r in mine if not r['referent'])
        row['unclear'] = answer['unclear']
        row['invalid'] = answer['invalid']
        row['cost'] = answer['cost_usd'] + (checked or {}).get('cost_usd', 0.0)
        row['seconds'] = answer['seconds'] + (checked or {}).get('seconds', 0.0)
        with open(os.path.join(args.dir, 'batches', group['number'] + '.json'),
                  encoding='utf-8') as handle:
            row['sections'] = sections(json.load(handle))
        usage = answer.get('usage') or {}
        row['input_tokens'] = sum(usage.get(k) or 0 for k in
                                  ('input_tokens', 'cache_read_input_tokens',
                                   'cache_creation_input_tokens'))
        row['output_tokens'] = usage.get('output_tokens') or 0

        pairs = []
        for record in mine:
            reading = None if not record['reading'] else record['reading']['referent']
            reading = None if reading == ANSWER_UNCLEAR else reading
            gazette = gazetteer_answer(record['gazetteer'])
            other = gazetteer_answer(record['otherDataset'])
            already = gazetteer_answer(record['annotated'])
            pairs.append((reading, gazette, other, already, record))
            if reading and gazette and reading != gazette:
                disagreements.append({
                    'number': record['number'], 'reference': record['reference'],
                    'reading': reading, 'gazetteer': gazette,
                    'check': None if not record['check'] else record['check']['referent'],
                    'settled': record['referent'], 'standing': record['standing'],
                    'reason': record['reading']['reason'],
                    'why': None if not record['check'] else record['check']['why'],
                    'rendering': None,
                })
        row['pairs'] = pairs
        rows.append(row)

    everything = [pair for row in rows for pair in row['pairs']]

    out = []
    write = out.append
    asked = read_json(args.dir, 'answers', rows[0]['number']) if rows else {}
    write(f'# The place namesake split, run — {len(rows)} of {len(manifest["groups"])} numbers\n')
    write(f'Model `{asked.get("model")}`, effort `{asked.get("effort")}`, prompt '
          f'`{asked.get("prompt_version")}`, asked {asked.get("asked")}.\n')

    write('\n## Agreement, per occurrence\n')
    write('| pair | agree | decided | % |')
    write('|---|---:|---:|---:|')
    for name, left, right in [('reading vs gazetteer', 0, 1),
                              ('reading vs BibleData', 0, 2),
                              ('gazetteer vs BibleData', 1, 2),
                              ('reading vs what is already annotated', 0, 3),
                              ('gazetteer vs what is already annotated', 1, 3)]:
        same, total, percent = agreement([(pair[left], pair[right]) for pair in everything])
        write(f'| {name} | {same} | {total} | {percent:.1f}% |')

    write('\n## Agreement against the gazetteer, per number\n')
    write('| number | name | occurrences | agree | decided | % |')
    write('|---|---|---:|---:|---:|---:|')
    for row in sorted(rows, key=lambda r: (-r['occurrences'], r['number'])):
        same, total, percent = agreement([(pair[0], pair[1]) for pair in row['pairs']])
        write(f'| {row["number"]} | {row["name"]} | {row["occurrences"]} | {same} | {total} '
              f'| {percent:.1f}% |')

    write('\n## Cost and wall clock, by band\n')
    write('| band | numbers | occurrences (median) | $ a number | s a number | $ |')
    write('|---|---:|---:|---:|---:|---:|')
    for name, _, _ in BANDS:
        band = [r for r in rows if r['band'] == name]
        if not band:
            continue
        write(f'| {name} | {len(band)} | '
              f'{statistics.median(r["occurrences"] for r in band):.0f} | '
              f'${statistics.mean(r["cost"] for r in band):.4f} | '
              f'{statistics.mean(r["seconds"] for r in band):.0f} | '
              f'${sum(r["cost"] for r in band):.2f} |')
    spent = sum(r['cost'] for r in rows)
    seconds = sum(r['seconds'] for r in rows)
    write(f'\n**${spent:.4f} and {seconds / 3600:.2f} hours of model time for {len(rows)} numbers.**\n')
    write('\nThat is the cost carried on the answers that survive. A run resumed after a failure paid '
          'for the calls it repeated as well, and what was actually paid is the sum of the `ask` and '
          "`check` lines in each run's own output.\n")

    write('\n## What the payload is made of\n')
    total = collections.Counter()
    for row in rows:
        total.update(row['sections'])
    whole = sum(total.values())
    write('| section | bytes | share |')
    write('|---|---:|---:|')
    for name, size in total.most_common():
        write(f'| {name} | {size:,} | {100.0 * size / whole:.1f}% |')

    write('\n## Disagreements\n')
    write(f'{len(disagreements)} occurrences where the reading and the gazetteer name different '
          'places, in `disagreements.json`. What each was settled as is in the `settled` column '
          'there, and what settled it in `standing`.\n')
    write('| number | reference | reading | gazetteer | second reading | settled |')
    write('|---|---|---|---|---|---|')
    for row in disagreements:
        write(f'| {row["number"]} | {row["reference"]} | {row["reading"]} | {row["gazetteer"]} '
              f'| {row["check"] or "—"} | {row["settled"] or "unsettled"} |')

    report = '\n'.join(out) + '\n'
    with open(os.path.join(args.dir, 'score.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    with open(os.path.join(args.dir, 'disagreements.json'), 'w', encoding='utf-8') as handle:
        json.dump(disagreements, handle, ensure_ascii=False, indent=1)
    print(report)


def census(args):
    """
    The population and what the gazetteer settles of it on its own, with no model in it. It is worth
    reading before any of this is run: the dataset that placed these records answers most of the
    question already, and what the model is for is the rest of it and a second opinion on the whole.
    """
    groups = population()
    said = {row['wordId']: row for row in witnesses()}
    standing = collections.defaultdict(list)
    for occurrence in occurrences():
        standing[occurrence['number']].append(occurrence)

    carrying = [g for g in groups if g['occurrences']]
    words = sum(g['occurrences'] for g in groups)
    print(f'{len(groups)} Strong numbers are borne by two or more places the gazetteer puts at two '
          f'or more sites; {len(carrying)} of them carry {words:,} occurrences, '
          f'{sum(g["annotated"] for g in groups):,} of which a word already names somebody at.')
    print(f'{sum(g["records"] for g in groups)} record memberships, '
          f'{len({g["name"] for g in groups})} distinct names.')
    print()

    places = collections.Counter()
    for group in groups:
        for occurrence in standing[group['number']]:
            places[len(said.get(occurrence['wordId'], {}).get('gazetteer', []))] += 1
    print('the gazetteer places, in the verse an occurrence stands in:')
    for count in sorted(places):
        print(f'  {count} of this number\'s records   {places[count]:>6}'
              + ('   settled by it' if count == 1 else '   the reading is on its own'))
    print()

    print('band     numbers  occurrences  median  records')
    for name, _, _ in BANDS:
        band = [g for g in carrying if band_of(g['occurrences']) == name]
        if not band:
            continue
        print(f'{name:<8} {len(band):>7} {sum(g["occurrences"] for g in band):>12} '
              f'{statistics.median(g["occurrences"] for g in band):>7.0f} '
              f'{sum(g["records"] for g in band):>8}')


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    commands = parser.add_subparsers(dest='command', required=True)

    extractor = commands.add_parser('extract', help='write a payload for every number')
    extractor.add_argument('--out', required=True)
    extractor.add_argument('--gazetteer', default=GAZETTEER_FILE)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='assign the occurrences, number by number')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--effort', required=True, choices=['low', 'medium', 'high', 'xhigh', 'max'])
    asker.add_argument('--workers', type=int, default=3)
    asker.add_argument('--again', action='store_true')
    asker.add_argument('--only', nargs='*', metavar='NUMBER')
    asker.set_defaults(run=ask)

    checker = commands.add_parser('check', help='read the contested occurrences a second time')
    checker.add_argument('--dir', required=True)
    checker.add_argument('--model', default='sonnet')
    checker.add_argument('--effort', required=True, choices=['low', 'medium', 'high', 'xhigh', 'max'])
    checker.add_argument('--workers', type=int, default=3)
    checker.add_argument('--again', action='store_true')
    checker.add_argument('--only', nargs='*', metavar='NUMBER')
    checker.set_defaults(run=check)

    registrar = commands.add_parser('register', help='decide every occurrence, or leave it open')
    registrar.add_argument('--dir', required=True)
    registrar.set_defaults(run=register)

    publisher = commands.add_parser('publish', help='write the register under Resources/Essenthos')
    publisher.add_argument('--dir', required=True)
    publisher.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'sites'))
    publisher.add_argument('--prefix', default='register')
    publisher.set_defaults(run=publish)

    scorer = commands.add_parser('score', help='the run against the witnesses, and what it cost')
    scorer.add_argument('--dir', required=True)
    scorer.set_defaults(run=score)

    counter = commands.add_parser('census', help='the population, with no model in it')
    counter.set_defaults(run=census)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
