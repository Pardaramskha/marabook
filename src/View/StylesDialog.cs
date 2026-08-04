using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Style sheet editor: list on the left, properties on the right.
    /// Works on a clone; returns the new sheet on OK, null on cancel.
    /// "body" cannot be deleted (it is the universal fallback).</summary>
    public class StylesDialog : Window
    {
        private readonly StyleSheet _sheet;
        private readonly ListBox _list;
        private ParagraphStyle _current;
        private bool _accepted, _syncing;

        private TextBox _nameBox, _sizeBox, _beforeBox, _afterBox, _firstBox, _leftBox, _colorBox;
        private ComboBox _fontCombo, _alignCombo;
        private CheckBox _boldCheck, _italicCheck;

        private StylesDialog(Window owner, StyleSheet source)
        {
            _sheet = source.Clone();

            Title = "Styles du projet";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 640;
            Height = 460;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var root = new Grid { Margin = new Thickness(14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
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

            // --- right: property form ---
            var form = new StackPanel();
            form.Children.Add(FormRow("Nom", _nameBox = new TextBox()));

            _fontCombo = new ComboBox();
            foreach (var family in Fonts.SystemFontFamilies) _fontCombo.Items.Add(family.Source);
            form.Children.Add(FormRow("Police", _fontCombo));

            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            sizeRow.Children.Add(_sizeBox = new TextBox { Width = 60 });
            _boldCheck = new CheckBox { Content = "Gras", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _italicCheck = new CheckBox { Content = "Italique", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sizeRow.Children.Add(_boldCheck);
            sizeRow.Children.Add(_italicCheck);
            form.Children.Add(FormRow("Taille", sizeRow));

            _alignCombo = new ComboBox();
            _alignCombo.Items.Add("Gauche");
            _alignCombo.Items.Add("Centré");
            _alignCombo.Items.Add("Droite");
            _alignCombo.Items.Add("Justifié");
            form.Children.Add(FormRow("Alignement", _alignCombo));

            var spacingRow = new StackPanel { Orientation = Orientation.Horizontal };
            spacingRow.Children.Add(_beforeBox = new TextBox { Width = 50 });
            spacingRow.Children.Add(new TextBlock { Text = "avant, ", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
            spacingRow.Children.Add(_afterBox = new TextBox { Width = 50 });
            spacingRow.Children.Add(new TextBlock { Text = "après", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            form.Children.Add(FormRow("Espacement", spacingRow));

            var indentRow = new StackPanel { Orientation = Orientation.Horizontal };
            indentRow.Children.Add(_firstBox = new TextBox { Width = 50 });
            indentRow.Children.Add(new TextBlock { Text = "1re ligne, ", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
            indentRow.Children.Add(_leftBox = new TextBox { Width = 50 });
            indentRow.Children.Add(new TextBlock { Text = "à gauche", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            form.Children.Add(FormRow("Retraits", indentRow));

            form.Children.Add(FormRow("Couleur", _colorBox = new TextBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "« auto » ou #RRGGBB" }));

            Grid.SetColumn(form, 1);
            root.Children.Add(form);

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

        // ------------------------------------------------------- helpers

        private static DockPanel FormRow(string label, UIElement field)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            var caption = new TextBlock
            {
                Text = label,
                Width = 90,
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
                var entry = new ListBoxItem { Content = style.Name, Tag = style.Id };
                _list.Items.Add(entry);
                if (selectId == null && style.Id == "body") _list.SelectedItem = entry;
                if (selectId != null && style.Id == selectId) _list.SelectedItem = entry;
            }
            if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void ShowStyle(ParagraphStyle style)
        {
            _current = style;
            if (style == null) return;
            _syncing = true;
            _nameBox.Text = style.Name;
            _fontCombo.SelectedItem = style.FontFamily;
            _sizeBox.Text = style.FontSize.ToString(CultureInfo.CurrentCulture);
            _boldCheck.IsChecked = style.Bold;
            _italicCheck.IsChecked = style.Italic;
            _alignCombo.SelectedIndex = style.Align == "center" ? 1
                                      : style.Align == "right" ? 2
                                      : style.Align == "justify" ? 3 : 0;
            _beforeBox.Text = style.SpaceBefore.ToString(CultureInfo.CurrentCulture);
            _afterBox.Text = style.SpaceAfter.ToString(CultureInfo.CurrentCulture);
            _firstBox.Text = style.FirstLineIndent.ToString(CultureInfo.CurrentCulture);
            _leftBox.Text = style.LeftIndent.ToString(CultureInfo.CurrentCulture);
            _colorBox.Text = style.Color ?? "auto";
            _syncing = false;
        }

        /// <summary>Writes the form back into the style being edited.</summary>
        private void CommitForm()
        {
            if (_current == null || _syncing) return;
            var name = _nameBox.Text.Trim();
            if (name.Length > 0) _current.Name = name;
            if (_fontCombo.SelectedItem != null) _current.FontFamily = (string)_fontCombo.SelectedItem;
            _current.FontSize = ParseDouble(_sizeBox.Text, _current.FontSize, 6, 200);
            _current.Bold = _boldCheck.IsChecked == true;
            _current.Italic = _italicCheck.IsChecked == true;
            _current.Align = _alignCombo.SelectedIndex == 1 ? "center"
                           : _alignCombo.SelectedIndex == 2 ? "right"
                           : _alignCombo.SelectedIndex == 3 ? "justify" : "left";
            _current.SpaceBefore = ParseDouble(_beforeBox.Text, _current.SpaceBefore, 0, 200);
            _current.SpaceAfter = ParseDouble(_afterBox.Text, _current.SpaceAfter, 0, 200);
            _current.FirstLineIndent = ParseDouble(_firstBox.Text, _current.FirstLineIndent, 0, 300);
            _current.LeftIndent = ParseDouble(_leftBox.Text, _current.LeftIndent, 0, 300);
            var color = _colorBox.Text.Trim();
            _current.Color = (color.Length == 0 || color.Equals("auto", StringComparison.OrdinalIgnoreCase))
                ? null : color;

            // Refresh the list label in place.
            var entry = _list.SelectedItem as ListBoxItem;
            if (entry != null) entry.Content = _current.Name;
        }

        private static double ParseDouble(string text, double fallback, double min, double max)
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
                MessageBox.Show(this, "Le style « Corps » est le style de secours : il ne peut pas être supprimé.",
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
