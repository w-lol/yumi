"""Conservative textual secret redaction for archives; diagnostics already exclude text."""
import re

PATTERNS = [
    re.compile(r'(?i)\b(password|passwd|api[_ -]?key|access[_ -]?token|secret)\s*(?:is|=|:)\s*[^\s,;]+'),
    re.compile(r'\b(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|AKIA[A-Z0-9]{16})\b'),
]


def redact(text):
    for pattern in PATTERNS:
        text=pattern.sub('[REDACTED SECRET]',text)
    return text


def contains_secret(text):
    return redact(text)!=text
