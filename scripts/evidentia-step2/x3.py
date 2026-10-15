"""X3 offline: one calibrated decision per content word over every signal a word record holds, with the pivot.

Population: content words the key covers whose state is linked by a lexical tier or unresolved: the decisions a
combiner would own. Edges: the four strongest candidates a record keeps, plus every word a pivot names. Label: the
split key names the edge's target. Logistic regression (numpy), fitted on the train label's sets, threshold read on
the check sets, reported on the test sets. One pick per word and per target, greedy by probability within a verse;
targets held by a grammatical-word placement are not free. Nothing here reads the database.
usage: x3.py <train label> <train sets> <check sets> <test label> <test sets> [pivots]
"""
import json, sys, glob, os, collections
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
train_label, train_sets, check_sets, test_label, test_sets = sys.argv[1], sys.argv[2].split(','), sys.argv[3].split(','), sys.argv[4], sys.argv[5].split(',')
PIVOTS = sys.argv[6].split(',') if len(sys.argv) > 6 else ['KJV', 'ULT', 'YLT', 'RLT2018']
USE_PIVOT = '--no-pivot' not in sys.argv
KINDS = ['DictionarySense', 'KnownRendering', 'TargetGloss', 'Morphology', 'MatchingNormalisedForm', 'Syntax']
POS = ['NOUN', 'PROPN', 'VERB', 'ADJ', 'ADV', 'NUM']
KIND_OF_PLACEMENT = ['GlobalReviewKnownRendering', 'UniqueDictionarySenseReview', 'UniqueTargetGlossReview', 'ResidualKnownRendering',
                     'AnchoredGapReview', 'RepeatedRenderingInOrder', 'UniqueContextGlossReview', 'DictionaryAndGlossReview']


def load(label, sets):
    words = []
    for s in sets:
        for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.words.json'))):
            p = os.path.basename(f).split('.')[0]
            for w in json.load(open(f, encoding='utf-8')):
                w['_p'] = p
                words.append(w)
    return words


def owned(w):
    return (not w.get('Grammatical') and w['GoldCoversSourceWord'] and (w.get('SplitKey') is not None)
            and (w.get('State') == 'unresolved' or (w.get('State') == 'linked' and w.get('Rule') == 'lexical')))


def rows(words):
    byverse = collections.defaultdict(list)
    pos, strong = {}, {}
    for w in words:
        byverse[(w['_p'], w['CanonicalChapter'], w['CanonicalVerse'])].append(w)
        for g in (w.get('Gold') or []):
            pos[g['TargetWordId']] = g['Position']
            strong[g['TargetWordId']] = g['StrongNumber']
        for c in (w.get('Candidates') or []):
            strong.setdefault(c['TargetWordId'], c['TargetStrongNumber'])
    out = []
    for key, verse in byverse.items():
        ns = max(x['SourcePosition'] for x in verse)
        nt = max([pos.get(t, 0) for t in pos] + [1])
        fixed = {x['TargetWordId'] for x in verse if x['TargetWordId'] and not owned(x) and x.get('State') == 'linked'}
        claim = collections.Counter()
        edges = {}
        for w in verse:
            if not owned(w):
                continue
            cands = {c['TargetWordId']: c for c in (w.get('Candidates') or [])}
            piv = {k: set(v) for k, v in (w.get('Pivot') or {}).items() if k in PIVOTS} if USE_PIVOT else {}
            named = set().union(*piv.values()) if piv else set()
            ts = (set(cands) | named) - fixed
            edges[w['SourceWordId']] = (w, cands, piv, ts)
            for t in ts:
                claim[t] += 1
        seen = sorted({t for (_, _, _, ts) in edges.values() for t in ts})
        rank = {t: i / max(1, len(seen) - 1) for i, t in enumerate(seen)}
        for sid, (w, cands, piv, ts) in edges.items():
            if not ts:
                continue
            key_t = set(w.get('SplitKey') or [])
            scores = sorted((c['Score'] for c in cands.values()), reverse=True) or [0]
            for t in ts:
                c = cands.get(t)
                ev = set(c['Evidence']) if c else set()
                sc = c['Score'] if c else 0.0
                share = (c.get('RenderingShare') or 0.0) if c else 0.0
                tp = pos.get(t)
                d = abs(rank[t] - (w['SourcePosition'] - 1) / max(1, ns - 1))
                second = max([x['Score'] for tt, x in cands.items() if tt != t] or [0])
                n_piv = sum(1 for v in piv.values() if t in v)
                other = 1.0 if piv and n_piv == 0 else 0.0
                current = 1.0 if w['TargetWordId'] == t else 0.0
                x = [1.0, sc, share, np.sqrt(share), sc - second, 1.0 if c and sc >= scores[0] else 0.0, 1.0 / max(1, len(cands)),
                     1.0 / claim[t], d, d * d, 1.0 if c is None else 0.0,
                     1.0 if c and (c['TargetStrongNumber'] or '').startswith('H90') else 0.0, float(len(ev)),
                     float(n_piv), 1.0 if n_piv >= 2 else 0.0, other, 1.0 if piv else 0.0,
                     current, current * (1.0 if w.get('Safe') else 0.0)]
                x += [1.0 if k in ev else 0.0 for k in KINDS]
                x += [1.0 if w.get('SourcePartOfSpeech') == p else 0.0 for p in POS]
                x += [1.0 if (t in piv.get(p, ())) else 0.0 for p in PIVOTS]
                x += [current * (1.0 if w.get('ProposalKind') == k else 0.0) for k in KIND_OF_PLACEMENT]
                x += [n_piv * (1.0 if ev & {'KnownRendering', 'DictionarySense', 'TargetGloss'} else 0.0), other * current]
                out.append((key, w, t, np.array(x), 1.0 if t in key_t else 0.0))
    return out


def fit(X, y, l2=1.0, steps=200):
    wt = np.zeros(X.shape[1])
    for _ in range(steps):
        p = 1 / (1 + np.exp(-X @ wt))
        g = X.T @ (p - y) + l2 * wt
        H = (X * (p * (1 - p))[:, None]).T @ X + l2 * np.eye(X.shape[1])
        step = np.linalg.solve(H, g)
        wt -= step
        if np.abs(step).max() < 1e-7:
            break
    return wt


def decode(rws, X, wt):
    p = 1 / (1 + np.exp(-X @ wt))
    byverse = collections.defaultdict(list)
    for (key, w, t, _, label), prob in zip(rws, p):
        byverse[key].append((prob, w, t, label))
    picks = []
    for key, es in byverse.items():
        used_w, used_t = set(), set()
        for prob, w, t, label in sorted(es, key=lambda e: -e[0]):
            if w['SourceWordId'] in used_w or t in used_t:
                continue
            used_w.add(w['SourceWordId'])
            used_t.add(t)
            picks.append((prob, w, t, label))
    return picks


def report(name, words, picks, thresholds):
    n = len(words)
    pop = [w for w in words if owned(w)]
    base_placed = [w for w in pop if w['TargetWordId']]
    base_right = sum(1 for w in base_placed if w.get('CorrectOnSplitKey'))
    print(f'\n{name}: {n} words; {len(pop)} owned decisions; EVIDENTIA places {len(base_placed)}, right {base_right} = {100 * base_right / max(1, len(base_placed)):.2f}%')
    print('  threshold  placed  right  precision  vs EVIDENTIA (right, points of all words)')
    for th in thresholds:
        sel = [r for r in picks if r[0] >= th]
        right = int(sum(r[3] for r in sel))
        print(f'  {th:5.2f}   {len(sel):6d} {right:6d}   {100 * right / max(1, len(sel)):6.2f}%   {right - base_right:+5d} ({100 * (right - base_right) / n:+.2f})')
    print('  reliability (band: picks, observed right)')
    for lo, hi in ((0.98, 1.01), (0.95, 0.98), (0.9, 0.95), (0.8, 0.9), (0.6, 0.8), (0.4, 0.6), (0, 0.4)):
        sel = [r for r in picks if lo <= r[0] < hi]
        if sel:
            print(f'    {lo:.2f}-{min(hi, 1):.2f}: {len(sel):5d}  {100 * sum(r[3] for r in sel) / len(sel):6.2f}%')


tr_words = load(train_label, train_sets)
ck_words = load(train_label, check_sets)
te_words = load(test_label, test_sets)
tr, ck, te = rows(tr_words), rows(ck_words), rows(te_words)
Xtr = np.array([r[3] for r in tr]); ytr = np.array([r[4] for r in tr])
mu = Xtr[:, 1:].mean(0); sd = Xtr[:, 1:].std(0) + 1e-9


def norm(rws):
    X = np.array([r[3] for r in rws])
    X[:, 1:] = (X[:, 1:] - mu) / sd
    return X


wt = fit(norm(tr), ytr)
th = (0.98, 0.95, 0.90, 0.85, 0.80, 0.70, 0.60, 0.50, 0.40, 0.0)
print(f'pivots {PIVOTS if USE_PIVOT else "none"}; train rows {len(tr)} ({int(ytr.sum())} positive)')
report(f'TRAIN {train_sets}', tr_words, decode(tr, norm(tr), wt), th)
report(f'CHECK {check_sets}', ck_words, decode(ck, norm(ck), wt), th)
report(f'TEST {test_label} {test_sets}', te_words, decode(te, norm(te), wt), th)


def isotonic(xs, ys):
    """Pool-adjacent-violators: a non-decreasing step map from raw probability to observed rate."""
    order = sorted(range(len(xs)), key=lambda i: xs[i])
    blocks = []
    for i in order:
        blocks.append([xs[i], xs[i], ys[i], 1])
        while len(blocks) > 1 and blocks[-2][2] / blocks[-2][3] > blocks[-1][2] / blocks[-1][3]:
            b = blocks.pop()
            blocks[-1][1] = b[1]; blocks[-1][2] += b[2]; blocks[-1][3] += b[3]
    return [(lo, hi, s / n) for lo, hi, s, n in blocks]


def calibrated(table, p):
    for lo, hi, rate in table:
        if p <= hi:
            return rate
    return table[-1][2]


ck_picks = decode(ck, norm(ck), wt)
table = isotonic([r[0] for r in ck_picks], [r[3] for r in ck_picks])
te_picks = decode(te, norm(te), wt)
print('\nTEST reliability by band of the probability calibrated on the check sets (band: picks, stated, observed)')
for lo, hi in ((0.98, 1.01), (0.95, 0.98), (0.90, 0.95), (0.80, 0.90), (0.60, 0.80), (0, 0.60)):
    sel = [(calibrated(table, r[0]), r[3]) for r in te_picks if lo <= calibrated(table, r[0]) < hi]
    if sel:
        print(f'    {lo:.2f}-{min(hi, 1):.2f}: {len(sel):5d}  stated {100 * sum(s for s, _ in sel) / len(sel):6.2f}%  observed {100 * sum(y for _, y in sel) / len(sel):6.2f}%')
