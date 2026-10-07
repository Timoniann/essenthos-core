"""PRB-0485: Strong entries that enumerate several bearers of a name while the encyclopedia holds one record.

For each entry, gpt-6.1-sol (Codex) says which enumerated sense the one record is, and which enumerated persons have
no record, with the verses (from the entry's own verse list) where each is meant. Checked: every verse given is one
of the entry's verses, and no verse is given to two senses. Out: out/<strong>.json; nothing is written anywhere else.
"""
import json, os, subprocess
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
BOOKS = ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL "
         "AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB "
         "JAS 1PE 2PE 1JN 2JN 3JN JUD REV").split()

SCHEMA = {"type": "object", "additionalProperties": False, "required": ["record_is", "missing", "note"], "properties": {
    "record_is": {"type": "string"},
    "missing": {"type": "array", "items": {"type": "object", "additionalProperties": False,
                                           "required": ["sense", "name", "who", "verses", "confidence"],
                                           "properties": {"sense": {"type": "string"}, "name": {"type": "string"},
                                                          "who": {"type": "string"},
                                                          "verses": {"type": "array", "items": {"type": "string"}},
                                                          "confidence": {"type": "number"}}}},
    "note": {"type": "string"}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are helping complete the person register of a Bible research site. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Below is one entry of Strong's Dictionary whose definition enumerates several senses (1), 2), 3)…), the ONE record the site holds for this Strong number, and every verse where the Hebrew/Greek word with this number occurs, with the King James text of the verse.

Decide:
- record_is: which numbered sense the site's one record is (e.g. "2"), or "" if it is none of them.
- missing: every enumerated sense that is a distinct PERSON (a man or a woman, not a people, tribe, place, common noun or adjective) and that the one record is not. For each: the sense number, the name, who it is in a short phrase from the definition, and the verses from the list below where the word means THIS person — read each verse; give a verse only when the verse shows it is this person (his father, his time, his office). A verse may belong to no missing person. confidence 0..1.
- If the entry is really a common noun or adjective (e.g. "daughter", "great") whose personal senses are only incidental, or every person sense is already the record, missing is [].
- note: one sentence, anything a reviewer should know (e.g. "senses 2 and 3 may be the same man").

Verses are written as "BOOK chapter:verse" exactly as listed; copy them exactly.

Entry:
{entry}
"""


def ref(r):
    b, cv = r.split()
    return f"{BOOKS[int(b) - 1]} {cv}" if int(b) <= len(BOOKS) else r


def check(item, ans):
    allowed = set(item["verses"])
    errs, seen = [], {}
    for m in ans.get("missing", []):
        for v in m["verses"]:
            if v not in allowed:
                errs.append(f"verse {v} is not in the list")
            if v in seen and seen[v] != m["sense"]:
                errs.append(f"verse {v} given to senses {seen[v]} and {m['sense']}")
            seen[v] = m["sense"]
    return errs


def run(e):
    out = OUT / f"{e['strong']}.json"
    if out.exists():
        return e["strong"], "kept"
    kjv = json.loads((HERE / "kjv.json").read_text(encoding="utf-8"))
    verses = e["verses"] if len(e["verses"]) <= 40 else []
    item = {"strong": e["strong"], "lemma": e["lemma"], "transliteration": e["translit"], "morphology": e["morphology"],
            "definition": e["definition"], "kjv_renderings": e["kjv"], "the_one_record": e["records"][0],
            "verses": [ref(v) for v in verses],
            "verse_text": {ref(v): kjv.get(v, "") for v in verses},
            "occurrences": len(e["verses"]) if len(e["verses"]) <= 40 else "more than 40 (a common word); not listed"}
    prompt = PROMPT.replace("{entry}", json.dumps(item, ensure_ascii=False, indent=1))
    prev = RAW / f"{e['strong']}.rejected.json"
    if prev.exists():
        prompt += "\n\nA previous answer was rejected by the checker: " + "; ".join(json.loads(prev.read_text(encoding="utf-8"))["errors"]) + ". Fix exactly that."
    last = RAW / f"{e['strong']}.last.json"
    args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
            "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last), "-C", str(RAW)]
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
    if p.returncode or not last.exists():
        return e["strong"], f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}"
    ans = json.loads(last.read_text(encoding="utf-8"))
    errs = check(item, ans)
    if errs:
        prev.write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1), encoding="utf-8")
        return e["strong"], "rejected: " + "; ".join(errs[:3])
    out.write_text(json.dumps({"strong": e["strong"], "record": e["records"][0], **ans, "model": MODEL, "effort": EFFORT,
                               "at": datetime.now(timezone.utc).isoformat(timespec="seconds")}, ensure_ascii=False, indent=1),
                   encoding="utf-8")
    return e["strong"], "ok"


entries = json.loads((HERE / "screen.json").read_text(encoding="utf-8"))
with ThreadPoolExecutor(max_workers=3) as pool:
    for strong, status in pool.map(run, entries):
        print(strong, status, flush=True)
