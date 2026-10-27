"""Source, identity, owner-scope and publication regressions for the hundred-case review."""
import contextlib
import copy
import io
import unittest
import review_held_occupations as p


class ReviewGuards(unittest.TestCase):
    def setUp(self):
        self.candidates = [{'id':'a','person':'hodiah-2'}]
        self.verses = {'KJV:16:8:7': {'canonical':[16,8,7]}}
        self.people = {'hodiah-2': {'references':[{'book':16,'chapter':8,'verse':7}]}}
        self.document = {'decisions':[{'id':'a','person':'hodiah-2','verdict':'reframed',
            'why':'The Law teacher is named in this specific verse.',
            'reviewed_source_keys':['KJV:16:8:7'],
            'claims':[{'occupationSlug':'teacher-of-the-law','source_keys':['KJV:16:8:7']}]}]}

    def test_complete_source_bound_claim_passes(self):
        p.validate_review(self.document, self.candidates, self.verses, self.people)

    def test_missing_duplicate_or_different_person_cannot_pass(self):
        for kind in ('missing','duplicate','person'):
            document = copy.deepcopy(self.document)
            if kind == 'missing': document['decisions'] = []
            elif kind == 'duplicate': document['decisions'] *= 2
            else: document['decisions'][0]['person'] = 'hodiah-3'
            with self.assertRaises(ValueError):
                p.validate_review(document, self.candidates, self.verses, self.people)

    def test_unresolved_or_unread_source_cannot_publish(self):
        for kind in ('held','unread','foreign'):
            document = copy.deepcopy(self.document)
            if kind == 'held': document['decisions'][0]['verdict'] = 'needs_owner'
            elif kind == 'unread': document['decisions'][0]['reviewed_source_keys'] = ['other']
            else: document['decisions'][0]['claims'][0]['source_keys'] = ['other']
            with self.assertRaises(ValueError):
                p.validate_review(document, self.candidates, self.verses, self.people)

    def test_similarly_named_signer_cannot_receive_the_law_teachers_role(self):
        self.people['hodiah-2']['references'] = [{'book':16,'chapter':10,'verse':13}]
        with self.assertRaises(ValueError):
            p.validate_review(self.document, self.candidates, self.verses, self.people)


class FullReviewProof(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.document = p.read(p.RUN / 'decisions.json')
        cls.people = {person['slug']:person for person in p.read(p.RUN / 'people.json')}
        cls.bundle = p.read(p.RUN / 'bundle.json')

    def test_all_hundred_final_dispositions_are_source_bound(self):
        p.validate_review(self.document, p.read(p.RUN/'candidates.json'), p.read(p.RUN/'source-verses.json'), self.people)
        self.assertEqual(len(self.document['decisions']),100)
        self.assertNotIn('needs_owner', {d['verdict'] for d in self.document['decisions']})

    def test_named_levite_cases_do_not_cross_epochs_or_homonyms(self):
        claims = {d['person']:{c['occupationSlug'] for c in d['claims']} for d in self.document['decisions']}
        self.assertEqual(claims['hodiah-2'], {'teacher-of-the-law'})
        self.assertEqual(claims['hodiah-3'], set())
        self.assertEqual(claims['jehiel-4'], {'levite-temple-servant'})

    def test_heavenly_priesthood_is_never_earthly_priesthood(self):
        concepts = {c['slug']:c for c in self.bundle['occupations']}
        jesus = {a['occupationSlug'] for a in self.bundle['assertions'] if a['personSlug']=='jesus'}
        self.assertIn('heavenly-high-priest',jesus)
        self.assertNotIn('high-priest',jesus)
        self.assertNotIn('priest',jesus)
        for slug in ('heavenly-high-priest','heavenly-apostle','heavenly-prince','heavenly-commander','heavenly-ruler-of-kings'):
            self.assertEqual(concepts[slug]['category'],'heavenly_role')
            self.assertEqual(set(concepts[slug]['labels']),{'eng','ukr','deu','spa'})
            self.assertEqual(concepts[slug]['labels']['ukr']['description'],'Небесна роль')

    def test_owner_case_rulings_preserve_false_qualifier_and_skip_jezebel(self):
        pairs = {(a['personSlug'],a['occupationSlug']) for a in self.bundle['assertions']}
        self.assertIn(('barjesus','false-prophet'),pairs)
        self.assertIn(('ahab-2','false-prophet'),pairs)
        self.assertNotIn(('barjesus','prophet'),pairs)
        self.assertFalse(any(person=='jezebel-2' for person,role in pairs))
        self.assertIn(('elisha','farmer'),pairs)

    def test_roster_correction_keeps_pauls_independently_attested_teaching(self):
        pairs = {(a['personSlug'],a['occupationSlug']) for a in self.bundle['assertions']}
        for person in ('manaen','simeon-4','lucius'):
            self.assertIn((person,'prophet-or-teacher'),pairs)
            self.assertNotIn((person,'prophet'),pairs)
            self.assertNotIn((person,'religious-teacher'),pairs)
        self.assertIn(('saul-2','religious-teacher'),pairs)
        self.assertNotIn(('saul-2','prophet'),pairs)
        baseline = p.read(p.MAIN/'Resources/Essenthos/generation/occupation-publication-2026-10-27-v1/bundle.json')
        self.assertTrue({(a['personSlug'],a['occupationSlug']) for a in baseline['assertions']}.issubset(pairs))

    def test_same_inputs_reproduce_all_bundle_bytes(self):
        before = p.sha(p.RUN/'bundle.json')
        with contextlib.redirect_stdout(io.StringIO()):
            p.compile_publication()
        self.assertEqual(p.sha(p.RUN/'bundle.json'),before)


if __name__ == '__main__':
    unittest.main()
