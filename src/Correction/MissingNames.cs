using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>LES FICHES MANQUANTES (1.0.5-a, Rémi) : les noms propres
    /// récurrents d'un spectre d'écrits (NameExtractor), avec le premier
    /// endroit, et ce que le projet sait déjà d'eux — une fiche éponyme, une
    /// entrée du dictionnaire personnel — pour créer les fiches en lot et
    /// voir d'un coup d'œil ce qui est déjà enregistré ailleurs.</summary>
    public sealed class MissingName
    {
        public string Name = "";
        public int Count;
        public string Where = "";     // le premier écrit qui le porte
        public string Context = "";   // un bout de phrase (NameExtractor)
        public bool HasSheet;         // une fiche porte déjà ce titre
        public bool InLexicon;        // le dictionnaire personnel le connaît
    }

    public static class MissingNames
    {
        /// <summary>Le relevé sur ces écrits (null = tous les écrits du projet),
        /// les plus fréquents d'abord. inLexicon : le mot est-il une entrée
        /// (ou une forme) du dictionnaire personnel, projet ou global ?</summary>
        public static List<MissingName> Collect(Project project, List<BinderItem> texts, Func<string, bool> inLexicon)
        {
            var result = new List<MissingName>();
            if (project == null) return result;
            if (texts == null) texts = project.TextsIn(null);
            var sb = new StringBuilder();
            var plain = new List<KeyValuePair<BinderItem, string>>();
            foreach (var text in texts)
            {
                if (text == null || text.Document == null || text.IsDescendantOf(project.Trash)) continue;
                var content = text.Document.ToPlainText();
                plain.Add(new KeyValuePair<BinderItem, string>(text, content));
                sb.AppendLine(content);
            }
            foreach (var candidate in NameExtractor.Extract(sb.ToString(), null))
            {
                var entry = new MissingName { Name = candidate.Name, Count = candidate.Count, Context = candidate.Context };
                foreach (var pair in plain)
                    if (pair.Value.IndexOf(candidate.Name, StringComparison.Ordinal) >= 0) { entry.Where = pair.Key.Title ?? ""; break; }
                var sheet = project.FindByTitle(candidate.Name);
                entry.HasSheet = sheet != null && sheet.Kind == ItemKind.Sheet;
                entry.InLexicon = inLexicon != null && inLexicon(candidate.Name);
                result.Add(entry);
            }
            return result;
        }
    }
}
