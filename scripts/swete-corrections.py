"""
The corrections this project makes to Swete's transcription by rule, written out one by one so each
can be read, and a measurement of what the rules leave.

Swete's files (nathans/lxx-swete, from First1KGreek's reading of the printed page) carry three kinds
of fault a rule can settle without the page, because the evidence is in the token itself:

  latin    a Latin letter standing for the Greek one it looks exactly like — Aἴγυπτον, ΚAI, oἱ —
           and, after a breathing written as an apostrophe, a Latin l for the capital iota. Only
           where every Latin letter in the token has one Greek counterpart and the word it gives is
           one Swete, Brenton or GLAUx prints somewhere.
  figure   the chapter's Roman numeral, or the verse's own letter, run into the verse's first word:
           IXκαὶ at 9:1, aἔσται at Proverbs 3:22a. Swete prints the number in the margin; it is the
           verse's own address, not a word.
  fused    two words run together — τῶνβρωμάτων, κύριοςὁ — where Brenton's Greek and GLAUx both read
           the two words at that place, Swete prints each of them elsewhere in this spelling, and
           the run-together token is not a word any of the three prints.
  number   the verse number Swete prints in the margin, in figures, let into the text: glued to a
           word (⁶⁵εὐλογεῖτε, (17)ἐν, ἐσπούδασας.5), between two (χρυσᾶς.¹καὶ) or standing as a
           token of its own ((16), 59). The figure is taken out and the word kept, where the word
           is one Swete prints elsewhere or the figure is the verse's own number, the next one, or
           the bracketed second numbering; a figure standing alone goes.
  overrun  the same, where the figure also took the first letters of a word the verses on either
           side print whole at the same place — the song of Daniel 3, whose every verse opens
           εὐλογεῖτε: 7⁸κὐλογεῖτε is εὐλογεῖτε.
  digit    a figure inside a word, which is the capital iota or the omicron it looks like
           (’1οθὸρ) or a stray mark (θρό9νου), where the word it leaves is one Swete prints.
  escape   a character the conversion wrote as its code point, U+03F2 for the lunate sigma.

The verse divisions the transcription lost at a figure are not corrections but repairs of the
numbering, made before these by Essenthos.Forge/Swete/SweteDivisions.cs; this reads them from there
and applies them first, and lists every figure that stands for a verse the file does not open.

Everything else is left as the transcription reads it, and anything with a Latin letter the rules do
not settle is listed rather than guessed. The list is Essenthos.Forge/Swete/SweteCorrections.json,
which the reader applies on a cold load and SweteRestorationLoader on a warm one.

  python scripts/swete-corrections.py                  write the corrections, list what is left
  python scripts/swete-corrections.py --gaps FILE      also write every verse where Brenton and GLAUx
                                                       both read words Swete does not, one per line

The second is a list to check against the page, not a list of corrections: where Swete lacks a word
both witnesses read, the same comparison run the other way — Swete reading a word both lack — finds
between a third and a half as many, and genuine readings run both ways, so something like one such
gap in three is what Vaticanus prints rather than what the transcription lost. That holds even where
Brenton reads the verse exactly as Swete apart from the one word. Only the page tells them apart,
and scripts/swete-scans.py reads it.

A worktree has no corpus of its own: --resources points at the main checkout's.
"""

import argparse, collections, difflib, json, os, re, sys, unicodedata
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8')
repository = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The Old Greek of Susanna, Daniel and Bel is read too, as a text of its own; Brenton prints Theodotion's,
# so no two words of it are divided on his evidence.
NOT_READ = {'48.Isaias'}

BRENTON = {
    '01': 'GEN', '02': 'EXO', '03': 'LEV', '04': 'NUM', '05': 'DEU', '06': 'JOS', '08': 'JDG', '10': 'RUT',
    '11': '1SA', '12': '2SA', '13': '1KI', '14': '2KI', '15': '1CH', '16': '2CH', '17': '1ES', '18': 'EZR',
    '19': 'ESG', '20': 'JDT', '21': 'TOB', '23': '1MA', '24': '2MA', '25': '3MA', '26': '4MA', '27': 'PSA',
    '29': 'PRO', '31': 'SNG', '32': 'JOB', '33': 'WIS', '34': 'SIR', '36': 'HOS', '37': 'AMO', '38': 'MIC',
    '39': 'JOL', '40': 'OBA', '41': 'JON', '42': 'NAM', '43': 'HAB', '44': 'ZEP', '45': 'HAG', '46': 'ZEC',
    '47': 'MAL', '49': 'JER', '50': 'BAR', '51': 'LAM', '52': 'LJE', '53': 'EZK', '55': 'SUS', '57': 'DAG',
    '59': 'BEL',
}

# GLAUx writes a verse's added parts with Greek letters, Swete with Latin ones.
GLAUX_LABEL = dict(zip('αβγδεζηθικλ', 'abcdefghikl'))

LOOK_ALIKE = dict(zip('ABEHIKMNOPTXYZo', 'ΑΒΕΗΙΚΜΝΟΡΤΧΥΖο'))
BREATHINGS = '’‘᾿ʼ\''
ROMAN = re.compile(r'^([IVXLC]+)(.+)$')

FIGURES = '0123456789⁰¹²³⁴⁵⁶⁷⁸⁹'
PLAIN = str.maketrans('⁰¹²³⁴⁵⁶⁷⁸⁹', '0123456789')
# The number as the transcription lets it in: plain or superscript figures, a verse letter, a range,
# and the brackets of Swete's second numbering in Sirach and Judith.
FIGURE = r'[\(⁽]*[0-9⁰¹²³⁴⁵⁶⁷⁸⁹]+(?:[a-zª]|[,\-–][0-9⁰¹²³⁴⁵⁶⁷⁸⁹]+[a-z]?)*°?\)?'
LEADING = re.compile(r'^[\)*;,–—]*(' + FIGURE + r')(.*)$')
BETWEEN = re.compile(r'^(.*[^0-9⁰¹²³⁴⁵⁶⁷⁸⁹\s(⁽])((?:[\(⁽][0-9⁰¹²³⁴⁵⁶⁷⁸⁹]+\))|[0-9⁰¹²³⁴⁵⁶⁷⁸⁹]+)([^0-9⁰¹²³⁴⁵⁶⁷⁸⁹].*)$')
TRAILING = re.compile(r'^(.*[^\w\s])([0-9⁰¹²³⁴⁵⁶⁷⁸⁹]+)$')
ESCAPED_SIGMA = 'U+03F2'
# How many times more often Swete prints the word the verses beside have than what the figure left.
OVERRUN_EVIDENCE = 10


def roman(numeral):
    values = {'I': 1, 'V': 5, 'X': 10, 'L': 50, 'C': 100}
    total = 0
    for i, c in enumerate(numeral):
        v = values[c]
        total += -v if i + 1 < len(numeral) and values[numeral[i + 1]] > v else v
    return total


def is_greek(c):
    return 'GREEK' in unicodedata.name(c, '') and unicodedata.category(c).startswith('L')


def is_latin(c):
    return 'LATIN' in unicodedata.name(c, '') and unicodedata.category(c).startswith('L')


def fold(s):
    s = unicodedata.normalize('NFD', s)
    return ''.join(c for c in s if unicodedata.category(c).startswith('L')).lower().replace('ς', 'σ')


def key(w):
    """The fold, with a movable nu taken off so ἐγέννησε and ἐγέννησεν compare as one word."""
    f = fold(w)
    return f[:-1] if len(f) >= 4 and f.endswith('ν') and f[-2] in 'ει' else f


def bare(tok):
    """The token without the punctuation after it."""
    i = len(tok)
    while i > 0 and not unicodedata.category(tok[i - 1]).startswith(('L', 'M')) and tok[i - 1] not in BREATHINGS:
        i -= 1
    return tok[:i]


def swete_runs(folder, book):
    runs = []
    for line in open(os.path.join(folder, book + '.txt'), encoding='utf-8'):
        ref, _, tok = line.rstrip('\n').partition(' ')
        tok = tok.strip()
        if not tok:
            continue
        if runs and runs[-1][0] == ref:
            runs[-1][1].append(tok)
        else:
            runs.append((ref, [tok]))
    return runs


def glaux(folder, number):
    verses = collections.defaultdict(list)
    path = os.path.join(folder, f'0527-0{number}.xml')
    if not os.path.exists(path):
        return verses
    for _, el in ET.iterparse(path):
        if el.tag == 'word':
            m = re.match(r'^(\d+)\.(\d+)(.*)$', el.get('div_section') or '')
            if m and not el.get('postag', '').startswith('u') and fold(el.get('form')):
                label = ''.join(GLAUX_LABEL.get(c, c) for c in m.group(3))
                verses[(m.group(1), m.group(2) + label)].append(el.get('form'))
            el.clear()
    return verses


def brenton(folder, code):
    verses = collections.defaultdict(list)
    if code is None:
        return verses
    name = next((f for f in os.listdir(folder) if f.endswith(code + 'grcbrent.usfm')), None)
    if name is None:
        return verses
    marker = chr(92)
    chapter = verse = None
    for line in open(os.path.join(folder, name), encoding='utf-8'):
        line = line.strip()
        if line.startswith(marker + 'c '):
            chapter, verse = line.split()[1], None
            continue
        m = re.match(re.escape(marker) + r'v (\S+)\s*(.*)$', line)
        if m:
            verse, rest = m.group(1), m.group(2)
        elif verse is not None and line and not line.startswith(marker):
            rest = line
        else:
            continue
        verses[(chapter, verse)].extend(rest.split())
    return verses


def moved(runs):
    """References whose words the reader moves or joins, which a correction must not be addressed to."""
    seen = collections.Counter(ref for ref, _ in runs)
    out = {ref for ref, n in seen.items() if n > 1}
    chapters = collections.OrderedDict()
    for ref, _ in runs:
        chapters.setdefault(ref.split('.')[1], []).append(ref)
    number = lambda ref: int(re.match(r'\d*', ref.split('.')[2]).group() or 0)
    for refs in chapters.values():
        if len(refs) > 1 and (number(refs[0]) == 0 or number(refs[0]) > number(refs[1])):
            out.add(refs[0])
    return out


def hand_restored(forge):
    source = open(os.path.join(forge, 'Swete', 'SweteRestorations.cs'), encoding='utf-8').read()
    files = {'Genesis': '01.Genesis', 'Exodus': '02.Exodus'}
    return {(files[m.group(1)], m.group(2), m.group(3))
            for m in re.finditer(r'new\((Genesis|Exodus), (\d+), (\d+),', source)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--resources', default=os.path.join(repository, 'Resources'))
    parser.add_argument('--gaps')
    args = parser.parse_args()

    sweteFolder = os.path.join(args.resources, 'Swete')
    books = sorted(f[:-4] for f in os.listdir(sweteFolder) if f.endswith('.txt') and f[:-4] not in NOT_READ)
    forge = os.path.join(repository, 'Essenthos.Forge')
    excluded = hand_restored(forge)

    made = divisions(forge)
    runs = {book: divide(swete_runs(sweteFolder, book), made.get(book, [])) for book in books}
    witnesses = {book: (glaux(os.path.join(args.resources, 'Glaux', 'xml'), book[:2]),
                        brenton(os.path.join(args.resources, 'Septuagint'), BRENTON.get(book[:2])))
                 for book in books}

    forms = collections.Counter()
    for book in books:
        for _, tokens in runs[book]:
            for token in tokens:
                word = bare(token)
                if word and not any(is_latin(c) for c in word) and not any(c.isdigit() for c in word):
                    forms[word] += 1
    witnessed = set()
    for g, b in witnesses.values():
        for words in list(g.values()) + list(b.values()):
            witnessed.update(fold(bare(w)) for w in words)
    vocabulary = witnessed | {fold(w) for w in forms}
    printed_forms = collections.Counter()
    for w, n in forms.items():
        printed_forms[fold(w)] += n

    corrections = []
    unsettled = []
    figures_left = []
    by_address = {book: {tuple(ref.split('.')[1:]): tokens for ref, tokens in runs[book]} for book in books}
    for book in books:
        g, b = witnesses[book]
        skip = moved(runs[book])
        for ref, tokens in runs[book]:
            _, chapter, verse = ref.split('.')
            addressable = chapter.isdigit() and chapter != '0' and ref not in skip and (book, chapter, verse) not in excluded
            label = re.sub(r'^\d+', '', verse)

            for at, token in enumerate(tokens):
                if not (any(is_latin(c) for c in token) and any(is_greek(c) for c in token)) or ESCAPED_SIGMA in token:
                    continue
                printed, kind = settle(token, at, chapter, verse, label, vocabulary,
                                      g.get((chapter, verse), []) + b.get((chapter, verse), []))
                if printed is None or not addressable:
                    unsettled.append(f'{book} {chapter}:{verse} {token}')
                    continue
                corrections.append(entry(book, chapter, verse, tokens, at, token, printed, kind))

            if addressable and (chapter, verse) in g and (chapter, verse) in b:
                for at, first, second in fusions(tokens, g[(chapter, verse)], b[(chapter, verse)], forms, witnessed):
                    corrections.append(entry(book, chapter, verse, tokens, at, tokens[at], f'{first} {second}', 'fused'))

            # The figures, against the verse as the corrections above leave it, since the reader makes
            # them in that order.
            now = list(tokens)
            for c in corrections:
                if (c['book'], c['chapter'], c['verse']) == (book, chapter, verse):
                    now = replaced(now, c['digitised'], c['printed'])
            number = int(re.match(r'\d*', verse).group() or 0)
            around = [by_address[book].get((chapter, str(number + step)), []) for step in (-1, 1)]
            at = 0
            while at < len(now):
                token = now[at]
                if not has_figure(token) or any(is_latin(c) for c in re.sub(FIGURE, '', token.replace(ESCAPED_SIGMA, ''))):
                    at += 1
                    continue
                printed, kind = unnumber(token, at, chapter, verse, vocabulary, printed_forms, around)
                if printed is None or not addressable:
                    if any(c.isascii() and c.isdigit() for c in token) or any(is_greek(c) for c in token):
                        figures_left.append(f'{book} {chapter}:{verse} {token}')
                    at += 1
                    continue
                c = entry(book, chapter, verse, now, at, token, printed, kind)
                corrections.append(c)
                now = replaced(now, c['digitised'], c['printed'])
                at += len([t for t in printed.split(' ') if t])

    corrections.sort(key=lambda c: (c['book'], int(c['chapter']), int(re.match(r'\d+', c['verse']).group()), c['verse']))
    out = os.path.join(forge, 'Swete', 'SweteCorrections.json')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('[\n' + ',\n'.join('  ' + json.dumps(c, ensure_ascii=False) for c in corrections) + '\n]\n')

    kinds = collections.Counter(c['kind'] for c in corrections)
    print(f'{len(corrections)} corrections written to {os.path.relpath(out, repository)}: '
          + ', '.join(f'{n} {k}' for k, n in kinds.most_common()))
    print(f'{len(unsettled)} tokens with a Latin letter left as the transcription reads them:')
    for line in unsettled:
        print('  ' + line)
    print(f'{len(figures_left)} tokens with a figure left as the transcription reads them:')
    for line in figures_left:
        print('  ' + line)
    lost = [line for book in books for line in lost_divisions(book, runs[book])]
    print(f'{len(lost)} figures naming a verse the file does not open, a division for SweteDivisions.cs:')
    for line in lost:
        print('  ' + line)

    measure(books, runs, witnesses, corrections, excluded, args.gaps)


def settle(token, at, chapter, verse, label, vocabulary, beside):
    """What a token with a Latin letter in it prints, and why; or None where no rule settles it."""
    if at == 0:
        m = ROMAN.match(token)
        if m and verse == '1' and roman(m.group(1)) == int(chapter) and fold(m.group(2)) in vocabulary \
                and not any(is_latin(c) for c in m.group(2)):
            return m.group(2), 'figure'
        figure = re.match(r'^([0-9⁰¹²³⁴⁵⁶⁷⁸⁹]*)([a-z])(.+)$', token)
        if figure and label and figure.group(2) == label and fold(figure.group(3)) in vocabulary \
                and not any(is_latin(c) for c in figure.group(3)):
            digits = figure.group(1).translate(str.maketrans('⁰¹²³⁴⁵⁶⁷⁸⁹', '0123456789'))
            if digits in ('', re.match(r'\d+', verse).group()):
                return figure.group(3), 'figure'

    printed = []
    for i, c in enumerate(token):
        if not is_latin(c):
            printed.append(c)
        elif c in 'lo' and i > 0 and all(p in BREATHINGS for p in token[:i]):
            printed.append('Ι' if c == 'l' else 'Ο')
        elif c in LOOK_ALIKE and unicodedata.normalize('NFD', c) == c:
            printed.append(LOOK_ALIKE[c])
        else:
            return None, None
    printed = ''.join(printed)
    word = bare(printed)
    if not word or any(c.isdigit() for c in word):
        return None, None
    if fold(word) not in vocabulary and not any(near(word, w) for w in beside):
        return None, None
    # A capital inside a lower-case word is not a look-alike but a letter read wrong.
    letters = [c for c in unicodedata.normalize('NFD', word) if unicodedata.category(c).startswith('L')]
    if any(c.isupper() for c in letters[1:]) and not all(c.isupper() for c in letters):
        return None, None
    return printed, 'latin'


def has_figure(token):
    return any(c in FIGURES for c in token) or ESCAPED_SIGMA in token


def greek_word(text):
    """Whether a token's letters are all Greek, so taking a figure out of it leaves a Greek word."""
    letters = [c for c in text if unicodedata.category(c).startswith('L')]
    return bool(letters) and all(is_greek(c) for c in letters)


def figure_number(figure):
    digits = re.match(r'\d+', re.sub(r'^[^\d]*', '', figure.translate(PLAIN)))
    return int(digits.group()) if digits else None


def unnumber(token, at, chapter, verse, vocabulary, printed, around):
    """
    What a token with a figure in it prints, and why; or None where no rule settles it. The printed
    form is empty for a figure standing alone and holds a space where the figure stood between two
    words.
    """
    if ESCAPED_SIGMA in token:
        printed = re.sub(re.escape(ESCAPED_SIGMA) + r'(?![^\W\d_])', 'ς', token).replace(ESCAPED_SIGMA, 'σ')
        return (printed, 'escape') if not has_figure(printed) else (None, None)

    number = int(re.match(r'\d*', verse).group() or 0)
    if not any(is_greek(c) for c in token):
        # The reader already hangs a token with no letter and no plain figure on the word before it.
        if not any(c.isdigit() and c.isascii() for c in token):
            return None, None
        left = re.sub(r'[0-9⁰¹²³⁴⁵⁶⁷⁸⁹()⁽⁾*°a-zª\-–—]', '', token)
        return left, 'number'

    leading = LEADING.match(token)
    if leading and any(is_greek(c) for c in leading.group(2)):
        rest = leading.group(2)
        if has_figure(rest) or not greek_word(bare(rest)):
            return None, None
        whole = overrun(rest, at, around, printed)
        if whole is not None:
            return whole, 'overrun'
        if fold(bare(rest)) in vocabulary:
            return rest, 'number'
        figure = leading.group(1)
        if figure_number(figure) in (number, number + 1) or figure[0] in '(⁽':
            return rest, 'number'
        return None, None

    between = BETWEEN.match(token)
    if between:
        first, figure, second = between.groups()
        if (figure[0] in '(⁽' or not unicodedata.category(first[-1]).startswith('L')) \
                and not has_figure(first + second) and greek_word(bare(first)) and greek_word(bare(second)) \
                and fold(bare(first)) in vocabulary and fold(bare(second)) in vocabulary:
            return f'{first} {second}', 'number'

    trailing = TRAILING.match(token)
    if trailing and not has_figure(trailing.group(1)) and fold(bare(trailing.group(1))) in vocabulary:
        return trailing.group(1), 'number'

    for printed in (re.sub(r'(?<=[’‘᾿ʼ])1', 'Ι', token), token.replace('0', 'ο'), re.sub(r'[0-9]', '', token)):
        if printed != token and not has_figure(printed) and greek_word(bare(printed)) \
                and fold(bare(printed)) in vocabulary:
            return printed, 'digit'
    return None, None


def overrun(rest, at, around, printed):
    """
    The word the verses on either side print at the same place, where what is left of this one is it
    with its first letters taken by the figure: κὐλογεῖτε or ὐλογεῖτε where they print εὐλογεῖτε.
    Only a word Swete prints many times over, against one he prints seldom or never: the verses beside
    carry the same misreadings, τὐλογεῖτε among them, and one of those is never the word put back.
    """
    word = bare(rest)
    f = fold(word)
    whole = set()
    for tokens in around:
        words = [LEADING.match(t).group(2) if LEADING.match(t) else t for t in tokens]
        words = [bare(w) for w in words if any(is_greek(c) for c in w)]
        if at < len(words) and not has_figure(words[at]):
            g = fold(words[at])
            if len(g) >= 5 and len(f) in (len(g) - 1, len(g)) and f.endswith(g[2:]) and f != g                     and printed[g] >= OVERRUN_EVIDENCE * max(1, printed[f]):
                whole.add(words[at])
    return whole.pop() + rest[len(word):] if len(whole) == 1 else None


def divisions(forge):
    """
    The repairs Essenthos.Forge/Swete/SweteDivisions.cs makes, by book, as (chapter, verse,
    digitised, printed, to), with no verse to go to for a word replaced in place: the divisions the
    transcription lost, which the reader opens again before any correction is made.
    """
    path = os.path.join(forge, 'Swete', 'SweteDivisions.cs')
    made = collections.defaultdict(list)
    if not os.path.exists(path):
        return made
    book = None
    for line in open(path, encoding='utf-8'):
        opened = re.match(r'\s*\["([^"]+)"\] =', line)
        if opened:
            book = opened.group(1)
        for m in re.finditer(r'EditionRepair\.(Divide|Replace)\((\d+), "([^"]+)", "([^"]+)", "([^"]*)"(?:, "([^"]+)")?', line):
            kind, chapter, verse, digitised, printed, to = m.groups()
            made[book].append((chapter, verse, digitised, printed, to if kind == 'Divide' else None))
    return made


def divide(runs, made):
    """The runs with the repairs made, the way EditionRepairs makes them."""
    for chapter, verse, digitised, printed, to in made:
        at = next(i for i, (ref, _) in enumerate(runs) if ref.split('.')[1:] == [chapter, verse])
        ref, tokens = runs[at]
        want = digitised.split(' ')
        found = [i for i in range(len(tokens)) if tokens[i:i + len(want)] == want]
        if len(found) != 1:
            raise SystemExit(f'{ref} holds "{digitised}" {len(found)} times; SweteDivisions.cs no longer reads as the file')
        i = found[0]
        printed = printed.split(' ') if printed else []
        if to is None:
            runs[at] = (ref, tokens[:i] + printed + tokens[i + len(want):])
            continue
        runs[at] = (ref, tokens[:i])
        runs.insert(at + 1, (f"{ref.split('.')[0]}.{chapter}.{to}", printed + tokens[i + len(want):]))
    return runs


def lost_divisions(book, runs):
    """Figures naming the next verse where the file opens no such verse: a division it lost."""
    refs = {ref for ref, _ in runs}
    for ref, tokens in runs:
        _, chapter, verse = ref.split('.')
        number = int(re.match(r'\d*', verse).group() or 0)
        for at, token in enumerate(tokens):
            m = LEADING.match(token) or BETWEEN.match(token)
            if not m or not (any(is_greek(c) for c in token) or token.isascii() and token.isdigit()):
                continue
            figure = m.group(1) if m.re is LEADING else m.group(2)
            if figure[0] not in '(⁽' and figure_number(figure) == number + 1 and f'{ref.rsplit(".", 1)[0]}.{number + 1}' not in refs:
                yield f'{book} {chapter}:{verse} at {at}: {" ".join(tokens[max(0, at - 2):at + 3])}'


def near(word, witness):
    """
    A name no text here prints in this spelling, standing where a witness prints one that opens with
    the same Greek letter and differs from it by two letters at most: Swete's ’Eλεισάβεθ where Brenton
    has Ἐλισάβεθ. It is what keeps a Latin letter that is the page's Greek one apart from a Latin
    letter that is some other letter misread — Bἰς where the psalm opens Εἰς.
    """
    a, b = fold(word), fold(bare(witness))
    if not a or not b or a[0] != b[0]:
        return False
    previous = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        current = [i]
        for j, y in enumerate(b, 1):
            current.append(min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + (x != y)))
        previous = current
    return previous[-1] <= 2


def fusions(tokens, glaux_words, brenton_words, forms, witnessed):
    """Tokens that are two words both witnesses read at that place, run together."""
    swete = [key(bare(t)) for t in tokens]
    g = [key(w) for w in glaux_words]
    b = [key(bare(w)) for w in brenton_words]
    g_ops = difflib.SequenceMatcher(None, swete, g, autojunk=False).get_opcodes()
    b_replaced = {(o[1], o[2]): tuple(b[o[3]:o[4]]) for o in difflib.SequenceMatcher(None, swete, b, autojunk=False).get_opcodes() if o[0] == 'replace'}
    for op, i1, i2, j1, j2 in g_ops:
        if op != 'replace' or i2 - i1 != 1 or j2 - j1 != 2 or b_replaced.get((i1, i2)) != tuple(g[j1:j2]):
            continue
        token = tokens[i1]
        if any(c.isdigit() for c in token) or fold(bare(token)) in witnessed or fold(bare(token)) != fold(glaux_words[j1]) + fold(glaux_words[j1 + 1]):
            continue
        for first, second in splits(token, len(fold(glaux_words[j1]))):
            if bare(first) in forms and bare(second).lstrip(OPENING) in forms and two_words(first, second):
                yield i1, first, second
                break


OPENING = '(«‘“[' + BREATHINGS.replace('’', '').replace('ʼ', '')
TONES = '̀́͂'
BREATHING_MARKS = '̓̔'


def two_words(first, second):
    """
    Whether the token shows in its own letters that it is two words: a final sigma or a mark between
    them, a capital or a breathing opening the second, or an accent on each. Swete writes οὐκέτι and
    οὐδὲ as one word, and nothing in them says otherwise, so they are left as he prints them.
    """
    if first.endswith('ς') or first != bare(first):
        return True
    opening = unicodedata.normalize('NFD', second)
    if opening[0].isupper() or not unicodedata.category(opening[0]).startswith('L'):
        return True
    i = 1
    while i < len(opening) and unicodedata.category(opening[i]).startswith('M'):
        if opening[i] in BREATHING_MARKS:
            return True
        i += 1
    tones = lambda w: sum(1 for c in unicodedata.normalize('NFD', w) if c in TONES)
    return tones(first) >= 2 or (tones(first) >= 1 and tones(second) >= 1)


def splits(token, letters):
    """The ways to cut a token after so many letters: marks between the two go with either half."""
    count = 0
    for i, c in enumerate(token):
        if unicodedata.category(c).startswith('L'):
            count += len(fold(c)) if fold(c) else 0
            if count == letters:
                end = i + 1
                while end < len(token) and unicodedata.category(token[end]).startswith('M'):
                    end += 1
                start = end
                while start < len(token) and not unicodedata.category(token[start]).startswith('L'):
                    start += 1
                opening = next((k for k in range(end, start) if token[k] in OPENING), None)
                cuts = [opening] if opening is not None else range(start, end - 1, -1)
                for cut in cuts:
                    yield token[:cut], token[cut:]
                return


def entry(book, chapter, verse, tokens, at, token, printed, kind):
    """The token, lengthened by its neighbours until it names one place in the verse."""
    lo, hi = at, at + 1
    while sum(1 for p in range(len(tokens)) if tokens[p:p + hi - lo] == tokens[lo:hi]) > 1:
        lo, hi = max(0, lo - 1), min(len(tokens), hi + 1)
    digitised = tokens[lo:hi]
    return {
        'book': book, 'chapter': chapter, 'verse': verse, 'kind': kind,
        'digitised': ' '.join(digitised),
        'printed': ' '.join(t for t in tokens[lo:at] + [printed] + tokens[at + 1:hi] if t),
    }


def replaced(tokens, digitised, printed):
    """The tokens with a correction made, as SweteRestorations makes it."""
    want = digitised.split(' ')
    i = next(i for i in range(len(tokens)) if tokens[i:i + len(want)] == want)
    return tokens[:i] + [t for t in printed.split(' ') if t] + tokens[i + len(want):]


def short_verses(books, runs, witnesses, corrections):
    """
    Every verse of Swete two words or more short of GLAUx at the same address once the corrections
    are made: its words as they then stand, the deficit, the gaps Brenton and GLAUx both fill (the
    position in those words each is missing before, and the missing words' folded keys), and why it
    is short.
    """
    fixed = collections.defaultdict(dict)
    for c in corrections:
        fixed[(c['book'], c['chapter'], c['verse'])][c['digitised']] = c['printed']

    for book in books:
        g, b = witnesses[book]
        for ref, tokens in runs[book]:
            _, chapter, verse = ref.split('.')
            if (chapter, verse) not in g:
                continue
            record = {'book': book, 'chapter': chapter, 'verse': verse, 'ref': ref, 'gaps': {}}
            words = [t for t in tokens if fold(t)]
            if len(g[(chapter, verse)]) - len(words) < 2:
                yield dict(record, reason=None)
                continue
            text = ' '.join(tokens)
            for digitised, printed in fixed.get((book, chapter, verse), {}).items():
                text = text.replace(digitised, printed, 1)
            now = [t for t in text.split(' ') if fold(t)]
            deficit = len(g[(chapter, verse)]) - len(now)
            record.update(words=now, deficit=deficit, short=True)
            if deficit < 2:
                yield dict(record, reason='whole after the corrections')
                continue
            swete = [key(bare(t)) for t in now]
            gk = [key(w) for w in g[(chapter, verse)]]
            bk = [key(bare(w)) for w in b.get((chapter, verse), [])]
            g_inserts = {o[1]: tuple(gk[o[3]:o[4]]) for o in difflib.SequenceMatcher(None, swete, gk, autojunk=False).get_opcodes() if o[0] == 'insert'}
            b_inserts = {o[1]: tuple(bk[o[3]:o[4]]) for o in difflib.SequenceMatcher(None, swete, bk, autojunk=False).get_opcodes() if o[0] == 'insert'}
            agreed = {at: w for at, w in g_inserts.items() if b_inserts.get(at) == w}
            missing = sum(len(w) for w in agreed.values())
            if (chapter, verse) not in b:
                reason = 'Brenton does not print the verse (Psalms of Solomon, a division of its own)'
            elif missing >= deficit:
                reason = 'both witnesses read the missing words: a loss or a reading of Vaticanus, only the page can tell'
            elif missing > 0:
                reason = 'partly words both witnesses read, partly words only GLAUx reads or a different division'
            elif len(swete) >= len(bk) - 1:
                reason = 'Brenton is as long as Swete: GLAUx reads a fuller text or divides the verse otherwise'
            else:
                reason = 'the witnesses disagree with each other where Swete is short'
            record['gaps'] = agreed
            yield dict(record, reason=reason)


def measure(books, runs, witnesses, corrections, excluded, gaps_path):
    """How many verses stand short of GLAUx by two words or more, before and after, and why."""
    short_before = short_after = comparable = 0
    reasons = collections.Counter()
    candidates = []
    for record in short_verses(books, runs, witnesses, corrections):
        comparable += 1
        if record['reason'] is None:
            continue
        short_before += 1
        reasons[record['reason']] += 1
        if record['reason'] == 'whole after the corrections':
            continue
        short_after += 1
        now = record['words']
        for at, w in sorted(record['gaps'].items()):
            candidates.append(f"{record['book']}\t{record['chapter']}:{record['verse']}\t{record['deficit']}\t"
                              f"{' '.join(now[max(0, at - 3):at])} [{' '.join(w)}] {' '.join(now[at:at + 3])}")

    print(f'\nVerses of Swete two words or more short of GLAUx at the same address, of {comparable} both hold:')
    print(f'  before {short_before}, after {short_after}')
    for reason, n in reasons.most_common():
        print(f'  {n:5}  {reason}')
    if gaps_path:
        with open(gaps_path, 'w', encoding='utf-8', newline='\n') as f:
            f.write('book\tverse\tdeficit\twhere Brenton and GLAUx both read words Swete does not\n')
            f.write('\n'.join(candidates) + '\n')
        print(f'  {len(candidates)} gaps both witnesses fill written to {gaps_path}')


if __name__ == '__main__':
    main()
