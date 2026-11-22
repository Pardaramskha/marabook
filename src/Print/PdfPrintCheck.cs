using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Marabook.Print
{
    /// <summary>Ce que le contrôle d'impression regarde — l'auteur coche.</summary>
    public class PrintCheckOptions
    {
        public bool Bleed = true;        // les fonds perdus
        public double MinBleedMm = 3;
        public bool Size = true;         // la taille des pages
        public double WidthMm, HeightMm; // 0 = celle de la première page
        public bool Consistency = true;  // l'homogénéité d'une page à l'autre
        public bool Fonts = true;        // les polices incorporées
    }

    public class PrintCheckLine
    {
        public const int Ok = 0, Note = 1, Problem = 2;
        public string Check = ""; // « Fonds perdus », « Taille »…
        public int Level;
        public string Text = "";
    }

    public class PrintCheckReport
    {
        public readonly List<PrintCheckLine> Lines = new List<PrintCheckLine>();
        public int Pages, Fonts;
        public bool Readable = true; // faux : ce n'est pas un PDF, ou il est verrouillé
        public bool Locked;

        public int Problems { get { var n = 0; foreach (var line in Lines) if (line.Level == PrintCheckLine.Problem) n++; return n; } }

        public void Add(string check, int level, string text) { Lines.Add(new PrintCheckLine { Check = check, Level = level, Text = text }); }

        public string Summary()
        {
            if (!Readable) return Lines.Count > 0 ? Lines[0].Text : "Le fichier ne se lit pas.";
            var problems = Problems;
            return (Pages == 1 ? "1 page" : Pages + " pages") + " — " + (problems == 0 ? "rien à signaler sur ce qui a été vérifié." : problems == 1 ? "1 point à revoir." : problems + " points à revoir.");
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary());
            foreach (var line in Lines)
                sb.AppendLine((line.Level == PrintCheckLine.Problem ? "[À REVOIR] " : line.Level == PrintCheckLine.Note ? "[à savoir] " : "[bon] ") + line.Check + " : " + line.Text);
            return sb.ToString();
        }
    }

    /// <summary>LE CONTRÔLE D'IMPRESSION (2.0.0, 21/11/2026, Rémi : l'Atelier,
    /// PDFaah). Ce qu'un imprimeur regarde d'abord dans un PDF, quel que soit
    /// le logiciel qui l'a fait : les fonds perdus, la taille des pages, leur
    /// homogénéité, les polices incorporées. On coche ce qu'on veut vérifier ;
    /// le rapport dit ce qui sort du fichier, en clair. Lecture seule : le
    /// fichier n'est jamais modifié. Un fichier verrouillé ne se contrôle pas
    /// (ses objets sont chiffrés) : l'Unlocker d'abord.</summary>
    public static class PdfPrintCheck
    {
        public const string CheckBleed = "Fonds perdus", CheckSize = "Taille", CheckConsistency = "Homogénéité", CheckFonts = "Polices", CheckFile = "Fichier";
        private const double PtToMm = 25.4 / 72.0;
        private const double Tolerance = 0.5; // mm

        /// <summary>Les formats proposés : nom, largeur, hauteur (mm, en portrait).</summary>
        public static readonly string[][] Formats =
        {
            new[] { "Poche 110 × 178", "110", "178" },
            new[] { "A5 — 148 × 210", "148", "210" },
            new[] { "Roman 140 × 216", "140", "216" },
            new[] { "Digest 152 × 229", "152", "229" },
            new[] { "B5 — 176 × 250", "176", "250" },
            new[] { "A4 — 210 × 297", "210", "297" },
            new[] { "US Letter 216 × 279", "215.9", "279.4" }
        };

        private sealed class PageInfo
        {
            public double[] Media, Crop, Trim, Bleed, Art;
            public bool Rotated;
            public Dictionary<string, object> Resources;
            public double[] Finished { get { return Trim ?? Art ?? Crop ?? Media; } }
            public double WidthMm { get { var b = Finished; return b == null ? 0 : (Rotated ? b[3] - b[1] : b[2] - b[0]) * PtToMm; } }
            public double HeightMm { get { var b = Finished; return b == null ? 0 : (Rotated ? b[2] - b[0] : b[3] - b[1]) * PtToMm; } }
        }

        public static PrintCheckReport Inspect(byte[] bytes, PrintCheckOptions options)
        {
            var report = new PrintCheckReport();
            options = options ?? new PrintCheckOptions();
            PdfLite pdf = null;
            try { pdf = bytes == null || bytes.Length < 64 ? null : PdfLite.Read(bytes); }
            catch (Exception) { pdf = null; }
            if (pdf == null || pdf.Objects.Count == 0)
            {
                report.Readable = false;
                report.Add(CheckFile, PrintCheckLine.Problem, "Ce n'est pas un PDF.");
                return report;
            }
            if (pdf.Trailer.ContainsKey("Encrypt"))
            {
                report.Readable = false;
                report.Locked = true;
                report.Add(CheckFile, PrintCheckLine.Problem, "Ce PDF est verrouillé (chiffré) : son contenu ne se lit pas tel quel. Déverrouillez-le d'abord avec l'Unlocker, puis contrôlez la copie.");
                return report;
            }
            var pages = new List<PageInfo>();
            try
            {
                var catalog = pdf.Dict(pdf.Trailer.ContainsKey("Root") ? pdf.Trailer["Root"] : null);
                var root = catalog == null ? null : pdf.Dict(catalog.ContainsKey("Pages") ? catalog["Pages"] : null);
                Collect(pdf, root, new Dictionary<string, object>(StringComparer.Ordinal), pages, new HashSet<object>(ReferenceComparer.Instance), 0);
            }
            catch (Exception) { }
            report.Pages = pages.Count;
            if (pages.Count == 0)
            {
                report.Readable = false;
                report.Add(CheckFile, PrintCheckLine.Problem, pdf.HasObjectStreams && !pdf.ObjectStreamsRead ? "Les pages de ce PDF sont rangées d'une façon que ce contrôle ne sait pas lire." : "Aucune page n'a été trouvée dans ce fichier.");
                return report;
            }
            if (options.Size) Size(report, pages, options);
            if (options.Bleed) Bleed(report, pages, options);
            if (options.Consistency) Consistency(report, pages);
            if (options.Fonts) Fonts(report, pdf, pages);
            if (!options.Size && !options.Bleed && !options.Consistency && !options.Fonts) report.Add(CheckFile, PrintCheckLine.Note, "Rien n'est coché : rien n'a été vérifié.");
            return report;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<object>.Equals(object a, object b) { return ReferenceEquals(a, b); }
            int IEqualityComparer<object>.GetHashCode(object value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }

        private static void Collect(PdfLite pdf, Dictionary<string, object> node, Dictionary<string, object> inherited, List<PageInfo> pages, HashSet<object> seen, int depth)
        {
            if (node == null || depth > 64 || !seen.Add(node)) return;
            var own = new Dictionary<string, object>(inherited, StringComparer.Ordinal);
            foreach (var key in new[] { "MediaBox", "CropBox", "Resources", "Rotate" })
                if (node.ContainsKey(key)) own[key] = node[key];
            var kids = pdf.Get(node, "Kids") as List<object>;
            if (kids == null || pdf.Name(node, "Type") == "Page")
            {
                var info = new PageInfo
                {
                    Media = Box(pdf, own.ContainsKey("MediaBox") ? own["MediaBox"] : null),
                    Crop = Box(pdf, own.ContainsKey("CropBox") ? own["CropBox"] : null),
                    Trim = Box(pdf, node.ContainsKey("TrimBox") ? node["TrimBox"] : null),
                    Bleed = Box(pdf, node.ContainsKey("BleedBox") ? node["BleedBox"] : null),
                    Art = Box(pdf, node.ContainsKey("ArtBox") ? node["ArtBox"] : null),
                    Resources = pdf.Dict(own.ContainsKey("Resources") ? own["Resources"] : null)
                };
                var rotate = pdf.Resolve(own.ContainsKey("Rotate") ? own["Rotate"] : null);
                if (rotate is double) { var turns = ((int)(double)rotate % 360 + 360) % 360; info.Rotated = turns == 90 || turns == 270; }
                pages.Add(info);
                return;
            }
            foreach (var kid in kids) Collect(pdf, pdf.Dict(kid), own, pages, seen, depth + 1);
        }

        private static double[] Box(PdfLite pdf, object value)
        {
            var list = pdf.Resolve(value) as List<object>;
            if (list == null || list.Count != 4) return null;
            var box = new double[4];
            for (var i = 0; i < 4; i++)
            {
                var number = pdf.Resolve(list[i]);
                if (!(number is double)) return null;
                box[i] = (double)number;
            }
            return new[] { Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Max(box[0], box[2]), Math.Max(box[1], box[3]) };
        }

        private static string Mm(double value)
        {
            return (Math.Round(value * 10) / 10).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"));
        }

        private static string SizeLabel(double width, double height) { return Mm(width) + " × " + Mm(height) + " mm"; }

        /// <summary>« 3, 5 à 8, 12 » — les pages (comptées de 1), en plages.</summary>
        public static string Ranges(List<int> pages)
        {
            var sb = new StringBuilder();
            var shown = 0;
            for (var i = 0; i < pages.Count; i++)
            {
                var start = pages[i];
                var end = start;
                while (i + 1 < pages.Count && pages[i + 1] == end + 1) { end = pages[i + 1]; i++; }
                if (shown == 8) { sb.Append("…"); break; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(start == end ? start.ToString() : end == start + 1 ? start + ", " + end : start + " à " + end);
                shown++;
            }
            return sb.ToString();
        }

        private static string PagesLabel(List<int> pages)
        {
            return pages.Count == 1 ? "page " + pages[0] : "pages " + Ranges(pages);
        }

        // ------------------------------------------------------------ la taille

        private static void Size(PrintCheckReport report, List<PageInfo> pages, PrintCheckOptions options)
        {
            double width = options.WidthMm, height = options.HeightMm;
            var fromFirst = width <= 0 || height <= 0;
            if (fromFirst) { width = pages[0].WidthMm; height = pages[0].HeightMm; }
            var declared = pages[0].Trim != null || pages[0].Art != null;
            var off = new List<int>();
            var turned = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                var w = pages[i].WidthMm;
                var h = pages[i].HeightMm;
                if (Math.Abs(w - width) <= Tolerance && Math.Abs(h - height) <= Tolerance) continue;
                if (Math.Abs(w - height) <= Tolerance && Math.Abs(h - width) <= Tolerance) turned.Add(i + 1);
                else off.Add(i + 1);
            }
            var what = declared ? "format fini (zone de coupe)" : "format de la page (aucune zone de coupe déclarée)";
            if (fromFirst)
                report.Add(CheckSize, PrintCheckLine.Ok, "Première page : " + SizeLabel(width, height) + " — " + what + ".");
            if (off.Count == 0 && turned.Count == 0)
            {
                if (!fromFirst) report.Add(CheckSize, PrintCheckLine.Ok, (pages.Count == 1 ? "La page fait" : "Les " + pages.Count + " pages font") + " bien " + SizeLabel(width, height) + " (" + what + ").");
                else if (pages.Count > 1) report.Add(CheckSize, PrintCheckLine.Ok, "Toutes les pages ont cette taille.");
                return;
            }
            if (off.Count > 0)
                report.Add(CheckSize, PrintCheckLine.Problem, (off.Count == 1 ? "Une page n'a" : off.Count + " pages n'ont") + " pas la taille attendue (" + SizeLabel(width, height) + ") : " + PagesLabel(off)
                    + " — par exemple " + SizeLabel(pages[off[0] - 1].WidthMm, pages[off[0] - 1].HeightMm) + " en page " + off[0] + ".");
            if (turned.Count > 0)
                report.Add(CheckSize, PrintCheckLine.Problem, (turned.Count == 1 ? "Une page a" : turned.Count + " pages ont") + " la bonne taille mais dans l'autre sens (paysage pour portrait, ou l'inverse) : " + PagesLabel(turned) + ".");
        }

        // ------------------------------------------------------------ les fonds perdus

        private static void Bleed(PrintCheckReport report, List<PageInfo> pages, PrintCheckOptions options)
        {
            var noTrim = new List<int>();
            var noRoom = new List<int>();
            var thin = new List<int>();
            var implied = 0;
            var least = double.MaxValue;
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var finished = page.Trim ?? page.Art;
                if (finished == null) { noTrim.Add(i + 1); continue; }
                // La zone de fond perdu déclarée ; à défaut, tout ce qui dépasse de la coupe dans la page.
                var outer = page.Bleed;
                if (outer == null) { outer = page.Crop ?? page.Media; implied++; }
                if (outer == null) { noRoom.Add(i + 1); continue; }
                var side = Math.Min(Math.Min(finished[0] - outer[0], finished[1] - outer[1]), Math.Min(outer[2] - finished[2], outer[3] - finished[3])) * PtToMm;
                if (side < least) least = side;
                if (side <= 0.05) noRoom.Add(i + 1);
                else if (side + 0.05 < options.MinBleedMm) thin.Add(i + 1);
            }
            if (noTrim.Count == pages.Count)
            {
                report.Add(CheckBleed, PrintCheckLine.Problem, "Aucune page ne déclare sa zone de coupe (TrimBox) : le fond perdu ne peut pas se mesurer. Un PDF d'imprimeur la déclare ; un PDF de lecture, non.");
                return;
            }
            if (noTrim.Count > 0) report.Add(CheckBleed, PrintCheckLine.Problem, (noTrim.Count == 1 ? "Une page ne déclare" : noTrim.Count + " pages ne déclarent") + " pas de zone de coupe : " + PagesLabel(noTrim) + ".");
            if (noRoom.Count > 0) report.Add(CheckBleed, PrintCheckLine.Problem, (noRoom.Count == 1 ? "Une page n'a" : noRoom.Count + " pages n'ont") + " aucun fond perdu (la page s'arrête à la coupe sur un côté au moins) : " + PagesLabel(noRoom) + ".");
            if (thin.Count > 0) report.Add(CheckBleed, PrintCheckLine.Problem, (thin.Count == 1 ? "Une page a" : thin.Count + " pages ont") + " moins de " + Mm(options.MinBleedMm) + " mm de fond perdu sur un côté au moins : " + PagesLabel(thin) + ".");
            if (noTrim.Count == 0 && noRoom.Count == 0 && thin.Count == 0)
                report.Add(CheckBleed, PrintCheckLine.Ok, "Au moins " + Mm(least) + " mm de fond perdu de chaque côté, sur toutes les pages (demandé : " + Mm(options.MinBleedMm) + " mm).");
            if (implied > 0)
                report.Add(CheckBleed, PrintCheckLine.Note, (implied == pages.Count ? "Le fichier ne déclare pas" : implied + " pages ne déclarent pas") + " de zone de fond perdu (BleedBox) : la mesure est prise entre la coupe et le bord de la page, traits de coupe compris s'il y en a.");
        }

        // ------------------------------------------------------------ l'homogénéité

        private static void Consistency(PrintCheckReport report, List<PageInfo> pages)
        {
            // Les tailles rencontrées, dans l'ordre de leur première page.
            var sizes = new List<double[]>();
            var members = new List<List<int>>();
            int withTrim = 0, portrait = 0, landscape = 0;
            var outside = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                double w = page.WidthMm, h = page.HeightMm;
                var found = -1;
                for (var s = 0; s < sizes.Count; s++)
                    if (Math.Abs(sizes[s][0] - w) <= Tolerance && Math.Abs(sizes[s][1] - h) <= Tolerance) { found = s; break; }
                if (found < 0) { sizes.Add(new[] { w, h }); members.Add(new List<int>()); found = sizes.Count - 1; }
                members[found].Add(i + 1);
                if (page.Trim != null || page.Art != null) withTrim++;
                if (w > h + Tolerance) landscape++; else portrait++;
                var finished = page.Trim ?? page.Art;
                if (finished != null && page.Media != null && !Inside(finished, page.Media)) outside.Add(i + 1);
                else if (page.Bleed != null && page.Media != null && !Inside(page.Bleed, page.Media)) outside.Add(i + 1);
                else if (finished != null && page.Bleed != null && !Inside(finished, page.Bleed)) outside.Add(i + 1);
            }
            var clean = true;
            if (sizes.Count > 1)
            {
                clean = false;
                var sb = new StringBuilder();
                var main = 0;
                for (var s = 1; s < sizes.Count; s++) if (members[s].Count > members[main].Count) main = s;
                for (var s = 0; s < sizes.Count && s < 5; s++)
                {
                    if (s == main) continue;
                    if (sb.Length > 0) sb.Append(" ; ");
                    sb.Append(SizeLabel(sizes[s][0], sizes[s][1])).Append(" (").Append(PagesLabel(members[s])).Append(")");
                }
                report.Add(CheckConsistency, PrintCheckLine.Problem, "Les pages n'ont pas toutes la même taille : " + sizes.Count + " tailles. La plus fréquente est " + SizeLabel(sizes[main][0], sizes[main][1])
                    + " (" + members[main].Count + " pages) ; s'en écartent : " + sb + (sizes.Count > 5 ? " ; …" : "") + ".");
            }
            if (portrait > 0 && landscape > 0)
            {
                clean = false;
                report.Add(CheckConsistency, PrintCheckLine.Problem, "Le fichier mêle des pages en portrait (" + portrait + ") et en paysage (" + landscape + ").");
            }
            if (withTrim > 0 && withTrim < pages.Count)
            {
                clean = false;
                report.Add(CheckConsistency, PrintCheckLine.Problem, "Certaines pages déclarent une zone de coupe et d'autres non (" + withTrim + " sur " + pages.Count + ").");
            }
            if (outside.Count > 0)
            {
                clean = false;
                report.Add(CheckConsistency, PrintCheckLine.Problem, "Les zones de " + (outside.Count == 1 ? "la " + PagesLabel(outside) : "certaines pages") + " ne s'emboîtent pas (coupe hors du fond perdu, ou fond perdu hors de la page)" + (outside.Count == 1 ? "." : " : " + PagesLabel(outside) + "."));
            }
            if (clean) report.Add(CheckConsistency, PrintCheckLine.Ok, pages.Count == 1 ? "Une seule page : rien à comparer." : "Les " + pages.Count + " pages ont la même taille, le même sens et les mêmes zones.");
        }

        private static bool Inside(double[] inner, double[] outer)
        {
            const double slack = 0.05;
            return inner[0] >= outer[0] - slack && inner[1] >= outer[1] - slack && inner[2] <= outer[2] + slack && inner[3] <= outer[3] + slack;
        }

        // ------------------------------------------------------------ les polices

        private sealed class FontRun
        {
            public PdfLite Pdf;
            public readonly HashSet<object> Seen = new HashSet<object>(ReferenceComparer.Instance);
            public readonly List<string> Missing = new List<string>();
            public readonly Dictionary<string, int> MissingPage = new Dictionary<string, int>(StringComparer.Ordinal);
            public int Count, Embedded, Page;
        }

        private static void Fonts(PrintCheckReport report, PdfLite pdf, List<PageInfo> pages)
        {
            var run = new FontRun { Pdf = pdf };
            for (var i = 0; i < pages.Count; i++)
            {
                run.Page = i + 1;
                try { Resources(run, pages[i].Resources, 0); }
                catch (Exception) { }
            }
            report.Fonts = run.Count;
            if (run.Count == 0)
            {
                report.Add(CheckFonts, PrintCheckLine.Note, "Aucune police dans ce fichier : il ne porte pas de texte (des images, ou un texte vectorisé).");
                return;
            }
            if (run.Missing.Count == 0)
            {
                report.Add(CheckFonts, PrintCheckLine.Ok, (run.Count == 1 ? "La police est incorporée" : "Les " + run.Count + " polices sont incorporées") + " au fichier.");
                return;
            }
            var sb = new StringBuilder();
            for (var i = 0; i < run.Missing.Count && i < 12; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(run.Missing[i]).Append(" (page ").Append(run.MissingPage[run.Missing[i]]).Append(")");
            }
            if (run.Missing.Count > 12) sb.Append(", …");
            report.Add(CheckFonts, PrintCheckLine.Problem, (run.Missing.Count == 1 ? "Une police n'est pas incorporée" : run.Missing.Count + " polices sur " + run.Count + " ne sont pas incorporées")
                + " : l'imprimeur la remplacera par ce qu'il a. " + sb + ".");
        }

        private static void Resources(FontRun run, Dictionary<string, object> resources, int depth)
        {
            var pdf = run.Pdf;
            if (resources == null || depth > 12 || !run.Seen.Add(resources)) return;
            var fonts = pdf.Dict(resources.ContainsKey("Font") ? resources["Font"] : null);
            if (fonts != null) foreach (var pair in fonts) Font(run, pdf.Dict(pair.Value), depth);
            var objects = pdf.Dict(resources.ContainsKey("XObject") ? resources["XObject"] : null);
            if (objects != null)
                foreach (var pair in objects)
                {
                    var form = pdf.Dict(pair.Value);
                    if (form == null || pdf.Name(form, "Subtype") != "Form" || !run.Seen.Add(form)) continue;
                    Resources(run, pdf.Dict(form.ContainsKey("Resources") ? form["Resources"] : null), depth + 1);
                }
            var patterns = pdf.Dict(resources.ContainsKey("Pattern") ? resources["Pattern"] : null);
            if (patterns != null)
                foreach (var pair in patterns)
                {
                    var pattern = pdf.Dict(pair.Value);
                    if (pattern == null || !run.Seen.Add(pattern)) continue;
                    Resources(run, pdf.Dict(pattern.ContainsKey("Resources") ? pattern["Resources"] : null), depth + 1);
                }
        }

        private static void Font(FontRun run, Dictionary<string, object> font, int depth)
        {
            var pdf = run.Pdf;
            if (font == null || !run.Seen.Add(font)) return;
            run.Count++;
            var subtype = pdf.Name(font, "Subtype");
            if (subtype == "Type3")
            {
                // Dessinée dans le fichier lui-même : incorporée par nature. Ses dessins peuvent appeler d'autres polices.
                run.Embedded++;
                Resources(run, pdf.Dict(font.ContainsKey("Resources") ? font["Resources"] : null), depth + 1);
                return;
            }
            var holder = font;
            if (subtype == "Type0")
            {
                var descendants = pdf.Get(font, "DescendantFonts") as List<object>;
                holder = descendants == null || descendants.Count == 0 ? null : pdf.Dict(descendants[0]);
            }
            var descriptor = holder == null ? null : pdf.Dict(holder.ContainsKey("FontDescriptor") ? holder["FontDescriptor"] : null);
            var embedded = descriptor != null && (descriptor.ContainsKey("FontFile") || descriptor.ContainsKey("FontFile2") || descriptor.ContainsKey("FontFile3"));
            if (embedded) { run.Embedded++; return; }
            var name = pdf.Name(font, "BaseFont") ?? (holder == null ? null : pdf.Name(holder, "BaseFont")) ?? "police sans nom";
            if (run.MissingPage.ContainsKey(name)) return;
            run.Missing.Add(name);
            run.MissingPage[name] = run.Page;
        }
    }

    /// <summary>UNE PAGE d'un PDF en cours de recomposition (Chop-Chop) : de quel fichier ouvert elle vient, et laquelle.</summary>
    public class PdfPageRef
    {
        public int Source, Page;
        public double WidthPt, HeightPt;
        public PdfPageRef Clone() { return (PdfPageRef)MemberwiseClone(); }
    }

    /// <summary>Des pages à recopier d'un même fichier, dans cet ordre.</summary>
    public class PdfPageRun
    {
        public int Source;
        public readonly List<int> Pages = new List<int>();
    }

    /// <summary>LA RECOMPOSITION D'UN PDF (Chop-Chop, 21/11/2026) : la liste des
    /// pages telle qu'on la veut — tirées d'un ou de plusieurs fichiers, dans
    /// l'ordre choisi. Rien ici ne touche à un fichier : c'est le plan ; le
    /// moteur (PDFium, dans l'application) l'exécute à l'enregistrement.</summary>
    public class PdfAssembly
    {
        public readonly List<string> Sources = new List<string>();
        public readonly List<PdfPageRef> Pages = new List<PdfPageRef>();

        /// <summary>Ajoute toutes les pages d'un fichier à cet endroit (négatif ou trop grand : à la fin). Rend la position de la première.</summary>
        public int Insert(string path, IList<double[]> sizes, int at)
        {
            var source = Sources.Count;
            Sources.Add(path);
            if (at < 0 || at > Pages.Count) at = Pages.Count;
            for (var i = 0; i < sizes.Count; i++)
                Pages.Insert(at + i, new PdfPageRef { Source = source, Page = i, WidthPt = sizes[i][0], HeightPt = sizes[i][1] });
            return at;
        }

        private static List<int> Clean(IEnumerable<int> indices, int count)
        {
            var list = new List<int>();
            if (indices != null) foreach (var index in indices) if (index >= 0 && index < count && !list.Contains(index)) list.Add(index);
            list.Sort();
            return list;
        }

        /// <summary>Retire ces pages. Rend le nombre retiré.</summary>
        public int Remove(IEnumerable<int> indices)
        {
            var list = Clean(indices, Pages.Count);
            for (var i = list.Count - 1; i >= 0; i--) Pages.RemoveAt(list[i]);
            return list.Count;
        }

        /// <summary>Déplace ces pages, ensemble et dans leur ordre, pour qu'elles
        /// se trouvent AVANT la page qui est aujourd'hui à « before » (le nombre
        /// de pages = tout à la fin). Rend la nouvelle position de la première.</summary>
        public int Move(IEnumerable<int> indices, int before)
        {
            var list = Clean(indices, Pages.Count);
            if (list.Count == 0) return -1;
            if (before < 0) before = 0;
            if (before > Pages.Count) before = Pages.Count;
            var moved = new List<PdfPageRef>();
            foreach (var index in list) moved.Add(Pages[index]);
            var target = before;
            foreach (var index in list) if (index < before) target--;
            for (var i = list.Count - 1; i >= 0; i--) Pages.RemoveAt(list[i]);
            Pages.InsertRange(target, moved);
            return target;
        }

        /// <summary>D'un cran vers le début (−1) ou vers la fin (+1) ; les pages choisies restent groupées telles qu'elles sont.</summary>
        public List<int> Nudge(IEnumerable<int> indices, int step)
        {
            var list = Clean(indices, Pages.Count);
            if (list.Count == 0 || step == 0) return list;
            if (step < 0)
            {
                if (list[0] == 0) return list;
                for (var i = 0; i < list.Count; i++) { var at = list[i]; var page = Pages[at]; Pages.RemoveAt(at); Pages.Insert(at - 1, page); list[i] = at - 1; }
            }
            else
            {
                if (list[list.Count - 1] == Pages.Count - 1) return list;
                for (var i = list.Count - 1; i >= 0; i--) { var at = list[i]; var page = Pages[at]; Pages.RemoveAt(at); Pages.Insert(at + 1, page); list[i] = at + 1; }
            }
            return list;
        }

        /// <summary>Les pages à recopier, groupées par suites tirées d'un même fichier.</summary>
        public List<PdfPageRun> Runs()
        {
            var runs = new List<PdfPageRun>();
            foreach (var page in Pages)
            {
                if (runs.Count == 0 || runs[runs.Count - 1].Source != page.Source) runs.Add(new PdfPageRun { Source = page.Source });
                runs[runs.Count - 1].Pages.Add(page.Page);
            }
            return runs;
        }

        /// <summary>Vrai tant que rien n'a bougé : un seul fichier, toutes ses pages, dans leur ordre.</summary>
        public bool Untouched(int originalCount)
        {
            if (Sources.Count != 1 || Pages.Count != originalCount) return false;
            for (var i = 0; i < Pages.Count; i++) if (Pages[i].Source != 0 || Pages[i].Page != i) return false;
            return true;
        }

        /// <summary>Les DOUBLES PAGES, comme le livre ouvert : la première page
        /// est une page de droite (impaire), seule ; puis paire à gauche, impaire
        /// à droite. Chaque entrée = { gauche, droite }, −1 pour un côté vide.</summary>
        public static List<int[]> Spreads(int count)
        {
            var spreads = new List<int[]>();
            if (count <= 0) return spreads;
            spreads.Add(new[] { -1, 0 });
            for (var i = 1; i < count; i += 2) spreads.Add(new[] { i, i + 1 < count ? i + 1 : -1 });
            return spreads;
        }

        /// <summary>« page 4 (paire, à gauche) » — le rang et le côté d'une page dans le livre.</summary>
        public static string Side(int index)
        {
            return (index + 1) % 2 == 1 ? "impaire, à droite" : "paire, à gauche";
        }
    }
}
