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
            return Import(markdown, null, null);
        }

        /// <summary>baseDirectory + project (0.50.0) : les images « ![alt](chemin) »
        /// dont le fichier existe (chemin relatif au .md, ou absolu) entrent
        /// dans le magasin du projet, à leur place dans le texte ; sans projet
        /// ou fichier introuvable, l'alt reste en texte.</summary>
        public static TextDocument Import(string markdown, string baseDirectory, Project project)
        {
            var document = new TextDocument();
            var images = new ImageSource { BaseDirectory = baseDirectory, Project = project };
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
                ParseInline(line, paragraph, noteIds, images);
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
        /// <summary>D'où viennent les images d'un Markdown importé (0.50.0).</summary>
        private sealed class ImageSource
        {
            public string BaseDirectory;
            public Project Project;

            /// <summary>Le run image d'un « ![alt](chemin) », ou null (pas de
            /// projet, fichier absent, format que WPF ne lit pas, plus de 20 Mo).</summary>
            public TextRun Load(string target, string alt)
            {
                if (Project == null || string.IsNullOrEmpty(target)) return null;
                try
                {
                    var path = target.Trim();
                    var space = path.IndexOf(' ');
                    if (space > 0 && path.IndexOf('"', space) > 0) path = path.Substring(0, space); // titre « (chemin "titre") »
                    if (path.Contains("://")) return null;
                    path = Uri.UnescapeDataString(path).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    if (!System.IO.Path.IsPathRooted(path) && !string.IsNullOrEmpty(BaseDirectory))
                        path = System.IO.Path.Combine(BaseDirectory, path);
                    if (!System.IO.File.Exists(path)) return null;
                    var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (Array.IndexOf(ImportedImages.Supported, extension) < 0) return null;
                    var info = new System.IO.FileInfo(path);
                    if (info.Length == 0 || info.Length > 20 * 1024 * 1024) return null;
                    var id = Project.AddImage(System.IO.File.ReadAllBytes(path), extension);
                    return new TextRun { ImageId = id, Image = new ImageLayout { Name = System.IO.Path.GetFileName(path) } };
                }
                catch (Exception) { return null; }
            }
        }

        private static void ParseInline(string line, TextParagraph paragraph,
            Dictionary<string, string> noteIds, ImageSource images)
        {
            var bold = false;
            var italic = false;
            var strike = false;
            var sb = new StringBuilder();
            var i = 0;
            while (i < line.Length)
            {
                // Une image « ![alt](chemin) » (0.50.0).
                if (Peek(line, i, "!["))
                {
                    var close = line.IndexOf("](", i, StringComparison.Ordinal);
                    var end = close < 0 ? -1 : line.IndexOf(')', close + 2);
                    if (close > i && end > close)
                    {
                        var alt = line.Substring(i + 2, close - i - 2);
                        var run = images == null ? null : images.Load(line.Substring(close + 2, end - close - 2), alt);
                        Flush(paragraph, sb, bold, italic, strike);
                        if (run != null) paragraph.Runs.Add(run);
                        else if (alt.Length > 0) sb.Append(alt);
                        i = end + 1;
                        continue;
                    }
                }
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
