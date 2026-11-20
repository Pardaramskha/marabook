using System;
using System.Collections.Generic;
using System.Globalization;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Exchange
{
    /// <summary>Un titre de niveau posé avant un écrit : son libellé, son
    /// style, son rang dans les signets (0 = premier rang).</summary>
    public class ExportLevelTitle
    {
        public string Title = "";
        public string StyleId = ExportLevels.StyleFolder;
        public int Level;
    }

    /// <summary>LE RENDU PAR NIVEAU (20/11/2026, Rémi) : à l'export, un titre
    /// à chaque changement de GENRE (écrits, fiches, plans, cartes mentales),
    /// de LIVRE, de DOSSIER (une partie) et de SOUS-DOSSIER — chacun avec son
    /// style, que Word garde sous son nom. Ici : ce qui précède chaque écrit,
    /// et les quatre styles.</summary>
    public static class ExportLevels
    {
        public const string StyleType = "export-level-type", StyleBook = "export-level-book",
            StyleFolder = "export-level-folder", StyleSubfolder = "export-level-subfolder";

        public const string TitleTexts = "Écrits", TitleSheets = "Fiches", TitlePlans = "Plans", TitleMindMaps = "Cartes mentales";

        public static bool IsLevelStyle(string styleId)
        {
            return styleId != null && styleId.StartsWith("export-level-", StringComparison.Ordinal);
        }

        /// <summary>Les contenants d'un écrit que le préréglage titre, du plus
        /// large au plus proche : livres, dossiers (le premier sous un livre
        /// ou sous la racine), sous-dossiers (les suivants).</summary>
        public static List<BinderItem> Chain(BinderItem text, ExportPreset preset)
        {
            var all = new List<BinderItem>();
            for (var parent = text == null ? null : text.Parent; parent != null && parent.Kind != ItemKind.Category; parent = parent.Parent)
                if (parent.Kind == ItemKind.Book || parent.Kind == ItemKind.Folder) all.Insert(0, parent);
            var chain = new List<BinderItem>();
            var depth = 0; // le rang du dossier depuis le livre (ou la racine)
            foreach (var item in all)
            {
                if (item.Kind == ItemKind.Book)
                {
                    depth = 0;
                    if (preset.SplitBooks) chain.Add(item);
                    continue;
                }
                if (depth == 0 ? preset.SplitFolders : preset.SplitSubfolders) chain.Add(item);
                depth++;
            }
            return chain;
        }

        private static string StyleOf(BinderItem item, List<BinderItem> chain, int index)
        {
            if (item.Kind == ItemKind.Book) return StyleBook;
            // Un dossier est « sous-dossier » dès qu'un autre dossier le précède depuis son livre.
            for (var i = index - 1; i >= 0; i--)
            {
                if (chain[i].Kind == ItemKind.Book) break;
                return StyleSubfolder;
            }
            return IsNested(item) ? StyleSubfolder : StyleFolder;
        }

        /// <summary>Vrai si un dossier en contient déjà un autre au-dessus de lui (jusqu'à son livre).</summary>
        private static bool IsNested(BinderItem folder)
        {
            for (var parent = folder.Parent; parent != null && parent.Kind != ItemKind.Category; parent = parent.Parent)
            {
                if (parent.Kind == ItemKind.Book) return false;
                if (parent.Kind == ItemKind.Folder) return true;
            }
            return false;
        }

        /// <summary>Pour chaque écrit, dans l'ordre : les titres de niveau à
        /// poser AVANT lui (rien si son livre et ses dossiers sont ceux de
        /// l'écrit d'avant), et son propre rang dans les signets.
        /// leadTitle (non null) : le titre de genre qui coiffe le tout.</summary>
        public static List<List<ExportLevelTitle>> Before(IList<BinderItem> texts, ExportPreset preset, string leadTitle, out int[] ranks)
        {
            var result = new List<List<ExportLevelTitle>>();
            ranks = new int[texts.Count];
            var offset = leadTitle != null ? 1 : 0;
            var last = new List<BinderItem>();
            for (var i = 0; i < texts.Count; i++)
            {
                var titles = new List<ExportLevelTitle>();
                if (i == 0 && leadTitle != null) titles.Add(new ExportLevelTitle { Title = leadTitle, StyleId = StyleType, Level = 0 });
                var chain = Chain(texts[i], preset);
                var common = 0;
                while (common < chain.Count && common < last.Count && ReferenceEquals(chain[common], last[common])) common++;
                for (var k = common; k < chain.Count; k++)
                    titles.Add(new ExportLevelTitle
                    {
                        Title = chain[k].Kind == ItemKind.Book ? BookInfo.TitleOf(chain[k]) : (chain[k].Title ?? ""),
                        StyleId = StyleOf(chain[k], chain, k),
                        Level = offset + k
                    });
                ranks[i] = offset + chain.Count;
                last = chain;
                result.Add(titles);
            }
            return result;
        }

        /// <summary>La feuille de styles avec les quatre styles de niveau,
        /// dérivés des titres du projet. Rend une copie ; la feuille telle
        /// quelle si elle les porte déjà.</summary>
        public static StyleSheet WithStyles(StyleSheet source)
        {
            if (source == null) return null;
            foreach (var style in source.Styles) if (style.Id == StyleType) return source;
            var styles = source.Clone();
            var body = styles.Body ?? new ParagraphStyle { Id = "body" };
            var title1 = styles.Find("title1") ?? body;
            var title2 = styles.Find("title2") ?? body;
            styles.Styles.Add(Derive(title1, StyleType, "Export — titre de genre", 1.0, true));
            styles.Styles.Add(Derive(title1, StyleBook, "Export — titre de livre", 1.0, true));
            styles.Styles.Add(Derive(title1, StyleFolder, "Export — titre de partie", 0.85, true));
            styles.Styles.Add(Derive(title2, StyleSubfolder, "Export — titre de sous-partie", 1.0, true));
            return styles;
        }

        private static ParagraphStyle Derive(ParagraphStyle source, string id, string name, double scale, bool centered)
        {
            var style = source.Clone();
            style.Id = id;
            style.Name = name;
            style.Scope = ParagraphStyle.ScopeGlobal;
            style.OwnerId = null;
            style.Shortcut = null;
            style.Content = null;
            style.FirstLineIndent = 0;
            if (centered) style.Align = "center";
            if (Math.Abs(scale - 1) > 0.001)
            {
                style.FontSize = Math.Round(style.FontSize * scale * 100) / 100;
                if (style.LineHeight > 1) style.LineHeight = Math.Round(style.LineHeight * scale * 100) / 100;
            }
            style.KeepNextLines = Math.Max(1, style.KeepNextLines);
            return style;
        }

        public static TextParagraph Paragraph(ExportLevelTitle title)
        {
            var paragraph = new TextParagraph { StyleId = title.StyleId };
            paragraph.Runs.Add(new TextRun { Text = title.Title });
            return paragraph;
        }
    }

    /// <summary>« RETIRER LES PLEINES PAGES BLANCHES » (20/11/2026, Rémi) :
    /// dans un flux prêt à composer, les pages qui sortiraient toutes
    /// blanches tombent, et celles qui ne porteraient qu'un titre de partie
    /// rejoignent la page suivante (le titre reste, la page s'en va). Une
    /// « page » est ici ce qui tient entre deux sauts de page voulus.</summary>
    public static class ExportBlankPages
    {
        /// <summary>Vrai si le style est un titre : ceux de la maison (title1,
        /// title2…), ceux de niveau de l'export, ou un style nommé « Titre… ».</summary>
        public static bool IsTitleStyle(StyleSheet styles, string styleId)
        {
            if (styleId == null) return false;
            if (styleId.StartsWith("title", StringComparison.Ordinal) || ExportLevels.IsLevelStyle(styleId) || styleId == ExportFlow.StyleSection) return true;
            if (styles == null) return false;
            foreach (var style in styles.Styles)
                if (style.Id == styleId) return (style.Name ?? "").TrimStart().StartsWith("Titre", StringComparison.CurrentCultureIgnoreCase);
            return false;
        }

        public static bool IsBlank(TextParagraph paragraph)
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.ImageId != null || run.IsRule || run.FootnoteId != null) return false;
                if (run.IsLineBreak) continue;
                if (!string.IsNullOrWhiteSpace(run.Text)) return false;
            }
            return true;
        }

        /// <summary>Retire les pages blanches du segment (son document, ses
        /// repères et ses liens suivent). keepHead : les premiers paragraphes
        /// à ne pas toucher (la page de titre). Rend le nombre de pages ôtées.</summary>
        public static int Apply(ExportSegment segment, int keepHead)
        {
            if (segment == null || segment.Document == null || segment.IsImage) return 0;
            var source = segment.Document.Paragraphs;
            if (source.Count == 0) return 0;

            // Les pages : chaque saut de page voulu en ouvre une.
            var starts = new List<int> { 0 };
            for (var i = 1; i < source.Count; i++) if (source[i].PageBreakBefore) starts.Add(i);

            var keep = new bool[source.Count];
            var unbreak = new bool[source.Count]; // ce paragraphe perd son saut de page
            for (var i = 0; i < keep.Length; i++) keep[i] = true;
            var removed = 0;
            var lastKept = -1; // la dernière page gardée
            var pending = false; // la page gardée d'avant ne porte que des titres : la suivante la rejoint
            for (var g = 0; g < starts.Count; g++)
            {
                var from = starts[g];
                var to = g + 1 < starts.Count ? starts[g + 1] : source.Count;
                var isHead = from < keepHead;
                bool blank = true, titles = true;
                for (var i = from; i < to; i++)
                {
                    if (IsBlank(source[i])) continue;
                    blank = false;
                    if (!IsTitleStyle(segment.Styles, source[i].StyleId)) titles = false;
                }
                if (blank && !isHead && starts.Count > 1)
                {
                    for (var i = from; i < to; i++) keep[i] = false;
                    removed++;
                    continue;
                }
                if (pending) { unbreak[from] = true; removed++; }
                pending = titles && !blank && !isHead;
                lastKept = g;
            }
            if (removed == 0) return 0;

            var map = new int[source.Count + 1];
            var output = new List<TextParagraph>();
            for (var i = 0; i < source.Count; i++)
            {
                map[i] = output.Count;
                if (!keep[i]) continue;
                var paragraph = source[i];
                // Le premier paragraphe gardé n'a plus de saut à faire ; celui qui rejoint un titre non plus.
                if ((unbreak[i] || output.Count == 0) && (paragraph.PageBreakBefore || paragraph.StartOnRecto))
                {
                    var shell = PivotEdit.CloneParagraphShell(paragraph);
                    shell.PageBreakBefore = false;
                    shell.StartOnRecto = false;
                    shell.Runs.AddRange(paragraph.Runs);
                    paragraph = shell;
                }
                output.Add(paragraph);
            }
            map[source.Count] = output.Count;
            if (output.Count == 0) output.Add(new TextParagraph());

            var document = new TextDocument { Footnotes = segment.Document.Footnotes, Annotations = segment.Document.Annotations, LineSpacing = segment.Document.LineSpacing };
            document.Paragraphs.AddRange(output);
            segment.Document = document;
            foreach (var mark in segment.Marks)
                if (mark.Paragraph >= 0) mark.Paragraph = Math.Min(output.Count - 1, map[Math.Min(mark.Paragraph, source.Count)]);
            for (var i = segment.Links.Count - 1; i >= 0; i--)
            {
                var link = segment.Links[i];
                if (link.Paragraph < 0 || link.Paragraph >= source.Count || !keep[link.Paragraph]) { segment.Links.RemoveAt(i); continue; }
                link.Paragraph = map[link.Paragraph];
            }
            return removed;
        }
    }

    /// <summary>Un caractère qu'une police n'a pas : la police, le caractère,
    /// combien de fois, et le premier écrit où il paraît.</summary>
    public class MissingGlyph
    {
        public string Family = "";
        public char Character;
        public int Count;
        public string FirstTitle = "";
    }

    /// <summary>L'ALERTE DE CARACTÈRE ABSENT (20/11/2026) : avant un PDF, les
    /// caractères du texte que leur police ne sait pas dessiner — le PDF les
    /// remplacerait en silence par ceux d'une autre police, ou par un carré.</summary>
    public static class ExportGlyphs
    {
        /// <summary>Les caractères absents d'un écrit, police par police.</summary>
        public static List<MissingGlyph> Missing(BinderItem text, StyleSheet styles, IGlyphMetrics metrics)
        {
            var found = new List<MissingGlyph>();
            if (text == null || text.Document == null || styles == null || metrics == null) return found;
            var known = new Dictionary<string, bool>();
            foreach (var paragraph in text.Document.Paragraphs)
                Scan(paragraph.Runs, styles.Find(paragraph.StyleId), text, metrics, known, found);
            var note = styles.FootnoteStyle();
            foreach (var footnote in text.Document.Footnotes)
                if (footnote.Runs != null) Scan(footnote.Runs, note, text, metrics, known, found);
            return found;
        }

        private static void Scan(IEnumerable<TextRun> runs, ParagraphStyle style, BinderItem text, IGlyphMetrics metrics, Dictionary<string, bool> known, List<MissingGlyph> found)
        {
            if (style == null) return;
            foreach (var run in runs)
            {
                if (PivotEdit.IsElement(run) || string.IsNullOrEmpty(run.Text)) continue;
                var family = string.IsNullOrEmpty(run.FontFamily) ? style.FontFamily : run.FontFamily;
                if (string.IsNullOrEmpty(family)) continue;
                var weight = !string.IsNullOrEmpty(run.Weight) ? TextWeights.Parse(run.Weight)
                    : run.Bold == true ? TextWeights.Bold : run.Bold == false ? TextWeights.Normal : style.EffectiveWeight();
                var italic = run.Italic ?? style.Italic;
                var prefix = family.ToLowerInvariant() + "|" + weight + "|" + (italic ? "i" : "r") + "|";
                foreach (var c in run.Text)
                {
                    if (!Visible(c)) continue;
                    bool has;
                    var key = prefix + c;
                    if (!known.TryGetValue(key, out has))
                    {
                        try { has = metrics.HasGlyph(family, weight, italic, c); }
                        catch { has = true; }
                        known[key] = has;
                    }
                    if (has) continue;
                    MissingGlyph entry = null;
                    foreach (var have in found)
                        if (have.Character == c && string.Equals(have.Family, family, StringComparison.OrdinalIgnoreCase)) { entry = have; break; }
                    if (entry == null) found.Add(entry = new MissingGlyph { Family = family, Character = c, FirstTitle = text.Title ?? "" });
                    entry.Count++;
                }
            }
        }

        /// <summary>Un caractère qui se dessine : ni blanc, ni contrôle, ni
        /// marque invisible, ni moitié de paire (le compositeur les traite à part).</summary>
        private static bool Visible(char c)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c)) return false;
            if (c == '­' || c == '￼' || c == '﻿') return false;
            var category = char.GetUnicodeCategory(c);
            return category != UnicodeCategory.Format && category != UnicodeCategory.NonSpacingMark && category != UnicodeCategory.PrivateUse;
        }

        /// <summary>Réunit les relevés de plusieurs écrits et en fait les
        /// remarques du plan : une par police.</summary>
        public static List<ExportWarning> Warnings(IEnumerable<List<MissingGlyph>> perText)
        {
            var families = new List<string>();
            var byFamily = new Dictionary<string, List<MissingGlyph>>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in perText)
            {
                if (list == null) continue;
                foreach (var glyph in list)
                {
                    List<MissingGlyph> merged;
                    if (!byFamily.TryGetValue(glyph.Family, out merged)) { byFamily[glyph.Family] = merged = new List<MissingGlyph>(); families.Add(glyph.Family); }
                    MissingGlyph same = null;
                    foreach (var have in merged) if (have.Character == glyph.Character) { same = have; break; }
                    if (same == null) merged.Add(new MissingGlyph { Family = glyph.Family, Character = glyph.Character, Count = glyph.Count, FirstTitle = glyph.FirstTitle });
                    else same.Count += glyph.Count;
                }
            }
            var warnings = new List<ExportWarning>();
            foreach (var family in families)
            {
                var glyphs = byFamily[family];
                var shown = new List<string>();
                var total = 0;
                for (var i = 0; i < glyphs.Count; i++)
                {
                    total += glyphs[i].Count;
                    if (i < 8) shown.Add("« " + glyphs[i].Character + " » (U+" + ((int)glyphs[i].Character).ToString("X4", CultureInfo.InvariantCulture) + ")");
                }
                var more = glyphs.Count > 8 ? " et " + (glyphs.Count - 8) + " de plus" : "";
                warnings.Add(new ExportWarning
                {
                    Level = ExportWarning.Warn,
                    Text = "La police « " + family + " » n'a pas " + (glyphs.Count > 1 ? "ces caractères" : "ce caractère") + " : " + string.Join(", ", shown.ToArray()) + more + ".",
                    Detail = total + (total > 1 ? " occurrences" : " occurrence") + ", d'abord dans « " + glyphs[0].FirstTitle + " ». Dans le PDF, une autre police les dessinera — ou un carré vide. Changez de police pour ces passages, ou remplacez le caractère."
                });
            }
            return warnings;
        }
    }
}
