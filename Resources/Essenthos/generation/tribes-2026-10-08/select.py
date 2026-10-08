"""PRB-0571: which words of a tribe's name the reading is asked about.

In: items.csv, every BHSA proper noun whose number one people and its ancestor both bear, and every NESTLE1904 word
whose Greek name Strong derives from such a number, with its construct and the annotations it carried on the copy of
2026-10-08. Out: selected.json (the words to read) and a tally on stdout.

A word is read when it stands alone (not after "sons of", the word for a tribe, or a realm's word, which the owner's
rulings settle) and nothing better than the interim rule or a reading made before the tribes were records names it:
no ruling, no source's statement, no gentilic, and no reading that already names the people unopposed."""
import csv, json, sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).parent
REALM = {"H4428", "H4467", "H4438", "H776", "H127", "H5892", "H1366", "H7704", "H2022"}
GREEK_REALM = {"G935", "G932", "G1093", "G5561", "G4172", "G3725", "G68", "G3735"}
TRIBE = {"H4294", "H7626"}
INTERIM = "on the project owner's ruling of 2026-09-16"
BEFORE_THE_TRIBES = "prompt sense-1"


def context(r):
    if r["text"] == "BHSA":
        if r["prevnum"] == "H1121" and r["prevstate"] == "c" and r["prevnumber"] == "pl":
            return "sons"
        if r["prevnum"] in TRIBE:
            return "tribe"
        if r["prevnum"] in REALM:
            return "realm"
        return "bare"
    before = [r["prevnum"], r["prev2num"]]
    if before[0] == "G5207" or (before[0] == "G3588" and before[1] == "G5207"):
        return "sons"
    if "G5443" in before:
        return "tribe"
    if before[0] in GREEK_REALM or (before[0] == "G3588" and before[1] in GREEK_REALM):
        return "realm"
    return "bare"


def read(r):
    """Whether the word is asked about, and why not where it is not."""
    answers = [a.split("|", 4) for a in r["ann"].split(" ## ")] if r["ann"] else []
    candidates = {r["man"], r["people"]}
    if any(slug not in candidates for slug, *_ in answers):
        return "names another record"
    if any(method in ("manual", "stated-by-source") for _, _, method, _, _ in answers):
        return "ruled"
    if any(kind == "people" and method == "lexical" for _, kind, method, _, _ in answers):
        return "gentilic"
    if any(method in ("lexical", "strong-number") for _, _, method, _, _ in answers):
        return "the name's own lexeme"
    if r["text"] != "BHSA" and r["num"] != "G2474":
        return "not Israel"
    weak = lambda method, source: INTERIM in source or BEFORE_THE_TRIBES in source or method in ("lexical", "strong-number") \
        or "read from the verses that name it" in source or "several records bear" in source
    strong = [(slug, kind) for slug, kind, method, _, source in answers if not weak(method, source)]
    if strong and all(kind == "people" for _, kind in strong) and not any(
            kind == "person" and BEFORE_THE_TRIBES in source for _, kind, _, _, source in answers):
        return "read as the people already"
    if any(INTERIM not in source and "owner" in source for *_, source in answers):
        return "ruled"
    return None


rows = list(csv.DictReader(open(HERE / "items.csv", encoding="utf-8")))
selected, why_not = [], Counter()
for r in rows:
    ctx = context(r)
    if ctx != "bare":
        why_not[ctx] += 1
        continue
    reason = read(r)
    if reason:
        why_not[reason] += 1
        continue
    selected.append({k: r[k] for k in ("id", "text", "b", "c", "n", "l", "p", "surface", "num", "cb", "cc", "cv", "man", "people", "ann")})
(HERE / "selected.json").write_text(json.dumps(selected, ensure_ascii=False, indent=0), encoding="utf-8")
print(len(rows), "words;", len(selected), "to read;", dict(why_not))
print(Counter((s["text"], s["man"]) for s in selected).most_common())
