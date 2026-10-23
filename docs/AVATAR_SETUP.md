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

The desktop builder now reads `Assets/prefab-id-v1_avtr_072f0a3c-94e1-4ab0-b9b3-772521da0ae8_5472754516.prefab` directly. The open scene is irrelevant. It clones the prefab root into the generated scene, reads the FX controller from that prefab descriptor, and retains its expression defaults. Missing controller or parameter references stop generation instead of substituting another avatar’s assets. The source prefab is never saved or unpacked. Editor tests verify source bytes remain unchanged and generated parameter defaults match the selected prefab.

The user verified this prefab renders with its clothing and textures on their PC. Cloud C# parsing and installer fixtures pass, but the updated Unity tests and Windows player build still require that PC. Previously passing scene-based tests do not validate this new source. The separate VRCCore-Editor.dll player inclusion error is not resolved by choosing a prefab.
