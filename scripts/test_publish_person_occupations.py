"""Offline admission guards; no API calls or database writes."""
import copy
import contextlib
import hashlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import publish_person_occupations as p


class PublicationGuards(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        p.write(self.root / 'manifest.json', {'groups': 1})
        self.patched = patch.object(p, 'RUN', self.root)
        self.patched.start()
        self.context = {'id': 'g-000', 'errors': [], 'sources': [
            {'kind': 'profession', 'key': 'KJV:51:4:14', 'quote': 'physician',
             'body': 'Luke, the beloved physician.', 'canonical': [51, 4, 14],
             'canonicalAddresses': [[51, 4, 14]], 'focus': True}]}
        self.groups = [{'id': 'g', 'contexts': [self.context]}]
        self.document = {'reviewedBy': 'source reader', 'sourceManifestSha256': p.sha(self.root / 'manifest.json'),
            'decisions': [{'id': 'g', 'verdict': 'admit', 'context_ids': ['g-000'],
                           'why': 'The source names Luke with the physician apposition.', 'source_keys': ['KJV:51:4:14']}]}
        label = {'name': 'Physician', 'masculine': 'physician', 'feminine': 'physician', 'description': None}
        self.taxonomy = {'mappings': [{'raw_role': 'trade:doctor', 'slug': 'physician', 'why': 'Same stated medical role.'}],
            'occupations': [{'slug': 'physician', 'category': 'trade', 'labels': {l: copy.deepcopy(label) for l in ('eng', 'ukr', 'deu', 'spa')}}]}

    def tearDown(self):
        self.patched.stop()
        self.temporary.cleanup()

    def test_valid_source_review_retains_explicit_selection(self):
        decisions = p.validate_decisions(self.groups, [self.document])
        self.assertEqual(decisions['g']['context_ids'], ['g-000'])

    def test_incomplete_or_duplicate_group_review_refuses_admission(self):
        for documents in ([], [self.document, self.document]):
            with self.assertRaises(ValueError):
                p.validate_decisions(self.groups, documents)

    def test_changed_source_manifest_or_missing_reviewer_refuses_admission(self):
        for key, value in (('sourceManifestSha256', '0' * 64), ('reviewedBy', '')):
            doc = copy.deepcopy(self.document)
            doc[key] = value
            with self.assertRaises(ValueError):
                p.validate_decisions(self.groups, [doc])

    def test_other_person_context_cannot_be_selected(self):
        self.document['decisions'][0]['context_ids'] = ['other-000']
        with self.assertRaises(ValueError):
            p.validate_decisions(self.groups, [self.document])

    def test_unbound_source_or_missing_profession_refuses_admission(self):
        for change in ('quote', 'identity'):
            group = copy.deepcopy(self.groups)
            if change == 'quote':
                group[0]['contexts'][0]['errors'] = ['unbound_quote']
            else:
                group[0]['contexts'][0]['sources'][0]['kind'] = 'identity'
            with self.assertRaises(ValueError):
                p.validate_decisions(group, [self.document])

    def test_held_or_empty_context_cannot_be_published(self):
        for verdict, contexts in (('hold', ['g-000']), ('admit', [])):
            doc = copy.deepcopy(self.document)
            doc['decisions'][0].update(verdict=verdict, context_ids=contexts)
            with self.assertRaises(ValueError):
                p.validate_decisions(self.groups, [doc])

    def test_taxonomy_needs_full_inventory_and_four_gendered_languages(self):
        self.assertEqual(p.validate_taxonomy(self.taxonomy, [{'category': 'trade', 'role_key': 'doctor'}])[0]['trade:doctor']['slug'], 'physician')
        for mutation in ('missing_role', 'missing_language', 'missing_gender', 'pending'):
            taxonomy = copy.deepcopy(self.taxonomy)
            if mutation == 'missing_role':
                taxonomy['mappings'] = []
            elif mutation == 'missing_language':
                del taxonomy['occupations'][0]['labels']['ukr']
            elif mutation == 'missing_gender':
                taxonomy['occupations'][0]['labels']['ukr']['feminine'] = ''
            else:
                taxonomy['mappings'][0]['why'] = 'Pending taxonomy reading.'
            with self.assertRaises(ValueError):
                p.validate_taxonomy(taxonomy, [{'category': 'trade', 'role_key': 'doctor'}])

    def test_printed_and_canonical_numbers_remain_distinct(self):
        source = copy.deepcopy(self.context['sources'][0])
        source['canonical'] = [51, 4, 15]
        source['canonicalAddresses'].append([51, 4, 15])
        result = p.witness(source)
        self.assertEqual(result['reference'], '51 4:14')
        self.assertEqual(result['canonicalReference'], '51 4:15')
        source['canonical'] = [51, 4, 16]
        with self.assertRaises(ValueError):
            p.witness(source)

    def test_paraphrase_is_not_an_exact_source_quote(self):
        source = copy.deepcopy(self.context['sources'][0])
        source['quote'] = 'medical doctor'
        with self.assertRaises(ValueError):
            p.witness(source)

    def test_source_hash_is_of_the_exact_full_verse(self):
        source = self.context['sources'][0]
        self.assertEqual(p.witness(source)['sourceSha256'], hashlib.sha256(source['body'].encode()).hexdigest())


@unittest.skipUnless((p.RUN / 'bundle.json').exists(), 'Frozen full publication is not available.')
class FullPublicationProof(unittest.TestCase):
    def test_every_group_and_person_is_accounted_and_baseline_pairs_survive(self):
        summary = p.read(p.RUN / 'publication-summary.json')
        self.assertEqual(summary['reviewed_groups'], 1281)
        self.assertEqual(summary['accepted_raw_groups'] + summary['not_admitted_raw_groups'] + 1, 1281)
        self.assertEqual(summary['preserved_previous_pairs'], 16)
        self.assertEqual(len(p.read(p.RUN / 'coverage-dispositions.json')), 3176)
        self.assertEqual(len(p.read(p.RUN / 'observation-dispositions.json')['decisions']), 6169)
        self.assertEqual(len(p.read(p.RUN / 'terminal-review.json')['findings']), 4)

    def test_changed_frozen_evidence_is_detected_by_file_hash(self):
        manifest = p.verify_snapshot()
        self.assertEqual(manifest['source_errors'], 0)

    def test_identical_inputs_compile_identical_publication_bytes(self):
        before = p.sha(p.RUN / 'bundle.json')
        with contextlib.redirect_stdout(io.StringIO()):
            p.compile_publication()
        self.assertEqual(p.sha(p.RUN / 'bundle.json'), before)


if __name__ == '__main__':
    unittest.main()
