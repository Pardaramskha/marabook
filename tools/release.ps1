# release.ps1 — fabrique ce qui part en release GitHub pour WINDOWS, dans dist\ :
#
#   marabook-windows-portable.zip   l'archive « portable » Windows (tools\publish.ps1
#        -Rid win-x64 : déballer n'importe où et lancer Marabook.exe ; c'est aussi
#        elle que l'application télécharge via Aide > Vérifier les mises à jour,
#        et que le hub Stargazer déballe dans apps\)
#   Marabook-Setup-Windows.exe      l'installeur autonome Windows
#        (auto-extracteur qui embarque la même archive, crée les
#        raccourcis, associe les .plot et s'inscrit dans « Applications
#        installées » — voir tools\setup-stub.cs)
#
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 [-SkipPublish]
#
# Les archives Linux et macOS : tools\publish.ps1 -Rid linux-x64 / osx-x64 /
# osx-arm64, fabriquées par le workflow sur chaque système.
#
# Le talon de l'installeur se compile avec le csc.exe livré avec Windows
# (.NET Framework 4.8) : pas de SDK requis pour lui ; l'application, elle,
# demande le SDK .NET 8 (dotnet publish).
#
# Les noms ne portent PAS la version (elle est dans le tag de la release
# et dans VERSION) : les boutons du README pointent sur
# releases/latest/download/<nom> et téléchargent toujours la dernière.
# VERSION doit être égal à Model/AppInfo.cs (un test C37 le vérifie).
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$version = ([IO.File]::ReadAllText('VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') {
    throw "VERSION doit être de la forme majeure.mineure.correctif[-suffixe] (lu : « $version »)."
}

if (Get-Process -Name 'Marabook' -ErrorAction SilentlyContinue) {
    throw 'Marabook est ouvert : fermez-le avant de fabriquer la release.'
}
New-Item -ItemType Directory -Force 'dist' | Out-Null
$dist = (Resolve-Path 'dist').Path
$zipWin = Join-Path $dist 'marabook-windows-portable.zip'

# 1. l'archive portable (publication autonome win-x64 + données)
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Rid win-x64
    if ($LASTEXITCODE -ne 0) { throw 'tools\publish.ps1 -Rid win-x64 a échoué.' }
}
if (-not (Test-Path $zipWin)) { throw "L'archive $zipWin manque." }

# 2. l'installeur : le talon compilé avec l'archive embarquée en ressource
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$setup = Join-Path $dist 'Marabook-Setup-Windows.exe'
& $csc /nologo /target:winexe "/out:$setup" /optimize+ /codepage:65001 `
    /win32icon:tools\app.ico `
    "/resource:$zipWin,app.zip" `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.IO.Compression.dll `
    tools\setup-stub.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation de l''installeur ratée.' }

Write-Host ''
Write-Host "Archive Windows  : $zipWin ($([math]::Round((Get-Item $zipWin).Length / 1MB, 1)) Mo)"
Write-Host "Installeur       : $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) Mo)"
Write-Host "Publication      : le workflow (fusion dev > main) publie les cinq assets ; à la main : gh release create v$version dist\*"
