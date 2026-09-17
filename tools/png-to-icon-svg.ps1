# Marabook — vectorise une icône PNG monochrome (fond transparent ou
# clair, tracé sombre) en SVG du jeu embarqué (assets\icons\<nom>.svg),
# en deux temps : masque de pixels via System.Drawing (le Python embarqué
# n'a pas Pillow), puis tools\trace-png-icon.py (17/09/2026).
#   powershell -ExecutionPolicy Bypass -File tools\png-to-icon-svg.ps1 chemin\icone.png [nom] [epsilon]
# Ensuite : regénérer le bloc de chemins de src\View\Icons.cs (voir
# tools\make-icons-cs.ps1) ou coller la ligne à la main.
param(
    [Parameter(Mandatory = $true)][string]$Png,
    [string]$Name = "",
    [double]$Epsilon = 1.2
)
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
if ($Name -eq "") { $Name = [System.IO.Path]::GetFileNameWithoutExtension($Png) }
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png))
$sb = New-Object System.Text.StringBuilder
for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $dark = ($c.A -gt 127) -and (($c.R + $c.G + $c.B) -lt 384)
        [void]$sb.Append($(if ($dark) { "1" } else { "0" }))
    }
    [void]$sb.Append("`n")
}
$bmp.Dispose()
$mask = Join-Path $env:TEMP ("marabook-mask-" + $Name + ".txt")
[System.IO.File]::WriteAllText($mask, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
$svg = Join-Path $root ("assets\icons\" + $Name + ".svg")
& (Join-Path $root "python\python.exe") (Join-Path $root "tools\trace-png-icon.py") $mask $svg $Epsilon
Remove-Item $mask -ErrorAction SilentlyContinue
