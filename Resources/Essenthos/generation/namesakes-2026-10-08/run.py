"""PRB-0490: in each verse where the dataset (BibleData) and our person split put two different men of one name, which
man does the verse name? gpt-6.1-sol reads the verse (KJV + Hebrew/Greek, one verse either side) with both records
(name, line, notes, family ties). Script-checked; one retry. Two read-only queries against live.
Out: out/<book>-<c>-<v>-<label>.json."""
import json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "out"; RAW = HERE / "raw"
OUT.mkdir(exist_ok=True); RAW.mkdir(exist_ok=True)
CODEX = str(max(Path.home().joinpath("AppData/Local/OpenAI/Codex/bin").glob("*/codex.exe"), key=os.path.getmtime))
MODEL, EFFORT = "gpt-6.1-sol", "high"
BOOKS = ("GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST JOB PSA PRO ECC SNG ISA JER LAM EZK DAN HOS JOL "
         "AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB "
         "JAS 1PE 2PE 1JN 2JN 3JN JUD REV").split()
SCHEMA = {"type": "object", "additionalProperties": False, "required": ["names", "why", "confidence"],
          "properties": {"names": {"type": "string", "enum": ["A", "B", "both", "neither"]},
                         "why": {"type": "string"}, "confidence": {"type": "number"}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """A Bible research site holds two different people of the same name, A and B, and two sources disagree about which of them this verse names. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Read the verse (original language first, with the verse before and after) and the two records, and say who the name in the verse is:
- "A" or "B": the verse names that person (his father, his time, his office, his place in the list, the events around him show it);
- "both": the verse names both of them (the name stands for two different people in it);
- "neither": the verse names a third person of that name, or the word is not a person here.
Decide from the text and the records' stated facts, not from which record looks more prominent. why = one or two sentences citing what decides it; confidence 0..1, low when the verse alone does not decide.

{case}
"""


def psql(sql):
    p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                        'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True, encoding="utf-8")
    if p.returncode:
        raise RuntimeError(p.stderr)
    return json.loads(p.stdout.strip() or "null")


def gather(rows):
    slugs = sorted({r["dataset_man"] for r in rows} | {r["split_man"] for r in rows})
    arr = "ARRAY[" + ",".join("'" + s.replace("'", "''") + "'" for s in slugs) + "]"
    recs = psql(f"""SELECT json_object_agg(e.slug, json_build_object('name', e.name, 'line', e.distinguisher, 'sex', e.sex, 'tribe', e.tribe,
      'notes', left(e.notes, 400),
      'family', (SELECT json_agg(r.type||' '||t.slug||' ('||t.name||coalesce(', '||left(t.distinguisher,60),'')||')') FROM entity_relationship r JOIN entity t ON t.id=r.to_entity_id WHERE r.from_entity_id=e.id AND t.kind='person'),
      'verses', (SELECT count(*) FROM entity_verse v WHERE v.entity_id=e.id)))
      FROM entity e WHERE e.slug = ANY({arr});""")
    refs = sorted({(r["b"], r["c"], r["v"] + d) for r in rows for d in (-1, 0, 1) if r["v"] + d > 0})
    values = ",".join(f"({b},{c},{v})" for b, c, v in refs)
    text = psql(f"""SELECT json_object_agg(t.slug||' '||q.b||' '||q.c||':'||q.v, x.line) FROM (VALUES {values}) q(b,c,v)
      CROSS JOIN (SELECT id, slug FROM text WHERE slug IN ('KJV','BHSA','NESTLE1904')) t
      CROSS JOIN LATERAL (SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
        FROM verse_reference vr JOIN verse ve ON ve.id = vr.verse_id AND ve.text_id = t.id JOIN word w ON w.verse_id = ve.id
        WHERE vr.canonical_book = q.b AND vr.canonical_chapter = q.c AND vr.canonical_verse = q.v AND vr.is_primary) x
      WHERE x.line IS NOT NULL;""") or {}
    return recs, text


def run(job):
    row, recs, text = job
    name = f"{BOOKS[row['b'] - 1]}-{row['c']}-{row['v']}-{row['label']}".replace(" ", "_")
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    vs = {}
    for d in (-1, 0, 1):
        for t in ("BHSA", "NESTLE1904", "KJV"):
            k = f"{t} {row['b']} {row['c']}:{row['v'] + d}"
            if k in text:
                vs[f"{t} {BOOKS[row['b'] - 1]} {row['c']}:{row['v'] + d}"] = text[k]
    case = {"verse": f"{BOOKS[row['b'] - 1]} {row['c']}:{row['v']}", "name": row["label"], "text": vs,
            "A": recs[row["dataset_man"]], "B": recs[row["split_man"]]}
    prompt = PROMPT.replace("{case}", json.dumps(case, ensure_ascii=False, indent=1))
    for attempt in (1, 2):
        last = RAW / f"{name}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last), "-C", str(RAW)]
        p = subprocess.run(args, input=prompt, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
        if p.returncode or not last.exists():
            return name, f"codex exit {p.returncode}: {p.stderr.strip()[-200:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        if ans["names"] in ("A", "B", "both", "neither") and 0 <= ans["confidence"] <= 1:
            out.write_text(json.dumps({**row, "case": case, **ans,
                                       "names_slug": {"A": row["dataset_man"], "B": row["split_man"]}.get(ans["names"], ans["names"]),
                                       "model": MODEL, "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return name, f"ok {ans['names']} {ans['confidence']}"
    return name, "rejected"


rows = json.loads((HERE / "rows.json").read_text(encoding="utf-8"))
if len(sys.argv) > 1:
    rows = rows[:int(sys.argv[1])]
recs, text = gather(rows)
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, [(r, recs, text) for r in rows]):
        print(name, status, flush=True)
print("DONE", flush=True)
