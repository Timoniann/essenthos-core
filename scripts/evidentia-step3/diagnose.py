"""Where the words that are not explained-and-right go: every key-covered word of a benchmark label, sorted into
one cause each, with counts and points of all words.

usage: diagnose.py <label> <sets> [--edges <label>] [--examples <cause> <n>]

Reads <label>'s word records (and the edges of --edges, default the same label, for the content words' candidate
graph); nothing else. The combiner's probability of an edge is the weights file's (fitted on ind+val, calibrated on
val2), so on ind and val it is in-sample.
"""
import collections
import random
import sys

from common import Combiner, edges, explained_right, share, words

GREEK_ARTICLE = 'ὁ'
HEBREW_ARTICLE = 'H9009'


def relation_group(r):
    rel = (r['Relation'] or '').split(':')[0]
    pos = r['SourcePartOfSpeech']
    if rel == 'case':
        return 'case (preposition)'
    if rel in ('nsubj', 'obj', 'iobj', 'expl') and pos == 'PRON':
        return f'{rel} pronoun'
    if rel == 'nmod' and pos == 'PRON' or (r['Relation'] == 'nmod:poss'):
        return 'nmod:poss (possessive)'
    if rel in ('aux', 'cop'):
        return 'aux/cop'
    if rel == 'det':
        return 'det (article, determiner)'
    if rel in ('cc', 'mark'):
        return f'{rel} (conjunction)'
    return f'other: {r["Relation"]}/{pos}'


def main():
    args = sys.argv[1:]
    label, sets = args[0], args[1].split(',')
    edge_label = args[args.index('--edges') + 1] if '--edges' in args else label
    want = (args[args.index('--examples') + 1], int(args[args.index('--examples') + 2])) if '--examples' in args else None

    rows = [r for r in words(label, sets) if r['GoldCoversSourceWord']]
    by_id = {r['SourceWordId']: r for r in rows}
    total = len(rows)
    _, edge_rows = edges(edge_label, sets)
    combiner = Combiner()
    edges_of = collections.defaultdict(list)
    for e in edge_rows:
        edges_of[e['SourceWordId']].append(e)

    causes = collections.Counter()
    examples = collections.defaultdict(list)
    gram_detail = collections.Counter()

    def put(r, cause):
        causes[cause] += 1
        examples[cause].append(r)

    right = 0
    for r in rows:
        if explained_right(r):
            right += 1
            continue
        state = r['State']
        if state in ('linked', 'attached', 'phrase member'):
            kind = 'grammatical' if r['Grammatical'] else 'content'
            if r['KeyReading'] is None:
                put(r, f'placed wrong, {kind}: the key unread')
            elif r['CorrectOnJudgedKey']:
                put(r, f'placed wrong, {kind}: read, the key wrong or both defensible')
            else:
                put(r, f'placed wrong, {kind}: read, ours wrong')
            continue
        if state == 'supplied':
            put(r, 'supplied, the key names a counterpart')
            continue
        targets = set(r['SplitKey'])
        gold = {g['TargetWordId']: g for g in r['Gold']}
        if r['Grammatical']:
            head = by_id.get(r['HeadWordId']) if r['HeadWordId'] else None
            group = relation_group(r)
            if head is None or r['HeadState'] in (None, 'None'):
                site = 'no head'
            elif head['State'] == 'supplied':
                site = 'head supplied'
            elif head['State'] == 'unresolved':
                site = 'head unplaced'
            elif head['TargetWordId'] in targets:
                site = 'head placed, key on the head' + ('' if head['CorrectOnSplitKey'] else ' (head wrong by the key)')
            elif head['CorrectOnSplitKey']:
                site = 'head placed right, key on another word'
            else:
                site = 'head placed wrong, key elsewhere'
            article = (r['SourceSurface'].lower() == 'the'
                       and not any(gold.get(t, {}).get('StrongNumber') == HEBREW_ARTICLE
                                   or gold.get(t, {}).get('Lemma') == GREEK_ARTICLE for t in targets))
            if article:
                group = 'det: the, the original writes no article'
            gram_detail[(group, site)] += 1
            put(r, f'grammatical unattached: {site}')
            continue
        # content word left unresolved
        held = [gold[t]['HeldBy'] for t in targets if t in gold and gold[t]['HeldBy'] not in (None, r['SourceWordId'])]
        held_right = [h for h in held if h in by_id and by_id[h]['TargetWordId'] in targets and by_id[h]['CorrectOnSplitKey']]
        mine = edges_of.get(r['SourceWordId'], [])
        right_edges = [e for e in mine if e['TargetWordId'] in targets]
        if held_right:
            put(r, 'content unresolved: its original word rendered by another word (several on one)')
        elif not mine:
            put(r, 'content unresolved: no edge at all')
        elif not right_edges:
            put(r, 'content unresolved: edges, none to the right word' + (' (right word held wrongly)' if held else ''))
        else:
            best = max(combiner.calibrated(e) for e in right_edges)
            band = ('>= 0.95' if best >= 0.95 else '0.90-0.95' if best >= 0.90 else '0.80-0.90' if best >= 0.80
                    else '0.50-0.80' if best >= 0.50 else '< 0.50')
            put(r, f'content unresolved: right edge offered, calibrated {band}')

    print(f'{label} {",".join(sets)}: {total:,} key-covered words; explained and right {share(right, total)}; '
          f'the rest {total - right:,} = {100 * (total - right) / total:.2f} points\n')
    print(f'{"cause":<92} {"words":>7} {"points":>7}')
    for cause, n in sorted(causes.items(), key=lambda kv: -kv[1]):
        print(f'{cause:<92} {n:7,} {100 * n / total:7.2f}')
    print(f'\ngrammatical unattached, by relation and by where the head stands (words, points):')
    groups = collections.Counter()
    for (group, site), n in gram_detail.items():
        groups[group] += n
    for group, n in groups.most_common():
        sites = ', '.join(f'{s} {m}' for (g, s), m in sorted(gram_detail.items(), key=lambda kv: -kv[1]) if g == group)
        print(f'  {group:<44} {n:6,} {100 * n / total:6.2f}  [{sites}]')

    if want:
        cause, n = want
        rng = random.Random(20261015)
        pick = [r for c, rs in examples.items() if c.startswith(cause) for r in rs]
        for r in rng.sample(pick, min(n, len(pick))):
            gold = ' '.join(f'{g["Surface"]}({g["Gloss"]})' + ('*' if g['TargetWordId'] in r['SplitKey'] else '') for g in r['Gold'])
            print(f'{r["Passage"]} {r["CanonicalBook"]}:{r["CanonicalChapter"]}:{r["CanonicalVerse"]} {r["SourceSurface"]!r} '
                  f'{r["SourcePartOfSpeech"]} {r["Relation"]} state={r["State"]} placed={r["TargetSurface"]} key: {gold}')


if __name__ == '__main__':
    main()
