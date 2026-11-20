' Hidden launcher for the desktop shortcut: runs run-from-source.ps1 with no console window.
' Keep this file ASCII-only (wscript reads .vbs with the system ANSI code page).
Option Explicit
Dim fso, shell, here, script, command
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)
script = fso.BuildPath(here, "run-from-source.ps1")
command = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & script & """"
shell.Run command, 0, False
