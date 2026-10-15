import pickle, collections, csv
exec(open('derive_load.py', encoding='utf-8').read())
F = pickle.load(open('final.pkl', 'rb'))
moved = {o[0].id: (set(o[1]) if o[3] != 'none' else set()) for o in F['lines']}
moved_books = {o[0].book for o in F['lines']}
word_verse = {}
for vid, ws in words_of.items():
    for _, _, wid, _ in ws: word_verse[wid] = vid
# Swete words were not loaded into state; read their verse ids directly
for r in csv.reader(open('words.csv', encoding='utf-8')):
    if r and r[0] == 'SWETE': word_verse[int(r[1])] = int(r[2])
links = collections.Counter(); wrong = collections.Counter(); per_verse = collections.Counter()
for r in csv.reader(open('links.csv', encoding='utf-8')):
    if len(r) < 5: continue
    slug, lid, fw, tw = r[0], int(r[1]), int(r[2]), int(r[3])
    gv = word_verse.get(fw); tv = word_verse.get(tw)
    if gv is None or tv is None: continue
    per_verse[(slug, gv)] += 1
    if gv in moved:
        links[slug] += 1
        if verses[tv].canon not in moved[gv]: wrong[slug] += 1
gwords = {vid: len(ws) for vid, ws in words_of.items()}
for slug in ('GRCBRENT', 'SWETE', 'NESTLE1904', 'RP2018'):
    nt = slug in ('NESTLE1904', 'RP2018')
    def dens(sel):
        w = sum(gwords.get(v.id, 0) for v in sel); l = sum(per_verse[(slug, v.id)] for v in sel)
        return l / w if w else 0, w
    pool = [v for v in order_in_text['GEEZ81'] if v.book in moved_books and (40 <= v.book <= 66) == nt]
    a = dens([v for v in pool if v.id in moved]); b = dens([v for v in pool if v.id not in moved])
    print(f'{slug}: links per Ge\'ez word in moved verses {a[0]:.3f} ({a[1]} words) vs the other verses of those books {b[0]:.3f}; '
          f'links on moved verses {links[slug]}, of them to a Greek verse the map does not pair {wrong[slug]}')
