using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UniversSale.View
{
    /// <summary>Le remplaçant maison de MessageBox (batch 43) : la boîte
    /// native Win32 ignorait le thème — celle-ci est une Window ordinaire,
    /// habillée par le style implicite comme tous les dialogues de
    /// l'application. Même signature que les 52 appels existants :
    /// Show(owner, texte, titre, boutons, icône) → MessageBoxResult.
    /// Fermer par la croix ou Échap rend le refus le plus sûr (Non ou
    /// Annuler selon les boutons, OK quand il n'y a que lui).</summary>
    public class MessageDialog : Window
    {
        private MessageBoxResult _result;

        private MessageDialog(Window owner, string text, string caption,
            MessageBoxButton buttons, MessageBoxImage image)
        {
            Title = caption ?? "";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            _result = DismissResult(buttons);

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MaxWidth = 480 };
            var body = new DockPanel();
            var glyph = Glyph(image);
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
            if (buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel)
                row.Children.Add(Choice("OK", MessageBoxResult.OK, true, buttons == MessageBoxButton.OK));
            if (buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel)
            {
                row.Children.Add(Choice("Oui", MessageBoxResult.Yes, true, false));
                row.Children.Add(Choice("Non", MessageBoxResult.No, false, buttons == MessageBoxButton.YesNo));
            }
            if (buttons == MessageBoxButton.OKCancel || buttons == MessageBoxButton.YesNoCancel)
                row.Children.Add(Choice("Annuler", MessageBoxResult.Cancel, false, true));
            panel.Children.Add(row);
            Content = panel;
        }

        /// <summary>Le résultat quand on ferme sans choisir (croix, Échap).</summary>
        private static MessageBoxResult DismissResult(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK: return MessageBoxResult.OK;
                case MessageBoxButton.YesNo: return MessageBoxResult.No;
                default: return MessageBoxResult.Cancel;
            }
        }

        private Button Choice(string label, MessageBoxResult result, bool isDefault, bool isCancel)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 84,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            if (isDefault) button.FontWeight = FontWeights.SemiBold;
            button.Click += delegate { _result = result; Close(); };
            return button;
        }

        /// <summary>La pastille d'icône : couleur de sens (Chrome) + signe.</summary>
        private static FrameworkElement Glyph(MessageBoxImage image)
        {
            string sign;
            Brush brush;
            switch (image)
            {
                case MessageBoxImage.Error: sign = "✕"; brush = Chrome.Danger; break;
                case MessageBoxImage.Warning: sign = "!"; brush = Chrome.Warn; break;
                case MessageBoxImage.Question: sign = "?"; brush = Chrome.Accent; break;
                case MessageBoxImage.Information: sign = "i"; brush = Chrome.Accent; break;
                default: return null;
            }
            return new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = brush,
                Child = new TextBlock
                {
                    Text = sign,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        public static MessageBoxResult Show(Window owner, string text, string caption,
            MessageBoxButton buttons, MessageBoxImage image)
        {
            var dialog = new MessageDialog(owner, text, caption, buttons, image);
            Dialogs.ShowModal(dialog);
            return dialog._result;
        }
    }
}
