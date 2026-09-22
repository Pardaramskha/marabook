# release.ps1 — fabrique ce qui part en release GitHub, dans dist\ :
#
#   marabook-windows-portable.zip   l'archive « portable » Windows
#        (déballer n'importe où et lancer Marabook.exe ; c'est aussi elle
#        que l'application télécharge via Aide > Vérifier les mises à jour,
#        et que le hub Stargazer déballe dans apps\)
#   Marabook-Setup-Windows.exe      l'installeur autonome Windows
#        (auto-extracteur qui embarque la même archive, crée les
#        raccourcis, associe les .plot et s'inscrit dans « Applications
#        installées » — voir tools\setup-stub.cs)
#
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1
#
# Aucun SDK requis : tout se compile avec le csc.exe livré avec Windows.
#
# Les noms ne portent PAS la version (elle est dans le tag de la release
# et dans VERSION) : les boutons du README pointent sur
# releases/latest/download/<nom> et téléchargent toujours la dernière.
# VERSION doit être égal à MainWindow.AppVersion (un test C37 le vérifie).

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$version = ([IO.File]::ReadAllText('VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$') {
    throw "VERSION doit être de la forme majeure.mineure.correctif[-suffixe] (lu : « $version »)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force 'dist' | Out-Null
$dist = (Resolve-Path 'dist').Path

# Zip entrée par entrée, noms en « / » (CreateFromDirectory sous
# PowerShell 5 écrit des « \ », illisibles hors Windows).
function Zipper($dossier, $zip) {
    if (Test-Path $zip) { Remove-Item -Force $zip }
    $racine = (Resolve-Path $dossier).Path.TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($fichier in Get-ChildItem $dossier -Recurse -File) {
            $nom = $fichier.FullName.Substring($racine.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $fichier.FullName, $nom, 'Optimal') | Out-Null
        }
    } finally { $archive.Dispose() }
}

function Copier($source, $stage) {
    $cible = Join-Path $stage $source
    New-Item -ItemType Directory -Force (Split-Path -Parent $cible) | Out-Null
    Copy-Item $source $cible
}

# Un dossier entier, sans les caches de bytecode Python.
function CopierDossier($source, $stage) {
    foreach ($fichier in Get-ChildItem $source -Recurse -File) {
        $relatif = $fichier.FullName.Substring((Resolve-Path '.').Path.Length + 1)
        if ($relatif -match '\\__pycache__\\' -or $relatif -like '*.pyc') { continue }
        Copier $relatif $stage
    }
}

# 1. compiler l'application (l'exe doit être fermé : csc ne peut pas
#    l'écraser tant qu'une instance tourne)
if (Get-Process -Name 'Marabook' -ErrorAction SilentlyContinue) {
    throw 'Marabook est ouvert : fermez-le avant de fabriquer la release.'
}
cmd /c .\build.bat
if ($LASTEXITCODE -ne 0 -or -not (Test-Path 'Marabook.exe')) { throw 'Compilation ratée : Marabook.exe manque.' }

# 2. rassembler ce qui part dans l'archive : l'exe, la version, le fichier
#    de portage, la licence, les ressources lues à l'exécution (fond de
#    l'accueil, icône .plot, image du fichier .plot, logo du catalogue,
#    images de succès), et les données linguistiques embarquées
#    (dictionnaires, Grammalecte, Python) — pas les outils, pas les sources,
#    pas les SVG (les icônes sont dessinées par le code).
$stage = Join-Path 'dist' 'stage-win'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
$fichiers = @('Marabook.exe', 'VERSION', 'marabook.stargazer.json', 'LICENSE', 'APPROVISIONNEMENT.md',
              'assets\background.jpg', 'assets\plot.ico', 'assets\plot-file.png', 'assets\marabook.png')
foreach ($f in $fichiers) { Copier $f $stage }
foreach ($d in @('assets\achievements', 'dict', 'grammalecte', 'python')) { CopierDossier $d $stage }

$zipWin = Join-Path $dist 'marabook-windows-portable.zip'
Zipper $stage $zipWin
Remove-Item -Recurse -Force $stage

# 3. l'installeur : le talon compilé avec l'archive embarquée en ressource
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
Write-Host "Publication      : gh release create v$version `"$zipWin`" `"$setup`" --title `"Marabook $version`" --notes-file <notes.md>"
