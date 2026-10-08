"""Codex (chapters/) against Haiku 5.5 (haiku/) on the same chapter inputs: which wrong-row and missing-row proposals
each made, where they agree, and each one's checker result. Out: compare.json and a summary on stdout."""
import glob, json, sys
from pathlib import Path

HERE = Path(__file__).parent
sys.argv = [sys.argv[0]]
src = (HERE / "chapters.py").read_text(encoding="utf-8")
ns = {"__file__": str(HERE / "chapters.py")}
exec(src[:src.index("SCHEMA = ")].replace("CODEX = ", "CODEX = None and "), ns)
exec(src[src.index("def check("):src.index("def run(")], ns)
check = ns["check"]

INV = {"father-of": "son-of", "mother-of": "son-of", "son-of": "father-of", "daughter-of": "father-of"}


def wrong_keys(ans):
    out = {}
    for w in ans.get("wrong", []):
        for r in w["rows"]:
            out[r] = (w["kind"], w.get("new_type") or w.get("new_to") or "")
    return out


def missing_keys(ans):
    out = set()
    for m in ans.get("missing", []):
        a, t, b = m["from"], m["type"], m["to"]
        # one statement either way round: a father-of b == b son-of a
        if t in ("son-of", "daughter-of"):
            out.add(("parent", b, a))
        elif t in ("father-of", "mother-of"):
            out.add(("parent", a, b))
        elif t.endswith("brother-of") or t.endswith("sister-of"):
            out.add(("sibling",) + tuple(sorted((a, b))))
        elif t in ("husband-of", "wife-of", "concubine-of"):
            out.add(("spouse",) + tuple(sorted((a, b))))
        else:
            out.add((t, a, b))
    return out


rows = []
tot = {"codex_wrong": 0, "haiku_wrong": 0, "both_wrong": 0, "same_fix": 0, "codex_missing": 0, "haiku_missing": 0,
       "both_missing": 0, "haiku_checker_errors": 0}
for f in sorted(glob.glob(str(HERE / "raw-chapters" / "*.input.json"))):
    name = Path(f).name.removesuffix(".input.json")
    item = json.loads(Path(f).read_text(encoding="utf-8"))
    cx = HERE / "chapters" / f"{name}.json"
    hk = HERE / "haiku" / f"{name}.json"
    c = json.loads(cx.read_text(encoding="utf-8")) if cx.exists() else None
    h = json.loads(hk.read_text(encoding="utf-8")) if hk.exists() else None
    herr = check(item, {"wrong": h.get("wrong", []), "missing": h.get("missing", [])}) if h else ["no answer"]
    cw, hw = (wrong_keys(c) if c else {}), (wrong_keys(h) if h else {})
    cm, hm = (missing_keys(c) if c else set()), (missing_keys(h) if h else set())
    both = set(cw) & set(hw)
    row = {"chapter": name, "codex": c is not None, "haiku": h is not None, "haiku_checker": herr,
           "wrong_codex_only": sorted(set(cw) - set(hw)), "wrong_haiku_only": sorted(set(hw) - set(cw)),
           "wrong_both": sorted(both), "same_fix": sorted(r for r in both if cw[r] == hw[r]),
           "missing_codex_only": sorted(map(list, cm - hm)), "missing_haiku_only": sorted(map(list, hm - cm)),
           "missing_both": sorted(map(list, cm & hm))}
    rows.append(row)
    if c and h:
        tot["codex_wrong"] += len(cw); tot["haiku_wrong"] += len(hw); tot["both_wrong"] += len(both)
        tot["same_fix"] += len(row["same_fix"]); tot["codex_missing"] += len(cm); tot["haiku_missing"] += len(hm)
        tot["both_missing"] += len(cm & hm)
    tot["haiku_checker_errors"] += bool(h) and bool(herr)
(HERE / "compare.json").write_text(json.dumps({"totals": tot, "chapters": rows}, ensure_ascii=False, indent=1), encoding="utf-8")
print(json.dumps(tot))
for r in rows:
    if not (r["codex"] and r["haiku"]) or r["haiku_checker"]:
        print(r["chapter"], "codex" if r["codex"] else "-", "haiku" if r["haiku"] else "-", r["haiku_checker"][:2])
