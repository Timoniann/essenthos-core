"""Collect every `notes` paragraph of our own records (any object with a slug and a notes string) with a SHA-256 of the English."""
import hashlib, json
from pathlib import Path

SRC = Path(r"C:\Users\timon\Projects\Essenthos\essenthos-core\Essenthos.Forge\Loading\Encyclopedia")
FIRST = ["ObjectRecords.json", "ObservanceRecords.json", "NarrativeRecords.json", "TitleRecords.json", "Peoples.json", "OwnRecords.json"]
FILES = FIRST + sorted(p.name for p in SRC.glob("*.json") if p.name not in FIRST and p.name != "NoteTranslations.json")
items = []


def add(file, slug, name, names, notes):
    items[:] = [i for i in items if not (i["file"] == file and i["slug"] == slug)]  # a later ruling on the record wins
    items.append({"file": file, "slug": slug, "name": name, "names": names, "english": notes,
                  "sha256": hashlib.sha256(notes.encode("utf-8")).hexdigest()})


def walk(o, file):
    if isinstance(o, dict):
        if isinstance(o.get("slug"), str) and isinstance(o.get("notes"), str) and o["notes"].strip():
            add(file, o["slug"], o.get("name"), o.get("names") if isinstance(o.get("names"), dict) else {}, o["notes"])
        says = o.get("says")
        if isinstance(o.get("existing"), str) and isinstance(says, dict) and isinstance(says.get("notes"), str) and says["notes"].strip():
            add(file, o["existing"], says.get("name") or o["existing"], {}, says["notes"])
        for v in o.values():
            walk(v, file)
    elif isinstance(o, list):
        for v in o:
            walk(v, file)


for f in FILES:
    p = SRC / f
    if p.exists():
        walk(json.loads(p.read_text(encoding="utf-8-sig")), f)
out = Path(__file__).parent / "notes-english.json"
out.write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
by = {}
for i in items:
    by.setdefault(i["file"], [0, 0]); by[i["file"]][0] += 1; by[i["file"]][1] += len(i["english"])
print(len(items), sum(len(i["english"]) for i in items), by)
