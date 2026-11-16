using System;
using System.Collections.Generic;
using System.Globalization;

namespace Marabook.Model
{
    // LES CARTES (2.0.0, 16/11/2026, Rémi : « le point culminant de l'aspect
    // worldbuilding »). Une carte (ItemKind.Map, racine « Cartes » de la Pile)
    // est une IMAGE découpée à l'import en pyramide de tuiles — comme les
    // cartes de the-universe-project (Leaflet, z/x/y) : un niveau par
    // doublement, le niveau 0 tient dans une tuile, le dernier est l'image
    // entière. On ne décode jamais que les tuiles que l'écran montre, et
    // l'image se raffine quand on s'approche.
    //
    // Par-dessus : des ZONES (polygones) et des REPÈRES (points), nommés,
    // colorés, liés au besoin à une fiche ; une ÉCHELLE (deux points et la
    // distance qui les sépare, ou un temps de trajet) dont tout le reste se
    // déduit — superficie de la carte, d'une zone, et, pour un planisphère
    // du monde entier, le tour et la surface du monde.
    //
    // Une carte PEUT être liée à un monde de l'Astrolabe (jour et nuit,
    // saisons, part de la surface du monde) ; sans monde, elle vit seule.

    /// <summary>Une zone tracée : un polygone en pixels de l'image entière.</summary>
    public class MapZone
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Zone";
        public string Color = "#5B67D8";
        public string LinkId;                    // la fiche liée, ou null
        public int Level;                        // Maps.LevelAlways, LevelFar, LevelMid, LevelNear
        public List<Pos> Points = new List<Pos>();

        public MapZone Clone()
        {
            var copy = new MapZone { Id = Id, Name = Name, Color = Color, LinkId = LinkId, Level = Level };
            copy.Points.AddRange(Points);
            return copy;
        }
    }

    /// <summary>Un repère : une ville, un lieu, un point de la carte.</summary>
    public class MapMarker
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Repère";
        public string Color = "#C0504D";
        public string LinkId;
        public int Level;
        public double X, Y;

        public MapMarker Clone()
        {
            return new MapMarker { Id = Id, Name = Name, Color = Color, LinkId = LinkId, Level = Level, X = X, Y = Y };
        }
    }

    public class MapInfo
    {
        public const int DefaultTileSize = 512;

        // L'image, telle que découpée à l'import.
        public int Width, Height;                // pixels du dernier niveau (l'image entière)
        public int TileSize = DefaultTileSize;
        public int Levels;                       // 0 = pas encore d'image
        public string Extension = ".jpg";        // « .jpg », ou « .png » pour une image à transparence
        public string SourceName;                // le nom du fichier importé
        public long SourceBytes;                 // son poids, pour dire ce que l'import a gagné ou coûté
        // Les tuiles, par clé « z/x_y » : en mémoire après un import, sinon lues à la demande
        // dans le .plot (MapTileSet, lot 12). PARTAGÉES entre les clones (annuler / rétablir
        // ne recopie jamais l'image).
        public MapTileSet Tiles = new MapTileSet();

        // Le monde, facultatif.
        public string WorldId;
        public bool WholeWorld;                  // l'image est le planisphère du monde ENTIER (360° × 180°)
        public string View = Maps.ViewFlat;      // Maps.ViewFlat, ViewGlobe
        // La CONSTRUCTION du planisphère (lot 12, 16/11) : comment l'image plate s'étale sur le monde.
        // « stretch » : elle couvre tout, 360° de large, d'un pôle à l'autre, quelles que soient ses
        // proportions. « fitted » : ses proportions sont gardées (un degré vaut autant de pixels en
        // large qu'en haut) et l'on dit OÙ sont les pôles, en fraction de sa hauteur — ils peuvent
        // être hors de l'image (elle ne monte pas jusqu'au pôle) ou dedans (une marge au-dessus).
        public string Layout = Maps.LayoutStretch;
        public double PoleNorth = 0, PoleSouth = 1;

        // L'échelle : deux points de l'image et la distance qui les sépare.
        public Pos ScaleA, ScaleB;
        public double ScaleKm;                   // 0 = pas d'échelle
        public string ScaleNote;                 // « 12 jours à cheval », si elle vient d'un trajet

        public List<MapZone> Zones = new List<MapZone>();
        public List<MapMarker> Markers = new List<MapMarker>();

        public bool HasImage { get { return Levels > 0 && Width > 0 && Height > 0; } }
        public bool HasScale { get { return ScaleKm > 0 && (Math.Abs(ScaleA.X - ScaleB.X) > 1e-6 || Math.Abs(ScaleA.Y - ScaleB.Y) > 1e-6); } }

        public MapZone FindZone(string id) { foreach (var zone in Zones) if (zone.Id == id) return zone; return null; }
        public MapMarker FindMarker(string id) { foreach (var marker in Markers) if (marker.Id == id) return marker; return null; }

        /// <summary>Une copie pour l'historique : tout, sauf les tuiles (la même table).</summary>
        public MapInfo Clone()
        {
            var copy = new MapInfo
            {
                Width = Width, Height = Height, TileSize = TileSize, Levels = Levels, Extension = Extension, SourceName = SourceName, SourceBytes = SourceBytes,
                Tiles = Tiles, WorldId = WorldId, WholeWorld = WholeWorld, View = View, ScaleA = ScaleA, ScaleB = ScaleB, ScaleKm = ScaleKm, ScaleNote = ScaleNote,
                Layout = Layout, PoleNorth = PoleNorth, PoleSouth = PoleSouth
            };
            foreach (var zone in Zones) copy.Zones.Add(zone.Clone());
            foreach (var marker in Markers) copy.Markers.Add(marker.Clone());
            return copy;
        }
    }

    /// <summary>Une allure de voyage, pour dire une distance en temps de trajet.</summary>
    public sealed class TravelMode
    {
        public string Name;
        public double KmPerDay;
    }

    public static class Maps
    {
        public const string ViewFlat = "flat", ViewGlobe = "globe";
        public const string LayoutStretch = "stretch", LayoutFitted = "fitted";
        public const int LevelAlways = 0, LevelFar = 1, LevelMid = 2, LevelNear = 3;
        public static readonly string[] LevelNames = { "Toujours", "Vue d'ensemble seulement", "À partir du zoom moyen", "De près seulement" };
        public const double MidZoom = 2, NearZoom = 5;   // en multiples de la vue « Tout voir »

        /// <summary>Les allures : des ordres de grandeur d'un voyage au long cours, haltes comprises.</summary>
        public static readonly TravelMode[] TravelModes =
        {
            new TravelMode { Name = "à pied", KmPerDay = 30 },
            new TravelMode { Name = "en caravane", KmPerDay = 25 },
            new TravelMode { Name = "à cheval", KmPerDay = 50 },
            new TravelMode { Name = "en chariot", KmPerDay = 35 },
            new TravelMode { Name = "en bateau fluvial", KmPerDay = 60 },
            new TravelMode { Name = "en voilier", KmPerDay = 150 }
        };

        // ------------------------------------------------------------ la pyramide

        /// <summary>Le nombre de niveaux d'une image : le niveau 0 tient dans UNE tuile, chaque niveau double.</summary>
        public static int LevelCount(int width, int height, int tileSize)
        {
            if (width <= 0 || height <= 0 || tileSize <= 0) return 0;
            var levels = 1;
            double side = Math.Max(width, height);
            while (side > tileSize) { side /= 2; levels++; }
            return levels;
        }

        /// <summary>La largeur (ou la hauteur) de l'image au niveau z : la dimension pleine divisée par 2 à chaque niveau en moins.</summary>
        public static int LevelSide(int full, int levels, int z)
        {
            var shift = Math.Max(0, levels - 1 - z);
            return Math.Max(1, (int)Math.Ceiling(full / Math.Pow(2, shift)));
        }

        public static int Columns(MapInfo map, int z) { return (LevelSide(map.Width, map.Levels, z) + map.TileSize - 1) / map.TileSize; }
        public static int Rows(MapInfo map, int z) { return (LevelSide(map.Height, map.Levels, z) + map.TileSize - 1) / map.TileSize; }

        public static int TileCount(int width, int height, int tileSize)
        {
            var levels = LevelCount(width, height, tileSize);
            var count = 0;
            for (var z = 0; z < levels; z++)
                count += ((LevelSide(width, levels, z) + tileSize - 1) / tileSize) * ((LevelSide(height, levels, z) + tileSize - 1) / tileSize);
            return count;
        }

        public static string TileKey(int z, int x, int y)
        {
            return z.ToString(CultureInfo.InvariantCulture) + "/" + x.ToString(CultureInfo.InvariantCulture) + "_" + y.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Le niveau à montrer pour une échelle d'affichage (px d'écran par px d'image) :
        /// le plus petit dont les tuiles ne sont pas agrandies — au plus le dernier.</summary>
        public static int LevelFor(MapInfo map, double scale)
        {
            if (map == null || map.Levels <= 0) return 0;
            var z = map.Levels - 1;
            while (z > 0 && scale <= 0.5) { scale *= 2; z--; }
            return z;
        }

        /// <summary>La place prise par la carte dans le .plot : la somme de ses tuiles.</summary>
        public static long Bytes(MapInfo map)
        {
            return map == null ? 0 : map.Tiles.TotalBytes;
        }

        /// <summary>« 12,4 Mo », « 830 Ko », « 512 octets ».</summary>
        public static string SizeLabel(long bytes)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", fr) + " Go";
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString(bytes >= 100 * 1024 * 1024 ? "0" : "0.0", fr) + " Mo";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0", fr) + " Ko";
            return bytes + (bytes > 1 ? " octets" : " octet");
        }

        // ------------------------------------------------------------ le projet

        /// <summary>Les cartes du projet (hors Corbeille), dans l'ordre de la Pile.</summary>
        public static List<BinderItem> All(Project project)
        {
            var list = new List<BinderItem>();
            var root = project == null ? null : project.Category(Project.KeyMaps);
            if (root == null) return list;
            foreach (var child in root.Children) if (child.Kind == ItemKind.Map) list.Add(child);
            return list;
        }

        public static World WorldOf(Project project, MapInfo map)
        {
            if (project == null || map == null || map.WorldId == null) return null;
            foreach (var world in project.Worlds) if (world.Id == map.WorldId) return world;
            return null;
        }

        public static bool Visible(int level, double zoom)
        {
            switch (level)
            {
                case LevelFar: return zoom < MidZoom;
                case LevelMid: return zoom >= MidZoom;
                case LevelNear: return zoom >= NearZoom;
                default: return true;
            }
        }

        // ------------------------------------------------------------ la sphère

        /// <summary>La construction « proportions gardées » est-elle en vigueur (et ses pôles lisibles) ?</summary>
        public static bool Fitted(MapInfo map) { return map != null && map.Layout == LayoutFitted && map.PoleSouth - map.PoleNorth > 1e-6; }

        /// <summary>Proportions gardées : les radians par pixel — les mêmes en large et en haut ;
        /// 180° séparent les deux pôles.</summary>
        public static double RadiansPerPixel(MapInfo map) { return Math.PI / ((map.PoleSouth - map.PoleNorth) * Math.Max(1, map.Height)); }

        /// <summary>La longitude (radians) d'une abscisse d'un planisphère entier : −π à π quand l'image
        /// fait le tour ; proportions gardées, comptée depuis le milieu de l'image (elle peut couvrir moins).</summary>
        public static double Longitude(MapInfo map, double x)
        {
            if (Fitted(map)) return (x - map.Width / 2.0) * RadiansPerPixel(map);
            return (x / Math.Max(1, map.Width) - 0.5) * 2 * Math.PI;
        }

        /// <summary>La latitude (radians, π/2 au nord) d'une ordonnée d'un planisphère entier.</summary>
        public static double Latitude(MapInfo map, double y)
        {
            if (Fitted(map)) return Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, Math.PI / 2 - (y - map.PoleNorth * map.Height) * RadiansPerPixel(map)));
            return (0.5 - y / Math.Max(1, map.Height)) * Math.PI;
        }

        /// <summary>L'inverse : l'abscisse d'une longitude, l'ordonnée d'une latitude (elles peuvent tomber hors de l'image).</summary>
        public static double XOf(MapInfo map, double longitude)
        {
            if (Fitted(map)) return map.Width / 2.0 + longitude / RadiansPerPixel(map);
            return (longitude / (2 * Math.PI) + 0.5) * map.Width;
        }

        public static double YOf(MapInfo map, double latitude)
        {
            if (Fitted(map)) return map.PoleNorth * map.Height + (Math.PI / 2 - latitude) / RadiansPerPixel(map);
            return (0.5 - latitude / Math.PI) * map.Height;
        }

        /// <summary>Les degrés de longitude que couvre la largeur de l'image : 360 quand elle est étirée.</summary>
        public static double LongitudeSpan(MapInfo map)
        {
            return Fitted(map) ? map.Width * RadiansPerPixel(map) * 180 / Math.PI : 360;
        }

        /// <summary>Proportions gardées : le pôle nord est-il DANS l'image (une marge au-dessus de lui) ?</summary>
        public static bool PoleNorthInside(MapInfo map) { return Fitted(map) && map.PoleNorth > 1e-9; }

        /// <summary>« 23,4° N · 12,5° E » : une position sur le monde (radians en entrée), la longitude
        /// comptée du méridien du centre de la carte.</summary>
        public static string GeoLabel(double latitude, double longitude)
        {
            return Astro.LatitudeLabel(latitude * 180 / Math.PI) + " · " + Astro.LongitudeLabel(longitude * 180 / Math.PI);
        }

        /// <summary>Les pôles aux bords de l'image, pour des proportions gardées : la hauteur couvre 180°.</summary>
        public static void ResetPoles(MapInfo map) { map.PoleNorth = 0; map.PoleSouth = 1; }

        /// <summary>LA PRÉSENCE D'UNE SAISON à une latitude (0 = aucune, 1 = pleine) : sans inclinaison de
        /// l'axe, il n'y a de saison nulle part ; entre les tropiques, elles s'effacent vers l'équateur,
        /// où le soleil passe deux fois l'an au zénith ; au-delà, elles sont entières.</summary>
        public static double SeasonPresence(World world, double latitude)
        {
            if (world == null || !world.HasTilt || world.Seasons.Count == 0) return 0;
            var tropic = Math.Abs(world.AxialTilt) * Math.PI / 180;
            if (tropic < 1e-6) return 0;
            return Math.Max(0, Math.Min(1, Math.Abs(latitude) / tropic));
        }

        /// <summary>L'angle au centre (radians) entre deux points d'un planisphère entier : le plus court chemin sur la sphère.</summary>
        public static double CentralAngle(MapInfo map, Pos a, Pos b)
        {
            double lat1 = Latitude(map, a.Y), lat2 = Latitude(map, b.Y), dLon = Longitude(map, b.X) - Longitude(map, a.X);
            var h = Math.Pow(Math.Sin((lat2 - lat1) / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
            return 2 * Math.Asin(Math.Min(1, Math.Sqrt(Math.Max(0, h))));
        }

        private static double PixelDistance(Pos a, Pos b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        /// <summary>Le rayon du monde (km) que dit un planisphère entier : par son échelle
        /// (distance ÷ angle au centre) d'abord, sinon par la circonférence du monde lié. 0 = inconnu.</summary>
        public static double WorldRadius(MapInfo map, World world)
        {
            if (map == null || !map.WholeWorld) return world != null && world.Circumference > 0 ? world.Circumference / (2 * Math.PI) : 0;
            if (map.HasScale)
            {
                var angle = CentralAngle(map, map.ScaleA, map.ScaleB);
                if (angle > 1e-9) return map.ScaleKm / angle;
            }
            return world != null && world.Circumference > 0 ? world.Circumference / (2 * Math.PI) : 0;
        }

        /// <summary>Le tour du monde à l'équateur (km) qu'un planisphère entier et son échelle impliquent ; 0 = inconnu.</summary>
        public static double ImpliedCircumference(MapInfo map)
        {
            if (map == null || !map.WholeWorld || !map.HasScale) return 0;
            return WorldRadius(map, null) * 2 * Math.PI;
        }

        /// <summary>Les km par pixel d'une carte plane (échelle) ; 0 = pas d'échelle. Un planisphère entier n'en a pas d'unique.</summary>
        public static double KmPerPixel(MapInfo map)
        {
            if (map == null || map.WholeWorld || !map.HasScale) return 0;
            var pixels = PixelDistance(map.ScaleA, map.ScaleB);
            return pixels > 1e-9 ? map.ScaleKm / pixels : 0;
        }

        /// <summary>La carte sait-elle mesurer (une échelle, ou un planisphère entier d'un monde de taille connue) ?</summary>
        public static bool CanMeasure(MapInfo map, World world)
        {
            if (map == null) return false;
            return map.WholeWorld ? WorldRadius(map, world) > 0 : KmPerPixel(map) > 0;
        }

        /// <summary>La distance (km) entre deux points de la carte ; 0 sans échelle.</summary>
        public static double Distance(MapInfo map, World world, Pos a, Pos b)
        {
            if (map == null) return 0;
            if (map.WholeWorld) return WorldRadius(map, world) * CentralAngle(map, a, b);
            return KmPerPixel(map) * PixelDistance(a, b);
        }

        /// <summary>La superficie (km²) d'un polygone de la carte ; 0 sans échelle ou à moins de trois points.</summary>
        public static double Area(MapInfo map, World world, List<Pos> points)
        {
            if (map == null || points == null || points.Count < 3) return 0;
            if (map.WholeWorld)
            {
                // Sur la sphère : la somme des trapèzes sphériques sous chaque côté.
                var radius = WorldRadius(map, world);
                double sum = 0;
                for (var i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    var q = points[(i + 1) % points.Count];
                    sum += (Longitude(map, q.X) - Longitude(map, p.X)) * (2 + Math.Sin(Latitude(map, p.Y)) + Math.Sin(Latitude(map, q.Y)));
                }
                return Math.Abs(sum) * radius * radius / 2;
            }
            double twice = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Count];
                twice += p.X * q.Y - q.X * p.Y;
            }
            var scale = KmPerPixel(map);
            return Math.Abs(twice) / 2 * scale * scale;
        }

        /// <summary>La superficie (km²) que couvre l'image : la surface du monde pour un planisphère entier.</summary>
        public static double MapArea(MapInfo map, World world)
        {
            if (map == null) return 0;
            if (map.WholeWorld) { var radius = WorldRadius(map, world); return 4 * Math.PI * radius * radius; }
            var scale = KmPerPixel(map);
            return (double)map.Width * map.Height * scale * scale;
        }

        /// <summary>La surface (km²) du monde lié, s'il a une taille ; 0 sinon.</summary>
        public static double WorldSurface(World world)
        {
            if (world == null || world.Circumference <= 0) return 0;
            var radius = world.Circumference / (2 * Math.PI);
            return 4 * Math.PI * radius * radius;
        }

        public static Pos Centroid(List<Pos> points)
        {
            if (points == null || points.Count == 0) return new Pos(0, 0);
            double x = 0, y = 0;
            foreach (var point in points) { x += point.X; y += point.Y; }
            return new Pos(x / points.Count, y / points.Count);
        }

        public static bool Contains(List<Pos> points, double x, double y)
        {
            var inside = false;
            if (points == null) return false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                if ((points[i].Y > y) != (points[j].Y > y)
                    && x < (points[j].X - points[i].X) * (y - points[i].Y) / (points[j].Y - points[i].Y) + points[i].X) inside = !inside;
            return inside;
        }

        // ------------------------------------------------------------ dire les mesures

        public static string KmLabel(double km)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (km <= 0 || double.IsNaN(km) || double.IsInfinity(km)) return "—";
            if (km < 1) return Math.Round(km * 1000).ToString("N0", fr) + " m";
            return km.ToString(km < 100 ? "N1" : "N0", fr) + " km";
        }

        public static string AreaLabel(double km2)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (km2 <= 0 || double.IsNaN(km2) || double.IsInfinity(km2)) return "—";
            if (km2 >= 1e6) return (km2 / 1e6).ToString(km2 >= 1e8 ? "N0" : "N2", fr) + " millions de km²";
            if (km2 < 1) return (km2 * 100).ToString("N1", fr) + " ha";
            return km2.ToString(km2 < 100 ? "N1" : "N0", fr) + " km²";
        }

        /// <summary>« 12 jours à pied · 7 à cheval · 2,4 en voilier » : une distance dite en trajets.</summary>
        public static string TravelLabel(double km)
        {
            if (km <= 0) return "";
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var parts = new List<string>();
            foreach (var index in new[] { 0, 2, 5 })
            {
                var days = km / TravelModes[index].KmPerDay;
                var text = days < 1 ? "moins d'un jour" : days.ToString(days < 10 ? "0.#" : "N0", fr) + (days < 2 ? " jour" : " jours");
                parts.Add(text + " " + TravelModes[index].Name);
            }
            return string.Join(" · ", parts.ToArray());
        }

        // ------------------------------------------------------------ jour et nuit, saisons

        /// <summary>La déclinaison du soleil (radians) au jour de l'année donné : l'inclinaison de l'axe,
        /// nulle au début de la PREMIÈRE saison du monde (prise pour l'équinoxe de printemps du nord),
        /// maximale un quart d'année plus tard. Sans inclinaison : 0, les jours valent les nuits partout.</summary>
        public static double Declination(World world, double dayOfYear, double yearDays)
        {
            if (world == null || !world.HasTilt || yearDays <= 0) return 0;
            double equinox = world.Seasons.Count > 0 ? world.Seasons[0].StartDay : 1;
            return world.AxialTilt * Math.PI / 180 * Math.Sin(2 * Math.PI * (dayOfYear - equinox) / yearDays);
        }

        /// <summary>La longitude (radians) où il est midi, pour une fraction du jour (0 = minuit au
        /// méridien du centre de la carte, 0,5 = midi) : le soleil va d'est en ouest.</summary>
        public static double NoonLongitude(double dayFraction)
        {
            var longitude = Math.PI - 2 * Math.PI * (dayFraction - Math.Floor(dayFraction));
            return longitude > Math.PI ? longitude - 2 * Math.PI : longitude;
        }

        /// <summary>L'éclairement d'un point (cosinus de l'angle au soleil) : positif = jour, négatif = nuit.</summary>
        public static double Daylight(double latitude, double longitude, double declination, double noonLongitude)
        {
            return Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(longitude - noonLongitude);
        }

        /// <summary>La durée du jour (en fraction de la journée) à une latitude : 0 = nuit polaire, 1 = jour sans fin.</summary>
        public static double DayShare(double latitude, double declination)
        {
            var x = -Math.Tan(latitude) * Math.Tan(declination);
            if (x >= 1) return 0;
            if (x <= -1) return 1;
            return Math.Acos(x) / Math.PI;
        }

        /// <summary>LE FONDU DES SAISONS (lot 13, Rémi : « un dégradé progressif des couleurs vers celles des
        /// autres saisons ») : au jour donné, la saison qui règne (a), celle vers laquelle on glisse (b) et la
        /// part de b (0 à 0,5). Au cœur d'une saison, elle est seule ; en approchant de sa fin, la suivante
        /// monte, jusqu'à moitié-moitié le jour du passage ; au début, c'est la précédente qui s'efface.
        /// Faux sans saisons.</summary>
        public static bool SeasonMix(World world, double dayOfYear, double yearDays, bool south, out int a, out int b, out double share)
        {
            a = b = -1;
            share = 0;
            if (world == null || world.Seasons.Count == 0) return false;
            a = b = SeasonIndex(world, dayOfYear, yearDays, south);
            if (world.Seasons.Count == 1 || yearDays <= 0) return true;
            if (south) { dayOfYear += yearDays / 2; if (dayOfYear > yearDays) dayOfYear -= yearDays; }
            // Les saisons dans l'ordre de l'année : celle d'avant, celle d'après.
            var order = new List<int>();
            for (var i = 0; i < world.Seasons.Count; i++) order.Add(i);
            order.Sort(delegate(int x, int y) { return world.Seasons[x].StartDay.CompareTo(world.Seasons[y].StartDay); });
            var at = order.IndexOf(a);
            int previous = order[(at + order.Count - 1) % order.Count], next = order[(at + 1) % order.Count];
            double start = world.Seasons[a].StartDay, end = world.Seasons[next].StartDay;
            var length = end - start;
            if (length <= 0) length += yearDays;
            var into = dayOfYear - start;
            if (into < 0) into += yearDays;
            var position = Math.Max(0, Math.Min(1, into / Math.Max(1e-9, length)));   // 0 = premier jour, 1 = dernier
            if (position < 0.5) { b = previous; share = 0.5 - position; }
            else { b = next; share = position - 0.5; }
            return true;
        }

        /// <summary>La saison (index dans world.Seasons) d'un jour de l'année ; −1 sans saisons.
        /// Hémisphère sud : la saison opposée (une demi-année plus loin).</summary>
        public static int SeasonIndex(World world, double dayOfYear, double yearDays, bool south)
        {
            if (world == null || world.Seasons.Count == 0) return -1;
            if (south && yearDays > 0) { dayOfYear += yearDays / 2; if (dayOfYear > yearDays) dayOfYear -= yearDays; }
            int best = -1, latest = 0;
            for (var i = 0; i < world.Seasons.Count; i++)
            {
                var start = world.Seasons[i].StartDay;
                if (world.Seasons[latest].StartDay < start) latest = i;
                if (start <= dayOfYear + 1e-9 && (best < 0 || world.Seasons[best].StartDay < start)) best = i;
            }
            return best < 0 ? latest : best;
        }
    }
}
