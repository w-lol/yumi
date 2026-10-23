"""Transport-independent v1 events and future capability boundary."""
from __future__ import annotations
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
import threading
import uuid
from typing import Callable


def now():
    return datetime.now(timezone.utc).isoformat()

@dataclass(frozen=True)
class Event:
    kind: str
    source: str
    session_id: str
    turn_id: str = ''
    payload: dict = field(default_factory=dict)
    timestamp: str = field(default_factory=now)
    correlation_id: str = field(default_factory=lambda: uuid.uuid4().hex)
    version: int = 1

    def to_dict(self):
        return asdict(self)

@dataclass(frozen=True)
class Capability:
    id: str
    description: str
    input_schema: dict
    output_schema: dict
    scopes: tuple[str, ...] = ()
    risk: str = 'inert'
    timeout_seconds: float = 1.0
    rollback: str | None = None
    version: int = 1

@dataclass(frozen=True)
class Result:
    status: str
    capability_id: str
    output: dict = field(default_factory=dict)
    error: str = ''
    version: int = 1

class Registry:
    def __init__(self, policy: Callable | None = None):
        self.entries = {}
        self.policy = policy or (lambda cap: cap.risk == 'inert' and not cap.scopes)

    def register(self, capability: Capability, adapter):
        if capability.id in self.entries:
            raise ValueError('Duplicate capability')
        if capability.risk != 'inert' or capability.scopes:
            raise ValueError('Machine action adapters are unavailable in this version')
        self.entries[capability.id] = (capability, adapter)

    def invoke(self, name, arguments, cancel: threading.Event):
        if name not in self.entries:
            return Result('unsupported', name, error='This capability is not installed')
        cap, adapter = self.entries[name]
        if not self.policy(cap):
            return Result('denied', name, error='Policy denied')
        if cancel.is_set():
            return Result('cancelled', name)
        from jsonschema import validate, ValidationError
        try:
            validate(arguments, cap.input_schema)
        except ValidationError:
            return Result('invalid', name, error='Invalid input')
        from concurrent.futures import ThreadPoolExecutor, TimeoutError
        pool = ThreadPoolExecutor(max_workers=1)
        future = pool.submit(adapter, arguments, cancel)
        try:
            output = future.result(timeout=cap.timeout_seconds)
            validate(output, cap.output_schema)
            return Result('cancelled' if cancel.is_set() else 'completed', name, output)
        except TimeoutError:
            cancel.set()
            return Result('timeout', name)
        except Exception as exc:
            return Result('failed', name, error=type(exc).__name__)
        finally:
            pool.shutdown(wait=False, cancel_futures=True)
