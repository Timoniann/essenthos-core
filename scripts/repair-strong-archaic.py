"""Read the archaic and false-friend King James words of the German and Ukrainian Strong lists on
pinned Codex, and stage narrow, reviewed repairs of the published files.

Modes:
  prepare  freeze the English source, the published files and the occurrence list into the run
  pilot    the same reading over inputs/pilot.json only, into pilot/
  read     ask Codex to locate and judge every occurrence in both languages (resumable)
  repair   ask Codex again for the items in repair-requests.json (reviewed feedback, resumable)
  stage    apply edits.json to copies of the published files, check them, write staged/ and a report
  status   counts and usage so far
"""
import argparse
import concurrent.futures
import datetime as dt
import hashlib
import importlib.util
import json
from collections import Counter
from pathlib import Path
import re

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
MAIN = REPO.parent.parent if REPO.parent.name == '.worktrees' else REPO
RUN = MAIN / 'Resources/Essenthos/generation/strong-archaic-repair-2026-10-21-v1'
SOURCE = MAIN / 'Resources/Essenthos/generation/strong-codex-sol61-2026-10-17-v3/de/full/source.json'
LEXICON = MAIN / 'Resources/Essenthos/lexicon'
FILES = {'de': 'strong-de.jsonl', 'uk': 'strong-uk.jsonl'}
CORPUS_LANGUAGE = {'de': 'deu', 'uk': 'ukr'}
MODEL = 'gpt-6.1-sol'
READ_VERSION = 'strong-archaic-read-1'
REPAIR_VERSION = 'strong-archaic-repair-1'
BATCH_CHARACTERS = 9000
BATCH_ENTRIES = 12
WORKERS = 4

VERDICTS = ['right', 'wrong', 'absent', 'unclear']
CHECK_PROPERTIES = {
    'word': {'type': 'string'},
    'english_item': {'type': 'string'},
    **{f'{lang}_{part}': spec for lang in ('de', 'uk') for part, spec in (
        ('rendering', {'type': 'string'}),
        ('verdict', {'type': 'string', 'enum': VERDICTS}),
        ('replacement', {'type': ['string', 'null']}))},
    'reason': {'type': 'string'},
}
READ_SCHEMA = {
    'type': 'object', 'additionalProperties': False, 'required': ['entries'],
    'properties': {'entries': {'type': 'array', 'items': {
        'type': 'object', 'additionalProperties': False, 'required': ['strong_number', 'checks'],
        'properties': {'strong_number': {'type': 'string'}, 'checks': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False, 'required': list(CHECK_PROPERTIES),
            'properties': CHECK_PROPERTIES}}}}}},
}
REPAIR_PROPERTIES = {
    'strong_number': {'type': 'string'}, 'language': {'type': 'string'},
    'old': {'type': 'string'}, 'new': {'type': 'string'}, 'reason': {'type': 'string'},
}
REPAIR_SCHEMA = {
    'type': 'object', 'additionalProperties': False, 'required': ['entries'],
    'properties': {'entries': {'type': 'array', 'items': {
        'type': 'object', 'additionalProperties': False, 'required': list(REPAIR_PROPERTIES),
        'properties': REPAIR_PROPERTIES}}},
}

READ_PROMPT = """\
You are checking translations of the King James rendering lists of Strong's Hebrew and Greek
dictionary, in German and in Ukrainian. A King James list is the English words the King James
Version of 1611 uses for one Hebrew or Greek word. A translator rendered each English word of it
into German and into Ukrainian. Many English words of 1611 have changed their meaning or are false
friends, and a translator who reads them in their modern sense writes a rendering the King James
never meant: 'let' that means hinder, 'allow' that means approve, 'nephew' that means grandson,
'meat' that means food, 'abroad' that means outside, 'meet' that means fitting.

For each entry you get the Strong number and lemma, Strong's definition and, where there is one,
the detailed definition (they say what the Hebrew or Greek word means, and so which sense the
English word has in this entry), the English list, the German list, the Ukrainian list, and the
checks: English items holding a word at risk, with the senses that word had in 1611.

Answer every check of every entry, and for each language separately:

  de_rendering / uk_rendering    copy, character for character, the part of that language's list
                                 that renders this English item: usually one comma item, sometimes
                                 two or part of one. An empty string if nothing in the list renders it.
  de_verdict / uk_verdict        right    the rendering carries the sense the King James word has in
                                          this entry, as the definition shows it
                                 wrong    it gives a modern or other sense the King James did not mean
                                          here
                                 absent   nothing in the list renders this item
                                 unclear  you cannot tell
  de_replacement / uk_replacement  only for wrong: the text to put in place of exactly the quoted
                                 rendering, in the form its neighbours in that list have (German and
                                 Ukrainian infinitives, nouns or adjectives as they stand there).
                                 Change only the wrong sense; keep anything else the quoted part
                                 carried. Otherwise null.
  reason                         one short sentence on the sense, with a verse where it helps.

Judge meaning only. Do not flag style, a choice between right synonyms, dropped notation ([idiom],
[phrase], X, +), or items you were not asked about. A word whose modern sense is also its sense in
this entry is right ('let' where the Hebrew means allow, 'suffer' where it means endure). Where the
entry's own definition shows the English item is a misprint in the source, say so in the reason and
judge against the word that was meant. Return word and english_item exactly as given.
"""

REPAIR_PROMPT = """\
You correct one item of a German or Ukrainian translation of a King James rendering list of
Strong's dictionary. A reviewer read the item against the English and the Hebrew or Greek meaning
and found it wrong; the feedback says how. You are given the entry's definition, the English list,
the translated list, the exact text to replace (old) and the feedback.

Return old exactly as given, and new: the text to put in place of old, in the form the neighbouring
items of that translated list have. Change only what the feedback asks; nothing else of the list is
touched by you, so do not return the list.
"""


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_suffix(path.suffix + '.tmp')
    pending.write_text(json.dumps(value, ensure_ascii=False, indent=1), encoding='utf-8')
    pending.replace(path)


def jsonl(path):
    rows = {}
    for line in Path(path).open(encoding='utf-8'):
        if line.strip():
            row = json.loads(line)
            rows[row['strong_number']] = row
    return rows


def adapter():
    spec = importlib.util.spec_from_file_location('codex_translation', RUN / 'code/lexicon-codex.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def frozen():
    record = read_json(RUN / 'frozen.json')
    for relative, expected in record['files'].items():
        if digest(RUN / relative) != expected:
            raise RuntimeError('Frozen input changed: ' + relative)
    for name, expected in record['published'].items():
        if digest(LEXICON / name) != expected:
            raise RuntimeError('The published file changed since the run was frozen: ' + name)
    return record


def prepare():
    if (RUN / 'frozen.json').exists():
        frozen()
        print('Frozen run already exists and matches; nothing replaced.')
        return
    occurrences = RUN / 'inputs/occurrences.json'
    if not occurrences.exists():
        raise RuntimeError('Write inputs/occurrences.json (review/risk.py) before preparing')
    (RUN / 'code').mkdir(exist_ok=True)
    for name in ('lexicon.py', 'lexicon-codex.py', 'repair-strong-archaic.py'):
        (RUN / 'code' / name).write_bytes((HERE / name).read_bytes())
    files = {str(path.relative_to(RUN)).replace('\\', '/'): digest(path)
             for folder in ('code', 'inputs') for path in (RUN / folder).rglob('*') if path.is_file()}
    write_json(RUN / 'frozen.json', {
        'model': MODEL, 'effort': 'medium', 'read_version': READ_VERSION,
        'source': str(SOURCE), 'source_sha256': digest(SOURCE),
        'published': {name: digest(LEXICON / name) for name in FILES.values()},
        'files': files, 'prepared_at': dt.datetime.now(dt.timezone.utc).isoformat()})
    print('Frozen', len(files), 'files and', len(FILES), 'published files')


def payload_of(occurrence):
    detailed = occurrence.get('detailed_definition') or ''
    return {
        'strong_number': occurrence['strong_number'], 'lemma': occurrence.get('lemma'),
        'definition': occurrence.get('definition'),
        'detailed_definition': detailed[:900] or None,
        'english_list': occurrence['kjv_en'], 'german_list': occurrence['kjv_de'],
        'ukrainian_list': occurrence['kjv_uk'],
        'checks': [{'word': m['word'], 'senses_in_1611': occurrence['risk'][m['word']],
                    'english_item': m['item']} for m in occurrence['matches']],
    }


def batches(occurrences):
    current, size, out = [], 0, []
    for occurrence in occurrences:
        payload = payload_of(occurrence)
        length = len(json.dumps(payload, ensure_ascii=False))
        if current and (size + length > BATCH_CHARACTERS or len(current) >= BATCH_ENTRIES):
            out.append(current)
            current, size = [], 0
        current.append(payload)
        size += length
    if current:
        out.append(current)
    return out


def invalid_read(asked, answered):
    problems = []
    by_number = {}
    for row in answered:
        by_number.setdefault(row.get('strong_number'), []).append(row)
    for entry in asked:
        rows = by_number.get(entry['strong_number']) or []
        if len(rows) != 1:
            problems.append(f"{entry['strong_number']}: answered {len(rows)} times")
            continue
        checks = rows[0]['checks']
        wanted = [(c['word'], c['english_item']) for c in entry['checks']]
        got = [(c.get('word'), c.get('english_item')) for c in checks]
        if Counter(wanted) != Counter(got):
            problems.append(f"{entry['strong_number']}: checks returned do not match those asked")
            continue
        for check in checks:
            for lang, listed in (('de', entry['german_list']), ('uk', entry['ukrainian_list'])):
                rendering = check[f'{lang}_rendering']
                if rendering and rendering not in (listed or ''):
                    problems.append(f"{entry['strong_number']} {lang}: rendering not in the list: {rendering}")
                if check[f'{lang}_verdict'] == 'wrong' and not (check[f'{lang}_replacement'] or '').strip():
                    problems.append(f"{entry['strong_number']} {lang}: wrong without a replacement")
    extra = set(by_number) - {entry['strong_number'] for entry in asked}
    if extra:
        problems.append('answered numbers not asked: ' + ', '.join(sorted(extra)))
    return problems


def ask(codex, phase, name, payload, system, schema, version, say=print):
    folder = RUN / 'calls' / name
    codex.JUDGE_SCHEMA = schema
    outcome, error = codex.lexicon.call_with_retries(
        json.dumps(payload, ensure_ascii=False), system, MODEL, say, effort='medium',
        record_dir=str(folder),
        provenance={'phase': 'judge', 'step': phase, 'batch': name, 'prompt_version': version,
                    'run': dt.datetime.now(dt.timezone.utc).isoformat()})
    if error:
        return None, error
    return json.loads(outcome['result']), None


def read(target='read'):
    frozen()
    codex = adapter()
    data = read_json(RUN / 'inputs/occurrences.json')
    occurrences = [{**o, 'risk': data['risk']} for o in data['occurrences']]
    if target == 'pilot':
        chosen = set(read_json(RUN / 'inputs/pilot.json'))
        occurrences = [o for o in occurrences if o['strong_number'] in chosen]
    answers_path = RUN / target / 'answers.jsonl'
    answers_path.parent.mkdir(exist_ok=True)
    done = set()
    if answers_path.exists():
        for line in answers_path.open(encoding='utf-8'):
            if line.strip():
                done.add(json.loads(line)['batch'])
    work = [(f'{target}-{index:03d}', batch) for index, batch in enumerate(batches(occurrences))]
    pending = [(name, batch) for name, batch in work if name not in done]
    print(len(work), 'batches,', len(pending), 'to ask', flush=True)

    def one(item):
        name, batch = item
        for attempt in (1, 2):
            answered, error = ask(codex, 'read', f'{name}-a{attempt}', {'entries': batch},
                                  READ_PROMPT, READ_SCHEMA, READ_VERSION)
            if error:
                raise RuntimeError(f'{name}: {error}')
            problems = invalid_read(batch, answered)
            if not problems:
                return name, answered, attempt
            print(f'{name} attempt {attempt}: ' + '; '.join(problems[:6]), flush=True)
        raise RuntimeError(f'{name}: invalid twice: ' + '; '.join(problems[:6]))

    errors = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as pool:
        futures = [pool.submit(one, item) for item in pending]
        for future in concurrent.futures.as_completed(futures):
            try:
                name, answered, attempt = future.result()
            except Exception as error:
                errors.append(str(error))
                print(error, flush=True)
                continue
            with answers_path.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps({'batch': name, 'attempt': attempt, 'model': MODEL,
                                         'prompt_version': READ_VERSION, 'entries': answered},
                                        ensure_ascii=False) + '\n')
            print(name, 'read', flush=True)
    status()
    return 1 if errors else 0


def repair():
    frozen()
    codex = adapter()
    requests = read_json(RUN / 'repair-requests.json')
    answers_path = RUN / 'repair/answers.jsonl'
    answers_path.parent.mkdir(exist_ok=True)
    done = set()
    if answers_path.exists():
        for line in answers_path.open(encoding='utf-8'):
            if line.strip():
                done.add(json.loads(line)['request'])
    src = {row['strong_number']: row for row in read_json(SOURCE)}
    published = {lang: jsonl(LEXICON / name) for lang, name in FILES.items()}
    pending = [r for r in requests if r['id'] not in done]
    print(len(requests), 'requests,', len(pending), 'to ask', flush=True)

    def one(request):
        number, lang, field = request['strong_number'], request['language'], request.get('field', 'kjv_definition')
        translated = published[lang][number][field]
        splice(translated, request['old'], request['old'], request['id'])
        payload = {'entries': [{
            'strong_number': number, 'language': {'de': 'German', 'uk': 'Ukrainian'}[lang],
            'definition': src[number].get('definition'),
            'detailed_definition': (src[number].get('detailed_definition') or '')[:900] or None,
            'english_field': src[number].get(field), 'translated_field': translated,
            'old': request['old'], 'feedback': request['feedback']}]}
        for attempt in (1, 2):
            answered, error = ask(codex, 'repair', f"repair-{request['id']}-a{attempt}", payload,
                                  REPAIR_PROMPT, REPAIR_SCHEMA, REPAIR_VERSION)
            if error:
                raise RuntimeError(f"{request['id']}: {error}")
            if (len(answered) == 1 and answered[0]['old'] == request['old']
                    and answered[0]['strong_number'] == number and answered[0]['new'].strip()):
                return request, answered[0], attempt
        raise RuntimeError(f"{request['id']}: no valid answer twice")

    errors = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as pool:
        futures = [pool.submit(one, request) for request in pending]
        for future in concurrent.futures.as_completed(futures):
            try:
                request, answer, attempt = future.result()
            except Exception as error:
                errors.append(str(error))
                print(error, flush=True)
                continue
            with answers_path.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps({'request': request['id'], 'attempt': attempt, 'model': MODEL,
                                         'prompt_version': REPAIR_VERSION, 'asked': request,
                                         'answered_at': dt.datetime.now(dt.timezone.utc).isoformat(),
                                         'answer': answer}, ensure_ascii=False) + '\n')
            print(request['id'], '->', answer['new'], flush=True)
    status()
    return 1 if errors else 0


SENSE_LINE = re.compile(r'^\s*(\d+[a-z]?)\)', re.M)
STRONG_REFERENCE = re.compile(r'\b[GH]\d{1,4}\b')


def loader_refusal(english, row):
    """The corpus loader's own refusals (StrongTranslationLoader.Kept), simulated on one row."""
    for field in ('definition', 'derivation', 'kjv_definition', 'detailed_definition'):
        wanted, got = (english.get(field) or '').strip(), (row.get(field) or '').strip()
        if wanted and got and set(STRONG_REFERENCE.findall(wanted)) - set(STRONG_REFERENCE.findall(got)):
            return 'lost-reference in ' + field
    wanted, got = english.get('detailed_definition') or '', row.get('detailed_definition') or ''
    if wanted.strip() and got.strip() and SENSE_LINE.findall(wanted) != SENSE_LINE.findall(got):
        return 'lost-sense'
    return None


def splice(text, old, new, where):
    """Replace old once: as it stands when it occurs once, else as a whole comma item."""
    if text.count(old) == 1:
        return text.replace(old, new)
    item = re.compile(r'(?:(?<=^)|(?<=, ))' + re.escape(old) + r'(?=,|\.?$)')
    found = item.findall(text)
    if len(found) != 1:
        raise RuntimeError(f'{where}: old occurs {text.count(old)} times, {len(found)} as an item: {old}')
    return item.sub(lambda _: new, text, count=1)


def stage():
    record = frozen()
    codex = adapter()
    edits = read_json(RUN / 'edits.json')
    src = {row['strong_number']: row for row in read_json(SOURCE)}
    stamp = edits['repaired_at']
    report = {'repaired_at': stamp, 'languages': {}}
    for lang, name in FILES.items():
        lines = (LEXICON / name).read_text(encoding='utf-8').splitlines(keepends=True)
        rows = [json.loads(line) for line in lines]
        index = {row['strong_number']: position for position, row in enumerate(rows)}
        changed = {}
        for edit in [e for e in edits['edits'] if e['language'] == lang]:
            number, field = edit['strong_number'], edit['field']
            row = changed.get(number) or json.loads(lines[index[number]])
            text = row[field]
            if 'old' in edit:
                row[field] = splice(text, edit['old'], edit['new'], f'{lang} {number} {field}')
            else:
                if text != edit['expected']:
                    raise RuntimeError(f'{lang} {number} {field}: the field is not the reviewed text')
                row[field] = edit['value']
            repaired = row.setdefault('repaired', {
                'original': {key: row[key] for key in ('model', 'prompt_version', 'translated_at')},
                'run': str(RUN.relative_to(MAIN)).replace('\\', '/'), 'edits': []})
            repaired['edits'].append({key: value for key, value in edit.items() if key != 'language'})
            changed[number] = row
        for number, row in changed.items():
            original = row['repaired']['original']
            by_model = any(e['by'] == 'codex' for e in row['repaired']['edits'])
            row['model'] = original['model'] + (' + ' + MODEL if by_model and original['model'] != MODEL else '')
            row['prompt_version'] = original['prompt_version'] + ' + ' + REPAIR_VERSION
            row['translated_at'] = stamp
            added = [term for e in row['repaired']['edits'] for term in e.get('uncertain', [])]
            if added:
                row['uncertain'] = list(dict.fromkeys([*(row.get('uncertain') or []), *added]))
            failures = codex.lexicon.failures_of(src[number], row, lang)
            refusal = loader_refusal(src[number], row)
            before = json.loads(lines[index[number]])
            new_failures = [f for f in failures if f not in codex.lexicon.failures_of(src[number], before, lang)]
            if new_failures or refusal:
                raise RuntimeError(f'{lang} {number}: the repaired row fails: {new_failures or refusal}')
            lines[index[number]] = json.dumps(row, ensure_ascii=False) + '\n'
        out = RUN / 'staged' / name
        out.parent.mkdir(exist_ok=True)
        out.write_text(''.join(lines), encoding='utf-8')
        untouched = [i for i, (a, b) in enumerate(zip(
            (LEXICON / name).read_text(encoding='utf-8').splitlines(keepends=True), lines)) if a != b]
        if sorted(rows[i]['strong_number'] for i in untouched) != sorted(changed):
            raise RuntimeError(f'{lang}: lines changed outside the repaired entries')
        report['languages'][lang] = {
            'file': str(out.relative_to(RUN)).replace('\\', '/'), 'sha256': digest(out),
            'published_sha256': record['published'][name], 'lines': len(lines),
            'entries_changed': len(changed), 'numbers': sorted(changed, key=lambda n: (n[0], int(n[1:]))),
            'edits': sum(len(row['repaired']['edits']) for row in changed.values())}
    write_json(RUN / 'staged/report.json', report)
    print(json.dumps(report['languages'], ensure_ascii=False)[:2000])
    return 0


def usage_of(folder):
    usage, records, failed = Counter(), 0, 0
    for path in folder.glob('*/*.json'):
        record = read_json(path)
        records += 1
        if record.get('error') or record.get('returncode'):
            failed += 1
        for key, value in ((record.get('outcome') or {}).get('usage') or {}).items():
            if isinstance(value, int):
                usage[key] += value
    return {'call_records': records, 'failed_records': failed, 'usage': dict(usage),
            'total_cost_usd': None}


def status():
    result = {'calls': usage_of(RUN / 'calls')}
    for step in ('pilot', 'read', 'repair'):
        path = RUN / step / 'answers.jsonl'
        result[step] = sum(1 for line in path.open(encoding='utf-8') if line.strip()) if path.exists() else 0
    write_json(RUN / 'status.json', {**result, 'updated_at': dt.datetime.now(dt.timezone.utc).isoformat()})
    print(json.dumps(result, ensure_ascii=False))
    return 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mode', required=True, choices=['prepare', 'pilot', 'read', 'repair', 'stage', 'status'])
    args = parser.parse_args()
    if args.mode == 'prepare':
        prepare()
        return
    if args.mode == 'pilot':
        raise SystemExit(read('pilot'))
    raise SystemExit({'read': read, 'repair': repair, 'stage': stage, 'status': status}[args.mode]())


if __name__ == '__main__':
    main()
