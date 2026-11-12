using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    // LES DISSONANCES (1.2.0, 12/11/2026, Rémi) : ce qui fait qu'un personnage
    // ne vieillit pas comme sa frise — un saut dans le temps (les années de
    // l'absence ne comptent pas), ou une arrivée dans un monde à une autre
    // temporalité (un âge donné à une date donnée). Portées par la fiche,
    // lues par l'Époque.

    /// <summary>Une dissonance d'une fiche. Les dates gardent ce qui est ÉCRIT
    /// (comme une brique en attente) et se relisent dans leur système.</summary>
    public class Dissonance
    {
        public const string Exclusion = "exclusion"; // de From à To, le temps ne passe pas pour la fiche
        public const string Reference = "reference"; // à From, la fiche a Age ans ; avant, elle est absente

        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = Exclusion;
        public string SystemId;
        public string From = "";
        public string To = "";
        public long Age;

        public bool IsReference { get { return Kind == Reference; } }

        public Dissonance Clone()
        {
            return new Dissonance { Id = Id, Kind = Kind, SystemId = SystemId, From = From, To = To, Age = Age };
        }

        public bool SameAs(Dissonance other)
        {
            return other != null && Kind == other.Kind && SystemId == other.SystemId && From == other.From && To == other.To && Age == other.Age;
        }

        /// <summary>Pourquoi cette dissonance ne se lit pas dans son système ;
        /// null si elle est complète.</summary>
        public string Problem(TimeSystem system)
        {
            if (system == null) return "Système temporel introuvable.";
            TimePoint from, to;
            if (!system.TryParse(From, out from)) return IsReference ? "La date de référence ne se lit pas." : "La date de départ ne se lit pas.";
            if (IsReference) return Age < 0 ? "L'âge de référence est négatif." : null;
            if (!system.TryParse(To, out to)) return "La date d'arrivée ne se lit pas.";
            if (system.AxisStart(to) <= system.AxisStart(from)) return "L'arrivée doit suivre le départ.";
            return null;
        }
    }

    public static partial class Epochs
    {
        // Un point d'où l'âge se compte : la naissance (0 an) ou un point de référence.
        private class Anchor
        {
            public TimePoint At;
            public long Age;
            public bool IsBirth;
        }

        private class Gap
        {
            public TimePoint From, To;
        }

        /// <summary>L'âge de la fiche à une date, dissonances comprises. Faux si
        /// rien ne permet de le compter (ni naissance ni référence atteinte).
        /// outOfTime : la date tombe dans une période d'exclusion (l'âge rendu
        /// est celui du départ, gap.From).</summary>
        private static bool AgeOn(TimeSystem system, List<Anchor> anchors, List<Gap> gaps, TimePoint at,
            out long age, out bool excluded, out Gap within)
        {
            age = 0;
            excluded = false;
            within = null;
            Anchor anchor = null;
            foreach (var candidate in anchors) // triées : la dernière atteinte l'emporte
                if (system.AxisEnd(at) > system.AxisStart(candidate.At)) anchor = candidate;
            if (anchor == null) return false;
            var until = at;
            long skipped = 0;
            foreach (var gap in gaps)
            {
                if (system.AxisStart(gap.From) < system.AxisStart(anchor.At)) continue; // d'avant ce point de départ
                if (system.AxisStart(at) <= system.AxisStart(gap.From)) continue;       // pas encore parti
                if (system.AxisStart(at) < system.AxisStart(gap.To))
                {
                    within = gap;
                    until = gap.From;
                    continue;
                }
                skipped += Math.Max(0, system.AgeAt(gap.From, gap.To));
                excluded = true;
            }
            age = Math.Max(0, anchor.Age + system.AgeAt(anchor.At, until) - skipped);
            if (within != null) excluded = false;
            return true;
        }


        public static LifeStatus Evaluate(Project project, BinderItem sheet, TimeSystem system, TimePoint at)
        {
            var status = new LifeStatus();
            var birth = DateOf(project, sheet, SheetDefaults.FieldBirthDate, system);
            var death = DateOf(project, sheet, SheetDefaults.FieldDeathDate, system);

            var anchors = new List<Anchor>();
            var gaps = new List<Gap>();
            var references = 0;
            if (birth != null) anchors.Add(new Anchor { At = birth, IsBirth = true });
            foreach (var entry in sheet.Dissonances)
            {
                if (entry.SystemId != system.Id || entry.Problem(system) != null) continue;
                TimePoint from, to;
                system.TryParse(entry.From, out from);
                if (entry.IsReference) { anchors.Add(new Anchor { At = from, Age = entry.Age }); references++; continue; }
                system.TryParse(entry.To, out to);
                gaps.Add(new Gap { From = from, To = to });
            }
            anchors.Sort(delegate(Anchor a, Anchor b) { return system.AxisStart(a.At).CompareTo(system.AxisStart(b.At)); });
            gaps.Sort(delegate(Gap a, Gap b) { return system.AxisStart(a.From).CompareTo(system.AxisStart(b.From)); });
            if (anchors.Count == 0 && death == null && gaps.Count == 0) return status;

            long age;
            bool excluded;
            Gap within;
            if (death != null && system.AxisStart(at) >= system.AxisEnd(death))
            {
                status.State = LifeState.Dead;
                status.Label = Word(project, sheet, "Mort", "Morte", "Mort·e");
                if (AgeOn(system, anchors, gaps, death, out age, out excluded, out within))
                {
                    status.HasAge = true;
                    status.Age = age;
                    status.Excluded = excluded;
                    status.Label += " à " + TimeSystem.YearsLabel(age);
                }
                var since = at.Year - death.Year;
                if (since > 0) status.Label += " · depuis " + TimeSystem.YearsLabel(since);
                return status;
            }
            if (!AgeOn(system, anchors, gaps, at, out age, out excluded, out within))
            {
                if (anchors.Count > 0 && anchors[0].IsBirth)
                {
                    status.State = LifeState.Unborn;
                    var wait = birth.Year - at.Year;
                    status.Label = wait <= 0 ? "Pas encore " + Word(project, sheet, "né", "née", "né·e") : "Naît dans " + TimeSystem.YearsLabel(wait);
                    return status;
                }
                if (references > 0)
                {
                    // Un point de référence pas encore atteint : la fiche n'est pas
                    // « non née », elle est ailleurs.
                    status.State = LifeState.Absent;
                    status.Label = Word(project, sheet, "Absent", "Absente", "Absent·e");
                    return status;
                }
                foreach (var gap in gaps)
                    if (system.AxisStart(at) > system.AxisStart(gap.From) && system.AxisStart(at) < system.AxisStart(gap.To))
                    {
                        status.State = LifeState.OutOfTime;
                        status.Label = "Hors-temps";
                        return status;
                    }
                if (death == null) return status;
                // Une date de décès seule, pas encore atteinte : en vie, d'âge inconnu.
                status.State = LifeState.Alive;
                status.Label = "En vie";
                return status;
            }
            status.HasAge = true;
            status.Age = age;
            if (within != null)
            {
                status.State = LifeState.OutOfTime;
                status.Detail = TimeSystem.YearsLabel(age) + " en " + system.Format(within.From);
                status.Label = "Hors-temps · " + status.Detail;
                return status;
            }
            status.State = LifeState.Alive;
            status.Excluded = excluded;
            status.Label = TimeSystem.YearsLabel(age) + (excluded ? " (excl.)" : "");
            return status;
        }

        /// <summary>La note d'époque du bandeau d'une fiche : « 33 ans en E5.912 »,
        /// « Hors-temps en E5.520 · 20 ans en E5.500 ».</summary>
        public static string BannerNote(LifeStatus status, string when)
        {
            if (status.State == LifeState.OutOfTime)
                return "Hors-temps en " + when + (status.Detail.Length > 0 ? " · " + status.Detail : "");
            return status.Label + " en " + when;
        }
    }

    /// <summary>Les dissonances relues quand la temporalité change : leurs dates
    /// se réécrivent dans la nouvelle notation (ce qui ne s'y lit plus reste
    /// tel quel, signalé dans la fenêtre Dissonances).</summary>
    public static class DissonanceChanges
    {
        public static void Build(Project project, TimeSystem before, TimeSystem after,
            Dictionary<BinderItem, List<Dissonance>> listsBefore, Dictionary<BinderItem, List<Dissonance>> listsAfter)
        {
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Dissonances.Count == 0) continue;
                var copies = new List<Dissonance>();
                var changed = false;
                foreach (var entry in sheet.Dissonances)
                {
                    var copy = entry.Clone();
                    copies.Add(copy);
                    if (entry.SystemId != before.Id) continue;
                    copy.From = Rewrite(before, after, entry.From);
                    if (!entry.IsReference) copy.To = Rewrite(before, after, entry.To);
                    if (!copy.SameAs(entry)) changed = true;
                }
                if (!changed) continue;
                var originals = new List<Dissonance>();
                foreach (var entry in sheet.Dissonances) originals.Add(entry.Clone());
                listsBefore[sheet] = originals;
                listsAfter[sheet] = copies;
            }
        }

        private static string Rewrite(TimeSystem before, TimeSystem after, string notation)
        {
            TimePoint point;
            if (!before.TryParse(notation, out point)) return notation;
            var converted = TimeSystemChange.Convert(before, after, point);
            return converted == null ? notation : after.Format(converted);
        }

        public static void Apply(Dictionary<BinderItem, List<Dissonance>> lists)
        {
            foreach (var pair in lists)
            {
                pair.Key.Dissonances.Clear();
                foreach (var entry in pair.Value) pair.Key.Dissonances.Add(entry.Clone());
            }
        }
    }
}
