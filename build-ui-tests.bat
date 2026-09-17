@echo off
rem Sondes UI de Marabook (lot 0.3 batch 26) : vraies fenetres WPF hors ecran,
rem a lancer A LA MAIN sur un poste avec bureau (pas dans le harnais console).
rem Memes sources que build-tests.bat ; /main: designe la sonde a executer.
rem settings.json est sauvegarde/restaure par la sonde elle-meme (batch 11).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\verify-dict.ps1"
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /target:exe /out:MarabookUiTests.exe /codepage:65001 ^
  /main:Marabook.Tests.Ui.A1Probe ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:ReachFramework.dll /r:System.Printing.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /recurse:src\*.cs /recurse:tests\*.cs

if errorlevel 1 (
  echo.
  echo *** UI test build failed ***
  exit /b 1
)

"%~dp0MarabookUiTests.exe"
if errorlevel 1 (
  echo.
  echo *** SONDES UI EN ECHEC ***
  exit /b 1
)
echo Sondes UI OK.
