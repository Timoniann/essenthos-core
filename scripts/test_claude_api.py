"""claude_api.py's key lookup and cost arithmetic, with no network and no real key."""
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

import claude_api


class ClaudeApiTests(unittest.TestCase):
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

    def test_cost_counts_cache_and_halves_a_batch(self):
        usage = SimpleNamespace(input_tokens=1_000_000, output_tokens=100_000,
                                cache_creation_input_tokens=0, cache_read_input_tokens=1_000_000)
        self.assertEqual(claude_api.cost('claude-opus-5-5', usage), 4.0 + 2.0 + 0.2)
        self.assertEqual(claude_api.cost('claude-opus-5-5', usage, batch=True), 3.1)
        self.assertIsNone(claude_api.cost('some-other-model', usage))


if __name__ == '__main__':
    unittest.main()
