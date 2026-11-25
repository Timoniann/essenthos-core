"""The hand review on a small made-up text: sheets, chunks, the answer check, the results, limits and conversion.

Run: python -m unittest discover -s Resources/Essenthos/hand-review -p "test_*.py"
"""
import csv
import json
import tempfile
import unittest
from datetime import datetime
from pathlib import Path
from unittest import mock

import convert
import export
import review
from common import ResultTally, TextFolder, read_result

SETTINGS = {'name': 'a test Bible', 'title': 'Test', 'language': 'Testish', 'pass': 'test pass',
            'targets': {'ot': 'BHSA', 'nt': 'NESTLE1904'}, 'notes': ['A note.']}

# id, book, abbr, chapter, number, text
VERSES = [(1, 1, 'Gen', 1, '1', 'TST'), (2, 1, 'Gen', 1, '2', 'TST'), (3, 1, 'Gen', 1, '3', 'TST'),
          (4, 1, 'Gen', 2, '1', 'TST'), (5, 40, 'Matt', 1, '1', 'TST'),
          (11, 1, 'Gen', 1, '1', 'BHSA'), (12, 1, 'Gen', 1, '2', 'BHSA'), (13, 1, 'Gen', 1, '3', 'BHSA'),
          (14, 1, 'Gen', 2, '1', 'BHSA'), (15, 1, 'Gen', 2, '2', 'BHSA'), (21, 40, 'Matt', 1, '1', 'NESTLE1904')]
# id, verse, position, text, gloss, strong, elided
WORDS = [(100, 1, 1, 'На', '', '', 'f'), (101, 1, 2, 'початку', '', '', 'f'), (102, 1, 3, 'Бог', '', '', 'f'),
         (110, 11, 1, 'בְּ', 'in', 'H9003', 'f'), (111, 11, 2, 'רֵאשִׁית', 'beginning', 'H7225', 'f'),
         (112, 11, 3, 'אֱלֹהִים', 'God', 'H430', 'f'),
         (200, 2, 1, 'А', '', '', 'f'), (210, 12, 1, 'וְ', 'and', 'H9001', 'f'),
         (300, 3, 1, 'Сказав', '', '', 'f'), (310, 13, 1, 'וַיֹּאמֶר', 'said', 'H559', 'f'),
         (400, 4, 1, 'Скінчились', '', '', 'f'), (410, 14, 1, 'וַיְכֻלּוּ', 'finished', 'H3615', 'f'),
         (411, 15, 1, 'וַיְכַל', 'finished', 'H3615', 'f'),
         (500, 5, 1, 'Родовід', '', '', 'f'), (510, 21, 1, 'Βίβλος', 'book', 'G976', 'f')]
# link id, target, method, confidence, relation, side, word
LINKS = [(1, 'BHSA', 'aligner', '0.90', 'renders', 'from', 100), (1, 'BHSA', 'aligner', '0.90', 'renders', 'from', 101),
         (1, 'BHSA', 'aligner', '0.90', 'renders', 'to', 111),
         (2, 'BHSA', 'aligner', '0.80', 'renders', 'from', 102), (2, 'BHSA', 'aligner', '0.80', 'renders', 'to', 112),
         (3, 'NESTLE1904', 'aligner', '0.70', 'renders', 'from', 500),
         (3, 'NESTLE1904', 'aligner', '0.70', 'renders', 'to', 510)]
FRAME = [(1, 11), (2, 12), (3, 13), (4, 14), (4, 15), (5, 21)]


def write(path, rows):
    with open(path, 'w', encoding='utf-8', newline='') as handle:
        csv.writer(handle, delimiter='\t', lineterminator='\n').writerows(rows)


class World(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name) / 'hand-review'
        self.store = Path(temp.name) / 'store'
        (self.root / 'TST').mkdir(parents=True)
        (self.root / 'TST' / 'text.json').write_text(json.dumps(SETTINGS), encoding='utf-8')
        self.text = TextFolder('TST', root=self.root, store=self.store)
        self.text.export.mkdir(parents=True)
        write(self.text.export / 'verses.tsv', VERSES)
        write(self.text.export / 'words.tsv', WORDS)
        write(self.text.export / 'links.tsv', LINKS)
        write(self.text.export / 'frame.tsv', FRAME)
        self.corpus = export.Corpus(self.text.export, 'TST')
        self.blocks = export.sheet_blocks(self.text, self.corpus)

    def result(self, name, lines):
        self.text.results.mkdir(parents=True, exist_ok=True)
        (self.text.results / name).write_text('kind\tfrom\tto\trelation\treason\n' + ''.join(l + '\n' for l in lines),
                                              encoding='utf-8')


class SheetTests(World):
    def test_a_verse_shows_its_frame_its_links_and_what_an_answer_may_name(self):
        block = self.blocks['1.1:1']
        self.assertIn('frame: BHSA 1.1:1', block['text'])
        self.assertIn('1,2 => 2 [aligner 0.90 renders]  «На початку» = רֵאשִׁית', block['text'])
        self.assertIn('unlinked: -', block['text'])
        self.assertEqual(block['pairs'], [['1.1:1.1', '1.1:1.2'], ['1.1:1.2', '1.1:1.2'], ['1.1:1.3', '1.1:1.3']])
        self.assertEqual(block['target'], 'BHSA')

    def test_a_verse_the_frame_maps_to_two_verses_shows_both_with_full_addresses(self):
        block = self.blocks['1.2:1']
        self.assertIn('frame: BHSA 1.2:1, BHSA 1.2:2', block['text'])
        self.assertIn('BHSA 1.2:2: 1:וַיְכַל(finished|H3615)', block['text'])
        self.assertEqual(sorted(block['targetWords']), ['1.2:1.1', '1.2:2.1'])

    def test_the_new_testament_is_read_against_its_own_original(self):
        self.assertEqual(self.blocks['40.1:1']['target'], 'NESTLE1904')


class PlanTests(World):
    def test_reviewed_verses_are_left_out_and_a_long_chapter_is_split(self):
        chunks = export.plan(self.blocks, {'1.1:2': 'ok'}, set(), max_verses=1)
        self.assertEqual([c['verses'] for c in chunks], [['1.1:1'], ['1.1:3'], ['1.2:1'], ['40.1:1']])
        self.assertEqual([c['id'] for c in chunks], ['01-Gen-001', '01-Gen-001-3', '01-Gen-002', '40-Matt-001'])

    def test_short_chapters_of_one_book_share_a_chunk_and_names_already_taken_are_not_reused(self):
        chunks = export.plan(self.blocks, {}, {'01-Gen-001'})
        self.assertEqual([c['verses'] for c in chunks], [['1.1:1', '1.1:2', '1.1:3', '1.2:1'], ['40.1:1']])
        self.assertEqual(chunks[0]['id'], '01-Gen-001-2')


class AnswerTests(World):
    def setUp(self):
        super().setUp()
        self.check = {'verses': ['1.1:1', '1.1:2'],
                      'words': self.blocks['1.1:1']['words'] + self.blocks['1.1:2']['words'],
                      'targetWords': self.blocks['1.1:1']['targetWords'] + self.blocks['1.1:2']['targetWords'],
                      'pairs': self.blocks['1.1:1']['pairs'] + self.blocks['1.1:2']['pairs']}
        self.answer = {'verses': [{'verse': '1.1:1', 'verdict': 'fixed', 'note': 'На is the prefix'},
                                  {'verse': '1.1:2', 'verdict': 'ok', 'note': ''}],
                       'refuse': [{'from': '1.1:1.1', 'to': '1.1:1.2', 'reason': 'На is בְּ'}],
                       'accept': [{'from': ['1.1:1.1'], 'to': ['1.1:1.1'], 'reason': 'На = בְּ'}],
                       'pattern': ''}

    def test_a_good_answer_passes_and_is_written_in_the_results_format(self):
        self.assertEqual(review.validate(self.answer, self.check), [])
        self.assertEqual(review.rows(self.answer).splitlines(), [
            'kind\tfrom\tto\trelation\treason', 'fixed\t1.1:1\t\t\tНа is the prefix', 'ok\t1.1:2\t\t\t',
            'refuse\t1.1:1.1\t1.1:1.2\t\tНа is בְּ', 'accept\t1.1:1.1\t1.1:1.1\trenders\tНа = בְּ'])

    def test_a_refusal_of_no_link_a_stranger_word_a_missing_verse_and_an_ok_with_corrections_are_refused(self):
        self.answer['refuse'].append({'from': '1.1:1.3', 'to': '1.1:1.1', 'reason': ''})
        self.answer['accept'].append({'from': ['1.1:3.1'], 'to': ['1.1:1.3'], 'reason': ''})
        self.answer['verses'] = [{'verse': '1.1:1', 'verdict': 'ok', 'note': ''}]
        errors = ' | '.join(review.validate(self.answer, self.check))
        self.assertIn('verses missing: 1.1:2', errors)
        self.assertIn('refuse 1.1:1.3 -> 1.1:1.1: no link on the sheet', errors)
        self.assertIn('1.1:3.1 is not a word of your verses', errors)
        self.assertIn('1.1:1 has corrections', errors)


class LimitTests(unittest.TestCase):
    def test_limits_are_told_from_other_failures(self):
        self.assertEqual(review.classify("ERROR: You've hit your usage limit. Try again at 3:05 PM."), 'limit')
        self.assertEqual(review.classify('stream error: 429 Too Many Requests'), 'rate')
        self.assertIsNone(review.classify('codex exit 1: model not found'))

    def test_the_wait_codex_names_is_read(self):
        at = datetime(2026, 11, 25, 14, 0).astimezone()
        self.assertEqual(review.wait_named('Try again in 2 hours 5 minutes.', at), 7500)
        self.assertEqual(review.wait_named('try again at 3:05 PM', at), 3900)
        self.assertEqual(review.wait_named('try again at 1:00 PM', at), 23 * 3600)
        self.assertIsNone(review.wait_named('limit reached', at))


class RunTests(World):
    def setUp(self):
        super().setUp()
        chunks = export.plan(self.blocks, {}, set(), max_verses=1)
        self.text.chunks.mkdir(parents=True)
        index = export.write_chunks(self.text, self.blocks, chunks, self.text.chunks)
        (self.text.run / 'chunks.json').write_text(json.dumps(index), encoding='utf-8')
        (self.text.run / 'export.json').write_text(json.dumps({'at': 'then', 'fingerprint': {}, 'verses': 5}),
                                                   encoding='utf-8')
        for name, value in (('refresh', lambda *a, **k: None), ('stale', lambda *a: None)):
            patcher = mock.patch.object(review.exporter, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        patcher = mock.patch.object(review, 'LAUNCH_GAP', 0)
        patcher.start()
        self.addCleanup(patcher.stop)

    def answer_for(self, chunk_id):
        check = json.loads((self.text.chunks / f'{chunk_id}.json').read_text(encoding='utf-8'))
        return {'verses': [{'verse': v, 'verdict': 'ok', 'note': ''} for v in check['verses']],
                'refuse': [], 'accept': [], 'pattern': ''}

    def test_every_chunk_is_read_on_several_lanes_and_written_whole(self):
        run = review.Review(self.text, 3, 'gpt-test', 'high')
        with mock.patch.object(review.Review, 'call', lambda me, prompt, chunk_id: (self.answer_for(chunk_id), '')):
            self.assertEqual(run.run(), 0)
        status = json.loads(self.text.status.read_text(encoding='utf-8'))
        self.assertEqual(status['state'], 'done')
        self.assertEqual(status['chunks'], {'total': 5, 'done': 5, 'failed': 0, 'running': 0, 'doneHere': 5})
        self.assertEqual(status['verses']['ok'], 5)
        self.assertEqual(len(self.text.log.read_text(encoding='utf-8').splitlines()), 6)
        self.assertEqual(list(self.text.results.glob('*.tmp')), [])

    def test_a_usage_limit_puts_the_chunk_back_and_waits_instead_of_failing_it(self):
        run = review.Review(self.text, 1, 'gpt-test', 'high')
        run.load_chunks()
        first = run.next_chunk()
        run.running[first] = 'now'
        run.settle(review.Outcome(first, 'limit', 'usage limit', said="You've hit your usage limit. Try again in 3 hours."))
        self.assertEqual(run.queue[0], first)
        self.assertEqual(run.failed, {})
        self.assertEqual(run.waiting['kind'], 'limit')
        run.settle(review.Outcome(first, 'limit', 'usage limit', said='usage limit'))
        self.assertEqual(run.limit_strikes, 1, 'a second lane meeting the same limit is not a second strike')

    def test_a_chunk_failing_three_times_is_set_aside_and_five_failures_in_a_row_pause_the_run(self):
        run = review.Review(self.text, 1, 'gpt-test', 'high')
        run.load_chunks()
        for _ in range(3):
            run.settle(review.Outcome('01-Gen-001', 'error', 'codex exit 1: boom'))
        self.assertIn('01-Gen-001', run.failed)
        self.assertIsNone(run.waiting)
        for _ in range(2):
            run.settle(review.Outcome('01-Gen-002', 'error', 'codex exit 1: boom'))
        self.assertEqual(run.waiting['kind'], 'errors')

    def test_a_chunk_whose_verses_all_have_a_verdict_is_done(self):
        self.result('older.tsv', ['ok\t1.1:2\t\t\t'])
        run = review.Review(self.text, 1, 'gpt-test', 'high')
        run.load_chunks()
        self.assertTrue(run.is_done('01-Gen-001-2'))
        self.assertFalse(run.is_done('01-Gen-001'))


class ConvertTests(World):
    def setUp(self):
        super().setUp()
        for pair in ('TST-BHSA', 'TST-NESTLE1904'):
            (self.store / 'mapping' / pair).mkdir(parents=True)
            (self.store / 'mapping' / pair / 'rulings.tsv').write_text(
                '# product x\naction\tfrom\tto\trelation\tsource\treason\n', encoding='utf-8')
        (self.store / 'mapping' / 'TST-BHSA' / 'rulings.tsv').write_text(
            'action\tfrom\tto\trelation\tsource\treason\naccept\t1.1:3.1\t1.1:3.1\trenders\t\tearlier\n',
            encoding='utf-8')
        self.links = convert.Links(self.corpus, self.text)

    def test_a_refusal_of_part_of_a_link_refuses_its_shape_and_accepts_the_rest(self):
        self.result('a.tsv', ['fixed\t1.1:1\t\t\tx', 'refuse\t1.1:1.1\t1.1:1.2\t\tНа is the prefix',
                              'accept\t1.1:1.1\t1.1:1.1\trenders\tНа = בְּ'])
        log = {'a.tsv': {'at': '2026-11-26T10:00:00+00:00', 'model': 'gpt-6.2-sol', 'effort': 'high'}}
        new, dropped, stale, per_file, verdicts = convert.convert(
            self.text, self.links, [self.text.results / 'a.tsv'], log, '2026-11-26')
        prefix = 'Codex review 2026-11-26 (gpt-6.2-sol high), test pass: '
        self.assertEqual(new['BHSA'], [
            ['refuse', '1.1:1.1 1.1:1.2', '1.1:1.2', '', '', prefix + 'На is the prefix'],
            ['accept', '1.1:1.2', '1.1:1.2', 'renders', '', prefix + 'На is the prefix (the rest of a partly wrong link)'],
            ['accept', '1.1:1.1', '1.1:1.1', 'renders', '', prefix + 'На = בְּ']])
        self.assertEqual(verdicts['fixed'], 1)
        self.assertEqual(stale, [])

    def test_a_refusal_of_a_link_gone_since_and_an_accept_of_a_word_gone_since_are_dropped_and_reported(self):
        self.result('b.tsv', ['refuse\t1.1:1.3\t1.1:1.1\t\tgone', 'accept\t1.1:1.9\t1.1:1.1\trenders\tgone',
                              'accept\t1.1:3.1\t1.1:3.1\trenders\talready pinned'])
        new, dropped, stale, _, _ = convert.convert(self.text, self.links, [self.text.results / 'b.tsv'], {}, 'today')
        self.assertEqual(new['BHSA'], [])
        self.assertEqual(dropped['refuse: no such link now'], 1)
        self.assertEqual(dropped['accept: a word is not in the database now'], 1)
        self.assertEqual(dropped['accept: over a word an earlier accept pins'], 1)
        self.assertEqual(len(stale), 2)

    def test_the_tally_counts_the_verdicts_on_disk(self):
        self.result('c.tsv', ['ok\t1.1:1\t\t\t', 'unsure\t1.1:2\t\t\twhy', 'accept\t1.1:2.1\t1.1:2.1\trenders\tx'])
        tally = ResultTally(self.text).read()
        self.assertEqual(tally['reviewed'], {'1.1:1': 'ok', '1.1:2': 'unsure'})
        self.assertEqual(tally['counts']['accept'], 1)
        self.assertEqual(len(read_result(self.text.results / 'c.tsv')), 3)


if __name__ == '__main__':
    unittest.main()
