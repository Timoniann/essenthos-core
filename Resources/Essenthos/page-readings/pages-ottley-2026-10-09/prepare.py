"""Proofreading Ottley's Isaiah page by page against the scan (the owner's «звірка сторінок», after PRB-0712's 16 verses).
For each printed page of the First1KGreek TEI (<pb n>), the verses that begin on it, the scan's leaf for that page and
its two neighbours (the page map is OCR and off by one in places), and our own OTTLEY text of those verses from live
(one read-only query). Out: items.json; page images go to the scans folder and stay out of git.

The TEI has no verse of its own for a verse the converter divides out of the one before it (EditionRepair.Divide in
OttleyIsaiah.cs): such a verse is set on the page its first words stand on in the verse it is divided from.

--divided asks nothing of live: only the divided verses, with the verse before each for context, read from the
converter's tokens (transcribed.txt) as the page table (OttleyPage.json) leaves them. Out: items-divided.json, which
run.py --divided reads into out-divided/.

Usage: prepare.py [--divided] [scans folder, default .scans/ottley in the control folder]
The TEI is read from Dataset__ResourcesPath when it is set (a worktree has no corpus)."""
import json, os, re, subprocess, sys, unicodedata, urllib.request
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).parent
DIVIDED = "--divided" in sys.argv
ARGS = [a for a in sys.argv[1:] if a != "--divided"]
PAGES = Path(ARGS[0]) if ARGS else next((p / ".scans" / "ottley" for p in HERE.parents if (p / ".scans" / "ottley").is_dir()), HERE.parents[4] / ".scans" / "ottley")
PAGES.mkdir(parents=True, exist_ok=True)
RESOURCES = Path(os.environ.get("Dataset__ResourcesPath") or HERE.parents[2])
TEI = RESOURCES / "Swete" / "First1KGreek" / "isaiah-ottley-1904.xml"
CONVERTER = HERE.parents[3] / "Essenthos.Forge" / "Swete" / "OttleyIsaiah.cs"
TABLE = HERE.parents[3] / "Essenthos.Forge" / "Swete" / "OttleyPage.json"
AID = "IsaiahAccordingToTheSeptuagint"
# Ottley's two volumes share one scan; the Greek text is volume 2, from leaf 382 on
AFTER = 381


def flat(s):
    return " ".join(unicodedata.normalize("NFC", s).split())


def divisions():
    """The converter's divisions in its order: (chapter, verse divided from, the words it opens with, its verse)."""
    source = CONVERTER.read_text(encoding="utf-8")
    found = [(int(m.group(1)), m.group(2), m.group(3), m.group(4))
             for m in re.finditer(r'EditionRepair\.Divide\((\d+), "([^"]+)", "([^"]+)", "[^"]*", "([^"]+)"', source)]
    if not found:
        sys.exit(f"no EditionRepair.Divide found in {CONVERTER}")
    return found


text = TEI.read_text(encoding="utf-8")
page, ch, by_page, segments = None, None, {}, {}
marks = list(re.finditer(r'<pb n="([^"]+)"\s*/>|<div type="textpart" subtype="(chapter|verse)" n="([^"]+)"', text))
for n, t in enumerate(marks):
    if t.group(1):
        page = t.group(1)
    elif t.group(2) == "chapter":
        ch = int(t.group(3))
    else:
        key = (ch, int(t.group(3)))
        by_page.setdefault(page, []).append(key)
        end = next((m.start() for m in marks[n + 1:] if m.group(2)), len(text))
        segments[key] = (page, text[t.end():end])

# A divided verse is placed where its opening words stand in the TEI verse that holds it: on that verse's page, unless
# a page break comes before them. Divisions out of one verse are found in order, each after the one before it.
holder, searched_from, placed = {}, {}, []
for chapter, verse, words, to in divisions():
    source = (chapter, int(verse))
    tei = holder.get(source, source)
    if tei not in segments:
        sys.exit(f"{chapter}:{verse} is divided, and the TEI has no verse {tei[0]}:{tei[1]} holding it")
    start_page, segment = segments[tei]
    pieces = re.split(r'<pb n="([^"]+)"\s*/>', re.sub(r"<note\b.*?</note>", " ", segment, flags=re.S))
    running, offsets = "", []
    for k in range(0, len(pieces), 2):
        offsets.append((len(running), start_page if k == 0 else pieces[k - 1]))
        running += flat(re.sub(r"<[^>]+>", " ", pieces[k])) + " "
    at = running.find(flat(words), searched_from.get(tei, 0))
    if at < 0:
        sys.exit(f"{chapter}:{to}: \"{words}\" is not in the TEI's {tei[0]}:{tei[1]}")
    searched_from[tei] = at + 1
    on = [p for o, p in offsets if o <= at][-1]
    target = (chapter, int(to))
    holder[target] = tei
    keys = by_page.setdefault(on, [])
    after = next((i + 1 for i, k in enumerate(keys) if k == source), 0)
    keys.insert(after, target)
    placed.append(target)
    print(f"{chapter}:{to} divided from {chapter}:{verse}, on page {on}", flush=True)


def leaf_of(printed):
    # the OCR map numbers leaf 382 as page 3 and runs one leaf behind from there; PRB-0712's 16 verses were all found one
    # leaf after the map's (page 7 on 389, 53 on 435, 84 on 466), so the printed page n is leaf n + 382
    return int(printed) + 382


def converted():
    """Our text of every verse as the converter writes it: its tokens, with the page table's entries made in order."""
    verses = {}
    for line in (HERE / "transcribed.txt").read_text(encoding="utf-8").splitlines():
        ref, token = line.split(" ", 1)
        _, c, v = ref.split(".")
        verses.setdefault(f"{c}:{v}", []).append(token)
    for e in json.loads(TABLE.read_text(encoding="utf-8")) if TABLE.exists() else []:
        tokens, digitised = verses[f"{e['chapter']}:{e['verse']}"], e["digitised"].split()
        at = [i for i in range(len(tokens) - len(digitised) + 1) if tokens[i:i + len(digitised)] == digitised]
        if len(at) != 1:
            sys.exit(f"{e['chapter']}:{e['verse']} {e['source']}: \"{e['digitised']}\" stands {len(at)} times")
        tokens[at[0]:at[0] + len(digitised)] = e["printed"].split()
    return {k: " ".join(v) for k, v in verses.items()}


if DIVIDED:
    ours = converted()
else:
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
for printed, verses in sorted(by_page.items(), key=lambda kv: int(kv[0])):
    keys = [f"{c}:{v}" for c, v in verses]
    asked = [f"{c}:{v}" for c, v in verses if (c, v) in placed] if DIVIDED else keys
    if asked:
        leaf = leaf_of(printed)
        files = []
        for l in (leaf - 1, leaf, leaf + 1):
            if l <= AFTER:
                continue
            f = PAGES / f"{AID}-{l}.jpg"
            if not f.exists():
                urllib.request.urlretrieve(f"https://archive.org/download/{AID}/page/n{l - 1}_w1800.jpg", f)
            files.append(f.name)
        before = keys[keys.index(asked[0]) - 1] if keys.index(asked[0]) else prev_last
        items.append({"page": printed, "leaf": leaf, "images": files, "verses": asked,
                      "before": {before: ours.get(before)} if before else {},
                      "ours": {k: ours.get(k) for k in asked}})
        print("page", printed, "leaf", leaf, len(asked), "verses", asked[0], "-", asked[-1], flush=True)
    prev_last = keys[-1] if keys else prev_last

name = "items-divided.json" if DIVIDED else "items.json"
(HERE / name).write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "pages;", sum(len(i["verses"]) for i in items), "verses;", len({f for i in items for f in i["images"]}), "images")
