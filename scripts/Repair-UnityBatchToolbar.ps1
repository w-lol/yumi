#requires -Version 5.1
[CmdletBinding()]
param([string]$Root='C:\Users\jason\3D Objects\yumi')
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
$Version=Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt') -Raw
if ($Version -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'Expected the duplicated Unity 2022.3.22f1 project.' }
if (Get-Command Get-CimInstance -ErrorAction SilentlyContinue) {
    $Open=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.IndexOf($Project,[StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    if ($Open.Count) { throw 'Close the duplicated project Editor/build process before repairing its toolbar plugin.' }
}
$File=Join-Path $Project 'Packages/jp.lilxyzw.editortoolbox/Editor/ToolbarExtension/ToolbarExtension.cs'
if (-not (Test-Path -LiteralPath $File)) { Write-Host 'Embedded lilEditorToolbox toolbar not present; no repair needed.'; return }
$Text=[IO.File]::ReadAllText($File)
$Pattern='(private\s+static\s+void\s+GetVisualElements\s*\(\s*\)\s*\{)'
$MatchesFound=[regex]::Matches($Text,$Pattern)
if ($MatchesFound.Count -ne 1) { throw 'Unrecognized lilEditorToolbox toolbar source; no files changed.' }
$After=$Text.Substring($MatchesFound[0].Index+$MatchesFound[0].Length)
if ($After -match '^\s*(?://[^\r\n]*\r?\n\s*)?if\s*\(\s*(?:UnityEngine\.)?Application\.isBatchMode\s*\)\s*return\s*;') {
    Write-Host 'lilEditorToolbox batch toolbar guard already present.'; return
}
$Backup=Join-Path $Root ('backups/unity-toolbar-'+[guid]::NewGuid().ToString('N')+'/ToolbarExtension.cs')
New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
Copy-Item -LiteralPath $File -Destination $Backup
$Newline="`n"
if ($Text.Contains("`r`n")) { $Newline="`r`n" }
$Insert=$Newline+'            // Yumi: batch mode has no editor toolbar; retain normal interactive behavior.'+$Newline+'            if (UnityEngine.Application.isBatchMode) return;'
$Repaired=$Text.Insert($MatchesFound[0].Index+$MatchesFound[0].Length,$Insert)
[IO.File]::WriteAllText($File,$Repaired,(New-Object Text.UTF8Encoding($false)))
Write-Host "Disabled only the toolbar callback in batch mode. Original source backup: $Backup"
