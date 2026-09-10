"""
The target's name in the case the phrase puts it in -- asked for on its own, checked before it is trusted.

A rendered descriptor line needs the *target's* forms: *тесть Мойсея*, not *тесть Мойсей*.
`scripts/descriptors.py` produces forms for the *subject* of the claims it writes, so the two sets
only coincide once every entity has been described -- PRB-0392 measured 205 of 985 loaded claims with
a Ukrainian form for the entity they point at. This harness asks for the forms alone, for the
entities somebody else's claim already names, and does not re-ask the expensive half.

    python scripts/name-forms.py check                                  # the loaded forms, checked
    python scripts/name-forms.py extract --out .forms/run --targets --languages eng,ukr,deu,spa
    python scripts/name-forms.py ask     --dir .forms/run --model sonnet --effort low --workers 3
    python scripts/name-forms.py check   --dir .forms/run                # before anything is loaded
    python scripts/name-forms.py publish --dir .forms/run --out ../Resources/Essenthos/name-forms                                          --name targets-2026-09-09 --asked-at 2026-09-09

`compare --dir` scores a run against the forms already loaded, which is how a model is chosen
(DOC-0196), and `republish` writes PRB-0435's rows back without the preposition they carry.

Three things about the design are load-bearing:

**The check is written before the run and proved on forms that already exist.** A genitive that does
not share the nominative's stem is visibly wrong, a Ukrainian form written in Cyrillic letters
Ukrainian does not have is visibly Russian, and a locative that carries its own preposition renders
as *похований у в Авані* because `DescriptorPhrasings` supplies the preposition itself. None of that
needs a model to see. `check` runs over `entity_name_form` as loaded, which is thousands of rows that
cost nothing, and a check that fails on those is a wrong check rather than a bad run.

**`--sample` picks entities that already have forms, so the run is measurable.** Sonnet wrote every
row in `entity_name_form` during RUN-DESCRIBE. Asking a second model for the same entities, from the
same evidence, gives agreement per case against an answer key that was paid for once.

**Ordering is by how often an entity is the target of an existing claim.** Moses, Jerusalem, Judah
and David are targets of a large share of everything; a few hundred of them repair more lines than a
thousand in alphabetical order. `--targets` selects and orders that way; it is what a real run takes.

**Nothing here writes to the database.** `publish` leaves files under `Resources/`, which the API
loads on its next start like every other source, so a run can be read and argued with before
anything believes it.

A run leaves:

    manifest.json              the selection, its seed, and which entities went into which batch
    batches/batch-NNNN.json    the prompt payload, exactly as the model saw it
    out/batch-NNNN.jsonl       one object per entity: the forms, the model, the cost
    compare.md                 agreement per language and case against the loaded forms
"""

import argparse
import concurrent.futures
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

PROMPT_VERSION = 'forms-2'

# The texts a form can be read out of rather than guessed at. English is the rendering the rest of
# the corpus is keyed to; the other four are the Bible each language's own readers know, so the
# spelling asked for is the spelling that language's Bible actually uses.
RENDERING = 'KJV'
BY_LANGUAGE = {'ukr': 'UBIO', 'rus': 'RUSV', 'deu': 'LUTH1912', 'spa': 'RV1909'}

# The book codes the corpus's own book file publishes (BibleData-Book.csv, usx_code).
BOOKS = [
    'GEN', 'EXO', 'LEV', 'NUM', 'DEU', 'JOS', 'JDG', 'RUT', '1SA', '2SA', '1KI', '2KI', '1CH',
    '2CH', 'EZR', 'NEH', 'EST', 'JOB', 'PSA', 'PRO', 'ECC', 'SNG', 'ISA', 'JER', 'LAM', 'EZK',
    'DAN', 'HOS', 'JOL', 'AMO', 'OBA', 'JON', 'MIC', 'NAM', 'HAB', 'ZEP', 'HAG', 'ZEC', 'MAL',
    'MAT', 'MRK', 'LUK', 'JHN', 'ACT', 'ROM', '1CO', '2CO', 'GAL', 'EPH', 'PHP', 'COL', '1TH',
    '2TH', '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JN', '2JN', '3JN', 'JUD',
    'REV',
]

# Which cases each language owes. English and Spanish inflect nothing here -- Spanish says *de
# Moisés* -- so asking for a genitive would be asking for a word that does not exist.
CASES = {
    'eng': ['nominative'],
    'spa': ['nominative'],
    'deu': ['nominative', 'genitive'],
    'ukr': ['nominative', 'genitive', 'locative'],
    'rus': ['nominative', 'genitive', 'locative'],
}

VERSES_SHOWN = 4
BATCH_ENTITIES = 8


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
            f"Check that the container is running -- docker ps -- and that the SQL is valid.")
    out = process.stdout.decode('utf-8').strip()
    return json.loads(out) if out else None


def reference(book, chapter, verse):
    code = BOOKS[book - 1] if 1 <= book <= len(BOOKS) else f'book{book}'
    return f'{code} {chapter}:{verse}'


# ---------------------------------------------------------------------------- the check


# What a Ukrainian word can contain that a Russian one cannot, and the reverse. A model asked for
# Ukrainian and answering *Авиезер* is not making a spelling choice; it is answering in the other
# language, and one letter says so without a dictionary.
UKRAINIAN_ONLY = set('їієґЇІЄҐ')
RUSSIAN_ONLY = set('ыъэёЫЪЭЁ')
CYRILLIC = re.compile(r'[Ѐ-ӿ]')
LATIN = re.compile(r'[A-Za-zÀ-ÿ]')

# The phrasing supplies the preposition -- `Phrasing("похований у ", Locative)` -- so a
# locative that carries its own renders as *похований у в Авані*.
PREPOSITIONS = {
    'ukr': {'в', 'у', 'на', 'при', 'під', 'до', 'з', 'із', 'зі'},
    'rus': {'в', 'во', 'на', 'при', 'под', 'до', 'с', 'со', 'у'},
    'deu': {'in', 'im', 'zu', 'zum', 'zur', 'am', 'an', 'auf', 'bei', 'beim', 'nach', 'von', 'vom'},
    'spa': {'en', 'a', 'de', 'del', 'al'},
    'eng': {'in', 'at', 'on', 'of'},
}

# A German genitive is normally written with its article -- *des Bachs* -- and Spanish names a place
# with one. Neither belongs in the form for the same reason the preposition does not: what stands
# before the name is the phrasing's, and a form carrying its own doubles it.
ARTICLES = {
    'deu': {'der', 'die', 'das', 'des', 'dem', 'den'},
    'spa': {'el', 'la', 'los', 'las'},
    'eng': {'the'},
}

SLAVIC = ('ukr', 'rus')

# How much of the nominative an inflected form may drop before it stops being the same word.
# *Київ* -> *Києва* loses two and *асирець* -> *асирця* loses three, because a fleeting vowel goes
# with the ending; *моавітяни* -> *моавітян* is a truncation rather than a suffix, so the rule is a
# shared stem rather than a prefix. The floor is one short of the nominative, because *Ай* -> *Аю*
# has only one letter to share. Tuned on the 1,167 entities Sonnet has already produced forms for:
# at three, everything it still refuses is a form a reader would refuse too.
STEM_SLACK = 3
SHORTEST_STEM = 2


def stem(one, other):
    """How many leading characters two words share, ignoring case."""
    one, other = one.casefold(), other.casefold()
    shared = 0
    while shared < len(one) and shared < len(other) and one[shared] == other[shared]:
        shared += 1
    return shared


def bare_words(language, value):
    """
    The parts of a form to compare one by one: what the sentence supplies for itself taken off the
    front, and a hyphenated name split, because *Авел-Бет-Мааха* inflects its first part and not the
    rest and comparing the whole string calls that a different name.

    Never down to nothing, because a name can be spelt like a preposition: German *zur* is a
    preposition and *Zur* is a Midianite prince, and stripping the only word he has called *Zurs* a
    different name from *Zur*.
    """
    words = [word for word in value.split() if word.strip('.,')]
    while len(words) > 1 and (words[0].strip('.,').casefold() in PREPOSITIONS.get(language, ())
                              or words[0].strip('.,').casefold() in ARTICLES.get(language, ())):
        words = words[1:]
    return [part for word in words for part in word.split('-') if part]


def faults(language, forms, english):
    """
    Everything visibly wrong with one entity's forms in one language, as (kind, sentence) pairs.

    Empty means nothing deterministic can see a fault -- which is not the same as correct. A dative
    where a genitive was asked for shares the stem and passes here; only a reader catches that.
    """
    found = []
    nominative = (forms.get('nominative') or '').strip()
    if not nominative:
        return [('no nominative', 'no nominative, so nothing else can be checked against it')]

    for case in CASES.get(language, ['nominative']):
        value = (forms.get(case) or '').strip()
        if not value:
            continue
        words = [word for word in value.split() if word.strip('.,')]
        first = words[0].strip('.,').casefold() if words else ''
        if len(words) > 1 and first in PREPOSITIONS.get(language, ()):
            found.append(('carries a preposition',
                          f'{case} "{value}" starts with a preposition; the phrasing supplies one, '
                          f'so this renders as "у {value}" -- give the bare form'))
        elif len(words) > 1 and first in ARTICLES.get(language, ()):
            found.append(('carries an article',
                          f'{case} "{value}" starts with an article, which the phrase around it '
                          f'supplies -- give the bare form'))
        if language in SLAVIC:
            if not CYRILLIC.search(value):
                found.append(('wrong alphabet', f'{case} "{value}" is not in Cyrillic'))
            elif LATIN.search(value):
                found.append(('two alphabets in one word',
                              f'{case} "{value}" mixes Cyrillic and Latin letters'))
            elif language == 'ukr' and set(value) & RUSSIAN_ONLY:
                found.append(('the other language',
                              f'{case} "{value}" uses letters Ukrainian does not have; it is Russian'))
            elif language == 'rus' and set(value) & UKRAINIAN_ONLY:
                found.append(('the other language',
                              f'{case} "{value}" uses letters Russian does not have; it is Ukrainian'))
        elif not LATIN.search(value):
            found.append(('wrong alphabet', f'{case} "{value}" is not in the Latin alphabet'))
        if language in SLAVIC and english and value.casefold() == english.casefold():
            found.append(('the English name',
                          f'{case} "{value}" is the English name, not a {language} form'))
        if case == 'nominative':
            continue

        inflected_words = bare_words(language, value)
        base_words = bare_words(language, nominative)
        if len(inflected_words) != len(base_words):
            found.append(('a different number of words',
                          f'{case} "{value}" has {len(inflected_words)} words where the nominative '
                          f'"{nominative}" has {len(base_words)}; an inflection does not change how '
                          f'many words a name is'))
            continue
        for inflected, plain in zip(inflected_words, base_words):
            shared = stem(plain, inflected)
            if shared < len(plain) - STEM_SLACK or shared < max(1, min(SHORTEST_STEM, len(plain) - 2)):
                found.append(('not the same name',
                              f'{case} "{value}" shares only {shared} characters with the '
                              f'nominative "{nominative}"; it is not a form of the same name'))
                break
    return found


def loaded_forms():
    """Every form `entity_name_form` holds, keyed by entity and language: the answer key."""
    return psql("""
        SELECT coalesce(json_agg(json_build_object(
                   'slug', e.slug, 'kind', e.kind, 'name', e.name, 'language', f.language,
                   'case', f.grammatical_case, 'form', f.form, 'source', f.source)), '[]')
        FROM entity_name_form f JOIN entity e ON e.id = f.entity_id
    """)


def keyed(rows):
    by_entity = {}
    for row in rows:
        entity = by_entity.setdefault(row['slug'], {'kind': row['kind'], 'name': row['name'],
                                                    'names': {}, 'source': row['source']})
        entity['names'].setdefault(row['language'], {})[row['case']] = row['form']
    return by_entity


def checked(entities, args, what):
    """
    Report `faults()` over {slug: {'name', 'names'}}, whatever produced them.

    The same function over a run and over the database on purpose: a check that is one thing for
    the answer key and another for the run being judged is two checks, and the second one is the
    one nobody proved.
    """
    tally, examples, clean = {}, {}, 0
    for slug, entity in sorted(entities.items()):
        english = (entity['names'].get('eng') or {}).get('nominative') or entity['name']
        faulty = False
        for language, forms in sorted(entity['names'].items()):
            for kind, fault in faults(language, forms, english):
                key_name = (language, kind)
                tally[key_name] = tally.get(key_name, 0) + 1
                examples.setdefault(key_name, []).append(f'{slug}: {fault}')
                faulty = True
        clean += not faulty

    print(f'{len(entities)} entities carry forms, {what}.')
    print(f'{clean} of them have nothing the check can see, '
          f'{len(entities) - clean} have something.\n')
    for key_name in sorted(tally, key=lambda k: -tally[k]):
        print(f'{tally[key_name]:5}  {key_name[0]}  {key_name[1]}')
        for line in examples[key_name][:args.examples]:
            print(f'         {line}')
    return tally


def check(args):
    """
    The check: over a run's own output when given one, over the forms already loaded otherwise.

    `--dir` is what a run is judged by before anything is written -- a refused form falls back to
    the English name and a wrong one cannot be told from a right one by a reader, so the refusals
    are the number that decides whether a pass is publishable.
    """
    if args.dir:
        rows = produced(args.dir)
        entities = {row['entity']: {'name': row['name'], 'names': row['names']} for row in rows}
        checked(entities, args, f'produced by {rows[0]["model"]} under {args.dir}')
        return 0

    key = keyed(loaded_forms())
    checked(key, args, f'written by {next(iter(key.values()))["source"]}')
    return 0


# ---------------------------------------------------------------------------- the payload


def described_entities():
    """
    Every entity, with what a form can be read out of: its name, its meaning, and its first verses.

    `distinguisher` is absent for the same reason `scripts/descriptors.py` leaves it out -- it is
    BibleData's sentence and this pass exists to stop showing it -- but here it is simply irrelevant:
    a name's genitive is not in an English gloss.
    """
    return psql(f"""
        SELECT coalesce(json_agg(json_build_object(
                   'slug', e.slug, 'kind', e.kind, 'name', e.name,
                   'sex', e.sex, 'place_kind', e.place_kind,
                   'meaning', (SELECT n.meaning FROM entity_name n
                               WHERE n.entity_id = e.id AND n.meaning IS NOT NULL LIMIT 1),
                   'verses', (SELECT coalesce(json_agg(json_build_array(
                                 ev.canonical_book, ev.canonical_chapter, ev.canonical_verse)
                                 ORDER BY ev.canonical_book, ev.canonical_chapter,
                                          ev.canonical_verse), '[]')
                              FROM (SELECT * FROM entity_verse v WHERE v.entity_id = e.id
                                    ORDER BY v.canonical_book, v.canonical_chapter,
                                             v.canonical_verse
                                    LIMIT {VERSES_SHOWN}) ev),
                   'targeted', (SELECT count(*) FROM entity_descriptor d
                                WHERE d.target_entity_id = e.id))
                   ORDER BY e.slug), '[]')
        FROM entity e
    """)


def rendering(slug):
    """One text's verses, keyed by canonical address. Words joined with their trailers, as read."""
    return psql(f"""
        SELECT coalesce(json_agg(json_build_array(x.b, x.c, x.v, x.line)), '[]')
        FROM (
            SELECT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v,
                   string_agg(w.text || w.trailer, '' ORDER BY w.position) AS line
            FROM verse ve
            JOIN verse_reference r ON r.verse_id = ve.id AND r.is_primary
            JOIN word w ON w.verse_id = ve.id
            WHERE ve.text_id = (SELECT id FROM text WHERE upper(slug) = '{slug}')
            GROUP BY 1, 2, 3
        ) x
    """)


def cache(directory, name, produce):
    path = os.path.join(directory, name + '.json')
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            return json.load(handle)
    value = produce()
    os.makedirs(directory, exist_ok=True)
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(value, handle, ensure_ascii=False)
    return value


def payload(entity, texts, languages):
    lines = []
    for book, chapter, verse in entity['verses']:
        key = (book, chapter, verse)
        line = {'reference': reference(book, chapter, verse),
                'english': texts[RENDERING].get(key)}
        for language in languages:
            text = BY_LANGUAGE.get(language)
            if text and texts.get(text, {}).get(key):
                line[language] = texts[text][key]
        lines.append(line)
    return {
        'entity': entity['slug'],
        'kind': entity['kind'],
        'name': entity['name'],
        'sex': entity.get('sex'),
        'place_kind': entity.get('place_kind'),
        'meaning': entity.get('meaning'),
        'verses': lines,
    }


def system_prompt(languages):
    asked = ', '.join(languages)
    return f"""\
You give a Biblical name in the grammatical forms a sentence needs it in, and nothing else.

For each entity you are given its slug, its kind, its English name, what the name means, and the
text of a few verses it is named in -- in English and in each language you are asked for, so the
spelling is one you can read rather than one you have to guess.

Give the name in {asked}, in the spelling that language's own Bible uses. The Ukrainian is Ohienko,
the Russian is Synodal, the German is Luther and the Spanish is Reina-Valera, and the verses you were
shown are from exactly those.

Which forms each language owes:

  eng   nominative only
  spa   nominative only -- Spanish says *de Moisés* and has no genitive case
  deu   nominative and genitive: *Mose*, *Moses*
  ukr   nominative and genitive: *Мойсей*, *Мойсея* -- and for a place, the locative as well
  rus   nominative and genitive: *Моисей*, *Моисея* -- and for a place, the locative as well

The genitive is what the rendering puts the name into: *тесть Мойсея*, not *тесть Мойсей*. The
locative is what *buried in* and *a city in* put a place into, and no genitive stands in for it:
*похований у Хевроні*, not *у Хеврона*; *місто в Юдеї*.

Five things go wrong often enough to be worth naming:

- **Give the bare form, with no preposition.** The sentence supplies its own: it renders *похований
  у* and then your form. *Хевроні*, never *у Хевроні*; *Adullam*, never *in Adullam*.
- **Watch the case the verse happens to be in.** A verse that says *Мегуманові* is a dative, and the
  nominative is what you owe. Read the verse to learn the spelling, not the ending.
- **A genitive, not a possessive adjective.** Ukrainian and Russian will happily make an adjective
  out of a name, and it is not what the sentence takes: *сина Аммігуда*, never *син Аммігудів*;
  *Адіна*, never *Адінових*. If the form does not answer *whose*, it is the wrong form.
- **A people's name is plural in every form**: *моавітяни*, *моавітян*.
- **If you genuinely do not know a language's form, leave that language out entirely.** A wrong
  ending is worse than a missing one, because a reader cannot tell it is wrong and the English name
  is what a missing form falls back to.

## The reply

A single JSON array and nothing else -- no prose before or after, no code fence needed. One object
per entity, in the order given, with exactly these fields:

  entity  the slug you were given, unchanged
  names   an object of language code -> {{nominative, genitive, locative}}, cases you have only

Every entity you were given must appear exactly once.
"""


def extract(args):
    languages = args.languages.split(',')
    directory = args.out
    os.makedirs(os.path.join(directory, 'batches'), exist_ok=True)
    cache_dir = os.path.join(directory, 'corpus')

    all_entities = cache(cache_dir, 'entities', described_entities)
    texts = {RENDERING: cache(cache_dir, RENDERING, lambda: rendering(RENDERING))}
    for language in languages:
        slug = BY_LANGUAGE.get(language)
        if slug:
            texts[slug] = cache(cache_dir, slug, lambda s=slug: rendering(s))
    texts = {name: {(b, c, v): line for b, c, v, line in rows} for name, rows in texts.items()}

    key = keyed(loaded_forms())
    if args.repair:
        # The entities the check refuses on rows that are already loaded: PRB-0435's 84 locatives
        # carrying their own preposition, and the handful of forms that are a different name from
        # the nominative beside them. Asked again rather than edited, so what lands is a form
        # somebody produced and not one this script inferred.
        refused = {slug for slug, entity in key.items()
                   for language, forms in entity['names'].items()
                   if language in languages
                   and faults(language, forms,
                              (entity['names'].get('eng') or {}).get('nominative') or entity['name'])}
        chosen = sorted((e for e in all_entities if e['slug'] in refused),
                        key=lambda e: e['slug'])
    elif args.gaps:
        # The entities a clause already points at that are missing a case a phrase will ask for.
        # `--targets` cannot see these: it takes entities with no forms at all, and the line that
        # reads *місто в Judah* is Judah having a genitive and no locative rather than nothing.
        #
        # Which case a phrase wants is decided by the relation, and that table is C#'s
        # (`DescriptorPhrasings`). Rather than keep a second copy of it here -- PRB-0447 is what
        # that costs -- the rule is taken from the kind: a place can stand in a locative and a
        # person cannot, so a targeted place wants all three cases and a targeted person two.
        wanted = {'place': ('nominative', 'genitive', 'locative')}
        chosen = []
        for entity in all_entities:
            if not entity['targeted']:
                continue
            cases = wanted.get(entity['kind'], ('nominative', 'genitive'))
            held = key.get(entity['slug'], {}).get('names', {})
            if any(case not in (held.get(language) or {})
                   for language in languages if language != 'eng'
                   for case in cases):
                chosen.append(entity)
        chosen.sort(key=lambda e: (-e['targeted'], e['slug']))
        chosen = chosen[:args.sample] if args.sample else chosen
    elif args.targets:
        # What a real run takes: the entities somebody else's claim names, most-named first, and
        # only the ones no pass has given forms to.
        chosen = [e for e in all_entities if e['targeted'] and e['slug'] not in key]
        chosen.sort(key=lambda e: (-e['targeted'], e['slug']))
        chosen = chosen[:args.sample] if args.sample else chosen
    else:
        # What the measurement takes: entities whose forms already exist, so there is a key.
        having = [e for e in all_entities if e['slug'] in key and e['verses']]
        random.Random(args.seed).shuffle(having)
        chosen = sorted(having[:args.sample], key=lambda e: e['slug'])

    batches = [chosen[at:at + args.batch_entities]
               for at in range(0, len(chosen), args.batch_entities)]
    for number, batch in enumerate(batches):
        with open(os.path.join(directory, 'batches', f'batch-{number:04}.json'),
                  'w', encoding='utf-8') as handle:
            json.dump([payload(entity, texts, languages) for entity in batch],
                      handle, ensure_ascii=False, indent=1)

    with open(os.path.join(directory, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump({'prompt_version': PROMPT_VERSION, 'languages': languages,
                   'selection': 'gaps' if args.gaps else 'repair' if args.repair
                                else 'targets' if args.targets else 'sample-with-forms',
                   'seed': args.seed, 'entities': len(chosen), 'batches': len(batches),
                   'batch_entities': args.batch_entities,
                   'slugs': [[e['slug'] for e in batch] for batch in batches]},
                  handle, ensure_ascii=False, indent=1)
    print(f'{len(chosen)} entities in {len(batches)} batches under {directory}.')
    return 0


# ---------------------------------------------------------------------------- asking


def executable():
    found = shutil.which('claude')
    if found:
        return found
    fallback = os.path.expanduser(r'~\.local\bin\claude.exe')
    if os.path.exists(fallback):
        return fallback
    raise SystemExit(
        'the claude CLI is not on PATH and is not at ~/.local/bin/claude.exe. '
        'Install it, or pass its path in the CLAUDE environment variable.')


CALL_TIMEOUT = 600
CALL_ATTEMPTS = 2


def call(prompt, model, prompt_text, effort=None):
    command = [
        os.environ.get('CLAUDE') or executable(),
        '-p', '--system-prompt', prompt_text, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1',
    ] + (['--effort', effort] if effort else [])
    for attempt in range(CALL_ATTEMPTS):
        last = None
        try:
            process = subprocess.run(
                command, input=prompt.encode('utf-8'),
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=CALL_TIMEOUT)
        except subprocess.TimeoutExpired:
            last = f'the harness answered nothing in {CALL_TIMEOUT}s and was killed'
        else:
            if process.returncode != 0:
                said = process.stderr.decode('utf-8', 'replace').strip()
                last = said or f'the harness exited {process.returncode} and said nothing'
            else:
                try:
                    return json.loads(process.stdout.decode('utf-8', 'replace')), None
                except json.JSONDecodeError as broken:
                    last = f'the harness did not return JSON: {broken}'
        if attempt + 1 == CALL_ATTEMPTS:
            return None, last
        time.sleep(5)
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


def ask(args):
    directory = args.dir
    with open(os.path.join(directory, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    languages = manifest['languages']
    prompt_text = system_prompt(languages)
    os.makedirs(os.path.join(directory, 'out'), exist_ok=True)
    with open(os.path.join(directory, 'asked.json'), 'w', encoding='utf-8') as handle:
        json.dump({'model': args.model, 'effort': args.effort,
                   'prompt_version': PROMPT_VERSION, 'system_prompt': prompt_text},
                  handle, ensure_ascii=False, indent=1)

    names = sorted(os.listdir(os.path.join(directory, 'batches')))
    lock = threading.Lock()
    done = [0]

    def one(name):
        number = name[len('batch-'):-len('.json')]
        out = os.path.join(directory, 'out', f'batch-{number}.jsonl')
        if os.path.exists(out) and not args.again:
            return name, 'already done', 0.0
        with open(os.path.join(directory, 'batches', name), encoding='utf-8') as handle:
            batch = json.load(handle)
        started = time.time()
        outcome, failed = call(json.dumps(batch, ensure_ascii=False, indent=1), args.model,
                               prompt_text, args.effort)
        if failed:
            return name, failed, 0.0
        answers = parse(outcome.get('result'))
        if answers is None:
            return name, 'the reply held no JSON array', outcome.get('total_cost_usd') or 0.0

        asked = {entity['entity']: entity for entity in batch}
        by_slug = {a.get('entity'): a for a in answers if isinstance(a, dict)}
        cost = outcome.get('total_cost_usd') or 0.0
        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        rows = []
        for slug, entity in asked.items():
            answer = by_slug.get(slug)
            rows.append({
                'entity': slug, 'kind': entity['kind'], 'name': entity['name'],
                'names': (answer or {}).get('names') or {},
                'answered': answer is not None,
                'model': model, 'seconds': round(time.time() - started, 1),
                'cost': round(cost / max(1, len(asked)), 6),
            })
        with open(out, 'w', encoding='utf-8') as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        # A short batch is a failure, and a harness that does not count one does not notice.
        short = len(asked) - sum(row['answered'] for row in rows)
        return name, (f'{short} of {len(asked)} entities missing' if short else 'ok'), cost

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        for name, how, cost in pool.map(one, names):
            with lock:
                done[0] += 1
                print(f'[{done[0]}/{len(names)}] {name}  {how}  ${cost:.4f}', flush=True)
    return 0


# ---------------------------------------------------------------------------- comparing


def produced(directory):
    rows = []
    out = os.path.join(directory, 'out')
    for name in sorted(os.listdir(out)) if os.path.exists(out) else []:
        with open(os.path.join(out, name), encoding='utf-8') as handle:
            rows += [json.loads(line) for line in handle if line.strip()]
    return rows


def compare(args):
    directory = args.dir
    with open(os.path.join(directory, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    with open(os.path.join(directory, 'asked.json'), encoding='utf-8') as handle:
        asked_as = json.load(handle)
    model, effort = asked_as['model'], asked_as.get('effort')
    key = keyed(loaded_forms())
    rows = produced(directory)

    agreement, differences = {}, []
    for row in rows:
        theirs = key.get(row['entity'])
        if not theirs:
            continue
        english = (theirs['names'].get('eng') or {}).get('nominative') or theirs['name']
        for language in manifest['languages']:
            for case in CASES.get(language, ['nominative']):
                mine = ((row['names'].get(language) or {}).get(case) or '').strip()
                sonnet = ((theirs['names'].get(language) or {}).get(case) or '').strip()
                if not sonnet:
                    continue
                tally = agreement.setdefault((language, case), {'same': 0, 'differ': 0, 'absent': 0})
                if not mine:
                    tally['absent'] += 1
                elif mine == sonnet:
                    tally['same'] += 1
                else:
                    tally['differ'] += 1
                    differences.append({'entity': row['entity'], 'kind': row['kind'],
                                        'language': language, 'case': case,
                                        'sonnet': sonnet, model: mine, 'english': english})

    faulty = {'sonnet': {}, model: {}}
    for row in rows:
        theirs = key.get(row['entity'], {})
        english = ((theirs.get('names') or {}).get('eng') or {}).get('nominative') or row['name']
        for language in manifest['languages']:
            for whose, forms in ((model, row['names'].get(language) or {}),
                                 ('sonnet', (theirs.get('names') or {}).get(language) or {})):
                if not forms:
                    continue
                for _, fault in faults(language, forms, english):
                    faulty[whose].setdefault(language, []).append(f"{row['entity']}: {fault}")

    cost = sum(row['cost'] for row in rows)
    lines = [f'# {model} against the loaded forms', '', f'Effort: {effort or "the default"}.', '',
             f"{len(rows)} entities asked, {sum(r['answered'] for r in rows)} answered, "
             f'{len([r for r in rows if key.get(r["entity"])])} with a form to compare against.',
             f'${cost:.4f} in total, ${cost / max(1, len(rows)):.5f} an entity.', '',
             '| language | case | same | differ | absent | agreement |',
             '|---|---|---|---|---|---|']
    for language, case in sorted(agreement):
        tally = agreement[(language, case)]
        total = sum(tally.values())
        lines.append(f'| {language} | {case} | {tally["same"]} | {tally["differ"]} | '
                     f'{tally["absent"]} | {100 * tally["same"] / max(1, total):.1f}% |')

    lines += ['', '## What the check sees, on both sides', '',
              '| language | ' + f'{model} | sonnet |', '|---|---|---|']
    for language in manifest['languages']:
        lines.append(f'| {language} | {len(faulty[model].get(language, []))} | '
                     f'{len(faulty["sonnet"].get(language, []))} |')

    for whose in (model, 'sonnet'):
        lines += ['', f'### {whose}', '']
        for language, found in sorted(faulty[whose].items()):
            for line in found[:args.examples]:
                lines.append(f'- {language} {line}')

    with open(os.path.join(directory, 'compare.md'), 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    with open(os.path.join(directory, 'differences.json'), 'w', encoding='utf-8') as handle:
        json.dump(differences, handle, ensure_ascii=False, indent=1)
    print('\n'.join(lines))
    print(f'\n{len(differences)} differences in {directory}/differences.json')
    return 0


# ---------------------------------------------------------------------------- publishing


def bare(language, value):
    """The form with what the phrase supplies for itself taken off the front (PRB-0435)."""
    words = value.split()
    while len(words) > 1 and (words[0].strip('.,').casefold() in PREPOSITIONS.get(language, ())
                              or words[0].strip('.,').casefold() in ARTICLES.get(language, ())):
        words = words[1:]
    return ' '.join(words)


SOURCE = re.compile(r'^.* by (?P<model>.+), asked (?P<asked>.+)$')


def republish(args):
    """
    Every loaded form carrying its own preposition, as a file that supersedes it: the same form
    without it.

    Read off the database as it stands rather than off a list, because the generation pass is still
    running and every batch it loads adds more of them -- 84 rows over 1,167 entities when PRB-0435
    was filed, 399 over 4,457 a day later. So this is re-run rather than kept, and the file it
    writes is replaced whole.

    Asking again does not close this. The run that was asked produced a Ukrainian locative for 11 of
    the 49 entities and left the rest out, which is the prompt's rule working -- a form the model
    will not vouch for is left out rather than invented. But nothing needs to be asked: *в Авані* is
    already the right word with a preposition glued to the front, and taking the preposition off is
    a token removed rather than an ending guessed at. So the form here is the one the original pass
    wrote, and it carries that pass's model and date, because that is whose word it is.
    """
    key = keyed(loaded_forms())
    lines, entities, forms = [], 0, 0
    for slug, entity in sorted(key.items()):
        english = (entity['names'].get('eng') or {}).get('nominative') or entity['name']
        names = {}
        for language, stored in entity['names'].items():
            repaired = {case: bare(language, (value or '').strip())
                        for case, value in stored.items()
                        if bare(language, (value or '').strip()) != (value or '').strip()}
            if repaired and not [f for case, value in repaired.items()
                                 for f in faults(language, dict(stored, **{case: value}), english)
                                 if 'carries' not in f[0]]:
                names[language] = dict(stored, **repaired)

        if not names:
            continue

        said = SOURCE.match(entity['source'])
        entities += 1
        forms += sum(len(cases) for cases in names.values())
        lines.append(json.dumps({'entity': slug, 'names': names,
                                 'model': said['model'], 'askedAt': said['asked']},
                                ensure_ascii=False))

    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, f'{args.name}.jsonl')
    with open(path, 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')
    print(f'{entities} entities and {forms} forms written to {path}, '
          f'each one a form already loaded with what the phrase supplies taken off the front.')
    return 0


def publish(args):
    """
    A run, as the files `EntityNameFormLoader` reads on the next start.

    Nothing here writes to the database. What a pass produces is a by-product of running a model
    over the corpus, so it lands beside the corpus and is loaded like every other source -- which
    also means a run can be read, argued with and re-published before anything believes it.

    **What the check refuses does not go in the file.** A case it refuses is dropped and a language
    whose nominative it refuses is dropped whole, because there is then nothing to check the rest
    against. A dropped form falls back to the English name, which is DOC-0191's rule and the only
    outcome a reader can see for what it is.
    """
    rows = produced(args.dir)
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        languages = json.load(handle)['languages']

    kept, dropped, stripped, entities = 0, {}, 0, 0
    lines = []
    for row in rows:
        english = (row['names'].get('eng') or {}).get('nominative') or row['name']
        names = {}
        for language in languages:
            forms = row['names'].get(language) or {}
            forms = {case: bare(language, (value or '').strip())
                     for case, value in forms.items() if case in CASES.get(language, [])}
            stripped += sum(1 for case, value in forms.items()
                            if value != (row['names'][language][case] or '').strip())
            forms = {case: value for case, value in forms.items() if value}
            if not forms:
                continue

            refused = {}
            for kind, _ in faults(language, forms, english):
                refused[kind] = refused.get(kind, 0) + 1
            if 'no nominative' in refused:
                dropped[language] = dropped.get(language, 0) + len(forms)
                continue

            # A fault names the case in its own sentence, so the case is refused rather than the
            # language: a sound nominative is worth keeping when the genitive beside it is not.
            sound = {}
            for case, value in forms.items():
                if faults(language, {'nominative': forms['nominative'], case: value}, english) \
                        and case != 'nominative':
                    dropped[language] = dropped.get(language, 0) + 1
                    continue
                sound[case] = value
            names[language] = sound
            kept += len(sound)

        if not names:
            continue

        entities += 1
        lines.append(json.dumps({'entity': row['entity'], 'names': names,
                                 'model': row['model'], 'askedAt': args.asked_at},
                                ensure_ascii=False))

    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, f'{args.name}.jsonl')
    with open(path, 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')

    print(f'{entities} entities and {kept} forms written to {path}.')
    print(f'{stripped} forms had a preposition or an article taken off the front.')
    for language in sorted(dropped):
        print(f'{dropped[language]:5} {language} forms the check refused, so they are not in it')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    commands = parser.add_subparsers(dest='command', required=True)

    checker = commands.add_parser('check', help='run the deterministic check')
    checker.add_argument('--dir', default=None,
                         help="a run's own output, checked before anything is published; the "
                              'loaded forms when it is left off')
    checker.add_argument('--examples', type=int, default=3)
    checker.set_defaults(run=check)

    publisher = commands.add_parser('publish', help='write a run as the files the loader reads')
    publisher.add_argument('--dir', required=True)
    publisher.add_argument('--out', required=True)
    publisher.add_argument('--name', default='forms')
    publisher.add_argument('--asked-at', required=True, help='the date of the run, as YYYY-MM-DD')
    publisher.set_defaults(run=publish)

    republisher = commands.add_parser(
        'republish', help="PRB-0435's rows, as a file that supersedes them without the preposition")
    republisher.add_argument('--out', required=True)
    republisher.add_argument('--name', default='republished')
    republisher.set_defaults(run=republish)

    extractor = commands.add_parser('extract', help='write the prompt payloads')
    extractor.add_argument('--out', required=True)
    extractor.add_argument('--sample', type=int, default=0)
    extractor.add_argument('--seed', type=int, default=11)
    extractor.add_argument('--repair', action='store_true',
                           help='the entities whose loaded forms the check refuses, asked again')
    extractor.add_argument('--gaps', action='store_true',
                           help='Entities a clause already names that lack a case its phrase will '
                                'ask for, most-named first. Wider than --targets, which only takes '
                                'entities no pass has given any form to.')
    extractor.add_argument('--targets', action='store_true',
                           help='select what a real run takes: entities named by a claim, most '
                                'named first, that no pass has given forms to')
    extractor.add_argument('--languages', default='eng,ukr,rus,deu')
    extractor.add_argument('--batch-entities', type=int, default=BATCH_ENTITIES)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='ask a model for the forms')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--model', default='haiku')
    asker.add_argument('--workers', type=int, default=4)
    asker.add_argument('--effort', default=None,
                       help='low, medium, high, xhigh or max. Declining a name needs no reasoning '
                            'and the default spends thousands of thinking tokens on it.')
    asker.add_argument('--again', action='store_true')
    asker.set_defaults(run=ask)

    comparer = commands.add_parser('compare', help='agreement against the loaded forms')
    comparer.add_argument('--dir', required=True)
    comparer.add_argument('--examples', type=int, default=6)
    comparer.set_defaults(run=compare)

    args = parser.parse_args()
    raise SystemExit(args.run(args))


if __name__ == '__main__':
    main()
