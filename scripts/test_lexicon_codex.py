import importlib.util
import json
import sys
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from test_lexicon import LexiconTests
from test_strong_raw_salvage import RawSalvageTests


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('codex_adapter', HERE / 'lexicon-codex.py')
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)
runner_spec = importlib.util.spec_from_file_location('codex_runner', HERE / 'generate-strong-codex.py')
runner = importlib.util.module_from_spec(runner_spec)
runner_spec.loader.exec_module(runner)


class CodexTransportTests(unittest.TestCase):
    def setUp(self):
        self.answer = json.dumps({'entries': [{'strong_number': 'G26', 'definition': 'Liebe'}]})
        self.stdout = json.dumps({'type': 'turn.completed', 'usage': {
            'input_tokens': 120, 'cached_input_tokens': 40, 'output_tokens': 30}})

    def test_success_keeps_usage_and_unknown_cost(self):
        result = adapter.normalize(self.stdout, self.answer, adapter.MODEL)
        self.assertIsNone(result['total_cost_usd'])
        self.assertEqual(result['usage']['output_tokens'], 30)
        self.assertEqual(json.loads(result['result'])[0]['strong_number'], 'G26')

    def test_unfinished_or_failed_turn_is_not_an_answer(self):
        for stdout in ('', json.dumps({'type': 'turn.failed', 'error': {'message': 'usage limit'}})):
            with self.assertRaises(ValueError):
                adapter.normalize(stdout, self.answer, adapter.MODEL)

    def test_any_tool_activity_fails_the_transport(self):
        tool = json.dumps({'type': 'item.completed', 'item': {'type': 'command_execution'}})
        with self.assertRaisesRegex(ValueError, 'tool activity'):
            adapter.normalize(tool + '\n' + self.stdout, self.answer, adapter.MODEL)

    def test_only_known_disabled_capability_notice_is_nonfatal(self):
        notice = {'type': 'item.completed', 'item': {'type': 'error',
                  'message': adapter.DISABLED_CODE_NOTICE + ' enable features.code_mode_host.'}}
        result = adapter.normalize(json.dumps(notice) + '\n' + self.stdout,
                                   self.answer, adapter.MODEL)
        self.assertTrue(result['completed'])
        notice['item']['message'] = 'Something else failed'
        with self.assertRaises(ValueError):
            adapter.normalize(json.dumps(notice) + '\n' + self.stdout, self.answer, adapter.MODEL)

    def test_wrong_final_shape_fails(self):
        for answer in ('[]', '{"entries":{}}', '{"entries":[],"extra":1}'):
            with self.assertRaises(ValueError):
                adapter.normalize(self.stdout, answer, adapter.MODEL)

    def test_model_substitution_refused_before_a_process_is_started(self):
        with patch.object(adapter.subprocess, 'run') as process:
            outcome, error = adapter.call('{}', 'translate', 'some-other-model')
        self.assertIsNone(outcome)
        self.assertIn('refusing model substitution', error)
        process.assert_not_called()

    def test_actual_command_pins_model_and_no_tools_and_preserves_raw(self):
        def process(command, **options):
            Path(command[command.index('--output-last-message') + 1]).write_text(
                self.answer, encoding='utf-8')
            self.assertEqual(command[command.index('--model') + 1], 'gpt-6.1-sol')
            self.assertIn('--ignore-user-config', command)
            self.assertIn('shell_tool', command)
            self.assertEqual(command[command.index('--sandbox') + 1], 'read-only')
            self.assertEqual(options['input'], b'payload')
            return SimpleNamespace(returncode=0, stdout=self.stdout.encode(), stderr=b'')
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(adapter, 'executable', return_value='codex.exe'), \
                    patch.object(adapter.subprocess, 'run', side_effect=process):
                outcome, error = adapter.call('payload', 'translate', adapter.MODEL,
                                              record_dir=directory)
            self.assertIsNone(error)
            self.assertTrue(outcome['completed'])
            record = json.loads(next(Path(directory).glob('*.json')).read_text())
            self.assertEqual(record['stdout'], self.stdout)
            self.assertIn('TRANSPORT CONTRACT', record['effective_system'])

    def test_failed_exit_still_retains_usage_in_raw_record(self):
        def process(command, **options):
            Path(command[command.index('--output-last-message') + 1]).write_text(
                self.answer, encoding='utf-8')
            return SimpleNamespace(returncode=1, stdout=self.stdout.encode(), stderr=b'failure')
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(adapter, 'executable', return_value='codex.exe'), \
                    patch.object(adapter.subprocess, 'run', side_effect=process):
                outcome, error = adapter.call('{}', 'translate', adapter.MODEL, record_dir=directory)
            self.assertIsNone(outcome)
            self.assertTrue(error)
            record = json.loads(next(Path(directory).glob('*.json')).read_text())
            self.assertEqual(record['outcome']['usage']['input_tokens'], 120)

    def test_malformed_final_json_still_retains_usage(self):
        def process(command, **options):
            Path(command[command.index('--output-last-message') + 1]).write_text('bad JSON')
            return SimpleNamespace(returncode=0, stdout=self.stdout.encode(), stderr=b'')
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(adapter, 'executable', return_value='codex.exe'), \
                    patch.object(adapter.subprocess, 'run', side_effect=process):
                outcome, error = adapter.call('{}', 'translate', adapter.MODEL, record_dir=directory)
            self.assertIsNone(outcome)
            self.assertTrue(error)
            record = json.loads(next(Path(directory).glob('*.json')).read_text())
            self.assertEqual(record['outcome']['usage']['output_tokens'], 30)

    def test_judge_has_a_judgement_schema(self):
        def process(command, **options):
            schema = json.loads(Path(command[command.index('--output-schema') + 1]).read_text())
            required = schema['properties']['entries']['items']['required']
            self.assertIn('terminology', required)
            self.assertNotIn('definition', required)
            Path(command[command.index('--output-last-message') + 1]).write_text(json.dumps({
                'entries': [{'strong_number': 'G26', 'terminology': 5,
                             'completeness': 5, 'fluency': 5, 'errors': []}]}))
            return SimpleNamespace(returncode=0, stdout=self.stdout.encode(), stderr=b'')
        with patch.object(adapter, 'executable', return_value='codex.exe'), \
                patch.object(adapter.subprocess, 'run', side_effect=process):
            outcome, error = adapter.call('{}', 'judge', adapter.MODEL,
                                          provenance={'phase': 'judge'})
        self.assertIsNone(error)
        self.assertEqual(json.loads(outcome['result'])[0]['terminology'], 5)

    def test_timeout_is_retained_without_a_success_result(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(adapter, 'executable', return_value='codex.exe'), \
                    patch.object(adapter.subprocess, 'run', side_effect=subprocess.TimeoutExpired(
                        'codex', 900, output=b'partial', stderr=b'timeout')):
                outcome, error = adapter.call('{}', 'translate', adapter.MODEL, record_dir=directory)
            self.assertIsNone(outcome)
            self.assertIn('timed out', error)
            record = json.loads(next(Path(directory).glob('*.json')).read_text())
            self.assertEqual(record['stdout'], 'partial')


class SpanishValidationTests(unittest.TestCase):
    def test_same_name_needs_a_verified_spelling_and_name_source(self):
        source = {'strong_number': 'H85', 'morphology': 'n-pr-m',
                  'detailed_definition': 'Abraham = father of a multitude'}
        self.assertTrue(adapter.lexicon.shared_spelling(source, 'Abraham.', 'es'))
        source['morphology'] = 'verb'
        self.assertFalse(adapter.lexicon.shared_spelling(source, 'Abraham.', 'es'))

    def test_english_unaccented_spellings_are_not_spanish_exceptions(self):
        source = {'strong_number': 'G2424', 'transliteration': 'Jesus', 'morphology': 'n-pr-m'}
        for word in ('Jesus', 'amen', 'Aaron', 'Alpha', 'Satan', 'Adam'):
            self.assertFalse(adapter.lexicon.shared_spelling(source, word, 'es'))

    def test_loanword_is_bound_to_the_correct_entry_and_whole_field(self):
        source = {'strong_number': 'G5598'}
        self.assertTrue(adapter.lexicon.shared_spelling(source, 'omega.', 'es'))
        self.assertFalse(adapter.lexicon.shared_spelling(source, 'omega is a letter', 'es'))
        self.assertFalse(adapter.lexicon.shared_spelling({'strong_number': 'H1'}, 'omega', 'es'))

    def test_spanish_glossary_covers_the_same_qualifiers(self):
        self.assertEqual({k for k, _ in adapter.lexicon.GLOSSARY['es']},
                         {k for k, _ in adapter.lexicon.GLOSSARY['de']})

    def test_spanish_sem_is_an_entry_specific_name_exception(self):
        self.assertTrue(adapter.lexicon.shared_spelling({'strong_number': 'G4590'}, 'Sem.', 'es'))
        self.assertFalse(adapter.lexicon.shared_spelling({'strong_number': 'G26'}, 'Sem.', 'es'))


class WorkflowTests(unittest.TestCase):
    def test_scoped_correction_preserves_other_fields_and_their_uncertainty(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            folder = run / 'full'
            runner.write_json(folder / 'source.json', [{'strong_number': 'G1', 'lemma': 'α',
                              'transliteration': 'a', 'morphology': None,
                              'definition': 'first', 'derivation': 'from G2'}])
            runner.write_json(folder / 'manifest.json', {'language_name': 'German',
                              'prompt_version': 'fixture'})
            runner.write_json(run / 'full-feedback.json', {
                'G1': {'feedback': 'correct definition only', 'fields': ['definition']}})
            (folder / 'answers.jsonl').write_text(json.dumps({'strong_number': 'G1',
                'definition': 'erster', 'derivation': 'von G2', 'uncertain': ['from']}) + '\n')
            module = adapter.lexicon

            def ask(args):
                module.run_batches(args, None, lambda manifest: 'system', None)

            outcome = {'result': json.dumps([{'strong_number': 'G1', 'definition': 'erste',
                        'derivation': 'abgeleitet von G2', 'uncertain': ['first']}])}
            with patch.object(runner, 'frozen'), \
                    patch.object(runner, 'module_at', return_value=adapter), \
                    patch.object(module, 'ask', side_effect=ask), \
                    patch.object(module, 'call_with_retries', return_value=(outcome, None)), \
                    patch.object(runner, 'step', return_value=0), \
                    patch.object(runner, 'report', return_value={'returned': 1}):
                self.assertEqual(runner.repair(run, 'de', 'full'), 0)
            row = module.read(str(folder / 'answers.jsonl'))[0]
            self.assertEqual(row['derivation'], 'von G2')
            self.assertEqual(set(row['uncertain']), {'from', 'first'})

    def test_failed_correction_keeps_other_successful_rows_and_reports_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            folder = run / 'full'
            runner.write_json(folder / 'source.json', [
                {'strong_number': 'G1', 'lemma': 'α', 'transliteration': 'a',
                 'morphology': None, 'definition': 'first'},
                {'strong_number': 'G2', 'lemma': 'β', 'transliteration': 'b',
                 'morphology': None, 'definition': 'second'}])
            runner.write_json(folder / 'manifest.json', {'language_name': 'German',
                              'prompt_version': 'fixture'})
            runner.write_json(run / 'full-feedback.json', {'G1': 'correct first', 'G2': 'correct second'})
            (folder / 'answers.jsonl').write_text('')
            module = adapter.lexicon

            def ask(args):
                module.run_batches(args, None, lambda manifest: 'system', None)

            def call(prompt, *args, **kwargs):
                if json.loads(prompt)['entries'][0]['strong_number'] == 'G1':
                    return None, 'fixture quota failure'
                return {'result': json.dumps([{'strong_number': 'G2', 'definition': 'zweite'}])}, None

            with patch.object(runner, 'frozen'), \
                    patch.object(runner, 'module_at', return_value=adapter), \
                    patch.object(module, 'ask', side_effect=ask), \
                    patch.object(module, 'call_with_retries', side_effect=call), \
                    patch.object(runner, 'step', return_value=1), \
                    patch.object(runner, 'report', return_value={'returned': 1}):
                self.assertEqual(runner.repair(run, 'de', 'full'), 1)
            rows = module.read(str(folder / 'answers.jsonl'))
            self.assertEqual([row['strong_number'] for row in rows], ['G2'])
            self.assertIn('G1', runner.read_json(run / 'status.json')['repair_errors'][0])

    def test_refused_lease_prevents_full_generation(self):
        with patch.object(sys, 'argv', ['run', '--language', 'es', '--mode', 'full']), \
                patch.object(runner, 'avioniq', return_value=1), \
                patch.object(runner, 'generate') as generate:
            with self.assertRaisesRegex(RuntimeError, 'lease was refused'):
                runner.main()
            generate.assert_not_called()

    def test_exception_updates_status_instead_of_leaving_running(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(sys, 'argv', ['run', '--language', 'de', '--mode', 'pilot']), \
                    patch.object(runner, 'ROOT', Path(directory)), \
                    patch.object(runner, 'generate', side_effect=RuntimeError('fixture failure')):
                with self.assertRaisesRegex(RuntimeError, 'fixture failure'):
                    runner.main()
            status = runner.read_json(Path(directory) / 'de/status.json')
            self.assertEqual(status['stage'], 'pilot-failed')

    def test_full_run_requires_readiness_before_calling_model(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(runner, 'frozen'), patch.object(runner, 'step') as step:
            with self.assertRaises(FileNotFoundError):
                runner.generate(Path(directory), 'es', 'full', 2)
            step.assert_not_called()

    def test_frozen_input_change_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            source = run / 'source.json'
            source.write_text('[]')
            runner.write_json(run / 'frozen.json', {'model': runner.MODEL,
                              'files': {'source.json': runner.digest(source)}})
            source.write_text('[1]')
            with self.assertRaisesRegex(RuntimeError, 'Frozen input changed'):
                runner.frozen(run)

    def test_matching_readiness_hash_cannot_approve_an_incomplete_pilot(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            (run / 'pilot').mkdir()
            answers = run / 'pilot/answers.jsonl'
            answers.write_text('{}')
            runner.write_json(run / 'generation-readiness.json', {
                'proceed': True, 'model': runner.MODEL, 'language': 'es',
                'answers_sha256': runner.digest(answers)})
            module = SimpleNamespace(lexicon=SimpleNamespace(
                check_run=lambda path: ([], [{'kind': 'missing-entry'}], {'G1'})))
            with patch.object(runner, 'frozen'), patch.object(runner, 'module_at', return_value=module), \
                    patch.object(runner, 'step') as step:
                with self.assertRaisesRegex(RuntimeError, 'Pilot completeness'):
                    runner.generate(run, 'es', 'full', 2)
            step.assert_not_called()

    def test_publication_requires_a_separate_current_delivery_review(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(runner, 'frozen'), patch.object(runner, 'step') as step:
            with self.assertRaises(FileNotFoundError):
                runner.publish(Path(directory), 'es')
            step.assert_not_called()


if __name__ == '__main__':
    unittest.main()
