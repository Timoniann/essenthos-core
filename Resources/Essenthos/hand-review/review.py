"""A translation's word links to the originals reviewed by Codex, a chunk of verses at a time: the avioniq service
hand-review. The owner starts and stops it; it runs on his Codex quota.

On start it makes the export again if the database changed since the last one (export.py), then keeps `lanes`
chunks in flight, one `codex exec` each, in a read-only sandbox with the sheet in the prompt and the answer as JSON
(the schema below). Each answer is checked against the chunk (every verse once, refusals only of links on the sheet,
addresses that exist) and asked again once with what was wrong; a checked answer is written to results/<id>.tsv
whole, through a temporary file, so a run stopped at any moment leaves no half-written result. A chunk whose
verses all have a verdict is done.

Codex's usage limit or a rate limit is not a failure: the chunk goes back in the queue and the run waits (until the
time Codex names, or longer each time) and goes on. Other failures are retried three times per chunk; five in a row
pause the run for ten minutes. Every fifteen minutes it asks whether the database changed (a landing), and if so
lets the chunks in flight finish, exports again and goes on with the new links.

run/status.json says how far it got; the owner's console (Texts) shows it. Stopping it from avioniq ends the
process at once: the chunks in flight are abandoned and counted nowhere. A file run/STOP (review.py --finish)
lets the chunks in flight finish first.

Usage: review.py --text UBIO [--lanes 4] [--model gpt-6.2-sol] [--effort high] [--chunks N] | --finish | --status
"""
import argparse
import json
import os
import re
import subprocess
import sys
import threading
import time
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait
from datetime import datetime, timedelta, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
# Codex moves its CLI to a new folder on every update, so it is looked up for each call (guide calling-the-codex-cli).
sys.path.insert(0, str(next(p / 'scripts' for p in HERE.parents if (p / 'scripts' / 'codex_cli.py').is_file())))
from codex_cli import executable as codex_executable  # noqa: E402
import export as exporter  # noqa: E402
from common import (LOG_HEADER, RESULT_HEADER, PidLock, ResultTally, TextFolder, append_line,  # noqa: E402
                    atomic_write, converted_files, now)

MOST_LANES = 30
ATTEMPTS = 3
ERRORS_IN_A_ROW = 5
CALL_TIMEOUT = 45 * 60
DATABASE_CHECK = 15 * 60
HEARTBEAT = 30
LAUNCH_GAP = 2.0
LIMIT_WAITS = (15, 30, 60)
RATE_WAITS = (2, 5, 10, 20, 30)
ERRORS_WAIT = 10
LONGEST_WAIT = 6 * 60

ADDRESS = {'type': 'string'}
SCHEMA = {
    'type': 'object', 'additionalProperties': False, 'required': ['verses', 'refuse', 'accept', 'pattern'],
    'properties': {
        'verses': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False, 'required': ['verse', 'verdict', 'note'],
            'properties': {'verse': ADDRESS, 'verdict': {'type': 'string', 'enum': ['ok', 'fixed', 'unsure']},
                           'note': {'type': 'string'}}}},
        'refuse': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False, 'required': ['from', 'to', 'reason'],
            'properties': {'from': ADDRESS, 'to': ADDRESS, 'reason': {'type': 'string'}}}},
        'accept': {'type': 'array', 'items': {
            'type': 'object', 'additionalProperties': False, 'required': ['from', 'to', 'reason'],
            'properties': {'from': {'type': 'array', 'items': ADDRESS}, 'to': {'type': 'array', 'items': ADDRESS},
                           'reason': {'type': 'string'}}}},
        'pattern': {'type': 'string'}}}

USAGE_LIMIT = re.compile(r'usage limit|insufficient_quota|exceeded your current quota|quota exceeded|'
                         r'purchase more credits|out of credits', re.I)
RATE_LIMIT = re.compile(r'rate.?limit|too many requests|\b429\b', re.I)


def clean(text):
    return ' '.join(str(text).split())


def build_prompt(template, text, chunk, sheet):
    settings = text.settings
    notes = '\n'.join(f'- {n}' for n in settings['notes'])
    return (template.replace('{name}', settings['name']).replace('{slug}', text.slug)
            .replace('{language}', settings['language']).replace('{ot}', settings['targets']['ot'])
            .replace('{nt}', settings['targets']['nt']).replace('{notes}', notes)
            .replace('{verses}', ', '.join(chunk['verses'])).replace('{sheet}', sheet.strip()))


def validate(answer, check):
    """What is wrong with an answer, as sentences the reviewer can act on; [] when it may be written."""
    errors = []
    want = check['verses']
    got = [v['verse'] for v in answer['verses']]
    missing = [v for v in want if v not in got]
    extra = sorted({v for v in got if v not in want})
    twice = sorted({v for v in got if got.count(v) > 1})
    if missing:
        errors.append(f'verses missing: {", ".join(missing[:20])}')
    if extra:
        errors.append(f'verses that are not yours: {", ".join(extra[:20])}')
    if twice:
        errors.append(f'verses given more than once: {", ".join(twice[:20])}')
    words, targets = set(check['words']), set(check['targetWords'])
    pairs = {tuple(p) for p in check['pairs']}
    touched = set()
    for r in answer['refuse']:
        if (r['from'], r['to']) not in pairs:
            errors.append(f'refuse {r["from"]} -> {r["to"]}: no link on the sheet holds that pair; refuse only '
                          f'pairs of existing links, with full addresses on both sides')
        touched.add(r['from'].rsplit('.', 1)[0])
    for a in answer['accept']:
        if not a['from'] or not a['to']:
            errors.append('an accept needs words on both sides')
            continue
        for w in a['from']:
            if w not in words:
                errors.append(f'accept: {w} is not a word of your verses (full address book.chapter:verse.position)')
        for w in a['to']:
            if w not in targets:
                errors.append(f'accept: {w} is not a word of the original verses shown for your verses')
        touched.update(w.rsplit('.', 1)[0] for w in a['from'])
    for v in answer['verses']:
        if v['verdict'] == 'ok' and v['verse'] in touched:
            errors.append(f'{v["verse"]} has corrections, so it is "fixed" or "unsure", not "ok"')
    return errors


def rows(answer):
    """The answer in the results format: kind, from, to, relation, reason."""
    out = [RESULT_HEADER]
    for v in answer['verses']:
        out.append('\t'.join((v['verdict'], v['verse'], '', '', clean(v['note']))))
    seen = set()
    for r in answer['refuse']:
        if (r['from'], r['to']) not in seen:
            seen.add((r['from'], r['to']))
            out.append('\t'.join(('refuse', r['from'], r['to'], '', clean(r['reason']))))
    for a in answer['accept']:
        out.append('\t'.join(('accept', ','.join(a['from']), ','.join(a['to']), 'renders', clean(a['reason']))))
    return '\n'.join(out) + '\n'


def wait_named(said, at=None):
    """The wait Codex names in its message ("try again in 2 hours 5 minutes", "try again at 3:05 PM"), in
    seconds, or None."""
    at = at or datetime.now().astimezone()
    m = re.search(r'try again in ((?:\d+\s*(?:days?|d|hours?|h|minutes?|mins?|m|seconds?|secs?|s)[\s,]*(?:and\s+)?)+)',
                  said, re.I)
    if m:
        seconds = 0
        for n, unit in re.findall(r'(\d+)\s*(days?|d|hours?|h|minutes?|mins?|m|seconds?|secs?|s)\b', m.group(1), re.I):
            u = unit.lower()[0]
            seconds += int(n) * {'d': 86400, 'h': 3600, 'm': 60, 's': 1}[u]
        return seconds or None
    m = re.search(r'try again at (\d{1,2}):(\d{2})\s*([AaPp][Mm])?', said)
    if m:
        hour, minute = int(m.group(1)), int(m.group(2))
        if m.group(3):
            hour = hour % 12 + (12 if m.group(3).lower() == 'pm' else 0)
        then = at.replace(hour=hour, minute=minute, second=0, microsecond=0)
        if then <= at:
            then += timedelta(days=1)
        return int((then - at).total_seconds())
    return None


def classify(said):
    """'limit', 'rate' or None for what a failed call said."""
    if USAGE_LIMIT.search(said):
        return 'limit'
    if RATE_LIMIT.search(said):
        return 'rate'
    return None


def kill_tree(process):
    if os.name == 'nt':
        subprocess.run(['taskkill', '/pid', str(process.pid), '/T', '/F'], capture_output=True)
    else:
        process.kill()


class Outcome:
    def __init__(self, chunk, kind, message='', answer=None, seconds=0, attempts=1, said=''):
        self.chunk, self.kind, self.message, self.answer = chunk, kind, message, answer
        self.seconds, self.attempts, self.said = seconds, attempts, said


class Review:
    def __init__(self, text, lanes, model, effort, most_chunks=None):
        self.text, self.lanes, self.model, self.effort = text, lanes, model, effort
        self.most_chunks = most_chunks
        self.template = (HERE / 'prompt.txt').read_text(encoding='utf-8')
        self.schema = text.run / 'schema.json'
        self.tally = ResultTally(text)
        self.started = now()
        self.state, self.note = 'starting', None
        self.queue, self.chunks = [], {}
        self.running = {}
        self.attempts, self.failed = {}, {}
        self.errors = []
        self.waiting = None
        self.limit_strikes = self.rate_strikes = self.errors_in_a_row = 0
        self.last = None
        self.done_here = 0
        self.refresh_wanted = False
        self.next_check = time.time() + DATABASE_CHECK
        self.sheets = None
        self.last_launch = 0.0
        self._status_lock = threading.Lock()

    # ---- status -------------------------------------------------------------------------------------------

    def error(self, kind, message, chunk=None):
        self.errors.append({'at': now(), 'kind': kind, 'chunk': chunk, 'message': clean(message)[:400]})
        del self.errors[:-20]
        print(f'{kind}: {chunk or ""} {clean(message)[:300]}', flush=True)

    def status(self):
        tally = self.tally.read()
        reviewed = tally['reviewed']
        verdicts = list(reviewed.values())
        record = exporter.current(self.text) or {}
        total_verses = record.get('verses') or 0
        remaining = [c for c in self.queue if not self.is_done(c, reviewed)]
        converted = converted_files(self.text)
        body = {
            'text': self.text.slug, 'title': self.text.settings.get('title', self.text.name),
            'service': 'hand-review', 'state': self.state, 'pid': os.getpid(),
            'started': self.started, 'updated': now(),
            'model': self.model, 'effort': self.effort, 'lanes': self.lanes,
            'chunks': {'total': tally['files'] + len(remaining) + len(self.running) + len(self.failed),
                       'done': tally['files'], 'failed': len(self.failed), 'running': len(self.running),
                       'doneHere': self.done_here},
            'verses': {'total': total_verses, 'reviewed': len(verdicts), 'ok': verdicts.count('ok'),
                       'fixed': verdicts.count('fixed'), 'unsure': verdicts.count('unsure')},
            'rows': {'refuse': tally['counts']['refuse'], 'accept': tally['counts']['accept']},
            'converted': {'chunks': len(converted), 'waiting': max(tally['files'] - len(converted), 0)},
            'last': self.last, 'waiting': self.waiting, 'errors': self.errors[-20:],
            'sheets': {'exported': record.get('at'), 'database': (record.get('fingerprint') or {}).get('database')}
            if record else None,
            'note': self.note,
        }
        with self._status_lock:
            atomic_write(self.text.status, json.dumps(body, ensure_ascii=False, indent=1))

    # ---- chunks -------------------------------------------------------------------------------------------

    def load_chunks(self):
        index = json.loads((self.text.run / 'chunks.json').read_text(encoding='utf-8'))
        self.chunks = {c['id']: c for c in index}
        self.queue = [c['id'] for c in index]
        self.failed.clear()
        self.attempts.clear()

    def is_done(self, chunk_id, reviewed=None):
        if (self.text.results / f'{chunk_id}.tsv').exists():
            return True
        reviewed = self.tally.read()['reviewed'] if reviewed is None else reviewed
        return all(v in reviewed for v in self.chunks[chunk_id]['verses'])

    def next_chunk(self):
        while self.queue:
            chunk_id = self.queue.pop(0)
            if chunk_id in self.running or chunk_id in self.failed or self.is_done(chunk_id):
                continue
            return chunk_id
        return None

    # ---- one chunk (runs on a lane's thread; writes only its own files) ------------------------------------

    def call(self, prompt, chunk_id):
        out = self.text.work / f'{chunk_id}.answer.json'
        log = self.text.run / 'logs' / f'{chunk_id}.log'
        log.parent.mkdir(parents=True, exist_ok=True)
        if out.exists():
            out.unlink()
        try:
            codex = codex_executable()
        except RuntimeError as problem:
            return None, str(problem)
        command = [codex, 'exec', '--skip-git-repo-check', '--ephemeral', '--sandbox', 'read-only', '-m', self.model,
                   '-c', f'model_reasoning_effort="{self.effort}"', '--output-schema', str(self.schema),
                   '-o', str(out), '-C', str(self.text.work)]
        with open(log, 'w', encoding='utf-8', errors='replace') as sink:
            process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=sink, stderr=subprocess.STDOUT,
                                       text=True, encoding='utf-8', errors='replace')
            try:
                process.stdin.write(prompt)
                process.stdin.close()
                code = process.wait(timeout=CALL_TIMEOUT)
            except subprocess.TimeoutExpired:
                kill_tree(process)
                return None, f'no answer in {CALL_TIMEOUT // 60} minutes'
            except OSError as problem:
                kill_tree(process)
                return None, f'codex could not be fed the prompt: {problem}'
        said = log.read_text(encoding='utf-8', errors='replace')[-4000:]
        if code or not out.exists():
            return None, f'codex exit {code}: ' + said[-1500:]
        try:
            answer = json.loads(out.read_text(encoding='utf-8'))
        except json.JSONDecodeError as problem:
            return None, f'the answer is no JSON: {problem}'
        finally:
            out.unlink(missing_ok=True)
        return answer, ''

    def review(self, chunk_id):
        chunk = self.chunks[chunk_id]
        started = time.time()
        try:
            sheet = (self.text.chunks / f'{chunk_id}.txt').read_text(encoding='utf-8')
            check = json.loads((self.text.chunks / f'{chunk_id}.json').read_text(encoding='utf-8'))
        except OSError as problem:
            return Outcome(chunk_id, 'error', f'the chunk files are missing ({problem}); the export is redone')
        prompt = build_prompt(self.template, self.text, chunk, sheet)
        extra = ''
        for attempt in (1, 2):
            answer, said = self.call(prompt + extra, chunk_id)
            if answer is None:
                kind = classify(said)
                return Outcome(chunk_id, kind or 'error', said[-600:], seconds=time.time() - started,
                               attempts=attempt, said=said)
            problems = validate(answer, check)
            if not problems:
                break
            extra = ('\n\nYour previous answer was refused by the checker: ' + '; '.join(problems[:15])
                     + '. Answer again, the whole answer.')
        else:
            return Outcome(chunk_id, 'error', 'the answer failed the check twice: ' + '; '.join(problems[:6]),
                           seconds=time.time() - started, attempts=2)
        seconds = time.time() - started
        name = f'{chunk_id}.tsv'
        # The log line first: a run killed between the two leaves a line that names no file, never a file nobody logged.
        append_line(self.text.log, LOG_HEADER, '\t'.join(
            (chunk_id, name, self.model, self.effort, now(), str(round(seconds)), str(attempt))))
        atomic_write(self.text.results / name, rows(answer))
        return Outcome(chunk_id, 'ok', answer.get('pattern', ''), answer=answer, seconds=seconds, attempts=attempt)

    # ---- the coordinator (main thread; the only writer of status.json) -------------------------------------

    def settle(self, outcome):
        chunk_id = outcome.chunk
        self.running.pop(chunk_id, None)
        if outcome.kind == 'ok':
            answer = outcome.answer
            verdicts = [v['verdict'] for v in answer['verses']]
            self.last = {'chunk': chunk_id, 'at': now(), 'seconds': round(outcome.seconds),
                         'verses': len(verdicts), 'ok': verdicts.count('ok'), 'fixed': verdicts.count('fixed'),
                         'unsure': verdicts.count('unsure'), 'refuse': len(answer['refuse']),
                         'accept': len(answer['accept']), 'pattern': clean(outcome.message)[:300]}
            self.done_here += 1
            self.errors_in_a_row = self.limit_strikes = self.rate_strikes = 0
            print(f'{chunk_id} ok in {outcome.seconds:.0f} s: {self.last["ok"]} ok, {self.last["fixed"]} fixed, '
                  f'{self.last["unsure"]} unsure, {self.last["refuse"]} refuse, {self.last["accept"]} accept', flush=True)
            return
        if outcome.kind in ('limit', 'rate'):
            self.queue.insert(0, chunk_id)
            if self.waiting and self.waiting['kind'] == outcome.kind:
                return  # the other lanes meeting the same limit are one strike, not one each
            named = wait_named(outcome.said)
            if outcome.kind == 'limit':
                minutes = LIMIT_WAITS[min(self.limit_strikes, len(LIMIT_WAITS) - 1)]
                self.limit_strikes += 1
                seconds = (named + 120) if named else minutes * 60
            else:
                minutes = RATE_WAITS[min(self.rate_strikes, len(RATE_WAITS) - 1)]
                self.rate_strikes += 1
                seconds = max(named or 0, minutes * 60)
            self.pause(outcome.kind, min(seconds, LONGEST_WAIT * 60), outcome.message)
            return
        self.error('error', outcome.message, chunk_id)
        self.attempts[chunk_id] = self.attempts.get(chunk_id, 0) + 1
        if self.attempts[chunk_id] >= ATTEMPTS:
            self.failed[chunk_id] = outcome.message[:300]
        else:
            self.queue.append(chunk_id)
        self.errors_in_a_row += 1
        if self.errors_in_a_row >= ERRORS_IN_A_ROW:
            self.errors_in_a_row = 0
            self.pause('errors', ERRORS_WAIT * 60, f'{ERRORS_IN_A_ROW} chunks failed in a row: {outcome.message}')

    def pause(self, kind, seconds, message):
        until = datetime.now(timezone.utc) + timedelta(seconds=seconds)
        if self.waiting and self.waiting['until'] >= until.isoformat(timespec='seconds'):
            return
        self.waiting = {'kind': kind, 'since': now(), 'until': until.isoformat(timespec='seconds'),
                        'message': clean(message)[-400:]}
        self.error(kind, f'waiting {seconds // 60} min: {message}')

    def check_database(self):
        self.next_check = time.time() + DATABASE_CHECK
        try:
            why = exporter.stale(self.text)
        except (RuntimeError, OSError, subprocess.TimeoutExpired, ValueError) as problem:
            self.error('database', f'could not ask whether the database changed; going on with the sheets as they '
                                   f'are: {problem}')
            return
        if why:
            print(f'the database changed: {why}; exporting again once the chunks in flight are done', flush=True)
            self.refresh_wanted = True

    def export(self):
        self.state = 'exporting'
        self.status()
        try:
            exporter.refresh(self.text, log=lambda m: print(m, flush=True))
        except (RuntimeError, OSError, subprocess.TimeoutExpired, ValueError) as problem:
            if (self.text.run / 'chunks.json').exists() and exporter.current(self.text):
                self.error('database', f'the export failed; going on with the sheets as they are: {problem}')
            else:
                self.pause('database', 10 * 60, f'the export failed and there are no sheets yet: {problem}')
                return False
        record = exporter.current(self.text)
        if record:
            self.load_chunks()
        self.refresh_wanted = False
        self.next_check = time.time() + DATABASE_CHECK
        return bool(record)

    def run(self):
        self.text.work.mkdir(parents=True, exist_ok=True)
        for leftover in self.text.work.glob('*'):
            leftover.unlink(missing_ok=True)
        for leftover in self.text.results.glob('*.tmp') if self.text.results.is_dir() else []:
            leftover.unlink(missing_ok=True)
        self.text.stop.unlink(missing_ok=True)
        atomic_write(self.schema, json.dumps(SCHEMA))
        print(f'{self.text.slug}: {self.lanes} lane(s), {self.model} ({self.effort})', flush=True)
        ready = self.export()
        pool = ThreadPoolExecutor(max_workers=self.lanes)
        futures = {}
        last_beat = 0.0
        stopping = False
        try:
            while True:
                if self.text.stop.exists() and not stopping:
                    stopping = True
                    self.note = 'Finishing the chunks in flight, then stopping (run/STOP).'
                if self.waiting and datetime.now(timezone.utc).isoformat(timespec='seconds') >= self.waiting['until']:
                    self.waiting = None
                if not ready and not self.waiting:
                    ready = self.export()
                if ready and not stopping and time.time() >= self.next_check and not self.refresh_wanted:
                    self.check_database()
                if self.refresh_wanted and not futures and not stopping:
                    ready = self.export()
                enough = self.most_chunks is not None and self.done_here + len(futures) >= self.most_chunks
                while (ready and not stopping and not self.waiting and not self.refresh_wanted and not enough
                       and len(futures) < self.lanes):
                    gap = LAUNCH_GAP - (time.time() - self.last_launch)
                    if gap > 0:
                        time.sleep(gap)
                    chunk_id = self.next_chunk()
                    if not chunk_id:
                        break
                    self.running[chunk_id] = now()
                    futures[pool.submit(self.review, chunk_id)] = chunk_id
                    self.last_launch = time.time()
                    enough = self.most_chunks is not None and self.done_here + len(futures) >= self.most_chunks
                if not futures:
                    if stopping:
                        self.state, self.note = 'stopped', 'Stopped after the chunks in flight (run/STOP).'
                        break
                    if self.most_chunks is not None and self.done_here >= self.most_chunks:
                        self.state, self.note = 'stopped', f'Stopped after {self.done_here} chunk(s), as asked.'
                        break
                    if ready and not self.waiting and not self.refresh_wanted and not self.next_chunk_left():
                        self.state = 'done' if not self.failed else 'failed'
                        self.note = (None if not self.failed else
                                     f'{len(self.failed)} chunk(s) failed three times; start it again to retry them.')
                        break
                self.state = 'waiting' if self.waiting and not futures else 'running'
                if time.time() - last_beat >= HEARTBEAT:
                    self.status()
                    last_beat = time.time()
                if futures:
                    finished, _ = wait(list(futures), timeout=5, return_when=FIRST_COMPLETED)
                    for future in finished:
                        chunk_id = futures.pop(future)
                        try:
                            outcome = future.result()
                        except Exception as problem:  # a lane's bug must not end the run; the chunk is retried
                            outcome = Outcome(chunk_id, 'error', f'{type(problem).__name__}: {problem}')
                        self.settle(outcome)
                    if finished:
                        self.status()
                        last_beat = time.time()
                else:
                    time.sleep(5)
        finally:
            pool.shutdown(wait=False, cancel_futures=True)
            self.status()
        print(f'{self.state}: {self.done_here} chunk(s) this run', flush=True)
        return 0 if self.state in ('done', 'stopped') else 1

    def next_chunk_left(self):
        reviewed = self.tally.read()['reviewed']
        return any(c not in self.failed and c not in self.running and not self.is_done(c, reviewed)
                   for c in self.queue)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--text')
    parser.add_argument('--lanes', type=int, default=4)
    parser.add_argument('--model', default='gpt-6.2-sol')
    parser.add_argument('--effort', default='high', choices=['low', 'medium', 'high', 'xhigh', 'max'])
    parser.add_argument('--chunks', type=int, help='stop after this many chunks (a trial)')
    parser.add_argument('--finish', action='store_true', help='let the running review finish its chunks in flight')
    parser.add_argument('--status', action='store_true', help='print the status file')
    args = parser.parse_args()
    if not args.text:
        parser.error('--text is required (the slug of the translation, e.g. UBIO)')
    text = TextFolder(args.text)
    if args.finish:
        text.run.mkdir(parents=True, exist_ok=True)
        text.stop.write_text(now(), encoding='utf-8')
        print('asked the running review to finish its chunks in flight and stop')
        return 0
    if args.status:
        print(text.status.read_text(encoding='utf-8') if text.status.exists() else 'no status yet')
        return 0
    if not 1 <= args.lanes <= MOST_LANES:
        parser.error(f'--lanes is 1 to {MOST_LANES}')
    if not re.fullmatch(r'[A-Za-z0-9._-]+', args.model):
        parser.error('--model is a model name such as gpt-6.2-sol')
    lock = PidLock(text.run / 'review.lock')
    if not lock.acquire():
        print(f'a review of {text.slug} is already running (pid {lock.holder()}); not starting a second one',
              flush=True)
        return 1
    try:
        return Review(text, args.lanes, args.model, args.effort, args.chunks).run()
    finally:
        lock.release()


if __name__ == '__main__':
    sys.exit(main())
