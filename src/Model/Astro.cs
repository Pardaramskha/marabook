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

    /// <summary>L'ALLURE D'UN ASTRE (lot 14, 16/11, Rémi : « personnaliser l'astre ») — pour un soleil, un
    /// satellite ou le monde lui-même : une couleur d'accent (le halo d'un soleil, l'atmosphère du monde),
    /// des anneaux, une inclinaison (l'axe d'un soleil ; l'orbite d'un satellite, penchée sur celle du
    /// monde) et, pour un satellite, la distance qui le sépare de l'astre autour duquel il tourne.</summary>
    public class BodyLook
    {
        public const double MoonDistance = 384400;   // km : de la Terre à la Lune, la séparation par défaut d'un satellite
        // Les valeurs de NOTRE système (lot 16, 17/11 : le calculateur orbital part d'elles).
        public const double SunDistance = 149597870.7; // km : de la Terre au Soleil, la séparation par défaut d'un soleil
        public const double MoonDiameter = 3474.8;     // km
        public const double MoonTilt = 5.145;          // degrés : l'orbite de la Lune sur celle de la Terre
        public const double SunTilt = 7.25;            // degrés : l'axe du Soleil sur l'orbite de la Terre

        public string Accent;                 // "#RRGGBB", null = la teinte de l'astre
        public double Distance = MoonDistance; // km, à l'astre autour duquel il tourne
        public double DefaultDistance = MoonDistance; // ce que vaut « d'office » pour cet astre (un soleil : SunDistance)
        public double Diameter;               // km (satellites) ; 0 = celui de notre Lune. Un soleil garde Sun.Size.
        public bool Rings;
        public string RingColor;              // null = la teinte de l'astre, pâlie
        public double RingSize = 1;           // l'ampleur : 1 = des anneaux comme ceux de Saturne, à l'échelle de l'astre
        public double RingGap = 1;            // l'écart entre l'astre et le bord intérieur (0 = au ras de l'astre)
        public double RingThickness = 1;      // la largeur de la bande, du bord intérieur au bord extérieur
        public double RingAngle;              // degrés : le plan des anneaux, penché sur l'équateur de l'astre
        public bool RingTexture = true;       // le grain (texture) ; faux = des bandes lisses
        public double RingGrain = 1;          // l'échelle du grain : plus grand = un détail plus gros
        // Lot 18 : la densité de la ceinture, de 0 à 1 — l'ombre qu'elle porte sur son astre. À 0, les anneaux
        // se voient sans rien assombrir ; à 1, sous le grand anneau, il fait nuit.
        public const double DefaultDensity = 0.6;
        public double RingDensity = DefaultDensity;
        public double Tilt;                   // degrés

        /// <summary>L'allure d'office d'un soleil : la même, à la distance de la Terre au Soleil.</summary>
        public static BodyLook ForSun()
        {
            return new BodyLook { Distance = SunDistance, DefaultDistance = SunDistance };
        }

        public bool IsDefault
        {
            get
            {
                return string.IsNullOrEmpty(Accent) && Math.Abs(Distance - DefaultDistance) < 1e-6 && Diameter <= 0 && !Rings && string.IsNullOrEmpty(RingColor) && Math.Abs(RingSize - 1) < 1e-9
                    && Math.Abs(RingGap - 1) < 1e-9 && Math.Abs(RingThickness - 1) < 1e-9 && Math.Abs(RingAngle) < 1e-9 && RingTexture && Math.Abs(RingGrain - 1) < 1e-9 && Math.Abs(RingDensity - DefaultDensity) < 1e-9 && Math.Abs(Tilt) < 1e-9;
            }
        }

        /// <summary>Le diamètre d'un satellite, en km : celui qu'on lui a donné, sinon celui de notre Lune.</summary>
        public double DiameterKm { get { return Diameter > 0 ? Diameter : MoonDiameter; } }

        /// <summary>D'où à où vont les anneaux, en RAYONS de l'astre (1 = sa surface) : l'écart les éloigne,
        /// l'épaisseur les élargit, l'ampleur grandit le tout. D'office : de 1,35 à 2,1 — à peu près Saturne.</summary>
        public void RingSpan(out double inner, out double outer)
        {
            var size = Math.Max(0.2, Math.Min(4, RingSize));
            inner = 1 + 0.35 * Math.Max(0, Math.Min(3, RingGap)) * size;
            outer = inner + 0.75 * Math.Max(0.1, Math.Min(3, RingThickness)) * size;
        }

        public BodyLook Clone()
        {
            return new BodyLook
            {
                Accent = Accent, Distance = Distance, DefaultDistance = DefaultDistance, Diameter = Diameter, Rings = Rings, RingColor = RingColor, RingSize = RingSize,
                RingGap = RingGap, RingThickness = RingThickness, RingAngle = RingAngle, RingTexture = RingTexture, RingGrain = RingGrain, RingDensity = RingDensity, Tilt = Tilt
            };
        }

        /// <summary>« 1,2 × notre Lune » : une grandeur dite en fois celle de référence.</summary>
        public static string TimesLabel(double value, double reference, string what)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return (value / reference).ToString("0.##", fr) + " × " + what;
        }

        /// <summary>« 1,5 × la distance de la Terre à la Lune ».</summary>
        public static string DistanceLabel(double km)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return (km / MoonDistance).ToString("0.##", fr) + " × la distance de la Terre à la Lune";
        }
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
        public string OrbitId;  // 16/11 : la lune autour de laquelle elle tourne ; null = autour du monde
        public int Eclipses;    // Astro.EclipseSeasons (comme la nôtre), EclipseAlways, EclipseNever
        public BodyLook Look = new BodyLook(); // lot 14 : accent, distance, anneaux, inclinaison de l'orbite
    }

    /// <summary>Un soleil (15/11, Rémi : des systèmes planétaires autres que le
    /// nôtre). Le PREMIER soleil d'un monde fait le jour et les saisons ; les
    /// suivants sont ses compagnons. Un compagnon sans période partage le ciel
    /// de jour du premier (deux soleils serrés) ; avec une période (en jours),
    /// il fait le tour du ciel : au plus haut à minuit le jour HighAt — la nuit
    /// reste claire —, puis de retour auprès du premier une demi-période plus tard.</summary>
    public class Sun
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public double Period;   // jours ; 0 = suit le premier soleil
        public double HighAt;   // le numéro d'un jour où il culmine à minuit
        public string Color;
        public string OrbitId;  // 16/11 : le soleil autour duquel il tourne ; null = « centrique », au milieu du système
        // Sa taille, en « fois notre Soleil » (son diamètre ; 16/11 soir) : ce qui décide, quand deux
        // soleils passent l'un devant l'autre, si l'éclipse est totale ou annulaire.
        public double Size = 1;
        public BodyLook Look = BodyLook.ForSun(); // lot 14 : accent du halo, anneaux, inclinaison de l'axe ; lot 16 : sa distance
    }

    /// <summary>UNE PLANÈTE VOISINE (16/11 soir, Rémi : le Planétarium) : un astre du
    /// système qui n'a ni calendrier ni récit — on le voit depuis le monde. Un
    /// nom ; au besoin la durée de son année (en jours du calendrier de
    /// référence) et le jour d'un alignement avec le monde, pour dater ses retours.</summary>
    public class Planet
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public double Period;   // jours ; 0 = non renseignée (elle n'est que placée)
        public double At;       // le numéro d'un jour où elle est alignée avec le monde et le soleil
        public string Color;
    }

    /// <summary>Un astre à sa place dans le système (Astro.Layout) : un soleil, le monde, une planète.</summary>
    public class SkyBody
    {
        public const int SunKind = 0, WorldKind = 1, PlanetKind = 2;
        public int Kind;
        public string Id;       // l'identifiant du soleil ou de la planète ; Astro.WorldToken pour le monde
        public string Name;
        public int Rank;        // 0 = au centre ; 1, 2, 3… en s'éloignant
        public Sun Sun;
        public Planet Planet;
        public bool Movable;    // un soleil centrique ne quitte pas le centre
    }

    /// <summary>Un PHÉNOMÈNE PERSONNALISÉ (16/11, Rémi) : ce qui revient dans le
    /// ciel à période fixe et que les astres du monde ne suffisent pas à dire —
    /// une comète, une pluie d'étoiles, la « lune rousse » d'une légende. Un
    /// nom, une période (en jours) et le jour d'un passage de référence.</summary>
    public class SkyEvent
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public double Period = 365;
        public double At;       // le numéro de jour d'un passage
        public string Color;
    }

    /// <summary>Ce que le ciel fait d'un jour, au-delà des phases : une éclipse,
    /// une lune bleue, toutes les lunes pleines ensemble, un phénomène personnalisé.</summary>
    public class SkyNote
    {
        public string Text;     // « Lune : éclipse totale (lune rousse) »
        public string Detail;   // la précédente et la prochaine, pour l'infobulle ; null sans
        public string Color;    // "#RRGGBB"
    }

    /// <summary>Un comportement que l'agencement des astres déclenche — dit en
    /// clair dans la fenêtre du monde, sous les soleils et sous les lunes.</summary>
    public class SkyBehavior
    {
        public string Title;
        public string Text;
        public bool Notable;    // vrai : sort de l'ordinaire (plusieurs soleils, éclipses à chaque lunaison…)
    }

    public class SizeFact
    {
        public string Label, Value, Note;
    }

    public class SizeFactGroup
    {
        public string Title;
        public List<SizeFact> Facts = new List<SizeFact>();
    }

    /// <summary>UN MARQUEUR DE NAVIGATION (lot 13, 16/11, Rémi : « équateur, tropiques, etc. ») : un
    /// parallèle du monde, nommé — une latitude en degrés, positive au nord. Les marqueurs par défaut
    /// se déduisent de l'inclinaison de l'axe ; l'auteur peut les remplacer par les siens.</summary>
    public class WorldGuide
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public double Latitude;   // degrés, de −90 (pôle sud) à 90 (pôle nord)
        public string Color;      // "#RRGGBB", null = la teinte de son rang
    }

    public class World
    {
        // LES MARQUEURS DE NAVIGATION (lot 13). CustomGuides faux = ceux par défaut, déduits de
        // l'inclinaison (Astro.DefaultGuides) ; vrai = la liste Guides, telle que l'auteur l'a écrite.
        // L'allure du monde lui-même (lot 14) : l'accent de son atmosphère, ses anneaux.
        public BodyLook Look = new BodyLook();
        public bool CustomGuides;
        public List<WorldGuide> Guides = new List<WorldGuide>();
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Monde";
        public double Circumference;   // km à l'équateur ; 0 = non renseignée
        public double AxialTilt = -1;  // degrés ; négatif = non renseignée
        public double SolarYear;       // la durée réelle de l'année, en jours ; 0 = celle du calendrier (aucune dérive)
        public List<Season> Seasons = new List<Season>();
        public List<Moon> Moons = new List<Moon>();
        // Les soleils (15/11) ; vide = un soleil dont on ne dit rien (les mondes d'avant).
        public List<Sun> Suns = new List<Sun>();
        // Le calendrier dans lequel la fenêtre du monde compte les jours et écrit
        // les dates (15/11) ; null = le premier calendrier du monde.
        public string ReferenceSystemId;
        // Les phénomènes personnalisés (16/11) : une comète, une pluie d'étoiles, la lune rousse des légendes.
        public List<SkyEvent> Phenomena = new List<SkyEvent>();
        // LE PLACEMENT STELLAIRE (16/11 soir, Rémi) : les planètes voisines, et l'ordre des astres en
        // s'éloignant du centre — les identifiants des soleils qui orbitent, des planètes, et
        // Astro.WorldToken pour le monde. Les soleils centriques, au centre, n'y figurent pas.
        public List<Planet> Planets = new List<Planet>();
        public List<string> Order = new List<string>();

        public bool HasSize { get { return Circumference > 0; } }
        public bool HasTilt { get { return AxialTilt >= 0; } }

        public World Clone()
        {
            var copy = new World { Id = Id, Name = Name, Circumference = Circumference, AxialTilt = AxialTilt, SolarYear = SolarYear, ReferenceSystemId = ReferenceSystemId };
            foreach (var sun in Suns) copy.Suns.Add(new Sun { Id = sun.Id, Name = sun.Name, Period = sun.Period, HighAt = sun.HighAt, Color = sun.Color, OrbitId = sun.OrbitId, Size = sun.Size, Look = sun.Look.Clone() });
            foreach (var season in Seasons) copy.Seasons.Add(new Season { Id = season.Id, Name = season.Name, StartDay = season.StartDay, Color = season.Color });
            foreach (var moon in Moons) copy.Moons.Add(new Moon { Id = moon.Id, Name = moon.Name, Period = moon.Period, FullAt = moon.FullAt, Color = moon.Color, OrbitId = moon.OrbitId, Eclipses = moon.Eclipses, Look = moon.Look.Clone() });
            foreach (var item in Phenomena) copy.Phenomena.Add(new SkyEvent { Id = item.Id, Name = item.Name, Period = item.Period, At = item.At, Color = item.Color });
            foreach (var planet in Planets) copy.Planets.Add(new Planet { Id = planet.Id, Name = planet.Name, Period = planet.Period, At = planet.At, Color = planet.Color });
            copy.Order.AddRange(Order);
            copy.CustomGuides = CustomGuides;
            copy.Look = Look.Clone();
            foreach (var guide in Guides) copy.Guides.Add(new WorldGuide { Id = guide.Id, Name = guide.Name, Latitude = guide.Latitude, Color = guide.Color });
            return copy;
        }

        /// <summary>Notre monde, pour partir de quelque chose : la Terre, ses
        /// quatre saisons, le Soleil, la Lune. C'est aussi le brouillon de tout
        /// monde neuf (15/11, Rémi) : des valeurs qu'on sait mesurer, à changer.</summary>
        public static World Earth()
        {
            var world = new World { Name = "Notre monde", Circumference = Astro.EarthCircumference, AxialTilt = 23.44, SolarYear = 365.2422 };
            // Les saisons de l'hémisphère nord, au jour de l'année (20 mars = jour 79, 21 décembre = jour 355).
            world.Seasons.Add(new Season { Name = "Printemps", StartDay = 79 });
            world.Seasons.Add(new Season { Name = "Été", StartDay = 172 });
            world.Seasons.Add(new Season { Name = "Automne", StartDay = 266 });
            world.Seasons.Add(new Season { Name = "Hiver", StartDay = 355 });
            world.Suns.Add(new Sun { Name = "Soleil" });
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

        // ------------------------------------------------------------------ marqueurs de navigation (lot 13)

        public static readonly string[] GuideColors = { "#E8B44A", "#D9603A", "#D9603A", "#6E8FB5", "#6E8FB5", "#8E6BBF", "#3E9C7A", "#0E7C7B" };

        /// <summary>Les marqueurs par défaut d'un monde : l'équateur toujours ; avec une inclinaison de
        /// l'axe, les deux tropiques (là où le soleil passe au zénith au plus loin de l'équateur) et les
        /// deux cercles polaires (au-delà, le soleil ne se couche plus, ou ne se lève plus, une partie de l'année).</summary>
        public static List<WorldGuide> DefaultGuides(World world)
        {
            var list = new List<WorldGuide> { new WorldGuide { Id = "equator", Name = "Équateur", Latitude = 0 } };
            if (world == null || !world.HasTilt || world.AxialTilt < 0.01 || world.AxialTilt > 89.99) return list;
            var tilt = world.AxialTilt;
            list.Add(new WorldGuide { Id = "tropic-n", Name = "Tropique nord", Latitude = tilt });
            list.Add(new WorldGuide { Id = "tropic-s", Name = "Tropique sud", Latitude = -tilt });
            list.Add(new WorldGuide { Id = "polar-n", Name = "Cercle polaire nord", Latitude = 90 - tilt });
            list.Add(new WorldGuide { Id = "polar-s", Name = "Cercle polaire sud", Latitude = -(90 - tilt) });
            return list;
        }

        /// <summary>Les marqueurs en vigueur : ceux de l'auteur s'il les a personnalisés, sinon ceux par défaut.</summary>
        public static List<WorldGuide> GuidesOf(World world)
        {
            if (world == null || !world.CustomGuides) return DefaultGuides(world);
            return world.Guides;
        }

        public static string ColorOf(WorldGuide guide, int index)
        {
            return string.IsNullOrEmpty(guide.Color) ? GuideColors[Math.Abs(index) % GuideColors.Length] : guide.Color;
        }

        /// <summary>« 23,4° N », « 0° », « 66,6° S ».</summary>
        public static string LatitudeLabel(double degrees)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var value = Math.Abs(degrees).ToString("0.#", fr);
            return value == "0" ? "0°" : value + "° " + (degrees > 0 ? "N" : "S");
        }

        /// <summary>« 12,5° E », « 0° », « 180° » — la longitude, comptée du méridien du centre de la carte.</summary>
        public static string LongitudeLabel(double degrees)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            degrees = degrees - 360 * Math.Round(degrees / 360);
            var value = Math.Abs(degrees).ToString("0.#", fr);
            return value == "0" || value == "180" ? value + "°" : value + "° " + (degrees > 0 ? "E" : "O");
        }

        public static string ColorOf(Season season, int index)
        {
            return string.IsNullOrEmpty(season.Color) ? SeasonColors[Math.Abs(index) % SeasonColors.Length] : season.Color;
        }

        public static string ColorOf(Moon moon, int index)
        {
            return string.IsNullOrEmpty(moon.Color) ? MoonColors[Math.Abs(index) % MoonColors.Length] : moon.Color;
        }

        public static readonly string[] SunColors = { "#E0A526", "#D9603A", "#5B9BD5", "#B86FC2" };

        /// <summary>La circonférence de la Terre à l'équateur, en kilomètres.</summary>
        public const double EarthCircumference = 40075;

        public static string ColorOf(Sun sun, int index)
        {
            return string.IsNullOrEmpty(sun.Color) ? SunColors[Math.Abs(index) % SunColors.Length] : sun.Color;
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

        /// <summary>Ce qui cause le glissement et ce qu'il donne dans le temps
        /// (16/11, Rémi), ligne à ligne ; vide sans glissement. La cause
        /// d'abord, puis l'écart après 10, 100 et 1 000 ans, puis ce qu'il
        /// faudrait retoucher au CALENDRIER pour garder cette année réelle.</summary>
        public static List<string> DriftReport(World world, TimeSystem system)
        {
            var lines = new List<string>();
            var drift = DriftPerYear(world, system);
            if (Math.Abs(drift) < 1e-7) return lines;
            var mean = TimeMath.MeanYearDays(system);
            lines.Add("Cause : l'année de « " + system.Name + " » dure " + Number(mean, 4) + " jours" + (system.Leaps.Count > 0 ? " en moyenne" : "") + ", l'année réelle du monde "
                + Number(world.SolarYear, 4) + ". Chaque année, les saisons arrivent " + Number(Math.Abs(drift), 2) + " jour" + (Math.Abs(drift) >= 2 ? "s" : "") + " plus " + (drift > 0 ? "tôt" : "tard") + " dans le calendrier.");
            var after = new List<string>();
            foreach (var years in new[] { 10, 100, 1000 })
            {
                var days = DriftOver(world, system, years);
                after.Add(Number(years, 0) + " ans : " + (days < 1 ? "moins d'un jour" : Number(days, 0) + " jour" + (days >= 1.5 ? "s" : ""))
                    + (days >= mean ? " (" + Number(days / mean, 1) + " tour" + (days / mean >= 1.95 ? "s" : "") + " de calendrier)" : ""));
            }
            lines.Add("Écart après " + string.Join("  ·  ", after.ToArray()) + ".");
            // Ce que le calendrier devrait gagner (ou perdre) pour suivre : des jours entiers chaque année, puis une bissextile.
            var missing = world.SolarYear - mean;
            var whole = Math.Truncate(missing);
            var rest = missing - whole;
            var verb = missing > 0 ? "ajouter" : "retirer";
            var parts = new List<string>();
            if (whole != 0) parts.Add(verb + " " + TimeSystem.Count((long)Math.Abs(whole), "jour", "jours") + " à ses mois");
            if (Math.Abs(rest) >= 0.0005) parts.Add(verb + " un jour tous les " + TimeSystem.YearsLabel(Math.Max(2, (long)Math.Round(1 / Math.Abs(rest)))) + " (une règle de bissextile)");
            if (parts.Count > 0) lines.Add("Pour garder cette année réelle, c'est le calendrier qu'il faut retoucher : " + string.Join(", puis ", parts.ToArray()) + ".");
            return lines;
        }

        /// <summary>La durée de chaque saison, en jours, dans une année de cette
        /// longueur (16/11, Rémi) : du début de l'une au début de la suivante, la
        /// dernière de l'année se poursuivant jusqu'à la première. Dans l'ordre de world.Seasons.</summary>
        public static double[] SeasonLengths(World world, double yearDays)
        {
            var count = world == null ? 0 : world.Seasons.Count;
            var lengths = new double[count];
            if (count == 0 || yearDays <= 0) return lengths;
            var order = new List<int>();
            for (var i = 0; i < count; i++) order.Add(i);
            order.Sort(delegate(int a, int b) { var by = world.Seasons[a].StartDay.CompareTo(world.Seasons[b].StartDay); return by != 0 ? by : a.CompareTo(b); });
            for (var k = 0; k < count; k++)
            {
                var start = Math.Min(world.Seasons[order[k]].StartDay, yearDays + 1);
                var next = k + 1 < count ? Math.Min(world.Seasons[order[k + 1]].StartDay, yearDays + 1) : world.Seasons[order[0]].StartDay + yearDays;
                lengths[order[k]] = count == 1 ? yearDays : Math.Max(0, Math.Min(yearDays, next - start));
            }
            return lengths;
        }

        /// <summary>« 90 jours · 25 % de l'année ».</summary>
        public static string SeasonShare(double days, double yearDays)
        {
            return Number(days, 1) + " jour" + (days >= 2 ? "s" : "") + "  ·  " + Number(yearDays <= 0 ? 0 : days / yearDays * 100, 1) + " % de l'année";
        }

        /// <summary>« Moyenne équivalente : 100 jours » — ce que durerait chaque saison si toutes duraient autant.</summary>
        public static string EvenSeasons(int seasons, double yearDays)
        {
            if (seasons <= 0 || yearDays <= 0) return "";
            return "Moyenne équivalente : " + Number(yearDays / seasons, 1) + " jour" + (yearDays / seasons >= 2 ? "s" : "") + " par saison (" + TimeSystem.Count(seasons, "saison", "saisons") + " sur une année de " + Number(yearDays, 4) + " jours).";
        }

        /// <summary>Quatre saisons égales, pour partir de quelque chose : la
        /// première commence le jour firstDay, les suivantes un quart d'année
        /// plus loin chacune (en repassant par le début de l'année au besoin).</summary>
        public static List<Season> FourSeasons(int daysInYear, int firstDay = 1)
        {
            var names = new[] { "Printemps", "Été", "Automne", "Hiver" };
            var year = Math.Max(4, daysInYear);
            var seasons = new List<Season>();
            for (var i = 0; i < 4; i++)
                seasons.Add(new Season { Name = names[i], StartDay = 1 + (int)Mod(Math.Max(1, firstDay) - 1 + (int)Math.Round(i * year / 4.0), year) });
            return seasons;
        }

        /// <summary>Le jour où commence NOTRE printemps (le 20 mars, jour 79 sur
        /// 365), rapporté à une année de cette longueur : le 78 d'une année de 360 jours.</summary>
        public static int SpringDay(int daysInYear)
        {
            var year = Math.Max(4, daysInYear);
            return 1 + (int)Mod(Math.Round(78 / 365.0 * year), year);
        }

        // ---------------------------------------------------------------- soleils

        /// <summary>Le soleil qui fait le jour et les saisons : le premier soleil
        /// « centrique » (sans orbite) ; à défaut le premier de la liste. Null sans soleil.</summary>
        public static Sun MainSun(World world)
        {
            if (world == null || world.Suns.Count == 0) return null;
            foreach (var sun in world.Suns) if (sun.OrbitId == null) return sun;
            return world.Suns[0];
        }

        /// <summary>Les soleils qui font le tour du ciel du monde : tous sauf le principal, dès
        /// qu'ils ont une période — et que leur orbite passe AU-DELÀ du monde (16/11 soir). Un
        /// compagnon en orbite rapprochée ne quitte jamais son soleil : voir CloseCompanions.</summary>
        public static List<Sun> Wanderers(World world)
        {
            var suns = new List<Sun>();
            if (world == null) return suns;
            var main = MainSun(world);
            foreach (var sun in world.Suns) if (sun != main && sun.Period > 0 && !IsInner(world, sun)) suns.Add(sun);
            return suns;
        }

        // ------------------------------------------------------ placement stellaire

        public const string WorldToken = "@world";

        /// <summary>L'ORDRE DES ASTRES en s'éloignant du centre (16/11 soir, Rémi) :
        /// world.Order nettoyé et complété. Ce qui n'y figure pas encore prend sa
        /// place par défaut — un soleil qui orbite, juste avant le monde (orbite
        /// rapprochée : c'est le cas courant d'une étoile double) ; le monde, après
        /// les soleils ; une planète, tout au bout.</summary>
        public static List<string> NormalOrder(World world)
        {
            var order = new List<string>();
            if (world == null) return order;
            foreach (var id in world.Order)
            {
                if (order.Contains(id)) continue;
                var sun = FindSun(world, id);
                if (id == WorldToken || (sun != null && sun.OrbitId != null) || FindPlanet(world, id) != null) order.Add(id);
            }
            if (!order.Contains(WorldToken)) order.Add(WorldToken);
            foreach (var sun in world.Suns)
                if (sun.OrbitId != null && !order.Contains(sun.Id)) order.Insert(order.IndexOf(WorldToken), sun.Id);
            foreach (var planet in world.Planets) if (!order.Contains(planet.Id)) order.Add(planet.Id);
            return order;
        }

        public static Planet FindPlanet(World world, string id)
        {
            if (world == null || id == null) return null;
            foreach (var planet in world.Planets) if (planet.Id == id) return planet;
            return null;
        }

        /// <summary>Le système, du centre vers le dehors : les soleils centriques (rang 0), puis chaque astre à son rang.</summary>
        public static List<SkyBody> Layout(World world)
        {
            var bodies = new List<SkyBody>();
            if (world == null) return bodies;
            for (var i = 0; i < world.Suns.Count; i++)
                if (world.Suns[i].OrbitId == null) bodies.Add(new SkyBody { Kind = SkyBody.SunKind, Id = world.Suns[i].Id, Name = SunLabel(world.Suns[i]), Rank = 0, Sun = world.Suns[i] });
            var rank = 1;
            foreach (var id in NormalOrder(world))
            {
                if (id == WorldToken) { bodies.Add(new SkyBody { Kind = SkyBody.WorldKind, Id = id, Name = string.IsNullOrEmpty(world.Name) ? "Le monde" : world.Name, Rank = rank++, Movable = true }); continue; }
                var sun = FindSun(world, id);
                if (sun != null) { bodies.Add(new SkyBody { Kind = SkyBody.SunKind, Id = id, Name = SunLabel(sun), Rank = rank++, Sun = sun, Movable = true }); continue; }
                var planet = FindPlanet(world, id);
                bodies.Add(new SkyBody { Kind = SkyBody.PlanetKind, Id = id, Name = string.IsNullOrEmpty(planet.Name) ? "Planète" : planet.Name, Rank = rank++, Planet = planet, Movable = true });
            }
            return bodies;
        }

        /// <summary>Vrai si cet astre est plus près du centre que le monde.</summary>
        public static bool IsInside(World world, string id)
        {
            var order = NormalOrder(world);
            var at = order.IndexOf(id);
            return at >= 0 && at < order.IndexOf(WorldToken);
        }

        /// <summary>Vrai pour un soleil en ORBITE RAPPROCHÉE : il tourne autour d'un
        /// autre soleil, plus près du centre que le monde. Vu du monde, il ne
        /// quitte jamais son soleil — pas de nuits claires.</summary>
        public static bool IsInner(World world, Sun sun)
        {
            return world != null && sun != null && sun.OrbitId != null && IsInside(world, sun.Id);
        }

        /// <summary>Place un soleil qui orbite en deçà (orbite rapprochée) ou au-delà (orbite lointaine) du monde.</summary>
        public static void SetInner(World world, Sun sun, bool inner)
        {
            if (world == null || sun == null || sun.OrbitId == null) return;
            var order = NormalOrder(world);
            if (order.IndexOf(sun.Id) < order.IndexOf(WorldToken) != inner)
            {
                order.Remove(sun.Id);
                order.Insert(order.IndexOf(WorldToken) + (inner ? 0 : 1), sun.Id);
            }
            world.Order = order;
        }

        /// <summary>Déplace un astre d'un rang vers le centre (-1) ou vers le dehors (+1) ; faux s'il est au bout.</summary>
        public static bool MoveBody(World world, string id, int delta)
        {
            var order = NormalOrder(world);
            var at = order.IndexOf(id);
            var to = at + (delta < 0 ? -1 : 1);
            if (at < 0 || to < 0 || to >= order.Count) return false;
            order.RemoveAt(at);
            order.Insert(to, id);
            world.Order = order;
            return true;
        }

        /// <summary>Les compagnons en orbite rapprochée qui ont une période : ceux qui donnent des éclipses solaires pures.</summary>
        public static List<Sun> CloseCompanions(World world)
        {
            var suns = new List<Sun>();
            if (world == null) return suns;
            foreach (var sun in world.Suns) if (sun.Period > 0 && IsInner(world, sun) && FindSun(world, sun.OrbitId) != null) suns.Add(sun);
            return suns;
        }

        // --------------------------------------------------- éclipses solaires pures

        public const int NoSunEclipse = 0, SunInFront = 1, SunBehind = 2;
        /// <summary>Le diamètre et la superficie de notre Soleil : l'unité de taille des soleils.</summary>
        public const double SunDiameter = 1392700, SunSurface = 6.09e12;

        /// <summary>L'ÉCLIPSE SOLAIRE PURE (16/11 soir, Rémi) : un soleil qui en cache un
        /// autre. Un compagnon en orbite rapprochée passe DEVANT son soleil le jour
        /// HighAt, puis à chaque période ; DERRIÈRE lui une demi-période plus tard.
        /// (Les orbites sont supposées alignées avec le monde : chaque passage est une éclipse.)</summary>
        public static int SunEclipseOn(World world, Sun sun, long dayNumber)
        {
            if (sun == null || sun.Period <= 0 || !IsInner(world, sun) || FindSun(world, sun.OrbitId) == null) return NoSunEclipse;
            if (Math.Floor((dayNumber + 1 - sun.HighAt) / sun.Period - 1e-9) > Math.Floor((dayNumber - sun.HighAt) / sun.Period - 1e-9)) return SunInFront;
            var back = sun.HighAt + sun.Period / 2;
            if (Math.Floor((dayNumber + 1 - back) / sun.Period - 1e-9) > Math.Floor((dayNumber - back) / sun.Period - 1e-9)) return SunBehind;
            return NoSunEclipse;
        }

        /// <summary>La part du disque du soleil de derrière que cache celui de devant : 1 = éclipse totale.</summary>
        public static double SunEclipseCover(World world, Sun sun, int eclipse)
        {
            var parent = FindSun(world, sun.OrbitId);
            if (parent == null || eclipse == NoSunEclipse) return 0;
            // Lot 17 : ce qui compte est la largeur APPARENTE, vue du monde. Si l'on a dit à quelle distance
            // le compagnon tourne autour de son soleil, il est plus près de nous quand il passe devant
            // (donc plus large), plus loin quand il passe derrière. Sans cela : leurs tailles seules.
            var far = SunWorldDistance(world, parent);
            var orbit = sun.Look.Distance;
            var known = orbit > 0 && Math.Abs(orbit - sun.Look.DefaultDistance) > 1 && orbit < far * 0.98;
            var mine = Math.Max(0.0001, sun.Size) / (known ? (eclipse == SunInFront ? far - orbit : far + orbit) : far);
            var theirs = Math.Max(0.0001, parent.Size) / far;
            var front = eclipse == SunInFront ? mine : theirs;
            var back = eclipse == SunInFront ? theirs : mine;
            return Math.Min(1, front / back * (front / back));
        }

        // ------------------------------------- tailles et distances dans les calculs (lot 17)

        /// <summary>La distance du monde à un soleil, en km. Un soleil centrique : celle qu'on lui a donnée.
        /// Un compagnon : celle du soleil autour duquel il tourne (sa propre distance le sépare de lui).</summary>
        public static double SunWorldDistance(World world, Sun sun)
        {
            for (var hop = 0; hop < 4 && sun != null && sun.OrbitId != null; hop++)
            {
                var parent = FindSun(world, sun.OrbitId);
                if (parent == null) break;
                sun = parent;
            }
            // (Une allure de satellite posée sur un soleil — un .plot du lot 14 — n'a pas de distance de soleil.)
            return sun != null && sun.Look != null && sun.Look.Distance > 0 && Math.Abs(sun.Look.DefaultDistance - BodyLook.SunDistance) < 1 ? sun.Look.Distance : BodyLook.SunDistance;
        }

        /// <summary>La largeur d'un soleil dans le ciel du monde (son diamètre sur sa distance).</summary>
        public static double SunApparent(World world, Sun sun)
        {
            return (sun == null ? 1 : Math.Max(0.0001, sun.Size)) * SunDiameter / (sun == null ? BodyLook.SunDistance : SunWorldDistance(world, sun));
        }

        /// <summary>La largeur d'un satellite dans le ciel du monde.</summary>
        public static double MoonApparent(Moon moon)
        {
            return moon.Look.DiameterKm / Math.Max(1, moon.Look.Distance);
        }

        /// <summary>La largeur du satellite rapportée à celle du soleil : à 1, il le couvre tout juste.</summary>
        public static double SolarCover(World world, Moon moon)
        {
            return MoonApparent(moon) / SunApparent(world, MainSun(world));
        }

        /// <summary>À partir de quel rapport l'éclipse de soleil est dite totale : une orbite n'est jamais
        /// tout à fait ronde, et notre Lune (0,97 en moyenne) couvre le Soleil quand elle est au plus près.</summary>
        public const double TotalCover = 0.95;

        public static bool SolarTotal(World world, Moon moon) { return SolarCover(world, moon) >= TotalCover; }

        /// <summary>Le rayon de l'ombre du monde (son cône d'ombre) à cette distance, en km ; négatif au-delà de sa pointe.</summary>
        public static double UmbraRadius(World world, double km)
        {
            var main = MainSun(world);
            var worldRadius = (world != null && world.HasSize ? world.Circumference : EarthCircumference) / (2 * Math.PI);
            var sunRadius = (main == null ? 1 : Math.Max(0.0001, main.Size)) * SunDiameter / 2;
            var far = main == null ? BodyLook.SunDistance : SunWorldDistance(world, main);
            return worldRadius - km * (sunRadius - worldRadius) / Math.Max(1, far);
        }

        /// <summary>Ce que l'ombre du monde peut faire à ce satellite : 2 = le couvrir en entier (lune rousse),
        /// 1 = l'entamer seulement (il est plus large qu'elle), 0 = rien (elle ne porte pas jusqu'à lui).</summary>
        public static int LunarReach(World world, Moon moon)
        {
            var umbra = UmbraRadius(world, moon.Look.Distance);
            return umbra >= moon.Look.DiameterKm / 2 ? 2 : umbra > 0 ? 1 : 0;
        }

        private static int Lunar(World world, Moon moon, int eclipse)
        {
            if (eclipse == NoEclipse) return NoEclipse;
            var reach = LunarReach(world, moon);
            return reach == 0 ? NoEclipse : reach == 1 ? PartialLunar : eclipse;
        }

        /// <summary>La force des marées que soulève ce satellite, en fois celles de notre Lune : à densité
        /// égale, sa masse suit le cube de son diamètre, et la marée faiblit comme le cube de la distance.</summary>
        public static double TideFactor(Moon moon)
        {
            return Math.Pow(moon.Look.DiameterKm / BodyLook.MoonDiameter, 3) / Math.Pow(Math.Max(1, moon.Look.Distance) / BodyLook.MoonDistance, 3);
        }

        /// <summary>Ce que le diamètre et la distance d'un satellite changent ; null s'il est comme notre Lune.</summary>
        public static SkyBehavior MoonSkyNote(World world, Moon moon)
        {
            var wide = MoonApparent(moon) / (BodyLook.MoonDiameter / BodyLook.MoonDistance);
            var cover = SolarCover(world, moon);
            var reach = LunarReach(world, moon);
            var tide = TideFactor(moon);
            if (Math.Abs(wide - 1) < 0.005 && Math.Abs(tide - 1) < 0.01 && cover >= TotalCover && reach == 2) return null;
            var text = "Large de " + Number(moon.Look.DiameterKm, 0) + " km, à " + Number(moon.Look.Distance, 0) + " km du monde : dans le ciel, elle fait "
                + Number(wide, 2) + " fois la largeur de notre Lune.";
            if (moon.Eclipses != EclipseNever)
            {
                text += cover >= TotalCover
                    ? " Elle couvre tout le soleil : ses éclipses de soleil sont totales."
                    : " Elle ne cache que " + Number(Math.Min(1, cover * cover) * 100, cover < 0.3 ? 1 : 0) + " % du disque du soleil : ses éclipses de soleil sont annulaires.";
                if (reach == 1) text += " L'ombre du monde est plus étroite qu'elle : jamais de lune rousse, des éclipses de lune partielles seulement.";
                else if (reach == 0) text += " L'ombre du monde ne porte pas jusqu'à elle : aucune éclipse de lune.";
            }
            if (Math.Abs(tide - 1) >= 0.01) text += " Marées : environ " + Number(tide, tide < 10 ? 2 : 0) + " fois celles de notre Lune, à densité égale.";
            return new SkyBehavior { Title = MoonLabel(moon) + " : sa taille dans le ciel", Notable = cover < TotalCover || reach < 2, Text = text };
        }

        /// <summary>Ce que la taille et la distance du soleil principal changent ; null s'il est comme le nôtre.</summary>
        public static SkyBehavior SunSkyNote(World world)
        {
            var main = MainSun(world);
            if (main == null) return null;
            var wide = SunApparent(world, main) / (SunDiameter / BodyLook.SunDistance);
            if (Math.Abs(wide - 1) < 0.005) return null;
            return new SkyBehavior
            {
                Title = SunLabel(main) + " : sa taille dans le ciel",
                Text = "Large de " + Number(Math.Max(0.0001, main.Size) * SunDiameter, 0) + " km, à " + Number(SunWorldDistance(world, main), 0) + " km du monde : dans le ciel, il fait "
                    + Number(wide, 2) + " fois la largeur de notre Soleil. À chaleur de surface égale, le monde en reçoit " + Number(wide * wide, 2) + " fois la lumière que nous recevons du nôtre."
            };
        }

        /// <summary>Le nom de l'éclipse que donne CE satellite : sa taille dans le ciel dit si elle est totale ou annulaire.</summary>
        public static string EclipseName(World world, Moon moon, int eclipse)
        {
            if (eclipse != TotalSolar || moon == null || SolarTotal(world, moon)) return EclipseName(eclipse);
            var cover = SolarCover(world, moon);
            return "éclipse annulaire de soleil (" + Number(cover * cover * 100, cover < 0.3 ? 1 : 0) + " % du disque)";
        }

        /// <summary>« Éclipse solaire pure : Braise passe devant Hélios et cache 25 % de son disque (annulaire) ».</summary>
        public static string SunEclipseLabel(World world, Sun sun, int eclipse)
        {
            var parent = FindSun(world, sun.OrbitId);
            if (parent == null || eclipse == NoSunEclipse) return "";
            var front = eclipse == SunInFront ? sun : parent;
            var back = eclipse == SunInFront ? parent : sun;
            var cover = SunEclipseCover(world, sun, eclipse);
            return "Éclipse solaire pure : " + SunLabel(front) + (cover >= 1 - 1e-9
                ? " cache entièrement " + SunLabel(back) + " (totale)"
                : " passe devant " + SunLabel(back) + " et cache " + Number(cover * 100, cover < 0.1 ? 1 : 0) + " % de son disque (annulaire)");
        }

        /// <summary>Le prochain jour d'une éclipse solaire pure de ce compagnon, ce jour compris.</summary>
        public static long NextSunEclipse(Sun sun, long dayNumber, int eclipse)
        {
            var period = Math.Max(0.01, sun.Period);
            var at = sun.HighAt + (eclipse == SunBehind ? period / 2 : 0);
            var k = Math.Ceiling((dayNumber - at) / period - 1e-9);
            return (long)Math.Floor(at + k * period + 1e-9);
        }

        // ---------------------------------------------------------------- planètes

        /// <summary>Le temps entre deux alignements d'une planète avec le monde et
        /// le soleil (sa période synodique), en jours ; 0 si elle n'a pas d'année
        /// renseignée, ou la même que le monde.</summary>
        public static double SynodicPeriod(World world, TimeSystem system, Planet planet)
        {
            if (planet == null || planet.Period <= 0) return 0;
            var year = SolarYear(world, system);
            var gap = Math.Abs(1 / year - 1 / planet.Period);
            return gap < 1e-9 ? 0 : 1 / gap;
        }

        /// <summary>Vrai le jour où cette planète s'aligne avec le monde et le soleil.</summary>
        public static bool PlanetAlignedOn(World world, TimeSystem system, Planet planet, long dayNumber)
        {
            var period = SynodicPeriod(world, system, planet);
            if (period <= 0) return false;
            return Math.Floor((dayNumber + 1 - planet.At) / period - 1e-9) > Math.Floor((dayNumber - planet.At) / period - 1e-9);
        }

        /// <summary>Ce que donne l'alignement : une planète du dehors est en opposition (au plus près,
        /// visible toute la nuit) ; une planète du dedans passe entre le monde et le soleil.</summary>
        public static string PlanetAlignmentLabel(World world, Planet planet)
        {
            var name = string.IsNullOrEmpty(planet.Name) ? "Planète" : planet.Name;
            return IsInside(world, planet.Id) ? name + " passe entre le monde et le soleil" : name + " au plus près du monde : visible toute la nuit";
        }

        public static readonly string[] PlanetColors = { "#6E8FB5", "#C8703A", "#3E9C7A", "#B5843A", "#8E6BBF", "#C08497" };

        public static string ColorOf(Planet planet, int index)
        {
            return string.IsNullOrEmpty(planet.Color) ? PlanetColors[Math.Abs(index) % PlanetColors.Length] : planet.Color;
        }

        public static Sun FindSun(World world, string id)
        {
            if (world == null || id == null) return null;
            foreach (var sun in world.Suns) if (sun.Id == id) return sun;
            return null;
        }

        public static Moon FindMoon(World world, string id)
        {
            if (world == null || id == null) return null;
            foreach (var moon in world.Moons) if (moon.Id == id) return moon;
            return null;
        }

        /// <summary>Vrai si l'orbite de ce soleil finit par revenir sur lui (A autour de B autour de A).</summary>
        public static bool OrbitLoops(World world, Sun sun)
        {
            var seen = new HashSet<string>();
            for (var at = sun; at != null && at.OrbitId != null; at = FindSun(world, at.OrbitId))
                if (!seen.Add(at.Id)) return true;
            return false;
        }

        public static bool OrbitLoops(World world, Moon moon)
        {
            var seen = new HashSet<string>();
            for (var at = moon; at != null && at.OrbitId != null; at = FindMoon(world, at.OrbitId))
                if (!seen.Add(at.Id)) return true;
            return false;
        }

        /// <summary>Vrai pour une lune qui tourne autour d'une autre lune (et non autour du monde).</summary>
        public static bool IsSubMoon(World world, Moon moon)
        {
            return moon != null && moon.OrbitId != null && FindMoon(world, moon.OrbitId) != null && FindMoon(world, moon.OrbitId) != moon;
        }

        /// <summary>Où en est un soleil compagnon : 0 = au plus haut à minuit (à
        /// l'opposé du premier soleil), 0,5 = auprès de lui, dans le ciel de jour.</summary>
        public static double SunPhase(Sun sun, double day)
        {
            var period = Math.Max(0.01, sun.Period);
            return Mod(day - sun.HighAt, period) / period;
        }

        /// <summary>La part de la nuit qu'un compagnon éclaire : 1 quand il
        /// culmine à minuit, 0 quand il accompagne le premier soleil.</summary>
        public static double NightShare(double phase)
        {
            var p = Mod(phase, 1);
            return 1 - 2 * Math.Min(p, 1 - p);
        }

        /// <summary>Ce que le compagnon fait du ciel : « nuits claires » autour
        /// de minuit, puis « soleil du soir » (il se couche de plus en plus tôt),
        /// « de jour » auprès du premier soleil, « soleil du matin » avant de revenir.</summary>
        public static string SunStateName(double phase)
        {
            var names = new[] { "nuits claires", "soleil du soir", "de jour", "soleil du matin" };
            return names[(int)Math.Floor(Mod(phase + 1.0 / 8, 1) * 4) % 4];
        }

        /// <summary>Vrai le jour où ce compagnon culmine à minuit.</summary>
        public static bool SunHighOn(Sun sun, long dayNumber)
        {
            if (sun == null || sun.Period <= 0) return false;
            return Math.Floor((dayNumber + 1 - sun.HighAt) / sun.Period - 1e-9) > Math.Floor((dayNumber - sun.HighAt) / sun.Period - 1e-9);
        }

        /// <summary>Le prochain jour où ce compagnon culmine à minuit, ce jour compris.</summary>
        public static long NextHigh(Sun sun, long dayNumber)
        {
            var period = Math.Max(0.01, sun.Period);
            var k = Math.Ceiling((dayNumber - sun.HighAt) / period - 1e-9);
            return (long)Math.Floor(sun.HighAt + k * period + 1e-9);
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

        /// <summary>Le jour où revient la phase principale qui tombe ce jour-là
        /// (MainPhaseOn) : « cycles » lunaisons plus tard — ou plus tôt, en
        /// négatif. La dernière pleine lune avant celle du jour : -1.</summary>
        public static long MainPhaseDay(Moon moon, long dayNumber, int cycles)
        {
            var period = Math.Max(0.01, moon.Period);
            var quarter = Math.Floor((dayNumber - moon.FullAt) / period * 4 - 1e-9) + 1 + 4.0 * cycles;
            return (long)Math.Floor(moon.FullAt + quarter * period / 4 + 1e-9);
        }

        /// <summary>Le jour où un compagnon culmine de nouveau à minuit, « cycles » tours après (ou avant) celui de ce jour.</summary>
        public static long HighDay(Sun sun, long dayNumber, int cycles)
        {
            var period = Math.Max(0.01, sun.Period);
            var turn = Math.Floor((dayNumber + 1 - sun.HighAt) / period - 1e-9) + cycles;
            return (long)Math.Floor(sun.HighAt + turn * period + 1e-9);
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
            var moons = new List<Moon>();
            if (world != null) foreach (var moon in world.Moons) if (!IsSubMoon(world, moon)) moons.Add(moon); // une lune de lune suit sa lune-mère
            if (moons.Count < 2) return null;
            var slow = moons[0];
            foreach (var moon in moons) if (moon.Period > slow.Period) slow = moon;
            var period = Math.Max(0.01, slow.Period);
            var k = Math.Ceiling((fromDay - slow.FullAt) / period - 1e-9);
            for (var i = 0; i < maxFullMoons; i++, k++)
            {
                var instant = slow.FullAt + k * period;
                var together = true;
                foreach (var moon in moons)
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

        // -------------------------------------------------- éclipses et phénomènes

        public const int EclipseSeasons = 0, EclipseAlways = 1, EclipseNever = 2;
        public const int NoEclipse = 0, PartialLunar = 1, TotalLunar = 2, PartialSolar = 3, TotalSolar = 4;

        /// <summary>« L'année des éclipses » d'un monde : le temps que mettent
        /// les saisons d'éclipses à refaire le tour de l'année — un peu moins
        /// d'une année solaire (346,6 jours chez nous, soit 0,949 année).</summary>
        public static double EclipseYear(World world, TimeSystem system)
        {
            return Math.Max(2, SolarYear(world, system) * 0.949);
        }

        /// <summary>L'éclipse que cette lune donne CE JOUR : de lune à sa pleine
        /// lune, de soleil à sa nouvelle lune. « À chaque lunaison » : toujours
        /// totale. « Par saisons » (une orbite inclinée, comme la nôtre) : deux
        /// saisons d'éclipses par année des éclipses, calées sur la pleine lune
        /// de référence — totale au cœur de la saison, partielle sur ses bords.
        /// Une lune de lune n'en donne pas.</summary>
        public static int EclipseOn(World world, TimeSystem system, Moon moon, long dayNumber)
        {
            if (moon == null || moon.Eclipses == EclipseNever || IsSubMoon(world, moon)) return NoEclipse;
            var phase = MainPhaseOn(moon, dayNumber);
            if (phase != Full && phase != New) return NoEclipse;
            if (moon.Eclipses == EclipseAlways) return phase == Full ? Lunar(world, moon, TotalLunar) : TotalSolar;
            var period = Math.Max(0.01, moon.Period);
            var quarter = Math.Floor((dayNumber - moon.FullAt) / period * 4 - 1e-9) + 1;
            var instant = moon.FullAt + quarter * period / 4;
            var year = EclipseYear(world, system);
            var half = year / 2;
            var gap = Math.Abs(Mod(instant - moon.FullAt + half / 2, half) - half / 2);
            if (phase == Full) return Lunar(world, moon, gap <= year * 0.013 ? TotalLunar : gap <= year * 0.03 ? PartialLunar : NoEclipse);
            return gap <= year * 0.014 ? TotalSolar : gap <= year * 0.045 ? PartialSolar : NoEclipse;
        }

        public static string EclipseName(int eclipse)
        {
            switch (eclipse)
            {
                case PartialLunar: return "éclipse partielle de lune";
                case TotalLunar: return "éclipse totale de lune (lune rousse)";
                case PartialSolar: return "éclipse partielle de soleil";
                case TotalSolar: return "éclipse totale de soleil";
                default: return "";
            }
        }

        /// <summary>Le prochain jour où cette lune donne cette éclipse, ce jour compris ; null si rien avant longtemps.</summary>
        public static long? NextEclipse(World world, TimeSystem system, Moon moon, long fromDay, int eclipse, int maxLunations = 4000)
        {
            if (moon == null || moon.Eclipses == EclipseNever) return null;
            var lunar = eclipse == PartialLunar || eclipse == TotalLunar;
            var period = Math.Max(0.01, moon.Period);
            var k = Math.Ceiling((fromDay - moon.FullAt - (lunar ? 0 : period / 2)) / period - 1e-9);
            for (var i = 0; i < maxLunations; i++, k++)
            {
                var day = (long)Math.Floor(moon.FullAt + (lunar ? 0 : period / 2) + k * period + 1e-9);
                if (day >= fromDay && EclipseOn(world, system, moon, day) == eclipse) return day;
            }
            return null;
        }

        /// <summary>Vrai le jour d'une LUNE BLEUE : la deuxième pleine lune d'un même mois du calendrier.</summary>
        public static bool BlueMoonOn(TimeSystem system, Moon moon, long dayNumber)
        {
            if (system == null || system.Months.Count < 2 || moon == null || MainPhaseOn(moon, dayNumber) != Full) return false;
            var before = MainPhaseDay(moon, dayNumber, -1);
            if (before >= dayNumber) return false;
            var today = TimeMath.FromDayNumber(system, dayNumber);
            var previous = TimeMath.FromDayNumber(system, before);
            return today.Year == previous.Year && today.Month == previous.Month;
        }

        /// <summary>Vrai le jour d'un passage de ce phénomène personnalisé.</summary>
        public static bool OccursOn(SkyEvent item, long dayNumber)
        {
            if (item == null || item.Period <= 0) return false;
            return Math.Floor((dayNumber + 1 - item.At) / item.Period - 1e-9) > Math.Floor((dayNumber - item.At) / item.Period - 1e-9);
        }

        /// <summary>Le jour d'un passage, « cycles » périodes après (ou avant) celui de ce jour.</summary>
        public static long OccurrenceDay(SkyEvent item, long dayNumber, int cycles)
        {
            var period = Math.Max(0.01, item.Period);
            var turn = Math.Floor((dayNumber + 1 - item.At) / period - 1e-9) + cycles;
            return (long)Math.Floor(item.At + turn * period + 1e-9);
        }

        public static readonly string[] PhenomenonColors = { "#B86FC2", "#3E9C7A", "#D9603A", "#5B9BD5", "#B5843A" };

        public static string ColorOf(SkyEvent item, int index)
        {
            return string.IsNullOrEmpty(item.Color) ? PhenomenonColors[Math.Abs(index) % PhenomenonColors.Length] : item.Color;
        }

        private static string MoonLabel(Moon moon) { return string.IsNullOrEmpty(moon.Name) ? "Lune" : moon.Name; }
        private static string SunLabel(Sun sun) { return string.IsNullOrEmpty(sun.Name) ? "Soleil" : sun.Name; }

        /// <summary>Ce que le ciel fait de CE JOUR au-delà des phases (16/11,
        /// Rémi) : éclipses, lune rousse, lune bleue, lunes pleines ensemble,
        /// passages d'une lune devant sa lune-mère, phénomènes personnalisés.</summary>
        public static List<SkyNote> NotesOn(World world, TimeSystem system, long dayNumber)
        {
            var notes = new List<SkyNote>();
            if (world == null || system == null) return notes;
            Func<long, string> date = delegate(long day) { return TimeMath.ShortDate(system, TimeMath.FromDayNumber(system, day)); };
            var fullTogether = 0;
            var freeMoons = 0;
            for (var i = 0; i < world.Moons.Count; i++)
            {
                var moon = world.Moons[i];
                var phase = MainPhaseOn(moon, dayNumber);
                if (IsSubMoon(world, moon))
                {
                    // Une lune de lune : ses « phases » sont ses passages devant et derrière sa lune-mère.
                    var parent = MoonLabel(FindMoon(world, moon.OrbitId));
                    if (phase == Full) notes.Add(new SkyNote { Text = MoonLabel(moon) + " passe devant " + parent, Color = ColorOf(moon, i) });
                    else if (phase == New) notes.Add(new SkyNote { Text = MoonLabel(moon) + " passe derrière " + parent, Color = ColorOf(moon, i) });
                    continue;
                }
                freeMoons++;
                if (phase == Full) fullTogether++;
                var eclipse = EclipseOn(world, system, moon, dayNumber);
                if (eclipse != NoEclipse)
                {
                    string detail = null;
                    if (moon.Eclipses == EclipseSeasons)
                    {
                        var next = NextEclipse(world, system, moon, dayNumber + 1, eclipse);
                        if (next != null) detail = "Prochaine : " + date(next.Value);
                    }
                    notes.Add(new SkyNote { Text = MoonLabel(moon) + " : " + EclipseName(world, moon, eclipse), Detail = detail, Color = eclipse == TotalLunar || eclipse == PartialLunar ? "#B5452E" : "#3A3F4D" });
                }
                if (BlueMoonOn(system, moon, dayNumber))
                    notes.Add(new SkyNote { Text = MoonLabel(moon) + " : lune bleue (deuxième pleine lune du mois)", Color = "#5B9BD5" });
            }
            if (freeMoons > 1 && fullTogether == freeMoons)
                notes.Add(new SkyNote { Text = "Toutes les lunes sont pleines ensemble", Color = "#8E6BBF" });
            for (var i = 0; i < world.Suns.Count; i++)
            {
                var pure = SunEclipseOn(world, world.Suns[i], dayNumber);
                if (pure == NoSunEclipse) continue;
                notes.Add(new SkyNote
                {
                    Text = SunEclipseLabel(world, world.Suns[i], pure), Color = ColorOf(world.Suns[i], i),
                    Detail = "Prochaine : " + date(NextSunEclipse(world.Suns[i], dayNumber + 1, pure))
                });
            }
            for (var i = 0; i < world.Planets.Count; i++)
                if (PlanetAlignedOn(world, system, world.Planets[i], dayNumber))
                    notes.Add(new SkyNote { Text = PlanetAlignmentLabel(world, world.Planets[i]), Color = ColorOf(world.Planets[i], i) });
            for (var i = 0; i < world.Phenomena.Count; i++)
            {
                var item = world.Phenomena[i];
                if (!OccursOn(item, dayNumber)) continue;
                notes.Add(new SkyNote
                {
                    Text = string.IsNullOrEmpty(item.Name) ? "Phénomène" : item.Name,
                    Detail = "Précédent : " + date(OccurrenceDay(item, dayNumber, -1)) + "\nProchain : " + date(OccurrenceDay(item, dayNumber, 1)),
                    Color = ColorOf(item, i)
                });
            }
            return notes;
        }

        /// <summary>CE QUE LES SOLEILS DÉCLENCHENT (16/11, Rémi) : leur nombre
        /// et leur agencement, dits en clair — un soleil seul, une étoile double
        /// au centre, un compagnon qui tourne autour d'un autre.</summary>
        public static List<SkyBehavior> SunBehaviors(World world, TimeSystem system)
        {
            var list = SunArrangement(world, system);
            var sky = SunSkyNote(world);
            if (sky != null) list.Add(sky);
            return list;
        }

        private static List<SkyBehavior> SunArrangement(World world, TimeSystem system)
        {
            var list = new List<SkyBehavior>();
            if (world == null || world.Suns.Count == 0)
            {
                list.Add(new SkyBehavior { Title = "Aucun soleil renseigné", Text = "Le monde garde un soleil dont on ne dit rien : un jour, une nuit, des saisons." });
                return list;
            }
            var main = MainSun(world);
            var centric = new List<Sun>();
            foreach (var sun in world.Suns) if (sun.OrbitId == null) centric.Add(sun);
            if (world.Suns.Count == 1)
            {
                list.Add(new SkyBehavior { Title = "Un seul soleil", Text = "Rien de particulier : un jour, une nuit, une ombre par objet — comme chez nous." });
                return list;
            }
            if (centric.Count > 1)
            {
                var names = new List<string>();
                foreach (var sun in centric) names.Add(SunLabel(sun));
                list.Add(new SkyBehavior
                {
                    Title = centric.Count == 2 ? "Étoile double au centre" : centric.Count + " soleils au centre", Notable = true,
                    Text = string.Join(", ", names.ToArray()) + " partagent le milieu du système : ils se lèvent et se couchent ensemble. " + centric.Count
                        + " ombres par objet, des aubes et des crépuscules en plusieurs temps, des journées plus chaudes ; ils passent régulièrement l'un devant l'autre."
                });
            }
            foreach (var sun in world.Suns)
            {
                if (sun == main || sun.OrbitId == null) continue;
                var around = FindSun(world, sun.OrbitId);
                var center = around == null ? "un soleil disparu" : SunLabel(around);
                if (sun.Period <= 0)
                {
                    list.Add(new SkyBehavior { Title = SunLabel(sun) + " reste auprès de " + center, Notable = true, Text = "Sans période, il ne quitte pas son soleil dans le ciel : un second disque de jour, jamais de nuit claire. Donnez-lui une période pour dater leurs éclipses solaires pures." });
                    continue;
                }
                if (IsInner(world, sun))
                {
                    var front = SunEclipseCover(world, sun, SunInFront);
                    var behind = SunEclipseCover(world, sun, SunBehind);
                    Func<double, string> kind = delegate(double cover) { return cover >= 1 - 1e-9 ? "totale" : "annulaire, " + Number(cover * 100, cover < 0.1 ? 1 : 0) + " % du disque"; };
                    list.Add(new SkyBehavior
                    {
                        Title = SunLabel(sun) + " : orbite rapprochée autour de " + center, Notable = true,
                        Text = "Le monde est plus loin dans le système : vu de lui, " + SunLabel(sun) + " ne quitte jamais " + center + ". Les deux se couchent ensemble : les nuits restent noires, la durée du jour ne change pas. "
                            + "Tous les " + Number(sun.Period, 2) + " jours, il passe devant lui (éclipse solaire pure " + kind(front) + "), puis derrière lui à mi-chemin (" + kind(behind) + ")."
                    });
                    continue;
                }
                var clear = Math.Max(1, (long)Math.Round(sun.Period / 4));
                list.Add(new SkyBehavior
                {
                    Title = SunLabel(sun) + " tourne autour de " + center + ", au-delà du monde", Notable = true,
                    Text = "Vu du monde, il fait le tour du ciel en " + Number(sun.Period, 2) + " jours"
                        + (system != null && sun.Period >= TimeMath.MeanYearDays(system) ? " (" + system.DurationLabel(sun.Period / TimeMath.MeanYearDays(system)) + ")" : "")
                        + ". Nuits claires pendant environ " + TimeSystem.Count(clear, "jour", "jours") + " autour de son passage à minuit (pas de vraie nuit), puis soleil du soir, second disque de jour, soleil du matin."
                        + (world.Moons.Count > 0 ? " Les lunes, éclairées par deux soleils, n'y sont jamais tout à fait noires." : "")
                });
            }
            if (world.Suns.Count >= 3)
                list.Add(new SkyBehavior { Title = "Trois soleils ou plus", Notable = true, Text = "Un ciel rare : les saisons de lumière se superposent. Les nuits vraiment noires n'arrivent que lorsque tous les compagnons sont du côté du jour." });
            return list;
        }

        /// <summary>CE QUE LES LUNES DÉCLENCHENT : phases, lunes bleues,
        /// éclipses et lunes rousses, lunes pleines ensemble, lunes de lunes.</summary>
        public static List<SkyBehavior> MoonBehaviors(World world, TimeSystem system)
        {
            var list = new List<SkyBehavior>();
            if (world == null || world.Moons.Count == 0)
            {
                list.Add(new SkyBehavior { Title = "Aucune lune", Text = "Des nuits noires toute l'année, pas de mois lunaire, des marées faibles (le soleil seul les soulève)." });
                return list;
            }
            Func<long, string> date = delegate(long day) { return system.Format(TimeMath.FromDayNumber(system, day)); };
            var free = new List<Moon>();
            foreach (var moon in world.Moons) if (!IsSubMoon(world, moon)) free.Add(moon);
            var longest = 0;
            foreach (var month in system.Months) longest = Math.Max(longest, month.Days);
            foreach (var moon in world.Moons)
            {
                var start = (long)Math.Floor(moon.FullAt) + 1;
                if (IsSubMoon(world, moon))
                {
                    list.Add(new SkyBehavior
                    {
                        Title = MoonLabel(moon) + " : lune de " + MoonLabel(FindMoon(world, moon.OrbitId)), Notable = true,
                        Text = "Elle ne quitte pas sa lune-mère dans le ciel et partage ses phases. Elle passe devant elle tous les " + Number(moon.Period, 2) + " jours, derrière elle à mi-chemin : le calendrier marque ces passages."
                    });
                    continue;
                }
                if (moon.Eclipses == EclipseAlways)
                    list.Add(new SkyBehavior
                    {
                        Title = MoonLabel(moon) + " : une éclipse à chaque lunaison", Notable = true,
                        Text = "Son orbite est dans le plan du monde : "
                            + (LunarReach(world, moon) == 2 ? "chaque pleine lune est une lune rousse (éclipse totale de lune)" : LunarReach(world, moon) == 1 ? "chaque pleine lune est entamée par l'ombre du monde (éclipse partielle de lune)" : "l'ombre du monde ne l'atteint pas")
                            + ", chaque nouvelle lune " + (SolarTotal(world, moon) ? "cache le soleil" : "passe devant le soleil sans le couvrir (éclipse annulaire)") + " — tous les " + Number(moon.Period, 2) + " jours."
                    });
                else if (moon.Eclipses == EclipseSeasons)
                {
                    var red = NextEclipse(world, system, moon, start, TotalLunar);
                    var solar = NextEclipse(world, system, moon, start, TotalSolar);
                    list.Add(new SkyBehavior
                    {
                        Title = MoonLabel(moon) + " : des saisons d'éclipses",
                        Text = "Orbite inclinée, comme notre Lune : les éclipses reviennent par saisons, tous les " + Number(EclipseYear(world, system) / 2, 0) + " jours environ."
                            + (red != null ? " Prochaine lune rousse (éclipse totale de lune) : " + date(red.Value) + "." : LunarReach(world, moon) < 2 ? "" : " Aucune lune rousse avant longtemps : sa période tombe à côté des saisons d'éclipses.")
                            + (solar != null ? " Prochaine éclipse " + (SolarTotal(world, moon) ? "totale" : "annulaire") + " de soleil : " + date(solar.Value) + "." : "")
                    });
                }
                var sky = MoonSkyNote(world, moon);
                if (sky != null) list.Add(sky);
                if (system.Months.Count > 1 && moon.Period < longest)
                    list.Add(new SkyBehavior { Title = MoonLabel(moon) + " : des lunes bleues", Text = "Sa lunaison (" + Number(moon.Period, 2) + " j) est plus courte que le mois le plus long (" + longest + " j) : certains mois comptent deux pleines lunes. Le calendrier marque la seconde." });
            }
            if (free.Count > 1)
                list.Add(new SkyBehavior
                {
                    Title = free.Count + " lunes dans le ciel", Notable = true,
                    Text = "Des nuits à plusieurs lunes, des phases qui se croisent, une lune qui en cache parfois une autre ; des marées plus fortes quand elles s'alignent. Le calendrier marque les jours où toutes sont pleines ensemble."
                });
            return list;
        }

        // ----------------------------------------------------------------- taille

        /// <summary>Le calculateur de circonférence (15/11, Rémi) : tant de fois
        /// la Terre — 1,2 donne 48 090 km. Zéro pour un facteur qui n'en est pas un.</summary>
        public static double ScaledCircumference(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0) return 0;
            return Math.Round(EarthCircumference * factor);
        }

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

        /// <summary>Les mêmes faits, HIÉRARCHISÉS (16/11, Rémi) : par groupes
        /// (dimensions, horizon, soleil, tour du monde, latitudes, durée du
        /// jour), chaque fait avec son libellé, sa valeur et sa précision.</summary>
        public static List<SizeFactGroup> SizeFactGroups(World world, TimeSystem system)
        {
            var groups = new List<SizeFactGroup>();
            if (world == null || !world.HasSize) return groups;
            var size = new SizeFactGroup { Title = "Dimensions" };
            size.Facts.Add(new SizeFact { Label = "Rayon", Value = Number(Radius(world), 0) + " km" });
            size.Facts.Add(new SizeFact { Label = "Surface", Value = Number(Surface(world) / 1e6, 1) + " M km²", Note = "millions de km²" });
            size.Facts.Add(new SizeFact { Label = "Face à la Terre", Value = "× " + Number(world.Circumference / EarthCircumference, 2), Note = "en circonférence" });
            groups.Add(size);
            var horizon = new SizeFactGroup { Title = "Horizon" };
            horizon.Facts.Add(new SizeFact { Label = "Pour un marcheur", Value = Number(Horizon(world, 1.7), 1) + " km", Note = "les yeux à 1,70 m" });
            horizon.Facts.Add(new SizeFact { Label = "Du haut d'une tour", Value = Number(Horizon(world, 30), 1) + " km", Note = "à 30 m de haut" });
            var hours = Math.Max(1, system == null ? 24 : system.HoursPerDay);
            horizon.Facts.Add(new SizeFact { Label = "Une heure de soleil", Value = Number(HourBand(world, system), 0) + " km", Note = "à l'équateur · jour de " + hours + " h" });
            groups.Add(horizon);
            var year = system == null ? 365.25 : TimeMath.MeanYearDays(system);
            var travel = new SizeFactGroup { Title = "Le tour du monde" };
            foreach (var pace in new[] { new KeyValuePair<string, double>("À pied", 30), new KeyValuePair<string, double>("À cheval", 60), new KeyValuePair<string, double>("À la voile", 150) })
            {
                var days = TravelDays(world, pace.Value);
                travel.Facts.Add(new SizeFact { Label = pace.Key, Value = Number(days, 0) + " jours", Note = Number(pace.Value, 0) + " km/j" + (days >= year ? " · " + Number(days / year, 1) + " ans" : "") });
            }
            groups.Add(travel);
            if (world.HasTilt)
            {
                var latitudes = new SizeFactGroup { Title = "Latitudes" };
                latitudes.Facts.Add(new SizeFact { Label = "Tropiques", Value = Number(world.AxialTilt, 1) + "°", Note = "de part et d'autre de l'équateur" });
                latitudes.Facts.Add(new SizeFact { Label = "Cercles polaires", Value = Number(90 - world.AxialTilt, 1) + "°", Note = "au-delà : jour et nuit polaires" });
                groups.Add(latitudes);
                var light = new SizeFactGroup { Title = "Durée du jour, du solstice d'hiver à celui d'été" };
                foreach (var latitude in new[] { 0.0, 30, 45, 60 })
                    light.Facts.Add(new SizeFact
                    {
                        Label = latitude == 0 ? "Équateur" : "Latitude " + Number(latitude, 0) + "°",
                        Value = Hours(system, DayLength(world, system, latitude, false)) + " → " + Hours(system, DayLength(world, system, latitude, true))
                    });
                groups.Add(light);
            }
            return groups;
        }

        // --------------------------------------------------------------- en clair

        /// <summary>Un monde en une phrase (sa tuile dans l'Astrolabe).</summary>
        public static string Summary(World world)
        {
            var parts = new List<string>();
            if (world.HasSize) parts.Add(Number(world.Circumference, 0) + " km de tour");
            if (world.Seasons.Count > 0) parts.Add(TimeSystem.Count(world.Seasons.Count, "saison", "saisons"));
            if (world.Suns.Count > 1) parts.Add(world.Suns.Count + " soleils"); // un seul soleil va sans dire
            if (world.Moons.Count > 0) parts.Add(TimeSystem.Count(world.Moons.Count, "lune", "lunes"));
            if (world.Planets.Count > 0) parts.Add(TimeSystem.Count(world.Planets.Count, "planète voisine", "planètes voisines"));
            if (world.Phenomena.Count > 0) parts.Add(TimeSystem.Count(world.Phenomena.Count, "phénomène", "phénomènes"));
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
                foreach (var sun in Wanderers(world))
                    parts.Add((string.IsNullOrEmpty(sun.Name) ? "Second soleil" : sun.Name) + " : " + SunStateName(SunPhase(sun, day + 0.5)));
                foreach (var moon in world.Moons)
                {
                    if (IsSubMoon(world, moon)) continue; // dite par ses passages (NotesOn)
                    var main = MainPhaseOn(moon, day);
                    parts.Add((string.IsNullOrEmpty(moon.Name) ? "Lune" : moon.Name) + " : " + (main >= 0 ? MainPhaseName(main) : PhaseName(Phase(moon, day + 0.5))));
                }
                foreach (var note in NotesOn(world, system, day)) parts.Add(note.Text);
            }
            return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
        }
    }
}
