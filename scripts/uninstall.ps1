#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='High')]
param([string]$Root = 'C:\Users\jason\3D Objects\yumi', [switch]$KeepMemory)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
$Marker=Join-Path $Root 'config\install-state.json'
if (-not (Test-Path $Marker)) { throw 'No verified Yumi installation marker here; refusing deletion.' }
if (Test-Path (Join-Path $Root 'tmp\yumi.lock')) { throw 'Close Yumi first. If it crashed, launch and close it once to clear the stale lock.' }
$State=Get-Content -Raw $Marker | ConvertFrom-Json
if ($PSCmdlet.ShouldProcess($Root, 'Remove inventoried Yumi files; preserve unknown files')) {
    $Files=@($State.owned_files)
    $Files+=@('config\assets-state.json','config\python-runtime.txt','config\install-state.json','tmp\install.lock','tmp\server-key.txt')
    if (-not $KeepMemory) {
        $Files+=@('data\memory.sqlite3','data\memory.sqlite3-wal','data\memory.sqlite3-shm','data\attention.json','data\task-state.json','config\settings.json','config\personality-history.json','config\model-lock.json')
        foreach ($Pattern in @('backups\memory-*.sqlite3','data\memory-export-*.json')) {
            Get-ChildItem -Path (Join-Path $Root $Pattern) -File -ErrorAction SilentlyContinue | ForEach-Object { $Files+=$_.FullName.Substring($Root.Length).TrimStart('\','/') }
        }
    }
    foreach ($Pattern in @('tmp\server-key-*.txt','logs\events.jsonl*','logs\install.log','logs\preflight.json','logs\target-smoke.json','logs\health-report.json','logs\diagnostics-export.json')) {
        Get-ChildItem -Path (Join-Path $Root $Pattern) -File -ErrorAction SilentlyContinue | ForEach-Object { $Files+=$_.FullName.Substring($Root.Length).TrimStart('\','/') }
    }
    foreach ($Relative in $Files) {
        $Path=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
        if (-not $Path.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe ownership entry; refusing deletion.' }
        if (Test-Path -LiteralPath $Path -PathType Leaf) { Remove-Item -LiteralPath $Path -Force }
    }
    Get-ChildItem -LiteralPath $Root -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        if (@(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $_.FullName -Force }
    }
    Write-Host 'Removed inventoried Yumi files. Unknown files and nonempty cache folders were preserved.'
    if ($KeepMemory) { Write-Host 'Memory, settings, exports and backups remain in the installation root.' }
}
