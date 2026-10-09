"""Proofreading Ottley's Isaiah page by page against the scan (the owner's «звірка сторінок», after PRB-0712's 16 verses).
For each printed page of the First1KGreek TEI (<pb n>), the verses that begin on it, the scan's leaf for that page and
its two neighbours (the page map is OCR and off by one in places), and our own OTTLEY text of those verses from live
(one read-only query). Out: items.json; page images go to <scratch>/pages/ and stay out of git.
Usage: prepare.py <scratch folder>"""
import json, re, subprocess, sys, urllib.request
from pathlib import Path

HERE = Path(__file__).parent
SCRATCH = Path(sys.argv[1])
PAGES = SCRATCH / "pages"; PAGES.mkdir(exist_ok=True)
TEI = HERE.parents[2] / "Swete" / "First1KGreek" / "isaiah-ottley-1904.xml"
AID = "IsaiahAccordingToTheSeptuagint"
# Ottley's two volumes share one scan; the Greek text is volume 2, from leaf 382 on
AFTER = 381

text = TEI.read_text(encoding="utf-8")
page, ch, by_page = None, None, {}
for t in re.finditer(r'<pb n="([^"]+)"\s*/>|<div type="textpart" subtype="(chapter|verse)" n="([^"]+)"', text):
    if t.group(1):
        page = t.group(1)
    elif t.group(2) == "chapter":
        ch = int(t.group(3))
    else:
        by_page.setdefault(page, []).append((ch, int(t.group(3))))


def leaf_of(printed):
    # the OCR map numbers leaf 382 as page 3 and runs one leaf behind from there; PRB-0712's 16 verses were all found one
    # leaf after the map's (page 7 on 389, 53 on 435, 84 on 466), so the printed page n is leaf n + 382
    return int(printed) + 382


sql = """SELECT json_object_agg(vr.canonical_chapter||':'||vr.canonical_verse, x.line) FROM text t
  JOIN verse ve ON ve.text_id = t.id
  JOIN verse_reference vr ON vr.verse_id = ve.id AND vr.is_primary AND vr.canonical_book = 23
  CROSS JOIN LATERAL (SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
    FROM word w WHERE w.verse_id = ve.id) x
  WHERE t.slug = 'OTTLEY' AND x.line IS NOT NULL;"""
p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                    'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True, encoding="utf-8")
ours = json.loads(p.stdout.strip() or "{}") or {}

items, prev_last = [], None
for printed, verses in by_page.items():
    leaf = leaf_of(printed)
    near = [l for l in ((leaf - 1, leaf, leaf + 1) if leaf else ()) if l > AFTER]
    files = []
    for l in near:
        f = PAGES / f"{AID}-{l}.jpg"
        if not f.exists():
            urllib.request.urlretrieve(f"https://archive.org/download/{AID}/page/n{l - 1}_w1800.jpg", f)
        files.append(f.name)
    keys = [f"{c}:{v}" for c, v in verses]
    items.append({"page": printed, "leaf": leaf, "images": files, "verses": keys,
                  "before": {prev_last: ours.get(prev_last)} if prev_last else {},
                  "ours": {k: ours.get(k) for k in keys}})
    prev_last = keys[-1]
    print("page", printed, "leaf", leaf, len(keys), "verses", keys[0], "-", keys[-1], flush=True)

(HERE / "items.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "pages;", sum(len(i["verses"]) for i in items), "verses;", len({f for i in items for f in i["images"]}), "images")
