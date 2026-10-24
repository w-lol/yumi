# Build and add the original avatar to Yumi

Close Yumi and Unity. Download the latest **Yumi-Windows-installer** artifact linked
from PR #2 and extract it once. Keep all four files together: `Yumi.zip`,
`Yumi.zip.sha256`, `Update-Yumi.ps1`, `Setup-Avatar.ps1`.

Open PowerShell in that extracted folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Setup-Avatar.ps1
```

This applies the update to `C:\Users\jason\3D Objects\yumi`, installs only scoped
add-ons into its original duplicated `avatar-unity\Fenriru` project, finds Unity
2022.3.22f1 (including `C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe`), runs
four actual Unity EditMode preflight tests, and builds/installs the Windows player.
Existing model downloads/settings/memory are reused by setup. No second Unity
project is created and the original VRChat project is not accessed.

Leave the console open. Large project imports and shader compilation can take time.
The runner explicitly waits for each Unity process and reports its actual exit code.
A build runs in a separate staging directory. An existing player is replaced only
after test success and complete build output; the previous player is backed up.
Failed tests, process failures or incomplete output leave the prior player in place.

If Yumi has already been updated, its **Unity avatar → Build / repair original avatar**
button runs the same installed builder. After successful setup/build:

1. Open `Yumi.vbs` and choose **Start / Resume**.
2. Open **Unity avatar → Launch locally built avatar**.
3. The player discovers Yumi's authenticated local backend and reports rig readiness.
   Use visibility/scale, expression preview, wave, stationary, stop and reset controls.
   Hide the separate 2D fallback from the Avatar tab if desired.

Output: `Builds\YumiAvatar\YumiAvatar.exe` with its data and UnityPlayer.dll.
Reports: `logs\avatar-unity\editor-tests.xml`, `editor-tests.log`, `build.log`, and
`material-errors.txt` when material validation fails. The generated adaptation report
is `avatar-unity\Fenriru\Assets\YumiDesktopGenerated\standalone-adaptation.txt`.

## Exact pants repair

The reported `pants/Body` null slots are not the Karin face. Independent Blender 4.3.2
inspection of the original FBX found three used sections:

| Section | Triangles | Location in original FBX |
|---|---:|---|
| EyeBase | 822 | leftover donor-eye geometry around head height |
| Blush | 41 | leftover donor-face geometry around head height |
| Belts | 4,945 | actual pants/clothing geometry |

The first two referenced absent materials. Assigning arbitrary materials would render
unwanted donor geometry. The builder instead creates a **separate generated mesh**
with those two submesh index arrays empty. It retains all 4,945 clothing triangles,
original Belts material, vertex arrays, skin weights, bind poses and blendshape data.
The primary Karin face is excluded from this repair. Original expression parameter
defaults are applied to the generated FX controller so clothing is enabled as intended. Original FBX/materials are unchanged.

This is allowed only for the exact source SHA-256, mesh GUID, material GUID,
`pants/Body` hierarchy and expected three-slot layout, with both donor materials
unassigned and a healthy clothing shader. Changed/unknown assets keep strict material
errors. The repair is recorded in the adaptation report; it is not a generic shader
replacement. Evidence: [validation/pants-mesh-audit.json](validation/pants-mesh-audit.json).

## What has and has not been tested

The cloud can run Blender asset inspection, Python/backend/audio/UI regressions,
PowerShell parsing and Windows process fixtures, and compiled pure C# repair-policy,
geometry/motion/playback tests. Those checks are distinct from running Unity.

The new **four Unity EditMode tests are shipped, not claimed as passed in cloud**:
original source/version availability; actual imported pants repair with original-mesh,
clothing/skin/blendshape preservation; interrupted playback timing; generated scene
rig/material/SDK-component validation. The setup runner executes them on your licensed
Windows Editor before publishing the build, and rejects missing/failed/zero test results.

Unity rendering, Poiyomi appearance, transparency, physical lip synchronization,
poses and desktop motion still require actual Windows visual acceptance. See
[UNITY_INTEGRATION.md](UNITY_INTEGRATION.md). SDK PhysBones/contacts and advanced
animation/gesture systems remain incomplete. Build/test success does not prove those.

## Repeating lilEditorToolbox toolbar exceptions during batch tests

`ToolbarWrap.get_m_Root` / `ToolbarExtension.GetVisualElements` exceptions indicate
an editor toolbar extension trying to access a window that batch mode does not
create. Waiting longer does not repair that callback. Stop only the batch Unity
process for the duplicated project, and retry with the updated build runner.
The runner invokes `Repair-UnityBatchToolbar.ps1` before starting Unity. This
backs up the embedded plugin source under `backups/unity-toolbar-*`, then adds
an early `Application.isBatchMode` return to its toolbar callback. Interactive
Unity behavior and avatar assets are preserved. Unknown plugin layouts are
rejected without modification; absent packages are left alone. Tests are still
required. PowerShell fixtures verify backup contents, repeated runs, absent
packages and refusal of unknown source layouts. Actual Unity testing must run
on the Windows installation.

## Selected prefab source

The desktop builder now reads `Assets/prefab-id-v1_avtr_072f0a3c-94e1-4ab0-b9b3-772521da0ae8_5472754516.prefab` directly. The open scene is irrelevant. It clones the prefab root into the generated scene, reads the FX controller from that prefab descriptor, and retains its expression defaults. Missing FX or parameter links in this upload prefab are resolved to the original Fenriru FX controller and Fenriru Parameters asset already in the duplicated project. Each repair is recorded in the generated adaptation report. Existing valid links take precedence; unavailable original assets still stop generation. The source prefab is never saved or unpacked. Editor tests verify source bytes remain unchanged and generated parameter defaults match the selected prefab.

The user verified this prefab renders with its clothing and textures on their PC. Cloud C# parsing and installer fixtures pass, but the updated Unity tests and Windows player build still require that PC. Previously passing scene-based tests do not validate this new source. The separate VRCCore-Editor.dll player inclusion error is not resolved by choosing a prefab.

The user may explicitly pass `-SkipTests` to attempt the actual build without rerunning Editor tests. This does not waive build success or output checks. It must not be described as test-validated. Missing prefab references are repaired read-only during generation, never saved back into original assets. Updated Editor tests check source prefab, original FX, and parameter asset bytes remain unchanged.

## Optional particle trail materials

The selected prefab contains particle renderers with a valid body material and an empty slot 1 for trails. When the ParticleSystem trails module is disabled, this slot is unused and is now accepted without changing its materials, activation state or source prefab. Missing particle body materials, enabled trail materials and mesh materials still fail validation. A focused Editor regression test covers these distinctions; that test requires Unity on Windows and has not run in the cloud.

## Broken inactive renderers in the upload prefab

Generation now omits renderers with unresolved materials when their GameObject is inactive in the prefab hierarchy. The face is always protected; active renderers still fail validation. Healthy inactive renderers are retained. Omitted renderers are removed only from the generated clone, leaving transforms/controller bindings and every source asset untouched. These omitted items cannot subsequently be shown by expression toggles in the desktop player. This may include optional outfit variants as well as props. Every omitted slot is recorded in `Assets/YumiDesktopGenerated/standalone-adaptation.txt`, including on a failed generation. This is a deliberate degraded rendering mode, not restoration of missing materials. The source avatar remains complete for later dependency restoration.

## VRCCore-Editor.dll player exclusion

The build runner now invokes `Repair-UnityEditorPlugin.ps1` before launching Unity. For the known embedded VRCCore-Editor.dll GUID, it backs up importer metadata and replaces contradictory legacy platform entries with Any disabled and Editor enabled. The DLL, GUID, reference validation, and unrelated importer metadata are preserved. Only the designated duplicate is modified; a running project or unknown plugin GUID stops repair. PowerShell fixtures verify Editor-only settings, original backup, idempotence and refusal of unknown metadata. Actual Windows build compatibility remains subject to Unity compilation; this is not evidence that all VRChat SDK runtime dependencies support standalone players.

## SDK WorldValidation logging in standalone builds

SDK base 3.6.1 WorldValidation.cs contains four unconditional calls (five Logger type references) to the Editor-only VRC.Core.Logger. The existing plugin repair helper now backs up this source, retains debug-level registration only under UNITY_EDITOR, and routes the two warning calls to UnityEngine.Debug.LogWarning in players. The remaining validation logic is unchanged. The helper requires exact known call statements and refuses unfamiliar source. It has been exercised against the actual public com.vrchat.base-3.6.1.zip source as well as fixtures for backups, repeated execution and unknown-source rejection. This addresses the reported CS0234 errors; full Windows compilation is still unverified.

## Complete SDK Editor DLL dependency chain

The pinned base and avatars SDK 3.6.1 managed DLL reference graph was read using System.Reflection.Metadata (without executing DLLs). The Editor dependency closure includes SDKBase-Legacy, VRCCore-Editor, VRCSDKBase-Editor and VRCSDK3A-Editor in Runtime folders, plus Contact.Editor and PhysBone.Editor already in Editor folders. The repair helper now restricts all four Runtime-folder plugins to Editor only and backs up each original metadata file. Real package metadata checks passed and all four DLL SHA256 hashes remained unchanged. The reference graph is recorded in validation/sdk-assembly-audit.json. This resolves the known inclusion chain at the importer level; additional C# dependencies and the actual Windows build remain unverified.

## White background in a running avatar player

The Windows overlay now uses a layered window with an exact magenta background key instead of relying on Unity swap-chain framebuffer alpha. Its own camera is forced to solid magenta, HDR and MSAA off, and SetLayeredWindowAttributes applies LWA_COLORKEY. The layered flag remains enabled when click-through changes; tool-window and no-activate styles are retained. This avoids removing black/white avatar details with a black/white key. Exact-key magenta pixels are transparent, so matching avatar pixels can disappear and blended edges can retain a fringe. This is a rendering tradeoff rather than full per-pixel alpha. The original prefab/materials are unchanged.

The player log records transparency setup, camera availability and graphics API, or a Win32 error. Pure style checks pass locally. A Windows-only owned hidden-window fixture configures and reads back the real native color-key attributes with click-through on/off; its execution and actual Unity visual acceptance must be distinguished from cloud syntax checks. Rebuild locally after installing YumiWindowsOverlay.cs and OverlayWindowPolicy.cs. Close the existing player first; the build runner retains the prior player until successful activation.

## Repair a mixed-version tool installation

Run scripts/Repair-AvatarInstallation.ps1 -Root <installation> -Build with Unity and the avatar player closed. This downloads all 26 tool files (complete integration source kit plus install/build/SDK repair scripts) from b33c973c85e4328aa34c530b31fcffdcbefdfda5, verifies pinned SHA256 checksums before replacing anything, backs up existing source files, invokes the matching installer and verifies deployed Runtime/Editor/EditorTests/inspection file hashes. -Build then runs the installed builder with -SkipTests as requested by the user. The existing installed player remains protected by build staging. Original avatar assets and app data are outside the synchronization scope. The updater also accepts -SourceRoot for offline fixture validation. It does not remove unrelated extra files. Cloud fixtures reproduce an old builder plus missing overlay helper and verify checksum failures leave installed tools intact. Actual Unity compilation and transparent rendering still require Windows acceptance.

## Horizontal gaze and leaving stationary mode

The camera-facing avatar root is rotated 180 degrees. Desktop horizontal cursor displacement now maps to inverse local yaw; vertical pitch remains unchanged per user confirmation that only horizontal tracking was reversed. Leaving stationary mode now releases the indefinite manual hold installed by Stay, allowing eligible autonomous routes to resume. The pure motion regression exercises Stay, verifies autonomous movement stays suppressed, releases stationary mode and verifies a reachable autonomous route starts. Movement remains off by default: uncheck Keep Unity character stationary and enable Allow autonomous activity for roaming. The exact command follow my mouse tests manual tracking without waiting for the autonomy interval. Actual head tracking and movement still require user visual confirmation after rebuilding.

## Distance-aware travel, monitor commands and conversational tone

Commands now include stop following my mouse, walk across the monitors (cycles to the next monitor), and go to the left/right monitor. Existing stop moving and jump to the other monitor remain available. No OS windows are moved. Short same-support trips up to 90 physical pixels use a procedural sidestep while facing the user; trips of 600 pixels or more select running, with walking between. The avatar smoothly rotates to face horizontal travel and returns toward the user when idle. Adjoining monitor floors at the same height are linked by continuous walking edges; gaps and height changes still use the physical jump planner and can be unreachable. Stopping at a monitor seam retains the actual supporting floor.

A nearby cursor around the upper body triggers a blended procedural arm reach while idle. This does not grab or click anything. Head tracking is suppressed during side-on travel. Body turning, sidesteps, procedural arm reach and outfit behavior require actual Unity visual testing; cloud tests verify gait selection, continuous floor crossing, no teleportation, stopping at the seam, existing routes and authenticated command handling. All 39 Python tests pass.

Dialogue instructions now request varied friendly, lightly cute vocabulary without flirting, romantic/sexual innuendo, pet names or baby talk. Existing saved persona text is preserved; the runtime tone instruction also applies to it. This is prompt guidance, not a verified model-quality claim. The complete repair now also updates the three affected app modules (controller, settings defaults and avatar command parser), with backups. Close Yumi before running the update and restart it afterward.

## Desktop behavior update: original clips, adaptive routes, dragging and vision

The builder now binds the existing Cute Idle Swing, Walk, jog, proxy strafe, chair-sit and friendly-wave clips from the uploaded original project. It copies clips into YumiDesktopGenerated to configure looping, mirrors the strafe for the other direction, and places the humanoid body layer last so copied FX layers cannot override locomotion. The mask leaves head/fingers with the original FX and runtime gaze. This is source integration, not proof of retargeting quality. The hip contact transitions to the actual window edge during chair sitting, with small lower-leg swings; standing transitions blend out that contact offset.

Navigation uses adaptive ballistic jump heights for window tops and monitor floors at unequal heights. It keeps the user-configured adaptive ceiling for windows and avoids window edges with insufficient headroom. Equal-height adjoining monitor floors still walk continuously. Shell/desktop/taskbar/tooltip and click-through overlay windows are excluded from native application detection; meaningful untitled application windows are retained. Unchanged geometry refreshes no longer replan an unchanged destination. Status reports monitor/window counts. Window-under-pointer actions now resolve actual rectangles instead of searching for an app named cursor.

Autonomy now chooses from reachable surfaces instead of requiring the foreground window, alternates destinations and can perch. It strolls along its support when there are no application windows. Busy/quiet/speech, stop, stationary and visibility controls remain. Beginning a drag pauses the planner; release snaps to a nearby real edge or falls onto a support, and applies a 45-second manual placement hold. Alt + left-drag works with click-through enabled; plain left-drag is available when click-through is off. Native drag hit testing uses projected renderer bounds, so Windows mouse/focus behavior still needs acceptance testing. No OS window is moved or input synthesized.

Reaching now solves a two-segment arm with a bent elbow, clamped reachable target and both segment lengths preserved. Gaze uses the projected head location and a wider angular range, smoothed across neck/head/eyes. Head yaw and pitch are configurable in Unity avatar settings. Body motion remains on the desktop's 2D plane; the avatar retains its 3D appearance.

Explicit screen questions now attach a fresh in-memory JPEG to the actual conversation model call. Screen settings and foreground application exclusions are checked before and after capture; screen index 0 selects the whole virtual desktop. Images are not added to chat history or archives. The existing periodic observer remains available. The real local vision model passed a synthetic blue-square question through this route; live Windows capture and small-text quality are separate acceptance checks.

The complete repair's -EnableAutonomy option backs up settings and enables the bridge, roaming, adaptive jumps, dragging, and all-monitor screen sampling while preserving persona and exclusions. Quit Yumi (including its tray process) before updating and restart afterward. It does not enable the webcam or override stop controls. The default update still stages/validates matching source files and retains the previous built player until build success. See validation/desktop-behavior.json for verified and unverified coverage.

Routing follow-ups: small adjoining floor-height changes interpolate continuously; larger monitor transfers use adaptive ballistic arcs. Window jump planning samples landing points immediately outside the source window, so centrally placed windows can be exited onto the floor. Window arcs are bounded by visible headroom, and far window-to-window travel may use a floor route rather than a direct jump. Cancelling an airborne route preserves the current safe landing instead of deleting horizontal momentum; completed autonomous perches remain seated while conversation pauses further roaming. Backend reconnects reapply configured stationary/autonomy state.
