"""Writes Ottley's page table, Essenthos.Forge/Swete/OttleyPage.json, which the converter applies after the
transcription's own repairs (OttleyIsaiah.Transcription). Rerun it whenever accepted.json grows (accept.py after a
second reading); nothing in code changes.

In: settled.json (corrections read on the scan by hand: batch Q's twelve, round 1, and the judge's third readings,
looked at again), accepted.json (Codex's corrections judged right), places.json (which occurrence an ambiguous
correction means), transcribed.txt (the converter's tokens after the transcription's repairs — the C# test
OttleyPageTests checks it is current), and the table already written (an entry keeps the round it first came in).

Each Codex correction gives `ours` as our text read back from live: words with their punctuation. It is placed in the
verse as the entries before it leave it:
- found once token for token: taken as it is;
- found once by the words alone (Codex quoted a word without the stop the token carries): the tokens are taken, and the
  printed words keep our punctuation where Codex said nothing about it, except the stop or comma the transcription read
  out of a grave accent (τὸ. for τὰ, διὸ, for διὰ), which is not on the page;
- found more than once: places.json names the occurrence, and words either side are added until it is found once;
- not found: reported and left out.
`same` pairs the tokens an entry puts right letter by letter, so a corpus holding the verse keeps those words' rows
and what stands on them; a hand entry says `"sameWord": false` where none of its words is the same word.
Out: OttleyPage.json and a report on stdout."""
import json, re, sys, unicodedata
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).parent
TABLE = HERE.parents[3] / "Essenthos.Forge" / "Swete" / "OttleyPage.json"
PUNCT = ".,·;:!?()<>[]"


def load(name, default=None):
    path = HERE / name
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else default


def core(token):
    return token.strip(PUNCT)


def bare(s):
    return "".join(ch for ch in unicodedata.normalize("NFD", s) if not unicodedata.combining(ch)).lower().replace("ς", "σ")


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        prev, row[0] = row[0], i
        for j, y in enumerate(b, 1):
            prev, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, prev + (x != y))
    return row[-1]


def similar(x, y):
    """Two readings of one word: the letters differ by no more than a third of the longer."""
    x, y = bare(core(x)), bare(core(y))
    return x == y or distance(x, y) <= max(1, max(len(x), len(y)) // 3)


def same_words(digitised, printed):
    """
    The words an entry puts right letter by letter, as pairs of its digitised and printed tokens: word for
    word where it prints as many words as it replaces, otherwise the longest run of words read alike in
    order, the same word preferred to a similar one. A word the page prints as another word altogether
    (Κύριος where it prints Ἰδοὺ) is not one of them.
    """
    d, p = digitised.split(), printed.split()
    if len(d) == len(p):
        # word for word: the word standing in the other's place, unless its letters are mostly others
        return [[x, y] for x, y in zip(d, p) if core(x) != core(y) and
                distance(bare(core(x)), bare(core(y))) <= max(2, max(len(core(x)), len(core(y))) // 2)]
    score = [[0] * (len(p) + 1) for _ in range(len(d) + 1)]
    for i in range(len(d) - 1, -1, -1):
        for j in range(len(p) - 1, -1, -1):
            pair = 2 if core(d[i]) == core(p[j]) else 1 if similar(d[i], p[j]) else 0
            score[i][j] = max(score[i + 1][j], score[i][j + 1], score[i + 1][j + 1] + pair if pair else 0)
    pairs, i, j = [], 0, 0
    while i < len(d) and j < len(p):
        pair = 2 if core(d[i]) == core(p[j]) else 1 if similar(d[i], p[j]) else 0
        if pair and score[i][j] == score[i + 1][j + 1] + pair:
            if core(d[i]) != core(p[j]):
                pairs.append([d[i], p[j]])
            i, j = i + 1, j + 1
        elif score[i][j] == score[i + 1][j]:
            i += 1
        else:
            j += 1
    return pairs


def find(tokens, words, key=lambda t: t):
    return [i for i in range(len(tokens) - len(words) + 1)
            if [key(t) for t in tokens[i:i + len(words)]] == [key(w) for w in words]]


def split(token):
    body = token.strip(PUNCT)
    start = token.index(body) if body else len(token)
    return token[:start], body, token[start + len(body):]


def stray(token, word):
    """The stop or comma the transcription read out of a grave accent: τὸ. or διὸ, where the page prints τὰ, διὰ."""
    _, body, trail = split(token)
    return trail in (".", ",") and body.endswith(("ὸ", "ό")) and core(word).endswith(("ὰ", "ά"))


def punctuate(ours, tokens, printed):
    """The printed words with our punctuation where Codex quoted a word without it and said nothing about it."""
    printed = list(printed)
    if not printed:
        return printed
    if len(ours) == len(printed):
        for k, (quoted, token) in enumerate(zip(ours, tokens)):
            q_lead, _, q_trail = split(quoted)
            lead, _, trail = split(token)
            p_lead, body, p_trail = split(printed[k])
            if (p_lead, p_trail) != (q_lead, q_trail):
                continue
            printed[k] = (lead if p_lead == q_lead else p_lead) + body + ("" if stray(token, printed[k]) else trail)
        return printed
    q_lead, _, _ = split(ours[0])
    lead, _, _ = split(tokens[0])
    p_lead, body, p_trail = split(printed[0])
    if p_lead == q_lead:
        printed[0] = lead + body + p_trail
    _, _, q_trail = split(ours[-1])
    _, _, trail = split(tokens[-1])
    p_lead, body, p_trail = split(printed[-1])
    if p_trail == q_trail and not stray(tokens[-1], printed[-1]):
        printed[-1] = p_lead + body + trail
    return printed


verses = {}
for line in (HERE / "transcribed.txt").read_text(encoding="utf-8").splitlines():
    ref, token = line.split(" ", 1)
    _, chapter, verse = ref.split(".")
    verses.setdefault((int(chapter), verse), []).append(token)

previous = {e["source"]: e["round"] for e in json.loads(TABLE.read_text(encoding="utf-8"))} if TABLE.exists() else {}
settled = load("settled.json")
accepted = load("accepted.json")
places = {int(k): v for k, v in load("places.json", {}).items()}
covered = {i for s in settled for i in s.get("settles", [])}
# a correction first written now comes in a round of its own, after every round a corpus may already hold
next_round = max(previous.values()) + 1 if previous else max(s["round"] for s in settled)

candidates = []
for n, s in enumerate(settled):
    source = f"hand-{s['round']}-{n}" if not s.get("settles") else f"codex-{s['settles'][0]}"
    candidates.append({**s, "source": source, "hand": True})
for a in accepted:
    if a["id"] in covered:
        continue
    source = f"codex-{a['id']}"
    chapter, verse = a["verse"].split(":")
    candidates.append({"round": previous.get(source, next_round), "chapter": int(chapter), "verse": verse,
                       "ours": a["ours"], "printed": a["printed"], "page": int(a["page"]),
                       "leaf": int(re.search(r"-(\d+)\.jpg$", a["image"]).group(1)),
                       "what": f"{a['why'].rstrip('.')} (read by Codex, {'confirmed by Opus' if a['checked'] == 'codex+opus' else 'twice'})",
                       "source": source, "id": a["id"], "hand": False})


def order(c):
    return c["round"], c["chapter"], int(re.match(r"\d+", c["verse"]).group()), c.get("id", -1)


report = {"as given": 0, "punctuation from our text": 0, "accent stop dropped": 0, "context added": 0}
unplaced, table = [], []
for c in sorted(candidates, key=order):
    tokens = verses.get((c["chapter"], c["verse"]))
    where = f"{c['chapter']}:{c['verse']} {c['source']}"
    if tokens is None:
        unplaced.append(f"{where}: no such verse")
        continue
    if c["hand"]:
        at = find(tokens, c["digitised"].split())
        if len(at) != 1:
            unplaced.append(f"{where}: hand entry \"{c['digitised']}\" found {len(at)} times")
            continue
        digitised, printed = c["digitised"].split(), c["printed"].split()
    else:
        ours, printed = c["ours"].split(), c["printed"].split()
        at = find(tokens, ours)
        how = "as given"
        if len(at) != 1:
            at = find(tokens, ours, core)
            how = "punctuation from our text"
        if len(at) > 1 and c["id"] in places:
            at = [at[places[c["id"]] - 1]]
            how = "context added"
        if len(at) != 1:
            unplaced.append(f"{where}: \"{c['ours']}\" found {len(at)} times in \"{' '.join(tokens)}\"")
            continue
        digitised = tokens[at[0]:at[0] + len(ours)]
        if how != "as given":
            printed = punctuate(ours, digitised, printed)
            if any(quoted != token and stray(token, word)
                   for quoted, token, word in zip(ours, digitised, c["printed"].split())):
                how = "accent stop dropped"
        if how == "context added":
            # widen until the tokens stand once in the verse
            start, end = at[0], at[0] + len(digitised)
            while len(find(tokens, tokens[start:end])) > 1:
                if start > 0:
                    start -= 1
                    printed = [tokens[start], *printed]
                if len(find(tokens, tokens[start:end])) > 1 and end < len(tokens):
                    printed = [*printed, tokens[end]]
                    end += 1
            digitised = tokens[start:end]
        report[how] += 1
    entry = {"chapter": c["chapter"], "verse": c["verse"], "digitised": " ".join(digitised), "printed": " ".join(printed),
             "page": c["page"], "leaf": c["leaf"], "what": c["what"],
             "same": same_words(" ".join(digitised), " ".join(printed)) if c.get("sameWord", True) else [],
             "round": c["round"], "source": c["source"]}
    if entry["digitised"] == entry["printed"]:
        unplaced.append(f"{where}: reads as printed already")
        continue
    # apply it, as the converter will, so the next entry is placed in the verse as this one leaves it
    i = find(tokens, digitised)
    assert len(i) == 1, where
    verses[(c["chapter"], c["verse"])] = tokens[:i[0]] + printed + tokens[i[0] + len(digitised):]
    table.append(entry)

TABLE.write_text(json.dumps(table, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(len(table), "entries written to", TABLE.name, "by round:",
      {r: sum(e["round"] == r for e in table) for r in sorted({e["round"] for e in table})})
print("Codex corrections placed:", report)
print(len(unplaced), "not placed:")
for u in unplaced:
    print(" ", u)
