"""Admission adds only reviewed supported evidence and never removes supported global pairs."""
import copy
import unittest

from compile_presence_closure import extend,object_sha


class CompilationTests(unittest.TestCase):
    def base(self):
        return {'records':[{'person':'p','place':'old','category':'present','addresses':[1001001],
            'evidence':[{'original':'kept'}]}]}

    def inputs(self,decision='supported',place='new'):
        row={'case_id':'p:1','decision':decision,'admit_presence':decision=='supported','status':'closed',
            'human_accepted':False,'target_place':place,'category':'present',
            'evidence':[{'verse':'BHSA:1:1:1','quote':'שֶׁם'}], 'external_source_ids':[],
            'provenance':{'job_id':'job'}}
        packets={'job':{'verses':[{'key':'BHSA:1:1:1','body':'שֶׁם כתוב',
            'canonicalAddresses':[[1,1,1],[1,1,2]]}]}}
        cases={'p:1':{'person':'p','source_kind':'disagreement','original':{'source_pointer':{'person':'p','needs_review_index':1}}}}
        return [row],packets,cases

    def test_supported_context_adds_pair_and_every_canonical_placement(self):
        rows,delta=extend(self.base(),*self.inputs())
        self.assertEqual(2,len(rows));self.assertEqual(1,delta['added_pairs'])
        self.assertEqual([1001001,1001002],next(r for r in rows if r['place']=='new')['addresses'])

    def test_closed_ambiguity_does_not_admit_a_visit(self):
        base=self.base(); rows,delta=extend(base,*self.inputs('closed_ambiguous'))
        self.assertEqual(base['records'],rows);self.assertEqual(0,delta['supported_contexts'])

    def test_closed_unsupported_cannot_remove_an_existing_global_pair(self):
        base=self.base(); rows,delta=extend(base,*self.inputs('closed_unsupported','old'))
        self.assertEqual(base['records'],rows);self.assertEqual(0,delta['added_pairs'])

    def test_another_supported_episode_adds_evidence_without_duplicate_pair(self):
        base=self.base(); rows,delta=extend(base,*self.inputs(place='old'))
        self.assertEqual(1,len(rows));self.assertEqual(0,delta['added_pairs'])
        self.assertEqual([{'original':'kept'}],base['records'][0]['evidence'])
        self.assertEqual(2,len(rows[0]['evidence']))

    def test_fabricated_quote_is_refused(self):
        rows,packets,cases=self.inputs();rows[0]['evidence'][0]['quote']='invented'
        with self.assertRaises(AssertionError):extend(self.base(),rows,packets,cases)

    def test_cannot_falsely_admit_a_closed_case(self):
        rows,packets,cases=self.inputs('closed_ambiguous');rows[0]['admit_presence']=True
        with self.assertRaises(AssertionError):extend(self.base(),rows,packets,cases)

    def test_only_an_explicit_complete_source_bound_withdrawal_removes_a_pair(self):
        base=self.base();old=base['records'][0]
        withdrawal={'person':'p','place':'old','category':'present','decision':'withdraw',
            'original_record_sha256':object_sha(old),'reason_uk':'All previous witnesses checked: only vicinity.',
            'evidence':[{'verse':'KJV:1:1:1','quote':'before the city'}]}
        rows,delta=extend(base,*self.inputs('closed_unsupported'),[withdrawal])
        self.assertEqual([],rows);self.assertEqual(1,delta['withdrawn_pairs'])
        self.assertEqual(0,delta['added_pairs']);self.assertEqual(-1,delta['net_pairs'])
        self.assertEqual([old],base['records'])
        withdrawal['original_record_sha256']='stale'
        with self.assertRaises(AssertionError):extend(base,*self.inputs('closed_unsupported'),[withdrawal])


if __name__=='__main__':unittest.main()
