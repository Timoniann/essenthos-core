"""The corrections that go into the converter now: every one the Opus judge called right (all letter and word
corrections it saw, and its 60-item sample of accent corrections), plus the judge's own readings where it found the
page prints a third form. The ~1,000 accent corrections it did not see wait for the second reading (verify.py);
verified ones are added by running this again. Out: accepted.json, judge-readings.json."""
import json
from pathlib import Path

HERE = Path(__file__).parent
rows = json.loads((HERE / "kinds.json").read_text(encoding="utf-8"))
verdicts = {x["id"]: x for n in (1, 2) for x in json.loads((HERE / "judge" / f"verdict-{n}.json").read_text(encoding="utf-8"))}
second = {x["id"]: x for f in sorted((HERE / "verify").glob("page-*.json")) for x in json.loads(f.read_text(encoding="utf-8"))}
accepted, readings = [], []
for i, r in enumerate(rows):
    item = {"id": i, "page": r["page"], "image": r["image"], "verse": r["verse"], "ours": r["ours"], "printed": r["printed"],
            "why": r["why"], "kind": r["kind"]}
    v = verdicts.get(i)
    if v:
        if v["verdict"] == "right":
            accepted.append({**item, "checked": "codex+opus"})
        elif v["verdict"] == "wrong" and v.get("actual") and v["actual"] != r["ours"]:
            readings.append({**item, "judge_actual": v["actual"], "judge_note": v.get("note", "")})
    elif i in second and second[i]["verdict"] == "right":
        accepted.append({**item, "checked": "codex twice"})
(HERE / "accepted.json").write_text(json.dumps(accepted, ensure_ascii=False, indent=1), encoding="utf-8")
(HERE / "judge-readings.json").write_text(json.dumps(readings, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(accepted), "accepted;", len(readings), "judge readings to look at")
