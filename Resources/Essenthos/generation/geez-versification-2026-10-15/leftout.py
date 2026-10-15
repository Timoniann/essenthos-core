"""The verses the alignment would have moved on too little evidence, for whoever reviews the map."""
import pickle, sys
exec(open('derive_load.py', encoding='utf-8').read())
ev = pickle.load(open('runs.pkl', 'rb'))
rows = ['# Ge\'ez verses the alignment would have moved, left at their own numbers because the run they stand in',
        '# gained too little over those numbers (under 8 points, or under 4 for a run of three or more).',
        '# book\tgeez\tgreek\trelation\tevidence']
for e, r in sorted(ev, key=lambda x: (x[1][0][0].book, x[1][0][0].chapter, x[1][0][0].number)):
    if e >= 8 or (len(r) >= 3 and e >= 4): continue
    for o in r:
        v = o[0]
        rows.append(f"{v.book}\t{v.chapter}:{v.number}\t{';'.join(f'{t[1]}:{t[2]}' for t in o[1])}\t{o[3]}\t{e:.1f}")
open(sys.argv[1], 'w', encoding='utf-8', newline='\n').write('\n'.join(rows) + '\n')
print(len(rows) - 3)
