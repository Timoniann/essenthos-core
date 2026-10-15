"""X1: grammatical words by the parser's relation, simulated offline and scored on the split key.

For every grammatical word of a benchmark label whose parse head is placed, the relation names a landing site on the
original, relative to the head's rendering: first a free word of the grammatical word's own kind written beside that
rendering (a preposition, article, conjunction, the verb *be*, a pronoun agreeing in person and number), then the
rendering itself where the original writes the function in the head's own form (a verb's ending, a suffix, a
construct), else nothing. The pass mark is read on the words whose head is placed.

usage: x1.py <label> <sets> <original words .tsv> [--examples <group> <n>] [--wrong]

The original words come from a read-only export (orig.sql); nothing here reads the database.
"""
import collections
import json
import random
import sys

from common import PLACED, share, words

PREPOSITION_SKIP = {'art'}
HEBREW_BE, GREEK_BE = {'H1961'}, {'G1510', 'G1096'}
HEBREW_OBJECT_MARKER = 'H853'
SUBORDINATORS = {'H3588', 'H834', 'H518', 'H3651', 'H6440', 'H4616', 'H3282', 'H834a', 'H3588a',
                 'G3754', 'G2443', 'G1487', 'G5613', 'G3704', 'G1437', 'G3753', 'G1063', 'G2531', 'G5618', 'G3767', 'G3361'}
NEGATIONS = {'H3808', 'H408', 'H369', 'H1077', 'G3756', 'G3361', 'G3364', 'G3762', 'G3367'}
ENGLISH_PERSON = {'1': 1, '2': 2, '3': 3}
HEBREW_PERSON = {'p1': 1, 'p2': 2, 'p3': 3}
GREEK_PERSON = {'first': 1, 'second': 2, 'third': 3}
HEBREW_NUMBER = {'sg': 'S', 'pl': 'P', 'du': 'P'}
GREEK_NUMBER = {'singular': 'S', 'plural': 'P'}
ENGLISH_NUMBER = {'Sing': 'S', 'Plur': 'P'}
ENGLISH_PREPOSITIONS = {'under', 'before', 'behind', 'above', 'below', 'near', 'outside', 'inside', 'beyond', 'apart', 'without', 'until', 'between', 'among', 'around', 'across', 'opposite', 'after', 'over', 'against', 'beside', 'within', 'because'}
OTHER_DETERMINERS = {'this', 'that', 'these', 'those', 'all', 'every', 'each', 'no', 'any', 'some', 'another', 'other',
                     'such', 'both', 'either', 'neither', 'what', 'which', 'whatever', 'whichever', 'many', 'much',
                     'few', 'several', 'half', 'whose'}


class Original:
    def __init__(self, path):
        self.by_id, self.verses = {}, collections.defaultdict(list)
        for line in open(path, encoding='utf-8'):
            f = line.rstrip('\n').split('\t')
            w = {'id': int(f[0]), 'slug': f[1], 'book': int(f[2]), 'chapter': int(f[3]), 'verse': int(f[4]),
                 'position': int(f[5]), 'surface': f[6], 'lemma': f[7], 'strong': f[8], 'gloss': f[9],
                 'm': json.loads(f[10])}
            w['pos'] = w['m'].get('pos')
            w['greek'] = w['slug'] == 'NESTLE1904'
            self.by_id[w['id']] = w
            self.verses[(w['slug'], w['book'], w['chapter'], w['verse'])].append(w)
        for verse in self.verses.values():
            verse.sort(key=lambda w: w['position'])
            for i, w in enumerate(verse):
                w['index'], w['verse_words'] = i, verse

    def at(self, w, offset):
        i = w['index'] + offset
        return w['verse_words'][i] if 0 <= i < len(w['verse_words']) else None


def joined(w):
    """A BHSA word written without a space after it: a prefix of the next."""
    return not w['greek'] and w['m'].get('phonoTrailer', ' ') == ''


def person_number(o, suffix=False):
    m = o['m']
    if o['greek']:
        return GREEK_PERSON.get(m.get('person')), GREEK_NUMBER.get(m.get('number'))
    if suffix:
        return HEBREW_PERSON.get(m.get('suffixPerson')), HEBREW_NUMBER.get(m.get('suffixNumber'))
    return HEBREW_PERSON.get(m.get('person')), HEBREW_NUMBER.get(m.get('number'))


def agrees(source, o, suffix=False):
    """Person must be stated on both and equal; number must not disagree where both state it."""
    sm = source['SourceMorphology'] or {}
    sp, sn = ENGLISH_PERSON.get(sm.get('Person')), ENGLISH_NUMBER.get(sm.get('Number'))
    op, on = person_number(o, suffix)
    if sp is None or op is None or sp != op:
        return False
    return sn is None or on is None or sn == on


def group_of(r):
    rel, pos = r['Relation'] or '', r['SourcePartOfSpeech']
    surface = r['SourceSurface'].lower()
    base = rel.split(':')[0]
    feats = r['SourceMorphology'] or {}
    if feats.get('Poss') == 'Yes':
        return 'nmod:poss'
    if pos == 'ADP' and base == 'case':
        return 'case'
    if base == 'det' and pos == 'DET':
        if surface == 'the' or feats.get('PronType') == 'Art' and feats.get('Definite') == 'Def':
            return 'det the'
        if surface in ('a', 'an') or feats.get('PronType') == 'Art' and feats.get('Definite') == 'Ind':
            return 'det a'
        return None
    if base == 'cc':
        return 'cc'
    if base == 'mark' and pos == 'PART' and surface == 'to':
        return 'mark to'
    if base == 'mark':
        return 'mark'
    if base in ('aux', 'cop'):
        return base
    if pos in ('PRON', 'PROPN') and feats.get('PronType') == 'Prs' and feats.get('Poss') != 'Yes' and feats.get('Reflex') is None:
        if base in ('nsubj',):
            return 'nsubj pron'
        if base in ('obj', 'iobj', 'obl', 'nmod'):
            return 'obj pron'
        if base == 'expl':
            return 'expl'
    if rel == 'nmod:poss' or feats.get('Poss') == 'Yes':
        return 'nmod:poss'
    if rel == 'compound:prt':
        return 'compound:prt'
    if base == 'advmod' and (surface == 'not' or feats.get('Polarity') == 'Neg'):
        return 'advmod not'
    return None


def same_written_word(a, b):
    if b is None or a['greek'] or b['greek'] or a['verse_words'] is not b['verse_words']:
        return False
    lo, hi = sorted((a['index'], b['index']))
    return all(joined(a['verse_words'][i]) for i in range(lo, hi))


def is_free(o, taken):
    return o['id'] not in taken


def walk_back(H, ok, skip, limit, prefix_only=False):
    """The first word before H that ok() accepts, passing words skip() accepts, at most limit words back."""
    for k in range(1, limit + 1):
        o = H_at = None
        o = H['verse_words'][H['index'] - k] if H['index'] - k >= 0 else None
        if o is None:
            return None
        if prefix_only and not joined(o):
            return None
        if ok(o):
            return o
        if not skip(o):
            return None
    return None


def site(r, group, H, taken):
    """(target, site name) or (None, reason)."""
    greek = H['greek']
    pos = H['pos']
    if group == 'case':
        kind = lambda o: (o['pos'] == 'prep' and o['strong'] != HEBREW_OBJECT_MARKER) or (greek and o['pos'] == 'adv' and o['gloss'].split(' ')[0] in ENGLISH_PREPOSITIONS)
        skip = lambda o: o['pos'] in ('art', 'det') or (greek and o['pos'] == 'adj')
        o = walk_back(H, kind, skip, 3)
        if o:
            return (o, 'own word before') if is_free(o, taken) else (None, 'own word held')
        return H, 'head'
    if group == 'det the':
        own = lambda o: o['pos'] in ('art', 'det') and (o['strong'] in ('H9009',) or o['lemma'] == 'ὁ')
        skip = lambda o: o['pos'] in ('adjv', 'adj') or (not greek and o['pos'] == 'prep' and joined(o))
        o = walk_back(H, own, skip, 2)
        if o:
            return (o, 'own word before') if is_free(o, taken) else (None, 'own word held')
        if not greek and H['m'].get('state') == 'c':
            return H, 'head: construct'
        if not greek and H['m'].get('suffixPerson'):
            return H, 'head: suffix'
        if pos in ('nmpr',) or (greek and H['m'].get('pos') == 'noun' and H['lemma'] and H['lemma'][0].isupper()):
            return H, 'head: name'
        return H, 'head: other'
    if group == 'det a':
        return H, 'head'
    if group == 'cc':
        own = lambda o: o['pos'] == 'conj'
        skip = lambda o: (not greek and joined(o)) or (greek and o['pos'] in ('det', 'prep'))
        o = walk_back(H, own, skip, 3)
        if o:
            return (o, 'own word before') if is_free(o, taken) else (None, 'own word held')
        return H, 'head'
    if group == 'mark to':
        own = lambda o: o['strong'] == 'H9005'
        o = walk_back(H, own, lambda o: False, 1)
        if o:
            return (o, 'own word before') if is_free(o, taken) else (None, 'own word held')
        return H, 'head'
    if group == 'mark':
        own = lambda o: (o['strong'] in SUBORDINATORS or o['pos'] == 'conj' and o['strong'] not in ('H9000', 'G2532', 'G1161'))
        o = walk_back(H, own, lambda o: True, 3)
        if o:
            return (o, 'own word before') if is_free(o, taken) else (None, 'own word held')
        return H, 'head'
    if group in ('aux', 'cop'):
        be = HEBREW_BE if not greek else GREEK_BE
        for k in (-1, 1, -2, 2):
            o = H['verse_words'][H['index'] + k] if 0 <= H['index'] + k < len(H['verse_words']) else None
            if o and o['strong'] in be and is_free(o, taken):
                return o, 'own word beside'
        return H, 'head' if pos == 'verb' else 'head: not a verb'
    if group == 'nsubj pron':
        for k in (-1, 1, -2, 2):
            o = H['verse_words'][H['index'] + k] if 0 <= H['index'] + k < len(H['verse_words']) else None
            if o and is_free(o, taken) and (o['pos'] == 'prps' or (greek and o['pos'] == 'pron' and o['m'].get('case') == 'nominative')) \
                    and agrees(r, o):
                return o, 'own word beside'
        if pos == 'verb':
            if agrees(r, H):
                return H, 'head: agreeing verb'
            return None, 'verb disagrees'
        return H, 'head: not a verb'
    if group == 'obj pron':
        for k in (1, 2, 3, -1):
            o = H['verse_words'][H['index'] + k] if 0 <= H['index'] + k < len(H['verse_words']) else None
            if o is None or not is_free(o, taken):
                continue
            if not greek and o['m'].get('suffixPerson') and o['pos'] == 'prep' and agrees(r, o, suffix=True):
                return o, 'own word beside'
            if greek and o['pos'] == 'pron' and o['m'].get('case') in ('accusative', 'dative', 'genitive') and agrees(r, o):
                return o, 'own word beside'
        if not greek and H['m'].get('suffixPerson') and agrees(r, H, suffix=True):
            return H, 'head: suffix'
        return None, 'no site'
    if group == 'nmod:poss':
        if not greek:
            if H['m'].get('suffixPerson') and agrees(r, H, suffix=True):
                return H, 'head: suffix'
            return None, 'no suffix'
        for k in (1, 2, -1):
            o = H['verse_words'][H['index'] + k] if 0 <= H['index'] + k < len(H['verse_words']) else None
            if o and is_free(o, taken) and o['pos'] == 'pron' and o['m'].get('case') == 'genitive' and agrees(r, o):
                return o, 'own word beside'
        return None, 'no genitive pronoun'
    if group == 'expl':
        return H, 'head'
    if group == 'compound:prt':
        return H, 'head'
    if group == 'advmod not':
        own = lambda o: o['strong'] in NEGATIONS and is_free(o, taken)
        o = walk_back(H, own, lambda o: True, 3)
        return (o, 'own word before') if o else (None, 'no negation')
    return None, 'no rule'


def main():
    args = sys.argv[1:]
    label, sets, tsv = args[0], args[1].split(','), args[2]
    want = (args[args.index('--examples') + 1], int(args[args.index('--examples') + 2])) if '--examples' in args else None
    original = Original(tsv)
    rows = [r for r in words(label, sets) if r['GoldCoversSourceWord']]
    total = len(rows)
    by_id = {r['SourceWordId']: r for r in rows}
    taken = {r['TargetWordId'] for r in rows if r['State'] in PLACED and r['TargetWordId']}

    stats = collections.defaultdict(lambda: collections.Counter())
    examples = collections.defaultdict(list)
    missing = 0
    for r in rows:
        if not r['Grammatical']:
            continue
        group = group_of(r)
        if group is None:
            continue
        head = by_id.get(r['ParseHeadWordId'])
        s = stats[group]
        s['words'] += 1
        current = r['State']
        if head is None or head['State'] not in PLACED or not head['TargetWordId']:
            s['head not placed'] += 1
            continue
        H = original.by_id.get(head['TargetWordId'])
        if H is None:
            missing += 1
            continue
        head_right = bool(head['CorrectOnSplitKey'])
        s['head placed'] += 1
        s['head placed right'] += head_right
        target, name = site(r, group, H, taken)
        if target is None:
            s['no site'] += 1
            s[f'no site: {name}'] += 1
            continue
        right = target['id'] in set(r['SplitKey'])
        lenient = right or any(same_written_word(target, original.by_id.get(t)) for t in r['SplitKey'])
        new = current == 'unresolved'
        for scope in ('all', 'new' if new else 'already'):
            s[f'{scope} placed'] += 1
            s[f'{scope} right'] += right
            if head_right:
                s[f'{scope} placed, head right'] += 1
                s[f'{scope} right, head right'] += right
        s[f'site {name}'] += 1
        s[f'site {name} right'] += right
        if new:
            s[f'new site {name}'] += 1
            s[f'new site {name} right'] += right
            s['new lenient'] += lenient
        if not current == 'unresolved':
            s['already, agrees with today'] += target['id'] == r['TargetWordId']
        examples[(group, right, new)].append((r, target, name, head))

    print(f'{label} {",".join(sets)}: {total:,} key-covered words; heads missing from the export {missing}\n')
    print(f'{"relation":<14} {"words":>6} {"head placed":>12} {"rule fires":>11} {"right (all fired)":>22} '
          f'{"new: fired":>10} {"new: right":>20} {"new, head right":>20} {"points":>7}')
    totals = collections.Counter()
    for group, s in sorted(stats.items(), key=lambda kv: -kv[1]['words']):
        totals.update(s)
        print(f'{group:<14} {s["words"]:6,} {s["head placed"]:12,} {s["all placed"]:11,} '
              f'{share(s["all right"], s["all placed"]):>22} {s["new placed"]:10,} {share(s["new right"], s["new placed"]):>20} '
              f'{share(s["new right, head right"], s["new placed, head right"]):>20} {100 * s["new right"] / total:7.2f}')
    print(f'{"total":<14} {totals["words"]:6,} {totals["head placed"]:12,} {totals["all placed"]:11,} '
          f'{share(totals["all right"], totals["all placed"]):>22} {totals["new placed"]:10,} {share(totals["new right"], totals["new placed"]):>20} '
          f'{share(totals["new right, head right"], totals["new placed, head right"]):>20} {100 * totals["new right"] / total:7.2f}')
    print(f'\nnew words, the key\'s word in the same written word counted right: {share(totals["new lenient"], totals["new placed"])}')
    print('\nby site, new words only:')
    for group, s in sorted(stats.items(), key=lambda kv: -kv[1]['words']):
        parts = [f'{k[9:]} {share(s[k + " right"], s[k])}' for k in sorted(s) if k.startswith('new site ') and not k.endswith(' right')]
        print(f'  {group:<14} ' + '; '.join(parts) + f'   [lenient {share(s["new lenient"], s["new placed"])}]')
    print('\nby site (all fired):')
    for group, s in sorted(stats.items(), key=lambda kv: -kv[1]['words']):
        parts = [f'{k[5:]} {share(s[k + " right"], s[k])}' for k in sorted(s) if k.startswith('site ') and not k.endswith(' right')]
        nos = [f'{k[9:]} {s[k]}' for k in sorted(s) if k.startswith('no site: ')]
        print(f'  {group:<14} ' + '; '.join(parts) + ('   [none: ' + ', '.join(nos) + ']' if nos else ''))

    if want:
        group, n = want
        rng = random.Random(20261015)
        for right in (False, True):
            pool = [e for (g, ok, new), es in examples.items() if g == group and ok == right and new for e in es]
            print(f'\n{group}, new, {"right" if right else "wrong"} ({len(pool)}):')
            for r, target, name, head in rng.sample(pool, min(n, len(pool))):
                key = ' '.join(f'{g["Surface"]}({g["Gloss"]})' + ('*' if g['TargetWordId'] in r['SplitKey'] else '') for g in r['Gold'])
                print(f'  {r["Passage"]} {r["CanonicalBook"]}:{r["CanonicalChapter"]}:{r["CanonicalVerse"]} {r["SourceSurface"]!r} '
                      f'head {head["SourceSurface"]!r}->{head["TargetSurface"]} [{name}] -> {target["surface"] or "(empty)"}({target["gloss"]}) | key: {key}')


if __name__ == '__main__':
    main()
