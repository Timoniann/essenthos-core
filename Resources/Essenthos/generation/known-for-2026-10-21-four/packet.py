"""Writes the KJV packet for a known-for draft: every cited verse, read from the live API (read-only)."""
import json
import re
import sys
import urllib.parse
import urllib.request

draft_path, out_path = sys.argv[1:3]
names = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
draft = json.load(open(draft_path, encoding="utf-8"))

SLUG = {"GEN": "genesis", "EXO": "exodus", "MAT": "matthew", "MRK": "mark", "LUK": "luke", "JHN": "john",
        "ACT": "acts", "1JN": "1-john", "ROM": "romans", "HEB": "hebrews", "1CO": "1-corinthians", "1TI": "1-timothy",
        "DEU": "deuteronomy", "2CO": "2-corinthians"}
BOOKED = re.compile(r"^([1-4]?[A-Z]{2,3}) (.+)$")


def addresses(refs):
    book = None
    for piece in (p.strip() for p in refs.split(",")):
        m = BOOKED.match(piece)
        if m:
            book, piece = m.group(1), m.group(2)
        first, _, last = piece.partition("-")
        c1, v1 = map(int, first.split(":"))
        if not last:
            c2, v2 = c1, v1
        elif ":" in last:
            c2, v2 = map(int, last.split(":"))
        else:
            c2, v2 = c1, int(last)
        for c in range(c1, c2 + 1):
            lo = v1 if c == c1 else 1
            hi = v2 if c == c2 else 80
            for v in range(lo, hi + 1):
                yield book, c, v


def fetch(adds):
    out = {}
    adds = list(adds)
    for i in range(0, len(adds), 60):
        chunk = adds[i:i + 60]
        refs = ",".join(f"{SLUG[b]}:{c}:{v}" for b, c, v in chunk)
        url = "http://localhost:5279/v1/verses?corpus=KJV&refs=" + urllib.parse.quote(refs, safe=":,")
        data = json.load(urllib.request.urlopen(url))
        for item in data["items"]:
            code = next(k for k, s in SLUG.items() if s == item["book"]["slug"])
            out[(code, item["chapter"], item["verse"])] = item["text"]
    return out


lines = []
for slug, clauses in draft.items():
    lines.append(f"######## {slug} — {names.get(slug, slug)}\n")
    for n, (en, uk, refs) in enumerate(clauses, 1):
        lines.append(f"--- clause {n}: «{en}» ({refs})\n    uk: «{uk}»")
        adds = list(addresses(refs))
        text = fetch(adds)
        for a in adds:
            if a in text:
                lines.append(f"{a[0]} {a[1]}:{a[2]} {text[a]}")
        lines.append("")
open(out_path, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
print(sum(1 for l in lines if re.match(r"^[1-4]?[A-Z]{2,3} \d", l)), "verses")
