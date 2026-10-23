import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location(
    'package_installer', Path(__file__).resolve().parents[1] / 'scripts/package_installer.py')
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)


class PackagingTests(unittest.TestCase):
    def test_bundle_excludes_local_data_and_checksums_exact_contents(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name in (*packaging.FILES, 'scripts/setup.ps1', 'scripts/Yumi.vbs',
                         'assets/model-lock.json', 'yumi/__init__.py',
                         'data/private.sqlite3', 'config/settings.json',
                         'docs/PRODUCT_BRIEF.md', 'scripts/__pycache__/private.pyc'):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('fixture', encoding='utf-8')
            archive = packaging.build(root, root / 'dist', 'a' * 40)
            first_bytes = archive.read_bytes()
            with zipfile.ZipFile(archive) as bundle:
                names = bundle.namelist()
                self.assertNotIn('data/private.sqlite3', names)
                self.assertNotIn('config/settings.json', names)
                self.assertNotIn('docs/PRODUCT_BRIEF.md', names)
                self.assertFalse(any('__pycache__' in name for name in names))
                manifest = json.loads(bundle.read('release-manifest.json'))
                self.assertEqual(manifest['revision'], 'a' * 40)
                for name, digest in manifest['files'].items():
                    self.assertEqual(hashlib.sha256(bundle.read(name)).hexdigest(), digest)
            packaging.build(root, root / 'dist', 'a' * 40)
            self.assertEqual(first_bytes, archive.read_bytes())
            self.assertEqual((root / 'dist/Yumi.zip.sha256').read_text().split()[0],
                             hashlib.sha256(first_bytes).hexdigest())
