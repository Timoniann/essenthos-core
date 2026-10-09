"""Sort Codex's corrections by kind: 'marks' changes only accents and breathings (the letters stay), 'letters' changes a
letter, 'words' adds, removes, joins or splits words. Out: kinds.json."""
import json, glob, unicodedata, collections
from pathlib import Path

HERE = Path(__file__).parent


def bare(s):
    return "".join(ch for ch in unicodedata.normalize("NFD", s) if not unicodedata.combining(ch)).lower().replace("ς", "σ")


def kind(c):
    o, p = c["ours"].split(), c["printed"].split()
    if len(o) != len(p) or not p:
        return "words"
    return "marks" if bare(c["ours"]) == bare(c["printed"]) else "letters"


rows = []
for f in sorted(glob.glob(str(HERE / "out" / "*.json"))):
    d = json.load(open(f, encoding="utf-8"))
    for v in d["verses"]:
        for c in v["corrections"]:
            rows.append({"page": d["page"], "image": d["image"], "verse": v["verse"], **c, "kind": kind(c)})
(HERE / "kinds.json").write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
print(collections.Counter(r["kind"] for r in rows))
