using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Marabook.View
{
    /// <summary>Un toast À BOUTONS (18/09) : la carte glisse depuis la droite
    /// comme celle des succès, mais reste jusqu'à ce qu'on réponde — le
    /// rapport d'un arrêt brutal et sa sauvegarde de secours. Posé dans un
    /// panneau hôte (bas droite d'une fenêtre) ; répondre le retire.</summary>
    public static class NoticeToast
    {
        public static Border Build(string icon, string title, string body,
            string primaryLabel, Action primary, string secondaryLabel, Action secondary)
        {
            var row = new DockPanel();
            var glyph = Icons.Make(icon, 28, Chrome.Accent) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 2, 12, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                row.Children.Add(glyph);
            }
            var text = new StackPanel { MaxWidth = 340 };
            text.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Chrome.Ink,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Border toast = null;
            var yes = Buttons.Text(primaryLabel, null, Buttons.Bar, Buttons.Look.Primary);
            yes.Click += delegate { Remove(toast); if (primary != null) primary(); };
            buttons.Children.Add(yes);
            if (!string.IsNullOrEmpty(secondaryLabel))
            {
                var no = Buttons.Text(secondaryLabel, null, Buttons.Bar, Buttons.Look.Calm);
                no.Margin = new Thickness(8, 0, 0, 0);
                no.Click += delegate { Remove(toast); if (secondary != null) secondary(); };
                buttons.Children.Add(no);
            }
            text.Children.Add(buttons);
            row.Children.Add(text);
            toast = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0,
                Child = row,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.3,
                    BlurRadius = 14,
                    ShadowDepth = 2
                },
                RenderTransform = new TranslateTransform(60, 0)
            };
            return toast;
        }

        /// <summary>Pose le toast dans son hôte et le fait glisser.</summary>
        public static void Show(Panel host, Border toast)
        {
            if (host == null || toast == null) return;
            host.Children.Add(toast);
            var appear = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
            var slide = new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            toast.BeginAnimation(UIElement.OpacityProperty, appear);
            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        private static void Remove(Border toast)
        {
            var host = toast == null ? null : toast.Parent as Panel;
            if (host != null) host.Children.Remove(toast);
        }
    }
}
