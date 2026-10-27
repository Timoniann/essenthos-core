"""Freeze cited occupation-reading packets without a model call or corpus write."""
import argparse
import collections
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

MAIN = Path(__file__).resolve().parents[3]
BASE_NAME = "person-presence-sonnet55-2026-10-26-v3"
RUN_NAME = "person-occupations-sonnet55-2026-10-27-v1"
PRIORITY = ("KJV", "BSB", "NESTLE1904", "TISCH", "GEEZ81")
CORPORA = set(PRIORITY) | {"BHSA"}
AUTHOR_HINTS = {18: "job", 23: "isaiah", 24: "jeremiah-6", 26: "ezekiel", 27: "daniel-2",
 28: "hosea", 29: "joel-11", 30: "amos", 31: "obadiah-12", 32: "jonah", 33: "micah-4",
 34: "nahum", 35: "habakkuk", 36: "zephaniah-3", 37: "haggai", 38: "zechariah-13",
 39: "malachi", **{n: "saul-2" for n in range(45, 58)}, 59: "james-3", 60: "simon",
 61: "simon", 62: "john-2", 63: "john-2", 64: "john-2", 65: "judas-2"}

CURRENT_SQL = r"""
SET application_name = 'person-occupations-TSK-1250-export';
SET statement_timeout = '90s';
SET client_encoding = 'UTF8';
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('type','snapshot','at',transaction_timestamp(),'database',current_database());
SET statement_timeout = '90s';
SELECT json_build_object('type','entity','slug',e.slug,'kind',e.kind,'name',e.name,
 'distinguisher',e.distinguisher,'sex',e.sex,'sourceId',e.source_id,'source',e.source,
 'subtype',e.subtype,'aliasOf',a.slug)
 FROM entity e LEFT JOIN entity a ON a.id=e.another_name_for_entity_id
 WHERE e.kind IN ('person','title') ORDER BY e.slug;
SET statement_timeout = '90s';
SELECT json_build_object('type','reference','slug',e.slug,'book',ev.canonical_book,
 'chapter',ev.canonical_chapter,'verse',ev.canonical_verse,'names',bool_or(ev.names),
 'disputed',bool_and(ev.disputed),'sources',array_agg(DISTINCT ev.source ORDER BY ev.source))
 FROM entity_verse ev JOIN entity e ON e.id=ev.entity_id AND e.kind='person'
 GROUP BY e.slug,ev.canonical_book,ev.canonical_chapter,ev.canonical_verse
 ORDER BY e.slug,ev.canonical_book,ev.canonical_chapter,ev.canonical_verse;
SET statement_timeout = '90s';
SELECT json_build_object('type','name','slug',e.slug,'language',f.language,'form',f.form)
 FROM entity_name_form f JOIN entity e ON e.id=f.entity_id AND e.kind IN ('person','title')
 WHERE f.grammatical_case='nominative' ORDER BY e.slug,f.language,f.form;
SET statement_timeout = '90s';
SELECT json_build_object('type','descriptor','slug',e.slug,'relation',d.relation,'target',t.slug,
 'book',d.canonical_book,'chapter',d.canonical_chapter,'verse',d.canonical_verse,
 'source',d.source,'witness',d.witness)
 FROM entity_descriptor d JOIN entity e ON e.id=d.entity_id AND e.kind='person'
 JOIN entity t ON t.id=d.target_entity_id ORDER BY e.slug,d.ordinal;
SET statement_timeout = '90s';
SELECT json_build_object('type','title_bearer','slug',e.slug,'title',t.slug,
 'book',tb.canonical_book,'chapter',tb.canonical_chapter,'verse',tb.canonical_verse,
 'source',tb.source,'note',tb.note,'called',tb.called)
 FROM title_bearer tb JOIN entity e ON e.id=tb.bearer_entity_id
 JOIN entity t ON t.id=tb.title_entity_id WHERE e.kind='person' ORDER BY e.slug,t.slug;
COMMIT;
"""


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def key(row):
    return f'{row["text"]}:{row["book"]}:{row["chapter"]}:{row["verse"]}{row.get("label", "")}'


def addr(row):
    return tuple(row[k] for k in ('book', 'chapter', 'verse'))


def opening_candidate(book, refs):
    author = AUTHOR_HINTS.get(book)
    if book not in set(range(45, 58)) | {59, 60, 61, 65} or not author:
        return None
    return author if (book, 1, 1) in refs.get(author, {}) else None


def export_current(run):
    target = run / 'current-index.jsonl'
    if target.exists():
        raise ValueError('Current export already frozen; no repeated live query')
    run.mkdir(parents=True, exist_ok=True)
    part = target.with_suffix('.partial')
    command = ['docker', 'exec', '-i', 'essenthos-core-db-1', 'sh', '-c',
               'exec psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d essenthos_core']
    with part.open('wb') as output:
        result = subprocess.run(command, input=CURRENT_SQL.encode(), stdout=output,
                                stderr=subprocess.PIPE, timeout=600)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', errors='replace')[-2000:])
    counts = collections.Counter(json.loads(line)['type'] for line in part.read_text(encoding='utf-8').splitlines())
    if counts['snapshot'] != 1 or not counts['entity'] or not counts['reference']:
        raise ValueError('Incomplete current index export retained as partial')
    part.replace(target)
    manifest = dict(sha256=sha(target), counts=dict(counts),
                    sql_sha256=hashlib.sha256(CURRENT_SQL.encode()).hexdigest(),
                    source='bounded repeatable-read read-only Docker transaction')
    write(run/'current-index-manifest.json', manifest)
    print(json.dumps(manifest))


def index(path):
    rows = collections.defaultdict(list)
    with path.open(encoding='utf-8') as source:
        for line in source:
            row = json.loads(line)
            rows[row['type']].append(row)
    return rows


def load_sources(main, base):
    manifest = read(base/'export-manifest.json')
    if sha(base/'export.jsonl') != manifest['export_sha256']:
        raise ValueError('Frozen corpus export hash mismatch')
    old, verses, placements, texts = collections.defaultdict(list), {}, collections.defaultdict(list), {}
    with (base/'export.jsonl').open(encoding='utf-8') as source:
        for line in source:
            row = json.loads(line)
            kind = row['type']
            if kind in ('entity', 'reference'):
                old[kind].append(row)
            elif kind == 'text':
                texts[row['slug']] = row
            elif kind == 'verse' and row['text'] in CORPORA:
                name = key(row)
                if name in verses:
                    raise ValueError('Duplicate source verse ' + name)
                row['canonicalAddresses'] = [row['canonical']]
                verses[name] = row
            elif kind == 'placement' and row['text'] in CORPORA:
                placements[key(row)].append(row['canonical'])
    for name, extra in placements.items():
        if name not in verses:
            raise ValueError('Secondary mapping has no verse: ' + name)
        verses[name]['canonicalAddresses'] = sorted({tuple(a) for a in verses[name]['canonicalAddresses'] + extra})
    exclusions = read(base/'paratext-excluded.json')
    if len(exclusions) != 14:
        raise ValueError('Expected fourteen exact subscription exclusions')
    for row in exclusions:
        if sha(main/row['source']) != row['source_sha256']:
            raise ValueError('KJV paratext source changed')
        verse = verses[row['verse']]
        normalized = ' '.join(verse['body'].split())
        suffix = row['excluded_suffix']
        if not normalized.endswith(suffix):
            raise ValueError('KJV subscription suffix mismatch')
        verse['body'] = normalized[:-len(suffix)].rstrip()
    return old, verses, texts, manifest, exclusions


def delta(current, old):
    current_people = {r['slug']: r for r in current['entity'] if r['kind'] == 'person'}
    old_people = {r['slug']: r for r in old['entity'] if r['kind'] == 'person'}
    before = {(r['slug'], *addr(r)) for r in old['reference'] if r['slug'] in old_people}
    after_rows = {(r['slug'], *addr(r)): r for r in current['reference']}
    after = set(after_rows)
    return dict(added_person_slugs=sorted(current_people.keys()-old_people.keys()),
        removed_person_slugs=sorted(old_people.keys()-current_people.keys()),
        changed_person_names=[dict(slug=s, before=old_people[s]['name'], after=current_people[s]['name'])
            for s in sorted(current_people.keys() & old_people.keys()) if current_people[s]['name'] != old_people[s]['name']],
        added_references=[list(r) for r in sorted(after-before)],
        added_reference_sources=dict(collections.Counter(source for r in sorted(after-before) for source in after_rows[r]['sources'])),
        removed_references=[list(r) for r in sorted(before-after)],
        comparison_scope='Prior presence reference export excluded BibleData by% sources; current export includes every indexed source.')


def prepare(main, run):
    if (run/'jobs.jsonl').exists():
        raise ValueError('Packets already frozen; use verify to replay')
    base = main/'Resources/Essenthos/generation'/BASE_NAME
    current_manifest = read(run/'current-index-manifest.json')
    if sha(run/'current-index.jsonl') != current_manifest['sha256']:
        raise ValueError('Current index hash mismatch')
    current = index(run/'current-index.jsonl')
    old, verses, texts, export_manifest, exclusions = load_sources(main, base)
    comparison = delta(current, old)
    write(run/'index-delta.json', comparison)
    people = {r['slug']: r for r in current['entity'] if r['kind'] == 'person'}
    refs = collections.defaultdict(dict)
    names = collections.defaultdict(lambda: collections.defaultdict(list))
    descriptors, titles = collections.defaultdict(list), collections.defaultdict(list)
    for row in current['reference']:
        refs[row['slug']][addr(row)] = {k: v for k, v in row.items() if k not in ('type', 'slug')}
    for row in current['name']:
        names[row['slug']][row['language']].append(row['form'])
    for row in current['descriptor']:
        descriptors[row['slug']].append({k: v for k, v in row.items() if k not in ('type', 'slug')})
    for row in current['title_bearer']:
        titles[row['slug']].append({k: v for k, v in row.items() if k not in ('type', 'slug')})
    by_address, chapters = collections.defaultdict(list), collections.defaultdict(list)
    for name, row in verses.items():
        chapters[(row['text'], row['book'], row['chapter'])].append(name)
        for address in row['canonicalAddresses']:
            by_address[tuple(address)].append(name)
    for chapter in chapters.values():
        chapter.sort(key=lambda name: (verses[name].get('sequence', 0), verses[name]['verse'], verses[name].get('label', '')))
    selected, missing = collections.defaultdict(lambda: collections.defaultdict(set)), []
    for slug in sorted(people):
        for address in sorted(refs[slug]):
            candidates = by_address.get(address, [])
            if not candidates:
                missing.append(dict(person=slug, canonical=list(address)))
                continue
            primary = min(candidates, key=lambda name: (PRIORITY.index(verses[name]['text']) if verses[name]['text'] in PRIORITY else 99, name))
            row = verses[primary]
            selected[(row['text'], row['book'], row['chapter'])][slug].add(address)
    write(run/'missing-references.json', missing)
    if missing:
        raise ValueError(f'{len(missing)} current references lack a selected frozen witness; explicit diagnostic retained')
    assigned = collections.defaultdict(list)
    reference_jobs = collections.defaultdict(lambda: collections.defaultdict(list))
    jobs = []
    parallel_missing = collections.Counter()
    for (text, book, chapter), targets in sorted(selected.items(), key=lambda row: (row[0][1], row[0][2], row[0][0])):
        author = opening_candidate(book, refs)
        if author:
            targets.setdefault(author, set())
        primary_focus = chapters[(text, book, chapter)]
        focus_addresses = {tuple(a) for name in primary_focus for a in verses[name]['canonicalAddresses']}
        focus_chapters = {a[:2] for a in focus_addresses}
        event_keys = set(primary_focus)
        parallel_editions = ['BSB'] + (['BHSA'] if any(a[0] <= 39 for a in focus_addresses) else ['NESTLE1904'] if any(40 <= a[0] <= 66 for a in focus_addresses) else [])
        diagnostics = {}
        for edition in parallel_editions:
            applicable = {a for a in focus_addresses if (a[0] <= 39 if edition == 'BHSA' else 40 <= a[0] <= 66 if edition == 'NESTLE1904' else True)}
            parallel_keys = {name for a in applicable for name in by_address.get(a, []) if verses[name]['text'] == edition}
            printed = {(verses[name]['text'], verses[name]['book'], verses[name]['chapter']) for name in parallel_keys}
            for printed_chapter in printed:
                event_keys.update(chapters[printed_chapter])
            covered = {tuple(a) for name in parallel_keys for a in verses[name]['canonicalAddresses']}
            absent = sorted(applicable-covered)
            diagnostics[edition] = dict(requested=len(applicable), missing=[list(a) for a in absent])
            parallel_missing[edition] += len(absent)
        context_keys = {name for neighbor in (chapter-1, chapter+1) for name in chapters.get((text, book, neighbor), [])} - event_keys
        identity_keys = set(chapters.get((text, book, 1), [])[:8]) - event_keys if author and chapter > 1 else set()
        context_keys -= identity_keys
        all_keys = event_keys | context_keys | identity_keys
        ordered_keys = sorted(all_keys, key=lambda name: (verses[name]['text'], verses[name]['book'], verses[name]['chapter'], verses[name].get('sequence', 0), name))
        packet_verses = [dict(key=name, body=verses[name]['body'], canonical=verses[name]['canonical'],
            canonicalAddresses=verses[name]['canonicalAddresses'], role='event' if name in event_keys else 'identity' if name in identity_keys else 'context') for name in ordered_keys]
        for offset in range(0, len(targets), 20):
            group = sorted(targets)[offset:offset+20]
            job_id = f'{text}-{book:03d}-{chapter:03d}-{offset//20:02d}'
            persons = []
            for slug in group:
                persons.append(dict(slug=slug, name=people[slug]['name'], aliasOf=people[slug]['aliasOf'],
                    identity_hint=people[slug]['distinguisher'], localNames=names[slug],
                    indexed_references=[list(a) for a in sorted(targets[slug])],
                    identity_candidate_source='explicit opening name indexed at canonical 1:1; read the supplied words and resolve identity independently' if slug == author else 'current mention index',
                    index_annotations=[refs[slug][a] for a in sorted(targets[slug])],
                    metadata_is_not_evidence=True))
                assigned[slug].append(job_id)
                for address in targets[slug]:
                    reference_jobs[slug][address].append(job_id)
            jobs.append(dict(job_id=job_id, persons=persons, verses=packet_verses,
                focus_keys=sorted(event_keys), source_metadata=dict(primary_text=text,
                    printed_focus=[book, chapter], canonical_focus_chapters=sorted(focus_chapters),
                    parallel_diagnostics=diagnostics, identity_author_hint=author,
                    originals_availability=dict(BHSA=dict(status='not_applicable' if book > 39 else 'partial' if diagnostics.get('BHSA',{}).get('missing') else 'complete',
                        keys=[name for name in sorted(event_keys) if name.startswith('BHSA:')]),
                        NESTLE1904=dict(status='not_applicable' if not 40 <= book <= 66 else 'partial' if diagnostics.get('NESTLE1904',{}).get('missing') else 'complete',
                        keys=[name for name in sorted(event_keys) if name.startswith('NESTLE1904:')])),
                    identity_hint_is_not_authorship_proof=True)))
    with (run/'jobs.jsonl').open('w', encoding='utf-8', newline='\n') as output:
        for job in jobs:
            output.write(json.dumps(job, ensure_ascii=False, separators=(',', ':')) + '\n')
    inventory = [dict(**{k:v for k,v in person.items() if k != 'type'}, localNames=names[slug],
        references=[refs[slug][a] for a in sorted(refs[slug])], jobs=assigned[slug],
        existing_descriptors=descriptors[slug], existing_titles=titles[slug],
        metadata_is_not_evidence=True, coverage='indexed_passages' if refs[slug] else 'no_indexed_references')
        for slug, person in sorted(people.items())]
    write(run/'persons.json', inventory)
    write(run/'coverage.json', [dict(slug=slug, total_references=len(refs[slug]),
        references=[dict(canonical=list(a), jobs=reference_jobs[slug][a]) for a in sorted(refs[slug])],
        jobs=assigned[slug], missing=[]) for slug in sorted(people)])
    write(run/'existing-roles.json', dict(definitions=[r for r in current['entity'] if r['kind']=='title'],
        bearers=current['title_bearer'], note='Snapshot metadata for deduplication; independently cite scripture for every occupation/office/ministry.'))
    write(run/'paratext-excluded.json', exclusions)
    pilot_chapters = {(1,4),(2,1),(2,31),(9,16),(30,7),(40,4),(43,12),(44,16),(44,18),(46,4),(51,4)}
    write(run/'pilot-candidate-jobs.json', [job['job_id'] for job in jobs
        if tuple(job['source_metadata']['printed_focus']) in pilot_chapters])
    source_paths = [base/'export.jsonl', base/'export-manifest.json', base/'jobs.jsonl', base/'persons.json', base/'manifest.json',
                    base/'paratext-excluded.json', run/'current-index.jsonl', run/'current-index-manifest.json']
    licence_paths = ['Resources/bible4u/LICENCE.md', 'Resources/KingJames2006/LICENCE.md',
        'Resources/etcbc/LICENCE.md', 'Resources/Nestle1904/LICENCE.md', 'Resources/Berean/LICENCE.md',
        'Resources/Tischendorf/LICENCE.md', 'Resources/BetaMasaheft/LICENCE.md',
        'Resources/BibleData2026/LICENCE.md']
    source_paths += [main/p for p in licence_paths]
    copied_licences = []
    for relative in licence_paths:
        destination = run/'licences'/Path(relative).parent.name/'LICENCE.md'
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(main/relative, destination)
        copied_licences.append(str(destination.relative_to(run)).replace('\\','/'))
    source_paths += [main/r['source'] for r in exclusions]
    for filename, field in [('jobs.jsonl','jobs_sha256'), ('persons.json','persons_sha256')]:
        if sha(base/filename) != read(base/'manifest.json')[field]:
            raise ValueError('Prior frozen artifact changed: '+filename)
    manifest = dict(version='occupations-input-v1', at=dt.datetime.now(dt.timezone.utc).isoformat(),
        source_hashes={str(path.relative_to(main)).replace('\\','/'):sha(path) for path in source_paths},
        source_code_sha256=sha(__file__), frozen_corpus=export_manifest,
        coverage=dict(persons=len(people), aliases=sum(bool(p['aliasOf']) for p in people.values()),
            persons_with_references=sum(bool(refs[s]) for s in people), zero_reference_persons=[s for s in sorted(people) if not refs[s]],
            all_indexed_references=sum(len(refs[s]) for s in people), missing_references=missing,
            jobs=len(jobs), primary_corpora=dict(collections.Counter(j['source_metadata']['primary_text'] for j in jobs)),
            parallel_missing_addresses=dict(parallel_missing)),
        corpora={s:texts[s] for s in sorted(CORPORA)}, licences=licence_paths,
        licensing=dict(BHSA='ETCBC BHSA 2021, CC BY-NC 4.0; data is not MIT',
            NESTLE1904='biblicalhumanities.org morph component, CC0 1.0', BSB='Bible Hub/Berean Bible, public domain 2023-04-30',
            KJV='bible4u permission with unmodified Biblical-content condition; source-bound CrossWire/eBible repairs and superscriptions public domain',
            TISCH='Tischendorf eighth edition, Sandborg-Petersen release 2.8, public domain',
            person_index='BibleData current master, Brady Stephenson et al., CC BY 4.0; current Essenthos additions individually credited in the snapshot',
            GEEZ81='Beta maṣāḥǝft/University of Hamburg, CC BY-SA 4.0 TEI; HaCohen/Jerabek transcription notices add NonCommercial and copyright/permission retention where applicable; see copied full notice'),
        policy=dict(index='Every current person, including aliases and zero-reference records; every entity_verse source, no keyword/genre/name-only filter.',
            witness='One preferred held reading per reference plus complete printed focus chapters in BHSA OT/NESTLE1904 NT and BSB where held; primary previous/next chapters and identity-only opening context.',
            limitations='Not every translation/variant or unindexed mention is independently reread. Missing parallel addresses are explicit, not fabricated. No Hebrew NT is invented. No occupation conclusions are copied from metadata.',
            identity='Names, descriptors, aliases, title definitions and author hints are metadata only, never scriptural evidence.'),
        outputs={name:sha(run/name) for name in ['jobs.jsonl','persons.json','coverage.json','existing-roles.json','paratext-excluded.json','index-delta.json','missing-references.json','pilot-candidate-jobs.json'] + copied_licences},
        input_bytes=(run/'jobs.jsonl').stat().st_size,
        input_chars=sum(len(json.dumps(j,ensure_ascii=False,separators=(',',':'))) for j in jobs))
    write(run/'source-manifest.json', manifest)
    verify(main, run)
    print(json.dumps({k:manifest[k] for k in ('coverage','input_bytes','input_chars')}, ensure_ascii=False))


def verify(main, run):
    manifest = read(run/'source-manifest.json')
    if sha(__file__) != manifest['source_code_sha256']:
        raise ValueError('Packet preparation source changed since freeze')
    for relative, expected in manifest['source_hashes'].items():
        if sha(main/relative) != expected:
            raise ValueError('Changed source '+relative)
    for name, expected in manifest['outputs'].items():
        if sha(run/name) != expected:
            raise ValueError('Changed output '+name)
    inventory = read(run/'persons.json')
    jobs = [json.loads(line) for line in (run/'jobs.jsonl').read_text(encoding='utf-8').splitlines()]
    validate_coverage(inventory, jobs)
    print(json.dumps(dict(valid=True, persons=len(inventory), jobs=len(jobs), jobs_sha256=sha(run/'jobs.jsonl'))))


def validate_coverage(inventory, jobs):
    people = {p['slug']:p for p in inventory}
    if len(people) != len(inventory) or len({j['job_id'] for j in jobs}) != len(jobs):
        raise ValueError('Duplicate inventory or job')
    covered, assigned = collections.defaultdict(set), collections.defaultdict(set)
    for job in jobs:
        verses = {v['key']:v for v in job['verses']}
        if len(verses) != len(job['verses']) or not set(job['focus_keys']) <= verses.keys():
            raise ValueError('Duplicate verse or missing focus')
        event_addresses = {tuple(a) for name in job['focus_keys'] for a in verses[name]['canonicalAddresses']}
        if any(verses[name]['role'] != 'event' for name in job['focus_keys']):
            raise ValueError('Focus must be event text')
        if any(v['role'] not in ('event','context','identity') or not v['canonicalAddresses'] for v in verses.values()):
            raise ValueError('Invalid verse role or missing mapping')
        for person in job['persons']:
            slug = person['slug']
            if slug not in people:
                raise ValueError('Unknown job person')
            required = {tuple(a) for a in person['indexed_references']}
            if not required <= event_addresses:
                raise ValueError('Indexed reference not in event text')
            covered[slug].update(required)
            assigned[slug].add(job['job_id'])
    for slug, person in people.items():
        if covered[slug] != {addr(r) for r in person['references']}:
            raise ValueError('Incomplete reference coverage '+slug)
        if assigned[slug] != set(person['jobs']):
            raise ValueError('Inventory job assignment mismatch '+slug)
    return True


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['export-current','prepare','verify'])
    parser.add_argument('--main', type=Path, default=MAIN)
    args = parser.parse_args()
    run = args.main/'Resources/Essenthos/generation'/RUN_NAME
    if args.mode == 'export-current':
        export_current(run)
    elif args.mode == 'prepare':
        prepare(args.main, run)
    else:
        verify(args.main, run)
