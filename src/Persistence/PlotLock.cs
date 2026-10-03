using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Ce que dit un fichier de verrou : qui tient le projet.</summary>
    public sealed class LockInfo
    {
        public string Machine = "";
        public string User = "";
        public int Pid;
        public string Started = "";   // « yyyy-MM-dd HH:mm », heure locale de la machine qui l'a posé
        public DateTime HeartbeatUtc;  // le dernier battement (UTC)

        /// <summary>« PC-SALON (remie), depuis le 03/10/2026 11:20 ».</summary>
        public string Describe()
        {
            var who = Machine.Length == 0 ? "une autre machine" : Machine;
            if (User.Length > 0) who += " (" + User + ")";
            return Started.Length == 0 ? who : who + ", depuis le " + Dates.Display(Started);
        }
    }

    /// <summary>Le VERROU d'un .plot (1.0.3) : un petit fichier « nom.plot.lock »
    /// posé à côté du projet tant qu'il est ouvert — « ce projet est ouvert
    /// sur PC-SALON ». Dans un dossier synchronisé (OneDrive, Dropbox, NAS),
    /// le verrou voyage avec le projet : l'autre machine le voit avant
    /// d'ouvrir et peut renoncer plutôt que de produire des copies en
    /// conflit. Un verrou de la même machine ne vaut que si son processus
    /// vit encore ; un verrou d'une autre machine se périme sans battement
    /// (le battement est renouvelé toutes les deux minutes par la fenêtre).
    /// Toute erreur de disque est avalée : un verrou n'empêche jamais de
    /// travailler, il prévient.</summary>
    public static class PlotLock
    {
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

        /// <summary>Le processus de ce pid est-il un Marabook vivant ? (pour les
        /// tests : remplaçable.)</summary>
        public static Func<int, bool> AliveProbe = IsMarabookAlive;

        /// <summary>Le nom de la machine telle que le verrou la nomme (pour les
        /// tests : remplaçable).</summary>
        public static Func<string> MachineNameProvider = delegate { return SafeMachineName(); };

        public static string PathFor(string plotPath)
        {
            return plotPath + ".lock";
        }

        /// <summary>Le verrou tel qu'il est sur le disque, ou null (absent ou illisible).</summary>
        public static LockInfo Read(string plotPath)
        {
            if (string.IsNullOrEmpty(plotPath)) return null;
            try
            {
                var lockPath = PathFor(plotPath);
                if (!File.Exists(lockPath)) return null;
                var node = Json.AsObject(Json.Parse(File.ReadAllText(lockPath)));
                if (node == null) return null;
                var info = new LockInfo
                {
                    Machine = Json.AsString(Json.Field(node, "machine")) ?? "",
                    User = Json.AsString(Json.Field(node, "user")) ?? "",
                    Pid = (int)Json.AsDouble(Json.Field(node, "pid"), 0),
                    Started = Json.AsString(Json.Field(node, "started")) ?? ""
                };
                DateTime beat;
                var raw = Json.AsString(Json.Field(node, "heartbeat")) ?? "";
                info.HeartbeatUtc = DateTime.TryParseExact(raw, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out beat) ? beat : DateTime.MinValue;
                return info;
            }
            catch { return null; }
        }

        /// <summary>Un verrou ÉTRANGER et vivant : celui d'un autre processus
        /// de cette machine encore en vie, ou d'une autre machine dont le
        /// battement date de moins de StaleAfter. Null : libre (absent, à nous,
        /// ou périmé).</summary>
        public static LockInfo Probe(string plotPath)
        {
            var info = Read(plotPath);
            if (info == null) return null;
            return IsForeignAndAlive(info, DateTime.UtcNow) ? info : null;
        }

        /// <summary>La règle pure, testable avec une horloge posée.</summary>
        public static bool IsForeignAndAlive(LockInfo info, DateTime nowUtc)
        {
            if (info == null) return false;
            if (string.Equals(info.Machine, MachineNameProvider(), StringComparison.OrdinalIgnoreCase))
            {
                if (info.Pid == CurrentPid()) return false; // le nôtre
                return AliveProbe(info.Pid);
            }
            return nowUtc - info.HeartbeatUtc < StaleAfter;
        }

        /// <summary>Pose (ou reprend) le verrou pour ce processus.</summary>
        public static void Acquire(string plotPath)
        {
            if (string.IsNullOrEmpty(plotPath)) return;
            WriteOurs(plotPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        }

        /// <summary>Renouvelle le battement — si le verrou est encore le nôtre
        /// (une autre machine a pu le reprendre : on ne l'écrase pas).</summary>
        public static void Heartbeat(string plotPath)
        {
            var info = Read(plotPath);
            if (info == null || !IsOurs(info)) return;
            WriteOurs(plotPath, info.Started);
        }

        /// <summary>Retire le verrou s'il est le nôtre.</summary>
        public static void Release(string plotPath)
        {
            if (string.IsNullOrEmpty(plotPath)) return;
            try
            {
                var info = Read(plotPath);
                if (info != null && !IsOurs(info)) return;
                var lockPath = PathFor(plotPath);
                if (File.Exists(lockPath)) File.Delete(lockPath);
            }
            catch { }
        }

        public static bool IsOurs(LockInfo info)
        {
            return info != null && info.Pid == CurrentPid()
                && string.Equals(info.Machine, MachineNameProvider(), StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteOurs(string plotPath, string started)
        {
            try
            {
                var node = new Dictionary<string, object>();
                node["machine"] = MachineNameProvider();
                node["user"] = SafeUserName();
                node["pid"] = CurrentPid();
                node["started"] = started ?? "";
                node["heartbeat"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                node["app"] = "Marabook " + AppInfo.Version;
                File.WriteAllText(PathFor(plotPath), Json.Write(node));
            }
            catch { }
        }

        // ------------------------------------------------------------ outils

        private static string SafeMachineName()
        {
            try { return Environment.MachineName ?? ""; } catch { return ""; }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? ""; } catch { return ""; }
        }

        private static int CurrentPid()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; } catch { return 0; }
        }

        private static bool IsMarabookAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                return process.ProcessName.StartsWith("Marabook", StringComparison.OrdinalIgnoreCase)
                    || process.ProcessName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
