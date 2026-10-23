# Original Unity avatar integration

## Delivered and verified scope

The uploaded original project is **avatar-unity/Fenriru**, Unity **2022.3.22f1**,
from main commit `222ae1f`. It was merged into the existing integration branch;
installer and earlier Unity commits remain intact. Main has not been merged or changed.
All 72 Git LFS objects were downloaded and individually SHA-256 verified.
[Static source inventory](validation/unity-source.json) records 129 prefabs,
40 controllers, 288 materials, 1,374 animation clips, 155 shaders and 47 FBX files.

The selected desktop source is `Assets/lordhavemercy.unity`. Its descriptor references
`Assets/Imported/Fbx/Avatar/Karin_2.fbx` (humanoid importer),
`Assets/Proppa/Animations/Controller/Fenriru FX.controller`,
`Assets/Proppa/Menu/RiruExpressionsMenu.asset`, and
`Assets/Proppa/Menu/Fenriru Parameters.asset`. The separately uploaded large
`prefab-id-v1_avtr_072f0a3c-94e1-4ab0-b9b3-772521da0ae8_5472754516.prefab`
contains unresolved generated controller GUIDs and is not substituted for this scene.

The descriptor's real mouth names are `VRC.v_aa`, `VRC.v_ih`, `VRC.v_ou`;
existing Proppa face clips bind `Body` shapes including `blink`, `smile` and `jitome`.
The builder checks the imported mesh's actual visemes and reports absent optional
expressions. Static names do not establish successful FBX import or visual appearance.

**No Unity Editor or licensed build runner is available in this cloud environment.**
There is no verified Unity executable in this update. Original-avatar rendering,
Poiyomi fidelity, transparent hit testing, bone poses, physical lip alignment and
real desktop navigation have not been tested in Unity. The working Qt avatar remains
available. The VRM experiment remains preserved separately, not used as primary.

## Source implementation

Additive source files are installed directly inside the duplicated project:

- `Assets/YumiDesktop/Runtime`: existing authenticated native WebSocket client and
  Win32 geometry/motion engine, plus desktop scene executor, DWM overlay, facial/rig
  driver and bounded playback scheduler.
- `Assets/YumiDesktop/Editor`: reproducible scene generator and Windows build method.
- `Assets/YumiDesktopInspection/Editor`: read-only imported rig/material/controller audit.

The same deployment sources live under `integrations/unity` for the installer.
Installation backs up previous copies of these scoped files. Original scenes,
textures, meshes, shaders, materials and controllers were not edited.

The scene generator clones the original scene avatar into `Assets/YumiDesktopGenerated`,
keeps material references, copies the original FX controller and retains its parameters
and layers. It adds a separate body layer using available GoLoco stand, cross-legged
sit and jump clips, with blend transitions. Walking/running and waving have bounded
procedural fallbacks; these do not establish foot planting, collision-free clothing,
IK quality or natural animation. Talking uses the standing pose with live facial drive.
Other requested animation states and unrestricted gesture composition are unfinished.

VRChat descriptors, PhysBones, contacts, modular build components and incompatible
state behaviours are removed **only from the generated clone/controller**. Every
removed component is listed in `standalone-adaptation.txt`. Hair/clothing PhysBone
motion, contacts, parameter drivers and VRChat expression menus are **not operational
standalone equivalents**. Original outfit hierarchy/material state and FX assets are
retained; equivalent menu controls and physics remain future work. Build errors are
reported, rather than silently replacing shaders or rig assets.

The native overlay uses borderless DWM glass, Direct3D11, disabled DXGI flip swapchain,
transparent camera clear, HDR off, tool-window/no-activate styles and a virtual-desktop
camera. Topmost and full click-through are configurable. **Full click-through includes
the character as well as empty regions**; use the Qt board for controls. Per-pixel
character interaction is unfinished. No code focuses, moves or closes application windows.
A built Windows player is required to test transparency; Editor preview cannot prove it.

The executor connects existing Win32 surfaces to character placement. It handles
routes, jumps, carried supports, disappearing windows, timeout/failure acknowledgements,
manual stop and explicit recovery. Cursor following is restricted to the current
supporting edge; it does not yet plan multi-window pursuit. User movement commands
supersede earlier routes. Pausing/disconnecting stops movement, and disabling autonomy
stops new autonomous route selection. Monitor layout updates reconfigure the overlay.

## Build and launch on the Windows PC

1. Close Yumi. Apply the downloaded update using `Update-Yumi.ps1` beside `Yumi.zip`
   and its checksum. The default root remains `C:\Users\jason\3D Objects\yumi`.
2. Install/open **Unity 2022.3.22f1**, with Windows Build Support, through Unity Hub.
   Use only the duplicated project. The uploaded layout is `avatar-unity\Fenriru`;
   scripts also accept the originally described direct `avatar-unity` project layout.
3. Add that existing duplicate to VRChat Creator Companion and restore the versions
   in `Packages/vpm-manifest.json`. The upload includes only the core VPM resolver;
   **23 locked packages are absent**, including VRChat Avatars/Base 3.6.1, Modular
   Avatar, NDMF, VRCFury, AV3Emulator and lilToon. Do not upgrade the Unity version
   or silently substitute the missing packages. Resolve all Editor compile errors.
4. Run this from PowerShell (close the project in Editor first for batch mode):

```powershell
$r = 'C:\Users\jason\3D Objects\yumi'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\scripts\Build-UnityAvatar.ps1" -Root $r
```

Pass `-UnityEditor '...\Unity.exe'` if Hub is installed elsewhere. This copies the
scoped add-ons, generates the desktop scene, and builds
`Builds\YumiAvatar\YumiAvatar.exe`. Logs: `logs\avatar-unity\build.log`.
Alternatively use **Yumi → Create desktop scene from original avatar**, inspect the
scene/adaptation report, then **Yumi → Build Windows desktop player** in Editor.
Build-specific graphics settings are restored after the build attempt.

5. Start `Yumi.vbs`, choose **Start / Resume**, then **Unity avatar → Launch locally
   built avatar**. That enables the bridge and starts only this installation's player.
   The app owns the launched process and closes it on shutdown; unrelated processes
   are never terminated. Player log: `logs\unity-player.log`. Keep the Qt board open.
   Hide the separate 2D avatar from its Avatar tab if desired.

## Windows acceptance checklist — not yet performed

- Compare original scene versus generated scene: skin, eyes, hair, outfits,
  accessories, locked Poiyomi materials and lighting. Reject pink/missing shaders.
- Verify transparent background in the player, no taskbar/focus disruption and normal
  mouse/keyboard operation behind empty areas; test topmost/click-through toggles.
- Check viseme mapping and five-second expression previews. Audition speech and
  calibrate mouth gain/timing offset. Interrupt and restart speech; mouth must close.
- Test stand/sit/jump/idle/wave transitions and procedural fallback bounds. Inspect
  feet, knees, hands, clothing and silhouette; do not assume the poses are acceptable.
- Test reachable window routes, occlusion, dragging/closing a supporting window,
  mixed-DPI/negative-coordinate monitors, monitor removal, stationary mode, stop,
  reset, and autonomy disabled. A math simulation passing is not visual acceptance.
- Restart backend; confirm reconnect/readiness, stale mouth timeout, and no resumed
  canceled route. Test shutdown and concurrent local model/TTS GPU load.

## Transport and tested mechanics

The existing ephemeral descriptor is `tmp/avatar-bridge.json` (protocol 1, session,
loopback URL and random bearer token). Browser Origins and unauthenticated/extra
clients are rejected. Readiness requires a loaded rig and executor, not just a socket.
State is coalesced at 20 Hz. Only operational state, affect/style, settings and mouth
estimates are sent; chat, memory, recordings and internal reasoning are excluded.
Actions contain validated high-level names/targets, never shell code or raw OS coordinates.

The output callback supplies RMS/spectral mouth estimates and estimated device DAC
wall-clock timestamps. A bounded Unity-side scheduler aligns mouth and delivery style,
rejects canceled epochs, closes immediately on interruption, and expires stale audio.
This is **not phoneme recognition**. Network/frame/device latency still needs calibration.

Verification in this session: 39 Python tests, actual Kokoro synthesis plus application
queue cancellation/restart in RAM, headless Qt chat/memory/worker recovery, PowerShell
source-preservation fixtures for direct and nested layouts, and compiled pure C# geometry,
platform physics and playback scheduler tests. Roslyn parses all Unity sources as C#9
for Editor/Windows variants; it does **not** compile Unity API references. Windows Actions
repeats the supported Windows checks and produces the downloadable backend/tools update.
