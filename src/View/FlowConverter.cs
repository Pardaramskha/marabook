using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Bridge between the pivot model and WPF FlowDocument. The pivot
    /// is the source of truth; the FlowDocument only exists while editing.
    /// Paragraphs carry their style id in Tag; footnote markers carry
    /// "fn:&lt;id&gt;" in Tag. "Automatic" color is the mutable Chrome.PaperInk brush,
    /// so documents recolor instantly on theme switch.</summary>
    public static class FlowConverter
    {
        // ------------------------------------------------------- pivot -> flow

        public static FlowDocument ToFlow(TextDocument document, StyleSheet styles,
            Project project = null, bool revisionTints = true)
        {
            var flow = new FlowDocument
            {
                PagePadding = new Thickness(48, 40, 48, 40),
                FontFamily = new FontFamily(styles.Body.FontFamily),
                FontSize = styles.Body.FontSize
            };
            var footnoteNumber = 0;
            List currentList = null;
            string currentKind = null;
            foreach (var paragraph in document.Paragraphs)
            {
                var style = styles.Find(paragraph.StyleId);
                var wpfParagraph = new Paragraph();
                ApplyParagraphStyle(wpfParagraph, style);
                // Interligne du document (22/09) : sur l'interligne fixe du
                // style, ou sur l'automatique (comme le compositeur) — revue 22/09.
                if (Math.Abs(document.LineSpacing - 1) > 0.001)
                {
                    var leading = style.LineHeight > 1
                        ? style.LineHeight
                        : style.FontSize * Math.Max(100, style.AutoLeadingPercent) / 100.0;
                    wpfParagraph.LineHeight = leading * document.LineSpacing;
                }
                if (paragraph.AlignOverride != null)
                    wpfParagraph.TextAlignment = ParseAlign(paragraph.AlignOverride);
                if (paragraph.Indent.HasValue || paragraph.FirstIndent.HasValue)
                {
                    // Décalage (17/09, première ligne 21/09) : le bloc en marge
                    // gauche, la première ligne par l'alinéa (négatif = suspendu).
                    double left, first;
                    paragraph.EffectiveIndents(style, out left, out first);
                    var margin = wpfParagraph.Margin;
                    wpfParagraph.Margin = new Thickness(left, margin.Top, margin.Right, margin.Bottom);
                    wpfParagraph.TextIndent = first - left;
                }
                if (paragraph.PageBreakBefore) MarkPageBreak(wpfParagraph, true);

                foreach (var run in paragraph.Runs)
                {
                    if (run.IsLineBreak)
                    {
                        wpfParagraph.Inlines.Add(new LineBreak());
                        continue;
                    }
                    if (run.FootnoteId != null)
                    {
                        footnoteNumber++;
                        wpfParagraph.Inlines.Add(MakeFootnoteMarker(run.FootnoteId, footnoteNumber, style.FontSize));
                        continue;
                    }
                    if (run.ImageId != null)
                    {
                        wpfParagraph.Inlines.Add(MakeImageInline(run.ImageId,
                            project == null ? null : project.FindImage(run.ImageId)));
                        continue;
                    }
                    if (run.IsRule)
                    {
                        wpfParagraph.Inlines.Add(MakeRuleInline(project));
                        continue;
                    }
                    // [[wiki links]] become accent-colored, Ctrl+clickable runs.
                    // Une annotation ACTIVE teinte son passage (cosmétique,
                    // jamais persistée comme surlignage — voir ReadRun).
                    var annotation = run.AnnotationId == null
                        ? null : document.FindAnnotation(run.AnnotationId);
                    var tinted = revisionTints && annotation != null
                        && !annotation.Resolved && run.Highlight == null;
                    var text = run.Text;
                    var cursor = 0;
                    while (cursor < text.Length)
                    {
                        var open = text.IndexOf("[[", cursor, StringComparison.Ordinal);
                        var close = open < 0 ? -1 : text.IndexOf("]]", open + 2, StringComparison.Ordinal);
                        if (open < 0 || close < 0)
                        {
                            wpfParagraph.Inlines.Add(MakeRun(SliceRun(run, text.Substring(cursor)), tinted));
                            break;
                        }
                        if (open > cursor)
                            wpfParagraph.Inlines.Add(MakeRun(SliceRun(run, text.Substring(cursor, open - cursor)), tinted));
                        var linkRun = MakeRun(SliceRun(run, text.Substring(open, close + 2 - open)), tinted);
                        linkRun.Tag = "wikilink";
                        linkRun.Foreground = Chrome.Accent;
                        linkRun.Cursor = System.Windows.Input.Cursors.Hand;
                        wpfParagraph.Inlines.Add(linkRun);
                        cursor = close + 2;
                    }
                }

                // Consecutive same-kind list paragraphs share one List block.
                if (paragraph.ListKind != null)
                {
                    if (currentList == null || currentKind != paragraph.ListKind)
                    {
                        currentList = new List
                        {
                            MarkerStyle = paragraph.ListKind == "number"
                                ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(24, 4, 0, 4)
                        };
                        currentKind = paragraph.ListKind;
                        flow.Blocks.Add(currentList);
                    }
                    currentList.ListItems.Add(new ListItem(wpfParagraph));
                }
                else
                {
                    currentList = null;
                    currentKind = null;
                    flow.Blocks.Add(wpfParagraph);
                }
            }
            if (flow.Blocks.Count == 0)
            {
                var empty = new Paragraph();
                ApplyParagraphStyle(empty, styles.Body);
                flow.Blocks.Add(empty);
            }
            return flow;
        }

        /// <summary>Shows/hides the editor's visual cue for a manual page break
        /// (a thin accent rule above the paragraph) and stores the flag on the
        /// WPF paragraph so FromFlow reads it back.</summary>
        public static void MarkPageBreak(Paragraph paragraph, bool enabled)
        {
            paragraph.BreakPageBefore = enabled;
            paragraph.BorderBrush = enabled ? (Brush)Chrome.Accent : null;
            paragraph.BorderThickness = enabled ? new Thickness(0, 1, 0, 0) : new Thickness(0);
            paragraph.Padding = enabled ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        }

        /// <summary>The visual for a stored image (or a placeholder frame when
        /// the bytes are missing), sized to stay inside the page.</summary>
        public static UIElement MakeImageElement(ProjectImage stored)
        {
            var source = stored == null || stored.Bytes == null
                ? null : MediaView.TryImage(stored.Bytes, 0);
            if (source != null)
                return new System.Windows.Controls.Image
                {
                    Source = source,
                    Stretch = Stretch.Uniform,
                    StretchDirection = System.Windows.Controls.StretchDirection.DownOnly,
                    MaxWidth = 480,
                    MaxHeight = 380
                };
            return new System.Windows.Controls.Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = "[image introuvable]",
                    Foreground = Chrome.PaperSoftInk
                }
            };
        }

        /// <summary>A horizontal rule, sized to the page's content width.
        /// Tag "hr" round-trips it back to a rule run.</summary>
        public static InlineUIContainer MakeRuleInline(Project project)
        {
            var width = project != null ? project.Page.ContentWidthPx - 20 : 580;
            return new InlineUIContainer(new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(80, width),
                Height = 1.5,
                Fill = Chrome.Border,
                Margin = new Thickness(0, 6, 0, 6)
            })
            {
                Tag = "hr",
                BaselineAlignment = BaselineAlignment.Center
            };
        }

        /// <summary>An inline image bound to the project image store. The id
        /// travels in Tag ("img:&lt;id&gt;") so editing round-trips it.</summary>
        public static InlineUIContainer MakeImageInline(string imageId, ProjectImage stored)
        {
            return new InlineUIContainer(MakeImageElement(stored))
            {
                Tag = "img:" + imageId,
                BaselineAlignment = BaselineAlignment.Bottom
            };
        }

        /// <summary>Applies a named style's visuals to a WPF paragraph and stamps
        /// its id in Tag. Run-level overrides survive: they sit on the runs.
        /// WPF renders the character/paragraph attributes plus leading,
        /// ligatures and hyphenation on/off; the fine hyphenation and
        /// justification numbers live in the model for export and 4b.</summary>
        public static void ApplyParagraphStyle(Paragraph paragraph, ParagraphStyle style)
        {
            paragraph.Tag = style.Id;
            paragraph.FontFamily = new FontFamily(style.FontFamily);
            paragraph.FontSize = style.FontSize;
            paragraph.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
            paragraph.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;
            paragraph.Foreground = style.Color != null
                ? new SolidColorBrush(ParseColor(style.Color)) : (Brush)Chrome.PaperInk;
            paragraph.TextAlignment = ParseAlign(style.Align);
            paragraph.Margin = new Thickness(style.LeftIndent, style.SpaceBefore,
                style.RightIndent, style.SpaceAfter);
            paragraph.TextIndent = style.FirstLineIndent;

            // Leading: "at least" strategy — bigger inline content still fits.
            if (style.LineHeight > 1) paragraph.LineHeight = style.LineHeight;
            else paragraph.ClearValue(Block.LineHeightProperty);

            paragraph.Typography.StandardLigatures = style.Ligatures;

            // Per-style hyphenation: only an explicit "off" overrides the
            // page-level switch (the document inherits it otherwise).
            if (style.HyphenationEnabled)
                paragraph.ClearValue(Block.IsHyphenationEnabledProperty);
            else
                paragraph.IsHyphenationEnabled = false;
        }

        private static TextRun SliceRun(TextRun source, string text)
        {
            return new TextRun
            {
                Text = text,
                Bold = source.Bold,
                Italic = source.Italic,
                Underline = source.Underline,
                Strike = source.Strike,
                Weight = source.Weight,
                Tracking = source.Tracking,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                Color = source.Color,
                Highlight = source.Highlight,
                AnnotationId = source.AnnotationId
            };
        }

        /// <summary>Named weight ↔ WPF FontWeight (the fine variants beyond
        /// the Bold flag: Fin, Moyen, Demi-gras, Noir…).</summary>
        public static FontWeight ParseWeight(string name)
        {
            switch (name)
            {
                case "Thin": return FontWeights.Thin;
                case "Light": return FontWeights.Light;
                case "Medium": return FontWeights.Medium;
                case "SemiBold": return FontWeights.SemiBold;
                case "Bold": return FontWeights.Bold;
                case "Black": return FontWeights.Black;
                default: return FontWeights.Normal;
            }
        }

        public static string WeightName(FontWeight weight)
        {
            if (weight == FontWeights.Thin) return "Thin";
            if (weight == FontWeights.Light) return "Light";
            if (weight == FontWeights.Medium) return "Medium";
            if (weight == FontWeights.SemiBold) return "SemiBold";
            if (weight == FontWeights.Black) return "Black";
            return null; // Normal/Bold travel through the Bold flag
        }

        private static Run MakeRun(TextRun run, bool annotationTint = false)
        {
            var wpfRun = new Run(run.Text);
            if (run.Weight != null)
                wpfRun.FontWeight = ParseWeight(run.Weight);
            else if (run.Bold.HasValue)
                wpfRun.FontWeight = run.Bold.Value ? FontWeights.Bold : FontWeights.Normal;
            if (run.Italic.HasValue)
                wpfRun.FontStyle = run.Italic.Value ? FontStyles.Italic : FontStyles.Normal;
            var decorations = BuildDecorations(run.Underline == true, run.Strike == true);
            if (decorations != null || run.Underline.HasValue || run.Strike.HasValue)
                wpfRun.TextDecorations = decorations ?? new TextDecorationCollection();
            if (run.FontFamily != null) wpfRun.FontFamily = new FontFamily(run.FontFamily);
            if (run.FontSize.HasValue) wpfRun.FontSize = run.FontSize.Value;
            if (run.Color != null) wpfRun.Foreground = new SolidColorBrush(ParseColor(run.Color));
            if (run.Highlight != null) wpfRun.Background = new SolidColorBrush(ParseColor(run.Highlight));
            if (annotationTint) wpfRun.Background = Chrome.AnnotationTint;
            // L'approche, l'ancre d'annotation et « ne pas corriger » n'ont
            // pas d'équivalent FlowDocument : ils voyagent sur le Tag du Run
            // pour survivre à l'aller-retour classique (mini-format à
            // segments, voir ComposeRunTag).
            var tag = ComposeRunTag(run.AnnotationId, run.Tracking, run.NoProof);
            if (tag != null) wpfRun.Tag = tag;
            return wpfRun;
        }

        /// <summary>Le Tag des runs du classique : « ann:&lt;id&gt; », « trk=&lt;t&gt; »
        /// et « np » en segments joints par « ; » (l'ancre d'annotation en
        /// tête quand elle existe). L'approche seule garde sa forme historique
        /// de double, que ParseRunTag relit toujours.</summary>
        public static object ComposeRunTag(string annotationId, double? tracking, bool noProof)
        {
            if (annotationId == null && !noProof)
                return tracking.HasValue ? (object)tracking.Value : null;
            var tag = "";
            if (annotationId != null) tag = "ann:" + annotationId;
            if (tracking.HasValue)
                tag += (tag.Length > 0 ? ";" : "") + "trk=" + tracking.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            if (noProof) tag += (tag.Length > 0 ? ";" : "") + "np";
            return tag;
        }

        /// <summary>Relit un Tag de run du classique — toutes les formes :
        /// double historique (approche seule), « ann:… », « ann:…;trk=… »,
        /// et les segments « np ». Les Tags étrangers (« fn: », « wikilink »,
        /// « img: », « hr ») rendent trois néants.</summary>
        public static void ParseRunTag(object tag, out string annotationId,
            out double? tracking, out bool noProof)
        {
            annotationId = null;
            tracking = null;
            noProof = false;
            if (tag is double) { tracking = (double)tag; return; }
            var text = tag as string;
            if (text == null) return;
            if (text.StartsWith("fn:", StringComparison.Ordinal)
                || text.StartsWith("img:", StringComparison.Ordinal)
                || text == "hr" || text == "wikilink") return;
            if (!text.StartsWith("ann:", StringComparison.Ordinal)
                && !text.StartsWith("trk=", StringComparison.Ordinal)
                && text != "np" && !text.StartsWith("np;", StringComparison.Ordinal))
                return; // Tag inconnu : ne rien inventer
            foreach (var segment in text.Split(';'))
            {
                if (segment.StartsWith("ann:", StringComparison.Ordinal))
                {
                    var id = segment.Substring(4);
                    if (id.Length > 0) annotationId = id;
                }
                else if (segment.StartsWith("trk=", StringComparison.Ordinal))
                {
                    double value;
                    if (double.TryParse(segment.Substring(4),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                        tracking = value;
                }
                else if (segment == "np") noProof = true;
            }
        }

        private static Run MakeFootnoteMarker(string footnoteId, int number, double paragraphSize)
        {
            return new Run(number.ToString())
            {
                Tag = "fn:" + footnoteId,
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = Math.Max(8, paragraphSize * 0.65),
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.Accent
            };
        }

        private static TextDecorationCollection BuildDecorations(bool underline, bool strike)
        {
            if (!underline && !strike) return null;
            var decorations = new TextDecorationCollection();
            if (underline) decorations.Add(TextDecorations.Underline[0]);
            if (strike) decorations.Add(TextDecorations.Strikethrough[0]);
            return decorations;
        }

        // ------------------------------------------------------- flow -> pivot

        public static TextDocument FromFlow(System.Windows.Documents.FlowDocument flow,
            StyleSheet styles, List<Footnote> knownFootnotes, Project project = null)
        {
            var document = new TextDocument();
            var seenFootnotes = new List<string>();

            foreach (var entry in WalkParagraphs(flow.Blocks, null))
            {
                var wpfParagraph = entry.Paragraph;
                var styleId = wpfParagraph.Tag as string ?? "body";
                var style = styles.Find(styleId);
                var paragraph = new TextParagraph { StyleId = style.Id };
                paragraph.ListKind = entry.ListKind;
                paragraph.PageBreakBefore = wpfParagraph.BreakPageBefore;

                var styleAlign = ParseAlign(style.Align);
                if (wpfParagraph.TextAlignment != styleAlign)
                    paragraph.AlignOverride = AlignToString(wpfParagraph.TextAlignment);

                CollectRuns(wpfParagraph.Inlines, paragraph, style, seenFootnotes, project);
                document.Paragraphs.Add(paragraph);
            }
            if (document.Paragraphs.Count == 0)
                document.Paragraphs.Add(new TextParagraph());

            // Footnotes: keep only notes whose marker still exists, in text order.
            if (knownFootnotes != null)
                foreach (var id in seenFootnotes)
                {
                    foreach (var note in knownFootnotes)
                        if (note.Id == id) { document.Footnotes.Add(note); break; }
                }
            return document;
        }

        public static IEnumerable<Paragraph> EnumerateParagraphs(FlowDocument flow)
        {
            return CollectParagraphs(flow.Blocks);
        }

        private sealed class ParagraphEntry
        {
            public Paragraph Paragraph;
            public string ListKind; // null when outside any list
        }

        /// <summary>Walks blocks in order, remembering the list context: Sections
        /// pass it through, Lists set it ("bullet"/"number"), Tables (pasted
        /// content) reset it. Nested lists flatten to their innermost kind.</summary>
        private static IEnumerable<ParagraphEntry> WalkParagraphs(BlockCollection blocks, string listKind)
        {
            foreach (var block in blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null)
                {
                    yield return new ParagraphEntry { Paragraph = paragraph, ListKind = listKind };
                    continue;
                }
                var section = block as Section;
                if (section != null)
                {
                    foreach (var inner in WalkParagraphs(section.Blocks, listKind)) yield return inner;
                    continue;
                }
                var list = block as List;
                if (list != null)
                {
                    var kind = list.MarkerStyle == TextMarkerStyle.Decimal
                        || list.MarkerStyle == TextMarkerStyle.LowerLatin
                        || list.MarkerStyle == TextMarkerStyle.UpperLatin
                        || list.MarkerStyle == TextMarkerStyle.LowerRoman
                        || list.MarkerStyle == TextMarkerStyle.UpperRoman
                        ? "number" : "bullet";
                    foreach (ListItem entry in list.ListItems)
                        foreach (var inner in WalkParagraphs(entry.Blocks, kind)) yield return inner;
                    continue;
                }
                var table = block as Table;
                if (table != null)
                {
                    foreach (TableRowGroup group in table.RowGroups)
                        foreach (TableRow row in group.Rows)
                            foreach (TableCell cell in row.Cells)
                                foreach (var inner in WalkParagraphs(cell.Blocks, null)) yield return inner;
                }
            }
        }

        /// <summary>Flattens Sections, Lists and Tables into a plain paragraph
        /// sequence (search, footnote renumbering).</summary>
        private static IEnumerable<Paragraph> CollectParagraphs(BlockCollection blocks)
        {
            foreach (var entry in WalkParagraphs(blocks, null))
                yield return entry.Paragraph;
        }

        private static void CollectRuns(InlineCollection inlines, TextParagraph paragraph,
            ParagraphStyle style, List<string> seenFootnotes, Project project)
        {
            foreach (var inline in inlines)
            {
                if (inline is LineBreak)
                {
                    paragraph.Runs.Add(new TextRun { IsLineBreak = true });
                    continue;
                }
                var container = inline as InlineUIContainer;
                if (container != null)
                {
                    var containerTag = container.Tag as string;
                    if (containerTag == "hr")
                    {
                        paragraph.Runs.Add(new TextRun { IsRule = true });
                        continue;
                    }
                    if (containerTag != null && containerTag.StartsWith("img:"))
                    {
                        paragraph.Runs.Add(new TextRun { ImageId = containerTag.Substring(4) });
                        continue;
                    }
                    // Foreign image (pasted from Word or a browser): adopt it
                    // into the project store so it survives the round-trip.
                    var adopted = project == null ? null : AdoptForeignImage(container, project);
                    if (adopted != null) paragraph.Runs.Add(new TextRun { ImageId = adopted });
                    continue;
                }
                var wpfRun = inline as Run;
                if (wpfRun != null)
                {
                    var tag = wpfRun.Tag as string;
                    if (tag != null && tag.StartsWith("fn:"))
                    {
                        var id = tag.Substring(3);
                        paragraph.Runs.Add(new TextRun { FootnoteId = id });
                        seenFootnotes.Add(id);
                        continue;
                    }
                    if (wpfRun.Text.Length == 0) continue;
                    var run = ReadRun(wpfRun, style);
                    // Wiki links are auto-styled at display time: the accent color
                    // and hand cursor are cosmetic, never stored as overrides.
                    if (tag == "wikilink" && run.Color == ColorToHex(Chrome.Accent.Color))
                        run.Color = null;
                    // Ancre d'annotation, approche, « ne pas corriger » : tout
                    // ce qui voyage sur le Tag (voir ParseRunTag).
                    string annotationId;
                    double? tagTracking;
                    bool noProof;
                    ParseRunTag(wpfRun.Tag, out annotationId, out tagTracking, out noProof);
                    if (annotationId != null) run.AnnotationId = annotationId;
                    if (tagTracking.HasValue) run.Tracking = tagTracking;
                    if (noProof) run.NoProof = true;
                    var last = paragraph.Runs.Count > 0 ? paragraph.Runs[paragraph.Runs.Count - 1] : null;
                    if (last != null && last.HasSameFormat(run))
                        last.Text += run.Text; // merge to keep files compact after heavy editing
                    else
                        paragraph.Runs.Add(run);
                    continue;
                }
                var span = inline as Span; // Span, Bold, Italic, Underline, Hyperlink
                if (span != null)
                    CollectRuns(span.Inlines, paragraph, style, seenFootnotes, project);
            }
        }

        /// <summary>Encodes a pasted image to PNG and registers it in the
        /// project store. Returns the new image id, or null if unreadable.</summary>
        private static string AdoptForeignImage(InlineUIContainer container, Project project)
        {
            try
            {
                var image = container.Child as System.Windows.Controls.Image;
                var source = image == null ? null : image.Source as System.Windows.Media.Imaging.BitmapSource;
                if (source == null) return null;
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
                using (var buffer = new System.IO.MemoryStream())
                {
                    encoder.Save(buffer);
                    var id = project.AddImage(buffer.ToArray(), ".png");
                    container.Tag = "img:" + id; // stabilize for the rest of the session
                    return id;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads a WPF run's *effective* (inherited) properties and keeps
        /// only what differs from the paragraph style.</summary>
        private static TextRun ReadRun(Run wpfRun, ParagraphStyle style)
        {
            var run = new TextRun { Text = wpfRun.Text };
            if (wpfRun.Tag is double) run.Tracking = (double)wpfRun.Tag; // approche

            var weightName = WeightName(wpfRun.FontWeight);
            if (weightName != null)
                run.Weight = weightName; // fine variant survives the round-trip
            else
            {
                var bold = wpfRun.FontWeight >= FontWeights.Bold;
                if (bold != style.Bold) run.Bold = bold;
            }

            var italic = wpfRun.FontStyle == FontStyles.Italic;
            if (italic != style.Italic) run.Italic = italic;

            var underline = HasDecoration(wpfRun.TextDecorations, TextDecorationLocation.Underline);
            if (underline) run.Underline = true;
            var strike = HasDecoration(wpfRun.TextDecorations, TextDecorationLocation.Strikethrough);
            if (strike) run.Strike = true;

            var family = wpfRun.FontFamily == null ? null : wpfRun.FontFamily.Source;
            if (family != null && family != style.FontFamily) run.FontFamily = family;

            if (Math.Abs(wpfRun.FontSize - style.FontSize) > 0.1) run.FontSize = wpfRun.FontSize;

            run.Color = ReadColorOverride(wpfRun.Foreground, style);
            // Les surlignages persistés sont opaques ; la teinte d'annotation
            // (semi-transparente, cosmétique) ne doit jamais en devenir un.
            var background = wpfRun.Background as SolidColorBrush;
            if (background != null && background.Color.A == 0xFF)
                run.Highlight = ColorToHex(background.Color);

            return run;
        }

        private static string ReadColorOverride(Brush foreground, ParagraphStyle style)
        {
            if (ReferenceEquals(foreground, Chrome.PaperInk)) // automatic: no override
                return null;
            var brush = foreground as SolidColorBrush;
            if (brush == null) return null;
            if (brush.Color == Chrome.PaperInk.Color && style.Color == null) return null;
            if (style.Color != null && brush.Color == ParseColor(style.Color)) return null;
            return ColorToHex(brush.Color);
        }

        private static bool HasDecoration(TextDecorationCollection decorations, TextDecorationLocation location)
        {
            if (decorations == null) return false;
            foreach (var decoration in decorations)
                if (decoration.Location == location) return true;
            return false;
        }

        // ------------------------------------------------------- footnote helpers

        /// <summary>Renumbers footnote markers in document order; returns the
        /// ordered ids so the notes panel can follow.</summary>
        public static List<string> RenumberFootnotes(FlowDocument flow)
        {
            var ordered = new List<string>();
            foreach (var paragraph in CollectParagraphs(flow.Blocks))
                RenumberInlines(paragraph.Inlines, ordered);
            return ordered;
        }

        private static void RenumberInlines(InlineCollection inlines, List<string> ordered)
        {
            foreach (var inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    var tag = run.Tag as string;
                    if (tag != null && tag.StartsWith("fn:"))
                    {
                        ordered.Add(tag.Substring(3));
                        var number = ordered.Count.ToString();
                        if (run.Text != number) run.Text = number;
                    }
                    continue;
                }
                var span = inline as Span;
                if (span != null) RenumberInlines(span.Inlines, ordered);
            }
        }

        // ------------------------------------------------------- conversions

        public static TextAlignment ParseAlign(string align)
        {
            if (align == "center") return TextAlignment.Center;
            if (align == "right") return TextAlignment.Right;
            if (align == "justify") return TextAlignment.Justify;
            return TextAlignment.Left;
        }

        public static string AlignToString(TextAlignment align)
        {
            if (align == TextAlignment.Center) return "center";
            if (align == TextAlignment.Right) return "right";
            if (align == TextAlignment.Justify) return "justify";
            return "left";
        }

        public static Color ParseColor(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return Colors.Black;
            }
        }

        public static string ColorToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
