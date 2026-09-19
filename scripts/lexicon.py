"""
The Strong lexicon in a language other than English, and the measurement that says whether it may be
believed.

14,197 entries, every definition in English, and a reader of the Ukrainian text meets an English
gloss the moment he asks what a word means. Strong's own text is public domain, so translating it is
allowed; what is not allowed is letting the result read as though Strong wrote it. Every row this
harness produces carries what did the translating, when, and against which English -- because a
machine's reading of *of uncertain affinity* is a new claim, not the old one in another language.

    python scripts/lexicon.py extract --language uk --sample 300 --seed 11 --out .lexicon/pilot-uk
    python scripts/lexicon.py ask     --dir .lexicon/pilot-uk
    python scripts/lexicon.py check   --dir .lexicon/pilot-uk
    python scripts/lexicon.py judge   --dir .lexicon/pilot-uk
    python scripts/lexicon.py score   --dir .lexicon/pilot-uk

Five things about the design are load-bearing.

**Four fields are language and the rest are identifiers.** `definition`, `derivation`,
`kjv_definition` and `detailed_definition` are prose. The lemma, the transliteration, the
pronunciation, the morphology code, the TWOT reference and the see-also numbers are not: they are
how a lookup finds the entry, and a translated identifier breaks the lookup silently. They are shown
to the model as context and are never asked for back, so there is no path by which a translated one
could be written.

**The structure inside the prose is an identifier too.** A derivation says `from G25;` and
`plural of אֱלוֹהַּ (H433);`; a detailed definition is numbered `1)`, `1a)`, `1b)`. Those tokens
address other entries and other senses, and `check` fails a row that loses one. The measurement that
matters most here is not fluency -- it is whether the machine kept the skeleton.

**A King James rendering list is half prose and half index.** `angels, [idiom] exceeding, God
(gods) (-dess, -ly)` is a list of the English words the concordance says the King James uses, with an
affix notation that only means anything in English. Rendering it into Ukrainian gives a reader the
senses and loses the pointer into the English text, so it is translated as a plain list of glosses,
the affix machinery dropped, and the English is kept beside it on the row. See KJV_RULE.

**The English never leaves.** A translated field is written next to the English it renders, never
over it, so the reader can put the two side by side and so a re-run can be diffed against the last.

**The number is produced before the run, not after.** `check` is deterministic and catches the
failures that are certain -- a dropped sense number, a lost Strong reference, an answer still in
English. `judge` is a second model reading, shown the English and the Ukrainian and nothing about
who produced either, scoring terminology, completeness and fluency separately, because a fluent
wrong translation of a term of art is the failure this whole exercise exists to avoid and fluency
alone would score it full marks.

A run leaves five things under its own directory:

    manifest.json            the selection, its seed, and which numbers went into which batch
    batches/batch-NNNN.json  the prompt payload, exactly as the model saw it
    answers.jsonl            one row per entry, appended, re-runnable batch by batch
    judgements.jsonl         one row per entry, from a second model that saw only the two texts
    score.md                 the report, with failures.json beside it

A run's own directory is not committed; it is cheap to make again from here.
"""

import argparse
import concurrent.futures
import datetime
import json
import os
import random
import re
import shutil
import subprocess
import sys
import threading
import time

sys.stdout.reconfigure(encoding='utf-8')

CONTAINER = 'essenthos-api-db-1'
DATABASE = 'essenthos_core'
USER = 'essenthos'

PROMPT_VERSION = 'lexicon-3'
JUDGE_VERSION = 'lexicon-judge-1'

# The four fields that are language. Everything else on the entry is a key into something.
PROSE = ('definition', 'derivation', 'kjv_definition', 'detailed_definition')

# The batch shape. The harness overhead is per call, so one entry per call costs several times what
# a batch does -- but sense.py found the other end of it the hard way: a batch too large came back
# with no JSON at all, forty batches into a run. So a batch is bounded twice, by entry count and by
# the characters of prose it carries, and the character budget is the one that actually binds.
BATCH_ENTRIES = 8
BATCH_CHARACTERS = 6000

# A batch is two to three minutes of output tokens, so fifteen is not a budget: it is the point past
# which the call is not coming back. Without it a worker blocked on a dead pipe costs the run a sixth
# of its throughput for as long as the run lasts, and says nothing while it does.
CALL_TIMEOUT_SECONDS = 900

# The subscription's rate limit is the one failure that is certain to happen on a run this long, and
# it is not a defect: it clears on its own. Six agents were killed by it in one afternoon, so a batch
# that hits it waits and asks again rather than being recorded as a failure. The waits are long
# because the window it is waiting for is measured in minutes, not seconds.
RETRY_WAITS = (60, 180, 420, 900)
RATE_LIMITED = ('rate limit', 'rate_limit', 'usage limit', 'too many requests',
                '429', 'overloaded', 'quota')

LANGUAGES = {
    'uk': 'Ukrainian',
    'de': 'German',
    'es': 'Spanish',
}

# What the corpus calls the same languages. Every text, name form and phrasing in the database is
# tagged with a three-letter code, so a published file that says `uk` is a file the loader has to
# translate before it can join anything -- and a language column with two spellings of Ukrainian in
# it is a column that silently answers half a question.
CORPUS_LANGUAGES = {
    'uk': 'ukr',
    'de': 'deu',
    'es': 'spa',
}

# Strong writes with a fixed vocabulary of about thirty qualifiers, and they are the whole difficulty.
# `by implication` is not "by meaning": it marks a sense derived from the head sense rather than
# stated by it, and a model that renders it by its everyday meaning produces fluent Ukrainian that
# asserts the opposite. Prompt version 1 lost four fifths of its terminology marks to that one phrase
# and to a handful like it, so the settled equivalents are given rather than left to be guessed. The
# counts are how often each appears across `definition` and `derivation` in the 14,197 entries.
#
# A language with no table here cannot be run: an empty glossary is not a neutral default, it is
# prompt version 1 again, and the pilot measured what that costs.
GLOSSARY = {
    'uk': [
        ('i.e.', 'тобто'),
        ('figuratively', 'переносно'),
        ('a primitive root', 'первісний корінь'),
        ('by implication', 'у похідному значенні — the sense is derived from the head sense rather '
                           'than stated by it. Never «звідси», which is *hence*; never «переносно», '
                           'which is *figuratively*; never «за значенням» or «опосередковано»'),
        ('properly', 'власне'),
        ('literally', 'буквально'),
        ('an unused root', 'невживаний корінь'),
        ('specially', 'особливо'),
        ('the base of', 'основа слова: «від основи G2865»'),
        ('compare', 'порівн.'),
        ('of Hebrew origin', 'єврейського походження'),
        ('causative', 'каузативна (спонукальна) форма'),
        ('middle voice', 'медіальний (середній) стан'),
        ('by analogy', 'за аналогією'),
        ('denominative', 'відіменникове (деномінатив)'),
        ('akin to', 'споріднене з'),
        ('by extension', 'у ширшому значенні'),
        ('hence', 'звідси'),
        ('adverbially', 'прислівниково'),
        ('of foreign origin', 'чужомовного походження'),
        ('patronymic', 'патронім, по батькові'),
        ('of uncertain affinity', 'неясної спорідненості'),
        ('a primary word', 'первинне слово'),
        ('intensive', 'підсилювальна форма (інтенсив)'),
        ('a variation of', 'варіант'),
        ('by reduplication', 'редуплікацією (подвоєнням)'),
        ('contracted', 'стягнена форма'),
        ('diminutive', 'зменшувальна форма (демінутив)'),
        ('prolongation, prolonged', 'подовжена форма'),
        ('collateral form', 'паралельна форма'),
        ('by Hebraism', 'за гебраїзмом'),
        ('cognate', 'споріднене слово'),
        ('gentilic', 'гентилік — назва народу або мешканця'),
    ],
}

# The Hebrew stem names and the Greek voice abbreviations are names, not words, and a lexicon that
# translates them stops agreeing with every grammar its reader owns.
UNTRANSLATED_TERMS = (
    'Qal, Niphal, Piel, Pual, Hiphil, Hophal, Hithpael and the other Hebrew stem names, and the '
    'Aramaic Peal, Peil, Pael, Aphel, Ithpeel, Ithpaal and Hithpaal'
)

KJV_RULE = (
    'a comma-separated list of the English words the King James uses to render the lexeme, in '
    "Strong's affix notation: parentheses mark an alternative or a suffix, [idiom] marks an "
    'idiomatic rendering, X marks one where the sense is not literal, + marks one where the word is '
    'part of a phrase. That notation is English morphology and does not survive translation. Give '
    'a plain comma-separated list of the senses those renderings carry, in the same order, and drop '
    'the affix machinery. Do not invent renderings that are not in the English list.'
)

SYSTEM_PROMPT = """\
You translate entries of Strong's Hebrew and Greek dictionary into {language}, for a Bible research
platform where a reader can always see the English beside your translation.

You are given, for each entry: its Strong number, its lemma in the original script, its
transliteration, its part-of-speech code, and the English fields to translate. The lemma, the
transliteration, the code and the number are context. You never return them.

What you are translating is a nineteenth-century lexicographer's prose about Hebrew and Greek
lexemes. It is dense with terms of art, and rendering one of them by its everyday meaning produces
fluent {language} that is wrong in a way no reader can detect. Use the established {language}
terminology of Biblical philology. Where a term genuinely has no settled {language} equivalent, give
the closest one and keep the Latin or Greek term in brackets after it rather than paraphrasing.

Strong writes with a fixed vocabulary, and these are its settled {language} equivalents. Use them:

{glossary}

The connectives above -- properly, literally, figuratively, by implication, by extension, hence,
specially, i.e. -- each mark a different relation between one sense and the next, and Strong chooses
between them deliberately. Never render two of them by the same {language} word: a distinction he
makes hundreds of times disappears and no reader can see that it has.

Leave these in Latin letters wherever they appear, because they are the names every grammar uses:
{untranslated}.

Five things must survive unchanged, because they are addresses and not words:

1. Strong references -- G25, H433, (H7218) -- exactly as written, in Latin letters.
2. Hebrew, Greek and Aramaic text quoted inside a field. Copy the characters; do not transliterate
   and do not translate.
3. The sense numbering of a detailed definition: 1), 1a), 2b) and so on, in the same order, one per
   line, none merged and none added.
4. Bracketed markers that are notation rather than language: [idiom], and a leading X or + in a
   King James rendering list.
5. The order and the count of the comma-separated items in a rendering list.

Field by field:

  definition            {language}. Strong's sense of the lexeme.
  derivation            {language}, except that the Strong references and the quoted original-script
                        words inside it are copied through untouched.
  kjv_definition        {kjv_rule}
  detailed_definition   {language}, one numbered sense per line, numbering preserved exactly.

A field you were not given is a field you do not return. Never return a field empty, and never
return the English unchanged as though it were a translation -- if a field is genuinely
untranslatable, translate what can be translated and leave the untranslatable token in place.

Answer with a single JSON array and nothing else -- no prose before or after, no code fence
necessary. One object per entry, in the order given, with exactly these fields:

  strong_number   the string you were given, unchanged
  definition      only if you were given one
  derivation      only if you were given one
  kjv_definition  only if you were given one
  detailed_definition  only if you were given one
  uncertain       a list, possibly empty, of English terms you were not confident about

Every entry you were given must appear exactly once.
"""

JUDGE_PROMPT = """\
You are scoring translations of Strong's Hebrew and Greek dictionary into {language}. You did not
make them and you are not told what did. Judge only what is in front of you.

For each entry you get the English fields and the {language} fields that claim to render them.

Score each entry on three axes, 1 to 5, where 3 is the lowest score a reader could rely on:

  terminology   Are the terms of art of Biblical philology rendered by their established {language}
                equivalents? A word rendered by its everyday meaning where the lexicon means
                something technical scores 2 or below however natural it reads.
  completeness  Is every claim of the English present, and nothing added? A dropped sense, a dropped
                qualifier such as "figuratively" or "by implication", or an invented gloss scores 2
                or below.
  fluency       Is it {language} a reader of a reference work would accept -- idiomatic, correctly
                inflected, not a word-for-word calque?

Do not reward fluency for accuracy or punish accuracy for reading stiffly. They are separate scores
and a fluent mistranslation is the worst outcome here, not a middling one.

List, in `errors`, every place the {language} says something the English does not, or fails to say
something the English does. Quote the {language} phrase and say in English what is wrong with it.
An entry with no errors has an empty list.

Answer with a single JSON array and nothing else -- no prose before or after, no code fence
necessary. One object per entry, in the order given, with exactly these fields:

  strong_number  the string you were given, unchanged
  terminology    1-5
  completeness   1-5
  fluency        1-5
  errors         a list of {{"field": ..., "quote": ..., "problem": ...}}, possibly empty

Every entry you were given must appear exactly once.
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


def entries(numbers=None):
    """Every lexicon entry, or the ones named, with the identifiers beside the prose."""
    where = ''
    if numbers:
        listed = ', '.join("'" + n.replace("'", "''") + "'" for n in numbers)
        where = f'WHERE strong_number IN ({listed})'
    return psql(f"""
        SELECT coalesce(json_agg(row), '[]')::text FROM (
            SELECT json_build_object(
                'strong_number', strong_number,
                'lemma', lemma,
                'transliteration', transliteration,
                'morphology', morphology,
                'source_language', source_language,
                'definition', definition,
                'derivation', derivation,
                'kjv_definition', kjv_definition,
                'detailed_definition', detailed_definition) AS row
            FROM strong_entry
            {where}
            ORDER BY left(strong_number, 1), (substring(strong_number from 2))::int
        ) rows;
    """) or []


# ---------------------------------------------------------------------------- extract


def asking(entry):
    """What the model is shown: the identifiers as context, the prose as the work."""
    shown = {
        'strong_number': entry['strong_number'],
        'lemma': entry['lemma'],
        'transliteration': entry['transliteration'],
        'part_of_speech': entry['morphology'],
    }
    for field in PROSE:
        if entry.get(field):
            shown[field] = entry[field]
    return {k: v for k, v in shown.items() if v}


def batched(selected):
    """Bounded by entries and by characters, because the character budget is the one that bites."""
    batches, current, size = [], [], 0
    for entry in selected:
        cost = sum(len(entry.get(f) or '') for f in PROSE)
        if current and (len(current) >= BATCH_ENTRIES or size + cost > BATCH_CHARACTERS):
            batches.append(current)
            current, size = [], 0
        current.append(entry)
        size += cost
    if current:
        batches.append(current)
    return batches


def extract(args):
    language = LANGUAGES[args.language]
    selected = entries(args.numbers)
    if args.sample:
        random.Random(args.seed).shuffle(selected)
        selected = selected[:args.sample]
        selected.sort(key=lambda e: (e['strong_number'][0], int(e['strong_number'][1:])))
    if not selected:
        raise SystemExit('nothing selected. Check --numbers, or that the lexicon is loaded.')

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    manifest = {
        'language': args.language,
        'language_name': language,
        'prompt_version': PROMPT_VERSION,
        'seed': args.seed,
        'sample': args.sample,
        'entries': len(selected),
        'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'batches': [],
    }
    for index, group in enumerate(batched(selected)):
        name = f'batch-{index:04d}'
        payload = {
            'batch': name,
            'language': language,
            'prompt_version': PROMPT_VERSION,
            'entries': [asking(e) for e in group],
        }
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as out:
            json.dump(payload, out, ensure_ascii=False, indent=1)
        manifest['batches'].append({
            'batch': name,
            'numbers': [e['strong_number'] for e in group],
            'characters': sum(len(e.get(f) or '') for e in group for f in PROSE),
        })

    with open(os.path.join(args.dir, 'source.json'), 'w', encoding='utf-8') as out:
        json.dump(selected, out, ensure_ascii=False, indent=1)
    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as out:
        json.dump(manifest, out, ensure_ascii=False, indent=1)
    print(f'{len(selected)} entries in {len(manifest["batches"])} batches -> {args.dir}')


# ---------------------------------------------------------------------------- sample


def sample(args):
    """
    A finished run, narrowed to a fresh sample of it, so the second reading can be afforded.

    Judging is the expensive half -- opus over 14,197 entries is several times the translation it is
    checking, and it measures the same thing a sample does. So the whole corpus is translated and a
    few hundred of it are judged, and this is what puts those few hundred somewhere `judge` can run.
    The sample is drawn from the answers rather than from the database, with its own seed, so it is
    a fresh draw and not the pilot's 300 again.
    """
    with open(os.path.join(args.source, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    with open(os.path.join(args.source, 'source.json'), encoding='utf-8') as handle:
        source = {e['strong_number']: e for e in json.load(handle)}

    answers = {r['strong_number']: r for r in read(os.path.join(args.source, 'answers.jsonl'))}
    numbers = sorted(answers, key=lambda n: (n[0], int(n[1:])))
    if not numbers:
        raise SystemExit(f'{args.source} has no answers to sample. Run `ask --dir {args.source}`.')

    drawn = list(numbers)
    random.Random(args.seed).shuffle(drawn)
    drawn = sorted(drawn[:args.size], key=lambda n: (n[0], int(n[1:])))

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    taken = {
        **manifest,
        'sample': len(drawn),
        'seed': args.seed,
        'entries': len(drawn),
        'sampled_from': args.source,
        'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'batches': [],
    }
    for index, group in enumerate(batched([source[n] for n in drawn])):
        name = f'batch-{index:04d}'
        payload = {
            'batch': name,
            'language': manifest['language_name'],
            'prompt_version': manifest['prompt_version'],
            'entries': [asking(e) for e in group],
        }
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as out:
            json.dump(payload, out, ensure_ascii=False, indent=1)
        taken['batches'].append({
            'batch': name,
            'numbers': [e['strong_number'] for e in group],
            'characters': sum(len(e.get(f) or '') for e in group for f in PROSE),
        })

    with open(os.path.join(args.dir, 'source.json'), 'w', encoding='utf-8') as out:
        json.dump([source[n] for n in drawn], out, ensure_ascii=False, indent=1)
    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as out:
        json.dump(taken, out, ensure_ascii=False, indent=1)
    with open(os.path.join(args.dir, 'answers.jsonl'), 'w', encoding='utf-8') as out:
        for number in drawn:
            out.write(json.dumps(answers[number], ensure_ascii=False) + '\n')

    print(f'{len(drawn)} of {len(numbers)} answered entries, seed {args.seed} '
          f'-> {args.dir}, in {len(taken["batches"])} batches')


# ---------------------------------------------------------------------------- ask


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


def call(prompt, system, model):
    """
    One batch, one call, one turn, no tools and no session. The flags are what turn the CLI into a
    worker rather than an agent; --bare looks like the right one and is not, because it reads auth
    strictly from an API key and never from the subscription.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', system, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1',
    ]
    try:
        process = subprocess.run(
            command, input=prompt.encode('utf-8'),
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=CALL_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        return None, f'the call did not return within {CALL_TIMEOUT_SECONDS}s and was killed'
    if process.returncode != 0:
        # An empty stderr is not "no error": read as a falsy failure it killed a sense.py run forty
        # batches in. Say something whatever the process said.
        said = process.stderr.decode('utf-8', 'replace').strip()
        out = process.stdout.decode('utf-8', 'replace').strip()
        return None, said or out[:400] or f'the harness exited {process.returncode} and said nothing'
    try:
        outcome = json.loads(process.stdout.decode('utf-8', 'replace'))
    except json.JSONDecodeError as broken:
        return None, f'the harness did not return JSON: {broken}'
    if outcome.get('is_error'):
        said = str(outcome.get('result') or outcome.get('subtype') or 'no reason given')
        return None, f'the model reported an error: {said[:400]}'
    return outcome, None


def rate_limited(why):
    lowered = (why or '').lower()
    return any(marker in lowered for marker in RATE_LIMITED)


def call_with_retries(prompt, system, model, say):
    """
    The same call, waited out rather than given up on. A rate limit is a wait, not a failure: it
    clears by itself and the alternative is a five-hour run stopping an hour in with nothing wrong.
    Everything else gets one retry, because a transient network fault looks the same as a permanent
    one from here and re-asking is cheaper than re-running the whole batch tomorrow.
    """
    for attempt, wait in enumerate(RETRY_WAITS + (None,)):
        outcome, why = call(prompt, system, model)
        if outcome is not None or wait is None:
            return outcome, why
        if not rate_limited(why) and attempt >= 1:
            return outcome, why
        say(f'{why} -- waiting {wait}s and asking again')
        time.sleep(wait)
    return None, 'unreachable'


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


def run_batches(args, path, system_of, payload_of, row_of, expected_of=None):
    """
    The loop `ask` and `judge` share: skip what is answered, call, parse, append, and never let a
    failed batch stop the ones after it. Resumable because the file is the state, so a run that dies
    halfway is re-run with the same command and costs only what it had not already done.

    Concurrent because it has to be. A batch takes two to three minutes whatever its size -- the
    time is output tokens, not overhead -- so the whole lexicon run is a day and a half in series and
    a few hours at eight workers. The file is appended under a lock and a row names its own batch,
    so the ordering of the file means nothing and does not have to.

    What counts as answered is every number of a batch having a row, not the batch having been
    called. A model that returns seven of eight entries leaves the eighth with nothing to say it is
    missing, and a resume that trusts the batch name never comes back for it -- one silently absent
    entry in fourteen thousand is exactly the failure nobody notices.
    """
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)

    answered = set()
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            answered = {json.loads(line)['strong_number'] for line in handle if line.strip()}

    run = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    system = system_of(manifest)
    expected_of = expected_of or (lambda entry: entry['numbers'])
    pending = [e for e in manifest['batches']
               if args.again or any(n not in answered for n in expected_of(e))]
    lock = threading.Lock()
    started = time.time()
    spent, called, failures, written = 0.0, 0, [], 0

    def note(line):
        with lock:
            print(line, flush=True)

    def one(entry):
        try:
            translate(entry)
        except Exception as broken:
            # One batch is one batch. A run of 1,775 that dies because the 900th raised has thrown
            # away four hours of the other 899, and pool.map propagates by default.
            with lock:
                failures.append((entry['batch'], f'{type(broken).__name__}: {broken}'))
                print(f'{entry["batch"]}: {type(broken).__name__}: {broken}', flush=True)

    def translate(entry):
        nonlocal spent, called, written
        name = entry['batch']
        payload = payload_of(args, manifest, name)
        if payload is None:
            return
        outcome, failed = call_with_retries(
            json.dumps(payload, ensure_ascii=False, indent=1), system, args.model,
            lambda why, name=name: note(f'{name}: {why}'))
        if failed:
            with lock:
                failures.append((name, failed))
                print(f'{name}: {failed}', flush=True)
            return

        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        answers = parse(outcome.get('result'))
        if answers is None:
            with lock:
                failures.append((name, 'no JSON array in the reply'))
                print(f'{name}: no JSON array in the reply', flush=True)
            return

        rows, seen = [], set()
        for answer in answers:
            number = answer.get('strong_number')
            if number in seen:
                continue
            seen.add(number)
            row = row_of(payload, answer, name, model, run)
            if row is not None:
                rows.append(row)
        missing = [n for n in expected_of(entry) if n not in seen]

        with lock:
            spent += outcome.get('total_cost_usd') or 0.0
            called += 1
            written += len(rows)
            if missing:
                failures.append((name, f'{len(missing)} unanswered: {", ".join(missing[:5])}'))
            with open(path, 'a', encoding='utf-8') as handle:
                for row in rows:
                    handle.write(json.dumps(row, ensure_ascii=False) + '\n')
            elapsed = time.time() - started
            left = (elapsed / called) * (len(pending) - called)
            print(f'{name}: {len(rows)} rows | {called}/{len(pending)} batches, '
                  f'{written} rows, {elapsed / 60:.0f}m elapsed, ~{left / 60:.0f}m left, '
                  f'${spent:.3f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(one, pending))

    print(f'{called} calls, ${spent:.3f}, {(time.time() - started) / 60:.0f}m, '
          f'{len(failures)} failures', flush=True)
    for name, why in failures:
        print(f'  {name}: {why}')


def ask(args):
    def system_of(manifest):
        table = GLOSSARY.get(manifest['language'])
        if not table:
            raise SystemExit(
                f"no glossary for {manifest['language_name']}. Strong's fixed qualifiers -- "
                "by implication, properly, of uncertain affinity -- have to be given their settled "
                f"equivalents before a {manifest['language_name']} run: the Ukrainian pilot lost "
                "four fifths of its terminology failures to guessing three of them. Add a "
                f"GLOSSARY['{manifest['language']}'] in this file.")
        return SYSTEM_PROMPT.format(
            language=manifest['language_name'],
            kjv_rule=KJV_RULE,
            untranslated=UNTRANSLATED_TERMS,
            glossary='\n'.join(f'  {english:<24} {target}' for english, target in table))

    def payload_of(args, manifest, name):
        with open(os.path.join(args.dir, 'batches', name + '.json'), encoding='utf-8') as handle:
            return json.load(handle)

    def row_of(payload, answer, name, model, run):
        number = answer.get('strong_number')
        given = {e['strong_number']: e for e in payload['entries']}
        if number not in given:
            return None
        row = {
            'strong_number': number,
            'language': payload['language'],
            'batch': name,
            'prompt_version': payload['prompt_version'],
            'model': model,
            'run': run,
            'method': 'model-translation',
            'uncertain': answer.get('uncertain') or [],
        }
        for field in PROSE:
            if given[number].get(field):
                row[field] = answer.get(field)
        return row

    run_batches(args, os.path.join(args.dir, 'answers.jsonl'), system_of, payload_of, row_of)


# ---------------------------------------------------------------------------- check


STRONG_REF = re.compile(r'\b([GH]\d{1,4})\b')
SENSE_NUMBER = re.compile(r'^\s*(\d+[a-z]?)\)', re.M)
ORIGINAL_SCRIPT = re.compile(r'[֐-׿Ͱ-Ͽἀ-῿]+')
LATIN_WORD = re.compile(r'\b[A-Za-z]{4,}\b')
BRACKETED = re.compile(r'\([^)]*\)|\[[^\]]*\]')

# Below this many letters a language test says nothing. `1) (Qal) to spin` is four words, one of
# which has to stay in Latin because it is the name of a Hebrew stem, and a share test over it
# reports a correct answer as being in the wrong language.
ENOUGH_TO_JUDGE_LANGUAGE = 40


def cyrillic_share(text):
    letters = [c for c in text if c.isalpha()]
    if not letters:
        return 1.0
    return sum(1 for c in letters if 'Ѐ' <= c <= 'ӿ') / len(letters)


def renderings(text):
    """
    How many renderings a King James list actually holds. Splitting on every comma counts
    `(cast, gather out, throw) stone(-s)` as four, because Strong's affix notation writes one
    rendering's alternatives inside the brackets -- so a translation that correctly gives one
    Ukrainian verb reads as having lost three of them. Only the commas outside the brackets separate.
    """
    return len([p for p in BRACKETED.sub('', text).split(',') if p.strip()])


def failures_of(source, answer, language):
    """
    The failures that are certain, so they never have to be argued about. Everything here is a
    property of the two strings; nothing here is a judgement about meaning.
    """
    found = []
    for field in PROSE:
        english = source.get(field)
        if not english:
            continue
        translated = answer.get(field)
        if not translated or not translated.strip():
            found.append((field, 'missing', 'the field was given and came back empty'))
            continue
        if translated.strip() == english.strip():
            found.append((field, 'untranslated', 'returned the English unchanged'))
            continue

        want = set(STRONG_REF.findall(english))
        got = set(STRONG_REF.findall(translated))
        if want - got:
            found.append((field, 'lost-reference', 'dropped ' + ', '.join(sorted(want - got))))
        if got - want:
            found.append((field, 'invented-reference', 'added ' + ', '.join(sorted(got - want))))

        want_script = ORIGINAL_SCRIPT.findall(english)
        got_script = ORIGINAL_SCRIPT.findall(translated)
        if want_script and len(got_script) < len(want_script):
            found.append((field, 'lost-original-script',
                          f'{len(want_script)} quoted words in, {len(got_script)} out'))

        if field == 'detailed_definition':
            want_senses = SENSE_NUMBER.findall(english)
            got_senses = SENSE_NUMBER.findall(translated)
            if want_senses != got_senses:
                found.append((field, 'sense-numbering',
                              f'{len(want_senses)} senses in, {len(got_senses)} out'))
        if field == 'kjv_definition':
            # Only a collapse is a failure. Dropping the affix notation makes a translated list
            # legitimately shorter or longer than the English, so the test is for a list that lost
            # most of what it was given, not for one that does not match.
            want_items, got_items = renderings(english), renderings(translated)
            if want_items >= 4 and got_items * 2 < want_items:
                found.append((field, 'renderings-collapsed',
                              f'{want_items} renderings in, {got_items} out'))

        if language == 'uk':
            stripped = BRACKETED.sub('', ORIGINAL_SCRIPT.sub('', STRONG_REF.sub('', translated)))
            if (len([c for c in stripped if c.isalpha()]) >= ENOUGH_TO_JUDGE_LANGUAGE
                    and cyrillic_share(stripped) < 0.7):
                leftover = LATIN_WORD.findall(stripped)
                found.append((field, 'not-in-language',
                              'mostly Latin letters: ' + ', '.join(leftover[:6])))
    return found


def check(args):
    with open(os.path.join(args.dir, 'source.json'), encoding='utf-8') as handle:
        source = {e['strong_number']: e for e in json.load(handle)}
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        language = json.load(handle)['language']

    rows = read(os.path.join(args.dir, 'answers.jsonl'))
    out = []
    for row in rows:
        for field, kind, detail in failures_of(source[row['strong_number']], row, language):
            out.append({'strong_number': row['strong_number'], 'field': field,
                        'kind': kind, 'detail': detail})
    with open(os.path.join(args.dir, 'failures.json'), 'w', encoding='utf-8') as handle:
        json.dump(out, handle, ensure_ascii=False, indent=1)

    clean = len(rows) - len({f['strong_number'] for f in out})
    print(f'{len(rows)} answered, {clean} with no structural failure, {len(out)} failures')
    counts = {}
    for failure in out:
        counts[failure['kind']] = counts.get(failure['kind'], 0) + 1
    for kind, count in sorted(counts.items(), key=lambda p: -p[1]):
        print(f'  {kind}: {count}')


# ---------------------------------------------------------------------------- judge


def judge(args):
    with open(os.path.join(args.dir, 'source.json'), encoding='utf-8') as handle:
        source = {e['strong_number']: e for e in json.load(handle)}
    answers = {r['strong_number']: r for r in read(os.path.join(args.dir, 'answers.jsonl'))}

    def system_of(manifest):
        return JUDGE_PROMPT.format(language=manifest['language_name'])

    def payload_of(args, manifest, name):
        with open(os.path.join(args.dir, 'batches', name + '.json'), encoding='utf-8') as handle:
            batch = json.load(handle)
        pairs = []
        for shown in batch['entries']:
            number = shown['strong_number']
            if number not in answers:
                continue
            pair = {'strong_number': number, 'lemma': shown.get('lemma'),
                    'transliteration': shown.get('transliteration'), 'english': {}, 'translated': {}}
            for field in PROSE:
                if source[number].get(field):
                    pair['english'][field] = source[number][field]
                    pair['translated'][field] = answers[number].get(field)
            pairs.append(pair)
        if not pairs:
            return None
        return {'batch': name, 'language': batch['language'],
                'prompt_version': JUDGE_VERSION, 'entries': pairs}

    def row_of(payload, answer, name, model, run):
        number = answer.get('strong_number')
        if number not in answers:
            return None
        return {
            'strong_number': number,
            'terminology': answer.get('terminology'),
            'completeness': answer.get('completeness'),
            'fluency': answer.get('fluency'),
            'errors': answer.get('errors') or [],
            'batch': name,
            'prompt_version': JUDGE_VERSION,
            'judge_model': model,
            'run': run,
            'method': 'model-judgement',
        }

    def expected_of(entry):
        # An entry that was never translated can never be judged, so it must not keep its batch
        # looking unfinished for ever.
        return [n for n in entry['numbers'] if n in answers]

    run_batches(args, os.path.join(args.dir, 'judgements.jsonl'),
                system_of, payload_of, row_of, expected_of)


# ---------------------------------------------------------------------------- score


def read(path):
    """
    The rows of a JSONL, one per entry, latest wins. Appending is what makes a run resumable, so a
    re-run with --again and two workers that happened to take the same batch both leave a second row
    for an entry -- and every reader here wants one answer per entry, not the history of them.
    """
    if not os.path.exists(path):
        return []
    with open(path, encoding='utf-8') as handle:
        rows = {}
        for line in handle:
            if line.strip():
                row = json.loads(line)
                rows[row['strong_number']] = row
    return list(rows.values())


ACCEPTABLE = 3


def score(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    answers = read(os.path.join(args.dir, 'answers.jsonl'))
    judged = read(os.path.join(args.dir, 'judgements.jsonl'))
    failures_path = os.path.join(args.dir, 'failures.json')
    if not os.path.exists(failures_path):
        raise SystemExit(f'no {failures_path}: run `check --dir {args.dir}` first.')
    with open(failures_path, encoding='utf-8') as handle:
        failures = json.load(handle)

    asked = manifest['entries']
    structural = {f['strong_number'] for f in failures}
    axes = ('terminology', 'completeness', 'fluency')
    scored = [j for j in judged if all(isinstance(j.get(a), int) for a in axes)]

    def mean(axis):
        return sum(j[axis] for j in scored) / len(scored) if scored else 0.0

    def share(axis):
        return sum(1 for j in scored if j[axis] >= ACCEPTABLE) / len(scored) if scored else 0.0

    usable = [j for j in scored
              if all(j[a] >= ACCEPTABLE for a in axes)
              and j['strong_number'] not in structural]

    kinds = {}
    for failure in failures:
        kinds[failure['kind']] = kinds.get(failure['kind'], 0) + 1

    lines = [
        f'# {manifest["language_name"]} lexicon'
        + (f' -- a sample of {manifest["sample"]}' if manifest.get('sample') else ' -- the whole of it'),
        '',
        f'- selected: {asked} entries, seed {manifest["seed"]}, prompt {manifest["prompt_version"]}',
        f'- translated: {len(answers)}',
        f'- judged: {len(scored)}',
        '',
        '## Structural checks, which are deterministic',
        '',
        f'- entries with no structural failure: {asked - len(structural)} / {asked}'
        f' ({(asked - len(structural)) / asked:.1%})',
    ]
    for kind, count in sorted(kinds.items(), key=lambda p: -p[1]):
        lines.append(f'- {kind}: {count}')
    lines += [
        '',
        '## A second model reading, which is a judgement',
        '',
        '| axis | mean | at least 3 |',
        '|---|---|---|',
    ]
    for axis in axes:
        lines.append(f'| {axis} | {mean(axis):.2f} | {share(axis):.1%} |')
    lines += [
        '',
        f'**Usable: {len(usable)} / {len(scored)} judged ({len(usable) / len(scored):.1%})** '
        '-- every axis at 3 or better and no structural failure.'
        if scored else '**Nothing judged.**',
        '',
        '## The worst of it',
        '',
    ]
    worst = sorted(scored, key=lambda j: min(j[a] for a in axes))[:15]
    for judgement in worst:
        errors = judgement['errors']
        detail = errors[0].get('problem') if errors else ''
        lines.append(
            f'- {judgement["strong_number"]}: '
            f'terminology {judgement["terminology"]}, completeness {judgement["completeness"]}, '
            f'fluency {judgement["fluency"]} -- {detail}')

    with open(os.path.join(args.dir, 'score.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


# ---------------------------------------------------------------------------- publish


PUBLISHED = 'Resources/Essenthos/lexicon'

# Written beside the output every time, rather than by hand once. A source is credited whatever its
# licence permits, and the statement of its terms lives beside the bytes -- and this file is the
# awkward case both rules are really about, because the work being credited is partly ours. A
# reader has to be able to tell which half is Strong's and which half is a machine's, and
# the file that says so cannot be the one somebody forgets to write.
ATTRIBUTION = """\
# Strong's dictionary in {language_name} -- a machine translation, and whose is which

**This is not Strong's dictionary in {language_name}. It is one model's reading of Strong's
dictionary, rendered into {language_name} on a date, and it says so on every row.**

## The English underneath

*A Concise Dictionary of the Words in the Hebrew Bible* and *...in the Greek New Testament*, by
**James Strong, LL.D., S.T.D.**, Hunt & Eaton, 1890. **Public Domain**, declared in the OSIS header
of the files this was made from -- see `Resources/Strong/LICENCE.md`, which also records the one
part of those files that is *not* public domain and is not translated here: the TWOT reference on
6,070 Hebrew entries, under a 1980 Moody Bible Institute copyright. The TWOT reference is an
identifier and this file does not carry it.

Because Strong's own text is public domain, translating it needs nobody's permission. That is a
statement about the English and not about this file.

## This file

- **Produced by:** the Essenthos project, `scripts/lexicon.py`.
- **Method:** `model-translation` -- a language model, given the English fields and a glossary of
  Strong's fixed qualifiers, asked for {language_name}.
- **Model, prompt version and date:** on every row, in `model`, `prompt_version` and
  `translated_at`. They are not metadata about the file; they are part of the claim.
- **Not translated, because they are identifiers and not language:** the lemma, the transliteration,
  the pronunciation, the morphology code, the see-also numbers and the TWOT reference. A translated
  identifier breaks a lookup and nothing says it has.
- **Rendered rather than translated:** `kjv_definition`. The English is the list of words the King
  James actually uses, in Strong's affix notation; what is here is the senses those renderings
  carry. It does not point into the King James text and the English must be shown beside it.

## What a reader is owed

The English, next to it. A {language_name} gloss standing alone reads as Strong saying it, and
Strong did not say it -- a machine did, once, and nobody has checked that row.
"""


def publish(args):
    """
    A run's answers as a corpus file, one JSON object per line, with the provenance on every row.

    The provenance is not decoration. Strong's English is public domain and this is not Strong's
    English -- it is one model's reading of it on one date under one prompt, and a row that does not
    say so is a machine's guess wearing a lexicographer's name. `method`, `model`, `prompt_version`
    and `translated_at` travel with the text so that whatever loads it can put them in front of a
    reader, and so a later run can be told from this one.
    """
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    rows = read(os.path.join(args.dir, 'answers.jsonl'))
    if args.clean:
        with open(os.path.join(args.dir, 'failures.json'), encoding='utf-8') as handle:
            failed = {f['strong_number'] for f in json.load(handle)}
        rows = [r for r in rows if r['strong_number'] not in failed]

    os.makedirs(os.path.dirname(args.out) or '.', exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as handle:
        for row in sorted(rows, key=lambda r: (r['strong_number'][0], int(r['strong_number'][1:]))):
            published = {
                'strong_number': row['strong_number'],
                'language': CORPUS_LANGUAGES[manifest['language']],
                'method': row['method'],
                'model': row['model'],
                'prompt_version': row['prompt_version'],
                'translated_at': row['run'],
            }
            for field in PROSE:
                if row.get(field):
                    published[field] = row[field]
            # The only doubt the run produces per row, and it is the translator's own rather than a
            # rule's, so it travels with the text instead of being turned into a score.
            if row.get('uncertain'):
                published['uncertain'] = row['uncertain']
            handle.write(json.dumps(published, ensure_ascii=False) + '\n')

    licence = os.path.join(os.path.dirname(args.out) or '.', 'LICENCE.md')
    with open(licence, 'w', encoding='utf-8') as handle:
        handle.write(ATTRIBUTION.format(language_name=manifest['language_name']))
    print(f'{len(rows)} rows -> {args.out}, attribution -> {licence}')


# ---------------------------------------------------------------------------- cli


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    sub = parser.add_subparsers(dest='command', required=True)

    one = sub.add_parser('extract', help='write the prompts')
    one.add_argument('--out', dest='dir', required=True)
    one.add_argument('--language', default='uk', choices=sorted(LANGUAGES))
    one.add_argument('--numbers', nargs='*')
    one.add_argument('--sample', type=int)
    one.add_argument('--seed', type=int, default=11)
    one.set_defaults(run=extract)

    two = sub.add_parser('ask', help='translate every unanswered batch')
    two.add_argument('--dir', required=True)
    two.add_argument('--model', default='sonnet')
    two.add_argument('--again', action='store_true')
    two.add_argument('--workers', type=int, default=6)
    two.set_defaults(run=ask)

    fresh = sub.add_parser('sample', help="a finished run's answers, narrowed to a fresh sample")
    fresh.add_argument('--from', dest='source', required=True, help='a directory `ask` has finished')
    fresh.add_argument('--out', dest='dir', required=True)
    fresh.add_argument('--size', type=int, default=500)
    fresh.add_argument('--seed', type=int, default=23)
    fresh.set_defaults(run=sample)

    three = sub.add_parser('check', help='the deterministic failures')
    three.add_argument('--dir', required=True)
    three.set_defaults(run=check)

    four = sub.add_parser('judge', help='a second model reading of the pair')
    four.add_argument('--dir', required=True)
    four.add_argument('--model', default='sonnet')
    four.add_argument('--again', action='store_true')
    four.add_argument('--workers', type=int, default=6)
    four.set_defaults(run=judge)

    five = sub.add_parser('score', help='the report')
    five.add_argument('--dir', required=True)
    five.set_defaults(run=score)

    six = sub.add_parser('publish', help='the answers as a corpus file, with their provenance')
    six.add_argument('--dir', required=True)
    six.add_argument('--out', required=True, help=f'under {PUBLISHED}/ for a real run')
    six.add_argument('--clean', action='store_true', help='drop rows check found a failure in')
    six.set_defaults(run=publish)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
