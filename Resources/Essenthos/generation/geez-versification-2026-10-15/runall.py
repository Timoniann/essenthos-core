import sys, collections, pickle, time
LABEL = sys.argv[1]; sys.argv = ['x', '0', '13:7', '0']
exec(open('dp.py', encoding='utf-8').read().replace("if __name__ == '__main__':", "if False:"))
THRESH = 0.3
def smooth(gb, i, ks, m, only=None):
    tot = inb = 0.0; cnt = 0
    for x in range(len(gb[i][0])):
        js = sorted(only) if only else range(m)
        col = {j: gb[i][j][x] for j in js}
        best = max(col.values())
        if best < THRESH: continue
        tied = [j for j in js if col[j] >= best - 1e-9]
        tot += best; cnt += 1
        inb += best * sum(1 for j in tied if j in ks) / len(tied)
    return (inb / tot if tot else None, cnt)
def m_ok(gb, i): return bool(gb[i]) and gb[i][0] is not None
out = []
t0 = time.time()
chs = chapters()
for (b, c) in sorted(chs):
    gv = sorted(chs[(b, c)], key=lambda v: (v.chapter, v.number, v.label))
    kv = window(b, c, greek_of(b))
    if not kv:
        for v in gv: out.append((v, None, None, 'no-greek'))
        continue
    beads, gb, kb = align(gv, kv)
    gain = align.gain
    for gs, ks in beads:
        targets = [kv[j] for j in ks]  # reordered per verse below
        for i in gs:
            # share: of this verse's words matched above THRESH anywhere in the window,
            # how many have their best match in the bead's Greek verses
            words = len(gb[i][0]) if m_ok(gb, i) else 0
            matched = inbead = 0
            for x in range(words):
                col = [gb[i][j][x] for j in range(len(kv))]
                order = sorted(range(len(kv)), key=lambda j: -col[j])
                best = col[order[0]]
                second = col[order[1]] if len(order) > 1 else 0.0
                if best < THRESH or best - second < 0.15: continue
                matched += 1
                if order[0] in ks: inbead += 1
            share = inbead / matched if matched else None
            kind = 'none' if not ks else ('equals' if len(gs) == 1 and len(ks) == 1 else
                                          'part-of' if len(gs) > 1 and len(ks) == 1 else
                                          'spans' if len(gs) == 1 else 'mixed')
            at = {kv[j].canon: j for j in range(len(kv))}
            oj = at.get(gv[i].canon)
            sc = align.score
            st = sc(i, i + 1, ks[0], ks[-1] + 1) if ks else 0.0
            so = sc(i, i + 1, oj, oj + 1) if oj is not None else 0.0
            pair_share = smooth(gb, i, ks, len(kv), only=set(ks) | ({oj} if oj is not None else set()))
            out.append((v_ := gv[i], [kv[j].canon for j in sorted(ks, key=lambda j: (-sum(gb[i][j]), j))], share, kind, len(gs), matched, gain, pair_share, st - so))
    if not gs: pass
print('time', round(time.time() - t0, 1), file=sys.stderr)
pickle.dump(out, open(f'beads-{LABEL}.pkl', 'wb'))
