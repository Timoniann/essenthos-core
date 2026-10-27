"""claude_api.py's key lookup, request layout, cost arithmetic, caps and ledger summary, with no network and no real key."""
import json
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

import claude_api

SYSTEM = 'You read Biblical verses and report who is present where. ' * 60   # ~3.5k bytes
CATALOGUE = {'places': [{'slug': f'place-{i}', 'name': f'Place {i}'} for i in range(300)]}


def usage(input_tokens=0, output_tokens=0, read=0, write_5m=0, write_1h=0, thinking=0):
    return SimpleNamespace(input_tokens=input_tokens, output_tokens=output_tokens, cache_read_input_tokens=read,
                           cache_creation_input_tokens=write_5m + write_1h,
                           cache_creation=SimpleNamespace(ephemeral_5m_input_tokens=write_5m,
                                                          ephemeral_1h_input_tokens=write_1h),
                           output_tokens_details=SimpleNamespace(thinking_tokens=thinking))


class TemporaryLedger:
    def __enter__(self):
        self.folder = tempfile.TemporaryDirectory()
        self.patch = mock.patch.object(claude_api, 'LEDGER', Path(self.folder.name) / 'usage.jsonl')
        self.patch.start()
        return claude_api.LEDGER

    def __exit__(self, *exc):
        self.patch.stop()
        self.folder.cleanup()


class KeyTests(unittest.TestCase):
    def test_the_key_comes_from_the_env_file_when_the_environment_has_none(self):
        with tempfile.TemporaryDirectory() as folder:
            env = Path(folder) / '.env'
            env.write_text('POSTGRES_PASSWORD=x\nANTHROPIC_API_KEY="sk-test"\n', encoding='utf-8')
            with mock.patch.object(claude_api, 'ENV_FILE', env), mock.patch.dict(os.environ, {'ANTHROPIC_API_KEY': ''}):
                self.assertEqual(claude_api.api_key(), 'sk-test')

    def test_an_empty_key_line_is_no_key(self):
        with tempfile.TemporaryDirectory() as folder:
            env = Path(folder) / '.env'
            env.write_text('ANTHROPIC_API_KEY=\n', encoding='utf-8')
            with mock.patch.object(claude_api, 'ENV_FILE', env), mock.patch.dict(os.environ, {'ANTHROPIC_API_KEY': ''}):
                with self.assertRaises(SystemExit) as stopped:
                    claude_api.api_key()
                self.assertNotIn('sk-', str(stopped.exception))

    def test_the_environment_wins(self):
        with mock.patch.dict(os.environ, {'ANTHROPIC_API_KEY': 'sk-env'}):
            self.assertEqual(claude_api.api_key(), 'sk-env')


class CostTests(unittest.TestCase):
    def test_cost_counts_cache_and_halves_a_batch(self):
        u = SimpleNamespace(input_tokens=1_000_000, output_tokens=100_000,
                            cache_creation_input_tokens=0, cache_read_input_tokens=1_000_000)
        self.assertEqual(claude_api.cost('claude-opus-5-5', u), 4.0 + 2.0 + 0.2)
        self.assertEqual(claude_api.cost('claude-opus-5-5', u, batch=True), 3.1)
        self.assertIsNone(claude_api.cost('some-other-model', u))

    def test_a_sonnet_cache_read_costs_five_percent_of_input(self):
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', usage(read=1_000_000)), 0.10)

    def test_writes_are_priced_by_their_ttl(self):
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', usage(write_5m=1_000_000)), 2.50)
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', usage(write_1h=1_000_000)), 4.00)
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', usage(write_1h=1_000_000), batch=True), 2.00)

    def test_a_write_without_its_ttl_split_is_priced_as_five_minutes(self):
        u = SimpleNamespace(input_tokens=0, output_tokens=0, cache_read_input_tokens=0,
                            cache_creation_input_tokens=1_000_000)
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', u), 2.50)

    def test_a_long_haiku_prompt_pays_the_higher_price(self):
        self.assertEqual(claude_api.cost('claude-haiku-5-5', usage(input_tokens=100_000)), 0.01)
        self.assertEqual(claude_api.cost('claude-haiku-5-5', usage(input_tokens=50_000, read=60_000)),
                         round((50_000 * 0.50 + 60_000 * 0.05) / 1e6, 6))

    def test_usage_may_be_a_dict(self):
        self.assertEqual(claude_api.cost('claude-sonnet-5-5', {'input_tokens': 1_000_000, 'output_tokens': 0}), 2.0)


class LayoutTests(unittest.TestCase):
    def markers(self, params):
        return [(where, b['cache_control']) for where, b in
                [('tool', t) for t in params.get('tools', [])]
                + [('system', s) for s in params.get('system', [])]
                + [('message', c) for c in params['messages'][0]['content']]
                if b.get('cache_control')]

    def test_stable_layers_come_first_and_each_ends_in_a_breakpoint(self):
        p = claude_api.build_params('the unique packet', system=SYSTEM, shared=CATALOGUE,
                                    model='claude-sonnet-5-5', ttl='1h')
        content = p['messages'][0]['content']
        self.assertEqual(content[-1], {'type': 'text', 'text': 'the unique packet'})
        self.assertEqual(json.loads(content[0]['text']), CATALOGUE)
        self.assertEqual(self.markers(p), [('system', {'type': 'ephemeral', 'ttl': '1h'}),
                                           ('message', {'type': 'ephemeral', 'ttl': '1h'})])

    def test_the_unique_part_never_carries_a_breakpoint(self):
        p = claude_api.build_params('x' * 50_000, system=SYSTEM, model='claude-sonnet-5-5')
        self.assertNotIn('cache_control', p['messages'][0]['content'][-1])
        self.assertEqual(p['system'][-1]['cache_control'], {'type': 'ephemeral'})

    def test_a_prefix_below_the_minimum_gets_no_breakpoint(self):
        p = claude_api.build_params('question', system='Be brief.', model='claude-sonnet-5-5')
        self.assertEqual(self.markers(p), [])

    def test_a_short_system_is_cached_together_with_the_shared_context(self):
        p = claude_api.build_params('question', system='Be brief.', shared=CATALOGUE, model='claude-sonnet-5-5')
        self.assertEqual([w for w, _ in self.markers(p)], ['message'])

    def test_shared_dicts_serialize_the_same_whatever_their_key_order(self):
        a = claude_api.build_params('q', shared={'b': 1, 'a': [2, 3]})
        b = claude_api.build_params('q', shared={'a': [2, 3], 'b': 1})
        self.assertEqual(a, b)

    def test_tools_are_sorted_and_cached_with_the_system_prompt(self):
        tools = [{'name': 'zeta', 'description': 'z' * 2000, 'input_schema': {'type': 'object'}},
                 {'name': 'alpha', 'description': 'a' * 2000, 'input_schema': {'type': 'object'}}]
        p = claude_api.build_params('q', tools=tools, system=SYSTEM)
        self.assertEqual([t['name'] for t in p['tools']], ['alpha', 'zeta'])
        self.assertEqual([w for w, _ in self.markers(p)], ['system'])
        no_system = claude_api.build_params('q', tools=tools)
        self.assertEqual([w for w, _ in self.markers(no_system)], ['tool'])
        self.assertEqual(no_system['tools'][-1]['name'], 'zeta')
        self.assertNotIn('cache_control', tools[0])

    def test_schema_and_effort_go_to_output_config(self):
        schema = {'type': 'object', 'properties': {}, 'additionalProperties': False}
        p = claude_api.build_params('q', schema=schema, effort='high', max_tokens=9000)
        self.assertEqual(p['output_config'], {'effort': 'high', 'format': {'type': 'json_schema', 'schema': schema}})
        self.assertEqual(p['max_tokens'], 9000)

    def test_a_longer_ttl_after_a_shorter_one_is_refused(self):
        with self.assertRaises(ValueError):
            claude_api.build_params('q', system=SYSTEM, shared=CATALOGUE, ttl='5m', shared_ttl='1h')
        p = claude_api.build_params('q', system=SYSTEM, shared=CATALOGUE, ttl='1h', shared_ttl='5m')
        self.assertEqual([m.get('ttl', '5m') for _, m in self.markers(p)], ['1h', '5m'])
        with self.assertRaises(ValueError):
            claude_api.build_params('q', system=SYSTEM, ttl='2h')

    def test_the_layout_check_refuses_what_the_api_refuses(self):
        five = {'type': 'ephemeral'}
        many = {'model': 'm', 'messages': [{'role': 'user', 'content': [
            {'type': 'text', 'text': str(i), 'cache_control': five} for i in range(5)]}]}
        with self.assertRaises(ValueError):
            claude_api.check_cache_layout(many)
        wrong_order = {'model': 'm', 'system': [{'type': 'text', 'text': 's', 'cache_control': five}],
                       'messages': [{'role': 'user', 'content': [
                           {'type': 'text', 'text': 'c', 'cache_control': {'type': 'ephemeral', 'ttl': '1h'}}]}]}
        with self.assertRaises(ValueError):
            claude_api.check_cache_layout(wrong_order)

    def test_requests_sharing_a_prefix_share_a_key(self):
        a = claude_api.build_params('first', system=SYSTEM, shared=CATALOGUE)
        b = claude_api.build_params('second', system=SYSTEM, shared=CATALOGUE)
        c = claude_api.build_params('first', system=SYSTEM + '!', shared=CATALOGUE)
        self.assertEqual(claude_api.cached_prefix_key(a), claude_api.cached_prefix_key(b))
        self.assertNotEqual(claude_api.cached_prefix_key(a), claude_api.cached_prefix_key(c))
        self.assertIsNone(claude_api.cached_prefix_key(claude_api.build_params('q')))


class SpendingTests(unittest.TestCase):
    def test_the_estimate_is_an_upper_bound_and_a_batch_halves_it(self):
        p = claude_api.build_params('q' * 3000, model='claude-sonnet-5-5', max_tokens=10_000)
        live = claude_api.estimate_usd(p)
        self.assertGreaterEqual(live, (1000 * 2.0 + 10_000 * 10.0) / 1e6)
        self.assertAlmostEqual(claude_api.estimate_usd(p, batch=True), live / 2)
        with self.assertRaises(ValueError):
            claude_api.estimate_usd({**p, 'model': 'unknown-model'})

    def test_the_guard_stops_at_the_cap(self):
        with TemporaryLedger():
            claude_api.record('run', 'claude-sonnet-5-5', usage(input_tokens=1_000_000))
            claude_api.guard('run', 7.9, cap_usd=10)
            with self.assertRaises(claude_api.BudgetExceeded):
                claude_api.guard('run', 8.1, cap_usd=10)
            claude_api.guard('run', 1_000, cap_usd=None)

    def test_a_batch_over_its_cap_is_never_sent(self):
        requests = [(f'id-{i}', claude_api.build_params('q', model='claude-sonnet-5-5', max_tokens=16_000))
                    for i in range(100)]
        fake = mock.MagicMock()
        with TemporaryLedger(), mock.patch.object(claude_api, 'client', return_value=fake):
            with self.assertRaises(claude_api.BudgetExceeded):
                claude_api.batch_submit(requests, label='run', cap_usd=1.0)
        fake.messages.batches.create.assert_not_called()

    def test_a_batch_is_sent_with_its_layout_and_logged_with_its_reserve(self):
        requests = [(f'id-{i}', claude_api.build_params(f'q{i}', system=SYSTEM, shared=CATALOGUE,
                                                        model='claude-sonnet-5-5', ttl='1h')) for i in range(3)]
        fake = mock.MagicMock()
        fake.messages.batches.create.return_value = SimpleNamespace(id='msgbatch_test')
        with TemporaryLedger() as ledger, mock.patch.object(claude_api, 'client', return_value=fake):
            self.assertEqual(claude_api.batch_submit(requests, label='run', cap_usd=50), 'msgbatch_test')
            log = json.loads((ledger.parent / 'batches.jsonl').read_text(encoding='utf-8'))
        sent = fake.messages.batches.create.call_args.kwargs['requests']
        self.assertEqual(len(sent), 3)
        self.assertEqual(sent[0]['params']['system'][-1]['cache_control'], {'type': 'ephemeral', 'ttl': '1h'})
        self.assertEqual(log['cached_prefixes'], 1)
        self.assertGreater(log['reserve_usd'], 0)
        plan = claude_api.batch_plan(requests)
        self.assertEqual(list(plan['cached_prefixes'].values()), [3])

    def test_ask_sends_the_cached_layout_and_records_the_cache_meters(self):
        message = SimpleNamespace(model='claude-sonnet-5-5', id='msg_1', stop_reason='end_turn',
                                  usage=usage(input_tokens=100, output_tokens=20, read=1200, thinking=5),
                                  content=[SimpleNamespace(type='text', text='Genesis')])
        stream = mock.MagicMock()
        stream.__enter__.return_value.get_final_message.return_value = message
        fake = mock.MagicMock()
        fake.beta.messages.stream.return_value = stream
        with TemporaryLedger(), mock.patch.object(claude_api, 'client', return_value=fake):
            self.assertEqual(claude_api.ask('Which book?', system=SYSTEM, model='claude-sonnet-5-5', label='t'), 'Genesis')
            row = claude_api.ledger_rows('t')[0]
        sent = fake.beta.messages.stream.call_args.kwargs
        self.assertEqual(sent['system'][-1]['cache_control'], {'type': 'ephemeral'})
        self.assertEqual(sent['extra_body'], {'fallbacks': 'default'})
        self.assertEqual((row['cache_read'], row['thinking'], row['stop_reason']), (1200, 5, 'end_turn'))

    def test_ask_refuses_past_its_cap_without_calling(self):
        fake = mock.MagicMock()
        with TemporaryLedger(), mock.patch.object(claude_api, 'client', return_value=fake):
            with self.assertRaises(claude_api.BudgetExceeded):
                claude_api.ask('q', label='t', cap_usd=0.0001)
        fake.beta.messages.stream.assert_not_called()

    def test_batch_results_record_the_batch_and_the_stop_reason(self):
        message = SimpleNamespace(model='claude-sonnet-5-5', id='msg_2', stop_reason='max_tokens',
                                  usage=usage(input_tokens=10, output_tokens=5, write_1h=3000),
                                  content=[SimpleNamespace(type='text', text='cut')])
        results = [SimpleNamespace(custom_id='a', result=SimpleNamespace(type='succeeded', message=message)),
                   SimpleNamespace(custom_id='b', result=SimpleNamespace(type='expired'))]
        fake = mock.MagicMock()
        fake.messages.batches.results.return_value = results
        with TemporaryLedger(), mock.patch.object(claude_api, 'client', return_value=fake):
            answers, failures = claude_api.batch_results('msgbatch_x', label='t')
            row = claude_api.ledger_rows('t')[0]
            s = claude_api.summary('t')
        self.assertEqual((answers, failures), ({'a': 'cut'}, {'b': 'expired'}))
        self.assertEqual((row['id'], row['message_id'], row['batch_id'], row['cache_write_1h']),
                         ('a', 'msg_2', 'msgbatch_x', 3000))
        self.assertEqual(s['truncated'], 1)


class SummaryTests(unittest.TestCase):
    def test_the_summary_reports_hit_rate_and_what_caching_and_batching_saved(self):
        with TemporaryLedger():
            claude_api.record('run', 'claude-sonnet-5-5', usage(input_tokens=1_000_000, write_5m=1_000_000), batch=True)
            claude_api.record('run', 'claude-sonnet-5-5', usage(input_tokens=1_000_000, read=1_000_000,
                                                                 output_tokens=100_000), batch=True)
            s = claude_api.summary('run')
        self.assertEqual(s['requests'], 2)
        self.assertEqual(s['cache_hit_rate'], 0.25)
        self.assertAlmostEqual(s['usd'], (2.0 + 2.5 + 2.0 + 0.1 + 1.0) / 2)
        self.assertAlmostEqual(s['usd_without_cache'], (4.0 + 4.0 + 1.0) / 2)
        self.assertAlmostEqual(s['saved_by_cache'], (9.0 - 7.6) / 2)
        self.assertAlmostEqual(s['saved_by_batch'], 7.6 / 2)
        self.assertIn('25.0%', claude_api.format_summary(s, 'run'))

    def test_old_ledger_lines_are_priced_again_at_current_rates(self):
        old = {'at': 'x', 'label': 'old', 'model': 'claude-sonnet-5-5', 'batch': False, 'id': 'm', 'input': 0,
               'output': 0, 'cache_read': 1_000_000, 'cache_write': 0, 'usd': 0.2}
        s = claude_api.summary(rows=[old])
        self.assertEqual((s['usd_recorded'], s['usd']), (0.2, 0.1))


if __name__ == '__main__':
    unittest.main()
