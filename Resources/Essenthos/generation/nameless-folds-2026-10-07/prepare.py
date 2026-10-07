"""PRB-0476: the 103 records with no verse and no Ukrainian name, each with the records it may be another spelling of.

Candidates: records of a compatible kind whose folded name is close (difflib) or which share a Strong number
in their name labels. Writes batches/NN.json for run.py.
"""
import difflib, json, re, unicodedata
from pathlib import Path

HERE = Path(__file__).parent
d = json.loads((HERE / "entities.json").read_text(encoding="utf-8"))
targets = [e for e in d if e["verses"] == 0 and not e["ukr"]]
STRONG = re.compile(r"\b[HG]\d{1,5}\b")
KINDS = {"place": {"place"}, "people": {"people"}, "person": {"person"}}


def fold(s):
    s = unicodedata.normalize("NFD", s or "").lower()
    s = "".join(c for c in s if c.isalpha())
    return s.replace("ph", "f").replace("ch", "k").replace("th", "t").replace("y", "i").replace("j", "i")


def strongs(e):
    return set(STRONG.findall(" ".join(e["labels"]) + " " + (e["distinguisher"] or "")))


def brief(e):
    return {k: e[k] for k in ("slug", "kind", "name", "distinguisher", "place_kind", "modern", "notes", "ukr", "verses",
                              "first_verses", "labels") if e.get(k) not in (None, [], "")}


others = [e for e in d if e["verses"] > 0 or e["ukr"]]
items = []
for t in targets:
    ft, st = fold(t["name"]), strongs(t)
    scored = []
    for o in others:
        if o["kind"] not in KINDS[t["kind"]] or o["slug"] == t["slug"]:
            continue
        r = difflib.SequenceMatcher(None, ft, fold(o["name"])).ratio()
        shared = bool(st & strongs(o))
        if r >= 0.6 or shared:
            scored.append((shared, r, o))
    scored.sort(key=lambda x: (-x[0], -x[1]))
    items.append({"record": brief(t), "source": t["source"],
                  "candidates": [dict(brief(o), shares_strong=s, name_similarity=round(r, 2)) for s, r, o in scored[:8]]})

(HERE / "batches").mkdir(exist_ok=True)
for i in range(0, len(items), 10):
    (HERE / "batches" / f"{i // 10:02}.json").write_text(json.dumps(items[i:i + 10], ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "records;", sum(1 for i in items if i["candidates"]), "with candidates;",
      sum(1 for i in items if any(c["shares_strong"] for c in i["candidates"])), "share a Strong number with one")
