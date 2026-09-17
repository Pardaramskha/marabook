using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.Print
{
    /// <summary>One positioned drawing piece of a composed line. Coordinates
    /// are line-relative: X across the text column, Y a baseline shift
    /// (superscripts). Each piece knows its source range in the paragraph's
    /// flat text, and the cumulative right edge of every character, so the
    /// editing caret can be placed to the pixel.</summary>
    public class ComposedPiece
    {
        public GlyphRun Glyphs;
        public FormattedText Fallback;
        public Point Origin;         // line-relative (x, baseline shift)
        public double ScaleX = 1.0;  // glyph scaling (justification)
        public Brush Ink = Brushes.Black;
        public ImageSource Image;
        public Rect Rect;            // images and rules, line-relative
        public bool IsRule;
        public bool IsSpace;         // invisible, occupies width
        public double SpaceWidth;    // spaces only (justified width included)
        public double SpaceNatural;  // pre-trim width — the caret advances past
                                     // a trailing space even though alignment
                                     // ignores it (Word behavior)

        public int SourceStart = -1; // flat offset in the paragraph, -1 = decoration
        public int SourceLength;
        public double[] CharRights;  // cumulative right x, piece-relative, per source char

        // Text decorations, carried through to every renderer (screen, print,
        // PDF): underline/strike drawn over the text, highlight behind it.
        public bool Underline, Strike;
        public Brush Highlight;
        public double FontSizePx;    // decoration geometry (spaces included)

        /// <summary>Visual advance of the piece (justified, scaled).</summary>
        public double VisualWidth()
        {
            if (IsSpace) return SpaceWidth;
            if (Glyphs != null)
            {
                double width = 0;
                foreach (var advance in Glyphs.AdvanceWidths) width += advance;
                return width * ScaleX;
            }
            if (Fallback != null)
                return Fallback.WidthIncludingTrailingWhitespace * ScaleX;
            return Rect.Width;
        }
    }

    /// <summary>A composed line: pieces, metrics, and the flat range
    /// [Start, End) of the paragraph it covers.</summary>
    public class ComposedLine
    {
        public List<ComposedPiece> Pieces = new List<ComposedPiece>();
        public double Height;
        public double Ascent;
        public int Start, End;
        public bool EndsParagraph;
        public bool Hyphenated;
        public bool ForcedBreak; // Shift+Enter ended this line
    }

    /// <summary>A paragraph fully composed into lines (page-agnostic).</summary>
    public class ComposedParagraphLayout
    {
        public List<ComposedLine> Lines = new List<ComposedLine>();
        public ParagraphStyle Style;
        public double SpaceBefore, SpaceAfter;
        public bool PageBreakBefore;
        public bool StartOnRecto; // books: chapter opens on an odd folio
        public int FlatLength;

        // Footnote markers of this paragraph: flat offset and global marker
        // index (document order) — pagination maps lines to their notes.
        public List<int> NoteOffsets;
        public List<int> NoteIndices;
    }

    /// <summary>One line placed on a page: which paragraph, which line, at
    /// which vertical position. Pieces are NOT copied — the renderer draws
    /// them through a translation, so repagination is cheap and pages can be
    /// compared for incremental redraws.</summary>
    public struct PlacedLine
    {
        public int ParagraphIndex;
        public int LineIndex;
        public double Y;
    }

    public class ComposedPageLayout
    {
        public List<PlacedLine> Lines = new List<PlacedLine>();

        // Bottom-of-page footnotes: which notes this page carries (marker
        // order), their placed lines (ParagraphIndex = index into
        // Composition.NoteParagraphs) and the separator rule position.
        public List<int> NoteIndices = new List<int>();
        public List<PlacedLine> NoteLines = new List<PlacedLine>();
        public double NotesRuleY = -1;

        // Widow/orphan corrections taken (or deliberately skipped) on this
        // page: the margin markers letting the writer toggle each one.
        public List<WidowMark> WidowMarks = new List<WidowMark>();
    }

    /// <summary>A widow/orphan decision point: pagination withheld lines here
    /// (Disabled=false), or would have but the paragraph allows the aberration
    /// (Disabled=true). Y is page-relative.</summary>
    public struct WidowMark
    {
        public int ParagraphIndex;
        public double Y;
        public bool Disabled;
    }

    public class Composition
    {
        public PageSetup Setup;
        public List<ComposedParagraphLayout> Paragraphs = new List<ComposedParagraphLayout>();
        public List<ComposedPageLayout> Pages = new List<ComposedPageLayout>();

        /// <summary>Signalements de correction à L'ÉCRAN, indexés par
        /// paragraphe (transitoire : posés par la vue Composition, jamais
        /// persistés — et jamais lus par l'aperçu, l'impression ni le PDF,
        /// qui passent par DrawPage(screenExtras=false) ou PdfWriter).</summary>
        public Dictionary<int, List<Correction.Finding>> ScreenFindings;

        /// <summary>One layout per footnote marker, in document order — placed
        /// at the bottom of the page carrying the marker.</summary>
        public List<ComposedParagraphLayout> NoteParagraphs = new List<ComposedParagraphLayout>();

        /// <summary>Folio of page index i = i + 1 + FolioOffset. Documents of
        /// a book carry the pages of the preceding documents here, so their
        /// pagination reads as the book's — and the margin mirroring follows
        /// the real folio parity.</summary>
        public int FolioOffset;

        /// <summary>Header/footer decor of the document (single docs); pages
        /// of a compiled book prefer the Decor riding on their paragraphs.</summary>
        public PageDecor DefaultDecor;

        /// <summary>The source pivot (composed paragraphs match its list 1:1).</summary>
        public TextDocument Source;

        /// <summary>The decor governing a page: its first paragraph's chapter
        /// decor (compiled books), else the document's.</summary>
        public PageDecor DecorOf(int pageIndex)
        {
            var page = Pages[pageIndex];
            if (page.Lines.Count > 0 && Source != null)
            {
                var paragraph = page.Lines[0].ParagraphIndex;
                if (paragraph < Source.Paragraphs.Count
                    && Source.Paragraphs[paragraph].Decor != null)
                    return Source.Paragraphs[paragraph].Decor;
            }
            return DefaultDecor;
        }

        public double PageWidthPx { get { return Setup.PageWidthMm * PageSetup.PxPerMm; } }
        public double PageHeightPx { get { return Setup.PageHeightMm * PageSetup.PxPerMm; } }
        public double LeftPx { get { return Setup.MarginLeftMm * PageSetup.PxPerMm; } }
        public double TopPx { get { return Setup.MarginTopMm * PageSetup.PxPerMm; } }
        public double BottomPx { get { return Setup.MarginBottomMm * PageSetup.PxPerMm; } }

        public int FolioOf(int pageIndex)
        {
            return pageIndex + 1 + FolioOffset;
        }

        /// <summary>Mirrored margins, spread-style (InDesign) : le petit fond
        /// (MarginLeft) borde la reliure — à gauche sur les rectos (folios
        /// impairs), à droite sur les versos.</summary>
        public double LeftPxFor(int pageIndex)
        {
            var mm = FolioOf(pageIndex) % 2 == 1 ? Setup.MarginLeftMm : Setup.MarginRightMm;
            return mm * PageSetup.PxPerMm;
        }
    }

    /// <summary>The 4b composition engine — the InDesign-style motor behind the
    /// in-app editable Composition mode, the page preview and printing. Breaks
    /// lines itself (greedy fit + French hyphenation), justifies within the
    /// word/letter/glyph ranges, honors keeps with widow/orphan control, and
    /// recomposes incrementally (one paragraph at a time) so typing stays
    /// fluid on long chapters.</summary>
    public class CompositionEngine
    {
        private readonly TextDocument _document;
        private readonly StyleSheet _styles;
        private readonly Project _project;
        private readonly PageSetup _setup;
        private readonly bool _appendNotes;
        private readonly IGlyphMetrics _metrics;

        // Per-paragraph numbering context (list numbers, footnote numbers):
        // recomposition compares them to catch renumbering ripples.
        private List<int> _listNumbers = new List<int>();
        private List<int> _noteBases = new List<int>();

        public Composition Current { get; private set; }

        /// <summary>Pages of the book that precede this document (0 outside a
        /// book). Applied at ComposeAll.</summary>
        public int FolioOffset;

        /// <summary>Header/footer decor of the document. Applied at ComposeAll.</summary>
        public PageDecor DefaultDecor;

        /// <summary>metrics : la seule route du moteur vers les largeurs de
        /// caractères (batch 24) — WpfGlyphMetrics dans l'app, StubGlyphMetrics
        /// dans les tests console.</summary>
        public CompositionEngine(TextDocument document, StyleSheet styles,
            PageSetup setup, Project project, bool appendNotes, IGlyphMetrics metrics)
        {
            _document = document;
            _styles = styles;
            _setup = setup;
            _project = project;
            _appendNotes = appendNotes;
            _metrics = metrics;
        }

        // Exceptions de césure du projet (mots à ne jamais couper) — figées à
        // chaque ComposeAll, comparaison insensible à la casse.
        private HashSet<string> _hyphenExceptions;

        private bool IsHyphenException(string word)
        {
            return _hyphenExceptions != null && _hyphenExceptions.Contains(word);
        }

        /// <summary>Full composition of every paragraph plus pagination.</summary>
        public void ComposeAll()
        {
            _hyphenExceptions = _project != null && _project.HyphenExceptions.Count > 0
                ? new HashSet<string>(_project.HyphenExceptions,
                    StringComparer.OrdinalIgnoreCase)
                : null;
            Current = new Composition
            {
                Setup = _setup,
                FolioOffset = FolioOffset,
                DefaultDecor = DefaultDecor,
                Source = _document
            };
            RefreshContexts();
            Current.Paragraphs.Clear();
            for (var i = 0; i < _document.Paragraphs.Count; i++)
                Current.Paragraphs.Add(ComposeParagraph(_document.Paragraphs[i],
                    _listNumbers[i], _noteBases[i]));
            _noteKeys.Clear(); // styles may have changed: rebuild every note
            RefreshNoteLayouts();
            Repaginate();
        }

        /// <summary>Recompose after an in-place edit of paragraph
        /// <paramref name="index"/>; ripples through paragraphs whose list or
        /// footnote numbering shifted. Returns the first page whose content
        /// changed. The pagination diff alone is NOT enough: same line at the
        /// same Y with new pieces (typical mid-line typing) must still redraw,
        /// so the edited paragraph's own page always counts as changed.</summary>
        public int RecomposeParagraph(int index)
        {
            var oldList = _listNumbers;
            var oldNotes = _noteBases;
            RefreshContexts();
            for (var i = 0; i < _document.Paragraphs.Count; i++)
            {
                var changed = i == index
                    || i >= oldList.Count
                    || _listNumbers[i] != oldList[i]
                    || _noteBases[i] != oldNotes[i];
                if (changed)
                    Current.Paragraphs[i] = ComposeParagraph(_document.Paragraphs[i],
                        _listNumbers[i], _noteBases[i]);
            }
            var noteChanges = RefreshNoteLayouts();
            var first = Math.Min(Repaginate(), FirstPageOf(index));
            return Math.Min(first, NotesFirstChangedPage(noteChanges));
        }

        /// <summary>First page carrying a line of this paragraph.</summary>
        public int FirstPageOf(int paragraphIndex)
        {
            for (var k = 0; k < Current.Pages.Count; k++)
                foreach (var placed in Current.Pages[k].Lines)
                    if (placed.ParagraphIndex == paragraphIndex) return k;
            return 0;
        }

        public int ParagraphInserted(int index)
        {
            Current.Paragraphs.Insert(index, null); // placeholder, filled below
            RefreshContexts();
            Current.Paragraphs[index] = ComposeParagraph(_document.Paragraphs[index],
                _listNumbers[index], _noteBases[index]);
            // The split sibling changed too.
            if (index > 0)
                Current.Paragraphs[index - 1] = ComposeParagraph(_document.Paragraphs[index - 1],
                    _listNumbers[index - 1], _noteBases[index - 1]);
            var noteChanges = RefreshNoteLayouts();
            var first = Math.Min(Repaginate(), FirstPageOf(Math.Max(0, index - 1)));
            return Math.Min(first, NotesFirstChangedPage(noteChanges));
        }

        public int ParagraphRemoved(int index, int mergedInto)
        {
            Current.Paragraphs.RemoveAt(index);
            RefreshContexts();
            if (mergedInto >= 0 && mergedInto < Current.Paragraphs.Count)
                Current.Paragraphs[mergedInto] = ComposeParagraph(_document.Paragraphs[mergedInto],
                    _listNumbers[mergedInto], _noteBases[mergedInto]);
            var noteChanges = RefreshNoteLayouts();
            var first = Math.Min(Repaginate(),
                FirstPageOf(Math.Max(0, Math.Min(mergedInto, Current.Paragraphs.Count - 1))));
            return Math.Min(first, NotesFirstChangedPage(noteChanges));
        }

        /// <summary>Footnote text edited outside the engine (the notes panel):
        /// recompose the affected note layouts and repaginate. Returns the
        /// first changed page, int.MaxValue when nothing moved.</summary>
        public int RefreshNotes()
        {
            if (Current == null) return int.MaxValue;
            var noteChanges = RefreshNoteLayouts();
            if (noteChanges.Count == 0) return int.MaxValue;
            return Math.Min(Repaginate(), NotesFirstChangedPage(noteChanges));
        }

        private void RefreshContexts()
        {
            _listNumbers = new List<int>();
            _noteBases = new List<int>();
            var listNumber = 0;
            var noteBase = 0;
            foreach (var paragraph in _document.Paragraphs)
            {
                listNumber = paragraph.ListKind == "number" ? listNumber + 1 : 0;
                _listNumbers.Add(listNumber);
                _noteBases.Add(noteBase);
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null) noteBase++;
            }
        }

        // ============================================================ footnotes

        // Reserved heights at the bottom of a page carrying notes.
        internal const double NotesRuleGap = 14; // separator rule + padding, once
        internal const double NoteGap = 2;       // between notes

        // Cache key per note layout: "number|text" — a note recomposes only
        // when its displayed number or its text changed.
        private List<string> _noteKeys = new List<string>();

        /// <summary>Texts of the notes in MARKER order (a note can live at a
        /// different index in Footnotes than its marker rank after cut/paste).</summary>
        private List<string> NoteTextsInMarkerOrder()
        {
            var texts = new List<string>();
            foreach (var paragraph in _document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null)
                    {
                        var note = _document.FindFootnote(run.FootnoteId);
                        texts.Add(note == null ? "" : note.Text);
                    }
            return texts;
        }

        /// <summary>Recompose the note layouts whose number or text changed.
        /// Returns the set of changed note indices (marker order).</summary>
        private HashSet<int> RefreshNoteLayouts()
        {
            var changed = new HashSet<int>();
            if (!_appendNotes)
            {
                if (Current.NoteParagraphs.Count > 0)
                {
                    Current.NoteParagraphs.Clear();
                    _noteKeys.Clear();
                    changed.Add(0);
                }
                return changed;
            }
            var texts = NoteTextsInMarkerOrder();
            var layouts = new List<ComposedParagraphLayout>();
            var keys = new List<string>();
            for (var i = 0; i < texts.Count; i++)
            {
                var key = (i + 1) + "|" + texts[i];
                keys.Add(key);
                if (i < _noteKeys.Count && _noteKeys[i] == key
                    && i < Current.NoteParagraphs.Count)
                {
                    layouts.Add(Current.NoteParagraphs[i]);
                    continue;
                }
                layouts.Add(ComposeNote(i, texts[i]));
                changed.Add(i);
            }
            if (texts.Count < _noteKeys.Count) changed.Add(texts.Count);
            _noteKeys = keys;
            Current.NoteParagraphs = layouts;
            return changed;
        }

        private ComposedParagraphLayout ComposeNote(int index, string text)
        {
            var body = _styles.Body;
            var style = body.Clone();
            style.FontSize = Math.Max(8, body.FontSize * 0.85);
            style.LineHeight = body.LineHeight > 1 ? body.LineHeight * 0.85 : 0;
            style.FirstLineIndent = 0;
            style.LeftIndent = 0;
            style.RightIndent = 0;
            style.LastLineIndent = 0;
            style.SpaceBefore = 0;
            style.SpaceAfter = 0;
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = (index + 1) + ". " + text });
            return ComposeWithStyle(paragraph, style, 0, 0);
        }

        /// <summary>First page whose bottom notes reference a recomposed note
        /// (the pagination diff alone cannot see a text change that moved no
        /// line).</summary>
        private int NotesFirstChangedPage(HashSet<int> changed)
        {
            if (changed.Count == 0) return int.MaxValue;
            for (var k = 0; k < Current.Pages.Count; k++)
                foreach (var placed in Current.Pages[k].NoteLines)
                    if (changed.Contains(placed.ParagraphIndex)) return k;
            return int.MaxValue;
        }

        /// <summary>Reserved bottom height for the notes called from this line
        /// (0 when the line calls none). firstOnPage adds the rule gap.</summary>
        private double LineNotesHeight(ComposedParagraphLayout paragraph,
            ComposedLine line, bool firstOnPage)
        {
            if (paragraph.NoteIndices == null) return 0;
            double height = 0;
            var any = false;
            for (var j = 0; j < paragraph.NoteIndices.Count; j++)
            {
                var offset = paragraph.NoteOffsets[j];
                if (offset < line.Start || offset >= line.End) continue;
                var index = paragraph.NoteIndices[j];
                if (index < 0 || index >= Current.NoteParagraphs.Count) continue;
                height += ParagraphHeight(Current.NoteParagraphs[index]) + NoteGap;
                any = true;
            }
            if (any && firstOnPage) height += NotesRuleGap;
            return height;
        }

        /// <summary>Registers the line's notes on the page; returns the height
        /// newly reserved at the page bottom.</summary>
        private double AddLineNotes(ComposedPageLayout page,
            ComposedParagraphLayout paragraph, ComposedLine line)
        {
            if (paragraph.NoteIndices == null) return 0;
            double height = 0;
            for (var j = 0; j < paragraph.NoteIndices.Count; j++)
            {
                var offset = paragraph.NoteOffsets[j];
                if (offset < line.Start || offset >= line.End) continue;
                var index = paragraph.NoteIndices[j];
                if (index < 0 || index >= Current.NoteParagraphs.Count) continue;
                if (page.NoteIndices.Count == 0) height += NotesRuleGap;
                page.NoteIndices.Add(index);
                height += ParagraphHeight(Current.NoteParagraphs[index]) + NoteGap;
            }
            return height;
        }

        // ============================================================ fonts
        // FontInfo, FontCache (le cache de mesure historique) et l'interface
        // IGlyphMetrics vivent dans Print/GlyphMetrics.cs depuis le batch 24.

        /// <summary>tracking : approche en millièmes de cadratin, ajoutée à
        /// l'avance de CHAQUE caractère (unités InDesign). Les largeurs
        /// passent par _metrics — l'unique couture entre la composition et
        /// les polices réelles.</summary>
        private double MeasureText(string text, FontInfo font, double size,
            double tracking = 0)
        {
            return _metrics.AdvanceWidth(font.Family, size, font.WeightValue,
                font.Italic, text) + size * tracking / 1000.0 * text.Length;
        }

        // ============================================================ atoms

        private sealed class Atom
        {
            public string Text;
            public bool IsSpace;
            public bool IsForcedBreak; // Shift+Enter
            public double Width;
            public double SpaceWidth;
            public FontInfo Font;
            public double Size;
            public Brush Ink;
            public bool Superscript;
            public List<int> Breaks;
            public ImageSource Image;
            public double ImageWidth, ImageHeight;
            public bool IsRule;
            public int SourceStart = -1;
            public int SourceLength;
            public bool Underline, Strike;
            public Brush Highlight;
            public double Tracking; // approche (em/1000)
        }

        private ComposedParagraphLayout ComposeParagraph(TextParagraph paragraph,
            int listNumber, int noteBase)
        {
            var layout = ComposeWithStyle(paragraph, _styles.Find(paragraph.StyleId),
                listNumber, noteBase);

            // Map the paragraph's footnote markers (flat offset → global marker
            // index) so pagination can pull each note to its page bottom.
            var offset = 0;
            var seen = 0;
            foreach (var run in paragraph.Runs)
            {
                if (run.FootnoteId != null)
                {
                    if (layout.NoteOffsets == null)
                    {
                        layout.NoteOffsets = new List<int>();
                        layout.NoteIndices = new List<int>();
                    }
                    layout.NoteOffsets.Add(offset);
                    layout.NoteIndices.Add(noteBase + seen);
                    seen++;
                    offset++;
                    continue;
                }
                if (run.IsLineBreak || run.IsRule || run.ImageId != null) { offset++; continue; }
                offset += run.Text.Length;
            }
            return layout;
        }

        private ComposedParagraphLayout ComposeWithStyle(TextParagraph paragraph,
            ParagraphStyle style, int listNumber, int noteBase)
        {
            var layout = new ComposedParagraphLayout
            {
                Style = style,
                SpaceBefore = style.SpaceBefore,
                SpaceAfter = style.SpaceAfter,
                PageBreakBefore = paragraph.PageBreakBefore,
                StartOnRecto = paragraph.StartOnRecto,
                FlatLength = PivotEdit.FlatLength(paragraph)
            };

            var contentWidth = _setup.ContentWidthPx;
            var atoms = BuildAtoms(paragraph, style, listNumber, noteBase);
            var align = paragraph.AlignOverride ?? style.Align;
            var leading = style.LineHeight > 1
                ? style.LineHeight
                : style.FontSize * Math.Max(100, style.AutoLeadingPercent) / 100.0;
            // Décalage du paragraphe (17/09) : une valeur remplace retrait
            // gauche, alinéa et retrait de liste d'un bloc — 0 = à la marge.
            var leftIndent = paragraph.Indent ?? (style.LeftIndent + (paragraph.ListKind != null ? 24 : 0));
            var firstLineIndent = paragraph.Indent.HasValue ? 0 : style.FirstLineIndent;
            var baseAvail = Math.Max(40, contentWidth - leftIndent - style.RightIndent);

            var index = 0;
            var cursor = 0;
            var first = true;
            var consecutiveHyphens = 0;
            while (index < atoms.Count || first)
            {
                var avail = baseAvail - (first ? firstLineIndent : 0);
                var line = FillLine(atoms, ref index, ref cursor, avail, style, ref consecutiveHyphens);
                line.EndsParagraph = index >= atoms.Count;
                if (line.EndsParagraph) line.End = layout.FlatLength;

                var lineAvail = avail - (line.EndsParagraph ? style.LastLineIndent : 0);
                if (!line.ForcedBreak) JustifyLine(line, lineAvail, align, style);

                var x = leftIndent + (first ? firstLineIndent : 0);
                if (align == "center") x += Math.Max(0, (lineAvail - LineWidth(line)) / 2);
                else if (align == "right") x += Math.Max(0, lineAvail - LineWidth(line));
                OffsetLine(line, x);
                FinalizeCharRights(line);

                line.Height = line.Height < 1 ? leading : Math.Max(line.Height, leading);
                layout.Lines.Add(line);
                first = false;
                if (index >= atoms.Count) break;
            }
            return layout;
        }

        private List<Atom> BuildAtoms(TextParagraph paragraph, ParagraphStyle style,
            int listNumber, int noteBase)
        {
            var atoms = new List<Atom>();
            if (paragraph.ListKind == "bullet")
                AddTextAtoms(atoms, "•  ", style, null, false, -1);
            else if (paragraph.ListKind == "number")
                AddTextAtoms(atoms, listNumber + ".  ", style, null, false, -1);

            var offset = 0;
            var notesSeen = 0;
            foreach (var run in paragraph.Runs)
            {
                if (run.IsLineBreak)
                {
                    atoms.Add(new Atom { IsForcedBreak = true, SourceStart = offset, SourceLength = 1 });
                    offset++;
                    continue;
                }
                if (run.IsRule)
                {
                    atoms.Add(new Atom { IsRule = true, SourceStart = offset, SourceLength = 1 });
                    offset++;
                    continue;
                }
                if (run.ImageId != null)
                {
                    var stored = _project == null ? null : _project.FindImage(run.ImageId);
                    var source = stored == null || stored.Bytes == null
                        ? null : View.MediaView.TryImage(stored.Bytes, 0);
                    if (source != null)
                    {
                        var w = Math.Min(source.Width, 480.0);
                        atoms.Add(new Atom
                        {
                            Image = source,
                            ImageWidth = w,
                            ImageHeight = source.Height * (w / source.Width),
                            SourceStart = offset,
                            SourceLength = 1
                        });
                    }
                    offset++;
                    continue;
                }
                if (run.FootnoteId != null)
                {
                    notesSeen++;
                    AddTextAtoms(atoms, (noteBase + notesSeen).ToString(), style, run, true, offset);
                    offset++;
                    continue;
                }
                AddTextAtoms(atoms, run.Text, style, run, false, offset);
                offset += run.Text.Length;
            }
            return atoms;
        }

        /// <summary>sourceBase &lt; 0 = decoration (list prefix): occupies no
        /// flat offsets, the caret skips it.</summary>
        private void AddTextAtoms(List<Atom> atoms, string text, ParagraphStyle style,
            TextRun run, bool superscript, int sourceBase)
        {
            if (string.IsNullOrEmpty(text)) return;
            var family = run != null && run.FontFamily != null ? run.FontFamily : style.FontFamily;
            var bold = run != null && run.Bold.HasValue ? run.Bold.Value : style.Bold;
            var italic = run != null && run.Italic.HasValue ? run.Italic.Value : style.Italic;
            var size = run != null && run.FontSize.HasValue ? run.FontSize.Value : style.FontSize;
            if (superscript) size = Math.Max(6, size * 0.65);
            var weight = run != null && run.Weight != null
                ? View.FlowConverter.ParseWeight(run.Weight)
                : (bold ? FontWeights.Bold : FontWeights.Normal);
            var font = FontCache.Resolve(family, weight, italic);
            Brush ink = Brushes.Black;
            if (run != null && run.Color != null)
                ink = new SolidColorBrush(View.FlowConverter.ParseColor(run.Color));
            else if (style.Color != null)
                ink = new SolidColorBrush(View.FlowConverter.ParseColor(style.Color));

            var underline = run != null && run.Underline == true && !superscript;
            var strike = run != null && run.Strike == true && !superscript;
            Brush highlight = run != null && run.Highlight != null
                ? new SolidColorBrush(View.FlowConverter.ParseColor(run.Highlight))
                : null;
            // Passage annoté (révision) : teinte semi-transparente, filtrée par
            // les rendus papier (aperçu, impression, PDF) sur son alpha — et
            // éteinte quand l'utilisateur masque les annotations.
            if (highlight == null && run != null && run.AnnotationId != null
                && Settings.AppSettings.ShowAnnotations)
            {
                var annotation = _document == null ? null
                    : _document.FindAnnotation(run.AnnotationId);
                if (annotation != null && !annotation.Resolved)
                    highlight = View.Chrome.AnnotationTint;
            }
            var tracking = run != null && run.Tracking.HasValue ? run.Tracking.Value : 0;

            var spaceWidth = MeasureText(" ", font, size, tracking);
            var start = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                var isSpace = i < text.Length && text[i] == ' ';
                if (i == text.Length || isSpace)
                {
                    if (i > start)
                    {
                        var word = text.Substring(start, i - start);
                        atoms.Add(new Atom
                        {
                            Text = word,
                            Width = MeasureText(word, font, size, tracking),
                            SpaceWidth = spaceWidth,
                            Font = font,
                            Size = size,
                            Ink = ink,
                            Tracking = tracking,
                            Superscript = superscript,
                            SourceStart = sourceBase < 0 ? -1 : (superscript ? sourceBase : sourceBase + start),
                            SourceLength = superscript ? 1 : word.Length,
                            Underline = underline,
                            Strike = strike,
                            Highlight = highlight,
                            // La césure obéit au bouton du document (PageSetup)
                            // ET au réglage du style — et jamais sur un mot
                            // des exceptions du projet.
                            Breaks = _setup.Hyphenation && style.HyphenationEnabled
                                && !superscript && !IsHyphenException(word)
                                ? FrenchHyphenator.BreakPoints(word,
                                    style.HyphenMinWordLength, style.HyphenMinBefore, style.HyphenMinAfter)
                                : null
                        });
                    }
                    if (isSpace)
                        atoms.Add(new Atom
                        {
                            IsSpace = true,
                            Width = spaceWidth,
                            SpaceWidth = spaceWidth,
                            Font = font,
                            Size = size,
                            Ink = ink,
                            Tracking = tracking,
                            Underline = underline,
                            Strike = strike,
                            Highlight = highlight,
                            SourceStart = sourceBase < 0 ? -1 : sourceBase + i,
                            SourceLength = 1
                        });
                    start = i + 1;
                }
            }
        }

        // ============================================================ line fill

        private ComposedLine FillLine(List<Atom> atoms, ref int index, ref int cursor,
            double avail, ParagraphStyle style, ref int consecutiveHyphens)
        {
            var line = new ComposedLine { Start = cursor };
            double x = 0, ascent = 0, height = 0;
            var contentPlaced = false;

            while (index < atoms.Count)
            {
                var atom = atoms[index];

                if (atom.IsForcedBreak)
                {
                    index++;
                    cursor = Advance(cursor, atom);
                    line.ForcedBreak = true;
                    break;
                }
                if (atom.IsRule)
                {
                    if (contentPlaced) break;
                    line.Pieces.Add(new ComposedPiece
                    {
                        IsRule = true,
                        Rect = new Rect(0, 0, avail, 1.5),
                        SourceStart = atom.SourceStart,
                        SourceLength = 1
                    });
                    line.Height = 14;
                    line.Ascent = 7;
                    index++;
                    cursor = Advance(cursor, atom);
                    line.End = cursor;
                    return line;
                }
                if (atom.Image != null)
                {
                    if (contentPlaced) break;
                    var w = Math.Min(atom.ImageWidth, avail);
                    var h = atom.ImageHeight * (w / atom.ImageWidth);
                    line.Pieces.Add(new ComposedPiece
                    {
                        Image = atom.Image,
                        Rect = new Rect(0, 0, w, h),
                        SourceStart = atom.SourceStart,
                        SourceLength = 1
                    });
                    line.Height = h + 6;
                    line.Ascent = h;
                    index++;
                    cursor = Advance(cursor, atom);
                    line.End = cursor;
                    return line;
                }

                if (atom.IsSpace && !contentPlaced)
                {
                    index++;
                    cursor = Advance(cursor, atom); // eaten leading space
                    continue;
                }

                if (x + atom.Width <= avail + 0.05)
                {
                    if (atom.IsSpace)
                    {
                        line.Pieces.Add(new ComposedPiece
                        {
                            IsSpace = true,
                            Origin = new Point(x, 0),
                            SpaceWidth = atom.Width,
                            SpaceNatural = atom.Width,
                            SourceStart = atom.SourceStart,
                            SourceLength = 1,
                            Ink = atom.Ink,
                            Underline = atom.Underline,
                            Strike = atom.Strike,
                            Highlight = atom.Highlight,
                            FontSizePx = atom.Size
                        });
                    }
                    else
                    {
                        AddAtomPiece(line, atom, atom.Text, atom.SourceLength, x);
                        contentPlaced = true;
                    }
                    UpdateMetrics(atom, ref ascent, ref height);
                    x += atom.Width;
                    index++;
                    cursor = Advance(cursor, atom);
                    continue;
                }

                // Overflow: hyphenation attempt — INCLUDING for the first atom
                // of the line. Un mot plus large que la colonne doit d'abord
                // tenter ses points de coupe ; le débordement dans la marge
                // n'est que le dernier recours (mot incoupable trop large).
                if (atom.Breaks != null && atom.Breaks.Count > 0
                    && style.HyphenConsecutiveLimit > 0
                    && consecutiveHyphens < style.HyphenConsecutiveLimit)
                {
                    var bestCut = -1;
                    foreach (var cut in atom.Breaks)
                    {
                        var prefix = atom.Text.Substring(0, cut) + "-";
                        if (x + MeasureText(prefix, atom.Font, atom.Size, atom.Tracking) <= avail + 0.05)
                            bestCut = cut;
                    }
                    if (bestCut > 0)
                    {
                        AddAtomPiece(line, atom, atom.Text.Substring(0, bestCut) + "-", bestCut, x);
                        UpdateMetrics(atom, ref ascent, ref height);
                        var rest = atom.Text.Substring(bestCut);
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
                            SourceStart = atom.SourceStart < 0 ? -1 : atom.SourceStart + bestCut,
                            SourceLength = atom.SourceLength - bestCut,
                            Breaks = FrenchHyphenator.BreakPoints(rest,
                                style.HyphenMinWordLength, style.HyphenMinBefore, style.HyphenMinAfter)
                        };
                        line.Hyphenated = true;
                        consecutiveHyphens++;
                        cursor += bestCut;
                        line.Ascent = ascent;
                        line.Height = height;
                        line.End = cursor;
                        TrimTrailingSpaces(line);
                        return line;
                    }
                }

                // Last resort: nothing on the line yet and no exploitable cut —
                // the atom is placed overflowing so composition always advances
                // (never an infinite loop, never an empty line).
                if (!contentPlaced && !atom.IsSpace)
                {
                    AddAtomPiece(line, atom, atom.Text, atom.SourceLength, x);
                    contentPlaced = true;
                    UpdateMetrics(atom, ref ascent, ref height);
                    x += atom.Width;
                    index++;
                    cursor = Advance(cursor, atom);
                    continue;
                }
                break;
            }

            if (!line.Hyphenated) consecutiveHyphens = 0;
            line.Ascent = ascent;
            line.Height = height;
            line.End = cursor;
            TrimTrailingSpaces(line);
            return line;
        }

        private static int Advance(int cursor, Atom atom)
        {
            return atom.SourceStart < 0 ? cursor : cursor + atom.SourceLength;
        }

        private void UpdateMetrics(Atom atom, ref double ascent, ref double height)
        {
            if (atom.Font == null) return;
            var m = _metrics.Metrics(atom.Font.Family, atom.Size,
                atom.Font.WeightValue, atom.Font.Italic);
            if (m.Ascent > ascent) ascent = m.Ascent;
            if (m.LineHeight > height) height = m.LineHeight;
        }

        private static void TrimTrailingSpaces(ComposedLine line)
        {
            // Trailing spaces stay addressable (their offsets are in the line
            // range) but stop occupying justified width.
            for (var i = line.Pieces.Count - 1; i >= 0; i--)
            {
                if (!line.Pieces[i].IsSpace) break;
                line.Pieces[i].SpaceWidth = 0;
            }
        }

        private void AddAtomPiece(ComposedLine line, Atom atom, string text,
            int sourceLength, double x)
        {
            var y = atom.Superscript ? -atom.Size * 0.35 : 0;
            var piece = BuildTextPiece(text, atom.Font, atom.Size, atom.Ink,
                new Point(x, y), atom.Tracking);
            piece.SourceStart = atom.SourceStart;
            piece.SourceLength = atom.SourceStart < 0 ? 0 : sourceLength;
            piece.Underline = atom.Underline;
            piece.Strike = atom.Strike;
            piece.Highlight = atom.Highlight;
            piece.FontSizePx = atom.Size;
            line.Pieces.Add(piece);
        }

        private static ComposedPiece BuildTextPiece(string text, FontInfo font, double size,
            Brush ink, Point origin, double tracking = 0)
        {
            if (font.Glyphs != null)
            {
                var extra = size * tracking / 1000.0; // approche par caractère
                var indices = new List<ushort>();
                var advances = new List<double>();
                var complete = true;
                foreach (var c in text)
                {
                    ushort glyph;
                    if (!font.Glyphs.CharacterToGlyphMap.TryGetValue(c, out glyph))
                    { complete = false; break; }
                    indices.Add(glyph);
                    advances.Add(font.Glyphs.AdvanceWidths[glyph] * size + extra);
                }
                if (complete && indices.Count > 0)
                    return new ComposedPiece
                    {
                        Glyphs = new GlyphRun(font.Glyphs, 0, false, size, 1.0f,
                            indices, new Point(0, 0), advances,
                            null, null, null, null, null, null),
                        Origin = origin,
                        Ink = ink
                    };
            }
            return new ComposedPiece
            {
                Fallback = new FormattedText(text, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, font.Typeface, size, ink, 1.0),
                Origin = origin,
                Ink = ink
            };
        }

        // ============================================================ justification

        private static double LineWidth(ComposedLine line)
        {
            double width = 0;
            foreach (var piece in line.Pieces)
            {
                if (piece.IsSpace) { width = Math.Max(width, piece.Origin.X + piece.SpaceWidth); continue; }
                width = Math.Max(width, piece.Origin.X + PieceWidth(piece));
            }
            return width;
        }

        private static double PieceWidth(ComposedPiece piece)
        {
            return piece.VisualWidth();
        }

        private void JustifyLine(ComposedLine line, double avail, string align, ParagraphStyle style)
        {
            if (align != "justify" || line.EndsParagraph || line.Pieces.Count == 0) return;

            var spaces = new List<ComposedPiece>();
            double spaceBase = 0;
            foreach (var piece in line.Pieces)
                if (piece.IsSpace && piece.SpaceWidth > 0) { spaces.Add(piece); spaceBase += piece.SpaceWidth; }

            var natural = LineWidth(line);
            var delta = avail - natural;
            if (Math.Abs(delta) < 0.25) return;

            double spaceAdjust = 0, glyphScale = 1.0, letterAdd = 0;
            if (spaces.Count > 0)
            {
                var min = spaceBase * (style.JustifyWordMin - 100) / 100.0;
                var max = spaceBase * (style.JustifyWordMax - 100) / 100.0;
                spaceAdjust = Math.Max(min, Math.Min(max, delta));
                delta -= spaceAdjust;
            }
            if (Math.Abs(delta) > 0.25 && spaces.Count > 0)
            {
                var gaps = 0;
                foreach (var piece in line.Pieces)
                    if (piece.Glyphs != null && piece.Glyphs.AdvanceWidths.Count > 1)
                        gaps += piece.Glyphs.AdvanceWidths.Count - 1;
                if (gaps > 0)
                {
                    var em = spaces[0].SpaceWidth;
                    var minAdd = em * style.JustifyLetterMin / 100.0;
                    var maxAdd = em * style.JustifyLetterMax / 100.0;
                    letterAdd = Math.Max(minAdd, Math.Min(maxAdd, delta / gaps));
                    delta -= letterAdd * gaps;
                }
            }
            if (Math.Abs(delta) > 0.25)
            {
                var textWidth = natural - spaceBase;
                if (textWidth > 1)
                {
                    var wanted = (textWidth + delta) / textWidth;
                    glyphScale = Math.Max(style.JustifyGlyphMin / 100.0,
                        Math.Min(style.JustifyGlyphMax / 100.0, wanted));
                    delta -= textWidth * (glyphScale - 1.0);
                }
            }
            if (Math.Abs(delta) > 0.25 && spaces.Count > 0)
                spaceAdjust += delta; // déversoir d'urgence (plafonds au backlog)

            var perSpace = spaces.Count > 0 ? spaceAdjust / spaces.Count : 0;
            double x = 0;
            foreach (var piece in line.Pieces)
            {
                if (piece.IsSpace)
                {
                    piece.Origin = new Point(x, 0);
                    if (piece.SpaceWidth > 0)
                    {
                        piece.SpaceWidth += perSpace;
                        // Garde de rendu : sur une ligne sur-remplie le déversoir
                        // peut être très négatif — un espace ne descend jamais
                        // sous JustifyWordMin % de sa largeur naturelle, sinon
                        // les mots se chevauchent.
                        var floor = piece.SpaceNatural * style.JustifyWordMin / 100.0;
                        if (piece.SpaceWidth < floor) piece.SpaceWidth = floor;
                    }
                    x += piece.SpaceWidth;
                    continue;
                }
                piece.Origin = new Point(x, piece.Origin.Y);
                piece.ScaleX = glyphScale;
                if (piece.Glyphs != null)
                {
                    double w = 0;
                    for (var g = 0; g < piece.Glyphs.AdvanceWidths.Count; g++)
                    {
                        piece.Glyphs.AdvanceWidths[g] += letterAdd / Math.Max(0.01, glyphScale);
                        w += piece.Glyphs.AdvanceWidths[g];
                    }
                    x += w * glyphScale;
                }
                else
                    x += PieceWidth(piece);
            }
        }

        private static void OffsetLine(ComposedLine line, double x)
        {
            foreach (var piece in line.Pieces)
            {
                piece.Origin = new Point(piece.Origin.X + x, piece.Origin.Y);
                if (piece.Image != null || piece.IsRule)
                    piece.Rect = new Rect(piece.Rect.X + x, piece.Rect.Y,
                        piece.Rect.Width, piece.Rect.Height);
            }
        }

        /// <summary>Cumulative per-character right edges (piece-relative),
        /// computed after justification so the caret math is exact.</summary>
        private static void FinalizeCharRights(ComposedLine line)
        {
            foreach (var piece in line.Pieces)
            {
                if (piece.SourceStart < 0 || piece.SourceLength <= 0) continue;
                if (piece.IsSpace)
                {
                    // Trimmed trailing spaces keep their natural advance for
                    // the caret (typing a space must move the caret at once).
                    piece.CharRights = new[]
                    {
                        piece.SpaceWidth > 0.01 ? piece.SpaceWidth : piece.SpaceNatural
                    };
                    continue;
                }
                var rights = new double[piece.SourceLength];
                if (piece.Glyphs != null)
                {
                    // Glyph count may exceed source length (hyphen appended):
                    // spread the visible advances over the source characters.
                    double total = 0;
                    var advances = piece.Glyphs.AdvanceWidths;
                    var glyphsPerChar = (double)advances.Count / piece.SourceLength;
                    var g = 0;
                    for (var c = 0; c < piece.SourceLength; c++)
                    {
                        var until = (int)Math.Round((c + 1) * glyphsPerChar);
                        if (c == piece.SourceLength - 1) until = advances.Count;
                        for (; g < until && g < advances.Count; g++) total += advances[g];
                        rights[c] = total * piece.ScaleX;
                    }
                }
                else
                {
                    var width = PieceWidth(piece);
                    for (var c = 0; c < piece.SourceLength; c++)
                        rights[c] = width * (c + 1) / piece.SourceLength;
                }
                piece.CharRights = rights;
            }
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
            var setup = _setup;
            var top = Current.TopPx;
            var bottom = Current.BottomPx;
            var pageHeight = Current.PageHeightPx;
            var contentHeight = pageHeight - top - bottom;

            var page = new ComposedPageLayout();
            pages.Add(page);
            var y = top;
            var noteHeight = 0.0; // reserved at the current page's bottom
            var pageStartIndex = 0;
            var paragraphs = Current.Paragraphs;

            for (var p = 0; p < paragraphs.Count; p++)
            {
                var paragraph = paragraphs[p];
                if (paragraph.PageBreakBefore && page.Lines.Count > 0)
                {
                    page = new ComposedPageLayout();
                    pages.Add(page);
                    // Chapter start of a book: land on a RECTO — an even folio
                    // gets a blank verso inserted before it.
                    if (paragraph.StartOnRecto
                        && (pages.Count + Current.FolioOffset) % 2 == 0)
                    {
                        page = new ComposedPageLayout();
                        pages.Add(page);
                    }
                    y = top;
                    noteHeight = 0;
                    pageStartIndex = p;
                }
                y += paragraph.SpaceBefore;

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
                        // Rebuild the page without the chain, replay from there.
                        var keepFrom = pageStartIndex;
                        pages.RemoveAt(pages.Count - 1);
                        page = new ComposedPageLayout();
                        pages.Add(page);
                        y = top;
                        noteHeight = 0;
                        for (var i = keepFrom; i < chainStart; i++)
                        {
                            y += paragraphs[i].SpaceBefore;
                            for (var l = 0; l < paragraphs[i].Lines.Count; l++)
                            {
                                page.Lines.Add(new PlacedLine { ParagraphIndex = i, LineIndex = l, Y = y });
                                noteHeight += AddLineNotes(page, paragraphs[i], paragraphs[i].Lines[l]);
                                y += paragraphs[i].Lines[l].Height;
                            }
                            y += paragraphs[i].SpaceAfter;
                        }
                        page = new ComposedPageLayout();
                        pages.Add(page);
                        y = top;
                        noteHeight = 0;
                        pageStartIndex = chainStart;
                        p = chainStart - 1;
                        continue;
                    }
                    page = new ComposedPageLayout();
                    pages.Add(page);
                    y = top;
                    noteHeight = 0;
                    pageStartIndex = p;
                }

                var lineIndex = 0;
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
                        page.Lines.Add(new PlacedLine { ParagraphIndex = p, LineIndex = lineIndex, Y = y });
                        noteHeight += AddLineNotes(page, paragraph, paragraph.Lines[lineIndex]);
                        y += paragraph.Lines[lineIndex].Height;
                        lineIndex++;
                    }
                    if (lineIndex < paragraph.Lines.Count)
                    {
                        page = new ComposedPageLayout();
                        pages.Add(page);
                        y = top;
                        noteHeight = 0;
                        pageStartIndex = p;
                    }
                }
                y += paragraph.SpaceAfter;
            }

            PlaceNoteLines(pages, top, contentHeight);
            Current.Pages = pages;
            return FirstChangedPage(previous, pages);
        }

        /// <summary>Stacks each page's notes at the bottom of its text area,
        /// mirroring the heights reserved during placement, and positions the
        /// separator rule.</summary>
        private void PlaceNoteLines(List<ComposedPageLayout> pages, double top, double contentHeight)
        {
            foreach (var page in pages)
            {
                if (page.NoteIndices.Count == 0) { page.NotesRuleY = -1; continue; }
                var total = NotesRuleGap;
                foreach (var index in page.NoteIndices)
                    total += ParagraphHeight(Current.NoteParagraphs[index]) + NoteGap;
                var y = top + contentHeight - total + NotesRuleGap;
                // Garde de rendu : une pile de notes plus haute que la page
                // (note-fleuve sur page quasi vide) remonterait AU-DESSUS du
                // bloc de texte et se dessinerait par-dessus lui. Le filet ne
                // monte jamais plus haut que le bloc ; les lignes qui débordent
                // sous la page sont tronquées plutôt qu'empiétantes.
                if (y - 5 < top) y = top + 5;
                page.NotesRuleY = y - 5;
                var bottom = top + contentHeight;
                foreach (var index in page.NoteIndices)
                {
                    var layout = Current.NoteParagraphs[index];
                    for (var l = 0; l < layout.Lines.Count; l++)
                    {
                        if (y + layout.Lines[l].Height > bottom + 0.5) break; // tronqué
                        page.NoteLines.Add(new PlacedLine { ParagraphIndex = index, LineIndex = l, Y = y });
                        y += layout.Lines[l].Height;
                    }
                    y += NoteGap;
                }
            }
        }

        private static int FirstChangedPage(List<ComposedPageLayout> before, List<ComposedPageLayout> after)
        {
            if (before == null) return 0;
            var common = Math.Min(before.Count, after.Count);
            for (var k = 0; k < common; k++)
            {
                if (PlacedDiffer(before[k].Lines, after[k].Lines)
                    || PlacedDiffer(before[k].NoteLines, after[k].NoteLines)
                    || Math.Abs(before[k].NotesRuleY - after[k].NotesRuleY) > 0.1) return k;
                if (before[k].WidowMarks.Count != after[k].WidowMarks.Count) return k;
                for (var i = 0; i < before[k].WidowMarks.Count; i++)
                    if (before[k].WidowMarks[i].Disabled != after[k].WidowMarks[i].Disabled
                        || Math.Abs(before[k].WidowMarks[i].Y - after[k].WidowMarks[i].Y) > 0.1)
                        return k;
            }
            return before.Count == after.Count ? int.MaxValue : common;
        }

        private static bool PlacedDiffer(List<PlacedLine> a, List<PlacedLine> b)
        {
            if (a.Count != b.Count) return true;
            for (var i = 0; i < a.Count; i++)
                if (a[i].ParagraphIndex != b[i].ParagraphIndex
                    || a[i].LineIndex != b[i].LineIndex
                    || Math.Abs(a[i].Y - b[i].Y) > 0.1) return true;
            return false;
        }

        private static int ChainStart(List<ComposedParagraphLayout> paragraphs, int p,
            int pageStartIndex, double contentHeight)
        {
            var start = p;
            while (start > pageStartIndex && paragraphs[start].Style.KeepWithPrevious)
                start--;
            if (start == pageStartIndex) return p;
            double total = 0;
            for (var i = start; i <= p; i++)
            {
                total += ParagraphHeight(paragraphs[i]);
                if (i > start) total += paragraphs[i].SpaceBefore + paragraphs[i - 1].SpaceAfter;
            }
            return total > contentHeight + 0.5 ? p : start;
        }

        private static double ParagraphHeight(ComposedParagraphLayout paragraph)
        {
            double height = 0;
            foreach (var line in paragraph.Lines) height += line.Height;
            return height;
        }
    }

    /// <summary>Static facade for one-shot composition (preview, print).</summary>
    public static class Composer
    {
        public static Composition Compose(TextDocument document, StyleSheet styles,
            PageSetup setup, Project project)
        {
            var engine = new CompositionEngine(document, styles, setup, project, true,
                new WpfGlyphMetrics());
            engine.ComposeAll();
            return engine.Current;
        }
    }
}
