#requires -Version 5.1
[CmdletBinding()]
param([string]$Root='C:\Users\jason\3D Objects\yumi',[string]$UnityEditor='')
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Archive=Join-Path $PSScriptRoot 'Yumi.zip'
# Artifact helper can apply its adjacent update; installed helper builds existing sources.
if (Test-Path -LiteralPath $Archive) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Update-Yumi.ps1') -Root $Root -Archive $Archive
    if ($LASTEXITCODE -ne 0) { throw 'Yumi update failed; avatar build was not started.' }
}
$Builder=Join-Path $Root 'scripts/Build-UnityAvatar.ps1'
if (-not (Test-Path -LiteralPath $Builder)) { throw 'Apply the Yumi update first.' }
$Arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$Builder,'-Root',$Root)
if ($UnityEditor) { $Arguments+=@('-UnityEditor',$UnityEditor) }
& powershell.exe @Arguments
if ($LASTEXITCODE -ne 0) { throw "Avatar setup stopped. Reports are in $Root\logs\avatar-unity. Your prior player was preserved." }
