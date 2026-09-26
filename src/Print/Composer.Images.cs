using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.Print
{
    /// <summary>Une image posée sur une page (refonte des images, 0.50.0) :
    /// son run (l'ancre dans le texte), son rectangle en coordonnées de page
    /// et sa source décodée (null = illisible, un cadre de substitution).</summary>
    public class PlacedImage
    {
        public TextRun Run;
        public int ParagraphIndex;
        public Rect Rect;
        public ImageSource Source;
        public bool Wrap;     // texte de part et d'autre
        public bool Attached; // suit la ligne de l'ancre (Y nul)
    }

    /// <summary>CompositionEngine, partie « pagination » (0.50.0). Avant, les
    /// images étaient des blocs dans la ligne ; elles sont désormais posées
    /// sur la page de leur ANCRE et le texte coule autour d'elles.
    ///
    /// Deux voies pour poser un paragraphe :
    /// — la voie PLEINE COLONNE : les lignes composées une fois pour toutes
    ///   (ComposeWithStyle), avec veuves/orphelines et blocs solidaires —
    ///   inchangée, c'est le cas de tous les paragraphes loin des images ;
    /// — la voie LIGNE À LIGNE : le paragraphe porte une ancre, ou sa bande
    ///   heurte une image de la page — ses atomes sont rejoués ligne par
    ///   ligne, chaque ligne mesurée dans les SEGMENTS que laissent les
    ///   images (« wrap » : de part et d'autre ; « exclude » : la ligne passe
    ///   dessous). Une image attachée à sa ligne apparaît quand l'ancre est
    ///   posée ; une image à position explicite peut être AU-DESSUS de son
    ///   ancre : la page est alors rejouée depuis son début avec l'image en
    ///   obstacle (au plus MaxRestarts fois). Une image reste sur la page où
    ///   son ancre est tombée d'abord, même si l'obstacle a fini par pousser
    ///   l'ancre à la page suivante (règle monotone : jamais d'oscillation).</summary>
    public partial class CompositionEngine
    {
        /// <summary>L'air entre le texte et une image, en px.</summary>
        internal const double ImageGap = 6;
        /// <summary>Un segment plus étroit ne reçoit pas de texte (≈ 3 cadratins).</summary>
        internal const double MinSegmentPx = 40;
        private const int MaxRestarts = 6;
        private const int MaxPages = 20000;

        /// <summary>Les atomes d'un paragraphe composé, gardés pour la voie
        /// ligne à ligne (faible : ils partent avec le paragraphe recomposé).</summary>
        private readonly ConditionalWeakTable<ComposedParagraphLayout, List<Atom>> _atomsOf =
            new ConditionalWeakTable<ComposedParagraphLayout, List<Atom>>();

        /// <summary>L'interligne d'un style, l'interligne du document compris.</summary>
        private double Leading(ParagraphStyle style)
        {
            var leading = style.LineHeight > 1
                ? style.LineHeight
                : style.FontSize * Math.Max(100, style.AutoLeadingPercent) / 100.0;
            // L'interligne du document (22/09) multiplie celui du style.
            if (_document.LineSpacing > 0) leading *= _document.LineSpacing;
            return leading;
        }

        private double ContentHeight
        {
            get { return Current.PageHeightPx - Current.TopPx - Current.BottomPx; }
        }

        /// <summary>Où en est le placement : le paragraphe en cours et, dedans,
        /// la prochaine ligne pleine colonne ou le prochain offset plat (voie
        /// ligne à ligne).</summary>
        private sealed class PlaceState
        {
            public int Paragraph;
            public int LineIndex;
            public int Cursor;
            public bool Shaped;
            public bool PageBreak; // la page s'est close sur un saut de page

            public PlaceState Clone() { return (PlaceState)MemberwiseClone(); }
        }

        private struct Segment
        {
            public double X, Width; // relatifs au bord gauche de la colonne
        }

        // ============================================================ pagination

        /// <summary>Places every composed line on pages (no piece copies) with
        /// keeps and widow/orphan control. Space for the footnotes called from
        /// each placed line is reserved at the bottom of its page — a line and
        /// its notes always share a page. Returns the index of the first page
        /// whose content differs from the previous pagination.</summary>
        public int Repaginate()
        {
            var previous = Current.Pages;
            var pages = new List<ComposedPageLayout>();
            var paragraphs = Current.Paragraphs;
            var placedRuns = new HashSet<TextRun>();
            var state = new PlaceState();
            while (pages.Count < MaxPages)
            {
                // Chapter start of a book: land on a RECTO — an even folio
                // gets a blank verso inserted before it.
                if (state.PageBreak && state.Paragraph < paragraphs.Count
                    && paragraphs[state.Paragraph].StartOnRecto
                    && (pages.Count + 1 + Current.FolioOffset) % 2 == 0)
                    pages.Add(new ComposedPageLayout());
                var start = state.Clone();
                var obstacles = new List<PlacedImage>();
                _stopBefore = null;
                ComposedPageLayout page;
                for (var attempt = 0; ; attempt++)
                {
                    page = new ComposedPageLayout();
                    state = start.Clone();
                    LayoutPage(page, pages.Count, state, obstacles, placedRuns);
                    // Une image à position explicite découverte sur cette page :
                    // la page est rejouée avec elle en obstacle dès le début.
                    var added = false;
                    foreach (var image in page.Images)
                        if (!image.Attached && FindRun(obstacles, image.Run) == null)
                        {
                            obstacles.Add(image);
                            added = true;
                        }
                    if (added && attempt < MaxRestarts) continue;
                    // Un obstacle qui a poussé sa propre ancre à la page suivante
                    // (image en haut de page, ancre en bas) : l'image et sa
                    // ligne partent ensemble — la page se ferme AVANT la ligne
                    // de l'ancre (règle de Word : le bas de page reste vide).
                    var anchorsHere = AnchorsOn(page);
                    PlacedImage slipped = null;
                    foreach (var image in obstacles)
                        if (!anchorsHere.Contains(image.Run)) { slipped = image; break; }
                    if (slipped != null && attempt < MaxRestarts)
                    {
                        obstacles.Remove(slipped);
                        if (_stopBefore == null || IsBefore(slipped.Run, _stopBefore)) _stopBefore = slipped.Run;
                        continue;
                    }
                    break;
                }
                _stopBefore = null;
                // Les obstacles autour desquels le texte a coulé se dessinent ici.
                foreach (var image in obstacles)
                    if (FindRun(page.Images, image.Run) == null) page.Images.Add(image);
                foreach (var image in page.Images) placedRuns.Add(image.Run);
                pages.Add(page);
                if (state.Paragraph >= paragraphs.Count) break;
            }
            PlaceNoteLines(pages, Current.TopPx, ContentHeight);
            Current.Pages = pages;
            return FirstChangedPage(previous, pages);
        }

        private static PlacedImage FindRun(List<PlacedImage> images, TextRun run)
        {
            foreach (var image in images)
                if (ReferenceEquals(image.Run, run)) return image;
            return null;
        }

        /// <summary>L'ancre devant laquelle la page en cours doit se fermer
        /// (son image l'avait poussée à la page suivante), ou null.</summary>
        private TextRun _stopBefore;

        /// <summary>Les runs d'ancre présents dans les lignes posées d'une page.</summary>
        private static HashSet<TextRun> AnchorsOn(ComposedPageLayout page)
        {
            var anchors = new HashSet<TextRun>();
            foreach (var placed in page.Lines)
                foreach (var piece in placed.Line.Pieces)
                    if (piece.IsAnchor && piece.AnchorRun != null) anchors.Add(piece.AnchorRun);
            return anchors;
        }

        /// <summary>a précède-t-il b dans le document ?</summary>
        private bool IsBefore(TextRun a, TextRun b)
        {
            foreach (var paragraph in _document.Paragraphs)
                foreach (var run in paragraph.Runs)
                {
                    if (ReferenceEquals(run, a)) return true;
                    if (ReferenceEquals(run, b)) return false;
                }
            return false;
        }

        private static bool ContainsAnchor(List<ComposedLine> lines, TextRun run)
        {
            if (run == null) return false;
            foreach (var line in lines)
                foreach (var piece in line.Pieces)
                    if (piece.IsAnchor && ReferenceEquals(piece.AnchorRun, run)) return true;
            return false;
        }

        /// <summary>Les obstacles courants d'une page : ses images posées et
        /// celles héritées des rejeux.</summary>
        private static List<PlacedImage> ActiveObstacles(ComposedPageLayout page, List<PlacedImage> obstacles)
        {
            if (obstacles.Count == 0) return page.Images;
            var active = new List<PlacedImage>(page.Images);
            foreach (var image in obstacles)
                if (FindRun(active, image.Run) == null) active.Add(image);
            return active;
        }

        private static void Close(PlaceState state, int paragraph, int lineIndex, int cursor, bool shaped, bool pageBreak)
        {
            state.Paragraph = paragraph;
            state.LineIndex = lineIndex;
            state.Cursor = cursor;
            state.Shaped = shaped;
            state.PageBreak = pageBreak;
        }

        /// <summary>Remplit UNE page depuis l'état donné ; à la sortie, l'état
        /// dit où la page suivante reprend (ou Paragraph = Count : fini).</summary>
        private void LayoutPage(ComposedPageLayout page, int pageIndex, PlaceState state,
            List<PlacedImage> obstacles, HashSet<TextRun> placedRuns)
        {
            var paragraphs = Current.Paragraphs;
            var top = Current.TopPx;
            var contentHeight = ContentHeight;
            var left = Current.LeftPxFor(pageIndex);
            var contentWidth = _setup.ContentWidthPx;
            var y = top;
            var noteHeight = 0.0; // reserved at the current page's bottom
            var pageStartIndex = state.Paragraph;
            var lineIndex = state.LineIndex;
            var cursor = state.Cursor;
            var shaped = state.Shaped;
            state.PageBreak = false;

            for (var p = state.Paragraph; p < paragraphs.Count; p++)
            {
                if (p != state.Paragraph) { lineIndex = 0; cursor = 0; shaped = false; }
                var paragraph = paragraphs[p];
                var atStart = lineIndex == 0 && cursor == 0;
                if (atStart && paragraph.PageBreakBefore && page.Lines.Count > 0)
                {
                    Close(state, p, 0, 0, false, true);
                    return;
                }
                if (atStart) y += paragraph.SpaceBefore;

                // La voie ligne à ligne : le paragraphe porte une ancre, ou sa
                // bande sur cette page heurte une image.
                if (!shaped && (paragraph.HasAnchor
                    || IntersectsObstacles(p, lineIndex, y, left, contentWidth, ActiveObstacles(page, obstacles))))
                {
                    shaped = true;
                    cursor = lineIndex > 0 && lineIndex < paragraph.Lines.Count ? paragraph.Lines[lineIndex].Start : 0;
                }
                if (shaped)
                {
                    if (PlaceShaped(page, pageIndex, p, ref cursor, ref y, ref noteHeight, obstacles, placedRuns))
                    {
                        Close(state, p, 0, cursor, true, false);
                        return;
                    }
                    y += paragraph.SpaceAfter;
                    continue;
                }

                // ---- la voie pleine colonne (les lignes composées une fois)
                var height = ParagraphHeight(paragraph);
                var wholeNotes = 0.0;
                {
                    var anyOnPage = page.NoteIndices.Count > 0;
                    foreach (var line in paragraph.Lines)
                    {
                        var extra = LineNotesHeight(paragraph, line, !anyOnPage);
                        if (extra > 0) anyOnPage = true;
                        wholeNotes += extra;
                    }
                }
                var fitsWhole = y + height <= top + contentHeight - noteHeight - wholeNotes + 0.5;
                if (!fitsWhole && paragraph.Style.KeepLinesTogether
                    && height <= contentHeight && page.Lines.Count > 0)
                {
                    var chainStart = ChainStart(paragraphs, p, pageStartIndex, contentHeight);
                    if (chainStart < p)
                    {
                        // La page se ferme AVANT la chaîne solidaire : ce qui
                        // en était déjà posé est retiré, elle repart entière.
                        Truncate(page, chainStart);
                        Close(state, chainStart, 0, 0, false, false);
                        return;
                    }
                    Close(state, p, 0, 0, false, false);
                    return;
                }

                while (lineIndex < paragraph.Lines.Count)
                {
                    var remaining = paragraph.Lines.Count - lineIndex;
                    var fit = 0;
                    var probe = y;
                    var probeNotes = 0.0;
                    var probeAny = page.NoteIndices.Count > 0;
                    for (var k = lineIndex; k < paragraph.Lines.Count; k++)
                    {
                        var candidate = paragraph.Lines[k];
                        var extra = LineNotesHeight(paragraph, candidate, !probeAny);
                        if (probe + candidate.Height
                            > top + contentHeight - noteHeight - probeNotes - extra + 0.5) break;
                        probe += candidate.Height;
                        probeNotes += extra;
                        if (extra > 0) probeAny = true;
                        fit++;
                    }
                    var pageEmpty = page.Lines.Count == 0 && y <= top + paragraph.SpaceBefore + 0.5;
                    if (fit < remaining)
                    {
                        // Contrôle veuves/orphelines 2/2 — débrayable paragraphe
                        // par paragraphe (AllowWidows) : la correction saute,
                        // le marqueur de marge reste (grisé) pour la rétablir.
                        var allow = p < _document.Paragraphs.Count
                            && _document.Paragraphs[p].AllowWidows;
                        var adjusted = false;
                        if (remaining - fit == 1 && fit > 1)
                        {
                            adjusted = true;
                            if (!allow) fit--;
                        }
                        if (fit == 1 && lineIndex == 0 && remaining >= 2 && !pageEmpty)
                        {
                            adjusted = true;
                            if (!allow) fit = 0;
                        }
                        if (fit <= 0 && pageEmpty) fit = 1;
                        if (adjusted)
                        {
                            double kept = 0;
                            for (var k = 0; k < fit && lineIndex + k < paragraph.Lines.Count; k++)
                                kept += paragraph.Lines[lineIndex + k].Height;
                            page.WidowMarks.Add(new WidowMark
                            {
                                ParagraphIndex = p,
                                Y = Math.Min(y + kept, top + contentHeight - 14),
                                Disabled = allow
                            });
                        }
                    }
                    for (var k = 0; k < fit; k++)
                    {
                        var line = paragraph.Lines[lineIndex];
                        page.Lines.Add(new PlacedLine { ParagraphIndex = p, LineIndex = lineIndex, Y = y, Line = line });
                        noteHeight += AddLineNotes(page, paragraph, line);
                        y += line.Height;
                        lineIndex++;
                    }
                    if (lineIndex < paragraph.Lines.Count)
                    {
                        Close(state, p, lineIndex, 0, false, false);
                        return;
                    }
                }
                y += paragraph.SpaceAfter;
            }
            Close(state, paragraphs.Count, 0, 0, false, false);
        }

        /// <summary>Retire de la page tout ce qui appartient aux paragraphes à
        /// partir de <paramref name="fromParagraph"/> (chaîne solidaire qui
        /// repart sur la page suivante) et recompte ses notes.</summary>
        private void Truncate(ComposedPageLayout page, int fromParagraph)
        {
            page.Lines.RemoveAll(delegate(PlacedLine placed) { return placed.ParagraphIndex >= fromParagraph; });
            page.Images.RemoveAll(delegate(PlacedImage image) { return image.ParagraphIndex >= fromParagraph; });
            page.WidowMarks.RemoveAll(delegate(WidowMark mark) { return mark.ParagraphIndex >= fromParagraph; });
            page.NoteIndices.Clear();
            foreach (var placed in page.Lines)
                AddLineNotes(page, Current.Paragraphs[placed.ParagraphIndex], placed.Line);
        }

        /// <summary>La bande qu'occuperaient les lignes restantes du paragraphe
        /// (pleine colonne) depuis y heurte-t-elle une image de la page ?</summary>
        private bool IntersectsObstacles(int p, int lineIndex, double y, double left, double contentWidth,
            List<PlacedImage> obstacles)
        {
            if (obstacles.Count == 0) return false;
            var layout = Current.Paragraphs[p];
            var height = 0.0;
            for (var l = lineIndex; l < layout.Lines.Count; l++) height += layout.Lines[l].Height;
            var bottom = Math.Min(y + height, Current.TopPx + ContentHeight);
            if (bottom <= y) return false;
            foreach (var image in obstacles)
                if (Overlaps(image.Rect, y, bottom - y, left, contentWidth)) return true;
            return false;
        }

        /// <summary>Une image (avec son air) touche-t-elle la bande [y, y+h)
        /// de la colonne ?</summary>
        private static bool Overlaps(Rect rect, double y, double h, double left, double width)
        {
            return rect.Bottom + ImageGap > y + 0.01 && rect.Y - ImageGap < y + h - 0.01
                && rect.Right > left + 0.01 && rect.X < left + width - 0.01;
        }

        /// <summary>Les segments de colonne disponibles pour une ligne de
        /// hauteur h à l'ordonnée y — y peut descendre (image « exclude »,
        /// ou image « wrap » qui ne laisse rien d'utilisable).</summary>
        private static List<Segment> SlotsAt(ref double y, double h, double left, double width,
            List<PlacedImage> obstacles)
        {
            var result = new List<Segment>();
            for (var guard = 0; guard < 64; guard++)
            {
                var pushed = false;
                foreach (var image in obstacles)
                {
                    if (image.Wrap || !Overlaps(image.Rect, y, h, left, width)) continue;
                    y = image.Rect.Bottom + ImageGap;
                    pushed = true;
                }
                if (pushed) continue; // tout se reteste à la nouvelle ordonnée
                result.Clear();
                result.Add(new Segment { X = 0, Width = width });
                foreach (var image in obstacles)
                {
                    if (!image.Wrap || !Overlaps(image.Rect, y, h, left, width)) continue;
                    var cut0 = image.Rect.X - ImageGap - left;
                    var cut1 = image.Rect.Right + ImageGap - left;
                    var next = new List<Segment>();
                    foreach (var segment in result)
                    {
                        var a = segment.X;
                        var b = segment.X + segment.Width;
                        if (cut1 <= a || cut0 >= b) { next.Add(segment); continue; }
                        if (cut0 > a) next.Add(new Segment { X = a, Width = cut0 - a });
                        if (cut1 < b) next.Add(new Segment { X = cut1, Width = b - cut1 });
                    }
                    result = next;
                }
                result.RemoveAll(delegate(Segment segment) { return segment.Width < MinSegmentPx; });
                if (result.Count > 0) return result;
                // Rien d'utilisable de part et d'autre : sous la plus basse
                // des images qui coupent la ligne.
                var lowest = double.MinValue;
                foreach (var image in obstacles)
                    if (image.Wrap && Overlaps(image.Rect, y, h, left, width))
                        lowest = Math.Max(lowest, image.Rect.Bottom);
                if (lowest == double.MinValue) break;
                y = lowest + ImageGap;
            }
            result.Clear();
            result.Add(new Segment { X = 0, Width = width });
            return result;
        }

        /// <summary>Une ligne plus haute que l'interligne prévu peut heurter une
        /// image « exclude » sous elle : elle passe dessous (sa largeur ne
        /// change pas).</summary>
        private static void PushBelowExclude(ref double y, double h, double left, double width,
            List<PlacedImage> obstacles)
        {
            for (var guard = 0; guard < 16; guard++)
            {
                var pushed = false;
                foreach (var image in obstacles)
                {
                    if (image.Wrap || !Overlaps(image.Rect, y, h, left, width)) continue;
                    y = image.Rect.Bottom + ImageGap;
                    pushed = true;
                }
                if (!pushed) return;
            }
        }

        private static bool HasVisibleContent(ComposedLine line)
        {
            foreach (var piece in line.Pieces)
                if (piece.Glyphs != null || piece.Fallback != null || piece.IsRule) return true;
            return false;
        }

        /// <summary>Positionne l'index d'atome sur l'offset plat de reprise ;
        /// un mot coupé par une césure à la page d'avant repart de sa fin.</summary>
        private void SkipTo(List<Atom> atoms, ref int index, int cursor, ParagraphStyle style)
        {
            if (cursor <= 0) return;
            while (index < atoms.Count)
            {
                var atom = atoms[index];
                if (atom.SourceStart < 0) { index++; continue; } // décor (puce, numéro) : jamais en milieu de paragraphe
                var end = atom.SourceStart + atom.SourceLength;
                if (end <= cursor) { index++; continue; }
                if (atom.SourceStart < cursor && atom.Text != null && !atom.IsSpace && !atom.Superscript)
                {
                    var cut = cursor - atom.SourceStart;
                    if (cut > 0 && cut < atom.Text.Length)
                    {
                        var rest = atom.Text.Substring(cut);
                        atoms[index] = new Atom
                        {
                            Text = rest,
                            Width = MeasureText(rest, atom.Font, atom.Size, atom.Tracking),
                            SpaceWidth = atom.SpaceWidth,
                            Font = atom.Font,
                            Size = atom.Size,
                            Ink = atom.Ink,
                            Tracking = atom.Tracking,
                            Underline = atom.Underline,
                            Strike = atom.Strike,
                            Highlight = atom.Highlight,
                            SourceStart = cursor,
                            SourceLength = atom.SourceLength - cut,
                            Breaks = atom.Breaks == null ? null : FrenchHyphenator.BreakPoints(rest,
                                style.HyphenMinWordLength, style.HyphenMinBefore, style.HyphenMinAfter)
                        };
                    }
                }
                return;
            }
        }

        private static int PlacedLinesOf(ComposedPageLayout page, int paragraphIndex)
        {
            var count = 0;
            foreach (var placed in page.Lines)
                if (placed.ParagraphIndex == paragraphIndex) count++;
            return count;
        }

        /// <summary>La voie ligne à ligne : pose le paragraphe p depuis
        /// l'offset plat <paramref name="cursor"/>, ligne après ligne, dans
        /// les segments que laissent les images. Rend vrai quand la page est
        /// pleine (cursor = l'offset de reprise), faux quand le paragraphe est
        /// entièrement posé. Pas de contrôle veuves/orphelines sur cette voie.</summary>
        private bool PlaceShaped(ComposedPageLayout page, int pageIndex, int p, ref int cursor,
            ref double y, ref double noteHeight, List<PlacedImage> obstacles, HashSet<TextRun> placedRuns)
        {
            var layout = Current.Paragraphs[p];
            var paragraph = _document.Paragraphs[p];
            var style = layout.Style;
            var top = Current.TopPx;
            var bottom = top + ContentHeight;
            var contentWidth = _setup.ContentWidthPx;
            var left = Current.LeftPxFor(pageIndex);

            List<Atom> cached;
            if (!_atomsOf.TryGetValue(layout, out cached))
                cached = BuildAtoms(paragraph, style, _listNumbers[p], _noteBases[p]);
            var atoms = new List<Atom>(cached);
            var index = 0;
            SkipTo(atoms, ref index, cursor, style);
            var align = paragraph.AlignOverride ?? style.Align;
            var leading = Leading(style);
            double leftIndent, firstX;
            paragraph.EffectiveIndents(style, out leftIndent, out firstX);
            var firstLineIndent = firstX - leftIndent;
            var first = cursor == 0;
            var consecutiveHyphens = 0;
            var lineCounter = PlacedLinesOf(page, p);

            while (index < atoms.Count || first)
            {
                var active = ActiveObstacles(page, obstacles);
                var lineY = y;
                var segments = SlotsAt(ref lineY, leading, left, contentWidth, active);
                if (lineY + leading > bottom - noteHeight + 0.5 && page.Lines.Count > 0) return true;

                // De quoi revenir en arrière si la ligne ne tient pas : la
                // césure remplace des atomes de la liste.
                var snapshot = new List<Atom>(atoms);
                var snapIndex = index;
                var snapCursor = cursor;
                var snapHyphens = consecutiveHyphens;
                var snapFirst = first;

                var lines = new List<ComposedLine>();
                foreach (var segment in segments)
                {
                    var s0 = Math.Max(segment.X, leftIndent + (first ? firstLineIndent : 0));
                    var s1 = Math.Min(segment.X + segment.Width, contentWidth - style.RightIndent);
                    if (s1 - s0 < MinSegmentPx && segments.Count > 1) continue;
                    var avail = Math.Max(40, s1 - s0);
                    var line = FillLine(atoms, ref index, ref cursor, avail, style, ref consecutiveHyphens);
                    line.EndsParagraph = index >= atoms.Count;
                    if (line.EndsParagraph) line.End = layout.FlatLength;
                    var lineAvail = avail - (line.EndsParagraph ? style.LastLineIndent : 0);
                    if (!line.ForcedBreak) JustifyLine(line, lineAvail, align, style);
                    var x = s0;
                    if (align == "center") x += Math.Max(0, (lineAvail - LineWidth(line)) / 2);
                    else if (align == "right") x += Math.Max(0, lineAvail - LineWidth(line));
                    OffsetLine(line, x);
                    FinalizeCharRights(line);
                    line.Height = line.Height < 1 ? leading : Math.Max(line.Height, leading);
                    lines.Add(line);
                    first = false;
                    if (index >= atoms.Count) break;
                }
                if (lines.Count == 0)
                {
                    // Tous les segments trop étroits pour les retraits : sous
                    // l'image la plus basse qui coupe la ligne.
                    var lowest = lineY;
                    foreach (var image in active)
                        if (Overlaps(image.Rect, lineY, leading, left, contentWidth))
                            lowest = Math.Max(lowest, image.Rect.Bottom + ImageGap);
                    y = lowest > lineY ? lowest : lineY + leading;
                    continue;
                }
                // La page se ferme devant cette ancre (son image l'avait
                // poussée à la page suivante) — jamais sur une page vide.
                if (_stopBefore != null && page.Lines.Count > 0 && ContainsAnchor(lines, _stopBefore))
                {
                    atoms = snapshot; index = snapIndex; cursor = snapCursor;
                    consecutiveHyphens = snapHyphens; first = snapFirst;
                    return true;
                }
                var lineHeight = 0.0;
                foreach (var line in lines) lineHeight = Math.Max(lineHeight, line.Height);
                PushBelowExclude(ref lineY, lineHeight, left, contentWidth, active);

                var notes = 0.0;
                var any = page.NoteIndices.Count > 0;
                foreach (var line in lines)
                {
                    var extra = LineNotesHeight(layout, line, !any);
                    if (extra > 0) any = true;
                    notes += extra;
                }
                var limit = bottom - noteHeight - notes;
                if (lineY + lineHeight > limit + 0.5 && page.Lines.Count > 0)
                {
                    atoms = snapshot; index = snapIndex; cursor = snapCursor;
                    consecutiveHyphens = snapHyphens; first = snapFirst;
                    return true;
                }

                // Les images dont l'ancre vient d'être posée.
                var imagesHere = new List<PlacedImage>();
                var rollback = false;
                foreach (var line in lines)
                {
                    foreach (var piece in line.Pieces)
                    {
                        if (!piece.IsAnchor || piece.AnchorRun == null) continue;
                        var run = piece.AnchorRun;
                        if (placedRuns.Contains(run) || FindRun(page.Images, run) != null || FindRun(imagesHere, run) != null) continue;
                        var image = FindRun(obstacles, run)
                            ?? BuildPlacedImage(run, p, pageIndex, lineY, lineY + lineHeight, !HasVisibleContent(line));
                        if (image.Attached && image.Rect.Bottom > limit + 0.5)
                        {
                            // L'image attachée ne tient pas sous sa ligne : la
                            // ligne et elle passent à la page suivante — sauf
                            // sur une page vide, où l'image se réduit à la place.
                            if (page.Lines.Count > 0) { rollback = true; break; }
                            var w = image.Rect.Width;
                            var h = image.Rect.Height;
                            ImageLayout.FitInside(ref w, ref h, contentWidth, Math.Max(ImageLayout.MinSizePx, limit - image.Rect.Y));
                            image.Rect = new Rect(image.Rect.X, image.Rect.Y, w, h);
                        }
                        imagesHere.Add(image);
                    }
                    if (rollback) break;
                }
                if (rollback)
                {
                    atoms = snapshot; index = snapIndex; cursor = snapCursor;
                    consecutiveHyphens = snapHyphens; first = snapFirst;
                    return true;
                }

                foreach (var image in imagesHere) page.Images.Add(image);
                foreach (var line in lines)
                {
                    page.Lines.Add(new PlacedLine { ParagraphIndex = p, LineIndex = lineCounter++, Y = lineY, Line = line });
                    noteHeight += AddLineNotes(page, layout, line);
                }
                y = lineY + lineHeight;
                if (index >= atoms.Count)
                {
                    cursor = layout.FlatLength;
                    return false;
                }
            }
            cursor = layout.FlatLength;
            return false;
        }

        // ============================================================ géométrie des images

        /// <summary>La taille affichée d'une image : celle du placement, sinon
        /// ses pixels (à 96 dpi), sinon un cadre de substitution.</summary>
        private static void NaturalSize(ImageLayout layout, ImageSource source, ProjectImage stored,
            out double width, out double height)
        {
            var ratio = source != null && source.Width > 0 ? source.Height / source.Width : 2.0 / 3.0;
            if (layout.Width > 0 && layout.Height > 0) { width = layout.Width; height = layout.Height; return; }
            if (layout.Width > 0) { width = layout.Width; height = width * ratio; return; }
            if (layout.Height > 0 && ratio > 0) { height = layout.Height; width = height / ratio; return; }
            if (source == null) { width = 120; height = 80; return; }
            var pixels = stored == null ? 0 : View.ImageCache.PixelWidthOf(stored.Bytes);
            width = pixels > 0 ? pixels : source.Width;
            height = width * ratio;
        }

        /// <summary>Le rectangle de page d'une image dont l'ancre vient d'être
        /// posée : sa taille réduite à la zone (ou à la page en placement
        /// libre), sa position explicite ou attachée à la ligne de l'ancre.</summary>
        private PlacedImage BuildPlacedImage(TextRun run, int paragraphIndex, int pageIndex,
            double anchorTop, double anchorBottom, bool anchorLineEmpty)
        {
            var layout = run.Image ?? new ImageLayout();
            var stored = _project == null ? null : _project.FindImage(run.ImageId);
            var source = View.ImageCache.For(stored); // décodée une fois, pas à chaque frappe (23/09)
            var left = Current.LeftPxFor(pageIndex);
            var top = Current.TopPx;
            var contentWidth = _setup.ContentWidthPx;
            var contentHeight = ContentHeight;
            var area = layout.Free
                ? new Rect(0, 0, Current.PageWidthPx, Current.PageHeightPx)
                : new Rect(left, top, contentWidth, contentHeight);
            double w, h;
            NaturalSize(layout, source, stored, out w, out h);
            ImageLayout.FitInside(ref w, ref h, area.Width, area.Height);
            var x = layout.X.HasValue ? left + layout.X.Value : left + (contentWidth - w) / 2;
            var y = layout.Y.HasValue ? top + layout.Y.Value
                : (anchorLineEmpty ? anchorTop : anchorBottom + ImageGap);
            var rect = new Rect(x, y, w, h);
            // Dans la zone (ou la page) : une image attachée ne borne que son
            // abscisse — son bas qui déborde la pousse à la page suivante.
            var clamped = ImageLayout.ClampInto(rect, area);
            rect = new Rect(clamped.X, layout.Y.HasValue ? clamped.Y : rect.Y, w, h);
            return new PlacedImage
            {
                Run = run,
                ParagraphIndex = paragraphIndex,
                Rect = rect,
                Source = source,
                Wrap = layout.IsWrap,
                Attached = !layout.Y.HasValue
            };
        }
    }
}
