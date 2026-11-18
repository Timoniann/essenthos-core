"""
Word-by-word check of a hand-made English alignment to BHSA (TSK-1859): a programmatic suspicion
pass, then a context-free Haiku score per suspicious rendering.

    python scripts/handcheck.py export BSB ULT            # live database -> <work>/data (read-only)
    python scripts/handcheck.py pass BSB --other ULT --books Gen   # renderings, acceptance, suspicion reasons
    python scripts/handcheck.py estimate BSB              # requests and upper-bound cost, nothing sent
    python scripts/handcheck.py score BSB [--limit N]     # one batch through claude_api.py, capped
    python scripts/handcheck.py report BSB                # flagged TSV and summary
    python scripts/handcheck.py sample BSB --count 40     # lowest-scored items with their verse, for a hand check

<work> is essenthos-core/.claude-api/handcheck of the checkout given by --repo (git-ignored), so the
ledger, the key and the outputs stay with the main checkout when this runs from a worktree.

For every Hebrew content word (pos not art/conj/prep) the rendering is the English words of every
stated link that holds it, in order. Its key drops articles, possessives, pronouns, auxiliaries,
`and` and prepositions, which in BHSA's tokenisation belong to the prefix words beside it. A
rendering is accepted when a key word matches a gloss word (BHSA gloss, Strong's definition and
KJV renderings, MACULA's gloss and contextual English) by a crude stem; a name when it is close to
the gloss. Anything else is only suspected: rare, carried by a neighbour (the shift shape of
PRB-1851), or disagreeing with another alignment of the same word. English parts of speech are not
checked: no offline tagger is installed.
"""
import argparse
import collections
import csv
import difflib
import importlib.util
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONTAINER = 'essenthos-core-db-1'
READ_ONLY = 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=300000'
MODEL = 'claude-haiku-5-5'
LABEL = 'tsk1859-handcheck'
CAP_USD = 7.0
SLUGS = {'BHSA': 1}
FUNCTION_POS = {'art', 'conj', 'prep'}
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


def matches(words, gloss_stems):
    for w in words:
        s = stem(w)
        if s in gloss_stems:
            return True
        for g in gloss_stems:
            short, long = sorted((s, g), key=len)
            if len(short) >= 4 and long.startswith(short) and len(long) - len(short) <= 3:
                return True
    return False


def name_like(words, gloss):
    target = (gloss or '').lower()
    return any(difflib.SequenceMatcher(None, w, target).ratio() >= 0.6 for w in words)


# ------------------------------------------------------------------------------------------- the pass

def load_pair(data, slug):
    english = {int(r[0]): (int(r[1]), int(r[2]), r[3]) for r in rows(data / f'{slug}-words.tsv')}
    links = collections.defaultdict(lambda: ([], []))
    for link_id, side, word_id in rows(data / f'{slug}-links.tsv'):
        links[int(link_id)][0 if side == 'from' else 1].append(int(word_id))
    rendered = collections.defaultdict(set)
    together = collections.defaultdict(set)
    for eng, heb in links.values():
        for h in heb:
            rendered[h].update(eng)
            together[h].update(heb)
    return english, rendered, together


def phrase(english, ids):
    ordered = sorted(english[i][1:] for i in ids if i in english)
    out, last = [], None
    for position, surface in ordered:
        if last is not None and position > last + 1:
            out.append('…')
        out.append(surface)
        last = position
    return ' '.join(out)


def accepted(words, word, glosses):
    number = word['strong']
    if any(w.isdigit() for w in words) and set(words_of(word['gloss'])) & NUMBERS:
        return True
    if word['pos'] == 'nmpr':
        return name_like(words, word['gloss']) or matches(words, glosses.get(number, set()))
    stems = set(glosses.get(number, set())) | {stem(w) for w in words_of(word['gloss'])}
    return matches(words, stems)


def run_pass(args):
    work = work_dir(args)
    data = work / 'data'
    glosses, meanings, macula_gloss = load_glosses(data, args.repo)
    hebrew = {}
    by_verse = collections.defaultdict(dict)
    for wid, verse, position, surface, lemma, strong, gloss, pos in rows(data / 'bhsa-words.tsv'):
        w = {'id': int(wid), 'verse': int(verse), 'pos_n': int(position), 'surface': surface, 'lemma': lemma,
             'strong': strong, 'gloss': gloss, 'pos': pos}
        hebrew[w['id']] = w
        by_verse[w['verse']][w['pos_n']] = w
    refs = {int(v): f'{b} {c}:{n}' for v, b, c, n in rows(data / 'bhsa-verses.tsv')}
    books = set(args.books.split(',')) if args.books else None
    english, rendered, _ = load_pair(data, args.pair)
    other = load_pair(data, args.other)[:2] if args.other else None

    groups = collections.defaultdict(lambda: collections.defaultdict(list))
    function_words = 0
    for hid, eng_ids in rendered.items():
        w = hebrew.get(hid)
        if w is None or (books and refs[w['verse']].split(' ')[0] not in books):
            continue
        if w['pos'] in FUNCTION_POS:
            function_words += 1
            continue
        raw = phrase(english, eng_ids)
        words = key_words(raw)
        key = ' '.join(words) or ' '.join(words_of(raw))
        groups[(w['lemma'], w['strong'], w['pos'])][key].append((hid, raw))

    lemmas, out = [], []
    totals = collections.Counter()
    for (lemma, strong, pos), renderings in groups.items():
        sample_word = hebrew[next(iter(renderings.values()))[0][0]]
        items = []
        for key, occurrences in renderings.items():
            words = key.split()
            count = len(occurrences)
            ok = accepted(words, sample_word, glosses)
            totals['renderings'] += 1
            totals['occurrences'] += count
            raw_forms = collections.Counter(r for _, r in occurrences)
            item = {'key': key, 'raw': raw_forms.most_common(1)[0][0], 'count': count, 'accepted': ok,
                    'refs': [refs[hebrew[h]['verse']] for h, _ in occurrences[:3]],
                    'hids': [h for h, _ in occurrences[:3]], 'reasons': []}
            if ok:
                totals['accepted'] += 1
                totals['accepted_occurrences'] += count
            else:
                if count <= 2:
                    item['reasons'].append('rare')
                shifted = []
                content = [x for x in words if x not in STOP and not x.isdigit()]
                for hid, _ in (occurrences if content else ()):
                    me = hebrew[hid]
                    for d in (-2, -1, 1, 2):
                        n = by_verse[me['verse']].get(me['pos_n'] + d)
                        if n and n['pos'] not in FUNCTION_POS and n['lemma'] != me['lemma'] \
                                and accepted(content, n, glosses):
                            shifted.append(f"{refs[me['verse']]} ({d:+d} {n['gloss']})")
                            break
                if shifted:
                    item['reasons'].append(f'shift {len(shifted)}/{count}: ' + '; '.join(shifted[:3]))
                if other:
                    o_english, o_rendered = other
                    differ = 0
                    for hid, _ in occurrences:
                        o_words = key_words(phrase(o_english, o_rendered.get(hid, ())))
                        if o_words and accepted(o_words, sample_word, glosses) and \
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
        entry = {'lemma': lemma, 'strong': strong, 'pos': pos, 'gloss': sample_word['gloss'],
                 'definition': definition, 'kjv': kjv,
                 'macula': [g for g, _ in macula_gloss.get(strong, collections.Counter()).most_common(5)],
                 'renderings': items}
        lemmas.append(entry)
    lemmas.sort(key=lambda e: -sum(i['count'] for i in e['renderings']))
    totals['lemmas'] = len(lemmas)
    totals['lemmas_suspected'] = sum(1 for e in lemmas if any(not i['accepted'] for i in e['renderings']))
    totals['function_words_skipped'] = function_words
    (work / f'{args.pair}-pass.json').write_text(json.dumps({'totals': totals, 'lemmas': lemmas},
                                                            ensure_ascii=False), encoding='utf-8')
    print(json.dumps(totals, indent=1))


# ------------------------------------------------------------------------------------------- Haiku

SYSTEM = """You check how an English Bible translation renders one Biblical Hebrew word, without seeing the verses.

You get the Hebrew lemma, its Strong number, its part of speech, its dictionary meanings, some renderings already known to be fine (with how often each occurs), and a numbered list of renderings to score.

Score every numbered rendering from 0 to 1:
- 1: it means this word: the same sense, a good synonym, an inflected or idiomatic form (father, my father, fathers; said, he said, saying).
- 0.5: possible only in a special context: a figurative, idiomatic or very free rendering.
- 0: it does not render this word: another meaning, another word altogether, a name for a common noun or the reverse, or a part of speech that cannot render it.

A rendering may carry small words (and, the, of, to, in, pronouns) that belong to Hebrew prefixes or neighbouring words. Judge the main word only, and do not lower a score for those extra words. Some renderings are a gap phrase written with an ellipsis (…).

Answer with the JSON only: one entry per numbered rendering, n being its number and s its score."""

SCHEMA = {'type': 'object', 'additionalProperties': False, 'required': ['scores'],
          'properties': {'scores': {'type': 'array', 'items': {
              'type': 'object', 'additionalProperties': False, 'required': ['n', 's'],
              'properties': {'n': {'type': 'integer'}, 's': {'type': 'number'}}}}}}
CHUNK = 120


def client_module(repo):
    path = Path(repo).resolve() / 'scripts' / 'claude_api.py'
    spec = importlib.util.spec_from_file_location('claude_api', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prompts(entry):
    good = [i for i in entry['renderings'] if i['accepted']][:25]
    todo = [i for i in entry['renderings'] if not i['accepted']]
    head = [f"Hebrew: {entry['lemma']} ({entry['strong']}), {POS_NAMES.get(entry['pos'], entry['pos'])}",
            f"Gloss: {entry['gloss']}"]
    if entry['macula']:
        head.append('Other glosses: ' + ', '.join(entry['macula']))
    if entry['definition']:
        head.append(f"Strong's: {entry['definition']}")
    if entry['kjv']:
        head.append(f"KJV renders it: {entry['kjv'][:600]}")
    head.append('Known fine: ' + ('; '.join(f"{i['raw']} ({i['count']})" for i in good) if good else 'none'))
    for start in range(0, len(todo), CHUNK):
        part = todo[start:start + CHUNK]
        lines = head + ['Score these:'] + [f'{n}. {i["raw"]} ({i["count"]})' for n, i in enumerate(part, 1)]
        yield start, part, '\n'.join(lines)


def build_requests(args):
    api = client_module(args.repo)
    passed = json.loads((work_dir(args) / f'{args.pair}-pass.json').read_text(encoding='utf-8'))
    done = load_scores(args)
    requests = []
    for index, entry in enumerate(passed['lemmas']):
        for start, part, text in prompts(entry):
            custom = f"{args.pair}-{index}-{start}"
            if custom in done:
                continue
            params = api.build_params(text, system=SYSTEM, model=MODEL, effort='low', schema=SCHEMA,
                                      max_tokens=200 + 25 * len(part))
            params['thinking'] = {'type': 'disabled'}
            requests.append((custom, params))
    return api, requests


def load_scores(args):
    path = work_dir(args) / f'{args.pair}-scores.json'
    return json.loads(path.read_text(encoding='utf-8')) if path.is_file() else {}


def estimate(args):
    api, requests = build_requests(args)
    if args.limit:
        requests = requests[:args.limit]
    plan = api.batch_plan(requests)
    tokens = sum(api.estimate_tokens(json.dumps(p['messages'] + p['system'], ensure_ascii=False)) for _, p in requests)
    print(f"{len(requests)} requests, ~{tokens:,} input tokens (high estimate), "
          f"reserve ${plan['reserve_usd']:.2f}; spent so far under {LABEL}: ${api.spent(LABEL):.2f} of ${CAP_USD:.2f}")


def score(args):
    api, requests = build_requests(args)
    if args.limit:
        requests = requests[:args.limit]
    if not requests:
        print('nothing left to score')
        return
    answers, failures = api.run_batch(requests, label=LABEL, cap_usd=CAP_USD, poll=30)
    scores = load_scores(args)
    for custom, text in answers.items():
        try:
            scores[custom] = {str(e['n']): e['s'] for e in json.loads(text)['scores']}
        except (ValueError, KeyError, TypeError):
            failures[custom] = 'unreadable answer'
    (work_dir(args) / f'{args.pair}-scores.json').write_text(json.dumps(scores), encoding='utf-8')
    print(f'{len(answers)} answered, {len(failures)} failed: {list(failures.items())[:5]}')


# ------------------------------------------------------------------------------------------- report

def report(args):
    work = work_dir(args)
    passed = json.loads((work / f'{args.pair}-pass.json').read_text(encoding='utf-8'))
    scores = load_scores(args)
    flagged = []
    for index, entry in enumerate(passed['lemmas']):
        for start, part, _ in prompts(entry):
            got = scores.get(f'{args.pair}-{index}-{start}', {})
            for n, item in enumerate(part, 1):
                flagged.append((entry, item, got.get(str(n))))
    with open(work / f'{args.pair}-flagged.tsv', 'w', encoding='utf-8', newline='') as handle:
        out = csv.writer(handle, delimiter='\t', lineterminator='\n')
        out.writerow(['lemma', 'strong', 'pos', 'gloss', 'rendering', 'key', 'count', 'refs', 'reasons', 'haiku'])
        for entry, item, s in sorted(flagged, key=lambda f: (f[2] is None, f[2] if f[2] is not None else 9, -f[1]['count'])):
            out.writerow([entry['lemma'], entry['strong'], entry['pos'], entry['gloss'], item['raw'], item['key'],
                          item['count'], '; '.join(item['refs']), ' | '.join(item['reasons']),
                          '' if s is None else s])
    scored = [s for _, _, s in flagged if s is not None]
    bands = collections.Counter('0' if s < 0.25 else '0.25-0.5' if s < 0.5 else '0.5-0.75' if s < 0.75 else '0.75-1'
                                for s in scored)
    summary = dict(passed['totals'])
    summary.update({'flagged': len(flagged), 'scored': len(scored), 'score_bands': dict(sorted(bands.items())),
                    'requests_answered': sum(1 for k in scores if k.startswith(args.pair + '-'))})
    (work / f'{args.pair}-summary.json').write_text(json.dumps(summary, indent=1), encoding='utf-8')
    print(json.dumps(summary, indent=1))


def sample(args):
    """The lowest-scored renderings, at most two per lemma, each with its first verse on both sides."""
    work = work_dir(args)
    data = work / 'data'
    passed = json.loads((work / f'{args.pair}-pass.json').read_text(encoding='utf-8'))
    scores = load_scores(args)
    hebrew, by_verse = {}, collections.defaultdict(list)
    for wid, verse, position, surface, lemma, strong, gloss, pos in rows(data / 'bhsa-words.tsv'):
        hebrew[int(wid)] = (int(verse), int(position), surface, gloss)
        by_verse[int(verse)].append((int(position), int(wid)))
    refs = {int(v): f'{b} {c}:{n}' for v, b, c, n in rows(data / 'bhsa-verses.tsv')}
    english, rendered, together = load_pair(data, args.pair)
    e_refs = {int(v): f'{b} {c}:{n}' for v, b, c, n in rows(data / f'{args.pair}-verses.tsv')}
    e_by_verse = collections.defaultdict(list)
    for eid, (verse, position, surface) in english.items():
        e_by_verse[verse].append((position, eid))
    low = []
    for index, entry in enumerate(passed['lemmas']):
        for start, part, _ in prompts(entry):
            got = scores.get(f'{args.pair}-{index}-{start}', {})
            for n, item in enumerate(part, 1):
                if got.get(str(n)) is not None and (not args.reason or any(r.startswith(args.reason) for r in item['reasons'])):
                    low.append((got[str(n)], -item['count'], entry, item))
    low.sort(key=lambda x: (x[0], x[1]))
    per_lemma, shown = collections.Counter(), 0
    for value, _, entry, item in low:
        if shown >= args.count or per_lemma[entry['strong']] >= 2:
            continue
        per_lemma[entry['strong']] += 1
        shown += 1
        hid = item['hids'][0]
        verse = hebrew[hid][0]
        heb = ' '.join((f'[[{hebrew[w][3]}]]' if w == hid else f'({hebrew[w][3]})' if w in together[hid] else hebrew[w][3])
                       for _, w in sorted(by_verse[verse]))
        linked = rendered.get(hid, set())
        e_verses = sorted({english[e][0] for e in linked if e in english})
        eng = ' / '.join(e_refs[v] + ' ' + ' '.join((f'[[{english[e][2]}]]' if e in linked else english[e][2])
                                                    for _, e in sorted(e_by_verse[v])) for v in e_verses)
        print(f"#{shown} {entry['lemma']} {entry['strong']} '{entry['gloss']}' -> \"{item['raw']}\" x{item['count']} "
              f"score {value} | {' | '.join(item['reasons'])}\n  {refs[verse]} glosses: {heb}\n  {eng}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo', default=str(HERE.parent), help='the checkout holding .env, the ledger and Resources')
    sub = parser.add_subparsers(dest='command', required=True)
    p = sub.add_parser('export'); p.add_argument('pairs', nargs='+'); p.set_defaults(run=export)
    p = sub.add_parser('pass'); p.add_argument('pair'); p.add_argument('--other')
    p.add_argument('--books', help='comma-separated BHSA book abbreviations, e.g. Gen'); p.set_defaults(run=run_pass)
    for name, run in (('estimate', estimate), ('score', score)):
        p = sub.add_parser(name); p.add_argument('pair'); p.add_argument('--limit', type=int); p.set_defaults(run=run)
    p = sub.add_parser('report'); p.add_argument('pair'); p.set_defaults(run=report)
    p = sub.add_parser('sample'); p.add_argument('pair'); p.add_argument('--count', type=int, default=40)
    p.add_argument('--reason', help='only items with a reason starting so, e.g. shift')
    p.set_defaults(run=sample)
    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
