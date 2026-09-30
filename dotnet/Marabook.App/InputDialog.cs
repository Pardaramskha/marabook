using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Marabook.App
{
    /// <summary>Le petit dialogue de saisie (nouveaux noms, renommages),
    /// porté de View/InputDialog.cs : asynchrone, comme tout dialogue Avalonia.</summary>
    public class InputDialog : Window
    {
        private readonly TextBox _input;
        private bool _accepted;

        private InputDialog(string title, string label, string initial)
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _input = new TextBox { Text = initial ?? "", MinWidth = 280 };
            _input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { _accepted = true; Close(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            panel.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = Buttons.Text("Valider", null, Buttons.Bar, Buttons.Look.Primary);
            ok.MinWidth = 80;
            ok.IsDefault = true;
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = Buttons.Text("Annuler", null, Buttons.Bar, Buttons.Look.Outline);
            cancel.MinWidth = 80;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok); // validation à droite, principale (30/09)
            panel.Children.Add(buttons);
            Content = panel;
            Opened += delegate { _input.Focus(); _input.SelectAll(); };
        }

        /// <summary>Le texte saisi, ou null si annulé ou vide.</summary>
        public static async Task<string> Ask(Window owner, string title, string label, string initial)
        {
            var dialog = new InputDialog(title, label, initial);
            if (owner != null) await dialog.ShowDialog(owner);
            else
            {
                var done = new TaskCompletionSource<bool>();
                dialog.Closed += delegate { done.TrySetResult(true); };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
                await done.Task;
            }
            if (!dialog._accepted) return null;
            var text = (dialog._input.Text ?? "").Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
