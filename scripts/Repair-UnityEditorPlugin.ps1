#requires -Version 5.1
[CmdletBinding()]
param([string]$Root='C:\Users\jason\3D Objects\yumi')
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
if ((Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt') -Raw) -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'Expected the duplicated Unity 2022.3.22f1 project.' }
if (Get-Command Get-CimInstance -ErrorAction SilentlyContinue) {
    $Open=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.IndexOf($Project,[StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    if ($Open.Count) { throw 'Close the duplicated project Editor/build before repairing plugin import settings.' }
}
# Audited against the complete DLL reference graph in SDK base/avatars 3.6.1.
# The Dynamics Editor DLLs already reside under Editor/; these four do not.
$Plugins=@(
    @('com.vrchat.base/Runtime/VRCSDK/Plugins/VRCCore-Editor.dll.meta','4ecd63eff847044b68db9453ce219299'),
    @('com.vrchat.base/Runtime/VRCSDK/Plugins/SDKBase-Legacy.dll.meta','3d5d4d6a234c8bf47a3b99f4580f9f76'),
    @('com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase-Editor.dll.meta','dc5cab6c932db3247aab9f50c5f3bd5f'),
    @('com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A-Editor.dll.meta','ed88f634f22f87443b6d34bb57c93fbe')
)
foreach ($Plugin in $Plugins) {
$File=Join-Path $Project ('Packages/'+$Plugin[0])
if (-not (Test-Path -LiteralPath $File)) { continue }
$Text=[IO.File]::ReadAllText($File)
if ($Text -notmatch ('(?m)^guid: '+[regex]::Escape($Plugin[1])+'\r?$') -or $Text -notmatch '(?m)^PluginImporter:\r?$') {
    throw "Unrecognized plugin metadata: $File. This file was not changed."
}
$Pattern='(?ms)^  platformData:\r?\n.*?(?=^  [A-Za-z_]+:|\z)'
$Blocks=[regex]::Matches($Text,$Pattern)
if ($Blocks.Count -ne 1) { throw 'Unrecognized plugin platform settings. No files changed.' }
# A single Any-disabled entry and Editor-enabled entry replace contradictory
# legacy Any entries. All players inherit disabled; Editor retains AnyCPU/AnyOS.
$Platforms=@'
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: AnyCPU
        OS: AnyOS
'@
$Newline="`n"
if ($Text.Contains("`r`n")) { $Newline="`r`n" }
$Platforms=($Platforms -replace '\r?\n',$Newline)+$Newline
$Updated=$Text.Remove($Blocks[0].Index,$Blocks[0].Length).Insert($Blocks[0].Index,$Platforms)
if ($Updated -ceq $Text) { Write-Host "Editor-only import settings already present: $($Plugin[0])" }
else {
    $Backup=Join-Path $Root ('backups/unity-editor-plugin-'+[guid]::NewGuid().ToString('N')+'/'+[IO.Path]::GetFileName($File))
    New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
    Copy-Item -LiteralPath $File -Destination $Backup
    [IO.File]::WriteAllText($File,$Updated,(New-Object Text.UTF8Encoding($false)))
    Write-Host "Restricted $($Plugin[0]) to Editor only. DLL and GUID unchanged. Backup: $Backup"
}
}
# SDK 3.6.1 WorldValidation refers to an Editor-only logger in runtime source.
# Preserve Editor logging and route the two player warnings to Unity's logger.
$Source=Join-Path $Project 'Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Scripts/Validation/WorldValidation.cs'
if (-not (Test-Path -LiteralPath $Source)) { Write-Host 'WorldValidation source not present; no logging adaptation needed.'; return }
$SourceText=[IO.File]::ReadAllText($Source)
$Marker='// Yumi standalone SDK logging'
if ($SourceText.Contains($Marker)) {
    if ([regex]::Matches($SourceText,[regex]::Escape($Marker)).Count -ne 4) { throw 'Incomplete SDK logging adaptation; restore the source backup before retrying.' }
    Write-Host 'WorldValidation standalone logging adaptation already present.'; return
}
$Calls=@(
    @('VRC.Core.Logger.DescribeDebugLevel(hashCode, "WorldValidation", VRC.Core.Logger.Color.red);',''),
    @('VRC.Core.Logger.AddDebugLevel(hashCode);',''),
    @('VRC.Core.Logger.LogWarning("PlayableDirector containing prefab removed", DebugLevel, playableDirector.gameObject);','UnityEngine.Debug.LogWarning("PlayableDirector containing prefab removed", playableDirector.gameObject);'),
    @('VRC.Core.Logger.LogWarning("VideoPlayer using DIRECT audio output fixed.");','UnityEngine.Debug.LogWarning("VideoPlayer using DIRECT audio output fixed.");')
)
foreach ($Pair in $Calls) {
    if ([regex]::Matches($SourceText,[regex]::Escape($Pair[0])).Count -ne 1) { throw 'Unrecognized SDK logging source; WorldValidation was not changed.' }
}
if ([regex]::Matches($SourceText,'VRC\.Core\.Logger\.').Count -ne 5) { throw 'Unexpected SDK logger dependencies; WorldValidation was not changed.' }
$SourceNewline="`n"
if ($SourceText.Contains("`r`n")) { $SourceNewline="`r`n" }
$Adapted=$SourceText
foreach ($Pair in $Calls) {
    $Replacement=$Marker+$SourceNewline+'#if UNITY_EDITOR'+$SourceNewline+$Pair[0]+$SourceNewline
    if ($Pair[1]) { $Replacement+='#else'+$SourceNewline+$Pair[1]+$SourceNewline }
    $Replacement+='#endif'+$SourceNewline
    $Adapted=$Adapted.Replace($Pair[0],$Replacement)
}
$SourceBackup=Join-Path $Root ('backups/unity-sdk-logging-'+[guid]::NewGuid().ToString('N')+'/WorldValidation.cs')
New-Item -ItemType Directory -Force -Path (Split-Path $SourceBackup -Parent) | Out-Null
Copy-Item -LiteralPath $Source -Destination $SourceBackup
[IO.File]::WriteAllText($Source,$Adapted,(New-Object Text.UTF8Encoding($false)))
Write-Host "Adapted WorldValidation player logging; Editor behavior retained. Backup: $SourceBackup"
