"""PRB-0977, third pass (2026-10-08 evening): the same reading over the 116 other chapters live cites in five or more
person-to-person relationships (narrative and list chapters: NEH 11, 1CH 27, 1CH 12, ...), listed in narrative-chapters.json.
Second pass, as it was: For each chapter, every person-to-person relationship
read from it, the records of everyone named in it, and its King James and Hebrew/Greek text; gpt-6.1-sol says which
rows the chapter contradicts and which family statements it makes that no row records. Script-checked: row ids are
the chapter's, slugs are the chapter's records, verses are in the chapter, types from the vocabulary; one retry with
the errors. Two read-only queries per chapter against live. Out: chapters/<BOOK>-<c>.json."""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "chapters-narrative"; RAW = HERE / "raw-chapters-narrative"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
BOOKS = ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL "
         "AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB "
         "JAS 1PE 2PE 1JN 2JN 3JN JUD REV").split()
CHAPTERS = [("GEN", c) for c in (4, 5, 10, 11, 22, 25, 29, 30, 35, 36, 38, 46)] + [("EXO", 6), ("NUM", 3), ("NUM", 26), ("RUT", 4)] \
    + [("1CH", c) for c in (1, 2, 3, 4, 5, 6, 7, 8, 9, 23, 24, 25, 26)] \
    + [("EZR", c) for c in (2, 7, 8, 10)] + [("NEH", c) for c in (7, 10, 11, 12)] + [("MAT", 1), ("LUK", 3)]
TYPES = ["father-of", "mother-of", "son-of", "daughter-of", "brother-of", "sister-of", "half-brother-of", "half-sister-of",
         "husband-of", "wife-of", "concubine-of", "grandfather-of", "grandson-of", "descendant-of", "ancestor-of",
         "father-in-law-of", "son-in-law-of", "brother-in-law-of", "adoptive-mother-of"]

ROW = {"type": "object", "additionalProperties": False,
       "required": ["kind", "rows", "new_type", "new_to", "verse", "why", "confidence"],
       "properties": {"kind": {"type": "string", "enum": ["delete", "retype", "retarget"]},
                      "rows": {"type": "array", "items": {"type": "integer"}},
                      "new_type": {"type": "string"}, "new_to": {"type": "string"},
                      "verse": {"type": "string"}, "why": {"type": "string"}, "confidence": {"type": "number"}}}
MISSING = {"type": "object", "additionalProperties": False, "required": ["from", "type", "to", "verse", "why", "confidence"],
           "properties": {"from": {"type": "string"}, "type": {"type": "string"}, "to": {"type": "string"},
                          "verse": {"type": "string"}, "why": {"type": "string"}, "confidence": {"type": "number"}}}
SCHEMA = {"type": "object", "additionalProperties": False, "required": ["wrong", "missing", "note"],
          "properties": {"wrong": {"type": "array", "items": ROW}, "missing": {"type": "array", "items": MISSING},
                         "note": {"type": "string"}}}
(HERE / "chapters-schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """You are checking the family relationships a Bible research site has recorded from one genealogical chapter. Answer only with the JSON the schema asks for; do not run any commands or write any files.

You get the chapter's King James and original-language text verse by verse, the records of every person the site has placed in it (slug, name, distinguisher, sex), every person-to-person relationship row read from a verse of this chapter (id, "from type to", verse, who recorded it), and the rows between these same people that were read from other chapters (recorded_from_other_chapters; not yours to judge, but they count as recorded). Direction: "isaac son-of abraham".

Read the original language first, then say:
- wrong: rows this chapter contradicts. kind "delete" (the chapter does not say it; NOT because the same relation is also recorded the other way round — the site records each relation from both sides on purpose, e.g. "a father-of b" with "b son-of a", "a brother-of b" with "b brother-of a", a husband-of with its wife-of, and an ancestor-of with its descendant-of: those pairs are by design and never wrong for that reason), "retype" (new_type = the right type), "retarget" (new_to = the slug of the person the verse means, one of the records given — typically a namesake picked wrongly). Not wrong: a relation another book states differently (that is a variant, kept); "son of" skipping generations when the text itself says "son" (keep it unless the chapter itself names the generation between).
- missing: family statements the chapter makes plainly (X begat Y, the sons of X: Y, Y his son, X's wife Z, X's brother Y) between two people who are both among the records given, and that no row records (here or in recorded_from_other_chapters) in either direction or with an equivalent type (father-of = son-of reversed). Only plain statements, not inferences; give the verse.
- Also not wrong: a row that is true but stated more loosely than another (descendant-of beside son-of). Judge only whether the chapter supports what the row says, and whether it names the right person.
- A row recorded "by the project owner" is his ruling: never list it as wrong; if it looks wrong, say so in note.
- Types: {types}. Slugs exactly as given; verses as "BOOK c:v" within this chapter.
- Each item: why = one sentence from the text; confidence 0..1. Prefer missing nothing to inventing something.
- note: one or two sentences, including any owner row that looks wrong.

Chapter:
{chapter}
"""


def psql(sql):
    p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                        'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'],
                       input=sql, capture_output=True, text=True, encoding="utf-8")
    if p.returncode:
        raise RuntimeError(p.stderr)
    return json.loads(p.stdout.strip() or "null")


def gather(book, chapter):
    b = BOOKS.index(book) + 1
    text = psql(f"""
SELECT json_object_agg(t.slug||' '||vr.canonical_verse, x.line) FROM text t
JOIN verse ve ON ve.text_id = t.id
JOIN verse_reference vr ON vr.verse_id = ve.id AND vr.is_primary AND vr.canonical_book = {b} AND vr.canonical_chapter = {chapter}
CROSS JOIN LATERAL (SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line FROM word w WHERE w.verse_id = ve.id) x
WHERE t.slug IN ('KJV', '{"BHSA" if b <= 39 else "NESTLE1904"}') AND x.line IS NOT NULL;""") or {}
    data = psql(f"""
WITH r AS (SELECT r.* FROM entity_relationship r JOIN entity a ON a.id = r.from_entity_id JOIN entity c ON c.id = r.to_entity_id
           WHERE r.canonical_book = {b} AND r.canonical_chapter = {chapter} AND a.kind = 'person' AND c.kind = 'person'),
     e AS (SELECT entity_id id FROM entity_verse WHERE canonical_book = {b} AND canonical_chapter = {chapter}
           UNION SELECT from_entity_id FROM r UNION SELECT to_entity_id FROM r)
SELECT json_build_object(
 'rows', (SELECT json_agg(json_build_object('id', r.id, 'row', a.slug||' '||r.type||' '||c.slug, 'verse', r.canonical_verse,
                 'source', r.source) ORDER BY r.canonical_verse, r.id)
          FROM r JOIN entity a ON a.id = r.from_entity_id JOIN entity c ON c.id = r.to_entity_id),
 'elsewhere', (SELECT json_agg(json_build_object('row', a.slug||' '||o.type||' '||c.slug, 'book', o.canonical_book,
                 'chapter', o.canonical_chapter, 'verse', o.canonical_verse) ORDER BY o.id)
          FROM entity_relationship o JOIN entity a ON a.id = o.from_entity_id JOIN entity c ON c.id = o.to_entity_id
          WHERE o.from_entity_id IN (SELECT id FROM e) AND o.to_entity_id IN (SELECT id FROM e)
            AND o.id NOT IN (SELECT id FROM r) AND a.kind = 'person' AND c.kind = 'person'),
 'records', (SELECT json_agg(json_build_object('slug', x.slug, 'name', x.name, 'distinguisher', x.distinguisher, 'sex', x.sex) ORDER BY x.slug)
             FROM entity x WHERE x.id IN (SELECT id FROM e) AND x.kind = 'person'));""")
    verses = sorted({int(k.split()[1]) for k in text})
    return {"chapter": f"{book} {chapter}",
            "text": {f"{book} {chapter}:{v}": {t: text.get(f"{t} {v}") for t in ("KJV", "BHSA" if b <= 39 else "NESTLE1904")} for v in verses},
            "records": data["records"] or [],
            "relationships": [{**r, "verse": f"{book} {chapter}:{r['verse']}"} for r in data["rows"] or []],
            "recorded_from_other_chapters": [f"{o['row']} ({BOOKS[o['book'] - 1] if o['book'] and o['book'] <= 66 else o['book']} {o['chapter']}:{o['verse']})"
                                             for o in data["elsewhere"] or []]}


def check(item, ans):
    ids = {r["id"] for r in item["relationships"]}
    owner = {r["id"] for r in item["relationships"] if "project owner" in (r["source"] or "")}
    slugs = {r["slug"] for r in item["records"]}
    verses = set(item["text"])
    errs = []
    for w in ans["wrong"]:
        if not w["rows"] or any(i not in ids for i in w["rows"]):
            errs.append(f"wrong: rows {w['rows']} are not (all) among this chapter's relationships")
        if owner & set(w["rows"]):
            errs.append(f"wrong: rows {sorted(owner & set(w['rows']))} are the owner's own; put them in note instead")
        if w["kind"] == "retype" and w["new_type"] not in TYPES:
            errs.append(f"retype: '{w['new_type']}' is not an allowed type")
        if w["kind"] == "retarget" and w["new_to"] not in slugs:
            errs.append(f"retarget: '{w['new_to']}' is not one of the records given")
        if w["verse"] not in verses:
            errs.append(f"wrong: verse '{w['verse']}' is not in this chapter")
    for m in ans["missing"]:
        if m["from"] not in slugs or m["to"] not in slugs:
            errs.append(f"missing: '{m['from']}' or '{m['to']}' is not one of the records given")
        if m["type"] not in TYPES:
            errs.append(f"missing: '{m['type']}' is not an allowed type")
        if m["verse"] not in verses:
            errs.append(f"missing: verse '{m['verse']}' is not in this chapter")
    return errs


def run(bc):
    book, chapter = bc
    name = f"{book}-{chapter}"
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    item = gather(book, chapter)
    (RAW / f"{name}.input.json").write_text(json.dumps(item, ensure_ascii=False, indent=1), encoding="utf-8")
    prompt = PROMPT.replace("{types}", ", ".join(TYPES)).replace("{chapter}", json.dumps(item, ensure_ascii=False, indent=1))
    errs = []
    for attempt in (1, 2):
        last = RAW / f"{name}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "chapters-schema.json"),
                "-o", str(last), "-C", str(RAW)]
        p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=2700)
        if p.returncode or not last.exists():
            return name, f"codex exit {p.returncode}: {p.stderr.strip()[-300:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        errs = check(item, ans)
        if not errs:
            out.write_text(json.dumps({"chapter": item["chapter"], "rows": len(item["relationships"]), **ans, "model": MODEL,
                                       "effort": EFFORT, "attempt": attempt,
                                       "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return name, f"ok ({len(ans['wrong'])} wrong, {len(ans['missing'])} missing)" + (" after retry" if attempt == 2 else "")
        (RAW / f"{name}.rejected.json").write_text(json.dumps({"errors": errs, "answer": ans}, ensure_ascii=False, indent=1),
                                                   encoding="utf-8")
        prompt += "\n\nYour previous answer was rejected by the checker: " + "; ".join(errs) + ". Fix exactly that."
    return name, "rejected twice: " + "; ".join(errs[:3])


CHAPTERS = [tuple(x) for x in json.loads((HERE / "narrative-chapters.json").read_text(encoding="utf-8"))]
todo = CHAPTERS if len(sys.argv) == 1 else [(a.split("-")[0], int(a.split("-")[1])) for a in sys.argv[1:]]
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, todo):
        print(name, status, flush=True)
print("DONE", flush=True)
