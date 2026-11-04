"""Compare the panes (/v1/panes) with /v1/parallel field by field, for the (text, book, chapter) triples of a
tab-separated file. usage: pane-compare.py <api base url> <triples.tsv> <report.txt>  (TSK-1502)"""
import gzip
import json
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
    m = w['morphology']
    return {
        'id': w['id'], 's': w['text'], 't': w['trailer'], 'g': w['gloss'], 'l': w['lexeme'], 'n': w['strongNo'],
        'w': sorted(w['originalWordIds']), 'p': w['mappingProvenance'], 'a': w['absence'], 'm': w['mark'], 'supplied': w['supplied'],
        'e': [card(e) for e in w['entities']], 'entity': card(w['entity']) if w['entity'] else None,
        'mo': None if m is None else {
            'pos': m['partOfSpeech'], 'case': m['case'], 'gender': m['gender'], 'number': m['number'], 'person': m['person'],
            'state': m['state'], 'stem': m['stem'], 'tense': m['tense'], 'lexicalSet': m['lexicalSet'],
            'phrasePos': m['phraseDependentPartOfSpeech'], 'suffixGender': m['pronominalGender'],
            'suffixNumber': m['pronominalNumber'], 'suffixPerson': m['pronominalPerson'], 'nameType': m['nametypes']},
        'ph': w['phono'], 'pt': w['phonoTrailer'], 'lg': w['language'], 'el': w['elided'], 'br': w['break'],
        'sc': w['strongCandidate'] and [w['strongCandidate']['number'], w['strongCandidate']['method'], w['strongCandidate']['confidence']],
        'lx': w['lexiconGloss'] and [w['lexiconGloss']['glosses'], w['lexiconGloss']['via']],
        'tg': w['throughGreek'] and [w['throughGreek'][k] for k in ('corpus', 'word', 'lemma', 'glosses', 'method', 'confidence')],
        'gz': w['geezEntry'] and [w['geezEntry'][k] for k in ('headword', 'latin', 'greek', 'via')],
        'at': w['attached'] and [w['attached']['kind'], w['attached']['headId'], w['attached']['head']],
        'ro': w['roles'] and [[r['occupation'], r['category'], r['person']] for r in w['roles']],
    }


def card(e):
    return [e['type'], e['slug'], e.get('method'), e.get('confidence'), e.get('source'), e.get('note'), e.get('namedAs')]


def new_word(w, pane):
    rec, prov, src = pane['records'], pane['provenance'], pane['sources']
    m = w.get('mo')
    cards = [[rec[c['r']][1], rec[c['r']][0], c.get('m'), c.get('c'), src[c['src']] if 'src' in c else None, c.get('via'), c.get('as')]
             for c in w.get('e', [])]
    return {
        'id': w['id'], 's': w['s'], 't': w.get('t', ''), 'g': w.get('g'), 'l': w.get('l'), 'n': w.get('n'),
        'w': sorted(w.get('w', [])), 'p': prov[w['p']] if 'p' in w else None, 'a': w.get('a'), 'm': w.get('m'),
        'supplied': w.get('m') == 'supplied', 'e': cards, 'entity': cards[0] if cards else None,
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
    return [n['kind'], n['content'], n.get('anchorWordId'), n.get('anchor')]


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
            ref = o['reference']
            if (ref and [ref['bookOrdinal'], ref['chapter'], ref['verse']]) != p.get('ref'):
                problems.append(f'{n} ref {ref} vs {p.get("ref")}')
            if o['verses'] != p.get('own', []):
                problems.append(f'{n} own {o["verses"]} vs {p.get("own")}')
            if o['statedVerses'] != p.get('stated', []):
                problems.append(f'{n} stated')
            if [note(x) for x in o['notes']] != [note(x) for x in p.get('notes', [])]:
                problems.append(f'{n} notes')
            ow = [old_word(w) for w in o['words']]
            nw = [new_word(w, pane) for w in p['words']]
            words += len(ow)
            if len(ow) != len(nw):
                problems.append(f'{n} {len(ow)} words vs {len(nw)}')
            for a, b in zip(ow, nw):
                if a != b:
                    keys = [k for k in a if a[k] != b.get(k)]
                    problems.append(f'{n} word {a["id"]} {keys}: {[a[k] for k in keys]} vs {[b.get(k) for k in keys]}'[:600])
        heads_old = [[h['verse'], h['heading'], [note(x) for x in h['notes']]] for h in old.get('chapterNotes', {}).get(text, [])]
        heads_new = [[h['verse'], h['heading'], [note(x) for x in h['notes']]] for h in pane.get('headings', [])]
        if heads_old != heads_new:
            problems.append('headings differ')
        bo = old.get('bookNotes', {}).get(text)
        bn = pane.get('bookNotes')
        if (bo and [bo['title'], [note(x) for x in bo['notes']]]) != (bn and [bn.get('title'), [note(x) for x in bn['notes']]]):
            problems.append('book notes differ')
    checked += 1
    if problems:
        differ += 1
        out.write(f'{text} {book} {chapter}: {len(problems)} differences\n')
        for problem in problems[:20]:
            out.write(f'  {problem}\n')
print(f'{checked} panes checked, {words} words, {differ} with differences')
out.write(f'{checked} panes checked, {words} words, {differ} with differences\n')
