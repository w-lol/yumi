"""Context classification, supported speech styles and incremental speech cleaning."""
from __future__ import annotations
from dataclasses import dataclass, asdict
import re

STYLES = {'neutral': 1.0, 'playful': 1.04, 'excited': 1.09, 'amused': 1.03,
          'curious': 1.0, 'thoughtful': .94, 'serious': .96, 'frustrated': .97,
          'comforting': .90}


@dataclass(frozen=True)
class Delivery:
    situation: str
    style: str
    max_tokens: int
    follow_up: bool = False
    interruption: str = 'yield'

    def instruction(self):
        return asdict(self)


def classify(text, ongoing=False, talkativeness=.7):
    value = text.casefold().strip()
    words = len(value.split())
    requested=re.search(r'\b(\d{2,5})\s*words?\b',value)
    if requested:return Delivery('extended_storytelling' if 'story' in value else 'technical_explanation','thoughtful',min(6000,max(500,int(requested[1])*2)))
    # Explicit detail wins over emotional/technical shortcuts.
    if re.search(r'\b(in detail|detailed|long (?:story|answer|explanation)|comprehensive|thorough|step.by.step|don.t (?:shorten|summarize)|full (?:story|explanation))\b', value):
        return Delivery('extended_storytelling' if 'story' in value else 'technical_explanation', 'thoughtful', 3600)
    if re.search(r'\b(frustrated|annoyed|pissed|not working|doesn.t work|broken|stop repeating)\b', value):
        return Delivery('user_frustration', 'serious', 900)
    if re.search(r'\b(grief|lonely|scared|anxious|depressed|heartbroken|sad|overwhelmed)\b', value):
        return Delivery('emotional', 'comforting', 700)
    if re.search(r'\b(grief|death|medical|serious|emergency)\b', value):
        return Delivery('serious', 'serious', 1100)
    if re.search(r'\b(joke|funny|roast|pun|make me laugh)\b', value):
        return Delivery('humor', 'amused', 400)
    if re.fullmatch(r"(?:how are you|what.s up|how.s it going)[ !?.]*",value):
        return Delivery('small_talk','neutral',160)
    if re.search(r'\b(excited|celebrate|won|got the job|great news)\b',value):
        return Delivery('small_talk','excited',400)
    if re.search(r'\b(tell (?:me|us) a story|once upon a time)\b',value):
        return Delivery('extended_storytelling','playful',1800)
    if re.fullmatch(r'(?:hey|hi|hello|yo|good (?:morning|evening|night))(?:[, !.]*yumi)?[ !.]*', value):
        return Delivery('greeting', 'playful', 100)
    if re.search(r'\b(explain|why|how|code|debug|install|compare|calculate|design|implement)\b', value):
        return Delivery('technical_explanation', 'thoughtful', 1400)
    if re.match(r'^(?:please )?(?:stop|go|jump|sit|stand|wave|run|look|move|stay)\b', value):
        return Delivery('direct_command', 'neutral', 120)
    follow_up=bool(re.search(r"\b(let.s (?:talk|chat)|ask me|your turn)\b",value))
    return Delivery('ongoing' if ongoing else 'small_talk', 'neutral', max(160, int(220 + talkativeness * 300)), follow_up)


def speech_text(text):
    """Keep visible text intact; discard presentation/control syntax from speech."""
    text = re.sub(r'```[\s\S]*?```', ' ', text)
    text = re.sub(r'(?<!\*)\*(?!\*)([^*\n]+)\*(?!\*)', ' ', text)
    text = re.sub(r'<(?:think|analysis|reasoning|tool_call)\b[^>]*>[\s\S]*?</[^>]+>', ' ', text, flags=re.I)
    text = re.sub(r'\[[^\]]*\]\(([^)]+)\)', lambda m: re.sub(r'\]\(.*', '', m.group(0))[1:], text)
    text = re.sub(r'https?://\S+', ' ', text)
    text = re.sub(r'\*[^*\n]{1,100}\*', lambda m: ' ' if re.search(r'\b(sighs?|laughs?|smiles?|thinks?|whispers?|pauses?|chuckles?|waves?|nods?)\b', m[0], re.I) else m[0].strip('*'), text)
    text = re.sub(r'\[(?:laughs?|sighs?|pause|thinking|emotion|action)[^\]]*\]', ' ', text, flags=re.I)
    text = re.sub(r'<[^>]*>|[*_`#~]|[\U0001F000-\U0001FAFF\u2600-\u27BF\ufe0f\u200d]', ' ', text)
    text = re.sub(r'^\s*(?:[-+] |\d+[.)] )', '', text, flags=re.M)
    if text.lstrip().startswith(('{', '[')):
        return ''
    return re.sub(r'\s+', ' ', text).strip()


class Phrases:
    """Do not speak incomplete Markdown fences or split decimal/abbreviated words."""
    def __init__(self):
        self.buffer = ''

    def feed(self, chunk, final=False):
        self.buffer += chunk
        result = []
        while self.buffer:
            fence = self.buffer.find('```')
            if fence == 0:
                close = self.buffer.find('```', 3)
                if close < 0:
                    if final: self.buffer = ''
                    break
                self.buffer = self.buffer[close+3:]
                continue
            limit = fence if fence >= 0 else len(self.buffer)
            matches = re.finditer(r'(?<!\bMr)(?<!\bDr)(?<!\bMs)[.!?;](?:\s+|$)|\n\s*\n', self.buffer[:limit])
            match = next((m for m in matches if self.buffer[:m.end()].count('*')%2==0),None)
            end = match.end() if match else None
            if match and not final:
                raw=self.buffer[:end]
                if end==len(self.buffer) and raw.endswith('.'):break
            if end is None and limit > 240:
                end = self.buffer.rfind(', ', 60, 240) + 1
                if end <= 0: end = self.buffer.rfind(' ', 80, 240)
            if end is None or end <= 0:
                if fence > 0:
                    end = fence
                elif final:
                    end = len(self.buffer)
                else:
                    break
            raw, self.buffer = self.buffer[:end], self.buffer[end:]
            cleaned = speech_text(raw)
            if cleaned: result.append(cleaned)
        return result
