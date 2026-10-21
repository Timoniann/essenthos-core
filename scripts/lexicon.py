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
from collections import Counter
import datetime
import hashlib
import json
import os
import random
import re
import shutil
import subprocess
import sys
import threading
import time
import unicodedata
import uuid

sys.stdout.reconfigure(encoding='utf-8')

# The rebuild's own Postgres. The frozen API's container still holds an older copy of this database
# under the same name, so a wrong container here answers every query and is wrong only in the data.
CONTAINER = os.environ.get('ESSENTHOS_DB_CONTAINER', 'essenthos-core-db-1')
DATABASE = 'essenthos_core'
USER = 'essenthos'
# Every session is read-only on the server's side, so a statement that would write fails instead.
READ_ONLY = 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=15000'

PROMPT_VERSION = 'lexicon-3'
PROMPT_VERSIONS = {'de': 'lexicon-de-codex-2', 'es': 'lexicon-es-codex-2'}
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
PERMANENT_UNAVAILABLE = ('not logged in', 'authentication failed', 'invalid api key',
                         'model not found', 'model does not exist', 'not authorized',
                         'unauthorized', 'unknown model')

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
    'es': [
        ('i.e.', 'es decir'),
        ('figuratively', 'figuradamente'),
        ('a primitive root', 'una raíz primitiva'),
        ('by implication', 'en sentido derivado — a sense implicit in or derived from the preceding '
                           'sense; not «de ahí» (= hence), «figuradamente» (= figuratively), '
                           'or «por extensión» (= by extension)'),
        ('properly', 'propiamente'),
        ('literally', 'literalmente'),
        ('an unused root', 'una raíz no usada — do not strengthen this to unattested in every period'),
        ('specially', 'en particular'),
        ('the base of', 'la base de — the lexical or etymological base of the referenced word'),
        ('compare', 'compárese'),
        ('of Hebrew origin', 'de origen hebreo'),
        ('causative', 'forma causativa'),
        ('middle voice', 'voz media'),
        ('by analogy', 'por analogía'),
        ('denominative', 'denominativo (derivado de un nombre)'),
        ('akin to', 'emparentado con'),
        ('by extension', 'por extensión'),
        ('hence', 'de ahí'),
        ('adverbially', 'adverbialmente'),
        ('of foreign origin', 'de origen extranjero'),
        ('patronymic', 'patronímico'),
        ('of uncertain affinity', 'de parentesco lingüístico incierto'),
        ('a primary word', 'una palabra primaria'),
        ('intensive', 'forma intensiva'),
        ('a variation of', 'una variante de'),
        ('by reduplication', 'por reduplicación (duplicación)'),
        ('contracted', 'forma contraída'),
        ('diminutive', 'forma diminutiva'),
        ('prolongation, prolonged', 'forma alargada'),
        ('collateral form', 'forma paralela'),
        ('by Hebraism', 'por hebraísmo'),
        ('cognate', 'cognado (palabra emparentada)'),
        ('gentilic', 'gentilicio (nombre de pueblo o habitante)'),
    ],
    'de': [
        ('i.e.', 'd. h.'),
        ('figuratively', 'bildlich'),
        ('a primitive root', 'eine ursprüngliche Wurzel'),
        ('by implication', 'in abgeleiteter Bedeutung — a sense implicit in or derived from the '
                           'preceding sense; never «daher» (= hence), «bildlich» (= figuratively), '
                           'or «in erweiterter Bedeutung» (= by extension)'),
        ('properly', 'eigentlich'),
        ('literally', 'wörtlich'),
        ('an unused root', 'eine nicht gebrauchte Wurzel — do not expand this into a claim that '
                           'the root is unattested in every language or period'),
        ('specially', 'insbesondere'),
        ('the base of', 'die Basis von — the lexical or etymological base of the referenced word'),
        ('compare', 'vgl.'),
        ('of Hebrew origin', 'hebräischen Ursprungs'),
        ('causative', 'kausative Form (Kausativ)'),
        ('middle voice', 'Medium'),
        ('by analogy', 'nach Analogie'),
        ('denominative', 'denominativ (von einem Nomen abgeleitet)'),
        ('akin to', 'verwandt mit'),
        ('by extension', 'in erweiterter Bedeutung'),
        ('hence', 'daher'),
        ('adverbially', 'adverbial'),
        ('of foreign origin', 'fremdsprachigen Ursprungs'),
        ('patronymic', 'Patronymikon (Vatersname); adjectivally: patronymisch'),
        ('of uncertain affinity', 'von unklarer sprachlicher Verwandtschaft'),
        ('a primary word', 'ein Grundwort'),
        ('intensive', 'Intensivform'),
        ('a variation of', 'eine Variante von'),
        ('by reduplication', 'durch Reduplikation (Verdoppelung)'),
        ('contracted', 'kontrahierte Form'),
        ('diminutive', 'Diminutivform (Verkleinerungsform)'),
        ('prolongation, prolonged', 'verlängerte Form'),
        ('collateral form', 'Nebenform'),
        ('by Hebraism', 'nach hebräischem Sprachgebrauch (Hebraismus)'),
        ('cognate', 'verwandtes Wort'),
        ('gentilic', 'Gentilizium (Volks- oder Einwohnerbezeichnung); adjectivally: gentilisch'),
    ],
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
        ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', '-e', READ_ONLY, CONTAINER,
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
        'prompt_version': PROMPT_VERSIONS.get(args.language, PROMPT_VERSION),
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
            'prompt_version': manifest['prompt_version'],
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


def call(prompt, system, model, effort='medium', record_dir=None, provenance=None):
    """
    One batch, one call, one turn, no tools and no session. The flags are what turn the CLI into a
    worker rather than an agent; --bare looks like the right one and is not, because it reads auth
    strictly from an API key and never from the subscription.
    """
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', system, '--model', model,
        '--effort', effort,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--tools', '',
        '--max-turns', '1',
    ]
    started = time.time()
    record = {'started_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'requested_model': model, 'effort': effort, 'provenance': provenance,
              'prompt': prompt, 'system': system,
              'prompt_sha256': hashlib.sha256(prompt.encode('utf-8')).hexdigest(),
              'system_sha256': hashlib.sha256(system.encode('utf-8')).hexdigest()}

    def finish(outcome, why, stdout='', stderr='', returncode=None):
        record.update(elapsed_seconds=time.time() - started, stdout=stdout, stderr=stderr,
                      returncode=returncode, error=why, outcome=outcome)
        if record_dir:
            os.makedirs(record_dir, exist_ok=True)
            target = os.path.join(record_dir, uuid.uuid4().hex + '.json')
            with open(target, 'w', encoding='utf-8') as handle:
                json.dump(record, handle, ensure_ascii=False, indent=1)
        return (None if why else outcome), why

    def decode(value):
        return value.decode('utf-8', 'replace') if isinstance(value, bytes) else value or ''

    try:
        process = subprocess.run(
            command, input=prompt.encode('utf-8'),
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=CALL_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired as broken:
        return finish(None, f'the call did not return within {CALL_TIMEOUT_SECONDS}s and was killed',
                      decode(broken.stdout), decode(broken.stderr))
    except OSError as broken:
        return finish(None, f'{type(broken).__name__}: {broken}')
    stdout, stderr = decode(process.stdout), decode(process.stderr)
    try:
        outcome = json.loads(stdout)
    except json.JSONDecodeError as broken:
        return finish(None, f'the harness did not return JSON (exit {process.returncode}): '
                      f'{broken}; {stderr[:400]}', stdout, stderr, process.returncode)
    if not isinstance(outcome, dict):
        return finish(outcome, 'the harness returned a non-object outcome', stdout, stderr,
                      process.returncode)
    if process.returncode != 0:
        return finish(outcome, stderr.strip() or str(outcome.get('result'))[:400]
                      or f'the harness exited {process.returncode}', stdout, stderr,
                      process.returncode)
    if outcome.get('is_error'):
        said = str(outcome.get('result') or outcome.get('subtype') or 'no reason given')
        return finish(outcome, f'the model reported an error: {said[:400]}', stdout, stderr,
                      process.returncode)
    return finish(outcome, None, stdout, stderr, process.returncode)


def rate_limited(why):
    lowered = (why or '').lower()
    return any(marker in lowered for marker in RATE_LIMITED)


def call_with_retries(prompt, system, model, say, effort='medium', record_dir=None,
                      provenance=None):
    """
    The same call, waited out rather than given up on. A rate limit is a wait, not a failure: it
    clears by itself and the alternative is a five-hour run stopping an hour in with nothing wrong.
    Everything else gets one retry, because a transient network fault looks the same as a permanent
    one from here and re-asking is cheaper than re-running the whole batch tomorrow.
    """
    for attempt, wait in enumerate(RETRY_WAITS + (None,)):
        outcome, why = call(prompt, system, model, effort=effort, record_dir=record_dir,
                            provenance={**(provenance or {}), 'attempt': attempt + 1})
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

    translating = os.path.basename(path) == 'answers.jsonl'
    with open(os.path.join(args.dir, 'source.json'), encoding='utf-8') as handle:
        sources = {e['strong_number']: e for e in json.load(handle)}
    axes = ('terminology', 'completeness', 'fluency')

    def valid(row):
        number = row.get('strong_number')
        if number not in sources:
            return False
        if translating:
            return not failures_of(sources[number], row, manifest['language'])
        return (all(type(row.get(a)) is int and 1 <= row[a] <= 5 for a in axes)
                and isinstance(row.get('errors'), list))

    answered = {row['strong_number'] for row in read(path) if valid(row)}

    run = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    invocation = uuid.uuid4().hex
    record_dir = os.path.join(args.dir, 'calls', invocation)
    system = system_of(manifest)
    expected_of = expected_of or (lambda entry: entry['numbers'])
    pending = [e for e in manifest['batches']
               if args.again or any(n not in answered for n in expected_of(e))]
    lock = threading.Lock()
    stop_unsent = threading.Event()
    started = time.time()
    spent, called, failures, written = 0.0, 0, [], 0
    skipped = 0

    def note(line):
        with lock:
            print(line, flush=True)

    def one(entry):
        nonlocal skipped
        if stop_unsent.is_set() or os.path.exists(os.environ.get('ESSENTHOS_LEXICON_STOP_FILE', '')):
            with lock:
                skipped += 1
            return
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
        if not args.again:
            payload = {**payload, 'entries': [e for e in payload['entries']
                                            if e['strong_number'] not in answered]}
        expected = {e['strong_number'] for e in payload['entries']}
        if not expected:
            return
        outcome, failed = call_with_retries(
            json.dumps(payload, ensure_ascii=False, indent=1), system, args.model,
            lambda why, name=name: note(f'{name}: {why}'),
            effort=getattr(args, 'effort', 'medium'), record_dir=record_dir,
            provenance={'batch': name, 'phase': 'ask' if translating else 'judge',
                        'prompt_version': payload['prompt_version'], 'run': run})
        if failed:
            if rate_limited(failed) or any(marker in failed.lower()
                                          for marker in PERMANENT_UNAVAILABLE):
                stop_unsent.set()
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

        rows, seen, invalid = [], set(), []
        duplicates = Counter(a.get('strong_number') for a in answers if isinstance(a, dict))
        for answer in answers:
            if not isinstance(answer, dict):
                invalid.append('non-object answer')
                continue
            number = answer.get('strong_number')
            if not isinstance(number, str) or number not in expected:
                invalid.append(f'unexpected ID: {number}')
                continue
            if duplicates[number] != 1:
                invalid.append(f'duplicate ID: {number}')
                continue
            if translating and failures_of(sources[number], answer, manifest['language']):
                invalid.append(f'invalid fields: {number}')
                continue
            row = row_of(payload, answer, name, model, run)
            if row is not None and valid(row):
                seen.add(number)
                row['effort'] = getattr(args, 'effort', 'medium')
                rows.append(row)
            else:
                invalid.append(f'invalid row: {number}')
        missing = sorted(expected - seen)

        with lock:
            spent += outcome.get('total_cost_usd') or 0.0
            called += 1
            written += len(rows)
            if missing:
                failures.append((name, f'{len(missing)} unanswered: {", ".join(missing[:5])}'))
            if invalid:
                failures.append((name, '; '.join(invalid)))
            with open(path, 'a', encoding='utf-8') as handle:
                for row in rows:
                    handle.write(json.dumps(row, ensure_ascii=False) + '\n')
            elapsed = time.time() - started
            left = (elapsed / called) * (len(pending) - called)
            cost_label = (f'${spent:.3f}' if outcome.get('total_cost_usd') is not None
                          else 'cost unavailable')
            print(f'{name}: {len(rows)} rows | {called}/{len(pending)} batches, '
                  f'{written} rows, {elapsed / 60:.0f}m elapsed, ~{left / 60:.0f}m left, '
                  f'{cost_label}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(one, pending))

    records = []
    if os.path.isdir(record_dir):
        for filename in os.listdir(record_dir):
            with open(os.path.join(record_dir, filename), encoding='utf-8') as handle:
                records.append(json.load(handle))
        costs = [r['outcome']['total_cost_usd'] for r in records
                 if isinstance(r.get('outcome'), dict)
                 and isinstance(r['outcome'].get('total_cost_usd'), (int, float))]
        spent = sum(costs) if costs else None
    usage = Counter()
    for record in records:
        for key, value in ((record.get('outcome') or {}).get('usage') or {}).items():
            if isinstance(value, int):
                usage[key] += value
    summary = {'invocation': invocation, 'phase': 'ask' if translating else 'judge',
               'calls': len(records), 'total_cost_usd': spent, 'usage': dict(usage),
               'elapsed_seconds': time.time() - started, 'rows_written': written,
               'skipped_batches': skipped,
               'failures': failures, 'requested_model': args.model,
               'effort': getattr(args, 'effort', 'medium')}
    os.makedirs(os.path.join(args.dir, 'calls'), exist_ok=True)
    with open(os.path.join(args.dir, 'calls', invocation + '-summary.json'), 'w',
              encoding='utf-8') as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=1)
    cost_label = f'${spent:.3f}' if spent is not None else 'cost unavailable'
    print(f'{len(records)} calls, {cost_label}, {(time.time() - started) / 60:.0f}m, '
          f'{len(failures)} failures', flush=True)
    for name, why in failures:
        print(f'  {name}: {why}')
    if skipped:
        print(f'  {skipped} unsent batches checkpointed after quota/authentication/model failure')
    if failures or skipped:
        raise SystemExit(1)


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
        template = SYSTEM_PROMPT
        if manifest['language'] == 'es':
            template += '''
Use standard Spanish reference-work prose, with all required written accents.
Use the part-of-speech code and complete source sentence to distinguish nouns,
participles, adjectives and verbs. Preserve who acts and who receives the action.
"Servants/worshippers (of God)" means "siervos/adoradores de Dios", humans serving
or worshipping God. By contrast "to turn away (of God)" can describe God's action,
"apartarse (dicho de Dios)". Never infer the actor from "of God" alone.

Reconstruct shared stems and affixes before translating KJV rendering lists.
"(be, make) clean" includes being clean and making clean; "afflicted(-ion)"
includes both the adjective and noun. A shared "again" applies to every action
it governs. Preserve all senses in order, expanding fragments into whole Spanish
words or phrases. Remove English notation only after preserving its meaning.
These are Spanish explanations of KJV senses, not quotations from Reina-Valera.
Read archaic English in its historical context. A KJV "let" can mean hinder or
prevent (impedir, estorbar), rather than allow (permitir). Determine which sense
the source entry carries; do not replace every occurrence of permitir blindly.

Keep historical lexical claims and uncertainty, without silently correcting them.
Use established Spanish biblical names when the entry identifies them clearly.
Do not drop accents: Alpha -> alfa; amen -> amén; Aaron -> Aarón; Jesus -> Jesús.
Copy every Strong-reference digit, including leading zeros, and every hierarchical
sense label; preserve quoted Hebrew/Greek under Unicode canonical equivalence.
'''
        if manifest['language'] == 'de':
            template += '''
For German, use modern standard reference-work prose and German noun capitalization.
Keep the source's historical lexical claims and uncertainty; do not silently modernize
its etymology or correct it using your own knowledge.

Use established German biblical forms of personal and place names only when the
identity is clear from the entry. Otherwise preserve the supplied name or transliterate
it consistently. Translate a quoted meaning of a name separately from the name itself.
Never merge namesakes or replace the name with its etymological meaning.

The KJV field is a German explanation of English KJV renderings. It is not a list of
renderings attested in Luther, Elberfelder, or another German Bible. Translate every
listed historical sense in order, even when two different English items need the same
German equivalent. Do not replace the list with preferred German Bible vocabulary.
Interpret archaic English contextually: conversation can concern conduct, meat can
mean food, quick can mean living, and prevent can mean precede. Do not automatically
use their modern everyday meanings. Archaic "let" can mean hinder/prevent (hindern,
hemmen) rather than allow (erlauben); determine its sense in the source entry.

Expand a shared modifier across every rendering it governs. For example, a leading
"again" governing break/build/circumcise/etc. must yield repeated-action senses
such as "wieder brechen, wieder bauen, wieder beschneiden", not bare action words.
Expand suffix fragments into whole German words: write "Göttin, göttlich" rather
than "(-göttin, -göttlich)". Keep the distinct source senses and their order.

Preserve grammatical roles in parenthetical usage labels. An English label such
as "to turn away (of God)" describes God as the subject; render it, for example,
as "sich abwenden (von Gott ausgesagt)", not an action directed away from God.
Where the source is ambiguous, preserve its ambiguity and mark uncertainty.
This is not a rule that every "of God" identifies a subject. After a noun such as
"servants, worshippers (of God)", God is the one served or worshipped: write
"Diener, Verehrer Gottes". Determine roles from the governing word and context;
never turn this noun phrase into a claim that God is a servant or worshipper.

Copy each Strong reference with every digit, including leading zeros: H05664
must not become H5664. Unicode-canonically equivalent original-script letters
are acceptable; never change their letters, accent content or quoted wording.
Use grammatical German, consistent paired quotation marks and established
German biblical place names where the identification is explicit.
'''
        if manifest['language'] in ('de', 'es'):
            template = template.replace(
                '4. Bracketed markers that are notation rather than language: [idiom], and a leading X or + in a\n'
                '   King James rendering list.\n'
                '5. The order and the count of the comma-separated items in a rendering list.',
                '4. In King James rendering lists preserve every sense and their order, but drop\n'
                '   English affix notation, including X, + and [idiom], as the field rule says.\n'
                '5. Proper names and established loanwords may keep an identical {language} spelling;\n'
                '   translate all surrounding English prose and qualifiers.')
        return template.format(
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


STRONG_REF = re.compile(r'\b([GH]\d+)\b')
SENSE_NUMBER = re.compile(r'^\s*(\d+(?:[a-z]\d*)*)\)', re.M)
INLINE_SENSE_NUMBER = re.compile(r'(?<!\w)(\d+[a-z]\d+(?:[a-z]\d*)*)\)')
ORIGINAL_SCRIPT = re.compile(r'[֐-׿Ͱ-Ͽἀ-῿]+')
LATIN_WORD = re.compile(r'\b[A-Za-z]{4,}\b')
BRACKETED = re.compile(r'\([^)]*\)|\[[^\]]*\]')

# Below this many letters a language test says nothing. `1) (Qal) to spin` is four words, one of
# which has to stay in Latin because it is the name of a Hebrew stem, and a share test over it
# reports a correct answer as being in the wrong language.
ENOUGH_TO_JUDGE_LANGUAGE = 40
SHARED_GERMAN_WORDS = {'amen', 'alpha', 'omega', 'beta', 'delta', 'gamma', 'iota',
                       'myrrhe', 'halleluja', 'rabbi', 'messias', 'abba', 'satan',
                       'cherub', 'seraph', 'jesus', 'abraham', 'aaron', 'adam',
                       'david', 'daniel', 'israel', 'gabriel', 'michael', 'maria',
                       'lydia', 'anna', 'martha', 'philemon', 'abaddon', 'hades'}
SPANISH_NAMES = {'abraham', 'abram', 'isaac', 'israel', 'elam', 'aram',
                 'edom', 'moab', 'magog', 'tubal'}
SPANISH_GREEK_NAMES = {'G11': 'abraham', 'G2464': 'isaac', 'G2474': 'israel', 'G4590': 'sem'}
SPANISH_LOANWORDS = {'G5614': 'hosanna', 'G5598': 'omega', 'G2503': 'iota'}
# Exact reviewed fields; a changed source, answer, field or entry loses its allowance.
REVIEWED_GERMAN_FIELDS = {
    ('H1213', 'kjv_definition'): ('28e1b11a83e25012d814c10005d50af5ca71bef03dbc3825fae5ae34192f8e5c', '28e1b11a83e25012d814c10005d50af5ca71bef03dbc3825fae5ae34192f8e5c', 'Exact supplied personal-name aliases Bazlith/Bazluth; surrounding German identity and name meaning are translated.'),
    ('H2566', 'kjv_definition'): ('9bc9daa8896b1e3af2b2251b82d4ef729e2c8fe39a2edd853d78f2618f3fda6b', '9bc9daa8896b1e3af2b2251b82d4ef729e2c8fe39a2edd853d78f2618f3fda6b', 'Exact source alias Amran; preserve source Chamran/Amran/Amram variation rather than harmonizing namesakes.'),
    ('H2695', 'kjv_definition'): ('00c0f85f2c09dc44b9381fc2e0f1d71abfb9f0afde29fd8472a015786ded3541', '00c0f85f2c09dc44b9381fc2e0f1d71abfb9f0afde29fd8472a015786ded3541', 'Exact source personal-name aliases Hezro/Hezrai; German warrior identity and enclosure gloss are translated.'),
    ('H6881', 'kjv_definition'): ('58848fdc7a51a0b41dbb11710773172dfcd2bb66d3f4945b8feec83202dec05d', '58848fdc7a51a0b41dbb11710773172dfcd2bb66d3f4945b8feec83202dec05d', 'Exact supplied place-name aliases; German Zora identity, hornet gloss and Dan/Manoah/Simson account are translated.'),
    ('G10', 'kjv_definition'): ('7d114e05e9995e0d38ca2828a5835efef4dd6a03072f4951e28f280e5fb9e334', '7d114e05e9995e0d38ca2828a5835efef4dd6a03072f4951e28f280e5fb9e334', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G13', 'kjv_definition'): ('262e1648b8d2997e77e249ca55b9eb24666e32af2f83bc0c0373346f771ff6fa', '262e1648b8d2997e77e249ca55b9eb24666e32af2f83bc0c0373346f771ff6fa', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G99', 'kjv_definition'): ('1d8b4e57e8418a34f69225090c64cc90dd826ca352c9674bc1bab4bc7b295f7a', '1d8b4e57e8418a34f69225090c64cc90dd826ca352c9674bc1bab4bc7b295f7a', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G223', 'kjv_definition'): ('70944dbf24d5ed9fa4f74f2c993efd9f3395cb20b6d67eb5d0854737afb1adcc', '70944dbf24d5ed9fa4f74f2c993efd9f3395cb20b6d67eb5d0854737afb1adcc', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G708', 'kjv_definition'): ('ee5af745aae3a409704e033a9bab49c03cacb00aea7ea0ff6714410de4dbda08', 'ee5af745aae3a409704e033a9bab49c03cacb00aea7ea0ff6714410de4dbda08', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G711', 'kjv_definition'): ('129c1ca9e45f907cf7d3bf495ef47b782861194a4d243379e7d84114eda1efd6', '129c1ca9e45f907cf7d3bf495ef47b782861194a4d243379e7d84114eda1efd6', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G745', 'kjv_definition'): ('2d87e59faf5b2f4e5415db516187ea83f6cbd99fee2c525285b942e4e9e62b06', '2d87e59faf5b2f4e5415db516187ea83f6cbd99fee2c525285b942e4e9e62b06', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G751', 'kjv_definition'): ('deef870441693a91674f6ea18b75ffb569399e299b33f0f5d7d4c0147a8a3e1e', 'deef870441693a91674f6ea18b75ffb569399e299b33f0f5d7d4c0147a8a3e1e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G825', 'kjv_definition'): ('1066cfd11e259a57f08adfb231b9f5c21b313529d23d2defb56c985c9b0fdd5e', '1066cfd11e259a57f08adfb231b9f5c21b313529d23d2defb56c985c9b0fdd5e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G828', 'kjv_definition'): ('9ffc34d42b77fd5622592363a33679e446c5511f2b700b6fc88f1e35b5b0a995', '9ffc34d42b77fd5622592363a33679e446c5511f2b700b6fc88f1e35b5b0a995', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G883', 'kjv_definition'): ('b9cfdcdedd39806038804cae13786b97c23841fae03355ffca5068411e4446c1', 'b9cfdcdedd39806038804cae13786b97c23841fae03355ffca5068411e4446c1', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G885', 'kjv_definition'): ('a80820163761935964b72750944cf3a4e36e29661c7315e1ef588c60d2d3700e', 'a80820163761935964b72750944cf3a4e36e29661c7315e1ef588c60d2d3700e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G919', 'kjv_definition'): ('eb0648db7781bf4f175f71fe64a73b4e45953bb0a0fac2366876c92610fca877', 'eb0648db7781bf4f175f71fe64a73b4e45953bb0a0fac2366876c92610fca877', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G965', 'kjv_definition'): ('c606d534259bc537150e99a2fc3cd72f18b72c143884aa805f95080705266c40', 'c606d534259bc537150e99a2fc3cd72f18b72c143884aa805f95080705266c40', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1058', 'kjv_definition'): ('e1adcb36725709cd03712b360f7dc44e3e8cab4590373a3a40b5aff8384fbdbc', 'e1adcb36725709cd03712b360f7dc44e3e8cab4590373a3a40b5aff8384fbdbc', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1148', 'kjv_definition'): ('1bfd11efd847c3885209c8991cbb8548270d23259e5b28210ce1f0f42e041ea7', '1bfd11efd847c3885209c8991cbb8548270d23259e5b28210ce1f0f42e041ea7', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1324', 'kjv_definition'): ('5c230034136f9d066c46a70544fb59725f63736d04adc22d07bfb67e69f34cf1', '5c230034136f9d066c46a70544fb59725f63736d04adc22d07bfb67e69f34cf1', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1409', 'kjv_definition'): ('7dc3706f47cabfbbacc6c074e895b8d3e824bb5f9be6b2aec4fdba29d71558a6', '7dc3706f47cabfbbacc6c074e895b8d3e824bb5f9be6b2aec4fdba29d71558a6', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1662', 'kjv_definition'): ('60aa4315b100c691885dd61592e17d45f2ab8d81ba1340cf2bf7acccf2402309', '60aa4315b100c691885dd61592e17d45f2ab8d81ba1340cf2bf7acccf2402309', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G1664', 'kjv_definition'): ('6debd9b1481cee89f1f3f2a6a5083e779501568964a6251d697ac7be9d01923a', '6debd9b1481cee89f1f3f2a6a5083e779501568964a6251d697ac7be9d01923a', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2037', 'kjv_definition'): ('7208b21105d4c7047bead37c94ea99a59a1928e96df4e70d43dea1af61c65d0e', '7208b21105d4c7047bead37c94ea99a59a1928e96df4e70d43dea1af61c65d0e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2161', 'kjv_definition'): ('623f94d7cc12685321a1b386f6eabd909c3d92822de3245385f2d714cc62a31b', '623f94d7cc12685321a1b386f6eabd909c3d92822de3245385f2d714cc62a31b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2181', 'kjv_definition'): ('52bde4f1f5eea8a32953e079f9c314b45ae666bd6e4b4a3b040a741d8bc4b995', '52bde4f1f5eea8a32953e079f9c314b45ae666bd6e4b4a3b040a741d8bc4b995', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2243', 'kjv_definition'): ('4c948d2ecf11d1f7377b9293622d00ce4dd11c311c093bb011cda2b35290af9a', '4c948d2ecf11d1f7377b9293622d00ce4dd11c311c093bb011cda2b35290af9a', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2321', 'kjv_definition'): ('083bd253cffdde6a8d7e6a5d707f8dcafe06a30b8353503199113e52a8835d39', '083bd253cffdde6a8d7e6a5d707f8dcafe06a30b8353503199113e52a8835d39', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2387', 'kjv_definition'): ('6176e1c6744fc963e9034837a15f9a914cd72a2a582d9448fb57b742c120bc1a', '6176e1c6744fc963e9034837a15f9a914cd72a2a582d9448fb57b742c120bc1a', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2388', 'kjv_definition'): ('73b2bd7c806a0da03d23f267daeb1645ddc238f2669f19a65b8fda188d96c303', '73b2bd7c806a0da03d23f267daeb1645ddc238f2669f19a65b8fda188d96c303', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2389', 'kjv_definition'): ('e85c542162d1db7112aeff1e51c2414644e057ec2f6c66234a3abb480f748211', 'e85c542162d1db7112aeff1e51c2414644e057ec2f6c66234a3abb480f748211', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2391', 'kjv_definition'): ('6e5236d805faf714cf8f13a796cc5a800fbd406e5eafa883628ccb22b99595ef', '6e5236d805faf714cf8f13a796cc5a800fbd406e5eafa883628ccb22b99595ef', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2394', 'kjv_definition'): ('1f9eedfb880c84df48b4192f117a8449e7682fad147c59903afd22cc4e303509', '1f9eedfb880c84df48b4192f117a8449e7682fad147c59903afd22cc4e303509', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2456', 'kjv_definition'): ('f9ab325acd228c0c72521e7d4a8638693ea547aea29e711f17a572a841f5acfb', 'f9ab325acd228c0c72521e7d4a8638693ea547aea29e711f17a572a841f5acfb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2492', 'kjv_definition'): ('7d96f4069757adcc6f5524639de66339c8c0f70dfa7b78b502830a6a6355511b', '7d96f4069757adcc6f5524639de66339c8c0f70dfa7b78b502830a6a6355511b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2493', 'kjv_definition'): ('f812e6a9b439d1ad509d2261f2740e96b7ff231d82d49800785b1536003e84d4', 'f812e6a9b439d1ad509d2261f2740e96b7ff231d82d49800785b1536003e84d4', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2494', 'kjv_definition'): ('2ed43dc2a176fcbd9a550a5f427747b2ae7ac8150af04ff487e84654a900ca22', '2ed43dc2a176fcbd9a550a5f427747b2ae7ac8150af04ff487e84654a900ca22', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2495', 'kjv_definition'): ('123c4f49f39c46b61e6293aa7740e1c271078987fed35a2fa494d2c11c9ae985', '123c4f49f39c46b61e6293aa7740e1c271078987fed35a2fa494d2c11c9ae985', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2496', 'kjv_definition'): ('81be714f6360bf9c839d66f2d1b86db70b73d20e54094ffac0be974d7e7e6d88', '81be714f6360bf9c839d66f2d1b86db70b73d20e54094ffac0be974d7e7e6d88', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2497', 'kjv_definition'): ('1f509dfb05461f2f344ea0c8052dee5bb1843005ced608ee0870cff2ba175d71', '1f509dfb05461f2f344ea0c8052dee5bb1843005ced608ee0870cff2ba175d71', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2498', 'kjv_definition'): ('a344b84fa9adc6573b4fad94e610aa4f820845f86727d21b00f5767b963ee7da', 'a344b84fa9adc6573b4fad94e610aa4f820845f86727d21b00f5767b963ee7da', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2500', 'kjv_definition'): ('a8609c21b750dfcf9ab73b992baa69ba856ead0ce3c8ad3e8acfbc6fa414afb8', 'a8609c21b750dfcf9ab73b992baa69ba856ead0ce3c8ad3e8acfbc6fa414afb8', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2501', 'kjv_definition'): ('ad247729af0b3fced03ce880d43a0a213c784e243cc2379ae23fabecec70046b', 'ad247729af0b3fced03ce880d43a0a213c784e243cc2379ae23fabecec70046b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2502', 'kjv_definition'): ('a67cf485312a3546d0d3b671d79f520658130750aab8059b85b0f01459a81a63', 'a67cf485312a3546d0d3b671d79f520658130750aab8059b85b0f01459a81a63', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2921', 'kjv_definition'): ('7d0b800e5649ce1aca3742f4d48130dce29acfd210d11ac517fd51b9c56d6aea', '7d0b800e5649ce1aca3742f4d48130dce29acfd210d11ac517fd51b9c56d6aea', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2996', 'kjv_definition'): ('6fc8f566fe6fe6f6a76a03a5c1fecfa5aa6b6ad2afdb30be7ab557e1434380fc', '6fc8f566fe6fe6f6a76a03a5c1fecfa5aa6b6ad2afdb30be7ab557e1434380fc', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3017', 'kjv_definition'): ('40ba3f09a93eb510d9f8f8bebc5ce52a894cdf76b0cbafd359e62567c1160179', '40ba3f09a93eb510d9f8f8bebc5ce52a894cdf76b0cbafd359e62567c1160179', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3066', 'kjv_definition'): ('2b4f806c6b2218a40cdde2783cdd14c4191862b4e0b0437e2dd80a3e7c8eb6ab', '2b4f806c6b2218a40cdde2783cdd14c4191862b4e0b0437e2dd80a3e7c8eb6ab', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3194', 'kjv_definition'): ('1f82c58784c7bad48098eecde4eb58243a31544fe5c31f0d0fd8c53d222e7e20', '1f82c58784c7bad48098eecde4eb58243a31544fe5c31f0d0fd8c53d222e7e20', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3486', 'kjv_definition'): ('a0e7598df43540fdd8ab52d4ca765dad73524c82d7d3ccda67b28fefb0b1a333', 'a0e7598df43540fdd8ab52d4ca765dad73524c82d7d3ccda67b28fefb0b1a333', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3488', 'kjv_definition'): ('fcc56745c0de0c86801228469aa8329fe328d3e9234d506f3d8121cef52a032d', 'fcc56745c0de0c86801228469aa8329fe328d3e9234d506f3d8121cef52a032d', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3497', 'kjv_definition'): ('55140528c284e64d2e1b40d1cb90e5aa4e407418d3e4db81d6fc545199443473', '55140528c284e64d2e1b40d1cb90e5aa4e407418d3e4db81d6fc545199443473', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3773', 'kjv_definition'): ('28020d598c66f67ff51dd5c7e60c908c8c801cff7ee5b382778791fe60ebe8e6', '28020d598c66f67ff51dd5c7e60c908c8c801cff7ee5b382778791fe60ebe8e6', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G3774', 'kjv_definition'): ('745e122c84a2e58fb2773c5a14227b5214b8e558a484d9e8e1f461d1b8a0d232', '745e122c84a2e58fb2773c5a14227b5214b8e558a484d9e8e1f461d1b8a0d232', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4011', 'kjv_definition'): ('48199b43deb43fd8f099dbf52ac0bf0fe5dee1dc7fbc7e8632033b479beeb004', '48199b43deb43fd8f099dbf52ac0bf0fe5dee1dc7fbc7e8632033b479beeb004', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4201', 'kjv_definition'): ('676631de921b817fe548b70208c46c28395d08df65d3c60fc150fba980b62c87', '676631de921b817fe548b70208c46c28395d08df65d3c60fc150fba980b62c87', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4223', 'kjv_definition'): ('fbf560acf56524f801050881f887edb8af36bd8ed1855609ce25d8ec5ea1f59d', 'fbf560acf56524f801050881f887edb8af36bd8ed1855609ce25d8ec5ea1f59d', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4227', 'kjv_definition'): ('a7a1d4207c5e0ae2f8c0bfad066f4001e513d5ab43152bfb865bbfe3e9cdc2f2', 'a7a1d4207c5e0ae2f8c0bfad066f4001e513d5ab43152bfb865bbfe3e9cdc2f2', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4324', 'kjv_definition'): ('4c83460fa84a82bdfb87719c82726d7e3c0f60deebce523ecff333e422a04fe1', '4c83460fa84a82bdfb87719c82726d7e3c0f60deebce523ecff333e422a04fe1', 'Opaque source fragment i. is preserved literally; do not invent a KJV gloss.'),
    ('G4402', 'kjv_definition'): ('e5345919ce62eaa2d932df1fbaab03bb515f6b8dd9e9b36e2ac7d2e71b3d2a7b', 'e5345919ce62eaa2d932df1fbaab03bb515f6b8dd9e9b36e2ac7d2e71b3d2a7b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4466', 'kjv_definition'): ('13ec079196c17d45840e1722785320f0c569d1b5282bdf5b5185930bf85f5b66', '13ec079196c17d45840e1722785320f0c569d1b5282bdf5b5185930bf85f5b66', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4478', 'kjv_definition'): ('4a1b8746f39cbecefbec9d37d9385419fc1c87f6b830bd2a96c7ae54264f2621', '4a1b8746f39cbecefbec9d37d9385419fc1c87f6b830bd2a96c7ae54264f2621', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4479', 'kjv_definition'): ('1d15dfbc277ac3f9adba36bf25dbb590ffb5e6f0ffcbf8662472dba9df18f09d', '1d15dfbc277ac3f9adba36bf25dbb590ffb5e6f0ffcbf8662472dba9df18f09d', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4481', 'kjv_definition'): ('1bb02e27b2f6288412eda330d89c14e61367ff3c36e47c4235597e12223d2efb', '1bb02e27b2f6288412eda330d89c14e61367ff3c36e47c4235597e12223d2efb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4484', 'kjv_definition'): ('a9f8bd190d62cf1bad4f4a92c8848e7b48cc323ea9b646c2ba31e923db09847e', 'a9f8bd190d62cf1bad4f4a92c8848e7b48cc323ea9b646c2ba31e923db09847e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4503', 'kjv_definition'): ('695fdc397299128c0676539227535b138eb7ba8837cfd89b17ad4ee660827f1f', '695fdc397299128c0676539227535b138eb7ba8837cfd89b17ad4ee660827f1f', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4504', 'kjv_definition'): ('16190b7e47a8e95ac198ed86f3f4b849632b37e5bf2ab5349e0a9829fb317446', '16190b7e47a8e95ac198ed86f3f4b849632b37e5bf2ab5349e0a9829fb317446', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4540', 'kjv_definition'): ('7169e78532dc8b03b8a66a0f34cba7137b4c3849e9922fe6e76b776d9c82fcce', '7169e78532dc8b03b8a66a0f34cba7137b4c3849e9922fe6e76b776d9c82fcce', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4569', 'kjv_definition'): ('86ee89039b403b8ce585f340443a75471fdd03669b6ba5071cb37b31b395411c', '86ee89039b403b8ce585f340443a75471fdd03669b6ba5071cb37b31b395411c', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4588', 'kjv_definition'): ('74889f448990e113eadd3143d571e0be8d78377848312c2d0edbf7501bd9afce', '74889f448990e113eadd3143d571e0be8d78377848312c2d0edbf7501bd9afce', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4610', 'kjv_definition'): ('4e440f1aa28c08ad15e280732e63720489c7c5653c2df1b12ed97d4e2594cb80', '4e440f1aa28c08ad15e280732e63720489c7c5653c2df1b12ed97d4e2594cb80', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4670', 'kjv_definition'): ('db2c112aadca18cbef98f4eeb3054f09bad66cb16cd6a8850cd07460e4a00a86', 'db2c112aadca18cbef98f4eeb3054f09bad66cb16cd6a8850cd07460e4a00a86', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4677', 'kjv_definition'): ('5b9e7d8405d1538d6dda3fc462fc2dc6baf87743147749bd95b2601c158127c2', '5b9e7d8405d1538d6dda3fc462fc2dc6baf87743147749bd95b2601c158127c2', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4986', 'kjv_definition'): ('42810f512da42a07dcad58b1db36c715d7157fdfa5b6d0315a7181cb5d2c9e64', '42810f512da42a07dcad58b1db36c715d7157fdfa5b6d0315a7181cb5d2c9e64', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G4989', 'kjv_definition'): ('385918bac110481fddc3e12e321d5beb35488e2a33a66607132449b74cf98812', '385918bac110481fddc3e12e321d5beb35488e2a33a66607132449b74cf98812', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5060', 'kjv_definition'): ('d4fe0853376c953cc8c1b4ec91b8b559e34e08c6462ffe247d9f357b4f2d0a1e', 'd4fe0853376c953cc8c1b4ec91b8b559e34e08c6462ffe247d9f357b4f2d0a1e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5061', 'kjv_definition'): ('a770ce7d074203a11ddf81db9765388b7991ecb15526cd0d6753f79d6cc42491', 'a770ce7d074203a11ddf81db9765388b7991ecb15526cd0d6753f79d6cc42491', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5086', 'kjv_definition'): ('23a2d0db4d0bcf05cf428eaf3f8e6d8bac2d51e4e4333ef43e014531a8c3a3b7', '23a2d0db4d0bcf05cf428eaf3f8e6d8bac2d51e4e4333ef43e014531a8c3a3b7', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5103', 'kjv_definition'): ('ab26de36fe89fd3df7f547768050e3efc1b4210f3ed22017e2c0f9c1018d65e2', 'ab26de36fe89fd3df7f547768050e3efc1b4210f3ed22017e2c0f9c1018d65e2', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5175', 'kjv_definition'): ('2a269d50c6cbd0cc9ef48b706ffcce26a37bf9c7eb8afb253eac4e32a5661695', '2a269d50c6cbd0cc9ef48b706ffcce26a37bf9c7eb8afb253eac4e32a5661695', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G5181', 'kjv_definition'): ('3dee06100eb0b8908cf6f7c62c55ebac732e04a63a7f61d51f245086ed528c9b', '3dee06100eb0b8908cf6f7c62c55ebac732e04a63a7f61d51f245086ed528c9b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H59', 'kjv_definition'): ('ff2d88b124ddbca7ee09644258d39d12ab1e048631803006929dcd7e933d3d6f', 'ff2d88b124ddbca7ee09644258d39d12ab1e048631803006929dcd7e933d3d6f', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H71', 'kjv_definition'): ('0a9eb44c9d3477a499311e4419fd5ad9ff2b02358f80e89fc4c84b52804c73ab', '0a9eb44c9d3477a499311e4419fd5ad9ff2b02358f80e89fc4c84b52804c73ab', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H72', 'kjv_definition'): ('3d97baee9a2ee31499f2865574b0f90f5a7e3b5bba466523a4e67b767d7cc8b6', '3d97baee9a2ee31499f2865574b0f90f5a7e3b5bba466523a4e67b767d7cc8b6', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H78', 'kjv_definition'): ('0aaf2ba9e062530d2738186408c1b6e2cafa3f4d659e97ef69a4936291d4ff9e', '0aaf2ba9e062530d2738186408c1b6e2cafa3f4d659e97ef69a4936291d4ff9e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H114', 'kjv_definition'): ('6ffd7810f54cbd56e7021af70b2296623a6b9f71d3a4bb36c4513eddc6908095', '6ffd7810f54cbd56e7021af70b2296623a6b9f71d3a4bb36c4513eddc6908095', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H143', 'kjv_definition'): ('c04218dc7c568fb8b5db114cc11863b50d15761a238733451c7957ff2826a1c5', 'c04218dc7c568fb8b5db114cc11863b50d15761a238733451c7957ff2826a1c5', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H144', 'kjv_definition'): ('c04218dc7c568fb8b5db114cc11863b50d15761a238733451c7957ff2826a1c5', 'c04218dc7c568fb8b5db114cc11863b50d15761a238733451c7957ff2826a1c5', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H146', 'kjv_definition'): ('350ae11f2ba6790c2f431e0c948552bdd5314ede9690e55e15db41373db1f384', '350ae11f2ba6790c2f431e0c948552bdd5314ede9690e55e15db41373db1f384', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H162', 'definition'): ('56723edcd935d4765276b152e8e4c03990edd9dd87863122cd85cddd74eb37ea', '56723edcd935d4765276b152e8e4c03990edd9dd87863122cd85cddd74eb37ea', 'Shared German interjection Oh; no English prose.'),
    ('H173', 'kjv_definition'): ('3012c6ea595b1da70bf67e4e2ae1e84f2670bc89bc3750f87d84fa471a8fa1d0', '3012c6ea595b1da70bf67e4e2ae1e84f2670bc89bc3750f87d84fa471a8fa1d0', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H224', 'kjv_definition'): ('2bb43f56f437c1f63af3b1189cec36e7e466a875467eddd79f558c4ed25b175a', '2bb43f56f437c1f63af3b1189cec36e7e466a875467eddd79f558c4ed25b175a', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H516', 'kjv_definition'): ('9c7480239a66cc1b5c2b680cd15be94fcfcade8e5060e0b17b90da2cd4f11582', '9c7480239a66cc1b5c2b680cd15be94fcfcade8e5060e0b17b90da2cd4f11582', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H550', 'kjv_definition'): ('59b0809b11c7673bd20f11d9efdc8fc9bc7b6a61adbd01e34e1e370f7d625668', '59b0809b11c7673bd20f11d9efdc8fc9bc7b6a61adbd01e34e1e370f7d625668', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H945', 'kjv_definition'): ('b933adb40b957e672ca85ed22d727f16307b6d942bf23c66819b8799c24559f0', 'b933adb40b957e672ca85ed22d727f16307b6d942bf23c66819b8799c24559f0', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1035', 'kjv_definition'): ('c606d534259bc537150e99a2fc3cd72f18b72c143884aa805f95080705266c40', 'c606d534259bc537150e99a2fc3cd72f18b72c143884aa805f95080705266c40', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1180', 'kjv_definition'): ('226e7d7c07ac3e30a68d91a6475c4e769020ee942a540f0633b3a41ca40f2077', '226e7d7c07ac3e30a68d91a6475c4e769020ee942a540f0633b3a41ca40f2077', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1308', 'kjv_definition'): ('f90d6eeff014920a9cf5d3e00ffe7d206be2cf423be4083eb7b323189423126c', 'f90d6eeff014920a9cf5d3e00ffe7d206be2cf423be4083eb7b323189423126c', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1562', 'kjv_definition'): ('3ab1b61fca4d121070001cb0027ad8ad6feb1432b777bf80f2b33679aecacceb', '3ab1b61fca4d121070001cb0027ad8ad6feb1432b777bf80f2b33679aecacceb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1665', 'kjv_definition'): ('fa52c337b56854f172b5fa8b7ad62c8eb6460cdf52f7ed9c1604d4320f0cf8e9', 'fa52c337b56854f172b5fa8b7ad62c8eb6460cdf52f7ed9c1604d4320f0cf8e9', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1720', 'kjv_definition'): ('6999e860682db4f71a4e4f6ac909deafbd8f70c93d8fa425ebeb5e4f56531bd4', '6999e860682db4f71a4e4f6ac909deafbd8f70c93d8fa425ebeb5e4f56531bd4', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H1889', 'definition'): ('b9be167dad05e2e4fd9d0e9ded54ac73d458569d3db03cdc74234df31e84bde4', 'b9be167dad05e2e4fd9d0e9ded54ac73d458569d3db03cdc74234df31e84bde4', 'Shared German interjection aha, including its numbered sense.'),
    ('H1889', 'detailed_definition'): ('9c240f4713bae8986b6a7b426e0acd358623db96ecb9d043de1f4ef24a6dce1e', '9c240f4713bae8986b6a7b426e0acd358623db96ecb9d043de1f4ef24a6dce1e', 'Shared German interjection aha, including its numbered sense.'),
    ('H1930', 'definition'): ('308450705d2745c66d68e5fe4c0c6bae8fd57edc789193fa5edcd944a6268da5', '308450705d2745c66d68e5fe4c0c6bae8fd57edc789193fa5edcd944a6268da5', 'Shared German interjection oh; no English prose.'),
    ('H1945', 'definition'): ('308450705d2745c66d68e5fe4c0c6bae8fd57edc789193fa5edcd944a6268da5', '308450705d2745c66d68e5fe4c0c6bae8fd57edc789193fa5edcd944a6268da5', 'Shared German interjection oh; no English prose.'),
    ('H2012', 'kjv_definition'): ('4434d9cc419bfe3db5925af00a9f3a65f881de4dd645da61560afbad87b46017', '4434d9cc419bfe3db5925af00a9f3a65f881de4dd645da61560afbad87b46017', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H2886', 'kjv_definition'): ('8b06b39f0d85964a2ce519980d6e76e6d66957bc139f4a81072cb7b4193a2b66', '8b06b39f0d85964a2ce519980d6e76e6d66957bc139f4a81072cb7b4193a2b66', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3011', 'kjv_definition'): ('ab409bfacb5912d724d77a260d4194ac440e1737f83db8f011b402e2345e005c', 'ab409bfacb5912d724d77a260d4194ac440e1737f83db8f011b402e2345e005c', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3056', 'kjv_definition'): ('8e0cb81fe73f41bbce5ce81b3d5ddf47b617bad8d874a5890f2a3df4e864e3fb', '8e0cb81fe73f41bbce5ce81b3d5ddf47b617bad8d874a5890f2a3df4e864e3fb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3057', 'kjv_definition'): ('490fd595290179cbba3a4fd6dfaadbc5fffc1069af3d9ba2de3a2c0f6fb66861', '490fd595290179cbba3a4fd6dfaadbc5fffc1069af3d9ba2de3a2c0f6fb66861', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3069', 'definition'): ('6644766367c5ebcc72a34cfd5f60d0e0ccf7eac64fdefedf231b8c0c84f925c5', '6644766367c5ebcc72a34cfd5f60d0e0ccf7eac64fdefedf231b8c0c84f925c5', 'The field contains only the divine-name identifier YHWH in source braces.'),
    ('H3122', 'kjv_definition'): ('d10af081dbec2ef69dc73bc16e7254333ebce2a1ac80c3e7f06b645efdfed159', 'd10af081dbec2ef69dc73bc16e7254333ebce2a1ac80c3e7f06b645efdfed159', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3128', 'kjv_definition'): ('7ba2c9b5381eeb9751e972eb15fa94f000b1c5665f256b33b2405b6f3f527149', '7ba2c9b5381eeb9751e972eb15fa94f000b1c5665f256b33b2405b6f3f527149', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3129', 'kjv_definition'): ('5b6b7f95231d71ed12d39e881cd79e815c1ba029d20bcb76509548f179ca650c', '5b6b7f95231d71ed12d39e881cd79e815c1ba029d20bcb76509548f179ca650c', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3281', 'kjv_definition'): ('7fc4a61d2681456c76dfd256d5398242e47daa0767df4e27ad194f5a9a1fdcaa', '7fc4a61d2681456c76dfd256d5398242e47daa0767df4e27ad194f5a9a1fdcaa', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3421', 'kjv_definition'): ('a8a0cb93c441cc96e88585f7646becd9d7161b5d614a986b27d84614f6216850', 'a8a0cb93c441cc96e88585f7646becd9d7161b5d614a986b27d84614f6216850', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3450', 'kjv_definition'): ('fc3a9600929065f6064af78fd7bcf627cb83930181c4fa77a19c5d446ece62a5', 'fc3a9600929065f6064af78fd7bcf627cb83930181c4fa77a19c5d446ece62a5', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3652', 'definition'): ('a1d9890884c1b4b960c279cfe7554a900d169422d6cec980beef67761487d3b9', 'a1d9890884c1b4b960c279cfe7554a900d169422d6cec980beef67761487d3b9', 'German adverb so expresses the supplied source sense.'),
    ('H3798', 'kjv_definition'): ('7d3a0a7b7975ed36b3939bdd8eff69bd1410189f13989a343e0fb8f1eadfcb6b', '7d3a0a7b7975ed36b3939bdd8eff69bd1410189f13989a343e0fb8f1eadfcb6b', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3817', 'kjv_definition'): ('700f9ba0467014c95f4244dbc1f90a342994606c2db252c9a6b11ac95e6304bb', '700f9ba0467014c95f4244dbc1f90a342994606c2db252c9a6b11ac95e6304bb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3853', 'kjv_definition'): ('e740bc08776ee999572580077c60f6f5ced217665800f8b6ed4609f244636aea', 'e740bc08776ee999572580077c60f6f5ced217665800f8b6ed4609f244636aea', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H3968', 'kjv_definition'): ('b094105c593e3a49470246c26480fe984b4123dba777fc99b03ab966e273004e', 'b094105c593e3a49470246c26480fe984b4123dba777fc99b03ab966e273004e', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H4176', 'kjv_definition'): ('42df256ebd5439558ebdfe3b0bfeba6fbcfa128bdfa301a696b4047b43ee52bb', '42df256ebd5439558ebdfe3b0bfeba6fbcfa128bdfa301a696b4047b43ee52bb', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H4366', 'kjv_definition'): ('e81d62a5e72e52d589f0e43f2c12b99a9ff5b2484a45b3675e5bc5699d10e25f', 'e81d62a5e72e52d589f0e43f2c12b99a9ff5b2484a45b3675e5bc5699d10e25f', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H4663', 'kjv_definition'): ('220b6df0a42c07180f56243d60cf5f7f3d8b0b56b8cff85a67103cde42b604ad', '220b6df0a42c07180f56243d60cf5f7f3d8b0b56b8cff85a67103cde42b604ad', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H4815', 'kjv_definition'): ('64a653f155a613dce998d6aa3c5e30144e69318238b59fc2654074438eb5479f', '64a653f155a613dce998d6aa3c5e30144e69318238b59fc2654074438eb5479f', 'German adjective bitter expresses the supplied source sense.'),
    ('H4815', 'detailed_definition'): ('175c163f6cf895fbb1759020b326f39a0b00c98199787036b009b748981c1560', '175c163f6cf895fbb1759020b326f39a0b00c98199787036b009b748981c1560', 'German adjective bitter expresses the supplied source sense.'),
    ('H5305', 'kjv_definition'): ('3b37fdb9afc97e87db204e4ef26e3d1dd63546d71e3c4a8c43d0c3fc43bde832', '3b37fdb9afc97e87db204e4ef26e3d1dd63546d71e3c4a8c43d0c3fc43bde832', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H5495', 'kjv_definition'): ('fdce61c3382de2e73a0f9ce4a81392e237c5110606b391f0cb3d4845cf507201', 'fdce61c3382de2e73a0f9ce4a81392e237c5110606b391f0cb3d4845cf507201', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H5510', 'kjv_definition'): ('e6eddd1a4cda6985ffccdecbfef3ed2d55dce577cfeeaa2f15e93ad141ee1270', 'e6eddd1a4cda6985ffccdecbfef3ed2d55dce577cfeeaa2f15e93ad141ee1270', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H5515', 'kjv_definition'): ('b43ba200a3cc39b68411852c0e8a204348e06df06cd15a9fb98068334e9bd7ac', 'b43ba200a3cc39b68411852c0e8a204348e06df06cd15a9fb98068334e9bd7ac', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H6047', 'kjv_definition'): ('d20316fec3506ddf228440566124e2108fed4ba619f3a13b927faf334dfa21c4', 'd20316fec3506ddf228440566124e2108fed4ba619f3a13b927faf334dfa21c4', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H6062', 'kjv_definition'): ('28acbebd52b50ed6c545383059054d21be1d6fbe750d9fc036ad664ec16a5bb8', '28acbebd52b50ed6c545383059054d21be1d6fbe750d9fc036ad664ec16a5bb8', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H6230', 'kjv_definition'): ('991d45744fa08153b05059709a77a8c9d9eb7d60ca56fc7dd7487065b90c8f48', '991d45744fa08153b05059709a77a8c9d9eb7d60ca56fc7dd7487065b90c8f48', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H6376', 'kjv_definition'): ('0870a2c7e9764e3b52b3154d60fa96e85dcd9b5ada3ac0edefa674fb78522e66', '0870a2c7e9764e3b52b3154d60fa96e85dcd9b5ada3ac0edefa674fb78522e66', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H6625', 'kjv_definition'): ('688a9ee4bfdb56407cf1a40cf720fabc56e4c65daca1a64dbd541a8c0e688e65', '688a9ee4bfdb56407cf1a40cf720fabc56e4c65daca1a64dbd541a8c0e688e65', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H7254', 'kjv_definition'): ('8425db5c080f0312a95a5d9007531a5372f94ee643a5cc42c93ee36beeb1d73c', '8425db5c080f0312a95a5d9007531a5372f94ee643a5cc42c93ee36beeb1d73c', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H7886', 'kjv_definition'): ('91e5e093da8d6b03e8adbcf1cc709d756b6efe05ee1a943f242d3d8ef4317a77', '91e5e093da8d6b03e8adbcf1cc709d756b6efe05ee1a943f242d3d8ef4317a77', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('H8095', 'kjv_definition'): ('52f3f1407a1a505348e03ac981e004a7776fb92537d2bc3514276899646cf9e5', '52f3f1407a1a505348e03ac981e004a7776fb92537d2bc3514276899646cf9e5', 'Reviewed historical proper-name or named-title alias; source supplies this identifier, not English prose.'),
    ('G2268', 'kjv_definition'): ('7885ac3f734a5e7dd78d89b41223a44322641616b81eba35dfa1225282344bfe', '7885ac3f734a5e7dd78d89b41223a44322641616b81eba35dfa1225282344bfe', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('G5617', 'kjv_definition'): ('d4aebf9dc343d3851df602dbe82b273aa220d92335d8ed68970ec114d174138d', 'd4aebf9dc343d3851df602dbe82b273aa220d92335d8ed68970ec114d174138d', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H26', 'kjv_definition'): ('fe7850eafb2a71318f43d605c9c371a724ee42a118cdc1b203f64ad684e6c4de', 'fe7850eafb2a71318f43d605c9c371a724ee42a118cdc1b203f64ad684e6c4de', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H28', 'kjv_definition'): ('84e819ed6b67b14ae581424711c7006d78f33c39aff305186817b5fab273452f', '84e819ed6b67b14ae581424711c7006d78f33c39aff305186817b5fab273452f', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H357', 'kjv_definition'): ('fa386526c78cbe0a3b51ac2b5610a8f3c24761da0d20f0d8de51bc5f5cd95fbb', 'fa386526c78cbe0a3b51ac2b5610a8f3c24761da0d20f0d8de51bc5f5cd95fbb', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H359', 'kjv_definition'): ('a0b87a7f5b4d52c5fc4a4e8aee51c25f5f74be18be61dec14faf73087a6eb5bb', 'a0b87a7f5b4d52c5fc4a4e8aee51c25f5f74be18be61dec14faf73087a6eb5bb', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H450', 'kjv_definition'): ('c2f69a03d6e4c8e8bf42ba494b793b84ad83b0242868ac93e08b6c2ace701aec', 'c2f69a03d6e4c8e8bf42ba494b793b84ad83b0242868ac93e08b6c2ace701aec', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H454', 'kjv_definition'): ('34eb2e78f1894584f8530a3b1af0daf7905fbc0e7a7a8311e794c461fd268693', '34eb2e78f1894584f8530a3b1af0daf7905fbc0e7a7a8311e794c461fd268693', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H467', 'kjv_definition'): ('b411ed634392927b58d696a442b2f033db2d5ee86c61bc230f98aa1a1b2ebbd2', 'b411ed634392927b58d696a442b2f033db2d5ee86c61bc230f98aa1a1b2ebbd2', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H469', 'kjv_definition'): ('b60f8aa59b55b9ce8282adb834ea232c7fd4b6f0803dc802d8ac7f3328ae8bde', 'b60f8aa59b55b9ce8282adb834ea232c7fd4b6f0803dc802d8ac7f3328ae8bde', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H501', 'kjv_definition'): ('d9bf016ddfcb298e904db72c99c3434f1ab3f997685e54520387c7434801410c', 'd9bf016ddfcb298e904db72c99c3434f1ab3f997685e54520387c7434801410c', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H682', 'kjv_definition'): ('90b39ec5a8bf72e74f78c43d5aa20df5000c6ade96d2dd2f73a19a646c3e4daa', '90b39ec5a8bf72e74f78c43d5aa20df5000c6ade96d2dd2f73a19a646c3e4daa', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H774', 'kjv_definition'): ('fa990f5dd376ecfb7c113ce69e720bdb51fca9a5afc3306da6f7638601ad1b7c', 'fa990f5dd376ecfb7c113ce69e720bdb51fca9a5afc3306da6f7638601ad1b7c', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H863', 'kjv_definition'): ('e6e084ba02647f0295df923881ca32e11e0ae9db92c2720f0078e3a6b7c47965', 'e6e084ba02647f0295df923881ca32e11e0ae9db92c2720f0078e3a6b7c47965', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H894', 'kjv_definition'): ('46cdfe52c199e386fd3f2fd38b2ff2a08129b7f7e43200471e6e1023ee21be14', '46cdfe52c199e386fd3f2fd38b2ff2a08129b7f7e43200471e6e1023ee21be14', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H895', 'kjv_definition'): ('360f31281da793766ccc26193867684b9c24dca9ca4ebc315d227d3230b58658', '360f31281da793766ccc26193867684b9c24dca9ca4ebc315d227d3230b58658', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H904', 'kjv_definition'): ('e8922f720f64d571426db54eab119dc9e769b2d4e28f43ec7c980870466f5069', 'e8922f720f64d571426db54eab119dc9e769b2d4e28f43ec7c980870466f5069', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H1268', 'kjv_definition'): ('44af7c44e37a7616a1ae0827d10a9fc0fbdafcfdbc1f9974ced6707ae6df3575', '44af7c44e37a7616a1ae0827d10a9fc0fbdafcfdbc1f9974ced6707ae6df3575', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H1329', 'kjv_definition'): ('fc2deab549a1da747e955cf18dc487f9b419f661b4a1a64ca91da272f9955274', 'fc2deab549a1da747e955cf18dc487f9b419f661b4a1a64ca91da272f9955274', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H1394', 'definition'): ('324cc988c1fe3784827f2893d357534e3b5a3a48be56ff3c384a91e1b1b28252', '324cc988c1fe3784827f2893d357534e3b5a3a48be56ff3c384a91e1b1b28252', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H1721', 'kjv_definition'): ('7c45dd10f9a082e34a40a525d671f059d5f457ca99aaa975571fe6581d284407', '7c45dd10f9a082e34a40a525d671f059d5f457ca99aaa975571fe6581d284407', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H1896', 'kjv_definition'): ('5d57738ea64449cc4ef900273da8174be4b30f78ad550851586e7841b58dad5e', '5d57738ea64449cc4ef900273da8174be4b30f78ad550851586e7841b58dad5e', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H2286', 'kjv_definition'): ('b33a18d286a524fdc0d7b3f6aa9cc0e13a4b79656e2d4f6a2ec008ebe52dcab4', 'b33a18d286a524fdc0d7b3f6aa9cc0e13a4b79656e2d4f6a2ec008ebe52dcab4', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H2712', 'kjv_definition'): ('b939a413013815130d2c4da1d95ec5063b9bc141efb55b14a2a0eb6f17b03973', 'b939a413013815130d2c4da1d95ec5063b9bc141efb55b14a2a0eb6f17b03973', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H2726', 'kjv_definition'): ('432a340a6e2e882f062526c882a8f73949adf6b4f14c0d2d2f63175500356544', '432a340a6e2e882f062526c882a8f73949adf6b4f14c0d2d2f63175500356544', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H2870', 'kjv_definition'): ('ca2923068fd65764c1dace0531ca59cddb2b9291f77c69cb5f9aebe192887267', 'ca2923068fd65764c1dace0531ca59cddb2b9291f77c69cb5f9aebe192887267', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3096', 'kjv_definition'): ('b853bb77152812505c422bb92a7ec0f2d0949af5aa0c13ededbf883891594484', 'b853bb77152812505c422bb92a7ec0f2d0949af5aa0c13ededbf883891594484', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3107', 'kjv_definition'): ('71a4023cd23c1dd70c79f2f1f556f72d5e9d345c9f5087aea3f1823e3fff2113', '71a4023cd23c1dd70c79f2f1f556f72d5e9d345c9f5087aea3f1823e3fff2113', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3279', 'kjv_definition'): ('aeefbb8b4e07372640ef20e36f0e0e90e2a0292b26f5696f890e1bb903b0ba58', 'aeefbb8b4e07372640ef20e36f0e0e90e2a0292b26f5696f890e1bb903b0ba58', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3300', 'kjv_definition'): ('08841d97d4ce5661691ab07939f96ef8ed60ee099ee1ef9a9141946b9f5e5415', '08841d97d4ce5661691ab07939f96ef8ed60ee099ee1ef9a9141946b9f5e5415', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3382', 'kjv_definition'): ('6e5236d805faf714cf8f13a796cc5a800fbd406e5eafa883628ccb22b99595ef', '6e5236d805faf714cf8f13a796cc5a800fbd406e5eafa883628ccb22b99595ef', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H3838', 'kjv_definition'): ('dc410ad3a03e5a238bb15b4c1d00bbc6e704a16c5f12f3af3de781b256b7d428', 'dc410ad3a03e5a238bb15b4c1d00bbc6e704a16c5f12f3af3de781b256b7d428', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4023', 'kjv_definition'): ('5ed6d5540e18b435c85c831bc3d83713560b8c6d45f0c3fab9620b7dc319c6a8', '5ed6d5540e18b435c85c831bc3d83713560b8c6d45f0c3fab9620b7dc319c6a8', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4036', 'kjv_definition'): ('0747fa85b973d0d0eecdc4d89bf2ffdbafa556669eb5fbb1a2d269d0e0471d3d', '0747fa85b973d0d0eecdc4d89bf2ffdbafa556669eb5fbb1a2d269d0e0471d3d', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4105', 'kjv_definition'): ('c6b790112db867ebe76759824de2e910ca2ba82f06213d796ec92e0d8c28b194', 'c6b790112db867ebe76759824de2e910ca2ba82f06213d796ec92e0d8c28b194', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4257', 'kjv_definition'): ('ee9ed10f49225e5431aa688cf4afbc3ebbd6b545ce5ec5b2a2f44b7d64906611', 'ee9ed10f49225e5431aa688cf4afbc3ebbd6b545ce5ec5b2a2f44b7d64906611', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4387', 'kjv_definition'): ('a3d9236ccc65144c2b12fa60193f5b120d1252b0234180eb84ddfa51d06fafee', 'a3d9236ccc65144c2b12fa60193f5b120d1252b0234180eb84ddfa51d06fafee', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4453', 'kjv_definition'): ('28c0c6ba54eca0a9f3efcafaa144cf02c5e0f2648529392ef715063ea762d2db', '28c0c6ba54eca0a9f3efcafaa144cf02c5e0f2648529392ef715063ea762d2db', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4484', 'kjv_definition'): ('9cb4fefe6f7b427f2d6e916f2d91050b291f5e858c0a08abee25b983967a54c8', '9cb4fefe6f7b427f2d6e916f2d91050b291f5e858c0a08abee25b983967a54c8', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H4905', 'kjv_definition'): ('c2dc9dd7bab6879e01017540a16303da4b60434719d67aed9c6ec2ae949d0458', 'c2dc9dd7bab6879e01017540a16303da4b60434719d67aed9c6ec2ae949d0458', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5096', 'kjv_definition'): ('3506ea4fb9ae82c2c4f80391ba8b1ca917c2d414af607da9555f0eea9bf99246', '3506ea4fb9ae82c2c4f80391ba8b1ca917c2d414af607da9555f0eea9bf99246', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5126', 'kjv_definition'): ('2314888bc7f107680a72b49824d07013b68b9f13dec6fbf3c0de8ec5ba1687b7', '2314888bc7f107680a72b49824d07013b68b9f13dec6fbf3c0de8ec5ba1687b7', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5451', 'kjv_definition'): ('00bbf6a6c4a6e3099aaa83bb45fd6ee1962026dda13494f43206ec5fcedf1742', '00bbf6a6c4a6e3099aaa83bb45fd6ee1962026dda13494f43206ec5fcedf1742', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5454', 'kjv_definition'): ('dc8e49b221a117403bb7c9850d0c794b4019854a60777b7ca6432bc6afe270f3', 'dc8e49b221a117403bb7c9850d0c794b4019854a60777b7ca6432bc6afe270f3', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5455', 'kjv_definition'): ('52940e441d9bab1b9ecdf4394cdc0b43be25b736e338329e011753723d6f8a01', '52940e441d9bab1b9ecdf4394cdc0b43be25b736e338329e011753723d6f8a01', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5517', 'kjv_definition'): ('0db8a1c2f768b0ec961130e89faeab33e276211418d8f8a2d53705f3009e6881', '0db8a1c2f768b0ec961130e89faeab33e276211418d8f8a2d53705f3009e6881', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5542', 'kjv_definition'): ('e145155bd6ffc2d56e8f668564ca5ea9edf6ec25ec0a8aeb9a814100b2964679', 'e145155bd6ffc2d56e8f668564ca5ea9edf6ec25ec0a8aeb9a814100b2964679', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5543', 'kjv_definition'): ('3622834e9c5bd659000bf1ae20ecd8dc47f247b4c7f78a34ec8285b2fdfe5fe4', '3622834e9c5bd659000bf1ae20ecd8dc47f247b4c7f78a34ec8285b2fdfe5fe4', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5677', 'kjv_definition'): ('25c86e015e0e1f57edeac05f284f8421e2f13e657791775e06a75ac1bb5850ec', '25c86e015e0e1f57edeac05f284f8421e2f13e657791775e06a75ac1bb5850ec', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5740', 'kjv_definition'): ('0dcacf129885c1f49129b9f8fccff2ec10e32f58f53abaf82e5d4f7dc94c1b73', '0dcacf129885c1f49129b9f8fccff2ec10e32f58f53abaf82e5d4f7dc94c1b73', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5798', 'kjv_definition'): ('9e3ebe041823b9e727522c418fa680ee064baec4e5c3b9668445464b297a1c6f', '9e3ebe041823b9e727522c418fa680ee064baec4e5c3b9668445464b297a1c6f', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5804', 'kjv_definition'): ('b847a185702c54fafd0ea7a0a015009e4f9266ab456408e3a3e276ce594c2241', 'b847a185702c54fafd0ea7a0a015009e4f9266ab456408e3a3e276ce594c2241', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5809', 'kjv_definition'): ('31ff46430ea33cd57b63f809ea95c4a062c3ce2a9dd16f07d77bef5ecb059115', '31ff46430ea33cd57b63f809ea95c4a062c3ce2a9dd16f07d77bef5ecb059115', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H5857', 'kjv_definition'): ('5af9ff8f7922db0db4c6ae971586b1bed614ff6209c51b8d7394c3b5a5d2cba8', '5af9ff8f7922db0db4c6ae971586b1bed614ff6209c51b8d7394c3b5a5d2cba8', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6107', 'kjv_definition'): ('ae1a78407a636baffad33d83d93a856b79d0667462fc5d869bfa800bd450725a', 'ae1a78407a636baffad33d83d93a856b79d0667462fc5d869bfa800bd450725a', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6332', 'kjv_definition'): ('90197788bd41bfacaad3e4497c274d9421a7eefa34f6fb602a62990db78670b6', '90197788bd41bfacaad3e4497c274d9421a7eefa34f6fb602a62990db78670b6', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6396', 'kjv_definition'): ('959b96fda8c5285daa91f1640b9ef015053ec16ca83e5f9b3679ec7a2c468196', '959b96fda8c5285daa91f1640b9ef015053ec16ca83e5f9b3679ec7a2c468196', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6406', 'kjv_definition'): ('159146670d62ea97fb018caf15a27a2b74ab06249041e1bb0095a9a269f40e6f', '159146670d62ea97fb018caf15a27a2b74ab06249041e1bb0095a9a269f40e6f', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6409', 'kjv_definition'): ('5df778b23f7ebae322d0e68b9cb57a2c9fbd43d8aeef1f9864a47e3cad5fa404', '5df778b23f7ebae322d0e68b9cb57a2c9fbd43d8aeef1f9864a47e3cad5fa404', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6439', 'kjv_definition'): ('c8df2daf77a5cb38d03371c222d61a7fc57e999e5c93d34b4837cf3f990a38a8', 'c8df2daf77a5cb38d03371c222d61a7fc57e999e5c93d34b4837cf3f990a38a8', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6464', 'kjv_definition'): ('c5a3863db63407753e5c3e13bfaa73b4d6105475624713a32e8a1634a0ec36ec', 'c5a3863db63407753e5c3e13bfaa73b4d6105475624713a32e8a1634a0ec36ec', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6636', 'kjv_definition'): ('650f70a280d15ecba883681b47728c5225864ccf53d3d89cf35067b1af09489d', '650f70a280d15ecba883681b47728c5225864ccf53d3d89cf35067b1af09489d', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H6678', 'kjv_definition'): ('9a2c9ce623486dcd514fda927fb0fafb902197ceaa0cffcb5cb600b431c9d8d5', '9a2c9ce623486dcd514fda927fb0fafb902197ceaa0cffcb5cb600b431c9d8d5', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H7417', 'kjv_definition'): ('c3609502583520a715b8ef112d3ae105619a697fddba772a7b62d13ad0ea68a6', 'c3609502583520a715b8ef112d3ae105619a697fddba772a7b62d13ad0ea68a6', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H7467', 'kjv_definition'): ('03b446355cc2fdce42812d0936d42af6ddefdd9dcce463572cb51e17f0fabc33', '03b446355cc2fdce42812d0936d42af6ddefdd9dcce463572cb51e17f0fabc33', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8067', 'kjv_definition'): ('8e721d76139119c13fd7b0f0cd48da338a64609897bcf14d22b1bf5b814ad30c', '8e721d76139119c13fd7b0f0cd48da338a64609897bcf14d22b1bf5b814ad30c', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8289', 'kjv_definition'): ('983e484f6b7010b9fce8376cb29341d35a1f51a6ebf8a7ff90a65528baa4afa4', '983e484f6b7010b9fce8376cb29341d35a1f51a6ebf8a7ff90a65528baa4afa4', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8352', 'kjv_definition'): ('5ff138a88253df1d9d91715ca3b791885bfd1a1b1a00a944dcdfa33451e2e6f5', '5ff138a88253df1d9d91715ca3b791885bfd1a1b1a00a944dcdfa33451e2e6f5', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8547', 'kjv_definition'): ('7a9413a3897a007539053011bffa075dff9c9ecd015aed040667a787e9e655b2', '7a9413a3897a007539053011bffa075dff9c9ecd015aed040667a787e9e655b2', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8555', 'kjv_definition'): ('bb8ae0a5e5817ca3d068a86f686c059014e913c3bb58cf30d53bc7805dc4a98c', 'bb8ae0a5e5817ca3d068a86f686c059014e913c3bb58cf30d53bc7805dc4a98c', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8556', 'kjv_definition'): ('6adeef79911867303181fdf5d164167a49f26450aa60f4425c6e49924375e56b', '6adeef79911867303181fdf5d164167a49f26450aa60f4425c6e49924375e56b', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
    ('H8590', 'kjv_definition'): ('c6cb97e64acdfba864e81deba9dc4c5718a4df1667bb462aec7b8e605b396caf', 'c6cb97e64acdfba864e81deba9dc4c5718a4df1667bb462aec7b8e605b396caf', 'Literal source-attested naming/title aliases, with meanings translated in the surrounding entry; not English prose or an asserted new German form.'),
}

ENGLISH_PROSE = re.compile(
    r'\b(?:of uncertain affinity|by implication|a primitive root|of Hebrew origin|'
    r'by extension|the same as|a primary word|an unused root|in the sense of|'
    r'from the root|that is to say|to be|to make|to cause|to become)\b', re.I)
ENGLISH_FUNCTION_WORDS = re.compile(r'\b(?:the|of|from|which|with|into|unto|whose|'
                                    r'hence|properly|figuratively|literally)\b', re.I)


def shared_spelling(source, text, language):
    text = text.strip().strip('.,;:!?()[]{}"\'')
    if language == 'es':
        if not re.fullmatch(r'[\w-]+', text, re.UNICODE):
            return False
        word = text.casefold()
        number = source.get('strong_number')
        if SPANISH_LOANWORDS.get(number) == word or SPANISH_GREEK_NAMES.get(number) == word:
            return True
        headword = (source.get('detailed_definition') or '').split('=', 1)[0].strip()
        return bool(word in SPANISH_NAMES
                    and (source.get('morphology') or '').startswith('n-pr')
                    and word == headword.casefold())
    if language != 'de' or not re.fullmatch(r'[\w-]+', text, re.UNICODE):
        return False
    if text.casefold() in SHARED_GERMAN_WORDS:
        return True
    headword = (source.get('detailed_definition') or '').split('=', 1)[0].strip()
    if ((source.get('morphology') or '').startswith('n-pr')
            and text.casefold() == headword.casefold()):
        return True
    def letters(value):
        return ''.join(c for c in unicodedata.normalize('NFKD', value or '')
                       if c.isalpha()).casefold()
    return bool(text[0].isupper() and letters(text) == letters(source.get('transliteration')))


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
    outside, stack = [], []
    closing = {')': '(', ']': '['}
    for char in text:
        if char in '([':
            stack.append(char)
        elif char in closing and stack and stack[-1] == closing[char]:
            stack.pop()
        elif not stack:
            outside.append(char)
    return len([p for p in re.split('[,;]', ''.join(outside)) if p.strip()])


def sense_numbers(text):
    labels = {match.start(1): match.group(1) for match in SENSE_NUMBER.finditer(text)}
    for match in INLINE_SENSE_NUMBER.finditer(text):
        labels.setdefault(match.start(1), match.group(1))
    return [labels[position] for position in sorted(labels)]


def reviewed_unchanged_field(number, field, english, translated, language):
    if language != 'de':
        return False
    entry = REVIEWED_GERMAN_FIELDS.get((number, field))
    return bool(entry and hashlib.sha256(english.encode('utf-8')).hexdigest() == entry[0]
                and hashlib.sha256(translated.encode('utf-8')).hexdigest() == entry[1])


def failures_of(source, answer, language):
    """
    The failures that are certain, so they never have to be argued about. Everything here is a
    property of the two strings; nothing here is a judgement about meaning.
    """
    found = []
    if answer.get('strong_number') != source.get('strong_number'):
        found.append(('strong_number', 'unexpected-id', 'returned a different Strong number'))
    for field in PROSE:
        english = source.get(field)
        if not english:
            if answer.get(field):
                found.append((field, 'unexpected-field', 'the source did not supply this field'))
            continue
        translated = answer.get(field)
        if not isinstance(translated, str) or not translated.strip():
            found.append((field, 'missing', 'the field was given and came back empty'))
            continue
        if (translated.strip() == english.strip()
                and not shared_spelling(source, english.strip(), language)
                and not reviewed_unchanged_field(source.get('strong_number'), field,
                                                english, translated, language)):
            found.append((field, 'untranslated', 'returned the English unchanged'))
            continue

        want = set(STRONG_REF.findall(english))
        got = set(STRONG_REF.findall(translated))
        if want - got:
            found.append((field, 'lost-reference', 'dropped ' + ', '.join(sorted(want - got))))
        if got - want:
            found.append((field, 'invented-reference', 'added ' + ', '.join(sorted(got - want))))

        want_script = ORIGINAL_SCRIPT.findall(unicodedata.normalize('NFC', english))
        got_script = ORIGINAL_SCRIPT.findall(unicodedata.normalize('NFC', translated))
        if Counter(want_script) != Counter(got_script):
            found.append((field, 'original-script-changed',
                          'quoted original-script characters or their counts changed'))

        if field == 'detailed_definition':
            want_senses = sense_numbers(english)
            got_senses = sense_numbers(translated)
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
        if language in ('de', 'es'):
            stripped = ORIGINAL_SCRIPT.sub('', STRONG_REF.sub('', translated))
            if (ENGLISH_PROSE.search(stripped)
                    or len(ENGLISH_FUNCTION_WORDS.findall(stripped)) >= 3):
                found.append((field, 'residual-English', 'English prose remains in the translation'))
    return found


def check_run(directory):
    with open(os.path.join(directory, 'source.json'), encoding='utf-8') as handle:
        source = {e['strong_number']: e for e in json.load(handle)}
    with open(os.path.join(directory, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    language = manifest['language']
    requested = {n for batch in manifest['batches'] for n in batch['numbers']}

    rows = read(os.path.join(directory, 'answers.jsonl'))
    answered = {r['strong_number'] for r in rows}
    out = []
    for number in sorted(requested - answered):
        out.append({'strong_number': number, 'field': '*', 'kind': 'missing-entry',
                    'detail': 'requested entry has no answer'})
    for number in sorted(requested ^ set(source)):
        out.append({'strong_number': number, 'field': '*', 'kind': 'source-selection',
                    'detail': 'manifest and frozen source IDs disagree'})
    if len(requested) != manifest['entries']:
        out.append({'strong_number': '*', 'field': '*', 'kind': 'manifest-count',
                    'detail': 'manifest entry count does not match its requested IDs'})
    for row in rows:
        if row['strong_number'] not in requested or row['strong_number'] not in source:
            out.append({'strong_number': row['strong_number'], 'field': '*',
                        'kind': 'unexpected-entry', 'detail': 'entry was not requested'})
            continue
        for field, kind, detail in failures_of(source[row['strong_number']], row, language):
            out.append({'strong_number': row['strong_number'], 'field': field,
                        'kind': kind, 'detail': detail})
    with open(os.path.join(directory, 'failures.json'), 'w', encoding='utf-8') as handle:
        json.dump(out, handle, ensure_ascii=False, indent=1)
    return rows, out, requested


def check(args):
    rows, out, requested = check_run(args.dir)
    clean = len(requested - {f['strong_number'] for f in out})
    print(f'{len(requested)} requested, {len(rows)} answered, {clean} with no structural failure, '
          f'{len(out)} failures')
    counts = {}
    for failure in out:
        counts[failure['kind']] = counts.get(failure['kind'], 0) + 1
    for kind, count in sorted(counts.items(), key=lambda p: -p[1]):
        print(f'  {kind}: {count}')
    if out:
        raise SystemExit(1)


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
    requested = {n for batch in manifest['batches'] for n in batch['numbers']}
    structural = {f['strong_number'] for f in failures} & requested
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
    rows, failures, requested = check_run(args.dir)
    if failures:
        raise SystemExit(f'refusing incomplete or structurally invalid run: {len(failures)} failures; '
                         'see failures.json. Repair the run before publishing.')
    required = ('method', 'model', 'prompt_version', 'run')
    if any(any(not row.get(key) for key in required) for row in rows):
        raise SystemExit('refusing rows without complete translation provenance')

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

    licence = os.path.join(os.path.dirname(args.out) or '.',
                           f'LICENCE.{manifest["language"]}.md')
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
    two.add_argument('--effort', default='medium', choices=('low', 'medium', 'high', 'max'))
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
    four.add_argument('--effort', default='medium', choices=('low', 'medium', 'high', 'max'))
    four.add_argument('--again', action='store_true')
    four.add_argument('--workers', type=int, default=6)
    four.set_defaults(run=judge)

    five = sub.add_parser('score', help='the report')
    five.add_argument('--dir', required=True)
    five.set_defaults(run=score)

    six = sub.add_parser('publish', help='the answers as a corpus file, with their provenance')
    six.add_argument('--dir', required=True)
    six.add_argument('--out', required=True, help=f'under {PUBLISHED}/ for a real run')
    six.add_argument('--clean', action='store_true', help='compatibility flag; incomplete runs are refused')
    six.set_defaults(run=publish)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
