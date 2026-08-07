using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UniversSale.Correction
{
    /// <summary>Professional writer's counts, ported from Typonanny's
    /// Statistiques: SEC (signs including spaces), signs without spaces, words,
    /// feuillets of 1,500 signs, reading time at ~220 words/minute.</summary>
    public class TextStats
    {
        private static readonly Regex WordPattern =
            new Regex("[\\p{L}\\p{Nd}]+(?:['’\\-][\\p{L}\\p{Nd}]+)*", RegexOptions.Compiled);

        public int Sec;
        public int NoSpaces;
        public int Words;
        public double Sheets;
        public int ReadingMinutes;

        public static TextStats Compute(string text)
        {
            var stats = new TextStats();
            if (string.IsNullOrEmpty(text)) return stats;
            foreach (var c in text)
            {
                if (c == '\r' || c == '\n') continue;
                stats.Sec++;
                if (!char.IsWhiteSpace(c)) stats.NoSpaces++;
            }
            stats.Words = WordPattern.Matches(text).Count;
            stats.Sheets = stats.Sec / 1500.0;
            stats.ReadingMinutes = (int)Math.Ceiling(stats.Words / 220.0);
            return stats;
        }

        /// <summary>Compact status-bar label, e.g. "1 234 mots · 6 789 SEC · 4,5 feuillets · ~6 min".</summary>
        public string ShortLabel()
        {
            if (Sec == 0) return "0 mot";
            var culture = CultureInfo.CurrentCulture;
            return Words.ToString("N0", culture) + (Words > 1 ? " mots · " : " mot · ")
                 + Sec.ToString("N0", culture) + " SEC · "
                 + Sheets.ToString("0.0", culture) + (Sheets >= 2 ? " feuillets" : " feuillet")
                 + " · ~" + ReadingMinutes + " min";
        }

        /// <summary>Detailed multi-line label for the inspector.</summary>
        public string LongLabel()
        {
            var culture = CultureInfo.CurrentCulture;
            return "Mots : " + Words.ToString("N0", culture)
                 + "\nCaractères espaces comprises : " + Sec.ToString("N0", culture)
                 + "\nSans espaces : " + NoSpaces.ToString("N0", culture)
                 + "\nFeuillets (1 500) : " + Sheets.ToString("0.0", culture)
                 + "\nLecture : ~" + ReadingMinutes + " min";
        }
    }
}
