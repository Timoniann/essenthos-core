"""Draw a hand-check sample of EVIDENTIA's links for a text that has no gold.

Usage: python evidentia-link-sample.py <benchmark folder> <run prefix, e.g. bbe> <out.tsv> [count]

Reads the <prefix>-*.disagreements.json files evidentia-benchmark.ps1 wrote. With no answer key
every final link is written there, with its verse, the original word it was put on and what the
route texts said about it. The sample is spread over the passages in proportion to their links, at
least three from each, and drawn with random.Random(SEED) over rows in a stable order, so the same
reports give the same sample on any machine.

A report of a text that may not be redistributed quotes its verses, and so does this sample: keep
both on the machine that made them.
"""
import csv, glob, json, os, random, sys

SEED = 20260924
MINIMUM = 3

folder, prefix, out = sys.argv[1], sys.argv[2], sys.argv[3]
count = int(sys.argv[4]) if len(sys.argv) > 4 else 50

passages = {}
for path in sorted(glob.glob(os.path.join(folder, f"{prefix}-*.disagreements.json"))):
    name = os.path.basename(path)[len(prefix) + 1:-len(".disagreements.json")]
    rows = json.load(open(path, encoding="utf-8"))
    rows.sort(key=lambda r: (r["CanonicalBook"], r["CanonicalChapter"], r["CanonicalVerse"],
                             r["SourcePosition"], r["TargetWordId"]))
    passages[name] = rows
if not passages:
    raise SystemExit(f"No {prefix}-*.disagreements.json in {folder}; run the benchmark with --disagreements first.")

total = sum(len(rows) for rows in passages.values())
allocation = {name: min(len(rows), MINIMUM) for name, rows in passages.items()}
remaining = count - sum(allocation.values())
spare = sum(len(rows) - allocation[name] for name, rows in passages.items())
for name, rows in passages.items():
    allocation[name] += min(len(rows) - allocation[name], round(remaining * (len(rows) - allocation[name]) / max(1, spare)))

rng = random.Random(SEED)
sample = []
for name in passages:
    for row in sorted(rng.sample(passages[name], allocation[name]),
                      key=lambda r: (r["CanonicalChapter"], r["CanonicalVerse"], r["SourcePosition"])):
        sample.append((name, row))

with open(out, "w", encoding="utf-8", newline="") as handle:
    writer = csv.writer(handle, delimiter="\t")
    writer.writerow(["passage", "book", "chapter", "verse", "word", "original", "strong", "lemma", "gloss",
                     "tier", "confidence", "routes", "verse text", "right? (y/n/?)"])
    for name, row in sample:
        writer.writerow([name, row["CanonicalBook"], row["CanonicalChapter"], row["CanonicalVerse"],
                         row["SourceSurface"], row["TargetSurface"], row["TargetStrongNumber"] or "",
                         row["TargetLemma"] or "", row["TargetGloss"] or "", row["Tier"],
                         f"{row['Confidence']:.2f}", row.get("Routes") or "no route reaches it",
                         row["SourceVerse"], ""])

print(f"{len(sample)} of {total:,} links from {len(passages)} passages written to {out}")
