using System;
using System.Text;

namespace Marabook.Persistence
{
    /// <summary>LES NOMS DE FICHIER SÛRS PARTOUT (25/11/2026, Rémi) : un nom
    /// proposé ou écrit par Marabook doit se lire sur les trois systèmes —
    /// un projet, un modèle, un export passent d'un poste à l'autre.
    ///
    /// Path.GetInvalidFileNameChars() ne dit que ce que refuse le système où
    /// l'on tourne : sur macOS et Linux, « / » et le caractère nul, rien
    /// d'autre. Un modèle nommé « Acte 1 : le départ ? » s'y enregistrait tel
    /// quel, et Windows ne pouvait plus l'ouvrir. La liste est donc FIXE,
    /// celle du plus strict (Windows), la même sur tous les systèmes.</summary>
    public static class FileNames
    {
        /// <summary>Les caractères que Windows refuse dans un nom ; s'y ajoutent les caractères de contrôle (0 à 31).</summary>
        public const string Forbidden = "\"<>|:*?\\/";

        /// <summary>Les noms que Windows réserve à ses périphériques, quelle que soit l'extension.</summary>
        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static bool IsForbidden(char c)
        {
            return c < ' ' || Forbidden.IndexOf(c) >= 0;
        }

        /// <summary>Chaque caractère interdit devient « _ » ; le reste ne bouge pas (un nom qui porte déjà son extension la garde).</summary>
        public static string Replace(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name ?? "") sb.Append(IsForbidden(c) ? '_' : c);
            return sb.ToString();
        }

        /// <summary>Un nom SANS extension rendu sûr : interdits remplacés, espaces
        /// des bords et points de fin retirés (Windows les ôte en silence, le
        /// fichier ne se retrouverait pas), nom réservé écarté d'un « _ ».
        /// Vide au bout du compte : <paramref name="fallback"/>.</summary>
        public static string Clean(string name, string fallback)
        {
            var clean = Replace((name ?? "").Trim()).Trim().TrimEnd('.', ' ');
            if (clean.Length == 0) return fallback ?? "";
            var stem = clean;
            var dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem.Substring(0, dot).TrimEnd(' ');
            foreach (var reserved in Reserved)
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return "_" + clean;
            return clean;
        }
    }
}
