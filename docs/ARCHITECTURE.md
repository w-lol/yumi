# Architecture and maintenance

Yumi consists of two native Qt windows in one owning Python process and a managed
llama.cpp server. The server binds only `127.0.0.1` on an ephemeral port. A fresh secret
key authenticates inference requests. Windows Job Objects terminate the private server
if the owner exits. A QLockFile prevents competing application instances. The core
emits typed/versioned events; it does not depend on the UI transport or execute shell
commands derived from chat, observations or retrieved text.

## Modules

- `controller.py`: bounded turn queue, context assembly, cancellation, worker supervision,
  explicit assistant controls, memory learning and low-priority perception scheduling.
- `inference.py`: local server lifecycle and authenticated streaming HTTP client. Final
  text filtering excludes reasoning/control tags, including tags split across chunks.
- `audio.py`: bounded microphone/VAD/STT, warmed speech model, sentence streaming and
  callback playback. Only PCM actually delivered to playback drives mouth amplitude.
- `attention.py`: address cues, recent-turn timing, question state, confidence thresholds,
  quiet mode and bounded persisted feedback calibration. Name cues are optional.
- `memory.py`, `semantic.py`: SQLite WAL/FTS, disk-resident vectors, indexed locality-
  sensitive hashing candidates and reciprocal-rank hybrid retrieval. The entire archive
  is never loaded into RAM. Encoder cache: 64 entries. Vector candidates: at most 256.
- `perception.py`: RAM-only camera preview and alternating downscaled camera/screen
  observations. Inference yields to chat/speech. Observations expire from prompt context
  after 60 seconds; the board shows their age. Excluded foreground processes fail closed
  when their identity cannot be determined.
- `protocol.py`: v1 event and capability contracts. No machine-action adapters are shipped.
- `resources.py`: background CPU, process-tree RAM, available RAM and NVIDIA measurements.
- `ui.py`, `avatar.py`: control board and original procedural 2D placeholder.

The user-supplied product specification is retained locally outside the public release. The implementation
status and remaining gaps are recorded separately in `VERIFICATION.md`.

## Memory behavior

Working conversation is capped at 12 messages with a bounded prompt text budget. SQLite
stores engaged conversation text, explicit facts/preferences, extractive episodes,
provenance, confidence, expiry and supersession. First-person preference/fact heuristics
never promote assistant output into an established fact. They are deliberately conservative;
use `remember:` for facts that do not match the heuristics. Temporary statements with
"today"/"tonight" expire. Pinned steering preferences are included in subsequent turns.

Source-memory links make corrections/deletions invalidate derived episodes and vectors.
Deletion clears the working context and encoder cache. A tombstone prevents the same
record from returning through import or backup restore. If a learned fact cites an
archived user message, forgetting it also removes that supporting message. Existing
export/backup files may still contain forgotten information; the UI discloses this and
offers backup purging. User-managed copies outside the application cannot be revoked.

Indexing runs in small background batches and pauses during conversation/perception.
Encoder version includes the model digest, so changed models trigger a resumable rebuild.
Hybrid search is approximate; indexed hash buckets bound the candidate set and may miss
some semantic neighbors. Lexical search remains available while indexing catches up.
The disk cap blocks further writes with an explicit error rather than silently deleting
curated facts. Conversation retention is independent of rotating diagnostic retention.

## Audio and attention limits

Speaker-safe mode gates microphone capture during playback and for a short tail. This
is HALF DUPLEX. Headphone mode permits barge-in based on speech energy and cancels old
speech/generation while retaining the candidate user utterance. Transcript similarity
rejects likely self-playback. No beamforming, reliable speaker identification, acoustic
echo cancellation or validated double-talk separation is claimed. Media that mentions
Yumi can still trigger her; feedback adjusts a bounded bias, not a perfect intent model.
A real room/device acceptance run is needed for thresholds and false-activation rates.

STT is endpoint based; it does not display speculative partial transcripts. LLM tokens
stream. Speech synthesis streams sentence chunks where the provider supports it. Pitch
and expressiveness controls are intentionally unavailable for the starter VITS voice.
Voice rate, volume, pronunciation and voice-asset selection work. The voice-provider
class is replaceable; assets must contain compatible ONNX, token and phonemizer files.

## Privacy boundary

Normal operation uses local model files only. Hugging Face offline flags are set before
loading providers. ONNX Runtime telemetry is disabled through its supported environment
opt-out before import and its API before sessions; its primary implementation documents
Windows ETW's separate API control. The model HTTP client ignores environment proxies
and only connects to the private loopback server. Download code is separate from runtime.

No application code writes audio buffers, input recordings, speech WAV files, screenshots
or video. Memory and logs store text/metadata only. Diagnostic exports use an allowlist,
excluding prompts, frames and audio. The optional prompt inspector is bounded and RAM-only;
it is not hidden reasoning access. Obvious credential-shaped strings are redacted from
conversation archives and rejected from curated memory. This heuristic is not a universal
secret detector, so avoid putting credentials in chat or persona configuration.

Windows may page process memory or include it in crash dumps; Yumi does not alter OS
paging/crash settings or promise physical-disk impossibility. Existing drivers, Windows
privacy permissions, OS caches, shell history and Windows-managed crash reporting can
have effects outside the installation root. Yumi does not modify these globally.

## Cancellation and supervision

The GUI never waits for model generation. Emergency Stop immediately revokes active
capture, clears audio/turn queues, invalidates stale results and schedules server shutdown.
A native recognition/synthesis kernel already in progress may finish internally; its
output is discarded. It cannot execute a machine action. The board remains responsive.
Model crashes receive at most three recovery attempts within five minutes. Repeated
failures require an explicit restart. Worker health reports distinguish unavailable
models/devices from live readiness.

## Future adapter example

The production registry is empty. An inert test adapter can be registered without
exposing a machine capability:

```python
from yumi.protocol import Capability, Registry
registry = Registry()
registry.register(Capability(
    id='test.echo', description='Return validated input without side effects',
    input_schema={'type':'object','properties':{'text':{'type':'string'}},
                  'required':['text'],'additionalProperties':False},
    output_schema={'type':'object'}), lambda arguments, cancellation: arguments)
```

Adapters declare version, input/output schemas, scopes, risk, timeout, cancellation and
optional rollback metadata. This build refuses non-inert/scoped adapters. A future
coding task must explicitly extend the independent policy hook; a model or document
cannot enable one. Cooperative cancellation is required of inert adapters; a timeout
signals cancellation but cannot forcibly kill arbitrary Python code in a thread.

## Avatar replacement

The procedural drawing in `avatar.py` is original placeholder art. Mouth movement is
amplitude-based, not phoneme visemes or a commercial rig. Keep the `Avatar` interface
and playback-level input when replacing its paint layer or introducing a local asset
renderer. Scale, opacity, pin, click-through, movement, boundaries and exclusions are
stored separately from the assistant core. Windows foreground fullscreen detection
hides the avatar; monitor changes clamp its position to an available desktop.
