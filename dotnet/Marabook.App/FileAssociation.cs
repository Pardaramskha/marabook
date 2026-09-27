using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Marabook.App
{
    /// <summary>L'association des fichiers .plot (22/09, trois OS en P4) : au
    /// lancement, si aucune application n'ouvre les .plot, ou si celle
    /// inscrite a disparu (dossier déplacé, ancienne copie effacée), Marabook
    /// s'inscrit pour la session de l'utilisateur — jamais d'élévation.
    /// Windows : les clés HKCU (les mêmes que tools\associate-plot.bat et
    /// que l'installeur). Linux : un lanceur .desktop et un type MIME dans
    /// ~/.local/share (xdg-mime). macOS : rien ici — l'association vient de
    /// l'Info.plist du bundle, posé par le script de publication.
    /// Une autre copie de Marabook encore présente garde la main : on ne se
    /// vole pas l'association entre copies.</summary>
    public static class FileAssociation
    {
        public const string Extension = ".plot";
        public const string ProgId = "Marabook.Project";
        public const string Description = "Projet Marabook";
        public const string MimeType = "application/x-marabook-project";

        /// <summary>Inscrit Marabook si rien (ou plus rien) n'ouvre les .plot.
        /// Rend vrai si l'inscription vient d'être faite.</summary>
        public static bool EnsureRegistered()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return EnsureWindows();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return EnsureLinux();
                return false;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ Windows

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool EnsureWindows()
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "Marabook.exe");
            if (!File.Exists(exe)) return false; // les exécutables de tests
            if (!NeedsRegistration(exe)) return false;
            Register(exe);
            return true;
        }

        /// <summary>Vrai s'il n'y a pas d'association, ou si l'exécutable
        /// qu'elle désigne n'existe plus.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool NeedsRegistration(string exe)
        {
            using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes"))
            {
                if (classes == null) return true;
                string progId;
                using (var ext = classes.OpenSubKey(Extension))
                    progId = ext == null ? null : ext.GetValue(null) as string;
                if (string.IsNullOrEmpty(progId)) return true;
                using (var command = classes.OpenSubKey(progId + @"\shell\open\command"))
                {
                    var value = command == null ? null : command.GetValue(null) as string;
                    if (string.IsNullOrEmpty(value)) return true;
                    var target = CommandTarget(value);
                    if (target == null) return false; // une commande qu'on ne sait pas lire : on n'y touche pas
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return false;
                    return !File.Exists(target); // une autre copie vivante garde la main
                }
            }
        }

        /// <summary>L'exécutable d'une commande « "C:\…\x.exe" "%1" ».</summary>
        private static string CommandTarget(string command)
        {
            var text = command.Trim();
            if (text.StartsWith("\""))
            {
                var end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : null;
            }
            var space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }

        /// <summary>Les clés HKCU : extension → ProgId, libellé, icône
        /// (assets\plot.ico s'il est là, sinon l'icône de l'exécutable), commande.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static void Register(string exe)
        {
            var folder = Path.GetDirectoryName(exe) ?? "";
            var icon = Path.Combine(folder, Path.Combine("assets", "plot.ico"));
            using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
            {
                using (var key = classes.CreateSubKey(Extension)) key.SetValue(null, ProgId);
                using (var key = classes.CreateSubKey(ProgId)) key.SetValue(null, Description);
                using (var key = classes.CreateSubKey(ProgId + @"\DefaultIcon"))
                    key.SetValue(null, File.Exists(icon) ? icon + ",0" : exe + ",0");
                using (var key = classes.CreateSubKey(ProgId + @"\shell\open\command"))
                    key.SetValue(null, "\"" + exe + "\" \"%1\"");
            }
            RefreshShell();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        private static void RefreshShell()
        {
            try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { } // SHCNE_ASSOCCHANGED
        }

        // ------------------------------------------------------------ Linux (XDG)

        /// <summary>~/.local/share/applications/marabook.desktop et
        /// ~/.local/share/mime/packages/marabook.xml, puis xdg-mime ; réécrits
        /// quand l'exécutable désigné n'existe plus (copie déplacée).</summary>
        private static bool EnsureLinux()
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "Marabook");
            if (!File.Exists(exe)) return false;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var share = Path.Combine(home, ".local", "share");
            var desktop = Path.Combine(share, "applications", "marabook.desktop");
            if (File.Exists(desktop))
            {
                var current = File.ReadAllText(desktop);
                var line = current.IndexOf("Exec=", StringComparison.Ordinal);
                if (line >= 0)
                {
                    var end = current.IndexOf('\n', line);
                    var command = (end < 0 ? current.Substring(line + 5) : current.Substring(line + 5, end - line - 5)).Trim();
                    var target = CommandTarget(command);
                    if (target != null && (target == exe || File.Exists(target))) return false;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(desktop));
            var icon = Path.Combine(AppContext.BaseDirectory, "assets", "marabook.png");
            File.WriteAllText(desktop,
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Marabook\n" +
                "Comment=Traitement de texte et construction narrative pour les romans\n" +
                "Exec=\"" + exe + "\" %f\n" +
                (File.Exists(icon) ? "Icon=" + icon + "\n" : "") +
                "Terminal=false\n" +
                "Categories=Office;WordProcessor;\n" +
                "MimeType=" + MimeType + ";\n");
            var mime = Path.Combine(share, "mime", "packages", "marabook.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(mime));
            File.WriteAllText(mime,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">\n" +
                "  <mime-type type=\"" + MimeType + "\">\n" +
                "    <comment>" + Description + "</comment>\n" +
                "    <glob pattern=\"*" + Extension + "\"/>\n" +
                "  </mime-type>\n" +
                "</mime-info>\n");
            Run("update-mime-database", Path.Combine(share, "mime"));
            Run("update-desktop-database", Path.Combine(share, "applications"));
            Run("xdg-mime", "default marabook.desktop " + MimeType);
            return true;
        }

        private static void Run(string command, string arguments)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(command, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = System.Diagnostics.Process.Start(start))
                    if (process != null) process.WaitForExit(5000);
            }
            catch { }
        }
    }
}
