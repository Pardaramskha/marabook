using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'EXEMPLE des frises (1.2.0) — le projet de démonstration et
    /// les tests s'en servent : un système temporel inventé (« Calendrier des
    /// Marées » : trois ères qui repartent de zéro, dix mois de trente-six
    /// jours, une bissextile tous les cinq ans, une semaine de six jours, des
    /// jours de vingt heures), sa frise (« Chronique du Littoral »), et une
    /// frise posée sur le calendrier de notre monde (« Écriture du roman »).</summary>
    public static class TimelineSamples
    {
        public const string SystemName = "Calendrier des Marées";
        public const string TimelineTitle = "Chronique du Littoral";
        public const string WritingTitle = "Écriture du roman";

        public static TimeSystem TidesSystem()
        {
            var system = new TimeSystem
            {
                Id = "sample-tides", Name = SystemName, Segmented = true, SegmentWord = "Ère", Cumulative = false,
                YearPattern = "E{s}.{a}", NegativePattern = "", DatePattern = "{j} {M} {année}",
                HoursPerDay = 20, MinutesPerHour = 50
            };
            system.Segments.Add(new TimeSegment { Id = "sample-era-1", Name = "Ère des Brumes", Length = 800 });
            system.Segments.Add(new TimeSegment { Id = "sample-era-2", Name = "Ère des Phares", Length = 1200 });
            system.Segments.Add(new TimeSegment { Id = "sample-era-3", Name = "Ère des Grandes Marées", Length = 0 });
            foreach (var name in new[] { "Aubemer", "Vent-Neuf", "Hautes-Eaux", "Sel", "Brisants", "Calme-Plat", "Morte-Eau", "Brumaire", "Noroît", "Longue-Nuit" })
                system.Months.Add(new TimeMonth { Name = name, Days = 36 });
            // Tous les cinq ans, cinq jours de plus à Longue-Nuit — sauf les années multiples de cinquante.
            system.Leaps.Add(new LeapRule { Every = 5, Except = 50, Month = 9, Days = 5 });
            system.WeekDays.AddRange(new[] { "lunemer", "flot", "jusant", "étale", "ressac", "repos" });
            return system;
        }

        private static TimePoint At(TimeSystem system, string notation)
        {
            TimePoint point;
            if (!system.TryParse(notation, out point)) throw new InvalidOperationException("Date d'exemple illisible : " + notation);
            return point;
        }

        private static TimelineEvent Event(TimeSystem system, string title, string type, string start, string end, string text)
        {
            return new TimelineEvent
            {
                Title = title, TypeId = type, Description = text ?? "",
                Start = At(system, start), End = end == null ? null : At(system, end)
            };
        }

        private static void Assign(TimelineEvent entry, BinderItem item)
        {
            if (item == null) return;
            entry.LinkKind = TimelineLinks.KindFor(item);
            entry.LinkId = entry.LinkKind == null ? null : item.Id;
        }

        private static BinderItem Find(Project project, string title, ItemKind kind)
        {
            foreach (var item in project.AllItems())
                if (item.Kind == kind && item.Title == title) return item;
            return null;
        }

        /// <summary>Pose l'exemple dans un projet : les deux systèmes, les deux
        /// frises, les dates de naissance et de décès liées sur les fiches
        /// « Keira Varenh » et « Le marabout » si elles existent, et une époque
        /// réglée (E3.30, éteinte). Rend la frise du Littoral.</summary>
        public static BinderItem Seed(Project project)
        {
            var root = project.Category(Project.KeyTimelines);
            if (root == null) return null;
            var tides = TidesSystem();
            var world = TimeSystem.Gregorian();
            world.Id = "sample-world";
            project.TimeSystems.Add(tides);
            project.TimeSystems.Add(world);

            var keira = Find(project, "Keira Varenh", ItemKind.Sheet);
            var marabout = Find(project, "Le marabout", ItemKind.Sheet);
            var book = Find(project, "Le marabout et la mer", ItemKind.Book);
            var chapter = Find(project, "Chapitre trois — L'odeur de la marée", ItemKind.Text);

            var timeline = new BinderItem
            {
                Id = "sample-timeline", Kind = ItemKind.Timeline, Title = TimelineTitle, Parent = root,
                Timeline = new TimelineInfo { SystemId = tides.Id, Types = TimelineInfo.DefaultTypes() }
            };
            const string history = "type-1", conflict = "type-2", character = "type-3", story = "type-4", culture = "type-5";
            var events = timeline.Timeline.Events;
            events.Add(Event(tides, "Les premiers pêcheurs", history, "E1.-120", null, "Des familles s'installent dans les criques, bien avant que l'on compte les années."));
            events.Add(Event(tides, "Fondation d'Ourkhal", history, "E1.1", null, "La première digue est levée : le compte des années commence."));
            events.Add(Event(tides, "Guerre des Brumes", conflict, "E1.212", "E1.240", "Vingt-neuf ans de razzias entre les villages du littoral."));
            events.Add(Event(tides, "Le premier phare", culture, "E1.640", null, "On allume un feu au sommet de la tour d'Ourkhal."));
            events.Add(Event(tides, "Serment des Gardiens", history, "1 Aubemer E2.1", null, "Les gardiens de phare prennent la charge du littoral : une ère commence."));
            events.Add(Event(tides, "Les Quarante Hivers", history, "E2.430", "E2.470", "Quarante années sans été ; la mer gèle dans les ports."));
            events.Add(Event(tides, "Le livre des marées", culture, "E2.905", null, "Première table écrite des marées du littoral."));
            var birthMarabout = Event(tides, "Naissance du marabout", character, "E2.1141", null, null);
            Assign(birthMarabout, marabout);
            events.Add(birthMarabout);
            events.Add(Event(tides, "La Grande Tempête", conflict, "E2.1188", "E2.1200", "Douze ans de tempêtes ; les phares s'éteignent un à un."));
            events.Add(Event(tides, "La première grande marée", history, "1 Aubemer E3.1", null, "La mer monte plus haut que la digue : l'ère des Grandes Marées."));
            var birthKeira = Event(tides, "Naissance de Keira", character, "12 Sel E3.8", null, "À Ourkhal, un jour de jusant.");
            Assign(birthKeira, keira);
            events.Add(birthKeira);
            var novel = Event(tides, "Le marabout et la mer", story, "3 Brisants E3.30", "20 Noroît E3.30", "Le temps du roman.");
            Assign(novel, book);
            events.Add(novel);
            var tide = Event(tides, "La grande marée", story, "14 Noroît E3.30", null, "Le chapitre trois.");
            Assign(tide, chapter);
            events.Add(tide);
            var deathMarabout = Event(tides, "Mort du marabout", character, "2 Longue-Nuit E3.34", null, null);
            Assign(deathMarabout, marabout);
            events.Add(deathMarabout);
            root.Children.Add(timeline);

            var writing = new BinderItem
            {
                Id = "sample-writing", Kind = ItemKind.Timeline, Title = WritingTitle, Parent = root,
                Timeline = new TimelineInfo { SystemId = world.Id, Types = TimelineInfo.DefaultTypes() }
            };
            var draft = Event(world, "Premier jet", story, "3 mars 2026", "28 juin 2026", null);
            Assign(draft, book);
            writing.Timeline.Events.Add(draft);
            writing.Timeline.Events.Add(Event(world, "Envoi aux bêta-lecteurs", history, "14 juillet 2026", null, null));
            writing.Timeline.Events.Add(Event(world, "Corrections", story, "septembre 2026", "octobre 2026", null));
            root.Children.Add(writing);

            // Les dates des personnages : des liens de date, dans le champ du
            // modèle quand la fiche en a un, en information libre sinon.
            SetDate(project, keira, SheetDefaults.FieldBirthDate, "12 Sel E3.8");
            SetDate(project, marabout, SheetDefaults.FieldBirthDate, "E2.1141");
            SetDate(project, marabout, SheetDefaults.FieldDeathDate, "2 Longue-Nuit E3.34");

            project.Epoch = new EpochState { Enabled = false, TimelineId = timeline.Id, Notation = "E3.30" };
            return timeline;
        }

        private static void SetDate(Project project, BinderItem sheet, string fieldName, string notation)
        {
            if (sheet == null) return;
            var value = TimeLinks.Markup(TimelineTitle, notation, null);
            var template = project.TemplateOf(sheet);
            if (template != null)
                foreach (var field in template.Fields)
                    if (field.Name == fieldName) { sheet.FieldValues[field.Id] = value; return; }
            sheet.FreeInfo.Add(new InfoEntry { Title = fieldName, Value = value });
        }
    }
}
