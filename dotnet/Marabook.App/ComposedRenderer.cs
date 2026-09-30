using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
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

        /// <summary>Caractères d'impression (batch 35) : la fonction n'avait
        /// jamais été portée sur la surface composée — le bouton ¶ repeignait
        /// un calque du classique replié. Écran seulement, jamais au papier.</summary>
        public static bool ShowMarks;
        private static readonly Typeface MarksTypeface = new Typeface(FontFamily.Default);
        private static readonly IBrush MarksBrush = FrozenBrush(Color.FromRgb(0x5B, 0x67, 0xD8));

        private static IBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            return brush;
        }

        private static void DrawMark(DrawingContext dc, string glyph, double x, double baseline, double size)
        {
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, MarksTypeface, Math.Max(7, size), MarksBrush);
            dc.DrawText(text, new Point(x, baseline - text.Baseline));
        }

        private static Pen BuildMarginPen()
        {
            var brush = new SolidColorBrush(Color.FromArgb(110, 60, 195, 230));
            var pen = new Pen(brush, 1);
            return pen;
        }

        /// <summary>L'encre par défaut du tracé en cours : sur l'écran, l'encre
        /// du papier (elle suit le thème et « papier blanc en mode sombre »,
        /// batch 40) ; au papier, le noir — l'impression ne change pas.</summary>
        private static IBrush DefaultInk = Brushes.Black;

        /// <summary>Les marqueurs veuves/orphelines à l'écran — éteints par le
        /// mode calme (13/09), comme les guides de marges.</summary>
        public static bool ShowWidowMarks = true;

        /// <summary>hiddenNote : l'index (ordre des appels) de la note en cours
        /// d'édition, qui n'est pas dessinée ; -1 = toutes (impression, PDF).</summary>
        public static void DrawPage(DrawingContext dc, Composition composition, int index,
            bool screenExtras, int hiddenNote = -1)
        {
            DefaultInk = screenExtras ? (IBrush)Chrome.PaperInk : Brushes.Black;
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

            // Les images de la page (0.50.0), sous le texte : le texte coule
            // autour, jamais dessus — sauf placement libre voulu tel.
            foreach (var image in page.Images)
                DrawImage(dc, image, composition, screenExtras);

            foreach (var placed in page.Lines)
                DrawLine(dc, placed.Line, left, placed.Y, screenExtras);

            // Bottom-of-page footnotes: separator rule, then the note lines.
            if (page.NoteLines.Count > 0)
            {
                if (page.NotesRuleY >= 0)
                    dc.DrawRectangle(DefaultInk, null, new Rect(
                        left, page.NotesRuleY,
                        Math.Min(160, Math.Max(40, contentWidth / 3)), 0.8));
                foreach (var placed in page.NoteLines)
                {
                    // La note en cours d'édition (0.50.0) n'est pas dessinée :
                    // le champ posé dessus EST la note, pas un calque par-dessus.
                    if (placed.ParagraphIndex == hiddenNote) continue;
                    DrawLine(dc, placed.Line, left, placed.Y, screenExtras);
                }
            }

            if (setup.LineNumbers)
            {
                var number = 0;
                foreach (var placed in page.Lines)
                {
                    number++;
                    var line = placed.Line;
                    var label = new FormattedText(number.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 9,
                        screenExtras ? (IBrush)Chrome.SoftText : Brushes.Gray);
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
                var book = decor == null ? "" : decor.BookTitle;
                if (header != null && !header.IsEmpty)
                    DrawDecor(dc, header, composition, index, title, book, left, contentWidth,
                        true, top, height, bottom, setup,
                        decor == null ? 0 : decor.HeaderGapMm);
                if (footer != null && !footer.IsEmpty)
                {
                    if (!footerHidden)
                        DrawDecor(dc, footer, composition, index, title, book, left, contentWidth,
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
                        Math.Max(6, setup.FooterSizePt * 4.0 / 3.0), DefaultInk);
                    dc.DrawText(folio, new Point((width - folio.Width) / 2,
                        height - bottom / 2 - folio.Height / 2));
                }
            }

            // Annotations (écran seulement) : en plus de la teinte du passage,
            // une pastille or dans la marge de droite signale chaque ligne
            // annotée — visible d'un coup d'œil en Composition.
            if (screenExtras)
            {
                var markerBrush = AnnotationMarkerBrush;
                // Une image annotée (0.50.0) : sa pastille à la hauteur de son bord haut.
                foreach (var image in page.Images)
                    if (IsAnnotated(image.Run, composition))
                        dc.DrawRectangle(markerBrush, null, new Rect(
                            Math.Min(width - 8, left + contentWidth + 10), image.Rect.Y + 2, 5, 8), 2.5, 2.5);
                foreach (var placed in page.Lines)
                {
                    var line = placed.Line;
                    var annotated = false;
                    foreach (var piece in line.Pieces)
                        if (piece.HighlightRole == HighlightRole.Annotation)
                        { annotated = true; break; }
                    if (!annotated) continue;
                    dc.DrawRectangle(markerBrush, null, new Rect(
                        Math.Min(width - 8, left + contentWidth + 10),
                        placed.Y + Math.Max(0, line.Height / 2 - 4), 5, 8), 2.5, 2.5);
                }
            }

            // Signalements de correction (écran seulement) : ondulé sous la
            // plage, couleur par catégorie — jamais dans l'aperçu,
            // l'impression, le PDF ni les exports (même discipline que la
            // teinte d'annotation, filtrée sur screenExtras).
            if (screenExtras && composition.ScreenFindings != null)
                foreach (var placed in page.Lines)
                {
                    List<Correction.Finding> findings;
                    if (!composition.ScreenFindings.TryGetValue(
                        placed.ParagraphIndex, out findings)) continue;
                    var line = placed.Line;
                    foreach (var finding in findings)
                    {
                        if (finding.End <= line.Start || finding.Start >= line.End)
                            continue;
                        var x1 = OffsetX(line, Math.Max(finding.Start, line.Start), left);
                        var x2 = OffsetX(line, Math.Min(finding.End, line.End), left);
                        if (x2 - x1 < 1.5) continue;
                        DrawSquiggle(dc, x1, x2,
                            placed.Y + line.Ascent + 2.2, FindingPen(finding));
                    }
                }

            // Marqueurs veuves/orphelines (écran seulement) : orange = une
            // correction retient des lignes ici, gris = correction débrayée.
            // Cliquables dans la vue Composition. Jamais en mode calme (13/09).
            if (screenExtras && ShowWidowMarks)
                foreach (var mark in page.WidowMarks)
                {
                    var brush = mark.Disabled
                        ? (IBrush)Brushes.Gray
                        : new SolidColorBrush(Color.FromRgb(230, 126, 34));
                    dc.DrawRectangle(brush, null,
                        new Rect(Math.Max(2, left - 22), mark.Y, 14, 14), 3, 3);
                    var glyph = new FormattedText("¶",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10,
                        Brushes.White);
                    dc.DrawText(glyph, new Point(Math.Max(2, left - 22) + 4, mark.Y + 1));
                }
        }

        private static readonly IBrush AnnotationMarkerBrush = FrozenBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
        private static readonly Pen PlaceholderPen = FrozenPen(Color.FromRgb(0xA0, 0xA6, 0xB4));

        /// <summary>Le run porte une annotation vivante (non résolue), et les
        /// annotations sont affichées.</summary>
        private static bool IsAnnotated(Marabook.Model.TextRun run, Composition composition)
        {
            if (run == null || run.AnnotationId == null || !Settings.AppSettings.ShowAnnotations
                || composition.Source == null) return false;
            var annotation = composition.Source.FindAnnotation(run.AnnotationId);
            return annotation != null && !annotation.Resolved;
        }

        /// <summary>Une image posée (0.50.0) : la bitmap dans son rectangle ;
        /// illisible, un cadre gris et sa légende ; annotée (écran), un liseré
        /// or — la teinte des passages annotés, en cadre pour ne pas voiler
        /// l'image.</summary>
        private static void DrawImage(DrawingContext dc, PlacedImage image, Composition composition, bool screenExtras)
        {
            // La bitmap décodée une fois (ImageCache) ; le compositeur n'en a
            // que les dimensions (P0).
            var source = image.Readable ? ImageCache.For(image.Stored) : null;
            if (source != null)
                dc.DrawImage(source, image.Rect.ToRect());
            else
            {
                dc.DrawRectangle(null, PlaceholderPen, new Rect(
                    image.Rect.X + 0.5, image.Rect.Y + 0.5, Math.Max(1, image.Rect.Width - 1), Math.Max(1, image.Rect.Height - 1)));
                var label = new FormattedText("[image introuvable]",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    MarksTypeface, 10, screenExtras ? (IBrush)Chrome.SoftText : Brushes.Gray);
                if (label.Width < image.Rect.Width - 4 && label.Height < image.Rect.Height - 4)
                    dc.DrawText(label, new Point(image.Rect.X + (image.Rect.Width - label.Width) / 2,
                        image.Rect.Y + (image.Rect.Height - label.Height) / 2));
            }
            if (screenExtras && IsAnnotated(image.Run, composition))
            {
                var pen = new Pen(AnnotationMarkerBrush, 2.5);
                dc.DrawRectangle(null, pen, new Rect(
                    image.Rect.X - 1.25, image.Rect.Y - 1.25, image.Rect.Width + 2.5, image.Rect.Height + 2.5));
            }
        }

        /// <summary>One header/footer line: tokens expanded, aligned on the
        /// text column. Rich zones (gabarits marqués au texte) rendent leurs
        /// runs stylés ; gapMm écarte du bloc de texte (0 = centré marge).</summary>
        private static void DrawDecor(DrawingContext dc, Marabook.Model.HeaderFooter decor,
            Composition composition, int index, string title, string book, double left,
            double contentWidth, bool isHeader, double top, double height, double bottom,
            PageSetup setup, double gapMm)
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
                    var text = new Marabook.Model.HeaderFooter { Text = run.Text }
                        .Expand(folio, pages, title, book);
                    if (text.Length == 0) continue;
                    var typeface = new Typeface(
                        new FontFamily(run.FontFamily ?? setup.FooterFont ?? "Times New Roman"),
                        run.Italic == true ? FontStyle.Italic : FontStyle.Normal,
                        run.Weight != null ? (FontWeight)TextWeights.Parse(run.Weight)
                            : run.Bold == true ? FontWeight.Bold : FontWeight.Normal,
                        FontStretch.Normal);
                    var brush = run.Color != null
                        ? (IBrush)new SolidColorBrush(Ink.Parse(run.Color).ToColor())
                        : DefaultInk;
                    pieces.Add(new FormattedText(text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, typeface,
                        run.FontSize ?? Math.Max(6, decor.SizePt * 4.0 / 3.0), brush));
                }
            }
            else
            {
                var text = decor.Expand(folio, pages, title, book);
                if (text.Trim().Length == 0) return;
                var typeface = new Typeface(
                    new FontFamily(decor.FontFamily ?? setup.FooterFont ?? "Times New Roman"),
                    decor.Italic ? FontStyle.Italic : FontStyle.Normal,
                    decor.Bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStretch.Normal);
                pieces.Add(new FormattedText(text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface,
                    Math.Max(6, decor.SizePt * 4.0 / 3.0), DefaultInk));
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
            // Écart signé et CONTINU : un décalage depuis la position centrée
            // dans la marge (0 = centré, positif = vers le bord de page,
            // négatif = vers le corps, jusqu'à mordre dedans) — plus de saut
            // entre 0 et ±1.
            var gap = gapMm * PageSetup.PxPerMm;
            var y = isHeader
                ? Math.Max(2, top / 2 - maxHeight / 2 - gap)
                : Math.Min(height - maxHeight - 2,
                    height - bottom / 2 - maxHeight / 2 + gap);
            foreach (var piece in pieces)
            {
                // Alignés sur une même ligne de base.
                dc.DrawText(piece, new Point(x, y + maxBaseline - piece.Baseline));
                x += piece.WidthIncludingTrailingWhitespace;
            }
        }

        /// <summary>Le surlignage d'une pièce ne va pas au papier quand il est
        /// semi-transparent (teinte d'annotation) : même règle que le PDF.</summary>
        private static bool IsTranslucent(ComposedPiece piece)
        {
            return piece.HighlightRole == HighlightRole.Annotation
                || (piece.Highlight.HasValue && piece.Highlight.Value.IsTranslucent);
        }

        // ------------------------------------------- signalements de correction

        // Les couleurs des relevés (13/09, demande de Rémi) : orthographe
        // rouge, grammaire bleu, typographie jaune, style vert (répétitions),
        // violet (adverbes en -ment), gris (verbes ternes). Le style se
        // distingue PAR RÈGLE : FindingPen(finding) tranche, la version par
        // catégorie sert aux en-têtes et aux filtres.
        private static readonly Pen SpellingPen = FrozenPen(Color.FromRgb(0xD6, 0x45, 0x41));
        private static readonly Pen GrammarPen = FrozenPen(Color.FromRgb(0x3B, 0x7D, 0xD8));
        private static readonly Pen TypographyPen = FrozenPen(Color.FromRgb(0xD9, 0xA4, 0x06));
        private static readonly Pen StylePen = FrozenPen(Color.FromRgb(0x2E, 0x9E, 0x6B));
        private static readonly Pen AdverbPen = FrozenPen(Color.FromRgb(0x8E, 0x44, 0xAD));
        private static readonly Pen DullVerbPen = FrozenPen(Color.FromRgb(0x85, 0x85, 0x85));

        private static Pen FrozenPen(Color color)
        {
            return new Pen(new SolidColorBrush(color), 1.1, null, PenLineCap.Round, PenLineJoin.Round);
        }

        /// <summary>La couleur d'UN signalement — l'ondulé, la pastille de sa
        /// fiche, le menu contextuel : la catégorie, affinée par la règle
        /// pour le style.</summary>
        public static Pen FindingPen(Correction.Finding finding)
        {
            return FindingPen(finding.Category, finding.RuleId);
        }

        public static Pen FindingPen(Correction.FindingCategory category, string ruleId)
        {
            if (category == Correction.FindingCategory.Style)
            {
                if (ruleId == Correction.Grammalecte.StyleChecker.AdverbRule) return AdverbPen;
                if (ruleId == Correction.Grammalecte.StyleChecker.DullVerbRule) return DullVerbPen;
            }
            return FindingPen(category);
        }

        /// <summary>Le libellé français d'une catégorie — filtres, en-têtes
        /// de groupe, dialogue des options : UNE source.</summary>
        public static string CategoryLabel(Correction.FindingCategory category)
        {
            switch (category)
            {
                case Correction.FindingCategory.Spelling: return "Orthographe";
                case Correction.FindingCategory.Grammar: return "Grammaire";
                case Correction.FindingCategory.Typography: return "Typographie";
                default: return "Style";
            }
        }

        /// <summary>La pastille d'un relevé (panneau, dialogue des options) :
        /// un rond de la couleur de l'ondulé.</summary>
        public static Border FindingDot(Pen pen, double size)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = pen.Brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 6, 0)
            };
        }

        /// <summary>La couleur d'une catégorie — en-têtes de groupe et
        /// filtres du panneau Correction (le style y est vert).</summary>
        public static Pen FindingPen(Correction.FindingCategory category)
        {
            switch (category)
            {
                case Correction.FindingCategory.Spelling: return SpellingPen;
                case Correction.FindingCategory.Grammar: return GrammarPen;
                case Correction.FindingCategory.Typography: return TypographyPen;
                default: return StylePen;
            }
        }

        /// <summary>Le zigzag d'un signalement, période 3 px, amplitude 1,3 px.</summary>
        private static void DrawSquiggle(DrawingContext dc, double x1, double x2,
            double y, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x1, y), false);
                var up = true;
                for (var x = x1 + 1.5; x < x2; x += 1.5)
                {
                    ctx.LineTo(new Point(x, y + (up ? -1.3 : 1.3)));
                    up = !up;
                }
                ctx.LineTo(new Point(x2, y));
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>Abscisse d'un offset plat sur une ligne composée — la
        /// géométrie du caret de ComposedView, factorisée pour que l'ondulé
        /// des signalements et le caret parlent du même pixel.</summary>
        public static double OffsetX(ComposedLine line, int offset, double left)
        {
            var x = left;
            double best = -1;
            foreach (var piece in line.Pieces)
            {
                if (piece.SourceStart < 0 || piece.SourceLength <= 0) continue;
                if (offset <= piece.SourceStart)
                {
                    if (best < 0) best = left + piece.Origin.X;
                    continue;
                }
                if (offset <= piece.SourceStart + piece.SourceLength)
                {
                    var into = offset - piece.SourceStart;
                    var dx = into == 0 ? 0
                        : piece.CharRights[Math.Min(into, piece.CharRights.Length) - 1];
                    return left + piece.Origin.X + dx;
                }
                x = left + piece.Origin.X
                    + (piece.CharRights != null && piece.CharRights.Length > 0
                        ? piece.CharRights[piece.CharRights.Length - 1]
                        : 0);
            }
            return best >= 0 && offset <= line.Start ? best : x;
        }

        /// <summary>One composed line (body or footnote), pieces drawn through
        /// translations at page position <paramref name="top"/>.</summary>
        private static void DrawLine(DrawingContext dc, ComposedLine line, double left, double top,
            bool screenExtras = true)
        {
            var baseline = top + line.Ascent;

            // Pass 1 — highlights, behind everything (spaces included). Les
            // teintes d'annotation (semi-transparentes) sont écran seulement.
            foreach (var piece in line.Pieces)
            {
                var highlight = Geo.HighlightBrush(piece);
                if (highlight == null) continue;
                if (!screenExtras && IsTranslucent(piece)) continue;
                var w = piece.VisualWidth();
                if (w < 0.1) continue;
                var size = piece.FontSizePx > 0 ? piece.FontSizePx : 16;
                dc.DrawRectangle(highlight, null, new Rect(
                    left + piece.Origin.X, baseline + piece.Origin.Y - size * 0.8,
                    w, size * 1.05));
            }

            var lastSize = 14.0;
            var lineEnd = 0.0;
            foreach (var piece in line.Pieces)
            {
                DrawDecorations(dc, piece, left, baseline);
                if (piece.FontSizePx > 0) lastSize = piece.FontSizePx;
                lineEnd = Math.Max(lineEnd, piece.Origin.X + piece.VisualWidth());
                if (piece.IsSpace)
                {
                    // « · » au milieu de l'espace (marques d'impression, écran).
                    if (screenExtras && ShowMarks && piece.SpaceWidth > 1)
                        DrawMark(dc, "·", left + piece.Origin.X + piece.SpaceWidth / 2 - 1.5,
                            baseline + piece.Origin.Y, lastSize * 0.9);
                    continue;
                }
                if (piece.IsRule)
                {
                    dc.DrawRectangle(DefaultInk, null, new Rect(
                        left + piece.Rect.X, top + line.Height / 2,
                        piece.Rect.Width, piece.Rect.Height));
                    continue;
                }
                if (piece.IsAnchor)
                {
                    // L'ancre d'une image (0.50.0) : rien, sauf avec les
                    // caractères d'impression — une petite ancre, à l'écran.
                    if (screenExtras && ShowMarks)
                        DrawMark(dc, "⚓", left + piece.Origin.X - 1, baseline + piece.Origin.Y, lastSize * 0.8);
                    continue;
                }
                var x = left + piece.Origin.X;
                var y = baseline + piece.Origin.Y;
                var scaled = Math.Abs(piece.ScaleX - 1.0) > 0.001;
                var transform = scaled
                    ? Matrix.CreateScale(piece.ScaleX, 1.0) * Matrix.CreateTranslation(x, y)
                    : Matrix.CreateTranslation(x, y);
                using (dc.PushTransform(transform))
                {
                    if (piece.IsGlyphs)
                    {
                        var run = GlyphRunOf(piece);
                        if (run != null)
                        {
                            var ink = Geo.InkBrush(piece);
                            dc.DrawGlyphRun(ink, run);
                            // Le gras ÉMULÉ (29/09) : la face n'a pas de graisse
                            // propre (police variable) — le contour des glyphes,
                            // tracé au 1/28e du corps, épaissit le trait ; on ne
                            // laisse jamais Avalonia le simuler (voir Resolve).
                            if (piece.Face != null && piece.Face.SimulatedBold)
                                dc.DrawGeometry(null, new Pen(ink, Math.Max(0.6, piece.EmSize * 0.036)), run.BuildGeometry());
                        }
                    }
                    else if (piece.IsFallback)
                    {
                        var text = FallbackOf(piece);
                        if (text != null) dc.DrawText(text, new Point(0, -piece.FallbackBaseline));
                    }
                }
            }
            // Fin de paragraphe « ¶ », saut de ligne forcé « ↵ ».
            if (screenExtras && ShowMarks && (line.EndsParagraph || line.ForcedBreak))
                DrawMark(dc, line.ForcedBreak && !line.EndsParagraph ? "↵" : "¶",
                    left + lineEnd + 2, baseline, lastSize * 0.85);
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
            var face = piece.IsGlyphs ? piece.Face : null;
            var ink = Geo.InkBrush(piece);
            var x = left + piece.Origin.X;
            var y = baseline + piece.Origin.Y;
            if (piece.Underline)
            {
                var offset = face != null ? -face.UnderlinePosition * size : size * 0.09;
                var thickness = face != null
                    ? Math.Max(0.8, face.UnderlineThickness * size) : Math.Max(0.8, size * 0.05);
                dc.DrawRectangle(ink, null, new Rect(x, y + offset - thickness / 2, w, thickness)); // le trait centré sur la position (30/09)
            }
            if (piece.Strike)
            {
                var offset = face != null ? -face.StrikethroughPosition * size : -size * 0.3;
                var thickness = face != null
                    ? Math.Max(0.8, face.StrikethroughThickness * size) : Math.Max(0.8, size * 0.05);
                dc.DrawRectangle(ink, null, new Rect(x, y + offset - thickness / 2, w, thickness));
            }
        }

        // ------------------------------------------- objets natifs des pièces

        /// <summary>Le GlyphRun d'une pièce (P0) : bâti une fois depuis la
        /// face et les avances, gardé dans la pièce ; le compositeur l'efface
        /// quand la justification bouge les avances.</summary>
        private static GlyphRun GlyphRunOf(ComposedPiece piece)
        {
            var cached = piece.NativeCache as GlyphRun;
            if (cached != null) return cached;
            var glyphs = AvaloniaFontEngine.GlyphsOf(piece.Face);
            if (glyphs == null || piece.GlyphIndices.Length == 0) return null;
            // Avalonia porte les avances dans des GlyphInfo (indice, grappe, avance).
            var infos = new Avalonia.Media.TextFormatting.GlyphInfo[piece.GlyphIndices.Length];
            for (var i = 0; i < infos.Length; i++)
                infos[i] = new Avalonia.Media.TextFormatting.GlyphInfo(piece.GlyphIndices[i], i, piece.Advances[i]);
            var run = new GlyphRun(glyphs, piece.EmSize, (piece.Text ?? "").AsMemory(), infos);
            // DrawGlyphRun dessine à BaselineOrigin, qui vaut (0, ascendante)
            // par défaut : la ligne de base tombait une ascendante SOUS le
            // point où DrawLine l'avait déjà posée — le texte flottait sous
            // le caret et la sélection (27/09). La translation poussée par
            // DrawLine est la ligne de base : l'origine du run est (0, 0).
            run.BaselineOrigin = new Point(0, 0);
            piece.NativeCache = run;
            return run;
        }

        /// <summary>Le FormattedText d'un repli, dans l'encre de la pièce (les
        /// rôles du thème ne concernent que les glyphes ; un repli est rare).</summary>
        private static FormattedText FallbackOf(ComposedPiece piece)
        {
            var cached = piece.NativeCache as FormattedText;
            if (cached != null) return cached;
            var text = AvaloniaFontEngine.Format(piece.Face, piece.Text, piece.EmSize, Geo.InkBrush(piece));
            piece.NativeCache = text;
            return text;
        }
    }
}
