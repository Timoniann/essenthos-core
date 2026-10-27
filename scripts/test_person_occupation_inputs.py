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


class RenameBridgeTests(unittest.TestCase):
    def setUp(self):
        self.keys = [edition+':44:13:9' for edition in ('KJV','BSB','NESTLE1904')]
        self.sources = {name:dict(text=name.split(':')[0],body='Saul who is also called Paul',
            canonical=[44,13,9],canonicalAddresses=[[44,13,9]]) for name in self.keys}
        self.addresses = {(44,13,9):self.keys}
        self.packet = dict(persons=[dict(slug='saul-2')],verses=[],focus_keys=[],source_metadata={})

    def test_all_held_languages_are_identity_only(self):
        result=packets.add_identity_bridges(self.packet,self.sources,self.addresses)
        self.assertEqual(set(self.keys),{v['key'] for v in result['verses']})
        self.assertTrue(all(v['role']=='identity' for v in result['verses']))
        self.assertEqual([],result['focus_keys'])
        self.assertTrue(packets.validate_identity_bridges([result]))

    def test_missing_required_witness_fails_closed(self):
        self.addresses[(44,13,9)]=self.keys[:2]
        with self.assertRaisesRegex(ValueError,'Required identity witness missing'):
            packets.add_identity_bridges(self.packet,self.sources,self.addresses)

    def test_existing_event_is_not_demoted(self):
        self.packet['focus_keys']=self.keys[:1]
        self.packet['verses']=[dict(key=self.keys[0],**self.sources[self.keys[0]],role='event')]
        result=packets.add_identity_bridges(self.packet,self.sources,self.addresses)
        self.assertEqual('event',result['verses'][0]['role'])
        self.assertTrue(packets.validate_identity_bridges([result]))

    def test_existing_context_becomes_identity_without_duplicate(self):
        self.packet['verses']=[dict(key=self.keys[0],**self.sources[self.keys[0]],role='context')]
        result=packets.add_identity_bridges(self.packet,self.sources,self.addresses)
        self.assertEqual(3,len(result['verses']))
        self.assertTrue(all(v['role']=='identity' for v in result['verses']))

    def test_bridge_may_not_be_promoted_outside_focus(self):
        result=packets.add_identity_bridges(self.packet,self.sources,self.addresses)
        result['verses'][0]['role']='event'
        with self.assertRaisesRegex(ValueError,'outside focus'):
            packets.validate_identity_bridges([result])

    def test_unlisted_person_does_not_inherit_a_bridge(self):
        self.packet['persons']=[dict(slug='saul')]
        result=packets.add_identity_bridges(self.packet,self.sources,self.addresses)
        self.assertEqual([],result['verses'])


if __name__ == '__main__':
    unittest.main()
