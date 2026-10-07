"""
Publish the Genesis 10 ties as decided clauses on the peoples' current records (PRB-0896 follow-up).

Each record keeps its own model, date and claims, less the ones whose target is a record since folded
into the people itself; the descent the re-ask of 2026-10-07 read (and the Canaanite families of
GEN 10:18) is added as a clause an agent decided against the verse on the owner's instruction.
"""
import glob, json, os

WT = r'C:\Users\timon\Projects\Essenthos\essenthos-core\.worktrees\c2-lang'
RUN = os.path.join(os.path.dirname(__file__), 'gen10')
DESCRIPTORS = os.path.join(WT, 'Resources', 'Essenthos', 'descriptors')

records = {}
for f in sorted(glob.glob(os.path.join(DESCRIPTORS, '*.jsonl')), key=os.path.basename):
    for line in open(f, encoding='utf-8'):
        if line.strip():
            r = json.loads(line)
            records[r['entity']] = r

asked = {}
for f in sorted(glob.glob(os.path.join(RUN, 'out', '*.jsonl'))):
    for line in open(f, encoding='utf-8'):
        if line.strip():
            r = json.loads(line)
            asked[r['entity']] = r

# Folded into the people they stood for, so a clause pointing at one points at nobody.
FOLDED = {'arvad', 'girgash', 'kittim', 'pathrus', 'sini', 'zemar', 'casluh', 'jebus'}

VERSES = {
    'GEN 10:4': 'And the sons of Javan; Elishah, and Tarshish, Kittim, and Dodanim',
    'GEN 10:14': 'Mizraim begat ... Pathrusim, and Casluhim, (out of whom came Philistim,) and Caphtorim (10:13-14)',
    'GEN 10:16': 'Canaan begat Sidon his firstborn, and Heth, and the Jebusite, and the Amorite, and the Girgasite (10:15-16)',
    'GEN 10:17': 'Canaan begat ... the Hivite, and the Arkite, and the Sinite (10:15-17)',
    'GEN 10:18': 'Canaan begat ... the Arvadite, and the Zemarite, and the Hamathite (10:15-18)',
}

DECIDED = "an agent reading {ref} against the re-ask of 2026-10-07 (claude-sonnet-5), on the project owner's instruction, decided 2026-10-07"

added = {}
for slug, answer in asked.items():
    for claim in answer['claims']:
        if claim['relation'] in ('descendant-of', 'descendants-of') and claim['target'] in ('canaan', 'mizraim', 'casluhim', 'javan'):
            added.setdefault(slug, []).append({
                'relation': 'descendants-of', 'target': claim['target'], 'reference': claim['reference'],
                'confidence': None, 'reason': VERSES[claim['reference']],
                'decidedBy': DECIDED.format(ref=claim['reference'])})
for slug in ('arvadites', 'zemarites'):
    added.setdefault(slug, []).append({
        'relation': 'of-people', 'target': 'canaanites', 'reference': 'GEN 10:18', 'confidence': None,
        'reason': 'afterward were the families of the Canaanites spread abroad (10:18)',
        'decidedBy': DECIDED.format(ref='GEN 10:18')})

out = []
for slug in sorted(added):
    record = dict(records[slug])
    kept = [c for c in record.get('claims') or [] if c.get('target') not in FOLDED]
    held = {(c['relation'], c['target']) for c in kept}
    for claim in added[slug]:
        if (claim['relation'], claim['target']) in held:
            kept = [c for c in kept if (c['relation'], c['target']) != (claim['relation'], claim['target'])]
        kept.insert(0, claim)
    record['claims'] = kept
    out.append(record)
    print(slug, [(c['relation'], c['target'], c['reference'], 'decided' if c.get('decidedBy') else c.get('confidence')) for c in kept])

with open(os.path.join(DESCRIPTORS, 'zz-zz-gen10-0000.jsonl'), 'w', encoding='utf-8', newline='\n') as handle:
    for record in out:
        handle.write(json.dumps(record, ensure_ascii=False) + '\n')
print(len(out), 'records')
