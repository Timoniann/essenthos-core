"""Monotone verse alignment of each Ge'ez chapter against the Greek, by the learned word associations."""
import collections, pickle, sys
exec(open('derive_load.py', encoding='utf-8').read())


import unicodedata
GEEZ_LATIN = {}
for ch, lat in [('ሀ','h'),('ለ','l'),('ሐ','h'),('መ','m'),('ሠ','s'),('ረ','r'),('ሰ','s'),('ሸ','s'),('ቀ','k'),('ቐ','k'),
                ('በ','b'),('ቨ','b'),('ተ','t'),('ቸ','c'),('ኀ','h'),('ነ','n'),('ኘ','n'),('አ',''),('ከ','k'),('ኸ','k'),
                ('ወ',''),('ዐ',''),('ዘ','z'),('ዠ','z'),('የ',''),('ደ','d'),('ዸ','d'),('ጀ','g'),('ገ','g'),('ጘ','g'),
                ('ጠ','t'),('ጨ','c'),('ጰ','p'),('ጸ','s'),('ፀ','s'),('ፈ','p'),('ፐ','p'),('ቈ','k'),('ኰ','k'),('ጐ','g'),('ኈ','h')]:
    GEEZ_LATIN[(ord(ch) - 0x1200) >> 3] = lat
PRO = ["እስከ", "እም", "እን", "ወ", "ለ", "በ", "ዘ"]
def geez_skeleton(word):
    w = ''.join(c for c in word if 0x1200 <= ord(c) <= 0x135A)
    for p in PRO:
        if len(w) - len(p) >= 2 and w.startswith(p): w = w[len(p):]; break
    return ''.join(GEEZ_LATIN.get((ord(c) - 0x1200) >> 3, '') for c in w).replace('h', '')
GREEK_LATIN = {'β':'b','γ':'g','δ':'d','ζ':'z','θ':'t','κ':'k','λ':'l','μ':'m','ν':'n','ξ':'ks','π':'p','ρ':'r',
               'σ':'s','ς':'s','τ':'t','φ':'p','χ':'k','ψ':'ps'}
def greek_skeleton(surface):
    t = unicodedata.normalize('NFD', surface.lower())
    t = ''.join(c for c in t if not unicodedata.combining(c))
    return ''.join(GREEK_LATIN.get(c, '') for c in t)
def is_name(surface):
    return surface[:1].isupper()
def near(a, b):
    if len(a) < 2 or len(b) < 2: return 0.0
    if a == b: return 1.0
    if min(len(a), len(b)) >= 3 and abs(len(a) - len(b)) <= 1:
        # one edit apart
        if len(a) == len(b): return 0.7 if sum(x != y for x, y in zip(a, b)) == 1 else 0.0
        s, l = (a, b) if len(a) < len(b) else (b, a)
        return 0.7 if any(l[:k] + l[k+1:] == s for k in range(len(l))) else 0.0
    return 0.0

import json, os
PRM = json.load(open(os.environ.get('GEEZ_PARAMS', 'params.json'))) if os.path.exists(os.environ.get('GEEZ_PARAMS', 'params.json')) else {}
P2 = PRM.get('p2', 0.6); BONUS = PRM.get('bonus', 1.5); P3 = PRM.get('p3', 2 * P2); SKIP = PRM.get('skip', 0.3); Q = PRM.get('q', None)
EDGE = 6
BEADS = [(1, 1), (1, 2), (2, 1), (2, 2), (1, 3), (3, 1), (1, 0), (0, 1)]
P22 = PRM.get('p22', 2.0)
ABSENT = PRM.get('absent', 0.0)
PEN = {(1, 1): 0, (1, 2): P2, (2, 1): P2, (2, 2): P22, (1, 3): P3, (3, 1): P3, (1, 0): SKIP, (0, 1): SKIP}

def greek_of(book):
    return 'RP2018' if 40 <= book <= 66 else 'GRCBRENT'

def chapters():
    g = collections.defaultdict(list)
    for v in order_in_text['GEEZ81']:
        if v.canon is None or v.book in OWN_DIVISION: continue
        g[(v.canon[0], v.canon[1])].append(v)
    return g

greek_by_chapter = {}
for s in ('GRCBRENT', 'RP2018'):
    d = collections.defaultdict(list)
    for v in order_in_text[s]:
        if v.canon: d[(v.canon[0], v.canon[1])].append(v)
    for k in d: d[k].sort(key=lambda v: (v.canon, v.number, v.label))
    greek_by_chapter[s] = d

EDGE_PEN = PRM.get('edge', 3.0)
def align(gv, kv):
    n, m = len(gv), len(kv)
    chapter = collections.Counter(v.canon[1] for v in gv).most_common(1)[0][0]
    gw = [[k for _, k, _, _ in words_of[v.id]] for v in gv]
    kw = [[k for _, k, _, _ in words_of[v.id]] for v in kv]
    kset = [collections.defaultdict(list) for _ in kv]
    for j, ws in enumerate(kw):
        for x, k in enumerate(ws): kset[j][k].append(x)
    gsk = [[geez_skeleton(s) for _, _, _, s in words_of[v.id]] for v in gv]
    names = [[(x, greek_skeleton(s)) for x, (_, k, _, s) in enumerate(words_of[v.id]) if is_name(s) or k in PROPER] for v in kv]
    # gb[i][j] = per Ge'ez word best; kb[j][i] = per Greek word best
    gb = [[None] * m for _ in range(n)]
    kb = [[None] * n for _ in range(m)]
    for i in range(n):
        cands = [assoc.get(w, {}) for w in gw[i]]
        for j in range(m):
            gvec = [0.0] * len(gw[i]); kvec = [0.0] * len(kw[j])
            ks = kset[j]
            for a, cand in enumerate(cands):
                if not cand: continue
                for k, d in cand.items():
                    xs = ks.get(k)
                    if xs:
                        if d > gvec[a]: gvec[a] = d
                        for x in xs:
                            if d > kvec[x]: kvec[x] = d
            for a, sk in enumerate(gsk[i]):
                if len(sk) < 2: continue
                for x, ksk in names[j]:
                    d = near(sk, ksk)
                    if d:
                        if d > gvec[a]: gvec[a] = d
                        if d > kvec[x]: kvec[x] = d
            gb[i][j] = gvec; kb[j][i] = kvec
    def bgvec(rows, size):
        # expected best per word over `size` consecutive verses of the other side, taken anywhere in the window
        if not rows or not rows[0]: return []
        L = len(rows)
        cols = [[] for _ in rows[0]]
        for s in range(0, L - size + 1):
            for x, t in enumerate(zip(*rows[s:s + size])): cols[x].append(max(t))
        if not cols or not cols[0]: return [0.0] * len(rows[0])
        if Q is None: return [sum(c) / len(c) for c in cols]
        return [sorted(c)[min(len(c) - 1, int(Q * len(c)))] for c in cols]
    bg_g = [{b: bgvec(gb[i], b) for b in (1, 2, 3)} for i in range(n)]
    bg_k = [{a: bgvec(kb[j], a) for a in (1, 2, 3)} for j in range(m)]
    def score(i0, i1, j0, j1):  # Ge'ez i0..i1-1 against Greek j0..j1-1
        if i0 == i1 or j0 == j1: return 0.0
        s = 0.0
        for i in range(i0, i1):
            vecs = [gb[i][j] for j in range(j0, j1)]
            if vecs[0]:
                bg = bg_g[i][j1 - j0]
                s += sum(max(t) - bg[x] for x, t in enumerate(zip(*vecs)))
        for j in range(j0, j1):
            vecs = [kb[j][i] for i in range(i0, i1)]
            if vecs[0]:
                bg = bg_k[j][i1 - i0]
                s += sum(max(t) - bg[x] for x, t in enumerate(zip(*vecs)))
        return s
    # edges: Greek verses outside the chapter at the start/end may be skipped freely
    NEG = -1e9
    present = {v.canon for v in kv}
    best = [[NEG] * (m + 1) for _ in range(n + 1)]
    back = [[None] * (m + 1) for _ in range(n + 1)]
    best[0][0] = 0
    for i in range(n + 1):
        for j in range(m + 1):
            if best[i][j] == NEG: continue
            for a, b in BEADS:
                ni, nj = i + a, j + b
                if ni > n or nj > m: continue
                if (a, b) == (1, 0):
                    val = best[i][j] - SKIP + (ABSENT if gv[i].canon not in present else 0.0)
                elif (a, b) == (0, 1):
                    edge = i == 0 or i == n
                    val = best[i][j] - (0 if edge else SKIP)
                else:
                    val = best[i][j] + score(i, ni, j, nj) - PEN[(a, b)]
                    if (a, b) == (1, 1) and gv[i].canon == kv[j].canon: val += BONUS
                    val -= EDGE_PEN * sum(1 for x in range(j, nj) if kv[x].canon[1] != chapter)
                if val > best[ni][nj] + 1e-12:
                    best[ni][nj] = val; back[ni][nj] = (i, j)
    beads = []
    i, j = n, m
    while (i, j) != (0, 0):
        pi, pj = back[i][j]
        beads.append((list(range(pi, i)), list(range(pj, j))))
        i, j = pi, pj
    beads.reverse()
    dp_total = sum(score(g[0], g[-1] + 1, k[0], k[-1] + 1) - PEN[(len(g), len(k))]
                   for g, k in beads if g and k and (len(g), len(k)) in PEN) - SKIP * sum(1 for g, k in beads if g and not k)
    at = {kv[j].canon: j for j in range(m)}
    ident = sum(score(i, i + 1, at[gv[i].canon], at[gv[i].canon] + 1) for i in range(n) if gv[i].canon in at)
    align.gain = dp_total - ident
    align.score = score
    return beads, gb, kb

def window(book, ch, greek):
    d = greek_by_chapter[greek]
    before = d.get((book, ch - 1), [])[-EDGE:]
    after = d.get((book, ch + 1), [])[:EDGE]
    return before + d.get((book, ch), []) + after

if __name__ == '__main__':
    want = sys.argv[2] if len(sys.argv) > 2 else '13:7'
    b, c = map(int, want.split(':'))
    gv = sorted(chapters()[(b, c)], key=lambda v: (v.chapter, v.number, v.label))
    kv = window(b, c, greek_of(b))
    beads, gb, kb = align(gv, kv)
    for gs, ks in beads:
        print([f'{gv[i].chapter}:{gv[i].number}{gv[i].label}' for i in gs], '->',
              [f'{kv[j].canon[1]}:{kv[j].canon[2]}' for j in ks])
