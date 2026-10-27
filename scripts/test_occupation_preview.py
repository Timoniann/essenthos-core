"""Offline rejection tests for the fixed source-reviewed occupation preview."""
import copy
from pathlib import Path
import tempfile
import unittest
import unicodedata

import publish_occupation_preview as preview


class OccupationPreviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original_bundle = preview.read(preview.RUN / 'bundle.json')
        cls.original_people = preview.read(preview.RUN / 'people.json')

    def setUp(self):
        self.bundle = copy.deepcopy(self.original_bundle)
        self.people = copy.deepcopy(self.original_people)

    def validate(self):
        return preview.validate_bundle(self.bundle, self.people)

    def assertion(self, person, role):
        return next(a for a in self.bundle['assertions']
                    if (a['personSlug'], a['occupationSlug']) == (person, role))

    def test_real_reviewed_slice_and_all_bound_artifacts_verify(self):
        preview.verify()
        self.assertEqual(self.validate(), dict(occupations=12, persons=15, assertions=16, witnesses=52))
        self.assertEqual({r['category'] for r in self.bundle['occupations']},
                         {'trade', 'office', 'military_role', 'religious_role'})

    def test_changed_quote_is_rejected(self):
        self.bundle['assertions'][0]['witnesses'][0]['quote'] = 'Luke, the renowned physician'
        with self.assertRaisesRegex(ValueError, 'Nonverbatim'):
            self.validate()

    def test_same_verse_unrelated_exact_quote_is_not_reviewed_evidence(self):
        self.bundle['assertions'][0]['witnesses'][0]['quote'] = 'Demas'
        with self.assertRaisesRegex(ValueError, 'Witness set differs'):
            self.validate()

    def test_changed_full_source_even_with_matching_new_hash_is_rejected(self):
        evidence = self.bundle['assertions'][0]['witnesses'][0]
        evidence['sourceText'] += ' Alteration.'
        evidence['sourceSha256'] = preview.digest(evidence['sourceText'].encode('utf-8'))
        with self.assertRaisesRegex(ValueError, 'Full-verse source/hash mismatch'):
            self.validate()

    def test_changed_hash_is_rejected(self):
        self.bundle['assertions'][0]['witnesses'][0]['sourceSha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'Full-verse source/hash mismatch'):
            self.validate()

    def test_non_nfc_greek_quote_is_rejected(self):
        evidence = self.bundle['assertions'][0]['witnesses'][1]
        evidence['quote'] = unicodedata.normalize('NFD', evidence['quote'])
        self.assertNotEqual(evidence['quote'], preview.nfc(evidence['quote']))
        with self.assertRaisesRegex(ValueError, 'Nonverbatim or non-NFC'):
            self.validate()

    def test_canonical_number_may_not_be_invented_from_printed_number(self):
        evidence = self.assertion('shiphrah', 'midwife')['witnesses'][2]
        evidence['canonicalReference'] = 'EXO 1:16'
        with self.assertRaisesRegex(ValueError, 'Printed/canonical source mapping differs'):
            self.validate()

    def test_changed_printed_reference_is_rejected(self):
        self.bundle['assertions'][0]['witnesses'][0]['reference'] = 'COL 4:13'
        with self.assertRaisesRegex(ValueError, 'Printed/canonical source mapping differs'):
            self.validate()

    def test_only_identity_evidence_does_not_establish_a_trade(self):
        for evidence in self.assertion('saul-2', 'tentmaker')['witnesses']:
            evidence['kind'] = 'identity'
        with self.assertRaisesRegex(ValueError, 'Missing profession witness'):
            self.validate()

    def test_missing_saul_paul_bridge_is_rejected(self):
        assertion = self.assertion('saul-2', 'tentmaker')
        assertion['witnesses'] = [w for w in assertion['witnesses'] if w['reference'] != 'ACT 13:9']
        with self.assertRaisesRegex(ValueError, 'Missing shared-trade or renamed-person identity bridge'):
            self.validate()

    def test_missing_shared_priscilla_context_is_rejected(self):
        assertion = self.assertion('priscilla', 'tentmaker')
        assertion['witnesses'] = [w for w in assertion['witnesses'] if w['kind'] != 'identity']
        with self.assertRaisesRegex(ValueError, 'Missing shared-trade or renamed-person identity bridge'):
            self.validate()

    def test_another_erastus_record_does_not_inherit_the_office(self):
        self.assertion('erastus-2', 'city-steward')['personSlug'] = 'erastus'
        with self.assertRaisesRegex(ValueError, 'Unknown person or occupation'):
            self.validate()

    def test_duplicate_assertion_is_rejected(self):
        self.bundle['assertions'].append(copy.deepcopy(self.bundle['assertions'][0]))
        with self.assertRaisesRegex(ValueError, 'Duplicate assertion'):
            self.validate()

    def test_extra_or_missing_locale_is_rejected(self):
        del self.bundle['occupations'][0]['labels']['spa']
        with self.assertRaisesRegex(ValueError, 'Exactly four reader locales'):
            self.validate()

    def test_missing_feminine_label_is_rejected(self):
        self.bundle['occupations'][0]['labels']['ukr']['feminine'] = ''
        with self.assertRaisesRegex(ValueError, 'Missing gender-aware label'):
            self.validate()

    def test_people_sex_and_names_must_come_from_frozen_records(self):
        next(p for p in self.people['people'] if p['slug'] == 'priscilla')['sex'] = 'male'
        with self.assertRaisesRegex(ValueError, 'Person metadata differs'):
            self.validate()

    def test_female_roles_and_time_boundary_are_explicit(self):
        persons = {p['slug']: p for p in self.people['people']}
        for slug in ('lydia', 'priscilla', 'shiphrah', 'puah'):
            self.assertEqual(persons[slug]['sex'], 'female')
        review = preview.read(preview.RUN / 'source-review.json')
        amos = [d for d in review['decisions'] if d['person'] == 'amos']
        self.assertEqual(len(amos), 2)
        self.assertEqual({d['time_scope'] for d in amos}, {'unspecified'})
        self.assertFalse(review['human_accepted'])
        self.assertFalse(review['live_corpus_admission'])

    def test_frozen_evidence_byte_tampering_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            data = run / 'source.txt'
            data.write_bytes(b'original')
            preview.write(run / 'snapshot-manifest.json', dict(files=[dict(
                snapshot='source.txt', sha256=preview.sha(data), bytes=8)]))
            preview.verify_snapshot(run)
            data.write_bytes(b'modified')
            with self.assertRaisesRegex(ValueError, 'Frozen evidence changed'):
                preview.verify_snapshot(run)

    def test_bundle_cannot_be_marked_unreviewed(self):
        self.bundle['review']['status'] = 'candidate'
        with self.assertRaisesRegex(ValueError, 'Unreviewed or unsupported'):
            self.validate()


if __name__ == '__main__':
    unittest.main()
