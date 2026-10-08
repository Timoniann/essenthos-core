import json
from pathlib import Path

HERE = Path(__file__).parent
items = []
for f in sorted((HERE / "out").glob("*.json")):
    a = json.loads(f.read_text(encoding="utf-8"))
    items.append({"id": len(items) + 1, "key": f.stem, **a["case"]})
half = (len(items) + 1) // 2
(HERE / "judge-items-1.json").write_text(json.dumps(items[:half], ensure_ascii=False, indent=1), encoding="utf-8")
(HERE / "judge-items-2.json").write_text(json.dumps(items[half:], ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), half)
