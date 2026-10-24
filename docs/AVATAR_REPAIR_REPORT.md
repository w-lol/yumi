# Avatar build repair session

User logs established three actual failures: an untitled batch startup scene prevented
additive scene creation; diagnostic code shadowed a local variable; and `pants/Body`
material slots 0/1 were null. Those failures have source fixes and regression coverage.

The missing references were traced to donor EyeBase/Blush surfaces in the pants FBX,
not the original Karin face. The earlier suggestion to restore them as pants materials
was superseded by inspecting the actual mesh. A fingerprint-checked generated copy
excludes only those donor surfaces; original clothing geometry/material is retained.

Implemented in this session:

- Read-only Blender FBX geometry/material audit with reproducible script and JSON.
- Exact repair policy with rejection tests for changed hashes/GUIDs, face references,
  assigned materials, unexpected layouts and missing clothing shaders.
- Unity Editor repair implementation and four actual-asset/scene/playback tests.
- Original descriptor expression defaults copied into the generated FX controller,
  preserving default hoodie, pants, shoes and socks rather than using all-false defaults.
- Correct Editor discovery/version checks, explicit process waiting/exit-code capture,
  preflight test gating, separate output staging, prior-player preservation/backup.
- Windows fake-process fixture covering quoted paths and real wait/exit behavior,
  failed tests/builds/incomplete output, and successful staged replacement. It does
  not imitate or claim a successful Unity build.
- A single Setup-Avatar.ps1 artifact helper plus a Build / repair button in Yumi.
- All add-on sources updated in the existing duplicate, preserving original assets.

Cloud verification: 39 Python regressions passed; real headless Qt chat/memory,
missing-voice fallback, worker restart, emergency stop and shutdown passed. Compiled
C# geometry, motion, audio timing and exact repair-policy checks passed. Roslyn also
checks local-name conflicts; full Unity API compilation is not available here.
PowerShell source-preservation fixtures/parsing passed. Windows Actions repeats
supported checks and runs the native PowerShell process fixture before packaging.

No Unity Editor/license or connection to the user's Windows PC is available in cloud.
The shipped Unity tests must run there; a final Windows avatar executable cannot be
honestly certified or produced here. The update provides an unattended local test/build/
installation path rather than another series of manual source edits. See
[AVATAR_SETUP.md](AVATAR_SETUP.md) for the one setup command and remaining acceptance.
