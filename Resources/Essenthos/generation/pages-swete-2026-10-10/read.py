"""Swete's Septuagint read against the printed page, a page at a time: the avioniq action pages-swete.

Each page of .scans/swete/items.json (prepare.py) is read twice by gpt-6.1-sol, as Ottley's Isaiah was, but with no Claude judge:
  1. read: the page's verses as printed, and every word where our text differs (run.py of pages-ottley's prompt).
  2. check: each proposed correction again, as a choice between our form and the proposed one shown in alternating
     order (verify.py of pages-ottley), asking only what the page prints, misprint or not. On Ottley this second
     reading and the Opus judge agreed on the large majority; a correction is taken only where both readings agree.
Both are script-checked, with one retry.

Resumable: a page already in out/ is kept, so a run stopped by Codex's usage limit continues where it stopped, and it
stops asking as soon as Codex says the limit is reached. The page images are fetched from archive.org when their page
is reached, to .scans/swete/pages in the control folder. progress.json beside this file says how far it got; the
owner's console shows it.

Out: out/v<volume>-p<page>.json, progress.json. Usage: read.py [--status] [--pages N]
--status only writes progress.json from what out/ holds; --pages N stops after N new pages (for a trial)."""
import json, os, subprocess, sys, threading, time, urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
SCANS = HERE.parents[4] / ".scans" / "swete"
PAGES = SCANS / "pages"; RAW = SCANS / "raw"; OUT = HERE / "out"; PROGRESS = HERE / "progress.json"
for d in (PAGES, RAW, OUT):
    d.mkdir(parents=True, exist_ok=True)
# Codex moves its CLI to a new folder on every update, so it is looked up for each call
# (scripts/codex_cli.py), not once when the run starts.
sys.path.insert(0, str(next(p / "scripts" for p in Path(__file__).resolve().parents if (p / "scripts" / "codex_cli.py").is_file())))
from codex_cli import executable as codex_executable
MODEL, EFFORT = "gpt-6.1-sol", "high"
LIMIT_PAGES = int(sys.argv[sys.argv.index("--pages") + 1]) if "--pages" in sys.argv else None

CORRECTION = {"type": "object", "additionalProperties": False, "required": ["ours", "printed", "why"],
              "properties": {"ours": {"type": "string"}, "printed": {"type": "string"}, "why": {"type": "string"}}}
READ_SCHEMA = {"type": "object", "additionalProperties": False, "required": ["image", "verses", "note", "confidence"],
               "properties": {
                   "image": {"type": "string"},
                   "verses": {"type": "array", "items": {
                       "type": "object", "additionalProperties": False, "required": ["verse", "printed", "corrections"],
                       "properties": {"verse": {"type": "string"}, "printed": {"type": "string"},
                                      "corrections": {"type": "array", "items": CORRECTION}}}},
                   "note": {"type": "string"},
                   "confidence": {"type": "number"}}}
CHECK_SCHEMA = {"type": "object", "additionalProperties": False, "required": ["items"],
                "properties": {"items": {"type": "array", "items": {
                    "type": "object", "additionalProperties": False, "required": ["id", "prints", "actual"],
                    "properties": {"id": {"type": "integer"}, "prints": {"type": "string", "enum": ["A", "B", "neither"]},
                                   "actual": {"type": "string"}}}}}}
(HERE / "read-schema.json").write_text(json.dumps(READ_SCHEMA), encoding="utf-8")
(HERE / "check-schema.json").write_text(json.dumps(CHECK_SCHEMA), encoding="utf-8")

EDITION = ("H. B. Swete, The Old Testament in Greek according to the Septuagint, Cambridge, vol. {volume} "
           "(the scan the transcription was made from)")
READ = """You are proofreading a digital transcription of {edition} against scans of its printed pages. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Printed page {page}. The attached images are the scan's leaf for this page and the leaf after it, named in order: {images}. The page's running number is at its top. The main text is the upper part of the page; the apparatus of variant readings in small type at the foot is NOT the text. In the margins stand chapter numbers in Roman figures, line numbers, and manuscript sigla or section letters; they are not the text either. Verse numbers are small figures in the text.

The verses that begin on this page, as our transcription has them:
{ours}

Do this for every one of those verses, in order:
1. verse = its reference exactly as given above.
2. printed = the verse exactly as the edition prints it in the main text: every word, with its accents, breathings and punctuation, in Unicode Greek. If the last verse runs onto the next page, continue it from there.
3. corrections = every place where our text of that verse differs from what is printed in a word: a wrong or missing accent or breathing, a misread letter, a lost or extra word, words fused or split, a stray letter, siglum, numeral or line number read into the text (then printed = ""). ours = our word or words copied exactly as they stand in our text above (enough of them to be found once in that verse), printed = what the page prints in their place, why = a few words on what went wrong.
   The page has misprints of its own; report what it prints, do not correct it. Do not report differences of punctuation alone, of letter case, of final sigma, of iota subscript versus adscript, or of spacing alone, unless the word itself changes. If our verse is right, corrections = [].
image = the file name of the image holding the page. note = anything a reader should know (e.g. a verse the page numbers differently, or a word you could not read). confidence 0..1 that the corrections are right and complete.
"""
CHECK = """You are checking a printed page of {edition}, printed page {page} (images: {images}; the main text is the upper part of the page, not the apparatus at its foot or the margins). Answer only with the JSON the schema asks for; do not run any commands or write any files.

For each item below, find the words in the given verse of the MAIN TEXT and say which of the two forms the page prints, letter by letter, accent by accent, breathing by breathing:
- prints = "A" if the page prints form A exactly, "B" if it prints form B exactly, "neither" if it prints something else (then give it in actual).
A form shown as (nothing) means the words are not printed there at all. The page has misprints of its own. If the page prints a form that is grammatically wrong, that is still what it prints: report it, do not correct it. Look closely at each accent and breathing; zoom if you need to. Ignore punctuation.

Items:
{items}
"""

lock = threading.Lock()
limit = {"said": None}
started = datetime.now(timezone.utc).isoformat(timespec="seconds")


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def name_of(item):
    return f"v{item['volume']}-p{item['page']:04d}"


def progress(state, items, failed, note=None):
    """What the console shows, counted from out/ so a resumed run counts the pages read before it."""
    counts = {"right": 0, "wrong": 0, "neither": 0}
    done = found = 0
    for item in items:
        out = OUT / f"{name_of(item)}.json"
        if out.exists():
            done += 1
            for c in json.loads(out.read_text(encoding="utf-8"))["corrections"]:
                found += 1
                counts[c["check"]] += 1
    body = {"title": "Swete's Septuagint: every page read twice against the scan", "action": "pages-swete",
            "state": state, "started": started, "updated": now(),
            "pages": {"total": len(items), "done": done, "failed": failed},
            "items": {"total": found, "read": sum(counts.values()), **counts},
            "note": note,
            "after": "Tell Claude it has finished: the corrections both readings agree on go into the Swete converter's "
                     "page table, then tests and the load. A book can be applied before the whole edition is read."}
    with lock:
        tmp = PROGRESS.with_suffix(".tmp")
        tmp.write_text(json.dumps(body, ensure_ascii=False, indent=1), encoding="utf-8")
        tmp.replace(PROGRESS)


def fetch(item):
    for image in item["images"]:
        f = PAGES / image
        if f.exists() and f.stat().st_size > 10_000:
            continue
        leaf = int(image.rsplit("-", 1)[1].split(".")[0])
        for attempt in range(4):
            try:
                urllib.request.urlretrieve(f"https://archive.org/download/{item['scan']}/page/n{leaf - 1}_w1800.jpg", f)
                break
            except OSError:
                time.sleep(10 * (attempt + 1))
        else:
            return False
    return True


def codex(prompt, schema, images, last):
    """One reading; the answer, or a string saying why there is none."""
    if last.exists():
        last.unlink()
    args = [codex_executable(), "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
            "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / schema), "-o", str(last), "-C", str(RAW)]
    for image in images:
        args += ["-i", str(PAGES / image)]
    p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=2400)
    if p.returncode or not last.exists():
        said = p.stderr.strip()
        if "usage limit" in said:
            limit["said"] = said.splitlines()[-1][-160:]
        return f"codex exit {p.returncode}: {said[-200:]}"
    return json.loads(last.read_text(encoding="utf-8"))


def check_read(ans, item):
    errs = []
    if ans["image"] not in item["images"]:
        errs.append(f"image must be one of {item['images']}")
    want = [v["verse"] for v in item["verses"]]
    got = [v["verse"] for v in ans["verses"]]
    if got != want:
        errs.append(f"verses must be exactly {want} in that order; you gave {got}")
    ours = {v["verse"]: v["ours"] for v in item["verses"]}
    for v in ans["verses"]:
        if not any("Ͱ" <= ch <= "Ͽ" or "ἀ" <= ch <= "῿" for ch in v["printed"]):
            errs.append(f"{v['verse']}: printed must be the Greek text of the verse")
        for c in v["corrections"]:
            if not c["ours"] or c["ours"] not in (ours.get(v["verse"]) or ""):
                errs.append(f"{v['verse']}: ours '{c['ours']}' is not found in our text of the verse; copy it exactly")
            if c["ours"] == c["printed"]:
                errs.append(f"{v['verse']}: a correction whose printed equals ours is no correction")
    if not 0 <= ans["confidence"] <= 1:
        errs.append("confidence must be 0..1")
    return errs


def run(item):
    name = name_of(item)
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    if limit["said"]:
        return name, "skipped: Codex usage limit"
    if not fetch(item):
        return name, "failed: the page image could not be fetched from archive.org"
    edition = EDITION.replace("{volume}", str(item["volume"]))
    images = ", ".join(item["images"])
    prompt = (READ.replace("{edition}", edition).replace("{page}", str(item["page"])).replace("{images}", images)
              .replace("{ours}", "\n".join(f"{v['verse']}: {v['ours']}" for v in item["verses"])))
    extra, ans = "", None
    for attempt in (1, 2):
        ans = codex(prompt + extra, "read-schema.json", item["images"], RAW / f"{name}.read.json")
        if isinstance(ans, str):
            return name, ans
        errs = check_read(ans, item)
        if not errs:
            break
        extra = "\n\nYour previous answer was refused by the checker: " + "; ".join(errs[:12]) + ". Answer again."
    else:
        return name, "rejected: " + "; ".join(errs[:6])

    proposed = [{"id": n, "verse": v["verse"], **c} for n, (v, c) in
                enumerate((v, c) for v in ans["verses"] for c in v["corrections"])]
    if proposed:
        # A and B alternate, so the reading cannot learn that B is always the proposal
        lines, key = [], {}
        for r in proposed:
            proposal = r["printed"] or "(nothing)"
            a, b = (r["ours"], proposal) if r["id"] % 2 == 0 else (proposal, r["ours"])
            key[r["id"]] = "B" if r["id"] % 2 == 0 else "A"
            lines.append(f'id {r["id"]}, {r["verse"]}: A = {a} | B = {b}')
        prompt = (CHECK.replace("{edition}", edition).replace("{page}", str(item["page"])).replace("{images}", images)
                  .replace("{items}", "\n".join(lines)))
        extra = ""
        for attempt in (1, 2):
            second = codex(prompt + extra, "check-schema.json", item["images"], RAW / f"{name}.check.json")
            if isinstance(second, str):
                return name, second
            if [x["id"] for x in second["items"]] == [r["id"] for r in proposed]:
                break
            extra = f"\n\nYour previous answer was refused: the ids must be exactly {[r['id'] for r in proposed]} in that order. Answer again."
        else:
            return name, "rejected: the check answered other ids"
        for r, x in zip(proposed, second["items"]):
            r["check"] = "right" if x["prints"] == key[r["id"]] else "neither" if x["prints"] == "neither" else "wrong"
            r["actual"] = x["actual"]

    out.write_text(json.dumps({**{k: item[k] for k in ("volume", "page", "scan", "leaf", "images")},
                               "image": ans["image"], "verses": [{**v, "printed": a["printed"]} for v, a in zip(item["verses"], ans["verses"])],
                               "corrections": proposed, "note": ans["note"], "confidence": ans["confidence"],
                               "model": MODEL, "at": now()}, ensure_ascii=False, indent=1), encoding="utf-8")
    return name, f"ok {sum(r['check'] == 'right' for r in proposed)}/{len(proposed)} corrections confirmed"


items = json.loads((SCANS / "items.json").read_text(encoding="utf-8"))
if "--status" in sys.argv:
    progress("stopped", items, 0)
    sys.exit(0)
todo = [i for i in items if not (OUT / f"{name_of(i)}.json").exists()]
if LIMIT_PAGES is not None:
    todo = todo[:LIMIT_PAGES]
print(len(items), "pages,", len(items) - len([i for i in items if not (OUT / f"{name_of(i)}.json").exists()]), "already read;",
      len(todo), "to read now", flush=True)
failed = 0
progress("running", items, failed)
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, todo):
        print(name, status, flush=True)
        if status.startswith(("codex", "rejected", "failed")):
            failed += 1
        progress("running", items, failed)
if limit["said"]:
    progress("limit", items, failed, f"Codex stopped at its usage limit ({limit['said']}). Start it again after that; it continues where it stopped.")
    print("STOPPED at the Codex usage limit", flush=True)
    sys.exit(2)
done = all((OUT / f"{name_of(i)}.json").exists() for i in items)
progress("done" if done else "stopped" if failed == 0 else "failed", items, failed,
         None if failed == 0 else f"{failed} pages were not read; start it again to retry them.")
print("DONE" if done else "STOPPED", flush=True)
sys.exit(0 if failed == 0 else 1)
