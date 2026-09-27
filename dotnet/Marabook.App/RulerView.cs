using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Marabook.App
{
    /// <summary>A centimeter ruler band overlaid on the editing surface.
    /// Horizontal: graduations across the page width. Vertical: graduations
    /// restarting at 0 on EVERY page — jamais de somme cumulative. Fed by the
    /// owner with the on-screen page rectangles (viewport coordinates, zoom
    /// already applied).</summary>
    public class RulerView : Control
    {
        public const double Thickness = 22;

        private readonly bool _vertical;
        private List<Rect> _pages = new List<Rect>();
        private double _pageWidthMm = 210;
        private double _pageHeightMm = 297;

        public RulerView(bool vertical)
        {
            _vertical = vertical;
            IsHitTestVisible = false;
        }

        /// <summary>pages: each visible page's rect in the ruler's own
        /// coordinate space; sizes in mm give the cm scale.</summary>
        public void Update(List<Rect> pages, double pageWidthMm, double pageHeightMm)
        {
            _pages = pages ?? new List<Rect>();
            _pageWidthMm = Math.Max(1, pageWidthMm);
            _pageHeightMm = Math.Max(1, pageHeightMm);
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            var band = _vertical
                ? new Rect(0, 0, Thickness, Bounds.Height)
                : new Rect(0, 0, Bounds.Width, Thickness);
            var bg = new SolidColorBrush(Color.FromArgb(235,
                Chrome.BarBg.Color.R, Chrome.BarBg.Color.G, Chrome.BarBg.Color.B));
            dc.DrawRectangle(bg, new Pen(Chrome.Border, 1), band);
            if (_pages.Count == 0) return;

            var typeface = new Typeface(FontFamily.Default);
            foreach (var page in _pages)
            {
                if (_vertical)
                {
                    // Remise à zéro à CHAQUE page.
                    var pxPerCm = page.Height / (_pageHeightMm / 10);
                    if (pxPerCm < 6) continue;
                    var count = (int)(_pageHeightMm / 10);
                    for (var cm = 0; cm <= count; cm++)
                    {
                        var y = page.Top + cm * pxPerCm;
                        if (y < -20 || y > Bounds.Height + 20) continue;
                        dc.DrawLine(new Pen(Chrome.SoftText, 1),
                            new Point(Thickness - 7, y), new Point(Thickness - 1, y));
                        if (cm > 0 && cm < count)
                        {
                            var label = new FormattedText(cm.ToString(CultureInfo.CurrentCulture),
                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                typeface, 8.5, Chrome.SoftText);
                            var cx = Thickness / 2 - 2;
                            using (dc.PushTransform(Matrix.CreateTranslation(-cx, -y) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(cx, y)))
                                dc.DrawText(label, new Point(cx - label.Width / 2, y - 11));
                        }
                        var half = y + pxPerCm / 2;
                        if (cm < count && half < Bounds.Height + 20)
                            dc.DrawLine(new Pen(Chrome.Border, 1),
                                new Point(Thickness - 4, half), new Point(Thickness - 1, half));
                    }
                }
                else
                {
                    var pxPerCm = page.Width / (_pageWidthMm / 10);
                    if (pxPerCm < 6) continue;
                    var count = (int)(_pageWidthMm / 10);
                    for (var cm = 0; cm <= count; cm++)
                    {
                        var x = page.Left + cm * pxPerCm;
                        if (x < -20 || x > Bounds.Width + 20) continue;
                        dc.DrawLine(new Pen(Chrome.SoftText, 1),
                            new Point(x, Thickness - 7), new Point(x, Thickness - 1));
                        if (cm > 0 && cm < count)
                        {
                            var label = new FormattedText(cm.ToString(CultureInfo.CurrentCulture),
                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                typeface, 8.5, Chrome.SoftText);
                            dc.DrawText(label, new Point(x - label.Width / 2, 2));
                        }
                        var half = x + pxPerCm / 2;
                        if (cm < count)
                            dc.DrawLine(new Pen(Chrome.Border, 1),
                                new Point(half, Thickness - 4), new Point(half, Thickness - 1));
                    }
                    break; // une seule fois : toutes les pages partagent le même axe X
                }
            }
        }
    }
}
