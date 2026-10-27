import copy
import unittest

import person_occupation_inputs as packets


class CoverageTests(unittest.TestCase):
    def setUp(self):
        self.people = [dict(slug='person', references=[dict(book=1,chapter=2,verse=3)], jobs=['j']),
                       dict(slug='alias', aliasOf='person', references=[], jobs=[]),
                       dict(slug='zero', references=[], jobs=[])]
        self.jobs = [dict(job_id='j', persons=[dict(slug='person',indexed_references=[[1,2,3]])],
             verses=[dict(key='KJV:1:2:3',canonical=[1,2,3],canonicalAddresses=[[1,2,3]],role='event'),
                     dict(key='KJV:1:1:1',canonical=[1,1,1],canonicalAddresses=[[1,1,1]],role='identity')],
             focus_keys=['KJV:1:2:3'])]

    def test_alias_and_zero_reference_records_are_retained(self):
        self.assertTrue(packets.validate_coverage(self.people,self.jobs))

    def test_missing_reference_is_rejected(self):
        self.jobs[0]['persons'][0]['indexed_references']=[]
        with self.assertRaisesRegex(ValueError,'Incomplete reference'):
            packets.validate_coverage(self.people,self.jobs)

    def test_secondary_mapping_can_cover_an_indexed_address(self):
        self.jobs[0]['verses'][0]['canonical']=[1,2,2]
        self.jobs[0]['verses'][0]['canonicalAddresses']=[[1,2,2],[1,2,3]]
        self.assertTrue(packets.validate_coverage(self.people,self.jobs))

    def test_unmapped_reference_is_rejected(self):
        self.jobs[0]['verses'][0]['canonicalAddresses']=[[1,2,2]]
        with self.assertRaisesRegex(ValueError,'not in event'):
            packets.validate_coverage(self.people,self.jobs)

    def test_identity_text_cannot_cover_event(self):
        self.jobs[0]['verses'][0]['role']='identity'
        with self.assertRaisesRegex(ValueError,'Focus must be event'):
            packets.validate_coverage(self.people,self.jobs)

    def test_inventory_assignment_must_match_job(self):
        self.people[0]['jobs']=[]
        with self.assertRaisesRegex(ValueError,'assignment mismatch'):
            packets.validate_coverage(self.people,self.jobs)

    def test_duplicate_verse_key_is_rejected(self):
        self.jobs[0]['verses'].append(copy.deepcopy(self.jobs[0]['verses'][0]))
        with self.assertRaisesRegex(ValueError,'Duplicate verse'):
            packets.validate_coverage(self.people,self.jobs)

    def test_unknown_person_is_rejected(self):
        self.jobs[0]['persons'][0]['slug']='unknown'
        with self.assertRaisesRegex(ValueError,'Unknown job person'):
            packets.validate_coverage(self.people,self.jobs)

    def test_duplicate_job_is_rejected(self):
        self.jobs.append(copy.deepcopy(self.jobs[0]))
        with self.assertRaisesRegex(ValueError,'Duplicate inventory or job'):
            packets.validate_coverage(self.people,self.jobs)


class CurrentIndexTests(unittest.TestCase):
    def test_letter_opening_candidate_requires_an_indexed_opening_name(self):
        self.assertEqual(packets.opening_candidate(46, {'saul-2': {(46,1,1):{}}}), 'saul-2')
        self.assertIsNone(packets.opening_candidate(46, {}))

    def test_an_unnamed_elder_does_not_become_a_named_author(self):
        self.assertIsNone(packets.opening_candidate(64, {'john-2': {(64,1,1):{}}}))

    def test_sql_is_read_only_timeout_bounded_and_all_sources(self):
        self.assertIn('REPEATABLE READ READ ONLY',packets.CURRENT_SQL)
        self.assertEqual(packets.CURRENT_SQL.count('SET statement_timeout'),6)
        self.assertNotIn('NOT LIKE',packets.CURRENT_SQL)
        self.assertNotIn('BibleData by',packets.CURRENT_SQL)
        self.assertNotIn('password',packets.CURRENT_SQL.lower())
        self.assertNotIn("'id'",packets.CURRENT_SQL)

    def test_names_and_reference_deltas_are_explicit(self):
        before=dict(entity=[dict(slug='person',kind='person',name='Old')],
                    reference=[dict(slug='person',book=1,chapter=2,verse=3)])
        current=dict(entity=[dict(slug='person',kind='person',name='New'),dict(slug='alias',kind='person',name='Alias')],
            reference=[dict(slug='person',book=1,chapter=2,verse=3,sources=['old']),
                       dict(slug='alias',book=1,chapter=2,verse=4,sources=['new'])])
        result=packets.delta(current,before)
        self.assertEqual(result['added_person_slugs'],['alias'])
        self.assertEqual(result['added_references'],[['alias',1,2,4]])
        self.assertEqual(result['changed_person_names'][0]['after'],'New')
        self.assertEqual(result['added_reference_sources'],{'new':1})


if __name__ == '__main__':
    unittest.main()
