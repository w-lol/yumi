"""Run the existing tests and expose failures as GitHub check annotations."""
from pathlib import Path
import os
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


def escape(value):
    return value.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')


if __name__ == '__main__':
    suite = unittest.defaultTestLoader.discover(str(ROOT / 'tests'))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if os.environ.get('GITHUB_ACTIONS') == 'true':
        for test, traceback in result.failures + result.errors:
            print(f'::error::{escape(str(test) + chr(10) + traceback)}', flush=True)
    sys.exit(0 if result.wasSuccessful() else 1)
