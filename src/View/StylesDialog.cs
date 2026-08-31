using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Style sheet editor: list on the left, attributes on the right in
    /// four tabs — Caractère, Paragraphe, Césure, Justification (InDesign-style;
    /// the fine hyphenation/justification numbers drive exports and the 4b
    /// print composer, WPF renders the rest live). Works on a clone; returns
    /// the new sheet on OK, null on cancel. "body" cannot be deleted.
    /// UI units: points for sizes and leading, millimeters for indents and
    /// spacing, percentages for justification. Model stays WPF px.</summary>
    public class StylesDialog : Window
    {
        private const double PxPerMm = 96.0 / 25.4;

        private readonly StyleSheet _sheet;
        private readonly ListBox _list;
        private ParagraphStyle _current;
        private ListBoxItem _currentEntry; // row of _current — never SelectedItem,
                                           // already moved when SelectionChanged commits
        private bool _accepted, _syncing;

        private TextBox _nameBox;
        // Caractère
        private ComboBox _fontCombo;
        private TextBox _sizeBox, _leadingBox, _colorBox;
        private CheckBox _boldCheck, _italicCheck, _ligaturesCheck;
        // Paragraphe
        private ComboBox _alignCombo;
        private TextBox _leftBox, _rightBox, _firstBox, _lastBox, _beforeBox, _afterBox;
        // Césure
        private CheckBox _hyphenCheck;
        private TextBox _hyphenWordBox, _hyphenBeforeBox, _hyphenAfterBox, _hyphenLimitBox;
        // Justification
        private TextBox[] _justifyBoxes; // word min/opt/max, letter, glyph (9)
        private TextBox _autoLeadingBox;
        // Enchaînements
        private CheckBox _keepPrevCheck, _keepLinesCheck;
        private TextBox _keepNextBox;

        private StylesDialog(Window owner, StyleSheet source)
        {
            _sheet = source.Clone();

            Title = "Gestion des styles";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 720;
            Height = 540;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new Grid { Margin = new Thickness(14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // --- left: style list + list actions ---
            var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            listButtons.Children.Add(SmallButton("Nouveau", NewStyle));
            listButtons.Children.Add(SmallButton("Dupliquer", DuplicateStyle));
            listButtons.Children.Add(SmallButton("Supprimer", DeleteStyle));
            DockPanel.SetDock(listButtons, Dock.Bottom);
            left.Children.Add(listButtons);

            _list = new ListBox();
            _list.SelectionChanged += delegate { CommitForm(); ShowStyle(SelectedStyle()); };
            left.Children.Add(_list);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // --- right: name + tabs ---
            var right = new DockPanel();
            var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var nameLabel = new TextBlock
            {
                Text = "Nom",
                Width = 60,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            _nameBox = new TextBox();
            nameRow.Children.Add(_nameBox);
            DockPanel.SetDock(nameRow, Dock.Top);
            right.Children.Add(nameRow);

            var tabs = new TabControl { Background = Brushes.Transparent };
            tabs.Items.Add(new TabItem { Header = "Caractère", Content = BuildCharacterTab() });
            tabs.Items.Add(new TabItem { Header = "Paragraphe", Content = BuildParagraphTab() });
            tabs.Items.Add(new TabItem { Header = "Césure", Content = BuildHyphenationTab() });
            tabs.Items.Add(new TabItem { Header = "Justification", Content = BuildJustificationTab() });
            tabs.Items.Add(new TabItem { Header = "Enchaînements", Content = BuildKeepsTab() });
            right.Children.Add(tabs);

            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            // --- bottom: OK / cancel ---
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { CommitForm(); _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 1);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            Content = root;
            FillList(null);
        }

        public static StyleSheet Show(Window owner, StyleSheet source)
        {
            var dialog = new StylesDialog(owner, source);
            dialog.ShowDialog();
            return dialog._accepted ? dialog._sheet : null;
        }

        // ------------------------------------------------------- tabs

        private UIElement BuildCharacterTab()
        {
            var form = new StackPanel { Margin = new Thickness(10) };

            _fontCombo = new ComboBox();
            foreach (var family in Fonts.SystemFontFamilies) _fontCombo.Items.Add(family.Source);
            form.Children.Add(FormRow("Police", _fontCombo));

            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            sizeRow.Children.Add(_sizeBox = new TextBox { Width = 60 });
            _boldCheck = new CheckBox { Content = "Gras", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _italicCheck = new CheckBox { Content = "Italique", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sizeRow.Children.Add(_boldCheck);
            sizeRow.Children.Add(_italicCheck);
            form.Children.Add(FormRow("Taille (pt)", sizeRow));

            var leadingRow = new StackPanel { Orientation = Orientation.Horizontal };
            leadingRow.Children.Add(_leadingBox = new TextBox { Width = 60, ToolTip = "0 = automatique" });
            leadingRow.Children.Add(new TextBlock
            {
                Text = "pt (0 = auto)",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            form.Children.Add(FormRow("Interligne", leadingRow));

            _ligaturesCheck = new CheckBox { Content = "Ligatures", VerticalAlignment = VerticalAlignment.Center };
            form.Children.Add(FormRow("", _ligaturesCheck));

            form.Children.Add(FormRow("Couleur", _colorBox = new TextBox
            {
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "« auto » ou #RRGGBB"
            }));
            return form;
        }

        private UIElement BuildParagraphTab()
        {
            var form = new StackPanel { Margin = new Thickness(10) };

            _alignCombo = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            _alignCombo.Items.Add("Gauche");
            _alignCombo.Items.Add("Centré");
            _alignCombo.Items.Add("Droite");
            _alignCombo.Items.Add("Justifié");
            form.Children.Add(FormRow("Alignement", _alignCombo));

            form.Children.Add(FormRow("Retrait gauche (mm)", _leftBox = new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left }));
            form.Children.Add(FormRow("Retrait droit (mm)", _rightBox = new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left }));
            form.Children.Add(FormRow("Retrait 1re ligne (mm)", _firstBox = new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left }));
            form.Children.Add(FormRow("Retrait dernière ligne (mm)", _lastBox = new TextBox
            {
                Width = 60,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Appliqué à l'impression (compositeur 4b)"
            }));
            form.Children.Add(FormRow("Espace avant (mm)", _beforeBox = new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left }));
            form.Children.Add(FormRow("Espace après (mm)", _afterBox = new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left }));
            return form;
        }

        private UIElement BuildHyphenationTab()
        {
            var form = new StackPanel { Margin = new Thickness(10) };
            _hyphenCheck = new CheckBox { Content = "Césure activée", VerticalAlignment = VerticalAlignment.Center };
            form.Children.Add(FormRow("", _hyphenCheck));
            form.Children.Add(FormRow("Mots d'au moins (lettres)", _hyphenWordBox = Small()));
            form.Children.Add(FormRow("Après les premières (lettres)", _hyphenBeforeBox = Small()));
            form.Children.Add(FormRow("Avant les dernières (lettres)", _hyphenAfterBox = Small()));
            form.Children.Add(FormRow("Limite de césures consécutives", _hyphenLimitBox = Small()));
            form.Children.Add(new TextBlock
            {
                Text = "L'éditeur applique la césure marche/arrêt ; les réglages fins\n" +
                       "s'appliquent à l'export Word et au compositeur d'impression (4b).",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            });
            return form;
        }

        private UIElement BuildJustificationTab()
        {
            var form = new StackPanel { Margin = new Thickness(10) };
            _justifyBoxes = new TextBox[9];

            var grid = new Grid();
            for (var c = 0; c < 4; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = c == 0 ? new GridLength(150) : new GridLength(70)
                });
            for (var r = 0; r < 4; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddCell(grid, 0, 1, HeaderCell("Min. %"));
            AddCell(grid, 0, 2, HeaderCell("Opt. %"));
            AddCell(grid, 0, 3, HeaderCell("Max. %"));
            string[] rows = { "Intermots", "Interlettrage", "Mise à l'échelle glyphe" };
            for (var r = 0; r < 3; r++)
            {
                AddCell(grid, r + 1, 0, new TextBlock
                {
                    Text = rows[r],
                    Foreground = Chrome.SoftText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 3, 8, 3)
                });
                for (var c = 0; c < 3; c++)
                {
                    var box = new TextBox { Width = 60, Margin = new Thickness(2) };
                    _justifyBoxes[r * 3 + c] = box;
                    AddCell(grid, r + 1, c + 1, box);
                }
            }
            form.Children.Add(grid);

            form.Children.Add(FormRow("Interligne auto (%)", _autoLeadingBox = Small()));
            form.Children.Add(new TextBlock
            {
                Text = "Copie d'InDesign : ces plages guident le compositeur de\n" +
                       "paragraphe de l'impression (4b) — l'écran suit la justification WPF.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            });
            return form;
        }

        private UIElement BuildKeepsTab()
        {
            var form = new StackPanel { Margin = new Thickness(10) };
            _keepPrevCheck = new CheckBox
            {
                Content = "Solidaire avec le paragraphe précédent",
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Jamais de saut de page entre ce paragraphe et le précédent"
            };
            form.Children.Add(FormRow("", _keepPrevCheck));

            var nextRow = new StackPanel { Orientation = Orientation.Horizontal };
            nextRow.Children.Add(_keepNextBox = Small());
            nextRow.Children.Add(new TextBlock
            {
                Text = "lignes du paragraphe suivant",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            form.Children.Add(FormRow("Paragraphes solidaires", nextRow));

            _keepLinesCheck = new CheckBox
            {
                Content = "Lignes solidaires (paragraphe insécable entre deux pages)",
                VerticalAlignment = VerticalAlignment.Center
            };
            form.Children.Add(FormRow("", _keepLinesCheck));

            form.Children.Add(new TextBlock
            {
                Text = "Appliqués par le compositeur : mode Composition, aperçu des\n" +
                       "pages et impression. L'éditeur honore « solidaire avec le\n" +
                       "précédent » et garde toujours les paragraphes entiers.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            });
            return form;
        }

        // ------------------------------------------------------- helpers

        private static TextBox Small()
        {
            return new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
        }

        private static TextBlock HeaderCell(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2)
            };
        }

        private static void AddCell(Grid grid, int row, int column, UIElement element)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private static DockPanel FormRow(string label, UIElement field)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = new TextBlock
            {
                Text = label,
                Width = 175,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            row.Children.Add(field);
            return row;
        }

        private Button SmallButton(string label, Action onClick)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            button.Click += delegate { onClick(); };
            return button;
        }

        private ParagraphStyle SelectedStyle()
        {
            var entry = _list.SelectedItem as ListBoxItem;
            return entry == null ? null : _sheet.Find((string)entry.Tag);
        }

        private void FillList(string selectId)
        {
            _list.Items.Clear();
            foreach (var style in _sheet.Styles)
            {
                // Each entry previews its own font — nothing else.
                var entry = new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = style.Name,
                        FontFamily = new FontFamily(style.FontFamily)
                    },
                    Tag = style.Id
                };
                _list.Items.Add(entry);
                if (selectId == null && style.Id == "body") _list.SelectedItem = entry;
                if (selectId != null && style.Id == selectId) _list.SelectedItem = entry;
            }
            if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        // ------------------------------------------------------- form <-> style

        private void ShowStyle(ParagraphStyle style)
        {
            _current = style;
            _currentEntry = _list.SelectedItem as ListBoxItem;
            if (style == null) return;
            _syncing = true;

            _nameBox.Text = style.Name;
            _fontCombo.SelectedItem = style.FontFamily;
            _sizeBox.Text = Pt(style.FontSize);
            _boldCheck.IsChecked = style.Bold;
            _italicCheck.IsChecked = style.Italic;
            _leadingBox.Text = Pt(style.LineHeight);
            _ligaturesCheck.IsChecked = style.Ligatures;
            _colorBox.Text = style.Color ?? "auto";

            _alignCombo.SelectedIndex = style.Align == "center" ? 1
                                      : style.Align == "right" ? 2
                                      : style.Align == "justify" ? 3 : 0;
            _leftBox.Text = Mm(style.LeftIndent);
            _rightBox.Text = Mm(style.RightIndent);
            _firstBox.Text = Mm(style.FirstLineIndent);
            _lastBox.Text = Mm(style.LastLineIndent);
            _beforeBox.Text = Mm(style.SpaceBefore);
            _afterBox.Text = Mm(style.SpaceAfter);

            _hyphenCheck.IsChecked = style.HyphenationEnabled;
            _hyphenWordBox.Text = style.HyphenMinWordLength.ToString();
            _hyphenBeforeBox.Text = style.HyphenMinBefore.ToString();
            _hyphenAfterBox.Text = style.HyphenMinAfter.ToString();
            _hyphenLimitBox.Text = style.HyphenConsecutiveLimit.ToString();

            double[] justify =
            {
                style.JustifyWordMin, style.JustifyWordOpt, style.JustifyWordMax,
                style.JustifyLetterMin, style.JustifyLetterOpt, style.JustifyLetterMax,
                style.JustifyGlyphMin, style.JustifyGlyphOpt, style.JustifyGlyphMax
            };
            for (var i = 0; i < 9; i++)
                _justifyBoxes[i].Text = justify[i].ToString("0.#", CultureInfo.CurrentCulture);
            _autoLeadingBox.Text = style.AutoLeadingPercent.ToString("0.#", CultureInfo.CurrentCulture);

            _keepPrevCheck.IsChecked = style.KeepWithPrevious;
            _keepNextBox.Text = style.KeepNextLines.ToString();
            _keepLinesCheck.IsChecked = style.KeepLinesTogether;

            _syncing = false;
        }

        /// <summary>Writes the form back into the style being edited.</summary>
        private void CommitForm()
        {
            if (_current == null || _syncing) return;
            var name = _nameBox.Text.Trim();
            if (name.Length > 0) _current.Name = name;

            if (_fontCombo.SelectedItem != null) _current.FontFamily = (string)_fontCombo.SelectedItem;
            _current.FontSize = FromPt(_sizeBox.Text, _current.FontSize, 4, 150);
            _current.Bold = _boldCheck.IsChecked == true;
            _current.Italic = _italicCheck.IsChecked == true;
            _current.LineHeight = FromPt(_leadingBox.Text, _current.LineHeight, 0, 200);
            _current.Ligatures = _ligaturesCheck.IsChecked == true;
            var color = _colorBox.Text.Trim();
            _current.Color = (color.Length == 0 || color.Equals("auto", StringComparison.OrdinalIgnoreCase))
                ? null : color;

            _current.Align = _alignCombo.SelectedIndex == 1 ? "center"
                           : _alignCombo.SelectedIndex == 2 ? "right"
                           : _alignCombo.SelectedIndex == 3 ? "justify" : "left";
            _current.LeftIndent = FromMm(_leftBox.Text, _current.LeftIndent, 0, 100);
            _current.RightIndent = FromMm(_rightBox.Text, _current.RightIndent, 0, 100);
            _current.FirstLineIndent = FromMm(_firstBox.Text, _current.FirstLineIndent, 0, 100);
            _current.LastLineIndent = FromMm(_lastBox.Text, _current.LastLineIndent, 0, 100);
            _current.SpaceBefore = FromMm(_beforeBox.Text, _current.SpaceBefore, 0, 100);
            _current.SpaceAfter = FromMm(_afterBox.Text, _current.SpaceAfter, 0, 100);

            _current.HyphenationEnabled = _hyphenCheck.IsChecked == true;
            _current.HyphenMinWordLength = ParseInt(_hyphenWordBox.Text, _current.HyphenMinWordLength, 2, 20);
            _current.HyphenMinBefore = ParseInt(_hyphenBeforeBox.Text, _current.HyphenMinBefore, 1, 10);
            _current.HyphenMinAfter = ParseInt(_hyphenAfterBox.Text, _current.HyphenMinAfter, 1, 10);
            _current.HyphenConsecutiveLimit = ParseInt(_hyphenLimitBox.Text, _current.HyphenConsecutiveLimit, 0, 20);

            _current.JustifyWordMin = ParsePercent(_justifyBoxes[0], _current.JustifyWordMin);
            _current.JustifyWordOpt = ParsePercent(_justifyBoxes[1], _current.JustifyWordOpt);
            _current.JustifyWordMax = ParsePercent(_justifyBoxes[2], _current.JustifyWordMax);
            _current.JustifyLetterMin = ParsePercent(_justifyBoxes[3], _current.JustifyLetterMin);
            _current.JustifyLetterOpt = ParsePercent(_justifyBoxes[4], _current.JustifyLetterOpt);
            _current.JustifyLetterMax = ParsePercent(_justifyBoxes[5], _current.JustifyLetterMax);
            _current.JustifyGlyphMin = ParsePercent(_justifyBoxes[6], _current.JustifyGlyphMin);
            _current.JustifyGlyphOpt = ParsePercent(_justifyBoxes[7], _current.JustifyGlyphOpt);
            _current.JustifyGlyphMax = ParsePercent(_justifyBoxes[8], _current.JustifyGlyphMax);
            _current.AutoLeadingPercent = ParsePercent(_autoLeadingBox, _current.AutoLeadingPercent);

            _current.KeepWithPrevious = _keepPrevCheck.IsChecked == true;
            _current.KeepNextLines = ParseInt(_keepNextBox.Text, _current.KeepNextLines, 0, 20);
            _current.KeepLinesTogether = _keepLinesCheck.IsChecked == true;

            // Refresh the edited style's own list label (name and font).
            if (_currentEntry != null)
                _currentEntry.Content = new TextBlock
                {
                    Text = _current.Name,
                    FontFamily = new FontFamily(_current.FontFamily)
                };
        }

        // px <-> UI units
        private static string Pt(double px) { return (px * 0.75).ToString("0.#", CultureInfo.CurrentCulture); }
        private static string Mm(double px) { return (px / PxPerMm).ToString("0.#", CultureInfo.CurrentCulture); }

        private static double FromPt(string text, double fallbackPx, double minPt, double maxPt)
        {
            return Parse(text, fallbackPx * 0.75, minPt, maxPt) * 4.0 / 3.0;
        }

        private static double FromMm(string text, double fallbackPx, double minMm, double maxMm)
        {
            return Parse(text, fallbackPx / PxPerMm, minMm, maxMm) * PxPerMm;
        }

        private static double ParsePercent(TextBox box, double fallback)
        {
            return Parse(box.Text, fallback, 0, 400);
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int value;
            if (!int.TryParse(text.Trim(), out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        private static double Parse(string text, double fallback, double min, double max)
        {
            double value;
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        // ------------------------------------------------------- list actions

        private void NewStyle()
        {
            CommitForm();
            var style = new ParagraphStyle { Name = "Nouveau style" };
            _sheet.Styles.Add(style);
            FillList(style.Id);
        }

        private void DuplicateStyle()
        {
            CommitForm();
            var source = SelectedStyle();
            if (source == null) return;
            var copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = source.Name + " (copie)";
            _sheet.Styles.Insert(_sheet.Styles.IndexOf(source) + 1, copy);
            FillList(copy.Id);
        }

        private void DeleteStyle()
        {
            var style = SelectedStyle();
            if (style == null) return;
            if (style.Id == "body")
            {
                MessageDialog.Show(this, "Le style « Corps » est le style de secours : il ne peut pas être supprimé.",
                    "Styles", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Paragraphs using a deleted style silently fall back to "body" at render time.
            _current = null;
            _sheet.Styles.Remove(style);
            FillList(null);
        }
    }
}
