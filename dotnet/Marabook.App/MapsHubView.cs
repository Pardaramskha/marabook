using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA PAGE DES CARTES (2.0.0, 16/11/2026) : ce qu'ouvre la racine « Cartes »
    /// de la Pile. Une tuile par carte — sa vignette, ses dimensions, son monde, et
    /// SON POIDS dans le .plot (Rémi : « on doit pouvoir savoir quelle est la place
    /// prise par chaque carte ») ; en tête, le poids de toutes.</summary>
    public class MapsHubView : DockPanel
    {
        private readonly WrapPanel _cards;
        private readonly TextBlock _total;
        private readonly Border _emptyBox;
        private Project _project;

        public event Action<BinderItem> OpenRequested;
        public event Action NewMapRequested;
        public event Action AnalyzerRequested;

        public MapsHubView()
        {
            Skin.Ground(this); // Chrome.WindowBg, ou transparent sur le fond d'un skin (21/11)
            Focusable = true;
            var head = new Border { Background = Chrome.BarBgLight, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8, 16, 8) };
            SetDock(head, Dock.Top);
            var row = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            _total = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Tag = "maps-total" };
            right.Children.Add(_total);
            var analyzer = Buttons.Text("Analyseur de .plot", "Ce que pèse chaque partie du projet dans son fichier", Buttons.Compact, Buttons.Look.Outline);
            analyzer.Click += delegate { var h = AnalyzerRequested; if (h != null) h(); };
            right.Children.Add(analyzer);
            var add = Buttons.IconText("plus-bold", "Nouvelle carte", "Importer une image : elle est découpée en tuiles et optimisée à l'import", Buttons.Compact, Buttons.Look.Primary);
            add.Tag = "maps-new";
            add.Click += delegate { var h = NewMapRequested; if (h != null) h(); };
            right.Children.Add(add);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Icons.Make("pile-cartes", 20, Chrome.Accent));
            title.Children.Add(new TextBlock { Text = Project.MapsRootTitle, Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = "Les lieux de votre univers : mondes, pays, villes — tracés, mesurés, liés aux fiches.", Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(title);
            head.Child = row;
            Children.Add(head);

            _cards = new WrapPanel();
            _emptyBox = new Border
            {
                Padding = new Thickness(18, 14, 18, 16), CornerRadius = new CornerRadius(10), BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left, Tag = "maps-empty",
                Child = new TextBlock
                {
                    Text = "Aucune carte pour l'instant.\n\n« Nouvelle carte » importe une image — le planisphère d'un monde, la carte d'un pays, le plan d'une ville. "
                         + "Elle est découpée en tuiles à l'import : même très grande, elle reste fluide, et nette jusqu'au plus près.\n\n"
                         + "Ensuite : tracez des zones, posez des repères, liez-les à vos fiches, posez une échelle (une distance ou un temps de trajet entre deux points) "
                         + "et la carte calcule les superficies. Liée à un monde de l'Astrolabe, elle montre aussi le jour, la nuit et les saisons — à plat ou sur un globe.",
                    Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, FontSize = 12.5
                }
            };
            var page = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            page.Children.Add(_emptyBox);
            page.Children.Add(_cards);
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page });
        }

        public void Load(Project project)
        {
            _project = project;
            Refresh();
        }

        public void Refresh()
        {
            _cards.Children.Clear();
            if (_project == null) return;
            var maps = Maps.All(_project);
            long total = 0;
            foreach (var item in maps)
            {
                total += Maps.Bytes(item.Map);
                _cards.Children.Add(Card(item));
            }
            _emptyBox.IsVisible = maps.Count == 0;
            _total.Text = maps.Count == 0 ? "" : TimeSystem.Count(maps.Count, "carte", "cartes") + "  ·  " + Maps.SizeLabel(total) + " dans le .plot";
        }

        private Control Card(BinderItem item)
        {
            var map = item.Map ?? new MapInfo();
            var body = new StackPanel { Width = 260 };
            // La vignette : le niveau 0 de la pyramide, toute la carte en une tuile.
            var thumb = new Border { Height = 150, Background = Chrome.CanvasBg, CornerRadius = new CornerRadius(7), ClipToBounds = true, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1) };
            var bitmap = map.HasImage ? MapTileCache.For(map).Get(map, 0, 0, 0, true) : null;
            if (bitmap != null) thumb.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            else thumb.Child = new TextBlock { Text = "Sans image", Foreground = Chrome.FaintText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(thumb);
            body.Children.Add(new TextBlock { Text = item.Title, Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 9, 0, 0) });
            var world = Maps.WorldOf(_project, map);
            body.Children.Add(new TextBlock
            {
                Text = (world != null ? (map.WholeWorld ? "Planisphère de « " + world.Name + " »" : "Dans « " + world.Name + " »") : map.WholeWorld ? "Planisphère entier" : "Carte autonome")
                     + (map.View == Maps.ViewGlobe ? "  ·  en globe" : ""),
                Foreground = Chrome.SoftText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
            });
            if (map.HasImage)
            {
                body.Children.Add(new TextBlock
                {
                    Text = map.Width + " × " + map.Height + " px  ·  " + TimeSystem.Count(map.Zones.Count, "zone", "zones") + "  ·  " + TimeSystem.Count(map.Markers.Count, "repère", "repères"),
                    Foreground = Chrome.SoftText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
                });
                var weight = new Border
                {
                    Background = Chrome.AccentTint, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0), Tag = "maps-weight",
                    Child = new TextBlock { Text = Maps.SizeLabel(Maps.Bytes(map)) + " dans le .plot", Foreground = Chrome.AccentStrong, FontSize = 11.5, FontWeight = FontWeight.SemiBold }
                };
                body.Children.Add(weight);
            }
            var card = new Border
            {
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 12, 12, 12),
                Margin = new Thickness(0, 0, 14, 14), Child = body, Cursor = new Cursor(StandardCursorType.Hand), Tag = "maps-card",
                [ToolTip.TipProperty] = "Ouvrir la carte « " + item.Title + " »"
            };
            card.PointerEntered += delegate { card.BorderBrush = Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = Chrome.Border; };
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                var handler = OpenRequested;
                if (handler != null) handler(item);
            };
            return card;
        }

        internal int CardsForProbe { get { return _cards.Children.Count; } }
        internal string TotalForProbe { get { return _total.Text; } }
        internal bool EmptyShownForProbe { get { return _emptyBox.IsVisible; } }
    }
}
