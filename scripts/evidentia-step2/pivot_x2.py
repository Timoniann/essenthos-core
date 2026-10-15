"""X2 offline: how the same-language pivot reads against EVIDENTIA's placements and on the words it leaves unplaced.
Reads the words.json files of one label (recorded with --pivot and no policy) for the named sets only."""
import json, glob, os, sys, collections, itertools
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
label, sets = sys.argv[1], sys.argv[2].split(',')
texts_filter = sys.argv[3].split(',') if len(sys.argv) > 3 else None
LEX = {'KnownRendering', 'DictionarySense', 'TargetGloss', 'MatchingNormalisedForm', 'SharedStrongNumber'}


def words_of(s):
    out = []
    for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.words.json'))):
        out += json.load(open(f, encoding='utf-8'))
    return out


def pct(r, n):
    return f'{r}/{n} = {100 * r / n:.1f}%' if n else '0/0'


def is_prefix(strong):
    return strong is not None and strong.startswith('H90') and len(strong) == 5


for s in sets:
    ws = words_of(s)
    if texts_filter:
        for w in ws:
            if w.get('Pivot'):
                w['Pivot'] = {k: v for k, v in w['Pivot'].items() if k in texts_filter}
    taken = {w['TargetWordId'] for w in ws if w['TargetWordId']}
    # target id -> strong, from gold and candidates traces
    strong = {}
    for w in ws:
        for g in w.get('Gold') or []:
            strong[g['TargetWordId']] = g['StrongNumber']
        for c in w.get('Candidates') or []:
            strong.setdefault(c['TargetWordId'], c['TargetStrongNumber'])
        if w['TargetWordId']:
            strong.setdefault(w['TargetWordId'], w['TargetStrongNumber'])
    n = len(ws)
    print(f'\n===== {s}: {n} words, label {label}, pivots {texts_filter or "all"}')
    stats = collections.defaultdict(lambda: [0, 0])

    def add(k, ok):
        stats[k][0] += 1
        stats[k][1] += bool(ok)

    reach = collections.Counter()
    for w in ws:
        piv = w.get('Pivot') or {}
        for t in piv:
            reach[t] += 1
        key = set(w.get('SplitKey') or [])
        if not key and not w['GoldCoversSourceWord']:
            continue
        named = set(itertools.chain.from_iterable(piv.values()))
        # placed linked words (lexical tiers only, the ones the pivot may withhold)
        if w['TargetWordId'] and w.get('State') == 'linked' and w.get('Rule') == 'lexical' and piv:
            right = w.get('CorrectOnSplitKey')
            judged = w.get('CorrectOnJudgedKey')
            nagree = sum(1 for v in piv.values() if w['TargetWordId'] in v)
            if nagree:
                add('placed, a pivot agrees (split key)', right)
                add(f'placed, {min(nagree,3)} pivots agree', right)
                add('placed, a pivot agrees (judged)', judged)
            else:
                add('placed, every pivot contradicts (split key)', right)
                add('placed, every pivot contradicts (judged)', judged)
                # is the pivot's word the same lexeme (occurrence) ?
                alt = [t for t in named if strong.get(t) and strong.get(t) == w['TargetStrongNumber']]
                if alt:
                    add('  contradicted, pivot names another occurrence of the same lexeme: placement right', right)
                    free = [t for t in alt if t not in taken]
                    if len(free) == 1:
                        add('  ... and that occurrence is free and single: moving it would be right', free[0] in key)
                else:
                    add('  contradicted, pivot names another lexeme: placement right', right)
                    add('  contradicted, another lexeme: placement right (judged)', judged)
                for t, v in piv.items():
                    add(f'  contradicted by {t}: placement right', right)
        # unplaced content words
        if not w['TargetWordId'] and w.get('State') == 'unresolved' and not w.get('Grammatical') and piv:
            cands = {c['TargetWordId']: c for c in (w.get('Candidates') or [])}
            for t, v in piv.items():
                vv = [x for x in v if not is_prefix(strong.get(x))]
                if len(vv) == 1:
                    add(f'unplaced content, {t} alone (single non-prefix word)', vv[0] in key)
            options = {}
            for t, v in piv.items():
                for x in v:
                    if is_prefix(strong.get(x)) or x in taken:
                        continue
                    options.setdefault(x, set()).add(t)
            for x, by in options.items():
                lex = x in cands and bool(set(cands[x]['Evidence']) & LEX)
                ok = x in key
                add('free option: any', ok)
                if len(by) >= 2:
                    add('free option: two texts', ok)
                if lex:
                    add('free option: one text + lexical candidate', ok)
                if len(by) >= 2 or lex:
                    add('free option: two texts or lexical', ok)
            # the rule as built: exactly one option with a second signal
            chosen2 = [x for x, by in options.items() if len(by) >= 2 or (x in cands and set(cands[x]['Evidence']) & LEX)]
            if len(chosen2) == 1:
                add('FILL (two texts or lexical, unique)', chosen2[0] in key)
            chosen_t = [x for x, by in options.items() if len(by) >= 2]
            if len(chosen_t) == 1:
                add('FILL (two texts, unique)', chosen_t[0] in key)
            chosen_l = [x for x, by in options.items() if x in cands and set(cands[x]['Evidence']) & LEX]
            if len(chosen_l) == 1:
                add('FILL (lexical, unique)', chosen_l[0] in key)
            chosen1 = list(options)
            if len(chosen1) == 1:
                add('FILL (any single free option)', chosen1[0] in key)
    print('pivot reach (words matched):', dict(reach))
    for k in sorted(stats):
        c, r = stats[k]
        print(f'  {k}: {pct(r, c)}')
