import json
import tempfile
import unittest
from pathlib import Path

import presence_closure_context as context


class ContextTests(unittest.TestCase):
    def test_changed_frozen_snapshot_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'export.jsonl'
            path.write_text('{}\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'export changed'):
                context.source_rows(path, 'wrong', {'BHSA'})

    def test_secondary_placement_never_renumbers_printed_hebrew(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'export.jsonl'
            rows = [dict(type='verse', text='BHSA', book=19, chapter=51, verse=2,
                         label=None, canonical=[19,51,0], body='כַּאֲשֶׁר־בָּ֝א אֶל־בַּת־שָֽׁבַע׃'),
                    dict(type='placement', text='BHSA', book=19, chapter=51, verse=2,
                         label=None, canonical=[19,51,1])]
            path.write_text('\n'.join(json.dumps(row) for row in rows), encoding='utf-8')
            verses, _ = context.source_rows(path, context.sha(path), {'BHSA'})
            self.assertIn('BHSA:19:51:2', verses)
            row = verses['BHSA:19:51:2']
            self.assertEqual(row['canonicalAddresses'], [[19,51,0],[19,51,1]])
            witness = context.scripture(row, {(19,51,0)})
            self.assertEqual(witness['body'], rows[0]['body'])
            self.assertEqual(witness['role'], 'event')

    def test_missing_hebrew_is_an_explicit_gap_not_a_translation(self):
        verses = [dict(key='BSB:2:2:3', body='Nile', canonicalAddresses=[[2,2,3]])]
        result = context.diagnostic(verses, {(2,2,3)})
        self.assertEqual(result['status'], 'partial')
        self.assertEqual(result['missing_focus_addresses'], [[2,2,3]])

    def test_new_testament_does_not_acquire_hebrew(self):
        result = context.diagnostic([], {(41,15,21)})
        self.assertEqual(result['status'], 'not_applicable_new_testament')
        self.assertFalse(result['applicable'])

    def test_earlier_case_identity_and_parallel_chapter_are_retained(self):
        job = dict(focus=['KJV:14:25:28'], verses=[
            dict(key='KJV:14:25:28', canonical=[14,25,28],canonicalAddresses=[[14,25,28]]),
            dict(key='KJV:14:1:1', canonical=[14,1,1],canonicalAddresses=[[14,1,1]])])
        focus, chapters, parallels = context.selection(job)
        self.assertEqual(focus, {(14,25,28)})
        self.assertTrue({(14,24),(14,25),(14,26),(14,1),(12,14)} <= chapters)
        self.assertEqual(parallels, {(12,14)})

    def test_frozen_output_replay_and_difference_refusal(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'proof.json'
            context.freeze(path, {'body':'וַיְהִי'})
            original = path.read_bytes()
            context.freeze(path, {'body':'וַיְהִי'})
            self.assertEqual(path.read_bytes(), original)
            with self.assertRaisesRegex(ValueError, 'Frozen output differs'):
                context.freeze(path, {'body':'changed'})
            self.assertEqual(path.read_bytes(), original)

    def test_exact_remaining_case_inventory_has_no_duplicates(self):
        items = context.inventory()
        self.assertEqual(len(items), 201)
        self.assertEqual(len({row['case_id'] for _, row in items}), 201)
        self.assertEqual(sum(kind=='disagreement' for kind,_ in items), 128)
        self.assertEqual(sum(kind=='place' for kind,_ in items), 73)


if __name__ == '__main__':
    unittest.main()
