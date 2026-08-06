@echo off
rem Regenerates tools\app.ico (the application logo). Style maison : csc direct.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
cd /d "%~dp0"

"%FW%\csc.exe" /nologo /out:MakeIcon.exe /r:System.Drawing.dll MakeIcon.cs
if errorlevel 1 exit /b 1
MakeIcon.exe app.ico
del MakeIcon.exe
