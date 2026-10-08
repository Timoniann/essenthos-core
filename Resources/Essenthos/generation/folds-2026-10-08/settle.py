"""The fold pass against the blind Opus judge. folds.json = pairs both call one person and keep the same record
(to fold); two.json = pairs both call two people (with Codex's verses on the wrong record, to be read before moving);
open.json = the rest. Out: those three files; counts on stdout."""
import json
from pathlib import Path

HERE = Path(__file__).parent
judge = {x["key"]: x for x in json.loads((HERE / "judge-verdict.json").read_text(encoding="utf-8"))}
folds, two, open_ = [], [], []
for f in sorted((HERE / "out").glob("*.json")):
    a = json.loads(f.read_text(encoding="utf-8"))
    key = "--".join(a["pair"])
    j = judge[key]
    j_keep = {"A": a["pair"][0], "B": a["pair"][1]}.get(j["keep"])
    entry = {"pair": a["pair"], "name": a["case"]["name"], "codex": a["verdict"], "codex_keep": a["keep_slug"],
             "codex_confidence": a["confidence"], "codex_why": a["why"], "codex_verses_of_other": a["verses_of_other"],
             "judge": j["verdict"], "judge_keep": j_keep, "judge_why": j["why"]}
    if a["verdict"] == j["verdict"] == "one" and a["keep_slug"] == j_keep:
        folds.append({**entry, "keep": j_keep, "fold": a["fold_slug"]})
    elif a["verdict"] == j["verdict"] == "one":
        # both say one person and differ only on which record to keep: the lead keeps the fuller one (more verses,
        # then more ties both ways)
        def size(s):
            r = a["case"]["A" if s == a["pair"][0] else "B"]
            return r["verse_count"], len(r["family"] or []) + len(r["named_by"] or [])
        keep = max(a["pair"], key=size)
        folds.append({**entry, "keep": keep, "fold": next(s for s in a["pair"] if s != keep),
                      "keep_decided_by": "the lead: the fuller record"})
    elif a["verdict"] == j["verdict"] == "two":
        two.append(entry)
    else:
        open_.append(entry)
for name, data in (("folds", folds), ("two", two), ("open", open_)):
    (HERE / f"{name}.json").write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"folds {len(folds)}, two {len(two)}, open {len(open_)}")
for e in open_:
    print(" open", e["pair"], "codex", e["codex"], e["codex_keep"], "| judge", e["judge"], e["judge_keep"])
