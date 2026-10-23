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
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'))) { $ProjectRoot=Join-Path $ProjectRoot 'Fenriru' }
$Version=Get-Content -Encoding UTF8 -Raw -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt')
if ($Version -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'Use the original Unity 2022.3.22f1 project.' }
& (Join-Path $PSScriptRoot 'Install-UnityInspector.ps1') -ProjectRoot $ProjectRoot
$Source=Join-Path (Split-Path $PSScriptRoot -Parent) 'integrations/unity'
$Target=Join-Path $ProjectRoot 'Assets/YumiDesktop'
$Backup=Join-Path (Split-Path $Boundary -Parent) ('backups/unity-desktop-'+[guid]::NewGuid().ToString('N'))
foreach ($Pair in @(@('Runtime','Runtime'),@('DesktopEditor','Editor'))) {
    $Destination=Join-Path $Target $Pair[1]
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($File in Get-ChildItem -LiteralPath (Join-Path $Source $Pair[0]) -File) {
        $Path=Join-Path $Destination $File.Name
        if (Test-Path -LiteralPath $Path) {
            $Folder=Join-Path $Backup $Pair[1]
            New-Item -ItemType Directory -Force -Path $Folder | Out-Null
            Copy-Item -LiteralPath $Path -Destination (Join-Path $Folder $File.Name)
        }
        Copy-Item -LiteralPath $File.FullName -Destination $Path -Force
    }
}
Write-Host 'Added scoped runtime/build tools. Original scenes, meshes, materials and controllers were preserved.'
Write-Host 'Restore pinned VPM packages, then use Yumi > Build Windows desktop player in Unity 2022.3.22f1.'
