"""Second reading of the accent and breathing corrections the Opus judge did not see (judge/half-*.json took every letter
and word correction and 60 of these). The Opus judge found that the reader's few wrong 'marks' were the page's own
misprints silently normalised (Κύριου, ἀληθεία, an unaccented οὐδε), so this reading is asked only whether the page
prints the proposed form exactly, misprint or not. gpt-6.1-sol, a page at a time, script-checked, three in parallel.

Resumable: a page already in verify/ is kept, so a run stopped by Codex's usage limit continues where it stopped. It stops
asking as soon as Codex says the limit is reached. progress.json beside this file says how far it got; the owner's console
shows it. Run it as the avioniq action pages-ottley.

Out: verify/page-NNN.json, progress.json. Usage: verify.py [folder holding the page images] [--status]
--status only writes progress.json from what verify/ holds, as stopped, and asks Codex nothing."""
import json, os, subprocess, sys, threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
# The scans are archive.org's and stay out of git: in the control folder, beside the repositories.
ARGS = [a for a in sys.argv[1:] if a != "--status"]
PAGES = Path(ARGS[0]) if ARGS else HERE.parents[4] / ".scans" / "ottley"
OUT = HERE / "verify"; RAW = PAGES.parent / "ottley-raw"; PROGRESS = HERE / "progress.json"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
SCHEMA = {"type": "object", "additionalProperties": False, "required": ["items"],
          "properties": {"items": {"type": "array", "items": {
              "type": "object", "additionalProperties": False, "required": ["id", "prints", "actual"],
              "properties": {"id": {"type": "integer"}, "prints": {"type": "string", "enum": ["A", "B", "neither"]},
                             "actual": {"type": "string"}}}}}}
(HERE / "verify-schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are checking a printed page of R. R. Ottley, The Book of Isaiah according to the Septuagint, vol. II, 1904 (images: {images}; the verse numbers are small superscripts in the running text, the margin numbers are line numbers). Answer only with the JSON the schema asks for; do not run any commands or write any files.

For each item below, find the words in the given verse of the MAIN TEXT (not the apparatus) and say which of the two forms the page prints, letter by letter, accent by accent, breathing by breathing:
- prints = "A" if the page prints form A exactly, "B" if it prints form B exactly, "neither" if it prints something else (then give it in actual).
The page has misprints of its own. If the page prints a form that is grammatically wrong, that is still what it prints: report it, do not correct it. Look closely at each accent and breathing; zoom if you need to. Ignore punctuation.

Items:
{items}
"""

lock = threading.Lock()
limit = {"said": None}
started = datetime.now(timezone.utc).isoformat(timespec="seconds")


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def progress(state, by_page, failed, note=None):
    """What the console shows: pages and items read so far, counted from verify/ so a resumed run counts the old ones."""
    read = {"right": 0, "wrong": 0, "neither": 0}
    done = 0
    for page in by_page:
        out = OUT / f"page-{int(page):03d}.json"
        if out.exists():
            done += 1
            for x in json.loads(out.read_text(encoding="utf-8")):
                read[x["verdict"]] += 1
    body = {"title": "Ottley's Isaiah: second reading of the accent corrections", "action": "pages-ottley",
            "state": state, "started": started, "updated": now(),
            "pages": {"total": len(by_page), "done": done, "failed": failed},
            "items": {"total": sum(len(v) for v in by_page.values()), "read": sum(read.values()), **read},
            "note": note,
            "after": "Tell Claude it has finished: accept.py and write_table.py turn the confirmed ones into corrections, then tests and the load."}
    with lock:
        tmp = PROGRESS.with_suffix(".tmp")
        tmp.write_text(json.dumps(body, ensure_ascii=False, indent=1), encoding="utf-8")
        tmp.replace(PROGRESS)


def run(page, items):
    name = f"page-{int(page):03d}"
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    if limit["said"]:
        return name, "skipped: Codex usage limit"
    images = items[0]["images"]
    # A and B are shown in a fixed order per item, alternating, so the reading cannot learn that B is always the proposal
    lines, key = [], {}
    for n, r in enumerate(items):
        a, b = (r["ours"], r["printed"]) if n % 2 == 0 else (r["printed"], r["ours"])
        key[r["id"]] = "B" if n % 2 == 0 else "A"
        lines.append(f'id {r["id"]}, verse {r["verse"]}: A = {a} | B = {b}')
    prompt = PROMPT.replace("{images}", ", ".join(Path(i).name for i in images)).replace("{items}", "\n".join(lines))
    extra = ""
    for attempt in (1, 2):
        last = RAW / f"verify-{name}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "verify-schema.json"), "-o", str(last),
                "-C", str(RAW)]
        for img in images:
            args += ["-i", img]
        p = subprocess.run(args, input=prompt + extra, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=2400)
        if p.returncode or not last.exists():
            said = p.stderr.strip()
            if "usage limit" in said:
                limit["said"] = said.splitlines()[-1][-160:]
            return name, f"codex exit {p.returncode}: {said[-200:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        got = [x["id"] for x in ans["items"]]
        if got == [r["id"] for r in items]:
            res = [{"id": x["id"], "verdict": "right" if x["prints"] == key[x["id"]] else "wrong" if x["prints"] != "neither" else "neither",
                    "actual": x["actual"]} for x in ans["items"]]
            out.write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding="utf-8")
            return name, f"ok {sum(x['verdict'] == 'right' for x in res)}/{len(res)} confirmed"
        extra = f"\n\nYour previous answer was refused: the ids must be exactly {[r['id'] for r in items]} in that order. Answer again."
    return name, "rejected"


rows = json.loads((HERE / "kinds.json").read_text(encoding="utf-8"))
judged = {x["id"] for n in (1, 2) for x in json.loads((HERE / "judge" / f"half-{n}.json").read_text(encoding="utf-8"))}
by_page = {}
for i, r in enumerate(rows):
    if r["kind"] == "marks" and i not in judged:
        leaf = int(r["image"].rsplit("-", 1)[1].split(".")[0])
        r["id"], r["images"] = i, [str(PAGES / r["image"]), str(PAGES / f"IsaiahAccordingToTheSeptuagint-{leaf + 1}.jpg")]
        by_page.setdefault(r["page"], []).append(r)
missing = sorted({i for v in by_page.values() for i in v[0]["images"] if not Path(i).exists()})
if missing:
    progress("failed", by_page, 0, f"{len(missing)} page images are missing from {PAGES}, e.g. {Path(missing[0]).name}")
    sys.exit(f"{len(missing)} page images missing from {PAGES}")
if "--status" in sys.argv:
    progress("stopped", by_page, 0)
    sys.exit(0)
print(sum(len(v) for v in by_page.values()), "items on", len(by_page), "pages", flush=True)
failed = 0
progress("running", by_page, failed)
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(lambda kv: run(*kv), by_page.items()):
        print(name, status, flush=True)
        if status.startswith(("codex", "rejected")):
            failed += 1
        progress("running", by_page, failed)
if limit["said"]:
    progress("limit", by_page, failed, f"Codex stopped at its usage limit ({limit['said']}). Start it again after that; it continues where it stopped.")
    print("STOPPED at the Codex usage limit", flush=True)
    sys.exit(2)
progress("done" if failed == 0 else "failed", by_page, failed,
         None if failed == 0 else f"{failed} pages were not read; start it again to retry them.")
print("DONE", flush=True)
sys.exit(0 if failed == 0 else 1)
