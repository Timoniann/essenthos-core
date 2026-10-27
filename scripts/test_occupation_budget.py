"""Offline cumulative authorization and provider-preserving handoff checks."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock,patch
import occupation_budget as b


class BudgetTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        root=Path(self.temp.name);self.run=root/'run';self.profile=root/'profile';self.base=root/'base'
        for path in (self.run,self.profile,self.base):path.mkdir()
        for module,name,value in ((b,'RUN',self.run),(b,'PROFILE',self.profile),(b.r,'OUT',self.run),
            (b.r,'BASE',self.base),(b.r.p,'CAP',100),(b.r.p,'DRAFT_CAP',70),(b.r.p,'cli',Mock())):
            context=patch.object(module,name,value);context.start();self.addCleanup(context.stop)
        b.r.write(self.base/'checkpoint.json',dict(batches=[]))
        self.auth=dict(total_cap_usd=125,draft_subcap_usd=80,owner_approved=True,label=b.r.p.LABEL,model=b.r.p.MODEL)
        b.r.write(self.profile/'authorization.json',self.auth)
        b.r.write(self.run/'checkpoint.json',dict(batches=[dict(status='processing',batch_id='known-provider-batch')]))
        b.r.write(self.run/'status.json',dict(stop_reason='operator_stop'))

    def marker(self,source=b.SOURCE):
        marker=dict(source=source,authorization_sha256=b.r.p.sha(self.profile/'authorization.json'))
        b.r.write(self.run/'stop.json',marker)
        b.r.write(self.profile/'handoff-complete.json',dict(authorization_sha256=marker['authorization_sha256'],
            stop_sha256=b.r.p.sha(self.run/'stop.json')))

    def test_only_the_explicit_cumulative_grant_is_allowed(self):
        b.policy(self.auth)
        for total,draft in ((126,80),(250,80),(125,90),(100,70)):
            value=dict(self.auth,total_cap_usd=total,draft_subcap_usd=draft)
            with self.subTest(total=total,draft=draft):
                with self.assertRaisesRegex(ValueError,'authorized'):b.policy(value)
        with self.assertRaises(ValueError):b.policy(dict(self.auth,owner_approved=False))

    def test_budget_stays_bound_to_original_model_and_labels(self):
        for field,value in (('model','other'),('label','new-spend-reset')):
            with self.assertRaises(ValueError):b.policy(dict(self.auth,**{field:value}))

    def test_raise_is_total_not_an_additional_allowance(self):
        b.apply_caps(self.auth)
        with patch.object(b.r.p,'budget',return_value=dict(draft=70,check=39)):
            receipt=b.r.p.reserve([('x',dict(max_tokens=1000))],'check',True)
            self.assertEqual(109,receipt['spent_usd']);self.assertEqual(125,receipt['cap_usd'])
        with patch.object(b.r.p,'budget',return_value=dict(draft=80,check=45)):
            with self.assertRaisesRegex(ValueError,'budget'):b.r.p.reserve([('x',dict(max_tokens=1000))],'check',True)
        with patch.object(b.r.p,'budget',return_value=dict(draft=80,check=1)):
            with self.assertRaisesRegex(ValueError,'budget'):b.r.p.reserve([('x',dict(max_tokens=1000))],'draft',True)

    def test_claim_note_reflects_effective_authorized_cap(self):
        cli=b.r.p.cli;b.apply_caps(self.auth)
        b.r.p.cli('claim','TSK-1250','--note','Occupation v3 recovery, unchanged100USD total cap')
        self.assertIn('125USD',cli.call_args.args[-1])

    def test_activation_preserves_provider_checkpoint_and_paid_requests(self):
        self.marker();checkpoint=b.r.p.sha(self.run/'checkpoint.json')
        raw=self.run/'paid-request.json';raw.write_text('already submitted');digest=b.r.p.sha(raw)
        with patch.object(b.r.p,'budget',return_value=dict(draft=56,check=13)),patch.object(b.r.p,'api',side_effect=AssertionError('No API during activation')):
            b.activate(self.auth)
        self.assertEqual(checkpoint,b.r.p.sha(self.run/'checkpoint.json'))
        self.assertEqual(digest,b.r.p.sha(raw))
        self.assertFalse((self.run/'stop.json').exists());self.assertTrue((self.profile/'applied-handoff-stop.json').exists())
        self.assertEqual(69,b.r.read(self.profile/'budget-applied.json')['existing_spent_usd'])

    def test_activation_never_erases_an_operator_cancellation(self):
        self.marker('operator cancellation')
        with self.assertRaisesRegex(ValueError,'Operator stop'):b.activate(self.auth)
        self.assertTrue((self.run/'stop.json').exists())

    def test_activation_refuses_a_changed_handoff_grant(self):
        self.marker();authfile=self.profile/'authorization.json';authfile.write_bytes(authfile.read_bytes()+b' ')
        with self.assertRaisesRegex(ValueError,'grant changed'):b.activate(self.auth)

    def test_handoff_stops_local_worker_without_cancelling_remote_batch(self):
        before=b.r.p.sha(self.run/'checkpoint.json')
        with (patch.object(b,'frozen',return_value=self.auth),patch.object(b,'service_state',side_effect=['running','stopped']),
             patch.object(b,'submission_acknowledged',return_value=True),
             patch.object(b.time,'sleep'),patch.object(b.r.p,'api',side_effect=AssertionError('No provider call')),patch('builtins.print')):
            b.handoff()
        receipt=b.r.read(self.profile/'handoff-complete.json')
        self.assertEqual(['known-provider-batch'],receipt['outstanding'])
        self.assertEqual(before,b.r.p.sha(self.run/'checkpoint.json'))
        self.assertFalse(b.r.read(self.run/'stop.json')['cancel_provider_batches'])

    def test_handoff_does_not_interrupt_an_unknown_http_submission(self):
        b.r.write(self.run/'checkpoint.json',dict(batches=[dict(status='submitting')]))
        with (patch.object(b,'frozen',return_value=self.auth),patch.object(b,'service_state',return_value='running'),
             patch.object(b.time,'monotonic',side_effect=[0,151])):
            with self.assertRaisesRegex(ValueError,'not complete'):b.handoff()
        self.assertFalse((self.run/'stop.json').exists())

    def test_frozen_budget_profile_binds_unchanged_reader_and_client(self):
        main=Path(self.temp.name)/'main';client=main/'scripts/claude_api.py';client.parent.mkdir(parents=True);client.write_text('client')
        reader=self.run/'code/resume_person_occupations.py';reader.parent.mkdir();reader.write_text('frozen reader')
        manifest=self.run/'manifest.json';manifest.write_text('{}')
        auth=dict(self.auth,script_sha256=b.r.p.sha(Path(b.__file__)),reader_sha256=b.r.p.sha(reader),
            reader_manifest_sha256=b.r.p.sha(manifest),client_sha256=b.r.p.sha(client))
        b.r.write(self.profile/'authorization.json',auth)
        with patch.object(b,'MAIN',main),patch.object(b.r,'frozen'):
            self.assertEqual(auth,b.frozen())
            reader.write_text('changed reader')
            with self.assertRaisesRegex(ValueError,'Changed budget'):b.frozen()

    def test_operator_stop_replaced_at_rename_is_restored_and_activation_aborts(self):
        self.marker();original=Path.replace
        def raced_replace(file,target):
            if file==self.run/'stop.json':b.r.write(file,dict(source='operator cancellation'))
            return original(file,target)
        with patch.object(Path,'replace',raced_replace):
            with self.assertRaisesRegex(ValueError,'raced with activation'):b.activate(self.auth)
        self.assertEqual('operator cancellation',b.r.read(self.run/'stop.json')['source'])
        self.assertFalse((self.profile/'budget-applied.json').exists())

    def test_real_submit_cannot_receive_handoff_stop_before_post_submit_check(self):
        client=Mock();client.batch_submit.return_value='new-batch';state=dict(batches=[])
        active=dict(phase='check',status='submitting',entries={'custom':['job',1]});state['batches'].append(active)
        original=b.r.write
        def between_checkpoint_and_post_submit(file,value):
            original(file,value)
            if file==self.run/'checkpoint.json' and active.get('batch_id'):
                self.assertFalse(b.mark_handoff('running','grant'))
        with patch.object(b.r,'write',between_checkpoint_and_post_submit),patch.object(b,'submission_acknowledged',return_value=False):
            self.assertTrue(b.r.submit(client,state,self.run/'checkpoint.json',active,[]))
        client.client().messages.batches.cancel.assert_not_called()
        self.assertFalse((self.run/'stop.json').exists())
        with patch.object(b,'submission_acknowledged',return_value=True):self.assertTrue(b.mark_handoff('running','grant'))


if __name__=='__main__':unittest.main()
