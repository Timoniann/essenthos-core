import sys, collections
import os
P2, BONUS = 'cfg', 'cfg'
sys.argv = ['x', '0', '13:7', '0']
exec(open('dp.py', encoding='utf-8').read().replace("if __name__ == '__main__':", "if False:"))
truth = {}
for line in open('GeezVerseMap.tsv', encoding='utf-8'):
    if line.startswith('#') or not line.strip(): continue
    f = line.rstrip('\n').split('\t')
    gb_, gvs, tb, tvs = int(f[0]), f[1], int(f[2]), f[3]
    def expand(s):
        out = []
        for part in s.split(';'):
            part = part.strip(); c, r = part.split(':'); r = r.split('-')
            out += [(int(c), v) for v in range(int(r[0]), int(r[-1]) + 1)]
        return out
    tos = [] if tvs == '-' else [(tb, c, v) for c, v in expand(tvs)]
    for c, v in expand(gvs): truth[(gb_, c, v)] = set(tos)
g = collections.defaultdict(list)
for v in order_in_text['GEEZ81']:
    if v.book in (17, 18, 22, 75, 76) and v.canon: g[(v.canon[0], v.canon[1])].append(v)
exact = overlap = total = 0
per = collections.Counter()
for (b, c) in sorted(g):
    gv = sorted(g[(b, c)], key=lambda v: (v.chapter, v.number, v.label))
    kv = window(b, c, 'GRCBRENT')
    if not kv: continue
    beads, GB, KB = align(gv, kv)
    for gs, ks in beads:
        got = {kv[j].canon for j in ks}
        for i in gs:
            key = (gv[i].book, gv[i].chapter, gv[i].number)
            if key not in truth: continue
            t = truth[key]; total += 1; per[b, 'n'] += 1
            if got == t: exact += 1; per[b, 'exact'] += 1
            if got & t or (not got and not t): overlap += 1; per[b, 'overlap'] += 1
            if ks:
                pj = max(ks, key=lambda j: sum(GB[i][j]))
                if kv[pj].canon in t: per['primary'] += 1
            elif not t: per['primary'] += 1
print('P2', P2, 'BONUS', BONUS, 'total', total, 'exact', round(exact/total, 3), 'overlap', round(overlap/total, 3), 'primary', round(per['primary']/total, 3))
for b in (17, 18, 22, 75, 76):
    if per[b,'n']: print(' ', b, per[b,'n'], round(per[b,'exact']/per[b,'n'],3), round(per[b,'overlap']/per[b,'n'],3))
