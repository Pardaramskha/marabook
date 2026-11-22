using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Un rétrolien : un endroit du projet qui cite la fiche.</summary>
    public class Backlink
    {
        public const string Sheets = "sheet", Texts = "text", Events = "event", Maps = "map", Trees = "tree", Plans = "plan";

        /// <summary>Les groupes, dans l'ordre où ils se montrent.</summary>
        public static readonly string[] Groups = { Sheets, Texts, Events, Maps, Trees, Plans };

        public string Group = Sheets;
        public BinderItem Item;          // ce qu'un clic ouvre : la fiche, l'écrit, la frise, la carte…
        public string RefId;             // dans la frise, l'évènement ; sur la carte, le repère ou la zone
        public string Label = "";        // ce qui se lit : le titre de la source, ou celui de l'évènement
        public readonly List<string> Reasons = new List<string>(); // par quoi : « lien », « relation : Père », « champ Mentor »…

        public string Reason { get { return string.Join(" · ", Reasons.ToArray()); } }

        public static string GroupLabel(string group)
        {
            switch (group)
            {
                case Sheets: return "Fiches";
                case Texts: return "Écrits";
                case Events: return "Événements";
                case Maps: return "Cartes";
                case Trees: return "Arbres";
                case Plans: return "Plans";
                default: return "";
            }
        }
    }

    /// <summary>LES RÉTROLIENS (20/11) : tout ce qui, dans le projet, cite
    /// une fiche — les fiches (un [[lien]], une relation, un champ « fiche »),
    /// les écrits et les plans (un [[lien]]), les évènements des frises (la
    /// fiche assignée, ou un [[lien]] dans leur texte), les repères et les
    /// zones des cartes, et les arbres généalogiques où elle figure. La
    /// Corbeille ne compte pas. Calculé à la demande : un passage sur le
    /// projet, sans index.</summary>
    public static class Backlinks
    {
        public const string ByLink = "lien";

        public static List<Backlink> Of(Project project, BinderItem sheet)
        {
            var found = new List<Backlink>();
            if (project == null || sheet == null) return found;
            var title = sheet.Title ?? "";
            var byKey = new Dictionary<string, Backlink>(StringComparer.Ordinal);

            foreach (var item in project.AllItems())
            {
                if (ReferenceEquals(item, sheet) || item.Kind == ItemKind.Category) continue;
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;

                if (item.Kind == ItemKind.Sheet)
                {
                    foreach (var relation in item.Relations)
                        if (relation.TargetId == sheet.Id)
                            Add(found, byKey, Backlink.Sheets, item, null, item.Title, "relation" + (string.IsNullOrEmpty(relation.Kind) ? "" : " : " + RelationKinds.Canonical(relation.Kind)));
                    var template = project.TemplateOf(item);
                    if (template != null)
                        foreach (var field in template.Fields)
                        {
                            if (FieldKinds.Normalize(field.Kind) != FieldKinds.Sheet) continue;
                            string value;
                            if (item.FieldValues.TryGetValue(field.Id, out value) && value == sheet.Id)
                                Add(found, byKey, Backlink.Sheets, item, null, item.Title, "champ « " + field.Name + " »");
                        }
                    if (title.Length > 0 && Links.LinksTo(item.SearchText(), title)) Add(found, byKey, Backlink.Sheets, item, null, item.Title, ByLink);
                }
                else if (item.Kind == ItemKind.Text)
                {
                    if (title.Length > 0 && Links.LinksTo(item.SearchText(), title)) Add(found, byKey, Backlink.Texts, item, null, item.Title, ByLink);
                    // Les fiches de scène (21/11) : point de vue, lieu ou présence dans une scène de cet écrit.
                    var many = item.Scenes.Count > 1;
                    for (var s = 0; s < item.Scenes.Count; s++)
                    {
                        var meta = item.Scenes[s];
                        var where = many ? ", scène " + (s + 1) : "";
                        if (meta.PovId == sheet.Id) Add(found, byKey, Backlink.Texts, item, null, item.Title, "point de vue" + where);
                        if (meta.PlaceSheetId == sheet.Id) Add(found, byKey, Backlink.Texts, item, null, item.Title, "lieu" + where);
                        if (meta.CharacterIds.Contains(sheet.Id)) Add(found, byKey, Backlink.Texts, item, null, item.Title, "présence" + where);
                    }
                }
                else if (item.Kind == ItemKind.Plan)
                {
                    if (title.Length > 0 && Links.LinksTo(item.SearchText(), title)) Add(found, byKey, Backlink.Plans, item, null, item.Title, ByLink);
                }
                else if (item.Kind == ItemKind.Timeline && item.Timeline != null)
                {
                    foreach (var entry in item.Timeline.Events)
                    {
                        if (entry.Derived) continue;
                        var label = string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title;
                        if (Timelines.HasLink(entry, sheet.Id)) Add(found, byKey, Backlink.Events, item, entry.Id, label, "assignée — " + item.Title);
                        else if (title.Length > 0 && Links.LinksTo((entry.Title ?? "") + "\n" + (entry.Description ?? ""), title))
                            Add(found, byKey, Backlink.Events, item, entry.Id, label, ByLink + " — " + item.Title);
                    }
                }
                else if (item.Kind == ItemKind.Map && item.Map != null)
                {
                    foreach (var marker in item.Map.Markers)
                        if (marker.LinkId == sheet.Id)
                            Add(found, byKey, Backlink.Maps, item, marker.Id, item.Title, "repère" + (string.IsNullOrEmpty(marker.Name) ? "" : " « " + marker.Name + " »"));
                    foreach (var zone in item.Map.Zones)
                        if (zone.LinkId == sheet.Id)
                            Add(found, byKey, Backlink.Maps, item, zone.Id, item.Title, "zone" + (string.IsNullOrEmpty(zone.Name) ? "" : " « " + zone.Name + " »"));
                }
            }

            // Les arbres : ceux des fiches qui figurent dans le sien — la parenté se lit dans les deux sens.
            foreach (var node in Genealogy.Build(project, sheet))
            {
                if (node.IsSelf || node.TargetId == null) continue;
                var relative = project.FindById(node.TargetId);
                if (relative == null || relative.Kind != ItemKind.Sheet) continue;
                Add(found, byKey, Backlink.Trees, relative, null, "Arbre de " + relative.Title, node.Kind ?? "");
            }

            found.Sort(delegate(Backlink a, Backlink b)
            {
                var ga = Array.IndexOf(Backlink.Groups, a.Group);
                var gb = Array.IndexOf(Backlink.Groups, b.Group);
                if (ga != gb) return ga.CompareTo(gb);
                return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return found;
        }

        private static void Add(List<Backlink> found, Dictionary<string, Backlink> byKey, string group, BinderItem item, string refId, string label, string reason)
        {
            var key = group + "|" + item.Id + "|" + (refId ?? "");
            Backlink link;
            if (!byKey.TryGetValue(key, out link))
            {
                link = new Backlink { Group = group, Item = item, RefId = refId, Label = label ?? "" };
                byKey[key] = link;
                found.Add(link);
            }
            if (!string.IsNullOrEmpty(reason) && !link.Reasons.Contains(reason)) link.Reasons.Add(reason);
        }

        /// <summary>Ceux d'un groupe.</summary>
        public static List<Backlink> In(List<Backlink> links, string group)
        {
            var list = new List<Backlink>();
            if (links != null) foreach (var link in links) if (link.Group == group) list.Add(link);
            return list;
        }

        /// <summary>« 3 fiches, 2 écrits, 1 évènement ».</summary>
        public static string Summary(List<Backlink> links)
        {
            var parts = new List<string>();
            foreach (var group in Backlink.Groups)
            {
                var count = In(links, group).Count;
                if (count == 0) continue;
                string one, many;
                switch (group)
                {
                    case Backlink.Sheets: one = "fiche"; many = "fiches"; break;
                    case Backlink.Texts: one = "écrit"; many = "écrits"; break;
                    case Backlink.Events: one = "événement"; many = "événements"; break;
                    case Backlink.Maps: one = "carte"; many = "cartes"; break;
                    case Backlink.Trees: one = "arbre"; many = "arbres"; break;
                    default: one = "plan"; many = "plans"; break;
                }
                parts.Add(count + " " + (count > 1 ? many : one));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
