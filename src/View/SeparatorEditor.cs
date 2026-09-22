using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>L'éditeur du séparateur de scène (22/09) : un style de
    /// paragraphe réduit à l'essentiel — le contenu inséré d'un clic (« *** »,
    /// « · · · »…), la police, la taille, la valeur d'interligne, l'alignement,
    /// les espaces avant et après, gras et italique. Sert au global
    /// (Préférences › Styles globaux) comme au livre (onglet Styles : « ce
    /// livre remplace le séparateur global »). Travaille sur le style reçu et
    /// lève Changed à chaque édition.</summary>
    public class SeparatorEditor : StackPanel
    {
        private const double PxPerMm = 96.0 / 25.4;

        private ParagraphStyle _style;
        private bool _syncing;
        private readonly TextBox _contentBox, _sizeBox, _leadingBox, _beforeBox, _afterBox;
        private readonly ComboBox _fontCombo, _alignCombo;
        private readonly CheckBox _boldCheck, _italicCheck;
        private readonly TextBlock _preview;

        public event Action Changed;

        public SeparatorEditor(ParagraphStyle style)
        {
            _contentBox = new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Le ou les caractères insérés par le bouton du ruban Texte" };
            _fontCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var family in Fonts.SystemFontFamilies) _fontCombo.Items.Add(family.Source);
            _sizeBox = Small();
            _leadingBox = Small();
            _alignCombo = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            _alignCombo.Items.Add("Gauche");
            _alignCombo.Items.Add("Centré");
            _alignCombo.Items.Add("Droite");
            _beforeBox = Small();
            _afterBox = Small();
            _boldCheck = new CheckBox { Content = "Gras", VerticalAlignment = VerticalAlignment.Center };
            _italicCheck = new CheckBox { Content = "Italique", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            Children.Add(FormRow("Contenu", _contentBox));
            Children.Add(FormRow("Police", _fontCombo));
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            sizeRow.Children.Add(_sizeBox);
            sizeRow.Children.Add(new TextBlock { Text = "pt", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) });
            sizeRow.Children.Add(_boldCheck);
            sizeRow.Children.Add(_italicCheck);
            Children.Add(FormRow("Taille", sizeRow));
            var leadingRow = new StackPanel { Orientation = Orientation.Horizontal };
            leadingRow.Children.Add(_leadingBox);
            leadingRow.Children.Add(new TextBlock { Text = "pt (0 = auto)", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            Children.Add(FormRow("Valeur d'interligne", leadingRow));
            Children.Add(FormRow("Alignement", _alignCombo));
            Children.Add(FormRow("Espace avant (mm)", _beforeBox));
            Children.Add(FormRow("Espace après (mm)", _afterBox));
            _preview = new TextBlock
            {
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var previewFrame = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Width = 320,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _preview
            };
            Children.Add(FormRow("Aperçu", previewFrame));

            Hook(_contentBox); Hook(_fontCombo); Hook(_sizeBox); Hook(_leadingBox); Hook(_alignCombo);
            Hook(_beforeBox); Hook(_afterBox); Hook(_boldCheck); Hook(_italicCheck);
            Load(style);
        }

        public ParagraphStyle Style { get { return _style; } }

        /// <summary>Montre un autre style (ou aucun : formulaire grisé).</summary>
        public void Load(ParagraphStyle style)
        {
            _style = style;
            IsEnabled = style != null;
            if (style == null) return;
            _syncing = true;
            _contentBox.Text = style.Content ?? "***";
            _fontCombo.SelectedItem = style.FontFamily;
            _sizeBox.Text = Pt(style.FontSize);
            _leadingBox.Text = Pt(style.LineHeight);
            _alignCombo.SelectedIndex = style.Align == "left" ? 0 : style.Align == "right" ? 2 : 1;
            _beforeBox.Text = Mm(style.SpaceBefore);
            _afterBox.Text = Mm(style.SpaceAfter);
            _boldCheck.IsChecked = style.Bold;
            _italicCheck.IsChecked = style.Italic;
            _syncing = false;
            RefreshPreview();
        }

        public void Commit()
        {
            if (_style == null || _syncing) return;
            var content = _contentBox.Text;
            _style.Content = content.Trim().Length == 0 ? "***" : content;
            if (_fontCombo.SelectedItem != null) _style.FontFamily = (string)_fontCombo.SelectedItem;
            _style.FontSize = StylesPanel.Parse(_sizeBox.Text, _style.FontSize * 0.75, 4, 150) * 4.0 / 3.0;
            _style.LineHeight = StylesPanel.Parse(_leadingBox.Text, _style.LineHeight * 0.75, 0, 200) * 4.0 / 3.0;
            _style.Align = _alignCombo.SelectedIndex == 0 ? "left" : _alignCombo.SelectedIndex == 2 ? "right" : "center";
            _style.SpaceBefore = StylesPanel.Parse(_beforeBox.Text, _style.SpaceBefore / PxPerMm, 0, 100) * PxPerMm;
            _style.SpaceAfter = StylesPanel.Parse(_afterBox.Text, _style.SpaceAfter / PxPerMm, 0, 100) * PxPerMm;
            _style.Bold = _boldCheck.IsChecked == true;
            _style.Italic = _italicCheck.IsChecked == true;
            _style.FirstLineIndent = 0;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_style == null) return;
            _preview.Text = _style.Content ?? "***";
            try { _preview.FontFamily = new FontFamily(_style.FontFamily); } catch { }
            _preview.FontSize = Math.Max(6, _style.FontSize);
            _preview.FontWeight = _style.Bold ? FontWeights.Bold : FontWeights.Normal;
            _preview.FontStyle = _style.Italic ? FontStyles.Italic : FontStyles.Normal;
            _preview.HorizontalAlignment = _style.Align == "left" ? HorizontalAlignment.Left
                : _style.Align == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        }

        private void Hook(UIElement element)
        {
            var box = element as TextBox;
            if (box != null) { box.LostKeyboardFocus += delegate { OnEdited(); }; return; }
            var check = element as CheckBox;
            if (check != null) { check.Click += delegate { OnEdited(); }; return; }
            var combo = element as ComboBox;
            if (combo != null) combo.SelectionChanged += delegate { OnEdited(); };
        }

        private void OnEdited()
        {
            if (_syncing || _style == null) return;
            Commit();
            var handler = Changed;
            if (handler != null) handler();
        }

        private static TextBox Small()
        {
            return new TextBox { Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
        }

        private static DockPanel FormRow(string label, UIElement field)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = new TextBlock
            {
                Text = label,
                Width = 150,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            row.Children.Add(field);
            return row;
        }

        private static string Pt(double px) { return (px * 0.75).ToString("0.#", CultureInfo.CurrentCulture); }
        private static string Mm(double px) { return (px / PxPerMm).ToString("0.#", CultureInfo.CurrentCulture); }
    }
}
