using System;
using System.Windows;
using System.Windows.Media;
using UniversSale.Model;
using UniversSale.Print;

namespace UniversSale.View
{
    /// <summary>Draws one composed page — lines placed by the engine, pieces
    /// drawn through translations (no copies). Shared by the editable
    /// Composition mode, the page preview and printing: screen and paper come
    /// from the same strokes.</summary>
    public static class ComposedRenderer
    {
        public static void DrawPage(DrawingContext dc, Composition composition, int index,
            bool screenExtras)
        {
            var setup = composition.Setup;
            var page = composition.Pages[index];
            var width = composition.PageWidthPx;
            var height = composition.PageHeightPx;
            var left = composition.LeftPx;
            var right = setup.MarginRightMm * PageSetup.PxPerMm;
            var top = composition.TopPx;
            var bottom = composition.BottomPx;

            if (screenExtras && setup.ShowMarginGuides)
            {
                var pen = new Pen(Chrome.Border, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
                dc.DrawRectangle(null, pen,
                    new Rect(left, top, Math.Max(4, width - left - right),
                        Math.Max(4, height - top - bottom)));
            }

            foreach (var placed in page.Lines)
                DrawLine(dc, composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex],
                    left, placed.Y);

            // Bottom-of-page footnotes: separator rule, then the note lines.
            if (page.NoteLines.Count > 0)
            {
                if (page.NotesRuleY >= 0)
                    dc.DrawRectangle(Brushes.Black, null, new Rect(
                        left, page.NotesRuleY,
                        Math.Min(160, Math.Max(40, (width - left - right) / 3)), 0.8));
                foreach (var placed in page.NoteLines)
                    DrawLine(dc, composition.NoteParagraphs[placed.ParagraphIndex].Lines[placed.LineIndex],
                        left, placed.Y);
            }

            if (setup.LineNumbers)
            {
                var number = 0;
                foreach (var placed in page.Lines)
                {
                    number++;
                    var line = composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex];
                    var label = new FormattedText(number.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
                        screenExtras ? (Brush)Chrome.SoftText : Brushes.Gray, 1.0);
                    // Right-aligned, vertically centered on the line box.
                    dc.DrawText(label, new Point(
                        Math.Max(2, left - 8 - label.Width),
                        placed.Y + Math.Max(0, (line.Height - label.Height) / 2)));
                }
            }

            if (setup.FooterPageNumbers)
            {
                var folio = new FormattedText((index + 1).ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(setup.FooterFont ?? "Times New Roman"),
                    Math.Max(6, setup.FooterSizePt * 4.0 / 3.0), Brushes.Black, 1.0);
                dc.DrawText(folio, new Point((width - folio.Width) / 2,
                    height - bottom / 2 - folio.Height / 2));
            }
        }

        /// <summary>One composed line (body or footnote), pieces drawn through
        /// translations at page position <paramref name="top"/>.</summary>
        private static void DrawLine(DrawingContext dc, ComposedLine line, double left, double top)
        {
            var baseline = top + line.Ascent;
            foreach (var piece in line.Pieces)
            {
                if (piece.IsSpace) continue;
                if (piece.IsRule)
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(
                        left + piece.Rect.X, top + line.Height / 2,
                        piece.Rect.Width, piece.Rect.Height));
                    continue;
                }
                if (piece.Image != null)
                {
                    dc.DrawImage(piece.Image, new Rect(
                        left + piece.Rect.X, top + piece.Rect.Y,
                        piece.Rect.Width, piece.Rect.Height));
                    continue;
                }
                var x = left + piece.Origin.X;
                var y = baseline + piece.Origin.Y;
                var scaled = Math.Abs(piece.ScaleX - 1.0) > 0.001;
                dc.PushTransform(new TranslateTransform(x, y));
                if (scaled) dc.PushTransform(new ScaleTransform(piece.ScaleX, 1.0));
                if (piece.Glyphs != null)
                    dc.DrawGlyphRun(piece.Ink ?? Brushes.Black, piece.Glyphs);
                else if (piece.Fallback != null)
                    dc.DrawText(piece.Fallback, new Point(0, -piece.Fallback.Baseline));
                if (scaled) dc.Pop();
                dc.Pop();
            }
        }
    }
}
