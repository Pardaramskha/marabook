using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>Une question Oui / Non avec UNE case à cocher (1.1.0) —
    /// « Supprimer la catégorie ? [x] Supprimer aussi son modèle ». Rend
    /// null si l'on renonce, sinon l'état de la case.</summary>
    public class ConfirmCheckDialog : Window
    {
        private readonly CheckBox _check;
        private bool _accepted;

        private ConfirmCheckDialog(string title, string message, string checkLabel, bool checkedByDefault)
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16), MinWidth = 340, MaxWidth = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            _check = new CheckBox { Content = new TextBlock { Text = checkLabel, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, IsChecked = checkedByDefault };
            panel.Children.Add(_check);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = Buttons.Text("Supprimer", null, Buttons.Bar, Buttons.Look.Primary);
            ok.MinWidth = 90;
            ok.IsDefault = true;
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = Buttons.Text("Annuler", null, Buttons.Bar, Buttons.Look.Outline);
            cancel.MinWidth = 80;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            Content = panel;
            Opened += delegate { ok.Focus(); };
        }

        /// <summary>Null = renoncé ; sinon la case, cochée ou non.</summary>
        public static async Task<bool?> Ask(Window owner, string title, string message, string checkLabel, bool checkedByDefault)
        {
            var dialog = new ConfirmCheckDialog(title, message, checkLabel, checkedByDefault);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            return dialog._check.IsChecked == true;
        }
    }
}
