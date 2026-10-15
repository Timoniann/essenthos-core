"""Paul's companions and the other ties the top records lacked, read verse by verse on the project
owner's instruction of 2026-10-14 (PRB-1044). Edits the effective descriptor record of each entity
in place, one line per record, nothing else.

usage: python edit_descriptors.py <Resources/Essenthos/descriptors>
"""
import json, os, sys

D = sys.argv[1]
AGENT = ("an agent (claude-opus-5-5) reading {ref} on the project owner's instruction of 2026-10-14, "
         "decided 2026-10-14")

PAUL = 'saul-2'

# (subject, relation, target, reference, reason, where): where is 'first' or 'last' for a clause
# added, or ('decide', <the reference the record cites now>) for a clause the record already holds.
CLAUSES = [
    # Paul's companions in Acts and the letters.
    ('joseph-12', 'companion-of', PAUL, 'ACT 13:2',
     "'Separate me Barnabas and Saul for the work whereunto I have called them' (ACT 13:2); they go out "
     "together through Cyprus, Pisidia and Lycaonia (ACT 13:4-14:28), go up to Jerusalem together "
     "(ACT 15:2; GAL 2:1) and part over John Mark (ACT 15:39).", ('decide', 'ACT 13:2')),
    ('silas', 'companion-of', PAUL, 'ACT 15:40',
     "'And Paul chose Silas, and departed' (ACT 15:40); they are beaten and imprisoned together at "
     "Philippi (ACT 16:19-25), and Silvanus writes with him to the Thessalonians (1TH 1:1).",
     ('decide', 'ACT 16:25')),
    ('luke', 'companion-of', PAUL, '2TI 4:11',
     "'Only Luke is with me' (2TI 4:11); Paul names him among 'my fellowlabourers' (PHM 1:24).", 'first'),
    ('john-5', 'companion-of', PAUL, 'ACT 12:25',
     "'Barnabas and Saul returned from Jerusalem ... and took with them John, whose surname was Mark' "
     "(ACT 12:25); 'they had also John to their minister' (ACT 13:5).", 'last'),
    ('titus', 'companion-of', PAUL, '2CO 8:23',
     "'Titus, he is my partner and fellowhelper concerning you' (2CO 8:23); Paul took him up to "
     "Jerusalem with Barnabas (GAL 2:1).", 'first'),
    ('tychicus', 'companion-of', PAUL, 'ACT 20:4',
     "'And there accompanied him into Asia ... Tychicus and Trophimus' (ACT 20:4); 'a faithful minister "
     "and fellowservant in the Lord' (COL 4:7).", 'first'),
    ('trophimus', 'companion-of', PAUL, 'ACT 21:29',
     "'They had seen before with him in the city Trophimus an Ephesian, whom they supposed that Paul had "
     "brought into the temple' (ACT 21:29); he accompanied Paul into Asia (ACT 20:4).", 'first'),
    ('epaphroditus', 'companion-of', PAUL, 'PHP 2:25',
     "'Epaphroditus, my brother, and companion in labour, and fellowsoldier' (PHP 2:25).", 'first'),
    ('priscilla', 'companion-of', PAUL, 'ACT 18:18',
     "Paul 'sailed thence into Syria, and with him Priscilla and Aquila' (ACT 18:18); he had lodged and "
     "worked with them at Corinth (ACT 18:2-3), and calls them 'my helpers in Christ Jesus' (ROM 16:3).",
     'last'),
    ('aquila', 'companion-of', PAUL, 'ACT 18:18',
     "Paul 'sailed thence into Syria, and with him Priscilla and Aquila' (ACT 18:18); he had lodged and "
     "worked with them at Corinth (ACT 18:2-3), and calls them 'my helpers in Christ Jesus' (ROM 16:3).",
     'last'),
    ('erastus', 'companion-of', PAUL, 'ACT 19:21-22',
     "Paul 'sent into Macedonia two of them that ministered unto him, Timotheus and Erastus' "
     "(ACT 19:21-22).", 'first'),
    ('erastus', 'companion-of', 'timothy', 'ACT 19:22',
     "'He sent into Macedonia two of them that ministered unto him, Timotheus and Erastus' (ACT 19:22).",
     ('decide', 'ACT 19:22')),
    ('gaius', 'companion-of', PAUL, 'ACT 19:29',
     "'Gaius and Aristarchus, men of Macedonia, Paul's companions in travel' (ACT 19:29).", 'first'),
    ('gaius-2', 'companion-of', PAUL, 'ACT 20:4',
     "'And there accompanied him into Asia ... Gaius of Derbe' (ACT 20:4).", 'first'),
    ('secundus', 'companion-of', PAUL, 'ACT 20:4',
     "'And there accompanied him into Asia ... of the Thessalonians, Aristarchus and Secundus' "
     "(ACT 20:4).", 'first'),
    ('sopater', 'companion-of', PAUL, 'ACT 20:4',
     "'And there accompanied him into Asia Sopater of Berea' (ACT 20:4).", 'first'),
    ('demas', 'companion-of', PAUL, 'PHM 1:24',
     "'Marcus, Aristarchus, Demas, Lucas, my fellowlabourers' (PHM 1:24); later 'Demas hath forsaken "
     "me' (2TI 4:10).", 'first'),
    ('onesimus', 'companion-of', PAUL, 'PHM 1:10; PHM 1:13',
     "'My son Onesimus, whom I have begotten in my bonds' (PHM 1:10), 'whom I would have retained with "
     "me, that in thy stead he might have ministered unto me in the bonds of the gospel' (PHM 1:13).",
     'first'),
    ('onesimus', 'companion-of', 'tychicus', 'COL 4:7-9',
     "Tychicus, 'whom I have sent unto you ... with Onesimus, a faithful and beloved brother' "
     "(COL 4:7-9).", 'last'),
    ('apollos', 'companion-of', PAUL, '1CO 3:6; 1CO 3:9',
     "'I have planted, Apollos watered' (1CO 3:6); 'for we are labourers together with God' (1CO 3:9).",
     'last'),
    ('jesus-2', 'companion-of', PAUL, 'COL 4:11',
     "'And Jesus, which is called Justus ... These only are my fellowworkers unto the kingdom of God' "
     "(COL 4:11).", 'first'),
    ('philemon', 'companion-of', PAUL, 'PHM 1:1',
     "'Paul, a prisoner of Jesus Christ, and Timothy our brother, unto Philemon our dearly beloved, and "
     "fellowlabourer' (PHM 1:1).", ('decide', 'PHM 1:1')),
    ('philemon', 'companion-of', 'timothy', 'PHM 1:1',
     "'Paul ... and Timothy our brother, unto Philemon our dearly beloved, and fellowlabourer' "
     "(PHM 1:1): the fellowlabourer of both who write.", ('decide', 'PHM 1:1')),
    ('archippus', 'companion-of', PAUL, 'PHM 1:2',
     "'Archippus our fellowsoldier' (PHM 1:2), in the letter of Paul and Timothy (PHM 1:1).", 'first'),
    ('clement', 'companion-of', PAUL, 'PHP 4:3',
     "'Those women which laboured with me in the gospel, with Clement also, and with other my "
     "fellowlabourers' (PHP 4:3).", 'first'),
    ('euodia', 'companion-of', PAUL, 'PHP 4:2; PHP 4:3',
     "'I beseech Euodias, and beseech Syntyche' (PHP 4:2); 'help those women which laboured with me in "
     "the gospel' (PHP 4:3).", 'first'),
    ('syntyche', 'companion-of', PAUL, 'PHP 4:2; PHP 4:3',
     "'I beseech Euodias, and beseech Syntyche' (PHP 4:2); 'help those women which laboured with me in "
     "the gospel' (PHP 4:3).", 'first'),
    ('andronicus', 'companion-of', PAUL, 'ROM 16:7',
     "'Salute Andronicus and Junia, my kinsmen, and my fellow-prisoners' (ROM 16:7).", 'first'),
    ('junias', 'companion-of', PAUL, 'ROM 16:7',
     "'Salute Andronicus and Junia, my kinsmen, and my fellow-prisoners' (ROM 16:7).", 'first'),
    ('urbanus', 'companion-of', PAUL, 'ROM 16:9',
     "'Salute Urbane, our helper in Christ' (ROM 16:9); the Greek calls him Paul's fellow worker.",
     'first'),
    ('tertius', 'scribe-of', PAUL, 'ROM 16:22',
     "'I Tertius, who wrote this epistle, salute you in the Lord' (ROM 16:22), in Paul's letter to the "
     "Romans (ROM 1:1).", 'first'),
    ('phoebe', 'supporter-of', PAUL, 'ROM 16:1; ROM 16:2',
     "'Phebe our sister ... she hath been a succourer of many, and of myself also' (ROM 16:1; ROM 16:2).",
     'first'),

    # Paul's companions with each other.
    ('silas', 'companion-of', 'timothy', 'ACT 18:5',
     "'Silas and Timotheus abode there still' (ACT 17:14); 'when Silas and Timotheus were come from "
     "Macedonia' (ACT 18:5).", ('decide', 'ACT 18:5')),
    ('timothy', 'companion-of', 'silas', 'ACT 17:14',
     "'Silas and Timotheus abode there still' (ACT 17:14); 'when Silas and Timotheus were come from "
     "Macedonia' (ACT 18:5).", ('decide', 'ACT 17:14')),

    # Paul himself.
    (PAUL, 'apostle-of', 'jesus', '1CO 1:1',
     "'Paul called to be an apostle of Jesus Christ through the will of God' (1CO 1:1); so also "
     "ROM 1:1 and GAL 1:1.", 'first'),
    ('gamaliel-2', 'teacher-of', PAUL, 'ACT 22:3',
     "Paul: 'brought up in this city at the feet of Gamaliel, and taught according to the perfect manner "
     "of the law of the fathers' (ACT 22:3).", 'first'),

    # The Twelve, and the apostles' companions.
    ('matthias', 'apostle-of', 'jesus', 'ACT 1:26',
     "'The lot fell upon Matthias; and he was numbered with the eleven apostles' (ACT 1:26).", 'first'),
    ('judas-of-james', 'apostle-of', 'jesus', 'LUK 6:13; LUK 6:16',
     "'Of them he chose twelve, whom also he named apostles' (LUK 6:13); among them 'Judas the brother of "
     "James' (LUK 6:16).", 'first'),
    ('john-2', 'companion-of', 'simon', 'ACT 3:1',
     "'Now Peter and John went up together into the temple at the hour of prayer' (ACT 3:1); they are "
     "sent together to Samaria (ACT 8:14).", ('decide', 'LUK 22:8')),
    ('andrew', 'disciple-of', 'john', 'JHN 1:35; JHN 1:40',
     "'John stood, and two of his disciples' (JHN 1:35); 'one of the two which heard John speak, and "
     "followed him, was Andrew' (JHN 1:40).", 'last'),
    ('mary-2', 'supporter-of', 'jesus', 'MAT 27:55-56',
     "'Many women ... which followed Jesus from Galilee, ministering unto him: among which was Mary "
     "Magdalene' (MAT 27:55-56); she ministered unto him of her substance (LUK 8:2-3).", 'first'),

    # Kin and covenant the records lacked.
    ('jacob', 'nephew-of', 'laban', 'GEN 28:5',
     "'Laban, son of Bethuel the Syrian, the brother of Rebekah, Jacob's and Esau's mother' (GEN 28:5); "
     "'Laban his mother's brother' (GEN 29:10).", 'last'),
    ('esau', 'nephew-of', 'laban', 'GEN 28:5',
     "'Laban, son of Bethuel the Syrian, the brother of Rebekah, Jacob's and Esau's mother' (GEN 28:5).",
     'last'),
    ('mattaniah', 'uncle-of', 'jehoiachin', '2KI 24:17',
     "'The king of Babylon made Mattaniah his father's brother king in his stead' (2KI 24:17), in the "
     "stead of Jehoiachin (2KI 24:15).", 'last'),
    ('jonathan-2', 'ally-of', 'david', '1SA 18:3',
     "'Then Jonathan and David made a covenant, because he loved him as his own soul' (1SA 18:3).",
     'last'),
    ('hiram', 'ally-of', 'solomon', '1KI 5:12',
     "'There was peace between Hiram and Solomon; and they two made a league together' (1KI 5:12).",
     'last'),
    ('joseph', 'servant-of', 'potiphar', 'GEN 39:1',
     "'Potiphar, an officer of Pharaoh ... bought him of the hands of the Ishmeelites' (GEN 39:1); 'he was "
     "in the house of his master the Egyptian' (GEN 39:2).", 'last'),
    ('caleb', 'companion-of', 'joshua', 'NUM 14:6',
     "'Joshua the son of Nun, and Caleb the son of Jephunneh, which were of them that searched the land, "
     "rent their clothes' (NUM 14:6), and the two spoke together against the evil report (NUM 14:7-9).",
     ('decide', 'NUM 14:6')),
]


def files(directory):
    paths = [os.path.join(r, f) for r, _, fs in os.walk(directory) for f in fs if f.endswith('.jsonl')]
    return sorted(paths, key=lambda p: p.replace('/', '\\'))


def effective(directory):
    found = {}
    for path in files(directory):
        with open(path, encoding='utf-8') as fh:
            for i, line in enumerate(fh):
                if line.strip():
                    found[json.loads(line)['entity']] = (path, i)
    return found


def edit(where, slug, change):
    path, i = where[slug]
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
    print('edited', slug, 'in', os.path.relpath(path, D))


def apply(subject, relation, target, reference, reason, where_to):
    decided = {
        'relation': relation, 'target': target, 'reference': reference, 'confidence': None,
        'reason': reason, 'decidedBy': AGENT.format(ref=reference),
    }

    def change(record):
        claims = record.setdefault('claims', None) or []
        record['claims'] = claims
        if isinstance(where_to, tuple):
            hits = [c for c in claims
                    if c['relation'] == relation and c['target'] == target and c['reference'] == where_to[1]]
            assert len(hits) == 1, (subject, relation, target, where_to)
            assert not hits[0].get('decidedBy'), (subject, relation, target, 'already decided')
            claims[claims.index(hits[0])] = decided
            return
        assert not any(c['relation'] == relation and c['target'] == target for c in claims), \
            (subject, relation, target, 'already stated')
        if where_to == 'first':
            claims.insert(0, decided)
        else:
            claims.append(decided)

    return change


if __name__ == '__main__':
    where = effective(D)
    for clause in CLAUSES:
        edit(where, clause[0], apply(*clause))
