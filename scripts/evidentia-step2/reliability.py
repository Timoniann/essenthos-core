"""The combiner's bands read against the split key: for every word the combiner placed, the calibrated probability
it stated and whether the key counts the placement right, by band and by testament; then each state's precision.

usage: reliability.py <label> <sets>
"""
import collections
import glob
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.join(os.environ.get('TEMP', r'C:\Users\timon\AppData\Local\Temp'), 'essenthos-evidentia')
BANDS = ((0.98, 1.01, 'safe band'), (0.95, 0.98, 'review band'), (0.90, 0.95, 'kept below the band'),
         (0.80, 0.90, 'kept below the band'), (0.60, 0.80, 'kept below the band'))


def words(label, sets):
    for s in sets:
        for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.words.json'))):
            testament = 'NT' if any(book in f for book in ('mark', 'john', 'rom', 'matt', 'heb', '2cor', 'rev')) else 'OT'
            for w in json.load(open(f, encoding='utf-8')):
                w['_t'] = testament
                yield w


def main():
    label, sets = sys.argv[1], sys.argv[2].split(',')
    all_words = list(words(label, sets))
    placed = [w for w in all_words if w.get('CombinerProbability') is not None and w.get('TargetWordId')]
    print(f'{label} {sets}: {len(all_words)} words; {len(placed)} placed by the combiner or kept by it')
    for scope in ('all', 'OT', 'NT'):
        mine = [w for w in placed if scope == 'all' or w['_t'] == scope]
        print(f'  {scope}:')
        for lo, hi, name in BANDS:
            band = [w for w in mine if lo <= w['CombinerProbability'] < hi]
            judged = [w for w in band if w.get('CorrectOnSplitKey') is not None]
            if not judged:
                continue
            right = sum(1 for w in judged if w['CorrectOnSplitKey'])
            judged_key = sum(1 for w in judged if w.get('CorrectOnJudgedKey'))
            stated = sum(w['CombinerProbability'] for w in judged) / len(judged)
            print(f'    {name:20s} {lo:.2f}-{min(hi, 1):.2f}: {len(band):5d} placed, {len(judged):5d} the key judges; stated {100 * stated:6.2f}%, '
                  f'observed {100 * right / len(judged):6.2f}% ({right}/{len(judged)}), against the judged key {100 * judged_key / len(judged):6.2f}%')
    kinds = collections.Counter()
    for w in placed:
        kinds[(w.get('ProposalKind'), w.get('CorrectOnSplitKey'))] += 1
    print('  by kind (right / judged):')
    for kind in sorted({k for k, _ in kinds}, key=str):
        right, wrong = kinds[(kind, True)], kinds[(kind, False)]
        print(f'    {kind}: {right}/{right + wrong}' + (f' = {100 * right / (right + wrong):.2f}%' if right + wrong else ''))


if __name__ == '__main__':
    main()
