"""The fifth pass's proposals (chapters-unlinked/, Codex alone) as blind items for a judge, shaped as judge_build.py
makes them: the claim, the row it is about, the records named, the verse with one either side (KJV + original).
Out: judge-unlinked/items-<n>.json, judge-unlinked/key.json."""
import json, random
from pathlib import Path

HERE = Path(__file__).parent
J = HERE / "judge-unlinked"; J.mkdir(exist_ok=True)
items, key = [], {}


def verses_around(inp, verse):
    book_ch, v = verse.rsplit(":", 1)
    out = {}
    for d in (-1, 0, 1):
        k = f"{book_ch}:{int(v) + d}"
        if k in inp["text"]:
            out[k] = inp["text"][k]
    return out


for f in sorted((HERE / "chapters-unlinked").glob("*.json")):
    name = f.stem
    ans = json.loads(f.read_text(encoding="utf-8"))
    inp = json.loads((HERE / "raw-chapters-unlinked" / f"{name}.input.json").read_text(encoding="utf-8"))
    rows = {r["id"]: r for r in inp["relationships"]}
    recs = {r["slug"]: r for r in inp["records"]}
    for w in ans.get("wrong", []):
        for rid in w["rows"]:
            r = rows[rid]
            a, t, b = r["row"].split(" ")
            claim = {"delete": f"The row '{r['row']}' should be removed: the chapter does not say it.",
                     "retype": f"The row '{r['row']}' should become '{a} {w['new_type']} {b}'.",
                     "retarget": f"The row '{r['row']}' points at the wrong person; it should point at '{w['new_to']}'."}[w["kind"]]
            names = {a, b, w.get("new_to") or a}
            items.append({"id": len(items) + 1, "chapter": name, "claim": claim, "row_verse": r["verse"],
                          "records": [recs[s] for s in sorted(names) if s in recs],
                          "text": verses_around(inp, w["verse"] if w["verse"] in inp["text"] else r["verse"])})
            key[len(items)] = {"type": "wrong", "row": rid, "row_text": r["row"], "kind": w["kind"],
                               "new_type": w.get("new_type"), "new_to": w.get("new_to"), "chapter": name,
                               "verse": w["verse"], "codex_why": w["why"], "codex_confidence": w["confidence"]}
    for m in ans.get("missing", []):
        names = {m["from"], m["to"]}
        other = [o for o in inp.get("recorded_from_other_chapters", []) if any(f" {s} " in f" {o} " for s in names)]
        items.append({"id": len(items) + 1, "chapter": name,
                      "claim": f"The chapter plainly states '{m['from']} {m['type']} {m['to']}', and the site does not record it.",
                      "records": [recs[s] for s in sorted(names) if s in recs],
                      "already_recorded_between_these_people": [r["row"] + " (" + r["verse"] + ")" for r in inp["relationships"]
                                                                 if set(r["row"].split(" ")[::2]) == names] + other[:6],
                      "text": verses_around(inp, m["verse"]) if m["verse"] in inp["text"] else {}})
        key[len(items)] = {"type": "missing", "claim": f"{m['from']} {m['type']} {m['to']}", "chapter": name,
                           "verse": m["verse"], "codex_why": m["why"], "codex_confidence": m["confidence"]}

order = list(range(len(items)))
random.Random(7).shuffle(order)
shuffled = [items[i] for i in order]
blind = {}
for n, it in enumerate(shuffled, 1):
    blind[f"blind-{n}"] = key[it["id"]]
    it["id"] = n
size = 50
for i in range(0, len(shuffled), size):
    (J / f"items-{i // size + 1}.json").write_text(json.dumps(shuffled[i:i + size], ensure_ascii=False, indent=1), encoding="utf-8")
(J / "key.json").write_text(json.dumps(blind, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "items in", (len(shuffled) + size - 1) // size, "chunks")
