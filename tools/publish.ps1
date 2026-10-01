# publish.ps1 — fabrique l'archive portable de Marabook (Avalonia, .NET 8)
# pour UN système, dans dist\ :
#
#   -Rid win-x64     → dist\marabook-windows-portable.zip   (nom INCHANGÉ : l'Updater
#                      0.43 et les boutons du README pointent releases/latest/download)
#   -Rid linux-x64   → dist\marabook-linux-x64.tar.gz
#   -Rid osx-x64     → dist\marabook-macos-x64.zip
#   -Rid osx-arm64   → dist\marabook-macos-arm64.zip
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Rid win-x64
#   pwsh tools/publish.ps1 -Rid linux-x64          (Linux, macOS : PowerShell 7)
#
# Ce qui part : la publication autonome (self-contained, un seul dossier, le
# runtime .NET compris — ni Windows ni Linux n'ont le .NET moderne), avec
# Marabook(.exe) À LA RACINE, plus VERSION, LICENSE, APPROVISIONNEMENT.md,
# marabook.stargazer.json, les ressources lues à l'exécution (assets :
# fond de l'accueil, icône .plot, image du fichier .plot, logo, succès) et
# les données linguistiques (dict, grammalecte) ; python\ (la distribution
# embarquée) n'existe que pour Windows — Linux et macOS emploient le python3
# du système (Platform.PythonExecutable). Même disposition que la 0.43 :
# l'Updater 0.43 recopie l'archive par-dessus et relance Marabook.exe.
#
# Les noms ne portent PAS la version (elle est dans le tag et dans VERSION).
param(
    [Parameter(Mandatory = $true)][string]$Rid,
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$version = ([IO.File]::ReadAllText('VERSION')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') {
    throw "VERSION doit être de la forme majeure.mineure.correctif[-suffixe] (lu : « $version »)."
}
$assetNames = @{
    'win-x64'   = 'marabook-windows-portable.zip'
    'linux-x64' = 'marabook-linux-x64.tar.gz'
    'osx-x64'   = 'marabook-macos-x64.zip'
    'osx-arm64' = 'marabook-macos-arm64.zip'
}
if (-not $assetNames.ContainsKey($Rid)) { throw "RID inconnu : $Rid (win-x64, linux-x64, osx-x64, osx-arm64)." }
$asset = $assetNames[$Rid]
$isWindows = $Rid.StartsWith('win')

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force 'dist' | Out-Null
$dist = (Resolve-Path 'dist').Path

# Zip entrée par entrée, noms en « / » (CreateFromDirectory sous
# PowerShell 5 écrit des « \ », illisibles hors Windows).
function Zipper($dossier, $zip) {
    if (Test-Path $zip) { Remove-Item -Force $zip }
    $racine = (Resolve-Path $dossier).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
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
    $base = (Resolve-Path '.').Path.Length + 1
    foreach ($fichier in Get-ChildItem $source -Recurse -File) {
        $relatif = $fichier.FullName.Substring($base)
        if ($relatif -match '[\\/]__pycache__[\\/]' -or $relatif -like '*.pyc') { continue }
        Copier $relatif $stage
    }
}

# 1. la publication autonome, dans le dossier de mise en scène
$stage = Join-Path $dist ("stage-" + $Rid)
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
if (-not $SkipBuild) {
    & dotnet publish (Join-Path 'dotnet' 'Marabook.App') -c $Configuration -r $Rid --self-contained -o $stage -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($Rid) a échoué." }
}
$exe = if ($isWindows) { 'Marabook.exe' } else { 'Marabook' }
if (-not (Test-Path (Join-Path $stage $exe))) { throw "La publication ne contient pas $exe." }
# Le .pdb ne part pas.
Get-ChildItem $stage -Filter '*.pdb' | Remove-Item -Force

# 2. ce qui accompagne l'exécutable
$fichiers = @('VERSION', 'marabook.stargazer.json', 'LICENSE', 'APPROVISIONNEMENT.md',
              (Join-Path 'assets' 'background.jpg'), (Join-Path 'assets' 'plot.ico'),
              (Join-Path 'assets' 'plot-file.png'), (Join-Path 'assets' 'marabook.png'))
foreach ($f in $fichiers) { Copier $f $stage }
$dossiers = @((Join-Path 'assets' 'achievements'), 'dict', 'grammalecte')
if ($isWindows) { $dossiers += 'python' }
foreach ($d in $dossiers) { CopierDossier $d $stage }
if (-not $isWindows) {
    # Le lanceur de bureau et le type MIME des .plot (Linux) sont posés par
    # l'application au premier lancement (FileAssociation) ; le script
    # d'installation ne fait que déballer et rendre l'exécutable exécutable.
    $install = @'
#!/bin/sh
# Marabook : rendre l'exécutable exécutable après déballage, puis lancer.
cd "$(dirname "$0")" && chmod +x ./Marabook && ./Marabook "$@"
'@
    [IO.File]::WriteAllText((Join-Path $stage 'marabook.sh'), $install.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

# 3. l'archive
$out = Join-Path $dist $asset
if (Test-Path $out) { Remove-Item -Force $out }
if ($asset.EndsWith('.tar.gz')) {
    # tar conserve le bit d'exécution (chmod +x avant), ce que le zip ne fait pas.
    if (-not $isWindows -and (Get-Command chmod -ErrorAction SilentlyContinue)) {
        & chmod +x (Join-Path $stage 'Marabook') (Join-Path $stage 'marabook.sh')
    }
    & tar -czf $out -C $stage .
    if ($LASTEXITCODE -ne 0) { throw 'tar a échoué.' }
} else {
    Zipper $stage $out
}
Remove-Item -Recurse -Force $stage

Write-Host ("Archive {0,-11}: {1} ({2} Mo)" -f $Rid, $out, [math]::Round((Get-Item $out).Length / 1MB, 1))
