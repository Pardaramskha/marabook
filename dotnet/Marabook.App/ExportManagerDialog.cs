using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Ce que le gestionnaire d'export rend : le préréglage tel que
    /// réglé, l'ampleur (les éléments cochés), les informations du livre.</summary>
    public class ExportRequest
    {
        public ExportPreset Preset;
        public List<string> ItemIds = new List<string>();
        public ExportMeta Meta = new ExportMeta();
        /// <summary>Le filtre de l'ampleur (statut, tag) ; null = aucun.</summary>
        public ExportFilter Filter;
        /// <summary>Reporter les informations dans ce livre (null = non).</summary>
        public BinderItem WriteBackBook;
        public bool OpenAfter = true;
        /// <summary>Les pages du livre qui précèdent (export rapide d'un écrit : ses folios restent ceux du livre).</summary>
        public int FolioOffset;
    }

    /// <summary>LE GESTIONNAIRE D'EXPORT (2.0.0, 18/11/2026, Rémi) : une seule
    /// fenêtre pour tout ce qui sort de Marabook en texte. Les onglets tiennent
    /// en colonne à gauche : le PRÉRÉGLAGE (ceux de la maison, rangés par
    /// catégorie avec leur ligne d'explication, et ceux de l'utilisateur),
    /// l'AMPLEUR (ce qu'on exporte — jamais gardée dans un préréglage), les
    /// OPTIONS, l'IMPRESSION (PDF), le LIVRE (titre, auteur·ice, couverture,
    /// pré-remplis depuis le livre de l'ampleur) et les VÉRIFICATIONS.</summary>
    public class ExportManagerDialog : Window
    {
        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private readonly Project _project;
        private readonly Func<ExportRequest, bool> _preview;
        private readonly Func<ExportRequest, List<Print.PdfPart>> _compose;
        private readonly bool _mindMaps;
        /// <summary>L'infobulle de chaque réglage quand il est modifiable ; grisé, il dit pourquoi à la place (20/11).</summary>
        private readonly Dictionary<Control, string> _tips = new Dictionary<Control, string>();
        private bool _accepted, _building;

        // ---- préréglage
        private List<ExportPreset> _presets;
        private ExportPreset _base;      // le préréglage choisi
        private ExportPreset _preset;    // sa copie de travail, telle que réglée
        private readonly StackPanel _presetList;
        private readonly Dictionary<string, Border> _presetCards = new Dictionary<string, Border>();

        // ---- ampleur
        private sealed class ScopeNode
        {
            public BinderItem Item;          // null = un regroupement (catégorie de fiches, section)
            public string Label;
            public bool Exportable;
            public bool OutOfBook;
            public CheckBox Box;
            public Control Children;
            public Button Caret;
            public ScopeNode Parent;
            public readonly List<ScopeNode> Nodes = new List<ScopeNode>();
        }
        private readonly HashSet<string> _checked = new HashSet<string>();
        private readonly List<ScopeNode> _roots = new List<ScopeNode>();
        private readonly StackPanel _scopePanel;
        private readonly TextBlock _scopeCount;
        private readonly ComboBox _filterStatus, _filterOnlyTag, _filterExceptTag;
        private readonly List<string> _tags = new List<string>();

        // ---- options
        private readonly ComboBox _format, _pages;
        private readonly List<string> _formatIds = new List<string>();
        private readonly CheckBox _titlePage, _headings, _numbers, _pageBreak;
        // ---- structure
        private readonly RadioButton _seqDefault, _seqRecto, _seqNoBlank;
        private readonly CheckBox _splitTypes, _splitBooks, _splitFolders, _splitSubfolders, _splitOwnPage;
        private readonly CheckBox _oneFile;
        private readonly TextBox _filePattern;
        private readonly TextBlock _fileSample;
        // ---- livre numérique
        private readonly CheckBox _dropCaps, _smallCaps, _kindle;
        // ---- aperçu
        private readonly ExportPagePreview _pageView;
        private readonly TextBlock _pageLabel, _previewNote;
        private readonly Button _pagePrevious, _pageNext;
        private readonly TabItem _previewTab;
        private readonly List<KeyValuePair<Print.PdfPart, int>> _previewPages = new List<KeyValuePair<Print.PdfPart, int>>();
        private int _previewIndex, _previewTicket;
        private bool _previewStale = true;
        private int _pageCount = -1; // le nombre de pages du dernier aperçu, tant qu'il vaut encore
        // ---- caractères absents des polices : le relevé de chaque écrit, fait une fois
        private AvaloniaFontEngine _fonts;
        private readonly Dictionary<string, List<MissingGlyph>> _glyphs = new Dictionary<string, List<MissingGlyph>>();
        private readonly CheckBox _headersFooters, _plainMargins, _capFont, _spacing, _comments, _links, _outline, _check;
        private readonly TextBox _maxFont, _lineSpacing;
        // ---- impression
        private readonly CheckBox _bookBleed, _marks, _guides, _pdfx;
        private readonly TextBox _bleed;
        private readonly ComboBox _profile, _imposition;
        private readonly TextBlock _printNote;
        private readonly Panel _printBody;
        // ---- livre
        private readonly TextBox _title, _subtitle, _author, _publisher, _collection, _isbn, _year, _language;
        private readonly ComboBox _cover;
        private readonly List<string> _coverIds = new List<string>();
        private readonly CheckBox _coverInclude, _writeBack, _openAfter;
        private readonly TextBlock _bookNote;
        private BinderItem _metaBook;
        private ExportMeta _metaFilled; // ce que la fenêtre a posé elle-même : tant que les champs le valent, on n'y a pas écrit
        // ---- vérifications et pied
        private readonly StackPanel _warnings;
        private readonly TabItem _warningsTab;
        private readonly TextBlock _footerPreset, _footerPlan;
        private readonly Button _previewButton, _ok;
        private readonly TabControl _tabs;
        private ExportPlan _plan = new ExportPlan();

        private ExportManagerDialog(Window owner, Project project, IEnumerable<string> initialIds, string presetId, Func<ExportRequest, bool> preview, Func<ExportRequest, List<Print.PdfPart>> compose)
        {
            _project = project;
            _preview = preview;
            _compose = compose;
            _mindMaps = MindMapModules.Provider != null;
            Title = "Gestionnaire d'export";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 980;
            Height = 700;
            MinWidth = 820;
            MinHeight = 520;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            Tag = "export-manager";
            _building = true;

            if (initialIds != null) foreach (var id in initialIds) if (id != null) _checked.Add(id);

            var root = new DockPanel { Margin = new Thickness(16, 6, 16, 14) };

            // ============================================================ le pied
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(buttons, Dock.Right);
            _previewButton = new Button { Content = "Aperçu…", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Tag = "export-preview" };
            ToolTip.SetTip(_previewButton, "Le PDF exact, réglages compris, ouvert dans votre lecteur — rien n'est encore enregistré.");
            _previewButton.Click += delegate
            {
                if (_preview == null) return;
                if (!_preview(BuildRequest()))
                    MessageDialog.Show(this, "Aperçu impossible.", "Gestionnaire d'export", MessageButtons.OK, MessageIcon.Warning);
            };
            buttons.Children.Add(_previewButton);
            _ok = new Button { Content = "Exporter…", IsDefault = true, MinWidth = 110, Tag = "export-ok" };
            _ok.Click += delegate { Accept(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, _ok);
            footer.Children.Add(buttons);
            var recap = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _footerPreset = new TextBlock { Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "export-footer-preset" };
            _footerPlan = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), Tag = "export-footer-plan" };
            recap.Children.Add(_footerPreset);
            recap.Children.Add(_footerPlan);
            footer.Children.Add(recap);
            root.Children.Add(footer);

            // ============================================================ les onglets, en colonne
            _tabs = AstrolabeUi.ColumnTabs(double.NaN);
            _tabs.Tag = "export-tabs";

            // ------------------------------------------------------------ préréglage
            _presetList = new StackPanel { Tag = "export-presets" };
            var presetPage = Page();
            presetPage.Children.Add(Hint("Un préréglage décide du format et de toutes ses options. L'ampleur — ce que vous exportez — se choisit à part, et n'en fait jamais partie."));
            presetPage.Children.Add(_presetList);
            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var save = Buttons.IconText("plus-bold", "Enregistrer la configuration en préréglage…", null, Buttons.Bar, Buttons.Look.Outline);
            save.Tag = "export-save-preset";
            save.Click += delegate { SavePreset(); };
            saveRow.Children.Add(save);
            presetPage.Children.Add(saveRow);
            _tabs.Items.Add(Tab("Préréglage", presetPage, "export-tab-preset"));

            // ------------------------------------------------------------ ampleur
            var scopePage = Page();
            scopePage.Children.Add(Hint("Cochez ce qui part dans l'export. Les éléments sortent dans l'ordre de la Pile : les écrits, puis les fiches, les plans, les cartes mentales et les cartes."));
            var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            quick.Children.Add(Quick("Tous les écrits", "export-scope-all-texts", delegate { CheckKind(ItemKind.Text, true); }));
            quick.Children.Add(Quick("Toutes les fiches", "export-scope-all-sheets", delegate { CheckKind(ItemKind.Sheet, true); }));
            quick.Children.Add(Quick("Tout", "export-scope-all", delegate { CheckAll(true); }));
            quick.Children.Add(Quick("Rien", "export-scope-none", delegate { CheckAll(false); }));
            _scopeCount = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Tag = "export-scope-count" };
            quick.Children.Add(_scopeCount);
            scopePage.Children.Add(quick);
            // Le filtre (20/11) : parmi ce qui est coché, un statut, un tag, ou tout sauf un tag.
            var filterGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto") };
            _filterStatus = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-filter-status" };
            _filterStatus.Items.Add("Tous les statuts");
            foreach (var key in TextStatus.Keys) _filterStatus.Items.Add("Seulement « " + TextStatus.Label(key) + " »");
            _filterOnlyTag = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-filter-only-tag" };
            _filterExceptTag = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-filter-except-tag" };
            _tags.AddRange(ExportFilter.TagsOf(project));
            _filterOnlyTag.Items.Add("Tous les tags");
            _filterExceptTag.Items.Add("Aucun tag écarté");
            foreach (var tag in _tags) { _filterOnlyTag.Items.Add("Seulement « " + tag + " »"); _filterExceptTag.Items.Add("Sauf « " + tag + " »"); }
            _filterStatus.SelectedIndex = 0;
            _filterOnlyTag.SelectedIndex = 0;
            _filterExceptTag.SelectedIndex = 0;
            GridRow(filterGrid, 0, "Statut des écrits", _filterStatus);
            GridRow(filterGrid, 2, "Garder un tag", _filterOnlyTag);
            GridRow(filterGrid, 4, "Écarter un tag", _filterExceptTag);
            Tip(_filterStatus, "Ne laisse partir que les écrits de ce statut. Les fiches, plans et cartes mentales n'ont pas de statut : ils passent.");
            Tip(_filterOnlyTag, "Ne laisse partir que ce qui porte ce tag — écrits, fiches, plans.");
            Tip(_filterExceptTag, "Laisse tout partir, sauf ce qui porte ce tag.");
            if (_tags.Count == 0)
            {
                Avail(_filterOnlyTag, false, "Aucun tag dans ce projet : posez-en sur un écrit ou une fiche pour filtrer par tag.");
                Avail(_filterExceptTag, false, "Aucun tag dans ce projet : posez-en sur un écrit ou une fiche pour filtrer par tag.");
            }
            scopePage.Children.Add(AstrolabeUi.Section("Filtrer", "Parmi ce qui est coché. Comme l'ampleur, le filtre ne fait partie d'aucun préréglage.", filterGrid, "export-section-filter"));
            _scopePanel = new StackPanel { Tag = "export-scope" };
            scopePage.Children.Add(_scopePanel);
            _tabs.Items.Add(Tab("Ampleur", scopePage, "export-tab-scope"));

            // ------------------------------------------------------------ options
            var optionsPage = Page();
            var formatGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto") };
            _format = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-format" };
            // Le RTF n'est proposé que si un convertisseur est branché (aucun dans le portage Avalonia).
            foreach (var format in ExportFormats.All)
            {
                if (format == ExportFormats.Rtf && Rtf.Exporter == null) continue;
                _formatIds.Add(format);
                _format.Items.Add(ExportFormats.Label(format));
            }
            _pages = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-pages" };
            _pages.Items.Add("A4, marges égales");
            _pages.Items.Add("La mise en page de chaque élément");
            _pages.Items.Add("Le gabarit du livre");
            GridRow(formatGrid, 0, "Format du fichier", _format);
            GridRow(formatGrid, 2, "Format des pages", _pages);
            optionsPage.Children.Add(AstrolabeUi.Section("Format", "Ce que le fichier sera, et sur quelles pages le texte se pose.", formatGrid, "export-section-format"));

            var look = new StackPanel();
            _headersFooters = Check(look, "En-têtes et pieds de page des gabarits", "export-decor", "Sinon, un simple folio centré en pied.");
            _plainMargins = Check(look, "Marges de côté égales, sans miroir", "export-plain-margins", "Le petit fond et le grand fond prennent leur moyenne : à relier soi-même, ou à agrafer.");
            _capFont = Check(look, "Aucune police au-dessus de", "export-cap-font", null);
            _maxFont = Number("12", "export-max-font");
            look.Children.Remove(_capFont);
            look.Children.Add(Inline(_capFont, _maxFont, "points"));
            _spacing = Check(look, "Interligne unifié à", "export-spacing", null);
            _lineSpacing = Number("1,25", "export-line-spacing");
            look.Children.Remove(_spacing);
            look.Children.Add(Inline(_spacing, _lineSpacing, "fois le corps"));
            _comments = Check(look, "Les annotations partent en commentaires Word", "export-comments", null);
            optionsPage.Children.Add(AstrolabeUi.Section("Mise en forme", null, look, "export-section-look"));

            var navigation = new StackPanel();
            _links = Check(navigation, "Liens cliquables entre les éléments exportés", "export-links", "Un [[lien]] ou une relation vers une fiche ou un écrit de l'export y mène d'un clic.");
            _outline = Check(navigation, "Signets (le volet de navigation du lecteur PDF)", "export-outline", null);
            optionsPage.Children.Add(AstrolabeUi.Section("Navigation dans le PDF", null, navigation, "export-section-navigation"));

            var ebook = new StackPanel();
            _dropCaps = Check(ebook, "Lettrine en ouverture de chapitre", "export-drop-caps", "La première lettre de chaque chapitre du récit, sur trois lignes. Un chapitre qui s'ouvre sur un tiret de dialogue n'en prend pas.");
            _smallCaps = Check(ebook, "Premiers mots du chapitre en petites capitales", "export-small-caps", "Les quatre premiers mots — moins si une ponctuation forte les arrête.");
            _kindle = Check(ebook, "Profil Kindle", "export-kindle", "Ce qu'Amazon demande à un EPUB : la couverture déclarée sans page de couverture en double, et un corps de texte qui laisse au lecteur sa police, sa taille et son interligne.");
            optionsPage.Children.Add(AstrolabeUi.Section("Livre numérique", "La couverture se choisit dans l'onglet « Livre ».", ebook, "export-section-ebook"));

            var control = new StackPanel();
            _check = Check(control, "Demander confirmation s'il reste des avertissements", "export-check", "Formats de page mêlés, livre incomplet, information manquante : la liste est dans « Vérifications ».");
            _openAfter = Check(control, "Montrer le fichier une fois exporté", "export-open-after", null);
            _openAfter.IsChecked = true;
            optionsPage.Children.Add(AstrolabeUi.Section("Avant et après", null, control, "export-section-control"));
            _tabs.Items.Add(Tab("Options", optionsPage, "export-tab-options"));

            // ------------------------------------------------------------ structure (20/11)
            var structurePage = Page();
            structurePage.Children.Add(Hint("Ce qui ouvre l'export, ce qui coiffe chaque écrit, comment les pages s'enchaînent, et les titres posés à chaque changement de niveau."));
            var structure = new StackPanel();
            _titlePage = Check(structure, "Page de titre", "export-title-page", "Titre, sous-titre, auteur·ice et ligne d'édition, tirés de l'onglet « Livre ».");
            _headings = Check(structure, "Titre de chaque écrit en tête de chapitre", "export-headings", "Dès que l'export compte deux écrits ; un écrit seul sort tel quel. Dans un EPUB, toujours.");
            _numbers = Check(structure, "Numéroter les chapitres", "export-numbers", "« 1. », « 2. »… devant le titre de chaque écrit.");
            _numbers.Margin = new Thickness(22, 2, 0, 2);
            _pageBreak = Check(structure, "Chaque écrit commence sur une nouvelle page", "export-page-break", "Sinon, le séparateur de scène les sépare.");
            structurePage.Children.Add(AstrolabeUi.Section("Écrits", null, structure, "export-section-structure"));

            var sequence = new StackPanel();
            _seqDefault = Radio(sequence, "Conserver l'enchaînement par défaut", "export-seq-default", "Rien n'est ajouté ni retiré : les pages se suivent comme les écrits les donnent.");
            _seqRecto = Radio(sequence, "Démarrer chaque écrit sur une belle page (recto)", "export-recto", "Une page blanche s'intercale quand il le faut, comme dans un livre.");
            _seqNoBlank = Radio(sequence, "Retirer les pleines pages blanches", "export-no-blank", "Les pages qui sortiraient toutes blanches tombent. Celles qui ne porteraient qu'un titre de partie et un folio rejoignent la page suivante : le titre reste, la page s'en va.");
            structurePage.Children.Add(AstrolabeUi.Section("Enchaînement des pages", "L'un exclut l'autre : une belle page pose des pages blanches.", sequence, "export-section-sequence"));

            var levels = new StackPanel();
            _splitTypes = Check(levels, "Séparer par genre", "export-split-types", "« Écrits », « Fiches », « Plans », « Cartes mentales » : un titre devant chaque genre, dès que l'export en mêle plusieurs.");
            _splitBooks = Check(levels, "Séparer par livre", "export-split-books", "Le titre de chaque livre devant ses écrits.");
            _splitFolders = Check(levels, "Séparer par dossier", "export-split-folders", "Le titre de chaque dossier de premier rang — une partie — devant ses écrits.");
            _splitSubfolders = Check(levels, "Séparer par sous-dossier", "export-split-subfolders", "Le titre de chaque dossier rangé dans un autre devant ses écrits.");
            _splitOwnPage = Check(levels, "Ces titres prennent une page à eux", "export-split-own-page", "Sinon, ils coiffent le premier écrit qu'ils annoncent. Avec « Retirer les pleines pages blanches », ils le coiffent toujours.");
            _splitOwnPage.Margin = new Thickness(0, 8, 0, 2);
            structurePage.Children.Add(AstrolabeUi.Section("Rendu par niveau", "Un titre à chaque changement, chacun dans son style : « Export — titre de genre », « de livre », « de partie », « de sous-partie », que Word garde sous ces noms. Les fiches gardent leurs catégories.", levels, "export-section-levels"));

            // Un fichier par écrit (20/11) : un dossier de fichiers nommés d'après un modèle.
            var files = new StackPanel();
            _oneFile = Check(files, "Un fichier par écrit", "export-one-file", "Au lieu d'un seul fichier, l'export en écrit un par élément coché — écrit, fiche ou plan — dans le dossier que vous choisirez.");
            _filePattern = new TextBox { Tag = "export-file-pattern", Width = 260, VerticalAlignment = VerticalAlignment.Center };
            Tip(_filePattern, "Le modèle du nom de chaque fichier. Ce qu'il sait remplacer :\n" + ExportFiles.Legend());
            files.Children.Add(Inline(new TextBlock { Text = "Nom des fichiers", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center }, _filePattern, ""));
            _fileSample = new TextBlock { Tag = "export-file-sample", Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            files.Children.Add(_fileSample);
            structurePage.Children.Add(AstrolabeUi.Section("Fichiers", "Un seul fichier, ou un par élément : un chapitre par fichier pour un·e bêta-lecteur·ice, une fiche par fichier pour un wiki.", files, "export-section-files"));
            _tabs.Items.Add(Tab("Structure", structurePage, "export-tab-structure"));

            // ------------------------------------------------------------ impression
            var printPage = Page();
            _printNote = Hint("");
            printPage.Children.Add(_printNote);
            var print = new StackPanel();
            _bookBleed = Check(print, "Fond perdu du livre", "export-book-bleed", "Celui réglé dans l'onglet Gabarits & Format du livre.");
            _bleed = Number("0", "export-bleed");
            print.Children.Add(Inline(new TextBlock { Text = "Fond perdu", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Width = 150 }, _bleed, "mm"));
            _marks = Check(print, "Traits de coupe", "export-marks", null);
            _guides = Check(print, "Repères de fond perdu (cadre cyan)", "export-guides", null);
            var printGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            _profile = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-profile" };
            _profile.Items.Add("RVB — lecture à l'écran");
            _profile.Items.Add("CMJN — Coated FOGRA39 (imprimerie)");
            _imposition = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-imposition" };
            _imposition.Items.Add("Pages — une page par feuille");
            _imposition.Items.Add("Cahier — livret à cheval (2 pages par face)");
            Tip(_imposition, "Le cahier s'imprime recto-verso puis se plie en deux. Fond perdu et traits de coupe n'y ont pas cours ; il ne vaut que pour un texte d'un seul format.");
            GridRow(printGrid, 0, "Profil couleur", _profile);
            GridRow(printGrid, 2, "Imposition", _imposition);
            print.Children.Add(printGrid);
            _pdfx = Check(print, "Conforme PDF/X-1a:2003", "export-pdfx", "Le fichier que les imprimeurs demandent : il se déclare PDF/X, porte son identifiant, ses métadonnées et sa condition d'impression (le profil ICC « ISO Coated v2 », livré avec Marabook, y est incorporé), et renonce aux liens cliquables. Après l'export, Marabook relit le fichier et le contrôle règle par règle ; ce contrôle ne remplace pas celui de l'imprimeur.");
            _pdfx.Margin = new Thickness(0, 8, 0, 2);
            _printBody = print;
            printPage.Children.Add(AstrolabeUi.Section("Le fichier pour l'imprimeur", "Polices incorporées, texte composé ligne à ligne. Laissez 0 sans coche pour un PDF de lecture.", print, "export-section-print"));
            _tabs.Items.Add(Tab("Impression", printPage, "export-tab-print"));

            // ------------------------------------------------------------ livre
            var bookPage = Page();
            _bookNote = Hint("");
            bookPage.Children.Add(_bookNote);
            var metaGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,8,*"), RowDefinitions = new RowDefinitions("Auto,6,Auto,6,Auto,6,Auto,6,Auto,6,Auto,6,Auto,6,Auto") };
            _title = Field(metaGrid, 0, "Titre", "export-meta-title");
            _subtitle = Field(metaGrid, 2, "Sous-titre", "export-meta-subtitle");
            _author = Field(metaGrid, 4, "Auteur·ice", "export-meta-author");
            _publisher = Field(metaGrid, 6, "Éditeur", "export-meta-publisher");
            _collection = Field(metaGrid, 8, "Collection", "export-meta-collection");
            _isbn = Field(metaGrid, 10, "ISBN", "export-meta-isbn");
            _year = Field(metaGrid, 12, "Année", "export-meta-year");
            _language = Field(metaGrid, 14, "Langue", "export-meta-language");
            _language.Watermark = "fr, en, es…";
            bookPage.Children.Add(AstrolabeUi.Section("Informations", "Elles nourrissent la page de titre, les métadonnées de l'EPUB et celles du fichier.", metaGrid, "export-section-meta"));
            var coverBody = new StackPanel();
            _cover = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "export-cover" };
            coverBody.Children.Add(_cover);
            _coverInclude = Check(coverBody, "Mettre la couverture dans l'EPUB", "export-cover-include", null);
            _coverInclude.Margin = new Thickness(0, 8, 0, 0);
            bookPage.Children.Add(AstrolabeUi.Section("Couverture", "L'image posée dans l'onglet Édition d'un livre.", coverBody, "export-section-cover"));
            _writeBack = new CheckBox { Content = "", Margin = new Thickness(2, 0, 0, 0), Tag = "export-write-back" };
            bookPage.Children.Add(_writeBack);
            _tabs.Items.Add(Tab("Livre", bookPage, "export-tab-book"));

            // ------------------------------------------------------------ aperçu (20/11)
            var previewPage = new DockPanel { Margin = new Thickness(4, 10, 8, 4) };
            var previewBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(previewBar, Dock.Top);
            _pagePrevious = Buttons.Text("◀", null, Buttons.Compact, Buttons.Look.Outline);
            _pagePrevious.Tag = "export-page-previous";
            _pagePrevious.Margin = new Thickness(0, 0, 6, 0);
            _pagePrevious.Click += delegate { ShowPreviewPage(_previewIndex - 1); };
            _pageNext = Buttons.Text("▶", null, Buttons.Compact, Buttons.Look.Outline);
            _pageNext.Tag = "export-page-next";
            _pageNext.Click += delegate { ShowPreviewPage(_previewIndex + 1); };
            _pageLabel = new TextBlock { Foreground = Chrome.Ink, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Tag = "export-page-label" };
            previewBar.Children.Add(_pagePrevious);
            previewBar.Children.Add(_pageNext);
            previewBar.Children.Add(_pageLabel);
            _previewNote = Hint("");
            _previewNote.Tag = "export-preview-note";
            DockPanel.SetDock(_previewNote, Dock.Top);
            _pageView = new ExportPagePreview { Tag = "export-page-view", MinHeight = 240 };
            previewPage.Children.Add(previewBar);
            previewPage.Children.Add(_previewNote);
            previewPage.Children.Add(_pageView);
            _previewTab = new TabItem { Header = "Aperçu", Tag = "export-tab-preview", Content = previewPage };
            _tabs.Items.Add(_previewTab);
            _tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (ReferenceEquals(e.Source, _tabs) && ReferenceEquals(_tabs.SelectedItem, _previewTab)) QueuePreview();
            };
            _pageView.PointerWheelChanged += delegate(object sender, PointerWheelEventArgs e)
            {
                ShowPreviewPage(_previewIndex + (e.Delta.Y < 0 ? 1 : -1));
                e.Handled = true;
            };

            // ------------------------------------------------------------ vérifications
            var warningsPage = Page();
            _warnings = new StackPanel { Tag = "export-warnings" };
            warningsPage.Children.Add(_warnings);
            _warningsTab = Tab("Vérifications", warningsPage, "export-tab-warnings");
            _tabs.Items.Add(_warningsTab);

            root.Children.Add(_tabs);
            Content = root;

            // ---- le câblage : tout changement relit les réglages et refait le plan
            EventHandler<SelectionChangedEventArgs> selection = delegate { OptionsChanged(); };
            _format.SelectionChanged += selection;
            _pages.SelectionChanged += selection;
            _profile.SelectionChanged += selection;
            _imposition.SelectionChanged += selection;
            foreach (var box in new[] { _titlePage, _headings, _numbers, _pageBreak, _headersFooters, _plainMargins, _capFont, _spacing, _comments, _links, _outline, _check, _bookBleed, _marks, _guides, _coverInclude,
                _splitTypes, _splitBooks, _splitFolders, _splitSubfolders, _splitOwnPage, _dropCaps, _smallCaps, _kindle, _pdfx, _oneFile })
                box.IsCheckedChanged += delegate { OptionsChanged(); };
            // L'exclusion des trois choix est tenue ici : le groupe d'Avalonia ne joue que pour des boutons à l'écran.
            var sequence3 = new[] { _seqDefault, _seqRecto, _seqNoBlank };
            foreach (var radio in sequence3)
                radio.IsCheckedChanged += delegate(object sender, Avalonia.Interactivity.RoutedEventArgs e)
                {
                    if (((RadioButton)sender).IsChecked != true) return;
                    foreach (var other in sequence3) if (!ReferenceEquals(other, sender) && other.IsChecked == true) other.IsChecked = false;
                    OptionsChanged();
                };
            foreach (var combo in new[] { _filterStatus, _filterOnlyTag, _filterExceptTag })
                combo.SelectionChanged += delegate { if (_building) return; Refresh(); };
            foreach (var box in new[] { _maxFont, _lineSpacing, _bleed, _filePattern })
                box.TextChanged += delegate { OptionsChanged(); };
            foreach (var box in new[] { _title, _subtitle, _author, _publisher, _collection, _isbn, _year, _language })
                box.TextChanged += delegate { if (_building) return; Refresh(); };
            _cover.SelectionChanged += delegate { if (_building) return; Refresh(); };

            _presets = ExportPresetStore.All();
            var chosen = ExportPresets.Find(_presets, presetId) ?? ExportPresets.Find(_presets, ExportPresetStore.LastId) ?? _presets[0];
            BuildScope();
            BuildPresetList();
            _building = false;
            Choose(chosen);
        }

        // ================================================================ briques

        private static StackPanel Page()
        {
            return new StackPanel { Margin = new Thickness(4, 10, 8, 4) };
        }

        private static TabItem Tab(string header, StackPanel page, string tag)
        {
            return new TabItem { Header = header, Tag = tag, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page } };
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        }

        /// <summary>Une case, large comme son libellé seulement : son infobulle
        /// ne paraît que sur elle, pas sur le vide à sa droite (20/11).</summary>
        private CheckBox Check(Panel panel, string label, string tag, string tip)
        {
            var box = new CheckBox { Content = label, Margin = new Thickness(0, 2, 0, 2), Tag = tag, HorizontalAlignment = HorizontalAlignment.Left };
            Tip(box, tip);
            panel.Children.Add(box);
            return box;
        }

        private RadioButton Radio(Panel panel, string label, string tag, string tip)
        {
            var radio = new RadioButton { Content = label, Margin = new Thickness(0, 2, 0, 2), Tag = tag, GroupName = "export-sequence", HorizontalAlignment = HorizontalAlignment.Left };
            Tip(radio, tip);
            panel.Children.Add(radio);
            return radio;
        }

        /// <summary>L'infobulle d'un réglage quand il est modifiable.</summary>
        private void Tip(Control control, string tip)
        {
            _tips[control] = tip;
            ToolTip.SetTip(control, tip);
            ToolTip.SetShowOnDisabled(control, true);
        }

        /// <summary>Active ou grise un réglage. Grisé, son infobulle dit
        /// POURQUOI (20/11, Rémi) ; actif, elle redit à quoi il sert.</summary>
        private void Avail(Control control, bool enabled, string reason)
        {
            control.IsEnabled = enabled;
            string tip;
            _tips.TryGetValue(control, out tip);
            ToolTip.SetTip(control, enabled || string.IsNullOrEmpty(reason) ? tip : reason);
            ToolTip.SetShowOnDisabled(control, true);
        }

        internal string TipForProbe(string tag)
        {
            var control = ControlForProbe<Control>(tag);
            return control == null ? null : ToolTip.GetTip(control) as string;
        }

        private static TextBox Number(string text, string tag)
        {
            return new TextBox { Text = text, Width = 64, Tag = tag, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right };
        }

        private static Control Inline(Control lead, Control field, string unit)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            lead.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(lead);
            row.Children.Add(field);
            row.Children.Add(new TextBlock { Text = unit, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return row;
        }

        private static void GridRow(Grid grid, int row, string label, Control field)
        {
            var caption = new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(caption, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 2);
            grid.Children.Add(caption);
            grid.Children.Add(field);
        }

        private static TextBox Field(Grid grid, int row, string label, string tag)
        {
            var box = new TextBox { Tag = tag };
            GridRow(grid, row, label, box);
            return box;
        }

        private static Button Quick(string label, string tag, Action action)
        {
            var button = Buttons.Text(label, null, Buttons.Compact, Buttons.Look.Outline);
            button.Tag = tag;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Click += delegate { action(); };
            return button;
        }

        // ================================================================ préréglages

        private void BuildPresetList()
        {
            _presetList.Children.Clear();
            _presetCards.Clear();
            foreach (var category in ExportPresets.Categories)
            {
                var first = true;
                foreach (var preset in _presets)
                {
                    if (preset.Category != category) continue;
                    if (first)
                    {
                        _presetList.Children.Add(new TextBlock
                        {
                            Text = category.ToUpper(French), Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold,
                            Margin = new Thickness(2, _presetList.Children.Count == 0 ? 0 : 10, 0, 5)
                        });
                        first = false;
                    }
                    _presetList.Children.Add(PresetCard(preset));
                }
            }
        }

        private Border PresetCard(ExportPreset preset)
        {
            var name = new TextBlock { Text = preset.Name, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                Child = new TextBlock { Text = ExportFormats.Short(preset.Format), Foreground = Chrome.AccentStrong, FontSize = 10.5, FontWeight = FontWeight.SemiBold },
                Background = Chrome.AccentTint, BorderBrush = Chrome.AccentSoft, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var head = new DockPanel();
            if (!preset.BuiltIn)
            {
                var remove = Buttons.Icon("x-bold", "Supprimer ce préréglage", Buttons.Compact, Buttons.Look.Calm);
                remove.Tag = "export-preset-remove";
                DockPanel.SetDock(remove, Dock.Right);
                remove.Click += delegate(object sender, Avalonia.Interactivity.RoutedEventArgs e) { e.Handled = true; RemovePreset(preset); };
                head.Children.Add(remove);
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(name);
            title.Children.Add(chip);
            head.Children.Add(title);
            var stack = new StackPanel();
            stack.Children.Add(head);
            var note = string.IsNullOrEmpty(preset.Note) ? Describe(preset) : preset.Note;
            stack.Children.Add(new TextBlock { Text = note, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            var card = new Border
            {
                Child = stack, Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7), BorderBrush = Chrome.Border, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = "export-preset:" + preset.Id
            };
            card.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                Choose(preset);
            };
            _presetCards[preset.Id] = card;
            return card;
        }

        /// <summary>Un préréglage personnalisé sans note : ses options en une ligne.</summary>
        private static string Describe(ExportPreset preset)
        {
            var parts = new List<string> { ExportFormats.Short(preset.Format) };
            if (ExportFormats.HasPages(preset.Format))
                parts.Add(preset.Pages == ExportPages.Source ? "mise en page de chaque élément" : preset.Pages == ExportPages.Book ? "gabarit du livre" : "A4");
            if (preset.TitlePage) parts.Add("page de titre");
            if (preset.Format == ExportFormats.Pdf && preset.Cmyk) parts.Add("CMJN");
            if (preset.Format == ExportFormats.Pdf && preset.CropMarks) parts.Add("traits de coupe");
            if (preset.MaxFontPt > 0) parts.Add("polices plafonnées");
            if (preset.Format == ExportFormats.Pdf && preset.Links) parts.Add("liens cliquables");
            return string.Join(" · ", parts.ToArray()) + ".";
        }

        private void PaintCards()
        {
            foreach (var pair in _presetCards)
            {
                var selected = _base != null && pair.Key == _base.Id;
                pair.Value.BorderBrush = selected ? Chrome.Accent : Chrome.Border;
                pair.Value.BorderThickness = new Thickness(selected ? 2 : 1);
                pair.Value.Padding = selected ? new Thickness(11, 8, 11, 9) : new Thickness(12, 9, 12, 10);
                pair.Value.Background = selected ? (IBrush)Chrome.AccentTint : new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7);
            }
        }

        /// <summary>Choisit un préréglage : ses options remplissent la fenêtre.</summary>
        private void Choose(ExportPreset preset)
        {
            if (preset == null) return;
            _base = preset;
            _preset = preset.Clone();
            PaintCards();
            _building = true;
            _format.SelectedIndex = Math.Max(0, _formatIds.IndexOf(ExportFormats.Normalize(_preset.Format)));
            _pages.SelectedIndex = _preset.Pages == ExportPages.Source ? 1 : _preset.Pages == ExportPages.Book ? 2 : 0;
            _titlePage.IsChecked = _preset.TitlePage;
            _headings.IsChecked = _preset.ChapterHeadings;
            _numbers.IsChecked = _preset.NumberChapters;
            _pageBreak.IsChecked = _preset.PageBreakPerText;
            _seqRecto.IsChecked = _preset.RectoStarts;
            _seqNoBlank.IsChecked = !_preset.RectoStarts && _preset.DropBlankPages;
            _seqDefault.IsChecked = !_preset.RectoStarts && !_preset.DropBlankPages;
            _splitTypes.IsChecked = _preset.SplitTypes;
            _splitBooks.IsChecked = _preset.SplitBooks;
            _splitFolders.IsChecked = _preset.SplitFolders;
            _splitSubfolders.IsChecked = _preset.SplitSubfolders;
            _splitOwnPage.IsChecked = _preset.SplitOwnPage;
            _dropCaps.IsChecked = _preset.DropCaps;
            _smallCaps.IsChecked = _preset.OpeningSmallCaps;
            _kindle.IsChecked = _preset.Kindle;
            _pdfx.IsChecked = _preset.PdfX;
            _oneFile.IsChecked = _preset.OneFilePerText;
            _filePattern.Text = ExportFiles.Normalize(_preset.FileNamePattern);
            _headersFooters.IsChecked = _preset.HeadersFooters;
            _plainMargins.IsChecked = _preset.PlainMargins;
            _capFont.IsChecked = _preset.MaxFontPt > 0;
            _maxFont.Text = (_preset.MaxFontPt > 0 ? _preset.MaxFontPt : 12).ToString("0.#", French);
            _spacing.IsChecked = _preset.LineSpacing > 0;
            _lineSpacing.Text = (_preset.LineSpacing > 0 ? _preset.LineSpacing : 1.25).ToString("0.##", French);
            _comments.IsChecked = _preset.Comments;
            _links.IsChecked = _preset.Links;
            _outline.IsChecked = _preset.Outline;
            _check.IsChecked = _preset.CheckConsistency;
            _bookBleed.IsChecked = _preset.BookBleed;
            _bleed.Text = _preset.BleedMm.ToString("0.#", French);
            _marks.IsChecked = _preset.CropMarks;
            _guides.IsChecked = _preset.BleedGuides;
            _profile.SelectedIndex = _preset.Cmyk ? 1 : 0;
            _imposition.SelectedIndex = _preset.Booklet ? 1 : 0;
            _coverInclude.IsChecked = _preset.Cover;
            _building = false;
            Refresh();
        }

        private static double Parse(string text, double fallback)
        {
            double value;
            return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0 ? value : fallback;
        }

        /// <summary>Relit les contrôles dans la copie de travail.</summary>
        private void ReadOptions()
        {
            var p = _preset;
            p.Format = _formatIds[Math.Max(0, Math.Min(_formatIds.Count - 1, _format.SelectedIndex))];
            p.Pages = _pages.SelectedIndex == 1 ? ExportPages.Source : _pages.SelectedIndex == 2 ? ExportPages.Book : ExportPages.A4;
            p.TitlePage = _titlePage.IsChecked == true;
            p.ChapterHeadings = _headings.IsChecked == true;
            p.NumberChapters = _numbers.IsChecked == true;
            p.PageBreakPerText = _pageBreak.IsChecked == true;
            p.RectoStarts = _seqRecto.IsChecked == true;
            p.DropBlankPages = _seqNoBlank.IsChecked == true && !p.RectoStarts;
            p.SplitTypes = _splitTypes.IsChecked == true;
            p.SplitBooks = _splitBooks.IsChecked == true;
            p.SplitFolders = _splitFolders.IsChecked == true;
            p.SplitSubfolders = _splitSubfolders.IsChecked == true;
            p.SplitOwnPage = _splitOwnPage.IsChecked == true;
            p.DropCaps = _dropCaps.IsChecked == true;
            p.OpeningSmallCaps = _smallCaps.IsChecked == true;
            p.Kindle = _kindle.IsChecked == true;
            p.PdfX = _pdfx.IsChecked == true;
            p.OneFilePerText = _oneFile.IsChecked == true;
            p.FileNamePattern = ExportFiles.Normalize(_filePattern.Text);
            p.HeadersFooters = _headersFooters.IsChecked == true;
            p.PlainMargins = _plainMargins.IsChecked == true;
            p.MaxFontPt = _capFont.IsChecked == true ? Math.Max(6, Math.Min(72, Parse(_maxFont.Text, 12))) : 0;
            p.LineSpacing = _spacing.IsChecked == true ? Math.Max(0.8, Math.Min(3, Parse(_lineSpacing.Text, 1.25))) : 0;
            p.Comments = _comments.IsChecked == true;
            p.Links = _links.IsChecked == true;
            p.Outline = _outline.IsChecked == true;
            p.CheckConsistency = _check.IsChecked == true;
            p.BookBleed = _bookBleed.IsChecked == true;
            p.BleedMm = Math.Min(20, Parse(_bleed.Text, 0));
            p.CropMarks = _marks.IsChecked == true;
            p.BleedGuides = _guides.IsChecked == true;
            p.Cmyk = _profile.SelectedIndex == 1;
            p.Booklet = _imposition.SelectedIndex == 1;
            p.Cover = _coverInclude.IsChecked == true;
        }

        private void OptionsChanged()
        {
            if (_building || _preset == null) return;
            ReadOptions();
            Refresh();
        }

        /// <summary>Ce qui n'a pas cours dans le format choisi se grise.</summary>
        private void SyncAvailability()
        {
            var format = _preset.Format;
            var pdf = format == ExportFormats.Pdf;
            var epub = format == ExportFormats.Epub;
            var paged = ExportFormats.HasPages(format);
            var styled = format == ExportFormats.Docx || pdf || format == ExportFormats.Odt || format == ExportFormats.Rtf;
            // Chaque réglage grisé dit pourquoi dans son infobulle (20/11) : seule une incompatibilité
            // de format grise — ou un réglage dont dépend celui-ci, qu'il suffit de cocher.
            var name = ExportFormats.Short(format);
            var pdfOnly = "Ne vaut que pour un PDF : le format choisi (" + name + ") ne le porte pas.";
            var epubOnly = "Ne vaut que pour un EPUB : le format choisi (" + name + ") ne le porte pas.";
            var noPages = epub ? "Un EPUB n'a pas de pages : le texte recoule sur l'écran du lecteur." : "Le format " + name + " n'a pas de pages.";
            Avail(_pages, paged, "Le format des pages ne vaut que pour Word et PDF : " + (epub ? "un EPUB recoule sur l'écran du lecteur." : "le format " + name + " n'a pas de pages."));
            Avail(_titlePage, true, null);
            Avail(_numbers, _preset.ChapterHeadings, "Cochez d'abord « Titre de chaque écrit en tête de chapitre » : c'est ce titre qui porte le numéro.");
            Avail(_pageBreak, !epub, "Dans un EPUB, chaque écrit est un chapitre : il ouvre toujours un nouvel écran.");
            var breakable = pdf || format == ExportFormats.Docx || format == ExportFormats.Odt || format == ExportFormats.Rtf;
            Avail(_seqRecto, pdf && _preset.PageBreakPerText, !pdf ? "Marabook ne pose les belles pages que dans un PDF : le format choisi (" + name + ") n'a pas de recto ni de verso." : "Cochez d'abord « Chaque écrit commence sur une nouvelle page ».");
            Avail(_seqNoBlank, breakable, noPages);
            Avail(_seqDefault, true, null);
            Avail(_splitTypes, !epub, "Un EPUB ne porte que des écrits : il n'y a qu'un genre, rien à séparer.");
            Avail(_splitOwnPage, _preset.SplitsLevels && (breakable || epub), !_preset.SplitsLevels ? "Cochez d'abord une séparation ci-dessus." : noPages);
            Avail(_filePattern, _preset.OneFilePerText, "Cochez d'abord « Un fichier par écrit ».");
            _fileSample.Text = !_preset.OneFilePerText ? "" : _plan.Count < 2 ? "Un seul élément est coché : l'export n'écrira qu'un fichier, au nom que vous lui donnerez." : "→ " + ExportFiles.Sample(_project, _plan, _preset, ReadMeta(), DateTime.Today, 3);
            _fileSample.IsVisible = _fileSample.Text.Length > 0;
            Avail(_headersFooters, pdf, "Les en-têtes et pieds des gabarits ne se dessinent que dans un PDF. " + (format == ExportFormats.Docx ? "Dans Word, un folio centré les remplace." : "Le format " + name + " n'en a pas."));
            Avail(_plainMargins, paged, "Les marges ne valent que pour Word et PDF : " + noPages.Substring(0, 1).ToLower(French) + noPages.Substring(1));
            var noStyle = epub ? "Dans un EPUB, c'est le lecteur qui règle la taille et l'interligne sur sa liseuse." : "Le format " + name + " ne porte ni taille de police ni interligne.";
            Avail(_capFont, styled, noStyle);
            Avail(_maxFont, styled && _preset.MaxFontPt > 0, styled ? "Cochez d'abord « Aucune police au-dessus de »." : noStyle);
            Avail(_spacing, styled, noStyle);
            Avail(_lineSpacing, styled && _preset.LineSpacing > 0, styled ? "Cochez d'abord « Interligne unifié à »." : noStyle);
            Avail(_comments, format == ExportFormats.Docx, "Les commentaires sont propres à Word : le format choisi (" + name + ") n'en a pas. Les annotations restent dans Marabook.");
            Avail(_links, pdf, pdfOnly);
            Avail(_outline, pdf, pdfOnly);
            Avail(_dropCaps, epub, epubOnly);
            Avail(_smallCaps, epub, epubOnly);
            Avail(_kindle, epub, epubOnly);
            var booklet = _preset.Booklet;
            var noBooklet = "L'imposition en cahier n'a ni fond perdu ni traits de coupe : revenez à « Pages » pour les régler.";
            Avail(_bookBleed, pdf && !booklet, pdf ? noBooklet : pdfOnly);
            Avail(_bleed, pdf && !booklet && !(_preset.BookBleed && _plan.Book != null), !pdf ? pdfOnly : booklet ? noBooklet : "« Fond perdu du livre » est coché : c'est celui du livre qui vaut. Décochez-le pour en saisir un autre.");
            Avail(_marks, pdf && !booklet, pdf ? noBooklet : pdfOnly);
            Avail(_guides, pdf && !booklet, pdf ? noBooklet : pdfOnly);
            Avail(_profile, pdf, pdfOnly);
            Avail(_imposition, pdf, pdfOnly);
            Avail(_pdfx, pdf && _preset.Cmyk, pdf ? "PDF/X-1a ne vaut qu'en CMJN : choisissez d'abord le profil « CMJN — Coated FOGRA39 » ci-dessus." : pdfOnly);
            _printNote.Text = pdf ? "Ces réglages ne valent que pour un PDF remis à un imprimeur. Pour lire à l'écran, laissez-les tels quels." : "Ces réglages ne valent que pour un PDF : le format choisi (" + name + ") n'en tient pas compte.";
            Avail(_coverInclude, epub, "Seul un EPUB embarque une couverture : le format choisi (" + name + ") n'en porte pas.");
            _previewButton.IsVisible = pdf && _preview != null;
        }

        private async void SavePreset()
        {
            ReadOptions();
            var suggestion = _base != null && !_base.BuiltIn ? _base.Name : ExportPresets.FreeName(_presets, "Mon " + (_base == null ? "préréglage" : _base.Name));
            var name = await InputDialog.Ask(this, "Enregistrer en préréglage", "Nom du préréglage (l'ampleur n'y est pas gardée) :", suggestion);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            foreach (var preset in _presets)
                if (preset.BuiltIn && string.Equals(preset.Name, name, StringComparison.CurrentCultureIgnoreCase))
                {
                    await MessageDialog.Show(this, "« " + preset.Name + " » est un préréglage de Marabook : choisissez un autre nom.", "Gestionnaire d'export", MessageButtons.OK, MessageIcon.Information);
                    return;
                }
            SavePresetAs(name);
        }

        private void SavePresetAs(string name)
        {
            var saved = ExportPresetStore.SaveAs(_preset, name, "");
            _presets = ExportPresetStore.All();
            BuildPresetList();
            Choose(ExportPresets.Find(_presets, saved.Id));
        }

        private void RemovePreset(ExportPreset preset)
        {
            if (preset == null || preset.BuiltIn) return;
            ExportPresetStore.Remove(preset.Id);
            _presets = ExportPresetStore.All();
            BuildPresetList();
            if (_base != null && _base.Id == preset.Id) Choose(_presets[0]);
            else PaintCards();
        }

        // ================================================================ ampleur

        private static bool IsExportable(BinderItem item)
        {
            return item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet || item.Kind == ItemKind.Plan || item.Kind == ItemKind.MindMap || (item.Kind == ItemKind.Map && item.Map != null && item.Map.HasImage);
        }

        private void BuildScope()
        {
            _roots.Clear();
            _scopePanel.Children.Clear();

            var writings = _project.Category(Project.KeyWritings);
            if (writings != null)
            {
                var root = new ScopeNode { Label = "Écrits" };
                AddTree(root, writings, false);
                if (root.Nodes.Count > 0) _roots.Add(root);
            }

            // Les fiches, par catégorie.
            var sheets = new ScopeNode { Label = "Fiches" };
            var byCategory = new Dictionary<string, ScopeNode>();
            ScopeNode loose = null;
            foreach (var item in ExportPlan.ReadingOrder(_project))
            {
                if (item.Kind != ItemKind.Sheet) continue;
                var home = item.RootCategory();
                if (home != null && home.CategoryKey == Project.KeyTrash) continue;
                var category = _project.TopSheetCategoryOf(item);
                ScopeNode group;
                if (category == null)
                {
                    if (loose == null) loose = new ScopeNode { Label = Encyclopedia.UncategorizedTitle, Parent = sheets };
                    group = loose;
                }
                else if (!byCategory.TryGetValue(category.Id, out group))
                {
                    group = new ScopeNode { Label = string.IsNullOrEmpty(category.Name) ? "Catégorie" : category.Name, Parent = sheets };
                    byCategory[category.Id] = group;
                    sheets.Nodes.Add(group);
                }
                group.Nodes.Add(new ScopeNode { Item = item, Label = item.Title, Exportable = true, Parent = group });
            }
            if (loose != null) sheets.Nodes.Add(loose);
            if (sheets.Nodes.Count > 0) _roots.Add(sheets);

            AddFlat("Plans", Project.KeyPlans, ItemKind.Plan);
            if (_mindMaps) AddFlat("Cartes mentales", Project.KeyMindMaps, ItemKind.MindMap);
            AddFlat("Cartes", Project.KeyMaps, ItemKind.Map); // 20/11 : chaque carte en image, sur une page à sa taille

            foreach (var root in _roots) _scopePanel.Children.Add(Render(root, 0));
            SyncScope();
        }

        private void AddFlat(string label, string key, ItemKind kind)
        {
            var root = new ScopeNode { Label = label };
            foreach (var item in ExportPlan.ReadingOrder(_project))
            {
                if (item.Kind != kind) continue;
                var home = item.RootCategory();
                if (home == null || home.CategoryKey != key) continue;
                root.Nodes.Add(new ScopeNode { Item = item, Label = item.Title, Exportable = true, Parent = root });
            }
            if (root.Nodes.Count > 0) _roots.Add(root);
        }

        private static void AddTree(ScopeNode parent, BinderItem container, bool outOfBook)
        {
            foreach (var child in container.Children)
            {
                if (child.Kind != ItemKind.Text && child.Kind != ItemKind.Folder && child.Kind != ItemKind.Book) continue;
                var node = new ScopeNode { Item = child, Label = child.Title, Exportable = child.Kind == ItemKind.Text, OutOfBook = outOfBook || child.IsOutOfBook, Parent = parent };
                AddTree(node, child, node.OutOfBook);
                if (node.Exportable || node.Nodes.Count > 0) parent.Nodes.Add(node);
            }
        }

        private Control Render(ScopeNode node, int depth)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(depth * 18, 0, 0, 0) };
            if (node.Nodes.Count > 0)
            {
                node.Caret = new Button { Width = 20, Height = 20, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
                node.Caret.Click += delegate { if (node.Children != null) { node.Children.IsVisible = !node.Children.IsVisible; PaintCaret(node); } };
                row.Children.Add(node.Caret);
            }
            else row.Children.Add(new Border { Width = 20 });
            var label = new TextBlock { Text = node.Label, Foreground = Chrome.Ink, FontSize = depth == 0 ? 13.5 : 12.5, FontWeight = depth == 0 ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 };
            Control content = label;
            if (node.Item != null && node.Item.Kind == ItemKind.Book) content = WithNote(label, "livre");
            else if (node.OutOfBook && node.Item != null && node.Item.IsOutOfBook) content = WithNote(label, "jamais publié avec le livre");
            node.Box = new CheckBox { Content = content, IsThreeState = false, Margin = new Thickness(2, 1, 0, 1), Tag = node.Item != null ? "export-scope-item:" + node.Item.Id : "export-scope-group:" + node.Label };
            node.Box.Click += delegate { Toggle(node); };
            row.Children.Add(node.Box);
            if (node.Nodes.Count == 0) return row;

            var children = new StackPanel();
            foreach (var child in node.Nodes) children.Children.Add(Render(child, depth + 1));
            node.Children = children;
            // Ouvert : les sections, et ce qui contient du coché ; le reste se déplie à la demande.
            children.IsVisible = depth == 0 || HasChecked(node);
            PaintCaret(node);
            var stack = new StackPanel { Margin = new Thickness(0, depth == 0 ? 8 : 0, 0, 0) };
            stack.Children.Add(row);
            stack.Children.Add(children);
            return stack;
        }

        private static Control WithNote(TextBlock label, string note)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(label);
            row.Children.Add(new TextBlock { Text = note, Foreground = Chrome.FaintText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return row;
        }

        private static void PaintCaret(ScopeNode node)
        {
            if (node.Caret == null || node.Children == null) return;
            node.Caret.Content = new TextBlock { Text = node.Children.IsVisible ? "▾" : "▸", Foreground = Chrome.SoftText, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        private bool HasChecked(ScopeNode node)
        {
            if (node.Exportable && node.Item != null && _checked.Contains(node.Item.Id)) return true;
            foreach (var child in node.Nodes) if (HasChecked(child)) return true;
            return false;
        }

        /// <summary>Compte les éléments exportables d'un nœud : total et cochés.
        /// skipOutOfBook : le Hors-livre ne suit pas la coche d'un livre.</summary>
        private void Count(ScopeNode node, ref int total, ref int on, bool skipOutOfBook)
        {
            if (skipOutOfBook && node.OutOfBook) return;
            if (node.Exportable && node.Item != null)
            {
                total++;
                if (_checked.Contains(node.Item.Id)) on++;
            }
            foreach (var child in node.Nodes) Count(child, ref total, ref on, skipOutOfBook);
        }

        private void Set(ScopeNode node, bool value, bool skipOutOfBook)
        {
            if (skipOutOfBook && node.OutOfBook) return;
            if (node.Exportable && node.Item != null)
            {
                if (value) _checked.Add(node.Item.Id);
                else _checked.Remove(node.Item.Id);
            }
            foreach (var child in node.Nodes) Set(child, value, skipOutOfBook);
        }

        /// <summary>Un clic sur une case : tout ce qu'elle couvre suit — sauf
        /// le Hors-livre, qui ne part que si on le coche lui-même.</summary>
        private void Toggle(ScopeNode node)
        {
            if (_building) return;
            var skip = !node.OutOfBook;
            int total = 0, on = 0;
            Count(node, ref total, ref on, skip);
            if (total == 0) { skip = false; Count(node, ref total, ref on, false); }
            Set(node, on < total, skip);
            SyncScope();
            Refresh();
        }

        private void SyncScope()
        {
            var was = _building;
            _building = true;
            foreach (var root in _roots) SyncNode(root);
            _building = was;
        }

        private void SyncNode(ScopeNode node)
        {
            int total = 0, on = 0;
            Count(node, ref total, ref on, !node.OutOfBook);
            if (total == 0) Count(node, ref total, ref on, false);
            if (node.Box != null)
            {
                node.Box.IsChecked = total > 0 && on == total ? true : on == 0 ? false : (bool?)null;
                Avail(node.Box, total > 0, "Rien ici ne s'exporte : ce regroupement ne contient ni écrit, ni fiche, ni plan.");
            }
            foreach (var child in node.Nodes) SyncNode(child);
        }

        private void CheckAll(bool value)
        {
            foreach (var root in _roots) Set(root, value, true);
            if (!value) _checked.Clear();
            SyncScope();
            Refresh();
        }

        private void CheckKind(ItemKind kind, bool value)
        {
            foreach (var root in _roots) SetKind(root, kind, value);
            SyncScope();
            Refresh();
        }

        private void SetKind(ScopeNode node, ItemKind kind, bool value)
        {
            if (node.OutOfBook) return;
            if (node.Exportable && node.Item != null && node.Item.Kind == kind)
            {
                if (value) _checked.Add(node.Item.Id);
                else _checked.Remove(node.Item.Id);
            }
            foreach (var child in node.Nodes) SetKind(child, kind, value);
        }

        // ================================================================ livre

        private ExportMeta ReadMeta()
        {
            var meta = new ExportMeta
            {
                Title = (_title.Text ?? "").Trim(), Subtitle = (_subtitle.Text ?? "").Trim(), Author = (_author.Text ?? "").Trim(),
                Publisher = (_publisher.Text ?? "").Trim(), Collection = (_collection.Text ?? "").Trim(), Isbn = (_isbn.Text ?? "").Trim(),
                Year = (_year.Text ?? "").Trim(), Language = (_language.Text ?? "").Trim()
            };
            if (meta.Language.Length == 0) meta.Language = "fr";
            var index = _cover.SelectedIndex;
            meta.CoverImageId = index > 0 && index < _coverIds.Count ? _coverIds[index] : null;
            return meta;
        }

        /// <summary>Le livre de l'ampleur a changé : ses informations
        /// remplissent les champs — tant que l'on n'y a pas écrit soi-même.</summary>
        private void SyncMeta()
        {
            var book = _plan.Book;
            var first = _cover.Items.Count == 0;
            if (!first && (ReferenceEquals(book, _metaBook) || MetaTouched())) { PaintBookNote(); return; }
            _metaBook = book;
            var single = _plan.Texts.Count == 1 ? _plan.Texts[0] : null;
            var meta = ExportMeta.For(_project, book, single != null ? single.Title : (_project.Name ?? ""));
            var was = _building;
            _building = true;
            _title.Text = meta.Title;
            _subtitle.Text = meta.Subtitle;
            _author.Text = meta.Author;
            _publisher.Text = meta.Publisher;
            _collection.Text = meta.Collection;
            _isbn.Text = meta.Isbn;
            _year.Text = meta.Year;
            _language.Text = meta.Language;
            // Les couvertures : celles des livres du projet, et l'image de l'écrit seul.
            _cover.Items.Clear();
            _coverIds.Clear();
            _cover.Items.Add("Aucune couverture");
            _coverIds.Add(null);
            foreach (var item in ExportPlan.ReadingOrder(_project))
            {
                if (item.Kind != ItemKind.Book || item.ImageId == null || _project.FindImage(item.ImageId) == null) continue;
                var home = item.RootCategory();
                if (home != null && home.CategoryKey == Project.KeyTrash) continue;
                _cover.Items.Add("La couverture de « " + BookInfo.TitleOf(item) + " »");
                _coverIds.Add(item.ImageId);
            }
            var cover = meta.CoverImageId;
            if (cover == null && single != null && single.ImageId != null && _project.FindImage(single.ImageId) != null)
            {
                cover = single.ImageId;
                if (!_coverIds.Contains(cover)) { _cover.Items.Add("L'image de « " + single.Title + " »"); _coverIds.Add(cover); }
            }
            _cover.SelectedIndex = Math.Max(0, _coverIds.IndexOf(cover));
            _building = was;
            _metaFilled = ReadMeta();
            PaintBookNote();
        }

        /// <summary>Vrai si l'on a écrit dans les champs depuis leur dernier
        /// remplissage. (TextChanged arrive APRÈS coup sur Avalonia : un
        /// drapeau levé dans l'événement se lèverait aussi pour nos propres écritures.)</summary>
        private bool MetaTouched()
        {
            if (_metaFilled == null) return false;
            var now = ReadMeta();
            return now.Title != _metaFilled.Title || now.Subtitle != _metaFilled.Subtitle || now.Author != _metaFilled.Author || now.Publisher != _metaFilled.Publisher
                || now.Collection != _metaFilled.Collection || now.Isbn != _metaFilled.Isbn || now.Year != _metaFilled.Year || now.Language != _metaFilled.Language
                || now.CoverImageId != _metaFilled.CoverImageId;
        }

        private void PaintBookNote()
        {
            var book = _plan.Book;
            _bookNote.Text = book != null
                ? "Pré-rempli depuis le livre « " + BookInfo.TitleOf(book) + " » (son onglet Édition). Ce que vous changez ici ne vaut que pour cet export — sauf si vous cochez la case du bas."
                : "L'ampleur ne tient pas dans un seul livre : seuls un titre et l'auteur·ice par défaut sont proposés — tout se règle ici, pour cet export.";
            _writeBack.IsVisible = book != null;
            if (book != null) _writeBack.Content = "Reporter ces informations dans le livre « " + BookInfo.TitleOf(book) + " »";
            else _writeBack.IsChecked = false;
        }

        // ================================================================ plan, vérifications, pied

        private void Refresh()
        {
            if (_building || _preset == null) return;
            var filter = ReadFilter();
            _plan = ExportPlan.Build(_project, _checked, _preset, null, filter);
            SyncMeta();
            _plan = ExportPlan.Build(_project, _checked, _preset, ReadMeta(), filter);
            RemarkGlyphs();
            RemarkEpubCheck();
            SyncAvailability();

            // L'aperçu ne vaut plus : il se refait s'il est à l'écran, sinon à sa prochaine ouverture.
            _previewStale = true;
            _pageCount = -1;
            if (ReferenceEquals(_tabs.SelectedItem, _previewTab)) QueuePreview();

            PaintFooter();
            _scopeCount.Text = _plan.Summary();
            _warningsTab.Header = _plan.Warnings.Count > 0 ? "Vérifications (" + _plan.Warnings.Count + ")" : "Vérifications";
            Avail(_ok, !_plan.IsEmpty, _plan.Filtered.Count > 0 ? "Le filtre de l'ampleur écarte tout ce qui est coché." : _plan.Skipped.Count + _plan.Dynamic.Count > 0 ? "Rien de ce qui est coché n'entre dans le format " + ExportFormats.Short(_preset.Format) + "." : "Rien à exporter : cochez au moins un élément dans l'onglet « Ampleur ».");

            _warnings.Children.Clear();
            if (_plan.Warnings.Count == 0)
            {
                _warnings.Children.Add(Hint("Rien à signaler : l'ampleur et le préréglage s'accordent."));
                return;
            }
            foreach (var warning in _plan.Warnings)
            {
                var notice = warning.Level == ExportWarning.Warn
                    ? AstrolabeUi.Notice(warning.Text, false, "export-warning")
                    : AstrolabeUi.Section(warning.Text, null, null, "export-info");
                if (!string.IsNullOrEmpty(warning.Detail))
                {
                    var detail = new TextBlock { Text = warning.Detail, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(warning.Level == ExportWarning.Warn ? 24 : 0, 4, 0, 0) };
                    var holder = notice.Child;
                    var stack = new StackPanel();
                    notice.Child = null;
                    stack.Children.Add(holder);
                    stack.Children.Add(detail);
                    notice.Child = stack;
                }
                _warnings.Children.Add(notice);
            }
        }

        private void PaintFooter()
        {
            var modified = _base != null && _preset.Signature() != _base.Signature();
            _footerPreset.Text = (_base == null ? "" : _base.Name) + (modified ? " — modifié" : "");
            var warn = _plan.WarnCount;
            _footerPlan.Text = _plan.Summary() + (_plan.IsEmpty ? "" : " → " + ExportFormats.Short(_preset.Format) + (ExportFiles.Applies(_preset, _plan) ? " (" + _plan.Count + " fichiers)" : ""))
                + (_pageCount > 0 ? "  ·  " + _pageCount + (_pageCount > 1 ? " pages" : " page") : "")
                + (warn > 0 ? "  ·  " + warn + (warn > 1 ? " avertissements" : " avertissement") : "");
            _footerPlan.Foreground = warn > 0 ? Chrome.Warn : Chrome.SoftText;
        }

        /// <summary>Avant un EPUB, dit qui le contrôlera (20/11) : EPUBCheck,
        /// l'outil officiel, s'il est installé ; sinon le contrôle maison.</summary>
        private void RemarkEpubCheck()
        {
            if (_preset.Format != ExportFormats.Epub || _plan.IsEmpty) return;
            var tool = EpubCheckTool.Find();
            if (tool != null)
                _plan.Warnings.Add(new ExportWarning { Level = ExportWarning.Info, Text = "EPUBCheck est installé : l'EPUB sera validé par l'outil officiel.", Detail = tool.Describe() });
            else
                _plan.Warnings.Add(new ExportWarning { Level = ExportWarning.Info, Text = "L'EPUB passera le contrôle de structure de Marabook.",
                    Detail = "Pour la validation officielle, installez EPUBCheck et Java : Marabook s'en sert dès qu'il trouve le lanceur « epubcheck » dans le PATH, ou le dossier d'EPUBCheck déballé dans " + System.IO.Path.Combine(Platform.Current.DataFolder, "outils") + "." });
        }

        private ExportFilter ReadFilter()
        {
            var filter = new ExportFilter();
            var status = _filterStatus.SelectedIndex;
            if (status > 0 && status <= TextStatus.Keys.Length) filter.OnlyStatus = TextStatus.Keys[status - 1];
            var only = _filterOnlyTag.SelectedIndex;
            if (only > 0 && only <= _tags.Count) filter.OnlyTag = _tags[only - 1];
            var except = _filterExceptTag.SelectedIndex;
            if (except > 0 && except <= _tags.Count) filter.ExceptTag = _tags[except - 1];
            return filter.IsEmpty ? null : filter;
        }

        /// <summary>L'alerte de caractère absent (20/11) : avant un PDF, les
        /// caractères que leur police ne sait pas dessiner rejoignent les
        /// vérifications. Chaque écrit n'est relevé qu'une fois.</summary>
        private void RemarkGlyphs()
        {
            if (_preset.Format != ExportFormats.Pdf || _plan.Texts.Count == 0) return;
            try
            {
                if (_fonts == null) _fonts = new AvaloniaFontEngine();
                var lists = new List<List<MissingGlyph>>();
                foreach (var text in _plan.Texts)
                {
                    List<MissingGlyph> missing;
                    if (!_glyphs.TryGetValue(text.Id, out missing))
                        _glyphs[text.Id] = missing = ExportGlyphs.Missing(text, _project.Styles.EffectiveFor(text), _fonts);
                    lists.Add(missing);
                }
                _plan.Warnings.AddRange(ExportGlyphs.Warnings(lists));
            }
            catch { }
        }

        // ================================================================ aperçu paginé

        /// <summary>Demande l'aperçu : il se calcule une fois la fenêtre
        /// redessinée, et seule la dernière demande compte.</summary>
        private void QueuePreview()
        {
            if (!_previewStale) return;
            var ticket = ++_previewTicket;
            _previewNote.IsVisible = true;
            _previewNote.Text = _preset.Format == ExportFormats.Pdf && !_plan.IsEmpty ? "Mise en pages…" : "";
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate { if (ticket == _previewTicket) ComputePreview(); }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private void ComputePreview()
        {
            _previewStale = false;
            _previewPages.Clear();
            _pageCount = -1;
            string note = null;
            if (_preset.Format != ExportFormats.Pdf)
                note = "L'aperçu paginé montre les pages d'un PDF. " + (_preset.Format == ExportFormats.Epub ? "Un EPUB n'en a pas : le texte recoule sur l'écran du lecteur." : _preset.Format == ExportFormats.Docx || _preset.Format == ExportFormats.Odt ? "Word et OpenOffice recomposent le texte à leur façon : leurs pages ne se devinent pas d'ici." : "Le format " + ExportFormats.Short(_preset.Format) + " n'a pas de pages.");
            else if (_plan.IsEmpty) note = "Rien à montrer : cochez au moins un élément dans l'onglet « Ampleur ».";
            else if (_compose == null) note = "Aperçu indisponible.";
            else
            {
                List<Print.PdfPart> parts = null;
                try { parts = _compose(BuildRequest()); }
                catch { }
                if (parts == null || parts.Count == 0) note = "La mise en pages a échoué : l'aperçu ne peut pas être dessiné.";
                else
                    foreach (var part in parts)
                    {
                        if (part.IsImage) _previewPages.Add(new KeyValuePair<Print.PdfPart, int>(part, 0));
                        else for (var i = 0; i < part.Composition.Pages.Count; i++) _previewPages.Add(new KeyValuePair<Print.PdfPart, int>(part, i));
                    }
            }
            if (_previewPages.Count > 0) _pageCount = _previewPages.Count;
            _previewNote.Text = note ?? "";
            _previewNote.IsVisible = note != null;
            ShowPreviewPage(_previewIndex);
            PaintFooter();
        }

        private void ShowPreviewPage(int index)
        {
            var count = _previewPages.Count;
            _previewIndex = Math.Max(0, Math.Min(count - 1, index));
            if (count == 0)
            {
                _pageView.Show(null, 0);
                _pageLabel.Text = "";
                Avail(_pagePrevious, false, "Aucune page à montrer.");
                Avail(_pageNext, false, "Aucune page à montrer.");
                return;
            }
            var page = _previewPages[_previewIndex];
            _pageView.Show(page.Key, page.Value);
            _pageLabel.Text = "Page " + (_previewIndex + 1) + " sur " + count + "  ·  " + ExportPagePreview.Describe(page.Key);
            Avail(_pagePrevious, _previewIndex > 0, "C'est la première page.");
            Avail(_pageNext, _previewIndex < count - 1, "C'est la dernière page.");
        }

        private ExportRequest BuildRequest()
        {
            ReadOptions();
            var request = new ExportRequest { Preset = _preset.Clone(), Meta = ReadMeta(), OpenAfter = _openAfter.IsChecked == true, Filter = ReadFilter() };
            request.Preset.Name = _base == null ? _preset.Name : _base.Name;
            request.ItemIds.AddRange(_checked);
            if (_writeBack.IsVisible && _writeBack.IsChecked == true) request.WriteBackBook = _plan.Book;
            return request;
        }

        private async void Accept()
        {
            ReadOptions();
            Refresh();
            if (_plan.IsEmpty) return;
            if (_preset.CheckConsistency && _plan.WarnCount > 0)
            {
                var lines = new List<string>();
                foreach (var warning in _plan.Warnings) if (warning.Level == ExportWarning.Warn) lines.Add("— " + warning.Text);
                var answer = await MessageDialog.Show(this, "Avant d'exporter :\n\n" + string.Join("\n", lines.ToArray()) + "\n\nExporter quand même ?",
                    "Gestionnaire d'export", MessageButtons.YesNo, MessageIcon.Warning);
                if (answer != MessageResult.Yes) { _tabs.SelectedItem = _warningsTab; return; }
            }
            _accepted = true;
            Close();
        }

        /// <summary>Le gestionnaire, avec cette ampleur déjà cochée et ce
        /// préréglage choisi (null = le dernier employé). Rend la demande, ou null.</summary>
        public static async Task<ExportRequest> Ask(Window owner, Project project, IEnumerable<string> initialIds, string presetId, Func<ExportRequest, bool> preview, Func<ExportRequest, List<Print.PdfPart>> compose = null)
        {
            var dialog = new ExportManagerDialog(owner, project, initialIds, presetId, preview, compose);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var request = dialog.BuildRequest();
            if (dialog._base != null) ExportPresetStore.LastId = dialog._base.Id;
            return request;
        }

        // ================================================================ sonde

        internal ExportPlan PlanForProbe { get { return _plan; } }
        internal ExportPreset PresetForProbe { get { return _preset; } }
        internal ExportPreset BaseForProbe { get { return _base; } }
        internal TabControl TabsForProbe { get { return _tabs; } }
        internal int PresetCardsForProbe { get { return _presetCards.Count; } }
        internal string FooterForProbe { get { return _footerPreset.Text + " | " + _footerPlan.Text; } }
        internal ExportMeta MetaForProbe { get { return ReadMeta(); } }
        internal bool WriteBackShownForProbe { get { return _writeBack.IsVisible; } }
        internal bool PreviewShownForProbe { get { return _previewButton.IsVisible; } }
        internal int PreviewPagesForProbe { get { return _previewPages.Count; } }
        internal string PreviewLabelForProbe { get { return _pageLabel.Text + " | " + _previewNote.Text; } }
        internal void PreviewForProbe() { _previewStale = true; ComputePreview(); }
        internal void ShowPageForProbe(int index) { ShowPreviewPage(index); }
        internal void ChooseForProbe(string presetId) { Choose(ExportPresets.Find(_presets, presetId)); }
        internal void SavePresetForProbe(string name) { ReadOptions(); SavePresetAs(name); }
        internal void RemovePresetForProbe(string presetId) { RemovePreset(ExportPresets.Find(_presets, presetId)); }
        internal void CheckAllForProbe(bool value) { CheckAll(value); }
        internal void CheckKindForProbe(ItemKind kind) { CheckKind(kind, true); }
        internal ExportRequest RequestForProbe() { return BuildRequest(); }
        /// <summary>Le clic sur la case d'un élément (ou d'un regroupement, par son libellé).</summary>
        internal bool ToggleForProbe(string idOrLabel)
        {
            foreach (var root in _roots)
            {
                var node = FindNode(root, idOrLabel);
                if (node == null) continue;
                Toggle(node);
                return true;
            }
            return false;
        }
        private static ScopeNode FindNode(ScopeNode node, string idOrLabel)
        {
            if ((node.Item != null && node.Item.Id == idOrLabel) || (node.Item == null && node.Label == idOrLabel)) return node;
            foreach (var child in node.Nodes)
            {
                var found = FindNode(child, idOrLabel);
                if (found != null) return found;
            }
            return null;
        }
        internal T ControlForProbe<T>(string tag) where T : Control
        {
            foreach (var control in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this))
            {
                var typed = control as T;
                if (typed != null && (typed.Tag as string) == tag) return typed;
            }
            return null;
        }
    }
}
