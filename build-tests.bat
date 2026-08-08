@echo off
rem Marabook test harness build + run. Same toolchain as build.bat (csc only).
rem The app sources are compiled WITH the tests; /main: designates the harness
rem entry point (TestMain), which disambiguates the two Main methods without
rem having to exclude Program.cs from the recurse.
rem Usage: build-tests.bat            -> compile + run, exit code 1 on failure
rem        build-tests.bat /nobuild   -> run only
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

if "%1"=="/nobuild" goto run

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\verify-dict.ps1"
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /target:exe /out:MarabookTests.exe /optimize+ /codepage:65001 ^
  /main:UniversSale.Tests.TestMain ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:ReachFramework.dll /r:System.Printing.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /recurse:src\*.cs /recurse:tests\*.cs

if errorlevel 1 (
  echo.
  echo *** Test build failed ***
  exit /b 1
)

:run
"%~dp0MarabookTests.exe"
if errorlevel 1 (
  echo.
  echo *** TESTS EN ECHEC ***
  exit /b 1
)
echo Tests OK.
