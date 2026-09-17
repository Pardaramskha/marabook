@echo off
rem Associates the .plot extension with Marabook for the current user
rem (no admin rights required). Reversible with dissociate-plot.bat.
setlocal
for %%I in ("%~dp0..") do set ROOT=%%~fI

reg add "HKCU\Software\Classes\.plot" /ve /d "Marabook.Project" /f >nul
reg add "HKCU\Software\Classes\Marabook.Project" /ve /d "Projet Marabook" /f >nul
reg add "HKCU\Software\Classes\Marabook.Project\DefaultIcon" /ve /d "%ROOT%\assets\plot.ico,0" /f >nul
reg add "HKCU\Software\Classes\Marabook.Project\shell\open\command" /ve /d "\"%ROOT%\Marabook.exe\" \"%%1\"" /f >nul

rem Refreshes the Explorer icon cache (the .plot icon is assets\plot.ico,
rem built from assets\plot-file.png by tools\make-icon.ps1).
ie4uinit.exe -show 2>nul

echo Association .plot enregistree : double-clic ouvrira Marabook.
