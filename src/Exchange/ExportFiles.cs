using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Un fichier d'un export « un fichier par écrit » : son nom
    /// (sans dossier, avec extension) et le plan qui ne porte que lui.</summary>
    public class ExportFile
    {
        public string Name = "";
        public BinderItem Item;
        public ExportPlan Plan;
        public ExportMeta Meta;
    }

    /// <summary>« Un fichier par écrit » (20/11) : au lieu d'un seul fichier,
    /// l'export en écrit un par élément retenu, nommés d'après un modèle —
    /// « {n} - {titre} » donne « 01 - Prologue.docx ».</summary>
    public static class ExportFiles
    {
        public const string DefaultPattern = "{n} - {titre}";

        /// <summary>Les jetons du modèle, et ce qu'ils valent.</summary>
        public static readonly string[][] Tokens =
        {
            new[] { "{n}", "le numéro d'ordre (01, 02…)" },
            new[] { "{titre}", "le titre de l'élément" },
            new[] { "{livre}", "le titre de son livre" },
            new[] { "{dossier}", "le dossier ou la partie qui le range" },
            new[] { "{statut}", "son état d'avancement" },
            new[] { "{auteur}", "l'auteur·ice" },
            new[] { "{projet}", "le nom du projet" },
            new[] { "{date}", "la date du jour (2026-11-20)" }
        };

        public static string Legend()
        {
            var sb = new StringBuilder();
            foreach (var token in Tokens)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(token[0]).Append(" : ").Append(token[1]);
            }
            return sb.ToString();
        }

        /// <summary>Le modèle tel qu'il servira : vide, il redevient celui de
        /// la maison ; sans {n} ni {titre}, deux fichiers porteraient le même
        /// nom — le numéro revient en tête.</summary>
        public static string Normalize(string pattern)
        {
            pattern = (pattern ?? "").Trim();
            if (pattern.Length == 0) return DefaultPattern;
            if (pattern.IndexOf("{n}", StringComparison.OrdinalIgnoreCase) < 0 && pattern.IndexOf("{titre}", StringComparison.OrdinalIgnoreCase) < 0)
                return "{n} - " + pattern;
            return pattern;
        }

        /// <summary>Un nom que tous les systèmes acceptent : ni séparateur de
        /// chemin, ni caractère réservé, ni point ou espace en bout.</summary>
        public static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name ?? "")
            {
                if (c < ' ' || "<>:\"/\\|?*".IndexOf(c) >= 0) sb.Append(c == ':' || c == '/' || c == '\\' || c == '|' ? '-' : '_');
                else sb.Append(c);
            }
            var text = sb.ToString();
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0) text = text.Replace("  ", " ");
            text = text.Trim().TrimEnd('.', ' ').TrimStart('-', ' ');
            if (text.Length > 120) text = text.Substring(0, 120).TrimEnd('.', ' ');
            return text.Length == 0 ? "document" : text;
        }

        /// <summary>Le nom (sans extension) de cet élément, le index-ième
        /// (dès 1) de total.</summary>
        public static string NameOf(string pattern, BinderItem item, int index, int total, Project project, ExportMeta meta, DateTime today)
        {
            var digits = Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length);
            var book = item == null ? null : item.EnclosingBook();
            var parent = item == null ? null : item.Parent;
            var folder = parent != null && parent.Kind != ItemKind.Category && !ReferenceEquals(parent, book) ? parent.Title : "";
            var author = meta != null && !string.IsNullOrEmpty((meta.Author ?? "").Trim()) ? meta.Author.Trim() : project == null ? "" : project.Author ?? "";
            var name = Normalize(pattern);
            name = Replace(name, "{n}", index.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0'));
            name = Replace(name, "{titre}", item == null ? "" : item.Title ?? "");
            name = Replace(name, "{livre}", book == null ? "" : BookInfo.TitleOf(book));
            name = Replace(name, "{dossier}", folder ?? "");
            name = Replace(name, "{statut}", item == null || item.Kind != ItemKind.Text ? "" : TextStatus.Label(item.Status));
            name = Replace(name, "{auteur}", author);
            name = Replace(name, "{projet}", project == null ? "" : project.Name ?? "");
            name = Replace(name, "{date}", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            // Un jeton resté vide ne laisse pas de « - - » derrière lui.
            while (name.IndexOf("  ", StringComparison.Ordinal) >= 0) name = name.Replace("  ", " ");
            while (name.IndexOf("- -", StringComparison.Ordinal) >= 0) name = name.Replace("- -", "-");
            name = name.Replace("()", "").Replace("[]", "");
            return Safe(name);
        }

        private static string Replace(string text, string token, string value)
        {
            var at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                text = text.Substring(0, at) + value + text.Substring(at + token.Length);
                at = text.IndexOf(token, at + value.Length, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        /// <summary>Vrai quand ce préréglage, sur ce plan, écrit plusieurs fichiers.</summary>
        public static bool Applies(ExportPreset preset, ExportPlan plan)
        {
            return preset != null && preset.OneFilePerText && plan != null && plan.Count > 1;
        }

        /// <summary>Les éléments du plan dans l'ordre où l'export les aurait
        /// enchaînés : écrits, fiches, plans, cartes mentales, cartes.</summary>
        public static List<BinderItem> ItemsOf(ExportPlan plan)
        {
            var items = new List<BinderItem>();
            if (plan == null) return items;
            items.AddRange(plan.Texts);
            items.AddRange(plan.Sheets);
            items.AddRange(plan.Plans);
            items.AddRange(plan.MindMaps);
            items.AddRange(plan.Maps);
            return items;
        }

        /// <summary>Les fichiers à écrire : un par élément, chacun avec son
        /// plan. Deux noms égaux (à la casse près) se départagent par « (2) ».</summary>
        public static List<ExportFile> Split(Project project, ExportPlan plan, ExportPreset preset, ExportMeta meta, DateTime today)
        {
            var files = new List<ExportFile>();
            if (project == null || plan == null || preset == null) return files;
            var items = ItemsOf(plan);
            var extension = ExportFormats.Extension(preset.Format);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Chaque fichier ne porte qu'un élément : le filtre a déjà parlé, il ne rejoue pas.
            var single = preset.Clone();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var stem = NameOf(preset.FileNamePattern, item, i + 1, items.Count, project, meta, today);
                var name = stem + extension;
                for (var n = 2; !taken.Add(name); n++) name = stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                var own = meta == null ? new ExportMeta() : meta.Clone();
                // Le titre du fichier est celui de l'élément : c'est lui qu'une liseuse affichera.
                own.Title = item.Title ?? "";
                own.Subtitle = "";
                files.Add(new ExportFile { Name = name, Item = item, Meta = own, Plan = ExportPlan.Build(project, new[] { item.Id }, single, own) });
            }
            return files;
        }

        /// <summary>Ceux de ces fichiers qui existent déjà dans ce dossier.</summary>
        public static List<string> Existing(string folder, IEnumerable<ExportFile> files)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(folder) || files == null) return found;
            foreach (var file in files)
                if (File.Exists(Path.Combine(folder, file.Name))) found.Add(file.Name);
            return found;
        }

        /// <summary>« 01 - Prologue.docx, 02 - La mer.docx… » pour l'exemple du gestionnaire.</summary>
        public static string Sample(Project project, ExportPlan plan, ExportPreset preset, ExportMeta meta, DateTime today, int max)
        {
            var items = ItemsOf(plan);
            if (items.Count == 0) return "";
            var names = new List<string>();
            var extension = ExportFormats.Extension(preset.Format);
            for (var i = 0; i < items.Count && i < max; i++)
                names.Add(NameOf(preset.FileNamePattern, items[i], i + 1, items.Count, project, meta, today) + extension);
            var text = string.Join(", ", names.ToArray());
            return items.Count > max ? text + "…" : text;
        }
    }
}
