"""geez_batch.py's TEI reading, request builders, validators and submit guard, offline: no API, no key, no database."""
import json
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from types import SimpleNamespace

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))
import claude_api  # noqa: E402
import geez_batch as gb  # noqa: E402

TEI_ENTRY = """<TEI xmlns="http://www.tei-c.org/ns/1.0"><text><body><div>
<entry xml:id="Lx" n="1"><form><foreign xml:lang="gez">ክዳን</foreign></form>
<sense xml:id="Dmain" xml:lang="la" source="#dillmann"> subst. [ <cit type="translation" xml:lang="la"><quote>arabice tegere</quote></cit> ]
 <sense n="1"> <cit type="translation" xml:lang="la"><quote> tegumentum </quote></cit>, <foreign xml:lang="grc">σκέπη</foreign>
   <ref cRef="Job " loc="24,8">Job 24,8</ref>; <ref cRef="Jes." loc="4,6">Jes. 4,6</ref>;
   <sense n="a"> <cit type="translation" xml:lang="la"><quote>velum</quote></cit> <ref cRef="Ex." loc="37,16">Ex. 37,16</ref>. </sense>
 </sense>
 <sense n="2"> quatenus sunt dicta, effata, verba <ref cRef="Gen." loc="1,1">Gen. 1,1</ref></sense>
 <sense n="L" xml:id="TL"> ክዳን kǝdān cover, lid (Leslau) 285b </sense>
</sense>
<sense xml:id="Tmain" xml:lang="en" source="#dillmann"><sense n="L" xml:id="DL">ክዳን kǝdān lid 285b</sense></sense>
<sense xml:id="Tmain" xml:lang="en" source="#traces"><cit type="translation" xml:lang="en"><quote>cover</quote></cit></sense>
</entry></div></body></text></TEI>"""


def chapter():
    """Two Ge'ez verses of three words, a Greek chapter of four words over two verses, one link each."""
    geez = [{'id': str(100 + i), 'c': 1, 'v': 1 + i // 3, 'label': '', 'p': i % 3, 't': f'ge{i}'} for i in range(6)]
    greek = [{'id': str(200 + i), 'c': 1, 'v': 1 + i // 2, 'label': '', 'p': i % 2, 't': f'gr{i}', 'lemma': f'l{i}'}
             for i in range(4)]
    return {'book': 'GEN', 'ordinal': 1, 'chapter': 1, 'greek_text': 'GRCBRENT', 'english_text': 'BRENTON',
            'geez': geez, 'greek': greek,
            'links': [{'ge': '100', 'gr': '200', 'conf': 0.9}, {'ge': '104', 'gr': '203', 'conf': 0.5}],
            'english': [{'c': 1, 'v': 1, 'label': '', 't': 'one'}, {'c': 1, 'v': 2, 'label': '', 't': 'two'}],
            'dillmann': {'101': {'headword': 'ሀ', 'latin': ['x'], 'greek': [], 'via': 'form'}}}


def word(key, greek=(), lemma='ምድር', source='model'):
    return {'w': key, 'lemma': lemma, 'lemma_source': source, 'sense_en': 'earth', 'sense_uk': 'земля',
            'en': 'the earth', 'uk': 'землю', 'greek': list(greek), 'morph': '', 'conf': 'high'}


class TeiTests(unittest.TestCase):
    def test_senses_keep_dillmanns_numbering_and_leave_out_cognates_and_traces(self):
        senses = gb.dillmann_senses(ET.fromstring(TEI_ENTRY))
        self.assertEqual([s['n'] for s in senses], ['main', '1', '1a', '2'])
        self.assertEqual(senses[0]['la'], [])                  # the bracketed Arabic gloss is not Dillmann's Ge'ez
        self.assertEqual(senses[1]['la'], ['tegumentum'])
        self.assertEqual(senses[1]['grc'], ['σκέπη'])
        self.assertEqual(senses[1]['refs'], ['Job 24,8', 'Jes. 4,6'])
        self.assertNotIn(';;', senses[1]['note'])
        self.assertEqual(senses[2]['la'], ['velum'])
        self.assertFalse(any('cover' in s['note'] for s in senses))   # TraCES' English senses are not read,
        self.assertNotIn('L', [s['n'] for s in senses])                # nor Leslau's nested inside Dillmann's

    def test_a_meaning_stated_in_running_latin_is_asked_with_empty_glosses(self):
        senses = gb.dillmann_senses(ET.fromstring(TEI_ENTRY))
        item = gb.dillmann_item({'headword': 'ክዳን', 'forms': ['ክዳን'], 'senses': senses}, 'e1')
        self.assertEqual([s['n'] for s in item['senses']], ['1', '1a', '2'])
        self.assertEqual(item['senses'][2]['la'], [])

    def test_groups_respect_the_entry_and_token_limits(self):
        entries = [{'headword': 'ሀ', 'forms': ['ሀ'], 'senses': [{'n': 'main', 'la': ['x' * 30], 'grc': [], 'note': ''}]}] * 70
        groups = gb.group_dillmann(entries, max_entries=30, max_tokens=10_000)
        self.assertEqual([len(g) for g in groups], [30, 30, 10])
        self.assertTrue(all(len(g) < 30 for g in gb.group_dillmann(entries, max_entries=30, max_tokens=200)))


class GlossBuildTests(unittest.TestCase):
    def test_windows_are_whole_verses(self):
        windows = gb.passages(chapter(), max_words=4)
        self.assertEqual([[k for k, _ in w] for w in windows], [[(1, 1, '')], [(1, 2, '')]])

    def test_packet_keys_alignment_dillmann_and_greek_span(self):
        packet, wkeys, gkeys = gb.gloss_packet(chapter(), gb.passages(chapter(), max_words=4)[0])
        self.assertEqual(wkeys, {'w1': '100', 'w2': '101', 'w3': '102'})
        words = packet["ge'ez"][0]['words']
        self.assertEqual(words[0]['aligned'], ['g1'])
        self.assertEqual(words[1]['dillmann']['headword'], 'ሀ')
        self.assertEqual([v['ref'] for v in packet['greek']], ['1:1'])
        self.assertEqual(gkeys, {'g1': '200', 'g2': '201'})
        self.assertEqual(packet['english'], [{'ref': '1:1', 't': 'one'}])

    def test_build_lays_requests_out_for_the_cache_and_maps_every_key(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder) / 'run'
            gb.write_jsonl(run / 'inputs' / 'gloss-chapters.jsonl', [chapter()])
            manifest = gb.build(run, 'gloss', claude_api, 'claude-sonnet-5-5', 'high')
            requests = gb.read_jsonl(run / 'requests.jsonl')
            self.assertEqual(manifest['requests'], 1)
            params = requests[0]['params']
            self.assertEqual(params['output_config']['effort'], 'high')
            self.assertEqual(params['output_config']['format']['type'], 'json_schema')
            self.assertNotIn('temperature', params)
            self.assertEqual(params['system'][-1].get('cache_control'), {'type': 'ephemeral'})
            self.assertEqual(len(manifest['items'][requests[0]['custom_id']]['words']), 6)


class ValidationTests(unittest.TestCase):
    item = {'words': {'w1': '100', 'w2': '101'}, 'greek': {'g1': '200', 'g2': '201'}}

    def test_a_complete_answer_passes(self):
        rows, errors, warnings = gb.validate_gloss({'words': [word('w1', ['g1']), word('w2')]}, self.item)
        self.assertEqual(errors, [])
        self.assertEqual(set(rows), {'w1', 'w2'})

    def test_every_word_once_and_greek_from_the_packet(self):
        for answer, needle in [({'words': [word('w1')]}, 'not answered'),
                               ({'words': [word('w1'), word('w1'), word('w2')]}, 'twice'),
                               ({'words': [word('w1', ['g9']), word('w2')]}, 'not in the packet'),
                               ({'words': [word('w1'), word('w2'), word('w3')]}, 'unknown word'),
                               ({'words': [word('w1', ['g1', 'g1']), word('w2')]}, 'a Greek key twice'),
                               ({'nope': []}, 'is not')]:
            rows, errors, _ = gb.validate_gloss(answer, self.item)
            self.assertEqual(rows, {})
            self.assertTrue(any(needle in e for e in errors), (needle, errors))

    def test_a_dillmann_lemma_not_in_his_lexicon_is_a_warning(self):
        answer = {'words': [word('w1', source='dillmann', lemma='ኀሰሰ'), word('w2', source='dillmann')]}
        rows, errors, warnings = gb.validate_gloss(answer, self.item, frozenset({'ምድር'}))
        self.assertEqual(errors, [])
        self.assertEqual(len(warnings), 1)

    def test_dillmann_senses_exactly_once_and_free_senses_may_be_empty(self):
        items = {'e1': {'entry': 'L1', 'headword': 'ሀ', 'senses': ['1', '2'], 'free': ['2']}}
        sense = {'n': '1', 'en': 'cover', 'uk': 'покриття', 'de': 'Decke', 'es': 'cubierta'}
        empty = {'n': '2', 'en': '', 'uk': '', 'de': '', 'es': ''}
        rows, errors = gb.validate_dillmann({'entries': [{'id': 'e1', 'senses': [sense, empty]}]}, items)
        self.assertEqual(errors, [])
        rows, errors = gb.validate_dillmann({'entries': [{'id': 'e1', 'senses': [sense]}]}, items)
        self.assertTrue(errors)
        rows, errors = gb.validate_dillmann({'entries': [{'id': 'e1', 'senses': [dict(sense, de=''), empty]}]}, items)
        self.assertTrue(any('empty rendering' in e for e in errors))
        rows, errors = gb.validate_dillmann({'entries': [{'id': 'e1', 'senses': [dict(sense, uk='pаnувати'), empty]}]}, items)
        self.assertTrue(any('mixes Latin and Cyrillic' in e for e in errors))
        entry_errors = []
        rows, errors = gb.validate_dillmann({'entries': [{'id': 'e1', 'senses': [dict(sense, uk='pаnувати'), empty]}]},
                                            items, entry_errors)
        self.assertEqual((rows, errors, len(entry_errors)), ({}, [], 1))   # only this entry is asked again
        rows, errors = gb.validate_dillmann({'entries': []}, items)
        self.assertTrue(any('not answered' in e for e in errors))


class SubmitTests(unittest.TestCase):
    def test_a_request_is_not_sent_twice_unless_it_was_rejected_or_forced(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder)
            requests = [{'custom_id': f'r{i}', 'params': {}} for i in range(5)]
            gb.append_jsonl(run / 'submissions.jsonl', {'batch': 'b1', 'custom_ids': ['r0', 'r1']})
            self.assertEqual([r['custom_id'] for r in gb.choose(run, requests, take_next=2)], ['r2', 'r3'])
            with self.assertRaises(SystemExit):
                gb.choose(run, requests, ids=['r1'])
            self.assertEqual(len(gb.choose(run, requests, ids=['r1'], force=True)), 1)
            gb.append_jsonl(run / 'rejects.jsonl', {'custom_id': 'r1', 'why': ['x']})
            self.assertEqual([r['custom_id'] for r in gb.choose(run, requests)], ['r1', 'r2', 'r3', 'r4'])

    def test_the_cap_leaves_every_geez_label_together_inside_the_budget(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder) / 'geez-gloss'
            gb.write_json(run / 'manifest.json', {'run': 'gloss', 'requests_sha256': 'h'})
            gb.append_jsonl(Path(folder) / 'geez-other' / 'submissions.jsonl',
                            {'batch': 'b0', 'custom_ids': ['x'], 'reserve_usd': 2.5})
            ledger = [{'at': '2026-11-16T10:00:00+00:00', 'label': 'geez-dillmann', 'usd': 30.0},
                      {'at': '2026-11-16T11:00:00+00:00', 'label': 'geez-gloss', 'usd': 5.0},
                      {'at': '2026-11-15T11:00:00+00:00', 'label': 'geez-old', 'usd': 99.0},
                      {'at': '2026-11-16T11:00:00+00:00', 'label': 'known-for', 'usd': 50.0}]
            sent = {}
            client = SimpleNamespace(
                ledger_rows=lambda: ledger,
                spent=lambda label: sum(r['usd'] for r in ledger if r['label'] == label),
                batch_plan=lambda pairs: {'reserve_usd': 1.0},
                batch_submit=lambda pairs, label, cap_usd: sent.update(label=label, cap=cap_usd) or 'batch-1')
            info = gb.submit(run, client, 125, '2026-11-16T00:00:00+00:00', [{'custom_id': 'r0', 'params': {}}])
            self.assertEqual(info['remaining_usd'], 87.5)        # 125 - 35 spent - 2.5 still in flight
            self.assertEqual(sent, {'label': 'geez-gloss', 'cap': 92.5})
            with self.assertRaises(SystemExit):     # not collected yet
                gb.submit(run, client, 125, '2026-11-16T00:00:00+00:00', [{'custom_id': 'r1', 'params': {}}])


class CollectTests(unittest.TestCase):
    def test_fake_answers_become_clean_lines_and_rejects(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder) / 'run'
            gb.write_jsonl(run / 'inputs' / 'gloss-chapters.jsonl', [chapter()])
            gb.build(run, 'gloss', claude_api, 'claude-sonnet-5-5', 'high')
            cid = gb.read_jsonl(run / 'requests.jsonl')[0]['custom_id']
            answer = {'words': [word(f'w{i}', ['g1'] if i == 1 else []) for i in range(1, 7)]}
            rows = [{'custom_id': cid, 'type': 'succeeded', 'model': 'claude-opus-5-5', 'stop_reason': 'end_turn',
                     'text': json.dumps(answer)}]
            fake = Path(folder) / 'fake.jsonl'
            gb.write_jsonl(fake, rows)
            report = gb.collect(run, fake=fake)
            self.assertEqual(report['clean_items'], 0)            # a fallback model's answer is refused
            rows[0]['model'] = 'claude-sonnet-5-5'
            gb.write_jsonl(fake, rows)
            report = gb.collect(run, fake=fake)
            self.assertEqual((report['clean_items'], report['rejected_requests']), (6, 0))
            first = gb.read_jsonl(run / 'clean.jsonl')[0]
            self.assertEqual((first['word_id'], first['greek_word_ids']), ('100', ['200']))

    def test_a_latin_twin_inside_a_ukrainian_word_is_written_cyrillic_and_kept_visible(self):
        # PRB-1796: 'оливa' and 'Божe' (Latin a and e) cost a whole request each; 'nахиляти' has no twin for n.
        self.assertEqual(gb.cyrillic_twins('оливa і Божe, not Latin'),
                         ('олива і Боже, not Latin', [('оливa', 'олива'), ('Божe', 'Боже')]))
        self.assertEqual(gb.cyrillic_twins('nахиляти'), ('nахиляти', []))
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder) / 'run'
            gb.write_jsonl(run / 'inputs' / 'gloss-chapters.jsonl', [chapter()])
            gb.build(run, 'gloss', claude_api, 'claude-sonnet-5-5', 'high')
            cid = gb.read_jsonl(run / 'requests.jsonl')[0]['custom_id']
            words = [word(f'w{i}') for i in range(1, 7)]
            words[0]['sense_uk'] = 'оливa'
            fake = Path(folder) / 'fake.jsonl'
            row = {'custom_id': cid, 'type': 'succeeded', 'model': 'claude-sonnet-5-5', 'stop_reason': 'end_turn',
                   'text': json.dumps({'words': words})}
            gb.write_jsonl(fake, [row])
            report = gb.collect(run, fake=fake)
            self.assertEqual((report['clean_items'], report['rejected_requests'], report['latin_letters_written_cyrillic']),
                             (6, 0, 1))
            first = gb.read_jsonl(run / 'clean.jsonl')[0]
            self.assertEqual((first['sense_uk'], first['fixed']), ('олива', [{'field': 'sense_uk', 'from': 'оливa', 'to': 'олива'}]))
            self.assertNotIn('_row', first)
            resent = dict(row, text=json.dumps({'words': [word(f'w{i}') for i in range(1, 7)]}))
            gb.write_jsonl(fake, [row, resent])
            gb.collect(run, fake=fake)
            first = gb.read_jsonl(run / 'clean.jsonl')[0]
            self.assertEqual((first['sense_uk'], 'fixed' in first), ('земля', False))   # the answer that needed no mapping
            words[0]['uk'] = 'nахиляти'
            gb.write_jsonl(fake, [dict(row, text=json.dumps({'words': words}))])
            gb.write_jsonl(run / 'raw' / 'fake.jsonl', [])
            report = gb.collect(run, fake=fake)
            self.assertEqual((report['clean_items'], report['rejected_requests']), (0, 1))



class ShortKeyTests(unittest.TestCase):
    def test_the_long_prompt_is_v1_byte_for_byte_and_the_short_one_names_every_key(self):
        # The pilot's runs (geez-gloss-pilot-*-v1) were built from this exact text.
        self.assertEqual(gb.digest(gb.prompts.GLOSS_SYSTEM),
                         '174fb4db519e40995213f7577560e8479cef77130f52d4a07698cf75815d743b')
        short = gb.prompts.gloss_system('short')
        for shown in ('"l" (lemma)', '"ls" (lemma source)', '"se" (sense en)', '"g" (greek)', '"c" (conf)',
                      '"h" (high)', '"d" (dillmann)'):
            self.assertIn(shown, short)
        self.assertEqual(set(gb.prompts.gloss_schema('short')['properties']['words']['items']['properties']),
                         {'w', 'l', 'ls', 'se', 'su', 'e', 'u', 'g', 'mo', 'c'})

    def test_a_short_answer_is_checked_and_stored_under_the_long_names(self):
        with tempfile.TemporaryDirectory() as folder:
            run = Path(folder) / 'run'
            gb.write_jsonl(run / 'inputs' / 'gloss-chapters.jsonl', [chapter()])
            manifest = gb.build(run, 'gloss', claude_api, 'claude-opus-5-5', 'medium', keys='short', out_per_word=150,
                                min_out=4000)
            self.assertEqual((manifest['keys'], manifest['prompt_version']), ('short', 'geez-v1-short'))
            params = gb.read_jsonl(run / 'requests.jsonl')[0]['params']
            self.assertEqual((params['max_tokens'], params['output_config']['effort']), (4000, 'medium'))
            cid = gb.read_jsonl(run / 'requests.jsonl')[0]['custom_id']
            answer = {'words': [{'w': f'w{i}', 'l': 'ምድር', 'ls': 'd', 'se': 'earth', 'su': 'земля', 'e': 'the earth',
                                 'u': 'землю', 'g': ['g1'] if i == 1 else [], 'mo': '', 'c': 'm'} for i in range(1, 7)]}
            fake = Path(folder) / 'fake.jsonl'
            gb.write_jsonl(fake, [{'custom_id': cid, 'type': 'succeeded', 'model': 'claude-opus-5-5',
                                   'stop_reason': 'end_turn', 'text': json.dumps(answer)}])
            report = gb.collect(run, fake=fake)
            self.assertEqual((report['clean_items'], report['rejected_requests']), (6, 0))
            first = gb.read_jsonl(run / 'clean.jsonl')[0]
            self.assertEqual((first['lemma'], first['lemma_source'], first['conf'], first['greek_word_ids']),
                             ('ምድር', 'dillmann', 'medium', ['200']))


class ExportTests(unittest.TestCase):
    def test_a_whole_book_split_by_chapter_equals_the_per_chapter_statement(self):
        rows = {'geez': [{'id': '1', 'c': 1, 'v': 1}, {'id': '2', 'c': 2, 'v': 1}],
                'links': [{'ge': '1', 'gr': '11'}, {'ge': '2', 'gr': '12'}],
                'greek': [{'id': '10', 'c': 1, 'v': 1}, {'id': '11', 'c': 1, 'v': 2}, {'id': '12', 'c': 1, 'v': 3},
                          {'id': '13', 'c': 2, 'v': 1}],
                'english': [{'c': 1, 'v': 1}, {'c': 1, 'v': 2}, {'c': 1, 'v': 3}, {'c': 2, 'v': 1}]}
        two = gb.split_book(rows, 2)
        self.assertEqual([w['id'] for w in two['geez']], ['2'])
        self.assertEqual([g['id'] for g in two['greek']], ['12', '13'])     # its chapter, and what its words link to
        self.assertEqual(two['english'], [{'c': 1, 'v': 3}, {'c': 2, 'v': 1}])

    def test_only_matthew_to_revelation_read_nestle(self):
        self.assertEqual(gb.chapter_sources(40), ('NESTLE1904', 'KJV'))
        self.assertEqual(gb.chapter_sources(66), ('NESTLE1904', 'KJV'))
        for ordinal in (1, 39, 67, 70, 91, 92):
            self.assertEqual(gb.chapter_sources(ordinal), ('GRCBRENT', 'BRENTON'))

    def test_books_with_no_greek_read_an_english_made_without_it(self):
        self.assertEqual(gb.chapter_sources(69), (None, 'KJV'))
        self.assertEqual(gb.chapter_sources(85), (None, gb.CHARLES))
        self.assertEqual(gb.chapter_sources(86), (None, gb.CHARLES))
        for ordinal in (87, 88, 89, 90):
            self.assertEqual(gb.chapter_sources(ordinal), (None, None))

    def test_a_book_with_no_greek_splits_its_english_by_its_own_chapters(self):
        rows = {'geez': [{'id': '1', 'c': 4, 'v': 2}], 'links': [], 'greek': [],
                'english': [{'c': 3, 'v': 9}, {'c': 4, 'v': 1}, {'c': 4, 'v': 14}, {'c': 5, 'v': 1}]}
        self.assertEqual(gb.split_book(rows, 4)['english'], [{'c': 4, 'v': 1}, {'c': 4, 'v': 14}])

    def test_charles_is_read_a_verse_either_side_and_said_to_be_from_the_ethiopic(self):
        ch = chapter()
        ch.update(greek=[], links=[], english_margin=1, english_from='from the Ethiopic',
                  english=[{'c': 1, 'v': v, 'label': '', 't': f'e{v}'} for v in (1, 2, 3, 4)])
        packet, _, gkeys = gb.gloss_packet(ch, gb.passages(ch, max_words=4)[1])
        self.assertEqual(packet['greek'], [])
        self.assertEqual(gkeys, {})
        self.assertEqual([e['ref'] for e in packet['english']], ['1:1', '1:2', '1:3'])
        self.assertEqual(packet['english_from'], 'from the Ethiopic')


VERSE_MAP = """# head
91	1:1	20	1:1	0.9
91	1:2-3	20	1:2	0.9
91	2:1	20	30:1;30:2	0.9
92	1:1	20	25:1	0.9
76	1:1	76	1:1	0.9
"""


def proverbs_rows():
    geez = [{'id': str(i), 'c': c, 'v': v, 'label': '', 'p': 1, 't': 'ge'} for i, (c, v) in
            enumerate([(1, 1), (1, 2), (1, 3), (2, 1)])]
    def g(i, c, v, label, fc, fv):
        return {'id': str(100 + i), 'c': c, 'v': v, 'label': label, 'p': 1, 't': f'gr{i}', 'lemma': '',
                'fc': fc, 'fv': fv}
    greek = [g(0, 1, 1, '', 1, 1), g(1, 1, 2, '', 1, 2), g(2, 24, 22, 'f', 30, 1), g(3, 24, 22, 'g', 30, 2),
             g(4, 24, 23, '', 24, 23)]
    english = [{'c': x['c'], 'v': x['v'], 'label': x['label'], 'fc': x['fc'], 'fv': x['fv'], 't': 'en'} for x in greek]
    return {'geez': geez, 'links': [], 'greek': greek, 'english': english}


class ProverbsTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.path = Path(self.folder.name) / 'map.tsv'
        self.path.write_text(VERSE_MAP, encoding='utf-8')

    def tearDown(self):
        self.folder.cleanup()

    def test_the_map_reads_ranges_and_several_rows_for_messale_and_tagsas_only(self):
        verse_map = gb.read_verse_map(self.path)
        self.assertEqual(verse_map[(91, 1, 3)], [(1, 2)])
        self.assertEqual(verse_map[(91, 2, 1)], [(30, 1), (30, 2)])
        self.assertEqual(verse_map[(92, 1, 1)], [(25, 1)])
        self.assertNotIn((76, 1, 1), verse_map)

    def test_proverbs_stands_at_the_messale_verse_its_frame_row_answers_and_keeps_its_own_address(self):
        placed = gb.place_proverbs(91, proverbs_rows(), gb.read_verse_map(self.path))
        self.assertEqual([(g['c'], g['v'], g['ref']) for g in placed['greek']],
                         [(1, 1, '1:1'), (1, 2, '1:2'), (2, 1, '24:22f'), (2, 1, '24:22g')])
        self.assertEqual([e['ref'] for e in placed['english']], ['1:1', '1:2', '24:22f', '24:22g'])
        self.assertNotIn('fc', placed['greek'][0])
        packet, _, _ = gb.gloss_packet({**proverbs_rows(), **placed, 'book': 'Msl', 'dillmann': {}},
                                       [((2, 1, ''), [placed['geez'][3]])])
        self.assertEqual([v['ref'] for v in packet['greek']], ['24:22f', '24:22g'])

    def test_a_messale_verse_the_map_lacks_stops_the_export(self):
        rows = proverbs_rows()
        rows['geez'].append({'id': '9', 'c': 3, 'v': 1, 'label': '', 'p': 1, 't': 'ge'})
        with self.assertRaises(SystemExit):
            gb.place_proverbs(91, rows, gb.read_verse_map(self.path))

    def test_a_messale_verse_whose_rows_hold_no_loaded_verse_is_named_unanswered(self):
        rows = proverbs_rows()
        rows['greek'] = [g for g in rows['greek'] if g['fv'] != 2]
        placed = gb.place_proverbs(91, rows, gb.read_verse_map(self.path))
        # 1:2-3 both read 1:2, which this Greek lacks; 2:1 still has 30:1 when 30:2 is gone.
        self.assertEqual(placed['unanswered'], [[1, 2], [1, 3]])

    def test_the_checked_in_map_names_as_many_verses_as_geez81_loads_of_messale_and_tagsas(self):
        # 766 and 161 verses, counted in the loaded GEEZ81 on 2026-11-16; the export checks the verses themselves.
        verse_map = gb.read_verse_map()
        self.assertEqual(len([k for k in verse_map if k[0] == 91]), 766)
        self.assertEqual(len([k for k in verse_map if k[0] == 92]), 161)


if __name__ == '__main__':
    unittest.main()
