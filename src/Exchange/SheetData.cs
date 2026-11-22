using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>LES FICHES EN DONNÉES (21/11) : le même choix de fiches que
    /// l'Encyclopédie, sorti sans mise en forme — en MARKDOWN (un fichier
    /// par fiche, rangé par catégorie, les [[liens]] gardés tels quels :
    /// Obsidian et les wikis les suivent) ou en CSV (un tableau par
    /// catégorie d'ensemble, une ligne par fiche, une colonne par champ ;
    /// point-virgule et UTF-8 à BOM, ce qu'attend un tableur français).
    /// Rien n'est décodé ni dessiné : ni portrait, ni radar, ni carte.</summary>
    public static class SheetData
    {
        public const string MarkdownIndex = "index.md";

        /// <summary>Les options de l'Encyclopédie, ramenées à ce que des
        /// données emportent : champs, relations et tags toujours ; ni
        /// lexique, ni cartes, ni présences.</summary>
        public static EncyclopediaOptions DataOptions(EncyclopediaOptions chosen)
        {
            var options = (chosen ?? new EncyclopediaOptions()).Clone();
            options.ShowInfobox = true;
            options.ShowRelations = true;
            options.ShowTags = true;
            options.ShowPresence = false;
            options.ShowRadar = false;
            options.IncludeLexicon = false;
            options.IncludeMaps = false;
            options.CoverImageId = null;
            return options;
        }

        private static string SectionFolder(EncyclopediaSection section)
        {
            if (section == null) return "";
            return section.Parent == null ? section.Slug : Path.Combine(section.Parent.Slug, section.Slug);
        }

        // ------------------------------------------------------------ Markdown

        /// <summary>Une fiche en Markdown : le titre, la catégorie et les tags,
        /// les champs par groupe, les relations, puis le texte de la fiche.</summary>
        public static string Markdown(EncyclopediaSheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(sheet.Title).Append("\n\n");
            var where = sheet.Section == null ? "" : sheet.Section.Parent == null ? sheet.Section.Title : sheet.Section.Parent.Title + " › " + sheet.Section.Title;
            if (where.Length > 0) sb.Append("*").Append(where).Append("*\n\n");
            if (sheet.Tags.Count > 0)
            {
                var tags = new List<string>();
                foreach (var tag in sheet.Tags) tags.Add("#" + tag.Trim().Replace(' ', '-'));
                sb.Append(string.Join(" ", tags.ToArray())).Append("\n\n");
            }
            foreach (var group in sheet.Groups)
            {
                if (group.Fields.Count == 0) continue;
                if (!string.IsNullOrWhiteSpace(group.Title)) sb.Append("## ").Append(group.Title.Trim()).Append("\n\n");
                foreach (var field in group.Fields)
                {
                    var value = field.Items.Count > 0 ? string.Join(", ", field.Items.ToArray()) : field.Value ?? "";
                    if (field.Kind == FieldKinds.Sheet && value.Length > 0) value = "[[" + value + "]]";
                    sb.Append("- **").Append(field.Label).Append("** : ").Append(OneLine(value)).Append("\n");
                }
                sb.Append("\n");
            }
            if (sheet.Relations.Count > 0)
            {
                sb.Append("## Relations\n\n");
                foreach (var relation in sheet.Relations)
                    sb.Append("- ").Append(string.IsNullOrEmpty(relation.Kind) ? "" : relation.Kind + " : ").Append("[[").Append(relation.Label).Append("]]\n");
                sb.Append("\n");
            }
            var body = (sheet.BodyMarkdown ?? "").Replace("\r\n", "\n").Trim();
            if (body.Length > 0) sb.Append(body).Append("\n");
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        private static string OneLine(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\n", " ⏎ ").Trim();
        }

        /// <summary>Écrit « index.md » et un fichier par fiche, dans un dossier
        /// par catégorie. Rend les chemins écrits.</summary>
        public static List<string> WriteMarkdown(string folder, EncyclopediaDocument document)
        {
            var written = new List<string>();
            Directory.CreateDirectory(folder);
            var utf8 = new UTF8Encoding(false);
            var index = new StringBuilder();
            index.Append("# ").Append(document.Title).Append("\n\n");
            foreach (var section in document.Sections) IndexSection(index, section, 2);
            var indexPath = Path.Combine(folder, MarkdownIndex);
            File.WriteAllText(indexPath, index.ToString(), utf8);
            written.Add(indexPath);
            foreach (var sheet in document.AllSheets())
            {
                var dir = Path.Combine(folder, SectionFolder(sheet.Section));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, sheet.Slug + ".md");
                File.WriteAllText(path, Markdown(sheet), utf8);
                written.Add(path);
            }
            return written;
        }

        private static void IndexSection(StringBuilder index, EncyclopediaSection section, int level)
        {
            if (section.Count == 0) return;
            index.Append(new string('#', Math.Min(level, 6))).Append(' ').Append(section.Title).Append("\n\n");
            foreach (var sheet in section.Sheets)
                index.Append("- [").Append(sheet.Title).Append("](").Append((SectionFolder(section).Replace('\\', '/') + "/" + sheet.Slug + ".md").TrimStart('/')).Append(")\n");
            if (section.Sheets.Count > 0) index.Append("\n");
            foreach (var sub in section.Subsections) IndexSection(index, sub, level + 1);
        }

        // ----------------------------------------------------------------- CSV

        /// <summary>Une cellule CSV : entre guillemets dès qu'elle porte un
        /// séparateur, un guillemet ou un saut de ligne.</summary>
        public static string Cell(string value)
        {
            var text = (value ?? "").Replace("\r\n", "\n");
            if (text.IndexOfAny(new[] { ';', '"', '\n', ',' }) < 0 && text == text.Trim()) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Le tableau d'une catégorie d'ensemble : Titre,
        /// Sous-catégorie, Tags, puis une colonne par champ rencontré (dans
        /// l'ordre des fiches), et Relations.</summary>
        public static string Csv(EncyclopediaSection section)
        {
            var sheets = new List<EncyclopediaSheet>();
            Gather(section, sheets);
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sheet in sheets)
                foreach (var group in sheet.Groups)
                    foreach (var field in group.Fields)
                        if (seen.Add(field.Label ?? "")) columns.Add(field.Label ?? "");
            var sb = new StringBuilder();
            var head = new List<string> { "Titre", "Sous-catégorie", "Tags" };
            head.AddRange(columns);
            head.Add("Relations");
            Line(sb, head);
            foreach (var sheet in sheets)
            {
                var row = new List<string>();
                row.Add(sheet.Title);
                row.Add(sheet.Section != null && sheet.Section.Parent != null ? sheet.Section.Title : "");
                row.Add(string.Join(", ", sheet.Tags.ToArray()));
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var group in sheet.Groups)
                    foreach (var field in group.Fields)
                    {
                        var value = field.Items.Count > 0 ? string.Join(", ", field.Items.ToArray()) : Links.Strip(field.Value ?? "");
                        string already;
                        values[field.Label ?? ""] = values.TryGetValue(field.Label ?? "", out already) && already.Length > 0 ? already + " / " + value : value;
                    }
                foreach (var column in columns) { string value; row.Add(values.TryGetValue(column, out value) ? value : ""); }
                var relations = new List<string>();
                foreach (var relation in sheet.Relations) relations.Add((string.IsNullOrEmpty(relation.Kind) ? "" : relation.Kind + " : ") + relation.Label);
                row.Add(string.Join(", ", relations.ToArray()));
                Line(sb, row);
            }
            return sb.ToString();
        }

        private static void Gather(EncyclopediaSection section, List<EncyclopediaSheet> sheets)
        {
            sheets.AddRange(section.Sheets);
            foreach (var sub in section.Subsections) Gather(sub, sheets);
        }

        private static void Line(StringBuilder sb, List<string> cells)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(Cell(cells[i]));
            }
            sb.Append("\r\n");
        }

        /// <summary>Écrit un « <catégorie>.csv » par catégorie d'ensemble qui a des fiches.</summary>
        public static List<string> WriteCsv(string folder, EncyclopediaDocument document)
        {
            var written = new List<string>();
            Directory.CreateDirectory(folder);
            var utf8 = new UTF8Encoding(true); // le BOM : sans lui, Excel lit les accents de travers
            foreach (var section in document.Sections)
            {
                if (section.Count == 0) continue;
                var path = Path.Combine(folder, section.Slug + ".csv");
                File.WriteAllText(path, Csv(section), utf8);
                written.Add(path);
            }
            return written;
        }
    }
}
