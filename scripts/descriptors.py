"""
What is true of this person, and which verse says so? Ask a model, and measure it against BibleData.

`entity.distinguisher` is one English sentence per entity, imported whole from BibleData: not ours,
not structured, not translatable (NOT-0171, FTR-0359). This harness replaces it with the claims that
would make one -- `hobab -> son-of -> reuel-2 (NUM 10:29)` -- read out of the King James text of the
verses the encyclopedia already attests the entity in, with the name forms every language needs
produced beside them. DOC-0191 is the contract for what comes out; this writes it and nothing else.

    python scripts/descriptors.py extract --out .descriptors/pilot --sample 300 --seed 11
    python scripts/descriptors.py ask     --dir .descriptors/pilot --workers 4 --effort low
    python scripts/descriptors.py score   --dir .descriptors/pilot
    python scripts/descriptors.py publish --dir .descriptors/pilot

To ask a second time about part of a corpus already described -- because the vocabulary widened, or
because the verses shown were too narrow -- name the entities, show the context, and publish under a
prefix that sorts after the run being superseded:

    python scripts/descriptors.py extract --out .descriptors/reask --only reask.txt --context
    python scripts/descriptors.py ask     --dir .descriptors/reask --workers 3 --effort low
    python scripts/descriptors.py publish --dir .descriptors/reask --against .descriptors/full --prefix reask

Seven things about the design are load-bearing, and each is a way the output could have been made
worthless rather than merely wrong:

**BibleData's sentence is never shown to the model.** Not the entity's `distinguisher` and not any
candidate's. The whole point of the pass is that the prose stops being theirs; a model shown *the
son of Reuel, Moses' father-in-law* and asked for claims would be transcribing it, and the agreement
figure would measure transcription. The model gets Scripture, our own slugs, and nothing else.

**`entity_relationship` is the answer key and is never in the prompt.** 5,448 rows of BibleData's
relations exist in the same database and would answer most of the question outright. `score` reads
them; `extract` must not, and the one place that could leak them -- the candidate list -- is built
from shared verses instead.

**A claim can only cite one of the entity's own verses.** Those are its `entity_verse` rows, which
is exactly what the loader will accept, so every surviving claim is loadable by construction. A
reference outside that set is dropped and counted rather than kept.

**The verses around a register line are shown, and are not citable.** *And his firstborn son Abdon,
and Zur, and Kish* names no father and *And Elioenai, and Jaakobah* no house; the verse before and
the `the sons of X` heading above carry what the line is missing, and without them the model can
only be silent about a name the encyclopedia does hold something about. They go in `context`, they
are offered only for a verse that is a bare list of names, and `validate` does not add them to the
references a claim may cite.

**A target we do not hold is not a claim.** It goes in `unresolved` as a plain string, so the gap is
countable. A claim whose target does not resolve to a slug is never turned into prose.

**The name forms are produced with the name, and the model is shown the Ohienko and Synodal verses
to produce them from.** A stemmer guessing the genitive of a Hebrew proper name is wrong often and
silently; a model that has seen *Мойсея* in the Ukrainian text of the same verse is not guessing.

**A published record never says less than the one it replaces.** The loader takes the later file
whole, so a second answer that leaves a language out is a form the reader loses; `publish` carries
the standing record's forms into the new one and leaves a record that gains neither a clause nor a
form unpublished rather than superseding for nothing.

`extract` writes the prompt payloads, `ask` writes one contract-shaped JSON object per entity per
batch, `score` measures those against BibleData and writes a report, `publish` copies the validated
batches under `Resources/Essenthos/descriptors/`. A run leaves:

    manifest.json              the selection, its seed, and which entities went into which batch
    batches/batch-NNNN.json    the prompt payload, exactly as the model saw it
    out/batch-NNNN.jsonl       the descriptors, in DOC-0191's shape, one object per line
    rejected.jsonl             every claim dropped in validation, with the reason
    score.md, disagreements.json
"""

import argparse
import concurrent.futures
import datetime
import json
import os
import random
import re
import shutil
import subprocess
import sys
import threading
import time

sys.stdout.reconfigure(encoding='utf-8')

CONTAINER = 'essenthos-api-db-1'
DATABASE = 'essenthos_core'
USER = 'essenthos'

RENDERING = 'KJV'
UKRAINIAN = 'UBIO'
RUSSIAN = 'RUSV'

PROMPT_VERSION = 'descriptor-2'

# The book codes the corpus's own book file publishes (BibleData-Book.csv, usx_code), which is the
# spelling DOC-0191's examples are written in.
BOOKS = [
    'GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH',
    '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK',
    'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL',
    'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH',
    '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD',
    'REV',
]

# DOC-0191's vocabulary, closed. The loader refuses anything else, so anything else is dropped here
# and counted, rather than written out for the loader to refuse one file at a time.
RELATIONS = [
    'son-of', 'daughter-of', 'father-of', 'mother-of', 'brother-of', 'sister-of',
    'husband-of', 'wife-of', 'concubine-of',
    'half-brother-of', 'half-sister-of',
    'grandfather-of', 'grandmother-of', 'grandson-of', 'granddaughter-of',
    'uncle-of', 'aunt-of', 'nephew-of', 'niece-of',
    'ancestor-of', 'descendant-of',
    'father-in-law-of', 'mother-in-law-of', 'son-in-law-of', 'daughter-in-law-of',
    'brother-in-law-of', 'sister-in-law-of', 'cousin-of',
    'king-of', 'queen-of', 'prophet-to', 'priest-of', 'judge-of', 'high-priest-of',
    'commander-of', 'governor-of', 'tetrarch-of',
    'servant-of', 'master-of', 'disciple-of', 'apostle-of', 'scribe-of', 'companion-of',
    'teacher-of', 'ally-of',
    'killed-by', 'killer-of', 'raped-by', 'raper-of', 'angel-of',
    'of-tribe', 'of-people', 'from-place', 'lived-in', 'buried-in',
    'descendants-of',
    'city-in', 'region-of', 'river-of', 'mountain-in', 'gate-of', 'near',
]

# The nineteen TSK-0424 added. A re-ask of an entity the first pass already described earns its
# place by using one of these; anything else it says, the pass before it could have said too.
WIDENED = {
    'concubine-of', 'half-brother-of', 'half-sister-of', 'grandson-of', 'granddaughter-of',
    'uncle-of', 'aunt-of', 'nephew-of', 'niece-of', 'brother-in-law-of', 'sister-in-law-of',
    'governor-of', 'tetrarch-of', 'master-of', 'companion-of',
    'killed-by', 'killer-of', 'angel-of', 'gate-of',
}

assert WIDENED <= set(RELATIONS)

LANGUAGES = ['eng', 'ukr', 'rus', 'deu']

# The nominative and the genitive are what DOC-0191 asks for; the locative is what PRB-0361 found
# missing, because *похований у Хевроні* has no genitive that stands in for it and four of the
# relations are exactly the ones a place page is made of. The loader already stores one where a
# pass supplies it.
FORMS = ['nominative', 'genitive', 'locative']

# How much of an entity is shown. A name borne through four hundred verses is established by its
# first dozen and the rest add cost, not evidence; the first mention is where a genealogy states
# itself, so the head of the list is taken whole and the tail is sampled.
VERSES_SHOWN = 14
VERSES_FROM_THE_HEAD = 9
TRANSLATED_VERSES_SHOWN = 3
CANDIDATES_SHOWN = 40
CANDIDATE_REFERENCES_SHOWN = 4

# A candidate the annotation does not supply but the King James line plainly names. Joshua 19:15
# puts Idalah in the inheritance of Zebulun and the encyclopedia holds Zebulun, but no word of that
# verse is annotated to him, so a candidate list built from `entity_verse` alone offers a place no
# territory to be in and the model can only answer `unresolved: Zebulun`. Matching the rendering's
# own capitalised words against the names we hold closes most of that, at the cost of offering the
# five men called Judah where the verse means one of them -- which is a question, not an error.
NAMED_CANDIDATES_SHOWN = 18
SHORTEST_NAME_MATCHED = 4

# The verses around an entity's own, for the register line that says nothing by itself. Of the
# 1,236 entities the first full pass could not describe, 403 were shown nothing but a bare name
# list, and the two ways the claim sits just outside it are different: *And his firstborn son
# Abdon, and Zur, and Kish* has its father in the verse immediately before, while *And Elioenai,
# and Jaakobah* belongs to a `the sons of X` heading a dozen verses above. Neighbours reach the
# first and not the second, so both are taken -- and the headings are walked back to rather than
# the chapter's opening being used, because 1 Chronicles 4 opens with Judah and turns to Simeon at
# verse 24, so the top of the chapter is a confident wrong answer.
CONTEXT_NEIGHBOURS = 1
CONTEXT_HEADINGS = 2
HEADING_REACH = 40
CONTEXT_VERSES_SHOWN = 8

# A segment of three words or fewer beginning with a capital is a name standing on its own. Two
# thirds of a verse made of those, and at least three of them, is a register rather than a sentence.
SHORT_SEGMENT_WORDS = 3
LIST_SEGMENTS_LEAST = 3

# The batch shape. The harness overhead is per call, so one entity per call costs several times what
# eight do; the verse budget is the other half of it, because eight entities is cheap until one of
# them is Judah. Nothing is ever split: an entity's verses are capped before batching, so no single
# entity can exceed the budget on its own.
BATCH_ENTITIES = 8
BATCH_VERSES = 90

SYSTEM_PROMPT = """\
You are a Biblical scholar writing the encyclopedia's own description of a person, a place or a
people -- not as a sentence, but as the claims a sentence would be made of, each read out of a
named verse.

For each entity you are given: its slug, its kind, its name and what that name means, the King James
text of verses the encyclopedia attests it in (for a few of them, the Ukrainian and Russian text of
the same verse as well), and the candidate entities the encyclopedia holds that could be meant in
those verses. Read the verses. Say what they establish about the entity, and nothing they do not.

A candidate marked `annotated` is one this corpus has already tied to a word of that verse. One
marked `named in the text` was found by matching the King James spelling, so it may be the wrong one
of several who bear the name, or no one at all -- its `attested_in` references are there for you to
tell which. Both are offers, never answers.

Some entities also carry `context`: verses standing around their own, given because a name in a
register says nothing on its own and the line that governs it is a verse or two away. *And his
firstborn son Abdon, and Zur, and Kish* names no father; the verse before it does. *And Elioenai,
and Jaakobah* belongs to whichever *the sons of X* heading it falls under, and that heading is in
the context.

**Read the context, cite your own verses.** A `context` verse is there to tell you who *his* is and
whose register this is; it is not a reference you may use. Every claim's `reference` must still be
one of the entity's own `verses`, because those are the verses the entity is recorded as occurring
in and the only ones a reader can be sent to. So `abdon-3 son-of jeiel` cites the verse that calls
him a firstborn son, not the one that names his father.

The context is for finding the head of the register -- *the sons of Shashak*, *of the sons of Bani*,
*they that came to David to Ziklag* -- and the claim to make from it is about that head. It is not a
licence to relate the entity to the names printed beside it.

Two rules decide almost every case:

- **Cite a verse from the list you were given for that entity.** A claim's `reference` must be one
  of that entity's own references, spelled exactly as given. If the verses you were shown do not
  establish a claim, do not make it -- however certain you are of it from elsewhere.
- **Name a target by its slug.** If the verse names somebody or somewhere the candidate list does
  not hold, put the plain English name in `unresolved` and make no claim about it. Never invent a
  slug, never guess at one, and never bend a claim onto a candidate that is merely nearby.

The relation vocabulary is closed. Use only these, and read each as "the entity IS THE RELATION of
the target" -- `son-of` means the entity is the son of the target, `father-in-law-of` means the
entity is the target's father-in-law:

  kinship   son-of daughter-of father-of mother-of brother-of sister-of
            husband-of wife-of concubine-of
            half-brother-of half-sister-of
            grandfather-of grandmother-of grandson-of granddaughter-of
            uncle-of aunt-of nephew-of niece-of
            ancestor-of descendant-of
            father-in-law-of mother-in-law-of son-in-law-of daughter-in-law-of
            brother-in-law-of sister-in-law-of cousin-of
  office    king-of queen-of prophet-to priest-of judge-of high-priest-of
            commander-of governor-of tetrarch-of
            servant-of master-of disciple-of apostle-of scribe-of companion-of
            teacher-of ally-of
  violence  killed-by killer-of raped-by raper-of
  belonging of-tribe of-people from-place lived-in buried-in angel-of
  peoples   descendants-of
  places    city-in region-of river-of mountain-in gate-of near

A relation outside that list is thrown away, so a claim that needs one is a claim not to make.

Three readings go wrong often enough to be worth naming:

- **"of the sons of X" in a register is a line, not a father.** Ezra 10, Nehemiah 7 and the
  Chronicles lists group men under the head of a family many generations back. Use `descendant-of`
  there, and keep `son-of` for a verse that states the parentage itself -- *A begat B*, *B the son
  of A*, *his son*.
- **A list of towns does not make them neighbours.** Joshua 15 and 19 enumerate an inheritance;
  standing next to another name in that enumeration is not `near`, and the towns of one list are
  often far apart. Claim `city-in` or `region-of` for the territory the list belongs to, and leave
  `near` for a verse that actually places one beside the other.
- **The same is true of men, and it is the commonest way to be wrong here.** *And Abdon, and Zichri,
  and Hanan* relates each of them to the head the register names -- `son-of` or `descendant-of`
  **Shashak** -- and to no one printed beside them. Never make `brother-of` or `companion-of` out of
  adjacency. If the register names no head even in the context, the honest answer is no claim.
- **A gentilic is a people, not a place.** *Ahimelech the Hittite* is `of-people` the Hittites;
  *a man of Bethlehem* is `from-place`.
- **A half-sibling needs the verse to say the parents differ.** *Sons of David by his wives* beside
  *the sons of the concubines* is `half-brother-of`; two men called sons of one father with no
  second mother named is `brother-of`. Never soften `brother-of` into `half-brother-of` to be safe.
- **`killed-by` is the person killed, `killer-of` the one who killed.** *Zichri slew Maaseiah* makes
  `maaseiah killed-by zichri` and `zichri killer-of maaseiah`. Both are worth stating; a battle in
  which somebody merely took part is neither.

`companion-of` is for a verse that says two people went, worked, were sent or stood together --
*Barnabas and Saul*, *his fellowprisoner*, *repaired next unto him* -- and never for two names that
merely occur in one list.
`angel-of` is a being the text defines by whose it is, as in *the angel of the LORD*.

Order the claims the way the sentence would run: the one that identifies the entity first. Two or
three good claims beat six weak ones -- this is a line under a name, not a biography. Give a
confidence between 0 and 1 for each: how sure you are that this verse establishes this claim about
this entity, not how famous the fact is.

For a people, `descendants-of` naming the forebear is the claim that matters most, and it must still
be read from one of the verses you were shown.

## The names

Give the entity's name in each of eng, ukr, rus and deu, in the spelling that language's Bible
actually uses -- the Ukrainian (Ohienko) and Russian (Synodal) verses you were shown are there so
you do not have to guess, and the German follows the Luther Bible. Watch the case the verse happens
to put the name in: a verse that says *Мегуманові* is a dative, and the nominative is what you owe.
For ukr, rus and deu give the genitive as well as the nominative, because the rendering puts the
name into it: *тесть Мойсея*, not *тесть Мойсей*. English needs only the nominative. A people's name
is plural in every form (*моавітяни*, *моавітян*). If you genuinely do not know a language's form,
leave that language out entirely rather than inventing one -- but all four are normally knowable
for a Biblical name, and a missing German is usually laziness rather than a gap.

**For a place, ukr and rus also need the locative**, because *buried in X* and *a city in X* put the
name into it and no genitive stands in: *похований у Хевроні*, not *у Хеврона*; *місто в Юдеї*. Give
`locative` beside the other two for every place, and for anything else a verse would say somebody
lived or was buried in.

## The reply

A single JSON array and nothing else -- no prose before or after, no code fence needed. One object
per entity, in the order given, with exactly these fields:

  entity      the slug you were given, unchanged
  claims      an array of {relation, target, reference, confidence}, ordered; may be empty
  names       an object of language code -> {nominative, genitive, locative}
  unresolved  an array of plain English names the verses gave that the candidates did not hold

Every entity you were given must appear exactly once.
"""


def psql(sql):
    """One JSON value out of the live database. Read-only by construction: nothing here writes."""
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', CONTAINER,
         'psql', '-U', USER, '-d', DATABASE, '-Aqt', '-v', 'ON_ERROR_STOP=1', '-f', '-'],
        input=sql.encode('utf-8'), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
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


REFERENCE = re.compile(r'^\s*([1-3]?[A-Za-z]{2,4})\s+(\d+)\s*[:.]\s*(\d+)\s*$')
BY_CODE = {code: index + 1 for index, code in enumerate(BOOKS)}


def parse_reference(text):
    """A reference back to (book, chapter, verse), forgiving of case and spacing but not of spelling."""
    match = REFERENCE.match(text or '')
    if not match:
        return None
    book = BY_CODE.get(match.group(1).upper())
    return (book, int(match.group(2)), int(match.group(3))) if book else None


def entities():
    """
    Every entity the encyclopedia holds, with its verses and the name rows that describe the name.

    `distinguisher` is deliberately absent: it is BibleData's sentence, it is what this pass exists
    to replace, and a model shown it would be rewriting it rather than reading Scripture.
    """
    return psql("""
        SELECT coalesce(json_agg(json_build_object(
                   'id', e.id, 'slug', e.slug, 'kind', e.kind, 'name', e.name,
                   'sex', e.sex, 'place_kind', e.place_kind, 'source', e.source,
                   'origin', (SELECT o.slug FROM entity o WHERE o.id = e.origin_entity_id),
                   'names', (SELECT coalesce(json_agg(json_build_object(
                                 'label', n.label, 'hebrew', n.hebrew,
                                 'hebrew_transliterated', n.hebrew_transliterated,
                                 'greek', n.greek, 'greek_transliterated', n.greek_transliterated,
                                 'meaning', n.meaning, 'kind', n.kind)), '[]')
                             FROM entity_name n WHERE n.entity_id = e.id),
                   'verses', (SELECT coalesce(json_agg(json_build_array(
                                 ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)
                                 ORDER BY ev.canonical_book, ev.canonical_chapter,
                                          ev.canonical_verse), '[]')
                              FROM entity_verse ev WHERE ev.entity_id = e.id))
                   ORDER BY e.slug), '[]')
        FROM entity e
    """)


def occupancy():
    """Which entities occur in which verse: the whole of `entity_verse`, keyed for intersection."""
    return psql("""
        SELECT coalesce(json_agg(json_build_array(
                   ev.canonical_book, ev.canonical_chapter, ev.canonical_verse, e.slug)), '[]')
        FROM entity_verse ev JOIN entity e ON e.id = ev.entity_id
    """)


def rendering(slug):
    """One text's verses, keyed by canonical address. Words joined with their trailers, as read."""
    return psql(f"""
        SELECT coalesce(json_agg(json_build_array(x.b, x.c, x.v, x.line)), '[]')
        FROM (
            SELECT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v,
                   string_agg(w.text || w.trailer, '' ORDER BY w.position) AS line
            FROM verse ve
            JOIN verse_reference r ON r.verse_id = ve.id AND r.is_primary
            JOIN word w ON w.verse_id = ve.id
            WHERE ve.text_id = (SELECT id FROM text WHERE upper(slug) = '{slug}')
            GROUP BY 1, 2, 3
        ) x
    """)


def answer_key():
    """
    BibleData's relations, read only at scoring time. `from` is the RELATION of `to`, which is the
    same direction the vocabulary reads in: `isaac son sarai` is Isaac the son of Sarai.
    """
    return psql("""
        SELECT coalesce(json_agg(json_build_object(
                   'from', f.slug, 'to', t.slug, 'type', r.type, 'category', r.category,
                   'book', r.canonical_book, 'chapter', r.canonical_chapter,
                   'verse', r.canonical_verse)), '[]')
        FROM entity_relationship r
        JOIN entity f ON f.id = r.from_entity_id
        JOIN entity t ON t.id = r.to_entity_id
    """)


# BibleData's type names against DOC-0191's vocabulary, in the same direction. What is absent is
# absent from the vocabulary, not from the mapping: patron, client, concubinator, exiled, original heir
# and Creator have no relation to be scored against (cousin, ally, rabbi and lady gained one on
# 2026-09-10), and `score` counts them so the size of that gap is visible
# rather than assumed. `victim` is scored as `killed-by` only because the homicides are what the
# vocabulary took; its rape rows will read as disagreements, which is the honest outcome.
KEY_RELATIONS = {
    'son': 'son-of', 'daughter': 'daughter-of', 'father': 'father-of', 'mother': 'mother-of',
    'brother': 'brother-of', 'sister': 'sister-of',
    'husband': 'husband-of', 'wife': 'wife-of',
    'half-brother': 'half-brother-of', 'half-sister': 'half-sister-of',
    'grandfather': 'grandfather-of', 'grandmother': 'grandmother-of',
    'grandson': 'grandson-of', 'granddaughter': 'granddaughter-of',
    'uncle': 'uncle-of', 'aunt': 'aunt-of', 'nephew': 'nephew-of', 'niece': 'niece-of',
    'ancestor': 'ancestor-of', 'descendant': 'descendant-of',
    'father-in-law': 'father-in-law-of', 'mother-in-law': 'mother-in-law-of',
    'son-in-law': 'son-in-law-of', 'daughter-in-law': 'daughter-in-law-of',
    'brother-in-law': 'brother-in-law-of', 'sister-in-law': 'sister-in-law-of',
    'concubine': 'concubine-of',
    'servant': 'servant-of', 'master': 'master-of',
    'disciple': 'disciple-of', 'apostle': 'apostle-of',
    'king': 'king-of',
    'killer': 'killer-of', 'killed by': 'killed-by', 'victim': 'killed-by',
    'army captain': 'commander-of',
    'officer': 'servant-of', 'chamberlain': 'servant-of', 'chief official': 'servant-of',
    'lieutenant': 'servant-of', 'chief': 'master-of', 'lady': 'master-of',
    'born by': 'son-of', 'bearer': 'mother-of',
    'cousin': 'cousin-of', 'rabbi': 'teacher-of', 'ally': 'ally-of', 'raper': 'raper-of',
}


def cache(directory, name, produce):
    path = os.path.join(directory, name + '.json')
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            return json.load(handle)
    value = produce()
    os.makedirs(directory, exist_ok=True)
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(value, handle, ensure_ascii=False)
    return value


def corpus(cache_dir):
    return {
        'entities': cache(cache_dir, 'entities', entities),
        'occupancy': cache(cache_dir, 'occupancy', occupancy),
        RENDERING: cache(cache_dir, RENDERING, lambda: rendering(RENDERING)),
        UKRAINIAN: cache(cache_dir, UKRAINIAN, lambda: rendering(UKRAINIAN)),
        RUSSIAN: cache(cache_dir, RUSSIAN, lambda: rendering(RUSSIAN)),
    }


def shown_verses(all_of_them):
    """
    Which of an entity's verses go in the prompt: the head whole, then the tail evenly sampled.

    The head is where a genealogy states itself -- *Hobab, the son of Raguel the Midianite* is the
    first time Hobab is named -- and the tail is what tells a reader that the name belongs to a
    king rather than to one of his officers.
    """
    if len(all_of_them) <= VERSES_SHOWN:
        return list(all_of_them)
    head = all_of_them[:VERSES_FROM_THE_HEAD]
    tail = all_of_them[VERSES_FROM_THE_HEAD:]
    room = VERSES_SHOWN - VERSES_FROM_THE_HEAD
    step = max(1, len(tail) // room)
    return head + tail[::step][:room]


LIST_SEGMENT = re.compile(r'[,;:]| and ')
LIST_OPENER = re.compile(r'^\s*(?:And|Also|Now|Then)\s+')

# The word an enumeration repeats before each name, which the name has to be seen past: Jeremiah 48
# is *and upon Kiriathaim, and upon Bethgamul*, Nehemiah 11 is *and at Jeshua, and at Moladah*.
LIST_PREPOSITION = re.compile(r'^(?:and\s+)?(?:upon|unto|at|of|in|to|from|by|with)\s+', re.I)


def list_shaped(line):
    """A verse that is a register of names rather than a sentence about anybody."""
    parts = [LIST_PREPOSITION.sub('', part.strip(' .,;:()'))
             for part in LIST_SEGMENT.split(LIST_OPENER.sub('', (line or '').strip()))]
    parts = [part for part in parts if part]
    if len(parts) < LIST_SEGMENTS_LEAST:
        return False
    bare = sum(1 for part in parts
               if len(part.split()) <= SHORT_SEGMENT_WORDS and part[:1].isupper())
    return bare >= LIST_SEGMENTS_LEAST and bare * 3 >= len(parts) * 2


HEADING = re.compile(
    r'\b(?:the sons of|the children of|the daughters of|these are|these were|now these'
    r'|out of the tribe of|the famil(?:y|ies) of|the house of|the men of|the number of'
    r'|the inheritance of|heads of the fathers|begat|dwelt|dwelleth)\b', re.I)


def context_verses(shown, lines):
    """
    The verses standing around a register line, so a name in a list has something to be read against.

    Two kinds, answering different questions: the neighbours resolve a pronoun, and the heading says
    whose register this is. A heading is looked for in both directions and taken nearest first,
    because 1 Chronicles 8 closes a run of names with *the sons of Shashak* where Ezra 10 opens one
    with *of the sons of Bani*, and reading only upwards gets one of the two wrong.

    Only a list-shaped verse gets any of this, so an entity shown whole sentences pays nothing.
    """
    owned = set(shown)
    picked = []
    for book, chapter, verse in shown:
        if not list_shaped(lines.get((book, chapter, verse))):
            continue
        around = [(book, chapter, verse + step)
                  for step in range(-CONTEXT_NEIGHBOURS, CONTEXT_NEIGHBOURS + 1) if step]
        headings = []
        for distance in range(1, HEADING_REACH + 1):
            for key in ((book, chapter, verse - distance), (book, chapter, verse + distance)):
                line = lines.get(key)
                if line and HEADING.search(line) and len(headings) < CONTEXT_HEADINGS:
                    headings.append(key)
            if len(headings) == CONTEXT_HEADINGS:
                break
        for key in around + headings:
            if key in lines and key not in owned and key not in picked:
                picked.append(key)
    return sorted(picked[:CONTEXT_VERSES_SHOWN])


def proper_name(entity):
    for row in entity['names']:
        if (row.get('kind') or 'proper name') == 'proper name':
            return row
    return entity['names'][0] if entity['names'] else {}


WORD = re.compile(r"[A-Za-z]+(?:-[A-Za-z]+)*")


def name_index(all_entities):
    """
    Every name the encyclopedia holds, keyed by the spelling a King James line would use.

    Title rows are left out on purpose: *the servant of the LORD* is a name row too, and indexing
    its words would put Moses forward as a candidate for every verse containing *servant*.
    """
    index = {}
    for entity in all_entities:
        labels = {entity['name']}
        for row in entity['names']:
            if (row.get('kind') or 'proper name') == 'proper name' and row.get('label'):
                labels.add(row['label'])
        for label in labels:
            for token in WORD.findall(label):
                key = token.replace('-', '').lower()
                if len(key) >= SHORTEST_NAME_MATCHED:
                    index.setdefault(key, set()).add(entity['slug'])
    return index


def named_in(line, index):
    """The entities a King James line spells out. Capitalised words only, which is what a name is."""
    found = set()
    for token in WORD.findall(line or ''):
        if not token[0].isupper():
            continue
        found |= index.get(token.replace('-', '').lower(), set())
        for part in token.split('-'):
            if part and part[0].isupper():
                found |= index.get(part.lower(), set())
    return found


def build(entity, verses, by_verse, texts, by_slug, index, with_context=False):
    """One entity as the model sees it: its name, its verses, and who else stands in them."""
    name_row = proper_name(entity)
    shown = shown_verses(verses)

    annotated, spelled = {}, {}
    for key in shown:
        for slug in by_verse.get(key, ()):
            if slug != entity['slug']:
                annotated[slug] = annotated.get(slug, 0) + 1
        for slug in named_in(texts[RENDERING].get(key), index):
            if slug != entity['slug'] and slug not in annotated:
                spelled[slug] = spelled.get(slug, 0) + 1
    if entity.get('origin') and entity['origin'] not in annotated:
        spelled.setdefault(entity['origin'], 1)

    ranked = [(slug, 'annotated') for slug in
              sorted(annotated, key=lambda s: (-annotated[s], s))[:CANDIDATES_SHOWN]]
    room = min(NAMED_CANDIDATES_SHOWN, CANDIDATES_SHOWN - len(ranked))
    ranked += [(slug, 'named in the text') for slug in
               sorted(spelled, key=lambda s: (-spelled[s], s))[:max(0, room)]]

    candidates = []
    for slug, how in ranked:
        other = by_slug.get(slug)
        if not other:
            continue
        step = max(1, len(other['verses']) // CANDIDATE_REFERENCES_SHOWN)
        candidates.append({
            'slug': slug,
            'how': how,
            'kind': other['kind'],
            'name': other['name'],
            'sex': other.get('sex'),
            'place_kind': other.get('place_kind'),
            'attested_in': [reference(*v) for v in other['verses'][::step][:CANDIDATE_REFERENCES_SHOWN]],
        })

    lines = []
    for index, key in enumerate(shown):
        line = {'reference': reference(*key), 'king_james': texts[RENDERING].get(key)}
        if index < TRANSLATED_VERSES_SHOWN:
            line['ukrainian'] = texts[UKRAINIAN].get(key)
            line['russian'] = texts[RUSSIAN].get(key)
        lines.append(line)

    payload = {
        'entity': entity['slug'],
        'kind': entity['kind'],
        'name': entity['name'],
        'sex': entity.get('sex'),
        'place_kind': entity.get('place_kind'),
        'hebrew': name_row.get('hebrew'),
        'hebrew_transliterated': name_row.get('hebrew_transliterated'),
        'greek': name_row.get('greek'),
        'greek_transliterated': name_row.get('greek_transliterated'),
        'name_means': name_row.get('meaning'),
        'attested_in_total': len(verses),
        'verses': lines,
        'candidates': candidates,
    }

    around = context_verses(shown, texts[RENDERING]) if with_context else []
    if around:
        payload['context'] = [{'reference': reference(*key), 'king_james': texts[RENDERING][key]}
                              for key in around]
    return payload


def keyed(rows):
    out = {}
    for book, chapter, verse, value in rows:
        out.setdefault((book, chapter, verse), value)
    return out


def occupants(rows):
    out = {}
    for book, chapter, verse, slug in rows:
        out.setdefault((book, chapter, verse), []).append(slug)
    return out


def extract(args):
    held = corpus(args.cache)
    by_slug = {e['slug']: e for e in held['entities']}
    for entity in held['entities']:
        # `entity_verse` holds a row per annotated word, so an entity named twice in one verse has
        # that verse twice. Shown twice it costs the verse budget and tells the model nothing.
        entity['verses'] = list(dict.fromkeys(tuple(v) for v in entity['verses']))
    by_verse = occupants(held['occupancy'])
    texts = {name: keyed(held[name]) for name in (RENDERING, UKRAINIAN, RUSSIAN)}

    kinds = set(args.kinds)
    pool = [e for e in held['entities'] if e['kind'] in kinds and e['verses']]

    if args.only:
        with open(args.only, encoding='utf-8') as handle:
            wanted = {line.strip() for line in handle if line.strip()}
        pool = [e for e in pool if e['slug'] in wanted]
        absent = wanted - {e['slug'] for e in pool}
        if absent:
            print(f'{len(absent)} of the {len(wanted)} named slugs are not entities with verses, '
                  f'and are skipped: {", ".join(sorted(absent)[:8])}')

    if args.sample:
        key = {}
        if args.prefer_scorable:
            for row in answer_key():
                if row['type'] in KEY_RELATIONS:
                    key.setdefault(row['from'], 0)
                    key[row['from']] += 1
        chosen = []
        shares = {'person': 0.66, 'place': 0.26, 'people': 0.08}
        for kind in sorted(kinds):
            of_kind = [e for e in pool if e['kind'] == kind]
            want = min(len(of_kind), max(1, round(args.sample * shares.get(kind, 1.0 / len(kinds)))))
            scorable = [e for e in of_kind if key.get(e['slug'])]
            plain = [e for e in of_kind if not key.get(e['slug'])]
            shuffle = random.Random(args.seed + sum(map(ord, kind)))
            shuffle.shuffle(scorable)
            shuffle.shuffle(plain)
            half = min(len(scorable), want // 2 if plain else want) if args.prefer_scorable else 0
            picked = scorable[:half] + plain[:want - half]
            chosen.extend(picked[:want])
        pool = sorted(chosen, key=lambda e: e['slug'])
        how = {'kind': 'sample', 'seed': args.seed, 'target': args.sample,
               'prefer_scorable': bool(args.prefer_scorable), 'drawn': len(pool)}
    else:
        how = {'kind': 'all', 'kinds': sorted(kinds), 'drawn': len(pool)}
    if args.only:
        how = dict(how, kind='named', only=os.path.basename(args.only), drawn=len(pool))
    how['context'] = bool(args.context)

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    manifest = {
        'prompt_version': PROMPT_VERSION,
        'database': DATABASE,
        'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'selection': how,
        'batches': [],
    }

    index = name_index(held['entities'])
    batch, verses_in_batch, plan = [], 0, []
    for entity in pool:
        payload = build(entity, entity['verses'], by_verse, texts, by_slug, index, args.context)
        cost = len(payload['verses']) + len(payload.get('context', ()))
        if batch and (len(batch) >= args.batch_entities
                      or verses_in_batch + cost > args.batch_verses):
            plan.append(batch)
            batch, verses_in_batch = [], 0
        batch.append(payload)
        verses_in_batch += cost
    if batch:
        plan.append(batch)

    for index, group in enumerate(plan):
        name = f'batch-{index:04d}'
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump({'batch': name, 'prompt_version': PROMPT_VERSION, 'entities': group},
                      handle, ensure_ascii=False, indent=1)
        manifest['batches'].append({
            'batch': name,
            'entities': [e['entity'] for e in group],
            'verses': sum(len(e['verses']) + len(e.get('context', ())) for e in group),
        })

    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    with_context = sum(1 for group in plan for e in group if e.get('context'))
    print(f'{len(pool)} entities, {len(plan)} batches, '
          f'{with_context} shown the verses around a register line -> {args.dir}')


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


# A call that has not answered in this long is not answering. Observed: twelve workers, four
# batches back in under three minutes each, and the other eight still holding a live process
# half an hour later -- with no timeout the pool never frees the slot and the run stops without
# ever reporting a failure, which is worse than the failure.
CALL_TIMEOUT = 600
CALL_ATTEMPTS = 2


def call(prompt, model, effort):
    """
    One batch, one call, one turn, no tools and no session. The flags are what turn the CLI into a
    worker rather than an agent; --bare looks like the right one and is not, because it reads auth
    strictly from an API key and never from the subscription.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', SYSTEM_PROMPT, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1',
    ] + (['--effort', effort] if effort else [])
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
                # An empty stderr is not "no error": read as a falsy failure it once killed a run
                # forty batches in. Say something whatever the process said.
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


ARRAY = re.compile(r'\[.*\]', re.S)


def parse(result):
    text = (result or '').strip()
    if text.startswith('```'):
        text = text.strip('`')
        text = text[text.find('\n') + 1:] if '\n' in text else text
        if text.lstrip().startswith('json'):
            text = text.lstrip()[4:]
    match = ARRAY.search(text)
    if not match:
        return None
    try:
        answers = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    return answers if isinstance(answers, list) else None


def validate(answer, asked, slugs, today, model):
    """
    One answered entity turned into a DOC-0191 object, and everything the loader would refuse
    turned into a counted rejection instead.
    """
    rejected = []
    claims = []
    unresolved = [u for u in (answer.get('unresolved') or []) if isinstance(u, str) and u.strip()]
    allowed = {r['reference'] for r in asked['verses']}

    for claim in answer.get('claims') or []:
        if not isinstance(claim, dict):
            continue
        relation, target = claim.get('relation'), claim.get('target')
        note = dict(claim, entity=asked['entity'])
        if relation not in RELATIONS:
            rejected.append(dict(note, why='relation outside the vocabulary'))
            continue
        if not isinstance(target, str) or target not in slugs:
            rejected.append(dict(note, why='target is not an entity we hold'))
            if isinstance(target, str) and target.strip() and target not in unresolved:
                unresolved.append(target)
            continue
        if target == asked['entity']:
            rejected.append(dict(note, why='the claim points at the entity itself'))
            continue
        text = claim.get('reference')
        if text not in allowed:
            parsed = parse_reference(text if isinstance(text, str) else '')
            text = reference(*parsed) if parsed else None
            if text not in allowed:
                rejected.append(dict(note, why='reference is not among the verses the entity occurs in'))
                continue
        try:
            confidence = float(claim.get('confidence'))
        except (TypeError, ValueError):
            rejected.append(dict(note, why='no usable confidence'))
            continue
        confidence = min(1.0, max(0.0, confidence))
        claims.append({'relation': relation, 'target': target,
                       'reference': text, 'confidence': round(confidence, 3)})

    names = {}
    for language, forms in (answer.get('names') or {}).items():
        if language not in LANGUAGES or not isinstance(forms, dict):
            continue
        kept = {form: value.strip() for form, value in forms.items()
                if form in FORMS and isinstance(value, str) and value.strip()}
        if kept.get('nominative'):
            names[language] = kept

    seen, ordered = set(), []
    for claim in claims:
        key = (claim['relation'], claim['target'])
        if key in seen:
            continue
        seen.add(key)
        ordered.append(claim)

    return {
        'entity': asked['entity'],
        'kind': asked['kind'],
        'claims': ordered,
        'names': names,
        'unresolved': sorted(set(unresolved)),
        'model': model,
        'askedAt': today,
    }, rejected


def ask(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)

    out_dir = os.path.join(args.dir, 'out')
    os.makedirs(out_dir, exist_ok=True)
    slugs = {e['slug'] for e in corpus(args.cache)['entities']}
    today = datetime.date.today().isoformat()
    lock = threading.Lock()
    totals = {'calls': 0, 'cost': 0.0, 'entities': 0, 'claims': 0, 'rejected': 0, 'failed': 0}
    rejected_path = os.path.join(args.dir, 'rejected.jsonl')

    with open(os.path.join(args.dir, 'asked.json'), 'w', encoding='utf-8') as handle:
        json.dump({'prompt_version': PROMPT_VERSION, 'model': args.model,
                   'effort': args.effort, 'asked': today},
                  handle, ensure_ascii=False, indent=1)

    pending = [entry for entry in manifest['batches']
               if args.again or not os.path.exists(os.path.join(out_dir, entry['batch'] + '.jsonl'))]
    if not pending:
        print('every batch already has answers. Pass --again to run them anyway.')
        return

    def run(entry):
        name = entry['batch']
        with open(os.path.join(args.dir, 'batches', name + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        asked = {e['entity']: e for e in payload['entities']}

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort)
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{name}: {failed}')
            return

        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        answers = parse(outcome.get('result'))
        if answers is None:
            with lock:
                totals['failed'] += 1
                print(f'{name}: no JSON array in the reply')
            return

        rows, rejects, seen = [], [], set()
        for answer in answers:
            if not isinstance(answer, dict):
                continue
            slug = answer.get('entity')
            if slug not in asked or slug in seen:
                continue
            seen.add(slug)
            row, bad = validate(answer, asked[slug], slugs, today, model)
            rows.append(row)
            rejects.extend(dict(r, batch=name) for r in bad)

        with open(os.path.join(out_dir, name + '.jsonl'), 'w', encoding='utf-8') as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        with lock:
            if rejects:
                with open(rejected_path, 'a', encoding='utf-8') as handle:
                    for reject in rejects:
                        handle.write(json.dumps(reject, ensure_ascii=False) + '\n')
            totals['calls'] += 1
            totals['cost'] += outcome.get('total_cost_usd') or 0.0
            totals['entities'] += len(rows)
            totals['claims'] += sum(len(r['claims']) for r in rows)
            totals['rejected'] += len(rejects)
            missing = len(asked) - len(rows)
            print(f'{name}: {len(rows)}/{len(asked)} entities, '
                  f'{sum(len(r["claims"]) for r in rows)} claims, {len(rejects)} rejected'
                  + (f', {missing} unanswered' if missing else '')
                  + f', {time.time() - started:.0f}s, ${outcome.get("total_cost_usd") or 0:.4f}')

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    print(f'{totals["calls"]} calls, {totals["entities"]} entities, {totals["claims"]} claims, '
          f'{totals["rejected"]} claims rejected, {totals["failed"]} batches failed, '
          f'${totals["cost"]:.4f} reported by the harness')


def descriptors(directory):
    out_dir = os.path.join(directory, 'out')
    rows = []
    if not os.path.isdir(out_dir):
        return rows
    for name in sorted(os.listdir(out_dir)):
        if not name.endswith('.jsonl'):
            continue
        with open(os.path.join(out_dir, name), encoding='utf-8') as handle:
            rows.extend(json.loads(line) for line in handle if line.strip())
    return rows


SCORABLE = set(KEY_RELATIONS.values())

# The same fact said from the other end. A descriptor is written from its own subject's side, so
# BibleData's `bani-4 ancestor-of adaiah-6` -- Bani the head of a clan in Ezra 10 -- is answered by
# the claim on Adaiah, not on Bani: `adaiah-6 descendant-of bani-4`. Scored one-directionally that
# reads as twenty-two misses, and every one of them is the encyclopedia saying the thing correctly
# in the only place a reader would look for it.
INVERSE = {
    'son-of': {'father-of', 'mother-of'},
    'daughter-of': {'father-of', 'mother-of'},
    'father-of': {'son-of', 'daughter-of'},
    'mother-of': {'son-of', 'daughter-of'},
    'brother-of': {'brother-of', 'sister-of'},
    'sister-of': {'brother-of', 'sister-of'},
    'husband-of': {'wife-of'},
    'wife-of': {'husband-of'},
    'ancestor-of': {'descendant-of'},
    'descendant-of': {'ancestor-of'},
    'father-in-law-of': {'son-in-law-of', 'daughter-in-law-of'},
    'mother-in-law-of': {'son-in-law-of', 'daughter-in-law-of'},
    'son-in-law-of': {'father-in-law-of', 'mother-in-law-of'},
    'daughter-in-law-of': {'father-in-law-of', 'mother-in-law-of'},
    'half-brother-of': {'half-brother-of', 'half-sister-of'},
    'half-sister-of': {'half-brother-of', 'half-sister-of'},
    'grandfather-of': {'grandson-of', 'granddaughter-of'},
    'grandmother-of': {'grandson-of', 'granddaughter-of'},
    'grandson-of': {'grandfather-of', 'grandmother-of'},
    'granddaughter-of': {'grandfather-of', 'grandmother-of'},
    'uncle-of': {'nephew-of', 'niece-of'},
    'aunt-of': {'nephew-of', 'niece-of'},
    'nephew-of': {'uncle-of', 'aunt-of'},
    'niece-of': {'uncle-of', 'aunt-of'},
    'brother-in-law-of': {'brother-in-law-of', 'sister-in-law-of'},
    'sister-in-law-of': {'brother-in-law-of', 'sister-in-law-of'},
    'servant-of': {'master-of'},
    'master-of': {'servant-of'},
    'killed-by': {'killer-of'},
    'killer-of': {'killed-by'},
    'cousin-of': {'cousin-of'},
    'disciple-of': {'teacher-of'},
    'teacher-of': {'disciple-of'},
    'ally-of': {'ally-of'},
    'raper-of': {'raped-by'},
    'raped-by': {'raper-of'},
}


def score(args):
    rows = descriptors(args.dir)
    if not rows:
        raise SystemExit(f'{args.dir}/out is empty. Run "ask --dir {args.dir}" first.')
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    asked_path = os.path.join(args.dir, 'asked.json')
    asked = {}
    if os.path.exists(asked_path):
        with open(asked_path, encoding='utf-8') as handle:
            asked = json.load(handle)
    offered = {}
    for entry in manifest['batches']:
        with open(os.path.join(args.dir, 'batches', entry['batch'] + '.json'), encoding='utf-8') as handle:
            for payload in json.load(handle)['entities']:
                offered[payload['entity']] = {c['slug'] for c in payload['candidates']}

    ours, mine = {}, {}
    for row in rows:
        for claim in row['claims']:
            mine.setdefault((row['entity'], claim['target']), []).append(claim)
            if claim['relation'] in SCORABLE:
                ours.setdefault((row['entity'], claim['target']), []).append(claim)

    covered = {row['entity'] for row in rows}
    key, unmapped = {}, {}
    for entry in answer_key():
        if entry['from'] not in covered:
            continue
        relation = KEY_RELATIONS.get(entry['type'])
        if not relation:
            unmapped[entry['type']] = unmapped.get(entry['type'], 0) + 1
            continue
        key.setdefault((entry['from'], entry['to']), set()).add(relation)

    agree, inverse_agree, disagree, disagreements = 0, 0, 0, []
    offered_not_taken, not_offered = 0, 0
    missed = []
    for pair, relations in sorted(key.items()):
        wanted = set()
        for relation in relations:
            wanted |= INVERSE.get(relation, set())
        claims = ours.get(pair)
        back = ours.get((pair[1], pair[0]))
        if claims and {c['relation'] for c in claims} & relations:
            agree += 1
            continue
        if back and {c['relation'] for c in back} & wanted:
            inverse_agree += 1
            continue
        if claims:
            disagree += 1
            disagreements.append({
                'entity': pair[0], 'target': pair[1],
                'ours': [{'relation': c['relation'], 'reference': c['reference'],
                          'confidence': c['confidence']} for c in claims],
                'bibledata': sorted(relations),
            })
            continue
        if pair[1] in offered.get(pair[0], ()):
            offered_not_taken += 1
            missed.append({'entity': pair[0], 'target': pair[1], 'bibledata': sorted(relations),
                           'we_said': [c['relation'] for c in mine.get(pair, [])],
                           'we_said_of_the_target': [c['relation'] for c in mine.get((pair[1], pair[0]), [])]})
        else:
            not_offered += 1

    extension = sum(1 for pair in ours if pair not in key and (pair[1], pair[0]) not in key)
    decided = agree + inverse_agree + disagree

    by_kind, forms = {}, {}
    for row in rows:
        counts = by_kind.setdefault(row['kind'], {'entities': 0, 'claims': 0, 'unresolved': 0,
                                                  'no_claims': 0, 'shown': 0})
        counts['entities'] += 1
        counts['claims'] += len(row['claims'])
        counts['unresolved'] += len(row['unresolved'])
        counts['no_claims'] += 0 if row['claims'] else 1
        counts['shown'] += sum(1 for c in row['claims'] if c['confidence'] >= 0.70)
        for language, values in row['names'].items():
            tally = forms.setdefault(language, {form: 0 for form in FORMS})
            for form in FORMS:
                tally[form] += 1 if values.get(form) else 0

    relations_used = {}
    for row in rows:
        for claim in row['claims']:
            relations_used[claim['relation']] = relations_used.get(claim['relation'], 0) + 1

    rejects = []
    rejected_path = os.path.join(args.dir, 'rejected.jsonl')
    if os.path.exists(rejected_path):
        with open(rejected_path, encoding='utf-8') as handle:
            rejects = [json.loads(line) for line in handle if line.strip()]
    why = {}
    for reject in rejects:
        why[reject['why']] = why.get(reject['why'], 0) + 1

    lines, write = [], None
    write = lines.append
    write('# Descriptor claims against BibleData')
    write('')
    write(f'{len(rows)} entities, {sum(len(r["claims"]) for r in rows)} claims, '
          f'{sum(len(r["unresolved"]) for r in rows)} unresolved names, '
          f'prompt {asked.get("prompt_version", manifest["prompt_version"])}, '
          f'model {rows[0]["model"]}, asked {rows[0]["askedAt"]}.')
    write('')
    write('## Agreement, over pairs both sides speak about')
    write('')
    write('| | count |')
    write('|---|---|')
    write(f'| agree, said of this entity | {agree} |')
    write(f'| agree, said of the target instead | {inverse_agree} |')
    write(f'| disagree | {disagree} |')
    write(f'| **decided** | **{decided}** |')
    write(f'| **agreement** | **{100.0 * (agree + inverse_agree) / decided:.2f}%** |' if decided
          else '| **agreement** | - |')
    write('')
    write('## Where only BibleData speaks')
    write('')
    write(f'- {offered_not_taken} pairs where the target **was offered** as a candidate and no '
          f'claim was made about it')
    write(f'- {not_offered} pairs where the target was never offered -- no verse shown to the '
          f'entity holds it')
    write('')
    write(f'{extension} pairs are ours alone: a claim BibleData does not state. Uncheckable here.')
    write('')
    write('## What BibleData states that the vocabulary cannot express')
    write('')
    for kind, count in sorted(unmapped.items(), key=lambda item: -item[1]):
        write(f'- {count} `{kind}`')
    if not unmapped:
        write('- nothing')
    write('')
    write('## By kind')
    write('')
    write('| kind | entities | claims | claims/entity | at or above 0.70 | with no claim | unresolved |')
    write('|---|---|---|---|---|---|---|')
    for kind, counts in sorted(by_kind.items()):
        write(f'| {kind} | {counts["entities"]} | {counts["claims"]} | '
              f'{counts["claims"] / counts["entities"]:.1f} | {counts["shown"]} | '
              f'{counts["no_claims"]} | {counts["unresolved"]} |')
    write('')
    write('## Name forms')
    write('')
    places = sum(1 for row in rows if row['kind'] == 'place')
    write('| language | nominative | genitive | locative (of any) | locative (of places) |')
    write('|---|---|---|---|---|')
    for language in LANGUAGES:
        tally = forms.get(language, {form: 0 for form in FORMS})
        placed = sum(1 for row in rows if row['kind'] == 'place'
                     and (row['names'].get(language) or {}).get('locative'))
        write(f'| {language} | {tally["nominative"]}/{len(rows)} | {tally["genitive"]}/{len(rows)} '
              f'| {tally["locative"]}/{len(rows)} | {placed}/{places} |')
    write('')
    write('## Relations used')
    write('')
    for relation, count in sorted(relations_used.items(), key=lambda item: -item[1]):
        write(f'- {count} `{relation}`')
    write('')
    write('## Claims dropped in validation')
    write('')
    for reason, count in sorted(why.items(), key=lambda item: -item[1]):
        write(f'- {count} {reason}')
    if not why:
        write('- none')
    write('')
    write('## Disagreements')
    write('')
    for item in disagreements[:args.disagreements]:
        ours_said = ', '.join(f'{c["relation"]} ({c["reference"]}, {c["confidence"]})'
                              for c in item['ours'])
        write(f'- **{item["entity"]}** -> **{item["target"]}**: ours {ours_said}; '
              f'BibleData {", ".join(item["bibledata"])}')
    if len(disagreements) > args.disagreements:
        write(f'- ... and {len(disagreements) - args.disagreements} more, in disagreements.json')

    report = '\n'.join(lines) + '\n'
    with open(os.path.join(args.dir, 'score.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    with open(os.path.join(args.dir, 'disagreements.json'), 'w', encoding='utf-8') as handle:
        json.dump({'disagreements': disagreements, 'offered_and_not_taken': missed},
                  handle, ensure_ascii=False, indent=1)
    print(report)


def improves(now, before):
    """
    Whether a re-asked record says more than the one it would supersede.

    A re-ask is not free of risk: the loader takes the later file whole, so a second answer with
    fewer claims than the first makes the page worse. It earns its place by filling a silence, by
    using one of the relations the vocabulary did not have, or by not shrinking.
    """
    if before is None or not before['claims']:
        return True
    if any(claim['relation'] in WIDENED for claim in now['claims']):
        return True
    return len(now['claims']) >= len(before['claims'])


def standing(directory):
    """
    What the published files already say, by entity, settled the way the loader settles it.

    `descriptors` reads a run, whose records sit under `out/`; these sit in the directory itself,
    one file per published batch, and the file that sorts last wins because that is the rule
    `DescriptorFiles.Read` applies.
    """
    rows = {}
    if not os.path.isdir(directory):
        return rows
    for name in sorted(name for name in os.listdir(directory) if name.endswith('.jsonl')):
        with open(os.path.join(directory, name), encoding='utf-8') as handle:
            for line in handle:
                if line.strip():
                    row = json.loads(line)
                    rows[row['entity']] = row
    return rows


def cases(row):
    return {(language, case)
            for language, forms in (row.get('names') or {}).items() for case in forms}


def carried(now, before):
    """
    A record as it should be published: its own answer, and any name form the record it replaces
    already had and it did not.

    The loader takes the later file whole -- it forgets this pass's forms for the entity and writes
    the new record's -- so a language the second answer happens to leave out is a form the reader
    loses on the next boot. Being asked again is not evidence that the earlier form was wrong; the
    second answer is to a different question, and 39 records of the first run to be measured this
    way dropped a case the standing record held.
    """
    if before is None:
        return now
    names = {language: dict(forms) for language, forms in (now.get('names') or {}).items()}
    for language, forms in (before.get('names') or {}).items():
        kept = names.setdefault(language, {})
        for case, value in forms.items():
            kept.setdefault(case, value)
    return dict(now, names=names)


def says_more(now, before):
    """Whether replacing what is published gains a reader anything: a clause, or a form."""
    return before is None or bool(now['claims']) or bool(cases(now) - cases(before))


def publish(args):
    rows = descriptors(args.dir)
    if not rows:
        raise SystemExit(f'{args.dir}/out is empty. Run "ask --dir {args.dir}" first.')
    earlier = {row['entity']: row for row in descriptors(args.against)} if args.against else {}
    published = standing(args.to)
    os.makedirs(args.to, exist_ok=True)
    out_dir = os.path.join(args.dir, 'out')
    written, entities, claims, withheld, silent, forms = 0, 0, 0, 0, 0, 0
    for name in sorted(name for name in os.listdir(out_dir) if name.endswith('.jsonl')):
        with open(os.path.join(out_dir, name), encoding='utf-8') as handle:
            keep = [json.loads(line) for line in handle if line.strip()]
        if args.against:
            asked_for = len(keep)
            keep = [row for row in keep if improves(row, earlier.get(row['entity']))]
            withheld += asked_for - len(keep)

        # A record that adds neither a clause nor a form is a supersession that costs the reader
        # nothing and can only lose them something. What the run answered is in the run.
        asked_about = len(keep)
        keep = [row for row in keep if says_more(row, published.get(row['entity']))]
        silent += asked_about - len(keep)
        keep = [carried(row, published.get(row['entity'])) for row in keep]
        forms += sum(len(cases(row)) for row in keep)

        if not keep:
            continue
        target = os.path.join(args.to, f'{args.prefix}-{written:04d}.jsonl')
        with open(target, 'w', encoding='utf-8') as handle:
            for row in keep:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        written += 1
        entities += len(keep)
        claims += sum(len(row['claims']) for row in keep)
    print(f'{written} files, {entities} entities, {claims} claims, {forms} name forms -> {args.to}'
          + (f'; {withheld} records withheld because they said less than {args.against} already had'
             if args.against else '')
          + f'; {silent} records left unpublished because they add neither a clause nor a form')


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    parser.add_argument('--cache', default='.descriptors/cache',
                        help='where the database reads are kept between runs')
    commands = parser.add_subparsers(dest='command', required=True)

    extractor = commands.add_parser('extract', help='write the prompt payloads for a run')
    extractor.add_argument('--out', dest='dir', required=True)
    extractor.add_argument('--kinds', nargs='+', default=['person', 'place', 'people'])
    extractor.add_argument('--sample', type=int, help='draw about this many entities instead of all')
    extractor.add_argument('--seed', type=int, default=1)
    extractor.add_argument('--prefer-scorable', action='store_true',
                           help='weight the sample towards entities BibleData says something about')
    extractor.add_argument('--only',
                           help='a file of entity slugs, one per line: extract only these')
    extractor.add_argument('--context', action='store_true',
                           help='show the verses around a register line, for the entities whose '
                                'own verses are a bare list of names')
    extractor.add_argument('--batch-entities', type=int, default=BATCH_ENTITIES)
    extractor.add_argument('--batch-verses', type=int, default=BATCH_VERSES)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='run every batch that has no answers yet')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--workers', type=int, default=4)
    asker.add_argument('--effort', default=None,
                       help='low, medium, high, xhigh or max. The CLI thinks by default and the '
                            'bill is mostly thinking tokens; PRB-0437 measured low as cheaper and '
                            'no worse.')
    asker.add_argument('--again', action='store_true', help='re-run batches that already answered')
    asker.set_defaults(run=ask)

    scorer = commands.add_parser('score', help='measure the claims against BibleData')
    scorer.add_argument('--dir', required=True)
    scorer.add_argument('--disagreements', type=int, default=40)
    scorer.set_defaults(run=score)

    publisher = commands.add_parser('publish', help='copy the validated batches into Resources')
    publisher.add_argument('--dir', required=True)
    publisher.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'descriptors'))
    publisher.add_argument('--prefix', default='batch',
                           help='what the published files are named. The loader keeps the record '
                                'from the file that sorts last, so a run meant to supersede an '
                                'earlier one needs a prefix that sorts after it.')
    publisher.add_argument('--against',
                           help="an earlier run's directory: publish a record only where it says "
                                'more than that run already said about the same entity')
    publisher.set_defaults(run=publish)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
