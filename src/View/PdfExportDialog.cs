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
        private bool _accepted;

        private PdfExportDialog(Window owner)
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
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(_marks);

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
            var dialog = new PdfExportDialog(owner);
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
                Title = title ?? ""
            };
        }
    }
}
