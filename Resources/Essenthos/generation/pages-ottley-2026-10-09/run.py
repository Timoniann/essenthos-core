"""Ottley's Isaiah page by page: gpt-6.1-sol reads the printed page (the scan's leaf and its two neighbours) and says,
verse by verse, what the edition prints and where our OTTLEY text differs. Script-checked (every verse of the page
answered, every `ours` found in our text of that verse); one retry with the errors; three in parallel.
Out: out/page-NNN.json. Usage: run.py <scratch folder holding pages/> [page ...]"""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
PAGES = Path(sys.argv[1]) / "pages"
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
CORRECTION = {"type": "object", "additionalProperties": False, "required": ["ours", "printed", "why"],
              "properties": {"ours": {"type": "string"}, "printed": {"type": "string"}, "why": {"type": "string"}}}
SCHEMA = {"type": "object", "additionalProperties": False, "required": ["image", "verses", "note", "confidence"],
          "properties": {
              "image": {"type": "string"},
              "verses": {"type": "array", "items": {
                  "type": "object", "additionalProperties": False, "required": ["verse", "printed", "corrections"],
                  "properties": {"verse": {"type": "string"}, "printed": {"type": "string"},
                                 "corrections": {"type": "array", "items": CORRECTION}}}},
              "note": {"type": "string"},
              "confidence": {"type": "number"}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are proofreading a digital transcription of R. R. Ottley, The Book of Isaiah according to the Septuagint (Codex Alexandrinus), vol. II, 1904, against scans of its printed pages. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Printed page {page}. The attached images are the scan's leaf for this page and the leaves before and after it, named in order: {images}. Find the page whose running text holds these verses (chapter numbers are roman numerals, verse numbers are small superscripts in the text; the numbers in the outer margin are line numbers, not verses).

The verses that begin on this page, as our transcription has them:
{ours}
{before}
Do this for every one of those verses, in order:
1. verse = its reference exactly as given above (e.g. "1:3").
2. printed = the verse exactly as the edition prints it in the main text (not the apparatus at the foot of the page): every word, with its accents, breathings and punctuation, in Unicode Greek. If the last verse runs onto the next page, continue it from there.
3. corrections = every place where our text of that verse differs from what is printed in a word: a wrong or missing accent or breathing (ὃ for ὁ, οὗ for οὐ, ἥν for ἣν), a misread letter, a lost or extra word, words fused or split, a stray letter, siglum, line number or footnote letters read into the text (then printed = ""). ours = our word or words copied exactly as they stand in our text above (enough of them to be found once in that verse), printed = what the page prints in their place, why = a few words on what went wrong.
   Do not report differences of punctuation alone, of letter case, of final sigma, of iota subscript versus adscript, or of spacing alone, unless the word itself changes. If our verse is right, corrections = [].
image = the file name of the image holding the page. note = anything a reader should know (e.g. a verse the page numbers differently, or a word you could not read). confidence 0..1 that the corrections are right and complete.
"""


def check(ans, item):
    errs = []
    if ans["image"] not in item["images"]:
        errs.append(f"image must be one of {item['images']}")
    got = [v["verse"] for v in ans["verses"]]
    if got != item["verses"]:
        errs.append(f"verses must be exactly {item['verses']} in that order; you gave {got}")
    for v in ans["verses"]:
        if not any("Ͱ" <= ch <= "Ͽ" or "ἀ" <= ch <= "῿" for ch in v["printed"]):
            errs.append(f"{v['verse']}: printed must be the Greek text of the verse")
        if any("A" <= ch <= "Z" for ch in v["printed"]):
            errs.append(f"{v['verse']}: printed contains a Latin capital letter; give the Greek text only")
        ours = item["ours"].get(v["verse"]) or ""
        for c in v["corrections"]:
            if not c["ours"] or c["ours"] not in ours:
                errs.append(f"{v['verse']}: ours '{c['ours']}' is not found in our text of the verse; copy it exactly")
            if c["ours"] == c["printed"]:
                errs.append(f"{v['verse']}: a correction whose printed equals ours is no correction")
    if not 0 <= ans["confidence"] <= 1:
        errs.append("confidence must be 0..1")
    return errs


def run(item):
    name = f"page-{int(item['page']):03d}"
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    ours = "\n".join(f"{k}: {v}" for k, v in item["ours"].items())
    before = "".join(f"\n(For context, the verse before them, which begins on the previous page: {k}: {v})\n"
                     for k, v in item["before"].items() if v)
    prompt = (PROMPT.replace("{page}", item["page"]).replace("{images}", ", ".join(item["images"]))
              .replace("{ours}", ours).replace("{before}", before))
    extra = ""
    for attempt in (1, 2):
        last = RAW / f"{name}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last),
                "-C", str(RAW)]
        for img in item["images"]:
            args += ["-i", str(PAGES / img)]
        p = subprocess.run(args, input=prompt + extra, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=2400)
        if p.returncode or not last.exists():
            return name, f"codex exit {p.returncode}: {p.stderr.strip()[-200:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        errs = check(ans, item)
        if not errs:
            out.write_text(json.dumps({**item, **ans, "model": MODEL, "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return name, f"ok {sum(len(v['corrections']) for v in ans['verses'])} corrections, {ans['confidence']}"
        extra = "\n\nYour previous answer was refused by the checker: " + "; ".join(errs[:12]) + ". Answer again."
    return name, "rejected: " + "; ".join(errs[:6])


items = json.loads((HERE / "items.json").read_text(encoding="utf-8"))
if len(sys.argv) > 2:
    items = [i for i in items if i["page"] in sys.argv[2:]]
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, items):
        print(name, status, flush=True)
print("DONE", flush=True)
