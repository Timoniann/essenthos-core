"""
What a place register of our own would hold, measured before any of it is built.

Persons are enumerable because 3,010 of 3,026 already carry a name record with a Strong number.
Places are not: 1,234 of the 1,351 we hold came from OpenBible, which supplies a label and
coordinates and no Strong number at all, so there is no index to disambiguate. This harness builds
the index the way the 164 peoples were built -- out of Strong and the occurrences already loaded --
and measures it against what we hold and against OpenBible, so the owner can price the register
before it exists.

    python scripts/places.py enumerate --out .places/run     # the free pass, from the lexicon
    python scripts/places.py occurrences --dir .places/run   # where the candidates stand, per text
    python scripts/places.py witness --dir .places/run       # BHSA's name types, the third witness
    python scripts/places.py compare --dir .places/run       # ours vs the 1,351 vs OpenBible's 1,233
    python scripts/places.py sample --dir .places/run --size 150
    python scripts/places.py ask --dir .places/run --model sonnet --effort medium
    python scripts/places.py match --dir .places/run         # the OpenBible link, as a claim of ours
    python scripts/places.py project --dir .places/run       # what it would yield and what it costs

Three things about the design are load-bearing:

**Nothing here writes to the database.** Every subcommand reads. `enumerate` leaves files under the
run directory and the register itself is somebody else's commit.

**The lexicon is asked twice, and the corpus is a third witness.** Strong
tags a Hebrew entry `n-pr-loc` and separately writes a gloss that names a place; where the two agree
the classification costs nothing and where they disagree somebody has to read. That cross-tabulation
is the whole estimate, and it is printed rather than summarised. BHSA then annotates the Hebrew
word itself with what its name names, which is nobody's reading of the dictionary and settles a
third of what the dictionary left open.

**The OpenBible link is measured in both directions.** Once the register is ours, `open_bible_id`
stops being inherited and becomes a claim we make by matching. What a match rate cannot say is what
the reader loses, so the run reports how many OpenBible records nothing of ours reaches, and writes
the failures out to be read rather than counted.

A run leaves:

    candidates.json    every Strong entry the pass considered, its evidence and its tier
    cross-tab.md       the tag against the gloss, which is the cost model
    occurrences.json   how many words of which text each candidate stands at
    witness.md         BHSA's own nameType against the tier the lexicon put each entry in
    compare.md         our enumeration against the entities we hold and against OpenBible
    sample.json        the pilot slice, its strata and its seed
    out/ask.jsonl      one object per entry the model read, with the model and the cost
    match.md           the OpenBible match, both directions, with the failures read out
    project.md         the slice's rates against the population, the yield and the bill
"""

import argparse
import concurrent.futures
import json
import os
import random
import re
import shutil
import subprocess
import sys
import threading
import time
import unicodedata

sys.stdout.reconfigure(encoding='utf-8')

CONTAINER = 'essenthos-api-db-1'
DATABASE = 'essenthos_core'
USER = 'essenthos'

PROMPT_VERSION = 'places-1'

# The joins over `word` run out of shared memory with a gather on this machine, and the planner
# picks one for anything touching the seven million rows.
NO_GATHER = 'set max_parallel_workers_per_gather=0;\n'


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


def array_literal(numbers):
    return "'{" + ','.join(f'"{number}"' for number in numbers) + "}'::text[]"


# ---------------------------------------------------------------------------- the free pass


# The nouns Strong classifies a place with. They are his words and not a taxonomy: he writes *a
# place in Palestine* six hundred times and *a monumental tree*, *a memorial cairn* and *the summer
# capital of Persia* once each, and a list that only took the common ones would call the rare ones
# unreadable and pay a model to read what the dictionary already says.
PLACE_NOUNS = (
    r'place|places|city|cities|town|towns|village|villages|hamlet|hamlets|capital|citadel|citadels'
    r'|fort|fortress|castle|tower|sanctuary|gate|gates|pass|peak|peaks|mountain|mountains|mount'
    r'|hill|hills|ridge|rock|cave|cliff|river|rivers|brook|brooks|stream|streams|spring|springs'
    r'|fountain|fountains|well|wells|pool|pools|lake|sea|seas|island|islands|isle|isles|port'
    r'|harbour|harbor|bay|region|regions|district|districts|country|countries|land|lands|territory'
    r'|territories|province|provinces|border|borders|valley|valleys|plain|plains|desert|deserts'
    r'|wilderness|meadow|oasis|station|stations|encampment|camp|road|highway|quarter|locality|site'
    r'|cairn'
)

# A place clause is a classifying noun standing in a naming construction -- *a place in Palestine*,
# *the name of two places*, *a Philistine city*. The article or the count in front is what separates
# a definition that names a place from one that merely mentions one: G4342 says *in a place* and is
# a verb, and a bare search for *a place* calls it a place name.
PLACE_CLAUSE = re.compile(
    r'\b(?:a|an|the|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|several'
    r'|his|her|its|their)\b'
    r'(?:\s+[\w-]+){0,3}?\s+'
    r'(?:' + PLACE_NOUNS + r')\b',
    re.IGNORECASE)

# *Armageddon (or Har-Meggiddon), a symbolic name*, *Ariel, a symbolical name for Jerusalem*,
# *Pekod, a symbolic name for Babylon*. Strong classifies these with no geographical noun at all,
# and about half of them are places -- the other half are *Mara, a symbolic name of Naomi*. So the
# cue is enough to reach for the entry and never enough to settle it: it goes to a reader.
SYMBOLIC = re.compile(r'\b(?:symbolic|symbolical|emblematic)\s+name\b', re.IGNORECASE)

# What Strong writes when the same entry also names a person. The place register does not lose these
# -- H2275 is Hebron the town and two men, and forcing one entry to be one thing is the error -- but
# the count of places in the entry is not the count of names, so they are reported apart.
PERSON_CLAUSE = re.compile(
    r'\ban? Israelites?\b|\bIsraelites\b'
    r'|\bthe name of\s+(?:[\w-]+\s+){0,2}(?:men|Israelites|persons|women)\b'
    r'|\ba (?:son|daughter|descendant|man|woman|king|priest|prophet|patriarch)\b',
    re.IGNORECASE)

# The tag is Strong's own, and it is wrong often enough to be worth catching: H1992 `n-pr-loc` is
# the pronoun *they*, H2038 is *a castle*, H11 is Hades. Each of those glosses names something that
# is not a place at all, and the mismatch is what sends the entry to a reader.
NOT_A_PLACE = re.compile(
    r'\b(?:deity|god|goddess|idol|pronoun|Hades|angel|demon)\b|only used when emphatic',
    re.IGNORECASE)

# A gloss that is nothing but the name -- *Gibath*, *Hamonah*, *Chamath-Tsobah* -- classifies
# nothing, so the tag stands alone. Anything with a comma or a semicolon is saying something more.
BARE_NAME = re.compile(r"^[{(\[]?[A-Z][\w'ʼʻ-]*(?:\s+[A-Zʼ][\w'ʼʻ-]*)?[)\]}]?\.?$")

# Strong's part of speech, which for Hebrew says outright whether the headword is a name at all.
# It is the discriminator the gloss cannot supply: *a stream, especially a winter torrent* and
# *Abanah, a river near Damascus* both name a river, and only the tag says that the first is the
# common noun and the second the name of one. It drops an entry only where the gloss offers no name
# either, because the tag is his and it is wrong: H5804 is parted `n-f` and glossed *Azzah, a place
# in Palestine*, and dropping Gaza on the strength of a part of speech is a loss nobody would see.
PROPER = re.compile(r'\bn-pr|\bnp\b')
COMMON = re.compile(r'^(?:n|n-m|n-f|v|a|a-m|a-f|adv|prep|conj|prt|d|p|i|inj)$')

# The window in which a capitalised word counts as the head the place clause classifies. Greek
# entries carry no part of speech at all, so the name has to be read off the prose: *Ephesus, a city
# of Asia Minor* names one and *a place of sitting apart, i.e. a privy* does not. Four words is what
# separates *Joppe (i.e. Japho), a place* from *a gate) through which they were led into Jerusalem*.
HEAD_WINDOW = 4

COUNTS = {
    'a': 1, 'an': 1, 'one': 1, 'two': 2, 'three': 3, 'four': 4, 'five': 5, 'six': 6, 'seven': 7,
    'eight': 8, 'nine': 9, 'ten': 10, 'eleven': 11, 'twelve': 12,
}

# *the name of two places in Palestine* -- the one construction Strong uses to say how many distinct
# places share a name, which is the place half of what TSK-0426 measured for persons.
STATED_COUNT = re.compile(
    r'\bname of\s+(?:[\w-]+\s+){0,2}?(' + '|'.join(COUNTS) + r'|\d+)\s+'
    r'(?:[\w-]+\s+){0,2}?(places|cities|towns|regions|mountains|rivers)\b',
    re.IGNORECASE)

TIERS = {
    'agreed': 'the tag and the gloss both say place',
    'gloss-only': 'no location tag, and the gloss names a place under a headword that is a name',
    'tag-only': 'tagged a location and glossed with the name alone, so the tag is all there is',
    'common': 'Strong parts it a common noun or a verb, so the place clause describes a thing rather than naming one',
    'read': 'the tag and the gloss disagree, or nothing says whether the headword is a name: somebody has to read it',
}

# The tiers that yield a place. `common` is settled and cheap and yields nothing: Strong has already
# said the headword is not a name, so the entry leaves the register without anybody reading it.
KEPT = ('agreed', 'gloss-only', 'tag-only')


def named_head(definition, at):
    """
    Whether a capitalised word stands close enough in front of a place clause to be what the clause
    classifies. It is the only evidence Greek offers, because Strong parts no Greek entry.
    """
    before = re.findall(r"[\w'ʼʻÆæ-]+", definition[:at])[-HEAD_WINDOW:]
    return any(word[:1].isupper() for word in before)


def classify(entry):
    """
    One entry's evidence and the tier it falls in. Deterministic and cheap: this is the pass that
    costs nothing, and what it cannot settle is the whole budget.
    """
    definition = (entry.get('definition') or '').strip()
    morphology = (entry.get('morphology') or '').strip()
    tagged = 'loc' in morphology
    clause = PLACE_CLAUSE.search(definition)
    place = bool(clause)
    person = bool(PERSON_CLAUSE.search(definition))
    refused = bool(NOT_A_PLACE.search(definition))
    bare = bool(definition) and bool(BARE_NAME.match(definition))
    proper = bool(PROPER.search(morphology)) or (place and named_head(definition, clause.start()))

    stated = STATED_COUNT.search(definition)
    count = None
    if stated:
        word = stated.group(1).lower()
        count = COUNTS.get(word) or (int(word) if word.isdigit() else None)

    symbolic = bool(SYMBOLIC.search(definition))

    if symbolic and not place:
        tier = 'read'
    elif tagged and place and not refused:
        tier = 'agreed'
    elif tagged and (bare or not definition):
        tier = 'tag-only'
    elif not tagged and place and proper and not refused:
        tier = 'gloss-only'
    elif COMMON.match(morphology) and not proper:
        tier = 'common'
    else:
        tier = 'read'

    return {
        'number': entry['number'],
        'lemma': entry.get('lemma'),
        'transliteration': entry.get('transliteration'),
        'definition': definition,
        'morphology': morphology or None,
        'tagged': tagged,
        'gloss_place': place,
        'gloss_person': person,
        'gloss_refuses': refused,
        'gloss_symbolic': symbolic,
        'proper': proper,
        'bare': bare,
        'stated_places': count,
        'tier': tier,
    }


CANDIDATE_SQL = r"""
select coalesce(json_agg(row_to_json(e) order by e.number), '[]'::json) from (
  select strong_number as number, lemma, transliteration, definition, morphology
  from strong_entry
  where morphology like '%loc%'
     or definition ~* '\m(a|an|the|one|two|three|four|five|six|seven|eight|nine|ten|his|her|its|their)\M[^;]{0,40}\m(place|places|city|cities|town|towns|village|hamlet|capital|citadel|castle|tower|gate|pass|peak|mountain|mountains|mount|hill|hills|rock|cave|river|rivers|brook|stream|spring|fountain|well|pool|lake|sea|island|isle|port|region|regions|district|country|land|territory|province|valley|plain|desert|wilderness|meadow|station|encampment|camp|cairn)\M'
     or definition ~* '\m(symbolic|symbolical|emblematic) name\M'
) e;
"""


def enumerate_(args):
    directory = args.out
    os.makedirs(directory, exist_ok=True)

    entries = psql(CANDIDATE_SQL) or []
    rows = [classify(entry) for entry in entries]
    rows.sort(key=lambda row: (row['number'][0], int(row['number'][1:])))

    with open(os.path.join(directory, 'candidates.json'), 'w', encoding='utf-8') as handle:
        json.dump(rows, handle, ensure_ascii=False, indent=1)

    write_cross_tab(directory, rows)
    report(rows)
    return 0


def write_cross_tab(directory, rows):
    """
    The tag against the gloss, in the cells that decide what has to be read. It is written out
    rather than summarised because the cell counts are the estimate, and an estimate whose shape
    cannot be checked is a number somebody has to take on trust.
    """
    cells = {}
    for row in rows:
        key = (row['tagged'], row['gloss_place'], row['tier'])
        cells[key] = cells.get(key, 0) + 1

    lines = ['# What Strong settles for himself, and what he leaves', '',
             f'{len(rows)} entries reach the net: a location tag, or a gloss that names a place.', '',
             '| tag | gloss names a place | tier | entries |', '|---|---|---|---|']
    for (tagged, place, tier), count in sorted(cells.items(), key=lambda item: -item[1]):
        lines.append(f'| {"yes" if tagged else "no"} | {"yes" if place else "no"} | {tier} | {count} |')

    lines += ['', '## The tiers', '']
    for tier, why in TIERS.items():
        lines.append(f'- **{tier}** -- {why}: {sum(1 for row in rows if row["tier"] == tier)}')

    stated = [row for row in rows if row['stated_places']]
    lines += ['', '## Where Strong states how many places share the name', '',
              f'{len(stated)} entries say so outright, covering '
              f'{sum(row["stated_places"] for row in stated)} distinct places.', '']
    for row in sorted(stated, key=lambda row: -row['stated_places'])[:12]:
        lines.append(f'- `{row["number"]}` {row["stated_places"]} -- {row["definition"]}')

    dual = [row for row in rows if row['gloss_place'] and row['gloss_person']]
    lines += ['', '## Entries that are a place and a person at once', '',
              f'{len(dual)} of them. H2275 is Hebron and two men; forcing one entry to be one kind '
              f'is the error this register has to avoid.', '']
    for row in dual[:10]:
        lines.append(f'- `{row["number"]}` {row["definition"]}')

    with open(os.path.join(directory, 'cross-tab.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')


def report(rows):
    by_tier = {}
    for row in rows:
        by_tier[row['tier']] = by_tier.get(row['tier'], 0) + 1
    print(f'{len(rows)} candidate entries.')
    for tier, why in TIERS.items():
        print(f'  {tier:<12} {by_tier.get(tier, 0):>5}   {why}')
    print(f'  {"kept":<12} {sum(by_tier.get(tier, 0) for tier in KEPT):>5}   places the free pass yields')
    free = sum(count for tier, count in by_tier.items() if tier != 'read')
    print(f'  {"free":<12} {free:>5}   settled without a model, kept or dropped')
    print(f'  {"hebrew":<12} {sum(1 for row in rows if row["number"].startswith("H")):>5}')
    print(f'  {"greek":<12} {sum(1 for row in rows if row["number"].startswith("G")):>5}')


# ---------------------------------------------------------------------------- where they stand


OCCURRENCE_SQL = """
select coalesce(json_agg(row_to_json(o)), '[]'::json) from (
  select w.strong_number as number, t.slug as text, 'stated' as how, count(*) as words,
         count(distinct w.verse_id) as verses
  from word w join text t on t.id = w.text_id
  where w.strong_number = any (NUMBERS)
  group by 1, 2
  union all
  select ws.number, t.slug, ws.method, count(*), count(distinct w.verse_id)
  from word_strong ws join word w on w.id = ws.word_id join text t on t.id = w.text_id
  where ws.number = any (NUMBERS)
  group by 1, 2, 3
) o;
"""

DEUTEROCANON_SQL = """
select coalesce(json_agg(row_to_json(d)), '[]'::json) from (
  select b.name as book, ws.number, count(*) as words
  from word_strong ws join word w on w.id = ws.word_id
       join verse v on v.id = w.verse_id join book b on b.id = v.book_id
  where b.canonical_ordinal > 66 and ws.number = any (NUMBERS)
  group by 1, 2
) d;
"""


def occurrences(args):
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)

    literal = array_literal([row['number'] for row in rows])
    found = psql(OCCURRENCE_SQL.replace('NUMBERS', literal)) or []

    per_number = {}
    for entry in found:
        bucket = per_number.setdefault(entry['number'], {})
        bucket[entry['text']] = {'words': entry['words'], 'verses': entry['verses'],
                                 'how': entry['how']}

    deutero = psql(DEUTEROCANON_SQL.replace('NUMBERS', literal)) or []

    with open(os.path.join(directory, 'occurrences.json'), 'w', encoding='utf-8') as handle:
        json.dump({'per_number': per_number, 'deuterocanon': deutero}, handle,
                  ensure_ascii=False, indent=1)

    standing = [row for row in rows if row['number'] in per_number]
    print(f'{len(standing)} of {len(rows)} candidates stand at a word of some text; '
          f'{len(rows) - len(standing)} at none.')

    per_text = {}
    for entry in found:
        key = (entry['text'], entry['how'])
        per_text[key] = per_text.get(key, 0) + entry['words']
    for (text, how), words in sorted(per_text.items(), key=lambda item: -item[1]):
        print(f'  {text:<22} {how:<16} {words:>8} words')

    for tier in TIERS:
        kept = [row for row in standing if row['tier'] == tier]
        print(f'  {tier:<12} {len(kept):>5} standing of '
              f'{sum(1 for row in rows if row["tier"] == tier)}')

    books = {}
    for entry in deutero:
        books[entry['book']] = books.get(entry['book'], 0) + entry['words']
    print(f'  deuterocanon: {sum(books.values())} words over {len(books)} books, '
          f'{len({entry["number"] for entry in deutero})} distinct candidate numbers')
    return 0


# ---------------------------------------------------------------------------- the third witness


# What BHSA says a name names, word by word, independently of anything Strong wrote. It is the only
# witness in this corpus that is not the lexicon, it exists for Hebrew alone -- nothing tags the
# Greek -- and where it is unambiguous it settles an entry for nothing.
NAME_TYPE_SQL = """
select coalesce(json_agg(row_to_json(x)), '[]'::json) from (
  select w.strong_number as number, w.morphology->>'nameType' as name_type, count(*) as words
  from word w join text t on t.id = w.text_id
  where t.slug = 'BHSA' and w.strong_number = any (NUMBERS)
  group by 1, 2
) x;
"""

# BHSA marks 4,506 words `pers,gens,topo` at once, which is the annotation declining to choose. A
# verdict is only worth having where it does choose.
VERDICTS = ('topo alone', 'topo among others', 'never topo', 'BHSA says nothing')


def verdict(types):
    """What BHSA's tags on one Strong number amount to."""
    types = {name for name in types if name}
    if not types:
        return 'BHSA says nothing'
    if not any('topo' in name for name in types):
        return 'never topo'
    return 'topo alone' if types == {'topo'} else 'topo among others'


def witness(args):
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)
    hebrew = [row for row in rows if row['number'].startswith('H')]

    tags = {}
    for entry in psql(NAME_TYPE_SQL.replace(
            'NUMBERS', array_literal([row['number'] for row in hebrew]))) or []:
        tags.setdefault(entry['number'], set()).add(entry['name_type'])

    table = {}
    for row in hebrew:
        key = (row['tier'], verdict(tags.get(row['number'], set())))
        table[key] = table.get(key, 0) + 1

    lines = ['# What BHSA says, which is nobody\'s reading of the dictionary', '',
             f'{len(hebrew)} of the {len(rows)} candidates are Hebrew and can be asked this. The '
             f'Greek cannot: nothing in this corpus annotates a Greek word with what its name '
             f'names.', '',
             '| tier | ' + ' | '.join(VERDICTS) + ' |',
             '|---|' + '---|' * len(VERDICTS)]
    for tier in TIERS:
        counts = [table.get((tier, said), 0) for said in VERDICTS]
        if any(counts):
            lines.append(f'| {tier} | ' + ' | '.join(str(count) for count in counts) + ' |')

    settles = table.get(('read', 'topo alone'), 0) + table.get(('read', 'topo among others'), 0)
    refuses = table.get(('read', 'never topo'), 0)
    contradicted = sum(table.get((tier, 'never topo'), 0) for tier in KEPT)
    lines += ['', '## What it is worth', '',
              f'- entries the lexicon could not settle that BHSA calls a place: **{settles}**',
              f'- entries it could not settle that BHSA never calls a place: **{refuses}**',
              f'- entries the free pass keeps that BHSA never calls a place: **{contradicted}** '
              f'-- read these before writing them',
              f'- entries the free pass drops as common nouns that BHSA calls a place: '
              f'**{table.get(("common", "topo alone"), 0)}**', '']

    with open(os.path.join(directory, 'witness.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))
    return 0


# ---------------------------------------------------------------------------- against what we hold


HELD_SQL = """
select coalesce(json_agg(row_to_json(p)), '[]'::json) from (
  select e.id, e.slug, e.name, e.source, e.open_bible_id,
         (select json_agg(n.label) from entity_name n where n.entity_id = e.id) as labels,
         (select json_agg(distinct n.hebrew_strong_number) from entity_name n
          where n.entity_id = e.id and n.hebrew_strong_number is not null) as hebrew,
         (select json_agg(distinct n.greek_strong_number) from entity_name n
          where n.entity_id = e.id and n.greek_strong_number is not null) as greek
  from entity e where e.kind = 'place'
) p;
"""


# The ligatures Strong prints and no gazetteer does. Unicode decomposition leaves them alone --
# NFKD knows nothing about æ -- so a strict match loses *Cenchreæ* against *Cenchreae* and
# *Cæsaria* against *Caesarea* on a typographic convention, which is not a disagreement about
# anything.
LIGATURES = {'æ': 'ae', 'œ': 'oe', 'ß': 'ss', 'ø': 'o', 'đ': 'd'}


def normalise(name):
    """
    A name reduced to what two spellings of it share. OpenBible writes *Beth-lehem*, Strong writes
    *Beth-Lechem* and the King James writes *Bethlehem*: the hyphen, the case, the ligature and the
    accent are spelling and not identity, so they come off before anything is compared.
    """
    if not name:
        return ''
    folded = name.lower()
    for ligature, into in LIGATURES.items():
        folded = folded.replace(ligature, into)
    folded = unicodedata.normalize('NFKD', folded)
    folded = ''.join(ch for ch in folded if not unicodedata.combining(ch))
    folded = folded.replace('ʼ', '').replace('ʻ', '').replace("'", '')
    return re.sub(r'[^a-z0-9]', '', folded)


# The spelling conventions that separate the same name in two books. Strong writes the tsade *Ts*
# and the King James writes it *Z* -- *Tsereth-hash-Shachar* against *Zereth-shahar*; the kaph and
# the cheth come out *ch*, *c*, *k* and *q* in different hands; *Jiphtach* and *Yiphtach* are one
# word. Folding all of it down to consonants gives a second, looser key, and the gap between the two
# match rates is how much of the residue is spelling rather than absence.
#
# It is deliberately not the matcher a register would use. *Dan* and *Don* fold together and so do
# *Gath* and *Goath*, so the loose rate is an upper bound to be read beside the strict one and a
# sample of what it newly joins, never a link to be written.
CONNECTIVES = re.compile(r'\b(?:of|the|in|at|upon)\b|^\s*(?:mount|mt)\b', re.IGNORECASE)
FOLDS = [
    ('ts', 'z'), ('ph', 'f'), ('ch', 'k'), ('kh', 'k'), ('sh', 's'), ('q', 'k'), ('c', 'k'),
    ('j', 'y'), ('v', 'w'), ('u', 'w'), ('h', ''),
]
DOUBLED = re.compile(r'(.)\1+')
VOWELS = re.compile(r'[aeiouy]')


def loosely(name):
    """A name folded to its consonants, for a second pass over what the strict key missed."""
    folded = normalise(CONNECTIVES.sub(' ', name or ''))
    for pattern, into in FOLDS:
        folded = folded.replace(pattern, into)
    return VOWELS.sub('', DOUBLED.sub(r'\1', folded))


def strong_numbers(place):
    found = set()
    for number in (place.get('hebrew') or []) + (place.get('greek') or []):
        for part in str(number).split(','):
            if part.strip():
                found.add(part.strip())
    return found


def compare(args):
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)
    held = psql(HELD_SQL) or []

    kept = {row['number'] for row in rows if row['tier'] in KEPT}
    caught = {row['number'] for row in rows}

    held_numbers = set()
    for place in held:
        held_numbers |= strong_numbers(place)

    lines = ['# Our own enumeration against what we hold', '',
             f'- candidate Strong entries the free pass keeps: **{len(kept)}** '
             f'(of {len(caught)} the net caught)',
             f'- place entities held today: **{len(held)}**',
             f'- of those carrying a Strong number at all: '
             f'**{sum(1 for place in held if strong_numbers(place))}**',
             f'- distinct Strong numbers those entities name: **{len(held_numbers)}**',
             f'- of which the candidate list reaches: **{len(held_numbers & caught)}**',
             f'- held numbers the candidate list misses: **{len(held_numbers - caught)}**',
             f'- candidates no held entity names: **{len(caught - held_numbers)}**', '']

    missed = sorted(held_numbers - caught)
    if missed:
        lines += ['## What a held entity names and the lexicon pass does not reach', '']
        detail = psql(
            "select coalesce(json_agg(row_to_json(m)), '[]'::json) from ("
            "select strong_number as number, definition, morphology from strong_entry "
            f"where strong_number = any ({array_literal(missed)})) m;") or []
        for entry in detail[:40]:
            lines.append(f'- `{entry["number"]}` [{entry["morphology"] or "-"}] {entry["definition"]}')
        if len(missed) > len(detail):
            lines.append(f'- and {len(missed) - len(detail)} the lexicon holds no entry for at all')
        lines.append('')

    by_name = {}
    for place in held:
        by_name.setdefault(normalise(place['name']), []).append(place)
    lines += ['## Names, not numbers', '',
              f'- distinct normalised names among held places: **{len(by_name)}**',
              f'- names carried by more than one held entity: '
              f'**{sum(1 for group in by_name.values() if len(group) > 1)}**', '']

    stated = [row for row in rows if row['stated_places']]
    lines += [f'- entries where Strong states how many places share the name: **{len(stated)}**',
              f'- distinct places those entries account for: '
              f'**{sum(row["stated_places"] for row in stated)}**', '']

    with open(os.path.join(directory, 'compare.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))
    return 0


# ---------------------------------------------------------------------------- the pilot slice


def sample(args):
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)
    standing = {}
    path = os.path.join(directory, 'occurrences.json')
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            standing = json.load(handle).get('per_number', {})

    def words(row):
        return sum(where['words'] for where in standing.get(row['number'], {}).values())

    # The strata are the tiers, because what a slice has to be representative of is the mix of work
    # and not the alphabet. Within a tier the draw is half from the entries that stand at most words
    # and half from the rest: a slice of entries nobody reads would price the register at the cost
    # of its rarest half.
    random.seed(args.seed)
    chosen = []
    for tier in TIERS:
        pool = [row for row in rows if row['tier'] == tier]
        if not pool:
            continue
        want = min(max(1, round(args.size * len(pool) / len(rows))), len(pool))
        pool.sort(key=words, reverse=True)
        cut = max(1, len(pool) // 2)
        picked = random.sample(pool[:cut], min(want // 2, cut))
        rest = [row for row in pool[cut:]]
        picked += random.sample(rest, min(want - len(picked), len(rest)))
        for row in picked:
            row = dict(row)
            row['words'] = words(row)
            chosen.append(row)

    with open(os.path.join(directory, 'sample.json'), 'w', encoding='utf-8') as handle:
        json.dump({'seed': args.seed, 'size': len(chosen), 'entries': chosen},
                  handle, ensure_ascii=False, indent=1)

    print(f'{len(chosen)} entries in the slice, seed {args.seed}.')
    for tier in TIERS:
        picked = [row for row in chosen if row['tier'] == tier]
        pool = sum(1 for row in rows if row['tier'] == tier)
        print(f'  {tier:<12} {len(picked):>4} of {pool:>5}   '
              f'{sum(row["words"] for row in picked):>7} words behind them')
    return 0


# ---------------------------------------------------------------------------- asking


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


CALL_TIMEOUT = 600
CALL_ATTEMPTS = 2

SYSTEM = """You read entries of Strong's dictionary and say whether the entry names a place.

Answer with a JSON array, one object per entry, in the order given, and nothing else:

  {"number": "H1234", "place": true, "places": 1, "person": false, "why": "..."}

  place   -- does this entry name one or more geographical places: a settlement, a region, a
             mountain, a river, a named site? A common noun meaning "place" is not one. A deity, a
             pronoun, a gentilic and a personal name are not one.
  places  -- how many distinct places the entry names, when the entry says so or clearly implies it.
             null when it cannot be told.
  person  -- does the same entry also name one or more persons? Many do, and both are true.
  why     -- one short clause, quoting the words of the entry that decide it.

The morphology tag is Strong's own and is sometimes wrong; the gloss is the evidence. Do not guess
beyond what the entry states."""


def call(prompt, model, effort=None):
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', SYSTEM, '--model', model,
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


BATCH_ENTRIES = 10


def ask(args):
    directory = args.dir
    with open(os.path.join(directory, 'sample.json'), encoding='utf-8') as handle:
        chosen = json.load(handle)
    entries = [row for row in chosen['entries'] if args.tier in (None, row['tier'])]
    os.makedirs(os.path.join(directory, 'out'), exist_ok=True)

    batches = [entries[at:at + BATCH_ENTRIES] for at in range(0, len(entries), BATCH_ENTRIES)]
    written, lock = [], threading.Lock()
    started = time.time()

    def one(batch):
        payload = [{'number': row['number'], 'lemma': row['lemma'],
                    'transliteration': row['transliteration'],
                    'morphology': row['morphology'], 'definition': row['definition']}
                   for row in batch]
        outcome, failure = call(json.dumps(payload, ensure_ascii=False, indent=1),
                                args.model, args.effort)
        if failure:
            return [], 0.0, failure
        cost = outcome.get('total_cost_usd') or 0.0
        answers = parse(outcome.get('result'))
        if answers is None:
            return [], cost, 'the reply held no JSON array'
        by_number = {str(answer.get('number')): answer
                     for answer in answers if isinstance(answer, dict)}
        rows = []
        for row in batch:
            answer = by_number.get(row['number'])
            rows.append({
                'number': row['number'], 'tier': row['tier'], 'definition': row['definition'],
                'morphology': row['morphology'],
                'free_place': row['tier'] in KEPT,
                'free_places': row['stated_places'],
                'model_place': answer.get('place') if answer else None,
                'model_places': answer.get('places') if answer else None,
                'model_person': answer.get('person') if answer else None,
                'why': answer.get('why') if answer else None,
                'model': args.model, 'effort': args.effort,
                'cost': round(cost / max(1, len(batch)), 6),
            })
        return rows, cost, None

    total = 0.0
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        for rows, cost, failure in pool.map(one, batches):
            with lock:
                total += cost
                written.extend(rows)
                print(f'[{len(written)}/{len(entries)}] ${cost:.4f}'
                      + (f'  {failure}' if failure else ''), flush=True)

    elapsed = time.time() - started
    with open(os.path.join(directory, 'out', 'ask.jsonl'), 'w', encoding='utf-8') as handle:
        for row in written:
            handle.write(json.dumps(row, ensure_ascii=False) + '\n')
    with open(os.path.join(directory, 'out', 'ask.json'), 'w', encoding='utf-8') as handle:
        json.dump({'model': args.model, 'effort': args.effort, 'entries': len(written),
                   'cost': round(total, 4), 'seconds': round(elapsed, 1),
                   'prompt_version': PROMPT_VERSION}, handle, indent=1)

    answered = [row for row in written if row['model_place'] is not None]
    print(f'{len(answered)} of {len(entries)} answered, ${total:.4f} in {elapsed:.0f}s -- '
          f'${total / max(1, len(answered)):.5f} an entry, '
          f'{elapsed / max(1, len(answered)):.2f}s an entry.')
    agree = sum(1 for row in answered if bool(row['model_place']) == bool(row['free_place']))
    print(f'{agree} of {len(answered)} agree with the free pass on whether it is a place.')
    for tier in TIERS:
        kept = [row for row in answered if row['tier'] == tier]
        if kept:
            same = sum(1 for row in kept if bool(row['model_place']) == bool(row['free_place']))
            print(f'  {tier:<12} {same}/{len(kept)} agree')
    return 0


# ---------------------------------------------------------------------------- the OpenBible link


# Strong prints the King James renderings of each headword beside the gloss, and for every one of
# the 765 entries he parts a location it is filled. That is the string an OpenBible label can be
# met by: OpenBible names *Bethlehem*, Strong transliterates *Bêyth Lechem*, and what stands between
# them is the King James spelling both were written from. The corpus cannot supply it -- the King
# James words in this database carry Greek numbers only -- so the lexicon's own column is the route.
KJV_RENDERINGS_SQL = """
select coalesce(json_agg(row_to_json(k)), '[]'::json) from (
  select strong_number as number, kjv_definition
  from strong_entry where strong_number = any (NUMBERS) and kjv_definition is not null
) k;
"""

HEAD_NAME = re.compile(r'^[{(\[]?\s*([^,;(]+)')

# What a King James rendering column carries besides names: Strong writes *Gazer, Gezer.* for one
# entry and *out of Egypt, Egyptian, Mizraim* for another, and the prepositional phrases are not
# names of anything.
RENDERING_NOISE = re.compile(
    r'^(?:out of|in|into|from|of|the|toward|unto|at|with|and|compare|see also|also)\b',
    re.IGNORECASE)


def renderings(numbers):
    """The King James spellings Strong lists for each number, split and cleaned."""
    found = {}
    for row in psql(KJV_RENDERINGS_SQL.replace('NUMBERS', array_literal(numbers))) or []:
        names = set()
        # The column ends the list with a full stop and sometimes carries a cross-reference after
        # it -- *Memphis. Compare no.ph (H5297)* -- so the sentence break is a separator like the
        # others, or the name comes back with the whole cross-reference attached to it.
        for part in re.split(r'[,;.]', row['kjv_definition'] or ''):
            part = part.strip(' .+')
            if part and not RENDERING_NOISE.match(part):
                names.add(part)
        if names:
            found[row['number']] = sorted(names)
    return found


# What the gazetteer puts in front of a name and the lexicon does not: OpenBible files the hill as
# *Mount Gilboa* and the wadi as *Valley of Eshcol*, where Strong writes *Gilboa, a mountain of
# Palestine*. A hundred and twelve held places carry one. Stripping it is not a loose match -- it is
# reading a naming convention -- so the bare name is indexed beside the full one.
FEATURE = re.compile(
    r'^(?:mount|mt\.?|the|valley of|valley|wilderness of|wilderness|desert of|desert|river of'
    r'|river|brook of|brook|sea of|sea|city of|city|land of|land|plain of|plain|plains of'
    r'|hill of|hill|rock of|rock|gate of|gate|pool of|pool|well of|well|spring of|springs of'
    r'|spring|springs|tower of|tower|cave of|cave|gulf of|lake|region of|district of|garden of'
    r'|house of|field of|road to|way to|waters of|waters|upper|lower|gate)\s+',
    re.IGNORECASE)


def forms(name):
    """A name and the same name with the gazetteer's feature word taken off the front."""
    name = (name or '').strip()
    if not name:
        return set()
    bare = FEATURE.sub('', name).strip()
    return {name, bare} if bare else {name}


def candidate_names(row, kjv):
    """Every string this entry could be called, for a held label to be met by."""
    names = set(forms(row['transliteration']))
    head = HEAD_NAME.match(row['definition'] or '')
    if head:
        names |= forms(head.group(1))
        # *Aphek (or Aphik)* and *Joppe (i.e. Japho)* -- the alternative spelling is a name the
        # entry offers, and it is often the one the gazetteer chose.
        for alternative in re.findall(r'\((?:or|i\.e\.) ([^)]+)\)', row['definition']):
            names |= forms(alternative)
    for form in kjv.get(row['number'], []):
        names |= forms(form)
    return {name for name in names if name}


def held_names(place):
    names = forms(place['name'])
    for label in (place.get('labels') or []):
        names |= forms(label)
    return {name for name in names if name}


def reach(entries, kjv, index, key=normalise):
    reached, missed = [], []
    for row in entries:
        names = candidate_names(row, kjv)
        hits = {place['id']: place
                for name in names for place in index.get(key(name), ())}
        (reached if hits else missed).append((row, sorted(names), list(hits.values())))
    return reached, missed


def match(args):
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)
    entries = [row for row in rows if row['tier'] in KEPT]
    with open(os.path.join(directory, 'sample.json'), encoding='utf-8') as handle:
        chosen = [row for row in json.load(handle)['entries'] if row['tier'] in KEPT]

    held = psql(HELD_SQL) or []
    open_bible = [place for place in held if place['source'].startswith('OpenBible')]

    index, open_index, loose_index = {}, {}, {}
    for place in held:
        for name in held_names(place):
            index.setdefault(normalise(name), []).append(place)
            if place['source'].startswith('OpenBible'):
                open_index.setdefault(normalise(name), []).append(place)
                loose_index.setdefault(loosely(name), []).append(place)

    kjv = renderings([row['number'] for row in rows])

    reached, missed = reach(entries, kjv, open_index)
    any_reached, any_missed = reach(entries, kjv, index)
    slice_reached, slice_missed = reach(chosen, kjv, open_index)
    loose_reached, loose_missed = reach([row for row, _, _ in missed], kjv, loose_index, loosely)

    matched = {place['id'] for _, _, hits in reached for place in hits}
    unreached = [place for place in open_bible if place['id'] not in matched]

    lines = ['# The OpenBible link, as a claim of ours rather than an inheritance', '',
             'Today `open_bible_id` sits on an entity that came out of OpenBible, so the link is '
             'inherited and cannot be wrong. On a record of our own it is a claim, established by '
             'matching the name the lexicon gives against the name the gazetteer gives, and it is '
             'measured here in both directions.', '',
             '## Ours reaching theirs', '',
             f'- entries the free pass keeps as places: **{len(entries)}**',
             f'- reaching an OpenBible record by name: **{len(reached)}** '
             f'({len(reached) / max(1, len(entries)):.1%})',
             f'- reaching a held place of any source: **{len(any_reached)}** '
             f'({len(any_reached) / max(1, len(entries)):.1%})',
             f'- reaching nothing held at all: **{len(any_missed)}**',
             f'- of the {len(missed)} that miss OpenBible strictly, reached once the two spellings '
             f'are folded to consonants: **{len(loose_reached)}** '
             f'(so {len(reached) + len(loose_reached)} at the loose upper bound, '
             f'{(len(reached) + len(loose_reached)) / max(1, len(entries)):.1%})', '',
             '## Theirs reached by ours -- the number that says what the reader would lose', '',
             f'- OpenBible place entities held: **{len(open_bible)}**',
             f'- reached by an entry of ours: **{len(matched)}** '
             f'({len(matched) / max(1, len(open_bible)):.1%})',
             f'- reached by nothing of ours: **{len(unreached)}**', '',
             '## The same on the pilot slice, so the projection can be checked', '',
             f'- slice entries kept as places: **{len(chosen)}**',
             f'- reaching an OpenBible record: **{len(slice_reached)}** '
             f'({len(slice_reached) / max(1, len(chosen)):.1%})',
             f'- reaching none: **{len(slice_missed)}**', '',
             '## Our failures, read rather than counted', '']
    for row, names, _ in slice_missed[:40]:
        lines.append(f'- `{row["number"]}` {row["definition"][:130]}')
        lines.append(f'  tried: {", ".join(names) or "nothing"} '
                     f'({row["words"]} words in the corpus)')

    lines += ['', '## What the fold newly joins, to be read for what it joins wrongly', '',
              'The loose key is an upper bound and not a matcher: it drops vowels, so *Dan* and '
              '*Don* fold together. What it newly reaches has to be read before the number above '
              'is believed.', '']
    for row, _, hits in loose_reached[:25]:
        head = HEAD_NAME.match(row['definition'] or '')
        lines.append(f'- `{row["number"]}` {(head.group(1) if head else "?").strip()} '
                     f'-> {", ".join(place["name"] for place in hits[:4])}')

    lines += ['', '## What ours would add -- entries reaching nothing we hold, read rather than counted',
              '', f'{len(any_missed)} of them. This is the half a borrowed list cannot have: a name '
              'the lexicon carries and the gazetteer does not.', '']
    for row, names, _ in any_missed[:30]:
        lines.append(f'- `{row["number"]}` {row["definition"][:120]}')
    if len(any_missed) > 30:
        lines.append(f'- and {len(any_missed) - 30} more')

    other = [place for place in held if not place['source'].startswith('OpenBible')]
    other_reached = {place['id'] for _, _, hits in any_reached for place in hits
                     if not place['source'].startswith('OpenBible')}
    lines += ['', '## The hundred and eighteen that are not OpenBible\'s', '',
              f'- held places from another source: **{len(other)}**',
              f'- reached by an entry of ours: **{len(other_reached)}** '
              f'({len(other_reached) / max(1, len(other)):.1%})', '']

    lines += ['', '## Their records nothing of ours reaches, read rather than counted', '']
    for place in unreached[:40]:
        lines.append(f'- {place["name"]} (`{place["open_bible_id"]}`)')
    if len(unreached) > 40:
        lines.append(f'- and {len(unreached) - 40} more')

    lines += ['', '## Where one entry reaches several records', '',
              'A name Strong gives once and the gazetteer surveys twice. Strong states the count '
              'for some of these outright, which is what makes the split ours rather than theirs.', '']
    several = [(row, hits) for row, _, hits in reached if len(hits) > 1]
    lines.append(f'{len(several)} entries reach more than one OpenBible record.')
    lines.append('')
    for row, hits in several[:25]:
        stated = row['stated_places']
        lines.append(f'- `{row["number"]}` -> {len(hits)} records '
                     f'({", ".join(place["name"] for place in hits[:6])})'
                     + (f' -- Strong states {stated}' if stated else ''))

    with open(os.path.join(directory, 'match.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines[:30]))
    print(f'... written to {os.path.join(directory, "match.md")}')
    return 0


# ---------------------------------------------------------------------------- the projection


def project(args):
    """
    What the whole pass would yield and cost, from the rates the slice measured. The sample's shape
    is printed beside every projection, because a number whose denominator is not shown is a number
    nobody can check.
    """
    directory = args.dir
    with open(os.path.join(directory, 'candidates.json'), encoding='utf-8') as handle:
        rows = json.load(handle)
    with open(os.path.join(directory, 'out', 'ask.jsonl'), encoding='utf-8') as handle:
        read = [json.loads(line) for line in handle if line.strip()]
    with open(os.path.join(directory, 'out', 'ask.json'), encoding='utf-8') as handle:
        run = json.load(handle)
    with open(os.path.join(directory, 'occurrences.json'), encoding='utf-8') as handle:
        standing = json.load(handle)['per_number']

    answered = [row for row in read if row['model_place'] is not None]
    per_entry = run['cost'] / max(1, len(answered))
    per_second = run['seconds'] / max(1, len(answered))

    population = {tier: sum(1 for row in rows if row['tier'] == tier) for tier in TIERS}
    lines = ['# What the register would hold, and what it would cost', '',
             f"Measured with {run['model']} at effort {run['effort'] or 'the CLI default'}: "
             f"{len(answered)} entries, ${run['cost']:.4f}, {run['seconds']:.0f}s wall clock at "
             f"{args.workers} workers -- **${per_entry:.5f} an entry, {per_second:.2f}s an entry**.",
             '', '## The slice against the population', '',
             '| tier | population | in the slice | the model agreed | it says place |',
             '|---|---|---|---|---|']

    projected = 0.0
    for tier in TIERS:
        kept = [row for row in answered if row['tier'] == tier]
        if not kept:
            lines.append(f'| {tier} | {population[tier]} | 0 | -- | -- |')
            continue
        agree = sum(1 for row in kept if bool(row['model_place']) == bool(row['free_place']))
        places = sum(1 for row in kept if row['model_place'])
        rate = places / len(kept)
        projected += rate * population[tier]
        lines.append(f'| {tier} | {population[tier]} | {len(kept)} | {agree}/{len(kept)} '
                     f'({agree / len(kept):.0%}) | {places}/{len(kept)} ({rate:.0%}) |')

    free = sum(population[tier] for tier in KEPT)
    lines += ['', '## The register the two passes yield', '',
              f'- the free pass alone keeps **{free}** entries as places',
              f'- read at the measured rates, the whole net yields **{projected:.0f}**',
              f'- the reading pass alone, over the {population["read"]} the free pass cannot settle, '
              f'adds **{sum(1 for row in answered if row["tier"] == "read" and row["model_place"]) / max(1, sum(1 for row in answered if row["tier"] == "read")) * population["read"]:.0f}**',
              '']

    stated = [row for row in rows if row['stated_places']]
    extra = sum(row['stated_places'] - 1 for row in stated)
    lines += [f'- Strong states a count above one for **{len(stated)}** entries, which is '
              f'**{extra}** places more than one record each', '']

    lines += ['## Cost, at the rate the slice measured', '', '| policy | entries | cost | wall clock |',
              '|---|---|---|---|']
    for what, count in (('read only what the free pass cannot settle', population['read']),
                        ('read everything the net caught, to catch the free pass out too',
                         len(rows))):
        lines.append(f'| {what} | {count} | ${count * per_entry:.2f} | '
                     f'{count * per_second / 60:.1f} min |')

    words = {tier: sum(where['words']
                       for row in rows if row['tier'] == tier
                       for where in standing.get(row['number'], {}).values())
             for tier in TIERS}
    lines += ['', '## What is left after the register exists', '',
              'Enumerating a place is not assigning its occurrences. Where one name is several '
              'places, every word standing at that number has to be told apart, which is the sense '
              'pass over again.', '',
              f'- words standing at an entry Strong states more than one place for: '
              f'**{sum(where["words"] for row in stated for where in standing.get(row["number"], {}).values())}**',
              f'- words standing at anything the free pass keeps: **{sum(words[tier] for tier in KEPT)}**',
              f'- words standing at what has to be read first: **{words["read"]}**', '']

    with open(os.path.join(directory, 'project.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))
    return 0


# ---------------------------------------------------------------------------- entry


def main():
    parser = argparse.ArgumentParser(description='What a place register of our own would hold.')
    sub = parser.add_subparsers(dest='command', required=True)

    lister = sub.add_parser('enumerate', help='the free pass over the lexicon')
    lister.add_argument('--out', default='.places/run')
    lister.set_defaults(run=enumerate_)

    stander = sub.add_parser('occurrences', help='where the candidates stand, per text')
    stander.add_argument('--dir', default='.places/run')
    stander.set_defaults(run=occurrences)

    witnesser = sub.add_parser('witness', help="BHSA's own name types against the lexicon's")
    witnesser.add_argument('--dir', default='.places/run')
    witnesser.set_defaults(run=witness)

    comparer = sub.add_parser('compare', help='ours against what we hold and against OpenBible')
    comparer.add_argument('--dir', default='.places/run')
    comparer.set_defaults(run=compare)

    sampler = sub.add_parser('sample', help='the pilot slice')
    sampler.add_argument('--dir', default='.places/run')
    sampler.add_argument('--size', type=int, default=120)
    sampler.add_argument('--seed', type=int, default=457)
    sampler.set_defaults(run=sample)

    asker = sub.add_parser('ask', help='read the slice with a model')
    asker.add_argument('--dir', default='.places/run')
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--effort', default=None,
                       help='passed straight to the CLI; leaving it off is the extended default')
    asker.add_argument('--tier', default=None)
    asker.add_argument('--workers', type=int, default=3)
    asker.set_defaults(run=ask)

    matcher = sub.add_parser('match', help='the OpenBible match on the slice')
    matcher.add_argument('--dir', default='.places/run')
    matcher.set_defaults(run=match)

    projector = sub.add_parser('project', help='what the whole pass would yield and cost')
    projector.add_argument('--dir', default='.places/run')
    projector.add_argument('--workers', type=int, default=3,
                           help='what the measured wall clock was taken at')
    projector.set_defaults(run=project)

    args = parser.parse_args()
    return args.run(args)


if __name__ == '__main__':
    raise SystemExit(main())
