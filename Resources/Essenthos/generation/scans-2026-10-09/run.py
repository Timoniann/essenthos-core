"""PRB-0973 and PRB-0712: gpt-6.1-sol reads the printed page (the scan's leaf and its two neighbours) and says, word for
word, what the edition prints for the verse, and what our text gets wrong. Script-checked; one retry with the errors;
three in parallel. Out: out/<verse>.json. Usage: run.py <scratch folder holding pages/> [verse ...]"""
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
SCHEMA = {"type": "object", "additionalProperties": False,
          "required": ["image", "printed", "corrections", "note", "confidence"],
          "properties": {
              "image": {"type": "string"},
              "printed": {"type": "string"},
              "corrections": {"type": "array", "items": {
                  "type": "object", "additionalProperties": False, "required": ["ours", "printed", "why"],
                  "properties": {"ours": {"type": "string"}, "printed": {"type": "string"}, "why": {"type": "string"}}}},
              "note": {"type": "string"},
              "confidence": {"type": "number"}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are proofreading a digital transcription of a printed Greek Septuagint edition against scans of the printed pages. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Edition: {edition}. Verse: {verse}. The attached images are the scan's page for this verse and the pages before and after it (the page map can be off by one); the images are named, in order: {images}.
What is known to be wrong in our transcription of this verse: {known}.

Our text of the verse and its neighbours (from the transcription):
{ours}

Do this:
1. Find the verse on the pages (the verse numbers are in the margin or as small superscripts in the text). image = the file name of the image where the verse begins.
2. printed = the verse exactly as the edition prints it in the main text (not the apparatus at the foot of the page): every word, with its accents and breathings and punctuation, in Unicode Greek. If the verse runs onto the next page, continue it from there.
3. corrections = every place where our text of THIS verse differs from what is printed: ours = our word(s) as given above, printed = the printed word(s), why = what went wrong (a stray letter or token, a lost word, a misread letter, words fused or interleaved, a footnote's letters read into the text). A sigil or marginal mark that is not part of the Greek text (a manuscript siglum such as § D, a page signature, a line number) must not be in our text: if ours has one, the correction is to remove it (printed = "").
   Do not report differences of letter case, of a final sigma, or of spacing alone, unless the word itself changes.
4. note = anything a reader should know (e.g. the apparatus at the foot of the page records a manuscript reading here). confidence 0..1 that printed is exactly right.
"""


def check(ans, item):
    errs = []
    if ans["image"] not in item["images"]:
        errs.append(f"image must be one of {item['images']}")
    if not any("Ͱ" <= ch <= "Ͽ" or "ἀ" <= ch <= "῿" for ch in ans["printed"]):
        errs.append("printed must be the Greek text of the verse")
    if any("A" <= ch <= "Z" for ch in ans["printed"]):
        errs.append("printed contains a Latin capital letter; give the Greek text only")
    if not 0 <= ans["confidence"] <= 1:
        errs.append("confidence must be 0..1")
    return errs


def run(item):
    name = item["verse"].replace(" ", "-").replace(":", "-") + "-" + item["text"]
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    edition = {"SWETE": "H. B. Swete, The Old Testament in Greek according to the Septuagint (Cambridge), the text of Codex Vaticanus",
               "OTTLEY": "R. R. Ottley, The Book of Isaiah according to the Septuagint (Codex Alexandrinus), vol. II, 1904"}[item["text"]]
    ours = "\n".join(f"{k}: {v}" for k, v in item["ours"].items() if v)
    prompt = (PROMPT.replace("{edition}", edition).replace("{verse}", item["verse"]).replace("{images}", ", ".join(item["images"]))
              .replace("{known}", item["known"]).replace("{ours}", ours))
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
            return name, f"ok {len(ans['corrections'])} corrections, {ans['confidence']}"
        extra = "\n\nYour previous answer was refused by the checker: " + "; ".join(errs) + ". Answer again."
    return name, "rejected: " + "; ".join(errs)


items = json.loads((HERE / "items.json").read_text(encoding="utf-8"))
if len(sys.argv) > 2:
    items = [i for i in items if i["verse"] in sys.argv[2:]]
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, items):
        print(name, status, flush=True)
print("DONE", flush=True)
