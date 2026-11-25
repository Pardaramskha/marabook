using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>LA PASTILLE QUI GLISSE (skins « Museum », 25/11/2026, Rémi) : quand on change d'onglet,
    /// le CONTENU change au clic, aussitôt, comme avant ; mais le fond de l'onglet — la pastille —
    /// ne saute plus d'un onglet à l'autre : il part de l'onglet quitté et glisse jusqu'au nouveau,
    /// en ease-in-out, en laissant les libellés sur place (de « Projet » à « Personnalisation »
    /// dans les Préférences, d'un onglet du ruban à l'autre…).
    /// Rien n'est ajouté à l'arbre : la pastille EST le cadre (« Bg ») du nouvel onglet, que l'on
    /// déplace et que l'on étire depuis la place de l'ancien par une transformation de rendu ;
    /// son libellé (« Header ») reçoit la transformation inverse, et ne bouge donc pas. Le temps
    /// de la course, l'onglet passe SOUS ses voisins : leurs libellés restent lisibles quand la
    /// pastille les traverse. Animations coupées (Préférences), ou hors « Museum » : rien.</summary>
    internal static class TabSlide
    {
        public const double Milliseconds = 240;
        private static bool _installed;
        private static readonly AttachedProperty<int> RunProperty = AvaloniaProperty.RegisterAttached<TabItem, TabItem, int>("TabSlideRun");

        /// <summary>Vrai quand un changement d'onglet glisse.</summary>
        public static bool Active { get { return Skin.Museum && AppSettings.Animations; } }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            SelectingItemsControl.SelectionChangedEvent.AddClassHandler<TabControl>(delegate(TabControl tabs, SelectionChangedEventArgs e)
            {
                if (!ReferenceEquals(e.Source, tabs) || !Active) return;
                if (e.RemovedItems == null || e.AddedItems == null || e.RemovedItems.Count != 1 || e.AddedItems.Count != 1) return;
                var from = tabs.ContainerFromItem(e.RemovedItems[0]) as TabItem;
                var to = tabs.ContainerFromItem(e.AddedItems[0]) as TabItem;
                if (from == null || to == null || ReferenceEquals(from, to)) return;
                // Après la mise en page : l'onglet choisi passe en gras, sa largeur vient de changer.
                Dispatcher.UIThread.Post(delegate { Slide(from, to); }, DispatcherPriority.Loaded);
            });
        }

        private static Border BandOf(TabItem tab)
        {
            return tab.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return border.Name == "Bg" && ReferenceEquals(border.TemplatedParent, tab); });
        }

        /// <summary>Fait glisser la pastille de <paramref name="from"/> à <paramref name="to"/> (déjà choisi).</summary>
        internal static bool Slide(TabItem from, TabItem to)
        {
            if (!to.IsSelected || !from.IsAttachedToVisualTree() || !to.IsAttachedToVisualTree()) return false;
            var band = BandOf(to);
            var old = BandOf(from);
            var header = band == null ? null : band.Child as Control;
            var top = TopLevel.GetTopLevel(to);
            if (band == null || old == null || header == null || top == null) return false;
            if (band.Bounds.Width < 1 || band.Bounds.Height < 1 || old.Bounds.Width < 1 || old.Bounds.Height < 1) return false;
            var origin = old.TranslatePoint(new Point(0, 0), band);
            if (origin == null) return false;
            // D'où part la pastille, dans le repère du cadre d'arrivée : décalage et étirement.
            var dx = origin.Value.X;
            var dy = origin.Value.Y;
            var sx = old.Bounds.Width / band.Bounds.Width;
            var sy = old.Bounds.Height / band.Bounds.Height;
            var offset = header.Bounds.Position; // la place du libellé dans le cadre
            var run = to.GetValue(RunProperty) + 1;
            to.SetValue(RunProperty, run);
            var bandOrigin = band.RenderTransformOrigin;
            var headerOrigin = header.RenderTransformOrigin;
            var zIndex = to.ZIndex;
            band.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            header.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            to.ZIndex = -1;
            var started = DateTime.Now;
            Action frame = null;
            Action finish = delegate
            {
                band.RenderTransform = null;
                header.RenderTransform = null;
                band.RenderTransformOrigin = bandOrigin;
                header.RenderTransformOrigin = headerOrigin;
                to.ZIndex = zIndex;
            };
            frame = delegate
            {
                if (to.GetValue(RunProperty) != run) return; // une course plus récente a repris la main
                var t = Math.Min(1, (DateTime.Now - started).TotalMilliseconds / Milliseconds);
                if (t >= 1 || !to.IsSelected || !to.IsAttachedToVisualTree()) { finish(); return; }
                var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                var k = 1 - eased; // ce qu'il reste du départ
                var scaleX = 1 + (sx - 1) * k;
                var scaleY = 1 + (sy - 1) * k;
                var moveX = dx * k;
                var moveY = dy * k;
                band.RenderTransform = new MatrixTransform(new Matrix(scaleX, 0, 0, scaleY, moveX, moveY));
                // Le libellé défait ce que son cadre subit : il reste où il est.
                header.RenderTransform = new MatrixTransform(new Matrix(1 / scaleX, 0, 0, 1 / scaleY,
                    (offset.X - moveX) / scaleX - offset.X, (offset.Y - moveY) / scaleY - offset.Y));
                top.RequestAnimationFrame(delegate { frame(); });
            };
            frame();
            return true;
        }

        // — Sonde.
        internal static bool SlidingForProbe(TabItem tab) { var band = tab == null ? null : BandOf(tab); return band != null && band.RenderTransform != null; }
        internal static Point HeaderPlaceForProbe(TabItem tab, Visual relativeTo)
        {
            var band = BandOf(tab);
            var header = band == null ? null : band.Child as Control;
            var at = header == null ? null : header.TranslatePoint(new Point(0, 0), relativeTo);
            return at ?? new Point(double.NaN, double.NaN);
        }
    }
}
