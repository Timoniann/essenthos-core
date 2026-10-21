"""
Where the Codex CLI is right now.

The Codex desktop app installs its CLI under %LOCALAPPDATA%\\OpenAI\\Codex\\bin\\<hash>\\ and moves it
to a new <hash> folder on every update, deleting the old one. A path read once at the start of a run
is therefore wrong after the next update, and a run of hours outlives updates: every call after one
fails with a missing executable (2026-10-21, after the owner updated Codex). So every script asks
here, and asks again for each call, instead of keeping a path.

Order: ESSENTHOS_CODEX if it names a file that exists (a deliberate override; a stale one is
ignored rather than obeyed), then the newest complete install folder, then `codex` on PATH. A folder
is complete when it holds codex.exe and codex-windows-sandbox-setup.exe; a half-written one starts
and then fails on its first sandboxed command (the same rule as avioniq's codex-shim.mjs).

Run as a script, it prints the path, for PowerShell launchers.
"""
import os
import shutil
import sys
from pathlib import Path

REQUIRED = ('codex.exe', 'codex-windows-sandbox-setup.exe')


def install_root():
    local = os.environ.get('LOCALAPPDATA') or str(Path.home() / 'AppData' / 'Local')
    return Path(local) / 'OpenAI' / 'Codex' / 'bin'


def installed():
    """The newest complete install folder's codex.exe, or None."""
    root = install_root()
    try:
        folders = [folder for folder in root.iterdir()
                   if folder.is_dir() and all((folder / name).is_file() for name in REQUIRED)]
    except OSError:
        return None
    if not folders:
        return None
    return str(max(folders, key=lambda folder: folder.stat().st_mtime) / 'codex.exe')


def executable():
    """The Codex CLI to call now. Call it per call, not once per run."""
    chosen = os.environ.get('ESSENTHOS_CODEX')
    if chosen and Path(chosen).is_file():
        return chosen
    found = installed() or shutil.which('codex')
    if not found:
        raise RuntimeError(
            'Codex CLI not found: no complete folder under ' + str(install_root())
            + ' and no codex on PATH. If Codex is updating, try again in a minute; '
              'otherwise set ESSENTHOS_CODEX to its executable.')
    return found


if __name__ == '__main__':
    try:
        print(executable())
    except RuntimeError as error:
        print(error, file=sys.stderr)
        sys.exit(1)
