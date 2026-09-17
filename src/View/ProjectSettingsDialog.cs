using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Project settings: author (also used by the compiler's title
    /// page) and the scene separator (text, font, size in points) inserted by
    /// the ⁂ button. Returns true when something changed.</summary>
    public class ProjectSettingsDialog : Window
    {
        private readonly Project _project;
        private readonly TextBox _authorBox, _separatorBox, _sizeBox;
        private readonly ComboBox _fontCombo;
        private bool _accepted;

        private const string BodyFontLabel = "(police du corps de texte)";

        private ProjectSettingsDialog(Window owner, Project project)
        {
            _project = project;

            Title = "Paramètres du projet";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 360 };

            panel.Children.Add(Label("Auteur (page de titre de la compilation) :"));
            _authorBox = new TextBox { Text = project.Author ?? "" };
            panel.Children.Add(_authorBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Séparateur de scène (bouton ⁂ de la barre de format)",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 4)
            });

            panel.Children.Add(Label("Texte :"));
            _separatorBox = new TextBox { Text = project.SeparatorText ?? "***" };
            panel.Children.Add(_separatorBox);

            panel.Children.Add(Label("Police :"));
            _fontCombo = new ComboBox();
            _fontCombo.Items.Add(BodyFontLabel);
            foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies)
                _fontCombo.Items.Add(family.Source);
            _fontCombo.SelectedItem = project.SeparatorFont ?? BodyFontLabel;
            if (_fontCombo.SelectedItem == null) _fontCombo.SelectedIndex = 0;
            panel.Children.Add(_fontCombo);

            panel.Children.Add(Label("Taille (points) :"));
            _sizeBox = new TextBox
            {
                Text = project.SeparatorSizePt.ToString("0.#", CultureInfo.CurrentCulture),
                Width = 70,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(_sizeBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _authorBox.Focus(); };
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 6, 0, 3)
            };
        }

        public static bool Show(Window owner, Project project)
        {
            var dialog = new ProjectSettingsDialog(owner, project);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return false;

            var changed = false;
            var author = dialog._authorBox.Text.Trim();
            if (author != (project.Author ?? "")) { project.Author = author; changed = true; }

            var separator = dialog._separatorBox.Text.Trim();
            if (separator.Length == 0) separator = "***";
            if (separator != project.SeparatorText) { project.SeparatorText = separator; changed = true; }

            var font = dialog._fontCombo.SelectedItem as string;
            if (font == BodyFontLabel) font = null;
            if (font != project.SeparatorFont) { project.SeparatorFont = font; changed = true; }

            double size;
            if (double.TryParse(dialog._sizeBox.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            {
                size = Math.Max(4, Math.Min(96, size));
                if (Math.Abs(size - project.SeparatorSizePt) > 0.01)
                {
                    project.SeparatorSizePt = size;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
