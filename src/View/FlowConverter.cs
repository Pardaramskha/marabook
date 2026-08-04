using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Bridge between the pivot model and WPF FlowDocument. The pivot
    /// is the source of truth; the FlowDocument only exists while editing.
    /// Paragraphs carry their style id in Tag; footnote markers carry
    /// "fn:&lt;id&gt;" in Tag. "Automatic" color is the mutable Chrome.Ink brush,
    /// so documents recolor instantly on theme switch.</summary>
    public static class FlowConverter
    {
        // ------------------------------------------------------- pivot -> flow

        public static FlowDocument ToFlow(TextDocument document, StyleSheet styles)
        {
            var flow = new FlowDocument
            {
                PagePadding = new Thickness(48, 40, 48, 40),
                FontFamily = new FontFamily(styles.Body.FontFamily),
                FontSize = styles.Body.FontSize
            };
            var footnoteNumber = 0;
            foreach (var paragraph in document.Paragraphs)
            {
                var style = styles.Find(paragraph.StyleId);
                var wpfParagraph = new Paragraph();
                ApplyParagraphStyle(wpfParagraph, style);
                if (paragraph.AlignOverride != null)
                    wpfParagraph.TextAlignment = ParseAlign(paragraph.AlignOverride);

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
                    // [[wiki links]] become accent-colored, Ctrl+clickable runs.
                    var text = run.Text;
                    var cursor = 0;
                    while (cursor < text.Length)
                    {
                        var open = text.IndexOf("[[", cursor, StringComparison.Ordinal);
                        var close = open < 0 ? -1 : text.IndexOf("]]", open + 2, StringComparison.Ordinal);
                        if (open < 0 || close < 0)
                        {
                            wpfParagraph.Inlines.Add(MakeRun(SliceRun(run, text.Substring(cursor))));
                            break;
                        }
                        if (open > cursor)
                            wpfParagraph.Inlines.Add(MakeRun(SliceRun(run, text.Substring(cursor, open - cursor))));
                        var linkRun = MakeRun(SliceRun(run, text.Substring(open, close + 2 - open)));
                        linkRun.Tag = "wikilink";
                        linkRun.Foreground = Chrome.Accent;
                        linkRun.Cursor = System.Windows.Input.Cursors.Hand;
                        wpfParagraph.Inlines.Add(linkRun);
                        cursor = close + 2;
                    }
                }
                flow.Blocks.Add(wpfParagraph);
            }
            if (flow.Blocks.Count == 0)
            {
                var empty = new Paragraph();
                ApplyParagraphStyle(empty, styles.Body);
                flow.Blocks.Add(empty);
            }
            return flow;
        }

        /// <summary>Applies a named style's visuals to a WPF paragraph and stamps
        /// its id in Tag. Run-level overrides survive: they sit on the runs.</summary>
        public static void ApplyParagraphStyle(Paragraph paragraph, ParagraphStyle style)
        {
            paragraph.Tag = style.Id;
            paragraph.FontFamily = new FontFamily(style.FontFamily);
            paragraph.FontSize = style.FontSize;
            paragraph.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
            paragraph.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;
            paragraph.Foreground = style.Color != null
                ? new SolidColorBrush(ParseColor(style.Color)) : (Brush)Chrome.Ink;
            paragraph.TextAlignment = ParseAlign(style.Align);
            paragraph.Margin = new Thickness(style.LeftIndent, style.SpaceBefore, 0, style.SpaceAfter);
            paragraph.TextIndent = style.FirstLineIndent;
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
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                Color = source.Color,
                Highlight = source.Highlight
            };
        }

        private static Run MakeRun(TextRun run)
        {
            var wpfRun = new Run(run.Text);
            if (run.Bold.HasValue)
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
            return wpfRun;
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
            StyleSheet styles, List<Footnote> knownFootnotes)
        {
            var document = new TextDocument();
            var seenFootnotes = new List<string>();

            foreach (var wpfParagraph in CollectParagraphs(flow.Blocks))
            {
                var styleId = wpfParagraph.Tag as string ?? "body";
                var style = styles.Find(styleId);
                var paragraph = new TextParagraph { StyleId = style.Id };

                var styleAlign = ParseAlign(style.Align);
                if (wpfParagraph.TextAlignment != styleAlign)
                    paragraph.AlignOverride = AlignToString(wpfParagraph.TextAlignment);

                CollectRuns(wpfParagraph.Inlines, paragraph, style, seenFootnotes);
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

        /// <summary>Flattens Sections, Lists and Tables (from pasted content)
        /// into a plain paragraph sequence.</summary>
        private static IEnumerable<Paragraph> CollectParagraphs(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null) { yield return paragraph; continue; }
                var section = block as Section;
                if (section != null)
                {
                    foreach (var inner in CollectParagraphs(section.Blocks)) yield return inner;
                    continue;
                }
                var list = block as List;
                if (list != null)
                {
                    foreach (ListItem entry in list.ListItems)
                        foreach (var inner in CollectParagraphs(entry.Blocks)) yield return inner;
                    continue;
                }
                var table = block as Table;
                if (table != null)
                {
                    foreach (TableRowGroup group in table.RowGroups)
                        foreach (TableRow row in group.Rows)
                            foreach (TableCell cell in row.Cells)
                                foreach (var inner in CollectParagraphs(cell.Blocks)) yield return inner;
                }
            }
        }

        private static void CollectRuns(InlineCollection inlines, TextParagraph paragraph,
            ParagraphStyle style, List<string> seenFootnotes)
        {
            foreach (var inline in inlines)
            {
                if (inline is LineBreak)
                {
                    paragraph.Runs.Add(new TextRun { IsLineBreak = true });
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
                    var last = paragraph.Runs.Count > 0 ? paragraph.Runs[paragraph.Runs.Count - 1] : null;
                    if (last != null && last.HasSameFormat(run))
                        last.Text += run.Text; // merge to keep files compact after heavy editing
                    else
                        paragraph.Runs.Add(run);
                    continue;
                }
                var span = inline as Span; // Span, Bold, Italic, Underline, Hyperlink
                if (span != null)
                    CollectRuns(span.Inlines, paragraph, style, seenFootnotes);
            }
        }

        /// <summary>Reads a WPF run's *effective* (inherited) properties and keeps
        /// only what differs from the paragraph style.</summary>
        private static TextRun ReadRun(Run wpfRun, ParagraphStyle style)
        {
            var run = new TextRun { Text = wpfRun.Text };

            var bold = wpfRun.FontWeight >= FontWeights.Bold;
            if (bold != style.Bold) run.Bold = bold;

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
            var background = wpfRun.Background as SolidColorBrush;
            if (background != null && background.Color.A > 0)
                run.Highlight = ColorToHex(background.Color);

            return run;
        }

        private static string ReadColorOverride(Brush foreground, ParagraphStyle style)
        {
            if (ReferenceEquals(foreground, Chrome.Ink)) // automatic: no override
                return null;
            var brush = foreground as SolidColorBrush;
            if (brush == null) return null;
            if (brush.Color == Chrome.Ink.Color && style.Color == null) return null;
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
