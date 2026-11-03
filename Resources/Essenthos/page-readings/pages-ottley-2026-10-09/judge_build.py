"""The judge's worklist: every 'letters' and 'words' correction, and a sample of 60 'marks', split into two halves by
page. The judge sees the page image and the proposal, and says whether the page prints it. Out: judge/half-N.json.
--divided: every correction of the divided verses' reading (classify.py marks them), accents included, which no
other reading sees. Out: judge/divided.json, judged into judge/verdict-divided.json."""
import json, random, sys
from pathlib import Path

HERE = Path(__file__).parent
PAGES = r"C:/Users/timon/AppData/Local/Temp/claude/C--Users-timon-Projects-Essenthos/6ce57ccb-b364-495a-8563-3a5ef1bef131/scratchpad/scans/pages"
SCANS = next((p / ".scans" / "ottley" for p in HERE.parents if (p / ".scans" / "ottley").is_dir()), HERE.parents[4] / ".scans" / "ottley")
rows = json.loads((HERE / "kinds.json").read_text(encoding="utf-8"))
for i, r in enumerate(rows):
    r["id"] = i
(HERE / "judge").mkdir(exist_ok=True)
if "--divided" in sys.argv:
    part = [r for r in rows if r.get("divided")]
    for r in part:
        leaf = int(r["image"].rsplit("-", 1)[1].split(".")[0])
        r["images"] = [(SCANS / r["image"]).as_posix(), (SCANS / f"IsaiahAccordingToTheSeptuagint-{leaf + 1}.jpg").as_posix()]
    items = [{k: r[k] for k in ("id", "page", "verse", "ours", "printed", "images")} for r in part]
    (HERE / "judge" / "divided.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
    print(len(items), "items on", len({r["page"] for r in part}), "pages")
    sys.exit(0)
rows = [r for r in rows if not r.get("divided")]
random.seed(9)
marks = random.sample([r for r in rows if r["kind"] == "marks"], 60)
pick = sorted([r for r in rows if r["kind"] != "marks"] + marks, key=lambda r: (int(r["page"]), r["id"]))
for n, (lo, hi) in enumerate(((1, 52), (53, 104)), 1):
    part = [r for r in pick if lo <= int(r["page"]) <= hi]
    for r in part:
        leaf = int(r["image"].rsplit("-", 1)[1].split(".")[0])
        r["images"] = [f"{PAGES}/{r['image']}", f"{PAGES}/IsaiahAccordingToTheSeptuagint-{leaf + 1}.jpg"]
    items = [{k: r[k] for k in ("id", "page", "verse", "ours", "printed", "images")} for r in part]
    (HERE / "judge" / f"half-{n}.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
    print(n, len(items), "items on", len({r["page"] for r in part}), "pages")
