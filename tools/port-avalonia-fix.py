# -*- coding: utf-8 -*-
"""Deuxième passe du portage WPF → Avalonia (P2) : les substitutions qui
supposent un contexte connu (gestionnaires d'événements avec « e »,
ScrollViewer, DrawingContext) — appliquées aux fichiers de la vue composée
déjà copiés dans dotnet/Marabook.App. Usage : python tools/port-avalonia-fix.py Fichier.cs…"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "dotnet", "Marabook.App")

SUBS = [
    (r"System\.Windows\.Controls\.Primitives\.Popup", "Popup"),
    (r"System\.Windows\.Shapes\.(Line|Ellipse)\b", r"\1"),
    (r"System\.Windows\.Shapes\.Rectangle", "Rectangle"),
    (r"System\.Windows\.Shapes\.Path", "Path"),
    (r"System\.Windows\.Controls\.Border", "Border"),
    (r"System\.Windows\.Controls\.Primitives\.PlacementMode\.MousePoint", "PlacementMode.Pointer"),
    (r"System\.Windows\.Controls\.Primitives\.ScrollBar", "ScrollBar"),
    (r"\bdc\.DrawRoundedRectangle\(", "dc.DrawRectangle("),
    (r"\bKeyboard\.Modifiers\b", "e.KeyModifiers"),
    (r"e\.LeftButton != MouseButtonState\.Pressed", "!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed"),
    (r"\bCaptureMouse\(\);", "e.Pointer.Capture(this);"),
    (r"\bReleaseMouseCapture\(\);", "e.Pointer.Capture(null);"),
    (r"\(e\.Key == Key\.System \? e\.SystemKey : e\.Key\)", "e.Key"),
    (r"e\.Key == Key\.System \? e\.SystemKey : e\.Key", "e.Key"),
    (r"\bScrollToVerticalOffset\((.*?)\);", r"Offset = new Vector(Offset.X, \1);"),
    (r"\bScrollToHorizontalOffset\((.*?)\);", r"Offset = new Vector(\1, Offset.Y);"),
    (r"\bVerticalOffset\b", "Offset.Y"),
    (r"\bHorizontalOffset\b", "Offset.X"),
    (r"\bViewportHeight\b", "Viewport.Height"),
    (r"\bViewportWidth\b", "Viewport.Width"),
    (r"\.Visibility = ", ".IsVisible = "),
    (r"^(\s+)Visibility = ", r"\1IsVisible = "),
    (r"\bWpf\.WpfFontEngine\b", "AvaloniaFontEngine"),
    (r"FlowConverter\.ParseWeight\(([^)]*)\)", r"(FontWeight)TextWeights.Parse(\1)"),
    (r"FlowConverter\.ParseColor\(([^)]*)\)", r"Ink.Parse(\1).ToColor()"),
    (r"(\bnew FormattedText\((?:[^()]|\([^()]*\))*?), 1\.0\)", r"\1)"),
    (r"\bFocusVisualStyle = null;\s*", ""),
    (r"\bLogicalTreeHelper\.GetParent\(source\)", "null"),
    (r"source is System\.Windows\.Media\.Visual\s*\?\s*System\.Windows\.Media\.VisualTreeHelper\.GetParent\(source\)", "source is Visual ? ((Visual)source).GetVisualParent()"),
    (r"\bDependencyObject\b", "Visual"),
    (r"e\.OriginalSource as Visual", "e.Source as Visual"),
    (r"e\.OriginalSource as PageElement", "e.Source as PageElement"),
    (r"\bzone\.Inflate\(2, 2\);", "zone = zone.Inflate(2);"),
    # — Les services d'Ui.cs (répartiteur, fenêtre propriétaire, presse-papiers).
    (r"^([ \t]+)ToolTip = ", r"\1[ToolTip.TipProperty] = "),
    (r"\b(\w+)\.ToolTip = ((?:\"(?:[^\"\\]|\\.)*\"|[^;\"])*);", r"ToolTip.SetTip(\1, \2);"),
    (r"Window\.GetWindow\(this\)", "Ui.OwnerOf(this)"),
    (r"Dispatcher\.BeginInvoke\(DispatcherPriority\.(\w+),\s*", r"Ui.Post(DispatcherPriority.\1, "),
    (r"Dispatcher\.BeginInvoke\(new Action\(", "Ui.Post(DispatcherPriority.Normal, new Action("),
    (r"\bMessageBoxButton\.", "MessageButtons."),
    (r"\bMessageBoxImage\.", "MessageIcon."),
    (r"ResizeMode = ResizeMode\.NoResize;", "CanResize = false;"),
    (r"ResizeMode = ResizeMode\.CanResize;", "CanResize = true;"),
    (r"\.IsKeyboardFocused\b", ".IsFocused"),
    (r"\.LostKeyboardFocus \+=", ".LostFocus +="),
    (r"\.Visibility == Visibility\.Collapsed", ".IsVisible == false"),
    (r"\.Visibility != Visibility\.Collapsed", ".IsVisible"),
    (r"\.Visibility == Visibility\.Visible", ".IsVisible"),
    (r"\bInternalChildren\b", "Children"),
    (r"^[ \t]*\w+\.SetResourceReference\(StyleProperty, [^)]*\);[ \t]*\n", ""),
    (r"\.LayoutTransform = new ScaleTransform\(-1, 1\);", ".RenderTransform = new ScaleTransform(-1, 1);"),
    (r"Clipboard\.SetText\(((?:\"(?:[^\"\\]|\\.)*\"|[^;\"])*)\);", r"Ui.SetClipboardText(this, \1);"),
    (r"as System\.Windows\.Media\.Imaging\.BitmapSource", "as Avalonia.Media.Imaging.Bitmap"),
    (r"\b(\w*[mM]enu)\.IsOpen = true;", r"\1.Open();"),
    (r"\bMainWindow\.AppName\b", "AppInfo.Name"),
    (r"([{,]\s*)ToolTip = ", r"\1[ToolTip.TipProperty] = "),
    (r"\b(\w+)\.ToolTip\b(?!\s*=)", r"ToolTip.GetTip(\1)"),
    (r"\bbitmap\.PixelWidth\b", "bitmap.PixelSize.Width"),
    (r"\bbitmap\.PixelHeight\b", "bitmap.PixelSize.Height"),
    (r"(\w+)\.Offset = new Vector\(Offset\.X, ", r"\1.Offset = new Vector(\1.Offset.X, "),
    (r"(\w+)\.Offset = new Vector\(([^,;]+), Offset\.Y\)", r"\1.Offset = new Vector(\2, \1.Offset.Y)"),
    (r"System\.Windows\.Threading\.DispatcherTimer", "DispatcherTimer"),
    (r"System\.Windows\.Controls\.Primitives\.PlacementMode\.", "PlacementMode."),
    (r"\bIsCheckable = true,", "ToggleType = MenuItemToggleType.CheckBox,"),
    (r"^([ \t]+)(Vertical|Horizontal)ScrollBarVisibility = ", r"\1[ScrollViewer.\2ScrollBarVisibilityProperty] = "),
]

def fix(path):
    text = io.open(path, encoding="utf-8-sig").read()
    for pattern, repl in SUBS:
        text = re.sub(pattern, repl, text, flags=re.M | re.S)
    if "using Avalonia.VisualTree;" not in text:
        text = text.replace("using Avalonia.Threading;", "using Avalonia.Threading;\nusing Avalonia.VisualTree;", 1)
    if "using Avalonia.Interactivity;" not in text:
        text = text.replace("using Avalonia.Input;", "using Avalonia.Input;\nusing Avalonia.Interactivity;", 1)
    if "using Avalonia.Controls.Documents;" not in text and re.search(r"\bnew Run\(", text):
        text = text.replace("using Avalonia.Controls;", "using Avalonia.Controls;\nusing Avalonia.Controls.Documents;", 1)
    if "using Marabook.Model;" not in text:
        text = text.replace("\nnamespace Marabook.App", "using Marabook.Model;\n\nnamespace Marabook.App", 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print("corrigé :", os.path.relpath(path, ROOT))

if __name__ == "__main__":
    for name in sys.argv[1:]:
        fix(os.path.join(APP, name))
