"""Write the reviewable Ge'ez versification file from final.pkl."""
import pickle, collections, sys
exec(open('derive_load.py', encoding='utf-8').read())
F = pickle.load(open('final.pkl', 'rb'))
names = {}
for line in open('books.tsv', encoding='utf-8'):
    n, name, abbr = line.rstrip('\n').split('\t')
    names.setdefault(int(n), name)

def rows(targets):
    by = collections.OrderedDict()
    for _, c, v in targets: by.setdefault(c, []).append(v)
    parts = []
    for c, vs in by.items():
        if sorted(vs) == list(range(min(vs), max(vs) + 1)) and len(vs) > 1 and vs == sorted(vs):
            parts.append(f'{c}:{vs[0]}-{vs[-1]}')
        else:
            parts.extend(f'{c}:{v}' for v in vs)
    return ';'.join(parts)

lines = F['lines']
per_book = collections.defaultdict(lambda: [0, set()])
for o in lines:
    per_book[o[0].book][0] += 1
    per_book[o[0].book][1].add(o[0].chapter)
moves = [o for o in lines if o[3] != 'none']

head = f"""# Where the verses of the Ethiopic Bible (GEEZ81) stand against the Greek, in the chapters where the
# Ge'ez divides or numbers its verses otherwise than the Greek it was translated from.
#
# No source states this. TVTMS has no Ethiopic tradition, Beta maṣāḥǝft ships no verse map, and the
# reference each verse of the source carries only repeats the verse's own number. So it was worked out,
# on 2026-10-15, by Essenthos from the corpus itself: every Ge'ez chapter was set against the Greek
# verse by verse in order (a monotone alignment that may join two verses to one, one to two, or leave
# one out), scored by the words the two have in common. Which Ge'ez word answers which Greek word was
# learned from the chapters where the two print the same verses: the statistical aligner's own links
# there and the words that keep meeting in the same verse, and names compared letter by letter.
# The Greek is Brenton's Septuagint for the Old Testament and the deuterocanon, and Robinson-Pierpont's
# Byzantine text for the New (Nestle leaves out verses the Ge'ez has, such as Mark 9:44). Rows are the
# shared frame's, the canonical addresses the Greek verse stands at, so a chapter Brenton itself numbers
# otherwise (Jeremiah, 3 Kingdoms 6-7, Exodus 35-40) is answered at the rows Brenton stands at.
#
# A line is written only where the verse does not stand at its own number's row. A verse not listed
# stands where the frame puts it. A chapter is read as moved only where the alignment found it clearly:
# a run of moved verses is kept when the words gain at least 8 points over the verses' own numbers, or
# at least 4 over a run of three or more; {F['gated_out']} verses the alignment would have moved on less
# are left at their numbers and are not listed.
#
# Columns, tab-separated:
#   book      the canonical book number
#   geez      the Ge'ez verse, chapter:verse as the edition numbers it
#   greek     the Greek verse(s) it answers, at the frame's rows; the first is where it stands;
#             '-' where the Greek has no counterpart, and then it stands with the verse before it
#   relation  equals   one verse against one
#             part-of  this verse and its neighbour together are the one Greek verse
#             spans    this verse holds more than one Greek verse
#             divided  this verse and its neighbour hold the same Greek verses, divided at another place
#             none     the Greek has nothing for it (Brenton follows Vaticanus, which lacks passages)
#   share     of this verse's words that match either the Greek verse(s) named or the verse at its own
#             number, how much matches the ones named (weighted by how sure each match is); '-' where
#             there is nothing to weigh
#   words     how many of its words found a match at all
#
# Summary: {len(lines)} lines in {len({(o[0].book, o[0].chapter) for o in lines})} chapters of {len(per_book)} books;
# {len(moves)} verses stand at another row ({sum(o[3]=='equals' for o in moves)} one against one, {sum(o[3]=='part-of' for o in moves)} part of a Greek verse,
# {sum(o[3]=='spans' for o in moves)} holding more than one, {sum(o[3]=='mixed' for o in moves)} divided at another place), and {sum(o[3]=='none' for o in lines)} have no Greek counterpart.
#
# Books with no Greek counterpart in the corpus, so not mapped: {', '.join(names[b] for b in F['no_greek'] if b != 27)};
# and the end of Daniel 14. The books the church divides in a way of its own (Esther, Job, the Psalter,
# the Song, Wisdom, the Letter of Jeremiah, Messale and Tägsas) are read in
# Essenthos.Forge/BetaMasaheft/GeezVerseMap.tsv, not here.
#
# Checked by reading: 1 Chronicles 7, the case that started this (Ge'ez 7:34 is 7:30, the sons of Asher;
# 7:41 is 7:37; 7:2-3 are the Greek 7:2), Numbers 1 (Ge'ez 1:4 is the end of 1:3, so 1:5 is 1:4, Reuben's
# Elizur), Mark 9 (Ge'ez 9:5 holds 9:5-6, so 9:6 is the cloud of 9:7), Mark 6 (6:8 holds the sandals of
# 6:9; 6:11 is the Sodom and Gomorrah saying the Byzantine prints in 6:11), 1 Chronicles 16 (the psalm one
# verse on from 16:9), Tobit 11 (Ge'ez 11:1 is Raphael's word to Tobias, the Greek 11:2). Of 28 lines
# drawn at random and read against the Greek, 27 were right. The bar leaves out some that are right:
# 2 Chronicles 13:23 is the Greek 14:1, Abijah's burial, a single verse with too little to go on.
# The same alignment run on the books GeezVerseMap.tsv reads verse by verse named a row that the
# reading also names for 90% of the verses of Job, 88% of the Song and 95% of Wisdom.
#
# book\tgeez\tgreek\trelation\tshare\twords
"""
RELATION = {'mixed': 'divided'}
body = []
current = None
for o in sorted(lines, key=lambda o: (o[0].book, o[0].chapter, o[0].number, o[0].label)):
    v = o[0]
    if v.book != current:
        current = v.book
        n, chs = per_book[v.book]
        body.append(f'# {names[v.book]}: {n} lines in {len(chs)} chapter{"s" if len(chs) > 1 else ""}')
    share = '-' if o[3] == 'none' or o[7][0] is None else f'{o[7][0]:.2f}'
    greek = '-' if o[3] == 'none' else rows(o[1])
    body.append(f'{v.book}\t{v.chapter}:{v.number}{v.label}\t{greek}\t{RELATION.get(o[3], o[3])}\t{share}\t{o[7][1]}')
open(sys.argv[1], 'w', encoding='utf-8', newline='\n').write(head + '\n'.join(body) + '\n')
print(len(body), 'written')
