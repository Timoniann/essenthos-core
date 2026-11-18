"""
Word-by-word check of a hand-made English alignment to BHSA (TSK-1859): a programmatic suspicion
pass, then a context-free Haiku score per suspicious rendering.

    python scripts/handcheck.py export BSB ULT                      # live database -> <work>/data (read-only)
    python scripts/handcheck.py pass BSB --other ULT [--books Gen] [--run BSB-OT]
    python scripts/handcheck.py estimate BSB-OT                     # requests and upper-bound cost, nothing sent
    python scripts/handcheck.py score BSB-OT [--limit N]            # one batch through claude_api.py, capped
    python scripts/handcheck.py report BSB-OT                       # flagged, crossing and missing TSVs, summary
    python scripts/handcheck.py sample BSB-OT --kind low --count 40 [--skip-books Gen]
    python scripts/handcheck.py evaluate --key key.json --runs BSB,ULT [--scores v1|ab]   # precision/recall
    python scripts/handcheck.py abscore --key key.json --runs BSB,ULT                     # score the key's lemmas

<work> is essenthos-core/.claude-api/handcheck of the checkout given by --repo (git-ignored), so the
ledger, the key and the outputs stay with the main checkout when this runs from a worktree. A run
is named <PAIR> or <PAIR>-<tag>; its files are <run>-pass.json, <run>-scores.json and so on.

For every Hebrew content word (pos not art/conj/prep) the rendering is the English words of every
stated link that holds it, in order. Its key drops articles, possessives, pronouns, auxiliaries,
`and` and prepositions, which in BHSA's tokenisation belong to the prefix words beside it. A
rendering is accepted when a key word matches a gloss word (BHSA gloss, Strong's definition and
KJV renderings, MACULA's gloss and contextual English) by a crude stem; a name when it is close to
the gloss; by one of the exact function-word rules (rule_for); or when another content word of the
same link carries the meaning (shared-link: the source states that the words render it together). A rule never accepts a rendering
while an unlinked English word of the verse matches the Hebrew word's gloss (an orphan): that is
the shape of a lost link. Anything else is only suspected: rare, carried by a neighbour (the
shift shape of PRB-1851), or disagreeing with another alignment of the same word.

Occurrence-level detectors, reported apart from the scored renderings:
- crossing: two neighbouring Hebrew words whose renderings each match the other's gloss
  (half-crossing: both unaccepted and one matches the other's);
- missing: a Hebrew content word with no link while a neighbour's rendering carries its meaning,
  or while an unlinked English word of the verse matches its gloss.
"""
import argparse
import collections
import csv
import difflib
import functools
import importlib.util
import json
import re
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONTAINER = 'essenthos-core-db-1'
READ_ONLY = 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=300000'
MODEL = 'claude-haiku-5-5'
LABEL = 'tsk1859-handcheck'
CAP_USD = 7.0
SLUGS = {'BHSA': 1}
FUNCTION_POS = {'art', 'conj', 'prep'}
WAW = 'וְ'
POS_NAMES = {'subs': 'noun', 'verb': 'verb', 'nmpr': 'proper noun', 'adjv': 'adjective', 'advb': 'adverb',
             'prps': 'personal pronoun', 'prde': 'demonstrative pronoun', 'prin': 'interrogative pronoun',
             'nega': 'negative particle', 'intj': 'interjection', 'inrg': 'interrogative particle'}
STOP = set("""
the a an my your his her its our their thy thine mine yours hers ours theirs whose
i me you he him she it we us they them thee ye myself yourself himself herself itself ourselves themselves
and or but nor so then
of to in into on upon at by for from with within without unto toward towards against among over under
before after about through throughout between beside besides until till
be is am are was were been being shall will would should may might must can could do does did done
have has had having let
that this these those who whom which what
""".split())
PRONOUNS = set("""
i me my mine myself you your yours yourself yourselves thou thee thy thine ye he him his himself she her hers
herself it its itself we us our ours ourselves they them their theirs themselves this that these those
""".split())
FILLER = set('and so then now but yet also even the a an of to in into for with from by on at upon unto own self'.split())
CONJUNCTIONS = set('and then so now but yet when also or thus the a an'.split())
COPULA = set('is am are was were be been being'.split())
AUXILIARIES = COPULA | set("""
do does did will would shall should can could may might must have has had isn't aren't wasn't weren't
don't doesn't didn't won't wouldn't shan't shouldn't can't couldn't haven't hasn't hadn't
""".split())
NEGATIVES = set("not no nor neither never nothing none nobody without cannot un".split()) | \
            {w for w in AUXILIARIES if w.endswith("n't")}
QUESTION = set('how what why where who whom which whether if'.split())
HAYAH_MARKERS = set('and now then so when as after afterward afterwards later meanwhile while once it came to '
                    'pass happened there this that'.split())
INTENSIFIERS = set('surely indeed certainly really truly utterly fully diligently freely gladly steadily '
                   'completely thoroughly altogether earnestly greatly continually'.split())
IRREGULAR = {'said': 'say', 'says': 'say', 'went': 'go', 'gone': 'go', 'goes': 'go', 'came': 'come',
             'gave': 'give', 'given': 'give', 'took': 'take', 'taken': 'take', 'saw': 'see', 'seen': 'see',
             'made': 'make', 'knew': 'know', 'known': 'know', 'ate': 'eat', 'eaten': 'eat', 'brought': 'bring',
             'sent': 'send', 'spoke': 'speak', 'spoken': 'speak', 'stood': 'stand', 'sat': 'sit', 'fell': 'fall',
             'fallen': 'fall', 'built': 'build', 'smote': 'smite', 'smitten': 'smite', 'struck': 'strike',
             'slew': 'slay', 'slain': 'slay', 'killed': 'kill', 'bore': 'bear', 'born': 'bear', 'borne': 'bear',
             'begot': 'beget', 'begat': 'beget', 'fled': 'flee', 'led': 'lead', 'held': 'hold', 'heard': 'hear',
             'told': 'tell', 'found': 'find', 'left': 'leave', 'kept': 'keep', 'lay': 'lie', 'lain': 'lie',
             'arose': 'arise', 'rose': 'rise', 'risen': 'rise', 'wrote': 'write', 'written': 'write',
             'drank': 'drink', 'drunk': 'drink', 'thought': 'think', 'bought': 'buy', 'sold': 'sell',
             'fought': 'fight', 'caught': 'catch', 'taught': 'teach', 'sought': 'seek', 'dwelt': 'dwell',
             'men': 'man', 'women': 'woman', 'children': 'child', 'feet': 'foot', 'teeth': 'tooth',
             'oxen': 'ox', 'mice': 'mouse', 'lives': 'life', 'wives': 'wife', 'knives': 'knife',
             'died': 'die', 'dead': 'die', 'death': 'die', 'better': 'good', 'best': 'good', 'greater': 'great',
             'shut': 'shut', 'put': 'put', 'set': 'set', 'cast': 'cast', 'hid': 'hide', 'hidden': 'hide',
             'chose': 'choose', 'chosen': 'choose', 'drew': 'draw', 'drawn': 'draw', 'grew': 'grow',
             'threw': 'throw', 'swore': 'swear', 'sworn': 'swear', 'wept': 'weep', 'slept': 'sleep',
             'met': 'meet', 'became': 'become', 'began': 'begin', 'begun': 'begin', 'forgot': 'forget',
             'forsook': 'forsake', 'forsaken': 'forsake', 'ran': 'run', 'won': 'win', 'broke': 'break',
             'broken': 'break', 'bound': 'bind', 'shook': 'shake', 'wore': 'wear', 'tore': 'tear', 'torn': 'tear',
             'was': 'be', 'were': 'be', 'is': 'be', 'am': 'be', 'are': 'be', 'been': 'be', 'being': 'be',
             'did': 'do', 'does': 'do', 'done': 'do', 'had': 'have', 'has': 'have', 'having': 'have'}
WORD = re.compile(r"[a-z]+(?:'[a-z]+)?|\d+")
NUMBERS = set('one two three four five six seven eight nine ten twenty hundred thousand myriad first second third'
              ' fourth fifth sixth seventh eighth ninth tenth'.split())
WINDOW = 3


# ------------------------------------------------------------------------------------------- reading

def work_dir(args):
    path = Path(args.repo).resolve() / '.claude-api' / 'handcheck'
    path.mkdir(parents=True, exist_ok=True)
    return path


def copy_out(sql, target):
    """COPY one query to a TSV file, read-only, through the container's own psql."""
    with open(target, 'wb') as out:
        process = subprocess.run(
            ['docker', 'exec', '-i', '-e', 'PGCLIENTENCODING=UTF8', '-e', READ_ONLY, CONTAINER,
             'psql', '-U', 'essenthos', '-d', 'essenthos_core', '-v', 'ON_ERROR_STOP=1', '-q', '-f', '-'],
            input=f'COPY ({sql}) TO STDOUT;'.encode('utf-8'), stdout=out, stderr=subprocess.PIPE)
    if process.returncode:
        raise SystemExit(process.stderr.decode('utf-8', 'replace'))


def rows(path):
    with open(path, encoding='utf-8') as handle:
        for line in handle:
            yield [None if f == r'\N' else f for f in line.rstrip('\n').split('\t')]


def text_id(slug):
    out = subprocess.run(
        ['docker', 'exec', '-i', '-e', READ_ONLY, CONTAINER, 'psql', '-U', 'essenthos', '-d', 'essenthos_core',
         '-Aqt', '-c', f"select id from text where slug = '{slug}'"], capture_output=True, text=True)
    if not out.stdout.strip():
        raise SystemExit(f'no text {slug}')
    return int(out.stdout.strip())


def export(args):
    data = work_dir(args) / 'data'
    data.mkdir(exist_ok=True)
    bhsa = SLUGS['BHSA']
    copy_out(f"select w.id, w.verse_id, w.position, w.text, w.lemma, w.strong_number, w.gloss, w.morphology->>'pos' "
             f"from word w where w.text_id = {bhsa}", data / 'bhsa-words.tsv')
    copy_out(f"select v.id, b.abbreviation, v.chapter_number, v.number from verse v join book b on b.id = v.book_id "
             f"where v.text_id = {bhsa}", data / 'bhsa-verses.tsv')
    copy_out("select strong_number, coalesce(definition, ''), coalesce(kjv_definition, '') from strong_entry "
             "where strong_number like 'H%'", data / 'strong.tsv')
    for slug in args.pairs:
        tid = text_id(slug)
        copy_out(f"select l.id, lw.side, lw.word_id from link l join link_word lw on lw.link_id = l.id "
                 f"where l.from_text_id = {tid} and l.to_text_id = {bhsa} and l.method = 'stated-by-source' "
                 f"and l.relation = 'renders'", data / f'{slug}-links.tsv')
        copy_out(f"select w.id, w.verse_id, w.position, w.text from word w where w.text_id = {tid}",
                 data / f'{slug}-words.tsv')
        copy_out(f"select v.id, b.abbreviation, v.chapter_number, v.number from verse v join book b on b.id = v.book_id "
                 f"where v.text_id = {tid}", data / f'{slug}-verses.tsv')
        print(f'{slug}: exported')


def load_bhsa(data):
    hebrew, by_verse = {}, collections.defaultdict(dict)
    for wid, verse, position, surface, lemma, strong, gloss, pos in rows(data / 'bhsa-words.tsv'):
        w = {'id': int(wid), 'verse': int(verse), 'pos_n': int(position), 'surface': surface, 'lemma': lemma,
             'strong': strong, 'gloss': gloss, 'pos': pos}
        hebrew[w['id']] = w
        by_verse[w['verse']][w['pos_n']] = w
    refs, book_of = {}, {}
    for v, b, c, n in rows(data / 'bhsa-verses.tsv'):
        refs[int(v)] = f'{b} {c}:{n}'
        book_of[int(v)] = b
    return hebrew, by_verse, refs, book_of


class Pair:
    """One translation's stated links to BHSA, read from the export."""

    def __init__(self, data, slug):
        self.english = {int(r[0]): (int(r[1]), int(r[2]), r[3]) for r in rows(data / f'{slug}-words.tsv')}
        links = collections.defaultdict(lambda: ([], []))
        for link_id, side, word_id in rows(data / f'{slug}-links.tsv'):
            links[int(link_id)][0 if side == 'from' else 1].append(int(word_id))
        self.rendered = collections.defaultdict(set)
        self.together = collections.defaultdict(set)
        self.linked = set()
        for eng, heb in links.values():
            self.linked.update(eng)
            for h in heb:
                self.rendered[h].update(eng)
                self.together[h].update(heb)
        self.by_verse = collections.defaultdict(list)
        for eid, (verse, position, _) in self.english.items():
            self.by_verse[verse].append((position, eid))
        for verse in self.by_verse.values():
            verse.sort()
        self.refs = {int(v): f'{b} {c}:{n}' for v, b, c, n in rows(data / f'{slug}-verses.tsv')}

    def phrase(self, ids):
        ordered = sorted(self.english[i][1:] for i in ids if i in self.english)
        out, last = [], None
        for position, surface in ordered:
            if last is not None and position > last + 1:
                out.append('…')
            out.append(surface)
            last = position
        return ' '.join(out)

    def verses_of(self, ids):
        return sorted({self.english[e][0] for e in ids if e in self.english})

    def orphans(self, verses):
        """Unlinked English words of these verses, as (id, surface)."""
        return [(e, self.english[e][2]) for v in verses for _, e in self.by_verse[v] if e not in self.linked]


# ------------------------------------------------------------------------------------------- glosses

def stem(word):
    word = IRREGULAR.get(word, word)
    for suffix, keep in (('ies', 'y'), ('ied', 'y'), ('ness', ''), ('ings', ''), ('ing', ''), ('edly', ''),
                         ('ed', ''), ('ly', ''), ('es', ''), ('s', ''), ('er', ''), ('e', '')):
        if word.endswith(suffix) and len(word) - len(suffix) >= 3:
            word = word[:-len(suffix)] + keep
            break
    if len(word) > 3 and word[-1] == word[-2]:
        word = word[:-1]
    return word


def words_of(text):
    return WORD.findall((text or '').lower().replace('’', "'"))


def key_words(text):
    return [w for w in words_of(text) if w not in STOP and not w.endswith("'s")] or \
           [w.removesuffix("'s") for w in words_of(text) if w.endswith("'s")]


def strong_key(raw):
    digits = re.sub(r'\D', '', raw or '')
    return f'H{int(digits)}' if digits else None


def load_glosses(data, repo):
    glosses = collections.defaultdict(set)
    meanings = {}
    for number, definition, kjv in rows(data / 'strong.tsv'):
        meanings[number] = (definition, kjv)
        for w in words_of(definition) + words_of(re.sub(r'\[[^\]]*\]', ' ', kjv)):
            if w not in STOP:
                glosses[number].add(stem(w))
    macula = Path(repo).resolve() / 'Resources' / 'MaculaHebrew' / 'tsv' / 'macula-hebrew.tsv'
    if not macula.is_file():
        macula = HERE.parent.parent.parent / 'Resources' / 'MaculaHebrew' / 'tsv' / 'macula-hebrew.tsv'
    macula_gloss = collections.defaultdict(collections.Counter)
    with open(macula, encoding='utf-8') as handle:
        reader = csv.DictReader(handle, delimiter='\t', quoting=csv.QUOTE_NONE)
        for row in reader:
            number = strong_key(row['strongnumberx'])
            if not number:
                continue
            for field in ('gloss', 'english'):
                for w in words_of(row[field]):
                    glosses[number].add(stem(w))
            if row['gloss']:
                macula_gloss[number][row['gloss']] += 1
    return glosses, meanings, macula_gloss


class Judge:
    """Does a list of English words match a Hebrew word's meaning? Memoized per (words, word)."""

    def __init__(self, glosses):
        self.glosses = glosses

    def stems(self, strong, gloss):
        return frozenset(self.glosses.get(strong, set())) | {stem(w) for w in words_of(gloss)}

    def __call__(self, words, w):
        return self._accepted(tuple(words), w['strong'], w['gloss'], w['pos'])

    @functools.lru_cache(maxsize=None)
    def _accepted(self, words, strong, gloss, pos):
        if not words:
            return False
        if any(x.isdigit() for x in words) and set(words_of(gloss)) & NUMBERS:
            return True
        if pos == 'nmpr':
            target = (gloss or '').lower()
            if any(difflib.SequenceMatcher(None, x, target).ratio() >= 0.6 for x in words):
                return True
            return self._matches(words, frozenset(self.glosses.get(strong, set())))
        return self._matches(words, self.stems(strong, gloss))

    @staticmethod
    def _matches(words, gloss_stems):
        for x in words:
            s = stem(x)
            if s in gloss_stems:
                return True
            for g in gloss_stems:
                short, long = sorted((s, g), key=len)
                if len(short) >= 4 and long.startswith(short) and len(long) - len(short) <= 3:
                    return True
        return False


# ------------------------------------------------------------------------------------------- rules

def own_waw(w, by_verse):
    """The וְ that prefixes this word: walk back over prefix words (article, prepositions, conjunctions)."""
    position = w['pos_n'] - 1
    while True:
        n = by_verse[w['verse']].get(position)
        if n is None or n['pos'] not in FUNCTION_POS:
            return None
        if n['lemma'] == WAW:
            return n['id']
        position -= 1


def same_lemma_beside(w, by_verse):
    return any((n := by_verse[w['verse']].get(w['pos_n'] + d)) and n['lemma'] == w['lemma'] for d in (-2, -1, 1, 2))


def rule_for(w, raw_words, together, by_verse):
    """The exact function-word rule a rendering satisfies at one occurrence, or None."""
    if not raw_words:
        return None
    words = set(raw_words)
    content = words - FILLER
    pos = w['pos']
    if pos in ('subs', 'nmpr') and content and content <= PRONOUNS:
        return 'pronoun-for-noun'
    if pos in ('prps', 'prde', 'prin') and content and content <= PRONOUNS | QUESTION | (COPULA if pos == 'prps' else set()):
        return 'pronoun'
    if words <= CONJUNCTIONS and (waw := own_waw(w, by_verse)) and waw in together:
        return 'own-waw'
    if pos == 'inrg' and words <= AUXILIARIES | NEGATIVES | QUESTION | CONJUNCTIONS | PRONOUNS:
        return 'interrogative'
    if pos == 'nega' and content and content <= NEGATIVES | AUXILIARIES:
        return 'negative'
    if w['strong'] == 'H1961' and words <= HAYAH_MARKERS | COPULA:
        return 'discourse-hayah'
    if pos == 'verb' and content and content <= INTENSIFIERS | AUXILIARIES and same_lemma_beside(w, by_verse):
        return 'infinitive-absolute'
    return None


# ------------------------------------------------------------------------------------------- the pass

def run_pass(args):
    work = work_dir(args)
    data = work / 'data'
    run = args.run or args.pair
    glosses, meanings, macula_gloss = load_glosses(data, args.repo)
    judge = Judge(glosses)
    hebrew, by_verse, refs, book_of = load_bhsa(data)
    books = set(args.books.split(',')) if args.books else None
    pair = Pair(data, args.pair)
    other = Pair(data, args.other) if args.other else None

    def in_scope(w):
        return w is not None and (not books or book_of[w['verse']] in books)

    occ = {}
    function_words = 0
    for hid, eng_ids in pair.rendered.items():
        w = hebrew.get(hid)
        if not in_scope(w):
            continue
        if w['pos'] in FUNCTION_POS:
            function_words += 1
            continue
        raw = pair.phrase(eng_ids)
        words = key_words(raw)
        key = ' '.join(words) or ' '.join(words_of(raw))
        o = {'raw': raw, 'words': key.split(), 'key': key, 'ok': judge(key.split(), w), 'rule': None, 'orphans': []}
        if not o['ok']:
            o['rule'] = rule_for(w, words_of(raw), pair.together[hid], by_verse)
            if not o['rule'] and any(judge(o['words'], hebrew[h]) for h in pair.together[hid]
                                     if h != hid and h in hebrew and hebrew[h]['pos'] not in FUNCTION_POS):
                o['rule'] = 'shared-link'
            o['orphans'] = [s for _, s in pair.orphans(pair.verses_of(eng_ids)) if judge(key_words(s), w)]
            if o['orphans']:
                o['rule'] = None
        occ[hid] = o

    def fine(hid):
        o = occ.get(hid)
        return o is not None and (o['ok'] or o['rule'])

    groups = collections.defaultdict(lambda: collections.defaultdict(list))
    for hid, o in occ.items():
        w = hebrew[hid]
        groups[(w['lemma'], w['strong'], w['pos'])][o['key']].append(hid)

    lemmas = []
    totals = collections.Counter()
    for (lemma, strong, pos), renderings in groups.items():
        sample_word = hebrew[next(iter(renderings.values()))[0]]
        items = []
        for key, hids in renderings.items():
            words = key.split()
            count = len(hids)
            os_ = [occ[h] for h in hids]
            gloss_ok = os_[0]['ok']
            rule = None if gloss_ok else (os_[0]['rule'] if all(o['rule'] for o in os_) else None)
            ok = gloss_ok or bool(rule)
            totals['renderings'] += 1
            totals['occurrences'] += count
            raw_forms = collections.Counter(o['raw'] for o in os_)
            item = {'key': key, 'raw': raw_forms.most_common(1)[0][0], 'count': count, 'accepted': ok,
                    'rule': rule, 'refs': [refs[hebrew[h]['verse']] for h in hids[:3]], 'hids': hids[:3],
                    'reasons': []}
            if gloss_ok:
                totals['accepted'] += 1
                totals['accepted_occurrences'] += count
            elif rule:
                totals['accepted_by_rule'] += 1
                totals['accepted_by_rule_occurrences'] += count
                totals[f'rule {rule}'] += count
            else:
                orphaned = [(refs[hebrew[h]['verse']], occ[h]['orphans'][0]) for h in hids if occ[h]['orphans']]
                if orphaned:
                    item['reasons'].append(f'orphan {len(orphaned)}/{count}: ' +
                                           '; '.join(f'{r} «{s}»' for r, s in orphaned[:3]))
                if count <= 2:
                    item['reasons'].append('rare')
                shifted = []
                content = [x for x in words if x not in STOP and not x.isdigit()]
                for hid in (hids if content else ()):
                    me = hebrew[hid]
                    for d in (-2, -1, 1, 2):
                        n = by_verse[me['verse']].get(me['pos_n'] + d)
                        if n and n['pos'] not in FUNCTION_POS and n['lemma'] != me['lemma'] and judge(content, n):
                            shifted.append(f"{refs[me['verse']]} ({d:+d} {n['gloss']})")
                            break
                if shifted:
                    item['reasons'].append(f'shift {len(shifted)}/{count}: ' + '; '.join(shifted[:3]))
                if other:
                    differ = 0
                    for hid in hids:
                        o_words = key_words(other.phrase(other.rendered.get(hid, ())))
                        if o_words and judge(o_words, sample_word) and \
                                not ({stem(x) for x in o_words} & {stem(x) for x in words}):
                            differ += 1
                    if differ:
                        item['reasons'].append(f'{args.other} differs {differ}/{count}')
                if not item['reasons']:
                    item['reasons'].append('unlike gloss')
                totals['suspected'] += 1
                totals['suspected_occurrences'] += count
            items.append(item)
        items.sort(key=lambda i: -i['count'])
        definition, kjv = meanings.get(strong, ('', ''))
        lemmas.append({'lemma': lemma, 'strong': strong, 'pos': pos, 'gloss': sample_word['gloss'],
                       'definition': definition, 'kjv': kjv,
                       'macula': [g for g, _ in macula_gloss.get(strong, collections.Counter()).most_common(5)],
                       'renderings': items})
    lemmas.sort(key=lambda e: -sum(i['count'] for i in e['renderings']))

    def describe(hid):
        w = hebrew[hid]
        o = occ.get(hid)
        return {'hid': hid, 'ref': refs[w['verse']], 'lemma': w['lemma'], 'strong': w['strong'], 'gloss': w['gloss'],
                'pos': w['pos'], 'rendering': o['raw'] if o else None}

    crossings, seen = [], set()
    for hid, o in occ.items():
        if fine(hid):
            continue
        a = hebrew[hid]
        for d in range(-WINDOW, WINDOW + 1):
            b = by_verse[a['verse']].get(a['pos_n'] + d)
            if not d or b is None or b['id'] not in occ or b['lemma'] == a['lemma'] or b['id'] in pair.together[hid]:
                continue
            if fine(b['id']) or frozenset((hid, b['id'])) in seen:
                continue
            ab = judge(o['words'], b)
            ba = judge(occ[b['id']]['words'], a)
            if ab or ba:
                seen.add(frozenset((hid, b['id'])))
                first, second = (hid, b['id']) if d > 0 else (b['id'], hid)
                crossings.append({'kind': 'crossing' if ab and ba else 'half-crossing',
                                  'a': describe(first), 'b': describe(second)})

    missing = []
    for hid, w in hebrew.items():
        if w['pos'] in FUNCTION_POS or w['pos'] == 'prps' or hid in pair.rendered or not in_scope(w):
            continue
        hit = None
        neighbours = [by_verse[w['verse']].get(w['pos_n'] + d) for d in range(-WINDOW, WINDOW + 1) if d]
        neighbours = [n for n in neighbours if n is not None and n['id'] in pair.rendered]
        for n in neighbours:
            if n['id'] not in occ:
                continue
            for x in occ[n['id']]['words']:
                if judge([x], w) and not judge([x], n):
                    hit = {'kind': 'carried', 'by': describe(n['id']), 'word': x}
                    break
            if hit:
                break
        if not hit and neighbours:
            verses = pair.verses_of(set().union(*(pair.rendered[n['id']] for n in neighbours)))
            for _, s in pair.orphans(verses):
                if judge(key_words(s), w):
                    hit = {'kind': 'orphan', 'word': s}
                    break
        if hit:
            hit['word_missing'] = describe(hid)
            missing.append(hit)

    totals['lemmas'] = len(lemmas)
    totals['lemmas_suspected'] = sum(1 for e in lemmas if any(not i['accepted'] for i in e['renderings']))
    totals['function_words_skipped'] = function_words
    totals['crossings'] = sum(1 for c in crossings if c['kind'] == 'crossing')
    totals['half_crossings'] = sum(1 for c in crossings if c['kind'] == 'half-crossing')
    totals['missing_carried'] = sum(1 for m in missing if m['kind'] == 'carried')
    totals['missing_orphan'] = sum(1 for m in missing if m['kind'] == 'orphan')
    (work / f'{run}-pass.json').write_text(json.dumps({'totals': totals, 'lemmas': lemmas, 'crossings': crossings,
                                                       'missing': missing}, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(totals, indent=1))


# ------------------------------------------------------------------------------------------- Haiku

SYSTEM = """You check how an English Bible translation renders one Biblical Hebrew word. You do not see the verses: you judge each English rendering against what the Hebrew word can mean.

You get the Hebrew lemma with its Strong number and part of speech, its gloss and other glosses, Strong's definition (the range of its senses), how the King James Version renders it, the renderings of this translation already accepted (with how often each occurs), and a numbered list of renderings to judge.

The translation is free. It renders words by idioms, paraphrases and pronouns, and a rendering often carries small English words that belong to Hebrew prefixes or to neighbouring words. Your task is to tell a free rendering of THIS word from English that renders a DIFFERENT word, because the link was put on the wrong Hebrew word.

Fine, score 1 (0.75 when the path from the word to the English is loose):
- same-sense: the same meaning or a synonym, in any form: singular or plural, with or without a possessive, any tense, person, voice or participle of a verb, a noun for a verb of the same idea or the reverse (judge, judgment; live, life; bless, blessing).
- valid-other-sense: another sense within the word's range, even a rare or figurative one (hand: power, side, share; house: family, household; soul: life, person, appetite; word: thing, matter).
- idiom-or-paraphrase: an idiom or paraphrase with the same idea (sons of Heth: the Hittites; went in to her: slept with her; lifted up his eyes: looked up; answered and said: replied; with all his heart: wholeheartedly).
- pronoun-or-reference: a pronoun, a title or another name for the same person or thing (Jacob: he; Pharaoh: the king; the man: him; my lord: you).
- function-word: only small words, when this word's content is a grammatical marker that English expresses so (a repeated verb rendered surely; a word of existence rendered there is; an interrogative particle rendered is, will, do; a discourse marker rendered now, then).

Errors, score 0 (0.25 when you are unsure):
- unrelated-word: the English means something this word never means: a noun rendered by an unrelated noun or verb, a common noun by an unrelated name or the reverse, a verb by an unrelated action. Such English usually renders another word of the verse.
- wrong-part-of-speech: the English cannot render this kind of word at all: a noun or verb rendered only by and, of, the, to or a preposition, or an adjective rendered by an unrelated verb.

Unsure: score 0.5 only when the verse would be needed to decide, with the category you find likelier.

The category decides the score: same-sense, valid-other-sense, idiom-or-paraphrase, pronoun-or-reference and function-word score 0.75 or 1; unrelated-word and wrong-part-of-speech score 0 or 0.25. Choose the category first.

How to judge:
- Judge the main English word. Small words beside it (and, then, the, a, of, to, in, with, a pronoun) never lower the score.
- An ellipsis (…) marks a gap: the English words on both sides are linked to this word.
- Score high whenever a plausible path from the word to the English exists; keep 0 for English that no sense of the word can produce.
- The accepted renderings show what this translation does with the word; a rendering like them is fine.

Answer with the JSON only: for each numbered rendering its number n, its category c, its score s, and, only when s is below 0.5, a reason r of at most twelve words naming what the English renders instead."""

CATEGORIES = ['same-sense', 'valid-other-sense', 'idiom-or-paraphrase', 'pronoun-or-reference', 'function-word',
              'unrelated-word', 'wrong-part-of-speech']
SCHEMA = {'type': 'object', 'additionalProperties': False, 'required': ['scores'],
          'properties': {'scores': {'type': 'array', 'items': {
              'type': 'object', 'additionalProperties': False, 'required': ['n', 'c', 's'],
              'properties': {'n': {'type': 'integer'}, 'c': {'type': 'string', 'enum': CATEGORIES},
                             's': {'type': 'number'}, 'r': {'type': 'string'}}}}}}
CHUNK = 120
ACCEPTED_SHOWN = 60


def client_module(repo):
    path = Path(repo).resolve() / 'scripts' / 'claude_api.py'
    spec = importlib.util.spec_from_file_location('claude_api', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prompts(entry):
    good = [i for i in entry['renderings'] if i['accepted']]
    todo = [i for i in entry['renderings'] if not i['accepted']]
    head = [f"Hebrew: {entry['lemma']} ({entry['strong']}), {POS_NAMES.get(entry['pos'], entry['pos'])}",
            f"Gloss: {entry['gloss']}"]
    if entry['macula']:
        head.append('Other glosses: ' + ', '.join(entry['macula']))
    if entry['definition']:
        head.append(f"Strong's definition: {entry['definition']}")
    if entry['kjv']:
        head.append(f"KJV renders it: {entry['kjv'][:600]}")
    shown = '; '.join(f"{i['raw']} ({i['count']})" for i in good[:ACCEPTED_SHOWN])
    more = f'; and {len(good) - ACCEPTED_SHOWN} rarer ones' if len(good) > ACCEPTED_SHOWN else ''
    head.append('Accepted renderings in this translation: ' + (shown + more if good else 'none'))
    for start in range(0, len(todo), CHUNK):
        part = todo[start:start + CHUNK]
        lines = head + ['Judge these renderings:'] + [f'{n}. {i["raw"]} ({i["count"]})' for n, i in enumerate(part, 1)]
        yield start, part, '\n'.join(lines)


def request_params(api, text, size):
    params = api.build_params(text, system=SYSTEM, model=MODEL, effort='low', schema=SCHEMA,
                              max_tokens=300 + 60 * size)
    params['thinking'] = {'type': 'disabled'}
    return params


def load_json(path):
    return json.loads(path.read_text(encoding='utf-8')) if path.is_file() else {}


def build_requests(args, only=None):
    api = client_module(args.repo)
    passed = load_json(work_dir(args) / f'{args.run}-pass.json')
    done = load_json(work_dir(args) / f'{args.run}-{args.scores}.json')
    requests = []
    for index, entry in enumerate(passed['lemmas']):
        for start, part, text in prompts(entry):
            custom = f"{args.run}-{index}-{start}"
            if custom in done or (only is not None and custom not in only):
                continue
            requests.append((custom, request_params(api, text, len(part))))
    return api, requests


def estimate(args):
    api, requests = build_requests(args)
    if args.limit:
        requests = requests[:args.limit]
    plan = api.batch_plan(requests)
    tokens = sum(api.estimate_tokens(json.dumps(p['messages'] + p['system'], ensure_ascii=False)) for _, p in requests)
    print(f"{len(requests)} requests, ~{tokens:,} input tokens (high estimate), "
          f"reserve ${plan['reserve_usd']:.2f}, {len(plan['cached_prefixes'])} cached prefix(es); "
          f"spent so far under {LABEL}: ${api.spent(LABEL):.2f} of ${CAP_USD:.2f}")


def parse_answer(text):
    out = {}
    for e in json.loads(text)['scores']:
        out[str(e['n'])] = {'s': e['s'], 'c': e.get('c'), 'r': e.get('r')}
    return out


def send(api, requests, path):
    answers, failures = api.run_batch(requests, label=LABEL, cap_usd=CAP_USD, poll=30)
    scores = load_json(path)
    for custom, text in answers.items():
        try:
            scores[custom] = parse_answer(text)
        except (ValueError, KeyError, TypeError):
            failures[custom] = 'unreadable answer'
    path.write_text(json.dumps(scores, ensure_ascii=False), encoding='utf-8')
    print(f'{len(answers)} answered, {len(failures)} failed: {list(failures.items())[:5]}')


def score(args):
    api, requests = build_requests(args)
    if args.limit:
        requests = requests[:args.limit]
    if not requests:
        print('nothing left to score')
        return
    send(api, requests, work_dir(args) / f'{args.run}-{args.scores}.json')


def score_of(got, n):
    """A stored answer: a bare number (the first prompt) or {s, c, r}."""
    value = got.get(str(n))
    if value is None:
        return None, None, None
    if isinstance(value, dict):
        return value['s'], value.get('c'), value.get('r')
    return value, None, None


# ------------------------------------------------------------------------------------------- report

def scored_items(work, run, scores_name='scores'):
    passed = load_json(work / f'{run}-pass.json')
    scores = load_json(work / f'{run}-{scores_name}.json')
    for index, entry in enumerate(passed['lemmas']):
        for start, part, _ in prompts(entry):
            got = scores.get(f'{run}-{index}-{start}', {})
            for n, item in enumerate(part, 1):
                yield entry, item, score_of(got, n)


def report(args):
    work = work_dir(args)
    passed = load_json(work / f'{args.run}-pass.json')
    flagged = list(scored_items(work, args.run, args.scores))
    with open(work / f'{args.run}-flagged.tsv', 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle, delimiter='\t', lineterminator='\n')
        out.writerow(['lemma', 'strong', 'pos', 'gloss', 'rendering', 'key', 'count', 'refs', 'reasons', 'haiku',
                      'category', 'reason'])
        for entry, item, (s, c, r) in sorted(flagged, key=lambda f: (f[2][0] is None, f[2][0] if f[2][0] is not None else 9,
                                                                    -f[1]['count'])):
            out.writerow([entry['lemma'], entry['strong'], entry['pos'], entry['gloss'], item['raw'], item['key'],
                          item['count'], '; '.join(item['refs']), ' | '.join(item['reasons']),
                          '' if s is None else s, c or '', r or ''])
    with open(work / f'{args.run}-crossings.tsv', 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle, delimiter='\t', lineterminator='\n')
        out.writerow(['kind', 'ref', 'a_lemma', 'a_gloss', 'a_rendering', 'b_lemma', 'b_gloss', 'b_rendering'])
        for c in sorted(passed['crossings'], key=lambda c: (c['kind'] != 'crossing', c['a']['hid'])):
            out.writerow([c['kind'], c['a']['ref'], c['a']['lemma'], c['a']['gloss'], c['a']['rendering'],
                          c['b']['lemma'], c['b']['gloss'], c['b']['rendering']])
    with open(work / f'{args.run}-missing.tsv', 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle, delimiter='\t', lineterminator='\n')
        out.writerow(['kind', 'ref', 'lemma', 'gloss', 'pos', 'english', 'carried_by', 'carrier_rendering'])
        for m in passed['missing']:
            w = m['word_missing']
            out.writerow([m['kind'], w['ref'], w['lemma'], w['gloss'], w['pos'], m['word'],
                          (m.get('by') or {}).get('lemma', ''), (m.get('by') or {}).get('rendering', '')])
    scored = [f[2] for f in flagged if f[2][0] is not None]
    bands = collections.Counter('0' if s < 0.25 else '0.25-0.5' if s < 0.5 else '0.5-0.75' if s < 0.75 else '0.75-1'
                                for s, _, _ in scored)
    summary = dict(passed['totals'])
    summary.update({'flagged': len(flagged), 'scored': len(scored), 'score_bands': dict(sorted(bands.items())),
                    'categories': dict(collections.Counter(c for _, c, _ in scored if c).most_common()),
                    'below_half': sum(1 for s, _, _ in scored if s < 0.5),
                    'below_half_occurrences': sum(f[1]['count'] for f in flagged if f[2][0] is not None and f[2][0] < 0.5)})
    (work / f'{args.run}-summary.json').write_text(json.dumps(summary, indent=1, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(summary, indent=1, ensure_ascii=False))


# ------------------------------------------------------------------------------------------- samples

def show_verse(hebrew_by_verse, together, focus, verse, glosses_of):
    return ' '.join((f'[[{glosses_of[w]}]]' if w in focus else f'({glosses_of[w]})' if w in together else glosses_of[w])
                    for _, w in sorted(hebrew_by_verse[verse]))


def sample(args):
    """Items for a hand check, each with its verse on both sides: lowest-scored, crossings or missing words."""
    work = work_dir(args)
    data = work / 'data'
    passed = load_json(work / f'{args.run}-pass.json')
    hebrew, by_verse, refs, book_of = load_bhsa(data)
    pair = Pair(data, args.run.split('-')[0])
    skip = set(args.skip_books.split(',')) if args.skip_books else set()
    h_by_verse = {v: [(p, w['id']) for p, w in ws.items()] for v, ws in by_verse.items()}
    glosses_of = {hid: w['gloss'] for hid, w in hebrew.items()}

    def context(hids):
        verse = hebrew[hids[0]]['verse']
        together = set().union(*(pair.together[h] for h in hids)) - set(hids)
        heb = show_verse(h_by_verse, together, set(hids), verse, glosses_of)
        linked = set().union(*(pair.rendered.get(h, set()) for h in hids))
        e_verses = pair.verses_of(linked) or pair.verses_of(
            set().union(*(pair.rendered.get(n['id'], set()) for n in by_verse[verse].values())))[:1]
        eng = ' / '.join(pair.refs[v] + ' ' + ' '.join((f'[[{pair.english[e][2]}]]' if e in linked else pair.english[e][2])
                                                       for _, e in pair.by_verse[v]) for v in e_verses)
        return f'  {refs[verse]} glosses: {heb}\n  {eng}'

    shown, per_lemma = 0, collections.Counter()
    if args.kind == 'low':
        low = [(s, -item['count'], entry, item, c, r) for entry, item, (s, c, r) in scored_items(work, args.run, args.scores)
               if s is not None and (not args.reason or any(x.startswith(args.reason) for x in item['reasons']))]
        low.sort(key=lambda x: (x[0], x[1]))
        for value, _, entry, item, c, r in low:
            if shown >= args.count or per_lemma[entry['strong']] >= args.per_lemma:
                continue
            hids = [h for h in item['hids'] if book_of[hebrew[h]['verse']] not in skip]
            if not hids:
                continue
            per_lemma[entry['strong']] += 1
            shown += 1
            print(f"#{shown} {entry['lemma']} {entry['strong']} '{entry['gloss']}' -> \"{item['raw']}\" x{item['count']} "
                  f"score {value} {c or ''} {('«' + r + '»') if r else ''} | {' | '.join(item['reasons'])}\n"
                  + context(hids[:1]))
    elif args.kind in ('crossing', 'half-crossing'):
        for c in passed['crossings']:
            if c['kind'] != args.kind or book_of[hebrew[c['a']['hid']]['verse']] in skip or shown >= args.count:
                continue
            shown += 1
            print(f"#{shown} {c['kind']} {c['a']['lemma']} '{c['a']['gloss']}' = \"{c['a']['rendering']}\"  <->  "
                  f"{c['b']['lemma']} '{c['b']['gloss']}' = \"{c['b']['rendering']}\"\n" + context([c['a']['hid'], c['b']['hid']]))
    else:
        for m in passed['missing']:
            w = m['word_missing']
            if m['kind'] != args.kind or book_of[hebrew[w['hid']]['verse']] in skip or shown >= args.count:
                continue
            shown += 1
            by = m.get('by')
            extra = f" carried by {by['lemma']} '{by['gloss']}' = \"{by['rendering']}\"" if by else ''
            print(f"#{shown} missing-{m['kind']} {w['lemma']} '{w['gloss']}' ({w['pos']}) english «{m['word']}»{extra}\n"
                  + context([w['hid']] + ([by['hid']] if by else [])))


# ------------------------------------------------------------------------------------------- verse check

VERSE_SYSTEM = """You check links of a hand-made word alignment between a Biblical Hebrew verse and an English translation of it.

For each numbered item you get:
- the Hebrew word in question: lemma, Strong number, gloss and Strong's definition;
- the Hebrew verse as a word-by-word English gloss, with that word in [[double brackets]] and any other Hebrew words of the same link in (parentheses);
- the English verse, with the English words linked to that Hebrew word in [[double brackets]].

You do NOT judge the translation. A translator may read a word differently from the gloss (a place name for a common noun, a rare sense, an emendation, a free or even a poor rendering): that is the translation's choice, and its link is still right. The only question is which Hebrew word the bracketed English stands for in this verse.

- right: the bracketed English is this translation's rendering of the bracketed Hebrew word, however free, by an idiom or paraphrase, as a pronoun or title, or together with the words in parentheses. Translations reorder sentences; the gloss line is only a crib, so do not judge by word order.
- wrong: the bracketed English plainly stands for ANOTHER Hebrew word of this verse, which you can point to in the gloss line (for example the English says "cords" where the verse's word for cords is another glossed word), or another English word of the verse plainly stands for this Hebrew word. You must name that other word in w, exactly as it appears in the gloss line or the English verse.
- unsure: when you suspect a mislink but cannot point to the other word.

Small words beside the main English word (and, then, the, a, of, to, in, with, a pronoun) never make a link wrong: they often belong to Hebrew prefixes. Judge the main word.

Answer with the JSON only: for each item its number n, its verdict v; when v is wrong, w, the other word; when v is not right, a reason r of at most twenty words."""

VERDICTS = ['right', 'wrong', 'unsure']
VERSE_SCHEMA = {'type': 'object', 'additionalProperties': False, 'required': ['items'],
                'properties': {'items': {'type': 'array', 'items': {
                    'type': 'object', 'additionalProperties': False, 'required': ['n', 'v'],
                    'properties': {'n': {'type': 'integer'}, 'v': {'type': 'string', 'enum': VERDICTS},
                                   'w': {'type': 'string'}, 'r': {'type': 'string'}}}}}}
VERSE_CHUNK = 12
# Stage 2 reads the verse at the effort of stage 1; stage 3 thinks, at a high effort, over what stage 2 kept.
STAGES = {2: {'effort': 'low', 'thinking': False}, 3: {'effort': 'high', 'thinking': True}}


class VerseContext:
    """Both verses of a link, as the verse check and the samples show them."""

    def __init__(self, work, run):
        data = work / 'data'
        self.hebrew, self.by_verse, self.refs, self.book_of = load_bhsa(data)
        self.pair = Pair(data, run.split('-')[0])
        self.h_by_verse = {v: [(p, w['id']) for p, w in ws.items()] for v, ws in self.by_verse.items()}
        self.glosses_of = {hid: w['gloss'] for hid, w in self.hebrew.items()}

    def __call__(self, hids):
        pair, hebrew = self.pair, self.hebrew
        verse = hebrew[hids[0]]['verse']
        together = set().union(*(pair.together[h] for h in hids)) - set(hids)
        heb = show_verse(self.h_by_verse, together, set(hids), verse, self.glosses_of)
        linked = set().union(*(pair.rendered.get(h, set()) for h in hids))
        e_verses = pair.verses_of(linked) or pair.verses_of(
            set().union(*(pair.rendered.get(n['id'], set()) for n in self.by_verse[verse].values())))[:1]
        eng = ' / '.join(pair.refs[v] + ' ' + ' '.join((f'[[{pair.english[e][2]}]]' if e in linked else pair.english[e][2])
                                                       for _, e in pair.by_verse[v]) for v in e_verses)
        return self.refs[verse], heb, eng


def verse_items(args, work):
    """The occurrences a stage judges: stage 2 every occurrence of a rendering stage 1 scored below 0.5,
    stage 3 the occurrences stage 2 did not call right."""
    if args.stage == 2:
        for entry, item, (s, c, r) in scored_items(work, args.run, args.scores):
            if s is not None and s < 0.5:
                for hid in item['hids']:
                    yield entry, item, hid, {'s': s, 'c': c, 'r': r}
    else:
        before = load_json(work / f'{args.run}-v2.json')
        for entry, item, hid, prior in verse_items(argparse.Namespace(**{**vars(args), 'stage': 2}), work):
            got = before.get(str(hid))
            if got is not None and got['v'] != 'right':
                yield entry, item, hid, {**prior, 'v2': got['v'], 'r2': got.get('r')}


def verse_requests(args):
    work = work_dir(args)
    api = client_module(args.repo)
    context = VerseContext(work, args.run)
    done = load_json(work / f'{args.run}-v{args.stage}.json')
    by_lemma = collections.defaultdict(list)
    for entry, item, hid, prior in verse_items(args, work):
        if str(hid) not in done:
            by_lemma[(entry['strong'], entry['lemma'])].append((entry, item, hid))
    stage = STAGES[args.stage]
    requests, index = [], {}
    for (strong, lemma), occurrences in sorted(by_lemma.items(), key=lambda kv: (str(kv[0][0]), kv[0][1])):
        for start in range(0, len(occurrences), VERSE_CHUNK):
            part = occurrences[start:start + VERSE_CHUNK]
            entry = part[0][0]
            lines = [f"Hebrew word: {lemma} ({strong}), {POS_NAMES.get(entry['pos'], entry['pos'])}; gloss: {entry['gloss']}"]
            if entry['definition']:
                lines.append(f"Strong's definition: {entry['definition'][:500]}")
            for n, (_, item, hid) in enumerate(part, 1):
                ref, heb, eng = context([hid])
                lines += [f'{n}. {ref}', f'   Hebrew glosses: {heb}', f'   English: {eng}']
            custom = f"{args.run}-v{args.stage}-{len(requests)}"
            params = api.build_params('\n'.join(lines), system=VERSE_SYSTEM, model=MODEL, effort=stage['effort'],
                                      schema=VERSE_SCHEMA, max_tokens=(4000 if stage['thinking'] else 0) + 200 + 60 * len(part))
            params['thinking'] = {'type': 'adaptive'} if stage['thinking'] else {'type': 'disabled'}
            requests.append((custom, params))
            index[custom] = [hid for _, _, hid in part]
    return api, requests, index


def verse_score(args):
    api, requests, index = verse_requests(args)
    if args.limit:
        requests = requests[:args.limit]
    if args.estimate or not requests:
        plan = api.batch_plan(requests) if requests else {'reserve_usd': 0}
        print(f"stage {args.stage}: {len(requests)} requests over {sum(len(index[c]) for c, _ in requests)} occurrences, "
              f"reserve ${plan['reserve_usd']:.2f}; spent so far under {LABEL}: ${api.spent(LABEL):.2f} of ${CAP_USD:.2f}")
        return
    answers, failures = api.run_batch(requests, label=LABEL, cap_usd=CAP_USD, poll=30)
    path = work_dir(args) / f'{args.run}-v{args.stage}.json'
    verdicts = load_json(path)
    for custom, text in answers.items():
        try:
            for e in json.loads(text)['items']:
                v = e['v'] if e['v'] != 'wrong' or e.get('w') else 'unsure'
                verdicts[str(index[custom][e['n'] - 1])] = {'v': v, 'w': e.get('w'), 'r': e.get('r')}
        except (ValueError, KeyError, TypeError, IndexError):
            failures[custom] = 'unreadable answer'
    path.write_text(json.dumps(verdicts, ensure_ascii=False), encoding='utf-8')
    print(f'{len(answers)} answered, {len(failures)} failed: {list(failures.items())[:5]}')


def verse_report(args):
    """The occurrences a stage judged, with both verses, and what is left for a person after it."""
    work = work_dir(args)
    context = VerseContext(work, args.run)
    verdicts = load_json(work / f'{args.run}-v{args.stage}.json')
    counts = collections.Counter()
    with open(work / f'{args.run}-v{args.stage}.tsv', 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle, delimiter='\t', lineterminator='\n')
        out.writerow(['verdict', 'ref', 'lemma', 'strong', 'gloss', 'rendering', 'stage1', 'reason', 'hebrew', 'english'])
        for entry, item, hid, prior in verse_items(args, work):
            got = verdicts.get(str(hid))
            v = got['v'] if got else 'unjudged'
            counts[v] += 1
            ref, heb, eng = context([hid])
            out.writerow([v, ref, entry['lemma'], entry['strong'], entry['gloss'], item['raw'],
                          f"{prior['s']} {prior['c'] or ''}", ((got or {}).get('w') or '') + ' | ' + ((got or {}).get('r') or ''), heb, eng])
    print(f"{args.run} stage {args.stage}: {dict(counts.most_common())}; left for a person: "
          f"{counts['wrong'] + counts['unsure'] + counts['unjudged']}")


# ------------------------------------------------------------------------------------------- evaluation

def key_items(args):
    """The hand-judged items of the key, located in their run's pass: (run, custom id, n, verdict)."""
    key = json.loads(Path(args.key).read_text(encoding='utf-8'))
    work = work_dir(args)
    found = []
    for run in args.runs.split(','):
        passed = load_json(work / f'{run}-pass.json')
        where = {}
        for index, entry in enumerate(passed['lemmas']):
            for start, part, _ in prompts(entry):
                for n, item in enumerate(part, 1):
                    where[(entry['strong'], entry['lemma'], item['raw'])] = (f'{run}-{index}-{start}', n, item)
        for k in key:
            if k['run'] != run:
                continue
            spot = where.get((k['strong'], k['lemma'], k['raw']))
            if spot is None:
                raise SystemExit(f'key item not found in {run}: {k}')
            found.append((run, spot[0], spot[1], k))
    return found


def abscore(args):
    """Score, with the current prompt, every chunk that holds a key item (A/B against stored scores)."""
    items = key_items(args)
    api = client_module(args.repo)
    requests = []
    for run in sorted({r for r, *_ in items}):
        args.run = run
        _, reqs = build_requests(args, only={c for r, c, _, _ in items if r == run})
        requests += reqs
    print(f'{len(requests)} requests for {len(items)} key items')
    if requests:
        answers, failures = api.run_batch(requests, label=LABEL, cap_usd=CAP_USD, poll=30)
        for run in sorted({r for r, *_ in items}):
            path = work_dir(args) / f'{run}-{args.scores}.json'
            scores = load_json(path)
            for custom, text in answers.items():
                if custom.startswith(run + '-'):
                    try:
                        scores[custom] = parse_answer(text)
                    except (ValueError, KeyError, TypeError):
                        failures[custom] = 'unreadable answer'
            path.write_text(json.dumps(scores, ensure_ascii=False), encoding='utf-8')
        print(f'{len(answers)} answered, {len(failures)} failed: {list(failures.items())[:5]}')


def evaluate(args):
    """Precision and recall on real errors, for a stored scores file, against the hand key."""
    items = key_items(args)
    work = work_dir(args)
    cache = {}
    results = []
    for run, custom, n, k in items:
        if run not in cache:
            cache[run] = load_json(work / f'{run}-{args.scores}.json')
        s, c, r = score_of(cache[run].get(custom, {}), n)
        results.append((k, s, c, r))
    missing = [k for k, s, _, _ in results if s is None]
    if missing:
        print(f'{len(missing)} key items have no score in {args.scores}')
    for strict in (True, False):
        for name, flagged in (('score < 0.5', lambda s, c: s < 0.5),
                              ('score <= 0.5', lambda s, c: s <= 0.5),
                              ('error category', lambda s, c: c in ('unrelated-word', 'wrong-part-of-speech'))):
            for scope in ('all', 'BSB'):
                tp = fp = fn = tn = 0
                for k, s, c, _ in results:
                    if s is None or (scope == 'BSB' and not k['run'].startswith('BSB')):
                        continue
                    real = k['verdict'] == 'error' or (not strict and k['verdict'] == 'debatable')
                    hit = flagged(s, c)
                    tp += real and hit
                    fp += (not real) and hit
                    fn += real and not hit
                    tn += (not real) and not hit
                if name == 'error category' and all(c is None for _, _, c, _ in results):
                    continue
                p = tp / (tp + fp) if tp + fp else 0
                rc = tp / (tp + fn) if tp + fn else 0
                print(f"{'strict' if strict else 'lenient'} {scope:3} {name:15} tp {tp:3} fp {fp:3} fn {fn:3} tn {tn:3}  "
                      f"precision {p:.2f} recall {rc:.2f}")
    if args.verbose:
        for k, s, c, r in results:
            print(f"{k['verdict']:9} {s} {c or ''} {k['run']} {k['lemma']} \"{k['raw']}\" {r or ''}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo', default=str(HERE.parent), help='the checkout holding .env, the ledger and Resources')
    sub = parser.add_subparsers(dest='command', required=True)
    p = sub.add_parser('export'); p.add_argument('pairs', nargs='+'); p.set_defaults(go=export)
    p = sub.add_parser('pass'); p.add_argument('pair'); p.add_argument('--other'); p.add_argument('--run')
    p.add_argument('--books', help='comma-separated BHSA book abbreviations, e.g. Gen'); p.set_defaults(go=run_pass)
    for name, go in (('estimate', estimate), ('score', score)):
        p = sub.add_parser(name); p.add_argument('run'); p.add_argument('--limit', type=int)
        p.add_argument('--scores', default='scores'); p.set_defaults(go=go)
    p = sub.add_parser('report'); p.add_argument('run'); p.add_argument('--scores', default='scores')
    p.set_defaults(go=report)
    p = sub.add_parser('sample'); p.add_argument('run'); p.add_argument('--count', type=int, default=40)
    p.add_argument('--kind', default='low', choices=['low', 'crossing', 'half-crossing', 'carried', 'orphan'])
    p.add_argument('--reason', help='only items with a reason starting so, e.g. shift')
    p.add_argument('--skip-books', help='comma-separated BHSA books to leave out, e.g. Gen')
    p.add_argument('--per-lemma', type=int, default=2); p.add_argument('--scores', default='scores')
    p.set_defaults(go=sample)
    p = sub.add_parser('vscore', help='stage 2 or 3: judge each low-scored occurrence in its verse')
    p.add_argument('run'); p.add_argument('--stage', type=int, choices=[2, 3], default=2)
    p.add_argument('--limit', type=int); p.add_argument('--estimate', action='store_true')
    p.add_argument('--scores', default='scores'); p.set_defaults(go=verse_score)
    p = sub.add_parser('vreport'); p.add_argument('run'); p.add_argument('--stage', type=int, choices=[2, 3], default=2)
    p.add_argument('--scores', default='scores'); p.set_defaults(go=verse_report)
    for name, go in (('evaluate', evaluate), ('abscore', abscore)):
        p = sub.add_parser(name); p.add_argument('--key', required=True); p.add_argument('--runs', required=True)
        p.add_argument('--scores', default='scores'); p.add_argument('--verbose', action='store_true')
        p.set_defaults(go=go)
    args = parser.parse_args()
    args.go(args)


if __name__ == '__main__':
    main()
