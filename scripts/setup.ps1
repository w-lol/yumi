#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root = 'C:\Users\jason\3D Objects\yumi',
    [switch]$Cpu,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$SourceRoot = Split-Path -Parent $PSScriptRoot
$Root = [IO.Path]::GetFullPath($Root)
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Yumi requires 64-bit Windows.' }
$Os = Get-CimInstance Win32_OperatingSystem
if ([int]$Os.BuildNumber -lt 17763) { throw 'Qt 6.8 requires Windows 10 version 1809 or newer. No OS change was made.' }
New-Item -ItemType Directory -Force -Path $Root | Out-Null
foreach ($Name in @('runtime','models','data','config','cache','tmp','logs','downloads','backups')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Root $Name) | Out-Null
}
$InstallLock = $null
try {
    $InstallLock = [IO.File]::Open((Join-Path $Root 'tmp\install.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
} catch { throw 'Another setup is running. Let it finish before retrying.' }
$SourceChanges = New-Object 'System.Collections.Generic.List[object]'
$InstallSucceeded = $false
$AssetsActivated = $false
$RollbackRoot = Join-Path $Root ('runtime\source-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$Log = Join-Path $Root 'logs\install.log'
Start-Transcript -Path $Log -Append | Out-Null
try {
    $env:TEMP = Join-Path $Root 'tmp'
    $env:TMP = $env:TEMP
    $env:PIP_CACHE_DIR = Join-Path $Root 'cache\pip'
    $env:PYTHONNOUSERSITE = '1'
    $env:PYTHONDONTWRITEBYTECODE = '1'
    $env:HF_HUB_DISABLE_TELEMETRY = '1'
    $env:DO_NOT_TRACK = '1'
    $env:ORT_DISABLE_TELEMETRY = '1'
    $env:HF_HOME = Join-Path $Root 'cache\huggingface'
    $Drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($Root))
    $Computer = Get-CimInstance Win32_ComputerSystem
    $Processor = Get-CimInstance Win32_Processor | Select-Object -First 1
    $Gpu = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion)
    $Preflight = [ordered]@{
        os = $Os.Caption; build = $Os.BuildNumber; cpu = $Processor.Name;
        ram_gib = [math]::Round($Computer.TotalPhysicalMemory / 1GB, 2);
        available_ram_gib = [math]::Round($Os.FreePhysicalMemory * 1KB / 1GB, 2);
        disk_free_gib = [math]::Round($Drive.AvailableFreeSpace / 1GB, 2);
        gpu = $Gpu; root = $Root; timestamp = (Get-Date).ToUniversalTime().ToString('o')
    }
    $Nvidia = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
    if ($Nvidia) {
        $Preflight['nvidia'] = @(& $Nvidia.Source '--query-gpu=name,driver_version,memory.total,memory.free' '--format=csv,noheader')
        if (-not $Cpu) {
            $GpuBudget = @(& $Nvidia.Source '--query-gpu=driver_version,memory.free' '--format=csv,noheader,nounits')
            if ($LASTEXITCODE -ne 0 -or $GpuBudget.Count -eq 0) { throw 'NVIDIA resource query failed. Use -Cpu or repair the existing driver.' }
            $Fields = $GpuBudget[0].Split(',')
            if ([version]$Fields[0].Trim() -lt [version]'551.61') { throw 'CUDA 12.4 requires a compatible NVIDIA driver (551.61 or newer for this build). Setup does not install drivers; -Cpu is available.' }
            if ([double]$Fields[1].Trim() -lt 6144) { throw 'The selected model needs approximately 6 GiB free VRAM. Close other GPU workloads or rerun with -Cpu.' }
        }
    } elseif (-not $Cpu) {
        throw 'NVIDIA driver utilities are unavailable. Install/repair the NVIDIA driver yourself, or rerun with -Cpu. Setup will not change system drivers.'
    }
    $Preflight | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $Root 'logs\preflight.json')
    Write-Host ($Preflight | ConvertTo-Json -Depth 6)
    if ($Drive.AvailableFreeSpace -lt 10GB) { throw 'At least 10 GiB free space is required for runtime, downloads, models and staging.' }
    if ($Computer.TotalPhysicalMemory -lt 8GB) { throw 'At least 8 GiB RAM is required; 16 GiB or more is recommended.' }
    $Manifest = Get-Content -Raw (Join-Path $SourceRoot 'assets\bootstrap-artifacts.json') | ConvertFrom-Json
    function Get-VerifiedArtifact($Entry) {
        $Destination = Join-Path $Root ('downloads\' + $Entry.filename)
        if ((Test-Path $Destination) -and ((Get-FileHash -Algorithm SHA256 $Destination).Hash.ToLowerInvariant() -eq $Entry.sha256)) { return $Destination }
        $Partial = $Destination + '.part'
        for ($Attempt = 0; $Attempt -lt 4; $Attempt++) {
            try {
                Invoke-WebRequest -UseBasicParsing -Uri $Entry.url -OutFile $Partial -TimeoutSec 120
                if ((Get-FileHash -Algorithm SHA256 $Partial).Hash.ToLowerInvariant() -ne $Entry.sha256) {
                    Remove-Item $Partial -Force
                    throw 'Artifact SHA-256 did not match the release manifest.'
                }
                Move-Item -Force $Partial $Destination
                return $Destination
            } catch {
                if ($Attempt -eq 3) { throw }
                Start-Sleep -Seconds ([math]::Pow(2,$Attempt))
            }
        }
    }
    $Requirements = Join-Path $SourceRoot 'requirements-windows.lock'
    $DependencyHash = (Get-FileHash -Algorithm SHA256 $Requirements).Hash
    $PythonRelative = 'runtime\python-3.13.17-' + $DependencyHash.Substring(0,12).ToLowerInvariant()
    $PythonHome = Join-Path $Root $PythonRelative
    $Python = Join-Path $PythonHome 'tools\python.exe'
    if (-not (Test-Path $Python)) {
        $Archive = Get-VerifiedArtifact $Manifest.python
        $Stage = $PythonHome + '-stage'
        if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
        New-Item -ItemType Directory -Path $Stage | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $Stage)
        $Signature = Get-AuthenticodeSignature (Join-Path $Stage 'tools\python.exe')
        if ($Signature.Status -ne 'Valid' -or $Signature.SignerCertificate.Subject -notmatch 'Python Software Foundation') { throw 'Private Python publisher signature could not be verified. No unverified runtime was executed.' }
        & (Join-Path $Stage 'tools\python.exe') -I -c 'import sys,ssl,sqlite3; assert sys.version_info[:2] == (3,13)'
        if ($LASTEXITCODE -ne 0) { throw 'Private Python failed its native-library check.' }
        Move-Item $Stage $PythonHome
    }
    # QLockFile detects live owners and stale locks without reading any credentials.
    $ExistingApp = Join-Path $Root 'tmp\yumi.lock'
    if (Test-Path $ExistingApp) {
        & $Python -I -c 'import sys; from PySide6.QtCore import QLockFile; x=QLockFile(sys.argv[1]); sys.exit(0 if x.tryLock(100) else 3)' $ExistingApp
        if ($LASTEXITCODE -ne 0) { throw 'Close Yumi before repair/update. No worker was killed by setup.' }
    }
    $Stamp = Join-Path $PythonHome 'dependencies.sha256'
    $Refresh = -not (Test-Path $Stamp)
    if (-not $Refresh) { $Refresh = ((Get-Content -Raw $Stamp).Trim() -ne $DependencyHash) }
    if (-not $Refresh) {
        & $Python -I -m pip check
        $Refresh = $LASTEXITCODE -ne 0
    }
    if ($Refresh) {
        & $Python -I -m pip install --disable-pip-version-check --no-warn-script-location --require-hashes -r $Requirements
        if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed. Rerun setup to resume; memory and settings are preserved.' }
        $DependencyHash | Set-Content -Encoding ASCII $Stamp
    }
    & $Python -I -c 'import onnxruntime; onnxruntime.disable_telemetry_events(); import PySide6.QtWidgets, sherpa_onnx, sounddevice, faster_whisper, cv2, mss, sqlite3'
    if ($LASTEXITCODE -ne 0) { throw 'A native dependency could not load. Review the installation log; no system prerequisite was installed.' }
    # Copy only release-owned source directories. Preserve all data/config/model directories.
    # For upgrades, extract the release separately and invoke this script with the existing root.
    $ReleaseFiles = @('yumi','scripts','assets','docs','tests','integrations','README.md','requirements.in','requirements-windows.lock','LICENSE')
    foreach ($Item in $ReleaseFiles) {
        $Source = Join-Path $SourceRoot $Item
        $Destination = Join-Path $Root $Item
        if ((Test-Path $Source) -and ([IO.Path]::GetFullPath($Source) -ne [IO.Path]::GetFullPath($Destination))) {
            $SourceFiles = @()
            if ((Get-Item $Source).PSIsContainer) { $SourceFiles = @(Get-ChildItem -LiteralPath $Source -Recurse -File) }
            else { $SourceFiles = @(Get-Item $Source) }
            foreach ($File in $SourceFiles) {
                $Relative = $File.FullName.Substring($SourceRoot.Length).TrimStart('\','/')
                $Target = Join-Path $Root $Relative
                $Backup = Join-Path $RollbackRoot $Relative
                $Existed = Test-Path $Target
                if ($Existed) {
                    New-Item -ItemType Directory -Force (Split-Path $Backup -Parent) | Out-Null
                    Copy-Item -LiteralPath $Target -Destination $Backup -Force
                }
                $SourceChanges.Add(@{target=$Target; backup=$Backup; existed=$Existed})
                New-Item -ItemType Directory -Force (Split-Path $Target -Parent) | Out-Null
                Copy-Item -LiteralPath $File.FullName -Destination $Target -Force
            }
        }
    }
    $AssetArgs = @((Join-Path $Root 'scripts\install_assets.py'), '--root', $Root)
    if ($Cpu) { $AssetArgs += '--cpu' }
    & $Python -I @AssetArgs
    if ($LASTEXITCODE -ne 0) { throw 'Model/runtime installation did not finish. Verified partial downloads and user data are retained; rerun setup.' }
    $AssetsActivated = $true
    if ($Cpu) {
        & $Python -I -c 'import sys; sys.path.insert(0,sys.argv[1]); from yumi.paths import Paths; from yumi.settings import Settings; Settings(Paths(sys.argv[1])).set("gpu_layers",0)' $Root
    }
    & $Python -I (Join-Path $Root 'scripts\run.py') --root $Root --smoke
    if ($LASTEXITCODE -ne 0) { throw 'Functional smoke did not pass. See logs\target-smoke.json. Setup has not claimed readiness.' }
    $OwnedFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($OwnedDirectory in @('yumi','scripts','assets','docs','tests','integrations','runtime','models','downloads')) {
        $OwnedPath = Join-Path $Root $OwnedDirectory
        if (Test-Path $OwnedPath) {
            Get-ChildItem -LiteralPath $OwnedPath -Recurse -File | ForEach-Object { $OwnedFiles.Add($_.FullName.Substring($Root.Length).TrimStart('\','/')) }
        }
    }
    foreach ($OwnedFile in @('README.md','requirements.in','requirements-windows.lock','LICENSE','Yumi.vbs')) { $OwnedFiles.Add($OwnedFile) }
    @{'schema'=1;'version'='0.1.0';'root'=$Root;'owned_files'=$OwnedFiles.ToArray();'ready_at'=(Get-Date).ToUniversalTime().ToString('o')} |
        ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $Root 'config\install-state.json')
    $PythonRelative | Set-Content -Encoding ASCII (Join-Path $Root 'config\python-runtime.txt')
    Copy-Item (Join-Path $Root 'scripts\Yumi.vbs') (Join-Path $Root 'Yumi.vbs') -Force
    $InstallSucceeded = $true
    Write-Host 'Setup and functional smoke passed. The two windows open inactive; Start enables sensors.'
    if (-not $NoLaunch) {
        $LaunchArgs = '-I "' + (Join-Path $Root 'scripts\run.py') + '" --root "' + $Root + '"'
        Start-Process -FilePath (Join-Path $PythonHome 'tools\pythonw.exe') -ArgumentList $LaunchArgs -WorkingDirectory $Root
    }
 } finally {
    if (-not $InstallSucceeded) {
        if ($AssetsActivated) {
            $AssetState = Get-Content -Raw (Join-Path $Root 'config\assets-state.json') | ConvertFrom-Json
            foreach ($Pair in @(@('runtime\llama','runtime\llama-previous',$AssetState.changed_runtime),@('models\voice','models\voice-previous',$AssetState.changed_voice))) {
                $Active = Join-Path $Root $Pair[0]
                $Previous = Join-Path $Root $Pair[1]
                if ($Pair[2] -and (Test-Path $Previous)) {
                    $Failed = $Active + '-failed-' + (Get-Date -Format 'yyyyMMddHHmmss')
                    Move-Item $Active $Failed
                    Move-Item $Previous $Active
                }
            }
            $OldState = Join-Path $Root 'config\assets-state.previous.json'
            if (Test-Path $OldState) { Copy-Item $OldState (Join-Path $Root 'config\assets-state.json') -Force }
        }
        foreach ($Change in $SourceChanges) {
            if ($Change.existed) { Copy-Item -LiteralPath $Change.backup -Destination $Change.target -Force }
            elseif (Test-Path $Change.target) { Remove-Item -LiteralPath $Change.target -Force }
        }
    }
    Stop-Transcript | Out-Null
    if ($InstallLock) { $InstallLock.Dispose() }
}
