using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Marabook.View
{
    /// <summary>Une petite fenêtre d'information (18/09) : un titre, des
    /// paragraphes, un lien vers une page web, « Fermer ». Habillée par le
    /// style implicite comme MessageDialog ; le lien s'ouvre dans le
    /// navigateur du système.</summary>
    public class InfoDialog : Window
    {
        private InfoDialog(Window owner, string caption, string[] paragraphs, string linkLabel, string url)
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

            var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16), MaxWidth = 460 };
            var body = new DockPanel();
            var glyph = Icons.Make("info-bold", 22, Chrome.Accent) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 2, 12, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                body.Children.Add(glyph);
            }
            var texts = new StackPanel();
            foreach (var paragraph in paragraphs ?? new string[0])
                texts.Children.Add(new TextBlock
                {
                    Text = paragraph,
                    Foreground = Chrome.Ink,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            if (!string.IsNullOrEmpty(url))
            {
                var link = new Hyperlink(new Run(linkLabel ?? url))
                {
                    Foreground = Chrome.Accent,
                    Cursor = Cursors.Hand,
                    ToolTip = url
                };
                link.Click += delegate
                {
                    try { System.Diagnostics.Process.Start(url); }
                    catch { }
                };
                texts.Children.Add(new TextBlock(link) { FontSize = 13, TextWrapping = TextWrapping.Wrap });
            }
            body.Children.Add(texts);
            panel.Children.Add(body);

            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            close.Click += delegate { Close(); };
            panel.Children.Add(close);
            Content = panel;
        }

        public static void Show(Window owner, string caption, string[] paragraphs, string linkLabel, string url)
        {
            Dialogs.ShowModal(new InfoDialog(owner, caption, paragraphs, linkLabel, url));
        }

        /// <summary>Le « i » de la barre du texte libre d'une fiche.</summary>
        public static void ShowMarkdownHelp(Window owner)
        {
            Show(owner, "Le texte libre", new[]
            {
                "Le texte libre accepte et lit le markdown, un langage de mise en forme "
                + "textuelle simple et efficace.",
                "La police est déterminée automatiquement dans le mode Vue Wiki — libre à vous "
                + "d'écrire votre propre wiki.",
                "Utilisez les fonctions de prévisualisation pour voir où vous en êtes, ou lisez "
                + "une brève documentation sur le markdown :"
            }, "docs.framasoft.org — le markdown en bref", "https://docs.framasoft.org/fr/grav/markdown.html");
        }
    }
}
