using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UniversSale.View
{
    /// <summary>Custom color picker: a base grid, RGB sliders and a hex box,
    /// live preview. Returns "#RRGGBB" or null. Chosen colors are stored per
    /// project (Project.CustomColors) by the caller.</summary>
    public class ColorDialog : Window
    {
        private readonly Border _preview;
        private readonly TextBox _hexBox;
        private readonly Slider _r, _g, _b;
        private bool _accepted, _syncing;

        private ColorDialog(Window owner)
        {
            Title = "Nouvelle couleur";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };

            // Base grid.
            var grid = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] bases =
            {
                "#000000", "#404040", "#7F8C8D", "#BDC3C7", "#FFFFFF",
                "#C0392B", "#E74C3C", "#E67E22", "#F1C40F", "#C9A227",
                "#27AE60", "#2ECC71", "#16A085", "#1ABC9C", "#2980B9",
                "#3498DB", "#5B67D8", "#8E44AD", "#9B59B6", "#703C2F",
                "#FFF3A3", "#FFD9A8", "#D3F8D3", "#D0E8FF", "#FFD6E7"
            };
            foreach (var hex in bases)
            {
                var swatch = new Button
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    ToolTip = hex
                };
                var hexRef = hex;
                swatch.Click += delegate { SetColor(hexRef); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            _r = MakeSlider(panel, "R");
            _g = MakeSlider(panel, "V");
            _b = MakeSlider(panel, "B");

            var hexRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _preview = new Border
            {
                Width = 46,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(_preview, Dock.Right);
            hexRow.Children.Add(_preview);
            _hexBox = new TextBox { Text = "#5B67D8" };
            _hexBox.TextChanged += delegate { if (!_syncing) SyncFromHex(); };
            hexRow.Children.Add(_hexBox);
            panel.Children.Add(hexRow);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Ajouter", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            SetColor("#5B67D8");
        }

        private Slider MakeSlider(StackPanel panel, string label)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var caption = new TextBlock
            {
                Text = label,
                Width = 18,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var slider = new Slider { Minimum = 0, Maximum = 255, SmallChange = 1 };
            slider.ValueChanged += delegate { if (!_syncing) SyncFromSliders(); };
            row.Children.Add(slider);
            panel.Children.Add(row);
            return slider;
        }

        private void SetColor(string hex)
        {
            _syncing = true;
            var color = FlowConverter.ParseColor(hex);
            _hexBox.Text = FlowConverter.ColorToHex(color);
            _r.Value = color.R;
            _g.Value = color.G;
            _b.Value = color.B;
            _preview.Background = new SolidColorBrush(color);
            _syncing = false;
        }

        private void SyncFromSliders()
        {
            _syncing = true;
            var color = Color.FromRgb((byte)_r.Value, (byte)_g.Value, (byte)_b.Value);
            _hexBox.Text = FlowConverter.ColorToHex(color);
            _preview.Background = new SolidColorBrush(color);
            _syncing = false;
        }

        private void SyncFromHex()
        {
            var text = _hexBox.Text.Trim();
            if (!text.StartsWith("#")) text = "#" + text;
            if (text.Length != 7) return;
            _syncing = true;
            var color = FlowConverter.ParseColor(text);
            _r.Value = color.R;
            _g.Value = color.G;
            _b.Value = color.B;
            _preview.Background = new SolidColorBrush(color);
            _syncing = false;
        }

        public static string Ask(Window owner)
        {
            var dialog = new ColorDialog(owner);
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            var text = dialog._hexBox.Text.Trim();
            if (!text.StartsWith("#")) text = "#" + text;
            return text.Length == 7 ? text.ToUpperInvariant() : null;
        }
    }
}
