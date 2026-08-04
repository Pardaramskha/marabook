@echo off
rem Univers Sale build. Requires only the .NET Framework 4.8 (included in Windows 10/11).
rem No System.Web.Extensions: JSON is handled by the in-tree Json.cs (see benchmark note there).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

"%FW%\csc.exe" /nologo /target:winexe /out:UniversSale.exe /optimize+ /codepage:65001 ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.IO.Compression.dll ^
  /recurse:src\*.cs

if errorlevel 1 (
  echo.
  echo *** Build failed ***
  exit /b 1
)
echo Build OK: UniversSale.exe
