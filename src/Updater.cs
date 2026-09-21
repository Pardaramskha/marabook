using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Marabook
{
    /// <summary>« Vérifier les mises à jour » — le standard des apps de la
    /// famille Stargazer, porté de Typonanny (13/09/2026) : on interroge la
    /// dernière release GitHub du dépôt, on compare avec AppVersion, et on
    /// installe sur place — l'archive portable est déballée dans un dossier
    /// temporaire, puis un petit script cmd attend la fermeture de l'app,
    /// recopie les fichiers par-dessus et relance l'exe. Sans WPF : la
    /// vérification tourne sur un thread de fond, l'appelant marshale.
    ///
    /// TANT QU'AUCUNE RELEASE N'EST PUBLIÉE, la vérification répond « aucune
    /// version publiée » (404) — l'écran d'accueil le dit sans bruit. Les
    /// noms des assets suivent la convention de la famille : STABLES, sans
    /// numéro de version (le bouton du README pointe releases/latest).
    ///
    /// Dépôt privé : l'API répond 404 sans jeton — GITHUB_TOKEN (variable
    /// d'environnement) est envoyé s'il existe, pratique pour tester.</summary>
    public static class Updater
    {
        public const string Repository = "Pardaramskha/marabook";
        public const string RepositoryUrl = "https://github.com/" + Repository;
        public const string PortableZip = "marabook-windows-portable.zip";
        public const string Exe = "Marabook.exe";

        public sealed class Info
        {
            public string Version = "";  // « 0.43.0 »
            public string ZipUrl = "";   // l'archive portable Windows de la release
            public string AssetApiUrl = ""; // l'asset par l'API (dépôt privé : avec jeton, Accept octet-stream)
            public string PageUrl = "";  // la page de la release
            public string Notes = "";    // le texte de la release (Markdown brut)
        }

        /// <summary>Le résultat d'une vérification, lisible tel quel.</summary>
        public sealed class Check
        {
            public Info Latest;          // null si rien de publié ou pas de réseau
            public bool Available;       // une version plus récente que la nôtre
            public string Message = "";  // ce que l'écran affiche
        }

        /// <summary>Interroge GitHub et compare — jamais d'exception : le
        /// message dit ce qui s'est passé (aucune release, pas de réseau…).</summary>
        public static Check Run(string localVersion)
        {
            var check = new Check();
            try
            {
                var info = Latest();
                check.Latest = info;
                check.Available = IsNewer(info.Version, localVersion);
                check.Message = check.Available
                    ? "Marabook " + info.Version + " est disponible"
                    : "Vous avez la dernière version";
            }
            catch (Exception failure)
            {
                check.Message = failure.Message;
            }
            return check;
        }

        /// <summary>La dernière release publiée. Lève une exception parlante sinon.</summary>
        public static Info Latest()
        {
            return LatestOf(Repository, PortableZip);
        }

        /// <summary>La dernière release d'un dépôt quelconque (les modules,
        /// 22/09) et l'URL de l'asset au nom donné. Même chemin, mêmes messages.</summary>
        public static Info LatestOf(string repository, string assetName)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            string json;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Marabook";
                client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (!string.IsNullOrEmpty(token))
                    client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                client.Encoding = Encoding.UTF8;
                try
                {
                    json = client.DownloadString("https://api.github.com/repos/" + repository + "/releases/latest");
                }
                catch (WebException failure)
                {
                    var response = failure.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                        throw new Exception("Aucune version publiée pour l'instant");
                    if (response != null)
                        throw new Exception("GitHub répond " + (int)response.StatusCode + " " + response.StatusDescription);
                    throw new Exception("Pas de connexion à GitHub");
                }
            }
            var info = new Info
            {
                Version = Field(json, "tag_name").TrimStart('v', 'V'),
                PageUrl = Field(json, "html_url"),
                Notes = Field(json, "body")
            };
            // L'asset au nom voulu : son URL de téléchargement, et son URL
            // d'API (le champ « url » du même objet asset, qui précède).
            foreach (Match match in Regex.Matches(json, "\"url\"\\s*:\\s*\"([^\"]+/releases/assets/[0-9]+)\"[^{}]*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                if (match.Groups[2].Value.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase))
                {
                    info.AssetApiUrl = match.Groups[1].Value;
                    info.ZipUrl = match.Groups[2].Value;
                    break;
                }
            if (info.ZipUrl.Length == 0)
                foreach (Match match in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                    if (match.Groups[1].Value.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.ZipUrl = match.Groups[1].Value;
                        break;
                    }
            if (info.Version.Length == 0) throw new Exception("Réponse GitHub illisible");
            return info;
        }

        /// <summary>Un champ texte du JSON (premier trouvé), séquences
        /// d'échappement rendues — assez pour l'API des releases.</summary>
        private static string Field(string json, string name)
        {
            var match = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return "";
            return Regex.Replace(match.Groups[1].Value, @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match escape)
            {
                var s = escape.Groups[1].Value;
                switch (s[0])
                {
                    case 'n': return "\n";
                    case 'r': return "";
                    case 't': return "\t";
                    case 'u': return ((char)int.Parse(s.Substring(1), NumberStyles.HexNumber)).ToString();
                    default: return s;
                }
            });
        }

        /// <summary>« 0.43.0 » > « 0.42.0-alpha » ? Les trois nombres, le
        /// suffixe (alpha, beta) ignoré.</summary>
        public static bool IsNewer(string remote, string local)
        {
            var a = Numbers(remote);
            var b = Numbers(local);
            for (var i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        private static int[] Numbers(string version)
        {
            var result = new int[3];
            var parts = (version ?? "").Split('.');
            for (var i = 0; i < 3 && i < parts.Length; i++)
            {
                var match = Regex.Match(parts[i], @"\d+");
                result[i] = match.Success ? int.Parse(match.Value) : 0;
            }
            return result;
        }

        /// <summary>Télécharge l'archive, la déballe à côté, puis laisse un
        /// script cmd finir le travail une fois l'app fermée (l'exe est
        /// verrouillé tant qu'elle tourne). L'appelant ferme l'application
        /// juste après. Lève une exception parlante en cas d'échec.</summary>
        public static void Install(Info info, string appDir)
        {
            if (string.IsNullOrEmpty(info.ZipUrl))
                throw new Exception("La release ne contient pas " + PortableZip);
            var temp = Path.Combine(Path.GetTempPath(), "marabook-maj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var zip = Path.Combine(temp, PortableZip);
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Marabook";
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (!string.IsNullOrEmpty(token))
                    client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                client.DownloadFile(info.ZipUrl, zip);
            }
            var content = Path.Combine(temp, "contenu");
            Extract(zip, content);
            if (!File.Exists(Path.Combine(content, Exe)))
                throw new Exception("L'archive ne contient pas " + Exe);

            var script = Path.Combine(temp, "maj.cmd");
            var pid = Process.GetCurrentProcess().Id;
            var lines = new StringBuilder();
            lines.Append("@echo off\r\n");
            lines.Append(":attend\r\n");
            lines.Append("tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\n");
            lines.Append("if not errorlevel 1 (ping 127.0.0.1 -n 2 >nul & goto attend)\r\n");
            lines.Append("xcopy \"" + content + "\\*\" \"" + appDir.TrimEnd('\\') + "\\\" /E /Y /I /Q >nul\r\n");
            lines.Append("start \"\" \"" + Path.Combine(appDir, Exe) + "\"\r\n");
            lines.Append("cd /d \"%TEMP%\"\r\n");
            lines.Append("rmdir /s /q \"" + temp + "\"\r\n");
            File.WriteAllText(script, lines.ToString(), Encoding.Default);

            UpdateRegistry(appDir, info.Version);

            var start = new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(start);
        }

        /// <summary>Déballe en refusant les chemins qui sortent du dossier.</summary>
        private static void Extract(string zip, string folder)
        {
            Directory.CreateDirectory(folder);
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    var target = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
        }

        /// <summary>Si l'app a été posée par un Setup, Paramètres → Applications
        /// installées doit afficher la nouvelle version.</summary>
        private static void UpdateRegistry(string appDir, string version)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Marabook", true))
                {
                    if (key == null) return;
                    var location = key.GetValue("InstallLocation") as string;
                    if (location == null || !string.Equals(Path.GetFullPath(location).TrimEnd('\\'),
                        Path.GetFullPath(appDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    key.SetValue("DisplayVersion", version);
                }
            }
            catch { }
        }
    }
}
