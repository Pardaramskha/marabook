using System;
using System.Diagnostics;
using System.Text;
using Marabook.Correction;

namespace Marabook.App
{
    /// <summary>LA VOIX (21/11) : la synthèse vocale DU SYSTÈME, lancée dans
    /// un processus à part que l'on tue pour l'arrêter. Windows : la voix
    /// SAPI, par Windows PowerShell (présent d'office) ; macOS : « say » ;
    /// Linux : spd-say, sinon espeak-ng, sinon espeak. Le texte passe par
    /// l'entrée standard — jamais par la ligne de commande, jamais par un
    /// fichier. Rien n'est embarqué et rien ne sort de la machine.</summary>
    public static class ReadAloud
    {
        private static Process _process;
        private static readonly object Gate = new object();

        /// <summary>Sonde et tests : on retient le texte, on ne parle pas.</summary>
        public static bool Dry;
        public static string LastText { get; private set; }

        /// <summary>La lecture commence ou s'arrête (peut venir d'un autre fil).</summary>
        public static event Action Changed;

        public static bool IsReading
        {
            get { lock (Gate) return _dryReading || (_process != null && !SafeExited(_process)); }
        }

        private static bool _dryReading;

        private static bool SafeExited(Process process)
        {
            try { return process.HasExited; }
            catch { return true; }
        }

        // Windows PowerShell 5.1 porte System.Speech ; une voix française est préférée s'il y en a une.
        private const string WindowsScript =
            "[Console]::InputEncoding = [System.Text.Encoding]::UTF8; $t = [Console]::In.ReadToEnd(); "
            + "Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            + "try { $v = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'fr*' } | Select-Object -First 1; "
            + "if ($v) { $s.SelectVoice($v.VoiceInfo.Name) } } catch {}; $s.Speak($t)";

        /// <summary>Lit ce texte (déjà préparé ou non). Rend null si la voix
        /// part, sinon ce qui a manqué, dit pour l'auteur.</summary>
        public static string Start(string text)
        {
            Stop();
            var prepared = ReadAloudText.Prepare(text);
            if (prepared.Length == 0) return "Rien à lire ici.";
            LastText = prepared;
            if (Dry)
            {
                lock (Gate) _dryReading = true;
                Raise();
                return null;
            }
            string[][] candidates;
            if (AppPlatform.IsWindows)
                candidates = new[] { new[] { "powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript)) } };
            else if (AppPlatform.IsMac)
                candidates = new[] { new[] { "say" } };
            else
                candidates = new[] { new[] { "spd-say", "-e", "-w", "-l", "fr" }, new[] { "espeak-ng", "-v", "fr", "--stdin" }, new[] { "espeak", "-v", "fr", "--stdin" } };
            foreach (var candidate in candidates)
            {
                Process process = null;
                try
                {
                    var info = new ProcessStartInfo(candidate[0])
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
                        StandardInputEncoding = new UTF8Encoding(false)
                    };
                    for (var i = 1; i < candidate.Length; i++) info.ArgumentList.Add(candidate[i]);
                    process = new Process { StartInfo = info, EnableRaisingEvents = true };
                    process.Exited += delegate { Raise(); };
                    if (!process.Start()) continue;
                    process.ErrorDataReceived += delegate { };
                    process.OutputDataReceived += delegate { };
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    process.StandardInput.Write(prepared);
                    process.StandardInput.Close();
                    lock (Gate) _process = process;
                    Raise();
                    return null;
                }
                catch (Exception)
                {
                    try { if (process != null) process.Dispose(); } catch { }
                }
            }
            return AppPlatform.IsWindows ? "La synthèse vocale de Windows n'a pas pu démarrer."
                : AppPlatform.IsMac ? "La commande « say » de macOS n'a pas pu démarrer."
                : "Aucune synthèse vocale trouvée : installez speech-dispatcher (spd-say) ou espeak-ng.";
        }

        public static void Stop()
        {
            Process process;
            bool was;
            lock (Gate)
            {
                process = _process;
                _process = null;
                was = _dryReading || process != null;
                _dryReading = false;
            }
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                try { process.Dispose(); } catch { }
            }
            if (was) Raise();
        }

        private static void Raise()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); } catch { }
        }
    }
}
