"""X1b: the relations as features. A logistic model over the edges of every unresolved grammatical word whose parse
head is placed: the head's own word (fold) and each original word near the head's rendering. Fitted on one set,
calibrated (isotonic, on its picks) on a second, read on a third, so the bands are checked on words neither saw.

usage: x1b.py <label> <train sets> <calibration sets> <test sets> <original words .tsv> [--show <n>]

The function lexicon (how often the key puts an English function word on an original word of a given Strong number,
and on its head's own word) is counted on the training sets only.
"""
import collections
import math
import os
import random
import sys

import numpy as np

from common import PLACED, share, words
from x1 import Original, agrees, group_of, joined, same_written_word

sys.path.insert(0, __file__.rsplit('\\', 2)[0] + '\\evidentia-step2')
from fit_combiner import fit, isotonic  # noqa: E402

GROUPS = ['case', 'det the', 'nsubj pron', 'cc', 'aux', 'nmod:poss', 'obj pron', 'cop', 'mark', 'det a', 'mark to',
          'advmod not', 'compound:prt', 'expl', 'other']
TARGET_POS = ['prep', 'conj', 'art', 'verb', 'subs', 'nmpr', 'prps', 'nega', 'advb', 'adjv', 'prde', 'intj',
              'pron', 'det', 'noun', 'adj', 'ptcl', 'adv']
CASES = ['nominative', 'genitive', 'dative', 'accusative', 'vocative']
WINDOW = (-4, 3)


def edges_of(rows, original, taken):
    by_id = {r['SourceWordId']: r for r in rows}
    out = []
    for r in rows:
        if not r['Grammatical'] or r['State'] != 'unresolved':
            continue
        head = by_id.get(r['ParseHeadWordId'])
        if head is None or head['State'] not in PLACED or not head['TargetWordId']:
            continue
        H = original.by_id.get(head['TargetWordId'])
        if H is None:
            continue
        group = group_of(r) or 'other'
        key = set(r['SplitKey'])
        seen = set()
        for k in range(WINDOW[0], WINDOW[1] + 1):
            i = H['index'] + k
            if not 0 <= i < len(H['verse_words']):
                continue
            o = H['verse_words'][i]
            if k != 0 and o['id'] in taken:
                continue
            if o['id'] in seen:
                continue
            seen.add(o['id'])
            out.append({'word': r, 'head': head, 'H': H, 'o': o, 'k': k, 'group': group, 'label': o['id'] in key,
                        'surface': r['SourceSurface'].lower()})
    return out


class Lexicon:
    """P(key on an original word of this Strong number | English surface), and P(key on the head's word | surface)."""

    def __init__(self, edges):
        pair, word, fold = collections.Counter(), collections.Counter(), collections.Counter()
        seen = set()
        for e in edges:
            w = e['word']['SourceWordId']
            if w not in seen:
                seen.add(w)
                word[e['surface']] += 1
            if e['label']:
                pair[(e['surface'], e['o']['strong'] or e['o']['pos'])] += 1
                if e['k'] == 0:
                    fold[e['surface']] += 1
        self.pair, self.word, self.fold = pair, word, fold

    def p(self, e):
        n = self.word.get(e['surface'], 0)
        return (self.pair.get((e['surface'], e['o']['strong'] or e['o']['pos']), 0) + 0.1) / (n + 1)

    def p_fold(self, e):
        n = self.word.get(e['surface'], 0)
        return (self.fold.get(e['surface'], 0) + 0.1) / (n + 1)

    def seen(self, e):
        return min(self.word.get(e['surface'], 0), 50) / 50


def features(e, lex):
    o, H, k, r = e['o'], e['H'], e['k'], e['word']
    fold = 1.0 if k == 0 else 0.0
    sm = r['SourceMorphology'] or {}
    pron = sm.get('PronType') == 'Prs'
    if pron and (o['m'].get('suffixPerson') or o['m'].get('person')):
        agree = 1.0 if (agrees(r, o, suffix=True) or agrees(r, o)) else -1.0
    else:
        agree = 0.0
    p = lex.p(e)
    pf = lex.p_fold(e)
    f = [fold, k, abs(k), 1.0 if k < 0 else 0.0, 1.0 if same_written_word(o, H) else 0.0,
         math.log(p), p, math.log(pf) * fold, pf * fold, lex.seen(e), agree,
         1.0 if o['m'].get('suffixPerson') else 0.0, 1.0 if e['head']['Safe'] else 0.0,
         1.0 if (o['greek']) else 0.0]
    f += [1.0 if e['group'] == g else 0.0 for g in GROUPS]
    f += [fold * (1.0 if e['group'] == g else 0.0) for g in GROUPS]
    f += [1.0 if o['pos'] == t else 0.0 for t in TARGET_POS]
    f += [1.0 if o['m'].get('case') == c else 0.0 for c in CASES]
    f += [1.0 if H['m'].get('state') == 'c' else 0.0, 1.0 if H['pos'] == 'verb' else 0.0]
    return f


def raw(model, x):
    t = model['intercept'] + sum(w * (v - m) / s for w, v, m, s in zip(model['weights'], x, model['mean'], model['scale']))
    return 1 / (1 + math.exp(-t))


def calibrated(table, p):
    for hi, rate in table:
        if p <= hi:
            return rate
    return table[-1][1]


def picks(edges, prob):
    """The best edge of each word; an original word other than the head's is taken once."""
    by_word = collections.defaultdict(list)
    for e in edges:
        by_word[e['word']['SourceWordId']].append(e)
    ordered = sorted(edges, key=lambda e: -prob(e))
    taken, done, out = set(), set(), []
    for e in ordered:
        w = e['word']['SourceWordId']
        if w in done or (e['k'] != 0 and e['o']['id'] in taken):
            continue
        done.add(w)
        if e['k'] != 0:
            taken.add(e['o']['id'])
        out.append(e)
    return out


def main():
    args = sys.argv[1:]
    label, train_sets, check_sets, test_sets, tsv = args[0], args[1].split(','), args[2].split(','), args[3].split(','), args[4]
    show = int(args[args.index('--show') + 1]) if '--show' in args else 0
    original = Original(tsv)

    def load(sets):
        rows = [r for r in words(label, sets) if r['GoldCoversSourceWord']]
        taken = {r['TargetWordId'] for r in rows if r['State'] in PLACED and r['TargetWordId']}
        return rows, edges_of(rows, original, taken)

    train_rows, train = load(train_sets)
    check_rows, check = load(check_sets)
    test_rows, test = load(test_sets)
    lex = Lexicon(train)
    X = np.array([features(e, lex) for e in train])
    y = np.array([1.0 if e['label'] else 0.0 for e in train])
    model = fit(X, y)
    prob = lambda e: raw(model, features(e, lex))
    cal_picks = picks(check, prob)
    table = isotonic([prob(e) for e in cal_picks], [1.0 if e['label'] else 0.0 for e in cal_picks])

    for name, rows, es in (('train ' + ','.join(train_sets), train_rows, train),
                           ('calibration ' + ','.join(check_sets), check_rows, check),
                           ('test ' + ','.join(test_sets), test_rows, test)):
        ps = picks(es, prob)
        words_n = len({e['word']['SourceWordId'] for e in es})
        reach = len({e['word']['SourceWordId'] for e in es if e['label']})
        print(f'\n{name}: {len(rows):,} key-covered words; {words_n:,} unresolved grammatical words with a placed head, '
              f'{reach:,} with the key\'s word among their edges; {len(es):,} edges')
        for lo, hi in ((0.98, 1.01), (0.95, 0.98), (0.90, 0.95), (0.80, 0.90), (0.0, 0.80)):
            sel = [e for e in ps if lo <= calibrated(table, prob(e)) < hi]
            if sel:
                stated = sum(calibrated(table, prob(e)) for e in sel) / len(sel)
                right = sum(e['label'] for e in sel)
                print(f'  {lo:.2f}-{min(hi, 1):.2f}: {len(sel):5,} picks, stated {100 * stated:6.2f}%, observed '
                      f'{share(right, len(sel))}, {100 * right / len(rows):.2f} points right')
        for band in (0.95, 0.90):
            sel = [e for e in ps if calibrated(table, prob(e)) >= band]
            right = sum(e['label'] for e in sel)
            print(f'  at {band:.2f} or above: {share(right, len(sel))}; +{100 * right / len(rows):.2f} points of all words; '
                  f'by relation: ' + ', '.join(f'{g} {sum(e["label"] for e in sel if e["group"] == g)}/{sum(1 for e in sel if e["group"] == g)}'
                                             for g in GROUPS if any(e['group'] == g for e in sel)))
        if show and name.startswith('test'):
            rng = random.Random(20261015)
            wrong = [e for e in ps if calibrated(table, prob(e)) >= 0.95 and not e['label']]
            for e in rng.sample(wrong, min(show, len(wrong))):
                r = e['word']
                key = ' '.join(f'{g["Surface"]}({g["Gloss"]})' + ('*' if g['TargetWordId'] in r['SplitKey'] else '') for g in r['Gold'])
                print(f'    wrong {r["Passage"]} {r["CanonicalBook"]}:{r["CanonicalChapter"]}:{r["CanonicalVerse"]} {r["SourceSurface"]!r} '
                      f'[{e["group"]}] head {e["head"]["SourceSurface"]!r}->{e["H"]["surface"]} k={e["k"]} -> {e["o"]["surface"] or "(empty)"}({e["o"]["gloss"]}) | key: {key}')


if __name__ == '__main__':
    main()
