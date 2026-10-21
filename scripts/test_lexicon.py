import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

module_path = Path(os.environ.get('LEXICON_MODULE_PATH', Path(__file__).with_name('lexicon.py')))
spec = importlib.util.spec_from_file_location('lexicon', module_path)
lexicon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lexicon)


class LexiconTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.source = [dict(strong_number='G1', lemma='Α', transliteration='alpha',
                            morphology='N', definition='first letter', derivation='from G2')]
        self.manifest = dict(language='de', language_name='German', prompt_version='lexicon-de-1',
                             entries=1, seed=11, sample=1,
                             batches=[dict(batch='batch-0000', numbers=['G1'])])
        self.write('source.json', self.source)
        self.write('manifest.json', self.manifest)
        self.write('batches/batch-0000.json', dict(entries=self.source, language='German',
                                               prompt_version='lexicon-de-1'))
        self.args = SimpleNamespace(dir=str(self.directory), model='mock', effort='medium',
                                    workers=1, again=False, clean=False,
                                    out=str(self.directory / 'published/strong-de.jsonl'))

    def write(self, path, value):
        target = self.directory / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8')

    def answers(self, rows):
        (self.directory / 'answers.jsonl').write_text(
            ''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows), encoding='utf-8')

    def translated(self, **changes):
        return dict(strong_number='G1', definition='erster Buchstabe', derivation='von G2',
                    method='model-translation', model='mock', prompt_version='lexicon-de-1',
                    run='2026-10-17T00:00:00Z', **changes)

    def test_missing_entry_fails_check(self):
        with self.assertRaises(SystemExit):
            lexicon.check(self.args)
        failures = json.loads((self.directory / 'failures.json').read_text())
        self.assertIn('missing-entry', {f['kind'] for f in failures})

    def test_operator_stop_file_prevents_unsent_model_calls(self):
        target = self.directory / 'stop-requested.json'
        target.write_text('{}')
        with patch.dict(os.environ, {'ESSENTHOS_LEXICON_STOP_FILE': str(target)}), \
                patch.object(lexicon, 'call_with_retries') as call:
            with self.assertRaises(SystemExit):
                lexicon.ask(self.args)
            call.assert_not_called()

    def test_unexpected_entry_fails_check(self):
        self.answers([dict(strong_number='G99', definition='unbekannt')])
        with self.assertRaises(SystemExit):
            lexicon.check(self.args)

    def test_missing_field_and_non_string_fail(self):
        for value in (None, '', ['wrong']):
            row = self.translated()
            row['derivation'] = value
            self.assertIn('missing', {kind for _, kind, _ in
                          lexicon.failures_of(self.source[0], row, 'de')})

    def test_original_script_exact_characters_and_counts(self):
        source = dict(strong_number='G1', definition='from λόγος λόγος')
        for text in ('von λόγος', 'von λογος λόγος', 'von λόγος λόγος λόγος'):
            failures = lexicon.failures_of(source, dict(strong_number='G1', definition=text), 'de')
            self.assertTrue(any(kind == 'original-script-changed' for _, kind, _ in failures))

    def test_padded_strong_references_preserved_exactly(self):
        source = dict(strong_number='H1', definition='from H05664 or H05665')
        preserved = dict(strong_number='H1', definition='von H05664 oder H05665')
        self.assertFalse(lexicon.failures_of(source, preserved, 'de'))
        changed = dict(strong_number='H1', definition='von H5664 oder H5665')
        kinds = {kind for _, kind, _ in lexicon.failures_of(source, changed, 'de')}
        self.assertEqual(kinds, {'lost-reference', 'invented-reference'})

    def test_hierarchical_senses_preserved_when_inline_label_moves(self):
        source = dict(strong_number='H1', detailed_definition=
                      '1) return\n1a1f2) to turn away (of God) 1d10) to restore')
        answer = dict(strong_number='H1', detailed_definition=
                      '1) umkehren\n1a1f2) sich abwenden (von Gott ausgesagt)\n1d10) wiederherstellen')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))
        answer['detailed_definition'] = '1) umkehren\n1d10) wiederherstellen'
        self.assertIn('sense-numbering', {kind for _, kind, _ in
                      lexicon.failures_of(source, answer, 'de')})

    def test_citations_and_prose_cross_references_are_not_sense_labels(self):
        source = dict(strong_number='H1', detailed_definition=
                      '1) a cubit (. 5m), De 3:11, Eze 40:5\n2) same as 1 above')
        answer = dict(strong_number='H1', detailed_definition=
                      '1) eine Elle (ca. 0,5 m), 5Mo 3,11, Hes 40,5\n2) derselbe wie 1)')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))
        answer['detailed_definition'] = '1) eine Elle, Ec 10:17)'
        self.assertIn('sense-numbering', {kind for _, kind, _ in
                      lexicon.failures_of(source, answer, 'de')})

    def test_renderings_use_semicolons_and_preserve_nested_alternatives(self):
        self.assertEqual(lexicon.renderings('(cast, (gather out), throw) stone, rock'), 2)
        source = dict(strong_number='H1', kjv_definition='cease, conceal, rest, work')
        answer = dict(strong_number='H1', kjv_definition='aufhören; verbergen; ruhen; arbeiten')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))
        answer['kjv_definition'] = 'ruhen'
        self.assertIn('renderings-collapsed', {kind for _, kind, _ in
                      lexicon.failures_of(source, answer, 'de')})

    def test_unclosed_explanatory_bracket_is_not_a_rendering_list(self):
        source = dict(strong_number='G1', kjv_definition=
                      '(used only with other particles, as G2, G3, G4, G5, etc.')
        answer = dict(strong_number='G1', kjv_definition=
                      '(nur mit anderen Partikeln verwendet, wie G2, G3, G4, G5, usw.)')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))
        answer['kjv_definition'] = '(nur mit G2, G3, G4, usw.)'
        self.assertIn('lost-reference', {kind for _, kind, _ in
                      lexicon.failures_of(source, answer, 'de')})

    def test_reviewed_allowance_is_bound_to_entry_field_language_and_both_strings(self):
        source = dict(strong_number='G13', kjv_definition='Agabus.')
        answer = dict(strong_number='G13', kjv_definition='Agabus.')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))
        for changed_source, changed_answer, language in (
                ({**source, 'strong_number': 'G999'}, {**answer, 'strong_number': 'G999'}, 'de'),
                (dict(strong_number='G13', definition='Agabus.'),
                 dict(strong_number='G13', definition='Agabus.'), 'de'),
                ({**source, 'kjv_definition': 'Agabus is an Israelite'},
                 {**answer, 'kjv_definition': 'Agabus is an Israelite'}, 'de'),
                (source, answer, 'es')):
            self.assertIn('untranslated', {kind for _, kind, _ in
                          lexicon.failures_of(changed_source, changed_answer, language)})
        self.assertFalse(lexicon.reviewed_unchanged_field('G13', 'kjv_definition',
                                                       'Agabus.', 'Agabus', 'de'))

    def test_known_english_names_and_gentilics_are_not_allowed(self):
        for number, word in [('G2491', 'John.'), ('H2741', 'Haruphite.'),
                             ('H3441', 'Jesuites.'), ('H8554', 'Timnite.')]:
            source = dict(strong_number=number, kjv_definition=word)
            answer = dict(strong_number=number, kjv_definition=word)
            self.assertIn('untranslated', {kind for _, kind, _ in
                          lexicon.failures_of(source, answer, 'de')})

    def test_shared_name_and_loanword_allowed(self):
        for text, transliteration in [('Noah', 'Nōah'), ('Alpha', 'alpha'), ('amen', 'amen'),
                                      ('Alpha.', 'alpha'), ('Jesus', 'Iesous'),
                                      ('(Abba);', 'abba'), ('Satan,', 'satan')]:
            source = dict(strong_number='G1', transliteration=transliteration, definition=text)
            self.assertFalse(lexicon.failures_of(source, dict(strong_number='G1', definition=text), 'de'))

    def test_canonical_equivalent_greek_preserved(self):
        source = dict(strong_number='G1', definition='from λείβω')
        self.assertFalse(lexicon.failures_of(
            source, dict(strong_number='G1', definition='von λείβω'), 'de'))
        source['definition'] = 'from λόγος'
        for changed in ('von λογος', 'von λόγορ', 'von λογοϲ'):
            self.assertIn('original-script-changed', {kind for _, kind, _ in
                          lexicon.failures_of(source, dict(strong_number='G1', definition=changed), 'de')})

    def test_punctuation_does_not_allow_unchanged_english_prose(self):
        source = dict(strong_number='G1', transliteration='alpha', definition='Alpha of the Greek alphabet.')
        found = lexicon.failures_of(source, dict(strong_number='G1', definition=source['definition']), 'de')
        self.assertIn('untranslated', {kind for _, kind, _ in found})

    def test_same_spelling_source_proper_name(self):
        source = dict(strong_number='H1', morphology='n-pr-m', transliteration='Yets',
                      kjv_definition='Jeuz.', detailed_definition='Jeuz = "counsellor"\n1) a man')
        answer = dict(strong_number='H1', kjv_definition='Jeuz.',
                      detailed_definition='Jeuz = "Ratgeber"\n1) ein Mann')
        self.assertFalse(lexicon.failures_of(source, answer, 'de'))

    def test_exhausted_quota_stops_unsent_batches(self):
        self.manifest['batches'] = [dict(batch=f'batch-{n:04}', numbers=['G1']) for n in range(4)]
        self.write('manifest.json', self.manifest)
        with patch.object(lexicon, 'call_with_retries', return_value=(None, 'usage limit exhausted')) as call, \
                patch.dict(lexicon.GLOSSARY, de=[('properly', 'eigentlich')]):
            with self.assertRaises(SystemExit):
                lexicon.ask(self.args)
        self.assertEqual(call.call_count, 1)
        summary = json.loads(next((self.directory / 'calls').glob('*-summary.json')).read_text())
        self.assertEqual(summary['skipped_batches'], 3)

    def test_english_prose_still_rejected(self):
        source = dict(strong_number='G1', transliteration='father', definition='Father')
        answer = dict(strong_number='G1', definition='Father of the house from which it comes')
        self.assertIn('residual-English', {kind for _, kind, _ in
                      lexicon.failures_of(source, answer, 'de')})
        source['transliteration'] = 'pater'
        self.assertIn('untranslated', {kind for _, kind, _ in lexicon.failures_of(
            source, dict(strong_number='G1', definition='Father'), 'de')})

    def test_publish_refuses_partial_and_preserves_existing_output(self):
        output = Path(self.args.out)
        output.parent.mkdir()
        output.write_text('previous', encoding='utf-8')
        with self.assertRaises(SystemExit):
            lexicon.publish(self.args)
        self.assertEqual(output.read_text(), 'previous')

    def test_publish_preserves_shared_licence(self):
        self.answers([self.translated()])
        output = Path(self.args.out)
        output.parent.mkdir()
        licence = output.parent / 'LICENCE.md'
        licence.write_text('Ukrainian attribution', encoding='utf-8')
        lexicon.publish(self.args)
        self.assertEqual(licence.read_text(), 'Ukrainian attribution')
        self.assertTrue((output.parent / 'LICENCE.de.md').is_file())

    def test_raw_parse_failure_retains_cost_and_effort(self):
        outcome = dict(result='not a JSON array', total_cost_usd=0.125,
                       modelUsage={'actual-model': {'inputTokens': 20}})
        process = subprocess.CompletedProcess([], 0, json.dumps(outcome).encode(), b'')
        with patch.object(lexicon, 'executable', return_value='mock'), \
                patch.object(lexicon.subprocess, 'run', return_value=process) as run, \
                patch.dict(lexicon.GLOSSARY, de=[('properly', 'eigentlich')]):
            with self.assertRaises(SystemExit):
                lexicon.ask(self.args)
        records = list((self.directory / 'calls').glob('*/*.json'))
        self.assertEqual(len(records), 1)
        record = json.loads(records[0].read_text())
        self.assertEqual(record['outcome']['total_cost_usd'], 0.125)
        self.assertEqual(record['effort'], 'medium')
        self.assertEqual(record['stdout'], json.dumps(outcome))
        self.assertIn('--effort', run.call_args.args[0])
        summary = json.loads(next((self.directory / 'calls').glob('*-summary.json')).read_text())
        self.assertEqual(summary['total_cost_usd'], 0.125)

    def test_failed_cli_preserves_raw_outcome_usage(self):
        outcome = dict(result='failed', is_error=True, total_cost_usd=0.25)
        process = subprocess.CompletedProcess([], 1, json.dumps(outcome).encode(), b'failure')
        with patch.object(lexicon, 'executable', return_value='mock'), \
                patch.object(lexicon.subprocess, 'run', return_value=process):
            result, why = lexicon.call('input', 'system', 'mock', record_dir=str(self.directory))
        self.assertIsNone(result)
        self.assertTrue(why)
        records = [p for p in self.directory.glob('*.json')
                   if p.name not in ('manifest.json', 'source.json')]
        record = json.loads(records[0].read_text())
        self.assertEqual(record['outcome']['total_cost_usd'], 0.25)
        self.assertEqual(record['returncode'], 1)

    def test_resume_preserves_complete_rows(self):
        old = self.translated()
        self.answers([old])
        with patch.object(lexicon, 'call_with_retries') as call, \
                patch.dict(lexicon.GLOSSARY, de=[('properly', 'eigentlich')]):
            lexicon.ask(self.args)
        call.assert_not_called()
        self.assertEqual(lexicon.read(str(self.directory / 'answers.jsonl')), [old])

    def test_partial_resume_requests_missing_only(self):
        second = {**self.source[0], 'strong_number': 'G2'}
        self.write('source.json', self.source + [second])
        self.manifest['entries'] = 2
        self.manifest['batches'][0]['numbers'].append('G2')
        self.write('manifest.json', self.manifest)
        self.write('batches/batch-0000.json', dict(entries=self.source + [second],
                                               language='German', prompt_version='lexicon-de-1'))
        old = self.translated()
        self.answers([old])
        outcome = dict(result=json.dumps([{**old, 'strong_number': 'G2'}]),
                       modelUsage={'new-model': {}}, total_cost_usd=0.1)
        with patch.object(lexicon, 'call_with_retries', return_value=(outcome, None)) as call, \
                patch.dict(lexicon.GLOSSARY, de=[('properly', 'eigentlich')]):
            lexicon.ask(self.args)
        sent = json.loads(call.call_args.args[0])
        self.assertEqual([r['strong_number'] for r in sent['entries']], ['G2'])
        self.assertEqual(lexicon.read(str(self.directory / 'answers.jsonl'))[0], old)

    def test_invalid_return_ids_and_fields_fail(self):
        for answers in ([{'strong_number': 'G99'}],
                        [{'strong_number': 'G1', 'definition': 'erster Buchstabe'}],
                        [self.translated(), self.translated()]):
            with self.subTest(answers=answers):
                outcome = dict(result=json.dumps(answers), modelUsage={'mock': {}})
                with patch.object(lexicon, 'call_with_retries', return_value=(outcome, None)), \
                        patch.dict(lexicon.GLOSSARY, de=[('properly', 'eigentlich')]):
                    with self.assertRaises(SystemExit):
                        lexicon.ask(self.args)

    def test_server_timeout_and_readonly(self):
        self.assertIn('default_transaction_read_only=on', lexicon.READ_ONLY)
        self.assertIn('statement_timeout=15000', lexicon.READ_ONLY)


if __name__ == '__main__':
    unittest.main()
