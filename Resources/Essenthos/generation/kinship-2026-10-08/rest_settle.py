"""The fourth pass settled: a Codex proposal goes to the worklist where the blind Opus judge calls it right; the rest is
kept for the record. Out: rest-worklist.json (apply), rest-rest.json; counts on stdout."""
import json
from pathlib import Path

HERE = Path(__file__).parent
J = HERE / "judge-rest"
key = json.loads((J / "key.json").read_text(encoding="utf-8"))
verdicts = {}
for f in sorted(J.glob("verdict-*.json")):
    for v in json.loads(f.read_text(encoding="utf-8")):
        verdicts[f"blind-{v['id']}"] = v
apply, rest = [], []
for k, item in key.items():
    v = verdicts[k]
    entry = {**item, "judge": v["verdict"], "judge_why": v["why"]}
    (apply if v["verdict"] == "right" else rest).append(entry)
apply.sort(key=lambda e: (e["chapter"], e.get("verse", "")))
(HERE / "rest-worklist.json").write_text(json.dumps(apply, ensure_ascii=False, indent=1), encoding="utf-8")
(HERE / "rest-rest.json").write_text(json.dumps(rest, ensure_ascii=False, indent=1), encoding="utf-8")
print("apply", len(apply), "(wrong rows", sum(1 for e in apply if e["type"] == "wrong"), ", missing", sum(1 for e in apply if e["type"] == "missing"), ") rest", len(rest))
