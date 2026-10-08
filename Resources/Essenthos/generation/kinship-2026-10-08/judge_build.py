"""Every proposal Codex and Haiku did not both make, as blind items for a judge: the claim, the row it is about, the
records named, and the verse with one either side (KJV + original). Who proposed it is kept in key.json, not in the
items. Out: judge/items-<n>.json (chunks), judge/key.json."""
import json, random
from pathlib import Path

HERE = Path(__file__).parent
J = HERE / "judge"; J.mkdir(exist_ok=True)
cmp = json.loads((HERE / "compare.json").read_text(encoding="utf-8"))
items, key = [], {}


def verses_around(inp, verse):
    book_ch, v = verse.rsplit(":", 1)
    out = {}
    for d in (-1, 0, 1):
        k = f"{book_ch}:{int(v) + d}"
        if k in inp["text"]:
            out[k] = inp["text"][k]
    return out


for ch in cmp["chapters"]:
    name = ch["chapter"]
    inp = json.loads((HERE / "raw-chapters" / f"{name}.input.json").read_text(encoding="utf-8"))
    rows = {r["id"]: r for r in inp["relationships"]}
    recs = {r["slug"]: r for r in inp["records"]}
    answers = {}
    for who, folder in (("codex", "chapters"), ("haiku", "haiku")):
        p = HERE / folder / f"{name}.json"
        answers[who] = json.loads(p.read_text(encoding="utf-8")) if p.exists() else {"wrong": [], "missing": []}
    for who, field in (("codex", "wrong_codex_only"), ("haiku", "wrong_haiku_only")):
        for rid in ch[field]:
            w = next(w for w in answers[who]["wrong"] if rid in w["rows"])
            r = rows[rid]
            a, _, b = r["row"].split(" ")
            claim = {"delete": f"The row '{r['row']}' should be removed: the chapter does not say it.",
                     "retype": f"The row '{r['row']}' should become '{a} {w['new_type']} {b}'.",
                     "retarget": f"The row '{r['row']}' points at the wrong person; it should point at '{w['new_to']}'."}[w["kind"]]
            names = {a, b, w.get("new_to") or a}
            items.append({"id": len(items) + 1, "chapter": name, "claim": claim, "row_verse": r["verse"],
                          "records": [recs[s] for s in sorted(names) if s in recs],
                          "text": verses_around(inp, w["verse"] if w["verse"] in inp["text"] else r["verse"])})
            key[len(items)] = {"who": who, "type": "wrong", "row": rid, "chapter": name}
    for who, field in (("codex", "missing_codex_only"), ("haiku", "missing_haiku_only")):
        for k in ch[field]:
            m = next(m for m in answers[who]["missing"]
                     if {m["from"], m["to"]} == set(k[1:]) or (k[0] not in ("parent", "sibling", "spouse") and m["type"] == k[0]))
            names = {m["from"], m["to"]}
            other = [o for o in inp.get("recorded_from_other_chapters", []) if any(f" {s} " in f" {o} " for s in names)]
            items.append({"id": len(items) + 1, "chapter": name,
                          "claim": f"The chapter plainly states '{m['from']} {m['type']} {m['to']}', and the site does not record it.",
                          "records": [recs[s] for s in sorted(names) if s in recs],
                          "already_recorded_between_these_people": [r["row"] + " (" + r["verse"] + ")" for r in inp["relationships"]
                                                                     if set(r["row"].split(" ")[::2]) == names] + other[:6],
                          "text": verses_around(inp, m["verse"]) if m["verse"] in inp["text"] else {}})
            key[len(items)] = {"who": who, "type": "missing", "claim": f"{m['from']} {m['type']} {m['to']}", "chapter": name}

order = list(range(len(items)))
random.Random(7).shuffle(order)
shuffled = [items[i] for i in order]
for n, it in enumerate(shuffled, 1):
    key[f"blind-{n}"] = key[it["id"]]
    it["id"] = n
size = 50
for i in range(0, len(shuffled), size):
    (J / f"items-{i // size + 1}.json").write_text(json.dumps(shuffled[i:i + size], ensure_ascii=False, indent=1), encoding="utf-8")
(J / "key.json").write_text(json.dumps({k: v for k, v in key.items() if str(k).startswith("blind-")}, indent=1), encoding="utf-8")
print(len(items), "items in", (len(shuffled) + size - 1) // size, "chunks")
