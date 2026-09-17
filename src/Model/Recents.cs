using System;
using System.Collections.Generic;
using System.Globalization;

namespace Marabook.Model
{
    /// <summary>Une entrée de la liste des récents du projet (batch 41,
    /// .plot v18) : l'identifiant d'un item ouvert et le moment de son
    /// ouverture (« yyyy-MM-dd HH:mm:ss », heure locale).</summary>
    public class RecentEntry
    {
        public string ItemId = "";
        public string Date = "";
    }

    /// <summary>Les récents : au plus dix entrées dans le manifeste, la plus
    /// récente en tête. Le point d'accroche est l'OUVERTURE d'un item (pas
    /// sa modification) ; un doublon remonte en tête. Les entrées dont
    /// l'item n'existe plus sont ignorées à l'affichage et purgées au
    /// chargement seulement — doctrine du batch 38 : jamais à la
    /// sauvegarde, tant que l'historique de session peut ressusciter
    /// l'item. Rien ici ne passe par HistoryManager : Ctrl+Z défait une
    /// épingle, jamais une visite. Indépendant de _navBack/_navForward
    /// (pile positionnelle de session) : deux petites structures.</summary>
    public static class Recents
    {
        public const int Cap = 10;
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        public static string Now()
        {
            return DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Un item vient d'être ouvert : en tête, sans doublon,
        /// plafond à Cap. Retourne faux si rien n'a changé (déjà en tête).</summary>
        public static bool Touch(List<RecentEntry> recents, string itemId, string date)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            for (var i = recents.Count - 1; i >= 0; i--)
                if (recents[i].ItemId == itemId) recents.RemoveAt(i);
            recents.Insert(0, new RecentEntry { ItemId = itemId, Date = date });
            while (recents.Count > Cap) recents.RemoveAt(recents.Count - 1);
            return true;
        }

        /// <summary>Les entrées encore VALIDES, dans l'ordre : l'item existe
        /// et n'est pas dans la Corbeille. C'est le filtre de l'affichage
        /// (Reprendre) et de la sélection initiale.</summary>
        public static List<RecentEntry> Valid(Project project)
        {
            var valid = new List<RecentEntry>();
            foreach (var entry in project.Recents)
            {
                var item = project.FindById(entry.ItemId);
                if (item == null || item.IsCategory) continue;
                if (InTrash(item)) continue;
                valid.Add(entry);
            }
            return valid;
        }

        /// <summary>Purge des entrées mortes — AU CHARGEMENT seulement.</summary>
        public static int Purge(Project project)
        {
            var removed = 0;
            for (var i = project.Recents.Count - 1; i >= 0; i--)
                if (project.FindById(project.Recents[i].ItemId) == null)
                {
                    project.Recents.RemoveAt(i);
                    removed++;
                }
            return removed;
        }

        public static bool InTrash(BinderItem item)
        {
            var root = item.RootCategory();
            return root != null && root.CategoryKey == Project.KeyTrash;
        }

        public static DateTime? Parse(string date)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out parsed)) return parsed;
            return null;
        }

        /// <summary>Le temps écoulé en clair : « à l'instant », « il y a
        /// 20 min », « il y a 3 h », « hier », « lundi » (moins d'une
        /// semaine), puis la date.</summary>
        public static string Elapsed(string date, DateTime now)
        {
            var when = Parse(date);
            if (when == null) return "";
            var delta = now - when.Value;
            if (delta.TotalMinutes < 1) return "à l'instant";
            if (delta.TotalMinutes < 60) return "il y a " + (int)delta.TotalMinutes + " min";
            if (now.Date == when.Value.Date) return "il y a " + (int)delta.TotalHours + " h";
            if (now.Date.AddDays(-1) == when.Value.Date) return "hier";
            if (delta.TotalDays < 7)
            {
                var day = when.Value.ToString("dddd", new CultureInfo("fr-FR"));
                return day;
            }
            return "le " + when.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); // JJ/MM/AAAA à l'écran (12/09)
        }
    }
}
