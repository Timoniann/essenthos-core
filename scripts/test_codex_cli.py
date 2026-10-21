"""Where codex_cli.py finds the Codex CLI after the desktop app has moved it."""
import os
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

import codex_cli


def install(root, name, files=codex_cli.REQUIRED):
    folder = root / 'OpenAI' / 'Codex' / 'bin' / name
    folder.mkdir(parents=True)
    for file in files:
        (folder / file).write_bytes(b'')
    return folder


class CodexCliTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        patcher = mock.patch.dict(os.environ, {'LOCALAPPDATA': str(self.root)})
        patcher.start()
        self.addCleanup(patcher.stop)
        os.environ.pop('ESSENTHOS_CODEX', None)
        self.addCleanup(self.temp.cleanup)

    def test_the_newest_complete_folder_wins(self):
        old = install(self.root, 'aaaa')
        time.sleep(0.02)
        new = install(self.root, 'bbbb')
        os.utime(old, (time.time() - 60, time.time() - 60))
        self.assertEqual(codex_cli.executable(), str(new / 'codex.exe'))

    def test_a_half_written_folder_is_passed_over(self):
        whole = install(self.root, 'aaaa')
        os.utime(whole, (time.time() - 60, time.time() - 60))
        install(self.root, 'bbbb', files=('codex.exe',))
        self.assertEqual(codex_cli.executable(), str(whole / 'codex.exe'))

    def test_a_stale_override_is_ignored_and_a_live_one_obeyed(self):
        current = install(self.root, 'cccc')
        os.environ['ESSENTHOS_CODEX'] = str(self.root / 'gone' / 'codex.exe')
        self.assertEqual(codex_cli.executable(), str(current / 'codex.exe'))
        pinned = self.root / 'pinned.exe'
        pinned.write_bytes(b'')
        os.environ['ESSENTHOS_CODEX'] = str(pinned)
        self.assertEqual(codex_cli.executable(), str(pinned))

    def test_nothing_installed_says_so(self):
        with mock.patch('shutil.which', return_value=None):
            with self.assertRaises(RuntimeError):
                codex_cli.executable()


if __name__ == '__main__':
    unittest.main()
