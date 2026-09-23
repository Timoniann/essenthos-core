"""
Which of the men called Zechariah is this verse about? Assign every occurrence of a shared name to
one of the bearers the lexicon enumerates, and publish the result as a register.

528 proper names are carried by more than one entity, and until now the count was borrowed: 3,009
persons exist because BibleData drew the lines that way. Strong drew them too, and wrote his out.
`strong_entry.detailed_definition` is a numbered list of the bearers with a distinguishing clause on
each -- *2) king of Israel, son of Jeroboam II* -- for 508 of the 528 groups, and the owner has
chosen the maximal grain that enumeration describes. So the enumeration is the spine of this pass
rather than a witness scored against it, and what is left is assignment, which is the shape the
sense pass reached 99.00% on.

    python scripts/persons.py census                          # the three witnesses, no model in it
    python scripts/persons.py extract --out .persons/run      # all 528, payloads with the spine
    python scripts/persons.py ask     --dir .persons/run --effort medium --workers 5
    python scripts/persons.py check   --dir .persons/run --effort medium --workers 5
    python scripts/persons.py register --dir .persons/run     # the bearers decided, ours matched
    python scripts/persons.py publish  --dir .persons/run --to Resources/Essenthos/persons
    python scripts/persons.py score    --dir .persons/run     # agreement, cost, wall clock

Five things about the design are load-bearing:

**The enumeration is in the prompt and BibleData is not.** The lexicon proposes the bearers; the
model says which verse is whose. BibleData's count is kept out, so agreement against it stays a
measurement rather than an echo -- which is the only reason `score` can be read at all.

**A verse in an entity's list is not a verse that names it.** `entity_verse` claims *this entity is
mentioned here*, which is looser: Zimri's sixty verses print his name twelve times and the rest
narrate a coup he is a byword in. So every occurrence is stamped `namesTheName` -- the King James
prints a spelling of the label, or a word of some text in that verse is annotated to an entity whose
own name is the label -- and no bearer may rest on an unnamed verse alone. 10,113 of the 12,908 name
their name; the other 2,795 are context and nothing more.

**The occurrence budget takes the named verses first, and every book of them.** The pilot capped at
the head of the list and lost Matthew's Jacob, which under a maximal register is a person rather
than a nuance. The budget now sorts named before unnamed, keeps the first and last named occurrence
of every book whole, and samples what is left.

**The model never touches the database.** It is given a payload and returns JSON. Every answer is
written to a file with the model, the effort level, the prompt version and the date, because it is a
claim about a reading and not a fact about the corpus.

**A bearer no verse establishes is read twice.** Under a maximal grain some bearers stand on the
lexicon's say-so and no verse of ours, and that is the owner's decision. What is not decided is
whether such an item is a bearer at all: Strong's enumerations hold places, peoples and the same man
twice under two spellings, and each of those would enter a register of persons as somebody who never
existed. So every lexicon-only bearer is asked again, under a prompt that asks the opposite question.

A run leaves:

    manifest.json           every group, its witnesses, and what its payload holds
    batches/<label>.json    the prompt payload, exactly as the model saw it
    answers/<label>.json    the reading, with the harness's own cost and wall clock beside it
    check/<label>.json      the second reading of that group's lexicon-only bearers
    register.json           every bearer decided, kept or refused, with the reason
    register.md             what the register holds, and what it does to the encyclopedia
    score.md                agreement, cost, wall clock, payload composition
    disagreements.json      every group where the witnesses do not all say the same

The run directory is not committed -- it is reproducible from here, and what it is kept for is
published under `Resources/Essenthos/persons` instead.
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

# The rebuild's own Postgres. The frozen API's container still holds an older copy of this database
# under the same name, so a wrong container here answers every query and is wrong only in the data.
CONTAINER = os.environ.get('ESSENTHOS_DB_CONTAINER', 'essenthos-core-db-1')
DATABASE = 'essenthos_core'
USER = 'essenthos'
# Every session is read-only on the server's side, so a statement that would write fails instead.
READ_ONLY = 'PGOPTIONS=-c default_transaction_read_only=on'

RENDERING = 'KJV'

PROMPT_VERSION = 'persons-2'
CHECK_VERSION = 'persons-check-1'

# The joins over `word` run out of shared memory with a gather on this machine, and the planner
# picks one for anything touching the seven million rows.
NO_GATHER = 'set max_parallel_workers_per_gather=0;\n'

# A namesake group is a `proper name` label carried by more than one entity. Titles, gentilics and
# descriptions share labels for reasons that are not namesakes -- `king of Judah` is eighteen men
# because it is an office -- and reading them here would measure the wrong thing.
NAME_KIND = 'proper name'

# Matthew, in the canonical ordinals `entity_verse` and `verse_reference` are written in.
FIRST_GREEK_BOOK = 40

# The verse budget for one group, and how many of a book's verses are held back from the sampling.
#
# The pilot took the first 400 addresses in canonical order, so Jacob's budget ended in Genesis and
# Matthew's Jacob never reached the model. Under a maximal register that does not lose a nuance, it
# loses a person. The budget is now spent on the verses that print the name first, keeps the first
# and last of those in every book whole, and only then fills with the rest -- so a bearer who
# appears once, late, in one book cannot fall outside it.
OCCURRENCES_SHOWN = 800
BOOK_ANCHORS = 2

# The size bands cost is reported over, by distinct verses in the group. The population is
# 304 / 103 / 72 / 26 / 23, and one mean over that would be dominated by the tail.
BANDS = [('1-9', 1, 9), ('10-19', 10, 19), ('20-49', 20, 49), ('50-99', 50, 99), ('100+', 100, None)]

# Strong writes his counts as words. `a` and `an` are counts too -- *the name of an Israelite* says
# one -- and `several` is a refusal to give one.
NUMBER_WORDS = {
    'a': 1, 'an': 1, 'one': 1, 'two': 2, 'three': 3, 'four': 4, 'five': 5, 'six': 6, 'seven': 7,
    'eight': 8, 'nine': 9, 'ten': 10, 'eleven': 11, 'twelve': 12, 'thirteen': 13, 'fourteen': 14,
    'fifteen': 15, 'sixteen': 16, 'seventeen': 17, 'eighteen': 18, 'nineteen': 19, 'twenty': 20,
    'twenty-one': 21, 'twenty-two': 22, 'twenty-three': 23, 'twenty-four': 24, 'twenty-five': 25,
    'twenty-six': 26, 'twenty-seven': 27, 'twenty-eight': 28, 'twenty-nine': 29, 'thirty': 30,
}

# Strong states a count in three shapes, not one: *the name of two Israelites*, *the name of a
# king of Israel and of a prophet at Babylon*, *a place in Palestine, also of an Israelite*.
# Reading only the first under-counts Ahab, Hezron, Rezin and eighty others by exactly the
# bearers the second clause adds, and every one of those shows up as a disagreement.
STATED_COUNT = re.compile(r'(?:name|also|and) of (?:at least )?([A-Za-z\-]+)')
ENUMERATED_BEARER = re.compile(r'^\s*(\d+)\)', re.M)

BOOKS = [
    'GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH',
    '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK',
    'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL',
    'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH',
    '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD',
    'REV',
]

SYSTEM_PROMPT = """\
You are a Biblical scholar assigning the occurrences of one name to the men who bear it.

You are given one name, its Hebrew and Greek forms, the entries a nineteenth-century lexicon heads
under it, the bearers that lexicon enumerates, the King James text of every verse this corpus
attests the name in, and a pool of short claims already read out of those verses -- a father, a
brother, an office, a place -- each with the verse it was read from.

The enumeration is the spine. It is a maximal list: it separates two mentions wherever they could be
two men, and this register keeps that grain deliberately. Your work is assignment, not proposal.

- Keep one bearer for every numbered item of the enumeration, in its order. Where the verses do not
  distinguish two of them, keep both anyway and leave one with no references. An empty reference
  list is the honest answer and this register records it as such; merging them is not.
- An item that is not a distinct man of this name is still reported, with `kind` saying what it is
  instead -- a place, a region, a people or a tribe -- or with `same_as` naming the item it repeats
  under another spelling. Never drop one silently.
- Add a bearer the enumeration does not list only where the verses require one.
- `namesTheName` says whether the King James verse prints a spelling of the name. A verse that does
  not print it may be attached to a bearer already established by one that does; it may never be a
  bearer's only evidence. Where it belongs to nobody in particular, leave it unassigned.
- Every reference must be one you were given, and goes to one bearer at most. A verse naming two
  different men of this name goes to the first of them and is mentioned in `note`.

Answer with a single JSON object and nothing else:

{
  "name":       the name you were given, unchanged
  "people":     an array, one object per bearer, in the enumeration's order and then yours:
                {
                  "id":          1, 2, 3 ... in order
                  "enumerated":  the lexicon item number this is, or null where you added it
                  "kind":        "person", "place", "people" or "other"
                  "same_as":     the id of the bearer this item repeats, or null
                  "description": under 20 words, what tells this one from the others
                  "references":  the references from `occurrences` that speak of this one
                  "confidence":  "high", "medium" or "low"
                }
  "unassigned": references you could not attach to any bearer, as an array
  "note":       one line, under 30 words, on what was hard about this name -- or null
}
"""

CHECK_PROMPT = """\
You read a lexicon's enumeration of the bearers of one name. Each item below has been proposed as a
distinct man of that name, and no verse of this corpus was assigned to it, so the item is all the
evidence there is. Your job is to say whether the enumeration bears that out.

Answer with a JSON array, one object per item, in the order given, and nothing else:

  {"key": "...", "person": true, "same_as": null, "why": "..."}

  person   -- true only if the item names a distinct human being bearing this name. False for a
              place, a region, a mountain, a river or a site; false for a people, a tribe or a clan;
              false where the item is a gloss on the name or on its etymology rather than a bearer
              of it; false where another item on the same list is plainly the same man under another
              spelling, another office or another patronymic.
  same_as  -- the key of the item this one repeats, where that is why `person` is false. Otherwise
              null.
  why      -- one short clause, quoting the words of the item that decide it.

Judge the items as written and against each other. Do not bring knowledge from outside them, and do
not treat the absence of a verse as a reason: this corpus's verse lists are incomplete, which is why
you are being asked.
"""


def psql(sql):
    """One JSON value out of the live database. Read-only by construction: nothing here writes."""
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', '-e', READ_ONLY, CONTAINER,
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


GROUPS = f"""
    WITH grouped AS (
        SELECT n.label
        FROM entity_name n
        WHERE n.kind = '{NAME_KIND}'
        GROUP BY n.label
        HAVING count(DISTINCT n.entity_id) > 1
    ),
    member AS (
        SELECT DISTINCT n.label, n.entity_id
        FROM entity_name n JOIN grouped g ON g.label = n.label
        WHERE n.kind = '{NAME_KIND}'
    )
"""


def population():
    """Every namesake group, with the witnesses' raw material and the size the report is banded on."""
    return psql(GROUPS + f"""
        , per AS (
            SELECT m.label,
                   count(DISTINCT m.entity_id) AS entities,
                   (SELECT coalesce(json_agg(DISTINCT e.kind), '[]')
                    FROM member i JOIN entity e ON e.id = i.entity_id
                    WHERE i.label = m.label) AS kinds,
                   (SELECT coalesce(json_agg(DISTINCT n.hebrew_strong_number), '[]')
                    FROM member i JOIN entity_name n ON n.entity_id = i.entity_id
                    WHERE i.label = m.label AND n.label = m.label
                      AND n.hebrew_strong_number IS NOT NULL) AS hebrew,
                   (SELECT coalesce(json_agg(DISTINCT n.greek_strong_number), '[]')
                    FROM member i JOIN entity_name n ON n.entity_id = i.entity_id
                    WHERE i.label = m.label AND n.label = m.label
                      AND n.greek_strong_number IS NOT NULL) AS greek,
                   (SELECT count(DISTINCT (ev.canonical_book, ev.canonical_chapter,
                                           ev.canonical_verse))
                    FROM member i JOIN entity_verse ev ON ev.entity_id = i.entity_id
                    WHERE i.label = m.label) AS verses,
                   (SELECT count(DISTINCT d.entity_id)
                    FROM member i JOIN entity_descriptor d ON d.entity_id = i.entity_id
                    WHERE i.label = m.label) AS described,
                   (SELECT CASE WHEN count(*) FILTER (WHERE ev.canonical_book < {FIRST_GREEK_BOOK}) = 0
                                     THEN 'nt'
                                WHEN count(*) FILTER (WHERE ev.canonical_book >= {FIRST_GREEK_BOOK}) = 0
                                     THEN 'ot'
                                ELSE 'mixed' END
                    FROM member i JOIN entity_verse ev ON ev.entity_id = i.entity_id
                    WHERE i.label = m.label) AS testament
            FROM member m GROUP BY m.label
        )
        SELECT coalesce(json_agg(json_build_object(
                   'label', label, 'entities', entities, 'kinds', kinds,
                   'hebrew', hebrew, 'greek', greek, 'verses', verses, 'described', described,
                   'testament', testament)
                   ORDER BY label), '[]')
        FROM per
    """)


def material():
    """The name forms, the occurrences and the clause pool, for every group."""
    return psql(GROUPS + """
        , per AS (
            SELECT c.label,
                   (SELECT coalesce(json_agg(DISTINCT jsonb_build_object(
                        'hebrew', n.hebrew, 'hebrew_transliterated', n.hebrew_transliterated,
                        'greek', n.greek, 'greek_transliterated', n.greek_transliterated,
                        'meaning', n.meaning)), '[]')
                    FROM member i JOIN entity_name n ON n.entity_id = i.entity_id
                    WHERE i.label = c.label AND n.label = c.label) AS forms,
                   (SELECT coalesce(json_agg(DISTINCT jsonb_build_array(
                        ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)), '[]')
                    FROM member i JOIN entity_verse ev ON ev.entity_id = i.entity_id
                    WHERE i.label = c.label) AS occurrences,
                   (SELECT coalesce(json_agg(jsonb_build_array(
                        d.relation, t.name, d.canonical_book, d.canonical_chapter,
                        d.canonical_verse, d.confidence)), '[]')
                    FROM member i
                    JOIN entity_descriptor d ON d.entity_id = i.entity_id
                    JOIN entity t ON t.id = d.target_entity_id
                    WHERE i.label = c.label) AS clauses
            FROM (SELECT DISTINCT label FROM member) c
        )
        SELECT coalesce(json_agg(json_build_object(
                   'label', label, 'forms', forms, 'occurrences', occurrences, 'clauses', clauses)
                   ORDER BY label), '[]')
        FROM per
    """)


def annotated_verses():
    """
    The verses where a word of some text is annotated to an entity whose own name is the group's
    label -- this corpus's own answer to *does this verse name this name*, beside the King James
    spelling.

    Restricted to entities the label names outright, because a group also holds the men who merely
    carry the label as a second name: Jezebel calls Jehu *Zimri* once in 2 Kings 9, so Jehu is a
    member of the Zimri group and forty-eight of his verses are in its occurrence list.
    """
    return psql(GROUPS + """
        , hit AS (
            SELECT DISTINCT m.label, r.canonical_book AS b, r.canonical_chapter AS c,
                   r.canonical_verse AS v
            FROM member m
            JOIN entity e ON e.id = m.entity_id AND e.name = m.label
            JOIN word_entity we ON we.entity_id = m.entity_id
            JOIN word w ON w.id = we.word_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        )
        SELECT coalesce(json_agg(json_build_array(label, b, c, v)), '[]') FROM hit
    """)


def held_persons():
    """
    Every entity a bearer of a name could be, with the verses it is attested in.

    Two ways in, and the second is not a nicety. A group is built from `entity_name`, and the 181
    records this corpus writes for itself carry no name row at all — 180 of the 181 — so
    `zechariah-the-trumpeting-priest` is invisible to the group and the register would add a second
    page for a man it already holds. An entity whose own name is the label is a candidate too, and
    the verses still decide.
    """
    return psql(GROUPS + """
        , candidate AS (
            SELECT DISTINCT label, entity_id FROM member
            UNION
            SELECT g.label, e.id FROM grouped g JOIN entity e ON e.name = g.label
        )
        , per AS (
            SELECT m.label, e.id, e.slug, e.name, e.distinguisher, e.kind, e.source,
                   e.source_id,
                   (SELECT coalesce(json_agg(DISTINCT jsonb_build_array(
                        ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)), '[]')
                    FROM entity_verse ev WHERE ev.entity_id = e.id) AS verses
            FROM candidate m
            JOIN entity e ON e.id = m.entity_id
        )
        SELECT coalesce(json_agg(json_build_object(
                   'label', label, 'id', id, 'slug', slug, 'name', name,
                   'distinguisher', distinguisher, 'kind', kind, 'source', source,
                   'sourceId', source_id, 'verses', verses)), '[]')
        FROM per
    """)


def lexicon(numbers):
    """Strong's entries for the groups' numbers, `detailed_definition` included."""
    listed = ', '.join("'" + number + "'" for number in sorted(numbers))
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'strong_number', strong_number, 'lemma', lemma,
                   'transliteration', transliteration, 'definition', definition,
                   'derivation', derivation, 'detailed_definition', detailed_definition)), '[]')
        FROM strong_entry WHERE strong_number IN ({listed})
    """)


def rendering():
    """The King James, keyed by canonical address. Words joined with their trailers, as read."""
    return psql(f"""
        SELECT coalesce(json_agg(json_build_array(x.b, x.c, x.v, x.line)), '[]')
        FROM (
            SELECT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v,
                   string_agg(w.text || w.trailer, '' ORDER BY w.position) AS line
            FROM verse ve
            JOIN verse_reference r ON r.verse_id = ve.id AND r.is_primary
            JOIN word w ON w.verse_id = ve.id
            WHERE ve.text_id = (SELECT id FROM text WHERE upper(slug) = '{RENDERING}')
            GROUP BY 1, 2, 3
        ) x
    """)


def numbers_of(group):
    """
    The Strong numbers a group's witnesses are read from.

    The Hebrew, except where the name is only ever borne in the New Testament. BibleData records
    the Hebrew number on a Greek name because Ananias is Hananiah and James is Jacob, and read as a
    witness that puts *the name of thirteen Israelites* against the three men Acts calls Ananias.
    Twenty groups are Greek only; seventy-five are named in both testaments, and for those neither
    entry counts the same men, which `score` says rather than hides.
    """
    if group.get('testament') == 'nt' and group['greek']:
        return sorted(group['greek'])
    return sorted(group['hebrew']) or sorted(group['greek'])


def stated_count(entries):
    """
    Strong's own arithmetic. A group spanning several numbers is several names spelt alike -- Ahi is
    H277 and H278 -- so the counts add rather than replacing one another.
    """
    total, said = 0, False
    for entry in entries:
        for match in STATED_COUNT.finditer(entry.get('definition') or ''):
            if match.group(1) in NUMBER_WORDS:
                total += NUMBER_WORDS[match.group(1)]
                said = True
    return total if said else None


def aliases(entry):
    """
    The English spellings the long definition opens with: *Jonathan or Jehonathan = "Jehovah has
    given"*. Two Strong numbers whose headwords share one of these are two spellings of one name.
    """
    head = (entry.get('detailed_definition') or '').split(chr(10))[0].split('=')[0]
    return {part.strip().lower() for part in head.split(' or ') if part.strip()}


def items_of(entry):
    """The numbered clauses of one entry's long definition, each with the number Strong gave it."""
    text = entry.get('detailed_definition') or ''
    found = list(ENUMERATED_BEARER.finditer(text))
    items = []
    for at, match in enumerate(found):
        end = found[at + 1].start() if at + 1 < len(found) else len(text)
        items.append({'item': int(match.group(1)),
                      'says': ' '.join(text[match.end():end].split()),
                      'strong_number': entry['strong_number']})
    return items


def clusters_of(entries):
    """
    A group's enumerations, merged where two Strong numbers are two spellings of one name.

    Enumerations add across a group's numbers where the numbers are different names spelt alike --
    Ahi is H277 and Ehi is H278 -- and do not where they are one name. H3083 and H3129 both open
    *Jonathan or Jehonathan* and both then list the same fifteen men, so adding them gives
    twenty-five Jonathans where the text has fifteen. The headword line is the discriminator, and
    within a cluster the longest list wins rather than the two being concatenated.
    """
    clusters = []
    for entry in entries:
        items = items_of(entry)
        if not items:
            continue
        names = aliases(entry)
        shared = next((at for at, (seen, _) in enumerate(clusters) if seen & names), None)
        if shared is None:
            clusters.append((names, items))
        elif len(items) > len(clusters[shared][1]):
            clusters[shared] = (clusters[shared][0] | names, items)
        else:
            clusters[shared] = (clusters[shared][0] | names, clusters[shared][1])
    return [items for _, items in clusters]


def enumeration(entries):
    """The bearers the lexicon lists, merged across the group's numbers and renumbered from one."""
    listed = [item for items in clusters_of(entries) for item in items]
    return [dict(item, item=at + 1) for at, item in enumerate(listed)]


def enumerated_count(entries):
    """
    How many bearers the enumeration lists: the highest number Strong writes in each cluster rather
    than how many clauses were parsed, so a gap in his numbering stays his count and not the
    parser's.
    """
    clusters = clusters_of(entries)
    if not clusters:
        return None
    return sum(max(item['item'] for item in items) for items in clusters)


def band_of(verses):
    for name, low, high in BANDS:
        if verses >= low and (high is None or verses <= high):
            return name
    return BANDS[0][0]


def folded(text):
    return re.sub(r'[^a-z0-9]', '', (text or '').lower())


def shown_occurrences(addresses, named):
    """
    Which of a group's occurrences go into the prompt when there are more than the budget.

    The named ones first, because only a verse that prints the name can establish a bearer; the
    first and last named occurrence of every book kept whole, because a bearer who appears once,
    late, in one book is exactly what the pilot lost; then the rest of the named ones evenly
    sampled, and the unnamed ones last, as context.
    """
    if len(addresses) <= OCCURRENCES_SHOWN:
        return list(addresses)

    speaking = [a for a in addresses if a in named]
    silent = [a for a in addresses if a not in named]

    per_book = collections.OrderedDict()
    for address in speaking:
        per_book.setdefault(address[0], []).append(address)
    kept = list(dict.fromkeys(
        a for book in per_book.values() for a in book[:BOOK_ANCHORS] + book[-BOOK_ANCHORS:]))

    for pool in (speaking, silent):
        room = OCCURRENCES_SHOWN - len(kept)
        if room <= 0:
            break
        taken = set(kept)
        rest = [a for a in pool if a not in taken]
        step = max(1, len(rest) // room) if rest else 1
        kept += rest[::step][:room]
    return sorted(set(kept))[:OCCURRENCES_SHOWN]


def witnessed(groups, entries):
    for group in groups:
        held = [entries[n] for n in numbers_of(group) if n in entries]
        group['band'] = band_of(group['verses'])
        group['stated'] = stated_count(held)
        group['enumeration'] = enumeration(held)
        group['enumerated'] = enumerated_count(held)
        group['entries'] = held
    return groups


def extract(args):
    groups = population()
    entries = {e['strong_number']: e
               for e in lexicon({n for g in groups for n in numbers_of(g)})}
    witnessed(groups, entries)

    os.makedirs(os.path.join(args.out, 'batches'), exist_ok=True)
    lines = {(b, c, v): line for b, c, v, line in rendering()}
    annotated = collections.defaultdict(set)
    for label, b, c, v in annotated_verses():
        annotated[label].add((b, c, v))
    supplied = {m['label']: m for m in material()}

    for group in groups:
        label = group['label']
        spelt = folded(label)
        seen = supplied[label]
        occurrences = sorted(tuple(a) for a in seen['occurrences'])
        named = {a for a in occurrences
                 if a in annotated[label] or spelt in folded(lines.get(a, ''))}
        shown = shown_occurrences(occurrences, named)
        group['occurrences'] = len(occurrences)
        group['named'] = len(named)
        group['shown'] = len(shown)
        group['truncated'] = len(shown) < len(occurrences)

        held = [{'strong_number': entry['strong_number'], 'lemma': entry['lemma'],
                 'transliteration': entry['transliteration'], 'derivation': entry['derivation']}
                for entry in group['entries']]
        # Two entities of one name carry two name rows that differ only in invisible ways, and the
        # database's own DISTINCT keeps both; the model gains nothing from reading the name twice.
        forms = list({json.dumps(f, ensure_ascii=False, sort_keys=True): f
                      for f in seen['forms']}.values())
        payload = {
            'prompt_version': PROMPT_VERSION,
            'name': label,
            'forms': forms,
            'lexicon': held,
            'enumeration': [{'item': item['item'], 'says': item['says']}
                            for item in group['enumeration']],
            'occurrences': [{'reference': reference(*address),
                             'namesTheName': address in named,
                             'king_james': lines.get(address, '')} for address in shown],
            'clauses': sorted(({'relation': relation, 'target': target,
                                'reference': reference(b, c, v), 'confidence': confidence}
                               for relation, target, b, c, v, confidence in seen['clauses']),
                              key=lambda claim: claim['reference']),
        }
        with open(os.path.join(args.out, 'batches', label + '.json'), 'w',
                  encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)

    manifest = {
        'prompt_version': PROMPT_VERSION,
        'occurrences_shown': OCCURRENCES_SHOWN,
        'population': {name: sum(1 for g in groups if g['band'] == name) for name, _, _ in BANDS},
        'groups': [{k: g[k] for k in ('label', 'band', 'entities', 'verses', 'described',
                                      'stated', 'enumerated', 'enumeration', 'kinds', 'testament',
                                      'occurrences', 'named', 'shown', 'truncated')}
                   for g in groups],
    }
    with open(os.path.join(args.out, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    offered = sum(g['occurrences'] for g in groups)
    print(f'{len(groups)} groups, {offered:,} occurrences, '
          f'{sum(g["named"] for g in groups):,} of them naming the name '
          f'({sum(g["named"] for g in groups) / max(1, offered):.1%})')
    print(f'{sum(1 for g in groups if g["truncated"])} groups exceed the '
          f'{OCCURRENCES_SHOWN}-verse budget; '
          f'{sum(g["shown"] for g in groups):,} verse lines go into the prompts')
    print(f'{sum(1 for g in groups if g["stated"] is None)} with no stated count, '
          f'{sum(1 for g in groups if not g["enumeration"])} with no enumeration')


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
    One group, one call, one turn, no tools and no session. `--effort` is passed always and named in
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


def read_answers(directory, manifest):
    answers = {}
    for group in manifest['groups']:
        path = os.path.join(directory, 'answers', group['label'] + '.json')
        if os.path.exists(path):
            with open(path, encoding='utf-8') as handle:
                answers[group['label']] = json.load(handle)
    return answers


def read_checks(directory):
    checks = {}
    folder = os.path.join(directory, 'check')
    if not os.path.isdir(folder):
        return checks
    for name in sorted(name for name in os.listdir(folder) if name.endswith('.json')):
        with open(os.path.join(folder, name), encoding='utf-8') as handle:
            row = json.load(handle)
        checks[row['label']] = row
    return checks


def read_manifest(directory):
    with open(os.path.join(directory, 'manifest.json'), encoding='utf-8') as handle:
        return json.load(handle)


def ask(args):
    manifest = read_manifest(args.dir)
    answers_dir = os.path.join(args.dir, 'answers')
    os.makedirs(answers_dir, exist_ok=True)

    pending = [g for g in manifest['groups']
               if (not args.only or g['label'] in args.only)
               and (args.again
                    or not os.path.exists(os.path.join(answers_dir, g['label'] + '.json')))]
    if not pending:
        print('every group already has an answer. Pass --again to run them anyway.')
        return

    lock = threading.Lock()
    totals = {'calls': 0, 'cost': 0.0, 'seconds': 0.0, 'failed': 0, 'done': 0}
    asked = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    began = time.time()

    def run(group):
        label = group['label']
        with open(os.path.join(args.dir, 'batches', label + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        offered = {o['reference'] for o in payload['occurrences']}
        naming = {o['reference'] for o in payload['occurrences'] if o['namesTheName']}

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort, SYSTEM_PROMPT)
        elapsed = time.time() - started
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{label}: {failed}', flush=True)
            return

        answer = parse(outcome.get('result'))
        if answer is None:
            with lock:
                totals['failed'] += 1
                print(f'{label}: no JSON object in the reply', flush=True)
            return

        people, assigned, invalid = [], set(), 0
        for person in answer.get('people') or []:
            if not isinstance(person, dict):
                continue
            kept = []
            for ref in person.get('references') or []:
                if ref not in offered:
                    invalid += 1
                elif ref not in assigned:
                    assigned.add(ref)
                    kept.append(ref)
            people.append({'id': person.get('id'), 'enumerated': person.get('enumerated'),
                           'kind': person.get('kind'), 'same_as': person.get('same_as'),
                           'description': person.get('description'),
                           'confidence': person.get('confidence'),
                           'references': [r for r in kept if r in naming],
                           'other_references': [r for r in kept if r not in naming]})

        usage = outcome.get('usage') or {}
        row = {
            'label': label, 'people': people,
            'unassigned': [r for r in (answer.get('unassigned') or []) if r in offered],
            'note': answer.get('note'),
            'count': sum(1 for p in people
                         if p['kind'] in (None, 'person') and not p['same_as']),
            'proposed': len(people),
            'offered': len(offered), 'assigned': len(assigned), 'invalid_references': invalid,
            'prompt_version': payload['prompt_version'],
            'model': next(iter(outcome.get('modelUsage') or {'unknown': None})),
            'effort': args.effort, 'asked': asked, 'method': 'model-reading',
            'cost_usd': outcome.get('total_cost_usd') or 0.0,
            'seconds': round(elapsed, 1),
            'usage': {k: usage.get(k) for k in
                      ('input_tokens', 'output_tokens', 'cache_read_input_tokens',
                       'cache_creation_input_tokens')},
        }
        with open(os.path.join(answers_dir, label + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(row, handle, ensure_ascii=False, indent=1)

        with lock:
            totals['calls'] += 1
            totals['done'] += 1
            totals['cost'] += row['cost_usd']
            totals['seconds'] += elapsed
            print(f'[{totals["done"]}/{len(pending)}] {label}: {row["count"]} people, '
                  f'{len(assigned)}/{len(offered)} assigned'
                  + (f', {invalid} invented' if invalid else '')
                  + f', {elapsed:.0f}s, ${row["cost_usd"]:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    print(f'{totals["calls"]} calls, {totals["failed"]} failed, '
          f'${totals["cost"]:.4f} reported by the harness, {totals["seconds"]:.0f}s of model time '
          f'in {time.time() - began:.0f}s of wall clock at {args.workers} workers')


def lexicon_only(answer):
    """
    A group's bearers that rest on the enumeration alone: proposed, not declared a repeat of
    another, and with no verse of ours that names the name. These are what the second reading is
    for.
    """
    return [person for person in answer['people']
            if not person['references'] and not person.get('same_as')]


def check(args):
    """
    The second reading, over the bearers no verse establishes.

    That a maximal register admits a bearer on the lexicon's say-so is the owner's decision and not
    in question here. What is in question is whether such an item is a bearer at all: Strong's
    enumerations hold places (*4) the land of Uz*), peoples, and the same man twice under two
    spellings, and each would enter a register of persons as somebody who never existed. The prompt
    asks the opposite question from the first pass, the way the place register's second reading
    does, and it is asked group by group so the items can be weighed against each other.
    """
    manifest = read_manifest(args.dir)
    answers = read_answers(args.dir, manifest)
    if not answers:
        raise SystemExit(f'{args.dir}/answers holds no readings. Run "ask --dir {args.dir}" first.')

    folder = os.path.join(args.dir, 'check')
    os.makedirs(folder, exist_ok=True)

    wanted = {}
    for group in manifest['groups']:
        answer = answers.get(group['label'])
        standing = lexicon_only(answer) if answer else []
        if standing:
            wanted[group['label']] = (group, answer, standing)

    pending = [label for label in sorted(wanted)
               if (not args.only or label in args.only)
               and (args.again or not os.path.exists(os.path.join(folder, label + '.json')))]
    print(f'{sum(len(row[2]) for row in wanted.values())} bearers in {len(wanted)} groups stand on '
          f'the enumeration alone; {len(pending)} groups to ask.')
    if not pending:
        return

    lock = threading.Lock()
    totals = {'calls': 0, 'cost': 0.0, 'seconds': 0.0, 'failed': 0, 'done': 0}
    asked = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    began = time.time()

    def run(label):
        group, answer, standing = wanted[label]
        payload = {
            'prompt_version': CHECK_VERSION,
            'name': label,
            'enumeration': [{'item': item['item'], 'says': item['says']}
                            for item in group['enumeration']],
            'items': [{'key': f'{label}#{person["id"]}', 'enumerated': person.get('enumerated'),
                       'says': person.get('description'), 'kind': person.get('kind')}
                      for person in standing],
            'bearers_the_verses_establish': [
                {'key': f'{label}#{person["id"]}', 'says': person.get('description'),
                 'verses': len(person['references'])}
                for person in answer['people'] if person['references']],
        }
        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort, CHECK_PROMPT)
        elapsed = time.time() - started
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{label}: {failed}', flush=True)
            return
        verdicts = parse(outcome.get('result'), ARRAY, list)
        if verdicts is None:
            with lock:
                totals['failed'] += 1
                print(f'{label}: the reply held no JSON array', flush=True)
            return

        by_key = {str(v.get('key')): v for v in verdicts if isinstance(v, dict)}
        row = {
            'label': label,
            'verdicts': [{'key': f'{label}#{person["id"]}', 'id': person['id'],
                          'person': (by_key.get(f'{label}#{person["id"]}') or {}).get('person'),
                          'same_as': (by_key.get(f'{label}#{person["id"]}') or {}).get('same_as'),
                          'why': (by_key.get(f'{label}#{person["id"]}') or {}).get('why')}
                         for person in standing],
            'prompt_version': CHECK_VERSION,
            'model': next(iter(outcome.get('modelUsage') or {'unknown': None})),
            'effort': args.effort, 'asked': asked, 'method': 'model-reading',
            'cost_usd': outcome.get('total_cost_usd') or 0.0,
            'seconds': round(elapsed, 1),
        }
        with open(os.path.join(folder, label + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(row, handle, ensure_ascii=False, indent=1)
        with lock:
            totals['calls'] += 1
            totals['done'] += 1
            totals['cost'] += row['cost_usd']
            totals['seconds'] += elapsed
            print(f'[{totals["done"]}/{len(pending)}] {label}: '
                  f'{sum(1 for v in row["verdicts"] if v["person"])}/{len(row["verdicts"])} upheld, '
                  f'{elapsed:.0f}s, ${row["cost_usd"]:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    verdicts = [v for row in read_checks(args.dir).values() for v in row['verdicts']
                if v['person'] is not None]
    upheld = sum(1 for v in verdicts if v['person'])
    print(f'{totals["calls"]} calls, {totals["failed"]} failed, ${totals["cost"]:.4f}, '
          f'{totals["seconds"]:.0f}s of model time in {time.time() - began:.0f}s of wall clock at '
          f'{args.workers} workers')
    print(f'{upheld} of {len(verdicts)} bearers upheld, {len(verdicts) - upheld} rejected '
          f'({(len(verdicts) - upheld) / max(1, len(verdicts)):.0%} of what rests on the '
          f'enumeration alone).')


# ---------------------------------------------------------------------------- the register itself


# What established a bearer, and the difference the owner is owed.
#
# `verse` is a split some verse of this corpus makes: the King James prints the name and the reading
# assigns that verse to this man and not to his namesake. `lexicon` is a split nothing we hold makes
# -- the enumeration separates two men and no verse we have distinguishes them. Both are records
# under the maximal grain; only one of them may cite a verse, and a page has to be able to say which
# it is looking at.
BY_A_VERSE = 'verse'
BY_THE_LEXICON = 'lexicon'


def decide(person, verdict):
    """
    Whether one proposed bearer becomes a record, and on what it stands.

    Written as one function because every count in the report is this rule applied, and a rule
    stated in two places is a rule that disagrees with itself.
    """
    kind = person.get('kind') or 'person'
    if person.get('same_as'):
        return False, None, f'the reading makes it the same man as #{person["same_as"]}'
    if kind != 'person':
        return False, None, f'the reading calls it a {kind}, and this is the register of persons'
    if person['references']:
        many = len(person['references'])
        return True, BY_A_VERSE, (
            f'{many} verse{"" if many == 1 else "s"} of this corpus '
            f'print{"s" if many == 1 else ""} the name, and the reading assigns '
            f'{"it" if many == 1 else "them"} to this bearer')
    if verdict is None or verdict.get('person') is None:
        return False, None, 'it stands on the enumeration alone and no second reading answered it'
    if verdict['person']:
        return True, BY_THE_LEXICON, (verdict.get('why')
                                      or 'a second reading upholds the enumeration for it')
    return False, None, ('a second reading refuses it: '
                         + (verdict.get('why') or 'not a distinct man of this name')
                         + (f' (the same as {verdict["same_as"]})' if verdict.get('same_as') else ''))


def matched(records, held):
    """
    Which held entity each record is the same person as, by the verses they share.

    A bearer's evidence is the verses the reading assigned to it, and a held entity's is the verses
    BibleData attests it in; where those overlap the two are the same man, and the encyclopedia's row
    stays with its slug. It is a derivation and not a statement, so it is deliberately strict: the
    pairs are taken in order of how much they share, each entity is claimed once within a name, and a
    bearer whose verses touch nothing held is a person we reach that nobody else holds.

    Only persons are candidates. Thirty of the entities under these names are places, and a register
    of persons that claimed a place's row would be putting a man on the page of a town.

    Mirrored in `PersonRegisterLoader.Match`, and the two are compared rather than trusted.
    """
    held = [entity for entity in held if entity['kind'] == 'person']
    pairs = []
    for record in records:
        mine = set(record['references'])
        if not mine:
            continue
        for entity in held:
            shared = len(mine & entity['verse_references'])
            if shared:
                pairs.append((shared, record['id'], entity['id'], record, entity))
    pairs.sort(key=lambda pair: (-pair[0], pair[1], pair[2]))

    taken_records, taken_entities, links = set(), set(), {}
    for shared, record_id, entity_id, record, entity in pairs:
        if record_id in taken_records or entity_id in taken_entities:
            continue
        taken_records.add(record_id)
        taken_entities.add(entity_id)
        links[record_id] = (entity, shared)
    return links


def register(args):
    """
    The register, as one line per bearer the reading proposed — the refusals with it, because what a
    register is asked next is why somebody is not in it.
    """
    manifest = read_manifest(args.dir)
    answers = read_answers(args.dir, manifest)
    checks = read_checks(args.dir)
    groups = {g['label']: g for g in manifest['groups']}

    # The group's Strong numbers are read again rather than taken from the manifest: twenty groups
    # enumerate nobody, and a number taken off the enumeration would leave exactly those twenty
    # with no number on the name row of anybody added under them.
    numbers = {group['label']: numbers_of(group) for group in population()}

    held = collections.defaultdict(list)
    for entity in held_persons():
        entity['verse_references'] = {reference(*address) for address in entity['verses']}
        held[entity['label']].append(entity)
    for entities in held.values():
        entities.sort(key=lambda entity: entity['id'])

    records, reached, missing = [], {}, []
    for label in sorted(groups):
        group = groups[label]
        answer = answers.get(label)
        if not answer:
            missing.append(label)
            continue
        verdicts = {v['id']: v for v in (checks.get(label) or {'verdicts': []})['verdicts']}
        item = {i['item']: i for i in group['enumeration']}
        made = []
        for person in answer['people']:
            kept, standing, why = decide(person, verdicts.get(person['id']))
            enumerated = person.get('enumerated')
            record = {
                'group': label,
                'id': person['id'],
                'key': f'{label}#{person["id"]}',
                'name': label,
                'description': person.get('description'),
                'kind': person.get('kind') or 'person',
                'kept': kept,
                'standing': standing,
                'why': why,
                'confidence': person.get('confidence'),
                'sameAs': person.get('same_as'),
                'enumerated': enumerated,
                'enumeration': (item.get(enumerated) or {}).get('says'),
                'strongNumbers': numbers.get(label) or None,
                'references': person['references'],
                'otherReferences': person['other_references'],
                'reading': {'model': answer['model'], 'effort': answer['effort'],
                            'promptVersion': answer['prompt_version'], 'askedAt': answer['asked'],
                            'note': answer['note']},
                'check': None if person['id'] not in verdicts else {
                    'person': verdicts[person['id']]['person'],
                    'sameAs': verdicts[person['id']]['same_as'],
                    'why': verdicts[person['id']]['why'],
                    'model': checks[label]['model'],
                    'promptVersion': checks[label]['prompt_version'],
                    'askedAt': checks[label]['asked']},
            }
            made.append(record)
        links = matched([r for r in made if r['kept']], held[label])
        for record in made:
            found = links.get(record['id'])
            record['reaches'] = None if not found else {
                'entityId': found[0]['id'], 'slug': found[0]['slug'], 'name': found[0]['name'],
                'source': found[0]['source'], 'shared': found[1]}
        reached[label] = {entity['id'] for _, (entity, _) in links.items()}
        records += made

    with open(os.path.join(args.dir, 'register.json'), 'w', encoding='utf-8') as handle:
        json.dump(records, handle, ensure_ascii=False, indent=1)

    report = register_report(records, held, reached, missing)
    with open(os.path.join(args.dir, 'register.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    print(report)


def register_report(records, held, reached, missing):
    out = []
    write = out.append
    kept = [r for r in records if r['kept']]
    claimed = [r for r in kept if r['reaches']]
    added = [r for r in kept if not r['reaches']]
    # A person can be a member of two groups — Zimri son of Zerah is the man another spelling files
    # under Zabdi — so the rows are counted once by their id and not once per membership.
    entities = [e for e in {e['id']: e for entities in held.values() for e in entities}.values()
                if e['kind'] == 'person']
    claimed_ids = {found for group in reached.values() for found in group}
    untouched = [e for e in entities if e['id'] not in claimed_ids]

    write('# The person register\n')
    write(f'{len(kept):,} persons of {len(records):,} bearers the reading proposed, over '
          f'{len(held):,} namesake groups.\n')
    # Records and rows are not the same count: two names can reach one man — Zimri son of Zerah is
    # the man BibleData files under Zabdi — so the rows claimed are fewer than the records claiming
    # them, and the loader reports rows. Both are printed, because otherwise the two implementations
    # look as though they disagree.
    rows = len(entities) - len(untouched)
    write('\n```')
    write(f'  register holds                    {len(kept):>6}')
    write(f'    reaches a held person           {len(claimed):>6}   the row stays, and its slug')
    write(f'      distinct rows they reach      {rows:>6}')
    write(f'    reaches nobody held             {len(added):>6}   added')
    write(f'  held persons in these groups      {len(entities):>6}')
    write(f'    nothing of ours reaches         {len(untouched):>6}   keep the provenance they have')
    write('')
    write(f'  standing on a verse of ours       {sum(1 for r in kept if r["standing"] == BY_A_VERSE):>6}')
    write(f'  standing on the lexicon alone     {sum(1 for r in kept if r["standing"] == BY_THE_LEXICON):>6}')
    write('')
    for why, count in collections.Counter(
            r['why'].split(':')[0] for r in records if not r['kept']).most_common():
        write(f'  refused  {count:>4}   {why}')
    write('```\n')
    if missing:
        write(f'\n{len(missing)} groups have no answer and are not in the register: '
              + ', '.join(missing[:20]) + ('...' if len(missing) > 20 else '') + '\n')

    write('\n## What the register does to the encyclopedia\n')
    write(f'{len(kept):,} records become {rows + len(added):,} rows: {len(claimed) - rows} men are '
          f'reached under two names at once and stay one page.\n')
    write('| | rows |')
    write('|---|---:|')
    write(f'| ours by derivation | {rows + len(added):,} |')
    for source, count in collections.Counter(
            e['source'] for e in entities if e['id'] in claimed_ids).most_common():
        write(f'| &nbsp;&nbsp;was {source} | {count:,} |')
    write(f'| &nbsp;&nbsp;added, held by nobody | {len(added):,} |')
    write(f'| still another party\'s | {len(untouched):,} |')
    for source, count in collections.Counter(e['source'] for e in untouched).most_common():
        write(f'| &nbsp;&nbsp;{source} | {count:,} |')
    return '\n'.join(out) + '\n'


REGISTER_BATCH = 100


def publish(args):
    """
    The register as the corpus carries it: newline-delimited JSON under this project's own folder,
    the way every other pass that cost a model run is published.
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

    print(f'{written} files, {len(records):,} bearers, '
          f'{sum(1 for record in records if record["kept"]):,} of them records -> {args.to}')


# ---------------------------------------------------------------------------- scoring the run


def sections(payload):
    """What one prompt is made of, in bytes, so the first thing to cut is measured and not guessed."""
    size = lambda value: len(json.dumps(value, ensure_ascii=False))
    return {
        'occurrences': size(payload['occurrences']),
        'clauses': size(payload['clauses']),
        'enumeration': size(payload['enumeration']),
        'lexicon': size(payload['lexicon']),
        'name and forms': size(payload['forms']) + size(payload['name']),
    }


def agreement(pairs):
    decided = [p for p in pairs if p[0] is not None and p[1] is not None]
    if not decided:
        return 0, 0, 0.0
    same = sum(1 for a, b in decided if a == b)
    return same, len(decided), 100.0 * same / len(decided)


def score(args):
    manifest = read_manifest(args.dir)
    answers = read_answers(args.dir, manifest)
    checks = read_checks(args.dir)
    with open(os.path.join(args.dir, 'register.json'), encoding='utf-8') as handle:
        records = json.load(handle)

    per_group = collections.defaultdict(list)
    for record in records:
        per_group[record['group']].append(record)

    rows, disagreements = [], []
    for group in manifest['groups']:
        answer = answers.get(group['label'])
        if not answer:
            continue
        row = {k: group[k] for k in ('label', 'band', 'entities', 'verses', 'stated', 'enumerated',
                                     'occurrences', 'named', 'shown', 'truncated')}
        row['model'] = answer['count']
        row['register'] = sum(1 for r in per_group[group['label']] if r['kept'])
        row['bibledata'] = group['entities']
        row['cost'] = answer['cost_usd'] + (checks.get(group['label']) or {}).get('cost_usd', 0.0)
        row['seconds'] = answer['seconds'] + (checks.get(group['label']) or {}).get('seconds', 0.0)
        row['assigned'] = answer['assigned']
        row['offered'] = answer['offered']
        row['invalid'] = answer['invalid_references']
        with open(os.path.join(args.dir, 'batches', group['label'] + '.json'),
                  encoding='utf-8') as handle:
            row['sections'] = sections(json.load(handle))
        usage = answer.get('usage') or {}
        row['input_tokens'] = sum(usage.get(k) or 0 for k in
                                  ('input_tokens', 'cache_read_input_tokens',
                                   'cache_creation_input_tokens'))
        row['output_tokens'] = usage.get('output_tokens') or 0
        rows.append(row)

        witnesses = {'register': row['register'], 'strong stated': row['stated'],
                     'strong enumerated': row['enumerated'], 'bibledata': row['bibledata']}
        spoken = {k: v for k, v in witnesses.items() if v is not None}
        if len(set(spoken.values())) > 1:
            disagreements.append({
                'label': group['label'], 'witnesses': witnesses,
                'note': answer.get('note'),
                'people': [{'description': r['description'], 'kind': r['kind'],
                            'kept': r['kept'], 'standing': r['standing'], 'why': r['why'],
                            'confidence': r['confidence'], 'verses': len(r['references'])}
                           for r in per_group[group['label']]],
                'enumeration': [item['says'] for item in group['enumeration']],
            })

    out = []
    write = out.append
    asked = next(iter(answers.values()))
    write(f'# The person split, run — {len(rows)} of {len(manifest["groups"])} namesake groups\n')
    write(f'Model `{asked["model"]}`, effort `{asked["effort"]}`, prompt '
          f'`{asked["prompt_version"]}`, asked {asked["asked"]}.\n')

    write('\n## Agreement\n')
    write('| pair | agree | decided | % |')
    write('|---|---:|---:|---:|')
    for name, left, right in [
            ('register vs BibleData', 'register', 'bibledata'),
            ('register vs Strong enumerated', 'register', 'enumerated'),
            ('register vs Strong stated', 'register', 'stated'),
            ('reading vs BibleData', 'model', 'bibledata'),
            ('Strong enumerated vs BibleData', 'enumerated', 'bibledata'),
            ('Strong stated vs BibleData', 'stated', 'bibledata'),
            ('Strong stated vs enumerated', 'stated', 'enumerated')]:
        same, total, percent = agreement([(r[left], r[right]) for r in rows])
        write(f'| {name} | {same} | {total} | {percent:.1f}% |')

    unanimous = [r for r in rows
                 if len({r[k] for k in ('register', 'stated', 'enumerated', 'bibledata')
                         if r[k] is not None}) == 1]
    write(f'\n{len(unanimous)} of {len(rows)} groups are decided — every witness that speaks says '
          f'the same number.\n')

    write('\n## Cost and wall clock, by band\n')
    write('| band | groups | verses (median) | $ a group | s a group | $ |')
    write('|---|---:|---:|---:|---:|---:|')
    for name, _, _ in BANDS:
        band = [r for r in rows if r['band'] == name]
        if not band:
            continue
        write(f'| {name} | {len(band)} | '
              f'{statistics.median(r["verses"] for r in band):.0f} | '
              f'${statistics.mean(r["cost"] for r in band):.4f} | '
              f'{statistics.mean(r["seconds"] for r in band):.0f} | '
              f'${sum(r["cost"] for r in band):.2f} |')
    spent = sum(r['cost'] for r in rows)
    seconds = sum(r['seconds'] for r in rows)
    write(f'\n**${spent:.4f} and {seconds / 3600:.1f} hours of model time for {len(rows)} groups** '
          f'— {seconds / 3600 / 3:.1f} hours at three workers.\n')
    write('\nThat is the cost carried on the answers that survive. A run resumed after a failure '
          'paid for the calls it repeated as well, and what was actually paid is the sum of the '
          "`ask` and `check` lines in each run's own output.\n")

    write('\n## What the payload is made of\n')
    total = collections.Counter()
    for row in rows:
        total.update(row['sections'])
    everything = sum(total.values())
    write('| section | bytes | share |')
    write('|---|---:|---:|')
    for name, size in total.most_common():
        write(f'| {name} | {size:,} | {100.0 * size / everything:.1f}% |')

    write('\n## Assignment\n')
    assigned = sum(r['assigned'] for r in rows)
    offered = sum(r['offered'] for r in rows)
    write(f'{assigned:,} of {offered:,} offered verses were assigned to a bearer '
          f'({100.0 * assigned / max(1, offered):.1f}%), '
          f'{sum(r["invalid"] for r in rows)} references invented and dropped.\n')

    write('\n## Disagreements\n')
    write(f'{len(disagreements)} of {len(rows)} groups, in `disagreements.json`.\n')
    write('| name | register | stated | enumerated | bibledata | verses |')
    write('|---|---:|---:|---:|---:|---:|')
    for row in sorted(rows, key=lambda r: -abs((r['register'] or 0) - r['bibledata'])):
        if len({row[k] for k in ('register', 'stated', 'enumerated', 'bibledata')
                if row[k] is not None}) == 1:
            continue
        write(f'| {row["label"]} | {row["register"]} '
              f'| {row["stated"] if row["stated"] is not None else "—"} '
              f'| {row["enumerated"] if row["enumerated"] is not None else "—"} '
              f'| {row["bibledata"]} | {row["verses"]} |')

    report = '\n'.join(out) + '\n'
    with open(os.path.join(args.dir, 'score.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    with open(os.path.join(args.dir, 'disagreements.json'), 'w', encoding='utf-8') as handle:
        json.dump(disagreements, handle, ensure_ascii=False, indent=1)
    print(report)


def census(args):
    """
    The three witnesses over all 528 groups, with no model in it. This is what the run is measured
    against, and it is worth reading on its own: the two witnesses that were to be scored against
    agree with each other on about half the groups.
    """
    groups = population()
    entries = {e['strong_number']: e
               for e in lexicon({n for g in groups for n in numbers_of(g)})}
    witnessed(groups, entries)

    print(f'{len(groups)} namesake groups, '
          f'{sum(g["entities"] for g in groups)} entity memberships, '
          f'{sum(g["verses"] for g in groups)} verses')
    print(f'{sum(1 for g in groups if g["stated"] is not None)} state a count, '
          f'{sum(1 for g in groups if g["enumerated"] is not None)} enumerate their bearers, '
          f'{sum(1 for g in groups if g["described"])} have a clause of ours')
    print()
    print('band     groups  verses  median  entities')
    for name, _, _ in BANDS:
        band = [g for g in groups if g['band'] == name]
        print(f'{name:<8} {len(band):>6} {sum(g["verses"] for g in band):>7} '
              f'{statistics.median(g["verses"] for g in band):>7.0f} '
              f'{sum(g["entities"] for g in band):>9}')
    print()
    print('pair                             agree  decided       %')
    for title, left, right in [('strong stated vs enumerated', 'stated', 'enumerated'),
                               ('strong stated vs bibledata', 'stated', 'entities'),
                               ('strong enumerated vs bibledata', 'enumerated', 'entities')]:
        same, total, percent = agreement([(g[left], g[right]) for g in groups])
        print(f'{title:<32} {same:>5} {total:>8} {percent:>6.1f}%')
    unanimous = sum(1 for g in groups
                    if len({g[k] for k in ('stated', 'enumerated', 'entities')
                            if g[k] is not None}) == 1)
    print()
    print(f'{unanimous} of {len(groups)} groups where every witness that speaks says the same.')


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    commands = parser.add_subparsers(dest='command', required=True)

    extractor = commands.add_parser('extract', help='write a payload for every group')
    extractor.add_argument('--out', required=True)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='assign the occurrences, group by group')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--effort', required=True, choices=['low', 'medium', 'high', 'xhigh', 'max'])
    asker.add_argument('--workers', type=int, default=3)
    asker.add_argument('--again', action='store_true')
    asker.add_argument('--only', nargs='*', metavar='NAME')
    asker.set_defaults(run=ask)

    checker = commands.add_parser('check', help='read the lexicon-only bearers a second time')
    checker.add_argument('--dir', required=True)
    checker.add_argument('--model', default='sonnet')
    checker.add_argument('--effort', required=True, choices=['low', 'medium', 'high', 'xhigh', 'max'])
    checker.add_argument('--workers', type=int, default=3)
    checker.add_argument('--again', action='store_true')
    checker.add_argument('--only', nargs='*', metavar='NAME')
    checker.set_defaults(run=check)

    registrar = commands.add_parser('register', help='decide the bearers and meet the encyclopedia')
    registrar.add_argument('--dir', required=True)
    registrar.set_defaults(run=register)

    publisher = commands.add_parser('publish', help='write the register under Resources/Essenthos')
    publisher.add_argument('--dir', required=True)
    publisher.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'persons'))
    publisher.add_argument('--prefix', default='register')
    publisher.set_defaults(run=publish)

    scorer = commands.add_parser('score', help='the run against the witnesses, and what it cost')
    scorer.add_argument('--dir', required=True)
    scorer.set_defaults(run=score)

    counter = commands.add_parser('census', help='the three witnesses over all 528 groups')
    counter.set_defaults(run=census)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
