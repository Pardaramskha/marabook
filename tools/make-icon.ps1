# Génère une icône ICO (déclinaisons 256 → 16 px, entrées PNG) à partir
# d'un PNG. Sans paramètre : tools\app.ico depuis assets\marabook.png (le
# visuel de Rémi, 12/09/2026 — remplace l'ancien MakeIcon.cs dessiné à la
# main) ; rebâtir ensuite l'exe (build.bat) pour qu'il porte la nouvelle
# icône (/win32icon:tools\app.ico). Aucun asset externe requis.
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Icône des fichiers .plot (17/09/2026, assets\plot.ico, pointée par
# associate-plot.bat via la clé DefaultIcon) :
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 assets\plot-file.png assets\plot.ico
param(
    [string]$Source = "",
    [string]$Output = ""
)
Add-Type -AssemblyName System.Drawing

if ($Source -eq "") { $Source = Join-Path $PSScriptRoot "..\assets\marabook.png" }
if ($Output -eq "") { $Output = Join-Path $PSScriptRoot "app.ico" }
if (-not (Test-Path $Source)) { throw "Source introuvable : $Source" }
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$pngs = @()
foreach ($s in $sizes) {
    $b = New-Object System.Drawing.Bitmap($s, $s)
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.Clear([System.Drawing.Color]::Transparent)
    $gg.DrawImage($src, 0, 0, $s, $s)
    $gg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $ms.Dispose()
    $b.Dispose()
}
$src.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -eq 256) { 0 } else { $s }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($e in $pngs) { $bw.Write($e[1]) }
$bw.Flush()

$iconPath = $Output
if (-not [System.IO.Path]::IsPathRooted($iconPath)) { $iconPath = Join-Path (Get-Location) $iconPath }
[System.IO.File]::WriteAllBytes($iconPath, $out.ToArray())
$out.Dispose()
Write-Output "OK -> $iconPath"
