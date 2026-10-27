"""Review the fixed hundred held occupation candidates without paid calls or live access."""
import argparse
import copy
import datetime as dt
import hashlib
import json
from pathlib import Path
import unicodedata

MAIN = Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
BASE = MAIN / 'Resources/Essenthos/generation/occupation-full-publication-2026-10-27-v1'
RUN = MAIN / 'Resources/Essenthos/generation/occupation-held-review-2026-10-27-v1'
BASE_HASH = 'd1801f6b544ef7abe4868e23b2f671dfb48926e0ed165ca318861ba354003795'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare():
    if (RUN / 'manifest.json').exists():
        raise ValueError('The review snapshot already exists.')
    if sha(BASE / 'bundle.json') != BASE_HASH:
        raise ValueError('The published baseline changed.')
    held = read(BASE / 'group-dispositions.json')['not_admitted']
    if len(held) != 100:
        raise ValueError('This review covers the fixed original hundred candidates.')
    groups = {g['id']: g for g in read(BASE / 'groups.json')}
    verses = {}
    files = []
    for entry in read(BASE / 'manifest.json')['files']:
        path = BASE / entry['snapshot']
        if sha(path) != entry['sha256']:
            raise ValueError('Original frozen source changed: ' + entry['snapshot'])
        if not entry['snapshot'].startswith('snapshot/packets/'):
            continue
        files.append({'path': str(path), 'sha256': sha(path)})
        for verse in read(path)['verses']:
            key = verse['key']
            if key in verses and verses[key]['body'] != verse['body']:
                raise ValueError('Source key has conflicting full texts: ' + key)
            verses[key] = verse
    write(RUN / 'source-verses.json', verses)
    write(RUN / 'candidates.json', [{**h, 'group': groups[h['id']]} for h in held])
    write(RUN / 'people.json', read(BASE / 'snapshot/v2/persons.json'))
    write(RUN / 'manifest.json', {
        'task': 'TSK-1254', 'baselineSha256': BASE_HASH,
        'originalGroupDispositionsSha256': sha(BASE / 'group-dispositions.json'),
        'sourceFiles': files,
        'reviewFiles': [{'path': name, 'sha256': sha(RUN / name)} for name in
                        ('source-verses.json', 'candidates.json', 'people.json')],
        'scope': 'Exactly100 held candidates; source packets indexed offline for their expanded context, not a corpus coverage verification.',
        'paid_calls': 0})
    print(json.dumps({'candidates': len(held), 'retained_source_verses': len(verses)}))


def context(args):
    verses = read(RUN / 'source-verses.json')
    start, end = [int(n) for n in args.verses.split('-')] if '-' in args.verses else (int(args.verses), int(args.verses))
    for verse in verses.values():
        text, book, chapter, number = verse['key'].split(':')
        if text in args.text.split(',') and int(book) == args.book and int(chapter) == args.chapter and start <= int(number) <= end:
            print(verse['key'] + ' ' + verse['body'])


def candidates(args):
    people = {p['slug']: p for p in read(RUN / 'people.json')}
    for candidate in read(RUN / 'candidates.json')[args.skip:args.skip + args.take]:
        group = candidate['group']
        existing = read(BASE / 'bundle.json')['assertions']
        print(json.dumps({k: candidate[k] for k in ('id', 'person', 'raw_role', 'why')}, ensure_ascii=False))
        print('Identity: ' + str(people[candidate['person']].get('distinguisher', '')))
        print('Published roles: ' + ', '.join(sorted({a['occupationSlug'] for a in existing if a['personSlug'] == candidate['person']})))
        for source in {s['key']: s for c in group['contexts'] for s in c['sources']}.values():
            print(source['key'] + ' ' + source['body'])


def record_review(revise=False):
    """Record the lead's explicit second source reading; these are curated decisions."""
    held = read(RUN / 'candidates.json')
    decisions = []
    for number, candidate in enumerate(held, 1):
        decisions.append({'id': candidate['id'], 'person': candidate['person'],
            'raw_role': candidate['raw_role'], 'verdict': 'not_established',
            'why': candidate['why'], 'claims': [],
            'reviewed_source_keys': sorted({s['key'] for c in candidate['group']['contexts'] for s in c['sources']})})

    def decide(numbers, verdict, why, slug=None, keys=()):
        for number in numbers:
            decision = decisions[number - 1]
            decision.update(verdict=verdict, why=why)
            decision['reviewed_source_keys'] = sorted(set(decision['reviewed_source_keys']) | set(keys))
            if slug:
                decision['claims'].append({'occupationSlug': slug, 'source_keys': list(keys)})

    decide([2, 3], 'reframed',
        'The five named sons are explicitly called five ancestral-house heads in1CH7:7. Publish that stated office, not a speculative military profession inferred from valor or the descendant census.',
        'ancestral-house-chief', ['BHSA:13:7:7', 'KJV:13:7:7'])
    decide(list(range(4, 11)), 'not_established',
        'Expanded1CH7:6-11,30-40 distinguishes the named ancestors from genealogically counted generations of troops. The census is not an individual occupation assignment.',
        keys=['BHSA:13:7:6','BHSA:13:7:7','BHSA:13:7:8','BHSA:13:7:9','BHSA:13:7:10','BHSA:13:7:11','BHSA:13:7:30','BHSA:13:7:40'])
    decide([11], 'uncertain_identity',
        'Jer39:3 segmentation is genuinely disputed. Modern Akkadian research treats samgar as an official title and Nebu as the beginning of the next name. Do not assign an occupation to a securely separate Samgar-Nebu person; no record merge/withdrawal is authorized here.',
        keys=['BHSA:24:39:3','KJV:24:39:3'])
    decide([13, 14], 'uncertain_identity',
        'Dan11:5-6 uses north/south kings in a succession prediction. A collective or changing designation does not identify one person whose historical employment can be asserted.')
    decide([15], 'reframed',
        'The registered Hanan is the bearer inKJV_Neh10:26/BHSA10:27, with Ahijah and Anan. Both Hanan occurrences belong to the same people-chief section headedKJV10:14/BHSA10:15 and ending before10:28. The identical office holds under either possible same-named entry; no speculative person merge is needed.',
        'leader-of-the-people', ['BHSA:16:10:15','BHSA:16:10:27','KJV:16:10:14','KJV:16:10:26','KJV:16:10:27','KJV:16:10:28'])
    decide([66], 'status_only',
        'The registered second Shebaniah is named inKJV_Neh10:12/BHSA10:13 within the Levite list. Both namesakes have that same affiliation, so identity uncertainty is not needed to reject the unsupported leap from affiliation to a profession.',
        keys=['KJV:16:10:9','KJV:16:10:12','BHSA:16:10:10','BHSA:16:10:13'])
    decide([17], 'reframed',
        'Ezek11:1 identifies Pelatiah by father and explicitly calls him a leader of the people;11:2 describes the named men giving counsel in the city and11:13 repeats his patronymic. The vision narrates this human civic role literally; it is not a prediction appointing him or a supernatural epithet.',
        'leader-of-the-people', ['BHSA:26:11:1','KJV:26:11:1','KJV:26:11:2','KJV:26:11:13'])
    decide([18], 'reframed',
        '1KI15:13 and2CH15:16 explicitly remove Maacah from the gebirah position. Removal from an office establishes its prior tenure independently of mother/grandmother kinship. Preserve the specific queen-mother office without calling her a reigning queen or queen consort.',
        'queen-mother', ['BHSA:11:15:13','KJV:11:15:13','BHSA:14:15:16','KJV:14:15:16'])
    decide([19], 'covered',
        'Jer39:3,13 establish a high Babylonian official but leave rab-mag technical duties uncertain. Existing royal-official is the supported shared minimum; do not invent magician, priest or general.',
        keys=['BHSA:24:39:3','BHSA:24:39:13'])
    decide([21,22,94], 'covered',
        'The personal appointment inEX28:1 and the named anointed priests inNUM3:2-4 resolve the genealogy-only concern. All three persons already have the canonical priest role; altar ministry is its duty, not another occupation.',
        keys=['BHSA:2:28:1','KJV:2:28:1','BHSA:4:3:2','BHSA:4:3:3','BHSA:4:3:4'])
    decide([23,26], 'metaphor',
        'Representative ambassador language in2Cor5:20/Eph6:20 describes gospel ministry; no literal diplomatic appointment is established. Keep the already supported apostolic ministry.')

    teachers = [29,30,39,56,67,70,74,75,80]
    decide(teachers, 'reframed',
        '2CH17:7 commissions teaching;17:8 names this member of the sent group;17:9 says they traveled through Judah teaching the book of the Law. This is an explicit assigned ministry rather than Levite descent alone.',
        'teacher-of-the-law', ['KJV:14:17:7','BHSA:14:17:8','BHSA:14:17:9','KJV:14:17:8','KJV:14:17:9'])
    servants = {31:12,34:12,38:14,40:13,42:12,43:12,52:12,53:13,68:14,72:14,73:13,78:14,81:13}
    for number, verse in servants.items():
        decide([number], 'reframed',
            '2CH29:12-14 names this contemporary Levite;29:15-19 assigns and records the sustained ritual cleansing of the temple under Hezekiah. Publish the actual Levite temple service, not ethnicity or a conjectured professional cleaner/singer specialty.',
            'levite-temple-servant', [f'BHSA:14:29:{verse}',f'KJV:14:29:{verse}','BHSA:14:29:15','BHSA:14:29:16','KJV:14:29:15','KJV:14:29:16'])
    decide([36,41,51,59,63,71], 'reframed',
        'Neh8:7 names this Levite among those explaining the Law and8:8 describes the explanatory reading. The registered identity is bound to this same verse, not inferred by equating unrelated namesakes.',
        'teacher-of-the-law', ['BHSA:16:8:7','BHSA:16:8:8','KJV:16:8:7','KJV:16:8:8'])
    decide([50], 'reframed',
        'Ezra10:23 explicitly says Kelaiah is Kelita (hu Qelita); Neh8:7 names Kelita among the Law teachers. The stated alias, not fuzzy name matching, establishes the bridge. This publishes the role on the existing record without silently merging two records.',
        'teacher-of-the-law', ['BHSA:15:10:23','KJV:15:10:23','BHSA:16:8:7','KJV:16:8:7','KJV:16:8:8'])
    decide([55], 'reframed',
        '1CH9:31 individually names Mattithiah, son of Shallum, and states his entrusted duty over pan-baked cakes. Unlike surrounding unnamed groups, the person and concrete standing assignment are in the same verse.',
        'temple-cake-maker', ['BHSA:13:9:31','KJV:13:9:31'])
    decide([54,58,77,87], 'covered',
        'Expanded context supplies a concrete already published assignment (Mattaniah prayer leadership,Neh11:17; Pedaiah treasury,Neh13:13; Uriel clan leadership,1CH15:11-12; Zetham temple treasures,1CH26:21-22). Retain that role rather than add generic Levite or ancestry-derived temple employment.',
        keys=['KJV:16:11:17','KJV:16:13:13','BHSA:13:15:11','BHSA:13:15:12','BHSA:13:26:21','BHSA:13:26:22'])
    decide([32,33,37,45,46,47,60,62,65,69,79], 'status_only',
        'Expanded sealing/return/household lists establish Levite affiliation or ancestry, not a distinct individual occupation. A neighboring descendant or namesake office is not inherited. No unsupported negative claim about the person is made.')
    decide([35,49,61,64], 'not_established',
        'Neh9:4-6 shows named Levites calling the assembly to blessing. It does not appoint each to a distinct continuing prayer-leader office. The observed act is retained; generic descent is not promoted to a profession.',
        keys=['BHSA:16:9:4','BHSA:16:9:5','KJV:16:9:4','KJV:16:9:5'])
    decide([44,57], 'not_established',
        'Ezra8:33-34 names Levite participants in receiving/weighing temple treasure, but supplies no individually appointed treasury office. Do not assign a job from participating once; a patronymic-free namesake elsewhere is not sufficient.',
        keys=['BHSA:15:8:33','KJV:15:8:33','KJV:15:8:34'])
    decide([76], 'reframed',
        'Neh12:8-9 places Unni in the corresponding watches beside the Levitical thanksgiving service. Publish only the generic assigned temple service, not a guessed singer/instrumentalist/porter specialty.',
        'levite-temple-servant', ['BHSA:16:12:8','BHSA:16:12:9','KJV:16:12:8','KJV:16:12:9'])
    decide(list(range(82,87)), 'not_established',
        'The named ancestral/clan genealogy in1CH23:6-23 precedes the count of service-age descendants in23:24. The count does not personally appoint every earlier ancestor as a temple servant.',
        keys=['KJV:13:23:8','KJV:13:23:9','KJV:13:23:10','KJV:13:23:11','KJV:13:23:19','KJV:13:23:20','KJV:13:23:24'])
    decide([88,89], 'covered',
        'Man of God is an honorific; the Gentile ministry label describes the separately attested ministry. Existing Elisha prophet and Paul apostle/teacher roles carry the supported vocation without a duplicate honorific or inferred cultic priesthood.')
    decide([90], 'status_only',
        'Acts26:5 explicitly identifies Pharisee affiliation; a religious party is not a job. Preserve affiliation separately rather than invent a vocation.')
    decide([91], 'uncertain_function',
        '2SAM20:23-26 lists royal officials but kohen-to-David has a genuinely divided priest/chief-minister interpretation in the primary translations and NET note. Exact specific function remains uncertain; keep priest unadmitted.',
        keys=['BHSA:10:20:26','KJV:10:20:26'])
    decide([93], 'reframed',
        '1KI4:5 names Zabud son of Nathan and the distinct title kings friend in the royal-officer list. That established adviser function supports royal-counselor without resolving whether kohen here means cultic priest or chief minister. The priest subclaim remains uncertain.',
        'royal-counselor', ['BHSA:11:4:5','KJV:11:4:5'])
    decide([48], 'reframed',
        'Ezra3:8 appoints oversight of temple building;3:9 explicitly names Kadmiel with his sons standing over the workers. This is his registered occurrence, unlike guesses from a later same-named prayer leader.',
        'temple-work-overseer', ['BHSA:15:3:8','BHSA:15:3:9','KJV:15:3:8','KJV:15:3:9'])
    owner_path = RUN / 'owner-decisions.json'
    owner = read(owner_path)
    if owner.get('heavenly_roles_marked') is not True or owner.get('combined_antioch_role') is not True or owner.get('elisha_farmer') is not True:
        raise ValueError('The explicitly requested owner decisions are missing.')
    decide([1], 'reframed',
        'Owner explicitly permits this directly named supernatural role only with the separate Heavenly role category. Joshua5:13-15 identifies the commander of the LORDs host; no human army appointment or identity with Jesus/Michael is inferred.',
        'heavenly-commander', ['BHSA:6:5:14','KJV:6:5:14','KJV:6:5:15'])
    decide([12], 'reframed',
        'Owner permits the directly named celestial role with an explicit Heavenly role mark. Dan12:1 names Michael as great prince; this does not make him a human civil official.',
        'heavenly-prince', ['BHSA:27:12:1','KJV:27:12:1'])
    decide([20], 'reframed',
        'Owner permits the resurrection-context theological role with an explicit Heavenly role mark; Rev1:5 names Jesus ruler of the earths kings. No earthly kingship office is asserted.',
        'heavenly-ruler-of-kings', ['NESTLE1904:66:1:5','KJV:66:1:5'])
    decide([24], 'reframed',
        'Owner permits directly stated theological titles in the separate Heavenly roles category. Heb3:1 names Jesus apostle of the confession beside high priest; this is kept distinct from the historical Twelve.',
        'heavenly-apostle', ['NESTLE1904:58:3:1','KJV:58:3:1'])
    decide([27,28,92], 'reframed',
        'Owner explicitly permits Jesus high priesthood with the Heavenly role mark and forbids presenting it as his earthly employment. Heb7:20-28 explains the oath, permanent priesthood and exaltation above the heavens; synonymous great-priest/priest/high-priest findings normalize to one celestial office.',
        'heavenly-high-priest', ['NESTLE1904:58:7:21','NESTLE1904:58:7:22','NESTLE1904:58:7:26','KJV:58:7:26','KJV:58:9:11','NESTLE1904:58:10:21'])
    decide([16], 'title_only',
        'Gog is a figure addressed in a future oracle. A predicted human prince is not automatically a celestial role; no actual held terrestrial profession is established here. Do not stretch the owners Heavenly role decision to this case.')
    decide([96,97,99], 'reframed',
        'Owner explicitly chooses the nonexclusive Prophet or teacher label for the mixed Acts13:1 roster. Membership is stated; neither both offices nor a particular one is guessed.',
        'prophet-or-teacher', ['NESTLE1904:44:13:1','KJV:44:13:1'])
    decide([100], 'reframed',
        'Owner explicitly accepts farmer from the full1KI19:19-21 narrative of Elisha working the twelve-team plowing and leaving that work. Preserve all three verses and the source identity; this is a recorded bounded case decision.',
        'farmer', ['BHSA:11:19:19','BHSA:11:19:20','BHSA:11:19:21','KJV:11:19:19','KJV:11:19:20','KJV:11:19:21'])
    if owner.get('barjesus_ahab_false_prophets') is not True or owner.get('skip_jezebel') is not True:
        raise ValueError('The owner false-prophet/Jezebel case ruling is missing.')
    decide([25], 'reframed',
        'Owner specifically admits Bar-Jesus as false prophet. Acts13:6 names him with pseudoprophetes; preserve that exact qualifier, never ordinary prophet.',
        'false-prophet', ['NESTLE1904:44:13:6','KJV:44:13:6'])
    decide([95], 'reframed',
        'Owner specifically admits Ahab son of Kolaiah as false prophet from the named lying prophetic activity inJer29:21-23. Keep him distinct from King Ahab and keep the false qualifier.',
        'false-prophet', ['BHSA:24:29:21','BHSA:24:29:23','KJV:24:29:21','KJV:24:29:23'])
    decide([98], 'owner_excluded',
        'Owner explicitly says to skip JezebelRev2:20 as probably figurative rather than a profession. No prophetess or teacher profession is admitted from this passage.')
    withdrawals = [{'person': person, 'occupation': occupation,
        'why': 'The only profession witness is the mixed roster Acts13:1; no individual role distribution is stated. Retract the overstated individual assignment pending a nonexclusive roster decision.'}
        for person, occupation in [('manaen','prophet'),('manaen','religious-teacher'),('simeon-4','religious-teacher'),('saul-2','prophet')]]
    if (RUN / 'decisions.json').exists():
        if not revise or (RUN / 'admission.json').exists():
            raise ValueError('Review already recorded/admitted; explicit draft revision required.')
        revision = len(list(RUN.glob('decisions-rev*.json'))) + 1
        (RUN / f'decisions-rev{revision}.json').write_bytes((RUN / 'decisions.json').read_bytes())
    write(RUN / 'decisions.json', {'sourceManifestSha256': sha(RUN / 'manifest.json'),
        'reviewedBy': 'Codex lead, explicit expanded Hebrew/Greek/KJV source reading,TSK-1254',
        'reviewedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'ownerDecisionsSha256': sha(owner_path),
        'decisions': decisions, 'withdrawals': withdrawals,
        'supplementalClaims': [{'person': 'manaen', 'occupationSlug': 'prophet-or-teacher',
            'why': 'Same source roster and owner combined-role ruling replace both overconfident individual Manaen assignments.',
            'source_keys': ['NESTLE1904:44:13:1','KJV:44:13:1']}]})
    print(json.dumps({'reviewed': len(decisions), 'dispositions': {v: sum(d['verdict'] == v for d in decisions) for v in sorted({d['verdict'] for d in decisions})}}, ensure_ascii=False))


def new_concepts():
    terms = {
        'queen-mother': ('office', {
            'eng': ('Queen mother','queen mother','queen mother'),
            'ukr': ('Цариця-мати','цариця-мати','цариця-мати'),
            'deu': ('Königinmutter','Königinmutter','Königinmutter'),
            'spa': ('Reina madre','reina madre','reina madre')}),
        'temple-cake-maker': ('religious_role', {
            'eng': ('Temple cake maker','temple cake maker','temple cake maker'),
            'ukr': ('Пекар храмових коржів','пекар храмових коржів','пекарка храмових коржів'),
            'deu': ('Bäcker der Tempelfladen','Bäcker der Tempelfladen','Bäckerin der Tempelfladen'),
            'spa': ('Panadero de tortas del templo','panadero de tortas del templo','panadera de tortas del templo')})}
    for slug, labels in {
        'prophet-or-teacher': {'eng':('Prophet or teacher','prophet or teacher','prophet or teacher'), 'ukr':('Пророк або вчитель','пророк або вчитель','пророчиця або вчителька'), 'deu':('Prophet oder Lehrer','Prophet oder Lehrer','Prophetin oder Lehrerin'), 'spa':('Profeta o maestro','profeta o maestro','profetisa o maestra')},
        'false-prophet': {'eng':('False prophet','false prophet','false prophetess'), 'ukr':('Лжепророк','лжепророк','лжепророчиця'), 'deu':('Falscher Prophet','falscher Prophet','falsche Prophetin'), 'spa':('Falso profeta','falso profeta','falsa profetisa')}
    }.items():
        terms[slug] = ('religious_role', labels)
    heavenly = {
        'heavenly-high-priest': {'eng':'High priest','ukr':'Первосвященник','deu':'Hohepriester','spa':'Sumo sacerdote'},
        'heavenly-apostle': {'eng':'Apostle','ukr':'Апостол','deu':'Apostel','spa':'Apóstol'},
        'heavenly-ruler-of-kings': {'eng':'Ruler of the kings of the earth','ukr':'Володар царів землі','deu':'Herrscher über die Könige der Erde','spa':'Soberano de los reyes de la tierra'},
        'heavenly-prince': {'eng':'Great prince','ukr':'Великий князь','deu':'Großer Fürst','spa':'Gran príncipe'},
        'heavenly-commander': {'eng':'Commander of the LORD’s army','ukr':'Вождь Господнього війська','deu':'Anführer des Heeres des HERRN','spa':'Jefe del ejército del SEÑOR'}
    }
    for slug, labels in heavenly.items():
        terms[slug] = ('heavenly_role', {language: (name, name, name) for language, name in labels.items()})
    marks = {'eng':'Heavenly role','ukr':'Небесна роль','deu':'Himmlische Rolle','spa':'Función celestial'}
    return [{'slug': slug, 'category': category, 'labels': {language: {'name': words[0], 'masculine': words[1], 'feminine': words[2], 'description': marks[language] if category == 'heavenly_role' else None} for language, words in labels.items()}} for slug, (category, labels) in terms.items()]


def validate_review(document, candidates, verses, people):
    expected = {c['id']: c for c in candidates}
    found = set()
    for decision in document['decisions']:
        ident = decision['id']
        if ident in found or ident not in expected or decision['person'] != expected[ident]['person']:
            raise ValueError('Review has a duplicate/foreign candidate or person.')
        found.add(ident)
        if decision['verdict'] not in {'reframed', 'covered', 'not_established', 'status_only', 'title_only', 'metaphor', 'owner_excluded', 'uncertain_identity', 'uncertain_function', 'needs_owner'}:
            raise ValueError('Unknown source-review verdict.')
        if not decision.get('why') or not decision.get('reviewed_source_keys'):
            raise ValueError('Every disposition needs a source reading and rationale.')
        if decision['claims'] and decision['verdict'] != 'reframed':
            raise ValueError('A held or closed candidate cannot publish a claim.')
        for key in decision['reviewed_source_keys']:
            if key not in verses:
                raise ValueError('Source context is not in the frozen primary snapshot: ' + key)
        for claim in decision['claims']:
            if not claim['source_keys'] or not set(claim['source_keys']).issubset(decision['reviewed_source_keys']):
                raise ValueError('Claim has no explicitly read source.')
            indexed = {(r['book'], r['chapter'], r['verse']) for r in people[decision['person']]['references']}
            if not any(tuple(verses[key]['canonical']) in indexed for key in claim['source_keys']):
                raise ValueError('No selected source is bound to the registered person; an explicit alias/identity bridge is required.')
    if found != set(expected):
        raise ValueError('All hundred candidates need a disposition.')


def compile_publication():
    manifest = read(RUN / 'manifest.json')
    for entry in manifest['reviewFiles']:
        if sha(RUN / entry['path']) != entry['sha256']:
            raise ValueError('Frozen review input changed.')
    if sha(BASE / 'bundle.json') != BASE_HASH:
        raise ValueError('Admitted baseline changed.')
    document = read(RUN / 'decisions.json')
    if not document.get('reviewedBy') or document['sourceManifestSha256'] != sha(RUN / 'manifest.json'):
        raise ValueError('Missing named reviewer or source manifest binding.')
    if document.get('ownerDecisionsSha256') != sha(RUN / 'owner-decisions.json'):
        raise ValueError('Owner case decisions changed after review.')
    verses = read(RUN / 'source-verses.json')
    people = {p['slug']: p for p in read(RUN / 'people.json')}
    validate_review(document, read(RUN / 'candidates.json'), verses, people)
    bundle = copy.deepcopy(read(BASE / 'bundle.json'))
    removed = set()
    for withdrawal in document['withdrawals']:
        pair = (withdrawal['person'], withdrawal['occupation'])
        matched = [a for a in bundle['assertions'] if (a['personSlug'], a['occupationSlug']) == pair]
        if not matched or not withdrawal.get('why') or {w['canonicalReference'] for a in matched for w in a['witnesses'] if w['kind'] == 'profession'} != {'44 13:1'}:
            raise ValueError('Withdrawal must be the explicitly reviewed unsupported mixed-roster assignment.')
        bundle['assertions'] = [a for a in bundle['assertions'] if (a['personSlug'], a['occupationSlug']) != pair]
        removed.add(pair)
    bundle['occupations'].extend(new_concepts())
    for concept in bundle['occupations']:
        if concept['slug'] == 'temple-work-overseer':
            concept['labels']['ukr'].update(name='Наглядач храмових робіт', masculine='наглядач храмових робіт', feminine='наглядачка храмових робіт')
        if concept['slug'] == 'farmer':
            concept['labels']['ukr'].update(name='Землероб', masculine='землероб', feminine='землеробка')
    concepts = {c['slug'] for c in bundle['occupations']}
    extra = []
    supplemental = [{'id': 'antioch-manaen-correction', 'person': c['person'], 'why': c['why'],
        'claims': [{'occupationSlug': c['occupationSlug'], 'source_keys': c['source_keys']}]}
        for c in document.get('supplementalClaims', [])]
    if len(supplemental) != 1 or supplemental[0]['person'] != 'manaen' or supplemental[0]['claims'] != [{'occupationSlug': 'prophet-or-teacher', 'source_keys': ['NESTLE1904:44:13:1','KJV:44:13:1']}]:
        raise ValueError('The only supplemental claim is the explicitly reviewed Manaen roster correction.')
    for decision in document['decisions'] + supplemental:
        for claim in decision['claims']:
            if claim['occupationSlug'] not in concepts:
                raise ValueError('Claim names an unreviewed occupation concept.')
            witnesses = []
            for key in claim['source_keys']:
                verse = verses[key]
                text, book, chapter, number = key.split(':')
                canonical = verse['canonical']
                if canonical not in verse.get('canonicalAddresses', [canonical]):
                    raise ValueError('Source canonical placement changed.')
                body = unicodedata.normalize('NFC', verse['body'])
                witnesses.append({'kind': 'profession', 'text': text,
                    'reference': f'{int(book)} {int(chapter)}:{int(number)}',
                    'canonicalReference': f'{canonical[0]} {canonical[1]}:{canonical[2]}',
                    'quote': body, 'sourceText': body, 'sourceSha256': hashlib.sha256(body.encode()).hexdigest()})
            extra.append({'personSlug': decision['person'], 'occupationSlug': claim['occupationSlug'],
                'witnesses': witnesses, 'origin': {'reviewedBy': document['reviewedBy'],
                'heldCandidate': decision['id'], 'reviewDecision': decision['why'],
                'sourceManifestSha256': sha(RUN / 'manifest.json')}})
    bundle['assertions'].extend(extra)
    base_pairs = {(a['personSlug'], a['occupationSlug']) for a in read(BASE / 'bundle.json')['assertions']}
    pairs = {(a['personSlug'], a['occupationSlug']) for a in bundle['assertions']}
    if base_pairs - pairs != removed:
        raise ValueError('An unreviewed existing occupation would be removed.')
    proof = {'baselineSha256': BASE_HASH, 'manifestSha256': sha(RUN / 'manifest.json'),
        'decisionsSha256': sha(RUN / 'decisions.json'),
        'ownerDecisionsSha256': sha(RUN / 'owner-decisions.json'),
        'researchSha256': sha(RUN / 'research.json'),
        'compilerSha256': hashlib.sha256(Path(__file__).read_text(encoding='utf-8-sig').encode()).hexdigest(),
        'reviewedBy': document['reviewedBy'], 'reviewedAt': document['reviewedAt']}
    write(RUN / 'publication-proof.json', proof)
    bundle['review'] = {'status': 'reviewed', 'reviewedBy': document['reviewedBy'], 'reviewedAt': document['reviewedAt']}
    bundle['source']['sha256'] = sha(RUN / 'publication-proof.json')
    bundle['source']['uri'] = 'urn:essenthos:occupation-held-review-2026-10-27-v1:publication-proof'
    bundle['source']['attribution'] += ' Expanded primary-source review of the hundred held candidates by Codex lead.'
    write(RUN / 'bundle.json', bundle)
    people = {p['slug']: p for p in read(RUN / 'people.json')}
    selected = sorted({a['personSlug'] for a in bundle['assertions']})
    write(RUN / 'preview-people.json', {'people': [{'slug': slug, 'name': people[slug]['name'], 'sex': people[slug]['sex'],
        'localNames': {language: values[0] for language, values in people[slug]['localNames'].items() if values}} for slug in selected]})
    summary = {'reviewed_candidates': len(document['decisions']), 'added_pairs': len(pairs - base_pairs),
        'withdrawn_overstated_pairs': len(removed), 'occupations': len(bundle['occupations']), 'people': len(selected),
        'pairs': len(pairs), 'bundle_sha256': sha(RUN / 'bundle.json'),
        'dispositions': {v: sum(d['verdict'] == v for d in document['decisions']) for v in sorted({d['verdict'] for d in document['decisions']})},
        'paid_calls': 0}
    write(RUN / 'summary.json', summary)
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'context', 'candidates', 'record', 'revise', 'compile'))
    parser.add_argument('--book', type=int, default=1)
    parser.add_argument('--chapter', type=int, default=1)
    parser.add_argument('--verses', default='1-200')
    parser.add_argument('--text', default='KJV,BHSA')
    parser.add_argument('--skip', type=int, default=0)
    parser.add_argument('--take', type=int, default=20)
    args = parser.parse_args()
    if args.mode == 'prepare':
        prepare()
    elif args.mode == 'context':
        context(args)
    elif args.mode == 'record':
        record_review()
    elif args.mode == 'revise':
        record_review(revise=True)
    elif args.mode == 'compile':
        compile_publication()
    else:
        candidates(args)
