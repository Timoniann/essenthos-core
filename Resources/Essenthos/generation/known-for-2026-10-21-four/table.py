"""Writes four.md, the table to read, from four.json and the German and Spanish renderings."""
import json
import sys

folder = sys.argv[1]
doc = json.load(open(f"{folder}/four.json", encoding="utf-8"))
renderings = json.load(open(f"{folder}/renderings-deu-spa.json", encoding="utf-8"))
c = doc["counts"]

out = [
    "# «Known for»: Adam, Eve, Jesus and God (FTR-1109), 2026-10-21",
    "",
    "**What this is.** The owner asked on 2026-10-21 for the «known for» line for Adam and Eve, and for Jesus and God "
    "«якшо не важко». God is the record `yhvh`, the one the site shows as God (LORD / Господь / HERR / Jehová). "
    "Made the pilot's way (`../known-for-pilot-2026-10-21/pilot.md`), under the owner's rulings on the pilot: deeds or "
    "events a reader would name, more than three where a life earns it but not overdone, no minor details. For Jesus "
    "and God: only what the text says, in its own terms; no titles of the risen Christ.",
    "",
    "**How it was made.** A first reader (Claude Opus 5.5) read the KJV verses from the live site, read-only, and wrote "
    "the clauses with references in English and Ukrainian (`raw/draft.json`; the verses in `raw/packet.txt`). A second, "
    "separate reader (another Claude Opus 5.5 run) checked every clause against the KJV text of its verses and the "
    "Ukrainian against the English (`raw/check.json`). Its fixes are applied; the first wording is kept in `four.json`. "
    "German and Spanish (`renderings-deu-spa.json`) were rendered from the English with the Ukrainian beside it, names as "
    "the live site gives them in Luther 1912 and Reina-Valera 1909, and read a second time against the English. No Russian.",
    "",
    f"**Counts.** {c['people']} records, {c['clauses']} clauses. Second reading: {c['second_reading']['right']} right, "
    f"{c['second_reading']['fix']} fixed (†).",
    "",
    "| Record | Current summary | Known for (English) | Known for (Ukrainian) | References |",
    "|---|---|---|---|---|",
]
for p in doc["people"]:
    cl = p["known_for"]["clauses"]
    mark = lambda one: "†" if one["check"]["verdict"] == "fix" else ""
    out.append(
        f"| **{p['name_en']}** / {p['name_uk']} | {p['current_summary']['en']} | "
        + "; ".join(one["en"] + mark(one) for one in cl) + " | "
        + "; ".join(one["uk"] for one in cl) + " | "
        + " · ".join(one["refs"] for one in cl) + " |")

out += ["", "## German and Spanish", "", "| Record | German | Spanish |", "|---|---|---|"]
for p in doc["people"]:
    rows = renderings[p["slug"]]
    out.append(f"| **{p['name_en']}** | " + "; ".join(r["deu"] for r in rows) + " | " + "; ".join(r["spa"] for r in rows) + " |")

out += ["", "## What the second reading changed", ""]
for p in doc["people"]:
    for one in p["known_for"]["clauses"]:
        if one["check"]["verdict"] == "fix":
            was = "; ".join(f"{k} was «{v}»" for k, v in one.get("first", {}).items())
            out.append(f"- **{p['name_en']}** † — {one['check']['why']} ({was})")
for p in doc["people"]:
    for one in p["known_for"]["clauses"]:
        if one.get("after_check"):
            was = "; ".join(f"{k} was «{v}»" for k, v in one.get("first", {}).items())
            out.append(f"- **{p['name_en']}** — {one['after_check']} ({was})")

out += ["", "## Selection notes not acted on", "",
        "The second reading's remarks on what else could be chosen. Adam's fatherhood and the Twelve were left out: the "
        "current summary already says them («father of Seth, Cain, Abel», «teacher of the Twelve»). Eve's children stay, "
        "as the pilot kept Sarah's and Ruth's. The burning bush is already Moses' clause; God's line keeps to five.", ""]
for p in doc["people"]:
    if p.get("selection_note"):
        out.append(f"- **{p['name_en']}**: {p['selection_note']}")
open(f"{folder}/four.md", "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
