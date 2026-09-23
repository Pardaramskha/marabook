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
        // LES SECTIONS EXTRAS (21/09, v25), désactivées par défaut :
        // — le SUIVI : la traque des noms de la fiche dans les écrits (le
        //   paper « Suivi », la présence du wiki) ; son AMPLITUDE = les ids
        //   d'écrits, de groupes ou de livres où chercher, vide = tous les
        //   écrits ;
        // — l'ÉVOLUTION : le paper des étapes (une fiche qui en porte déjà
        //   le garde, rien n'est caché).
        public bool Tracking;
        public List<string> TrackingScope = new List<string>();
        public bool Evolution;
        // LE GRAPH STATISTIQUE (b47 bis, v22 — « radar » jusqu'au 21/09) :
        // désactivé par défaut ; activé, la fiche gagne un troisième onglet
        // — une toile à un axe par RadarAxes, chaque valeur de 0 à RadarMax.
        public bool Radar;
        public string RadarName = DefaultRadarName; // le nom du graph, libre (« Traits », « Aptitudes »…)
        public List<RadarAxis> RadarAxes = new List<RadarAxis>();
        public int RadarMax = 5;

        public const string DefaultRadarName = "Statistiques";

        /// <summary>Le nom affiché (onglet, infobox) : le nom du modèle ou « Statistiques ».</summary>
        public string RadarLabel { get { return string.IsNullOrEmpty((RadarName ?? "").Trim()) ? DefaultRadarName : RadarName.Trim(); } }

        public const int RadarMaxFloor = 3, RadarMaxCeiling = 10;

        /// <summary>Les axes livrés quand on active le radar sur un modèle
        /// qui n'en a pas encore.</summary>
        public static readonly string[] DefaultRadarAxes =
            { "Force", "Agilité", "Intelligence", "Charisme", "Volonté", "Chance" };

        public SheetTemplate Clone()
        {
            var copy = new SheetTemplate
            {
                Id = Id, Name = Name, Relations = Relations, Radar = Radar, RadarMax = RadarMax, RadarName = RadarName,
                Tracking = Tracking, Evolution = Evolution
            };
            foreach (var field in Fields) copy.Fields.Add(field.Clone());
            copy.Sections.AddRange(Sections);
            copy.TrackingScope.AddRange(TrackingScope);
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

    /// <summary>Les catégories livrées et leurs modèles par défaut — la liste
    /// de Rémi du 23/09/2026 (refonte des fiches). Chaque modèle porte sa
    /// section « Infos » (le groupe vide) ; le Personnage y ajoute
    /// « Apparence » et « Personnalité ». Pas de champ « Description » : le
    /// corps markdown de la fiche est là pour ça. Sert aussi de source aux
    /// migrations (PlotFile : catégories d'avant la v11, Personnage d'avant
    /// la v30).</summary>
    public static class SheetDefaults
    {
        /// <summary>Le libellé de la section par défaut (groupe vide) :
        /// « Infos » depuis le 23/09 (« Informations » du b42 au 22/09).</summary>
        public const string DefaultSectionLabel = "Infos";

        /// <summary>La section « Apparence » (batch 42 : elle porte son nom —
        /// « Physique » était la clé d'avant, migrée au chargement).</summary>
        public const string GroupLooks = "Apparence";
        /// <summary>La section « Personnalité » du Personnage (23/09).</summary>
        public const string GroupPersonality = "Personnalité";
        public const string LegacyGroupLooks = "Physique";
        public const string LegacyGroupInfos = "Infos";

        public static readonly string[] CategoryNames =
        {
            "Personnage", "Lieu", "Événement", "Système",
            "Peuple", "Bestiaire", "Pays / Gouvernement", "Faction / Organisation"
        };

        /// <summary>Le modèle par défaut d'une catégorie livrée (null pour un
        /// nom inconnu). Les ids sont neufs à chaque appel.</summary>
        public static SheetTemplate TemplateFor(string categoryName)
        {
            switch (categoryName)
            {
                case "Personnage": return Character();
                case "Lieu": return Simple("Lieu",
                    Choice("Échelle", GroupInfos, "Continent", "Pays", "Région", "Environnement", "Ville", "Village", "Autre"),
                    S("Population"), S("Climat"));
                case "Événement": return Simple("Événement",
                    S("Date"), S("Lieu"), Kind("Participants", FieldKinds.List));
                case "Système": return Simple("Système",
                    Choice("Type", GroupInfos, "Magie", "Technologie", "Anomalie", "Autre"),
                    S("Provenance"), S("Ressource"), S("Accessibilité"), S("Limites"));
                case "Peuple": return Simple("Peuple",
                    S("Territoire"), S("Langue"));
                case "Bestiaire": return Simple("Bestiaire",
                    Choice("Type", GroupInfos, "Animal", "Créature", "Artificiel", "Autre"),
                    S("Provenance"), S("Longévité"), S("Organisation sociale"),
                    Choice("Domestique", GroupInfos, "Oui", "Non", "Autre"));
                case "Pays / Gouvernement": return Simple("Pays / Gouvernement",
                    S("Type de régime"), S("Dirigeant"), S("Siège"), S("Fondation"));
                case "Faction / Organisation": return Simple("Faction / Organisation",
                    S("Type"), S("Affiliation"), S("Direction"), S("Création"), S("Fin"),
                    Choice("Échelle", GroupInfos, "Locale", "Régionale", "Nationale", "Multinationale", "Mondiale", "Universelle"));
                default: return null;
            }
        }

        /// <summary>Le groupe des champs d'état civil du personnage : la
        /// section par défaut « Infos » (groupe vide).</summary>
        public const string GroupInfos = "";

        // IMPORTANT (batch 36) : « Âge ». Champ texte libre pour l'instant —
        // on y revient vite (calcul depuis la date de naissance et la date du
        // récit, âge à chaque scène…). Tout ce qui touche l'âge doit passer
        // par ce nom : FieldAge.
        public const string FieldAge = "Âge";
        public const string FieldBirthDate = "Date de naissance";
        // LE GENRE (23/09) : deux champs à choix — le genre de naissance, et
        // le genre « si différent ». Ce dernier, s'il est renseigné, est LE
        // point de référence de la conjugaison des relations (RelationKinds
        // .GenderOf) ; sinon le genre de naissance.
        public const string FieldBirthGender = "Genre de naissance";
        public const string FieldGender = "Genre (si différent)";
        public static readonly string[] GenderOptions = { "Masculin", "Féminin", "Neutre", "Autre" };

        /// <summary>Les champs d'infos par défaut du personnage, dans l'ordre (23/09).</summary>
        public static readonly string[] CharacterInfos =
        {
            "Nom", "Prénom", "Alias", FieldAge, FieldBirthDate, "Lieu de naissance",
            FieldBirthGender, FieldGender, "Lieu de résidence", "Croyance", "Capacités/Magie", "Affiliation"
        };

        /// <summary>Les champs d'apparence par défaut du personnage, dans
        /// l'ordre (b36, refondu le 23/09) ; Particularités est multiligne.</summary>
        public static readonly string[] CharacterLooks =
            { "Taille", "Poids", "Couleur des yeux", "Couleur des cheveux", "Teinte de peau", "Traits", "Particularités" };

        /// <summary>Les champs de personnalité par défaut du personnage (23/09).</summary>
        public static readonly string[] CharacterPersonality =
            { "En un mot", "Voix", "Gestuelle", "Sociabilité" };

        /// <summary>Un champ par défaut : son nom, sa section, sa nature, ses
        /// options, et les NOMS D'AVANT qu'il remplace à la migration (un
        /// champ existant ainsi nommé est renommé, même id : les valeurs des
        /// fiches suivent).</summary>
        private class FieldSpec
        {
            public string Name, Group = GroupInfos, Kind = FieldKinds.Text;
            public string[] Options = new string[0], Legacy = new string[0];

            public SheetField Make()
            {
                var field = new SheetField { Name = Name, Group = Group, Kind = Kind };
                field.Options.AddRange(Options);
                return field;
            }
        }

        /// <summary>Le modèle Personnage, champ par champ, avec les noms
        /// d'avant : la seule définition — le modèle neuf ET la migration.
        /// « Sexe de naissance » (b31) précède « Genre » comme source du genre
        /// de naissance : un modèle qui a les deux garde « Genre » pour le
        /// genre « si différent ».</summary>
        private static List<FieldSpec> CharacterSpecs()
        {
            return new List<FieldSpec>
            {
                S("Nom"), S("Prénom"), S("Alias"), S(FieldAge), S(FieldBirthDate), S("Lieu de naissance"),
                Choice(FieldBirthGender, GroupInfos, GenderOptions, "Sexe de naissance", "Genre", "Sexe"),
                Choice(FieldGender, GroupInfos, GenderOptions, "Genre"),
                S("Lieu de résidence"),
                S("Croyance", GroupInfos, "Religion"),
                S("Capacités/Magie", GroupInfos, "Magie", "Capacités"),
                S("Affiliation"),
                S("Taille", GroupLooks), S("Poids", GroupLooks),
                S("Couleur des yeux", GroupLooks, "Yeux"),
                S("Couleur des cheveux", GroupLooks, "Cheveux"),
                S("Teinte de peau", GroupLooks, "Peau", "Couleur de peau"),
                S("Traits", GroupLooks),
                Kind("Particularités", FieldKinds.Multiline, GroupLooks),
                S("En un mot", GroupPersonality), S("Voix", GroupPersonality),
                S("Gestuelle", GroupPersonality), S("Sociabilité", GroupPersonality)
            };
        }

        /// <summary>Le modèle Personnage : Infos, Apparence, Personnalité,
        /// et le paper Relations.</summary>
        private static SheetTemplate Character()
        {
            var t = new SheetTemplate { Name = "Personnage", Relations = true };
            t.Sections.Add(GroupLooks);
            t.Sections.Add(GroupPersonality);
            foreach (var spec in CharacterSpecs()) t.Fields.Add(spec.Make());
            return t;
        }

        /// <summary>Migration v30 (23/09 ; absorbe la v16) du modèle Personnage
        /// d'un projet existant : chaque champ par défaut est retrouvé par son
        /// nom ou un nom d'avant (renommé, même id — les valeurs suivent ; un
        /// « Genre » texte devient un choix, sa valeur d'avant reste proposée),
        /// sinon créé ; les champs par défaut prennent l'ordre livré, les
        /// champs maison suivent, jamais retirés ; sections Apparence et
        /// Personnalité, paper Relations. Idempotente. Rend vrai si changé.</summary>
        public static bool UpgradeCharacterTemplate(SheetTemplate template)
        {
            if (template == null) return false;
            var changed = false;
            var fields = template.Fields;

            // — Les groupes d'avant les sections (b42) : « Physique » →
            //   « Apparence », « Infos » → la section par défaut.
            foreach (var field in fields)
            {
                var migrated = MigrateGroup(field.Group);
                if (migrated != field.Group) { field.Group = migrated; changed = true; }
            }
            if (!template.HasSection(GroupLooks)) { template.Sections.Add(GroupLooks); changed = true; }
            if (!template.HasSection(GroupPersonality)) { template.Sections.Add(GroupPersonality); changed = true; }
            if (!template.Relations) { template.Relations = true; changed = true; }

            var rebuilt = new List<SheetField>();
            foreach (var spec in CharacterSpecs())
            {
                var field = FindField(fields, spec.Name, rebuilt);
                foreach (var legacy in spec.Legacy)
                {
                    if (field != null) break;
                    field = FindField(fields, legacy, rebuilt);
                }
                if (field == null)
                {
                    field = spec.Make();
                    changed = true;
                }
                else
                {
                    if (field.Name != spec.Name) { field.Name = spec.Name; changed = true; }
                    if (!string.Equals(field.Group, spec.Group, StringComparison.CurrentCultureIgnoreCase))
                    { field.Group = spec.Group; changed = true; }
                    if (spec.Kind == FieldKinds.Choice && FieldKinds.Normalize(field.Kind) == FieldKinds.Text && field.Options.Count == 0)
                    {
                        field.Kind = FieldKinds.Choice;
                        field.Options.AddRange(spec.Options);
                        changed = true;
                    }
                }
                rebuilt.Add(field);
            }
            foreach (var field in fields)
                if (!rebuilt.Contains(field)) rebuilt.Add(field);
            for (var i = 0; i < rebuilt.Count && !changed; i++)
                if (i >= fields.Count || !ReferenceEquals(fields[i], rebuilt[i])) changed = true;
            if (changed)
            {
                fields.Clear();
                fields.AddRange(rebuilt);
            }
            return changed;
        }

        /// <summary>Le champ de ce nom (casse et espaces ignorés) qui n'a pas
        /// déjà été pris par un champ par défaut, ou null.</summary>
        private static SheetField FindField(List<SheetField> fields, string name, List<SheetField> taken)
        {
            foreach (var field in fields)
                if (FieldIs(field, name) && !taken.Contains(field)) return field;
            return null;
        }

        private static bool FieldIs(SheetField field, string name)
        {
            return string.Equals(field.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase);
        }

        private static SheetTemplate Simple(string name, params FieldSpec[] specs)
        {
            var template = new SheetTemplate { Name = name };
            foreach (var spec in specs) template.Fields.Add(spec.Make());
            return template;
        }

        private static FieldSpec S(string name)
        {
            return new FieldSpec { Name = name };
        }

        private static FieldSpec S(string name, string group, params string[] legacy)
        {
            return new FieldSpec { Name = name, Group = group, Legacy = legacy };
        }

        private static FieldSpec Kind(string name, string kind)
        {
            return new FieldSpec { Name = name, Kind = kind };
        }

        private static FieldSpec Kind(string name, string kind, string group)
        {
            return new FieldSpec { Name = name, Kind = kind, Group = group };
        }

        private static FieldSpec Choice(string name, string group, params string[] options)
        {
            return new FieldSpec { Name = name, Group = group, Kind = FieldKinds.Choice, Options = options };
        }

        private static FieldSpec Choice(string name, string group, string[] options, params string[] legacy)
        {
            return new FieldSpec { Name = name, Group = group, Kind = FieldKinds.Choice, Options = options, Legacy = legacy };
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
