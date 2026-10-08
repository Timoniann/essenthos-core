"""PRB-0490, the fold pass: the blind judge of the namesakes reading found 50 pairs of person records whose verses
name, by the judge's reading, one man held twice (../namesakes-2026-10-08/same.json). For each pair gpt-6.1-sol reads
both records (name, line, notes, family both ways, every verse each is named in, KJV, with the Hebrew/Greek of the
verses the two disagreed on) and says whether they are one person or two. Script-checked; one retry with the errors.
Two read-only queries against live. Out: out/<a>--<b>.json."""
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
MAX_VERSES = 40
SCHEMA = {"type": "object", "additionalProperties": False,
          "required": ["verdict", "keep", "why", "confidence", "verses_of_other"],
          "properties": {"verdict": {"type": "string", "enum": ["one", "two", "unclear"]},
                         "keep": {"type": "string", "enum": ["A", "B", "none"]},
                         "why": {"type": "string"}, "confidence": {"type": "number"},
                         "verses_of_other": {"type": "array", "items": {"type": "string"}}}}
(HERE / "schema.json").write_text(json.dumps(SCHEMA), encoding="utf-8")

PROMPT = """A Bible research site holds two person records of the same name, A and B. A reader of some verses decided they name one man held twice. Decide whether A and B are one person or two. Answer only with the JSON the schema asks for; do not run any commands or write any files.

Read both records (their line, notes, family ties both ways) and every verse each is named in (KJV; the Hebrew/Greek is given for the verses the two records were disputed on):
- verdict "one": everything said of A and of B fits one person (same father or line, same time, same office or place in the same list, or one record is only a thinner copy of the other). keep = the record to keep: the one whose family ties and verses are fuller and right; the other is folded into it. verses_of_other = [].
- verdict "two": the text distinguishes them (different fathers, generations far apart, different tribes or offices, both named in one list as two people). keep = "none". verses_of_other = references ("1CH 6:10") of verses now on one record that name the other person, if any; else [].
- verdict "unclear": the text does not decide. keep = "none".
Scripture often repeats a name across generations and lists; a shared name and a shared father's name alone are not proof of one person when the times differ. why = two to four sentences citing the verses that decide it; confidence 0..1.

{case}
"""


def psql(sql):
    p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                        'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True, encoding="utf-8")
    if p.returncode:
        raise RuntimeError(p.stderr)
    return json.loads(p.stdout.strip() or "null")


def lit(xs):
    return "ARRAY[" + ",".join("'" + s.replace("'", "''") + "'" for s in xs) + "]::text[]"


def gather(pairs):
    slugs = sorted({s for p in pairs for s in p["pair"]})
    recs = psql(f"""SELECT json_object_agg(e.slug, json_build_object('name', e.name, 'line', e.distinguisher, 'sex', e.sex, 'tribe', e.tribe,
      'notes', left(e.notes, 600),
      'family', (SELECT json_agg(r.type||' '||t.slug||' ('||t.name||coalesce(', '||left(t.distinguisher,60),'')||')') FROM entity_relationship r JOIN entity t ON t.id=r.to_entity_id WHERE r.from_entity_id=e.id),
      'named_by', (SELECT json_agg(f.slug||' ('||f.name||coalesce(', '||left(f.distinguisher,60),'')||') '||r.type) FROM entity_relationship r JOIN entity f ON f.id=r.from_entity_id WHERE r.to_entity_id=e.id),
      'verses', (SELECT json_agg(json_build_array(v.canonical_book, v.canonical_chapter, v.canonical_verse) ORDER BY v.canonical_book, v.canonical_chapter, v.canonical_verse)
                 FROM (SELECT DISTINCT canonical_book, canonical_chapter, canonical_verse FROM entity_verse WHERE entity_id=e.id) v)))
      FROM entity e WHERE e.slug = ANY({lit(slugs)});""")
    refs = set()
    for p in pairs:
        for s in p["pair"]:
            refs |= {tuple(v) for v in (recs.get(s, {}).get("verses") or [])[:MAX_VERSES]}
        refs |= {tuple(v) for v in p["disputed_refs"]}
    values = ",".join(f"({b},{c},{v})" for b, c, v in sorted(refs))
    text = psql(f"""SELECT json_object_agg(t.slug||' '||q.b||' '||q.c||':'||q.v, x.line) FROM (VALUES {values}) q(b,c,v)
      CROSS JOIN (SELECT id, slug FROM text WHERE slug IN ('KJV','BHSA','NESTLE1904')) t
      CROSS JOIN LATERAL (SELECT string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
        FROM verse_reference vr JOIN verse ve ON ve.id = vr.verse_id AND ve.text_id = t.id JOIN word w ON w.verse_id = ve.id
        WHERE vr.canonical_book = q.b AND vr.canonical_chapter = q.c AND vr.canonical_verse = q.v AND vr.is_primary) x
      WHERE x.line IS NOT NULL;""") or {}
    return recs, text


def ref(b, c, v):
    return f"{BOOKS[b - 1]} {c}:{v}"


def check(ans, case):
    errs = []
    if ans["verdict"] == "one" and ans["keep"] not in ("A", "B"):
        errs.append('verdict "one" needs keep "A" or "B"')
    if ans["verdict"] != "one" and ans["keep"] != "none":
        errs.append('keep must be "none" unless verdict is "one"')
    if ans["verdict"] != "two" and ans["verses_of_other"]:
        errs.append('verses_of_other must be empty unless verdict is "two"')
    known = set(case["A"]["verses"]) | set(case["B"]["verses"])
    bad = [r for r in ans["verses_of_other"] if r not in known]
    if bad:
        errs.append(f"verses_of_other names verses neither record holds: {bad}")
    if not 0 <= ans["confidence"] <= 1:
        errs.append("confidence must be 0..1")
    if len(ans["why"]) < 40:
        errs.append("why is too short")
    return errs


def run(job):
    pair, recs, text = job
    a, b = pair["pair"]
    name = f"{a}--{b}"
    out = OUT / f"{name}.json"
    if out.exists():
        return name, "kept"
    if a not in recs or b not in recs:
        return name, "a record is gone on live (folded already?)"

    def rec(s):
        r = dict(recs[s])
        vs = r.pop("verses") or []
        r["verse_count"] = len(vs)
        r["verses"] = [ref(*v) for v in vs]
        r["kjv"] = {ref(*v): text.get(f"KJV {v[0]} {v[1]}:{v[2]}") for v in vs[:MAX_VERSES]}
        return r

    disputed = {}
    for b_, c, v in pair["disputed_refs"]:
        for t in ("BHSA", "NESTLE1904", "KJV"):
            k = f"{t} {b_} {c}:{v}"
            if k in text:
                disputed[f"{t} {ref(b_, c, v)}"] = text[k]
    case = {"name": pair["name"], "A": rec(a), "B": rec(b), "disputed_verses": disputed,
            "first_reader_said": pair["why"]}
    prompt = PROMPT.replace("{case}", json.dumps(case, ensure_ascii=False, indent=1))
    extra = ""
    for attempt in (1, 2):
        last = RAW / f"{name}.last.json"
        if last.exists():
            last.unlink()
        args = [CODEX, "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "-m", MODEL,
                "-c", f'model_reasoning_effort="{EFFORT}"', "--output-schema", str(HERE / "schema.json"), "-o", str(last), "-C", str(RAW)]
        p = subprocess.run(args, input=prompt + extra, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800)
        if p.returncode or not last.exists():
            return name, f"codex exit {p.returncode}: {p.stderr.strip()[-200:]}"
        ans = json.loads(last.read_text(encoding="utf-8"))
        errs = check(ans, case)
        if not errs:
            keep = {"A": a, "B": b}.get(ans["keep"])
            out.write_text(json.dumps({"pair": [a, b], "case": case, **ans, "keep_slug": keep,
                                       "fold_slug": (b if keep == a else a) if keep else None,
                                       "model": MODEL, "at": datetime.now(timezone.utc).isoformat(timespec="seconds")},
                                      ensure_ascii=False, indent=1), encoding="utf-8")
            return name, f"ok {ans['verdict']} {ans['keep']} {ans['confidence']}"
        extra = "\n\nYour previous answer was refused by the checker: " + "; ".join(errs) + ". Answer again."
    return name, "rejected: " + "; ".join(errs)


same = json.loads((HERE.parent / "namesakes-2026-10-08" / "same.json").read_text(encoding="utf-8"))
pairs = {}
for e in same:
    key = tuple(sorted((e["dataset_man"], e["split_man"])))
    p = pairs.setdefault(key, {"pair": list(key), "name": e["name"], "disputed_refs": [], "why": []})
    bk, cv = e["verse"].split(" ")
    c, v = cv.split(":")
    p["disputed_refs"].append([BOOKS.index(bk) + 1, int(c), int(v)])
    p["why"].append(f"{e['verse']}: {e['judge_why']}")
pairs = list(pairs.values())
(HERE / "pairs.json").write_text(json.dumps(pairs, ensure_ascii=False, indent=1), encoding="utf-8")
if len(sys.argv) > 1:
    pairs = pairs[:int(sys.argv[1])]
recs, text = gather(pairs)
with ThreadPoolExecutor(max_workers=3) as pool:
    for name, status in pool.map(run, [(p, recs, text) for p in pairs]):
        print(name, status, flush=True)
print("DONE", flush=True)
