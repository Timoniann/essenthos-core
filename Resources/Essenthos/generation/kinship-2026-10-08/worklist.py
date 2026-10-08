"""The kinship worklist for batch K: what to apply, from the two readings and the blind judges.
- apply: proposals both Codex and Haiku made with the same fix, and disputed ones the judge called right;
- read: both flagged a row but proposed different fixes, or only Haiku read the chapter (NEH 11) — read the verse and decide;
- skip: disputed proposals the judge called wrong or unclear (kept with the judge's reason, not applied).
Out: worklist.json."""
import json
from pathlib import Path

HERE = Path(__file__).parent
cmp = json.loads((HERE / "compare.json").read_text(encoding="utf-8"))
key = json.loads((HERE / "judge" / "key.json").read_text(encoding="utf-8"))
verdict = {}
for i in (1, 2, 3):
    for x in json.loads((HERE / "judge" / f"verdict-{i}.json").read_text(encoding="utf-8")):
        verdict[x["id"]] = x
items = {}
for i in (1, 2, 3):
    for x in json.loads((HERE / "judge" / f"items-{i}.json").read_text(encoding="utf-8")):
        items[x["id"]] = x

out = {"apply": [], "read": [], "skip": []}
for ch in cmp["chapters"]:
    name = ch["chapter"]
    ans = {}
    for who, folder in (("codex", "chapters"), ("haiku", "haiku")):
        p = HERE / folder / f"{name}.json"
        ans[who] = json.loads(p.read_text(encoding="utf-8")) if p.exists() else None
    inp = json.loads((HERE / "raw-chapters" / f"{name}.input.json").read_text(encoding="utf-8"))
    rows = {r["id"]: r for r in inp["relationships"]}

    def wrong_of(who, rid):
        return next(w for w in ans[who]["wrong"] if rid in w["rows"])

    for rid in ch["wrong_both"]:
        c, h = wrong_of("codex", rid), wrong_of("haiku", rid)
        entry = {"chapter": name, "row_id": rid, "row": rows[rid]["row"], "row_verse": rows[rid]["verse"],
                 "codex": {k: c[k] for k in ("kind", "new_type", "new_to", "verse", "why")},
                 "haiku": {k: h[k] for k in ("kind", "new_type", "new_to", "verse", "why")}}
        out["apply" if rid in ch["same_fix"] else "read"].append(entry)
    for k in ch["missing_both"]:
        m = next(m for m in ans["codex"]["missing"] if {m["from"], m["to"]} == set(k[1:]) or m["type"] == k[0])
        out["apply"].append({"chapter": name, "missing": f"{m['from']} {m['type']} {m['to']}", "verse": m["verse"], "why": m["why"]})
    if ans["codex"] is None and ans["haiku"]:
        for w in ans["haiku"]["wrong"]:
            out["read"].append({"chapter": name, "rows": [rows[r]["row"] + f" (id {r})" for r in w["rows"] if r in rows],
                                "haiku_only": {k: w[k] for k in ("kind", "new_type", "new_to", "verse", "why")}})
        for m in ans["haiku"]["missing"]:
            out["read"].append({"chapter": name, "missing": f"{m['from']} {m['type']} {m['to']}", "verse": m["verse"],
                                "haiku_only": m["why"]})

for k, meta in key.items():
    n = int(k.split("-")[1])
    v = verdict.get(n, {"verdict": "none", "why": ""})
    it = items[n]
    entry = {"chapter": meta["chapter"], "claim": it["claim"], "judge": v["verdict"], "judge_why": v["why"]}
    if meta["type"] == "wrong":
        entry["row_id"] = meta["row"]
        w = next(w for w in json.loads((HERE / ("chapters" if meta["who"] == "codex" else "haiku") / f"{meta['chapter']}.json")
                                       .read_text(encoding="utf-8"))["wrong"] if meta["row"] in w["rows"])
        entry["fix"] = {k2: w[k2] for k2 in ("kind", "new_type", "new_to", "verse")}
    out["apply" if v["verdict"] == "right" else "skip"].append(entry)

(HERE / "worklist.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
print({k: len(v) for k, v in out.items()})
