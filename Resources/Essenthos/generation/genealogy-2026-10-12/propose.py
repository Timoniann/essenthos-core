import csv, re, json, collections, sys

def rows(path):
    with open(path, encoding='utf-8') as f:
        lines = [l.rstrip('\n') for l in f if '\t' in l and not l.startswith('Field')]
    head = lines[0].split('\t')
    return [dict(zip(head, l.split('\t'))) for l in lines[1:]]

inp = rows('forms-input.tsv')
linked = collections.defaultdict(dict)
for r in rows('forms-linked.tsv'):
    linked[r['slug']][r['txt']] = [(m.group(1), int(m.group(2))) for m in re.finditer(r'(\S+)x(\d+)', r['string_agg'])]

def parse_forms(s):
    out = collections.defaultdict(list)
    for tok in (s or '').split(' '):
        if '=' in tok:
            k, v = tok.split('=', 1)
            out[k].append(v)
    return out

skip = {'kish-5', 'oholibamah-2', 'basemath-daughter-of-ishmael', 'basemath', 'ethan-2', 'adaiah-2', 'ethni', 'chelubai'}
text_of = {'rus': 'RUSV', 'ukr': 'UBIO'}
out = []
for r in inp:
    if r['slug'] in skip:
        continue
    own = parse_forms(r['own_forms'])
    ns = parse_forms(r['namesakes'])
    rec = {'slug': r['slug'], 'name': r['name']}
    for lang, has in (('ukr', r['ukr'] == 't'), ('rus', r['rus'] == 't')):
        if has:
            continue
        printed = linked.get(r['slug'], {}).get(text_of[lang], [])
        if lang == 'ukr' and not printed:
            printed = linked.get(r['slug'], {}).get('UKR1871', [])
        noms = ns.get(f'{lang}.nominative', [])
        gens = ns.get(f'{lang}.genitive', [])
        pforms = [p for p, _ in printed]
        pick = None
        why = ''
        hit = [n for n in noms if n in pforms]
        if hit:
            pick = hit[0]; why = 'namesake form printed here'
        elif printed:
            pick = None; why = 'printed: ' + ' '.join(f'{p}x{n}' for p, n in printed)
        elif len(noms) == 1:
            pick = noms[0]; why = 'namesake'
        else:
            why = 'namesakes: ' + ' '.join(noms)
        rec[lang] = {'nom': pick, 'gens': gens, 'why': why}
    out.append(rec)

for rec in out:
    print(rec['slug'], '|', rec['name'])
    for lang in ('ukr', 'rus'):
        if lang in rec:
            x = rec[lang]
            print('   ', lang, x['nom'], '| gens', ' '.join(x['gens']), '|', x['why'])
json.dump(out, open('proposals.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
