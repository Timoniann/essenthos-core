"""
Which of the relationships only BibleData states does the text itself state? Ask, verse in hand.

2,225 of BibleData's relationship rows reach no row this corpus read for itself. They are the
dataset's claims, and a claim is not evidence: this harness turns each one into a question for a
model reading the text -- the verse the dataset cites, the chapter around it, and every verse where
both people are named -- and keeps what the text states as a clause of this corpus's own, with the
verse it was read from and the witness it rests on.

    python scripts/relationships.py measure
    python scripts/relationships.py extract --out .relationships/pilot --sample 120 --seed 7
    python scripts/relationships.py ask     --dir .relationships/pilot --workers 4 --effort medium
    python scripts/relationships.py check   --dir .relationships/pilot --workers 4 --effort medium
    python scripts/relationships.py score   --dir .relationships/pilot
    python scripts/relationships.py publish --dir .relationships/pilot .relationships/full

Seven things about the design are load-bearing:

**The unit is the fact, not the row.** BibleData writes nearly every relationship from both ends --
*Lot son Haran* and *Haran father Lot* -- so asking row by row pays twice for one question and can
answer it two ways. Rows are folded into facts the way `RelationshipVocabulary.Reversed` reads them,
and a fact counts as read by this corpus when any row of ours states it from either end.

**The dataset's grading and notes are never shown.** *explicit* and *inferred* are the dataset's
opinion of its own claim; shown to the model they are a hint about the answer. They survive only as
the strata a run is sampled and reported by.

**The relation word is the loader's.** Which of BibleData's types a clause can answer is decided by
`RelationshipVocabulary.Says`, read out of the C# here rather than restated, so this harness and the
page it feeds cannot disagree about what counts as already read.

**The verse is read in its witnesses, not in the King James alone.** Every cited verse, and the first
verses naming both people, carry the original word by word -- BHSA with its article, number, gender
and construct state for the Old Testament, Nestle 1904 for the New with the Textus Receptus where it
reads otherwise -- with the King James, Berean and Synodal words that render each original word laid
under it over the corpus's links, the Berean and Synodal whole, Swete's and Brenton's Septuagint for
the Old Testament, and the footnotes that say where the witnesses part. The chapter around a verse
stays in the King James: it is read for the heading a register line belongs to, not weighed. The
owner reads a verse this way, and three rounds of decisions turned on what the King James flattened.

**A verdict names what it rests on.** `reference` stays the verse; `witness` says which text's words
decide it and `original` which Hebrew or Greek word, and *the witnesses differ* is an answer of its
own. A fact only some witnesses state is not written as a clause: it is counted and left for the
owner, because a reader sent to the verse would find it stated in one text and not in another.

**A chain of fathers is not a statement.** The prompt says so, and a positive is read a second time
by a prompt asking the opposite question, shown the same witnesses, before anything is published; on
the place register that second reading refused 41% of the decisive positives.

**A clause goes on the side whose verse it is.** The loader accepts a reference only among the verses
its subject is named in, so a fact stated in a verse that names only the father is written on the
father's record, as the inverse relation. A verse naming neither is counted and not written.

A run leaves:

    facts.json                 every fact only BibleData states, with its stratum
    manifest.json              the selection, its seed, and which facts went into which batch
    batches/batch-NNNN.json    the prompt payload, exactly as the model saw it
    out/batch-NNNN.jsonl       one verdict per fact, with the tokens and cost its batch spent
    check/batch-NNNN.jsonl     the second reading of each positive
    report.md, sample-stated.md, sample-not-stated.md
"""

import argparse
import concurrent.futures
import datetime
import functools
import json
import os
import random
import re
import subprocess
import threading
import time

import descriptors as shared

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VOCABULARY = os.path.join(ROOT, 'Essenthos.Corpus', 'Corpus', 'RelationshipVocabulary.cs')
RELATION_NAMES = os.path.join(ROOT, 'Essenthos.Corpus', 'Database', 'Entities', 'EntityDescriptor.cs')

PROMPT_VERSION = 'relationships-2'
CHECK_VERSION = 'relationships-check-2'

WITNESS_SOURCE = 'BibleData'
OUR_SOURCE = 'read from Scripture by'
STATED_BY_SOURCE = 'stated-by-source'

STATED, MOVED, NOT_STATED, DIFFER = 'stated', 'moved', 'not-stated', 'witnesses-differ'
VERDICTS = (STATED, MOVED, NOT_STATED, DIFFER)
POSITIVE = (STATED, MOVED)

EXPLICIT, INFERRED, IMPLICIT = 'explicit', 'inferred', 'implicit'
DESCENT = 'descent'
UNMAPPED = 'unmapped'
SUBSETS = (EXPLICIT, INFERRED, DESCENT, UNMAPPED)
DESCENT_RELATIONS = {'ancestor-of', 'descendant-of'}

# The end a fact is asked from, and written on where its verse allows: the one the relation defines
# by the other. *Lot, son of Haran* is how a genealogy reads and how Lot's page opens.
DEFINED_BY_THE_OTHER = {
    'son-of', 'daughter-of', 'descendant-of', 'grandson-of', 'granddaughter-of', 'nephew-of',
    'niece-of', 'son-in-law-of', 'daughter-in-law-of', 'servant-of', 'killed-by', 'disciple-of',
    'apostle-of', 'concubine-of', 'wife-of',
}

FEMININE = {
    'daughter-of', 'mother-of', 'sister-of', 'wife-of', 'concubine-of', 'half-sister-of',
    'grandmother-of', 'granddaughter-of', 'aunt-of', 'niece-of', 'mother-in-law-of',
    'daughter-in-law-of', 'sister-in-law-of', 'queen-of',
}
FEMALE = 'female'

# Which relations answer one question, as `RelationshipVocabulary.Branch` groups them: a man is
# somebody's son or his descendant, not both on one line. A record that already relates two people
# in one of these words does not gain a second word for the same tie.
KINSHIP = {
    'son-of', 'daughter-of', 'father-of', 'mother-of', 'brother-of', 'sister-of',
    'half-brother-of', 'half-sister-of', 'grandfather-of', 'grandmother-of', 'grandson-of',
    'granddaughter-of', 'uncle-of', 'aunt-of', 'nephew-of', 'niece-of', 'ancestor-of',
    'descendant-of', 'father-in-law-of', 'mother-in-law-of', 'son-in-law-of',
    'daughter-in-law-of', 'brother-in-law-of', 'sister-in-law-of',
}
MARRIAGE = {'husband-of', 'wife-of', 'concubine-of'}
VIOLENCE = {'killer-of', 'killed-by'}


def branch(relation):
    for name, members in (('kinship', KINSHIP), ('marriage', MARRIAGE), ('violence', VIOLENCE)):
        if relation in members:
            return name
    return relation

# What the model is shown. The chapter is shown whole up to a length where it stops being a chapter
# and becomes a book; past that the window leans backwards, because the heading a register line
# belongs to -- *the sons of Shashak* -- stands above it.
CHAPTER_SHOWN_WHOLE = 40
WINDOW_BEFORE = 25
WINDOW_AFTER = 10
BOTH_NAMED_SHOWN = 8
BOTH_NAMED_FROM_THE_HEAD = 5

# Which verses carry their witnesses: every cited verse, and the first few naming both people, which is
# where a statement moved from the cited verse is found. A witnessed verse costs ten lines of King
# James, so a batch is bounded by them as well as by lines.
BOTH_NAMED_WITNESSED = 3

PAIRS_PER_BATCH = 20
BATCH_VERSES = 200
BATCH_WITNESSED_VERSES = 30

CHECK_ITEMS_PER_BATCH = 20

# A register line is read by the run of lines above it. One neighbour each side was measured too
# little: the second reading refused *Eliada son of David* because *born unto him* is two verses up,
# and Bartholomew as Jesus's disciple because *his twelve disciples* is.
CHECK_BEFORE = 8
CHECK_AFTER = 1
CHECK_HEADINGS = 2
CHECK_HEADING_REACH = 40

RECORDS_PER_FILE = 40
SAMPLE_READ_BY_EYE = 20

SYSTEM = """\
You are checking whether the Bible text itself states a relationship between two people.

For each pair you are given a claim of the form "A son-of B", read as "A is the son of B"; the verse or
verses the claim was taken from, under `cited`; and the verses where both people are named, under
`both_named_in`. The King James text of those verses, and of the chapter around each cited verse, is
under `passages`. Each passage lists in `named_here` which of the people in this batch this corpus has
already identified in that verse. Use it to tell namesakes apart -- a verse about another man of the
same name is not about this one -- but not as proof of absence, because the identification is not
complete.

## The witnesses

The King James is one translation, and it flattens what the original distinguishes. Every cited verse,
and the first verses naming both people, are also under `witnesses`, read the way a scholar reads them:

- `words`: the original, one line per word -- BHSA Hebrew for the Old Testament, Nestle 1904 Greek for
  the New -- as `number surface lexeme strong morphology "gloss"`, followed by the words of each
  rendering linked to it: `| KJV ... | BSB ... | RUSV ... | GRCBRENT ...`. A rendering word marked
  `~` was linked by a statistical aligner and may be wrong. The Hebrew article, the conjunction and the
  prepositions are words of their own, so you can see whether "the man" is `article + noun` or a bare
  noun, whether "sons of" is a construct plural, and whether "his son" carries a suffix.
- `BSB` and `RUSV`: the Berean Standard Bible and the Russian Synodal, whole.
- `SWETE` (and `GRCBRENT` where it reads otherwise): the Septuagint, a Greek witness to a Hebrew
  text older than the one BHSA prints. `TR1894`: the Textus Receptus the King James translated,
  shown only where it reads otherwise than Nestle.
- `no_original_word`: words a rendering prints that no original word is linked to -- often a supplied
  word, sometimes an addition from another witness.
- `footnotes`: what the WEB and ASV note about the verse, which is often exactly where the witnesses part.

Read the original first. Where the English says something the Hebrew or Greek does not, or the witnesses
disagree about the names or the relation, that matters more than the English.

The claim came from a dataset that sometimes reads a relationship and sometimes deduces one. Your job
is to read the text, not to trust the claim.

Answer for each pair with one of:

  "stated"            a cited verse states this relation between these two in the original, and the
                      witnesses agree -- read together with the lines immediately around it where a
                      list continues a heading above it
  "moved"             the text states it, but in a verse other than the one cited; give that verse,
                      which must be one of the verses in `passages`
  "witnesses-differ"  some witnesses state it and others do not -- a name one text carries and another
                      lacks, a relation the Septuagint reads and the Hebrew does not, a Textus Receptus
                      reading. This is a legitimate and useful answer, not a failure to decide: say in
                      `how` exactly which witness reads what. Where no witness states it, however the
                      witnesses word the verse, the answer is "not-stated"
  "not-stated"        nothing you were shown states it in any witness, even if it may well be true

The rules that decide most cases:

- **A chain is not a statement.** If A is the father of B and B the father of C, that is not a statement
  that A is C's grandfather or ancestor unless a passage says so or runs the line in one list. Saying
  "not-stated" for a true fact is correct here; saying "stated" for a deduction is the error this pass
  exists to catch.
- **The relation must be the one claimed, and in the direction claimed.** A verse making B the son of A
  does not state that A is the son of B. A register's "of the sons of X" grouping men under the head of
  a family states descent, not parentage; "his son", "A begat B" and "B the son of A" state parentage.
  Where the English punctuation and the Hebrew construct chain read differently, the Hebrew decides.
- **Standing beside somebody in a list is not a relationship.** Names printed one after another are
  not thereby brothers, companions, husband and wife, or father and son.
- **Descent is stated** where a genealogy runs the line between them in one list, or where a passage
  calls one the father, forefather or forebear of the other or of the people the other belongs to.
- **Serving, discipleship, killing and marriage are stated** where a verse says it of these two: "his
  servant", "his disciples", "slew him", "took her to wife". Taking part in the same event is not.
- **Do not bring in a witness you were not shown.** The Vulgate, a manuscript or a commentary you
  remember is not evidence here; a footnote you were shown is.

Give a confidence between 0 and 1: how sure you are that the verse you give states this claim about
these two people, not how likely the claim is to be true.

Return a single JSON array and nothing else, one object per pair, in the order given:

  { "pair": "<id>", "verdict": "stated" | "moved" | "witnesses-differ" | "not-stated",
    "reference": "BOOK C:V" | null,
    "witness": "BHSA" | "NESTLE1904" | "TR1894" | "KJV" | "BSB" | "RUSV" | "SWETE" | "GRCBRENT" | null,
    "original": "the Hebrew or Greek word or words the reading turns on" | null,
    "confidence": 0.0-1.0,
    "reason": "one sentence quoting the words that state it, or saying what is missing",
    "how": "for witnesses-differ only: which witness reads what" | null }

`witness` is the text whose words decide the verdict: the original, wherever the verse was shown with
its witnesses and the original settles it; the King James only for a verse shown without them. For
"witnesses-differ" it is the witness that states the relation.
"""

CHECK = """\
You are a second reader. Somebody has read each verse below as stating a relationship between two
people, and your job is to say whether the words bear that out -- and to refuse wherever they do not.

For each item you are given the claim, the verse proposed as stating it, the witness the first reader
said it rests on, the lines before and after it, and the heading of the list it stands in where one was
found. Every line carries `named_here`: which of the two people this corpus has identified in it,
whatever spelling the line uses. A line naming one of them under another spelling -- Shimhi for Shema,
Heli for Eli, Meshelemiah for Shallum -- names that person, and a pronoun or a list refers to whoever
the lines shown make it refer to. The relation itself must still be in the words; an identification
never supplies it.

The proposed verse also carries `witnesses`: the original word by word (BHSA Hebrew or Nestle 1904
Greek, as `number surface lexeme strong morphology "gloss"`), with the King James, Berean, Synodal and
Septuagint words linked to each original word after `|` -- a word marked `~` was linked by a
statistical aligner -- the Berean, Synodal and Septuagint whole, the Textus Receptus where it differs
from Nestle, the rendered words no original word stands behind, and the footnotes. Judge the relation
in the original.

Answer with a JSON array, one object per item, in the order given, and nothing else:

  {"item": "<id>", "holds": true, "why": "..."}

  holds -- true only if the proposed verse, read with the lines shown, states this exact relation
           between these two people, in this direction, in the original. False where the relation is
           reached by joining two statements rather than read in one; where the verse states a closer
           or a different tie than the one claimed (a clan's "of the sons of" where a son is claimed, a
           grandson where a son is claimed, a brother where a half-brother is claimed) -- though a son
           or a register's "the sons of X" does state that he is X's descendant; where the direction
           is reversed; where the two names merely stand next to each other in a list; where the verse
           is about another person of the same name; where a translation says it and the original
           does not, or one witness says it and another does not; and where the words that state it
           are not in this verse or the lines shown.
  why   -- one short clause, quoting the words that decide it.

Judge the lines as written. Do not bring knowledge from outside them, and do not uphold a claim
because you know it to be true.
"""


@functools.cache
def relation_words():
    with open(RELATION_NAMES, encoding='utf-8-sig') as handle:
        return dict(re.findall(r'public const string (\w+) = "([^"]+)"', handle.read()))


@functools.cache
def broader():
    """
    The wider tie a closer one already states -- son-of states descendant-of -- as
    `RelationshipVocabulary.Broader` holds it, so a fact BibleData states loosely counts as read
    wherever a row of ours states it closely, exactly as the page pairs the two.
    """
    with open(VOCABULARY, encoding='utf-8-sig') as handle:
        text = handle.read()
    start = text.find('Broader =')
    if start < 0:
        raise SystemExit(f'no Broader table was found in {VOCABULARY}; if it has been renamed or moved, '
                         'point this function at where it is now.')
    block = text[start:text.index('};', start)]
    words = relation_words()
    return {words[closer]: words[wider] for closer, wider in
            re.findall(r'\[DescriptorRelations\.(\w+)\]\s*=\s*DescriptorRelations\.(\w+)', block)}


def csharp_table(name):
    """One of `RelationshipVocabulary`'s tables, as its source text."""
    with open(VOCABULARY, encoding='utf-8-sig') as handle:
        text = handle.read()
    start = text.find(name + ' =')
    if start < 0:
        raise SystemExit(f'no {name} table was found in {VOCABULARY}; if it has been renamed or moved, '
                         'point this harness at where it is now.')
    return text[start:text.index('};', start)]


@functools.cache
def from_the_other_end():
    """A dataset word that states a relation read from the other end: concubinator is concubine-of."""
    words = relation_words()
    return {kind: words[constant] for kind, constant in
            re.findall(r'\["([^"]+)"\]\s*=\s*DescriptorRelations\.(\w+)', csharp_table('SaysFromTheOtherEnd'))}


@functools.cache
def one_of():
    """A dataset word that states one of several relations and does not say which: victim."""
    words = relation_words()
    return {kind: {words[constant] for constant in re.findall(r'DescriptorRelations\.(\w+)', members)}
            for kind, members in re.findall(r'\["([^"]+)"\]\s*=\s*Set\(([^)]*)\)', csharp_table('SaysOneOf'))}


@functools.cache
def vocabulary():
    """
    BibleData's type names against this corpus's relation words, as the loader holds them.

    Read out of `RelationshipVocabulary.Says` and `DescriptorRelations`, because those are what
    decide whether a clause and a row say the same thing on a page. A copy here would be a second
    answer to that question, and the two drift the first time a word is added.
    """
    words = relation_words()
    entries = re.findall(r'\["([^"]+)"\]\s*=\s*DescriptorRelations\.(\w+)', csharp_table('Says'))
    if not words or not entries:
        raise SystemExit(
            f'no relation words were found in {RELATION_NAMES} or {VOCABULARY}. This harness reads '
            'DescriptorRelations and RelationshipVocabulary.Says out of the C#; if either has been '
            'moved or reshaped, point RELATION_NAMES and VOCABULARY at where they are now.')
    unknown = [constant for _, constant in entries if constant not in words]
    if unknown:
        raise SystemExit(f'RelationshipVocabulary.Says names relations DescriptorRelations does not '
                         f'declare: {", ".join(unknown)}. Check both files are from the same commit.')
    return {kind: words[constant] for kind, constant in entries}


def relationships():
    return shared.psql("""
        SELECT coalesce(json_agg(json_build_object(
                   'id', r.id, 'from', f.slug, 'to', t.slug, 'type', r.type, 'from_sex', f.sex,
                   'category', r.category, 'method', r.method, 'source', r.source,
                   'book', r.canonical_book, 'chapter', r.canonical_chapter,
                   'verse', r.canonical_verse) ORDER BY r.id), '[]')
        FROM entity_relationship r
        JOIN entity f ON f.id = r.from_entity_id
        JOIN entity t ON t.id = r.to_entity_id
    """)


def cited(row):
    return shared.reference(row['book'], row['chapter'], row['verse']) if row.get('book') else None


KILLED_BY, RAPED_BY = 'killed-by', 'raped-by'


def asked_of_either(row, choices):
    """
    Which of a word's several relations the text is asked about. This chooses only the question; the
    reading decides whether the verse states it. The dataset's `victim` puts two raped women beside
    the dead, so a woman is asked about raped-by and everybody else about killed-by.
    """
    if RAPED_BY in choices and row.get('from_sex') == FEMALE:
        return RAPED_BY
    return KILLED_BY if KILLED_BY in choices else sorted(choices)[0]


def fold(rows, says):
    """
    BibleData's rows as facts, and which of them no row of ours states from either end.

    A row joins a fact when it is the same statement or the same statement read from the other end,
    which is `INVERSE` in the harness and `Reversed` in the loader. A type the vocabulary has no word
    for folds with nothing and is its own fact, because nothing here can tell what it answers.
    """
    ours_said = set()
    for row in rows:
        if not row['source'].startswith(OUR_SOURCE):
            continue
        for relation in filter(None, (row['type'], broader().get(row['type']))):
            ours_said.add((row['from'], row['to'], relation))
            for other in shared.INVERSE.get(relation, ()):
                ours_said.add((row['to'], row['from'], other))

    other_end, either = from_the_other_end(), one_of()
    facts, index = [], {}
    for row in rows:
        if row['method'] != STATED_BY_SOURCE or not row['source'].startswith(WITNESS_SOURCE):
            continue
        relation = says.get(row['type'])
        if row['type'] in other_end:
            row = dict(row, **{'from': row['to'], 'to': row['from']})
            relation = other_end[row['type']]
        elif relation is None and row['type'] in either:
            relation = asked_of_either(row, either[row['type']])
        key = (row['from'], row['to'], relation or '?' + row['type'])
        fact = index.get(key)
        if fact is None and relation:
            for other in shared.INVERSE.get(relation, ()):
                fact = index.get((row['to'], row['from'], other))
                if fact is not None:
                    break
        if fact is None:
            fact = {'rows': []}
            facts.append(fact)
        fact['rows'].append(dict(row, relation=relation))
        index[key] = fact

    alone = []
    for fact in facts:
        if any((row['from'], row['to'], relation) in ours_said
               for row in fact['rows'] for relation in either.get(row['type']) or {row['relation']}):
            continue
        mapped = [row for row in fact['rows'] if row['relation']]
        grades = {row['category'] for row in fact['rows']}
        grade = EXPLICIT if EXPLICIT in grades else IMPLICIT if IMPLICIT in grades else INFERRED
        if not mapped:
            subset = UNMAPPED
        elif any(row['relation'] in DESCENT_RELATIONS for row in mapped):
            subset = DESCENT
        else:
            subset = EXPLICIT if grade == EXPLICIT else INFERRED
        asked = next((row for row in mapped if row['relation'] in DEFINED_BY_THE_OTHER),
                     mapped[0] if mapped else fact['rows'][0])
        alone.append({
            'subset': subset,
            'grade': grade,
            'a': asked['from'],
            'b': asked['to'],
            'relation': asked['relation'],
            'type': asked['type'],
            'cited': list(dict.fromkeys(ref for ref in map(cited, fact['rows']) if ref)),
            'rows': [{key: row[key] for key in ('id', 'from', 'to', 'type', 'category')}
                     for row in fact['rows']],
        })

    alone.sort(key=lambda fact: min(row['id'] for row in fact['rows']))
    for number, fact in enumerate(alone):
        fact['fact'] = f'f{number:04d}'
    return len(facts), sum(len(fact['rows']) for fact in facts), alone


class Corpus:
    """The encyclopedia's entities, which verses each is named in, and the King James text."""

    def __init__(self, cache_dir):
        held = shared.cache(cache_dir, 'entities', shared.entities)
        self.entities = {entity['slug']: entity for entity in held}
        self.verses = {slug: {tuple(v) for v in entity['verses']} for slug, entity in self.entities.items()}
        self.named = {}
        for book, chapter, verse, slug in shared.cache(cache_dir, 'occupancy', shared.occupancy):
            self.named.setdefault((book, chapter, verse), set()).add(slug)
        self.lines = shared.keyed(shared.cache(cache_dir, shared.RENDERING,
                                               lambda: shared.rendering(shared.RENDERING)))
        self.chapters = {}
        for book, chapter, verse in self.lines:
            self.chapters.setdefault((book, chapter), []).append(verse)
        for verses in self.chapters.values():
            verses.sort()

    def window(self, address):
        book, chapter, verse = address
        verses = self.chapters.get((book, chapter), [])
        if len(verses) > CHAPTER_SHOWN_WHOLE:
            verses = [v for v in verses if verse - WINDOW_BEFORE <= v <= verse + WINDOW_AFTER]
        return [(book, chapter, v) for v in verses]

    def both_named(self, a, b):
        shared_verses = sorted(self.verses.get(a, set()) & self.verses.get(b, set()))
        if len(shared_verses) <= BOTH_NAMED_SHOWN:
            return shared_verses
        head = shared_verses[:BOTH_NAMED_FROM_THE_HEAD]
        tail = shared_verses[BOTH_NAMED_FROM_THE_HEAD:]
        room = BOTH_NAMED_SHOWN - BOTH_NAMED_FROM_THE_HEAD
        return head + tail[::max(1, len(tail) // room)][:room]

    def namesakes(self, address, people):
        """
        Whether a verse is identified with somebody else of the same name as one of these people.

        EZR 10:21 is *of the sons of Harim; Maaseiah*, and it carries both the Maaseiah of Harim and
        the Maaseiah of Jeshua three verses up. A clause read there about either is a claim about
        whichever the identification happened to pick, and the reading cannot tell them apart.
        """
        here = self.named.get(address, set())
        return any(other != slug and other not in people
                   and self.entities[other]['name'] == self.entities[slug]['name']
                   for slug in people for other in here if other in self.entities)

    def person(self, slug):
        entity = self.entities[slug]
        return {'slug': slug, 'name': entity['name'], 'kind': entity['kind'], 'sex': entity.get('sex')}


def reads(a, relation, b):
    if relation == 'killed-by':
        return f'{a} was killed by {b}'
    return f'{a} is the {relation.removesuffix("-of").replace("-", " ")} of {b}'


def measure(args):
    says = vocabulary()
    folded, rows, alone = fold(relationships(), says)
    corpus = Corpus(args.cache)
    print(f'{folded} facts in {rows} BibleData rows; {len(alone)} facts '
          f'({sum(len(f["rows"]) for f in alone)} rows) reach no row of ours from either end.')
    for subset in SUBSETS:
        of = [fact for fact in alone if fact['subset'] == subset]
        placed = {'the cited verse names A': 0, 'names only B': 0, 'names neither': 0, 'cites none': 0}
        for fact in of:
            addresses = [shared.parse_reference(ref) for ref in fact['cited']]
            if not addresses:
                placed['cites none'] += 1
            elif any(address in corpus.verses.get(fact['a'], ()) for address in addresses):
                placed['the cited verse names A'] += 1
            elif any(address in corpus.verses.get(fact['b'], ()) for address in addresses):
                placed['names only B'] += 1
            else:
                placed['names neither'] += 1
        print(f'  {subset:<9} {len(of):>5}   ' + ', '.join(f'{count} {what}' for what, count in placed.items()))
    return alone


def pair_payload(fact, corpus):
    addresses = [a for a in map(shared.parse_reference, fact['cited']) if a]
    shown = []
    for address in addresses:
        shown += corpus.window(address)
    both = corpus.both_named(fact['a'], fact['b'])
    shown = sorted(set(shown) | set(both))
    witnessed = list(dict.fromkeys(addresses + both[:BOTH_NAMED_WITNESSED]))
    return {
        'pair': fact['fact'],
        'claim': f'{fact["a"]} {fact["relation"]} {fact["b"]}',
        'reads': reads(corpus.entities[fact['a']]['name'], fact['relation'],
                       corpus.entities[fact['b']]['name']),
        'a': corpus.person(fact['a']),
        'b': corpus.person(fact['b']),
        'cited': fact['cited'],
        'both_named_in': [shared.reference(*v) for v in both],
    }, shown, witnessed


def extract(args):
    corpus = Corpus(args.cache)
    alone = measure(args)
    os.makedirs(os.path.join(args.dir, 'batches'), exist_ok=True)
    with open(os.path.join(args.dir, 'facts.json'), 'w', encoding='utf-8') as handle:
        json.dump(alone, handle, ensure_ascii=False, indent=1)

    excluded = set()
    for other in args.exclude or []:
        with open(os.path.join(other, 'manifest.json'), encoding='utf-8') as handle:
            excluded |= {fact for batch in json.load(handle)['batches'] for fact in batch['facts']}

    pool = [fact for fact in alone
            if fact['subset'] in args.subsets and fact['fact'] not in excluded
            and fact['a'] in corpus.entities and fact['b'] in corpus.entities
            and (not args.rows or any(row['id'] in args.rows for row in fact['rows']))]
    if args.sample:
        shuffle = random.Random(args.seed)
        chosen = []
        for subset in args.subsets:
            of = [fact for fact in pool if fact['subset'] == subset]
            shuffle.shuffle(of)
            chosen += of[:round(args.sample / len(args.subsets))]
        pool = chosen

    def where(fact):
        addresses = [a for a in map(shared.parse_reference, fact['cited']) if a]
        return min(addresses) if addresses else (99, 0, 0)

    pool.sort(key=where)

    plan, batch, verses, witnessed = [], [], set(), set()
    for fact in pool:
        payload, shown, weighed = pair_payload(fact, corpus)
        if batch and (len(batch) >= PAIRS_PER_BATCH or len(verses | set(shown)) > BATCH_VERSES
                      or len(witnessed | set(weighed)) > BATCH_WITNESSED_VERSES):
            plan.append((batch, verses, witnessed))
            batch, verses, witnessed = [], set(), set()
        batch.append(payload)
        verses |= set(shown)
        witnessed |= set(weighed)
    if batch:
        plan.append((batch, verses, witnessed))
    read = shared.witnesses({address for _, _, weighed in plan for address in weighed})

    manifest = {
        'prompt_version': PROMPT_VERSION,
        'extracted': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'selection': {'subsets': args.subsets, 'sample': args.sample, 'seed': args.seed,
                      'excluded': args.exclude or [], 'drawn': len(pool)},
        'batches': [],
    }
    for number, (pairs, verses, weighed) in enumerate(plan):
        name = f'batch-{number:04d}'
        people = {slug for pair in pairs for slug in (pair['a']['slug'], pair['b']['slug'])}
        passages = [{'reference': shared.reference(*address),
                     'text': corpus.lines.get(address),
                     'named_here': sorted(corpus.named.get(address, set()) & people)}
                    for address in sorted(verses)]
        witnesses = [dict(reference=shared.reference(*address), **read[address])
                     for address in sorted(weighed) if address in read]
        with open(os.path.join(args.dir, 'batches', name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump({'batch': name, 'prompt_version': PROMPT_VERSION, 'pairs': pairs,
                       'passages': passages, 'witnesses': witnesses}, handle, ensure_ascii=False, indent=1)
        manifest['batches'].append({'batch': name, 'facts': [pair['pair'] for pair in pairs],
                                    'verses': len(passages), 'witnessed': len(witnesses)})
    with open(os.path.join(args.dir, 'manifest.json'), 'w', encoding='utf-8') as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=1)

    drawn = {subset: sum(1 for fact in pool if fact['subset'] == subset) for subset in args.subsets}
    print(f'{len(pool)} facts ({", ".join(f"{n} {s}" for s, n in drawn.items())}) in {len(plan)} '
          f'batches, {sum(len(v) for _, v, _ in plan)} verse lines, '
          f'{sum(len(w) for _, _, w in plan)} of them read in their witnesses -> {args.dir}')


def call(prompt, model, effort, system):
    command = [
        os.environ.get('CLAUDE') or shared.executable(),
        '-p', '--system-prompt', system, '--model', model,
        '--output-format', 'json',
        '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}',
        '--setting-sources', '', '--no-session-persistence', '--disable-slash-commands',
        '--disallowed-tools', 'Bash Read Write Edit Glob Grep WebFetch WebSearch Task Agent TodoWrite',
        '--max-turns', '1',
    ] + (['--effort', effort] if effort else [])
    last = None
    for attempt in range(shared.CALL_ATTEMPTS):
        try:
            process = subprocess.run(
                command, input=prompt.encode('utf-8'),
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=shared.CALL_TIMEOUT)
        except subprocess.TimeoutExpired:
            last = f'the harness answered nothing in {shared.CALL_TIMEOUT}s and was killed'
        else:
            if process.returncode != 0:
                said = process.stderr.decode('utf-8', 'replace').strip()
                last = said or f'the harness exited {process.returncode} and said nothing'
            else:
                try:
                    return json.loads(process.stdout.decode('utf-8', 'replace')), None
                except json.JSONDecodeError as broken:
                    last = f'the harness did not return JSON: {broken}'
        if attempt + 1 < shared.CALL_ATTEMPTS:
            time.sleep(5)
    return None, last


def run_batches(directory, folder, jobs, system, version, args, shape):
    """
    Each batch asked once, its answers written the moment they arrive, so a run that dies half way
    has paid for half and keeps it. `jobs` is (name, prompt, items); `shape` turns one item and the
    answer the model gave for it into the row that is kept.
    """
    out = os.path.join(directory, folder)
    os.makedirs(out, exist_ok=True)
    pending = [job for job in jobs
               if args.again or not os.path.exists(os.path.join(out, job[0] + '.jsonl'))]
    if not pending:
        print(f'{folder}: every batch already has answers. Pass --again to run them anyway.')
        return
    today = datetime.date.today().isoformat()
    lock = threading.Lock()
    began = time.time()
    totals = {'cost': 0.0, 'done': 0, 'failed': 0, 'input': 0, 'output': 0}

    def one(job):
        name, prompt, items = job
        started = time.time()
        outcome, failure = call(prompt, args.model, args.effort, system)
        seconds = time.time() - started
        if failure:
            with lock:
                totals['failed'] += 1
                print(f'{name}: {failure}', flush=True)
            return
        cost = outcome.get('total_cost_usd') or 0.0
        model = next(iter(outcome.get('modelUsage') or {}), args.model)
        answers = shared.parse(outcome.get('result'))
        if answers is None:
            with lock:
                totals['cost'] += cost
                totals['failed'] += 1
                print(f'{name}: the reply held no JSON array (${cost:.4f} paid)', flush=True)
            return
        read, wrote = shared.tokens(outcome)
        share = {'model': model, 'askedAt': today, 'effort': args.effort, 'promptVersion': version,
                 'cost': round(cost / len(items), 6), 'seconds': round(seconds / len(items), 2),
                 'inputTokens': round(read / len(items)), 'outputTokens': round(wrote / len(items))}
        rows = [dict(shape(item, answers), **share) for item in items]
        with lock:
            with open(os.path.join(out, name + '.jsonl'), 'w', encoding='utf-8') as handle:
                for row in rows:
                    handle.write(json.dumps(row, ensure_ascii=False) + '\n')
            totals['cost'] += cost
            totals['done'] += 1
            totals['input'] += read
            totals['output'] += wrote
            print(f'[{totals["done"]}/{len(pending)}] {name}: {len(items)} items, {seconds:.0f}s, '
                  f'{read} tokens in, {wrote} out, ${cost:.4f}', flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(one, pending))

    wall = time.time() - began
    path = os.path.join(out, 'runs.jsonl')
    with open(path, 'a', encoding='utf-8') as handle:
        handle.write(json.dumps({'at': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
                                 'batches': len(pending), 'failed': totals['failed'],
                                 'cost': round(totals['cost'], 4), 'wall_seconds': round(wall, 1),
                                 'workers': args.workers, 'model': args.model,
                                 'effort': args.effort, 'prompt_version': version,
                                 'input_tokens': totals['input'], 'output_tokens': totals['output']}) + '\n')
    print(f'{folder}: {totals["done"]} batches answered, {totals["failed"]} failed, '
          f'${totals["cost"]:.4f} in {wall:.0f}s wall clock')


def normalised(text):
    address = shared.parse_reference(text if isinstance(text, str) else '')
    return shared.reference(*address) if address else None


def verdict_row(pair, shown, witnessed, answers):
    """
    One fact's answer, with the verse it names settled against what the model was actually shown.

    A `stated` naming a verse other than the cited one is a `moved`, and a `moved` naming a verse it
    was not shown is kept as said but marked, because a verse nobody showed it is a verse it did
    not read. The witness it names is kept only where that text was shown for that verse -- the King
    James for any passage, the others only where the verse carried its witnesses -- so a verdict
    resting on a text it never saw says so by resting on none.
    """
    answer = next((a for a in answers if isinstance(a, dict) and a.get('pair') == pair['pair']), None)
    row = {'fact': pair['pair'], 'claim': pair['claim'], 'cited': pair['cited']}
    if not answer or answer.get('verdict') not in VERDICTS:
        return dict(row, verdict=None, reference=None, witness=None, original=None, confidence=None,
                    reason=None, how=None, shown=False)
    verdict = answer['verdict']
    reference = normalised(answer.get('reference'))
    if verdict in (STATED, DIFFER) and reference is None and pair['cited']:
        reference = pair['cited'][0]
    if verdict == STATED and reference not in pair['cited']:
        verdict = MOVED
    if verdict == MOVED and reference in pair['cited']:
        verdict = STATED
    try:
        confidence = round(min(1.0, max(0.0, float(answer.get('confidence')))), 3)
    except (TypeError, ValueError):
        confidence = None
    witness = answer.get('witness') if verdict != NOT_STATED else None
    if witness not in witnessed.get(reference, {shared.RENDERING}):
        witness = None
    original = answer.get('original') if isinstance(answer.get('original'), str) else None
    return dict(row, verdict=verdict, reference=reference if verdict != NOT_STATED else None,
                witness=witness, original=original if verdict != NOT_STATED else None,
                confidence=confidence, reason=answer.get('reason'),
                how=answer.get('how') if verdict == DIFFER else None,
                shown=verdict == NOT_STATED or reference in shown)


def ask(args):
    with open(os.path.join(args.dir, 'manifest.json'), encoding='utf-8') as handle:
        manifest = json.load(handle)
    jobs = []
    for entry in manifest['batches']:
        with open(os.path.join(args.dir, 'batches', entry['batch'] + '.json'), encoding='utf-8') as handle:
            payload = json.load(handle)
        shown = {passage['reference'] for passage in payload['passages']}
        witnessed = {block['reference']: shared.witness_names(block) for block in payload.get('witnesses', ())}
        items = [(pair, shown, witnessed) for pair in payload['pairs']]
        jobs.append((entry['batch'], json.dumps(payload, ensure_ascii=False, indent=1), items))
    run_batches(args.dir, 'out', jobs, SYSTEM, PROMPT_VERSION, args,
                lambda item, answers: verdict_row(*item, answers))


def collect(directory, folder):
    out = os.path.join(directory, folder)
    rows = []
    if os.path.isdir(out):
        for name in sorted(n for n in os.listdir(out) if re.fullmatch(r'batch-\d+\.jsonl', n)):
            with open(os.path.join(out, name), encoding='utf-8') as handle:
                rows += [json.loads(line) for line in handle if line.strip()]
    return rows


def line(key, corpus, people):
    return {'reference': shared.reference(*key), 'text': corpus.lines.get(key),
            'named_here': sorted(corpus.named.get(key, set()) & people)}


def around(address, corpus, people):
    """
    The lines a second reader needs to read a verse by: the run above it that a list continues, the
    line after it, and the heading further up where the run does not reach one.
    """
    book, chapter, verse = address
    picked = [(book, chapter, v) for v in range(verse - CHECK_BEFORE, verse + CHECK_AFTER + 1) if v != verse]
    headings = []
    for distance in range(CHECK_BEFORE + 1, CHECK_HEADING_REACH + 1):
        key = (book, chapter, verse - distance)
        if shared.HEADING.search(corpus.lines.get(key) or ''):
            headings.append(key)
            if len(headings) == CHECK_HEADINGS:
                break
    return [line(key, corpus, people) for key in sorted(set(picked + headings)) if key in corpus.lines]


def positives(directories):
    rows = {}
    for directory in directories:
        for row in collect(directory, 'out'):
            rows[row['fact']] = row
    return rows


def check(args):
    """
    The second reading, over every positive that names a verse it was shown.

    It is a different question on purpose. The first asks what the text states about a pair and
    reads a chapter to find it; this one is handed a single verse and a single claim and asked to
    refuse, which is where a reading that joined two statements, or took a clan for a father, shows.
    """
    corpus = Corpus(args.cache)
    facts = {fact['fact']: fact for fact in load_facts([args.dir])}
    wanted = [row for row in positives([args.dir]).values()
              if row['verdict'] in POSITIVE and row['shown'] and row['reference']]
    print(f'{len(wanted)} positives name a verse they were shown and are read a second time.')
    read = shared.witnesses({shared.parse_reference(row['reference']) for row in wanted})
    items = []
    for row in wanted:
        fact = facts[row['fact']]
        address = shared.parse_reference(row['reference'])
        people = {fact['a'], fact['b']}
        items.append({
            'item': row['fact'],
            'claim': reads(corpus.entities[fact['a']]['name'], fact['relation'],
                           corpus.entities[fact['b']]['name']),
            'a': corpus.person(fact['a']),
            'b': corpus.person(fact['b']),
            'verse': line(address, corpus, people),
            'rests_on': {'witness': row.get('witness'), 'original': row.get('original')},
            'witnesses': read.get(address),
            'around': around(address, corpus, people),
        })
    jobs = []
    for number, start in enumerate(range(0, len(items), CHECK_ITEMS_PER_BATCH)):
        group = items[start:start + CHECK_ITEMS_PER_BATCH]
        jobs.append((f'batch-{number:04d}', json.dumps(group, ensure_ascii=False, indent=1), group))

    def shape(item, answers):
        answer = next((a for a in answers if isinstance(a, dict) and a.get('item') == item['item']), None)
        holds = answer.get('holds') if answer else None
        return {'fact': item['item'], 'holds': holds if isinstance(holds, bool) else None,
                'why': answer.get('why') if answer else None}

    run_batches(args.dir, 'check', jobs, CHECK, CHECK_VERSION, args, shape)


def load_facts(directories):
    with open(os.path.join(directories[0], 'facts.json'), encoding='utf-8') as handle:
        return json.load(handle)


def carrier(fact, reference, corpus):
    """
    Which record a verdict can be written on, and as what -- or why it cannot be written at all.

    The loader keeps a clause only where its reference is among the verses its subject is named in.
    So the fact goes on the end the question was asked from where that verse names it, and on the
    other end, as the inverse relation, where only that one is named there. The inverse of *son of*
    is *father of* or *mother of*, and which is the other end's sex; where the encyclopedia records
    none, the dataset's own row from that end names it.
    """
    address = shared.parse_reference(reference)
    if address in corpus.verses.get(fact['a'], ()):
        return (fact['a'], fact['relation'], fact['b']), None
    if address not in corpus.verses.get(fact['b'], ()):
        return None, 'the verse names neither in this corpus'
    words = sorted(shared.INVERSE.get(fact['relation'], ()))
    if not words:
        return None, 'the verse names only the other end, and the relation has no inverse'
    if len(words) > 1:
        sex = corpus.entities[fact['b']].get('sex')
        if sex:
            words = [word for word in words if (word in FEMININE) == (sex == FEMALE)]
        else:
            says = vocabulary()
            words = [says.get(row['type']) for row in fact['rows']
                     if row['from'] == fact['b'] and says.get(row['type']) in words]
    if len(words) != 1:
        return None, 'the verse names only the other end, whose sex is not recorded'
    return (fact['b'], words[0], fact['a']), None


def standing_records(directory, prefix):
    """
    What the published files say about each entity, settled the way the loader settles it, with this
    pass's own files left out so that publishing twice cannot fold a run into itself.
    """
    records = {}
    for name in sorted(n for n in os.listdir(directory)
                       if n.endswith('.jsonl') and not n.startswith(prefix + '-')):
        with open(os.path.join(directory, name), encoding='utf-8') as handle:
            for text in handle:
                if text.strip():
                    record = json.loads(text)
                    records[record['entity']] = record
    return records


def settle_against_record(clause, refused, standing):
    """
    A clause against what its subject's record already says about the same person.

    Measured on the pilot, every such case was one tie read in two degrees, and the loader had
    already withheld ours for disagreeing with the dataset: *Muppim son of Benjamin* against
    BibleData's *descendant*, *Jerimoth descendant of Bela* against its *son*. A vaguer tie never
    joins a closer one on a line. A closer one replaces our own *descendant of*, because it was read
    twice and a son is what the vaguer clause was approximating. Anything else in the same branch is
    two readings disagreeing, and neither is overruled here.
    """
    if clause is None:
        return None, refused, []
    subject, relation, target = clause
    same = [c for c in (standing.get(subject) or {}).get('claims', [])
            if c['target'] == target and branch(c['relation']) == branch(relation)]
    if not same:
        return clause, None, []
    if any(c['relation'] == relation for c in same):
        return None, 'our record already says it, and the loader refuses it', []
    if relation in DESCENT_RELATIONS:
        return None, 'our record already states a closer tie', []
    if all(c['relation'] in DESCENT_RELATIONS for c in same):
        return clause, None, sorted({c['relation'] for c in same})
    return None, 'our record states a different tie', []


def outcomes(directories, cache_dir, descriptors_dir, prefix):
    """Every fact, what the first reading said, what the second said, and what can be written."""
    corpus = Corpus(cache_dir)
    standing = standing_records(descriptors_dir, prefix)
    facts = load_facts(directories)
    first = positives(directories)
    second = {}
    for directory in directories:
        for row in collect(directory, 'check'):
            second[row['fact']] = row
    settled = []
    for fact in facts:
        row = first.get(fact['fact'])
        outcome = {'fact': fact, 'reading': row, 'check': second.get(fact['fact'])}
        if row is None:
            outcome['why'] = 'no relation word yet' if fact['subset'] == UNMAPPED else 'not asked'
        elif row['verdict'] is None:
            outcome['why'] = 'no usable answer'
        elif row['verdict'] == NOT_STATED:
            outcome['why'] = 'the text shown does not state it'
        elif row['verdict'] == DIFFER:
            outcome['why'] = 'the witnesses differ, which is for the owner to read'
        elif not row['shown']:
            outcome['why'] = 'moved to a verse the reader was not shown'
        elif outcome['check'] is None or outcome['check']['holds'] is None:
            outcome['why'] = 'not read a second time'
        elif not outcome['check']['holds']:
            outcome['why'] = 'the second reading refuses it'
        elif corpus.namesakes(shared.parse_reference(row['reference']), (fact['a'], fact['b'])):
            outcome['why'] = 'the verse is identified with two people of that name'
        else:
            clause, refused = carrier(fact, row['reference'], corpus)
            clause, refused, replaces = settle_against_record(clause, refused, standing)
            outcome['replaces'] = replaces
            outcome['clause'] = clause
            outcome['why'] = refused
        settled.append(outcome)
    return corpus, standing, settled


def score(args):
    corpus, _, settled = outcomes(args.dir, args.cache, args.to, args.prefix)
    asked = [o for o in settled if o['reading']]
    lines = []
    write = lines.append
    write('# BibleData-only relationships, read against the text\n')
    write('## Verdicts, first reading\n')
    write('| subset | asked | stated | moved | witnesses differ | not-stated | no answer |')
    write('|---|---|---|---|---|---|---|')
    for subset in SUBSETS:
        of = [o for o in asked if o['fact']['subset'] == subset]
        if not of:
            continue
        count = {v: sum(1 for o in of if o['reading']['verdict'] == v) for v in (*VERDICTS, None)}
        write(f'| {subset} | {len(of)} | {count[STATED]} | {count[MOVED]} | {count[DIFFER]} | '
              f'{count[NOT_STATED]} | {count[None]} |')

    write('\n## Second reading, over the positives\n')
    write('| subset | read again | upheld | refused | refused share |')
    write('|---|---|---|---|---|')
    for subset in SUBSETS:
        checked = [o for o in asked if o['fact']['subset'] == subset and o['check']
                   and o['check']['holds'] is not None]
        if checked:
            upheld = sum(1 for o in checked if o['check']['holds'])
            write(f'| {subset} | {len(checked)} | {upheld} | {len(checked) - upheld} | '
                  f'{(len(checked) - upheld) / len(checked):.0%} |')

    write('\n## What becomes ours, and why the rest stays BibleData\'s\n')
    ours = [o for o in settled if o.get('clause')]
    write(f'**{len(ours)} of {len(settled)} facts become clauses of this corpus.**\n')
    why = {}
    for o in settled:
        if not o.get('clause'):
            key = (o['fact']['subset'], o['why'])
            why[key] = why.get(key, 0) + 1
    write('| subset | why it stays | facts |')
    write('|---|---|---|')
    for (subset, reason), count in sorted(why.items(), key=lambda kv: (SUBSETS.index(kv[0][0]), -kv[1])):
        write(f'| {subset} | {reason} | {count} |')
    written = {}
    for o in ours:
        side = 'on the end asked' if o['clause'][0] == o['fact']['a'] else 'on the other end, inverted'
        written[side] = written.get(side, 0) + 1
    write('\n' + ', '.join(f'{n} written {side}' for side, n in written.items())
          + f'; {sum(1 for o in ours if o["replaces"])} of them replace a vaguer clause of ours.')

    write('\n## Cost\n')
    for folder in ('out', 'check'):
        rows = [r for d in args.dir for r in collect(d, folder)]
        runs = []
        for d in args.dir:
            path = os.path.join(d, folder, 'runs.jsonl')
            if os.path.exists(path):
                with open(path, encoding='utf-8') as handle:
                    runs += [json.loads(line) for line in handle if line.strip()]
        spent = sum(r['cost'] for r in runs)
        wall = sum(r['wall_seconds'] for r in runs)
        read = sum(r.get('inputTokens') or 0 for r in rows)
        wrote = sum(r.get('outputTokens') or 0 for r in rows)
        write(f'- {folder}: {len(rows)} items, ${spent:.4f} paid over {len(runs)} runs, '
              f'{wall:.0f}s wall clock, ${spent / max(1, len(rows)):.5f} an item, '
              f'{read / max(1, len(rows)):.0f} tokens read and {wrote / max(1, len(rows)):.0f} written an item')
    first_cost = sum(r['cost'] for d in args.dir for r in collect(d, 'out'))
    second_cost = sum(r['cost'] for d in args.dir for r in collect(d, 'check'))
    if asked:
        per_fact = (first_cost + second_cost) / len(asked)
        write(f'\n**${per_fact:.5f} a fact asked**, both readings together. Projected over the whole of '
              'each subset:\n')
        write('| subset | facts | projected |')
        write('|---|---|---|')
        for subset in SUBSETS:
            total = sum(1 for o in settled if o['fact']['subset'] == subset)
            of = [o for o in asked if o['fact']['subset'] == subset]
            spent = sum((o['reading'].get('cost') or 0) + ((o['check'] or {}).get('cost') or 0) for o in of)
            rate = spent / len(of) if of else per_fact
            write(f'| {subset} | {total} | ${rate * total:.2f} at ${rate:.5f} a fact |')

    write('\n## Moved\n')
    for o in [o for o in asked if o['reading']['verdict'] == MOVED][:args.examples]:
        write(f'- `{o["fact"]["fact"]}` {o["reading"]["claim"]}: cited {", ".join(o["fact"]["cited"]) or "nothing"}, '
              f'read in **{o["reading"]["reference"]}** ({o["reading"]["confidence"]}) -- {o["reading"]["reason"]}')

    write('\n## Witnesses differ\n')
    for o in [o for o in asked if o['reading']['verdict'] == DIFFER][:args.examples]:
        write(f'- `{o["fact"]["fact"]}` {o["reading"]["claim"]} in {o["reading"]["reference"]}, stated by '
              f'{o["reading"].get("witness") or "?"} ({o["reading"].get("original") or "-"}): {o["reading"].get("how")}')

    write('\n## Refused by the second reading\n')
    for o in [o for o in asked if o['check'] and o['check']['holds'] is False][:args.examples]:
        write(f'- `{o["fact"]["fact"]}` [{o["fact"]["subset"]}] {o["reading"]["claim"]} in '
              f'{o["reading"]["reference"]}: first "{o["reading"]["reason"]}"; second "{o["check"]["why"]}"')

    report = '\n'.join(lines) + '\n'
    with open(os.path.join(args.dir[-1], 'report.md'), 'w', encoding='utf-8') as handle:
        handle.write(report)
    print(report)

    shuffle = random.Random(args.seed)
    for verdict, name in ((STATED, 'sample-stated.md'), (NOT_STATED, 'sample-not-stated.md')):
        pool = [o for o in asked if o['reading']['verdict'] == verdict]
        shuffle.shuffle(pool)
        out = [f'# {len(pool)} {verdict}, {min(len(pool), SAMPLE_READ_BY_EYE)} drawn with seed {args.seed}\n']
        for o in pool[:SAMPLE_READ_BY_EYE]:
            fact = o['fact']
            out.append(f'## {fact["fact"]} [{fact["subset"]}] {o["reading"]["claim"]}\n')
            out.append(f'{reads(corpus.entities[fact["a"]]["name"], fact["relation"], corpus.entities[fact["b"]]["name"])}; '
                       f'cited {", ".join(fact["cited"]) or "nothing"}; confidence {o["reading"]["confidence"]}\n')
            out.append(f'> reason: {o["reading"]["reason"]}\n')
            if o['check']:
                out.append(f'> second reading: holds={o["check"]["holds"]} -- {o["check"]["why"]}\n')
            for ref in dict.fromkeys(([o['reading']['reference']] if o['reading']['reference'] else []) + fact['cited']):
                address = shared.parse_reference(ref)
                out.append(f'- **{ref}** {corpus.lines.get(address)}')
                out.append(f'  (named here: {", ".join(sorted(corpus.named.get(address, ())))})')
            out.append('')
        with open(os.path.join(args.dir[-1], name), 'w', encoding='utf-8') as handle:
            handle.write('\n'.join(out) + '\n')


def publish(args):
    """
    The upheld, writable facts as descriptor records, under a prefix that sorts after every other.

    The loader takes an entity's record from the file that sorts last and replaces what it loaded
    for that entity with it, so a record here is the standing record with the new clauses after its
    own -- never the new clauses alone, which would cost the entity every clause it already had.
    The standing record is read with this prefix's own files left out, so publishing twice gives
    the same files rather than folding a run into itself.
    """
    corpus, standing, settled = outcomes(args.dir, args.cache, args.to, args.prefix)
    if not standing:
        raise SystemExit(f'{args.to} holds no descriptor records to extend. Point --to at '
                         'Resources/Essenthos/descriptors.')
    later = sorted(n for n in os.listdir(args.to) if n.endswith('.jsonl') and n > args.prefix + '-')
    if any(not n.startswith(args.prefix + '-') for n in later):
        raise SystemExit(f'{later[-1]} sorts after the prefix "{args.prefix}", so the loader would read it '
                         'last and these records would not supersede it. Choose a prefix that sorts later.')

    today = datetime.date.today().isoformat()
    by_entity, duplicates, replaced = {}, 0, 0
    for o in settled:
        if not o.get('clause'):
            continue
        subject, relation, target = o['clause']
        before = standing.get(subject)
        said = {(c['relation'], c['target']) for c in (before or {}).get('claims', [])}
        if (relation, target) in said:
            duplicates += 1
            continue
        record = by_entity.setdefault(subject, {
            'entity': subject,
            'kind': (before or {}).get('kind') or corpus.entities[subject]['kind'],
            'claims': list((before or {}).get('claims', [])),
            'names': (before or {}).get('names') or {},
            'unresolved': (before or {}).get('unresolved') or [],
            'model': o['reading']['model'],
            'askedAt': today,
        })
        if (relation, target) in {(c['relation'], c['target']) for c in record['claims']}:
            duplicates += 1
            continue
        claim = {'relation': relation, 'target': target, 'reference': o['reading']['reference'],
                 'confidence': o['reading']['confidence'], 'reason': o['reading']['reason']}
        claim.update({key: o['reading'][key] for key in ('witness', 'original') if o['reading'].get(key)})
        vaguer = [at for at, c in enumerate(record['claims'])
                  if c['target'] == target and c['relation'] in o['replaces']]
        if vaguer:
            record['claims'][vaguer[0]] = claim
            record['claims'] = [c for at, c in enumerate(record['claims']) if at not in vaguer[1:]]
            replaced += 1
        else:
            record['claims'].append(claim)

    for name in os.listdir(args.to):
        if name.startswith(args.prefix + '-') and name.endswith('.jsonl'):
            os.remove(os.path.join(args.to, name))
    records = [by_entity[slug] for slug in sorted(by_entity)]
    files = 0
    for files, start in enumerate(range(0, len(records), RECORDS_PER_FILE), start=1):
        with open(os.path.join(args.to, f'{args.prefix}-{files - 1:04d}.jsonl'), 'w', encoding='utf-8') as handle:
            for record in records[start:start + RECORDS_PER_FILE]:
                handle.write(json.dumps(record, ensure_ascii=False) + '\n')
    added = sum(1 for o in settled if o.get('clause')) - duplicates
    print(f'{files} files, {len(records)} records, {added} new clauses -> {args.to}; '
          f'{sum(1 for slug in by_entity if slug in standing)} records extend a standing one, '
          f'{replaced} clauses replace a vaguer one of ours, '
          f'{duplicates} clauses were already in the standing record')


DECIDED_BY = 'the project owner, decided {date}'
REVIEW = os.path.join('Resources', 'Essenthos', 'review')


def decide(args):
    """
    The owner's decisions from the review page, applied to the facts only BibleData states.

    A fact confirmed becomes a clause of this corpus that the owner decided: written on the end whose
    verse names it, with no confidence and a `decidedBy`, so the loader stores it as a person's
    judgement and not as a reading. A fact to re-ask is printed as BibleData row ids for
    `extract --rows`. A fact removed stays BibleData's and is listed in a review file for the cut-over,
    which is where BibleData's rows leave the page. Only the page's categories asked for are applied,
    so a decision taken on the page is never applied before somebody asks for it.
    """
    corpus = Corpus(args.cache)
    standing = standing_records(args.to, args.prefix)
    # The rows an earlier run of this command put in the corpus are left out of the fold: their clauses
    # live only in the files this command rewrites, so a fact they settled must come back to be written again.
    rows = [row for row in relationships() if DECIDED_BY.split(',')[0] not in row['source']]
    _, _, alone = fold(rows, vocabulary())
    corrected = dict(item.split('=', 1) for item in args.reference or [])
    relabelled = dict(item.split('=', 1) for item in args.relation or [])
    held = {int(row) for row in args.hold or []}
    unsure = {row: float(sure) for row, sure in (item.split('=', 1) for item in args.unsure or [])}
    flipped = {int(row) for row in args.flip or []}

    decisions = []
    for name in sorted(os.listdir(args.decisions)):
        if not name.endswith('.json'):
            continue
        with open(os.path.join(args.decisions, name), encoding='utf-8') as handle:
            document = json.load(handle)
        body = document.get('data', document)
        if body.get('subset') in args.subsets and body.get('decision'):
            decisions.append(({int(row) for row in str(body['rows']).split('|')}, body))

    by_entity, report, reask, removed = {}, {}, [], []

    def count(what):
        report[what] = report.get(what, 0) + 1

    reached = {row['id'] for fact in alone for row in fact['rows']}
    for rows, body in decisions:
        if not rows <= reached:
            count(f'{body["decision"]}, but a row of ours already states it: nothing to write')

    for fact in alone:
        ids = {row['id'] for row in fact['rows']}
        mine = [body for rows, body in decisions if rows <= ids]
        if not mine:
            continue
        # The page keys a decision by the rows the fold held when it was taken, so a fold that has
        # since joined two rows leaves an older decision beside a newer one about the same fact. The
        # owner's latest word is the one that stands.
        verdicts = {body['decision'] for body in mine}
        if len(verdicts) > 1:
            latest = max(body['decidedAt'] for body in mine)
            mine = [body for body in mine if body['decidedAt'] == latest]
            count('decisions disagree about one fact: the latest stands')
            verdicts = {body['decision'] for body in mine}
        verdict = sorted(verdicts)[0]
        if ids & held:
            count(f'{verdict}, held: the note questions who is meant')
            continue
        note = ' '.join(body['note'] for body in mine if body.get('note'))
        # A removal whose note names the right word, and a re-ask whose note accepts it as unsure,
        # are confirmations in the owner's own terms.
        # A removal or a re-ask whose note names the right word is a correction, not a refusal.
        if verdict in ('remove', 'reask') and any(str(i) in relabelled for i in ids):
            verdict = 'confirm'
        if verdict == 'reask' and any(str(i) in unsure for i in ids):
            verdict = 'confirm'
        if verdict == 'remove':
            removed.append({'rows': sorted(ids), 'a': fact['a'], 'relation': fact['relation'] or fact['type'],
                            'b': fact['b'], 'note': note, 'decidedAt': max(b['decidedAt'] for b in mine)})
            count('removed: stays BibleData\'s until the cut-over')
            continue
        if verdict == 'reask':
            reask.extend(sorted(ids))
            count('to re-ask')
            continue
        if verdict != 'confirm':
            count(f'left alone: {verdict}')
            continue

        reference = next((corrected[str(i)] for i in sorted(ids) if str(i) in corrected), None) \
            or (fact['cited'][0] if fact['cited'] else None)
        if not reference or not fact['relation']:
            count('confirmed, but there is no verse or no relation word to write it with')
            continue
        # The dataset states the tie from the end the owner does not want it read from -- a king is
        # not defined by whose king he is, but a commander is by whose commander he is. Reading it
        # the other way round is a decision about the fact, not about the words, so it is said here.
        if ids & flipped:
            fact = dict(fact, a=fact['b'], b=fact['a'])
        relation_as = next((relabelled[str(i)] for i in sorted(ids) if str(i) in relabelled), None)
        if relation_as:
            fact = dict(fact, relation=relation_as)
        clause, refused = carrier(fact, reference, corpus)
        clause, refused, _ = settle_against_record(clause, refused, standing)
        if not clause:
            count(f'confirmed, not writable: {refused}')
            continue

        subject, relation, target = clause
        before = standing.get(subject) or {}
        record = by_entity.setdefault(subject, {
            'entity': subject,
            'kind': before.get('kind') or corpus.entities[subject]['kind'],
            'claims': list(before.get('claims', [])),
            'names': before.get('names') or {},
            'unresolved': before.get('unresolved') or [],
            'model': before.get('model') or 'the project owner',
            'askedAt': before.get('askedAt') or max(b['decidedAt'] for b in mine)[:10],
        })
        if (relation, target) in {(c['relation'], c['target']) for c in record['claims']}:
            count('confirmed, already in the record')
            continue
        sure = next((unsure[str(i)] for i in sorted(ids) if str(i) in unsure), None)
        record['claims'].append({
            'relation': relation, 'target': target, 'reference': reference, 'confidence': sure,
            'reason': note or 'the verse states it, decided on the review page',
            'decidedBy': DECIDED_BY.format(date=max(b['decidedAt'] for b in mine)[:10]),
        })
        count('confirmed and written')

    for name in os.listdir(args.to):
        if name.startswith(args.prefix + '-') and name.endswith('.jsonl'):
            os.remove(os.path.join(args.to, name))
    records = [by_entity[slug] for slug in sorted(by_entity)]
    for number, start in enumerate(range(0, len(records), RECORDS_PER_FILE)):
        with open(os.path.join(args.to, f'{args.prefix}-{number:04d}.jsonl'), 'w', encoding='utf-8') as handle:
            for record in records[start:start + RECORDS_PER_FILE]:
                handle.write(json.dumps(record, ensure_ascii=False) + '\n')

    if removed:
        os.makedirs(REVIEW, exist_ok=True)
        path = os.path.join(REVIEW, 'bibledata-removed.json')
        kept = []
        if os.path.exists(path):
            with open(path, encoding='utf-8') as handle:
                kept = [r for r in json.load(handle) if r['rows'] not in [x['rows'] for x in removed]]
        with open(path, 'w', encoding='utf-8') as handle:
            json.dump(kept + removed, handle, ensure_ascii=False, indent=1)

    print(f'{len(records)} records -> {args.to} under "{args.prefix}"')
    for what, n in sorted(report.items()):
        print(f'  {n:>4}  {what}')
    if reask:
        print('re-ask with: extract --rows ' + ' '.join(map(str, reask)))


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    parser.add_argument('--cache', default='.relationships/cache',
                        help='where the database reads are kept between runs')
    parser.add_argument('--database', default=shared.DATABASE,
                        help='which database to read; a copy the published files were loaded into '
                             'is how what they change is measured before the live corpus loads them')
    commands = parser.add_subparsers(dest='command', required=True)

    measurer = commands.add_parser('measure', help='count the facts only BibleData states')
    measurer.set_defaults(run=measure)

    extractor = commands.add_parser('extract', help='write the prompt payloads for a run')
    extractor.add_argument('--out', dest='dir', required=True)
    extractor.add_argument('--subsets', nargs='+', default=[EXPLICIT, INFERRED, DESCENT],
                           choices=[EXPLICIT, INFERRED, DESCENT])
    extractor.add_argument('--sample', type=int, help='draw about this many facts, evenly across the subsets')
    extractor.add_argument('--seed', type=int, default=7)
    extractor.add_argument('--exclude', nargs='+', help="earlier runs' directories whose facts are not asked again")
    extractor.add_argument('--rows', nargs='+', type=int,
                           help='only the facts holding one of these BibleData row ids, as `decide` prints them')
    extractor.set_defaults(run=extract)

    decider = commands.add_parser('decide', help="apply the owner's decisions from the review page")
    decider.add_argument('--decisions', required=True,
                         help='a directory of the decision documents the review page stored, one JSON file each')
    decider.add_argument('--subsets', nargs='+', default=[UNMAPPED],
                         help="which of the page's categories to apply; a decision in another is left alone")
    decider.add_argument('--reference', nargs='+',
                         help='ROW_ID=BOOK C:V, where the decision corrects the verse BibleData cites')
    decider.add_argument('--relation', nargs='+',
                         help='ROW_ID=relation, where the decision reads the fact in another word of the vocabulary')
    decider.add_argument('--hold', nargs='+',
                         help='row ids whose decision waits, because its note says the person is not who the record names')
    decider.add_argument('--flip', nargs='+',
                         help='row ids the owner reads from the other end, so A and B swap before the relation is read')
    decider.add_argument('--unsure', nargs='+',
                         help='ROW_ID=confidence, where the owner accepts the fact but says the verse does not settle it')
    decider.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'descriptors'))
    decider.add_argument('--prefix', default='words')
    decider.set_defaults(run=decide)

    for name, run, what in (('ask', ask, 'the first reading'), ('check', check, 'the second reading')):
        sub = commands.add_parser(name, help=f'run {what} over every batch that has no answers yet')
        sub.add_argument('--dir', required=True)
        sub.add_argument('--model', default='sonnet')
        sub.add_argument('--workers', type=int, default=4)
        sub.add_argument('--effort', default='medium',
                         help='low was measured noisy on reading work (PRB-0437); medium is the default here')
        sub.add_argument('--again', action='store_true')
        sub.set_defaults(run=run)

    scorer = commands.add_parser('score', help='the verdicts, the second reading, what becomes ours, the cost')
    scorer.add_argument('--dir', nargs='+', required=True)
    scorer.add_argument('--examples', type=int, default=30)
    scorer.add_argument('--seed', type=int, default=7)
    scorer.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'descriptors'))
    scorer.add_argument('--prefix', default='verify')
    scorer.set_defaults(run=score)

    publisher = commands.add_parser('publish', help='write the upheld facts as descriptor records')
    publisher.add_argument('--dir', nargs='+', required=True)
    publisher.add_argument('--to', default=os.path.join('Resources', 'Essenthos', 'descriptors'))
    publisher.add_argument('--prefix', default='verify')
    publisher.set_defaults(run=publish)

    args = parser.parse_args()
    shared.DATABASE = args.database
    args.run(args)


if __name__ == '__main__':
    main()
