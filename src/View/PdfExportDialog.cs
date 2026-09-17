using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Marabook.Print;

namespace Marabook.View
{
    /// <summary>Options for the print-ready PDF export (4b-2): bleed and crop
    /// marks. The page format itself comes from « Mise en page ».</summary>
    public class PdfExportDialog : Window
    {
        private readonly TextBox _bleed;
        private readonly CheckBox _marks;
        private readonly CheckBox _guides;
        private readonly ComboBox _profile;
        private readonly ComboBox _imposition;
        private readonly string _title;
        private bool _accepted;

        /// <summary>Aperçu du BAT : reçoit les options courantes, produit le
        /// PDF (fichier temporaire) et l'ouvre — retour faux si échec.</summary>
        private readonly Func<PdfExportOptions, bool> _preview;

        private PdfExportDialog(Window owner, bool bookDefaults, string title,
            Func<PdfExportOptions, bool> preview)
        {
            _title = title ?? "";
            _preview = preview;
            Title = "PDF prêt à imprimer";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

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

            // 4b-3 : imposition. Le cahier plie le tirage en livret — deux
            // pages par face, complété à un multiple de 4 ; fond perdu et
            // traits n'ont pas cours (coupe au pli).
            var impositionRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var impositionLabel = new TextBlock
            {
                Text = "Imposition :",
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(impositionLabel, Dock.Left);
            impositionRow.Children.Add(impositionLabel);
            _imposition = new ComboBox { MinWidth = 220 };
            _imposition.Items.Add("Pages — une page par feuille");
            _imposition.Items.Add("Cahier — livret à cheval (2 pages par face)");
            _imposition.SelectedIndex = 0;
            _imposition.ToolTip = "Le cahier s'imprime recto-verso puis se plie en deux :\n"
                + "les faces portent les bonnes paires de pages (complété en\n"
                + "pages blanches à un multiple de 4). Fond perdu et traits de\n"
                + "coupe sont ignorés dans ce mode.";
            _imposition.SelectionChanged += delegate
            {
                var booklet = _imposition.SelectedIndex == 1;
                _bleed.IsEnabled = !booklet;
                _marks.IsEnabled = !booklet;
                _guides.IsEnabled = !booklet;
            };
            impositionRow.Children.Add(_imposition);
            panel.Children.Add(impositionRow);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            if (_preview != null)
            {
                var previewBtn = new Button
                {
                    Content = "Aperçu du BAT…",
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = "Produit le PDF exact (options ci-dessus) dans un fichier\n"
                        + "temporaire et l'ouvre — le BAT, trait pour trait."
                };
                previewBtn.Click += delegate
                {
                    if (!_preview(CurrentOptions()))
                        MessageDialog.Show(this, "Aperçu impossible.", "PDF prêt à imprimer",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                };
                buttons.Children.Add(previewBtn);
            }
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

        /// <summary>Les options telles que réglées à l'instant (export et
        /// aperçu du BAT lisent la même vérité).</summary>
        private PdfExportOptions CurrentOptions()
        {
            double bleed;
            var text = _bleed.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out bleed)
                || bleed < 0) bleed = 0;
            return new PdfExportOptions
            {
                BleedMm = System.Math.Min(20, bleed),
                CropMarks = _marks.IsChecked == true,
                BleedGuides = _guides.IsChecked == true,
                Cmyk = _profile.SelectedIndex == 1,
                Booklet = _imposition.SelectedIndex == 1,
                Title = _title
            };
        }

        public static PdfExportOptions Ask(Window owner, string title)
        {
            return Ask(owner, title, false, 0);
        }

        /// <summary>bookDefaults : préréglages imprimerie (CMJN FOGRA39 +
        /// traits de coupe) et fond perdu proposé depuis le gabarit ;
        /// preview : générateur d'aperçu du BAT (null = pas de bouton).</summary>
        public static PdfExportOptions Ask(Window owner, string title,
            bool bookDefaults, double bleedMm,
            Func<PdfExportOptions, bool> preview = null)
        {
            var dialog = new PdfExportDialog(owner, bookDefaults, title, preview);
            if (bleedMm > 0)
                dialog._bleed.Text = bleedMm.ToString(CultureInfo.InvariantCulture);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            return dialog.CurrentOptions();
        }
    }
}
