using System;
using System.Collections.Generic;
using System.IO;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C68 — l'Astrolabe au-delà des frises (1.2.0, 14/11) : les mondes
    /// — saisons et dérive du calendrier, lunes (phases, pleines lunes,
    /// conjonctions), taille (rayon, horizon, durée du jour) —, leur vie dans
    /// le .plot et l'historique, et le Calculateur de dates au rail.</summary>
    public static class AstroTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C68 — mondes : saisons, lunes, taille ; calculateur au rail");
            Seasons(t);
            Moons(t);
            Size(t);
            Lifecycle(t);
            Rail(t);
        }

        private static TimePoint At(TimeSystem system, string notation)
        {
            TimePoint point;
            if (!system.TryParse(notation, out point)) throw new InvalidOperationException("date illisible : " + notation);
            return point;
        }

        /// <summary>Les espaces fines et insécables des nombres français, ramenées à l'espace ordinaire.</summary>
        private static string Plain(string text) { return (text ?? "").Replace(' ', ' ').Replace(' ', ' '); }

        private static bool Near(double a, double b, double tolerance) { return Math.Abs(a - b) <= tolerance; }

        private static void Seasons(Harness t)
        {
            var calendar = TimeSystem.Gregorian();
            var world = World.Earth();
            world.SolarYear = 0; // d'abord sans dérive : les saisons aux mêmes dates chaque année
            Func<string, string> season = delegate(string date) { var found = Astro.SeasonAt(world, calendar, At(calendar, date)); return found == null ? null : found.Name; };
            t.Equal("Hiver", season("15 janvier 2026"), "mi-janvier : l'hiver (la dernière saison de l'année se poursuit au début de la suivante)");
            t.Equal("Printemps", season("20 mars 2026"), "20 mars (jour 79) : le printemps commence");
            t.Equal("Hiver", season("19 mars 2026"), "…la veille, encore l'hiver");
            t.Equal("Été", season("14 juillet 2026"), "14 juillet : l'été");
            t.Equal("Automne", season("1 novembre 2026"), "1er novembre : l'automne");
            t.Equal("Hiver", season("25 décembre 2026"), "25 décembre : l'hiver");
            t.Equal("Été", season("juillet 2026"), "une date au mois compte pour son premier jour");
            t.Check(Astro.SeasonAt(new World(), calendar, At(calendar, "14 juillet 2026")) == null && Astro.SeasonIndexAt(null, calendar, At(calendar, "14 juillet 2026")) == -1, "sans saisons, ou sans monde : rien");
            t.Check(Astro.DriftLabel(world, calendar) == null && Astro.DriftPerYear(world, calendar) == 0, "sans durée réelle de l'année : pas de dérive");

            // La dérive : le calendrier julien (une bissextile tous les quatre ans) contre l'année solaire.
            var julian = TimeSystem.Gregorian();
            julian.Leaps.Clear();
            julian.Leaps.Add(new LeapRule { Every = 4, Month = 1, Days = 1 });
            world.SolarYear = 365.2422;
            t.Check(Near(Astro.DriftPerYear(world, julian), 0.0078, 1e-4), "le calendrier julien dérive de 0,0078 jour par an (" + Astro.DriftPerYear(world, julian) + ")");
            var label = Astro.DriftLabel(world, julian) ?? "";
            t.Check(label.Contains("plus longue") && label.Contains("plus tôt") && label.Contains("un jour tous les 128 ans"), "dit en clair : un jour tous les 128 ans, les saisons arrivent plus tôt (" + label + ")");
            t.Check(Near(Astro.DriftOver(world, julian, 7500), 58.5, 0.5), "sur une ère de 7 500 ans : environ 58 jours de glissement (" + Astro.DriftOver(world, julian, 7500) + ")");
            // Avec la dérive, la saison d'une même date finit par changer.
            var early = Astro.SeasonAt(world, julian, At(julian, "1 juin 300"));
            var late = Astro.SeasonAt(world, julian, At(julian, "1 juin 9000"));
            t.Check(early != null && late != null && early.Name != late.Name, "…et le 1er juin n'est plus dans la même saison neuf mille ans plus tard (" + (early == null ? "-" : early.Name) + " → " + (late == null ? "-" : late.Name) + ")");
            var gregorianLabel = Astro.DriftLabel(world, calendar) ?? "";
            t.Check(Plain(gregorianLabel).Contains("un jour tous les 3 333 ans"), "le grégorien : un jour tous les trois mille ans et quelques (" + gregorianLabel + ")");
            var short_ = new World { SolarYear = 366 };
            t.Check((Astro.DriftLabel(short_, calendar) ?? "").Contains("plus courte") && (Astro.DriftLabel(short_, calendar) ?? "").Contains("plus tard"), "une année de calendrier trop courte : les saisons arrivent plus tard");

            var four = Astro.FourSeasons(360);
            t.Check(four.Count == 4 && four[0].StartDay == 1 && four[1].StartDay == 91 && four[2].StartDay == 181 && four[3].StartDay == 271 && four[0].Name == "Printemps", "quatre saisons égales sur 360 jours : jours 1, 91, 181, 271");
            t.Check(Astro.ColorOf(four[0], 0) == Astro.SeasonColors[0] && Astro.ColorOf(new Season { Color = "#112233" }, 2) == "#112233", "une saison a la teinte de son rang, sauf couleur choisie");
        }

        private static void Moons(Harness t)
        {
            var moon = new Moon { Name = "Sélène", Period = 30, FullAt = 100 };
            t.Check(Near(Astro.Phase(moon, 100), 0, 1e-9) && Near(Astro.Phase(moon, 115), 0.5, 1e-9) && Near(Astro.Phase(moon, 130), 0, 1e-9) && Near(Astro.Phase(moon, 85), 0.5, 1e-9),
                "la phase : 0 à la pleine lune, 0,5 à la nouvelle — avant la référence aussi");
            t.Check(Near(Astro.Illumination(0), 1, 1e-9) && Near(Astro.Illumination(0.5), 0, 1e-9) && Near(Astro.Illumination(0.25), 0.5, 1e-9), "le disque : plein, éteint, à moitié au quartier");
            t.Equal("pleine lune", Astro.PhaseName(0.01), "près de 0 : pleine lune");
            t.Equal("pleine lune", Astro.PhaseName(0.98), "…de l'autre côté aussi");
            t.Equal("dernier quartier", Astro.PhaseName(0.25), "0,25 : dernier quartier");
            t.Equal("nouvelle lune", Astro.PhaseName(0.5), "0,5 : nouvelle lune");
            t.Equal("premier quartier", Astro.PhaseName(0.75), "0,75 : premier quartier");
            t.Equal("gibbeuse croissante", Astro.PhaseName(0.88), "avant la pleine lune : gibbeuse croissante");
            // Les phases principales tombent un jour précis.
            t.Check(Astro.MainPhaseOn(moon, 100) == Astro.Full && Astro.MainPhaseOn(moon, 107) == Astro.LastQuarter && Astro.MainPhaseOn(moon, 115) == Astro.New && Astro.MainPhaseOn(moon, 122) == Astro.FirstQuarter && Astro.MainPhaseOn(moon, 130) == Astro.Full,
                "pleine le jour 100, dernier quartier le 107 (7,5 jours), nouvelle le 115, premier quartier le 122, pleine le 130");
            t.Check(Astro.MainPhaseOn(moon, 101) == -1 && Astro.MainPhaseOn(moon, 114) == -1 && Astro.MainPhaseOn(moon, 99) == -1, "les autres jours : aucune phase principale");
            var count = 0;
            for (long day = 100; day < 100 + 300; day++) if (Astro.MainPhaseOn(moon, day) == Astro.Full) count++;
            t.Equal(10, count, "dix pleines lunes en trois cents jours");
            t.Check(Astro.NextFull(moon, 100) == 100 && Astro.NextFull(moon, 101) == 130 && Astro.NextFull(moon, 0) == 10 && Astro.NextFull(moon, -55) == -50, "la prochaine pleine lune, ce jour compris — avant la référence aussi");
            // Notre lune : 29,53 jours — les pleines lunes ne tombent pas à jour fixe.
            var real = new Moon { Period = 29.530588, FullAt = 0 };
            t.Check(Astro.NextFull(real, 1) == 29 && Astro.NextFull(real, 30) == 59 && Astro.NextFull(real, 60) == 88, "une période à virgule : pleines lunes aux jours 29, 59, 88");

            // Deux lunes de 30 et 45 jours se retrouvent tous les 90 jours.
            var world = new World();
            world.Moons.Add(moon);
            world.Moons.Add(new Moon { Name = "Nyx", Period = 45, FullAt = 100 });
            t.Check(Astro.NextConjunction(world, 100) == 100 && Astro.NextConjunction(world, 101) == 190 && Astro.NextConjunction(world, 191) == 280, "deux lunes de 30 et 45 jours : pleines ensemble tous les 90 jours");
            world.Moons.Add(new Moon { Name = "Troisième", Period = 20, FullAt = 100 });
            t.Check(Astro.NextConjunction(world, 101) == 280, "trois lunes (30, 45, 20) : tous les 180 jours");
            var lonely = new World();
            lonely.Moons.Add(moon);
            t.Check(Astro.NextConjunction(lonely, 0) == null && Astro.NextConjunction(null, 0) == null, "une seule lune : pas de conjonction");
            var never = new World();
            never.Moons.Add(new Moon { Period = 30, FullAt = 0 });
            never.Moons.Add(new Moon { Period = 30, FullAt = 15 });
            t.Check(Astro.NextConjunction(never, 0, 2000) == null, "deux lunes de même période, en opposition : jamais pleines ensemble");
        }

        private static void Size(Harness t)
        {
            var earth = World.Earth();
            var calendar = TimeSystem.Gregorian();
            t.Check(Near(Astro.Radius(earth), 6378.1, 0.2), "la Terre : 6 378 km de rayon (" + Astro.Radius(earth) + ")");
            t.Check(Near(Astro.Surface(earth) / 1e6, 511.2, 0.5), "…511 millions de km² (" + Astro.Surface(earth) / 1e6 + ")");
            t.Check(Near(Astro.Horizon(earth, 1.7), 4.66, 0.02) && Near(Astro.Horizon(earth, 30), 19.56, 0.05), "l'horizon : 4,7 km pour un marcheur, 19,6 km à 30 m de haut");
            t.Check(Near(Astro.HourBand(earth, calendar), 1669.8, 0.2), "une heure de soleil à l'équateur : 1 670 km");
            t.Check(Near(Astro.TravelDays(earth, 30), 1335.8, 0.2), "le tour du monde à pied : 1 336 jours");
            t.Check(Near(Astro.DayLength(earth, calendar, 0, true), 12, 1e-6) && Near(Astro.DayLength(earth, calendar, 0, false), 12, 1e-6), "à l'équateur, douze heures toute l'année");
            t.Check(Near(Astro.DayLength(earth, calendar, 45, true), 15.43, 0.05) && Near(Astro.DayLength(earth, calendar, 45, false), 8.57, 0.05), "à 45° : 15 h 26 l'été, 8 h 34 l'hiver ("
                + Astro.DayLength(earth, calendar, 45, true) + ")");
            t.Check(Near(Astro.DayLength(earth, calendar, 80, true), 24, 1e-6) && Near(Astro.DayLength(earth, calendar, 80, false), 0, 1e-6), "au-delà du cercle polaire : le jour polaire, la nuit polaire");
            var flat = new World { Circumference = 20000 };
            t.Check(Near(Astro.DayLength(flat, calendar, 60, true), 12, 1e-6), "sans inclinaison renseignée : le jour vaut la moitié de la journée, partout");
            // Les heures du monde : un jour de vingt heures.
            var tides = new TimeSystem { HoursPerDay = 20, MinutesPerHour = 50 };
            tides.Months.Add(new TimeMonth { Name = "Mois", Days = 360 });
            t.Check(Near(Astro.DayLength(earth, tides, 0, true), 10, 1e-6) && Astro.Hours(tides, 12.5) == "12 h 25", "la durée du jour se compte dans les heures du calendrier (20 h par jour, 50 min par heure)");
            var facts = Astro.SizeFacts(earth, calendar).ConvertAll(Plain);
            t.Check(facts.Count == 6 && facts[0].Contains("6 378 km") && facts[4].Contains("23,4°") && facts[4].Contains("66,6°") && facts[5].Contains("45° :"), "les faits de la taille se disent en six lignes (" + facts.Count + ")");
            t.Check(facts[5].Contains("0° : 12 h 00 → 12 h 00") && facts[5].Contains("30° :") && facts[5].Contains("60° :"), "…les latitudes rondes gardent leurs zéros (" + facts[5] + ")");
            t.Check(Plain(Astro.Summary(new World { Circumference = 20000 })) == "20 000 km de tour", "…les nombres ronds aussi (20 000 km)");
            t.Check(Astro.SizeFacts(new World(), calendar).Count == 0, "sans circonférence : rien à dire");
            t.Equal("40 075 km de tour · 4 saisons · 1 lune", Plain(Astro.Summary(earth)), "un monde en une phrase");
        }

        private static void Lifecycle(Harness t)
        {
            var project = Project.CreateNew();
            var timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            var tides = Timelines.SystemOf(project, timeline);
            t.Check(Astro.WorldOf(project, tides) == null && Astro.DayLabel(project, tides, At(tides, "1 Aubemer E3.1")) == null, "un système sans monde : pas de ciel");

            var before = WorldsEditAction.Snapshot(project);
            var links = WorldsEditAction.Links(project);
            var world = new World { Name = "Littoral", Circumference = 30000, AxialTilt = 18, SolarYear = 360.5 };
            world.Seasons.AddRange(Astro.FourSeasons(360));
            var start = tides.DayNumber(At(tides, "1 Aubemer E3.1"));
            world.Moons.Add(new Moon { Name = "Sélène", Period = 30, FullAt = start });
            world.Moons.Add(new Moon { Name = "Nyx", Period = 45, FullAt = start, Color = "#C08497" });
            project.Worlds.Add(world);
            tides.WorldId = world.Id;
            var history = new HistoryManager();
            history.Push(new WorldsEditAction(project, before, links));
            t.Check(ReferenceEquals(Astro.WorldOf(project, tides), world) && Astro.SystemsOf(project, world).Count == 1, "le calendrier vit dans son monde");
            world.SolarYear = 0;
            t.Equal("Printemps · Sélène : pleine lune · Nyx : pleine lune", Astro.DayLabel(project, tides, At(tides, "1 Aubemer E3.1")), "le ciel d'un jour : la saison, et chaque lune");
            t.Equal("Printemps · Sélène : nouvelle lune · Nyx : dernier croissant", Astro.DayLabel(project, tides, At(tides, "16 Aubemer E3.1")), "quinze jours plus tard");
            t.Equal("Été", Astro.DayLabel(project, tides, At(tides, "Sel E3.1")), "une date au mois : la saison seule");
            t.Check(Astro.DayLabel(project, tides, At(tides, "E3.1")) == null, "une date à l'année : rien");
            world.SolarYear = 360.5;

            var dir = Path.Combine(Path.GetTempPath(), "marabook-c68-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "monde.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var back = loaded.Worlds.Count == 1 ? loaded.Worlds[0] : null;
                t.Check(back != null && back.Id == world.Id && back.Name == "Littoral" && back.Circumference == 30000 && back.AxialTilt == 18 && back.SolarYear == 360.5, "le .plot rend le monde : nom, taille, inclinaison, année solaire");
                t.Check(back != null && back.Seasons.Count == 4 && back.Seasons[2].Name == "Automne" && back.Seasons[2].StartDay == 181 && back.Seasons[2].Id == world.Seasons[2].Id, "…ses saisons");
                t.Check(back != null && back.Moons.Count == 2 && back.Moons[1].Name == "Nyx" && back.Moons[1].Period == 45 && back.Moons[1].FullAt == start && back.Moons[1].Color == "#C08497", "…ses lunes");
                var system = loaded.FindTimeSystem(tides.Id);
                t.Check(system != null && system.WorldId == world.Id && ReferenceEquals(Astro.WorldOf(loaded, system), back) && loaded.FindTimeSystem("sample-world").WorldId == null, "…et le lien du calendrier à son monde");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

            t.Check(tides.Clone().WorldId == world.Id && world.Clone().Moons[1].FullAt == start && !ReferenceEquals(world.Clone().Seasons[0], world.Seasons[0]), "les clones gardent le lien et recopient saisons et lunes");
            history.Undo();
            t.Check(project.Worlds.Count == 0 && tides.WorldId == null, "Ctrl+Z : le monde s'en va, le calendrier est délié");
            history.Redo();
            t.Check(project.Worlds.Count == 1 && project.Worlds[0].Name == "Littoral" && tides.WorldId == world.Id, "Ctrl+Y : il revient, lié");
            var plain = Project.CreateNew();
            t.Check(plain.Worlds.Count == 0 && TimelineJson.BuildWorlds(plain.Worlds).Count == 0 && TimelineJson.ReadWorlds(null).Count == 0, "un projet sans monde : rien d'écrit, rien de lu");
        }

        private static void Rail(Harness t)
        {
            t.Equal("inspector,search,pinned,notebook", Join(RightPanels.Offered(ItemKind.Timeline)), "sans demande : pas d'onglet Calculateur");
            t.Equal("inspector,search,pinned,notebook,calculator", Join(RightPanels.Offered(ItemKind.Timeline, false, false, false, true)), "demandé : le Calculateur suit le Carnet");
            t.Equal("inspector,correction,search,versions,pinned,lexicon,notebook,calculator,style-report", Join(RightPanels.Offered(ItemKind.Text, false, true, true, true)), "avec tout le monde : Lexique, Carnet, Calculateur, Bilan");
            t.Check(ReferenceEquals(RightPanels.Offered(ItemKind.Sheet, false, true, false, true), RightPanels.Offered(ItemKind.Sheet, false, true, false, true)), "des instances fixes (le rail compare par référence)");
            t.Check(!RightPanels.Available(RightPanel.Calculator, false, true, ItemKind.Timeline), "Calculateur ni épinglé ni demandé : indisponible");
            t.Check(RightPanels.Available(RightPanel.Calculator, false, true, ItemKind.Timeline, false, false, false, false, true) && RightPanels.Available(RightPanel.Calculator, false, true, null, false, false, false, false, true),
                "demandé ou épinglé : disponible, élément courant ou non");
            t.Check(!RightPanels.Available(RightPanel.Calculator, true, true, ItemKind.Timeline, false, false, false, false, true) && !RightPanels.Available(RightPanel.Calculator, false, false, null, false, false, false, false, true),
                "colonne masquée ou pas de projet : indisponible");
            t.Check(RightPanels.Parse("calculator") == RightPanel.Calculator && RightPanels.Name(RightPanel.Calculator) == "calculator" && !RightPanels.DescribesCurrent(RightPanel.Calculator), "son nom persisté ; c'est un outil");
        }

        private static string Join(RightPanel[] panels)
        {
            var names = new List<string>();
            foreach (var panel in panels) names.Add(RightPanels.Name(panel));
            return string.Join(",", names.ToArray());
        }
    }
}
