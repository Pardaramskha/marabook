using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Marabook.Model
{
    /// <summary>La place d'une nature de relation dans l'arbre généalogique
    /// (batch 36) : la ligne directe (parents, enfants…), les collatéraux
    /// (fratrie, oncles, cousins…), les partenaires (à côté du personnage)
    /// et le reste (natures libres, non placées).</summary>
    public enum RelationLane { Direct, Side, Partner, Other }

    /// <summary>Ce que l'arbre sait d'une nature de relation.</summary>
    public class RelationKindInfo
    {
        public string Name;
        public string Family;      // "parent", "child", "sibling"… (clé de réciprocité)
        public char Gender;        // 'm', 'f' ou 'n' (forme neutre)
        public int Generation;     // -3…+3 vu depuis le personnage (négatif = ascendant)
        public RelationLane Lane;
    }

    /// <summary>Les natures de relation livrées (batch 36) — le sélecteur des
    /// fiches personnage les propose dans cet ordre — avec leur réciproque :
    /// quand Kaladin déclare Lirin comme « Père », Lirin reçoit Kaladin comme
    /// « Fils » (ou « Fille », ou « Enfant » si le genre est inconnu). Les
    /// natures personnalisées du projet (Project.RelationKinds) sont
    /// symétriques : « Mentor » répond « Mentor ».</summary>
    public static class RelationKinds
    {
        private static readonly List<RelationKindInfo> _infos = new List<RelationKindInfo>();
        private static readonly Dictionary<string, RelationKindInfo> _byKey = new Dictionary<string, RelationKindInfo>();

        static RelationKinds()
        {
            Family("parent", -1, RelationLane.Direct, "Père", "Mère", "Parent");
            Family("grandparent", -2, RelationLane.Direct, "Grand-père", "Grand-mère", "Grand-parent");
            Family("child", 1, RelationLane.Direct, "Fils", "Fille", "Enfant");
            Family("grandchild", 2, RelationLane.Direct, "Petit-fils", "Petite-fille", "Petit-enfant");
            Family("ancestor", -3, RelationLane.Direct, null, null, "Aïeul");
            Family("descendant", 3, RelationLane.Direct, null, null, "Descendant");
            Family("lover", 0, RelationLane.Partner, null, null, "Amoureux");
            Family("amant", 0, RelationLane.Partner, null, null, "Amant");
            Family("ex", 0, RelationLane.Partner, null, null, "Ex");
            Family("spouse", 0, RelationLane.Partner, "Mari", "Femme", "Partenaire");
            Family("double", 0, RelationLane.Side, null, null, "Doppelgänger");
            Family("uncle", -1, RelationLane.Side, "Oncle", "Tante", null);
            Family("nephew", 1, RelationLane.Side, "Neveu", "Nièce", null);
            Family("cousin", 0, RelationLane.Side, "Cousin", "Cousine", null);
            Family("sibling", 0, RelationLane.Side, "Frère", "Sœur", "Adelphe");
        }

        private static void Family(string family, int generation, RelationLane lane,
            string masculine, string feminine, string neutral)
        {
            if (masculine != null) Add(masculine, family, 'm', generation, lane);
            if (feminine != null) Add(feminine, family, 'f', generation, lane);
            if (neutral != null) Add(neutral, family, 'n', generation, lane);
        }

        private static void Add(string name, string family, char gender, int generation, RelationLane lane)
        {
            var info = new RelationKindInfo { Name = name, Family = family, Gender = gender, Generation = generation, Lane = lane };
            _infos.Add(info);
            _byKey[Key(name)] = info;
        }

        /// <summary>Les natures livrées, dans l'ordre du sélecteur.</summary>
        public static IEnumerable<string> Defaults
        {
            get { foreach (var info in _infos) yield return info.Name; }
        }

        /// <summary>Clé de comparaison : sans casse, sans accents, « œ » → « oe »,
        /// tirets et espaces confondus — « Soeur », « SŒUR » et « Sœur » sont
        /// la même nature.</summary>
        public static string Key(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            var folded = kind.Trim().ToLowerInvariant().Replace("œ", "oe").Replace("æ", "ae");
            var decomposed = folded.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c == '-' || c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }

        public static bool Same(string a, string b) { return Key(a) == Key(b); }

        /// <summary>La nature livrée qui porte ce nom (null pour une nature
        /// libre ou personnalisée).</summary>
        public static RelationKindInfo Find(string kind)
        {
            RelationKindInfo info;
            return _byKey.TryGetValue(Key(kind), out info) ? info : null;
        }

        /// <summary>La forme canonique d'une nature livrée (« soeur » → « Sœur »),
        /// ou la saisie telle quelle.</summary>
        public static string Canonical(string kind)
        {
            var info = Find(kind);
            return info != null ? info.Name : (kind ?? "").Trim();
        }

        /// <summary>La nature réciproque, vue depuis la cible : la nature que la
        /// CIBLE déclare envers la SOURCE, selon le genre de la source ('m',
        /// 'f', autre = forme neutre). Une nature inconnue se répond à
        /// elle-même.</summary>
        public static string Reciprocal(string kind, char sourceGender)
        {
            var info = Find(kind);
            if (info == null) return (kind ?? "").Trim();
            string family;
            switch (info.Family)
            {
                case "parent": family = "child"; break;
                case "child": family = "parent"; break;
                case "grandparent": family = "grandchild"; break;
                case "grandchild": family = "grandparent"; break;
                case "ancestor": family = "descendant"; break;
                case "descendant": family = "ancestor"; break;
                case "uncle": family = "nephew"; break;
                case "nephew": family = "uncle"; break;
                default: family = info.Family; break; // symétriques : époux, fratrie, cousins, amours, double
            }
            return Gendered(family, sourceGender);
        }

        /// <summary>La forme d'une famille pour un genre : « child » + 'f' →
        /// « Fille » ; sans forme pour ce genre, la neutre ; sans neutre, la
        /// masculine (cousins, oncles, neveux — le français n'en a pas).</summary>
        public static string Gendered(string family, char gender)
        {
            RelationKindInfo exact = null, neutral = null, masculine = null;
            foreach (var info in _infos)
            {
                if (info.Family != family) continue;
                if (info.Gender == gender) exact = info;
                if (info.Gender == 'n') neutral = info;
                if (info.Gender == 'm') masculine = info;
            }
            var chosen = exact ?? neutral ?? masculine;
            return chosen == null ? "" : chosen.Name;
        }

        /// <summary>Génération d'une nature vue depuis le personnage (0 pour
        /// une nature inconnue).</summary>
        public static int GenerationOf(string kind)
        {
            var info = Find(kind);
            return info == null ? 0 : info.Generation;
        }

        public static RelationLane LaneOf(string kind)
        {
            var info = Find(kind);
            return info == null ? RelationLane.Other : info.Lane;
        }

        /// <summary>Le genre d'une fiche, lu dans son champ « Genre » (ou
        /// « Sexe ») : 'm' (homme, masculin, garçon, mâle…), 'f' (femme,
        /// féminin, fille…), sinon 'n'.</summary>
        public static char GenderOf(BinderItem sheet, SheetTemplate template)
        {
            if (sheet == null) return 'n';
            string value = null;
            if (template != null)
                foreach (var field in template.Fields)
                {
                    var name = Key(field.Name);
                    if (name != "genre" && name != "sexe" && name != "sexe de naissance") continue;
                    string v;
                    if (sheet.FieldValues.TryGetValue(field.Id, out v) && !string.IsNullOrEmpty(v)) { value = v; break; }
                }
            if (value == null)
                foreach (var entry in sheet.FreeInfo)
                {
                    var name = Key(entry.Title);
                    if ((name == "genre" || name == "sexe") && !string.IsNullOrEmpty(entry.Value)) { value = entry.Value; break; }
                }
            return GenderOfValue(value);
        }

        public static char GenderOfValue(string value)
        {
            var key = Key(value);
            if (key.Length == 0) return 'n';
            string[] male = { "homme", "masculin", "male", "garcon", "h", "m", "man", "mec", "monsieur", "he", "il" };
            string[] female = { "femme", "feminin", "femelle", "fille", "f", "woman", "madame", "she", "elle" };
            foreach (var word in male) if (key == word || key.StartsWith(word + " ")) return 'm';
            foreach (var word in female) if (key == word || key.StartsWith(word + " ")) return 'f';
            return 'n';
        }
    }

    /// <summary>La synchronisation des relations entre fiches (batch 36) :
    /// une relation posée vers une fiche personnage se reflète sur celle-ci
    /// (nature réciproque selon le genre de la source), suit ses changements
    /// de nature ou de cible, et s'efface avec elle.</summary>
    public static class RelationSync
    {
        /// <summary>Reflète « relation » (source → cible) sur la fiche cible :
        /// met à jour le miroir existant (celui qui répondait à previousKind),
        /// sinon en crée un. Rend le miroir, ou null (cible libre, absente,
        /// ou la source elle-même).</summary>
        public static SheetRelation Mirror(Project project, BinderItem source, SheetRelation relation, string previousKind)
        {
            var target = TargetSheet(project, source, relation.TargetId);
            if (target == null) return null;
            var gender = RelationKinds.GenderOf(source, project.FindTemplate(source.TemplateId));
            var wanted = RelationKinds.Reciprocal(relation.Kind, gender);
            var mirror = FindMirror(target, source, wanted);
            if (mirror == null && previousKind != null)
                mirror = FindMirror(target, source, RelationKinds.Reciprocal(previousKind, gender));
            if (mirror == null && previousKind != null)
            {
                // Changement de nature d'une relation déjà reflétée, et une
                // seule relation cible → source : c'est elle (l'auteur l'a
                // peut-être renommée à la main). Jamais pour une relation
                // NEUVE : deux relations vers la même fiche (Amant, puis Ex)
                // ont chacune leur reflet.
                var candidates = new List<SheetRelation>();
                foreach (var r in target.Relations) if (r.TargetId == source.Id) candidates.Add(r);
                if (candidates.Count == 1) mirror = candidates[0];
            }
            if (mirror == null)
            {
                mirror = new SheetRelation { Kind = wanted, TargetId = source.Id };
                target.Relations.Add(mirror);
            }
            else mirror.Kind = wanted;
            return mirror;
        }

        /// <summary>Retire de la cible le miroir d'une relation (source → cible,
        /// de nature kind) — quand la relation est supprimée ou déplacée.
        /// Rend vrai si un miroir a été retiré.</summary>
        public static bool Unmirror(Project project, BinderItem source, string targetId, string kind)
        {
            var target = TargetSheet(project, source, targetId);
            if (target == null) return false;
            var gender = RelationKinds.GenderOf(source, project.FindTemplate(source.TemplateId));
            var mirror = FindMirror(target, source, RelationKinds.Reciprocal(kind, gender));
            if (mirror == null) return false;
            target.Relations.Remove(mirror);
            return true;
        }

        private static BinderItem TargetSheet(Project project, BinderItem source, string targetId)
        {
            if (project == null || source == null || targetId == null) return null;
            var target = project.FindById(targetId);
            if (target == null || target == source || target.Kind != ItemKind.Sheet) return null;
            return target;
        }

        private static SheetRelation FindMirror(BinderItem target, BinderItem source, string kind)
        {
            foreach (var r in target.Relations)
                if (r.TargetId == source.Id && RelationKinds.Same(r.Kind, kind)) return r;
            return null;
        }
    }

    /// <summary>Un nœud de l'arbre généalogique : le personnage lui-même ou
    /// l'une de ses relations, placé sur une génération et une voie.</summary>
    public class GenealogyNode
    {
        public string Label = "";
        public string Kind = "";
        public string TargetId;      // fiche liée (cliquable), null = nom libre
        public int Generation;       // 0 = la génération du personnage
        public RelationLane Lane;
        public bool IsSelf;
    }

    /// <summary>L'arbre par défaut d'une fiche (batch 36) : le personnage au
    /// centre, ses relations réparties par génération (ascendants au-dessus,
    /// descendants au-dessous, partenaires et collatéraux à côté), les
    /// natures libres dans une voie « autres ». Modèle pur, testable ; la
    /// fenêtre GenealogyWindow le dessine.</summary>
    public static class Genealogy
    {
        public static List<GenealogyNode> Build(Project project, BinderItem self)
        {
            var nodes = new List<GenealogyNode>();
            if (self == null) return nodes;
            nodes.Add(new GenealogyNode { Label = self.Title, IsSelf = true, Lane = RelationLane.Direct });
            foreach (var relation in self.Relations)
            {
                var target = project == null || relation.TargetId == null ? null : project.FindById(relation.TargetId);
                var label = target != null ? target.Title : relation.Name;
                if (label.Length == 0) continue;
                nodes.Add(new GenealogyNode
                {
                    Label = label,
                    Kind = RelationKinds.Canonical(relation.Kind),
                    TargetId = target != null ? target.Id : null,
                    Generation = RelationKinds.GenerationOf(relation.Kind),
                    Lane = RelationKinds.LaneOf(relation.Kind)
                });
            }
            nodes.Sort(delegate(GenealogyNode a, GenealogyNode b)
            {
                if (a.IsSelf != b.IsSelf) return a.IsSelf ? -1 : 1;
                if (a.Generation != b.Generation) return a.Generation.CompareTo(b.Generation);
                if (a.Lane != b.Lane) return a.Lane.CompareTo(b.Lane);
                return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return nodes;
        }

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
    }
}
