"""PRB-0977: gpt-6.1-sol (Codex) reads each kinship contradiction against the King James and the Hebrew and says which
rows are wrong and what to do with them. Checked by script: every row id is the case's own, every slug named is one of
the case's records, the action kinds and their fields are well formed; a rejected answer is retried once with the
errors. Out: out/<case>.json; nothing is written anywhere else."""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
KINDS = ["delete-row", "retype-row", "retarget-row", "add-row", "fold-records", "split-record", "correct-sex", "none"]
TYPES = ["father-of", "mother-of", "son-of", "daughter-of", "brother-of", "sister-of", "half-brother-of", "half-sister-of",
         "husband-of", "wife-of", "concubine-of", "grandfather-of", "grandson-of", "descendant-of", "ancestor-of",
         "father-in-law-of", "son-in-law-of", "adoptive-mother-of"]

SCHEMA = {"type": "object", "additionalProperties": False, "required": ["verdict", "actions", "note"], "properties": {
    "verdict": {"type": "string", "enum": ["legitimate", "defect", "mixed"]},
    "actions": {"type": "array", "items": {"type": "object", "additionalProperties": False,
                "required": ["kind", "rows", "record", "other", "new_type", "new_to", "verse", "why", "confidence"],
                "properties": {"kind": {"type": "string", "enum": KINDS},
                               "rows": {"type": "array", "items": {"type": "integer"}},
                               "record": {"type": "string"}, "other": {"type": "string"},
                               "new_type": {"type": "string"}, "new_to": {"type": "string"},
                               "verse": {"type": "string"}, "why": {"type": "string"},
                               "confidence": {"type": "number"}}}},
    "note": {"type": "string"}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are checking the family relationships of a Bible research site's person register. Answer only with the JSON the schema asks for; do not run any commands or write any files.

A sweep found the contradiction described in "question" below. You get every relationship row touching the people involved (id, "from type to", the verse it was read from, who recorded it), their records (name, distinguisher, sex, tribe, notes), and the King James and Hebrew (BHSA) text of each cited verse with one verse either side.

Decide what is true by reading the verses, the Hebrew first:
- verdict "legitimate": the rows are right as they stand — e.g. two genealogies that genuinely disagree (Matthew vs Luke, Chronicles vs Genesis, a Septuagint-based line), a half-sibling who is also a spouse (Gen 20:12). Then actions is [] or only "none".
- verdict "defect": something is wrong; "mixed": some rows right, some wrong.

Actions (fill every field; use "" or [] for those a kind does not use):
- delete-row: rows = the ids to remove (a row read wrongly, a duplicate of a right one, a grandfather recorded as father when the grandson row exists elsewhere…).
- retype-row: rows = ids; new_type = the right type (one of: {types}).
- retarget-row: rows = ids; new_to = the slug the row should point at instead (must be one of the records given).
- add-row: record = from-slug, new_type, new_to = to-slug, verse = "BOOK c:v" where the text states it (both slugs from the records given).
- fold-records: record = the slug to keep, other = the slug that is the same man/woman and should be merged into it (both from the records given); say in why which verse shows they are one.
- split-record: record = the slug that stands for more than one person; why = who the persons are and which rows belong to which.
- correct-sex: record = slug, new_type = "male" or "female".
Every action: verse = the verse that decides it ("BOOK c:v", or "" if none), why = one or two sentences from the text, confidence 0..1.

Rules:
- A row whose recorder is "read from Scripture by the project owner" is his ruling: never delete or change it; if it looks wrong, say so in note only.
- "son of" in a genealogy may skip generations; prefer retype to descendant-of or grandson-of over delete when the text states a line but not a direct father.
- Take only what the text says. If the verses do not decide it, verdict "legitimate" with a note, or a low confidence — do not guess.
- note: one or two sentences a reviewer should read.

Case:
{case}
"""


def check(case, ans):
    ids = {r["id"] for r in case["relationships"]}
    slugs = {r["slug"] for r in case["records"]}
    errs = []
    for a in ans["actions"]:
        k = a["kind"]
        bad = [i for i in a["rows"] if i not in ids]
        if bad:
            errs.append(f"{k}: row ids {bad} are not among this case's relationships")
        if k in ("delete-row", "retype-row", "retarget-row") and not a["rows"]:
            errs.append(f"{k} names no rows")
        if k == "retype-row" and a["new_type"] not in TYPES:
            errs.append(f"retype-row: new_type '{a['new_type']}' is not one of the allowed types")
        if k in ("retarget-row", "add-row") and a["new_to"] not in slugs:
            errs.append(f"{k}: new_to '{a['new_to']}' is not one of the records given")
        if k in ("add-row", "fold-records", "split-record", "correct-sex") and a["record"] not in slugs:
            errs.append(f"{k}: record '{a['record']}' is not one of the records given")
        if k == "fold-records" and (a["other"] not in slugs or a["other"] == a["record"]):
            errs.append(f"fold-records: other '{a['other']}' must be a different record given")
        if k == "add-row" and a["new_type"] not in TYPES:
            errs.append(f"add-row: new_type '{a['new_type']}' is not one of the allowed types")
        if k == "correct-sex" and a["new_type"] not in ("male", "female"):
            errs.append("correct-sex: new_type must be male or female")
        owner = [r["id"] for r in case["relationships"] if r["id"] in a["rows"] and "project owner" in (r["source"] or "")]
        if owner and k != "none":
            errs.append(f"rows {owner} are the owner's own; they may not be changed")
    if ans["verdict"] == "legitimate" and any(a["kind"] != "none" for a in ans["actions"]):
        errs.append("verdict legitimate but actions change something")
    return errs


def run(case):
    cid = case["id"]
    out = OUT / f"{cid}.json"
    if out.exists():
        return cid, "kept"
    item = {k: case[k] for k in ("question", "records", "relationships", "verses")}
    prompt = PROMPT.replace("{types}", ", ".join(TYPES)).replace("{case}", json.dumps(item, ensure_ascii=False, indent=1))
    for attempt in (1, 2):
        last = RAW / f"{cid}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last),
                "-C", str(RAW)]
        p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
        if p.returncode or not last.exists():
            return cid, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        errs = check(case, ans)
        if not errs:
            out.write_text(json.dumps({"case": cid, "question": case["question"], **ans, "model": MODEL, "effort": EFFORT,
                                       "attempt": attempt, "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return cid, "ok" if attempt == 1 else "ok after retry"
        (RAW / f"{cid}.rejected.json").write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1),
                                                   encoding="utf-8")
        prompt += "\n\nYour previous answer was rejected by the checker: " + "; ".join(errs) + ". Fix exactly that."
    return cid, "rejected twice: " + "; ".join(errs[:3])


cases = json.loads((HERE / "cases.json").read_text(encoding="utf-8"))
if len(sys.argv) > 1:
    cases = [c for c in cases if c["id"] in sys.argv[1:]]
with ThreadPoolExecutor(max_workers=3) as pool:
    for cid, status in pool.map(run, cases):
        print(cid, status, flush=True)
print("DONE", flush=True)
