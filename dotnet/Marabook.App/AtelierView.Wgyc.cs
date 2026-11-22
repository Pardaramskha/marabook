using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>WGYC — « WRITE GOOD YOU CUNT » (2.0.0, 22/11/2026, Rémi) : la
    /// boîte de l'Atelier qui réunit ce qu'il faut savoir de la langue
    /// française pour mieux écrire, ou ne pas se planter en le faisant.
    ///  - le CONJUGUEUR, son premier outil : le tableau d'un verbe, dans la
    ///    page (le même panneau que la fenêtre du menu Outils) ;
    ///  - les LIENS UTILES : des ressources en ligne de référence, d'office
    ///    (le CNRTL, le Wiktionnaire), et celles que l'auteur ajoute. Un lien
    ///    s'ouvre dans le navigateur du système ; seules les adresses http et
    ///    https sont acceptées.</summary>
    public partial class AtelierView
    {
        /// <summary>Les liens d'office : { titre, adresse, ce qu'on y trouve }.</summary>
        internal static readonly string[][] DefaultLinks =
        {
            new[] { "CNRTL", "https://www.cnrtl.fr/", "Le Centre national de ressources textuelles et lexicales : le Trésor de la langue française, l'étymologie, les synonymes et antonymes, la morphologie." },
            new[] { "Wiktionnaire", "https://fr.wiktionary.org/wiki/Wiktionnaire:Page_d%E2%80%99accueil", "Le dictionnaire libre : définitions, prononciations, conjugaisons, traductions, mots rares et régionaux." }
        };

        private StackPanel _links;
        private ConjugatorPanel _conjugator;

        private void BuildWgyc()
        {
            _page.Children.Add(Paragraph("La langue française sous la main : de quoi vérifier un accord, un temps, un sens, avant de l'écrire de travers."));
            var tiles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Tag = "atelier-tools" };
            foreach (var screen in Screens)
                if (screen.Box == BoxWgyc) tiles.Children.Add(Tile(screen));
            _page.Children.Add(tiles);

            _page.Children.Add(Heading("Liens utiles"));
            _page.Children.Add(Paragraph("Les ressources en ligne qu'on rouvre sans cesse. Un clic ouvre la page dans votre navigateur ; ajoutez les vôtres."));
            _links = new StackPanel { Tag = "atelier-links", MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            _page.Children.Add(_links);
            var add = Buttons.IconText("plus-bold", "Ajouter un lien…", "Un site de plus dans cette liste : son nom, son adresse", Buttons.Bar, Buttons.Look.Outline);
            add.Tag = "atelier-link-add";
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 10, 0, 0);
            add.Click += async delegate
            {
                var owner = Ui.OwnerOf(this);
                var title = await InputDialog.Ask(owner, "Ajouter un lien", "Nom du site :", "");
                if (string.IsNullOrWhiteSpace(title)) return;
                var url = await InputDialog.Ask(owner, "Ajouter un lien", "Adresse (https://…) :", "https://");
                if (url == null) return;
                var problem = AddLink(title, url);
                if (problem != null) await MessageDialog.Show(owner, problem, "Liens utiles", MessageButtons.OK, MessageIcon.Warning);
            };
            _page.Children.Add(add);
            RebuildLinks();
        }

        /// <summary>Une adresse qu'on accepte d'ouvrir : http ou https, rien d'autre. null si elle ne convient pas.</summary>
        internal static string CleanUrl(string url)
        {
            var text = (url ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "https://" + text;
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (string.IsNullOrEmpty(uri.Host) || uri.Host.IndexOf('.') < 0) return null;
            return uri.AbsoluteUri;
        }

        /// <summary>Ajoute un lien de l'auteur. null si c'est fait, sinon ce qui cloche.</summary>
        internal string AddLink(string title, string url)
        {
            var name = (title ?? "").Trim();
            var address = CleanUrl(url);
            if (name.Length == 0) return "Un lien a besoin d'un nom.";
            if (address == null) return "« " + (url ?? "").Trim() + " » n'est pas une adresse de site (http:// ou https://).";
            foreach (var link in DefaultLinks) if (string.Equals(link[1], address, StringComparison.OrdinalIgnoreCase)) return "Ce site est déjà dans la liste.";
            foreach (var link in AppSettings.UsefulLinks) if (string.Equals(link[1], address, StringComparison.OrdinalIgnoreCase)) return "Ce site est déjà dans la liste.";
            AppSettings.UsefulLinks.Add(new[] { name, address });
            AppSettings.Save();
            RebuildLinks();
            return null;
        }

        private void RebuildLinks()
        {
            if (_links == null) return;
            _links.Children.Clear();
            foreach (var link in DefaultLinks) _links.Children.Add(LinkRow(link[0], link[1], link[2], null));
            foreach (var link in new List<string[]>(AppSettings.UsefulLinks))
            {
                var mine = link;
                _links.Children.Add(LinkRow(mine[0], mine[1], null, delegate
                {
                    AppSettings.UsefulLinks.Remove(mine);
                    AppSettings.Save();
                    RebuildLinks();
                }));
            }
        }

        private Control LinkRow(string title, string url, string text, Action remove)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            column.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            if (!string.IsNullOrEmpty(text)) column.Children.Add(new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            column.Children.Add(new TextBlock { Text = url, Foreground = Chrome.FaintText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), Tag = "atelier-link-url" });
            row.Children.Add(column);
            var open = Buttons.IconText("magnifying-glass-bold", "Ouvrir", "Ouvre " + url + " dans votre navigateur", Buttons.Bar, Buttons.Look.Outline);
            open.Tag = "atelier-link-open";
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Margin = new Thickness(14, 0, 0, 0);
            open.Click += delegate { OpenLink(url); };
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            if (remove != null)
            {
                var drop = Buttons.Icon("x-bold", "Retirer ce lien de la liste", Buttons.Compact, Buttons.Look.Calm);
                drop.Tag = "atelier-link-remove";
                drop.VerticalAlignment = VerticalAlignment.Center;
                drop.Margin = new Thickness(6, 0, 0, 0);
                drop.Click += delegate { remove(); };
                Grid.SetColumn(drop, 2);
                row.Children.Add(drop);
            }
            return new Border { Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 10, 10), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Child = row, Tag = "atelier-link:" + title };
        }

        /// <summary>Sonde : l'adresse qu'un clic aurait ouverte, sans lancer de navigateur.</summary>
        internal static Action<string> LinkOpener;

        private void OpenLink(string url)
        {
            var address = CleanUrl(url);
            if (address == null) return;
            var opener = LinkOpener;
            if (opener != null) { opener(address); return; }
            try { AppPlatform.OpenWithShell(address); }
            catch (Exception)
            {
                MessageDialog.Show(Ui.OwnerOf(this), "Le navigateur n'a pas pu s'ouvrir. L'adresse : " + address, "Liens utiles", MessageButtons.OK, MessageIcon.Warning);
            }
        }

        // ================================================================ le Conjugueur, dans la page

        private void BuildConjugator()
        {
            var bridge = GrammarBridge == null ? null : GrammarBridge();
            if (_conjugator != null) _conjugator.Cancel();
            _conjugator = new ConjugatorPanel(bridge, null) { MinHeight = 420, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left, Tag = "atelier-conjugator" };
            // Le panneau a son propre défilement : il prend la hauteur de la page.
            _page.Children.Add(_conjugator);
            // La hauteur se prend une fois la page mesurée : au moment où le panneau s'attache, elle ne l'est pas encore.
            var panel = _conjugator;
            Ui.Post(Avalonia.Threading.DispatcherPriority.Loaded, delegate { SizeConjugator(); if (ReferenceEquals(panel, _conjugator)) panel.FocusInput(); });
            if (!_scrollWatched) { _scrollWatched = true; _scroll.SizeChanged += delegate { SizeConjugator(); }; }
        }

        private bool _scrollWatched;

        private void SizeConjugator()
        {
            if (_conjugator == null || _tool != ToolConjugator) return;
            var room = _scroll.Bounds.Height - _page.Margin.Top - _page.Margin.Bottom;
            if (room > 200) _conjugator.Height = room;
        }

        internal ConjugatorPanel ConjugatorForProbe { get { return _conjugator; } }
        internal int LinkRowsForProbe { get { return _links == null ? 0 : _links.Children.Count; } }
    }
}
