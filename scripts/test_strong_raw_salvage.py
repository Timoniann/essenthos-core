import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('finish', Path(__file__).with_name('finish-strong-de-codex.py'))
finish = importlib.util.module_from_spec(spec)
spec.loader.exec_module(finish)


class RawSalvageTests(unittest.TestCase):
    def test_valid_objects_are_exact_substrings_around_a_broken_object(self):
        first = json.dumps({'strong_number': 'G1', 'definition': 'Anfang'}, ensure_ascii=False)
        second = '{"strong_number":"G2","definition":"„unescaped" quote"}'
        third = json.dumps({'strong_number': 'G3', 'definition': 'Ende'}, ensure_ascii=False)
        text = '```json\n[' + first + ',\n' + second + ',\n' + third + ']\n```'
        rows = finish.exact_entry_fragments(text, {'G1', 'G2', 'G3'}, ('definition',))
        self.assertEqual([r['answer']['strong_number'] for r in rows], ['G1', 'G3'])
        for row in rows:
            exact = text[row['start']:row['end']]
            self.assertEqual(json.loads(exact), row['answer'])
            self.assertEqual(hashlib.sha256(exact.encode('utf-8')).hexdigest(), row['sha256'])

    def test_duplicates_including_a_malformed_duplicate_are_refused(self):
        row = json.dumps({'strong_number': 'G1', 'definition': 'Anfang'})
        for text in ('[' + row + ',' + row + ']',
                     '[' + row + ',{"strong_number":"G1","definition":bad}]'):
            self.assertFalse(finish.exact_entry_fragments(text, {'G1'}, ('definition',)))

    def test_only_expected_ids_and_entry_fields_are_candidates(self):
        text = '[{"strong_number":"G1","definition":"gut"},' \
               '{"strong_number":"G9","definition":"fremd"},' \
               '{"strong_number":"G2","definition":"Metadaten","extra":1}]'
        rows = finish.exact_entry_fragments(text, {'G1', 'G2'}, ('definition',))
        self.assertEqual([r['answer']['strong_number'] for r in rows], ['G1'])

    def test_duplicate_field_keys_are_not_silently_collapsed(self):
        text = '[{"strong_number":"G1","definition":"eins","definition":"zwei"}]'
        self.assertFalse(finish.exact_entry_fragments(text, {'G1'}, ('definition',)))

    def test_prefixed_pseudo_array_does_not_hide_the_real_entries(self):
        text = '{"entries":[...]}\nNow output JSON array.```json\n' \
               '[{"strong_number":"G1","definition":"Anfang"}]```'
        rows = finish.exact_entry_fragments(text, {'G1'}, ('definition',))
        self.assertEqual(rows[0]['answer']['definition'], 'Anfang')

    def test_recovery_preserves_existing_rows_and_checks_every_fragment(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            origin, run = root / 'origin', root / 'run'
            (origin / 'full/calls/run').mkdir(parents=True)
            (origin / 'full/batches').mkdir()
            (run / 'full').mkdir(parents=True)
            sources = [{'strong_number': 'G1', 'definition': 'first'},
                       {'strong_number': 'G2', 'definition': 'second'}]
            existing = {'strong_number': 'G1', 'definition': 'erster', 'model': 'new-model'}
            answers_path = run / 'full/answers.jsonl'
            answers_path.write_text(json.dumps(existing) + '\n', encoding='utf-8')
            (run / 'full/source.json').write_text(json.dumps(sources), encoding='utf-8')
            (origin / 'full/manifest.json').write_text('{"prompt_version":"original"}', encoding='utf-8')
            (origin / 'full/batches/batch-1.json').write_text(json.dumps({'entries': sources}), encoding='utf-8')
            record = {'outcome': {'result': '{"entries":[...]}\n[' +
                      json.dumps({'strong_number': 'G1', 'definition': 'old answer'}) + ',' +
                      json.dumps({'strong_number': 'G2', 'definition': 'zweiter'}) + ']',
                      'modelUsage': {'original-model': {}}},
                      'provenance': {'phase': 'ask', 'batch': 'batch-1', 'run': 'original-date'},
                      'effort': 'medium', 'returncode': 0}
            (origin / 'full/calls/run/call.json').write_text(json.dumps(record), encoding='utf-8')
            module = finish.base.module_at(Path(__file__).parent).lexicon
            (run / 'code').mkdir()
            (run / 'code/lexicon.py').write_text('# frozen fixture validator', encoding='utf-8')
            with patch.object(finish, 'RUN', run), patch.object(finish, 'ORIGIN', origin), \
                    patch.object(finish, 'PREVIOUS', root / 'previous'), \
                    patch.object(finish.base, 'frozen'), patch.object(finish.base, 'report'), \
                    patch.object(finish.base, 'module_at', return_value=SimpleNamespace(lexicon=module)):
                finish.recover()
            rows = module.read(str(answers_path))
            self.assertEqual(rows[0], existing)
            self.assertEqual(rows[1]['strong_number'], 'G2')
            self.assertEqual(rows[1]['model'], 'original-model')
            self.assertIn('recovered_raw_fragment', rows[1])


if __name__ == '__main__':
    unittest.main()
