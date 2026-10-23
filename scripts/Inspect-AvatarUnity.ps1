#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ProjectRoot = 'C:\Users\jason\3D Objects\yumi\avatar-unity',
    [string]$OutputFile = ''
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
if ((Split-Path $ProjectRoot -Leaf) -ne 'avatar-unity') {
    throw 'This inspection tool only accepts the designated duplicate named avatar-unity.'
}
$VersionFile = Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $VersionFile)) { throw "Unity project not found: $ProjectRoot" }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'Assets'))) { throw 'Assets directory missing.' }
if (-not $OutputFile) {
    $OutputFile = Join-Path (Split-Path $ProjectRoot -Parent) 'logs\avatar-unity\project-audit.json'
}
$Version = Get-Content -Encoding UTF8 -Raw -LiteralPath $VersionFile
$ManifestPath = Join-Path $ProjectRoot 'Packages\manifest.json'
$Packages = if (Test-Path $ManifestPath) { (Get-Content -Raw $ManifestPath | ConvertFrom-Json).dependencies } else { @{} }
$Assets = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets') -Recurse -File -Force)
$GuidMap = @{}
foreach ($Meta in @($Assets | Where-Object Extension -eq '.meta')) {
    $Text = Get-Content -Encoding UTF8 -LiteralPath $Meta.FullName -TotalCount 8
    $Match = [regex]::Match(($Text -join "`n"), '(?m)^guid:\s*([0-9a-f]{32})')
    if ($Match.Success) {
        $GuidMap[$Match.Groups[1].Value] = $Meta.FullName.Substring($ProjectRoot.Length+1,$Meta.FullName.Length-$ProjectRoot.Length-6)
    }
}
function Get-References([string]$Text) {
    $Paths = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($Match in [regex]::Matches($Text, 'guid:\s*([0-9a-f]{32})')) {
        $Guid=$Match.Groups[1].Value
        if ($GuidMap.ContainsKey($Guid)) { $null=$Paths.Add($GuidMap[$Guid]) }
    }
    return @($Paths | Sort-Object)
}
$Prefabs = @(); $Materials = @(); $Controllers = @(); $Clips = @(); $Physics = @(); $Expressions = @()
foreach ($File in $Assets) {
    $Relative=$File.FullName.Substring($ProjectRoot.Length+1)
    if ($File.Extension -in @('.prefab','.unity','.controller','.overrideController','.anim','.mat','.asset') -and $File.Length -lt 16MB) {
        $Text=Get-Content -Encoding UTF8 -Raw -LiteralPath $File.FullName
        if ($null -eq $Text) { continue }
        if ($File.Extension -eq '.prefab') {
            $Prefabs+=@{path=$Relative; references=@(Get-References $Text);
                has_animator=($Text -match '(?m)^Animator:'); has_skinned_mesh=($Text -match '(?m)^SkinnedMeshRenderer:');
                descriptor_fields=(@('VisemeBlendShapes','lipSync','baseAnimationLayers','expressionsMenu','expressionParameters','customExpressions') | Where-Object { $Text.Contains($_) })}
        }
        if ($File.Extension -eq '.mat') {
            $Materials+=@{path=$Relative; references=@(Get-References $Text)}
        }
        if ($File.Extension -in @('.controller','.overrideController')) {
            $Controllers+=@{path=$Relative;references=@(Get-References $Text)}
        }
        if ($Text -match 'VRCPhysBone|DynamicBone|MagicaCloth|PhysBone|m_Cloth') { $Physics+=$Relative }
        if ($Text -match 'expressionParameters|expressionsMenu|VRCExpressionsMenu|VRCExpressionParameters') { $Expressions+=$Relative }
    }
    if ($File.Extension -eq '.anim') { $Clips+=$Relative }
}
$Report=[ordered]@{
    schema=1; inspection='read-only static file inventory; not a Unity runtime/build validation';
    unity_version_file=$Version.Trim(); packages=$Packages; asset_file_count=$Assets.Count;
    prefabs=$Prefabs; materials=$Materials; controllers=$Controllers; animation_clips=$Clips;
    shader_paths=@($Assets | Where-Object { $_.Extension -in @('.shader','.shadergraph') } | ForEach-Object { $_.FullName.Substring($ProjectRoot.Length+1) });
    poiyomi_paths=@($Assets | Where-Object { $_.FullName -match '(?i)poiyomi|\.poiyomi|thry' } | ForEach-Object { $_.FullName.Substring($ProjectRoot.Length+1) });
    physics_candidate_files=@($Physics | Select-Object -Unique); expression_candidate_files=@($Expressions | Select-Object -Unique);
    mesh_sources=@($Assets | Where-Object { $_.Extension -in @('.fbx','.blend','.obj') } | ForEach-Object { $_.FullName.Substring($ProjectRoot.Length+1) });
    limitations=@('FBX rigs and facial blendshapes require Unity Editor inspection.', 'Package GUIDs may be unresolved in a static scan.', 'No project assets, settings, materials, controllers or SDK components were modified.', 'No source avatar geometry or textures are included in this report.')
}
New-Item -ItemType Directory -Force -Path (Split-Path ([IO.Path]::GetFullPath($OutputFile)) -Parent) | Out-Null
$Report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
Write-Host "Read-only project audit saved: $OutputFile"
Write-Host 'Send project-audit.json for compatibility review. Do not upload your avatar project to the public Yumi repository.'
