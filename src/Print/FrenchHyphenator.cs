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
                if (cut < minBefore) continue;
                if (word.Length - cut < minAfter) continue;
                if (points.Count == 0 || points[points.Count - 1] < cut)
                    points.Add(cut);
            }
            return points;
        }
    }
}
