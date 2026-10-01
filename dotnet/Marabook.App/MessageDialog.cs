using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    public enum MessageButtons { OK, OKCancel, YesNo, YesNoCancel }

    public enum MessageIcon { None, Information, Question, Warning, Error }

    public enum MessageResult { None, OK, Cancel, Yes, No }

    /// <summary>La boîte de message maison (portée de View/MessageDialog.cs) :
    /// une fenêtre ordinaire, habillée par le thème. Fermer par la croix ou
    /// Échap rend le refus le plus sûr (Non ou Annuler selon les boutons, OK
    /// quand il n'y a que lui). Asynchrone : Avalonia n'a pas de dialogue
    /// bloquant — l'appelant attend le résultat (await).</summary>
    public class MessageDialog : Window
    {
        private MessageResult _result;

        private MessageDialog(string text, string caption, MessageButtons buttons, MessageIcon icon)
        {
            Title = caption ?? "";
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Chrome.RaisedBg;
            _result = DismissResult(buttons);

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MaxWidth = 480 };
            var body = new DockPanel();
            var glyph = Glyph(icon);
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 2, 12, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                body.Children.Add(glyph);
            }
            body.Children.Add(new TextBlock
            {
                Text = text ?? "",
                Foreground = Chrome.Ink,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(body);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            // La validation (OK, Oui) à droite, en principal ; Annuler et Non en
            // contour à sa gauche (règle des dialogues, 30/09).
            Button primary = null;
            switch (buttons)
            {
                case MessageButtons.OK:
                    primary = Choice("OK", MessageResult.OK, true);
                    row.Children.Add(primary);
                    break;
                case MessageButtons.OKCancel:
                    row.Children.Add(Choice("Annuler", MessageResult.Cancel, false));
                    primary = Choice("OK", MessageResult.OK, true);
                    row.Children.Add(primary);
                    break;
                case MessageButtons.YesNo:
                    row.Children.Add(Choice("Non", MessageResult.No, false));
                    primary = Choice("Oui", MessageResult.Yes, true);
                    row.Children.Add(primary);
                    break;
                case MessageButtons.YesNoCancel:
                    row.Children.Add(Choice("Annuler", MessageResult.Cancel, false));
                    row.Children.Add(Choice("Non", MessageResult.No, false));
                    primary = Choice("Oui", MessageResult.Yes, true);
                    row.Children.Add(primary);
                    break;
            }
            Dialogs.Arrange(row, primary);
            panel.Children.Add(row);
            Content = panel;

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
        }

        private Button Choice(string label, MessageResult result, bool primary)
        {
            var button = Buttons.Text(label, null, Buttons.Bar, primary ? Buttons.Look.Primary : Buttons.Look.Outline);
            button.MinWidth = 88;
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsDefault = primary;
            button.Focusable = true;
            button.Click += delegate { _result = result; Close(); };
            return button;
        }

        private static MessageResult DismissResult(MessageButtons buttons)
        {
            switch (buttons)
            {
                case MessageButtons.OK: return MessageResult.OK;
                case MessageButtons.YesNo: return MessageResult.No;
                default: return MessageResult.Cancel;
            }
        }

        private static Control Glyph(MessageIcon icon)
        {
            string name;
            IBrush brush;
            switch (icon)
            {
                case MessageIcon.Warning: name = "warning-fill"; brush = Chrome.Warn; break;
                case MessageIcon.Error: name = "warning-fill"; brush = Chrome.Danger; break;
                case MessageIcon.Question: name = "lifebuoy-bold"; brush = Chrome.Accent; break;
                case MessageIcon.Information: name = "info-bold"; brush = Chrome.Accent; break;
                default: return null;
            }
            if (!Icons.Has(name)) return null;
            return Icons.Make(name, 22, brush);
        }

        /// <summary>Montre la boîte et rend le choix. owner : la fenêtre qui la
        /// possède (centrée dessus) ; null = centrée sur l'écran.</summary>
        public static async Task<MessageResult> Show(Window owner, string text, string caption,
            MessageButtons buttons, MessageIcon icon)
        {
            var dialog = new MessageDialog(text, caption, buttons, icon);
            if (owner != null)
            {
                await dialog.ShowDialog(owner);
                return dialog._result;
            }
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var done = new TaskCompletionSource<MessageResult>();
            dialog.Closed += delegate { done.TrySetResult(dialog._result); };
            dialog.Show();
            return await done.Task;
        }
    }
}
