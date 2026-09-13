@echo off
rem Marabook build. Requires only the .NET Framework 4.8 (included in Windows 10/11).
rem No System.Web.Extensions: JSON is handled by the in-tree Json.cs (see benchmark note there).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

rem Les donnees linguistiques embarquees sont verifiees AVANT de compiler :
rem le build echoue si une empreinte SHA256 ne colle pas (APPROVISIONNEMENT.md).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\verify-dict.ps1"
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /target:winexe /out:Marabook.exe /optimize+ /codepage:65001 ^
  /win32icon:tools\app.ico ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:ReachFramework.dll /r:System.Printing.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /recurse:src\*.cs

if errorlevel 1 (
  echo.
  echo *** Build failed ***
  exit /b 1
)
echo Build OK: Marabook.exe
