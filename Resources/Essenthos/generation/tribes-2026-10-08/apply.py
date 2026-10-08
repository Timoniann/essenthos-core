"""PRB-0571: the reading's answers as the file the loader ships with. In: batches.json and out/*.json (run.py). Out:
Essenthos.Forge/Loading/Encyclopedia/EponymReadings.json — each word by its witness's own address, the record its
sentence means (null where the reading could not say) and the other of the two — and a tally on stdout."""
import json, sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).parent
TARGET = HERE.parents[3] / "Essenthos.Forge" / "Loading" / "Encyclopedia" / "EponymReadings.json"

words = {}
for batch in json.loads((HERE / "batches.json").read_text(encoding="utf-8")):
    for verse in batch["verses"]:
        for w in verse["words"]:
            words[w["key"]] = w

readings, missing, tally = [], [], Counter()
answers = {}
for f in sorted((HERE / "out").glob("*.json")):
    for a in json.loads(f.read_text(encoding="utf-8"))["answers"]:
        answers[a["key"]] = a
for key, w in words.items():
    a = answers.get(key)
    if a is None:
        missing.append(key)
        continue
    text, rest = key.split(" ", 1)
    reference, position = rest.rsplit(" #", 1)
    man, people = w["man"]["slug"], w["people"]["slug"]
    names = {"man": man, "people": people}.get(a["answer"])
    readings.append({"text": text, "reference": reference, "position": int(position), "surface": w["word"],
                     "names": names, "instead": people if names == man else man,
                     "confidence": round(float(a["confidence"]), 2), "why": a["why"]})
    tally[(man, a["answer"])] += 1

TARGET.write_text(json.dumps({"model": "gpt-6.1-sol (Codex), reasoning effort medium", "read": "2026-10-08",
                              "readings": readings}, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(len(readings), "readings,", len(missing), "words not answered")
for tribe in sorted({m for m, _ in tally}):
    print(f"  {tribe}: man {tally[(tribe, 'man')]}, people {tally[(tribe, 'people')]}, unclear {tally[(tribe, 'unclear')]}")
print("all:", Counter(a for _, a in tally.elements()))
if missing and "--partial" not in sys.argv:
    sys.exit("not every word is answered; run run.py again, or pass --partial")
