"""Derive the Ge'ez verse map: which Greek verse(s) each Ge'ez verse answers.

Reads verses.csv, words.csv, links.csv (dumped read-only from the live corpus) and writes
map.tsv (every Ge'ez verse with its bead) and stats to stdout.
"""
import csv, collections, math, sys, unicodedata, pickle, os

OWN_DIVISION = {17, 18, 19, 22, 75, 76, 91, 92}
FIRST, LAST = 0x1200, 0x135A
PROCLITICS = ["እስከ", "እም", "እን", "ወ", "ለ", "በ", "ዘ"]
SUFFIXES = ["ሆሙ", "ሆን", "ክሙ", "ክን", "ሁ", "ሃ", "ሙ", "ከ", "ኪ", "ነ", "ኒ", "ዮ"]
def row(c): return (ord(c) - FIRST) >> 3
def order(c): return (ord(c) - FIRST) & 7
HOMO = {row('ሐ'): row('ሀ'), row('ኀ'): row('ሀ'), row('ሠ'): row('ሰ'), row('ዐ'): row('አ'), row('ፀ'): row('ጸ')}
LABIO = {row('ቈ'): row('ቀ'), row('ቘ'): row('ቐ'), row('ኈ'): row('ኀ'), row('ኰ'): row('ከ'), row('ዀ'): row('ኸ'), row('ጐ'): row('ገ')}
def syl(c): return FIRST <= ord(c) <= LAST
def merged(r):
    r = LABIO.get(r, r)
    return HOMO.get(r, r)
def stem(word):
    bare = ''.join(c for c in word if syl(c))
    if not bare: return word.lower()
    w = bare
    stripped = True
    while stripped:
        stripped = False
        for p in PROCLITICS:
            if len(w) - len(p) >= 2 and w.startswith(p):
                w = w[len(p):]; stripped = True; break
    for s in SUFFIXES:
        if len(w) - len(s) >= 2 and w.endswith(s):
            w = w[:-len(s)]; break
    else:
        if len(w) - 1 >= 2 and w.endswith('የ') and order(w[-2]) == 5:
            w = w[:-1]
    return ''.join(chr(FIRST + (merged(row(c)) << 3)) if syl(c) else c for c in w)

def greek_key(surface, lemma):
    t = lemma if lemma else surface
    t = unicodedata.normalize('NFD', t.lower())
    return ''.join(c for c in t if not unicodedata.combining(c) and c.isalpha())

# ---- verses
Verse = collections.namedtuple('Verse', 'slug book chapter number label id canon words')
verses = {}
order_in_text = collections.defaultdict(list)
for r in csv.reader(open('verses.csv', encoding='utf-8')):
    if len(r) < 10 or r[0] == 'SET': continue
    slug, book, ch, num, label, vid, cb, cc, cv, wc = r
    v = Verse(slug, int(book), int(ch), int(num), label, int(vid), (int(cb), int(cc), int(cv)) if cb else None, int(wc))
    verses[v.id] = v
    order_in_text[slug].append(v)

# ---- words
words_of = collections.defaultdict(list)  # verse id -> [(position, key, word id, surface)]
word_verse = {}
TEXTS = {'GEEZ81', 'GRCBRENT', 'NESTLE1904', 'RP2018'}
PROPER = set()
for r in csv.reader(open('words.csv', encoding='utf-8')):
    if len(r) < 7 or r[0] not in TEXTS: continue
    slug, wid, vid, pos, surface, lemma, strong = r
    if slug == 'NESTLE1904' and lemma[:1].isupper() and strong: PROPER.add(strong)
    key = stem(surface) if slug == 'GEEZ81' else (strong or greek_key(surface, '')) if slug == 'RP2018' else greek_key(surface, lemma)
    if not key: continue
    words_of[int(vid)].append((int(pos), key, int(wid), surface))
    word_verse[int(wid)] = int(vid)
for k in words_of: words_of[k].sort()

# ---- training: aligner links in chapters where Ge'ez and the Greek print the same verses
def chapter_sets(slug):
    d = collections.defaultdict(set)
    for v in order_in_text[slug]:
        d[(v.book, v.chapter)].add((v.number, v.label))
    return d
gch = chapter_sets('GEEZ81')
agree = {}
for other in ('GRCBRENT', 'RP2018'):
    och = chapter_sets(other)
    agree[other] = {k for k in gch if k in och and len(gch[k]) == len(och[k]) and k[0] not in OWN_DIVISION}

wkey = {}
for vid, ws in words_of.items():
    for pos, key, wid, s_ in ws: wkey[wid] = key
links_by = collections.defaultdict(list)
for r in csv.reader(open('links.csv', encoding='utf-8')):
    if len(r) < 5 or r[0] not in ('GRCBRENT', 'RP2018'): continue
    fw, tw = int(r[2]), int(r[3])
    if fw in word_verse and tw in word_verse: links_by[r[0]].append((fw, tw))
by_addr = {s_: collections.defaultdict(list) for s_ in TEXTS}
for s_ in TEXTS:
    for v in order_in_text[s_]:
        if v.canon: by_addr[s_][v.canon].append(v.id)
assoc = collections.defaultdict(dict)
for other in ('GRCBRENT', 'RP2018'):
    pair = collections.Counter(); gcount = collections.Counter(); kcount = collections.Counter()
    for fw, tw in links_by[other]:
        gv = verses[word_verse[fw]]
        if (gv.book, gv.chapter) in agree[other]: pair[(wkey[fw], wkey[tw])] += 1
    for v in order_in_text['GEEZ81']:
        if (v.book, v.chapter) in agree[other]:
            for _, key, _, _ in words_of[v.id]: gcount[key] += 1
    for v in order_in_text[other]:
        if (v.book, v.chapter) in agree[other]:
            for _, key, _, _ in words_of[v.id]: kcount[key] += 1
    for (g, k), c in pair.items():
        if c < 2: continue
        d = 2 * c / (gcount[g] + kcount[k])
        if d >= 0.05: assoc[g][k] = max(d, assoc[g].get(k, 0))
    co = collections.Counter(); gdf = collections.Counter(); kdf = collections.Counter()
    for addr, gids in by_addr['GEEZ81'].items():
        gv = verses[gids[0]]
        if (gv.book, gv.chapter) not in agree[other] or addr not in by_addr[other]: continue
        G = {k for i in gids for _, k, _, _ in words_of[i]}
        K = {k for i in by_addr[other][addr] for _, k, _, _ in words_of[i]}
        for g in G: gdf[g] += 1
        for k in K: kdf[k] += 1
        for g in G:
            for k in K: co[(g, k)] += 1
    for (g, k), c in co.items():
        if c < 3: continue
        d = 2 * c / (gdf[g] + kdf[k])
        if d >= 0.15 and d > assoc[g].get(k, 0): assoc[g][k] = d
    print(other, 'pairs so far', sum(len(x) for x in assoc.values()), file=sys.stderr)
pickle.dump({'verses': {k: tuple(v) for k, v in verses.items()},
             'order': {s: [v.id for v in vs] for s, vs in order_in_text.items()},
             'words': dict(words_of), 'assoc': dict(assoc), 'agree': agree, 'proper': PROPER}, open('state.pkl', 'wb'))
