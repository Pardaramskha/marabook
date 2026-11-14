using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C67 — l'arithmétique des dates (1.2.0, 14/11) : le numéro du
    /// jour et son inverse, une date plus ou moins une durée, le temps entre
    /// deux dates, la répartition des jours par semaines, ce qu'un calendrier
    /// peut ranger dans ses cases (et ses orphelins), la racine « Astrolabe ».</summary>
    public static class TimeMathTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C67 — arithmétique des dates, calendrier, Astrolabe");
            DayNumbers(t);
            Adding(t);
            Distances(t);
            Distribution(t);
            CalendarCells(t);
            Astrolabe(t);
        }

        private static TimePoint At(TimeSystem system, string notation)
        {
            TimePoint point;
            if (!system.TryParse(notation, out point)) throw new InvalidOperationException("date illisible : " + notation);
            return point;
        }

        private static TimeSystem Tides()
        {
            var project = Project.CreateNew();
            var timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            return Timelines.SystemOf(project, timeline);
        }

        private static void DayNumbers(Harness t)
        {
            var world = TimeSystem.Gregorian();
            var tides = Tides();
            foreach (var system in new[] { world, tides })
            {
                // L'inverse exact, sur des milliers d'années, de part et d'autre de l'origine.
                var broken = 0;
                var step = system.BaseDays * 7 + 13;
                for (long n = -400000; n <= 3200000; n += step)
                {
                    var point = TimeMath.FromDayNumber(system, n);
                    if (system.DayNumber(point) != n || point.Day < 1 || point.Day > system.DaysInMonth(point.Year, point.Month)) broken++;
                }
                t.Equal(0, broken, system.Name + " : numéro de jour → date → numéro de jour, sans perte");
                // Jour après jour sur deux ans : chaque jour suit le précédent.
                var previous = TimeMath.FromDayNumber(system, -3);
                var jumps = 0;
                for (long n = -2; n < system.BaseDays * 2 + 10; n++)
                {
                    var point = TimeMath.FromDayNumber(system, n);
                    var next = previous.Day < system.DaysInMonth(previous.Year, previous.Month)
                        ? point.Year == previous.Year && point.Month == previous.Month && point.Day == previous.Day + 1
                        : point.Day == 1 && (point.Month == previous.Month + 1 || (point.Month == 1 && point.Year == previous.Year + 1));
                    if (!next) jumps++;
                    previous = point;
                }
                t.Equal(0, jumps, system.Name + " : les jours se suivent, fins de mois et d'année comprises");
            }
            t.Equal("14 juillet 1789", world.Format(TimeMath.FromDayNumber(world, world.DayNumber(At(world, "14 juillet 1789")))), "le 14 juillet 1789 revient du numéro de son jour");
            t.Check(Math.Abs(TimeMath.MeanYearDays(world) - 365.2425) < 1e-9, "l'année moyenne de notre monde : 365,2425 jours (" + TimeMath.MeanYearDays(world) + ")");
            t.Equal(1, TimeMath.DayOfYear(world, At(world, "1 janvier 2026")), "le premier jour de l'année est le jour 1");
            t.Equal(366, TimeMath.DayOfYear(world, At(world, "31 décembre 2024")), "le 31 décembre d'une bissextile : jour 366");
            t.Equal(60, TimeMath.DayOfYear(world, At(world, "mars 2026")), "une date sans jour compte pour le premier du mois (1er mars : jour 60)");
        }

        private static void Adding(Harness t)
        {
            var world = TimeSystem.Gregorian();
            Func<string, long, long, long, string> add = delegate(string from, long years, long months, long days)
            {
                return world.Format(TimeMath.Add(world, At(world, from), years, months, days));
            };
            t.Equal("23 août 1789", add("14 juillet 1789", 0, 0, 40), "14 juillet + 40 jours = 23 août");
            t.Equal("4 juin 1789", add("14 juillet 1789", 0, 0, -40), "14 juillet − 40 jours = 4 juin");
            t.Equal("29 février 2024", add("31 janvier 2024", 0, 1, 0), "31 janvier + 1 mois : le jour est ramené dans février (bissextile : le 29)");
            t.Equal("28 février 2026", add("31 janvier 2026", 0, 1, 0), "…le 28 une année ordinaire");
            t.Equal("28 février 2025", add("29 février 2024", 1, 0, 0), "29 février + 1 an = 28 février");
            t.Equal("1 janvier 2027", add("1 décembre 2026", 0, 1, 0), "décembre + 1 mois passe l'année");
            t.Equal("1 novembre 2025", add("1 février 2026", 0, -3, 0), "février − 3 mois recule d'une année");
            t.Equal("15 mars 2031", add("1 janvier 2026", 5, 2, 14), "années, mois puis jours, dans cet ordre");
            t.Equal("1 janvier 2027", add("2026", 1, 0, 0), "une année seule compte pour son premier jour");
            t.Equal("1 mars 2000", add("29 février 2000", 0, 0, 1), "le lendemain d'un 29 février");

            var tides = Tides();
            t.Equal("36 Longue-Nuit E2.1200", tides.Format(TimeMath.AddDays(tides, At(tides, "1 Aubemer E3.1"), -1)), "la veille du premier jour d'une ère est le dernier de la précédente");
            t.Equal("1 Aubemer E3.1", tides.Format(TimeMath.AddDays(tides, At(tides, "36 Longue-Nuit E2.1200"), 1)), "…et l'inverse");
            t.Equal("1 Aubemer E3.31", tides.Format(TimeMath.AddDays(tides, At(tides, "1 Aubemer E3.30"), 365)), "E3.30 est bissextile dans ce calendrier : 365 jours (360 + 5)");
        }

        private static void Distances(Harness t)
        {
            var world = TimeSystem.Gregorian();
            var revolution = TimeMath.Between(world, At(world, "14 juillet 1789"), At(world, "9 novembre 1799"));
            t.Check(revolution.Years == 10 && revolution.Months == 3 && revolution.Days == 26 && !revolution.Backwards, "du 14 juillet 1789 au 9 novembre 1799 : 10 ans, 3 mois et 26 jours ("
                + TimeMath.Label(revolution) + ")");
            t.Equal(3770L, revolution.TotalDays, "…soit 3 770 jours");
            t.Equal("10 ans, 3 mois et 26 jours", TimeMath.Label(revolution), "dit en clair");
            var back = TimeMath.Between(world, At(world, "9 novembre 1799"), At(world, "14 juillet 1789"));
            t.Check(back.Backwards && back.TotalDays == 3770 && back.Years == 10, "à rebours : la même durée, marquée « plus tôt »");
            t.Equal("le même jour", TimeMath.Label(TimeMath.Between(world, At(world, "1 mars 2026"), At(world, "1 mars 2026"))), "aucune durée : « le même jour »");
            t.Equal("1 an", TimeMath.Label(TimeMath.Between(world, At(world, "29 février 2024"), At(world, "28 février 2025"))), "du 29 février au 28 février suivant : un an");
            t.Equal("1 jour", TimeMath.Label(TimeMath.Between(world, At(world, "28 février 2026"), At(world, "1 mars 2026"))), "singulier");
            // La durée ajoutée ramène à la seconde date.
            var cases = new[] { "3 mars 2026|28 juin 2026", "31 janvier 2024|1 mars 2025", "14 juillet 1789|1 janvier 1800", "30 novembre 1999|29 février 2000" };
            foreach (var pair in cases)
            {
                var from = At(world, pair.Split('|')[0]);
                var to = At(world, pair.Split('|')[1]);
                var distance = TimeMath.Between(world, from, to);
                t.Equal(world.Format(to), world.Format(TimeMath.Add(world, from, distance.Years, distance.Months, distance.Days)), "date + (durée entre les deux) = l'autre date (" + pair + " : " + TimeMath.Label(distance) + ")");
            }
            var tides = Tides();
            var novel = TimeMath.Between(tides, At(tides, "3 Brisants E3.30"), At(tides, "20 Noroît E3.30"));
            t.Check(novel.Years == 0 && novel.Months == 4 && novel.Days == 17 && novel.TotalDays == 161, "calendrier des Marées : du 3 Brisants au 20 Noroît, 4 mois et 17 jours (161 jours)");
        }

        private static void Distribution(Harness t)
        {
            // Le cas de Rémi : treize mois, cinq semaines par mois, des semaines de six jours.
            var system = new TimeSystem();
            for (var i = 1; i <= 13; i++) system.Months.Add(new TimeMonth { Name = "Mois " + i, Days = 28 + i % 3 });
            system.WeekDays.AddRange(new[] { "un", "deux", "trois", "quatre", "cinq", "six" });
            t.Check(TimeMath.Distribute(system, 5), "treize mois, cinq semaines de six jours : la répartition se fait");
            t.Check(system.Months.TrueForAll(delegate(TimeMonth m) { return m.Days == 30; }) && system.BaseDays == 390, "…30 jours par mois, 390 par an");
            var aligned = true;
            for (var year = 0; year < 3; year++)
                for (var month = 1; month <= 13; month++)
                    if (system.WeekDayOf(new TimePoint(year, month, 1)) != "un") aligned = false;
            t.Check(aligned, "…et chaque mois commence au premier jour de la semaine");
            var bare = new TimeSystem();
            bare.Months.Add(new TimeMonth { Name = "Seul", Days = 17 });
            t.Check(!TimeMath.Distribute(bare, 5) && bare.Months[0].Days == 17, "sans semaine : rien ne change");
            t.Check(!TimeMath.Distribute(system, 0) && system.Months[0].Days == 30, "zéro semaine : rien ne change");
        }

        private static void CalendarCells(Harness t)
        {
            var tides = Tides();
            Func<string, string, TimelineEvent> make = delegate(string start, string end)
            {
                return new TimelineEvent { Title = start, Start = At(tides, start), End = end == null ? null : At(tides, end) };
            };
            var day = make("14 Noroît E3.30", null);
            var span = make("3 Brisants E3.30", "20 Noroît E3.30");
            var year = make("E2.905", null);
            var month = make("Sel E3.8", null);
            var loose = make("E2.430", "E2.470");
            var half = make("3 Brisants E3.30", "Noroît E3.30");
            t.Check(TimeMath.IsDayPrecise(day) && TimeMath.IsDayPrecise(span), "un jour, une période d'un jour à un jour : des cases");
            t.Check(!TimeMath.IsDayPrecise(year) && !TimeMath.IsDayPrecise(month) && !TimeMath.IsDayPrecise(loose) && !TimeMath.IsDayPrecise(half), "à l'année, au mois, ou une fin sans jour : orphelins");
            var number = tides.DayNumber(At(tides, "14 Noroît E3.30"));
            t.Check(TimeMath.CoversDay(tides, day, number) && !TimeMath.CoversDay(tides, day, number + 1), "un événement d'un jour couvre ce jour, pas le lendemain");
            t.Check(TimeMath.CoversDay(tides, span, number) && TimeMath.CoversDay(tides, span, tides.DayNumber(At(tides, "20 Noroît E3.30")))
                && !TimeMath.CoversDay(tides, span, tides.DayNumber(At(tides, "21 Noroît E3.30"))) && !TimeMath.CoversDay(tides, span, tides.DayNumber(At(tides, "2 Brisants E3.30"))),
                "une période couvre chaque jour de son début à sa fin, bornes comprises");
            t.Check(!TimeMath.CoversDay(tides, year, tides.DayNumber(year.Start)), "un orphelin ne couvre aucune case");
            t.Check(TimeMath.OrphanIn(month, month.Start.Year, 4) && !TimeMath.OrphanIn(month, month.Start.Year, 5) && !TimeMath.OrphanIn(month, month.Start.Year, 0), "daté au mois : orphelin de ce mois, pas de l'année");
            t.Check(TimeMath.OrphanIn(year, year.Start.Year, 0) && !TimeMath.OrphanIn(year, year.Start.Year, 1) && !TimeMath.OrphanIn(year, year.Start.Year + 1, 0), "daté à l'année : orphelin de cette année");
            t.Check(TimeMath.OrphanIn(loose, loose.Start.Year + 20, 0) && !TimeMath.OrphanIn(loose, loose.End.Year + 1, 0), "une période à l'année : orpheline de chaque année qu'elle traverse");
            t.Check(TimeMath.OrphanIn(half, half.Start.Year, 7) && !TimeMath.OrphanIn(half, half.Start.Year, 10), "une période à la fin sans jour : orpheline des mois qu'elle traverse");
            t.Check(!TimeMath.OrphanIn(day, day.Start.Year, 9) && !TimeMath.OrphanIn(day, day.Start.Year, 0), "un événement daté au jour n'est jamais orphelin");
            t.Check(TimeMath.TouchesYear(loose, loose.Start.Year + 11) && !TimeMath.TouchesYear(loose, loose.Start.Year - 1) && TimeMath.TouchesYear(year, year.Start.Year) && !TimeMath.TouchesYear(year, year.Start.Year + 1),
                "le survol : une année éclaire l'événement qui s'y trouve et la période qui la traverse");
        }

        private static void Astrolabe(Harness t)
        {
            var project = Project.CreateNew();
            t.Equal("Astrolabe", project.Category(Project.KeyTimelines).Title, "un projet neuf : la racine des frises s'appelle « Astrolabe »");
            // Un projet d'avant, dont la racine s'appelait « Frises », est relu « Astrolabe ».
            project.Category(Project.KeyTimelines).Title = "Frises";
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c67-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "avant.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Equal("Astrolabe", loaded.Category(Project.KeyTimelines).Title, "un .plot dont la racine s'appelait « Frises » : relu « Astrolabe »");
                t.Equal(Project.KeyTimelines, loaded.Category(Project.KeyTimelines).CategoryKey, "…la clé de la racine ne change pas (rien à migrer dans le fichier)");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
