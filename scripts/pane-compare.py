"""Compare the panes (/v1/panes) with /v1/parallel field by field, for the (text, book, chapter) triples of a
tab-separated file. usage: pane-compare.py <api base url> <triples.tsv> <report.txt>  (TSK-1502)

/v1/parallel's cards carry a differs word's base-reading card inline, after the word's own; a pane writes it
apart, in "eb" (c8523a7f). So the old reading's base-reading cards are held against "eb" and the rest against
"e", and every card compares role, origin and the base text, as the reader decodes a pane (PRB-1512)."""
import gzip
import json
import re
import sys
import urllib.request
import urllib.error

base, triples, report = sys.argv[1], sys.argv[2], sys.argv[3]


def get(url, gz=False):
    req = urllib.request.Request(url, headers={'Accept-Encoding': 'gzip'} if gz else {})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            body = r.read()
            if r.headers.get('Content-Encoding') == 'gzip':
                body = gzip.decompress(body)
            return r.status, json.loads(body)
    except urllib.error.HTTPError as e:
        return e.code, None


def old_word(w):
    m = w.get('morphology')
    sc, lx, tg, gz, at = (w.get(k) for k in ('strongCandidate', 'lexiconGloss', 'throughGreek', 'geezEntry', 'attached'))
    entity = w.get('entity')
    return {
        'id': w['id'], 's': w.get('text', ''), 't': w.get('trailer', ''), 'g': w.get('gloss'), 'l': w.get('lexeme'), 'n': w.get('strongNo'), 'rn': w.get('renderedStrong'),
        'w': sorted(w.get('originalWordIds', [])), 'p': w.get('mappingProvenance'), 'a': w.get('absence'), 'm': w.get('mark'),
        'supplied': w.get('supplied', False),
        'e': [card(e) for e in w.get('entities', []) if e.get('origin') != BASE_READING],
        'eb': [card(e) for e in w.get('entities', []) if e.get('origin') == BASE_READING],
        'entity': card(entity) if entity else None,
        'mo': None if m is None else {
            'pos': m.get('partOfSpeech'), 'case': m.get('case'), 'gender': m.get('gender'), 'number': m.get('number'),
            'person': m.get('person'), 'state': m.get('state'), 'stem': m.get('stem'), 'tense': m.get('tense'),
            'lexicalSet': m.get('lexicalSet'), 'phrasePos': m.get('phraseDependentPartOfSpeech'),
            'suffixGender': m.get('pronominalGender'), 'suffixNumber': m.get('pronominalNumber'),
            'suffixPerson': m.get('pronominalPerson'), 'nameType': m.get('nametypes')},
        'ph': w.get('phono'), 'pt': w.get('phonoTrailer'), 'lg': w.get('language'), 'el': w.get('elided', False), 'br': w.get('break'),
        'sc': sc and [sc.get('number'), sc.get('method'), sc.get('confidence')],
        'lx': lx and [lx.get('glosses'), lx.get('via')],
        'tg': tg and [tg.get(k) for k in ('corpus', 'word', 'lemma', 'glosses', 'method', 'confidence')],
        'gz': gz and [gz.get(k) for k in ('headword', 'latin', 'greek', 'via')],
        'at': at and [at.get('kind'), at.get('headId'), at.get('head')],
        'ro': w.get('roles') and [[r.get('occupation'), r.get('category'), r.get('person')] for r in w['roles']],
    }


def empty(value):
    """Both answers leave out a null, false, empty string and empty list (TSK-1661), so each of them counts as absent."""
    if isinstance(value, dict):
        return {k: empty(v) for k, v in value.items()}
    if isinstance(value, list):
        return None if not value else [empty(v) for v in value]
    return None if value is False or value == '' else value


BASE_READING = 'base-reading'
THROUGH = re.compile(r'^through (\S+) word \d+')


def card(e):
    return [e['type'], e['slug'], e.get('method'), e.get('confidence'), e.get('source'), e.get('note'), e.get('namedAs'),
            e.get('role'), e.get('origin'), e.get('baseText')]


def carried_through(route):
    """The text a base-reading card came through, read from its route as the reader does (panes.ts carriedThrough)."""
    match = THROUGH.match(route) if route else None
    return match.group(1) if match else None


def pane_card(c, rec, src, base=False):
    origin = c.get('origin') or (BASE_READING if base else None)
    route = c.get('via')
    return [rec[c['r']][1], rec[c['r']][0], c.get('m'), c.get('c'), src[c['src']] if 'src' in c else None, route, c.get('as'),
            c.get('role'), origin, carried_through(route) if origin == BASE_READING else None]


def new_word(w, pane):
    rec, prov, src = pane['records'], pane['provenance'], pane['sources']
    m = w.get('mo')
    cards = [pane_card(c, rec, src) for c in w.get('e', [])]
    base = [pane_card(c, rec, src, base=True) for c in w.get('eb', [])]
    return {
        'id': w['id'], 's': w['s'], 't': w.get('t', ''), 'g': w.get('g'), 'l': w.get('l'), 'n': w.get('n'), 'rn': w.get('rn'),
        'w': sorted(w.get('w', [])), 'p': prov[w['p']] if 'p' in w else None, 'a': w.get('a'), 'm': w.get('m'),
        'supplied': w.get('m') == 'supplied', 'e': cards, 'eb': base, 'entity': cards[0] if cards else None,
        'mo': None if m is None else {k: m.get(k) for k in ('pos', 'case', 'gender', 'number', 'person', 'state', 'stem', 'tense',
                                                            'lexicalSet', 'phrasePos', 'suffixGender', 'suffixNumber', 'suffixPerson', 'nameType')},
        'ph': w.get('ph'), 'pt': w.get('pt'), 'lg': w.get('lg'), 'el': w.get('el') == 1, 'br': w.get('br'),
        'sc': w.get('sc') and [w['sc']['n'], w['sc']['m'], w['sc'].get('c')],
        'lx': w.get('lx') and [w['lx']['g'], w['lx']['via']],
        'tg': w.get('tg') and [w['tg'].get(k) for k in ('corpus', 'word', 'lemma', 'glosses', 'method', 'confidence')],
        'gz': w.get('gz') and [w['gz'][k] for k in ('headword', 'latin', 'greek', 'via')],
        'at': w.get('at') and [w['at']['kind'], w['at']['headId'], w['at']['head']],
        'ro': w.get('ro') and [[r['occupation'], r['category'], r['person']] for r in w['ro']],
    }


def note(n):
    return [n.get('kind'), n.get('content'), n.get('anchorWordId'), n.get('anchor')]


out = open(report, 'w', encoding='utf-8')
checked = words = differ = 0
for line in open(triples, encoding='utf-8'):
    if not line.strip():
        continue
    text, book, chapter = line.split()
    code_old, old = get(f'{base}/v1/parallel/{book}/{chapter}?corpora={text}')
    code_new, pane = get(f'{base}/v1/panes/{text}/{book}/{chapter}', gz=True)
    problems = []
    if code_old != 200 or old is None:
        problems.append(f'parallel {code_old}')
    rows_old = {v['number']: v['texts'][text] for v in (old or {}).get('verses', []) if v['texts'].get(text) is not None}
    if code_new != 200:
        if rows_old or (old or {}).get('chapterNotes', {}).get(text) or (old or {}).get('bookNotes', {}).get(text):
            problems.append(f'pane {code_new} but parallel has {len(rows_old)} rows')
    else:
        rows_new = {v['n']: v for v in pane['verses']}
        if sorted(rows_old) != sorted(rows_new):
            problems.append(f'rows differ: {sorted(set(rows_old) ^ set(rows_new))[:10]}')
        if old['chapterCount'] != pane['chapters']:
            problems.append(f"chapters {old['chapterCount']} vs {pane['chapters']}")
        for n in sorted(set(rows_old) & set(rows_new)):
            o, p = rows_old[n], rows_new[n]
            ref = o.get('reference')
            if (ref and [ref['bookOrdinal'], ref['chapter'], ref['verse']]) != p.get('ref'):
                problems.append(f'{n} ref {ref} vs {p.get("ref")}')
            if o.get('verses', []) != p.get('own', []):
                problems.append(f'{n} own {o.get("verses")} vs {p.get("own")}')
            if o.get('statedVerses', []) != p.get('stated', []):
                problems.append(f'{n} stated')
            if [note(x) for x in o.get('notes', [])] != [note(x) for x in p.get('notes', [])]:
                problems.append(f'{n} notes')
            ow = [empty(old_word(w)) for w in o.get('words', [])]
            nw = [empty(new_word(w, pane)) for w in p.get('words', [])]
            words += len(ow)
            if len(ow) != len(nw):
                problems.append(f'{n} {len(ow)} words vs {len(nw)}')
            for a, b in zip(ow, nw):
                if a != b:
                    keys = [k for k in a if a[k] != b.get(k)]
                    problems.append(f'{n} word {a["id"]} {keys}: {[a[k] for k in keys]} vs {[b.get(k) for k in keys]}'[:600])
        heads_old = [[h.get('verse'), h.get('heading'), [note(x) for x in h.get('notes', [])]] for h in old.get('chapterNotes', {}).get(text, [])]
        heads_new = [[h.get('verse'), h.get('heading'), [note(x) for x in h.get('notes', [])]] for h in pane.get('headings', [])]
        if heads_old != heads_new:
            problems.append('headings differ')
        bo = old.get('bookNotes', {}).get(text)
        bn = pane.get('bookNotes')
        if (bo and [bo.get('title'), [note(x) for x in bo.get('notes', [])]]) != (bn and [bn.get('title'), [note(x) for x in bn.get('notes', [])]]):
            problems.append('book notes differ')
    checked += 1
    if problems:
        differ += 1
        out.write(f'{text} {book} {chapter}: {len(problems)} differences\n')
        for problem in problems[:20]:
            out.write(f'  {problem}\n')
print(f'{checked} panes checked, {words} words, {differ} with differences')
out.write(f'{checked} panes checked, {words} words, {differ} with differences\n')
