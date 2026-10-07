using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le défilement FLUIDE à la molette (0.50.0, porté de
    /// View/SmoothScroll.cs le 27/09), pour toute la fenêtre : un
    /// gestionnaire de classe en TUNNEL sur ScrollViewer intercepte la
    /// molette et anime l'offset vertical vers sa cible (cubique sortante,
    /// ~180 ms) au lieu de sauter d'un cran ; les crans qui s'enchaînent
    /// prolongent la même course. La vitesse (AppSettings.ScrollSpeed)
    /// multiplie le pas (trois lignes de 16 px). Ne touche pas : Ctrl+molette
    /// (zoom), Maj+molette, les listes qui défilent par élément
    /// (virtualisées : listes, combos) et les vues arrivées en butée —
    /// l'événement suit alors son cours normal, le parent reprend.</summary>
    public static class SmoothScroll
    {
        private const double DurationMs = 180;
        private static bool _installed;
        private static readonly Dictionary<ScrollViewer, Motion> _motions = new Dictionary<ScrollViewer, Motion>();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            // macOS (1.0.4) : le trackpad et la Magic Mouse livrent des dizaines
            // d'événements par seconde, à pas fractionnaires, avec leur propre
            // inertie — relancer une course de 180 ms à chacun rendait l'éditeur
            // et la bibliothèque des fiches pâteux (retour de Rémi, 07/10). Le
            // système défile déjà en douceur : on ne s'interpose pas.
            if (OperatingSystem.IsMacOS()) return;
            InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnPreviewWheel, RoutingStrategies.Tunnel);
        }

        /// <summary>Le pas d'un cran, en px : trois lignes de 16 px (le
        /// réglage courant de Windows), multipliées par la vitesse réglée.</summary>
        public static double StepPx()
        {
            return 3 * 16 * AppSettings.ScrollSpeed;
        }

        private static void OnPreviewWheel(ScrollViewer viewer, PointerWheelEventArgs e)
        {
            if (e.Handled || Math.Abs(e.Delta.Y) < 0.001) return;
            if (Ui.HasCommand(e.KeyModifiers) || (e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0) return;
            // Le tunnel passe d'abord par les ScrollViewer EXTÉRIEURS : seul
            // le plus profond qui peut encore défiler dans ce sens agit — les
            // autres laissent passer (l'intérieur aura son tour, ou le parent
            // reprend quand l'intérieur est en butée).
            var target = InnermostScrollable(e.Source as Visual, e.Delta.Y);
            if (target != viewer) return;
            if (IsLogical(viewer)) return; // défilement par élément : le comportement natif
            Motion motion;
            if (!_motions.TryGetValue(viewer, out motion))
            {
                motion = new Motion(viewer);
                _motions[viewer] = motion;
            }
            var from = motion.Active ? motion.Target : viewer.Offset.Y;
            var wanted = Math.Max(0, Math.Min(ScrollableHeight(viewer), from - e.Delta.Y * StepPx()));
            motion.Start(wanted);
            e.Handled = true;
        }

        private static double ScrollableHeight(ScrollViewer viewer)
        {
            return Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        }

        /// <summary>Un contenu qui défile par élément (ItemsPresenter d'une
        /// liste virtualisée) : l'offset n'est pas en pixels de contenu.</summary>
        private static bool IsLogical(ScrollViewer viewer)
        {
            var presenter = viewer.Presenter;
            var logical = presenter == null ? null : presenter.Child as ILogicalScrollable;
            return logical != null && logical.IsLogicalScrollEnabled;
        }

        /// <summary>Le ScrollViewer le plus profond, depuis la source de la
        /// molette, qui peut encore défiler dans son sens ; null si aucun.</summary>
        private static ScrollViewer InnermostScrollable(Visual source, double delta)
        {
            var node = source;
            while (node != null)
            {
                var viewer = node as ScrollViewer;
                if (viewer != null && CanScroll(viewer, delta)) return viewer;
                node = node.GetVisualParent();
            }
            return null;
        }

        private static bool CanScroll(ScrollViewer viewer, double delta)
        {
            var scrollable = ScrollableHeight(viewer);
            if (scrollable <= 0.5) return false;
            if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) return false;
            Motion motion;
            var offset = _motions.TryGetValue(viewer, out motion) && motion.Active ? motion.Target : viewer.Offset.Y;
            return delta < 0 ? offset < scrollable - 0.5 : offset > 0.5;
        }

        /// <summary>La course en cours d'un ScrollViewer : de l'offset du
        /// départ à la cible, rendue image par image (RequestAnimationFrame
        /// de la fenêtre ; un DispatcherTimer si la vue n'est plus posée).</summary>
        private sealed class Motion
        {
            private readonly ScrollViewer _viewer;
            private double _from;
            private DateTime _started;
            public double Target;
            public bool Active;

            public Motion(ScrollViewer viewer) { _viewer = viewer; }

            public void Start(double target)
            {
                _from = _viewer.Offset.Y;
                Target = target;
                _started = DateTime.Now;
                if (!Active)
                {
                    Active = true;
                    Frame();
                }
            }

            private void Frame()
            {
                var top = TopLevel.GetTopLevel(_viewer);
                if (top == null) { Finish(); return; }
                top.RequestAnimationFrame(delegate { Step(); });
            }

            private void Step()
            {
                var t = Math.Min(1, (DateTime.Now - _started).TotalMilliseconds / DurationMs);
                var eased = 1 - Math.Pow(1 - t, 3); // cubique sortante
                var offset = _from + (Target - _from) * eased;
                _viewer.Offset = new Vector(_viewer.Offset.X, offset);
                if (t >= 1) Finish();
                else Frame();
            }

            private void Finish()
            {
                Active = false;
                _motions.Remove(_viewer);
            }
        }
    }
}
