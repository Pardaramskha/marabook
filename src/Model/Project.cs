using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>An image embedded in the project (sheet portraits, inline text
    /// images). Bytes are written verbatim to the .plot zip.</summary>
    public class ProjectImage
    {
        public byte[] Bytes;
        public string Extension = ".png"; // includes the dot
    }

    /// <summary>A .plot project: metadata plus the Binder tree, whose four fixed
    /// roots are the categories Écrits / Recherche / Fiches / Corbeille.</summary>
    public class Project
    {
        public const string KeyWritings = "writings";
        public const string KeyResearch = "research";
        public const string KeySheets = "sheets";
        public const string KeyDictionary = "dictionary"; // batch 33 : le dictionnaire personnel
        public const string KeyPlans = "plans";           // batch 35 : les plans
        public const string KeyTrash = "trash";

        public string Name = "Sans titre";
        public string Author = "";

        // Runtime only, set at load — never serialized. A file written by a
        // NEWER Marabook (manifest version > FormatVersion) opens read-only:
        // saving it with this version would silently destroy every field this
        // version does not know about.
        public bool ReadOnlyNewerFormat;
        public int LoadedFormatVersion;

        // Scene separator (Format bar + project settings). A null font means
        // "use the body style's font".
        public string SeparatorText = "***";
        public string SeparatorFont;
        public double SeparatorSizePt = 12;

        // Custom text/highlight colors, shared by the whole project (hex).
        public List<string> CustomColors = new List<string>();
        // Exceptions de césure : mots que le compositeur ne coupe jamais
        // (clic droit sur un mot en mode composition).
        public List<string> HyphenExceptions = new List<string>();
        // Mots ignorés par les correcteurs dans CE projet (« ignorer dans ce
        // projet » du menu de signalement) ; la liste globale vit dans les
        // réglages de l'application.
        public List<string> ProofIgnored = new List<string>();
        // Dictionnaire personnel du PROJET : les mots ENSEIGNÉS au correcteur
        // (noms propres du roman, néologismes) — « ajouter au dictionnaire »
        // ≠ « ignorer » : ignorer TAIT un signalement, enseigner APPREND un
        // mot. Le pendant global vit dans les réglages.
        // Depuis le batch 33 : des ENTRÉES avec nature grammaticale (le
        // correcteur accepte leurs formes) — les anciennes listes de chaînes
        // (v9) sont migrées en entrées « autre ».
        public List<LexiconEntry> Lexicon = new List<LexiconEntry>();
        // Règles de correction ignorées dans CE projet (« ignorer cette
        // règle » du menu d'un signalement grammatical — batch 29) : des
        // sRuleId de Grammalecte, filtrés par le pilote après cache.
        public List<string> IgnoredRules = new List<string>();
        public string CreatedAt = "";
        public string ModifiedAt = "";
        public WritingJournal Journal = new WritingJournal();
        public StyleSheet Styles = StyleSheet.CreateDefault();
        // Modèles et catégories de fiches (batch 31) : un projet NEUF est
        // semé par SheetDefaults.Seed (CreateNew) ; un projet chargé reçoit
        // le contenu du .plot, la migration d'EnsureSheetCategories comble
        // les projets d'avant les catégories.
        public List<SheetTemplate> Templates = new List<SheetTemplate>();
        public List<SheetCategory> SheetCategories = new List<SheetCategory>();

        // Natures de relation PERSONNALISÉES du projet (batch 36, v16) —
        // « Mentor », « Rivale »… créées depuis le sélecteur d'une fiche,
        // proposées ensuite sur toutes les fiches. Les natures livrées
        // (RelationKinds.Defaults) ne sont pas stockées ici.
        public List<string> RelationKinds = new List<string>();

        /// <summary>Toutes les natures proposées au sélecteur : les livrées,
        /// puis les personnalisées du projet.</summary>
        public IEnumerable<string> AllRelationKinds()
        {
            foreach (var kind in Model.RelationKinds.Defaults) yield return kind;
            foreach (var kind in RelationKinds) yield return kind;
        }

        /// <summary>Enregistre une nature personnalisée (sans doublon avec les
        /// livrées ni les existantes) ; rend la forme retenue.</summary>
        public string AddRelationKind(string kind)
        {
            var trimmed = (kind ?? "").Trim();
            if (trimmed.Length == 0) return "";
            foreach (var known in AllRelationKinds())
                if (Model.RelationKinds.Same(known, trimmed)) return known;
            RelationKinds.Add(trimmed);
            return trimmed;
        }
        public PageSetup Page = new PageSetup();
        public Dictionary<string, ProjectImage> Images = new Dictionary<string, ProjectImage>();
        public List<BinderItem> Roots = new List<BinderItem>();

        /// <summary>Registers image bytes in the store and returns their id.</summary>
        public string AddImage(byte[] bytes, string extension)
        {
            var image = new ProjectImage
            {
                Bytes = bytes,
                Extension = string.IsNullOrEmpty(extension) ? ".png" : extension.ToLowerInvariant()
            };
            var id = Guid.NewGuid().ToString("N");
            Images[id] = image;
            return id;
        }

        public ProjectImage FindImage(string id)
        {
            ProjectImage image;
            return id != null && Images.TryGetValue(id, out image) ? image : null;
        }

        /// <summary>Drops images no item references anymore (called at save so
        /// deleted pictures do not bloat the .plot forever).</summary>
        public void PurgeUnusedImages()
        {
            var used = new HashSet<string>();
            foreach (var item in AllItems())
            {
                if (item.ImageId != null) used.Add(item.ImageId);
                if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) continue;
                foreach (var paragraph in item.Document.Paragraphs)
                    foreach (var run in paragraph.Runs)
                        if (run.ImageId != null) used.Add(run.ImageId);
            }
            var stale = new List<string>();
            foreach (var id in Images.Keys)
                if (!used.Contains(id)) stale.Add(id);
            foreach (var id in stale) Images.Remove(id);
        }

        public SheetTemplate FindTemplate(string id)
        {
            foreach (var template in Templates)
                if (template.Id == id) return template;
            return null;
        }

        public SheetCategory FindSheetCategory(string id)
        {
            foreach (var category in SheetCategories)
                if (category.Id == id) return category;
            return null;
        }

        /// <summary>La catégorie d'une fiche : la sienne si elle existe encore,
        /// sinon celle qui porte son modèle (fiches d'avant les catégories),
        /// sinon null (« Sans catégorie » dans la bibliothèque).</summary>
        public SheetCategory SheetCategoryOf(BinderItem sheet)
        {
            if (sheet == null) return null;
            var own = FindSheetCategory(sheet.CategoryId);
            if (own != null) return own;
            if (sheet.TemplateId != null)
                foreach (var category in SheetCategories)
                    if (category.TemplateId == sheet.TemplateId) return category;
            return null;
        }

        /// <summary>Migration des catégories (batch 31), idempotente — appelée
        /// au chargement. Un projet d'avant la v11 : les sept catégories
        /// livrées se créent, chacune adopte le modèle existant de même nom
        /// (les fiches gardent leurs valeurs) ou reçoit le modèle par défaut ;
        /// tout modèle restant devient sa propre catégorie personnalisée.
        /// Les fiches sans catégorie rejoignent celle de leur modèle.</summary>
        public void EnsureSheetCategories()
        {
            if (SheetCategories.Count == 0)
            {
                foreach (var name in SheetDefaults.CategoryNames)
                {
                    SheetTemplate adopted = null;
                    foreach (var template in Templates)
                        if (template.Name == name) { adopted = template; break; }
                    if (adopted == null)
                    {
                        adopted = SheetDefaults.TemplateFor(name);
                        Templates.Add(adopted);
                    }
                    SheetCategories.Add(new SheetCategory
                    {
                        Name = name,
                        TemplateId = adopted.Id
                    });
                }
                foreach (var template in Templates)
                {
                    var owned = false;
                    foreach (var category in SheetCategories)
                        if (category.TemplateId == template.Id) { owned = true; break; }
                    if (!owned)
                        SheetCategories.Add(new SheetCategory
                        {
                            Name = template.Name,
                            TemplateId = template.Id
                        });
                }
            }
            foreach (var item in AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                if (FindSheetCategory(item.CategoryId) != null) continue;
                var home = SheetCategoryOf(item);
                item.CategoryId = home != null ? home.Id
                    : SheetCategories.Count > 0 ? SheetCategories[0].Id : null;
            }
        }

        /// <summary>Le modèle de base de la catégorie « Personnage » (ou, à
        /// défaut, le modèle qui porte ce nom) — null s'il n'y en a pas.</summary>
        public SheetTemplate CharacterTemplate()
        {
            foreach (var category in SheetCategories)
                if (category.Name == "Personnage")
                {
                    var template = FindTemplate(category.TemplateId);
                    if (template != null) return template;
                }
            foreach (var template in Templates)
                if (template.Name == "Personnage") return template;
            return null;
        }

        /// <summary>Migration v16 (batch 36) : « Âge » et l'apparence par
        /// défaut sur le modèle Personnage — voir
        /// SheetDefaults.UpgradeCharacterTemplate. Rend vrai si changé.</summary>
        public bool UpgradeCharacterTemplate()
        {
            var template = CharacterTemplate();
            if (template == null) return false;
            var sheets = new List<BinderItem>();
            foreach (var item in AllItems())
                if (item.Kind == ItemKind.Sheet && item.TemplateId == template.Id) sheets.Add(item);
            return SheetDefaults.UpgradeCharacterTemplate(template, sheets);
        }

        /// <summary>First item whose title matches (case- and accent-insensitive),
        /// for [[wiki link]] navigation. Sheets win over other kinds.</summary>
        public BinderItem FindByTitle(string title)
        {
            BinderItem fallback = null;
            foreach (var item in AllItems())
            {
                if (item.IsCategory) continue;
                if (string.Compare(item.Title, title,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.CompareOptions.IgnoreCase
                    | System.Globalization.CompareOptions.IgnoreNonSpace) != 0) continue;
                if (item.Kind == ItemKind.Sheet) return item;
                if (fallback == null) fallback = item;
            }
            return fallback;
        }

        public static Project CreateNew()
        {
            var project = new Project();
            SheetDefaults.Seed(project.Templates, project.SheetCategories);
            project.Roots.Add(MakeCategory("Écrits", KeyWritings));
            project.Roots.Add(MakeCategory("Recherche", KeyResearch));
            project.Roots.Add(MakeCategory("Fiches", KeySheets));
            project.Roots.Add(MakeCategory("Plans", KeyPlans));
            project.Roots.Add(MakeCategory("Dictionnaire", KeyDictionary));
            project.Roots.Add(MakeCategory("Corbeille", KeyTrash));

            var first = new BinderItem();
            first.Kind = ItemKind.Text;
            first.Title = "Nouvel écrit";
            // Un document ne vit JAMAIS à zéro paragraphe (famille du crash
            // « Composition impossible » du batch 14 : les gardes aval
            // existaient, la source non). Le chargement normalise déjà ainsi.
            first.Document.Paragraphs.Add(new TextParagraph());
            var writings = project.Category(KeyWritings);
            first.Parent = writings;
            writings.Children.Add(first);
            return project;
        }

        private static BinderItem MakeCategory(string title, string key)
        {
            var category = new BinderItem();
            category.Kind = ItemKind.Category;
            category.Title = title;
            category.CategoryKey = key;
            category.Id = key; // stable id: categories are addressable across versions
            return category;
        }

        public BinderItem Category(string key)
        {
            foreach (var root in Roots)
                if (root.CategoryKey == key) return root;
            return null;
        }

        public BinderItem Trash { get { return Category(KeyTrash); } }

        /// <summary>Le plan dont une colonne est reliée à cet écrit (batch 35), ou null.</summary>
        public BinderItem PlanForText(string textId)
        {
            if (textId == null) return null;
            foreach (var item in AllItems())
                if (item.Kind == ItemKind.Plan && item.Plan != null && item.Plan.ColumnOf(textId) != null
                    && item.RootCategory().CategoryKey != KeyTrash)
                    return item;
            return null;
        }

        /// <summary>Le plan relié à ce livre ou dossier (batch 35), ou null.</summary>
        public BinderItem PlanForContainer(string itemId)
        {
            if (itemId == null) return null;
            foreach (var item in AllItems())
                if (item.Kind == ItemKind.Plan && item.Plan != null && item.Plan.LinkedItemId == itemId
                    && item.RootCategory().CategoryKey != KeyTrash)
                    return item;
            return null;
        }

        public BinderItem FindById(string id)
        {
            foreach (var item in AllItems())
                if (item.Id == id) return item;
            return null;
        }

        public IEnumerable<BinderItem> AllItems()
        {
            var queue = new Queue<BinderItem>(Roots);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                yield return item;
                foreach (var child in item.Children) queue.Enqueue(child);
            }
        }

        /// <summary>Rebuilds Parent pointers after deserialization.</summary>
        public void RelinkParents()
        {
            foreach (var root in Roots)
            {
                root.Parent = null;
                root.RelinkChildren();
            }
        }
    }
}
