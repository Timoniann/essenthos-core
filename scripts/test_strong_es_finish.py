import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from test_lexicon_codex import *  # noqa: F401,F403 - one action runs the whole Codex harness suite

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('finish_es', HERE / 'finish-strong-es-codex.py')
finish = importlib.util.module_from_spec(spec)
spec.loader.exec_module(finish)
lexicon = finish.base.module_at(HERE).lexicon
LOADER_SENSE = re.compile(r'^\s*(\d+[a-z]?)\)', re.M)


def sha(text):
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


class SpanishReviewedFieldTests(unittest.TestCase):
    source = {'strong_number': 'G2082', 'definition': 'interior', 'derivation': 'comparative of G2080;',
              'kjv_definition': 'inner, within.'}
    answer = {'strong_number': 'G2082', 'definition': 'interior', 'derivation': 'comparativo de G2080;',
              'kjv_definition': 'interior, dentro'}

    def test_a_reviewed_identical_spanish_word_passes(self):
        self.assertEqual(lexicon.failures_of(self.source, self.answer, 'es'), [])

    def test_the_allowance_is_bound_to_the_exact_field_text(self):
        changed = {**self.answer, 'definition': 'interior.'}
        self.assertEqual(lexicon.failures_of({**self.source, 'definition': 'interior.'}, changed, 'es'),
                         [('definition', 'untranslated', 'returned the English unchanged')])

    def test_the_allowance_is_bound_to_its_entry_field_and_language(self):
        elsewhere = {**self.source, 'strong_number': 'G2083'}
        self.assertTrue(lexicon.failures_of(elsewhere, {**self.answer, 'strong_number': 'G2083'}, 'es'))
        self.assertTrue(lexicon.failures_of(self.source, self.answer, 'de'))

    def test_every_spanish_allowance_is_an_unchanged_field(self):
        for (number, field), (english, translated, reason) in lexicon.REVIEWED_SPANISH_FIELDS.items():
            self.assertRegex(number, r'^[GH]\d+$')
            self.assertIn(field, lexicon.PROSE)
            self.assertEqual(english, translated, number)
            self.assertTrue(reason)


class LoaderSenseAgreementTests(unittest.TestCase):
    english = 'Rekem = "variegation"\nn pr m 1) one of the 5 kings\n2) a son of Hebron\nn pr loc\n3) a town'

    def test_a_label_the_loader_cannot_see_must_stay_where_the_english_has_it(self):
        moved = 'Requem = "variedad"\nnombre propio masculino\n1) uno de los 5 reyes\n2) un hijo\n' \
                'nombre propio de lugar\n3) una ciudad'
        kept = 'Requem = "variedad"\nnombre propio masculino 1) uno de los 5 reyes\n2) un hijo\n' \
               'nombre propio de lugar\n3) una ciudad'
        source = {'strong_number': 'H7552', 'detailed_definition': self.english}
        self.assertNotEqual(LOADER_SENSE.findall(self.english), LOADER_SENSE.findall(moved))
        self.assertTrue(lexicon.failures_of(source, {'strong_number': 'H7552', 'detailed_definition': moved}, 'es'))
        self.assertEqual(LOADER_SENSE.findall(self.english), LOADER_SENSE.findall(kept))
        self.assertFalse(lexicon.failures_of(source, {'strong_number': 'H7552', 'detailed_definition': kept}, 'es'))

    def test_a_cross_reference_to_a_sense_is_not_a_sense(self):
        english = 'J = "x"\n1) a Hachmonite\n2) a Korahite; same as 1?'
        spanish = 'J = "x"\n1) un hacmonita\n2) un coreíta; ¿el mismo que en 1)?'
        self.assertFalse(lexicon.failures_of({'strong_number': 'H3434', 'detailed_definition': english},
                                             {'strong_number': 'H3434', 'detailed_definition': spanish}, 'es'))


class SpanishRecoveryTests(unittest.TestCase):
    def fixture(self, root, answer):
        origin, run = root / 'origin', root / 'run'
        (origin / 'full/calls/inv').mkdir(parents=True)
        (run / 'full').mkdir(parents=True)
        (run / 'pilot').mkdir()
        (run / 'code').mkdir()
        (run / 'code/lexicon.py').write_text('# frozen fixture validator', encoding='utf-8')
        sources = [{'strong_number': 'G1857', 'definition': 'exterior', 'kjv_definition': 'outer.'},
                   {'strong_number': 'G26', 'definition': 'love'}]
        (run / 'full/source.json').write_text(json.dumps(sources), encoding='utf-8')
        kept = {'strong_number': 'G26', 'definition': 'amor', 'model': 'gpt-6.1-sol'}
        (run / 'full/answers.jsonl').write_text(json.dumps(kept) + '\n', encoding='utf-8')
        pilot = {'strong_number': 'G26', 'definition': 'amor (revisado)', 'model': 'gpt-6.1-sol',
                 'method': 'model-translation', 'prompt_version': 'p', 'run': 'r'}
        (run / 'pilot/answers.jsonl').write_text(json.dumps(pilot) + '\n', encoding='utf-8')
        record = {'prompt': json.dumps({'entries': sources[:1]}), 'effort': 'medium', 'returncode': 0,
                  'provenance': {'phase': 'ask', 'batch': 'batch-0232', 'run': '2026-10-20',
                                 'prompt_version': 'lexicon-es-codex-2'},
                  'outcome': {'result': json.dumps([answer]), 'modelUsage': {'gpt-6.1-sol': {}}}}
        raw = origin / 'full/calls/inv/call.json'
        raw.write_text(json.dumps(record), encoding='utf-8')
        return origin, run, raw

    def recover(self, origin, run, raw, digest=None):
        bindings = {'G1857': ('inv/call.json', digest or finish.base.digest(raw))}
        with patch.object(finish, 'RUN', run), patch.object(finish, 'ORIGIN', origin), \
                patch.object(finish, 'REVIEWED_RECOVERIES', bindings), \
                patch.object(finish.base, 'frozen'), patch.object(finish.base, 'report'), \
                patch.object(finish.base, 'module_at', return_value=SimpleNamespace(lexicon=lexicon)), \
                patch.object(lexicon, 'REVIEWED_SPANISH_FIELDS', {('G1857', 'definition'): (
                    sha('exterior'), sha('exterior'), 'identical Spanish word')}):
            finish.recover()
        return lexicon.read(str(run / 'full/answers.jsonl'))

    def test_a_reviewed_raw_answer_is_recovered_with_its_own_provenance(self):
        answer = {'strong_number': 'G1857', 'definition': 'exterior', 'kjv_definition': 'exterior',
                  'derivation': None, 'detailed_definition': None, 'uncertain': []}
        with tempfile.TemporaryDirectory() as directory:
            origin, run, raw = self.fixture(Path(directory), answer)
            rows = {row['strong_number']: row for row in self.recover(origin, run, raw)}
            self.assertEqual(rows['G1857']['definition'], 'exterior')
            self.assertEqual(rows['G1857']['model'], 'gpt-6.1-sol')
            self.assertEqual(rows['G1857']['run'], '2026-10-20')
            self.assertEqual(rows['G1857']['recovered_raw_sha256'], finish.base.digest(raw))
            self.assertEqual(rows['G26']['definition'], 'amor (revisado)')
            self.assertTrue(rows['G26']['retained_reviewed_pilot'])
            lines = (run / 'full/answers.jsonl').read_text(encoding='utf-8').splitlines()
            self.recover(origin, run, raw)
            self.assertEqual((run / 'full/answers.jsonl').read_text(encoding='utf-8').splitlines(), lines)

    def test_a_changed_raw_record_is_refused(self):
        answer = {'strong_number': 'G1857', 'definition': 'exterior', 'kjv_definition': 'exterior'}
        with tempfile.TemporaryDirectory() as directory:
            origin, run, raw = self.fixture(Path(directory), answer)
            with self.assertRaisesRegex(RuntimeError, 'changed'):
                self.recover(origin, run, raw, digest='0' * 64)

    def test_a_reviewed_raw_answer_that_still_fails_is_refused(self):
        answer = {'strong_number': 'G1857', 'definition': 'exterior', 'kjv_definition': 'outer.'}
        with tempfile.TemporaryDirectory() as directory:
            origin, run, raw = self.fixture(Path(directory), answer)
            with self.assertRaisesRegex(RuntimeError, 'fails the checker'):
                self.recover(origin, run, raw)


if __name__ == '__main__':
    unittest.main()
