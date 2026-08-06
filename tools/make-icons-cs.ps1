# Regénère src/View/Icons.cs depuis assets/icons/*.svg.
# Usage : powershell -File tools/make-icons-cs.ps1 (depuis la racine du projet)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("using System;")
[void]$sb.AppendLine("using System.Collections.Generic;")
[void]$sb.AppendLine("using System.Windows;")
[void]$sb.AppendLine("using System.Windows.Media;")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("namespace UniversSale.View")
[void]$sb.AppendLine("{")
[void]$sb.AppendLine("    /// <summary>The embedded icon set (Phosphor, bold weight): SVG path data")
[void]$sb.AppendLine("    /// compiled into the exe by tools/make-icons-cs.ps1 from assets/icons —")
[void]$sb.AppendLine("    /// zero runtime dependency, tintable at will (256x256 viewbox).</summary>")
[void]$sb.AppendLine("    public static class Icons")
[void]$sb.AppendLine("    {")
[void]$sb.AppendLine("        private static readonly Dictionary<string, string> _paths = new Dictionary<string, string>")
[void]$sb.AppendLine("        {")
Get-ChildItem "assets\icons\*.svg" | Sort-Object Name | ForEach-Object {
  $name = $_.BaseName
  $svg = Get-Content $_.FullName -Raw
  $ds = [regex]::Matches($svg, ' d="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
  $data = ($ds -join " ")
  [void]$sb.AppendLine('            { "' + $name + '", "' + $data + '" },')
}
[void]$sb.AppendLine("        };")
# ... coller ici le bloc runtime (Get/Make) inchangé de la version courante.
Write-Output "Voir la version en place de src/View/Icons.cs pour le bloc runtime."
