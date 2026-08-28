using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>Un PLAN (batch 35) : une succession de colonnes, de gauche à
    /// droite ; chaque colonne peut être titrée et reliée à un écrit, le
    /// plan entier à un livre ou à un dossier. Dans une colonne : des
    /// ÉLÉMENTS (nom, couleur, intensité) et des NOTES (post-it), ordonnés.</summary>
    public class PlanInfo
    {
        /// <summary>Livre ou dossier relié (id de BinderItem), null = aucun.</summary>
        public string LinkedItemId;

        /// <summary>Le mot des nouvelles colonnes (« Colonne » → « Colonne 3 »,
        /// « chapitre » → « chapitre 3 ») — options du plan, inspecteur.</summary>
        public string ColumnWord = DefaultColumnWord;
        public const string DefaultColumnWord = "Colonne";

        /// <summary>Le titre de la prochaine colonne créée.</summary>
        public string NextColumnTitle()
        {
            var word = (ColumnWord ?? "").Trim();
            if (word.Length == 0) word = DefaultColumnWord;
            return word + " " + (Columns.Count + 1);
        }
        public List<PlanColumn> Columns = new List<PlanColumn>();

        public PlanColumn FindColumn(string id)
        {
            foreach (var column in Columns) if (column.Id == id) return column;
            return null;
        }

        /// <summary>La colonne reliée à cet écrit, ou null.</summary>
        public PlanColumn ColumnOf(string textId)
        {
            if (textId == null) return null;
            foreach (var column in Columns) if (column.LinkedTextId == textId) return column;
            return null;
        }
    }

    public class PlanColumn
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string LinkedTextId;   // écrit relié, null = aucun
        public List<PlanEntry> Entries = new List<PlanEntry>();

        /// <summary>L'intensité la plus haute des éléments de la colonne (0 = aucun).</summary>
        public int PeakIntensity()
        {
            var peak = 0;
            foreach (var entry in Entries)
                if (entry.IsElement && entry.Intensity > peak) peak = entry.Intensity;
            return peak;
        }

        /// <summary>L'intensité moyenne des éléments (0 sans élément).</summary>
        public double MeanIntensity()
        {
            var sum = 0;
            var count = 0;
            foreach (var entry in Entries)
                if (entry.IsElement) { sum += entry.Intensity; count++; }
            return count == 0 ? 0 : (double)sum / count;
        }
    }

    /// <summary>Une brique d'une colonne : un élément (nom, couleur,
    /// intensité 1–5) ou une note (post-it, texte seul).</summary>
    public class PlanEntry
    {
        public const string KindElement = "element";
        public const string KindNote = "note";

        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = KindElement;
        public string Text = "";
        public string Color;          // "#RRGGBB", null = neutre (éléments)
        public int Intensity = 1;     // 1..5 (éléments)

        public bool IsElement { get { return Kind != KindNote; } }
        public bool IsNote { get { return Kind == KindNote; } }
    }

    /// <summary>L'échelle d'intensité — l'implication du lecteur dans un
    /// événement du fait de sa nature, du plus bas au plus haut.</summary>
    public static class PlanIntensity
    {
        public const int Min = 1;
        public const int Max = 5;

        public static readonly string[] Labels =
        {
            "Explications, slice of life", // 1
            "Exploration",                 // 2
            "Tension",                     // 3
            "Action",                      // 4
            "Pinnacle"                     // 5
        };

        public static int Clamp(int value)
        {
            return Math.Max(Min, Math.Min(Max, value));
        }

        public static string Label(int value)
        {
            return Labels[Clamp(value) - 1];
        }

        /// <summary>Le graphique d'intensité du plan : une valeur par colonne
        /// (le pic des éléments, 0 = colonne sans élément).</summary>
        public static List<int> Profile(PlanInfo plan)
        {
            var profile = new List<int>();
            if (plan == null) return profile;
            foreach (var column in plan.Columns) profile.Add(column.PeakIntensity());
            return profile;
        }
    }
}
