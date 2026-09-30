"""
Asking a model in batches, and reading back what it answered.

The harnesses that put a question to a model ask it many items at a time, and each needs the same
three things: one call that survives a harness that hangs or dies, a run over the batches that
writes each batch's answers the moment they arrive, and a way to read every answer of a run back.

    import batches
    batches.run_batches(directory, 'out', jobs, SYSTEM, PROMPT_VERSION, args, shape)
    rows = batches.collect(directory, 'out')
"""

import concurrent.futures
import datetime
import json
import os
import re
import subprocess
import threading
import time

import descriptors as shared


def call(prompt, model, effort, system):
    command = [
        os.environ.get('CLAUDE') or shared.executable(),
        '-p', '--system-prompt', system, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--tools', '',
        '--max-turns', '1',
    ] + (['--effort', effort] if effort else [])
    last = None
    for attempt in range(shared.CALL_ATTEMPTS):
        try:
            process = subprocess.run(
                command, input=prompt.encode('utf-8'),
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=shared.CALL_TIMEOUT)
        except subprocess.TimeoutExpired:
            last = f'the harness answered nothing in {shared.CALL_TIMEOUT}s and was killed'
        else:
            if process.returncode != 0:
                said = process.stderr.decode('utf-8', 'replace').strip()
                last = said or f'the harness exited {process.returncode} and said nothing'
            else:
                try:
                    return json.loads(process.stdout.decode('utf-8', 'replace')), None
                except json.JSONDecodeError as broken:
                    last = f'the harness did not return JSON: {broken}'
        if attempt + 1 < shared.CALL_ATTEMPTS:
            time.sleep(5)
    return None, last


def run_batches(directory, folder, jobs, system, version, args, shape):
    """
    Each batch asked once, its answers written the moment they arrive, so a run that dies half way
    has paid for half and keeps it. `jobs` is (name, prompt, items); `shape` turns one item and the
    answer the model gave for it into the row that is kept.
    """
    out = os.path.join(directory, folder)
    os.makedirs(out, exist_ok=True)
    pending = [job for job in jobs
               if args.again or not os.path.exists(os.path.join(out, job[0] + '.jsonl'))]
    if not pending:
        print(f'{folder}: every batch already has answers. Pass --again to run them anyway.')
        return
    today = datetime.date.today().isoformat()
    lock = threading.Lock()
    began = time.time()
    totals = {'cost': 0.0, 'done': 0, 'failed': 0, 'input': 0, 'output': 0}

    budget = getattr(args, 'budget', None)

    def one(job):
        name, prompt, items = job
        with lock:
            if budget is not None and totals['cost'] >= budget:
                totals['failed'] += 1
                print(f'{name}: not asked, ${totals["cost"]:.4f} already spent of the ${budget} budget', flush=True)
                return
        started = time.time()
        outcome, failure = call(prompt, args.model, args.effort, system)
        seconds = time.time() - started
        if failure:
            with lock:
                totals['failed'] += 1
                print(f'{name}: {failure}', flush=True)
            return
        cost = outcome.get('total_cost_usd') or 0.0
        model = next(iter(outcome.get('modelUsage') or {}), args.model)
        answers = shared.parse(outcome.get('result'))
        if answers is None:
            with lock:
                totals['cost'] += cost
                totals['failed'] += 1
                print(f'{name}: the reply held no JSON array (${cost:.4f} paid)', flush=True)
            return
        read, wrote = shared.tokens(outcome)
        share = {'model': model, 'askedAt': today, 'effort': args.effort, 'promptVersion': version,
                 'cost': round(cost / len(items), 6), 'seconds': round(seconds / len(items), 2),
                 'inputTokens': round(read / len(items)), 'outputTokens': round(wrote / len(items))}
        rows = [dict(shape(item, answers), **share) for item in items]
        with lock:
            with open(os.path.join(out, name + '.jsonl'), 'w', encoding='utf-8') as handle:
                for row in rows:
                    handle.write(json.dumps(row, ensure_ascii=False) + '\n')
            totals['cost'] += cost
            totals['done'] += 1
            totals['input'] += read
            totals['output'] += wrote
            print(f'[{totals["done"]}/{len(pending)}] {name}: {len(items)} items, {seconds:.0f}s, '
                  f'{read} tokens in, {wrote} out, ${cost:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(one, pending))

    wall = time.time() - began
    path = os.path.join(out, 'runs.jsonl')
    with open(path, 'a', encoding='utf-8') as handle:
        handle.write(json.dumps({'at': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
                                 'batches': len(pending), 'failed': totals['failed'],
                                 'cost': round(totals['cost'], 4), 'wall_seconds': round(wall, 1),
                                 'workers': args.workers, 'model': args.model,
                                 'effort': args.effort, 'prompt_version': version,
                                 'input_tokens': totals['input'], 'output_tokens': totals['output']}) + '\n')
    print(f'{folder}: {totals["done"]} batches answered, {totals["failed"]} failed, '
          f'${totals["cost"]:.4f} in {wall:.0f}s wall clock')


def collect(directory, folder):
    out = os.path.join(directory, folder)
    rows = []
    if os.path.isdir(out):
        for name in sorted(n for n in os.listdir(out) if re.fullmatch(r'batch-\d+\.jsonl', n)):
            with open(os.path.join(out, name), encoding='utf-8') as handle:
                rows += [json.loads(line) for line in handle if line.strip()]
    return rows
