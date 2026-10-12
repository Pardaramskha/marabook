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
        public bool CommonWord;       // un mot du dictionnaire employé comme nom (« le Cairn ») — à juger
    }

    public static class MissingNames
    {
        /// <summary>Le relevé sur ces écrits (null = tous les écrits du projet),
        /// les plus fréquents d'abord. inLexicon : le mot est-il une entrée
        /// (ou une forme) du dictionnaire personnel, projet ou global ?
        /// « Fiche éponyme » regarde le TITRE des fiches mais aussi leurs
        /// nom, prénom, alias et chaque composant de ces noms (Presence.NamesOf)
        /// — « Keira » est servie par la fiche « Keira Varenh ». Un nom qui a
        /// déjà sa fiche ET son entrée au dictionnaire n'est plus proposé.</summary>
        public static List<MissingName> Collect(Project project, List<BinderItem> texts, Func<string, bool> inLexicon)
        {
            return Collect(project, texts, inLexicon, null);
        }

        /// <summary>Avec isCommonWord (1.0.5-a) : le mot est-il connu du
        /// dictionnaire en minuscules ? Les noms communs promus (« le Cairn »)
        /// restent et portent CommonWord ; les majuscules de titre après une
        /// virgule (« Nom, Discours public ») tombent — voir NameExtractor.</summary>
        public static List<MissingName> Collect(Project project, List<BinderItem> texts, Func<string, bool> inLexicon, Func<string, bool> isCommonWord)
        {
            var result = new List<MissingName>();
            if (project == null) return result;
            if (texts == null) texts = project.TextsIn(null);
            var known = KnownSheetNameKeys(project);
            var sb = new StringBuilder();
            var plain = new List<KeyValuePair<BinderItem, string>>();
            foreach (var text in texts)
            {
                if (text == null || text.Document == null || text.IsDescendantOf(project.Trash)) continue;
                var content = text.Document.ToPlainText();
                plain.Add(new KeyValuePair<BinderItem, string>(text, content));
                sb.AppendLine(content);
            }
            foreach (var candidate in NameExtractor.Extract(sb.ToString(), null, NameExtractor.DefaultMinCount, isCommonWord))
            {
                var entry = new MissingName { Name = candidate.Name, Count = candidate.Count, Context = candidate.Context, CommonWord = candidate.CommonWord };
                foreach (var pair in plain)
                    if (pair.Value.IndexOf(candidate.Name, StringComparison.Ordinal) >= 0) { entry.Where = pair.Key.Title ?? ""; break; }
                entry.HasSheet = known.Contains(NameExtractor.Key(candidate.Name));
                entry.InLexicon = inLexicon != null && inLexicon(candidate.Name);
                if (entry.HasSheet && entry.InLexicon) continue; // tout est déjà enregistré
                result.Add(entry);
            }
            return result;
        }

        /// <summary>Les clés (NameExtractor.Key) de tous les noms que les fiches
        /// portent — titre, Nom, Prénom, Alias — et de chacun de leurs
        /// composants (« Keira Varenh » sert « Keira » et « Varenh »).</summary>
        public static HashSet<string> KnownSheetNameKeys(Project project)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Kind != ItemKind.Sheet || sheet.IsDescendantOf(project.Trash)) continue;
                var names = Presence.NamesOf(sheet, project.TemplateOf(sheet));
                if (!names.Contains(sheet.Title ?? "")) names.Add(sheet.Title ?? "");
                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    keys.Add(NameExtractor.Key(name));
                    foreach (var part in name.Split(' ', '\t', '-'))
                        if (part.Trim().Length > 1) keys.Add(NameExtractor.Key(part.Trim()));
                }
            }
            return keys;
        }
    }
}
