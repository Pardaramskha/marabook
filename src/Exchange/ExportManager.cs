using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>LE GESTIONNAIRE D'EXPORT (2.0.0, 18/11/2026, Rémi) — le cœur,
    /// sans interface : les formats, les PRÉRÉGLAGES (ceux de la maison et
    /// ceux de l'utilisateur), les informations du livre, et le PLAN d'un
    /// export : ce que l'ampleur choisie contient, ce que le format sait
    /// porter, ce qu'il écarte, et les incohérences à signaler.</summary>
    public static class ExportFormats
    {
        public const string Docx = "docx", Pdf = "pdf", Epub = "epub", Odt = "odt", Rtf = "rtf", Markdown = "md", Text = "txt";

        public static readonly string[] All = { Docx, Pdf, Epub, Odt, Rtf, Markdown, Text };

        public static string Label(string format)
        {
            switch (format)
            {
                case Docx: return "Document Word (.docx)";
                case Pdf: return "PDF (.pdf)";
                case Epub: return "EPUB (.epub)";
                case Odt: return "OpenDocument (.odt)";
                case Rtf: return "Texte enrichi (.rtf)";
                case Markdown: return "Markdown (.md)";
                default: return "Texte brut (.txt)";
            }
        }

        public static string Short(string format)
        {
            switch (format)
            {
                case Docx: return "Word";
                case Pdf: return "PDF";
                case Epub: return "EPUB";
                case Odt: return "OpenDocument";
                case Rtf: return "RTF";
                case Markdown: return "Markdown";
                default: return "texte brut";
            }
        }

        public static string Normalize(string format)
        {
            foreach (var known in All) if (known == format) return known;
            return Docx;
        }

        public static string Extension(string format) { return "." + Normalize(format); }

        /// <summary>Le filtre du dialogue d'enregistrement.</summary>
        public static string Filter(string format)
        {
            format = Normalize(format);
            return Label(format).Replace(" (." + format + ")", "") + " (*." + format + ")|*." + format;
        }

        /// <summary>Le format porte une mise en page (format de papier, marges).</summary>
        public static bool HasPages(string format) { return format == Docx || format == Pdf; }

        /// <summary>Ce qu'un format sait porter : les écrits partout ; les
        /// fiches partout sauf dans un EPUB ; les plans et les cartes mentales
        /// (des images à leur propre format) dans un PDF seulement.</summary>
        public static bool Carries(string format, ItemKind kind)
        {
            if (kind == ItemKind.Text) return true;
            if (kind == ItemKind.Sheet) return format != Epub;
            if (kind == ItemKind.Plan || kind == ItemKind.MindMap) return format == Pdf;
            return false;
        }
    }

    /// <summary>Le format de page d'un export.</summary>
    public static class ExportPages
    {
        public const string A4 = "a4";         // tout en A4, marges égales
        public const string Source = "source"; // chaque élément garde sa mise en page
        public const string Book = "book";     // le gabarit intérieur du livre

        public static string Normalize(string pages)
        {
            return pages == Source || pages == Book ? pages : A4;
        }

        /// <summary>L'A4 des exports : 25 mm tout autour, sans miroir, folio centré.</summary>
        public static PageSetup A4Setup()
        {
            return new PageSetup
            {
                PageWidthMm = 210, PageHeightMm = 297,
                MarginTopMm = 25, MarginBottomMm = 25, MarginLeftMm = 25, MarginRightMm = 25,
                FooterPageNumbers = true
            };
        }

        /// <summary>La même page, marges de côté égales (« sans miroir ») :
        /// petit fond et grand fond prennent leur moyenne.</summary>
        public static PageSetup Symmetric(PageSetup setup)
        {
            var copy = (setup ?? new PageSetup()).Clone();
            var side = (copy.MarginLeftMm + copy.MarginRightMm) / 2;
            copy.MarginLeftMm = side;
            copy.MarginRightMm = side;
            return copy;
        }

        /// <summary>« A5 », « A4 », sinon « 148 × 210 mm ».</summary>
        public static string Describe(PageSetup setup)
        {
            if (setup == null) return "";
            var w = Math.Min(setup.PageWidthMm, setup.PageHeightMm);
            var h = Math.Max(setup.PageWidthMm, setup.PageHeightMm);
            string name = null;
            if (Near(w, 210) && Near(h, 297)) name = "A4";
            else if (Near(w, 148) && Near(h, 210)) name = "A5";
            else if (Near(w, 105) && Near(h, 148)) name = "A6";
            else if (Near(w, 297) && Near(h, 420)) name = "A3";
            else if (Near(w, 110) && Near(h, 178)) name = "poche";
            if (name != null) return setup.PageWidthMm > setup.PageHeightMm ? name + " paysage" : name;
            return Number(setup.PageWidthMm) + " × " + Number(setup.PageHeightMm) + " mm";
        }

        public static bool SameSize(PageSetup a, PageSetup b)
        {
            if (a == null || b == null) return a == b;
            return Near(a.PageWidthMm, b.PageWidthMm) && Near(a.PageHeightMm, b.PageHeightMm);
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) < 0.6; }
        private static string Number(double value) { return value.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")); }
    }

    /// <summary>UN PRÉRÉGLAGE D'EXPORT : le format et toutes ses options —
    /// jamais l'ampleur (ce qu'on exporte), jamais les informations du livre.</summary>
    public class ExportPreset
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Préréglage";
        public string Category = ExportPresets.CategoryCustom;
        public string Note = "";
        public bool BuiltIn;

        public string Format = ExportFormats.Docx;
        public string Pages = ExportPages.A4;

        // --- Structure
        public bool TitlePage;
        public bool ChapterHeadings = true;   // le titre de chaque écrit en tête (dès deux écrits ; toujours dans un EPUB)
        public bool NumberChapters;
        public bool PageBreakPerText = true;  // sinon le séparateur de scène
        public bool RectoStarts;              // chaque écrit ouvre une belle page

        // --- Typographie et pages
        public bool HeadersFooters;           // les en-têtes et pieds des gabarits ; sinon un folio centré
        public bool PlainMargins;             // marges de côté égales, sans miroir
        public double MaxFontPt;              // 0 = libre ; sinon toute police plus grande y revient
        public double LineSpacing;            // 0 = celui des styles ; sinon ce multiple du corps (1,25…)
        public bool Comments = true;          // Word : les annotations partent en commentaires

        // --- PDF
        public bool Links;                    // liens cliquables entre les éléments exportés
        public bool Outline;                  // signets (le volet de navigation du lecteur PDF)
        public double BleedMm;
        public bool BookBleed;                // le fond perdu du livre plutôt que BleedMm
        public bool CropMarks, BleedGuides, Cmyk, Booklet;

        // --- EPUB
        public bool Cover;

        // --- Contrôles
        public bool CheckConsistency;         // avertir avant d'exporter (formats mêlés, informations manquantes)

        public ExportPreset Clone() { return (ExportPreset)MemberwiseClone(); }

        /// <summary>L'empreinte des OPTIONS (ni nom, ni note) : deux préréglages
        /// de même empreinte exportent pareil — c'est ce qui dit « modifié ».</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(ExportFormats.Normalize(Format)).Append('|').Append(ExportPages.Normalize(Pages));
            foreach (var flag in new[] { TitlePage, ChapterHeadings, NumberChapters, PageBreakPerText, RectoStarts, HeadersFooters, PlainMargins, Comments, Links, Outline, BookBleed, CropMarks, BleedGuides, Cmyk, Booklet, Cover, CheckConsistency })
                sb.Append(flag ? '1' : '0');
            sb.Append('|').Append(MaxFontPt.ToString("0.##", CultureInfo.InvariantCulture))
              .Append('|').Append(LineSpacing.ToString("0.##", CultureInfo.InvariantCulture))
              .Append('|').Append(BleedMm.ToString("0.##", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object>();
            o["id"] = Id; o["name"] = Name; o["note"] = Note ?? "";
            o["format"] = ExportFormats.Normalize(Format); o["pages"] = ExportPages.Normalize(Pages);
            o["titlePage"] = TitlePage; o["chapterHeadings"] = ChapterHeadings; o["numberChapters"] = NumberChapters;
            o["pageBreakPerText"] = PageBreakPerText; o["rectoStarts"] = RectoStarts;
            o["headersFooters"] = HeadersFooters; o["plainMargins"] = PlainMargins;
            o["maxFontPt"] = MaxFontPt; o["lineSpacing"] = LineSpacing; o["comments"] = Comments;
            o["links"] = Links; o["outline"] = Outline;
            o["bleedMm"] = BleedMm; o["bookBleed"] = BookBleed; o["cropMarks"] = CropMarks; o["bleedGuides"] = BleedGuides;
            o["cmyk"] = Cmyk; o["booklet"] = Booklet; o["cover"] = Cover; o["checkConsistency"] = CheckConsistency;
            return o;
        }

        public static ExportPreset FromJson(object node)
        {
            var o = Json.AsObject(node);
            if (o == null) return null;
            var preset = new ExportPreset();
            var id = Json.AsString(Json.Field(o, "id"));
            if (!string.IsNullOrEmpty(id)) preset.Id = id;
            var name = (Json.AsString(Json.Field(o, "name")) ?? "").Trim();
            if (name.Length > 0) preset.Name = name;
            preset.Note = Json.AsString(Json.Field(o, "note")) ?? "";
            preset.Format = ExportFormats.Normalize(Json.AsString(Json.Field(o, "format")));
            preset.Pages = ExportPages.Normalize(Json.AsString(Json.Field(o, "pages")));
            preset.TitlePage = Json.AsBool(Json.Field(o, "titlePage"), false);
            preset.ChapterHeadings = Json.AsBool(Json.Field(o, "chapterHeadings"), true);
            preset.NumberChapters = Json.AsBool(Json.Field(o, "numberChapters"), false);
            preset.PageBreakPerText = Json.AsBool(Json.Field(o, "pageBreakPerText"), true);
            preset.RectoStarts = Json.AsBool(Json.Field(o, "rectoStarts"), false);
            preset.HeadersFooters = Json.AsBool(Json.Field(o, "headersFooters"), false);
            preset.PlainMargins = Json.AsBool(Json.Field(o, "plainMargins"), false);
            preset.MaxFontPt = Math.Max(0, Math.Min(72, Json.AsDouble(Json.Field(o, "maxFontPt"), 0)));
            preset.LineSpacing = Math.Max(0, Math.Min(3, Json.AsDouble(Json.Field(o, "lineSpacing"), 0)));
            preset.Comments = Json.AsBool(Json.Field(o, "comments"), true);
            preset.Links = Json.AsBool(Json.Field(o, "links"), false);
            preset.Outline = Json.AsBool(Json.Field(o, "outline"), false);
            preset.BleedMm = Math.Max(0, Math.Min(20, Json.AsDouble(Json.Field(o, "bleedMm"), 0)));
            preset.BookBleed = Json.AsBool(Json.Field(o, "bookBleed"), false);
            preset.CropMarks = Json.AsBool(Json.Field(o, "cropMarks"), false);
            preset.BleedGuides = Json.AsBool(Json.Field(o, "bleedGuides"), false);
            preset.Cmyk = Json.AsBool(Json.Field(o, "cmyk"), false);
            preset.Booklet = Json.AsBool(Json.Field(o, "booklet"), false);
            preset.Cover = Json.AsBool(Json.Field(o, "cover"), false);
            preset.CheckConsistency = Json.AsBool(Json.Field(o, "checkConsistency"), false);
            preset.Category = ExportPresets.CategoryCustom;
            preset.BuiltIn = false;
            return preset;
        }
    }

    /// <summary>Les préréglages de la maison, rangés par catégorie.</summary>
    public static class ExportPresets
    {
        public const string CategoryTexts = "Adapté aux textes";
        public const string CategoryMixed = "Export de plusieurs types de documents";
        public const string CategoryBooks = "Livres";
        public const string CategorySheets = "Encyclopédie & Fiches";
        public const string CategoryCustom = "Mes préréglages";

        public const string WordA4 = "word-a4", WordLayout = "word-layout", PdfA4 = "pdf-a4", Epub = "epub", Bfp = "bfp",
            Bat = "bat", EpubPublish = "epub-publish", Manuscript = "manuscript", EncyclopediaPdf = "encyclopedia-pdf";

        /// <summary>L'ordre des catégories dans la liste.</summary>
        public static readonly string[] Categories = { CategoryTexts, CategoryMixed, CategoryBooks, CategorySheets, CategoryCustom };

        public static List<ExportPreset> BuiltIn()
        {
            var list = new List<ExportPreset>();
            list.Add(new ExportPreset
            {
                Id = WordA4, Name = "Document Word A4", Category = CategoryTexts, BuiltIn = true,
                Note = "Un fichier Word en A4, marges égales, à relire ou à envoyer. Les styles suivent ; la mise en page propre à chaque texte, non. Les plans et les cartes mentales n'y entrent pas.",
                Format = ExportFormats.Docx, Pages = ExportPages.A4
            });
            list.Add(new ExportPreset
            {
                Id = WordLayout, Name = "Document Word mis en forme", Category = CategoryTexts, BuiltIn = true,
                Note = "Conserve la mise en page des textes : chaque écrit garde son format de page, ses marges et ses colonnes (une section Word par mise en page). Les en-têtes et pieds des gabarits ne passent pas dans Word.",
                Format = ExportFormats.Docx, Pages = ExportPages.Source
            });
            list.Add(new ExportPreset
            {
                Id = PdfA4, Name = "PDF A4", Category = CategoryTexts, BuiltIn = true,
                Note = "Un PDF de lecture en A4, polices incorporées, folio centré, pagination continue. Pour l'écran ou l'imprimante de bureau — pas pour un imprimeur.",
                Format = ExportFormats.Pdf, Pages = ExportPages.A4, Outline = true
            });
            list.Add(new ExportPreset
            {
                Id = Epub, Name = "EPUB", Category = CategoryTexts, BuiltIn = true,
                Note = "Un EPUB 3 sans apprêt, pour lire sur liseuse : le texte recoule, les formats de page et les folios n'ont pas cours. Les fiches, plans et cartes mentales n'y entrent pas.",
                Format = ExportFormats.Epub
            });
            list.Add(new ExportPreset
            {
                Id = Bfp, Name = "BFP — Big Fat PDF", Category = CategoryMixed, BuiltIn = true,
                Note = "Un seul PDF pour tout : écrits, fiches, plans, cartes mentales. Chaque élément garde sa mise en forme et son format — les pages n'ont donc pas toutes la même taille. Une archive à feuilleter, pas un fichier d'imprimeur.",
                Format = ExportFormats.Pdf, Pages = ExportPages.Source, HeadersFooters = true, Links = true, Outline = true
            });
            list.Add(new ExportPreset
            {
                Id = Bat, Name = "BAT Imprimeur", Category = CategoryBooks, BuiltIn = true,
                Note = "L'ancien bouton « Publier » : le livre au format de son gabarit, chaque écrit en belle page, CMJN FOGRA39, traits de coupe et fond perdu du livre. Avertit si l'ampleur mêle plusieurs formats ou déborde du livre.",
                Format = ExportFormats.Pdf, Pages = ExportPages.Book, ChapterHeadings = false, RectoStarts = true, HeadersFooters = true,
                BookBleed = true, CropMarks = true, Cmyk = true, CheckConsistency = true
            });
            list.Add(new ExportPreset
            {
                Id = EpubPublish, Name = "EPUB Publiable", Category = CategoryBooks, BuiltIn = true,
                Note = "L'EPUB du livre prêt pour les librairies en ligne : couverture, titre, auteur·ice, éditeur, ISBN. Avertit s'il manque une information ou si l'ampleur déborde du livre.",
                Format = ExportFormats.Epub, Cover = true, CheckConsistency = true
            });
            list.Add(new ExportPreset
            {
                Id = Manuscript, Name = "PDF Manuscrit papier", Category = CategoryBooks, BuiltIn = true,
                Note = "Le manuscrit à imprimer pour pas cher et à lire sans effort : A4, marges égales sans miroir, interligne 1,25, aucune police au-dessus de 12 points, une seule pagination d'un bout à l'autre. Pour les maisons d'édition et les comités de lecture.",
                Format = ExportFormats.Pdf, Pages = ExportPages.A4, TitlePage = true, PlainMargins = true, MaxFontPt = 12, LineSpacing = 1.25
            });
            list.Add(new ExportPreset
            {
                Id = EncyclopediaPdf, Name = "PDF Encyclopédique", Category = CategorySheets, BuiltIn = true,
                Note = "Les fiches en un PDF qui se parcourt : les liens entre fiches, les relations et les signets sont cliquables. Pensé pour les fiches — les écrits y entrent aussi, leurs liens vers les fiches exportées restent vivants.",
                Format = ExportFormats.Pdf, Pages = ExportPages.A4, Links = true, Outline = true
            });
            return list;
        }

        public static ExportPreset Find(IEnumerable<ExportPreset> presets, string id)
        {
            if (presets != null && id != null)
                foreach (var preset in presets) if (preset.Id == id) return preset;
            return null;
        }

        /// <summary>Un nom libre parmi ceux-là : « Nom », « Nom (2) »…</summary>
        public static string FreeName(IEnumerable<ExportPreset> presets, string wanted)
        {
            wanted = (wanted ?? "").Trim();
            if (wanted.Length == 0) wanted = "Mon préréglage";
            var name = wanted;
            for (var n = 2; ; n++)
            {
                var taken = false;
                foreach (var preset in presets)
                    if (string.Equals(preset.Name, name, StringComparison.CurrentCultureIgnoreCase)) { taken = true; break; }
                if (!taken) return name;
                name = wanted + " (" + n + ")";
            }
        }
    }

    /// <summary>Les informations du livre portées par un export : pré-remplies
    /// depuis le livre de l'ampleur, toujours modifiables dans le gestionnaire.</summary>
    public class ExportMeta
    {
        public string Title = "", Subtitle = "", Author = "", Publisher = "", Collection = "", Isbn = "", Year = "", Language = "fr";
        public string CoverImageId; // l'image de couverture (magasin du projet), null = aucune

        public ExportMeta Clone() { return (ExportMeta)MemberwiseClone(); }

        /// <summary>La ligne d'édition de la page de titre : éditeur — collection — année — ISBN.</summary>
        public string Colophon()
        {
            var parts = new List<string>();
            foreach (var part in new[] { Publisher, Collection, Year, string.IsNullOrEmpty((Isbn ?? "").Trim()) ? "" : "ISBN " + Isbn.Trim() })
                if (!string.IsNullOrEmpty((part ?? "").Trim())) parts.Add(part.Trim());
            return string.Join(" — ", parts.ToArray());
        }

        /// <summary>Pré-remplies depuis le livre (onglet Édition) et les
        /// défauts de l'auteur ; sans livre, le titre donné et l'auteur.</summary>
        public static ExportMeta For(Project project, BinderItem book, string fallbackTitle)
        {
            var meta = new ExportMeta();
            var info = book != null && book.Kind == ItemKind.Book ? book.Book : null;
            meta.Title = book != null && book.Kind == ItemKind.Book ? BookInfo.TitleOf(book) : (fallbackTitle ?? "");
            if (info != null)
            {
                meta.Subtitle = info.Subtitle ?? "";
                meta.Publisher = info.Publisher ?? "";
                meta.Collection = info.Collection ?? "";
                meta.Isbn = info.Isbn ?? "";
                meta.Year = info.Year ?? "";
            }
            meta.Author = Defaults.Or(info != null ? info.AuthorOverride : "", Defaults.Or(project != null ? project.Author : "", Defaults.Author));
            if (info != null && meta.Publisher.Length == 0) meta.Publisher = Defaults.Publisher ?? "";
            if (info != null && meta.Collection.Length == 0) meta.Collection = Defaults.Collection ?? "";
            if (book != null && book.ImageId != null && project != null && project.FindImage(book.ImageId) != null) meta.CoverImageId = book.ImageId;
            return meta;
        }

        /// <summary>Reporte ces informations dans le livre ; vrai si quelque chose a changé.</summary>
        public bool ApplyTo(BinderItem book, Project project)
        {
            if (book == null || book.Kind != ItemKind.Book) return false;
            if (book.Book == null) book.Book = new BookInfo();
            var info = book.Book;
            var changed = false;
            var title = (Title ?? "").Trim();
            if (title != BookInfo.TitleOf(book)) { info.Title = title == (book.Title ?? "").Trim() ? "" : title; changed = true; }
            changed |= Set(ref info.Subtitle, Subtitle);
            changed |= Set(ref info.Publisher, Publisher);
            changed |= Set(ref info.Collection, Collection);
            changed |= Set(ref info.Isbn, Isbn);
            changed |= Set(ref info.Year, Year);
            var author = (Author ?? "").Trim();
            var inherited = Defaults.Or(project != null ? project.Author : "", Defaults.Author);
            var wanted = author == inherited ? "" : author;
            if ((info.AuthorOverride ?? "") != wanted && author != Defaults.Or(info.AuthorOverride, inherited)) { info.AuthorOverride = wanted; changed = true; }
            return changed;
        }

        private static bool Set(ref string field, string value)
        {
            value = (value ?? "").Trim();
            if ((field ?? "") == value) return false;
            field = value;
            return true;
        }
    }

    /// <summary>Une remarque du plan : ce qui sera écarté, ce qui cloche.</summary>
    public class ExportWarning
    {
        public const int Info = 0, Warn = 1;
        public int Level;
        public string Text = "";
        public string Detail = "";
    }

    /// <summary>LE PLAN D'UN EXPORT : l'ampleur (des identifiants d'éléments)
    /// confrontée au préréglage — ce qui part, dans l'ordre de la Pile, ce que
    /// le format écarte, et les remarques.</summary>
    public class ExportPlan
    {
        public readonly List<BinderItem> Texts = new List<BinderItem>();
        public readonly List<BinderItem> Sheets = new List<BinderItem>();
        public readonly List<BinderItem> Plans = new List<BinderItem>();
        public readonly List<BinderItem> MindMaps = new List<BinderItem>();
        public readonly List<BinderItem> Skipped = new List<BinderItem>();   // genre que le format ne porte pas
        public readonly List<BinderItem> Dynamic = new List<BinderItem>();   // pages dynamiques écartées (EPUB)
        public readonly List<ExportWarning> Warnings = new List<ExportWarning>();
        /// <summary>Le livre qui contient TOUS les écrits du plan, sinon null.</summary>
        public BinderItem Book;
        /// <summary>Les livres touchés par l'ampleur.</summary>
        public readonly List<BinderItem> Books = new List<BinderItem>();

        public int Count { get { return Texts.Count + Sheets.Count + Plans.Count + MindMaps.Count; } }
        public bool IsEmpty { get { return Count == 0; } }

        public int WarnCount
        {
            get { var n = 0; foreach (var w in Warnings) if (w.Level == ExportWarning.Warn) n++; return n; }
        }

        /// <summary>« 12 écrits, 3 fiches, 1 plan ».</summary>
        public string Summary()
        {
            var parts = new List<string>();
            Count1(parts, Texts.Count, "écrit", "écrits");
            Count1(parts, Sheets.Count, "fiche", "fiches");
            Count1(parts, Plans.Count, "plan", "plans");
            Count1(parts, MindMaps.Count, "carte mentale", "cartes mentales");
            return parts.Count == 0 ? "Rien à exporter" : string.Join(", ", parts.ToArray());
        }

        private static void Count1(List<string> parts, int count, string one, string many)
        {
            if (count > 0) parts.Add(count + " " + (count > 1 ? many : one));
        }

        /// <summary>Le nom de fichier proposé : le titre du livre, l'élément seul, sinon le projet.</summary>
        public string DefaultName(Project project, ExportMeta meta)
        {
            if (Count == 1)
            {
                if (Texts.Count == 1) return Texts[0].Title;
                if (Sheets.Count == 1) return Sheets[0].Title;
                if (Plans.Count == 1) return Plans[0].Title;
                return MindMaps[0].Title;
            }
            if (meta != null && !string.IsNullOrEmpty((meta.Title ?? "").Trim())) return meta.Title.Trim();
            if (Book != null) return BookInfo.TitleOf(Book);
            return project == null || string.IsNullOrEmpty(project.Name) ? "export" : project.Name;
        }

        /// <summary>La mise en page d'un écrit pour ce préréglage.</summary>
        public static PageSetup PageOf(BinderItem text, Project project, ExportPreset preset)
        {
            var pages = ExportPages.Normalize(preset.Pages);
            PageSetup setup;
            if (pages == ExportPages.A4) setup = ExportPages.A4Setup();
            else if (pages == ExportPages.Book) setup = BookInfo.CountingPageFor(text, project);
            else setup = text.Page ?? BookInfo.CountingPageFor(text, project);
            if (preset.PlainMargins) setup = ExportPages.Symmetric(setup);
            return setup;
        }

        /// <summary>Les éléments du projet dans l'ORDRE DE LECTURE de la Pile
        /// (en profondeur : un livre, sa première partie, ses écrits, la partie
        /// suivante…) — Project.AllItems les rend par niveaux.</summary>
        public static List<BinderItem> ReadingOrder(Project project)
        {
            var list = new List<BinderItem>();
            if (project != null) foreach (var root in project.Roots) Walk(root, list);
            return list;
        }

        private static void Walk(BinderItem item, List<BinderItem> list)
        {
            list.Add(item);
            foreach (var child in item.Children) Walk(child, list);
        }

        public static ExportPlan Build(Project project, IEnumerable<string> itemIds, ExportPreset preset, ExportMeta meta)
        {
            var plan = new ExportPlan();
            if (project == null || preset == null) return plan;
            var wanted = new HashSet<string>();
            if (itemIds != null) foreach (var id in itemIds) if (id != null) wanted.Add(id);
            var format = ExportFormats.Normalize(preset.Format);

            foreach (var item in ReadingOrder(project))
            {
                if (!wanted.Contains(item.Id)) continue;
                if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet && item.Kind != ItemKind.Plan && item.Kind != ItemKind.MindMap) continue;
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                if (!ExportFormats.Carries(format, item.Kind)) { plan.Skipped.Add(item); continue; }
                if (item.Kind == ItemKind.Text)
                {
                    if (format == ExportFormats.Epub && ExtraPages.IsDynamic(item)) { plan.Dynamic.Add(item); continue; }
                    plan.Texts.Add(item);
                }
                else if (item.Kind == ItemKind.Sheet) plan.Sheets.Add(item);
                else if (item.Kind == ItemKind.Plan) plan.Plans.Add(item);
                else plan.MindMaps.Add(item);
            }

            // Les livres de l'ampleur.
            var outside = 0;
            foreach (var text in plan.Texts)
            {
                var book = text.EnclosingBook();
                if (book == null) { outside++; continue; }
                if (!plan.Books.Contains(book)) plan.Books.Add(book);
            }
            if (plan.Books.Count == 1 && outside == 0) plan.Book = plan.Books[0];

            plan.Remark(project, preset, meta ?? new ExportMeta(), outside);
            return plan;
        }

        private void Add(int level, string text, string detail = "")
        {
            Warnings.Add(new ExportWarning { Level = level, Text = text, Detail = detail ?? "" });
        }

        private static string Names(IList<BinderItem> items, int max)
        {
            var names = new List<string>();
            for (var i = 0; i < items.Count && i < max; i++) names.Add("« " + items[i].Title + " »");
            var text = string.Join(", ", names.ToArray());
            return items.Count > max ? text + " et " + (items.Count - max) + " de plus" : text;
        }

        private void Remark(Project project, ExportPreset preset, ExportMeta meta, int outside)
        {
            var format = ExportFormats.Normalize(preset.Format);
            var pages = ExportPages.Normalize(preset.Pages);

            if (IsEmpty) Add(ExportWarning.Warn, Skipped.Count + Dynamic.Count > 0 ? "Rien de ce qui est coché n'entre dans ce format." : "Rien n'est coché dans l'ampleur.", "Onglet « Ampleur » : cochez ce qu'il faut exporter.");

            if (Skipped.Count > 0)
            {
                int sheets = 0, plans = 0, maps = 0;
                foreach (var item in Skipped)
                {
                    if (item.Kind == ItemKind.Sheet) sheets++;
                    else if (item.Kind == ItemKind.Plan) plans++;
                    else maps++;
                }
                var parts = new List<string>();
                Count1(parts, sheets, "fiche", "fiches");
                Count1(parts, plans, "plan", "plans");
                Count1(parts, maps, "carte mentale", "cartes mentales");
                Add(ExportWarning.Warn, "Écarté par le format " + ExportFormats.Short(format) + " : " + string.Join(", ", parts.ToArray()) + ".",
                    format == ExportFormats.Epub ? "Un EPUB ne porte que des écrits. Le « BFP — Big Fat PDF » prend tout." : "Les plans et les cartes mentales ne vivent que dans un PDF. Le « BFP — Big Fat PDF » prend tout.");
            }
            if (Dynamic.Count > 0)
                Add(ExportWarning.Info, "Pages dynamiques écartées : " + Names(Dynamic, 4) + ".", "Table des matières, index, notes de fin : leurs folios n'ont pas de sens dans un texte qui recoule.");

            // Les formats de page des écrits.
            if (ExportFormats.HasPages(format) && Texts.Count > 0)
            {
                if (pages == ExportPages.Book)
                {
                    if (Book == null)
                        Add(ExportWarning.Warn, Books.Count == 0 ? "Aucun livre dans l'ampleur : chaque écrit sera composé sur sa propre mise en page." : "L'ampleur déborde d'un seul livre" + (Books.Count > 1 ? " (" + Books.Count + " livres)" : "") + (outside > 0 ? " : " + outside + (outside > 1 ? " écrits hors livre" : " écrit hors livre") : "") + ".",
                            "Un BAT se fait livre par livre : le gabarit, les folios et les belles pages sont ceux d'un livre.");
                    var odd = new List<BinderItem>();
                    foreach (var text in Texts)
                    {
                        var book = text.EnclosingBook();
                        if (book == null || book.Book == null || text.Page == null) continue;
                        if (!text.Page.SameLayout(book.Book.Template)) odd.Add(text);
                    }
                    if (odd.Count > 0)
                        Add(ExportWarning.Warn, odd.Count + (odd.Count > 1 ? " écrits n'ont pas" : " écrit n'a pas") + " la mise en page du gabarit : " + Names(odd, 4) + ".",
                            "Ils seront composés au gabarit du livre — leur pagination peut différer de ce que montre l'éditeur.");
                    if (Book != null)
                    {
                        var all = new List<BinderItem>();
                        BookFlow.Texts(Book, all);
                        if (all.Count > Texts.Count)
                            Add(ExportWarning.Warn, "Le livre n'est pas entier : " + Texts.Count + " de ses " + all.Count + " écrits.", "Les folios et la table des matières supposent le livre complet.");
                    }
                }
                else
                {
                    var distinct = new List<PageSetup>();
                    var counts = new List<int>();
                    foreach (var text in Texts)
                    {
                        var own = text.Page ?? BookInfo.CountingPageFor(text, project);
                        var found = -1;
                        for (var i = 0; i < distinct.Count; i++) if (ExportPages.SameSize(distinct[i], own)) { found = i; break; }
                        if (found < 0) { distinct.Add(own); counts.Add(1); }
                        else counts[found]++;
                    }
                    if (pages == ExportPages.A4)
                    {
                        var reflowed = 0;
                        var a4 = ExportPages.A4Setup();
                        for (var i = 0; i < distinct.Count; i++) if (!ExportPages.SameSize(distinct[i], a4)) reflowed += counts[i];
                        if (reflowed > 0)
                            Add(ExportWarning.Info, reflowed + (reflowed > 1 ? " écrits ne sont pas en A4 : ils seront recoulés" : " écrit n'est pas en A4 : il sera recoulé") + " en A4.",
                                "Pour garder leur format : « Document Word mis en forme » ou « BFP — Big Fat PDF ».");
                    }
                    else if (distinct.Count > 1)
                    {
                        var labels = new List<string>();
                        for (var i = 0; i < distinct.Count; i++) labels.Add(counts[i] + " en " + ExportPages.Describe(distinct[i]));
                        Add(preset.CheckConsistency ? ExportWarning.Warn : ExportWarning.Info, "Plusieurs formats de page : " + string.Join(", ", labels.ToArray()) + ".",
                            format == ExportFormats.Pdf ? "Le PDF aura des pages de tailles différentes." : "Le document Word aura une section par format.");
                    }
                }
            }

            if (preset.Booklet && format == ExportFormats.Pdf && (Sheets.Count + Plans.Count + MindMaps.Count > 0 || preset.Links || pages == ExportPages.Source))
                Add(ExportWarning.Info, "L'imposition en cahier ne vaut que pour un texte d'un seul format, sans liens : elle sera ignorée si l'export en mêle plusieurs.");

            if (preset.MaxFontPt > 0 && Texts.Count > 0)
            {
                var limit = preset.MaxFontPt * 4.0 / 3.0 + 0.01;
                var big = 0;
                foreach (var style in project.Styles.Styles) if (!style.IsSeparator && style.FontSize > limit) big++;
                if (big > 0)
                    Add(ExportWarning.Info, big + (big > 1 ? " styles dépassent " : " style dépasse ") + preset.MaxFontPt.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " points : ramené" + (big > 1 ? "s" : "") + " à cette taille dans l'export.");
            }

            // Les informations du livre.
            if (format == ExportFormats.Epub)
            {
                if (preset.CheckConsistency)
                {
                    if (Books.Count > 1 || (Books.Count == 1 && outside > 0))
                        Add(ExportWarning.Warn, "L'ampleur déborde d'un seul livre.", "Un EPUB publiable porte un livre, ses métadonnées et sa couverture.");
                    if (Book != null)
                    {
                        var plan = Epub.Plan(project, Book);
                        if (plan.Chapters.Count > Texts.Count)
                            Add(ExportWarning.Warn, "Le livre n'est pas entier : " + Texts.Count + " de ses " + plan.Chapters.Count + " écrits.");
                    }
                    var missing = new List<string>();
                    if (string.IsNullOrEmpty((meta.Title ?? "").Trim())) missing.Add("le titre");
                    if (string.IsNullOrEmpty((meta.Author ?? "").Trim())) missing.Add("l'auteur·ice");
                    if (missing.Count > 0) Add(ExportWarning.Warn, "Il manque " + string.Join(" et ", missing.ToArray()) + ".", "Onglet « Livre ».");
                    if (preset.Cover && (meta.CoverImageId == null || project.FindImage(meta.CoverImageId) == null))
                        Add(ExportWarning.Warn, "Pas de couverture.", "Posez l'image du livre dans son onglet Édition, puis choisissez-la dans l'onglet « Livre ».");
                    if (string.IsNullOrEmpty((meta.Isbn ?? "").Trim()))
                        Add(ExportWarning.Info, "Pas d'ISBN : un identifiant unique sera généré.", "Les librairies en ligne en demandent souvent un.");
                }
            }
            else if (preset.TitlePage && string.IsNullOrEmpty((meta.Title ?? "").Trim()))
                Add(ExportWarning.Info, "La page de titre n'a pas de titre : le nom du projet y figurera.", "Onglet « Livre ».");
        }
    }

    /// <summary>La mise en forme imposée par un préréglage (le manuscrit
    /// papier) : polices plafonnées, interligne unifié.</summary>
    public static class ExportTypography
    {
        public static bool Applies(ExportPreset preset) { return preset != null && (preset.MaxFontPt > 0 || preset.LineSpacing > 0); }

        /// <summary>La feuille de styles de l'export : une copie plafonnée et
        /// ré-interlignée, ou la feuille telle quelle.</summary>
        public static StyleSheet Styles(StyleSheet styles, ExportPreset preset)
        {
            if (styles == null || !Applies(preset)) return styles;
            var copy = styles.Clone();
            var limit = preset.MaxFontPt > 0 ? preset.MaxFontPt * 4.0 / 3.0 : 0;
            foreach (var style in copy.Styles)
            {
                if (limit > 0 && style.FontSize > limit)
                {
                    // Les espaces autour suivent la réduction, sans dépasser deux lignes.
                    var ratio = limit / style.FontSize;
                    style.FontSize = limit;
                    style.SpaceBefore = Math.Min(style.SpaceBefore * ratio, limit * 2);
                    style.SpaceAfter = Math.Min(style.SpaceAfter * ratio, limit * 2);
                    if (preset.LineSpacing <= 0 && style.LineHeight > 1) style.LineHeight = Math.Round(style.LineHeight * ratio * 100) / 100;
                }
                if (preset.LineSpacing > 0) style.LineHeight = Math.Round(style.FontSize * preset.LineSpacing * 100) / 100;
            }
            return copy;
        }

        /// <summary>Le document de l'export : les tailles posées à la main
        /// au-dessus du plafond y reviennent, l'interligne propre du document
        /// cède à celui du préréglage. Rend le document lui-même s'il n'y a rien à faire.</summary>
        public static TextDocument Document(TextDocument document, ExportPreset preset)
        {
            if (document == null || !Applies(preset)) return document;
            var limit = preset.MaxFontPt > 0 ? preset.MaxFontPt * 4.0 / 3.0 : 0;
            var copy = new TextDocument { Annotations = document.Annotations, LineSpacing = preset.LineSpacing > 0 ? 1 : document.LineSpacing };
            foreach (var note in document.Footnotes)
            {
                var kept = note;
                if (limit > 0 && HasBig(note.Runs, limit)) { kept = note.Clone(); kept.Id = note.Id; kept.SetRuns(Cap(note.Runs, limit)); }
                copy.Footnotes.Add(kept);
            }
            foreach (var paragraph in document.Paragraphs)
            {
                if (limit <= 0 || !HasBig(paragraph.Runs, limit)) { copy.Paragraphs.Add(paragraph); continue; }
                var shell = PivotEdit.CloneParagraphShell(paragraph);
                shell.Runs.AddRange(Cap(paragraph.Runs, limit));
                copy.Paragraphs.Add(shell);
            }
            return copy;
        }

        private static bool HasBig(IEnumerable<TextRun> runs, double limit)
        {
            foreach (var run in runs) if (run.FontSize.HasValue && run.FontSize.Value > limit && run.ImageId == null) return true;
            return false;
        }

        private static List<TextRun> Cap(IEnumerable<TextRun> runs, double limit)
        {
            var list = new List<TextRun>();
            foreach (var run in runs)
            {
                if (!run.FontSize.HasValue || run.FontSize.Value <= limit || run.ImageId != null || PivotEdit.IsElement(run)) { list.Add(run); continue; }
                var clone = PivotEdit.CloneFormat(run);
                clone.Text = run.Text;
                clone.FontSize = limit;
                list.Add(clone);
            }
            return list;
        }
    }
}
