using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Marabook.App
{
    /// <summary>L'HABIT DES FENÊTRES DE DIALOGUE (2.0.0, 24/11/2026, Rémi : « une interface
    /// rafraîchie ») — toute fenêtre qui s'ouvre par-dessus Marabook (Préférences, dictionnaire,
    /// nouvel élément, « enregistrer avant de quitter »…) quitte le cadre du système pour celui
    /// du tutoriel : un panneau à coins arrondis (le skin et le réglage « Arrondi des angles »
    /// en décident), une barre de titre qu'on tire pour déplacer la fenêtre (curseur main), la
    /// croix Fermer en haut à droite, Échap qui ferme, et un VOILE sur la fenêtre d'où elle
    /// vient, qui bloque tout ce qui est derrière tant qu'elle est ouverte.
    /// Rien n'est réécrit dans les dialogues : c'est leur GABARIT qui change (Window.Template),
    /// posé ici à leur naissance. Leur contenu, leurs boutons, leurs résultats, leur taille, le
    /// zoom de l'accessibilité et le défilement de Dialogs.Fit restent ce qu'ils étaient.
    /// Ne sont PAS habillées : la fenêtre principale, l'accueil et la fenêtre du tutoriel (qui a
    /// déjà son cadre). Les sélecteurs de fichiers restent ceux du système.</summary>
    internal static class ModalChrome
    {
        /// <summary>La hauteur de la barre de titre, que le cadre du système portait HORS de la fenêtre.</summary>
        public const double TitleHeight = 38;
        public const string CornerKey = "MbDialogCorner", TopCornerKey = "MbDialogTopCorner", GlassKey = "MbDialogGlass";
        /// <summary>LA SILHOUETTE GALBÉE (24/11 au soir) : sous un skin « Museum », les grands dialogues
        /// (Préférences, Sprint) quittent le panneau rectangulaire pour elle. Trois ressources la
        /// commandent, republiées à chaque changement de skin : est-elle là, ne l'est-elle pas, et le
        /// retrait du contenu (le renfoncement du bord droit, les losanges du bord gauche).</summary>
        public const string GalbeKey = "MbDialogGalbe", PlainKey = "MbDialogPlain", MoonKey = "MbDialogMoon";
        /// <summary>La silhouette des grands dialogues : 0 = le panneau droit, 1 = galbée (« Ascension »),
        /// 2 = LA LUNE et son planétarium (Gardien du Feu, Bois Blanc, Assermenté — 24/11 au soir).</summary>
        private static int _shape;
        private static readonly ConditionalWeakTable<Window, GrandParts> Grands = new ConditionalWeakTable<Window, GrandParts>();
        private sealed class GrandParts { public Panel Root; public Border Frame, Veil; public Planetarium Sky; public double Added; }
        private static bool _galbe;
        private static readonly System.Collections.Generic.List<Window> Open = new System.Collections.Generic.List<Window>();
        private static readonly ConditionalWeakTable<Window, TextBlock> Titles = new ConditionalWeakTable<Window, TextBlock>();

        private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x12, 0x1A)); // le voile du tutoriel
        private static readonly AttachedProperty<bool> DimmedProperty = AvaloniaProperty.RegisterAttached<Window, Window, bool>("ModalDimmed");
        private static readonly ConditionalWeakTable<Window, object> Dressed = new ConditionalWeakTable<Window, object>();
        private static readonly ConditionalWeakTable<Window, object> Reserved = new ConditionalWeakTable<Window, object>();
        private static readonly ConditionalWeakTable<Window, Border> Veils = new ConditionalWeakTable<Window, Border>();
        private static readonly ConditionalWeakTable<Window, Window> Owners = new ConditionalWeakTable<Window, Window>();
        private static bool _installed;

        /// <summary>Vrai pour une fenêtre qui prend l'habit (toutes, sauf la principale, l'accueil et le tutoriel).</summary>
        public static bool Applies(Window window)
        {
            return window != null && !(window is MainWindow) && !(window is WelcomeWindow) && !(window is TutorialWindow);
        }

        /// <summary>Au démarrage : toute fenêtre qui naît est habillée — dès qu'elle reçoit son
        /// contenu (le plus tôt possible), au plus tard à l'instant où elle se montre.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            ContentControl.ContentProperty.Changed.AddClassHandler<Window>(delegate(Window window, AvaloniaPropertyChangedEventArgs e) { Dress(window); });
            Visual.IsVisibleProperty.Changed.AddClassHandler<Window>(delegate(Window window, AvaloniaPropertyChangedEventArgs e)
            {
                if (!Applies(window)) return;
                if (window.IsVisible) { Dress(window); Reserve(window); }
                RefreshOwnerOf(window);
            });
            // Échap ferme TOUJOURS : un dialogue qui ne l'a pas déjà pris à son compte se ferme (comme par sa croix).
            InputElement.KeyDownEvent.AddClassHandler<Window>(delegate(Window window, KeyEventArgs e)
            {
                if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !Applies(window)) return;
                e.Handled = true;
                window.Close();
            }, RoutingStrategies.Bubble);
        }

        /// <summary>Les ressources du cadre : ses coins (ceux du skin, fois l'arrondi choisi) et
        /// l'opacité de son fond (1 ; moins sous un skin « Museum », dont les fenêtres sont de verre).</summary>
        public static void Publish(Application app, double corner, double glass, double blur, int shape)
        {
            if (app == null) return;
            app.Resources[CornerKey] = new CornerRadius(corner);
            app.Resources[TopCornerKey] = new CornerRadius(Math.Max(0, corner - 1), Math.Max(0, corner - 1), 0, 0);
            app.Resources[GlassKey] = glass;
            _shape = shape;
            var galbe = shape == 1;
            _galbe = shape != 0;
            app.Resources[GalbeKey] = galbe;
            app.Resources[PlainKey] = shape == 0;
            app.Resources[MoonKey] = shape == 2;
            // Les titres des fenêtres déjà ouvertes suivent le skin (les Préférences, où on le change).
            foreach (var open in new System.Collections.Generic.List<Window>(Open)) if (Grand(open)) { Widen(open); FitGrand(open); }
            foreach (var open in new System.Collections.Generic.List<Window>(Open))
            {
                TextBlock shown;
                if (Titles.TryGetValue(open, out shown)) StyleTitle(open, shown);
            }
            if (Math.Abs(blur - _blur) < 0.01) return;
            _blur = blur;
            // Le réglage a bougé pendant qu'une fenêtre est ouverte (les Préférences, justement) : ce qui est flouté suit.
            foreach (var owner in new System.Collections.Generic.List<Window>(BlurredOwners)) Blur(owner, true);
        }

        // ================================================================ le flou derrière le verre

        private static double _blur;
        private static readonly System.Collections.Generic.List<Window> BlurredOwners = new System.Collections.Generic.List<Window>();
        private static readonly ConditionalWeakTable<Window, Visual> Blurred = new ConditionalWeakTable<Window, Visual>();

        /// <summary>LE FLOU DERRIÈRE LE VERRE (24/11/2026, Rémi : « un backdrop-filter ») — sous un skin
        /// « Museum », une fenêtre de dialogue est de verre : ce qu'on voit au travers, c'est la fenêtre
        /// d'où elle vient. Avalonia ne sait pas flouter « ce qui est derrière un panneau » ; mais ce qui
        /// est derrière est à nous : tant que le dialogue est ouvert, le CONTENU de la fenêtre parente
        /// est flouté (un effet posé sur lui, retiré à la fermeture). Le rayon est celui du skin, ou
        /// celui choisi dans ses options ; 0 = pas de flou, et rien n'est posé hors de ces skins.</summary>
        private static void Blur(Window owner, bool on)
        {
            Visual was;
            if (Blurred.TryGetValue(owner, out was))
            {
                was.Effect = null;
                Blurred.Remove(owner);
            }
            BlurredOwners.Remove(owner);
            if (!on) return;
            BlurredOwners.Add(owner); // retenue même sans flou : il peut venir pendant qu'elle est voilée
            var content = owner.Content as Visual;
            if (content == null || _blur < 0.5 || content.Effect != null) return;
            content.Effect = new BlurEffect { Radius = _blur };
            Blurred.Add(owner, content);
        }

        internal static double BlurOfForProbe(Window owner)
        {
            Visual visual;
            var effect = owner != null && Blurred.TryGetValue(owner, out visual) ? visual.Effect as BlurEffect : null;
            return effect == null ? 0 : effect.Radius;
        }

        private static void Dress(Window window)
        {
            if (!Applies(window)) return;
            object done;
            if (Dressed.TryGetValue(window, out done)) return;
            Dressed.Add(window, string.Empty);
            window.SystemDecorations = SystemDecorations.None;
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            window.Template = new FuncControlTemplate<Window>(Face);
            // Le voile de la fenêtre parente : posé à l'ouverture, levé à la fermeture.
            window.Opened += delegate
            {
                if (!Open.Contains(window)) Open.Add(window);
                var owner = window.Owner as Window;
                if (owner == null) return;
                Owners.Remove(window);
                Owners.Add(window, owner);
                Refresh(owner, null, window);
            };
            window.Closed += delegate
            {
                Open.Remove(window);
                Window owner;
                if (Owners.TryGetValue(window, out owner)) { Owners.Remove(window); Refresh(owner, window, null); }
            };
        }

        /// <summary>La barre de titre vivait hors de la fenêtre : une fenêtre de taille FIXE grandit
        /// de sa hauteur, pour que son contenu garde la place qu'il avait. Une fois par fenêtre, avant
        /// que l'accessibilité ne relève ses dimensions d'origine (Accessibility.Dress).</summary>
        public static void Reserve(Window window)
        {
            if (!Applies(window)) return;
            object done;
            if (Reserved.TryGetValue(window, out done)) return;
            Reserved.Add(window, string.Empty);
            var byHeight = window.SizeToContent == SizeToContent.Height || window.SizeToContent == SizeToContent.WidthAndHeight;
            // Le plafond (MaxHeight) ne bouge pas : c'est le plus souvent celui de l'écran (Dialogs.Fit),
            // et la fenêtre entière — barre comprise, désormais — doit y tenir.
            var cap = double.IsNaN(window.MaxHeight) || double.IsInfinity(window.MaxHeight) ? double.PositiveInfinity : window.MaxHeight;
            var screen = Dialogs.AvailableHeight(window.Owner as Window);
            if (screen > 200 && screen < cap) cap = screen;
            if (!double.IsNaN(window.MinHeight) && window.MinHeight > 0) window.MinHeight = Math.Min(cap, window.MinHeight + TitleHeight);
            if (!byHeight && !double.IsNaN(window.Height)) window.Height = Math.Min(cap, window.Height + TitleHeight);
            Widen(window);
        }

        // ================================================================ le gabarit

        private static Control Face(Window window, INameScope scope)
        {
            var root = new Panel();

            // Le fond : celui que la fenêtre s'est donné (ou que le skin lui a donné), plus ou moins opaque.
            // Un GRAND dialogue (Préférences, Sprint) prend la silhouette galbée sous un skin « Museum ».
            var grand = Grand(window);
            if (grand)
            {
                var shape = new GalbeFrame { Tag = "modal-galbe" };
                shape[!GalbeFrame.FillProperty] = window[!TemplatedControl.BackgroundProperty];
                shape.Bind(GalbeFrame.GlassProperty, new DynamicResourceExtension(GlassKey));
                shape.Bind(Visual.IsVisibleProperty, new DynamicResourceExtension(GalbeKey));
                root.Children.Add(shape);
                // …ou LA LUNE : le planétarium d'abord (il ne se voit que par la fente), la silhouette ensuite.
                // (Dans un Canvas : sa place ne doit rien peser dans la mesure d'un dialogue à la taille de son contenu.)
                var sky = new Planetarium();
                var skyHost = new Canvas { IsHitTestVisible = false };
                skyHost.Children.Add(sky);
                sky.Bind(Visual.IsVisibleProperty, new DynamicResourceExtension(MoonKey));
                root.Children.Add(skyHost);
                var moon = new MoonFrame { Tag = "modal-moon" };
                moon[!MoonFrame.FillProperty] = window[!TemplatedControl.BackgroundProperty];
                moon.Bind(MoonFrame.GlassProperty, new DynamicResourceExtension(GlassKey));
                moon.Bind(Visual.IsVisibleProperty, new DynamicResourceExtension(MoonKey));
                root.Children.Add(moon);
                var made = Parts(window);
                made.Root = root;
                made.Sky = sky;
            }
            var glass = new Border { IsHitTestVisible = false };
            if (grand) glass.Bind(Visual.IsVisibleProperty, new DynamicResourceExtension(PlainKey));
            glass[!Border.BackgroundProperty] = window[!TemplatedControl.BackgroundProperty];
            glass.Bind(Visual.OpacityProperty, new DynamicResourceExtension(GlassKey));
            glass.Bind(Border.CornerRadiusProperty, new DynamicResourceExtension(CornerKey));
            root.Children.Add(glass);

            var frame = new Border { BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), ClipToBounds = true, Tag = "modal-frame" };
            frame.Bind(Border.CornerRadiusProperty, new DynamicResourceExtension(CornerKey));
            if (grand)
            {
                GrandParts parts;
                if (Grands.TryGetValue(window, out parts)) parts.Frame = frame;
            }
            var column = new DockPanel();

            // — La barre de titre : on la tire pour déplacer la fenêtre ; la croix à droite.
            var bar = new Panel { Height = TitleHeight, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Tag = "modal-title" };
            var barBack = new Border { Background = Chrome.BarBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
            barBack.Bind(Visual.OpacityProperty, new DynamicResourceExtension(GlassKey));
            barBack.Bind(Border.CornerRadiusProperty, new DynamicResourceExtension(TopCornerKey));
            if (grand) barBack.Bind(Visual.IsVisibleProperty, new DynamicResourceExtension(PlainKey));
            bar.Children.Add(barBack);
            var barRow = new DockPanel { Margin = new Thickness(14, 0, 6, 0) };
            var close = Buttons.Icon("x-bold", "Fermer (Échap)", Buttons.Compact, Buttons.Look.Calm);
            close.Focusable = false; // le clavier entre dans le dialogue par son contenu, comme avant
            close.VerticalAlignment = VerticalAlignment.Center;
            close.Cursor = new Cursor(StandardCursorType.Arrow);
            close.Tag = "modal-close";
            close.Click += delegate { window.Close(); };
            DockPanel.SetDock(close, Dock.Right);
            barRow.Children.Add(close);
            var title = new TextBlock { Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            Titles.Remove(window);
            Titles.Add(window, title);
            StyleTitle(window, title);
            barRow.Children.Add(title);
            bar.Children.Add(barRow);
            bar.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
                var source = e.Source as Visual;
                for (var visual = source; visual != null && !ReferenceEquals(visual, bar); visual = visual.GetVisualParent() as Visual)
                    if (visual is Button) return;
                window.BeginMoveDrag(e);
            };
            DockPanel.SetDock(bar, Dock.Top);
            column.Children.Add(bar);

            var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
            presenter[!ContentPresenter.ContentProperty] = window[!ContentControl.ContentProperty];
            presenter[!ContentPresenter.ContentTemplateProperty] = window[!ContentControl.ContentTemplateProperty];
            presenter[!ContentPresenter.PaddingProperty] = window[!TemplatedControl.PaddingProperty];
            presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = window[!ContentControl.HorizontalContentAlignmentProperty];
            presenter[!ContentPresenter.VerticalContentAlignmentProperty] = window[!ContentControl.VerticalContentAlignmentProperty];
            presenter.RegisterInNameScope(scope);
            column.Children.Add(new VisualLayerManager { Child = presenter });
            frame.Child = column;
            root.Children.Add(frame);

            // — Les bords d'une fenêtre redimensionnable (le cadre du système n'est plus là pour ça).
            if (window.CanResize) AddGrips(root, window);

            // — Le voile, quand cette fenêtre en a ouvert une autre.
            var veil = new Border { Background = Shade, Tag = "modal-veil" };
            veil.Bind(Border.CornerRadiusProperty, new DynamicResourceExtension(CornerKey));
            veil[!Visual.IsVisibleProperty] = window[!DimmedProperty];
            if (grand)
            {
                // Le voile suit la silhouette : hors d'elle, la fenêtre est transparente.
                EventHandler<AvaloniaPropertyChangedEventArgs> fit = delegate(object sender, AvaloniaPropertyChangedEventArgs e)
                {
                    if (e.Property != Visual.BoundsProperty && e.Property != Visual.IsVisibleProperty) return;
                    FitGrand(window);
                };
                GrandParts held;
                if (Grands.TryGetValue(window, out held)) held.Veil = veil;
                root.PropertyChanged += fit;
                veil.PropertyChanged += fit;
            }
            veil.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; BringChildForward(window); };
            root.Children.Add(veil);
            return root;
        }

        /// <summary>Vrai pour un GRAND dialogue, qui prend la silhouette galbée sous un skin « Museum ».</summary>
        internal static bool Grand(Window window) { return window is PreferencesDialog || window is SprintDialog; }

        private static GrandParts Parts(Window window)
        {
            GrandParts parts;
            if (!Grands.TryGetValue(window, out parts)) { parts = new GrandParts(); Grands.Add(window, parts); }
            return parts;
        }

        /// <summary>La lune déborde à droite du corps : un grand dialogue de largeur FIXE s'élargit
        /// d'autant (dans la limite de l'écran), pour que son contenu garde sa place ; il reprend sa
        /// largeur quand le skin change. Un dialogue à la mesure de son contenu s'élargit de lui-même.</summary>
        private static void Widen(Window window)
        {
            if (!Grand(window)) return;
            var byWidth = window.SizeToContent == SizeToContent.Width || window.SizeToContent == SizeToContent.WidthAndHeight;
            if (byWidth || double.IsNaN(window.Width) || double.IsNaN(window.Height)) return;
            var parts = Parts(window);
            var wanted = _shape == 2 ? MoonFrame.Extra(window.Height) : 0;
            var basis = window.Width - parts.Added;
            var room = Dialogs.AvailableWidth(window.Owner as Window);
            if (room > 200) wanted = Math.Max(0, Math.Min(wanted, room - basis));
            wanted = Math.Round(wanted);
            if (Math.Abs(wanted - parts.Added) < 0.5) return;
            parts.Added = wanted;
            window.Width = basis + wanted;
        }

        /// <summary>Un grand dialogue se règle sur sa silhouette : le retrait de son contenu (le
        /// renfoncement de la silhouette galbée ; toute la lune, à droite du corps), son filet droit
        /// (aucun sous une silhouette), la place du planétarium, la découpe de son voile.</summary>
        private static void FitGrand(Window window)
        {
            GrandParts parts;
            if (!Grands.TryGetValue(window, out parts) || parts.Root == null || parts.Frame == null) return;
            var size = parts.Root.Bounds.Size;
            var padding = new Thickness(0);
            Geometry clip = null;
            if (_shape == 1)
            {
                padding = GalbeFrame.Inset;
                if (size.Height > 0) clip = GalbeFrame.Outline(size, 0);
            }
            else if (_shape == 2 && size.Height > 0)
            {
                var extra = MoonFrame.Extra(size.Height);
                padding = new Thickness(9, 5, Math.Round(extra) + 9, 8);
                var body = MoonFrame.BodyWidth(size);
                if (parts.Sky != null)
                {
                    var k = size.Height / MoonFrame.Reference;
                    Canvas.SetLeft(parts.Sky, Math.Floor(body));
                    Canvas.SetTop(parts.Sky, 0);
                    parts.Sky.Width = Math.Ceiling(140 * k) + 2;
                    parts.Sky.Height = Math.Min(size.Height, Math.Ceiling(545 * k));
                    parts.Sky.Place(body - Math.Floor(body), size.Height);
                }
                clip = MoonFrame.Outline(size, 0);
            }
            else if (_shape == 2) padding = parts.Frame.Padding; // pas encore mesuré : on ne touche à rien
            var line = new Thickness(_shape == 0 ? 1 : 0);
            if (parts.Frame.BorderThickness != line) parts.Frame.BorderThickness = line;
            if (parts.Frame.Padding != padding) parts.Frame.Padding = padding;
            if (parts.Veil != null) parts.Veil.Clip = _shape != 0 && parts.Veil.IsVisible ? clip : null;
        }

        /// <summary>Sonde : ce que la lune a ajouté à la largeur de cette fenêtre.</summary>
        internal static double WidenedForProbe(Window window) { GrandParts parts; return Grands.TryGetValue(window, out parts) ? parts.Added : 0; }

        /// <summary>Le titre de la barre : celui de la fenêtre. Sous un skin « Museum », dans la police
        /// des titres du skin (plus grand sur une silhouette galbée, en capitales sous « Ascension »).</summary>
        private static void StyleTitle(Window window, TextBlock title)
        {
            title.Bind(TextBlock.TextProperty, new Binding("Title") { Source = window, Converter = new Avalonia.Data.Converters.FuncValueConverter<string, string>(Skin.TitleText) });
            var font = Skin.TitleFont;
            if (font == null)
            {
                title.ClearValue(TextBlock.FontFamilyProperty);
                title.FontSize = 13;
                title.FontWeight = FontWeight.SemiBold;
                title.LetterSpacing = 0;
                return;
            }
            var big = _galbe && Grand(window);
            title.FontFamily = font;
            title.FontWeight = FontWeight.Medium;
            title.FontSize = Skin.Caps ? (big ? 16 : 13.5) : (big ? 20 : 16);
            title.LetterSpacing = Skin.Caps ? title.FontSize * 0.05 : 0;
        }

        private static void AddGrips(Panel root, Window window)
        {
            const double Edge = 5, Corner = 12;
            Grip(root, window, WindowEdge.North, StandardCursorType.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, Edge);
            Grip(root, window, WindowEdge.South, StandardCursorType.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, Edge);
            Grip(root, window, WindowEdge.West, StandardCursorType.SizeWestEast, HorizontalAlignment.Left, VerticalAlignment.Stretch, Edge, double.NaN);
            Grip(root, window, WindowEdge.East, StandardCursorType.SizeWestEast, HorizontalAlignment.Right, VerticalAlignment.Stretch, Edge, double.NaN);
            Grip(root, window, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Top, Corner, Corner);
            Grip(root, window, WindowEdge.NorthEast, StandardCursorType.TopRightCorner, HorizontalAlignment.Right, VerticalAlignment.Top, Corner, Corner);
            Grip(root, window, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Bottom, Corner, Corner);
            Grip(root, window, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner, HorizontalAlignment.Right, VerticalAlignment.Bottom, Corner, Corner);
        }

        private static void Grip(Panel root, Window window, WindowEdge edge, StandardCursorType cursor, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
        {
            var grip = new Border { Background = Brushes.Transparent, Cursor = new Cursor(cursor), HorizontalAlignment = horizontal, VerticalAlignment = vertical, Width = width, Height = height, Tag = "modal-grip" };
            grip.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || !window.CanResize) return;
                e.Handled = true;
                window.BeginResizeDrag(edge, e);
            };
            root.Children.Add(grip);
        }

        // ================================================================ le voile

        private static void RefreshOwnerOf(Window window)
        {
            Window owner;
            if (Owners.TryGetValue(window, out owner)) Refresh(owner, null, null);
        }

        /// <summary>Le voile de <paramref name="owner"/> : posé tant qu'une de ses fenêtres habillées est à l'écran.</summary>
        private static void Refresh(Window owner, Window closing, Window opening)
        {
            if (owner == null) return;
            var dimmed = opening != null || TopChild(owner, closing) != null; // à son ouverture, la fenêtre n'est pas encore inscrite chez sa parente
            Blur(owner, dimmed);
            if (Applies(owner)) { owner.SetValue(DimmedProperty, dimmed); return; }
            // La fenêtre principale et l'accueil n'ont pas notre gabarit : le voile va dans leur couche d'ornements.
            Border veil;
            if (!Veils.TryGetValue(owner, out veil))
            {
                if (!dimmed) return;
                veil = new Border { Background = Shade, Tag = "modal-veil" };
                veil.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; BringChildForward(owner); };
                Veils.Add(owner, veil);
                owner.Activated += delegate
                {
                    // Le clavier ne passe pas derrière le voile : la fenêtre qu'il annonce reprend la main.
                    if (Dimmed(owner)) Dispatcher.UIThread.Post(delegate { BringChildForward(owner); }, DispatcherPriority.Input);
                };
            }
            var content = owner.Content as Visual;
            var layer = content == null ? null : AdornerLayer.GetAdornerLayer(content);
            if (layer == null) return;
            if (dimmed)
            {
                if (veil.Parent == null || !layer.Children.Contains(veil))
                {
                    var was = veil.Parent as AdornerLayer;
                    if (was != null) was.Children.Remove(veil);
                    AdornerLayer.SetAdornedElement(veil, content);
                    layer.Children.Add(veil);
                }
            }
            else
            {
                var was = veil.Parent as AdornerLayer;
                if (was != null) was.Children.Remove(veil);
            }
        }

        /// <summary>La dernière fenêtre habillée, visible, ouverte depuis <paramref name="owner"/> ; null s'il n'y en a pas.</summary>
        private static Window TopChild(Window owner, Window closing)
        {
            Window top = null;
            foreach (var child in owner.OwnedWindows)
                if (!ReferenceEquals(child, closing) && child.IsVisible && Applies(child)) top = child;
            return top;
        }

        private static void BringChildForward(Window owner)
        {
            var child = TopChild(owner, null);
            if (child == null) return;
            // Jusqu'au bout de la chaîne : la fenêtre ouverte par la fenêtre ouverte…
            for (var next = TopChild(child, null); next != null; next = TopChild(child, null)) child = next;
            try { child.Activate(); } catch (Exception) { }
        }

        // — Sonde.
        internal static bool Dimmed(Window owner)
        {
            if (owner == null) return false;
            if (Applies(owner)) return owner.GetValue(DimmedProperty);
            Border veil;
            return Veils.TryGetValue(owner, out veil) && veil.Parent != null;
        }
        internal static bool DressedForProbe(Window window) { object done; return window != null && Dressed.TryGetValue(window, out done); }
    }
}
