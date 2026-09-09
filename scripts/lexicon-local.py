"""
The same lexicon batches, asked of a model running on this machine, scored the same way.

The question is not "can a local model translate Ukrainian" — it is whether it can leave a
dictionary entry's *structure* alone while doing it. 99.7% of Sonnet's entries kept every identifier
and 95.3% were judged usable; those are the numbers to beat, and the only honest comparison is the
same entries through the same deterministic check.

So this reuses scripts/lexicon.py rather than reimplementing it: the same SYSTEM_PROMPT, the same
payload shape, the same failures_of(). Only the call changes.

    python scripts/lexicon-local.py --model gemma3:12b --batches 4

Context is capped deliberately. A batch is about 1.7 KB and the system prompt 3.4 KB, so 8k tokens
is several times what the work needs; leaving Ollama's window at its default costs minutes per call
on a 16 GB card for context nothing fills.
"""
import argparse, glob, io, json, os, sys, time, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util
spec = importlib.util.spec_from_file_location(
    'lexicon', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lexicon.py'))
lexicon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lexicon)

OLLAMA = os.environ.get('OLLAMA_HOST', 'http://localhost:11434')


ENTRY_SCHEMA = {
    'type': 'array',
    'items': {
        'type': 'object',
        'properties': {
            'strong_number': {'type': 'string'},
            'definition': {'type': 'string'},
            'derivation': {'type': 'string'},
            'kjv_definition': {'type': 'string'},
            'detailed_definition': {'type': 'string'},
            'uncertain': {'type': 'array', 'items': {'type': 'string'}},
        },
        'required': ['strong_number'],
    },
}


def ask(model, system, prompt, num_ctx, timeout, schema):
    """
    `format` takes either the string "json" or a JSON schema. The schema is the fair equivalent of
    what the paid harness gets from --output-format json: a way to be held to a shape without the
    prompt being rewritten for one model. Mistral answered a single object three times out of three
    under the loose form and the same batches under the schema are a different measurement.
    """
    body = json.dumps({
        'model': model,
        'stream': False,
        'format': (ENTRY_SCHEMA if schema else 'json'),
        'options': {'num_ctx': num_ctx, 'temperature': 0},
        'messages': [
            {'role': 'system', 'content': system},
            {'role': 'user', 'content': prompt},
        ],
    }).encode('utf-8')
    request = urllib.request.Request(
        OLLAMA + '/api/chat', data=body, headers={'Content-Type': 'application/json'})
    started = time.time()
    with urllib.request.urlopen(request, timeout=timeout) as response:
        answer = json.loads(response.read().decode('utf-8'))
    return answer['message']['content'], time.time() - started


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True)
    parser.add_argument('--dir', default='.lexicon/full-uk')
    parser.add_argument('--batches', type=int, default=4)
    parser.add_argument('--num-ctx', type=int, default=8192)
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--out', default='.lexicon/local')
    parser.add_argument('--schema', action='store_true',
                        help='hold the model to a JSON schema rather than only to "json"')
    parser.add_argument('--tag', default='', help='suffix for the output file, to keep runs apart')
    args = parser.parse_args()

    source = {e['strong_number']: e for e in json.load(io.open(
        os.path.join(args.dir, 'source.json'), encoding='utf-8'))}
    language = json.load(io.open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8'))['language']

    os.makedirs(args.out, exist_ok=True)
    files = sorted(glob.glob(os.path.join(args.dir, 'batches', '*.json')))[:args.batches]

    asked = kept = usable = wanted = 0
    seconds = 0.0
    faults = {}
    rows = []

    for path in files:
        payload = json.load(io.open(path, encoding='utf-8'))
        wanted += len(payload['entries'])
        try:
            said, took = ask(args.model, lexicon.SYSTEM_PROMPT,
                             json.dumps(payload, ensure_ascii=False, indent=1),
                             args.num_ctx, args.timeout, args.schema)
        except Exception as broken:
            print(f'{os.path.basename(path)}: call failed — {broken}')
            continue
        seconds += took
        try:
            answers = json.loads(said)
        except json.JSONDecodeError as broken:
            print(f'{os.path.basename(path)}: not JSON — {broken}')
            faults.setdefault('unparseable-batch', 0)
            faults['unparseable-batch'] += 1
            continue

        items = answers.get('entries') if isinstance(answers, dict) else answers
        if not isinstance(items, list):
            faults.setdefault('wrong-shape', 0)
            faults['wrong-shape'] += 1
            print(f'{os.path.basename(path)}: answered {type(items).__name__}, not a list')
            continue

        for answer in items:
            number = answer.get('strong_number')
            if number not in source:
                faults.setdefault('unknown-number', 0)
                faults['unknown-number'] += 1
                continue
            asked += 1
            found = lexicon.failures_of(source[number], answer, language)
            structural = [f for f in found if f[1] != 'not-in-language']
            if not structural:
                kept += 1
            if not found:
                usable += 1
            for _, kind, _ in found:
                faults[kind] = faults.get(kind, 0) + 1
            rows.append({'strong_number': number, 'faults': [list(f) for f in found], 'answer': answer})

        print(f'{os.path.basename(path)}: {len(items)} entries, {took:.0f}s')

    name = args.model.replace(':', '-').replace('/', '-') + (('-' + args.tag) if args.tag else '')
    io.open(os.path.join(args.out, f'{name}.jsonl'), 'w', encoding='utf-8').write(
        '\n'.join(json.dumps(r, ensure_ascii=False) for r in rows))

    print()
    print(f'model {args.model}, num_ctx {args.num_ctx}')
    print(f'entries asked {wanted}, answered {asked}' +
          (f'  — {wanted - asked} never came back' if wanted > asked else ''))
    if asked:
        print(f'kept every identifier {kept} ({100*kept/asked:.1f}%)   Sonnet: 99.7%')
        print(f'no fault at all      {usable} ({100*usable/asked:.1f}%)   Sonnet: 95.3%')
    print(f'seconds per entry {seconds/max(asked,1):.1f}')
    if faults:
        print('faults: ' + ', '.join(f'{k} {v}' for k, v in sorted(faults.items(), key=lambda x: -x[1])))


if __name__ == '__main__':
    main()
