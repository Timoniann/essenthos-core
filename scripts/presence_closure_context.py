"""Freeze expanded, edition-addressed Scripture for the remaining presence cases."""
import argparse
import collections
import copy
import hashlib
import json
from pathlib import Path

MAIN = Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
BASE = MAIN / 'Resources/Essenthos/generation/person-presence-sonnet55-2026-10-26-v3'
REVIEW = MAIN / 'Resources/Essenthos/generation/presence-refinement-2026-10-26-v1'
OUT = MAIN / 'Resources/Essenthos/generation/presence-closure-2026-10-26-v1'
VERSION = 'presence-closure-context-v1'
CORPORA = ('BHSA', 'KJV', 'BSB', 'UBIO')
PARALLELS = {
    (14, 25): [(12, 14)],
    (10, 6): [(9, 7), (13, 13)],
    (13, 13): [(9, 7), (10, 6)],
    (1, 35): [(1, 33)],
    (2, 2): [(2, 7)],
    (10, 24): [(13, 21)],
    (13, 21): [(10, 24)],
    (12, 11): [(14, 22), (14, 23)],
    (14, 23): [(12, 11)],
    (4, 13): [(5, 1), (4, 32)],
    (4, 11): [(4, 33)],
}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def freeze(path, value):
    data = (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    if path.exists():
        if path.read_bytes() != data:
            raise ValueError(f'Frozen output differs: {path}; use a new version instead of overwriting')
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_bytes(data)
    temporary.replace(path)


def key(row):
    return f'{row["text"]}:{row["book"]}:{row["chapter"]}:{row["verse"]}{row.get("label") or ""}'


def inventory(review=REVIEW):
    disagreements = read(review / 'disagreements/decisions.json')['decisions']
    places = read(review / 'places/classifications.json')
    items = [('disagreement', row) for row in disagreements if row['decision'] == 'unresolved']
    items += [('place', row) for row in places if row['name_resolution']['category'] == 'ambiguous']
    assert collections.Counter(kind for kind, _ in items) == {'disagreement': 128, 'place': 73}
    assert len({row['case_id'] for _, row in items}) == 201
    return sorted(items, key=lambda item: item[1]['case_id'])


def selection(job):
    by_key = {verse['key']: verse for verse in job['verses']}
    focus = {tuple(address) for name in job['focus']
             for address in by_key[name].get('canonicalAddresses', [by_key[name]['canonical']])}
    focus_chapters = {(b, ch) for b, ch, _ in focus}
    wanted = {(b, neighbor) for b, ch in focus_chapters for neighbor in (ch-1, ch, ch+1) if neighbor > 0}
    # A source packet can contain identity witnesses farther away than its adjacent chapters.
    wanted.update(tuple(address[:2]) for verse in job['verses']
                  for address in verse.get('canonicalAddresses', [verse['canonical']]))
    parallels = {address for chapter in focus_chapters for address in PARALLELS.get(chapter, [])}
    return focus, wanted | parallels, parallels


def source_rows(path, expected_hash, corpora):
    if sha(path) != expected_hash:
        raise ValueError('The frozen corpus export changed')
    verses, placements, texts = {}, collections.defaultdict(list), {}
    with path.open(encoding='utf-8') as source:
        for line in source:
            row = json.loads(line)
            if row['type'] == 'text' and row['slug'] in corpora:
                texts[row['slug']] = row
            elif row['type'] == 'verse' and row['text'] in corpora:
                name = key(row)
                if name in verses:
                    raise ValueError(f'Duplicate edition address: {name}')
                verses[name] = row
            elif row['type'] == 'placement' and row['text'] in corpora:
                placements[key(row)].append(row['canonical'])
    for name, row in verses.items():
        row['canonicalAddresses'] = [row['canonical']]
        for address in placements.get(name, []):
            if address not in row['canonicalAddresses']:
                row['canonicalAddresses'].append(address)
    return verses, texts


def scripture(row, focus):
    addresses = row['canonicalAddresses']
    return dict(key=key(row), body=row['body'], canonical=row['canonical'],
                canonicalAddresses=addresses,
                role='event' if any(tuple(address) in focus for address in addresses) else 'context')


def diagnostic(verses, focus):
    old = sorted(address for address in focus if address[0] <= 39)
    supplied = {tuple(address) for verse in verses if verse['key'].startswith('BHSA:')
                for address in verse['canonicalAddresses']}
    missing = [address for address in old if address not in supplied]
    return dict(applicable=bool(old), requested_focus_addresses=[list(a) for a in old],
                missing_focus_addresses=[list(a) for a in missing],
                bhsa_verse_count=sum(verse['key'].startswith('BHSA:') for verse in verses),
                status='complete' if old and not missing else 'partial' if old else 'not_applicable_new_testament')


def build():
    items = inventory()
    job_ids = {row['job_id'] if kind == 'disagreement' else row['original']['job_id'] for kind, row in items}
    jobs = {}
    with (BASE / 'jobs.jsonl').open(encoding='utf-8') as source:
        for line in source:
            job = json.loads(line)
            if job['id'] in job_ids:
                jobs[job['id']] = job
    assert jobs.keys() == job_ids
    corpora = set(CORPORA) | {job['text'] for job in jobs.values()}
    export_manifest = read(BASE / 'export-manifest.json')
    verses, texts = source_rows(BASE / 'export.jsonl', export_manifest['export_sha256'], corpora)
    by_canonical_chapter = collections.defaultdict(set)
    by_printed_chapter = collections.defaultdict(list)
    for name, verse in verses.items():
        printed = (verse['text'], verse['book'], verse['chapter'])
        by_printed_chapter[printed].append(name)
        for address in verse['canonicalAddresses']:
            by_canonical_chapter[tuple(address[:2])].add(printed)
    # Exclude only the exact source-bound fourteen editorial subscriptions already removed in v3.
    excluded = read(BASE / 'paratext-excluded.json')
    for note in excluded:
        name = note['verse']
        if name not in verses:
            continue
        body = ' '.join(verses[name]['body'].split())
        suffix = note['excluded_suffix']
        if not body.endswith(suffix):
            raise ValueError(f'Editorial exclusion no longer matches: {name}')
        verses[name]['body'] = body[:-len(suffix)].rstrip()
    held_catalogue = read(REVIEW / 'places/held-catalogue.json')
    catalogue = {row['slug']: copy.deepcopy(row) for group in ('entity', 'object') for row in held_catalogue[group]}
    for row in read(MAIN / 'Resources/Essenthos/person-presence.json')['sites']:
        catalogue.setdefault(row['slug'], {**row, 'catalogue_provenance': 'admitted person-presence sites'})
    definitions = read(REVIEW / 'places/current-records-review.json')['explicit_record_definitions']
    for row in definitions:
        record = row['record']
        if record.get('slug') in catalogue:
            catalogue[record['slug']].setdefault('held_definition', record)
    freeze(OUT / 'catalogue.json', dict(version=VERSION, records=list(catalogue.values())))
    paths = [BASE/'export.jsonl', BASE/'export-manifest.json', BASE/'jobs.jsonl',
             BASE/'paratext-excluded.json', REVIEW/'disagreements/decisions.json',
             REVIEW/'places/classifications.json', REVIEW/'places/held-catalogue.json',
             REVIEW/'places/current-records-review.json', MAIN/'Resources/Essenthos/person-presence.json',
             MAIN/'Resources/etcbc/LICENCE.md', MAIN/'Resources/etcbc/verse.tf',
             MAIN/'Resources/bible4u/LICENCE.md', MAIN/'Resources/Berean/LICENCE.md',
             MAIN/'Resources/KingJames2006/LICENCE.md', OUT/'catalogue.json']
    hashes = {str(path.relative_to(MAIN)).replace('\\', '/'): sha(path) for path in paths}
    cases = []
    for kind, row in items:
        original = row if kind == 'disagreement' else row['original']
        job = jobs[original['job_id']]
        focus, wanted, parallels = selection(job)
        printed = {chapter for address in wanted for chapter in by_canonical_chapter[address]}
        names = {name for chapter in printed for name in by_printed_chapter[chapter]}
        selected = {name: scripture(verses[name], focus) for name in names}
        # The actual original packet remains the authority for exact earlier quotations.
        for verse in job['verses']:
            name = verse['key']
            if name in selected and selected[name]['body'] != verse['body']:
                raise ValueError(f'Original packet text differs: {name}')
            selected[name] = {**verse, 'role': 'event' if name in job['focus'] else 'context'}
        candidates = {p['slug'] for p in job['places']}
        if kind == 'disagreement':
            candidates.add(row['place_slug'])
        else:
            resolution = row['name_resolution']
            candidates.update(resolution.get('alternative_slugs', []))
            if resolution.get('target_slug'):
                candidates.add(resolution['target_slug'])
        selected_verses = [selected[name] for name in sorted(selected)]
        cases.append(dict(case_id=row['case_id'], source_kind=kind, person=row['source_pointer']['person'],
            place_slug=row.get('place_slug') if kind == 'disagreement' else row['name_resolution'].get('target_slug'),
            place_name=row.get('place_name') if kind == 'disagreement' else original['place_name'],
            category=row.get('category') if kind == 'disagreement' else 'buried' if original['relation']=='buried' else 'present',
            original=row, focus_keys=[v['key'] for v in selected_verses if v['role']=='event'],
            verses=selected_verses, candidate_places=[catalogue[p] for p in sorted(candidates) if p in catalogue],
            context_metadata=dict(original_job_id=job['id'], original_focus_keys=job['focus'],
                requested_canonical_chapters=[list(a) for a in sorted(wanted)],
                explicit_parallel_chapters=[list(a) for a in sorted(parallels)],
                selection_is_retrieval_not_identity_proof=True,
                bhsa=diagnostic(selected_verses, focus),
                catalogue_path=str((OUT/'catalogue.json').relative_to(MAIN)).replace('\\', '/'),
                text_form='Raw frozen held verse text; no Hebrew normalization or translation',
                editorial_exclusions=excluded)))
    result = dict(version=VERSION, source_hashes=hashes, corpora=[texts[name] for name in sorted(texts)], cases=cases,
        source_provenance=dict(snapshot_at=export_manifest['at'], snapshot_revision=export_manifest['main_revision'],
            live_read=False, bhsa=dict(name='Biblia Hebraica Stuttgartensia Amstelodamensis',version='ETCBC 2021',
            attribution='Eep Talstra Centre for Bible and Computer, VU University Amsterdam',
            source_url='https://github.com/ETCBC/bhsa',doi='https://doi.org/10.17026/dans-z6y-skyh',
            licence='CC BY-NC 4.0',licence_url='https://creativecommons.org/licenses/by-nc/4.0/',
            distinction='MIT conversion-code badge does not license the BHSA data'),
            ubio_terms='Held bible4u text; original rights condition and conflicting Door43 notices retained in Resources/bible4u/LICENCE.md. Not labelled unconditionally public domain.',
            assembly='Whole printed chapters selected through held primary and secondary canonical placements; identity hints and name indices are metadata, not scriptural proof.'))
    validate(result)
    freeze(OUT/'contexts.json', result)
    freeze(OUT/'context-validation.json', dict(complete=True, count=201,
        contexts_sha256=sha(OUT/'contexts.json'), catalogue_sha256=sha(OUT/'catalogue.json'),
        source_code_sha256=sha(Path(__file__)),
        proofs=['all original cases and quotations retained', 'source hashes match',
                'all selected printed chapters retained with original and secondary canonical placements',
                'BHSA missing addresses are explicit', 'exact frozen replay refuses changed output']))
    print(json.dumps(summary(result), ensure_ascii=False))


def validate(result):
    assert len(result['cases']) == 201
    assert len({row['case_id'] for row in result['cases']}) == 201
    expected = {row['case_id'] for _, row in inventory()}
    assert {row['case_id'] for row in result['cases']} == expected
    for path, expected_hash in result['source_hashes'].items():
        assert sha(MAIN/path) == expected_hash, path
    for row in result['cases']:
        by_key = {verse['key']: verse for verse in row['verses']}
        assert len(by_key) == len(row['verses'])
        assert set(row['focus_keys']) <= by_key.keys()
        assert all(by_key[key]['role']=='event' for key in row['focus_keys'])
        assert row['context_metadata']['bhsa']['status'] in ('complete','partial','not_applicable_new_testament')
        original = row['original']
        flag = original['original_flag'] if row['source_kind']=='disagreement' else original['original']
        evidence = [q for reading in flag.get('readings',[flag])
                    for field in ('evidence','identity_evidence') for q in reading.get(field,[])]
        for quote in evidence:
            assert quote['verse'] in by_key, (row['case_id'],quote['verse'])
            assert quote['quote'] in by_key[quote['verse']]['body'], (row['case_id'],quote)
    return result


def summary(result):
    return dict(version=VERSION, cases=len(result['cases']),
        kinds=dict(collections.Counter(row['source_kind'] for row in result['cases'])),
        bhsa=dict(collections.Counter(row['context_metadata']['bhsa']['status'] for row in result['cases'])),
        cases_with_explicit_parallel=sum(bool(row['context_metadata']['explicit_parallel_chapters']) for row in result['cases']),
        unique_verse_keys=len({v['key'] for row in result['cases'] for v in row['verses']}),
        contexts_sha256=sha(OUT/'contexts.json') if (OUT/'contexts.json').exists() else None)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['prepare','verify'])
    args = parser.parse_args()
    if args.mode == 'prepare':
        build()
    else:
        # Rebuild from the hash-bound raw export, not merely from the already-produced JSON.
        build()
