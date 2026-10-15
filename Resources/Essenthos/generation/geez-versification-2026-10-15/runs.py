import pickle, collections, sys
exec(open('derive_load.py', encoding='utf-8').read())
out = pickle.load(open(sys.argv[1], 'rb'))
def moved(o): return o[3] not in ('no-greek', 'none') and o[1] and o[0].canon not in o[1]
runs = []; cur = []
prev = None
for o in out:
    key = (o[0].book, o[0].canon[1] if o[0].canon else None)
    if moved(o) and cur and key == prev: cur.append(o)
    else:
        if cur: runs.append(cur)
        cur = [o] if moved(o) else []
    prev = key
if cur: runs.append(cur)
ev = sorted(((sum(o[8] for o in r), r) for r in runs), key=lambda x: x[0])
for lo, hi in [(-1e9, 0), (0, 1), (1, 2), (2, 4), (4, 8), (8, 1e9)]:
    sel = [x for x in ev if lo <= x[0] < hi]
    print(f'evidence {lo}..{hi}: runs {len(sel)} verses {sum(len(r) for _, r in sel)}',
          [f'{r[0][0].book}:{r[0][0].chapter}:{r[0][0].number}x{len(r)}' for _, r in sel[:15]])
pickle.dump(ev, open('runs.pkl', 'wb'))
