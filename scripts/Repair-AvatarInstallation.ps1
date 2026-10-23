#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [switch]$Build,
    [string]$SourceRoot=''
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Project=Join-Path $Root 'avatar-unity'
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt'))) { $Project=Join-Path $Project 'Fenriru' }
if ((Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt') -Raw) -notmatch 'm_EditorVersion: 2022\.3\.22f1(?:\s|$)') { throw 'Expected the duplicated Unity 2022.3.22f1 project.' }
if (Get-Command Get-CimInstance -ErrorAction SilentlyContinue) {
    $Open=@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe' OR Name='YumiAvatar.exe'" | Where-Object {
        ($_.CommandLine -and $_.CommandLine.IndexOf($Project,[StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($_.ExecutablePath -and $_.ExecutablePath.IndexOf((Join-Path $Root 'Builds/YumiAvatar'),[StringComparison]::OrdinalIgnoreCase) -eq 0)
    })
    if ($Open.Count) { throw 'Close the duplicated Unity Editor and the Yumi avatar player before repairing.' }
}
# Every file is pinned to one revision and verified before installation starts.
$Revision='b33c973c85e4328aa34c530b31fcffdcbefdfda5'
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
    "sha256": "dc9cd26b3b1e0ad58678ca6c53705a7569af26c9585d0c0c07f2163bdebb2912"
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
    "sha256": "84d0f72a25865ab1714c611d37b6947e6d2e00aec9c47216f9f1d349aad0da0b"
  },
  {
    "path": "integrations/unity/EditorTests/Yumi.Desktop.Editor.Tests.asmdef",
    "sha256": "72a0c49cd3af64cccb5b11e381c88918b5d448fd6b82435fe95b9882da75a19d"
  },
  {
    "path": "integrations/unity/Runtime/DesktopGeometry.cs",
    "sha256": "363c9ac8b297ed33bce74e63e2c6eecc0fc8edcc70171ca39b4606a74e6ea6c0"
  },
  {
    "path": "integrations/unity/Runtime/DesktopMotion.cs",
    "sha256": "faf869a70f67576c7e9a258453f79931fcaf798fe084466f3db1a802b59b1e7d"
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
    "path": "integrations/unity/Runtime/Yumi.Desktop.Runtime.asmdef",
    "sha256": "b5ce51ffa872e3ac774abbea70137f01b7050aef481af3fc543cf4b4579a8581"
  },
  {
    "path": "integrations/unity/Runtime/YumiAvatarRig.cs",
    "sha256": "8d5221750521395b0ca544ba72cf2b3b03a2a0f2ea215c1781cd100288dadddb"
  },
  {
    "path": "integrations/unity/Runtime/YumiBackendClient.cs",
    "sha256": "a30d5799b890a3b237251471c791580302ed8d689aae4bc35b3f66e72262b5fc"
  },
  {
    "path": "integrations/unity/Runtime/YumiDesktopAvatar.cs",
    "sha256": "1c1fe40ee6679771e509f908d897b21d6af52fde9a4dda79522b9c3b6d7cf199"
  },
  {
    "path": "integrations/unity/Runtime/YumiWindowsOverlay.cs",
    "sha256": "03dad8ad947ab6e348ec24ae134953356511079eb92b27c04388cc39bbc6f3f1"
  },
  {
    "path": "integrations/unity/Tests/GeometryTests.csproj",
    "sha256": "ab0dbc8233c313c19e3c3075663ee6527bde2ab25cdf8b09a166395b240e6761"
  },
  {
    "path": "integrations/unity/Tests/Program.cs",
    "sha256": "7b6930fb8237f70e77d324331b9be9a09a2079c3d2dcb047336c1e208694e83a"
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
    Write-Host "Complete avatar toolset synchronized to $Revision. Backups: $Backup"
} finally {
    if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
}
if ($Build) { & (Join-Path $Root 'scripts/Build-UnityAvatar.ps1') -Root $Root -SkipTests }
