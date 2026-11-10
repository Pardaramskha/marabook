using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Un nœud de l'arbre généalogique : le personnage lui-même, l'une
    /// de ses relations, ou (1.1.1) une personne trouvée PAR REBOND — le père
    /// déclaré sur la fiche du grand-père devient l'arrière-grand-père du
    /// personnage. Chaque nœud connaît ses parents (les clés des nœuds qui le
    /// déclarent comme enfant, ou qu'il déclare comme parents) : c'est ce qui
    /// permet de faire descendre la branche de chaque fratrie du milieu de SES
    /// deux parents, et non du parent commun à toutes.</summary>
    public class GenealogyNode
    {
        public string Key = "";       // l'id de la fiche liée, ou « libre:<id de la relation> » pour un nom libre
        public string Label = "";
        public string Kind = "";      // la nature affichée : déclarée (canonisée) ou déduite (« Arrière-grand-père », « Père d'Abel »)
        public string TargetId;       // fiche liée (cliquable), null = nom libre
        public int Generation;        // 0 = la génération du personnage
        public RelationLane Lane;
        public bool IsSelf;
        public char Gender = 'n';     // le genre de la fiche liée, sinon celui de la nature par laquelle on l'a atteint
        public string ImageId;        // la photo de la fiche liée (1.1.1 : au-dessus du nom)
        public bool Inferred;         // trouvé par rebond, pas déclaré par la fiche du personnage
        public string ViaKey;         // le nœud par lequel on l'a atteint (null pour le personnage)
        public string Family = "";    // la famille de la nature dans le schéma (parent, child, sibling…) ; "" pour une nature libre
        public bool Lineal;           // en ligne directe : ascendant d'ascendant, descendant de descendant
        public int Depth;             // le nombre de rebonds depuis le personnage
        public List<string> ParentKeys = new List<string>();
        public List<string> PartnerKeys = new List<string>();
        public List<string> SiblingKeys = new List<string>();
    }

    /// <summary>Une rangée de l'arbre : une génération, ses nœuds dans l'ordre
    /// de dessin (les enfants d'un même couple côte à côte, sous leurs parents ;
    /// les partenaires collés à leur partenaire).</summary>
    public class GenealogyRow
    {
        public int Generation;
        public List<GenealogyNode> Nodes = new List<GenealogyNode>();
    }

    /// <summary>Un couple (ou un parent seul, ou un groupe de parents présumés)
    /// et les enfants qu'il a en commun : le trait d'union, la branche qui
    /// descend du milieu, le bus et les pieds de la vue.</summary>
    public class GenealogyCouple
    {
        public List<GenealogyNode> Parents = new List<GenealogyNode>();
        public List<GenealogyNode> Children = new List<GenealogyNode>();
        public bool WithSelf;         // le personnage est l'un des parents : trait double
    }

    /// <summary>L'arbre d'une fiche (batch 36 ; familles élargies 1.1.1) : le
    /// personnage au centre, ses relations réparties par génération, et les
    /// personnes que les fiches liées déclarent à leur tour (parents de
    /// parents, enfants de la fratrie, parents de la fratrie — le demi-frère
    /// amène son autre parent), jusqu'à MaxDepth rebonds. Modèle pur,
    /// testable ; GenealogyView le dessine.</summary>
    public static class Genealogy
    {
        public const int MaxDepth = 4;
        public const int MaxNodes = 80;

        private sealed class Pending { public BinderItem Item; public GenealogyNode Node; }

        public static List<GenealogyNode> Build(Project project, BinderItem self)
        {
            var nodes = new List<GenealogyNode>();
            if (self == null) return nodes;
            var byKey = new Dictionary<string, GenealogyNode>(StringComparer.Ordinal);
            var selfNode = new GenealogyNode
            {
                Key = self.Id ?? "", Label = self.Title ?? "", IsSelf = true, Lane = RelationLane.Direct, Lineal = true,
                Gender = project == null ? 'n' : RelationKinds.GenderOf(self, project.TemplateOf(self)), ImageId = self.ImageId
            };
            nodes.Add(selfNode);
            byKey[selfNode.Key] = selfNode;
            var pendingPartners = new List<KeyValuePair<string, string>>();
            var queue = new Queue<Pending>();
            queue.Enqueue(new Pending { Item = self, Node = selfNode });
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var source = current.Node;
                foreach (var relation in current.Item.Relations)
                {
                    var target = project == null || relation.TargetId == null ? null : project.FindById(relation.TargetId);
                    var label = target != null ? target.Title : relation.Name;
                    if (string.IsNullOrEmpty(label)) continue;
                    var key = target != null ? target.Id : "libre:" + relation.Id;
                    if (key == source.Key) continue;
                    var info = RelationKinds.Find(relation.Kind);
                    if (info == null)
                    {
                        // Une nature libre (Mentor, Rivale) : celles du personnage
                        // seulement, dans la bande « autres ».
                        if (!source.IsSelf || byKey.ContainsKey(key)) continue;
                        var free = new GenealogyNode
                        {
                            Key = key, Label = label, Kind = RelationKinds.Canonical(relation.Kind), TargetId = target != null ? target.Id : null,
                            Lane = RelationLane.Other, ViaKey = source.Key, Depth = 1, ImageId = target != null ? target.ImageId : null
                        };
                        nodes.Add(free);
                        byKey[key] = free;
                        continue;
                    }
                    if (!source.IsSelf)
                    {
                        // Par rebond : les partenaires des autres ne font pas de
                        // nœud (ils ne sont dessinés que s'ils sont aussi parents
                        // d'un enfant de l'arbre) ; les collatéraux de même
                        // génération non plus, hors la fratrie.
                        if (info.Lane == RelationLane.Partner)
                        {
                            pendingPartners.Add(new KeyValuePair<string, string>(source.Key, key));
                            continue;
                        }
                        if (info.Lane == RelationLane.Side && info.Generation == 0 && info.Family != "sibling") continue;
                        if (source.Depth + 1 > MaxDepth || byKey.Count >= MaxNodes) continue;
                    }
                    GenealogyNode existing;
                    if (byKey.TryGetValue(key, out existing))
                    {
                        Link(source, existing, info);
                        continue;
                    }
                    var lineal = source.IsSelf ? info.Lane == RelationLane.Direct
                        : source.Lineal && info.Lane == RelationLane.Direct
                            && ((source.Generation <= 0 && info.Generation < 0) || (source.Generation >= 0 && info.Generation > 0));
                    var gender = target != null && project != null ? RelationKinds.GenderOf(target, project.TemplateOf(target)) : 'n';
                    if (gender == 'n') gender = info.Gender;
                    var created = new GenealogyNode
                    {
                        Key = key, Label = label, TargetId = target != null ? target.Id : null,
                        Generation = source.Generation + info.Generation,
                        Kind = source.IsSelf ? RelationKinds.Canonical(relation.Kind) : "",
                        Family = info.Family,
                        Lane = source.IsSelf ? info.Lane : lineal ? RelationLane.Direct : RelationLane.Side,
                        Inferred = !source.IsSelf, ViaKey = source.Key, Depth = source.Depth + 1, Lineal = lineal,
                        Gender = gender, ImageId = target != null ? target.ImageId : null
                    };
                    Link(source, created, info);
                    nodes.Add(created);
                    byKey[key] = created;
                    if (target != null && created.Depth < MaxDepth && target.Relations.Count > 0)
                        queue.Enqueue(new Pending { Item = target, Node = created });
                }
            }
            foreach (var pair in pendingPartners)
            {
                GenealogyNode a, b;
                if (byKey.TryGetValue(pair.Key, out a) && byKey.TryGetValue(pair.Value, out b)) Partner(a, b);
            }
            foreach (var node in nodes)
                if (node.Inferred) node.Kind = InferredKind(node, byKey);
            nodes.Sort(delegate(GenealogyNode a, GenealogyNode b)
            {
                if (a.IsSelf != b.IsSelf) return a.IsSelf ? -1 : 1;
                if (a.Generation != b.Generation) return a.Generation.CompareTo(b.Generation);
                if (a.Lane != b.Lane) return a.Lane.CompareTo(b.Lane);
                if (a.Inferred != b.Inferred) return a.Inferred ? 1 : -1;
                return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return nodes;
        }

        /// <summary>Ce qu'une relation source → cible apprend des liens de
        /// parenté : « Père » (la cible est parent de la source), « Fils » (la
        /// source est parent de la cible), la fratrie, les partenaires.</summary>
        private static void Link(GenealogyNode source, GenealogyNode target, RelationKindInfo info)
        {
            if (info.Lane == RelationLane.Partner) { Partner(source, target); return; }
            switch (info.Family)
            {
                case "parent": AddOnce(source.ParentKeys, target.Key); break;
                case "child": AddOnce(target.ParentKeys, source.Key); break;
                case "sibling": AddOnce(source.SiblingKeys, target.Key); AddOnce(target.SiblingKeys, source.Key); break;
            }
        }

        private static void Partner(GenealogyNode a, GenealogyNode b)
        {
            AddOnce(a.PartnerKeys, b.Key);
            AddOnce(b.PartnerKeys, a.Key);
        }

        private static void AddOnce(List<string> list, string key)
        {
            if (!list.Contains(key)) list.Add(key);
        }

        // ------------------------------------------------------- les libellés

        /// <summary>La nature d'une personne trouvée par rebond, vue depuis le
        /// personnage : en ligne directe, le mot de sa génération (« Arrière-
        /// grand-mère ») ; la fratrie d'un ascendant, oncle ou grand-oncle ;
        /// l'enfant d'un ascendant, frère ou oncle ; l'enfant de la fratrie,
        /// neveu ; l'enfant d'un oncle, cousin ; sinon « Père d'Abel » — la
        /// nature déclarée et le nom de la fiche qui la déclare.</summary>
        private static string InferredKind(GenealogyNode node, Dictionary<string, GenealogyNode> byKey)
        {
            var g = node.Generation;
            var gender = node.Gender;
            if (node.Lineal && g != 0) return LinealWord(g, gender);
            GenealogyNode via;
            if (node.ViaKey == null || !byKey.TryGetValue(node.ViaKey, out via)) via = null;
            if (via != null)
            {
                var viaUncle = via.Family == "uncle" && via.Lane == RelationLane.Side;
                var viaSibling = via.Family == "sibling" && via.Generation == 0;
                if (node.Family == "sibling" && via.Lineal && via.Generation < 0) return UncleWord(g, gender);
                if (node.Family == "child" && via.Lineal && via.Generation < 0)
                {
                    if (g == 0) return RelationKinds.Gendered("sibling", gender);
                    if (g < 0) return UncleWord(g, gender);
                }
                if (node.Family == "child" && (viaSibling || (via.Family == "sibling" && via.Lineal)) && g == 1) return RelationKinds.Gendered("nephew", gender);
                if (node.Family == "child" && viaUncle && g == 0) return RelationKinds.Gendered("cousin", gender);
                if (node.Family == "sibling" && viaUncle) return RelationKinds.Gendered("uncle", gender);
                var word = node.Family.Length > 0 ? RelationKinds.Gendered(node.Family, gender) : "";
                if (word.Length > 0) return word + " " + Of(via.Label);
            }
            return node.Family.Length > 0 ? RelationKinds.Gendered(node.Family, gender) : "relation";
        }

        /// <summary>Le mot d'une génération en ligne directe : Père, Grand-mère,
        /// Arrière-grand-parent, Aïeul·e ; Fils, Petite-fille, Arrière-petit-
        /// enfant, Descendant·e.</summary>
        public static string LinealWord(int generation, char gender)
        {
            switch (generation)
            {
                case -1: return RelationKinds.Gendered("parent", gender);
                case -2: return RelationKinds.Gendered("grandparent", gender);
                case -3: return "Arrière-" + Lower(RelationKinds.Gendered("grandparent", gender));
                case 1: return RelationKinds.Gendered("child", gender);
                case 2: return RelationKinds.Gendered("grandchild", gender);
                case 3: return "Arrière-" + Lower(RelationKinds.Gendered("grandchild", gender));
            }
            if (generation < 0) return gender == 'f' ? "Aïeule" : "Aïeul";
            return gender == 'f' ? "Descendante" : "Descendant";
        }

        /// <summary>Oncle ou tante (-1), grand-oncle (-2), arrière-grand-oncle (-3).</summary>
        public static string UncleWord(int generation, char gender)
        {
            var word = RelationKinds.Gendered("uncle", gender);
            if (generation >= -1) return word;
            if (generation == -2) return "Grand-" + Lower(word);
            return "Arrière-grand-" + Lower(word);
        }

        private static string Lower(string word)
        {
            return word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word.Substring(1);
        }

        /// <summary>« de Marc », « d'Abel » (élision devant une voyelle ou un h).</summary>
        public static string Of(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return "";
            var first = char.ToLowerInvariant(trimmed[0]);
            var folded = first.ToString().Normalize(System.Text.NormalizationForm.FormD)[0];
            return ("aeiouyh".IndexOf(folded) >= 0 ? "d'" : "de ") + trimmed;
        }

        // ------------------------------------------------------ les générations

        /// <summary>Les générations présentes, de la plus ancienne à la plus
        /// récente — la génération 0 toujours incluse.</summary>
        public static List<int> Generations(List<GenealogyNode> nodes)
        {
            var set = new SortedDictionary<int, bool>();
            set[0] = true;
            foreach (var node in nodes)
                if (node.Lane != RelationLane.Other) set[node.Generation] = true;
            return new List<int>(set.Keys);
        }

        // ------------------------------------------------------- la mise en rangées

        /// <summary>Les parents EFFECTIFS de chaque nœud placé : ceux qu'il
        /// déclare ou qui le déclarent ; à défaut ceux de sa fratrie ; à défaut
        /// la présomption d'avant la 1.1.1 — un enfant du personnage sans autre
        /// parent connu pend du personnage et de ses partenaires, un parent sans
        /// parents connus pend du groupe des grands-parents, un oncle de même,
        /// un cousin des oncles, un neveu de la fratrie.</summary>
        public static Dictionary<string, List<string>> EffectiveParents(List<GenealogyNode> nodes)
        {
            var byKey = new Dictionary<string, GenealogyNode>(StringComparer.Ordinal);
            foreach (var node in nodes) if (node.Lane != RelationLane.Other || node.IsSelf) byKey[node.Key] = node;
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in byKey.Values)
            {
                var parents = new List<string>();
                foreach (var key in node.ParentKeys) if (byKey.ContainsKey(key) && key != node.Key) AddOnce(parents, key);
                result[node.Key] = parents;
            }
            // La fratrie partage ses parents (tant que ça apprend quelque chose).
            for (var pass = 0; pass < 6; pass++)
            {
                var changed = false;
                foreach (var node in byKey.Values)
                {
                    if (result[node.Key].Count > 0) continue;
                    foreach (var siblingKey in node.SiblingKeys)
                    {
                        List<string> theirs;
                        if (!result.TryGetValue(siblingKey, out theirs) || theirs.Count == 0) continue;
                        result[node.Key] = new List<string>(theirs);
                        changed = true;
                        break;
                    }
                }
                if (!changed) break;
            }
            // Un seul parent connu : ses partenaires sont présumés co-parents —
            // tous ceux du personnage (comme avant la 1.1.1), le partenaire
            // d'un autre nœud seulement s'il n'en a qu'un.
            foreach (var node in byKey.Values)
            {
                var mine = result[node.Key];
                if (mine.Count != 1) continue;
                GenealogyNode parent;
                if (!byKey.TryGetValue(mine[0], out parent)) continue;
                var mates = new List<string>();
                foreach (var key in parent.PartnerKeys) if (byKey.ContainsKey(key) && key != node.Key) AddOnce(mates, key);
                if (parent.IsSelf || mates.Count == 1) foreach (var key in mates) AddOnce(mine, key);
            }
            // Les présomptions, pour ce que les fiches ne disent pas.
            GenealogyNode self = null;
            foreach (var node in byKey.Values) if (node.IsSelf) self = node;
            foreach (var node in byKey.Values)
            {
                if (result[node.Key].Count > 0) continue;
                List<string> presumed = null;
                if (node.IsSelf) presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return !n.IsSelf && n.Lane == RelationLane.Direct && n.Generation == -1; });
                else if (node.Inferred) presumed = null; // trouvé par rebond sans parents connus : pend de la fiche qui le déclare
                else if (node.Lane == RelationLane.Direct && node.Generation <= -1)
                {
                    var above = node.Generation - 1;
                    presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return !n.IsSelf && n.Lane == RelationLane.Direct && n.Generation == above; });
                }
                else if (node.Lane == RelationLane.Direct && node.Generation == 1 && self != null)
                {
                    presumed = new List<string> { self.Key };
                    foreach (var key in self.PartnerKeys) if (byKey.ContainsKey(key)) AddOnce(presumed, key);
                }
                else if (node.Lane == RelationLane.Direct && node.Generation >= 2)
                {
                    var above = node.Generation - 1;
                    presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return !n.IsSelf && n.Lane == RelationLane.Direct && n.Generation == above; });
                }
                else if (node.Family == "sibling" && node.Generation == 0 && self != null) presumed = new List<string>(result[self.Key]);
                else if (node.Family == "uncle") presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return !n.IsSelf && n.Lane == RelationLane.Direct && n.Generation == node.Generation - 1; });
                else if (node.Family == "cousin") presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return n.Family == "uncle" && n.Lane == RelationLane.Side && n.Generation == node.Generation - 1; });
                else if (node.Family == "nephew") presumed = Keys(byKey.Values, delegate(GenealogyNode n) { return n.Family == "sibling" && n.Generation == node.Generation - 1; });
                if (presumed != null && presumed.Count > 0) result[node.Key] = presumed;
            }
            return result;
        }

        private static List<string> Keys(IEnumerable<GenealogyNode> nodes, Func<GenealogyNode, bool> test)
        {
            var keys = new List<string>();
            foreach (var node in nodes) if (test(node)) keys.Add(node.Key);
            return keys;
        }

        /// <summary>Les rangées, une par génération présente, chacune dans
        /// l'ordre de dessin : les enfants d'un même couple se suivent, sous
        /// leurs parents, dans l'ordre des couples de la rangée du dessus ; un
        /// partenaire se colle à son partenaire (à gauche pour le premier, à
        /// droite pour les suivants — Mathieu, Suzanne, Albert) ; le reste
        /// (partenaires, le personnage, la ligne directe, les collatéraux) en
        /// fin de rangée.</summary>
        public static List<GenealogyRow> Rows(List<GenealogyNode> nodes)
        {
            var parents = EffectiveParents(nodes);
            var byKey = new Dictionary<string, GenealogyNode>(StringComparer.Ordinal);
            foreach (var node in nodes) if (parents.ContainsKey(node.Key)) byKey[node.Key] = node;
            var partners = PartnerMap(byKey, parents);
            var rows = new List<GenealogyRow>();
            foreach (var generation in Generations(nodes))
            {
                var row = new GenealogyRow { Generation = generation };
                var members = new List<GenealogyNode>();
                foreach (var node in byKey.Values) if (node.Generation == generation) members.Add(node);
                var previous = rows.Count > 0 ? rows[rows.Count - 1] : null;
                var seed = new List<GenealogyNode>();
                if (previous != null && previous.Generation == generation - 1)
                    foreach (var couple in CouplesBetween(previous, members, parents, byKey))
                        foreach (var child in couple.Children) if (!seed.Contains(child)) seed.Add(child);
                var rest = new List<GenealogyNode>();
                foreach (var node in members) if (!seed.Contains(node)) rest.Add(node);
                rest.Sort(delegate(GenealogyNode a, GenealogyNode b)
                {
                    var ra = Rank(a); var rb = Rank(b);
                    if (ra != rb) return ra.CompareTo(rb);
                    return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                });
                var order = new List<GenealogyNode>(seed);
                order.AddRange(rest);
                var placed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in order)
                {
                    if (placed.Contains(node.Key)) continue;
                    var mates = new List<GenealogyNode>();
                    var posed = new List<GenealogyNode>();
                    List<string> mateKeys;
                    if (partners.TryGetValue(node.Key, out mateKeys))
                        foreach (var key in mateKeys)
                        {
                            GenealogyNode mate;
                            if (!byKey.TryGetValue(key, out mate) || mate.Generation != generation) continue;
                            if (placed.Contains(key)) posed.Add(mate); else mates.Add(mate);
                        }
                    if (mates.Count == 0 && posed.Count > 0)
                    {
                        // Tous ses partenaires sont déjà posés : il se colle au
                        // premier (après lui, derrière ses autres partenaires).
                        var at = row.Nodes.IndexOf(posed[0]) + 1;
                        while (at < row.Nodes.Count && partners[posed[0].Key].Contains(row.Nodes[at].Key)) at++;
                        row.Nodes.Insert(at, node);
                        placed.Add(node.Key);
                        continue;
                    }
                    if (mates.Count >= 2) { row.Nodes.Add(mates[0]); placed.Add(mates[0].Key); }
                    row.Nodes.Add(node); placed.Add(node.Key);
                    for (var i = mates.Count >= 2 ? 1 : 0; i < mates.Count; i++) { row.Nodes.Add(mates[i]); placed.Add(mates[i].Key); }
                }
                rows.Add(row);
            }
            return rows;
        }

        private static int Rank(GenealogyNode node)
        {
            if (node.Lane == RelationLane.Partner) return 0;
            if (node.IsSelf) return 1;
            if (node.Lane == RelationLane.Direct) return 2;
            return 3;
        }

        /// <summary>Les partenaires pour l'ordre : ceux déclarés, et ceux qui
        /// ont un enfant en commun (co-parents).</summary>
        private static Dictionary<string, List<string>> PartnerMap(Dictionary<string, GenealogyNode> byKey, Dictionary<string, List<string>> parents)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in byKey.Values)
            {
                var mates = new List<string>();
                foreach (var key in node.PartnerKeys) if (byKey.ContainsKey(key)) AddOnce(mates, key);
                map[node.Key] = mates;
            }
            foreach (var pair in parents)
                for (var i = 0; i < pair.Value.Count; i++)
                    for (var j = 0; j < pair.Value.Count; j++)
                        if (i != j && map.ContainsKey(pair.Value[i])) AddOnce(map[pair.Value[i]], pair.Value[j]);
            return map;
        }

        /// <summary>Les couples entre une rangée de parents et les nœuds de la
        /// génération suivante : les enfants groupés par leurs parents effectifs
        /// présents dans la rangée du dessus, dans l'ordre de ces parents (le
        /// plus à gauche d'abord) ; les enfants de chaque couple triés par nom.</summary>
        private static List<GenealogyCouple> CouplesBetween(GenealogyRow parentsRow, List<GenealogyNode> children, Dictionary<string, List<string>> parents, Dictionary<string, GenealogyNode> byKey)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < parentsRow.Nodes.Count; i++) index[parentsRow.Nodes[i].Key] = i;
            var groups = new Dictionary<string, GenealogyCouple>(StringComparer.Ordinal);
            var ordered = new List<GenealogyCouple>();
            foreach (var child in children)
            {
                var mine = new List<string>();
                List<string> declared;
                if (parents.TryGetValue(child.Key, out declared))
                    foreach (var key in declared) if (index.ContainsKey(key)) mine.Add(key);
                if (mine.Count == 0) continue;
                mine.Sort(delegate(string a, string b) { return index[a].CompareTo(index[b]); });
                var signature = string.Join("\u0001", mine.ToArray());
                GenealogyCouple couple;
                if (!groups.TryGetValue(signature, out couple))
                {
                    couple = new GenealogyCouple();
                    foreach (var key in mine) { couple.Parents.Add(byKey[key]); if (byKey[key].IsSelf) couple.WithSelf = true; }
                    groups[signature] = couple;
                    ordered.Add(couple);
                }
                couple.Children.Add(child);
            }
            foreach (var couple in ordered)
                couple.Children.Sort(delegate(GenealogyNode a, GenealogyNode b)
                {
                    if (a.IsSelf != b.IsSelf) return a.IsSelf ? -1 : 1;
                    return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                });
            ordered.Sort(delegate(GenealogyCouple a, GenealogyCouple b)
            {
                var ia = index[a.Parents[0].Key]; var ib = index[b.Parents[0].Key];
                if (ia != ib) return ia.CompareTo(ib);
                return index[a.Parents[a.Parents.Count - 1].Key].CompareTo(index[b.Parents[b.Parents.Count - 1].Key]);
            });
            return ordered;
        }

        /// <summary>Tous les couples de l'arbre, rangée par rangée (parents dans
        /// une rangée, enfants dans la suivante — génération + 1 strictement).
        /// Les enfants y sont dans l'ordre de leur rangée.</summary>
        public static List<GenealogyCouple> Couples(List<GenealogyNode> nodes, List<GenealogyRow> rows)
        {
            var parents = EffectiveParents(nodes);
            var byKey = new Dictionary<string, GenealogyNode>(StringComparer.Ordinal);
            foreach (var node in nodes) if (parents.ContainsKey(node.Key)) byKey[node.Key] = node;
            var result = new List<GenealogyCouple>();
            for (var r = 0; r + 1 < rows.Count; r++)
            {
                if (rows[r + 1].Generation != rows[r].Generation + 1) continue;
                result.AddRange(CouplesBetween(rows[r], rows[r + 1].Nodes, parents, byKey));
            }
            return result;
        }

        /// <summary>Les paires de partenaires déclarés d'une rangée (pour le
        /// trait d'union sans enfant) : chaque paire une fois.</summary>
        public static List<KeyValuePair<GenealogyNode, GenealogyNode>> PartnerPairs(List<GenealogyNode> nodes)
        {
            var byKey = new Dictionary<string, GenealogyNode>(StringComparer.Ordinal);
            foreach (var node in nodes) if (node.Lane != RelationLane.Other || node.IsSelf) byKey[node.Key] = node;
            var pairs = new List<KeyValuePair<GenealogyNode, GenealogyNode>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in byKey.Values)
                foreach (var key in node.PartnerKeys)
                {
                    GenealogyNode mate;
                    if (!byKey.TryGetValue(key, out mate) || mate.Generation != node.Generation) continue;
                    var signature = string.CompareOrdinal(node.Key, key) < 0 ? node.Key + "\u0001" + key : key + "\u0001" + node.Key;
                    if (seen.Add(signature)) pairs.Add(new KeyValuePair<GenealogyNode, GenealogyNode>(node, mate));
                }
            return pairs;
        }
    }
}
