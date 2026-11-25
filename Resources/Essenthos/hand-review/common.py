"""What the hand-review scripts share: where a text's files are, its settings, addresses, the results on disk.

A text under review has a folder of its own here, named by its slug (UBIO/, RUSV/):
  text.json         what the prompt says about the translation, and which original each testament is read against
  results/<id>.tsv  the verdicts of one chunk (kind, from, to, relation, reason), written whole or not at all
  results/log.tsv   one line per chunk written: who read it, when, how long
  converted.tsv     the results files the lead has turned into rulings already
  run/              the export, the chunks, the status and the logs; rewritten by every run, not in git
"""
import json
import os
import re
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
STORE = HERE.parent
VERDICTS = ('ok', 'fixed', 'unsure')
CORRECTIONS = ('refuse', 'accept')
RESULT_HEADER = 'kind\tfrom\tto\trelation\treason'
LOG_HEADER = 'chunk\tfile\tmodel\teffort\tat\tseconds\tattempts'
CONVERTED_HEADER = 'file\tsha256\tconverted\trefuse\taccept'

_append_lock = threading.Lock()


def now():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


class TextFolder:
    """One translation's review: its settings and the paths of everything it writes."""

    def __init__(self, slug, root=HERE, store=STORE):
        if not re.fullmatch(r'[A-Z0-9][A-Z0-9_-]*', slug or ''):
            raise SystemExit(f'"{slug}" is not a text slug (upper case, as the corpus names it: UBIO, RUSV).')
        self.slug = slug
        self.store = store
        self.folder = root / slug
        settings = self.folder / 'text.json'
        if not settings.is_file():
            known = sorted(p.parent.name for p in root.glob('*/text.json'))
            raise SystemExit(f'{slug} has no {settings.relative_to(root.parent)}; texts set up for review: '
                             f'{", ".join(known) or "none"}. Write one like UBIO/text.json to review another.')
        self.settings = json.loads(settings.read_text(encoding='utf-8'))
        for key in ('name', 'language', 'targets', 'notes'):
            if key not in self.settings:
                raise SystemExit(f'{settings} lacks "{key}".')
        self.results = self.folder / 'results'
        self.log = self.results / 'log.tsv'
        self.converted = self.folder / 'converted.tsv'
        self.run = self.folder / 'run'
        self.export = self.run / 'export'
        self.chunks = self.run / 'chunks'
        self.work = self.run / 'work'
        self.status = self.run / 'status.json'
        self.stop = self.run / 'STOP'

    @property
    def name(self):
        return self.settings['name']

    def target_of(self, book):
        """The original a verse of this canonical book is read against."""
        return self.settings['targets']['nt' if book >= 40 else 'ot']

    @property
    def targets(self):
        return sorted(set(self.settings['targets'].values()))

    def pair(self, target):
        return f'{self.slug}-{target}'

    def rulings(self, target):
        return self.store / 'mapping' / self.pair(target) / 'rulings.tsv'


def pid_alive(pid):
    """Whether a process is running. Never os.kill(pid, 0): on Windows that terminates the process."""
    if not pid or pid <= 0:
        return False
    if os.name == 'nt':
        import ctypes
        kernel = ctypes.windll.kernel32
        handle = kernel.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        code = ctypes.c_ulong()
        try:
            return bool(kernel.GetExitCodeProcess(handle, ctypes.byref(code))) and code.value == 259  # STILL_ACTIVE
        finally:
            kernel.CloseHandle(handle)
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


class PidLock:
    """A lock file holding the owner's pid; a lock whose process is gone is taken over."""

    def __init__(self, path):
        self.path = path

    def holder(self):
        try:
            pid = int(self.path.read_text(encoding='utf-8').strip() or 0)
        except (OSError, ValueError):
            return None
        return pid if pid != os.getpid() and pid_alive(pid) else None

    def acquire(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        for _ in range(2):
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(fd, str(os.getpid()).encode())
                os.close(fd)
                return True
            except FileExistsError:
                if self.holder():
                    return False
                try:
                    self.path.unlink()
                except OSError:
                    return False
        return False

    def release(self):
        try:
            if self.path.read_text(encoding='utf-8').strip() == str(os.getpid()):
                self.path.unlink()
        except OSError:
            pass

    def __enter__(self):
        while not self.acquire():
            time.sleep(5)
        return self

    def __exit__(self, *exc):
        self.release()


def atomic_write(path, text):
    """Write whole or not at all: a reader, or a run killed mid-write, never sees half a file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + f'.{os.getpid()}.{threading.get_ident()}.tmp')
    tmp.write_text(text, encoding='utf-8', newline='\n')
    for _ in range(50):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            # On Windows a reader holding the file open (the console polling status.json) refuses the replace.
            time.sleep(0.1)
    os.replace(tmp, path)


def append_line(path, header, line):
    with _append_lock:
        path.parent.mkdir(parents=True, exist_ok=True)
        new = not path.exists()
        with open(path, 'a', encoding='utf-8', newline='\n') as handle:
            if new:
                handle.write(header + '\n')
            handle.write(line + '\n')


def book_of(address):
    return int(address.split('.', 1)[0])


def verse_of(word_address):
    """1.2:3.4 (book.chapter:verse.position) -> 1.2:3."""
    return word_address.rsplit('.', 1)[0]


def verse_sort_key(address):
    """Canonical order of a verse address book.chapter:verse, with a lettered verse after its number."""
    book, rest = address.split('.', 1)
    chapter, verse = rest.split(':', 1)
    digits = ''.join(ch for ch in verse if ch.isdigit())
    return int(book), int(chapter), int(digits or 0), verse


def word_sort_key(address):
    verse, position = address.rsplit('.', 1)
    return (*verse_sort_key(verse), int(position))


def read_result(path):
    """The rows of one results file, header and blank lines left out."""
    rows = []
    for line in path.read_text(encoding='utf-8').splitlines():
        cells = line.split('\t')
        if not line.strip() or cells[0] == 'kind':
            continue
        rows.append(cells + [''] * (5 - len(cells)))
    return rows


def result_files(text):
    return sorted(p for p in text.results.glob('*.tsv') if p.name != 'log.tsv') if text.results.is_dir() else []


class ResultTally:
    """What the results on disk add up to, reading again only the files that changed since the last count."""

    def __init__(self, text):
        self.text = text
        self._seen = {}

    def _count(self, path):
        stamp = path.stat().st_mtime_ns
        known = self._seen.get(path.name)
        if known and known[0] == stamp:
            return known[1]
        verdicts, counts = {}, {k: 0 for k in VERDICTS + CORRECTIONS}
        for row in read_result(path):
            if row[0] in counts:
                counts[row[0]] += 1
            if row[0] in VERDICTS:
                verdicts[row[1]] = row[0]
        entry = (stamp, (verdicts, counts))
        self._seen[path.name] = entry
        return entry[1]

    def read(self):
        files = result_files(self.text)
        names = {p.name for p in files}
        for gone in set(self._seen) - names:
            del self._seen[gone]
        reviewed, totals = {}, {k: 0 for k in VERDICTS + CORRECTIONS}
        for path in files:
            verdicts, counts = self._count(path)
            reviewed.update(verdicts)
            for key, value in counts.items():
                totals[key] += value
        return {'files': len(files), 'reviewed': reviewed, 'counts': totals}


def converted_files(text):
    """file name -> sha256 of every results file already turned into rulings."""
    if not text.converted.exists():
        return {}
    out = {}
    for line in text.converted.read_text(encoding='utf-8').splitlines()[1:]:
        cells = line.split('\t')
        if len(cells) >= 2:
            out[cells[0]] = cells[1]
    return out


def read_log(text):
    """chunk file name -> its last log line as a dict."""
    if not text.log.exists():
        return {}
    lines = text.log.read_text(encoding='utf-8').splitlines()
    header = lines[0].split('\t') if lines else []
    out = {}
    for line in lines[1:]:
        cells = line.split('\t')
        if len(cells) == len(header):
            row = dict(zip(header, cells))
            out[row['file']] = row
    return out
