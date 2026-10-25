"""
What a record is known for, written through the project's Claude API client (scripts/claude_api.py) on the
owner's key, claude-sonnet-5-5 only, in two readings: a first reader writes the English clauses from the
record's verses in the King James Version, a second, separate reader checks every clause against the verses it
cites and renders it into Ukrainian, German and Spanish beside the Ohienko, Luther 1912 and Reina-Valera 1909
texts of those verses. Every answer is checked here before it is kept (shape, references, quote).

    python kf.py candidates                  # candidates.json from the export
    python kf.py draft-ask <slug>...         # first reading now (pilot)
    python kf.py check-ask <slug>...         # second reading now (pilot)
    python kf.py draft-batch [--limit N]     # first reading of every candidate not yet read, as a batch
    python kf.py check-batch                 # second reading of every drafted line not yet checked
    python kf.py collect <batch-id> draft|check
    python kf.py assemble                    # review file and summary
    python kf.py spent

The export (verse texts of KJV, UBIO, LUTH1912 and RV1909 by canonical address, and the records) is read once
from the corpus into KF_EXPORT (default: ./export) by export.sh; the verse texts are not kept in git.
"""
import csv
import collections
import datetime as dt
import hashlib
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
PROMPT_VERSION = 'v1'

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


def addr(key):
    return f'{CODES[key[0] - 1]} {key[1]}:{key[2]}'


def expand(ref):
    """The verses a reference names, or None if it is not one."""
    m = REF.match(ref.strip())
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
    named, mentioned = collections.defaultdict(set), collections.defaultdict(set)
    for i, b, c, v, n in csv.reader(open(EXPORT / 'entity_verses.csv', encoding='utf-8')):
        key = (int(b), int(c), int(v))
        if key[0] <= 66:
            mentioned[int(i)].add(key)
            if n == 't':
                named[int(i)].add(key)
    by_id = {r['id']: r for r in rows.values()}
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
    for i, r in by_id.items():
        r.update(names=names[i], named=named[i], mentioned=mentioned[i], relations=relations[i], events=events[i],
                 titles=titles[i], passages=passages[i], held=i in held)
    return by_id


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
1. Say only what the verses you are given state. Nothing from tradition, theology, interpretation, chronology or other books, and nothing you know of the record from verses you were not given. A figure of speech is not an event.
2. Each clause is one deed of the record or one thing that happened to it, in the past tense, with the record as the unstated subject: begin with the verb ("built…", "was taken up…"). Plain modern English; names of people and places as the King James spells them, modernised only where it joins two words (Pharaoh Necho); God as the verses call him (God, the LORD).
3. Only the chief things, the ones a reader would name the record for. Fewer is better: a record whose story is one event gets one clause; most get one or two; three for a life told at length; four or five only for the very greatest, where each is a major event. One event is one clause, never split into its steps (not the welcome, the deed and its aftermath as three clauses). No minor detail inside a clause: the editor struck "and God provided a ram" from Abraham's binding of Isaac.
4. Do not restate the summary. Being someone's son, father, wife or brother, holding an office, coming from a place: none of that is a clause. An event can be a clause (was anointed king by Samuel; was made governor over the land), and where the summary names a deed only as a relation (killer of Sisera), the deed itself, told once as it happened, can be.
5. A clause is at most about 100 characters; the whole line at most about 220.
6. Each clause cites the verses that state it: a list of runs, each "BOOK C:V", "BOOK C:V-V" or "BOOK C:V-C:V", with the book codes shown, and only verses you were given. Cite narrowly — the verses that say it, not the whole chapter.
7. quote: copy, word for word and in its spelling, the few words (at most 15) of one cited verse that most directly state the clause.
8. Verses marked • name the record; the others are context around them. A marked verse may concern someone or somewhere else of the same name: leave such a verse out, and say so in doubts.
9. Decide "none" — no line — when the verses only list the record (a genealogy, a list of names, a census, a list of towns, a boundary, a stage of a journey) or say no more of it than the summary does. A line that pads a name in a list is worse than no line. Give the reason in one sentence.
10. A place: what happened there or to it, with the place as the unstated subject ("was destroyed by brimstone and fire from the LORD"; "was where Jacob dreamed of a ladder reaching to heaven"). A place is worth a line only where something of the story happened there or to it. A land, a great city, a sea or a river that the story passes through again and again gets a line only for the few events that are most its own; never a summary of its history.
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


def context_keys(r):
    kjv = verses()
    seeds = set(r['mentioned'])
    seeds |= {key for *_, key in r['relations']}
    seeds |= {key for *_, key in r['events'] if key}
    seeds |= {key for _, key in r['titles']}
    for _, b, c1, v1, c2, v2 in r['passages']:
        run = expand(f'{CODES[b - 1]} {c1}:{v1}-{c2}:{v2}') or []
        seeds |= set(run[:80])
    seeds = {k for k in seeds if k in kjv}
    for width in (2, 1, 0):
        keys = set()
        for b, c, v in seeds:
            for d in range(-width, width + 1):
                if (b, c, v + d) in kjv:
                    keys.add((b, c, v + d))
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
    body = [describe(r), '', f'The verses (King James Version; • names the record; others are the {width} verses '
                             'around each):']
    prev = None
    for key in keys:
        if prev and (key[0], key[1]) == (prev[0], prev[1]) and key[2] != prev[2] + 1:
            body.append('…')
        elif prev and (key[0], key[1]) != (prev[0], prev[1]):
            body.append('')
        mark = '•' if key in r['mentioned'] else ' '
        body.append(f'{mark} {addr(key)} {kjv[key]}')
        prev = key
    return '\n'.join(body), keys


def draft_params(r):
    prompt, _ = draft_prompt(r)
    return {'model': MODEL, 'max_tokens': 32000,
            'system': [{'type': 'text', 'text': DRAFT_SYSTEM, 'cache_control': {'type': 'ephemeral'}}],
            'messages': [{'role': 'user', 'content': prompt}],
            'output_config': {'effort': 'high', 'format': {'type': 'json_schema', 'schema': DRAFT_SCHEMA}}}


# ---------------------------------------------------------------- the second reading

CHECK_SYSTEM = """You are the second, independent reader of the «known for» line of a record in the encyclopedia of a Bible reader. Another reader wrote the line from the record's verses in the King James Version: a few short clauses of what the person did or what happened to them (or, for a place or an object, what happened there or to it), each with the verses it rests on. The record's summary — kin, offices, origin — is shown elsewhere on the page and is not repeated in the line.

Read every clause against the verses it cites, which you are given (with a few verses of context around them), and judge it:
- right: the cited verses state everything the clause says, the record is the one who did it or to whom it happened, and nothing is added;
- fix: it can be put right from the verses shown — a wrong actor, a detail the verses do not give, a word they do not support, a citation that leaves out a verse the clause needs or takes in far more than it needs, a minor detail that does not belong in a chief deed. Give the corrected English and references;
- drop: the verses shown cannot support it, it concerns someone or somewhere else of the same name, or it only restates a kinship, an office or an origin.
why: one short sentence saying what you found (for right, an empty string).

The English keeps the line's form: past tense, the record as the unstated subject (the clause begins with its verb), plain modern English, names as the King James spells them, God as the verses call him, at most about 100 characters. References are runs "BOOK C:V", "BOOK C:V-V" or "BOOK C:V-C:V" of verses shown to you.

Then render each clause you keep (right or fix) into Ukrainian, German and Spanish, beside the same verses in the Ohienko Bible (Ukrainian), the Luther Bible 1912 (German) and the Reina-Valera 1909 (Spanish):
- the same content as the English, nothing added or left out, natural in the language, the record as the unstated subject;
- names of people and places as that translation of the verses spells them, declined as the sentence needs; the record's own name, where it is needed, as given;
- the LORD is «Господь» (declined), «der HERR» (declined: des HERRN, dem HERRN), «el SEÑOR»;
- the verb agrees with the record: feminine for a woman (Ukrainian «дала», «народила»; Spanish «fue salvada»); for a place or an object, with the gender of its name in that language;
- Ukrainian in the modern standard orthography, with the apostrophe written ' (зв'язав).
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

TRANSLATIONS = (('UBIO', 'Ohienko (Ukrainian)'), ('LUTH1912', 'Luther 1912 (German)'), ('RV1909', 'Reina-Valera 1909 (Spanish)'))


def check_shown(clauses):
    kjv = verses()
    cited = set()
    for clause in clauses:
        for ref in clause['references']:
            cited |= set(expand(ref) or [])
    shown = set()
    for b, c, v in cited:
        for d in range(-2, 3):
            if (b, c, v + d) in kjv:
                shown.add((b, c, v + d))
    return sorted(cited), sorted(shown)


def check_prompt(r, draft):
    kjv = verses()
    cited, shown = check_shown(draft['clauses'])
    forms = r['names']
    lines = [describe(r)]
    lines.append('Its name: ' + '; '.join(f'{lang} {forms[lang]}' for lang in ('ukr', 'deu', 'spa') if lang in forms))
    lines += ['', 'The line:']
    for n, clause in enumerate(draft['clauses'], 1):
        lines.append(f'{n}. {clause["english"]} ({", ".join(clause["references"])})')
    lines += ['', 'King James Version (* cited by a clause; the rest is context):']
    for key in shown:
        lines.append(f'{"*" if key in cited else " "} {addr(key)} {kjv[key]}')
    near = sorted({(b, c, v + d) for b, c, v in cited for d in (-1, 0, 1)} & set(shown))
    for slug, title in TRANSLATIONS:
        text = verses(slug)
        lines += ['', f'{title}, the same verses by canonical address:']
        lines += [f'{addr(key)} {text[key]}' for key in near if key in text]
    return '\n'.join(lines)


def check_params(r, draft):
    return {'model': MODEL, 'max_tokens': 32000,
            'system': [{'type': 'text', 'text': CHECK_SYSTEM, 'cache_control': {'type': 'ephemeral'}}],
            'messages': [{'role': 'user', 'content': check_prompt(r, draft)}],
            'output_config': {'effort': 'high', 'format': {'type': 'json_schema', 'schema': CHECK_SCHEMA}}}


# ---------------------------------------------------------------- validation

def norm(text):
    return re.sub(r'[^\w]+', ' ', text.lower()).strip()


def problems_of_draft(r, draft, shown):
    found = []
    if draft['decision'] == 'none':
        if draft['clauses']:
            found.append('declined yet gave clauses')
        return found
    if not 1 <= len(draft['clauses']) <= 5:
        found.append(f'{len(draft["clauses"])} clauses')
    shown = set(shown)
    for n, clause in enumerate(draft['clauses'], 1):
        found += problems_of_clause(n, clause['english'], clause['references'], shown)
        cited = [k for ref in clause['references'] for k in (expand(ref) or [])]
        text = norm(' '.join(verses()[k] for k in cited))
        if norm(clause['quote']) not in text:
            found.append(f'clause {n}: its quote is not in the verses it cites')
    return found


LEAKS = re.compile(r'\b(the text|the verses?|this record|the record|dataset|source|scripture says|the bible says)\b', re.I)


def problems_of_clause(n, english, references, shown):
    found = []
    if not english.strip():
        found.append(f'clause {n}: empty')
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
            found.append(f'clause {n}: {ref} cites verses the reader was not shown')
    return found


def problems_of_check(r, draft, check, shown):
    found = []
    if len(check['clauses']) != len(draft['clauses']):
        return [f'{len(check["clauses"])} verdicts for {len(draft["clauses"])} clauses']
    shown = set(shown)
    for n, clause in enumerate(check['clauses'], 1):
        if clause['verdict'] == 'drop':
            continue
        found += problems_of_clause(n, clause['english'], clause['references'], shown)
        for lang in ('ukr', 'deu', 'spa'):
            if not clause[lang].strip():
                found.append(f'clause {n}: no {lang}')
        if re.search(r'[a-zA-Z]{3,}', clause['ukr']):
            found.append(f'clause {n}: Latin letters in the Ukrainian')
    if check['clauses'] and all(c['verdict'] == 'drop' for c in check['clauses']):
        found.append('every clause dropped')
    return found


# ---------------------------------------------------------------- running

def save(kind, slug, payload):
    (RAW / kind).mkdir(parents=True, exist_ok=True)
    json.dump(payload, open(RAW / kind / f'{slug}.json', 'w', encoding='utf-8', newline='\n'), ensure_ascii=False,
              indent=1)


def load(kind, slug):
    path = RAW / kind / f'{slug}.json'
    return json.load(open(path, encoding='utf-8')) if path.is_file() else None


def guard():
    spent = claude_api.spent(LABEL)
    if spent > BUDGET_USD:
        raise SystemExit(f'stopped: the ledger shows ${spent:.2f} spent under {LABEL}, over ${BUDGET_USD}')
    return spent


def ask_json(params, label):
    with claude_api.client().messages.stream(**params) as stream:
        message = stream.get_final_message()
    claude_api.record(label, message.model, message.usage, request_id=message.id)
    if message.model != MODEL:
        raise RuntimeError(f'answered by {message.model}, not {MODEL}')
    if message.stop_reason != 'end_turn':
        raise RuntimeError(f'stopped: {message.stop_reason}')
    return json.loads(claude_api.text_of(message)), message.usage


def by_slug():
    return {r['slug']: r for r in records().values()}


def record_draft(r, answer, how):
    _, keys = draft_prompt(r)
    found = problems_of_draft(r, answer, [tuple(k) for k in keys])
    save('draft', r['slug'], {'slug': r['slug'], 'model': MODEL, 'prompt': PROMPT_VERSION, 'how': how,
                              'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'answer': answer,
                              'problems': found})
    return found


def record_check(r, draft, answer, how):
    _, shown = check_shown(draft['clauses'])
    found = problems_of_check(r, draft, answer, shown)
    save('check', r['slug'], {'slug': r['slug'], 'model': MODEL, 'prompt': PROMPT_VERSION, 'how': how,
                              'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'answer': answer,
                              'problems': found})
    return found


def cmd_draft_ask(slugs):
    rs = by_slug()
    for slug in slugs:
        guard()
        answer, usage = ask_json(draft_params(rs[slug]), LABEL)
        found = record_draft(rs[slug], answer, 'ask')
        print(slug, answer['decision'], len(answer['clauses']), found, usage.input_tokens, usage.output_tokens)


def cmd_check_ask(slugs):
    rs = by_slug()
    for slug in slugs:
        guard()
        draft = load('draft', slug)
        if not draft or draft['answer']['decision'] != 'write' or draft['problems']:
            print(slug, 'no sound draft to check')
            continue
        answer, usage = ask_json(check_params(rs[slug], draft['answer']), LABEL)
        found = record_check(rs[slug], draft['answer'], answer, 'ask')
        print(slug, [c['verdict'] for c in answer['clauses']], found, usage.input_tokens, usage.output_tokens)


def cmd_draft_batch(limit=None):
    guard()
    rs = by_slug()
    wanted = [c['slug'] for c in json.load(open(HERE / 'candidates.json', encoding='utf-8'))['records']]
    todo = [s for s in wanted if load('draft', s) is None][:limit]
    if not todo:
        print('nothing to draft')
        return
    batch = claude_api.batch_submit([(s, draft_params(rs[s])) for s in todo], label=LABEL)
    log_batch(batch, 'draft', todo)


def cmd_check_batch():
    guard()
    rs = by_slug()
    todo = []
    for path in sorted((RAW / 'draft').glob('*.json')):
        draft = json.load(open(path, encoding='utf-8'))
        if draft['answer']['decision'] == 'write' and not draft['problems'] and load('check', draft['slug']) is None:
            todo.append((draft['slug'], check_params(rs[draft['slug']], draft['answer'])))
    if not todo:
        print('nothing to check')
        return
    batch = claude_api.batch_submit(todo, label=LABEL)
    log_batch(batch, 'check', [s for s, _ in todo])


def log_batch(batch, kind, slugs):
    with open(HERE / 'batches.jsonl', 'a', encoding='utf-8', newline='\n') as log:
        log.write(json.dumps({'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'batch': batch, 'kind': kind,
                              'requests': len(slugs)}) + '\n')
    print(kind, batch, len(slugs))


def cmd_collect(batch, kind):
    claude_api.batch_wait(batch, poll=60)
    rs = by_slug()
    failures = {}
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
        if kind == 'draft':
            record_draft(rs[slug], answer, f'batch {batch}')
        else:
            record_check(rs[slug], load('draft', slug)['answer'], answer, f'batch {batch}')
    json.dump(failures, open(HERE / f'failures-{kind}-{batch}.json', 'w', encoding='utf-8', newline='\n'), indent=1)
    print(kind, 'collected; failures', len(failures), 'spent', claude_api.spent(LABEL))


def cmd_spent():
    rows = [json.loads(line) for line in open(claude_api.LEDGER, encoding='utf-8') if line.strip()]
    mine = [row for row in rows if row['label'] == LABEL]
    print(f'{len(mine)} calls, {sum(r["input"] for r in mine)} in, {sum(r["output"] for r in mine)} out, '
          f'{sum(r["cache_read"] for r in mine)} cache read, ${sum(r["usd"] or 0 for r in mine):.4f}')


if __name__ == '__main__':
    verb, *rest = sys.argv[1:] or ['help']
    if verb == 'candidates':
        cmd_candidates()
    elif verb == 'draft-ask':
        cmd_draft_ask(rest)
    elif verb == 'check-ask':
        cmd_check_ask(rest)
    elif verb == 'draft-batch':
        cmd_draft_batch(int(rest[1]) if rest[:1] == ['--limit'] else None)
    elif verb == 'check-batch':
        cmd_check_batch()
    elif verb == 'collect':
        cmd_collect(rest[0], rest[1])
    elif verb == 'spent':
        cmd_spent()
    else:
        print(__doc__)
