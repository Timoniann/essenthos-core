"""
Scores four variants on the same passages against the same independent gold, pair by pair:
A the aligner alone (IBM-4, the pairs an alignment run would store), B EVIDENTIA without the
learned index, C EVIDENTIA as it runs, D C with the aligner filling the source words C leaves
unplaced and does not mark supplied.

usage: evidentia-variants.py <C words dir> <B words dir> <aligner pairs tsv>... -- <passage names>

  C: the <name>.words.json files evidentia-benchmark.ps1 writes for a run set;
  B: the same passages measured by evidentia-measure-book with --without-known-renderings --words;
  aligner pairs: `score BSB BHSA --stated --pairs <file>` and `score BSB NESTLE1904 --stated --pairs <file>`.
The aligner learns from no alignment, so it is scored on the same answer key as the others.
Pair metric throughout: precision over gold-covered source words, recall over gold pairs, which is
the report's 'by word, links ... by pair' line.
"""
import json
import sys
from pathlib import Path

args = sys.argv[1:]
cut = args.index('--')
c_dir, b_dir, *ibm_files = args[:cut]
passages = args[cut + 1:]

ibm = {}
for path in ibm_files:
    for line in open(path, encoding='utf-8'):
        source, target, *_ = line.rstrip('\n').split('\t')
        ibm.setdefault(int(source), set()).add(int(target))


def load(folder, name):
    return json.load(open(Path(folder) / f'{name}.words.json', encoding='utf-8'))


def score(words, placed):
    """placed: source word id -> set of target ids."""
    gold_pairs = sum(len(w['Gold']) for w in words)
    covered = {w['SourceWordId'] for w in words if w['Gold']}
    gold = {w['SourceWordId']: {g['TargetWordId'] for g in w['Gold']} for w in words}
    proposed = right = 0
    for word, targets in placed.items():
        if word not in covered:
            continue
        for target in targets:
            proposed += 1
            right += target in gold[word]
    return right, proposed, gold_pairs


def evidentia(words):
    return {w['SourceWordId']: {w['TargetWordId']} for w in words if w.get('TargetWordId')}


rows = {}
totals = {key: [0, 0, 0] for key in 'ABCD'}
for name in passages:
    c = load(c_dir, name)
    b = load(b_dir, name)
    ids = {w['SourceWordId'] for w in c}
    a_placed = {word: ibm[word] for word in ids if word in ibm}
    c_placed = evidentia(c)
    absent = {w['SourceWordId'] for w in c if w.get('Absence')}
    d_placed = dict(c_placed)
    for word, targets in a_placed.items():
        if word not in d_placed and word not in absent:
            d_placed[word] = targets
    for key, placed, words in (('A', a_placed, c), ('B', evidentia(b), b), ('C', c_placed, c), ('D', d_placed, c)):
        r, p, g = score(words, placed)
        rows.setdefault(name, {})[key] = (r, p, g)
        for i, v in enumerate((r, p, g)):
            totals[key][i] += v


def cell(r, p, g):
    return f'{r:,}/{p:,} = {100 * r / max(p, 1):.2f}% | {100 * r / max(g, 1):.2f}%'


print('passage | ' + ' | '.join(f'{k} precision | {k} recall' for k in 'ABCD'))
for name, cells in rows.items():
    print(name + ' | ' + ' | '.join(cell(*cells[k]) for k in 'ABCD'))
print('all | ' + ' | '.join(cell(*totals[k]) for k in 'ABCD'))
