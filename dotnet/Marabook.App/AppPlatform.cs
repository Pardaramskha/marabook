using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Ce qui varie d'un système à l'autre (IPlatform, P0) vu des
    /// trois OS : le dossier des données, l'interpréteur Python, LibreOffice.
    /// Windows garde ses règles historiques (%APPDATA%\Marabook, python\
    /// python.exe embarqué) ; Linux suit XDG, macOS ~/Library.</summary>
    public sealed class AppPlatform : IPlatform
    {
        /// <summary>Ouvre un fichier (ou une URL) avec l'application du
        /// système : ShellExecute sur Windows, open sur macOS, xdg-open sur Linux.</summary>
        /// <summary>Montre un fichier dans son dossier : l'Explorateur le
        /// sélectionne sur Windows ; ailleurs, le dossier s'ouvre.</summary>
        public static void RevealInFolder(string path)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else OpenWithShell(System.IO.Path.GetDirectoryName(path) ?? path);
        }

        public static void OpenWithShell(string path)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                System.Diagnostics.Process.Start("open", new[] { path });
            else
                System.Diagnostics.Process.Start("xdg-open", new[] { path });
        }

        public static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>Branche la plate-forme et le codec d'images sur le cœur.</summary>
        public static void Install()
        {
            Platform.Init();
            Platform.Current = new AppPlatform();
            Platform.Images = new AvaloniaImageCodec();
        }

        /// <summary>« Windows », « Linux » ou « macOS » — pour la barre d'état.</summary>
        public static string OsName
        {
            get { return IsWindows ? "Windows" : IsMac ? "macOS" : IsLinux ? "Linux" : RuntimeInformation.OSDescription; }
        }

        public string DataFolder
        {
            get
            {
                string folder;
                if (IsWindows)
                    folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook");
                else if (IsMac)
                    folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Marabook");
                else
                {
                    var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    var config = !string.IsNullOrEmpty(xdg) ? xdg
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                    folder = Path.Combine(config, "marabook");
                }
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public string PythonExecutable(string baseFolder)
        {
            if (IsWindows) return Path.Combine(baseFolder, "python", "python.exe");
            // Une distribution portable livrée à côté (python-build-standalone),
            // sinon le python3 du système.
            var bundled = Path.Combine(baseFolder, "python", "bin", "python3");
            return File.Exists(bundled) ? bundled : "python3";
        }

        public string FindLibreOffice()
        {
            string[] candidates;
            if (IsWindows)
                candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
                };
            else if (IsMac)
                candidates = new[] { "/Applications/LibreOffice.app/Contents/MacOS/soffice" };
            else
                candidates = new[] { "/usr/bin/soffice", "/usr/bin/libreoffice", "/snap/bin/libreoffice", "/usr/local/bin/soffice" };
            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }
    }

    /// <summary>Le codec d'images du cœur sur Avalonia (Skia derrière) : les
    /// dimensions par l'en-tête sinon par le décodage, le réencodage PNG. Les
    /// pixels RVB pour le PDF viendront avec la composition (P2).</summary>
    public sealed class AvaloniaImageCodec : IImageCodec
    {
        public bool TryGetSize(byte[] bytes, out int width, out int height)
        {
            if (ImageHeader.TryReadSize(bytes, out width, out height) && height > 0) return true;
            width = 0;
            height = 0;
            if (bytes == null) return false;
            try
            {
                using (var stream = new MemoryStream(bytes))
                using (var bitmap = new Bitmap(stream))
                {
                    width = bitmap.PixelSize.Width;
                    height = bitmap.PixelSize.Height;
                    return width > 0 && height > 0;
                }
            }
            catch { return false; }
        }

        /// <summary>Les pixels RVB24 sur blanc (le PDF), l'image réduite à
        /// maxWidthPx si elle est plus large — décodée par Skia.</summary>
        public byte[] ToRgb24(byte[] bytes, double maxWidthPx, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes == null) return null;
            try
            {
                int headerWidth, headerHeight;
                var known = TryGetSize(bytes, out headerWidth, out headerHeight);
                var target = maxWidthPx > 0 && known && headerWidth > maxWidthPx ? (int)Math.Round(maxWidthPx) : 0;
                using (var stream = new MemoryStream(bytes))
                using (var bitmap = target > 0 ? Bitmap.DecodeToWidth(stream, target) : new Bitmap(stream))
                    return AvaloniaFontEngine.Rgb24OverWhite(bitmap, out width, out height);
            }
            catch { return null; }
        }

        public byte[] ToPng(byte[] bytes)
        {
            if (bytes == null) return null;
            try
            {
                using (var stream = new MemoryStream(bytes))
                using (var bitmap = new Bitmap(stream))
                using (var output = new MemoryStream())
                {
                    bitmap.Save(output);
                    return output.ToArray();
                }
            }
            catch { return null; }
        }
    }
}
