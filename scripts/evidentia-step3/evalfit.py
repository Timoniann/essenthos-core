"""A fitted weights file read on sets it was neither fitted nor calibrated on: what the Forge's rule would decide there,
new placements apart from the tiers' own, by band.

usage: evalfit.py <weights.json> <label> <sets>
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'evidentia-step2'))
import fit_combiner as fc  # noqa: E402

from common import share  # noqa: E402

document = json.load(open(sys.argv[1], encoding='utf-8'))
models = document['models']
names, rows = fc.load(sys.argv[2], sys.argv[3].split(','))
if names != document['features']:
    raise SystemExit('the export has other features than the weights file')
picks = [(r, c) for r, c in fc.decide(models, rows) if r['Covered']]
covered = [r for r in rows if r['Covered']]
words = len({r['SourceWordId'] for r in covered})
base = [r for r in covered if r['Current']]
print(f'{sys.argv[3]}: {words} covered words weighed; the tiers place {len(base)}, right {sum(r["Label"] for r in base)}')
new = [(r, c) for r, c in picks if not r['Current']]
kept = [(r, c) for r, c in picks if r['Current']]
for name, sel in (('new', new), ('kept', kept)):
    right = sum(r['Label'] for r, _ in sel)
    print(f'  {name}: {share(right, len(sel))}')
    for lo, hi in ((0.98, 1.01), (0.95, 0.98), (0.90, 0.95), (0.80, 0.90), (0.60, 0.80)):
        band = [(r, c) for r, c in sel if lo <= c < hi]
        if band:
            print(f'    {lo:.2f}-{min(hi, 1):.2f}: {len(band):5d}, stated {100 * sum(c for _, c in band) / len(band):6.2f}%, '
                  f'observed {100 * sum(r["Label"] for r, _ in band) / len(band):6.2f}%')
withdrawn = {r['SourceWordId'] for r in base} - {r['SourceWordId'] for r, _ in picks}
print(f'  right after: {sum(r["Label"] for r, _ in picks)} against {sum(r["Label"] for r in base)} '
      f'({sum(r["Label"] for r, _ in picks) - sum(r["Label"] for r in base):+d}); withdrawn {len(withdrawn)}')
