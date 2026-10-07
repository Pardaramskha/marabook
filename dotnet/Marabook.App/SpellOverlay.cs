using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Correction;

namespace Marabook.App
{
    /// <summary>L'ondulé rouge sous les mots inconnus d'une ZONE DE TEXTE
    /// (1.0.4 : le corps Markdown des fiches). Un TextBox d'Avalonia ne
    /// souligne rien : ce contrôle, posé PAR-DESSUS lui dans la même case de
    /// grille et transparent aux clics, demande au TextLayout de son
    /// présentateur les rectangles de chaque signalement et y dessine la
    /// vague — rafraîchi à chaque disposition (frappe, défilement, zoom,
    /// redimensionnement). Rien à dessiner : il ne coûte rien.</summary>
    public sealed class SpellOverlay : Control
    {
        private readonly TextBox _box;
        private TextPresenter _presenter;
        private ScrollViewer _scroll;
        private List<Finding> _findings = new List<Finding>();
        private static readonly Pen Wave = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0x3B, 0x3B)), 1.2);

        public SpellOverlay(TextBox box)
        {
            _box = box;
            IsHitTestVisible = false;
            ClipToBounds = true;
            _box.LayoutUpdated += delegate { InvalidateVisual(); };
            _box.TemplateApplied += delegate { Hook(); InvalidateVisual(); };
        }

        private void Hook()
        {
            if (_presenter != null) return;
            _presenter = _box.FindDescendantOfType<TextPresenter>();
            var scroll = _box.FindDescendantOfType<ScrollViewer>();
            if (scroll != null && scroll != _scroll)
            {
                _scroll = scroll;
                _scroll.ScrollChanged += delegate { InvalidateVisual(); };
            }
        }

        public IReadOnlyList<Finding> Findings { get { return _findings; } }

        public void SetFindings(List<Finding> findings)
        {
            _findings = findings ?? new List<Finding>();
            InvalidateVisual();
        }

        /// <summary>Le signalement sous un point de la zone (coordonnées du
        /// TextBox), ou null — pour le menu contextuel.</summary>
        public Finding FindingAt(Point pointInBox)
        {
            Hook();
            if (_presenter == null || _presenter.TextLayout == null) return null;
            var inPresenter = _box.TranslatePoint(pointInBox, _presenter);
            if (inPresenter == null) return null;
            var hit = _presenter.TextLayout.HitTestPoint(inPresenter.Value);
            if (!hit.IsInside) return null;
            var position = hit.TextPosition;
            foreach (var finding in _findings)
                if (position >= finding.Start && position < finding.Start + finding.Length) return finding;
            return null;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_findings.Count == 0) return;
            Hook();
            if (_presenter == null || _presenter.TextLayout == null) return;
            var layout = _presenter.TextLayout;
            var textLength = (_box.Text ?? "").Length;
            foreach (var finding in _findings)
            {
                if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > textLength) continue;
                foreach (var rect in layout.HitTestTextRange(finding.Start, finding.Length))
                {
                    if (rect.Width <= 0) continue;
                    var origin = _presenter.TranslatePoint(new Point(rect.X, rect.Bottom), this);
                    if (origin == null) continue;
                    DrawWave(context, origin.Value.X, origin.Value.Y - 1.5, rect.Width);
                }
            }
        }

        /// <summary>Un zigzag d'amplitude 1,5 px, pas de 3 px.</summary>
        private static void DrawWave(DrawingContext context, double x, double y, double width)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x, y), false);
                var up = true;
                for (var dx = 3.0; dx <= width + 2.9; dx += 3)
                {
                    g.LineTo(new Point(x + Math.Min(dx, width), up ? y - 1.5 : y + 1.5));
                    up = !up;
                }
                g.EndFigure(false);
            }
            context.DrawGeometry(null, Wave, geometry);
        }
    }
}
