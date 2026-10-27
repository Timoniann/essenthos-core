"""Offline guardrails for expensive occupation extraction; never makes API calls."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import person_occupations as p


class OccupationTests(unittest.TestCase):
    def packet(self):
        return {'job_id':'job','persons':[{'slug':'luke'}],
            'focus_keys':['KJV:51:4:14','NESTLE1904:51:4:14'],
            'verses':[{'key':'KJV:51:4:14','body':'Luke, the beloved physician, and Demas, greet you.'},
                {'key':'NESTLE1904:51:4:14','body':'Λουκᾶς ὁ ἰατρὸς ὁ ἀγαπητὸς καὶ Δημᾶς.'}]}

    def finding(self):
        return {'category':'trade','role_key':'physician','label_en':'physician','label_uk':'лікар',
            'source_term':'ἰατρὸς','basis':'explicit','time_scope':'present_in_passage',
            'evidence':[{'verse':'KJV:51:4:14','quote':'Luke, the beloved physician'},
                {'verse':'NESTLE1904:51:4:14','quote':'Λουκᾶς ὁ ἰατρὸς'}],
            'identity_evidence':[{'verse':'KJV:51:4:14','quote':'Luke, the beloved physician'}],
            'reason_uk':'Луку прямо названо лікарем у цьому вірші.'}

    def answer(self):
        return {'job_id':'job','persons':[{'person':'luke','findings':[self.finding()],
            'needs_review':[],'not_occupations':[]}]}

    def test_exact_profession_identity_and_original_are_required(self):
        self.assertEqual([],p.validate(self.answer(),self.packet(),'draft'))

    def test_omitted_person_is_not_a_negative_finding(self):
        answer=self.answer();answer['persons']=[]
        self.assertIn('missing/extra/duplicate persons',p.validate(answer,self.packet(),'draft'))

    def test_duplicate_person_is_refused(self):
        answer=self.answer();answer['persons']*=2
        self.assertIn('missing/extra/duplicate persons',p.validate(answer,self.packet(),'draft'))

    def test_offscope_person_is_refused(self):
        answer=self.answer();answer['persons'][0]['person']='paul'
        self.assertIn('missing/extra/duplicate persons',p.validate(answer,self.packet(),'draft'))

    def test_no_finding_is_valid_but_not_claim_of_no_profession(self):
        answer=self.answer();answer['persons'][0]['findings']=[]
        self.assertEqual([],p.validate(answer,self.packet(),'draft'))

    def test_fabricated_quote_cannot_be_accepted(self):
        finding=self.finding();finding['evidence'][0]['quote']='Luke was a painter'
        self.assertTrue(any('nonverbatim' in e for e in p.finding_errors(finding,self.packet())))

    def test_unknown_witness_is_refused(self):
        finding=self.finding();finding['identity_evidence'][0]['verse']='KJV:1:1:999'
        self.assertIn('unknown quote key',p.finding_errors(finding,self.packet()))

    def test_identity_only_cannot_establish_profession(self):
        packet=self.packet();packet['focus_keys']=[]
        self.assertIn('no focus profession witness',p.finding_errors(self.finding(),packet))

    def test_original_language_cannot_be_silently_ignored(self):
        finding=self.finding();finding['evidence']=finding['evidence'][:1]
        self.assertIn('original language witness missing',p.finding_errors(finding,self.packet()))

    def test_source_term_is_not_a_guessed_lemma(self):
        finding=self.finding();finding['source_term']='ἰατρός'
        self.assertIn('source_term not quoted',p.finding_errors(finding,self.packet()))

    def test_nfc_equivalence_but_not_mark_stripping(self):
        self.assertTrue(p.quoted('שֶׁמה','שֶׁמה'))
        self.assertFalse(p.quoted('שמהא','שֶׁמהא'))

    def test_moral_label_has_no_profession_category(self):
        finding=self.finding();finding['category']='moral_label'
        self.assertIn('invalid category',p.finding_errors(finding,self.packet()))

    def test_moral_observation_is_preserved_separately(self):
        answer=self.answer();person=answer['persons'][0];person['findings']=[]
        person['not_occupations']=[{'label':'physician','kind':'example','reason_uk':'Відокремлене спостереження.',
            'evidence':[{'verse':'KJV:51:4:14','quote':'Luke, the beloved physician'}]}]
        self.assertEqual([],p.validate(answer,self.packet(),'draft'))

    def test_duplicate_occupation_is_refused(self):
        answer=self.answer();answer['persons'][0]['findings']*=2
        self.assertIn('duplicate role for luke',p.validate(answer,self.packet(),'draft'))

    def test_profession_key_is_normalized(self):
        finding=self.finding();finding['role_key']='A Physician!'
        self.assertIn('invalid role_key',p.finding_errors(finding,self.packet()))

    def test_check_covers_each_actual_draft_fact_once(self):
        packet=self.packet();packet['draft_facts']=p.facts_from_draft(self.answer())
        fact=packet['draft_facts'][0]
        answer={'job_id':'job','verdicts':[{'id':fact['id'],'verdict':'accept','finding':self.finding(),'reason_uk':'Прямий текстовий доказ.'}]}
        self.assertEqual([],p.validate(answer,packet,'check'))
        answer['verdicts']=[]
        self.assertIn('missing/extra/duplicate fact IDs',p.validate(answer,packet,'check'))

    def test_budget_reserves_input_cache_and_maximum_output(self):
        request={'max_tokens':1000,'prompt':'source'}
        with patch.object(p,'budget',return_value={'draft':0,'check':0}):
            regular=p.reserve([('x',request)],'draft',False)
            batch=p.reserve([('x',request)],'draft',True)
            self.assertGreater(regular['reserved_usd'],.01)
            self.assertEqual(regular['reserved_usd']/2,batch['reserved_usd'])
        with patch.object(p,'budget',return_value={'draft':p.DRAFT_CAP,'check':0}):
            with self.assertRaisesRegex(ValueError,'budget'):p.reserve([('x',request)],'draft',True)
        with patch.object(p,'budget',return_value={'draft':60,'check':40}):
            with self.assertRaisesRegex(ValueError,'budget'):p.reserve([('x',request)],'check',True)

    def test_unknown_usage_is_not_free(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'MAIN',Path(directory)):
            ledger=p.MAIN/'.claude-api/usage.jsonl';ledger.parent.mkdir()
            ledger.write_text(json.dumps({'label':p.LABEL+'-draft','id':'msg','usd':None}),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'Unknown'):p.budget()

    def test_cost_includes_both_stages_and_deduplicates_only_same_message(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'MAIN',Path(directory)):
            ledger=p.MAIN/'.claude-api/usage.jsonl';ledger.parent.mkdir()
            rows=[{'label':p.LABEL+'-draft','id':'a','usd':1.0}]*2
            rows+=[{'label':p.LABEL+'-check','id':'b','usd':2.0},{'label':'other-feature','id':'c','usd':999}]
            ledger.write_text('\n'.join(json.dumps(r) for r in rows),encoding='utf-8')
            self.assertEqual({'draft':1.0,'check':2.0},p.budget())

    def test_request_is_pinned_without_fallback(self):
        request=p.parameters(self.packet(),'draft')
        self.assertEqual(p.MODEL,request['model']);self.assertNotIn('fallbacks',request)
        self.assertEqual('high',request['output_config']['effort'])

    def test_paid_pilot_is_exclusive_before_client_is_loaded(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(p,'OUT',Path(directory)):
            (p.OUT/'controller.lock').write_text('owned',encoding='utf-8')
            with patch.object(p,'api',side_effect=AssertionError('must not load API')):
                with self.assertRaises(FileExistsError):p.pilot()


if __name__=='__main__':unittest.main()
