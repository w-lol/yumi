#requires -Version 5.1
[CmdletBinding()]
param([string]$ProjectRoot='C:\Users\jason\3D Objects\yumi\avatar-unity')
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
if ((Split-Path $ProjectRoot -Leaf) -ne 'avatar-unity') { throw 'Only the designated duplicate avatar-unity may be used.' }
$Version=Get-Content -Encoding UTF8 -Raw -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt')
if ($Version -notmatch 'm_EditorVersion:\s*(\d+)\.(\d+)') { throw 'Unity Editor version could not be read.' }
if ([int]$Matches[1] -lt 2019 -or ([int]$Matches[1] -eq 2019 -and [int]$Matches[2] -lt 4)) {
    throw 'This inspector targets Unity 2019.4 or newer; report the actual version before proceeding.'
}
$Source=Join-Path (Split-Path $PSScriptRoot -Parent) 'integrations\unity\Editor'
$Target=Join-Path $ProjectRoot 'Assets\YumiDesktopInspection\Editor'
if (-not (Test-Path -LiteralPath $Source)) { throw 'The Yumi Unity inspection integration files are missing.' }
New-Item -ItemType Directory -Force -Path $Target | Out-Null
foreach ($File in Get-ChildItem -LiteralPath $Source -File) {
    $Destination=Join-Path $Target $File.Name
    if (Test-Path -LiteralPath $Destination) {
        $Backup=Join-Path (Split-Path $ProjectRoot -Parent) ('backups\unity-inspector-'+(Get-Date -Format yyyyMMddHHmmss))
        New-Item -ItemType Directory -Force -Path $Backup | Out-Null
        Copy-Item -LiteralPath $Destination -Destination (Join-Path $Backup $File.Name)
    }
    Copy-Item -LiteralPath $File.FullName -Destination $Destination -Force
}
Write-Host 'Added only Assets\YumiDesktopInspection\Editor. No existing avatar assets or project settings were changed.'
Write-Host 'Open this duplicated project using its existing Unity version, then choose Yumi > Inspect duplicated avatar project (read only).'
