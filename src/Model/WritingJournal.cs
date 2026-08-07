using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversSale.Model
{
    /// <summary>One day of writing: net words added to the manuscript (typing
    /// minus deletions, floored at zero for the day).</summary>
    public class JournalDay
    {
        public string Date; // "yyyy-MM-dd"
        public int Words;
    }

    /// <summary>The writer's personal journal, per project: net words written
    /// day by day plus the daily goal. Only EDITS feed it (the deltas of the
    /// open document) — imports, trash purges and structure moves never count
    /// as words "written". Persisted in the .plot manifest.</summary>
    public class WritingJournal
    {
        public int DailyGoal;         // words per day, 0 = disabled
        public string LastCelebrated; // day the goal fanfare last fired ("yyyy-MM-dd")
        public List<JournalDay> Days = new List<JournalDay>();

        public static string Today()
        {
            return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public JournalDay FindDay(string date)
        {
            foreach (var day in Days)
                if (day.Date == date) return day;
            return null;
        }

        public int WordsOn(string date)
        {
            var day = FindDay(date);
            return day == null ? 0 : day.Words;
        }

        /// <summary>Accumulates a net delta on the given day. Deletions reduce
        /// the count but a day never goes negative.</summary>
        public void Add(string date, int delta)
        {
            if (delta == 0) return;
            var day = FindDay(date);
            if (day == null)
            {
                if (delta <= 0) return;
                day = new JournalDay { Date = date };
                Days.Add(day);
            }
            day.Words = Math.Max(0, day.Words + delta);
        }

        /// <summary>Net words over the last <paramref name="count"/> days,
        /// today included.</summary>
        public int WordsOverDays(int count)
        {
            var floor = DateTime.Now.Date.AddDays(1 - count)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var total = 0;
            foreach (var day in Days)
                if (string.CompareOrdinal(day.Date, floor) >= 0) total += day.Words;
            return total;
        }

        public int TotalWords()
        {
            var total = 0;
            foreach (var day in Days) total += day.Words;
            return total;
        }

        /// <summary>Consecutive days with at least one word, ending today (or
        /// yesterday when today is still blank — the streak is not broken by a
        /// day that has just begun).</summary>
        public int Streak()
        {
            var cursor = DateTime.Now.Date;
            if (WordsOn(Format(cursor)) == 0) cursor = cursor.AddDays(-1);
            var streak = 0;
            while (WordsOn(Format(cursor)) > 0)
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }
            return streak;
        }

        private static string Format(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
