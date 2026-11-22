using System;
using System.Collections.Generic;

namespace Marabook.Print
{
    /// <summary>Algorithmic French hyphenation for the composer: syllable
    /// boundaries from vowel/consonant structure (V-CV, VC-CV with the usual
    /// unbreakable onsets bl/br/ch/…), honoring the style's minimums. Not a
    /// TeX pattern set — good manuscript quality, refinable later without
    /// touching the composer.</summary>
    public static class FrenchHyphenator
    {
        private const string Vowels = "aàâäeéèêëiîïoôöuùûüyœæAÀÂÄEÉÈÊËIÎÏOÔÖUÙÛÜYŒÆ";

        private static readonly HashSet<string> Onsets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bl", "br", "ch", "cl", "cr", "dr", "fl", "fr",
            "gl", "gn", "gr", "ph", "pl", "pr", "th", "tr", "vr"
        };

        private static readonly char[] ApostropheChars = { '\'', '\u2019' };

        // Les élisions (09/10) : « d’incompréhension », « l’entourait »,
        // « qu’elle », « jusqu’au »… — le mot derrière l’apostrophe se coupe
        // comme n’importe quel mot (jamais juste après l’apostrophe : les
        // minima du style s’appliquent au mot lui-même). Les soudures
        // lexicalisées (aujourd'hui, presqu'île, quelqu'un) restent entières.
        private static readonly HashSet<string> Elisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "l", "d", "j", "m", "n", "s", "t", "c", "qu", "jusqu", "lorsqu", "puisqu", "quoiqu"
        };

        internal static string Mode = Environment.GetEnvironmentVariable("MARABOOK_HYPHEN_MODE") ?? "";

        private static Dictionary<string, byte[]> _patterns;
        private static int _longest;

        private static void LoadPatterns()
        {
            var patterns = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var longest = 0;
            foreach (var pattern in FrenchHyphenPatterns.All.Split(' '))
            {
                if (pattern.Length == 0) continue;
                var letters = new System.Text.StringBuilder();
                var levels = new List<byte>();
                byte pending = 0;
                foreach (var c in pattern)
                {
                    if (c >= '0' && c <= '9') { pending = (byte)(c - '0'); continue; }
                    levels.Add(pending);
                    pending = 0;
                    letters.Append(c);
                }
                levels.Add(pending);
                patterns[letters.ToString()] = levels.ToArray();
                longest = Math.Max(longest, letters.Length);
            }
            _longest = longest;
            _patterns = patterns;
        }

        /// <summary>Les points de coupe d'un mot simple selon les motifs de
        /// Liang du français (ceux de TeX) : index = lettres avant la coupe,
        /// croissants, sans égard pour les minima.</summary>
        public static List<int> LiangPoints(string word)
        {
            var points = new List<int>();
            if (string.IsNullOrEmpty(word)) return points;
            var levels = LiangLevels(word);
            for (var cut = 1; cut < word.Length; cut++)
                if ((levels[cut] & 1) == 1) points.Add(cut);
            return points;
        }

        /// <summary>Le niveau de Liang à chaque position du mot (index =
        /// lettres avant) : impair = coupe permise, pair non nul = coupe
        /// INTERDITE par un motif, 0 = les motifs n'en disent rien.</summary>
        public static byte[] LiangLevels(string word)
        {
            if (_patterns == null) lock (Vowels) { if (_patterns == null) LoadPatterns(); }
            var text = "." + word.ToLowerInvariant().Replace('’', '\'') + ".";
            var levels = new byte[text.Length + 1];
            for (var i = 0; i < text.Length; i++)
            {
                var max = Math.Min(_longest, text.Length - i);
                for (var length = 1; length <= max; length++)
                {
                    byte[] found;
                    if (!_patterns.TryGetValue(text.Substring(i, length), out found)) continue;
                    for (var k = 0; k < found.Length; k++)
                        if (found[k] > levels[i + k]) levels[i + k] = found[k];
                }
            }
            // levels[j] : entre text[j-1] et text[j] ; le mot commence à text[1].
            var result = new byte[word.Length + 1];
            for (var cut = 1; cut < word.Length; cut++) result[cut] = levels[cut + 1];
            return result;
        }

        // ------------------------------------------------ exceptions du projet

        /// <summary>Le mot d'une exception, ses traits de coupe retirés.</summary>
        public static string ExceptionWord(string entry)
        {
            return (entry ?? "").Replace("-", "");
        }

        /// <summary>Les coupes d'une exception écrite avec ses traits
        /// (« ma-ra-bout » → 2, 4) ; null si elle n'en porte aucun — le mot
        /// ne se coupe alors jamais.</summary>
        public static List<int> ExplicitPoints(string entry)
        {
            if (string.IsNullOrEmpty(entry) || entry.IndexOf('-') < 0) return null;
            var points = new List<int>();
            var letters = 0;
            foreach (var c in entry)
            {
                if (c != '-') { letters++; continue; }
                if (letters > 0 && (points.Count == 0 || points[points.Count - 1] != letters)) points.Add(letters);
            }
            if (points.Count > 0 && points[points.Count - 1] >= letters) points.RemoveAt(points.Count - 1); // un trait final ne coupe rien
            return points;
        }

        /// <summary>Le mot avec ses coupes marquées : « ma-ra-bout ».</summary>
        public static string Mark(string word, IList<int> points)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < word.Length; i++)
            {
                if (i > 0 && points != null && points.Contains(i)) sb.Append('-');
                sb.Append(word[i]);
            }
            return sb.ToString();
        }

        /// <summary>Les points d'un mot dont on a déjà pris les
        /// <paramref name="taken"/> premiers caractères : ceux qui restent,
        /// décalés (un point négatif — trait existant — garde son signe).</summary>
        public static List<int> ShiftPoints(List<int> points, int taken, int restLength, int minBefore, int minAfter)
        {
            var rest = new List<int>();
            if (points == null) return rest;
            foreach (var point in points)
            {
                var at = Math.Abs(point) - taken;
                if (at <= 0 || at < Math.Min(minBefore, 2) || restLength - at < 1) continue;
                rest.Add(point < 0 ? -at : at);
            }
            return rest;
        }

        private static bool IsVowel(char c)
        {
            return Vowels.IndexOf(c) >= 0;
        }

        private static bool IsLetter(char c)
        {
            return char.IsLetter(c);
        }

        // Les finales muettes de trois lettres qu'on ne rejette pas seules à
        // la ligne (usage typographique, corpus C2) : -que, -gue, -phe, -che,
        // -ble. Les groupes à liquide (-tre, -dre, -bre, -vre, -ple, -cle,
        // -gle, -fle) se coupent, eux : au-tre, siè-cle, sim-ple.
        private static readonly HashSet<string> MuteFinals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "que", "gue", "phe", "che", "ble"
        };

        /// <summary>Vrai si ce qui suit la coupe est une syllabe finale muette :
        /// consonne + e sourd (-re, -le, -te…), ou l'une des finales de trois
        /// lettres listées.</summary>
        private static bool IsMuteFinal(string word, int cut)
        {
            var length = word.Length - cut;
            if (word[word.Length - 1] != 'e') return false;
            if (length == 2) return !IsVowel(word[cut]);
            return length == 3 && MuteFinals.Contains(word.Substring(cut));
        }

        /// <summary>Allowed break positions (index = chars before the hyphen),
        /// ascending. Empty for unbreakable words (digits, too short,
        /// all-caps sigles, soudures). Un point NÉGATIF −k (09/10) : coupe
        /// après k caractères SANS ajouter de trait — le mot en porte déjà un
        /// là (mots composés : « peut-être », « arc-en-ciel »).</summary>
        public static List<int> BreakPoints(string word, int minWordLength, int minBefore, int minAfter)
        {
            var points = new List<int>();
            if (word == null || word.Length < Math.Max(2, minWordLength)) return points;

            // Élision : le mot derrière l'apostrophe, ses coupes décalées.
            var apostrophe = word.IndexOfAny(ApostropheChars);
            if (apostrophe > 0 && apostrophe < word.Length - 1)
            {
                if (word.IndexOfAny(ApostropheChars, apostrophe + 1) >= 0) return points; // deux apostrophes : on laisse
                if (!Elisions.Contains(word.Substring(0, apostrophe))) return points;    // aujourd'hui, presqu'île, quelqu'un
                var tail = word.Substring(apostrophe + 1);
                foreach (var cut in BreakPoints(tail, minWordLength, minBefore, minAfter))
                    points.Add(cut < 0 ? cut - (apostrophe + 1) : cut + apostrophe + 1);
                return points;
            }

            // Mot composé : une coupe après chaque trait d'union, le trait
            // existant fait office — jamais de « -- » en bout de ligne.
            if (word.IndexOf('-') > 0)
            {
                for (var i = 1; i < word.Length - 1; i++)
                {
                    if (word[i] != '-' || !IsLetter(word[i - 1]) || !IsLetter(word[i + 1])) continue;
                    if (i < minBefore) continue;                   // assez de lettres avant le trait
                    if (word.Length - (i + 1) < minAfter) continue; // et après
                    points.Add(-(i + 1));
                }
                return points;
            }

            var upper = 0;
            foreach (var c in word)
            {
                if (!IsLetter(c)) return points; // digits, apostrophes, hyphens: leave alone
                if (char.IsUpper(c)) upper++;
            }
            if (upper > 1) return points; // sigles, noms composés bizarres

            // LA CÉSURE HYBRIDE (20/11) : les coupes syllabiques de la maison
            // (réglées sur le corpus C2), corrigées par les motifs de Liang du
            // français — ceux de TeX. Un motif qui PERMET une coupe l'ajoute
            // (l'étymologie : trans-atlantique, dés-habiller, extra-ordinaire),
            // et chasse la coupe syllabique voisine qu'il interdit. Puis les
            // minima du style.
            var syllables = SyllablePoints(word);
            if (Mode != "syllabes")
            {
                var levels = LiangLevels(word);
                var kept = new SortedSet<int>(syllables);
                for (var cut = 1; cut < word.Length; cut++)
                {
                    if ((levels[cut] & 1) == 0 || kept.Contains(cut) || IsMuteFinal(word, cut)) continue;
                    foreach (var near in new[] { cut - 1, cut + 1 })
                        if (kept.Contains(near) && levels[near] != 0 && (levels[near] & 1) == 0) kept.Remove(near);
                    kept.Add(cut);
                }
                syllables = new List<int>(kept);
            }
            foreach (var cut in syllables)
                if (cut >= minBefore && word.Length - cut >= minAfter) points.Add(cut);
            return points;
        }

        /// <summary>Les coupes syllabiques d'un mot simple (lettres seules),
        /// sans égard pour les minima : à chaque passage voyelle → consonne,
        /// là où s'ouvre la syllabe suivante.</summary>
        private static List<int> SyllablePoints(string word)
        {
            var points = new List<int>();
            // Syllable scan: at each vowel→consonant transition, find where the
            // next syllable starts.
            for (var i = 1; i < word.Length - 1; i++)
            {
                if (IsVowel(word[i])) continue;
                if (!IsVowel(word[i - 1])) continue; // need V C
                // x entre deux voyelles (09/10) : il reste avec la voyelle qui
                // précède et rien ne se coupe autour (exa-men, maxi-mum, ga-laxie).
                if ((word[i] == 'x' || word[i] == 'X') && IsVowel(word[i + 1])) continue;

                var cut = i; // break before word[i] (V-CV)
                if (!IsVowel(word[i + 1]))
                {
                    // VCC…V : la coupe tombe entre les consonnes, sauf devant
                    // une attaque insécable qui ouvre la syllabe entière
                    // (ta-bleau, pas tab-leau). Trois consonnes et plus
                    // (09/10) : les DEUX dernières ouvrent la syllabe si elles
                    // font une attaque (ins-truc-tion, abs-trait), sinon la
                    // dernière seule (obs-cur, comp-ter, tech-nique).
                    var next = i;
                    while (next < word.Length && !IsVowel(word[next])) next++;
                    if (next >= word.Length) break; // plus de voyelle : rien à ouvrir
                    var run = next - i;
                    if (run == 2)
                        cut = Onsets.Contains(word.Substring(i, 2)) ? i : i + 1;
                    else
                        cut = Onsets.Contains(word.Substring(next - 2, 2)) ? next - 2 : next - 1;
                }

                // Pas de coupe devant une syllabe finale MUETTE (09/10) : -que,
                // -ble, -che, -gue, -phe, -tre… (nu-mé-rique, pos-sible, af-fiche).
                if (IsMuteFinal(word, cut)) continue;
                if (points.Count == 0 || points[points.Count - 1] < cut)
                    points.Add(cut);
            }
            return points;
        }
    }
}
