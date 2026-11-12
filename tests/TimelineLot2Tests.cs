using System;
using System.Collections.Generic;
using System.IO;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C64 — les frises, lot 2 (1.2.0) : les naissances et les décès
    /// nés des fiches, l'accord de « mort / morte », l'âge d'époque de la vue
    /// wiki, le changement de temporalité (dates relues, briques en attente,
    /// liens de date réécrits) et son annulation.</summary>
    public static class TimelineLot2Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C64 — frises, lot 2");
            Life(t);
            Gender(t);
            AgeNote(t);
            Convert(t);
            EraLength(t);
            Pending(t);
            NotationRewrite(t);
            Undo(t);
        }

        /// <summary>Ce que la frise montre : ses briques datées et celles nées des fiches.</summary>
        internal static List<TimelineEvent> AllEvents(Project project, BinderItem timeline, TimeSystem system)
        {
            var events = new List<TimelineEvent>();
            foreach (var entry in timeline.Timeline.Events) if (!entry.IsPending) events.Add(entry);
            events.AddRange(Timelines.LifeEventsOf(project, timeline, system).Events);
            return events;
        }

        private static Project Sample(out BinderItem timeline, out BinderItem keira, out BinderItem marabout)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira Varenh", TemplateId = template.Id };
            marabout = new BinderItem { Kind = ItemKind.Sheet, Title = "Le marabout", TemplateId = template.Id };
            sheets.Children.Add(keira);
            sheets.Children.Add(marabout);
            project.RelinkParents();
            timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            return project;
        }

        private static void SetField(Project project, BinderItem sheet, string name, string value)
        {
            foreach (var field in project.TemplateOf(sheet).Fields)
                if (field.Name == name) { sheet.FieldValues[field.Id] = value; return; }
            throw new InvalidOperationException("champ introuvable : " + name);
        }

        private static string Field(Project project, BinderItem sheet, string name)
        {
            return Epochs.FieldValue(project, sheet, name);
        }

        private static void Life(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            var system = Timelines.SystemOf(project, timeline);
            var life = Timelines.LifeEventsOf(project, timeline, system);
            t.Equal(3, life.Events.Count, "trois briques nées des fiches : deux naissances, un décès");
            TimelineEvent birth = null, death = null;
            foreach (var entry in life.Events)
            {
                if (entry.LinkId == marabout.Id && entry.TypeId == TimelineInfo.BirthTypeId) birth = entry;
                if (entry.LinkId == marabout.Id && entry.TypeId == TimelineInfo.DeathTypeId) death = entry;
            }
            t.Check(birth != null && birth.Derived && birth.Title == "Naissance de Le marabout" && system.Format(birth.Start) == "E2.1141" && birth.LinkKind == TimelineLinks.Sheet,
                "la naissance du marabout : née de sa fiche, datée E2.1141, assignée à la fiche");
            t.Check(death != null && death.Title == "Décès de Le marabout" && system.Format(death.Start) == "2 Longue-Nuit E3.34", "…et son décès, le 2 Longue-Nuit E3.34");
            t.Check(timeline.Timeline.FindType(TimelineInfo.BirthTypeId) != null && timeline.Timeline.FindType(TimelineInfo.BirthTypeId).Name == "Naissance"
                && timeline.Timeline.FindType(TimelineInfo.DeathTypeId).Name == "Décès", "toute frise porte les types « Naissance » et « Décès »");
            t.Check(!timeline.Timeline.Events.Exists(delegate(TimelineEvent e) { return e.Derived; }), "…sans qu'aucune de ces briques soit rangée dans la frise");
            // Délier la date retire la brique ; la lier la crée.
            SetField(project, keira, SheetDefaults.FieldBirthDate, "");
            t.Equal(2, Timelines.LifeEventsOf(project, timeline, system).Events.Count, "date de naissance déliée : la brique disparaît");
            SetField(project, keira, SheetDefaults.FieldDeathDate, TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.71", null));
            t.Equal(3, Timelines.LifeEventsOf(project, timeline, system).Events.Count, "date de décès liée : la brique paraît");
            // Une date liée à une AUTRE frise ne paraît pas ici.
            SetField(project, keira, SheetDefaults.FieldBirthDate, TimeLinks.Markup(TimelineSamples.WritingTitle, "3 mars 2026", null));
            t.Check(!Timelines.LifeEventsOf(project, timeline, system).Events.Exists(delegate(TimelineEvent e) { return e.LinkId == keira.Id && e.TypeId == TimelineInfo.BirthTypeId; }),
                "une date liée à une autre frise ne naît pas dans celle-ci");
            // Une date illisible est un problème, pas une brique.
            SetField(project, keira, SheetDefaults.FieldBirthDate, "[[date:" + TimelineSamples.TimelineTitle + "@E9.1|E9.1]]");
            life = Timelines.LifeEventsOf(project, timeline, system);
            t.Check(life.Problems.Count == 1 && life.Problems[0].Sheet == keira && life.Problems[0].Notation == "E9.1", "une date de fiche illisible est signalée (E9.1 : il n'y a que trois ères)");
            // Les briques nées des fiches ne s'enregistrent jamais.
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c64-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                timeline.Timeline.Events.Add(new TimelineEvent { Title = "intruse", Start = new TimePoint(5), Derived = true });
                var path = Path.Combine(dir, "vie.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Check(!loaded.FindById("sample-timeline").Timeline.Events.Exists(delegate(TimelineEvent e) { return e.Title == "intruse"; }), "une brique née d'une fiche n'entre jamais dans le .plot");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static void Gender(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            SetField(project, keira, SheetDefaults.FieldDeathDate, TimeLinks.Markup(TimelineSamples.TimelineTitle, "E3.71", null));
            project.Epoch.Enabled = true;
            project.Epoch.Notation = "E3.80";
            t.Check(Epochs.Evaluate(project, marabout).Label.StartsWith("Mort·e à 93 ans"), "sans genre renseigné : « Mort·e » (" + Epochs.Evaluate(project, marabout).Label + ")");
            SetField(project, marabout, SheetDefaults.FieldBirthGender, "Masculin");
            SetField(project, keira, SheetDefaults.FieldBirthGender, "Féminin");
            t.Check(Epochs.Evaluate(project, marabout).Label.StartsWith("Mort à 93 ans"), "un homme : « Mort » (" + Epochs.Evaluate(project, marabout).Label + ")");
            t.Check(Epochs.Evaluate(project, keira).Label.StartsWith("Morte à 63 ans"), "une femme : « Morte » (" + Epochs.Evaluate(project, keira).Label + ")");
            project.Epoch.Notation = "E3.2";
            t.Equal("Naît dans 6 ans", Epochs.Evaluate(project, keira).Label, "pas encore née : « Naît dans 6 ans »");
        }

        private static void AgeNote(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            t.Check(Epochs.AgeNote(project, keira) == null, "époque éteinte : rien à côté de l'âge");
            project.Epoch.Enabled = true;
            project.Epoch.Notation = "E3.41";
            t.Equal("(33 ans en E3.41)", Epochs.AgeNote(project, keira), "époque E3.41 : « (33 ans en E3.41) » à côté de l'âge de Keira");
            SetField(project, marabout, SheetDefaults.FieldBirthGender, "Masculin");
            t.Equal("(mort à 93 ans, avant E3.41)", Epochs.AgeNote(project, marabout), "…et pour un mort : « (mort à 93 ans, avant E3.41) »");
            project.Epoch.Notation = "E3.2";
            SetField(project, keira, SheetDefaults.FieldBirthGender, "Féminin");
            t.Equal("(pas encore née en E3.2)", Epochs.AgeNote(project, keira), "…pas encore née : « (pas encore née en E3.2) »");
        }

        private static TimePoint At(TimeSystem system, string notation)
        {
            TimePoint point;
            if (!system.TryParse(notation, out point)) throw new InvalidOperationException("illisible : " + notation);
            return point;
        }

        private static void Convert(Harness t)
        {
            var before = TimelineSamples.TidesSystem();
            // L'ère 1 passe de 800 à 7 500 ans : une date garde son ère et son année dans l'ère.
            var after = before.Clone();
            after.Segments[0].Length = 7500;
            var moved = TimeSystemChange.Convert(before, after, At(before, "E2.905"));
            t.Check(moved != null && after.Format(moved) == "E2.905" && moved.Year == 7500 + 904, "l'ère 1 allongée : E2.905 reste E2.905 (année absolue " + (moved == null ? "?" : moved.Year.ToString()) + ")");
            t.Equal("12 Sel E3.8", after.Format(TimeSystemChange.Convert(before, after, At(before, "12 Sel E3.8"))), "…le jour et le mois suivent");
            t.Equal("E1.-120", after.Format(TimeSystemChange.Convert(before, after, At(before, "E1.-120"))), "…une année d'avant l'origine ne bouge pas");
            // L'ère 2 raccourcie à 1 000 ans : E2.1141 n'existe plus.
            var shorter = before.Clone();
            shorter.Segments[1].Length = 1000;
            t.Check(TimeSystemChange.Convert(before, shorter, At(before, "E2.1141")) == null, "l'ère 2 raccourcie à 1 000 ans : E2.1141 n'existe plus");
            t.Check(TimeSystemChange.Convert(before, shorter, At(before, "E2.905")) != null, "…E2.905 existe toujours");
            // Une ère retirée.
            var fewer = before.Clone();
            fewer.Segments.RemoveAt(2);
            fewer.Segments[1].Length = 0;
            t.Check(TimeSystemChange.Convert(before, fewer, At(before, "E3.8")) == null, "la troisième ère retirée : E3.8 n'existe plus");
            // Un mois raccourci, un mois retiré.
            var months = before.Clone();
            months.Months[3].Days = 10;
            t.Check(TimeSystemChange.Convert(before, months, At(before, "12 Sel E3.8")) == null, "Sel raccourci à dix jours : le 12 Sel n'existe plus");
            t.Check(TimeSystemChange.Convert(before, months, At(before, "Sel E3.8")) != null, "…mais « Sel E3.8 », au mois, si");
            // Passer aux années cumulatives : le même moment, écrit autrement.
            var cumulative = before.Clone();
            cumulative.Cumulative = true;
            var same = TimeSystemChange.Convert(before, cumulative, At(before, "E2.905"));
            t.Check(same != null && same.Year == 1704 && cumulative.Format(same) == "E2.1705", "années cumulatives : E2.905 devient E2.1705, le même moment");
            // L'an zéro ajouté : l'année lue reste la même.
            var zero = before.Clone();
            zero.YearZero = true;
            t.Equal("E2.905", zero.Format(TimeSystemChange.Convert(before, zero, At(before, "E2.905"))), "avec l'an zéro : E2.905 reste E2.905");
        }

        /// <summary>Le défaut rapporté par Rémi (13/11) : changer la durée d'une
        /// ère laissait les briques à leur ancienne place sur l'axe.</summary>
        private static void EraLength(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            var before = Timelines.SystemOf(project, timeline);
            var after = before.Clone();
            after.Segments[0].Length = 7500;
            var change = TimeSystemChange.Build(project, before, after);
            t.Equal(0, change.Pending, "l'ère 1 allongée à 7 500 ans : aucune brique en attente");
            t.Check(change.Moved >= 8, "…les briques des ères 2 et 3 ont suivi (" + change.Moved + ")");
            new TimeSystemEditAction(project, change, false).Do();
            var system = Timelines.SystemOf(project, timeline);
            t.Check(ReferenceEquals(system, after), "le projet porte le nouveau système");
            var ok = true;
            var expected = new Dictionary<string, string>
            {
                { "Serment des Gardiens", "1 Aubemer E2.1" }, { "Le livre des marées", "E2.905" }, { "La Grande Tempête", "E2.1188" },
                { "Le marabout et la mer", "3 Brisants E3.30" }, { "Fondation d'Ourkhal", "E1.1" }, { "Les premiers pêcheurs", "E1.-120" }
            };
            foreach (var entry in timeline.Timeline.Events)
            {
                string wanted;
                if (expected.TryGetValue(entry.Title, out wanted) && system.Format(entry.Start) != wanted) { ok = false; t.Check(false, entry.Title + " : " + system.Format(entry.Start) + " au lieu de " + wanted); }
            }
            t.Check(ok, "chaque brique garde la date qu'elle portait (E2.905 reste E2.905)");
            t.Equal(7500L, Timelines.Stats(system, timeline.Timeline).Segments[0].Length, "l'ère 1 dure 7 500 ans dans les statistiques");
            // Les liens de date s'écrivent pareil : rien à réécrire, et les vies suivent.
            t.Equal(0, change.LinksRewritten, "aucun lien de date à réécrire (la notation n'a pas changé)");
            var life = Timelines.LifeEventsOf(project, timeline, system);
            t.Check(life.Events.Count == 3 && life.Problems.Count == 0, "les naissances et les décès se relisent dans le nouveau système");
            t.Check(Epochs.Evaluate(project, keira, system, At(system, "E3.30")).Age == 22, "…et Keira a toujours 22 ans en E3.30");
        }

        private static void Pending(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            var before = Timelines.SystemOf(project, timeline);
            var after = before.Clone();
            after.Segments[1].Length = 1000; // E2.1188 → E2.1200 n'existe plus
            var change = TimeSystemChange.Build(project, before, after);
            t.Equal(1, change.Pending, "l'ère 2 raccourcie à 1 000 ans : une brique en attente (La Grande Tempête)");
            t.Equal(1, change.LinksUnreadable, "…et un lien de date devenu illisible (la naissance du marabout, E2.1141)");
            var action = new TimeSystemEditAction(project, change, false);
            action.Do();
            var storm = timeline.Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == "La Grande Tempête"; });
            t.Check(storm != null && storm.IsPending && storm.PendingStart == "E2.1188" && storm.PendingEnd == "E2.1200", "elle garde ce qui était écrit : E2.1188 → E2.1200");
            var system = Timelines.SystemOf(project, timeline);
            t.Equal(11, Timelines.Stats(system, timeline.Timeline).Events, "une brique en attente ne compte plus sur l'axe (onze sur douze)");
            var life = Timelines.LifeEventsOf(project, timeline, system);
            t.Check(life.Problems.Count == 1 && life.Problems[0].Sheet == marabout && life.Problems[0].Notation == "E2.1141", "la date de naissance du marabout est signalée illisible");
            // L'attente traverse le .plot.
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c64-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "attente.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var again = loaded.FindById("sample-timeline").Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == "La Grande Tempête"; });
                t.Check(again != null && again.IsPending && again.PendingStart == "E2.1188" && again.PendingEnd == "E2.1200", "la brique en attente et ses dates écrites traversent le .plot");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            // L'ère rallongée de nouveau : ce qui était écrit se relit, la brique revient.
            var restored = after.Clone();
            restored.Segments[1].Length = 1300;
            var back = TimeSystemChange.Build(project, after, restored);
            new TimeSystemEditAction(project, back, false).Do();
            storm = timeline.Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == "La Grande Tempête"; });
            t.Check(storm != null && !storm.IsPending && restored.Format(storm.Start) == "E2.1188" && restored.Format(storm.End) == "E2.1200", "l'ère 2 rallongée : la brique en attente retrouve sa date");
        }

        private static void NotationRewrite(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            // Un lien de date dans un écrit, dont un avec ses propres mots.
            var text = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre" };
            text.Document = TextDocument.FromPlainText("En [[date:Chronique du Littoral@E2.905|E2.905]], puis [[date:Chronique du Littoral@E3.30|cette année-là]]. Voir [[Keira Varenh]].");
            project.Category(Project.KeyWritings).Children.Add(text);
            project.RelinkParents();
            project.Epoch.Enabled = true;
            var before = Timelines.SystemOf(project, timeline);
            var after = before.Clone();
            after.YearPattern = "{a} de l'ère {s}";
            var change = TimeSystemChange.Build(project, before, after);
            t.Equal(0, change.Pending, "la notation change : aucune brique en attente (elles gardent leur place)");
            t.Check(change.LinksRewritten == 5, "cinq liens de date à réécrire : deux dans l'écrit, trois sur les fiches (" + change.LinksRewritten + ")");
            var action = new TimeSystemEditAction(project, change, false);
            action.Do();
            var written = text.Document.ToPlainText();
            t.Check(written.Contains("[[date:Chronique du Littoral@905 de l'ère 2|905 de l'ère 2]]"), "le lien écrit « E2.905 » devient « 905 de l'ère 2 », texte compris");
            t.Check(written.Contains("[[date:Chronique du Littoral@30 de l'ère 3|cette année-là]]"), "…celui qui avait ses propres mots les garde");
            t.Check(written.Contains("[[Keira Varenh]]"), "…un lien de fiche n'est pas touché");
            t.Equal("[[date:Chronique du Littoral@12 Sel 8 de l'ère 3|12 Sel 8 de l'ère 3]]", Field(project, keira, SheetDefaults.FieldBirthDate), "la date de naissance de Keira est réécrite dans son champ");
            t.Equal("30 de l'ère 3", project.Epoch.Notation, "l'époque suit la nouvelle notation");
            t.Check(Epochs.Evaluate(project, keira).Age == 22 && Timelines.LifeEventsOf(project, timeline, after).Events.Count == 3, "les âges et les vies se relisent dans la nouvelle notation");
            action.Undo();
            t.Check(text.Document.ToPlainText().Contains("[[date:Chronique du Littoral@E2.905|E2.905]]") && project.Epoch.Notation == "E3.30"
                && Field(project, keira, SheetDefaults.FieldBirthDate) == "[[date:Chronique du Littoral@12 Sel E3.8|12 Sel E3.8]]"
                && ReferenceEquals(Timelines.SystemOf(project, timeline), before), "Ctrl+Z : les liens, l'époque et le système reviennent");
            // Renommer la frise recible aussi les liens de date posés dans les champs de fiche.
            var plan = LinkHygiene.Retarget(project, TimelineSamples.TimelineTitle, "Annales");
            plan.Apply(project, true);
            t.Equal("[[date:Annales@12 Sel E3.8|12 Sel E3.8]]", Field(project, keira, SheetDefaults.FieldBirthDate), "frise renommée : le lien d'un champ de fiche suit, intact");
        }

        private static void Undo(Harness t)
        {
            BinderItem timeline, keira, marabout;
            var project = Sample(out timeline, out keira, out marabout);
            var history = new HistoryManager();
            var before = timeline.Timeline.Clone();
            var removed = timeline.Timeline.Events[2];
            timeline.Timeline.Events.RemoveAt(2);
            history.Push(new TimelineEditAction(timeline, before, timeline.Timeline.Clone()));
            t.Equal(11, timeline.Timeline.Events.Count, "une brique retirée : onze");
            history.Undo();
            t.Check(timeline.Timeline.Events.Count == 12 && timeline.Timeline.Events[2].Id == removed.Id && timeline.Timeline.Events[2].Title == removed.Title, "Ctrl+Z : la brique revient, à sa place, avec son id");
            history.Redo();
            t.Equal(11, timeline.Timeline.Events.Count, "Ctrl+Y : elle repart");
            history.Undo();
            // Un système créé puis défait.
            var created = TimeSystemDialogTemplate();
            var creation = new TimeSystemEditAction(project, TimeSystemChange.Build(project, null, created), true);
            history.Run(creation);
            t.Check(project.TimeSystems.Contains(created), "un système créé entre dans le projet");
            history.Undo();
            t.Check(!project.TimeSystems.Contains(created) && project.TimeSystems.Count == 2, "…et en sort d'un Ctrl+Z");
        }

        private static TimeSystem TimeSystemDialogTemplate()
        {
            var system = new TimeSystem { Name = "Autre", YearPattern = "{a}" };
            system.Months.Add(new TimeMonth { Name = "Mois", Days = 30 });
            return system;
        }
    }
}
