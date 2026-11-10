using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Marabook.Model
{
    /// <summary>Les notes de version et les notes de patch (1.1.1, Rémi) : une
    /// version MAJEURE (1.0, 1.1, 2.0 — x.y.0) ouvre un cycle ; ses patchs
    /// (1.1.1, 1.1.2, « 1.0.5-patch-a ») s'y ajoutent. La fenêtre des
    /// nouveautés montre deux onglets : « Notes du patch » (le patch qui
    /// arrive, ouvert par défaut quand la mise à jour est un patch) et « Notes
    /// de version » (la majeure du cycle, suivie des patchs précédents,
    /// chacun après un filet « --- » et sous son nom en H1 — ouvert par défaut
    /// quand la mise à jour est une majeure). Modèle pur, testable ; la coquille
    /// fournit les textes (patch notes embarquées, mémoire des releases vues).</summary>
    public static class ReleaseNotes
    {
        /// <summary>Les trois nombres d'une version (« 1.1.1-beta » → 1, 1, 1).</summary>
        public static int[] Numbers(string version)
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

        /// <summary>La lettre d'un correctif à chaud (« a » de « 1.0.3-patch-a »), vide sinon.</summary>
        public static string PatchLetter(string version)
        {
            var match = Regex.Match(version ?? "", @"-patch-([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
        }

        public static bool IsBeta(string version)
        {
            return (version ?? "").IndexOf("-beta", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Un patch : x.y.z avec z > 0, ou un « -patch-a » ; une beta n'est ni l'un ni l'autre.</summary>
        public static bool IsPatch(string version)
        {
            if (string.IsNullOrEmpty(version) || IsBeta(version)) return false;
            return Numbers(version)[2] > 0 || PatchLetter(version).Length > 0;
        }

        /// <summary>Une version majeure : x.y.0 sans suffixe de patch ni beta.</summary>
        public static bool IsMajor(string version)
        {
            if (string.IsNullOrEmpty(version) || IsBeta(version)) return false;
            return Numbers(version)[2] == 0 && PatchLetter(version).Length == 0;
        }

        /// <summary>La majeure du cycle d'une version : « 1.1.1 » → « 1.1.0 », « 1.1.0 » → « 1.1.0 ».</summary>
        public static string MajorOf(string version)
        {
            var n = Numbers(version);
            return n[0] + "." + n[1] + ".0";
        }

        /// <summary>L'ordre des versions : par nombres, puis la lettre de
        /// correctif (1.0.3 &lt; 1.0.3-patch-a &lt; 1.0.3-patch-b), la beta avant la
        /// stable aux mêmes nombres.</summary>
        public static int Compare(string a, string b)
        {
            var na = Numbers(a);
            var nb = Numbers(b);
            for (var i = 0; i < 3; i++)
                if (na[i] != nb[i]) return na[i].CompareTo(nb[i]);
            var betaA = IsBeta(a);
            var betaB = IsBeta(b);
            if (betaA != betaB) return betaA ? -1 : 1;
            return string.CompareOrdinal(PatchLetter(a), PatchLetter(b));
        }

        /// <summary>Les patchs d'un cycle parmi des versions connues, AVANT une
        /// version donnée (exclue), du plus ancien au plus récent — les betas
        /// et les autres cycles écartés.</summary>
        public static List<string> PatchesBefore(IEnumerable<string> known, string version)
        {
            var major = MajorOf(version);
            var result = new List<string>();
            if (known == null) return result;
            foreach (var candidate in known)
            {
                if (!IsPatch(candidate) || MajorOf(candidate) != major) continue;
                if (Compare(candidate, version) >= 0) continue;
                if (!result.Contains(candidate)) result.Add(candidate);
            }
            result.Sort(Compare);
            return result;
        }

        /// <summary>Le titre d'un patch dans les notes de version : « # Patch 1.1.1 ».</summary>
        public static string PatchHeading(string version)
        {
            return "# Patch " + (version ?? "").Trim();
        }

        /// <summary>Le texte de l'onglet « Notes de version » : les notes de la
        /// majeure, puis chaque patch (dans l'ordre donné) après un filet « --- »
        /// et sous son nom en H1. Les textes absents sont sautés ; null si rien.</summary>
        public static string Compose(string majorNotes, IList<KeyValuePair<string, string>> patches)
        {
            var text = new StringBuilder();
            var major = (majorNotes ?? "").Trim();
            if (major.Length > 0) text.Append(major);
            if (patches != null)
                foreach (var patch in patches)
                {
                    var notes = (patch.Value ?? "").Trim();
                    if (notes.Length == 0) continue;
                    if (text.Length > 0) text.Append("\n\n---\n\n");
                    text.Append(PatchHeading(patch.Key)).Append("\n\n").Append(notes);
                }
            return text.Length == 0 ? null : text.ToString();
        }
    }
}
