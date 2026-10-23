#requires -Version 5.1
$ErrorActionPreference='Stop'
$TestRoot=Join-Path ([IO.Path]::GetTempPath()) ('Yumi-inspection-test-'+[guid]::NewGuid().ToString('N'))
$Project=Join-Path $TestRoot 'avatar-unity'
try {
    foreach ($Name in @('Assets','ProjectSettings','Packages')) { New-Item -ItemType Directory -Force (Join-Path $Project $Name) | Out-Null }
    'm_EditorVersion: 2022.3.22f1' | Set-Content -Encoding UTF8 (Join-Path $Project 'ProjectSettings\ProjectVersion.txt')
    '{"dependencies":{"com.example.local":"1.0.0"}}' | Set-Content -Encoding UTF8 (Join-Path $Project 'Packages\manifest.json')
    'guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.mat.meta')
    "Material:`nm_Shader: {fileID: 4800000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}" | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.mat')
    'guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\Poiyomi.shader.meta')
    'Shader "Fixture/Poiyomi" {}' | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\Poiyomi.shader')
    "Animator:`nSkinnedMeshRenderer:`nVisemeBlendShapes: []`nmaterial: {fileID: 2100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}" | Set-Content -Encoding UTF8 (Join-Path $Project 'Assets\original.prefab')
    $Before=@{}
    Get-ChildItem -LiteralPath $Project -Recurse -File | ForEach-Object { $Before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName).Hash }
    $Output=Join-Path $TestRoot 'project-audit.json'
    & (Join-Path $PSScriptRoot 'Inspect-AvatarUnity.ps1') -ProjectRoot $Project -OutputFile $Output
    $Report=Get-Content -Encoding UTF8 -Raw -LiteralPath $Output | ConvertFrom-Json
    if ($Report.prefabs.Count -ne 1 -or -not $Report.prefabs[0].has_animator) { throw 'Prefab detection failed.' }
    if (@($Report.prefabs[0].references).Count -ne 1 -or $Report.prefabs[0].references[0] -notmatch 'original.mat$') { throw 'GUID reference resolution failed.' }
    if ($Report.materials.Count -ne 1 -or @($Report.shader_paths).Count -ne 1) { throw 'Material/shader inventory failed.' }
    if (@(Get-ChildItem -LiteralPath $Project -Recurse -File).Count -ne $Before.Count) { throw 'Read-only inspection created project files.' }
    foreach ($Path in $Before.Keys) { if ((Get-FileHash -LiteralPath $Path).Hash -ne $Before[$Path]) { throw 'Inspection modified source asset.' } }
    & (Join-Path $PSScriptRoot 'Install-UnityInspector.ps1') -ProjectRoot $Project
    if (-not (Test-Path (Join-Path $Project 'Assets\YumiDesktopInspection\Editor\YumiProjectInspection.cs'))) { throw 'Scoped Editor inspector installation failed.' }
    foreach ($Path in $Before.Keys) { if ((Get-FileHash -LiteralPath $Path).Hash -ne $Before[$Path]) { throw 'Inspector installation modified original fixture assets.' } }
    $Parsed=$null; $Errors=$null
    foreach ($File in @('Inspect-AvatarUnity.ps1','Install-UnityInspector.ps1','setup.ps1','uninstall.ps1')) {
        [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File),[ref]$Parsed,[ref]$Errors) | Out-Null
        if ($Errors.Count) { throw ($Errors | Out-String) }
    }
    Write-Host 'Unity inspection fixture passed: references, shader inventory, source hashes, additive Editor installation and script parsing.'
} finally {
    if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force }
}
