"""The judge's worklist: every 'letters' and 'words' correction, and a sample of 60 'marks', split into two halves by
page. The judge sees the page image and the proposal, and says whether the page prints it. Out: judge/half-N.json."""
import json, random
from pathlib import Path

HERE = Path(__file__).parent
PAGES = r"C:/Users/timon/AppData/Local/Temp/claude/C--Users-timon-Projects-Essenthos/6ce57ccb-b364-495a-8563-3a5ef1bef131/scratchpad/scans/pages"
rows = json.loads((HERE / "kinds.json").read_text(encoding="utf-8"))
for i, r in enumerate(rows):
    r["id"] = i
random.seed(9)
marks = random.sample([r for r in rows if r["kind"] == "marks"], 60)
pick = sorted([r for r in rows if r["kind"] != "marks"] + marks, key=lambda r: (int(r["page"]), r["id"]))
(HERE / "judge").mkdir(exist_ok=True)
for n, (lo, hi) in enumerate(((1, 52), (53, 104)), 1):
    part = [r for r in pick if lo <= int(r["page"]) <= hi]
    for r in part:
        leaf = int(r["image"].rsplit("-", 1)[1].split(".")[0])
        r["images"] = [f"{PAGES}/{r['image']}", f"{PAGES}/IsaiahAccordingToTheSeptuagint-{leaf + 1}.jpg"]
    items = [{k: r[k] for k in ("id", "page", "verse", "ours", "printed", "images")} for r in part]
    (HERE / "judge" / f"half-{n}.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
    print(n, len(items), "items on", len({r["page"] for r in part}), "pages")
