"""Apply the second reading's confirmed corrections (names checked against the corpus's own Ohienko, Luther 1912
and Reina-Valera 1909 text through /v1/text; corrections the corpus refuted are not applied). Prints every change."""
import json, re
from pathlib import Path

OUT = Path(__file__).parent / "out"
A = "’"  # the apostrophe the translations use

# (file or "*", language, old, new) — "*" applies to every file
FIXES = [
    ("*", "ukr", "Веселеїл", f"Бецал{A}їл"),             # Ohienko EXO 31:2 Бецал'їла
    ("*", "ukr", "Веселіїл", f"Бецал{A}їл"),
    ("*", "ukr", "з Вефілю", "з Бет-Елу"),               # Ohienko GEN 28:19 Бет-Ел
    ("*", "ukr", "у Вефілі", "в Бет-Елі"),
    ("*", "ukr", "Вефіль", "Бет-Ел"),
    ("*", "ukr", f"Навузар{A}адан", f"Невузар{A}адан"),  # Ohienko 2KI 25:8
    ("*", "ukr", "Агей", "Огій"),                        # Ohienko HAG 1:1 Огія
    ("*", "ukr", "Агея", "Огія"),
    ("ObjectRecords--altar-of-incense", "ukr", "Озія", "Уззійя"),  # Ohienko 2CH 26:19
    ("*", "ukr", "Юдиф", "Єгудиту"),                     # Ohienko GEN 26:34 Єгудиту
    ("*", "ukr", "Юдит", "Єгудит"),
    ("ReviewNoteRecords--abijah-3", "spa", "Abías es esa hija", "Abia es esa hija"),  # RV 1CH 2:24 Abia
    ("UnsettledRecordsThird--mahalath", "spa", "hija de Beer el heteo", "hija de Beeri el heteo"),  # RV GEN 26:34
    ("UnsettledRecords--jerioth", "spa",
     "Qué otra persona podría ser y por qué se enumera junto a este registro",
     "Quién más podría ser ella, y por qué, se indica junto a este registro"),
    ("NarrativeRecords--holy-spirit", "deu", "und Paulus verbot, das Wort", "und verbot dem Paulus, das Wort"),
    ("ObservanceRecords--appointed-times", "deu",
     "verwerfen sowohl Israels festgesetzte Feste als auch verheißen sie deren Wiederherstellung",
     "verwerfen Israels festgesetzte Feste und verheißen zugleich deren Wiederherstellung"),
    ("NarrativeRecords--nathanael", "ukr", "Біблійні дані", "BibleData"),
    ("NarrativeRecords--nathanael", "deu", "Bibeldaten", "BibleData"),
    ("NarrativeRecords--nathanael", "spa", "Datos bíblicos", "BibleData"),
]

changed = set()
for f in sorted(OUT.glob("*.json")):
    d = json.loads(f.read_text(encoding="utf-8"))
    name = f.stem
    for target, lang, old, new in FIXES:
        if target not in ("*", name) or old not in d[lang]:
            continue
        n = d[lang].count(old)
        d[lang] = d[lang].replace(old, new)
        changed.add(name)
        print(f"{name} {lang}: {old} -> {new} ({n})")
    if name in changed:
        d["corrected"] = "second reading by Claude (Anthropic), names checked against the corpus's Ohienko, Luther 1912 and Reina-Valera 1909"
        f.write_text(json.dumps(d, ensure_ascii=False, indent=1), encoding="utf-8")

unapplied = [x for x in FIXES if x[0] != "*" and x[0] not in changed]
print(len(changed), "files changed; targeted fixes that found nothing:", unapplied)
