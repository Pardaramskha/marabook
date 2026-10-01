using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'ordre d'un sélecteur de police (0.50.0, catalogue de
    /// polices) — pur, sur des noms : les FAVORITES en tête (des doublons
    /// marqués), un trait, les RÉCENTES (doublons, sans celles déjà
    /// favorites), un trait, puis le catalogue entier — les polices EXCLUES
    /// n'apparaissent nulle part, favorites comprises. La vue (FontCatalog)
    /// habille chaque case avec la famille de police du système.</summary>
    public static class FontOrder
    {
        /// <summary>Une case du sélecteur : un nom (null = le trait), copie
        /// (favorite ou récente) ou l'entrée du catalogue elle-même.</summary>
        public sealed class Slot
        {
            public string Name;
            public bool IsFavoriteCopy;
            public bool IsCopy;
            public bool IsSeparator { get { return Name == null; } }
            public string Badge { get { return IsFavoriteCopy ? "★" : ""; } }
        }

        public static List<Slot> Arrange(IList<string> catalog, IList<string> favorites, IList<string> recents,
            IList<string> excluded, int recentCount)
        {
            var result = new List<Slot>();
            var head = new List<string>();
            foreach (var name in favorites ?? new List<string>())
            {
                var entry = FindIn(catalog, name);
                if (entry == null || IsIn(excluded, name) || IsIn(head, name)) continue;
                head.Add(entry);
            }
            if (head.Count > 0)
            {
                foreach (var name in head) result.Add(new Slot { Name = name, IsFavoriteCopy = true, IsCopy = true });
                result.Add(new Slot());
            }
            var recentCopies = new List<string>();
            foreach (var name in recents ?? new List<string>())
            {
                if (recentCopies.Count >= recentCount) break;
                var entry = FindIn(catalog, name);
                if (entry == null || IsIn(excluded, name) || IsIn(head, name) || IsIn(recentCopies, name)) continue;
                recentCopies.Add(entry);
            }
            if (recentCopies.Count > 0)
            {
                foreach (var name in recentCopies) result.Add(new Slot { Name = name, IsCopy = true });
                result.Add(new Slot());
            }
            foreach (var name in catalog)
                if (!IsIn(excluded, name)) result.Add(new Slot { Name = name });
            return result;
        }

        private static string FindIn(IList<string> names, string name)
        {
            if (string.IsNullOrEmpty(name) || names == null) return null;
            foreach (var candidate in names)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return candidate;
            return null;
        }

        private static bool IsIn(IList<string> names, string name)
        {
            return FindIn(names, name) != null;
        }
    }
}
