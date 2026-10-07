"""
Names every word of the model's sense answers and of the two review files beside them by its address
(text, the verse as that text numbers it, position, surface) instead of by its row id, which a rebuilt
corpus gives to another word.

    python scripts/address-sense-readings.py query <resources>  > addresses.sql
    (run addresses.sql against the corpus the ids were written against; it prints CSV)
    python scripts/address-sense-readings.py apply <resources> addresses.csv

`apply` rewrites every Resources/SenseReadings/**/answers*.jsonl and the repository's
Essenthos.Forge/Loading/Encyclopedia/RefusedReadings.json and SupersededReadings.json, and refuses to
write anything if one id has no address. Run it once; a file already addressed is left as it is.
"""
import csv
import glob
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENCYCLOPEDIA = os.path.join(REPO, 'Essenthos.Forge', 'Loading', 'Encyclopedia')
REVIEWS = ('RefusedReadings', 'SupersededReadings')


def codes():
    source = open(os.path.join(REPO, 'Essenthos.Forge', 'Loading', 'Frame', 'BookCodes.cs'), encoding='utf-8-sig').read()
    return {int(n): c.upper() for c, n in re.findall(r'\["(\w+)"\] = (\d+)', source)}


def answer_files(resources):
    return sorted(glob.glob(os.path.join(resources, 'SenseReadings', '**', 'answers*.jsonl'), recursive=True))


def ids(resources):
    found = set()
    for path in answer_files(resources):
        for line in open(path, encoding='utf-8'):
            if line.strip() and 'word_id' in (row := json.loads(line)):
                found.add(int(row['word_id']))
    for name in REVIEWS:
        data = json.load(open(os.path.join(ENCYCLOPEDIA, name + '.json'), encoding='utf-8'))
        found.update(int(r['wordId']) for r in data['readings'] if 'wordId' in r)
    return sorted(found)


def query(resources):
    every = ids(resources)
    print("SET statement_timeout = '300s';")
    print("\\copy (SELECT w.id, t.slug, b.canonical_ordinal, v.chapter_number, v.number, v.label, w.position, w.text "
          "FROM unnest(ARRAY[" + ','.join(map(str, every)) + "]::bigint[]) AS x(id) JOIN word w ON w.id = x.id "
          "JOIN verse v ON v.id = w.verse_id JOIN book b ON b.id = v.book_id JOIN text t ON t.id = w.text_id) "
          "TO STDOUT WITH CSV HEADER")


def apply(resources, table):
    book = codes()
    rows = {int(r['id']): r for r in csv.DictReader(open(table, encoding='utf-8'))}
    missing = [i for i in ids(resources) if i not in rows]
    if missing:
        sys.exit(f'{len(missing)} ids have no address in {table}: {missing[:10]}. Nothing was written.')

    def address(word_id):
        r = rows[int(word_id)]
        ordinal = int(r['canonical_ordinal'])
        if ordinal not in book:
            sys.exit(f'word {word_id} is in book {ordinal}, which has no code. Nothing was written.')
        return {'text': r['slug'], 'reference': f"{book[ordinal]} {r['chapter_number']}:{r['number']}{r['label']}",
                'position': int(r['position']), 'surface': r['text']}

    pending = []
    for path in answer_files(resources):
        out, count = [], 0
        for line in open(path, encoding='utf-8'):
            if not line.strip():
                continue
            row = json.loads(line)
            if 'word_id' in row:
                word_id = row.pop('word_id')
                row = {**address(word_id), **row}
                count += 1
            out.append(json.dumps(row, ensure_ascii=False))
        pending.append((path, '\n'.join(out) + '\n', count))

    for name in REVIEWS:
        path = os.path.join(ENCYCLOPEDIA, name + '.json')
        data = json.load(open(path, encoding='utf-8'))
        count = 0
        readings = []
        for r in data['readings']:
            if 'wordId' in r:
                word_id = r.pop('wordId')
                r.pop('reference', None)
                r = {**address(word_id), **r}
                count += 1
            readings.append(r)
        data['readings'] = readings
        pending.append((path, json.dumps(data, ensure_ascii=False, indent=2) + '\n', count))

    for path, text, count in pending:
        if count:
            with open(path, 'w', encoding='utf-8', newline='\n') as handle:
                handle.write(text)
        print(f'{count:6} addressed  {os.path.relpath(path, REPO if path.startswith(REPO) else resources)}')


if __name__ == '__main__':
    if len(sys.argv) >= 3 and sys.argv[1] == 'query':
        query(sys.argv[2])
    elif len(sys.argv) >= 4 and sys.argv[1] == 'apply':
        apply(sys.argv[2], sys.argv[3])
    else:
        sys.exit(__doc__)
