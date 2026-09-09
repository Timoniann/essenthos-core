"""
Re-ask the entries the deterministic check rejected, one at a time, told what they got wrong.

`check` names every failure precisely — which entry, which field, which kind, and what was dropped
or invented — and a failure named that exactly is a failure a model can be asked to avoid. So this
does not re-run a batch and hope: it re-asks each rejected entry alone, with its own faults quoted
back at it, and **replaces the stored answer only if the new one passes the same check**. A repair
that fails leaves the original in place and is reported, because a silent downgrade is worse than a
known fault.

    python scripts/lexicon-repair.py --dir .lexicon/full-uk

The Ukrainian run left 40 failures over 31 entries in 14,197 — 99.78% clean — and this is what the
last fifth of a percent costs: about thirty calls.
"""
import argparse, importlib.util, io, json, os, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('lexicon', os.path.join(HERE, 'lexicon.py'))
lexicon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lexicon)

SAID = {
    'lost-reference': 'You dropped a Strong reference that was in the English. Every one of them is '
                      'an address another entry is found by; copy them through untouched.',
    'invented-reference': 'You added a Strong reference the English does not have. A reference that '
                          'is not in the source is an address pointing at the wrong entry.',
    'lost-original-script': 'You dropped a quoted Hebrew or Greek word. Those are copied through in '
                            'their own script, never transliterated and never translated.',
    'untranslated': 'You returned the English unchanged. A list of one is still a list, and a proper '
                    'name is written in the target language\'s own spelling.',
    'not-in-language': 'You answered mostly in Latin letters. Answer in the target language.',
    'missing': 'You returned the field empty. It was given, so it is translated.',
    'sense-numbering': 'You changed the sense numbering. It is preserved exactly, one numbered sense '
                       'per line.',
    'renderings-collapsed': 'You collapsed the list of renderings. Give the senses those renderings '
                            'carry, in the same order, one for one.',
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dir', required=True)
    parser.add_argument('--model', default='sonnet')
    args = parser.parse_args()

    manifest = json.load(io.open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8'))
    language = manifest['language']
    source = {e['strong_number']: e for e in json.load(
        io.open(os.path.join(args.dir, 'source.json'), encoding='utf-8'))}
    failures = json.load(io.open(os.path.join(args.dir, 'failures.json'), encoding='utf-8'))

    by_entry = {}
    for f in failures:
        by_entry.setdefault(f['strong_number'], []).append(f)

    table = lexicon.GLOSSARY.get(language)
    system = lexicon.SYSTEM_PROMPT.format(
        language=manifest['language_name'], kjv_rule=lexicon.KJV_RULE,
        untranslated=lexicon.UNTRANSLATED_TERMS,
        glossary='\n'.join(f'  {e:<24} {t}' for e, t in table))

    path = os.path.join(args.dir, 'answers.jsonl')
    rows = [json.loads(l) for l in io.open(path, encoding='utf-8') if l.strip()]
    stored = {r['strong_number']: r for r in rows}

    repaired = refused = 0
    for number, faults in sorted(by_entry.items()):
        entry = source[number]
        told = '\n'.join(
            f"  {f['field']}: {SAID.get(f['kind'], f['kind'])} ({f['detail']})" for f in faults)
        payload = {
            'batch': 'repair', 'language': language,
            'prompt_version': manifest.get('prompt_version', lexicon.PROMPT_VERSION),
            'entries': [entry],
        }
        prompt = (json.dumps(payload, ensure_ascii=False, indent=1)
                  + '\n\nA previous answer for this entry was rejected:\n' + told
                  + '\nAnswer again, avoiding that. Everything else about the task is unchanged.')
        outcome, why = lexicon.call(prompt, system, args.model)
        if why:
            print(f'{number}: call failed — {why}')
            refused += 1
            continue
        answers = lexicon.parse(outcome.get('result'))
        answer = answers[0] if answers else None
        if not answer:
            print(f'{number}: no parseable answer')
            refused += 1
            continue
        left = lexicon.failures_of(entry, answer, language)
        if left:
            print(f'{number}: still failing — ' + ', '.join(f'{a}:{b}' for a, b, _ in left))
            refused += 1
            continue
        row = dict(stored[number])
        for field in lexicon.PROSE:
            if entry.get(field):
                row[field] = answer.get(field)
        row['uncertain'] = answer.get('uncertain') or []
        row['repaired'] = [f"{f['field']}:{f['kind']}" for f in faults]
        stored[number] = row
        repaired += 1
        print(f'{number}: repaired ({", ".join(f["kind"] for f in faults)})')

    shutil.copyfile(path, path + '.before-repair')
    with io.open(path, 'w', encoding='utf-8', newline='') as handle:
        for r in rows:
            handle.write(json.dumps(stored[r['strong_number']], ensure_ascii=False) + '\n')

    print(f'\n{repaired} repaired, {refused} left as they were, of {len(by_entry)} entries')
    print(f'the answers before this run are kept at {os.path.basename(path)}.before-repair')


if __name__ == '__main__':
    main()
