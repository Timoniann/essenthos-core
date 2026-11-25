"""A text's words, links and verse mapping read from the live database, and the chunks a review reads them in.

Read-only: four COPY statements through the database container's own psql, which signs in with the container's
environment, so no password passes through here (guide database). The export is made again only when the database
has changed since the last one: a landing swaps a new database in (another oid) and changes the text's links.

  run/export/{verses,words,links,frame}.tsv   what the database held, as CSV with tabs
  run/export.json                             when, from which database, how many rows (written last)
  run/chunks/<id>.txt, <id>.json              the sheet the reviewer reads, and what its answer is checked against
  run/chunks.json                             the chunks in canonical order

A chunk holds whole chapters up to MAX_VERSES verses, a longer chapter split evenly, and never a verse that a
results file already has a verdict for.

Usage: export.py --text UBIO [--force] [--check]
  --check   say whether the export is current, export nothing (exit 0 current, 3 stale)
"""
import argparse
import collections
import csv
import json
import math
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import PidLock, TextFolder, ResultTally, atomic_write, now, verse_sort_key  # noqa: E402

CONTAINER = os.environ.get('HAND_REVIEW_DB_CONTAINER', 'essenthos-core-db-1')
DATABASE = os.environ.get('HAND_REVIEW_DATABASE', 'essenthos_core')
MAX_VERSES = 45
csv.field_size_limit(1 << 30)


def psql(sql, out=None, timeout=1800):
    """Run SQL in the database container; COPY output goes to the file `out`, else stdout is returned."""
    command = ['docker', 'exec', '-i', CONTAINER, 'sh', '-c',
               f'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d {DATABASE}']
    with open(out, 'wb') if out else open(os.devnull, 'wb') as sink:
        p = subprocess.run(command, input=sql.encode('utf-8'), stdout=sink if out else subprocess.PIPE,
                           stderr=subprocess.PIPE, timeout=timeout)
    if p.returncode:
        raise RuntimeError(f'psql exit {p.returncode}: {p.stderr.decode("utf-8", "replace").strip()[-400:]}')
    return None if out else p.stdout.decode('utf-8', 'replace')


def slugs(text):
    return ', '.join(f"'{s}'" for s in [text.slug, *text.targets])


def targets_sql(text):
    return ', '.join(f"'{s}'" for s in text.targets)


def fingerprint(text):
    """What says the database has changed for this text: the database's oid and the count of its links."""
    sql = ("SET statement_timeout = '60s';\n"
           "COPY (SELECT (SELECT oid FROM pg_database WHERE datname = current_database()), "
           f"(SELECT count(*) FROM link WHERE from_text_id = (SELECT id FROM text WHERE slug = '{text.slug}') "
           f"AND to_text_id IN (SELECT id FROM text WHERE slug IN ({targets_sql(text)})))) TO STDOUT;\n")
    oid, links = psql(sql, timeout=120).split()
    return {'database': oid, 'links': int(links)}


def current(text):
    """The export's own record, or None when there is none or it was cut short."""
    record = text.run / 'export.json'
    if not record.exists():
        return None
    try:
        return json.loads(record.read_text(encoding='utf-8'))
    except json.JSONDecodeError:
        return None


def stale(text):
    """Why the export must be made again, or None when it is current."""
    made = current(text)
    if not made:
        return 'there is no export yet'
    if not (text.run / 'chunks.json').exists():
        return 'the chunks are missing'
    now_is = fingerprint(text)
    if now_is != made.get('fingerprint'):
        return f'the database changed since {made.get("at")} ({made.get("fingerprint")} -> {now_is})'
    return None


def queries(text):
    t = f"(SELECT id FROM text WHERE slug = '{text.slug}')"
    targets = f"(SELECT id FROM text WHERE slug IN ({targets_sql(text)}))"
    copy = "COPY ({}) TO STDOUT WITH (FORMAT csv, DELIMITER E'\\t');"
    return {
        'verses': copy.format(
            "SELECT v.id, b.canonical_ordinal, b.abbreviation, v.chapter_number, v.number, t.slug FROM verse v "
            f"JOIN book b ON b.id = v.book_id JOIN text t ON t.id = v.text_id WHERE t.slug IN ({slugs(text)})"),
        'words': copy.format(
            "SELECT w.id, w.verse_id, w.position, w.text, coalesce(w.gloss, ''), coalesce(w.strong_number, ''), "
            f"w.elided FROM word w JOIN text t ON t.id = w.text_id WHERE t.slug IN ({slugs(text)})"),
        'links': copy.format(
            "SELECT l.id, tt.slug, l.method, coalesce(round(l.confidence::numeric, 2)::text, ''), l.relation, "
            "lw.side, lw.word_id FROM link l JOIN text tt ON tt.id = l.to_text_id "
            f"JOIN link_word lw ON lw.link_id = l.id WHERE l.from_text_id = {t} AND l.to_text_id IN {targets}"),
        'frame': copy.format(
            "SELECT f.verse_id, o.verse_id FROM verse_link vl "
            "JOIN verse_link_verse f ON f.verse_link_id = vl.id AND f.side = 'from' "
            "JOIN verse_link_verse o ON o.verse_link_id = vl.id AND o.side = 'to' "
            f"WHERE vl.from_text_id = {t} AND vl.to_text_id IN {targets}"),
    }


def rows(path):
    with open(path, encoding='utf-8', newline='') as handle:
        yield from csv.reader(handle, delimiter='\t')


class Corpus:
    """The export in memory: verses, words and links, keyed by the export's own row ids (never kept beyond it)."""

    def __init__(self, folder, slug):
        self.slug = slug
        self.verse = {}
        for vid, book, abbr, chapter, number, text in rows(folder / 'verses.tsv'):
            self.verse[int(vid)] = (int(book), abbr, int(chapter), number, text)
        self.words = collections.defaultdict(list)
        self.word = {}
        for wid, vid, position, form, gloss, strong, elided in rows(folder / 'words.tsv'):
            w = (int(wid), int(vid), int(position), form, gloss, strong, elided == 't')
            self.word[w[0]] = w
            self.words[w[1]].append(w)
        for ws in self.words.values():
            ws.sort(key=lambda w: w[2])
        meta, sides = {}, collections.defaultdict(lambda: ([], []))
        for lid, target, method, confidence, relation, side, wid in rows(folder / 'links.tsv'):
            meta[int(lid)] = (target, method, confidence, relation)
            sides[int(lid)][0 if side == 'from' else 1].append(int(wid))
        self.links = {lid: (meta[lid], f, t) for lid, (f, t) in sides.items() if f}
        self.frame = collections.defaultdict(set)
        for f, o in rows(folder / 'frame.tsv'):
            self.frame[int(f)].add(int(o))

    def address(self, vid):
        book, _, chapter, number, _ = self.verse[vid]
        return f'{book}.{chapter}:{number}'

    def word_address(self, wid):
        w = self.word[wid]
        return f'{self.address(w[1])}.{w[2]}'


def verse_order(corpus, vid):
    return verse_sort_key(corpus.address(vid))


def form(w):
    """A word as the sheet shows it: an elided word (not printed, e.g. the article inside לַ) in brackets."""
    return f'[{w[3] or "elided"}]' if w[6] else w[3]


def sheet_blocks(text, corpus):
    """Each translation verse's block of the sheet, and what an answer about it may name."""
    per_verse = collections.defaultdict(list)
    for lid, (meta, f, t) in corpus.links.items():
        per_verse[corpus.word[f[0]][1]].append(lid)
    blocks = {}
    for vid, (book, abbr, chapter, number, slug) in corpus.verse.items():
        if slug != text.slug:
            continue
        target = text.target_of(book)
        own = corpus.words[vid]
        lids = sorted((l for l in per_verse.get(vid, []) if corpus.links[l][0][0] == target),
                      key=lambda l: min(corpus.word[x][2] for x in corpus.links[l][1]))
        framed = {o for o in corpus.frame.get(vid, ()) if corpus.verse[o][4] == target}
        reached = {corpus.word[x][1] for l in lids for x in corpus.links[l][2]}
        shown = sorted(framed | reached, key=lambda x: verse_order(corpus, x))
        here = corpus.address(vid)
        lines = [f'## {here}']
        lines.append('frame: ' + (', '.join(f'{target} {corpus.address(o)}' for o in
                                             sorted(framed, key=lambda x: verse_order(corpus, x)))
                                  if framed else f'no {target} verse (the store maps this verse to none)'))
        lines.append(f'{text.slug}: ' + ' '.join(f'{w[2]}:{form(w)}' for w in own))
        for o in shown:
            note = '' if o in framed else ' (reached by a link only)'
            lines.append(f'{target} {corpus.address(o)}{note}: ' + ' '.join(
                f'{w[2]}:{form(w)}' + (f'({w[4]}|{w[5]})' if w[4] or w[5] else '') for w in corpus.words[o]))
        if not shown:
            lines.append(f'{target}: (no verse)')
        single = len(shown) == 1
        linked, pairs = set(), []
        for l in lids:
            (_, method, confidence, relation), f, t = corpus.links[l]
            f = sorted(f, key=lambda x: corpus.word[x][2])
            t = sorted(t, key=lambda x: (verse_order(corpus, corpus.word[x][1]), corpus.word[x][2]))
            linked.update(corpus.word[x][2] for x in f)
            pairs += [[corpus.word_address(a), corpus.word_address(b)] for a in f for b in t]
            to = ','.join(str(corpus.word[x][2]) if single else corpus.word_address(x) for x in t)
            said = ' '.join(form(corpus.word[x]) for x in t) if t else '(stated: no counterpart)'
            lines.append(f"  {','.join(str(corpus.word[x][2]) for x in f)} => {to or '-'} "
                         f"[{method} {confidence} {relation}]  «{' '.join(form(corpus.word[x]) for x in f)}» = {said}")
        unlinked = [f'{w[2]}:{w[3]}' for w in own if w[2] not in linked]
        lines.append('  unlinked: ' + (' '.join(unlinked) if unlinked else '-'))
        blocks[here] = {
            'book': book, 'abbr': abbr, 'chapter': chapter, 'target': target,
            'text': '\n'.join(lines),
            'words': [f'{here}.{w[2]}' for w in own],
            'targetWords': [corpus.word_address(w[0]) for o in shown for w in corpus.words[o]],
            'pairs': pairs, 'links': len(lids),
        }
    return blocks


def chunk_id(book, abbr, chapter, first, chapter_first, taken):
    base = f'{book:02d}-{abbr.replace(" ", "")}-{chapter:03d}'
    if first != chapter_first:
        base += '-' + first.split(':', 1)[1]
    name, n = base, 2
    while name in taken:
        name, n = f'{base}-{n}', n + 1
    taken.add(name)
    return name


def plan(blocks, reviewed, taken, max_verses=MAX_VERSES):
    """The chunks: consecutive chapters of one book up to max_verses, a longer chapter split evenly."""
    chapters = collections.OrderedDict()
    all_by_chapter = collections.defaultdict(list)
    for address in sorted(blocks, key=verse_sort_key):
        b = blocks[address]
        all_by_chapter[(b['book'], b['chapter'])].append(address)
        if address not in reviewed:
            chapters.setdefault((b['book'], b['abbr'], b['chapter']), []).append(address)
    chunks, open_chunk = [], None

    def close():
        nonlocal open_chunk
        if open_chunk:
            chunks.append(open_chunk)
        open_chunk = None

    for (book, abbr, chapter), verses in chapters.items():
        first_of_chapter = all_by_chapter[(book, chapter)][0]
        if len(verses) > max_verses:
            close()
            parts = math.ceil(len(verses) / max_verses)
            size = math.ceil(len(verses) / parts)
            for i in range(0, len(verses), size):
                part = verses[i:i + size]
                chunks.append({'id': chunk_id(book, abbr, chapter, part[0], first_of_chapter, taken), 'verses': part})
            continue
        if open_chunk and (open_chunk['book'] != book or len(open_chunk['verses']) + len(verses) > max_verses):
            close()
        if not open_chunk:
            open_chunk = {'id': chunk_id(book, abbr, chapter, verses[0], first_of_chapter, taken),
                          'book': book, 'verses': []}
        open_chunk['verses'] += verses
    close()
    for c in chunks:
        c.pop('book', None)
    return chunks


def write_chunks(text, blocks, chunks, folder):
    index = []
    for c in chunks:
        bs = [blocks[v] for v in c['verses']]
        targets = sorted({b['target'] for b in bs})
        (folder / f"{c['id']}.txt").write_text('\n\n'.join(b['text'] for b in bs) + '\n', encoding='utf-8')
        check = {'id': c['id'], 'verses': c['verses'], 'targets': targets,
                 'words': [w for b in bs for w in b['words']],
                 'targetWords': sorted({w for b in bs for w in b['targetWords']}),
                 'pairs': [p for b in bs for p in b['pairs']]}
        (folder / f"{c['id']}.json").write_text(json.dumps(check, ensure_ascii=False), encoding='utf-8')
        index.append({'id': c['id'], 'verses': c['verses'], 'links': sum(b['links'] for b in bs), 'targets': targets})
    return index


def refresh(text, log=print, force=False):
    """Export again if the database moved, one exporter at a time (the service and the lead's conversion share it).
    Returns why it exported, or None."""
    text.run.mkdir(parents=True, exist_ok=True)
    with PidLock(text.run / 'export.lock'):
        why = 'forced' if force else stale(text)
        if why:
            log(f'export: {why}')
            export(text, log)
        return why


def export(text, log=print):
    """Read the text from the database again and plan the chunks; the old export stands until the new one is whole.
    Call it through refresh(), which holds the export lock."""
    text.run.mkdir(parents=True, exist_ok=True)
    started = time.time()
    before = fingerprint(text)
    fresh = text.run / 'export.new'
    shutil.rmtree(fresh, ignore_errors=True)
    fresh.mkdir()
    counts = {}
    for name, sql in queries(text).items():
        log(f'export: {name} from {DATABASE}')
        psql("SET statement_timeout = '15min';\n" + sql + '\n', out=fresh / f'{name}.tsv')
        counts[name] = sum(1 for _ in rows(fresh / f'{name}.tsv'))
    after = fingerprint(text)
    if after != before:
        shutil.rmtree(fresh, ignore_errors=True)
        raise RuntimeError(f'the database changed while it was read ({before} -> {after}); try again')
    log(f'export: {counts} in {time.time() - started:.0f} s; planning the chunks')
    corpus = Corpus(fresh, text.slug)
    blocks = sheet_blocks(text, corpus)
    del corpus
    reviewed = ResultTally(text).read()['reviewed']
    taken = {p.stem for p in text.results.glob('*.tsv')} if text.results.is_dir() else set()
    chunks = plan(blocks, reviewed, taken)
    chunk_folder = text.run / 'chunks.new'
    shutil.rmtree(chunk_folder, ignore_errors=True)
    chunk_folder.mkdir()
    index = write_chunks(text, blocks, chunks, chunk_folder)
    for old, new in ((text.export, fresh), (text.chunks, chunk_folder)):
        shutil.rmtree(old, ignore_errors=True)
        os.replace(new, old)
    atomic_write(text.run / 'chunks.json', json.dumps(index, ensure_ascii=False, indent=0))
    record = {'at': now(), 'fingerprint': after, 'rows': counts, 'verses': len(blocks),
              'reviewed': len(reviewed.keys() & blocks.keys()), 'chunks': len(index),
              'seconds': round(time.time() - started)}
    atomic_write(text.run / 'export.json', json.dumps(record, indent=1))
    log(f'export: {len(blocks)} verses, {record["reviewed"]} already reviewed, {len(index)} chunks to read '
        f'({record["seconds"]} s)')
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--text', required=True)
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    text = TextFolder(args.text)
    if args.check:
        why = stale(text)
        print(why or 'current')
        return 3 if why else 0
    if not refresh(text, force=args.force):
        print('export: current')
    return 0


if __name__ == '__main__':
    sys.exit(main())
