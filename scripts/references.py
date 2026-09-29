"""
Which word of the verse speaks of the person BibleData lists there, where the verse does not print the
name? Ask, passage in hand, and write down only the word.

BibleData cites verses for a person that no word of this corpus names them in. After every pass that
reads a printed name, what is left of those in a verse that does not print the name is a title (*the
king of Babylon*, *the man of God*, *the angel of the LORD*), an epithet (*Master*, *Rabbi*), another
name, or a pronoun. No rule settles whom a title or a pronoun means; the passage does, where it does.
So each reference is a question for a model reading the passage: which word of this verse, in the
original, refers to this person -- chosen from the people the passage names, or none.

    python scripts/references.py measure
    python scripts/references.py extract --set heldout --out .references/heldout
    python scripts/references.py ask     --dir .references/heldout --model claude-sonnet-5-5 --workers 3
    python scripts/references.py check   --dir .references/heldout --model claude-sonnet-5-5 --workers 3
    python scripts/references.py score   --dir .references/heldout
    python scripts/references.py extract --set read --out .references/read
    python scripts/references.py ask     --dir .references/read ...; check ...
    python scripts/references.py publish --dir .references/read --heldout .references/heldout \\
                                         --accept name title

Six things about the design are load-bearing:

**The answer is a word, not a verse.** A verse that "refers to" somebody is a looser claim than the
corpus holds anywhere else; what is written is the one word that stands for the person -- a name, the
head noun of a title (*king* in *the king of Babylon*), or a pronoun that is a word of its own -- and a
verse that speaks of them only through a verb's person or a suffix on another noun names nobody at
the word and is counted, not written.

**The asked record is one of several.** Every question lists the people this corpus's own words name in
the passage beside the one asked about, so a pronoun can be given to the man it means rather than to
the man it was asked about. BibleData's sentences and its verse list are never shown; the record's
line is the corpus's own, where it has one.

**Held out first, per class.** The same prompt is put to references whose answer this corpus already
has -- BibleData and our words agree on the person, on a word that is not the printed name -- with our
answer hidden, and to verses of the same passages that neither lists, which should come back empty.
Precision is measured per class of word (a name, a title, a pronoun), and only a class that reads
right goes on to be written.

**A positive is read twice.** A second reader is handed the one word and the one claim and asked to
refuse, as the relationship pass does; only what both readings say is written.

**Nothing is overwritten.** The answers are published as a file; the loader writes a word only where no
annotation stands on it, and carries it across the links as every other pass does.

**The model never touches the database.** It is given a payload and returns JSON; every answer is kept
with the model, the effort, the prompt version and the date.

A run leaves, under its directory (not committed):

    items.json                 every reference asked, with the answer where one is known
    manifest.json              the selection, and which items went into which batch
    batches/batch-NNNN.json    the prompt payload, exactly as the model saw it
    out/batch-NNNN.jsonl       the reading of each item, with what its batch cost
    check/batch-NNNN.jsonl     the second reading of each positive
    report.md                  (held out) precision per class; (read) what was answered

and `publish` writes the answers kept for the corpus under Resources/Essenthos/references.
"""

import argparse
import collections
import datetime
import json
import os
import random
import re

import descriptors as shared
import relationships as harness

PROMPT_VERSION = 'references-2'
CHECK_VERSION = 'references-check-2'

HEBREW, GREEK = shared.HEBREW, shared.GREEK
LAST_OLD_TESTAMENT_BOOK = shared.LAST_OLD_TESTAMENT_BOOK

WITNESS = 'BibleData%'
PERSON = 'person'

# BibleData's references these records stand for are decided already, by rulings of the owner's
# rather than by reading: YHVH where the verse says elohim, and records that are not names.
DECIDED = {'yhvh', 'yhvh-2', 'earth', 'heaven', 'waters', 'thegarden', 'daughter', 'death', 'admin',
           'adespicableperson'}

# The kinds of name row that are not a spelling of the record's name.
NOT_A_NAME = ('title', 'description', 'term')

NAME, TITLE, PRONOUN, IMPLIED = 'name', 'title', 'pronoun', 'implied'
KINDS = (NAME, TITLE, PRONOUN, IMPLIED)
CLASSES = (NAME, TITLE, PRONOUN)
NONE = 'none'

# What the model is shown. A chapter is shown whole up to a length where it stops being a chapter;
# past that the window leans backwards, where the antecedent of a pronoun and the naming of a title
# stand.
CHAPTER_SHOWN_WHOLE = 40
WINDOW_BEFORE = 20
WINDOW_AFTER = 5
OTHERS_SHOWN = 10

QUESTIONS_PER_BATCH = 16
ITEMS_PER_BATCH = 36
BATCH_VERSES = 200
BATCH_ORIGINALS = 36

CHECK_ITEMS_PER_BATCH = 20
CHECK_BEFORE = 8
CHECK_AFTER = 1

# Decoys: verses of a held-out passage that neither BibleData nor this corpus lists for the person,
# within this reach of an asked verse, one per question. They are the known negatives.
DECOY_REACH = 3
DECOYS_PER_QUESTION = 1

ACCEPTED_PRECISION = 0.99
ACCEPTED_ANSWERED = 300

# Below this the first reading says itself that the text barely settles it; such an answer goes to the
# owner's review list even where the second reading upholds it.
LEAST_CONFIDENCE = 0.7

# The held-out decoys a model named the person in, read by hand against the passage.
ADJUDICATED = 'adjudicated.json'

PUBLISHED = os.path.join('Resources', 'Essenthos', 'references')
REVIEW = os.path.join('Resources', 'Essenthos', 'review', 'reference-reading-disagreements.json')
RECORDS_PER_FILE = 200

SYSTEM = """\
You are reading Bible passages to find the word by which a verse speaks of a particular person.

Each question names a person under `record` -- the corpus's name for them and a line that tells them
apart from others of the name -- and lists under `others` the other people this corpus finds named in
the passage. For each item of the question you are given a verse. The King James text of the passage
around the verses is under `passages`; the verse itself is under `originals`, in the original word by
word -- BHSA Hebrew for the Old Testament, Nestle 1904 Greek for the New -- as
`number surface lexeme strong morphology "gloss"`, followed by `| KJV` and the King James words linked
to that original word (a word marked `~` was linked by a statistical aligner and may be wrong). The
Hebrew article, conjunctions and prepositions are words of their own.

For each item, find the word of the verse, in the original, that refers to the person in `record`:

- a **name**: another name or spelling of the person (Christ, Elias);
- a **title**: the head noun of a title, epithet or description that stands for the person -- `king` in
  "the king of Babylon", `man` in "the man of God", `angel` in "the angel of the LORD", `Master`,
  `Rabbi`, `Lord` -- give the number of that head noun, not of the article or the genitive after it;
- a **pronoun**: a pronoun that is a word of its own (Hebrew הוּא, אַתָּה, אֲנִי; Greek αὐτός, σύ, ἐγώ,
  οὗτος, ἐκεῖνος), or a Hebrew preposition or object marker whose suffix is the pronoun (לוֹ "to him",
  אֵלָיו "unto him", אֹתוֹ "him").

If the verse speaks of the person only through a verb's person ("and he said" as one Hebrew verb) or a
suffix on another noun ("his servants"), answer kind "implied" with no word. If a verse has several such
words, give the one that most plainly stands for the person: a name before a title, a title before a
pronoun.

The rules that decide most cases:

- **Read the text literally.** The lines shown must settle that the word refers to this person: a
  pronoun whose antecedent in the lines shown is this person, a title the passage fixes to this person
  (the chapter names him as the king of Babylon, or the man of God is the prophet the passage is about).
  A title the passage never ties to anybody, or a pronoun that could be one of two people, is "none".
- **Being in the story is not being referred to.** A verse about what happened next, or about the people
  around somebody, does not refer to them unless one of its own words does.
- **Somebody else is not this person.** If the word you would take for this person refers in fact to
  one of the `others` -- another king, another prophet, a namesake -- answer with that person's slug and
  that word. The LORD is not the angel of the LORD; a king of Egypt is not every king in the chapter.
- **Do not bring in knowledge from outside the lines shown**, and do not answer from what you know the
  person did elsewhere. Where the text does not settle it, "none" is the right answer, not a failure.

Give a confidence between 0 and 1: how sure you are that this word, in this verse, refers to this person.

Return a single JSON array and nothing else, one object per item, in the order given:

  { "item": "<id>",
    "record": "<the slug of the record or of one of the others>" | "none",
    "word": <the number of the original word> | null,
    "kind": "name" | "title" | "pronoun" | "implied" | null,
    "confidence": 0.0-1.0,
    "reason": "one line quoting the words of the passage that settle it, or saying what is missing" }
"""

CHECK = """\
You are a second reader. Somebody has read one word of each verse below as referring to a particular
person, and your job is to say whether the text bears that out -- and to refuse wherever it does not.

For each item you are given the person (a name and a line telling them apart from others of the name),
the other people this corpus finds named nearby, the verse in the original word by word -- BHSA Hebrew
or Nestle 1904 Greek as `number surface lexeme strong morphology "gloss" | KJV words` -- the number of the
word proposed, and the King James lines before and after the verse.

Answer with a JSON array, one object per item, in the order given, and nothing else:

  {"item": "<id>", "holds": true, "why": "..."}

  holds -- true only if the proposed word itself stands for this person -- as a name, the head noun of a
           title or description, or a pronoun -- and the lines shown make it this person and nobody
           else. False where the word is an article, a genitive, a verb or a word about something else;
           where the pronoun or title could as well be one of the other people, or the lines shown never
           tie it to this person; where it refers to a namesake; and where the reading rests on knowledge
           of the story from outside the lines shown.
  why   -- one short clause, quoting the words that decide it.

Judge the lines as written. Do not uphold a reading because you know it to be true.
"""


# ---------------------------------------------------------------------------------------------------
# What the corpus holds


def read_entities():
    return shared.psql(f"""
        SELECT json_object_agg(e.id, json_build_object(
            'slug', e.slug, 'name', e.name, 'kind', e.kind, 'sex', e.sex,
            'described', CASE WHEN e.source LIKE '{WITNESS}' THEN NULL ELSE e.distinguisher END,
            'numbers', (SELECT coalesce(json_agg(DISTINCT trim(x)), '[]')
                        FROM entity_name n,
                             unnest(string_to_array(coalesce(n.hebrew_strong_number, '') || ',' ||
                                                    coalesce(n.greek_strong_number, ''), ',')) x
                        WHERE n.entity_id = e.id AND n.kind NOT IN {NOT_A_NAME} AND trim(x) <> ''),
            'labels', (SELECT coalesce(json_agg(DISTINCT n.label), '[]') FROM entity_name n
                       WHERE n.entity_id = e.id AND n.kind NOT IN {NOT_A_NAME})))
        FROM entity e
    """)


def read_references():
    """Every verse reference, BibleData's and ours, as (entity, book, chapter, verse, whose)."""
    return shared.psql(f"""
        SELECT json_agg(json_build_array(entity_id, b, c, v, witness)) FROM (
            SELECT DISTINCT entity_id, canonical_book b, canonical_chapter c, canonical_verse v,
                   source LIKE '{WITNESS}' AS witness
            FROM entity_verse) x
    """)


def read_original_words(addresses):
    """
    The original words of these verses: position, surface, lemma, number, morphology, whether any
    annotation stands on the word, and the entity the settled annotation names.
    """
    rows = []
    wanted = sorted(set(addresses))
    for start in range(0, len(wanted), 2000):
        values = ', '.join(f'({b}, {c}, {v})' for b, c, v in wanted[start:start + 2000])
        rows += shared.psql(f"""
            WITH wanted(b, c, v) AS (VALUES {values}),
            words AS (
                SELECT w.b, w.c, w.v, t.slug, x.id, x.position, x.text, x.lemma, x.strong_number,
                       x.morphology
                FROM wanted w
                JOIN verse_reference r ON r.canonical_book = w.b AND r.canonical_chapter = w.c
                                      AND r.canonical_verse = w.v AND r.is_primary
                JOIN verse ve ON ve.id = r.verse_id
                JOIN text t ON t.id = ve.text_id AND t.slug IN ('{HEBREW}', '{GREEK}')
                JOIN word x ON x.verse_id = ve.id
            ),
            {SETTLED}
            SELECT coalesce(json_agg(json_build_array(
                       w.b, w.c, w.v, w.slug, w.position, w.text, w.lemma, w.strong_number,
                       w.morphology->>'pos', coalesce(w.morphology->>'form', ''),
                       w.morphology->>'suffixPerson' IS NOT NULL,
                       EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id),
                       s.entity_id) ORDER BY w.b, w.c, w.v, w.position), '[]')
            FROM words w LEFT JOIN settled s ON s.word_id = w.id
        """) or []
    verses = collections.defaultdict(list)
    for b, c, v, slug, position, text, lemma, strong, pos, form, suffixed, annotated, settled in rows:
        verses[(b, c, v)].append({'text_slug': slug, 'position': position, 'text': text, 'lemma': lemma,
                                  'strong': strong, 'pos': pos, 'form': form, 'suffixed': suffixed,
                                  'annotated': annotated, 'settled': settled})
    return verses


# The annotation a word stands named as, by the standing the corpus settles by (Annotating.Settled):
# the claim of highest standing and then confidence, and nothing where two of equal standing and
# confidence name two entities.
SETTLED = """
    standing AS (
        SELECT a.word_id, a.entity_id,
               CASE a.method WHEN 'manual' THEN 7 WHEN 'stated-by-source' THEN 6 WHEN 'strong-number' THEN 5
                             WHEN 'model-reading' THEN 4 WHEN 'rule-based' THEN 3 WHEN 'lexical' THEN 2
                             WHEN 'aligner' THEN 1 ELSE 0 END AS standing,
               coalesce(a.confidence, 1.0) AS confidence
        FROM word_entity a JOIN words w ON w.id = a.word_id
    ),
    ranked AS (
        SELECT s.*, row_number() OVER x AS place, count(*) OVER (PARTITION BY s.word_id) AS claims,
               lead(s.entity_id) OVER x AS next_entity, lead(s.standing) OVER x AS next_standing,
               lead(s.confidence) OVER x AS next_confidence
        FROM standing s
        WINDOW x AS (PARTITION BY s.word_id ORDER BY s.standing DESC, s.confidence DESC)
    ),
    settled AS (
        SELECT word_id, entity_id FROM ranked
        WHERE place = 1 AND (claims = 1 OR NOT (next_entity <> entity_id AND next_standing = standing
                                                AND next_confidence = confidence))
    )
"""


class Corpus:
    """The encyclopedia's people, its references and BibleData's, and the King James text."""

    def __init__(self, cache_dir):
        def read(name, produce):
            return shared.cache(cache_dir, name, produce)

        self.entities = {int(k): v for k, v in read('entities', read_entities).items()}
        self.by_slug = {e['slug']: i for i, e in self.entities.items()}
        self.witness = collections.defaultdict(set)
        self.ours = collections.defaultdict(set)
        self.named = collections.defaultdict(set)
        for entity, b, c, v, witness in read('references', read_references):
            (self.witness if witness else self.ours)[entity].add((b, c, v))
            if not witness:
                self.named[(b, c, v)].add(entity)
        self.lines = shared.keyed(read(shared.RENDERING, lambda: shared.rendering(shared.RENDERING)))
        self.chapters = collections.defaultdict(list)
        for b, c, v in self.lines:
            self.chapters[(b, c)].append(v)
        for verses in self.chapters.values():
            verses.sort()
        addresses = {a for refs in self.witness.values() for a in refs}
        self.originals = read('originals', lambda: [
            [list(a), words] for a, words in read_original_words(addresses).items()])
        self.originals = {tuple(a): words for a, words in self.originals}

    def spellings(self, entity):
        record = self.entities[entity]
        return {folded(n) for n in {record['name'], *record['labels']}
                if n and n[0].isupper() and ' ' not in n.strip() and folded(n)}

    def numbers(self, entity, address):
        prefix = 'H' if address[0] <= LAST_OLD_TESTAMENT_BOOK else 'G'
        return {n for n in self.entities[entity]['numbers'] if n.startswith(prefix)}

    def printed(self, entity, address):
        """Whether the verse prints the record's name: its number in the original, or a spelling in the KJV."""
        numbers = self.numbers(entity, address)
        if any(w['strong'] in numbers for w in self.originals.get(address, ())):
            return True
        spellings = self.spellings(entity)
        return any(folded(word) in spellings for word in re.findall(r"[A-Za-z'’-]+", self.lines.get(address, ''))
                   if word[0].isupper())

    def window(self, address):
        b, c, v = address
        verses = self.chapters.get((b, c), [])
        if len(verses) > CHAPTER_SHOWN_WHOLE:
            verses = [x for x in verses if v - WINDOW_BEFORE <= x <= v + WINDOW_AFTER]
        return [(b, c, x) for x in verses]

    def person(self, entity):
        record = self.entities[entity]
        return {'slug': record['slug'], 'name': record['name'], 'sex': record.get('sex'),
                'described': record.get('described')}


def folded(word):
    return re.sub(r'[^a-z]', '', re.sub(r"['’]s?$", '', word).lower())


def in_scope(corpus, entity, address):
    """
    A BibleData reference of a person, in a verse that does not print the name, of a record that is
    either a title the dataset holds as a person or has a number of the testament to be printed by.
    """
    record = corpus.entities[entity]
    if record['kind'] != PERSON or record['slug'] in DECIDED or address not in corpus.lines:
        return False
    if corpus.printed(entity, address):
        return False
    return bool(corpus.numbers(entity, address)) or ' ' in record['name'].strip()


def targets(corpus):
    """BibleData's references no reference of ours reaches, that a reading of the passage could settle."""
    return sorted((entity, address) for entity, refs in corpus.witness.items() for address in refs
                  if address not in corpus.ours[entity] and in_scope(corpus, entity, address))


def known(corpus):
    """
    References whose answer this corpus already has: BibleData and our words agree on the person, the
    verse does not print the name, and a settled annotation of ours names the person on a word of the
    original. The word is the answer; it is kept here and never shown.
    """
    found = []
    for entity, refs in corpus.witness.items():
        for address in refs & corpus.ours[entity]:
            if not in_scope(corpus, entity, address):
                continue
            words = [w for w in corpus.originals.get(address, ()) if w['settled'] == entity]
            if words:
                found.append((entity, address, [w['position'] for w in words]))
    return sorted(found)


def measure(args):
    corpus = Corpus(args.cache)
    wanted = targets(corpus)
    answered = known(corpus)
    heads = collections.Counter(corpus.entities[e]['slug'] for e, _ in wanted)
    print(f'{len(wanted)} references only BibleData states, of {len(heads)} people, in verses that do not '
          f'print the name.')
    print('  most cited: ' + ', '.join(f'{s} {n}' for s, n in heads.most_common(15)))
    known_heads = collections.Counter(corpus.entities[e]['slug'] for e, _, _ in answered)
    print(f'{len(answered)} references whose answer is known (ours and BibleData agree, on a word that is '
          f'not the printed name): ' + ', '.join(f'{s} {n}' for s, n in known_heads.most_common(12)))


# ---------------------------------------------------------------------------------------------------
# The payloads


def original_blocks(addresses):
    """The original of each verse word by word, with the King James words linked to each word."""
    shown = {}
    wanted = sorted(set(addresses))
    for start in range(0, len(wanted), shared.WITNESS_ADDRESSES_PER_QUERY):
        chunk = wanted[start:start + shared.WITNESS_ADDRESSES_PER_QUERY]
        rows = shared.psql(shared.witness_query(chunk))
        shown.update(assemble(chunk, rows))
    return shown


def assemble(addresses, rows):
    by_text = {}
    for word in rows['words']:
        by_text.setdefault((tuple(word['at']), word['text_slug']), []).append(word)
    best = {}
    for source, relation, method, target in rows['links']:
        standing = shared.LINK_STANDING.get(method, len(shared.LINK_STANDING))
        held = best.get(source)
        if held is None or standing < held[0]:
            best[source] = (standing, method, set())
        if best[source][0] == standing and relation == shared.RENDERS and target:
            best[source][2].add(target)
    shown = {}
    for address in addresses:
        old = address[0] <= LAST_OLD_TESTAMENT_BOOK
        slug = HEBREW if old else GREEK
        original = by_text.get((address, slug), [])
        if not original:
            continue
        under = {word['id']: [] for word in original}
        for word in by_text.get((address, shared.RENDERING), []):
            link = best.get(word['id'])
            if not link or not any(ch.isalnum() for ch in word['text']):
                continue
            for target in link[2]:
                if target in under:
                    under[target].append(word['text'] + (shared.GUESS_MARK if link[1] == shared.GUESSED else ''))
        lines, words = [], []
        for number, word in enumerate(original, start=1):
            if old:
                surface = shared.CANTILLATION.sub('', word['text'])
                lexeme = shared.CANTILLATION.sub('', (word['morphology'] or {}).get('vocalizedLexeme')
                                                 or word['lemma'] or '')
                morph = shared.hebrew_morphology(word['morphology'])
            else:
                surface, lexeme = word['text'], word['lemma'] or ''
                morph = (word['morphology'] or {}).get('form') or ''
            head = ' '.join(filter(None, (str(number), surface, lexeme, word['strong'], morph,
                                          f'"{word["gloss"]}"' if word['gloss'] else None)))
            lines.append(head + (f' | KJV {" ".join(under[word["id"]])}' if under[word['id']] else ''))
            words.append({'number': number, 'position': word['position'], 'surface': surface,
                          'strong': word['strong'], 'kjv': ' '.join(under[word['id']]) or None})
        shown[address] = {'original': slug, 'lines': lines, 'words': words}
    return shown


def decoy_candidates(corpus, entity, addresses):
    """Verses near the asked ones that neither witness lists for this person."""
    listed = corpus.witness[entity] | corpus.ours[entity]
    return sorted({(b, c, x) for b, c, v in addresses for x in corpus.chapters.get((b, c), [])
                   if 0 < abs(x - v) <= DECOY_REACH} - set(addresses) - listed)


def extract(args):
    corpus = Corpus(args.cache)
    shuffle = random.Random(args.seed)
    if args.set == 'heldout':
        asked = [(entity, address, positions) for entity, address, positions in known(corpus)]
    else:
        asked = [(entity, address, None) for entity, address in targets(corpus)]

    # One question per person and chapter, its verses the items.
    questions = collections.defaultdict(list)
    for entity, address, answer in asked:
        questions[(entity, address[0], address[1])].append((address, answer))

    nearby = {}
    if args.set == 'heldout':
        nearby = {key: decoy_candidates(corpus, key[0], [a for a, _ in verses]) for key, verses in questions.items()}
        unread = {a for near in nearby.values() for a in near} - set(corpus.originals)
        corpus.originals.update(read_original_words(unread))

    items, plan = [], []
    for (entity, b, c), verses in sorted(questions.items(), key=lambda q: (q[0][1], q[0][2], q[0][0])):
        extra = [a for a in nearby.get((entity, b, c), ()) if not corpus.printed(entity, a)]
        shuffle.shuffle(extra)
        extra = extra[:DECOYS_PER_QUESTION]
        entries = [(a, answer, False) for a, answer in verses] + [(a, [], True) for a in extra]
        shown = sorted({x for a, _, _ in entries for x in corpus.window(a)})
        others = collections.Counter(e for a in shown for e in corpus.named.get(a, ())
                                     if e != entity and corpus.entities[e]['kind'] == PERSON)
        question = {'entity': entity, 'shown': shown,
                    'others': sorted(e for e, _ in others.most_common(OTHERS_SHOWN)), 'items': []}
        for address, answer, decoy in sorted(entries):
            item = {'item': f'i{len(items):04d}', 'entity': corpus.entities[entity]['slug'],
                    'reference': shared.reference(*address), 'answer': answer, 'decoy': decoy}
            items.append(item)
            question['items'].append(item)
        plan.append(question)

    blocks = original_blocks({shared.parse_reference(i['reference']) for i in items})
    batches, batch = [], None
    for question in plan:
        verses = set(question['shown'])
        originals = {shared.parse_reference(i['reference']) for i in question['items']}
        if batch is None or (len(batch['questions']) >= QUESTIONS_PER_BATCH
                             or batch['items'] + len(question['items']) > ITEMS_PER_BATCH
                             or len(batch['verses'] | verses) > BATCH_VERSES
                             or len(batch['originals'] | originals) > BATCH_ORIGINALS):
            batch = {'questions': [], 'items': 0, 'verses': set(), 'originals': set()}
            batches.append(batch)
        batch['questions'].append(question)
        batch['items'] += len(question['items'])
        batch['verses'] |= verses
        batch['originals'] |= originals

    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    manifest = {'prompt_version': PROMPT_VERSION, 'set': args.set,
                'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
                'selection': {'seed': args.seed, 'items': len(items), 'questions': len(plan),
                              'decoys': sum(i['decoy'] for i in items)},
                'batches': []}
    for number, batch in enumerate(batches):
        name = f'batch-{number:04d}'
        payload = {'batch': name, 'prompt_version': PROMPT_VERSION, 'questions': [], 'passages': [],
                   'originals': []}
        for q, question in enumerate(batch['questions']):
            payload['questions'].append({
                'question': f'{name}-q{q:02d}',
                'record': corpus.person(question['entity']),
                'others': [corpus.person(e) for e in question['others']],
                'items': [{'item': i['item'], 'verse': i['reference']} for i in question['items']]})
        payload['passages'] = [{'reference': shared.reference(*a), 'text': corpus.lines[a]}
                               for a in sorted(batch['verses']) if a in corpus.lines]
        payload['originals'] = [{'reference': shared.reference(*a), 'original': blocks[a]['original'],
                                 'words': blocks[a]['lines']}
                                for a in sorted(batch['originals']) if a in blocks]
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)
        manifest['batches'].append({'batch': name, 'items': [i['item'] for q in batch['questions']
                                                             for i in q['items']],
                                    'verses': len(payload['passages']), 'originals': len(payload['originals'])})
    for item in items:
        block = blocks.get(shared.parse_reference(item['reference']))
        item['original'] = block['original'] if block else None
        item['words'] = block['words'] if block else []
        item['lines'] = block['lines'] if block else []
        item['others'] = next(q['others'] for q in plan if item in q['items'])
        item['others'] = [corpus.entities[e]['slug'] for e in item['others']]
    with open(os.path.join(args.dir, 'items.json'), 'w', encoding='utf-8') as handle:
        json.dump(items, handle, ensure_ascii=False, indent=1)
    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)
    print(f'{len(items)} items ({manifest["selection"]["decoys"]} decoys) in {len(plan)} questions, '
          f'{len(batches)} batches, {sum(b["verses"] for b in manifest["batches"])} passage lines, '
          f'{sum(b["originals"] for b in manifest["batches"])} verses in the original -> {args.dir}')


# ---------------------------------------------------------------------------------------------------
# The readings


def load_items(directory):
    with open(os.path.join(directory, 'items.json'), encoding='utf-8') as handle:
        return {item['item']: item for item in json.load(handle)}


def word_class(word):
    """What kind of word an answer rests on, read off the word's morphology rather than the model's say-so."""
    if word is None:
        return None
    pos, form = word.get('pos') or '', word.get('form') or ''
    if pos == 'nmpr' or (form.startswith('N-') and (word.get('lemma') or ' ')[0].isupper()):
        return NAME
    if pos in ('prps', 'prde', 'prin') or form[:2] in ('P-', 'D-', 'R-', 'F-', 'S-', 'Q-', 'K-', 'C-', 'X-', 'I-'):
        return PRONOUN
    if pos in ('prep', 'nega', 'intj', 'advb', 'conj') and word.get('suffixed'):
        return PRONOUN
    if pos in ('subs', 'adjv') or form.startswith(('N-', 'A-')):
        return TITLE
    return IMPLIED


def reading_row(item, answers, corpus_words):
    answer = next((a for a in answers if isinstance(a, dict) and a.get('item') == item['item']), None)
    row = {'item': item['item'], 'entity': item['entity'], 'reference': item['reference']}
    if not answer:
        return dict(row, record=None, word=None, position=None, kind=None, klass=None, confidence=None,
                    reason=None)
    record = answer.get('record') if isinstance(answer.get('record'), str) else None
    if record not in {item['entity'], NONE, *item['others']}:
        record = None
    kind = answer.get('kind') if answer.get('kind') in KINDS else None
    number = answer.get('word') if isinstance(answer.get('word'), int) and kind != IMPLIED else None
    word = next((w for w in item['words'] if w['number'] == number), None)
    detail = corpus_words.get((item['reference'], word['position'])) if word else None
    try:
        confidence = round(min(1.0, max(0.0, float(answer.get('confidence')))), 3)
    except (TypeError, ValueError):
        confidence = None
    return dict(row, record=record, word=number if word else None,
                position=word['position'] if word else None,
                surface=word['surface'] if word else None, strong=word['strong'] if word else None,
                kjv=word['kjv'] if word else None,
                kind=kind, klass=word_class(detail) if detail else (IMPLIED if kind == IMPLIED else None),
                annotated=detail['annotated'] if detail else None,
                confidence=confidence, reason=answer.get('reason'))


def item_words(directory, cache):
    """The morphology of every original word an item shows, keyed by (reference, position)."""
    corpus = Corpus(cache)
    out = {}
    for item in load_items(directory).values():
        address = shared.parse_reference(item['reference'])
        for w in corpus.originals.get(address, ()):
            out[(item['reference'], w['position'])] = w
    missing = {shared.parse_reference(i['reference']) for i in load_items(directory).values()} - set(corpus.originals)
    if missing:
        for address, words in read_original_words(missing).items():
            for w in words:
                out[(shared.reference(*address), w['position'])] = w
    return out


def ask(args):
    items = load_items(args.dir)
    words = item_words(args.dir, args.cache)
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    jobs = []
    for entry in manifest['batches']:
        with open(os.path.join(args.dir, 'batches', entry['batch'] + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        jobs.append((entry['batch'], json.dumps(payload, ensure_ascii=False, indent=1),
                     [items[i] for i in entry['items']]))
    harness.run_batches(args.dir, 'out', jobs, SYSTEM, PROMPT_VERSION, args,
                        lambda item, answers: reading_row(item, answers, words))


def readings(directory, folder='out'):
    return {row.get('item') or row.get('fact'): row for row in harness.collect(directory, folder)}


def positive(row):
    """An answer naming the asked record on a word that can name somebody: not a verb, not an article."""
    return row['record'] == row['entity'] and row['position'] is not None and row['klass'] in CLASSES


def check(args):
    """
    The second reading, over every answer that names the asked record on a word. It is handed the one
    verse, the one word and the one claim, and asked to refuse.
    """
    corpus = Corpus(args.cache)
    items = load_items(args.dir)
    wanted = [row for row in readings(args.dir).values() if positive(row)]
    print(f'{len(wanted)} answers name the asked record on a word and are read a second time.')
    lines = []
    for row in wanted:
        item = items[row['item']]
        b, c, v = shared.parse_reference(item['reference'])
        around = [{'reference': shared.reference(b, c, x), 'text': corpus.lines[(b, c, x)]}
                  for x in range(v - CHECK_BEFORE, v + CHECK_AFTER + 1) if (b, c, x) in corpus.lines]
        lines.append({
            'item': row['item'],
            'person': corpus.person(corpus.by_slug[item['entity']]),
            'others': [corpus.person(corpus.by_slug[s]) for s in item['others']],
            'verse': item['reference'],
            'original': item['original'],
            'words': item['lines'],
            'proposed_word': row['word'],
            'lines': around,
        })
    jobs = []
    for number, start in enumerate(range(0, len(lines), CHECK_ITEMS_PER_BATCH)):
        group = lines[start:start + CHECK_ITEMS_PER_BATCH]
        jobs.append((f'batch-{number:04d}', json.dumps(group, ensure_ascii=False, indent=1), group))

    def shape(item, answers):
        answer = next((a for a in answers if isinstance(a, dict) and a.get('item') == item['item']), None)
        holds = answer.get('holds') if answer else None
        return {'item': item['item'], 'holds': holds if isinstance(holds, bool) else None,
                'why': answer.get('why') if answer else None}

    harness.run_batches(args.dir, 'check', jobs, CHECK, CHECK_VERSION, args, shape)


def spent(directory):
    total = {}
    for folder in ('out', 'check'):
        path = os.path.join(directory, folder, 'runs.jsonl')
        if os.path.exists(path):
            with open(path, encoding='utf-8') as handle:
                total[folder] = round(sum(json.loads(line)['cost'] for line in handle if line.strip()), 4)
    return total


# ---------------------------------------------------------------------------------------------------
# Held out: precision per class


def agreed(row, second):
    return positive(row) and second is not None and second.get('holds') is True


def score(args):
    items = load_items(args.dir)
    first = readings(args.dir)
    second = readings(args.dir, 'check')
    out = [f'# Held out: {PROMPT_VERSION}, second reading {CHECK_VERSION}\n',
           f'{sum(not i["decoy"] for i in items.values())} references whose answer is known, '
           f'{sum(i["decoy"] for i in items.values())} decoys (verses of the same passages that neither '
           f'BibleData nor this corpus lists for the person). Cost: {spent(args.dir)}\n']

    adjudicated = {}
    path = os.path.join(args.dir, ADJUDICATED)
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            adjudicated = json.load(handle)

    def tally(accept):
        by = {k: collections.Counter() for k in (*CLASSES, None)}
        for item in items.values():
            row = first.get(item['item'])
            if not row:
                continue
            claimed = accept(row, second.get(item['item']))
            klass = row['klass'] if row['klass'] in CLASSES else None
            if item['decoy']:
                if not claimed:
                    by[klass]['decoy none'] += 1
                elif (adjudicated.get(item['item']) or {}).get('right'):
                    by[klass]['decoy read right'] += 1
                else:
                    by[klass]['decoy wrong'] += 1
                continue
            if claimed:
                by[klass]['right'] += 1
                by[klass]['same word' if row['position'] in item['answer'] else 'other word'] += 1
            elif row['record'] not in (None, NONE, item['entity']) and row['position'] is not None:
                by[klass]['another record'] += 1
            else:
                by[klass]['none'] += 1
        return by

    out.append('`precision` is of what would be written: answers naming the asked record on a word, right where '
               'the answer is known, or where a decoy was named and reading the passage by hand shows the verse '
               f'does name the person there (BibleData\'s list lacks it; {ADJUDICATED}). An answer naming another '
               'record on a known verse is a miss, not a false claim, and is counted apart.\n')
    for title, accept in (('first reading alone', lambda row, _: positive(row)),
                          ('both readings agree', agreed)):
        by = tally(accept)
        out.append(f'\n## {title}\n')
        out.append('| class | right (known) | decoys read right | decoys wrong | precision | answered | same word | '
                   'other word | another record (known) | none (known) | decoys none |')
        out.append('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
        verdicts = []
        for klass in (*CLASSES, None):
            c = by[klass]
            answered = c['right'] + c['decoy read right'] + c['decoy wrong']
            precision = (c['right'] + c['decoy read right']) / answered if answered else 0
            out.append(f'| {klass or "no word"} | {c["right"]} | {c["decoy read right"]} | {c["decoy wrong"]} | '
                       f'{precision:.2%} | {answered} | {c["same word"]} | {c["other word"]} | '
                       f'{c["another record"]} | {c["none"]} | {c["decoy none"]} |')
            if klass and answered:
                verdicts.append(f'{klass}: ' + (
                    f'passes, {precision:.2%} on {answered}' if precision >= ACCEPTED_PRECISION
                    and answered >= ACCEPTED_ANSWERED else
                    f'a small class, every one of {answered} right' if precision == 1 else
                    f'fails, {precision:.2%} on {answered}'))
        out.append('\nAgainst the bar (>= 99% on >= 300 answered, or a small class entirely right): '
                   + '; '.join(verdicts) + '.')

    out.append('\n## Every answer counted wrong, and the decoys answered\n')
    for item in items.values():
        row = first.get(item['item'])
        if not row:
            continue
        wrong = (item['decoy'] and positive(row)) or (
            not item['decoy'] and row['record'] not in (None, NONE, item['entity']) and row['position'] is not None)
        if wrong:
            check_row = second.get(item['item']) or {}
            out.append(f'- {item["item"]} {"decoy " if item["decoy"] else ""}{item["entity"]} {item["reference"]}: '
                       f'{row["record"]} word {row["word"]} {row["surface"]} (KJV {row["kjv"]}) {row["klass"]}, '
                       f'{row["confidence"]}: {row["reason"]} | second: {check_row.get("holds")} {check_row.get("why")}')
    out.append('\n## Known answers read on another word\n')
    for item in items.values():
        row = first.get(item['item'])
        if row and not item['decoy'] and positive(row) and row['position'] not in item['answer']:
            out.append(f'- {item["item"]} {item["entity"]} {item["reference"]}: word {row["word"]} {row["surface"]} '
                       f'(KJV {row["kjv"]}) {row["klass"]}; known {item["answer"]}: {row["reason"]}')
    report = '\n'.join(out) + '\n'
    with open(os.path.join(args.dir, 'report.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    print(report[:6000])


# ---------------------------------------------------------------------------------------------------
# What is kept


def publish(args):
    """
    The read references, published: every answer that names the asked record on a word, with both
    readings. `write` is true where both readings agree, the word's class was accepted on the held-out
    set, and no annotation stood on the word when it was read; the loader checks the last again.
    Disagreements go to the owner's review list.
    """
    items = load_items(args.dir)
    first = readings(args.dir)
    second = readings(args.dir, 'check')
    runs = {}
    for folder in ('out', 'check'):
        path = os.path.join(args.dir, folder, 'runs.jsonl')
        with open(path, encoding='utf-8') as handle:
            runs[folder] = [json.loads(line) for line in handle if line.strip()]
    kept, review = [], []
    counts = collections.Counter()
    for item in items.values():
        row = first.get(item['item'])
        counts['asked'] += 1
        if not row or row['record'] is None:
            counts['no answer'] += 1
            continue
        if row['record'] == NONE:
            counts['none'] += 1
            continue
        if row['record'] != item['entity']:
            counts['another record'] += 1
            continue
        if not positive(row):
            counts['implied, or a word that names nobody'] += 1
            continue
        counts['answered'] += 1
        check_row = second.get(item['item']) or {}
        both = check_row.get('holds') is True
        accepted = row['klass'] in args.accept
        sure = (row['confidence'] or 0) >= LEAST_CONFIDENCE
        write = both and accepted and sure and not row['annotated']
        counts['agreed' if both else 'refused by the second reading'] += 1
        if both and not accepted:
            counts[f'agreed, class {row["klass"]} not accepted'] += 1
        if both and accepted and not sure:
            counts[f'agreed, confidence under {LEAST_CONFIDENCE}'] += 1
        if both and accepted and sure and row['annotated']:
            counts['agreed, the word already names somebody'] += 1
        counts['to write' if write else 'not written'] += 1
        line = {
            'reference': item['reference'], 'record': item['entity'], 'text': item['original'],
            'position': row['position'], 'surface': row['surface'], 'strong': row['strong'], 'kjv': row['kjv'],
            'kind': row['kind'], 'class': row['klass'], 'confidence': row['confidence'], 'reason': row['reason'],
            'reading': {'model': row['model'], 'effort': row['effort'], 'promptVersion': row['promptVersion'],
                        'askedAt': row['askedAt']},
            'check': {'holds': check_row.get('holds'), 'why': check_row.get('why'),
                      'model': check_row.get('model'), 'promptVersion': check_row.get('promptVersion'),
                      'askedAt': check_row.get('askedAt')},
            'write': write,
        }
        kept.append(line)
        if not both or not sure:
            review.append({'reference': item['reference'], 'record': item['entity'], 'word': row['surface'],
                           'kjv': row['kjv'], 'class': row['klass'], 'reading': row['reason'],
                           'confidence': row['confidence'], 'second': check_row.get('why'),
                           'secondHolds': check_row.get('holds')})
    os.makedirs(args.to, exist_ok=True)
    for name in os.listdir(args.to):
        if re.fullmatch(r'reading-\d+\.jsonl', name):
            os.remove(os.path.join(args.to, name))
    kept.sort(key=lambda line: (shared.parse_reference(line['reference']), line['position']))
    for number, start in enumerate(range(0, len(kept), RECORDS_PER_FILE)):
        with open(os.path.join(args.to, f'reading-{number:04d}.jsonl'), 'w', encoding='utf-8') as handle:
            for line in kept[start:start + RECORDS_PER_FILE]:
                handle.write(json.dumps(line, ensure_ascii=False) + '\n')
    with open(os.path.join(args.to, 'runs.json'), 'w', encoding='utf-8') as handle:
        json.dump({'accepted': sorted(args.accept), 'counts': dict(counts), 'runs': runs,
                   'heldout': spent(args.heldout) if args.heldout else None}, handle, ensure_ascii=False, indent=1)
    with open(args.review, 'w', encoding='utf-8') as handle:
        json.dump(sorted(review, key=lambda r: shared.parse_reference(r['reference'])), handle,
                  ensure_ascii=False, indent=1)
    if args.heldout:
        publish_heldout(args.heldout, args.to)
    for what, n in counts.most_common():
        print(f'  {n:5} {what}')
    print(f'{len(kept)} answers -> {args.to}; {len(review)} disagreements -> {args.review}')


def publish_heldout(directory, to):
    """The held-out run beside the answers it vouched for: every item, its known answer, both readings."""
    items = load_items(directory)
    first = readings(directory)
    second = readings(directory, 'check')
    adjudicated = {}
    if os.path.exists(os.path.join(directory, ADJUDICATED)):
        with open(os.path.join(directory, ADJUDICATED), encoding='utf-8') as handle:
            adjudicated = json.load(handle)
    with open(os.path.join(to, 'heldout.jsonl'), 'w', encoding='utf-8') as handle:
        for item in items.values():
            row = first.get(item['item']) or {}
            handle.write(json.dumps({
                'item': item['item'], 'reference': item['reference'], 'record': item['entity'],
                'decoy': item['decoy'], 'known': item['answer'],
                'answer': {key: row.get(key) for key in ('record', 'position', 'surface', 'kjv', 'kind', 'klass',
                                                         'confidence', 'reason', 'model', 'askedAt')},
                'check': second.get(item['item']),
                'adjudicated': adjudicated.get(item['item'])}, ensure_ascii=False) + '\n')
    with open(os.path.join(directory, 'report.md'), encoding='utf-8') as handle:
        report = handle.read()
    with open(os.path.join(to, 'heldout.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    parser.add_argument('--cache', default='.references/cache', help='where the database reads are kept between runs')
    commands = parser.add_subparsers(dest='command', required=True)

    commands.add_parser('measure', help='count the references in scope and the ones whose answer is known'
                        ).set_defaults(run=measure)

    extractor = commands.add_parser('extract', help='write the prompt payloads for a run')
    extractor.add_argument('--set', choices=('heldout', 'read'), required=True)
    extractor.add_argument('--out', dest='dir', required=True)
    extractor.add_argument('--seed', type=int, default=7)
    extractor.set_defaults(run=extract)

    for name, what, run in (('ask', 'the reading', ask), ('check', 'the second reading', check)):
        sub = commands.add_parser(name, help=f'run {what} over every batch that has no answers yet')
        sub.add_argument('--dir', required=True)
        sub.add_argument('--model', default='sonnet')
        sub.add_argument('--workers', type=int, default=3)
        sub.add_argument('--effort', default='medium')
        sub.add_argument('--budget', type=float, help='stop starting batches once this many dollars are spent')
        sub.add_argument('--again', action='store_true')
        sub.set_defaults(run=run)

    scorer = commands.add_parser('score', help='held out: precision per class, first reading and both')
    scorer.add_argument('--dir', required=True)
    scorer.set_defaults(run=score)

    publisher = commands.add_parser('publish', help='write the answers kept for the corpus')
    publisher.add_argument('--dir', required=True)
    publisher.add_argument('--heldout')
    publisher.add_argument('--accept', nargs='+', choices=CLASSES, default=[])
    publisher.add_argument('--to', default=PUBLISHED)
    publisher.add_argument('--review', default=REVIEW)
    publisher.set_defaults(run=publish)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
