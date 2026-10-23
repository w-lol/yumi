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
$File=Join-Path $Project 'Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCCore-Editor.dll.meta'
if (-not (Test-Path -LiteralPath $File)) { Write-Host 'Embedded VRCCore-Editor plugin not present; no import repair needed.'; return }
$Text=[IO.File]::ReadAllText($File)
if ($Text -notmatch '(?m)^guid: 4ecd63eff847044b68db9453ce219299\r?$' -or $Text -notmatch '(?m)^PluginImporter:\r?$') {
    throw 'Unrecognized VRCCore-Editor plugin metadata. No files changed.'
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
if ($Updated -ceq $Text) { Write-Host 'VRCCore-Editor is already restricted to the Unity Editor.'; return }
$Backup=Join-Path $Root ('backups/unity-editor-plugin-'+[guid]::NewGuid().ToString('N')+'/VRCCore-Editor.dll.meta')
New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
Copy-Item -LiteralPath $File -Destination $Backup
[IO.File]::WriteAllText($File,$Updated,(New-Object Text.UTF8Encoding($false)))
Write-Host "Restricted VRCCore-Editor.dll to Editor only. DLL and GUID unchanged. Backup: $Backup"
