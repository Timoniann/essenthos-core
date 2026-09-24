"""
Settles against the printed page the words Swete's transcription lacks where Brenton and GLAUx both
read them — the gaps `swete-corrections.py --gaps` lists and cannot settle, because a word both
witnesses read is as often a reading of Vaticanus as a word the transcription lost.

The page is the Internet Archive's scan of Swete's three volumes, and what reads it here is the
archive's own OCR of those scans (Tesseract with Greek, the `_djvu.xml` beside each scan), which
owes nothing to First1KGreek's transcription. Each verse is found in that OCR by its words, aligned
word for word, and every gap is one of:

  present    the OCR reads, between the two words the transcription runs together, exactly the
             words the witnesses fill: the page prints them and the transcription lost them. They are
             restored in the page's spelling, provided it is a spelling Swete's text prints elsewhere;
             otherwise the gap is listed for the page image to settle.
  absent     the OCR reads the two words side by side with nothing between them: the page prints the
             verse as the transcription does, and the gap is Vaticanus's reading, not a loss.
  other      the OCR has something else between them — other words, a page break, a margin note
             read into the line — listed with what it reads.
  unlocated  the verse, or one of the two words around the gap, is not found in the OCR.

A gap at the edge of a verse whose words open or close the verse beside it in the transcription is
a division, not a loss, and is not looked for on the page.

Only `present` becomes a restoration. They are written, in the order each verse needs them, to
Essenthos.Forge/Swete/SwetePage.json with the volume and printed page they were read on, and with
any the page image settled by hand (--settled) kept.

  python scripts/swete-scans.py --report FILE --settled scripts/swete-page-read.tsv
                                [--books 01,02] [--dry] [--crops FOLDER [--sample N]]

--settled is the TSV of gaps settled by reading the page image where the OCR could not: book, verse,
the gap's position, the printed words (or nothing, where the page prints the verse without them),
volume and scan leaf. --crops writes the page lines around every gap the OCR did not settle, six to
an image, for reading them. A verse is looked for only in its own volume, and between the verses
around it that were found in the book's order, since formulas repeat. A worktree has no corpus of
its own: --resources points at the main checkout's.
"""

import argparse, bisect, collections, difflib, importlib.util, json, os, re, sys, unicodedata
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8')
repository = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

spec = importlib.util.spec_from_file_location('corrections', os.path.join(repository, 'scripts', 'swete-corrections.py'))
corrections = importlib.util.module_from_spec(spec)
spec.loader.exec_module(corrections)
fold, key, bare = corrections.fold, corrections.key, corrections.bare

# The Internet Archive's items, with the printing each scans.
VOLUMES = [
    (1, 'oldtestamentingr0001henr', '1901'),
    (2, 'oldtestamentingr02swetuoft', '1896'),
    (3, 'theoldtestamenti03swetuoft', '1905'),
]

# The volume each book is printed in, by the number its file opens with. A verse is looked for only
# there: the histories repeat one another, and 2 Chronicles found in 3 Kingdoms is the wrong page.
HOLDS = {**{n: 1 for n in range(1, 15)}, **{n: 2 for n in (*range(15, 23), *range(27, 35))},
         **{n: 3 for n in (*range(23, 27), *range(35, 60))}}

MARKS = set('̀́͂̓̔̈ͅ')
PUNCTUATION = {':': '·', ';': ';', '·': '·', '.': '.', ',': ',', ';': ';'}
LEADING = '"“”‘’\'«(['


SPACING = {'᾿': '̓', '᾽': '̓', '’': '̓', '῾': '̔', '‘': '̔',
           '῍': '̓̀', '῎': '̓́', '῏': '̓͂',
           '῝': '̔̀', '῞': '̔́', '῟': '̔͂', '΄': '́'}

# Words printed without accent or breathing: the enclitics. Any other unmarked word of two letters or
# fewer is a mark the OCR read as a letter.
ENCLITICS = {'με', 'σε', 'τε', 'γε', 'μου', 'σου', 'μοι', 'σοι'}


def normalise(s):
    """The OCR's word in the characters Swete's text uses: a breathing written before a capital goes on it."""
    s = s.replace('µ', 'μ')
    m = re.match(r'^([᾿᾽’῾‘῍-῏῝-῟΄]+)(\w)(.*)$', s)
    if m and m.group(2) in 'ΑΕΗΙΟΥΩΡ':
        s = m.group(2) + ''.join(SPACING[c] for c in m.group(1)) + m.group(3)
    return unicodedata.normalize('NFC', s)


def noise(word):
    f = greek_fold(word)
    return len(f) <= 2 and not marked(word) and f not in ENCLITICS


def is_greek_letter(c):
    return 'GREEK' in unicodedata.name(c, '') and unicodedata.category(c).startswith('L')


def greek_fold(s):
    """The fold, of the Greek letters only: the OCR's Latin letters are its misreadings."""
    s = unicodedata.normalize('NFD', s)
    return ''.join(c for c in s if is_greek_letter(c)).lower().replace('ς', 'σ')


def marked(word):
    return any(c in MARKS for c in unicodedata.normalize('NFD', word))


def main_text(words):
    """
    Whether an OCR line is the edition's text rather than its apparatus, a running head, or the
    accents the OCR reads as a line of their own. The text is accented and the apparatus is not.
    """
    greek = [w for w in words if len(greek_fold(w)) >= 2]
    if not greek:
        return False
    return sum(1 for w in greek if marked(w)) / len(greek) >= 0.6


def read_volume(resources, identifier):
    folder = os.path.join(resources, 'SweteScans')
    numbers = json.load(open(os.path.join(folder, identifier + '_page_numbers.json'), encoding='utf-8'))
    printed = {p['leafNum']: p['pageNumber'] for p in numbers['pages']}
    stream = []
    line_number = 0
    dropped = set()
    for _, el in ET.iterparse(os.path.join(folder, identifier + '_djvu.xml')):
        if el.tag != 'OBJECT':
            continue
        leaf = int(re.search(r'_(\d+)\.djvu$', el.get('usemap')).group(1))
        for line in el.iter('LINE'):
            line_number += 1
            words = [(w.text or '', w.get('coords')) for w in line.iter('WORD')]
            if not main_text([w for w, _ in words]):
                if any(len(greek_fold(w)) >= 3 for w, _ in words):
                    dropped.add(line_number)
                continue
            for text, coords in words:
                text = normalise(text)
                if greek_fold(text) and noise(text):
                    continue
                if stream and stream[-1]['s'].endswith(('-', '‐')) and stream[-1]['end'] and greek_fold(text):
                    # A word broken over the line's end.
                    stream[-1].update(s=stream[-1]['s'][:-1] + text, f=greek_fold(stream[-1]['s'][:-1] + text), end=False)
                    continue
                stream.append({'s': text, 'f': greek_fold(text), 'leaf': leaf, 'page': printed.get(leaf) or '',
                               'box': coords, 'end': False, 'line': line_number})
            if stream:
                stream[-1]['end'] = True
        el.clear()
    return stream, dropped


def similar(a, b):
    if a == b:
        return 2.0
    if min(len(a), len(b)) < 3:
        return 0.0
    return 1.0 if difflib.SequenceMatcher(None, a, b, autojunk=False).ratio() >= 0.75 else 0.0


def align(xs, ys):
    """Word-for-word alignment by the greatest weight of equal or near-equal words; pairs (i, j)."""
    n, m = len(xs), len(ys)
    score = [[0.0] * (m + 1) for _ in range(n + 1)]
    for i in range(n - 1, -1, -1):
        for j in range(m - 1, -1, -1):
            best = max(score[i + 1][j], score[i][j + 1])
            if xs[i] and ys[j]:
                s = similar(xs[i], ys[j])
                if s:
                    best = max(best, s + score[i + 1][j + 1])
            score[i][j] = best
    pairs, i, j = {}, 0, 0
    while i < n and j < m:
        if xs[i] and ys[j] and similar(xs[i], ys[j]) and score[i][j] == similar(xs[i], ys[j]) + score[i + 1][j + 1]:
            pairs[i] = j
            i, j = i + 1, j + 1
        elif score[i][j] == score[i + 1][j]:
            i += 1
        else:
            j += 1
    return pairs


def longest_increasing(found):
    """The longest run of (verse, position) pairs whose positions rise with the verses."""
    tails, links, previous = [], [], [None] * len(found)
    for k, (_, p) in enumerate(found):
        at = bisect.bisect_left(tails, p)
        if at == len(tails):
            tails.append(p)
            links.append(k)
        else:
            tails[at] = p
            links[at] = k
        previous[k] = links[at - 1] if at > 0 else None
    run, k = [], links[-1] if links else None
    while k is not None:
        run.append(found[k])
        k = previous[k]
    return run[::-1]


class Page:
    def __init__(self, resources):
        self.streams = {}
        self.dropped = {}
        self.span = None
        self.index = collections.defaultdict(list)
        for volume, identifier, _ in VOLUMES:
            path = os.path.join(resources, 'SweteScans', identifier + '_djvu.xml')
            if not os.path.exists(path) or os.path.getsize(path) < 1_000_000:
                continue
            stream, dropped = read_volume(resources, identifier)
            self.streams[volume] = stream
            self.dropped[volume] = dropped
            folded = [t['f'] for t in stream]
            for p in range(len(folded) - 2):
                if all(folded[p:p + 3]):
                    self.index[tuple(folded[p:p + 3])].append((volume, p))

    def within(self, book, verses):
        """
        Where each verse of a book may be looked for: between the verses before and after it that
        were found in order. Formulas repeat — the offerings of Numbers 7, the Decalogue in Exodus
        and Deuteronomy, the histories within a volume — and a verse found in the wrong one is a
        verse settled against the wrong page, so a place is trusted only as part of the longest run
        of verses found in the order the book gives them.
        """
        self.span = None
        volume = HOLDS[int(book[:2])]
        found = []
        for i, words in enumerate(verses):
            place = self.locate([greek_fold(t) for t in words], volume) if len(words) >= 6 else None
            if place:
                found.append((i, place[1]))
        ordered = longest_increasing(found)
        self.bounds = []
        for i in range(len(verses)):
            before = max((p for j, p in ordered if j < i), default=None)
            after = min((p for j, p in ordered if j > i), default=None)
            self.bounds.append((before - 300 if before is not None else 0,
                                after + 300 if after is not None else 10 ** 9))

    def around(self, i):
        self.span = self.bounds[i]

    def locate(self, words, volume):
        """Where in the volume's OCR a run of folded words stands: (volume, position of its first word)."""
        votes = collections.Counter()
        for i in range(len(words) - 2):
            hits = [h for h in self.index.get(tuple(words[i:i + 3]), [])
                    if h[0] == volume and (self.span is None or self.span[0] <= h[1] <= self.span[1])]
            if len(hits) > 20:
                continue
            for v, p in hits:
                votes[(v, p - i)] += 1
        if not votes:
            return None
        # Neighbouring offsets are the same place with a word more or less on either side.
        merged = collections.Counter()
        for (volume, p), n in votes.items():
            for d in range(-3, 4):
                merged[(volume, p + d)] += n
        (volume, p), n = merged.most_common(1)[0]
        return (volume, p) if n >= 2 else None


def settle(page, volume, before, words, after, gaps):
    """
    What the page reads at each gap of a verse: {at: (outcome, volume, [OCR words between], [the
    OCR words either side, as near the gap as the alignment found them])}.
    """
    ext = before + words + after
    folded = [key(greek_fold(t)) if greek_fold(t) else '' for t in ext]
    place = page.locate([greek_fold(t) for t in ext], volume)
    if place is None:
        return {at: ('unlocated', None, [], []) for at in gaps}
    volume, p = place
    stream = page.streams[volume]
    lo = max(0, p - 12)
    window = stream[lo:p + len(ext) + sum(len(w) for w in gaps.values()) + 12]
    pairs = align(folded, [key(t['f']) if t['f'] else '' for t in window])
    out = {}
    for at, missing in gaps.items():
        left, right = len(before) + at - 1, len(before) + at
        near = [window[pairs[i]] for i in (max((i for i in pairs if i <= left), default=None),
                                           min((i for i in pairs if i >= right), default=None)) if i is not None]
        if left not in pairs or right not in pairs or not (left - 1 in pairs or right + 1 in pairs)                 or pairs[right] <= pairs[left]:
            out[at] = ('unlocated', volume, [], near)
            continue
        between = window[pairs[left] + 1:pairs[right]]
        read = [t for t in between if t['f']]
        if any(not t['f'] and re.search(r'[^\W\d_]', t['s']) for t in between):
            out[at] = ('other', volume, between, near)
        elif not read and any(n in page.dropped[volume] for n in range(near[0]['line'] + 1, near[1]['line'])):
            # A line between the two that did not read as the text may hold the words.
            out[at] = ('other', volume, between, near)
        elif not read:
            # Nothing between but a margin number or a mark.
            out[at] = ('absent', volume, near, near)
        elif len(read) == len(missing) and all(similar(key(t['f']), m) for t, m in zip(read, missing)):
            out[at] = ('present', volume, read, near)
        else:
            out[at] = ('other', volume, between, near)
    return out


def elsewhere(missing, neighbour, reverse=False):
    """
    Whether words missing at a verse's edge stand at the facing edge of the verse beside it: the
    transcription divided the verses otherwise, and nothing is lost.
    """
    wanted = missing[::-1] if reverse else missing
    n = min(len(wanted), len(neighbour))
    alike = [bool(similar(m, key(greek_fold(t)))) for m, t in zip(wanted[:n], neighbour[:n])]
    # A misread word among several is still the same run of words.
    return n > 0 and alike[0] and sum(alike) >= (n if n < 3 else n - 1)


def surface(token):
    """The OCR's word as the edition prints it: its punctuation in Swete's characters, no stray marks before it."""
    s = token['s'].lstrip(LEADING + '0123456789')
    end = len(s)
    while end > 0 and not (unicodedata.category(s[end - 1]).startswith(('L', 'M'))):
        end -= 1
    word, tail = s[:end], s[end:]
    tail = ''.join(PUNCTUATION.get(c, '') for c in tail)
    return word, tail


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--resources', default=os.path.join(repository, 'Resources'))
    parser.add_argument('--report', required=True)
    parser.add_argument('--books')
    parser.add_argument('--settled')
    parser.add_argument('--crops', help='a folder to write the page images of every gap the OCR did not settle')
    parser.add_argument('--dry', action='store_true', help='report and crop without writing SwetePage.json')
    parser.add_argument('--sample', type=int, help='with --crops, also every n-th gap the OCR did settle')
    args = parser.parse_args()

    swete = os.path.join(args.resources, 'Swete')
    books = sorted(f[:-4] for f in os.listdir(swete) if f.endswith('.txt') and f[:-4] not in corrections.NOT_READ)
    runs = {book: corrections.swete_runs(swete, book) for book in books}
    witnesses = {book: (corrections.glaux(os.path.join(args.resources, 'Glaux', 'xml'), book[:2]),
                        corrections.brenton(os.path.join(args.resources, 'Septuagint'), corrections.BRENTON.get(book[:2])))
                 for book in books}
    fixes = json.load(open(os.path.join(repository, 'Essenthos.Forge', 'Swete', 'SweteCorrections.json'), encoding='utf-8'))
    excluded = corrections.hand_restored(os.path.join(repository, 'Essenthos.Forge'))

    # A spelling the page gives is taken only where Swete's own text prints it somewhere.
    spellings = collections.Counter()
    for book in books:
        for _, tokens in runs[book]:
            for t in tokens:
                spellings[unicodedata.normalize('NFC', bare(t))] += 1

    wanted = set(args.books.split(',')) if args.books else None
    page = Page(args.resources)
    printings = {volume: (identifier, year) for volume, identifier, year in VOLUMES}

    settled_by_hand = {}
    if args.settled:
        for line in (l for path in args.settled.split(',') for l in open(path, encoding='utf-8')):
            if line.startswith('#') or not line.strip():
                continue
            book, verse, at, printed, volume, leaf = line.rstrip('\n').split('\t')
            settled_by_hand[(book, verse, int(at))] = (printed, int(volume), int(leaf))

    report, entries, to_look = [], [], []
    outcomes = collections.Counter()
    for book in books:
        if wanted and book[:2] not in wanted:
            continue
        skip = corrections.moved(runs[book])
        page.within(book, [tokens for _, tokens in runs[book]])
        records = [r for r in corrections.short_verses([book], runs, witnesses, fixes)]
        neighbours = {r['ref']: i for i, (r_ref, _) in enumerate(runs[book]) for r in [{'ref': r_ref}]}
        for record in records:
            if not record['gaps']:
                continue
            ref, chapter, verse = record['ref'], record['chapter'], record['verse']
            gaps = {at: list(w) for at, w in record['gaps'].items()}
            words = record['words']
            if ref in skip or not chapter.isdigit() or chapter == '0' or (book, chapter, verse) in excluded:
                for at in gaps:
                    outcomes['not addressable'] += 1
                    report.append((book, f'{chapter}:{verse}', at, 'not addressable', '', '', ' '.join(gaps[at]), ''))
                continue
            i = neighbours[ref]
            page.around(i)
            before = [t for t in (runs[book][i - 1][1] if i > 0 else []) if fold(t)][-4:]
            after = [t for t in (runs[book][i + 1][1] if i + 1 < len(runs[book]) else []) if fold(t)][:4]
            result = settle(page, HOLDS[int(book[:2])], before, words, after, gaps)
            for at, missing in gaps.items():
                if elsewhere(missing, before[::-1] if at == 0 else after if at == len(words) else [],
                             reverse=at == 0):
                    result[at] = ('in the neighbouring verse', None, [], [])

            text = ' '.join(runs[book][i][1])
            for c in fixes:
                if (c['book'], c['chapter'], c['verse']) == (book, chapter, verse):
                    text = text.replace(c['digitised'], c['printed'], 1)
            tokens = text.split(' ')
            inserted = 0
            for at in sorted(gaps):
                outcome, volume, read, near = result[at]
                where = read[0] if read else None
                hand = settled_by_hand.get((book, f'{chapter}:{verse}', at))
                printed = None
                if hand is not None:
                    outcome = 'present (image)' if hand[0] else 'absent (image)'
                    volume, leaf = hand[1], hand[2]
                    printed = hand[0].split(' ') if hand[0] else None
                    where = {'leaf': leaf, 'page': page_number(page, volume, leaf)}
                elif outcome == 'present':
                    forms = [surface(t) for t in read]
                    printed_there = {unicodedata.normalize('NFC', bare(w)) for w in witness_words(witnesses[book], chapter, verse)}
                    if all(w in spellings or w in printed_there for w, _ in forms):
                        printed = [w + tail for w, tail in forms]
                    else:
                        outcome = 'present, spelling unsure'
                outcomes[outcome] += 1
                if args.crops and not outcome.endswith('(image)') and (outcome not in ('present', 'absent') or (outcome == 'absent' and args.sample and outcomes['absent'] % args.sample == 0)
                                   or (outcome == 'present' and args.sample and outcomes['present'] % args.sample == 0)) and near:
                    to_look.append(((book, f'{chapter}:{verse}', at, outcome, ' '.join(gaps[at]),
                                     ' '.join(words[max(0, at - 4):at]) + ' [·] ' + ' '.join(words[at:at + 4]),
                                     ' '.join(t['s'] for t in read)), volume, near))
                report.append((book, f'{chapter}:{verse}', at, outcome, volume or '',
                               f"{where['page']}/{where['leaf']}" if where else '',
                               ' '.join(gaps[at]), ' '.join(t['s'] for t in read)))
                if printed is None:
                    continue
                kept = [k for k, t in enumerate(tokens) if fold(t)]
                position = kept[at + inserted] if at + inserted < len(kept) else kept[-1] + 1
                entries.append(entry(book, chapter, verse, tokens, position, printed, volume, where, printings))
                tokens = tokens[:position] + printed + tokens[position:]
                inserted += len(printed)

    with open(args.report, 'w', encoding='utf-8', newline='\n') as f:
        f.write('book\tverse\tat\toutcome\tvolume\tpage/leaf\tthe witnesses\tthe OCR between\n')
        for row in report:
            f.write('\t'.join(str(c) for c in row) + '\n')
    out = os.path.join(repository, 'Essenthos.Forge', 'Swete', 'SwetePage.json')
    if args.dry:
        print(f'{sum(outcomes.values())} gaps: ' + ', '.join(f'{n} {k}' for k, n in outcomes.most_common()))
        if args.crops:
            crops(args.resources, args.crops, to_look, printings)
        return
    kept = []
    if wanted and os.path.exists(out):
        kept = [e for e in json.load(open(out, encoding='utf-8')) if e['book'][:2] not in wanted]
    entries = kept + entries
    entries.sort(key=lambda e: (e['book'], int(e['chapter']), int(re.match(r'\d+', e['verse']).group()), e['verse']))
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('[\n' + ',\n'.join('  ' + json.dumps(e, ensure_ascii=False) for e in entries) + '\n]\n')
    print(f'{sum(outcomes.values())} gaps: ' + ', '.join(f'{n} {k}' for k, n in outcomes.most_common()))
    print(f'{len(entries)} restorations in {os.path.relpath(out, repository)}')
    if args.crops:
        crops(args.resources, args.crops, to_look, printings)


def witness_words(held, chapter, verse):
    g, b = held
    return g.get((chapter, verse), []) + b.get((chapter, verse), [])


def leaf_image(resources, identifier, leaf):
    """One page of the scan, fetched from the Internet Archive's item once and kept beside the OCR."""
    import urllib.request
    from PIL import Image
    folder = os.path.join(resources, 'SweteScans', 'pages')
    os.makedirs(folder, exist_ok=True)
    name = f'{identifier}_{leaf:04d}.jp2'
    path = os.path.join(folder, name)
    if not os.path.exists(path):
        url = f'https://archive.org/download/{identifier}/{identifier}_jp2.zip/{identifier}_jp2%2F{name}'
        with urllib.request.urlopen(url, timeout=120) as response, open(path + '.part', 'wb') as f:
            f.write(response.read())
        os.replace(path + '.part', path)
    return Image.open(path)


def crops(resources, folder, to_look, printings):
    """The lines of the page around each gap, six to an image, numbered as the index beside them lists them."""
    from PIL import Image, ImageDraw
    os.makedirs(folder, exist_ok=True)
    width = 1000
    pieces, volumes, leaves_of = [], {}, {}
    for number, (row, volume, near) in enumerate(to_look, 1):
        volumes[number] = volume
        leaves_of[number] = ','.join(sorted({str(t['leaf']) for t in near}, key=int))
        identifier = printings[volume][0]
        boxes = [(t['leaf'], [int(v) for v in t['box'].split(',')]) for t in near]
        parts = []
        leaves = sorted({leaf for leaf, _ in boxes})
        for leaf in leaves:
            image = leaf_image(resources, identifier, leaf)
            ys = [(b[3], b[1]) for l, b in boxes if l == leaf]
            top, bottom = min(y[0] for y in ys) - 45, max(y[1] for y in ys) + 45
            if len(leaves) == 2:
                top, bottom = (top, bottom + 90) if leaf == leaves[0] else (top - 90, bottom)
            if len(boxes) == 1:
                top, bottom = top - 90, bottom + 90
            part = image.crop((40, max(0, top), image.width - 40, min(image.height, bottom))).convert('L')
            parts.append(part.resize((width, max(1, part.height * width // part.width))))
        pieces.append((number, row, parts))
    with open(os.path.join(folder, 'index.tsv'), 'w', encoding='utf-8', newline=chr(10)) as f:
        for number, row, _ in pieces:
            f.write(chr(9).join([str(number)] + [str(c) for c in row] + [str(volumes[number]), leaves_of[number]]) + chr(10))
    for start in range(0, len(pieces), 6):
        group = pieces[start:start + 6]
        height = sum(18 + sum(p.height for p in parts) for _, _, parts in group)
        sheet = Image.new('L', (width, height), 255)
        draw = ImageDraw.Draw(sheet)
        y = 0
        for number, _, parts in group:
            draw.rectangle((0, y, width, y + 17), fill=0)
            draw.text((6, y + 3), f'#{number}', fill=255)
            y += 18
            for part in parts:
                sheet.paste(part, (0, y))
                y += part.height
        sheet.save(os.path.join(folder, f'sheet-{start // 6 + 1:03d}.png'))


def page_number(page, volume, leaf):
    for t in page.streams.get(volume, []):
        if t['leaf'] == leaf and t['page']:
            return t['page']
    return ''


def entry(book, chapter, verse, tokens, position, printed, volume, where, printings):
    """The words either side of the gap, lengthened until they name one place in the verse."""
    lo, hi = max(0, position - 1), min(len(tokens), position + 1)
    while sum(1 for p in range(len(tokens)) if tokens[p:p + hi - lo] == tokens[lo:hi]) > 1:
        lo, hi = max(0, lo - 1), min(len(tokens), hi + 1)
    identifier, year = printings[volume]
    return {
        'book': book, 'chapter': chapter, 'verse': verse,
        'digitised': ' '.join(tokens[lo:hi]),
        'printed': ' '.join(tokens[lo:position] + printed + tokens[position:hi]),
        'volume': volume, 'printing': year, 'page': where['page'], 'leaf': where['leaf'], 'scan': identifier,
    }


if __name__ == '__main__':
    main()
