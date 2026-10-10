"""Swete's Septuagint page by page against the scan (the owner's «звірка сторінок», after Ottley's Isaiah in
pages-ottley-2026-10-09). For every printed page of First1KGreek's TEI (<pb n>, pinned to the commit the corpus is built
from), the verses that begin on it and our own SWETE / SWETEOG text of them, read from live in one read-only query.

The TEI names the scan it was transcribed from (archive.org's oldtestamentingr01swetuoft, oldtestamentingr02swetuoft,
theoldtestamenti03swetuoft), and in those three the printed page n is leaf n + 32, n + 18 and n + 24: checked on eight
verses across the volumes that were found on exactly those leaves (scans-2026-10-09). Sirach is left out: its TEI is
transcribed from Hart's Ecclesiasticus, whose pages are not Swete's.

Out: .scans/swete/items.json (11 MB of our own text, so out of git; prepare.py makes it again), one item per printed page, in the order of the books. The TEI goes to .scans/swete/tei and the page
images to .scans/swete/pages in the control folder, out of git; read.py fetches each image when it reaches its page.
Usage: prepare.py"""
import json, re, subprocess, urllib.request
from pathlib import Path

HERE = Path(__file__).parent
SCANS = HERE.parents[4] / ".scans" / "swete"
TEI = SCANS / "tei"; TEI.mkdir(parents=True, exist_ok=True)
COMMIT = "eb81494731fd632f582c4b94634127bdbd596b43"
VOLUMES = {1: ("oldtestamentingr01swetuoft", 32), 2: ("oldtestamentingr02swetuoft", 18), 3: ("theoldtestamenti03swetuoft", 24)}

# file number -> (text, canonical ordinal of the book in our corpus, as SweteTextSource.Canon and SweteOldGreekTextSource
# place it; 18, 2 Esdras, is Ezra to chapter 10 and Nehemiah after)
BOOKS = {1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 6, 8: 7, 10: 8, 11: 9, 12: 10, 13: 11, 14: 12, 15: 13, 16: 14, 17: 68, 18: 15,
         19: 17, 20: 71, 21: 70, 23: 73, 24: 74, 25: 80, 26: 81, 27: 19, 28: 83, 29: 20, 31: 22, 32: 18, 33: 75, 35: 84,
         36: 28, 37: 30, 38: 33, 39: 29, 40: 31, 41: 32, 42: 34, 43: 35, 44: 36, 45: 37, 46: 38, 47: 39, 48: 23, 49: 24,
         50: 67, 51: 25, 52: 76, 53: 26, 55: 77, 57: 27, 59: 78}
OLD_GREEK = {54: 77, 56: 27, 58: 78}
SKIPPED = {34: "Sirach: the TEI is Hart's Ecclesiasticus, not Swete's pages"}

sql = """SELECT json_agg(json_build_array(t.slug, b.canonical_ordinal, b.name, v.chapter_number, v.number, v.label, x.line))
  FROM text t JOIN book b ON b.text_id = t.id JOIN verse v ON v.book_id = b.id
  CROSS JOIN LATERAL (SELECT string_agg(w.text || coalesce(w.trailer, ''), '' ORDER BY w.position) line
    FROM word w WHERE w.verse_id = v.id) x
  WHERE t.slug IN ('SWETE', 'SWETEOG') AND x.line IS NOT NULL;"""
p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                    'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True,
                   encoding="utf-8", check=True)
ours, names = {}, {}
for slug, canonical, name, chapter, number, label, line in json.loads(p.stdout):
    ours[(slug, canonical, chapter, f"{number}{label or ''}")] = line
    names[(slug, canonical)] = name

pages = {}  # (volume, page) -> verses that begin on it
unmatched = 0
for number in sorted(set(BOOKS) | set(OLD_GREEK) | set(SKIPPED)):
    if number in SKIPPED:
        print(f"tlg{number:03d} skipped: {SKIPPED[number]}")
        continue
    f = TEI / f"tlg{number:03d}.xml"
    if not f.exists():
        urllib.request.urlretrieve(f"https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/{COMMIT}/data/tlg0527/"
                                   f"tlg{number:03d}/tlg0527.tlg{number:03d}.1st1K-grc1.xml", f)
    text = f.read_text(encoding="utf-8")
    scan = re.search(r'archive\.org/details/([^/"]+)', text)
    volume = next((v for v, (aid, _) in VOLUMES.items() if scan and aid == scan.group(1)), 3)  # Hosea names no scan; it is vol. 3
    slug, canonical = ("SWETEOG", OLD_GREEK[number]) if number in OLD_GREEK else ("SWETE", BOOKS[number])
    page, chapter, found = None, 1, 0
    for t in re.finditer(r'<pb n="([^"]+)"\s*/>|<div type="textpart" subtype="(chapter|verse)" n="([^"]+)"', text):
        if t.group(1):
            page = t.group(1)
        elif t.group(2) == "chapter":
            chapter = int(t.group(3)) if t.group(3).isdigit() else chapter
        elif page and page.isdigit():
            book, ch = canonical, chapter
            if number == 18 and chapter > 10:
                book, ch = 16, chapter - 10
            line = ours.get((slug, book, ch, t.group(3)))
            if line is None:
                unmatched += 1
                continue
            name = names[(slug, book)] + (" (Old Greek)" if slug == "SWETEOG" else " (Theodotion)" if number in (55, 57, 59) else "")
            pages.setdefault((volume, int(page)), []).append(
                {"verse": f"{name} {chapter}:{t.group(3)}", "text": slug, "book": book, "chapter": ch, "number": t.group(3),
                 "ours": line})
            found += 1
    print(f"tlg{number:03d} vol {volume} {slug} {found} verses", flush=True)

items, previous = [], {}
for (volume, page), verses in pages.items():
    aid, offset = VOLUMES[volume]
    leaf = page + offset
    items.append({"volume": volume, "page": page, "scan": aid, "leaf": leaf,
                  "images": [f"{aid}-{leaf}.jpg", f"{aid}-{leaf + 1}.jpg"], "verses": verses})
(SCANS / "items.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "pages;", sum(len(i["verses"]) for i in items), "verses;", unmatched, "TEI verses with no verse of ours")
