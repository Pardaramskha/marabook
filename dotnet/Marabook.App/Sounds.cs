using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES SONS (1.1.0, 31/10, Rémi) : quatre sons discrets de Pixabay —
    /// un succès débloqué, l'enregistrement manuel, l'apparition d'un rapport
    /// d'erreur, une notification (snackbar). Des WAV PCM 16 bits mono
    /// 44,1 kHz embarqués (convertis des .mp3 : aucun décodage, aucune
    /// latence), posés une fois dans le dossier de données puis joués sans
    /// attendre. Sur Windows, chaque son a SA VOIX (MCI waveaudio, un alias
    /// par lecture, refermé après sa durée) : PlaySound n'en avait qu'une et
    /// coupait le son en cours pour jouer le suivant — le grésillement du
    /// « succès puis enregistrement » (31/10). macOS : afplay ; Linux :
    /// paplay ou aplay. Préférences › Général coupe tout
    /// (AppSettings.SoundsEnabled) ; les captures se taisent (Muted) ; les
    /// succès qui tombent ensemble ne sonnent qu'une fois.</summary>
    public static class Sounds
    {
        /// <summary>Vrai en capture (et, hors essai, en sonde) : pas un son.</summary>
        public static bool Muted;

        private const double SuccessQuietSeconds = 2.5;
        private static DateTime _lastSuccess = DateTime.MinValue;
        private static readonly object _gate = new object();
        private static int _voice; // le numéro du prochain alias MCI

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
        public static void Notification() { Play("notification"); }

        private static void Play(string name)
        {
            if (Muted || !AppSettings.SoundsEnabled) return;
            try
            {
                var path = Extract(name);
                if (path == null) return;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlayWindows(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Launch("afplay", Quote(path));
                else if (!Launch("paplay", Quote(path)))
                    Launch("aplay", "-q " + Quote(path));
            }
            catch (Exception) { } // un son qui ne part pas n'arrête rien
        }

        /// <summary>Une voix MCI par lecture : « open … alias vN », « play »,
        /// puis « close » une fois la durée écoulée (lue à MCI) — deux sons
        /// qui se suivent ou se chevauchent ne se coupent plus. Si MCI refuse
        /// (pilote absent), PlaySound reste la roue de secours.</summary>
        private static void PlayWindows(string path)
        {
            var alias = "marabook" + Interlocked.Increment(ref _voice);
            if (mciSendString("open \"" + path + "\" type waveaudio alias " + alias, null, 0, IntPtr.Zero) != 0)
            {
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                return;
            }
            var length = new StringBuilder(32);
            mciSendString("status " + alias + " length", length, length.Capacity, IntPtr.Zero);
            int ms;
            if (!int.TryParse(length.ToString(), out ms) || ms <= 0) ms = 4000;
            if (mciSendString("play " + alias, null, 0, IntPtr.Zero) != 0)
            {
                mciSendString("close " + alias, null, 0, IntPtr.Zero);
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                return;
            }
            Task.Delay(ms + 300).ContinueWith(delegate
            {
                try { mciSendString("close " + alias, null, 0, IntPtr.Zero); } catch (Exception) { }
            });
        }

        private static bool Launch(string program, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(program, arguments)
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

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int mciSendString(string command, StringBuilder result, int resultLength, IntPtr callback);
    }
}
