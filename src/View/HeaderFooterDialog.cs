using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Edits one header/footer line: text with tokens, font, size,
    /// bold/italic, alignment. Vider le texte supprime l'en-tête/le pied.</summary>
    public class HeaderFooterDialog : Window
    {
        private readonly TextBox _text;
        private readonly TextBox _font;
        private readonly TextBox _size;
        private readonly CheckBox _bold;
        private readonly CheckBox _italic;
        private readonly ComboBox _align;
        private bool _accepted;

        private HeaderFooterDialog(Window owner, HeaderFooter initial, bool isHeader)
        {
            Title = isHeader ? "En-tête du document" : "Pied de page du document";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 360 };
            panel.Children.Add(Label("Texte — jetons : {page} (folio), {pages} (total), {titre}, {livre} :"));
            _text = new TextBox { Text = initial == null ? (isHeader ? "" : "{page}") : initial.Text };
            panel.Children.Add(_text);

            panel.Children.Add(Label("Police (vide = police du folio du projet) :"));
            _font = new TextBox { Text = initial == null || initial.FontFamily == null ? "" : initial.FontFamily };
            panel.Children.Add(_font);

            panel.Children.Add(Label("Taille (pt) :"));
            _size = new TextBox
            {
                Text = (initial == null ? 10 : initial.SizePt).ToString(CultureInfo.CurrentCulture),
                MaxWidth = 70,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(_size);

            var styleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _bold = new CheckBox
            {
                Content = "Gras",
                Foreground = Chrome.Ink,
                IsChecked = initial != null && initial.Bold,
                Margin = new Thickness(0, 0, 12, 0)
            };
            _italic = new CheckBox
            {
                Content = "Italique",
                Foreground = Chrome.Ink,
                IsChecked = initial != null && initial.Italic
            };
            styleRow.Children.Add(_bold);
            styleRow.Children.Add(_italic);
            panel.Children.Add(styleRow);

            panel.Children.Add(Label("Alignement (sur la colonne de texte) :"));
            _align = new ComboBox { MaxWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
            _align.Items.Add("Gauche");
            _align.Items.Add("Centré");
            _align.Items.Add("Droite");
            var align = initial == null ? "center" : initial.Align;
            _align.SelectedIndex = align == "left" ? 0 : align == "right" ? 2 : 1;
            panel.Children.Add(_align);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button
            {
                Content = "Annuler",
                IsCancel = true,
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _text.Focus(); _text.SelectAll(); };
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };
        }

        /// <summary>Returns the edited value (empty Text = à supprimer), or
        /// null when cancelled.</summary>
        public static HeaderFooter Edit(Window owner, HeaderFooter initial, bool isHeader)
        {
            var dialog = new HeaderFooterDialog(owner, initial, isHeader);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            double size;
            if (!double.TryParse(dialog._size.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out size) || size < 4)
                size = 10;
            var font = dialog._font.Text.Trim();
            return new HeaderFooter
            {
                Text = dialog._text.Text,
                FontFamily = font.Length == 0 ? null : font,
                SizePt = System.Math.Min(48, size),
                Bold = dialog._bold.IsChecked == true,
                Italic = dialog._italic.IsChecked == true,
                Align = dialog._align.SelectedIndex == 0 ? "left"
                      : dialog._align.SelectedIndex == 2 ? "right" : "center"
            };
        }
    }
}
