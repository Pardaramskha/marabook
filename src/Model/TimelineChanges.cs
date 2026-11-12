using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    // LES FRISES, lot 2 (1.2.0, 12/11/2026, Rémi) : les naissances et les décès
    // nés des fiches, l'accord de « mort / morte », l'âge d'époque de la vue
    // wiki, et ce qui se passe quand la temporalité change — chaque date est
    // RELUE dans le nouveau système (une brique datée « E2.905 » reste
    // « E2.905 » si l'ère 1 s'allonge), ce qui ne s'y lit plus passe en attente.

    /// <summary>Une date de fiche (naissance, décès) liée à la frise mais qui
    /// ne s'y lit pas.</summary>
    public class LifeProblem
    {
        public BinderItem Sheet;
        public string Field;     // « Date de naissance », « Date de décès »
        public string Notation;  // ce qui est écrit dans le lien
    }

    public class LifeEvents
    {
        public List<TimelineEvent> Events = new List<TimelineEvent>();
        public List<LifeProblem> Problems = new List<LifeProblem>();
    }

    public static partial class Timelines
    {
        /// <summary>LES NAISSANCES ET LES DÉCÈS d'une frise : une brique par date
        /// de naissance ou de décès qu'une fiche lie à CETTE frise. Calculées,
        /// jamais enregistrées : lier la date sur la fiche crée la brique,
        /// délier la retire.</summary>
        public static LifeEvents LifeEventsOf(Project project, BinderItem timeline, TimeSystem system)
        {
            var result = new LifeEvents();
            if (project == null || timeline == null || system == null) return result;
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Kind != ItemKind.Sheet || sheet.RootCategory().CategoryKey == Project.KeyTrash) continue;
                AddLife(project, timeline, system, sheet, SheetDefaults.FieldBirthDate, TimelineInfo.BirthTypeId, "Naissance de ", ":naissance", result);
                AddLife(project, timeline, system, sheet, SheetDefaults.FieldDeathDate, TimelineInfo.DeathTypeId, "Décès de ", ":deces", result);
            }
            return result;
        }

        private static void AddLife(Project project, BinderItem timeline, TimeSystem system, BinderItem sheet, string field,
            string typeId, string prefix, string suffix, LifeEvents result)
        {
            var value = Epochs.FieldValue(project, sheet, field);
            if (value == null) return;
            foreach (var link in Links.Find(value))
            {
                string title, notation;
                if (!TimeLinks.SplitField(project, link, out title, out notation)) continue;
                if (!Links.SameTitle(title, (timeline.Title ?? "").Trim())) continue;
                TimePoint point;
                if (!system.TryParse(notation, out point))
                {
                    result.Problems.Add(new LifeProblem { Sheet = sheet, Field = field, Notation = notation });
                    return;
                }
                result.Events.Add(new TimelineEvent
                {
                    Id = "vie:" + sheet.Id + suffix, Title = prefix + sheet.Title, TypeId = typeId, Start = point,
                    LinkKind = TimelineLinks.Sheet, LinkId = sheet.Id, Derived = true
                });
                return;
            }
        }
    }

    public static partial class Epochs
    {
        /// <summary>Un mot accordé au genre de la fiche (« Mort », « Morte », « Mort·e »
        /// sans genre renseigné).</summary>
        public static string Word(Project project, BinderItem sheet, string masculine, string feminine, string neutral)
        {
            var gender = sheet == null ? 'n' : RelationKinds.GenderOf(sheet, project == null ? null : project.TemplateOf(sheet));
            return gender == 'm' ? masculine : gender == 'f' ? feminine : neutral;
        }

        /// <summary>Ce que la vue wiki ajoute à côté de l'âge quand une époque est
        /// active : « (33 ans en E5.912) » ; null sans époque ou sans date liée.</summary>
        public static string AgeNote(Project project, BinderItem sheet)
        {
            TimeSystem system;
            BinderItem timeline;
            var at = Current(project, out system, out timeline);
            if (at == null || sheet == null || sheet.Kind != ItemKind.Sheet) return null;
            var status = Evaluate(project, sheet, system, at);
            var when = system.Format(at);
            switch (status.State)
            {
                case LifeState.Alive: return status.HasAge ? "(" + status.Label + " en " + when + ")" : null;
                case LifeState.Absent: return "(" + Word(project, sheet, "absent", "absente", "absent·e") + " en " + when + ")";
                case LifeState.OutOfTime: return "(hors-temps en " + when + (status.Detail.Length > 0 ? ", " + status.Detail : "") + ")";
                case LifeState.Dead:
                    return "(" + Word(project, sheet, "mort", "morte", "mort·e") + (status.HasAge ? " à " + TimeSystem.YearsLabel(status.Age) : "") + ", avant " + when + ")";
                case LifeState.Unborn: return "(pas encore " + Word(project, sheet, "né", "née", "né·e") + " en " + when + ")";
                default: return null;
            }
        }
    }

    /// <summary>CE QU'UN CHANGEMENT DE TEMPORALITÉ FAIT AUX DATES : les briques
    /// des frises du système, l'époque, et les liens de date du texte. Rien
    /// n'est modifié ici — le plan est appliqué (et défait) par
    /// History.TimeSystemEditAction.</summary>
    public class TimeSystemChange
    {
        public TimeSystem Before, After;
        public Dictionary<BinderItem, TimelineInfo> InfosBefore = new Dictionary<BinderItem, TimelineInfo>();
        public Dictionary<BinderItem, TimelineInfo> InfosAfter = new Dictionary<BinderItem, TimelineInfo>();
        public string EpochBefore, EpochAfter;
        public Dictionary<BinderItem, List<Dissonance>> DissonancesBefore = new Dictionary<BinderItem, List<Dissonance>>();
        public Dictionary<BinderItem, List<Dissonance>> DissonancesAfter = new Dictionary<BinderItem, List<Dissonance>>();
        public ReplacePlan Links = new ReplacePlan();
        public int Moved;        // briques dont la place sur l'axe a suivi
        public int Pending;      // briques passées en attente
        public int LinksRewritten;
        public int LinksUnreadable;

        /// <summary>Relit une date de l'ancien système dans le nouveau. Entre deux
        /// systèmes dont chaque segment repart de zéro, la date garde son
        /// segment et son année DANS le segment ; sinon elle garde son année
        /// sur l'axe. Null si elle n'existe plus (segment retiré, année au-delà
        /// de son segment, mois ou jour disparus).</summary>
        public static TimePoint Convert(TimeSystem from, TimeSystem to, TimePoint point)
        {
            if (point == null) return null;
            var year = point.Year;
            var fromReset = from.HasSegments && !from.Cumulative;
            var toReset = to.HasSegments && !to.Cumulative;
            if (fromReset && toReset && year >= 0)
            {
                var segment = from.SegmentIndexOf(year);
                if (segment >= to.Segments.Count) return null;
                var offset = year - from.SegmentStart(segment);
                // L'an zéro ajouté ou retiré : l'année LUE reste la même.
                offset += from.FirstYear - to.FirstYear;
                if (offset < 0) return null;
                year = to.SegmentStart(segment) + offset;
                if (to.SegmentIndexOf(year) != segment) return null;
            }
            else if (year >= 0 && from.FirstYear != to.FirstYear)
            {
                year += from.FirstYear - to.FirstYear;
                if (year < 0) return null;
            }
            var converted = new TimePoint(year);
            if (!point.HasMonth) return converted;
            if (point.Month > to.Months.Count) return null;
            converted.Month = point.Month;
            if (!point.HasDay) return converted;
            if (point.Day > to.DaysInMonth(year, point.Month)) return null;
            converted.Day = point.Day;
            if (!point.HasTime) return converted;
            if (point.Hour >= Math.Max(1, to.HoursPerDay) || point.Minute >= Math.Max(1, to.MinutesPerHour)) return null;
            converted.Hour = point.Hour;
            converted.Minute = point.Minute;
            return converted;
        }

        /// <summary>Le plan complet d'un changement de système (before : le
        /// système du projet ; after : sa version validée).</summary>
        public static TimeSystemChange Build(Project project, TimeSystem before, TimeSystem after)
        {
            var change = new TimeSystemChange { Before = before, After = after };
            if (project == null || before == null || after == null) return change;
            var titles = new List<string>(); // les frises posées sur ce système
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Timeline || item.Timeline == null) continue;
                if (!ReferenceEquals(Timelines.SystemOf(project, item), before)) continue;
                titles.Add((item.Title ?? "").Trim());
                var info = item.Timeline.Clone();
                foreach (var entry in info.Events)
                {
                    if (entry.IsPending)
                    {
                        // Déjà en attente : ce qui était écrit se lit-il de nouveau ?
                        TimePoint start, end = null;
                        if (!after.TryParse(entry.PendingStart, out start)) continue;
                        if (entry.PendingEnd != null && !after.TryParse(entry.PendingEnd, out end)) continue;
                        if (end != null && after.AxisEnd(end) <= after.AxisStart(start)) continue;
                        entry.Start = start;
                        entry.End = end;
                        entry.PendingStart = null;
                        entry.PendingEnd = null;
                        change.Moved++;
                        continue;
                    }
                    var newStart = Convert(before, after, entry.Start);
                    var newEnd = entry.End == null ? null : Convert(before, after, entry.End);
                    if (newStart == null || (entry.End != null && newEnd == null)
                        || (newEnd != null && after.AxisEnd(newEnd) <= after.AxisStart(newStart)))
                    {
                        entry.PendingStart = before.Format(entry.Start);
                        entry.PendingEnd = entry.End == null ? null : before.Format(entry.End);
                        change.Pending++;
                        continue;
                    }
                    if (!newStart.SameAs(entry.Start) || (newEnd != null && !newEnd.SameAs(entry.End))) change.Moved++;
                    entry.Start = newStart;
                    entry.End = newEnd;
                }
                change.InfosBefore[item] = item.Timeline;
                change.InfosAfter[item] = info;
            }

            // L'époque, si elle se lit sur une frise de ce système.
            if (project.Epoch != null && !string.IsNullOrEmpty(project.Epoch.Notation))
            {
                var epochTimeline = project.FindById(project.Epoch.TimelineId);
                TimePoint at;
                if (epochTimeline != null && change.InfosBefore.ContainsKey(epochTimeline) && before.TryParse(project.Epoch.Notation, out at))
                {
                    var converted = Convert(before, after, at);
                    if (converted != null)
                    {
                        change.EpochBefore = project.Epoch.Notation;
                        change.EpochAfter = after.Format(converted);
                    }
                }
            }

            // Les dissonances des fiches, écrites dans ce système (12/11).
            DissonanceChanges.Build(project, before, after, change.DissonancesBefore, change.DissonancesAfter);

            // Les liens de date du texte : réécrits quand la date se lit toujours
            // mais ne s'écrit plus pareil (la notation a changé).
            if (titles.Count == 0) return change;
            change.Links = LinkHygiene.RewriteLinks(project, delegate(Link link)
            {
                string title, notation;
                if (!TimeLinks.Split(link.Target, out title, out notation)) return null;
                var mine = false;
                foreach (var known in titles) if (Model.Links.SameTitle(known, title)) { mine = true; break; }
                if (!mine) return null;
                TimePoint point;
                if (!before.TryParse(notation, out point)) return null; // déjà illisible avant : on n'y touche pas
                var converted = Convert(before, after, point);
                if (converted == null) { change.LinksUnreadable++; return null; }
                var written = after.Format(converted);
                if (written == notation) return null;
                change.LinksRewritten++;
                return TimeLinks.Markup(title, written, link.Text == notation ? null : link.Text);
            });
            return change;
        }

        public bool TouchesDates { get { return Moved > 0 || Pending > 0 || LinksRewritten > 0 || LinksUnreadable > 0 || EpochAfter != EpochBefore; } }
    }
}
