Option Explicit
Dim fso, root, shell, python, script, pointer, handle, relative
Set fso = CreateObject("Scripting.FileSystemObject")
root = fso.GetParentFolderName(WScript.ScriptFullName)
If LCase(fso.GetFileName(root)) = "scripts" Then root = fso.GetParentFolderName(root)
pointer = fso.BuildPath(root, "config\python-runtime.txt")
If Not fso.FileExists(pointer) Then
    MsgBox "Run scripts\setup.ps1 first.", 48, "Yumi setup required"
    WScript.Quit 1
End If
Set handle = fso.OpenTextFile(pointer, 1)
relative = Trim(handle.ReadAll)
handle.Close
If InStr(relative, "..") > 0 Or Left(relative, 8) <> "runtime\" Then
    MsgBox "Invalid runtime pointer. Rerun setup.", 48, "Yumi"
    WScript.Quit 1
End If
python = fso.BuildPath(root, relative & "\tools\pythonw.exe")
script = fso.BuildPath(root, "scripts\run.py")
If Not fso.FileExists(python) Then
    MsgBox "Run scripts\setup.ps1 first.", 48, "Yumi setup required"
    WScript.Quit 1
End If
Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = root
shell.Run Chr(34) & python & Chr(34) & " -I " & Chr(34) & script & Chr(34) & " --root " & Chr(34) & root & Chr(34), 0, False
