#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [string]$Archive=(Join-Path $PSScriptRoot 'Yumi.zip')
)
$ErrorActionPreference='Stop'
$Archive=[IO.Path]::GetFullPath($Archive)
if (-not (Test-Path -LiteralPath $Archive)) { throw 'Place Update-Yumi.ps1 beside Yumi.zip and Yumi.zip.sha256 from the same workflow artifact.' }
$Checksum=$Archive+'.sha256'
if (-not (Test-Path -LiteralPath $Checksum)) { throw 'The adjacent Yumi.zip.sha256 is required.' }
$Expected=((Get-Content -Encoding ASCII -Raw -LiteralPath $Checksum).Trim() -split '\s+')[0]
if ($Expected -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -Algorithm SHA256 -LiteralPath $Archive).Hash -ne $Expected) { throw 'Update checksum mismatch; download the complete artifact again.' }
$Root=[IO.Path]::GetFullPath($Root)
$Stage=Join-Path $Root ('downloads/update-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
# Validate entries before Expand-Archive; ZIP paths must remain within the staging folder.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Zip=[IO.Compression.ZipFile]::OpenRead($Archive)
try {
    $Prefix=$Stage.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    foreach ($Entry in $Zip.Entries) {
        $Destination=[IO.Path]::GetFullPath((Join-Path $Stage $Entry.FullName))
        if (-not $Destination.StartsWith($Prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive path.' }
    }
} finally { $Zip.Dispose() }
Expand-Archive -LiteralPath $Archive -DestinationPath $Stage -Force
$Setup=Join-Path $Stage 'scripts/setup.ps1'
if (-not (Test-Path -LiteralPath $Setup)) { throw 'This is not a Yumi installer bundle.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Setup -Root $Root
if ($LASTEXITCODE -ne 0) { throw "Setup failed; original settings/memory retained. Inspect $Root\logs." }
Write-Host 'Yumi updated. Start Yumi.vbs, then use Start / Resume.'
Write-Host 'Unity source tools are included. An avatar player requires a local Unity build; see docs/UNITY_INTEGRATION.md.'
