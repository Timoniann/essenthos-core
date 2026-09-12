"""
Which verses that only BibleData lists for a person or a place speak of them, where the verse does not
print the name? Ask, verse in hand.

BibleData cites 9,112 entity-verse references no annotation of this corpus reaches. In 2,579 of them the
King James text of the verse prints no spelling of the entity's name at all: the verse speaks of the
person by a pronoun, a title or a description, or does not speak of them. A name-matching pass cannot
tell those apart, so each reference is taken as a question for a model reading the verse and the lines
around it, and nothing of BibleData's is shown except which verse to look at.

    python scripts/references.py measure
    python scripts/references.py extract --out .references/pilot --sample 100 --seed 7
    python scripts/references.py ask     --dir .references/pilot --workers 4 --effort medium
    python scripts/references.py score   --dir .references/pilot

**Who is meant comes from this corpus, not from the dataset.** The model is shown up to three verses in
which this corpus's own annotations find the name printed, so a pronoun can be tied to the right man;
BibleData's descriptive sentence is never shown, because it is the answer the pass exists to check.

A run leaves:

    references.json            every reference in scope
    manifest.json              the selection, its seed, and which references went into which batch
    batches/batch-NNNN.json    the prompt payload, exactly as the model saw it
    out/batch-NNNN.jsonl       one verdict per reference
    report.md, sample-refers.md, sample-not.md
"""

import argparse
import collections
import datetime
import json
import os
import random
import re

import descriptors as shared
import relationships as harness

PROMPT_VERSION = 'references-1'

REFERS, NOT, UNCLEAR = 'refers', 'not', 'unclear'
VERDICTS = (REFERS, NOT, UNCLEAR)

WINDOW_BEFORE = 6
WINDOW_AFTER = 2
NAMED_IN_SHOWN = 3
ITEMS_PER_BATCH = 20
SAMPLE_READ_BY_EYE = 20

SYSTEM = """\
You are checking whether a Bible verse speaks of a particular person or place, in a verse that does not
print that name.

For each item you are given the person or place under `entity`, the verse under `verse`, and under
`named_in` a few other verses where this corpus has found the name printed, so that you know who is
meant. The King James text of the verse, the lines just before and after it, and the `named_in` verses is
under `passages`. Each passage lists in `named_here` the people and places this corpus has already
identified in that line.

Answer for each item with one of:

  "refers"   the verse speaks of this person or place -- by a pronoun, a title, a description, or a
             spelling of the name other than the one given -- and the lines shown make it clear that it
             is this one. Quote the words of the verse that refer to them.
  "not"      the verse does not speak of them, or speaks of somebody else.
  "unclear"  the lines shown do not settle it.

The rules that decide most cases:

- **Being in the same story is not being spoken of.** A verse about what happened next, or about the
  people around somebody, does not refer to them unless its own words do.
- **A pronoun counts only when the lines shown make its referent this person or place**, not when it
  merely could be.
- **A people, a tribe or a land is not the person it is named after**, and a namesake is not this one.
- **"The LORD", "God" and "the king" refer to this entity only if this entity is that one** -- the
  angel of the LORD is not the LORD, and a king of Egypt is not every king named in the chapter.

Give a confidence between 0 and 1: how sure you are that the words you quote, in this verse, refer to
this entity.

Return a single JSON array and nothing else, one object per item, in the order given:

  { "item": "<id>", "verdict": "refers" | "not" | "unclear",
    "words": "the words of the verse that refer to them" | null,
    "confidence": 0.0-1.0, "reason": "one sentence" }
"""


def in_scope():
    """BibleData's references that no reference of ours reaches, with the entity's spellings."""
    references = shared.psql("""
        WITH ours AS (SELECT DISTINCT entity_id, canonical_book b, canonical_chapter c, canonical_verse v
                      FROM entity_verse WHERE source LIKE 'Essenthos%')
        SELECT coalesce(json_agg(json_build_array(x.entity_id, x.b, x.c, x.v)), '[]') FROM (
            SELECT DISTINCT ev.entity_id, ev.canonical_book b, ev.canonical_chapter c, ev.canonical_verse v
            FROM entity_verse ev
            WHERE ev.source LIKE 'BibleData%'
              AND NOT EXISTS (SELECT 1 FROM ours o WHERE o.entity_id = ev.entity_id AND o.b = ev.canonical_book
                              AND o.c = ev.canonical_chapter AND o.v = ev.canonical_verse)) x""")
    entities = shared.psql("""
        SELECT json_object_agg(e.id, json_build_object(
            'slug', e.slug, 'name', e.name, 'kind', e.kind, 'sex', e.sex,
            'labels', (SELECT coalesce(json_agg(DISTINCT n.label), '[]') FROM entity_name n WHERE n.entity_id = e.id),
            'ours', (SELECT coalesce(json_agg(json_build_array(v.canonical_book, v.canonical_chapter, v.canonical_verse)
                                              ORDER BY v.canonical_book, v.canonical_chapter, v.canonical_verse), '[]')
                     FROM entity_verse v WHERE v.entity_id = e.id AND v.source LIKE 'Essenthos%')))
        FROM entity e""")
    return references, entities


def spellings(entity):
    return {n.lower() for n in {entity['name'], *entity['labels']} if n and n[0].isalpha()}


def printed(names, line):
    flat = re.sub(r'[^a-z ]', '', line.lower())
    return any(re.search(r'(?<![a-z])' + re.escape(form) + r'(?![a-z])', flat)
               for name in names for form in {name, name.replace('-', ''), name.replace('-', ' ')})


def unprinted(cache_dir):
    references, entities = shared.cache(cache_dir, 'scope', lambda: list(in_scope()))
    lines = shared.keyed(shared.cache(cache_dir, shared.RENDERING, lambda: shared.rendering(shared.RENDERING)))
    chosen = []
    for entity_id, b, c, v in references:
        entity = entities.get(str(entity_id))
        line = lines.get((b, c, v))
        if entity is None or line is None or printed(spellings(entity), line):
            continue
        chosen.append({'item': f'r{len(chosen):04d}', 'entity': entity['slug'], 'reference': shared.reference(b, c, v)})
    return chosen, entities, lines


def measure(args):
    chosen, entities, _ = unprinted(args.cache)
    by_slug = {e['slug']: e for e in entities.values()}
    kinds = collections.Counter(by_slug[r['entity']]['kind'] for r in chosen)
    heads = collections.Counter(r['entity'] for r in chosen)
    print(f'{len(chosen)} references only BibleData states, in verses that do not print the name.')
    print('  by kind: ' + ', '.join(f'{n} {k}' for k, n in kinds.most_common()))
    print('  most cited: ' + ', '.join(f'{by_slug[s]["name"]} {n}' for s, n in heads.most_common(12)))
    return chosen


def extract(args):
    chosen, entities, lines = unprinted(args.cache)
    by_slug = {e['slug']: e for e in entities.values()}
    occupancy = collections.defaultdict(set)
    for b, c, v, slug in shared.cache(args.cache, 'occupancy', shared.occupancy):
        occupancy[(b, c, v)].add(slug)
    chapters = collections.defaultdict(list)
    for b, c, v in lines:
        chapters[(b, c)].append(v)

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    with open(os.path.join(args.dir, 'references.json'), 'w', encoding='utf-8') as handle:
        json.dump(chosen, handle, ensure_ascii=False, indent=1)

    pool = list(chosen)
    if args.sample:
        random.Random(args.seed).shuffle(pool)
        pool = pool[:args.sample]
    pool.sort(key=lambda r: shared.parse_reference(r['reference']))

    def shown_for(item):
        b, c, v = shared.parse_reference(item['reference'])
        window = [(b, c, x) for x in chapters[(b, c)] if v - WINDOW_BEFORE <= x <= v + WINDOW_AFTER]
        ours = [tuple(a) for a in by_slug[item['entity']]['ours']]
        named_in = ours[:: max(1, len(ours) // NAMED_IN_SHOWN)][:NAMED_IN_SHOWN] if ours else []
        return window, named_in

    manifest = {'prompt_version': PROMPT_VERSION,
                'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
                'selection': {'population': len(chosen), 'sample': args.sample, 'seed': args.seed, 'drawn': len(pool)},
                'batches': []}
    for number, start in enumerate(range(0, len(pool), ITEMS_PER_BATCH)):
        group, verses, people = pool[start:start + ITEMS_PER_BATCH], set(), set()
        items = []
        for item in group:
            entity = by_slug[item['entity']]
            window, named_in = shown_for(item)
            verses |= set(window) | set(named_in)
            people.add(entity['slug'])
            items.append({'item': item['item'],
                          'entity': {'slug': entity['slug'], 'name': entity['name'], 'kind': entity['kind'],
                                     'sex': entity.get('sex')},
                          'verse': item['reference'],
                          'named_in': [shared.reference(*a) for a in named_in]})
        passages = [{'reference': shared.reference(*a), 'text': lines.get(a),
                     'named_here': sorted(occupancy.get(a, set()) & people)} for a in sorted(verses) if a in lines]
        name = f'batch-{number:04d}'
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump({'batch': name, 'prompt_version': PROMPT_VERSION, 'items': items, 'passages': passages},
                      handle, ensure_ascii=False, indent=1)
        manifest['batches'].append({'batch': name, 'items': [i['item'] for i in items], 'verses': len(passages)})
    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)
    print(f'{len(pool)} of {len(chosen)} references in {len(manifest["batches"])} batches -> {args.dir}')


def ask(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    jobs = []
    for entry in manifest['batches']:
        with open(os.path.join(args.dir, 'batches', entry['batch'] + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        jobs.append((entry['batch'], json.dumps(payload, ensure_ascii=False, indent=1), payload['items']))

    def shape(item, answers):
        answer = next((a for a in answers if isinstance(a, dict) and a.get('item') == item['item']), None)
        verdict = answer.get('verdict') if answer else None
        try:
            confidence = round(min(1.0, max(0.0, float(answer.get('confidence')))), 3) if answer else None
        except (TypeError, ValueError):
            confidence = None
        return {'item': item['item'], 'entity': item['entity']['slug'], 'reference': item['verse'],
                'verdict': verdict if verdict in VERDICTS else None,
                'words': answer.get('words') if answer else None,
                'confidence': confidence, 'reason': answer.get('reason') if answer else None}

    harness.run_batches(args.dir, 'out', jobs, SYSTEM, args, shape)


def score(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    rows = harness.collect(args.dir, 'out')
    _, entities, lines = unprinted(args.cache)
    by_slug = {e['slug']: e for e in entities.values()}
    population = manifest['selection']['population']
    runs = []
    runs_path = os.path.join(args.dir, 'out', 'runs.jsonl')
    if os.path.exists(runs_path):
        with open(runs_path, encoding='utf-8') as handle:
            runs = [json.loads(line) for line in handle if line.strip()]
    spent = sum(r['cost'] for r in runs)
    per_item = spent / max(1, len(rows))

    out = ['# References only BibleData states, in verses that do not print the name\n',
           f'{len(rows)} of {population} asked ({manifest["prompt_version"]}, seed {manifest["selection"]["seed"]}).\n',
           '| verdict | items | share | of them confidence >= 0.8 |', '|---|---|---|---|']
    for verdict in (*VERDICTS, None):
        of = [r for r in rows if r['verdict'] == verdict]
        confident = sum(1 for r in of if (r['confidence'] or 0) >= 0.8)
        out.append(f'| {verdict or "no answer"} | {len(of)} | {len(of) / max(1, len(rows)):.0%} | {confident} |')
    out.append('\n| entity kind | refers | not | unclear |')
    out.append('|---|---|---|---|')
    for kind in sorted({by_slug[r['entity']]['kind'] for r in rows}):
        of = [r for r in rows if by_slug[r['entity']]['kind'] == kind]
        out.append(f'| {kind} | ' + ' | '.join(str(sum(1 for r in of if r['verdict'] == v)) for v in VERDICTS) + ' |')
    out.append(f'\n**Cost:** ${spent:.4f} over {len(runs)} runs, ${per_item:.5f} an item, '
               f'{sum(r["wall_seconds"] for r in runs):.0f}s wall clock.')
    out.append(f'**Projected over all {population}:** ${per_item * population:.2f} for one reading; a second '
               f'reading of the positives, as the relationship pass used, adds roughly the share that refers.')
    report = '\n'.join(out) + '\n'
    with open(os.path.join(args.dir, 'report.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    print(report)

    shuffle = random.Random(args.seed)
    for verdict, name in ((REFERS, 'sample-refers.md'), (NOT, 'sample-not.md')):
        pool = [r for r in rows if r['verdict'] == verdict]
        shuffle.shuffle(pool)
        text = [f'# {len(pool)} {verdict}, {min(len(pool), SAMPLE_READ_BY_EYE)} drawn\n']
        for r in pool[:SAMPLE_READ_BY_EYE]:
            entity = by_slug[r['entity']]
            text.append(f'## {r["item"]} {entity["name"]} ({entity["kind"]}) in {r["reference"]}  '
                        f'confidence {r["confidence"]}\n')
            text.append(f'> {lines.get(shared.parse_reference(r["reference"]))}\n')
            text.append(f'words: {r["words"]!r}; reason: {r["reason"]}\n')
        with open(os.path.join(args.dir, name), 'w', encoding='utf-8') as handle:
            handle.write('\n'.join(text) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    parser.add_argument('--cache', default='.references/cache', help='where the database reads are kept between runs')
    commands = parser.add_subparsers(dest='command', required=True)

    commands.add_parser('measure', help='count the references in scope').set_defaults(run=measure)

    extractor = commands.add_parser('extract', help='write the prompt payloads for a run')
    extractor.add_argument('--out', dest='dir', required=True)
    extractor.add_argument('--sample', type=int, help='draw this many references at random')
    extractor.add_argument('--seed', type=int, default=7)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='run the reading over every batch that has no answers yet')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--workers', type=int, default=4)
    asker.add_argument('--effort', default='medium',
                       help='low was measured noisy on reading work (PRB-0437); medium is the default here')
    asker.add_argument('--again', action='store_true')
    asker.set_defaults(run=ask)

    scorer = commands.add_parser('score', help='the verdicts, the cost and a sample to read by eye')
    scorer.add_argument('--dir', required=True)
    scorer.add_argument('--seed', type=int, default=7)
    scorer.set_defaults(run=score)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
