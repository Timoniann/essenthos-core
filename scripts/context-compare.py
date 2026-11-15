"""Compare /v1/context and its family panel between two APIs on one database, byte for byte, over all of Genesis, Matthew,
Esther and Tobit in six pairs of languages, open texts and canon modes; then time 20 chapters and count their statements
(Server-Timing). usage: context-compare.py <before api> <after api> <report.txt>  (TSK-1669: before = the composing code,
after = the stored chapter context)"""
import json, re, statistics, sys, time, urllib.request, urllib.error

BEFORE = sys.argv[1].rstrip("/")
AFTER = sys.argv[2].rstrip("/")
out = sys.argv[3]


def get(base, path):
    req = urllib.request.Request(base + path, headers={"Accept": "application/json"})
    t = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            body = r.read(); status = r.status; timing = r.headers.get("Server-Timing", "")
    except urllib.error.HTTPError as e:
        body = e.read(); status = e.code; timing = e.headers.get("Server-Timing", "")
    ms = (time.perf_counter() - t) * 1000
    m = re.search(r'total;dur=([\d.]+).*?desc="(\d+) statements"', timing)
    return status, body, (float(m.group(1)) if m else ms), (int(m.group(2)) if m else -1)


combos = {
    "eng": "language=eng&prose=eng",
    "eng-kjv-protestant": "language=eng&prose=eng&texts=KJV&canon=protestant",
    "ukr-ubio-protestant": "language=ukr&prose=ukr&texts=UBIO&canon=protestant",
    "rus-ukr-rusv-orthodox": "language=rus&prose=ukr&texts=RUSV&canon=orthodox",
    "deu-luth": "language=deu&prose=deu&texts=LUTH1912",
    "spa-rv-catholic": "language=spa&prose=spa&texts=RV1909&canon=catholic",
}
books = {"genesis": None, "matthew": None, "esther": None, "tobit": None}

report = open(out, "w", encoding="utf-8")
def pct(values, p):
    values = sorted(values)
    return values[min(len(values) - 1, int(round(p / 100 * (len(values) - 1))))]


timed = [("genesis", 1), ("genesis", 5), ("genesis", 10), ("genesis", 12), ("genesis", 36), ("genesis", 46), ("exodus", 25),
         ("numbers", 26), ("joshua", 15), ("ruth", 4), ("1-samuel", 17), ("1-kings", 12), ("1-chronicles", 1),
         ("1-chronicles", 6), ("esther", 1), ("psalms", 119), ("isaiah", 53), ("matthew", 1), ("luke", 3), ("acts", 13)]
query = combos["eng-kjv-protestant"]
rows = {"before": {"first": [], "warm": [], "stmts": [], "bytes": []}, "after": {"first": [], "warm": [], "stmts": [], "bytes": []}}
report.write("\nchapter\tbefore first ms\tbefore warm ms\tbefore stmts\tafter first ms\tafter warm ms\tafter stmts\tbytes\n")
for book, chapter in timed:
    path = f"/v1/context/{book}/{chapter}?{query}"
    fa = get(AFTER, path); fb = get(BEFORE, path)
    wa = [get(AFTER, path) for _ in range(5)]
    wb = [get(BEFORE, path) for _ in range(5)]
    eq = fa[1] == fb[1]
    for side, first, warm in (("before", fb, wb), ("after", fa, wa)):
        rows[side]["first"].append(first[2]); rows[side]["warm"].extend(w[2] for w in warm)
        rows[side]["stmts"].append(warm[-1][3]); rows[side]["bytes"].append(len(first[1]))
    report.write(f"{book} {chapter}\t{fb[2]:.0f}\t{statistics.median(w[2] for w in wb):.1f}\t{wb[-1][3]}\t{fa[2]:.0f}\t"
                 f"{statistics.median(w[2] for w in wa):.1f}\t{wa[-1][3]}\t{len(fa[1])}{'' if eq else ' DIFF'}\n")


for side in ("before", "after"):
    r = rows[side]
    report.write(f"{side}: first request p50 {pct(r['first'], 50):.0f} ms p95 {pct(r['first'], 95):.0f} ms; "
                 f"warm p50 {pct(r['warm'], 50):.1f} ms p95 {pct(r['warm'], 95):.1f} ms; "
                 f"statements p50 {pct(r['stmts'], 50)} max {max(r['stmts'])}\n")
report.flush()

same = differ = 0
for book in books:
    chapter = 1
    while True:
        status = None
        for name, query in combos.items():
            path = f"/v1/context/{book}/{chapter}?{query}"
            b = get(BEFORE, path); a = get(AFTER, path)
            status = b[0]
            if b[0] == 404 and a[0] == 404:
                break
            eq = b[0] == a[0] and b[1] == a[1]
            same += eq; differ += (not eq)
            if not eq:
                report.write(f"DIFF\t{path}\tbefore {b[0]} {len(b[1])} B\tafter {a[0]} {len(a[1])} B\n")
                open(out + ".diff-" + re.sub(r'[^a-z0-9]+', '_', path)[:90], "wb").write(b[1] + b"\n=====\n" + a[1])
        if status == 404:
            break
        if book in ("genesis", "matthew"):
            path = f"/v1/context/{book}/{chapter}/family?language=eng&prose=eng"
            b = get(BEFORE, path); a = get(AFTER, path)
            eq = b[0] == a[0] and b[1] == a[1]
            same += eq; differ += (not eq)
            if not eq:
                report.write(f"DIFF\t{path}\n")
        books[book] = chapter
        chapter += 1
report.write(f"byte-equal: {same} same, {differ} different; chapters {books}\n")
report.flush()

report.close()
print(open(out, encoding="utf-8").read()[-3000:])
