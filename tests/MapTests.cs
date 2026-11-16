using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C73 — 2.0.0, lot 11 (16/11/2026) : les frises sortent de l'Astrolabe
    /// (racines Chronologie, Astrolabe, Cartes), l'étendue d'une frise (fragment), les
    /// cartes (pyramide de tuiles, échelle, superficies, sphère, jour et nuit, .plot,
    /// historique) et l'Analyseur de .plot.</summary>
    public static class MapTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C73 — Chronologie et Astrolabe séparés, fragment d'une frise, cartes, Analyseur de .plot");
            Roots(t);
            Fragment(t);
            Pyramid(t);
            Measures(t);
            Sky(t);
            Persistence(t);
            Analysis(t);
        }

        private static void Roots(Harness t)
        {
            var project = Project.CreateNew();
            t.Check(project.Category(Project.KeyAstrolabe) != null && project.Category(Project.KeyAstrolabe).Title == "Astrolabe"
                && project.Category(Project.KeyMaps) != null && project.Category(Project.KeyMaps).Title == "Cartes",
                "un projet neuf a ses racines Astrolabe et Cartes");
            t.Equal("Chronologie", project.Category(Project.KeyTimelines).Title, "…et la racine des frises s'appelle « Chronologie »");
            // Un .plot d'avant (racine des frises nommée « Astrolabe », pas de racine Astrolabe ni Cartes) est remis d'aplomb.
            project.Category(Project.KeyTimelines).Title = "Astrolabe";
            project.Roots.Remove(project.Category(Project.KeyAstrolabe));
            project.Roots.Remove(project.Category(Project.KeyMaps));
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Parent = project.Category(Project.KeyTimelines), Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            project.Category(Project.KeyTimelines).Children.Add(timeline);
            InTemp(delegate(string dir)
            {
                var path = Path.Combine(dir, "avant.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Equal("Chronologie", loaded.Category(Project.KeyTimelines).Title, "un .plot du lot 10 : sa racine « Astrolabe » (les frises) est relue « Chronologie »");
                t.Check(loaded.Category(Project.KeyTimelines).Children.Count == 1 && loaded.Category(Project.KeyTimelines).Children[0].Title == "Histoire", "…ses frises y sont toujours");
                t.Check(loaded.Category(Project.KeyAstrolabe) != null && loaded.Category(Project.KeyAstrolabe).Children.Count == 0 && loaded.Category(Project.KeyMaps) != null,
                    "…et les racines Astrolabe (sans enfants) et Cartes lui sont ajoutées");
                t.Check(loaded.Roots.IndexOf(loaded.Category(Project.KeyMaps)) < loaded.Roots.IndexOf(loaded.Trash), "…avant la Corbeille");
            });
        }

        private static void Fragment(Harness t)
        {
            var system = TimeSystem.Gregorian();
            var timeline = new TimelineInfo { SystemId = system.Id, Types = TimelineInfo.DefaultTypes() };
            double min, max;
            t.Check(!timeline.HasFragment && !Timelines.FragmentRange(system, timeline, out min, out max) && Timelines.FragmentLabel(system, timeline) == "toute la durée",
                "sans bornes, une frise couvre toute la durée de son système");
            TimePoint from, to;
            t.Check(Timelines.ReadFragment(system, "1914", "1918", out from, out to) == null && system.Format(from).Contains("1914") && system.Format(to).Contains("1918"), "un fragment se lit dans la notation du système");
            timeline.From = from;
            timeline.To = to;
            t.Check(Timelines.FragmentRange(system, timeline, out min, out max) && Math.Abs(min - system.AxisStart(from)) < 1e-9 && Math.Abs(max - system.AxisEnd(to)) < 1e-9 && max - min >= 4.99,
                "ses bornes sur l'axe : du début de la première année à la fin de la dernière (cinq ans pour 1914-1918)");
            t.Check(Timelines.FragmentLabel(system, timeline).StartsWith("de ") && Timelines.FragmentLabel(system, timeline).Contains(" à "), "…dites « de … à … »");
            TimePoint a, b;
            t.Check(Timelines.ReadFragment(system, "1918", "1914", out a, out b) == null && a.Year == from.Year && b.Year == to.Year, "deux bornes à l'envers sont remises à l'endroit");
            t.Check(Timelines.ReadFragment(system, "pas une date", "1918", out a, out b) != null && Timelines.ReadFragment(system, "1914", "", out a, out b) != null,
                "une borne illisible ou vide est refusée, avec sa raison");
            var inside = new TimelineEvent { Start = new TimePoint(1916) };
            var before = new TimelineEvent { Start = new TimePoint(1900) };
            var after = new TimelineEvent { Start = new TimePoint(1930) };
            t.Check(!Timelines.OutsideFragment(system, timeline, inside) && Timelines.OutsideFragment(system, timeline, before) && Timelines.OutsideFragment(system, timeline, after),
                "un événement posé avant ou après le fragment est dit « hors du fragment »");
            // Copie et .plot.
            var copy = timeline.Clone();
            t.Check(copy.HasFragment && copy.From.Year == from.Year && !ReferenceEquals(copy.From, timeline.From), "le fragment suit la copie (annuler / rétablir)");
            var read = TimelineJson.ReadTimeline(Json.Parse(Json.Write(TimelineJson.BuildTimeline(timeline))));
            t.Check(read.HasFragment && read.From.Year == from.Year && read.To.Year == to.Year, "…et le .plot");
            var whole = TimelineJson.ReadTimeline(Json.Parse(Json.Write(TimelineJson.BuildTimeline(new TimelineInfo { SystemId = system.Id }))));
            t.Check(!whole.HasFragment, "une frise sans fragment n'en gagne pas à la relecture");
        }

        private static void Pyramid(Harness t)
        {
            t.Check(Maps.LevelCount(512, 300, 512) == 1 && Maps.LevelCount(513, 300, 512) == 2 && Maps.LevelCount(6058, 4246, 512) == 5 && Maps.LevelCount(0, 10, 512) == 0,
                "le nombre de niveaux : le niveau 0 tient dans une tuile, chaque niveau double (5 niveaux pour 6058 × 4246)");
            t.Check(Maps.LevelSide(6058, 5, 4) == 6058 && Maps.LevelSide(6058, 5, 3) == 3029 && Maps.LevelSide(6058, 5, 0) == 379 && Maps.LevelSide(4246, 5, 0) == 266,
                "la taille d'un niveau : la pleine largeur au dernier, divisée par deux à chaque niveau en moins");
            var map = new MapInfo { Width = 6058, Height = 4246, TileSize = 512, Levels = 5 };
            t.Check(Maps.Columns(map, 4) == 12 && Maps.Rows(map, 4) == 9 && Maps.Columns(map, 0) == 1 && Maps.Rows(map, 0) == 1, "les tuiles d'un niveau : 12 × 9 au dernier, 1 au premier");
            t.Check(Maps.TileCount(6058, 4246, 512) == 108 + 30 + 9 + 4 + 1, "…152 tuiles en tout");
            t.Equal("3/2_5", Maps.TileKey(3, 2, 5), "la clé d'une tuile : niveau/colonne_ligne");
            t.Check(Maps.LevelFor(map, 1) == 4 && Maps.LevelFor(map, 0.6) == 4 && Maps.LevelFor(map, 0.5) == 3 && Maps.LevelFor(map, 0.2) == 2 && Maps.LevelFor(map, 0.01) == 0 && Maps.LevelFor(map, 4) == 4,
                "le niveau à montrer : le plus petit dont les tuiles ne sont pas agrandies — le dernier au-delà");
            map.Tiles["0/0_0"] = new byte[1000];
            map.Tiles["1/0_0"] = new byte[2500];
            t.Check(Maps.Bytes(map) == 3500 && Maps.Bytes(null) == 0, "le poids d'une carte : la somme de ses tuiles");
            t.Check(Maps.SizeLabel(512) == "512 octets" && Maps.SizeLabel(1) == "1 octet" && Maps.SizeLabel(2048) == "2 Ko" && Maps.SizeLabel(8102676) == "7,7 Mo" && Maps.SizeLabel(3L * 1024 * 1024 * 1024) == "3,00 Go",
                "…dit en octets, Ko, Mo, Go");
            t.Check(Maps.Visible(Maps.LevelAlways, 0.5) && Maps.Visible(Maps.LevelFar, 1) && !Maps.Visible(Maps.LevelFar, 2.5) && !Maps.Visible(Maps.LevelMid, 1) && Maps.Visible(Maps.LevelMid, 2)
                && !Maps.Visible(Maps.LevelNear, 4) && Maps.Visible(Maps.LevelNear, 5), "la visibilité selon le zoom : toujours, de loin, à partir du zoom moyen, de près");
            // La copie partage les tuiles, pas les zones.
            map.Zones.Add(new MapZone { Name = "Nord" });
            var copy = map.Clone();
            copy.Zones[0].Name = "Sud";
            t.Check(ReferenceEquals(copy.Tiles, map.Tiles) && map.Zones[0].Name == "Nord" && copy.Width == 6058 && copy.Levels == 5, "une copie partage la table des tuiles (jamais recopiée) mais pas les zones");
        }

        private static void Measures(Harness t)
        {
            // Une carte plane de 1000 × 500 px ; 100 px = 250 km.
            var flat = new MapInfo { Width = 1000, Height = 500, Levels = 2, ScaleA = new Pos(100, 100), ScaleB = new Pos(200, 100), ScaleKm = 250 };
            t.Check(flat.HasScale && Math.Abs(Maps.KmPerPixel(flat) - 2.5) < 1e-9 && Maps.CanMeasure(flat, null), "une échelle : deux points et leur distance donnent les km par pixel");
            t.Check(Math.Abs(Maps.Distance(flat, null, new Pos(0, 0), new Pos(300, 400)) - 1250) < 1e-6, "une distance sur une carte plane (triangle 3-4-5 : 500 px, 1 250 km)");
            var square = new List<Pos> { new Pos(0, 0), new Pos(100, 0), new Pos(100, 100), new Pos(0, 100) };
            t.Check(Math.Abs(Maps.Area(flat, null, square) - 62500) < 1e-6 && Math.Abs(Maps.MapArea(flat, null) - 2500.0 * 1250) < 1e-3,
                "la superficie d'une zone (250 km de côté : 62 500 km²) et celle de la carte (2 500 × 1 250 km)");
            square.Reverse();
            t.Check(Math.Abs(Maps.Area(flat, null, square) - 62500) < 1e-6 && Maps.Area(flat, null, new List<Pos> { new Pos(0, 0), new Pos(1, 1) }) == 0, "…quel que soit le sens du tracé ; rien à moins de trois points");
            var none = new MapInfo { Width = 1000, Height = 500, Levels = 2 };
            t.Check(!none.HasScale && !Maps.CanMeasure(none, null) && Maps.Distance(none, null, new Pos(0, 0), new Pos(10, 0)) == 0 && Maps.AreaLabel(Maps.MapArea(none, null)) == "—", "sans échelle, la carte ne mesure rien");
            t.Check(Maps.Contains(square, 50, 50) && !Maps.Contains(square, 150, 50) && Math.Abs(Maps.Centroid(square).X - 50) < 1e-9, "un point est dans une zone ou non ; le centre d'une zone");

            // Le planisphère entier de la Terre : 2000 × 1000 px.
            var earth = World.Earth();
            var globe = new MapInfo { Width = 2000, Height = 1000, Levels = 3, WholeWorld = true };
            t.Check(Math.Abs(Maps.Longitude(globe, 0) + Math.PI) < 1e-9 && Math.Abs(Maps.Longitude(globe, 1000)) < 1e-9 && Math.Abs(Maps.Latitude(globe, 0) - Math.PI / 2) < 1e-9 && Math.Abs(Maps.Latitude(globe, 500)) < 1e-9,
                "un planisphère entier : la longitude de −180° à 180° sur la largeur, la latitude de 90° à −90° sur la hauteur");
            t.Check(Maps.CanMeasure(globe, earth) && !Maps.CanMeasure(globe, null), "sans échelle, il mesure par la taille du monde lié — et rien sans monde");
            var quarter = Maps.Distance(globe, earth, new Pos(1000, 500), new Pos(1500, 500));
            t.Check(Math.Abs(quarter - earth.Circumference / 4) < 1, "un quart de tour à l'équateur : le quart de la circonférence (" + Math.Round(quarter) + " km)");
            var pole = Maps.Distance(globe, earth, new Pos(1000, 500), new Pos(300, 0));
            t.Check(Math.Abs(pole - earth.Circumference / 4) < 1, "de l'équateur au pôle, quelle que soit la longitude du pôle : un quart de tour aussi (la sphère, pas l'image)");
            t.Check(Math.Abs(Maps.MapArea(globe, earth) - 510e6) < 2e6, "sa superficie est la surface du monde (510 millions de km² pour la Terre)");
            var north = new List<Pos> { new Pos(0, 0), new Pos(2000, 0), new Pos(2000, 500), new Pos(0, 500) };
            t.Check(Math.Abs(Maps.Area(globe, earth, north) - Maps.MapArea(globe, earth) / 2) < 1e3, "une zone qui couvre l'hémisphère nord : la moitié de la surface du monde");
            var band = new List<Pos> { new Pos(0, 0), new Pos(2000, 0), new Pos(2000, 250), new Pos(0, 250) };
            var share = Maps.Area(globe, earth, band) / Maps.MapArea(globe, earth);
            t.Check(Math.Abs(share - (1 - Math.Sin(Math.PI / 4)) / 2) < 1e-6, "…au-delà de 45° nord : 14,6 % seulement — les pôles sont étirés sur l'image, pas sur la sphère");

            // LA DEMANDE DE RÉMI : une distance entre deux points donne la taille du monde.
            globe.ScaleA = new Pos(1000, 500);
            globe.ScaleB = new Pos(1500, 500);
            globe.ScaleKm = 5000;
            t.Check(Math.Abs(Maps.ImpliedCircumference(globe) - 20000) < 1e-6 && Math.Abs(Maps.WorldRadius(globe, earth) - 20000 / (2 * Math.PI)) < 1e-6,
                "avec une échelle (un quart de tour = 5 000 km), le planisphère dit le tour du monde : 20 000 km — et l'échelle l'emporte sur la taille du monde lié");
            t.Check(Math.Abs(Maps.MapArea(globe, null) - 4 * Math.PI * Math.Pow(20000 / (2 * Math.PI), 2)) < 1e-3 && Maps.ImpliedCircumference(flat) == 0, "…et sa surface ; une carte plane n'implique aucune taille de monde");
            t.Check(Math.Abs(Maps.WorldSurface(earth) - 510e6) < 2e6 && Maps.WorldSurface(new World()) == 0, "la surface d'un monde par sa circonférence ; rien sans taille");

            // Les trajets.
            t.Check(Maps.TravelModes.Length >= 5 && Maps.TravelModes[0].KmPerDay == 30, "les allures de voyage : à pied, 30 km par jour");
            t.Check(Maps.TravelLabel(300).Contains("10 jours à pied") && Maps.TravelLabel(300).Contains("6 jours à cheval") && Maps.TravelLabel(300).Contains("2 jours en voilier") && Maps.TravelLabel(0) == "",
                "une distance dite en jours de voyage (300 km : 10 jours à pied, 6 à cheval, 2 en voilier)");
            t.Check(Maps.KmLabel(0.4) == "400 m" && Maps.KmLabel(12.34) == "12,3 km" && Maps.KmLabel(0) == "—" && Maps.AreaLabel(0.5) == "50,0 ha" && Maps.AreaLabel(510e6).EndsWith("millions de km²"),
                "les mesures dites : mètres, kilomètres, hectares, millions de km²");
        }

        private static void Sky(Harness t)
        {
            var earth = World.Earth();
            double year = 365;
            var equinox = earth.Seasons[0].StartDay;
            t.Check(Math.Abs(Maps.Declination(earth, equinox, year)) < 1e-9, "à l'équinoxe (le début de la première saison), le soleil est à l'aplomb de l'équateur");
            t.Check(Math.Abs(Maps.Declination(earth, equinox + year / 4, year) - earth.AxialTilt * Math.PI / 180) < 1e-9 && Maps.Declination(earth, equinox + 3 * year / 4, year) < 0,
                "un quart d'année plus tard, à l'aplomb du tropique nord (l'inclinaison de l'axe) ; trois quarts : au sud");
            t.Check(Maps.Declination(new World(), 100, 365) == 0 && Maps.Declination(null, 100, 365) == 0, "sans inclinaison (ou sans monde) : pas de saisons du soleil");
            t.Check(Math.Abs(Maps.NoonLongitude(0.5)) < 1e-9 && Math.Abs(Math.Abs(Maps.NoonLongitude(0)) - Math.PI) < 1e-9 && Maps.NoonLongitude(0.75) < 0 && Maps.NoonLongitude(0.25) > 0,
                "midi au centre de la carte à la mi-journée ; le soleil va d'est en ouest");
            t.Check(Maps.Daylight(0, 0, 0, 0) > 0.99 && Maps.Daylight(0, Math.PI, 0, 0) < -0.99 && Math.Abs(Maps.Daylight(0, Math.PI / 2, 0, 0)) < 1e-9, "l'éclairement : plein jour sous le soleil, pleine nuit à l'opposé, le terminateur à 90°");
            var summer = earth.AxialTilt * Math.PI / 180;
            t.Check(Math.Abs(Maps.DayShare(0, summer) - 0.5) < 1e-9 && Maps.DayShare(Math.PI / 4, summer) > 0.6 && Maps.DayShare(-Math.PI / 4, summer) < 0.4
                && Maps.DayShare(80 * Math.PI / 180, summer) == 1 && Maps.DayShare(-80 * Math.PI / 180, summer) == 0,
                "la durée du jour : égale à la nuit à l'équateur, longue au nord en été, soleil de minuit près du pôle, nuit polaire à l'autre");
            t.Check(Maps.SeasonIndex(earth, 100, year, false) == 0 && Maps.SeasonIndex(earth, 100, year, true) == 2 && Maps.SeasonIndex(earth, 10, year, false) == 3 && Maps.SeasonIndex(new World(), 10, year, false) == -1,
                "la saison d'un jour : le printemps au nord est l'automne au sud ; début janvier, l'hiver de l'année d'avant ; -1 sans saisons");
        }

        private static MapInfo SampleMap()
        {
            var map = new MapInfo { Width = 900, Height = 450, TileSize = 512, Levels = 2, SourceName = "ehnmir.jpg", SourceBytes = 123456, WorldId = "w1", WholeWorld = true, View = Maps.ViewGlobe };
            for (var z = 0; z < 2; z++)
                for (var y = 0; y < Maps.Rows(map, z); y++)
                    for (var x = 0; x < Maps.Columns(map, z); x++)
                    {
                        var bytes = new byte[4000 + z * 1000 + x];
                        new Random(z * 100 + x).NextBytes(bytes);
                        map.Tiles[Maps.TileKey(z, x, y)] = bytes;
                    }
            map.ScaleA = new Pos(10.5, 20.25);
            map.ScaleB = new Pos(300, 40);
            map.ScaleKm = 1200;
            map.ScaleNote = "40 jours à pied";
            var zone = new MapZone { Name = "Empire d'Aube", Color = "#3E9C7A", LinkId = "sheet-1", Level = Maps.LevelMid };
            zone.Points.Add(new Pos(10, 10));
            zone.Points.Add(new Pos(200.5, 12));
            zone.Points.Add(new Pos(120, 180.75));
            map.Zones.Add(zone);
            map.Markers.Add(new MapMarker { Name = "Port-Brume", Color = "#C0504D", X = 55.5, Y = 77, Level = Maps.LevelNear, LinkId = "sheet-2" });
            return map;
        }

        private static void Persistence(Harness t)
        {
            var project = Project.CreateNew();
            var root = project.Category(Project.KeyMaps);
            var item = new BinderItem { Kind = ItemKind.Map, Title = "Ehnmir", Parent = root, Map = SampleMap() };
            root.Children.Add(item);
            t.Check(Maps.All(project).Count == 1 && Maps.WorldOf(project, item.Map) == null, "une carte dans la racine Cartes ; son monde introuvable vaut « aucun »");
            var world = World.Earth();
            world.Id = "w1";
            project.Worlds.Add(world);
            t.Check(Maps.WorldOf(project, item.Map) == world, "…et son monde, quand il existe");
            InTemp(delegate(string dir)
            {
                var path = Path.Combine(dir, "cartes.plot");
                PlotFile.Save(project, path);
                using (var archive = ZipFile.OpenRead(path))
                {
                    var tiles = 0;
                    foreach (var entry in archive.Entries) if (entry.FullName.StartsWith("cartes/" + item.Id + "/", StringComparison.Ordinal)) tiles++;
                    t.Check(tiles == item.Map.Tiles.Count && archive.GetEntry("cartes/" + item.Id + "/1/1_0.jpg") != null && archive.GetEntry("cartes/" + item.Id + "/0/0_0.jpg") != null,
                        "le .plot : une entrée par tuile, « cartes/<id>/<niveau>/<colonne>_<ligne>.jpg » (" + tiles + ")");
                    var tile = archive.GetEntry("cartes/" + item.Id + "/1/1_0.jpg");
                    t.Check(tile.CompressedLength <= tile.Length + 64 && tile.Length == item.Map.Tiles["1/1_0"].Length, "…stockée telle quelle : une image déjà compressée ne se recompresse pas");
                }
                var loaded = PlotFile.Load(path);
                var back = Maps.All(loaded)[0];
                var map = back.Map;
                t.Check(back.Kind == ItemKind.Map && back.Title == "Ehnmir" && map.Width == 900 && map.Height == 450 && map.Levels == 2 && map.TileSize == 512 && map.Extension == ".jpg"
                    && map.SourceName == "ehnmir.jpg" && map.SourceBytes == 123456, "une carte relue : ses dimensions, ses niveaux, son fichier d'origine");
                var same = map.Tiles.Count == item.Map.Tiles.Count;
                foreach (var pair in item.Map.Tiles)
                {
                    byte[] bytes;
                    if (!map.Tiles.TryGetValue(pair.Key, out bytes) || bytes.Length != pair.Value.Length || bytes[bytes.Length - 1] != pair.Value[pair.Value.Length - 1] || bytes[0] != pair.Value[0]) same = false;
                }
                t.Check(same && !back.LoadDamaged, "…toutes ses tuiles, octet pour octet");
                t.Check(map.WorldId == "w1" && map.WholeWorld && map.View == Maps.ViewGlobe && map.HasScale && map.ScaleKm == 1200 && map.ScaleNote == "40 jours à pied"
                    && Math.Abs(map.ScaleA.X - 10.5) < 1e-9 && Math.Abs(map.ScaleA.Y - 20.25) < 1e-9 && map.ScaleB.X == 300, "…son monde, son rendu en globe, son échelle");
                t.Check(map.Zones.Count == 1 && map.Zones[0].Name == "Empire d'Aube" && map.Zones[0].Color == "#3E9C7A" && map.Zones[0].LinkId == "sheet-1" && map.Zones[0].Level == Maps.LevelMid
                    && map.Zones[0].Points.Count == 3 && Math.Abs(map.Zones[0].Points[2].Y - 180.75) < 1e-9 && map.Zones[0].Id == item.Map.Zones[0].Id, "…ses zones (nom, couleur, fiche, visibilité, sommets)");
                t.Check(map.Markers.Count == 1 && map.Markers[0].Name == "Port-Brume" && map.Markers[0].X == 55.5 && map.Markers[0].Y == 77 && map.Markers[0].Level == Maps.LevelNear && map.Markers[0].LinkId == "sheet-2",
                    "…ses repères");

                // L'enregistrement différentiel recopie les tuiles inchangées.
                var first = PlotFile.Write(PlotFile.Prepare(loaded), path, false);
                var result = PlotFile.Write(PlotFile.Prepare(loaded), path, false, null, first.Index);
                t.Check(result.Copied >= map.Tiles.Count, "un second enregistrement recopie les tuiles par bloc, sans les recompresser (" + result.Copied + " entrées recopiées)");

                // Un secours « textes seuls » n'a pas les tuiles : la carte survit, marquée abîmée.
                var texts = Path.Combine(dir, "textes.plot");
                PlotFile.Save(loaded, texts, true);
                var warnings = new List<string>();
                var light = PlotFile.Load(texts, warnings);
                var lightMap = Maps.All(light)[0];
                t.Check(lightMap.Map.Zones.Count == 1 && lightMap.Map.Tiles.Count == 0 && lightMap.LoadDamaged && warnings.Exists(delegate(string w) { return w.Contains("Ehnmir"); }),
                    "un secours « textes seuls » garde les zones et l'échelle, pas les tuiles — la carte est marquée, et c'est dit");
            });

            // Une carte sans image, et un manifeste vide.
            var empty = MapJson.Read(Json.Parse(Json.Write(MapJson.Build(new MapInfo()))));
            t.Check(!empty.HasImage && empty.Zones.Count == 0 && empty.View == Maps.ViewFlat && !empty.HasScale && MapJson.Read(null).Levels == 0, "une carte sans image se relit vide ; un nœud absent aussi");

            // L'historique : avant, après — et les tuiles ne sont jamais recopiées.
            var before = item.Map.Clone();
            item.Map.Zones.Clear();
            item.Map.ScaleKm = 0;
            var action = new MapEditAction(item, before, item.Map.Clone());
            action.Undo();
            t.Check(item.Map.Zones.Count == 1 && item.Map.ScaleKm == 1200 && ReferenceEquals(item.Map.Tiles, before.Tiles), "annuler rend les zones et l'échelle d'avant, sur la même table de tuiles");
            action.Do();
            t.Check(item.Map.Zones.Count == 0 && item.Map.ScaleKm == 0, "…rétablir les retire à nouveau");
            var twin = item.Duplicate(false);
            t.Check(twin.Map != null && !ReferenceEquals(twin.Map, item.Map) && ReferenceEquals(twin.Map.Tiles, item.Map.Tiles), "une carte dupliquée partage ses tuiles avec l'original (pas de double poids en mémoire)");
        }

        private static void Analysis(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var text = writings.Children[0];
            text.Document = TextDocument.FromPlainText(new string('a', 4000) + " Lorem ipsum dolor sit amet, consectetur adipiscing elit.");
            var sheets = project.Category(Project.KeySheets);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Ehnmir", Parent = sheets, ImageId = "portrait" };
            sheet.Document.Paragraphs.Add(new TextParagraph());
            sheet.FieldValues["champ"] = new string('z', 300);
            sheets.Children.Add(sheet);
            var portrait = new byte[30000];
            new Random(7).NextBytes(portrait);
            project.Images["portrait"] = new ProjectImage { Bytes = portrait, Extension = ".jpg" };
            var maps = project.Category(Project.KeyMaps);
            var map = new BinderItem { Kind = ItemKind.Map, Title = "Le monde", Parent = maps, Map = SampleMap() };
            maps.Children.Add(map);
            var media = new byte[9000];
            new Random(9).NextBytes(media);
            var research = project.Category(Project.KeyResearch);
            research.Children.Add(new BinderItem { Kind = ItemKind.Media, Title = "Photo", Parent = research, MediaBytes = media, MediaExtension = ".png" });
            var trashed = new BinderItem { Kind = ItemKind.Text, Title = "Jeté", Parent = project.Trash };
            trashed.Document = TextDocument.FromPlainText("Un texte jeté, assez long pour peser quelque chose dans le fichier.");
            project.Trash.Children.Add(trashed);

            t.Check(PlotAnalysis.SectionOf(text) == PlotAnalysis.Writings && PlotAnalysis.SectionOf(sheet) == PlotAnalysis.Sheets && PlotAnalysis.SectionOf(map) == PlotAnalysis.Maps
                && PlotAnalysis.SectionOf(trashed) == PlotAnalysis.Trash && PlotAnalysis.SectionOf(project.Category(Project.KeyAstrolabe)) == PlotAnalysis.Timeline,
                "chaque élément a sa section : écrits, fiches, cartes, chronologie et astrolabe — la Corbeille d'abord");
            InTemp(delegate(string dir)
            {
                var path = Path.Combine(dir, "analyse.plot");
                PlotFile.Save(project, path);
                var report = PlotAnalysis.Analyze(path, project);
                long sum = 0;
                foreach (var section in report.Sections) sum += section.Bytes;
                t.Check(report.FileBytes == new FileInfo(path).Length && sum == report.FileBytes, "l'analyse rend compte de CHAQUE octet du fichier (" + sum + " sur " + report.FileBytes + ")");
                var cartes = report.Find(PlotAnalysis.Maps);
                var tiles = Maps.Bytes(map.Map);
                t.Check(cartes.Bytes >= tiles && cartes.Bytes < tiles + 4000 && cartes.Count == 1 && cartes.Details.Count >= 1 && cartes.Details[0].Name == "« Le monde »" && cartes.Details[0].Bytes == tiles,
                    "les cartes : le poids des tuiles, une ligne par carte (" + cartes.Bytes + " octets pour " + tiles + " de tuiles)");
                var fiches = report.Find(PlotAnalysis.Sheets);
                t.Check(fiches.Details.Exists(delegate(PlotAnalysis.Detail d) { return d.Name == "Illustrations des fiches" && d.Bytes >= 30000; }) && fiches.Count == 1 && fiches.Bytes > 30000,
                    "les fiches : leurs illustrations comptées avec elles");
                t.Check(report.Find(PlotAnalysis.Writings).Bytes > 0 && report.Find(PlotAnalysis.Writings).Count == 1 && report.Find(PlotAnalysis.Research).Bytes >= 9000
                    && report.Find(PlotAnalysis.Trash).Bytes > 0 && report.Find(PlotAnalysis.Settings).Bytes > 0 && report.Find(PlotAnalysis.Envelope).Bytes > 0,
                    "écrits, recherche, corbeille, réglages et enveloppe du zip ont chacun leur poids");
                t.Check(report.Sections[0].Name == PlotAnalysis.Sheets && report.Sections[1].Name == PlotAnalysis.Maps && report.Sections[report.Sections.Count - 1].Name == PlotAnalysis.Envelope && report.Entries > 8,
                    "les sections vont de la plus lourde à la plus légère, l'enveloppe en dernier");
                // Une carte jetée pèse dans la Corbeille.
                maps.Children.Remove(map);
                map.Parent = project.Trash;
                project.Trash.Children.Add(map);
                PlotFile.Save(project, path);
                var after = PlotAnalysis.Analyze(path, project);
                t.Check(after.Find(PlotAnalysis.Maps).Bytes < 2000 && after.Find(PlotAnalysis.Trash).Bytes >= tiles, "une carte à la Corbeille pèse dans la Corbeille — la vider rend la place");
                // Sans projet (rien pour classer), l'analyse tient quand même le compte.
                var blind = PlotAnalysis.Analyze(path, null);
                long blindSum = 0;
                foreach (var section in blind.Sections) blindSum += section.Bytes;
                t.Check(blindSum == blind.FileBytes, "sans projet pour classer, le compte reste juste");
            });
        }

        private static void InTemp(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c73-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try { body(dir); }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
