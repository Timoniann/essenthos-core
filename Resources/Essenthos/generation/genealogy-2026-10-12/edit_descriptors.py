"""Edits the effective descriptor record of each entity in place, one line per record, nothing else."""
import json, os, sys

D = sys.argv[1]
AGENT = "an agent reading {ref} on the project owner's instruction, decided 2026-10-12"


def effective():
    found = {}
    for f in sorted(os.listdir(D)):
        if f.endswith('.jsonl'):
            with open(os.path.join(D, f), encoding='utf-8') as fh:
                for i, line in enumerate(fh):
                    if line.strip():
                        found[json.loads(line)['entity']] = (f, i)
    return found


def edit(slug, change):
    f, i = where[slug]
    path = os.path.join(D, f)
    with open(path, encoding='utf-8', newline='') as fh:
        lines = fh.read().split('\n')
    record = json.loads(lines[i])
    before = json.dumps(record, ensure_ascii=False)
    change(record)
    after = json.dumps(record, ensure_ascii=False)
    assert before != after, slug
    lines[i] = after
    with open(path, 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(lines))
    print('edited', slug, 'in', f)


def drop(relation, target, reference):
    def change(record):
        kept = [c for c in record['claims'] if not (c['relation'] == relation and c['target'] == target and c['reference'] == reference)]
        assert len(kept) == len(record['claims']) - 1, (relation, target, reference)
        record['claims'] = kept
    return change


def replace(relation, target, reference, new):
    def change(record):
        hits = [c for c in record['claims'] if c['relation'] == relation and c['target'] == target and c['reference'] == reference]
        assert len(hits) == 1, (relation, target, reference)
        index = record['claims'].index(hits[0])
        record['claims'][index] = new
    return change


def decide(relation, target, reference, reason, ref_read):
    def change(record):
        hits = [c for c in record['claims'] if c['relation'] == relation and c['target'] == target and c['reference'] == reference]
        assert len(hits) == 1, (relation, target, reference)
        c = hits[0]
        c['confidence'] = None
        c['reason'] = reason
        c['decidedBy'] = AGENT.format(ref=ref_read)
    return change


where = effective()

# 1 Chronicles 6:22-23 lists Korah's three sons and then Ebiasaph's son Assir; it is not a chain.
edit('elkanah', drop('son-of', 'assir', '1CH 6:23'))
edit('abiasaph', drop('son-of', 'elkanah', '1CH 6:23'))

# 1 Chronicles 7:12's "Shuppim ... the children of Ir" does not name Muppim or Iri.
edit('iri', drop('father-of', 'muppim', '1CH 7:12'))

# Maachah daughter of Abishalom is Abijam's mother (1KI 15:2) and Asa's grandmother, the queen mother.
edit('maacah-3', replace('mother-of', 'asa', '2CH 15:16', {
    'relation': 'grandmother-of', 'target': 'asa', 'reference': '1KI 15:2; 1KI 15:10', 'confidence': None,
    'reason': "1KI 15:2 makes Maachah the daughter of Abishalom Abijam's mother, and 15:8 Asa Abijam's son; "
              "15:10 calls the same Maachah daughter of Abishalom Asa's mother, and 15:13 says Asa removed her "
              "from being queen: she was his grandmother and the queen mother. The clause first read 2CH 15:16 "
              "as 'mother of Asa' and was corrected on the project owner's word of 2026-10-12.",
    'decidedBy': AGENT.format(ref='1KI 15:2-13'),
}))
edit('asa', replace('son-of', 'maacah-3', '1KI 15:13', {
    'relation': 'grandson-of', 'target': 'maacah-3', 'reference': '1KI 15:8; 1KI 15:10', 'confidence': None,
    'reason': "1KI 15:8 makes Asa the son of Abijam, and 15:2 Maachah the daughter of Abishalom Abijam's "
              "mother; the 'mother' of 15:10 and 15:13 is the queen mother, his grandmother. The clause first "
              "read 1KI 15:13 as 'son of Maachah' and was corrected on the project owner's word of 2026-10-12.",
    'decidedBy': AGENT.format(ref='1KI 15:2-13'),
}))

# Two fathers the text itself gives, each stated on purpose.
edit('jacob-2', decide('father-of', 'joseph-6', 'MAT 1:16',
    "MAT 1:16: 'Jacob begat Joseph the husband of Mary'. Luke 3:23 calls Joseph the son of Heli; both "
    "statements stand as the two Gospels give them.", 'MAT 1:16 and LUK 3:23'))
edit('eli-2', decide('father-of', 'joseph-6', 'LUK 3:23',
    "LUK 3:23: 'Joseph, which was the son of Heli'. Matthew 1:16 says Jacob begat Joseph; both statements "
    "stand as the two Gospels give them.", 'MAT 1:16 and LUK 3:23'))
edit('zerubbabel-2', decide('son-of', 'shealtiel-2', 'EZR 3:2',
    "EZR 3:2: 'Zerubbabel the son of Shealtiel', as Haggai 1:1 and Matthew 1:12 also call him; 1 Chronicles "
    "3:19 names Pedaiah, Shealtiel's brother, as his father. Both statements stand as the text gives them.",
    'EZR 3:2 and 1CH 3:17-19'))
