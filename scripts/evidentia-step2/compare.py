"""Word by word, what a variant changed against the baseline: placements added (and whether the split key and the
judged key count them right), placements taken away or moved, and the attached words that followed a new head."""
import json, glob, os, sys, collections
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
base, variant, sets = sys.argv[1], sys.argv[2], sys.argv[3].split(',')


def load(label, s):
    out = {}
    for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.words.json'))):
        for w in json.load(open(f, encoding='utf-8')):
            out[w['SourceWordId']] = w
    return out


def right(w):
    if w.get('State') in (None, 'unresolved'):
        return None
    if w.get('State') == 'supplied':
        return w.get('AbsenceCorrect')
    return w.get('CorrectOnSplitKey')


grand = collections.Counter()
for s in sets:
    b, v = load(base, s), load(variant, s)
    if not v:
        continue
    n = len(v)
    c = collections.Counter()
    newheads = set()
    for i, wv in v.items():
        wb = b.get(i)
        if wb is None:
            c['missing in base'] += 1
            continue
        tb, tv = wb['TargetWordId'], wv['TargetWordId']
        if tb == tv and wb.get('State') == wv.get('State'):
            continue
        if wv.get('ProposalKind') == 'SameLanguagePivot' and tb != tv:
            newheads.add(i)
    for i, wv in v.items():
        wb = b.get(i)
        if wb is None:
            continue
        tb, tv = wb['TargetWordId'], wv['TargetWordId']
        sb, sv = wb.get('State'), wv.get('State')
        if tb == tv and sb == sv:
            continue
        rb, rv = right(wb), right(wv)
        jb, jv = wb.get('CorrectOnJudgedKey'), wv.get('CorrectOnJudgedKey')
        kind = wv.get('ProposalKind')
        if sb in (None, 'unresolved') and sv not in (None, 'unresolved'):
            tag = 'filled by the pivot' if kind == 'SameLanguagePivot' else (
                'attached after a pivot head' if wv.get('HeadWordId') in newheads or (kind == 'AttachedWord') else f'newly {sv} ({kind})')
            c[f'{tag}: words'] += 1
            c[f'{tag}: right by the split key'] += bool(rv)
            c[f'{tag}: right by the judged key'] += bool(jv)
        elif sv in (None, 'unresolved') and sb not in (None, 'unresolved'):
            c[f'withdrawn ({sb}, {wb.get("ProposalKind")}): words'] += 1
            c[f'withdrawn ({sb}, {wb.get("ProposalKind")}): were right by the split key'] += bool(rb)
            c[f'withdrawn ({sb}, {wb.get("ProposalKind")}): were right by the judged key'] += bool(jb)
        else:
            c[f'changed {sb}->{sv} ({kind}): words'] += 1
            c[f'changed {sb}->{sv} ({kind}): right before'] += bool(rb)
            c[f'changed {sb}->{sv} ({kind}): right after'] += bool(rv)
            c[f'changed {sb}->{sv} ({kind}): right before (judged)'] += bool(jb)
            c[f'changed {sb}->{sv} ({kind}): right after (judged)'] += bool(jv)
    rb_total = sum(1 for w in b.values() if right(w))
    rv_total = sum(1 for w in v.values() if right(w))
    jb_total = sum(1 for w in b.values() if w.get('State') not in (None, 'unresolved') and (w.get('CorrectOnJudgedKey') if w.get('State') != 'supplied' else w.get('AbsenceCorrect')))
    jv_total = sum(1 for w in v.values() if w.get('State') not in (None, 'unresolved') and (w.get('CorrectOnJudgedKey') if w.get('State') != 'supplied' else w.get('AbsenceCorrect')))
    print(f'\n== {s}: {n} words')
    print(f'  explained and right (split key): {rb_total} -> {rv_total} ({100 * (rv_total - rb_total) / n:+.2f} pts)')
    print(f'  explained and right (judged key): {jb_total} -> {jv_total} ({100 * (jv_total - jb_total) / n:+.2f} pts)')
    for k in sorted(c):
        print(f'  {k}: {c[k]}')
    grand['n'] += n
    grand['rb'] += rb_total
    grand['rv'] += rv_total
    grand['jb'] += jb_total
    grand['jv'] += jv_total
    for k in c:
        grand[k] += c[k]
n = grand['n']
print(f'\n== all {sets}: {n} words')
print(f"  explained and right (split key): {grand['rb']} -> {grand['rv']} ({100 * (grand['rv'] - grand['rb']) / n:+.2f} pts)")
print(f"  explained and right (judged key): {grand['jb']} -> {grand['jv']} ({100 * (grand['jv'] - grand['jb']) / n:+.2f} pts)")
for k in sorted(grand):
    if k not in ('n', 'rb', 'rv', 'jb', 'jv'):
        print(f'  {k}: {grand[k]}')
