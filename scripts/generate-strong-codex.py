"""Prepare and operate isolated German/Spanish Codex lexicon runs."""

import argparse
import concurrent.futures
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import threading
from types import SimpleNamespace


HERE = Path(__file__).resolve().parent
REPO = HERE.parent
MAIN = REPO.parent.parent if REPO.parent.name == '.worktrees' else REPO
CONTROL = MAIN.parent
ROOT = MAIN / 'Resources/Essenthos/generation/strong-codex-sol61-2026-10-17-v3'
SONNET = MAIN / 'Resources/Essenthos/generation/strong-de-2026-10-17-v3'
EXPECTED = 14197
MODEL = 'gpt-6.1-sol'
CODE = ('lexicon.py', 'lexicon-codex.py', 'generate-strong-codex.py')


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_suffix(path.suffix + '.tmp')
    pending.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    pending.replace(path)


def module_at(folder):
    spec = importlib.util.spec_from_file_location('codex_translation', folder / 'lexicon-codex.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def state(run, stage, **values):
    record = {'stage': stage, 'updated_at': dt.datetime.now(dt.timezone.utc).isoformat(),
              'model': MODEL, **values}
    write_json(run / 'status.json', record)
    print(json.dumps(record, ensure_ascii=False), flush=True)


def invoke(command, env=None):
    return subprocess.run(command, cwd=CONTROL, env=env).returncode


def avioniq(*args):
    executable = shutil.which('avioniq')
    if not executable:
        raise RuntimeError('avioniq is unavailable')
    return invoke([executable, *args])


def step(run, verb, folder, *options):
    env = {**os.environ, 'ESSENTHOS_LEXICON_STOP_FILE': str(run / 'stop-requested.json')}
    return invoke([sys.executable, '-u', str(run / 'code/lexicon-codex.py'), verb,
                   '--dir', str(folder), *options], env=env)


def frozen(run):
    record = read_json(run / 'frozen.json')
    if record['model'] != MODEL:
        raise RuntimeError('The frozen run selected a different model')
    for relative, expected in record['files'].items():
        if digest(run / relative) != expected:
            raise RuntimeError('Frozen input changed: ' + relative)


def prepare(run, language):
    if (run / 'frozen.json').exists():
        frozen(run)
        print('Frozen preparation already exists and matches; nothing replaced.')
        return
    if run.exists():
        raise RuntimeError('An incomplete preparation exists; inspect it before replacing anything.')
    source_path = SONNET / 'full/source.json'
    source = read_json(source_path)
    if len(source) != EXPECTED or len({r['strong_number'] for r in source}) != EXPECTED:
        raise RuntimeError('The frozen source inventory is incomplete or duplicated')
    module = module_at(HERE).lexicon
    if not module.GLOSSARY.get(language):
        raise RuntimeError('A target-language glossary is required')
    (run / 'code').mkdir(parents=True)
    for filename in CODE:
        shutil.copy2(HERE / filename, run / 'code' / filename)
    pilot = read_json(SONNET / 'pilot/source.json')
    selections = {'full': source, 'pilot': pilot,
                  'smoke': [r for r in source if r['strong_number'] in {'G26', 'H1'}]}
    for name, entries in selections.items():
        module.entries = lambda numbers=None, entries=entries: entries
        module.extract(SimpleNamespace(language=language, numbers=None, sample=None,
                                       seed=1050, dir=str(run / name)))
    files = {str(path.relative_to(run)).replace('\\', '/'): digest(path)
             for path in run.rglob('*') if path.is_file()}
    write_json(run / 'frozen.json', {'model': MODEL, 'language': language,
               'source_sha256': digest(source_path), 'files': files})
    state(run, 'prepared', language=language, requested=EXPECTED, pilot_entries=len(pilot))


def report(run, folder):
    module = module_at(run / 'code').lexicon
    rows, failures, requested = module.check_run(str(folder))
    summaries = [read_json(path) for path in (folder / 'calls').glob('*-summary.json')]
    usage = {}
    for summary in summaries:
        for key, value in summary.get('usage', {}).items():
            usage[key] = usage.get(key, 0) + value
    result = {'requested': len(requested), 'returned': len(rows),
              'structural_failures': len(failures), 'calls': sum(s['calls'] for s in summaries),
              'elapsed_seconds': sum(s['elapsed_seconds'] for s in summaries),
              'usage': usage, 'total_cost_usd': None,
              'answers_sha256': digest(folder / 'answers.jsonl') if rows else None}
    write_json(folder / 'report.json', result)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    return result


def generate(run, language, target, workers):
    frozen(run)
    folder = run / target
    if target == 'full':
        if (run / 'stop-requested.json').exists():
            raise RuntimeError('A previous stop request needs review before resuming')
        gate = read_json(run / 'generation-readiness.json')
        if not gate.get('proceed') or gate.get('model') != MODEL or gate.get('language') != language:
            raise RuntimeError('The target language needs recorded pilot readiness first')
        if gate.get('answers_sha256') != digest(run / 'pilot/answers.jsonl'):
            raise RuntimeError('Pilot answers changed after readiness review')
        module = module_at(run / 'code').lexicon
        pilot_rows, pilot_failures, requested = module.check_run(str(run / 'pilot'))
        if pilot_failures or len(pilot_rows) != 48 or len(requested) != 48:
            raise RuntimeError('Pilot completeness and structure must pass before a full run')
    state(run, target + '-running', language=language, workers=workers, effort='medium')
    result = step(run, 'ask', folder, '--model', MODEL, '--effort', 'medium',
                  '--workers', str(workers))
    check = step(run, 'check', folder)
    measured = report(run, folder)
    complete = not result and not check and not measured['structural_failures']
    state(run, target + ('-awaiting-review' if complete else '-needs-repair'),
          language=language, ask_exit=result, check_exit=check, **measured)
    return 0 if complete else 1


def maintain_claim(stopped, language):
    while not stopped.wait(1800):
        result = avioniq('claim', 'strong-codex-' + language, '--agent', 'codex', '--ttl', '2h',
                         '--note', 'Codex lexicon generation is running')
        if result:
            run = ROOT / language
            write_json(run / 'stop-requested.json', {'reason': 'claim renewal refused'})
            print('Claim renewal failed; unsent model batches will checkpoint.', flush=True)
            stopped.set()
            return
        avioniq('services', 'run', 'backup')


def repair(run, language, target='pilot'):
    frozen(run)
    adapter = module_at(run / 'code')
    module = adapter.lexicon
    folder = run / target
    sources = {row['strong_number']: row for row in read_json(folder / 'source.json')}
    answers = {row['strong_number']: row for row in module.read(str(folder / 'answers.jsonl'))}
    feedback = read_json(run / (target + '-feedback.json'))
    manifest = read_json(folder / 'manifest.json')
    captured = {}
    original = module.run_batches
    module.run_batches = lambda args, path, system_of, *rest: captured.update(system=system_of(manifest))
    try:
        module.ask(SimpleNamespace(dir=str(folder)))
    finally:
        module.run_batches = original
    def one(item):
        number, detail = item
        payload = {'entries': [module.asking(sources[number])], 'language': manifest['language_name'],
                   'previous_answer': answers.get(number), 'review_feedback': detail,
                   'instruction': 'Return the complete corrected entry; preserve all other source senses.'}
        date = dt.datetime.now(dt.timezone.utc).isoformat()
        version = manifest['prompt_version'] + '-review-1'
        invocation = 'review-' + number + '-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S%f')
        records_path = folder / 'calls' / invocation
        try:
            outcome, error = module.call_with_retries(
                json.dumps(payload, ensure_ascii=False), captured['system'], MODEL, print,
                effort='medium', record_dir=str(records_path),
                provenance={'phase': 'repair', 'batch': 'review-' + number,
                            'prompt_version': version, 'run': date})
        finally:
            records = [read_json(path) for path in records_path.glob('*.json')]
            usage = {}
            for record in records:
                for key, value in ((record.get('outcome') or {}).get('usage') or {}).items():
                    if isinstance(value, (int, float)):
                        usage[key] = usage.get(key, 0) + value
            write_json(folder / 'calls' / (invocation + '-summary.json'), {
                'invocation': invocation, 'phase': 'repair', 'calls': len(records),
                'usage': usage, 'total_cost_usd': None,
                'elapsed_seconds': sum(record['elapsed_seconds'] for record in records)})
        if error:
            raise RuntimeError(number + ': ' + error)
        translated = module.parse(outcome.get('result')) or []
        if len(translated) != 1 or module.failures_of(sources[number], translated[0], language):
            raise RuntimeError(number + ': repaired entry did not pass structural checks')
        row = {field: translated[0][field] for field in module.PROSE if sources[number].get(field)}
        uncertain = translated[0].get('uncertain') or []
        if isinstance(detail, dict) and 'fields' in detail:
            fields = set(detail['fields'])
            if not fields or fields - set(module.PROSE) or number not in answers:
                raise RuntimeError(number + ': invalid scoped correction fields')
            for field in row:
                if field not in fields:
                    row[field] = answers[number].get(field)
            if module.failures_of(sources[number], row | {'strong_number': number}, language):
                raise RuntimeError(number + ': scoped correction failed structural validation')
            uncertain = list(dict.fromkeys([*(answers[number].get('uncertain') or []), *uncertain]))
        row.update(strong_number=number, language=manifest['language_name'], method='model-translation',
                   model=MODEL, prompt_version=version, run=date, batch='review-' + number,
                   effort='medium', uncertain=uncertain,
                   review_feedback=detail)
        return row

    errors = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        pending = {pool.submit(one, item): item[0] for item in feedback.items()}
        for future in concurrent.futures.as_completed(pending):
            try:
                row = future.result()
            except Exception as error:
                errors.append(pending[future] + ': ' + str(error))
                continue
            with (folder / 'answers.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(row, ensure_ascii=False) + '\n')
            print(row['strong_number'] + ': reviewed correction retained', flush=True)
    result = step(run, 'check', folder)
    measured = report(run, folder)
    state(run, target + ('-awaiting-review' if not result and not errors else '-needs-repair'),
          language=language, repair_errors=errors, **measured)
    return 1 if result or errors else 0


def publish(run, language):
    frozen(run)
    gate = read_json(run / 'delivery-review.json')
    if not gate.get('proceed') or gate.get('answers_sha256') != digest(run / 'full/answers.jsonl'):
        raise RuntimeError('The full translation needs a current semantic delivery review')
    return step(run, 'publish', run / 'full', '--out', str(run / 'checked' / f'strong-{language}.jsonl'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--language', choices=['de', 'es'], required=True)
    parser.add_argument('--mode', choices=['prepare', 'smoke', 'pilot', 'repair', 'full', 'check-pilot',
                                         'check-full', 'status', 'publish'], required=True)
    parser.add_argument('--workers', type=int, choices=range(1, 9), default=2)
    args = parser.parse_args()
    run = ROOT / args.language
    if args.mode == 'prepare':
        prepare(run, args.language)
        return
    if args.mode == 'status':
        print(json.dumps(read_json(run / 'status.json'), ensure_ascii=False))
        module = module_at(run / 'code').lexicon
        for target in ('smoke', 'pilot', 'full'):
            print(f'{target}: {len(module.read(str(run / target / "answers.jsonl")))} returned')
        return
    if args.mode == 'repair':
        try:
            result = repair(run, args.language)
        except Exception as error:
            state(run, 'pilot-failed', language=args.language, reason=str(error))
            raise
        raise SystemExit(result)
    if args.mode in ('smoke', 'pilot'):
        try:
            result = generate(run, args.language, args.mode, args.workers)
        except Exception as error:
            state(run, args.mode + '-failed', language=args.language, reason=str(error))
            raise
        raise SystemExit(result)
    if args.mode.startswith('check-'):
        frozen(run)
        raise SystemExit(step(run, 'check', run / args.mode.removeprefix('check-')))
    if args.mode == 'publish':
        raise SystemExit(publish(run, args.language))
    stopped = threading.Event()
    if avioniq('claim', 'strong-codex-' + args.language, '--agent', 'codex', '--ttl', '2h',
                '--note', 'Codex lexicon generation starting'):
        raise RuntimeError('The translation lease was refused; no model calls were started')
    threading.Thread(target=maintain_claim, args=(stopped, args.language), daemon=True).start()
    try:
        try:
            result = generate(run, args.language, 'full', args.workers)
        except Exception as error:
            state(run, 'full-failed', language=args.language, reason=str(error))
            raise
    finally:
        stopped.set()
        avioniq('services', 'run', 'backup')
        avioniq('release', 'strong-codex-' + args.language, '--agent', 'codex')
    raise SystemExit(result)


if __name__ == '__main__':
    main()
