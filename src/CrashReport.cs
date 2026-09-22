using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Marabook
{
    /// <summary>Les rapports de plantage (22/09) : à chaque erreur non
    /// rattrapée, un fichier Markdown lisible et partageable dans
    /// %APPDATA%\Marabook\rapports — ce que les testeurs de la bêta joignent à
    /// leur signalement. Aide › Rapports de plantage les liste. Le journal
    /// brut (crash.log) continue en parallèle.</summary>
    public static class CrashReport
    {
        public const string FolderName = "rapports";

        /// <summary>Le dossier des rapports (créé au besoin).</summary>
        public static string Folder
        {
            get
            {
                var folder = Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook"), FolderName);
                try { Directory.CreateDirectory(folder); } catch { }
                return folder;
            }
        }

        /// <summary>Écrit le rapport d'une erreur ; rend son chemin, ou null si
        /// même cela a échoué (rien ne doit remonter d'ici).</summary>
        public static string Write(Exception error, string version, IList<string> context)
        {
            try
            {
                var now = DateTime.Now;
                var path = Path.Combine(Folder, FileName(now));
                File.WriteAllText(path, Render(error, version, context, now), new UTF8Encoding(false));
                return path;
            }
            catch { return null; }
        }

        /// <summary>« plantage-2026-09-22-21h04m37.md » — se trie par date.</summary>
        public static string FileName(DateTime when)
        {
            return "plantage-" + when.ToString("yyyy-MM-dd-HH'h'mm'm'ss", CultureInfo.InvariantCulture) + ".md";
        }

        /// <summary>Le texte du rapport (pur : les tests le lisent).</summary>
        public static string Render(Exception error, string version, IList<string> context, DateTime when)
        {
            var sb = new StringBuilder();
            sb.Append("# Rapport de plantage Marabook\n\n");
            sb.Append("À joindre tel quel à un signalement : il ne contient ni texte de vos écrits, ni nom de fichier au-delà du projet ouvert.\n\n");
            sb.Append("| | |\n|---|---|\n");
            sb.Append("| Date | ").Append(when.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)).Append(" |\n");
            sb.Append("| Marabook | ").Append(version ?? "?").Append(" |\n");
            sb.Append("| Windows | ").Append(Environment.OSVersion.VersionString).Append(Environment.Is64BitOperatingSystem ? " (64 bits)" : " (32 bits)").Append(" |\n");
            sb.Append("| .NET | ").Append(Environment.Version).Append(" |\n");
            sb.Append("| Langue | ").Append(CultureInfo.CurrentUICulture.Name).Append(" |\n");
            if (context != null)
                foreach (var line in context)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var colon = line.IndexOf(" : ", StringComparison.Ordinal);
                    if (colon > 0) sb.Append("| ").Append(line.Substring(0, colon)).Append(" | ").Append(line.Substring(colon + 3)).Append(" |\n");
                    else sb.Append("| ").Append(line).Append(" | |\n");
                }
            sb.Append("\n## Erreur\n\n");
            if (error == null) sb.Append("(erreur inconnue)\n");
            else
            {
                var depth = 0;
                for (var current = error; current != null && depth < 8; current = current.InnerException, depth++)
                {
                    if (depth > 0) sb.Append("\n### Cause ").Append(depth).Append("\n\n");
                    sb.Append("**").Append(current.GetType().FullName).Append("** — ").Append(current.Message).Append("\n\n");
                    sb.Append("```\n").Append(current.StackTrace ?? "(pas de pile)").Append("\n```\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>Les rapports présents, du plus récent au plus ancien.</summary>
        public static List<string> List()
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(Folder, "plantage-*.md"));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                files.Reverse();
            }
            catch { }
            return files;
        }

        /// <summary>« 22/09/2026 21:04 — NullReferenceException » depuis le nom
        /// et la première ligne d'erreur du fichier.</summary>
        public static string Label(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path) ?? "";
            var label = name;
            DateTime when;
            if (name.StartsWith("plantage-") && DateTime.TryParseExact(name.Substring(9), "yyyy-MM-dd-HH'h'mm'm'ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                label = when.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (!line.StartsWith("**")) continue;
                    var end = line.IndexOf("**", 2, StringComparison.Ordinal);
                    if (end > 2)
                    {
                        var type = line.Substring(2, end - 2);
                        var dot = type.LastIndexOf('.');
                        label += " — " + (dot >= 0 ? type.Substring(dot + 1) : type);
                    }
                    break;
                }
            }
            catch { }
            return label;
        }

        public static void DeleteAll()
        {
            foreach (var path in List())
                try { File.Delete(path); } catch { }
        }
    }
}
