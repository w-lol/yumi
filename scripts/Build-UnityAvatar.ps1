#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [string]$UnityEditor='',
    [switch]$SkipTests
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
$VersionFile=Join-Path $Project 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $VersionFile)) { throw "Duplicated Unity project not found beneath $Root\avatar-unity." }
if ((Get-Content -LiteralPath $VersionFile -Raw) -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'This avatar requires its original Unity 2022.3.22f1.' }
if (-not $UnityEditor) {
    $Candidates=@(
        'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe',
        'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
        'C:\Program Files\Unity Hub\Hub\Editor\2022.3.22f1\Editor\Unity.exe',
        'C:\Program Files\Unity\Editor\Unity.exe'
    )
    foreach ($Candidate in $Candidates) {
        if ((Test-Path -LiteralPath $Candidate) -and (Get-Item -LiteralPath $Candidate).VersionInfo.ProductVersion -like '2022.3.22f1*') { $UnityEditor=$Candidate; break }
    }
}
if (-not $UnityEditor -or -not (Test-Path -LiteralPath $UnityEditor)) { throw 'Unity 2022.3.22f1 was not found. Pass -UnityEditor with its Unity.exe path.' }
$ActualVersion=(Get-Item -LiteralPath $UnityEditor).VersionInfo.ProductVersion
if ($ActualVersion -notlike '2022.3.22f1*') { throw "Selected executable is Unity $ActualVersion; use 2022.3.22f1." }
$OpenEditors=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -and $_.CommandLine.IndexOf($Project,[StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($OpenEditors.Count) { throw 'This duplicated project is already open in Unity. Close that Editor/build process, then retry.' }
Write-Host "Using Unity $ActualVersion at $UnityEditor"
& (Join-Path $PSScriptRoot 'Repair-UnityBatchToolbar.ps1') -Root $Root
& (Join-Path $PSScriptRoot 'Install-UnityDesktop.ps1') -ProjectRoot $Project
$LogFolder=Join-Path $Root 'logs/avatar-unity'
New-Item -ItemType Directory -Force -Path $LogFolder | Out-Null
function Invoke-AvatarUnity([string[]]$Arguments,[string]$Log) {
    $AllArguments=@('-batchmode','-projectPath',$Project,'-buildTarget','Win64','-logFile',$Log)+$Arguments
    # Start-Process joins ArgumentList on Windows. Quote each argument explicitly;
    # quotes are not valid in these Windows paths and are rejected rather than interpreted.
    foreach ($Argument in $AllArguments) { if ($Argument.Contains('"')) { throw 'Unexpected quote in Unity argument.' } }
    $Quoted=($AllArguments | ForEach-Object { '"'+$_+'"' }) -join ' '
    $Process=Start-Process -FilePath $UnityEditor -ArgumentList $Quoted -WorkingDirectory $Project -Wait -PassThru
    if ($Process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $Log) {
            Select-String -LiteralPath $Log -Pattern 'error CS\d+|InvalidOperationException|Exception:|Aborting batchmode|Original material/shader|Report:' -Context 0,3 | Select-Object -Last 12 | Out-Host
        }
        throw "Unity exited with code $($Process.ExitCode). Full log: $Log"
    }
}
$TestResult=Join-Path $LogFolder 'editor-tests.xml'
if (-not $SkipTests) {
    if (Test-Path -LiteralPath $TestResult) { Remove-Item -LiteralPath $TestResult -Force }
    Write-Host 'Running original-avatar Unity EditMode tests. This can take several minutes.'
    # Unity Test Runner exits itself; -quit can terminate tests prematurely.
    Invoke-AvatarUnity -Arguments @('-runTests','-testPlatform','EditMode','-assemblyNames','Yumi.Desktop.Editor.Tests','-testResults',$TestResult) -Log (Join-Path $LogFolder 'editor-tests.log')
    if (-not (Test-Path -LiteralPath $TestResult)) { throw 'Unity did not create test results; no build was published.' }
    [xml]$Results=Get-Content -LiteralPath $TestResult -Raw
    $Run=$Results.SelectSingleNode('/test-run')
    if ($null -eq $Run -or $Run.result -ne 'Passed' -or [int]$Run.failed -ne 0 -or [int]$Run.total -lt 4 -or [int]$Run.passed -lt 4) {
        throw "Unity Editor tests failed or did not execute all four required tests. See $TestResult"
    }
    Write-Host "Unity Editor tests passed: $($Run.passed)."
} else { Write-Warning 'Unity Editor tests were explicitly skipped; this build is not test-validated.' }
$Builds=Join-Path $Root 'Builds'
$Stage=Join-Path $Builds ('.YumiAvatar-stage-'+[guid]::NewGuid().ToString('N'))
$Final=Join-Path $Builds 'YumiAvatar'
$Log=Join-Path $LogFolder 'build.log'
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
Write-Host 'Building original avatar into a separate staging folder. Leave this window open.'
Invoke-AvatarUnity -Arguments @('-quit','-executeMethod','Yumi.Desktop.Editor.YumiDesktopBuild.BuildWindows','-yumiOutput',(Join-Path $Stage 'YumiAvatar.exe')) -Log $Log
foreach ($Required in @('YumiAvatar.exe','UnityPlayer.dll','YumiAvatar_Data','yumi-avatar-build.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Stage $Required))) { throw "Incomplete Unity output: $Required missing. Existing player retained. See $Log" }
}
$Marker=Get-Content -LiteralPath (Join-Path $Stage 'yumi-avatar-build.json') -Raw | ConvertFrom-Json
if ($Marker.unity -ne '2022.3.22f1') { throw 'Build marker version mismatch; existing player retained.' }
$Backup=Join-Path $Root ('backups/avatar-player-'+[guid]::NewGuid().ToString('N'))
$HadPrevious=Test-Path -LiteralPath $Final
try {
    if ($HadPrevious) {
        New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
        Move-Item -LiteralPath $Final -Destination $Backup
    }
    Move-Item -LiteralPath $Stage -Destination $Final
} catch {
    if ($HadPrevious -and (Test-Path -LiteralPath $Backup) -and -not (Test-Path -LiteralPath $Final)) { Move-Item -LiteralPath $Backup -Destination $Final }
    throw "Could not activate the player. Close the running avatar and retry. $($_.Exception.Message)"
}
Write-Host "Built and installed $Final\YumiAvatar.exe"
Write-Host 'Open Yumi > Start / Resume > Unity avatar > Launch locally built avatar.'
Write-Host 'Build/test success does not replace visual acceptance of materials, transparency, poses and desktop movement.'
