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

    /// <summary>Ce que l'arbre sait d'une nature de relation — une entrée du
    /// schéma des relations (RelationSchema, 1.1.0).</summary>
    public class RelationKindInfo
    {
        public string Name;
        public string Family;      // "parent", "child", "sibling"… (clé de réciprocité ; « perso:… » pour une nature du projet)
        public char Gender;        // 'm', 'f' ou 'n' (forme neutre)
        public int Generation;     // -3…+3 vu depuis le personnage (négatif = ascendant)
        public RelationLane Lane;
        public int Slot;           // la case de la famille dans sa voie et sa génération (éditeur de relations)
        public string Reciprocal;  // la famille que la cible déclare en retour (null ou vide = la même)

        public RelationKindInfo Clone()
        {
            return new RelationKindInfo { Name = Name, Family = Family, Gender = Gender, Generation = Generation, Lane = Lane, Slot = Slot, Reciprocal = Reciprocal };
        }
    }

    /// <summary>Les natures de relation (batch 36) — le sélecteur des fiches
    /// personnage les propose dans cet ordre — avec leur réciproque : quand
    /// Kaladin déclare Lirin comme « Père », Lirin reçoit Kaladin comme
    /// « Fils » (ou « Fille », ou « Enfant » si le genre est inconnu). Depuis
    /// la 1.1.0, tout vient du SCHÉMA du projet ouvert (Active, posé par la
    /// fenêtre à l'ouverture du projet) — sans projet, le schéma livré.</summary>
    public static class RelationKinds
    {
        private static RelationSchema _defaults;

        /// <summary>Le schéma du projet ouvert ; null = le schéma livré.</summary>
        public static RelationSchema Active;

        public static RelationSchema Schema
        {
            get
            {
                if (Active != null) return Active;
                if (_defaults == null) _defaults = RelationSchema.Defaults();
                return _defaults;
            }
        }

        /// <summary>Les natures proposées, dans l'ordre du sélecteur.</summary>
        public static IEnumerable<string> Defaults
        {
            get { return Schema.Names; }
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

        /// <summary>La nature GÉNÉALOGIQUE qui porte ce nom — une nature du
        /// schéma hors voie « autres » (null pour une nature libre ou inconnue :
        /// « Mentor » n'oblige à rien).</summary>
        public static RelationKindInfo Find(string kind)
        {
            var info = Schema.Find(kind);
            return info != null && info.Lane != RelationLane.Other ? info : null;
        }

        /// <summary>La forme canonique d'une nature du schéma (« soeur » → « Sœur »),
        /// ou la saisie telle quelle.</summary>
        public static string Canonical(string kind)
        {
            var info = Schema.Find(kind);
            return info != null ? info.Name : (kind ?? "").Trim();
        }

        /// <summary>La nature réciproque, vue depuis la cible : la nature que la
        /// CIBLE déclare envers la SOURCE, selon le genre de la source ('m',
        /// 'f', autre = forme neutre). Une nature inconnue se répond à
        /// elle-même.</summary>
        public static string Reciprocal(string kind, char sourceGender)
        {
            return Schema.Reciprocal(kind, sourceGender);
        }

        /// <summary>La forme d'une famille pour un genre : « child » + 'f' →
        /// « Fille » ; sans forme pour ce genre, la neutre ; sans neutre, la
        /// masculine (cousins, oncles, neveux — le français n'en a pas).</summary>
        public static string Gendered(string family, char gender)
        {
            return Schema.Gendered(family, gender);
        }

        /// <summary>Génération d'une nature vue depuis le personnage (0 pour
        /// une nature inconnue ou libre).</summary>
        public static int GenerationOf(string kind)
        {
            var info = Schema.Find(kind);
            return info == null || info.Lane == RelationLane.Other ? 0 : info.Generation;
        }

        public static RelationLane LaneOf(string kind)
        {
            var info = Schema.Find(kind);
            return info == null ? RelationLane.Other : info.Lane;
        }

        /// <summary>Les noms de champ où lire le genre, par priorité (23/09) :
        /// « Genre (si différent) » est LE point de référence, puis « Genre de
        /// naissance », puis les noms d'avant (« Genre », « Sexe »…).</summary>
        private static readonly string[] GenderFieldKeys =
        {
            Key(SheetDefaults.FieldGender), Key(SheetDefaults.FieldBirthGender),
            "genre", "sexe", "sexe de naissance"
        };

        /// <summary>Le genre d'une fiche, lu dans ses champs de genre (le
        /// modèle d'abord, puis les champs libres — GenderFieldKeys) : 'm'
        /// (homme, masculin, garçon, mâle…), 'f' (femme, féminin, fille…),
        /// sinon 'n'.</summary>
        public static char GenderOf(BinderItem sheet, SheetTemplate template)
        {
            if (sheet == null) return 'n';
            foreach (var key in GenderFieldKeys)
            {
                if (template != null)
                    foreach (var field in template.Fields)
                    {
                        string v;
                        if (Key(field.Name) == key && sheet.FieldValues.TryGetValue(field.Id, out v) && !string.IsNullOrEmpty(v))
                            return GenderOfValue(v);
                    }
                foreach (var entry in sheet.FreeInfo)
                    if (Key(entry.Title) == key && !string.IsNullOrEmpty(entry.Value)) return GenderOfValue(entry.Value);
            }
            return 'n';
        }

        /// <summary>Le genre d'un NOM tel qu'une fiche le porte (1.1.1, pour
        /// pré-remplir « Ajouter au dictionnaire ») : la fiche dont le titre
        /// est ce mot, ou dont le titre commence par ce mot (le prénom de
        /// « Suzanne Verlaine ») ; 'n' sans fiche ou sans genre.</summary>
        public static char GenderOfName(Project project, string word)
        {
            var name = (word ?? "").Trim();
            if (project == null || name.Length == 0) return 'n';
            // Trois forces : le titre entier, le premier mot du titre, un autre
            // mot du titre ; à force égale, deux fiches qui se contredisent
            // (Odile Verlaine, Marc Verlaine) ne disent rien.
            var found = new char[] { 'n', 'n', 'n' };
            var conflict = new bool[3];
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet || string.IsNullOrEmpty(item.Title)) continue;
                var words = item.Title.Trim().Split(new[] { ' ', (char)0xA0, '-', (char)39 }, StringSplitOptions.RemoveEmptyEntries);
                var level = -1;
                if (string.Equals(item.Title.Trim(), name, StringComparison.CurrentCultureIgnoreCase)) level = 0;
                else
                    for (var i = 0; i < words.Length; i++)
                        if (string.Equals(words[i], name, StringComparison.CurrentCultureIgnoreCase)) { level = i == 0 ? 1 : 2; break; }
                if (level < 0) continue;
                var gender = GenderOf(item, project.TemplateOf(item));
                if (gender == 'n') continue;
                if (found[level] == 'n') found[level] = gender;
                else if (found[level] != gender) conflict[level] = true;
            }
            for (var level = 0; level < 3; level++)
                if (found[level] != 'n' && !conflict[level]) return found[level];
            return 'n';
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
            var gender = RelationKinds.GenderOf(source, project.TemplateOf(source));
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
            var gender = RelationKinds.GenderOf(source, project.TemplateOf(source));
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
}
