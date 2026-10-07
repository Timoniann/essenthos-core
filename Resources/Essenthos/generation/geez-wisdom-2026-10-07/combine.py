"""Combine the accepted chapter answers and the lead's corrections from the second reading into one map.

Greek references are book-chapter:verse as GRCBRENT serves them through /v1/text — by its verse number, which
repeats where Brenton prints a plus-verse (Job 2:9, 19:4, 23:15, 36:28, 42:17, Ps 144:13) and once looks wrong
(Ps 115:4 twice, PRB-0972). A number that repeats means every row served with it.
"""
import json
from pathlib import Path

HERE = Path(__file__).parent
SOURCE = ("read verse by verse against Brenton's Greek by gpt-6.1-sol (OpenAI, through Codex, reasoning high) on "
          "2026-10-07; every line below 0.9, every line with no Greek and a 10% sample of the rest read again by "
          "Claude (Anthropic): 818 of 827 agreed, the rest corrected by the lead")

# (book, geez chapter) -> replacement lines for those Ge'ez verses: (geez, greek "ch:v[-v]" or "", confidence, note)
FIXES = {
    ("psalms", 92): {"4": None, "5": None, "4-5": ("92:3", 0.85, "Ge'ez 5 repeats the rivers clause; Brenton's Greek 3 holds all three clauses")},
    ("song-of-solomon", 6): {"14": ("7:1", 0.8, "the first half of Greek 7:1 (Return, return, O Shulamite); the Ge'ez closes chapter 6 with it")},
    ("song-of-solomon", 7): {"1": ("7:1", 0.8, "the second half of Greek 7:1 (what will ye see in the Shulamite)")},
    ("job", 40): {"33": ("41:1-2", 0.8, "Greek 41:1 and the first clause of 41:2; the Ge'ez closes chapter 40 with them")},
    ("job", 41): {"1": ("41:2", 0.8, "the second clause of Greek 41:2 (who is there that resists me)")},
    ("job", 36): {"26": None},
    ("job", 37): {"5": None},
}
LOWER = {("job", 36, "26"): "the tail of Greek 36:28 stands inside Ge'ez 37:5, not here",
         ("job", 37, "5"): "its middle holds the clauses of Greek 36:28 (the cattle's seasons, the heart not astonished)"}

rows = []
for f in sorted((HERE / "out").glob("*.json")):
    o = json.loads(f.read_text(encoding="utf-8"))
    book, ch = o["book"], o["chapter"]
    fx = FIXES.get((book, ch), {})
    for l in o["lines"]:
        g = l["geez"]
        greek = f"{ch}:{l['greek']}" if l["greek"] else ""
        conf, note = l["confidence"], l["note"]
        if g in fx:
            rep = fx[g]
            if rep is None:
                if (book, ch, g) in LOWER:
                    conf, note = min(conf, 0.75), LOWER[(book, ch, g)]
                else:
                    continue
            else:
                greek, conf, note = rep
        rows.append({"book": book, "geez": f"{ch}:{g}", "greek": greek, "confidence": conf, "note": note})
    for g, rep in fx.items():
        if rep is not None and not any(l["geez"] == g for l in o["lines"]):
            rows.append({"book": book, "geez": f"{ch}:{g}", "greek": rep[0], "confidence": rep[1], "note": rep[2]})


def key(r):
    c, v = r["geez"].split(":")
    return (r["book"], int(c), int(v.split("-")[0]))


rows.sort(key=key)
(HERE / "geez-wisdom-map.json").write_text(json.dumps({"source": SOURCE, "greek_text": "GRCBRENT", "rows": rows},
                                                      ensure_ascii=False, indent=1), encoding="utf-8")
print(len(rows), sum(1 for r in rows if not r["greek"]))
