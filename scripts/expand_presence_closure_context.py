"""Add distant Biblical definitions without changing the earlier case/event snapshot."""
import argparse
import collections
import copy
import json
import re
from pathlib import Path

import presence_closure_context as original

OUT = original.OUT
VERSION = 'presence-closure-context-expanded-v1'
ORIGINAL_SHA = '53e0c03ff0112a49f98b240363ab32bb5af4dbf9f6e4e949b2d48ab1545010b9'
CATALOGUE_SHA = '898cc2ed75be25e22acb0640a9a6e03a6b716b3a045a1da6240b209ecc3efdd4'
CHAPTER_DEFINITIONS = {
    (2, 40): ([(2, 26)], 'Exodus26 defines the compartments and furniture placed in Exodus40'),
    (12, 22): ([(14, 34)], 'Parallel Huldah consultation and district wording'),
    (14, 34): ([(12, 22)], 'Parallel Huldah consultation and district wording'),
    (13, 3): ([(10, 11), (10, 12)], 'BathShua/Bathsheba and parent-name comparison; identity is not pre-decided'),
}
GROUP_DEFINITIONS = {
    'holy_city_parallel': ([(42, 4)], 'Explicit Luke4:9 parallel to the holy city wording'),
    'kadesh_ancient_identity_vs_point': ([(4, 13), (5, 1)], 'Spy mission and Kadesh comparison, distinct from modern coordinates'),
    'reuel_jethro': ([(2, 2), (2, 3)], 'Reuel/Jethro names and Midian priesthood; separate identity argument'),
    'royal_prison_court_phase': ([(24, 32)], 'Jeremiah32:2 defines the court within the royal complex of that narrative'),
    'gerasa_gadara_gergesa': ([(40, 8), (41, 5), (42, 8)], 'Parallel regional names preserved as distinct Gospel readings'),
}
PASSION = {(40, 26), (41, 14), (42, 22), (43, 18)}


def focus_chapters(case):
    names = set(case['context_metadata']['original_focus_keys'])
    return {tuple(address[:2]) for verse in case['verses'] if verse['key'] in names
            for address in verse['canonicalAddresses']}


def requests(case, research):
    focus = focus_chapters(case)
    result = {}
    for chapter in focus:
        if chapter in CHAPTER_DEFINITIONS:
            chapters, reason = CHAPTER_DEFINITIONS[chapter]
            for wanted in chapters:
                result.setdefault(wanted, []).append(reason)
    group = research['group']
    if group in GROUP_DEFINITIONS:
        chapters, reason = GROUP_DEFINITIONS[group]
        for wanted in chapters:
            result.setdefault(wanted, []).append(reason)
    # Cross-Gospel priestly-court context is relevant to these cases, not every NT episode.
    place = ' '.join(str(case.get(field) or '') for field in ('place_slug','place_name'))
    priestly = re.search(r'high.?priest|caiaph|annas|Каяф|Анн|первосвящ', place, re.I)
    if focus & PASSION and (group == 'annas_caiaphas_sequence' or priestly):
        for wanted in PASSION - focus:
            result.setdefault(wanted, []).append('Compare named high-priest courts and transfers without harmonizing the sequence')
    return [{'canonical_chapter': list(chapter), 'reasons': sorted(set(reasons))}
            for chapter, reasons in sorted(result.items())]


def merge(case, additional):
    result = copy.deepcopy(case)
    existing = {verse['key']: verse for verse in result['verses']}
    added = []
    for verse in additional:
        name = original.key(verse)
        if name in existing:
            # Existing events and their exact evidence remain untouched.
            continue
        value = original.scripture(verse, set())
        assert value['role'] == 'context'
        existing[name] = value
        added.append(name)
    result['verses'] = [existing[name] for name in sorted(existing)]
    assert result['focus_keys'] == case['focus_keys']
    result['context_metadata']['original_snapshot_bhsa_diagnostic'] = copy.deepcopy(case['context_metadata']['bhsa'])
    focus = {tuple(a) for verse in case['verses'] if verse['key'] in case['focus_keys'] for a in verse['canonicalAddresses']}
    result['context_metadata']['bhsa'] = original.diagnostic(result['verses'], focus)
    return result, sorted(added)


def validate_additive(base, expanded):
    assert len(base['cases']) == len(expanded['cases']) == 201
    before = {case['case_id']: case for case in base['cases']}
    after = {case['case_id']: case for case in expanded['cases']}
    assert before.keys() == after.keys()
    for name, previous in before.items():
        current = after[name]
        for field in ('case_id','source_kind','person','place_slug','place_name','category','original','focus_keys','candidate_places'):
            assert current[field] == previous[field], (name, field)
        old_verses = {v['key']: v for v in previous['verses']}
        new_verses = {v['key']: v for v in current['verses']}
        assert len(new_verses) == len(current['verses'])
        for key, verse in old_verses.items():
            assert new_verses[key] == verse, (name, key)
        added = set(new_verses) - old_verses.keys()
        assert all(new_verses[key]['role']=='context' for key in added)
        assert added == set(current['context_metadata']['additive_context']['added_keys'])
        assert not added.intersection(current['focus_keys'])
        if all(address[0]>39 for address in focus_chapters(previous)):
            assert not any(v['key'].startswith('BHSA:') for v in current['verses'])
    return expanded


def build():
    base_path = OUT/'contexts.json'
    catalogue_path = OUT/'catalogue.json'
    research_path = OUT/'web-research.json'
    assert original.sha(base_path) == ORIGINAL_SHA, 'Original context snapshot changed'
    assert original.sha(catalogue_path) == CATALOGUE_SHA, 'Original catalogue changed'
    base = original.read(base_path)
    research = original.read(research_path)
    assert research['complete']
    by_case = {case['case_id']: case for case in research['cases']}
    assert by_case.keys() == {case['case_id'] for case in base['cases']}
    wanted = {case['case_id']: requests(case, by_case[case['case_id']]) for case in base['cases']}
    corpora = {row['slug'] for row in base['corpora']}
    export_path = original.BASE/'export.jsonl'
    expected_export = base['source_hashes'][str(export_path.relative_to(original.MAIN)).replace('\\','/')]
    verses, _ = original.source_rows(export_path, expected_export, corpora)
    by_canonical = collections.defaultdict(set)
    by_printed = collections.defaultdict(list)
    for verse in verses.values():
        printed = (verse['text'],verse['book'],verse['chapter'])
        by_printed[printed].append(verse)
        for address in verse['canonicalAddresses']:
            by_canonical[tuple(address[:2])].add(printed)
    result = copy.deepcopy(base)
    result['version'] = VERSION
    result['source_hashes'].update({
        str(path.relative_to(original.MAIN)).replace('\\','/'): original.sha(path)
        for path in (base_path, research_path)})
    result['cases'] = []
    for case in base['cases']:
        definitions = wanted[case['case_id']]
        chapters = {tuple(item['canonical_chapter']) for item in definitions}
        printed = {source for chapter in chapters for source in by_canonical[chapter]}
        additions = [verse for source in sorted(printed) for verse in by_printed[source]]
        expanded, added_keys = merge(case, additions)
        expanded['context_metadata']['additive_context'] = dict(
            version=VERSION, source_contexts_sha256=ORIGINAL_SHA,
            requested_chapters=definitions, added_keys=added_keys,
            research_group=by_case[case['case_id']]['group'],
            all_additions_role='context', changes_event_focus=False,
            selection_is_retrieval_not_identity_or_presence_proof=True)
        result['cases'].append(expanded)
    result['source_provenance']['additive_context'] = dict(version=VERSION,
        base_contexts_sha256=ORIGINAL_SHA, base_catalogue_sha256=CATALOGUE_SHA,
        method='Distant chapter definitions and selected parallel narratives; all original event anchors, rows and exact witnesses preserved',
        web_research_role='Only directs retrieval of held Bible chapters; external claims are not inserted into Scripture text')
    validate_additive(base,result)
    original.validate(result)
    original.freeze(OUT/'contexts-expanded.json', result)
    proof = dict(complete=True, version=VERSION, cases=len(result['cases']),
        source_contexts_sha256=ORIGINAL_SHA, source_catalogue_sha256=CATALOGUE_SHA,
        contexts_expanded_sha256=original.sha(OUT/'contexts-expanded.json'),
        source_code_sha256=original.sha(Path(__file__)),
        cases_with_requested_context=sum(bool(wanted[case['case_id']]) for case in base['cases']),
        cases_with_added_verses=sum(bool(case['context_metadata']['additive_context']['added_keys']) for case in result['cases']),
        added_case_verse_count=sum(len(case['context_metadata']['additive_context']['added_keys']) for case in result['cases']),
        unique_added_verse_keys=len({key for case in result['cases'] for key in case['context_metadata']['additive_context']['added_keys']}),
        existing_focus_unchanged=True, existing_verses_unchanged=True,
        original_context_and_catalogue_unchanged=(original.sha(base_path)==ORIGINAL_SHA and original.sha(catalogue_path)==CATALOGUE_SHA))
    assert proof['original_context_and_catalogue_unchanged']
    original.freeze(OUT/'context-expanded-validation.json', proof)
    print(json.dumps(proof,ensure_ascii=False))


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('mode',choices=['prepare','verify'])
    parser.parse_args()
    # Both modes reproduce the entire additive snapshot from raw, hash-bound source data.
    build()
