"""
A fingerprint of every corpus folder, so a copy of the data can be checked against the one the
measurements in this repository were taken from.

The corpus is a gigabyte of third-party Bibles, lexicons and alignments, and it is not this
repository's to carry — the data stays out of git and the fetch scripts under scripts/ put it back.
But only four of the seventeen folders have a fetch script, so most of the corpus exists because
somebody downloaded it once, and nothing until now said what "it" was. Two machines could disagree
about the bytes under one folder name and every number either of them measured would be unfalsifiable.

Writes Resources/MANIFEST.json, which IS committed: file count, byte count, and a SHA-256 over the
sorted list of every file's path, size and hash. It does not make the corpus reproducible — a fetch
script does that — but it makes a copy checkable, which is the cheaper half and the one that was
missing.

  python scripts/corpus-manifest.py           write the manifest
  python scripts/corpus-manifest.py --check    compare the corpus against it, exit 1 on a difference
"""

import hashlib, os, json, subprocess, sys, time
sys.stdout.reconfigure(encoding='utf-8')
root = 'Resources'
started = time.time()

# Files git already carries are excluded. They are versioned, so the manifest adds nothing about
# them -- and they are text, so git rewrites their line endings on checkout: a LICENCE.md alone made
# a folder's fingerprint differ between two worktrees of the same commit by 799 bytes. The manifest
# is for the data git does not carry, which is all of it that matters.
tracked = set()
try:
    listed = subprocess.run(['git', 'ls-files', root], capture_output=True, text=True, check=True)
    tracked = {os.path.normpath(line) for line in listed.stdout.splitlines() if line}
except Exception:
    pass

folders = {}
for name in sorted(os.listdir(root)):
    d = os.path.join(root, name)
    if not os.path.isdir(d):
        continue
    files = []
    for dirpath, _, filenames in os.walk(d):
        for f in sorted(filenames):
            p = os.path.join(dirpath, f)
            if os.path.normpath(p) in tracked:
                continue
            rel = os.path.relpath(p, d).replace(os.sep, '/')
            h = hashlib.sha256()
            with open(p, 'rb') as fh:
                for chunk in iter(lambda: fh.read(1 << 20), b''):
                    h.update(chunk)
            files.append((rel, os.path.getsize(p), h.hexdigest()))
    digest = hashlib.sha256()
    for rel, size, h in sorted(files):
        digest.update((rel + ' ' + str(size) + ' ' + h + '\n').encode())
    if not files:
        # A folder git carries entirely -- WorldHistory is 319 KB of committed CC0 exports -- has
        # nothing here to fingerprint, and listing it as zero files reads like the data is missing.
        # It is versioned instead, which is the stronger guarantee.
        print(f"{name:22} {'':>5} tracked in full, nothing to fingerprint")
        continue

    total = sum(s for _, s, _ in files)
    folders[name] = {"files": len(files), "bytes": total, "sha256": digest.hexdigest()}
    print(f"{name:22} {len(files):>5} files {total/1e6:>9.1f} MB  {digest.hexdigest()[:16]}")
print(f"-- {time.time()-started:.0f}s")
path = os.path.join(root, 'MANIFEST.json')

if '--check' in sys.argv:
    if not os.path.exists(path):
        print('No manifest to check against. Run without --check to write one.')
        raise SystemExit(1)
    recorded = json.load(open(path, encoding='utf-8'))
    differences = []
    for name in sorted(set(recorded) | set(folders)):
        was, now = recorded.get(name), folders.get(name)
        if was is None:
            differences.append(f'{name}: present here, not in the manifest')
        elif now is None:
            differences.append(f'{name}: in the manifest, missing here')
        elif was['sha256'] != now['sha256']:
            differences.append(
                f"{name}: {was['files']} files/{was['bytes']} bytes recorded, "
                f"{now['files']}/{now['bytes']} here")
    if differences:
        print('The corpus differs from the manifest:')
        for d in differences:
            print('  ' + d)
        raise SystemExit(1)
    print(f'{len(folders)} folders match the manifest.')
    raise SystemExit(0)

json.dump(folders, open(path, 'w', encoding='utf-8'), indent=2, sort_keys=True)
print(f'Wrote {path}.')
