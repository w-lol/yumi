#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Install Unity 2022.3.22f1 with Windows Build Support, or pass -UnityEditor.' }
& (Join-Path $PSScriptRoot 'Install-UnityDesktop.ps1') -ProjectRoot $Project
$Log=Join-Path $Root 'logs/avatar-unity/build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $Log -Parent) | Out-Null
$Output=Join-Path $Root 'Builds/YumiAvatar/YumiAvatar.exe'
# Start-Process explicitly waits for the GUI executable; PowerShell must not
# check output or a stale LASTEXITCODE while Unity is still importing/building.
$Arguments='-batchmode -quit -projectPath "{0}" -buildTarget Win64 -executeMethod Yumi.Desktop.Editor.YumiDesktopBuild.BuildWindows -yumiOutput "{1}" -logFile "{2}"' -f $Project,$Output,$Log
$Build=Start-Process -FilePath $UnityEditor -ArgumentList $Arguments -Wait -PassThru
if ($Build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Output)) { throw "Unity build failed (exit code $($Build.ExitCode)). See $Log for the actual error." }
Write-Host "Built $Output. Transparency, materials, rig and motion still require the Windows acceptance checklist."
