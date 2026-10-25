"""
What a record is known for, written through the project's Claude API client (scripts/claude_api.py) on the
owner's key, claude-sonnet-5-5 only, in three separate readings:

  draft   a first reader writes the English clauses from the record's verses in the King James Version, each
          with the verses it rests on and a quote from them, or declines a record its verses only list;
  check   a second reader judges every clause against the verses it cites (right, fix, drop) and renders the
          clauses it keeps into Ukrainian, German and Spanish beside the Ohienko, Luther 1912 and
          Reina-Valera 1909 text of those verses;
  review  a third reader reads the three renderings against the English and puts right what is wrong.

Every answer is checked here before it is kept (shape, references inside the verses shown, the quote in the
cited verses, form, the LORD in each language); an answer refused for that is asked once more with the reasons.

    python kf.py candidates
    python kf.py ask <draft|check|review> <slug>...        # now, full price (the pilot)
    python kf.py batch <draft|check|review> [--redo] [--limit N] [--only slug,slug]
    python kf.py collect <batch-id>
    python kf.py assemble
    python kf.py spent

The export (verse texts of KJV, UBIO, LUTH1912 and RV1909 by canonical address, and the records) is read once
from the corpus by export.sh into KF_EXPORT; the verse texts are not kept in git.
"""
import csv
import collections
import datetime as dt
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPORT = Path(os.environ.get('KF_EXPORT', HERE / 'export'))
MAIN = Path(os.environ.get('KF_MAIN', HERE.parents[5]))  # the main checkout, which holds .env and the ledger
sys.path.insert(0, str(MAIN / 'scripts'))
import claude_api  # noqa: E402

MODEL = 'claude-sonnet-5-5'
LABEL = 'known-for-TSK-1210'
BUDGET_USD = 120.0
RAW = HERE / 'raw'
PROMPT_VERSION = 'v4'
KINDS = ('draft', 'check', 'review')

CODES = ["GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR",
         "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO",
         "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM",
         "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE",
         "2PE", "1JN", "2JN", "3JN", "JUD", "REV"]
ORDINAL = {code: i + 1 for i, code in enumerate(CODES)}
REF = re.compile(r"^([1-3]?[A-Z]{2,3}) (\d+):(\d+)(?:-(?:(\d+):)?(\d+))?$")


# ---------------------------------------------------------------- the export

def read_verses(slug):
    out = {}
    for b, c, v, _, text in csv.reader(open(EXPORT / f'verses_{slug}.csv', encoding='utf-8')):
        key = (int(b), int(c), int(v))
        if key[0] > 66:
            continue
        out[key] = (out[key] + ' ' + text.strip()) if key in out else text.strip()
    return out


_cache = {}


def verses(slug='KJV'):
    if slug not in _cache:
        _cache[slug] = read_verses(slug)
    return _cache[slug]


def last_verse(book, chapter):
    if 'last' not in _cache:
        last = {}
        for b, c, v in verses():
            last[(b, c)] = max(v, last.get((b, c), 0))
        _cache['last'] = last
    return _cache['last'].get((book, chapter), 1)


def addr(key):
    return f'{CODES[key[0] - 1]} {key[1]}:{key[2]}'


def normal(ref):
    """A reference as the record files write it: the book once, even where the answer repeated it after the dash."""
    return re.sub(r"^([1-3]?[A-Z]{2,3}) (\d+:\d+)-\1 (\d+:\d+)$", r"\1 \2-\3", ref.strip())


def expand(ref):
    """The verses a reference names, or None if it is not one."""
    m = REF.match(normal(ref))
    if not m or m.group(1) not in ORDINAL:
        return None
    book = ORDINAL[m.group(1)]
    c1, v1 = int(m.group(2)), int(m.group(3))
    if m.group(5) is None:
        c2, v2 = c1, v1
    else:
        c2 = int(m.group(4)) if m.group(4) else c1
        v2 = int(m.group(5))
    if (c2, v2) < (c1, v1):
        return None
    kjv = verses()
    if (book, c1, v1) not in kjv or (book, c2, v2) not in kjv:
        return None
    out = []
    for c in range(c1, c2 + 1):
        v = v1 if c == c1 else 1
        last = v2 if c == c2 else 200
        while v <= last:
            if (book, c, v) in kjv:
                out.append((book, c, v))
            elif c != c2:
                break
            v += 1
    return out


def records():
    rows = {}
    for r in csv.reader(open(EXPORT / 'entities.csv', encoding='utf-8')):
        rows[int(r[0])] = dict(id=int(r[0]), slug=r[1], kind=r[2], name=r[3], distinguisher=r[4] or None,
                               sex=r[5] or None, subtype=r[6] or None, place_kind=r[7] or None, alias=bool(r[8]))
    names = {i: {} for i in rows}
    for i, lang, form in csv.reader(open(EXPORT / 'name_forms.csv', encoding='utf-8')):
        if int(i) in names:
            names[int(i)][lang] = form
    mentioned = collections.defaultdict(set)
    for i, b, c, v, n in csv.reader(open(EXPORT / 'entity_verses.csv', encoding='utf-8')):
        key = (int(b), int(c), int(v))
        if key[0] <= 66:
            mentioned[int(i)].add(key)
    relations = collections.defaultdict(list)
    for i, _, relation, _, target, _, b, c, v in csv.reader(open(EXPORT / 'descriptors.csv', encoding='utf-8')):
        relations[int(i)].append((relation, target, (int(b), int(c), int(v))))
    events = collections.defaultdict(list)
    for i, name, kind, description, b, c, v in csv.reader(open(EXPORT / 'events.csv', encoding='utf-8')):
        key = (int(b), int(c), int(v)) if b and c and v else None
        events[int(i)].append((name, description or None, key))
    titles = collections.defaultdict(list)
    for i, _, name, b, c, v in csv.reader(open(EXPORT / 'titles.csv', encoding='utf-8')):
        titles[int(i)].append((name, (int(b), int(c), int(v))))
    passages = collections.defaultdict(list)
    for i, role, b, c1, v1, c2, v2, note in csv.reader(open(EXPORT / 'passages.csv', encoding='utf-8')):
        passages[int(i)].append((role, int(b), int(c1), int(v1) if v1 else 1, int(c2), int(v2) if v2 else 200))
    held = {int(r[0]) for r in csv.reader(open(EXPORT / 'known_for_held.csv', encoding='utf-8'))}
    for i, r in rows.items():
        r.update(names=names[i], mentioned=mentioned[i], relations=relations[i], events=events[i],
                 titles=titles[i], passages=passages[i], held=i in held)
    return rows


def by_slug():
    return {r['slug']: r for r in records().values()}


# ---------------------------------------------------------------- candidates

CRITERIA = {
    'person': 'not another name for a record, no line yet, and named in at least 5 verses, or in fewer but with '
              'an event, a title or a key passage of its own in the corpus',
    'place': 'not another name for a record, and named in at least 5 verses',
    'object': 'every object named in at least one verse',
    'all': 'the first reader may decline a record whose verses only list it (a genealogy, a census, a list of '
           'towns or a boundary) or say no more than its summary; a declined record gets no line',
}


def is_candidate(r):
    if r['alias'] or r['held'] or not r['mentioned']:
        return False
    n = len(r['mentioned'])
    if r['kind'] == 'person':
        return n >= 5 or bool(r['events'] or r['titles'] or r['passages'])
    if r['kind'] == 'place':
        return n >= 5
    return r['kind'] == 'object'


def cmd_candidates():
    rs = records()
    chosen = sorted((r for r in rs.values() if is_candidate(r)),
                    key=lambda r: (['person', 'place', 'object'].index(r['kind']), -len(r['mentioned']), r['slug']))
    out = {'criteria': CRITERIA, 'export': (EXPORT / 'SHA256SUMS').read_text(encoding='utf-8').splitlines(),
           'counts': collections.Counter(r['kind'] for r in chosen),
           'records': [{'slug': r['slug'], 'kind': r['kind'], 'name': r['name'], 'verses': len(r['mentioned']),
                        'events': len(r['events']), 'titles': len(r['titles']), 'passages': len(r['passages'])}
                       for r in chosen]}
    json.dump(out, open(HERE / 'candidates.json', 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
    print(dict(out['counts']), len(chosen))


def candidates():
    return [c['slug'] for c in json.load(open(HERE / 'candidates.json', encoding='utf-8'))['records']]


# ---------------------------------------------------------------- the first reading

DRAFT_SYSTEM = """You write the «known for» line of a record in the encyclopedia of a Bible reader. The record's page already shows its summary: how a person is related (son of, father of, wife of), the offices they held (king of Judah, prophet to Ahab), where they came from or lived. The «known for» line adds what the summary cannot: what the person did or what happened to them, in a few short clauses, each resting on the verses that say it.

Lines the editor has accepted:
- Noah: built the ark and was saved from the Flood with his family (GEN 6:13-8:19); received God's covenant never again to destroy the earth by a flood (GEN 9:8-17)
- Elijah: foretold a drought and was fed by ravens (1KI 17:1-6); prayed on Mount Carmel, and the fire of the LORD fell on the sacrifice (1KI 18:20-39); was taken up to heaven in a whirlwind (2KI 2:11)
- Sarah: gave her maid Hagar to Abraham as a wife (GEN 16:1-3); laughed at the promise of a son, then bore Isaac in her old age (GEN 18:10-15, GEN 21:1-7)
- Naboth: refused to give King Ahab his vineyard (1KI 21:1-3); was falsely accused by Jezebel's plot and stoned (1KI 21:7-14)
- Cornelius: saw an angel in a vision and sent for Peter (ACT 10:1-8); received the Holy Ghost with those in his house and was baptized (ACT 10:44-48)
- Rahab: hid the two spies in Jericho (JOS 2:1-16); was spared with her family when the city fell (JOS 6:22-25)

How to write it:
1. Say only what the verses you are given state. Nothing from tradition, theology, interpretation, chronology or other books, and nothing you know of the record from verses you were not given. A figure of speech, a comparison or a blessing pronounced is not an event.
2. Each clause is one deed of the record or one thing that happened to it, in the past tense, with the record as the unstated subject: begin with the verb, in lower case ("built…", "was taken up…"). Plain modern English; names of people and places as the King James spells them, modernised only where it joins two words (Pharaoh Necho); God as the verses call him (God, the LORD).
3. Only the chief things, the ones a reader would name the record for. Fewer is better: a record whose story is one event gets one clause; most get one or two; three for a life told at length; four or five only for the very greatest, where each is a major event. One event is one clause, never split into its steps (not the welcome, the deed and its aftermath as three clauses), and two events are never joined into one clause. No minor detail inside a clause: the editor struck "and God provided a ram" from Abraham's binding of Isaac. Leave out what surrounds the deed — who saw it, what someone thought or said of it, how it was done, what was eaten, worn or carried — unless that is what the record is known for. Most clauses are 40 to 80 characters.
4. Do not restate the summary. Being someone's son, father, wife or brother, holding an office, coming from a place: none of that is a clause. An event can be a clause (was anointed king by Samuel; was made governor over the land), and where the summary names a deed only as a relation (killer of Sisera), the deed itself, told once as it happened, can be.
5. A clause is at most about 100 characters; the whole line at most about 220.
6. Each clause cites the verses that state it: a list of runs, each "BOOK C:V", "BOOK C:V-V" or "BOOK C:V-C:V", with the book codes shown, and only verses you were given. Cite narrowly — the verses that say it, not the whole chapter.
7. quote: copy, word for word and in its spelling, the few words (at most 15) of one cited verse that most directly state the clause.
8. Verses marked • name the record; the others are context around them. A marked verse may concern someone or somewhere else of the same name: leave such a verse out, and say so in doubts.
9. Decide "none" — no line — when the verses only list the record (a genealogy, a list of names, a census, a list of towns, a boundary, a stage of a journey) or say no more of it than the summary does. A line that pads a name in a list is worse than no line. Give the reason in one sentence.
10. A place: what happened there or to it, with the place as the unstated subject ("was destroyed by brimstone and fire from the LORD"; "was where Jacob dreamed of a ladder reaching to heaven"). A place is worth a line only where something of the story happened there or to it. A whole land or region (Egypt, Canaan, Assyria), a sea or a great river gets no line unless one or two events of the story belong to it alone; a great city, only its few defining events; never a summary of its history.
11. An object: what was made, done with it or happened through it, with the object as the unstated subject.
12. The line is read by readers. Never speak of the text, the verses, the record, sources or this task in a clause.

Answer with the JSON the schema asks for: decision, reason (one sentence; may be empty when you write), clauses (empty when none), doubts (an empty list if there are none)."""

DRAFT_SCHEMA = {
    'type': 'object', 'additionalProperties': False,
    'required': ['decision', 'reason', 'clauses', 'doubts'],
    'properties': {
        'decision': {'type': 'string', 'enum': ['write', 'none']},
        'reason': {'type': 'string'},
        'clauses': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False,
            'required': ['english', 'references', 'quote'],
            'properties': {'english': {'type': 'string'},
                           'references': {'type': 'array', 'items': {'type': 'string'}},
                           'quote': {'type': 'string'}}}},
        'doubts': {'type': 'array', 'items': {'type': 'string'}},
    },
}

MAX_CONTEXT = 4000
GAP = 3


def context_keys(r):
    kjv = verses()
    seeds = set(r['mentioned'])
    seeds |= {key for *_, key in r['relations']}
    seeds |= {key for *_, key in r['events'] if key}
    seeds |= {key for _, key in r['titles']}
    for _, b, c1, v1, c2, v2 in r['passages']:
        if b <= 66:
            run = expand(f'{CODES[b - 1]} {c1}:{v1}-{c2}:{min(v2, last_verse(b, c2))}') or []
            seeds |= set(run[:80])
    seeds = {k for k in seeds if k in kjv}
    for width in (2, 1, 0):
        keys = set()
        for b, c, v in seeds:
            for d in range(-width, width + 1):
                if (b, c, v + d) in kjv:
                    keys.add((b, c, v + d))
        ordered = sorted(keys)
        for a, z in zip(ordered, ordered[1:]):
            if (a[0], a[1]) == (z[0], z[1]) and 1 < z[2] - a[2] <= GAP + 1:
                keys |= {(a[0], a[1], v) for v in range(a[2] + 1, z[2]) if (a[0], a[1], v) in kjv}
        if len(keys) <= MAX_CONTEXT or width == 0:
            return sorted(keys), width


def describe(r):
    lines = [f'Record: {r["name"]} ({r["kind"]}{", " + r["sex"] if r["sex"] else ""})']
    if r['distinguisher']:
        lines.append(f'Told apart from namesakes as: {r["distinguisher"]}')
    if r['place_kind'] or r['subtype']:
        lines.append(f'Kind: {r["place_kind"] or r["subtype"]}')
    if r['relations']:
        lines.append('Its summary already says: ' + '; '.join(
            f'{relation.replace("-", " ")} {target} ({addr(key)})' for relation, target, key in r['relations'][:40]))
    if r['titles']:
        lines.append('Titles: ' + '; '.join(f'{name} ({addr(key)})' for name, key in r['titles']))
    if r['events']:
        lines.append('Events a dataset lists: ' + '; '.join(
            f'{name}{" — " + d if d else ""}{" (" + addr(k) + ")" if k else ""}' for name, d, k in r['events'][:30]))
    return '\n'.join(lines)


def draft_prompt(r):
    keys, width = context_keys(r)
    kjv = verses()
    body = [describe(r), '', f'The verses (King James Version; • names the record; the others are context around '
                             'them):']
    prev = None
    for key in keys:
        if prev and (key[0], key[1]) == (prev[0], prev[1]) and key[2] != prev[2] + 1:
            body.append('…')
        elif prev and (key[0], key[1]) != (prev[0], prev[1]):
            body.append('')
        body.append(f'{"•" if key in r["mentioned"] else " "} {addr(key)} {kjv[key]}')
        prev = key
    return '\n'.join(body), keys


# ---------------------------------------------------------------- the second reading

RENDERING_RULES = """- the same content as the English, nothing added or left out;
- the same form: the record is the unstated subject and the clause begins, in lower case, with its verb (or the verb's auxiliary), exactly as the English does. Never turn a clause round to make someone else its subject: «був названий Яковом Бет-Елом», not «Яків назвав це місце Бет-Елом»;
- modern standard language, as an encyclopedia writes today: take only the names from the translations, never their old wording («Weiber», «огонь», «пущена з огнем»);
- names of people and places as that translation of these verses spells them, declined as the sentence needs;
- the LORD is always «Господь» (declined), «der HERR» (declined: des HERRN, dem HERRN), «el SEÑOR» — also where a translation writes Jehová;
- the verb agrees with the record: feminine for a woman (Ukrainian «дала», «народила»; Spanish «fue salvada»); for a place or an object, with the gender and number of its own name in that language (given below);
- Ukrainian in the current standard orthography, the apostrophe written ' (зв'язав)."""

CHECK_SYSTEM = """You are the second, independent reader of the «known for» line of a record in the encyclopedia of a Bible reader. Another reader wrote the line from the record's verses in the King James Version: a few short clauses of what the person did or what happened to them (or, for a place or an object, what happened there or to it), each with the verses it rests on. The record's summary — kin, offices, origin — is shown elsewhere on the page and is not repeated in the line.

Read every clause against the verses it cites, which you are given (with a few verses of context around them), and judge it:
- right: the cited verses state everything the clause says, the record is the one who did it or to whom it happened, and nothing is added;
- fix: it can be put right from the verses shown — a wrong actor, a detail the verses do not give, a word they do not support, a citation that leaves out a verse the clause needs or takes in far more than it needs, a minor detail that does not belong in a chief deed (who saw it, what someone thought of it, how it was done). Give the corrected English and references. A fix makes the clause true by taking out what the verses do not say, or by changing the actor or the citation; it never adds detail, and the clause stays as short as it can. Where what the clause says (a name, the chief fact) is stated in a verse shown to you just outside its citation, widen the citation to take that verse in rather than taking it out of the clause;
- drop: the verses shown cannot support it, it concerns someone or somewhere else of the same name, it is a figure of speech, a comparison or a blessing pronounced rather than an event, or it only restates a kinship, an office or an origin.
why: one short sentence saying what you found (for right, an empty string).

The English keeps the line's form: past tense, the record as the unstated subject, beginning in lower case with its verb, plain modern English, names as the King James spells them, God as the verses call him, at most about 100 characters. References are runs "BOOK C:V", "BOOK C:V-V" or "BOOK C:V-C:V" of verses shown to you.

Then render each clause you keep (right or fix) into Ukrainian, German and Spanish, beside the same verses in the Ohienko Bible (Ukrainian), the Luther Bible 1912 (German) and the Reina-Valera 1909 (Spanish):
""" + RENDERING_RULES + """
For a dropped clause give empty renderings.

note: one sentence if the line leaves out something plainly more important than what it says (for the editor; no fix), otherwise an empty string.

Answer with the JSON the schema asks for, one entry per clause, in the order given."""

CHECK_SCHEMA = {
    'type': 'object', 'additionalProperties': False,
    'required': ['clauses', 'note'],
    'properties': {
        'clauses': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False,
            'required': ['verdict', 'why', 'english', 'references', 'ukr', 'deu', 'spa'],
            'properties': {'verdict': {'type': 'string', 'enum': ['right', 'fix', 'drop']},
                           'why': {'type': 'string'},
                           'english': {'type': 'string'},
                           'references': {'type': 'array', 'items': {'type': 'string'}},
                           'ukr': {'type': 'string'}, 'deu': {'type': 'string'}, 'spa': {'type': 'string'}}}},
        'note': {'type': 'string'},
    },
}

TRANSLATIONS = (('UBIO', 'Ohienko (Ukrainian)'), ('LUTH1912', 'Luther 1912 (German)'),
                ('RV1909', 'Reina-Valera 1909 (Spanish)'))


def cited_and_shown(clauses, width=2):
    kjv = verses()
    cited = set()
    for clause in clauses:
        for ref in clause['references']:
            cited |= set(expand(ref) or [])
    shown = {(b, c, v + d) for b, c, v in cited for d in range(-width, width + 1)} & set(kjv)
    return sorted(cited), sorted(shown)


def names_line(r):
    forms = r['names']
    return 'Its name: ' + '; '.join(f'{lang} {forms[lang]}' for lang in ('ukr', 'deu', 'spa') if lang in forms)


def parallels(keys):
    lines = []
    for slug, title in TRANSLATIONS:
        text = verses(slug)
        lines += ['', f'{title}, the same verses by canonical address:']
        lines += [f'{addr(key)} {text[key]}' for key in keys if key in text]
    return lines


def check_prompt(r, draft):
    kjv = verses()
    cited, shown = cited_and_shown(draft['clauses'])
    lines = [describe(r), names_line(r), '', 'The line:']
    for n, clause in enumerate(draft['clauses'], 1):
        lines.append(f'{n}. {clause["english"]} ({", ".join(clause["references"])})')
    lines += ['', 'King James Version (* cited by a clause; the rest is context):']
    lines += [f'{"*" if key in cited else " "} {addr(key)} {kjv[key]}' for key in shown]
    _, near = cited_and_shown(draft['clauses'], width=1)
    return '\n'.join(lines + parallels(near))


# ---------------------------------------------------------------- the third reading

REVIEW_SYSTEM = """You are the third reader of the «known for» line of a record in the encyclopedia of a Bible reader: you read its Ukrainian, German and Spanish renderings against the English, which is final. The line is shown under a heading — «Known for» / «Відомий тим, що» (a woman: «Відома тим, що») / «Bekannt für» / «Conocido porque» (a woman: «Conocida porque») — as a list of clauses.

Put right anything that breaks these rules, and leave everything else exactly as it is:
""" + RENDERING_RULES + """
- correct grammar, case and agreement throughout; natural word order; no word left in English.

For each clause give the three final texts (unchanged where right) and why: one short sentence in English naming what you changed, or an empty string if nothing.

Answer with the JSON the schema asks for, one entry per clause, in the order given."""

REVIEW_SCHEMA = {
    'type': 'object', 'additionalProperties': False,
    'required': ['clauses'],
    'properties': {
        'clauses': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False,
            'required': ['ukr', 'deu', 'spa', 'why'],
            'properties': {'ukr': {'type': 'string'}, 'deu': {'type': 'string'}, 'spa': {'type': 'string'},
                           'why': {'type': 'string'}}}},
    },
}


def kept(check):
    return [c for c in check['clauses'] if c['verdict'] != 'drop']


def review_prompt(r, check):
    clauses = kept(check)
    _, near = cited_and_shown(clauses, width=0)
    lines = [f'Record: {r["name"]} ({r["kind"]}{", " + r["sex"] if r["sex"] else ""})', names_line(r), '']
    for n, c in enumerate(clauses, 1):
        lines += [f'{n}. English: {c["english"]} ({", ".join(c["references"])})',
                  f'   Ukrainian: {c["ukr"]}', f'   German: {c["deu"]}', f'   Spanish: {c["spa"]}']
    return '\n'.join(lines + parallels(near))


# ---------------------------------------------------------------- requests

SYSTEMS = {'draft': DRAFT_SYSTEM, 'check': CHECK_SYSTEM, 'review': REVIEW_SYSTEM}
SCHEMAS = {'draft': DRAFT_SCHEMA, 'check': CHECK_SCHEMA, 'review': REVIEW_SCHEMA}


def prompt_of(kind, r):
    if kind == 'draft':
        return draft_prompt(r)[0]
    if kind == 'check':
        return check_prompt(r, load('draft', r['slug'])['answer'])
    return review_prompt(r, load('check', r['slug'])['answer'])


def params(kind, r, feedback=None):
    content = prompt_of(kind, r)
    if feedback:
        content += ('\n\nAn earlier answer to this was refused, for these reasons:\n- ' + '\n- '.join(feedback['problems'])
                    + '\nThe earlier answer was:\n' + json.dumps(feedback['answer'], ensure_ascii=False)
                    + '\nAnswer again, avoiding those faults.')
    return {'model': MODEL, 'max_tokens': 32000,
            'system': [{'type': 'text', 'text': SYSTEMS[kind], 'cache_control': {'type': 'ephemeral'}}],
            'messages': [{'role': 'user', 'content': content}],
            'output_config': {'effort': 'high', 'format': {'type': 'json_schema', 'schema': SCHEMAS[kind]}}}


# ---------------------------------------------------------------- validation

def norm(text):
    return re.sub(r'[^\w]+', ' ', text.lower()).strip()


LEAKS = re.compile(r'\b(the text|the verses?|this record|the record|dataset|sources?|scripture says|bible says)\b',
                   re.I)


def problems_of_clause(n, english, references, shown):
    found = []
    if not english.strip():
        return [f'clause {n}: empty']
    if not english[0].islower():
        found.append(f'clause {n}: does not begin, in lower case, with its verb')
    if len(english) > 140:
        found.append(f'clause {n}: {len(english)} characters')
    if LEAKS.search(english):
        found.append(f'clause {n}: speaks of the text')
    if not references:
        found.append(f'clause {n}: cites nothing')
    for ref in references:
        run = expand(ref)
        if not run:
            found.append(f'clause {n}: {ref!r} is no run of verses')
        elif shown is not None and not set(run) <= shown:
            found.append(f'clause {n}: {ref} cites verses that were not shown')
    return found


def problems_of_rendering(n, english, ukr, deu, spa):
    found = []
    for lang, text in (('ukr', ukr), ('deu', deu), ('spa', spa)):
        if not text.strip():
            found.append(f'clause {n}: no {lang}')
        elif not text[0].islower():
            found.append(f'clause {n}: the {lang} does not begin in lower case')
    if re.search(r'[A-Za-z]{2,}', ukr):
        found.append(f'clause {n}: Latin letters in the Ukrainian')
    if re.search(r'Jehov(?!á-)', spa):
        found.append(f'clause {n}: the Spanish writes Jehová for the LORD')
    if 'LORD' in english:
        if 'SEÑOR' not in spa:
            found.append(f'clause {n}: the LORD is not «el SEÑOR» in the Spanish')
        if 'HERR' not in deu:
            found.append(f'clause {n}: the LORD is not «der HERR» in the German')
        if 'Господ' not in ukr:
            found.append(f'clause {n}: the LORD is not «Господь» in the Ukrainian')
    return found


def problems(kind, r, answer):
    slug = r['slug']
    if kind == 'draft':
        _, keys = draft_prompt(r)
        shown = set(keys)
        if answer['decision'] == 'none':
            return ['declined, yet gave clauses'] if answer['clauses'] else []
        found = [] if 1 <= len(answer['clauses']) <= 5 else [f'{len(answer["clauses"])} clauses; at most five']
        for n, clause in enumerate(answer['clauses'], 1):
            found += problems_of_clause(n, clause['english'], clause['references'], shown)
            cited = [k for ref in clause['references'] for k in (expand(ref) or [])]
            if norm(clause['quote']) not in norm(' '.join(verses()[k] for k in cited)):
                found.append(f'clause {n}: its quote is not word for word in the verses it cites')
        return found
    if kind == 'check':
        draft = load('draft', slug)['answer']
        if len(answer['clauses']) != len(draft['clauses']):
            return [f'{len(answer["clauses"])} verdicts for {len(draft["clauses"])} clauses']
        _, shown = cited_and_shown(draft['clauses'])
        found = []
        for n, clause in enumerate(answer['clauses'], 1):
            if clause['verdict'] == 'drop':
                continue
            found += problems_of_clause(n, clause['english'], clause['references'], set(shown))
            found += problems_of_rendering(n, clause['english'], clause['ukr'], clause['deu'], clause['spa'])
        return found
    check = load('check', slug)['answer']
    english = kept(check)
    if len(answer['clauses']) != len(english):
        return [f'{len(answer["clauses"])} renderings for {len(english)} clauses']
    found = []
    for n, (clause, source) in enumerate(zip(answer['clauses'], english), 1):
        found += problems_of_rendering(n, source['english'], clause['ukr'], clause['deu'], clause['spa'])
    return found


# ---------------------------------------------------------------- running

def save(kind, slug, payload):
    (RAW / kind).mkdir(parents=True, exist_ok=True)
    json.dump(payload, open(RAW / kind / f'{slug}.json', 'w', encoding='utf-8', newline='\n'), ensure_ascii=False,
              indent=1)


def load(kind, slug):
    path = RAW / kind / f'{slug}.json'
    return json.load(open(path, encoding='utf-8')) if path.is_file() else None


def keep(kind, r, answer, how):
    """Validates an answer and stores it with its earlier attempts; returns what is wrong with it."""
    found = problems(kind, r, answer)
    earlier = load(kind, r['slug'])
    attempts = (earlier or {}).get('attempts', []) + ([{k: earlier[k] for k in ('how', 'at', 'answer', 'problems')}]
                                                     if earlier else [])
    save(kind, r['slug'], {'slug': r['slug'], 'model': MODEL, 'prompt': PROMPT_VERSION, 'how': how,
                           'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'answer': answer,
                           'problems': found, 'attempts': attempts})
    return found


def guard():
    spent = claude_api.spent(LABEL)
    if spent > BUDGET_USD:
        raise SystemExit(f'stopped: the ledger shows ${spent:.2f} spent under {LABEL}, over ${BUDGET_USD}')
    return spent


def sound(kind, slug):
    held = load(kind, slug)
    return held is not None and not held['problems']


def ready(kind, slug):
    """Whether this reading can be asked for the record: the reading before it is sound and has something."""
    if kind == 'draft':
        return True
    if kind == 'check':
        held = load('draft', slug)
        return sound('draft', slug) and held['answer']['decision'] == 'write'
    held = load('check', slug)
    return sound('check', slug) and bool(kept(held['answer']))


def todo(kind, redo, only):
    slugs = only or candidates()
    out = []
    for slug in slugs:
        if not ready(kind, slug):
            continue
        held = load(kind, slug)
        if held is None:
            out.append((slug, None))
        elif redo and held['problems'] and len(held.get('attempts', [])) < 2:
            out.append((slug, held))
    return out


def cmd_ask(kind, slugs):
    rs = by_slug()
    for slug, feedback in todo(kind, True, slugs):
        guard()
        with claude_api.client().messages.stream(**params(kind, rs[slug], feedback)) as stream:
            message = stream.get_final_message()
        claude_api.record(LABEL, message.model, message.usage, request_id=message.id)
        if message.model != MODEL or message.stop_reason != 'end_turn':
            print(slug, 'not kept:', message.model, message.stop_reason)
            continue
        found = keep(kind, rs[slug], json.loads(claude_api.text_of(message)), 'ask')
        print(slug, kind, found, message.usage.input_tokens, message.usage.output_tokens)


def cmd_batch(kind, redo, limit, only):
    guard()
    rs = by_slug()
    work = todo(kind, redo, only)[:limit]
    if not work:
        print('nothing to ask')
        return
    batch = claude_api.batch_submit([(slug, params(kind, rs[slug], feedback)) for slug, feedback in work],
                                    label=LABEL)
    with open(HERE / 'batches.jsonl', 'a', encoding='utf-8', newline='\n') as log:
        log.write(json.dumps({'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'batch': batch, 'kind': kind,
                              'redo': redo, 'requests': len(work)}) + '\n')
    print(kind, batch, len(work))


def batch_kind(batch):
    for line in open(HERE / 'batches.jsonl', encoding='utf-8'):
        row = json.loads(line)
        if row['batch'] == batch:
            return row['kind']
    raise SystemExit(f'{batch} is not in batches.jsonl')


def cmd_collect(batch):
    kind = batch_kind(batch)
    claude_api.batch_wait(batch, poll=60)
    rs = by_slug()
    failures, refused = {}, {}
    for result in claude_api.client().messages.batches.results(batch):
        slug = result.custom_id
        if result.result.type != 'succeeded':
            failures[slug] = result.result.type
            continue
        message = result.result.message
        claude_api.record(LABEL, message.model, message.usage, batch=True, request_id=slug)
        if message.model != MODEL or message.stop_reason != 'end_turn':
            failures[slug] = f'{message.model} {message.stop_reason}'
            continue
        try:
            answer = json.loads(claude_api.text_of(message))
        except json.JSONDecodeError as error:
            failures[slug] = f'not JSON: {error}'
            continue
        found = keep(kind, rs[slug], answer, f'batch {batch}')
        if found:
            refused[slug] = found
    json.dump({'failures': failures, 'refused': refused},
              open(HERE / 'batches' / f'{batch}.json', 'w', encoding='utf-8', newline='\n'), ensure_ascii=False,
              indent=1) if (HERE / 'batches').mkdir(exist_ok=True) is None else None
    print(kind, 'collected; failed', len(failures), 'refused', len(refused), 'spent', claude_api.spent(LABEL))


def cmd_revalidate():
    """Checks every stored answer again with the checks as they stand now; no request is made."""
    rs = by_slug()
    for kind in KINDS:
        for path in sorted((RAW / kind).glob('*.json')):
            held = json.load(open(path, encoding='utf-8'))
            found = problems(kind, rs[held['slug']], held['answer'])
            if found != held['problems']:
                print(kind, held['slug'], held['problems'], '->', found)
                held['problems'] = found
                save(kind, held['slug'], held)


def cmd_spent():
    rows = [json.loads(line) for line in open(claude_api.LEDGER, encoding='utf-8') if line.strip()]
    mine = [row for row in rows if row['label'] == LABEL]
    print(f'{len(mine)} calls, {sum(r["input"] for r in mine)} in, {sum(r["output"] for r in mine)} out, '
          f'{sum(r["cache_read"] for r in mine)} cache read, ${sum(r["usd"] or 0 for r in mine):.4f}')


def flags(argv):
    redo = '--redo' in argv
    limit = int(argv[argv.index('--limit') + 1]) if '--limit' in argv else None
    only = argv[argv.index('--only') + 1].split(',') if '--only' in argv else None
    return redo, limit, only


if __name__ == '__main__':
    verb, *rest = sys.argv[1:] or ['help']
    if verb == 'candidates':
        cmd_candidates()
    elif verb == 'ask' and rest[0] in KINDS:
        cmd_ask(rest[0], rest[1:])
    elif verb == 'batch' and rest[0] in KINDS:
        cmd_batch(rest[0], *flags(rest))
    elif verb == 'collect':
        cmd_collect(rest[0])
    elif verb == 'assemble':
        import assemble
        assemble.main()
    elif verb == 'revalidate':
        cmd_revalidate()
    elif verb == 'spent':
        cmd_spent()
    else:
        print(__doc__)
