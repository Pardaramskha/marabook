using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>La fenêtre « Nouveautés » (02/10) : une version plus récente
    /// est publiée — son nom, la date, les patch notes telles que Rémi les a
    /// écrites (le texte de la release GitHub, du Markdown, rendu par le
    /// moteur des fiches), un lien vers la page de la release, et deux
    /// boutons : « Plus tard » et « Installer et redémarrer ». Ouverte par le
    /// statut cliquable de l'accueil et par Aide › Vérifier les mises à jour
    /// quand il y a plus récent. Elle ne télécharge rien elle-même : le
    /// bouton d'installation rend la main à la coquille (install).</summary>
    public class UpdateNotesDialog : Window
    {
        public UpdateNotesDialog(Window owner, Updater.Info info, string localVersion, bool prepared, Action install)
        {
            Title = "Nouveautés de Marabook " + info.Version;
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };

            // ---- l'en-tête : l'icône, le nom de la version, la date, la nôtre
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var glyph = Icons.Make("file-arrow-down-bold", 30, Chrome.Accent) as Control;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 2, 14, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                head.Children.Add(glyph);
            }
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock
            {
                Text = "Marabook " + info.Version,
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink
            });
            var when = PublishedOn(info.PublishedAt);
            titles.Children.Add(new TextBlock
            {
                Text = (when.Length > 0 ? "Publiée le " + when + " · " : "")
                    + "vous avez la " + localVersion
                    + (prepared ? " · téléchargée, prête à installer" : ""),
                FontSize = 12,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 3, 0, 0)
            });
            head.Children.Add(titles);
            panel.Children.Add(head);

            // ---- les patch notes : une feuille de papier qui défile
            var notes = (info.Notes ?? "").Trim();
            Control body;
            if (notes.Length > 0)
                body = MarkdownRender.Build(notes, null, null);
            else
                body = new TextBlock
                {
                    Text = "Aucune note n'accompagne cette version.",
                    FontSize = 13,
                    Foreground = Chrome.FaintText,
                    FontStyle = FontStyle.Italic
                };
            body.Margin = new Thickness(18, 14, 18, 14);
            panel.Children.Add(new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new ScrollViewer
                {
                    MaxHeight = 380,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = body
                }
            });

            // ---- le lien vers la release
            if (!string.IsNullOrEmpty(info.PageUrl))
            {
                var url = info.PageUrl;
                var link = new TextBlock
                {
                    Text = "Voir la release sur GitHub",
                    Foreground = Chrome.Accent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 12,
                    TextDecorations = TextDecorations.Underline,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                ToolTip.SetTip(link, url);
                link.PointerPressed += delegate
                {
                    try { AppPlatform.OpenWithShell(url); }
                    catch { }
                };
                panel.Children.Add(link);
            }

            // ---- les boutons : Plus tard, Installer et redémarrer (principal, à droite)
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var later = new Button { Content = "Plus tard", IsCancel = true, MinWidth = 88 };
            later.Click += delegate { Close(false); };
            buttons.Children.Add(later);
            var go = new Button { Content = "Installer et redémarrer", IsDefault = true, MinWidth = 88 };
            ToolTip.SetTip(go, prepared
                ? "La mise à jour est déjà téléchargée : Marabook se ferme, remplace ses fichiers (vos projets et réglages restent) et redémarre"
                : "Télécharge la mise à jour, puis Marabook se ferme, remplace ses fichiers (vos projets et réglages restent) et redémarre");
            go.Click += delegate
            {
                Close(true);
                if (install != null) install();
            };
            buttons.Children.Add(go);
            Dialogs.Arrange(buttons, go);
            panel.Children.Add(buttons);
            Content = panel;
        }

        /// <summary>« 2026-10-02T09:12:33Z » → « 02/10/2026 » (heure locale) ; vide si illisible.</summary>
        private static string PublishedOn(string iso)
        {
            DateTime date;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out date))
                return "";
            return Dates.Display(date.ToLocalTime().Date);
        }

        /// <summary>Montre la fenêtre, posée sur owner ; vrai si « Installer »
        /// a été choisi (install a alors déjà été appelé).</summary>
        public static async Task<bool> Show(Window owner, Updater.Info info, string localVersion, bool prepared, Action install)
        {
            var result = await Dialogs.ShowModal(new UpdateNotesDialog(owner, info, localVersion, prepared, install), owner);
            return result == true;
        }
    }
}
