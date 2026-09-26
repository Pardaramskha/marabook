using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>Le défilement FLUIDE à la molette (0.50.0), pour toute la
    /// fenêtre : un gestionnaire de classe sur ScrollViewer intercepte la
    /// molette et anime l'offset vertical vers sa cible (cubique sortante,
    /// ~180 ms) au lieu de sauter d'un cran ; les crans qui s'enchaînent
    /// prolongent la même course. La vitesse (AppSettings.ScrollSpeed)
    /// multiplie le pas de Windows. Ne touche pas : Ctrl+molette (zoom),
    /// Maj+molette, les listes qui défilent par élément (Pile, listes,
    /// combos) et les vues arrivées en butée — l'événement remonte alors au
    /// parent, comme avant.</summary>
    public static class SmoothScroll
    {
        private const double DurationMs = 180;
        private static bool _installed;
        private static readonly Dictionary<ScrollViewer, Motion> _motions = new Dictionary<ScrollViewer, Motion>();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel));
        }

        /// <summary>Le pas d'un cran, en px : les lignes de Windows × 16 px,
        /// multipliées par la vitesse réglée.</summary>
        public static double StepPx()
        {
            var lines = Math.Max(1, SystemParameters.WheelScrollLines);
            return lines * 16 * AppSettings.ScrollSpeed;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
            var viewer = sender as ScrollViewer;
            if (viewer == null) return;
            // Le tunnel passe d'abord par les ScrollViewer EXTÉRIEURS : seul
            // le plus profond qui peut encore défiler dans ce sens agit — les
            // autres laissent passer (l'intérieur aura son tour, ou le parent
            // reprend quand l'intérieur est en butée).
            var target = InnermostScrollable(e.OriginalSource as DependencyObject, e.Delta);
            if (target != viewer) return;
            if (viewer.CanContentScroll) return; // défilement par élément : le comportement natif
            Motion motion;
            if (!_motions.TryGetValue(viewer, out motion))
            {
                motion = new Motion(viewer);
                _motions[viewer] = motion;
            }
            var from = motion.Active ? motion.Target : viewer.VerticalOffset;
            var wanted = Math.Max(0, Math.Min(viewer.ScrollableHeight, from - e.Delta / 120.0 * StepPx()));
            motion.Start(wanted);
            e.Handled = true;
        }

        /// <summary>Le ScrollViewer le plus profond, depuis la source du clic,
        /// qui peut encore défiler dans le sens de la molette ; null si aucun.</summary>
        private static ScrollViewer InnermostScrollable(DependencyObject source, int delta)
        {
            var node = source;
            while (node != null)
            {
                var viewer = node as ScrollViewer;
                if (viewer != null && CanScroll(viewer, delta)) return viewer;
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }

        private static bool CanScroll(ScrollViewer viewer, int delta)
        {
            if (viewer.ScrollableHeight <= 0.5) return false;
            if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) return false;
            Motion motion;
            var offset = _motions.TryGetValue(viewer, out motion) && motion.Active ? motion.Target : viewer.VerticalOffset;
            return delta < 0 ? offset < viewer.ScrollableHeight - 0.5 : offset > 0.5;
        }

        /// <summary>La course en cours d'un ScrollViewer : de l'offset du
        /// départ à la cible, rendue image par image.</summary>
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
                _from = _viewer.VerticalOffset;
                Target = target;
                _started = DateTime.Now;
                if (!Active)
                {
                    Active = true;
                    CompositionTarget.Rendering += OnRendering;
                }
            }

            private void OnRendering(object sender, EventArgs e)
            {
                var t = Math.Min(1, (DateTime.Now - _started).TotalMilliseconds / DurationMs);
                var eased = 1 - Math.Pow(1 - t, 3); // cubique sortante
                var offset = _from + (Target - _from) * eased;
                _viewer.ScrollToVerticalOffset(offset);
                if (t >= 1)
                {
                    Active = false;
                    CompositionTarget.Rendering -= OnRendering;
                    _motions.Remove(_viewer);
                }
            }
        }
    }
}
