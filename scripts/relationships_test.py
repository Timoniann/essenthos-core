"""The rules `relationships.py decide` applies before it writes a decision: python scripts/relationships_test.py"""
import unittest

import relationships


def record(*claims):
    return {'claims': [dict(zip(('relation', 'target', 'reference', 'confidence'), c)) for c in claims]}


class CitationTests(unittest.TestCase):
    def test_a_passage_and_a_verse_elsewhere_are_composed(self):
        verses, composed = relationships.citation('GEN 20:12-14; GEN 11:27')
        self.assertTrue(composed)
        self.assertEqual(verses, [(1, 20, 12), (1, 20, 13), (1, 20, 14), (1, 11, 27)])

    def test_what_the_loader_refuses_is_refused(self):
        for text in ('GEN 20:12-14; GEN 20:14', 'GEN 20:11-14; GEN 11:27', '2KI 8:16; 2KI 8:18; 2KI 8:26'):
            self.assertIsNone(relationships.citation(text), text)


class OverruledReadingTests(unittest.TestCase):
    def test_a_model_reading_the_decided_verse_otherwise_is_withdrawn_from_either_end(self):
        standing = {
            'hodiah': record(('sister-of', 'naham', '1CH 4:19', 0.75)),
            'naham': record(('brother-of', 'hodiah', '1CH 4:19', 0.85)),
        }
        found = relationships.overruled_readings(('hodiah', 'brother-in-law-of', 'naham'), {(13, 4, 19)}, standing)
        self.assertEqual([(entity, claim['relation']) for entity, claim in found],
                         [('hodiah', 'sister-of'), ('naham', 'brother-of')])

    def test_a_reversed_reading_is_withdrawn(self):
        standing = {'zabad-2': record(('son-of', 'shuthelah-2', '1CH 7:21', 0.85))}
        found = relationships.overruled_readings(('shuthelah-2', 'son-of', 'zabad-2'), {(13, 7, 21)}, standing)
        self.assertEqual([entity for entity, _ in found], ['zabad-2'])

    def test_a_reading_of_another_verse_stays(self):
        standing = {'sarai': record(('daughter-in-law-of', 'terah', 'GEN 11:31', 0.9))}
        decided_on = {(1, 20, 12), (1, 20, 13), (1, 20, 14), (1, 11, 27)}
        self.assertEqual(relationships.overruled_readings(('sarai', 'daughter-of', 'terah'), decided_on, standing), [])

    def test_a_reading_that_agrees_or_says_less_stays(self):
        standing = {
            'zechariah-13': record(('descendant-of', 'iddo-5', 'EZR 5:1', 0.85)),
            'iddo-5': record(('grandfather-of', 'zechariah-13', 'EZR 5:1', 0.85),
                             ('ancestor-of', 'zechariah-13', 'EZR 5:1', 0.85)),
        }
        found = relationships.overruled_readings(('zechariah-13', 'grandson-of', 'iddo-5'), {(15, 5, 1)}, standing)
        self.assertEqual(found, [])

    def test_a_decided_claim_is_never_withdrawn(self):
        standing = {'zabad-2': {'claims': [{'relation': 'son-of', 'target': 'shuthelah-2', 'reference': '1CH 7:21',
                                            'confidence': None, 'decidedBy': 'the project owner, decided 2026-09-29'}]}}
        found = relationships.overruled_readings(('shuthelah-2', 'son-of', 'zabad-2'), {(13, 7, 21)}, standing)
        self.assertEqual(found, [])


if __name__ == '__main__':
    unittest.main()
