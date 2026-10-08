"""PRB-0977: build one case per kinship contradiction from anomalies.txt, with every relationship touching the people
involved, their records, and the King James and Hebrew of each verse cited (one verse either side for context).
Two read-only queries against live. Out: cases.json."""
import json, subprocess
from pathlib import Path

HERE = Path(__file__).parent
BOOKS = ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL "
         "AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB "
         "JAS 1PE 2PE 1JN 2JN 3JN JUD REV").split()


def psql(sql):
    p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                        'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'],
                       input=sql, capture_output=True, text=True, encoding="utf-8")
    if p.returncode:
        raise SystemExit(p.stderr)
    return json.loads(p.stdout.strip() or "null")


cases = []
section = None
for line in (HERE / "anomalies.txt").read_text(encoding="utf-8").splitlines():
    if line.startswith("== "):
        section = line[3:5].strip(". ")
        continue
    cells = [c.strip() for c in line.split("|")]
    if section == "6" and len(cells) == 3 and cells[0] != "child":
        cases.append({"id": f"parents-{cells[0]}", "kind": "two parents of the same sex",
                      "question": f"{cells[0]} is recorded with more than one {'father' if cells[1] == 'male' else 'mother'}: {cells[2]}.",
                      "focus": [cells[0], *[p.strip() for p in cells[2].split(",")]]})
cases.append({"id": "sibling-and-parent-rephah", "kind": "parent and sibling at once",
              "question": "rephah is recorded both as the brother of resheph and as his father (resheph son-of rephah).",
              "focus": ["rephah", "resheph", "ephraim", "telah", "beriah"]})
cases.append({"id": "cycle-amasai", "kind": "a parent cycle",
              "question": "amasai, mahath and elkanah-4 are each recorded as an ancestor of the other: a cycle.",
              "focus": ["amasai", "mahath", "elkanah-4", "elkanah", "ahimoth"]})
cases.append({"id": "sex-hodiah", "kind": "sex contradicts the relation",
              "question": "hodiah is recorded male and is the mother-of keilah and eshtemoa-2 (1 Chronicles 4:19).",
              "focus": ["hodiah", "keilah", "eshtemoa-2", "naham"]})

slugs = sorted({s for c in cases for s in c["focus"]})
arr = "ARRAY[" + ",".join("'" + s.replace("'", "''") + "'" for s in slugs) + "]"
rows = psql(f"""
WITH f AS (SELECT id FROM entity WHERE slug = ANY({arr}))
SELECT json_build_object(
 'rels', (SELECT json_agg(json_build_object('id', r.id, 'from', a.slug, 'type', r.type, 'to', b.slug,
                 'book', r.canonical_book, 'chapter', r.canonical_chapter, 'verse', r.canonical_verse, 'source', r.source)
                 ORDER BY r.id)
          FROM entity_relationship r JOIN entity a ON a.id = r.from_entity_id JOIN entity b ON b.id = r.to_entity_id
          WHERE (r.from_entity_id IN (SELECT id FROM f) OR r.to_entity_id IN (SELECT id FROM f))
            AND a.kind = 'person' AND b.kind = 'person'),
 'ents', (SELECT json_agg(json_build_object('slug', e.slug, 'name', e.name, 'distinguisher', e.distinguisher, 'sex', e.sex,
                 'tribe', e.tribe, 'notes', left(e.notes, 400)))
          FROM entity e WHERE e.id IN (SELECT id FROM f)
             OR e.id IN (SELECT to_entity_id FROM entity_relationship WHERE from_entity_id IN (SELECT id FROM f))
             OR e.id IN (SELECT from_entity_id FROM entity_relationship WHERE to_entity_id IN (SELECT id FROM f))));
""")
rels, ents = rows["rels"] or [], {e["slug"]: e for e in rows["ents"] or []}

refs = sorted({(r["book"], r["chapter"], r["verse"] + d) for r in rels if r["book"] for d in (-1, 0, 1) if r["verse"] + d > 0})
values = ",".join(f"({b},{c},{v})" for b, c, v in refs)
verses = psql(f"""
SELECT json_object_agg(t.slug||' '||q.b||' '||q.c||':'||q.v, x.line) FROM (VALUES {values}) q(b,c,v)
CROSS JOIN (SELECT id, slug FROM text WHERE slug IN ('KJV','BHSA')) t
CROSS JOIN LATERAL (
  SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
  FROM verse_reference vr JOIN verse ve ON ve.id = vr.verse_id AND ve.text_id = t.id JOIN word w ON w.verse_id = ve.id
  WHERE vr.canonical_book = q.b AND vr.canonical_chapter = q.c AND vr.canonical_verse = q.v AND vr.is_primary) x
WHERE x.line IS NOT NULL;
""") or {}


def ref(b, c, v):
    return f"{BOOKS[b - 1] if b <= len(BOOKS) else b} {c}:{v}"


out = []
for c in cases:
    focus = set(c["focus"])
    mine = [r for r in rels if r["from"] in focus or r["to"] in focus]
    people = sorted({r["from"] for r in mine} | {r["to"] for r in mine} | focus)
    vs = {}
    for r in mine:
        if not r["book"]:
            continue
        for d in (-1, 0, 1):
            for t in ("KJV", "BHSA"):
                k = f"{t} {r['book']} {r['chapter']}:{r['verse'] + d}"
                if k in verses:
                    vs[f"{t} {ref(r['book'], r['chapter'], r['verse'] + d)}"] = verses[k]
    out.append({**c,
                "records": [ents[p] for p in people if p in ents],
                "relationships": [{"id": r["id"], "row": f"{r['from']} {r['type']} {r['to']}",
                                   "verse": ref(r["book"], r["chapter"], r["verse"]) if r["book"] else None,
                                   "source": r["source"]} for r in mine],
                "verses": dict(sorted(vs.items()))})
(HERE / "cases.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
print(len(out), "cases;", sum(len(c["relationships"]) for c in out), "rows")
