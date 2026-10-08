"""PRB-0571: gpt-6.1-sol (Codex) reads each sentence in which a tribe's name stands alone and says whether the name is the
man (the patriarch) or the people (the tribe or nation named after him). Checked by script: every word of the batch is
answered exactly once, by its own key, with one of the three answers and a confidence between 0 and 1; a rejected answer
is retried once with the errors. Out: out/<batch>.json; nothing is written anywhere else. A batch already answered is
skipped, so a run stopped by the plan's limit resumes where it stopped."""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] in ("low", "medium", "high") else "medium"
ANSWERS = ["man", "people", "unclear"]

SCHEMA = {"type": "object", "additionalProperties": False, "required": ["answers"], "properties": {
    "answers": {"type": "array", "items": {"type": "object", "additionalProperties": False,
                "required": ["key", "answer", "why", "confidence"],
                "properties": {"key": {"type": "string"}, "answer": {"type": "string", "enum": ANSWERS},
                               "why": {"type": "string"}, "confidence": {"type": "number"}}}}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are annotating a Bible research site. Answer only with the JSON the schema asks for; do not run any commands or write any files.

In Hebrew a tribe is called by its ancestor's own name: "Reuben" is Jacob's son in one verse and the tribe of Reubenites in the next, and "Israel" is the patriarch Jacob and also the whole nation. Each word below is such a name standing on its own in its sentence (not after "sons of", "tribe of", "king of", "land of" and the like, which are already settled). For each word say whom the sentence means:

- "man": the individual patriarch himself — the person Jacob/Israel, Judah, Reuben, Ephraim... as a man: his life, his journeys, his own words, his death and burial, his household and his literal sons while he lives, a blessing or speech addressed to him in person (Jacob's blessing of his sons in Genesis 49 speaks to the men), "Abraham, Isaac and Israel" as the fathers, the ancestor named as somebody's father.
- "people": the tribe, the nation or the kingdom named after him — "Israel sinned", "all Israel", "O Israel" said to the nation, "Ephraim is joined to idols", "Judah went up", "Judah and Israel dwelt safely", the northern kingdom or the kingdom of Judah, a tribe receiving land or going to war, Moses' blessing of the tribes (Deuteronomy 33), the land the tribe holds ("went down to Judah", "in Ephraim"), "the God of Israel" and "the Holy One of Israel" (the nation's God) unless the verse sets the patriarch among the fathers.
- "unclear": only where the sentence genuinely allows both and nothing nearby decides it.

Read the original first, then the King James; the verse before and after are given for context. In Genesis the name is usually the man; outside Genesis it is usually the people — but read each sentence, do not apply that as a rule. A word that occurs twice in one verse may need two different answers.

For every word give: key (copied exactly), answer, why (one short sentence from the text), confidence 0..1 (how sure the reading is).

Words, grouped by verse:
{batch}
"""


def check(batch, ans):
    keys = [w["key"] for v in batch["verses"] for w in v["words"]]
    got = [a["key"] for a in ans["answers"]]
    errs = []
    missing = [k for k in keys if k not in got]
    extra = [k for k in got if k not in keys]
    twice = sorted({k for k in got if got.count(k) > 1})
    if missing:
        errs.append(f"keys not answered: {missing}")
    if extra:
        errs.append(f"keys that are not in the batch: {extra}")
    if twice:
        errs.append(f"keys answered more than once: {twice}")
    for a in ans["answers"]:
        if a["answer"] not in ANSWERS:
            errs.append(f"{a['key']}: answer must be one of {ANSWERS}")
        if not 0 <= a["confidence"] <= 1:
            errs.append(f"{a['key']}: confidence must be between 0 and 1")
        if not a["why"].strip():
            errs.append(f"{a['key']}: why is empty")
    return errs


def run(batch):
    bid = batch["id"]
    out = OUT / f"{bid}.json"
    if out.exists():
        return bid, "kept"
    prompt = PROMPT.replace("{batch}", json.dumps(batch["verses"], ensure_ascii=False, indent=1))
    for attempt in (1, 2):
        last = RAW / f"{bid}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last),
                "-C", str(RAW)]
        p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
        if p.returncode or not last.exists():
            return bid, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        errs = check(batch, ans)
        if not errs:
            out.write_text(json.dumps({"batch": bid, **ans, "model": MODEL, "effort": EFFORT, "attempt": attempt,
                                       "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return bid, "ok" if attempt == 1 else "ok after retry"
        (RAW / f"{bid}.rejected.json").write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1),
                                                   encoding="utf-8")
        prompt += "\n\nYour previous answer was rejected by the checker: " + "; ".join(errs) + ". Fix exactly that."
    return bid, "rejected twice: " + "; ".join(errs[:3])


batches = json.loads((HERE / "batches.json").read_text(encoding="utf-8"))
only = [a for a in sys.argv[1:] if a.startswith("b")]
if only:
    batches = [b for b in batches if b["id"] in only]
with ThreadPoolExecutor(max_workers=3) as pool:
    for bid, status in pool.map(run, batches):
        print(bid, status, flush=True)
print("DONE", flush=True)
