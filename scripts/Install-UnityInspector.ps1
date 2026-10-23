#requires -Version 5.1
[CmdletBinding()]
param([string]$ProjectRoot='C:\Users\jason\3D Objects\yumi\avatar-unity')
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
$Boundary=$ProjectRoot
while ((Split-Path $Boundary -Leaf) -ne 'avatar-unity') {
    $Parent=Split-Path $Boundary -Parent
    if (-not $Parent -or $Parent -eq $Boundary) { throw 'Only the designated avatar-unity duplicate is allowed.' }
    $Boundary=$Parent
}
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'))) {
    $Nested=Join-Path $ProjectRoot 'Fenriru'
    if (Test-Path -LiteralPath (Join-Path $Nested 'ProjectSettings/ProjectVersion.txt')) { $ProjectRoot=$Nested }
}
$Version=Get-Content -Encoding UTF8 -Raw -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt')
if ($Version -notmatch 'm_EditorVersion:\s*(\d+)\.(\d+)' -or [int]$Matches[1] -lt 2019 -or ([int]$Matches[1] -eq 2019 -and [int]$Matches[2] -lt 4)) {
    throw 'Inspection requires Unity 2019.4 or newer.'
}
$Source=Join-Path (Split-Path $PSScriptRoot -Parent) 'integrations\unity\Editor'
$Target=Join-Path $ProjectRoot 'Assets\YumiDesktopInspection\Editor'
if (-not (Test-Path -LiteralPath $Source)) { throw 'The Yumi Unity inspection integration files are missing.' }
New-Item -ItemType Directory -Force -Path $Target | Out-Null
foreach ($File in Get-ChildItem -LiteralPath $Source -File) {
    $Destination=Join-Path $Target $File.Name
    if (Test-Path -LiteralPath $Destination) {
        $Backup=Join-Path (Split-Path $Boundary -Parent) ('backups\unity-inspector-'+(Get-Date -Format yyyyMMddHHmmss))
        New-Item -ItemType Directory -Force -Path $Backup | Out-Null
        Copy-Item -LiteralPath $Destination -Destination (Join-Path $Backup $File.Name)
    }
    Copy-Item -LiteralPath $File.FullName -Destination $Destination -Force
}
Write-Host 'Added only Assets\YumiDesktopInspection\Editor. No existing avatar assets or project settings were changed.'
Write-Host 'Open this duplicated project using its existing Unity version, then choose Yumi > Inspect duplicated avatar project (read only).'
