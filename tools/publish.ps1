# publish.ps1 — fabrique l'archive portable de Marabook (Avalonia, .NET 8)
# pour UN système, dans dist\ :
#
#   -Rid win-x64     → dist\marabook-windows-portable.zip   (nom INCHANGÉ : l'Updater
#                      0.43 et les boutons du README pointent releases/latest/download)
#   -Rid linux-x64   → dist\marabook-linux-x64.tar.gz
#   -Rid osx-x64     → dist\marabook-macos-x64.zip     (un bundle Marabook.app)
#   -Rid osx-arm64   → dist\marabook-macos-arm64.zip   (idem)
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Rid win-x64
#   pwsh tools/publish.ps1 -Rid linux-x64          (Linux, macOS : PowerShell 7)
#
# macOS (1.0.3-patch-b) : l'archive ne porte plus un dossier à plat mais un
# BUNDLE Marabook.app — la publication et ses ressources dans Contents/MacOS
# (AppContext.BaseDirectory : rien ne change pour l'application), Info.plist
# (identité, version, le type de document .plot), les icônes .icns tirées de
# assets/marabook.png et assets/plot-file.png (sips + iconutil), le tout SIGNÉ
# AD HOC (codesign --sign -, sans certificat payant) et archivé par ditto, qui
# garde les bits d'exécution et la structure du bundle. Gatekeeper ne
# s'interpose alors qu'UNE fois, au premier lancement (Réglages système >
# Confidentialité et sécurité > Ouvrir quand même), au lieu d'une fois par
# bibliothèque native ; l'Updater (Updater.cs) remplace le bundle entier.
# Fabriqué ailleurs que sur macOS (essai local sous Windows) : sans sips,
# codesign ni ditto, le bundle est tout de même mis en scène, non signé, et
# zippé par .NET — avertissements à l'appui.
#
# Ce qui part : la publication autonome (self-contained, un seul dossier, le
# runtime .NET compris — ni Windows ni Linux n'ont le .NET moderne), avec
# Marabook(.exe) À LA RACINE, plus VERSION, LICENSE, APPROVISIONNEMENT.md,
# marabook.stargazer.json, les ressources lues à l'exécution (assets :
# fond de l'accueil, icône .plot, image du fichier .plot, logo, succès) et
# les données linguistiques (dict, grammalecte) et PYTHON : python\ (la
# distribution embarquée du dépôt) pour Windows ; pour Linux et macOS, une
# distribution portable python-build-standalone (30/10, Rémi : Grammalecte
# ne doit JAMAIS manquer), téléchargée au moment de la publication, vérifiée
# par son empreinte SHA-256 et déballée dans python/ (python/bin/python3,
# que Platform.PythonExecutable préfère au python3 du système). Même
# disposition que la 0.43 : l'Updater 0.43 recopie l'archive par-dessus et
# relance Marabook.exe.
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
$ridWindows = $Rid.StartsWith('win')
$ridMac = $Rid.StartsWith('osx')
function Outil($nom) { return [bool](Get-Command $nom -ErrorAction SilentlyContinue) }

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
# macOS : tout va dans Marabook.app/Contents/MacOS ; ailleurs, à la racine.
$bundle = Join-Path $stage 'Marabook.app'
$payload = if ($ridMac) { Join-Path (Join-Path $bundle 'Contents') 'MacOS' } else { $stage }
New-Item -ItemType Directory -Force $payload | Out-Null
if (-not $SkipBuild) {
    & dotnet publish (Join-Path 'dotnet' 'Marabook.App') -c $Configuration -r $Rid --self-contained -o $payload -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($Rid) a échoué." }
}
$exe = if ($ridWindows) { 'Marabook.exe' } else { 'Marabook' }
if (-not (Test-Path (Join-Path $payload $exe))) { throw "La publication ne contient pas $exe." }
# Le .pdb ne part pas.
Get-ChildItem $payload -Filter '*.pdb' | Remove-Item -Force

# 2. ce qui accompagne l'exécutable
$fichiers = @('VERSION', 'marabook.stargazer.json', 'LICENSE', 'APPROVISIONNEMENT.md',
              (Join-Path 'assets' 'background.jpg'), (Join-Path 'assets' 'plot.ico'),
              (Join-Path 'assets' 'plot-file.png'), (Join-Path 'assets' 'marabook.png'))
foreach ($f in $fichiers) { Copier $f $payload }
$dossiers = @((Join-Path 'assets' 'achievements'), (Join-Path 'assets' 'tutoriel'), 'dict', 'grammalecte')
if ($ridWindows) { $dossiers += 'python' }
foreach ($d in $dossiers) { CopierDossier $d $payload }

# 2 ter. Python embarqué pour Linux et macOS (30/10) : python-build-standalone
# (astral-sh), la même 3.13 que la distribution Windows, en « install_only
# stripped » (~25–35 Mo compressés). Le tarball porte un dossier python/ à sa
# racine, déballé tel quel dans la publication ; l'empreinte est épinglée,
# l'archive gardée dans dist\cache pour ne pas la retélécharger. Un hôte
# Windows ne sait pas déballer les liens symboliques du tarball : l'essai
# local d'une archive Linux ou macOS part alors sans Python (avertissement),
# la vraie fabrication se fait sur le système cible (workflow).
$pythonTag = '20261026'
$pythonBuilds = @{
    'linux-x64' = @{ Name = 'cpython-3.13.18+20261026-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz'; Sha256 = 'df15fe60d3ff55bf9dcf25824b5572756bb8816ff93865a273d3301d7066a8b7' }
    'osx-x64'   = @{ Name = 'cpython-3.13.18+20261026-x86_64-apple-darwin-install_only_stripped.tar.gz';       Sha256 = '7412c19d896c99431b74a18a5643e110449a7f9590fbab179c3e5ab27b9aa9e3' }
    'osx-arm64' = @{ Name = 'cpython-3.13.18+20261026-aarch64-apple-darwin-install_only_stripped.tar.gz';      Sha256 = '8392b6ea51d94391befb2c31ebe75463f045d0b69dfcf5e1bb3234e1f5fa3405' }
}
if (-not $ridWindows) {
    $hoteWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    if ($hoteWindows) {
        Write-Warning "Python embarqué ($Rid) : non déballé depuis un hôte Windows (liens symboliques) — fabriquer sur le système cible."
    } else {
        $build = $pythonBuilds[$Rid]
        $cache = Join-Path $dist 'cache'
        New-Item -ItemType Directory -Force $cache | Out-Null
        $archive = Join-Path $cache $build.Name
        if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $build.Sha256) {
            $url = "https://github.com/astral-sh/python-build-standalone/releases/download/$pythonTag/" + $build.Name
            Write-Host "Python embarqué : téléchargement de $($build.Name)"
            Invoke-WebRequest -Uri $url -OutFile $archive
        }
        $hash = (Get-FileHash $archive -Algorithm SHA256).Hash
        if ($hash -ne $build.Sha256) { throw "Python embarqué : empreinte inattendue pour $($build.Name) ($hash)." }
        & tar -xzf $archive -C $payload
        if ($LASTEXITCODE -ne 0) { throw 'tar (Python embarqué) a échoué.' }
        $python3 = Join-Path $payload (Join-Path 'python' (Join-Path 'bin' 'python3'))
        if (-not (Test-Path $python3)) { throw "Python embarqué : python/bin/python3 manque après déballage." }
        # Les tests et les caches de bytecode ne partent pas (place).
        $lib = Join-Path $payload (Join-Path 'python' 'lib')
        Get-ChildItem $lib -Recurse -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force
        Get-ChildItem $lib -Recurse -Directory | Where-Object { $_.Name -eq 'test' -or $_.Name -eq 'tests' -or $_.Name -eq 'idlelib' -or $_.Name -eq 'tkinter' -or $_.Name -eq 'turtledemo' } | Remove-Item -Recurse -Force
        if (Outil 'chmod') { & chmod -R a+rX (Join-Path $payload 'python') }
        Write-Host ("Python embarqué : {0} ({1} Mo déballés)" -f $build.Name, [math]::Round(((Get-ChildItem (Join-Path $payload 'python') -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1))
    }
}
if (-not $ridWindows -and -not $ridMac) {
    # Le lanceur de bureau et le type MIME des .plot (Linux) sont posés par
    # l'application au premier lancement (FileAssociation) ; le script
    # d'installation ne fait que déballer et rendre l'exécutable exécutable.
    $install = @'
#!/bin/sh
# Marabook : rendre l'exécutable exécutable après déballage, puis lancer.
cd "$(dirname "$0")" && chmod +x ./Marabook && ./Marabook "$@"
'@
    [IO.File]::WriteAllText((Join-Path $payload 'marabook.sh'), $install.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

# 2 bis. le bundle macOS : icônes, Info.plist, signature ad hoc
function Icns($png, $icns) {
    if (-not (Outil 'sips') -or -not (Outil 'iconutil')) { Write-Warning "sips/iconutil absents : pas d'icône $icns (fabriquer sur macOS)." ; return }
    $set = Join-Path $dist 'icone.iconset'
    if (Test-Path $set) { Remove-Item -Recurse -Force $set }
    New-Item -ItemType Directory -Force $set | Out-Null
    foreach ($taille in 16, 32, 128, 256, 512) {
        $double = $taille * 2
        & sips -z $taille $taille $png --out (Join-Path $set "icon_${taille}x${taille}.png") | Out-Null
        & sips -z $double $double $png --out (Join-Path $set "icon_${taille}x${taille}@2x.png") | Out-Null
    }
    & iconutil -c icns $set -o $icns
    if ($LASTEXITCODE -ne 0) { throw "iconutil a échoué pour $icns." }
    Remove-Item -Recurse -Force $set
}
if ($ridMac) {
    $contents = Join-Path $bundle 'Contents'
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Force $resources | Out-Null
    Icns (Join-Path 'assets' 'marabook.png') (Join-Path $resources 'Marabook.icns')
    Icns (Join-Path 'assets' 'plot-file.png') (Join-Path $resources 'plot.icns')
    # CFBundleShortVersionString veut des nombres (1.0.3) ; la version
    # complète, suffixe compris (1.0.3-patch-b), va dans CFBundleVersion.
    $courte = ($version -split '-')[0]
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>fr</string>
  <key>CFBundleName</key>
  <string>Marabook</string>
  <key>CFBundleDisplayName</key>
  <string>Marabook</string>
  <key>CFBundleIdentifier</key>
  <string>com.pardaramskha.marabook</string>
  <key>CFBundleExecutable</key>
  <string>Marabook</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleSignature</key>
  <string>????</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleIconFile</key>
  <string>Marabook.icns</string>
  <key>CFBundleShortVersionString</key>
  <string>$courte</string>
  <key>CFBundleVersion</key>
  <string>$version</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.productivity</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>Marabook, logiciel libre sous licence GNU GPL v3 ou ultérieure</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>
      <string>Projet Marabook</string>
      <key>CFBundleTypeExtensions</key>
      <array>
        <string>plot</string>
      </array>
      <key>CFBundleTypeIconFile</key>
      <string>plot.icns</string>
      <key>CFBundleTypeRole</key>
      <string>Editor</string>
      <key>LSHandlerRank</key>
      <string>Owner</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>com.pardaramskha.marabook.plot</string>
        <string>io.pardaramskha.marabook.plot</string>
      </array>
    </dict>
  </array>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key>
      <string>com.pardaramskha.marabook.plot</string>
      <key>UTTypeDescription</key>
      <string>Projet Marabook</string>
      <key>UTTypeIconFile</key>
      <string>plot.icns</string>
      <key>UTTypeConformsTo</key>
      <array>
        <string>public.data</string>
      </array>
      <key>UTTypeTagSpecification</key>
      <dict>
        <key>public.filename-extension</key>
        <array>
          <string>plot</string>
        </array>
      </dict>
    </dict>
    <!-- L'identifiant de la 0.42 alpha (12/10) : un Mac qui l'a connu range encore
         les .plot sous io.… — on le réclame aussi, même icône, même rôle, sinon
         les fichiers restent sans icône. -->
    <dict>
      <key>UTTypeIdentifier</key>
      <string>io.pardaramskha.marabook.plot</string>
      <key>UTTypeDescription</key>
      <string>Projet Marabook (ancien identifiant)</string>
      <key>UTTypeIconFile</key>
      <string>plot.icns</string>
      <key>UTTypeConformsTo</key>
      <array>
        <string>public.data</string>
      </array>
      <key>UTTypeTagSpecification</key>
      <dict>
        <key>public.filename-extension</key>
        <array>
          <string>plot</string>
        </array>
      </dict>
    </dict>
  </array>
</dict>
</plist>
"@
    [IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    if (Outil 'chmod') { & chmod +x (Join-Path $payload 'Marabook') }
    if (Outil 'codesign') {
        # Ad hoc (« - ») : pas d'identité, mais un sceau cohérent sur le
        # bundle et chaque binaire imbriqué — Gatekeeper juge l'ensemble
        # une fois, et l'Updater peut recopier le bundle sans le casser.
        # Les binaires de Python embarqué (python3.13, .dylib, .so) ne sont
        # pas à un emplacement que --deep connaît : signés un à un d'abord.
        $pythonDir = Join-Path $payload 'python'
        if (Test-Path $pythonDir) {
            $binaires = Get-ChildItem $pythonDir -Recurse -File | Where-Object {
                $_.Extension -eq '.dylib' -or $_.Extension -eq '.so' -or $_.DirectoryName.EndsWith([IO.Path]::DirectorySeparatorChar + 'bin')
            }
            foreach ($b in $binaires) {
                if ($b.LinkType) { continue } # les liens (python3 → python3.13) suivent leur cible
                & codesign --force --sign - $b.FullName 2>$null
            }
        }
        & codesign --force --deep --sign - $bundle
        if ($LASTEXITCODE -ne 0) { throw 'codesign (ad hoc) a échoué.' }
    } else { Write-Warning 'codesign absent : bundle non signé (fabriquer sur macOS).' }
}

# 3. l'archive
$out = Join-Path $dist $asset
if (Test-Path $out) { Remove-Item -Force $out }
if ($asset.EndsWith('.tar.gz')) {
    # tar conserve le bit d'exécution (chmod +x avant), ce que le zip ne fait pas.
    if (-not $ridWindows -and (Outil 'chmod')) {
        & chmod +x (Join-Path $stage 'Marabook') (Join-Path $stage 'marabook.sh')
    }
    & tar -czf $out -C $stage .
    if ($LASTEXITCODE -ne 0) { throw 'tar a échoué.' }
} elseif ($ridMac -and (Outil 'ditto')) {
    # ditto : le zip « à la Mac » (Marabook.app à la racine, bits d'exécution
    # et signature conservés) ; c'est aussi ditto qui le déballe (Updater).
    & ditto -c -k --keepParent $bundle $out
    if ($LASTEXITCODE -ne 0) { throw 'ditto a échoué.' }
} else {
    if ($ridMac) { Write-Warning 'ditto absent : zip par .NET (bits d''exécution non garantis).' }
    Zipper $stage $out
}
Remove-Item -Recurse -Force $stage

Write-Host ("Archive {0,-11}: {1} ({2} Mo)" -f $Rid, $out, [math]::Round((Get-Item $out).Length / 1MB, 1))
