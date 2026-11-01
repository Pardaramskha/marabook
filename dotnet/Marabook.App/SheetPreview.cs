using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'APERÇU D'UNE FICHE AU SURVOL D'UN LIEN (1.1.0, 1/11, Rémi :
    /// « une popup façon Wikipédia avec la photo et le début du Texte libre »,
    /// notée à l'état des lieux du 29/10) : une carte flottante sous la
    /// souris — titre, nom du modèle, les premières lignes du Texte libre
    /// (Encyclopedia.Excerpt), le portrait en vignette à droite. Les lignes à
    /// liens du wiki (SheetWiki.Anchor) et du corps Markdown
    /// (MarkdownRender.Link) l'appellent à chaque mouvement : Hover avec la
    /// fiche sous le pointeur (ou null), Leave quand le pointeur sort. Une
    /// seule carte à la fois, ouverte après 400 ms d'arrêt, jamais sur un
    /// écrit (les fiches seulement).</summary>
    public static class SheetPreview
    {
        /// <summary>Le projet ouvert, posé par SheetWiki.Build : les [[liens]]
        /// du corps ne connaissent que le titre de leur cible.</summary>
        public static Project Project;

        public const int ExcerptChars = 260, ThumbWidth = 96, DelayMs = 400;

        private static Popup _popup;
        private static Control _host;
        private static BinderItem _target;
        private static DispatcherTimer _timer;

        /// <summary>Sonde : une carte est-elle ouverte, et pour quelle fiche ?</summary>
        public static bool IsShowing { get { return _popup != null && _popup.IsOpen; } }
        public static BinderItem Showing { get { return IsShowing ? _target : null; } }

        /// <summary>Un [[lien]] par titre : la fiche du projet qui le porte, sinon rien.</summary>
        public static void Hover(Control host, string title)
        {
            Hover(host, title == null || Project == null ? null : Project.FindByTitle(title));
        }

        public static void Hover(Control host, BinderItem target)
        {
            if (target != null && target.Kind != ItemKind.Sheet) target = null;
            if (target == null) { if (host == _host) Hide(); return; }
            if (target == _target && host == _host) return; // déjà en route, ou déjà montrée
            Hide();
            _host = host;
            _target = target;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DelayMs) };
            _timer.Tick += delegate
            {
                _timer.Stop();
                _timer = null;
                if (_host == host && _target == target) Show(host, target);
            };
            _timer.Start();
        }

        public static void Leave(Control host)
        {
            if (host == _host) Hide();
        }

        public static void Hide()
        {
            if (_timer != null) { _timer.Stop(); _timer = null; }
            if (_popup != null)
            {
                try { _popup.IsOpen = false; } catch (Exception) { }
                _popup = null;
            }
            _host = null;
            _target = null;
        }

        private static void Show(Control host, BinderItem item)
        {
            var project = Project;
            if (project == null) return;
            var card = Card(item, project);
            if (card == null) return;
            var popup = new Popup
            {
                PlacementTarget = host,
                Placement = PlacementMode.Pointer,
                IsLightDismissEnabled = false,
                Child = card
            };
            try { ((ISetLogicalParent)popup).SetParent(host); } catch (Exception) { }
            host.DetachedFromVisualTree += delegate { if (_popup == popup) Hide(); };
            _popup = popup;
            try { popup.IsOpen = true; }
            catch (Exception) { _popup = null; }
        }

        /// <summary>La carte : titre, modèle, extrait (cinq lignes au plus), le
        /// portrait à droite ; null si la fiche n'a ni portrait ni texte (rien
        /// à montrer de plus que le lien). Partagée avec la sonde.</summary>
        public static Border Card(BinderItem item, Project project)
        {
            if (item == null || project == null) return null;
            var excerpt = Exchange.Encyclopedia.Excerpt(item.Document == null ? "" : item.Document.ToPlainText(), ExcerptChars);
            if (excerpt.Length == 0) excerpt = Exchange.Encyclopedia.FieldsSummary(item, project, ExcerptChars); // sans Texte libre : les champs de l'infobox
            Avalonia.Media.Imaging.Bitmap portrait = null;
            try { portrait = item.ImageId == null ? null : ImageCache.Thumbnail(project.FindImage(item.ImageId), ThumbWidth); }
            catch (Exception) { portrait = null; }
            var template = project.TemplateOf(item);
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = string.IsNullOrEmpty((item.Title ?? "").Trim()) ? "Sans titre" : item.Title.Trim(), FontWeight = FontWeight.Bold, FontSize = 13.5, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap });
            if (template != null && !string.IsNullOrEmpty(template.Name)) text.Children.Add(new TextBlock { Text = template.Name, FontSize = 11, Foreground = Chrome.SoftText });
            if (excerpt.Length > 0) text.Children.Add(new TextBlock { Text = excerpt, FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, MaxLines = 6, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(portrait != null ? "*,12," + ThumbWidth : "*") };
            row.Children.Add(text);
            if (portrait != null)
            {
                var image = new Border
                {
                    Width = ThumbWidth, MaxHeight = 130, CornerRadius = new CornerRadius(6), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
                    Child = new Image { Source = portrait, Stretch = Stretch.UniformToFill, Width = ThumbWidth }
                };
                Grid.SetColumn(image, 2);
                row.Children.Add(image);
            }
            return new Border
            {
                Width = 360,
                Background = Chrome.FieldBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 12),
                Margin = new Thickness(12, 16, 0, 0),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 14, Color = Color.FromArgb(0x38, 0, 0, 0) }),
                Child = row
            };
        }
    }
}
