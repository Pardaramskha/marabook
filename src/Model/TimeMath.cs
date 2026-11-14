using System;
using System.Collections.Generic;
using System.Globalization;

namespace Marabook.Model
{
    /// <summary>Une durée entre deux dates, dite comme on la compte : des
    /// années, des mois, des jours — et le total en jours.</summary>
    public class TimeDistance
    {
        public long Years;
        public int Months;
        public long Days;
        public long TotalDays;
        public bool Backwards; // la seconde date précède la première
    }

    /// <summary>L'ARITHMÉTIQUE DES DATES (1.2.0, 14/11/2026, Rémi — le
    /// « Calculateur de dates » et la vue Calendrier) : compter des jours dans
    /// un système temporel quelconque. Tout passe par le NUMÉRO DU JOUR
    /// (TimeSystem.DayNumber) et son inverse, FromDayNumber. Une date sans
    /// mois ou sans jour compte pour le premier jour de sa période ; les
    /// heures ne comptent pas.</summary>
    public static class TimeMath
    {
        /// <summary>La date au jour près : le premier jour de l'année ou du mois quand ils manquent.</summary>
        public static TimePoint Full(TimePoint point)
        {
            return new TimePoint(point.Year, Math.Max(1, point.Month), Math.Max(1, point.Day));
        }

        /// <summary>La longueur moyenne d'une année, bissextiles comprises.</summary>
        public static double MeanYearDays(TimeSystem system)
        {
            const long span = 12000; // multiple des cycles usuels (4, 100, 400…)
            return Math.Max(1.0, (system.DaysBeforeYear(span) - system.DaysBeforeYear(0)) / (double)span);
        }

        /// <summary>L'inverse de TimeSystem.DayNumber : la date d'un numéro de jour.</summary>
        public static TimePoint FromDayNumber(TimeSystem system, long dayNumber)
        {
            var year = (long)Math.Floor(dayNumber / MeanYearDays(system));
            // L'estimation est à quelques années près : on la cale.
            while (system.DaysBeforeYear(year) > dayNumber) year--;
            while (system.DaysBeforeYear(year + 1) <= dayNumber) year++;
            var rest = dayNumber - system.DaysBeforeYear(year);
            var months = Math.Max(1, system.Months.Count);
            for (var month = 1; month <= months; month++)
            {
                var inMonth = Math.Max(1, system.DaysInMonth(year, month));
                if (rest < inMonth || month == months) return new TimePoint(year, month, (int)Math.Min(inMonth, rest + 1));
                rest -= inMonth;
            }
            return new TimePoint(year, 1, 1);
        }

        public static TimePoint AddDays(TimeSystem system, TimePoint point, long days)
        {
            return FromDayNumber(system, system.DayNumber(Full(point)) + days);
        }

        /// <summary>La date décalée : les années et les mois d'abord (le jour
        /// est ramené dans son mois d'arrivée — le 31 d'un mois de 30 jours
        /// devient le 30), puis les jours.</summary>
        public static TimePoint Add(TimeSystem system, TimePoint point, long years, long months, long days)
        {
            var from = Full(point);
            var count = Math.Max(1, system.Months.Count);
            var index = from.Month - 1 + months;
            var year = from.Year + years + LeapRule.FloorDiv(index, count);
            var month = (int)LeapRule.Mod(index, count) + 1;
            var day = Math.Min(from.Day, Math.Max(1, system.DaysInMonth(year, month)));
            var moved = new TimePoint(year, month, day);
            return days == 0 ? moved : AddDays(system, moved, days);
        }

        /// <summary>Le rang du jour dans son année (1 = le premier).</summary>
        public static int DayOfYear(TimeSystem system, TimePoint point)
        {
            var full = Full(point);
            return (int)(system.DayNumber(full) - system.DaysBeforeYear(full.Year)) + 1;
        }

        /// <summary>La durée d'une date à l'autre : années, mois, jours révolus, et le total en jours.</summary>
        public static TimeDistance Between(TimeSystem system, TimePoint a, TimePoint b)
        {
            var from = Full(a);
            var to = Full(b);
            var distance = new TimeDistance();
            if (system.DayNumber(to) < system.DayNumber(from))
            {
                var swap = from; from = to; to = swap;
                distance.Backwards = true;
            }
            var target = system.DayNumber(to);
            distance.TotalDays = target - system.DayNumber(from);
            var years = to.Year - from.Year;
            while (years > 0 && system.DayNumber(Add(system, from, years, 0, 0)) > target) years--;
            var months = 0;
            var count = Math.Max(1, system.Months.Count);
            while (months < count && system.DayNumber(Add(system, from, years, months + 1, 0)) <= target) months++;
            if (months >= count) { years++; months = 0; }
            distance.Years = years;
            distance.Months = months;
            distance.Days = target - system.DayNumber(Add(system, from, years, months, 0));
            return distance;
        }

        /// <summary>« 3 ans, 2 mois et 5 jours » ; « le même jour » pour rien.</summary>
        public static string Label(TimeDistance distance)
        {
            var parts = new List<string>();
            if (distance.Years != 0) parts.Add(TimeSystem.YearsLabel(distance.Years));
            if (distance.Months != 0) parts.Add(distance.Months.ToString(CultureInfo.InvariantCulture) + " mois");
            if (distance.Days != 0) parts.Add(TimeSystem.Count(distance.Days, "jour", "jours"));
            if (parts.Count == 0) return "le même jour";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " et " + parts[parts.Count - 1];
        }

        /// <summary>RÉPARTIR LES JOURS (onglet Calendrier) : chaque mois reçoit
        /// un nombre entier de semaines — 13 mois de 5 semaines de 6 jours
        /// font 13 mois de 30 jours. Faux (rien ne change) sans semaine.</summary>
        public static bool Distribute(TimeSystem system, int weeksPerMonth)
        {
            if (system == null || system.WeekDays.Count == 0 || weeksPerMonth < 1) return false;
            var days = Math.Min(10000, weeksPerMonth * system.WeekDays.Count);
            foreach (var month in system.Months) month.Days = days;
            return true;
        }

        // ------------------------------------------------------- le calendrier

        /// <summary>Une brique a-t-elle sa place dans les cases d'un calendrier ?
        /// Il lui faut un JOUR — au début, et à la fin si elle dure. Sinon elle
        /// est « orpheline » : datée à l'année ou au mois seulement.</summary>
        public static bool IsDayPrecise(TimelineEvent entry)
        {
            if (entry == null || entry.Start == null || !entry.Start.HasDay) return false;
            return entry.End == null || entry.End.HasDay;
        }

        /// <summary>La brique couvre-t-elle ce jour (numéro de jour) ?</summary>
        public static bool CoversDay(TimeSystem system, TimelineEvent entry, long dayNumber)
        {
            if (!IsDayPrecise(entry)) return false;
            var start = system.DayNumber(entry.Start);
            var end = entry.End == null ? start : Math.Max(start, system.DayNumber(entry.End));
            return dayNumber >= start && dayNumber <= end;
        }

        /// <summary>La brique touche-t-elle cette année ? (survol de la frise)</summary>
        public static bool TouchesYear(TimelineEvent entry, long year)
        {
            if (entry == null || entry.Start == null) return false;
            var end = entry.End == null ? entry.Start.Year : Math.Max(entry.Start.Year, entry.End.Year);
            return year >= entry.Start.Year && year <= end;
        }

        /// <summary>Une brique orpheline touche-t-elle ce mois (month ≥ 1) ou,
        /// month = 0, cette année sans dire de mois ?</summary>
        public static bool OrphanIn(TimelineEvent entry, long year, int month)
        {
            if (entry == null || entry.Start == null || IsDayPrecise(entry)) return false;
            var start = entry.Start;
            var end = entry.End ?? entry.Start;
            if (month <= 0)
            {
                // À l'année : une date sans mois, de cette année.
                if (start.HasMonth && (entry.End == null || end.HasMonth)) return false;
                return year >= start.Year && year <= Math.Max(start.Year, end.Year);
            }
            if (!start.HasMonth || !end.HasMonth) return false; // à l'année : rangée avec l'année
            var first = start.Year * 10000L + start.Month;
            var last = Math.Max(first, end.Year * 10000L + end.Month);
            var wanted = year * 10000L + month;
            return wanted >= first && wanted <= last;
        }
    }
}
