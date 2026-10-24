#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Root='C:\Users\jason\3D Objects\yumi',
    [switch]$Build,
    [switch]$EnableAutonomy,
    [switch]$UpgradeConversation,
    [switch]$EnableWeb,
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
$Revision='d8baa29b1102371e30eb51b7ea1c5153d3b84df4'
$Manifest=@'
[
  {
    "path": "assets/continuity/development-history.json",
    "sha256": "69ddffbde8b2d86b5bb88102f8bcdd8cabd06d9b92f29ad66f0d621abff9b996"
  },
  {
    "path": "assets/conversation-model.json",
    "sha256": "4ff8a54356e33a111687d67380be9edc242ca5ab3bfdb357029d7ff79dca40c0"
  },
  {
    "path": "docs/COMPANION-UPDATE.md",
    "sha256": "3ed906fd7fe38fc43dbe2562581cb825de449a9e1f9004c820ba98477fc5a6ea"
  },
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
    "sha256": "4738a3da4039dd4b193414fc0a8db26ebbde529a1f869a0d99d6bcead4c234c8"
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
    "sha256": "5d65b8526841d530289dd49fc847cccab457faedf15af5d33cbf43e8f4c869f4"
  },
  {
    "path": "integrations/unity/EditorTests/Yumi.Desktop.Editor.Tests.asmdef",
    "sha256": "72a0c49cd3af64cccb5b11e381c88918b5d448fd6b82435fe95b9882da75a19d"
  },
  {
    "path": "integrations/unity/Runtime/DesktopAttention.cs",
    "sha256": "8cbbc854008ab3abb20c9d2a9b908aee5e920e0f51db237af14356c3e8ff8419"
  },
  {
    "path": "integrations/unity/Runtime/DesktopGeometry.cs",
    "sha256": "e187b180e5b9a1b4ecd46957f86665910912804a7bebf34adf7e3c3b4a2fe692"
  },
  {
    "path": "integrations/unity/Runtime/DesktopMotion.cs",
    "sha256": "15d3ae59f3a7f2160f56183ae0d8d253c9e9968a2fc946c60846fea560b12a73"
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
    "path": "integrations/unity/Runtime/ReachLatch.cs",
    "sha256": "0302237bf5e0fe1b9b970bdb4da2fb31fcaf860995cf676e708c21a4eb72a77d"
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
    "sha256": "052b6bf06a181dfdaffd8097ada9548258178ad3760c55dacfcab1c161a57b24"
  },
  {
    "path": "integrations/unity/Runtime/YumiBackendClient.cs",
    "sha256": "4ddad2d03eab5c9cefbe36acf92e6289a5d0e14de08cbc499577aa6b880262f9"
  },
  {
    "path": "integrations/unity/Runtime/YumiDesktopAvatar.cs",
    "sha256": "3d2c2b3be341f027f6ff8cf4487ce1c6c6c63b948925a6a88f06798c1358c517"
  },
  {
    "path": "integrations/unity/Runtime/YumiHairDynamics.cs",
    "sha256": "81c71e26953ef81d749bdde4a1b9b0baf02919ab75bee36aad679ae191d343f0"
  },
  {
    "path": "integrations/unity/Runtime/YumiWindowsOverlay.cs",
    "sha256": "2a64746fc10c4beeffbd3b4f0021b6c7b81ad77581ec5c5fc85103b33f6db1c4"
  },
  {
    "path": "integrations/unity/Tests/GeometryTests.csproj",
    "sha256": "5345ce53c5710134f1bc01d0305260dfe821f93656ac9154c57cc3e7af8e4e25"
  },
  {
    "path": "integrations/unity/Tests/Program.cs",
    "sha256": "804cdcd9475394ac88001e42fb6b449280e8bd9f117471f3f30aea3648f61f6d"
  },
  {
    "path": "scripts/Build-UnityAvatar.ps1",
    "sha256": "fa3b6634f38d40bb8c3d82f8143b58ee4b36b667e29a51106688d1dae6ef04c0"
  },
  {
    "path": "scripts/Inspect-AvatarUnity.ps1",
    "sha256": "f12ee2d1b77e3142a82d41e96f4d4bd3cd223444ea6ec28359e4af333685779d"
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
    "path": "scripts/install_assets.py",
    "sha256": "8bf691c0fe02d6a8baf1676e0460cb1ec1f79d7f1748ee836d5c26eaa33d1e82"
  },
  {
    "path": "scripts/upgrade_conversation.py",
    "sha256": "5ec6f10ed3b044624c50834c089e7f261c580cd365be4df011a2d51c03189469"
  },
  {
    "path": "yumi/__init__.py",
    "sha256": "e264da0ae7c8ceb9e29fed7d797e0db095d4117ac2048da1223f868a0dc08123"
  },
  {
    "path": "yumi/__main__.py",
    "sha256": "87a7140000ca6730b1e98288c0fdeea3fdca5a904a4103720df86386c34b2fdd"
  },
  {
    "path": "yumi/attention.py",
    "sha256": "d384cec211eb5e4dc22300aa78b41fc5d142bec7977c97c58424437b4ed90681"
  },
  {
    "path": "yumi/audio.py",
    "sha256": "f6d62fd7c539ec1f5d11b362d3a803f6630102efbe4c4c288376e3b8bb11d7c1"
  },
  {
    "path": "yumi/avatar.py",
    "sha256": "fcf06b51b664234b76a735f1d116ee9b72d7af7d1bb757c876a25934e565fc80"
  },
  {
    "path": "yumi/companion_tools.py",
    "sha256": "2e56bee7deda5d2d7ac0cc4553c74b59c61664bb634c0c99c61c83ad6c12993a"
  },
  {
    "path": "yumi/continuity.py",
    "sha256": "8970790f9b2919477c08def41a809e90ed42f158e69e13eeb16a5c4dbc4fc7bf"
  },
  {
    "path": "yumi/controller.py",
    "sha256": "ef742581045616d1ba8298fbc295a28cecd2f68626112b87785dcbeaed44f8ae"
  },
  {
    "path": "yumi/delivery.py",
    "sha256": "42de616ab104cb1488146c8e6193cf3bf65d5ff9556bbf1dfd2d22572c647a79"
  },
  {
    "path": "yumi/inference.py",
    "sha256": "c404347b25a6f654a7367114e51cfe0648a2f76a2c7220c38bf3634ce49643e5"
  },
  {
    "path": "yumi/journal.py",
    "sha256": "b223ecb2cadc38e82d7d574750c7f736a5520ee0712ea690382e119d1e88a9eb"
  },
  {
    "path": "yumi/memory.py",
    "sha256": "377ab3461f73c828e6f40f291d5aad9b682bfe52d5eea242b8d3b96eb55e81f2"
  },
  {
    "path": "yumi/model_profiles.py",
    "sha256": "d5f3ca5d56d5de67eaabbf88ff1bbd854473e28385f2dbc7a221aa7ed9525ef2"
  },
  {
    "path": "yumi/paths.py",
    "sha256": "21075f4bb5dc491dd311e6566654eb63292b4406a3e31c7e3bc33f5b614b3ab1"
  },
  {
    "path": "yumi/perception.py",
    "sha256": "d4b2ad0c852725efb138033978bda1d2b9f5fb8d11d52ce9113d5e2179626a30"
  },
  {
    "path": "yumi/privacy.py",
    "sha256": "6ac99b8ed2bb15cda63aee6644319e1246948af6cf6d0ca04eb06e49f46eebf6"
  },
  {
    "path": "yumi/protocol.py",
    "sha256": "73f0b06e8386f0328fbd521f3d3190d0957d36d036de7cbddb1056df71d8df76"
  },
  {
    "path": "yumi/resources.py",
    "sha256": "8380665b12f4499c7e353e8daf0d92f4e098f07dbbe391f899c920478068c363"
  },
  {
    "path": "yumi/semantic.py",
    "sha256": "82905361b2a69eb0c607f0ac87a6b0775943fcbfe946eb5361bcfbcd335ac993"
  },
  {
    "path": "yumi/settings.py",
    "sha256": "d81421f3f77eeec7c422374cbbaf61838accc17990b3a4477cd446381216868e"
  },
  {
    "path": "yumi/smoke.py",
    "sha256": "43662c3c7ebad90992038c64534a526f9e7e1a34def65a2f8aedd2a3c6cecc56"
  },
  {
    "path": "yumi/ui.py",
    "sha256": "c08d28764880b9a6b98a3709e06f37c27f4596043b17668be8ed1b308ac4ef5d"
  },
  {
    "path": "yumi/unity_bridge.py",
    "sha256": "d6a7bc4984d9f2af5154e6a4c36695307b967f8be955f3bdb832a915c8067e18"
  },
  {
    "path": "yumi/unity_player.py",
    "sha256": "f073196a79d5a93bbf12c65a92ee037e36fba66a48086e16e1aaf56f6ece3b1a"
  },
  {
    "path": "yumi/voice.py",
    "sha256": "1064e93a251b4a3bd38c4888c9b2459fa286fff93ef74b52b8b5963e24c534fa"
  },
  {
    "path": "yumi/web.py",
    "sha256": "329b7e39e1bc238d20aefb28189b2f933e45bbf85a851b92a5b0c99bec34c140"
  },
  {
    "path": "yumi/windows.py",
    "sha256": "db7190f85056a304ba4599c80b86a79bd86b70179f2f4c444489179b87ff24ef"
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
    if ($EnableAutonomy -or $EnableWeb) {
        $SettingsPath=Join-Path $Root 'config/settings.json'
        if (Test-Path -LiteralPath $SettingsPath) {
            $Configuration=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
            $SavedSettings=Join-Path $Backup 'config/settings.json'
            New-Item -ItemType Directory -Force -Path (Split-Path $SavedSettings -Parent) | Out-Null
            Copy-Item -LiteralPath $SettingsPath -Destination $SavedSettings
        } else { $Configuration=[PSCustomObject]@{schema=1} }
        $BehaviorSettings=@()
        if ($EnableWeb) { $BehaviorSettings+=,@('web_enabled',$true) }
        if ($EnableAutonomy) { $BehaviorSettings+=@(@('unity_bridge_enabled',$true),@('unity_autonomous',$true),@('unity_stationary',$false),@('unity_drag_enabled',$true),@('unity_adaptive_jumps',$true),@('screen_enabled',$true),@('monitor_index',0)) }
        foreach ($Setting in $BehaviorSettings) {
            $Configuration | Add-Member -MemberType NoteProperty -Name $Setting[0] -Value $Setting[1] -Force
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $SettingsPath -Parent) | Out-Null
        $SettingsTemporary=$SettingsPath+'.avatar-update'
        [IO.File]::WriteAllText($SettingsTemporary,($Configuration | ConvertTo-Json -Depth 30),(New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $SettingsTemporary -Destination $SettingsPath -Force
        Write-Host 'Applied requested desktop/web options. Existing persona and capture exclusions preserved.'
    }
    Write-Host "Complete avatar toolset synchronized to $Revision. Backups: $Backup"
} finally {
    if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
}
if ($UpgradeConversation) {
    $Python=Join-Path $Root 'runtime/python/python.exe'
    if (-not (Test-Path -LiteralPath $Python)) { throw 'Installed Yumi Python is missing. Run setup first.' }
    & $Python (Join-Path $Root 'scripts/upgrade_conversation.py') --root $Root
    if ($LASTEXITCODE -ne 0) { throw 'Conversation profile activation failed; the previous model remains selected.' }
}
if ($Build) { & (Join-Path $Root 'scripts/Build-UnityAvatar.ps1') -Root $Root -SkipTests }
