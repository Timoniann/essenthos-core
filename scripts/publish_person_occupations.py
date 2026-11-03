# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
"""Source-bound review and publication of the completed occupation readings."""
import argparse
import copy
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil
import unicodedata

MAIN = Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
RUN = MAIN / 'Resources/Essenthos/generation/occupation-full-publication-2026-10-27-v1'
V2 = MAIN / 'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v2'
V3 = MAIN / 'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v3'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stable(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True).encode()).hexdigest()[:16]


def nfc(value):
    return unicodedata.normalize('NFC', value)


def prepare():
    if (RUN / 'manifest.json').exists():
        raise ValueError('Review snapshot exists; do not replace frozen inputs.')
    snapshot = RUN / 'snapshot'
    files = []
    def retain(source, target):
        destination = snapshot / target
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        files.append({'source': source.relative_to(MAIN).as_posix(),
                      'snapshot': destination.relative_to(RUN).as_posix(), 'sha256': sha(destination)})
    for folder, source in (('v2', V2), ('v3', V3)):
        names = ('persons.json', 'source-manifest.json', 'run-manifest.json', 'occupation-policy.json') if folder == 'v2' else ('manifest.json', 'results.json', 'status.json', 'checkpoint.json')
        for name in names:
            retain(source / name, folder + '/' + name)
    results = read(snapshot / 'v3/results.json')
    people = {p['slug']: p for p in read(snapshot / 'v2/persons.json')}
    packets = {}
    for parent in read(snapshot / 'v3/manifest.json')['parents']:
        job = parent['job_id']
        source = V2 / 'draft/inputs' / (job + '.json')
        retain(source, 'packets/' + source.name)
        packets[job] = read(source)
    retained = []
    roles = {}
    observations = []
    for person in results['persons']:
        grouped = {}
        for index, finding in enumerate(person['findings']):
            category, key = finding['category'], finding['role_key']
            ident = stable([person['person'], category, key])
            group = grouped.setdefault(ident, {'id': ident, 'person': person['person'],
                'name': people[person['person']]['name'], 'category': category, 'role_key': key, 'contexts': []})
            packet = packets[finding.get('parent_job', finding['job_id'])]
            verses = {v['key']: v for v in packet['verses']}
            context = {'id': f"{ident}-{len(group['contexts']):03d}", 'finding': finding, 'sources': [], 'errors': []}
            for kind, quotes in (('profession', finding['evidence']), ('identity', finding['identity_evidence'])):
                for quote in quotes:
                    verse = verses.get(quote['verse'])
                    if verse is None or nfc(quote['quote']) not in nfc(verse['body']):
                        context['errors'].append('unbound_quote:' + quote['verse'])
                        continue
                    context['sources'].append({'kind': kind, 'key': quote['verse'], 'quote': nfc(quote['quote']),
                        'body': nfc(verse['body']), 'canonical': verse['canonical'],
                        'canonicalAddresses': verse.get('canonicalAddresses', [verse['canonical']]),
                        'focus': quote['verse'] in packet['focus_keys']})
            group['contexts'].append(context)
            role = roles.setdefault(category + ':' + key, {'category': category, 'role_key': key,
                'labels_en': [], 'labels_uk': [], 'groups': [], 'examples': []})
            for field, target in (('label_en', 'labels_en'), ('label_uk', 'labels_uk')):
                if finding[field] not in role[target]:
                    role[target].append(finding[field])
            if ident not in role['groups']:
                role['groups'].append(ident)
            if len(role['examples']) < 3:
                role['examples'].append({'person': person['person'], 'context': context['id'],
                    'basis': finding['basis'], 'evidence': context['sources']})
        retained.extend(grouped.values())
        for field in ('needs_review', 'not_occupations'):
            for index, observation in enumerate(person[field]):
                observations.append({'id': stable([person['person'], field, index, observation]),
                    'person': person['person'], 'type': field, 'observation': observation})
    retained.sort(key=lambda g: (g['category'], g['role_key'], g['person']))
    for index, group in enumerate(retained):
        group['part'] = 'a' if index % 2 == 0 else 'b'
    write(RUN / 'groups.json', retained)
    write(RUN / 'roles.json', sorted(roles.values(), key=lambda r: (r['category'], r['role_key'])))
    write(RUN / 'observations.json', observations)
    for name in ('groups.json', 'roles.json', 'observations.json'):
        files.append({'snapshot': name, 'sha256': sha(RUN / name)})
    write(RUN / 'manifest.json', {'task': 'TSK-1251', 'source_task': 'TSK-1250',
        'files': files, 'groups': len(retained), 'roles': len(roles), 'contexts': sum(len(g['contexts']) for g in retained),
        'observations': len(observations), 'persons': len(people),
        'source_errors': sum(bool(c['errors']) for g in retained for c in g['contexts']),
        'owner_instruction': 'Обробити все й перенести підтверджені професії в основу', 'paid_calls': 0})
    print(json.dumps({k: v for k, v in read(RUN / 'manifest.json').items() if k != 'files'}, ensure_ascii=False))


def inspect(args):
    groups = read(RUN / 'groups.json')
    if args.part != 'all':
        groups = [g for g in groups if g['part'] == args.part]
    if args.id:
        groups = [g for g in groups if g['id'] == args.id]
    for group in groups[args.skip:args.skip + args.take]:
        contexts = sorted(group['contexts'], key=lambda c: (c['finding']['basis'] != 'explicit', bool(c['errors']), len(c['finding']['evidence'])))
        print(json.dumps({'id': group['id'], 'person': group['person'], 'name': group['name'],
            'raw_role': group['category'] + ':' + group['role_key'], 'contexts_total': len(contexts),
            'contexts': [{'id': c['id'], 'basis': c['finding']['basis'], 'time': c['finding']['time_scope'],
                'source_term': c['finding']['source_term'], 'sources': c['sources'], 'errors': c['errors']}
                for c in (contexts if args.id else contexts[:2])]}, ensure_ascii=False))


def inventory(args):
    roles = read(RUN / 'roles.json')
    for role in roles[args.skip:args.skip + args.take]:
        print(json.dumps({'raw_role': role['category'] + ':' + role['role_key'],
            'groups': len(role['groups']), 'labels_en': role['labels_en'], 'labels_uk': role['labels_uk'],
            'examples': role['examples'][:1]}, ensure_ascii=False))


def terminal():
    target = RUN / 'terminal-review.json'
    if target.exists():
        raise ValueError('Terminal source review already exists; verify without replacing it.')
    packet_path = V3 / 'check/inputs/KJV-013-011-00-v02.json'
    packet = read(packet_path)
    verses = {v['key']: v for v in packet['verses']}
    explanations = {
        'david': '1CH11:3 names David and anoints him king of Israel; the original draft quotation matches the Hebrew. The failed checker substituted Cyrillic д for Hebrew ד. Source review uses the unchanged correct draft and full source, not a rewritten raw answer.',
        'benaiah-2': '1CH11:26 introduces the valiant men of the armies. The continuing list in27-31 names Benaiah the Pirathonite, distinguishing him from Jehoiada\'s son. This is membership of the military retinue, not inference from a solitary deed.',
        'eleazar-3': '1CH11:10 introduces David\'s mighty men;11:12 names Eleazar son of Dodo the Ahohite among the Three.11:13-14 explicitly sets this titled group in battle. The source supports an elite warrior without inventing a modern rank.',
        'elhanan-son-of-dodo': '1CH11:26 itself names Elhanan son of Dodo of Bethlehem under the valiant men of the armies. The father and locality distinguish him from the other Elhanan.',
    }
    findings = []
    for fact in packet['draft_facts']:
        finding = copy.deepcopy(fact['finding'])
        sources = []
        for kind, quotes in (('profession', finding['evidence']), ('identity', finding['identity_evidence'])):
            for quote in quotes:
                verse = verses[quote['verse']]
                if nfc(quote['quote']) not in nfc(verse['body']):
                    raise ValueError('Original terminal draft no longer quotes the source exactly.')
                sources.append({'kind': kind, 'key': quote['verse'], 'quote': nfc(quote['quote']),
                    'body': nfc(verse['body']), 'canonical': verse['canonical'],
                    'canonicalAddresses': verse.get('canonicalAddresses', [verse['canonical']]),
                    'focus': quote['verse'] in packet['focus_keys']})
        findings.append({'id': 'terminal-' + fact['id'], 'person': fact['person'],
            'category': finding['category'], 'role_key': finding['role_key'], 'finding': finding,
            'sources': sources, 'why': explanations[fact['person']], 'verdict': 'admit'})
    retained = []
    for source in [packet_path, *sorted((V3 / 'check/attempts').glob('KJV-013-011-00-v02-*.json'))]:
        destination = RUN / 'terminal-source' / source.name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        retained.append({'path': destination.relative_to(RUN).as_posix(), 'sha256': sha(destination)})
    write(target, {'reviewedBy': 'Codex lead, independent source reading',
        'sourceManifestSha256': sha(RUN / 'manifest.json'), 'paid_calls': 0,
        'scope': 'Four source-adjudicated facts; original paid checkpoint, attempts and collector state remain unchanged.',
        'source_keys_read': ['KJV:13:11:' + str(v) for v in (3, 10, 12, 13, 14, 26, 27, 28, 29, 30, 31)]
            + ['BHSA:13:11:' + str(v) for v in (3, 10, 12, 13, 14, 26, 31)],
        'files': retained, 'findings': findings})
    print(json.dumps({'terminal_source_reviewed': len(findings), 'raw_changed': False, 'paid_calls': 0}))


def observation_ledger():
    observations = read(RUN / 'observations.json')
    decisions = []
    for row in observations:
        excluded = row['type'] == 'not_occupations'
        decisions.append({'id': row['id'], 'person': row['person'],
            'disposition': 'not_promoted_nonoccupation' if excluded else 'held_unconfirmed_observation',
            'basis': 'Original run explicitly separated this from positive vocation findings. It is not a source-reviewed positive assertion and is not promoted by normalization.',
            'original_type': row['type'], 'original_kind': row['observation'].get('kind'),
            'original_reason_retained': True})
    write(RUN / 'observation-dispositions.json', {'reviewedBy': 'Codex lead, conservative publication boundary',
        'sourceManifestSha256': sha(RUN / 'manifest.json'),
        'not_a_claim_that_all_observations_are_false': True,
        'policy': 'Only positive candidates receive source admission. Unconfirmed observations remain explicit held evidence; moral/status/one-off/metaphorical observations are not manufactured into professions. The owner\'s moral display question remains open.',
        'decisions': decisions})
    print(json.dumps({'observations_accounted': len(decisions), 'held': sum(r['disposition'].startswith('held') for r in decisions),
        'not_promoted_nonoccupation': sum(r['disposition'].startswith('not_promoted') for r in decisions)}))


def licences():
    entries = []
    preview = MAIN / 'Resources/Essenthos/generation/occupation-interface-preview-2026-10-27-v1/snapshot/licences'
    for component in ('bible4u', 'KingJames2006', 'etcbc', 'Nestle1904', 'BibleData2026', 'Berean'):
        source = MAIN / 'Resources/Berean/LICENCE.md' if component == 'Berean' else preview / component / 'LICENCE.md'
        target = RUN / 'snapshot/licences' / component / 'LICENCE.md'
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() and sha(target) != sha(source):
            raise ValueError('Retained source licence bytes changed.')
        shutil.copyfile(source, target)
        entries.append({'path': target.relative_to(RUN).as_posix(), 'source': str(source), 'sha256': sha(target)})
    write(RUN / 'licence-manifest.json', {'components': entries,
        'basis': 'Original corpus component terms retained; BSB public-domain publisher declaration read by lead. No Ge\'ez source is admitted.'})
    print(json.dumps({'source_licences_retained': len(entries)}))


def verify_snapshot():
    manifest = read(RUN / 'manifest.json')
    for entry in manifest['files']:
        if sha(RUN / entry['snapshot']) != entry['sha256']:
            raise ValueError('Frozen review source changed: ' + entry['snapshot'])
    return manifest


def validate_taxonomy(taxonomy, roles):
    expected = {r['category'] + ':' + r['role_key'] for r in roles}
    mappings = taxonomy['mappings']
    if len(mappings) != len(expected) or {m['raw_role'] for m in mappings} != expected:
        raise ValueError('Every frozen raw role needs exactly one taxonomy decision.')
    concepts = {c['slug']: c for c in taxonomy['occupations']}
    if len(concepts) != len(taxonomy['occupations']):
        raise ValueError('Canonical occupation slugs repeat.')
    for concept in concepts.values():
        if concept['category'] not in ('trade', 'office', 'military_role', 'religious_role'):
            raise ValueError('Unknown canonical occupation category.')
        if set(concept['labels']) != {'eng', 'ukr', 'deu', 'spa'}:
            raise ValueError('Reader labels need all four languages.')
        for label in concept['labels'].values():
            if not all(isinstance(label.get(k), str) and label[k].strip() for k in ('name', 'masculine', 'feminine')):
                raise ValueError('Missing grammatical reader label.')
    for mapping in mappings:
        if not mapping.get('why') or 'pending' in mapping['why'].lower():
            raise ValueError('Taxonomy still has an unresolved preparation placeholder.')
        if mapping['slug'] is not None and mapping['slug'] not in concepts:
            raise ValueError('Taxonomy refers to an absent canonical concept.')
    return {m['raw_role']: m for m in mappings}, concepts


def validate_decisions(groups, documents):
    expected = {g['id']: g for g in groups}
    decisions = {}
    for document in documents:
        if document.get('sourceManifestSha256') != sha(RUN / 'manifest.json') or not document.get('reviewedBy'):
            raise ValueError('Source decisions must pin their frozen manifest and reviewer.')
        for decision in document['decisions']:
            ident = decision['id']
            if ident in decisions or ident not in expected:
                raise ValueError('Duplicate or unknown person-role review decision.')
            group = expected[ident]
            contexts = {c['id']: c for c in group['contexts']}
            if decision['verdict'] not in ('admit', 'hold', 'exclude') or not decision.get('why'):
                raise ValueError('Every person-role review needs an explicit source disposition.')
            selected = decision['context_ids']
            if decision['verdict'] == 'admit' and not selected:
                raise ValueError('Admission needs a source-reviewed context.')
            if decision['verdict'] != 'admit' and selected:
                raise ValueError('Held claims cannot retain admitted contexts.')
            if len(set(selected)) != len(selected) or any(c not in contexts for c in selected):
                raise ValueError('Selected context is not this person\'s frozen finding.')
            for context_id in selected:
                context = contexts[context_id]
                if context['errors'] or not any(s['kind'] == 'profession' for s in context['sources']):
                    raise ValueError('Unbound or empty profession source cannot be admitted.')
            decisions[ident] = {**decision, 'reviewedBy': document['reviewedBy']}
    if set(decisions) != set(expected):
        raise ValueError(f'Person-role review incomplete: {len(decisions)}/{len(expected)}.')
    return decisions


def witness(source):
    text, book, chapter, verse = source['key'].split(':')
    body = source['body']
    if nfc(source['quote']) not in nfc(body) or nfc(body) != body:
        raise ValueError('Publication quote is not an exact NFC source substring.')
    canonical = source['canonical']
    if canonical not in source['canonicalAddresses']:
        raise ValueError('Publication canonical address is not a held source placement.')
    return {'kind': source['kind'], 'text': text, 'reference': f'{int(book)} {int(chapter)}:{int(verse)}',
        'canonicalReference': f'{canonical[0]} {canonical[1]}:{canonical[2]}', 'quote': source['quote'],
        'sourceText': body, 'sourceSha256': hashlib.sha256(body.encode()).hexdigest()}


def compile_publication():
    manifest = verify_snapshot()
    groups = read(RUN / 'groups.json')
    roles = read(RUN / 'roles.json')
    taxonomy = read(RUN / 'taxonomy.json')
    if taxonomy['sourceManifestSha256'] != sha(RUN / 'manifest.json'):
        raise ValueError('Taxonomy source manifest changed.')
    mappings, concepts = validate_taxonomy(taxonomy, roles)
    review_files = [RUN / 'decisions/part-a.json', RUN / 'decisions/part-b.json']
    decisions = validate_decisions(groups, [read(p) for p in review_files])
    previous_path = MAIN / 'Resources/Essenthos/generation/occupation-publication-2026-10-27-v1/bundle.json'
    if sha(previous_path) != 'ccc0947cb8b6eac8074de644cfab4a007ef7431cd38403ce193d1530b61dc6e1':
        raise ValueError('Previously admitted publication changed.')
    previous = read(previous_path)
    assertions = copy.deepcopy(previous['assertions'])
    exclusions = []
    accepted_groups = 0
    def admit(person, raw_role, sources, origin):
        slug = mappings[raw_role]['slug']
        if slug is None:
            return False
        assertions.append({'personSlug': person, 'occupationSlug': slug,
            'witnesses': [witness(s) for s in sources], 'origin': origin})
        return True
    for group in groups:
        decision = decisions[group['id']]
        mapping = mappings[group['category'] + ':' + group['role_key']]
        if decision['verdict'] != 'admit' or mapping['slug'] is None:
            exclusions.append({'id': group['id'], 'person': group['person'], 'raw_role': group['category'] + ':' + group['role_key'],
                'verdict': decision['verdict'] if decision['verdict'] != 'admit' else 'taxonomy_hold',
                'why': decision['why'] if decision['verdict'] != 'admit' else mapping['why']})
            continue
        contexts = {c['id']: c for c in group['contexts']}
        for context_id in decision['context_ids']:
            context = contexts[context_id]
            finding = context['finding']
            admit(group['person'], group['category'] + ':' + group['role_key'], context['sources'],
                {k: finding.get(k) for k in ('job_id', 'parent_job', 'draft_message_id', 'check_message_id', 'model')}
                | {'sourceGroup': group['id'], 'sourceContext': context_id, 'reviewedBy': decision['reviewedBy'],
                   'sourceReview': decision['why'], 'timeScope': finding['time_scope']})
        accepted_groups += 1
    terminal_review = read(RUN / 'terminal-review.json')
    if terminal_review['sourceManifestSha256'] != sha(RUN / 'manifest.json'):
        raise ValueError('Terminal review changed source manifest.')
    for entry in terminal_review['files']:
        if sha(RUN / entry['path']) != entry['sha256']:
            raise ValueError('Terminal raw/source evidence changed.')
    for finding in terminal_review['findings']:
        admit(finding['person'], finding['category'] + ':' + finding['role_key'], finding['sources'],
            {'model': 'claude-sonnet-5-5', 'job_id': 'KJV-013-011-00-v02',
             'sourceContext': finding['id'], 'reviewedBy': terminal_review['reviewedBy'], 'sourceReview': finding['why'],
             'recovery': 'Independent source review; original draft is exact, checker Cyrillic character error retained raw.'})
    source_adjudication_path = RUN / 'source-adjudications.json'
    source_adjudications = read(source_adjudication_path)
    if source_adjudications['sourceManifestSha256'] != sha(RUN / 'manifest.json'):
        raise ValueError('Additional source adjudication is not bound to the frozen source.')
    group_index = {g['id']: g for g in groups}
    split_groups = set()
    manual_assertions = 0
    for decision in source_adjudications['decisions']:
        group = group_index[decision['group_id']]
        context = next(c for c in group['contexts'] if c['id'] == decision['context_id'])
        if context['errors'] or not decision.get('why'):
            raise ValueError('Manual source adjudication has an invalid source.')
        selected_sources = [dict(s, quote=s['body']) for s in context['sources']
            if s['kind'] == 'profession' and s['key'] in decision['source_keys']]
        if set(s['key'] for s in selected_sources) != set(decision['source_keys']):
            raise ValueError('Manual adjudication refers to a source it did not retain.')
        for slug in decision['occupationSlugs']:
            if slug not in concepts:
                raise ValueError('Manual source adjudication names an unknown reader concept.')
            assertions.append({'personSlug': group['person'], 'occupationSlug': slug,
                'witnesses': [witness(s) for s in selected_sources],
                'origin': {'sourceGroup': group['id'], 'sourceContext': context['id'],
                    'reviewedBy': source_adjudications['reviewedBy'], 'sourceReview': decision['why'],
                    'adjudication': decision['id']}})
            manual_assertions += 1
        split_groups.add(group['id'])
    exclusions = [e for e in exclusions if e['id'] not in split_groups]
    used = {a['occupationSlug'] for a in assertions}
    for concept in previous['occupations']:
        if concept['slug'] not in concepts:
            concepts[concept['slug']] = concept
    if not used.issubset(concepts):
        raise ValueError('An admitted claim has no canonical four-language concept.')
    licence_manifest_path = RUN / 'licence-manifest.json'
    for entry in read(licence_manifest_path)['components']:
        if sha(RUN / entry['path']) != entry['sha256']:
            raise ValueError('Source licence evidence changed.')
    if any(s['text'] == 'GEEZ81' for a in assertions for s in a['witnesses']):
        raise ValueError('A Ge\'ez-only claim requires a separate original-language/source admission.')
    provenance_files = [RUN / 'manifest.json', RUN / 'taxonomy.json', RUN / 'terminal-review.json',
        RUN / 'observation-dispositions.json', source_adjudication_path, previous_path, *review_files]
    provenance_files.append(licence_manifest_path)
    source_manifest = {'task': 'TSK-1251', 'source_task': 'TSK-1250',
        'owner_authorization': 'Process the full result and integrate confirmed occupations in the main corpus,2026-10-27.',
        'reviewed_files': [{'path': str(p), 'sha256': sha(p)} for p in provenance_files],
        'source_mechanism': 'Frozen exact primary/secondary placements and NFC verse bytes, independently reviewed person-role contexts. Raw model decisions remain separate.',
        'source_readings_retained': manifest['files']}
    receipt_path = RUN / 'review-receipt-v2.json'
    review_digest = hashlib.sha256(json.dumps(source_manifest, ensure_ascii=False, sort_keys=True).encode()).hexdigest()
    if receipt_path.exists():
        receipt = read(receipt_path)
        if receipt['reviewInputsDigest'] != review_digest:
            raise ValueError('Review inputs changed after the publication receipt was frozen.')
    else:
        receipt = {'reviewInputsDigest': review_digest, 'reviewedAt': dt.datetime.now(dt.timezone.utc).isoformat()}
        write(receipt_path, receipt)
    source_manifest['reviewedAt'] = receipt['reviewedAt']
    source_manifest['compiler'] = {'path': 'scripts/publish_person_occupations.py',
        'sha256': hashlib.sha256(Path(__file__).read_text(encoding='utf-8-sig').encode()).hexdigest(),
        'hash_normalization': 'UTF-8 source with universal LF newlines; scripture bytes are never normalized this way.'}
    write(RUN / 'reviewed-source-manifest.json', source_manifest)
    source = copy.deepcopy(previous['source'])
    source['attribution'] += ' Berean Standard Bible by Bible Hub / Berean Bible. Full occupation source reviews by Codex agents.'
    source['license'] += ' Berean Standard Bible: publisher public-domain declaration,2023-04-30.'
    source['uri'] = 'urn:essenthos:occupation-full-publication-2026-10-27-v1:reviewed-source-manifest'
    source['sha256'] = sha(RUN / 'reviewed-source-manifest.json')
    publication = {'schemaVersion': 1, 'publicationId': previous['publicationId'],
        'review': {'status': 'reviewed', 'reviewedBy': 'Codex lead and independent frozen-source reviewers',
                   'reviewedAt': receipt['reviewedAt']},
        'source': source, 'occupations': [concepts[s] for s in sorted(used)], 'assertions': assertions}
    write(RUN / 'bundle.json', publication)
    people = {p['slug']: p for p in read(RUN / 'snapshot/v2/persons.json')}
    selected_people = sorted({a['personSlug'] for a in assertions})
    write(RUN / 'people.json', {'people': [{'slug': slug, 'name': people[slug]['name'], 'sex': people[slug]['sex'],
        'localNames': {language: values[0] for language, values in people[slug]['localNames'].items() if values}}
        for slug in selected_people]})
    pairs = {(a['personSlug'], a['occupationSlug']) for a in assertions}
    previous_pairs = {(a['personSlug'], a['occupationSlug']) for a in previous['assertions']}
    if not previous_pairs.issubset(pairs):
        raise ValueError('Full publication lost a previously admitted person-role pair.')
    write(RUN / 'group-dispositions.json', {'accepted_groups': accepted_groups, 'source_split_groups': sorted(split_groups), 'not_admitted': exclusions,
        'unused_context_policy': 'Only explicitly selected source-reviewed contexts are published; all other repetitions remain in the frozen source inventory.'})
    write(RUN / 'coverage-dispositions.json', [{'person': slug,
        'scope': 'published' if slug in selected_people else 'no_indexed_source' if person['coverage'] == 'no_indexed_references' else 'no_admitted_positive_claim',
        'negative_vocation_claim': False} for slug, person in people.items()])
    summary = {'occupations': len(used), 'people': len(selected_people), 'person_role_pairs': len(pairs),
        'accepted_raw_groups': accepted_groups, 'not_admitted_raw_groups': len(exclusions),
        'reviewed_groups': len(decisions), 'source_terminal_facts': len(terminal_review['findings']),
        'source_split_assertions': manual_assertions,
        'preserved_previous_pairs': len(previous_pairs), 'raw_contexts_retained': manifest['contexts'],
        'observations_retained': manifest['observations'], 'bundle_sha256': sha(RUN / 'bundle.json'),
        'publication_id': publication['publicationId'], 'paid_calls': 0}
    write(RUN / 'publication-summary.json', summary)
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'inspect', 'roles', 'terminal', 'observations', 'licences', 'compile', 'verify'))
    parser.add_argument('--part', choices=('all', 'a', 'b'), default='all')
    parser.add_argument('--skip', type=int, default=0)
    parser.add_argument('--take', type=int, default=15)
    parser.add_argument('--id', default='')
    args = parser.parse_args()
    if args.mode == 'prepare':
        prepare()
    elif args.mode == 'roles':
        inventory(args)
    elif args.mode == 'terminal':
        terminal()
    elif args.mode == 'observations':
        observation_ledger()
    elif args.mode == 'licences':
        licences()
    elif args.mode == 'compile':
        compile_publication()
    elif args.mode == 'verify':
        print(json.dumps({'frozen_source_verified': verify_snapshot()['groups']}))
    else:
        inspect(args)
