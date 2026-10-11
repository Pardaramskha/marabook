using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'inventaire des polices d'un projet (07/10) : les familles
    /// que les styles, les passages mis en forme et les notes demandent, et
    /// celles qui MANQUENT à l'installation locale — un .plot écrit sous
    /// Windows avec Garamond Premier s'ouvre sur un Mac qui ne l'a pas, et
    /// le texte se composait en silence dans une police de repli. La barre
    /// d'état le signale, et un remplacement global par police manquante
    /// (AppSettings.FontSubstitutions) s'applique partout où elle est
    /// demandée, sans toucher au document.</summary>
    public static class FontAudit
    {
        /// <summary>Les familles employées par le projet, distinctes (sans
        /// casse, bords retirés), dans l'ordre de rencontre : les styles
        /// (globaux, de livre, d'écrit), puis les runs et les notes de chaque
        /// écrit et fiche. Jamais de nom vide.</summary>
        public static List<string> UsedFamilies(Project project)
        {
            var families = new List<string>();
            if (project == null) return families;
            if (project.Styles != null)
                foreach (var style in project.Styles.Styles)
                    Add(families, style.FontFamily);
            foreach (var item in project.AllItems())
            {
                if (item.Document == null) continue;
                foreach (var paragraph in item.Document.Paragraphs)
                    foreach (var run in paragraph.Runs)
                        Add(families, run.FontFamily);
                foreach (var note in item.Document.Footnotes)
                    if (note.Runs != null)
                        foreach (var run in note.Runs)
                            Add(families, run.FontFamily);
            }
            return families;
        }

        /// <summary>Les familles employées qui ne sont pas dans la liste des
        /// polices installées (sans casse), dans l'ordre de rencontre.</summary>
        public static List<string> Missing(Project project, IEnumerable<string> installed)
        {
            var missing = new List<string>();
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (installed != null)
                foreach (var name in installed)
                    if (!string.IsNullOrEmpty(name)) known.Add(name.Trim());
            foreach (var family in UsedFamilies(project))
                if (!known.Contains(family)) missing.Add(family);
            return missing;
        }

        /// <summary>Où une famille est demandée (09/10) : les styles qui la
        /// portent (« Style « Corps » »), puis les écrits et fiches dont un
        /// passage ou une note la demande — dans l'ordre de rencontre, sans
        /// doublon.</summary>
        public static List<string> UsersOf(Project project, string family)
        {
            var users = new List<string>();
            if (project == null || string.IsNullOrEmpty(family)) return users;
            if (project.Styles != null)
                foreach (var style in project.Styles.Styles)
                    if (Same(style.FontFamily, family)) AddUser(users, "Style « " + style.Name + " »");
            foreach (var item in project.AllItems())
            {
                if (item.Document == null) continue;
                var found = false;
                foreach (var paragraph in item.Document.Paragraphs)
                {
                    foreach (var run in paragraph.Runs) if (Same(run.FontFamily, family)) { found = true; break; }
                    if (found) break;
                }
                if (!found)
                    foreach (var note in item.Document.Footnotes)
                    {
                        if (note.Runs != null) foreach (var run in note.Runs) if (Same(run.FontFamily, family)) { found = true; break; }
                        if (found) break;
                    }
                if (found) AddUser(users, (item.Kind == ItemKind.Sheet ? "Fiche « " : "Écrit « ") + item.Title + " »");
            }
            return users;
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUser(List<string> users, string label)
        {
            if (!users.Contains(label)) users.Add(label);
        }

        private static void Add(List<string> families, string family)
        {
            if (string.IsNullOrEmpty(family)) return;
            family = family.Trim();
            if (family.Length == 0) return;
            foreach (var known in families)
                if (string.Equals(known, family, StringComparison.OrdinalIgnoreCase)) return;
            families.Add(family);
        }
    }
}
