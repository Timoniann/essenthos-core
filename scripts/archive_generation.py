"""Move finished generation runs from Resources/Essenthos/generation to the archive on E: (TSK-1452).

    python scripts/archive_generation.py <run> [<run> ...]            copy and verify; nothing is deleted
    python scripts/archive_generation.py --remove <run> [<run> ...]   the same, then delete each verified source copy

Each run folder is copied to E:\\EssenthosArchive\\generation\\<run>, every file is then read back and compared with its
source by size and SHA-256, and a manifest (path, bytes, sha256) is written beside the archive as
E:\\EssenthosArchive\\generation-<run>.manifest.tsv. A source copy is deleted only with --remove and only when every one
of its files verified and the archive holds nothing the source does not. A run already in the archive is refused, so an
archived run is never overwritten. ESSENTHOS_ARCHIVE overrides E:\\EssenthosArchive. Run it from the main checkout,
where the run folders are; run folders are not in git."""
import argparse
import hashlib
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Resources" / "Essenthos" / "generation"
ARCHIVE = Path(os.environ.get("ESSENTHOS_ARCHIVE") or r"E:\EssenthosArchive")


def extended(path: Path) -> str:
    """The path in the form Windows accepts beyond 260 characters."""
    text = str(path.resolve())
    return "\\\\?\\" + text if os.name == "nt" and not text.startswith("\\\\?\\") else text


def files(root: Path) -> list[str]:
    found = []
    for folder, folders, names in os.walk(extended(root)):
        folders.sort()
        for name in sorted(names):
            found.append(os.path.relpath(os.path.join(folder, name), extended(root)).replace(os.sep, "/"))
    return found


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with open(extended(path), "rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def archive(run: str, remove: bool) -> bool:
    source, target = SOURCE / run, ARCHIVE / "generation" / run
    manifest = ARCHIVE / f"generation-{run}.manifest.tsv"
    if not source.is_dir():
        print(f"{run}: no such run folder under {SOURCE}")
        return False
    if target.exists() or manifest.exists():
        print(f"{run}: already in the archive ({target}); an archived run is never overwritten")
        return False

    names = files(source)
    for name in names:
        os.makedirs(extended((target / name).parent), exist_ok=True)
        shutil.copy2(extended(source / name), extended(target / name))

    rows, bad, total = [], [], 0
    for name in names:
        size = os.path.getsize(extended(source / name))
        digest = sha256(source / name)
        if os.path.getsize(extended(target / name)) != size or sha256(target / name) != digest:
            bad.append(name)
            continue
        rows.append(f"{name}\t{size}\t{digest}")
        total += size
    extra = sorted(set(files(target)) - set(names))

    with open(manifest, "w", encoding="utf-8", newline="\n") as out:
        out.write(f"# Resources/Essenthos/generation/{run}, archived {datetime.now(timezone.utc):%Y-%m-%dT%H:%M:%SZ}\n")
        out.write(f"# files {len(rows)}  bytes {total}  differing {len(bad)}  extra {len(extra)}\n")
        out.write("path\tbytes\tsha256\n")
        out.writelines(row + "\n" for row in rows)

    if bad or extra:
        print(f"{run}: {len(bad)} files differ, {len(extra)} extra in the archive; the source is kept")
        for name in (bad + extra)[:20]:
            print("  " + name)
        return False

    print(f"{run}: {len(rows)} files, {total} bytes verified -> {target}")
    if remove:
        shutil.rmtree(extended(source))
        print(f"{run}: source copy removed")
    return True


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("runs", nargs="+", help="run folder names under Resources/Essenthos/generation")
    parser.add_argument("--remove", action="store_true", help="delete each source copy once it verified")
    arguments = parser.parse_args()
    results = [archive(run, arguments.remove) for run in arguments.runs]
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
