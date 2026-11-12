using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marabook.Model
{
    // LES FRISES (1.2.0, 12/11/2026, Rémi — « Chrono-worries ») : une racine
    // « Frises » dans la Pile, des frises chronologiques (ItemKind.Timeline)
    // posées sur un SYSTÈME TEMPOREL du projet — son calendrier (mois,
    // bissextiles, semaine, horloge), ses SEGMENTS (ères, âges…) et sa
    // NOTATION (« E1.880 » = l'an 880 de la première ère). Rien ici ne touche
    // l'interface : le calcul des dates, des durées et des âges est testable
    // en console.
    //
    // L'axe : une année ABSOLUE t (entier, 0 = la première année du premier
    // segment, négatif avant), que la notation montre comme une année de
    // segment. Une date porte sa précision : l'année seule, le mois, le jour,
    // l'heure.

    public class TimeMonth
    {
        public string Name = "";
        public int Days = 30;
    }

    /// <summary>Une règle de bissextile : tous les « Every » ans — sauf les
    /// multiples d'« Except », à moins qu'ils le soient d'« Unless » — le mois
    /// « Month » (0 = le premier) gagne « Days » jours. Le grégorien : 4, 100,
    /// 400, février, 1.</summary>
    public class LeapRule
    {
        public int Every = 4;
        public int Except;
        public int Unless;
        public int Month;
        public int Days = 1;

        public bool IsLeap(long number)
        {
            if (Every <= 0 || Mod(number, Every) != 0) return false;
            if (Except > 0 && Mod(number, Except) == 0)
                return Unless > 0 && Mod(number, Unless) == 0;
            return true;
        }

        /// <summary>Le nombre d'années bissextiles STRICTEMENT avant ce numéro,
        /// à une constante près (seule une différence a un sens).</summary>
        public long CountBelow(long number)
        {
            if (Every <= 0) return 0;
            var count = FloorDiv(number - 1, Every);
            if (Except > 0)
            {
                var skipped = Lcm(Every, Except);
                count -= FloorDiv(number - 1, skipped);
                if (Unless > 0) count += FloorDiv(number - 1, Lcm(skipped, Unless));
            }
            return count;
        }

        public static long FloorDiv(long a, long b)
        {
            var q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        public static long Mod(long a, long b)
        {
            var m = a % b;
            return m < 0 ? m + b : m;
        }

        private static long Lcm(long a, long b)
        {
            long x = a, y = b;
            while (y != 0) { var r = x % y; x = y; y = r; }
            return a / x * b;
        }
    }

    /// <summary>Un segment de la temporalité (une ère, un âge) : son nom, son
    /// abréviation, sa durée en années — 0 = ouvert (le dernier seulement :
    /// il court jusqu'au dernier événement).</summary>
    public class TimeSegment
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Code = "";
        public long Length;
        public string Color; // "#RRGGBB", null = la teinte de son rang
    }

    /// <summary>Une date sur l'axe, avec sa précision : Month = 0 → l'année
    /// seule ; Day = 0 → le mois ; Hour &lt; 0 → le jour.</summary>
    public class TimePoint
    {
        public long Year;
        public int Month;
        public int Day;
        public int Hour = -1;
        public int Minute;

        public TimePoint() { }
        public TimePoint(long year) { Year = year; }
        public TimePoint(long year, int month, int day) { Year = year; Month = month; Day = day; }

        public bool HasMonth { get { return Month > 0; } }
        public bool HasDay { get { return Month > 0 && Day > 0; } }
        public bool HasTime { get { return HasDay && Hour >= 0; } }

        public TimePoint Clone()
        {
            return new TimePoint { Year = Year, Month = Month, Day = Day, Hour = Hour, Minute = Minute };
        }

        public bool SameAs(TimePoint other)
        {
            return other != null && Year == other.Year && Month == other.Month && Day == other.Day
                && Hour == other.Hour && Minute == other.Minute;
        }
    }

    /// <summary>UN SYSTÈME TEMPOREL du projet : calendrier, segments, notation.
    /// Le système de base est celui de notre monde (Gregorian).</summary>
    public class TimeSystem
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Calendrier";

        // — La temporalité par segments.
        public bool Segmented;
        public string SegmentWord = "Ère";   // « ère », « âge », « cycle »…
        public bool Cumulative;              // vrai : les années se cumulent d'un segment à l'autre ; faux : chaque segment repart de zéro
        public List<TimeSegment> Segments = new List<TimeSegment>();

        // — La notation. {a} l'année, {s} le numéro du segment, {n} son nom,
        //   {c} son abréviation ; pour une date : {j} le jour, {m} le numéro
        //   du mois, {M} son nom, {année} la notation de l'année.
        public string YearPattern = "{a}";
        public string NegativePattern = "";  // vide : la notation de l'année avec un nombre négatif
        public string DatePattern = "{j} {M} {année}";
        public bool YearZero;                // vrai : la première année est l'an 0

        // — Le calendrier.
        public List<TimeMonth> Months = new List<TimeMonth>();
        public List<LeapRule> Leaps = new List<LeapRule>();
        public List<string> WeekDays = new List<string>();
        public int HoursPerDay = 24;
        public int MinutesPerHour = 60;

        public const string GregorianName = "Calendrier de notre monde";

        /// <summary>Le système de base : le calendrier grégorien.</summary>
        public static TimeSystem Gregorian()
        {
            var system = new TimeSystem { Name = GregorianName, NegativePattern = "{a} av. J.-C." };
            var names = new[] { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };
            var days = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            for (var i = 0; i < 12; i++) system.Months.Add(new TimeMonth { Name = names[i], Days = days[i] });
            system.Leaps.Add(new LeapRule { Every = 4, Except = 100, Unless = 400, Month = 1, Days = 1 });
            system.WeekDays.AddRange(new[] { "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche" });
            return system;
        }

        public TimeSystem Clone()
        {
            var copy = new TimeSystem
            {
                Id = Id, Name = Name, Segmented = Segmented, SegmentWord = SegmentWord, Cumulative = Cumulative,
                YearPattern = YearPattern, NegativePattern = NegativePattern, DatePattern = DatePattern, YearZero = YearZero,
                HoursPerDay = HoursPerDay, MinutesPerHour = MinutesPerHour
            };
            foreach (var segment in Segments)
                copy.Segments.Add(new TimeSegment { Id = segment.Id, Name = segment.Name, Code = segment.Code, Length = segment.Length, Color = segment.Color });
            foreach (var month in Months) copy.Months.Add(new TimeMonth { Name = month.Name, Days = month.Days });
            foreach (var leap in Leaps)
                copy.Leaps.Add(new LeapRule { Every = leap.Every, Except = leap.Except, Unless = leap.Unless, Month = leap.Month, Days = leap.Days });
            copy.WeekDays.AddRange(WeekDays);
            return copy;
        }

        // ------------------------------------------------------------ segments

        public bool HasSegments { get { return Segmented && Segments.Count > 0; } }
        public int FirstYear { get { return YearZero ? 0 : 1; } }

        public string SegmentLabel
        {
            get { var word = (SegmentWord ?? "").Trim(); return word.Length == 0 ? "Segment" : word; }
        }

        /// <summary>L'année absolue où commence le segment.</summary>
        public long SegmentStart(int index)
        {
            long start = 0;
            for (var i = 0; i < index && i < Segments.Count; i++) start += Math.Max(0, Segments[i].Length);
            return start;
        }

        /// <summary>Le segment d'une année absolue (le premier avant l'origine,
        /// le dernier au-delà) ; 0 sans segments.</summary>
        public int SegmentIndexOf(long year)
        {
            if (!HasSegments || year < 0) return 0;
            long start = 0;
            for (var i = 0; i < Segments.Count; i++)
            {
                var length = Segments[i].Length;
                if (length <= 0 || i == Segments.Count - 1) return i;
                if (year < start + length) return i;
                start += length;
            }
            return Segments.Count - 1;
        }

        /// <summary>L'année telle qu'on la lit : dans son segment si chaque
        /// segment repart de zéro, cumulée sinon. Avant l'origine : négative.</summary>
        public long DisplayYear(long year)
        {
            if (year < 0) return year;
            var local = HasSegments && !Cumulative ? year - SegmentStart(SegmentIndexOf(year)) : year;
            return local + FirstYear;
        }

        /// <summary>L'inverse : l'année absolue d'une année lue dans un segment.</summary>
        public long AbsoluteYear(int segment, long shown)
        {
            var reset = HasSegments && !Cumulative;
            if (shown < 0) return reset && segment > 0 ? SegmentStart(segment) + shown : shown;
            var local = Math.Max(0, shown - FirstYear);
            return reset ? SegmentStart(Math.Max(0, Math.Min(segment, Segments.Count - 1))) + local : local;
        }

        // ---------------------------------------------------------- calendrier

        public int BaseDays
        {
            get
            {
                var days = 0;
                foreach (var month in Months) days += Math.Max(1, month.Days);
                return Math.Max(1, days);
            }
        }

        private long LeapNumber(long year) { return year + FirstYear; }

        public int DaysInMonth(long year, int month)
        {
            if (month < 1 || month > Months.Count) return 0;
            var days = Math.Max(1, Months[month - 1].Days);
            foreach (var leap in Leaps)
                if (leap.Month == month - 1 && leap.IsLeap(LeapNumber(year))) days += leap.Days;
            return Math.Max(1, days);
        }

        public int DaysInYear(long year)
        {
            var days = BaseDays;
            foreach (var leap in Leaps)
                if (leap.Month >= 0 && leap.Month < Months.Count && leap.IsLeap(LeapNumber(year))) days += leap.Days;
            return Math.Max(1, days);
        }

        public bool IsLeapYear(long year)
        {
            foreach (var leap in Leaps)
                if (leap.Month >= 0 && leap.Month < Months.Count && leap.Days != 0 && leap.IsLeap(LeapNumber(year))) return true;
            return false;
        }

        /// <summary>Les jours écoulés entre l'origine et le début de l'année.</summary>
        public long DaysBeforeYear(long year)
        {
            var days = year * BaseDays;
            foreach (var leap in Leaps)
            {
                if (leap.Month < 0 || leap.Month >= Months.Count) continue;
                days += leap.Days * (leap.CountBelow(LeapNumber(year)) - leap.CountBelow(LeapNumber(0)));
            }
            return days;
        }

        private int DaysBeforeMonth(long year, int month)
        {
            var days = 0;
            for (var m = 1; m < month && m <= Months.Count; m++) days += DaysInMonth(year, m);
            return days;
        }

        /// <summary>Le numéro du jour depuis l'origine (une date sans mois ni
        /// jour compte pour le premier jour de sa période).</summary>
        public long DayNumber(TimePoint point)
        {
            var month = Math.Max(1, point.Month);
            return DaysBeforeYear(point.Year) + DaysBeforeMonth(point.Year, month) + Math.Max(1, point.Day) - 1;
        }

        /// <summary>Le jour de la semaine d'une date précise au jour, null sinon.</summary>
        public string WeekDayOf(TimePoint point)
        {
            if (point == null || !point.HasDay || WeekDays.Count == 0) return null;
            return WeekDays[(int)LeapRule.Mod(DayNumber(point), WeekDays.Count)];
        }

        // ----------------------------------------------------------------- axe

        /// <summary>Le début de la période que désigne la date, en années.</summary>
        public double AxisStart(TimePoint point)
        {
            if (point == null) return 0;
            if (!point.HasMonth) return point.Year;
            double days = DaysBeforeMonth(point.Year, point.Month) + Math.Max(1, point.Day) - 1;
            if (point.HasTime) days += DayFraction(point);
            return point.Year + days / DaysInYear(point.Year);
        }

        /// <summary>La fin de cette période (l'année entière, le mois, le jour, la minute).</summary>
        public double AxisEnd(TimePoint point)
        {
            if (point == null) return 0;
            if (!point.HasMonth) return point.Year + 1;
            double inYear = DaysInYear(point.Year);
            if (!point.HasDay) return point.Year + (DaysBeforeMonth(point.Year, point.Month) + DaysInMonth(point.Year, point.Month)) / inYear;
            if (!point.HasTime) return AxisStart(point) + 1 / inYear;
            return AxisStart(point) + 1.0 / Math.Max(1, HoursPerDay * MinutesPerHour) / inYear;
        }

        public double AxisMid(TimePoint point) { return (AxisStart(point) + AxisEnd(point)) / 2; }

        private double DayFraction(TimePoint point)
        {
            var hours = Math.Max(1, HoursPerDay);
            var minutes = Math.Max(1, MinutesPerHour);
            return (Math.Max(0, point.Hour) + (double)Math.Max(0, point.Minute) / minutes) / hours;
        }

        /// <summary>La date d'un point de l'axe : precision 0 = l'année, 1 = le
        /// mois, 2 = le jour.</summary>
        public TimePoint FromAxis(double axis, int precision)
        {
            var year = (long)Math.Floor(axis);
            var point = new TimePoint(year);
            if (precision <= 0 || Months.Count == 0) return point;
            var day = (int)Math.Floor((axis - year) * DaysInYear(year));
            for (var m = 1; m <= Months.Count; m++)
            {
                var inMonth = DaysInMonth(year, m);
                if (day < inMonth || m == Months.Count)
                {
                    point.Month = m;
                    if (precision >= 2) point.Day = Math.Max(1, Math.Min(inMonth, day + 1));
                    break;
                }
                day -= inMonth;
            }
            return point;
        }

        /// <summary>L'ordre de deux dates (le début de leur période).</summary>
        public int Compare(TimePoint a, TimePoint b)
        {
            return AxisStart(a).CompareTo(AxisStart(b));
        }

        // ------------------------------------------------------------ notation

        public string FormatYear(long year)
        {
            var segment = SegmentIndexOf(year);
            var shown = DisplayYear(year);
            var negative = shown < 0 && !string.IsNullOrEmpty((NegativePattern ?? "").Trim());
            var pattern = negative ? NegativePattern : (string.IsNullOrEmpty(YearPattern) ? "{a}" : YearPattern);
            var number = (negative ? -shown : shown).ToString(CultureInfo.InvariantCulture);
            var text = pattern.Replace("{a}", number).Replace("{s}", (segment + 1).ToString(CultureInfo.InvariantCulture));
            var name = HasSegments ? Segments[segment].Name ?? "" : "";
            var code = HasSegments ? SegmentCode(segment) : "";
            return text.Replace("{n}", name).Replace("{c}", code);
        }

        /// <summary>L'abréviation d'un segment ; à défaut, l'initiale du mot et son rang (« E2 »).</summary>
        public string SegmentCode(int index)
        {
            if (index < 0 || index >= Segments.Count) return "";
            var code = (Segments[index].Code ?? "").Trim();
            if (code.Length > 0) return code;
            // L'initiale sans accent : « Ère » donne E1, comme la notation E{s}.{a}.
            var initial = SegmentLabel.Substring(0, 1).Normalize(NormalizationForm.FormD).Substring(0, 1).ToUpperInvariant();
            return initial + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Le nom d'un segment ; à défaut, le mot et son rang (« Ère 2 »).</summary>
        public string SegmentName(int index)
        {
            if (index < 0 || index >= Segments.Count) return "";
            var name = (Segments[index].Name ?? "").Trim();
            return name.Length > 0 ? name : SegmentLabel + " " + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        private string DayPattern
        {
            get { return string.IsNullOrEmpty((DatePattern ?? "").Trim()) ? "{j} {M} {année}" : DatePattern.Replace("{annee}", "{année}"); }
        }

        /// <summary>La notation d'un mois : celle du jour, sans le jour.</summary>
        private string MonthPattern
        {
            get
            {
                var pattern = DayPattern.Replace("{j}", "");
                pattern = pattern.Trim(' ', '/', '.', '-', ',', ' ');
                while (pattern.Contains("  ")) pattern = pattern.Replace("  ", " ");
                return pattern.Length == 0 ? "{M} {année}" : pattern;
            }
        }

        public string Format(TimePoint point)
        {
            if (point == null) return "";
            var year = FormatYear(point.Year);
            if (!point.HasMonth || point.Month > Months.Count) return year;
            var pattern = point.HasDay ? DayPattern : MonthPattern;
            var text = pattern.Replace("{année}", year)
                .Replace("{j}", point.Day.ToString(CultureInfo.InvariantCulture))
                .Replace("{m}", point.Month.ToString(CultureInfo.InvariantCulture))
                .Replace("{M}", Months[point.Month - 1].Name ?? "");
            if (point.HasTime)
                text += " " + point.Hour.ToString(CultureInfo.InvariantCulture) + "h" + point.Minute.ToString("00", CultureInfo.InvariantCulture);
            return text;
        }

        /// <summary>Un exemple de la notation (la fenêtre du système, le dialogue du lien).</summary>
        public string Example()
        {
            long year = HasSegments ? SegmentStart(Math.Min(1, Segments.Count - 1)) + 879 : 879;
            if (HasSegments)
            {
                var index = Math.Min(1, Segments.Count - 1);
                var length = Segments[index].Length;
                if (length > 0 && 879 >= length) year = SegmentStart(index) + length / 2;
            }
            return FormatYear(year);
        }

        /// <summary>Ce qui manque à la notation pour être lue sans ambiguïté, null si tout va.</summary>
        public string NotationProblem()
        {
            var pattern = YearPattern ?? "";
            if (pattern.IndexOf("{a}", StringComparison.Ordinal) < 0) return "La notation doit contenir l'année : {a}.";
            if (HasSegments && !Cumulative && pattern.IndexOf("{s}", StringComparison.Ordinal) < 0
                && pattern.IndexOf("{n}", StringComparison.Ordinal) < 0 && pattern.IndexOf("{c}", StringComparison.Ordinal) < 0)
                return "Chaque segment repart de zéro : la notation doit dire le segment — {s} (son numéro), {c} (son abréviation) ou {n} (son nom).";
            var negative = (NegativePattern ?? "").Trim();
            if (negative.Length > 0 && negative.IndexOf("{a}", StringComparison.Ordinal) < 0) return "La notation des années négatives doit contenir l'année : {a}.";
            var date = DayPattern;
            if (date.IndexOf("{année}", StringComparison.Ordinal) < 0) return "La notation d'une date doit contenir {année}.";
            if (date.IndexOf("{j}", StringComparison.Ordinal) < 0) return "La notation d'une date doit contenir le jour : {j}.";
            if (date.IndexOf("{m}", StringComparison.Ordinal) < 0 && date.IndexOf("{M}", StringComparison.Ordinal) < 0)
                return "La notation d'une date doit contenir le mois : {m} (son numéro) ou {M} (son nom).";
            return null;
        }

        // ------------------------------------------------------------- lecture

        private List<Regex> _parsers;
        private string _parsersKey;

        private string ParsersKey()
        {
            var sb = new StringBuilder();
            sb.Append(YearPattern).Append('\u0001').Append(NegativePattern).Append('\u0001').Append(DatePattern).Append('\u0001');
            foreach (var segment in Segments) sb.Append(segment.Name).Append('\u0002').Append(segment.Code).Append('\u0002');
            sb.Append(SegmentWord).Append('\u0001');
            foreach (var month in Months) sb.Append(month.Name).Append('\u0002');
            return sb.ToString();
        }

        private List<Regex> Parsers()
        {
            var key = ParsersKey();
            if (_parsers != null && _parsersKey == key) return _parsers;
            var years = new List<string>();
            var negative = (NegativePattern ?? "").Trim();
            if (negative.Length > 0) years.Add(negative.Replace("{a}", "{-a}"));
            years.Add(string.IsNullOrEmpty(YearPattern) ? "{a}" : YearPattern);
            var parsers = new List<Regex>();
            // Du plus précis au moins précis : le jour, le mois, l'année.
            foreach (var shape in new[] { DayPattern, MonthPattern, "{année}" })
                foreach (var year in years)
                {
                    var regex = Compile(shape.Replace("{année}", year), shape.IndexOf("{j}", StringComparison.Ordinal) >= 0);
                    if (regex != null) parsers.Add(regex);
                }
            _parsers = parsers;
            _parsersKey = key;
            return parsers;
        }

        private Regex Compile(string pattern, bool withTime)
        {
            var sb = new StringBuilder("^");
            var i = 0;
            while (i < pattern.Length)
            {
                var close = pattern[i] == '{' ? pattern.IndexOf('}', i) : -1;
                var token = close > i ? pattern.Substring(i, close - i + 1) : null;
                string group = null;
                switch (token)
                {
                    case "{a}": group = @"(?<a>-?\d+)"; break;
                    case "{-a}": group = @"(?<na>\d+)"; break;
                    case "{s}": group = @"(?<s>\d+)"; break;
                    case "{j}": group = @"(?<j>\d{1,4})"; break;
                    case "{m}": group = @"(?<m>\d{1,4})"; break;
                    case "{n}": group = "(?<n>" + Alternation(SegmentNames(false)) + ")"; break;
                    case "{c}": group = "(?<c>" + Alternation(SegmentNames(true)) + ")"; break;
                    case "{M}": group = "(?<M>" + Alternation(MonthNames()) + ")"; break;
                }
                if (group != null) { sb.Append(group); i = close + 1; continue; }
                if (char.IsWhiteSpace(pattern[i]) || pattern[i] == ' ')
                {
                    while (i < pattern.Length && (char.IsWhiteSpace(pattern[i]) || pattern[i] == ' ')) i++;
                    sb.Append(@"\s+");
                    continue;
                }
                sb.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
            if (withTime) sb.Append(@"(?:\s+(?:à\s+)?(?<h>\d{1,3})\s*[h:]\s*(?<min>\d{1,3})?)?");
            sb.Append("$");
            try { return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { return null; }
        }

        private List<string> SegmentNames(bool codes)
        {
            var names = new List<string>();
            for (var i = 0; i < Segments.Count; i++) names.Add(codes ? SegmentCode(i) : SegmentName(i));
            return names;
        }

        private List<string> MonthNames()
        {
            var names = new List<string>();
            foreach (var month in Months) names.Add(month.Name ?? "");
            return names;
        }

        private static string Alternation(List<string> names)
        {
            // Les plus longs d'abord : « Ère 10 » avant « Ère 1 ».
            var sorted = new List<string>();
            foreach (var name in names) if (!string.IsNullOrEmpty(name)) sorted.Add(name);
            if (sorted.Count == 0) return "(?!)";
            sorted.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            var sb = new StringBuilder();
            foreach (var name in sorted)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(Regex.Escape(name).Replace(@"\ ", @"\s+"));
            }
            return sb.ToString();
        }

        private static int IndexOfName(List<string> names, string value)
        {
            for (var i = 0; i < names.Count; i++)
                if (string.Compare(Squeeze(names[i]), Squeeze(value), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0) return i;
            return -1;
        }

        private static string Squeeze(string text)
        {
            var sb = new StringBuilder();
            var space = false;
            foreach (var c in (text ?? "").Trim())
            {
                if (char.IsWhiteSpace(c) || c == ' ') { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Lit une date écrite dans la notation du système (« E5.902 »,
        /// « 14 juillet 1789 », « juillet 1789 », « 44 av. J.-C. »). Faux si
        /// le texte n'en est pas une, ou si la date n'existe pas.</summary>
        public bool TryParse(string text, out TimePoint point)
        {
            point = null;
            var input = Squeeze(text);
            if (input.Length == 0 || input.Length > 80) return false;
            foreach (var regex in Parsers())
            {
                var match = regex.Match(input);
                if (!match.Success) continue;
                var read = Read(match);
                if (read == null) continue;
                point = read;
                return true;
            }
            return false;
        }

        private TimePoint Read(Match match)
        {
            long shown;
            if (match.Groups["na"].Success)
            {
                if (!long.TryParse(match.Groups["na"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out shown)) return null;
                shown = -shown;
            }
            else if (!long.TryParse(match.Groups["a"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out shown)) return null;

            var segment = -1;
            if (match.Groups["s"].Success)
            {
                int number;
                if (!int.TryParse(match.Groups["s"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return null;
                segment = number - 1;
            }
            else if (match.Groups["n"].Success) segment = IndexOfName(SegmentNames(false), match.Groups["n"].Value);
            else if (match.Groups["c"].Success) segment = IndexOfName(SegmentNames(true), match.Groups["c"].Value);
            var named = match.Groups["s"].Success || match.Groups["n"].Success || match.Groups["c"].Success;
            if (named && HasSegments && (segment < 0 || segment >= Segments.Count)) return null;
            if (shown == 0 && !YearZero) return null; // pas d'an zéro dans ce système

            var year = AbsoluteYear(Math.Max(0, segment), shown);
            // Une année au-delà de son segment (« E1.5000 » quand la première
            // ère dure 1 200 ans) n'existe pas.
            if (named && HasSegments && !Cumulative && shown > 0 && SegmentIndexOf(year) != segment) return null;
            var point = new TimePoint(year);

            if (match.Groups["m"].Success || match.Groups["M"].Success)
            {
                int month;
                if (match.Groups["M"].Success) month = IndexOfName(MonthNames(), match.Groups["M"].Value) + 1;
                else if (!int.TryParse(match.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out month)) return null;
                if (month < 1 || month > Months.Count) return null;
                point.Month = month;
                if (match.Groups["j"].Success)
                {
                    int day;
                    if (!int.TryParse(match.Groups["j"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out day)) return null;
                    if (day < 1 || day > DaysInMonth(year, month)) return null;
                    point.Day = day;
                    if (match.Groups["h"].Success)
                    {
                        int hour, minute = 0;
                        if (!int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out hour)) return null;
                        if (match.Groups["min"].Success && !int.TryParse(match.Groups["min"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute)) return null;
                        if (hour >= Math.Max(1, HoursPerDay) || minute >= Math.Max(1, MinutesPerHour)) return null;
                        point.Hour = hour;
                        point.Minute = minute;
                    }
                }
            }
            return point;
        }

        // -------------------------------------------------------------- durées

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        public static string Count(long value, string one, string many)
        {
            return value.ToString("N0", French) + " " + (Math.Abs(value) > 1 ? many : one);
        }

        public static string YearsLabel(long years) { return Count(years, "an", "ans"); }

        /// <summary>Une durée en années de l'axe, dite en clair : « 1 200 ans »,
        /// « 3 ans et 4 mois », « 8 mois », « 12 jours », « 5 heures ».</summary>
        public string DurationLabel(double years)
        {
            if (years < 0) years = -years;
            var months = Math.Max(1, Months.Count);
            if (years >= 1 - 1e-9)
            {
                var whole = (long)Math.Floor(years + 1e-9);
                var rest = (int)Math.Round((years - whole) * months);
                if (rest >= months) { whole++; rest = 0; }
                if (rest == 0 || whole >= 20) return YearsLabel(rest * 2 >= months && whole >= 20 ? whole + 1 : whole);
                return YearsLabel(whole) + " et " + rest + " mois";
            }
            var days = years * BaseDays;
            var perMonth = (double)BaseDays / months;
            if (days >= perMonth * 2 - 0.5) return (long)Math.Round(days / perMonth) + " mois";
            if (days >= 1 - 1e-6) return Count((long)Math.Round(days), "jour", "jours");
            var hours = days * Math.Max(1, HoursPerDay);
            if (hours >= 1 - 1e-6) return Count((long)Math.Round(hours), "heure", "heures");
            return Count(Math.Max(1, (long)Math.Round(hours * Math.Max(1, MinutesPerHour))), "minute", "minutes");
        }

        /// <summary>L'âge en années révolues à une date, depuis une naissance
        /// (le mois et le jour comptent quand les deux dates les donnent).</summary>
        public long AgeAt(TimePoint birth, TimePoint at)
        {
            var age = at.Year - birth.Year;
            if (birth.HasMonth && at.HasMonth)
            {
                if (at.Month < birth.Month) age--;
                else if (at.Month == birth.Month && birth.HasDay && at.HasDay && at.Day < birth.Day) age--;
            }
            return age;
        }
    }

    // ================================================================ la frise

    public class TimelineEventType
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Color = "#5B67D8";
    }

    /// <summary>Une brique de la frise : un événement ponctuel (End null) ou
    /// sur la durée, d'un type, et — au choix — assigné à un élément du projet
    /// (LinkKind dit sa sorte : fiche, écrit, livre…).</summary>
    public class TimelineEvent
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Description = "";
        public string TypeId;
        public TimePoint Start = new TimePoint();
        public TimePoint End;
        public string LinkKind;
        public string LinkId;

        // EN ATTENTE (1.2.0, lot 2) : la temporalité a changé et la date de la
        // brique ne s'y lit plus. On garde ce qui était écrit (le début, la fin)
        // pour le redemander à l'auteur ; la brique quitte l'axe d'ici là.
        public string PendingStart;
        public string PendingEnd;
        public bool IsPending { get { return PendingStart != null; } }

        // Née d'une fiche (date de naissance ou de décès liée à la frise) :
        // jamais enregistrée, recalculée à chaque lecture.
        public bool Derived;

        public bool IsSpan { get { return End != null; } }

        public TimelineEvent Clone()
        {
            return new TimelineEvent
            {
                Id = Id, Title = Title, Description = Description, TypeId = TypeId,
                Start = Start == null ? new TimePoint() : Start.Clone(), End = End == null ? null : End.Clone(),
                LinkKind = LinkKind, LinkId = LinkId, PendingStart = PendingStart, PendingEnd = PendingEnd, Derived = Derived
            };
        }
    }

    public class TimelineInfo
    {
        public string SystemId;
        public List<TimelineEventType> Types = new List<TimelineEventType>();
        public List<TimelineEvent> Events = new List<TimelineEvent>();

        public TimelineEventType FindType(string id)
        {
            if (id == null) return null;
            foreach (var type in Types) if (type.Id == id) return type;
            return BuiltInType(id); // « Naissance » et « Décès », nés des fiches
        }

        public const string BirthTypeId = "life-birth";
        public const string DeathTypeId = "life-death";
        private static readonly TimelineEventType Birth = new TimelineEventType { Id = BirthTypeId, Name = "Naissance", Color = "#4FA36B" };
        private static readonly TimelineEventType Death = new TimelineEventType { Id = DeathTypeId, Name = "Décès", Color = "#6B7280" };

        /// <summary>Les deux types que toute frise porte d'office (1.2.0, lot 2).</summary>
        public static TimelineEventType BuiltInType(string id)
        {
            return id == BirthTypeId ? Birth : id == DeathTypeId ? Death : null;
        }

        public TimelineEvent FindEvent(string id)
        {
            if (id == null) return null;
            foreach (var entry in Events) if (entry.Id == id) return entry;
            return null;
        }

        /// <summary>Les types d'événement d'une frise neuve.</summary>
        public static List<TimelineEventType> DefaultTypes()
        {
            var types = new List<TimelineEventType>();
            var names = new[] { "Histoire", "Conflit", "Personnage", "Récit", "Culture" };
            var colors = new[] { "#5B67D8", "#C0504D", "#3E9C7A", "#B5843A", "#8E6BBF" };
            for (var i = 0; i < names.Length; i++)
                types.Add(new TimelineEventType { Id = "type-" + (i + 1), Name = names[i], Color = colors[i] });
            return types;
        }

        public TimelineInfo Clone()
        {
            var copy = new TimelineInfo { SystemId = SystemId };
            foreach (var type in Types) copy.Types.Add(new TimelineEventType { Id = type.Id, Name = type.Name, Color = type.Color });
            foreach (var entry in Events) copy.Events.Add(entry.Clone());
            return copy;
        }
    }

    /// <summary>Les sortes d'élément auxquelles une brique s'assigne : on
    /// choisit la sorte, PUIS l'élément (jamais un seul menu de tout le projet).</summary>
    public static class TimelineLinks
    {
        public const string Sheet = "sheet";
        public const string Text = "text";
        public const string Book = "book";
        public const string Plan = "plan";
        public const string MindMap = "mindmap";
        public const string Media = "media";

        public static readonly string[] Kinds = { Sheet, Text, Book, Plan, MindMap, Media };

        public static string Label(string kind)
        {
            switch (kind ?? "")
            {
                case Sheet: return "Fiche";
                case Text: return "Écrit";
                case Book: return "Livre";
                case Plan: return "Plan";
                case MindMap: return "Carte mentale";
                case Media: return "Document de recherche";
                default: return "Aucune";
            }
        }

        public static ItemKind KindOf(string kind)
        {
            switch (kind ?? "")
            {
                case Text: return ItemKind.Text;
                case Book: return ItemKind.Book;
                case Plan: return ItemKind.Plan;
                case MindMap: return ItemKind.MindMap;
                case Media: return ItemKind.Media;
                default: return ItemKind.Sheet;
            }
        }

        public static string KindFor(BinderItem item)
        {
            if (item == null) return null;
            switch (item.Kind)
            {
                case ItemKind.Sheet: return Sheet;
                case ItemKind.Text: return Text;
                case ItemKind.Book: return Book;
                case ItemKind.Plan: return Plan;
                case ItemKind.MindMap: return MindMap;
                case ItemKind.Media: return Media;
                default: return null;
            }
        }

        /// <summary>Les éléments d'une sorte, hors Corbeille, triés par titre.</summary>
        public static List<BinderItem> ItemsOf(Project project, string kind)
        {
            var items = new List<BinderItem>();
            if (project == null || Array.IndexOf(Kinds, kind) < 0) return items;
            var wanted = KindOf(kind);
            foreach (var item in project.AllItems())
            {
                if (item.Kind != wanted || item.IsCategory) continue;
                if (item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                if (wanted == ItemKind.Text && (item.IsExtraPage || item.IsToc)) continue;
                items.Add(item);
            }
            items.Sort(delegate(BinderItem a, BinderItem b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            return items;
        }
    }

    public class TimelineSegmentStats
    {
        public string Name;
        public long Start;
        public long Length;   // la durée effective (un segment ouvert court jusqu'au dernier événement)
        public bool Open;
        public int Events;
    }

    /// <summary>Ce que le Général du rail dit d'une frise : ses événements,
    /// la durée totale de la temporalité, celle de chaque segment.</summary>
    public class TimelineStats
    {
        public int Events, Points, Spans;
        public double Min, Max;       // l'étendue de l'axe (segments et événements)
        public long TotalYears;
        public List<TimelineSegmentStats> Segments = new List<TimelineSegmentStats>();
    }

    public static partial class Timelines
    {
        /// <summary>Le système d'une frise ; à défaut (système supprimé, vieille
        /// frise), le premier du projet, sinon celui de notre monde.</summary>
        public static TimeSystem SystemOf(Project project, BinderItem timeline)
        {
            if (project != null && timeline != null && timeline.Timeline != null)
            {
                var system = project.FindTimeSystem(timeline.Timeline.SystemId);
                if (system != null) return system;
            }
            if (project != null && project.TimeSystems.Count > 0) return project.TimeSystems[0];
            return Fallback;
        }

        private static readonly TimeSystem Fallback = TimeSystem.Gregorian();

        /// <summary>Les frises du projet (hors Corbeille), dans l'ordre de la Pile.</summary>
        public static List<BinderItem> All(Project project)
        {
            var list = new List<BinderItem>();
            if (project == null) return list;
            var root = project.Category(Project.KeyTimelines);
            if (root == null) return list;
            foreach (var child in root.Children)
                if (child.Kind == ItemKind.Timeline) list.Add(child);
            return list;
        }

        public static BinderItem FindByTitle(Project project, string title)
        {
            foreach (var item in All(project))
                if (Links.SameTitle((item.Title ?? "").Trim(), (title ?? "").Trim())) return item;
            return null;
        }

        /// <summary>Les frises posées sur ce système.</summary>
        public static int UsesOf(Project project, TimeSystem system)
        {
            var count = 0;
            if (project == null || system == null) return 0;
            foreach (var item in project.AllItems())
                if (item.Kind == ItemKind.Timeline && item.Timeline != null && item.Timeline.SystemId == system.Id) count++;
            return count;
        }

        /// <summary>Le segment (onglet) d'un événement : celui de son début.</summary>
        public static int SegmentOf(TimeSystem system, TimelineEvent entry)
        {
            return system.SegmentIndexOf(entry.Start == null ? 0 : entry.Start.Year);
        }

        public static double StartOf(TimeSystem system, TimelineEvent entry)
        {
            return entry.IsSpan ? system.AxisStart(entry.Start) : system.AxisMid(entry.Start);
        }

        public static double EndOf(TimeSystem system, TimelineEvent entry)
        {
            if (!entry.IsSpan) return system.AxisMid(entry.Start);
            return Math.Max(system.AxisEnd(entry.End), system.AxisStart(entry.Start));
        }

        public static string WhenLabel(TimeSystem system, TimelineEvent entry)
        {
            if (entry == null || entry.Start == null) return "";
            if (!entry.IsSpan) return system.Format(entry.Start);
            return system.Format(entry.Start) + " → " + system.Format(entry.End)
                + " (" + system.DurationLabel(EndOf(system, entry) - system.AxisStart(entry.Start)) + ")";
        }

        public static TimelineStats Stats(TimeSystem system, TimelineInfo timeline)
        {
            var events = new List<TimelineEvent>();
            if (timeline != null) foreach (var entry in timeline.Events) if (!entry.IsPending) events.Add(entry);
            return Stats(system, events);
        }

        /// <summary>Les statistiques d'une liste de briques (celles de la frise et
        /// celles nées des fiches).</summary>
        public static TimelineStats Stats(TimeSystem system, IList<TimelineEvent> events)
        {
            var stats = new TimelineStats();
            var any = false;
            double min = 0, max = 0;
                foreach (var entry in events)
                {
                    if (entry.Start == null) continue;
                    stats.Events++;
                    if (entry.IsSpan) stats.Spans++; else stats.Points++;
                    var from = system.AxisStart(entry.Start);
                    var to = entry.IsSpan ? Math.Max(system.AxisEnd(entry.End), from) : system.AxisEnd(entry.Start);
                    if (!any || from < min) min = from;
                    if (!any || to > max) max = to;
                    any = true;
                }
            if (system.HasSegments)
            {
                for (var i = 0; i < system.Segments.Count; i++)
                {
                    var start = system.SegmentStart(i);
                    var length = system.Segments[i].Length;
                    var open = length <= 0;
                    if (open) length = Math.Max(1, (long)Math.Ceiling(any ? max : 0) - start); // ouvert : jusqu'au dernier événement
                    var segment = new TimelineSegmentStats { Name = system.SegmentName(i), Start = start, Length = length, Open = open };
                        foreach (var entry in events)
                            if (entry.Start != null && SegmentOf(system, entry) == i) segment.Events++;
                    stats.Segments.Add(segment);
                    if (open) break; // rien après un segment ouvert
                }
                var last = stats.Segments[stats.Segments.Count - 1];
                var end = last.Start + last.Length;
                if (!any || 0 < min) min = Math.Min(0, any ? min : 0);
                if (!any || end > max) max = end;
                any = true;
            }
            if (!any) { stats.Min = 0; stats.Max = 0; return stats; }
            stats.Min = min;
            stats.Max = max;
            stats.TotalYears = Math.Max(0, (long)Math.Ceiling(max - 1e-9) - (long)Math.Floor(min + 1e-9));
            return stats;
        }

        /// <summary>Les briques assignées à cet élément, toutes frises confondues.</summary>
        public static List<KeyValuePair<BinderItem, TimelineEvent>> EventsFor(Project project, string itemId)
        {
            var found = new List<KeyValuePair<BinderItem, TimelineEvent>>();
            if (project == null || itemId == null) return found;
            foreach (var timeline in All(project))
                if (timeline.Timeline != null)
                    foreach (var entry in timeline.Timeline.Events)
                        if (entry.LinkId == itemId) found.Add(new KeyValuePair<BinderItem, TimelineEvent>(timeline, entry));
            return found;
        }
    }

    // ============================================================ les liens

    /// <summary>LE LIEN VERS LA TEMPORALITÉ (Ctrl+!) : un [[lien]] comme les
    /// autres dans le texte, dont la cible commence par « date: » —
    /// « [[date:Frise des Âges@E5.902|E5.902]] ». La notation reste du texte :
    /// à l'export, seuls les mots affichés restent.</summary>
    public static partial class TimeLinks
    {
        public const string Prefix = "date:";

        public static bool IsTimeLink(string target)
        {
            return target != null && target.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string Target(string timelineTitle, string notation)
        {
            return Prefix + (timelineTitle ?? "").Trim() + "@" + (notation ?? "").Trim();
        }

        /// <summary>La frise et la date d'une cible « date:Frise@notation ».</summary>
        public static bool Split(string target, out string timelineTitle, out string notation)
        {
            timelineTitle = null;
            notation = null;
            if (!IsTimeLink(target)) return false;
            var body = target.TrimStart().Substring(Prefix.Length);
            var at = body.IndexOf('@');
            if (at < 0) return false;
            timelineTitle = body.Substring(0, at).Trim();
            notation = body.Substring(at + 1).Trim();
            return timelineTitle.Length > 0 && notation.Length > 0;
        }

        /// <summary>La date d'un lien posé dans un CHAMP de fiche (naissance,
        /// décès) : un lien de date, ou — toléré, 12/11 — un lien ordinaire vers
        /// une frise dont le texte affiché se lit comme une date de son système
        /// (« [[Ma frise|E5.878]] » : la fiche de Rémi disait « en vie » sans âge).</summary>
        public static bool SplitField(Project project, Link link, out string timelineTitle, out string notation)
        {
            if (Split(link.Target, out timelineTitle, out notation)) return true;
            timelineTitle = null;
            notation = null;
            if (IsTimeLink(link.Target) || string.IsNullOrEmpty(link.Text)) return false;
            var timeline = Timelines.FindByTitle(project, link.Target);
            TimePoint point;
            if (timeline == null || !Timelines.SystemOf(project, timeline).TryParse(link.Text, out point)) return false;
            timelineTitle = (timeline.Title ?? "").Trim();
            notation = link.Text.Trim();
            return true;
        }

        /// <summary>La notation à insérer : le texte affiché est toujours écrit
        /// (la date, à défaut d'autres mots) pour que la cible reste cachée.</summary>
        public static string Markup(string timelineTitle, string notation, string text)
        {
            var shown = (text ?? "").Trim().Length == 0 || (text ?? "").IndexOf('|') >= 0
                || (text ?? "").IndexOf("]]", StringComparison.Ordinal) >= 0 || (text ?? "").IndexOf('\n') >= 0
                ? (notation ?? "").Trim() : text;
            return "[[" + Target(timelineTitle, notation) + "|" + shown + "]]";
        }

        /// <summary>La frise, son système et la date que vise une cible ; la
        /// frise seule si la date ne se lit plus (notation changée depuis).</summary>
        public static BinderItem Resolve(Project project, string target, out TimeSystem system, out TimePoint point)
        {
            system = null;
            point = null;
            string title, notation;
            if (!Split(target, out title, out notation)) return null;
            var timeline = Timelines.FindByTitle(project, title);
            if (timeline == null) return null;
            system = Timelines.SystemOf(project, timeline);
            TimePoint read;
            if (system.TryParse(notation, out read)) point = read;
            return timeline;
        }

        /// <summary>L'expression sélectionnée est-elle une date d'une des frises
        /// (« E5.902 » tapé puis Ctrl+!) ? La frise préférée d'abord, puis les
        /// systèmes à segments (leur notation est la plus distinctive).</summary>
        public static BinderItem MatchNotation(Project project, string text, BinderItem preferred, out TimePoint point)
        {
            point = null;
            var wanted = (text ?? "").Trim();
            if (wanted.Length == 0 || wanted.IndexOf('\n') >= 0) return null;
            var candidates = new List<BinderItem>();
            if (preferred != null) candidates.Add(preferred);
            var all = Timelines.All(project);
            foreach (var item in all) if (item != preferred && Timelines.SystemOf(project, item).HasSegments) candidates.Add(item);
            foreach (var item in all) if (!candidates.Contains(item)) candidates.Add(item);
            foreach (var item in candidates)
            {
                TimePoint read;
                if (Timelines.SystemOf(project, item).TryParse(wanted, out read)) { point = read; return item; }
            }
            return null;
        }

        /// <summary>La première date qu'un texte lie à une frise (un champ
        /// « Date de naissance »), et sa frise ; null sans lien lisible.</summary>
        public static TimePoint FirstDate(Project project, string text, out BinderItem timeline, out TimeSystem system)
        {
            timeline = null;
            system = null;
            foreach (var link in Links.Find(text ?? ""))
            {
                if (!IsTimeLink(link.Target)) continue;
                TimeSystem found;
                TimePoint point;
                var item = Resolve(project, link.Target, out found, out point);
                if (item == null || point == null) continue;
                timeline = item;
                system = found;
                return point;
            }
            return null;
        }
    }

    // =========================================================== les époques

    // Absent : avant un point de référence ; OutOfTime : dans une période
    // d'exclusion (les dissonances, 12/11 — Dissonances.cs).
    public enum LifeState { Unknown, Unborn, Alive, Dead, Absent, OutOfTime }

    /// <summary>L'état d'un personnage à l'époque choisie : pas encore né,
    /// vivant (et son âge), mort (et l'âge de sa mort).</summary>
    public class LifeStatus
    {
        public LifeState State = LifeState.Unknown;
        public bool HasAge;
        public long Age;
        public string Label = "";
        public bool Excluded;       // l'âge saute une période d'exclusion : « 28 ans (excl.) »
        public string Detail = "";  // hors-temps : « 20 ans en E5.500 » (l'âge du départ)
    }

    /// <summary>L'ÉPOQUE de la bibliothèque des fiches : une frise et une date.
    /// Tant qu'elle est active, chaque fiche dont la date de naissance ou de
    /// décès est un lien vers la temporalité dit qui est né, mort, et son âge.</summary>
    public class EpochState
    {
        public bool Enabled;
        public string TimelineId;
        public string Notation = "";
    }

    public static partial class Epochs
    {
        private static string Key(string name)
        {
            return (name ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>La valeur d'un champ d'une fiche, par son nom (le modèle, les
        /// champs de sous-catégorie, puis les informations libres).</summary>
        public static string FieldValue(Project project, BinderItem sheet, string fieldName)
        {
            if (sheet == null) return null;
            var wanted = Key(fieldName);
            var template = project == null ? null : project.TemplateOf(sheet);
            if (template != null)
                foreach (var field in template.Fields)
                {
                    string value;
                    if (Key(field.Name) == wanted && sheet.FieldValues.TryGetValue(field.Id, out value) && !string.IsNullOrEmpty(value)) return value;
                }
            if (project != null)
                foreach (var category in project.SheetCategories)
                    foreach (var field in category.ExtraFields)
                    {
                        string value;
                        if (Key(field.Name) == wanted && sheet.FieldValues.TryGetValue(field.Id, out value) && !string.IsNullOrEmpty(value)) return value;
                    }
            foreach (var info in sheet.FreeInfo)
                if (Key(info.Title) == wanted && !string.IsNullOrEmpty(info.Value)) return info.Value;
            return null;
        }

        /// <summary>La date d'époque lue dans le système de sa frise ; null si
        /// l'époque est éteinte, sa frise absente ou sa date illisible.</summary>
        public static TimePoint Current(Project project, out TimeSystem system, out BinderItem timeline)
        {
            system = null;
            timeline = null;
            if (project == null || project.Epoch == null || !project.Epoch.Enabled) return null;
            var item = project.FindById(project.Epoch.TimelineId);
            if (item == null || item.Kind != ItemKind.Timeline || item.RootCategory().CategoryKey == Project.KeyTrash) return null;
            var found = Timelines.SystemOf(project, item);
            TimePoint point;
            if (!found.TryParse(project.Epoch.Notation, out point)) return null;
            system = found;
            timeline = item;
            return point;
        }

        /// <summary>L'état d'une fiche à l'époque active (Unknown si l'époque est
        /// éteinte ou si la fiche ne lie aucune date à ce système).</summary>
        public static LifeStatus Evaluate(Project project, BinderItem sheet)
        {
            TimeSystem system;
            BinderItem timeline;
            var at = Current(project, out system, out timeline);
            if (at == null || sheet == null || sheet.Kind != ItemKind.Sheet) return new LifeStatus();
            return Evaluate(project, sheet, system, at);
        }

        /// <summary>La date d'un champ de la fiche, si elle est un lien vers une
        /// frise DE CE SYSTÈME (un âge ne se calcule pas entre deux calendriers).</summary>
        public static TimePoint DateOf(Project project, BinderItem sheet, string fieldName, TimeSystem system)
        {
            var value = FieldValue(project, sheet, fieldName);
            if (value == null) return null;
            foreach (var link in Links.Find(value))
            {
                string title, notation;
                if (!TimeLinks.SplitField(project, link, out title, out notation)) continue;
                var item = Timelines.FindByTitle(project, title);
                TimePoint point;
                if (item != null && Timelines.SystemOf(project, item).Id == system.Id && system.TryParse(notation, out point)) return point;
            }
            return null;
        }
    }
}
