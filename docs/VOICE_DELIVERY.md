# Voice delivery and audition

Kokoro remains the default working local engine. The Voice & listening tab now lets
users compare the same editable dialogue using different voices, a neutral baseline,
and saved per-style native rate multipliers. Voice and rate are captured when each
phrase is queued, so later UI changes cannot alter an already queued audition.
There is no fabricated pitch/emotion/reference-voice parameter. Styles change native
speed (and conversation phrasing); they do not turn Kokoro into an emotional actor.

Speech cleaning removes Markdown, emoji, stage directions and parenthetical asides,
including nested/unfinished parentheses and hidden reasoning split across tokens.
Display text remains unchanged. Complete requested explanations retain interruptible
queue backpressure; no sentence cap or random hesitation/silence is added. Sentence
punctuation drives Kokoro's own timing. Playback packets carry their actual delivery
style, rather than using the style of text still waiting to be synthesized.

`python scripts/validate_voice.py` synthesizes Heart and Bella in RAM, then exercises
Yumi's actual synthesis queue through cancellation/restart and playback callbacks.
No microphone/output device or waveform file is used. Timing measurements describe
this machine and test phrase; they do not establish perceptual naturalness.
See `docs/validation/kokoro-overnight-linux.json` and Windows artifact validation.

## Optional engine evaluation

Inspected upstream `resemble-ai/chatterbox` README and pyproject, version 0.1.7.
Original Chatterbox exposes CFG/exaggeration; Turbo (350M) and Nano (110M) have a
different feature set and support native paralinguistic tags. These capabilities must
not be assumed interchangeable. Upstream advertises Nano CPU speed; it was not measured
here. Its present dependency tree includes Torch/Torchaudio 2.6, Transformers 5.2,
Diffusers, Gradio and a Perth dependency from an unpinned Git master branch. It is not
a drop-in replacement for the pinned ONNX voice environment.

No GPU is exposed in this cloud environment, so concurrent RTX 4070 Super VRAM,
first-audio latency, identity consistency and subjective expressiveness could not be
compared. No Chatterbox model was downloaded and no optional provider is advertised
as functional. A future trial should use an isolated local worker/environment, pinned
assets/dependencies, identical dialogue/reference recordings, and measure cold/warm
startup, first audio, generation RTF, peak VRAM and cancellation. Keep Kokoro as fallback.

Sources: https://github.com/hexgrad/kokoro,
https://github.com/k2-fsa/sherpa-onnx,
https://github.com/resemble-ai/chatterbox.
