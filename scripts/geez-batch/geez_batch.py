"""
Claude batches for the Ge'ez dictionary (FTR-0824 Dillmann renderings, FTR-1141 contextual glosses).

A thin layer over scripts/claude_api.py (NOT-0210): it exports the inputs, lays out the requests
with build_params, submits them with batch_submit under a cap, and validates what comes back. Every
run lives in one folder (default Resources/Essenthos/generation/<run>/, gitignored, backed up):

    inputs/            the frozen export the requests were built from
    requests.jsonl     one line per request: custom_id, params
    manifest.json      job, model, effort, prompt version, hashes, custom_id -> the items it asks
    submissions.jsonl  every batch sent: batch id, label, the custom_ids it carried, the manifest hash
    raw/<batch>.jsonl  each batch's results as downloaded, once (the ledger is written then)
    clean.jsonl        answers that passed every check, one line per item
    rejects.jsonl      requests to send again, with why

Commands (README.md has the order):
    export-dillmann  --run R                      Dillmann's senses from the TEI + the loaded entry list
    export-gloss     --run R --chapters GEN:1,…   Ge'ez words, their links, the Greek, the English, Dillmann
    build            --run R --job dillmann|gloss --model M --effort E [--from RUN]
    estimate         --run R [--thinking 1.0]     offline tokens and dollars at batch prices
    submit           --run R --budget 125 [--sample N | --next N | --ids …] [--force]
    collect          --run R [--wait] [--fake answers.jsonl]
    report           --run R

The key is never read here: claude_api reads ANTHROPIC_API_KEY (or the git-ignored .env) itself.
"""
import argparse
import datetime as dt
import hashlib
import json
import re
import subprocess
import sys
import unicodedata
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import prompts  # noqa: E402

REPO = HERE.parent.parent
TEI = '{http://www.tei-c.org/ns/1.0}'
XML_LANG = '{http://www.w3.org/XML/1998/namespace}lang'
XML_ID = '{http://www.w3.org/XML/1998/namespace}id'
DILLMANN_FOLDERS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', 'new', 'new1']
LABEL_PREFIX = 'geez-'
MODELS = ('claude-sonnet-5-5', 'claude-opus-5-5')
# Canonical ordinals of the pilot's books; the full run lists them from the database.
BOOKS = {'GEN': 1, 'PSA': 19, 'JHN': 43}
NT_FIRST_ORDINAL = 40


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'))


def digest(value):
    return hashlib.sha256(canonical(value).encode('utf-8')).hexdigest()


def nfc(text):
    return unicodedata.normalize('NFC', text)


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.tmp')
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    tmp.replace(path)


def read_jsonl(path):
    path = Path(path)
    if not path.is_file():
        return []
    return [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]


def write_jsonl(path, rows):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.tmp')
    with tmp.open('w', encoding='utf-8') as out:
        for row in rows:
            out.write(json.dumps(row, ensure_ascii=False) + '\n')
    tmp.replace(path)


def append_jsonl(path, row):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a', encoding='utf-8') as out:
        out.write(json.dumps(row, ensure_ascii=False) + '\n')


def claude_api(repo):
    """The project's client from `repo` (the main checkout runs against its own ledger and .env)."""
    sys.path.insert(0, str(Path(repo) / 'scripts'))
    import claude_api as client
    return client


# ---------------------------------------------------------------------------------------------------
# Reading the live corpus (read-only, one statement per call, as guide `database` says)

def psql_rows(sql):
    """One SELECT through the container's own psql; returns the rows of a single JSON column."""
    script = "SET statement_timeout='120s';\n\\copy (" + ' '.join(sql.split()).rstrip(';') + ") to stdout\n"
    done = subprocess.run(['docker', 'exec', '-i', 'essenthos-core-db-1', 'sh', '-c',
                           'psql -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d essenthos_core'],
                          input=script.encode('utf-8'), capture_output=True, check=False)
    if done.returncode != 0:
        raise SystemExit('psql failed: ' + done.stderr.decode('utf-8', 'replace')[-2000:])
    # \copy text format escapes backslashes; JSON from json_build_object has them doubled.
    lines = done.stdout.decode('utf-8').splitlines()
    return [json.loads(line.replace('\\\\', '\\')) for line in lines if line.strip()]


def api_json(base, path):
    with urllib.request.urlopen(base.rstrip('/') + path, timeout=120) as response:
        return json.loads(response.read().decode('utf-8'))


# ---------------------------------------------------------------------------------------------------
# Dillmann's senses from the TEI

def _lang(element):
    return element.get(XML_LANG) or ''


def _collapse(text):
    return re.sub(r'\s+', ' ', text or '').strip()


def _is_dillmann(element):
    return element.tag == TEI + 'sense' and element.get('source') == '#dillmann'


def dillmann_senses(root):
    """
    Dillmann's own senses of one TEI entry (source="#dillmann"), in his order and numbering. Each is
    {"n", "la", "grc", "note", "refs"}: his Latin glosses outside his square-bracketed notes on the
    cognates (which gloss the Arabic or Syriac, not the Ge'ez — the loader's rule too), the Greek he
    cites, the rest of the sense's text with the references taken out, and the references.
    """
    entry = root.find(f'.//{TEI}entry')
    if entry is None:
        return []
    out = []
    tops = [s for s in entry.findall(f'{TEI}sense') if _is_dillmann(s)]
    for i, top in enumerate(tops):
        _walk(top, 'main' if i == 0 else f'main{i + 1}', out)
    return out


def _walk(sense, label, out):
    own = {'n': label, 'la': [], 'grc': [], 'note': [], 'refs': []}
    out.append(own)
    depth = 0

    def text(piece):
        nonlocal depth
        if piece:
            depth += piece.count('[') - piece.count(']')
            own['note'].append(piece)

    text(sense.text)
    for child in sense:
        if child.tag == TEI + 'sense':
            n = child.get('n') or ''
            _walk(child, (n if label.startswith('main') else label + n) or label + '?', out)
        elif child.tag == TEI + 'cit' and child.get('type') == 'translation' and _lang(child) == 'la':
            gloss = _collapse(''.join(child.itertext())).strip(' ,;:')
            if depth <= 0 and sum(c.isalpha() for c in gloss) >= 2:
                own['la'].append(gloss)
            own['note'].append(gloss)
        elif child.tag == TEI + 'ref':
            own['refs'].append(_collapse(''.join(child.itertext())))
        elif child.tag == TEI + 'foreign' and _lang(child) == 'grc':
            greek = _collapse(''.join(child.itertext()))
            if greek:
                own['grc'].append(greek)
            own['note'].append(greek)
        else:
            own['note'].append(''.join(child.itertext()))
        text(child.tail)
    note = _collapse(' '.join(own['note']))
    note = re.sub(r'\s+([,;:.])', r'\1', note)
    # The references are gone; the commas and semicolons that stood between them go too.
    note = re.sub(r'(?:[;,:]\s*){2,}', '; ', note)
    note = re.sub(r'(?:[;,:]\s*)+\.$', '.', note).strip(' ;,')
    own['note'] = note[:300]
    own['la'] = list(dict.fromkeys(own['la']))
    own['grc'] = list(dict.fromkeys(own['grc']))


def tei_index(folder):
    index = {}
    for name in DILLMANN_FOLDERS:
        for path in sorted((Path(folder) / name).glob('L*.xml')):
            index.setdefault(path.stem, path)
    return index


def export_dillmann(run, resources, rows=None):
    """inputs/dillmann.jsonl: every loaded entry (geez_lexicon_entry) with Dillmann's senses."""
    rows = rows if rows is not None else psql_rows(
        "select json_build_object('entry',entry,'headword',headword,'forms',forms,'latin',latin,'greek',greek)"
        " from geez_lexicon_entry order by entry")
    index = tei_index(Path(resources) / 'Dillmann')
    entries, report = [], {'loaded': len(rows), 'no_tei': 0, 'fallback_latin': 0, 'no_latin': 0,
                           'latin_not_in_tei': 0, 'senses_asked': 0}
    for row in rows:
        path = index.get(row['entry'])
        senses = dillmann_senses(ET.parse(path).getroot()) if path else []
        if path is None:
            report['no_tei'] += 1
        tei_latin = {g for s in senses for g in s['la']}
        if any(g not in tei_latin for g in row['latin']):
            report['latin_not_in_tei'] += 1
        if not tei_latin and row['latin']:
            # A name Dillmann gives as plain Latin ("Epicurus"): the loader's own fallback.
            report['fallback_latin'] += 1
            senses = [{'n': 'main', 'la': list(row['latin']), 'grc': list(row['greek']), 'note': '', 'refs': []}]
        asked = [s for s in senses if s['la']]
        report['senses_free'] = report.get('senses_free', 0) + sum(1 for s in senses if not s['la'] and asked_sense(s)) * bool(asked)
        if not asked:
            report['no_latin'] += 1
        report['senses_asked'] += len(asked)
        entries.append({'entry': row['entry'], 'headword': row['headword'], 'forms': row['forms'],
                        'latin': row['latin'], 'greek': row['greek'], 'senses': senses})
    write_jsonl(Path(run) / 'inputs' / 'dillmann.jsonl', entries)
    report['entries_with_latin'] = report['loaded'] - report['no_latin']
    write_json(Path(run) / 'inputs' / 'dillmann-export.json', report)
    return report


# ---------------------------------------------------------------------------------------------------
# The contextual-gloss passages

GLOSS_SQL = """
with ge as (
  select w.id, v.chapter_number c, v.number v, v.label, w.position p, w.text t
  from word w join verse v on v.id = w.verse_id join text x on x.id = w.text_id join book b on b.id = v.book_id
  where x.slug = 'GEEZ81' and b.canonical_ordinal = {ordinal} and v.chapter_number = {chapter}),
links as (
  select lf.word_id ge, lt.word_id gr, l.confidence conf
  from link l join text tt on tt.id = l.to_text_id
  join link_word lf on lf.link_id = l.id and lf.side = 'from'
  join link_word lt on lt.link_id = l.id and lt.side = 'to'
  where l.from_text_id = (select id from text where slug = 'GEEZ81') and tt.slug = '{greek}'
    and lf.word_id in (select id from ge)),
gr as (
  select w.id, v.chapter_number c, v.number v, v.label, w.position p, w.text t, w.lemma
  from word w join verse v on v.id = w.verse_id join text x on x.id = w.text_id join book b on b.id = v.book_id
  where x.slug = '{greek}' and b.canonical_ordinal = {ordinal}
    and (v.chapter_number = {chapter} or w.id in (select gr from links))),
en as (
  select v.chapter_number c, v.number v, v.label, string_agg(w.text || w.trailer, '' order by w.position) t
  from word w join verse v on v.id = w.verse_id join text x on x.id = w.text_id join book b on b.id = v.book_id
  where x.slug = '{english}' and b.canonical_ordinal = {ordinal}
    and (v.chapter_number, v.number) in (select distinct c, v from gr)
  group by 1, 2, 3)
select json_build_object(
  'geez', (select json_agg(json_build_object('id', id::text, 'c', c, 'v', v, 'label', label, 'p', p, 't', t) order by c, v, label, p) from ge),
  'links', (select json_agg(json_build_object('ge', ge::text, 'gr', gr::text, 'conf', conf)) from links),
  'greek', (select json_agg(json_build_object('id', id::text, 'c', c, 'v', v, 'label', label, 'p', p, 't', t, 'lemma', lemma) order by c, v, label, p) from gr),
  'english', (select json_agg(json_build_object('c', c, 'v', v, 'label', label, 't', t) order by c, v, label) from en))
"""


def chapter_sources(ordinal):
    return ('NESTLE1904', 'KJV') if ordinal >= NT_FIRST_ORDINAL else ('GRCBRENT', 'BRENTON')


def export_chapter(book, ordinal, chapter, api_base):
    greek, english = chapter_sources(ordinal)
    data = psql_rows(GLOSS_SQL.format(ordinal=ordinal, chapter=chapter, greek=greek, english=english))[0]
    pane = api_json(api_base, f'/v1/panes/GEEZ81/{ordinal}/{chapter}')
    dillmann = {}
    for verse in pane.get('verses', []):
        for word in verse.get('words', []):
            if word.get('gz'):
                gz = word['gz']
                dillmann[str(word['id'])] = {'headword': gz['headword'], 'latin': gz['latin'][:6],
                                             'greek': gz['greek'][:4], 'via': gz['via']}
    return {'book': book, 'ordinal': ordinal, 'chapter': chapter, 'greek_text': greek, 'english_text': english,
            'geez': data['geez'] or [], 'links': data['links'] or [], 'greek': data['greek'] or [],
            'english': data['english'] or [], 'dillmann': dillmann}


def export_gloss(run, chapters, api_base):
    out = []
    for book, ordinal, chapter in chapters:
        out.append(export_chapter(book, ordinal, chapter, api_base))
    write_jsonl(Path(run) / 'inputs' / 'gloss-chapters.jsonl', out)
    # Every spelling of every loaded Dillmann headword, to check a lemma the model says is his.
    forms = psql_rows("select json_agg(distinct f order by f) from geez_lexicon_entry, unnest(forms) f")[0]
    write_json(Path(run) / 'inputs' / 'dillmann-headwords.json', forms)
    return {'chapters': len(out), 'geez_words': sum(len(c['geez']) for c in out),
            'with_dillmann': sum(len(c['dillmann']) for c in out),
            'linked': sum(len({link['ge'] for link in c['links']}) for c in out)}


def parse_chapters(spec):
    chapters = []
    for part in spec.split(','):
        book, _, chapter = part.strip().partition(':')
        if book.upper() not in BOOKS:
            raise SystemExit(f'unknown book {book}; known: {sorted(BOOKS)} (or --all-chapters)')
        chapters.append((book.upper(), BOOKS[book.upper()], int(chapter)))
    return chapters


def all_chapters():
    rows = psql_rows("select json_build_object('book', b.abbreviation, 'ordinal', b.canonical_ordinal, 'chapter', c)"
                     " from (select distinct v.book_id, v.chapter_number c from verse v join text x on x.id = v.text_id"
                     " where x.slug = 'GEEZ81') v join book b on b.id = v.book_id order by b.canonical_ordinal, c")
    return [(r['book'] or str(r['ordinal']), r['ordinal'], r['chapter']) for r in rows]


# ---------------------------------------------------------------------------------------------------
# Building requests

LATIN_WORDS = re.compile(r'[A-Za-z]{3,}')


def asked_sense(sense):
    """
    A sense is rendered when it has Latin glosses, or (below the entry's opening, which is grammar
    and cognates) when its Latin text states a meaning without marking it up as a gloss: ቃል 2c
    "quatenus sunt dicta, effata, verba". Those come with empty "la" and may be answered empty.
    """
    if sense['la']:
        return True
    return not sense['n'].startswith('main') and len(LATIN_WORDS.findall(sense['note'])) >= 2


def dillmann_item(entry, key):
    senses = []
    for s in entry['senses']:
        if not asked_sense(s):
            continue
        sense = {'n': s['n'], 'la': s['la']}
        if s['grc']:
            sense['grc'] = s['grc'][:6]
        note = s['note']
        if note and note != ', '.join(s['la']):
            sense['note'] = note
        senses.append(sense)
    item = {'id': key, 'headword': entry['headword'], 'senses': senses}
    if len(entry['forms']) > 1:
        item['forms'] = entry['forms'][1:]
    return item


def group_dillmann(entries, max_entries=30, max_tokens=3000, estimate=None):
    """Consecutive entries into requests of at most max_entries and about max_tokens of input each."""
    estimate = estimate or (lambda text: len(text.encode('utf-8')) // 3 + 1)
    groups, current, size = [], [], 0
    for entry in entries:
        cost = estimate(canonical(dillmann_item(entry, 'e00')))
        if current and (len(current) >= max_entries or size + cost > max_tokens):
            groups.append(current)
            current, size = [], 0
        current.append(entry)
        size += cost
    if current:
        groups.append(current)
    return groups


def build_dillmann(run, client, model, effort, ttl='5m', max_entries=30, max_tokens_in=3000):
    entries = [e for e in read_jsonl(Path(run) / 'inputs' / 'dillmann.jsonl') if any(s['la'] for s in e['senses'])]
    requests, manifest_items = [], {}
    for i, group in enumerate(group_dillmann(entries, max_entries, max_tokens_in, client.estimate_tokens), 1):
        custom_id = f'dl-{i:04d}'
        items = {}
        packet = []
        for j, entry in enumerate(group, 1):
            key = f'e{j}'
            item = dillmann_item(entry, key)
            packet.append(item)
            items[key] = {'entry': entry['entry'], 'headword': entry['headword'],
                          'senses': [s['n'] for s in item['senses']],
                          'free': [s['n'] for s in item['senses'] if not s['la']]}
        out_tokens = 60 + sum(14 + 4 * 18 for item in packet for _ in item['senses'])
        params = client.build_params({'entries': packet}, system=prompts.DILLMANN_SYSTEM, model=model,
                                     effort=effort, schema=prompts.DILLMANN_SCHEMA, ttl=ttl,
                                     max_tokens=max(8000, out_tokens * 6))
        requests.append({'custom_id': custom_id, 'params': params})
        manifest_items[custom_id] = items
    return requests, manifest_items


def passages(chapter, max_words=90):
    """A chapter's Ge'ez verses cut into windows of whole verses of at most about max_words words."""
    verses = {}
    for word in chapter['geez']:
        verses.setdefault((word['c'], word['v'], word['label']), []).append(word)
    windows, current = [], []
    for key in sorted(verses):
        if current and sum(len(verses[k]) for k in current) + len(verses[key]) > max_words:
            windows.append(current)
            current = []
        current.append(key)
    if current:
        windows.append(current)
    return [[(k, verses[k]) for k in window] for window in windows]


def gloss_packet(chapter, window):
    """The request's packet and the key map: Ge'ez words w1…, Greek words g1… of the verses it touches."""
    links = {}
    for link in chapter['links']:
        links.setdefault(link['ge'], []).append(link['gr'])
    greek_by_id = {g['id']: g for g in chapter['greek']}
    numbers = {(c, v) for (c, v, _), _ in window}
    touched = {(greek_by_id[g]['c'], greek_by_id[g]['v'])
               for (_, words) in window for w in words for g in links.get(w['id'], []) if g in greek_by_id}
    span = sorted(numbers | touched)
    first, last = span[0], span[-1]
    greek_words = [g for g in chapter['greek'] if first <= (g['c'], g['v']) <= last]
    gkeys = {g['id']: f'g{i}' for i, g in enumerate(greek_words, 1)}
    wkeys, geez_out = {}, []
    n = 0
    for (c, v, label), words in window:
        verse = {'ref': f'{c}:{v}{label}', 'words': []}
        for w in words:
            n += 1
            key = f'w{n}'
            wkeys[key] = w['id']
            entry = {'w': key, 't': w['t']}
            aligned = [gkeys[g] for g in links.get(w['id'], []) if g in gkeys]
            if aligned:
                entry['aligned'] = aligned
            if w['id'] in chapter['dillmann']:
                gz = chapter['dillmann'][w['id']]
                entry['dillmann'] = {'headword': gz['headword'], 'latin': gz['latin'], 'greek': gz['greek']}
            verse['words'].append(entry)
        geez_out.append(verse)
    greek_out = []
    for g in greek_words:
        ref = f"{g['c']}:{g['v']}{g['label']}"
        if not greek_out or greek_out[-1]['ref'] != ref:
            greek_out.append({'ref': ref, 'words': []})
        word = {'g': gkeys[g['id']], 't': g['t']}
        if g.get('lemma'):
            word['lemma'] = g['lemma']
        greek_out[-1]['words'].append(word)
    english = [{'ref': f"{e['c']}:{e['v']}{e['label']}", 't': e['t'].strip()} for e in chapter['english']
               if first <= (e['c'], e['v']) <= last]
    packet = {'book': chapter['book'], "ge'ez": geez_out, 'greek': greek_out, 'english': english}
    return packet, wkeys, {k: v for v, k in gkeys.items()}


def build_gloss(run, client, model, effort, ttl='5m', max_words=90):
    requests, manifest_items = [], {}
    for chapter in read_jsonl(Path(run) / 'inputs' / 'gloss-chapters.jsonl'):
        for i, window in enumerate(passages(chapter, max_words), 1):
            packet, wkeys, gkeys = gloss_packet(chapter, window)
            custom_id = f"gl-{chapter['ordinal']:02d}-{chapter['chapter']:03d}-{i:02d}"
            params = client.build_params(packet, system=prompts.GLOSS_SYSTEM, model=model, effort=effort,
                                         schema=prompts.GLOSS_SCHEMA, ttl=ttl,
                                         max_tokens=max(16000, 400 * len(wkeys)))
            requests.append({'custom_id': custom_id, 'params': params})
            manifest_items[custom_id] = {'book': chapter['book'], 'ordinal': chapter['ordinal'],
                                         'chapter': chapter['chapter'], 'words': wkeys, 'greek': gkeys,
                                         'greek_text': chapter['greek_text'],
                                         'refs': [p['ref'] for p in packet["ge'ez"]]}
    return requests, manifest_items


def build(run, job, client, model, effort, ttl='5m', copy_inputs_from=None):
    run = Path(run)
    if copy_inputs_from:
        source = Path(copy_inputs_from) / 'inputs'
        for path in source.iterdir():
            target = run / 'inputs' / path.name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
    if (run / 'submissions.jsonl').is_file():
        raise SystemExit(f'{run} has submissions already: build into a new run folder')
    if job == 'dillmann':
        requests, items = build_dillmann(run, client, model, effort, ttl)
        system, schema = prompts.DILLMANN_SYSTEM, prompts.DILLMANN_SCHEMA
    else:
        requests, items = build_gloss(run, client, model, effort, ttl)
        system, schema = prompts.GLOSS_SYSTEM, prompts.GLOSS_SCHEMA
    write_jsonl(run / 'requests.jsonl', requests)
    inputs = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((run / 'inputs').glob('*'))}
    manifest = {'at': now(), 'run': run.name, 'job': job, 'model': model, 'effort': effort, 'ttl': ttl,
                'prompt_version': prompts.PROMPT_VERSION, 'system_sha256': digest(system),
                'schema_sha256': digest(schema), 'inputs_sha256': inputs,
                'requests_sha256': hashlib.sha256((run / 'requests.jsonl').read_bytes()).hexdigest(),
                'requests': len(requests), 'items': items}
    write_json(run / 'manifest.json', manifest)
    return manifest


# ---------------------------------------------------------------------------------------------------
# Estimating

# Visible output per item, from the answer schemas with typical values (tokens, high side).
OUT_PER_DILLMANN_SENSE = 14 + 4 * 14
OUT_PER_GLOSS_WORD = 75


def estimate(run, client, thinking=1.0, models=MODELS):
    """
    Offline: input tokens by claude_api's high estimate (UTF-8 bytes / 3; Ethiopic is 3 bytes a letter),
    the system prefix read from the cache after the first request, visible output from the schema,
    thinking as a multiple of it (unknown before the pilot: give 0.5-2). Batch prices, every model.
    """
    run = Path(run)
    manifest = read_json(run / 'manifest.json')
    requests = read_jsonl(run / 'requests.jsonl')
    unique, prefix, out_visible = 0, 0, 0
    for request in requests:
        params = request['params']
        system = ''.join(b['text'] for b in params.get('system', []))
        prefix = client.estimate_tokens(system) + client.estimate_tokens(canonical(params['output_config']))
        unique += sum(client.estimate_tokens(b['text']) for b in params['messages'][0]['content'])
    for items in manifest['items'].values():
        if manifest['job'] == 'dillmann':
            out_visible += 40 + sum(OUT_PER_DILLMANN_SENSE * len(i['senses']) for i in items.values())
        else:
            out_visible += 30 + OUT_PER_GLOSS_WORD * len(items['words'])
    n = len(requests)
    result = {'run': run.name, 'job': manifest['job'], 'requests': n,
              'items': sum(len(i) if manifest['job'] == 'dillmann' else len(i['words'])
                           for i in manifest['items'].values()),
              'input_unique_tokens': unique, 'system_prefix_tokens': prefix,
              'output_visible_tokens': out_visible, 'thinking_multiple': thinking, 'usd_batch': {}}
    for model in models:
        price = client.PRICES[model]
        write = prefix * price['input'] * client.WRITE_MULTIPLIER[manifest.get('ttl', '5m')]
        reads = prefix * (n - 1) * price['cache_read']
        usd = (unique * price['input'] + write + reads + out_visible * (1 + thinking) * price['output']) / 1e6 / 2
        result['usd_batch'][model] = round(usd, 2)
    return result


# ---------------------------------------------------------------------------------------------------
# Submitting

def label_of(manifest):
    run = manifest['run']
    return run if run.startswith(LABEL_PREFIX) else LABEL_PREFIX + run


def geez_spent(client, since=None):
    """What every geez-* label has spent in the ledger (since an ISO time, if given)."""
    rows = [r for r in client.ledger_rows() if r['label'].startswith(LABEL_PREFIX)
            and (since is None or r['at'] >= since)]
    return round(sum(r['usd'] or 0 for r in rows), 4)


def submitted_ids(run):
    return {cid for s in read_jsonl(Path(run) / 'submissions.jsonl') for cid in s['custom_ids']}


def pending_batches(run):
    collected = {p.stem for p in (Path(run) / 'raw').glob('*.jsonl')}
    return [s for s in read_jsonl(Path(run) / 'submissions.jsonl') if s['batch'] not in collected]


def choose(run, requests, sample=None, take_next=None, ids=None, force=False):
    done = submitted_ids(run)
    retry = {r['custom_id'] for r in read_jsonl(Path(run) / 'rejects.jsonl')}
    if ids:
        chosen = [r for r in requests if r['custom_id'] in set(ids)]
    elif sample:
        step = max(1, len(requests) // sample)
        chosen = requests[::step][:sample]
    else:
        left = [r for r in requests if r['custom_id'] not in done or r['custom_id'] in retry]
        chosen = left[:take_next] if take_next else left
    again = [r['custom_id'] for r in chosen if r['custom_id'] in done and r['custom_id'] not in retry]
    if again and not force:
        raise SystemExit(f'{len(again)} of these requests were submitted already ({again[:3]}…) and are not '
                         'rejects: collect first, or pass --force')
    return chosen


def submit(run, client, budget, since, chosen, dry=False):
    """
    Sends `chosen` as one batch with cap_usd set so that every geez-* label together, counted from
    `since`, can never pass `budget`; the batch id is written before anything else can fail.
    """
    run = Path(run)
    manifest = read_json(run / 'manifest.json')
    pending = pending_batches(run)
    if pending:
        raise SystemExit(f'batch {pending[0]["batch"]} is not collected yet: collect before submitting again')
    label = label_of(manifest)
    spent_all = geez_spent(client, since)
    # Batches of the sibling geez-* runs not collected yet are not in the ledger: hold their reserve.
    in_flight = round(sum(s.get('reserve_usd', 0) for sibling in run.parent.glob(LABEL_PREFIX + '*')
                          if sibling != run for s in pending_batches(sibling)), 4)
    remaining = round(budget - spent_all - in_flight, 4)
    cap = round(client.spent(label) + remaining, 4)
    pairs = [(r['custom_id'], r['params']) for r in chosen]
    plan = client.batch_plan(pairs)
    info = {'label': label, 'requests': len(pairs), 'reserve_usd': plan['reserve_usd'], 'in_flight_usd': in_flight,
            'spent_geez_usd': spent_all, 'remaining_usd': remaining, 'cap_for_label_usd': cap}
    if dry or not pairs:
        return info
    batch_id = client.batch_submit(pairs, label=label, cap_usd=cap)
    append_jsonl(run / 'submissions.jsonl', {'at': now(), 'batch': batch_id, 'label': label,
                                             'manifest_sha256': manifest['requests_sha256'],
                                             'custom_ids': [cid for cid, _ in pairs], **info})
    info['batch'] = batch_id
    return info


# ---------------------------------------------------------------------------------------------------
# Validating

def _nonempty(value):
    return isinstance(value, str) and value.strip() != ''


def validate_dillmann(answer, items):
    """(clean rows by entry key, errors). Every entry and every sense answered exactly once."""
    errors, rows = [], {}
    if not isinstance(answer, dict) or not isinstance(answer.get('entries'), list):
        return {}, ['answer is not {"entries": [...]}']
    seen = {}
    for entry in answer['entries']:
        key = entry.get('id') if isinstance(entry, dict) else None
        if key not in items:
            errors.append(f'unknown entry id {key!r}')
            continue
        if key in seen:
            errors.append(f'entry {key} answered twice')
            continue
        seen[key] = entry
    for key in items:
        if key not in seen:
            errors.append(f'entry {key} not answered')
    for key, entry in seen.items():
        asked = items[key]['senses']
        got = [s.get('n') for s in entry.get('senses', []) if isinstance(s, dict)]
        if sorted(got) != sorted(asked) or len(set(got)) != len(got):
            errors.append(f'entry {key}: senses {got} != asked {asked}')
            continue
        free = set(items[key].get('free', []))
        langs = ('en', 'uk', 'de', 'es')
        bad = [s['n'] for s in entry['senses']
               if not all(_nonempty(s.get(lang)) for lang in langs)
               and not (s['n'] in free and not any(_nonempty(s.get(lang)) for lang in langs))]
        if bad:
            errors.append(f'entry {key}: empty rendering in senses {bad}')
            continue
        rows[key] = entry
    return rows, errors


def validate_gloss(answer, item, headwords=frozenset()):
    """(clean rows by word key, errors, warnings). Every word once; Greek keys from the packet only."""
    errors, warnings, rows = [], [], {}
    if not isinstance(answer, dict) or not isinstance(answer.get('words'), list):
        return {}, ['answer is not {"words": [...]}'], []
    words, greek = item['words'], item['greek']
    seen = {}
    for word in answer['words']:
        key = word.get('w') if isinstance(word, dict) else None
        if key not in words:
            errors.append(f'unknown word key {key!r}')
        elif key in seen:
            errors.append(f'word {key} answered twice')
        else:
            seen[key] = word
    missing = [k for k in words if k not in seen]
    if missing:
        errors.append(f'words not answered: {missing[:10]}')
    for key, word in seen.items():
        unknown = [g for g in word.get('greek', []) if g not in greek]
        if unknown:
            errors.append(f'word {key}: Greek keys not in the packet {unknown}')
        if len(set(word.get('greek', []))) != len(word.get('greek', [])):
            errors.append(f'word {key}: a Greek key twice')
        for field in ('lemma', 'sense_en', 'sense_uk', 'en', 'uk'):
            if not _nonempty(word.get(field)):
                errors.append(f'word {key}: empty {field}')
        if word.get('lemma_source') == 'dillmann' and headwords and nfc(word.get('lemma', '')) not in headwords:
            warnings.append(f'word {key}: lemma {word.get("lemma")} is not a Dillmann headword or form')
        rows[key] = word
    return (rows if not errors else {}), errors, warnings


def headword_set(run):
    listed = Path(run) / 'inputs' / 'dillmann-headwords.json'
    if listed.is_file():
        return frozenset(nfc(f) for f in read_json(listed))
    path = Path(run) / 'inputs' / 'dillmann.jsonl'
    return frozenset(nfc(f) for e in read_jsonl(path) for f in e['forms']) if path.is_file() else frozenset()


# ---------------------------------------------------------------------------------------------------
# Collecting

def download(run, client, submission, wait=False, poll=60):
    """raw/<batch>.jsonl, written once; the ledger gets one line per succeeded message then."""
    run = Path(run)
    raw = run / 'raw' / f"{submission['batch']}.jsonl"
    if raw.is_file():
        return read_jsonl(raw)
    api = client.client()
    batch = api.messages.batches.retrieve(submission['batch'])
    if batch.processing_status != 'ended':
        if not wait:
            counts = batch.request_counts
            print(f"{submission['batch']}: {batch.processing_status} (processing {counts.processing}, "
                  f"succeeded {counts.succeeded}, errored {counts.errored})")
            return None
        client.batch_wait(submission['batch'], poll=poll)
    rows = []
    for result in api.messages.batches.results(submission['batch']):
        row = {'custom_id': result.custom_id, 'type': result.result.type}
        if result.result.type == 'succeeded':
            message = result.result.message
            client.record(submission['label'], message.model, message.usage, batch=True,
                          request_id=result.custom_id, message_id=message.id, batch_id=submission['batch'],
                          stop_reason=message.stop_reason)
            row.update({'model': message.model, 'stop_reason': message.stop_reason,
                        'text': client.text_of(message), 'usage': client.usage_counts(message.usage),
                        'usd': client.cost(message.model, message.usage, batch=True)})
        else:
            error = getattr(result.result, 'error', None)
            row['error'] = str(getattr(error, 'type', '') or error or '')
        rows.append(row)
    write_jsonl(raw, rows)
    return rows


def judge(run, manifest, rows, headwords):
    """Turns raw rows into clean lines and rejects; answers of another model are rejected."""
    clean, rejects = [], []
    for row in rows:
        cid = row['custom_id']
        items = manifest['items'].get(cid)
        why = None
        if items is None:
            why = ['custom_id not in the manifest']
        elif row['type'] != 'succeeded':
            why = [f"batch result {row['type']} {row.get('error', '')}".strip()]
        elif row.get('model') != manifest['model']:
            why = [f"answered by {row.get('model')}, not {manifest['model']}"]
        elif row.get('stop_reason') != 'end_turn':
            why = [f"stop_reason {row.get('stop_reason')}"]
        if why is None:
            try:
                answer = json.loads(row['text'])
            except (TypeError, ValueError) as error:
                answer, why = None, [f'not JSON: {error}']
        if why is None and manifest['job'] == 'dillmann':
            good, errors = validate_dillmann(answer, items)
            if errors:
                why = errors
            else:
                for key, entry in good.items():
                    info = items[key]
                    clean.append({'custom_id': cid, 'entry': info['entry'], 'headword': info['headword'],
                                  'senses': entry['senses'], 'model': row['model'], 'run': manifest['run'],
                                  'prompt_version': manifest['prompt_version']})
        elif why is None:
            good, errors, warnings = validate_gloss(answer, items, headwords)
            if errors:
                why = errors
            else:
                for key, word in good.items():
                    clean.append({'custom_id': cid, 'word_id': items['words'][key], 'book': items['book'],
                                  'chapter': items['chapter'], 'lemma': word['lemma'],
                                  'lemma_source': word['lemma_source'], 'sense_en': word['sense_en'],
                                  'sense_uk': word['sense_uk'], 'en': word['en'], 'uk': word['uk'],
                                  'greek_word_ids': [items['greek'][g] for g in word['greek']],
                                  'greek_text': items['greek_text'], 'morph': word['morph'], 'conf': word['conf'],
                                  'warnings': [w for w in warnings if w.startswith(f'word {key}:')],
                                  'model': row['model'], 'run': manifest['run'],
                                  'prompt_version': manifest['prompt_version']})
        if why is not None:
            rejects.append({'custom_id': cid, 'why': why[:20]})
    return clean, rejects


def collect(run, client=None, wait=False, fake=None):
    """
    Downloads every uncollected batch (or reads --fake rows), validates all raw rows of the run and
    rewrites clean.jsonl and rejects.jsonl from them; a request answered well in any batch is clean.
    """
    run = Path(run)
    manifest = read_json(run / 'manifest.json')
    if fake:
        rows = read_jsonl(fake)
        write_jsonl(run / 'raw' / 'fake.jsonl', rows)
    else:
        for submission in pending_batches(run):
            download(run, client, submission, wait=wait)
    all_rows = [row for path in sorted((run / 'raw').glob('*.jsonl')) for row in read_jsonl(path)]
    # A later good answer supersedes an earlier failure of the same request.
    clean, rejects = judge(run, manifest, all_rows, headword_set(run))
    good = {c['custom_id'] for c in clean}
    seen_clean, final_clean = set(), []
    for line in clean:
        marker = (line['custom_id'], line.get('entry') or line.get('word_id'))
        if marker not in seen_clean:
            seen_clean.add(marker)
            final_clean.append(line)
    final_rejects = [r for r in rejects if r['custom_id'] not in good]
    write_jsonl(run / 'clean.jsonl', final_clean)
    write_jsonl(run / 'rejects.jsonl', final_rejects)
    report = {'run': manifest['run'], 'requests': manifest['requests'], 'answered_requests': len(good),
              'rejected_requests': len({r['custom_id'] for r in final_rejects}), 'clean_items': len(final_clean),
              'warnings': sum(len(c.get('warnings', [])) for c in final_clean),
              'submitted_requests': len(submitted_ids(run)),
              'usd': round(sum(r.get('usd') or 0 for r in all_rows), 4)}
    write_json(run / 'collect-report.json', report)
    return report


TAB, NEWLINE = chr(9), chr(10)


def compare(run, other, out):
    """
    One TSV line per Ge'ez word of two gloss runs over the same packets: the word, the read-time
    Dillmann match, each run's lemma, sense, line and Greek, and whether they agree. For reading.
    """
    def by_word(folder):
        return {row['word_id']: row for row in read_jsonl(Path(folder) / 'clean.jsonl')}

    mine, theirs = by_word(run), by_word(other)
    texts, dillmann, greek = {}, {}, {}
    for chapter in read_jsonl(Path(run) / 'inputs' / 'gloss-chapters.jsonl'):
        for word in chapter['geez']:
            texts[word['id']] = (chapter['book'], f"{word['c']}:{word['v']}{word['label']}", word['t'])
        greek.update({g['id']: g['t'] for g in chapter['greek']})
        dillmann.update({k: v['headword'] for k, v in chapter['dillmann'].items()})
    stats = {'words': len(texts), 'both': 0, 'lemma_agree': 0, 'greek_agree': 0, 'with_dillmann': 0,
             'a_lemma_is_dillmann_match': 0, 'b_lemma_is_dillmann_match': 0}
    lines = [TAB.join(['book', 'ref', 'geez', 'dillmann_match', 'a_lemma', 'b_lemma', 'a_sense', 'b_sense',
                         'a_line', 'b_line', 'a_greek', 'b_greek', 'a_conf', 'b_conf', 'lemma', 'greek'])]
    for word_id, (book, ref, text) in texts.items():
        a, b = mine.get(word_id, {}), theirs.get(word_id, {})
        ga = ' '.join(greek.get(g, '?') for g in a.get('greek_word_ids', []))
        gb_ = ' '.join(greek.get(g, '?') for g in b.get('greek_word_ids', []))
        same_lemma = bool(a and b) and nfc(a['lemma']) == nfc(b['lemma'])
        same_greek = bool(a and b) and a['greek_word_ids'] == b['greek_word_ids']
        if a and b:
            stats['both'] += 1
            stats['lemma_agree'] += same_lemma
            stats['greek_agree'] += same_greek
        if word_id in dillmann:
            stats['with_dillmann'] += 1
            stats['a_lemma_is_dillmann_match'] += bool(a) and nfc(a['lemma']) == nfc(dillmann[word_id])
            stats['b_lemma_is_dillmann_match'] += bool(b) and nfc(b['lemma']) == nfc(dillmann[word_id])
        lines.append(TAB.join([book, ref, text, dillmann.get(word_id, ''), a.get('lemma', ''), b.get('lemma', ''),
                                a.get('sense_en', ''), b.get('sense_en', ''), a.get('en', ''), b.get('en', ''), ga, gb_,
                                a.get('conf', ''), b.get('conf', ''), 'same' if same_lemma else 'DIFF',
                                'same' if same_greek else 'DIFF']))
    Path(out).write_text(NEWLINE.join(lines) + NEWLINE, encoding='utf-8')
    return stats


# ---------------------------------------------------------------------------------------------------

def default_run(name, repo=REPO):
    return Path(repo) / 'Resources' / 'Essenthos' / 'generation' / name


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['export-dillmann', 'export-gloss', 'build', 'estimate', 'submit',
                                            'collect', 'report', 'compare'])
    parser.add_argument('--run', required=True, help='run folder, or a bare name under Resources/Essenthos/generation')
    parser.add_argument('--repo', default=str(REPO), help='checkout whose claude_api, ledger and .env are used')
    parser.add_argument('--resources', help='Resources folder (default <repo>/Resources)')
    parser.add_argument('--api', default='http://localhost:5279', help='core API for the Dillmann matches')
    parser.add_argument('--chapters', default='GEN:1,PSA:22,JHN:1')
    parser.add_argument('--all-chapters', action='store_true', help='every GEEZ81 chapter in canonical order')
    parser.add_argument('--job', choices=['dillmann', 'gloss'])
    parser.add_argument('--model', choices=MODELS, default='claude-sonnet-5-5')
    parser.add_argument('--effort', choices=['low', 'medium', 'high', 'xhigh', 'max'], default='high')
    parser.add_argument('--ttl', choices=['5m', '1h'], default='5m')
    parser.add_argument('--from', dest='source', help='copy inputs/ from this run folder before building')
    parser.add_argument('--thinking', type=float, default=1.0, help='estimate: thinking tokens per visible token')
    parser.add_argument('--budget', type=float, help='submit: USD every geez-* label together may spend')
    parser.add_argument('--since', help='submit: count the budget from this ISO time (first Ge\'ez batch)')
    parser.add_argument('--sample', type=int, help='submit: N requests spread evenly over the run')
    parser.add_argument('--next', dest='take_next', type=int, help='submit: the next N unsent requests, in order')
    parser.add_argument('--ids', help='submit: comma-separated custom_ids')
    parser.add_argument('--dry', action='store_true', help='submit: print the plan, send nothing')
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--wait', action='store_true', help='collect: wait for the batches to end')
    parser.add_argument('--fake', help='collect: validate these hand-written raw rows instead of the API')
    parser.add_argument('--other', help='compare: the second gloss run (same packets)')
    parser.add_argument('--out', help='compare: the TSV to write')
    args = parser.parse_args(argv)

    run = Path(args.run) if ('/' in args.run or '\\' in args.run) else default_run(args.run, args.repo)
    resources = Path(args.resources) if args.resources else Path(args.repo) / 'Resources'
    if args.command == 'export-dillmann':
        print(json.dumps(export_dillmann(run, resources), indent=1))
    elif args.command == 'export-gloss':
        chapters = all_chapters() if args.all_chapters else parse_chapters(args.chapters)
        print(json.dumps(export_gloss(run, chapters, args.api), indent=1))
    elif args.command == 'build':
        if not args.job:
            raise SystemExit('build needs --job')
        manifest = build(run, args.job, claude_api(args.repo), args.model, args.effort, args.ttl, args.source)
        print(f"{run}: {manifest['requests']} requests, model {manifest['model']}, effort {manifest['effort']}")
    elif args.command == 'estimate':
        print(json.dumps(estimate(run, claude_api(args.repo), args.thinking), indent=1))
    elif args.command == 'submit':
        if args.budget is None or not args.since:
            raise SystemExit('submit needs --budget and --since (the owner\'s cap and when it started)')
        requests = read_jsonl(run / 'requests.jsonl')
        chosen = choose(run, requests, args.sample, args.take_next, args.ids.split(',') if args.ids else None,
                        args.force)
        print(json.dumps(submit(run, claude_api(args.repo), args.budget, args.since, chosen, args.dry), indent=1))
    elif args.command == 'collect':
        client = None if args.fake else claude_api(args.repo)
        print(json.dumps(collect(run, client, args.wait, args.fake), indent=1))
    elif args.command == 'compare':
        other = Path(args.other) if ('/' in args.other or chr(92) in args.other) else default_run(args.other, args.repo)
        print(json.dumps(compare(run, other, args.out or run / f'compare-{other.name}.tsv'), indent=1))
    elif args.command == 'report':
        client = claude_api(args.repo)
        print(client.format_summary(client.summary(label_of(read_json(run / 'manifest.json'))), run.name))
    return 0


if __name__ == '__main__':
    sys.exit(main())
