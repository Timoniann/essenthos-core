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


if __name__ == '__main__':
    unittest.main()
