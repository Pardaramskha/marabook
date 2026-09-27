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
    (r"System\.Windows\.Input\.new Cursor", "new Cursor"),
    (r"System\.Windows\.Controls\.Primitives\.", ""),
    (r"System\.Windows\.Input\.", ""),
    (r"System\.Windows\.Controls\.", ""),
    (r"System\.Windows\.Threading\.", ""),
    (r"\bImageSource\b", "Avalonia.Media.Imaging.Bitmap"),
    (r"\bRoutedEventHandler\b", "EventHandler<RoutedEventArgs>"),
    (r"RoutedPropertyChangedEventArgs<object>", "SelectionChangedEventArgs"),
    (r"\bSelectedItemChanged \+=", "SelectionChanged +="),
    (r"\bMessageBoxResult\b", "MessageResult"),
    (r"\bMessageBoxButton\b", "MessageButtons"),
    (r"\bMessageBoxImage\b", "MessageIcon"),
    (r"\.IsMouseOver\b", ".IsPointerOver"),
    (r"new FlowDocumentScrollViewer\s*\{\s*Document = ", "new ScrollViewer { Content = "),
    (r"^[ \t]*IsToolBarVisible = false,?[ \t]*\n", ""),
    (r"\bFlowDocument\b", "Control"),
    (r"System\.Windows\.Input\.Cursors\.(\w+)", r"new Cursor(StandardCursorType.\1)"),
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

SUBS += [
    # — Troisième vague (P3, les vues) : visibilité, menus, glisser-déposer,
    #   défilement, formes, animations simples, raccourcis, dialogues.
    (r"\bVisibility\.Visible\b", "true"),
    (r"\bVisibility\.(Collapsed|Hidden)\b", "false"),
    (r"\.Visibility\b", ".IsVisible"),
    (r"\.IsCheckable = true;", ".ToggleType = MenuItemToggleType.CheckBox;"),
    (r"\b(\w*[mM]enu\w*)\.IsOpen = true;", r"\1.Open();"),
    (r"\bSystemParameters\.VerticalScrollBarWidth\b", "18.0"),
    (r"\bSystemParameters\.HorizontalScrollBarHeight\b", "18.0"),
    (r"X1 = ([^,\n]+),\s*Y1 = ([^,\n]+),\s*X2 = ([^,\n]+),\s*Y2 = ([^,\n]+)", r"StartPoint = new Point(\1, \2), EndPoint = new Point(\3, \4)"),
    (r"\bStrokeLineJoin\b", "StrokeJoin"),
    (r"\bShadowDepth = ", "OffsetY = "),
    (r"^[ \t]*Direction = 270,?[ \t]*\n", ""),
    (r"\bPointCollection\b", "Points"),
    (r"\bDoubleCollection\b", "Avalonia.Collections.AvaloniaList<double>"),
    (r"\bnew DataObject\(", "Ui.DataOf("),
    (r"(\w+)\.Data\.GetData\(DataFormats\.FileDrop\) as string\[\]", r"Ui.DroppedPaths(\1.Data)"),
    (r"\.GetDataPresent\(DataFormats\.FileDrop\)", ".Contains(DataFormats.Files)"),
    (r"\.GetDataPresent\(", ".Contains("),
    (r"\.GetData\(", ".Get("),
    (r"\be\.Effects = ", "e.DragEffects = "),
    (r"\be\.Effects\b", "e.DragEffects"),
    (r"^([ \t]+)AllowDrop = true,", r"\1[DragDrop.AllowDropProperty] = true,"),
    (r"\b(\w+)\.AllowDrop = true;", r"DragDrop.SetAllowDrop(\1, true);"),
    (r"\b(\w+)\.Select\(([^,()=]+), ([^,()=]+)\);", r"Ui.Select(\1, \2, \3);"),
    (r"\b(\w+)\.SelectionLength\b(?! =)", r"(\1.SelectionEnd - \1.SelectionStart)"),
    (r"\b(\w+)\.ScrollableHeight\b", r"(\1.Extent.Height - \1.Viewport.Height)"),
    (r"\b(\w+)\.ScrollableWidth\b", r"(\1.Extent.Width - \1.Viewport.Width)"),
    (r"\b(\w+)\.ScrollToRightEnd\(\);", r"\1.Offset = new Vector(\1.Extent.Width, \1.Offset.Y);"),
    (r"\b(\w+)\.ScrollToEnd\(\);", r"\1.Offset = new Vector(\1.Offset.X, \1.Extent.Height);"),
    (r"\b(\w+)\.ScrollToBottom\(\);", r"\1.Offset = new Vector(\1.Offset.X, \1.Extent.Height);"),
    (r"\b(\w+)\.ScrollToTop\(\);", r"\1.Offset = new Vector(\1.Offset.X, 0);"),
    (r"^[ \t]*\w+\.CanContentScroll = (true|false);[ \t]*\n", ""),
    (r"^[ \t]*ScrollViewer\.SetCanContentScroll\([^;]*\);[ \t]*\n", ""),
    (r"\bSelector\b", "SelectingItemsControl"),
    (r"\bToolTipService\.SetInitialShowDelay\(", "ToolTip.SetShowDelay("),
    (r"^[ \t]*ToolTipService\.Set\w+\([^;]*\);[ \t]*\n", ""),
    (r"\bInputBindings\b", "KeyBindings"),
    (r"new KeyBinding\(new DelegateCommand\((\w+)\), key, modifiers\)", r"new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = new DelegateCommand(\1) }"),
    (r"\bModifierKeys\b", "KeyModifiers"),
    (r"\bKeyModifiers\.Windows\b", "KeyModifiers.Meta"),
    (r"Application\.Current\.Shutdown\(\)", "App.Exit()"),
    (r"\bDialogResult = (true|false);", r"Close(\1);"),
    (r"RenderTransformOrigin = new Point\(([^)]*)\)", r"RenderTransformOrigin = new RelativePoint(\1, RelativeUnit.Relative)"),
    (r"(TranslatePoint\((?:[^()]|\([^()]*\))*\))\.(X|Y)\b", r"\1.Value.\2"),
    (r"(?<== )(\w+\.TranslatePoint\((?:[^()]|\([^()]*\))*\));", r"\1 ?? new Point();"),
    (r"\.LayoutTransform = ", ".RenderTransform = "),
    (r"\b(\w+)\.e\.Pointer\.Capture\(this\);", r"e.Pointer.Capture(\1);"),
    (r"\b(\w+)\.e\.Pointer\.Capture\(null\);", r"e.Pointer.Capture(null);"),
    (r"\bDispatcher\.Invoke\(DispatcherPriority\.(\w+), new Action\(", r"Dispatcher.UIThread.Invoke(new Action("),
    (r"\bDispatcher\.CheckAccess\(\)", "Dispatcher.UIThread.CheckAccess()"),
    (r"Application\.Current\.Dispatcher\b", "Dispatcher.UIThread"),
    (r"\bDispatcher\.BeginInvoke\(\(Action\)delegate", "Ui.Post(DispatcherPriority.Normal, (Action)delegate"),
    (r"\.GotKeyboardFocus \+=", ".GotFocus +="),
    (r"\bMouseButton\.(Left|Right|Middle)\b", r"MouseButton.\1"),
    (r"\be\.ChangedButton == MouseButton\.XButton1", "e.GetCurrentPoint(this).Properties.IsXButton1Pressed"),
    (r"\be\.ChangedButton == MouseButton\.XButton2", "e.GetCurrentPoint(this).Properties.IsXButton2Pressed"),
    (r"(?<![\w.])Path\.(Combine|GetFileName|GetFileNameWithoutExtension|GetDirectoryName|GetExtension|GetTempPath|GetInvalidFileNameChars|GetInvalidPathChars|ChangeExtension|GetFullPath|GetTempFileName|DirectorySeparatorChar|IsPathRooted|GetRandomFileName|HasExtension)\b", r"System.IO.Path.\1"),
    # — Quatrième vague : arbres visuel et logique, glisser-déposer, formes, popups.
    (r"System\.Windows\.Media\.Imaging\.", ""),
    (r"System\.Windows\.Documents\.", "Avalonia.Controls.Documents."),
    (r"VisualTreeHelper\.GetParent\(([^()]*)\)", r"((\1) as Visual)?.GetVisualParent()"),
    (r"LogicalTreeHelper\.GetParent\(([^()]*)\)", r"((\1) as Avalonia.LogicalTree.ILogical)?.GetLogicalParent()"),
    (r"\be\.OriginalSource\b", "e.Source"),
    (r"e\.KeyStates & DragDropKeyStates\.ControlKey", "e.KeyModifiers & KeyModifiers.Control"),
    (r"\bDragDropKeyStates\.ControlKey\b", "KeyModifiers.Control"),
    (r"\bDragDropKeyStates\.ShiftKey\b", "KeyModifiers.Shift"),
    (r"\be\.KeyStates\b", "e.KeyModifiers"),
    (r"(\w+)\.X1 = ([^;]+);\s*\1\.Y1 = ([^;]+);\s*\1\.X2 = ([^;]+);\s*\1\.Y2 = ([^;]+);", r"\1.StartPoint = new Point(\2, \3);\n\1.EndPoint = new Point(\4, \5);"),
    (r"StrokeStartLineCap = ([^,\n]+),", r"StrokeLineCap = \1,"),
    (r"^[ \t]*StrokeEndLineCap = [^,\n]+,?[ \t]*\n", ""),
    (r"\bStaysOpen = false,", "IsLightDismissEnabled = true,"),
    (r"^[ \t]*AllowsTransparency = (true|false),?[ \t]*\n", ""),
    (r"^[ \t]*PopupAnimation = [^,\n]+,?[ \t]*\n", ""),
    (r"(?<![\w.])(PointerReleasedEvent|PointerPressedEvent|PointerWheelChangedEvent|PointerMovedEvent|KeyDownEvent|KeyUpEvent|TextInputEvent)\b", r"InputElement.\1"),
    (r"^([ \t]+)AllowDrop = true\b(?!;)", r"\1[DragDrop.AllowDropProperty] = true"),
    (r"^([ \t]+)AllowDrop = true;", r"\1DragDrop.SetAllowDrop(this, true);"),
    (r"\.IBrush\b", ".Brush"),
    (r"\bBitmapCacheOption\.OnLoad\b", "0"),
    (r"\bMessageDialog\.Show\(this, ([^;]*?)\) (==|!=) MessageResult\.", r"await MessageDialog.Show(this, \1) \2 MessageResult."),
    (r"\bSystemParameters\.Minimum(Horizontal|Vertical)DragDistance\b", "4.0"),
    (r"var dispatcher = Dispatcher;", "var dispatcher = Dispatcher.UIThread;"),
    (r"\bDispatcher\.BeginInvoke\(\(Action\)", "Ui.Post(DispatcherPriority.Normal, (Action)"),
    (r"\b(\w+)\.BeginInvoke\(new Action\(", r"\1.Post(new Action("),
    (r"\b(\w+)\.BeginInvoke\(\(Action\)", r"\1.Post((Action)"),
    (r"\b(\w+)\.BeginInvoke\(DispatcherPriority\.(\w+), new Action\(", r"\1.Post(new Action("),
    (r", Dispatcher, ", ", Dispatcher.UIThread, "),
    (r"\be\.Delta\b(?!\.)", "Ui.Wheel(e)"),
    # — Cinquième vague.
    (r"X1 = ([^,\n]+),\s*X2 = ([^,\n]+),\s*Y1 = ([^,\n]+),\s*Y2 = ([^,\n]+)", r"StartPoint = new Point(\1, \3), EndPoint = new Point(\2, \4)"),
    (r"^([ \t]+)\[ScrollViewer\.(\w+)Property\] = ([^;\n]+);[ \t]*$", r"\1ScrollViewer.Set\2(this, \3);"),
    (r"^[ \t]*ToolTipService\.Set\w+\([^;]*\);[^\n]*\n", ""),
    (r"System\.Windows\.Media\.(SolidColorBrush|Color|Colors|Brushes)\b", r"\1"),
    (r"System\.Windows\.Media\.Effects\.", ""),
    (r"Dispatcher\.Invoke\((new Action\([^;]*?\)), TimeSpan\.FromSeconds\(\d+\)\)", r"Dispatcher.UIThread.Invoke(\1)"),
    (r"current is Visual \? \(\(current\) as Visual\)\?\.GetVisualParent\(\)\s*: \(\(current\) as Avalonia\.LogicalTree\.ILogical\)\?\.GetLogicalParent\(\)", "(current as Visual)?.GetVisualParent()"),
    (r"var count = VisualTreeHelper\.GetChildrenCount\((\w+)\);\s*for \(var i = 0; i < count; i\+\+\)\s*\{\s*var child = VisualTreeHelper\.GetChild\(\1, i\);", r"foreach (var child in \1.GetVisualChildren())\n            {"),
    (r"Ui\.Post\(DispatcherPriority\.Normal, (new Action\((?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*\)),\s*DispatcherPriority\.(\w+)\);", r"Ui.Post(DispatcherPriority.\2, \1);"),
    (r"!(\w+)\.IsMouseCaptured\b", r"e.Pointer.Captured != \1"),
    (r"(\w+)\.IsMouseCaptured\b", r"e.Pointer.Captured == \1"),
    (r"\bGeo\.ToWpf\(", "Geo.ToAvalonia("),
    (r"^[ \t]*item\.InputGestureText = [^\n]*\n", ""),
    (r"(if \(TryGesture\(gesture, out key, out modifiers\)\)\n[ \t]+)(KeyBindings\.Add\([^\n]*\);)", r"\1{ item.InputGesture = new KeyGesture(key, modifiers); \2 }"),
    (r"Theme\.Switch\(Application\.Current, AppSettings\.DarkTheme\)", "App.ApplyTheme(AppSettings.DarkTheme)"),
    (r"new LinearGradientBrush\(([^,()]+), ([^,()]+), 90\)", r"Ui.VerticalGradient(\1, \2)"),
    (r"Ink\.Parse\(([^()]*(?:\([^()]*\))?[^()]*)\.ToColor\(\)\)", r"Ink.Parse(\1).ToColor()"),
    (r"\bnew Calendar\b", "new Avalonia.Controls.Calendar"),
    (r"(\w+)\.Select\(([^,()]+), (Math\.Max\([^;]*\))\);", r"Ui.Select(\1, \2, \3);"),
    (r"(\w+)\.GetLineIndexFromCharacterIndex\(([^)]*)\)", r"Ui.LineOf(\1, \2)"),
    (r"(\w+)\.ScrollToLine\(([^;]*)\);", r"Ui.ScrollToLine(\1, \2);"),
    (r"(PointerReleased \+= )delegate(\s*\{\s*if \(e\.InitialPressMouseButton)", r"\1delegate(object sender, PointerReleasedEventArgs e)\2"),
    (r"\)\.IsOpen = true;", ").Open();"),
    (r"^([ \t]+)LayoutTransform = ", r"\1RenderTransform = "),
    (r"new Wpf\.WpfFontEngine\(\)", "new AvaloniaFontEngine()"),
    (r"System\.Diagnostics\.Process\.Start\(path\);", "AppPlatform.OpenWithShell(path);"),
    (r"Wpf\.Printing\.ShowPreview\(this, document, [^;]*;", "PreviewPdf(document, setup, name, new Print.PdfExportOptions(), decor, offset); // l'aperçu = le PDF dans la visionneuse (trois OS)"),
    (r"Wpf\.Printing\.Print\(document, [^;]*;", "PreviewPdf(document, setup, name, new Print.PdfExportOptions(), decor, offset); // imprimer = le PDF dans la visionneuse (trois OS)"),
    (r"new ChangeIconAction\(item, await icon\)", "new ChangeIconAction(item, icon)"),
    (r"if \(await (\w+)\.Title == choice\)", r"if (\1.Title == choice)"),
    (r"DragDrop\.DoDragDrop\((?:this|_tree), ", "DragDrop.DoDragDrop(e, "),
    (r"PointerReleased \+= delegate\s*\{", "PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {"),
    (r"PointerPressed \+= delegate\s*\{", "PointerPressed += delegate(object sender, PointerPressedEventArgs e) {"),
    (r"PointerMoved \+= delegate\s*\{", "PointerMoved += delegate(object sender, PointerEventArgs e) {"),
    # Le Text d'un TextBox est null par défaut chez Avalonia ("" chez WPF).
    (r"(?<![\w.])((?:this\.)?_?\w+(?:\.\w+)*)\.Text\.(Trim|Length|Replace|StartsWith|EndsWith|Split|ToLower|ToLowerInvariant|ToUpper|ToUpperInvariant|Contains|IndexOf|LastIndexOf|Substring|Insert|Remove|Equals|PadLeft|TrimEnd|TrimStart)\b", r"(\1.Text ?? \"\").\2"),
]

# Les abonnements « x.Événement += gestionnaire; » qui changent de forme chez
# Avalonia : AddHandler (glisser-déposer, tunnel) ou une aide d'Ui. Le
# gestionnaire (méthode, lambda ou délégué anonyme sur plusieurs lignes) est
# délimité par équilibrage des accolades et parenthèses jusqu'au « ; ».
SUBSCRIPTIONS = [
    # (événement WPF, gabarit avec {r} le récepteur et {h} le gestionnaire, garde ou None, type d'args à corriger ou None)
    ("Drop", "{r}.AddHandler(DragDrop.DropEvent, {h})", None, None),
    ("DragOver", "{r}.AddHandler(DragDrop.DragOverEvent, {h})", None, None),
    ("DragEnter", "{r}.AddHandler(DragDrop.DragEnterEvent, {h})", None, None),
    ("DragLeave", "{r}.AddHandler(DragDrop.DragLeaveEvent, {h})", None, None),
    ("PreviewMouseWheel", "{r}.AddHandler(PointerWheelChangedEvent, {h}, RoutingStrategies.Tunnel)", None, None),
    ("PreviewMouseDown", "{r}.AddHandler(PointerPressedEvent, {h}, RoutingStrategies.Tunnel)", None, None),
    ("PreviewMouseUp", "{r}.AddHandler(PointerReleasedEvent, {h}, RoutingStrategies.Tunnel)", None, "PointerReleasedEventArgs"),
    ("PreviewKeyDown", "{r}.AddHandler(KeyDownEvent, {h}, RoutingStrategies.Tunnel)", None, None),
    ("PreviewTextInput", "{r}.AddHandler(TextInputEvent, {h}, RoutingStrategies.Tunnel)", None, None),
    ("MouseRightButtonUp", "{r}.PointerReleased += {h}", "if (e.InitialPressMouseButton != MouseButton.Right) return;", "PointerReleasedEventArgs"),
    ("MouseLeftButtonUp", "{r}.PointerReleased += {h}", "if (e.InitialPressMouseButton != MouseButton.Left) return;", "PointerReleasedEventArgs"),
    ("MouseUp", "{r}.PointerReleased += {h}", None, "PointerReleasedEventArgs"),
    ("PointerReleased", "{r}.PointerReleased += {h}", None, "PointerReleasedEventArgs"),
    ("IsVisibleChanged", "Ui.OnVisibilityChanged({r}, {h})", None, None),
    ("SizeChanged", "Ui.OnSizeChanged({r}, {h})", None, None),
]

def handler_end(text, start):
    """L'indice juste après le « ; » qui clôt l'expression du gestionnaire
    (accolades et parenthèses équilibrées, chaînes ignorées)."""
    depth = 0
    i = start
    in_string = None
    while i < len(text):
        c = text[i]
        if in_string:
            if c == "\\\\": i += 2; continue
            if c == in_string: in_string = None
        elif c in "\"'": in_string = c
        elif c in "{(": depth += 1
        elif c in "})": depth -= 1
        elif c == ";" and depth == 0: return i + 1
        i += 1
    return -1

def rewrite_subscriptions(text):
    for event, template, guard, args_type in SUBSCRIPTIONS:
        pattern = re.compile(r"(?<![\w.])(?:(this|\w+(?:\.\w+)*)\.)?" + event + r" \+= ")
        pos = 0
        while True:
            m = pattern.search(text, pos)
            if not m: break
            receiver = m.group(1) or "this"
            if receiver == "this" and m.group(1) is None and template.startswith("{r}."):
                receiver_text = ""
            else:
                receiver_text = receiver
            end = handler_end(text, m.end())
            if end < 0: break
            handler = text[m.end():end - 1]
            if args_type:
                handler = re.sub(r"\bPointerPressedEventArgs\b", args_type, handler, count=1)
            if guard:
                brace = handler.find("{")
                if brace >= 0:
                    line_start = handler.rfind("\n", 0, brace)
                    indent = handler[line_start + 1:brace] if line_start >= 0 else ""
                    handler = handler[:brace + 1] + "\n" + indent + "    " + guard + handler[brace + 1:]
            replacement = template.replace("{r}", receiver_text if receiver_text else "this").replace("{h}", handler) + ";"
            if receiver_text == "" and template.startswith("{r}."):
                replacement = replacement[len("this."):]
            text = text[:m.start()] + replacement + text[end:]
            pos = m.start() + len(replacement)
    return text

def fix(path):
    text = io.open(path, encoding="utf-8-sig").read()
    text = rewrite_subscriptions(text)
    for pattern, repl in SUBS:
        text = re.sub(pattern, repl, text, flags=re.M | re.S)
    if "using Avalonia.VisualTree;" not in text:
        text = text.replace("using Avalonia.Threading;", "using Avalonia.Threading;\nusing Avalonia.VisualTree;", 1)
    if "using Avalonia.Interactivity;" not in text:
        text = text.replace("using Avalonia.Input;", "using Avalonia.Input;\nusing Avalonia.Interactivity;", 1)
    if "using System.Windows.Input;" not in text and re.search(r"\bICommand\b", text):
        text = text.replace("using System;\n", "using System;\nusing System.Windows.Input;\n", 1)
    if "using Avalonia.Controls.Documents;" not in text and re.search(r"\bnew Run\(", text):
        text = text.replace("using Avalonia.Controls;", "using Avalonia.Controls;\nusing Avalonia.Controls.Documents;", 1)
    if "using Avalonia.LogicalTree;" not in text and "GetLogicalParent" in text:
        text = text.replace("using Avalonia.Layout;", "using Avalonia.Layout;\nusing Avalonia.LogicalTree;", 1)
    if "using Marabook.Model;" not in text:
        text = text.replace("\nnamespace Marabook.App", "using Marabook.Model;\n\nnamespace Marabook.App", 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)
    print("corrigé :", os.path.relpath(path, ROOT))

if __name__ == "__main__":
    for name in sys.argv[1:]:
        fix(os.path.join(APP, name))
