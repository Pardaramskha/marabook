using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'OBJECTIF et la LIMITE d'un écrit (1.1.0, .plot v41) : un
    /// nombre de mots, de signes (espaces comprises) ou de pages, réglé dans
    /// le Général du rail. L'objectif se remplit à mesure qu'on écrit ; la
    /// limite dépassée cercle la carte de rouge au tableau. Pur : les
    /// comptes viennent de l'appelant (les mots et signes de TextStats, les
    /// pages du compositeur).</summary>
    public static class TextGoal
    {
        public const string Words = "words";
        public const string Chars = "chars";
        public const string Pages = "pages";

        public static readonly string[] Units = { Words, Chars, Pages };

        /// <summary>Une unité connue, ou les mots pour tout le reste (un
        /// .plot plus récent lu par une version qui ne la sait pas).</summary>
        public static string Normalize(string unit)
        {
            if (unit == Chars || unit == Pages) return unit;
            return Words;
        }

        public static string Label(string unit)
        {
            switch (Normalize(unit))
            {
                case Chars: return "Signes";
                case Pages: return "Pages";
                default: return "Mots";
            }
        }

        /// <summary>Le libellé au pluriel ou au singulier selon le nombre :
        /// « 1 mot », « 12 signes », « 3 pages ».</summary>
        public static string Count(int count, string unit)
        {
            string word;
            switch (Normalize(unit))
            {
                case Chars: word = count == 1 ? "signe" : "signes"; break;
                case Pages: word = count == 1 ? "page" : "pages"; break;
                default: word = count == 1 ? "mot" : "mots"; break;
            }
            return count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " " + word;
        }

        /// <summary>La mesure courante dans l'unité demandée.</summary>
        public static int Measure(string unit, int words, int chars, int pages)
        {
            switch (Normalize(unit))
            {
                case Chars: return chars;
                case Pages: return pages;
                default: return words;
            }
        }

        /// <summary>La part de l'objectif atteinte, bornée à 1 ; 0 sans objectif.</summary>
        public static double Ratio(int measured, int goal)
        {
            if (goal <= 0) return 0;
            return Math.Max(0, Math.Min(1.0, measured / (double)goal));
        }

        /// <summary>Vrai quand la limite est posée (> 0) et franchie.</summary>
        public static bool Exceeded(int measured, int limit)
        {
            return limit > 0 && measured > limit;
        }

        /// <summary>Un nombre saisi : les chiffres seuls (espaces et séparateurs
        /// de milliers ignorés), 0 pour tout le reste ou un nombre négatif.</summary>
        public static int Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var digits = new System.Text.StringBuilder();
            foreach (var c in text) if (char.IsDigit(c)) digits.Append(c);
            int value;
            if (digits.Length == 0 || !int.TryParse(digits.ToString(), out value)) return 0;
            return Math.Max(0, value);
        }
    }

    /// <summary>Les TAGS des fiches (1.1.0) : la normalisation d'un tag tapé
    /// et l'inventaire d'un projet, pour le filtre de la bibliothèque.</summary>
    public static class SheetTags
    {
        /// <summary>Le tag tel qu'il se range : rogné, les blancs intérieurs
        /// réduits ; vide pour rien.</summary>
        public static string Normalize(string tag)
        {
            if (tag == null) return "";
            var parts = tag.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Ajoute le tag à la fiche s'il n'y est pas (casse ignorée) ;
        /// vrai si ajouté.</summary>
        public static bool Add(BinderItem sheet, string tag)
        {
            var clean = Normalize(tag);
            if (sheet == null || clean.Length == 0 || Has(sheet, clean)) return false;
            sheet.Tags.Add(clean);
            return true;
        }

        public static bool Has(BinderItem sheet, string tag)
        {
            if (sheet == null) return false;
            foreach (var existing in sheet.Tags) if (Same(existing, tag)) return true;
            return false;
        }

        public static bool Remove(BinderItem sheet, string tag)
        {
            if (sheet == null) return false;
            for (var i = 0; i < sheet.Tags.Count; i++)
                if (Same(sheet.Tags[i], tag)) { sheet.Tags.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>Tous les tags des fiches du projet (corbeille exclue), sans
        /// doublon, dans l'ordre alphabétique à la française.</summary>
        public static List<string> All(Project project)
        {
            var tags = new List<string>();
            if (project == null) return tags;
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                foreach (var tag in item.Tags)
                {
                    var found = false;
                    foreach (var known in tags) if (Same(known, tag)) { found = true; break; }
                    if (!found) tags.Add(Normalize(tag));
                }
            }
            tags.Sort(delegate(string a, string b)
            {
                return string.Compare(a, b, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                    System.Globalization.CompareOptions.IgnoreCase);
            });
            return tags;
        }
    }
}
