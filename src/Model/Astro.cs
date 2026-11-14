using System;
using System.Collections.Generic;
using System.Globalization;

namespace Marabook.Model
{
    // L'ASTROLABE, AU-DELÀ DES FRISES (1.2.0, 14/11/2026, Rémi) : le MONDE où
    // vivent les calendriers — sa taille, ses saisons, ses lunes. L'astronomie
    // appartient au monde, pas au calendrier : deux cultures d'une même planète
    // partagent lunes et saisons sans compter le temps de la même façon. Un
    // système temporel dit donc dans quel monde il vit (TimeSystem.WorldId).
    //
    // Tout se compte en JOURS depuis l'origine du calendrier (TimeSystem.DayNumber) :
    // la saison d'un jour, la phase d'une lune ce jour-là. Rien ici ne touche
    // l'interface.

    /// <summary>Une saison : son nom et le jour de l'année où elle commence (1 = le premier).</summary>
    public class Season
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public int StartDay = 1;
        public string Color; // "#RRGGBB", null = la teinte de son rang
    }

    /// <summary>Une lune : sa période (d'une pleine lune à la suivante, en
    /// jours) et le jour d'une pleine lune de référence.</summary>
    public class Moon
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public double Period = 29.53;
        public double FullAt;   // le numéro de jour d'une pleine lune
        public string Color;
    }

    public class World
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Monde";
        public double Circumference;   // km à l'équateur ; 0 = non renseignée
        public double AxialTilt = -1;  // degrés ; négatif = non renseignée
        public double SolarYear;       // la durée réelle de l'année, en jours ; 0 = celle du calendrier (aucune dérive)
        public List<Season> Seasons = new List<Season>();
        public List<Moon> Moons = new List<Moon>();

        public bool HasSize { get { return Circumference > 0; } }
        public bool HasTilt { get { return AxialTilt >= 0; } }

        public World Clone()
        {
            var copy = new World { Id = Id, Name = Name, Circumference = Circumference, AxialTilt = AxialTilt, SolarYear = SolarYear };
            foreach (var season in Seasons) copy.Seasons.Add(new Season { Id = season.Id, Name = season.Name, StartDay = season.StartDay, Color = season.Color });
            foreach (var moon in Moons) copy.Moons.Add(new Moon { Id = moon.Id, Name = moon.Name, Period = moon.Period, FullAt = moon.FullAt, Color = moon.Color });
            return copy;
        }

        /// <summary>Notre monde, pour partir de quelque chose : la Terre, ses quatre saisons, la Lune.</summary>
        public static World Earth()
        {
            var world = new World { Name = "Notre monde", Circumference = 40075, AxialTilt = 23.44, SolarYear = 365.2422 };
            // Les saisons de l'hémisphère nord, au jour de l'année (21 décembre = jour 355).
            world.Seasons.Add(new Season { Name = "Hiver", StartDay = 355 });
            world.Seasons.Add(new Season { Name = "Printemps", StartDay = 79 });
            world.Seasons.Add(new Season { Name = "Été", StartDay = 172 });
            world.Seasons.Add(new Season { Name = "Automne", StartDay = 266 });
            world.Moons.Add(new Moon { Name = "Lune", Period = 29.530588, FullAt = 0 });
            return world;
        }
    }

    public static class Astro
    {
        public static readonly string[] SeasonColors = { "#4FA36B", "#D9A441", "#C8703A", "#6E8FB5", "#8E6BBF", "#0E7C7B" };
        public static readonly string[] MoonColors = { "#8A93A6", "#C08497", "#6E8FB5", "#B5843A", "#3E9C7A" };

        public const int Full = 0, LastQuarter = 1, New = 2, FirstQuarter = 3;

        public static World WorldOf(Project project, TimeSystem system)
        {
            if (project == null || system == null || system.WorldId == null) return null;
            foreach (var world in project.Worlds) if (world.Id == system.WorldId) return world;
            return null;
        }

        /// <summary>Les systèmes temporels qui vivent dans ce monde.</summary>
        public static List<TimeSystem> SystemsOf(Project project, World world)
        {
            var systems = new List<TimeSystem>();
            if (project == null || world == null) return systems;
            foreach (var system in project.TimeSystems) if (system.WorldId == world.Id) systems.Add(system);
            return systems;
        }

        public static string ColorOf(Season season, int index)
        {
            return string.IsNullOrEmpty(season.Color) ? SeasonColors[Math.Abs(index) % SeasonColors.Length] : season.Color;
        }

        public static string ColorOf(Moon moon, int index)
        {
            return string.IsNullOrEmpty(moon.Color) ? MoonColors[Math.Abs(index) % MoonColors.Length] : moon.Color;
        }

        private static double Mod(double a, double b)
        {
            var m = a % b;
            return m < 0 ? m + b : m;
        }

        // ---------------------------------------------------------------- saisons

        /// <summary>La durée de l'année solaire : celle du monde, sinon l'année moyenne du calendrier.</summary>
        public static double SolarYear(World world, TimeSystem system)
        {
            return world != null && world.SolarYear > 0 ? world.SolarYear : TimeMath.MeanYearDays(system);
        }

        /// <summary>Le jour de l'année SOLAIRE d'une date (1 = le premier). Sans
        /// durée réelle de l'année, c'est le jour de l'année du calendrier ; avec,
        /// le soleil suit son propre compte et le calendrier dérive.</summary>
        public static double SolarDay(World world, TimeSystem system, TimePoint point)
        {
            if (world == null || world.SolarYear <= 0) return TimeMath.DayOfYear(system, point);
            return Mod(system.DayNumber(TimeMath.Full(point)), world.SolarYear) + 1;
        }

        /// <summary>L'index de la saison d'une date dans world.Seasons ; -1 sans saisons.</summary>
        public static int SeasonIndexAt(World world, TimeSystem system, TimePoint point)
        {
            if (world == null || world.Seasons.Count == 0 || system == null || point == null) return -1;
            var day = SolarDay(world, system, point);
            // La dernière saison commencée ; avant la première de l'année, celle qui commence le plus tard.
            var best = -1;
            var latest = 0;
            for (var i = 0; i < world.Seasons.Count; i++)
            {
                var start = world.Seasons[i].StartDay;
                if (world.Seasons[latest].StartDay < start) latest = i;
                if (start <= day + 1e-9 && (best < 0 || world.Seasons[best].StartDay < start)) best = i;
            }
            return best >= 0 ? best : latest;
        }

        public static Season SeasonAt(World world, TimeSystem system, TimePoint point)
        {
            var index = SeasonIndexAt(world, system, point);
            return index < 0 ? null : world.Seasons[index];
        }

        /// <summary>La dérive du calendrier, en jours par an : positive quand
        /// l'année du calendrier est plus longue que l'année solaire (les
        /// saisons arrivent alors de plus en plus tôt dans le calendrier).</summary>
        public static double DriftPerYear(World world, TimeSystem system)
        {
            if (world == null || world.SolarYear <= 0 || system == null) return 0;
            return TimeMath.MeanYearDays(system) - world.SolarYear;
        }

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private static string Number(double value, int decimals)
        {
            var text = Math.Round(value, decimals).ToString("N" + decimals, French);
            // Les zéros de fin ne s'ôtent qu'après la virgule (« 2 000 » reste « 2 000 », « 23,40 » devient « 23,4 »).
            return decimals > 0 && text.IndexOf(',') >= 0 ? text.TrimEnd('0').TrimEnd(',') : text;
        }

        /// <summary>La dérive dite en clair ; null sans durée réelle de l'année.</summary>
        public static string DriftLabel(World world, TimeSystem system)
        {
            if (world == null || world.SolarYear <= 0 || system == null) return null;
            var drift = DriftPerYear(world, system);
            if (Math.Abs(drift) < 1e-7) return "Le calendrier suit exactement l'année solaire : aucune dérive.";
            var every = 1 / Math.Abs(drift);
            var pace = every >= 2 ? "un jour tous les " + TimeSystem.YearsLabel((long)Math.Round(every)) : Number(Math.Abs(drift), 2) + " jour par an";
            return "L'année du calendrier (" + Number(TimeMath.MeanYearDays(system), 4) + " j) est plus " + (drift > 0 ? "longue" : "courte")
                + " que l'année solaire : les saisons y arrivent plus " + (drift > 0 ? "tôt" : "tard") + ", " + pace + ".";
        }

        /// <summary>Le glissement des saisons sur une durée (en années) : « 58 jours ».</summary>
        public static double DriftOver(World world, TimeSystem system, double years)
        {
            return Math.Abs(DriftPerYear(world, system) * years);
        }

        /// <summary>Quatre saisons égales, pour partir de quelque chose.</summary>
        public static List<Season> FourSeasons(int daysInYear)
        {
            var names = new[] { "Printemps", "Été", "Automne", "Hiver" };
            var seasons = new List<Season>();
            for (var i = 0; i < 4; i++)
                seasons.Add(new Season { Name = names[i], StartDay = 1 + (int)Math.Round(i * Math.Max(4, daysInYear) / 4.0) });
            return seasons;
        }

        // ------------------------------------------------------------------ lunes

        /// <summary>La phase d'une lune à un instant (numéro de jour, fractions
        /// permises) : 0 = pleine lune, 0,5 = nouvelle lune, puis de nouveau vers 1.</summary>
        public static double Phase(Moon moon, double day)
        {
            var period = Math.Max(0.01, moon.Period);
            return Mod(day - moon.FullAt, period) / period;
        }

        /// <summary>La part éclairée du disque : 1 à la pleine lune, 0 à la nouvelle.</summary>
        public static double Illumination(double phase)
        {
            return (1 + Math.Cos(2 * Math.PI * phase)) / 2;
        }

        public static string PhaseName(double phase)
        {
            var names = new[] { "pleine lune", "gibbeuse décroissante", "dernier quartier", "dernier croissant", "nouvelle lune", "premier croissant", "premier quartier", "gibbeuse croissante" };
            return names[(int)Math.Floor(Mod(phase + 1.0 / 16, 1) * 8) % 8];
        }

        /// <summary>La phase principale qui tombe CE JOUR (Full, LastQuarter, New,
        /// FirstQuarter) ; -1 si aucune. Une lune très rapide peut en avoir
        /// plusieurs le même jour : la première l'emporte.</summary>
        public static int MainPhaseOn(Moon moon, long dayNumber)
        {
            var period = Math.Max(0.01, moon.Period);
            var before = Math.Floor((dayNumber - moon.FullAt) / period * 4 - 1e-9);
            var after = Math.Floor((dayNumber + 1 - moon.FullAt) / period * 4 - 1e-9);
            if (after <= before) return -1;
            return (int)Mod(before + 1, 4);
        }

        public static string MainPhaseName(int phase)
        {
            switch (phase)
            {
                case Full: return "pleine lune";
                case LastQuarter: return "dernier quartier";
                case New: return "nouvelle lune";
                case FirstQuarter: return "premier quartier";
                default: return "";
            }
        }

        /// <summary>Le jour de la prochaine pleine lune, ce jour compris.</summary>
        public static long NextFull(Moon moon, long dayNumber)
        {
            var period = Math.Max(0.01, moon.Period);
            var k = Math.Ceiling((dayNumber - moon.FullAt) / period - 1e-9);
            return (long)Math.Floor(moon.FullAt + k * period + 1e-9);
        }

        /// <summary>Le prochain jour où TOUTES les lunes sont pleines ensemble
        /// (à un jour près), à partir d'un jour ; null si rien dans l'horizon
        /// cherché (ou s'il n'y a pas au moins deux lunes).</summary>
        public static long? NextConjunction(World world, long fromDay, int maxFullMoons = 200000)
        {
            if (world == null || world.Moons.Count < 2) return null;
            var slow = world.Moons[0];
            foreach (var moon in world.Moons) if (moon.Period > slow.Period) slow = moon;
            var period = Math.Max(0.01, slow.Period);
            var k = Math.Ceiling((fromDay - slow.FullAt) / period - 1e-9);
            for (var i = 0; i < maxFullMoons; i++, k++)
            {
                var instant = slow.FullAt + k * period;
                var together = true;
                foreach (var moon in world.Moons)
                {
                    if (moon == slow) continue;
                    var other = Math.Max(0.01, moon.Period);
                    var gap = Math.Abs(Mod(instant - moon.FullAt + other / 2, other) - other / 2);
                    if (gap > Math.Min(1.0, other / 4)) { together = false; break; }
                }
                if (together) return (long)Math.Floor(instant + 1e-9);
            }
            return null;
        }

        // ----------------------------------------------------------------- taille

        public static double Radius(World world) { return world.Circumference / (2 * Math.PI); }
        public static double Surface(World world) { var r = Radius(world); return 4 * Math.PI * r * r; }

        /// <summary>La distance à l'horizon (km) pour des yeux à cette hauteur (m).</summary>
        public static double Horizon(World world, double heightMeters)
        {
            return Math.Sqrt(2 * Radius(world) * (heightMeters / 1000.0));
        }

        /// <summary>Ce que le soleil parcourt à l'équateur en une heure du calendrier (km) : la largeur d'un fuseau.</summary>
        public static double HourBand(World world, TimeSystem system)
        {
            return world.Circumference / Math.Max(1, system == null ? 24 : system.HoursPerDay);
        }

        /// <summary>La durée du jour (du lever au coucher, en heures du calendrier)
        /// à une latitude, au solstice d'été (summer) ou d'hiver de cet hémisphère.</summary>
        public static double DayLength(World world, TimeSystem system, double latitude, bool summer)
        {
            var hours = Math.Max(1, system == null ? 24 : system.HoursPerDay);
            if (!world.HasTilt) return hours / 2.0;
            var declination = (summer ? 1 : -1) * world.AxialTilt * Math.PI / 180;
            var cos = -Math.Tan(latitude * Math.PI / 180) * Math.Tan(declination);
            if (cos <= -1) return hours; // le jour polaire
            if (cos >= 1) return 0;      // la nuit polaire
            return hours * Math.Acos(cos) / Math.PI;
        }

        /// <summary>Le tour du monde à l'équateur, en jours, à tant de kilomètres par jour.</summary>
        public static double TravelDays(World world, double kmPerDay)
        {
            return world.Circumference / Math.Max(0.001, kmPerDay);
        }

        public static string Hours(TimeSystem system, double hours)
        {
            var minutes = Math.Max(1, system == null ? 60 : system.MinutesPerHour);
            var whole = (int)Math.Floor(hours + 1e-9);
            var rest = (int)Math.Round((hours - whole) * minutes);
            if (rest >= minutes) { whole++; rest = 0; }
            return whole + " h " + rest.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Les faits que la taille du monde permet de dire, ligne à ligne (la fenêtre du monde).</summary>
        public static List<string> SizeFacts(World world, TimeSystem system)
        {
            var facts = new List<string>();
            if (world == null || !world.HasSize) return facts;
            facts.Add("Rayon : " + Number(Radius(world), 0) + " km  ·  surface : " + Number(Surface(world) / 1e6, 1) + " millions de km²");
            facts.Add("Horizon : " + Number(Horizon(world, 1.7), 1) + " km pour un marcheur, " + Number(Horizon(world, 30), 1) + " km du haut d'une tour de 30 m");
            var hours = Math.Max(1, system == null ? 24 : system.HoursPerDay);
            facts.Add("Une heure de soleil (le jour en compte " + hours + ") : " + Number(HourBand(world, system), 0) + " km à l'équateur");
            var year = system == null ? 365.25 : TimeMath.MeanYearDays(system);
            Func<double, string> travel = delegate(double kmPerDay)
            {
                var days = TravelDays(world, kmPerDay);
                return Number(days, 0) + " jours" + (days >= year ? " (" + Number(days / year, 1) + " ans)" : "");
            };
            facts.Add("Le tour du monde : à pied (30 km/j) " + travel(30) + "  ·  à cheval (60 km/j) " + travel(60) + "  ·  à la voile (150 km/j) " + travel(150));
            if (world.HasTilt)
            {
                facts.Add("Tropiques à " + Number(world.AxialTilt, 1) + "° de l'équateur  ·  cercles polaires à " + Number(90 - world.AxialTilt, 1) + "°");
                var lines = new List<string>();
                foreach (var latitude in new[] { 0.0, 30, 45, 60 })
                    lines.Add(Number(latitude, 0) + "° : " + Hours(system, DayLength(world, system, latitude, false)) + " → " + Hours(system, DayLength(world, system, latitude, true)));
                facts.Add("Durée du jour, du solstice d'hiver à celui d'été — " + string.Join("  ·  ", lines.ToArray()));
            }
            return facts;
        }

        // --------------------------------------------------------------- en clair

        /// <summary>Un monde en une phrase (sa tuile dans l'Astrolabe).</summary>
        public static string Summary(World world)
        {
            var parts = new List<string>();
            if (world.HasSize) parts.Add(Number(world.Circumference, 0) + " km de tour");
            if (world.Seasons.Count > 0) parts.Add(TimeSystem.Count(world.Seasons.Count, "saison", "saisons"));
            if (world.Moons.Count > 0) parts.Add(TimeSystem.Count(world.Moons.Count, "lune", "lunes"));
            return parts.Count == 0 ? "Rien de renseigné pour l'instant." : string.Join(" · ", parts.ToArray());
        }

        /// <summary>Le ciel d'un jour : « Été · Lune : pleine lune » ; la saison
        /// seule pour une date sans jour ; null sans monde ou sans rien à dire.</summary>
        public static string DayLabel(Project project, TimeSystem system, TimePoint point)
        {
            var world = WorldOf(project, system);
            if (world == null || point == null) return null;
            var parts = new List<string>();
            if (point.HasMonth)
            {
                var season = SeasonAt(world, system, point);
                if (season != null && !string.IsNullOrEmpty(season.Name)) parts.Add(season.Name);
            }
            if (point.HasDay)
            {
                var day = system.DayNumber(point);
                foreach (var moon in world.Moons)
                {
                    var main = MainPhaseOn(moon, day);
                    parts.Add((string.IsNullOrEmpty(moon.Name) ? "Lune" : moon.Name) + " : " + (main >= 0 ? MainPhaseName(main) : PhaseName(Phase(moon, day + 0.5))));
                }
            }
            return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
        }
    }
}
