using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Un élément du projet relié à un événement : sa sorte
    /// (TimelineLinks) et son identifiant.</summary>
    public class EventLink
    {
        public string Kind;
        public string Id;

        public EventLink() { }
        public EventLink(string kind, string id) { Kind = kind; Id = id; }
    }

    /// <summary>Le lieu d'un événement, retrouvé : la carte, et son repère
    /// ou sa zone.</summary>
    public class EventPlace
    {
        public BinderItem Map;
        public MapMarker Marker;
        public MapZone Zone;

        public string ShapeId { get { return Marker != null ? Marker.Id : Zone != null ? Zone.Id : null; } }
        public string Name
        {
            get
            {
                var name = Marker != null ? Marker.Name : Zone != null ? Zone.Name : null;
                return string.IsNullOrWhiteSpace(name) ? (Marker != null ? "Repère" : "Zone") : name.Trim();
            }
        }
        /// <summary>La fiche liée au repère ou à la zone, ou null.</summary>
        public string SheetId { get { return Marker != null ? Marker.LinkId : Zone != null ? Zone.LinkId : null; } }
        /// <summary>« Port-Brume (Carte du Nord) ».</summary>
        public string Label { get { return Name + (Map == null || string.IsNullOrEmpty(Map.Title) ? "" : " (" + Map.Title + ")"); } }
    }

    /// <summary>Un tag d'événement et le nombre d'événements qui le portent.</summary>
    public class EventTagCount
    {
        public string Tag;
        public int Count;
    }

    /// <summary>L'âge d'une fiche à la date d'un événement : « 23 ans »,
    /// « Décédée à 80 ans · depuis 3 ans », « Naît dans 5 ans ».</summary>
    public class EventAge
    {
        public BinderItem Sheet;
        public string Label;
    }

    /// <summary>CE QU'UN ÉVÉNEMENT RELIE (21/11) : plusieurs éléments du
    /// projet (le premier reste LinkKind/LinkId, les autres suivent dans
    /// More), UN lieu — un repère ou une zone d'une carte — et des tags
    /// libres. Tout est facultatif : un événement sans rien de cela se lit
    /// et s'enregistre comme avant.</summary>
    public static partial class Timelines
    {
        /// <summary>Plafond d'éléments par événement : au-delà, c'est un tag qu'il faut.</summary>
        public const int MaxElements = 24;

        /// <summary>Tous les éléments de l'événement, le premier d'abord,
        /// sans doublon ni entrée vide.</summary>
        public static List<EventLink> LinksOf(TimelineEvent entry)
        {
            var links = new List<EventLink>();
            if (entry == null) return links;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (entry.LinkKind != null && entry.LinkId != null && seen.Add(entry.LinkId)) links.Add(new EventLink(entry.LinkKind, entry.LinkId));
            if (entry.More != null)
                foreach (var link in entry.More)
                    if (link != null && link.Kind != null && link.Id != null && seen.Add(link.Id)) links.Add(new EventLink(link.Kind, link.Id));
            return links;
        }

        /// <summary>Pose la liste des éléments : le premier devient l'élément
        /// principal, les autres le suivent. Doublons et vides écartés.</summary>
        public static void SetLinks(TimelineEvent entry, IEnumerable<EventLink> links)
        {
            if (entry == null) return;
            entry.LinkKind = null;
            entry.LinkId = null;
            entry.More = new List<EventLink>();
            if (links == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in links)
            {
                if (link == null || link.Kind == null || link.Id == null || !seen.Add(link.Id)) continue;
                if (seen.Count > MaxElements) break;
                if (entry.LinkId == null) { entry.LinkKind = link.Kind; entry.LinkId = link.Id; }
                else entry.More.Add(new EventLink(link.Kind, link.Id));
            }
        }

        /// <summary>L'événement relie-t-il cet élément (premier ou suivant) ?</summary>
        public static bool HasLink(TimelineEvent entry, string itemId)
        {
            if (entry == null || itemId == null) return false;
            if (entry.LinkId == itemId) return true;
            if (entry.More != null) foreach (var link in entry.More) if (link != null && link.Id == itemId) return true;
            return false;
        }

        /// <summary>Les éléments reliés qui existent encore, dans l'ordre.</summary>
        public static List<BinderItem> ElementsOf(Project project, TimelineEvent entry)
        {
            var items = new List<BinderItem>();
            if (project == null) return items;
            foreach (var link in LinksOf(entry))
            {
                var item = project.FindById(link.Id);
                if (item != null) items.Add(item);
            }
            return items;
        }

        /// <summary>« Keira, Port-Brume et 2 autres » : les éléments reliés,
        /// dits court pour une colonne de liste ; "" s'il n'y en a pas.</summary>
        public static string ElementsLabel(Project project, TimelineEvent entry, int shown = 2)
        {
            var items = ElementsOf(project, entry);
            if (items.Count == 0) return "";
            var names = new List<string>();
            for (var i = 0; i < items.Count && i < shown; i++) names.Add(items[i].Title ?? "");
            var rest = items.Count - names.Count;
            var text = string.Join(", ", names.ToArray());
            return rest <= 0 ? text : text + " et " + rest + (rest == 1 ? " autre" : " autres");
        }

        // ------------------------------------------------------------ le lieu

        /// <summary>Le lieu de l'événement, ou null : aucun, ou la carte, le
        /// repère ou la zone n'existent plus.</summary>
        public static EventPlace PlaceOf(Project project, TimelineEvent entry)
        {
            if (project == null || entry == null || entry.PlaceMapId == null || entry.PlaceShapeId == null) return null;
            var map = project.FindById(entry.PlaceMapId);
            if (map == null || map.Kind != ItemKind.Map || map.Map == null) return null;
            var marker = map.Map.FindMarker(entry.PlaceShapeId);
            if (marker != null) return new EventPlace { Map = map, Marker = marker };
            var zone = map.Map.FindZone(entry.PlaceShapeId);
            return zone == null ? null : new EventPlace { Map = map, Zone = zone };
        }

        /// <summary>Tous les lieux où poser un événement : les repères puis
        /// les zones de chaque carte, hors Corbeille, triés par nom.</summary>
        public static List<EventPlace> Places(Project project)
        {
            var places = new List<EventPlace>();
            if (project == null) return places;
            foreach (var map in Maps.All(project))
            {
                if (map.Map == null) continue;
                var own = new List<EventPlace>();
                foreach (var marker in map.Map.Markers) own.Add(new EventPlace { Map = map, Marker = marker });
                foreach (var zone in map.Map.Zones) own.Add(new EventPlace { Map = map, Zone = zone });
                own.Sort(delegate(EventPlace a, EventPlace b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                places.AddRange(own);
            }
            return places;
        }

        /// <summary>Les événements posés sur ce repère ou cette zone, toutes
        /// frises confondues.</summary>
        public static List<KeyValuePair<BinderItem, TimelineEvent>> EventsAt(Project project, string mapId, string shapeId)
        {
            var found = new List<KeyValuePair<BinderItem, TimelineEvent>>();
            if (project == null || mapId == null || shapeId == null) return found;
            foreach (var timeline in All(project))
                if (timeline.Timeline != null)
                    foreach (var entry in timeline.Timeline.Events)
                        if (entry.PlaceMapId == mapId && entry.PlaceShapeId == shapeId) found.Add(new KeyValuePair<BinderItem, TimelineEvent>(timeline, entry));
            return found;
        }

        // ------------------------------------------------------------ les tags

        /// <summary>Ce que l'auteur a saisi (« guerre, Nord ; guerre ») rendu
        /// en tags : séparés par virgule ou point-virgule, sans « # » de
        /// tête, sans doublon (casse et accents ignorés), douze au plus.</summary>
        public static List<string> ParseTags(string text)
        {
            var tags = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tag = part.Trim().TrimStart('#').Trim();
                if (tag.Length == 0) continue;
                if (tag.Length > 40) tag = tag.Substring(0, 40).Trim();
                if (!seen.Add(TutorialParser.Fold(tag))) continue;
                tags.Add(tag);
                if (tags.Count >= 12) break;
            }
            return tags;
        }

        public static string TagsText(TimelineEvent entry)
        {
            return entry == null || entry.Tags == null ? "" : string.Join(", ", entry.Tags.ToArray());
        }

        public static bool HasTag(TimelineEvent entry, string tag)
        {
            if (entry == null || entry.Tags == null || string.IsNullOrEmpty(tag)) return false;
            var wanted = TutorialParser.Fold(tag);
            foreach (var own in entry.Tags) if (TutorialParser.Fold(own ?? "") == wanted) return true;
            return false;
        }

        /// <summary>Les tags que portent ces événements, triés, chacun avec
        /// son compte — le menu du filtre.</summary>
        public static List<EventTagCount> TagsOf(IEnumerable<TimelineEvent> events)
        {
            var counts = new List<EventTagCount>();
            if (events == null) return counts;
            var byKey = new Dictionary<string, EventTagCount>(StringComparer.Ordinal);
            foreach (var entry in events)
            {
                if (entry == null || entry.Tags == null) continue;
                foreach (var tag in entry.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag)) continue;
                    var key = TutorialParser.Fold(tag);
                    EventTagCount count;
                    if (!byKey.TryGetValue(key, out count))
                    {
                        count = new EventTagCount { Tag = tag };
                        byKey[key] = count;
                        counts.Add(count);
                    }
                    count.Count++;
                }
            }
            counts.Sort(delegate(EventTagCount a, EventTagCount b) { return string.Compare(a.Tag, b.Tag, StringComparison.CurrentCultureIgnoreCase); });
            return counts;
        }

        // ------------------------------------------------------------- l'âge

        /// <summary>L'âge, à la date de l'événement, de chaque fiche qu'il
        /// relie et dont la vie est datée (naissance, décès, dissonances).
        /// Une fiche sans date n'y figure pas ; les naissances et les décès
        /// nés d'une fiche non plus (leur titre le dit déjà).</summary>
        public static List<EventAge> AgesOf(Project project, TimeSystem system, TimelineEvent entry)
        {
            var ages = new List<EventAge>();
            if (project == null || system == null || entry == null || entry.Derived || entry.IsPending || entry.Start == null) return ages;
            foreach (var item in ElementsOf(project, entry))
            {
                if (item.Kind != ItemKind.Sheet) continue;
                LifeStatus status;
                try { status = Epochs.Evaluate(project, item, system, entry.Start); }
                catch (Exception) { continue; }
                if (status == null || string.IsNullOrEmpty(status.Label)) continue;
                if (status.State != LifeState.Alive && status.State != LifeState.Dead && status.State != LifeState.Unborn && status.State != LifeState.OutOfTime) continue;
                if (status.State == LifeState.Alive && !status.HasAge) continue;
                ages.Add(new EventAge { Sheet = item, Label = status.Label });
            }
            return ages;
        }

        /// <summary>« Keira : 23 ans · Abel : 41 ans », ou "".</summary>
        public static string AgesLabel(Project project, TimeSystem system, TimelineEvent entry)
        {
            var parts = new List<string>();
            foreach (var age in AgesOf(project, system, entry)) parts.Add(age.Sheet.Title + " : " + age.Label);
            return string.Join(" · ", parts.ToArray());
        }
    }
}
