using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Un commentaire Word lu dans un .docx (b49) : auteur, date,
    /// texte, et le PASSAGE qu'il commentait (son empreinte), avec l'index
    /// du paragraphe où il commençait.</summary>
    public class DocxComment
    {
        public string Id = "";
        public string Author = "";
        public string Date = "";        // ISO du docx, "" si absente
        public string Text = "";
        public string Anchor = "";      // le texte commenté, "" si le commentaire n'avait pas de plage
        public int ParagraphIndex = -1; // dans le document lu
    }

    /// <summary>Les commentaires d'un document RELU (bêta-lecteur, éditeur)
    /// ramenés dans l'écrit d'origine, comme annotations (b49). Le texte a
    /// pu bouger de part et d'autre : chaque commentaire cherche son passage
    /// par empreinte — d'abord dans le paragraphe de même rang, puis
    /// partout, casse et accents ignorés. Un passage introuvable pose
    /// l'annotation en tête du paragraphe de même rang, le passage cité
    /// dans le texte de l'annotation : rien n'est perdu. Pur, testable (C30).</summary>
    public static class CommentMerge
    {
        public class Result
        {
            public int Placed;    // sur leur passage
            public int Fallback;  // sur le paragraphe de même rang, passage cité
            public int Lost;      // document sans texte : impossible de poser
            public int Total { get { return Placed + Fallback + Lost; } }
        }

        /// <summary>Le texte d'annotation : « Auteur — texte », ou le texte seul.</summary>
        public static string Label(string author, string text)
        {
            author = (author ?? "").Trim();
            text = (text ?? "").Trim();
            return author.Length > 0 ? author + " — " + text : text;
        }

        /// <summary>Date ISO d'un commentaire Word → « yyyy-MM-dd HH:mm »
        /// (l'heure locale) ; aujourd'hui si elle manque ou ne se lit pas.</summary>
        public static string ToCreated(string isoDate)
        {
            DateTime date;
            if (!string.IsNullOrEmpty(isoDate)
                && DateTime.TryParse(isoDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
                return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>« yyyy-MM-dd HH:mm » d'une annotation → date ISO UTC du docx.</summary>
        public static string ToIsoDate(string created)
        {
            DateTime date;
            if (!string.IsNullOrEmpty(created)
                && DateTime.TryParseExact(created, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out date))
                return date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        /// <summary>Pose les commentaires sur le document (modifié en place).</summary>
        public static Result Merge(TextDocument document, List<DocxComment> comments)
        {
            var result = new Result();
            if (document == null || comments == null) return result;
            foreach (var comment in comments)
            {
                int paragraph, start, end;
                var anchor = FirstLine(comment.Anchor);
                if (anchor.Length > 0 && Find(document, anchor, comment.ParagraphIndex, out paragraph, out start, out end))
                {
                    Annotate(document, paragraph, start, end, Label(comment.Author, comment.Text), comment.Date);
                    result.Placed++;
                    continue;
                }
                if (FallbackRange(document, comment.ParagraphIndex, out paragraph, out start, out end))
                {
                    var text = anchor.Length > 0
                        ? "« " + anchor + " »\n\n" + Label(comment.Author, comment.Text)
                        : Label(comment.Author, comment.Text);
                    Annotate(document, paragraph, start, end, text, comment.Date);
                    result.Fallback++;
                    continue;
                }
                result.Lost++;
            }
            return result;
        }

        private static string FirstLine(string anchor)
        {
            var text = (anchor ?? "").Replace("\r", "");
            var cut = text.IndexOf('\n');
            if (cut >= 0) text = text.Substring(0, cut);
            return text.Trim();
        }

        /// <summary>Cherche le passage : paragraphe de même rang d'abord,
        /// puis tous dans l'ordre ; casse et accents ignorés.</summary>
        public static bool Find(TextDocument document, string anchor, int preferred,
            out int paragraph, out int start, out int end)
        {
            paragraph = -1; start = 0; end = 0;
            if (string.IsNullOrEmpty(anchor)) return false;
            var order = new List<int>();
            if (preferred >= 0 && preferred < document.Paragraphs.Count) order.Add(preferred);
            for (var i = 0; i < document.Paragraphs.Count; i++) if (i != preferred) order.Add(i);
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            foreach (var index in order)
            {
                var flat = PivotEdit.FlatText(document.Paragraphs[index]);
                if (flat.Length < anchor.Length) continue;
                var at = flat.IndexOf(anchor, StringComparison.Ordinal);
                if (at < 0)
                {
                    try { at = compare.IndexOf(flat, anchor, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace); }
                    catch (ArgumentException) { at = -1; }
                }
                if (at < 0) continue;
                paragraph = index;
                start = at;
                end = at + anchor.Length;
                // Les comparaisons pliées peuvent différer d'une longueur : on
                // reste dans le paragraphe.
                if (end > flat.Length) end = flat.Length;
                return end > start;
            }
            return false;
        }

        /// <summary>Le premier mot du paragraphe de même rang (ou du plus
        /// proche non vide) — l'ancre de repli.</summary>
        private static bool FallbackRange(TextDocument document, int preferred,
            out int paragraph, out int start, out int end)
        {
            paragraph = -1; start = 0; end = 0;
            var count = document.Paragraphs.Count;
            if (count == 0) return false;
            var from = Math.Max(0, Math.Min(preferred, count - 1));
            for (var step = 0; step < count; step++)
            {
                foreach (var index in new[] { from + step, from - step })
                {
                    if (index < 0 || index >= count) continue;
                    var flat = PivotEdit.FlatText(document.Paragraphs[index]);
                    var first = FirstWord(flat);
                    if (first <= 0) continue;
                    paragraph = index;
                    start = 0;
                    end = first;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Longueur du premier mot (jusqu'au premier blanc, 40 car. max).</summary>
        private static int FirstWord(string flat)
        {
            var text = flat ?? "";
            var k = 0;
            while (k < text.Length && (char.IsWhiteSpace(text[k]) || text[k] == '￼')) k++;
            var start = k;
            while (k < text.Length && !char.IsWhiteSpace(text[k]) && text[k] != '￼' && k - start < 40) k++;
            return k > start ? k : 0;
        }

        private static void Annotate(TextDocument document, int paragraph, int start, int end, string text, string isoDate)
        {
            var annotation = new Annotation { Text = text, Created = ToCreated(isoDate) };
            var id = annotation.Id;
            PivotEdit.ApplyFormat(document.Paragraphs[paragraph], start, end,
                delegate(TextRun run) { run.AnnotationId = id; });
            document.Annotations.Add(annotation);
        }

        /// <summary>Le passage porté par une annotation (les runs qui la
        /// portent, paragraphes joints par un saut de ligne) et le premier
        /// paragraphe — l'empreinte d'un commentaire à l'export comme à la
        /// relecture.</summary>
        public static string AnchorOf(TextDocument document, string annotationId, out int firstParagraph)
        {
            firstParagraph = -1;
            var sb = new StringBuilder();
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var any = false;
                foreach (var run in document.Paragraphs[p].Runs)
                {
                    if (run.AnnotationId != annotationId || PivotEdit.IsElement(run)) continue;
                    if (!any && sb.Length > 0) sb.Append('\n');
                    sb.Append(run.Text);
                    any = true;
                }
                if (any && firstParagraph < 0) firstParagraph = p;
            }
            return sb.ToString();
        }
    }
}
