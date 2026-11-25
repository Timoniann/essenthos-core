"""The lead's step: results not yet converted become rulings rows of the text's pairs, with a report.

Only the lead commits rulings, so this is never run by the review itself. It first makes the export current (the
database read again if it moved), then for every results file not in converted.tsv:
  refuse  the pair must be a link of the database as it is now; that link's whole shape is refused, and where it
          held other words too, the rest of it is accepted. A refusal whose link is gone is dropped and reported.
  accept  every word must exist now; the shape is pinned. One over a word an earlier accept already pins, a
          duplicate, or one contradicting an existing ruling is dropped and reported.
Without --apply it writes nothing but run/conversions/<time>/ (the report and the rows it would add). With --apply
it appends the rows to mapping/<TEXT>-<original>/rulings.tsv and the files to converted.tsv; then commit both with
the results.

Usage: convert.py --text UBIO [--apply]
"""
import argparse
import collections
import hashlib
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import export as exporter  # noqa: E402
from common import (CONVERTED_HEADER, ResultTally, TextFolder, append_line, book_of, converted_files,  # noqa: E402
                    read_log, read_result, result_files, word_sort_key)


def shape(words):
    return ' '.join(sorted(words, key=word_sort_key))


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def existing_rulings(path):
    rows = []
    if not path.exists():
        return rows
    for line in path.read_text(encoding='utf-8').splitlines():
        if line.startswith('#') or line.startswith('action\t') or not line.strip():
            continue
        cells = line.split('\t')
        if len(cells) >= 3:
            rows.append(cells)
    return rows


class Links:
    """The text's links to its originals as the export holds them, by word address."""

    def __init__(self, corpus, text):
        self.pairs = collections.defaultdict(list)
        self.shapes = {}
        for lid, (meta, f, t) in corpus.links.items():
            fa = {corpus.word_address(x) for x in f}
            ta = {corpus.word_address(x) for x in t}
            self.shapes[lid] = (fa, ta)
            for a in fa:
                for b in ta:
                    self.pairs[(a, b)].append(lid)
        self.words = {slug: set() for slug in [text.slug, *text.targets]}
        for vid, ws in corpus.words.items():
            slug = corpus.verse[vid][4]
            address = corpus.address(vid)
            self.words[slug].update(f'{address}.{w[2]}' for w in ws)
        self.verses = sum(1 for v in corpus.verse.values() if v[4] == text.slug)
        self.nt_verses = sum(1 for v in corpus.verse.values() if v[4] == text.slug and v[0] >= 40)


def who(log_row, date):
    if not log_row:
        return f'hand review {date}'
    return f'Codex review {log_row["at"][:10]} ({log_row["model"]} {log_row["effort"]})'


def convert(text, links, files, log, today):
    """The rulings rows the files give, per pair, and what was dropped and why."""
    new = {target: [] for target in text.targets}
    seen, pinned_from = set(), set()
    for target in text.targets:
        for cells in existing_rulings(text.rulings(target)):
            seen.add((cells[0], cells[1], cells[2]))
            if cells[0] == 'accept':
                pinned_from.update(cells[1].split())
    dropped, stale = collections.Counter(), []
    per_file = {}
    verdicts = collections.Counter()
    for path in files:
        results = read_result(path)
        prefix = f'{who(log.get(path.name), today)}, {text.settings.get("pass", text.slug + " pass")}: '
        refused = {(r[1], r[2]) for r in results if r[0] == 'refuse'}
        counts = collections.Counter()
        for kind, frm, to, relation, reason, *_ in results:
            if kind in ('ok', 'fixed', 'unsure'):
                verdicts[kind] += 1
                continue
            if kind not in ('refuse', 'accept'):
                dropped['unknown kind'] += 1
                continue
            first = frm.replace(',', ' ').split()[0] if frm.strip() else ''
            if not first:
                dropped['no words'] += 1
                continue
            target = text.target_of(book_of(first))
            rows = new[target]
            if kind == 'refuse':
                lids = links.pairs.get((frm, to))
                if not lids:
                    dropped['refuse: no such link now'] += 1
                    stale.append(f'refuse\t{frm}\t{to}\t{reason}\t({path.name}) no link of the database holds it now')
                    continue
                for lid in lids:
                    f, t = links.shapes[lid]
                    row = ('refuse', shape(f), shape(t))
                    if row in seen or ('accept', row[1], row[2]) in seen:
                        dropped['refuse: duplicate or contradicting a ruling'] += 1
                        continue
                    seen.add(row)
                    rows.append([*row, '', '', prefix + reason])
                    counts['refuse'] += 1
                    if len(f) > 1 or len(t) > 1:
                        bad = {(a, b) for a in f for b in t if (a, b) in refused}
                        f2 = {a for a in f if not all((a, b) in bad for b in t)}
                        t2 = {b for b in t if not all((a, b) in bad for a in f)}
                        if f2 and t2:
                            rest = ('accept', shape(f2), shape(t2))
                            if rest not in seen:
                                seen.add(rest)
                                rows.append([*rest, 'renders', '', prefix + reason + ' (the rest of a partly wrong link)'])
                                counts['accept'] += 1
            else:
                f = frm.replace(',', ' ').split()
                t = to.replace(',', ' ').split()
                gone = [w for w in f if w not in links.words[text.slug]] + [w for w in t if w not in links.words[target]]
                if not t or gone:
                    dropped['accept: a word is not in the database now'] += 1
                    stale.append(f'accept\t{frm}\t{to}\t{reason}\t({path.name}) missing now: {" ".join(gone) or "the original side"}')
                    continue
                if any(w in pinned_from for w in f):
                    dropped['accept: over a word an earlier accept pins'] += 1
                    continue
                row = ('accept', shape(f), shape(t))
                if row in seen or ('refuse', row[1], row[2]) in seen:
                    dropped['accept: duplicate or contradicting a ruling'] += 1
                    continue
                seen.add(row)
                rows.append([*row, 'renders', '', prefix + reason])
                counts['accept'] += 1
        per_file[path.name] = counts
    return new, dropped, stale, per_file, verdicts


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--text', required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    text = TextFolder(args.text)
    exporter.refresh(text)
    done = converted_files(text)
    files = [p for p in result_files(text) if done.get(p.name) != sha256(p)]
    changed = [p.name for p in files if p.name in done]
    stamp = datetime.now(timezone.utc)
    today = stamp.date().isoformat()
    links = Links(exporter.Corpus(text.export, text.slug), text)
    new, dropped, stale, per_file, verdicts = convert(text, links, files, read_log(text), today)

    tally = ResultTally(text).read()
    reviewed = tally['reviewed']
    nt = sum(1 for v in reviewed if book_of(v) >= 40)
    lines = [f'{text.slug} conversion {stamp.isoformat(timespec="seconds")}' + ('' if args.apply else ' (dry run)'),
             f'export: {exporter.current(text).get("at")}, database {exporter.current(text)["fingerprint"]}',
             f'results files: {len(files)} to convert' + (f' ({len(changed)} changed since converted: '
                                                         f'{", ".join(changed[:10])})' if changed else ''),
             f'verses in them: ' + ', '.join(f'{k} {verdicts[k]}' for k in ('ok', 'fixed', 'unsure')),
             f'coverage: {len(reviewed)} of {links.verses} verses reviewed ({100 * len(reviewed) / max(links.verses, 1):.1f}%); '
             f'Old Testament {len(reviewed) - nt} of {links.verses - links.nt_verses}, New {nt} of {links.nt_verses}']
    for target, rows in new.items():
        kinds = collections.Counter(r[0] for r in rows)
        lines.append(f'{text.pair(target)}: {kinds["refuse"]} refuse, {kinds["accept"]} accept')
    lines.append('dropped: ' + (', '.join(f'{k} {v}' for k, v in sorted(dropped.items())) or 'none'))
    if stale:
        lines.append(f'stale ({len(stale)}; all in stale.tsv):')
        lines += ['  ' + s for s in stale[:30]]
    report = '\n'.join(lines)

    out = text.run / 'conversions' / stamp.strftime('%Y%m%dT%H%M%SZ')
    out.mkdir(parents=True, exist_ok=True)
    (out / 'report.txt').write_text(report + '\n', encoding='utf-8')
    (out / 'stale.tsv').write_text('kind\tfrom\tto\treason\twhy\n' + ''.join(s + '\n' for s in stale), encoding='utf-8')
    for target, rows in new.items():
        (out / f'{text.pair(target)}.tsv').write_text(''.join('\t'.join(r) + '\n' for r in rows), encoding='utf-8')
    if args.apply:
        for target, rows in new.items():
            if not rows:
                continue
            path = text.rulings(target)
            if not path.exists():
                raise SystemExit(f'{path} does not exist; a pair gets its rulings file with its other hand files first.')
            body = path.read_bytes()
            with open(path, 'a', encoding='utf-8', newline='\n') as handle:
                if body and not body.endswith(b'\n'):
                    handle.write('\n')
                handle.writelines('\t'.join(r) + '\n' for r in rows)
        for p in files:
            c = per_file.get(p.name, {})
            append_line(text.converted, CONVERTED_HEADER, '\t'.join(
                (p.name, sha256(p), stamp.isoformat(timespec='seconds'), str(c.get('refuse', 0)), str(c.get('accept', 0)))))
    print(report)
    print(f'written: {out}' + ('' if args.apply else ' (nothing else; --apply appends the rows to the rulings)'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
