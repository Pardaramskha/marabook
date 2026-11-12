using System;
using System.Collections.Generic;
using System.IO;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C65 — les dissonances (1.2.0, 12/11) : la période d'exclusion
    /// (un saut dans le temps ne vieillit pas), le point de référence (un âge
    /// donné à une date d'une autre temporalité), le lien ordinaire vers une
    /// frise lu comme une date de naissance, et leur vie dans le .plot,
    /// l'historique et le changement de temporalité.</summary>
    public static class DissonanceTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C65 — dissonances");
            LooseLink(t);
            Exclusion(t);
            Reference(t);
            Lifecycle(t);
        }

        private static Project Sample(out BinderItem timeline, out TimeSystem system, out BinderItem sheet)
        {
            var project = Project.CreateNew();
            sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "André", TemplateId = project.CharacterTemplate().Id };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            system = Timelines.SystemOf(project, timeline);
            return project;
        }

        private static void SetField(Project project, BinderItem sheet, string name, string value)
        {
            foreach (var field in project.TemplateOf(sheet).Fields)
                if (field.Name == name) { sheet.FieldValues[field.Id] = value; return; }
            throw new InvalidOperationException("champ introuvable : " + name);
        }

        private static LifeStatus At(Project project, BinderItem sheet, TimeSystem system, string notation)
        {
            TimePoint at;
            if (!system.TryParse(notation, out at)) throw new InvalidOperationException("date illisible : " + notation);
            return Epochs.Evaluate(project, sheet, system, at);
        }

        private static string Note(Project project, BinderItem sheet, string notation)
        {
            project.Epoch.Enabled = true;
            project.Epoch.Notation = notation;
            return Epochs.AgeNote(project, sheet);
        }

        /// <summary>Le rapport de Rémi : une date de naissance posée comme un lien
        /// ORDINAIRE vers la frise (« [[Frise|E5.878]] ») et un décès en lien de
        /// date — la fiche disait « En vie », sans âge.</summary>
        private static void LooseLink(Harness t)
        {
            BinderItem timeline, sheet;
            TimeSystem system;
            var project = Sample(out timeline, out system, out sheet);
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[" + TimelineSamples.TimelineTitle + "|E3.10]]");
            SetField(project, sheet, SheetDefaults.FieldDeathDate, "[[date:" + TimelineSamples.TimelineTitle + "@E3.44|E3.44]]");
            var status = At(project, sheet, system, "E3.44");
            t.Check(status.State == LifeState.Alive && status.HasAge && status.Age == 34 && status.Label == "34 ans",
                "un lien ordinaire vers la frise, au texte daté, vaut date de naissance : 34 ans, plus « En vie » (" + status.Label + ")");
            var life = Timelines.LifeEventsOf(project, timeline, system);
            t.Check(life.Events.Exists(delegate(TimelineEvent e) { return e.Id == "vie:" + sheet.Id + ":naissance" && system.Format(e.Start) == "E3.10"; }),
                "…et la frise en tire la brique de naissance");
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[" + TimelineSamples.TimelineTitle + "|sa chronique]]");
            t.Check(At(project, sheet, system, "E3.40").Label == "En vie", "un lien vers la frise dont le texte n'est pas une date reste un lien ordinaire");
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[Keira Varenh|E3.10]]");
            t.Check(At(project, sheet, system, "E3.40").Label == "En vie", "…comme un lien vers autre chose qu'une frise");
        }

        /// <summary>André saute de 30 à 80 : né en 10, il a 20 ans au départ.</summary>
        private static void Exclusion(Harness t)
        {
            BinderItem timeline, sheet;
            TimeSystem system;
            var project = Sample(out timeline, out system, out sheet);
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[date:" + TimelineSamples.TimelineTitle + "@E3.10|E3.10]]");
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = system.Id, From = "E3.30", To = "E3.80" });
            var leaving = At(project, sheet, system, "E3.30");
            t.Check(leaving.State == LifeState.Alive && leaving.Age == 20 && !leaving.Excluded, "l'année du départ, il a encore son âge : 20 ans");
            var away = At(project, sheet, system, "E3.50");
            t.Check(away.State == LifeState.OutOfTime && away.Age == 20 && away.Label == "Hors-temps · 20 ans en E3.30",
                "pendant le saut : « Hors-temps · 20 ans en E3.30 » (" + away.Label + ")");
            t.Equal("Hors-temps en E3.50 · 20 ans en E3.30", Epochs.BannerNote(away, "E3.50"), "le bandeau de la fiche le dit avec l'époque");
            var back = At(project, sheet, system, "E3.88");
            t.Check(back.State == LifeState.Alive && back.Age == 28 && back.Excluded && back.Label == "28 ans (excl.)",
                "huit ans après l'arrivée : « 28 ans (excl.) » (" + back.Label + ")");
            t.Check(At(project, sheet, system, "E3.80").Label == "20 ans (excl.)", "à l'arrivée, il a l'âge du départ");
            t.Equal("(hors-temps en E3.50, 20 ans en E3.30)", Note(project, sheet, "E3.50"), "la vue wiki, pendant le saut");
            t.Equal("(28 ans (excl.) en E3.88)", Note(project, sheet, "E3.88"), "…et après");
            // Deux sauts : les années du premier ne comptent pas dans le second.
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = system.Id, From = "E3.90", To = "E3.100" });
            t.Check(At(project, sheet, system, "E3.95").Label == "Hors-temps · 30 ans en E3.90" && At(project, sheet, system, "E3.105").Label == "35 ans (excl.)",
                "deux sauts se cumulent");
            // Mort après un saut : l'âge de la mort saute aussi.
            SetField(project, sheet, SheetDefaults.FieldDeathDate, "[[date:" + TimelineSamples.TimelineTitle + "@E3.110|E3.110]]");
            var dead = At(project, sheet, system, "E3.120");
            t.Check(dead.State == LifeState.Dead && dead.Age == 40, "l'âge de la mort tient compte des sauts (" + dead.Label + ")");
            // Une entrée illisible ou d'un autre système est ignorée.
            sheet.Dissonances.Clear();
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = system.Id, From = "E3.80", To = "E3.30" });
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = "ailleurs", From = "E3.30", To = "E3.80" });
            t.Check(sheet.Dissonances[0].Problem(system) != null && At(project, sheet, system, "E3.50").Label == "40 ans", "une période à l'envers ou d'un autre système ne change rien");
        }

        /// <summary>Zoé, née ailleurs, arrive en E3.20 à 19 ans.</summary>
        private static void Reference(Harness t)
        {
            BinderItem timeline, sheet;
            TimeSystem system;
            var project = Sample(out timeline, out system, out sheet);
            sheet.Title = "Zoé";
            // Née en 1993 dans l'autre système du projet : rien à compter ici.
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[date:" + TimelineSamples.WritingTitle + "@1993|1993]]");
            t.Check(At(project, sheet, system, "E3.22").State == LifeState.Unknown, "née dans un autre calendrier, sans dissonance : rien à dire");
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Reference, SystemId = system.Id, From = "E3.20", Age = 19 });
            var before = At(project, sheet, system, "E3.12");
            t.Check(before.State == LifeState.Absent && before.Label == "Absent·e", "avant le point de référence : absente, pas « non née » (" + before.Label + ")");
            var arrival = At(project, sheet, system, "E3.20");
            t.Check(arrival.State == LifeState.Alive && arrival.Age == 19, "au point de référence : 19 ans");
            var later = At(project, sheet, system, "E3.22");
            t.Check(later.State == LifeState.Alive && later.Age == 21 && later.Label == "21 ans" && !later.Excluded, "deux ans plus tard : 21 ans (" + later.Label + ")");
            t.Equal("(absent·e en E3.12)", Note(project, sheet, "E3.12"), "la vue wiki avant l'arrivée");
            SetField(project, sheet, "Genre de naissance", "Féminin");
            t.Check(At(project, sheet, system, "E3.12").Label == "Absente", "« Absente » s'accorde au genre de la fiche");
            // Un saut après l'arrivée se retranche aussi.
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = system.Id, From = "E3.25", To = "E3.35" });
            t.Check(At(project, sheet, system, "E3.40").Label == "29 ans (excl.)", "référence puis saut : 19 + 20 − 10");
            // Née dans ce monde, partie, revenue plus âgée : le dernier point atteint l'emporte.
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[date:" + TimelineSamples.TimelineTitle + "@E3.5|E3.5]]");
            sheet.Dissonances.Clear();
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Reference, SystemId = system.Id, From = "E3.20", Age = 40 });
            t.Check(At(project, sheet, system, "E3.10").Label == "5 ans" && At(project, sheet, system, "E3.21").Label == "41 ans" && At(project, sheet, system, "E3.2").State == LifeState.Unborn,
                "naissance et référence dans le même système : la dernière atteinte compte");
        }

        private static void Lifecycle(Harness t)
        {
            BinderItem timeline, sheet;
            TimeSystem system;
            var project = Sample(out timeline, out system, out sheet);
            SetField(project, sheet, SheetDefaults.FieldBirthDate, "[[date:" + TimelineSamples.TimelineTitle + "@E3.10|E3.10]]");

            // L'historique de la fiche.
            var empty = SheetState.Capture(sheet);
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Exclusion, SystemId = system.Id, From = "E3.30", To = "E3.80" });
            sheet.Dissonances.Add(new Dissonance { Kind = Dissonance.Reference, SystemId = system.Id, From = "E3.90", Age = 50 });
            var full = SheetState.Capture(sheet);
            t.Check(SheetState.DiffKey(empty, full).Contains("dissonance-set"), "ajouter une dissonance est une édition de la fiche");
            empty.ApplyTo(sheet);
            t.Equal(0, sheet.Dissonances.Count, "Ctrl+Z la retire");
            full.ApplyTo(sheet);
            t.Check(sheet.Dissonances.Count == 2 && !ReferenceEquals(sheet.Dissonances[0], full.Dissonances[0]), "Ctrl+Y la remet (une copie)");

            // La copie d'une fiche.
            var twin = sheet.Duplicate(false);
            t.Check(twin.Dissonances.Count == 2 && twin.Dissonances[0].Id != sheet.Dissonances[0].Id && twin.Dissonances[0].SameAs(sheet.Dissonances[0]), "dupliquer la fiche copie ses dissonances");

            // Le .plot.
            var path = Path.Combine(Path.GetTempPath(), "marabook-c65-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                BinderItem again = null;
                foreach (var item in loaded.AllItems()) if (item.Id == sheet.Id) again = item;
                t.Check(again != null && again.Dissonances.Count == 2 && again.Dissonances[0].SameAs(sheet.Dissonances[0]) && again.Dissonances[1].SameAs(sheet.Dissonances[1])
                    && again.Dissonances[1].Id == sheet.Dissonances[1].Id, "les dissonances font l'aller-retour dans le .plot");
            }
            finally { try { File.Delete(path); } catch { } }

            // La notation change : les dates des dissonances se réécrivent, et reviennent.
            var after = system.Clone();
            after.YearPattern = "{a} de l'ère {s}";
            var change = TimeSystemChange.Build(project, system, after);
            var action = new TimeSystemEditAction(project, change, false);
            action.Do();
            t.Check(sheet.Dissonances[0].From == "30 de l'ère 3" && sheet.Dissonances[0].To == "80 de l'ère 3" && sheet.Dissonances[1].From == "90 de l'ère 3",
                "changement de notation : les dates des dissonances sont réécrites (" + sheet.Dissonances[0].From + ")");
            t.Check(At(project, sheet, after, "50 de l'ère 3").State == LifeState.OutOfTime, "…et se lisent toujours");
            action.Undo();
            t.Check(sheet.Dissonances[0].From == "E3.30" && sheet.Dissonances[1].From == "E3.90", "Ctrl+Z : elles reviennent");
        }
    }
}
