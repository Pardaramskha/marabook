using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>A field declared by a sheet template, e.g. "Âge" on a character
    /// sheet. Values are stored on instances by field id, so renaming a field
    /// keeps every instance's value and deleting one leaves values dormant
    /// (never destroyed) in the instance dictionaries.</summary>
    public class SheetField
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Champ";
        public string Kind = "text"; // une nature de FieldKinds (b47 bis) : text, multiline, number, date, rating, list, choice, sheet
        // Les options d'un champ « choice » (b47 bis, v22) : les valeurs
        // proposées, dans l'ordre.
        public List<string> Options = new List<string>();

        // Groupe d'affichage (batch 31) : les champs d'un même groupe sont
        // rendus sous un intertitre ("Infos", "Physique"…). Vide = groupe
        // par défaut, affiché sans intertitre en tête de fiche.
        public string Group = "";

        public SheetField Clone()
        {
            var copy = (SheetField)MemberwiseClone();
            copy.Options = new List<string>(Options);
            return copy;
        }
    }

    /// <summary>Un axe du radar d'un modèle (b47 bis, v22) : « Force »,
    /// « Charisme »… Une fiche porte une valeur par axe (BinderItem.RadarValues,
    /// clé = l'id de l'axe) — renommer un axe garde les valeurs.</summary>
    public class RadarAxis
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Axe";
    }

    /// <summary>LES NATURES DE CHAMP (b47 bis) — la seule liste. Une nature
    /// dit comment on saisit et comment on lit ; la valeur reste TOUJOURS une
    /// chaîne dans le .plot (FieldValues, InfoEntry.Value), donc un vieux
    /// fichier s'ouvre tel quel et une valeur illisible dans sa nature
    /// s'affiche en texte, jamais perdue.</summary>
    public static class FieldKinds
    {
        public const string Text = "text";
        public const string Multiline = "multiline";
        public const string Number = "number";
        public const string Date = "date";
        public const string Rating = "rating";
        public const string List = "list";
        public const string Choice = "choice";
        public const string Sheet = "sheet";

        public const int RatingMax = 5;

        public static readonly string[] All = { Text, Multiline, Number, Date, Rating, List, Choice, Sheet };

        private static readonly char[] ListSeparators = { ',', ';' };

        /// <summary>Une nature connue, ou « text » pour tout le reste (une
        /// valeur inconnue d'un .plot plus récent lu par une version qui ne
        /// la sait pas : jamais un plantage).</summary>
        public static string Normalize(string kind)
        {
            if (kind == null) return Text;
            foreach (var known in All) if (known == kind) return kind;
            return Text;
        }

        public static string Label(string kind)
        {
            switch (Normalize(kind))
            {
                case Multiline: return "Texte long";
                case Number: return "Nombre";
                case Date: return "Date";
                case Rating: return "Note sur 5";
                case List: return "Liste";
                case Choice: return "Choix";
                case Sheet: return "Fiche liée";
                default: return "Texte court";
            }
        }

        /// <summary>Un nombre en tête de la valeur (« 1,78 m » → 1.78, l'unité
        /// suit librement) ; faux si la valeur ne commence pas par un nombre.</summary>
        public static bool TryNumber(string value, out double number)
        {
            number = 0;
            if (value == null) return false;
            var text = value.Trim().Replace(',', '.');
            var end = 0;
            if (end < text.Length && (text[end] == '-' || text[end] == '+')) end++;
            var digits = false;
            var dot = false;
            while (end < text.Length)
            {
                var c = text[end];
                if (char.IsDigit(c)) { digits = true; end++; continue; }
                if (c == '.' && !dot) { dot = true; end++; continue; }
                if (c == ' ' && digits && end + 1 < text.Length && char.IsDigit(text[end + 1])) { end++; continue; } // « 12 000 »
                break;
            }
            if (!digits) return false;
            return double.TryParse(text.Substring(0, end).Replace(" ", ""),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
        }

        /// <summary>La note d'une valeur « rating » : 0 (vide ou illisible) à RatingMax.</summary>
        public static int RatingOf(string value)
        {
            int n;
            if (value == null || !int.TryParse(value.Trim(), out n)) return 0;
            return n < 0 ? 0 : n > RatingMax ? RatingMax : n;
        }

        /// <summary>Les ronds d'une note : « ●●●○○ ».</summary>
        public static string RatingText(int rating)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 1; i <= RatingMax; i++) sb.Append(i <= rating ? '●' : '○');
            return sb.ToString();
        }

        /// <summary>Les éléments d'une valeur « list » (virgule ou point-virgule),
        /// rognés, vides écartés.</summary>
        public static List<string> ListItems(string value)
        {
            var items = new List<string>();
            if (string.IsNullOrEmpty(value)) return items;
            foreach (var part in value.Split(ListSeparators))
            {
                var item = part.Trim();
                if (item.Length > 0) items.Add(item);
            }
            return items;
        }

        /// <summary>Les options d'un champ « choice » écrites sur une ligne
        /// (« vivant, mort, disparu ») — et l'inverse (ListItems).</summary>
        public static string JoinOptions(IEnumerable<string> options)
        {
            return string.Join(", ", new List<string>(options).ToArray());
        }

        /// <summary>Ce qu'on LIT d'une valeur selon sa nature : la note en
        /// ronds, la liste en éléments séparés, la fiche liée par son titre
        /// (« (fiche disparue) » si l'id ne mène nulle part), le reste tel quel.</summary>
        public static string Display(string kind, string value, Project project)
        {
            switch (Normalize(kind))
            {
                case Rating: return RatingText(RatingOf(value));
                case List: return string.Join(" · ", ListItems(value).ToArray());
                case Sheet:
                {
                    if (string.IsNullOrEmpty(value)) return "";
                    var target = project == null ? null : project.FindById(value);
                    return target != null ? target.Title : "(fiche disparue)";
                }
                default: return value ?? "";
            }
        }

        /// <summary>Une fiche d'une valeur « sheet », ou null.</summary>
        public static BinderItem SheetOf(string value, Project project)
        {
            if (string.IsNullOrEmpty(value) || project == null) return null;
            var target = project.FindById(value);
            return target != null && target.Kind == ItemKind.Sheet ? target : null;
        }

        /// <summary>Vrai si la nature est « lisible » par la recherche (une
        /// fiche liée est un id : on ne la cherche ni ne la remplace).</summary>
        public static bool Searchable(string kind)
        {
            return Normalize(kind) != Sheet;
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
        // LE RADAR (b47 bis, v22) : désactivé par défaut ; activé, la fiche
        // gagne un troisième onglet « Radar » — une toile à un axe par
        // RadarAxes, chaque valeur de 0 à RadarMax.
        public bool Radar;
        public string RadarName = "Radar"; // le nom du radar, libre (« Traits », « Aptitudes »…)
        public List<RadarAxis> RadarAxes = new List<RadarAxis>();
        public int RadarMax = 5;

        /// <summary>Le nom affiché (onglet, infobox) : le nom du modèle ou « Radar ».</summary>
        public string RadarLabel { get { return string.IsNullOrEmpty((RadarName ?? "").Trim()) ? "Radar" : RadarName.Trim(); } }

        public const int RadarMaxFloor = 3, RadarMaxCeiling = 10;

        /// <summary>Les axes livrés quand on active le radar sur un modèle
        /// qui n'en a pas encore.</summary>
        public static readonly string[] DefaultRadarAxes =
            { "Force", "Agilité", "Intelligence", "Charisme", "Volonté", "Chance" };

        public SheetTemplate Clone()
        {
            var copy = new SheetTemplate { Id = Id, Name = Name, Relations = Relations, Radar = Radar, RadarMax = RadarMax, RadarName = RadarName };
            foreach (var field in Fields) copy.Fields.Add(field.Clone());
            copy.Sections.AddRange(Sections);
            foreach (var axis in RadarAxes) copy.RadarAxes.Add(new RadarAxis { Id = axis.Id, Name = axis.Name });
            return copy;
        }

        /// <summary>Le radar est-il à montrer : activé ET au moins trois axes
        /// (à deux, ce n'est pas une toile).</summary>
        public bool ShowsRadar { get { return Radar && RadarAxes.Count >= 3; } }

        public RadarAxis FindAxis(string id)
        {
            foreach (var axis in RadarAxes) if (axis.Id == id) return axis;
            return null;
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
        public string Kind = "text"; // la nature (b47 bis, v22) — comme SheetField.Kind
        public List<string> Options = new List<string>(); // options d'un « choice »
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
