"""
What a reader stops seeing when only our own verse references are shown, and why each one is not ours.

The reader shows a verse under a record only where a source other than BibleData states it. This
reads the corpus and says what that leaves out: every (record, verse) only BibleData lists, sorted
into what the text says there, the records that lose verses with how many they keep, and the records
BibleData supplied that are not yet read as ours.

    python scripts/reference-cutover.py                  # a summary on the screen
    python scripts/reference-cutover.py --write          # and Resources/Essenthos/review/references-only-bibledata.json
    ESSENTHOS_DB=essenthos_core_s17 python scripts/reference-cutover.py

Read-only. The classes, in the order they are tried:

    word-for-god    the record is YHVH; the verse has elohim, el, eloah, theos or kyrios, which the
                    owner ruled are not the name
    not-a-name      the record is headed by a common noun or a description
    title           the record is one of the dataset's per-king Pharaohs or Caesars; the owner ruled
                    the word a title
    people          the record is a people; the verse prints the ancestor's or the realm's name, which
                    the owner's rulings read as the man or the realm, or prints no name of the people
    other-bearer    the name is printed and our words name another record of that name there
    unsettled       the name is printed, the original word carries a number of the record, and no
                    annotation stands on it: namesakes nothing has told apart
    other-number    the King James prints the name and no original word of the verse carries a number
                    the record is held under
    not-printed     the verse prints no name of the record: a title, a pronoun, another name
"""

import argparse
import collections
import datetime
import json
import os
import re

import descriptors as shared

shared.DATABASE = os.environ.get('ESSENTHOS_DB', shared.DATABASE)

WITNESS = 'BibleData by%'
NOT_A_NAME = ('title', 'description', 'term', 'gentilic', 'collective')
ORIGINALS = ('BHSA', 'NESTLE1904')
REVIEW = os.path.join('Resources', 'Essenthos', 'review', 'references-only-bibledata.json')
GOD = ('yhvh', 'yhvh-2')
TITLED = re.compile(r'^(pharaoh|caesar)(-\d+)?$')

CLASSES = {
    'word-for-god': "YHVH where the verse has a word for God and not the name; the owner's rule of 2026-09-11",
    'not-a-name': 'the record is headed by a common noun or a description',
    'title': "one of the dataset's per-king Pharaohs or Caesars; the owner ruled the word a title",
    'people': "a people's verse that prints the ancestor's or the realm's name, or no name of the people",
    'other-bearer': 'the name is printed and our words name another record of that name there',
    'unsettled': 'the name is printed and nothing has told its bearers apart there',
    'other-number': 'the name is printed under a number the record is not held under',
    'not-printed': 'the verse prints no name of the record: a title, a pronoun or another name',
}


def read():
    return shared.psql(f"""
        WITH listed AS (
            SELECT entity_id, canonical_book b, canonical_chapter c, canonical_verse v,
                   min(label) AS label
            FROM entity_verse WHERE source LIKE '{WITNESS}' GROUP BY 1, 2, 3, 4
        ),
        ours AS (
            SELECT DISTINCT entity_id, canonical_book b, canonical_chapter c, canonical_verse v
            FROM entity_verse WHERE source NOT LIKE '{WITNESS}'
        ),
        alone AS (
            SELECT l.* FROM listed l
            WHERE NOT EXISTS (SELECT 1 FROM ours o WHERE o.entity_id = l.entity_id
                                AND o.b = l.b AND o.c = l.c AND o.v = l.v)
        ),
        verses AS (SELECT DISTINCT b, c, v FROM alone),
        words AS (
            SELECT x.b, x.c, x.v, t.slug, w.id, w.position, w.text, w.strong_number
            FROM verses x
            JOIN verse_reference r ON r.canonical_book = x.b AND r.canonical_chapter = x.c
                                  AND r.canonical_verse = x.v AND r.is_primary
            JOIN word w ON w.verse_id = r.verse_id
            JOIN text t ON t.id = w.text_id AND t.slug IN ('BHSA', 'NESTLE1904', 'KJV')
        )
        SELECT json_build_object(
            'alone', (SELECT coalesce(json_agg(json_build_array(entity_id, b, c, v, label)), '[]') FROM alone),
            'kept', (SELECT coalesce(json_object_agg(entity_id, verses), '{{}}') FROM (
                        SELECT entity_id, count(*) AS verses FROM ours GROUP BY 1) k),
            'words', (SELECT coalesce(json_agg(json_build_array(
                          b, c, v, slug, position, text, strong_number,
                          (SELECT json_agg(DISTINCT a.entity_id) FROM word_entity a WHERE a.word_id = words.id))), '[]')
                      FROM words),
            'entities', (SELECT json_object_agg(e.id, json_build_object(
                             'slug', e.slug, 'name', e.name, 'kind', e.kind,
                             'supplied', e.source LIKE '{WITNESS}',
                             'described', EXISTS (SELECT 1 FROM entity_descriptor d WHERE d.entity_id = e.id),
                             'words', EXISTS (SELECT 1 FROM word_entity a WHERE a.entity_id = e.id),
                             'listed', EXISTS (SELECT 1 FROM entity_verse ev WHERE ev.entity_id = e.id
                                                 AND ev.source LIKE '{WITNESS}'),
                             'names', (SELECT coalesce(json_agg(json_build_array(
                                                 n.label, n.hebrew_strong_number, n.greek_strong_number, n.kind)), '[]')
                                       FROM entity_name n
                                       WHERE coalesce(n.aspect_of_entity_id, n.entity_id) = e.id)))
                         FROM entity e))
    """)


def numbers(entity):
    found = set()
    for _, hebrew, greek, kind in entity['names']:
        if (kind or '') in NOT_A_NAME:
            continue
        found.update(n for n in (hebrew, greek) if n and ',' not in n)
    return found


def labels(entity):
    found = {entity['name']}
    found.update(label for label, _, _, kind in entity['names'] if (kind or '') not in NOT_A_NAME)
    return {label.strip() for label in found if label and label.strip()}


def printed(entity, english):
    return any(re.search(r'(?<![A-Za-z])' + re.escape(label) + r'(?![a-z])', english)
               for label in labels(entity))


def classify(entity, original, english, entities, asked):
    if entity['slug'] in GOD:
        return 'word-for-god', []
    if entity['name'][:1].islower() or entity['slug'] in ('earth', 'thegarden', 'daughter'):
        return 'not-a-name', []
    if TITLED.match(entity['slug']):
        return 'title', []
    if entity['kind'] == 'people':
        return 'people', []

    held = numbers(entity)
    named = [word for word in original if word[6] in held]
    if named:
        others = sorted({entities[str(other)]['slug'] for word in named for other in (word[7] or [])
                         if other != asked})
        if others:
            return 'other-bearer', others
        return 'unsettled', []
    return ('other-number' if printed(entity, english) else 'not-printed'), []


def reference(book, chapter, verse):
    return shared.reference(book, chapter, verse)


def report():
    corpus = read()
    entities = corpus['entities']
    by_verse = collections.defaultdict(list)
    for word in corpus['words']:
        by_verse[tuple(word[:3])].append(word)

    rows = []
    for entity_id, b, c, v, label in corpus['alone']:
        entity = entities[str(entity_id)]
        words = sorted(by_verse[(b, c, v)], key=lambda w: w[4])
        original = [w for w in words if w[3] in ORIGINALS]
        english = ' '.join(w[5] for w in words if w[3] == 'KJV')
        kind, others = classify(entity, original, english, entities, entity_id)
        rows.append({'entity': entity_id, 'slug': entity['slug'], 'at': (b, c, v), 'label': label,
                     'class': kind, 'ours': others})

    lost = collections.Counter(row['slug'] for row in rows)
    kept = {entities[entity_id]['slug']: verses for entity_id, verses in corpus['kept'].items()}
    classes = collections.Counter(row['class'] for row in rows)
    in_class = collections.defaultdict(set)
    for row in rows:
        in_class[row['class']].add(row['slug'])

    losers = [{'slug': slug, 'name': next(e['name'] for e in entities.values() if e['slug'] == slug),
               'lost': count, 'kept': kept.get(slug, 0)}
              for slug, count in sorted(lost.items(), key=lambda pair: (-pair[1], pair[0]))]

    listed = [{'slug': row['slug'], 'reference': reference(*row['at']), 'class': row['class'],
               **({'ours': row['ours']} if row['ours'] else {})}
              for row in sorted(rows, key=lambda r: (r['class'], r['at'], r['slug']))
              if row['class'] in ('other-bearer', 'unsettled', 'other-number')]

    unprinted = collections.defaultdict(collections.Counter)
    for row in rows:
        if row['class'] == 'not-printed':
            unprinted[row['slug']][row['label'] or ''] += 1
    titles = [{'slug': slug, 'verses': sum(counts.values()),
               'labels': dict(sorted(counts.items(), key=lambda pair: (-pair[1], pair[0])))}
              for slug, counts in sorted(unprinted.items(), key=lambda pair: (-sum(pair[1].values()), pair[0]))]

    own_verses = set(kept)
    records = {'noLine': [], 'nothingOfOurs': []}
    why = collections.defaultdict(collections.Counter)
    for row in rows:
        why[row['slug']][row['class']] += 1
    supplied = sorted((e for e in entities.values() if e['supplied']), key=lambda e: e['slug'])
    for entity in supplied:
        has_verses = entity['slug'] in own_verses
        if entity['described'] and has_verses:
            continue
        if has_verses:
            records['noLine'].append({'slug': entity['slug'], 'name': entity['name'], 'verses': kept[entity['slug']]})
            continue
        reasons = why.get(entity['slug'])
        records['nothingOfOurs'].append({
            'slug': entity['slug'], 'name': entity['name'], 'line': entity['described'],
            'listed': lost.get(entity['slug'], 0),
            'why': ('the dataset lists no verse for it' if not entity['listed']
                    else ', '.join(f'{kind} {count}' for kind, count in reasons.most_common()) if reasons
                    else 'no word of ours names it')})

    return {
        'about': 'The verse references only BibleData states, which a reader is not shown. classes says what '
                 'the text has at each; losers every record that loses verses, with the verses of ours it '
                 'keeps; namesakes each reference where the name is printed and no rule decides the bearer '
                 '(ours names the records our words name there instead); titles the records listed where '
                 'the verse does not print their name, with the labels the dataset gives; records the '
                 'BibleData records that are not yet read as ours. Reference is the canonical address.',
        'written': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'references': len(rows),
        'entities': len(lost),
        'classes': [{'class': kind, 'references': classes[kind], 'entities': len(in_class[kind]), 'what': what}
                    for kind, what in CLASSES.items()],
        'losers': losers,
        'namesakes': listed,
        'titles': titles,
        'records': records,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--write', action='store_true', help=f'write {REVIEW}')
    parser.add_argument('--top', type=int, default=20, help='how many of the records losing most to print')
    args = parser.parse_args()

    found = report()
    print(f"{found['references']} references only BibleData states, over {found['entities']} records")
    for row in found['classes']:
        print(f"  {row['class']:14} {row['references']:5}  {row['entities']:4} records")
    print(f"the {args.top} records that lose most (lost / kept):")
    for row in found['losers'][:args.top]:
        print(f"  {row['slug']:22} {row['lost']:5} / {row['kept']}")
    emptied = [row for row in found['losers'] if row['kept'] == 0]
    print(f"{len(emptied)} records keep no verse; {len(found['namesakes'])} namesake references listed; "
          f"records without a line {len(found['records']['noLine'])}, with nothing of ours "
          f"{len(found['records']['nothingOfOurs'])}")

    if args.write:
        os.makedirs(os.path.dirname(REVIEW), exist_ok=True)
        with open(REVIEW, 'w', encoding='utf-8', newline='\n') as out:
            json.dump(found, out, ensure_ascii=False, indent=2)
            out.write('\n')
        print(f'wrote {REVIEW}')


if __name__ == '__main__':
    main()
