# -*- coding: utf-8 -*-
"""Portage mécanique WPF → Avalonia (P2, 27/09/2026).

Copie un fichier de src/View vers dotnet/Marabook.App en appliquant les
substitutions sûres (usings, énumérations, événements, types) ; ce qui
demande la main (gabarits, presse-papiers, Popup, transformations,
ToolTip dans les initialiseurs) est laissé tel quel et signalé par le
compilateur. Usage :

    python tools/port-avalonia.py src/View/Fichier.cs [Cible.cs]

Le fichier cible est écrit en UTF-8 sans BOM, fins de ligne LF.
"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

USINGS = [
    "using Avalonia;",
    "using Avalonia.Controls;",
    "using Avalonia.Controls.Primitives;",
    "using Avalonia.Controls.Shapes;",
    "using Avalonia.Input;",
    "using Avalonia.Layout;",
    "using Avalonia.Media;",
    "using Avalonia.Threading;",
]

DROP_USINGS = re.compile(r"^using System\.Windows(\.[A-Za-z.]+)?;\s*$", re.M)

SUBS = [
    (r"\bnamespace Marabook\.View\b", "namespace Marabook.App"),
    (r"\bFontWeights\.", "FontWeight."),
    (r"\bFontStyles\.", "FontStyle."),
    (r"\bFontStretches\.", "FontStretch."),
    (r"\bFrameworkElement\b", "Control"),
    (r"\bUIElement\b", "Control"),
    (r"\(Brush\)", "(IBrush)"),
    (r"\bBrush\b(?=\s+[A-Za-z_])", "IBrush"),
    (r"\bBrush\b(?=[>,)\]])", "IBrush"),
    (r"Visibility\s*=\s*Visibility\.Visible", "IsVisible = true"),
    (r"Visibility\s*=\s*Visibility\.Collapsed", "IsVisible = false"),
    (r"Visibility\s*=\s*Visibility\.Hidden", "IsVisible = false"),
    (r"\.Visibility\s*==\s*Visibility\.Visible", ".IsVisible"),
    (r"\.Visibility\s*!=\s*Visibility\.Visible", ".IsVisible == false"),
    (r"\?\s*Visibility\.Visible\s*:\s*Visibility\.(Collapsed|Hidden)", "? true : false"),
    (r"\?\s*Visibility\.(Collapsed|Hidden)\s*:\s*Visibility\.Visible", "? false : true"),
    (r"\bMouseButtonEventArgs\b", "PointerPressedEventArgs"),
    (r"\bMouseEventArgs\b", "PointerEventArgs"),
    (r"\bMouseWheelEventArgs\b", "PointerWheelEventArgs"),
    (r"\bTextCompositionEventArgs\b", "TextInputEventArgs"),
    (r"\bPreviewMouseLeftButtonDown\b", "PointerPressed"),
    (r"\bPreviewMouseLeftButtonUp\b", "PointerReleased"),
    (r"\bPreviewMouseRightButtonDown\b", "PointerPressed"),
    (r"\bPreviewMouseMove\b", "PointerMoved"),
    (r"\bMouseLeftButtonDown\b", "PointerPressed"),
    (r"\bMouseLeftButtonUp\b", "PointerReleased"),
    (r"\bMouseRightButtonDown\b", "PointerPressed"),
    (r"\bMouseMove\b", "PointerMoved"),
    (r"\bMouseEnter\b", "PointerEntered"),
    (r"\bMouseLeave\b", "PointerExited"),
    (r"\bPreviewKeyDown\b", "KeyDown"),
    (r"\bPreviewTextInput\b", "TextInput"),
    (r"\bModifierKeys\b", "KeyModifiers"),
    (r"\bCursors\.Hand\b", "new Cursor(StandardCursorType.Hand)"),
    (r"\bCursors\.Arrow\b", "new Cursor(StandardCursorType.Arrow)"),
    (r"\bCursors\.IBeam\b", "new Cursor(StandardCursorType.Ibeam)"),
    (r"\bCursors\.SizeWE\b", "new Cursor(StandardCursorType.SizeWestEast)"),
    (r"\bCursors\.SizeNS\b", "new Cursor(StandardCursorType.SizeNorthSouth)"),
    (r"\bCursors\.SizeNWSE\b", "new Cursor(StandardCursorType.TopLeftCorner)"),
    (r"\bCursors\.SizeNESW\b", "new Cursor(StandardCursorType.TopRightCorner)"),
    (r"\bCursors\.SizeAll\b", "new Cursor(StandardCursorType.SizeAll)"),
    (r"\bCursors\.Wait\b", "new Cursor(StandardCursorType.Wait)"),
    (r"\bActualWidth\b", "Bounds.Width"),
    (r"\bActualHeight\b", "Bounds.Height"),
    (r"\bprotected override void OnRender\(DrawingContext (\w+)\)", r"public override void Render(DrawingContext \1)"),
    (r"\bSnapsToDevicePixels\s*=\s*true,?\s*", ""),
    (r"\bUseLayoutRounding\s*=\s*true,?\s*", ""),
    (r"^\s*\w+(\.\w+)*\.Freeze\(\);\s*\n", ""),
    (r"\bDispatcherPriority\.Render\b", "DispatcherPriority.Render"),
    (r"\bSystemColors\.HighlightColor\b", "Chrome.Accent.Color"),
    (r"\bTextTrimming\.CharacterEllipsis\b", "TextTrimming.CharacterEllipsis"),
    (r"\bIsHitTestVisible\b", "IsHitTestVisible"),
    (r"new Typeface\(\"Segoe UI\"\)", "new Typeface(FontFamily.Default)"),
]

def port(src, dst):
    text = io.open(src, encoding="utf-8-sig").read()
    text = DROP_USINGS.sub("", text)
    for pattern, repl in SUBS:
        text = re.sub(pattern, repl, text, flags=re.M)
    # les usings Avalonia après le dernier using System.*
    lines = text.split("\n")
    last = -1
    for i, line in enumerate(lines):
        if line.startswith("using System"):
            last = i
    existing = set(l.strip() for l in lines if l.startswith("using "))
    add = [u for u in USINGS if u not in existing]
    lines[last + 1:last + 1] = add
    text = "\n".join(lines)
    text = re.sub(r"\n{3,}", "\n\n", text)
    io.open(dst, "w", encoding="utf-8", newline="\n").write(text)
    print("porté :", os.path.relpath(src, ROOT), "->", os.path.relpath(dst, ROOT), "(%d lignes)" % len(lines))

if __name__ == "__main__":
    src = os.path.join(ROOT, sys.argv[1])
    dst = os.path.join(ROOT, "dotnet", "Marabook.App", sys.argv[2] if len(sys.argv) > 2 else os.path.basename(src))
    port(src, dst)
