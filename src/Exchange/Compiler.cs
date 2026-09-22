using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Exchange
{
    public class CompileOptions
    {
        public bool TitlePage = true;
        public string Title;          // null = project name
        public string Subtitle = "";  // books
        public string Author = "";
        public string Colophon = "";  // books: éditeur — collection — année — ISBN
        public bool ChapterHeadings = true;
        public bool NumberChapters;
        public bool PageBreakPerText = true;
        public bool RectoChapterStarts;   // books: every text opens on a recto
        public string Separator = ""; // used between texts when no page break
    }

    /// <summary>The compiler: assembles the text items under a chosen root into
    /// one manuscript pivot document, ready for any exporter. Deep copies
    /// nothing heavy — runs are shared references, the output is transient and
    /// exported immediately.</summary>
    public static class Compiler
    {
        public static TextDocument Build(Project project, BinderItem root, CompileOptions options)
        {
            var output = new TextDocument();

            if (options.TitlePage)
            {
                var title = new TextParagraph { StyleId = "title1" };
                title.Runs.Add(new TextRun { Text = options.Title ?? project.Name });
                output.Paragraphs.Add(title);
                if (!string.IsNullOrEmpty(options.Subtitle))
                {
                    var subtitle = new TextParagraph { StyleId = "title2", AlignOverride = "center" };
                    subtitle.Runs.Add(new TextRun { Text = options.Subtitle, Bold = false, Italic = true });
                    output.Paragraphs.Add(subtitle);
                }
                if (!string.IsNullOrEmpty(options.Author))
                {
                    var author = new TextParagraph { StyleId = "body", AlignOverride = "center" };
                    author.Runs.Add(new TextRun { Text = options.Author });
                    output.Paragraphs.Add(author);
                }
                if (!string.IsNullOrEmpty(options.Colophon))
                {
                    var colophon = new TextParagraph { StyleId = "body", AlignOverride = "center" };
                    colophon.Runs.Add(new TextRun
                    {
                        Text = options.Colophon,
                        FontSize = 12 // 9 pt: la ligne d'édition reste discrète
                    });
                    output.Paragraphs.Add(colophon);
                }
            }

            var texts = new List<BinderItem>();
            CollectTexts(root, texts);

            var chapterNumber = 0;
            var first = true;
            foreach (var text in texts)
            {
                chapterNumber++;
                var startIndex = output.Paragraphs.Count;

                if (options.ChapterHeadings)
                {
                    var heading = new TextParagraph { StyleId = "title2" };
                    heading.Runs.Add(new TextRun
                    {
                        Text = (options.NumberChapters ? chapterNumber + ". " : "") + text.Title
                    });
                    output.Paragraphs.Add(heading);
                }

                // Chaque page du manuscrit fusionné retrouve le décor
                // (en-tête/pied, gabarit de pages) de SON chapitre.
                var decor = PageDecor.For(text, project);
                foreach (var paragraph in text.Document.Paragraphs)
                {
                    // Clone the paragraph shell (runs shared): footnote markers
                    // must be renumbered against the merged note list.
                    var copy = new TextParagraph
                    {
                        StyleId = paragraph.StyleId,
                        AlignOverride = paragraph.AlignOverride,
                        ListKind = paragraph.ListKind,
                        Indent = paragraph.Indent,
                        FirstIndent = paragraph.FirstIndent,
                        PageBreakBefore = paragraph.PageBreakBefore,
                        AllowWidows = paragraph.AllowWidows,
                        Decor = decor
                    };
                    copy.Runs.AddRange(paragraph.Runs);
                    output.Paragraphs.Add(copy);
                }
                foreach (var note in text.Document.Footnotes)
                    output.Footnotes.Add(note);

                if (!first || options.TitlePage)
                {
                    if (options.PageBreakPerText && output.Paragraphs.Count > startIndex)
                    {
                        output.Paragraphs[startIndex].PageBreakBefore = true;
                        if (options.RectoChapterStarts)
                            output.Paragraphs[startIndex].StartOnRecto = true;
                    }
                    else if (!options.PageBreakPerText && !first
                        && !string.IsNullOrEmpty(options.Separator))
                    {
                        var separator = new TextParagraph { StyleId = StyleSheet.SeparatorId }; // le style séparateur (22/09)
                        separator.Runs.Add(new TextRun { Text = options.Separator });
                        output.Paragraphs.Insert(startIndex, separator);
                    }
                }
                first = false;
            }

            if (output.Paragraphs.Count == 0) output.Paragraphs.Add(new TextParagraph());
            return output;
        }

        /// <summary>Degrades list paragraphs to visible "•"/"1." text prefixes
        /// and horizontal rules to a dash string, for exporters without native
        /// support (odt, txt). Numbering restarts on each consecutive run of
        /// numbered paragraphs.</summary>
        public static TextDocument FlattenLists(TextDocument document)
        {
            var needed = false;
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.ListKind != null) needed = true;
                foreach (var run in paragraph.Runs) if (run.IsRule) needed = true;
                if (needed) break;
            }
            if (!needed) return document;

            var output = new TextDocument { Footnotes = document.Footnotes, Annotations = document.Annotations, LineSpacing = document.LineSpacing };
            var number = 0;
            foreach (var paragraph in document.Paragraphs)
            {
                var source = FlattenRulesIn(paragraph);
                if (source.ListKind == null)
                {
                    number = 0;
                    output.Paragraphs.Add(source);
                    continue;
                }
                number = source.ListKind == "number" ? number + 1 : 0;
                var copy = new TextParagraph
                {
                    StyleId = source.StyleId,
                    AlignOverride = source.AlignOverride,
                    Indent = source.Indent,
                    PageBreakBefore = source.PageBreakBefore
                };
                copy.Runs.Add(new TextRun
                {
                    Text = source.ListKind == "number" ? number + ". " : "•  "
                });
                copy.Runs.AddRange(source.Runs);
                output.Paragraphs.Add(copy);
            }
            return output;
        }

        /// <summary>Rules only (for RTF, whose lists survive natively but whose
        /// inline shapes do not).</summary>
        public static TextDocument FlattenRules(TextDocument document)
        {
            var needed = false;
            foreach (var paragraph in document.Paragraphs)
            {
                foreach (var run in paragraph.Runs) if (run.IsRule) { needed = true; break; }
                if (needed) break;
            }
            if (!needed) return document;
            var output = new TextDocument { Footnotes = document.Footnotes, Annotations = document.Annotations, LineSpacing = document.LineSpacing };
            foreach (var paragraph in document.Paragraphs)
                output.Paragraphs.Add(FlattenRulesIn(paragraph));
            return output;
        }

        private static TextParagraph FlattenRulesIn(TextParagraph paragraph)
        {
            var hasRule = false;
            foreach (var run in paragraph.Runs) if (run.IsRule) { hasRule = true; break; }
            if (!hasRule) return paragraph;
            var copy = new TextParagraph
            {
                StyleId = paragraph.StyleId,
                AlignOverride = paragraph.AlignOverride ?? "center",
                ListKind = paragraph.ListKind,
                PageBreakBefore = paragraph.PageBreakBefore
            };
            foreach (var run in paragraph.Runs)
                copy.Runs.Add(run.IsRule
                    ? new TextRun { Text = "────────────────────" } : run);
            return copy;
        }

        /// <summary>Depth-first reading order; texts may carry children.</summary>
        private static void CollectTexts(BinderItem root, List<BinderItem> texts)
        {
            if (root.Kind == ItemKind.Text) texts.Add(root);
            foreach (var child in root.Children)
                CollectTexts(child, texts);
        }
    }
}
