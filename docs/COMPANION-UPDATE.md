# Local conversation and avatar update

Conversational controls use the local model's native tool calls. Yumi can adjust size,
movement speed, voice rate, volume, talkativeness, animation speed, head turn range,
autonomy and activity frequency within bounds. She can save explicit tone preferences,
phrases to avoid, and user preferences. Tool results report actual success or rejection;
an avatar request is not an arrival confirmation. Critical settings, OS actions, model
selection, startup, credentials, privacy permissions and memory deletion are not tools.
Disable conversational controls in Personality if desired. Saved preferences remain
editable/deletable through Memory. Custom personalities are preserved; only the exact
previous shipped default migrates to the new quieter default, with version history.

Examples: “Could you speak more slowly?”, “Make yourself a little smaller”, “Don't call
me buddy”, “Keep your replies short”, “Hang out on Firefox”, “The other PowerShell”.
Natural-language inference can still misunderstand; manual settings remain available.

Continuity imports only supported project history from this supplied conversation,
with provenance. It does not import private ChatGPT account history or fabricate a
human biography. Recent actual dialogue survives restarts; clear-context and forgetting
prevent old dialogue from reappearing. External reference notes remain separate.

The optional verified Qwen3 VL 8B profile downloads about 6.2 GB and keeps the existing
4B profile. Select Existing local model in Models & performance and restart to revert.
8B can be significantly slower, especially on CPU; no ChatGPT Voice latency or quality
parity is claimed. Kokoro remains the local voice. Pause before replying is configurable.

Public Internet lookup is read-only, optional, bounded, and returns links and retrieval
dates. It searches the current question, not private memory or screenshots. Sources
are untrusted information; they cannot invoke another tool round. This does not retrain
model weights. Disable public lookup in Personality. No accounts or remote writes.

The generated Unity clone now includes spring simulation on supported original hair
bones, approximate head/body collision, original lying/stationary clips, an arm-only
occasional sway layer, slower rest transitions, foot-bone placement correction, palm
reach targets and a persistent chosen hand with two-hand assistance. Caret attention
reads Windows geometry and activity timestamps, never typed characters. Appearance
can smoothly scale nearer/farther while movement stays on the desktop plane.

## Verified in this cloud environment (2026-10-24)

- 47 Python tests: settings persistence/bounds, critical-setting rejection, continuity
  import/forget/clear, streamed tool fragments/cancellation, app aliases, existing audio,
  installer, authenticated bridge and privacy regressions.
- Pure C# geometry/routing/dragging/timing, two-bone math, arm-choice hysteresis; C#9
  parsing and local-scope checks across 34 Editor/player source variants.
- Real local-model Qt: typed conversation, remembered fact, fresh synthetic image,
  crash recovery, stop during generation and clean shutdown.
- Downloaded 8B pair verified against pinned SHA256. Actual 8B native tool calls selected
  speaking rate 0.9 and sitting on the supplied Firefox window ID. CPU test requests
  took roughly 45–79 seconds, not a GPU performance measurement.

## Still requires Windows/Unity verification

No Unity Editor, Windows native GUI, GPU rendering, hair simulation or avatar visuals
can run here. Actual transparency, foot contact, lying poses, hand/finger alignment,
hair stability, native caret accessibility and multi-monitor movement need testing on
the user's machine. Supported hair dynamics approximate rather than reproduce VRChat
PhysBones. Edge navigation uses existing walk/jump routing; a dedicated climbing
animation/controller is not implemented. Live public search could not be verified
because this environment's network policy does not allow the search service.
Original avatar assets/controllers are preserved; only Yumi add-on files changed.
