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
            t.Suite("C69 — mondes : notre monde en brouillon, calculateur de circonférence, soleils, calendrier de référence");
            OurWorld(t);
            Suns(t);
            t.Suite("C70 — durée des saisons, glissement expliqué, date d'une naissance changée depuis la frise");
            SeasonDetails(t);
            LifeDates(t);
        }

        /// <summary>16/11 : ce que dure chaque saison, la moyenne équivalente, et le rapport de glissement.</summary>
        private static void SeasonDetails(Harness t)
        {
            var earth = World.Earth();
            var lengths = Astro.SeasonLengths(earth, 365);
            t.Check(lengths.Length == 4 && lengths[0] == 93 && lengths[1] == 94 && lengths[2] == 89 && lengths[3] == 89, "nos saisons sur 365 jours : 93, 94, 89 et 89 jours (" + string.Join(", ", Array.ConvertAll(lengths, delegate(double d) { return d.ToString(); })) + ")");
            var custom = new World();
            custom.Seasons.Add(new Season { Name = "Hiver", StartDay = 264 });   // dans le désordre : la durée suit le jour, pas le rang
            custom.Seasons.Add(new Season { Name = "Printemps", StartDay = 1 });
            custom.Seasons.Add(new Season { Name = "Été", StartDay = 92 });
            custom.Seasons.Add(new Season { Name = "Automne", StartDay = 178 });
            var mine = Astro.SeasonLengths(custom, 360);
            t.Check(mine[1] == 91 && mine[2] == 86 && mine[3] == 86 && mine[0] == 97, "des saisons aux jours 1, 92, 178, 264 d'une année de 360 : 91, 86, 86 et 97 jours — la dernière court jusqu'au retour de la première");
            var alone = new World();
            alone.Seasons.Add(new Season { Name = "Toujours", StartDay = 40 });
            t.Check(Astro.SeasonLengths(alone, 360)[0] == 360 && Astro.SeasonLengths(new World(), 360).Length == 0, "une seule saison dure toute l'année ; sans saison, rien");
            t.Equal("100 jours  ·  25 % de l'année", Plain(Astro.SeasonShare(100, 400)), "le détail d'une saison : sa durée et sa part de l'année");
            t.Check(Plain(Astro.SeasonShare(91, 360)).Contains("25,3 %"), "…à une décimale (" + Plain(Astro.SeasonShare(91, 360)) + ")");
            t.Check(Plain(Astro.EvenSeasons(4, 400)).StartsWith("Moyenne équivalente : 100 jours par saison") && Astro.EvenSeasons(0, 400) == "", "quatre saisons sur une année de 400 jours : « Moyenne équivalente : 100 jours » (" + Plain(Astro.EvenSeasons(4, 400)) + ")");

            // Le glissement : l'année de la Terre posée sur un calendrier de 360 jours (le projet de Rémi).
            var calendar = new TimeSystem { Name = "Douze mois de trente" };
            for (var i = 1; i <= 12; i++) calendar.Months.Add(new TimeMonth { Name = "Mois " + i, Days = 30 });
            var report = Astro.DriftReport(earth, calendar).ConvertAll(Plain);
            t.Check(report.Count == 3 && report[0].Contains("dure 360 jours") && report[0].Contains("365,2422") && report[0].Contains("5,24 jours plus tard"), "la cause du glissement : 360 jours contre 365,2422, 5,24 jours plus tard chaque année (" + (report.Count > 0 ? report[0] : "-") + ")");
            t.Check(report.Count == 3 && report[1].Contains("10 ans : 52 jours") && report[1].Contains("100 ans : 524 jours (1,5 tour de calendrier)"), "…l'écart après 10, 100 et 1 000 ans (" + (report.Count > 1 ? report[1] : "-") + ")");
            t.Check(report.Count == 3 && report[2].Contains("ajouter 5 jours à ses mois") && report[2].Contains("ajouter un jour tous les 4 ans"), "…et ce que le calendrier devrait gagner pour suivre (" + (report.Count > 2 ? report[2] : "-") + ")");
            earth.SolarYear = 0;
            t.Check(Astro.DriftReport(earth, calendar).Count == 0 && Astro.DriftReport(null, calendar).Count == 0, "sans durée réelle de l'année : aucun glissement, aucun rapport");
            earth.SolarYear = 360;
            t.Check(Astro.DriftReport(earth, calendar).Count == 0, "une année réelle égale à celle du calendrier : rien non plus");
            // Sans glissement, le printemps posé au jour 1 commence bien le premier jour de chaque année.
            custom.SolarYear = 0;
            t.Equal("Printemps", Astro.SeasonAt(custom, calendar, new TimePoint(4321, 1, 1)).Name, "sans glissement, la saison du jour 1 commence le premier jour de l'an, quelle que soit l'année");
            custom.SolarYear = 365.2422;
            t.Check(Astro.SeasonAt(custom, calendar, new TimePoint(4321, 1, 1)).Name != "Printemps", "…avec l'année de la Terre sur ce calendrier, elle a glissé (le bug rapporté le 16/11)");
        }

        /// <summary>16/11 : la date d'une naissance née d'une fiche se change depuis la frise — dans le champ de la fiche.</summary>
        private static void LifeDates(Harness t)
        {
            var project = Project.CreateNew();
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", TemplateId = project.CharacterTemplate().Id };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            var timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            var tides = Timelines.SystemOf(project, timeline);
            foreach (var field in project.TemplateOf(sheet).Fields)
                if (field.Name == SheetDefaults.FieldBirthDate) sheet.FieldValues[field.Id] = TimeLinks.Markup(TimelineSamples.TimelineTitle, "12 Sel E3.8", null);
            var birth = Timelines.LifeEventsOf(project, timeline, tides).Events.Find(delegate(TimelineEvent e) { return e.Title.Contains("Keira") && e.TypeId == TimelineInfo.BirthTypeId; });
            t.Check(birth != null && tides.Format(birth.Start) == "12 Sel E3.8" && Timelines.LifeFieldOf(birth) == SheetDefaults.FieldBirthDate, "la naissance de Keira naît du champ « Date de naissance » de sa fiche (12 Sel E3.8)");
            if (birth == null) return;
            var keira = project.FindById(birth.LinkId);
            t.Check(Timelines.SetLifeDate(project, timeline, tides, keira, SheetDefaults.FieldBirthDate, At(tides, "14 Sel E3.8")), "changer sa date depuis la frise réécrit le champ de la fiche");
            var value = Epochs.FieldValue(project, keira, SheetDefaults.FieldBirthDate) ?? "";
            t.Equal(TimeLinks.Markup(TimelineSamples.TimelineTitle, "14 Sel E3.8", null), value, "…le lien de date du champ dit la nouvelle date");
            var moved = Timelines.LifeEventsOf(project, timeline, tides).Events.Find(delegate(TimelineEvent e) { return e.Id == birth.Id; });
            t.Check(moved != null && tides.Format(moved.Start) == "14 Sel E3.8", "…et la brique suit");
            t.Check(!Timelines.SetLifeDate(project, timeline, tides, keira, SheetDefaults.FieldDeathDate, At(tides, "1 Sel E3.90")), "une fiche sans date de décès liée : rien à réécrire");
            // Un lien ordinaire vers la frise (toléré depuis le 12/11) devient un lien de date ; un texte choisi par l'auteur est gardé.
            Epochs.ReplaceFieldValue(project, keira, SheetDefaults.FieldBirthDate, delegate(string old) { return "née le [[" + TimelineSamples.TimelineTitle + "|12 Sel E3.8]], à l'aube"; });
            t.Check(Timelines.SetLifeDate(project, timeline, tides, keira, SheetDefaults.FieldBirthDate, At(tides, "E3.9")), "un lien ordinaire vers la frise se réécrit aussi");
            t.Equal("née le " + TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.9", null) + ", à l'aube", Epochs.FieldValue(project, keira, SheetDefaults.FieldBirthDate), "…en lien de date, le reste du champ intact");
            Epochs.ReplaceFieldValue(project, keira, SheetDefaults.FieldBirthDate, delegate(string old) { return TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.9", "l'an du grand gel"); });
            Timelines.SetLifeDate(project, timeline, tides, keira, SheetDefaults.FieldBirthDate, At(tides, "E3.10"));
            t.Equal(TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.10", "l'an du grand gel"), Epochs.FieldValue(project, keira, SheetDefaults.FieldBirthDate), "un texte affiché choisi par l'auteur est gardé");
            t.Check(Timelines.LifeFieldOf(timeline.Timeline.Events[0]) == null && Timelines.LifeFieldOf(null) == null, "une brique ordinaire n'a pas de champ de fiche");
        }

        /// <summary>15/11 : un monde neuf part des données du nôtre ; le
        /// calculateur de circonférence ; les quatre saisons égales calées sur
        /// notre printemps.</summary>
        private static void OurWorld(Harness t)
        {
            var earth = World.Earth();
            t.Check(earth.Circumference == 40075 && earth.AxialTilt == 23.44 && earth.SolarYear == 365.2422, "notre monde : 40 075 km, 23,44°, 365,2422 jours");
            t.Check(earth.Seasons.Count == 4 && earth.Seasons[0].Name == "Printemps" && earth.Seasons[0].StartDay == 79 && earth.Seasons[1].StartDay == 172 && earth.Seasons[2].StartDay == 266 && earth.Seasons[3].Name == "Hiver" && earth.Seasons[3].StartDay == 355,
                "…ses saisons dans l'ordre de l'année : printemps le jour 79, été 172, automne 266, hiver 355");
            t.Check(earth.Suns.Count == 1 && earth.Suns[0].Name == "Soleil" && earth.Moons.Count == 1 && earth.Moons[0].Name == "Lune", "…un Soleil, une Lune");

            t.Check(Astro.ScaledCircumference(1) == 40075 && Astro.ScaledCircumference(1.2) == 48090 && Astro.ScaledCircumference(0.5) == 20038, "le calculateur : 1 × la Terre = 40 075 km, 1,2 × = 48 090 km, 0,5 × = 20 038 km (" + Astro.ScaledCircumference(0.5) + ")");
            t.Check(Astro.ScaledCircumference(0) == 0 && Astro.ScaledCircumference(-2) == 0 && Astro.ScaledCircumference(double.NaN) == 0, "…zéro, un négatif, un non-nombre : rien");

            t.Check(Astro.SpringDay(365) == 79 && Astro.SpringDay(360) == 78 && Astro.SpringDay(4) >= 1 && Astro.SpringDay(4) <= 4, "notre printemps rapporté à l'année : jour 79 sur 365, jour 78 sur 360");
            var ours = Astro.FourSeasons(365, Astro.SpringDay(365));
            t.Check(ours[0].Name == "Printemps" && ours[0].StartDay == 79 && ours[1].StartDay == 170 && ours[2].StartDay == 261 && ours[3].StartDay == 353,
                "quatre saisons égales calées sur notre printemps : jours 79, 170, 261, 353 (" + ours[1].StartDay + ", " + ours[2].StartDay + ", " + ours[3].StartDay + ")");
            var wrapped = Astro.FourSeasons(360, 300);
            t.Check(wrapped[0].StartDay == 300 && wrapped[1].StartDay == 30 && wrapped[2].StartDay == 120 && wrapped[3].StartDay == 210, "une première saison tardive : les suivantes repassent par le début de l'année (300, 30, 120, 210)");
            // Calées sur le printemps, la saison de janvier est la dernière de la liste.
            var calendar = TimeSystem.Gregorian();
            var world = new World();
            world.Seasons.AddRange(ours);
            t.Equal("Hiver", Astro.SeasonAt(world, calendar, At(calendar, "15 janvier 2026")).Name, "…et la mi-janvier reste en hiver");
        }

        /// <summary>15/11 : d'autres systèmes planétaires — le premier soleil
        /// fait le jour, ses compagnons partagent son ciel ou font le tour du leur.</summary>
        private static void Suns(Harness t)
        {
            var sun = new Sun { Name = "Brasier", Period = 400, HighAt = 100 };
            t.Check(Near(Astro.SunPhase(sun, 100), 0, 1e-9) && Near(Astro.SunPhase(sun, 300), 0.5, 1e-9) && Near(Astro.SunPhase(sun, -100), 0.5, 1e-9), "un compagnon : 0 quand il culmine à minuit, 0,5 auprès du premier soleil — avant la référence aussi");
            t.Check(Near(Astro.NightShare(0), 1, 1e-9) && Near(Astro.NightShare(0.5), 0, 1e-9) && Near(Astro.NightShare(0.25), 0.5, 1e-9) && Near(Astro.NightShare(0.75), 0.5, 1e-9), "la part de nuit éclairée : toute, aucune, la moitié");
            t.Equal("nuits claires", Astro.SunStateName(0), "à minuit : nuits claires");
            t.Equal("nuits claires", Astro.SunStateName(0.95), "…un peu avant aussi");
            t.Equal("soleil du soir", Astro.SunStateName(0.25), "un quart de tour plus tard : soleil du soir");
            t.Equal("de jour", Astro.SunStateName(0.5), "à mi-tour : de jour, auprès du premier");
            t.Equal("soleil du matin", Astro.SunStateName(0.75), "puis soleil du matin");
            t.Check(Astro.SunHighOn(sun, 100) && !Astro.SunHighOn(sun, 101) && !Astro.SunHighOn(sun, 99) && Astro.SunHighOn(sun, 500) && Astro.SunHighOn(sun, -300), "il culmine à minuit le jour 100, puis tous les 400 jours");
            t.Check(!Astro.SunHighOn(new Sun { HighAt = 100 }, 100) && !Astro.SunHighOn(null, 100), "un compagnon sans période ne culmine jamais seul");
            t.Check(Astro.NextHigh(sun, 100) == 100 && Astro.NextHigh(sun, 101) == 500 && Astro.NextHigh(sun, -500) == -300, "le prochain minuit clair, ce jour compris");

            t.Check(Astro.HighDay(sun, 100, -1) == -300 && Astro.HighDay(sun, 100, 1) == 500 && Astro.HighDay(sun, 500, 0) == 500, "le minuit clair d'avant et d'après celui du jour (-300, 500)");

            // 16/11 : la phase d'un jour, la fois d'avant et la fois d'après (les infobulles du calendrier).
            var moon = new Moon { Name = "Sélène", Period = 30, FullAt = 100 };
            t.Check(Astro.MainPhaseDay(moon, 100, 0) == 100 && Astro.MainPhaseDay(moon, 100, -1) == 70 && Astro.MainPhaseDay(moon, 100, 1) == 130, "pleine le jour 100 : la précédente le 70, la prochaine le 130");
            t.Check(Astro.MainPhaseDay(moon, 107, 0) == 107 && Astro.MainPhaseDay(moon, 107, -1) == 77 && Astro.MainPhaseDay(moon, 107, 1) == 137, "dernier quartier le 107 (à 7,5 jours) : le précédent le 77, le prochain le 137");
            var real = new Moon { Period = 29.530588, FullAt = 0 };
            t.Check(Astro.MainPhaseOn(real, 29) == Astro.Full && Astro.MainPhaseDay(real, 29, 1) == 59 && Astro.MainPhaseDay(real, 59, -1) == 29 && Astro.MainPhaseDay(real, 29, -1) == 0, "une période à virgule : pleines lunes aux jours 0, 29, 59");
            var calendar = TimeSystem.Gregorian();
            t.Equal("12/3/2026", TimeMath.ShortDate(calendar, At(calendar, "12 mars 2026")), "la notation abrégée d'une date : jour/mois/année");
            t.Check(TimeMath.ShortDate(calendar, At(calendar, "mars 2026")) == "3/2026" && TimeMath.ShortDate(calendar, At(calendar, "2026")) == "2026" && TimeMath.ShortDate(null, null) == "", "…au mois, à l'année, sans rien (" + TimeMath.ShortDate(calendar, At(calendar, "mars 2026")) + " ; " + TimeMath.ShortDate(calendar, At(calendar, "2026")) + ")");

            var project = Project.CreateNew();
            var timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            var tides = Timelines.SystemOf(project, timeline);
            t.Equal("1/1/E3.1", TimeMath.ShortDate(tides, At(tides, "1 Aubemer E3.1")), "…dans la notation du calendrier pour l'année (1/1/E3.1)");
            var start = tides.DayNumber(At(tides, "1 Aubemer E3.1"));
            var world = new World { Name = "Deux soleils", ReferenceSystemId = tides.Id };
            world.Suns.Add(new Sun { Name = "Hélios" });
            world.Suns.Add(new Sun { Name = "Jumeau" });
            world.Suns.Add(new Sun { Name = "Brasier", Period = 360, HighAt = start, Color = "#D9603A" });
            project.Worlds.Add(world);
            tides.WorldId = world.Id;
            t.Check(Astro.Wanderers(world).Count == 1 && Astro.Wanderers(world)[0].Name == "Brasier" && Astro.Wanderers(null).Count == 0, "seuls les compagnons à période font le tour du ciel");
            t.Equal("3 soleils", Astro.Summary(world), "un monde à plusieurs soleils le dit");
            t.Check(!Astro.Summary(World.Earth()).Contains("soleil"), "…un seul soleil va sans dire");
            t.Equal("Brasier : nuits claires", Astro.DayLabel(project, tides, At(tides, "1 Aubemer E3.1")), "le ciel d'un jour dit où en est le compagnon");
            t.Equal("Brasier : de jour", Astro.DayLabel(project, tides, TimeMath.FromDayNumber(tides, start + 180)), "…une demi-période plus tard, il accompagne le premier soleil");
            t.Check(Astro.ColorOf(world.Suns[2], 2) == "#D9603A" && Astro.ColorOf(world.Suns[0], 0) == Astro.SunColors[0], "un soleil a la teinte de son rang, sauf couleur choisie");

            var dir = Path.Combine(Path.GetTempPath(), "marabook-c69-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "soleils.plot");
                PlotFile.Save(project, path);
                var back = PlotFile.Load(path).Worlds[0];
                t.Check(back.Suns.Count == 3 && back.Suns[0].Name == "Hélios" && back.Suns[1].Period == 0 && back.Suns[2].Id == world.Suns[2].Id && back.Suns[2].Period == 360 && back.Suns[2].HighAt == start && back.Suns[2].Color == "#D9603A",
                    "le .plot rend les soleils : nom, période, minuit clair, couleur");
                t.Check(back.ReferenceSystemId == tides.Id, "…et le calendrier de référence du monde");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
            var copy = world.Clone();
            t.Check(copy.Suns.Count == 3 && copy.Suns[2].HighAt == start && !ReferenceEquals(copy.Suns[2], world.Suns[2]) && copy.ReferenceSystemId == tides.Id, "le clone recopie les soleils et la référence");
            t.Check(TimelineJson.ReadWorlds(TimelineJson.BuildWorlds(new List<World> { new World() }))[0].Suns.Count == 0, "un monde d'avant, sans soleils : rien d'écrit, rien de lu");
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
