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
edit('achsah', decide('daughter-of', 'caleb', 'JOS 15:16',
    "JOS 15:16-17 and JDG 1:12-13: Caleb the son of Jephunneh gives his daughter Achsah to Othniel; 1CH 2:49 names "
    "Achsah the daughter of Caleb in the line of Hezron, whose son Caleb is Chelubai (1CH 2:9, 2:18). Many commentators "
    "take the two as one man, Jephunneh's Caleb counted into Hezron's clan; others hold they are two. Both statements "
    "stand as the text gives them.", 'JOS 15:16, JDG 1:12 and 1CH 2:49'))
edit('ner', decide('son-of', 'abiel', '1SA 14:51',
    "1SA 14:51: 'Ner the father of Abner was the son of Abiel'; 1CH 9:35-36 lists Ner among the sons of Jehiel (Jeiel) "
    "the father of Gibeon. Either one of the two is a forebear further up, or Abiel and Jeiel are one man under two "
    "names. Both statements stand as the text gives them.", '1SA 14:51 and 1CH 9:35-36'))
edit('meshuallam', decide('son-of', 'hodaviah-4', '1CH 9:7',
    "1CH 9:7: 'Sallu the son of Meshullam, the son of Hodaviah, the son of Hasenuah'; NEH 11:7 makes Meshullam the son "
    "of Joed in its own list of the Benjamites who dwelt in Jerusalem. The two lists give different lines, whether two "
    "records of one family or of different men. Both statements stand as the text gives them.", '1CH 9:7 and NEH 11:7'))
