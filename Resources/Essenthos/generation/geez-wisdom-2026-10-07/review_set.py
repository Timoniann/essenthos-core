"""Build the second-model review set: every line below 0.9, every line with no Greek, and a 10% sample of the rest."""
import json, random
from pathlib import Path

HERE = Path(__file__).parent
random.seed(7)


def rng(s):
    a, _, b = s.partition("-")
    return range(int(a), int(b or a) + 1)


items = []
for f in sorted((HERE / "out").glob("*.json")):
    o = json.loads(f.read_text(encoding="utf-8"))
    c = json.loads((HERE / "chapters" / f.name).read_text(encoding="utf-8"))
    G = {v["n"]: v["t"] for v in c["GEEZ81"]}
    # Greek and English by position, so PRB-0972's repeated verse number cannot hide a verse.
    K = {i + 1: v["t"] for i, v in enumerate(c["GRCBRENT"])}
    E = {i + 1: v["t"] for i, v in enumerate(c["BRENTON"])}
    for l in o["lines"]:
        if l["confidence"] < 0.9 or not l["greek"] or random.random() < 0.10:
            g = list(rng(l["geez"]))
            k = list(rng(l["greek"])) if l["greek"] else []
            items.append({"id": f"{f.stem}:{l['geez']}", "geez_verses": l["geez"], "greek_verses": l["greek"],
                          "confidence": l["confidence"], "note": l["note"],
                          "geez": " ".join(G[i] for i in g),
                          "greek": " ".join(K.get(i, "") for i in k),
                          "english": " ".join(E.get(i, "") for i in k),
                          "prev_geez": G.get(g[0] - 1, ""), "next_geez": G.get(g[-1] + 1, ""),
                          "greek_before": K.get(k[0] - 1, "") if k else "", "greek_after": K.get(k[-1] + 1, "") if k else ""})
half = (len(items) + 1) // 2
for name, part in (("a", items[:half]), ("b", items[half:])):
    (HERE / f"review-{name}.json").write_text(json.dumps(part, ensure_ascii=False, indent=0), encoding="utf-8")
print(len(items), half)
