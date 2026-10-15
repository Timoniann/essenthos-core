"""Fit EVIDENTIA's combiner on an export of the Forge's own edges (--combiner-export) and write its weights file.

Fitted on the training sets, calibrated (isotonic) on the calibration sets, both read from one benchmark label's
<passage>.edges.json; nothing here reads the database or a held-out set. A model per original language (Hebrew,
Greek) is kept only if it beats the shared one on the calibration sets.

usage: fit_combiner.py <label> <train sets> <calibration sets> <out.json> [--version V] [--split auto|yes|no]
                       [--kept apart|together] [--drop feature,feature] [--min-block N]

--kept apart calibrates the tiers' own placements the model keeps on their own picks, and its new edges on theirs.
--min-block merges calibration steps until each holds N picks.
--drop holds features at zero, so a fit without them can be compared on the same export.
"""
import collections
import glob
import json
import math
import os
import re
import subprocess
import sys

import numpy as np

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.join(os.environ.get('TEMP', r'C:\Users\timon\AppData\Local\Temp'), 'essenthos-evidentia')
BANDS = {'safe': 0.98, 'review': 0.95, 'withdraw': 0.60, 'new': 0.98}
L2 = 1.0
KEPT_APART = True
MIN_BLOCK = 1


def load(label, sets):
    names, rows = None, []
    for s in sets:
        for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.edges.json'))):
            data = json.load(open(f, encoding='utf-8'))
            if names is None:
                names = data['Features']
            elif names != data['Features']:
                raise SystemExit(f'{f} has other features than the passages before it')
            passage = os.path.basename(f).split('.')[0]
            for r in data['Rows']:
                r['Passage'] = passage
                rows.append(r)
    if names is None:
        raise SystemExit(f'no edges under {label} for {sets}')
    return names, rows


def matrix(rows):
    return np.array([r['Features'] for r in rows], dtype=float)


def fit(X, y):
    """L2-penalised logistic regression by Newton's method; the intercept is not penalised."""
    mean = X.mean(0)
    scale = X.std(0)
    scale[scale < 1e-9] = 1.0
    Z = np.hstack([np.ones((len(X), 1)), (X - mean) / scale])
    w = np.zeros(Z.shape[1])
    penalty = np.full(Z.shape[1], L2)
    penalty[0] = 0.0
    for _ in range(100):
        p = 1 / (1 + np.exp(-(Z @ w)))
        g = Z.T @ (p - y) + penalty * w
        H = (Z * (p * (1 - p))[:, None]).T @ Z + np.diag(penalty + 1e-9)
        step = np.linalg.solve(H, g)
        w -= step
        if np.abs(step).max() < 1e-10:
            break
    return {'intercept': float(w[0]), 'weights': [float(v) for v in w[1:]], 'mean': [float(v) for v in mean],
            'scale': [float(v) for v in scale]}


def raw(model, features):
    total = model['intercept']
    for weight, value, mean, scale in zip(model['weights'], features, model['mean'], model['scale']):
        total += weight * (value - mean) / scale
    return 1 / (1 + math.exp(-total))


def calibrated(model, p, current=False):
    table = model.get('calibrationKept') if current and model.get('calibrationKept') else model['calibration']
    for hi, rate in table:
        if p <= hi:
            return rate
    return table[-1][1]


def isotonic(xs, ys):
    """Pool-adjacent-violators over the picks: steps of (highest raw probability in the step, observed rate)."""
    blocks = []
    for x, y in sorted(zip(xs, ys)):
        blocks.append([x, y, 1])
        while len(blocks) > 1 and blocks[-2][1] / blocks[-2][2] > blocks[-1][1] / blocks[-1][2]:
            hi, s, n = blocks.pop()
            blocks[-1][0] = hi
            blocks[-1][1] += s
            blocks[-1][2] += n
    # A step of a few picks states a rate the next passage will not see; merge each with a neighbour until it holds
    # MIN_BLOCK picks. Merging neighbours of a non-decreasing sequence keeps it non-decreasing.
    while len(blocks) > 1 and min(b[2] for b in blocks) < MIN_BLOCK:
        i = min(range(len(blocks)), key=lambda k: blocks[k][2])
        j = i - 1 if i == len(blocks) - 1 or (i > 0 and blocks[i - 1][2] <= blocks[i + 1][2]) else i + 1
        lo, hi = min(i, j), max(i, j)
        blocks[lo] = [blocks[hi][0], blocks[lo][1] + blocks[hi][1], blocks[lo][2] + blocks[hi][2]]
        del blocks[hi]
    steps = [[hi, s / n] for hi, s, n in blocks]
    steps[-1][0] = 1.0
    return steps


def chapters(rows):
    by = collections.defaultdict(list)
    for r in rows:
        by[(r['Passage'], r['Book'], r['Chapter'])].append(r)
    return by.values()


def greedy(rows, prob, accept=None):
    """One edge per word and per word of the original, by raw probability, the tiers' own edge first on a tie."""
    picks = []
    for chapter in chapters(rows):
        words, targets = set(), set()
        ordered = sorted(chapter, key=lambda r: (-prob(r)[0], not r['Current'], r['Verse'], r['SourceWordId'], r['TargetWordId']))
        for r in ordered:
            if accept is not None and not accept(r):
                continue
            if r['SourceWordId'] in words or r['TargetWordId'] in targets:
                continue
            words.add(r['SourceWordId'])
            targets.add(r['TargetWordId'])
            picks.append(r)
    return picks


def models_for(names, train, split):
    groups = sorted({r['Group'] for r in train})
    if not split:
        X, y = matrix(train), np.array([r['Label'] for r in train], dtype=float)
        return {'all': fit(X, y)}
    out = {}
    for g in groups:
        part = [r for r in train if r['Group'] == g]
        out[g] = fit(matrix(part), np.array([r['Label'] for r in part], dtype=float))
    return out


def model_of(models, r):
    return models.get(r['Group']) or models['all']


def calibrate(models, check):
    probs = lambda r: (raw(model_of(models, r), r['Features']),)
    picks = greedy(check, probs)
    for key, model in models.items():
        mine = [r for r in picks if key == 'all' or r['Group'] == key]
        if KEPT_APART:
            new = [r for r in mine if not r['Current']]
            # Every placement of the tiers, not only those the greedy picks keep: where the model prefers another edge
            # that does not reach the band, the tiers' own stands, and that is the placement whose rate is stated.
            kept = [r for r in check if r['Current'] and (key == 'all' or r['Group'] == key)]
            model['calibration'] = isotonic([probs(r)[0] for r in new], [1.0 if r['Label'] else 0.0 for r in new])
            model['calibrationKept'] = isotonic([probs(r)[0] for r in kept], [1.0 if r['Label'] else 0.0 for r in kept])
        else:
            model['calibration'] = isotonic([probs(r)[0] for r in mine], [1.0 if r['Label'] else 0.0 for r in mine])


def log_loss(models, rows):
    total = 0.0
    for r in rows:
        p = min(max(raw(model_of(models, r), r['Features']), 1e-12), 1 - 1e-12)
        total -= math.log(p) if r['Label'] else math.log(1 - p)
    return total / max(1, len(rows))


def decide(models, rows):
    """The rule the Forge applies: a new edge at the review band or better, the tiers' own at the withdrawal line."""
    def prob(r):
        p = raw(model_of(models, r), r['Features'])
        return p, calibrated(model_of(models, r), p, r['Current'])

    def accept(r):
        c = prob(r)[1]
        return c >= BANDS['withdraw'] if r['Current'] else c >= BANDS.get('new', BANDS['review'])

    return [(r, prob(r)[1]) for r in greedy(rows, prob, accept)]


def report(name, models, rows):
    covered = [r for r in rows if r['Covered']]
    words = {r['SourceWordId'] for r in covered}
    base = [r for r in covered if r['Current']]
    base_right = sum(1 for r in base if r['Label'])
    picks = [(r, c) for r, c in decide(models, rows) if r['Covered']]
    right = sum(1 for r, _ in picks if r['Label'])
    print(f'{name}: {len(words)} covered words weighed; the tiers place {len(base)}, right {base_right} '
          f'({100 * base_right / max(1, len(base)):.2f}%); the combiner places {len(picks)}, right {right} '
          f'({100 * right / max(1, len(picks)):.2f}%), {right - base_right:+d} right')
    for lo, hi in ((BANDS['safe'], 1.01), (BANDS['review'], BANDS['safe']), (BANDS['withdraw'], BANDS['review'])):
        sel = [(r, c) for r, c in picks if lo <= c < hi]
        if sel:
            print(f'    {lo:.2f}-{min(hi, 1):.2f}: {len(sel):5d} picks, stated {100 * sum(c for _, c in sel) / len(sel):6.2f}%, '
                  f'observed {100 * sum(1 for r, _ in sel if r["Label"]) / len(sel):6.2f}%')
    return right - base_right


def main():
    args, opts, rest = [], {}, sys.argv[1:]
    while rest:
        if rest[0].startswith('--'):
            opts[rest[0]] = rest[1]
            rest = rest[2:]
        else:
            args.append(rest.pop(0))
    label, train_sets, check_sets, out = args[0], args[1].split(','), args[2].split(','), args[3]
    version = opts.get('--version', 'unversioned')
    global KEPT_APART
    KEPT_APART = opts.get('--kept', 'apart') == 'apart'
    global MIN_BLOCK
    MIN_BLOCK = int(opts.get('--min-block', '1'))
    split_mode = opts.get('--split', 'auto')
    names, train = load(label, train_sets)
    _, check = load(label, check_sets)
    dropped = [n for n in opts.get('--drop', '').split(',') if n]
    for n in dropped:
        if n not in names:
            raise SystemExit(f'no feature {n}')
        j = names.index(n)
        for r in train + check:
            r['Features'] = r['Features'][:j] + [0.0] + r['Features'][j + 1:]
    if dropped:
        print(f'dropped (held at zero): {", ".join(dropped)}')
    fit_rows = [r for r in train if r['Covered']]
    check_scored = [r for r in check if r['Covered']]
    print(f'{len(names)} features; train {len(fit_rows)} edges ({sum(r["Label"] for r in fit_rows)} positive) of {len(train)}; '
          f'calibration {len(check)} edges')

    candidates = {}
    for split in (False, True):
        models = models_for(names, fit_rows, split)
        calibrate(models, check)
        name = 'apart' if split else 'together'
        loss = log_loss(models, check_scored)
        print(f'\n== Hebrew and Greek {name}: calibration-set log loss {loss:.5f}')
        report(f'  train {train_sets}', models, train)
        gain = report(f'  calibration {check_sets}', models, check)
        candidates[split] = (loss, gain, models)

    if split_mode == 'auto':
        split = candidates[True][0] < candidates[False][0] and candidates[True][1] >= candidates[False][1]
    else:
        split = split_mode == 'yes'
    models = candidates[split][2]
    print(f'\nkept: Hebrew and Greek {"apart" if split else "together"}')

    commit = subprocess.run(['git', 'rev-parse', '--short=12', 'HEAD'], capture_output=True, text=True,
                            cwd=os.path.dirname(os.path.abspath(__file__))).stdout.strip()
    if subprocess.run(['git', 'status', '--porcelain', '--untracked-files=no'], capture_output=True, text=True,
                      cwd=os.path.dirname(os.path.abspath(__file__))).stdout.strip():
        commit += '-dirty'
    pivots = [n[len('pivot_'):] for n in names if re.fullmatch(r'pivot_[A-Z0-9]+', n)]
    ruth = [r for r in check + train if r['Passage'] == 'ind-ruth' and r['Chapter'] == 1][:40]
    checks = []
    for r in ruth:
        model = model_of(models, r)
        p = raw(model, r['Features'])
        checks.append({'group': r['Group'], 'features': r['Features'], 'raw': p, 'calibrated': calibrated(model, p, r['Current']),
                       'where': f"{r['Passage']} {r['Book']}:{r['Chapter']}:{r['Verse']} {r['SourceWordId']}->{r['TargetWordId']}",
                       'current': bool(r['Current'])})
    document = {
        'version': version,
        'fittedOn': {
            'label': label, 'train': train_sets, 'calibration': check_sets,
            'trainEdges': len(fit_rows), 'trainPositive': int(sum(r['Label'] for r in fit_rows)),
            'calibrationEdges': len(check), 'l2': L2, 'split': split,
        },
        'commit': commit,
        'pivots': pivots,
        'features': names,
        'bands': BANDS,
        'models': models,
        'checks': checks,
    }
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(document, f, indent=1)
        f.write('\n')
    print(f'wrote {out}')


if __name__ == '__main__':
    main()
