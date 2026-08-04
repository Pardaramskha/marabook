using System.Windows;
using System.Windows.Controls;

namespace UniversSale.View
{
    /// <summary>Small generic input dialog (new item names, renames).</summary>
    public class InputDialog : Window
    {
        private readonly TextBox _input;
        private bool _accepted;

        private InputDialog(Window owner, string title, string label, string initial)
        {
            Title = title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };

            var caption = new TextBlock
            {
                Text = label,
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(caption);

            _input = new TextBox { Text = initial ?? "", MinWidth = 280 };
            _input.SelectAll();
            panel.Children.Add(_input);

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
            Loaded += delegate { _input.Focus(); };
        }

        /// <summary>Returns the entered text, or null if cancelled or empty.</summary>
        public static string Ask(Window owner, string title, string label, string initial)
        {
            var dialog = new InputDialog(owner, title, label, initial);
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            var text = dialog._input.Text.Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
