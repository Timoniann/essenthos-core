"""Assembles four.json (pilot.json's shape) from the draft, the second reading and the record as the live API serves it."""
import json
import sys
import urllib.request

folder = sys.argv[1]
draft = json.load(open(f"{folder}/raw/draft.json", encoding="utf-8"))
check = json.load(open(f"{folder}/raw/check.json", encoding="utf-8"))

# Optional suggestions of the second reading that were taken although the verdict was "right".
TAKEN = {
    ("jesus", 2): {"refs": "MAT 3:13-17, MRK 1:9", "note": "MRK 1:9 added, as the second reading suggested"},
    ("yhvh", 1): {"uk": "створив небо й землю, а людину — на свій образ", "note": "Ukrainian smoothed, as the second reading suggested"},
    ("yhvh", 2): {"en": "brought the Flood upon the earth, then made a covenant with Noah never to send another",
                  "uk": "навів на землю потоп, а потім уклав із Ноєм заповіт більше не наводити потопу",
                  "note": "shortened after the check for length; same verses, same sense"},
    ("yhvh", 4): {"refs": "EXO 12:51, 19:20, 20:1-17, 31:18", "note": "EXO 19:20 added, as the second reading suggested"},
}


def get(url):
    return json.load(urllib.request.urlopen("http://localhost:5279" + url))


def summary(record):
    d = record.get("descriptor")
    return "".join(p["text"] for p in d["parts"]) if d else None


people = []
records = {}
for slug, clauses in draft.items():
    en = get(f"/v1/entities/{slug}")
    uk = get(f"/v1/entities/{slug}?language=ukr&prose=ukr")
    records[slug] = {"slug": slug, "name_en": en["name"], "name_uk": uk.get("localName"), "sex": en.get("sex"),
                     "distinguisher": en.get("distinguisher"), "references": en.get("references"),
                     "summary_en": summary(en), "summary_uk": summary(uk)}
    out = []
    for n, (c_en, c_uk, refs) in enumerate(clauses, 1):
        verdict = next(v for v in check[slug] if v["clause"] == n)
        clause = {"en": c_en, "uk": c_uk, "refs": refs,
                  "check": {"verdict": verdict["verdict"], "why": verdict.get("why")}}
        first = {}
        for field, key in (("en", "fix_en"), ("uk", "fix_uk"), ("refs", "fix_refs")):
            if verdict.get(key):
                first[field] = clause[field]
                clause[field] = verdict[key]
        taken = TAKEN.get((slug, n))
        if taken:
            for field in ("en", "uk", "refs"):
                if field in taken:
                    first[field] = clause[field]
                    clause[field] = taken[field]
            clause["after_check"] = taken["note"]
        if first:
            clause["first"] = first
        out.append(clause)
    people.append({
        "slug": slug, "name_en": records[slug]["name_en"], "name_uk": records[slug]["name_uk"],
        "verses": records[slug]["references"], "distinguisher": records[slug]["distinguisher"],
        "current_summary": {"en": records[slug]["summary_en"], "uk": records[slug]["summary_uk"]},
        "known_for": {
            "en": "; ".join(f"{c['en']} ({c['refs']})" for c in out),
            "uk": "; ".join(f"{c['uk']} ({c['refs']})" for c in out),
            "length_en": len("; ".join(c["en"] for c in out)),
            "length_uk": len("; ".join(c["uk"] for c in out)),
            "clauses": out,
        },
        "selection_note": check.get("selection", {}).get(slug),
    })

checked = [c["check"]["verdict"] for p in people for c in p["known_for"]["clauses"]]
doc = {
    "batch": "FTR-1109 known for: Adam, Eve, Jesus and God",
    "date": "2026-10-21",
    "first_reading": "claude-opus-5-5 (Claude Code agent): read the KJV verses (live API, read-only) and wrote the clauses with references, English and Ukrainian, after the pilot's method",
    "second_reading": "a separate claude-opus-5-5 agent with no part in the first reading: checked every clause against the KJV text of its cited verses; verdicts in raw/check.json",
    "status": "model-produced, unreviewed by the owner",
    "counts": {"people": len(people), "clauses": len(checked),
               "second_reading": {"checked": len(checked), "right": checked.count("right"), "fix": checked.count("fix")}},
    "owner_rulings": [
        {"at": "2026-10-21", "text": "«Також зроби для Адама та Єви також описи. І для Ісуса і для Бога, якшо не важко.»"},
        {"at": "2026-10-21", "text": "no minor details; more than three clauses allowed but not overdone"},
    ],
    "people": people,
}
json.dump(doc, open(f"{folder}/four.json", "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
json.dump(records, open(f"{folder}/raw/records.json", "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
print(doc["counts"])
for p in people:
    print(p["slug"], p["known_for"]["length_en"], p["known_for"]["length_uk"])
