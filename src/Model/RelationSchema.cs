using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>LE SCHÉMA DES RELATIONS D'UN PROJET (1.1.0, 27/10/2026 — l'éditeur
    /// de relations) : la liste des natures de relation que les fiches
    /// proposent, chacune avec sa famille (la clé qui groupe « Père », « Mère »
    /// et « Parent »), son genre, sa génération et sa voie dans l'arbre
    /// généalogique, sa case (Slot) dans la voie, et la famille réciproque
    /// (« parent » répond « child »). Le projet part du schéma livré
    /// (Defaults) ; l'éditeur le remanie ; le .plot ne l'écrit que modifié.
    /// Caché, jamais cassé : une fiche garde la nature qu'elle porte, le schéma
    /// ne dit que ce qui se PROPOSE et où ça se DESSINE.</summary>
    public class RelationSchema
    {
        public List<RelationKindInfo> Kinds = new List<RelationKindInfo>();

        /// <summary>Le schéma livré : les trente natures de Marabook, placées.</summary>
        public static RelationSchema Defaults()
        {
            var schema = new RelationSchema();
            Family(schema, "parent", -1, RelationLane.Direct, "child", "Père", "Mère", "Parent");
            Family(schema, "grandparent", -2, RelationLane.Direct, "grandchild", "Grand-père", "Grand-mère", "Grand-parent");
            Family(schema, "child", 1, RelationLane.Direct, "parent", "Fils", "Fille", "Enfant");
            Family(schema, "grandchild", 2, RelationLane.Direct, "grandparent", "Petit-fils", "Petite-fille", "Petit-enfant");
            Family(schema, "ancestor", -3, RelationLane.Direct, "descendant", null, null, "Aïeul");
            Family(schema, "descendant", 3, RelationLane.Direct, "ancestor", null, null, "Descendant");
            Family(schema, "lover", 0, RelationLane.Partner, "lover", null, null, "Amoureux");
            Family(schema, "amant", 0, RelationLane.Partner, "amant", null, null, "Amant");
            Family(schema, "ex", 0, RelationLane.Partner, "ex", null, null, "Ex");
            Family(schema, "spouse", 0, RelationLane.Partner, "spouse", "Mari", "Femme", "Partenaire");
            Family(schema, "double", 0, RelationLane.Side, "double", null, null, "Doppelgänger");
            Family(schema, "uncle", -1, RelationLane.Side, "nephew", "Oncle", "Tante", null);
            Family(schema, "nephew", 1, RelationLane.Side, "uncle", "Neveu", "Nièce", null);
            Family(schema, "cousin", 0, RelationLane.Side, "cousin", "Cousin", "Cousine", null);
            Family(schema, "sibling", 0, RelationLane.Side, "sibling", "Frère", "Sœur", "Adelphe");
            schema.PlaceSlots();
            return schema;
        }

        private static void Family(RelationSchema schema, string family, int generation, RelationLane lane, string reciprocal,
            string masculine, string feminine, string neutral)
        {
            if (masculine != null) schema.Kinds.Add(new RelationKindInfo { Name = masculine, Family = family, Gender = 'm', Generation = generation, Lane = lane, Reciprocal = reciprocal });
            if (feminine != null) schema.Kinds.Add(new RelationKindInfo { Name = feminine, Family = family, Gender = 'f', Generation = generation, Lane = lane, Reciprocal = reciprocal });
            if (neutral != null) schema.Kinds.Add(new RelationKindInfo { Name = neutral, Family = family, Gender = 'n', Generation = generation, Lane = lane, Reciprocal = reciprocal });
        }

        /// <summary>Les cases : dans chaque génération de chaque voie, les familles
        /// se suivent (0, 1, 2…) dans l'ordre de la liste — les trois formes d'une
        /// famille partagent la case.</summary>
        public void PlaceSlots()
        {
            var next = new Dictionary<string, int>();
            var placed = new Dictionary<string, int>();
            foreach (var kind in Kinds)
            {
                int slot;
                if (placed.TryGetValue(kind.Family, out slot)) { kind.Slot = slot; continue; }
                var key = kind.Lane + "/" + kind.Generation;
                int n;
                next.TryGetValue(key, out n);
                kind.Slot = n;
                placed[kind.Family] = n;
                next[key] = n + 1;
            }
        }

        public RelationSchema Clone()
        {
            var copy = new RelationSchema();
            foreach (var kind in Kinds) copy.Kinds.Add(kind.Clone());
            return copy;
        }

        /// <summary>Même contenu, champ à champ et dans le même ordre.</summary>
        public bool SameAs(RelationSchema other)
        {
            if (other == null || other.Kinds.Count != Kinds.Count) return false;
            for (var i = 0; i < Kinds.Count; i++)
            {
                var a = Kinds[i]; var b = other.Kinds[i];
                if (a.Name != b.Name || a.Family != b.Family || a.Gender != b.Gender || a.Generation != b.Generation
                    || a.Lane != b.Lane || a.Slot != b.Slot || a.Reciprocal != b.Reciprocal) return false;
            }
            return true;
        }

        /// <summary>Les noms, dans l'ordre du sélecteur.</summary>
        public IEnumerable<string> Names
        {
            get { foreach (var kind in Kinds) yield return kind.Name; }
        }

        /// <summary>Les familles, dans l'ordre de première apparition.</summary>
        public List<string> Families()
        {
            var families = new List<string>();
            foreach (var kind in Kinds) if (!families.Contains(kind.Family)) families.Add(kind.Family);
            return families;
        }

        public List<RelationKindInfo> OfFamily(string family)
        {
            var list = new List<RelationKindInfo>();
            foreach (var kind in Kinds) if (kind.Family == family) list.Add(kind);
            return list;
        }

        /// <summary>La nature qui porte ce nom (RelationKinds.Key), ou null.</summary>
        public RelationKindInfo Find(string name)
        {
            var key = RelationKinds.Key(name);
            if (key.Length == 0) return null;
            foreach (var kind in Kinds) if (RelationKinds.Key(kind.Name) == key) return kind;
            return null;
        }

        /// <summary>La forme d'une famille pour un genre : « child » + 'f' →
        /// « Fille » ; sans forme pour ce genre, la neutre ; sans neutre, la
        /// masculine (cousins, oncles, neveux — le français n'en a pas) ; sans
        /// masculine, la première.</summary>
        public string Gendered(string family, char gender)
        {
            RelationKindInfo exact = null, neutral = null, masculine = null, first = null;
            foreach (var kind in Kinds)
            {
                if (kind.Family != family) continue;
                if (first == null) first = kind;
                if (kind.Gender == gender) exact = kind;
                if (kind.Gender == 'n') neutral = kind;
                if (kind.Gender == 'm') masculine = kind;
            }
            var chosen = exact ?? neutral ?? masculine ?? first;
            return chosen == null ? "" : chosen.Name;
        }

        /// <summary>La nature réciproque, vue depuis la cible, selon le genre de
        /// la source ; une nature inconnue se répond à elle-même.</summary>
        public string Reciprocal(string kind, char sourceGender)
        {
            var info = Find(kind);
            if (info == null) return (kind ?? "").Trim();
            var family = string.IsNullOrEmpty(info.Reciprocal) ? info.Family : info.Reciprocal;
            var answer = Gendered(family, sourceGender);
            return answer.Length == 0 ? info.Name : answer;
        }

        /// <summary>Une nature libre (« Mentor ») : voie « autres », génération 0,
        /// réciproque d'elle-même, sans doublon ; rend la forme retenue.</summary>
        public string AddFree(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return "";
            var known = Find(trimmed);
            if (known != null) return known.Name;
            var family = FreeFamilyKey(trimmed);
            var slot = 0;
            foreach (var kind in Kinds) if (kind.Lane == RelationLane.Other && kind.Generation == 0 && kind.Slot >= slot) slot = kind.Slot + 1;
            Kinds.Add(new RelationKindInfo { Name = trimmed, Family = family, Gender = 'n', Generation = 0, Lane = RelationLane.Other, Slot = slot, Reciprocal = family });
            return trimmed;
        }

        /// <summary>Une clé de famille pour une nature créée dans le projet,
        /// jamais en collision avec les livrées (« perso:mentor »).</summary>
        public string FreeFamilyKey(string name)
        {
            var baseKey = "perso:" + RelationKinds.Key(name).Replace(' ', '-');
            var key = baseKey;
            var n = 2;
            while (HasFamily(key)) key = baseKey + "-" + n++;
            return key;
        }

        public bool HasFamily(string family)
        {
            foreach (var kind in Kinds) if (kind.Family == family) return true;
            return false;
        }

        /// <summary>Les natures de voie « autres » qui ne sont pas livrées : ce que
        /// l'ancienne liste « relationKinds » du manifeste doit porter pour les
        /// Marabook d'avant le schéma.</summary>
        public List<string> FreeNames()
        {
            var defaults = Defaults();
            var names = new List<string>();
            foreach (var kind in Kinds)
                if (defaults.Find(kind.Name) == null && !names.Contains(kind.Name)) names.Add(kind.Name);
            return names;
        }
    }
}
