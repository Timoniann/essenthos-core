"""Ask gpt-6.1-sol (Codex) for each record whether it is another spelling of a record the corpus holds. One batch of
10 records per call; the answer is checked (every record answered once, fold targets among its candidates)."""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"

SCHEMA = {"type": "object", "additionalProperties": False, "required": ["answers"], "properties": {"answers": {
    "type": "array", "items": {"type": "object", "additionalProperties": False,
                               "required": ["slug", "verdict", "fold_into", "confidence", "reason"],
                               "properties": {
                                   "slug": {"type": "string"},
                                   "verdict": {"type": "string", "enum": ["same", "distinct", "unsure"]},
                                   "fold_into": {"type": "string"},
                                   "confidence": {"type": "number"},
                                   "reason": {"type": "string"}}}}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are helping clean the person-and-place register of a Bible research site. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Each item below is a record that no verse of the corpus reaches and that has no name in Ukrainian, German or Spanish. Most are places from the OpenBible.info geocoding data or headwords of Strong's Dictionary; a few are peoples (gentilics) or persons. Many are another spelling of a record the register already holds (Gomorrha = Gomorrah, Sychem = Shechem, Charran = Haran, Zabulon = Zebulun: the Greek New Testament's or the Apocrypha's spelling of an Old Testament name, or an older English spelling). Others are genuinely different places that only sound alike, or places of the deuterocanonical books / later history that the register holds nowhere else.

For each record decide:
- "same": it is the same referent as exactly one of its candidates — give that candidate's slug in fold_into. Same referent means the same place / people / person, not merely the same name: two different towns called Aphek are not the same. Use the distinguishers, Strong numbers (a shared Strong number is strong evidence for a place or people, not for persons), the verses, the place kind and what you know of biblical geography.
- "distinct": no candidate is the same referent. fold_into = "".
- "unsure": you cannot tell from what is given. fold_into = the likeliest candidate or "".
confidence in 0..1; reason one or two sentences naming the evidence (e.g. "Greek NT spelling of Gomorrah, MAT 10:15").

Items:
{items}
"""


def check(batch, ans):
    errs = []
    want = [i["record"]["slug"] for i in batch]
    got = [a["slug"] for a in ans.get("answers", [])]
    if sorted(got) != sorted(want):
        errs.append(f"answered {sorted(got)} for {sorted(want)}")
    by = {i["record"]["slug"]: {c["slug"] for c in i["candidates"]} for i in batch}
    for a in ans.get("answers", []):
        if a["verdict"] == "same" and a["fold_into"] not in by.get(a["slug"], set()):
            errs.append(f"{a['slug']} folds into {a['fold_into']}, which is not one of its candidates")
    return errs


def run(path):
    out = OUT / path.name
    if out.exists():
        return path.name, "kept"
    batch = json.loads(path.read_text(encoding="utf-8"))
    prompt = PROMPT.replace("{items}", json.dumps(batch, ensure_ascii=False, indent=1))
    prev = RAW / f"{path.stem}.rejected.json"
    if prev.exists():
        prompt += "\n\nA previous answer was rejected by the checker: " + "; ".join(json.loads(prev.read_text(encoding="utf-8"))["errors"]) + ". Fix exactly that."
    last = RAW / f"{path.stem}.last.json"
    args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
            "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last), "-C", str(RAW)]
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
    if p.returncode or not last.exists():
        return path.name, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}"
    ans = json.loads(last.read_text(encoding="utf-8"))
    errs = check(batch, ans)
    if errs:
        prev.write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1), encoding="utf-8")
        return path.name, "rejected: " + "; ".join(errs[:3])
    ans.update(model=MODEL, effort=EFFORT, at=datetime.now(timezone.utc).isoformat(timespec="seconds"))
    out.write_text(json.dumps(ans, ensure_ascii=False, indent=1), encoding="utf-8")
    return path.name, "ok"


with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, sorted((HERE / "batches").glob("*.json"))):
        print(name, status, flush=True)
