"""Second reading of the accent and breathing corrections the Opus judge did not see (judge/half-*.json took every letter
and word correction and 60 of these). The Opus judge found that the reader's few wrong 'marks' were the page's own
misprints silently normalised (Κύριου, ἀληθεία, an unaccented οὐδε), so this reading is asked only whether the page
prints the proposed form exactly, misprint or not. gpt-6.1-sol, a page at a time, script-checked, three in parallel.
Out: verify/page-NNN.json. Usage: verify.py <scratch folder holding pages/>"""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).parent
PAGES = Path(sys.argv[1]) / "pages"
OUT = HERE / "verify"; RAW = HERE / "raw"
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


def run(page, items):
    name = f"page-{int(page):03d}"
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
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
            return name, f"codex exit {p.returncode}: {p.stderr.strip()[-200:]}"
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
print(sum(len(v) for v in by_page.values()), "items on", len(by_page), "pages", flush=True)
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(lambda kv: run(*kv), by_page.items()):
        print(name, status, flush=True)
print("DONE", flush=True)
