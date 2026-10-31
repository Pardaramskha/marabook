using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Les natures d'arêtes du GRAPHE RELATIONNEL (1.1.0, 28/10) :
    /// ce qui relie déjà les éléments d'un projet, sans nouvelle saisie.</summary>
    public enum GraphEdgeKind
    {
        Relation,  // une relation typée entre deux fiches (schéma relationnel)
        Wiki,      // un lien [[…]] d'un élément vers un autre
        Presence,  // un nom de fiche qui apparaît dans un écrit (compte = poids)
        Structure, // un livre et ses écrits
        Plan,      // un plan et les écrits de ses colonnes
        Tag        // deux fiches qui partagent un tag
    }

    public class GraphNode
    {
        public string Id;
        public string Title = "";
        public ItemKind Kind;
        public string CategoryId; // fiches : leur catégorie (couleur, icône)
        public string Color;      // la couleur de carte (#RRGGBB), null = aucune
        public int Degree;        // arêtes incidentes (toutes natures)
        public int Presence;      // écrits : fiches présentes ; fiches : écrits où elle paraît

        public bool IsSheet { get { return Kind == ItemKind.Sheet; } }
        public bool IsText { get { return Kind == ItemKind.Text; } }
    }

    public class GraphEdge
    {
        public string From, To;
        public GraphEdgeKind Kind;
        public string Label = ""; // la nature d'une relation, le tag partagé
        public int Weight = 1;    // présence : le nombre d'occurrences
        public int First = -1;    // présence : l'index du premier nom dans l'écrit (l'ordre d'entrée en scène, 31/10)

        public string Other(string id) { return From == id ? To : From; }
    }

    /// <summary>Ce qu'un élément apporte au graphe, PRIS SUR LE FIL D'INTERFACE
    /// (GraphInput.Capture) : titres, relations, cibles des liens, noms et
    /// texte brut. Le calcul lui-même (présences, arêtes, disposition) peut
    /// alors tourner ailleurs sans toucher au projet.</summary>
    public class GraphEntry
    {
        public string Id, Title, CategoryId, Color, BookId, ParentId;
        public ItemKind Kind;
        public int Order;                          // l'ordre du récit pour un écrit, -1 sinon
        public List<string> Tags = new List<string>();
        public List<KeyValuePair<string, string>> Relations = new List<KeyValuePair<string, string>>(); // (id cible, nature)
        public List<string> LinkTargets = new List<string>();  // ids des éléments visés par [[…]]
        public List<string> LinkedTexts = new List<string>();  // plans : écrits reliés aux colonnes
        public List<string> Names;                              // fiches : les noms cherchés (Presence.NamesOf)
        public string Plain;                                    // écrits : le texte brut
    }

    public class GraphOptions
    {
        public bool Relations = true, Wiki = true, Presence = true, Structure = true, Plans = true, Tags = false;
        public bool Texts = true;  // les écrits (et livres, plans) comme nœuds ; false = fiches seules
    }

    public static class GraphInput
    {
        /// <summary>Photographie le projet pour le graphe : fiches (hors
        /// corbeille), écrits du récit, livres et plans. Les liens [[…]] sont
        /// résolus ici par leur titre (FindByTitle), comme la navigation.</summary>
        public static List<GraphEntry> Capture(Project project)
        {
            var entries = new List<GraphEntry>();
            if (project == null) return entries;
            var trash = project.Trash;
            var order = new Dictionary<string, int>();
            var writings = Presence.Writings(project);
            for (var i = 0; i < writings.Count; i++) order[writings[i].Id] = i;
            var resolved = new Dictionary<string, string>(); // titre → id (ou null)
            foreach (var item in project.AllItems())
            {
                if (item.IsCategory) continue;
                if (trash != null && (item == trash || item.IsDescendantOf(trash))) continue;
                int rank;
                var isWriting = order.TryGetValue(item.Id, out rank);
                if (item.Kind == ItemKind.Text && !isWriting) continue; // pages extra, écrits hors récit
                if (item.Kind != ItemKind.Sheet && item.Kind != ItemKind.Text && item.Kind != ItemKind.Book && item.Kind != ItemKind.Plan) continue;
                var entry = new GraphEntry
                {
                    Id = item.Id,
                    Title = item.Title ?? "",
                    Kind = item.Kind,
                    CategoryId = item.CategoryId,
                    Color = item.CardColor,
                    ParentId = item.Parent == null ? null : item.Parent.Id,
                    Order = isWriting ? rank : -1
                };
                var book = item.Kind == ItemKind.Text ? item.EnclosingBook() : null;
                entry.BookId = book == null ? null : book.Id;
                entry.Tags.AddRange(item.Tags);
                if (item.Kind == ItemKind.Sheet)
                {
                    foreach (var relation in item.Relations)
                        if (!string.IsNullOrEmpty(relation.TargetId))
                            entry.Relations.Add(new KeyValuePair<string, string>(relation.TargetId, relation.Kind ?? ""));
                    entry.Names = Presence.NamesForGraph(item, project.TemplateOf(item)); // prénom du titre compris (28/10)
                }
                if (item.Kind == ItemKind.Plan && item.Plan != null)
                    foreach (var column in item.Plan.Columns)
                        if (!string.IsNullOrEmpty(column.LinkedTextId)) entry.LinkedTexts.Add(column.LinkedTextId);
                // Les liens [[…]] de tous les champs (corps, synopsis, notes,
                // champs de modèle), résolus par leur titre.
                var seen = new HashSet<string>();
                foreach (var field in Searchable.Build(item, project))
                {
                    if (string.IsNullOrEmpty(field.Text)) continue;
                    foreach (var title in Links.Targets(field.Text))
                    {
                        string id;
                        if (!resolved.TryGetValue(title, out id))
                        {
                            var target = project.FindByTitle(title);
                            id = target == null ? null : target.Id;
                            resolved[title] = id;
                        }
                        if (id != null && id != item.Id && seen.Add(id)) entry.LinkTargets.Add(id);
                    }
                }
                if (item.Kind == ItemKind.Text) entry.Plain = item.Document == null ? "" : item.Document.ToPlainText();
                entries.Add(entry);
            }
            return entries;
        }
    }

    /// <summary>LE GRAPHE : nœuds et arêtes dérivés d'une photographie du
    /// projet, plus les lectures qu'on en fait (voisinage, orphelins, îlots,
    /// absences). Pur : testé au cœur (C55), calculable hors du fil d'interface.</summary>
    public class RelationGraph
    {
        public readonly List<GraphNode> Nodes = new List<GraphNode>();
        public readonly List<GraphEdge> Edges = new List<GraphEdge>();
        public readonly Dictionary<string, GraphNode> ById = new Dictionary<string, GraphNode>();
        private readonly Dictionary<string, List<GraphEdge>> _incident = new Dictionary<string, List<GraphEdge>>();
        /// <summary>Les écrits du récit dans l'ordre (pour les présences et les absences).</summary>
        public readonly List<GraphNode> Writings = new List<GraphNode>();

        public GraphNode Node(string id) { GraphNode node; return ById.TryGetValue(id, out node) ? node : null; }

        public List<GraphEdge> EdgesOf(string id)
        {
            List<GraphEdge> list;
            return _incident.TryGetValue(id, out list) ? list : new List<GraphEdge>();
        }

        /// <summary>Bâtit le graphe. Le comptage des présences (chaque nom de
        /// chaque fiche dans chaque écrit) est le seul travail coûteux :
        /// appelé à l'ouverture de la vue, sous un indicateur, jamais en fond.</summary>
        public static RelationGraph Build(List<GraphEntry> entries, GraphOptions options)
        {
            if (options == null) options = new GraphOptions();
            var graph = new RelationGraph();
            var byId = new Dictionary<string, GraphEntry>();
            foreach (var entry in entries)
            {
                if (!options.Texts && entry.Kind != ItemKind.Sheet) continue;
                if (byId.ContainsKey(entry.Id)) continue;
                byId[entry.Id] = entry;
                var node = new GraphNode { Id = entry.Id, Title = entry.Title, Kind = entry.Kind, CategoryId = entry.CategoryId, Color = entry.Color };
                graph.Nodes.Add(node);
                graph.ById[entry.Id] = node;
            }
            var writings = new List<GraphEntry>();
            foreach (var entry in byId.Values) if (entry.Kind == ItemKind.Text) writings.Add(entry);
            writings.Sort(delegate(GraphEntry a, GraphEntry b) { return a.Order.CompareTo(b.Order); });
            foreach (var entry in writings) graph.Writings.Add(graph.ById[entry.Id]);

            var dedupe = new HashSet<string>();
            foreach (var entry in byId.Values)
            {
                if (options.Relations)
                    foreach (var relation in entry.Relations)
                        if (byId.ContainsKey(relation.Key))
                            graph.AddEdge(entry.Id, relation.Key, GraphEdgeKind.Relation, relation.Value, 1, dedupe, true);
                if (options.Wiki)
                    foreach (var target in entry.LinkTargets)
                        if (byId.ContainsKey(target))
                            graph.AddEdge(entry.Id, target, GraphEdgeKind.Wiki, "", 1, dedupe, false);
                if (options.Structure && entry.Kind == ItemKind.Text && entry.BookId != null && byId.ContainsKey(entry.BookId))
                    graph.AddEdge(entry.BookId, entry.Id, GraphEdgeKind.Structure, "", 1, dedupe, false);
                if (options.Plans && entry.Kind == ItemKind.Plan)
                    foreach (var text in entry.LinkedTexts)
                        if (byId.ContainsKey(text))
                            graph.AddEdge(entry.Id, text, GraphEdgeKind.Plan, "", 1, dedupe, false);
            }
            if (options.Tags)
            {
                var byTag = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in byId.Values)
                {
                    if (entry.Kind != ItemKind.Sheet) continue;
                    foreach (var tag in entry.Tags)
                    {
                        List<string> list;
                        if (!byTag.TryGetValue(tag, out list)) byTag[tag] = list = new List<string>();
                        list.Add(entry.Id);
                    }
                }
                foreach (var pair in byTag)
                    for (var i = 0; i < pair.Value.Count; i++)
                        for (var j = i + 1; j < pair.Value.Count; j++)
                            graph.AddEdge(pair.Value[i], pair.Value[j], GraphEdgeKind.Tag, pair.Key, 1, dedupe, false);
            }
            if (options.Presence && options.Texts)
            {
                var sheets = new List<GraphEntry>();
                foreach (var entry in byId.Values)
                    if (entry.Kind == ItemKind.Sheet && entry.Names != null && entry.Names.Count > 0) sheets.Add(entry);
                foreach (var text in writings)
                {
                    if (string.IsNullOrEmpty(text.Plain)) continue;
                    foreach (var sheet in sheets)
                    {
                        int first;
                        var count = Presence.CountIn(text.Plain, sheet.Names, out first);
                        if (count == 0) continue;
                        graph.AddEdge(text.Id, sheet.Id, GraphEdgeKind.Presence, "", count, dedupe, false, first);
                        graph.ById[text.Id].Presence++;
                        graph.ById[sheet.Id].Presence++;
                    }
                }
            }
            return graph;
        }

        /// <summary>Une arête de plus — jamais deux fois la même paire pour la
        /// même nature (les relations réciproques n'en font qu'une : la
        /// première rencontrée nomme l'arête).</summary>
        private void AddEdge(string from, string to, GraphEdgeKind kind, string label, int weight, HashSet<string> dedupe, bool undirectedKey, int first = -1)
        {
            if (from == to) return;
            var a = string.CompareOrdinal(from, to) <= 0 ? from : to;
            var b = ReferenceEquals(a, from) ? to : from;
            var key = kind + "|" + a + "|" + b;
            if (!dedupe.Add(key)) return;
            var edge = new GraphEdge { From = from, To = to, Kind = kind, Label = label ?? "", Weight = weight, First = first };
            Edges.Add(edge);
            Incident(from).Add(edge);
            Incident(to).Add(edge);
            ById[from].Degree++;
            ById[to].Degree++;
        }

        private List<GraphEdge> Incident(string id)
        {
            List<GraphEdge> list;
            if (!_incident.TryGetValue(id, out list)) _incident[id] = list = new List<GraphEdge>();
            return list;
        }

        /// <summary>Le VOISINAGE d'un nœud à N sauts (lui compris), par les
        /// natures d'arêtes retenues (null = toutes).</summary>
        public HashSet<string> Neighborhood(string id, int depth, Func<GraphEdge, bool> keep)
        {
            var seen = new HashSet<string>();
            if (id == null || !ById.ContainsKey(id)) return seen;
            seen.Add(id);
            var frontier = new List<string> { id };
            for (var step = 0; step < depth && frontier.Count > 0; step++)
            {
                var next = new List<string>();
                foreach (var current in frontier)
                    foreach (var edge in EdgesOf(current))
                    {
                        if (keep != null && !keep(edge)) continue;
                        var other = edge.Other(current);
                        if (seen.Add(other)) next.Add(other);
                    }
                frontier = next;
            }
            return seen;
        }

        /// <summary>Les nœuds sans aucune arête (des natures retenues).</summary>
        public List<GraphNode> Orphans(Func<GraphEdge, bool> keep)
        {
            var list = new List<GraphNode>();
            foreach (var node in Nodes)
            {
                var linked = false;
                foreach (var edge in EdgesOf(node.Id)) if (keep == null || keep(edge)) { linked = true; break; }
                if (!linked) list.Add(node);
            }
            return list;
        }

        /// <summary>Les ÎLOTS : les composantes connexes d'au moins deux nœuds,
        /// la plus grande d'abord. Un graphe bien tissé n'en a qu'une.</summary>
        public List<List<GraphNode>> Components(Func<GraphEdge, bool> keep)
        {
            var result = new List<List<GraphNode>>();
            var seen = new HashSet<string>();
            foreach (var start in Nodes)
            {
                if (!seen.Add(start.Id)) continue;
                var component = new List<GraphNode>();
                var stack = new Stack<string>();
                stack.Push(start.Id);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    component.Add(ById[id]);
                    foreach (var edge in EdgesOf(id))
                    {
                        if (keep != null && !keep(edge)) continue;
                        var other = edge.Other(id);
                        if (seen.Add(other)) stack.Push(other);
                    }
                }
                if (component.Count > 1) result.Add(component);
            }
            result.Sort(delegate(List<GraphNode> a, List<GraphNode> b) { return b.Count.CompareTo(a.Count); });
            return result;
        }

        /// <summary>Une fiche et son silence : depuis combien d'écrits (dans
        /// l'ordre du récit) elle n'a plus paru. Jamais présente = -1.</summary>
        public class Absence
        {
            public GraphNode Sheet;
            public int LastIndex = -1;   // l'index du dernier écrit où elle paraît
            public int Since;            // écrits écoulés depuis (0 = présente dans le dernier)
            public int Appearances;      // nombre d'écrits où elle paraît
        }

        /// <summary>Les absences des fiches qui ont paru au moins une fois,
        /// les plus longues d'abord ; les fiches jamais présentes à part
        /// (NeverPresent). Sans écrit, rien.</summary>
        public List<Absence> Absences()
        {
            var list = new List<Absence>();
            if (Writings.Count == 0) return list;
            var index = new Dictionary<string, int>();
            for (var i = 0; i < Writings.Count; i++) index[Writings[i].Id] = i;
            foreach (var node in Nodes)
            {
                if (!node.IsSheet) continue;
                var absence = new Absence { Sheet = node };
                foreach (var edge in EdgesOf(node.Id))
                {
                    if (edge.Kind != GraphEdgeKind.Presence) continue;
                    int at;
                    if (!index.TryGetValue(edge.Other(node.Id), out at)) continue;
                    absence.Appearances++;
                    if (at > absence.LastIndex) absence.LastIndex = at;
                }
                if (absence.Appearances == 0) continue;
                absence.Since = Writings.Count - 1 - absence.LastIndex;
                list.Add(absence);
            }
            list.Sort(delegate(Absence a, Absence b)
            {
                var bySince = b.Since.CompareTo(a.Since);
                return bySince != 0 ? bySince : string.Compare(a.Sheet.Title, b.Sheet.Title, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// <summary>Les fiches qui ont des noms mais ne paraissent dans aucun écrit.</summary>
        public List<GraphNode> NeverPresent()
        {
            var list = new List<GraphNode>();
            foreach (var node in Nodes)
                if (node.IsSheet && node.Presence == 0) list.Add(node);
            return list;
        }

        /// <summary>Le nombre d'occurrences d'une fiche dans un écrit (0 = absente).</summary>
        public int PresenceCount(string textId, string sheetId)
        {
            foreach (var edge in EdgesOf(textId))
                if (edge.Kind == GraphEdgeKind.Presence && edge.Other(textId) == sheetId) return edge.Weight;
            return 0;
        }
    }

    /// <summary>La position d'un nœud retenue par le projet (.plot v43) :
    /// là où la disposition l'a posé, ou là où l'auteur l'a tiré (épinglé).</summary>
    public class GraphPosition
    {
        public double X, Y;
        public bool Pinned;
    }
}
