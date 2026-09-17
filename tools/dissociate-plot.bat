@echo off
rem Removes the .plot association created by associate-plot.bat.
setlocal

reg delete "HKCU\Software\Classes\.plot" /f >nul 2>nul
reg delete "HKCU\Software\Classes\Marabook.Project" /f >nul 2>nul

echo Association .plot supprimee.
