using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace UniversSale.View
{
    /// <summary>Small generic dialog asking for a few numbers (custom margins,
    /// custom page size). Values are clamped to [min, max]; returns null on
    /// cancel. Accepts both comma and dot decimals.</summary>
    public class NumbersDialog : Window
    {
        private readonly TextBox[] _boxes;
        private bool _accepted;

        private NumbersDialog(Window owner, string title, string[] labels, double[] initial)
        {
            Title = title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 240 };
            _boxes = new TextBox[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var caption = new TextBlock
                {
                    Text = labels[i],
                    Width = 90,
                    Foreground = Chrome.SoftText,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(caption, Dock.Left);
                row.Children.Add(caption);
                _boxes[i] = new TextBox
                {
                    Text = initial[i].ToString("0.##", CultureInfo.CurrentCulture)
                };
                row.Children.Add(_boxes[i]);
                panel.Children.Add(row);
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _boxes[0].Focus(); _boxes[0].SelectAll(); };
        }

        public static double[] Ask(Window owner, string title, string[] labels,
            double[] initial, double min, double max)
        {
            var dialog = new NumbersDialog(owner, title, labels, initial);
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            var values = new double[labels.Length];
            for (var i = 0; i < values.Length; i++)
            {
                double value;
                if (!double.TryParse(dialog._boxes[i].Text.Trim().Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    value = initial[i];
                values[i] = Math.Max(min, Math.Min(max, value));
            }
            return values;
        }
    }
}
