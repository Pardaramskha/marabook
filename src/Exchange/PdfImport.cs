using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Un mot lu dans un PDF : son texte, sa boîte (repère du PDF :
    /// l'origine en bas à gauche, y vers le HAUT), le corps de sa police et
    /// ce que son nom laisse deviner de sa graisse.</summary>
    public class PdfWord
    {
        public string Text = "";
        public double Left, Bottom, Right, Top;
        public double Size;
        public bool Bold, Italic;
    }

    public class PdfPageWords
    {
        public double Width, Height;
        public readonly List<PdfWord> Words = new List<PdfWord>();
    }

    /// <summary>L'IMPORT D'UN PDF (21/11/2026) : des mots posés sur des
    /// pages, refaire des paragraphes. Un PDF ne sait pas ce qu'est un
    /// paragraphe — il ne garde que des positions — : tout ici est DÉDUIT,
    /// donc approximatif, et l'interface le dit.
    ///
    /// La méthode : les mots d'une page sont rangés en lignes (même hauteur),
    /// les lignes de haut en bas ; les titres courants et les folios (une
    /// ligne en tête ou en pied qui revient de page en page, ou un nombre
    /// seul) sont écartés ; un nouveau paragraphe commence après un écart
    /// plus grand que l'interligne, à un alinéa, ou après une ligne qui
    /// s'arrête court ; un mot coupé en fin de ligne est recollé ; un
    /// paragraphe à cheval sur deux pages est réuni ; une ligne nettement
    /// plus grosse que le corps devient un titre ; le gras et l'italique
    /// suivent le nom des polices. Ne sont PAS repris : les colonnes, les
    /// tableaux, les images, les notes de bas de page (elles viennent en
    /// paragraphes), les PDF scannés (ils n'ont pas de texte).
    ///
    /// La lecture du fichier elle-même est faite par PdfPig, une
    /// bibliothèque téléchargée à la demande (voir PdfPigDependency) : ce
    /// fichier-ci n'en dépend pas et se teste sans elle.</summary>
    public static class PdfImport
    {
        public sealed class Line
        {
            public readonly List<PdfWord> Words = new List<PdfWord>();
            public double Left, Right, Baseline, Top, Size;
            public int Page;
            public string Text
            {
                get
                {
                    var sb = new StringBuilder();
                    foreach (var word in Words) { if (sb.Length > 0) sb.Append(' '); sb.Append(word.Text); }
                    return sb.ToString();
                }
            }
        }

        public class Result
        {
            public TextDocument Document = new TextDocument();
            public int Pages, Paragraphs, Headings, DroppedLines, Rejoined;
            public bool NoText; // un PDF scanné : des pages, aucun mot
        }

        /// <summary>Les lignes d'une page, de haut en bas ; les mots de gauche à droite.</summary>
        public static List<Line> Lines(PdfPageWords page, int pageIndex)
        {
            var lines = new List<Line>();
            var words = new List<PdfWord>();
            foreach (var word in page.Words) if (word != null && !string.IsNullOrWhiteSpace(word.Text)) words.Add(word);
            words.Sort(delegate(PdfWord a, PdfWord b)
            {
                var order = b.Bottom.CompareTo(a.Bottom);
                return order != 0 ? order : a.Left.CompareTo(b.Left);
            });
            foreach (var word in words)
            {
                var height = Math.Max(1, word.Size > 0 ? word.Size : word.Top - word.Bottom);
                Line home = null;
                // La même ligne : des pieds à moins d'un tiers de corps d'écart.
                for (var i = lines.Count - 1; i >= 0 && i >= lines.Count - 4; i--)
                    if (Math.Abs(lines[i].Baseline - word.Bottom) <= Math.Max(lines[i].Size, height) * 0.35) { home = lines[i]; break; }
                if (home == null)
                {
                    home = new Line { Baseline = word.Bottom, Left = word.Left, Right = word.Right, Top = word.Top, Size = height, Page = pageIndex };
                    lines.Add(home);
                }
                home.Words.Add(word);
                home.Left = Math.Min(home.Left, word.Left);
                home.Right = Math.Max(home.Right, word.Right);
                home.Top = Math.Max(home.Top, word.Top);
                home.Size = Math.Max(home.Size, height);
            }
            foreach (var line in lines) line.Words.Sort(delegate(PdfWord a, PdfWord b) { return a.Left.CompareTo(b.Left); });
            lines.Sort(delegate(Line a, Line b) { return b.Baseline.CompareTo(a.Baseline); });
            return lines;
        }

        private static double Median(List<double> values, double fallback)
        {
            if (values.Count == 0) return fallback;
            values.Sort();
            return values[values.Count / 2];
        }

        /// <summary>Le texte d'une ligne, chiffres ôtés : deux titres courants
        /// qui ne diffèrent que par le folio se reconnaissent.</summary>
        private static string Skeleton(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text) if (!char.IsDigit(c) && !char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static bool IsFolio(string text)
        {
            var trimmed = text.Trim().Trim('-', '–', '—', '·', ' ');
            if (trimmed.Length == 0 || trimmed.Length > 5) return false;
            foreach (var c in trimmed) if (!char.IsDigit(c)) return false;
            return true;
        }

        public static Result Build(List<PdfPageWords> pages)
        {
            var result = new Result { Pages = pages == null ? 0 : pages.Count };
            if (pages == null || pages.Count == 0) { result.NoText = true; result.Document = TextDocument.FromPlainText(""); return result; }

            var perPage = new List<List<Line>>();
            var total = 0;
            for (var i = 0; i < pages.Count; i++)
            {
                var lines = Lines(pages[i], i);
                perPage.Add(lines);
                total += lines.Count;
            }
            if (total == 0) { result.NoText = true; result.Document = TextDocument.FromPlainText(""); return result; }

            // — Titres courants et folios : la première et la dernière ligne d'une page, si elles
            //   reviennent (au chiffre près) sur le tiers des pages, ou ne sont qu'un nombre.
            var heads = new Dictionary<string, int>(StringComparer.Ordinal);
            var feet = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var lines in perPage)
            {
                if (lines.Count < 2) continue;
                Bump(heads, Skeleton(lines[0].Text));
                Bump(feet, Skeleton(lines[lines.Count - 1].Text));
            }
            var threshold = Math.Max(2, pages.Count / 3);
            var kept = new List<Line>();
            for (var p = 0; p < perPage.Count; p++)
            {
                var lines = perPage[p];
                for (var i = 0; i < lines.Count; i++)
                {
                    var edge = lines.Count >= 2 && (i == 0 || i == lines.Count - 1);
                    if (edge)
                    {
                        var text = lines[i].Text;
                        var key = Skeleton(text);
                        var repeats = pages.Count >= 3 && key.Length > 0 && (i == 0 ? heads : feet)[key] >= threshold;
                        if (IsFolio(text) || repeats) { result.DroppedLines++; continue; }
                    }
                    kept.Add(lines[i]);
                }
            }
            if (kept.Count == 0) { result.NoText = true; result.Document = TextDocument.FromPlainText(""); return result; }

            // — Le corps du texte : sa taille, sa marge gauche, sa marge droite, son interligne.
            var sizes = new List<double>();
            var lefts = new List<double>();
            var rights = new List<double>();
            var gaps = new List<double>();
            for (var i = 0; i < kept.Count; i++)
            {
                sizes.Add(Math.Round(kept[i].Size * 2) / 2);
                if (i > 0 && kept[i].Page == kept[i - 1].Page)
                {
                    var gap = kept[i - 1].Baseline - kept[i].Baseline;
                    if (gap > 0) gaps.Add(gap);
                }
            }
            var body = Median(sizes, 11);
            foreach (var line in kept)
                if (Math.Abs(line.Size - body) <= body * 0.12) { lefts.Add(Math.Round(line.Left)); rights.Add(Math.Round(line.Right)); }
            var leading = Median(gaps, body * 1.3);
            lefts.Sort();
            var margin = lefts.Count == 0 ? 0 : lefts[lefts.Count / 6];              // les lignes d'alinéa tirent la médiane vers la droite
            rights.Sort();
            var edgeRight = rights.Count == 0 ? 0 : rights[rights.Count - 1 - rights.Count / 10]; // le plein de la justification

            // — Les paragraphes.
            var document = new TextDocument();
            document.Paragraphs.Clear();
            List<Line> current = null;
            var currentHeading = false;
            Action flush = delegate
            {
                if (current == null || current.Count == 0) return;
                document.Paragraphs.Add(MakeParagraph(current, currentHeading, body, result));
                result.Paragraphs++;
                if (currentHeading) result.Headings++;
                current = null;
            };
            for (var i = 0; i < kept.Count; i++)
            {
                var line = kept[i];
                var heading = line.Size >= body * 1.18 && line.Text.Length <= 140;
                var breakHere = current == null;
                if (!breakHere)
                {
                    var previous = current[current.Count - 1];
                    var samePage = previous.Page == line.Page;
                    var gap = previous.Baseline - line.Baseline;
                    var indent = line.Left - margin > body * 0.8 && previous.Left - margin <= body * 0.8;
                    var shortBefore = edgeRight - previous.Right > Math.Max(body * 2.5, (edgeRight - margin) * 0.12);
                    var endsSentence = EndsSentence(previous.Text);
                    if (heading != currentHeading) breakHere = true;
                    else if (Math.Abs(line.Size - previous.Size) > body * 0.15) breakHere = true;
                    else if (samePage && gap > leading * 1.45) breakHere = true;
                    else if (indent) breakHere = true;
                    else if (shortBefore && endsSentence) breakHere = true;
                    else if (!samePage && endsSentence && shortBefore) breakHere = true;
                    // Sinon la ligne continue le paragraphe — y compris d'une page à l'autre.
                }
                if (breakHere) { flush(); current = new List<Line>(); currentHeading = heading; }
                current.Add(line);
            }
            flush();
            if (document.Paragraphs.Count == 0) document = TextDocument.FromPlainText("");
            result.Document = document;
            return result;
        }

        private static void Bump(Dictionary<string, int> counts, string key)
        {
            int count;
            counts[key] = counts.TryGetValue(key, out count) ? count + 1 : 1;
        }

        private static bool EndsSentence(string text)
        {
            var trimmed = text.TrimEnd();
            if (trimmed.Length == 0) return true;
            var last = trimmed[trimmed.Length - 1];
            return last == '.' || last == '!' || last == '?' || last == '…' || last == '»' || last == '"' || last == ':' || last == '”';
        }

        /// <summary>Les lignes d'un paragraphe mises bout à bout : un mot coupé
        /// par un trait d'union en fin de ligne est recollé quand la suite
        /// commence par une minuscule ; le gras et l'italique font des passages.</summary>
        private static TextParagraph MakeParagraph(List<Line> lines, bool heading, double body, Result result)
        {
            var paragraph = new TextParagraph { StyleId = heading ? (lines[0].Size >= body * 1.5 ? "title1" : "title2") : "body" };
            paragraph.Runs.Clear();
            var sb = new StringBuilder();
            bool bold = false, italic = false;
            var any = false;
            Action close = delegate
            {
                if (sb.Length == 0) return;
                var run = new TextRun { Text = sb.ToString() };
                if (bold && !heading) run.Bold = true;
                if (italic) run.Italic = true;
                paragraph.Runs.Add(run);
                sb.Length = 0;
            };
            for (var l = 0; l < lines.Count; l++)
            {
                var words = lines[l].Words;
                for (var w = 0; w < words.Count; w++)
                {
                    var word = words[w];
                    var text = word.Text;
                    var glue = false;
                    if (any)
                    {
                        // Premier mot d'une ligne : la précédente finissait-elle par une coupe ?
                        if (w == 0 && sb.Length > 1 && sb[sb.Length - 1] == '-' && char.IsLetter(sb[sb.Length - 2]) && text.Length > 0 && char.IsLower(text[0]))
                        {
                            sb.Length--; // « por- » + « tait » = « portait »
                            glue = true;
                            result.Rejoined++;
                        }
                    }
                    if (any && (word.Bold != bold || word.Italic != italic))
                    {
                        if (!glue) sb.Append(' ');
                        close();
                    }
                    else if (any && !glue) sb.Append(' ');
                    bold = word.Bold;
                    italic = word.Italic;
                    sb.Append(text);
                    any = true;
                }
            }
            close();
            if (paragraph.Runs.Count == 0) paragraph.Runs.Add(new TextRun { Text = "" });
            return paragraph;
        }

        /// <summary>Ce que le nom d'une police dit de sa graisse :
        /// « ABCDEF+Garamond-BoldItalic » est grasse et italique.</summary>
        public static void StyleOfFont(string fontName, out bool bold, out bool italic)
        {
            var name = (fontName ?? "").ToLowerInvariant();
            var plus = name.IndexOf('+');
            if (plus >= 0) name = name.Substring(plus + 1);
            bold = name.Contains("bold") || name.Contains("black") || name.Contains("heavy") || name.EndsWith("-bd", StringComparison.Ordinal) || name.Contains("gras");
            if (name.Contains("semibold") || name.Contains("demibold")) bold = true;
            italic = name.Contains("italic") || name.Contains("oblique") || name.EndsWith("-it", StringComparison.Ordinal) || name.EndsWith("-ita", StringComparison.Ordinal) || name.Contains("italique");
        }

        /// <summary>Le compte rendu de l'import, pour l'auteur.</summary>
        public static string Summary(Result result)
        {
            if (result == null) return "";
            if (result.NoText) return "Ce PDF n'a pas de texte à lire : c'est sans doute un document scanné (des images de pages).";
            var parts = new List<string>();
            parts.Add(result.Pages + (result.Pages > 1 ? " pages" : " page"));
            parts.Add(result.Paragraphs + (result.Paragraphs > 1 ? " paragraphes" : " paragraphe"));
            if (result.Headings > 0) parts.Add(result.Headings + (result.Headings > 1 ? " titres" : " titre"));
            if (result.Rejoined > 0) parts.Add(result.Rejoined + (result.Rejoined > 1 ? " mots recollés" : " mot recollé"));
            if (result.DroppedLines > 0) parts.Add(result.DroppedLines + (result.DroppedLines > 1 ? " titres courants ou folios écartés" : " titre courant ou folio écarté"));
            return string.Join(", ", parts.ToArray()) + ". Les paragraphes sont déduits de la mise en page : relisez les coupes.";
        }
    }
}
