# Overnight development update

## Included changes

- Original Unity upload merged into the continuing integration branch. Existing
  installer/Unity commits and source avatar assets are retained. All 72 LFS files
  are actual content and pass SHA-256 checks. No merge to main was performed.
- Voice audition panel, editable comparison dialogue, neutral comparison, selectable
  Kokoro voice/rate, saved delivery-rate presets and captured per-phrase settings.
- Stream-safe parenthetical/stage-direction and hidden-reasoning removal. Avatar
  delivery style now follows the audio packet actually playing.
- Additive original-scene generator, copied FX controller, checked face mappings,
  DWM overlay source, playback-timed mouth scheduler, blink/gaze/expression driver,
  selected existing pose clips and procedural fallback movement.
- Existing window mechanics connected to a Unity root/executor in source, with
  readiness/acknowledgements, support tracking, cursor-edge following, stop/reset,
  visibility/frame-rate/animation controls and owned-player launch/shutdown.
- Disabling autonomy cancels active autonomous routes without canceling manual routes.
- Downloadable updater script with checksum/path validation, using the existing
  memory/settings-preserving setup. No Unity binary is included.

## Verification

| Check | Result and scope |
|---|---|
| Python suite | 39 passed locally; includes reconnection readiness and owned-process lifecycle mocks |
| Real Kokoro | Heart/Bella synthesis passed; actual application queue cancellation/restart and playback style passed in RAM |
| Headless Qt | Chat/memory, missing-voice fallback, worker recovery, emergency stop and shutdown passed |
| Real model integration | Qwen text/vision/auth/cancel, Kokoro→Whisper and semantic-memory deletion/restore passed |
| PowerShell | Parsing and fixtures passed for direct/nested project layouts; original fixture source hashes preserved |
| Pure C# | Compiled geometry, routes, support disappearance, stationary/autonomy controls, cross-monitor 30/60 Hz, DAC scheduler/interruption tests passed |
| Unity source syntax | C#9 parsing passed for 18 Editor/Windows variants; Unity API references are not compiled by this check |
| Actual Unity Editor/player | **Not run**; no Unity rendering, shader, animation, physics or transparency acceptance is claimed |

Real Kokoro measurements in this container: model load 1.14 seconds, first callbacks
1.17 seconds (Heart) and 0.79 seconds (Bella), on one short fixed phrase. These are
CPU/RAM measurements, not physical playback latency or subjective quality judgments.
The report is `docs/validation/kokoro-overnight-linux.json`.
Windows Actions run 39937121011 passed all supported Windows checks, including
39 Python tests, PowerShell 5.1 fixtures, native geometry/C# mechanics, real Kokoro
application-queue cancellation/restart and packaging. Final packaging follows in the
latest run linked from PR #2. Unity Editor tests remain unavailable.

## Main files

| Area | Files |
|---|---|
| Voice | `yumi/audio.py`, `delivery.py`, `settings.py`, `ui.py`; `scripts/validate_voice.py`; delivery/audio tests |
| Backend/player | `yumi/controller.py`, `unity_bridge.py`, `unity_player.py`; bridge/player tests |
| Unity add-ons | `integrations/unity/{Runtime,Editor,DesktopEditor,Tests}` and matching scoped `avatar-unity/Fenriru/Assets/YumiDesktop*` files |
| Build/install | `Build-UnityAvatar.ps1`, `Install-UnityDesktop.ps1`, updated inspector scripts, `Update-Yumi.ps1`, setup/package workflow |
| Evidence | `docs/validation/unity-source.json`, `kokoro-overnight-linux.json`, Unity/voice/install documentation |

Commits: `9351f61` (voice auditions), `e22fcf5` (non-destructive source merge),
`4fed1d0` (Unity source integration/build/executor). A final documentation/packaging
commit records this report. Full history is on PR #2's integration branch.

## Blockers and remaining work

The uploaded project is Unity 2022.3.22f1 at `avatar-unity/Fenriru`. Twenty-three
locked VPM packages are absent, including VRChat SDK 3.6.1. Restore those dependencies
on the Windows PC and compile/build using the exact commands in
[UNITY_INTEGRATION.md](UNITY_INTEGRATION.md). Source materials/animations are preserved,
but SDK PhysBones, contacts, menu controls and state drivers are not replaced yet.
Generated standalone copies explicitly exclude incompatible components and report them.

The first local priority is original-avatar material/overlay acceptance, followed by
actual face timing and body/feet validation. Then replace unsupported SDK physics and
expression controls, improve locomotion/IK, and implement bounded gesture composition
with persistent recipes. Generative gesture memory, full animation coverage and advanced
physics are not implemented. Chatterbox was evaluated from upstream documentation;
no provider or target-GPU benchmark is claimed. Kokoro remains the working engine.

Install the update with `Update-Yumi.ps1` beside its ZIP and checksum; see
[INSTALL.md](INSTALL.md). Existing Yumi remains usable while Unity validation is pending.
