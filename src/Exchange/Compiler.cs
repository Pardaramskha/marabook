using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Exchange
{
    public class CompileOptions
    {
        public bool TitlePage = true;
        public string Author = "";
        public bool ChapterHeadings = true;
        public bool NumberChapters;
        public bool PageBreakPerText = true;
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
                title.Runs.Add(new TextRun { Text = project.Name });
                output.Paragraphs.Add(title);
                if (!string.IsNullOrEmpty(options.Author))
                {
                    var author = new TextParagraph { StyleId = "body", AlignOverride = "center" };
                    author.Runs.Add(new TextRun { Text = options.Author });
                    output.Paragraphs.Add(author);
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

                foreach (var paragraph in text.Document.Paragraphs)
                {
                    // Clone the paragraph shell (runs shared): footnote markers
                    // must be renumbered against the merged note list.
                    var copy = new TextParagraph
                    {
                        StyleId = paragraph.StyleId,
                        AlignOverride = paragraph.AlignOverride
                    };
                    copy.Runs.AddRange(paragraph.Runs);
                    output.Paragraphs.Add(copy);
                }
                foreach (var note in text.Document.Footnotes)
                    output.Footnotes.Add(note);

                if (!first || options.TitlePage)
                {
                    if (options.PageBreakPerText && output.Paragraphs.Count > startIndex)
                        output.Paragraphs[startIndex].PageBreakBefore = true;
                    else if (!options.PageBreakPerText && !first
                        && !string.IsNullOrEmpty(options.Separator))
                    {
                        var separator = new TextParagraph { StyleId = "body", AlignOverride = "center" };
                        separator.Runs.Add(new TextRun { Text = options.Separator });
                        output.Paragraphs.Insert(startIndex, separator);
                    }
                }
                first = false;
            }

            if (output.Paragraphs.Count == 0) output.Paragraphs.Add(new TextParagraph());
            return output;
        }

        private static void CollectTexts(BinderItem root, List<BinderItem> texts)
        {
            if (root.Kind == ItemKind.Text) { texts.Add(root); return; }
            foreach (var child in root.Children)
            {
                if (child.Kind == ItemKind.Text) texts.Add(child);
                else if (child.CanHaveChildren) CollectTexts(child, texts);
            }
        }
    }
}
