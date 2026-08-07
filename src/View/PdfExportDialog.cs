using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Print;

namespace UniversSale.View
{
    /// <summary>Options for the print-ready PDF export (4b-2): bleed and crop
    /// marks. The page format itself comes from « Mise en page ».</summary>
    public class PdfExportDialog : Window
    {
        private readonly TextBox _bleed;
        private readonly CheckBox _marks;
        private readonly CheckBox _guides;
        private readonly ComboBox _profile;
        private bool _accepted;

        private PdfExportDialog(Window owner, bool bookDefaults)
        {
            Title = "PDF prêt à imprimer";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 340 };

            panel.Children.Add(new TextBlock
            {
                Text = "Polices incorporées, texte composé ligne à ligne.\n"
                    + "Fond perdu et traits de coupe pour le BAT imprimeur ;\n"
                    + "laissez 0 sans coche pour un PDF de lecture.",
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var label = new TextBlock
            {
                Text = "Fond perdu (mm) :",
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            _bleed = new TextBox { Text = "0", MaxWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(_bleed);
            panel.Children.Add(row);

            _marks = new CheckBox
            {
                Content = "Traits de coupe",
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = bookDefaults
            };
            panel.Children.Add(_marks);

            _guides = new CheckBox
            {
                Content = "Repères de fond perdu (cadre cyan)",
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(_guides);

            var profileRow = new DockPanel();
            var profileLabel = new TextBlock
            {
                Text = "Profil couleur :",
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(profileLabel, Dock.Left);
            profileRow.Children.Add(profileLabel);
            _profile = new ComboBox { MinWidth = 220 };
            _profile.Items.Add("RVB — lecture à l'écran");
            _profile.Items.Add("CMJN — Coated FOGRA39 (imprimerie)");
            _profile.SelectedIndex = bookDefaults ? 1 : 0;
            _profile.ToolTip = "RVB pour un PDF de relecture ; CMJN FOGRA39 pour le "
                + "fichier remis à l'imprimeur (noir du texte porté par le seul canal N).";
            profileRow.Children.Add(_profile);
            panel.Children.Add(profileRow);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Exporter", IsDefault = true, MinWidth = 90 };
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
            Loaded += delegate { _bleed.Focus(); _bleed.SelectAll(); };
        }

        public static PdfExportOptions Ask(Window owner, string title)
        {
            return Ask(owner, title, false, 0);
        }

        /// <summary>bookDefaults : préréglages imprimerie (CMJN FOGRA39 +
        /// traits de coupe) et fond perdu proposé depuis le gabarit.</summary>
        public static PdfExportOptions Ask(Window owner, string title,
            bool bookDefaults, double bleedMm)
        {
            var dialog = new PdfExportDialog(owner, bookDefaults);
            if (bleedMm > 0)
                dialog._bleed.Text = bleedMm.ToString(CultureInfo.InvariantCulture);
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            double bleed;
            var text = dialog._bleed.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out bleed)
                || bleed < 0) bleed = 0;
            return new PdfExportOptions
            {
                BleedMm = System.Math.Min(20, bleed),
                CropMarks = dialog._marks.IsChecked == true,
                BleedGuides = dialog._guides.IsChecked == true,
                Cmyk = dialog._profile.SelectedIndex == 1,
                Title = title ?? ""
            };
        }
    }
}
