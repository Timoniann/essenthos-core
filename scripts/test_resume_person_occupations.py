"""Offline recovery invariants for paid draft retention and isolated retries."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch,Mock
from types import SimpleNamespace
import resume_person_occupations as r


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        root=Path(self.temp.name);self.base=root/'base';self.out=root/'new'
        self.base.mkdir();self.out.mkdir()
        for module,name,value in ((r,'BASE',self.base),(r,'OUT',self.out),(r.p,'OUT',self.base)):
            context=patch.object(module,name,value);context.start();self.addCleanup(context.stop)

    def packet(self):
        return dict(job_id='D',persons=[dict(slug='luke',name='Luke')],focus_keys=['NESTLE1904:51:4:14'],
            verses=[dict(key='NESTLE1904:51:4:14',body='Λουκᾶς ὁ ἰατρὸς',canonical=[51,4,14],
                canonicalAddresses=[[51,4,14]],role='event')],source_metadata={})

    def answer(self,packet=None):
        packet=packet or self.packet()
        return dict(job_id=packet['job_id'],persons=[dict(person=p['slug'],findings=[],needs_review=[],not_occupations=[]) for p in packet['persons']])

    def retained(self):
        packet=self.packet();answer=self.answer(packet);job=packet['job_id']
        source=self.base/'draft/inputs/D.json';r.write(source,packet)
        raw=self.base/'draft/raw/D-1-message.json'
        r.write(raw,dict(id='message',model=r.p.MODEL,stop_reason='end_turn',content=[dict(type='text',text=json.dumps(answer))]))
        checked=self.base/'draft/checked/D.json'
        r.write(checked,dict(model=r.p.MODEL,input_sha256=r.p.sha(source),attempt=1,message_id='message',answer=answer))
        entry=dict(job_id=job,input_sha256=r.p.sha(source),checked_sha256=r.p.sha(checked),raw_sha256=r.p.sha(raw),
            raw_file=raw.name,answer_sha256=r.p.digest(answer))
        return dict(retained=[entry],new_drafts=[],parents=[dict(job_id='D',units=['D'],persons=['luke'])])

    def test_split_preserves_every_person_once_and_all_source_bytes(self):
        packet=self.packet();packet['persons']=[dict(slug=str(n)) for n in range(13)]
        parts=r.split_packet(packet,6)
        self.assertEqual([6,6,1],[len(p['persons']) for p in parts])
        self.assertEqual(packet['persons'],[p for part in parts for p in part['persons']])
        for part in parts:
            for field in ('verses','focus_keys','source_metadata'):self.assertEqual(packet[field],part[field])
        parts[0]['verses'][0]['body']='changed'
        self.assertNotEqual(parts[0]['verses'],packet['verses'])

    def test_retained_result_is_validated_without_loading_the_API(self):
        m=self.retained()
        with patch.object(r.p,'api',side_effect=AssertionError('no paid operation')):
            self.assertEqual(self.answer(),r.result_file(m,'draft','D')['answer'])

    def test_each_retained_input_raw_and_checked_hash_is_bound(self):
        for relative in ('draft/inputs/D.json','draft/raw/D-1-message.json','draft/checked/D.json'):
            with self.subTest(relative=relative):
                m=self.retained();file=self.base/relative;file.write_bytes(file.read_bytes()+b' ')
                with self.assertRaises(ValueError):r.result_file(m,'draft','D')

    def test_retained_answer_digest_is_not_replaceable(self):
        m=self.retained();m['retained'][0]['answer_sha256']='wrong'
        with self.assertRaises(AssertionError):r.result_file(m,'draft','D')

    def test_double_lock_does_not_remove_a_lock_owned_by_someone_else(self):
        existing=self.out/'controller.lock';existing.write_text('other')
        with self.assertRaises(FileExistsError):
            with r.exclusive():self.fail('second lock must refuse')
        self.assertEqual('other',existing.read_text())
        self.assertFalse((self.base/'controller.lock').exists())

    def test_locks_cover_both_runtimes_and_are_released_on_failure(self):
        with self.assertRaisesRegex(ValueError,'test'):
            with r.exclusive():
                self.assertTrue((self.base/'controller.lock').exists())
                self.assertTrue((self.out/'controller.lock').exists());raise ValueError('test')
        self.assertFalse((self.base/'controller.lock').exists())
        self.assertFalse((self.out/'controller.lock').exists())

    def test_active_or_unknown_prior_provider_batch_blocks_recovery(self):
        for status in ('submitting','processing'):
            r.write(self.base/'checkpoint.json',dict(batches=[dict(status=status)]))
            with self.assertRaisesRegex(ValueError,'Uncollected'):r.stopped_base()
        r.write(self.base/'checkpoint.json',dict(batches=[dict(status='collected')]))
        r.stopped_base()

    def test_pilot_refuses_unsettled_recovery_batches_and_explicit_stop(self):
        r.write(self.out/'checkpoint.json',dict(batches=[dict(status='submitting')]))
        with self.assertRaisesRegex(ValueError,'Active/unknown'):r.pilot_idle()
        r.write(self.out/'checkpoint.json',dict(batches=[]));r.write(self.out/'stop.json',{})
        with self.assertRaisesRegex(ValueError,'cancelled'):r.pilot_idle()

    def test_terminal_unit_does_not_hide_an_independent_unit(self):
        m=dict(new_drafts=[dict(job_id='failed'),dict(job_id='pending')])
        for n in range(1,4):r.write(r.path('draft','attempts',f'failed-{n}'),dict(attempt=n,errors=['quality']))
        self.assertEqual(['pending'],r.eligible(m,'draft',[]))
        self.assertIsNone(r.request(m,'draft','failed'))

    def test_old_failure_is_reproducibly_all_or_stop(self):
        for n in range(1,4):r.write(self.base/'draft/attempts'/f'D-{n}.json',dict(attempt=n,errors=['quality']))
        with self.assertRaisesRegex(ValueError,'Attempts exhausted'):r.p.request('D','draft')

    def test_output_budget_is_larger_and_unknown_request_is_not_repeated(self):
        packet=self.packet();m=dict(new_drafts=[dict(job_id='D',parent='parent')])
        with patch.object(r,'packet_for',return_value=packet):
            attempt,params,file=r.request(m,'draft','D')
            self.assertEqual(r.p.MODEL,params['model']);self.assertGreater(params['max_tokens'],4650)
            r.write(file,params)
            with self.assertRaisesRegex(ValueError,'Unknown request'):r.request(m,'draft','D')

    def test_rejected_malformed_json_shapes_do_not_crash_feedback(self):
        values=[[],dict(persons=[None,dict(findings=[None,dict(evidence=[None,dict(verse=[])],identity_evidence=None)])]),dict(verdicts=[None,dict(finding=None)])]
        for value in values:
            with self.subTest(value=value):self.assertEqual(['shape'],r.feedback(self.packet(),dict(answer=value,errors=['shape']))['errors'])

    def test_feedback_supplies_actual_marked_original_text(self):
        packet=self.packet();prior=dict(errors=['original missing'],answer=dict(persons=[dict(findings=[dict(evidence=[dict(verse='NESTLE1904:51:4:14')])])]))
        result=r.feedback(packet,prior)
        self.assertEqual('Λουκᾶς ὁ ἰατρὸς',result['held_original_verses_to_recopy'][0]['body'])

    def test_verifier_partition_is_complete_and_scoped_to_each_draft(self):
        packet=self.packet();answer=self.answer(packet)
        answer['persons'][0]['findings']=[dict(category='trade',role_key='role-'+str(n)) for n in range(9)]
        first=r.check_packets(packet,answer)
        self.assertEqual([4,4,1],[len(r['draft_facts']) for r in first])
        self.assertEqual(r.p.facts_from_draft(answer),[f for unit in first for f in unit['draft_facts']])
        other=copy.deepcopy(packet);other['job_id']='other';other_answer=copy.deepcopy(answer);other_answer['job_id']='other'
        second=r.check_packets(other,other_answer)
        self.assertEqual(first[0]['draft_facts'][0]['id'],second[0]['draft_facts'][0]['id'])
        self.assertNotEqual(first[0]['job_id'],second[0]['job_id'])
        self.assertNotEqual(first[0]['draft_sha256'],second[0]['draft_sha256'])

    def test_verifier_changed_subset_is_rejected(self):
        m=self.retained();packet=self.packet();answer=self.answer();answer['persons'][0]['findings']=[dict(category='trade',role_key='physician')]
        expected=r.check_packets(packet,answer)[0];damaged=copy.deepcopy(expected);damaged['draft_facts']=[]
        r.write(r.path('check','inputs',expected['job_id']),damaged)
        with patch.object(r,'result_file',return_value=dict(answer=answer)):
            with self.assertRaisesRegex(ValueError,'exact draft partition'):r.packet_for(m,'check',expected['job_id'])

    def test_shared_budget_does_not_reset_for_new_version(self):
        with patch.object(r.p,'budget',return_value=dict(draft=47.081946,check=1.40999)):
            receipt=r.p.reserve([('x',dict(max_tokens=1000))],'draft',True)
            self.assertAlmostEqual(48.491936,receipt['spent_usd'])
        with patch.object(r.p,'budget',return_value=dict(draft=69.99,check=30)):
            with self.assertRaisesRegex(ValueError,'budget'):r.p.reserve([('x',dict(max_tokens=32000))],'draft',True)

    def test_stop_before_submit_makes_no_paid_request(self):
        r.write(self.out/'stop.json',{});client=Mock()
        active=dict(phase='draft',status='submitting',entries={'custom':['D',1]});state=dict(batches=[active])
        self.assertFalse(r.submit(client,state,self.out/'checkpoint.json',active,[]))
        client.batch_submit.assert_not_called()
        self.assertTrue(active['not_submitted']);self.assertEqual('collected',active['status'])

    def test_stop_during_submit_cancels_the_newly_known_remote_batch(self):
        client=Mock()
        def send(*args,**kwargs):r.write(self.out/'stop.json',{});return 'remote-batch'
        client.batch_submit.side_effect=send
        client.client().messages.batches.retrieve.return_value=SimpleNamespace(processing_status='in_progress')
        active=dict(phase='draft',status='submitting',entries={'custom':['D',1]});state=dict(batches=[active])
        self.assertFalse(r.submit(client,state,self.out/'checkpoint.json',active,[]))
        client.client().messages.batches.cancel.assert_called_once_with('remote-batch')
        self.assertEqual('remote-batch',r.read(self.out/'checkpoint.json')['batches'][0]['batch_id'])

    def test_lock_acquisition_rechecks_prior_remote_state(self):
        r.write(self.base/'checkpoint.json',dict(batches=[dict(status='collected')]))
        @r.contextlib.contextmanager
        def raced_lock():
            r.write(self.base/'checkpoint.json',dict(batches=[dict(status='submitting')]))
            yield
        with patch.object(r,'exclusive',raced_lock),patch.object(r,'frozen',return_value={}),patch.object(r.p,'api') as api:
            with self.assertRaisesRegex(ValueError,'Uncollected'):r.pilot()
            api.assert_not_called()

    def test_report_distinguishes_pending_people_from_actual_source_gaps(self):
        m=self.retained();m['parents'].append(dict(job_id='pending',units=['pending-r00'],persons=['waiting']))
        m['new_drafts']=[dict(job_id='pending-r00',parent='pending')]
        r.write(self.base/'persons.json',[dict(slug=s) for s in ('luke','waiting','gap')])
        r.write(self.base/'run-manifest.json',dict(zero_packet_persons=['gap']))
        with patch.object(r,'frozen',return_value=m),patch.object(r.p,'budget',return_value=dict(draft=48,check=1)):
            with patch('builtins.print'):status=r.report()
        people={p['person']:p for p in r.read(self.out/'results.json')['persons']}
        self.assertEqual('complete',people['luke']['coverage_status'])
        self.assertEqual('in_progress',people['waiting']['coverage_status'])
        self.assertEqual('no_indexed_passage',people['gap']['coverage_status'])
        self.assertFalse(status['complete']);self.assertEqual(0,status['check_units_complete'])

    def test_recollecting_identical_message_records_usage_only_once(self):
        packet=self.packet();m=dict(retained=[],new_drafts=[dict(job_id='D',parent='parent')])
        r.write(r.path('draft','inputs','D'),packet);answer=self.answer()
        raw=dict(id='msg',model=r.p.MODEL,stop_reason='end_turn',content=[dict(type='text',text=json.dumps(answer))])
        usage=Mock();usage.model_dump.return_value={}
        message=Mock(id='msg',model=r.p.MODEL,stop_reason='end_turn',usage=usage)
        message.model_dump.return_value=raw
        client=Mock();client.LEDGER=self.out/'usage.jsonl';client.text_of.return_value=json.dumps(answer);client.cost.return_value=.1
        def record(label,model,usage,**kwargs):
            with client.LEDGER.open('a') as file:file.write(json.dumps(dict(id=kwargs['request_id'],label=label,usd=.1))+'\n')
        client.record.side_effect=record
        with patch.object(r,'packet_for',return_value=packet),patch('builtins.print'):
            self.assertEqual([],r.store(m,client,message,'draft','D',1,True))
            self.assertEqual([],r.store(m,client,message,'draft','D',1,True))
        self.assertEqual(1,client.record.call_count)


if __name__=='__main__':unittest.main()
