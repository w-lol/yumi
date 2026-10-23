#requires -Version 5.1
$ErrorActionPreference='Stop'
$TestRoot=Join-Path ([IO.Path]::GetTempPath()) ('Yumi-inspection-test-'+[guid]::NewGuid().ToString('N'))
$Project=Join-Path $TestRoot 'avatar-unity'
try {
    foreach ($Name in @('Assets','ProjectSettings','Packages')) { New-Item -ItemType Directory -Force (Join-Path $Project $Name) | Out-Null }
    'm_EditorVersion: 2022.3.22f1' | Set-Content -Encoding UTF8 (Join-Path $Project 'ProjectSettings\ProjectVersion.txt')
    '{"dependencies":{"com.example.local":"1.0.0"}}' | Set-Content -Encoding UTF8 (Join-Path $Project 'Packages\manifest.json')
    'guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.mat.meta')
    "Material:`nm_Shader: {fileID: 4800000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}" | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.mat')
    'guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\Poiyomi.shader.meta')
    'Shader "Fixture/Poiyomi" {}' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\Poiyomi.shader')
    "Animator:`nSkinnedMeshRenderer:`nVisemeBlendShapes: []`nmaterial: {fileID: 2100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}" | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.prefab')
    $Before=@{}
    Get-ChildItem -LiteralPath $Project -Recurse -File | ForEach-Object { $Before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName).Hash }
    $Output=Join-Path $TestRoot 'project-audit.json'
    & (Join-Path $PSScriptRoot 'Inspect-AvatarUnity.ps1') -ProjectRoot $Project -OutputFile $Output
    $Report=Get-Content -Encoding UTF8 -Raw -LiteralPath $Output | ConvertFrom-Json
    if ($Report.prefabs.Count -ne 1 -or -not $Report.prefabs[0].has_animator) { throw 'Prefab detection failed.' }
    if (@($Report.prefabs[0].references).Count -ne 1 -or $Report.prefabs[0].references[0] -notmatch 'original.mat$') { throw 'GUID reference resolution failed.' }
    if ($Report.materials.Count -ne 1 -or @($Report.shader_paths).Count -ne 1) { throw 'Material/shader inventory failed.' }
    if (@(Get-ChildItem -LiteralPath $Project -Recurse -File).Count -ne $Before.Count) { throw 'Read-only inspection created project files.' }
    foreach ($Path in $Before.Keys) { if ((Get-FileHash -LiteralPath $Path).Hash -ne $Before[$Path]) { throw 'Inspection modified source asset.' } }
    & (Join-Path $PSScriptRoot 'Install-UnityInspector.ps1') -ProjectRoot $Project
    if (-not (Test-Path (Join-Path $Project 'Assets\YumiDesktopInspection\Editor\YumiProjectInspection.cs'))) { throw 'Scoped Editor inspector installation failed.' }
    foreach ($Path in $Before.Keys) { if ((Get-FileHash -LiteralPath $Path).Hash -ne $Before[$Path]) { throw 'Inspector installation modified original fixture assets.' } }
    & (Join-Path $PSScriptRoot 'Install-UnityDesktop.ps1') -ProjectRoot $Project
    if (-not (Test-Path (Join-Path $Project 'Assets/YumiDesktop/Runtime/YumiDesktopAvatar.cs'))) { throw 'Desktop runtime installation failed.' }
    if (-not (Test-Path (Join-Path $Project 'Assets/YumiDesktop/Editor/YumiDesktopBuild.cs'))) { throw 'Desktop builder installation failed.' }
    foreach ($Path in $Before.Keys) { if ((Get-FileHash -LiteralPath $Path).Hash -ne $Before[$Path]) { throw 'Desktop installation modified original fixture assets.' } }
    # Exercise the actual uploaded wrapper/Fenriru layout as well as a direct root.
    $Nested=Join-Path $Project 'Fenriru'
    New-Item -ItemType Directory -Force -Path $Nested | Out-Null
    foreach ($Name in @('Assets','ProjectSettings','Packages')) { Move-Item -LiteralPath (Join-Path $Project $Name) -Destination $Nested }
    & (Join-Path $PSScriptRoot 'Inspect-AvatarUnity.ps1') -ProjectRoot $Project -OutputFile $Output
    & (Join-Path $PSScriptRoot 'Install-UnityDesktop.ps1') -ProjectRoot $Project
    if (-not (Test-Path (Join-Path $Nested 'Assets/YumiDesktop/Runtime/YumiDesktopAvatar.cs'))) { throw 'Nested project resolution failed.' }
    $Plugin=Join-Path $Nested 'Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCCore-Editor.dll.meta'
    New-Item -ItemType Directory -Force -Path (Split-Path $Plugin -Parent) | Out-Null
    $OriginalPlugin="fileFormatVersion: 2`r`nguid: 4ecd63eff847044b68db9453ce219299`r`nPluginImporter:`r`n  serializedVersion: 2`r`n  platformData:`r`n  - first:`r`n      Any:`r`n    second:`r`n      enabled: 1`r`n      settings: {}`r`n  userData: keep`r`n"
    [IO.File]::WriteAllText($Plugin,$OriginalPlugin)
    foreach ($Pair in @(
        @('com.vrchat.base/Runtime/VRCSDK/Plugins/SDKBase-Legacy.dll.meta','3d5d4d6a234c8bf47a3b99f4580f9f76'),
        @('com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase-Editor.dll.meta','dc5cab6c932db3247aab9f50c5f3bd5f'),
        @('com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A-Editor.dll.meta','ed88f634f22f87443b6d34bb57c93fbe')
    )) {
        $Other=Join-Path (Join-Path $Nested 'Packages') $Pair[0]
        New-Item -ItemType Directory -Force -Path (Split-Path $Other -Parent) | Out-Null
        [IO.File]::WriteAllText($Other,$OriginalPlugin.Replace('4ecd63eff847044b68db9453ce219299',$Pair[1]))
    }
    & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot
    $FixedPlugin=[IO.File]::ReadAllText($Plugin)
    foreach ($Meta in Get-ChildItem (Join-Path $Nested 'Packages') -Recurse -Filter '*.dll.meta') {
        $PluginText=[IO.File]::ReadAllText($Meta.FullName)
        if ($PluginText -notmatch 'Any:\r?\n    second:\r?\n      enabled: 0' -or $PluginText -notmatch 'Editor: Editor\r?\n    second:\r?\n      enabled: 1') { throw "Editor dependency was not excluded: $($Meta.Name)" }
    }
    if ($FixedPlugin -notmatch 'Any:\r?\n    second:\r?\n      enabled: 0' -or $FixedPlugin -notmatch 'Editor: Editor\r?\n    second:\r?\n      enabled: 1' -or $FixedPlugin -notmatch 'userData: keep') { throw 'Editor-only plugin repair failed.' }
    $PluginBackups=@(Get-ChildItem (Join-Path $TestRoot 'backups') -Recurse -Filter VRCCore-Editor.dll.meta)
    if ($PluginBackups.Count -ne 1 -or [IO.File]::ReadAllText($PluginBackups[0].FullName) -cne $OriginalPlugin) { throw 'Plugin metadata backup mismatch.' }
    & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot
    if ([IO.File]::ReadAllText($Plugin) -cne $FixedPlugin) { throw 'Plugin repair is not idempotent.' }
    $World=Join-Path $Nested 'Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Scripts/Validation/WorldValidation.cs'
    New-Item -ItemType Directory -Force -Path (Split-Path $World -Parent) | Out-Null
    $WorldOriginal=@'
VRC.Core.Logger.DescribeDebugLevel(hashCode, "WorldValidation", VRC.Core.Logger.Color.red);
VRC.Core.Logger.AddDebugLevel(hashCode);
VRC.Core.Logger.LogWarning("PlayableDirector containing prefab removed", DebugLevel, playableDirector.gameObject);
VRC.Core.Logger.LogWarning("VideoPlayer using DIRECT audio output fixed.");
'@
    [IO.File]::WriteAllText($World,$WorldOriginal)
    & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot
    $WorldFixed=[IO.File]::ReadAllText($World)
    if ([regex]::Matches($WorldFixed,'// Yumi standalone SDK logging').Count -ne 4 -or [regex]::Matches($WorldFixed,'#if UNITY_EDITOR').Count -ne 4 -or [regex]::Matches($WorldFixed,'UnityEngine.Debug.LogWarning').Count -ne 2) { throw 'Standalone SDK logging adaptation failed.' }
    $WorldBackups=@(Get-ChildItem (Join-Path $TestRoot 'backups') -Recurse -Filter WorldValidation.cs)
    if ($WorldBackups.Count -ne 1 -or [IO.File]::ReadAllText($WorldBackups[0].FullName) -cne $WorldOriginal) { throw 'SDK logging backup mismatch.' }
    & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot
    if ([IO.File]::ReadAllText($World) -cne $WorldFixed) { throw 'SDK logging adaptation is not idempotent.' }
    [IO.File]::WriteAllText($World,'unexpected SDK source');$WorldRefused=$false
    try { & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot } catch { $WorldRefused=$true }
    if (-not $WorldRefused -or [IO.File]::ReadAllText($World) -cne 'unexpected SDK source') { throw 'Unknown SDK logging source was not preserved.' }
    $Unknown=$OriginalPlugin.Replace('4ecd63eff847044b68db9453ce219299','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')
    [IO.File]::WriteAllText($Plugin,$Unknown);$Refused=$false
    try { & (Join-Path $PSScriptRoot 'Repair-UnityEditorPlugin.ps1') -Root $TestRoot } catch { $Refused=$true }
    if (-not $Refused -or [IO.File]::ReadAllText($Plugin) -cne $Unknown) { throw 'Unknown plugin metadata was not preserved.' }
    # The editor-only toolbar must not reflect a nonexistent GUI in batch mode.
    $Toolbar=Join-Path $Nested 'Packages/jp.lilxyzw.editortoolbox/Editor/ToolbarExtension/ToolbarExtension.cs'
    New-Item -ItemType Directory -Force -Path (Split-Path $Toolbar -Parent) | Out-Null
    $Original="class ToolbarExtension { private static void GetVisualElements()`r`n        {`r`n            rootVisualElement = ToolbarWrap.get.m_Root;`r`n        } }"
    [IO.File]::WriteAllText($Toolbar,$Original)
    & (Join-Path $PSScriptRoot 'Repair-UnityBatchToolbar.ps1') -Root $TestRoot
    $Patched=[IO.File]::ReadAllText($Toolbar)
    if (-not $Patched.Contains('if (UnityEngine.Application.isBatchMode) return;')) { throw 'Batch toolbar guard missing.' }
    $Backups=@(Get-ChildItem (Join-Path $TestRoot 'backups') -Recurse -Filter ToolbarExtension.cs)
    if ($Backups.Count -ne 1 -or [IO.File]::ReadAllText($Backups[0].FullName) -cne $Original) { throw 'Original toolbar backup mismatch.' }
    & (Join-Path $PSScriptRoot 'Repair-UnityBatchToolbar.ps1') -Root $TestRoot
    if ([IO.File]::ReadAllText($Toolbar) -cne $Patched -or @(Get-ChildItem (Join-Path $TestRoot 'backups') -Recurse -Filter ToolbarExtension.cs).Count -ne 1) { throw 'Toolbar repair is not idempotent.' }
    [IO.File]::WriteAllText($Toolbar,'unknown plugin layout')
    $Rejected=$false
    try { & (Join-Path $PSScriptRoot 'Repair-UnityBatchToolbar.ps1') -Root $TestRoot } catch { $Rejected=$true }
    if (-not $Rejected -or [IO.File]::ReadAllText($Toolbar) -cne 'unknown plugin layout') { throw 'Unknown toolbar layout was not preserved.' }
    Remove-Item -LiteralPath $Toolbar
    & (Join-Path $PSScriptRoot 'Repair-UnityBatchToolbar.ps1') -Root $TestRoot
    # Reproduce a mixed-version installation: old builder + missing runtime helper.
    $KitBuilder=Join-Path $TestRoot 'integrations/unity/DesktopEditor/YumiDesktopBuild.cs'
    New-Item -ItemType Directory -Force -Path (Split-Path $KitBuilder -Parent) | Out-Null
    [IO.File]::WriteAllText($KitBuilder,'old builder without public Source')
    $SourceRoot=Split-Path $PSScriptRoot -Parent
    & (Join-Path $PSScriptRoot 'Repair-AvatarInstallation.ps1') -Root $TestRoot -SourceRoot $SourceRoot
    $InstalledBuilder=Join-Path $Nested 'Assets/YumiDesktop/Editor/YumiDesktopBuild.cs'
    if ([IO.File]::ReadAllText($InstalledBuilder) -notmatch 'public const string Source=') { throw 'Mixed-version builder was not repaired.' }
    if (-not (Test-Path (Join-Path $Nested 'Assets/YumiDesktop/Runtime/OverlayWindowPolicy.cs'))) { throw 'Missing overlay helper was not repaired.' }
    if (-not @(Get-ChildItem (Join-Path $TestRoot 'backups') -Recurse -Filter YumiDesktopBuild.cs | Where-Object { [IO.File]::ReadAllText($_.FullName) -ceq 'old builder without public Source' }).Count) { throw 'Old builder backup missing.' }
    # A bad source checksum must fail before replacing any installed files.
    $BadSource=Join-Path $TestRoot 'bad-source'
    New-Item -ItemType Directory -Force -Path $BadSource | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceRoot 'integrations') -Destination $BadSource -Recurse
    [IO.File]::WriteAllText((Join-Path $BadSource 'integrations/unity/DesktopEditor/YumiDesktopBuild.cs'),'corrupt download fixture')
    $BeforeBad=(Get-FileHash -LiteralPath $InstalledBuilder).Hash
    $BadRefused=$false
    try { & (Join-Path $PSScriptRoot 'Repair-AvatarInstallation.ps1') -Root $TestRoot -SourceRoot $BadSource } catch { $BadRefused=$true }
    if (-not $BadRefused -or (Get-FileHash -LiteralPath $InstalledBuilder).Hash -ne $BeforeBad) { throw 'Bad checksum replaced installed tools.' }
    $Parsed=$null; $Errors=$null
    foreach ($File in @('Repair-AvatarInstallation.ps1','Repair-UnityEditorPlugin.ps1','Repair-UnityBatchToolbar.ps1','Inspect-AvatarUnity.ps1','Install-UnityInspector.ps1','Install-UnityDesktop.ps1','Build-UnityAvatar.ps1','Setup-Avatar.ps1','validate_unity_build.ps1','Update-Yumi.ps1','setup.ps1','uninstall.ps1')) {
        [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File),[ref]$Parsed,[ref]$Errors) | Out-Null
        if ($Errors.Count) { throw ($Errors | Out-String) }
    }
    Write-Host 'Unity inspection fixture passed: references, shader inventory, source hashes, additive Editor installation and script parsing.'
} finally {
    if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force }
}
