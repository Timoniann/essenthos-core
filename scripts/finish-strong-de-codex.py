"""Retain German candidates and finish only missing or reviewed entries on Codex."""
import argparse
from collections import Counter
import datetime as dt
import importlib.util
import json
import hashlib
from pathlib import Path
import re
import shutil
from types import SimpleNamespace

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('controller', HERE / 'generate-strong-codex.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
RUN = base.MAIN / 'Resources/Essenthos/generation/strong-de-finish-codex-2026-10-19-v4'
PREVIOUS = RUN.with_name('strong-de-finish-codex-2026-10-18-v3')
ORIGIN = base.SONNET
PILOT = base.ROOT / 'de'


def exact_entry_fragments(text, expected, prose):
    def unique_object(pairs):
        value = dict(pairs)
        if len(value) != len(pairs):
            raise ValueError('Duplicate keys in raw object')
        return value
    decoder = json.JSONDecoder(object_pairs_hook=unique_object)
    fields = {'strong_number', 'uncertain', *prose}
    candidates = []
    for match in re.finditer(r'[\[,](\s*\{)', text):
        start = match.start(1) + len(match.group(1)) - 1
        try:
            answer, end = decoder.raw_decode(text, start)
        except ValueError:
            continue
        if not isinstance(answer, dict) or not isinstance(answer.get('strong_number'), str):
            continue
        if set(answer) - fields or not any(field in answer for field in prose):
            continue
        following = text[end:].lstrip()
        if not following or following[0] not in ',]':
            continue
        candidates.append({'answer': answer, 'start': start, 'end': end,
                           'sha256': hashlib.sha256(text[start:end].encode('utf-8')).hexdigest()})
    duplicates = Counter(item['answer']['strong_number'] for item in candidates)
    declared = Counter(re.findall(r'(?<!\\)"strong_number"\s*:\s*"([^"\r\n]+)"', text))
    return [item for item in candidates if item['answer']['strong_number'] in expected
            and duplicates[item['answer']['strong_number']] == 1
            and declared[item['answer']['strong_number']] == 1]


def prepare():
    if RUN.exists():
        base.frozen(RUN)
        print('Frozen finish run already exists; no retained answers replaced.')
        return
    base.CODE = (*base.CODE, Path(__file__).name)
    base.prepare(RUN, 'de')
    retained = {}
    for source, dest in ((PREVIOUS / 'full/answers.jsonl', RUN / 'full/answers.jsonl'),
                         (PILOT / 'pilot/answers.jsonl', RUN / 'pilot/answers.jsonl')):
        shutil.copy2(source, dest)
        retained[str(source)] = base.digest(source)
    shutil.copytree(PREVIOUS / 'full/calls', RUN / 'full/calls')
    gate = base.read_json(PILOT / 'generation-readiness.json')
    if gate['answers_sha256'] != base.digest(RUN / 'pilot/answers.jsonl'):
        raise RuntimeError('The reviewed Codex pilot hash does not match')
    base.write_json(RUN / 'generation-readiness.json', gate)
    base.write_json(RUN / 'retained-provenance.json', {
        'files': retained, 'original_calls': str(ORIGIN / 'full/calls'),
        'previous_codex_run': str(PREVIOUS),
        'note': 'Retained v2 rows and raw Codex calls with original provenance, including reviewed corrections; new calls remain pinned Codex.'})
    print('Retained original German rows and reviewed Codex pilot; no model call made.')


def recover():
    base.frozen(RUN)
    module = base.module_at(RUN / 'code').lexicon
    folder = RUN / 'full'
    original_answers_sha = base.digest(folder / 'answers.jsonl')
    sources = {row['strong_number']: row for row in base.read_json(folder / 'source.json')}
    stored = {row['strong_number'] for row in module.read(str(folder / 'answers.jsonl'))}
    original_manifest = base.read_json(ORIGIN / 'full/manifest.json')
    recovered, rejected = [], []
    paths = [path for root in (PREVIOUS, ORIGIN)
             for path in sorted((root / 'full/calls').glob('*/*.json'))]
    for path in paths:
        record = base.read_json(path)
        outcome = record.get('outcome')
        if (not isinstance(outcome, dict) or record.get('returncode') != 0
                or record.get('error') or outcome.get('is_error')):
            continue
        provenance = record.get('provenance') or {}
        if provenance.get('phase') != 'ask':
            continue
        batch = provenance.get('batch')
        payload_path = ORIGIN / 'full/batches' / (str(batch) + '.json')
        if not payload_path.exists():
            raise RuntimeError('Raw record has no source batch: ' + str(path))
        payload = base.read_json(payload_path)
        expected = {row['strong_number'] for row in payload['entries']}
        if record.get('prompt'):
            asked = json.loads(record['prompt'])['entries']
            expected &= {row['strong_number'] for row in asked}
            for entry in asked:
                number = entry['strong_number']
                if number not in sources or any(entry.get(field) != sources[number].get(field)
                        for field in module.PROSE if sources[number].get(field)):
                    raise RuntimeError('Raw prompt does not match the frozen source: ' + str(path))
        text = outcome.get('result') or ''
        answers = module.parse(text) or []
        fragments = {}
        if not answers:
            for item in exact_entry_fragments(text, expected, module.PROSE):
                fragments[item['answer']['strong_number']] = {key: value for key, value in item.items()
                                                            if key != 'answer'}
                answers.append(item['answer'])
        duplicates = Counter(row.get('strong_number') for row in answers if isinstance(row, dict))
        for answer in answers:
            if not isinstance(answer, dict):
                continue
            number = answer.get('strong_number')
            if number in stored or number not in sources or number not in expected or duplicates[number] != 1:
                continue
            failures = module.failures_of(sources[number], answer, 'de')
            if failures:
                rejected.append({'strong_number': number, 'raw': str(path), 'failures': failures})
                continue
            model = next(iter(outcome.get('modelUsage') or {}), None)
            if not model or not provenance.get('run'):
                raise RuntimeError('Cannot recover uncredited model result')
            row = {field: answer[field] for field in module.PROSE if sources[number].get(field)}
            row.update(strong_number=number, language='German', method='model-translation', model=model,
                       prompt_version=provenance.get('prompt_version') or original_manifest['prompt_version'],
                       run=provenance['run'], batch=batch, effort=record.get('effort'),
                       uncertain=answer.get('uncertain') or [], recovered_from=str(path),
                       recovered_raw_sha256=base.digest(path))
            if number in fragments:
                row['recovered_raw_fragment'] = fragments[number]
            recovered.append(row)
            stored.add(number)
    if base.digest(folder / 'answers.jsonl') != original_answers_sha:
        raise RuntimeError('Answers changed during recovery; wait for the writer to finish and retry')
    with (folder / 'answers.jsonl').open('a', encoding='utf-8') as stream:
        for row in recovered:
            stream.write(json.dumps(row, ensure_ascii=False) + '\n')
    base.write_json(RUN / 'recovery-report.json', {
        'recovered': len(recovered), 'rejected': rejected,
        'initial_answers_sha256': original_answers_sha,
        'final_answers_sha256': base.digest(folder / 'answers.jsonl'),
        'validator_sha256': base.digest(RUN / 'code/lexicon.py')})
    base.report(RUN, folder)
    print('Recovered valid raw rows:', len(recovered))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mode', required=True, choices=['prepare', 'recover', 'fill', 'repair', 'check', 'publish'])
    parser.add_argument('--workers', type=int, default=6, choices=range(1, 9))
    args = parser.parse_args()
    if args.mode == 'prepare':
        prepare()
        return
    base.frozen(RUN)
    if args.mode == 'recover':
        recover()
        return
    if args.mode == 'fill':
        code = base.generate(RUN, 'de', 'full', args.workers)
    elif args.mode == 'repair':
        code = base.repair(RUN, 'de', target='full')
    elif args.mode == 'check':
        result = base.report(RUN, RUN / 'full')
        code = int(result['structural_failures'] != 0)
    else:
        code = base.publish(RUN, 'de')
    raise SystemExit(code)


if __name__ == '__main__':
    main()
