@echo off
rem Associates the .plot extension with Univers Sale for the current user
rem (no admin rights required). Reversible with dissociate-plot.bat.
setlocal
for %%I in ("%~dp0..") do set ROOT=%%~fI

reg add "HKCU\Software\Classes\.plot" /ve /d "UniversSale.Project" /f >nul
reg add "HKCU\Software\Classes\UniversSale.Project" /ve /d "Projet Univers Sale" /f >nul
reg add "HKCU\Software\Classes\UniversSale.Project\shell\open\command" /ve /d "\"%ROOT%\UniversSale.exe\" \"%%1\"" /f >nul

rem Refreshes the Explorer icon cache.
ie4uinit.exe -show 2>nul

echo Association .plot enregistree : double-clic ouvrira Univers Sale.
