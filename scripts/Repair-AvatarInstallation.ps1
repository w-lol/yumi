#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [switch]$Build,
    [switch]$EnableAutonomy,
    [string]$SourceRoot=''
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
if ((Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt') -Raw) -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'Expected the duplicated Unity 2022.3.22f1 project.' }
if (Get-Command Get-CimInstance -ErrorAction SilentlyContinue) {
    $Open=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe' OR Name='YumiAvatar.exe' OR Name='python.exe' OR Name='pythonw.exe' OR Name='Yumi.exe'" | Where-Object {
        ($_.CommandLine -and $_.CommandLine.IndexOf($Project,[StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($_.ExecutablePath -and $_.ExecutablePath.IndexOf((Join-Path $Root 'Builds/YumiAvatar'),[StringComparison]::OrdinalIgnoreCase) -eq 0) -or
        ($_.CommandLine -and $_.CommandLine.IndexOf($Root,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            ($_.CommandLine.Replace('"','') -match '(?i)-m\s+yumi\b|[\\/]yumi[\\/]__main__\.py|[\\/]Yumi\.exe\b'))
    })
    if ($Open.Count) { throw 'Quit Yumi from its tray menu and close the duplicated Unity Editor/avatar player before repairing.' }
}
# Every file is pinned to one revision and verified before installation starts.
$Revision='c9963c51c8b02b9decac85ad691854b4adb3efab'
$Manifest=@'
[
  {
    "path": "integrations/unity/DesktopEditor/PantsMeshRepair.cs",
    "sha256": "dde2cf1e1fecbe0040e5bea12205536a7e22cf29df512f46493a0e62b707f558"
  },
  {
    "path": "integrations/unity/DesktopEditor/PantsRepairPolicy.cs",
    "sha256": "60fcf5f6c27effb87d5e40784a4f6d469505c824e2cc461a511a9da6e2c9dcb0"
  },
  {
    "path": "integrations/unity/DesktopEditor/Yumi.Desktop.Build.Editor.asmdef",
    "sha256": "ce6accb3d73c722fddd4d122c19e718db4856567cd45efb37569c34aa198f239"
  },
  {
    "path": "integrations/unity/DesktopEditor/YumiDesktopBuild.cs",
    "sha256": "776f1d2e3fc63f057f6a9048fbb4bea593a8d543d2b72d734b9b22cb963aa56f"
  },
  {
    "path": "integrations/unity/Editor/Yumi.Desktop.Inspection.Editor.asmdef",
    "sha256": "2f294b7ad3c5f16b703acb377c7ce20adbd150ab42d00310b036f72563999907"
  },
  {
    "path": "integrations/unity/Editor/YumiProjectInspection.cs",
    "sha256": "0f7ed8f1b874c6c269cef48d88f978a8dfd92d3ad81f90031d7585867eeba148"
  },
  {
    "path": "integrations/unity/EditorTests/OriginalAvatarBuildTests.cs",
    "sha256": "0963d8f5282e85b8cc348a438fa341ca96928ae5de0da14ea75f36700393984f"
  },
  {
    "path": "integrations/unity/EditorTests/Yumi.Desktop.Editor.Tests.asmdef",
    "sha256": "72a0c49cd3af64cccb5b11e381c88918b5d448fd6b82435fe95b9882da75a19d"
  },
  {
    "path": "integrations/unity/Runtime/DesktopGeometry.cs",
    "sha256": "e187b180e5b9a1b4ecd46957f86665910912804a7bebf34adf7e3c3b4a2fe692"
  },
  {
    "path": "integrations/unity/Runtime/DesktopMotion.cs",
    "sha256": "bc141f58747f08d3dff1154a71a6e4e7c6784a540230a18428cb00a15d874a60"
  },
  {
    "path": "integrations/unity/Runtime/OverlayWindowPolicy.cs",
    "sha256": "c3b2db864422f3554ede066b8cc380613fc0a0e1868f705eb3f9dc5a55431c69"
  },
  {
    "path": "integrations/unity/Runtime/PlaybackTimeline.cs",
    "sha256": "2f8efa77b4708f40fb816afeae0f980993180bb8f49b39fa5f1ae66975d2ef16"
  },
  {
    "path": "integrations/unity/Runtime/TwoBoneReach.cs",
    "sha256": "7501bb8cdc866b1b4743e8e4a642daaa13d851f9f2cfd76758c12ae887226f1c"
  },
  {
    "path": "integrations/unity/Runtime/Yumi.Desktop.Runtime.asmdef",
    "sha256": "b5ce51ffa872e3ac774abbea70137f01b7050aef481af3fc543cf4b4579a8581"
  },
  {
    "path": "integrations/unity/Runtime/YumiAvatarRig.cs",
    "sha256": "44b1d193db08c7ccf9d6060ee89ed85202cf074f6d98e4c98d6552d63515b822"
  },
  {
    "path": "integrations/unity/Runtime/YumiBackendClient.cs",
    "sha256": "2e61e3fcc1c9354a4ab72c017e57bc2034e981e01dac2da7f8b0130d412a07b7"
  },
  {
    "path": "integrations/unity/Runtime/YumiDesktopAvatar.cs",
    "sha256": "fade71c71c66ab71fbc68323ee8aadffe0f8c952047f05e2b3ef9dc615718be7"
  },
  {
    "path": "integrations/unity/Runtime/YumiWindowsOverlay.cs",
    "sha256": "2a64746fc10c4beeffbd3b4f0021b6c7b81ad77581ec5c5fc85103b33f6db1c4"
  },
  {
    "path": "integrations/unity/Tests/GeometryTests.csproj",
    "sha256": "01e09e1cc1dbebc2f36b41cb845e62d5d249c68876f84990674901a4abef8df0"
  },
  {
    "path": "integrations/unity/Tests/Program.cs",
    "sha256": "173cf0c397d7b5ee0f9b4acc3ea0f2eb4fc4ce580338192ce5e4fec55bb9262d"
  },
  {
    "path": "scripts/Build-UnityAvatar.ps1",
    "sha256": "fa3b6634f38d40bb8c3d82f8143b58ee4b36b667e29a51106688d1dae6ef04c0"
  },
  {
    "path": "scripts/Install-UnityDesktop.ps1",
    "sha256": "55089335442bbade33fc5191a10fa18b4929a026c471d38890a20c7502955fb9"
  },
  {
    "path": "scripts/Install-UnityInspector.ps1",
    "sha256": "61bec197abfa0feffc09fe6dd71e45a38801daa5ca6501b0c38f532d44cee7b6"
  },
  {
    "path": "scripts/Inspect-AvatarUnity.ps1",
    "sha256": "f12ee2d1b77e3142a82d41e96f4d4bd3cd223444ea6ec28359e4af333685779d"
  },
  {
    "path": "scripts/Repair-UnityBatchToolbar.ps1",
    "sha256": "a3e21158271654e81b331d134d68a0ef3d807b3eccb8d113b645bfdafcb9fdfd"
  },
  {
    "path": "scripts/Repair-UnityEditorPlugin.ps1",
    "sha256": "9b2341d9e4a0de403cc4cd40bbea4d35ab16402b1fb77079593604e41b2a4f43"
  },
  {
    "path": "scripts/Setup-Avatar.ps1",
    "sha256": "60f42515ffad889fae13c82c891f1856f106b4c94e9ff868e9ba2b1dc110e2ea"
  },
  {
    "path": "yumi/controller.py",
    "sha256": "fb785ab1659a212ac36439d20bac6cbaa777a4f179d6bcdf064b2409673762b9"
  },
  {
    "path": "yumi/settings.py",
    "sha256": "1386568594c10c2f91c59020488b6ddda841a43e7ffabe6f9ccbc8387ff8458a"
  },
  {
    "path": "yumi/unity_bridge.py",
    "sha256": "f23fbf2df4c0ac4142161403c419ac4e46c12bd7a07ab0abcd3507a31dd8c854"
  },
  {
    "path": "yumi/perception.py",
    "sha256": "d4b2ad0c852725efb138033978bda1d2b9f5fb8d11d52ce9113d5e2179626a30"
  },
  {
    "path": "yumi/ui.py",
    "sha256": "14a728d1292be67345504c0c6b4798bb6c5c4d0599ac02c2087dd3c95c08ad25"
  }
]
'@ | ConvertFrom-Json
$Stage=Join-Path $Root ('downloads/avatar-toolset-'+[guid]::NewGuid().ToString('N'))
$Backup=Join-Path $Root ('backups/avatar-toolset-'+[guid]::NewGuid().ToString('N'))
try {
    foreach ($Entry in $Manifest) {
        $Destination=Join-Path $Stage $Entry.path
        New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent) | Out-Null
        if ($SourceRoot) { Copy-Item -LiteralPath (Join-Path $SourceRoot $Entry.path) -Destination $Destination }
        else {
            Write-Host "Downloading matching avatar tool: $($Entry.path)"
            Invoke-WebRequest "https://raw.githubusercontent.com/w-lol/yumi/$Revision/$($Entry.path)" -OutFile $Destination -UseBasicParsing
        }
        if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ine $Entry.sha256) { throw "Checksum mismatch: $($Entry.path). Installation has not started." }
    }
    foreach ($Entry in $Manifest) {
        $Target=Join-Path $Root $Entry.path
        if (Test-Path -LiteralPath $Target) {
            $Saved=Join-Path $Backup $Entry.path
            New-Item -ItemType Directory -Force -Path (Split-Path $Saved -Parent) | Out-Null
            Copy-Item -LiteralPath $Target -Destination $Saved
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $Target -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $Stage $Entry.path) -Destination $Target -Force
    }
    & (Join-Path $Root 'scripts/Install-UnityDesktop.ps1') -ProjectRoot $Project
    foreach ($Pair in @(@('Runtime','Runtime'),@('DesktopEditor','Editor'),@('EditorTests','Tests/Editor'),@('Editor','../YumiDesktopInspection/Editor'))) {
        foreach ($File in Get-ChildItem -LiteralPath (Join-Path $Stage ('integrations/unity/'+$Pair[0])) -File) {
            $Installed=Join-Path $Project ('Assets/YumiDesktop/'+$Pair[1]+'/'+$File.Name)
            if (-not (Test-Path -LiteralPath $Installed) -or (Get-FileHash -LiteralPath $Installed).Hash -ne (Get-FileHash -LiteralPath $File.FullName).Hash) { throw "Installed Unity tool mismatch: $Installed" }
        }
    }
    if ($EnableAutonomy) {
        $SettingsPath=Join-Path $Root 'config/settings.json'
        if (Test-Path -LiteralPath $SettingsPath) {
            $Configuration=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
            $SavedSettings=Join-Path $Backup 'config/settings.json'
            New-Item -ItemType Directory -Force -Path (Split-Path $SavedSettings -Parent) | Out-Null
            Copy-Item -LiteralPath $SettingsPath -Destination $SavedSettings
        } else { $Configuration=[PSCustomObject]@{schema=1} }
        foreach ($Setting in @(@('unity_bridge_enabled',$true),@('unity_autonomous',$true),@('unity_stationary',$false),@('unity_drag_enabled',$true),@('unity_adaptive_jumps',$true),@('screen_enabled',$true),@('monitor_index',0))) {
            $Configuration | Add-Member -MemberType NoteProperty -Name $Setting[0] -Value $Setting[1] -Force
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $SettingsPath -Parent) | Out-Null
        $SettingsTemporary=$SettingsPath+'.avatar-update'
        [IO.File]::WriteAllText($SettingsTemporary,($Configuration | ConvertTo-Json -Depth 30),(New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $SettingsTemporary -Destination $SettingsPath -Force
        Write-Host 'Enabled autonomous desktop activity, adaptive jumps, dragging and all-monitor screen sampling. Existing persona and capture exclusions preserved.'
    }
    Write-Host "Complete avatar toolset synchronized to $Revision. Backups: $Backup"
} finally {
    if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
}
if ($Build) { & (Join-Path $Root 'scripts/Build-UnityAvatar.ps1') -Root $Root -SkipTests }
