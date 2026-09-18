using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Une session de travail sur un projet, vue par le secours
    /// (18/09) : le fichier de secours (textes seuls) et le TÉMOIN de
    /// session, un petit JSON qui n'existe que pendant que le projet est
    /// ouvert. Un témoin retrouvé au lancement = la session précédente ne
    /// s'est pas terminée proprement (plantage, arrêt brutal).</summary>
    public class RecoverySession
    {
        public string Id = "";
        public string ProjectPath;   // null : projet jamais enregistré
        public string ProjectName = "";
        public string RecoveryPath = "";
        public string SessionPath = "";
        public string Started = "";  // "yyyy-MM-dd HH:mm"
        public int Pid;

        /// <summary>La date du fichier de secours, s'il existe.</summary>
        public DateTime? RecoveryWritten
        {
            get
            {
                try { return File.Exists(RecoveryPath) ? File.GetLastWriteTime(RecoveryPath) : (DateTime?)null; }
                catch { return null; }
            }
        }

        public bool HasRecovery { get { return File.Exists(RecoveryPath); } }
    }

    /// <summary>Le magasin de secours : %APPDATA%\Marabook\recovery. Rien de
    /// WPF ; le dossier se remplace pour les tests (Root).</summary>
    public static class RecoveryStore
    {
        /// <summary>Le dossier, remplaçable (tests). Null = celui de l'application.</summary>
        public static string Root;

        public static string Folder()
        {
            var folder = Root ?? Path.Combine(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook"),
                "recovery");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Ouvre une session : pose le témoin. Le fichier de secours
        /// n'est écrit qu'à la première modification (Write).</summary>
        public static RecoverySession Begin(string projectPath, string projectName)
        {
            var folder = Folder();
            var id = projectPath == null
                ? "sans-titre-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                : Hash(projectPath);
            var session = new RecoverySession
            {
                Id = id,
                ProjectPath = projectPath,
                ProjectName = projectName ?? "",
                RecoveryPath = Path.Combine(folder, SafeName(projectName) + "-" + id + ".plot"),
                SessionPath = Path.Combine(folder, id + ".session"),
                Started = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Pid = CurrentPid()
            };
            WriteSession(session);
            return session;
        }

        private static void WriteSession(RecoverySession session)
        {
            var node = new Dictionary<string, object>();
            node["id"] = session.Id;
            if (session.ProjectPath != null) node["path"] = session.ProjectPath;
            node["name"] = session.ProjectName;
            node["recovery"] = session.RecoveryPath;
            node["started"] = session.Started;
            node["pid"] = session.Pid;
            File.WriteAllText(session.SessionPath, Json.Write(node), new UTF8Encoding(false));
        }

        /// <summary>Le secours : les textes du projet, rien de lourd.</summary>
        public static void Write(Project project, RecoverySession session)
        {
            PlotFile.Save(project, session.RecoveryPath, true);
        }

        /// <summary>Le .plot vient d'être enregistré en entier : le secours
        /// est périmé, on le retire (le témoin reste — la session continue).</summary>
        public static void DropRecovery(RecoverySession session)
        {
            Delete(session.RecoveryPath);
            Delete(session.RecoveryPath + ".tmp");
        }

        /// <summary>Fin de session propre : témoin retiré, secours aussi
        /// sauf demande contraire.</summary>
        public static void End(RecoverySession session, bool keepRecovery = false)
        {
            if (session == null) return;
            Delete(session.SessionPath);
            if (!keepRecovery) DropRecovery(session);
        }

        /// <summary>Les sessions laissées par un arrêt brutal : un témoin
        /// dont le processus n'est plus vivant ET un secours présent. Un
        /// témoin sans secours (rien n'avait été modifié) est nettoyé en
        /// silence ; un témoin d'un Marabook encore ouvert (deux fenêtres)
        /// est laissé tranquille.</summary>
        public static List<RecoverySession> Pending()
        {
            var pending = new List<RecoverySession>();
            string[] files;
            try { files = Directory.GetFiles(Folder(), "*.session"); }
            catch { return pending; }
            foreach (var file in files)
            {
                RecoverySession session;
                try { session = ReadSession(file); }
                catch { Delete(file); continue; }
                if (session == null) { Delete(file); continue; }
                // Notre propre session (projet ouvert par argument avant le
                // lancement) et celles d'un autre Marabook vivant : à laisser.
                if (session.Pid == CurrentPid() || IsAlive(session.Pid)) continue;
                if (!session.HasRecovery) { Delete(file); continue; }
                pending.Add(session);
            }
            return pending;
        }

        private static RecoverySession ReadSession(string file)
        {
            var node = Json.AsObject(Json.Parse(File.ReadAllText(file)));
            if (node == null) return null;
            var session = new RecoverySession
            {
                Id = Json.AsString(Json.Field(node, "id")) ?? "",
                ProjectPath = Json.AsString(Json.Field(node, "path")),
                ProjectName = Json.AsString(Json.Field(node, "name")) ?? "",
                RecoveryPath = Json.AsString(Json.Field(node, "recovery")) ?? "",
                SessionPath = file,
                Started = Json.AsString(Json.Field(node, "started")) ?? "",
                Pid = Json.AsInt(Json.Field(node, "pid"), 0)
            };
            return session.RecoveryPath.Length == 0 ? null : session;
        }

        // ------------------------------------------------------------ outils

        private static int CurrentPid()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }

        /// <summary>Un autre Marabook porte-t-il ce pid ? (le nôtre ne compte pas)</summary>
        private static bool IsAlive(int pid)
        {
            if (pid <= 0 || pid == CurrentPid()) return false;
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                return process.ProcessName.StartsWith("Marabook", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Douze hexadécimaux du SHA-1 du chemin (casse ignorée) :
        /// un même projet retrouve toujours le même secours.</summary>
        public static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((path ?? "").ToLowerInvariant()));
                var sb = new StringBuilder();
                for (var i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in (name ?? "").Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            var safe = sb.ToString();
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            return safe.Length == 0 ? "projet" : safe;
        }
    }
}
