# Génère tools\app.ico à partir de assets\marabook.png (le visuel de Rémi,
# 12/09/2026 — remplace l'ancien MakeIcon.cs dessiné à la main) :
# déclinaisons 256 → 16 px empaquetées en ICO à entrées PNG. Aucun asset
# externe requis. Rebâtir ensuite l'exe (build.bat) pour qu'il porte la
# nouvelle icône (/win32icon:tools\app.ico).
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$source = Join-Path $PSScriptRoot "..\assets\marabook.png"
if (-not (Test-Path $source)) { throw "marabook.png introuvable dans assets\" }
$src = [System.Drawing.Image]::FromFile($source)

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

$iconPath = Join-Path $PSScriptRoot "app.ico"
[System.IO.File]::WriteAllBytes($iconPath, $out.ToArray())
$out.Dispose()
Write-Output "OK -> $iconPath"
