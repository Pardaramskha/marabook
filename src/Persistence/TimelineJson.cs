using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Les frises dans le .plot (v46, 1.2.0) : les systèmes temporels
    /// du manifeste (« timeSystems »), l'époque de la bibliothèque (« epoch »)
    /// et la frise d'un élément (« timeline »). Chaque champ a son défaut : un
    /// champ absent se relit comme au premier jour.</summary>
    public static class TimelineJson
    {
        // ------------------------------------------------------------ systèmes

        public static List<object> BuildSystems(List<TimeSystem> systems)
        {
            var list = new List<object>();
            foreach (var system in systems)
            {
                var o = new Dictionary<string, object>();
                o["id"] = system.Id;
                o["name"] = system.Name;
                if (system.Segmented) o["segmented"] = true;
                if (system.SegmentWord != "Ère") o["segmentWord"] = system.SegmentWord ?? "";
                if (system.Cumulative) o["cumulative"] = true;
                if (system.Segments.Count > 0)
                {
                    var segments = new List<object>();
                    foreach (var segment in system.Segments)
                    {
                        var s = new Dictionary<string, object>();
                        s["id"] = segment.Id;
                        s["name"] = segment.Name ?? "";
                        if (!string.IsNullOrEmpty(segment.Code)) s["code"] = segment.Code;
                        if (segment.Length > 0) s["length"] = (double)segment.Length;
                        if (segment.Color != null) s["color"] = segment.Color;
                        segments.Add(s);
                    }
                    o["segments"] = segments;
                }
                o["yearPattern"] = system.YearPattern ?? "{a}";
                if (!string.IsNullOrEmpty(system.NegativePattern)) o["negativePattern"] = system.NegativePattern;
                o["datePattern"] = system.DatePattern ?? "";
                if (system.YearZero) o["yearZero"] = true;
                var months = new List<object>();
                foreach (var month in system.Months)
                {
                    var m = new Dictionary<string, object>();
                    m["name"] = month.Name ?? "";
                    m["days"] = month.Days;
                    months.Add(m);
                }
                o["months"] = months;
                if (system.Leaps.Count > 0)
                {
                    var leaps = new List<object>();
                    foreach (var leap in system.Leaps)
                    {
                        var l = new Dictionary<string, object>();
                        l["every"] = leap.Every;
                        if (leap.Except > 0) l["except"] = leap.Except;
                        if (leap.Unless > 0) l["unless"] = leap.Unless;
                        l["month"] = leap.Month;
                        l["days"] = leap.Days;
                        leaps.Add(l);
                    }
                    o["leaps"] = leaps;
                }
                if (system.WeekDays.Count > 0) o["weekDays"] = new List<object>(system.WeekDays.ToArray());
                if (system.HoursPerDay != 24) o["hoursPerDay"] = system.HoursPerDay;
                if (system.MinutesPerHour != 60) o["minutesPerHour"] = system.MinutesPerHour;
                list.Add(o);
            }
            return list;
        }

        public static List<TimeSystem> ReadSystems(object node)
        {
            var systems = new List<TimeSystem>();
            var list = Json.AsList(node);
            if (list == null) return systems;
            foreach (var entry in list)
            {
                var o = Json.AsObject(entry);
                if (o == null) continue;
                var system = new TimeSystem();
                var id = Json.AsString(Json.Field(o, "id"));
                if (!string.IsNullOrEmpty(id)) system.Id = id;
                system.Name = Json.AsString(Json.Field(o, "name")) ?? "Calendrier";
                system.Segmented = Json.AsBool(Json.Field(o, "segmented"), false);
                system.SegmentWord = Json.AsString(Json.Field(o, "segmentWord")) ?? "Ère";
                system.Cumulative = Json.AsBool(Json.Field(o, "cumulative"), false);
                var segments = Json.AsList(Json.Field(o, "segments"));
                if (segments != null)
                    foreach (var segmentNode in segments)
                    {
                        var s = Json.AsObject(segmentNode);
                        if (s == null) continue;
                        var segment = new TimeSegment();
                        var segmentId = Json.AsString(Json.Field(s, "id"));
                        if (!string.IsNullOrEmpty(segmentId)) segment.Id = segmentId;
                        segment.Name = Json.AsString(Json.Field(s, "name")) ?? "";
                        segment.Code = Json.AsString(Json.Field(s, "code")) ?? "";
                        segment.Length = Math.Max(0, (long)Json.AsDouble(Json.Field(s, "length"), 0));
                        segment.Color = Json.AsString(Json.Field(s, "color"));
                        system.Segments.Add(segment);
                    }
                system.YearPattern = Json.AsString(Json.Field(o, "yearPattern")) ?? "{a}";
                system.NegativePattern = Json.AsString(Json.Field(o, "negativePattern")) ?? "";
                system.DatePattern = Json.AsString(Json.Field(o, "datePattern")) ?? "{j} {M} {année}";
                system.YearZero = Json.AsBool(Json.Field(o, "yearZero"), false);
                var months = Json.AsList(Json.Field(o, "months"));
                if (months != null)
                    foreach (var monthNode in months)
                    {
                        var m = Json.AsObject(monthNode);
                        if (m == null) continue;
                        system.Months.Add(new TimeMonth
                        {
                            Name = Json.AsString(Json.Field(m, "name")) ?? "",
                            Days = Math.Max(1, Json.AsInt(Json.Field(m, "days"), 30))
                        });
                    }
                if (system.Months.Count == 0) system.Months.Add(new TimeMonth { Name = "Année", Days = 365 }); // jamais sans mois
                var leaps = Json.AsList(Json.Field(o, "leaps"));
                if (leaps != null)
                    foreach (var leapNode in leaps)
                    {
                        var l = Json.AsObject(leapNode);
                        if (l == null) continue;
                        system.Leaps.Add(new LeapRule
                        {
                            Every = Math.Max(1, Json.AsInt(Json.Field(l, "every"), 4)),
                            Except = Math.Max(0, Json.AsInt(Json.Field(l, "except"), 0)),
                            Unless = Math.Max(0, Json.AsInt(Json.Field(l, "unless"), 0)),
                            Month = Math.Max(0, Json.AsInt(Json.Field(l, "month"), 0)),
                            Days = Json.AsInt(Json.Field(l, "days"), 1)
                        });
                    }
                var weekDays = Json.AsList(Json.Field(o, "weekDays"));
                if (weekDays != null)
                    foreach (var day in weekDays)
                        if (day is string) system.WeekDays.Add((string)day);
                system.HoursPerDay = Math.Max(1, Json.AsInt(Json.Field(o, "hoursPerDay"), 24));
                system.MinutesPerHour = Math.Max(1, Json.AsInt(Json.Field(o, "minutesPerHour"), 60));
                systems.Add(system);
            }
            return systems;
        }

        // --------------------------------------------------------------- époque

        public static Dictionary<string, object> BuildEpoch(EpochState epoch)
        {
            if (epoch == null || (!epoch.Enabled && string.IsNullOrEmpty(epoch.TimelineId) && string.IsNullOrEmpty(epoch.Notation))) return null;
            var o = new Dictionary<string, object>();
            if (epoch.Enabled) o["enabled"] = true;
            if (epoch.TimelineId != null) o["timeline"] = epoch.TimelineId;
            o["date"] = epoch.Notation ?? "";
            return o;
        }

        public static EpochState ReadEpoch(object node)
        {
            var epoch = new EpochState();
            var o = Json.AsObject(node);
            if (o == null) return epoch;
            epoch.Enabled = Json.AsBool(Json.Field(o, "enabled"), false);
            epoch.TimelineId = Json.AsString(Json.Field(o, "timeline"));
            epoch.Notation = Json.AsString(Json.Field(o, "date")) ?? "";
            return epoch;
        }

        // ---------------------------------------------------------------- frise

        private static Dictionary<string, object> BuildPoint(TimePoint point)
        {
            var o = new Dictionary<string, object>();
            o["y"] = (double)point.Year;
            if (point.Month > 0) o["m"] = point.Month;
            if (point.Day > 0) o["d"] = point.Day;
            if (point.Hour >= 0) { o["h"] = point.Hour; o["n"] = point.Minute; }
            return o;
        }

        private static TimePoint ReadPoint(object node)
        {
            var o = Json.AsObject(node);
            if (o == null) return null;
            var point = new TimePoint((long)Json.AsDouble(Json.Field(o, "y"), 0));
            point.Month = Math.Max(0, Json.AsInt(Json.Field(o, "m"), 0));
            point.Day = point.Month > 0 ? Math.Max(0, Json.AsInt(Json.Field(o, "d"), 0)) : 0;
            point.Hour = point.Day > 0 ? Json.AsInt(Json.Field(o, "h"), -1) : -1;
            point.Minute = point.Hour >= 0 ? Math.Max(0, Json.AsInt(Json.Field(o, "n"), 0)) : 0;
            return point;
        }

        public static Dictionary<string, object> BuildTimeline(TimelineInfo timeline)
        {
            var o = new Dictionary<string, object>();
            if (timeline.SystemId != null) o["system"] = timeline.SystemId;
            var types = new List<object>();
            foreach (var type in timeline.Types)
            {
                var t = new Dictionary<string, object>();
                t["id"] = type.Id;
                t["name"] = type.Name ?? "";
                t["color"] = type.Color ?? "#5B67D8";
                types.Add(t);
            }
            o["types"] = types;
            var events = new List<object>();
            foreach (var entry in timeline.Events)
            {
                var e = new Dictionary<string, object>();
                e["id"] = entry.Id;
                e["title"] = entry.Title ?? "";
                if (!string.IsNullOrEmpty(entry.Description)) e["text"] = entry.Description;
                if (entry.TypeId != null) e["type"] = entry.TypeId;
                e["start"] = BuildPoint(entry.Start ?? new TimePoint());
                if (entry.End != null) e["end"] = BuildPoint(entry.End);
                if (entry.LinkKind != null && entry.LinkId != null)
                {
                    e["linkKind"] = entry.LinkKind;
                    e["link"] = entry.LinkId;
                }
                events.Add(e);
            }
            o["events"] = events;
            return o;
        }

        public static TimelineInfo ReadTimeline(object node)
        {
            var timeline = new TimelineInfo();
            var o = Json.AsObject(node);
            if (o == null) { timeline.Types = TimelineInfo.DefaultTypes(); return timeline; }
            timeline.SystemId = Json.AsString(Json.Field(o, "system"));
            var types = Json.AsList(Json.Field(o, "types"));
            if (types != null)
                foreach (var typeNode in types)
                {
                    var t = Json.AsObject(typeNode);
                    if (t == null) continue;
                    var type = new TimelineEventType();
                    var id = Json.AsString(Json.Field(t, "id"));
                    if (!string.IsNullOrEmpty(id)) type.Id = id;
                    type.Name = Json.AsString(Json.Field(t, "name")) ?? "";
                    type.Color = Json.AsString(Json.Field(t, "color")) ?? "#5B67D8";
                    timeline.Types.Add(type);
                }
            var events = Json.AsList(Json.Field(o, "events"));
            if (events != null)
                foreach (var eventNode in events)
                {
                    var e = Json.AsObject(eventNode);
                    if (e == null) continue;
                    var entry = new TimelineEvent();
                    var id = Json.AsString(Json.Field(e, "id"));
                    if (!string.IsNullOrEmpty(id)) entry.Id = id;
                    entry.Title = Json.AsString(Json.Field(e, "title")) ?? "";
                    entry.Description = Json.AsString(Json.Field(e, "text")) ?? "";
                    entry.TypeId = Json.AsString(Json.Field(e, "type"));
                    entry.Start = ReadPoint(Json.Field(e, "start")) ?? new TimePoint();
                    entry.End = ReadPoint(Json.Field(e, "end"));
                    entry.LinkKind = Json.AsString(Json.Field(e, "linkKind"));
                    entry.LinkId = Json.AsString(Json.Field(e, "link"));
                    if (entry.LinkKind == null || entry.LinkId == null) { entry.LinkKind = null; entry.LinkId = null; }
                    timeline.Events.Add(entry);
                }
            return timeline;
        }
    }
}
