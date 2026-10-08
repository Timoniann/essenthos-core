"""Writes the agreed folds (folds.json) into Essenthos.Forge/Loading/Encyclopedia/DuplicateRecords.json as merges,
skipping a pair the file already merges either way. The Strong number of each pair is the one the folded and kept
records' original words carry most (one read-only query against live). Prints what it added and skipped."""
import collections, json, subprocess
from pathlib import Path

HERE = Path(__file__).parent
FILE = HERE.parents[3] / "Essenthos.Forge" / "Loading" / "Encyclopedia" / "DuplicateRecords.json"
SOURCE = ("Essenthos: the namesakes judge's pairs of one man held twice (PRB-0490), each read by gpt-6.1-sol (Codex) and, "
          "blind, by claude-opus-5-5, folded where both call them one person, 2026-10-08")

folds = json.loads((HERE / "folds.json").read_text(encoding="utf-8"))
raw = FILE.read_bytes().decode("utf-8")
doc = json.loads(raw)
have = {frozenset((m["keeps"], m["folds"])) for m in doc["merges"]}
# a record the file splits others from stays the kept one, so the splits keep their source
split_from = {x["from"] for x in doc["splits"]}
for f in folds:
    if f["fold"] in split_from and f["keep"] not in split_from:
        f["keep"], f["fold"] = f["fold"], f["keep"]
        print("kept the split source instead:", f["fold"], "->", f["keep"])
todo = [f for f in folds if frozenset((f["keep"], f["fold"])) not in have]
for f in folds:
    if f not in todo:
        print("skip, already merged:", f["fold"], "->", f["keep"])

slugs = sorted({s for f in todo for s in (f["keep"], f["fold"])})
arr = "ARRAY[" + ",".join("'" + s + "'" for s in slugs) + "]::text[]"
sql = f"""SELECT json_agg(json_build_array(e.slug, w.strong_number, n)) FROM (
  SELECT we.entity_id, w.strong_number, count(*) n FROM word_entity we JOIN word w ON w.id = we.word_id
  JOIN text t ON t.id = w.text_id AND t.slug IN ('BHSA','NESTLE1904')
  WHERE we.entity_id IN (SELECT id FROM entity WHERE slug = ANY({arr})) AND w.strong_number IS NOT NULL
  GROUP BY 1, 2) w JOIN entity e ON e.id = w.entity_id;"""
p = subprocess.run(["docker", "exec", "-i", "essenthos-core-db-1", "sh", "-c",
                    'psql -q -At -U "$POSTGRES_USER" -d essenthos_core'], input=sql, capture_output=True, text=True, encoding="utf-8")
counts = collections.defaultdict(collections.Counter)
for slug, strong, n in json.loads(p.stdout.strip() or "[]"):
    counts[slug][strong] += n

base = len(doc["merges"])
for f in todo:
    c = counts[f["fold"]] + counts[f["keep"]]
    strong = c.most_common(1)[0][0] if c else None
    if strong is None:
        print("skip, no Strong number:", f["fold"], "->", f["keep"])
        continue
    why = f["codex_why"].strip()
    doc["merges"].append({"strongNumber": strong, "keeps": f["keep"], "folds": f["fold"], "why": why,
                          "confidence": round(min(f["codex_confidence"], 0.95), 2), "source": SOURCE})
    print("add", strong, f["fold"], "->", f["keep"])

# spliced in as text after the last merge, so the hand-kept layout of the rest of the file is left as it is
added = doc["merges"][base:]
block = ",\n".join("    {\n" + ",\n".join(f"      {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}" for k, v in m.items())
                   + "\n    }" for m in added)
nl = "\r\n" if "\r\n" in raw else "\n"
end = '    }\n  ],\n  "declined"'.replace("\n", nl)
assert raw.count(end) == 1
FILE.write_bytes(raw.replace(end, ("    },\n" + block + '\n  ],\n  "declined"').replace("\n", nl)).encode("utf-8"))
