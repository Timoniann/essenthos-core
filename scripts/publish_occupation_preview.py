"""Prepare and verify a bounded, offline, source-reviewed occupation preview."""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import unicodedata

MAIN = Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
RUN = MAIN / 'Resources/Essenthos/generation/occupation-interface-preview-2026-10-27-v1'
V2 = MAIN / 'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v2'
V3 = MAIN / 'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v3'
BASE = MAIN / 'Resources/Essenthos/generation/person-presence-sonnet55-2026-10-26-v3'
PEOPLE = {'luke', 'lydia', 'simon', 'andrew', 'aquila', 'priscilla', 'saul-2',
          'shiphrah', 'puah', 'amos', 'eliashib-7', 'cornelius', 'erastus-2',
          'matthew', 'demetrius'}
ADDRESSES = {(51, 4, 14), (44, 16, 14), (40, 4, 18), (44, 18, 1),
             (44, 18, 2), (44, 18, 3), (44, 13, 9), (2, 1, 15), (2, 1, 16),
             (30, 7, 14), (30, 7, 10), (16, 3, 1), (44, 10, 1), (45, 16, 23),
             (40, 9, 9), (40, 10, 3), (44, 19, 24)}
EDITIONS = {'KJV', 'BHSA', 'NESTLE1904'}
REVIEWER = 'Codex agent, source reading on owner-authorized TSK-1234 preview'
BOOKS = {2: 'EXO', 16: 'NEH', 30: 'AMO', 40: 'MAT', 44: 'ACT', 45: 'ROM', 51: 'COL'}
LOCALES = {'eng', 'ukr', 'deu', 'spa'}
ROLES = [
    ('physician', 'trade', [
        ('Physician', 'Physician', 'Physician'), ('Лікар', 'Лікар', 'Лікарка'),
        ('Arzt', 'Arzt', 'Ärztin'), ('Médico', 'Médico', 'Médica')]),
    ('purple-dealer', 'trade', [
        ('Purple dealer', 'Purple dealer', 'Purple dealer'),
        ('Продавець пурпуру', 'Продавець пурпуру', 'Продавчиня пурпуру'),
        ('Purpurhändler', 'Purpurhändler', 'Purpurhändlerin'),
        ('Comerciante de púrpura', 'Comerciante de púrpura', 'Comerciante de púrpura')]),
    ('fisherman', 'trade', [
        ('Fisher', 'Fisherman', 'Fisherwoman'), ('Рибалка', 'Рибалка', 'Рибачка'),
        ('Fischer', 'Fischer', 'Fischerin'), ('Pescador', 'Pescador', 'Pescadora')]),
    ('tentmaker', 'trade', [
        ('Tentmaker', 'Tentmaker', 'Tentmaker'),
        ('Виробник наметів', 'Виробник наметів', 'Виробниця наметів'),
        ('Zeltmacher', 'Zeltmacher', 'Zeltmacherin'),
        ('Fabricante de tiendas', 'Fabricante de tiendas', 'Fabricante de tiendas')]),
    ('midwife', 'trade', [
        ('Midwife', 'Midwife', 'Midwife'),
        ('Повитуха', 'Помічник при пологах', 'Повитуха'),
        ('Hebamme', 'Geburtshelfer', 'Hebamme'), ('Partera', 'Partero', 'Partera')]),
    ('herdsman', 'trade', [
        ('Herdsman', 'Herdsman', 'Herdswoman'), ('Скотар', 'Скотар', 'Скотарка'),
        ('Viehhirte', 'Viehhirte', 'Viehirtin'),
        ('Pastor de ganado', 'Pastor de ganado', 'Pastora de ganado')]),
    ('sycamore-fig-gatherer', 'trade', [
        ('Sycamore-fig gatherer', 'Sycamore-fig gatherer', 'Sycamore-fig gatherer'),
        ('Збирач плодів сикомори', 'Збирач плодів сикомори', 'Збирачка плодів сикомори'),
        ('Sammler von Maulbeerfeigen', 'Sammler von Maulbeerfeigen', 'Sammlerin von Maulbeerfeigen'),
        ('Recolector de higos de sicómoro', 'Recolector de higos de sicómoro', 'Recolectora de higos de sicómoro')]),
    ('high-priest', 'religious_role', [
        ('High priest', 'High priest', 'High priestess'),
        ('Первосвященник', 'Первосвященник', 'Первосвященниця'),
        ('Hohepriester', 'Hohepriester', 'Hohepriesterin'),
        ('Sumo sacerdote', 'Sumo sacerdote', 'Suma sacerdotisa')]),
    ('centurion', 'military_role', [
        ('Centurion', 'Centurion', 'Centurion'), ('Сотник', 'Сотник', 'Сотниця'),
        ('Zenturio', 'Zenturio', 'Zenturionin'), ('Centurión', 'Centurión', 'Centuriona')]),
    ('city-steward', 'office', [
        ('City steward', 'City steward', 'City steward'),
        ('Міський управитель', 'Міський управитель', 'Міська управителька'),
        ('Stadtverwalter', 'Stadtverwalter', 'Stadtverwalterin'),
        ('Administrador de la ciudad', 'Administrador de la ciudad', 'Administradora de la ciudad')]),
    ('tax-collector', 'office', [
        ('Tax collector', 'Tax collector', 'Tax collector'),
        ('Збирач податків', 'Збирач податків', 'Збирачка податків'),
        ('Zöllner', 'Zöllner', 'Zöllnerin'),
        ('Recaudador de impuestos', 'Recaudador de impuestos', 'Recaudadora de impuestos')]),
    ('silversmith', 'trade', [
        ('Silversmith', 'Silversmith', 'Silversmith'), ('Срібляр', 'Срібляр', 'Сріблярка'),
        ('Silberschmied', 'Silberschmied', 'Silberschmiedin'), ('Platero', 'Platero', 'Platera')]),
]


def spec(kind, text, book, chapter, verse, quote=None):
    return kind, text, book, chapter, verse, quote


def named(kind, text, address, quote=None):
    return spec(kind, text, *address, quote)


def reviewed_claims():
    claims = []

    def add(person, role, witnesses, conclusion, time_scope='unspecified'):
        claims.append(dict(person=person, role=role, witnesses=witnesses,
                           conclusion=conclusion, time_scope=time_scope))

    add('luke', 'physician', [
        spec('profession', 'KJV', 51, 4, 14, 'Luke, the beloved physician'),
        spec('profession', 'NESTLE1904', 51, 4, 14, 'Λουκᾶς ὁ ἰατρὸς ὁ ἀγαπητὸς')],
        'The name Luke and physician are directly joined in the greeting; Demas is not assigned the role.')
    add('lydia', 'purple-dealer', [
        spec('profession', 'KJV', 44, 16, 14, 'a certain woman named Lydia, a seller of purple'),
        spec('profession', 'NESTLE1904', 44, 16, 14, 'γυνὴ ὀνόματι Λυδία, πορφυρόπωλις')],
        'Lydia is explicitly a seller of purple. No manufacturing, wealth or dye-production claim is inferred.')
    for person in ('simon', 'andrew'):
        add(person, 'fisherman', [
            spec('profession', 'KJV', 40, 4, 18, 'for they were fishers.'),
            spec('identity', 'KJV', 40, 4, 18, 'Simon called Peter, and Andrew his brother'),
            spec('profession', 'NESTLE1904', 40, 4, 18, 'ἦσαν γὰρ ἁλεεῖς.'),
            spec('identity', 'NESTLE1904', 40, 4, 18, 'Σίμωνα τὸν λεγόμενον Πέτρον καὶ Ἀνδρέαν τὸν ἀδελφὸν αὐτοῦ')],
            'The two named brothers are the plural subject of fishers. Simon called Peter is the simon record; no other Simon inherits the role.')
    for person in ('aquila', 'priscilla', 'saul-2'):
        witnesses = [
            spec('profession', 'KJV', 44, 18, 3),
            spec('identity', 'KJV', 44, 18, 2),
            spec('profession', 'NESTLE1904', 44, 18, 3),
            spec('identity', 'NESTLE1904', 44, 18, 2)]
        if person == 'saul-2':
            witnesses += [spec('identity', 'KJV', 44, 18, 1),
                          spec('identity', 'KJV', 44, 13, 9, 'Saul, (who also is called Paul,)'),
                          spec('identity', 'NESTLE1904', 44, 13, 9, 'Σαῦλος δέ, ὁ καὶ Παῦλος')]
        add(person, 'tentmaker', witnesses,
            'Acts18:1 names Paul;18:2 names Aquila and his wife Priscilla;18:3 joins his same craft with their plural occupation. The KJV and Greek plural support the shared role. Saul/Paul identity is explicitly bridged by13:9. This contextual group attribution is agent-reviewed, not a name-only transfer.')
    for person in ('shiphrah', 'puah'):
        add(person, 'midwife', [spec('profession', 'KJV', 2, 1, 15),
                                spec('identity', 'KJV', 2, 1, 15),
                                spec('profession', 'BHSA', 2, 1, 15),
                                spec('identity', 'BHSA', 2, 1, 15)],
            'The named women are the two midwives addressed by the king. The other Puah record is male and is not this woman. No claim is made about which ethnic attachment the Hebrew adjective requires.')
    add('amos', 'herdsman', [
        spec('profession', 'KJV', 30, 7, 14, 'but I was an herdman'),
        spec('identity', 'KJV', 30, 7, 14, 'Then answered Amos'),
        spec('profession', 'BHSA', 30, 7, 14)],
        'Amos identifies himself as a herdsman in his answer. English past tense and Hebrew nominal wording do not establish when the occupation ended; time remains unspecified.')
    add('amos', 'sycamore-fig-gatherer', [
        spec('profession', 'KJV', 30, 7, 14, 'a gatherer of sycomore fruit'),
        spec('identity', 'KJV', 30, 7, 14, 'Then answered Amos'),
        spec('profession', 'BHSA', 30, 7, 14)],
        'The narrow reader label follows the KJV gatherer wording, not a claim that the Hebrew literally says gatherer. Hebrew boles is retained in the original full-verse quote. Sycamore means the fig tree, not the unrelated European maple. No ceased-occupation claim is made.')
    add('eliashib-7', 'high-priest', [
        spec('profession', 'KJV', 16, 3, 1, 'Eliashib the high priest'),
        spec('profession', 'BHSA', 16, 3, 1)],
        'The verse directly calls Eliashib high priest. The frozen record indexed at Nehemiah3:1 is eliashib-7, not another same-name record.')
    add('cornelius', 'centurion', [
        spec('profession', 'KJV', 44, 10, 1, 'Cornelius, a centurion of the band called the Italian band'),
        spec('profession', 'NESTLE1904', 44, 10, 1, 'Κορνήλιος, ἑκατοντάρχης')],
        'The name and centurion role are explicit; the label does not assert a modern rank equivalence or an exact troop headcount.')
    add('erastus-2', 'city-steward', [
        spec('profession', 'KJV', 45, 16, 23, 'Erastus the chamberlain of the city'),
        spec('profession', 'NESTLE1904', 45, 16, 23, 'Ἔραστος ὁ οἰκονόμος τῆς πόλεως')],
        'City steward is a broad rendering of oikonomos/chamberlain. No aedile identity, inscription identification, particular financial duties or identity with the Acts19:22 Erastus is asserted. The frozen ROM16:23 record is erastus-2.')
    add('matthew', 'tax-collector', [
        spec('profession', 'KJV', 40, 10, 3, 'Matthew the publican'),
        spec('profession', 'NESTLE1904', 40, 10, 3, 'Μαθθαῖος ὁ τελώνης'),
        spec('identity', 'KJV', 40, 9, 9, 'a man, named Matthew, sitting at the receipt of custom')],
        'Publican/telones names the tax-collection office; the Matthew record is explicitly named. No Levi/Matthew identification is added.')
    add('demetrius', 'silversmith', [
        spec('profession', 'KJV', 44, 19, 24, 'Demetrius, a silversmith'),
        spec('profession', 'NESTLE1904', 44, 19, 24, 'Δημήτριος γάρ τις ὀνόματι, ἀργυροκόπος')],
        'The named silversmith is the Acts19 Demetrius, not the same-name person of3John. His specific silver-shrine work is preserved in the full source verse.')
    return claims


def digest(data):
    return hashlib.sha256(data).hexdigest()


def sha(path):
    hasher = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            hasher.update(block)
    return hasher.hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def snapshot():
    if RUN.exists():
        raise ValueError('Preview already exists; verify it instead of replacing frozen evidence')
    snap = RUN / 'snapshot'
    snap.mkdir(parents=True)
    records = []

    def retain(source, relative):
        target = snap / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        data = source.read_bytes()
        if source.suffix == '.json':
            json.loads(data)
        target.write_bytes(data)
        records.append({'source': source.relative_to(MAIN).as_posix(),
                        'snapshot': target.relative_to(RUN).as_posix(),
                        'sha256': digest(data), 'bytes': len(data)})

    retain(V3 / 'results.json', 'v3/results.json')
    retain(V3 / 'manifest.json', 'v3/manifest.json')
    for name in ('source-manifest.json', 'run-manifest.json', 'persons.json',
                 'current-index-manifest.json', 'identity-bridge-proof.json',
                 'occupation-policy.json'):
        retain(V2 / name, 'v2/' + name)
    retain(BASE / 'export-manifest.json', 'export-manifest.json')
    results = read(snap / 'v3/results.json')
    selected = [row for row in results['persons'] if row['person'] in PEOPLE]
    write(snap / 'selected-findings.json', selected)
    jobs = {f['job_id'] for p in selected for f in p['findings']}
    parents = {f.get('parent_job', f['job_id']) for p in selected for f in p['findings']}
    for run_name, source in (('v3', V3), ('v2', V2)):
        for phase in ('draft', 'check'):
            for folder in ('checked', 'raw', 'inputs'):
                root = source / phase / folder
                if not root.exists():
                    continue
                for path in root.glob('*.json'):
                    if any(path.stem == job or path.stem.startswith(job + '-') for job in jobs | parents):
                        retain(path, f'{run_name}/{phase}/{folder}/{path.name}')
    for source_folder in ('bible4u', 'KingJames2006', 'etcbc', 'Nestle1904', 'BibleData2026'):
        retain(MAIN / 'Resources' / source_folder / 'LICENCE.md', f'licences/{source_folder}/LICENCE.md')
    for folder in ('etcbc', 'Nestle1904'):
        for path in (MAIN / 'Resources' / folder).glob('**/LICENSE*'):
            retain(path, 'licences/' + folder + '/' + path.relative_to(MAIN / 'Resources' / folder).as_posix())
    verses, placements, metadata = [], [], []
    export_hasher = hashlib.sha256()
    with (BASE / 'export.jsonl').open('rb') as stream:
        for line in stream:
            export_hasher.update(line)
            row = json.loads(line)
            if row['type'] == 'verse' and row['text'] in EDITIONS:
                printed = tuple(row[k] for k in ('book', 'chapter', 'verse'))
                if tuple(row['canonical']) in ADDRESSES or printed in ADDRESSES:
                    verses.append(row)
            elif row['type'] == 'placement' and row['text'] in EDITIONS:
                printed = tuple(row[k] for k in ('book', 'chapter', 'verse'))
                if tuple(row['canonical']) in ADDRESSES or printed in ADDRESSES:
                    placements.append(row)
            elif row['type'] in ('text', 'snapshot') or row.get('slug') in PEOPLE:
                metadata.append(row)
    expected = read(V2 / 'source-manifest.json')['frozen_corpus']['export_sha256']
    if export_hasher.hexdigest() != expected:
        raise ValueError('Frozen export SHA differs from occupation input provenance')
    write(snap / 'verses.json', verses)
    write(snap / 'placements.json', placements)
    write(snap / 'export-selected-metadata.json', metadata)
    for path in sorted(snap.rglob('*')):
        if path.is_file() and path.relative_to(RUN).as_posix() not in {r['snapshot'] for r in records}:
            records.append({'snapshot': path.relative_to(RUN).as_posix(),
                            'sha256': sha(path), 'bytes': path.stat().st_size})
    write(RUN / 'snapshot-manifest.json', {
        'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'files': records,
        'export': {'source': (BASE / 'export.jsonl').relative_to(MAIN).as_posix(),
                   'sha256': expected, 'method': 'One-pass extraction from frozen hash-verified export'},
        'mutable_results': 'Frozen once; subsequent collector changes are outside this reviewed slice',
        'persons': sorted(PEOPLE), 'canonical_addresses': sorted(ADDRESSES)})
    print(json.dumps({'snapshot': str(RUN), 'files': len(records), 'verses': len(verses)}))


def inspect():
    for person in read(RUN / 'snapshot/selected-findings.json'):
        print(json.dumps({'person': person['person'], 'checked_roles': [
            f['role_key'] for f in person['findings']]}, ensure_ascii=False))
    for verse in read(RUN / 'snapshot/verses.json'):
        print(json.dumps(verse, ensure_ascii=False))


def nfc(value):
    return unicodedata.normalize('NFC', value)


def reference(address):
    book, chapter, verse = address
    return f'{BOOKS[book]} {chapter}:{verse}'


def verse_index(run=RUN):
    return {(v['text'], v['book'], v['chapter'], v['verse']): v
            for v in read(run / 'snapshot/verses.json')}


def witness(item, verses):
    kind, edition, book, chapter, number, quote = item
    original = verses[edition, book, chapter, number]
    body = nfc(original['body'])
    excerpt = nfc(body if quote is None else quote)
    if excerpt not in body:
        raise ValueError(f'Nonverbatim reviewed quote: {edition} {book}:{chapter}:{number}: {excerpt}')
    return dict(kind=kind, text=edition, reference=reference((book, chapter, number)),
                canonicalReference=reference(original['canonical']), quote=excerpt,
                sourceText=body, sourceSha256=digest(body.encode('utf-8')))


def validate_bundle(bundle, people, run=RUN):
    if bundle.get('schemaVersion') != 1 or bundle['review']['status'] != 'reviewed':
        raise ValueError('Unreviewed or unsupported bundle')
    roles = bundle['occupations']
    role_keys = {r['slug'] for r in roles}
    if len(role_keys) != len(roles):
        raise ValueError('Duplicate occupation')
    for role in roles:
        if role['category'] not in {'trade', 'office', 'military_role', 'religious_role'}:
            raise ValueError('Invalid category')
        if set(role['labels']) != LOCALES:
            raise ValueError('Exactly four reader locales required')
        for label in role['labels'].values():
            if set(label) != {'name', 'masculine', 'feminine', 'description'}:
                raise ValueError('Unexpected reader label fields')
            if not all(isinstance(label[k], str) and label[k].strip() for k in ('name', 'masculine', 'feminine')):
                raise ValueError('Missing gender-aware label')
    people_by_slug = {p['slug']: p for p in people['people']}
    if len(people_by_slug) != len(people['people']):
        raise ValueError('Duplicate person')
    frozen_people = {p['slug']: p for p in read(run / 'snapshot/v2/persons.json')}
    for slug, person in people_by_slug.items():
        source = frozen_people[slug]
        expected_names = {language: names[0] for language, names in source['localNames'].items() if names}
        if person != dict(slug=slug, name=source['name'], sex=source['sex'], localNames=expected_names):
            raise ValueError('Person metadata differs from frozen source')
    verses = verse_index(run)
    by_source = {(v['text'], reference((v['book'], v['chapter'], v['verse']))): v for v in verses.values()}
    pairs = set()
    for assertion in bundle['assertions']:
        pair = assertion['personSlug'], assertion['occupationSlug']
        if pair in pairs:
            raise ValueError('Duplicate assertion')
        pairs.add(pair)
        if pair[0] not in people_by_slug or pair[1] not in role_keys:
            raise ValueError('Unknown person or occupation')
        if not any(w['kind'] == 'profession' for w in assertion['witnesses']):
            raise ValueError('Missing profession witness')
        for evidence in assertion['witnesses']:
            if evidence['kind'] not in {'profession', 'identity'}:
                raise ValueError('Invalid witness kind')
            source = by_source.get((evidence['text'], evidence['reference']))
            if source is None or evidence['canonicalReference'] != reference(source['canonical']):
                raise ValueError('Printed/canonical source mapping differs')
            body = nfc(source['body'])
            if evidence['sourceText'] != body or evidence['sourceSha256'] != digest(body.encode('utf-8')):
                raise ValueError('Full-verse source/hash mismatch')
            if not evidence['quote'] or nfc(evidence['quote']) != evidence['quote'] or evidence['quote'] not in body:
                raise ValueError('Nonverbatim or non-NFC quote')
    expected_pairs = {(c['person'], c['role']) for c in reviewed_claims()}
    if pairs != expected_pairs:
        raise ValueError('Assertion set differs from explicit reviewed slice')
    if set(people_by_slug) != {p for p, _ in pairs}:
        raise ValueError('People sidecar is not exactly the asserted people')
    for person in ('aquila', 'priscilla', 'saul-2'):
        assertion = next(a for a in bundle['assertions'] if (a['personSlug'], a['occupationSlug']) == (person, 'tentmaker'))
        identities = {w['reference'] for w in assertion['witnesses'] if w['kind'] == 'identity'}
        required = {'ACT 18:2', 'ACT 13:9', 'ACT 18:1'} if person == 'saul-2' else {'ACT 18:2'}
        if not required <= identities:
            raise ValueError('Missing shared-trade or renamed-person identity bridge')
    for claim in reviewed_claims():
        assertion = next(a for a in bundle['assertions']
                         if (a['personSlug'], a['occupationSlug']) == (claim['person'], claim['role']))
        expected = [witness(item, verses) for item in claim['witnesses']]
        if assertion['witnesses'] != expected:
            raise ValueError('Witness set differs from explicit source review')
    return dict(occupations=len(roles), persons=len(people_by_slug), assertions=len(pairs),
                witnesses=sum(len(a['witnesses']) for a in bundle['assertions']))


def verify_snapshot(run=RUN):
    manifest = read(run / 'snapshot-manifest.json')
    for record in manifest['files']:
        path = run / record['snapshot']
        if sha(path) != record['sha256'] or path.stat().st_size != record['bytes']:
            raise ValueError(f'Frozen evidence changed: {record["snapshot"]}')
    return manifest


def compile_preview():
    verify_snapshot()
    if (RUN / 'bundle.json').exists():
        raise ValueError('Reviewed bundle already exists; verify, do not replace it')
    verses = verse_index()
    reviewed_at = dt.datetime.now(dt.timezone.utc).isoformat()
    assertions, decisions = [], []
    findings = {p['person']: p['findings'] for p in read(RUN / 'snapshot/selected-findings.json')}
    for claim in reviewed_claims():
        evidence = [witness(item, verses) for item in claim['witnesses']]
        assertion = dict(personSlug=claim['person'], occupationSlug=claim['role'], witnesses=evidence)
        candidate = next((f for f in findings[claim['person']] if f['role_key'] == claim['role']), None)
        if candidate:
            assertion['origin'] = dict(model=candidate['model'], readingRun=V3.name,
                jobId=candidate['job_id'], draftMessageId=candidate['draft_message_id'],
                checkMessageId=candidate['check_message_id'], reviewedBy=REVIEWER)
        assertions.append(assertion)
        decisions.append(dict(person=claim['person'], occupation=claim['role'],
            decision='source-reviewed-preview-only', reviewedBy=REVIEWER, human_accepted=False,
            evidence='read', time_scope=claim['time_scope'], conclusion=claim['conclusion'],
            candidate_provenance_retained=bool(candidate),
            witnesses=[dict(text=w['text'], printed=w['reference'], canonical=w['canonicalReference'],
                            source_sha256=w['sourceSha256']) for w in evidence]))
    occupations = [dict(slug=slug, category=category, labels={
        locale: dict(name=values[0], masculine=values[1], feminine=values[2], description=None)
        for locale, values in zip(('eng', 'ukr', 'deu', 'spa'), localized)})
        for slug, category, localized in ROLES]
    bundle = dict(schemaVersion=1, publicationId=RUN.name,
        review=dict(status='reviewed', reviewedBy=REVIEWER, reviewedAt=reviewed_at),
        source=dict(
            attribution='King James Version (bible4u, corrected against eBible KingJames2006); BHSA2021, Eep Talstra Centre for Bible and Computer, VU Amsterdam; Nestle1904 morphology, biblicalhumanities.org, Diego Renato dos Santos, Ulrik Sandborg-Petersen, Maurice A. Robinson, Jonathan Robie. Frozen Essenthos verse export, 2026-10-26. Person metadata includes BibleData by Brady Stephenson and Essenthos own records.',
            license='KJV: public-domain Bible text; bible4u permits free copying/distribution with Biblical content unchanged. BHSA: CC BY-NC 4.0 (underlying BHS rights Deutsche Bibelgesellschaft). Nestle1904 morph: CC0 1.0. BibleData person metadata: CC BY 4.0. Retained component terms are in snapshot/licences.',
            uri='urn:essenthos:occupation-interface-preview-2026-10-27-v1:snapshot-manifest',
            sha256=sha(RUN / 'snapshot-manifest.json')),
        occupations=occupations, assertions=assertions)
    source_people = {p['slug']: p for p in read(RUN / 'snapshot/v2/persons.json')}
    people = dict(people=[dict(slug=slug, name=source_people[slug]['name'], sex=source_people[slug]['sex'],
        localNames={language: names[0] for language, names in source_people[slug]['localNames'].items() if names})
        for slug in sorted(PEOPLE)])
    counts = validate_bundle(bundle, people)
    write(RUN / 'bundle.json', bundle)
    write(RUN / 'people.json', people)
    write(RUN / 'source-review.json', dict(reviewedBy=REVIEWER, reviewedAt=reviewed_at,
        human_accepted=False, live_corpus_admission=False, model_calls=0, database_calls=0,
        status='reviewed-preview-only', evidence='read', counts=counts, decisions=decisions,
        locale_review='Dedicated English/Ukrainian/German/Spanish reader labels were authored and read by the agent, not copied from model/owner working reasons. No human language sign-off is claimed. English and Spanish epicene nouns may have identical gender forms. All optional descriptions are null.',
        exclusions=['No Joseph/Hananiah/Judas duty or moral-label admission.',
                    'No prophet assertion for Amos; no historical/current/end-date assertion for his two trades.',
                    'No candidate/model verdict is treated as gold; pending/empty NT results were reviewed directly from retained verses.',
                    'No whole-run precision, recall or completion measurement; only this fixed 15-person slice.',
                    'Female forms are grammatical display forms, not a claim of a female bearer of each role.']))
    metadata = [dict(slug=p['slug'], source=p['source'], sourceId=p.get('sourceId'),
                     localNames=p['localNames']) for p in source_people.values() if p['slug'] in PEOPLE]
    write(RUN / 'provenance.json', dict(
        bundle_sha256=sha(RUN / 'bundle.json'), people_sha256=sha(RUN / 'people.json'),
        source_snapshot_manifest_sha256=sha(RUN / 'snapshot-manifest.json'),
        original_frozen_export=read(RUN / 'snapshot-manifest.json')['export'],
        quote_transformation='Unicode NFC only; spaces, punctuation, capitalization, trailers and full verse content are preserved. sourceSha256 hashes UTF-8 NFC full verse, not the raw export row. Quotes are exact contiguous substrings of that sourceText.',
        numbering='reference is the printed edition address; canonicalReference comes from the frozen verse row. No re-numbering guess is permitted. All selected printed and canonical addresses happen to coincide; validator still checks them independently.',
        reader_labels='Agent-authored normalized reader labels, not claims that each label is a literal source phrase. Original evidence is retained in quote/sourceText. No review notes are reader descriptions.',
        person_metadata=metadata,
        source_licences=[dict(path=str(path.relative_to(RUN)).replace(chr(92), '/'), sha256=sha(path))
                         for path in sorted((RUN / 'snapshot/licences').rglob('*')) if path.is_file()],
        review_report_sha256=sha(RUN / 'source-review.json')))
    artifact_files = ('bundle.json', 'people.json', 'source-review.json', 'provenance.json', 'snapshot-manifest.json')
    write(RUN / 'manifest.json', dict(schemaVersion=1, publicationId=RUN.name,
        at=reviewed_at, purpose='Isolated occupation interface preview; no live admission',
        human_accepted=False, counts=counts,
        files={name: sha(RUN / name) for name in artifact_files}))
    verify()


def verify(run=RUN):
    verify_snapshot(run)
    manifest = read(run / 'manifest.json')
    for name, expected in manifest['files'].items():
        if sha(run / name) != expected:
            raise ValueError(f'Bound preview artifact changed: {name}')
    bundle, people = read(run / 'bundle.json'), read(run / 'people.json')
    counts = validate_bundle(bundle, people, run)
    if bundle['source']['sha256'] != sha(run / 'snapshot-manifest.json'):
        raise ValueError('Bundle source does not bind frozen snapshot')
    review = read(run / 'source-review.json')
    if review['human_accepted'] or review['live_corpus_admission'] or review['counts'] != counts:
        raise ValueError('Review boundary or counts invalid')
    print(json.dumps(dict(verified=True, bundle_sha256=sha(run / 'bundle.json'),
                          people_sha256=sha(run / 'people.json'), **counts)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('snapshot', 'inspect', 'compile', 'verify'))
    args = parser.parse_args()
    {'snapshot': snapshot, 'inspect': inspect, 'compile': compile_preview, 'verify': verify}[args.mode]()
