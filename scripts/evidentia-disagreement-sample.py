"""Draw the EVIDENTIA disagreement sample mechanically from the files --disagreements writes.

Usage: python evidentia-disagreement-sample.py <folder holding ind-gen.json, ind-ruth.json, ind-jon.json, ind-mark.json>
Writes sample.json into the same folder.

Strata are passage x gold source. Every stratum gets at least MINIMUM rows, the rest is allocated in
proportion to its size, and each stratum is drawn with random.Random(SEED) over rows sorted by a
stable key, so the same dumps give the same sample on any machine.
"""
import json, os, random, sys

SEED = 20260916
TOTAL = 130
MINIMUM = 12
HERE = sys.argv[1]
PASSAGES = [("ind-gen", "Genesis 1-10"), ("ind-ruth", "Ruth"), ("ind-jon", "Jonah"), ("ind-mark", "Mark 1-4")]


def gold_label(row):
    sources = row["GoldSources"]
    if not sources:
        return "uncovered"
    if any("Clear Bible" in s for s in sources) and not any("Berean" in s for s in sources):
        return "clear"
    if any("Clear Bible" in s for s in sources):
        return "berean+clear"
    return "berean"


def key(row):
    return (row["CanonicalBook"], row["CanonicalChapter"], row["CanonicalVerse"], row["SourcePosition"], row["TargetWordId"])


rows = []
for tag, name in PASSAGES:
    for row in json.load(open(os.path.join(HERE, tag + ".json"), encoding="utf-8")):
        row["Passage"] = name
        # Uncovered rows belong to the passage's gold as a whole: in Mark both golds are in scope.
        row["Stratum"] = f"{name} / {'berean' if gold_label(row) == 'uncovered' and tag != 'ind-mark' else gold_label(row)}"
        rows.append(row)

strata = {}
for row in sorted(rows, key=key):
    strata.setdefault(row["Stratum"], []).append(row)

allocation = {name: min(len(members), MINIMUM) for name, members in strata.items()}
remaining = TOTAL - sum(allocation.values())
pool = sum(len(m) - allocation[n] for n, m in strata.items())
for name, members in strata.items():
    allocation[name] += min(len(members) - allocation[name], round(remaining * (len(members) - allocation[name]) / pool))

rng = random.Random(SEED)
sample = []
for name in sorted(strata):
    sample += rng.sample(strata[name], allocation[name])
sample.sort(key=lambda r: (["Genesis 1-10", "Ruth", "Jonah", "Mark 1-4"].index(r["Passage"]),) + key(r))

summary = {name: {"disagreements": len(members), "sampled": allocation[name]} for name, members in sorted(strata.items())}
json.dump({"seed": SEED, "strata": summary, "sample": sample},
          open(os.path.join(HERE, "sample.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(json.dumps(summary, indent=1))
print("sampled", len(sample), "of", len(rows))
