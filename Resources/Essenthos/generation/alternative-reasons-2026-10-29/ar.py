"""
The reasons of the records' alternatives («Ким це ще може бути» / "Who else this might be"), and the
reasons said back on the other record (RUL-0201), rendered into Ukrainian, German and Spanish through
the project's Claude API client (scripts/claude_api.py) on the owner's key, claude-sonnet-5-5 only, as
a batch. PRB-1347.

Every rendering is a machine translation and is stored and served as one. A quotation of a verse is
given the words of the reader's Bible (Ohienko, Luther 1912, Reina-Valera 1909), which the request
carries; a name the corpus already spells in that language is given that spelling; a Bible reference
is written with the book's name in the reader's language, so the reader's page can link it.

    python ar.py items        # the rulings files -> items.json (every distinct English reason, where it is said)
    python ar.py export       # the cited verses (live API, GET only) and name spellings (one bounded read) -> export/
    python ar.py plan         # what the batch would send and cost at most, sending nothing
    python ar.py batch        # one batch: every item in ukr, deu and spa
    python ar.py collect <batch-id>
    python ar.py check        # the answers against the English, mechanically
    python ar.py assemble     # answers -> Essenthos.Forge/Loading/Encyclopedia/AlternativeReasonTranslations.json
    python ar.py spent

The export is read once and is not kept in git: the verse texts are the Bibles' own.
"""
import collections
import glob
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
EXPORT = HERE / 'export'
RAW = HERE / 'raw'
RULINGS = REPO / 'Essenthos.Forge' / 'Loading' / 'Encyclopedia'
OUT = RULINGS / 'AlternativeReasonTranslations.json'
MAIN = Path(os.environ.get('AR_MAIN', r'C:\Users\timon\Projects\Essenthos\essenthos-core'))
API = os.environ.get('AR_API', 'http://localhost:5279')
sys.path.insert(0, str(MAIN / 'scripts'))
import claude_api  # noqa: E402

MODEL = 'claude-sonnet-5-5'
EFFORT = 'medium'
LABEL = 'alternative-reasons-PRB-1347'
CAP_USD = 3.0
PROMPT_VERSION = 'v1'
MOST_ITEMS = 12
LANGS = {'ukr': ('Ukrainian', 'UBIO', 'the Ohienko Bible'),
         'deu': ('German', 'LUTH1912', 'the Luther Bible of 1912'),
         'spa': ('Spanish', 'RV1909', 'the Reina-Valera Bible of 1909')}
SOURCE = ('Essenthos: machine translation by claude-sonnet-5-5 (effort medium, prompt v1), 2026-10-29, '
          'through the Claude API; quotations in the words of the Ohienko, Luther 1912 and Reina-Valera 1909 Bibles; '
          'ten phrases corrected on reading (corrections.json)')

CODES = ["GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR",
         "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO",
         "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM",
         "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE",
         "2PE", "1JN", "2JN", "3JN", "JUD", "REV"]
ENGLISH = ["Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "1 Samuel",
           "2 Samuel", "1 Kings", "2 Kings", "1 Chronicles", "2 Chronicles", "Ezra", "Nehemiah", "Esther", "Job",
           "Psalm", "Proverbs", "Ecclesiastes", "Song of Solomon", "Isaiah", "Jeremiah", "Lamentations",
           "Ezekiel", "Daniel", "Hosea", "Joel", "Amos", "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk",
           "Zephaniah", "Haggai", "Zechariah", "Malachi", "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
           "1 Corinthians", "2 Corinthians", "Galatians", "Ephesians", "Philippians", "Colossians",
           "1 Thessalonians", "2 Thessalonians", "1 Timothy", "2 Timothy", "Titus", "Philemon", "Hebrews", "James",
           "1 Peter", "2 Peter", "1 John", "2 John", "3 John", "Jude", "Revelation"]
SLUGS = [n.lower().replace(' ', '-') for n in ENGLISH]
SLUGS[SLUGS.index('psalm')] = 'psalms'

# The book names a reader's page reads as citations (essenthos-web src/lib/citations.ts), for the books
# the reasons cite.
BOOKS = {
    'ukr': {'GEN': 'Буття', 'NUM': 'Числа', 'JOS': 'Ісуса Навина', 'JDG': 'Суддів', '1SA': '1 Самуїлова',
            '2SA': '2 Самуїлова', '1KI': '1 Царів', '2KI': '2 Царів', '1CH': '1 Хронік', '2CH': '2 Хронік',
            'EZR': 'Ездри', 'NEH': 'Неемії', 'PSA': 'Псалом', 'JER': 'Єремії', 'DAN': 'Даниїла', 'HOS': 'Осії',
            'ZEC': 'Захарії', 'MAT': 'Матвія', 'MRK': 'Марка', 'LUK': 'Луки', 'JHN': 'Івана', 'REV': "Об'явлення"},
    'deu': {'GEN': '1. Mose', 'NUM': '4. Mose', 'JOS': 'Josua', 'JDG': 'Richter', '1SA': '1. Samuel',
            '2SA': '2. Samuel', '1KI': '1. Könige', '2KI': '2. Könige', '1CH': '1. Chronik', '2CH': '2. Chronik',
            'EZR': 'Esra', 'NEH': 'Nehemia', 'PSA': 'Psalm', 'JER': 'Jeremia', 'DAN': 'Daniel', 'HOS': 'Hosea',
            'ZEC': 'Sacharja', 'MAT': 'Matthäus', 'MRK': 'Markus', 'LUK': 'Lukas', 'JHN': 'Johannes',
            'REV': 'Offenbarung'},
    'spa': {'GEN': 'Génesis', 'NUM': 'Números', 'JOS': 'Josué', 'JDG': 'Jueces', '1SA': '1 Samuel',
            '2SA': '2 Samuel', '1KI': '1 Reyes', '2KI': '2 Reyes', '1CH': '1 Crónicas', '2CH': '2 Crónicas',
            'EZR': 'Esdras', 'NEH': 'Nehemías', 'PSA': 'Salmo', 'JER': 'Jeremías', 'DAN': 'Daniel', 'HOS': 'Oseas',
            'ZEC': 'Zacarías', 'MAT': 'Mateo', 'MRK': 'Marcos', 'LUK': 'Lucas', 'JHN': 'Juan',
            'REV': 'Apocalipsis'},
}

NAME_OF = {name: code for code, name in zip(CODES, ENGLISH)}
_codes = '|'.join(sorted(CODES, key=len, reverse=True))
_names = '|'.join(sorted(NAME_OF, key=len, reverse=True))
REF = re.compile(rf"(?<![\w-])({_codes}|{_names}) (\d{{1,3}}):(\d{{1,3}})(?:[-–](\d{{1,3}}))?")
BARE = re.compile(r"^(?:,|;| and) ?(\d{1,3}):(\d{1,3})(?:[-–](\d{1,3}))?")
VERSE_NUMBERS = re.compile(r"\d{1,3}:\d{1,3}(?:[-–]\d{1,3})?")

SCHEMA = {
    'type': 'object',
    'properties': {'items': {'type': 'array', 'items': {
        'type': 'object',
        'properties': {'id': {'type': 'string'}, 'text': {'type': 'string'}},
        'required': ['id', 'text'], 'additionalProperties': False}}},
    'required': ['items'], 'additionalProperties': False,
}


def digest(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def refs(text):
    """Every verse a reason cites, as (code, chapter, first, last), a bare address after one taking its book."""
    out = []
    for m in REF.finditer(text):
        code = m.group(1) if m.group(1) in CODES else NAME_OF[m.group(1)]
        first = int(m.group(3))
        out.append((code, int(m.group(2)), first, int(m.group(4)) if m.group(4) else first))
        rest = m.end()
        while bare := BARE.match(text[rest:]):
            first = int(bare.group(2))
            out.append((code, int(bare.group(1)), first, int(bare.group(3)) if bare.group(3) else first))
            rest += bare.end()
    return out


# ---------------------------------------------------------------- items

def read_items():
    items = {}

    def add(text, record, other, side, file):
        key = digest(text)
        item = items.setdefault(key, {'id': key[:12], 'sha256': key, 'english': text, 'said': []})
        item['said'].append({'record': record, 'other': other, 'side': side, 'file': file})

    for path in sorted(glob.glob(str(RULINGS / '*.json'))):
        try:
            data = json.load(open(path, encoding='utf-8-sig'))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        if not isinstance(data, dict):
            continue
        for key in ('rulings', 'titles'):
            for ruling in data.get(key) or []:
                if not isinstance(ruling, dict) or not ruling.get('alternatives'):
                    continue
                record = (ruling['create']['slug'] if ruling.get('create')
                          else ruling.get('existing') or ruling.get('slug'))
                for alternative in ruling['alternatives']:
                    other = alternative.get('slug') or alternative.get('describes')
                    add(alternative['reason'], record, other, 'forward', os.path.basename(path))
                    if alternative.get('back'):
                        add(alternative['back'], alternative['slug'], record, 'back', os.path.basename(path))
    return sorted(items.values(), key=lambda i: i['sha256'])


def cmd_items():
    items = read_items()
    json.dump(items, open(HERE / 'items.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f"{len(items)} distinct reasons, {sum(len(i['english']) for i in items)} characters")


def items():
    return json.load(open(HERE / 'items.json', encoding='utf-8'))


# ---------------------------------------------------------------- export

def get(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def cmd_export():
    EXPORT.mkdir(exist_ok=True)
    wanted = set()
    for item in items():
        for code, chapter, first, last in refs(item['english']):
            for verse in range(first, min(last, first + 5) + 1):
                wanted.add((code, chapter, verse))
    wanted = sorted(wanted, key=lambda r: (CODES.index(r[0]), r[1], r[2]))
    for corpus in ('KJV', 'UBIO', 'LUTH1912', 'RV1909'):
        texts = {}
        for start in range(0, len(wanted), 40):
            part = wanted[start:start + 40]
            query = ','.join(f'{SLUGS[CODES.index(c)]}:{ch}:{v}' for c, ch, v in part)
            data = get(f"{API}/v1/verses?corpus={corpus}&refs={urllib.parse.quote(query, safe=':,')}")
            for verse in data['items']:
                code = CODES[verse['book']['ordinal'] - 1]
                texts[f"{code} {verse['chapter']}:{verse['verse']}"] = verse['text']
        json.dump(texts, open(EXPORT / f'verses-{corpus}.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print(corpus, len(texts), 'of', len(wanted), 'verses')

    capitalised = sorted({m.group(0) for item in items()
                          for m in re.finditer(r"\b[A-Z][a-z]+(?:-[a-z]+)*", item['english'])})
    names = ','.join("'" + n.replace("'", "''") + "'" for n in capitalised)
    sql = ("SET statement_timeout = '20s';\n"
           "COPY (SELECT e.name, f.language, string_agg(DISTINCT f.form, '|') FROM entity e "
           "JOIN entity_name_form f ON f.entity_id = e.id "
           "WHERE f.grammatical_case = 'nominative' AND f.language IN ('ukr', 'deu', 'spa') "
           f"AND e.kind IN ('person', 'place', 'people', 'title') AND e.name IN ({names}) "
           "GROUP BY e.name, f.language ORDER BY 1, 2) TO STDOUT WITH CSV;\n")
    out = subprocess.run(['docker', 'exec', '-i', 'essenthos-core-db-1', 'sh', '-c',
                          'psql -q -U "$POSTGRES_USER" -d essenthos_core'],
                         input=sql, capture_output=True, text=True, encoding='utf-8', check=True).stdout
    glossary = collections.defaultdict(dict)
    import csv
    import io
    for name, language, forms in csv.reader(io.StringIO(out)):
        glossary[name][language] = forms.split('|')
    json.dump(glossary, open(EXPORT / 'names.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1, sort_keys=True)
    print(len(glossary), 'names spelled')
    manifest = {p.name: digest(p.read_text(encoding='utf-8')) for p in sorted(EXPORT.glob('*.json'))}
    json.dump(manifest, open(HERE / 'export-sha256.json', 'w', encoding='utf-8'), indent=1)


# ---------------------------------------------------------------- requests

def system_prompt(language):
    tongue, _, bible = LANGS[language]
    books = '; '.join(f'{code} = {name}' for code, name in BOOKS[language].items())
    return f"""You translate short passages of an encyclopedia of the Bible into {tongue}, for a reader of its {tongue} edition. Each passage is the reason given on a person's (or place's) page for why that person may be someone else the text names, or why another person may be this one. Render it faithfully and completely: say what the English says, no more and no less, in natural modern {tongue}. Keep its tone: careful, plain, never more certain or less certain than the English. Do not explain, summarise, correct or comment.

Rules:
1. Quoted scripture. Where the English quotes words of a verse (in quotation marks), use the words of {bible} for them, which the item gives under "verses" for each verse it cites: take the corresponding words of that verse, not a fresh translation of the English quotation. Put them in the quotation marks usual in {tongue}.
2. References. Write every Bible reference with the book's name in {tongue} followed by chapter and verse, using these names: {books}. A book code (1CH 2:18) and an English book name (1 Chronicles 2:18) are both written so: e.g. {BOOKS[language]['1CH']} 2:18. Keep each reference where the English has it, with its brackets, its verse numbers and ranges exactly as they are; keep a bare "1:9" after a reference bare.
3. Names. Where "names" gives a spelling for a name, use it, declined as the sentence needs (where it gives several, use the one that fits the person meant). Other names of people and places: as {bible} spells them. Never leave a Bible name in its English spelling.
4. Words about the text: the Hebrew, the Septuagint, the Vulgate, Strong's Dictionary, the King James translators — translate them as {tongue} usually names them. "The text does not say" and "Nobody has shown" keep their exact force.
5. The LORD written in capitals in a quotation is the divine name as {bible} gives it in that verse.

Answer with every item, by its id, and nothing else."""


def payload(item, language, verses, names):
    _, corpus, _ = LANGS[language]
    shown = {}
    for code, chapter, first, last in refs(item['english']):
        for verse in range(first, min(last, first + 5) + 1):
            key = f'{code} {chapter}:{verse}'
            if key in verses[corpus] and len(shown) < 10:
                shown[key] = verses[corpus][key]
    one = {'id': item['id'], 'english': item['english']}
    if shown:
        one['verses'] = shown
    spelled = {}
    for m in re.finditer(r"\b[A-Z][a-z]+(?:-[a-z]+)*", item['english']):
        forms = names.get(m.group(0), {}).get(language)
        if forms:
            spelled[m.group(0)] = forms if len(forms) > 1 else forms[0]
    if spelled:
        one['names'] = spelled
    return one


def requests():
    verses = {corpus: json.load(open(EXPORT / f'verses-{corpus}.json', encoding='utf-8'))
              for _, corpus, _ in LANGS.values()}
    names = json.load(open(EXPORT / 'names.json', encoding='utf-8'))
    every = items()
    out = []
    for language in LANGS:
        for start in range(0, len(every), MOST_ITEMS):
            chunk = every[start:start + MOST_ITEMS]
            prompt = json.dumps({'items': [payload(i, language, verses, names) for i in chunk]},
                                ensure_ascii=False, indent=1)
            params = claude_api.build_params(prompt, system=system_prompt(language), model=MODEL, effort=EFFORT,
                                             max_tokens=12000, schema=SCHEMA, ttl='5m')
            out.append((f'{language}-{start // MOST_ITEMS:02d}', params))
    return out


def cmd_plan():
    reqs = requests()
    print(json.dumps(claude_api.batch_plan(reqs), indent=1))


def cmd_batch():
    reqs = requests()
    RAW.mkdir(exist_ok=True)
    json.dump([{'id': i, 'params': p} for i, p in reqs], open(RAW / 'requests.json', 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    batch = claude_api.batch_submit(reqs, label=LABEL, cap_usd=CAP_USD)
    with open(HERE / 'batches.jsonl', 'a', encoding='utf-8') as log:
        log.write(json.dumps({'batch': batch, 'requests': len(reqs), 'model': MODEL, 'effort': EFFORT,
                              'prompt': PROMPT_VERSION}) + '\n')
    print(batch)


def cmd_collect(batch):
    claude_api.batch_wait(batch, poll=30)
    answers, failures = claude_api.batch_results(batch, label=LABEL)
    RAW.mkdir(exist_ok=True)
    for custom_id, text in answers.items():
        (RAW / f'{custom_id}.json').write_text(text, encoding='utf-8')
    print(len(answers), 'answers,', len(failures), 'failures', failures)


def answers():
    by = collections.defaultdict(dict)
    for path in sorted(RAW.glob('*-[0-9][0-9].json')):
        language = path.name.split('-')[0]
        for one in json.loads(path.read_text(encoding='utf-8'))['items']:
            by[one['id']][language] = one['text'].strip()
    return by


LATIN = re.compile(r'[A-Za-z]{2,}')


def problems():
    found = []
    by = corrected() if (HERE / 'corrections.json').exists() else answers()
    for item in items():
        rendered = by.get(item['id'], {})
        for language in LANGS:
            text = rendered.get(language)
            if not text:
                found.append(f"{item['id']} {language}: missing")
                continue
            if sorted(VERSE_NUMBERS.findall(item['english'])) != sorted(VERSE_NUMBERS.findall(text)):
                found.append(f"{item['id']} {language}: verse numbers {VERSE_NUMBERS.findall(item['english'])} "
                             f"-> {VERSE_NUMBERS.findall(text)}")
            if re.search(rf"\b(?:{_codes}) \d", text):
                found.append(f"{item['id']} {language}: a book code left in")
            if language == 'ukr' and LATIN.search(text):
                found.append(f"{item['id']} {language}: Latin letters: {LATIN.findall(text)}")
    return found


def cmd_check():
    found = problems()
    print('\n'.join(found) or 'every item rendered in every language, verse numbers kept')


def corrected():
    """The answers with the read corrections (corrections.json) spliced in; each must still find its words."""
    by = answers()
    for one in json.load(open(HERE / 'corrections.json', encoding='utf-8'))['corrections']:
        text = by[one['id']][one['language']]
        if text.count(one['from']) != 1:
            raise SystemExit(f"correction {one['id']} {one['language']}: {one['from']!r} is not in the answer once")
        by[one['id']][one['language']] = text.replace(one['from'], one['to'])
    return by


def cmd_assemble():
    by = corrected()
    entries = []
    for item in sorted(items(), key=lambda i: i['english']):
        rendered = by[item['id']]
        entries.append({'english': item['english'], 'ukr': rendered['ukr'], 'deu': rendered['deu'],
                        'spa': rendered['spa']})
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'source': SOURCE, 'entries': entries}, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print(len(entries), 'entries ->', OUT)


def main(argv):
    command = argv[1] if len(argv) > 1 else ''
    if command == 'items':
        cmd_items()
    elif command == 'export':
        cmd_export()
    elif command == 'plan':
        cmd_plan()
    elif command == 'batch':
        cmd_batch()
    elif command == 'collect':
        cmd_collect(argv[2])
    elif command == 'check':
        cmd_check()
    elif command == 'assemble':
        cmd_assemble()
    elif command == 'spent':
        print(claude_api.format_summary(claude_api.summary(LABEL)))
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
