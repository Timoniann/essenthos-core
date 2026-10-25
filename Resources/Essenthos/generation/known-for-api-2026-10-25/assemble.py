"""
Gathers the three readings into the list the owner reviews, Resources/Essenthos/review/known-for.json, and a table
to read (results.md) and the counts (summary.json) beside the run.

A record enters the list only where every reading of it passed the checks in kf.py and at least one clause
survived the second reading. An entry the owner has answered is kept exactly as he left it, whatever this run
says of the record now.
"""
import collections
import json
from pathlib import Path

import kf

REVIEW = kf.HERE.parents[1] / 'review' / 'known-for.json'

SOURCE = (
    "Essenthos, what a record is known for, written from its verses in the King James Version with the verses each "
    "clause rests on by claude-sonnet-5-5 on 2026-10-25, read again clause by clause against those verses and "
    "rendered into Ukrainian, German and Spanish by a second, separate reading, the renderings read by a third; "
    "on the project owner's instruction, each record shown once he has approved it"
)

ABOUT = (
    "What a person, a place or an object is known for: a few clauses, each with the verses that say it, in English, "
    "Ukrainian, German and Spanish, written by a language model and read twice more. Nothing here is shown on the "
    "site until it is approved: an entry answered 'approved' is loaded by the next load of the known-for step, "
    "one answered 'rejected' is not. 'declined' lists the records the first reading gave no line, with its reason."
)


def entry(r, draft, check, review):
    second = []
    clauses = []
    renderings = iter(review['answer']['clauses'])
    for first, judged in zip(draft['answer']['clauses'], check['answer']['clauses']):
        verdict = {'verdict': judged['verdict'], 'why': judged['why'] or None}
        if judged['verdict'] != 'right':
            verdict['first'] = {'english': first['english'], 'references': [kf.normal(ref) for ref in first['references']]}
        second.append(verdict)
        if judged['verdict'] == 'drop':
            continue
        rendered = next(renderings)
        clauses.append({'english': judged['english'], 'ukr': rendered['ukr'], 'deu': rendered['deu'],
                        'spa': rendered['spa'], 'references': [kf.normal(ref) for ref in judged['references']],
                        'renderingsChanged': rendered['why'] or None})
    return {
        'slug': r['slug'], 'kind': r['kind'], 'name': r['name'], 'sex': r['sex'], 'verses': len(r['mentioned']),
        'clauses': clauses,
        'readings': {'first': {'reason': draft['answer']['reason'] or None, 'doubts': draft['answer']['doubts']},
                     'second': second, 'note': check['answer']['note'] or None},
    }


def main():
    rs = kf.by_slug()
    held = {}
    if REVIEW.is_file():
        for old in json.load(open(REVIEW, encoding='utf-8'))['records']:
            if old.get('decision'):
                held[old['slug']] = old
    entries, declined, unfinished = [], [], []
    for slug in kf.candidates():
        r = rs[slug]
        if slug in held:
            entries.append(held[slug])
            continue
        draft, check, review = (kf.load(kind, slug) for kind in kf.KINDS)
        if draft is None:
            unfinished.append({'slug': slug, 'stage': 'draft', 'problems': ['not read']})
        elif draft['problems']:
            unfinished.append({'slug': slug, 'stage': 'draft', 'problems': draft['problems']})
        elif draft['answer']['decision'] == 'none':
            declined.append({'slug': slug, 'kind': r['kind'], 'name': r['name'], 'verses': len(r['mentioned']),
                             'reason': draft['answer']['reason']})
        elif check is None or check['problems']:
            unfinished.append({'slug': slug, 'stage': 'check', 'problems': check['problems'] if check else ['not read']})
        elif not kf.kept(check['answer']):
            declined.append({'slug': slug, 'kind': r['kind'], 'name': r['name'], 'verses': len(r['mentioned']),
                             'reason': 'the second reading dropped every clause: ' + '; '.join(
                                 c['why'] for c in check['answer']['clauses'])})
        elif review is None or review['problems']:
            unfinished.append({'slug': slug, 'stage': 'review',
                               'problems': review['problems'] if review else ['not read']})
        else:
            entries.append(entry(r, draft, check, review))
    out = {'about': ABOUT, 'source': SOURCE, 'records': entries, 'declined': declined, 'unfinished': unfinished}
    REVIEW.parent.mkdir(parents=True, exist_ok=True)
    with open(REVIEW, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
        f.write('\n')

    counts = {
        'candidates': collections.Counter(rs[s]['kind'] for s in kf.candidates()),
        'written': collections.Counter(e['kind'] for e in entries),
        'clauses': sum(len(e['clauses']) for e in entries),
        'declined': collections.Counter(d['kind'] for d in declined),
        'unfinished': collections.Counter(u['stage'] for u in unfinished),
        'second reading': collections.Counter(v['verdict'] for e in entries for v in e['readings']['second']),
        'renderings changed by the third reading': sum(1 for e in entries for c in e['clauses']
                                                       if c['renderingsChanged']),
    }
    json.dump(counts, open(kf.HERE / 'summary.json', 'w', encoding='utf-8', newline='\n'), ensure_ascii=False,
              indent=1)
    write_table(entries, declined, unfinished)
    print(json.dumps(counts, ensure_ascii=False))


def write_table(entries, declined, unfinished):
    lines = ['# Known for, written through the Claude API (TSK-1210), 2026-10-25', '',
             'Model claude-sonnet-5-5; three readings (write, check and render, read the renderings). '
             'The list the console shows is Resources/Essenthos/review/known-for.json.', '']
    for kind in ('person', 'place', 'object'):
        mine = [e for e in entries if e['kind'] == kind]
        if not mine:
            continue
        lines += [f'## {kind} ({len(mine)})', '', '| Record | Verses | English | Ukrainian | References |',
                  '|---|---|---|---|---|']
        for e in mine:
            lines.append(f'| **{e["name"]}** `{e["slug"]}` | {e["verses"]} | '
                         + '; '.join(c['english'] for c in e['clauses']) + ' | '
                         + '; '.join(c['ukr'] for c in e['clauses']) + ' | '
                         + ' · '.join(', '.join(c['references']) for c in e['clauses']) + ' |')
        lines.append('')
    lines += [f'## Declined ({len(declined)})', '']
    lines += [f'- `{d["slug"]}` ({d["kind"]}, {d["verses"]} verses): {d["reason"]}' for d in declined]
    lines += ['', f'## Unfinished ({len(unfinished)})', '']
    lines += [f'- `{u["slug"]}` at {u["stage"]}: {"; ".join(u["problems"])}' for u in unfinished]
    (kf.HERE / 'results.md').write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
