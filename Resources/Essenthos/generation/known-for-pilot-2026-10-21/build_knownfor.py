"""Writes Essenthos.Forge/Loading/Encyclopedia/KnownFor.json from the pilot and the German and Spanish renderings."""
import json
import re
import sys

pilot_path, renderings_path, out_path = sys.argv[1:4]
pilot = json.load(open(pilot_path, encoding="utf-8"))
renderings = json.load(open(renderings_path, encoding="utf-8")) if renderings_path != "-" else {}

BOOKED = re.compile(r"^([1-4]?[A-Z]{2,3}) (\d+(?::\d+)?(?:-\d+(?::\d+)?)?)$")
BARE = re.compile(r"^\d+(?::\d+)?(?:-\d+(?::\d+)?)?$")


def spans(written):
    book = None
    out = []
    for piece in (p.strip() for p in written.split(",")):
        m = BOOKED.match(piece)
        if m:
            book = m.group(1)
            out.append(piece)
        elif BARE.match(piece) and book:
            out.append(f"{book} {piece}")
        else:
            raise SystemExit(f"cannot read reference {piece!r} in {written!r}")
    return out


records = []
for person in pilot["people"]:
    clauses = []
    for at, clause in enumerate(person["known_for"]["clauses"]):
        rendered = renderings.get(person["slug"], [])
        one = rendered[at] if at < len(rendered) else {}
        if one and one.get("en") != clause["en"]:
            raise SystemExit(f"{person['slug']} clause {at}: rendering is of {one.get('en')!r}, not {clause['en']!r}")
        entry = {"english": clause["en"], "ukr": one.get("ukr") or clause["uk"]}
        for language in ("deu", "spa"):
            if one.get(language):
                entry[language] = one[language]
        entry["references"] = spans(clause["refs"])
        clauses.append(entry)
    if len(renderings.get(person["slug"], [])) not in (0, len(clauses)):
        raise SystemExit(f"{person['slug']}: {len(renderings[person['slug']])} renderings for {len(clauses)} clauses")
    records.append({"slug": person["slug"], "clauses": clauses})

source = (
    "Essenthos, what a person is known for, written from the person's verses in the King James Version "
    "with the verses each clause rests on, in English and Ukrainian by claude-opus-5-5 on 2026-10-21 and "
    "read again clause by clause against those verses by a second, separate reading; rendered into German "
    "and Spanish by claude-opus-5-5 on 2026-10-21; on the project owner's instruction"
)
json.dump({"source": source, "records": records}, open(out_path, "w", encoding="utf-8", newline="\n"),
          ensure_ascii=False, indent=2)
open(out_path, "a", encoding="utf-8", newline="\n").write("\n")
print(len(records), "people", sum(len(r["clauses"]) for r in records), "clauses")
