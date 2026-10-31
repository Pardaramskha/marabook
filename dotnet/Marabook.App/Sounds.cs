using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES SONS (1.1.0, 31/10, Rémi) : trois sons discrets de Pixabay —
    /// un succès débloqué, l'enregistrement manuel, l'apparition d'un rapport
    /// d'erreur. Des WAV PCM 16 bits mono 44,1 kHz embarqués (convertis des
    /// .mp3 : aucun décodage, aucune latence), posés une fois dans le dossier
    /// de données puis joués sans attendre : winmm PlaySound sur Windows,
    /// afplay sur macOS, paplay ou aplay sur Linux. Préférences › Général les
    /// coupe (AppSettings.SoundsEnabled) ; la sonde les tait (Muted) ; les
    /// succès qui tombent ensemble ne sonnent qu'une fois.</summary>
    public static class Sounds
    {
        /// <summary>Vrai en sonde et en capture : pas un son.</summary>
        public static bool Muted;

        private const double SuccessQuietSeconds = 2.5;
        private static DateTime _lastSuccess = DateTime.MinValue;
        private static readonly object _gate = new object();

        public static void Success()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastSuccess).TotalSeconds < SuccessQuietSeconds) return; // plusieurs d'affilée : un seul son
                _lastSuccess = now;
            }
            Play("success");
        }

        public static void Saved() { Play("save"); }
        public static void Error() { Play("error"); }

        private static void Play(string name)
        {
            if (Muted || !AppSettings.SoundsEnabled) return;
            try
            {
                var path = Extract(name);
                if (path == null) return;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Launch("afplay", path);
                else if (!Launch("paplay", path))
                    Launch("aplay", "-q " + Quote(path));
            }
            catch (Exception) { } // un son qui ne part pas n'arrête rien
        }

        private static bool Launch(string program, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(program, arguments.StartsWith("\"") || arguments.StartsWith("-") ? arguments : Quote(arguments))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                var process = Process.Start(info);
                if (process == null) return false;
                process.EnableRaisingEvents = true;
                process.Exited += delegate { process.Dispose(); };
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string Quote(string path) { return "\"" + path + "\""; }

        /// <summary>Le fichier WAV posé dans le dossier de données (une fois,
        /// ou si sa taille a changé avec une version), depuis la ressource.</summary>
        private static string Extract(string name)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "sounds");
            var path = Path.Combine(folder, name + ".wav");
            var assembly = typeof(Sounds).Assembly;
            using (var stream = assembly.GetManifestResourceStream("sounds/" + name + ".wav"))
            {
                if (stream == null) return null;
                var existing = new FileInfo(path);
                if (existing.Exists && existing.Length == stream.Length) return path;
                Directory.CreateDirectory(folder);
                var temp = path + ".tmp";
                using (var file = File.Create(temp)) stream.CopyTo(file);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            return path;
        }

        private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_FILENAME = 0x00020000;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PlaySound(string sound, IntPtr module, uint flags);
    }
}
