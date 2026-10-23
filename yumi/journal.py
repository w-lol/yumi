"""Metadata-only rotating diagnostics. Never serialize sensor buffers or prompts."""
import json
import logging
from logging.handlers import RotatingFileHandler
import re
from collections import deque
from .protocol import now

SAFE_KEYS = {'state', 'component', 'error_type', 'duration_ms', 'count', 'turn_id',
             'memory_ids', 'confidence', 'reason', 'queue_depth', 'version', 'status'}

class Journal:
    def __init__(self, paths):
        self.recent = deque(maxlen=500)
        self.log = logging.getLogger('yumi.' + str(id(self)))
        self.log.setLevel(logging.INFO)
        self.log.propagate = False
        self.handler = RotatingFileHandler(paths.logs / 'events.jsonl', maxBytes=2_000_000,
                                           backupCount=4, encoding='utf-8')
        self.log.addHandler(self.handler)

    def write(self, kind, level='info', **fields):
        record = {'timestamp': now(), 'kind': kind, 'level': level}
        for key, value in fields.items():
            if key in SAFE_KEYS and isinstance(value, (str, int, float, list, bool)):
                record[key] = value[:500] if isinstance(value, str) else value
        self.recent.append(record)
        self.log.info(json.dumps(record))
        return record

    def close(self):
        self.handler.close()
        self.log.removeHandler(self.handler)
