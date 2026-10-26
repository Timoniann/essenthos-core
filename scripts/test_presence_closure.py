"""Offline gates for exact source evidence and closed uncertainty; never calls a model."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import presence_closure as p


class ClosureTests(unittest.TestCase):
    def packet(self, hebrew=True):
        verses=[{'key':'KJV:2:2:3','body':'she laid it in the flags by the river',
            'canonical':[2,2,3],'canonicalAddresses':[[2,2,3]]}]
        if hebrew:
            verses.append({'key':'BHSA:2:2:3','body':'וַתָּ֣שֶׂם בַּסּ֖וּף עַל־שְׂפַ֥ת הַיְאֹֽר׃',
                'canonical':[2,2,3],'canonicalAddresses':[[2,2,3]]})
        return {'job_id':'test','cases':[{'case_id':'moses:2','place_slug':'nile','category':'present',
            'focus_keys':['KJV:2:2:3'],'hebrew_available':hebrew,'allowed_external_sources':['primary1']}],
            'verses':verses,'registered_places':[{'slug':'nile'}]}

    def answer(self,hebrew=True):
        evidence=[{'verse':'KJV:2:2:3','quote':'she laid it in the flags','role':'event'}]
        if hebrew:
            evidence.append({'verse':'BHSA:2:2:3','quote':'הַיְאֹֽר׃','role':'hebrew'})
        return {'job_id':'test','decisions':[{'case_id':'moses:2','decision':'closed_ambiguous',
            'target_place':'nile','category':'present','reason_code':'uncertain_place',
            'reason_uk':'Перевірений текст лишає відкритою ідентичність конкретного місця.',
            'identity_context_uk':'Ідеться про немовля Мойсея; самої назви річки недостатньо.',
            'checked_hebrew':hebrew,'evidence':evidence,'external_source_ids':['primary1']}]}

    def assertInvalid(self,answer,packet,text):
        self.assertTrue(any(text in e for e in p.validate(answer,packet)),p.validate(answer,packet))

    def test_closed_ambiguity_is_valid_and_does_not_assert_a_visit(self):
        self.assertEqual([],p.validate(self.answer(),self.packet()))
        self.assertNotEqual('supported',self.answer()['decisions'][0]['decision'])

    def test_closed_unsupported_is_valid(self):
        answer=self.answer();answer['decisions'][0]['decision']='closed_unsupported'
        self.assertEqual([],p.validate(answer,self.packet()))

    def test_unresolved_is_not_a_final_disposition(self):
        answer=self.answer();answer['decisions'][0]['decision']='unresolved'
        self.assertInvalid(answer,self.packet(),'decision')

    def test_duplicate_case_is_refused(self):
        answer=self.answer();answer['decisions']*=2
        self.assertInvalid(answer,self.packet(),'duplicate')

    def test_missing_case_is_refused(self):
        self.assertInvalid({'job_id':'test','decisions':[]},self.packet(),'missing')

    def test_unknown_case_is_refused(self):
        answer=self.answer();answer['decisions'][0]['case_id']='other:1'
        self.assertInvalid(answer,self.packet(),'extra')

    def test_fabricated_quotation_is_refused(self):
        answer=self.answer();answer['decisions'][0]['evidence'][0]['quote']='Moses was beside the Nile'
        self.assertInvalid(answer,self.packet(),'nonverbatim')

    def test_hebrew_marks_cannot_be_silently_stripped(self):
        answer=self.answer();answer['decisions'][0]['evidence'][1]['quote']='היאר'
        self.assertInvalid(answer,self.packet(),'nonverbatim')

    def test_only_canonical_unicode_order_can_differ(self):
        self.assertTrue(p.quoted('שֶׁ','שֶׁ'))
        self.assertFalse(p.quoted('שה','שֶׁה'))

    def test_unknown_witness_is_refused(self):
        answer=self.answer();answer['decisions'][0]['evidence'][0]['verse']='KJV:2:2:999'
        self.assertInvalid(answer,self.packet(),'invalid witness')

    def test_event_witness_is_required_even_for_closure(self):
        answer=self.answer();answer['decisions'][0]['evidence']=answer['decisions'][0]['evidence'][1:]
        self.assertInvalid(answer,self.packet(),'original episode')

    def test_hebrew_must_be_read_when_available(self):
        answer=self.answer();answer['decisions'][0]['checked_hebrew']=False
        self.assertInvalid(answer,self.packet(),'Hebrew was not quoted')

    def test_claimed_hebrew_read_without_quote_is_refused(self):
        answer=self.answer();answer['decisions'][0]['evidence']=answer['decisions'][0]['evidence'][:1]
        self.assertInvalid(answer,self.packet(),'Hebrew was not quoted')

    def test_new_testament_has_no_invented_hebrew(self):
        self.assertEqual([],p.validate(self.answer(False),self.packet(False)))
        answer=self.answer(False);answer['decisions'][0]['checked_hebrew']=True
        self.assertInvalid(answer,self.packet(False),'invented Hebrew')

    def test_supported_needs_all_three_evidence_roles(self):
        answer=self.answer();row=answer['decisions'][0]
        row.update(decision='supported',reason_code='context_chain')
        self.assertInvalid(answer,self.packet(),'event/identity/location')
        row['evidence'] += [{'verse':'KJV:2:2:3','quote':'she laid it in the flags','role':role}
            for role in ('identity','location')]
        self.assertEqual([],p.validate(answer,self.packet()))

    def test_supported_needs_target_and_locating_chain(self):
        answer=self.answer();row=answer['decisions'][0];row.update(decision='supported',target_place=None)
        self.assertInvalid(answer,self.packet(),'without target')
        self.assertInvalid(answer,self.packet(),'without evidence chain')

    def test_unknown_or_replaced_place_is_refused(self):
        answer=self.answer();answer['decisions'][0]['target_place']='river-other'
        self.assertInvalid(answer,self.packet(),'unknown exact target')
        self.assertInvalid(answer,self.packet(),'silently replaced')

    def test_unsupported_external_source_is_refused(self):
        answer=self.answer();answer['decisions'][0]['external_source_ids']=['invented-url']
        self.assertInvalid(answer,self.packet(),'external source')

    def test_burial_cannot_turn_into_living_presence(self):
        packet=self.packet();packet['cases'][0]['category']='buried'
        self.assertInvalid(self.answer(),packet,'category changed')

    def test_budget_reserves_output_and_cache_writing_before_paying(self):
        params={'max_tokens':1000,'model':p.MODEL,'text':'example'}
        with patch.object(p,'budget',return_value=(0.0,120.0)):
            regular=p.reserve([('job',params)],False)
            batch=p.reserve([('job',params)],True)
            self.assertGreater(regular['reserved_usd'],0.01)
            self.assertEqual(regular['reserved_usd']/2,batch['reserved_usd'])
        with patch.object(p,'budget',return_value=(p.CAP,120.0)):
            with self.assertRaisesRegex(ValueError,'Budget'):p.reserve([('job',params)],False)
        with patch.object(p,'budget',return_value=(0.0,p.TOTAL_CAP)):
            with self.assertRaisesRegex(ValueError,'Budget'):p.reserve([('job',params)],True)

    def test_unknown_request_outcome_cannot_be_resubmitted(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'OUT',Path(directory)):
            p.dump(p.OUT/'inputs/job.json',{'cases':[]})
            p.dump(p.OUT/'raw/job-1-request.json',{})
            with self.assertRaisesRegex(ValueError,'Unknown prior request'):p.request('job')

    def test_another_paid_controller_blocks_pilot_before_client_is_loaded(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'OUT',Path(directory)):
            (p.OUT/'controller.lock').write_text('123',encoding='utf-8')
            with patch.object(p,'api',side_effect=AssertionError('must not load API client')):
                with self.assertRaises(FileExistsError):p.pilot()

    def test_deduplicate_actual_usage_ids_not_distinct_responses(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'MAIN',Path(directory)):
            ledger=p.MAIN/'.claude-api/usage.jsonl';ledger.parent.mkdir()
            rows=[{'label':p.LABEL,'id':'same','usd':1.5}]*2
            rows += [{'label':p.LABEL,'id':'other','usd':2.0},
                {'label':p.LABELS[1],'id':'old','usd':100.0}]
            ledger.write_text('\n'.join(json.dumps(r) for r in rows),encoding='utf-8')
            self.assertEqual((3.5,103.5),p.budget())
            rows.append({'label':p.LABEL,'id':'same','usd':2.5})
            ledger.write_text('\n'.join(json.dumps(r) for r in rows),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'Conflicting duplicate'):p.budget()

    def test_unknown_cost_is_not_treated_as_free(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'MAIN',Path(directory)):
            ledger=p.MAIN/'.claude-api/usage.jsonl';ledger.parent.mkdir()
            ledger.write_text(json.dumps({'label':p.LABEL,'id':'unknown','usd':None}),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'Unknown price'):p.budget()

    def test_request_is_pinned_and_does_not_enable_subscription_or_fallback(self):
        params=p.parameters({'cases':[{}]})
        self.assertEqual(p.MODEL,params['model'])
        self.assertEqual('high',params['output_config']['effort'])
        self.assertNotIn('fallbacks',params)

    def test_original_inventory_has_exactly201_distinct_cases(self):
        self.assertEqual(201,len(p.wanted()))


if __name__=='__main__':unittest.main()
