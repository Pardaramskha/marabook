@echo off
rem Marabook.Core (portage Avalonia, lot P0) : la preuve que le coeur compile
rem SANS System.Windows. Modele, Persistence, Correction, History, Settings,
rem Print, Exchange et Json.cs, en bibliotheque, sans aucune reference WPF
rem (ni PresentationCore, ni WindowsBase, ni System.Xaml). Une regression
rem (un "using System.Windows" qui revient dans le coeur) casse ce build.
rem La DLL produite ne sert a rien d'autre : l'app et les tests compilent
rem toujours les sources ensemble (build.bat, build-tests.bat).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

"%FW%\csc.exe" /nologo /target:library /out:Marabook.Core.dll /optimize+ /codepage:65001 ^
  /nostdlib+ /noconfig ^
  /r:"%FW%\mscorlib.dll" /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.Xml.dll" /r:"%FW%\System.Xml.Linq.dll" ^
  /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
  src\Json.cs ^
  /recurse:src\Model\*.cs /recurse:src\Persistence\*.cs /recurse:src\Correction\*.cs ^
  /recurse:src\History\*.cs /recurse:src\Settings\*.cs /recurse:src\Print\*.cs ^
  /recurse:src\Exchange\*.cs

if errorlevel 1 (
  echo.
  echo *** Marabook.Core : le coeur ne compile pas sans WPF ***
  exit /b 1
)
echo Core OK: Marabook.Core.dll (sans System.Windows)
