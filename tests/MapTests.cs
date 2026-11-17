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
            t.Suite("C74 — lot 12 : tuiles lues à la demande dans le .plot, construction du planisphère, présence des saisons, onglets de la carte au rail");
            LazyTiles(t);
            Layout(t);
            Rail(t);
            t.Suite("C75 — lot 13 : marqueurs de navigation d'un monde, fondu des saisons, latitude et longitude, mémoire des cartes");
            Guides(t);
            SeasonBlend(t);
            MapMemory(t);
            t.Suite("C76 — lot 14 : l'allure d'un astre (accent, distance, anneaux, inclinaison)");
            Looks(t);
            t.Suite("C77 — lot 16 : taille et orbite d'un astre, écart, épaisseur, angle et grain des anneaux");
            Rings(t);
            t.Suite("C78 — lot 17 : le diamètre et la distance des astres entrent dans les calculs (éclipses, marées, taille dans le ciel)");
            Sizes(t);
        }

        private static void Sizes(Harness t)
        {
            var calendar = TimeSystem.Gregorian();
            var earth = World.Earth();
            var moon = earth.Moons[0];
            t.Check(Math.Abs(Astro.SolarCover(earth, moon) - 0.971) < 0.002 && Astro.SolarTotal(earth, moon) && Astro.LunarReach(earth, moon) == 2 && Math.Abs(Astro.UmbraRadius(earth, BodyLook.MoonDistance) - 4605) < 10,
                "chez nous : la Lune est à peine moins large que le Soleil (0,97) et le couvre au plus près ; l'ombre de la Terre, large de 4 600 km là où passe la Lune, la couvre en entier");
            t.Check(Astro.MoonSkyNote(earth, moon) == null && Astro.SunSkyNote(earth) == null && Astro.SunBehaviors(earth, calendar).Count == 1 && Math.Abs(Astro.TideFactor(moon) - 1) < 1e-9,
                "un ciel comme le nôtre : rien de plus à en dire");
            t.Equal("éclipse totale de soleil", Astro.EclipseName(earth, moon, Astro.TotalSolar), "…et l'éclipse de soleil reste totale");

            // Deux fois plus loin : deux fois moins large — elle ne couvre plus le soleil.
            moon.Look.Distance = BodyLook.MoonDistance * 2;
            t.Check(!Astro.SolarTotal(earth, moon) && Astro.LunarReach(earth, moon) == 2 && Math.Abs(Astro.TideFactor(moon) - 0.125) < 1e-9, "une lune deux fois plus loin : elle ne couvre plus le soleil ; ses marées, huit fois plus faibles");
            t.Equal("éclipse annulaire de soleil (24 % du disque)", Astro.EclipseName(earth, moon, Astro.TotalSolar), "son éclipse de soleil devient annulaire");
            var note = Astro.MoonSkyNote(earth, moon);
            t.Check(note != null && note.Notable && note.Title == "Lune : sa taille dans le ciel" && note.Text.Contains("0,5 fois la largeur de notre Lune") && note.Text.Contains("annulaires") && note.Text.Contains("Marées"),
                "la fenêtre du monde le dit (" + (note == null ? "" : note.Text) + ")");
            t.Check(Astro.MoonBehaviors(earth, calendar).Exists(delegate(SkyBehavior b) { return b.Title == "Lune : sa taille dans le ciel"; })
                && Astro.MoonBehaviors(earth, calendar).Exists(delegate(SkyBehavior b) { return b.Text.Contains("Prochaine éclipse annulaire de soleil"); }), "…parmi ce que le ciel déclenche");

            // Plus large que l'ombre du monde : plus de lune rousse.
            moon.Look.Distance = BodyLook.MoonDistance;
            moon.Look.Diameter = 12000;
            t.Check(Astro.LunarReach(earth, moon) == 1 && Astro.EclipseOn(earth, calendar, moon, 0) == Astro.PartialLunar && Astro.NextEclipse(earth, calendar, moon, 0, Astro.TotalLunar) == null && Astro.SolarTotal(earth, moon),
                "une lune plus large que l'ombre du monde : jamais de lune rousse, des éclipses partielles ; elle couvre le soleil sans peine");
            moon.Eclipses = Astro.EclipseAlways;
            t.Check(Astro.EclipseOn(earth, calendar, moon, 0) == Astro.PartialLunar, "…même « à chaque lunaison »");
            moon.Eclipses = Astro.EclipseSeasons;
            // Au-delà de la pointe du cône d'ombre : aucune éclipse de lune.
            moon.Look.Diameter = 0;
            moon.Look.Distance = 1500000;
            t.Check(Astro.LunarReach(earth, moon) == 0 && Astro.EclipseOn(earth, calendar, moon, 0) == Astro.NoEclipse && Astro.MoonSkyNote(earth, moon).Text.Contains("aucune éclipse de lune"), "une lune au-delà de l'ombre du monde : aucune éclipse de lune");
            // Un monde plus petit porte une ombre plus étroite.
            moon.Look.Distance = BodyLook.MoonDistance;
            earth.Circumference = 12000;
            t.Check(Astro.LunarReach(earth, moon) < 2, "un monde trois fois plus petit : son ombre ne couvre plus notre Lune");
            earth.Circumference = Astro.EarthCircumference;

            // Le soleil : deux fois plus loin, deux fois moins large — la lune le couvre largement.
            earth.Suns[0].Look.Distance = BodyLook.SunDistance * 2;
            var sunNote = Astro.SunSkyNote(earth);
            t.Check(Math.Abs(Astro.SolarCover(earth, moon) - 1.942) < 0.004 && sunNote != null && sunNote.Text.Contains("0,5 fois la largeur de notre Soleil") && sunNote.Text.Contains("0,25 fois la lumière") && Astro.SunBehaviors(earth, calendar).Count == 2,
                "un soleil deux fois plus loin : moitié moins large dans le ciel, un quart de sa lumière");
            earth.Suns[0].Look = new BodyLook { Accent = "#D9503A" };
            t.Check(Astro.SunWorldDistance(earth, earth.Suns[0]) == BodyLook.SunDistance, "une allure de satellite posée sur un soleil ne le rapproche pas à la distance de la Lune");
            earth.Suns[0].Look = BodyLook.ForSun();

            // L'éclipse solaire pure : le compagnon, plus près de nous quand il passe devant, paraît plus large.
            var second = new Sun { Name = "Braise", Period = 40, HighAt = 100, OrbitId = earth.Suns[0].Id, Size = 0.5 };
            earth.Suns.Add(second);
            Astro.SetInner(earth, second, true);
            t.Check(Math.Abs(Astro.SunEclipseCover(earth, second, Astro.SunInFront) - 0.25) < 1e-9, "un compagnon moitié moins large, sans distance dite : il cache un quart du disque");
            second.Look.Distance = BodyLook.SunDistance * 0.5;
            t.Check(Math.Abs(Astro.SunEclipseCover(earth, second, Astro.SunInFront) - 1) < 1e-9 && Math.Abs(Astro.SunEclipseCover(earth, second, Astro.SunBehind) - 1) < 1e-9 && Astro.SunWorldDistance(earth, second) == BodyLook.SunDistance,
                "à mi-chemin du monde, il paraît aussi large que son soleil quand il passe devant : l'éclipse devient totale");
            second.Look.Distance = BodyLook.SunDistance * 0.2;
            t.Check(Math.Abs(Astro.SunEclipseCover(earth, second, Astro.SunInFront) - 0.390625) < 1e-9, "à un cinquième du chemin : 39 % du disque");
        }

        /// <summary>Lot 16 (17/11) : le diamètre et la distance d'un astre, les réglages de ses anneaux.</summary>
        private static void Rings(Harness t)
        {
            var look = new BodyLook();
            double inner, outer;
            look.RingSpan(out inner, out outer);
            t.Check(look.RingGap == 1 && look.RingThickness == 1 && look.RingAngle == 0 && look.RingTexture && look.RingGrain == 1 && look.Diameter == 0 && look.DiameterKm == 3474.8,
                "l'allure d'office : des anneaux à l'écart et à l'épaisseur de Saturne, sans angle, avec leur grain ; un satellite large comme notre Lune");
            t.Check(Math.Abs(inner - 1.35) < 1e-9 && Math.Abs(outer - 2.1) < 1e-9, "d'office, les anneaux vont de 1,35 à 2,1 rayons de l'astre");
            look.RingGap = 0;
            look.RingSpan(out inner, out outer);
            t.Check(Math.Abs(inner - 1) < 1e-9 && Math.Abs(outer - 1.75) < 1e-9, "sans écart, ils commencent au ras de l'astre — jamais dedans");
            look.RingGap = 2; look.RingThickness = 0.5; look.RingSize = 2;
            look.RingSpan(out inner, out outer);
            t.Check(Math.Abs(inner - 2.4) < 1e-9 && Math.Abs(outer - 3.15) < 1e-9, "l'écart les éloigne, l'épaisseur les amincit, l'ampleur grandit le tout");
            look.RingGap = 99; look.RingThickness = -4; look.RingSize = 99;
            look.RingSpan(out inner, out outer);
            t.Check(Math.Abs(inner - 5.2) < 1e-9 && outer > inner, "des réglages fous restent dans leurs bornes : la bande garde une largeur");

            var sun = BodyLook.ForSun();
            t.Check(sun.IsDefault && sun.Distance == BodyLook.SunDistance && new Sun().Look.IsDefault && new Sun().Look.Distance == BodyLook.SunDistance, "un soleil est d'office à la distance de la Terre au Soleil — et c'est encore « d'office »");
            t.Equal("1,2 × notre Lune", BodyLook.TimesLabel(4169.76, BodyLook.MoonDiameter, "notre Lune"), "une grandeur, dite en fois celle de notre système");

            var world = World.Earth();
            world.Look = new BodyLook { Rings = true, RingGap = 1.8, RingThickness = 0.6, RingAngle = 25, RingTexture = false, RingGrain = 2.5 };
            world.Moons[0].Look = new BodyLook { Diameter = 4169.76 };
            world.Suns[0].Look.Distance = BodyLook.SunDistance * 2;
            t.Check(!world.Look.IsDefault && !world.Moons[0].Look.IsDefault && !world.Suns[0].Look.IsDefault, "un diamètre, une distance de soleil, un réglage d'anneaux : plus « d'office »");
            var copy = world.Clone();
            t.Check(copy.Look.RingGap == 1.8 && copy.Look.RingThickness == 0.6 && copy.Look.RingAngle == 25 && !copy.Look.RingTexture && copy.Look.RingGrain == 2.5 && copy.Moons[0].Look.Diameter == 4169.76
                && copy.Suns[0].Look.Distance == BodyLook.SunDistance * 2 && copy.Suns[0].Look.DefaultDistance == BodyLook.SunDistance, "une copie du monde emporte tout cela");
            var back = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { world }))))[0];
            t.Check(back.Look.Rings && back.Look.RingGap == 1.8 && back.Look.RingThickness == 0.6 && back.Look.RingAngle == 25 && !back.Look.RingTexture && back.Look.RingGrain == 2.5, "le monde relu : l'écart, l'épaisseur, l'angle, la texture et le grain de ses anneaux");
            t.Check(back.Moons[0].Look.Diameter == 4169.76 && back.Moons[0].Look.Distance == BodyLook.MoonDistance, "son satellite : le diamètre ; la distance, restée d'office");
            t.Check(back.Suns[0].Look.Distance == BodyLook.SunDistance * 2 && back.Suns[0].Look.DefaultDistance == BodyLook.SunDistance, "son soleil : deux fois plus loin que le nôtre");
            var plain = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { World.Earth() }))))[0];
            t.Check(plain.Suns[0].Look.IsDefault && plain.Suns[0].Look.Distance == BodyLook.SunDistance && plain.Moons[0].Look.IsDefault, "un monde d'avant le lot 16 se relit : son soleil à la distance du nôtre, sa lune large comme la nôtre");
            // Une allure de satellite posée sur un soleil (un .plot du lot 14) : sa distance « d'office » ne devient pas celle de la Lune.
            var old = World.Earth();
            old.Suns[0].Look = new BodyLook { Accent = "#D9503A" };
            var oldBack = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { old }))))[0];
            t.Check(oldBack.Suns[0].Look.Accent == "#D9503A" && oldBack.Suns[0].Look.Distance == BodyLook.SunDistance, "un soleil personnalisé au lot 14 garde son accent et prend la distance d'un soleil");
            var odd = TimelineJson.ReadWorlds(Json.Parse("[{\"id\":\"w\",\"name\":\"X\",\"look\":{\"ringGap\":-3,\"ringThickness\":50,\"ringAngle\":400,\"ringGrain\":0,\"diameter\":-8}}]"))[0];
            t.Check(odd.Look.RingGap == 0 && odd.Look.RingThickness == 3 && odd.Look.RingAngle == 90 && odd.Look.RingGrain == 0.25 && odd.Look.Diameter == 0, "des valeurs folles dans le fichier sont ramenées dans leurs bornes");
        }

        /// <summary>Lot 14 (16/11) : personnaliser un soleil, un satellite, le monde.</summary>
        private static void Looks(Harness t)
        {
            var look = new BodyLook();
            t.Check(look.IsDefault && look.Distance == 384400 && !look.Rings && look.RingSize == 1 && look.Tilt == 0 && look.Accent == null, "l'allure d'office : sans accent ni anneaux, à la distance de la Terre à la Lune");
            t.Equal("1 × la distance de la Terre à la Lune", BodyLook.DistanceLabel(384400), "la distance, dite en fois la nôtre");
            t.Equal("2,5 × la distance de la Terre à la Lune", BodyLook.DistanceLabel(961000), "…deux fois et demie");
            var world = World.Earth();
            t.Check(world.Look.IsDefault && world.Suns[0].Look.IsDefault && world.Moons[0].Look.IsDefault, "notre monde, son Soleil et sa Lune ont l'allure d'office");
            var plain = TimelineJson.BuildWorlds(new List<World> { world })[0] as Dictionary<string, object>;
            var plainSun = (plain["suns"] as List<object>)[0] as Dictionary<string, object>;
            var plainMoon = (plain["moons"] as List<object>)[0] as Dictionary<string, object>;
            t.Check(!plain.ContainsKey("look") && !plainSun.ContainsKey("look") && !plainMoon.ContainsKey("look"), "d'office : rien de plus dans le manifeste (un .plot d'avant se relit tel quel)");

            world.Look = new BodyLook { Accent = "#6FC3C0", Rings = true, RingColor = "#E7E3D8", RingSize = 1.4 };
            world.Suns[0].Look = new BodyLook { Accent = "#D9503A", Tilt = 12 };
            world.Moons[0].Look = new BodyLook { Distance = 768800, Tilt = 5.1, Rings = true };
            t.Check(!world.Look.IsDefault && !world.Suns[0].Look.IsDefault && !world.Moons[0].Look.IsDefault, "personnalisés, ils ne sont plus « d'office »");
            var copy = world.Clone();
            copy.Look.Rings = false;
            copy.Moons[0].Look.Distance = 1;
            t.Check(world.Look.Rings && world.Moons[0].Look.Distance == 768800 && copy.Suns[0].Look.Accent == "#D9503A" && !ReferenceEquals(copy.Suns[0].Look, world.Suns[0].Look), "une copie du monde emporte les allures, sans les partager");
            var back = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { world }))))[0];
            t.Check(back.Look.Accent == "#6FC3C0" && back.Look.Rings && back.Look.RingColor == "#E7E3D8" && Math.Abs(back.Look.RingSize - 1.4) < 1e-9, "le monde relu : son accent, ses anneaux, leur couleur, leur ampleur");
            t.Check(back.Suns[0].Look.Accent == "#D9503A" && back.Suns[0].Look.Tilt == 12 && !back.Suns[0].Look.Rings, "son soleil : l'accent du halo, l'inclinaison de l'axe");
            t.Check(back.Moons[0].Look.Distance == 768800 && back.Moons[0].Look.Tilt == 5.1 && back.Moons[0].Look.Rings && back.Moons[0].Look.Accent == null, "son satellite : la distance, l'inclinaison de l'orbite, les anneaux");
            var odd = TimelineJson.ReadWorlds(Json.Parse("[{\"id\":\"w\",\"name\":\"X\",\"look\":{\"ringSize\":99,\"tilt\":400,\"distance\":-5}}]"))[0];
            t.Check(odd.Look.RingSize == 4 && odd.Look.Tilt == 90 && odd.Look.Distance == 384400, "des valeurs folles dans le fichier sont ramenées dans leurs bornes");
        }

        /// <summary>Lot 13 (16/11) : les marqueurs de navigation — par défaut d'après l'inclinaison, ou ceux de l'auteur.</summary>
        private static void Guides(Harness t)
        {
            var world = World.Earth();
            var guides = Astro.GuidesOf(world);
            t.Check(!world.CustomGuides && guides.Count == 5 && guides[0].Name == "Équateur" && guides[0].Latitude == 0, "un monde neuf a ses marqueurs par défaut : l'équateur d'abord");
            t.Check(Math.Abs(guides[1].Latitude - 23.44) < 1e-9 && Math.Abs(guides[2].Latitude + 23.44) < 1e-9 && guides[1].Name == "Tropique nord" && guides[2].Name == "Tropique sud",
                "…les deux tropiques, à la latitude de l'inclinaison de l'axe");
            t.Check(Math.Abs(guides[3].Latitude - 66.56) < 1e-9 && Math.Abs(guides[4].Latitude + 66.56) < 1e-9 && guides[3].Name == "Cercle polaire nord", "…et les deux cercles polaires, à son complément");
            world.AxialTilt = 10;
            t.Check(Math.Abs(Astro.GuidesOf(world)[1].Latitude - 10) < 1e-9 && Math.Abs(Astro.GuidesOf(world)[3].Latitude - 80) < 1e-9, "ils suivent l'inclinaison quand elle change");
            world.AxialTilt = 0;
            t.Check(Astro.GuidesOf(world).Count == 1, "sans inclinaison : l'équateur seul");
            world.AxialTilt = -1;
            t.Check(Astro.GuidesOf(world).Count == 1 && Astro.GuidesOf(null).Count == 1 && Astro.GuidesOf(null)[0].Name == "Équateur", "inclinaison inconnue, ou pas de monde : l'équateur seul");
            world.AxialTilt = 23.44;

            // Personnalisés : la liste de l'auteur, qui ne suit plus l'inclinaison.
            world.CustomGuides = true;
            world.Guides.Add(new WorldGuide { Name = "Ceinture d'Aube", Latitude = 12.5, Color = "#C0504D" });
            world.Guides.Add(new WorldGuide { Name = "Ligne des Brumes", Latitude = -48 });
            world.AxialTilt = 30;
            guides = Astro.GuidesOf(world);
            t.Check(guides.Count == 2 && guides[0].Name == "Ceinture d'Aube" && guides[0].Latitude == 12.5 && guides[1].Latitude == -48, "personnalisés : la liste est celle de l'auteur, et l'inclinaison n'y touche plus");
            t.Check(Astro.ColorOf(guides[0], 0) == "#C0504D" && Astro.ColorOf(guides[1], 1) == Astro.GuideColors[1], "un marqueur a sa teinte, ou celle de son rang");
            var copy = world.Clone();
            copy.Guides[0].Name = "autre";
            t.Check(copy.CustomGuides && copy.Guides.Count == 2 && world.Guides[0].Name == "Ceinture d'Aube" && copy.Guides[1].Id == world.Guides[1].Id, "une copie du monde emporte ses marqueurs, sans les partager");
            var back = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { world }))))[0];
            t.Check(back.CustomGuides && back.Guides.Count == 2 && back.Guides[0].Name == "Ceinture d'Aube" && back.Guides[0].Latitude == 12.5 && back.Guides[0].Color == "#C0504D"
                && back.Guides[1].Latitude == -48 && back.Guides[1].Id == world.Guides[1].Id, "ils s'enregistrent et se relisent (nom, latitude, teinte)");
            var plain = TimelineJson.BuildWorlds(new List<World> { World.Earth() })[0] as Dictionary<string, object>;
            t.Check(!plain.ContainsKey("guides") && !TimelineJson.ReadWorlds(Json.Parse(Json.Write(new List<object> { plain })))[0].CustomGuides, "par défaut : rien de plus dans le manifeste (un .plot d'avant se relit tel quel)");
            world.Guides.Clear();
            var none = TimelineJson.ReadWorlds(Json.Parse(Json.Write(TimelineJson.BuildWorlds(new List<World> { world }))))[0];
            t.Check(none.CustomGuides && none.Guides.Count == 0 && Astro.GuidesOf(none).Count == 0, "une liste personnalisée vide reste vide : l'auteur ne veut aucun marqueur");

            // Dire une position.
            t.Equal("23,4° N", Astro.LatitudeLabel(23.44), "une latitude nord");
            t.Equal("66,6° S", Astro.LatitudeLabel(-66.56), "une latitude sud");
            t.Equal("0°", Astro.LatitudeLabel(0), "l'équateur");
            t.Equal("12,5° E", Astro.LongitudeLabel(12.5), "une longitude est");
            t.Equal("90° O", Astro.LongitudeLabel(-90), "une longitude ouest");
            t.Equal("170° O", Astro.LongitudeLabel(190), "au-delà de 180°, on repasse de l'autre côté");
            t.Equal("180°", Astro.LongitudeLabel(180), "l'antiméridien n'est ni à l'est ni à l'ouest");
            t.Equal("45° N · 90° E", Maps.GeoLabel(Math.PI / 4, Math.PI / 2), "une position : latitude, puis longitude");
            var map = new MapInfo { Width = 2000, Height = 1000, Levels = 3, WholeWorld = true };
            t.Equal("45° S · 90° O", Maps.GeoLabel(Maps.Latitude(map, 750), Maps.Longitude(map, 500)), "…lue d'un point de la carte");
        }

        /// <summary>Lot 13 : d'une saison à l'autre, les teintes se fondent.</summary>
        private static void SeasonBlend(Harness t)
        {
            var world = new World { AxialTilt = 23.44 };
            world.Seasons.Add(new Season { Name = "Printemps", StartDay = 1 });
            world.Seasons.Add(new Season { Name = "Été", StartDay = 101 });
            world.Seasons.Add(new Season { Name = "Automne", StartDay = 201 });
            world.Seasons.Add(new Season { Name = "Hiver", StartDay = 301 });
            int a, b;
            double share;
            t.Check(Maps.SeasonMix(world, 151, 400, false, out a, out b, out share) && a == 1 && share < 1e-9, "au cœur d'une saison, elle est seule");
            Maps.SeasonMix(world, 176, 400, false, out a, out b, out share);
            t.Check(a == 1 && b == 2 && Math.Abs(share - 0.25) < 1e-9, "aux trois quarts de l'été, l'automne monte : un quart");
            Maps.SeasonMix(world, 200.999, 400, false, out a, out b, out share);
            t.Check(a == 1 && b == 2 && Math.Abs(share - 0.5) < 1e-3, "au dernier jour, moitié-moitié");
            Maps.SeasonMix(world, 201, 400, false, out a, out b, out share);
            t.Check(a == 2 && b == 1 && Math.Abs(share - 0.5) < 1e-9, "…et le lendemain aussi, vu de l'automne : aucune saute de teinte au passage");
            Maps.SeasonMix(world, 126, 400, false, out a, out b, out share);
            t.Check(a == 1 && b == 0 && Math.Abs(share - 0.25) < 1e-9, "au premier quart de l'été, le printemps s'efface encore");
            Maps.SeasonMix(world, 390, 400, false, out a, out b, out share);
            t.Check(a == 3 && b == 0 && share > 0.38 && share < 0.42, "la dernière saison de l'année glisse vers la première");
            Maps.SeasonMix(world, 151, 400, true, out a, out b, out share);
            t.Check(a == 3 && share < 1e-9, "au sud, la saison opposée — le cœur de l'hiver quand le nord est au cœur de l'été");
            var single = new World { AxialTilt = 20 };
            single.Seasons.Add(new Season { Name = "Toujours", StartDay = 1 });
            t.Check(Maps.SeasonMix(single, 50, 400, false, out a, out b, out share) && a == 0 && b == 0 && share == 0, "une seule saison : pas de fondu");
            t.Check(!Maps.SeasonMix(new World(), 50, 400, false, out a, out b, out share) && a == -1 && !Maps.SeasonMix(null, 1, 400, false, out a, out b, out share), "pas de saisons, pas de teinte");
        }

        /// <summary>Lot 13 : les réglages de chaque carte, gardés par l'application.</summary>
        private static void MapMemory(Harness t)
        {
            var states = Marabook.Settings.AppSettings.MapStates;
            var order = Marabook.Settings.AppSettings.MapStateOrder;
            Marabook.Settings.AppSettings.MapStates = new Dictionary<string, Dictionary<string, object>>();
            Marabook.Settings.AppSettings.MapStateOrder = new List<string>();
            try
            {
                for (var i = 0; i < Marabook.Settings.AppSettings.MapStatesKept + 5; i++)
                    Marabook.Settings.AppSettings.RememberMapState("carte-" + i, new Dictionary<string, object> { { "night", i % 2 == 0 } });
                t.Check(Marabook.Settings.AppSettings.MapStates.Count == Marabook.Settings.AppSettings.MapStatesKept && !Marabook.Settings.AppSettings.MapStates.ContainsKey("carte-4")
                    && Marabook.Settings.AppSettings.MapStates.ContainsKey("carte-5"), "l'application garde les réglages des dernières cartes visitées, les plus anciens s'effacent");
                Marabook.Settings.AppSettings.RememberMapState("carte-5", new Dictionary<string, object> { { "night", true } });
                Marabook.Settings.AppSettings.RememberMapState("neuve", new Dictionary<string, object>());
                t.Check(Marabook.Settings.AppSettings.MapStates.ContainsKey("carte-5") && !Marabook.Settings.AppSettings.MapStates.ContainsKey("carte-6") && (bool)Marabook.Settings.AppSettings.MapStates["carte-5"]["night"],
                    "une carte revisitée repasse en tête : ce n'est pas elle qu'on oublie");
                Marabook.Settings.AppSettings.RememberMapState(null, new Dictionary<string, object>());
                Marabook.Settings.AppSettings.RememberMapState("x", null);
                t.Check(!Marabook.Settings.AppSettings.MapStates.ContainsKey("x") && Marabook.Settings.AppSettings.MapStates.Count == Marabook.Settings.AppSettings.MapStatesKept, "ni carte sans identifiant, ni état vide");
            }
            finally
            {
                Marabook.Settings.AppSettings.MapStates = states;
                Marabook.Settings.AppSettings.MapStateOrder = order;
            }
        }

        private static bool SameTiles(MapTileSet a, Dictionary<string, byte[]> expected)
        {
            if (a.Count != expected.Count) return false;
            foreach (var pair in expected)
            {
                var bytes = a.Read(pair.Key);
                if (bytes == null || bytes.Length != pair.Value.Length) return false;
                for (var i = 0; i < bytes.Length; i += 97) if (bytes[i] != pair.Value[i]) return false;
            }
            return true;
        }

        /// <summary>Lot 12 (16/11) : une carte ouverte ne tient plus ses tuiles en mémoire.</summary>
        private static void LazyTiles(Harness t)
        {
            var project = Project.CreateNew();
            var root = project.Category(Project.KeyMaps);
            var item = new BinderItem { Kind = ItemKind.Map, Title = "Ehnmir", Parent = root, Map = SampleMap() };
            root.Children.Add(item);
            var expected = new Dictionary<string, byte[]>();
            foreach (var key in item.Map.Tiles.Keys) expected[key] = item.Map.Tiles[key];
            long total = 0;
            foreach (var bytes in expected.Values) total += bytes.Length;
            t.Check(item.Map.Tiles.MemoryCount == expected.Count && item.Map.Tiles.MemoryBytes == total && Maps.Bytes(item.Map) == total, "une carte tout juste importée tient ses tuiles en mémoire");
            InTemp(delegate(string dir)
            {
                var path = Path.Combine(dir, "paresse.plot");
                PlotFile.Save(project, path);
                t.Check(item.Map.Tiles.MemoryCount == 0 && item.Map.Tiles.MemoryBytes == 0 && item.Map.Tiles.Count == expected.Count && Maps.Bytes(item.Map) == total,
                    "au premier enregistrement, ses tuiles sont rendues : plus rien en mémoire, le même poids annoncé");
                t.Check(SameTiles(item.Map.Tiles, expected), "…et elles se relisent dans le .plot, octet pour octet");

                var reads = PlotTiles.Reads;
                var loaded = PlotFile.Load(path);
                var back = Maps.All(loaded)[0];
                t.Check(PlotTiles.Reads == reads && back.Map.Tiles.MemoryCount == 0 && back.Map.Tiles.Count == expected.Count && Maps.Bytes(back.Map) == total && !back.LoadDamaged,
                    "ouvrir un projet ne lit AUCUNE tuile : leur adresse, leur poids, rien d'autre");
                var one = back.Map.Tiles.Read("1/1_0");
                t.Check(PlotTiles.Reads == reads + 1 && one != null && one.Length == expected["1/1_0"].Length && back.Map.Tiles.MemoryCount == 0, "une tuile demandée est lue seule, et n'est pas gardée");
                t.Check(back.Map.Tiles.Read("9/9_9") == null && !back.Map.Tiles.Contains("9/9_9"), "une tuile qui n'existe pas rend null");

                // Enregistrer sans avoir rien lu : les tuiles passent de l'ancien fichier au neuf par copie de bloc.
                reads = PlotTiles.Reads;
                var first = PlotFile.Write(PlotFile.Prepare(loaded), path, false);
                t.Check(first.Copied >= expected.Count && first.LostTiles == 0 && back.Map.Tiles.MemoryCount == 0 && SameTiles(back.Map.Tiles, expected),
                    "enregistrer recopie les tuiles par bloc, sans les lire ni les recompresser (" + first.Copied + " recopiées)");

                // Enregistrer sous : les tuiles viennent du fichier d'origine, et la carte est reliée au nouveau.
                var other = Path.Combine(dir, "ailleurs.plot");
                var moved = PlotFile.Write(PlotFile.Prepare(loaded), other, false);
                File.Delete(path);
                t.Check(moved.LostTiles == 0 && moved.Copied >= expected.Count && SameTiles(back.Map.Tiles, expected), "« Enregistrer sous » : les tuiles suivent dans le nouveau fichier, et l'ancien peut disparaître");
                t.Check(SameTiles(Maps.All(PlotFile.Load(other))[0].Map.Tiles, expected), "…le nouveau fichier se rouvre entier");

                // Une copie de la carte, puis l'original retiré : la copie reprend les tuiles sous son propre nom.
                var maps = loaded.Category(Project.KeyMaps);
                var twin = back.Duplicate(false);
                twin.Parent = maps;
                maps.Children.Add(twin);
                maps.Children.Remove(back);
                var afterTwin = PlotFile.Write(PlotFile.Prepare(loaded), other, false);
                t.Check(afterTwin.LostTiles == 0 && SameTiles(twin.Map.Tiles, expected), "une carte dupliquée dont l'original est supprimé garde toutes ses tuiles");
                using (var archive = ZipFile.OpenRead(other))
                    t.Check(archive.GetEntry("cartes/" + twin.Id + "/0/0_0.jpg") != null && archive.GetEntry("cartes/" + back.Id + "/0/0_0.jpg") == null, "…rangées sous son identifiant, celles de l'original parties du fichier");

                // L'image remplacée : l'ancienne table n'est plus dans le projet, mais l'historique la tient —
                // l'enregistrement la ramène en mémoire avant de réécrire le fichier sans elle.
                var old = twin.Map;
                var fresh = SampleMap();
                fresh.Tiles = new MapTileSet();
                fresh.Tiles["0/0_0"] = new byte[] { 1, 2, 3, 4, 5 };
                fresh.Levels = 1;
                fresh.Width = fresh.Height = 100;
                twin.Map = fresh;
                var replaced = PlotFile.Write(PlotFile.Prepare(loaded), other, false);
                t.Check(replaced.LostTiles == 0 && old.Tiles.MemoryCount == expected.Count && SameTiles(old.Tiles, expected),
                    "une image remplacée puis enregistrée : l'ancienne, que Ctrl+Z peut rendre, est revenue entière en mémoire");
                twin.Map = old;
                var undone = PlotFile.Write(PlotFile.Prepare(loaded), other, false);
                t.Check(undone.LostTiles == 0 && old.Tiles.MemoryCount == 0 && SameTiles(Maps.All(PlotFile.Load(other))[0].Map.Tiles, expected), "…annulée et enregistrée, elle retourne dormir dans le .plot");

                // Un autre projet encore ouvert sur le même fichier (ici : le premier, « project ») n'est pas trahi non plus :
                // ses tuiles, que l'enregistrement de « loaded » allait laisser sans fichier, sont revenues en mémoire.
                t.Check(SameTiles(item.Map.Tiles, expected), "une carte encore tenue ailleurs (historique, autre projet) ne perd jamais ses tuiles quand son fichier est réécrit");

                // Le fichier d'origine a disparu pendant la session : on enregistre quand même, et on le DIT.
                var gone = Path.Combine(dir, "perdu.plot");
                File.Copy(other, gone);
                var orphaned = PlotFile.Load(gone);
                File.Delete(gone);
                var lost = PlotFile.Write(PlotFile.Prepare(orphaned), Path.Combine(dir, "apres.plot"), false);
                t.Check(lost.LostTiles == expected.Count && File.Exists(Path.Combine(dir, "apres.plot")), "le .plot d'origine disparu : le projet s'enregistre, et les tuiles perdues sont comptées (" + lost.LostTiles + ")");
                var warnings = new List<string>();
                var holed = Maps.All(PlotFile.Load(Path.Combine(dir, "apres.plot"), warnings))[0];
                t.Check(holed.LoadDamaged && holed.Map.Zones.Count == 1 && warnings.Count > 0, "…la carte rouverte est marquée trouée, ses zones intactes");

                // Un fichier remplacé par un autre contenu sous le même nom : la tuile n'est pas confondue.
                var swapPath = Path.Combine(dir, "troc.plot");
                File.Copy(other, swapPath);
                var swapped = Maps.All(PlotFile.Load(swapPath))[0];
                var stranger = Project.CreateNew();
                var strangerPath = Path.Combine(dir, "etranger.plot");
                PlotFile.Save(stranger, strangerPath);
                File.Copy(strangerPath, swapPath, true); // un autre programme (une synchronisation) pose un autre fichier sous le même nom
                t.Check(swapped.Map.Tiles.Read("0/0_0") == null, "un .plot remplacé par un autre : la tuile ne se relit pas (plutôt rien qu'une image fausse)");
            });

            // Le lecteur d'une entrée : stockée ou compressée, vérifiée par son CRC.
            InTemp(delegate(string dir)
            {
                var zip = Path.Combine(dir, "essai.zip");
                var payload = new byte[5000];
                new Random(7).NextBytes(payload);
                var text = System.Text.Encoding.UTF8.GetBytes(new string('m', 3000));
                using (var stream = new FileStream(zip, FileMode.Create))
                {
                    var writer = new PlotZip.Writer(stream);
                    writer.Add("brut.bin", payload, null, CompressionLevel.NoCompression, Digest.Crc32(payload, 0, payload.Length), payload.Length);
                    writer.Add("texte.txt", text, null, CompressionLevel.Optimal, Digest.Crc32(text, 0, text.Length), text.Length);
                    writer.Finish();
                }
                using (var stream = new FileStream(zip, FileMode.Open, FileAccess.Read))
                {
                    var entries = PlotZip.ReadDirectory(stream);
                    var raw = PlotZip.ReadEntry(stream, entries["brut.bin"]);
                    var packed = PlotZip.ReadEntry(stream, entries["texte.txt"]);
                    t.Check(raw != null && raw.Length == 5000 && raw[4999] == payload[4999] && packed != null && packed.Length == 3000 && packed[0] == (byte)'m', "une entrée se lit seule, stockée ou compressée");
                    entries["brut.bin"].Crc ^= 1;
                    t.Check(PlotZip.ReadEntry(stream, entries["brut.bin"]) == null, "…et un CRC qui ne tombe pas juste la refuse");
                }
                t.Check(PlotTiles.Read(zip, "brut.bin", Digest.Crc32(payload, 0, payload.Length), 5000) != null && PlotTiles.Read(zip, "brut.bin", 12345, 5000) == null
                    && PlotTiles.Read(zip, "absent", 0, 0) == null && PlotTiles.Read(Path.Combine(dir, "nulle-part.zip"), "brut.bin", 0, 0) == null,
                    "le lecteur de tuiles : la bonne entrée, sinon null (CRC différent, entrée absente, fichier absent)");
            });
        }

        /// <summary>Lot 12 : la construction du planisphère (étirée, ou proportions gardées et pôles placés), les saisons.</summary>
        private static void Layout(Harness t)
        {
            var map = new MapInfo { Width = 6000, Height = 4200, Levels = 5, WholeWorld = true };
            t.Check(!Maps.Fitted(map) && Maps.LongitudeSpan(map) == 360 && Math.Abs(Maps.Latitude(map, 0) - Math.PI / 2) < 1e-9 && Math.Abs(Maps.Longitude(map, 6000) - Math.PI) < 1e-9,
                "étirée : l'image couvre 360° de large, d'un pôle à l'autre, quelles que soient ses proportions");
            t.Check(Math.Abs(Maps.XOf(map, Maps.Longitude(map, 1234)) - 1234) < 1e-6 && Math.Abs(Maps.YOf(map, Maps.Latitude(map, 987)) - 987) < 1e-6, "…et l'on revient de l'angle au pixel");
            map.Layout = Maps.LayoutFitted;
            t.Check(Maps.Fitted(map) && Math.Abs(Maps.LongitudeSpan(map) - 180.0 * 6000 / 4200) < 1e-6, "proportions gardées, pôles aux bords : la largeur couvre 180° × largeur ÷ hauteur (257°)");
            t.Check(Math.Abs(Maps.Latitude(map, 0) - Math.PI / 2) < 1e-9 && Math.Abs(Maps.Latitude(map, 4200) + Math.PI / 2) < 1e-9 && Math.Abs(Maps.Latitude(map, 2100)) < 1e-9 && Math.Abs(Maps.Longitude(map, 3000)) < 1e-9,
                "…le haut est le pôle nord, le bas le pôle sud, le milieu l'équateur et le méridien du centre");
            var degree = Maps.RadiansPerPixel(map);
            t.Check(Math.Abs((Maps.Longitude(map, 3100) - Maps.Longitude(map, 3000)) - 100 * degree) < 1e-12 && Math.Abs((Maps.Latitude(map, 2000) - Maps.Latitude(map, 2100)) - 100 * degree) < 1e-12,
                "…un degré vaut autant de pixels en large qu'en haut : rien n'est déformé");
            // « Un tour complet » : 360° sur la largeur → les pôles à (0,5 ∓ largeur ÷ 4 ÷ hauteur).
            var half = 6000 / 4.0 / 4200;
            map.PoleNorth = 0.5 - half;
            map.PoleSouth = 0.5 + half;
            t.Check(Math.Abs(Maps.LongitudeSpan(map) - 360) < 1e-6 && Math.Abs(Maps.Latitude(map, 2100)) < 1e-9 && Maps.PoleNorthInside(map),
                "les pôles replacés dans l'image : un tour complet (360°), l'équateur au milieu");
            t.Check(Math.Abs(Maps.Latitude(map, 0) - Math.PI / 2) < 1e-9 && Math.Abs(Maps.Latitude(map, 4200) + Math.PI / 2) < 1e-9, "…au-delà d'un pôle, la latitude reste celle du pôle");
            t.Check(Math.Abs(Maps.XOf(map, Maps.Longitude(map, 1234)) - 1234) < 1e-6 && Math.Abs(Maps.YOf(map, Maps.Latitude(map, 1987)) - 1987) < 1e-6, "…aller-retour angle ↔ pixel");
            // Des pôles hors de l'image : elle ne monte que jusqu'à 60° nord.
            map.PoleNorth = -0.25;
            map.PoleSouth = 1.25;
            t.Check(Math.Abs(Maps.Latitude(map, 0) * 180 / Math.PI - 60) < 1e-6 && Math.Abs(Maps.Latitude(map, 4200) * 180 / Math.PI + 60) < 1e-6 && !Maps.PoleNorthInside(map),
                "des pôles hors de l'image (−25 % et 125 %) : elle va de 60° nord à 60° sud");
            // Les distances suivent la construction : un quart de tour le long de l'équateur.
            map.PoleNorth = 0;
            map.PoleSouth = 1;
            var quarter = Math.PI / 2 / Maps.RadiansPerPixel(map);
            t.Check(Math.Abs(Maps.CentralAngle(map, new Pos(3000 - quarter / 2, 2100), new Pos(3000 + quarter / 2, 2100)) - Math.PI / 2) < 1e-9, "les distances suivent la construction choisie");

            // Le manifeste.
            map.PoleNorth = 0.1;
            map.PoleSouth = 0.9;
            var back = MapJson.Read(Json.Parse(Json.Write(MapJson.Build(map))));
            t.Check(back.Layout == Maps.LayoutFitted && Math.Abs(back.PoleNorth - 0.1) < 1e-9 && Math.Abs(back.PoleSouth - 0.9) < 1e-9 && map.Clone().Layout == Maps.LayoutFitted && map.Clone().PoleSouth == 0.9,
                "la construction s'enregistre, se relit et suit les copies");
            var plain = MapJson.Build(new MapInfo { Width = 10, Height = 10, Levels = 1 });
            t.Check(!plain.ContainsKey("layout") && !plain.ContainsKey("poleNorth") && MapJson.Read(Json.Parse(Json.Write(plain))).Layout == Maps.LayoutStretch, "étirée : rien de plus dans le manifeste (un .plot du lot 11 se relit tel quel)");
            var broken = MapJson.Read(Json.Parse("{\"width\":10,\"height\":10,\"levels\":1,\"layout\":\"fitted\",\"poleNorth\":0.8,\"poleSouth\":0.2}"));
            t.Check(broken.Layout == Maps.LayoutFitted && broken.PoleNorth == 0 && broken.PoleSouth == 1, "des pôles à l'envers dans le fichier : ramenés aux bords");

            // La présence d'une saison selon la latitude.
            var world = World.Earth();
            t.Check(Maps.SeasonPresence(world, 0) == 0 && Maps.SeasonPresence(world, Math.PI / 3) == 1 && Maps.SeasonPresence(world, -Math.PI / 3) == 1,
                "les saisons : absentes à l'équateur, entières au-delà des tropiques, au nord comme au sud");
            var mid = Maps.SeasonPresence(world, 11.72 * Math.PI / 180);
            t.Check(Math.Abs(mid - 0.5) < 0.01, "…à mi-chemin du tropique, à moitié marquées (" + mid.ToString("0.00") + ")");
            world.AxialTilt = 0;
            t.Check(Maps.SeasonPresence(world, 1) == 0 && Maps.SeasonPresence(null, 1) == 0, "sans inclinaison de l'axe (ou sans monde), aucune saison nulle part");
            world.AxialTilt = 23.44;
            world.Seasons.Clear();
            t.Check(Maps.SeasonPresence(world, 1) == 0, "un monde sans saisons n'en montre pas");
        }

        /// <summary>Lot 12 : les deux onglets propres à la carte, tout en bas du rail.</summary>
        private static void Rail(Harness t)
        {
            var offered = Marabook.Settings.RightPanels.Offered(ItemKind.Map);
            var names = new List<string>();
            foreach (var panel in offered) names.Add(Marabook.Settings.RightPanels.Name(panel));
            t.Equal("inspector,search,pinned,notebook,map-sky,map-shapes,map-navigation", string.Join(",", names.ToArray()), "une carte ouverte : Général, les outils de partout, puis ses trois onglets");
            var all = Marabook.Settings.RightPanels.Offered(ItemKind.Map, false, true, false, true);
            t.Check(all[all.Length - 3] == Marabook.Settings.RightPanel.MapSky && all[all.Length - 2] == Marabook.Settings.RightPanel.MapShapes && all[all.Length - 1] == Marabook.Settings.RightPanel.MapNavigation
                && Array.IndexOf(all, Marabook.Settings.RightPanel.Calculator) == all.Length - 4 && Marabook.Settings.RightPanels.BelongsToMap(Marabook.Settings.RightPanel.MapNavigation)
                && Marabook.Settings.RightPanels.Parse("map-navigation") == Marabook.Settings.RightPanel.MapNavigation && Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.MapNavigation, false, true, ItemKind.Map)
                && !Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.MapNavigation, false, true, ItemKind.Text),
                "…toujours les derniers, sous le Lexique, le Carnet et le Calculateur");
            t.Check(ReferenceEquals(offered, Marabook.Settings.RightPanels.Offered(ItemKind.Map)), "des instances fixes (le rail compare par référence)");
            t.Check(Marabook.Settings.RightPanels.BelongsToMap(Marabook.Settings.RightPanel.MapSky) && Marabook.Settings.RightPanels.BelongsToMap(Marabook.Settings.RightPanel.MapShapes)
                && !Marabook.Settings.RightPanels.BelongsToMap(Marabook.Settings.RightPanel.Notebook) && !Marabook.Settings.RightPanels.DescribesCurrent(Marabook.Settings.RightPanel.MapSky), "le rail sait les séparer des autres par un filet");
            t.Check(Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.MapSky, false, true, ItemKind.Map) && Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.MapShapes, false, true, ItemKind.Map)
                && Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.Inspector, false, true, ItemKind.Map), "disponibles pour une carte, avec son Général");
            foreach (var kind in new ItemKind?[] { ItemKind.Text, ItemKind.Sheet, ItemKind.Timeline, ItemKind.Category, ItemKind.Book, null })
                t.Check(!Marabook.Settings.RightPanels.Available(Marabook.Settings.RightPanel.MapSky, false, true, kind) && !Marabook.Settings.RightPanels.Offers(kind, Marabook.Settings.RightPanel.MapShapes),
                    "…et pour rien d'autre (" + (kind == null ? "aucun élément" : kind.ToString()) + ")");
            t.Check(Marabook.Settings.RightPanels.Parse("map-sky") == Marabook.Settings.RightPanel.MapSky && Marabook.Settings.RightPanels.Name(Marabook.Settings.RightPanel.MapShapes) == "map-shapes"
                && Marabook.Settings.RightPanels.Parse(Marabook.Settings.RightPanels.Name(Marabook.Settings.RightPanel.MapShapes)) == Marabook.Settings.RightPanel.MapShapes, "leurs noms persistés");
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
                foreach (var key in item.Map.Tiles.Keys)
                {
                    byte[] bytes;
                    var original = item.Map.Tiles[key];
                    if (original == null || !map.Tiles.TryGetValue(key, out bytes) || bytes.Length != original.Length || bytes[bytes.Length - 1] != original[original.Length - 1] || bytes[0] != original[0]) same = false;
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
