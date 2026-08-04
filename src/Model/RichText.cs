using System;
using System.Collections.Generic;
using System.Text;

namespace UniversSale.Model
{
    /// <summary>The pivot text model — the single central format every exchange
    /// goes through (editor, .plot, docx/odt/RTF later, PDF later). Deliberately
    /// aligned with docx/odt semantics: paragraphs referencing a named style,
    /// holding styled runs. Run properties are nullable: null = inherit from the
    /// paragraph style; only overrides are stored.</summary>
    public class TextRun
    {
        public string Text = "";
        public bool? Bold, Italic, Underline, Strike;
        public string FontFamily;   // null = style font
        public double? FontSize;    // null = style size
        public string Color;        // "#RRGGBB", null = automatic (style color, else theme ink)
        public string Highlight;    // "#RRGGBB", null = none
        public string FootnoteId;   // set on footnote markers; Text is regenerated
        public bool IsLineBreak;    // explicit line break (Shift+Enter)

        public bool HasSameFormat(TextRun other)
        {
            return Bold == other.Bold && Italic == other.Italic
                && Underline == other.Underline && Strike == other.Strike
                && FontFamily == other.FontFamily && FontSize == other.FontSize
                && Color == other.Color && Highlight == other.Highlight
                && FootnoteId == null && other.FootnoteId == null
                && !IsLineBreak && !other.IsLineBreak;
        }
    }

    public class TextParagraph
    {
        public string StyleId = "body";
        public string AlignOverride; // "left"|"center"|"right"|"justify", null = style alignment
        public List<TextRun> Runs = new List<TextRun>();

        // Compilation output only (title page, chapter starts): honored by the
        // docx exporter, never persisted in .plot and invisible in the editor.
        public bool PageBreakBefore;
    }

    public class Footnote
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text = "";
    }

    public class TextDocument
    {
        public List<TextParagraph> Paragraphs = new List<TextParagraph>();
        public List<Footnote> Footnotes = new List<Footnote>();

        public static TextDocument FromPlainText(string text)
        {
            var document = new TextDocument();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var paragraph = new TextParagraph();
                if (line.Length > 0)
                    paragraph.Runs.Add(new TextRun { Text = line });
                document.Paragraphs.Add(paragraph);
            }
            if (document.Paragraphs.Count == 0)
                document.Paragraphs.Add(new TextParagraph());
            return document;
        }

        /// <summary>Body text only (footnote markers and note texts excluded) —
        /// used for statistics and search indexing.</summary>
        public string ToPlainText()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                foreach (var run in Paragraphs[i].Runs)
                {
                    if (run.FootnoteId != null) continue;
                    if (run.IsLineBreak) { sb.Append('\n'); continue; }
                    sb.Append(run.Text);
                }
            }
            return sb.ToString();
        }

        public Footnote FindFootnote(string id)
        {
            foreach (var note in Footnotes)
                if (note.Id == id) return note;
            return null;
        }
    }
}
