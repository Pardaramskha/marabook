using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Ce que l'export prend : tout, une catégorie (et ses
    /// sous-catégories), ou des fiches nommées.</summary>
    public class EncyclopediaSelection
    {
        public const string ScopeAll = "all", ScopeCategory = "category", ScopeSheets = "sheets";
        public string Scope = ScopeAll;
        public readonly List<string> Ids = new List<string>(); // catégories (ScopeCategory) ou fiches (ScopeSheets)
        public bool IncludeSubcategories = true;

        public static EncyclopediaSelection All() { return new EncyclopediaSelection(); }
        public static EncyclopediaSelection Category(string categoryId) { var s = new EncyclopediaSelection { Scope = ScopeCategory }; s.Ids.Add(categoryId); return s; }
        public static EncyclopediaSelection Sheet(string sheetId) { var s = new EncyclopediaSelection { Scope = ScopeSheets }; s.Ids.Add(sheetId); return s; }
    }

    /// <summary>L'ENCYCLOPÉDIE (1.1.0, 31/10, Rémi) : le document PIVOT de
    /// l'export des fiches — des données, pas des contrôles — que chaque
    /// rendu (HTML, PDF) dessine à sa façon. Sections = catégories (les
    /// sous-catégories imbriquées), chaque fiche = titre, portrait, infobox
    /// (groupes de champs remplis, comme le wiki), corps Markdown, relations,
    /// évolution et présence, radar, tags ; puis le Lexique en dictionnaire.
    /// Les liens ([[…]], relations, champs « fiche ») ne pointent que vers
    /// des fiches EXPORTÉES — sinon du texte, jamais un lien mort.</summary>
    public class EncyclopediaDocument
    {
        public string Title = "", Subtitle = "", Author = "";
        public string Date = "";              // la date de l'export, affichée si ShowDates
        public string ProjectName = "";
        public string CoverImageId;
        public EncyclopediaOptions Options;
        public readonly List<EncyclopediaSection> Sections = new List<EncyclopediaSection>();
        public readonly List<EncyclopediaLexiconEntry> Lexicon = new List<EncyclopediaLexiconEntry>();
        public readonly Dictionary<string, EncyclopediaSheet> ById = new Dictionary<string, EncyclopediaSheet>();
        /// <summary>Les images employées (portraits, couverture, images du corps) : id → extension.</summary>
        public readonly Dictionary<string, string> Images = new Dictionary<string, string>();

        /// <summary>Toutes les fiches, dans l'ordre de lecture (sections puis sous-sections).</summary>
        public List<EncyclopediaSheet> AllSheets()
        {
            var list = new List<EncyclopediaSheet>();
            foreach (var section in Sections) Collect(section, list);
            return list;
        }

        private static void Collect(EncyclopediaSection section, List<EncyclopediaSheet> into)
        {
            into.AddRange(section.Sheets);
            foreach (var sub in section.Subsections) Collect(sub, into);
        }

        public int SheetCount { get { return AllSheets().Count; } }

        public EncyclopediaSheet FindByTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var wanted = title.Trim();
            foreach (var sheet in ById.Values)
                if (string.Equals(sheet.Title.Trim(), wanted, StringComparison.CurrentCultureIgnoreCase)) return sheet;
            return null;
        }
    }

    public class EncyclopediaSection
    {
        public string Id = "", Title = "", Slug = "";
        public string Color;   // la couleur de la sous-catégorie, null = neutre
        public EncyclopediaSection Parent;
        public readonly List<EncyclopediaSheet> Sheets = new List<EncyclopediaSheet>();
        public readonly List<EncyclopediaSection> Subsections = new List<EncyclopediaSection>();
        public int Count { get { var n = Sheets.Count; foreach (var sub in Subsections) n += sub.Count; return n; } }
    }

    public class EncyclopediaSheet
    {
        public string Id = "", Title = "", Slug = "";
        public string Subtitle = "";          // le nom du modèle (« Personnage »)
        public string ImageId;                // le portrait
        public EncyclopediaSection Section;
        public string BodyMarkdown = "";
        public readonly List<EncyclopediaGroup> Groups = new List<EncyclopediaGroup>();
        public readonly List<EncyclopediaRelation> Relations = new List<EncyclopediaRelation>();
        public readonly List<EncyclopediaStep> Evolution = new List<EncyclopediaStep>();
        public readonly List<EncyclopediaPresence> Presence = new List<EncyclopediaPresence>();
        public EncyclopediaRadar Radar;       // null = pas de radar
        public readonly List<string> Tags = new List<string>();
        public bool IsEmpty { get { return Groups.Count == 0 && BodyMarkdown.Trim().Length == 0 && Relations.Count == 0 && Evolution.Count == 0 && Presence.Count == 0 && Radar == null; } }
    }

    public class EncyclopediaGroup
    {
        public string Title = "";
        public readonly List<EncyclopediaField> Fields = new List<EncyclopediaField>();
    }

    public class EncyclopediaField
    {
        public string Label = "", Kind = FieldKinds.Text, Value = "";
        public readonly List<string> Items = new List<string>(); // liste
        public string TargetId;        // champ « fiche » : la fiche exportée visée, sinon null (Value = son titre)
    }

    public class EncyclopediaRelation
    {
        public string Kind = "", Label = "";
        public string TargetId;        // la fiche exportée, sinon null : Label seul
    }

    public class EncyclopediaStep { public string Label = "", Note = ""; public string TextId; }
    public class EncyclopediaPresence { public string TextTitle = ""; public int Count; }

    public class EncyclopediaRadar
    {
        public string Label = "";
        public int Max = 5;
        public readonly List<KeyValuePair<string, double>> Axes = new List<KeyValuePair<string, double>>();
    }

    public class EncyclopediaLexiconEntry
    {
        public string Word = "", ClassLabel = "", Definition = "", Note = "";
        public readonly List<string> Forms = new List<string>();
        public string Letter = "#";    // la lettre d'index (A–Z, # sinon)
    }

    public static class Encyclopedia
    {
        public const string UncategorizedTitle = "Sans catégorie";

        /// <summary>Réduit une image (octets d'origine, largeur maximale en px)
        /// et la rend en PNG ; null = garder l'original. Posé par l'application
        /// (le cœur ne décode pas d'image) ; les tests n'en ont pas.</summary>
        public static Func<byte[], int, byte[]> ShrinkImage;

        public const int PortraitMaxWidth = 800, BodyImageMaxWidth = 1200, CoverMaxWidth = 1600;

        public static EncyclopediaOptions DefaultOptions(Project project)
        {
            var options = project != null && project.Encyclopedia != null ? project.Encyclopedia.Clone() : new EncyclopediaOptions();
            return options;
        }

        // ================================================================ la collecte

        public static EncyclopediaDocument Build(Project project, EncyclopediaSelection selection, EncyclopediaOptions options)
        {
            return Build(project, selection, options, null);
        }

        /// <summary>globalLexicon : le dictionnaire de tous les projets (réglages),
        /// null = rien ; le projet seul suffit aux tests.</summary>
        public static EncyclopediaDocument Build(Project project, EncyclopediaSelection selection, EncyclopediaOptions options, IEnumerable<LexiconEntry> globalLexicon)
        {
            if (project == null) throw new ArgumentNullException("project");
            selection = selection ?? EncyclopediaSelection.All();
            options = (options ?? new EncyclopediaOptions()).Clone();
            var document = new EncyclopediaDocument
            {
                Options = options,
                Title = options.TitleFor(project),
                Subtitle = (options.Subtitle ?? "").Trim(),
                Author = options.AuthorFor(project),
                Date = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                ProjectName = project.Name ?? "",
                CoverImageId = options.CoverImageId != null && project.FindImage(options.CoverImageId) != null ? options.CoverImageId : null
            };
            if (document.CoverImageId != null) RegisterImage(document, project, document.CoverImageId);

            // 1. Les fiches retenues, et leur section.
            var sheets = SelectSheets(project, selection, options);
            var sectionById = new Dictionary<string, EncyclopediaSection>();
            var slugs = new HashSet<string>();
            foreach (var category in project.SheetCategories)
            {
                if (category.IsSub) continue;
                var section = MakeSection(category, slugs);
                sectionById[category.Id] = section;
                foreach (var sub in project.SheetCategories)
                {
                    if (sub.ParentId != category.Id) continue;
                    var child = MakeSection(sub, slugs);
                    child.Parent = section;
                    sectionById[sub.Id] = child;
                    section.Subsections.Add(child);
                }
                document.Sections.Add(section);
            }
            EncyclopediaSection uncategorized = null;
            var sheetSlugs = new HashSet<string>();
            foreach (var item in sheets)
            {
                EncyclopediaSection section = null;
                if (item.CategoryId != null) sectionById.TryGetValue(item.CategoryId, out section);
                if (section == null)
                {
                    if (uncategorized == null)
                    {
                        uncategorized = new EncyclopediaSection { Id = "uncategorized", Title = UncategorizedTitle, Slug = Slug(UncategorizedTitle, slugs) };
                        document.Sections.Add(uncategorized);
                    }
                    section = uncategorized;
                }
                var sheet = new EncyclopediaSheet { Id = item.Id, Title = TitleOf(item), Section = section };
                sheet.Slug = Slug(sheet.Title, sheetSlugs);
                section.Sheets.Add(sheet);
                document.ById[item.Id] = sheet;
            }
            // Les sections vides tombent ; les fiches se rangent par titre.
            document.Sections.RemoveAll(delegate(EncyclopediaSection s) { Prune(s); return s.Count == 0; });
            foreach (var section in document.Sections) SortSheets(section);

            // 2. Le contenu de chaque fiche — les liens ne visent que l'exporté.
            foreach (var item in sheets)
            {
                EncyclopediaSheet sheet;
                if (!document.ById.TryGetValue(item.Id, out sheet)) continue;
                Fill(sheet, item, project, document, options);
            }
            if (!options.IncludeEmptySheets)
            {
                foreach (var section in document.Sections) DropEmpty(section, document);
                document.Sections.RemoveAll(delegate(EncyclopediaSection s) { return s.Count == 0; });
            }

            // 3. Le Lexique.
            if (options.IncludeLexicon) FillLexicon(document, project, options, globalLexicon);
            return document;
        }

        /// <summary>Les fiches du projet dans la sélection : hors corbeille,
        /// par ordre de l'arbre (le tri par titre vient après).</summary>
        public static List<BinderItem> SelectSheets(Project project, EncyclopediaSelection selection, EncyclopediaOptions options)
        {
            var wantedCategories = new HashSet<string>();
            var wantedSheets = new HashSet<string>(selection.Ids);
            if (selection.Scope == EncyclopediaSelection.ScopeCategory)
                foreach (var id in selection.Ids)
                {
                    wantedCategories.Add(id);
                    if (selection.IncludeSubcategories)
                        foreach (var sub in project.SheetCategories)
                            if (sub.ParentId == id) wantedCategories.Add(sub.Id);
                }
            var list = new List<BinderItem>();
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                if (selection.Scope == EncyclopediaSelection.ScopeSheets && !wantedSheets.Contains(item.Id)) continue;
                if (selection.Scope == EncyclopediaSelection.ScopeCategory && (item.CategoryId == null || !wantedCategories.Contains(item.CategoryId))) continue;
                list.Add(item);
            }
            return list;
        }

        private static EncyclopediaSection MakeSection(SheetCategory category, HashSet<string> slugs)
        {
            return new EncyclopediaSection
            {
                Id = category.Id,
                Title = string.IsNullOrEmpty((category.Name ?? "").Trim()) ? "Catégorie" : category.Name.Trim(),
                Color = EncyclopediaOptions.SafeColor(category.Color, null),
                Slug = Slug(category.Name, slugs)
            };
        }

        private static void Prune(EncyclopediaSection section)
        {
            section.Subsections.RemoveAll(delegate(EncyclopediaSection s) { Prune(s); return s.Count == 0; });
        }

        private static void DropEmpty(EncyclopediaSection section, EncyclopediaDocument document)
        {
            section.Sheets.RemoveAll(delegate(EncyclopediaSheet s) { if (s.IsEmpty) { document.ById.Remove(s.Id); return true; } return false; });
            foreach (var sub in section.Subsections) DropEmpty(sub, document);
            section.Subsections.RemoveAll(delegate(EncyclopediaSection s) { return s.Count == 0; });
        }

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private static void SortSheets(EncyclopediaSection section)
        {
            section.Sheets.Sort(delegate(EncyclopediaSheet a, EncyclopediaSheet b) { return string.Compare(a.Title, b.Title, French, CompareOptions.IgnoreCase); });
            foreach (var sub in section.Subsections) SortSheets(sub);
        }

        private static string TitleOf(BinderItem item)
        {
            return string.IsNullOrEmpty((item.Title ?? "").Trim()) ? "Sans titre" : item.Title.Trim();
        }

        // ================================================================ une fiche

        private static void Fill(EncyclopediaSheet sheet, BinderItem item, Project project, EncyclopediaDocument document, EncyclopediaOptions options)
        {
            var template = project.TemplateOf(item);
            sheet.Subtitle = template == null ? "" : (template.Name ?? "").Trim();
            if (item.ImageId != null && project.FindImage(item.ImageId) != null)
            {
                sheet.ImageId = item.ImageId;
                RegisterImage(document, project, item.ImageId);
            }
            sheet.BodyMarkdown = item.Document == null ? "" : item.Document.ToPlainText() ?? "";
            foreach (var id in MarkdownDialect.StoredImageIds(sheet.BodyMarkdown))
                if (project.FindImage(id) != null) RegisterImage(document, project, id);

            // L'infobox : les groupes du modèle puis ceux des champs libres,
            // dans l'ordre du wiki (SheetWiki.BuildSections) ; seuls les
            // champs remplis comptent, un groupe sans champ ne paraît pas.
            if (options.ShowInfobox)
            {
                var names = new List<string> { "" };
                if (template != null)
                {
                    foreach (var section in template.Sections) if (SectionKey(names, section) == null) names.Add(section);
                    foreach (var field in template.Fields) if (SectionKey(names, field.Group) == null) names.Add(field.Group ?? "");
                }
                foreach (var entry in item.FreeInfo) if (SectionKey(names, entry.Group) == null) names.Add(entry.Group ?? "");
                foreach (var name in names)
                {
                    var group = new EncyclopediaGroup { Title = name.Length == 0 ? SheetDefaults.DefaultSectionLabel : name };
                    if (template != null)
                        foreach (var field in template.Fields)
                        {
                            if (!string.Equals(SectionKey(names, field.Group), name, StringComparison.Ordinal)) continue;
                            string value;
                            item.FieldValues.TryGetValue(field.Id, out value);
                            if (string.IsNullOrEmpty(value)) continue;
                            group.Fields.Add(MakeField(field.Name, field.Kind, value, project, document));
                        }
                    foreach (var entry in item.FreeInfo)
                        if (!string.IsNullOrEmpty(entry.Value) && string.Equals(SectionKey(names, entry.Group), name, StringComparison.Ordinal))
                            group.Fields.Add(MakeField(entry.Title, entry.Kind, entry.Value, project, document));
                    if (group.Fields.Count > 0) sheet.Groups.Add(group);
                }
            }

            if (options.ShowRelations)
                foreach (var relation in item.Relations)
                {
                    var target = relation.TargetId == null ? null : project.FindById(relation.TargetId);
                    var label = target != null ? TitleOf(target) : (relation.Name ?? "").Trim();
                    var kind = RelationKinds.Canonical(relation.Kind ?? "");
                    if (label.Length == 0 && kind.Length == 0) continue;
                    sheet.Relations.Add(new EncyclopediaRelation
                    {
                        Kind = kind,
                        Label = label,
                        TargetId = target != null && document.ById.ContainsKey(target.Id) ? target.Id : null
                    });
                }

            if (options.ShowPresence)
            {
                var showSteps = (template != null && template.Evolution) || item.Evolution.Count > 0;
                if (showSteps)
                    foreach (var step in Presence.OrderedSteps(item, project))
                    {
                        var label = step.Label(project) ?? "";
                        var note = (step.Note ?? "").Trim();
                        if (label.Length == 0 && note.Length == 0) continue;
                        sheet.Evolution.Add(new EncyclopediaStep { Label = label, Note = note, TextId = step.TextId });
                    }
                foreach (var row in Presence.Of(item, template, project))
                    sheet.Presence.Add(new EncyclopediaPresence { TextTitle = TitleOf(row.Text), Count = row.Count });
            }

            if (options.ShowRadar && template != null && template.ShowsRadar)
            {
                var radar = new EncyclopediaRadar { Label = template.RadarLabel, Max = Math.Max(1, template.RadarMax) };
                var any = false;
                foreach (var axis in template.RadarAxes)
                {
                    double value;
                    item.RadarValues.TryGetValue(axis.Id, out value);
                    value = Math.Max(0, Math.Min(radar.Max, value));
                    if (value > 0) any = true;
                    radar.Axes.Add(new KeyValuePair<string, double>(axis.Name ?? "", value));
                }
                if (any) sheet.Radar = radar;
            }

            if (options.ShowTags) foreach (var tag in item.Tags) if (!string.IsNullOrEmpty(tag)) sheet.Tags.Add(tag);
        }

        private static EncyclopediaField MakeField(string label, string kind, string value, Project project, EncyclopediaDocument document)
        {
            var field = new EncyclopediaField { Label = label ?? "", Kind = FieldKinds.Normalize(kind) };
            switch (field.Kind)
            {
                case FieldKinds.List:
                    field.Items.AddRange(FieldKinds.ListItems(value));
                    field.Value = string.Join(" · ", field.Items.ToArray());
                    break;
                case FieldKinds.Sheet:
                {
                    var target = FieldKinds.SheetOf(value, project);
                    field.Value = FieldKinds.Display(kind, value, project);
                    field.TargetId = target != null && document.ById.ContainsKey(target.Id) ? target.Id : null;
                    break;
                }
                default:
                    field.Value = FieldKinds.Display(kind, value, project);
                    break;
            }
            return field;
        }

        /// <summary>Le nom de section déjà connu qui correspond (sans casse), ou null — comme SheetWiki.</summary>
        private static string SectionKey(List<string> names, string group)
        {
            var wanted = group ?? "";
            foreach (var name in names)
                if (string.Equals(name, wanted, StringComparison.CurrentCultureIgnoreCase)) return name;
            return null;
        }

        private static void RegisterImage(EncyclopediaDocument document, Project project, string imageId)
        {
            if (imageId == null || document.Images.ContainsKey(imageId)) return;
            var image = project.FindImage(imageId);
            if (image == null) return;
            document.Images[imageId] = string.IsNullOrEmpty(image.Extension) ? ".png" : image.Extension;
        }

        // ================================================================ le Lexique

        private static void FillLexicon(EncyclopediaDocument document, Project project, EncyclopediaOptions options, IEnumerable<LexiconEntry> globalLexicon)
        {
            var seen = new HashSet<string>();
            var sources = new List<IEnumerable<LexiconEntry>> { project.Lexicon };
            if (options.LexiconAllProjects && globalLexicon != null) sources.Add(globalLexicon);
            foreach (var source in sources)
                foreach (var entry in source)
                {
                    if (entry == null || string.IsNullOrEmpty((entry.Word ?? "").Trim())) continue;
                    var definition = (entry.Definition ?? "").Trim();
                    if (options.LexiconOnlyDefined && definition.Length == 0) continue;
                    var key = FrenchTokenizer.Fold(entry.Word.Trim());
                    if (!seen.Add(key)) continue; // le projet l'emporte sur le global
                    var line = new EncyclopediaLexiconEntry
                    {
                        Word = entry.Word.Trim(),
                        ClassLabel = LexiconEntry.ClassLabel(entry.Class),
                        Definition = definition,
                        Note = (entry.Note ?? "").Trim(),
                        Letter = LetterOf(entry.Word)
                    };
                    foreach (var form in entry.Forms())
                        if (!string.IsNullOrEmpty(form) && !string.Equals(form, line.Word, StringComparison.CurrentCultureIgnoreCase) && !line.Forms.Contains(form)) line.Forms.Add(form);
                    document.Lexicon.Add(line);
                }
            document.Lexicon.Sort(delegate(EncyclopediaLexiconEntry a, EncyclopediaLexiconEntry b) { return string.Compare(a.Word, b.Word, French, CompareOptions.IgnoreCase); });
        }

        /// <summary>La lettre d'index d'un mot : sa première lettre pliée, en
        /// capitale ; « # » si ce n'est pas une lettre.</summary>
        public static string LetterOf(string word)
        {
            var folded = FrenchTokenizer.Fold((word ?? "").Trim());
            if (folded.Length == 0) return "#";
            var c = folded[0];
            return c >= 'a' && c <= 'z' ? char.ToUpperInvariant(c).ToString() : c >= 'A' && c <= 'Z' ? c.ToString() : "#";
        }

        // ================================================================ les noms de fichiers

        /// <summary>Un nom sûr pour un fichier ou une ancre : plié en ASCII,
        /// minuscules, tirets ; jamais vide ; unique dans l'ensemble donné.</summary>
        public static string Slug(string title, HashSet<string> taken)
        {
            var folded = FrenchTokenizer.Fold((title ?? "").Trim());
            var sb = new StringBuilder();
            var dash = false;
            foreach (var c in folded)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); dash = false; }
                else if (c >= 'A' && c <= 'Z') { sb.Append(char.ToLowerInvariant(c)); dash = false; }
                else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            }
            var slug = sb.ToString().TrimEnd('-');
            if (slug.Length == 0) slug = "fiche";
            if (slug.Length > 60) slug = slug.Substring(0, 60).TrimEnd('-');
            if (taken == null) return slug;
            var candidate = slug;
            var n = 2;
            while (!taken.Add(candidate)) candidate = slug + "-" + n++;
            return candidate;
        }
    }
}
