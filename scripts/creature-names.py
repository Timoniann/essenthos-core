"""A familiar creature's name in each reader language, from how the translations render its word (PRB-1992).

    python -I scripts/creature-names.py count <Resources/Essenthos> <counts.json> [--essx <essx.exe>]
    python -I scripts/creature-names.py table <Resources/Essenthos> <counts.json> <decisions.tsv> <table.tsv>
    python -I scripts/creature-names.py rows  <Resources/Essenthos> <table.tsv> <rows.records.tsv>

count: for every creature record (kind being, subtype creature) and each of KJV, BSB, UBIO, UKR1871, LUTH1912, RV1909 and
RUSV, the words of the text that carry the record in names/projected, counted by spelling; where the projection names no
word of a text for the record, the words the text's final links join to the record's original words (names/BHSA and
names/NESTLE1904), counted the same way and marked "linked".

table: the review table (Resources/Essenthos/creature-names/creature-names.tsv): the decisions (familiar or not, the name
in each language, why a record keeps its original name) beside the counts behind them and the record's original word.

rows: owner rows for every familiar record: its English name (records/records), its nominative in ukr, deu, spa and rus
(records/i18n/<lang>/name-forms), and its line in every language with the original name put first, where the line began
with the name it now has, without it ("лев, поетичне слово" under Лев becomes "шахал · поетичне слово").
"""
import argparse
import collections
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

TEXTS = ["KJV", "BSB", "UBIO", "UKR1871", "LUTH1912", "RV1909", "RUSV"]
ORIGINALS = ["BHSA", "NESTLE1904"]
NAMED = ["ukr", "deu", "spa", "rus"]
LINES = ["ukr", "deu", "spa", "rus"]
PUNCTUATION = ".,;:!?()[]«»\"'“”‘’„—–-…"
ARTICLES = {
    "eng": ["the ", "a ", "an "],
    "deu": ["der ", "die ", "das ", "ein ", "eine "],
    "spa": ["el ", "la ", "los ", "las ", "un ", "una "],
    "ukr": [],
    "rus": [],
}
ENTRY = "PRB-1992"
BY = ("Essenthos: a familiar creature named as the established translation of each reader language renders its word "
      "most (English the Berean Standard Bible, with the King James beside it; Ukrainian the Ohienko; German Luther 1912; "
      "Spanish the Reina-Valera 1909; Russian the Synodal), counted over the words that carry the record, and put in its "
      "dictionary form by claude-opus-5-5 on 2026-11-24, on the project owner's instruction of 2026-11-24 that the "
      "animals everyone knows are named in the reader's language and the creatures nobody can translate keep their "
      "original name")


def rows(path):
    """The rows of a product TSV as dicts by column name, its # header skipped."""
    with open(path, encoding="utf-8") as f:
        header = None
        for line in f:
            if line.startswith("#"):
                continue
            parts = line.rstrip("\n").split("\t")
            if header is None:
                header = [h.split(":")[0] for h in parts]
                continue
            yield {k: (None if v == "\\N" else v) for k, v in zip(header, parts)}


def creatures(store):
    return {r["slug"]: r["name"] for r in rows(store / "records" / "records.tsv")
            if r["kind"] == "being" and r["subtype"] == "creature"}


def essx_tsv(essx, product, folder):
    out = folder / (product.name + ".tsv")
    subprocess.run([str(essx), "convert", str(product), str(out)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return out


def fold(surface):
    return surface.strip(PUNCTUATION).lower()


def count(store, out, essx):
    beings = creatures(store)
    originals = {o: collections.defaultdict(set) for o in ORIGINALS}
    for o in ORIGINALS:
        for r in rows(store / "names" / f"{o}.tsv"):
            if r["record"] in beings:
                originals[o][r["own"]].add(r["record"])
    counts = {}
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        for text in TEXTS:
            words = {r["own"]: fold(r["surface"])
                     for r in rows(essx_tsv(essx, store / "texts" / text / "words.essx", temp))}
            projected = collections.defaultdict(collections.Counter)
            for r in rows(essx_tsv(essx, store / "names" / "projected" / f"{text}.essx", temp)):
                if r["record"] in beings and r["role"] == "name":
                    projected[r["record"]][words.get(r["own"], "?")] += 1
            linked = collections.defaultdict(collections.Counter)
            for o in ORIGINALS:
                final = store / "mapping" / f"{text}-{o}" / "final.essx"
                for r in rows(essx_tsv(essx, final, temp)):
                    named = set().union(*(originals[o].get(k, set()) for k in r["to"].split()))
                    for record in named - projected.keys():
                        for k in r["from"].split():
                            linked[record][words.get(k, "?")] += 1
            counts[text] = {record: {"stage": "projected", "forms": dict(c.most_common())} for record, c in projected.items()}
            for record, c in linked.items():
                counts[text].setdefault(record, {"stage": "linked", "forms": dict(c.most_common())})
            print(text, len(projected), "projected,", len(linked), "linked", file=sys.stderr)
    Path(out).write_text(json.dumps(counts, ensure_ascii=False, indent=0), encoding="utf-8")


def evidence(entry, take=8):
    if not entry:
        return ""
    forms = ", ".join(f"{form} {n}" for form, n in list(entry["forms"].items())[:take])
    return forms if entry["stage"] == "projected" else f"linked: {forms}"


def original_word(store):
    first = {}
    for r in rows(store / "records" / "names.tsv"):
        if r["n"] == "1":
            word = r["hebrew"] or r["greek"]
            spelled = r["hebrew_transliterated"] or r["greek_transliterated"]
            first[r["slug"]] = " ".join(x for x in [word, spelled] if x)
    return first


TABLE_HEAD = """# Creature names: which creature records the reader sees under the animal's name in their language, and which keep the original word (PRB-1992)
# Hand file: the decisions are the columns familiar, eng, ukr, deu, spa, rus and why; scripts/creature-names.py rows writes owner rows from them.
# The rule: a creature is familiar when KJV, BSB, the Ohienko (UBIO; the Kulish where it gives no word), Luther 1912 and the Reina-Valera 1909
#   each name mostly one and the same animal for the record's words, as a word that names that animal today (Luther's Otter, now the otter, and
#   the Spanish pavo, now the turkey, do not), and the project owner did not keep it as it is (Behemoth, Leviathan, Tannin, Lilith, Rahab,
#   the seraphim, the saraph serpents, the se'irim, the dragon of Babylon). Translations that disagree, transliterate, give no word, or give no
#   animal (young, creatures, swarm) keep the original name.
# The name in a language is the rendering its text prints most (English the Berean Standard Bible), in its dictionary form; a diminutive or a
#   young animal's word built on the animal's own (левчук, осля, козеня, голубеня, бичок, Böcklein, leoncillo, cabrito) counts as the animal;
#   a female's word stays as printed (ослиця, Eselin, asna). rus is the Synodal's, left empty where it names another animal.
# Counts: the words of each text that carry the record (names/projected), by spelling; "linked:" where the projection names none and the
#   counts are the text's words its final links join to the record's original words. Counted on the store of 2026-11-24.
"""
TABLE_COLUMNS = ["record", "original", "name_until_now", "familiar", "eng", "ukr", "deu", "spa", "rus", "why"] + TEXTS


def table(store, counts_path, decisions_path, out):
    counts = json.loads(Path(counts_path).read_text(encoding="utf-8"))
    beings = creatures(store)
    words = original_word(store)
    decided = {r["record"]: r for r in rows(decisions_path)}
    missing = sorted(set(beings) - set(decided))
    extra = sorted(set(decided) - set(beings))
    if missing or extra:
        sys.exit(f"decisions and records differ: missing {missing}, not creatures {extra}")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(TABLE_HEAD)
        f.write("\t".join(TABLE_COLUMNS) + "\n")
        for record in sorted(beings):
            d = decided[record]
            cells = [record, words.get(record, ""), beings[record], d["familiar"]]
            cells += [d.get(column) or "" for column in ["eng", "ukr", "deu", "spa", "rus", "why"]]
            cells += [evidence(counts[t].get(record)) for t in TEXTS]
            f.write("\t".join(c.replace("\t", " ") for c in cells) + "\n")


def lowered(name, language):
    return name if language == "deu" or not name else name[0].lower() + name[1:]


def first_put(line, name, language):
    """The line with the original name first, less a lead that only says the name the record now has."""
    lowered_line = line.lower()
    for article in [""] + ARTICLES[language]:
        lead = (article + name).lower()
        if lowered_line.startswith(lead) and lowered_line[len(lead):len(lead) + 2] in (": ", ", "):
            return line[len(lead) + 2:]
    return line


def put_first(original, line, name, language):
    return f"{lowered(original, language)} · {first_put(line, name, language)}" if line else lowered(original, language)


def unescape(cell):
    return re.sub(r"\\(.)", lambda m: {"\\": "\\", "t": "\t", "n": "\n"}.get(m.group(1), m.group(0)), cell)


def escape(values):
    return json.dumps(values, ensure_ascii=False).replace("\\", "\\\\")


def moved_products(d):
    """The record files whose earlier put of the record these rows take over, so the two never alternate."""
    products = {"records/records", "records/i18n/eng/records"} | {f"records/i18n/{lang}/lines" for lang in LINES}
    return products | {f"records/i18n/{lang}/name-forms" for lang in NAMED if d.get(lang)}


def take_over(folder, out, decided):
    """
    The earlier puts of the rows these decisions rewrite (the files that made the records), taken out of their files
    and returned by (product, record): a put left there would put the old name back on every run before this file
    puts the new one, and owner-rows would never read as done.
    """
    wanted = {d["record"]: moved_products(d) for d in decided}
    taken = {}
    for path in sorted(Path(folder).glob("*.records.tsv")):
        if path.resolve() == Path(out).resolve():
            continue
        with open(path, encoding="utf-8", newline="") as f:
            lines = f.read().splitlines(keepends=True)
        kept = []
        for line in lines:
            parts = line.rstrip("\r\n").split("\t")
            if line.startswith("#") or len(parts) < 6 or parts[3] not in wanted or parts[1] not in wanted[parts[3]]:
                kept.append(line)
                continue
            values = json.loads(unescape(parts[4]))
            if parts[1].endswith("/name-forms") and values.get("grammatical_case") != "nominative":
                kept.append(line)
                continue
            if parts[2] != "put" or (parts[1], parts[3]) in taken:
                sys.exit(f"{path.name}: {parts[1]} {parts[3]} is not one put; take it over by hand")
            taken[(parts[1], parts[3])] = values
        if len(kept) != len(lines):
            with open(path, "w", encoding="utf-8", newline="") as f:
                f.write("".join(kept))
            print(f"{path.name}: {len(lines) - len(kept)} rows taken over", file=sys.stderr)
    return taken


def owner_rows(store, table_path, out):
    beings = creatures(store)
    english = {r["slug"]: r for r in rows(store / "records" / "i18n" / "eng" / "records.tsv")}
    nominative = {lang: {r["slug"]: r["form"] for r in rows(store / "records" / "i18n" / lang / "name-forms.tsv")
                         if r["grammatical_case"] == "nominative"} for lang in NAMED}
    other_cases = {lang: {r["slug"] for r in rows(store / "records" / "i18n" / lang / "name-forms.tsv")
                          if r["grammatical_case"] != "nominative"} for lang in NAMED + ["eng"]}
    lines = {lang: {r["slug"]: r for r in rows(store / "records" / "i18n" / lang / "lines.tsv")} for lang in LINES}
    decided = [r for r in rows(table_path) if r["familiar"] == "yes"]
    for d in decided:
        if d["record"] not in beings:
            sys.exit(f"{d['record']}: no creature record")
        for lang in NAMED + ["eng"]:
            if d["record"] in other_cases[lang]:
                sys.exit(f"{d['record']}: has {lang} name forms in other cases, which these rows do not rewrite")
    taken = take_over(Path(out).parent, out, decided)
    out_rows = []

    def row(product, record, values, why, amend=None):
        earlier = taken.get((product, record))
        if earlier is not None:
            out_rows.append([ENTRY, product, "put", record, escape({**earlier, **values}), why])
        else:
            out_rows.append([ENTRY, product, amend or "put", record, escape(values), why])

    for d in decided:
        record = d["record"]
        evidence_note = "; ".join(f"{t} {d[t]}" for t in TEXTS if d.get(t))
        row("records/records", record, {"name": d["eng"]},
            f"the English name the Berean Standard Bible renders the word by most, the King James agreeing on the animal "
            f"({evidence_note})", amend="amend")
        old_english = beings[record]
        line = (english.get(record) or {}).get("line")
        new_line = put_first(old_english, line, d["eng"], "eng")
        row("records/i18n/eng/records", record, {"line": new_line},
            f"the original name {old_english} first in the line, now that the record is called {d['eng']}",
            amend="amend" if record in english else "put")
        for lang in NAMED:
            if d.get(lang):
                row(f"records/i18n/{lang}/name-forms", record,
                    {"grammatical_case": "nominative", "form": d[lang], "method": "manual"},
                    f"the name the {lang} reader language's text renders the word by most, in its dictionary form; "
                    f"until now {nominative[lang].get(record)}")
        for lang in LINES:
            held = lines[lang].get(record)
            if held is None:
                continue
            original = nominative[lang].get(record) or old_english
            name = d.get(lang)
            rendered = put_first(original, held["line"], name, lang) if name else held["line"]
            by = held["by"] + ", its original name put first on 2026-11-24" if name else held["by"]
            row(f"records/i18n/{lang}/lines", record, {"line": rendered, "english": new_line, "by": by},
                f"the original name {original} first in the {lang} line, now that the record is called {name}"
                if name else "the line as it was, rendering the English line as it now reads")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("# producer   by hand: familiar creatures named in each reader language as its translation renders them, "
                "their original name first in the line (PRB-1992), written by scripts/creature-names.py rows from "
                "Resources/Essenthos/creature-names/creature-names.tsv, applied by forge owner-rows. The rows of the files "
                "that made these records which these rows rewrite were taken out of them and are put here whole.\n")
        f.write(f"# by         {BY}\n")
        f.write("# One row per row of a file the build reads, applied by forge owner-rows; 'why' is the reading the row rests on.\n")
        f.write("entry\tproduct\top\trecord\tvalues\twhy\n")
        for r in out_rows:
            f.write("\t".join(r) + "\n")
    print(f"{len(decided)} familiar records, {len(out_rows)} rows", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    c = sub.add_parser("count")
    c.add_argument("store", type=Path)
    c.add_argument("out")
    c.add_argument("--essx", type=Path, default=Path(__file__).resolve().parents[1] / ".run" / "essx" / "essx.exe")
    t = sub.add_parser("table")
    t.add_argument("store", type=Path)
    t.add_argument("counts")
    t.add_argument("decisions")
    t.add_argument("out")
    r = sub.add_parser("rows")
    r.add_argument("store", type=Path)
    r.add_argument("table")
    r.add_argument("out")
    a = parser.parse_args()
    if a.command == "count":
        count(a.store, a.out, a.essx)
    elif a.command == "table":
        table(a.store, a.counts, a.decisions, a.out)
    else:
        owner_rows(a.store, a.table, a.out)


if __name__ == "__main__":
    main()
