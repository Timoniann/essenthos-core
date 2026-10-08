"""PRB-0973 and PRB-0712: the printed page settles what a transcription lost. For each verse the tickets name, find the
printed page from the First1KGreek TEI's <pb n> (Swete: fetched to a scratch folder, Ottley: Resources/Swete), the
scan's leaf from archive.org's page-number map, and fetch that page's image; read our own text of the verse and its
neighbours from live (one read-only query). Out: items.json and pages/<id>-<leaf>.jpg (pages/ stays out of git).
Usage: prepare.py <scratch folder holding tei/ and meta/>"""
import json, re, subprocess, sys, urllib.request
from pathlib import Path

HERE = Path(__file__).parent
SCRATCH = Path(sys.argv[1])
PAGES = SCRATCH / "pages"; PAGES.mkdir(exist_ok=True)
OTTLEY_TEI = HERE.parents[2] / "Swete" / "First1KGreek" / "isaiah-ottley-1904.xml"
BOOKS = ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL "
         "AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB "
         "JAS 1PE 2PE 1JN 2JN 3JN JUD REV").split()
NUM = {b: n for n, b in enumerate(BOOKS, 1)} | {"WIS": 75}

# (ticket, text, book, chapter, verse, TEI, archive id, what is known to be wrong)
ITEMS = [
    ("PRB-0973", "SWETE", "GEN", 11, 4, "tlg001", "oldtestamentingr01swetuoft", "a stray Latin token 'L' in the verse"),
    ("PRB-0973", "SWETE", "DEU", 21, 1, "tlg005", "oldtestamentingr01swetuoft", "a stray Latin token 'X' in the verse"),
    ("PRB-0973", "SWETE", "1SA", 25, 28, "tlg011", "oldtestamentingr01swetuoft", "a stray Latin token 'V' in the verse"),
    ("PRB-0973", "SWETE", "PRO", 22, 17, "tlg029", "oldtestamentingr02swetuoft", "a stray Latin token 'C' in the verse"),
    ("PRB-0973", "SWETE", "WIS", 12, 10, "tlg033", "oldtestamentingr02swetuoft", "a stray Latin token 'C' in the verse"),
    ("PRB-0973", "SWETE", "WIS", 14, 19, "tlg033", "oldtestamentingr02swetuoft", "a stray Latin token 'C' in the verse"),
    ("PRB-0973", "SWETE", "MAL", 2, 12, "tlg047", "theoldtestamenti03swetuoft", "a token 'IΙαντοκράτωρ' with a Latin I fused to the word"),
    ("PRB-0973", "SWETE", "MAL", 3, 17, "tlg047", "theoldtestamenti03swetuoft", "a token 'IΙαντοκράτωρ' with a Latin I fused to the word"),
    ("PRB-0973", "SWETE", "EZK", 34, 12, "tlg053", "theoldtestamenti03swetuoft", "a token 'Xώσπερ' with a Latin X fused to the word"),
    ("PRB-0712", "OTTLEY", "ISA", 5, 5, None, "IsaiahAccordingToTheSeptuagint", "the verse's last word was an undecoded character reference and is missing"),
    ("PRB-0712", "OTTLEY", "ISA", 34, 11, None, "IsaiahAccordingToTheSeptuagint", "a word was the placeholder ABBREV and is missing"),
    ("PRB-0712", "OTTLEY", "ISA", 35, 3, None, "IsaiahAccordingToTheSeptuagint", "'γόνατα παραλεφοβεῖσθε' fuses παραλελυμένα with the start of 35:4"),
    ("PRB-0712", "OTTLEY", "ISA", 35, 4, None, "IsaiahAccordingToTheSeptuagint", "begins mid-sentence and ends σώσει without ἡμᾶς"),
    ("PRB-0712", "OTTLEY", "ISA", 2, 19, None, "IsaiahAccordingToTheSeptuagint", "2:19-21 interleaved in the transcription (2:20-22 have no verse of their own there), letter misreadings such as ἅνθριιηνος for ἄνθρωπος"),
    ("PRB-0712", "OTTLEY", "ISA", 53, 1, None, "IsaiahAccordingToTheSeptuagint", "βραχίων was lost (repaired from the apparatus; confirm) and ὃ for ὁ"),
    ("PRB-0712", "OTTLEY", "ISA", 35, 9, None, "IsaiahAccordingToTheSeptuagint", "πορεύευρον σονται was repaired to πορεύσονται; confirm"),
]


def pages_of(tei_text, chapter, verse):
    """The printed pages a verse stands on: the last <pb> before its div, and any <pb> inside it."""
    tokens = re.finditer(r'<pb n="([^"]+)"\s*/>|<div type="textpart" subtype="(chapter|verse)" n="([^"]+)"', tei_text)
    page, ch, inside, out = None, None, False, []
    for t in tokens:
        if t.group(1):
            page = t.group(1)
            if inside:
                out.append(page)
        elif t.group(2) == "chapter":
            if inside:
                break
            ch = t.group(3)
        else:
            if inside:
                break
            if ch == str(chapter) and t.group(3) == str(verse):
                inside = True
                out.append(page)
    return out


def leaf_of(archive_id, printed, after=0):
    pages = json.loads((SCRATCH / "meta" / f"{archive_id}.json").read_text(encoding="utf-8"))["pages"]
    hits = [p["leafNum"] for p in pages if p["pageNumber"] == printed and p["leafNum"] > after]
    return hits[0] if hits else None


items = []
for ticket, text, book, ch, v, tei, aid, known in ITEMS:
    src = (SCRATCH / "tei" / f"{tei}.xml") if tei else OTTLEY_TEI
    printed = pages_of(src.read_text(encoding="utf-8"), ch, v)
    # Ottley's two volumes share one scan; the Greek text is volume 2, from leaf 382 on (the OCR numbers the introduction's
    # roman pages in arabic just before it)
    leaves = [leaf_of(aid, p, after=381 if aid.startswith("Isaiah") else 0) for p in printed]
    # the scans' page-number maps are OCR and are off by one in places (Ottley's printed 6 is mapped as 7), so the
    # leaf before and after are fetched too and the reader finds the verse among them
    near = sorted({l + d for l in leaves if l is not None for d in (-1, 0, 1)})
    files = []
    for leaf in near:
        f = PAGES / f"{aid}-{leaf}.jpg"
        if not f.exists():
            url = f"https://archive.org/download/{aid}/page/n{leaf - 1}_w1800.jpg"
            urllib.request.urlretrieve(url, f)
        files.append(f.name)
    items.append({"ticket": ticket, "text": text, "verse": f"{book} {ch}:{v}", "code": book, "book": NUM[book],
                  "chapter": ch, "v": v, "printed_pages": printed, "leaves": leaves, "images": files, "known": known})
    print(ticket, book, ch, v, "pages", printed, "leaves", leaves)

refs = ",".join(f"('{i['text']}',{i['book']},{i['chapter']},{i['v'] + d})" for i in items for d in (-1, 0, 1) if i["v"] + d > 0)
sql = f"""SELECT json_object_agg(q.t||' '||q.b||' '||q.c||':'||q.v, x.line) FROM (VALUES {refs}) q(t,b,c,v)
  JOIN text t ON t.slug = q.t
  CROSS JOIN LATERAL (SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
    FROM verse_reference vr JOIN verse ve ON ve.id = vr.verse_id AND ve.text_id = t.id JOIN word w ON w.verse_id = ve.id
    WHERE vr.canonical_book = q.b AND vr.canonical_chapter = q.c AND vr.canonical_verse = q.v AND vr.is_primary) x
  WHERE x.line IS NOT NULL;"""
p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                    'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True, encoding="utf-8")
ours = json.loads(p.stdout.strip() or "{}") or {}
for i in items:
    i["ours"] = {f"{i['text']} {i['code']} {i['chapter']}:{i['v'] + d}": ours.get(f"{i['text']} {i['book']} {i['chapter']}:{i['v'] + d}")
                 for d in (-1, 0, 1)}
(HERE / "items.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(items), "items;", sum(len(i["images"]) for i in items), "images")
