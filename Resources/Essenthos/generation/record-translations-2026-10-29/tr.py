"""
The English our records carry from their sources, rendered into Ukrainian, German and Spanish through the
project's Claude API client (scripts/claude_api.py) on the owner's key, claude-sonnet-5-5 only, as a batch:
event titles, descriptions and notes, the arithmetic and notes behind each reckoning's date, an event's
place and region, the modern site of a place, the notes of records no translation renders yet, and the notes
on titles borne.

Every rendering is machine-made and is stored and served as such. A quotation of a verse is given the words
of the reader's Bible (Ohienko, Luther 1912, Reina-Valera 1909), which the request carries beside it; a name
the corpus already spells in that language is given that spelling; every Bible reference stays a book code,
for the reader to render and link. «G-d» stays elided as the source writes it: Б-г, G-tt, D-os.

    python tr.py items                     # export/ -> items.json (what is to be rendered, with its context)
    python tr.py batch <ukr|deu|spa> [--limit N] [--redo]
    python tr.py collect <batch-id>
    python tr.py flag <language> <ids-file> <reason>   # answers read and found wrong, for the next --redo
    python tr.py assemble                  # answers -> Resources/Essenthos/translations/record-texts.json
    python tr.py spent

The export (export.sh) is read once from the corpus into export/ and is not kept in git.
"""
import collections
import csv
import datetime as dt
import hashlib
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPORT = Path(os.environ.get('TR_EXPORT', HERE / 'export'))
MAIN = Path(os.environ.get('TR_MAIN', r'C:\Users\timon\Projects\Essenthos\essenthos-core'))
sys.path.insert(0, str(MAIN / 'scripts'))
import claude_api  # noqa: E402

MODEL = 'claude-sonnet-5-5'
EFFORT = 'low'
LABEL = 'record-translations-PRB-1035'
CAP_USD = 20.0
PROMPT_VERSION = 'v2'
RAW = HERE / 'raw'
OUT = HERE.parents[1] / 'translations' / 'record-texts.json'
LANGS = {'ukr': ('Ukrainian', 'UBIO', 'the Ohienko Bible'),
         'deu': ('German', 'LUTH1912', 'the Luther Bible of 1912'),
         'spa': ('Spanish', 'RV1909', 'the Reina-Valera Bible of 1909')}
ELIDED = {'ukr': 'Б-г', 'deu': 'G-tt', 'spa': 'D-os'}
MOST_CHARS = 4000
MOST_ITEMS = 60
QUOTING = ('event-name', 'event-description', 'title-note', 'record-notes')
COUNTED = ('date-arithmetic', 'date-notes', 'event-notes', 'modern-site', 'event-region')

CODES = ["GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR",
         "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO",
         "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM",
         "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE",
         "2PE", "1JN", "2JN", "3JN", "JUD", "REV"]
ALIASES = {"EZE": "EZK", "JOE": "JOL", "NAH": "NAM", "ACTS": "ACT", "PHI": "PHP", "SOS": "SNG"}
ENGLISH = ["Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "1 Samuel",
           "2 Samuel", "1 Kings", "2 Kings", "1 Chronicles", "2 Chronicles", "Ezra", "Nehemiah", "Esther", "Job",
           "Psalms?", "Proverbs", "Ecclesiastes", "Song of Solomon", "Isaiah", "Jeremiah", "Lamentations",
           "Ezekiel", "Daniel", "Hosea", "Joel", "Amos", "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk",
           "Zephaniah", "Haggai", "Zechariah", "Malachi", "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
           "1 Corinthians", "2 Corinthians", "Galatians", "Ephesians", "Philippians", "Colossians",
           "1 Thessalonians", "2 Thessalonians", "1 Timothy", "2 Timothy", "Titus", "Philemon", "Hebrews", "James",
           "1 Peter", "2 Peter", "1 John", "2 John", "3 John", "Jude", "Revelation"]
SHORT = {"Gen": "GEN", "Exod": "EXO", "Ex": "EXO", "Lev": "LEV", "Num": "NUM", "Deut": "DEU", "Josh": "JOS",
         "Judg": "JDG", "1 Sam": "1SA", "2 Sam": "2SA", "1 Ki": "1KI", "2 Ki": "2KI", "1 Chr": "1CH",
         "2 Chr": "2CH", "Neh": "NEH", "Mal": "MAL", "Matt": "MAT", "Ki": None}
CODE_OF = {**{c: c for c in CODES}, **ALIASES}
NAME_OF = {}
for code, name in zip(CODES, ENGLISH):
    NAME_OF[name] = code
NAME_OF.update({k: v for k, v in SHORT.items() if v})
ORDINAL = {code: i + 1 for i, code in enumerate(CODES)}

_codes = '|'.join(sorted(CODE_OF, key=len, reverse=True))
_names = '|'.join(sorted(NAME_OF, key=len, reverse=True))
CODE_REF = re.compile(rf"(?<![\w-])({_codes}) (\d{{1,3}}):(\d{{1,3}})(?:[-–] ?(?:(\d{{1,3}}):)?(\d{{1,3}}))?")
NAME_REF = re.compile(rf"(?<![\w-])({_names}) (\d{{1,3}}):(\d{{1,3}})(?:[-–] ?(?:(\d{{1,3}}):)?(\d{{1,3}}))?")
CHAPTER_REF = re.compile(rf"(?<![\w-])({_codes}|{_names}) \d{{1,3}}(?![\d:])")
# A bare "22:51" after a cited book, inside the same run of references ("2SA 19:21; 22:51").
BARE_AFTER = re.compile(r"^[;,]\s*(\d{1,3}):(\d{1,3})(?:[-–](\d{1,3}))?")

SCHEMA = {
    'type': 'object',
    'properties': {'items': {'type': 'array', 'items': {
        'type': 'object',
        'properties': {'id': {'type': 'string'}, 'text': {'type': 'string'}},
        'required': ['id', 'text'], 'additionalProperties': False}}},
    'required': ['items'], 'additionalProperties': False,
}

KINDS = {
    'event-name': 'the title of an event on a timeline of the Bible or of world history',
    'event-description': 'what an event is, as the dataset describes it; scripture events mostly quote a verse',
    'event-notes': "the dataset author's notes on an event and its date",
    'event-location': 'where an event took place, as the dataset names it',
    'event-region': 'the country or region of an event (a modern or a historical state)',
    'date-arithmetic': 'how one chronologer reaches the year of an event, as arithmetic in words',
    'date-notes': "the dataset author's notes on one chronologer's date of an event",
    'modern-site': 'where a biblical place is today, as a gazetteer gives it (a modern site, a tell, a town)',
    'record-notes': 'a note on a person or place in an encyclopedia of the Bible',
    'title-note': 'what a verse says when it gives a person a title, with its references',
}


def digest(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


# ---------------------------------------------------------------- references

def refs(text):
    """Every reference a text makes, as (code, chapter, verse, last) in order."""
    found = []
    spans = []
    for pattern, mapping in ((CODE_REF, CODE_OF), (NAME_REF, NAME_OF)):
        for m in pattern.finditer(text):
            if any(a <= m.start() < b for a, b in spans):
                continue
            code = mapping.get(m.group(1))
            if not code:
                continue
            chapter, verse = int(m.group(2)), int(m.group(3))
            last = int(m.group(5)) if m.group(5) and not m.group(4) else verse
            found.append((m.start(), code, chapter, verse, last))
            spans.append((m.start(), m.end()))
            rest = m.end()
            while True:
                bare = BARE_AFTER.match(text[rest:])
                if not bare:
                    break
                c, v = int(bare.group(1)), int(bare.group(2))
                found.append((rest, code, c, v, int(bare.group(3)) if bare.group(3) else v))
                spans.append((rest, rest + bare.end()))
                rest += bare.end()
    return [f[1:] for f in sorted(found)]


def code_refs(text):
    """The references a rendering makes: book codes only."""
    out = []
    for m in CODE_REF.finditer(text):
        code = CODE_OF[m.group(1)]
        out.append((code, int(m.group(2)), int(m.group(3))))
        rest = m.end()
        while True:
            bare = BARE_AFTER.match(text[rest:])
            if not bare:
                break
            out.append((code, int(bare.group(1)), int(bare.group(2))))
            rest += bare.end()
    return out


# ---------------------------------------------------------------- the export

_verses = {}


def verses(slug):
    if slug not in _verses:
        out = {}
        for b, c, v, text in csv.reader(open(EXPORT / f'verses_{slug}.csv', encoding='utf-8')):
            key = (int(b), int(c), int(v))
            if key[0] <= 66:
                out[key] = (out[key] + ' ' + text.strip()) if key in out else text.strip()
        _verses[slug] = out
    return _verses[slug]


def read_items():
    """Every English string to render, once, with what it is and the record it was first met on."""
    items = {}

    def add(text, kind, record=None, names=()):
        if text is None or not text.strip():
            return
        key = digest(text)
        if key not in items:
            items[key] = {'id': key[:16], 'sha256': key, 'english': text, 'kind': kind, 'record': record,
                          'names': list(names)}
        else:
            items[key]['names'] = sorted(set(items[key]['names']) | set(names))

    events = {}
    for r in csv.reader(open(EXPORT / 'events.csv', encoding='utf-8')):
        (eid, slug, realm, kind, name, desc, notes, location, region, at_time,
         person, person_source, *_) = r
        events[eid] = (slug, person, person_source)
        names = [n for n in (person_source,) if n]
        add(name, 'event-name', slug, names)
        add(desc, 'event-description', slug, names)
        add(notes, 'event-notes', slug, names)
        add(location, 'event-location', slug)
        add(region, 'event-region', slug)
        add(at_time, 'event-region', slug)
    for eid, chronology, calculation, notes in csv.reader(open(EXPORT / 'event_dates.csv', encoding='utf-8')):
        slug, person, person_source = events.get(eid, (None, None, None))
        names = [n for n in (person_source,) if n]
        # The Septuagint reckonings' arithmetic is this corpus's own sentence, said from a template; it
        # is not the source's English and is not sent to be translated.
        if chronology == 'bibledata':
            add(calculation, 'date-arithmetic', slug, names)
        add(notes, 'date-notes', slug, names)
    for _, slug, kind, name, notes, modern, source_id, translated in csv.reader(
            open(EXPORT / 'records.csv', encoding='utf-8')):
        if notes and translated == '0':
            add(notes, 'record-notes', slug, [source_id])
        add(modern, 'modern-site', slug)
    for slug, site in csv.reader(open(EXPORT / 'candidate_sites.csv', encoding='utf-8')):
        add(site, 'modern-site', slug)
    for title, bearer, note in csv.reader(open(EXPORT / 'title_notes.csv', encoding='utf-8')):
        add(note, 'title-note', f'{title}/{bearer}')
    return items


# Two Spanish nominatives the corpus holds are not the Reina-Valera's names for these records (PRB-1343):
# Joshua son of Nun is held as «Jesuá» (Ezra's Jeshua) and Abram as «Abraham». A request carries the
# Reina-Valera's spelling instead.
NAME_FIXES = {('joshua', 'spa'): 'Josué', ('abram', 'spa'): 'Abram'}


def glossary():
    """English name -> {language: {forms}} over every record, and source id -> {language: form}."""
    by_name = collections.defaultdict(lambda: collections.defaultdict(set))
    by_source = collections.defaultdict(dict)
    for slug, kind, name, source_id, language, form in csv.reader(open(EXPORT / 'name_forms.csv', encoding='utf-8')):
        if kind not in ('person', 'place', 'people'):
            continue
        form = NAME_FIXES.get((slug, language), form)
        by_name[name][language].add(form)
        by_source[source_id][language] = form
        by_source[f'slug:{slug}'][language] = form
    return by_name, by_source


TOKEN = re.compile(r"\b([A-Z][a-z]+(?:-[a-z]+)*)_(\d+)\b")
CAPITALISED = re.compile(r"\b[A-Z][a-z'’-]+(?: (?:of |the )?[A-Z][a-z'’-]+)*")


def names_in(item, language, by_name, by_source):
    """The spellings the corpus gives the names this text mentions, where it gives one."""
    out = {}
    for source in item['names']:
        if source in by_source and language in by_source[source]:
            out[source.split(':', 1)[-1].rsplit('_', 1)[0]] = by_source[source][language]
    for m in TOKEN.finditer(item['english']):
        source = f'person:{m.group(1)}_{m.group(2)}'
        if source in by_source and language in by_source[source]:
            out[m.group(0)] = by_source[source][language]
    for m in CAPITALISED.finditer(item['english']):
        for phrase in {m.group(0), *m.group(0).split(' ')}:
            forms = by_name.get(phrase, {}).get(language)
            if forms and len(forms) == 1 and phrase not in out:
                out[phrase] = next(iter(forms))
    return out


# ---------------------------------------------------------------- requests

SAID = {'ukr': '"[дата за Ашшером, с. 58]", "[дата за Шульманом, с. 83]"',
        'deu': '"[Datierung nach Ussher, S. 58]", "[Datierung nach Shulman, S. 83]"',
        'spa': '"[fecha según Ussher, p. 58]", "[fecha según Shulman, p. 83]"'}
CHRONOLOGERS = {'ukr': 'Ussher is Ашшер, Shulman is Шульман', 'deu': 'Ussher and Shulman stay as they are',
                'spa': 'Ussher and Shulman stay as they are'}


def system_prompt(language):
    tongue, _, bible = LANGS[language]
    elided = ELIDED[language]
    return f"""You translate the English that an encyclopedia of the Bible carries from its sources into {tongue}, for a reader of the {tongue} edition of the site. Each item says what it is. Render it faithfully and completely: say what the English says, no more and no less, in natural modern {tongue}. Do not explain, summarise, correct or comment, and do not add anything the English does not say.

Rules:
1. Quoted scripture. Where the English quotes a verse, use the words of {bible} for it, which each item gives you under "verses" where it cites one. Take the corresponding words of that verse (the whole verse where the English quotes it whole, the part where it quotes a part); do not translate the English quotation afresh. Where the English writes words in CAPITALS for emphasis (usually numbers and ages), write the corresponding words of the verse in capitals too.
2. References. Write every Bible reference as a book code with chapter and verse, exactly in this form: GEN 11:26, 1SA 17:32-33, EXO 12:40-41. Where the English writes a book name ("Genesis 5:3", "1 Kings 6:1"), write its code instead (GEN 5:3, 1KI 6:1); "references" lists the code of every reference the item makes. Keep a reference where the English has it, with its brackets and punctuation. Never write a book name or a book name's abbreviation.
3. Names. Where "names" gives a spelling for a name, use it (declined as the sentence needs). An identifier such as Seth_1 is a person: write his name as "names" gives it, without the number. Other names: as {bible} spells them; places and things outside the Bible as {tongue} usually writes them.
4. «G-d». The sources write the divine name elided, as G-d. Keep the elision in {tongue}: write {elided} (declined as the sentence needs), never the full word. Where the English writes God or LORD in full, translate it as usual.
5. Keep the form: line breaks where the English has them; an asterisk where the English has one (it marks the source's hypothesis); brackets as they are. A note in square brackets such as [Ussher date p58] or [Shulman date p83] names a chronologer's date and the page of his work: always translate it, as {SAID[language]}.
   The chronologers are James Ussher and Eliezer Shulman: {CHRONOLOGERS[language]}. The names of the datasets, BibleData and Essenthos, stay as they are, and so does the title of a published work. An identifier of an event such as The_Exodus is that event: name it in words ("the Exodus"), without the identifier.
6. Numbers stay in digits where the English has digits. Do not reckon or change a number.
7. Words and phrases of a dataset that are not prose (a single word such as "uncertain", "in Jerusalem", "not a place (person)", "within 5 km of Bethlehem 1") are translated as such, short, keeping their numbers.
8. A modern site name (Tell el Daba, Khirbet Haiyan, Jebel Musa, Qantir) is written as {tongue} usually writes Arabic and Hebrew place names, in its own script; translate the descriptive words around it ("or possibly", "near").
9. A world-history event takes its usual {tongue} name where it has one (as an encyclopedia in {tongue} names it); otherwise translate it.

Answer with every item, by its id, and nothing else."""


def item_payload(item, language, by_name, by_source, local_id=None):
    _, slug, _ = LANGS[language]
    bible = verses(slug)
    cited = refs(item['english'])
    shown = {}
    for code, chapter, verse, last in (cited if item['kind'] in QUOTING else []):
        book = CODES.index(code) + 1 if code in CODES else None
        if book is None:
            continue
        for v in range(verse, min(last, verse + 5) + 1):
            text = bible.get((book, chapter, v))
            if text and len(shown) < 10:
                shown[f'{code} {chapter}:{v}'] = text
    payload = {'id': local_id, 'what': KINDS[item['kind']], 'english': item['english']}
    if cited:
        payload['references'] = sorted({f'{c} {ch}:{v}' + (f'-{last}' if last != v else '')
                                        for c, ch, v, last in cited})
    if shown:
        payload['verses'] = shown
    spelled = names_in(item, language, by_name, by_source)
    if spelled:
        payload['names'] = spelled
    return payload


def chunks(items):
    """Requests of a few thousand characters, items of one kind together, in a stable order."""
    order = sorted(items, key=lambda i: (list(KINDS).index(i['kind']), i['sha256']))
    out, current, size = [], [], 0
    for item in order:
        length = len(item['english'])
        if current and (size + length > MOST_CHARS or len(current) >= MOST_ITEMS
                        or current[0]['kind'] != item['kind']):
            out.append(current)
            current, size = [], 0
        current.append(item)
        size += length
    if current:
        out.append(current)
    return out


def max_tokens(chunk, language):
    english = sum(len(i['english']) for i in chunk)
    factor = {'ukr': 0.9, 'deu': 0.6, 'spa': 0.6}[language]
    return int(400 + english * factor + 15 * len(chunk))


def params(chunk, language, by_name, by_source, feedback=None):
    body = {'items': [item_payload(i, language, by_name, by_source, str(n + 1)) for n, i in enumerate(chunk)]}
    if feedback:
        local = {i['id']: str(n + 1) for n, i in enumerate(chunk)}
        body['your_last_answer_was_refused'] = {local[k]: v for k, v in feedback.items()}
    return claude_api.build_params(json.dumps(body, ensure_ascii=False), system=system_prompt(language),
                                   model=MODEL, effort=EFFORT, max_tokens=max_tokens(chunk, language),
                                   schema=SCHEMA, ttl='5m')


# ---------------------------------------------------------------- checking an answer

LATIN = re.compile(r"[A-Za-z]{2,}")
QUOTED = re.compile(r"«[^»]*»|“[^”]*”|\"[^\"]*\"")
ENGLISH_BOOK = re.compile(rf"(?<![\w-])(?:{_names}) \d")


def problems(item, text, language):
    english = item['english']
    why = []
    if not text.strip():
        return ['empty']
    want = sorted({(c, ch, v) for c, ch, v, _ in refs(english)})
    got = sorted(set(code_refs(text)))
    if not set(want) <= set(got) or not {c for c, _, _ in got} <= {c for c, _, _ in want}:
        why.append(f'references must be {[f"{c} {ch}:{v}" for c, ch, v in want]} as book codes, '
                   f'found {[f"{c} {ch}:{v}" for c, ch, v in got]}')
    if ENGLISH_BOOK.search(text):
        why.append('a book is named instead of written as its code')
    if english.rstrip().endswith('*') and not text.rstrip().endswith('*'):
        why.append('the asterisk at the end is missing')
    if 'G-d' in english:
        if 'G-d' in text or ELIDED[language].split('-')[0] + '-' not in text:
            why.append(f'«G-d» must be written {ELIDED[language]}')
    numbers = lambda s: sorted(re.sub(r'\D', '', n) for n in re.findall(
        r'\d[\d,.\u00a0\u202f ]*\d|\d', TOKEN.sub(' ', CHAPTER_REF.sub(' ', CODE_REF.sub(' ', NAME_REF.sub(' ', s))))))
    if item['kind'] in COUNTED and numbers(english) != numbers(text):
        why.append(f'the numbers differ from the English: {numbers(english)} against {numbers(text)}')
    if language == 'ukr':
        rest = QUOTED.sub(' ', CHAPTER_REF.sub(' ', CODE_REF.sub(' ', text)))
        latin = [w for w in LATIN.findall(rest) if w not in ('AM', 'BC', 'AD', 'BCE', 'CE', 'BibleData', 'Essenthos')]
        if latin:
            why.append(f'Latin letters in the Ukrainian: {latin[:6]}')
    if english.count('\n') and text.count('\n') < english.count('\n') // 2:
        why.append('the line breaks are lost')
    return why


# ---------------------------------------------------------------- the run

def load_items():
    return json.loads((HERE / 'items.json').read_text(encoding='utf-8'))


def answers(language):
    path = RAW / f'{language}.jsonl'
    out = {}
    if path.exists():
        for line in path.read_text(encoding='utf-8').splitlines():
            row = json.loads(line)
            out[row['id']] = row
    return out


def cmd_items():
    items = read_items()
    (HERE / 'items.json').write_text(json.dumps(sorted(items.values(), key=lambda i: i['sha256']),
                                                ensure_ascii=False, indent=1), encoding='utf-8')
    counts = collections.Counter(i['kind'] for i in items.values())
    chars = collections.Counter()
    for i in items.values():
        chars[i['kind']] += len(i['english'])
    for kind in KINDS:
        print(f'{kind:20} {counts[kind]:6} items {chars[kind]:8} chars')
    print(f'{"all":20} {len(items):6} items {sum(chars.values()):8} chars; {len(chunks(list(items.values())))} requests per language')


def cmd_batch(language, limit=None, redo=False, only=None):
    items = load_items()
    have = answers(language)
    by_id = {i['id']: i for i in items}
    if redo:
        work = [by_id[k] for k, row in have.items() if row.get('problems')]
        feedback = {k: row['problems'] for k, row in have.items() if row.get('problems')}
    else:
        work = [i for i in items if i['id'] not in have]
        feedback = {}
    if only:
        work = [i for i in work if i['id'] in only]
    by_name, by_source = glossary()
    groups = chunks(work)
    if limit:
        groups = groups[:limit]
    requests = []
    for n, group in enumerate(groups):
        fb = {i['id']: feedback[i['id']] for i in group if i['id'] in feedback} or None
        requests.append((f'{language}-{n:04d}-{group[0]["id"]}', params(group, language, by_name, by_source, fb)))
    plan = claude_api.batch_plan(requests)
    print(f'{len(requests)} requests, {sum(len(g) for g in groups)} items, reserve ${plan["reserve_usd"]:.2f}, '
          f'spent so far ${claude_api.spent(LABEL):.2f} of ${CAP_USD:.2f}')
    batch_id = claude_api.batch_submit(requests, label=LABEL, cap_usd=CAP_USD)
    (HERE / 'batches.jsonl').open('a', encoding='utf-8').write(json.dumps({
        'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'batch': batch_id, 'language': language,
        'requests': {cid: [i['id'] for i in g] for (cid, _), g in zip(requests, groups)},
        'prompt': PROMPT_VERSION, 'model': MODEL, 'effort': EFFORT, 'redo': redo}, ensure_ascii=False) + '\n')
    print(batch_id)


def cmd_collect(batch_id):
    record = None
    for line in (HERE / 'batches.jsonl').read_text(encoding='utf-8').splitlines():
        row = json.loads(line)
        if row['batch'] == batch_id:
            record = row
    if record is None:
        raise SystemExit(f'{batch_id} is not in batches.jsonl')
    language = record['language']
    batch = claude_api.client().messages.batches.retrieve(batch_id)
    if batch.processing_status != 'ended':
        raise SystemExit(f'{batch_id}: {batch.processing_status} {batch.request_counts}')
    by_id = {i['id']: i for i in load_items()}
    RAW.mkdir(exist_ok=True)
    have = answers(language)
    stats = collections.Counter()
    for result in claude_api.client().messages.batches.results(batch_id):
        cid = result.custom_id
        if result.result.type != 'succeeded':
            stats['failed'] += 1
            continue
        message = result.result.message
        claude_api.record(LABEL, message.model, message.usage, batch=True, request_id=cid, message_id=message.id,
                          batch_id=batch_id, stop_reason=message.stop_reason)
        if message.model != MODEL:
            stats['wrong-model'] += 1
            continue
        if message.stop_reason != 'end_turn':
            stats[message.stop_reason] += 1
            continue
        try:
            got = {row['id']: row['text'] for row in json.loads(claude_api.text_of(message))['items']}
            ids = record['requests'][cid]
            if record.get('prompt', 'v1') != 'v1':
                got = {ids[int(k) - 1]: v for k, v in got.items() if k.isdigit() and 0 < int(k) <= len(ids)}
        except (ValueError, KeyError):
            stats['unreadable'] += 1
            continue
        for item_id in record['requests'][cid]:
            item = by_id[item_id]
            text = got.get(item_id)
            if text is None:
                stats['omitted'] += 1
                continue
            why = problems(item, text, language)
            have[item_id] = {'id': item_id, 'sha256': item['sha256'], 'text': text, 'problems': why,
                             'batch': batch_id, 'prompt': PROMPT_VERSION}
            stats['refused' if why else 'kept'] += 1
    with (RAW / f'{language}.jsonl').open('w', encoding='utf-8') as out:
        for row in sorted(have.values(), key=lambda r: r['id']):
            out.write(json.dumps(row, ensure_ascii=False) + '\n')
    print(dict(stats))
    print(claude_api.format_summary(claude_api.summary(LABEL), LABEL))


def cmd_revalidate():
    by_id = {i['id']: i for i in load_items()}
    for language in LANGS:
        have = answers(language)
        bad = 0
        for row in have.values():
            row['problems'] = problems(by_id[row['id']], row['text'], language)
            bad += bool(row['problems'])
        with (RAW / f'{language}.jsonl').open('w', encoding='utf-8') as out:
            for row in sorted(have.values(), key=lambda r: r['id']):
                out.write(json.dumps(row, ensure_ascii=False) + '\n')
        print(language, len(have), 'answers,', bad, 'refused')


def cmd_flag(language, ids_file, reason):
    """Mark answers read and found wrong by a person, so the next --redo sends them again with the reason."""
    ids = {line.strip() for line in Path(ids_file).read_text(encoding='utf-8').splitlines() if line.strip()}
    have = answers(language)
    for item_id in ids:
        have[item_id]['problems'] = [reason]
    with (RAW / f'{language}.jsonl').open('w', encoding='utf-8') as out:
        for row in sorted(have.values(), key=lambda r: r['id']):
            out.write(json.dumps(row, ensure_ascii=False) + '\n')
    print(f'{len(ids)} {language} answers flagged')


def cmd_assemble():
    items = load_items()
    rendered = {language: answers(language) for language in LANGS}
    entries, missing = [], collections.Counter()
    for item in items:
        entry = {'english': item['english']}
        for language in LANGS:
            row = rendered[language].get(item['id'])
            if row and not row['problems']:
                entry[language] = row['text']
            else:
                missing[language] += 1
        if len(entry) > 1:
            entries.append(entry)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({
        'source': f'Essenthos: machine translation by {MODEL} (effort {EFFORT}, prompt {PROMPT_VERSION}), '
                  f'2026-10-29, through the Claude API; quotations in the words of the Ohienko, Luther 1912 and '
                  f'Reina-Valera 1909 Bibles',
        'entries': sorted(entries, key=lambda e: digest(e['english'])),
    }, ensure_ascii=False, indent=1) + '\n', encoding='utf-8', newline='\n')
    print(f'{len(entries)} entries written to {OUT}; not rendered: {dict(missing)}')


def main(argv):
    if not argv:
        print(__doc__)
        return
    verb, rest = argv[0], argv[1:]
    if verb == 'items':
        cmd_items()
    elif verb == 'batch':
        limit = int(rest[rest.index('--limit') + 1]) if '--limit' in rest else None
        only = set(rest[rest.index('--only') + 1].split(',')) if '--only' in rest else None
        cmd_batch(rest[0], limit=limit, redo='--redo' in rest, only=only)
    elif verb == 'collect':
        cmd_collect(rest[0])
    elif verb == 'revalidate':
        cmd_revalidate()
    elif verb == 'flag':
        cmd_flag(rest[0], rest[1], rest[2])
    elif verb == 'assemble':
        cmd_assemble()
    elif verb == 'spent':
        print(claude_api.format_summary(claude_api.summary(LABEL), LABEL))
    else:
        raise SystemExit(__doc__)


if __name__ == '__main__':
    main(sys.argv[1:])
