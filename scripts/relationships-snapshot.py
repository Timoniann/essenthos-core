"""
Every relationship this corpus holds as its own, as one sorted file, so two states of the database
can be compared line by line.

    python scripts/relationships-snapshot.py before.tsv
    python scripts/relationships-snapshot.py after.tsv
    python scripts/relationships-snapshot.py after.tsv --against before.tsv

One line per row of `entity_relationship` whose source is not a dataset's: the slug it reads from,
the relation, the slug it reads to, the verse (the whole citation where the row cites a passage or
two verses), the source, the method and whether the row is withdrawn. Slugs and verses rather than
row ids, because a rebuild renumbers the rows and the comparison is about what a page says.

One query, read-only on the server's side and bounded by a statement timeout. It goes through the
database container, so no connection string or password is read here.
"""

import argparse
import json
import os
import subprocess
import sys

CONTAINER = os.environ.get('ESSENTHOS_DB_CONTAINER', 'essenthos-core-db-1')
DATABASE = 'essenthos_core'
USER = 'essenthos'
READ_ONLY = 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=60000'

# The one dataset that has ever written relationship rows. Everything else in the table is ours.
DATASET = 'BibleData'

BOOKS = [
    'GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH',
    '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK',
    'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL',
    'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH',
    '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD',
    'REV',
]

COLUMNS = ('from', 'type', 'to', 'verse', 'source', 'method', 'withdrawn')

QUERY = f"""
    SELECT coalesce(json_agg(json_build_object(
               'from', f.slug, 'type', r.type, 'to', t.slug,
               'book', r.canonical_book, 'chapter', r.canonical_chapter, 'verse', r.canonical_verse,
               'citation', r.citation, 'source', r.source, 'method', r.method,
               'withdrawn', coalesce(to_jsonb(r) ->> 'withdrawn', 'false') = 'true')), '[]')
    FROM entity_relationship r
    JOIN entity f ON f.id = r.from_entity_id
    JOIN entity t ON t.id = r.to_entity_id
    WHERE r.source NOT LIKE '{DATASET}%'
"""


def rows(database):
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', '-e', READ_ONLY, CONTAINER,
         'psql', '-X', '-U', USER, '-d', database, '-Aqt', '-v', 'ON_ERROR_STOP=1', '-f', '-'],
        input=QUERY.encode('utf-8'), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if process.returncode != 0:
        raise SystemExit(
            f'the query failed against {database} in container {CONTAINER}:\n'
            f"{process.stderr.decode('utf-8', 'replace')}\n"
            'Check that the container is running -- docker ps -- and that the database name is right.')
    return json.loads(process.stdout.decode('utf-8').strip() or '[]')


def verse(row):
    if row['citation']:
        return ' '.join(row['citation'].split())
    if not row['book']:
        return ''
    code = BOOKS[row['book'] - 1] if 1 <= row['book'] <= len(BOOKS) else f"book{row['book']}"
    return f"{code} {row['chapter']}:{row['verse']}"


def lines(database):
    def clean(value):
        return ' '.join(str(value).split())

    return sorted(
        '\t'.join((clean(row['from']), clean(row['type']), clean(row['to']), verse(row),
                   clean(row['source']), clean(row['method']), 'withdrawn' if row['withdrawn'] else ''))
        for row in rows(database))


def read(path):
    with open(path, encoding='utf-8') as handle:
        return [line.rstrip('\n') for line in handle][1:]


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    parser.add_argument('out', help='the file to write, tab-separated, one relationship a line')
    parser.add_argument('--database', default=DATABASE,
                        help='which database to read; a rehearsal copy is how a change is measured before the live corpus takes it')
    parser.add_argument('--against', help='an earlier snapshot; every line only one of the two holds is printed')
    args = parser.parse_args()

    taken = lines(args.database)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('\t'.join(COLUMNS) + '\n')
        handle.writelines(line + '\n' for line in taken)
    print(f'{len(taken)} relationships of ours -> {args.out}')

    if not args.against:
        return
    before = read(args.against)
    gone = sorted(set(before) - set(taken))
    new = sorted(set(taken) - set(before))
    repeats = (len(before) - len(set(before))) - (len(taken) - len(set(taken)))
    for line in gone:
        print(f'- {line}')
    for line in new:
        print(f'+ {line}')
    print(f'{len(gone)} lines only in {args.against}, {len(new)} only in {args.out}'
          + (f'; the count of repeated lines differs by {repeats}' if repeats else ''))
    if gone or new or len(before) != len(taken):
        sys.exit(1)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
