"""
The corrections this project makes to bible4u's Synodal, written out one by one so each can be read.

bible4u's RUSV.xml is the 1876 Synodal, and in a handful of places it prints what no printing of that
translation has. The Strong-tagged digitisation of the same translation (Resources/SynodalStrong,
swmail/RST) is a second witness to the same text, and where the two disagree about the letters, not
about the translation, the tagged one reads as the Synodal does:

  glued    words run together — Тирянинбыл, Иорданаразделилась — where the tagged edition prints the
           same letters as separate words in the verse at that place. Every one of them in five
           chapters, Joshua 4, 1 Kings 7, Esther 6 and Isaiah 28 and 51, where the digitisation lost
           its spaces throughout, and one more at Mark 10:28. Outside those the same test finds only
           оттого and Заиорданскою, which are the file's orthography and stay; and where the tagged
           edition divides a word the file prints whole elsewhere (наподобие), the file's spelling stands.
  dash     a dash the digitisation wrote as the letter г (or д), alone or run into the next word, in the
           same five chapters, and the hyphen of из-за, пол-локтя and восточно-южной written the same way.
  letters  a word neither edition prints: сщставляли and произщшли (Genesis 10), Бой мой for Бог мой
           (Psalm 59:10), ОТРАСЛЪ with a hard sign (Zechariah 3:8, 6:12), a stray 4 (Psalm 15:3).
  case     four names in lower case: дедан, аккад, ресен (Genesis 10), факей (Isaiah 7:1).

The letters, dashes and names were read one by one and are listed here by hand; the glue is found.
The list is Essenthos.Forge/Loading/SynodalCorrections.json, which the reader applies on a cold load
and TextRepairLoader on a warm one.

  python scripts/synodal-corrections.py [Resources folder]
"""
import collections
import difflib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
RESOURCES = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'Resources')
OUT = os.path.join(HERE, '..', 'Essenthos.Forge', 'Loading', 'SynodalCorrections.json')

OSIS = ['Gen', 'Exod', 'Lev', 'Num', 'Deut', 'Josh', 'Judg', 'Ruth', '1Sam', '2Sam', '1Kgs', '2Kgs', '1Chr', '2Chr',
        'Ezra', 'Neh', 'Esth', 'Job', 'Ps', 'Prov', 'Eccl', 'Song', 'Isa', 'Jer', 'Lam', 'Ezek', 'Dan', 'Hos', 'Joel',
        'Amos', 'Obad', 'Jonah', 'Mic', 'Nah', 'Hab', 'Zeph', 'Hag', 'Zech', 'Mal', 'Matt', 'Mark', 'Luke', 'John',
        'Acts', 'Rom', '1Cor', '2Cor', 'Gal', 'Eph', 'Phil', 'Col', '1Thess', '2Thess', '1Tim', '2Tim', 'Titus', 'Phlm',
        'Heb', 'Jas', '1Pet', '2Pet', '1John', '2John', '3John', 'Jude', 'Rev']

# Joshua 4, 1 Kings 7, Esther 6, Isaiah 28 and 51.
GLUE_CHAPTERS = {(6, 4), (11, 7), (17, 6), (23, 28), (23, 51)}
GLUED_ELSEWHERE = {((41, 10, 28), 'ипоследовали')}

# Read one by one. The dash written as г alone is found below; these are the ones run into a word, and
# the words the glue test cannot divide because a letter was lost with the space.
BY_HAND = [
    ((1, 10, 10), 'letters', 'сщставляли', 'составляли'),
    ((1, 10, 13), 'letters', 'произщшли', 'произошли'),
    ((19, 59, 10), 'letters', 'Бой', 'Бог'),
    ((19, 15, 3), 'letters', 'своего4', 'своего'),
    ((38, 3, 8), 'letters', 'ОТРАСЛЪ', 'ОТРАСЛЬ'),
    ((38, 6, 12), 'letters', 'ОТРАСЛЪ', 'ОТРАСЛЬ'),
    ((1, 10, 7), 'case', 'дедан', 'Дедан'),
    ((1, 10, 10), 'case', 'аккад', 'Аккад'),
    ((1, 10, 12), 'case', 'ресен', 'Ресен'),
    ((23, 7, 1), 'case', 'факей', 'Факей'),
    ((11, 7, 39), 'dash', 'восточногюжной', 'восточно-южной'),
    ((17, 6, 13), 'dash', 'изгза', 'из-за'),
    ((11, 7, 35), 'dash', 'полглоктя', 'пол-локтя'),
    ((11, 7, 30), 'glued', 'Начетырех', 'На четырех'),
    ((11, 7, 42), 'glued', 'рядагранатовых', 'ряда гранатовых'),
    ((11, 7, 45), 'glued', 'Хирамцарю', 'Хирам царю'),
    ((17, 6, 9), 'glued', 'первыхкнязей', 'первых князей'),
    ((17, 6, 10), 'glued', 'возьмиодеяние', 'возьми одеяние'),
    ((23, 28, 13), 'glued', 'заповедьна', 'заповедь на'),
    ((23, 51, 7), 'glued', 'сердцезакон', 'сердце закон'),
    ((23, 51, 17), 'glued', 'изруки', 'из руки'),
]

WORD = re.compile(r"\w+(?:[-'’]\w+)*")


def fold(word):
    return word.lower().replace('ё', 'е')


def markup_free(text):
    return re.sub(r'\(\d+(?:[-:]\d+)?\)', '', text.replace('^^', ''))


def bible4u(path):
    verses = {}
    for book in ET.parse(path).getroot().iter('BIBLEBOOK'):
        for chapter in book.iter('CHAPTER'):
            for verse in chapter.iter('VERS'):
                address = (int(book.get('bnumber')), int(chapter.get('cnumber')), int(verse.get('vnumber')))
                verses[address] = ''.join(verse.itertext()).strip()
    return verses


def tagged(path):
    ns = '{http://www.bibletechnologies.net/2003/OSIS/namespace}'
    skipped = (ns + 'note', ns + 'title')

    def text(element):
        parts = [element.text or ''] if element.tag not in skipped else []
        for child in element:
            if child.tag not in skipped:
                parts.append(text(child))
            parts.append(child.tail or '')
        return ''.join(parts)

    verses = {}
    for verse in ET.parse(path).getroot().iter(ns + 'verse'):
        if verse.get('osisID'):
            book, chapter, number = verse.get('osisID').split('.')
            verses[(OSIS.index(book) + 1, int(chapter), int(number))] = re.sub(r'\s+', ' ', text(verse)).strip()
    return verses


def nearest(address, words, edition):
    """The tagged verse the file's verse is: at its address or a neighbour, the Synodal numbering being its own."""
    book, chapter, number = address
    folded = [fold(w) for w in words]
    best, score = None, 0.5
    for c in (chapter, chapter - 1, chapter + 1):
        for n in range(max(0, number - 3), number + 4):
            if (book, c, n) in edition:
                theirs = WORD.findall(edition[(book, c, n)])
                ratio = difflib.SequenceMatcher(a=folded, b=[fold(w) for w in theirs], autojunk=False).ratio()
                if ratio > score:
                    best, score = theirs, ratio
    return best


def glued(address, text, edition):
    """Every token of the verse the tagged edition prints as several words with the same letters."""
    words = WORD.findall(markup_free(text))
    theirs = nearest(address, words, edition)
    if theirs is None:
        return
    mine, other = [fold(w) for w in words], [fold(w) for w in theirs]
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(a=mine, b=other, autojunk=False).get_opcodes():
        if tag != 'replace':
            continue
        j = j1
        for i in range(i1, i2):
            start, joined = j, ''
            while j < j2 and len(joined) < len(mine[i]):
                joined += other[j]
                j += 1
            if joined != mine[i]:
                break
            if j - start >= 2:
                yield words[i], ' '.join(theirs[start:j])


def main():
    file = bible4u(os.path.join(RESOURCES, 'bible4u', 'RUSV.xml'))
    edition = tagged(os.path.join(RESOURCES, 'SynodalStrong', 'RSTE_verse_words.xml'))
    own = collections.Counter(w for address, text in file.items() if address[:2] not in GLUE_CHAPTERS
                              for w in WORD.findall(markup_free(text)))

    corrections = collections.OrderedDict()
    for address, text in sorted(file.items()):
        if address[:2] not in GLUE_CHAPTERS and address not in {a for a, _ in GLUED_ELSEWHERE}:
            continue
        for digitised, printed in glued(address, text, edition):
            if address[:2] not in GLUE_CHAPTERS and (address, digitised) not in GLUED_ELSEWHERE:
                continue
            parts = []
            for part in printed.split(' '):
                if parts and parts[-1] + part in own and parts[-1] + part != digitised:
                    parts[-1] += part
                else:
                    parts.append(part)
            corrections[(address, digitised)] = ('glued', ' '.join(parts))
        if address[:2] in GLUE_CHAPTERS:
            for match in re.finditer(r'(?<![\w-])([гд])(?!\w)', text):
                corrections[(address, match.group(1))] = ('dash', '–')
            for match in re.finditer(r'(?<![\w-])г(четыре|палкою|опустошение)(?!\w)', text):
                corrections[(address, match.group(0))] = ('dash', '– ' + match.group(1))

    for address, kind, digitised, printed in BY_HAND:
        corrections[(address, digitised)] = (kind, printed)

    for (address, digitised), _ in corrections.items():
        if not re.search(r'(?<![\w-])' + re.escape(digitised) + r'(?!\w)', file[address]):
            sys.exit(f'{address} does not print {digitised}: the file is not the one this list was drawn against')

    rows = [{'book': a[0], 'chapter': a[1], 'verse': a[2], 'kind': kind, 'digitised': d, 'printed': p}
            for (a, d), (kind, p) in sorted(corrections.items(), key=lambda item: (item[0][0], item[0][1]))]
    with open(OUT, 'w', encoding='utf-8', newline='\n') as out:
        out.write('[\n' + ',\n'.join('  ' + json.dumps(row, ensure_ascii=False) for row in rows) + '\n]\n')
    print(len(rows), 'corrections in', len({r['book'] * 10**6 + r['chapter'] * 1000 + r['verse'] for r in rows}),
          'verses:', dict(collections.Counter(r['kind'] for r in rows)))


if __name__ == '__main__':
    main()
