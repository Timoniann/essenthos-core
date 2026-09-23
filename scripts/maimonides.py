"""
The 613 commandments as Maimonides counted them, with the verses each rests on, written as
Resources/Maimonides/commandments.tsv for CommandmentLoader.

Two public-domain works, both read from Sefaria, and a numbering taken from a third:

- The numbers are those of Maimonides' Sefer HaMitzvot (248 positive, 365 negative), read from the
  Warsaw 1883 Hebrew edition.
- The wording and the verse references are Moses Hyamson's English (1937-1949) of the list that
  opens the Mishneh Torah. That list is Maimonides' own summary of the same count and follows the
  Sefer HaMitzvot's order except in the places ORDER names, which were read commandment by
  commandment in both.

What this project adds is written down rather than silent, in the note column of each row:

- The verses are in the shared English numbering. Hyamson prints the Hebrew numbering for most of
  the verses where the two differ (Deut. 23:2 for the English 23:1) and the English for a few, so
  every reference is checked against the words he quotes from it in the King James text, and moved
  to the English address only where the Hebrew one is what he meant.
- Where he prints no verse, or one his quotation is not in, CORRECTIONS gives ours and says why.

  python scripts/maimonides.py --fetch     download the two works from Sefaria, checking their terms
  python scripts/maimonides.py             write commandments.tsv from the downloads

A worktree has no corpus of its own, so --resources can point at the main checkout's while the file
written is this checkout's:

  python scripts/maimonides.py --resources ../../Resources
"""

import argparse, csv, json, os, re, sys, urllib.parse, urllib.request
sys.stdout.reconfigure(encoding='utf-8')

repository = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
parser = argparse.ArgumentParser(description="Write Maimonides' count of the commandments.")
parser.add_argument('--resources', default=os.path.join(repository, 'Resources'),
                    help="where the King James and the Sefaria downloads are; this repository's by default")
parser.add_argument('--out', default=os.path.join(repository, 'Resources', 'Maimonides', 'commandments.tsv'))
parser.add_argument('--fetch', action='store_true', help='download the sources from Sefaria first')
args = parser.parse_args()

downloads = os.path.join(args.resources, 'Maimonides', 'sefaria')

# What is taken from Sefaria, and the terms Sefaria states for each version. A version whose stated
# licence is not public domain is refused rather than downloaded.
SOURCES = {
    'mishneh-torah-positive': ('Mishneh_Torah,_Positive_Mitzvot', 'english',
                               'The Mishneh Torah by Maimonides. trans. by Moses Hyamson, 1937-1949'),
    'mishneh-torah-negative': ('Mishneh_Torah,_Negative_Mitzvot', 'english',
                               'The Mishneh Torah by Maimonides. trans. by Moses Hyamson, 1937-1949'),
    'sefer-hamitzvot-positive': ('Sefer_HaMitzvot,_Positive_Commandments', 'hebrew', 'Sefer HaMitzvot, Warsaw 1883'),
    'sefer-hamitzvot-negative': ('Sefer_HaMitzvot,_Negative_Commandments', 'hebrew', 'Sefer HaMitzvot, Warsaw 1883'),
}
PUBLIC_DOMAIN = 'Public Domain'

COUNTS = {'positive': 248, 'negative': 365}

# Where the Mishneh Torah's list numbers a commandment differently from the Sefer HaMitzvot:
# Sefer HaMitzvot number -> the list's number. Each was read in both. The king's Torah scroll comes
# before every man's in the Sefer HaMitzvot; the two ransoms are in the other order, as Hyamson's own
# footnote says; and the days of rest run Tabernacles, the eighth day, the Day of Atonement there,
# where the list puts the Day of Atonement first.
ORDER = {
    ('positive', 17): 18, ('positive', 18): 17,
    ('negative', 295): 296, ('negative', 296): 295,
    ('negative', 327): 328, ('negative', 328): 329, ('negative', 329): 327,
}

# Where the verse is ours: keyed by the list's number, the verses in the shared numbering, and why.
CORRECTIONS = {
    ('positive', 71): ('LEV 5:15; LEV 6:2-7; LEV 19:20-21',
                       'the list cites no verse; these are the three trespasses it names'),
    ('positive', 164): ('LEV 16:29', 'the list prints Lev. 16:27; the words it quotes are verse 29'),
    ('negative', 83): ('EXO 30:32', 'the list prints Ex. 30:23-24; the words it quotes are verse 32'),
    ('negative', 137): ('LEV 22:12', 'the list prints Lev. 10:14; the words it quotes are Lev. 22:12'),
    ('negative', 336): ('LEV 18:10', 'the list cites no verse; this is the one it says the rule is learnt from'),
}

USX = {'Gen': 'GEN', 'Exod': 'EXO', 'Lev': 'LEV', 'Num': 'NUM', 'Deut': 'DEU'}
PRINTED = {'gen': 'Gen', 'ex': 'Exod', 'exod': 'Exod', 'lev': 'Lev', 'num': 'Num', 'deut': 'Deut', 'duet': 'Deut'}

# Where the Hebrew numbering of the Torah departs from the English: book, Hebrew chapter, first and
# last Hebrew verse, and the English chapter and verse the first one is.
SHIFTS = [
    ('Gen', 32, 1, 1, 31, 55), ('Gen', 32, 2, 33, 32, 1),
    ('Exod', 7, 26, 29, 8, 1), ('Exod', 8, 1, 28, 8, 5), ('Exod', 21, 37, 37, 22, 1), ('Exod', 22, 1, 30, 22, 2),
    ('Lev', 5, 20, 26, 6, 1), ('Lev', 6, 1, 23, 6, 8),
    ('Num', 17, 1, 15, 16, 36), ('Num', 17, 16, 28, 17, 1), ('Num', 30, 1, 1, 29, 40), ('Num', 30, 2, 17, 30, 1),
    ('Deut', 13, 1, 1, 12, 32), ('Deut', 13, 2, 19, 13, 1), ('Deut', 23, 1, 1, 22, 30), ('Deut', 23, 2, 26, 23, 1),
    ('Deut', 28, 69, 69, 29, 1), ('Deut', 29, 1, 28, 29, 2),
]

# Editions printed with the upper accents make the four short commands of the Decalogue one verse.
DECALOGUE = {('Exod', 20, 13): [13, 14, 15, 16], ('Exod', 20, 14): [17],
             ('Deut', 5, 17): [17, 18, 19, 20], ('Deut', 5, 18): [21]}

STOP = set('the a an and of to in unto thou thee thy shalt shall not be is it that he his him ye you your '
           'for with as all or nor any which them they their from by on upon this these was are i me my '
           'neither no'.split())


def fetch():
    os.makedirs(downloads, exist_ok=True)
    for name, (ref, language, version) in SOURCES.items():
        url = ('https://www.sefaria.org/api/v3/texts/' + urllib.parse.quote(ref)
               + '?version=' + urllib.parse.quote(f'{language}|{version}'))
        with urllib.request.urlopen(url) as response:
            body = json.load(response)
        versions = body.get('versions') or []
        if not versions:
            raise SystemExit(f'Sefaria has no version "{version}" of {ref} any more; read what it offers '
                             f'at https://www.sefaria.org/api/texts/versions/{ref} and decide again.')
        licence = versions[0].get('license')
        if licence != PUBLIC_DOMAIN:
            raise SystemExit(f'Sefaria now says {ref} ({version}) is "{licence}", not public domain. '
                             'Nothing was written: a changed licence is a decision, not a download.')
        with open(os.path.join(downloads, name + '.json'), 'w', encoding='utf-8') as file:
            json.dump(body, file, ensure_ascii=False, indent=1)
        print(f'{name}: {len(versions[0]["text"])} segments, {licence}')


def segments(name):
    with open(os.path.join(downloads, name + '.json'), encoding='utf-8') as file:
        return json.load(file)['versions'][0]['text']


def words(text):
    return re.findall(r'[a-z]+', text.lower())


def read_king_james():
    files = {'Gen': '02-GEN', 'Exod': '03-EXO', 'Lev': '04-LEV', 'Num': '05-NUM', 'Deut': '06-DEU'}
    bible = {}
    for book, file in files.items():
        with open(os.path.join(args.resources, 'KingJames2006', file + 'eng-kjv2006.usfm'), encoding='utf-8') as f:
            text = f.read()
        text = re.sub(r'\\w ([^|\\]*)\|[^\\]*\\w\*', r'\1', text)
        text = re.sub(r'\\f .*?\\f\*', '', text, flags=re.S)
        chapters, chapter = {}, 0
        for match in re.finditer(r'\\c (\d+)|\\v (\d+) ((?:(?!\\[cv] ).)*)', text, re.S):
            if match.group(1):
                chapter = int(match.group(1))
                chapters[chapter] = {}
            else:
                chapters[chapter][int(match.group(2))] = set(words(re.sub(r'\\[a-z0-9+]+\*?', '', match.group(3))))
        bible[book] = chapters
    return bible


KJV = None


def verse_words(book, c1, v1, c2, v2):
    found = set()
    for chapter in range(c1, c2 + 1):
        verses = KJV[book].get(chapter, {})
        last = v2 if chapter == c2 else max(verses or [0])
        for verse in range(v1 if chapter == c1 else 1, last + 1):
            found |= verses.get(verse, set())
    return found


def shares(quote, found):
    """How much of a quotation is in the verse, a word counting where its first letters agree."""
    stems = {w[:5] for w in found}
    return sum(1 for w in quote if w[:5] in stems) / len(quote)


def without_footnotes(segment):
    kept, i = [], 0
    while i < len(segment):
        if segment.startswith('<i class="footnote">', i):
            depth = 0
            while i < len(segment):
                if segment.startswith('<i', i):
                    depth += 1
                    i = segment.index('>', i) + 1
                elif segment.startswith('</i>', i):
                    depth -= 1
                    i += 4
                    if depth == 0:
                        break
                else:
                    i += 1
            continue
        if segment.startswith('<sup', i):
            i = segment.index('</sup>', i) + 6
            continue
        kept.append(segment[i])
        i += 1
    return re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', ''.join(kept))).strip()


REFERENCE = re.compile(
    r'(?:(Gen|Ex|Exod|Lev|Num|Deut|Duet|ibid|Ibid)\.?\s*)?(\d+)\s*[:.\-]\s*(\d+)(?:\s*-\s*(\d+)(?:\s*:\s*(\d+))?)?')
CITATION = re.compile(r'(Gen|Ex|Lev|Num|Deut|Duet|ibid)\.?\s*\d|^\s*ibid\.?\s*$|\d+\s*[:\-]\s*\d+', re.I)


def references(group, previous):
    """The verses one parenthesis cites, as (book, chapter, verse, chapter, verse); ibid. is the last cited."""
    group = re.split(r'authori[sz]ed English version', group)[0]
    if re.fullmatch(r'\s*ibid\.?\s*', group, re.I):
        return [previous] if previous else []
    cited, book = [], None
    for match in REFERENCE.finditer(group):
        named = match.group(1)
        if named and named.lower() != 'ibid':
            book = PRINTED[named.lower()]
        elif book is None and previous:
            book = previous[0]
        chapter, verse = int(match.group(2)), int(match.group(3))
        if match.group(5):
            cited.append((book, chapter, verse, int(match.group(4)), int(match.group(5))))
        elif match.group(4):
            cited.append((book, chapter, verse, chapter, int(match.group(4))))
        else:
            cited.append((book, chapter, verse, chapter, verse))
    return cited


def title_of(text):
    said = re.search(r',?\s*(?:as it is said|for it is said|it is said)', text)
    title = text[:said.start()] if said else re.split(r'\s*[“]', text)[0]
    title = re.sub(r'\s*\([^()]*\d+:\d+[^()]*\)', '', title)
    title = re.split(r'(?<=[a-z)])\.\s+(?=[A-Z])', title)[0]
    title = re.sub(r'^\([^()]*\)\s*', '', title)
    title = re.sub(r'^The first of the (?:positive|negative) precepts is\s+', '', title)
    title = re.sub(r'[,;]?\s*(?:and|or)$', '', title.strip()).strip().rstrip(',;.:').strip()
    return title[0].upper() + title[1:]


def heb_to_eng(book, chapter, verse):
    for shifted, at, first, last, english, start in SHIFTS:
        if book == shifted and chapter == at and first <= verse <= last:
            return english, start + verse - first
    return chapter, verse


def placed(quote, cited):
    """Where one printed reference sits in the English numbering, and whether it had to move."""
    book, c1, v1, c2, v2 = cited
    options = []
    e1, f1 = heb_to_eng(book, c1, v1)
    e2, f2 = heb_to_eng(book, c2, v2)
    if (e1, f1, e2, f2) != (c1, v1, c2, v2):
        options.append(((book, e1, f1, e2, f2), 'the Hebrew numbering'))
    if c1 == c2 and v1 == v2 and (book, c1, v1) in DECALOGUE:
        options += [((book, c1, v, c1, v), 'the Decalogue as one verse') for v in DECALOGUE[(book, c1, v1)]]
    if not quote:
        # Nothing quoted to check it by. A range that runs past the English chapter can only be the
        # Hebrew numbering; anything else is taken as printed.
        if options and options[0][1] == 'the Hebrew numbering' and v2 > max(KJV[book].get(c2, {0: 0})):
            return options[0][0], options[0][1]
        return cited, None
    best, why, score = cited, None, shares(quote, verse_words(*cited))
    for option, reason in options:
        moved = shares(quote, verse_words(*option))
        if moved > score:
            best, why, score = option, reason, moved
    return best, why


def printed_form(cited):
    book, c1, v1, c2, v2 = cited
    tail = '' if (c1, v1) == (c2, v2) else (f'-{v2}' if c1 == c2 else f'-{c2}:{v2}')
    return f'{book}. {c1}:{v1}{tail}'


def canonical_form(cited):
    """USX book, split at chapter ends so each piece is one chapter's verses."""
    book, c1, v1, c2, v2 = cited
    pieces = []
    for chapter in range(c1, c2 + 1):
        first = v1 if chapter == c1 else 1
        last = v2 if chapter == c2 else max(KJV[book][chapter])
        pieces.append(f'{USX[book]} {chapter}:{first}' + ('' if first == last else f'-{last}'))
    return pieces


def listed(kind):
    """The Mishneh Torah's list of one kind, by its own number: title, and the verses with their notes."""
    rows, previous = {}, None
    for number, segment in enumerate(segments(f'mishneh-torah-{kind}')[:COUNTS[kind]], start=1):
        text = without_footnotes(segment)
        cited, notes, first = [], [], True
        # Every parenthesis that follows a quotation cites the verse quoted; the first parenthesis
        # cites the commandment's verse even where nothing is quoted. A parenthesis inside the
        # translator's explanation afterwards is his aside, not the commandment's source.
        for match in re.finditer(r'(“[^”]*”)?\s*\(([^()]*)\)', text):
            group = match.group(2)
            if not CITATION.search(group) or not (first or match.group(1)):
                continue
            first = False
            quote = [w for w in words(match.group(1) or '') if w not in STOP and len(w) > 2]
            for reference in references(group, previous):
                at, why = placed(quote, reference)
                if why:
                    notes.append(f'{printed_form(reference)} in {why} is {" ".join(canonical_form(at))}')
                cited += canonical_form(at)
                previous = reference
        correction = CORRECTIONS.get((kind, number))
        if correction:
            cited, notes = correction[0].split('; '), [f'ours: {correction[1]}']
        rows[number] = (title_of(text), list(dict.fromkeys(cited)), notes)
    return rows


def main():
    global KJV
    if args.fetch:
        fetch()
    KJV = read_king_james()
    out = []
    for kind in ('positive', 'negative'):
        rows = listed(kind)
        counted = segments(f'sefer-hamitzvot-{kind}')[:COUNTS[kind]]
        if len(counted) != COUNTS[kind] or len(rows) != COUNTS[kind]:
            raise SystemExit(f'Expected {COUNTS[kind]} {kind} commandments in both works and found '
                             f'{len(counted)} and {len(rows)}; the downloads are not the editions this was written for.')
        for number in range(1, COUNTS[kind] + 1):
            listed_as = ORDER.get((kind, number), number)
            title, cited, notes = rows[listed_as]
            if listed_as != number:
                opening = ' '.join(re.sub(r'<[^>]+>', '', ''.join(counted[number - 1])).split()[:6])
                print(f'{kind} {number} is the list\'s {listed_as}: "{title}" / {opening}')
            if not cited:
                raise SystemExit(f'{kind} {number} ("{title}") cites no verse and has no correction.')
            out.append([kind, number, listed_as, title, '; '.join(cited), '; '.join(notes)])
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8', newline='') as file:
        writer = csv.writer(file, delimiter='\t', lineterminator='\n')
        writer.writerow(['kind', 'number', 'mishneh_torah', 'title', 'verses', 'note'])
        writer.writerows(out)
    moved = sum(1 for row in out if row[5] and not row[5].startswith('ours'))
    ours = sum(1 for row in out if row[5].startswith('ours'))
    print(f'{len(out)} commandments written to {args.out}: {moved} with a verse moved to the English '
          f'numbering, {ours} with verses of ours')


main()
