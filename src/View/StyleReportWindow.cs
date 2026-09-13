using System;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Correction;

namespace UniversSale.View
{
    /// <summary>La fenêtre du BILAN DE STYLE (b45) : les sections racontées
    /// par StyleReport.Narrate, en phrases simples — un point fort se voit
    /// à sa pastille verte. Rien à régler ici : c'est un compte rendu.</summary>
    public class StyleReportWindow : Window
    {
        private StyleReportWindow(Window owner, string title, StyleReport report)
        {
            Title = "Bilan de style — " + title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 640;
            Height = 620;
            MinWidth = 480;
            MinHeight = 400;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.Ink
            });
            head.Children.Add(new TextBlock
            {
                Text = "Des mesures, pas des notes : chaque ligne dit ce qu'on a compté et où regarder.",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 2, 0, 10)
            });
            root.Children.Add(head);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var copy = Buttons.Text("Copier le bilan", "Copie le texte du bilan dans le presse-papiers",
                Buttons.Compact, Buttons.Look.Outline);
            copy.Margin = new Thickness(0, 0, 8, 0);
            copy.Click += delegate
            {
                try { Clipboard.SetText(report.ToText()); }
                catch { }
            };
            buttons.Children.Add(copy);
            var close = Buttons.Text("Fermer", null, Buttons.Compact, Buttons.Look.Primary);
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var section in report.Narrate())
            {
                var titleRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 3)
                };
                titleRow.Children.Add(ComposedRenderer.FindingDot(
                    section.Good ? ComposedRenderer.FindingPen(FindingCategory.Style)
                        : ComposedRenderer.FindingPen(FindingCategory.Typography), 8));
                titleRow.Children.Add(new TextBlock
                {
                    Text = section.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Chrome.Ink,
                    VerticalAlignment = VerticalAlignment.Center
                });
                list.Children.Add(titleRow);
                foreach (var line in section.Lines)
                    list.Children.Add(new TextBlock
                    {
                        Text = line,
                        FontSize = 12,
                        Foreground = Chrome.Ink,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14, 1, 0, 2)
                    });
            }
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            });
            Content = root;
        }

        public static void Show(Window owner, string title, StyleReport report)
        {
            Dialogs.ShowModal(new StyleReportWindow(owner, title, report));
        }
    }
}
