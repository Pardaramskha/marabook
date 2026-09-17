using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Markdown import/export. Deliberately plain: #/## headings map to
    /// Titre 1/2, &gt; to Citation, **bold**, *italic*, ~~strike~~, [^n]
    /// footnotes. Underline, colors and fonts have no Markdown form and are
    /// dropped on export, which is the point of the format.</summary>
    public static class MarkdownExchange
    {
        public const string Filter = "Markdown (*.md)|*.md";

        // ------------------------------------------------------- export

        public static string Export(TextDocument document)
        {
            var sb = new StringBuilder();
            var noteNumbers = new Dictionary<string, int>();
            for (var i = 0; i < document.Footnotes.Count; i++)
                noteNumbers[document.Footnotes[i].Id] = i + 1;

            var listNumber = 0;
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.ListKind == "number") sb.Append(++listNumber).Append(". ");
                else
                {
                    listNumber = 0;
                    if (paragraph.ListKind == "bullet") sb.Append("- ");
                    else if (paragraph.StyleId == "title1") sb.Append("# ");
                    else if (paragraph.StyleId == "title2") sb.Append("## ");
                    else if (paragraph.StyleId == "quote") sb.Append("> ");
                }

                foreach (var run in paragraph.Runs)
                {
                    if (run.IsRule) { sb.Append("---"); continue; }
                    if (run.IsLineBreak) { sb.Append("  \n"); continue; }
                    if (run.FootnoteId != null)
                    {
                        int number;
                        if (noteNumbers.TryGetValue(run.FootnoteId, out number))
                            sb.Append("[^").Append(number).Append("]");
                        continue;
                    }
                    var text = run.Text;
                    var marks = "";
                    if (run.Bold == true) marks += "**";
                    if (run.Italic == true) marks += "*";
                    if (run.Strike == true) marks += "~~";
                    if (marks.Length > 0 && text.Trim().Length > 0)
                    {
                        // Markers hug the words; surrounding spaces stay outside.
                        var start = 0;
                        var end = text.Length;
                        while (start < end && text[start] == ' ') start++;
                        while (end > start && text[end - 1] == ' ') end--;
                        sb.Append(text.Substring(0, start)).Append(marks)
                          .Append(text.Substring(start, end - start))
                          .Append(Reverse(marks)).Append(text.Substring(end));
                    }
                    else sb.Append(text);
                }
                sb.Append("\n\n");
            }

            if (document.Footnotes.Count > 0)
            {
                for (var i = 0; i < document.Footnotes.Count; i++)
                    sb.Append("[^").Append(i + 1).Append("]: ")
                      .Append(document.Footnotes[i].Text.Replace("\n", " ")).Append("\n");
            }
            return sb.ToString();
        }

        private static string Reverse(string marks)
        {
            var chars = marks.ToCharArray();
            Array.Reverse(chars);
            // "**" and "~~" are symmetric; only the order of groups matters.
            return new string(chars);
        }

        // ------------------------------------------------------- import

        public static TextDocument Import(string markdown)
        {
            var document = new TextDocument();
            var noteIds = new Dictionary<string, string>(); // "1" -> footnote id
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');

            // First pass: footnote definitions "[^n]: text".
            foreach (var line in lines)
            {
                if (!line.StartsWith("[^")) continue;
                var close = line.IndexOf("]:", StringComparison.Ordinal);
                if (close < 0) continue;
                var number = line.Substring(2, close - 2);
                var note = new Footnote { Text = line.Substring(close + 2).Trim() };
                document.Footnotes.Add(note);
                noteIds[number] = note.Id;
            }

            foreach (var raw in lines)
            {
                if (raw.StartsWith("[^") && raw.Contains("]:")) continue; // definition line
                var line = raw;
                var paragraph = new TextParagraph();
                if (line.StartsWith("# ")) { paragraph.StyleId = "title1"; line = line.Substring(2); }
                else if (line.StartsWith("## ")) { paragraph.StyleId = "title2"; line = line.Substring(3); }
                else if (line.StartsWith("### ")) { paragraph.StyleId = "title2"; line = line.Substring(4); }
                else if (line.StartsWith("> ")) { paragraph.StyleId = "quote"; line = line.Substring(2); }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                { paragraph.ListKind = "bullet"; line = line.Substring(2); }
                else
                {
                    // "12. item" -> numbered list entry
                    var dot = line.IndexOf(". ", StringComparison.Ordinal);
                    if (dot > 0 && dot <= 3)
                    {
                        var digits = true;
                        for (var d = 0; d < dot; d++) if (!char.IsDigit(line[d])) digits = false;
                        if (digits) { paragraph.ListKind = "number"; line = line.Substring(dot + 2); }
                    }
                }

                var trimmed = line.Trim();
                if (trimmed == "---" || trimmed == "___" || trimmed == "- - -")
                {
                    var rule = new TextParagraph();
                    rule.Runs.Add(new TextRun { IsRule = true });
                    document.Paragraphs.Add(rule);
                    continue;
                }
                if (line.Trim().Length == 0)
                {
                    // Blank lines separate paragraphs; avoid stacking empties.
                    if (document.Paragraphs.Count > 0
                        && document.Paragraphs[document.Paragraphs.Count - 1].Runs.Count == 0)
                        continue;
                    document.Paragraphs.Add(new TextParagraph());
                    continue;
                }
                ParseInline(line, paragraph, noteIds);
                document.Paragraphs.Add(paragraph);
            }

            // Trim a trailing empty paragraph left by the final blank line.
            while (document.Paragraphs.Count > 1
                && document.Paragraphs[document.Paragraphs.Count - 1].Runs.Count == 0)
                document.Paragraphs.RemoveAt(document.Paragraphs.Count - 1);
            if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());
            return document;
        }

        /// <summary>Single-pass inline parser: **, *, ~~ toggles and [^n] refs.
        /// Unclosed markers fall back to literal text at end of line.</summary>
        private static void ParseInline(string line, TextParagraph paragraph,
            Dictionary<string, string> noteIds)
        {
            var bold = false;
            var italic = false;
            var strike = false;
            var sb = new StringBuilder();
            var i = 0;
            while (i < line.Length)
            {
                if (Peek(line, i, "**")) { Flush(paragraph, sb, bold, italic, strike); bold = !bold; i += 2; continue; }
                if (Peek(line, i, "~~")) { Flush(paragraph, sb, bold, italic, strike); strike = !strike; i += 2; continue; }
                if (line[i] == '*') { Flush(paragraph, sb, bold, italic, strike); italic = !italic; i += 1; continue; }
                if (Peek(line, i, "[^"))
                {
                    var close = line.IndexOf(']', i);
                    if (close > i)
                    {
                        var number = line.Substring(i + 2, close - i - 2);
                        string noteId;
                        if (noteIds.TryGetValue(number, out noteId))
                        {
                            Flush(paragraph, sb, bold, italic, strike);
                            paragraph.Runs.Add(new TextRun { FootnoteId = noteId });
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(line[i]);
                i++;
            }
            Flush(paragraph, sb, bold, italic, strike);
        }

        private static bool Peek(string line, int index, string token)
        {
            return index + token.Length <= line.Length
                && string.CompareOrdinal(line, index, token, 0, token.Length) == 0;
        }

        private static void Flush(TextParagraph paragraph, StringBuilder sb,
            bool bold, bool italic, bool strike)
        {
            if (sb.Length == 0) return;
            paragraph.Runs.Add(new TextRun
            {
                Text = sb.ToString(),
                Bold = bold ? (bool?)true : null,
                Italic = italic ? (bool?)true : null,
                Strike = strike ? (bool?)true : null
            });
            sb.Length = 0;
        }
    }
}
