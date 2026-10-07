"""Fetch the Ge'ez, Brenton Greek and Brenton English text of Job, Psalms and Song from the local core API.

Read-only GETs against the reader's own endpoint, paced, one chapter file per chapter. Rerunning skips what exists.
"""
import json, sys, time, urllib.request
from pathlib import Path

API = "http://localhost:5279/v1/text"
HERE = Path(__file__).parent
OUT = HERE / "chapters"
OUT.mkdir(exist_ok=True)
BOOKS = {"job": 42, "psalms": 151, "song-of-solomon": 8}
TEXTS = ["GEEZ81", "GRCBRENT", "BRENTON"]


def verses(corpus, book, chapter):
    with urllib.request.urlopen(f"{API}/{corpus}/{book}/{chapter}", timeout=60) as r:
        d = json.load(r)
    return [{"n": v["number"], "t": "".join(w["text"] + (w.get("trailer") or "") for w in v["words"]).strip()}
            for v in d.get("verses", [])]


only = sys.argv[1:]  # e.g. psalms:22 job:3
targets = [(b, c) for b, n in BOOKS.items() for c in range(1, n + 1)]
if only:
    targets = [(s.split(":")[0], int(s.split(":")[1])) for s in only]
for book, ch in targets:
    f = OUT / f"{book}-{ch:03d}.json"
    if f.exists():
        continue
    data = {"book": book, "chapter": ch, **{t: verses(t, book, ch) for t in TEXTS}}
    f.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    print(book, ch, {t: len(data[t]) for t in TEXTS})
    time.sleep(0.2)
