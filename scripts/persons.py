"""
How many men bear this name, and which verses belong to which of them? Ask a model, and score the
answer against every witness the corpus already holds.

528 proper names are carried by more than one entity, and the count is the one place the register is
borrowed: 3,009 persons exist because BibleData drew the lines that way. This harness proposes the
lines instead -- from the King James text and from the clauses `entity_descriptor` already reads out
of it -- and measures the proposal against three witnesses that are not the answer:

    Strong's stated count       *Zecarjah, the name of twenty-nine Israelites*     424 of 528
    Strong's enumerated bearers *1) 11th of the minor prophets 2) king of Israel*  519 of 528
    BibleData's entity count    how many rows the table holds under the label      528 of 528

None of the three is a key, and they disagree with each other more often than they agree, which is
why the deliverable is a scored pilot and not a run.

    python scripts/persons.py extract --out .persons/pilot --seed 11
    python scripts/persons.py ask     --dir .persons/pilot --effort medium --workers 3
    python scripts/persons.py score   --dir .persons/pilot

Five things about the design are load-bearing, and each is a way the number could have been made
meaningless:

**Every witness is struck out of the prompt.** `detailed_definition` names the bearers one by one --
*1) 11th in order of the minor prophets; a priest, son of Berechiah* -- and 519 of the 528 groups
have one. `definition` states the count outright: *the name of twenty-nine Israelites*. Neither
reaches the model. What is shown of the lexicon is the lemma, its transliteration and its
derivation, which are etymology and say nothing about how many men carried the name -- checked over
all 528 groups, where three derivations mention a number and all three are *seven* meaning seven.

**The clauses are flattened and stripped of whose they are.** `entity_descriptor` rows carry the
entity they belong to, and that entity is BibleData's split. Grouped, the payload would state the
answer; so the clauses arrive as a pool -- relation, target, verse -- in reference order, and the
model has to work out for itself which of them speak of the same man.

**The occurrence list is BibleData's extent, not its count.** Which verses name somebody called
Zechariah comes from `entity_verse`, unioned across the group. That is borrowed and said to be: the
question being asked is where the lines fall inside that set, not what the set is.

**The model never touches the database.** It is given a payload and returns JSON. Every answer is
written to a file with the model, the effort level, the prompt version and the date, because it is a
claim about a reading and not a fact about the corpus.

**A group is one call.** Groups run from one verse to 1,479, so the cost of a group is the number
this pilot exists to produce, and a batch of several would average it away. `score` projects to 528
stratum by stratum for the same reason: sampling twelve tiny groups and calling it the mean would
flatter the bill by an order of magnitude.

A run leaves five things behind under its own directory:

    manifest.json           the selection, its seed, the strata and their populations
    batches/<label>.json    the prompt payload, exactly as the model saw it
    answers/<label>.json    the reading, with the harness's own cost and wall clock beside it
    score.md                agreement, cost, wall clock, payload composition, the projection
    disagreements.json      every group where the four witnesses do not all say the same

The run directory is not committed -- it is cheap to make again from here.
"""

import argparse
import collections
import concurrent.futures
import datetime
import json
import os
import random
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

RENDERING = 'KJV'

PROMPT_VERSION = 'persons-1'

# A namesake group is a `proper name` label carried by more than one entity. Titles, gentilics and
# descriptions share labels for reasons that are not namesakes -- `king of Judah` is eighteen men
# because it is an office -- and reading them here would measure the wrong thing.
NAME_KIND = 'proper name'

# Matthew, in the canonical ordinals `entity_verse` and `verse_reference` are written in.
FIRST_GREEK_BOOK = 40

# The verse budget for one group. Three of the 528 exceed it -- Jesus at 1,479, Jacob at 1,076 and
# Saul at 518 -- and for those the reading is of the head of the list rather than of all of it. A
# cap low enough to fit the median would have been the flattering choice this pilot exists to avoid.
OCCURRENCES_SHOWN = 400

# The size bands the sample is drawn from and the projection is summed over, by distinct verses in
# the group. The population is 304 / 103 / 72 / 26 / 23, so a proportional sample would put two
# groups in the tail that carries a fifth of the corpus's verses.
BANDS = [('1-9', 1, 9), ('10-19', 10, 19), ('20-49', 20, 49), ('50-99', 50, 99), ('100+', 100, None)]
BAND_SAMPLE = {'1-9': 12, '10-19': 8, '20-49': 8, '50-99': 6, '100+': 6}

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
You are a Biblical scholar deciding how many distinct people a single name belongs to.

You are given one name, its Hebrew and Greek forms, what a lexicon says the name means, the King
James text of every verse the name is attested in, and a pool of short claims already read out of
those verses by this corpus -- a father, a brother, an office, a place -- each with the verse it was
read from. The claims are given as a flat list on purpose: which of them belong to the same man is
part of what you are being asked.

Read the verses. Decide how many distinct people (or, where the name is also a place, how many
distinct places) bear this name, and say which verses belong to each of them.

Two people are distinct when the text distinguishes them: a different father, a different
generation, a different tribe or town, an office one holds and the other cannot, a span of years
that cannot hold one life. Two mentions are the same man until something says otherwise -- a
register that names Zechariah twice in one genealogy is naming him once.

Answer with a single JSON object and nothing else:

{
  "name":       the name you were given, unchanged
  "people":     an array, one object per distinct bearer, in the order they are first named:
                {
                  "id":          1, 2, 3 ... in order
                  "kind":        "person" or "place"
                  "description": under 20 words, what tells this one from the others
                  "references":  the references from `occurrences` that speak of this one
                  "confidence":  "high", "medium" or "low"
                }
  "unassigned": references you could not attach to any bearer, as an array
  "note":       one line, under 30 words, on what was hard about this name -- or null
}

Every reference must be one you were given. Assign each reference to exactly one bearer, or leave it
in `unassigned`; a verse that names two different men of this name goes to the first of them and is
mentioned in `note`. Do not invent bearers the verses do not support, and do not merge two the
verses plainly separate.
"""


def psql(sql):
    """One JSON value out of the live database. Read-only by construction: nothing here writes."""
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', CONTAINER,
         'psql', '-U', USER, '-d', DATABASE, '-Aqt', '-v', 'ON_ERROR_STOP=1', '-f', '-'],
        input=('set max_parallel_workers_per_gather=0;\n' + sql).encode('utf-8'),
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
    """Every namesake group, with the witnesses' raw material and the size the sample is drawn on."""
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


def material(labels):
    """The name forms, the occurrences and the clause pool, for the groups that were chosen."""
    chosen = ', '.join("'" + label.replace("'", "''") + "'" for label in labels)
    return psql(GROUPS + f"""
        , picked AS (SELECT * FROM member WHERE label IN ({chosen})),
        per AS (
            SELECT c.label,
                   (SELECT coalesce(json_agg(DISTINCT jsonb_build_object(
                        'hebrew', n.hebrew, 'hebrew_transliterated', n.hebrew_transliterated,
                        'greek', n.greek, 'greek_transliterated', n.greek_transliterated,
                        'meaning', n.meaning)), '[]')
                    FROM picked i JOIN entity_name n ON n.entity_id = i.entity_id
                    WHERE i.label = c.label AND n.label = c.label) AS forms,
                   (SELECT coalesce(json_agg(DISTINCT jsonb_build_array(
                        ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)), '[]')
                    FROM picked i JOIN entity_verse ev ON ev.entity_id = i.entity_id
                    WHERE i.label = c.label) AS occurrences,
                   (SELECT coalesce(json_agg(jsonb_build_array(
                        d.relation, t.name, d.canonical_book, d.canonical_chapter,
                        d.canonical_verse, d.confidence)), '[]')
                    FROM picked i
                    JOIN entity_descriptor d ON d.entity_id = i.entity_id
                    JOIN entity t ON t.id = d.target_entity_id
                    WHERE i.label = c.label) AS clauses
            FROM picked c GROUP BY c.label
        )
        SELECT coalesce(json_agg(json_build_object(
                   'label', label, 'forms', forms, 'occurrences', occurrences, 'clauses', clauses)
                   ORDER BY label), '[]')
        FROM per
    """)


def lexicon(numbers):
    """Strong's entries for the groups' numbers, `detailed_definition` included -- for `score` only."""
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


def enumerated_count(entries):
    """
    How many bearers the lexicon's long definition lists, one numbered clause each.

    Counts add across a group's Strong numbers where the numbers are different names spelt alike --
    Ahi is H277 and Ehi is H278 -- and do not where they are two spellings of one name. H3083 and
    H3129 both open *Jonathan or Jehonathan* and both then list the same fifteen men, so adding
    them gives twenty-five Jonathans where the text has fifteen. The stated counts do add, even
    there: those are per spelling, and four men carry the long one.
    """
    listed = []
    for entry in entries:
        items = [int(m.group(1))
                 for m in ENUMERATED_BEARER.finditer(entry.get('detailed_definition') or '')]
        if items:
            listed.append((aliases(entry), max(items)))
    if not listed:
        return None
    total, taken = 0, []
    for names, count in listed:
        shared = next((i for i, (seen, _) in enumerate(taken) if seen & names), None)
        if shared is None:
            taken.append((names, count))
        else:
            taken[shared] = (taken[shared][0] | names, max(taken[shared][1], count))
    return sum(count for _, count in taken)


def band_of(verses):
    for name, low, high in BANDS:
        if verses >= low and (high is None or verses <= high):
            return name
    return BANDS[0][0]


def extract(args):
    groups = population()
    entries = {e['strong_number']: e
               for e in lexicon({n for g in groups for n in numbers_of(g)})}

    for group in groups:
        held = [entries[n] for n in numbers_of(group) if n in entries]
        group['band'] = band_of(group['verses'])
        group['stated'] = stated_count(held)
        group['enumerated'] = enumerated_count(held)

    strata = collections.OrderedDict((name, [g for g in groups if g['band'] == name])
                                     for name, _, _ in BANDS)
    seeded = random.Random(args.seed)
    chosen = []
    for name, members in strata.items():
        wanted = min(BAND_SAMPLE[name], len(members))
        # Half the sample of each band is drawn from the groups Strong gives no count for: 104 of
        # the 528 have no such witness, and they are the part of the run with nothing to score
        # against, so a sample that took them in proportion would say almost nothing about them.
        silent = [g for g in members if g['stated'] is None]
        speaking = [g for g in members if g['stated'] is not None]
        take_silent = min(len(silent), wanted // 2)
        picked = seeded.sample(silent, take_silent) + seeded.sample(speaking, wanted - take_silent)
        chosen.extend(sorted(picked, key=lambda g: g['label']))

    os.makedirs(os.path.join(args.out, 'batches'), exist_ok=True)
    labels = [g['label'] for g in chosen]
    lines = {(b, c, v): line for b, c, v, line in rendering()}
    supplied = {m['label']: m for m in material(labels)}

    for group in chosen:
        seen = supplied[group['label']]
        occurrences = sorted(tuple(a) for a in seen['occurrences'])
        group['truncated'] = len(occurrences) > OCCURRENCES_SHOWN
        occurrences = occurrences[:OCCURRENCES_SHOWN]
        held = []
        for number in numbers_of(group):
            entry = entries.get(number)
            if not entry:
                continue
            held.append({'strong_number': number, 'lemma': entry['lemma'],
                         'transliteration': entry['transliteration'],
                         'derivation': entry['derivation']})
        # Two entities of one name carry two name rows that differ only in invisible ways, and the
        # database's own DISTINCT keeps both; the model gains nothing from reading the name twice.
        forms = list({json.dumps(f, ensure_ascii=False, sort_keys=True): f
                      for f in seen['forms']}.values())
        payload = {
            'prompt_version': PROMPT_VERSION,
            'name': group['label'],
            'forms': forms,
            'lexicon': held,
            'occurrences': [{'reference': reference(*address),
                             'king_james': lines.get(address, '')} for address in occurrences],
            'clauses': sorted(({'relation': relation, 'target': target,
                                'reference': reference(b, c, v), 'confidence': confidence}
                               for relation, target, b, c, v, confidence in seen['clauses']),
                              key=lambda claim: claim['reference']),
        }
        with open(os.path.join(args.out, 'batches', group['label'] + '.json'), 'w',
                  encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)

    manifest = {
        'seed': args.seed, 'prompt_version': PROMPT_VERSION,
        'occurrences_shown': OCCURRENCES_SHOWN,
        'population': {name: len(members) for name, members in strata.items()},
        'groups': [{k: g[k] for k in ('label', 'band', 'entities', 'verses', 'described',
                                      'stated', 'enumerated', 'kinds', 'testament', 'truncated')}
                   for g in chosen],
    }
    with open(os.path.join(args.out, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    print(f'{len(chosen)} groups of {len(groups)}, '
          + ', '.join(f'{name} {sum(1 for g in chosen if g["band"] == name)}/{len(members)}'
                      for name, members in strata.items()))
    print(f'{sum(1 for g in chosen if g["stated"] is None)} with no stated count, '
          f'{sum(1 for g in chosen if g["enumerated"] is None)} with no enumeration')


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


def call(prompt, model, effort):
    """
    One group, one call, one turn, no tools and no session. `--effort` is passed always and named in
    the answer file: the CLI thinks by default, and a bill nobody chose is a bill nobody can read.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', SYSTEM_PROMPT, '--model', model,
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


def parse(result):
    text = (result or '').strip()
    if text.startswith('```'):
        text = text.strip('`')
        text = text[text.find('\n') + 1:] if '\n' in text else text
        if text.lstrip().startswith('json'):
            text = text.lstrip()[4:]
    match = OBJECT.search(text)
    if not match:
        return None
    try:
        answer = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    return answer if isinstance(answer, dict) else None


def ask(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
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
    totals = {'calls': 0, 'cost': 0.0, 'seconds': 0.0, 'failed': 0}
    asked = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')

    def run(group):
        label = group['label']
        with open(os.path.join(args.dir, 'batches', label + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        offered = {o['reference'] for o in payload['occurrences']}

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, args.effort)
        elapsed = time.time() - started
        if failed:
            with lock:
                totals['failed'] += 1
                print(f'{label}: {failed}')
            return

        answer = parse(outcome.get('result'))
        if answer is None:
            with lock:
                totals['failed'] += 1
                print(f'{label}: no JSON object in the reply')
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
            people.append({'id': person.get('id'), 'kind': person.get('kind'),
                           'description': person.get('description'),
                           'confidence': person.get('confidence'), 'references': kept})

        usage = outcome.get('usage') or {}
        row = {
            'label': label, 'people': people,
            'unassigned': [r for r in (answer.get('unassigned') or []) if r in offered],
            'note': answer.get('note'),
            'count': len(people),
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
            totals['cost'] += row['cost_usd']
            totals['seconds'] += elapsed
            print(f'{label}: {len(people)} people, {len(assigned)}/{len(offered)} verses assigned'
                  + (f', {invalid} references invented' if invalid else '')
                  + f', {elapsed:.0f}s, ${row["cost_usd"]:.4f}')

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(run, pending))

    print(f'{totals["calls"]} calls, {totals["failed"]} failed, '
          f'${totals["cost"]:.4f} reported by the harness, '
          f'{totals["seconds"] / max(totals["calls"], 1):.0f}s a group')


def sections(payload):
    """What one prompt is made of, in bytes, so the first thing to cut is measured and not guessed."""
    size = lambda value: len(json.dumps(value, ensure_ascii=False))
    return {
        'occurrences': size(payload['occurrences']),
        'clauses': size(payload['clauses']),
        'lexicon': size(payload['lexicon']),
        'name and forms': size(payload['forms']) + size(payload['name']),
    }


def overhead(rows):
    """
    What a call costs before the payload is in it. Every batch is charged for the CLI's own preamble
    -- tool definitions and reminders that arrive whether or not tools are allowed -- and on a group
    of three verses that is the whole bill. Fitted rather than assumed: input tokens against payload
    bytes over the run, where the intercept is the preamble and the slope is the payload's own rate.
    """
    points = [(sum(r['sections'].values()), r['input_tokens']) for r in rows if r['input_tokens']]
    if len(points) < 2:
        return None, None
    mean_x = statistics.mean(x for x, _ in points)
    mean_y = statistics.mean(y for _, y in points)
    spread = sum((x - mean_x) ** 2 for x, _ in points)
    if not spread:
        return None, None
    slope = sum((x - mean_x) * (y - mean_y) for x, y in points) / spread
    return mean_y - slope * mean_x, slope


def agreement(pairs):
    decided = [p for p in pairs if p[0] is not None and p[1] is not None]
    if not decided:
        return 0, 0, 0.0
    same = sum(1 for a, b in decided if a == b)
    return same, len(decided), 100.0 * same / len(decided)


def score(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    answers, payloads = {}, {}
    for group in manifest['groups']:
        path = os.path.join(args.dir, 'answers', group['label'] + '.json')
        if os.path.exists(path):
            with open(path, encoding='utf-8') as handle:
                answers[group['label']] = json.load(handle)
        with open(os.path.join(args.dir, 'batches', group['label'] + '.json'),
                  encoding='utf-8') as handle:
            payloads[group['label']] = json.load(handle)

    groups = {g['label']: g for g in population() if g['label'] in answers}
    entries = {e['strong_number']: e for e in lexicon(
        {n for g in groups.values() for n in numbers_of(g)})}

    rows, disagreements = [], []
    for group in manifest['groups']:
        answer = answers.get(group['label'])
        if not answer:
            continue
        # The witnesses are read again rather than taken from the manifest: the manifest records
        # what was selected and when, and a count read at selection time would go stale the moment
        # the lexicon is reloaded or the rule for choosing a group's Strong number changes.
        held = [entries[n] for n in numbers_of(groups[group['label']]) if n in entries]
        row = dict(group)
        row['stated'] = stated_count(held)
        row['enumerated'] = enumerated_count(held)
        row['model'] = answer['count']
        row['bibledata'] = group['entities']
        row['cost'] = answer['cost_usd']
        row['seconds'] = answer['seconds']
        row['assigned'] = answer['assigned']
        row['offered'] = answer['offered']
        row['invalid'] = answer['invalid_references']
        row['sections'] = sections(payloads[group['label']])
        usage = answer.get('usage') or {}
        row['input_tokens'] = sum(usage.get(k) or 0 for k in
                                  ('input_tokens', 'cache_read_input_tokens',
                                   'cache_creation_input_tokens'))
        row['output_tokens'] = (usage.get('output_tokens') or 0)
        rows.append(row)
        witnesses = {'model': row['model'], 'strong stated': row['stated'],
                     'strong enumerated': row['enumerated'], 'bibledata': row['bibledata']}
        spoken = {k: v for k, v in witnesses.items() if v is not None}
        if len(set(spoken.values())) > 1:
            disagreements.append({
                'label': group['label'], 'witnesses': witnesses,
                'note': answer.get('note'),
                'people': [{'description': p['description'], 'kind': p['kind'],
                            'confidence': p['confidence'], 'verses': len(p['references'])}
                           for p in answer['people']],
                'strong_definition': ' | '.join(
                    entries[n]['definition'] or ''
                    for n in numbers_of(groups[group['label']]) if n in entries),
                'strong_enumeration': ' | '.join(
                    entries[n]['detailed_definition'] or ''
                    for n in numbers_of(groups[group['label']]) if n in entries),
            })

    out = []
    write = out.append
    write(f'# The person split, scored — {len(rows)} of 528 namesake groups\n')
    asked = next(iter(answers.values()))
    write(f'Model `{asked["model"]}`, effort `{asked["effort"]}`, prompt `{asked["prompt_version"]}`, '
          f'asked {asked["asked"]}, seed {manifest["seed"]}.\n')

    write('\n## Agreement\n')
    write('| pair | agree | decided | % |')
    write('|---|---:|---:|---:|')
    for name, left, right in [
            ('model vs Strong stated', 'model', 'stated'),
            ('model vs Strong enumerated', 'model', 'enumerated'),
            ('model vs BibleData', 'model', 'bibledata'),
            ('Strong stated vs BibleData', 'stated', 'bibledata'),
            ('Strong enumerated vs BibleData', 'enumerated', 'bibledata'),
            ('Strong stated vs enumerated', 'stated', 'enumerated')]:
        same, total, percent = agreement([(r[left], r[right]) for r in rows])
        write(f'| {name} | {same} | {total} | {percent:.1f}% |')

    unanimous = [r for r in rows
                 if len({r[k] for k in ('model', 'stated', 'enumerated', 'bibledata')
                         if r[k] is not None}) == 1]
    write(f'\n{len(unanimous)} of {len(rows)} groups are decided — every witness that speaks '
          f'says the same number.\n')

    write('\n## Cost and wall clock, by band\n')
    write('| band | population | sampled | verses (median) | $ a group | s a group | projected $ |')
    write('|---|---:|---:|---:|---:|---:|---:|')
    projected_cost = projected_seconds = 0.0
    for name, _, _ in BANDS:
        band = [r for r in rows if r['band'] == name]
        if not band:
            continue
        cost = statistics.mean(r['cost'] for r in band)
        seconds = statistics.mean(r['seconds'] for r in band)
        size = manifest['population'][name]
        projected_cost += cost * size
        projected_seconds += seconds * size
        write(f'| {name} | {size} | {len(band)} | '
              f'{statistics.median(r["verses"] for r in band):.0f} | '
              f'${cost:.4f} | {seconds:.0f} | ${cost * size:.2f} |')
    spent = sum(r['cost'] for r in rows)
    write(f'\n**${spent:.4f} for {len(rows)} groups.** Summed stratum by stratum, all 528 project to '
          f'**${projected_cost:.2f}** and {projected_seconds / 3600:.1f} hours of model time — '
          f'{projected_seconds / 3600 / max(args.workers, 1):.1f} hours at {args.workers} workers.\n')

    write('\n## What the payload is made of\n')
    total = collections.Counter()
    for row in rows:
        total.update(row['sections'])
    everything = sum(total.values())
    write('| section | bytes | share |')
    write('|---|---:|---:|')
    for name, size in total.most_common():
        write(f'| {name} | {size:,} | {100.0 * size / everything:.1f}% |')

    fixed, rate = overhead(rows)
    if fixed is not None:
        payload_bytes = statistics.mean(sum(r['sections'].values()) for r in rows)
        write(f'\nThat is the payload. What the call is charged for is larger: input averaged '
              f'{statistics.mean(r["input_tokens"] for r in rows):,.0f} tokens over '
              f'{payload_bytes:,.0f} bytes of payload, and fitting one against the other puts '
              f'**{fixed:,.0f} tokens on every call before the payload is in it** at '
              f"{rate:.2f} tokens a payload byte -- the CLI's own preamble, charged in full "
              f'whether the group has three verses or four hundred. Output averaged '
              f'{statistics.mean(r["output_tokens"] for r in rows):,.0f} tokens.\n')

    write('\n## Assignment\n')
    assigned = sum(r['assigned'] for r in rows)
    offered = sum(r['offered'] for r in rows)
    write(f'{assigned:,} of {offered:,} offered verses were assigned to a bearer '
          f'({100.0 * assigned / offered:.1f}%), '
          f'{sum(r["invalid"] for r in rows)} references invented and dropped.\n')

    write('\n## Disagreements\n')
    write(f'{len(disagreements)} of {len(rows)} groups, in `disagreements.json`.\n')
    write('| name | model | stated | enumerated | bibledata | verses |')
    write('|---|---:|---:|---:|---:|---:|')
    for row in sorted(rows, key=lambda r: -abs((r['model'] or 0) - r['bibledata'])):
        if len({row[k] for k in ('model', 'stated', 'enumerated', 'bibledata')
                if row[k] is not None}) == 1:
            continue
        write(f'| {row["label"]} | {row["model"]} | {row["stated"] if row["stated"] is not None else "—"} '
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
    The three witnesses over all 528 groups, with no model in it. This is what the pilot's sample is
    drawn from and what its projection is checked against, and it is worth reading on its own: the
    two witnesses a reading is scored against agree with each other on about half the groups.
    """
    groups = population()
    entries = {e['strong_number']: e
               for e in lexicon({n for g in groups for n in numbers_of(g)})}
    for group in groups:
        held = [entries[n] for n in numbers_of(group) if n in entries]
        group['band'] = band_of(group['verses'])
        group['stated'] = stated_count(held)
        group['enumerated'] = enumerated_count(held)

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

    extractor = commands.add_parser('extract', help='choose the groups and write the payloads')
    extractor.add_argument('--out', required=True)
    extractor.add_argument('--seed', type=int, default=11)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='run every group that has no answer yet')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--effort', required=True, choices=['low', 'medium', 'high', 'xhigh', 'max'])
    asker.add_argument('--workers', type=int, default=3)
    asker.add_argument('--again', action='store_true')
    asker.add_argument('--only', nargs='*', metavar='NAME')
    asker.set_defaults(run=ask)

    scorer = commands.add_parser('score', help='measure the reading against the three witnesses')
    scorer.add_argument('--dir', required=True)
    scorer.add_argument('--workers', type=int, default=3)
    scorer.set_defaults(run=score)

    counter = commands.add_parser('census', help='the three witnesses over all 528 groups')
    counter.set_defaults(run=census)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
