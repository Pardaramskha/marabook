using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Les copies horodatées (1.1.0, 30/10) : à côté du « .plot.bak »
    /// roulant (la version d'avant, réécrite à chaque enregistrement, cachée
    /// et retirée à la fermeture propre), des copies DATÉES du projet tel
    /// qu'enregistré, espacées d'au moins IntervalMinutes et gardées au
    /// nombre de Backups.Keep. Chacune est un .plot complet qui s'ouvre
    /// comme un autre. Rangées dans le dossier de données de Marabook
    /// (« sauvegardes/&lt;nom&gt;-&lt;hash du chemin&gt; »), jamais dans
    /// l'arborescence de l'auteur (Rémi, 30/10).</summary>
    public sealed class BackupPolicy
    {
        public bool Enabled = true;
        public int IntervalMinutes = DefaultIntervalMinutes;

        public const int DefaultIntervalMinutes = 30, MinIntervalMinutes = 0, MaxIntervalMinutes = 1440;

        public static int ClampInterval(int value) { return Math.Max(MinIntervalMinutes, Math.Min(MaxIntervalMinutes, value)); }
    }

    public sealed class BackupInfo
    {
        public string Path;
        public DateTime Date;
    }

    public static class Backups
    {
        /// <summary>Le nombre de copies gardées par projet — fixe (Rémi, 30/10).</summary>
        public const int Keep = 5;
        /// <summary>Le dossier des copies, remplaçable (tests). Null = celui de l'application.</summary>
        public static string Root;
        private const string StampFormat = "yyyy-MM-dd-HH'h'mm'm'ss";
        private static readonly Regex StampPattern = new Regex(@"^(.*)-(\d{4}-\d{2}-\d{2}-\d{2}h\d{2}m\d{2})\.plot$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Le dossier racine des copies (créé au besoin).</summary>
        public static string RootFolder()
        {
            return Root ?? Path.Combine(Platform.Current.DataFolder, "sauvegardes");
        }

        /// <summary>Le dossier des copies d'un projet : « nom-hash » sous la
        /// racine — le hash du chemin (celui du secours) sépare deux projets
        /// de même nom.</summary>
        public static string FolderFor(string plotPath)
        {
            var name = Path.GetFileNameWithoutExtension(plotPath);
            return Path.Combine(RootFolder(), name + "-" + RecoveryStore.Hash(Path.GetFullPath(plotPath)));
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
        public static List<BackupInfo> List(string plotPath)
        {
            var result = new List<BackupInfo>();
            var folder = FolderFor(plotPath);
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
                var existing = List(plotPath);
                if (existing.Count > 0 && (now - existing[0].Date).TotalMinutes < policy.IntervalMinutes) return null;
                var folder = FolderFor(plotPath);
                Directory.CreateDirectory(folder);
                var name = Path.GetFileNameWithoutExtension(plotPath) + "-" + now.ToString(StampFormat, CultureInfo.InvariantCulture) + ".plot";
                var target = Path.Combine(folder, name);
                // La copie passe par un .tmp : une copie interrompue ne laisse
                // pas un .plot tronqué qui aurait l'air d'une sauvegarde.
                File.Copy(plotPath, target + ".tmp", true);
                if (File.Exists(target)) File.Delete(target);
                File.Move(target + ".tmp", target);
                Prune(plotPath);
                return target;
            }
            catch (IOException e) { error = e.Message; return null; }
            catch (UnauthorizedAccessException e) { error = e.Message; return null; }
        }

        /// <summary>Retire les copies au-delà de Keep, les plus anciennes d'abord.</summary>
        public static int Prune(string plotPath)
        {
            var existing = List(plotPath);
            var removed = 0;
            for (var i = Keep; i < existing.Count; i++)
            {
                try { File.Delete(existing[i].Path); removed++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return removed;
        }

        // ------------------------------------------------- le .bak roulant

        /// <summary>Le « .plot.bak » qui vient d'être posé par l'échange : caché
        /// et tenu hors de l'indexation (Windows ; ailleurs, sans effet). Ne
        /// lève jamais.</summary>
        public static void HideRollingBak(string plotPath)
        {
            var bak = plotPath + ".bak";
            try
            {
                if (!File.Exists(bak)) return;
                File.SetAttributes(bak, File.GetAttributes(bak) | FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (PlatformNotSupportedException) { }
        }

        /// <summary>À la fermeture propre : le .bak est retiré SI le .plot est
        /// bien la sauvegarde complète attendue — il porte l'empreinte du
        /// dernier enregistrement (saveId) et n'est pas plus vieux que le
        /// .bak. Après un plantage, rien ne passe par ici, et un .plot
        /// abîmé ou étranger ne lit pas la bonne empreinte : le .bak reste,
        /// pour le secours. Vrai si retiré.</summary>
        public static bool DropRollingBak(string plotPath, string saveId)
        {
            if (string.IsNullOrEmpty(plotPath) || saveId == null) return false;
            var bak = plotPath + ".bak";
            try
            {
                if (!File.Exists(bak) || !File.Exists(plotPath)) return false;
                if (File.GetLastWriteTimeUtc(plotPath) < File.GetLastWriteTimeUtc(bak)) return false;
                var onDisk = PlotFile.ReadSaveId(plotPath);
                if (onDisk == null || onDisk != saveId) return false;
                File.SetAttributes(bak, FileAttributes.Normal);
                File.Delete(bak);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
