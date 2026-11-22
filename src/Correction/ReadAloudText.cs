using System;
using System.Text;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>LA LECTURE À VOIX HAUTE (21/11) : le texte tel qu'une voix
    /// de synthèse doit le recevoir. La notation des [[liens]] disparaît
    /// (les mots affichés restent), les images et les notes ancrées aussi,
    /// un paragraphe vaut une pause, et la lecture s'arrête à une fin de
    /// phrase passé un plafond — une voix ne lit pas un roman d'une traite.
    /// La voix elle-même est celle du système : rien n'est embarqué, rien
    /// ne sort de la machine.</summary>
    public static class ReadAloudText
    {
        /// <summary>Au-delà (≈ 25 minutes de lecture), on s'arrête à la fin de la phrase.</summary>
        public const int MaxChars = 24000;

        public static string Prepare(string text, int maxChars = MaxChars)
        {
            var clean = Links.Strip(text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("￼", "");
            var sb = new StringBuilder(clean.Length);
            var blank = true; // début de ligne : on rogne les blancs de tête
            foreach (var c in clean)
            {
                if (c == '\n')
                {
                    while (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\t')) sb.Length--;
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                    blank = true;
                    continue;
                }
                if (blank && (c == ' ' || c == '\t' || c == ' ' || c == ' ')) continue;
                // Les astérismes et filets de séparation (« * * * », « ——— ») ne se lisent pas.
                blank = false;
                sb.Append(c == ' ' || c == ' ' ? ' ' : c);
            }
            var lines = sb.ToString().Split('\n');
            sb.Length = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || IsOrnament(trimmed)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(trimmed);
            }
            var result = sb.ToString();
            if (maxChars > 0 && result.Length > maxChars) result = CutAtSentence(result, maxChars);
            return result;
        }

        private static bool IsOrnament(string line)
        {
            foreach (var c in line) if (char.IsLetterOrDigit(c)) return false;
            return true;
        }

        /// <summary>Coupe à la dernière fin de phrase avant le plafond (ou au
        /// dernier blanc, faute de mieux).</summary>
        public static string CutAtSentence(string text, int maxChars)
        {
            if (text == null || text.Length <= maxChars) return text ?? "";
            var cut = -1;
            for (var i = maxChars - 1; i > maxChars / 2; i--)
            {
                var c = text[i];
                if (c == '.' || c == '!' || c == '?' || c == '…' || c == '\n') { cut = i + 1; break; }
            }
            if (cut < 0) cut = text.LastIndexOf(' ', maxChars - 1);
            if (cut <= 0) cut = maxChars;
            return text.Substring(0, cut).TrimEnd();
        }

        /// <summary>La durée de lecture, à vue de nez (≈ 15 caractères par
        /// seconde) : « moins d'une minute », « environ 4 minutes ».</summary>
        public static string Duration(string prepared)
        {
            var seconds = (prepared ?? "").Length / 15.0;
            if (seconds < 50) return "moins d'une minute";
            var minutes = (int)Math.Round(seconds / 60.0);
            return "environ " + minutes + (minutes > 1 ? " minutes" : " minute");
        }
    }
}
