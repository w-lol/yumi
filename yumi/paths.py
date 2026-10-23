from __future__ import annotations
import json
import os
from pathlib import Path


def atomic_json(path: Path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.new')
    with temp.open('w', encoding='utf-8') as handle:
        json.dump(value, handle, ensure_ascii=False, indent=2)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(temp, path)


class Paths:
    def __init__(self, root):
        self.root = Path(root).resolve()
        for name in ('data', 'logs', 'cache', 'tmp', 'models', 'config', 'backups'):
            setattr(self, name, self.root / name)
            getattr(self, name).mkdir(parents=True, exist_ok=True)

    def activate(self):
        for key in ('TMP', 'TEMP', 'TMPDIR'):
            os.environ[key] = str(self.tmp)
        for key in ('HF_HOME', 'HUGGINGFACE_HUB_CACHE', 'XDG_CACHE_HOME',
                    'TRANSFORMERS_CACHE', 'TORCH_HOME', 'NUMBA_CACHE_DIR'):
            os.environ[key] = str(self.cache / key.lower())
        os.environ['ORT_DISABLE_TELEMETRY'] = '1'
        os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1',
                          HF_HUB_DISABLE_TELEMETRY='1', DO_NOT_TRACK='1',
                          PYTHONDONTWRITEBYTECODE='1', TOKENIZERS_PARALLELISM='false')

    def inside(self, relative: str) -> Path:
        path = (self.root / relative).resolve()
        if not path.is_relative_to(self.root):
            raise ValueError('Path must stay inside the Yumi installation')
        return path
