"""
Two sources agreed on 8,092 of these words. Ask a third whether they are both wrong.

`sense.py` measured a model's reading against the encyclopedia's verse lists and reported 99%
agreement. Agreement is not proof. It is two witnesses saying the same thing, and it is weakest
exactly where both can fail the same way: the encyclopedia holds one man under two records and both
witnesses pick the same one of the two; the encyclopedia does not hold the referent at all and both
fall back on the nearest listed candidate; every candidate under the name is wrong and the agreement
is agreement about the wrong set.

So this is not a re-run of the original question. It is a cheaper one, asked of a stronger model:
here is the word, its verse, the referent both sources name, and every other candidate -- is that
right? The answers are `agree`, `doubt` with a reason, or `wrong` with what it should be instead.

    python scripts/sense_audit.py plan    --answers .sense/source-answers --out .sense/audit
    python scripts/sense_audit.py records --dir .sense/audit
    python scripts/sense_audit.py extract --dir .sense/audit --stratum 1
    python scripts/sense_audit.py ask     --dir .sense/audit --stratum 1
    python scripts/sense_audit.py report  --dir .sense/audit

The extraction is `sense.py`'s, imported rather than copied: the same occurrence rows, the same
candidate rows, the same lexicon, the same rule that a comma-joined Strong label is the words of a
title and not a name. What differs is the question and the population.

**The population is ordered by risk, not sampled uniformly.** These failures are systematic, so a
uniform sample would spend most of its budget on the safe middle. Four strata, dangerous first:

    1  the agreed record shares its name with another that looks like the same person -- the same
       father, the same verse in the distinguisher, or verses attested to both
    2  names with five or more candidates, and occurrences the first reading called medium or low
    3  the kind is in tension -- BHSA marks the word as a place and the record is a person, or the
       name is held as both a person and a place and the choice between them was never checked
    4  everything else

A stratum's numbers are written as soon as it finishes, because a rate found in the first stratum
changes what the owner wants done about the fourth.

**A third opinion is not a verdict.** `doubt` says the evidence is thinner than two agreeing sources
imply; it does not say the answer is wrong. The report keeps the two apart and quotes the reason for
every one of them, because a reader has to be able to judge the judgement.

Alongside the occurrence audit, `records` asks a different question of the encyclopedia itself: of
the records that look alike, which are one person written twice? That is worth as much as a wrong
referent and is easier to act on -- a duplicate cannot be fixed by choosing better between the two.

A run leaves behind, under its own directory:

    strata.json              every agreement, its stratum and why it is in it
    batches/<n>/*.json       the prompt payloads, exactly as the model saw them
    verdicts-<n>.jsonl       one row per occurrence, appended, re-runnable batch by batch
    records.jsonl            the duplicate-record adjudications
    audit.md, audit.json     the report
"""

import argparse
import collections
import datetime
import difflib
import glob
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

import sense

PROMPT_VERSION = 'audit-1'
RECORD_PROMPT_VERSION = 'records-1'

# The verdict is cheap to say and the reason is not, so a batch here carries more occurrences than a
# sense.py batch did: the model writes a handful of words per agreement and a sentence only where it
# dissents. The name budget is what keeps the shared half -- lexicon and records -- amortised.
BATCH_NUMBERS = 8
BATCH_OCCURRENCES = 40

# Candidate counts at or above this are the Zechariah class, where the risk is that the list is long
# enough for two readers to land on the same wrong entry for the same reason.
MANY_CANDIDATES = 5

# How alike two names have to look before their records are worth comparing at all. Abiasaph and
# Ebiasaph are one man under two spellings; Jehucal and Jucal are another.
NAME_SIMILARITY = 0.8

VERDICT_AGREE = 'agree'
VERDICT_DOUBT = 'doubt'
VERDICT_WRONG = 'wrong'
VERDICTS = (VERDICT_AGREE, VERDICT_DOUBT, VERDICT_WRONG)

STRATA = {
    1: 'the agreed record looks like a duplicate of another candidate',
    2: 'five or more candidates, or the first reading was not confident',
    3: 'the kind is in tension between the word and the record',
    4: 'everything else',
}

SYSTEM_PROMPT = """\
You are a Hebrew Bible scholar checking work that two other sources already agree on.

For each occurrence of a proper name in the Masoretic text you are given: the lexicon entry, the
person or place that BOTH a previous reading and the encyclopedia's verse lists say it refers to,
every other candidate the encyclopedia holds under that name, the reference, the name type BHSA
marks on the word, the Hebrew verse with the word marked, and the King James rendering of that verse
with the verse before and after it.

Your job is to find the cases where both of them are wrong. They agree, so the easy occurrences are
already right; what is left is the kinds of error two sources make together:

- the encyclopedia holds one person under two records and both sources picked the same one of them,
  so the answer looks settled and the other record is orphaned
- the encyclopedia does not hold the referent at all -- most often the word means a territory and
  every candidate is a person -- and both fell back on the nearest listed candidate
- every candidate under this name is wrong, and the agreement is agreement about the wrong set

Decide each occurrence on the verse in front of you. Genealogies, parallel lists and the line before
are what usually settle it. The King James may spell the name differently from the record's label
and that is not a reason to reject it. BHSA's name type is a property of the lemma and not of this
word, so a name that can be a place does not make this occurrence a place.

Do not manufacture doubt. Most of these are right, and saying so is the answer. Say:

  "agree"  the proposed referent is right. Add a cue: at most eight words naming what in the verse
           establishes it.
  "doubt"  it may be right, but the evidence does not carry it -- two records that look like the
           same person, or a verse that decides nothing either way. Say what is missing.
  "wrong"  it is not right. Say what it should be: another candidate's "key", or "unlisted" with
           "names" saying what the referent actually is, or "unclear" when nothing decides it.

Answer with a single JSON array and nothing else -- no prose before or after. One object per
occurrence, in the order given, with exactly these fields:

  word_id      the integer you were given, unchanged
  verdict      "agree", "doubt" or "wrong"
  cue          for "agree": at most eight words. Omit otherwise.
  should_be    for "wrong": a candidate "key", or "unlisted", or "unclear". Omit otherwise.
  names        only when should_be is "unlisted": a short phrase naming the real referent.
  reason       for "doubt" and "wrong": one line under 25 words. Omit for "agree".
  confidence   "high", "medium" or "low" -- how sure you are of THIS verdict.

Every occurrence you were given must appear exactly once.
"""

RECORD_SYSTEM_PROMPT = """\
You are auditing an encyclopedia of Biblical persons and places for records that are the same
referent written twice.

For one name you are given every record the encyclopedia holds under it: its key, its kind, its
distinguisher as written, and the verses it is attested in. Some of these names really are borne by
many people -- there are twenty-seven men called Zechariah and they are not duplicates. What you are
looking for is the other thing: two records whose distinguishers describe one person, whose attested
verses are parallels of each other, or which differ only in spelling.

Two records naming the same father are not duplicates on that ground alone; brothers exist, and so
do a man and his grandson under one name. Two records attested in the same verse are strong evidence
of a split, unless the verse plainly names two men of that name.

Answer with a single JSON array and nothing else. One object per pair you judge:

  a, b         the two record keys
  same         true if they are one referent written twice, false if you considered them and they
               are genuinely different
  reason       one line under 25 words
  confidence   "high", "medium" or "low"

Report a pair with "same": false only where the records look alike enough that a reader would ask.
If no pair under this name is worth reporting, answer with an empty array.
"""


def normalised(name):
    return re.sub(r'[^a-z]', '', (name or '').lower())


DISTINGUISHER_REFERENCE = re.compile(r'\(([^)]*)\)')
DISTINGUISHER_KIN = re.compile(r'\b(son|daughter|wife|father|mother) of ([A-Z][A-Za-z\'-]*)')


def resemblance(a, b):
    """
    Why two records under one name might be one record. Returns the reasons, strongest first, or [].

    Sharing a name is the precondition and never the evidence; what counts is a distinguisher that
    points at the same verse or the same kin, or attestation lists that overlap, because a person
    cannot be attested in a verse that names somebody else of the same name.

    A person and a place under one name are never a duplicate of each other -- Moab is a man and a
    country and the encyclopedia is right to hold both. That pair is a different question, and the
    stratum for the kinds being in tension is where it is asked.
    """
    if a['kind'] != b['kind']:
        return []
    left, right = normalised(a['name']), normalised(b['name'])
    if left != right and difflib.SequenceMatcher(None, left, right).ratio() < NAME_SIMILARITY:
        return []

    why = []
    references = (set(DISTINGUISHER_REFERENCE.findall(a['distinguisher'] or ''))
                  & set(DISTINGUISHER_REFERENCE.findall(b['distinguisher'] or '')))
    if references:
        why.append('the distinguishers name the same verse: ' + '; '.join(sorted(references)))
    kin = (set(DISTINGUISHER_KIN.findall(a['distinguisher'] or ''))
           & set(DISTINGUISHER_KIN.findall(b['distinguisher'] or '')))
    if kin:
        why.append('both are ' + '; '.join(f'{role} of {who}' for role, who in sorted(kin)))
    shared = {tuple(v) for v in a['attested']} & {tuple(v) for v in b['attested']}
    if shared:
        why.append(f'{len(shared)} verses are attested to both')
    if not why and left != right:
        why.append(f'the labels differ only in spelling ({a["name"]} / {b["name"]})')
    if not why and not (a['distinguisher'] and b['distinguisher']):
        why.append('the same name, and one of the records says nothing to tell them apart')
    return why


def suspect_pairs(by_number):
    """Every pair of records under one name that resembles itself enough to be worth asking about."""
    found = collections.defaultdict(list)
    for number, records_under_name in by_number.items():
        for i in range(len(records_under_name)):
            for j in range(i + 1, len(records_under_name)):
                why = resemblance(records_under_name[i], records_under_name[j])
                if why:
                    found[number].append({'a': records_under_name[i]['key'],
                                          'b': records_under_name[j]['key'], 'why': why})
    return found


def read_answers(directory):
    """
    Every answer the sense runs wrote, one row per word.

    The shards overlap: some occurrences were answered twice, and an aggregate that sums the shards'
    scores rather than the words counts that overlap twice. The later answer wins here, and the
    collisions are counted so the report can say how big the double count was.
    """
    rows, repeated, differing = {}, 0, []
    for path in sorted(glob.glob(os.path.join(directory, '*.jsonl'))):
        for line in open(path, encoding='utf-8'):
            if not line.strip():
                continue
            row = json.loads(line)
            previous = rows.get(row['word_id'])
            if previous:
                repeated += 1
                if previous['referent'] != row['referent']:
                    differing.append({'word_id': row['word_id'],
                                      'strong_number': row['strong_number'],
                                      'answers': [previous['referent'], row['referent']]})
            rows[row['word_id']] = row
    return rows, repeated, differing


def witness_for(word_ids, chunk=4000):
    answers = {}
    ids = sorted(word_ids)
    for start in range(0, len(ids), chunk):
        answers.update(sense.witness_answers(ids[start:start + chunk]))
    return answers


def name_types(word_ids, chunk=4000):
    types = {}
    ids = sorted(word_ids)
    for start in range(0, len(ids), chunk):
        block = ', '.join(str(int(i)) for i in ids[start:start + chunk])
        types.update(sense.psql(f"""
            SELECT coalesce(json_object_agg(w.id, w.morphology->>'nameType'), '{{}}')
            FROM word w WHERE w.id IN ({block})
        """))
    return types


def stratum_of(row, record, kinds_under_name, candidate_count, name_type, duplicates):
    """Which stratum an agreement belongs in, and the sentence saying why."""
    if row['referent'] in duplicates:
        return 1, duplicates[row['referent']]
    if candidate_count >= MANY_CANDIDATES:
        return 2, f'{candidate_count} records carry this name'
    if row['confidence'] in ('medium', 'low'):
        return 2, f'the first reading was {row["confidence"]}'
    marked = set((name_type or '').split(','))
    if marked == {'topo'} and record['kind'] == 'person':
        return 3, 'BHSA marks the word as a place name and the record is a person'
    if marked == {'gens'} and record['kind'] == 'person':
        return 3, 'BHSA marks the word as a people and the record is a person'
    if 'topo' not in marked and record['kind'] == 'place':
        return 3, 'BHSA does not mark the word as a place name and the record is one'
    if len(kinds_under_name) > 1:
        return 3, 'the name is held as both a person and a place'
    return 4, ''


def plan(args):
    """Find the agreements, put each in a stratum, and write down what the population is."""
    rows, repeated, differing = read_answers(args.answers)
    witness = witness_for(rows)
    types = name_types(rows)

    numbers = sorted({r['strong_number'] for r in rows.values()})
    known = sense.candidates(numbers)
    by_number = collections.defaultdict(list)
    for record in known:
        by_number[record['number']].append(record)
    by_key = {(r['number'], r['key']): r for r in known}
    pairs = suspect_pairs(by_number)

    resembles = collections.defaultdict(dict)
    for number, found in pairs.items():
        for pair in found:
            for key, other in ((pair['a'], pair['b']), (pair['b'], pair['a'])):
                resembles[number].setdefault(
                    key, f'{other} may be the same record: {pair["why"][0]}')

    agreements, tallies = [], collections.Counter()
    for word_id, row in rows.items():
        named = witness.get(str(word_id)) or witness.get(word_id) or []
        if len(named) != 1:
            tallies['the verse lists did not decide'] += 1
            continue
        if row['referent'] != named[0]:
            tallies['the two disagreed'] += 1
            continue
        number = row['strong_number']
        record = by_key[(number, row['referent'])]
        stratum, why = stratum_of(row, record, {c['kind'] for c in by_number[number]},
                                  len(by_number[number]), types.get(str(word_id)),
                                  resembles.get(number, {}))
        agreements.append({
            'word_id': word_id, 'strong_number': number, 'referent': row['referent'],
            'first_confidence': row['confidence'], 'first_reason': row['reason'],
            'candidates': len(by_number[number]), 'kind': record['kind'],
            'bhsa_name_type': types.get(str(word_id)),
            'stratum': stratum, 'stratum_reason': why,
        })
        tallies[f'stratum {stratum}'] += 1

    os.makedirs(args.dir, exist_ok=True)
    with open(os.path.join(args.dir, 'strata.json'), 'w', encoding='utf-8') as handle:
        json.dump({
            'prompt_version': PROMPT_VERSION,
            'database': sense.DATABASE,
            'planned': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
            'answers_read': len(rows),
            'answered_twice': repeated,
            'answered_twice_differently': differing,
            'strata': {str(k): v for k, v in STRATA.items()},
            'counts': dict(tallies),
            'suspect_pairs': dict(sorted(pairs.items())),
            'agreements': agreements,
        }, handle, ensure_ascii=False, indent=1)

    print(f'{len(rows)} answers, {len(agreements)} agreements to audit')
    for stratum in sorted(STRATA):
        print(f'  stratum {stratum}: {tallies[f"stratum {stratum}"]:5d}  {STRATA[stratum]}')
    print(f'  {repeated} occurrences were answered twice, {len(differing)} of them differently')


def load_strata(directory):
    with open(os.path.join(directory, 'strata.json'), encoding='utf-8') as handle:
        return json.load(handle)


def shown_record(record, proposed):
    return {
        'key': record['key'],
        'kind': record['kind'],
        'name': record['name'],
        'distinguisher': record['distinguisher'],
        'meaning': record['meaning'],
        'attested_in': [sense.reference(*a) for a in record['attested'][:sense.ATTESTATION_SHOWN]],
        'attested_in_total': len(record['attested']),
        'proposed_for_some_of_these_occurrences': record['key'] in proposed,
    }


def shard_of(agreements, shards):
    """
    Split a stratum across parallel runs by name, never by occurrence.

    A name is only decidable beside its own candidate list, and that list is the expensive half of
    the prompt: splitting Judah across eight shards sends its twenty records eight times. Names go
    to the lightest shard, biggest first, which is close enough to balanced and keeps each name in
    one place.
    """
    weight = collections.Counter(a['strong_number'] for a in agreements)
    buckets = [set() for _ in range(shards)]
    loads = [0] * shards
    for number, count in sorted(weight.items(), key=lambda pair: (-pair[1], pair[0])):
        lightest = loads.index(min(loads))
        buckets[lightest].add(number)
        loads[lightest] += count
    return buckets


def extract(args):
    """Write the prompt payloads for one stratum, or for one shard of one."""
    strata = load_strata(args.dir)
    wanted = [a for a in strata['agreements'] if a['stratum'] == args.stratum]
    if args.shards > 1:
        mine = shard_of(wanted, args.shards)[args.shard]
        wanted = [a for a in wanted if a['strong_number'] in mine]
    if not wanted:
        raise SystemExit(f'stratum {args.stratum} shard {args.shard} has nothing in it. '
                         f'Run plan first, or ask for fewer shards.')

    numbers = sorted({a['strong_number'] for a in wanted})
    entries = {e['number']: e for e in sense.lexicon(numbers)}
    by_number_records = collections.defaultdict(list)
    for record in sense.candidates(numbers):
        by_number_records[record['number']].append(record)
    by_number_occurrences = collections.defaultdict(dict)
    for occurrence in sense.occurrences(numbers):
        by_number_occurrences[occurrence['number']][occurrence['word_id']] = occurrence

    proposed = collections.defaultdict(dict)
    for agreement in wanted:
        proposed[agreement['strong_number']][agreement['word_id']] = agreement['referent']

    counts = {n: len(proposed[n]) for n in numbers}
    directory = os.path.join(args.dir, 'batches', str(args.stratum))
    os.makedirs(directory, exist_ok=True)
    written_batches = []

    for index, (batch_numbers, part, parts) in enumerate(
            sense.batched(numbers, counts, args.batch_numbers, args.batch_occurrences)):
        name = f'batch-{args.shard:02d}-{index:04d}'
        payload = {'batch': name, 'prompt_version': PROMPT_VERSION, 'names': []}
        written = 0
        for number in batch_numbers:
            asked = sorted(proposed[number])
            if parts > 1:
                size = -(-len(asked) // parts)
                asked = asked[part * size:(part + 1) * size]
            if not asked:
                continue
            keys = {proposed[number][w] for w in asked}
            payload['names'].append({
                'strong_number': number,
                'part': None if parts == 1 else f'{part + 1} of {parts}',
                'lexicon': entries.get(number),
                'candidates': [shown_record(r, keys) for r in by_number_records[number]],
                'occurrences': [dict(sense.asking(by_number_occurrences[number][w]),
                                     proposed_referent=proposed[number][w])
                                for w in asked],
            })
            written += len(asked)
        if not written:
            continue
        with open(os.path.join(directory, name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)
        written_batches.append({'batch': name, 'numbers': batch_numbers, 'occurrences': written})

    with open(os.path.join(args.dir, f'plan-{args.stratum}-{args.shard:02d}.json'), 'w',
              encoding='utf-8') as handle:
        json.dump({'stratum': args.stratum, 'shard': args.shard, 'shards': args.shards,
                   'batches': written_batches}, handle, ensure_ascii=False, indent=1)
    total = sum(b['occurrences'] for b in written_batches)
    print(f'stratum {args.stratum} shard {args.shard}: {total} occurrences over '
          f'{len(written_batches)} batches -> {directory}')


def ask(args):
    """Judge every batch of a stratum that has no verdicts yet."""
    plans = sorted(glob.glob(os.path.join(args.dir, f'plan-{args.stratum}-*.json')))
    if args.shards > 1:
        plans = [p for p in plans if p.endswith(f'-{args.shard:02d}.json')]
    batches = [b for path in plans for b in json.load(open(path, encoding='utf-8'))['batches']]
    if not batches:
        raise SystemExit(f'no batches for stratum {args.stratum}. Run extract first.')

    path = os.path.join(args.dir, f'verdicts-{args.stratum}.jsonl')
    done = set()
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            done = {json.loads(line)['batch'] for line in handle if line.strip()}

    agreed = {a['word_id']: a['referent'] for a in load_strata(args.dir)['agreements']}
    run = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    spent, called, failures = 0.0, 0, []

    for entry in batches:
        name = entry['batch']
        if name in done and not args.again:
            continue
        payload = json.load(open(
            os.path.join(args.dir, 'batches', str(args.stratum), name + '.json'), encoding='utf-8'))
        expected = {o['word_id']: n['strong_number']
                    for n in payload['names'] for o in n['occurrences']}
        keys = {c['key'] for n in payload['names'] for c in n['candidates']}

        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, SYSTEM_PROMPT)
        if failed:
            failures.append((name, failed))
            print(f'{name}: {failed}', flush=True)
            continue
        spent += outcome.get('total_cost_usd') or 0.0
        called += 1
        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        answers = sense.parse(outcome.get('result'))
        if answers is None:
            failures.append((name, 'no JSON array in the reply'))
            print(f'{name}: no JSON array in the reply', flush=True)
            continue

        rows, seen = [], set()
        for answer in answers:
            word_id = answer.get('word_id')
            if word_id not in expected or word_id in seen:
                continue
            seen.add(word_id)
            verdict = answer.get('verdict')
            if verdict not in VERDICTS:
                verdict = VERDICT_DOUBT
            should_be = answer.get('should_be')
            if verdict == VERDICT_WRONG and should_be not in keys and should_be not in (
                    sense.ANSWER_UNCLEAR, sense.ANSWER_UNLISTED):
                should_be = sense.ANSWER_UNCLEAR
            rows.append({
                'word_id': word_id,
                'strong_number': expected[word_id],
                'agreed_referent': agreed[word_id],
                'stratum': args.stratum,
                'verdict': verdict,
                'should_be': should_be if verdict == VERDICT_WRONG else None,
                'names': answer.get('names'),
                'cue': answer.get('cue'),
                'reason': answer.get('reason'),
                'confidence': answer.get('confidence'),
                'batch': name,
                'prompt_version': payload['prompt_version'],
                'model': model,
                'run': run,
                'method': 'model-audit',
            })
        with open(path, 'a', encoding='utf-8') as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        dissent = sum(1 for r in rows if r['verdict'] != VERDICT_AGREE)
        print(f'{name}: {len(rows)}/{len(expected)} judged, {dissent} not agree, '
              f'${outcome.get("total_cost_usd") or 0:.4f}', flush=True)

    print(f'{called} calls, ${spent:.4f} reported by the harness'
          + (f', {len(failures)} failed' if failures else ''))


def call(prompt, model, system_prompt):
    """`sense.call` with this pass's system prompt in place of the reading one."""
    original = sense.SYSTEM_PROMPT
    sense.SYSTEM_PROMPT = system_prompt
    try:
        return sense.call(prompt, model)
    finally:
        sense.SYSTEM_PROMPT = original


def records(args):
    """Ask, name by name, which of the look-alike records are one referent written twice."""
    pairs = load_strata(args.dir)['suspect_pairs']
    numbers = sorted(pairs)
    by_number = collections.defaultdict(list)
    for record in sense.candidates(numbers):
        by_number[record['number']].append(record)
    entries = {e['number']: e for e in sense.lexicon(numbers)}

    path = os.path.join(args.dir, 'records.jsonl')
    done = set()
    if os.path.exists(path):
        with open(path, encoding='utf-8') as handle:
            done = {json.loads(line)['strong_number'] for line in handle if line.strip()}

    run = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds')
    spent, called = 0.0, 0
    for number in numbers:
        if number in done and not args.again:
            continue
        payload = {
            'strong_number': number,
            'prompt_version': RECORD_PROMPT_VERSION,
            'lexicon': entries.get(number),
            'records': [{'key': r['key'], 'kind': r['kind'], 'name': r['name'],
                         'distinguisher': r['distinguisher'], 'meaning': r['meaning'],
                         'attested_in': [sense.reference(*a) for a in r['attested']][:40],
                         'attested_in_total': len(r['attested'])}
                        for r in by_number[number]],
            'pairs_that_look_alike': pairs[number],
        }
        outcome, failed = call(json.dumps(payload, ensure_ascii=False, indent=1),
                               args.model, RECORD_SYSTEM_PROMPT)
        if failed:
            print(f'{number}: {failed}', flush=True)
            continue
        spent += outcome.get('total_cost_usd') or 0.0
        called += 1
        model = next(iter(outcome.get('modelUsage') or {'unknown': None}))
        answers = sense.parse(outcome.get('result'))
        if answers is None:
            print(f'{number}: no JSON array in the reply', flush=True)
            continue
        keys = {r['key'] for r in by_number[number]}
        rows = [{'strong_number': number, 'a': a['a'], 'b': a['b'], 'same': bool(a.get('same')),
                 'reason': a.get('reason'), 'confidence': a.get('confidence'),
                 'prompt_version': RECORD_PROMPT_VERSION, 'model': model, 'run': run,
                 'method': 'model-audit'}
                for a in answers if a.get('a') in keys and a.get('b') in keys]
        with open(path, 'a', encoding='utf-8') as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        print(f'{number}: {sum(1 for r in rows if r["same"])} of {len(rows)} judged the same, '
              f'${outcome.get("total_cost_usd") or 0:.4f}', flush=True)
    print(f'{called} calls, ${spent:.4f} reported by the harness')


def percentage(part, whole):
    return f'{100.0 * part / whole:.2f}%' if whole else '-'


def report(args):
    """Read every verdict written so far and write the report, whichever strata have run."""
    strata = load_strata(args.dir)
    judged = {}
    for path in sorted(glob.glob(os.path.join(args.dir, 'verdicts-*.jsonl'))):
        for line in open(path, encoding='utf-8'):
            if line.strip():
                row = json.loads(line)
                judged[row['word_id']] = row

    duplicates, distinct = [], []
    records_path = os.path.join(args.dir, 'records.jsonl')
    if os.path.exists(records_path):
        for line in open(records_path, encoding='utf-8'):
            if line.strip():
                row = json.loads(line)
                (duplicates if row['same'] else distinct).append(row)

    per_stratum = collections.defaultdict(collections.Counter)
    for row in judged.values():
        per_stratum[row['stratum']][row['verdict']] += 1
        per_stratum[row['stratum']]['judged'] += 1
    planned = collections.Counter(a['stratum'] for a in strata['agreements'])

    wrong = [r for r in judged.values() if r['verdict'] == VERDICT_WRONG]
    doubt = [r for r in judged.values() if r['verdict'] == VERDICT_DOUBT]
    missing = [r for r in wrong if r['should_be'] == sense.ANSWER_UNLISTED]
    named = [r for r in wrong if r['should_be'] not in (sense.ANSWER_UNLISTED, sense.ANSWER_UNCLEAR)]
    undecidable = [r for r in wrong if r['should_be'] == sense.ANSWER_UNCLEAR]

    lines = []
    write = lines.append
    write('# The agreements, audited')
    write('')
    write(f'{len(strata["agreements"])} occurrences where the first reading and the encyclopedia\'s '
          f'verse lists name the same referent, read a second time and asked whether both are '
          f'wrong. {len(judged)} have been judged, prompt {PROMPT_VERSION}, model '
          f'{next(iter({r["model"] for r in judged.values()}), "none yet")}.')
    write('')
    write('## By stratum, dangerous first')
    write('')
    write('| | population | audited | agree | doubt | wrong | doubt rate | wrong rate |')
    write('|---|---|---|---|---|---|---|---|')
    for stratum in sorted(STRATA):
        counts = per_stratum[stratum]
        write(f'| {stratum}. {STRATA[stratum]} | {planned[stratum]} | {counts["judged"]} '
              f'| {counts[VERDICT_AGREE]} | {counts[VERDICT_DOUBT]} | {counts[VERDICT_WRONG]} '
              f'| {percentage(counts[VERDICT_DOUBT], counts["judged"])} '
              f'| {percentage(counts[VERDICT_WRONG], counts["judged"])} |')
    total = collections.Counter()
    for counts in per_stratum.values():
        total.update(counts)
    write(f'| all | {len(strata["agreements"])} | {total["judged"]} | {total[VERDICT_AGREE]} '
          f'| {total[VERDICT_DOUBT]} | {total[VERDICT_WRONG]} '
          f'| {percentage(total[VERDICT_DOUBT], total["judged"])} '
          f'| {percentage(total[VERDICT_WRONG], total["judged"])} |')
    write('')
    write('## Referents the encyclopedia does not hold')
    write('')
    if missing:
        write(f'{len(missing)} occurrences whose referent the audit says is not among the candidates '
              f'at all. This is the most valuable class: no better choice between the records would '
              f'have found it, because the right answer is not one of them.')
        write('')
        write('Two different things live in this class and they should not be added together. Where '
              'the word means a place, or a man, that the encyclopedia does not hold, it is a '
              'coverage gap and a fact. Where the word is a tribal name standing in construct -- '
              '*the tribe of Judah*, *the camp of Judah*, *the children of Judah* -- calling the '
              'referent the tribe rather than the ancestor it is named for is a reading, and one a '
              'scholar could take either way; the encyclopedia holds no peoples, so the patriarch '
              'was the only record there was to choose. They are listed here as what the audit '
              'said, not as proven errors.')
        write('')
        for row in sorted(missing, key=lambda r: (r['strong_number'], r['word_id']))[:args.show]:
            write(f'- {row["strong_number"]} word {row["word_id"]}: both said '
                  f'**{row["agreed_referent"]}**, the audit says **{row["names"]}** '
                  f'({row["confidence"]}) -- {row["reason"]}')
        if len(missing) > args.show:
            write(f'- ... and {len(missing) - args.show} more, in audit.json')
    else:
        write('None in what has been audited so far.')
    write('')
    write('## Wrong, where the encyclopedia holds the right record')
    write('')
    if named:
        for row in sorted(named, key=lambda r: (r['strong_number'], r['word_id']))[:args.show]:
            write(f'- {row["strong_number"]} word {row["word_id"]}: both said '
                  f'**{row["agreed_referent"]}**, the audit says **{row["should_be"]}** '
                  f'({row["confidence"]}) -- {row["reason"]}')
        if len(named) > args.show:
            write(f'- ... and {len(named) - args.show} more, in audit.json')
    else:
        write('None in what has been audited so far.')
    write('')
    write(f'{len(undecidable)} more the audit calls wrong without being able to say what instead.')
    write('')
    write('## Doubted')
    write('')
    write(f'{len(doubt)} occurrences the audit would not endorse and does not call wrong. A doubt is '
          f'a third opinion and not a verdict: it says the evidence is thinner than two agreeing '
          f'sources imply.')
    write('')
    for row in sorted(doubt, key=lambda r: (r['strong_number'], r['word_id']))[:args.show]:
        write(f'- {row["strong_number"]} word {row["word_id"]}: **{row["agreed_referent"]}** '
              f'({row["confidence"]}) -- {row["reason"]}')
    if len(doubt) > args.show:
        write(f'- ... and {len(doubt) - args.show} more, in audit.json')
    write('')
    write('## Records that are one referent written twice')
    write('')
    if duplicates:
        write(f'{len(duplicates)} pairs over {len({d["strong_number"] for d in duplicates})} names. '
              f'A duplicate cannot be fixed by choosing better between the two records, so it is '
              f'worth as much as a wrong referent and is easier to act on.')
        write('')
        for row in sorted(duplicates, key=lambda r: r['strong_number']):
            write(f'- {row["strong_number"]} **{row["a"]}** and **{row["b"]}** '
                  f'({row["confidence"]}) -- {row["reason"]}')
    else:
        write('None adjudicated yet.')
    write('')
    write(f'{len(distinct)} further pairs were compared and judged genuinely different.')
    write('')
    write('## What the population itself says')
    write('')
    write(f'- {strata["answers_read"]} distinct occurrences carry a first reading, and '
          f'{strata["answered_twice"]} of them were read twice by overlapping shards; '
          f'{len(strata["answered_twice_differently"])} of those pairs of answers differed.')
    write(f'- {len(strata["agreements"])} distinct agreements. The 8,198 previously reported summed '
          f'the shards\' scores rather than the words, and counted the overlap twice.')

    text = '\n'.join(lines) + '\n'
    payload = {
        'generated': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
        'prompt_version': PROMPT_VERSION,
        'population': len(strata['agreements']),
        'audited': len(judged),
        'strata': {str(s): {'population': planned[s], **dict(per_stratum[s])}
                   for s in sorted(STRATA)},
        'wrong': sorted(wrong, key=lambda r: (r['strong_number'], r['word_id'])),
        'doubt': sorted(doubt, key=lambda r: (r['strong_number'], r['word_id'])),
        'duplicate_records': duplicates,
        'distinct_records': distinct,
        'answered_twice': strata['answered_twice'],
        'answered_twice_differently': strata['answered_twice_differently'],
    }
    with open(os.path.join(args.dir, 'audit.md'), 'w', encoding='utf-8') as handle:
        handle.write(text)
    with open(os.path.join(args.dir, 'audit.json'), 'w', encoding='utf-8') as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)
    if args.into:
        os.makedirs(args.into, exist_ok=True)
        with open(os.path.join(args.into, 'audit.md'), 'w', encoding='utf-8') as handle:
            handle.write(text)
        with open(os.path.join(args.into, 'audit.json'), 'w', encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=1)
    print(text)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[1])
    commands = parser.add_subparsers(dest='command', required=True)

    planner = commands.add_parser('plan', help='find the agreements and put each in a stratum')
    planner.add_argument('--answers', required=True, help="directory of the sense runs' answers")
    planner.add_argument('--out', dest='dir', required=True)
    planner.set_defaults(run=plan)

    extractor = commands.add_parser('extract', help='write the prompt payloads for one stratum')
    extractor.add_argument('--dir', required=True)
    extractor.add_argument('--stratum', type=int, required=True, choices=sorted(STRATA))
    extractor.add_argument('--shard', type=int, default=0)
    extractor.add_argument('--shards', type=int, default=1)
    extractor.add_argument('--batch-numbers', type=int, default=BATCH_NUMBERS)
    extractor.add_argument('--batch-occurrences', type=int, default=BATCH_OCCURRENCES)
    extractor.set_defaults(run=extract)

    asker = commands.add_parser('ask', help='judge every batch of a stratum that has no verdict yet')
    asker.add_argument('--dir', required=True)
    asker.add_argument('--stratum', type=int, required=True, choices=sorted(STRATA))
    asker.add_argument('--shard', type=int, default=0)
    asker.add_argument('--shards', type=int, default=1)
    asker.add_argument('--model', default='opus')
    asker.add_argument('--again', action='store_true')
    asker.set_defaults(run=ask)

    recorder = commands.add_parser('records', help='adjudicate the look-alike records')
    recorder.add_argument('--dir', required=True)
    recorder.add_argument('--model', default='opus')
    recorder.add_argument('--again', action='store_true')
    recorder.set_defaults(run=records)

    reporter = commands.add_parser('report', help='write the report from whatever has run')
    reporter.add_argument('--dir', required=True)
    reporter.add_argument('--show', type=int, default=60)
    reporter.add_argument('--into', help='also write the report here, to be committed')
    reporter.set_defaults(run=report)

    args = parser.parse_args()
    args.run(args)


if __name__ == '__main__':
    main()
