using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>A field declared by a sheet template, e.g. "Âge" on a character
    /// sheet. Values are stored on instances by field id, so renaming a field
    /// keeps every instance's value and deleting one leaves values dormant
    /// (never destroyed) in the instance dictionaries.</summary>
    public class SheetField
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Champ";
        public string Kind = "text"; // "text" (one line) | "multiline"

        // Groupe d'affichage (batch 31) : les champs d'un même groupe sont
        // rendus sous un intertitre ("Infos", "Physique"…). Vide = groupe
        // par défaut, affiché sans intertitre en tête de fiche.
        public string Group = "";

        public SheetField Clone()
        {
            return (SheetField)MemberwiseClone();
        }
    }

    /// <summary>A sheet template ("Personnage", "Lieu"…), duplicable ad infinitum
    /// into instances. Modeled on The Universe Project's dual pattern: fixed
    /// typed fields here, plus a free key/value list on each instance.</summary>
    public class SheetTemplate
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Modèle";
        public List<SheetField> Fields = new List<SheetField>();
        // Les SECTIONS du modèle (batch 42, v19) : « Informations » (groupe
        // vide) existe toujours ; ici les sections nommées en plus, dans
        // l'ordre des papers (« Apparence » pour le Personnage). Un champ
        // (SheetField.Group) ou une info libre (InfoEntry.Group) s'y range
        // par son nom.
        public List<string> Sections = new List<string>();
        // Le paper « Relations » (liens entre fiches) — vrai pour le
        // Personnage, faux pour les autres modèles livrés (batch 42).
        public bool Relations;

        public SheetTemplate Clone()
        {
            var copy = new SheetTemplate { Id = Id, Name = Name, Relations = Relations };
            foreach (var field in Fields) copy.Fields.Add(field.Clone());
            copy.Sections.AddRange(Sections);
            return copy;
        }

        public bool HasSection(string name)
        {
            foreach (var section in Sections)
                if (string.Equals(section, name, StringComparison.CurrentCultureIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>Une catégorie de fiches (batch 31) : Personnage, Lieu, Magie…
    /// plus les catégories personnalisées. Chaque catégorie porte son MODÈLE
    /// DE BASE (les nouvelles fiches le reçoivent) ; une fiche existante dont
    /// le modèle diffère est marquée d'une puce dans la bibliothèque.</summary>
    public class SheetCategory
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Catégorie";
        public string TemplateId; // modèle de base des nouvelles fiches
    }

    /// <summary>Les sept catégories livrées et leurs modèles par défaut —
    /// volontairement sobres (« sans trop en mettre ») ; le détail viendra
    /// au fil de l'usage. Sert aussi de source à la migration des projets
    /// d'avant les catégories (PlotFile).</summary>
    public static class SheetDefaults
    {
        /// <summary>La section « Apparence » (batch 42 : elle porte son nom —
        /// « Physique » était la clé d'avant, migrée au chargement).</summary>
        public const string GroupLooks = "Apparence";
        public const string LegacyGroupLooks = "Physique";
        public const string LegacyGroupInfos = "Infos";

        public static readonly string[] CategoryNames =
        {
            "Personnage", "Lieu", "Magie", "Objet",
            "Peuple", "Gouvernement", "Environnement"
        };

        /// <summary>Le modèle par défaut d'une catégorie livrée (null pour un
        /// nom inconnu). Les ids sont neufs à chaque appel.</summary>
        public static SheetTemplate TemplateFor(string categoryName)
        {
            switch (categoryName)
            {
                case "Personnage": return Character();
                case "Lieu": return Simple("Lieu",
                    F("Région"), F("Climat"), F("Population"),
                    M("Description"));
                case "Magie": return Simple("Magie",
                    F("Source"), F("Coût"), F("Limites"),
                    M("Description"));
                case "Objet": return Simple("Objet",
                    F("Type"), F("Propriétaire"), F("Origine"),
                    M("Description"));
                case "Peuple": return Simple("Peuple",
                    F("Territoire"), F("Langue"), F("Coutumes"),
                    M("Description"));
                case "Gouvernement": return Simple("Gouvernement",
                    F("Type de régime"), F("Dirigeant"), F("Siège"),
                    M("Description"));
                case "Environnement": return Simple("Environnement",
                    F("Type"), F("Faune"), F("Flore"),
                    M("Description"));
                default: return null;
            }
        }

        /// <summary>Le groupe des champs d'état civil du personnage : depuis le
        /// batch 42, la section par défaut « Informations » (groupe vide).</summary>
        public const string GroupInfos = "";

        // IMPORTANT (batch 36) : « Âge », sous la date de naissance. Champ
        // texte libre pour l'instant — on y revient vite (calcul depuis la
        // date de naissance et la date du récit, âge à chaque scène…). Tout
        // ce qui touche l'âge doit passer par ce nom : FieldAge.
        public const string FieldAge = "Âge";
        public const string FieldBirthDate = "Date de naissance";

        /// <summary>Les champs d'apparence par défaut du personnage (batch 36),
        /// dans l'ordre : Taille, Poids, Peau, Yeux, Traits, Particularités
        /// (multiligne). Sert au modèle neuf ET à la migration v16.</summary>
        public static readonly string[] CharacterLooks =
            { "Taille", "Poids", "Peau", "Yeux", "Traits", "Particularités" };

        /// <summary>Le modèle Personnage, en deux groupes : « Infos » (l'état
        /// civil, le genre, l'âge) et « Physique » (batch 31, refondu b36).</summary>
        private static SheetTemplate Character()
        {
            var t = new SheetTemplate { Name = "Personnage", Relations = true };
            t.Sections.Add(GroupLooks); // Informations (implicite) + Apparence
            t.Fields.Add(G("Nom", GroupInfos));
            t.Fields.Add(G("Prénom", GroupInfos));
            t.Fields.Add(G("Alias", GroupInfos));
            t.Fields.Add(G("Genre", GroupInfos));
            t.Fields.Add(G(FieldBirthDate, GroupInfos));
            t.Fields.Add(G(FieldAge, GroupInfos)); // IMPORTANT : voir FieldAge
            t.Fields.Add(G("Lieu de naissance", GroupInfos));
            t.Fields.Add(G("Affiliation", GroupInfos));
            t.Fields.Add(G("Religion", GroupInfos));
            t.Fields.Add(G("Magie", GroupInfos));
            foreach (var name in CharacterLooks)
            {
                var field = G(name, GroupLooks);
                if (name == "Particularités") field.Kind = "multiline";
                t.Fields.Add(field);
            }
            return t;
        }

        /// <summary>Migration v16 du modèle Personnage d'un projet existant :
        /// « Âge » sous la date de naissance, l'apparence alignée sur
        /// CharacterLooks (« Couleur de peau » renommée « Peau » — même id, les
        /// valeurs suivent ; « Cheveux » et « Sexe de naissance » ne sont
        /// retirés que si AUCUNE fiche ne les a remplis, sinon ils restent en
        /// queue d'apparence). Idempotente. Rend vrai si le modèle a changé.</summary>
        public static bool UpgradeCharacterTemplate(SheetTemplate template, IEnumerable<BinderItem> sheets)
        {
            if (template == null) return false;
            var changed = false;
            var fields = template.Fields;

            // — Les groupes d'avant les sections (b42) : « Physique » →
            //   « Apparence », « Infos » → Informations — même appelée seule.
            foreach (var field in fields)
            {
                var migrated = MigrateGroup(field.Group);
                if (migrated != field.Group) { field.Group = migrated; changed = true; }
            }
            if (!template.HasSection(GroupLooks)) { template.Sections.Add(GroupLooks); changed = true; }

            // — Âge, juste sous la date de naissance.
            if (FindField(fields, FieldAge) == null)
            {
                var birth = FindField(fields, FieldBirthDate);
                var age = G(FieldAge, birth != null && birth.Group.Length > 0 ? birth.Group : GroupInfos);
                fields.Insert(birth != null ? fields.IndexOf(birth) + 1 : fields.Count, age);
                changed = true;
            }

            // — Apparence : renommage, complément, ordre.
            var skin = FindField(fields, "Couleur de peau");
            if (skin != null && FindField(fields, "Peau") == null) { skin.Name = "Peau"; changed = true; }
            var looks = new List<SheetField>();
            foreach (var name in CharacterLooks)
            {
                var field = FindField(fields, name);
                if (field == null)
                {
                    field = G(name, GroupLooks);
                    if (name == "Particularités") field.Kind = "multiline";
                    changed = true;
                }
                else if (!string.Equals(field.Group, GroupLooks, StringComparison.CurrentCultureIgnoreCase))
                {
                    field.Group = GroupLooks; // « transféré » vers l'apparence
                    changed = true;
                }
                looks.Add(field);
            }
            var rest = new List<SheetField>();
            foreach (var field in fields)
            {
                if (looks.Contains(field)) continue;
                var stale = string.Equals(field.Group, GroupLooks, StringComparison.CurrentCultureIgnoreCase)
                    && (FieldIs(field, "Cheveux") || FieldIs(field, "Sexe de naissance"))
                    && !AnyValue(field, sheets);
                if (stale) { changed = true; continue; }
                rest.Add(field);
            }
            // L'apparence en bloc, à la place du premier champ d'apparence.
            var firstLooks = rest.FindIndex(delegate(SheetField f)
            { return string.Equals(f.Group, GroupLooks, StringComparison.CurrentCultureIgnoreCase); });
            var rebuilt = new List<SheetField>();
            for (var i = 0; i < rest.Count; i++)
            {
                if (i == firstLooks) rebuilt.AddRange(looks);
                rebuilt.Add(rest[i]);
            }
            if (firstLooks < 0) rebuilt.AddRange(looks);
            for (var i = 0; i < rebuilt.Count; i++)
                if (i >= fields.Count || !ReferenceEquals(fields[i], rebuilt[i])) { changed = true; break; }
            if (changed)
            {
                fields.Clear();
                fields.AddRange(rebuilt);
            }
            return changed;
        }

        private static SheetField FindField(List<SheetField> fields, string name)
        {
            foreach (var field in fields) if (FieldIs(field, name)) return field;
            return null;
        }

        private static bool FieldIs(SheetField field, string name)
        {
            return string.Equals(field.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool AnyValue(SheetField field, IEnumerable<BinderItem> sheets)
        {
            if (sheets == null) return false;
            foreach (var sheet in sheets)
            {
                string value;
                if (sheet.FieldValues.TryGetValue(field.Id, out value) && !string.IsNullOrEmpty(value)) return true;
            }
            return false;
        }

        private static SheetTemplate Simple(string name, params SheetField[] fields)
        {
            var template = new SheetTemplate { Name = name };
            template.Fields.AddRange(fields);
            return template;
        }

        private static SheetField F(string name)
        {
            return new SheetField { Name = name };
        }

        private static SheetField M(string name)
        {
            return new SheetField { Name = name, Kind = "multiline" };
        }

        private static SheetField G(string name, string group)
        {
            return new SheetField { Name = name, Group = group };
        }

        /// <summary>Migration v19 (batch 42) des modèles et des fiches d'un
        /// projet d'avant les sections : « Physique » devient « Apparence »,
        /// « Infos » rejoint Informations (groupe vide) ; chaque modèle reçoit
        /// ses sections (les groupes nommés encore présents) et le paper
        /// Relations n'est gardé que par le Personnage (modèle de base de la
        /// catégorie de ce nom, ou modèle ainsi nommé). Idempotente.</summary>
        public static void UpgradeSections(Project project)
        {
            var characterTemplates = new HashSet<string>();
            foreach (var category in project.SheetCategories)
                if (string.Equals(category.Name, "Personnage", StringComparison.CurrentCultureIgnoreCase)
                    && category.TemplateId != null)
                    characterTemplates.Add(category.TemplateId);
            foreach (var template in project.Templates)
            {
                foreach (var field in template.Fields) field.Group = MigrateGroup(field.Group);
                foreach (var field in template.Fields)
                    if (field.Group.Length > 0 && !template.HasSection(field.Group))
                        template.Sections.Add(field.Group);
                if (characterTemplates.Contains(template.Id)
                    || string.Equals(template.Name, "Personnage", StringComparison.CurrentCultureIgnoreCase))
                    template.Relations = true;
            }
            foreach (var item in project.AllItems())
                foreach (var entry in item.FreeInfo)
                    entry.Group = MigrateGroup(entry.Group);
        }

        public static string MigrateGroup(string group)
        {
            if (group == null) return "";
            if (string.Equals(group, LegacyGroupLooks, StringComparison.CurrentCultureIgnoreCase)) return GroupLooks;
            if (string.Equals(group, LegacyGroupInfos, StringComparison.CurrentCultureIgnoreCase)) return GroupInfos;
            return group.Trim();
        }

        /// <summary>Peuple un projet NEUF : sept modèles, sept catégories.</summary>
        public static void Seed(List<SheetTemplate> templates,
            List<SheetCategory> categories)
        {
            foreach (var name in CategoryNames)
            {
                var template = TemplateFor(name);
                templates.Add(template);
                categories.Add(new SheetCategory
                {
                    Name = name,
                    TemplateId = template.Id
                });
            }
        }
    }

    /// <summary>A free key/value entry on a sheet — the escape hatch for
    /// whatever the template did not foresee.</summary>
    public class InfoEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Value = "";
        public string Group = ""; // "" = Informations, "Physique" = Apparence (batch 34)
    }

    /// <summary>Une relation d'une fiche vers une autre (batch 34) : sa
    /// nature (« frère », « mentor »…) et sa cible — une fiche du projet
    /// (TargetId) ou un simple nom (Name) quand la fiche n'existe pas.</summary>
    public class SheetRelation
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = "";
        public string TargetId;   // null = cible libre
        public string Name = "";
    }
}
