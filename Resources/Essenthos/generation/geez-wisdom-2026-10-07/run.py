"""Map Ge'ez verses of Job, Psalms and Song to Brenton's Greek verses, one chapter per Codex run.

Codex only reads the prompt and answers in the JSON schema; nothing is written by it. Each answer is
validated (every Ge'ez verse once and in order, Greek ranges inside the chapter and not reused) and kept
in out/<book>-<ch>.json with the model, effort and date. Rerunning skips chapters already accepted.

usage: python run.py [--workers N] [book:chapter ...]   (default: every fetched chapter)
"""
import json, os, re, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
CH = HERE / "chapters"
OUT = HERE / "out"
RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"

PROMPT = """You are aligning verse divisions between two witnesses of the same biblical chapter. Answer only with the JSON the schema asks for; do not run any commands or write any files.

The Ethiopic (Ge'ez) church text and the Greek Septuagint (Brenton's edition) of {book} chapter {chapter} hold the same text but divide it into verses differently inside the chapter. Brenton's English translation of the Greek is given to help you read the Greek. Read every Ge'ez verse against the Greek and say which Greek verse or verses it answers.

Rules:
- Cover every Ge'ez verse exactly once, in order, as single verses ("3") or contiguous ranges ("3-4"). Use a range on the Ge'ez side only when the Ge'ez divides one Greek verse into several.
- The Greek side is a single verse or a contiguous range ("2-3") when one Ge'ez verse spans several Greek verses. Greek verses should normally be used at most once and in order; if a Ge'ez verse and its neighbour share one Greek verse, join the two Ge'ez verses into one line instead.
- If a Ge'ez verse has no Greek counterpart (an addition, a title the Greek lacks), give greek "" and say why in the note. Greek verses the Ge'ez lacks are simply not used.
- Confidence: 0.9 where the two plainly say the same; 0.7-0.85 where the division differs or a line has to join a passage; 0.5 where you are unsure. A note is required below 0.9.
- Judge by meaning (names, numbers, distinctive words, the order of clauses), not by verse length alone.

GE'EZ ({gn} verses):
{geez}

GREEK, Brenton ({kn} verses):
{greek}

ENGLISH, Brenton's translation of the Greek:
{english}
"""


def fmt(vs):
    return "\n".join(f"{v['n']}. {v['t']}" for v in vs)


def rng(s):
    a, _, b = s.partition("-")
    return int(a), int(b or a)


def validate(d, lines):
    g = [v["n"] for v in d["GEEZ81"]]
    k = {v["n"] for v in d["GRCBRENT"]}
    errs, seen, used, last_k = [], [], set(), 0
    for ln in lines:
        if not re.fullmatch(r"\d+(-\d+)?", ln["geez"]):
            errs.append(f"bad geez {ln['geez']!r}"); continue
        a, b = rng(ln["geez"]); seen += range(a, b + 1)
        if ln["greek"]:
            if not re.fullmatch(r"\d+(-\d+)?", ln["greek"]):
                errs.append(f"bad greek {ln['greek']!r}"); continue
            x, y = rng(ln["greek"])
            if not (x <= y and x in k and y in k):
                errs.append(f"greek {ln['greek']} outside the chapter")
            explained = ln["note"].strip() and ln["confidence"] <= 0.85
            if used & set(range(x, y + 1)) and not explained:
                errs.append(f"greek {ln['greek']} reused")
            if x < last_k and not explained:
                errs.append(f"greek {ln['greek']} out of order")
            used |= set(range(x, y + 1)); last_k = max(last_k, y)
        if not 0 < ln["confidence"] <= 1:
            errs.append(f"confidence {ln['confidence']}")
    if seen != g:
        errs.append(f"geez coverage {seen[:5]}... != {g[:5]}... ({len(seen)} vs {len(g)})")
    return errs, sorted(k - used)


def run(path):
    d = json.loads(path.read_text(encoding="utf-8"))
    name = path.stem
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept", 0
    prev = RAW / f"{name}.rejected.json"
    feedback = ""
    if prev.exists():
        r = json.loads(prev.read_text(encoding="utf-8"))
        feedback = ("\n\nA previous answer for this chapter was rejected by the checker: " + "; ".join(r["errors"]) +
                    ". The Ge'ez verse numbers are exactly those listed above (1 to " + str(len(d["GEEZ81"])) + "). "
                    "If the Ge'ez truly puts a passage in another order than the Greek, or truly repeats it, you may use a Greek verse "
                    "out of order or a second time, but only with a note saying so and confidence 0.8 or lower. Previous answer: " +
                    json.dumps(r["lines"], ensure_ascii=False))
    prompt = PROMPT.format(book=d["book"], chapter=d["chapter"], gn=len(d["GEEZ81"]), kn=len(d["GRCBRENT"]),
                           geez=fmt(d["GEEZ81"]), greek=fmt(d["GRCBRENT"]), english=fmt(d["BRENTON"])) + feedback
    last = RAW / f"{name}.last.json"
    args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
            "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"),
            "-o", str(last), "-C", str(RAW)]
    t = time.time()
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
    (RAW / f"{name}.log").write_text(p.stdout[-20000:] + "\n--- stderr\n" + p.stderr[-5000:], encoding="utf-8")
    if p.returncode or not last.exists():
        return name, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}", round(time.time() - t)
    try:
        lines = json.loads(last.read_text(encoding="utf-8"))["lines"]
    except (ValueError, KeyError) as e:
        return name, f"unreadable answer: {e}", round(time.time() - t)
    errs, unused = validate(d, lines)
    if errs:
        (RAW / f"{name}.rejected.json").write_text(json.dumps({"errors": errs, "lines": lines}, ensure_ascii=False, indent=1), encoding="utf-8")
        return name, "rejected: " + "; ".join(errs[:4]), round(time.time() - t)
    out.write_text(json.dumps({"book": d["book"], "chapter": d["chapter"], "model": MODEL, "effort": EFFORT,
                               "at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                               "greek_unused": unused, "lines": lines}, ensure_ascii=False, indent=1), encoding="utf-8")
    return name, f"ok {len(lines)} lines, greek unused {unused}", round(time.time() - t)


args = sys.argv[1:]
workers = 3
if args[:1] == ["--workers"]:
    workers, args = int(args[1]), args[2:]
paths = [CH / f"{a.split(':')[0]}-{int(a.split(':')[1]):03d}.json" for a in args] if args else sorted(CH.glob("*.json"))
with ThreadPoolExecutor(max_workers=workers) as pool:
    for name, status, secs in pool.map(run, paths):
        print(f"{name}\t{secs}s\t{status}", flush=True)
