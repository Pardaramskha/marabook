using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>Le VOILE du tutoriel (1.1.0, 29/10) : un fond sombre sur
    /// toute la fenêtre, troué aux endroits mis en valeur (la Pile, le rail,
    /// un bouton…) et cerné d'un trait d'accent autour de chaque trou. Il
    /// vit dans la grille de la coquille, au-dessus de tout ; il bloque les
    /// clics (le tuto conduit), la fenêtre du tuto reste une fenêtre à part.
    /// Les cibles sont relues à chaque mise en page : une vue qui apparaît
    /// après la navigation trouve son trou dès qu'elle est posée.</summary>
    public sealed class TutorialVeil : Control
    {
        private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x12, 0x1A));
        private static readonly IBrush Catch = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)); // attrape les clics, invisible
        private readonly List<Func<Control>> _targets = new List<Func<Control>>();
        private readonly Panel _host;
        private List<Rect> _holes = new List<Rect>();

        public TutorialVeil(Panel host)
        {
            _host = host;
            IsHitTestVisible = true;
            host.LayoutUpdated += delegate { if (IsVisible) Refresh(); };
        }

        /// <summary>Les contrôles à mettre en valeur (null ou invisible = ignoré).</summary>
        public void SetTargets(IEnumerable<Func<Control>> targets)
        {
            _targets.Clear();
            if (targets != null) _targets.AddRange(targets);
            Refresh();
        }

        /// <summary>Les trous, dans les coordonnées du voile.</summary>
        public IList<Rect> Holes { get { return _holes; } }

        /// <summary>Un trou s'est-il ouvert pour chaque cible visible ?</summary>
        public int HoleCount { get { return _holes.Count; } }

        private void Refresh()
        {
            var holes = new List<Rect>();
            foreach (var resolve in _targets)
            {
                Control target = null;
                try { target = resolve(); } catch { }
                if (target == null || !target.IsEffectivelyVisible || target.Bounds.Width <= 0 || target.Bounds.Height <= 0) continue;
                var origin = target.TranslatePoint(new Point(0, 0), this);
                if (origin == null) continue;
                var rect = new Rect(origin.Value, target.Bounds.Size).Inflate(4);
                holes.Add(rect);
            }
            var changed = holes.Count != _holes.Count;
            for (var i = 0; !changed && i < holes.Count; i++) if (holes[i] != _holes[i]) changed = true;
            if (!changed) return;
            _holes = holes;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            // Un fond quasi invisible sur TOUT le voile, trous compris : ce qui
            // est mis en valeur se regarde, ne se touche pas (29/10, Rémi) —
            // sans lui, un clic passait par le trou.
            context.FillRectangle(Catch, bounds);
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new RectangleGeometry(bounds));
            foreach (var hole in _holes) group.Children.Add(new RectangleGeometry(hole.Intersect(bounds)));
            context.DrawGeometry(Shade, null, group);
            var pen = new Pen(Chrome.Accent, 2);
            foreach (var hole in _holes) context.DrawRectangle(null, pen, hole.Intersect(bounds), 6, 6);
        }
    }
}
