using System;
using System.Collections.Generic;
using Marabook.Correction;

namespace Marabook.Model
{
    /// <summary>Une étape de l'évolution d'une fiche (batch 47) : ce qui
    /// change pour le personnage (ou le lieu, l'objet…) dans un écrit donné
    /// — « perd son bras », « apprend la vérité ». Liée à un écrit par son
    /// id (null = étape libre, hors récit), une note en texte simple.</summary>
    public class EvolutionEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string TextId;      // l'écrit où ça se passe, null = libre
        public string Title = "";  // le nom d'une étape libre (21/09, v25) — « Enfance », « Après la guerre »
        public string Note = "";

        /// <summary>Ce qui nomme l'étape à l'affichage : l'écrit lié (son
        /// titre), sinon le titre libre, sinon rien.</summary>
        public string Label(Project project)
        {
            var text = project == null || TextId == null ? null : project.FindById(TextId);
            if (text != null) return text.Title;
            return (Title ?? "").Trim();
        }
    }

    /// <summary>Une ligne de présence : l'écrit, le nombre d'occurrences des
    /// noms de la fiche dedans, et le livre qui le contient (null hors livre).</summary>
    public class PresenceRow
    {
        public BinderItem Text;
        public BinderItem Book;
        public int Count;
    }

    /// <summary>LA PRÉSENCE (batch 47) : où une fiche apparaît dans les
    /// écrits, par comptage de ses noms et alias. Règles pures, sans WPF,
    /// testées en console (C24).
    ///
    /// Les noms d'une fiche : son titre, puis les champs « Nom », « Prénom »
    /// et « Alias » de son modèle (un alias par virgule, point-virgule ou
    /// barre). Un nom compte quand il apparaît en MOT ENTIER, accents pliés
    /// (« Léa » = « Lea »), mais CASSE RESPECTÉE (« Pierre » n'est pas la
    /// pierre). Les noms longs sont cherchés d'abord et masquent ce qu'ils
    /// couvrent : « Jean Valjean » ne compte pas aussi pour « Jean ».</summary>
    public static class Presence
    {
        private static readonly char[] AliasSeparators = { ',', ';', '/' };

        /// <summary>Les noms cherchés pour une fiche, dédoublonnés (pliés,
        /// casse gardée), les plus longs d'abord. Vide si la fiche n'a pas
        /// de titre utile.</summary>
        public static List<string> NamesOf(BinderItem sheet, SheetTemplate template)
        {
            var names = new List<string>();
            if (sheet == null) return names;
            AddName(names, sheet.Title);
            if (template != null)
                foreach (var field in template.Fields)
                {
                    string value;
                    if (!sheet.FieldValues.TryGetValue(field.Id, out value) || string.IsNullOrEmpty(value)) continue;
                    var kind = FrenchTokenizer.Fold(field.Name ?? "");
                    if (kind == "nom" || kind == "prenom") AddName(names, value);
                    else if (kind == "alias" || kind == "surnom" || kind == "surnoms")
                        foreach (var alias in value.Split(AliasSeparators)) AddName(names, alias);
                }
            // Tri stable (List.Sort ne l'est pas) : les plus longs d'abord,
            // à longueur égale l'ordre de déclaration (titre, nom, prénom, alias).
            for (var i = 1; i < names.Count; i++)
            {
                var current = names[i];
                var j = i - 1;
                while (j >= 0 && names[j].Length < current.Length) { names[j + 1] = names[j]; j--; }
                names[j + 1] = current;
            }
            return names;
        }

        private static void AddName(List<string> names, string raw)
        {
            var name = FrenchTokenizer.Fold((raw ?? "").Trim(), false);
            if (name.Length < 2) return; // une lettre seule compterait tout et n'importe quoi
            if (!names.Contains(name)) names.Add(name);
        }

        /// <summary>Le nombre d'occurrences des noms (déjà pliés par NamesOf)
        /// dans un texte BRUT — plié ici, casse gardée. Mots entiers ; les
        /// noms longs masquent les courts qu'ils contiennent.</summary>
        public static int CountIn(string text, List<string> names)
        {
            if (string.IsNullOrEmpty(text) || names == null || names.Count == 0) return 0;
            var folded = FrenchTokenizer.Fold(text, false);
            bool[] taken = null;
            var count = 0;
            foreach (var name in names)
            {
                var from = 0;
                while (from <= folded.Length - name.Length)
                {
                    var at = folded.IndexOf(name, from, StringComparison.Ordinal);
                    if (at < 0) break;
                    from = at + 1;
                    if (!IsBoundary(folded, at - 1) || !IsBoundary(folded, at + name.Length)) continue;
                    if (taken != null && Overlaps(taken, at, name.Length)) continue;
                    if (names.Count > 1)
                    {
                        if (taken == null) taken = new bool[folded.Length];
                        for (var i = at; i < at + name.Length; i++) taken[i] = true;
                    }
                    count++;
                    from = at + name.Length;
                }
            }
            return count;
        }

        private static bool IsBoundary(string text, int index)
        {
            if (index < 0 || index >= text.Length) return true;
            return !char.IsLetterOrDigit(text[index]);
        }

        private static bool Overlaps(bool[] taken, int at, int length)
        {
            for (var i = at; i < at + length; i++) if (taken[i]) return true;
            return false;
        }

        /// <summary>Les écrits du récit, dans l'ordre de la Pile : ceux de la
        /// racine Écrits, pages extra et table des matières exclues (elles ne
        /// racontent rien), corbeille exclue.</summary>
        public static List<BinderItem> Writings(Project project)
        {
            var list = new List<BinderItem>();
            if (project == null) return list;
            var root = project.Category(Project.KeyWritings);
            if (root != null) Collect(root, list);
            return list;
        }

        private static void Collect(BinderItem item, List<BinderItem> list)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text && !child.IsExtraPage && !child.IsToc) list.Add(child);
                Collect(child, list);
            }
        }

        /// <summary>L'AMPLITUDE DU SUIVI (21/09) : un écrit est suivi quand
        /// l'amplitude du modèle est vide (tous les écrits) ou qu'elle nomme
        /// l'écrit lui-même, son groupe ou son livre (un ancêtre quelconque).</summary>
        public static bool InScope(BinderItem text, SheetTemplate template)
        {
            if (template == null || template.TrackingScope.Count == 0) return true;
            for (var item = text; item != null; item = item.Parent)
                if (template.TrackingScope.Contains(item.Id)) return true;
            return false;
        }

        /// <summary>Les écrits suivis par un modèle : ceux du récit, dans
        /// l'amplitude de son suivi.</summary>
        public static List<BinderItem> Writings(Project project, SheetTemplate template)
        {
            var list = new List<BinderItem>();
            foreach (var text in Writings(project))
                if (InScope(text, template)) list.Add(text);
            return list;
        }

        /// <summary>Une fiche dont le modèle suit les noms (21/09) : sans
        /// modèle, ou modèle sans suivi, rien n'est compté nulle part.</summary>
        public static bool Tracks(SheetTemplate template)
        {
            return template != null && template.Tracking;
        }

        /// <summary>La présence d'une fiche : un rang par écrit suivi où l'un
        /// de ses noms apparaît, dans l'ordre du récit. Vide si le modèle ne
        /// suit pas les noms.</summary>
        public static List<PresenceRow> Of(BinderItem sheet, SheetTemplate template, Project project)
        {
            var rows = new List<PresenceRow>();
            if (!Tracks(template)) return rows;
            var names = NamesOf(sheet, template);
            if (names.Count == 0) return rows;
            foreach (var text in Writings(project, template))
            {
                var count = CountIn(text.Document.ToPlainText(), names);
                if (count == 0) continue;
                rows.Add(new PresenceRow { Text = text, Book = text.EnclosingBook(), Count = count });
            }
            return rows;
        }

        /// <summary>Qui est présent dans un écrit : les fiches (filtrées —
        /// les personnages, en pratique) dont un nom y apparaît, les plus
        /// présentes d'abord, à égalité dans l'ordre de la Pile.</summary>
        public static List<PresenceRow> In(BinderItem text, Project project, Func<BinderItem, bool> filter)
        {
            var rows = new List<PresenceRow>();
            if (text == null || project == null) return rows;
            var plain = text.Document.ToPlainText();
            if (plain.Length == 0) return rows;
            var trash = project.Trash;
            var order = 0;
            var ranks = new Dictionary<PresenceRow, int>();
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                if (trash != null && (item == trash || item.IsDescendantOf(trash))) continue;
                if (filter != null && !filter(item)) continue;
                // Le suivi du modèle et son amplitude (21/09) valent ici aussi.
                var template = project.FindTemplate(item.TemplateId);
                if (!Tracks(template) || !InScope(text, template)) continue;
                var count = CountIn(plain, NamesOf(item, template));
                if (count == 0) continue;
                var row = new PresenceRow { Text = item, Count = count };
                ranks[row] = order++;
                rows.Add(row);
            }
            rows.Sort(delegate(PresenceRow a, PresenceRow b)
            {
                var byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : ranks[a].CompareTo(ranks[b]);
            });
            return rows;
        }

        /// <summary>L'étape d'évolution d'une fiche pour un écrit donné, ou null.</summary>
        public static EvolutionEntry StepIn(BinderItem sheet, string textId)
        {
            if (sheet == null || textId == null) return null;
            foreach (var entry in sheet.Evolution)
                if (entry.TextId == textId && entry.Note.Trim().Length > 0) return entry;
            return null;
        }

        /// <summary>Les étapes dans l'ordre du récit — LE MÊME partout (fiche
        /// et wiki, 21/09) : celles liées à un écrit suivent l'ordre de la
        /// Pile ; une étape libre (ou liée à un écrit disparu) reste où
        /// l'auteur l'a mise, c'est-à-dire juste après l'étape liée qui la
        /// précède à la saisie (en tête s'il n'y en a pas) — plus jamais
        /// reléguée tout en bas.</summary>
        public static List<EvolutionEntry> OrderedSteps(BinderItem sheet, Project project)
        {
            var steps = new List<EvolutionEntry>();
            if (sheet == null) return steps;
            var rank = new Dictionary<string, int>();
            var writings = Writings(project);
            for (var i = 0; i < writings.Count; i++) rank[writings[i].Id] = i;
            var indexed = new List<KeyValuePair<int, EvolutionEntry>>();
            var anchor = -1;
            for (var i = 0; i < sheet.Evolution.Count; i++)
            {
                var entry = sheet.Evolution[i];
                int r;
                if (entry.TextId != null && rank.TryGetValue(entry.TextId, out r)) anchor = r;
                indexed.Add(new KeyValuePair<int, EvolutionEntry>(anchor, entry));
            }
            // Tri stable : à rang égal, l'ordre de saisie.
            for (var i = 1; i < indexed.Count; i++)
            {
                var current = indexed[i];
                var j = i - 1;
                while (j >= 0 && indexed[j].Key > current.Key) { indexed[j + 1] = indexed[j]; j--; }
                indexed[j + 1] = current;
            }
            foreach (var pair in indexed) steps.Add(pair.Value);
            return steps;
        }
    }
}
