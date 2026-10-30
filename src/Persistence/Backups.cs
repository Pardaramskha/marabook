using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Marabook.Persistence
{
    /// <summary>Les copies horodatées (1.1.0, 30/10) : à côté du « .plot.bak »
    /// roulant (la version d'avant, réécrite à chaque enregistrement), des
    /// copies DATÉES du projet tel qu'enregistré, espacées d'au moins
    /// IntervalMinutes et gardées au nombre de Keep. Chacune est un .plot
    /// complet qui s'ouvre comme un autre. Rangées dans « &lt;nom&gt;.sauvegardes »
    /// à côté du projet, ou dans le dossier choisi aux Préférences.</summary>
    public sealed class BackupPolicy
    {
        public bool Enabled = true;
        /// <summary>Le dossier commun choisi, ou null : à côté du projet.</summary>
        public string Folder;
        public int Keep = DefaultKeep;
        public int IntervalMinutes = DefaultIntervalMinutes;

        public const int DefaultKeep = 20, MinKeep = 1, MaxKeep = 200;
        public const int DefaultIntervalMinutes = 30, MinIntervalMinutes = 0, MaxIntervalMinutes = 1440;

        public static int ClampKeep(int value) { return Math.Max(MinKeep, Math.Min(MaxKeep, value)); }
        public static int ClampInterval(int value) { return Math.Max(MinIntervalMinutes, Math.Min(MaxIntervalMinutes, value)); }
    }

    public sealed class BackupInfo
    {
        public string Path;
        public DateTime Date;
    }

    public static class Backups
    {
        public const string FolderSuffix = ".sauvegardes";
        private const string StampFormat = "yyyy-MM-dd-HH'h'mm'm'ss";
        private static readonly Regex StampPattern = new Regex(@"^(.*)-(\d{4}-\d{2}-\d{2}-\d{2}h\d{2}m\d{2})\.plot$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Le dossier des copies d'un projet. Dossier commun : un
        /// sous-dossier au nom du projet, pour que deux projets ne mêlent pas
        /// leurs copies.</summary>
        public static string FolderFor(string plotPath, BackupPolicy policy)
        {
            var name = Path.GetFileNameWithoutExtension(plotPath);
            if (policy != null && !string.IsNullOrEmpty(policy.Folder))
                return Path.Combine(policy.Folder, name);
            return Path.Combine(Path.GetDirectoryName(plotPath) ?? "", name + FolderSuffix);
        }

        /// <summary>Le fichier est-il une copie horodatée (par son nom) ?</summary>
        public static bool IsBackupName(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return StampPattern.IsMatch(Path.GetFileName(path));
        }

        /// <summary>Le nom du projet d'où vient une copie (« roman » pour
        /// « roman-2026-10-30-14h12m05.plot »), ou null.</summary>
        public static string ProjectNameOf(string path)
        {
            var match = StampPattern.Match(Path.GetFileName(path ?? ""));
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>La date portée par le nom d'une copie, ou null.</summary>
        public static DateTime? DateOf(string path)
        {
            var match = StampPattern.Match(Path.GetFileName(path ?? ""));
            if (!match.Success) return null;
            DateTime date;
            if (DateTime.TryParseExact(match.Groups[2].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date)) return date;
            return null;
        }

        /// <summary>Les copies d'un projet, de la plus récente à la plus ancienne.</summary>
        public static List<BackupInfo> List(string plotPath, BackupPolicy policy)
        {
            var result = new List<BackupInfo>();
            var folder = FolderFor(plotPath, policy);
            var prefix = Path.GetFileNameWithoutExtension(plotPath) + "-";
            try
            {
                if (!Directory.Exists(folder)) return result;
                foreach (var file in Directory.GetFiles(folder, "*.plot"))
                {
                    var name = Path.GetFileName(file);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var date = DateOf(file);
                    if (date == null) continue;
                    result.Add(new BackupInfo { Path = file, Date = date.Value });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            result.Sort(delegate(BackupInfo a, BackupInfo b) { return b.Date.CompareTo(a.Date); });
            return result;
        }

        /// <summary>Le projet vient d'être enregistré sous plotPath : une copie
        /// datée si la dernière est plus vieille que l'intervalle (ou s'il n'y
        /// en a pas), puis les plus anciennes au-delà de Keep sont retirées.
        /// Rend le chemin de la copie faite, ou null. Ne lève jamais : une
        /// copie manquée ne doit pas faire échouer l'enregistrement — l'erreur
        /// est rendue par « error » pour qui veut la dire.</summary>
        public static string Record(string plotPath, BackupPolicy policy, DateTime now, out string error)
        {
            error = null;
            if (policy == null || !policy.Enabled || string.IsNullOrEmpty(plotPath)) return null;
            try
            {
                var existing = List(plotPath, policy);
                if (existing.Count > 0 && (now - existing[0].Date).TotalMinutes < policy.IntervalMinutes) return null;
                var folder = FolderFor(plotPath, policy);
                Directory.CreateDirectory(folder);
                var name = Path.GetFileNameWithoutExtension(plotPath) + "-" + now.ToString(StampFormat, CultureInfo.InvariantCulture) + ".plot";
                var target = Path.Combine(folder, name);
                // La copie passe par un .tmp : une copie interrompue ne laisse
                // pas un .plot tronqué qui aurait l'air d'une sauvegarde.
                File.Copy(plotPath, target + ".tmp", true);
                if (File.Exists(target)) File.Delete(target);
                File.Move(target + ".tmp", target);
                Prune(plotPath, policy);
                return target;
            }
            catch (IOException e) { error = e.Message; return null; }
            catch (UnauthorizedAccessException e) { error = e.Message; return null; }
        }

        /// <summary>Retire les copies au-delà de Keep, les plus anciennes d'abord.</summary>
        public static int Prune(string plotPath, BackupPolicy policy)
        {
            var keep = BackupPolicy.ClampKeep(policy.Keep);
            var existing = List(plotPath, policy);
            var removed = 0;
            for (var i = keep; i < existing.Count; i++)
            {
                try { File.Delete(existing[i].Path); removed++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return removed;
        }
    }
}
