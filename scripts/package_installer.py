"""Build the portable Windows setup bundle without models or local user data."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

DIRECTORIES = ('yumi', 'scripts', 'assets', 'docs', 'tests', 'integrations')
FILES = ('README.md', 'LICENSE', 'requirements.in', 'requirements-windows.lock')
EXCLUDED = {'__pycache__', 'PRODUCT_BRIEF.md', 'bin', 'obj'}


def build(root: Path, output: Path, revision: str = '') -> Path:
    root, output = root.resolve(), output.resolve()
    if revision and not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise ValueError('Revision must be a full Git commit SHA.')
    paths = [root / name for name in FILES]
    for directory in DIRECTORIES:
        paths.extend(p for p in (root / directory).rglob('*')
                     if p.is_file() and not EXCLUDED.intersection(p.relative_to(root).parts)
                     and p.suffix not in ('.pyc', '.pyo'))
    for path in paths:
        if not path.is_file() or any(p.is_symlink() for p in (path, *path.parents)):
            raise ValueError(f'Missing release file or symlink: {path}')
        path.resolve().relative_to(root)
    output.mkdir(parents=True, exist_ok=True)
    archive = output / 'Yumi.zip'
    entries = {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(paths)}
    manifest = {'schema': 1, 'revision': revision, 'format': 'Windows bootstrap installer',
                'files': {name: hashlib.sha256(data).hexdigest() for name, data in entries.items()}}
    entries['release-manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for name, data in sorted(entries.items()):
            info = zipfile.ZipInfo(name, date_time=(2000, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            bundle.writestr(info, data)
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise ValueError('Archive integrity check failed.')
        for required in ('scripts/setup.ps1', 'scripts/Yumi.vbs', 'assets/model-lock.json'):
            if required not in bundle.namelist():
                raise ValueError(f'Missing installer component: {required}')
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    (output / 'Yumi.zip.sha256').write_text(f'{checksum}  Yumi.zip\n', encoding='ascii')
    print(f'Built {archive} ({archive.stat().st_size} bytes); SHA256 {checksum}')
    return archive


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, default=Path('dist'))
    parser.add_argument('--revision', default='')
    args = parser.parse_args()
    build(args.root, args.output, args.revision)
