"""Build the admitted core dataset from frozen, attributed readings without any model calls."""
import argparse
import hashlib
import json
from pathlib import Path

MAIN = Path(r'C:\Users\timon\Projects\Essenthos\essenthos-core')
BASE = MAIN / 'Resources/Essenthos/generation/person-presence-sonnet55-2026-10-26-v3'
REVIEW = MAIN / 'Resources/Essenthos/generation/presence-refinement-2026-10-26-v1'
ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / 'Resources/Essenthos/person-presence.json'
BOOKS = 'GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD REV'.split()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build():
    parent = read(REVIEW / 'parent-validation.json')
    assert parent['complete'] and parent['covered'] == 1171
    assert sha(BASE / 'results.json') == parent['source_results_sha256']
    for name, expected in parent['inputs'].items():
        assert sha(REVIEW / name.replace('\\', '/')) == expected, name
    jobs = {}
    with (BASE / 'jobs.jsonl').open(encoding='utf-8') as stream:
        for line in stream:
            job = json.loads(line)
            jobs[job['id']] = {v['key']: v for v in job['verses']}
    pairs = {}

    def add(person, place, category, events):
        key = (person, place, category)
        row = pairs.setdefault(key, dict(person=person, place=place, category=category, addresses=set(), evidence=[]))
        for event in events:
            witnesses = jobs[event['job_id']]
            quoted = event.get('evidence', []) + event.get('identity_evidence', [])
            if 'readings' in event:
                quoted += [q for reading in event['readings'] for k in ('evidence', 'identity_evidence') for q in reading.get(k, [])]
            assert quoted, key
            for quote in quoted:
                verse = witnesses[quote['verse']]
                assert quote['quote'] in verse['body'], (key, quote)
                for book, chapter, number in verse.get('canonicalAddresses', [verse['canonical']]):
                    row['addresses'].add(book * 1_000_000 + chapter * 1000 + number)
            row['evidence'].append(event)

    baseline = read(BASE / 'results.json')
    for person in baseline['persons']:
        for place in person['places']:
            add(person['person'], place['place_slug'], place['category'], place['findings'])
    assert len(pairs) == 1567
    for pair in read(REVIEW / 'disagreements/accepted-pair-delta.json')['pairs']:
        add(pair['person'], pair['place_slug'], pair['category'], pair['findings'])
    assert len(pairs) == 1946
    # Both original readers explicitly describe Abner's traversal in the same held verse.
    # Their only refusal was the missing candidate, now resolved against the full catalogue.
    classifications = {row['case_id']: row for row in read(REVIEW / 'places/classifications.json')}
    for case_id in ('abner:0', 'abner:1'):
        row = classifications[case_id]
        assert row['name_resolution']['target_slug'] == 'bithron'
        event = row['original']
        assert event['reason'] == 'unregistered_place' and event['relation'] == 'passed'
        add('abner', 'bithron', 'present', [{**event, 'integration_reading': 'Agent reading 2SA 2:29 on owner integration instruction, 2026-10-26; existing Bithron identified; actual traversal explicit.'}])
    sites = []
    for proposal in read(REVIEW / 'places/draft-records.json'):
        record = proposal['record']
        citations = proposal['held_citations']
        passages = []
        for citation in citations:
            _, book, chapter, number = citation['verse'].split(':')
            assert citation['quote'] in citation['text']
            passage = dict(reference=f'{BOOKS[int(book)-1]} {chapter}:{number}', role='key')
            if passage not in passages:
                passages.append(passage)
        sites.append(dict(
            slug=record['slug'], kind=record['kind'], subtype=record.get('subtype') if record['kind'] == 'object' else None,
            name=record['name'], names=record['names'],
            distinguisher=record['name'], passages=passages,
            why=proposal['duplicate_exclusion_uk'], placeKind=record.get('subtype', 'site') if record['kind'] == 'place' else None,
        ))
    assert len(sites) == 18
    records = []
    for key in sorted(pairs):
        row = pairs[key]
        row['addresses'] = sorted(row['addresses'])
        records.append(row)
    return dict(
        version=1,
        source='Essenthos: claude-sonnet-5-5 independent chapter readings and model/agent adjudication, 2026-10-26; owner instructed integration; model provenance retained, not independently measured certainty.',
        admission=dict(instruction='Інтегруй в core', date='2026-10-26', task='TSK-1246', sourceResultsSha256=parent['source_results_sha256'], sourceInputs=parent['inputs'], newModelCalls=0),
        records=records, sites=sites,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['publish', 'verify'])
    args = parser.parse_args()
    payload = build()
    body = json.dumps(payload, ensure_ascii=False, separators=(',', ':')) + '\n'
    if args.mode == 'publish':
        TARGET.parent.mkdir(parents=True, exist_ok=True)
        temp = TARGET.with_suffix('.json.partial')
        temp.write_bytes(body.encode('utf-8'))
        temp.replace(TARGET)
    else:
        assert TARGET.read_text(encoding='utf-8') == body
    summary = dict(pairs=len(payload['records']), persons=len({r['person'] for r in payload['records']}),
                   sites=len(payload['sites']), sha256=sha(TARGET), new_model_calls=0)
    (REVIEW / 'integration-compiled.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary))


if __name__ == '__main__':
    main()
