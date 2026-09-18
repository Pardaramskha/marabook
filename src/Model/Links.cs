using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Un [[lien]] du texte (18/09) : la notation reste du texte
    /// ordinaire dans le pivot — « [[Cible]] », ou « [[Cible|mots du
    /// texte]] » quand l'expression qui porte le lien n'est pas le nom de
    /// la fiche. Rien ici ne touche WPF : la détection, le texte affiché,
    /// et la version « propre » pour l'export (les marques retirées, les
    /// mots gardés tels quels).</summary>
    public class Link
    {
        public int Start;      // offset de « [[ »
        public int End;        // offset APRÈS « ]] »
        public int TextStart;  // offset du premier caractère affiché
        public int TextEnd;    // offset APRÈS le dernier caractère affiché
        public string Target;  // la cible (fiche ou écrit), sans espaces de bord
        public string Text;    // ce que le lecteur voit
    }

    /// <summary>Une plage de la notation à masquer à l'écran (les marques).</summary>
    public struct LinkSpan
    {
        public int Start, Length;
        public bool IsMark; // vrai = « [[cible| » ou « ]] » ; faux = le texte du lien
        public LinkSpan(int start, int length, bool isMark)
        {
            Start = start; Length = length; IsMark = isMark;
        }
        public int End { get { return Start + Length; } }
    }

    public static class Links
    {
        public const int MaxLength = 120;

        /// <summary>Tous les liens d'un texte plat, dans l'ordre. Une notation
        /// vide ou démesurée n'est pas un lien.</summary>
        public static List<Link> Find(string text)
        {
            var links = new List<Link>();
            if (string.IsNullOrEmpty(text)) return links;
            var cursor = 0;
            while (true)
            {
                var open = text.IndexOf("[[", cursor, StringComparison.Ordinal);
                if (open < 0) break;
                var close = text.IndexOf("]]", open + 2, StringComparison.Ordinal);
                if (close < 0) break;
                // Un « [[ » ouvert dans un « [[ » : le dernier ouvrant compte.
                var inner = text.Substring(open + 2, close - open - 2);
                var reopen = inner.LastIndexOf("[[", StringComparison.Ordinal);
                if (reopen >= 0) { cursor = open + 2 + reopen; continue; }
                var link = Parse(inner, open, close + 2);
                if (link != null) links.Add(link);
                cursor = close + 2;
            }
            return links;
        }

        private static Link Parse(string inner, int start, int end)
        {
            if (inner.Length == 0 || inner.Length > MaxLength) return null;
            var pipe = inner.IndexOf('|');
            string target, shown;
            var textStart = start + 2;
            if (pipe < 0)
            {
                target = inner.Trim();
                shown = inner;
            }
            else
            {
                target = inner.Substring(0, pipe).Trim();
                shown = inner.Substring(pipe + 1);
                textStart = start + 2 + pipe + 1;
            }
            if (target.Length == 0) return null;
            if (shown.Length == 0) { shown = target; }
            return new Link
            {
                Start = start,
                End = end,
                TextStart = textStart,
                TextEnd = textStart + (pipe < 0 ? inner.Length : inner.Length - pipe - 1),
                Target = target,
                Text = shown
            };
        }

        /// <summary>Le lien qui couvre l'offset (marques comprises), sinon null.</summary>
        public static Link At(string text, int offset)
        {
            foreach (var link in Find(text))
                if (offset >= link.Start && offset <= link.End) return link;
            return null;
        }

        /// <summary>Les plages d'un texte occupées par des liens : marques
        /// (à masquer) et textes affichés (à mettre en évidence), triées.</summary>
        public static List<LinkSpan> Spans(string text)
        {
            var spans = new List<LinkSpan>();
            foreach (var link in Find(text))
            {
                spans.Add(new LinkSpan(link.Start, link.TextStart - link.Start, true));
                if (link.TextEnd > link.TextStart)
                    spans.Add(new LinkSpan(link.TextStart, link.TextEnd - link.TextStart, false));
                spans.Add(new LinkSpan(link.TextEnd, link.End - link.TextEnd, true));
            }
            return spans;
        }

        /// <summary>La notation à insérer : « [[Cible]] », ou « [[Cible|texte]] »
        /// quand le texte sélectionné diffère de la cible.</summary>
        public static string Markup(string target, string text)
        {
            target = (target ?? "").Trim();
            var shown = text ?? "";
            if (shown.Trim().Length == 0 || shown.Trim() == target
                || shown.IndexOf('|') >= 0 || shown.IndexOf("]]", StringComparison.Ordinal) >= 0
                || shown.IndexOf('\n') >= 0)
                return "[[" + target + "]]";
            return "[[" + target + "|" + shown + "]]";
        }

        /// <summary>Le texte sans ses marques de lien : ce que le lecteur lit.</summary>
        public static string Strip(string text)
        {
            var links = Find(text);
            if (links.Count == 0) return text ?? "";
            var sb = new StringBuilder();
            var cursor = 0;
            foreach (var link in links)
            {
                sb.Append(text, cursor, link.Start - cursor);
                sb.Append(text, link.TextStart, link.TextEnd - link.TextStart);
                cursor = link.End;
            }
            sb.Append(text, cursor, text.Length - cursor);
            return sb.ToString();
        }

        /// <summary>Une copie du document sans les marques de lien — pour
        /// tout ce qui sort de l'application (export, impression, PDF).
        /// Les runs gardent leur mise en forme ; notes et annotations sont
        /// partagées (jamais modifiées ici).</summary>
        public static TextDocument Strip(TextDocument document)
        {
            var copy = new TextDocument
            {
                Footnotes = document.Footnotes,
                Annotations = document.Annotations
            };
            foreach (var paragraph in document.Paragraphs)
                copy.Paragraphs.Add(Strip(paragraph));
            return copy;
        }

        public static TextParagraph Strip(TextParagraph paragraph)
        {
            var copy = PivotEdit.CloneParagraphShell(paragraph);
            var marks = new List<LinkSpan>();
            foreach (var span in Spans(PivotEdit.FlatText(paragraph)))
                if (span.IsMark) marks.Add(span);
            if (marks.Count == 0)
            {
                copy.Runs.AddRange(paragraph.Runs);
                return copy;
            }
            var offset = 0;
            foreach (var run in paragraph.Runs)
            {
                if (PivotEdit.IsElement(run))
                {
                    copy.Runs.Add(run);
                    offset++;
                    continue;
                }
                var kept = Keep(run.Text, offset, marks);
                offset += run.Text.Length;
                if (kept.Length == 0) continue;
                if (kept == run.Text) { copy.Runs.Add(run); continue; }
                var clone = PivotEdit.CloneFormat(run);
                clone.Text = kept;
                copy.Runs.Add(clone);
            }
            return copy;
        }

        /// <summary>Le texte d'un run privé des caractères tombant dans une
        /// plage de marques (offsets plats).</summary>
        private static string Keep(string text, int offset, List<LinkSpan> marks)
        {
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var flat = offset + i;
                var hidden = false;
                foreach (var mark in marks)
                    if (flat >= mark.Start && flat < mark.End) { hidden = true; break; }
                if (!hidden) sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>Déplacement du curseur quand les marques sont masquées :
        /// un pas qui entre dans une marque la traverse d'un coup (comme le
        /// texte masqué de Word). direction &gt; 0 = vers la droite.</summary>
        public static int SkipHidden(string text, int offset, int direction)
        {
            foreach (var span in Spans(text))
            {
                if (!span.IsMark) continue;
                if (direction > 0 && offset > span.Start && offset < span.End) return span.End;
                if (direction < 0 && offset >= span.Start && offset < span.End) return span.Start;
            }
            return offset;
        }

        /// <summary>Un clic qui atterrit dans une marque masquée se pose au
        /// bord visible le plus proche : une marque ouvrante mène au début
        /// du texte du lien, une fermante après le lien.</summary>
        public static int SnapOutOfHidden(string text, int offset)
        {
            foreach (var link in Find(text))
            {
                if (offset > link.Start && offset < link.TextStart) return link.TextStart;
                if (offset > link.TextEnd && offset < link.End) return link.End;
            }
            return offset;
        }

        /// <summary>Les cibles distinctes des liens d'un texte, dans l'ordre.</summary>
        public static List<string> Targets(string text)
        {
            var targets = new List<string>();
            foreach (var link in Find(text))
            {
                var known = false;
                foreach (var target in targets)
                    if (SameTitle(target, link.Target)) { known = true; break; }
                if (!known) targets.Add(link.Target);
            }
            return targets;
        }

        /// <summary>Le texte contient-il un lien vers ce titre ? (casse et
        /// accents ignorés, comme la résolution des cibles).</summary>
        public static bool LinksTo(string text, string title)
        {
            foreach (var link in Find(text))
                if (SameTitle(link.Target, title)) return true;
            return false;
        }

        public static bool SameTitle(string a, string b)
        {
            return string.Compare(a ?? "", b ?? "",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.CompareOptions.IgnoreCase
                | System.Globalization.CompareOptions.IgnoreNonSpace) == 0;
        }
    }
}
