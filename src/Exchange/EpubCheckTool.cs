using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Marabook.Exchange
{
    /// <summary>Ce qu'EPUBCheck dit d'un EPUB.</summary>
    public class EpubCheckReport
    {
        public bool Ran;                 // l'outil a tourné et rendu son rapport
        public string Version = "";
        public string Failure = "";      // pourquoi il n'a pas tourné
        public readonly List<string> Errors = new List<string>();    // FATAL et ERROR : une librairie refuse
        public readonly List<string> Warnings = new List<string>();  // WARNING : elle accepte, mais le dit

        public bool Valid { get { return Ran && Errors.Count == 0; } }
    }

    /// <summary>Comment lancer EPUBCheck sur cette machine.</summary>
    public class EpubCheckCommand
    {
        public string FileName = "";     // « java », ou le lanceur « epubcheck »
        public string Jar;               // le .jar, quand c'est java qui le lance
        public string Describe() { return Jar ?? FileName; }
    }

    /// <summary>EPUBCheck, le validateur officiel du W3C (20/11) : Marabook
    /// ne l'embarque pas (il lui faut Java), mais s'en sert quand il est
    /// installé — le lanceur « epubcheck » dans le PATH, ou un epubcheck.jar
    /// avec Java. Sans lui, le contrôle maison (EpubCheck.Verify) reste seul.</summary>
    public static class EpubCheckTool
    {
        /// <summary>La variable d'environnement qui désigne le .jar ou le lanceur.</summary>
        public const string Variable = "EPUBCHECK";

        private static bool _searched;
        private static EpubCheckCommand _found;

        /// <summary>Oublie ce qui a été trouvé (les tests, et qui vient d'installer l'outil).</summary>
        public static void Forget() { _searched = false; _found = null; }

        /// <summary>L'outil de cette machine, ou null. Cherché une fois.</summary>
        public static EpubCheckCommand Find()
        {
            if (_searched) return _found;
            _searched = true;
            try { _found = Search(Environment.GetEnvironmentVariable(Variable), PathFolders(), JarFolders()); }
            catch { _found = null; }
            return _found;
        }

        private static List<string> PathFolders()
        {
            var folders = new List<string>();
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(Path.PathSeparator))
            {
                var folder = part.Trim().Trim('"');
                if (folder.Length > 0) folders.Add(folder);
            }
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome)) folders.Add(Path.Combine(javaHome, "bin"));
            return folders;
        }

        /// <summary>Où un epubcheck.jar se pose d'ordinaire : le dossier
        /// « outils » des données de Marabook, le dossier personnel, les
        /// dossiers de programmes.</summary>
        private static List<string> JarFolders()
        {
            var folders = new List<string>();
            try { folders.Add(Path.Combine(Marabook.Model.Platform.Current.DataFolder, "outils")); } catch { }
            foreach (var special in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                try
                {
                    var folder = Environment.GetFolderPath(special);
                    if (string.IsNullOrEmpty(folder)) continue;
                    folders.Add(folder);
                    if (special == Environment.SpecialFolder.LocalApplicationData) folders.Add(Path.Combine(folder, "Programs"));
                }
                catch { }
            }
            folders.Add("/usr/share/java");
            folders.Add("/usr/local/share/java");
            folders.Add("/opt");
            return folders;
        }

        /// <summary>La recherche, sur des dossiers donnés (les tests en
        /// donnent de faux) : la variable d'abord, puis un lanceur dans le
        /// PATH, puis un .jar rangé dans un dossier « epubcheck… » — qui
        /// demande Java.</summary>
        public static EpubCheckCommand Search(string variable, IList<string> pathFolders, IList<string> jarFolders)
        {
            var java = Executable(pathFolders, "java");
            variable = (variable ?? "").Trim().Trim('"');
            if (variable.Length > 0 && File.Exists(variable))
            {
                if (variable.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return java == null ? null : new EpubCheckCommand { FileName = java, Jar = variable };
                return new EpubCheckCommand { FileName = variable };
            }
            var launcher = Executable(pathFolders, "epubcheck");
            if (launcher != null) return new EpubCheckCommand { FileName = launcher };
            if (java == null) return null;
            foreach (var folder in jarFolders ?? new List<string>())
            {
                var jar = JarIn(folder);
                if (jar != null) return new EpubCheckCommand { FileName = java, Jar = jar };
            }
            return null;
        }

        private static string Executable(IList<string> folders, string name)
        {
            if (folders == null) return null;
            var windows = Path.DirectorySeparatorChar == '\\';
            var names = windows ? new[] { name + ".exe", name + ".cmd", name + ".bat" } : new[] { name };
            foreach (var folder in folders)
                foreach (var candidate in names)
                {
                    try
                    {
                        var path = Path.Combine(folder, candidate);
                        if (File.Exists(path)) return path;
                    }
                    catch { }
                }
            return null;
        }

        /// <summary>epubcheck.jar dans ce dossier, ou dans un de ses
        /// sous-dossiers « epubcheck… » (l'archive du W3C se déballe en
        /// « epubcheck-5.1.0 ») — le plus récent par le nom.</summary>
        private static string JarIn(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                var direct = Path.Combine(folder, "epubcheck.jar");
                if (File.Exists(direct)) return direct;
                var subs = new List<string>(Directory.GetDirectories(folder, "epubcheck*"));
                subs.Sort(StringComparer.OrdinalIgnoreCase);
                for (var i = subs.Count - 1; i >= 0; i--)
                {
                    var jar = Path.Combine(subs[i], "epubcheck.jar");
                    if (File.Exists(jar)) return jar;
                }
            }
            catch { }
            return null;
        }

        private static string Quote(string argument)
        {
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Passe cet EPUB à EPUBCheck. Ne lève jamais : un outil
        /// qui ne tourne pas le dit dans Failure.</summary>
        public static EpubCheckReport Run(EpubCheckCommand command, string epubPath, int timeoutMs = 120000)
        {
            var report = new EpubCheckReport();
            if (command == null) { report.Failure = "EPUBCheck n'est pas installé."; return report; }
            var json = Path.Combine(Path.GetTempPath(), "marabook-epubcheck-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var arguments = (command.Jar != null ? "-jar " + Quote(command.Jar) + " " : "") + Quote(epubPath) + " --json " + Quote(json) + " --locale en";
                var info = new ProcessStartInfo
                {
                    FileName = command.FileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(info))
                {
                    // Les deux sorties sont vidées au fil de l'eau : un tampon plein bloquerait l'outil.
                    process.OutputDataReceived += delegate { };
                    process.ErrorDataReceived += delegate { };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        report.Failure = "EPUBCheck n'a pas répondu en " + (timeoutMs / 1000) + " secondes.";
                        return report;
                    }
                    process.WaitForExit();
                }
                if (!File.Exists(json)) { report.Failure = "EPUBCheck n'a pas rendu de rapport."; return report; }
                return Parse(File.ReadAllText(json, Encoding.UTF8));
            }
            catch (Exception error)
            {
                report.Failure = "EPUBCheck n'a pas pu être lancé : " + error.Message;
                return report;
            }
            finally
            {
                try { if (File.Exists(json)) File.Delete(json); } catch { }
            }
        }

        /// <summary>Lit le rapport JSON d'EPUBCheck (--json).</summary>
        public static EpubCheckReport Parse(string json)
        {
            var report = new EpubCheckReport();
            object root;
            try { root = Json.Parse(json); }
            catch { root = null; }
            var o = Json.AsObject(root);
            if (o == null) { report.Failure = "Le rapport d'EPUBCheck est illisible."; return report; }
            report.Ran = true;
            report.Version = Json.AsString(Json.Field(Json.Field(o, "checker"), "checkerVersion")) ?? "";
            var messages = Json.AsList(Json.Field(o, "messages"));
            if (messages == null) return report;
            foreach (var node in messages)
            {
                var severity = (Json.AsString(Json.Field(node, "severity")) ?? "").ToUpperInvariant();
                if (severity != "FATAL" && severity != "ERROR" && severity != "WARNING") continue;
                var line = new StringBuilder();
                var id = Json.AsString(Json.Field(node, "ID")) ?? "";
                if (id.Length > 0) line.Append(id).Append(" — ");
                line.Append((Json.AsString(Json.Field(node, "message")) ?? "").Trim());
                var locations = Json.AsList(Json.Field(node, "locations"));
                if (locations != null && locations.Count > 0)
                {
                    var path = Json.AsString(Json.Field(locations[0], "path")) ?? "";
                    var at = Json.AsInt(Json.Field(locations[0], "line"), -1);
                    if (path.Length > 0)
                    {
                        line.Append(" (").Append(path);
                        if (at > 0) line.Append(", ligne ").Append(at);
                        if (locations.Count > 1) line.Append(", et ").Append(locations.Count - 1).Append(locations.Count > 2 ? " autres endroits" : " autre endroit");
                        line.Append(')');
                    }
                }
                (severity == "WARNING" ? report.Warnings : report.Errors).Add(line.ToString());
            }
            return report;
        }
    }
}
