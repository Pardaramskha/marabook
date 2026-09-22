using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Marabook
{
    /// <summary>L'association des fichiers .plot (22/09) : au lancement, si
    /// aucune application n'ouvre les .plot, ou si celle inscrite a disparu
    /// (dossier déplacé, ancienne copie effacée), Marabook s'inscrit pour la
    /// session de l'utilisateur — clés HKCU seulement, aucune élévation. Une
    /// autre copie de Marabook encore présente garde la main : on ne se vole
    /// pas l'association entre copies. Même clés que tools\associate-plot.bat
    /// et que l'installeur (tools\setup-stub.cs).</summary>
    public static class FileAssociation
    {
        public const string Extension = ".plot";
        public const string ProgId = "Marabook.Project";
        public const string Description = "Projet Marabook";

        /// <summary>Inscrit Marabook si rien (ou plus rien) n'ouvre les .plot.
        /// Rend vrai si l'inscription vient d'être faite.</summary>
        public static bool EnsureRegistered()
        {
            try
            {
                var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Marabook.exe");
                if (!File.Exists(exe)) return false; // les exécutables de tests
                if (!NeedsRegistration(exe)) return false;
                Register(exe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Vrai s'il n'y a pas d'association, ou si l'exécutable
        /// qu'elle désigne n'existe plus.</summary>
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
    }
}
