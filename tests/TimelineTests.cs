using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C63 — les frises (1.2.0) : le calendrier (mois, bissextiles
    /// personnalisées, semaine), les segments (cumulés ou qui repartent de
    /// zéro), la notation lue et écrite, les années négatives, les durées, les
    /// statistiques du rail, les liens de date, les âges de l'Époque, et
    /// l'aller-retour du .plot v46.</summary>
    public static class TimelineTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C63 — frises");
            Gregorian(t);
            Leaps(t);
            Segments(t);
            Notation(t);
            Negative(t);
            Durations(t);
            Stats(t);
            TimeLinkMarkup(t);
            Epoch(t);
            Rename(t);
            RoundTrip(t);
        }

        private static TimePoint Parse(Harness t, TimeSystem system, string text)
        {
            TimePoint point;
            t.Check(system.TryParse(text, out point), "« " + text + " » se lit");
            return point ?? new TimePoint();
        }

        private static bool Reads(TimeSystem system, string text)
        {
            TimePoint point;
            return system.TryParse(text, out point);
        }

        private static void Gregorian(Harness t)
        {
            var world = TimeSystem.Gregorian();
            t.Equal(365, world.DaysInYear(2024), "notre monde : 2025 (t = 2024) compte 365 jours");
            t.Equal(366, world.DaysInYear(2023), "…2024 est bissextile");
            t.Equal(365, world.DaysInYear(1899), "…1900 ne l'est pas (sauf 100)");
            t.Equal(366, world.DaysInYear(1999), "…2000 l'est (à moins que 400)");
            var bastille = Parse(t, world, "14 juillet 1789");
            t.Check(bastille.Year == 1788 && bastille.Month == 7 && bastille.Day == 14, "14 juillet 1789 : année absolue 1788, mois 7, jour 14");
            t.Equal("14 juillet 1789", world.Format(bastille), "…et se réécrit pareil");
            t.Equal("mardi", world.WeekDayOf(bastille), "…c'était un mardi");
            t.Equal("jeudi", world.WeekDayOf(Parse(t, world, "12 novembre 2026")), "12 novembre 2026 : un jeudi");
            t.Equal("juillet 1789", world.Format(Parse(t, world, "juillet 1789")), "une date au mois : « juillet 1789 »");
            t.Equal("1789", world.Format(Parse(t, world, "1789")), "une année seule : « 1789 »");
            t.Check(!Reads(world, "30 février 2024") && Reads(world, "29 février 2024") && !Reads(world, "29 février 2023"), "le 29 février n'existe que les années bissextiles");
            t.Check(!Reads(world, "0") && !Reads(world, "bonjour") && !Reads(world, ""), "ni an zéro, ni mots, ni vide");
            var withTime = Parse(t, world, "14 juillet 1789 14h30");
            t.Check(withTime.Hour == 14 && withTime.Minute == 30 && world.Format(withTime) == "14 juillet 1789 14h30", "l'heure se lit et s'écrit");
            t.Check(!Reads(world, "14 juillet 1789 25h00"), "…mais pas 25 heures");
            // 365 jours entre deux 1er janvier d'années ordinaires, 366 à cheval sur une bissextile.
            t.Equal(366L, world.DayNumber(new TimePoint(2024, 1, 1)) - world.DayNumber(new TimePoint(2023, 1, 1)), "le compte des jours traverse l'année bissextile");
        }

        private static void Leaps(Harness t)
        {
            var tides = TimelineSamples.TidesSystem();
            t.Equal(360, tides.BaseDays, "Marées : dix mois de 36 jours");
            // Numéro d'année = t + 1 : bissextile tous les 5, sauf les multiples de 50.
            t.Equal(365, tides.DaysInYear(4), "l'an 5 est bissextile (+5 jours)");
            t.Equal(360, tides.DaysInYear(3), "l'an 4 ne l'est pas");
            t.Equal(360, tides.DaysInYear(49), "l'an 50 ne l'est pas (sauf 50)");
            t.Equal(41, tides.DaysInMonth(4, 10), "…les cinq jours vont à Longue-Nuit");
            t.Equal(36, tides.DaysInMonth(4, 9), "…pas à Noroît");
            // La formule fermée des jours écoulés égale la somme année par année.
            long sum = 0;
            var ok = true;
            for (long year = 0; year < 400; year++)
            {
                if (tides.DaysBeforeYear(year) != sum) { ok = false; break; }
                sum += tides.DaysInYear(year);
            }
            t.Check(ok, "les jours écoulés (formule fermée) = la somme des années, sur 400 ans");
            long back = 0;
            ok = true;
            for (long year = -1; year > -300; year--)
            {
                back -= tides.DaysInYear(year);
                if (tides.DaysBeforeYear(year) != back) { ok = false; break; }
            }
            t.Check(ok, "…et de même avant l'origine, sur 300 ans");
            var world = TimeSystem.Gregorian();
            sum = 0;
            ok = true;
            for (long year = 0; year < 2100; year++)
            {
                if (world.DaysBeforeYear(year) != sum) { ok = false; break; }
                sum += world.DaysInYear(year);
            }
            t.Check(ok, "…et pour le grégorien, sur 2 100 ans");
            t.Equal("jusant", tides.WeekDayOf(new TimePoint(0, 1, 3)), "une semaine de six jours : le 3e jour est « jusant »");
            t.Equal("lunemer", tides.WeekDayOf(new TimePoint(0, 1, 7)), "…et le 7e recommence la semaine");
        }

        private static void Segments(Harness t)
        {
            var tides = TimelineSamples.TidesSystem();
            t.Equal(0L, tides.SegmentStart(0), "la première ère commence à l'origine");
            t.Equal(800L, tides.SegmentStart(1), "la deuxième après 800 ans");
            t.Equal(2000L, tides.SegmentStart(2), "la troisième après 2 000 ans");
            t.Equal(0, tides.SegmentIndexOf(799), "l'année 799 est de la première ère");
            t.Equal(1, tides.SegmentIndexOf(800), "…la 800 de la deuxième");
            t.Equal(2, tides.SegmentIndexOf(999999), "…tout ce qui suit la dernière borne, de la troisième (ouverte)");
            // Chaque segment repart de zéro.
            t.Equal("E1.800", tides.FormatYear(799), "repart de zéro : la dernière année de l'ère 1 est E1.800");
            t.Equal("E2.1", tides.FormatYear(800), "…suivie de E2.1");
            t.Equal("E3.30", tides.FormatYear(2029), "…et E3.30 est l'année absolue 2029");
            t.Equal(2029L, Parse(t, tides, "E3.30").Year, "E3.30 se relit en 2029");
            t.Check(!Reads(tides, "E1.801"), "E1.801 n'existe pas (l'ère 1 dure 800 ans)");
            t.Check(!Reads(tides, "E4.1"), "ni E4.1 (trois ères)");
            // Années cumulatives entre segments.
            tides.Cumulative = true;
            t.Equal("E1.800", tides.FormatYear(799), "cumulatif : E1.800");
            t.Equal("E2.801", tides.FormatYear(800), "…suivie de E2.801");
            t.Equal(2029L, Parse(t, tides, "E3.2030").Year, "…E3.2030 se relit en 2029");
            // L'an zéro.
            tides.Cumulative = false;
            tides.YearZero = true;
            t.Equal("E2.0", tides.FormatYear(800), "avec l'an zéro : la deuxième ère commence à E2.0");
            t.Equal(800L, Parse(t, tides, "E2.0").Year, "…et E2.0 se relit");
        }

        private static void Notation(Harness t)
        {
            var tides = TimelineSamples.TidesSystem();
            var birth = Parse(t, tides, "12 Sel E3.8");
            t.Check(birth.Year == 2007 && birth.Month == 4 && birth.Day == 12, "« 12 Sel E3.8 » : 2007, mois 4, jour 12");
            t.Equal("12 Sel E3.8", tides.Format(birth), "…se réécrit pareil");
            t.Check(Reads(tides, "12 sel e3.8") && Reads(tides, "  12  Sel   E3.8 "), "la casse et les espaces ne comptent pas");
            t.Equal("Sel E3.8", tides.Format(Parse(t, tides, "Sel E3.8")), "au mois : « Sel E3.8 »");
            t.Check(!Reads(tides, "37 Sel E3.8"), "le 37 Sel n'existe pas (36 jours)");
            t.Check(Reads(tides, "41 Longue-Nuit E1.5") && !Reads(tides, "41 Longue-Nuit E1.4"), "le 41 Longue-Nuit n'existe que les années bissextiles");
            // D'autres notations : le nom du segment, son abréviation, les numéros de mois.
            tides.YearPattern = "an {a} de l'{n}";
            t.Equal("an 30 de l'Ère des Grandes Marées", tides.FormatYear(2029), "notation par le nom du segment");
            t.Equal(2029L, Parse(t, tides, "an 30 de l'Ère des Grandes Marées").Year, "…relue");
            tides.YearPattern = "{c}-{a}";
            tides.Segments[0].Code = "Br";
            tides.Segments[1].Code = "Ph";
            tides.Segments[2].Code = "GM";
            t.Equal("GM-30", tides.FormatYear(2029), "notation par l'abréviation du segment");
            t.Equal(2029L, Parse(t, tides, "GM-30").Year, "…relue");
            tides.DatePattern = "{j}/{m}/{année}";
            t.Equal("12/4/GM-8", tides.Format(new TimePoint(2007, 4, 12)), "date à numéros : 12/4/GM-8");
            t.Equal("4/GM-8", tides.Format(new TimePoint(2007, 4, 0)), "…au mois : 4/GM-8");
            t.Check(Parse(t, tides, "12/4/GM-8").Day == 12 && Parse(t, tides, "4/GM-8").Month == 4, "…relues toutes deux");
            // Ce qui manque à une notation.
            var plain = TimelineSamples.TidesSystem();
            plain.YearPattern = "an {a}";
            t.Check(plain.NotationProblem() != null, "segments qui repartent de zéro : une notation sans le segment est refusée");
            plain.Cumulative = true;
            t.Check(plain.NotationProblem() == null, "…acceptée quand les années se cumulent");
            plain.YearPattern = "E{s}";
            t.Check(plain.NotationProblem() != null, "une notation sans {a} est refusée");
        }

        private static void Negative(Harness t)
        {
            var world = TimeSystem.Gregorian();
            t.Equal("44 av. J.-C.", world.FormatYear(-44), "notre monde : l'an −44 s'écrit « 44 av. J.-C. »");
            t.Equal(-44L, Parse(t, world, "44 av. J.-C.").Year, "…et se relit");
            t.Equal("15 mars 44 av. J.-C.", world.Format(Parse(t, world, "15 mars 44 av. J.-C.")), "…avec le jour et le mois");
            t.Equal("1", world.FormatYear(0), "l'an 1 suit l'an −1 (pas d'an zéro)");
            var tides = TimelineSamples.TidesSystem();
            t.Equal("E1.-120", tides.FormatYear(-120), "sans notation dédiée : E1.-120");
            t.Equal(-120L, Parse(t, tides, "E1.-120").Year, "…relue");
            t.Check(tides.AxisStart(new TimePoint(-120)) < tides.AxisStart(new TimePoint(0)), "les années négatives précèdent l'origine sur l'axe");
            tides.NegativePattern = "{a} avant les Brumes";
            t.Equal("120 avant les Brumes", tides.FormatYear(-120), "avec une notation des années négatives");
            t.Equal(-120L, Parse(t, tides, "120 avant les Brumes").Year, "…relue");
        }

        private static void Durations(Harness t)
        {
            var tides = TimelineSamples.TidesSystem();
            t.Equal("1 an", tides.DurationLabel(1), "1 an");
            t.Equal("29 ans", tides.DurationLabel(29), "29 ans");
            t.Check(tides.DurationLabel(1200).Replace(' ', ' ').Replace(' ', ' ') == "1 200 ans", "1 200 ans (" + tides.DurationLabel(1200) + ")");
            t.Equal("3 ans et 5 mois", tides.DurationLabel(3.5), "3 ans et 5 mois (dix mois par an)");
            t.Equal("12 jours", tides.DurationLabel(12.0 / 360), "12 jours");
            t.Equal("3 mois", tides.DurationLabel(108.0 / 360), "3 mois");
            // Un événement sur la durée, du jour au jour.
            var entry = new TimelineEvent { Start = Parse(t, tides, "3 Brisants E3.30"), End = Parse(t, tides, "20 Noroît E3.30") };
            var years = Timelines.EndOf(tides, entry) - Timelines.StartOf(tides, entry);
            t.Equal(162, (int)Math.Round(years * tides.DaysInYear(2029)), "du 3 Brisants au 20 Noroît inclus : 162 jours");
            t.Check(Timelines.WhenLabel(tides, entry).StartsWith("3 Brisants E3.30 → 20 Noroît E3.30 ("), "…dit en clair (" + Timelines.WhenLabel(tides, entry) + ")");
            // Une durée d'années entières va du début de la première à la fin de la dernière.
            var war = new TimelineEvent { Start = Parse(t, tides, "E1.212"), End = Parse(t, tides, "E1.240") };
            t.Equal("29 ans", tides.DurationLabel(Timelines.EndOf(tides, war) - Timelines.StartOf(tides, war)), "E1.212 → E1.240 : 29 ans");
            // L'âge.
            t.Equal(21L, tides.AgeAt(Parse(t, tides, "12 Sel E3.8"), Parse(t, tides, "3 Brisants E3.29")), "née le 12 Sel E3.8 : 21 ans au 3 Brisants E3.29");
            t.Equal(20L, tides.AgeAt(Parse(t, tides, "12 Sel E3.8"), Parse(t, tides, "11 Sel E3.29")), "…20 ans la veille de son anniversaire");
            t.Equal(22L, tides.AgeAt(Parse(t, tides, "12 Sel E3.8"), Parse(t, tides, "E3.30")), "…22 ans en E3.30 (à l'année)");
            // Le point de l'axe redonne la date.
            var point = tides.FromAxis(tides.AxisMid(new TimePoint(2007, 4, 12)), 2);
            t.Check(point.Year == 2007 && point.Month == 4 && point.Day == 12, "un point de l'axe redonne sa date au jour");
        }

        private static Project Sample(out BinderItem timeline)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            var keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira Varenh", TemplateId = template.Id };
            var marabout = new BinderItem { Kind = ItemKind.Sheet, Title = "Le marabout" }; // sans modèle : ses dates en informations libres
            sheets.Children.Add(keira);
            sheets.Children.Add(marabout);
            project.RelinkParents();
            timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            return project;
        }

        private static void Stats(Harness t)
        {
            BinderItem timeline;
            var project = Sample(out timeline);
            var system = Timelines.SystemOf(project, timeline);
            t.Equal(TimelineSamples.SystemName, system.Name, "la frise d'exemple est posée sur le Calendrier des Marées");
            var stats = Timelines.Stats(system, timeline.Timeline);
            t.Equal(14, stats.Events, "quatorze événements");
            t.Equal(4, stats.Spans, "…dont quatre sur la durée");
            t.Equal(3, stats.Segments.Count, "trois ères");
            t.Equal(800L, stats.Segments[0].Length, "l'ère des Brumes : 800 ans");
            t.Equal(1200L, stats.Segments[1].Length, "l'ère des Phares : 1 200 ans");
            t.Check(stats.Segments[2].Open && stats.Segments[2].Length == 34, "l'ère des Grandes Marées, ouverte, court jusqu'au dernier événement : 34 ans (" + stats.Segments[2].Length + ")");
            t.Equal(4, stats.Segments[0].Events, "quatre événements dans la première ère (dont celui d'avant l'origine)");
            t.Equal(5, stats.Segments[1].Events, "cinq dans la deuxième");
            t.Equal(5, stats.Segments[2].Events, "cinq dans la troisième");
            // Le total : les trois ères (2 034 ans) et les 120 ans d'avant l'origine.
            t.Equal(2154L, stats.TotalYears, "durée totale de la temporalité : 2 154 ans");
            t.Equal(2, Timelines.All(project).Count, "deux frises dans le projet");
            t.Equal(1, Timelines.UsesOf(project, system), "une seule sur ce système");
            var keira = project.FindByTitle("Keira Varenh");
            t.Equal(1, Timelines.EventsFor(project, keira.Id).Count, "une brique assignée à la fiche de Keira");
            t.Check(TimelineLinks.ItemsOf(project, TimelineLinks.Sheet).Count == 2 && TimelineLinks.ItemsOf(project, TimelineLinks.Plan).Count == 0,
                "l'assignation : les éléments d'UNE sorte (deux fiches, aucun plan)");
            // Une frise sans segments : du premier au dernier événement.
            var writing = Timelines.FindByTitle(project, TimelineSamples.WritingTitle);
            var world = Timelines.SystemOf(project, writing);
            var writingStats = Timelines.Stats(world, writing.Timeline);
            t.Check(writingStats.Segments.Count == 0 && writingStats.Events == 3 && writingStats.TotalYears == 1, "la frise du calendrier de notre monde : sans segment, trois événements, une année");
        }

        private static void TimeLinkMarkup(Harness t)
        {
            BinderItem timeline;
            var project = Sample(out timeline);
            var markup = TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.30", null);
            t.Equal("[[date:Chronique du Littoral@E3.30|E3.30]]", markup, "le lien de date écrit toujours son texte");
            t.Equal("[[date:Chronique du Littoral@E3.30|cette année-là]]", TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.30", "cette année-là"), "…ou les mots choisis");
            t.Equal(markup, Links.Markup(TimeLinks.Target(TimelineSamples.TimelineTitle, "E3.30"), ""), "Links.Markup garde la cible cachée pour un lien de date");
            var text = "En " + markup + ", la mer monta.";
            var links = Links.Find(text);
            t.Check(links.Count == 1 && TimeLinks.IsTimeLink(links[0].Target) && links[0].Text == "E3.30", "il se lit comme un [[lien]] ordinaire, cible « date: »");
            t.Equal("En E3.30, la mer monta.", Links.Strip(text), "à l'export, seule la date reste");
            var spans = Links.Spans(text);
            t.Check(spans.Count == 3 && spans[0].IsTime && spans[1].IsTime && !spans[1].IsMark, "ses plages portent la marque « temps » (une autre teinte)");
            t.Check(!Links.Spans("[[Keira Varenh]]")[0].IsTime, "…pas celles d'un lien de fiche");
            TimeSystem system;
            TimePoint point;
            var resolved = TimeLinks.Resolve(project, links[0].Target, out system, out point);
            t.Check(resolved == timeline && point != null && point.Year == 2029, "le lien se résout : la frise, et l'année 2029");
            t.Check(TimeLinks.Resolve(project, "date:Frise inconnue@E3.30", out system, out point) == null, "une frise disparue ne se résout pas");
            // La notation sélectionnée avant Ctrl+! désigne sa frise.
            t.Check(TimeLinks.MatchNotation(project, "E5.902", null, out point) == null, "« E5.902 » n'est une date d'aucune frise (trois ères)");
            t.Check(TimeLinks.MatchNotation(project, "E2.905", null, out point) == timeline && point.Year == 1704, "« E2.905 » sélectionné : la frise du Littoral");
            var writing = Timelines.FindByTitle(project, TimelineSamples.WritingTitle);
            t.Check(TimeLinks.MatchNotation(project, "14 juillet 2026", null, out point) == writing, "« 14 juillet 2026 » : la frise du calendrier de notre monde");
            t.Check(TimeLinks.MatchNotation(project, "Keira", null, out point) == null, "un nom n'est pas une date");
        }

        private static void Epoch(Harness t)
        {
            BinderItem timeline;
            var project = Sample(out timeline);
            var system = Timelines.SystemOf(project, timeline);
            var keira = project.FindByTitle("Keira Varenh");
            var marabout = project.FindByTitle("Le marabout");
            TimeSystem found;
            BinderItem foundTimeline;
            t.Check(Epochs.Current(project, out found, out foundTimeline) == null, "l'époque d'exemple est réglée mais éteinte");
            t.Check(Epochs.Evaluate(project, keira).State == LifeState.Unknown, "…éteinte, aucune fiche n'a d'état");
            project.Epoch.Enabled = true;
            t.Check(Epochs.Current(project, out found, out foundTimeline) != null && foundTimeline == timeline, "allumée : E3.30 sur la frise du Littoral");
            var status = Epochs.Evaluate(project, keira);
            t.Check(status.State == LifeState.Alive && status.HasAge && status.Age == 22, "en E3.30, Keira a 22 ans (" + status.Label + ")");
            status = Epochs.Evaluate(project, marabout);
            t.Check(status.State == LifeState.Alive && status.Age == 89, "…le marabout 89 (dates en informations libres)");
            project.Epoch.Notation = "E3.12";
            t.Check(Epochs.Evaluate(project, keira).Age == 4, "en E3.12, Keira a 4 ans");
            project.Epoch.Notation = "E3.2";
            status = Epochs.Evaluate(project, keira);
            t.Check(status.State == LifeState.Unborn && status.Label == "Naît dans 6 ans", "en E3.2, elle n'est pas née (" + status.Label + ")");
            project.Epoch.Notation = "E3.40";
            status = Epochs.Evaluate(project, marabout);
            t.Check(status.State == LifeState.Dead && status.Age == 93 && status.Label.StartsWith("Mort·e à 93 ans"), "en E3.40, le marabout est mort, à 93 ans (" + status.Label + ")");
            t.Check(Epochs.Evaluate(project, keira).State == LifeState.Alive, "…Keira vit toujours");
            // L'année même de la mort, à l'année près : encore en vie.
            project.Epoch.Notation = "E3.34";
            t.Check(Epochs.Evaluate(project, marabout).State == LifeState.Alive, "l'année de sa mort, il compte encore parmi les vivants");
            project.Epoch.Notation = "3 Longue-Nuit E3.34";
            t.Check(Epochs.Evaluate(project, marabout).State == LifeState.Dead, "…le lendemain de sa mort, non");
            // Une époque d'un autre calendrier ne dit rien de ces fiches.
            project.Epoch.TimelineId = Timelines.FindByTitle(project, TimelineSamples.WritingTitle).Id;
            project.Epoch.Notation = "2026";
            t.Check(Epochs.Evaluate(project, keira).State == LifeState.Unknown, "une époque d'un autre calendrier : pas d'âge (on ne mêle pas deux temps)");
            project.Epoch.Notation = "n'importe quoi";
            t.Check(Epochs.Current(project, out found, out foundTimeline) == null, "une date d'époque illisible : pas d'époque");
            t.Check(system != null, "");
        }

        private static void Rename(Harness t)
        {
            BinderItem timeline;
            var project = Sample(out timeline);
            var keira = project.FindByTitle("Keira Varenh");
            var plan = LinkHygiene.Retarget(project, TimelineSamples.TimelineTitle, "Annales");
            t.Check(plan.Items.Contains(keira), "renommer la frise recible les liens de date des fiches");
        }

        private static void RoundTrip(Harness t)
        {
            BinderItem timeline;
            var project = Sample(out timeline);
            project.Epoch.Enabled = true;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c63-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "frises.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Equal(2, loaded.TimeSystems.Count, "le .plot rend les deux systèmes temporels");
                var system = loaded.FindTimeSystem("sample-tides");
                t.Check(system != null && system.Segmented && !system.Cumulative && system.Segments.Count == 3 && system.Segments[1].Length == 1200
                    && system.Segments[2].Length == 0 && system.YearPattern == "E{s}.{a}", "…segments, durées, notation");
                t.Check(system != null && system.Months.Count == 10 && system.Months[3].Name == "Sel" && system.Months[3].Days == 36
                    && system.Leaps.Count == 1 && system.Leaps[0].Every == 5 && system.Leaps[0].Except == 50 && system.Leaps[0].Month == 9 && system.Leaps[0].Days == 5,
                    "…mois et bissextiles");
                t.Check(system != null && system.WeekDays.Count == 6 && system.HoursPerDay == 20 && system.MinutesPerHour == 50, "…semaine et horloge");
                var world = loaded.FindTimeSystem("sample-world");
                t.Check(world != null && world.NegativePattern == "{a} av. J.-C." && world.Leaps[0].Unless == 400 && world.HoursPerDay == 24, "…et le calendrier de notre monde");
                var root = loaded.Category(Project.KeyTimelines);
                t.Check(root != null && root.Children.Count == 2 && root.Children[0].Kind == ItemKind.Timeline, "la racine Frises et ses deux frises");
                var again = loaded.FindById("sample-timeline");
                t.Check(again != null && again.Timeline != null && again.Timeline.SystemId == "sample-tides" && again.Timeline.Types.Count == 5
                    && again.Timeline.Events.Count == 14, "la frise : son système, ses types, ses quatorze événements");
                TimelineEvent novel = null, birth = null;
                foreach (var entry in again.Timeline.Events)
                {
                    if (entry.Title == "Le marabout et la mer") novel = entry;
                    if (entry.Title == "Naissance de Keira") birth = entry;
                }
                t.Check(novel != null && novel.IsSpan && novel.Start.Year == 2029 && novel.Start.Month == 5 && novel.Start.Day == 3
                    && novel.End.Month == 9 && novel.End.Day == 20 && novel.TypeId == "type-4", "un événement sur la durée : début, fin, type");
                t.Check(birth != null && !birth.IsSpan && birth.LinkKind == TimelineLinks.Sheet && loaded.FindById(birth.LinkId) != null
                    && loaded.FindById(birth.LinkId).Title == "Keira Varenh" && birth.Description.Length > 0, "un événement ponctuel : son assignation à une fiche, sa description");
                t.Check(loaded.Epoch != null && loaded.Epoch.Enabled && loaded.Epoch.TimelineId == "sample-timeline" && loaded.Epoch.Notation == "E3.30", "l'époque : frise, date, active");
                t.Check(Epochs.Evaluate(loaded, loaded.FindByTitle("Keira Varenh")).Age == 22, "…et les âges se recalculent sur le projet relu");
                // Un projet sans frise n'écrit rien de plus, et se relit avec sa racine.
                var plain = Project.CreateNew();
                var plainPath = Path.Combine(dir, "nu.plot");
                PlotFile.Save(plain, plainPath);
                var plainLoaded = PlotFile.Load(plainPath);
                t.Check(plainLoaded.TimeSystems.Count == 0 && plainLoaded.Category(Project.KeyTimelines) != null && !plainLoaded.Epoch.Enabled, "un projet sans frise : aucun système, la racine Frises, pas d'époque");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
