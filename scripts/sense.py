"""
Which of the men called Zechariah does this word mean? Ask a model, and measure how often it is right.

515 proper-noun Strong numbers answer with more than one person or place, over 9,996 Hebrew
occurrences, and nothing in the corpus says which referent an occurrence carries: BHSA's name type
is a property of the lemma, not of the word, and Strong's own entry names the man and the land in
one sentence. Only reading fills that gap. This is the harness that does the reading and, more
importantly, the measurement that says whether the reading may be believed -- 8,508 of those
occurrences already have an answer from the encyclopedia's verse lists, so the model can be run
over ground somebody else has already covered and scored against it before it is trusted anywhere
new.

    python scripts/sense.py extract --numbers H5780 --out .sense/pilot
    python scripts/sense.py extract --sample 400 --seed 11 --out .sense/run-11
    python scripts/sense.py ask   --dir .sense/run-11
    python scripts/sense.py score --dir .sense/run-11

Four things about the design are load-bearing, and each of them is a way the number could have been
made meaningless:

**A comma-joined Strong label is not a name.** `H4428,H3389` on the entity Adonizedek says what the
words of the title *king of Jerusalem* are. Read as a name it makes H3389 mean the man rather than
the city, and it makes H3068 look contested between the LORD, Moses, Saul and Haggai because *the
servant of the LORD* is a title too. Only single-number labels are read here, which is the same rule
the annotation loader follows. Reading the commas instead inflates this population from 9,996
occurrences to 20,880 and its answered part from 8,508 to 18,855 -- entirely with candidates that
are not candidates.

**The answer is removed from the prompt.** Each candidate is shown the verses the encyclopedia
already attests it in, because that is what lets a model reason by parallel -- but every verse being
asked about in the same batch is struck out of those lists first. Left in, a model could read the
answer off the evidence and the agreement figure would measure nothing but its ability to copy.

**The model never touches the database.** It is given text and it returns JSON. Every answer is
written to a file with the model id, the prompt version, the batch and the date, because it is a
claim about a reading and not a fact about the corpus.

**`unclear` and `unlisted` are answers.** An occurrence the model declines is not a wrong answer,
and an `unlisted` may be the model being right about a gap: three of the eight occurrences of H5780
are *the land of Uz*, which the encyclopedia does not hold at all, and a harness that forced a
choice would have put Job's homeland on a son of Aram. They are scored in their own columns.

Everything is re-runnable and diffable. `extract` writes the prompts, `ask` writes one JSONL row per
occurrence, `score` reads the JSONL and the database and writes a report; nothing downstream reaches
back. A run leaves four things behind under its own directory:

    manifest.json           the selection, its seed, and which names went into which batch
    batches/batch-NNNN.json the prompt payload, exactly as the model saw it
    answers.jsonl           one row per occurrence, appended, re-runnable batch by batch
    score.md                the agreement report, and disagreements.json beside it

An answer row is::

    {"word_id": 6477436, "strong_number": "H5780", "referent": "uz-2", "names": null,
     "confidence": "high", "reason": "Firstborn of Nahor by Milcah, matching Genesis 22:20-21.",
     "batch": "batch-0000", "prompt_version": "sense-1", "model": "claude-sonnet-5",
     "run": "2026-09-05T20:47:18+00:00", "method": "model-reading"}

`referent` is an entity slug, or `unlisted` -- with `names` saying what it is instead -- or
`unclear`. The method, the model, the prompt version and the date are on every row because the row
is a claim about a reading, and a claim with no method on it is indistinguishable from data.

A run's own directory is not committed -- it is cheap to make again from here. `scripts/sense-sample/`
carries enough of one to read without running anything: the prompt and the answers for H5780, and
the manifest, report and disagreements of the sample that was measured.
"""

import argparse
import datetime
import json
import os
import random
import re
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding='utf-8')

CONTAINER = 'essenthos-api-db-1'
DATABASE = 'essenthos_core'
USER = 'essenthos'

WITNESS = 'bhsa'
RENDERING = 'kjv'

PROMPT_VERSION = 'sense-1'

# How many verses of prior attestation a candidate is shown. A name borne by one of David's officers
# can be attested in a hundred places and the hundredth adds nothing the tenth did not; the cap keeps
# a batch's prompt bounded without changing what a reader could conclude from it.
ATTESTATION_SHOWN = 24

# The batch shape the economics were measured on: the harness overhead is per call, so one lemma per
# call cost six times as much per occurrence as three did. The occurrence budget is the second half
# of it -- three lemmas is cheap until one of them is Zechariah.
BATCH_NUMBERS = 3
BATCH_OCCURRENCES = 36

ANSWER_UNCLEAR = 'unclear'
ANSWER_UNLISTED = 'unlisted'

BOOKS = [
    'Genesis', 'Exodus', 'Leviticus', 'Numbers', 'Deuteronomy', 'Joshua', 'Judges', 'Ruth',
    '1 Samuel', '2 Samuel', '1 Kings', '2 Kings', '1 Chronicles', '2 Chronicles', 'Ezra',
    'Nehemiah', 'Esther', 'Job', 'Psalms', 'Proverbs', 'Ecclesiastes', 'Song of Solomon',
    'Isaiah', 'Jeremiah', 'Lamentations', 'Ezekiel', 'Daniel', 'Hosea', 'Joel', 'Amos',
    'Obadiah', 'Jonah', 'Micah', 'Nahum', 'Habakkuk', 'Zephaniah', 'Haggai', 'Zechariah',
    'Malachi',
]

# Only a label that is a single number is read. See the module docstring for what the commas are.
NAMING = """
    SELECT DISTINCT n.hebrew_strong_number AS number, n.entity_id
    FROM entity_name n
    WHERE n.hebrew_strong_number IS NOT NULL AND position(',' IN n.hebrew_strong_number) = 0
"""

CONTESTED = f"""
    SELECT number FROM ({NAMING}) named GROUP BY 1 HAVING count(*) > 1
"""

SYSTEM_PROMPT = """\
You are a Hebrew Bible scholar deciding, for each occurrence of a proper name in the Masoretic text,
which person or place that occurrence refers to.

You are given, for each Strong number: the lexicon entry, every candidate referent the encyclopedia
holds under that name with the verses it is otherwise attested in, and every occurrence of the name
with its reference, the name type BHSA marks on it, the Hebrew verse with the word in question
marked, the King James rendering of the same verse with the verse before and after it, and the King
James words that the alignment says stand for this Hebrew word.

Decide each occurrence on its own evidence. Genealogies, parallel lists and the line before are what
usually settle it; the King James rendering may spell the name differently from the candidate label
and that is not a reason to reject a candidate.

Two answers are as correct as any other, and you must use them rather than guess:

- "unlisted" when the referent is real but not among the candidates. A candidate list of persons
  where the occurrence plainly means a territory is the common case: say so, and name what it is.
- "unclear" when the evidence genuinely does not decide between two candidates.

Never pick the least-bad candidate to avoid answering. A confident wrong referent is worse here than
no answer, because a reader cannot tell it from scholarship.

Answer with a single JSON array and nothing else -- no prose before or after, no code fence
necessary. One object per occurrence, in the order given, with exactly these fields:

  word_id      the integer you were given, unchanged
  referent     a candidate's "key", or "unlisted", or "unclear"
  names        only when referent is "unlisted": a short phrase naming what it does refer to
  confidence   "high", "medium" or "low"
  reason       one line, under 25 words, saying what decided it

Every occurrence you were given must appear exactly once.
"""


def psql(sql):
    """One JSON value out of the live database. Read-only by construction: nothing here writes."""
    process = subprocess.run(
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', CONTAINER,
         'psql', '-U', USER, '-d', DATABASE, '-Aqt', '-v', 'ON_ERROR_STOP=1', '-f', '-'],
        input=sql.encode('utf-8'), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if process.returncode != 0:
        raise SystemExit(
            f"the query failed against {DATABASE} in container {CONTAINER}:\n"
            f"{process.stderr.decode('utf-8', 'replace')}\n"
            f"Check that the container is running -- docker ps -- and that the SQL above is valid.")
    out = process.stdout.decode('utf-8').strip()
    return json.loads(out) if out else None


def reference(book, chapter, verse):
    name = BOOKS[book - 1] if 1 <= book <= len(BOOKS) else f'book {book}'
    return f'{name} {chapter}:{verse}'


def quoted(numbers):
    return ', '.join("'" + n.replace("'", "''") + "'" for n in numbers)


def contested_numbers():
    """Every proper-noun Strong number the encyclopedia answers with more than one entity."""
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'number', o.number,
                   'candidates', o.candidates,
                   'occurrences', o.occurrences,
                   'answered', o.answered) ORDER BY o.number), '[]')
        FROM (
            SELECT w.strong_number AS number,
                   (SELECT count(*) FROM ({NAMING}) n WHERE n.number = w.strong_number) AS candidates,
                   count(*) AS occurrences,
                   count(*) FILTER (WHERE EXISTS (
                       SELECT 1
                       FROM verse_reference r
                       JOIN ({NAMING}) n ON n.number = w.strong_number
                       JOIN entity_verse ev ON ev.entity_id = n.entity_id
                            AND ev.canonical_book = r.canonical_book
                            AND ev.canonical_chapter = r.canonical_chapter
                            AND ev.canonical_verse = r.canonical_verse
                       WHERE r.verse_id = w.verse_id AND r.is_primary)) AS answered
            FROM word w
            JOIN text t ON t.id = w.text_id AND t.slug = '{WITNESS}'
            WHERE w.morphology->>'nameType' IS NOT NULL
              AND w.strong_number IN ({CONTESTED})
            GROUP BY 1
        ) o
    """)


def lexicon(numbers):
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'number', s.strong_number, 'lemma', s.lemma,
                   'transliteration', s.transliteration, 'definition', s.definition,
                   'derivation', s.derivation, 'kjv_definition', s.kjv_definition,
                   'detailed_definition', s.detailed_definition)), '[]')
        FROM strong_entry s WHERE s.strong_number IN ({quoted(numbers)})
    """)


def candidates(numbers):
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'number', c.number, 'entity_id', c.entity_id, 'key', c.slug,
                   'kind', c.kind, 'name', c.name, 'distinguisher', c.distinguisher,
                   'label', c.label, 'meaning', c.meaning, 'attested', c.attested)
                   ORDER BY c.number, c.slug), '[]')
        FROM (
            SELECT n.number, e.id AS entity_id, e.slug, e.kind, e.name, e.distinguisher,
                   min(en.label) AS label, min(en.meaning) AS meaning,
                   (SELECT coalesce(json_agg(json_build_array(
                                ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)), '[]')
                    FROM entity_verse ev WHERE ev.entity_id = e.id) AS attested
            FROM ({NAMING}) n
            JOIN entity e ON e.id = n.entity_id
            JOIN entity_name en ON en.entity_id = e.id AND en.hebrew_strong_number = n.number
            WHERE n.number IN ({quoted(numbers)})
            GROUP BY 1, 2, 3, 4, 5, 6
        ) c
    """)


def occurrences(numbers):
    """
    Every Hebrew occurrence of these names, with what a reader would need beside it: the word marked
    inside its own verse, the King James verse and its neighbours -- a referent is usually
    established a line earlier -- and the King James words the alignment puts opposite this one,
    which is what tells a reader that Genesis 22:21 spells Uz as *Huz*.
    """
    return psql(f"""
        WITH occ AS (
            SELECT w.id AS word_id, w.strong_number AS number, w.text AS hebrew,
                   w.morphology->>'nameType' AS name_type, w.verse_id, w.position,
                   r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v
            FROM word w
            JOIN text t ON t.id = w.text_id AND t.slug = '{WITNESS}'
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE w.morphology->>'nameType' IS NOT NULL
              AND w.strong_number IN ({quoted(numbers)})
        ),
        rendered AS (
            SELECT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v,
                   string_agg(w.text || w.trailer, '' ORDER BY w.position) AS line
            FROM verse ve
            JOIN verse_reference r ON r.verse_id = ve.id AND r.is_primary
            JOIN word w ON w.verse_id = ve.id
            WHERE ve.text_id = (SELECT id FROM text WHERE slug = '{RENDERING}')
              AND r.canonical_book IN (SELECT DISTINCT b FROM occ)
              AND r.canonical_chapter IN (SELECT DISTINCT c FROM occ)
            GROUP BY 1, 2, 3
        )
        SELECT coalesce(json_agg(json_build_object(
                   'word_id', o.word_id, 'number', o.number, 'hebrew', o.hebrew,
                   'name_type', o.name_type,
                   'book', o.b, 'chapter', o.c, 'verse', o.v,
                   'hebrew_verse', marked.line,
                   'rendering_before', (SELECT line FROM rendered x
                                        WHERE x.b = o.b AND x.c = o.c AND x.v = o.v - 1),
                   'rendering', (SELECT line FROM rendered x
                                 WHERE x.b = o.b AND x.c = o.c AND x.v = o.v),
                   'rendering_after', (SELECT line FROM rendered x
                                       WHERE x.b = o.b AND x.c = o.c AND x.v = o.v + 1),
                   'rendering_of_word', aligned.words)
                   ORDER BY o.b, o.c, o.v, o.position), '[]')
        FROM occ o
        CROSS JOIN LATERAL (
            SELECT string_agg(
                       CASE WHEN w.position = o.position THEN '<<' || w.text || '>>' ELSE w.text END,
                       ' ' ORDER BY w.position) AS line
            FROM word w WHERE w.verse_id = o.verse_id
        ) marked
        CROSS JOIN LATERAL (
            SELECT string_agg(DISTINCT k.text, ' ') AS words
            FROM link_word mine
            JOIN link_word other ON other.link_id = mine.link_id AND other.side <> mine.side
            JOIN word k ON k.id = other.word_id
                 AND k.text_id = (SELECT id FROM text WHERE slug = '{RENDERING}')
            WHERE mine.word_id = o.word_id
        ) aligned
    """)


def witness_answers(word_ids):
    """
    What the encyclopedia's verse lists say about these occurrences: the candidates it attests in
    the very verse the word stands in. This is never shown to the model -- it is read at scoring
    time, from the database, against answers already written.
    """
    ids = ', '.join(str(int(i)) for i in word_ids)
    return psql(f"""
        SELECT coalesce(json_object_agg(a.word_id, a.entities), '{{}}')
        FROM (
            SELECT w.id AS word_id,
                   coalesce(json_agg(DISTINCT e.slug) FILTER (WHERE e.slug IS NOT NULL), '[]') AS entities
            FROM word w
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            LEFT JOIN ({NAMING}) n ON n.number = w.strong_number
            LEFT JOIN entity_verse ev ON ev.entity_id = n.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            LEFT JOIN entity e ON e.id = ev.entity_id
            WHERE w.id IN ({ids})
            GROUP BY 1
        ) a
    """)


def batched(numbers, counts, per_call, budget):
    batch, occurrences_in_batch = [], 0
    for number in numbers:
        if batch and (len(batch) >= per_call or occurrences_in_batch + counts[number] > budget):
            yield batch
            batch, occurrences_in_batch = [], 0
        batch.append(number)
        occurrences_in_batch += counts[number]
    if batch:
        yield batch


def extract(args):
    """Write one prompt payload per batch, and the run's provenance beside them."""
    population = contested_numbers()
    counts = {p['number']: p['occurrences'] for p in population}

    if args.numbers:
        chosen, how = list(args.numbers), {'kind': 'named', 'numbers': list(args.numbers)}
        missing = [n for n in chosen if n not in counts]
        if missing:
            raise SystemExit(
                f"{', '.join(missing)} is not a contested proper-noun Strong number in {DATABASE}. "
                f"Run with --sample to draw from the {len(population)} that are.")
    else:
        # Cluster sampling by lemma, not by occurrence: a name is only decidable beside its other
        # occurrences, so a batch always carries all of them. The shuffle is seeded and the seed is
        # recorded, which is what makes a second run on a fresh sample cheap to justify.
        scorable = [p['number'] for p in population if p['answered'] > 0]
        random.Random(args.seed).shuffle(scorable)
        chosen, taken = [], 0
        for number in scorable:
            if taken >= args.sample:
                break
            chosen.append(number)
            taken += counts[number]
        how = {'kind': 'sample', 'seed': args.seed, 'target': args.sample, 'drawn': taken}

    entries = {e['number']: e for e in lexicon(chosen)}
    by_number_candidates, by_number_occurrences = {}, {}
    for candidate in candidates(chosen):
        by_number_candidates.setdefault(candidate['number'], []).append(candidate)
    for occurrence in occurrences(chosen):
        by_number_occurrences.setdefault(occurrence['number'], []).append(occurrence)

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    manifest = {
        'prompt_version': PROMPT_VERSION,
        'database': DATABASE,
        'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'selection': how,
        'numbers': chosen,
        'batches': [],
    }

    for index, numbers in enumerate(batched(chosen, counts, args.batch_numbers, args.batch_occurrences)):
        name = f'batch-{index:04d}'
        asked = {(o['book'], o['chapter'], o['verse'])
                 for n in numbers for o in by_number_occurrences.get(n, [])}
        payload = {'batch': name, 'prompt_version': PROMPT_VERSION, 'names': []}
        for number in numbers:
            payload['names'].append({
                'strong_number': number,
                'lexicon': entries.get(number),
                'candidates': [shown(c, asked) for c in by_number_candidates.get(number, [])],
                'occurrences': [asking(o) for o in by_number_occurrences.get(number, [])],
            })
        path = os.path.join(args.dir, 'batches', name + '.json')
        with open(path, 'w', encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)
        manifest['batches'].append({
            'batch': name,
            'numbers': numbers,
            'occurrences': sum(len(by_number_occurrences.get(n, [])) for n in numbers),
        })

    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    total = sum(b['occurrences'] for b in manifest['batches'])
    print(f"{len(chosen)} names, {total} occurrences, {len(manifest['batches'])} batches -> {args.dir}")


def shown(candidate, asked):
    """
    A candidate as the model sees it. Every verse this batch is asking about is struck out of the
    attestation list: left in, the model would be reading the answer off the evidence and the
    agreement figure would measure copying.
    """
    attested = [tuple(a) for a in candidate['attested']]
    withheld = [a for a in attested if a in asked]
    keep = [a for a in attested if a not in asked]
    step = max(1, len(keep) // ATTESTATION_SHOWN + (1 if len(keep) % ATTESTATION_SHOWN else 0))
    return {
        'key': candidate['key'],
        'kind': candidate['kind'],
        'name': candidate['name'],
        'distinguisher': candidate['distinguisher'],
        'meaning': candidate['meaning'],
        'attested_in': [reference(*a) for a in keep[::step]],
        'attested_in_total': len(keep),
        'withheld_for_this_batch': len(withheld),
    }


def asking(occurrence):
    return {
        'word_id': occurrence['word_id'],
        'reference': reference(occurrence['book'], occurrence['chapter'], occurrence['verse']),
        'bhsa_name_type': occurrence['name_type'],
        'hebrew_verse': occurrence['hebrew_verse'],
        'king_james_before': occurrence['rendering_before'],
        'king_james': occurrence['rendering'],
        'king_james_after': occurrence['rendering_after'],
        'king_james_words_for_this_word': occurrence['rendering_of_word'],
    }


def executable():
    found = shutil.which('claude')
    if found:
        return found
    fallback = os.path.expanduser(r'~\.local\bin\claude.exe')
    if os.path.exists(fallback):
        return fallback
    raise SystemExit(
        "the claude CLI is not on PATH and is not at ~/.local/bin/claude.exe. "
        "Install it, or pass its path in the CLAUDE environment variable.")


def call(prompt, model):
    """
    One batch, one call, one turn, no tools and no session. The flags are what turn the CLI into a
    worker rather than an agent; --bare looks like the right one and is not, because it reads auth
    strictly from an API key and never from the subscription.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', SYSTEM_PROMPT, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1',
    ]
    process = subprocess.run(
        command, input=prompt.encode('utf-8'),
        stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if process.returncode != 0:
        return None, process.stderr.decode('utf-8', 'replace')
    try:
        return json.loads(process.stdout.decode('utf-8', 'replace')), None
    except json.JSONDecodeError as broken:
        return None, f'the harness did not return JSON: {broken}'


ARRAY = re.compile(r'\[.*\]', re.S)


def parse(result):
    text = (result or '').strip()
    if text.startswith('```'):
        text = text.strip('`')
        text = text[text.find('\n') + 1:] if '\n' in text else text
        if text.lstrip().startswith('json'):
            text = text.lstrip()[4:]
    match = ARRAY.search(text)
    if not match:
        return None
    try:
        answers = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    return answers if isinstance(answers, list) else None


def ask(args):
    """Run every batch that has no answers yet, appending one JSONL row per occurrence."""
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)

    answers_path = os.path.join(args.dir, 'answers.jsonl')
    done = set()
    if os.path.exists(answers_path):
        with open(answers_path, encoding='utf-8') as handle:
            done = {json.loads(line)['batch'] for line in handle if line.strip()}

    run = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    spent, called, failures = 0.0, 0, []

    for entry in manifest['batches']:
        name = entry['batch']
        if name in done and not args.again:
            continue
        with open(os.path.join(args.dir, 'batches', name + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        expected = [o['word_id'] for n in payload['names'] for o in n['occurrences']]
        by_word = {o['word_id']: n['strong_number']
                   for n in payload['names'] for o in n['occurrences']}
        keys = {c['key'] for n in payload['names'] for c in n['candidates']}

        started = time.time()
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1), args.model)
        if failed:
            failures.append((name, failed))
            print(f'{name}: {failed}')
            continue

        spent += outcome.get('total_cost_usd') or 0.0
        called += 1
        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        answers = parse(outcome.get('result'))
        if answers is None:
            failures.append((name, 'no JSON array in the reply'))
            print(f'{name}: no JSON array in the reply')
            continue

        rows, seen = [], set()
        for answer in answers:
            word_id = answer.get('word_id')
            if word_id not in by_word or word_id in seen:
                continue
            seen.add(word_id)
            referent = answer.get('referent')
            if referent not in keys and referent not in (ANSWER_UNCLEAR, ANSWER_UNLISTED):
                referent, answer['names'] = ANSWER_UNCLEAR, answer.get('names')
            rows.append({
                'word_id': word_id,
                'strong_number': by_word[word_id],
                'referent': referent,
                'names': answer.get('names'),
                'confidence': answer.get('confidence'),
                'reason': answer.get('reason'),
                'batch': name,
                'prompt_version': payload['prompt_version'],
                'model': model,
                'run': run,
                'method': 'model-reading',
            })

        with open(answers_path, 'a', encoding='utf-8') as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')

        missing = len(expected) - len(rows)
        note = f', {missing} unanswered' if missing else ''
        print(f'{name}: {len(rows)}/{len(expected)} answered{note}, '
              f'{time.time() - started:.0f}s, ${outcome.get("total_cost_usd") or 0:.4f}')

    print(f'{called} calls, ${spent:.4f} reported by the harness'
          + (f', {len(failures)} failed' if failures else ''))


def band(confidence):
    return confidence if confidence in ('high', 'medium', 'low') else 'unstated'


def bucket(count):
    if count == 2:
        return '2 candidates'
    if count <= 4:
        return '3-4 candidates'
    if count <= 9:
        return '5-9 candidates'
    return '10+ candidates'


def tally():
    return {'agree': 0, 'disagree': 0, 'declined_unclear': 0, 'declined_unlisted': 0}


def rate(counts):
    decided = counts['agree'] + counts['disagree']
    return f'{100.0 * counts["agree"] / decided:.1f}%' if decided else '-'


def score(args):
    """
    Compare every answer with the encyclopedia's verse lists, which are a witness and not a truth.

    An occurrence is only *scorable* where the lists name exactly one of the candidates in that
    verse. Where they name none, an "unlisted" from the model may be the model being right about a
    gap and a named candidate may be an extension nobody can check; where they name two, the witness
    itself has not decided. Both are counted and neither is folded into the agreement figure.
    """
    answers_path = os.path.join(args.dir, 'answers.jsonl')
    with open(answers_path, encoding='utf-8') as handle:
        rows = [json.loads(line) for line in handle if line.strip()]
    if not rows:
        raise SystemExit(f'{answers_path} is empty. Run "ask --dir {args.dir}" first.')

    witness = witness_answers([r['word_id'] for r in rows])
    numbers = sorted({r['strong_number'] for r in rows})
    candidate_count = {p['number']: p['candidates'] for p in contested_numbers()
                       if p['number'] in set(numbers)}

    overall, by_band, by_bucket = tally(), {}, {}
    unwitnessed = {'model_unlisted': 0, 'model_named': 0, 'model_unclear': 0}
    contested_witness = 0
    disagreements = []

    for row in rows:
        named = witness.get(str(row['word_id'])) or witness.get(row['word_id']) or []
        answer = row['referent']
        if len(named) > 1:
            contested_witness += 1
            continue
        if not named:
            if answer == ANSWER_UNLISTED:
                unwitnessed['model_unlisted'] += 1
            elif answer == ANSWER_UNCLEAR:
                unwitnessed['model_unclear'] += 1
            else:
                unwitnessed['model_named'] += 1
            continue

        buckets = [overall,
                   by_band.setdefault(band(row['confidence']), tally()),
                   by_bucket.setdefault(bucket(candidate_count.get(row['strong_number'], 0)), tally())]
        if answer == ANSWER_UNCLEAR:
            key = 'declined_unclear'
        elif answer == ANSWER_UNLISTED:
            key = 'declined_unlisted'
        elif answer == named[0]:
            key = 'agree'
        else:
            key = 'disagree'
        for target in buckets:
            target[key] += 1
        if key == 'disagree':
            disagreements.append({
                'word_id': row['word_id'], 'strong_number': row['strong_number'],
                'model': answer, 'witness': named[0],
                'confidence': row['confidence'], 'reason': row['reason'],
            })

    lines = []
    write = lines.append
    write(f'# Agreement with the encyclopedia\'s verse lists')
    write('')
    write(f'{len(rows)} answers over {len(numbers)} Strong numbers, '
          f'prompt {rows[0]["prompt_version"]}, model {rows[0]["model"]}, run {rows[0]["run"]}.')
    write('')
    scorable = sum(overall.values())
    write(f'{scorable} occurrences the witness answers with exactly one candidate; '
          f'{sum(unwitnessed.values())} it answers with none and '
          f'{contested_witness} with more than one.')
    write('')
    write('## Overall, where the witness decides')
    write('')
    write('| | agree | disagree | unclear | unlisted | agreement |')
    write('|---|---|---|---|---|---|')
    write(f'| all | {overall["agree"]} | {overall["disagree"]} | {overall["declined_unclear"]} '
          f'| {overall["declined_unlisted"]} | {rate(overall)} |')
    for title, table, order in (('confidence', by_band, ['high', 'medium', 'low', 'unstated']),
                                ('candidates', by_bucket,
                                 ['2 candidates', '3-4 candidates', '5-9 candidates', '10+ candidates'])):
        write('')
        write(f'## By {title}')
        write('')
        write('| | agree | disagree | unclear | unlisted | agreement |')
        write('|---|---|---|---|---|---|')
        for key in order:
            if key in table:
                counts = table[key]
                write(f'| {key} | {counts["agree"]} | {counts["disagree"]} '
                      f'| {counts["declined_unclear"]} | {counts["declined_unlisted"]} | {rate(counts)} |')
    write('')
    write('## Where the witness says nothing')
    write('')
    write(f'- {unwitnessed["model_unlisted"]} the model also called unlisted')
    write(f'- {unwitnessed["model_named"]} the model named a candidate for')
    write(f'- {unwitnessed["model_unclear"]} the model declined as unclear')
    write('')
    write('## Disagreements')
    write('')
    for item in disagreements[:args.disagreements]:
        write(f'- {item["strong_number"]} word {item["word_id"]}: model **{item["model"]}** '
              f'({item["confidence"]}), witness **{item["witness"]}** -- {item["reason"]}')
    if len(disagreements) > args.disagreements:
        write(f'- ... and {len(disagreements) - args.disagreements} more, in disagreements.json')

    report = '\n'.join(lines) + '\n'
    with open(os.path.join(args.dir, 'score.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    with open(os.path.join(args.dir, 'disagreements.json'), 'w', encoding='utf-8') as handle:
        json.dump(disagreements, handle, ensure_ascii=False, indent=1)
    print(report)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    commands = parser.add_subparsers(dest='command', required=True)

    extractor = commands.add_parser('extract', help='write the prompt payloads for a run')
    extractor.add_argument('--out', dest='dir', required=True)
    extractor.add_argument('--numbers', nargs='+', help='name the Strong numbers explicitly')
    extractor.add_argument('--sample', type=int, help='draw about this many occurrences instead')
    extractor.add_argument('--seed', type=int, default=1)
    extractor.add_argument('--batch-numbers', type=int, default=BATCH_NUMBERS)
    extractor.add_argument('--batch-occurrences', type=int, default=BATCH_OCCURRENCES)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='run every batch that has no answers yet')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='sonnet')
    asker.add_argument('--again', action='store_true', help='re-run batches that already answered')
    asker.set_defaults(run=ask)

    scorer = commands.add_parser('score', help='measure the answers against the verse lists')
    scorer.add_argument('--dir', required=True)
    scorer.add_argument('--disagreements', type=int, default=20)
    scorer.set_defaults(run=score)

    args = parser.parse_args()
    if args.command == 'extract' and not args.numbers and not args.sample:
        raise SystemExit('extract needs either --numbers or --sample.')
    args.run(args)


if __name__ == '__main__':
    main()
