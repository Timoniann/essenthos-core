"""Translate the notes of our own records into Ukrainian, German and Spanish, one record per Codex run.

Codex only answers in the JSON schema; nothing is written by it. Each answer is checked (every reference kept,
no English sentence left, length plausible) and kept in out/<file>--<slug>.json with the English's SHA-256,
so a note the owner later edits in the console makes its translation stale. Rerunning skips what is kept.
"""
import json, os, re, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "medium"
REF = re.compile(r"\b[1-3]?[A-Z]{2,3} \d+:\d+(?:-\d+(?::\d+)?)?(?:, ?\d+(?::\d+)?(?:-\d+)?)*")

PROMPT = """Translate one short encyclopedia note about a biblical {kind} into Ukrainian, German and Spanish. Answer only with the JSON the schema asks for; do not run any commands or write any files.

The note is our own prose for a Bible research site. Translate it faithfully and naturally, as an educated native writer of each language would write it for this site — same facts, same order, same hedges ("may", "is not certain"), nothing added or left out.

Rules:
- Verse references such as "EXO 25:8-9" or "1KI 6:1" stay exactly as written, character for character.
- A Bible book named in words ("1 Samuel 21:10-15", "Nehemiah 7:65", "the book of Ezra") is translated: the name as that language's classic Bible titles the book (Ukrainian as Ohienko: 1 Самуїлова, Неемія, Ездра, 1 Хроніки…), the numbers kept.
- Biblical names of people, places and peoples: use the form that language's classic Bible uses — Ukrainian as in Ivan Ohienko's translation (e.g. Мойсей, Ааро́н without stress marks: Аарон, Бецал’їл), German as in Luther 1912, Spanish as in the Reina-Valera 1909. The record itself is called: {names}.
- "the LORD" (the divine name) is «Господь» in Ukrainian, "der HERR" in German, "Jehová" in Spanish (as the Reina-Valera 1909 writes it); "God" is Бог / Gott / Dios.
- Ukrainian writes «проект», never «проєкт». No English words may remain in the Ukrainian, German or Spanish text except inside verse references.
- Keep it one paragraph; no notes of your own, no quotation marks around the whole text.

The note (English):
{english}
"""

SCHEMA = {"type": "object", "additionalProperties": False, "required": ["ukr", "deu", "spa"],
          "properties": {k: {"type": "string"} for k in ("ukr", "deu", "spa")}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")
ENGLISH_WORDS = re.compile(r"\b(the|and|of|which|that|with|was|were|from)\b")


def check(item, ans):
    errs = []
    refs = REF.findall(item["english"])
    for lang in ("ukr", "deu", "spa"):
        t = ans.get(lang, "")
        if not t.strip():
            errs.append(f"{lang} empty"); continue
        missing = [r for r in refs if r not in t]
        if missing:
            errs.append(f"{lang} lost references {missing[:3]}")
        ratio = len(t) / max(1, len(item["english"]))
        if not 0.6 <= ratio <= 1.9:
            errs.append(f"{lang} length ratio {ratio:.2f}")
        stripped = REF.sub("", t)
        if lang != "deu" and len(ENGLISH_WORDS.findall(stripped)) > 1:
            errs.append(f"{lang} has English words")
    latin = re.findall(r"[A-Z]?[a-z]{3,}", REF.sub("", ans.get("ukr", "")))
    if latin:
        errs.append(f"ukr keeps English words {latin[:4]}")
    if "проєкт" in ans.get("ukr", ""):
        errs.append("ukr writes проєкт")
    return errs


def run(item):
    name = f"{item['file'][:-5]}--{item['slug']}"
    out = OUT / f"{name}.json"
    if out.exists() and json.loads(out.read_text(encoding="utf-8"))["sha256"] == item["sha256"]:
        return name, "kept", 0
    kind = {"ObjectRecords.json": "object", "ObservanceRecords.json": "feast or appointed time", "NarrativeRecords.json": "narrative",
            "TitleRecords.json": "title", "Peoples.json": "people"}.get(item["file"], "person")
    names = "; ".join([f"English {item['name']}"] + [f"{k} {v}" for k, v in (item.get("names") or {}).items()])
    prompt = PROMPT.format(kind=kind, names=names, english=item["english"])
    prev = RAW / f"{name}.rejected.json"
    if prev.exists():
        r = json.loads(prev.read_text(encoding="utf-8"))
        prompt += "\n\nA previous answer was rejected by the checker: " + "; ".join(r["errors"]) + ". Fix exactly that."
    last = RAW / f"{name}.last.json"
    args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
            "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last), "-C", str(RAW)]
    t = time.time()
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1200)
    if p.returncode or not last.exists():
        return name, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}", round(time.time() - t)
    try:
        ans = json.loads(last.read_text(encoding="utf-8"))
    except ValueError as e:
        return name, f"unreadable: {e}", round(time.time() - t)
    errs = check(item, ans)
    if errs:
        prev.write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1), encoding="utf-8")
        return name, "rejected: " + "; ".join(errs[:4]), round(time.time() - t)
    out.write_text(json.dumps({"file": item["file"], "slug": item["slug"], "sha256": item["sha256"], "english": item["english"],
                               **ans, "model": MODEL, "effort": EFFORT,
                               "at": datetime.now(timezone.utc).isoformat(timespec="seconds")}, ensure_ascii=False, indent=1), encoding="utf-8")
    return name, "ok", round(time.time() - t)


items = json.loads((HERE / "notes-english.json").read_text(encoding="utf-8"))
args = sys.argv[1:]
workers = 3
if args[:1] == ["--workers"]:
    workers, args = int(args[1]), args[2:]
if args:
    items = [i for i in items if i["slug"] in args]
with ThreadPoolExecutor(max_workers=workers) as pool:
    for name, status, secs in pool.map(run, items):
        print(f"{name}\t{secs}s\t{status}", flush=True)
