# Original Unity avatar integration — current state

The primary target is the original character in the duplicated `avatar-unity/` Unity
project, with its existing meshes, Poiyomi materials, clothing and animation assets.
The VRM experiment is preserved separately on the local Git branch
`codex/vrm-prototype-checkpoint` (`a9b42f1`); it is not the primary renderer.
The existing working Qt avatar remains the visible fallback. No second Unity project
has been created, and no original VRChat project outside this repository is accessed.

## Source availability and first priority

At inspection, GitHub `main` (`bf08f2f`) contains only README.md. The existing
`codex/windows-installer-actions` branch contains Yumi but no `avatar-unity/` directory.
The recursive GitHub tree response was not truncated. The local checkout also has no
`avatar-unity/ProjectSettings/ProjectVersion.txt`, avatar prefab, controller or materials.
The actual avatar cannot be rendered, adapted or built until those files are accessible.
No character/prefab names, blendshape mappings, Unity version or Poiyomi compatibility
are assumed. A plan document saying assets were uploaded does not establish availability.

Once the project is available, work proceeds on `codex/unity-avatar-integration`:

1. Record the actual source commit and inventory Unity version, packages, prefabs,
   humanoid animators, material/shader dependencies, clips, descriptor layers, expression
   parameters and physics components. Use the existing project and Editor version.
2. Create recoverable copies only where asset changes are needed. Get the original
   appearance rendering in a standalone Windows build first. Evaluate the free MIT
   [UniWindowController](https://github.com/kirurobo/UniWindowController) against the actual
   Unity version/render pipeline. Verify borderless alpha, focus and transparent hit
   testing on Windows; do not call these operational based on source code alone.
3. Connect the real Unity renderer to the tested localhost transport below. Map the
   avatar's actual facial blendshapes and verify synchronized mouth movement against
   real playback. The current audio signal is a spectral estimate, not phoneme timing.
4. Reuse compatible animation assets and controllers. Inventory VRChat-dependent state
   behaviours, parameters, menus, PhysBones and contacts before replacing them. Do not
   assume SDK components function in standalone players or silently drop their features.
5. Implement/verify Unity-owned full-body locomotion, Win32/DWM window geometry, platform
   support, ballistic jumping and pathfinding, then optional autonomy. High-level actions
   from Yumi must never mutate actual application windows or execute shell commands.

There is currently **no buildable Unity overlay or operational original-avatar renderer**
in this integration stage. These are required next steps, not completed features.

## Inspect the duplicate locally

The read-only static inventory works without launching Unity or installing Python:

```powershell
$r = 'C:\Users\jason\3D Objects\yumi'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\scripts\Inspect-AvatarUnity.ps1" -ProjectRoot "$r\avatar-unity"
```

It writes `logs/avatar-unity/project-audit.json` and does not modify project assets.
Static GUID references cannot establish FBX blendshapes or runtime compatibility.

For the real rig, face and controller inventory, add the scoped Editor inspector:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\scripts\Install-UnityInspector.ps1" -ProjectRoot "$r\avatar-unity"
```

This version-gated installer copies only the two inspection extension files into
`Assets/YumiDesktopInspection/Editor`, backing up existing copies of those files. It
does not change packages, project settings, scenes, materials, controllers or prefabs.
It targets Unity 2019.4 or newer, pending verification against the actual project.
Open the **duplicate** with its existing Unity version and choose
**Yumi → Inspect duplicated avatar project (read only)**. This writes
`logs/avatar-unity/editor-audit.json`. The report lists humanoid prefab candidates,
skinned mesh blendshape/bone names, materials and shaders, animation layers, clips,
parameters, controller state behaviour types, and avatar descriptor references.
The Editor extension has not been compiled against the user's unavailable Unity project.

## Backend transport and controls (implemented)

The control board has a **Unity avatar** page. Its controls configure state sent to a
future connected renderer; they do not implement a renderer themselves. The bridge is
off by default, preserving current startup and device behaviour. Enable it to start a
local WebSocket server on an ephemeral loopback port. A native client discovers:

```
<Yumi root>/tmp/avatar-bridge.json
```

The descriptor contains protocol version, session ID, URL and a short-lived random
token. It is removed at shutdown. It is not a public endpoint or a user-facing URL.
Connect with `Authorization: Bearer <token>`; browser Origins, unauthenticated clients
and multiple active renderers are rejected. Never commit the descriptor or log its token.

State frames arrive at 20 Hz:

```json
{"protocol":1,"type":"state","session":"<id>","sequence":1,"time_ms":0,"payload":{"state":"engaged","active":true,"speaking":true,"audio_epoch":1,"mouth":{"aa":0.2,"ih":0.1,"ou":0.1},"audio_level":0.02,"lip_sync_source":"played_audio_envelope_and_spectral_estimate","style":"neutral","affect":{"energy":0.5},"scale":1.0,"topmost":true,"clickthrough":true,"stationary":true,"autonomous":false,"frequency":0.3,"movement_speed":150,"jump_height":170}}
```

The client must report readiness before actions may be queued:

```json
{"protocol":1,"type":"hello","payload":{"ready":true,"renderer":"Unity"}}
```

Readiness must mean the real avatar and required action executor are available. No
movement is acknowledged just because a socket connects. Action messages contain an
ID and a validated high-level payload such as `{"type":"move_to_window","target":"Discord"}`.
Only the listed action types are accepted; OS commands and arbitrary coordinate fields
are rejected. Unity must validate targets and execute navigation itself, then reply:

```json
{"protocol":1,"type":"action_result","payload":{"action_id":"<id>","success":false,"message":"Destination not reachable"}}
```

No conversation, memory content, audio recording, image or internal model reasoning is
sent over this interface. Mouth signals derive from the actual output callback, reset
on interruption, and are estimates rather than verified phoneme/viseme alignment.
Quiet/pause/busy/speaking states suppress the autonomous-activity flag. The bridge never
moves, focuses, closes or captures other windows. A native ClientWebSocket adapter is staged under `integrations/unity/Runtime`, with
main-thread event delivery, bounded queues, reconnects and cancellation. It is not installed
into the project or compiled against its unavailable Editor yet. The standalone player
launch path and rig/action executor still require project inspection. Starting this bridge
alone will not launch a Unity avatar.

## Verification

- 32 Python tests pass locally, including real loopback WebSocket authentication,
  readiness gating, state delivery, high-level action delivery, invalid-client-message
  rejection, cleanup and descriptor removal, plus existing memory/audio/installer tests.
- The PowerShell fixture confirms GUID resolution, material/shader detection, unchanged
  original source hashes, scoped Editor-tool copying, and script parsing.
- The Windows Actions workflow also runs these checks; its result is the evidence for
  Windows-side Python/PowerShell behaviour. It does not compile or build Unity.
- Actual avatar rendering, Poiyomi fidelity, transparent overlay, controller adaptation,
  facial mapping, PhysBone replacements and desktop movement remain blocked on the
  unavailable project and require Unity Editor/Windows acceptance.

Conversation delivery is implemented separately: context selects response budgets and
native Kokoro speed; the personality remains editable. Speech text is cleaned and buffered
apart from display text and action metadata. No pitch/emotion API or unsupported prosody
is invented, and no TTS model is reloaded for each response.

The staged Unity-side `DesktopGeometry.cs` uses read-only Win32/DWM enumeration, physical
pixel coordinates, monitor work areas, cloaked/minimized filtering, visible top-edge
occlusion and stable live-window IDs. WinEvent hooks mark changes dirty, with bounded
reconciliation. A separate .NET test harness compiles this pure geometry code and tests
negative coordinates/occlusion plus an owned native window on Windows CI. It is not yet
a movement, collision or animation executor.
