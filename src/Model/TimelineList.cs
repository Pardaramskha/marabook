using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Une fiche que des événements concernent, et combien.</summary>
    public class TimelineSheetCount
    {
        public BinderItem Sheet;
        public int Count;
    }

    /// <summary>LE FILTRE PAR FICHE ET LA LISTE DES ÉVÉNEMENTS (20/11). Un
    /// événement « concerne » une fiche quand elle lui est assignée — les
    /// naissances et les décès nés d'une fiche la concernent donc — ou quand
    /// son titre ou sa description porte un [[lien]] vers elle. La liste
    /// range les événements dans l'ordre du temps et se cherche sans casse
    /// ni accents : titre, description, type, élément assigné, date.</summary>
    public static partial class Timelines
    {
        public static bool Concerns(TimelineEvent entry, BinderItem sheet)
        {
            if (entry == null || sheet == null) return false;
            if (HasLink(entry, sheet.Id)) return true;
            var title = sheet.Title ?? "";
            if (title.Length == 0) return false;
            var text = (entry.Title ?? "") + "\n" + (entry.Description ?? "");
            return text.IndexOf("[[", StringComparison.Ordinal) >= 0 && Links.LinksTo(text, title);
        }

        /// <summary>Les fiches que ces événements concernent, triées par
        /// titre, chacune avec son compte — le menu du filtre.</summary>
        public static List<TimelineSheetCount> SheetsConcerned(Project project, IEnumerable<TimelineEvent> events)
        {
            var counts = new List<TimelineSheetCount>();
            if (project == null || events == null) return counts;
            var byId = new Dictionary<string, TimelineSheetCount>(StringComparer.Ordinal);
            foreach (var entry in events)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var assigned in ElementsOf(project, entry))
                    if (assigned.Kind == ItemKind.Sheet) Count(byId, counts, assigned, seen);
                var text = (entry.Title ?? "") + "\n" + (entry.Description ?? "");
                if (text.IndexOf("[[", StringComparison.Ordinal) < 0) continue;
                foreach (var target in Links.Targets(text))
                {
                    if (TimeLinks.IsTimeLink(target)) continue;
                    var cited = project.FindByTitle(target);
                    if (cited != null && cited.Kind == ItemKind.Sheet) Count(byId, counts, cited, seen);
                }
            }
            counts.RemoveAll(delegate(TimelineSheetCount c) { var root = c.Sheet.RootCategory(); return root != null && root.CategoryKey == Project.KeyTrash; });
            counts.Sort(delegate(TimelineSheetCount a, TimelineSheetCount b) { return string.Compare(a.Sheet.Title, b.Sheet.Title, StringComparison.CurrentCultureIgnoreCase); });
            return counts;
        }

        private static void Count(Dictionary<string, TimelineSheetCount> byId, List<TimelineSheetCount> counts, BinderItem sheet, HashSet<string> seen)
        {
            if (!seen.Add(sheet.Id)) return; // un événement ne compte qu'une fois par fiche
            TimelineSheetCount count;
            if (!byId.TryGetValue(sheet.Id, out count))
            {
                count = new TimelineSheetCount { Sheet = sheet };
                byId[sheet.Id] = count;
                counts.Add(count);
            }
            count.Count++;
        }

        /// <summary>Ces événements dans l'ordre du temps (le début, puis la
        /// fin, puis le titre).</summary>
        public static List<TimelineEvent> InOrder(TimeSystem system, IEnumerable<TimelineEvent> events)
        {
            var list = new List<TimelineEvent>();
            if (events != null) foreach (var entry in events) if (entry != null && entry.Start != null) list.Add(entry);
            if (system == null) return list;
            list.Sort(delegate(TimelineEvent a, TimelineEvent b)
            {
                var order = StartOf(system, a).CompareTo(StartOf(system, b));
                if (order == 0) order = EndOf(system, a).CompareTo(EndOf(system, b));
                return order != 0 ? order : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// <summary>Ceux de ces événements qui répondent à la recherche :
        /// chaque mot saisi doit se trouver quelque part — titre,
        /// description (sans la notation des liens), nom du type, élément
        /// assigné, date écrite. Une recherche vide garde tout.</summary>
        public static List<TimelineEvent> Search(Project project, TimeSystem system, TimelineInfo timeline, IEnumerable<TimelineEvent> events, string query)
        {
            var words = TutorialParser.Fold(query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<TimelineEvent>();
            if (events == null) return kept;
            foreach (var entry in events)
            {
                if (words.Length == 0) { kept.Add(entry); continue; }
                var hay = TutorialParser.Fold(Haystack(project, system, timeline, entry));
                var all = true;
                foreach (var word in words) if (hay.IndexOf(word, StringComparison.Ordinal) < 0) { all = false; break; }
                if (all) kept.Add(entry);
            }
            return kept;
        }

        private static string Haystack(Project project, TimeSystem system, TimelineInfo timeline, TimelineEvent entry)
        {
            var type = timeline == null ? null : timeline.FindType(entry.TypeId);
            var linked = "";
            foreach (var item in ElementsOf(project, entry)) linked += (item.Title ?? "") + "\n";
            var place = PlaceOf(project, entry);
            return Links.Strip(entry.Title ?? "") + "\n" + Links.Strip(entry.Description ?? "") + "\n" + (type == null ? "" : type.Name) + "\n"
                + linked + (place == null ? "" : place.Label + "\n") + TagsText(entry) + "\n" + (system == null ? "" : WhenLabel(system, entry));
        }

        /// <summary>L'élément assigné à l'événement, ou null.</summary>
        public static BinderItem LinkedOf(Project project, TimelineEvent entry)
        {
            if (project == null || entry == null || entry.LinkId == null) return null;
            return project.FindById(entry.LinkId);
        }
    }
}
