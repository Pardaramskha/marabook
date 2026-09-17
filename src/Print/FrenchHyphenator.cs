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

        private static bool IsVowel(char c)
        {
            return Vowels.IndexOf(c) >= 0;
        }

        private static bool IsLetter(char c)
        {
            return char.IsLetter(c);
        }

        /// <summary>Allowed break positions (index = chars before the hyphen),
        /// ascending. Empty for unbreakable words (digits, apostrophes,
        /// too short, all-caps sigles).</summary>
        public static List<int> BreakPoints(string word, int minWordLength, int minBefore, int minAfter)
        {
            var points = new List<int>();
            if (word == null || word.Length < Math.Max(2, minWordLength)) return points;

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

                var cut = i; // break before word[i] (V-CV)
                if (!IsVowel(word[i + 1]))
                {
                    // VCC…: break between the consonants, unless they form an
                    // unbreakable onset (ta-bleau, not tab-leau).
                    var pair = word.Substring(i, 2);
                    if (Onsets.Contains(pair))
                    {
                        cut = i; // the onset opens the next syllable whole
                    }
                    else
                    {
                        cut = i + 1;
                        if (cut >= word.Length - 1) continue;
                        // Clusters of 3+ consonants without a clean onset: skip
                        // (a single consonant before a vowel is fine: del-le).
                        if (!IsVowel(word[cut]) && cut + 1 < word.Length
                            && !IsVowel(word[cut + 1])
                            && !Onsets.Contains(word.Substring(cut, 2)))
                            continue;
                    }
                }

                if (cut < minBefore) continue;
                if (word.Length - cut < minAfter) continue;
                if (points.Count == 0 || points[points.Count - 1] < cut)
                    points.Add(cut);
            }
            return points;
        }
    }
}
