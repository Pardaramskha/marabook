using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        private static Rect? _anchor;
        private static DispatcherTimer _timer, _hideTimer;
        private static readonly System.Collections.Generic.HashSet<Control> _wired = new System.Collections.Generic.HashSet<Control>();
        public const int HideGraceMs = 200;

        /// <summary>Sonde : une carte est-elle ouverte, et pour quelle fiche ?</summary>
        public static bool IsShowing { get { return _popup != null && _popup.IsOpen; } }
        public static BinderItem Showing { get { return IsShowing ? _target : null; } }

        /// <summary>Un [[lien]] par titre : la fiche du projet qui le porte, sinon rien.
        /// anchor : le rectangle du lien dans host — la carte se pose DESSOUS,
        /// jamais sous le curseur (1/11, Rémi : « le pointeur clignote » — posée
        /// au pointeur, la carte passait sous la souris, la ligne perdait le
        /// pointeur, la carte se fermait, rouvrait… en boucle).</summary>
        public static void Hover(Control host, string title, Rect? anchor = null)
        {
            Hover(host, title == null || Project == null ? null : Project.FindByTitle(title), anchor);
        }

        public static void Hover(Control host, BinderItem target, Rect? anchor = null)
        {
            if (target != null && target.Kind != ItemKind.Sheet) target = null;
            if (target == null) { if (host == _host) Hide(); return; }
            if (target == _target && host == _host) { CancelHide(); return; } // déjà en route, ou déjà montrée
            Hide();
            _host = host;
            _target = target;
            _anchor = anchor;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DelayMs) };
            _timer.Tick += delegate
            {
                _timer.Stop();
                _timer = null;
                if (_host == host && _target == target) Show(host, target);
            };
            _timer.Start();
        }

        /// <summary>Le pointeur quitte la ligne : la carte en route s'annule ;
        /// la carte ouverte attend un court instant — le temps d'y entrer.</summary>
        public static void Leave(Control host)
        {
            if (host != _host) return;
            if (_popup == null) { Hide(); return; }
            if (_hideTimer != null) return;
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideGraceMs) };
            _hideTimer.Tick += delegate { CancelHide(); Hide(); };
            _hideTimer.Start();
        }

        private static void CancelHide()
        {
            if (_hideTimer != null) { _hideTimer.Stop(); _hideTimer = null; }
        }

        public static void Hide()
        {
            CancelHide();
            if (_timer != null) { _timer.Stop(); _timer = null; }
            if (_popup != null)
            {
                try { _popup.IsOpen = false; } catch (Exception) { }
                _popup = null;
            }
            _host = null;
            _target = null;
            _anchor = null;
        }

        /// <summary>Le rectangle d'une plage de texte d'un TextBlock (le lien),
        /// dans ses coordonnées ; null si la mise en page ne le connaît pas.</summary>
        public static Rect? RangeRect(TextBlock block, int start, int length)
        {
            try
            {
                var layout = block.TextLayout;
                if (layout == null || length <= 0) return null;
                Rect? union = null;
                foreach (var rect in layout.HitTestTextRange(start, length))
                    union = union == null ? rect : union.Value.Union(rect);
                return union;
            }
            catch (Exception) { return null; }
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
                IsLightDismissEnabled = false,
                Child = card
            };
            if (_anchor != null)
            {
                // Sous le lien, à gauche ; remontée au-dessus si le bas manque (le positionneur retourne).
                popup.Placement = PlacementMode.AnchorAndGravity;
                popup.PlacementRect = _anchor.Value;
                popup.PlacementAnchor = PopupAnchor.BottomLeft;
                popup.PlacementGravity = PopupGravity.BottomRight;
                popup.VerticalOffset = 6;
            }
            else
            {
                popup.Placement = PlacementMode.BottomEdgeAlignedLeft;
                popup.VerticalOffset = 6;
            }
            card.Margin = new Thickness(0);
            card.PointerEntered += delegate { CancelHide(); };
            card.PointerExited += delegate { if (_popup == popup) Hide(); };
            try { ((ISetLogicalParent)popup).SetParent(host); } catch (Exception) { }
            if (_wired.Add(host)) host.DetachedFromVisualTree += delegate { _wired.Remove(host); if (_host == host) Hide(); };
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
            // Sans premier paragraphe, pas d'information de la fiche (1/11, Rémi) : le titre, le modèle, le portrait.
            var excerpt = Exchange.Encyclopedia.Excerpt(item.Document == null ? "" : item.Document.ToPlainText(), ExcerptChars);
            Avalonia.Media.Imaging.Bitmap portrait = null;
            // La vignette décodée à trois fois sa taille d'affichage (1/11, Rémi : « atrocement pixellisée ») : nette au zoom et sur un écran dense.
            try { portrait = item.ImageId == null ? null : ImageCache.Thumbnail(project.FindImage(item.ImageId), ThumbWidth * 3); }
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
                var picture = new Image { Source = portrait, Stretch = Stretch.UniformToFill, Width = ThumbWidth };
                RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.HighQuality);
                var image = new Border
                {
                    Width = ThumbWidth, MaxHeight = 130, CornerRadius = new CornerRadius(6), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
                    Child = picture
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
