using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>« À propos de Marabook » (réécrit le 01/10) : le nom et la
    /// version, quatre lignes sur ce qu'est le logiciel, la licence — puis,
    /// en petit, les références des ressources embarquées et le lien vers
    /// les sources. Habillée par le style implicite comme les autres
    /// dialogues ; « Fermer » seul.</summary>
    public class AboutDialog : Window
    {
        private AboutDialog(Window owner)
        {
            Title = "À propos de Marabook";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16), Width = 480 };
            panel.Children.Add(new TextBlock
            {
                Text = MainWindow.AppName + " " + MainWindow.AppVersion,
                Foreground = Chrome.Ink,
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            foreach (var paragraph in new[]
            {
                "Logiciel libre et gratuit de la famille Stargazer, par Rémi Escamilla, et grâce au travail de bien d'autres nerds avant lui.",
                "Écrivez, créez et perdez-vous dans vos notes — au moins, elles sont toutes au même endroit, maintenant.",
                "Pas d'IA embarquée, pas de service en ligne.",
                "Licence : GNU GPL v3 ou ultérieure."
            })
                panel.Children.Add(Paragraph(paragraph, 12, Chrome.Ink, 6));

            panel.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 8, 0, 10)
            });

            // Les références : le dépôt des sources, puis les ressources
            // embarquées et leurs licences, en petit.
            panel.Children.Add(Link("Sources et licence complète : " + Updater.RepositoryUrl, Updater.RepositoryUrl));
            panel.Children.Add(Paragraph("Ressources embarquées :", 11, Chrome.SoftText, 2));
            foreach (var line in new[]
            {
                "• Dictionnaire orthographique français « toutes variantes » v7.7 par Olivier R. — licence MPL-2.0 (notice : dict/README_dict_fr.txt)",
                "• Grammalecte 2.3.0, correcteur grammatical par Olivier R. — licence GPL-3.0+ (le source Python livré dans grammalecte/ est le source)",
                "• Python " + Correction.Grammalecte.GrammalecteBridge.EmbeddedPythonVersion + " embeddable, runtime de Grammalecte — licence PSF",
                "• Icônes Phosphor — licence MIT",
                "• Icônes Flaticon — crédit exigé"
            })
                panel.Children.Add(Paragraph(line, 11, Chrome.SoftText, 1));
            var links = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var pair in new[]
            {
                new[] { "grammalecte.net", "https://grammalecte.net/" },
                new[] { "python.org", "https://www.python.org/" },
                new[] { "phosphoricons.com", "https://phosphoricons.com/" },
                new[] { "flaticon.com", "https://www.flaticon.com/" }
            })
            {
                var link = Link(pair[0], pair[1]);
                link.Margin = new Thickness(0, 0, 14, 0);
                links.Children.Add(link);
            }
            panel.Children.Add(links);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 88 };
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            Dialogs.Arrange(buttons, close);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private static TextBlock Paragraph(string text, double size, IBrush brush, double below)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = size,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, below)
            };
        }

        private static TextBlock Link(string label, string url)
        {
            var link = new TextBlock
            {
                Text = label,
                Foreground = Chrome.Accent,
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = TextDecorations.Underline,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ToolTip.SetTip(link, url);
            link.PointerPressed += delegate
            {
                try { AppPlatform.OpenWithShell(url); }
                catch { }
            };
            return link;
        }

        public static void Show(Window owner)
        {
            var _ = Dialogs.ShowModal(new AboutDialog(owner), owner);
        }
    }
}
