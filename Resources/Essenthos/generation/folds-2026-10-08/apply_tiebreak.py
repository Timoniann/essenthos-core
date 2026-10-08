"""The tie-break (tiebreak.json, a third reader, claude-fable-5-1) settled by majority: a pair is folded where two of
the three readers call it one person with the same kept record, and a verse is moved where two readers call the pair
two people and both put that verse on the other record. Everything else stays as it is. Writes merges and splits into
Essenthos.Forge/Loading/Encyclopedia/DuplicateRecords.json as text, leaving the rest of the file's layout alone."""
import collections, json, subprocess
from pathlib import Path

HERE = Path(__file__).parent
FILE = HERE.parents[3] / "Essenthos.Forge" / "Loading" / "Encyclopedia" / "DuplicateRecords.json"
SOURCE = ("Essenthos: the namesakes judge's pairs of one man held twice (PRB-0490), read by gpt-6.1-sol (Codex), blind by "
          "claude-opus-5-5 and, where they differed, by claude-fable-5-1; folded where two of the three call them one person, 2026-10-08")

tie = {tuple(p["pair"]): p for p in json.loads((HERE / "tiebreak.json").read_text(encoding="utf-8"))["pairs"]}
prev = {tuple(e["pair"]): e for f in ("open", "two") for e in json.loads((HERE / f"{f}.json").read_text(encoding="utf-8"))}
merges, splits = [], []
for pair, t in tie.items():
    e = prev[pair]
    readers = [(e["codex"], e["codex_keep"]), (e["judge"], e["judge_keep"]), (t["verdict"], t["keep"])]
    ones = collections.Counter(k for v, k in readers if v == "one")
    keep, n = ones.most_common(1)[0] if ones else (None, 0)
    if n >= 2:
        merges.append({"keep": keep, "fold": next(s for s in pair if s != keep), "why": t["why"], "confidence": t["confidence"]})
        continue
    if sum(1 for v, _ in readers if v == "two") >= 2 and e["codex"] == "two":
        for m in t["move"]:
            if m["verse"] in e["codex_verses_of_other"]:
                splits.append({"from": m["from"], "to": m["to"], "name": None, "verses": [m["verse"]], "why": t["why"]})

slugs = sorted({s for m in merges for s in (m["keep"], m["fold"])})
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


def obj(d, indent):
    pad = " " * indent
    lines = []
    for k, v in d.items():
        if isinstance(v, list):
            inner = ",\n".join(f"{pad}    {json.dumps(x, ensure_ascii=False)}" for x in v)
            lines.append(f"{pad}  {json.dumps(k)}: [\n{inner}\n{pad}  ]")
        else:
            lines.append(f"{pad}  {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}")
    return f"{pad}{{\n" + ",\n".join(lines) + f"\n{pad}}}"


new_merges = []
for m in merges:
    strong = (counts[m["fold"]] + counts[m["keep"]]).most_common(1)
    if not strong:
        print("skip, no Strong number:", m)
        continue
    new_merges.append({"strongNumber": strong[0][0], "keeps": m["keep"], "folds": m["fold"], "why": m["why"],
                       "confidence": round(min(m["confidence"], 0.9), 2), "source": SOURCE})
    print("fold", m["fold"], "->", m["keep"])
for s in splits:
    print("move", s["verses"], s["from"], "->", s["to"])

raw = FILE.read_bytes().decode("utf-8")
shipped = {(s["from"], v) for s in json.loads(raw)["splits"] for v in s["verses"]}
splits = [s for s in splits if (s["from"], s["verses"][0]) not in shipped]  # NEH 11:15 Hashabiah was split already
nl ="\r\n" if "\r\n" in raw else "\n"
end_merges = '    }\n  ],\n  "declined"'.replace("\n", nl)
end_file = '    }\n  ]\n}'.replace("\n", nl)
assert raw.count(end_merges) == 1 and raw.rstrip().endswith(end_file)
if new_merges:
    raw = raw.replace(end_merges, ("    },\n" + ",\n".join(obj(m, 4) for m in new_merges) + '\n  ],\n  "declined"').replace("\n", nl))
if splits:
    i = raw.rstrip().rindex(end_file)
    raw = raw[:i] + ("    },\n" + ",\n".join(obj(s, 4) for s in splits) + "\n  ]\n}\n").replace("\n", nl)
FILE.write_bytes(raw.encode("utf-8"))
json.loads(raw)
