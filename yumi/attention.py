from __future__ import annotations
from dataclasses import dataclass
import re
import time
import json
from .paths import atomic_json

@dataclass
class Decision:
    confidence: float
    accepted: bool
    signals: dict
    reason: str

class Attention:
    def __init__(self, path, settings, clock=time.monotonic):
        self.path, self.settings, self.clock = path, settings, clock
        self.engaged_at = -10000.0
        self.quiet_until = 0.0
        self.question = False
        self.last_features = {}
        self.feedback = []
        if path.exists():
            self.feedback = json.loads(path.read_text(encoding='utf-8'))[-200:]
        self.bias = self._bias()

    def _bias(self):
        return max(-0.25, min(0.25, sum((r['addressed'] - r['prediction']) * 0.025 for r in self.feedback)))

    def decide(self, text, echo=False, typed=False):
        timestamp = self.clock()
        features = {'name': bool(re.search(r'\b(yumi|you me|yummy)\b', text, re.I)),
                    'recent_exchange': timestamp - self.engaged_at < self.settings['engagement_seconds'],
                    'reply_to_question': self.question, 'echo': echo,
                    'direct_address': bool(re.search(r'\b(you|your|hey|hello|hi)\b', text, re.I)),
                    'typed': typed,
                    'direct_question': bool(re.search(r'^(?:hey[, ]+)?(?:can you|could you|would you|do you|what do you think|hello|hi there)\b',text,re.I))}
        score = (0.12 + 0.65 * features['name'] + 0.50 * features['recent_exchange']
                 + 0.15 * (features['reply_to_question'] and features['recent_exchange'])
                 + 0.12 * features['direct_address'] + 0.45 * features['direct_question'] + self.bias)
        score = 1.0 if typed else max(0.0, min(0.99, score))
        if echo:
            score = 0.0
        quiet = timestamp < self.quiet_until
        accepted = score >= self.settings['attention_threshold'] and (typed or not quiet)
        self.last_features = {'features': features, 'prediction': score}
        if accepted:
            self.engaged_at = timestamp
        return Decision(score, accepted, features, 'quiet mode' if quiet and not typed else 'address cues')

    def correction(self, addressed):
        if not self.last_features:
            return
        self.feedback.append(dict(self.last_features, addressed=int(addressed)))
        self.feedback = self.feedback[-200:]
        self._save()
        if addressed:
            self.engaged_at = self.clock()

    def undo(self):
        if self.feedback:
            self.feedback.pop()
            self._save()

    def reset(self):
        self.feedback = []
        self._save()

    def _save(self):
        self.bias = self._bias()
        atomic_json(self.path, self.feedback)

    def quiet(self, seconds=60):
        self.quiet_until = self.clock() + seconds

    def resume(self):
        self.quiet_until = 0


def internal_control(text):
    normalized = re.sub(r'[.!?,]', '', text.lower()).strip()
    normalized = re.sub(r'^(hey )?yumi\s+', '', normalized)
    return {
        'stop speaking': 'silence', 'be quiet': 'silence', 'shut up': 'silence',
        'cancel the task': 'cancel', 'cancel': 'cancel', 'stop': 'cancel',
        'pause': 'pause', 'pause listening': 'pause', 'emergency stop': 'stop',
        'mute': 'mute', 'unmute': 'unmute', 'you can talk now': 'talk',
        'be quiet for a minute': 'quiet', "i wasn't talking to you": 'unaddressed',
        'i was not talking to you': 'unaddressed', 'i was talking to you': 'addressed',
    }.get(normalized)


def machine_action_request(text):
    text=re.sub(r'^(?:hey[, ]+)?yumi[, ]+','',text.strip(),flags=re.I)
    text=re.sub(r'^(?:(?:please|can you|could you|would you|will you)\s+)+','',text,flags=re.I)
    return bool(re.match(r'^(?:click|double.click|right.click|press (?:the )?(?:key|enter|tab)|type (?:into|in)|'
                         r'(?:send|post) (?:an? |the )?(?:email|message|tweet|reply)|'
                         r'(?:run|execute) (?:an? |the )?(?:shell|command|powershell|script)|'
                         r'(?:open|launch|close|install|uninstall|download|delete|move|rename)\b)',text,re.I))
