using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The rich text editor: format bar, RichTextBox over a pivot
    /// document, find &amp; replace bar, footnotes panel. The pivot stays the
    /// source of truth — Commit() flushes the FlowDocument back into the item.
    /// Text-level undo is the RichTextBox's own; the Binder history is separate.</summary>
    public class EditorView : DockPanel
    {
        private RichTextBox _box;
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private BinderItem _item;
        private bool _loading, _syncing;

        private ComboBox _styleCombo, _fontCombo, _sizeCombo;
        private ToggleButton _boldBtn, _italicBtn, _underBtn, _strikeBtn;
        private ToggleButton _alignLeft, _alignCenter, _alignRight, _alignJustify;

        private Border _searchBar;
        private TextBox _searchBox, _replaceBox;
        private CheckBox _caseCheck;
        private TextBlock _searchInfo;

        private Border _notesBar;
        private StackPanel _notesList;

        public event Action Edited; // any content or footnote change
        public event Action<string> LinkClicked; // Ctrl+click on a [[wiki link]]

        public EditorView()
        {
            BuildFormatBar();
            BuildSearchBar();
            BuildNotesBar();
            BuildPage();
        }

        public bool HasItem { get { return _item != null; } }

        // ============================================================= construction

        private void BuildFormatBar()
        {
            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4)
            };
            SetDock(bar, Dock.Top);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            _styleCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 6, 0) };
            _styleCombo.SelectionChanged += OnStyleComboChanged;
            panel.Children.Add(_styleCombo);

            _fontCombo = new ComboBox { Width = 140, Margin = new Thickness(0, 0, 6, 0) };
            foreach (var family in ListFonts()) _fontCombo.Items.Add(family);
            _fontCombo.SelectionChanged += OnFontComboChanged;
            panel.Children.Add(_fontCombo);

            _sizeCombo = new ComboBox { Width = 52, Margin = new Thickness(0, 0, 10, 0) };
            foreach (var size in new[] { 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 32, 36, 42, 48, 60, 72 })
                _sizeCombo.Items.Add(size);
            _sizeCombo.SelectionChanged += OnSizeComboChanged;
            panel.Children.Add(_sizeCombo);

            _boldBtn = FormatToggle("G", "Gras (Ctrl+B)", true, false, false, false);
            _boldBtn.Click += delegate { EditingCommands.ToggleBold.Execute(null, _box); AfterFormat(); };
            _italicBtn = FormatToggle("I", "Italique (Ctrl+I)", false, true, false, false);
            _italicBtn.Click += delegate { EditingCommands.ToggleItalic.Execute(null, _box); AfterFormat(); };
            _underBtn = FormatToggle("S", "Souligné (Ctrl+U)", false, false, true, false);
            _underBtn.Click += delegate { ToggleDecoration(TextDecorationLocation.Underline); };
            _strikeBtn = FormatToggle("B", "Barré", false, false, false, true);
            _strikeBtn.Click += delegate { ToggleDecoration(TextDecorationLocation.Strikethrough); };
            panel.Children.Add(_boldBtn);
            panel.Children.Add(_italicBtn);
            panel.Children.Add(_underBtn);
            panel.Children.Add(_strikeBtn);

            panel.Children.Add(VerticalRule());

            _alignLeft = AlignToggle("left", "Aligné à gauche");
            _alignCenter = AlignToggle("center", "Centré");
            _alignRight = AlignToggle("right", "Aligné à droite");
            _alignJustify = AlignToggle("justify", "Justifié");
            panel.Children.Add(_alignLeft);
            panel.Children.Add(_alignCenter);
            panel.Children.Add(_alignRight);
            panel.Children.Add(_alignJustify);

            panel.Children.Add(VerticalRule());

            panel.Children.Add(PaletteButton("Couleur du texte", true));
            panel.Children.Add(PaletteButton("Surlignage", false));

            bar.Child = panel;
            Children.Add(bar);
        }

        private static List<string> ListFonts()
        {
            var names = new List<string>();
            foreach (var family in Fonts.SystemFontFamilies) names.Add(family.Source);
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        private ToggleButton FormatToggle(string label, string tooltip,
            bool bold, bool italic, bool underline, bool strike)
        {
            var text = new TextBlock { Text = label, FontSize = 13 };
            if (bold) text.FontWeight = FontWeights.Bold;
            if (italic) text.FontStyle = FontStyles.Italic;
            if (underline) text.TextDecorations = TextDecorations.Underline;
            if (strike) text.TextDecorations = TextDecorations.Strikethrough;
            return new ToggleButton
            {
                Content = text,
                ToolTip = tooltip,
                Width = 28,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private ToggleButton AlignToggle(string align, string tooltip)
        {
            var button = new ToggleButton
            {
                Content = AlignIcon(align),
                ToolTip = tooltip,
                Width = 28,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Tag = align
            };
            button.Click += delegate
            {
                var command = align == "center" ? EditingCommands.AlignCenter
                            : align == "right" ? EditingCommands.AlignRight
                            : align == "justify" ? EditingCommands.AlignJustify
                            : EditingCommands.AlignLeft;
                command.Execute(null, _box);
                AfterFormat();
            };
            return button;
        }

        /// <summary>Tiny alignment icon: four bars whose widths sketch the mode.</summary>
        private UIElement AlignIcon(string align)
        {
            var panel = new StackPanel { Width = 14 };
            var widths = align == "center" ? new double[] { 14, 10, 14, 8 }
                       : align == "right" ? new double[] { 14, 10, 14, 8 }
                       : align == "justify" ? new double[] { 14, 14, 14, 14 }
                       : new double[] { 14, 10, 14, 8 };
            var alignment = align == "center" ? HorizontalAlignment.Center
                          : align == "right" ? HorizontalAlignment.Right
                          : HorizontalAlignment.Left;
            foreach (var width in widths)
                panel.Children.Add(new Border
                {
                    Height = 2,
                    Width = width,
                    Background = Chrome.Ink,
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = alignment
                });
            return panel;
        }

        private static Border VerticalRule()
        {
            return new Border
            {
                Width = 1,
                Background = Chrome.Border,
                Margin = new Thickness(7, 2, 7, 2)
            };
        }

        private Button PaletteButton(string tooltip, bool isForeground)
        {
            var button = new Button
            {
                ToolTip = tooltip,
                Width = 30,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
            if (isForeground)
            {
                button.Content = new TextBlock
                {
                    Text = "A",
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
                };
            }
            else
            {
                button.Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA3)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock { Text = "A", Foreground = Brushes.Black }
                };
            }

            var menu = new ContextMenu { Placement = PlacementMode.Bottom };
            if (isForeground)
            {
                AddColorEntry(menu, "Automatique", null, true);
                foreach (var hex in new[] { "#C0392B", "#E67E22", "#C9A227", "#27AE60",
                    "#16A085", "#2980B9", "#5B67D8", "#8E44AD", "#7F8C8D", "#703C2F" })
                    AddColorEntry(menu, hex, hex, true);
            }
            else
            {
                AddColorEntry(menu, "Aucun", null, false);
                foreach (var hex in new[] { "#FFF3A3", "#FFD9A8", "#D3F8D3", "#D0E8FF",
                    "#FFD6E7", "#E5D4FF", "#E8E8E8" })
                    AddColorEntry(menu, hex, hex, false);
            }
            menu.PlacementTarget = button;
            button.ContextMenu = menu;
            button.Click += delegate { menu.IsOpen = true; };
            return button;
        }

        private void AddColorEntry(ContextMenu menu, string label, string hex, bool isForeground)
        {
            var entry = new MenuItem { Header = label };
            if (hex != null)
                entry.Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1)
                };
            entry.Click += delegate
            {
                if (isForeground)
                    _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
                        hex == null ? (Brush)Chrome.Ink : new SolidColorBrush(FlowConverter.ParseColor(hex)));
                else
                    _box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                        hex == null ? null : new SolidColorBrush(FlowConverter.ParseColor(hex)));
                AfterFormat();
            };
            menu.Items.Add(entry);
        }

        private void BuildSearchBar()
        {
            _searchBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 5, 8, 5),
                Visibility = Visibility.Collapsed
            };
            SetDock(_searchBar, Dock.Top);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(Label("Rechercher :"));
            _searchBox = new TextBox { Width = 160, Margin = new Thickness(4, 0, 10, 0) };
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
            };
            panel.Children.Add(_searchBox);

            panel.Children.Add(Label("Remplacer :"));
            _replaceBox = new TextBox { Width = 160, Margin = new Thickness(4, 0, 10, 0) };
            panel.Children.Add(_replaceBox);

            _caseCheck = new CheckBox
            {
                Content = "Respecter la casse",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 0, 10, 0)
            };
            panel.Children.Add(_caseCheck);

            panel.Children.Add(SmallButton("Suivant", FindNext));
            panel.Children.Add(SmallButton("Remplacer", ReplaceCurrent));
            panel.Children.Add(SmallButton("Tout remplacer", ReplaceAll));

            _searchInfo = new TextBlock
            {
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            panel.Children.Add(_searchInfo);

            _searchBar.Child = panel;
            _searchBar.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { HideSearch(); e.Handled = true; }
            };
            Children.Add(_searchBar);
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Button SmallButton(string label, Action onClick)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            button.Click += delegate { onClick(); };
            return button;
        }

        private void BuildNotesBar()
        {
            _notesBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 8, 24, 8),
                Visibility = Visibility.Collapsed,
                MaxHeight = 180
            };
            SetDock(_notesBar, Dock.Bottom);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Notes de bas de page",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _notesList = new StackPanel();
            panel.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 130,
                Content = _notesList
            });
            _notesBar.Child = panel;
            Children.Add(_notesBar);
        }

        private void BuildPage()
        {
            _box = new RichTextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Chrome.Ink,
                CaretBrush = Chrome.Ink,
                AcceptsTab = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _box.TextChanged += delegate { if (!_loading) NotifyEdited(); };
            _box.SelectionChanged += delegate { SyncToolbar(); };
            _box.PreviewMouseLeftButtonDown += OnEditorMouseDown;

            var page = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                MaxWidth = 900,
                Margin = new Thickness(24, 20, 24, 20),
                Child = _box
            };
            Children.Add(page); // last child fills the remaining space
        }

        // ============================================================= item lifecycle

        public void SetStyleSheet(StyleSheet styles)
        {
            _styles = styles;
            _syncing = true;
            _styleCombo.Items.Clear();
            foreach (var style in styles.Styles)
                _styleCombo.Items.Add(new ComboBoxItem { Content = style.Name, Tag = style.Id });
            _syncing = false;
        }

        public void LoadItem(BinderItem item)
        {
            _item = item;
            _loading = true;
            _box.Document = FlowConverter.ToFlow(item.Document, _styles);
            _loading = false;
            RebuildNotesPanel();
            HideSearch();
        }

        /// <summary>Flushes the FlowDocument back into the pivot. Call before any
        /// save, item switch or style-sheet edit.</summary>
        public void Commit()
        {
            if (_item == null) return;
            _item.Document = FlowConverter.FromFlow(_box.Document, _styles, _item.Document.Footnotes);
        }

        /// <summary>Re-renders the current item (after the style sheet changed).</summary>
        public void Reload()
        {
            if (_item == null) return;
            Commit();
            LoadItem(_item);
        }

        /// <summary>Detaches the editor from its item (selection moved to a
        /// folder, or the item was undone out of existence).</summary>
        public void Clear()
        {
            _item = null;
            _loading = true;
            _box.Document = new FlowDocument();
            _loading = false;
            RebuildNotesPanel();
            HideSearch();
        }

        /// <summary>Cheap periodic notes refresh: renumbers markers and rebuilds
        /// the panel only when the marker set changed, never while a note is
        /// being typed in (that would steal focus).</summary>
        public void SyncNotes()
        {
            if (_item == null) return;
            foreach (DockPanel row in _notesList.Children)
                foreach (var child in row.Children)
                {
                    var box = child as TextBox;
                    if (box != null && box.IsKeyboardFocused) return;
                }
            var ordered = FlowConverter.RenumberFootnotes(_box.Document);
            var changed = ordered.Count != _notesList.Children.Count;
            if (!changed)
            {
                var i = 0;
                foreach (DockPanel row in _notesList.Children)
                {
                    foreach (var child in row.Children)
                    {
                        var box = child as TextBox;
                        if (box != null && (string)box.Tag != ordered[i]) { changed = true; break; }
                    }
                    if (changed) break;
                    i++;
                }
            }
            if (changed) RebuildNotesPanel();
        }

        public void FocusEditor()
        {
            _box.Focus();
        }

        public string PlainText()
        {
            if (_item == null) return "";
            return new TextRange(_box.Document.ContentStart, _box.Document.ContentEnd).Text;
        }

        private void NotifyEdited()
        {
            var handler = Edited;
            if (handler != null) handler();
        }

        private void AfterFormat()
        {
            NotifyEdited();
            SyncToolbar();
            _box.Focus();
        }

        // ============================================================= formatting

        private void OnStyleComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null) return;
            var chosen = _styleCombo.SelectedItem as ComboBoxItem;
            if (chosen == null) return;
            var style = _styles.Find((string)chosen.Tag);

            var paragraph = _box.Selection.Start.Paragraph;
            var last = _box.Selection.End.Paragraph;
            while (paragraph != null)
            {
                FlowConverter.ApplyParagraphStyle(paragraph, style);
                if (paragraph == last) break;
                Block next = paragraph.NextBlock;
                while (next != null && !(next is Paragraph)) next = next.NextBlock;
                paragraph = next as Paragraph;
            }
            AfterFormat();
        }

        private void OnFontComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _fontCombo.SelectedItem == null) return;
            _box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
                new FontFamily((string)_fontCombo.SelectedItem));
            AfterFormat();
        }

        private void OnSizeComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _sizeCombo.SelectedItem == null) return;
            _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,
                (double)(int)_sizeCombo.SelectedItem);
            AfterFormat();
        }

        private void ToggleDecoration(TextDecorationLocation location)
        {
            var current = _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                as TextDecorationCollection;
            var has = false;
            if (current != null)
                foreach (var decoration in current)
                    if (decoration.Location == location) { has = true; break; }

            var next = new TextDecorationCollection();
            if (current != null)
                foreach (var decoration in current)
                    if (decoration.Location != location) next.Add(decoration);
            if (!has)
                next.Add(location == TextDecorationLocation.Underline
                    ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
            _box.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
            AfterFormat();
        }

        /// <summary>Reflects the selection's formatting in the format bar.</summary>
        private void SyncToolbar()
        {
            if (_loading || _item == null) return;
            _syncing = true;
            try
            {
                var weight = _box.Selection.GetPropertyValue(TextElement.FontWeightProperty);
                _boldBtn.IsChecked = weight is FontWeight && (FontWeight)weight >= FontWeights.Bold;

                var fontStyle = _box.Selection.GetPropertyValue(TextElement.FontStyleProperty);
                _italicBtn.IsChecked = fontStyle is FontStyle && (FontStyle)fontStyle == FontStyles.Italic;

                var decorations = _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                    as TextDecorationCollection;
                var under = false;
                var strike = false;
                if (decorations != null)
                    foreach (var decoration in decorations)
                    {
                        if (decoration.Location == TextDecorationLocation.Underline) under = true;
                        if (decoration.Location == TextDecorationLocation.Strikethrough) strike = true;
                    }
                _underBtn.IsChecked = under;
                _strikeBtn.IsChecked = strike;

                var family = _box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                _fontCombo.SelectedItem = family == null ? null : (object)family.Source;

                var size = _box.Selection.GetPropertyValue(TextElement.FontSizeProperty);
                _sizeCombo.SelectedItem = size is double ? (object)(int)Math.Round((double)size) : null;

                var paragraph = _box.Selection.Start.Paragraph;
                if (paragraph != null)
                {
                    var styleId = paragraph.Tag as string ?? "body";
                    ComboBoxItem match = null;
                    foreach (ComboBoxItem candidate in _styleCombo.Items)
                        if ((string)candidate.Tag == styleId) { match = candidate; break; }
                    _styleCombo.SelectedItem = match;

                    var align = paragraph.TextAlignment;
                    _alignLeft.IsChecked = align == TextAlignment.Left;
                    _alignCenter.IsChecked = align == TextAlignment.Center;
                    _alignRight.IsChecked = align == TextAlignment.Right;
                    _alignJustify.IsChecked = align == TextAlignment.Justify;
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        // ============================================================= find & replace

        public void ShowSearch()
        {
            if (_item == null) return;
            _searchBar.Visibility = Visibility.Visible;
            _searchInfo.Text = "";
            if (!_box.Selection.IsEmpty && _box.Selection.Text.Length < 80
                && !_box.Selection.Text.Contains("\n"))
                _searchBox.Text = _box.Selection.Text;
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public void HideSearch()
        {
            if (_searchBar.Visibility == Visibility.Collapsed) return;
            _searchBar.Visibility = Visibility.Collapsed;
            _box.Focus();
        }

        private StringComparison Comparison()
        {
            return _caseCheck.IsChecked == true
                ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        }

        /// <summary>A paragraph's text plus a map from character offsets to runs,
        /// so a hit index can be turned back into TextPointers.</summary>
        private sealed class ParagraphMap
        {
            public Paragraph Paragraph;
            public string Text;
            public List<int> Offsets = new List<int>();
            public List<Run> Runs = new List<Run>();

            public TextPointer PointerAt(int index)
            {
                for (var i = Offsets.Count - 1; i >= 0; i--)
                    if (Offsets[i] <= index)
                        return Runs[i].ContentStart.GetPositionAtOffset(index - Offsets[i]);
                return Paragraph.ContentStart;
            }
        }

        private List<ParagraphMap> MapParagraphs()
        {
            var maps = new List<ParagraphMap>();
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
            {
                var map = new ParagraphMap { Paragraph = paragraph };
                var sb = new System.Text.StringBuilder();
                AppendInlines(paragraph.Inlines, sb, map);
                map.Text = sb.ToString();
                maps.Add(map);
            }
            return maps;
        }

        private static void AppendInlines(InlineCollection inlines, System.Text.StringBuilder sb, ParagraphMap map)
        {
            foreach (var inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    var tag = run.Tag as string;
                    if (tag != null && tag.StartsWith("fn:")) continue; // markers are not text
                    map.Offsets.Add(sb.Length);
                    map.Runs.Add(run);
                    sb.Append(run.Text);
                    continue;
                }
                if (inline is LineBreak) { sb.Append('\n'); continue; }
                var span = inline as Span;
                if (span != null) AppendInlines(span.Inlines, sb, map);
            }
        }

        private void FindNext()
        {
            TryFindNext(true);
        }

        private bool TryFindNext(bool wrap)
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null) return false;
            var comparison = Comparison();
            var caret = _box.Selection.End;

            var maps = MapParagraphs();
            TextPointer firstStart = null, firstEnd = null; // first hit in the document (wrap target)
            foreach (var map in maps)
            {
                var index = 0;
                while ((index = map.Text.IndexOf(needle, index, comparison)) >= 0)
                {
                    var start = map.PointerAt(index);
                    var end = map.PointerAt(index + needle.Length);
                    if (start != null && end != null)
                    {
                        if (firstStart == null) { firstStart = start; firstEnd = end; }
                        if (start.CompareTo(caret) > 0)
                        {
                            SelectResult(start, end);
                            return true;
                        }
                    }
                    index += 1;
                }
            }
            if (wrap && firstStart != null)
            {
                SelectResult(firstStart, firstEnd);
                _searchInfo.Text = "Reprise au début";
                return true;
            }
            _searchInfo.Text = "Aucun résultat";
            return false;
        }

        private void SelectResult(TextPointer start, TextPointer end)
        {
            _box.Selection.Select(start, end);
            _searchInfo.Text = "";
            var element = start.Parent as FrameworkContentElement;
            if (element != null) element.BringIntoView();
            _box.Focus();
        }

        private void ReplaceCurrent()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle)) return;
            if (!_box.Selection.IsEmpty
                && string.Equals(_box.Selection.Text, needle, Comparison()))
            {
                _box.Selection.Text = _replaceBox.Text;
                NotifyEdited();
            }
            FindNext();
        }

        private void ReplaceAll()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null) return;
            var count = 0;
            _box.CaretPosition = _box.Document.ContentStart;
            _box.Selection.Select(_box.Document.ContentStart, _box.Document.ContentStart);
            while (count < 10000 && TryFindNext(false))
            {
                _box.Selection.Text = _replaceBox.Text;
                _box.CaretPosition = _box.Selection.End;
                count++;
            }
            if (count > 0) NotifyEdited();
            _searchInfo.Text = count == 0 ? "Aucun résultat"
                : count == 1 ? "1 remplacement" : count + " remplacements";
        }

        // ============================================================= wiki links

        private void OnEditorMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var pointer = _box.GetPositionFromPoint(e.GetPosition(_box), true);
            if (pointer == null) return;
            var run = pointer.Parent as Run;
            if (run == null || (run.Tag as string) != "wikilink") return;
            var text = run.Text.Trim();
            if (!text.StartsWith("[[") || !text.EndsWith("]]")) return;
            var handler = LinkClicked;
            if (handler != null) handler(text.Substring(2, text.Length - 4).Trim());
            e.Handled = true;
        }

        /// <summary>Inserts a styled [[link]] at the caret, immediately clickable.</summary>
        public void InsertWikiLink(string title)
        {
            if (_item == null || string.IsNullOrEmpty(title)) return;
            var caret = _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var link = new Run("[[" + title + "]]", caret)
            {
                Tag = "wikilink",
                Foreground = Chrome.Accent,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _box.CaretPosition = link.ElementEnd;
            _box.Selection.Select(link.ElementEnd, link.ElementEnd);
            _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)Chrome.Ink);
            NotifyEdited();
            _box.Focus();
        }

        // ============================================================= footnotes

        public void InsertFootnote()
        {
            if (_item == null) return;
            var note = new Footnote();
            _item.Document.Footnotes.Add(note);

            var caret = _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var paragraph = caret.Paragraph;
            var size = paragraph != null ? paragraph.FontSize : _styles.Body.FontSize;
            var marker = new Run("?", caret)
            {
                Tag = "fn:" + note.Id,
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = Math.Max(8, size * 0.65),
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.Accent
            };

            // Move the caret past the marker and neutralize its spring-loaded
            // formatting so typing resumes with normal text.
            _box.CaretPosition = marker.ElementEnd;
            _box.Selection.Select(marker.ElementEnd, marker.ElementEnd);
            _box.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
            _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            _box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)Chrome.Ink);

            RebuildNotesPanel();
            NotifyEdited();
            FocusNote(note.Id);
        }

        /// <summary>Renumbers markers, prunes orphaned notes and rebuilds the
        /// bottom panel. Called after load, insert and edits that may have
        /// removed a marker.</summary>
        public void RebuildNotesPanel()
        {
            _notesList.Children.Clear();
            if (_item == null) { _notesBar.Visibility = Visibility.Collapsed; return; }

            var ordered = FlowConverter.RenumberFootnotes(_box.Document);
            _notesBar.Visibility = ordered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            var number = 0;
            foreach (var id in ordered)
            {
                number++;
                var note = _item.Document.FindFootnote(id);
                if (note == null)
                {
                    // A marker without its note (should not happen): recreate the
                    // note rather than lose the marker silently.
                    note = new Footnote { Id = id };
                    _item.Document.Footnotes.Add(note);
                }

                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var label = new TextBlock
                {
                    Text = number + ".",
                    Foreground = Chrome.SoftText,
                    Width = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                var noteRef = note;
                var box = new TextBox
                {
                    Text = note.Text,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = false,
                    Tag = id
                };
                box.TextChanged += delegate
                {
                    noteRef.Text = box.Text;
                    NotifyEdited();
                };
                row.Children.Add(box);
                _notesList.Children.Add(row);
            }
        }

        private void FocusNote(string id)
        {
            foreach (DockPanel row in _notesList.Children)
                foreach (var child in row.Children)
                {
                    var box = child as TextBox;
                    if (box != null && (string)box.Tag == id) { box.Focus(); return; }
                }
        }
    }
}
