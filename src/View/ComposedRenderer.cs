using System;
using System.Collections.Generic;
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
        /// <summary>Margin guides, InDesign-style: solid, light cyan, slightly
        /// transparent — shared with the classic mirror so both surfaces match.</summary>
        public static readonly Pen MarginPen = BuildMarginPen();

        private static Pen BuildMarginPen()
        {
            var brush = new SolidColorBrush(Color.FromArgb(110, 60, 195, 230));
            brush.Freeze();
            var pen = new Pen(brush, 1);
            pen.Freeze();
            return pen;
        }

        public static void DrawPage(DrawingContext dc, Composition composition, int index,
            bool screenExtras)
        {
            var setup = composition.Setup;
            var page = composition.Pages[index];
            var width = composition.PageWidthPx;
            var height = composition.PageHeightPx;
            // Mirrored margins: the binding-side margin alternates with the
            // page's folio parity (recto = petit fond à gauche).
            var left = composition.LeftPxFor(index);
            var contentWidth = setup.ContentWidthPx;
            var top = composition.TopPx;
            var bottom = composition.BottomPx;

            if (screenExtras && setup.ShowMarginGuides)
                dc.DrawRectangle(null, MarginPen,
                    new Rect(left, top, Math.Max(4, contentWidth),
                        Math.Max(4, height - top - bottom)));

            foreach (var placed in page.Lines)
                DrawLine(dc, composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex],
                    left, placed.Y);

            // Bottom-of-page footnotes: separator rule, then the note lines.
            if (page.NoteLines.Count > 0)
            {
                if (page.NotesRuleY >= 0)
                    dc.DrawRectangle(Brushes.Black, null, new Rect(
                        left, page.NotesRuleY,
                        Math.Min(160, Math.Max(40, contentWidth / 3)), 0.8));
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

            // En-tête / pied de page : le décor du document (ou du gabarit de
            // pages appliqué — recto/verso asymétriques). Un pied personnalisé
            // REMPLACE le folio par défaut ({page} y règle le look du numéro).
            // Les pages blanches (versos d'imposition) restent nues.
            var blank = page.Lines.Count == 0 && page.NoteLines.Count == 0;
            if (!blank)
            {
                var decor = composition.DecorOf(index);
                var recto = composition.FolioOf(index) % 2 == 1;
                // Première page de SON document (livre compilé : le décor
                // change de référence à la frontière de chapitre).
                var firstOfDoc = index == 0
                    || !ReferenceEquals(decor, composition.DecorOf(index - 1));
                var header = decor == null ? null : (recto ? decor.HeaderRecto : decor.HeaderVerso);
                var footer = decor == null ? null : (recto ? decor.FooterRecto : decor.FooterVerso);
                if (decor != null && decor.HeaderHideFirst && firstOfDoc) header = null;
                var footerHidden = decor != null && decor.FooterHideFirst && firstOfDoc;
                var title = decor == null ? "" : decor.Title;
                if (header != null && !header.IsEmpty)
                    DrawDecor(dc, header, composition, index, title, left, contentWidth,
                        true, top, height, bottom, setup,
                        decor == null ? 0 : decor.HeaderGapMm);
                if (footer != null && !footer.IsEmpty)
                {
                    if (!footerHidden)
                        DrawDecor(dc, footer, composition, index, title, left, contentWidth,
                            false, top, height, bottom, setup,
                            decor == null ? 0 : decor.FooterGapMm);
                }
                else if (setup.FooterPageNumbers && !footerHidden
                    && (decor == null || !decor.SuppressFolio))
                {
                    var folio = new FormattedText(composition.FolioOf(index).ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(setup.FooterFont ?? "Times New Roman"),
                        Math.Max(6, setup.FooterSizePt * 4.0 / 3.0), Brushes.Black, 1.0);
                    dc.DrawText(folio, new Point((width - folio.Width) / 2,
                        height - bottom / 2 - folio.Height / 2));
                }
            }

            // Marqueurs veuves/orphelines (écran seulement) : orange = une
            // correction retient des lignes ici, gris = correction débrayée.
            // Cliquables dans la vue Composition.
            if (screenExtras)
                foreach (var mark in page.WidowMarks)
                {
                    var brush = mark.Disabled
                        ? (Brush)Brushes.Gray
                        : new SolidColorBrush(Color.FromRgb(230, 126, 34));
                    dc.DrawRoundedRectangle(brush, null,
                        new Rect(Math.Max(2, left - 22), mark.Y, 14, 14), 3, 3);
                    var glyph = new FormattedText("¶",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                        Brushes.White, 1.0);
                    dc.DrawText(glyph, new Point(Math.Max(2, left - 22) + 4, mark.Y + 1));
                }
        }

        /// <summary>One header/footer line: tokens expanded, aligned on the
        /// text column. Rich zones (gabarits marqués au texte) rendent leurs
        /// runs stylés ; gapMm écarte du bloc de texte (0 = centré marge).</summary>
        private static void DrawDecor(DrawingContext dc, UniversSale.Model.HeaderFooter decor,
            Composition composition, int index, string title, double left, double contentWidth,
            bool isHeader, double top, double height, double bottom, PageSetup setup,
            double gapMm)
        {
            var folio = composition.FolioOf(index);
            var pages = composition.Pages.Count + composition.FolioOffset;
            var pieces = new List<FormattedText>();
            var align = decor.Align;
            if (decor.Rich != null)
            {
                if (decor.Rich.AlignOverride != null) align = decor.Rich.AlignOverride;
                foreach (var run in decor.Rich.Runs)
                {
                    var text = new UniversSale.Model.HeaderFooter { Text = run.Text }
                        .Expand(folio, pages, title);
                    if (text.Length == 0) continue;
                    var typeface = new Typeface(
                        new FontFamily(run.FontFamily ?? setup.FooterFont ?? "Times New Roman"),
                        run.Italic == true ? FontStyles.Italic : FontStyles.Normal,
                        run.Weight != null ? FlowConverter.ParseWeight(run.Weight)
                            : run.Bold == true ? FontWeights.Bold : FontWeights.Normal,
                        FontStretches.Normal);
                    var brush = run.Color != null
                        ? (Brush)new SolidColorBrush(FlowConverter.ParseColor(run.Color))
                        : Brushes.Black;
                    pieces.Add(new FormattedText(text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, typeface,
                        run.FontSize ?? Math.Max(6, decor.SizePt * 4.0 / 3.0), brush, 1.0));
                }
            }
            else
            {
                var text = decor.Expand(folio, pages, title);
                if (text.Trim().Length == 0) return;
                var typeface = new Typeface(
                    new FontFamily(decor.FontFamily ?? setup.FooterFont ?? "Times New Roman"),
                    decor.Italic ? FontStyles.Italic : FontStyles.Normal,
                    decor.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal);
                pieces.Add(new FormattedText(text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface,
                    Math.Max(6, decor.SizePt * 4.0 / 3.0), Brushes.Black, 1.0));
            }
            if (pieces.Count == 0) return;

            double totalWidth = 0, maxHeight = 0, maxBaseline = 0;
            foreach (var piece in pieces)
            {
                totalWidth += piece.WidthIncludingTrailingWhitespace;
                if (piece.Height > maxHeight) maxHeight = piece.Height;
                if (piece.Baseline > maxBaseline) maxBaseline = piece.Baseline;
            }
            var x = align == "left" ? left
                  : align == "right" ? left + contentWidth - totalWidth
                  : left + (contentWidth - totalWidth) / 2;
            var gap = gapMm * PageSetup.PxPerMm;
            var y = isHeader
                ? (gap > 0.01 ? Math.Max(2, top - gap - maxHeight)
                              : Math.Max(2, top / 2 - maxHeight / 2))
                : (gap > 0.01 ? Math.Min(height - maxHeight - 2, height - bottom + gap)
                              : height - bottom / 2 - maxHeight / 2);
            foreach (var piece in pieces)
            {
                // Alignés sur une même ligne de base.
                dc.DrawText(piece, new Point(x, y + maxBaseline - piece.Baseline));
                x += piece.WidthIncludingTrailingWhitespace;
            }
        }

        /// <summary>One composed line (body or footnote), pieces drawn through
        /// translations at page position <paramref name="top"/>.</summary>
        private static void DrawLine(DrawingContext dc, ComposedLine line, double left, double top)
        {
            var baseline = top + line.Ascent;

            // Pass 1 — highlights, behind everything (spaces included).
            foreach (var piece in line.Pieces)
            {
                if (piece.Highlight == null) continue;
                var w = piece.VisualWidth();
                if (w < 0.1) continue;
                var size = piece.FontSizePx > 0 ? piece.FontSizePx : 16;
                dc.DrawRectangle(piece.Highlight, null, new Rect(
                    left + piece.Origin.X, baseline + piece.Origin.Y - size * 0.8,
                    w, size * 1.05));
            }

            foreach (var piece in line.Pieces)
            {
                DrawDecorations(dc, piece, left, baseline);
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

        /// <summary>Underline and strikethrough, spaces included so the line
        /// runs unbroken under a whole passage. Font metrics when the glyph
        /// run carries them, sane ratios otherwise.</summary>
        private static void DrawDecorations(DrawingContext dc, ComposedPiece piece,
            double left, double baseline)
        {
            if (!piece.Underline && !piece.Strike) return;
            var w = piece.VisualWidth();
            if (w < 0.1) return;
            var size = piece.FontSizePx > 0 ? piece.FontSizePx : 16;
            var typeface = piece.Glyphs != null ? piece.Glyphs.GlyphTypeface : null;
            var ink = piece.Ink ?? Brushes.Black;
            var x = left + piece.Origin.X;
            var y = baseline + piece.Origin.Y;
            if (piece.Underline)
            {
                var offset = typeface != null ? -typeface.UnderlinePosition * size : size * 0.09;
                var thickness = typeface != null
                    ? Math.Max(0.8, typeface.UnderlineThickness * size) : Math.Max(0.8, size * 0.05);
                dc.DrawRectangle(ink, null, new Rect(x, y + offset, w, thickness));
            }
            if (piece.Strike)
            {
                var offset = typeface != null ? -typeface.StrikethroughPosition * size : -size * 0.3;
                var thickness = typeface != null
                    ? Math.Max(0.8, typeface.StrikethroughThickness * size) : Math.Max(0.8, size * 0.05);
                dc.DrawRectangle(ink, null, new Rect(x, y + offset, w, thickness));
            }
        }
    }
}
