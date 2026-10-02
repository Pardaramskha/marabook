using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Win32;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>« Vérifier les mises à jour » — le standard des apps de la
    /// famille Stargazer, porté de Typonanny (13/09/2026) : on interroge la
    /// dernière release GitHub du dépôt, on compare avec AppVersion, et on
    /// installe sur place — l'archive portable est déballée dans un dossier
    /// temporaire, puis un petit script cmd attend la fermeture de l'app,
    /// recopie les fichiers par-dessus et relance l'exe. Sans WPF : la
    /// vérification tourne sur un thread de fond, l'appelant marshale.
    ///
    /// L'écran d'accueil montre le verdict — « Vous êtes à jour » ou « Nouvelle
    /// version X disponible », cliquable vers UpdateNotesDialog (02/10) ; le
    /// texte de la release est patchnotes/&lt;version&gt;.md, publié par le workflow. Les
    /// noms des assets suivent la convention de la famille : STABLES, sans
    /// numéro de version (le bouton du README pointe releases/latest).
    ///
    /// Dépôt privé : l'API répond 404 sans jeton — GITHUB_TOKEN (variable
    /// d'environnement) est envoyé s'il existe, pratique pour tester.</summary>
    public static class Updater
    {
        public const string Repository = "Pardaramskha/marabook";
        public const string RepositoryUrl = "https://github.com/" + Repository;
        /// <summary>L'asset de CE système (P4) : les noms sont stables d'une
        /// release à l'autre — celui de Windows est celui de la 0.43, que
        /// l'Updater 0.43 sait déjà télécharger.</summary>
        public static string PortableZip
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "marabook-windows-portable.zip";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "marabook-macos-arm64.zip" : "marabook-macos-x64.zip";
                return "marabook-linux-x64.tar.gz";
            }
        }

        public static string Exe
        {
            get { return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Marabook.exe" : "Marabook"; }
        }

        public sealed class Info
        {
            public string Version = "";  // « 0.43.0 »
            public string ZipUrl = "";   // l'archive portable Windows de la release
            public string AssetApiUrl = ""; // l'asset par l'API (dépôt privé : avec jeton, Accept octet-stream)
            public string PageUrl = "";  // la page de la release
            public string Notes = "";    // le texte de la release (Markdown brut) — les patch notes de Rémi (patchnotes/<version>.md)
            public string PublishedAt = ""; // « 2026-10-02T09:12:33Z » (02/10)
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
                Notes = Field(json, "body"),
                PublishedAt = Field(json, "published_at")
            };
            // L'asset au nom voulu : son URL de téléchargement, et son URL
            // d'API — le dernier champ « url …/releases/assets/N » qui la
            // précède dans le JSON (l'objet asset commence par lui ; son
            // « uploader » imbriqué interdit une seule expression).
            var apiUrls = Regex.Matches(json, "\"url\"\\s*:\\s*\"([^\"]+/releases/assets/[0-9]+)\"");
            foreach (Match match in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                if (match.Groups[1].Value.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase))
                {
                    info.ZipUrl = match.Groups[1].Value;
                    foreach (Match api in apiUrls)
                        if (api.Index < match.Index) info.AssetApiUrl = api.Groups[1].Value;
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
            Install(Prepare(info), appDir);
        }

        /// <summary>Une mise à jour TÉLÉCHARGÉE ET DÉBALLÉE (01/10) : prête à
        /// être posée par Install, ou oubliée (Discard). Le lancement la
        /// prépare en silence ; le toast et le menu Aide l'installent.</summary>
        public sealed class Prepared
        {
            public Info Info;
            public string Temp;    // le dossier de travail (archive + contenu + script)
            public string Content; // le contenu déballé, Marabook(.exe) à sa racine

            public void Discard()
            {
                try { if (Directory.Exists(Temp)) Directory.Delete(Temp, true); } catch { }
            }
        }

        /// <summary>Télécharge l'archive de la release et la déballe dans un
        /// dossier temporaire — sur un fil de fond, sans toucher à
        /// l'application qui tourne. Lève une exception parlante en cas
        /// d'échec (le dossier est alors nettoyé).</summary>
        public static Prepared Prepare(Info info)
        {
            if (string.IsNullOrEmpty(info.ZipUrl))
                throw new Exception("La release ne contient pas " + PortableZip);
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-maj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var prepared = new Prepared { Info = info, Temp = temp, Content = System.IO.Path.Combine(temp, "contenu") };
            try
            {
                var zip = System.IO.Path.Combine(temp, PortableZip);
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Marabook";
                    // Un jeton ne s'envoie qu'à l'API de l'asset : l'URL publique
                    // redirige vers un stockage qui refuse l'en-tête d'autorisation
                    // (revue 22/09 ; même logique que ModuleStore.Download).
                    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(info.AssetApiUrl))
                    {
                        client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                        client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                        client.DownloadFile(info.AssetApiUrl, zip);
                    }
                    else client.DownloadFile(info.ZipUrl, zip);
                }
                Extract(zip, prepared.Content);
                if (!File.Exists(System.IO.Path.Combine(prepared.Content, Exe)))
                    throw new Exception("L'archive ne contient pas " + Exe);
                try { File.Delete(zip); } catch { } // le contenu suffit ; l'archive pesait 60 Mo
                return prepared;
            }
            catch
            {
                prepared.Discard();
                throw;
            }
        }

        /// <summary>Pose une mise à jour préparée : écrit le script qui finit
        /// le travail une fois l'app fermée et le lance. L'appelant ferme
        /// l'application juste après.</summary>
        public static void Install(Prepared prepared, string appDir)
        {
            var info = prepared.Info;
            var temp = prepared.Temp;
            var content = prepared.Content;
            if (!File.Exists(System.IO.Path.Combine(content, Exe)))
                throw new Exception("La mise à jour préparée a disparu (" + content + ")");

            // Le script qui finit le travail une fois l'app fermée (P4, trois
            // OS) : SAUVEGARDE de l'ancien dossier, copie par-dessus, relance
            // — et RETOUR ARRIÈRE si le nouvel exécutable s'arrête dans les
            // dix secondes (une archive incomplète, un runtime qui manque).
            var pid = Process.GetCurrentProcess().Id;
            var backup = appDir.TrimEnd('\\', '/') + ".avant-maj";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = System.IO.Path.Combine(temp, "maj.cmd");
                var app = appDir.TrimEnd('\\');
                var exe = System.IO.Path.Combine(app, Exe);
                var lines = new StringBuilder();
                lines.Append("@echo off\r\n");
                lines.Append(":attend\r\n");
                lines.Append("tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\n");
                lines.Append("if not errorlevel 1 (ping 127.0.0.1 -n 2 >nul & goto attend)\r\n");
                lines.Append("if exist \"" + backup + "\" rmdir /s /q \"" + backup + "\"\r\n");
                lines.Append("xcopy \"" + app + "\\*\" \"" + backup + "\\\" /E /Y /I /Q >nul\r\n");
                lines.Append("xcopy \"" + content + "\\*\" \"" + app + "\\\" /E /Y /I /Q >nul\r\n");
                lines.Append("start \"\" \"" + exe + "\"\r\n");
                lines.Append("ping 127.0.0.1 -n 11 >nul\r\n");
                lines.Append("tasklist /FI \"IMAGENAME eq " + Exe + "\" 2>nul | find /I \"" + Exe + "\" >nul\r\n");
                lines.Append("if errorlevel 1 (\r\n");
                lines.Append("  xcopy \"" + backup + "\\*\" \"" + app + "\\\" /E /Y /I /Q >nul\r\n");
                lines.Append("  start \"\" \"" + exe + "\" --maj-annulee\r\n");
                lines.Append(") else (\r\n");
                lines.Append("  rmdir /s /q \"" + backup + "\"\r\n");
                lines.Append(")\r\n");
                lines.Append("cd /d \"%TEMP%\"\r\n");
                lines.Append("rmdir /s /q \"" + temp + "\"\r\n");
                File.WriteAllText(script, lines.ToString(), Encoding.Default);

                UpdateRegistry(appDir, info.Version);

                var start = new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };
                Process.Start(start);
            }
            else
            {
                var script = System.IO.Path.Combine(temp, "maj.sh");
                var app = appDir.TrimEnd('/');
                var exe = System.IO.Path.Combine(app, Exe);
                var lines = new StringBuilder();
                lines.Append("#!/bin/sh\n");
                lines.Append("while kill -0 " + pid + " 2>/dev/null; do sleep 1; done\n");
                lines.Append("rm -rf '" + backup + "'\n");
                lines.Append("cp -a '" + app + "' '" + backup + "'\n");
                lines.Append("cp -a '" + content + "/.' '" + app + "/'\n");
                lines.Append("chmod +x '" + exe + "'\n");
                lines.Append("'" + exe + "' &\n");
                lines.Append("nouveau=$!\n");
                lines.Append("sleep 10\n");
                lines.Append("if kill -0 $nouveau 2>/dev/null; then rm -rf '" + backup + "'; else\n");
                lines.Append("  cp -a '" + backup + "/.' '" + app + "/'\n");
                lines.Append("  '" + exe + "' --maj-annulee &\n");
                lines.Append("fi\n");
                lines.Append("rm -rf '" + temp + "'\n");
                File.WriteAllText(script, lines.ToString(), new UTF8Encoding(false));
                var start = new ProcessStartInfo("/bin/sh", "'" + script + "'")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };
                Process.Start(start);
            }
        }

        /// <summary>Déballe en refusant les chemins qui sortent du dossier ;
        /// une archive .tar.gz (Linux) passe par tar, qui garde le bit
        /// d'exécution.</summary>
        private static void Extract(string zip, string folder)
        {
            Directory.CreateDirectory(folder);
            if (zip.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var tar = new ProcessStartInfo("tar", "-xzf '" + zip + "' -C '" + folder + "'") { UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(tar))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new Exception("tar n'a pas pu déballer l'archive");
                }
                return;
            }
            var root = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, entry.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
        }

        /// <summary>Si l'app a été posée par un Setup, Paramètres → Applications
        /// installées doit afficher la nouvelle version.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void UpdateRegistry(string appDir, string version)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Marabook", true))
                {
                    if (key == null) return;
                    var location = key.GetValue("InstallLocation") as string;
                    if (location == null || !string.Equals(System.IO.Path.GetFullPath(location).TrimEnd('\\'),
                        System.IO.Path.GetFullPath(appDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    key.SetValue("DisplayVersion", version);
                }
            }
            catch { }
        }
    }
}
