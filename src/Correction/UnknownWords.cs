using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>LES MOTS INCONNUS DU PROJET (1.0.5, Rémi) : tout ce que
    /// l'orthographe rougirait dans les écrits et les fiches — corps, champs,
    /// notes — compté, avec le premier endroit où ça apparaît, pour apprendre
    /// en lot au lieu de mot à mot. Les néologismes et le familier, qui ont
    /// leur catégorie, n'y sont pas ; la corbeille non plus.</summary>
    public sealed class UnknownWord
    {
        public string Word = "";
        public int Count;
        public string Where = "";       // le titre du premier item qui le porte
        public int Items;               // le nombre d'items distincts
        public string Sample = "";      // un bout de phrase autour de la première occurrence
    }

    public static class UnknownWords
    {
        /// <summary>Le relevé, trié par compte décroissant puis par mot. Le
        /// vérificateur est celui du projet (dictionnaires personnels posés
        /// par l'appelant). Un mot se reconnaît à sa clé pliée : « Riune » et
        /// « riune » sont une seule ligne, la première graphie l'emporte.</summary>
        public static List<UnknownWord> Collect(Project project, SpellChecker checker)
        {
            var byKey = new Dictionary<string, UnknownWord>(StringComparer.Ordinal);
            var itemsByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (project == null || checker == null) return new List<UnknownWord>();
            foreach (var item in project.AllItems())
            {
                if (item.IsCategory || item.IsDescendantOf(project.Trash)) continue;
                if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) continue;
                foreach (var field in item.SearchFields(project))
                {
                    if (field.Kind == SearchField.KindTitle || string.IsNullOrEmpty(field.Text)) continue;
                    if (!FieldKindsSearchable(field)) continue;
                    foreach (var finding in checker.CheckText(field.Text))
                    {
                        if (finding.Category != FindingCategory.Spelling) continue;
                        var key = FrenchTokenizer.Fold(finding.Word);
                        UnknownWord entry;
                        if (!byKey.TryGetValue(key, out entry))
                        {
                            entry = new UnknownWord
                            {
                                Word = finding.Word,
                                Where = item.Title ?? "",
                                Sample = Around(field.Text, finding.Start, finding.Length)
                            };
                            byKey[key] = entry;
                            itemsByKey[key] = new HashSet<string>();
                        }
                        entry.Count++;
                        itemsByKey[key].Add(item.Id);
                    }
                }
            }
            var list = new List<UnknownWord>(byKey.Values);
            foreach (var entry in list) entry.Items = itemsByKey[FrenchTokenizer.Fold(entry.Word)].Count;
            list.Sort(delegate(UnknownWord a, UnknownWord b)
            {
                if (a.Count != b.Count) return b.Count.CompareTo(a.Count);
                return string.Compare(a.Word, b.Word, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static bool FieldKindsSearchable(SearchField field)
        {
            // Les champs de la recherche sont déjà filtrés (une fiche liée est
            // un id) ; tout le reste est du texte d'auteur.
            return field.Kind != SearchField.KindBook && field.Kind != SearchField.KindLexicon;
        }

        /// <summary>Une trentaine de caractères autour du mot, coupés aux
        /// espaces, le mot entre crochets.</summary>
        private static string Around(string text, int start, int length)
        {
            var from = Math.Max(0, start - 28);
            var to = Math.Min(text.Length, start + length + 28);
            while (from > 0 && from < start && !char.IsWhiteSpace(text[from - 1])) from--;
            while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;
            var before = text.Substring(from, start - from).Replace('\n', ' ');
            var after = text.Substring(start + length, to - start - length).Replace('\n', ' ');
            return (from > 0 ? "…" : "") + before + "[" + text.Substring(start, length) + "]" + after + (to < text.Length ? "…" : "");
        }
    }
}
