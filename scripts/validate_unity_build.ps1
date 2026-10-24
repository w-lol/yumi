#requires -Version 5.1
# Windows process/file orchestration fixture. This does not run Unity or render an avatar.
$ErrorActionPreference='Stop'
$TestRoot=Join-Path ([IO.Path]::GetTempPath()) ('Yumi build fixture spaces '+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
try {
    $Project=Join-Path $TestRoot 'avatar-unity/Fenriru'
    foreach ($Folder in @('Assets','Packages','ProjectSettings')) { New-Item -ItemType Directory -Force -Path (Join-Path $Project $Folder) | Out-Null }
    'm_EditorVersion: 2022.3.22f1' | Set-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectVersion.txt') -Encoding UTF8
    $Fake=Join-Path $TestRoot 'Fake Unity Editor.exe'
    $Code=@'
using System;
using System.IO;
using System.Reflection;
[assembly: AssemblyInformationalVersion("2022.3.22f1")]
public class FakeUnityFixture {
    static string Value(string[] a,string key) { int i=Array.IndexOf(a,key); return i>=0&&i+1<a.Length?a[i+1]:""; }
    public static int Main(string[] args) {
        string project=Value(args,"-projectPath"), log=Value(args,"-logFile");
        string root=Directory.GetParent(Directory.GetParent(project).FullName).FullName;
        File.WriteAllText(log,"Owned orchestration fixture only; no Unity Editor used.");
        System.Threading.Thread.Sleep(150);
        if(Array.IndexOf(args,"-runTests")>=0) {
            bool fail=File.Exists(Path.Combine(root,"fail-tests"));
            File.WriteAllText(Value(args,"-testResults"),fail?"<test-run result='Failed' total='4' passed='3' failed='1'/>":"<test-run result='Passed' total='4' passed='4' failed='0'/>");
            return fail?2:0;
        }
        string exe=Value(args,"-yumiOutput");Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Assembly.GetExecutingAssembly().Location,exe,true);
        if(File.Exists(Path.Combine(root,"fail-build")))return 7;
        if(File.Exists(Path.Combine(root,"incomplete-build")))return 0;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe),"UnityPlayer.dll"),"fixture");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(exe),"YumiAvatar_Data"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe),"yumi-avatar-build.json"),"{\"unity\":\"2022.3.22f1\",\"fixture\":true}");
        return 0;
    }
}
'@
    Add-Type -TypeDefinition $Code -Language CSharp -OutputAssembly $Fake -OutputType ConsoleApplication
    $Builder=Join-Path $PSScriptRoot 'Build-UnityAvatar.ps1'
    $Final=Join-Path $TestRoot 'Builds/YumiAvatar'
    New-Item -ItemType Directory -Force -Path $Final | Out-Null
    $Sentinel=Join-Path $Final 'previous-player.txt'
    'existing working player' | Set-Content -LiteralPath $Sentinel
    foreach ($Failure in @('fail-tests','fail-build','incomplete-build')) {
        New-Item -ItemType File -Path (Join-Path $TestRoot $Failure) | Out-Null
        $Failed=$false
        try { & $Builder -Root $TestRoot -UnityEditor $Fake } catch { $Failed=$true }
        if (-not $Failed) { throw "Failure fixture $Failure was incorrectly accepted." }
        if (-not (Test-Path -LiteralPath $Sentinel)) { throw "Existing player lost after $Failure." }
        Remove-Item -LiteralPath (Join-Path $TestRoot $Failure)
    }
    & $Builder -Root $TestRoot -UnityEditor $Fake
    if (-not (Test-Path -LiteralPath (Join-Path $Final 'YumiAvatar.exe'))) { throw 'Completed fixture build not installed.' }
    if (Test-Path -LiteralPath $Sentinel) { throw 'Staged replacement did not activate.' }
    if (-not @(Get-ChildItem -LiteralPath (Join-Path $TestRoot 'backups') -Filter previous-player.txt -Recurse -File).Count) { throw 'Previous player was not backed up.' }
    Write-Host 'Windows process fixture passed: quoted paths, actual waiting/exit codes, test/build/incomplete-output failures, old-player preservation and staged activation. Not a Unity build test.'
} finally { if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force } }
