"""PRB-0571: the reading's batches. In: selected.json (select.py), verses.csv (BHSA, NESTLE1904 and KJV text of every
verse by its canonical address, exported from the copy of 2026-10-08) and records.csv (the candidates' names and lines).
Out: batches.json — the selected words grouped by verse, the verses of one chapter kept together, about 25 words a
batch, each verse with its original and King James text and one verse either side."""
import csv, json
from collections import OrderedDict
from pathlib import Path

HERE = Path(__file__).parent
CODES = ["GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH",
         "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON",
         "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL",
         "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN",
         "JUD", "REV"]
PER_BATCH = 25

verses = {}
for slug, b, c, v, text in csv.reader(open(HERE / "verses.csv", encoding="utf-8")):
    verses[(slug, int(b), int(c), int(v))] = " ".join(text.split())
records = {slug: {"slug": slug, "name": name, "is": "a person" if kind == "person" else "a people (tribe or nation)",
                  "line": line}
           for slug, kind, name, line in csv.reader(open(HERE / "records.csv", encoding="utf-8"))}


def code(b):
    return CODES[int(b) - 1]


def key(w):
    """The word as its witness numbers it: text, book, chapter:verse and label, position."""
    return f"{w['text']} {code(w['b'])} {w['c']}:{w['n']}{w['l']} #{w['p']}"


def around(slug, b, c, v):
    return {"before": verses.get((slug, b, c, v - 1)), "verse": verses.get((slug, b, c, v)),
            "after": verses.get((slug, b, c, v + 1))}


selected = json.loads((HERE / "selected.json").read_text(encoding="utf-8"))
groups = OrderedDict()
for w in sorted(selected, key=lambda w: (int(w["cb"]), int(w["cc"]), int(w["cv"]), w["text"], int(w["p"]))):
    groups.setdefault((w["text"], int(w["cb"]), int(w["cc"]), int(w["cv"])), []).append(w)

batches, current, size = [], [], 0
last_chapter = None
for (slug, b, c, v), words in groups.items():
    if current and (size + len(words) > PER_BATCH or (size >= PER_BATCH // 2 and (b, c) != last_chapter)):
        batches.append(current)
        current, size = [], 0
    current.append({
        "verse": f"{code(b)} {c}:{v}",
        "original": around(slug, b, c, v),
        "kjv": around("KJV", b, c, v),
        "words": [{"key": key(w), "word": w["surface"], "position": int(w["p"]),
                   "man": records[w["man"]], "people": records[w["people"]]} for w in words]})
    size += len(words)
    last_chapter = (b, c)
if current:
    batches.append(current)

out = [{"id": f"b{i:03d}-{items[0]['verse'].replace(' ', '-').replace(':', '-')}", "verses": items}
       for i, items in enumerate(batches, 1)]
(HERE / "batches.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(selected), "words,", len(groups), "verses,", len(out), "batches")
