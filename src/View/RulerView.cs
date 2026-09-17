using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Marabook.View
{
    /// <summary>A centimeter ruler band overlaid on the editing surface.
    /// Horizontal: graduations across the page width. Vertical: graduations
    /// restarting at 0 on EVERY page — jamais de somme cumulative. Fed by the
    /// owner with the on-screen page rectangles (viewport coordinates, zoom
    /// already applied).</summary>
    public class RulerView : FrameworkElement
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

        protected override void OnRender(DrawingContext dc)
        {
            var band = _vertical
                ? new Rect(0, 0, Thickness, ActualHeight)
                : new Rect(0, 0, ActualWidth, Thickness);
            var bg = new SolidColorBrush(Color.FromArgb(235,
                Chrome.BarBg.Color.R, Chrome.BarBg.Color.G, Chrome.BarBg.Color.B));
            dc.DrawRectangle(bg, new Pen(Chrome.Border, 1), band);
            if (_pages.Count == 0) return;

            var typeface = new Typeface("Segoe UI");
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
                        if (y < -20 || y > ActualHeight + 20) continue;
                        dc.DrawLine(new Pen(Chrome.SoftText, 1),
                            new Point(Thickness - 7, y), new Point(Thickness - 1, y));
                        if (cm > 0 && cm < count)
                        {
                            var label = new FormattedText(cm.ToString(CultureInfo.CurrentCulture),
                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                typeface, 8.5, Chrome.SoftText, 1.0);
                            dc.PushTransform(new RotateTransform(-90, Thickness / 2 - 2, y));
                            dc.DrawText(label, new Point(Thickness / 2 - 2 - label.Width / 2, y - 11));
                            dc.Pop();
                        }
                        var half = y + pxPerCm / 2;
                        if (cm < count && half < ActualHeight + 20)
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
                        if (x < -20 || x > ActualWidth + 20) continue;
                        dc.DrawLine(new Pen(Chrome.SoftText, 1),
                            new Point(x, Thickness - 7), new Point(x, Thickness - 1));
                        if (cm > 0 && cm < count)
                        {
                            var label = new FormattedText(cm.ToString(CultureInfo.CurrentCulture),
                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                typeface, 8.5, Chrome.SoftText, 1.0);
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
