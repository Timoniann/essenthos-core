import sys; sys.stdout.reconfigure(encoding="utf-8")
import pickle, collections
Verse = collections.namedtuple('Verse', 'slug book chapter number label id canon words')
OWN_DIVISION = {17, 18, 19, 22, 75, 76, 91, 92}
_s = pickle.load(open('state.pkl', 'rb'))
verses = {k: Verse(*v) for k, v in _s['verses'].items()}
order_in_text = {s: [verses[i] for i in ids] for s, ids in _s['order'].items()}
words_of = collections.defaultdict(list, _s['words'])
assoc = _s['assoc']
agree = _s['agree']
PROPER = _s['proper']
