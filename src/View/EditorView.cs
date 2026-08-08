using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The rich text editor: format bar, RichTextBox over a pivot
    /// document, find &amp; replace bar, footnotes panel. The pivot stays the
    /// source of truth — Commit() flushes the FlowDocument back into the item.
    /// Text-level undo is the RichTextBox's own; the Binder history is separate.</summary>
    public class EditorView : DockPanel
    {
        private RichTextBox _box;
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private BinderItem _item;
        private Project _project; // image store; null until a project is loaded
        private PageSetup _pageSetup = new PageSetup();
        private bool _loading, _syncing;

        private Border _page;
        private Grid _pageHost;
        private Canvas _sheets;    // hand-drawn selection, behind the text
        private Canvas _pageMarks; // caret, line numbers, ¶ marks — above the text
        private ScrollViewer _scroller;

        // The paged mirror: the RichTextBox lays the text out CONTINUOUSLY in a
        // hidden (clipped) host, and the visible surface is a stack of page
        // frames, each showing a line-accurate slice of it through a
        // VisualBrush. Real pages, no holes, no document mutation — the same
        // illusion as the Composition mode.
        private StackPanel _mirror;
        private readonly List<double> _sliceTops = new List<double>();
        private readonly List<double> _sliceHeights = new List<double>();
        private System.Windows.Shapes.Rectangle _classicCaret;
        private DispatcherTimer _classicBlink;
        private TextPointer _mirrorAnchor; // forwarded drag-selection anchor
        private ComposedView _composed;        // « Composition » mode (composer 4b)
        private RulerView _rulerH, _rulerV;    // règles cm (Ctrl+R)
        private TextBox _trackingBox;          // champ d'approche (em/1000)
        // L'axe d'affichage (batch 26, lot B.2) : Pages / Brouillon / Calme —
        // même moteur, même pivot, seule la présentation change.
        private ToggleButton _pagesViewBtn, _draftViewBtn, _calmViewBtn;
        private bool _draftView;
        private DispatcherTimer _marksTimer;   // full pagination, debounced typing
        private DispatcherTimer _overlayTimer; // fast overlay redraw (scroll, zoom)
        private double _zoom = 1.0;
        private bool _showMarks; // ¶ formatting marks

        // Real pagination: paragraphs are pushed page by page with presentation
        // margins (never recorded in undo — see UndoGate). _pageTops[k] is the
        // top of sheet k, _pageBottoms[k] its bottom, in _box coordinates.
        private readonly Dictionary<Paragraph, double> _appliedExtra = new Dictionary<Paragraph, double>();
        private readonly List<double> _pageTops = new List<double>();
        private readonly List<double> _pageBottoms = new List<double>();
        // Virtual page starts: text flows continuously on stretched sheets,
        // page boundaries are drawn every A4-content-height (the Composition
        // mode remains the physically paged reference).
        private readonly List<double> _virtualStarts = new List<double>();
        private const double PageGap = 18;
        private bool _paginating;

        public event Action<int> ZoomStepRequested; // +10 / -10 (percent)
        public event Action PageSetupChanged;       // edited from the Mise en page tab
        public event Action<int, int> PageInfoChanged; // caret page, page count

        /// <summary>Pages du livre précédant ce document (0 hors livre) :
        /// folio affiché et parité des marges miroir de la Composition. Le
        /// miroir classique n'alterne pas sa colonne (RichTextBox mono-flux)
        /// mais ses folios suivent.</summary>
        public int FolioOffset;

        /// <summary>Décor en-tête/pied du document (fixé par la coquille à
        /// l'ouverture, recalculé après édition via le menu Gabarit).</summary>
        public PageDecor Decor;
        public event Action<bool> MarksToggled;     // ¶ button
        public event Action StylesRequested;        // « Gestion des styles » button

        private ComboBox _styleCombo, _fontCombo, _sizeCombo;
        private ToggleButton _boldBtn, _italicBtn, _underBtn, _strikeBtn;
        private ToggleButton _alignLeft, _alignCenter, _alignRight, _alignJustify;
        private ToggleButton _bulletBtn, _numberBtn, _checkBtn;

        private Border _ribbonBar; // le ruban entier (masqué en mode calme)
        private bool _calm;

        /// <summary>Clic sur le bouton « Mode calme » du ruban.</summary>
        public event Action CalmRequested;

        private Border _searchBar;
        private TextBox _searchBox, _replaceBox;
        private CheckBox _caseCheck;
        private CheckBox _wholeWordCheck;
        private PivotSearch.Match _searchCurrent; // résultat courant (composé)
        private TextBlock _searchInfo;

        private Border _notesBar;
        private StackPanel _notesList;

        private ToggleButton _annVisibleBtn; // « Visibles » de l'onglet Révision
        private Canvas _bubbleLayer; // bulles de commentaire façon Word (classique)
        private Grid _surface;       // hôte du miroir + des bulles

        // ---- correction (batch 26) : le pilote, ses signalements, son panneau
        private readonly Correction.CheckerHost _checkHost = new Correction.CheckerHost();
        private Correction.SpellChecker _spellChecker; // null sans dictionnaire
        private ToggleButton _corrDetailsBtn; // « Détails de correction » (b28)

        /// <summary>Le panneau des signalements vit À DROITE depuis le batch
        /// 28 (il remplace l'inspecteur quand il est ouvert) — la coquille
        /// l'héberge et écoute cette bascule.</summary>
        public event Action CorrectionPanelToggled;
        public UIElement CorrectionPanel { get { return _corrBar; } }
        private List<Correction.Finding> _findings = new List<Correction.Finding>();
        private DispatcherTimer _checkTimer;
        private Border _corrBar;
        private StackPanel _corrList;
        private readonly Dictionary<Correction.FindingCategory, ToggleButton> _corrFilters
            = new Dictionary<Correction.FindingCategory, ToggleButton>();

        public event Action Edited; // any content or footnote change
        public event Action<string> LinkClicked; // Ctrl+click on a [[wiki link]]

        public EditorView()
        {
            _draftView = Settings.AppSettings.DraftView; // avant le ruban
            BuildFormatBar();
            BuildSearchBar();
            BuildNotesBar();
            BuildCorrectionBar();
            BuildPage();

            // Le pilote de correction (batch 26) : les vérificateurs actifs,
            // les ignorés globaux, et la cadence — un debounce de 600 ms,
            // jamais à chaque touche. La passe complète coûte 46 ms sur
            // 50 000 mots (mesure C5) : le fil UI suffit largement.
            _checkHost.Add(new Correction.RepetitionChecker());
            // L'orthographe (batch 27) : moteur Hunspell maison sur le
            // dictionnaire embarqué — absent du disque, le vérificateur se
            // retire sans bruit. Les suggestions sont servies À LA DEMANDE.
            var spellEngine = Correction.SpellDictionary.Default;
            if (spellEngine != null)
            {
                _spellChecker = new Correction.SpellChecker(spellEngine);
                _spellChecker.GlobalWords = Settings.AppSettings.LearnedWords;
                _checkHost.Add(_spellChecker);
                // Le critère composé lexical / grappe enclitique du
                // tokeniseur (batch 29, 0.1) : la MÊME connaissance que
                // l'orthographe — moteur ET mots appris (amendement A1 :
                // un « Vaux-le-Vicomte » enseigné garde son « -le »). Sans
                // dictionnaire, le prédicat reste nul : liste fermée.
                var checker = _spellChecker;
                Correction.FrenchTokenizer.KnownWord = delegate(string word)
                {
                    return spellEngine.Accepts(word) || checker.IsLearned(word);
                };
            }
            _composed.FindingLearn += delegate(Correction.Finding finding, bool projectScope)
            {
                if (_spellChecker == null || finding.Word.Length == 0) return;
                var list = projectScope
                    ? _spellChecker.ProjectWords : _spellChecker.GlobalWords;
                if (!list.Contains(finding.Word)) list.Add(finding.Word);
                if (projectScope) NotifyEdited(); // la liste vit dans le .plot
                else Settings.AppSettings.Save();
                // La connaissance a changé, pas le texte : le cache des
                // vérificateurs locaux doit oublier ses verdicts — et les
                // clés pliées des appris aussi (batch 29, 0.3).
                _spellChecker.InvalidateLearned();
                _checkHost.InvalidateCache();
                RunCheck();
            };
            _composed.SuggestionProvider = delegate(Correction.Finding finding)
            {
                return _spellChecker != null && finding.CheckerId == "spelling"
                    ? _spellChecker.Suggestions(finding.Word)
                    : finding.Suggestions;
            };
            _checkHost.GlobalIgnored = Settings.AppSettings.ProofIgnored;
            _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _checkTimer.Tick += delegate { _checkTimer.Stop(); RunCheck(); };
        }

        public bool HasItem { get { return _item != null; } }

        /// <summary>True when THIS item is the one on screen — the shell's
        /// re-click guard must never trust visibility alone.</summary>
        public bool ShowsItem(Model.BinderItem item) { return _item == item; }

        // ============================================================= construction

        private void BuildFormatBar()
        {
            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            SetDock(bar, Dock.Top);
            // LE RUBAN À DEUX LIGNES (batch 28) : chaque onglet dispose de
            // deux rangées — les blocs denses s'empilent, les séparateurs
            // verticaux courent sur toute la hauteur.
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 3, 8, 3),
                MinHeight = 52
            };
            var typeRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var typeTop = RibbonRow();
            var typeBottom = RibbonRow();
            typeRows.Children.Add(typeTop);
            typeRows.Children.Add(typeBottom);

            _styleCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 2, 0) };
            _styleCombo.SelectionChanged += OnStyleComboChanged;
            typeTop.Children.Add(_styleCombo);

            var manageStyles = new Button
            {
                Content = new TextBlock { Text = "Aa", FontSize = 12, FontWeight = FontWeights.SemiBold },
                ToolTip = "Gestion des styles…",
                Width = 34,
                Margin = new Thickness(0, 0, 6, 0),
                Focusable = false
            };
            manageStyles.Click += delegate
            {
                var handler = StylesRequested;
                if (handler != null) handler();
            };
            typeTop.Children.Add(manageStyles);

            // Éditables : on peut TAPER un nom de police ou une taille
            // personnalisée (Entrée applique).
            _fontCombo = new ComboBox
            {
                Width = 140,
                Margin = new Thickness(0, 0, 6, 0),
                IsEditable = true,
                ToolTip = "Police — tapez un nom puis Entrée pour une police hors liste"
            };
            foreach (var family in ListFonts()) _fontCombo.Items.Add(family);
            _fontCombo.SelectionChanged += OnFontComboChanged;
            _fontCombo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                ApplyTypedFont(_fontCombo.Text);
            };
            typeTop.Children.Add(_fontCombo);

            // Sizes are displayed in points (like every word processor);
            // internally everything stays WPF pixels (1 pt = 4/3 px).
            _sizeCombo = new ComboBox
            {
                Width = 52,
                Margin = new Thickness(0, 0, 10, 0),
                IsEditable = true,
                ToolTip = "Taille (points) — tapez une valeur libre puis Entrée"
            };
            foreach (var size in new[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 })
                _sizeCombo.Items.Add(size);
            _sizeCombo.SelectionChanged += OnSizeComboChanged;
            _sizeCombo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                ApplyTypedSize(_sizeCombo.Text);
            };
            typeTop.Children.Add(_sizeCombo);

            // Variantes de caractère (Fin, Normal, Moyen, Demi-gras, Gras, Noir).
            var weightBtn = new Button
            {
                ToolTip = "Variantes de caractère (graisse)",
                Width = 34,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("variante-caractere", 14, Chrome.Ink)
            };
            var weightMenu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = weightBtn };
            weightBtn.ContextMenu = weightMenu;
            weightBtn.Click += delegate
            {
                BuildWeightMenu(weightMenu); // graisses de LA police, coche incluse
                weightMenu.IsOpen = true;
            };
            typeTop.Children.Add(weightBtn);

            _boldBtn = FormatToggle("G", "Gras (Ctrl+B)", true, false, false, false);
            _boldBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleBold(); _composed.Focus(); return; }
                EditingCommands.ToggleBold.Execute(null, _box);
                AfterFormat();
            };
            _italicBtn = FormatToggle("I", "Italique (Ctrl+I)", false, true, false, false);
            _italicBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleItalic(); _composed.Focus(); return; }
                EditingCommands.ToggleItalic.Execute(null, _box);
                AfterFormat();
            };
            _underBtn = FormatToggle("S", "Souligné (Ctrl+U)", false, false, true, false);
            _underBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleUnderline(); _composed.Focus(); return; }
                ToggleDecoration(TextDecorationLocation.Underline);
            };
            _strikeBtn = FormatToggle("B", "Barré", false, false, false, true);
            _strikeBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleStrike(); _composed.Focus(); return; }
                ToggleDecoration(TextDecorationLocation.Strikethrough);
            };
            // Icônes Flaticon (batch 28) — les lettres G/I/S/B laissent place
            // aux glyphes universels.
            _boldBtn.Content = Icons.Make("bold", 12, Chrome.Ink);
            _italicBtn.Content = Icons.Make("italic", 12, Chrome.Ink);
            _underBtn.Content = Icons.Make("underline", 12, Chrome.Ink);
            _strikeBtn.Content = Icons.Make("strikethrough", 12, Chrome.Ink);
            typeBottom.Children.Add(_boldBtn);
            typeBottom.Children.Add(_italicBtn);
            typeBottom.Children.Add(_underBtn);
            typeBottom.Children.Add(_strikeBtn);
            panel.Children.Add(typeRows);
            panel.Children.Add(VerticalRuleTall());

            // Bloc alignements (haut) / listes (bas), borne a droite.
            var alignRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var alignTop = RibbonRow();
            var alignBottom = RibbonRow();
            alignRows.Children.Add(alignTop);
            alignRows.Children.Add(alignBottom);

            _alignLeft = AlignToggle("left", "Aligné à gauche");
            _alignCenter = AlignToggle("center", "Centré");
            _alignRight = AlignToggle("right", "Aligné à droite");
            _alignJustify = AlignToggle("justify", "Justifié");
            _alignLeft.Content = Icons.Make("align-left", 12, Chrome.Ink);
            _alignCenter.Content = Icons.Make("align-center", 12, Chrome.Ink);
            // Pas d'icône « droite » dans le jeu : la gauche, en miroir.
            var alignRightIcon = Icons.Make("align-left", 12, Chrome.Ink) as FrameworkElement;
            if (alignRightIcon != null)
                alignRightIcon.LayoutTransform = new ScaleTransform(-1, 1);
            _alignRight.Content = alignRightIcon;
            _alignJustify.Content = Icons.Make("align-justify", 12, Chrome.Ink);
            alignTop.Children.Add(_alignLeft);
            alignTop.Children.Add(_alignCenter);
            alignTop.Children.Add(_alignRight);
            alignTop.Children.Add(_alignJustify);

            _bulletBtn = IconToggle("list", "Liste à puces");
            _bulletBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("bullet"); _composed.Focus(); return; }
                EditingCommands.ToggleBullets.Execute(null, _box);
                AfterFormat();
            };
            _numberBtn = IconToggle("list-numbers-bold", "Liste numérotée");
            _numberBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("number"); _composed.Focus(); return; }
                EditingCommands.ToggleNumbering.Execute(null, _box);
                AfterFormat();
            };
            _checkBtn = IconToggle("list-check", "Case à cocher (☐ → ☑ → retirer)");
            _checkBtn.Click += delegate
            {
                if (ComposedActive) { _composed.TypeText("☐ "); return; }
                ToggleChecklist();
            };
            alignBottom.Children.Add(_bulletBtn);
            alignBottom.Children.Add(_numberBtn);
            alignBottom.Children.Add(_checkBtn);
            panel.Children.Add(alignRows);
            panel.Children.Add(VerticalRuleTall());

            // Le reste du ruban Texte coule sur une ligne, centre verticalement.
            var rest = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(rest);

            rest.Children.Add(PaletteButton("Couleur du texte", true));
            rest.Children.Add(PaletteButton("Surlignage", false));

            var imageBtn = new Button
            {
                ToolTip = "Insérer une image…",
                Width = 34,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("image-square-bold", 14, Chrome.Ink)
            };
            imageBtn.Click += delegate { InsertImage(); };
            rest.Children.Add(imageBtn);

            var ruleBtn = new Button
            {
                ToolTip = "Ligne horizontale",
                Width = 34,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("horizontal-rule", 14, Chrome.Ink)
            };
            ruleBtn.Click += delegate { InsertRule(); };
            rest.Children.Add(ruleBtn);

            var separatorBtn = new Button
            {
                ToolTip = "Séparateur de scène (texte et police : Fichier → Paramètres du projet)",
                Width = 34,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("symbol", 14, Chrome.Ink)
            };
            separatorBtn.Click += delegate { InsertSeparator(); };
            rest.Children.Add(separatorBtn);
            rest.Children.Add(VerticalRule());

            _marksBtn = new ToggleButton
            {
                Content = Icons.Make("paragraph", 14, Chrome.Ink),
                ToolTip = "Afficher les caractères d'impression (¶ espaces · insécables ° tabulations →)",
                Width = 32,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
            _marksBtn.Click += delegate
            {
                var handler = MarksToggled;
                if (handler != null) handler(_marksBtn.IsChecked == true);
            };
            rest.Children.Add(_marksBtn);

            // Approche (tracking, millièmes de cadratin) — champ de valeur à
            // la Adobe : petits boutons ± verticaux à gauche, valeur absolue
            // lisible et retouchable. Rendue par le compositeur (Composition,
            // aperçu, PDF).
            rest.Children.Add(VerticalRule());
            var trackLabel = new TextBlock
            {
                Text = "Approche",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 0),
                ToolTip = "Espacement entre les caractères, en millièmes de cadratin "
                    + "— valeur de la sélection, pas de 5 aux flèches"
            };
            var kerningIcon = Icons.Make("kerning", 13, Chrome.SoftText) as FrameworkElement;
            if (kerningIcon != null)
            {
                kerningIcon.VerticalAlignment = VerticalAlignment.Center;
                kerningIcon.Margin = new Thickness(0, 0, 3, 0);
                rest.Children.Add(kerningIcon);
            }
            rest.Children.Add(trackLabel);
            var spinner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var trackUp = new RepeatButton
            {
                Content = "▲",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false,
                ToolTip = "+5"
            };
            trackUp.Click += delegate { ApplyTrackingStep(5); };
            var trackDown = new RepeatButton
            {
                Content = "▼",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false,
                ToolTip = "−5"
            };
            trackDown.Click += delegate { ApplyTrackingStep(-5); };
            spinner.Children.Add(trackUp);
            spinner.Children.Add(trackDown);
            rest.Children.Add(spinner);
            _trackingBox = new TextBox
            {
                Width = 42,
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = "0",
                ToolTip = "Approche de la sélection (millièmes de cadratin, −100 à 400) — "
                    + "Entrée pour appliquer ; vide = mixte"
            };
            _trackingBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                double value;
                if (double.TryParse(_trackingBox.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                    ApplyTrackingAbsolute(value);
            };
            rest.Children.Add(_trackingBox);

            // Ribbon: « Texte » (this panel) + « Mise en page » (page setup).
            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            tabs.Items.Add(new TabItem { Header = "Texte", Content = panel });
            tabs.Items.Add(new TabItem { Header = "Mise en page", Content = BuildPageSetupTab() });
            tabs.Items.Add(new TabItem { Header = "Gabarit", Content = BuildDecorTab() });
            tabs.Items.Add(new TabItem { Header = "Composition", Content = BuildCompositionTab() });
            tabs.Items.Add(new TabItem { Header = "Révision", Content = BuildRevisionTab() });

            // L'AXE D'AFFICHAGE (batch 26), collé au bord droit des onglets :
            // comment on VOIT la page — Pages (marges, folios, gabarits),
            // Brouillon (colonne continue sans décor), Calme (rien que le
            // texte). Même moteur, même pivot dessous — jamais un moteur.
            var views = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 6, 0)
            };
            _pagesViewBtn = ViewToggle("Pages",
                "La page réelle : marges, folios, gabarits");
            _pagesViewBtn.Click += delegate { SetDraftView(false); };
            views.Children.Add(_pagesViewBtn);
            _draftViewBtn = ViewToggle("Brouillon",
                "Colonne continue sans décor de page ni folio — écrire au fil de l'eau");
            _draftViewBtn.Click += delegate { SetDraftView(true); };
            views.Children.Add(_draftViewBtn);
            _calmViewBtn = ViewToggle("Calme",
                "Ne garder que le texte (Échap pour revenir)");
            _calmViewBtn.Click += delegate
            {
                UpdateViewButtons(); // l'état réel suivra SetCalm
                var handler = CalmRequested;
                if (handler != null) handler();
            };
            views.Children.Add(_calmViewBtn);
            UpdateViewButtons();

            var host = new Grid();
            host.Children.Add(tabs);
            host.Children.Add(views);
            bar.Child = host;
            _ribbonBar = bar;
            Children.Add(bar);
        }

        private ToggleButton ViewToggle(string label, string tooltip)
        {
            return new ToggleButton
            {
                Content = label,
                ToolTip = tooltip,
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11,
                Focusable = false
            };
        }

        /// <summary>L'état du sélecteur d'affichage — une seule position
        /// enfoncée ; Pages et Brouillon se grisent en mode de compatibilité
        /// (le repli classique n'a que la vue paginée du miroir).</summary>
        private void UpdateViewButtons()
        {
            if (_pagesViewBtn == null) return;
            _pagesViewBtn.IsChecked = !_calm && !_draftView;
            _draftViewBtn.IsChecked = !_calm && _draftView;
            _calmViewBtn.IsChecked = _calm;
            var composed = !Settings.AppSettings.ClassicCompatibility;
            _pagesViewBtn.IsEnabled = composed;
            _draftViewBtn.IsEnabled = composed;
            if (!composed)
            {
                _pagesViewBtn.ToolTip = "Mode de compatibilité actif (Préférences)";
                _draftViewBtn.ToolTip = _pagesViewBtn.ToolTip;
            }
        }

        /// <summary>Bascule Pages ↔ Brouillon : ré-attache la surface composée
        /// avec le réglage de page dérivé. Persistant (réglage d'application).</summary>
        private void SetDraftView(bool draft)
        {
            _draftView = draft;
            Settings.AppSettings.DraftView = draft;
            Settings.AppSettings.Save();
            UpdateViewButtons();
            if (ComposedActive && _item != null) SetComposition(true);
        }

        /// <summary>Le réglage de page du BROUILLON : même moteur, même pivot —
        /// colonne continue à mesure confortable, sans marges apparentes,
        /// numéros de ligne ni folio. Les « pages » font 600 mm : la couture
        /// entre deux tranches reste rare. Ce n'est PAS un troisième chemin
        /// de composition, juste un PageSetup dérivé (doctrine du lot B.2).</summary>
        private static PageSetup DraftSetup(PageSetup source)
        {
            var draft = source.Clone();
            draft.PageWidthMm = 165;
            draft.PageHeightMm = 600;
            draft.MarginTopMm = 10;
            draft.MarginBottomMm = 10;
            draft.MarginLeftMm = 18;
            draft.MarginRightMm = 18;
            draft.Columns = 1;
            draft.ShowMarginGuides = false;
            draft.LineNumbers = false;
            draft.FooterPageNumbers = false;
            return draft;
        }

        /// <summary>Mode calme : le ruban et le panneau de notes s'effacent,
        /// la coquille masque le reste (Pile, inspecteur, menus, barre d'état).</summary>
        public void SetCalm(bool calm)
        {
            _calm = calm;
            _ribbonBar.Visibility = calm ? Visibility.Collapsed : Visibility.Visible;
            if (calm) _searchBar.Visibility = Visibility.Collapsed;
            UpdateViewButtons(); // le sélecteur d'affichage suit
            RebuildNotesPanel(); // la visibilité des panneaux suit _calm
            RebuildCorrectionPanel();
            RebuildAnnotationsPanel();
        }

        // ============================================================= « Gabarit » tab

        /// <summary>Header/footer of THIS document — applied to every page.
        /// A page gabarit applied to the document wins over these.</summary>
        private UIElement BuildDecorTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
            var header = new Button
            {
                Content = TabButtonContent("sort-descending-bold", "Éditer l'en-tête…"),
                ToolTip = "Ligne d'en-tête sur toutes les pages du document "
                    + "(jetons : {page}, {pages}, {titre})",
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            header.Click += delegate { EditHeaderFooter(true); };
            panel.Children.Add(header);
            var footer = new Button
            {
                Content = TabButtonContent("sort-ascending-bold", "Éditer le pied de page…"),
                ToolTip = "Pied de page sur toutes les pages — c'est ici que se "
                    + "règle le look des numéros de page ({page})",
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            footer.Click += delegate { EditHeaderFooter(false); };
            panel.Children.Add(footer);
            panel.Children.Add(new TextBlock
            {
                Text = "Un gabarit de pages appliqué au document (livres) remplace ces réglages.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        /// <summary>Approche sur la sélection : effective dans la Composition
        /// (le RichTextBox classique ne rend pas l'interlettrage — le réglage
        /// s'applique au pivot et se voit en Composition/aperçu/PDF).</summary>
        private void ApplyTrackingStep(double delta)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.ApplyTracking(delta);
                SyncTrackingBox();
                _composed.Focus();
                return;
            }
            MessageBox.Show(Window.GetWindow(this),
                "L'approche se règle depuis le mode Composition (onglet Composition),\n"
                + "où son effet est visible à l'écran.",
                "Approche", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyTrackingAbsolute(double value)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.SetTracking(value);
                SyncTrackingBox();
                _composed.Focus();
                return;
            }
            MessageBox.Show(Window.GetWindow(this),
                "L'approche se règle depuis le mode Composition (onglet Composition),\n"
                + "où son effet est visible à l'écran.",
                "Approche", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Combos du ruban en MODE COMPOSITION : style, police et
        /// taille au caret — la synchro classique (SyncToolbar) s'arrête dès
        /// que la surface composée est active, d'où des combos vides.</summary>
        private void SyncToolbarComposed()
        {
            if (!ComposedActive || _item == null || _styleCombo == null) return;
            _syncing = true;
            try
            {
                string styleId, fontFamily;
                double sizePt;
                _composed.CaretFormat(out styleId, out fontFamily, out sizePt);
                ComboBoxItem match = null;
                foreach (ComboBoxItem candidate in _styleCombo.Items)
                    if ((string)candidate.Tag == styleId) { match = candidate; break; }
                _styleCombo.SelectedItem = match;
                _fontCombo.SelectedItem = fontFamily;
                if (fontFamily != null && _fontCombo.SelectedItem == null)
                    _fontCombo.Text = fontFamily;
                _sizeCombo.SelectedItem = (int)Math.Round(sizePt);
                if (_sizeCombo.SelectedItem == null)
                    _sizeCombo.Text = sizePt.ToString("0.#",
                        System.Globalization.CultureInfo.CurrentCulture);

                // Les BASCULES aussi (gras/italique/…, alignements exclusifs,
                // listes) — sans cette synchro, un ToggleButton cliqué gardait
                // son état à lui (centré ET justifié actifs à la fois).
                bool bold, italic, underline, strike;
                string align, listKind;
                _composed.SelectionFlags(out bold, out italic, out underline,
                    out strike, out align, out listKind);
                _boldBtn.IsChecked = bold;
                _italicBtn.IsChecked = italic;
                _underBtn.IsChecked = underline;
                _strikeBtn.IsChecked = strike;
                _alignLeft.IsChecked = align == "left";
                _alignCenter.IsChecked = align == "center";
                _alignRight.IsChecked = align == "right";
                _alignJustify.IsChecked = align == "justify";
                _bulletBtn.IsChecked = listKind == "bullet";
                _numberBtn.IsChecked = listKind == "number";
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>Reflète l'approche de la sélection dans le champ (vide =
        /// valeurs mixtes).</summary>
        private void SyncTrackingBox()
        {
            if (_trackingBox == null || _trackingBox.IsKeyboardFocused) return;
            if (!ComposedActive || _item == null) { _trackingBox.Text = "0"; return; }
            var value = _composed.SelectionTracking();
            _trackingBox.Text = value.HasValue
                ? value.Value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)
                : "";
        }

        private void EditHeaderFooter(bool isHeader)
        {
            if (_item == null) return;
            var edited = HeaderFooterDialog.Edit(Window.GetWindow(this),
                isHeader ? _item.Header : _item.Footer, isHeader);
            if (edited == null) return; // annulé
            if (isHeader) _item.Header = edited.IsEmpty ? null : edited;
            else _item.Footer = edited.IsEmpty ? null : edited;
            Decor = PageDecor.For(_item, _project);
            if (ComposedActive)
            {
                _composed.Decor = Decor;
                _composed.RefreshComposition();
            }
            NotifyEdited();
        }

        // ============================================================= « Composition » tab

        public event Action PreviewRequested;  // Aperçu des pages
        public event Action PrintRequested;    // Imprimer / PDF
        public event Action ExportRequested;   // Exporter l'écrit
        public event Action CompileRequested;  // Compiler le manuscrit
        public event Action PdfRequested;      // PDF prêt à imprimer (4b-2)

        private UIElement BuildCompositionTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };

            // Le bouton « Composition » a disparu avec le gel du classique
            // (batch 26) : l'onglet ne porte plus que les sorties.
            panel.Children.Add(CompositionAction("Aperçu des pages",
                "Les pages exactes, prêtes à relire (Ctrl+Alt+P)",
                delegate { var handler = PreviewRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("Imprimer / PDF…",
                "Impression ou PDF via « Microsoft Print to PDF » (Ctrl+P)",
                delegate { var handler = PrintRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("PDF prêt à imprimer…",
                "PDF maison : polices incorporées, fond perdu, traits de coupe",
                delegate { var handler = PdfRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("Exporter l'écrit…",
                "docx, odt, RTF, Markdown, texte (Ctrl+E)",
                delegate { var handler = ExportRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("Compiler le manuscrit…",
                "Assembler les écrits en un manuscrit exportable (Ctrl+Maj+E)",
                delegate { var handler = CompileRequested; if (handler != null) handler(); }));
            return panel;
        }

        private Button CompositionAction(string label, string tooltip, Action onClick)
        {
            var button = new Button
            {
                Content = label,
                ToolTip = tooltip,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            button.Click += delegate { onClick(); };
            return button;
        }

        private static UIElement TabButtonContent(string iconName, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make(iconName, 14, Chrome.Ink) as FrameworkElement;
            if (icon != null) icon.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        // ============================================================= « Mise en page » tab

        private ComboBox _marginsCombo, _sizeComboPage, _columnsCombo;
        private ToggleButton _guidesBtn, _lineNumbersBtn, _hyphenBtn, _folioBtn;
        private ToggleButton _marksBtn;
        private bool _syncingPage;

        private UIElement BuildPageSetupTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };

            panel.Children.Add(PageLabel("Marges"));
            _marginsCombo = new ComboBox
            {
                Width = 150,
                Margin = new Thickness(4, 0, 10, 0),
                ToolTip = "Nomenclature PAO : de tête (haut), de pied (bas), "
                    + "petit fond (côté reliure), grand fond (côté extérieur)"
            };
            _marginsCombo.Items.Add("Livre (20/20/30/20)");
            _marginsCombo.Items.Add("Uniformes (2,5 cm)");
            _marginsCombo.Items.Add("Étroites (1,27 cm)");
            _marginsCombo.Items.Add("Personnalisées…");
            _marginsCombo.SelectionChanged += OnMarginsComboChanged;
            panel.Children.Add(_marginsCombo);

            panel.Children.Add(PageLabel("Taille"));
            _sizeComboPage = new ComboBox { Width = 150, Margin = new Thickness(4, 0, 10, 0) };
            _sizeComboPage.Items.Add("A4 (21 × 29,7 cm)");
            _sizeComboPage.Items.Add("A5 (14,8 × 21 cm)");
            _sizeComboPage.Items.Add("Letter (21,6 × 27,9 cm)");
            _sizeComboPage.Items.Add("Livre (14 × 21,6 cm)");
            _sizeComboPage.Items.Add("Personnalisée…");
            _sizeComboPage.SelectionChanged += OnPageSizeComboChanged;
            panel.Children.Add(_sizeComboPage);

            var columnsIcon = Icons.Make("text-columns-bold", 14, Chrome.SoftText) as FrameworkElement;
            if (columnsIcon != null)
            {
                columnsIcon.VerticalAlignment = VerticalAlignment.Center;
                columnsIcon.ToolTip = "Colonnes";
                panel.Children.Add(columnsIcon);
            }
            _columnsCombo = new ComboBox { Width = 46, Margin = new Thickness(4, 0, 10, 0), ToolTip = "Colonnes — appliquées à l'export et à l'impression" };
            _columnsCombo.Items.Add(1);
            _columnsCombo.Items.Add(2);
            _columnsCombo.Items.Add(3);
            _columnsCombo.SelectionChanged += delegate
            {
                if (_syncingPage || _project == null || _columnsCombo.SelectedItem == null) return;
                _pageSetup.Columns = (int)_columnsCombo.SelectedItem;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_columnsCombo);

            var breakBtn = new Button
            {
                Content = TabButtonContent("file-arrow-down-bold", "Saut de page"),
                ToolTip = "Commencer une nouvelle page au paragraphe du curseur (Ctrl+Entrée)",
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            breakBtn.Click += delegate { InsertPageBreak(); };
            panel.Children.Add(breakBtn);

            _guidesBtn = PageToggle("Marges", "Cadres de marges sur chaque page");
            _guidesBtn.Content = TabButtonContent("margins", "Marges");
            _guidesBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.ShowMarginGuides = _guidesBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_guidesBtn);

            _lineNumbersBtn = PageToggle("N° de ligne", "Numéros de ligne à l'export Word et à l'impression");
            _lineNumbersBtn.Content = TabButtonContent("list-numbers-bold", "N° de ligne");
            _lineNumbersBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.LineNumbers = _lineNumbersBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_lineNumbersBtn);

            _hyphenBtn = PageToggle("Césure", "Coupure des mots en fin de ligne");
            _hyphenBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.Hyphenation = _hyphenBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_hyphenBtn);

            _folioBtn = PageToggle("Folio", "Numéro de page centré en pied de page (aperçu, impression, export Word)");
            _folioBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.FooterPageNumbers = _folioBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_folioBtn);

            return panel;
        }

        private TextBlock PageLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private ToggleButton PageToggle(string label, string tooltip)
        {
            return new ToggleButton
            {
                Content = label,
                ToolTip = tooltip,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
        }

        private void OnMarginsComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPage || _project == null || _marginsCombo.SelectedIndex < 0) return;
            var page = _pageSetup;
            if (_marginsCombo.SelectedIndex == 0) SetMarginsMm(page, 20, 20, 30, 20);
            else if (_marginsCombo.SelectedIndex == 1) SetMarginsMm(page, 25, 25, 25, 25);
            else if (_marginsCombo.SelectedIndex == 2) SetMarginsMm(page, 12.7, 12.7, 12.7, 12.7);
            else
            {
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Marges (cm)",
                    new[]
                    {
                        "De tête (marge haute)",
                        "De pied (marge basse)",
                        "Petit fond (côté reliure)",
                        "Grand fond (côté extérieur)"
                    },
                    new[] { page.MarginTopMm / 10, page.MarginBottomMm / 10, page.MarginLeftMm / 10, page.MarginRightMm / 10 },
                    0.5, 10);
                if (values == null) { SyncPageTab(); return; }
                SetMarginsMm(page, values[0] * 10, values[1] * 10, values[2] * 10, values[3] * 10);
            }
            AfterPageSetupEdit();
        }

        private static void SetMarginsMm(PageSetup page, double top, double bottom, double left, double right)
        {
            page.MarginTopMm = top;
            page.MarginBottomMm = bottom;
            page.MarginLeftMm = left;
            page.MarginRightMm = right;
        }

        private void OnPageSizeComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPage || _project == null || _sizeComboPage.SelectedIndex < 0) return;
            var page = _pageSetup;
            if (_sizeComboPage.SelectedIndex == 0) { page.PageWidthMm = 210; page.PageHeightMm = 297; }
            else if (_sizeComboPage.SelectedIndex == 1) { page.PageWidthMm = 148; page.PageHeightMm = 210; }
            else if (_sizeComboPage.SelectedIndex == 2) { page.PageWidthMm = 216; page.PageHeightMm = 279; }
            else if (_sizeComboPage.SelectedIndex == 3) { page.PageWidthMm = 140; page.PageHeightMm = 216; }
            else
            {
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Taille de page (cm)",
                    new[] { "Largeur", "Hauteur" },
                    new[] { page.PageWidthMm / 10, page.PageHeightMm / 10 }, 5, 100);
                if (values == null) { SyncPageTab(); return; }
                page.PageWidthMm = values[0] * 10;
                page.PageHeightMm = values[1] * 10;
            }
            AfterPageSetupEdit();
        }

        private void AfterPageSetupEdit()
        {
            ApplyPageVisuals();
            if (ComposedActive) _composed.RefreshComposition();
            var handler = PageSetupChanged;
            if (handler != null) handler();
        }

        // ============================================================= Composition mode

        /// <summary>True while the composed surface is the writing surface —
        /// then the pivot is the live source of truth and the RichTextBox is
        /// dormant/stale.</summary>
        public bool ComposedActive
        {
            get { return _composed != null && _composed.HasItem
                    && _composed.Visibility == Visibility.Visible; }
        }

        /// <summary>Bascule interne entre la surface composée (LA surface
        /// d'édition depuis le gel du batch 26) et le repli classique (mode
        /// de compatibilité des Préférences). Plus aucun bouton de ruban n'y
        /// mène — le choix de l'utilisateur est un AFFICHAGE (Pages /
        /// Brouillon / Calme), jamais un moteur.</summary>
        public void SetComposition(bool active)
        {
            if (!active)
            {
                if (ComposedActive)
                {
                    // Back to the classic surface: reload it from the pivot,
                    // which the composed editor was mutating live.
                    _composed.Detach();
                    _composed.Visibility = Visibility.Collapsed;
                    _scroller.Visibility = Visibility.Visible;
                    if (_item != null)
                    {
                        _loading = true;
                        _box.Document = FlowConverter.ToFlow(_item.Document, _styles,
                            _project, Settings.AppSettings.ShowAnnotations);
                        _loading = false;
                        ApplyPageVisuals();
                        RebuildNotesPanel();
                        RebuildAnnotationsPanel();
                    }
                    _box.Focus();
                }
                else
                {
                    _composed.Detach();
                    _composed.Visibility = Visibility.Collapsed;
                    _scroller.Visibility = Visibility.Visible;
                }
                RunCheck(); // hors composé : panneau et ondulés s'éteignent
                return;
            }
            if (_item == null) return;
            try
            {
                // The classic surface may hold unsaved keystrokes: flush first
                // (la liste d'annotations vit à part des runs, elle survit).
                if (!ComposedActive && _box.Document != null)
                {
                    var annotations = _item.Document.Annotations;
                    _item.Document = FlowConverter.FromFlow(_box.Document, _styles,
                        _item.Document.Footnotes, _project);
                    _item.Document.Annotations = annotations;
                    _item.Document.AnnotationOrder(true);
                }
                _composed.SetZoom(_zoom);
                // Brouillon : même moteur, réglage de page dérivé — colonne
                // continue sans décor ni folio (voir DraftSetup).
                _composed.FolioOffset = _draftView ? 0 : FolioOffset;
                _composed.Decor = _draftView ? null : Decor;
                _composed.Attach(_item, _styles,
                    _draftView ? DraftSetup(_pageSetup) : _pageSetup, _project);
                _composed.Visibility = Visibility.Visible;
                _scroller.Visibility = Visibility.Collapsed;
                _composed.Focus();
                RebuildAnnotationsPanel();
                RunCheck(); // la surface composée s'ouvre vérifiée
            }
            catch (Exception error)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Composition impossible :\n" + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetComposition(false);
            }
        }

        /// <summary>Reflects the page setup in the « Mise en page » tab.</summary>
        private void SyncPageTab()
        {
            if (_marginsCombo == null) return;
            _syncingPage = true;
            try
            {
                var page = _pageSetup;
                _marginsCombo.SelectedIndex =
                    Near(page.MarginTopMm, 20) && Near(page.MarginBottomMm, 20)
                        && Near(page.MarginLeftMm, 30) && Near(page.MarginRightMm, 20) ? 0
                    : Near(page.MarginTopMm, 25) && Near(page.MarginBottomMm, 25)
                        && Near(page.MarginLeftMm, 25) && Near(page.MarginRightMm, 25) ? 1
                    : Near(page.MarginTopMm, 12.7) && Near(page.MarginLeftMm, 12.7) ? 2 : 3;
                _sizeComboPage.SelectedIndex =
                    Near(page.PageWidthMm, 210) && Near(page.PageHeightMm, 297) ? 0
                    : Near(page.PageWidthMm, 148) ? 1
                    : Near(page.PageWidthMm, 216) && Near(page.PageHeightMm, 279) ? 2
                    : Near(page.PageWidthMm, 140) ? 3 : 4;
                _columnsCombo.SelectedItem = Math.Max(1, Math.Min(3, page.Columns));
                _guidesBtn.IsChecked = page.ShowMarginGuides;
                _lineNumbersBtn.IsChecked = page.LineNumbers;
                _hyphenBtn.IsChecked = page.Hyphenation;
                _folioBtn.IsChecked = page.FooterPageNumbers;
            }
            finally
            {
                _syncingPage = false;
            }
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 0.05;
        }

        /// <summary>Recomputes the ruler bands from the on-screen page rects
        /// of the active surface. Cheap: geometry only.</summary>
        public void UpdateRulers()
        {
            if (_rulerH == null) return;
            var show = Settings.AppSettings.ShowRulers && _item != null;
            _rulerH.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _rulerV.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var pages = new System.Collections.Generic.List<Rect>();
            try
            {
                if (ComposedActive)
                    pages = _composed.PageRects(_rulerH);
                else
                    foreach (var child in _mirror.Children)
                    {
                        var frame = child as FrameworkElement;
                        if (frame == null || frame.ActualWidth < 1) continue;
                        var p0 = frame.TranslatePoint(new Point(0, 0), _rulerH);
                        var p1 = frame.TranslatePoint(
                            new Point(frame.ActualWidth, frame.ActualHeight), _rulerH);
                        pages.Add(new Rect(p0, p1));
                    }
            }
            catch { }
            _rulerH.Update(pages, _pageSetup.PageWidthMm, _pageSetup.PageHeightMm);
            _rulerV.Update(pages, _pageSetup.PageWidthMm, _pageSetup.PageHeightMm);
        }

        /// <summary>¶ state, pushed by the shell so both editors stay in sync.
        /// Redraws instantly — no debounce on an explicit toggle.</summary>
        public void SetFormattingMarks(bool visible)
        {
            _showMarks = visible;
            if (_marksBtn != null) _marksBtn.IsChecked = visible;
            RefreshOverlay();
        }

        private ToggleButton IconToggle(string iconName, string tooltip)
        {
            return new ToggleButton
            {
                Content = Icons.Make(iconName, 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 32,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private ToggleButton ListToggle(string label, string tooltip)
        {
            return new ToggleButton
            {
                Content = new TextBlock { Text = label, FontSize = 13 },
                ToolTip = tooltip,
                Width = 32,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private static List<string> ListFonts()
        {
            var names = new List<string>();
            foreach (var family in Fonts.SystemFontFamilies) names.Add(family.Source);
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        private ToggleButton FormatToggle(string label, string tooltip,
            bool bold, bool italic, bool underline, bool strike)
        {
            // Icônes vectorielles embarquées (jeu Phosphor).
            var icon = bold ? "text-b-bold"
                     : italic ? "text-italic-bold"
                     : underline ? "text-underline-bold"
                     : "text-strikethrough-bold";
            return new ToggleButton
            {
                Content = Icons.Make(icon, 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 32,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        /// <summary>Only the weights the current font really ships, with a
        /// check mark on the (uniform) weight of the selection.</summary>
        private void BuildWeightMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            var family = CurrentFontFamily();
            var available = AvailableWeights(family);
            var current = CurrentWeightName(); // null = Normal, "mixed" = pas de coche

            string[] labels = { "Fin", "Normal", "Moyen", "Demi-gras", "Gras", "Noir" };
            string[] weights = { "Light", null, "Medium", "SemiBold", "Bold", "Black" };
            for (var i = 0; i < labels.Length; i++)
            {
                // Normal et Gras existent toujours (WPF les synthétise au besoin).
                var weight = weights[i];
                if (weight != null && weight != "Bold" && !available.Contains(weight)) continue;
                var entry = new MenuItem
                {
                    Header = labels[i],
                    FontWeight = weight == null ? FontWeights.Normal : FlowConverter.ParseWeight(weight),
                    IsChecked = current != "mixed"
                        && ((weight == null && current == null) || weight == current)
                };
                var weightRef = weight;
                entry.Click += delegate { ApplyWeight(weightRef); };
                menu.Items.Add(entry);
            }
        }

        private static HashSet<string> AvailableWeights(string family)
        {
            var set = new HashSet<string>();
            try
            {
                foreach (var typeface in new FontFamily(family).FamilyTypefaces)
                {
                    var name = FlowConverter.WeightName(typeface.Weight);
                    if (name != null) set.Add(name);
                }
            }
            catch { }
            return set;
        }

        private string CurrentFontFamily()
        {
            if (ComposedActive)
            {
                var family = _composed.GetCaretFontFamily();
                if (family != null) return family;
            }
            else
            {
                var value = _box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                if (value != null) return value.Source;
            }
            return _styles.Body.FontFamily;
        }

        private string CurrentWeightName()
        {
            if (ComposedActive) return _composed.GetSelectionWeightName();
            var value = _box.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            if (!(value is FontWeight)) return "mixed";
            var weight = (FontWeight)value;
            var name = FlowConverter.WeightName(weight);
            if (name != null) return name;
            return weight >= FontWeights.Bold ? "Bold" : null;
        }

        private void ApplyWeight(string weight)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.ApplyWeight(weight);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,
                weight == null ? FontWeights.Normal : FlowConverter.ParseWeight(weight));
            AfterFormat();
        }

        private ToggleButton AlignToggle(string align, string tooltip)
        {
            var button = new ToggleButton
            {
                Content = Icons.Make("text-align-" + align + "-bold", 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 32,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Tag = align
            };
            button.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyAlign(align); _composed.Focus(); return; }
                var command = align == "center" ? EditingCommands.AlignCenter
                            : align == "right" ? EditingCommands.AlignRight
                            : align == "justify" ? EditingCommands.AlignJustify
                            : EditingCommands.AlignLeft;
                command.Execute(null, _box);
                AfterFormat();
            };
            return button;
        }

        /// <summary>Tiny alignment icon: four bars whose widths sketch the mode.</summary>
        private UIElement AlignIcon(string align)
        {
            var panel = new StackPanel { Width = 14 };
            var widths = align == "center" ? new double[] { 14, 10, 14, 8 }
                       : align == "right" ? new double[] { 14, 10, 14, 8 }
                       : align == "justify" ? new double[] { 14, 14, 14, 14 }
                       : new double[] { 14, 10, 14, 8 };
            var alignment = align == "center" ? HorizontalAlignment.Center
                          : align == "right" ? HorizontalAlignment.Right
                          : HorizontalAlignment.Left;
            foreach (var width in widths)
                panel.Children.Add(new Border
                {
                    Height = 2,
                    Width = width,
                    Background = Chrome.Ink,
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = alignment
                });
            return panel;
        }

        private static Border VerticalRule()
        {
            return new Border
            {
                Width = 1,
                Background = Chrome.Border,
                Margin = new Thickness(7, 2, 7, 2)
            };
        }

        // --------------------------------------- le ruban à deux lignes (b28)

        /// <summary>Une rangée d'un bloc empilé du ruban.</summary>
        private static StackPanel RibbonRow()
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        /// <summary>Séparateur vertical courant sur les deux lignes.</summary>
        private static Border VerticalRuleTall()
        {
            return new Border
            {
                Width = 1,
                MinHeight = 44,
                Background = Chrome.Border,
                Margin = new Thickness(7, 2, 7, 2)
            };
        }

        /// <summary>Un bouton « sur deux lignes » du ruban : libellé enroulé,
        /// pleine hauteur — la monnaie courante d'Office.</summary>
        private static TextBlock TallLabel(string label)
        {
            return new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 76,
                FontSize = 11
            };
        }

        private Button TallButton(string label, string tooltip)
        {
            return new Button
            {
                Content = TallLabel(label),
                ToolTip = tooltip,
                MinHeight = 44,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Variante à ICÔNE au-dessus du libellé (façon Office).</summary>
        private Button TallButton(string icon, string label, string tooltip)
        {
            var content = new StackPanel();
            var glyph = Icons.Make(icon, 16, Chrome.Ink) as FrameworkElement;
            if (glyph != null)
            {
                glyph.HorizontalAlignment = HorizontalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 0, 2);
                content.Children.Add(glyph);
            }
            content.Children.Add(TallLabel(label));
            var button = TallButton(label, tooltip);
            button.Content = content;
            return button;
        }

        private ToggleButton TallToggle(string label, string tooltip)
        {
            return new ToggleButton
            {
                Content = TallLabel(label),
                ToolTip = tooltip,
                MinHeight = 44,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Icône + libellé des petits boutons de navigation.</summary>
        private static UIElement NavContent(string icon, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Icons.Make(icon, 9, Chrome.Ink) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 4, 0);
                row.Children.Add(glyph);
            }
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        /// <summary>Deux petits boutons empilés (Précédente/Suivante…).</summary>
        private static StackPanel StackedPair(Button top, Button bottom)
        {
            top.Margin = new Thickness(0, 0, 6, 1);
            bottom.Margin = new Thickness(0, 1, 6, 0);
            top.HorizontalAlignment = HorizontalAlignment.Stretch;
            bottom.HorizontalAlignment = HorizontalAlignment.Stretch;
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(top);
            stack.Children.Add(bottom);
            return stack;
        }

        private Button PaletteButton(string tooltip, bool isForeground)
        {
            var accent = new SolidColorBrush(Color.FromRgb(0x5B, 0x67, 0xD8));
            accent.Freeze();
            var button = new Button
            {
                ToolTip = tooltip,
                Width = 34,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make(isForeground ? "palette" : "highlighter-line", 14, accent)
            };
            var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = button };
            button.ContextMenu = menu;
            button.Click += delegate
            {
                BuildPaletteMenu(menu, isForeground); // custom colors live per project
                menu.IsOpen = true;
            };
            return button;
        }

        /// <summary>Rebuilt at each opening: the project's custom colors first,
        /// then « Nouvelle couleur… », then the standard palette.</summary>
        private void BuildPaletteMenu(ContextMenu menu, bool isForeground)
        {
            menu.Items.Clear();
            AddColorEntry(menu, isForeground ? "Automatique" : "Aucun", null, isForeground);

            if (_project != null && _project.CustomColors.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var hex in _project.CustomColors)
                    AddColorEntry(menu, hex, hex, isForeground);
            }
            var custom = new MenuItem { Header = "Nouvelle couleur…" };
            custom.Click += delegate
            {
                var hex = ColorDialog.Ask(Window.GetWindow(this));
                if (hex == null || _project == null) return;
                if (!_project.CustomColors.Contains(hex))
                {
                    _project.CustomColors.Insert(0, hex);
                    NotifyEdited(); // le projet a changé
                }
                ApplyPaletteColor(hex, isForeground);
            };
            menu.Items.Add(custom);
            menu.Items.Add(new Separator());

            var standard = isForeground
                ? new[] { "#C0392B", "#E67E22", "#C9A227", "#27AE60",
                    "#16A085", "#2980B9", "#5B67D8", "#8E44AD", "#7F8C8D", "#703C2F" }
                : new[] { "#FFF3A3", "#FFD9A8", "#D3F8D3", "#D0E8FF",
                    "#FFD6E7", "#E5D4FF", "#E8E8E8" };
            foreach (var hex in standard)
                AddColorEntry(menu, hex, hex, isForeground);
        }

        private void ApplyPaletteColor(string hex, bool isForeground)
        {
            if (ComposedActive)
            {
                if (isForeground) _composed.ApplyColor(hex);
                else _composed.ApplyHighlight(hex);
                _composed.Focus();
                return;
            }
            if (isForeground)
                _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
                    hex == null ? (Brush)Chrome.PaperInk : new SolidColorBrush(FlowConverter.ParseColor(hex)));
            else
                _box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                    hex == null ? null : new SolidColorBrush(FlowConverter.ParseColor(hex)));
            AfterFormat();
        }

        private void AddColorEntry(ContextMenu menu, string label, string hex, bool isForeground)
        {
            var entry = new MenuItem { Header = label };
            if (hex != null)
                entry.Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1)
                };
            entry.Click += delegate { ApplyPaletteColor(hex, isForeground); };
            menu.Items.Add(entry);
        }

        private void BuildSearchBar()
        {
            _searchBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 5, 8, 5),
                Visibility = Visibility.Collapsed
            };
            SetDock(_searchBar, Dock.Top);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(Label("Rechercher :"));
            _searchBox = new TextBox { Width = 160, Margin = new Thickness(4, 0, 10, 0) };
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
            };
            panel.Children.Add(_searchBox);

            panel.Children.Add(Label("Remplacer :"));
            _replaceBox = new TextBox { Width = 160, Margin = new Thickness(4, 0, 10, 0) };
            panel.Children.Add(_replaceBox);

            _caseCheck = new CheckBox
            {
                Content = "Respecter la casse",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 0, 10, 0)
            };
            panel.Children.Add(_caseCheck);

            _wholeWordCheck = new CheckBox
            {
                Content = "Mot entier",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 0, 10, 0)
            };
            panel.Children.Add(_wholeWordCheck);

            panel.Children.Add(SmallButton("Suivant", FindNext));
            panel.Children.Add(SmallButton("Remplacer", ReplaceCurrent));
            panel.Children.Add(SmallButton("Tout remplacer", ReplaceAll));

            _searchInfo = new TextBlock
            {
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            panel.Children.Add(_searchInfo);

            _searchBar.Child = panel;
            _searchBar.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { HideSearch(); e.Handled = true; }
            };
            Children.Add(_searchBar);
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Button SmallButton(string label, Action onClick)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            button.Click += delegate { onClick(); };
            return button;
        }

        private void BuildNotesBar()
        {
            _notesBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 8, 24, 8),
                Visibility = Visibility.Collapsed,
                MaxHeight = 180
            };
            SetDock(_notesBar, Dock.Bottom);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Notes de bas de page",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _notesList = new StackPanel();
            panel.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 130,
                Content = _notesList
            });
            _notesBar.Child = panel;
            Children.Add(_notesBar);
        }

        // ============================================================= révision

        /// <summary>Onglet « Révision » : annoter la sélection, naviguer entre
        /// les annotations, les masquer. En classique, chaque bulle porte son
        /// commentaire et ses commandes ; le panneau du bas ne sert qu'en
        /// Composition (qui n'a pas de bulles).</summary>
        private UIElement BuildRevisionTab()
        {
            // Deux SECTIONS séparées d'un filet vertical (batch 28) : les
            // ANNOTATIONS, puis la RÉVISION ORTHOTYPO.
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 3, 8, 3),
                MinHeight = 52
            };

            // ---- Annotations -------------------------------------------------
            var annotate = TallButton("add-annotation", "Annoter la sélection",
                "Ancre un commentaire de révision au passage sélectionné "
                + "(teinte or à l'écran, jamais imprimée)");
            annotate.Click += delegate { CreateAnnotation(); };
            panel.Children.Add(annotate);

            var previous = new Button
            {
                Content = NavContent("previous", "Précédente"),
                ToolTip = "Aller à l'annotation précédente",
                Padding = new Thickness(8, 1, 8, 1),
                Focusable = false
            };
            previous.Click += delegate { NavigateAnnotation(-1); };
            var next = new Button
            {
                Content = NavContent("next", "Suivante"),
                ToolTip = "Aller à l'annotation suivante",
                Padding = new Thickness(8, 1, 8, 1),
                Focusable = false
            };
            next.Click += delegate { NavigateAnnotation(1); };
            panel.Children.Add(StackedPair(previous, next));

            _annVisibleBtn = TallToggle("Afficher les notes",
                "Affiche ou masque les annotations (teintes et bulles) — "
                + "elles restent dans le projet");
            _annVisibleBtn.IsChecked = Settings.AppSettings.ShowAnnotations;
            _annVisibleBtn.Click += delegate
            {
                Settings.AppSettings.ShowAnnotations = _annVisibleBtn.IsChecked == true;
                Settings.AppSettings.Save();
                ApplyAnnotationVisibility();
            };
            panel.Children.Add(_annVisibleBtn);

            panel.Children.Add(VerticalRuleTall());

            // ---- Révision OrthoTypo -----------------------------------------
            var proofToggle = TallToggle("Vérifier",
                "Vérification continue du texte — répétitions et orthographe "
                + "aujourd'hui, grammaire au prochain batch");
            proofToggle.IsChecked = Settings.AppSettings.ProofEnabled;
            proofToggle.Click += delegate
            {
                Settings.AppSettings.ProofEnabled = proofToggle.IsChecked == true;
                Settings.AppSettings.Save();
                RunCheck();
            };
            panel.Children.Add(proofToggle);

            var previousFinding = new Button
            {
                Content = NavContent("previous", "Signalement"),
                ToolTip = "Aller au signalement de correction précédent",
                Padding = new Thickness(8, 1, 8, 1),
                Focusable = false
            };
            previousFinding.Click += delegate { NavigateFinding(-1); };
            var nextFinding = new Button
            {
                Content = NavContent("next", "Signalement"),
                ToolTip = "Aller au signalement de correction suivant",
                Padding = new Thickness(8, 1, 8, 1),
                Focusable = false
            };
            nextFinding.Click += delegate { NavigateFinding(1); };
            panel.Children.Add(StackedPair(previousFinding, nextFinding));

            _corrDetailsBtn = TallToggle("Détails de correction",
                "Le panneau des signalements, à droite — il remplace les "
                + "détails du chapitre tant qu'il est ouvert");
            _corrDetailsBtn.IsChecked = Settings.AppSettings.CorrectionPanelVisible;
            _corrDetailsBtn.Click += delegate
            {
                Settings.AppSettings.CorrectionPanelVisible =
                    _corrDetailsBtn.IsChecked == true;
                Settings.AppSettings.Save();
                var handler = CorrectionPanelToggled;
                if (handler != null) handler();
            };
            panel.Children.Add(_corrDetailsBtn);

            var noProof = TallButton("Ne pas corriger",
                "Soustrait le passage sélectionné aux correcteurs "
                + "(noms inventés, langues fictives, citations étrangères) "
                + "— re-cliquer pour l'y rendre");
            noProof.Click += delegate
            {
                // Le gel du classique (batch 26) : la commande vit dans les
                // pages composées, la seule surface d'édition supportée.
                if (!ComposedActive || !_composed.ToggleNoProofSelection())
                    MessageBox.Show(Window.GetWindow(this),
                        ComposedActive
                            ? "Sélectionnez d'abord le passage à soustraire."
                            : "« Ne pas corriger » s'applique dans les pages composées.",
                        "Révision", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            panel.Children.Add(noProof);
            return panel;
        }

        // ============================================================ correction

        /// <summary>Le panneau Correction, sur le modèle du panneau
        /// Annotations : liste des signalements, extrait cliquable,
        /// suggestions en un clic, filtres par catégorie. Composé seulement
        /// (le classique est gelé, batch 26).</summary>
        /// <summary>Le PANNEAU de correction — à DROITE depuis le batch 28
        /// (hébergé par la coquille à la place de l'inspecteur, bascule
        /// « Détails de correction » du ruban Révision) : pleine hauteur,
        /// filtres par catégorie empilés en tête, liste défilante.</summary>
        private void BuildCorrectionBar()
        {
            _corrBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var panel = new DockPanel();
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = "Correction",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var filters = new WrapPanel();
            AddCorrectionFilter(filters, Correction.FindingCategory.Spelling, "Orthographe");
            AddCorrectionFilter(filters, Correction.FindingCategory.Grammar, "Grammaire");
            AddCorrectionFilter(filters, Correction.FindingCategory.Typography, "Typographie");
            AddCorrectionFilter(filters, Correction.FindingCategory.Style, "Style");
            head.Children.Add(filters);
            panel.Children.Add(head);
            _corrList = new StackPanel();
            panel.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _corrList
            });
            _corrBar.Child = panel;
            // Plus AUCUN panneau du bas : la coquille héberge _corrBar.
        }

        private void AddCorrectionFilter(Panel host,
            Correction.FindingCategory category, string label)
        {
            var chip = new ToggleButton
            {
                Content = label,
                IsChecked = true,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(7, 1, 7, 1),
                FontSize = 11,
                Focusable = false,
                ToolTip = "Afficher/masquer cette catégorie dans la liste"
            };
            chip.Click += delegate { RebuildCorrectionPanel(); };
            _corrFilters[category] = chip;
            host.Children.Add(chip);
        }

        /// <summary>La connaissance des vérificateurs a changé hors édition
        /// (dictionnaire personnel retouché dans les Préférences) : cache
        /// oublié, passe relancée.</summary>
        public void RefreshProofing()
        {
            if (_spellChecker != null) _spellChecker.InvalidateLearned();
            _checkHost.InvalidateCache();
            RunCheck();
        }

        private void ScheduleCheck()
        {
            if (_checkTimer == null || _item == null || !ComposedActive) return;
            _checkTimer.Stop();
            _checkTimer.Start();
        }

        /// <summary>La passe de correction complète : pilote → ondulés →
        /// panneau. Synchrone sur le fil UI, coût mesuré 46 ms / 50 000 mots
        /// (C5) — bien en deçà du debounce de 600 ms qui l'appelle.</summary>
        private void RunCheck()
        {
            if (_checkTimer != null) _checkTimer.Stop();
            if (_item == null || !ComposedActive
                || !Settings.AppSettings.ProofEnabled)
            {
                _findings = new List<Correction.Finding>();
                if (_composed != null && _composed.HasItem) _composed.SetFindings(null);
                RebuildCorrectionPanel();
                return;
            }
            _findings = _checkHost.Run(_item.Document, _styles);
            _composed.SetFindings(_findings);
            RebuildCorrectionPanel();
            // Les bulles suivent la recomposition (leurs lignes ont pu bouger
            // sous la frappe) — même debounce, jamais pendant la saisie en
            // bulle (garde anti-vol de focus).
            RebuildAnnotationsPanel();
        }

        private void RebuildCorrectionPanel()
        {
            if (_corrList == null) return;
            _corrList.Children.Clear();
            var visible = new List<Correction.Finding>();
            foreach (var finding in _findings)
            {
                ToggleButton chip;
                if (_corrFilters.TryGetValue(finding.Category, out chip)
                    && chip.IsChecked != true) continue;
                visible.Add(finding);
            }
            // La VISIBILITÉ du panneau appartient à la coquille (batch 28) ;
            // ici on ne gère que son CONTENU — vide inclus.
            if (visible.Count == 0)
            {
                _corrList.Children.Add(new TextBlock
                {
                    Text = !Settings.AppSettings.ProofEnabled
                        ? "La vérification est désactivée (bouton « Vérifier », onglet Révision)."
                        : !ComposedActive
                            ? "La correction vit dans les pages composées."
                            : "Aucun signalement — tout est propre.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }
            // Jamais de troncature SILENCIEUSE : la queue est annoncée.
            const int cap = 150;
            for (var i = 0; i < visible.Count && i < cap; i++)
                _corrList.Children.Add(BuildFindingRow(visible[i]));
            if (visible.Count > cap)
                _corrList.Children.Add(new TextBlock
                {
                    Text = "… et " + (visible.Count - cap) + " autres signalements"
                        + " — naviguer avec ◀ ▶ (Révision), la liste suit les corrections",
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    Margin = new Thickness(14, 4, 0, 2)
                });
        }

        /// <summary>Une fiche de signalement du panneau de droite (batch 28) :
        /// pastille + mot cliquable, message enroulé, puis suggestions et
        /// « Ignorer » — la colonne est étroite, tout s'empile.</summary>
        private UIElement BuildFindingRow(Correction.Finding finding)
        {
            var row = new StackPanel { Margin = new Thickness(0, 3, 0, 6) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = ComposedRenderer.FindingPen(finding.Category).Brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 6, 0)
            });
            var excerpt = new TextBlock
            {
                Text = "« " + (finding.Word.Length > 0 ? finding.Word : "…") + " »",
                Foreground = Chrome.Ink,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = Cursors.Hand,
                ToolTip = "Aller au passage signalé"
            };
            var goRef = finding;
            excerpt.MouseLeftButtonDown += delegate { _composed.GoToFinding(goRef); };
            title.Children.Add(excerpt);
            row.Children.Add(title);

            row.Children.Add(new TextBlock
            {
                Text = finding.Message,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 1, 0, 2)
            });

            var buttons = new WrapPanel { Margin = new Thickness(14, 0, 0, 0) };
            // Suggestions à la demande (jamais pendant la passe) — le cache
            // du vérificateur rend la reconstruction du panneau indolore.
            var suggestions = _spellChecker != null && finding.CheckerId == "spelling"
                ? _spellChecker.Suggestions(finding.Word)
                : finding.Suggestions;
            for (var i = 0; i < suggestions.Count && i < 3; i++)
            {
                var suggestion = suggestions[i];
                var findingRef = finding;
                var apply = SmallButton(suggestion,
                    delegate { _composed.ApplySuggestion(findingRef, suggestion); });
                apply.FontSize = 11;
                apply.Margin = new Thickness(0, 0, 4, 2);
                apply.ToolTip = "Remplacer par « " + suggestion + " »";
                buttons.Children.Add(apply);
            }
            var ignoreRef = finding;
            var ignore = SmallButton("Ignorer", delegate
            {
                if (ignoreRef.Word.Length > 0)
                {
                    _checkHost.IgnoreInProject(ignoreRef.Word);
                    NotifyEdited(); // la liste du projet est persistée
                }
                else _checkHost.IgnoreHere(ignoreRef);
                RunCheck();
            });
            ignore.FontSize = 11;
            ignore.Margin = new Thickness(0, 0, 4, 2);
            ignore.ToolTip = finding.Word.Length > 0
                ? "Ne plus signaler « " + finding.Word + " » dans ce projet"
                : "Taire ce signalement (session)";
            buttons.Children.Add(ignore);
            row.Children.Add(buttons);
            return row;
        }

        /// <summary>Signalement suivant/précédent depuis le caret composé —
        /// boucle en bout de course, comme la navigation d'annotations.</summary>
        private void NavigateFinding(int direction)
        {
            if (_findings.Count == 0 || !ComposedActive) return;
            int paragraph, offset;
            _composed.CaretLocation(out paragraph, out offset);
            Correction.Finding target = null;
            if (direction > 0)
            {
                foreach (var finding in _findings)
                    if (finding.ParagraphIndex > paragraph
                        || (finding.ParagraphIndex == paragraph && finding.Start > offset))
                    { target = finding; break; }
                if (target == null) target = _findings[0];
            }
            else
            {
                for (var i = _findings.Count - 1; i >= 0; i--)
                {
                    var finding = _findings[i];
                    if (finding.ParagraphIndex < paragraph
                        || (finding.ParagraphIndex == paragraph && finding.End < offset))
                    { target = finding; break; }
                }
                if (target == null) target = _findings[_findings.Count - 1];
            }
            _composed.GoToFinding(target);
        }

        /// <summary>Ids d'annotations dans l'ordre du texte — pivot en mode
        /// Composition (toujours vivant), FlowDocument en classique (le pivot
        /// n'y est à jour qu'au Commit).</summary>
        private List<string> AnnotationOrderLive()
        {
            if (_item == null) return new List<string>();
            if (ComposedActive) return _item.Document.AnnotationOrder(false);
            var order = new List<string>();
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                CollectAnnotationIds(paragraph.Inlines, order);
            return order;
        }

        private void CollectAnnotationIds(InlineCollection inlines, List<string> order)
        {
            foreach (var inline in inlines)
            {
                var span = inline as Span;
                if (span != null) { CollectAnnotationIds(span.Inlines, order); continue; }
                var id = AnnotationIdOf(inline as Run);
                if (id != null && _item.Document.FindAnnotation(id) != null
                    && !order.Contains(id))
                    order.Add(id);
            }
        }

        private static string AnnotationIdOf(Run run)
        {
            if (run == null) return null;
            string id;
            double? tracking;
            bool noProof;
            FlowConverter.ParseRunTag(run.Tag, out id, out tracking, out noProof);
            return id;
        }

        /// <summary>Le passage annoté tel qu'affiché (extrait du panneau).</summary>
        private string AnnotatedTextLive(string id)
        {
            if (ComposedActive) return _item.Document.AnnotatedText(id);
            var sb = new System.Text.StringBuilder();
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                AppendAnnotatedText(paragraph.Inlines, id, sb);
            return sb.ToString();
        }

        private void AppendAnnotatedText(InlineCollection inlines, string id,
            System.Text.StringBuilder sb)
        {
            foreach (var inline in inlines)
            {
                var span = inline as Span;
                if (span != null) { AppendAnnotatedText(span.Inlines, id, sb); continue; }
                var run = inline as Run;
                if (run != null && AnnotationIdOf(run) == id) sb.Append(run.Text);
            }
        }

        /// <summary>Annoter la sélection : ancre un commentaire neuf au passage
        /// et ouvre son champ dans le panneau.</summary>
        public void CreateAnnotation()
        {
            if (_item == null) return;
            // Annoter en mode masqué réaffiche les annotations (façon Word) :
            // on veut voir naître la bulle qu'on crée.
            if (!Settings.AppSettings.ShowAnnotations)
            {
                Settings.AppSettings.ShowAnnotations = true;
                Settings.AppSettings.Save();
                if (_annVisibleBtn != null) _annVisibleBtn.IsChecked = true;
                ApplyAnnotationVisibility();
            }
            var annotation = new Annotation
            {
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            var anchored = ComposedActive
                ? _composed.AnnotateSelection(annotation.Id)
                : AnnotateClassicSelection(annotation.Id);
            if (!anchored)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Sélectionnez d'abord le passage à annoter.",
                    "Révision", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _item.Document.Annotations.Add(annotation);
            NotifyEdited();
            RebuildAnnotationsPanel();
            FocusAnnotation(annotation.Id);
        }

        /// <summary>Mode classique : pose le fond cosmétique (ce qui découpe les
        /// runs aux bornes de la sélection), puis étiquette les runs couverts.</summary>
        private bool AnnotateClassicSelection(string id)
        {
            var selection = _box.Selection;
            if (selection.IsEmpty) return false;
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, Chrome.AnnotationTint);
            var pointer = selection.Start;
            while (pointer != null && pointer.CompareTo(selection.End) < 0)
            {
                var run = pointer.Parent as Run;
                if (run != null && run.ContentStart.CompareTo(selection.Start) >= 0)
                {
                    // L'approche et « ne pas corriger » déjà portés par le Tag
                    // survivent à l'ancrage (mini-format à segments).
                    string previousId;
                    double? tracking;
                    bool noProof;
                    FlowConverter.ParseRunTag(run.Tag, out previousId, out tracking, out noProof);
                    run.Tag = FlowConverter.ComposeRunTag(id, tracking, noProof);
                    pointer = run.ElementEnd;
                    continue;
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                if (pointer == null) break;
            }
            return true;
        }

        /// <summary>Navigation ruban : va à l'annotation suivante/précédente
        /// après celle du caret (ou la première/dernière).</summary>
        private void NavigateAnnotation(int direction)
        {
            var order = AnnotationOrderLive();
            if (order.Count == 0) return;
            var current = ComposedActive ? _composed.AnnotationAtCaret() : ClassicAnnotationAtCaret();
            var index = current == null ? -1 : order.IndexOf(current);
            index = index < 0
                ? (direction > 0 ? 0 : order.Count - 1)
                : (index + direction + order.Count) % order.Count;
            GoToAnnotation(order[index]);
        }

        private string ClassicAnnotationAtCaret()
        {
            var run = _box.CaretPosition.Parent as Run;
            var id = AnnotationIdOf(run);
            if (id != null) return id;
            var backward = _box.CaretPosition.GetNextInsertionPosition(LogicalDirection.Backward);
            return backward == null ? null : AnnotationIdOf(backward.Parent as Run);
        }

        /// <summary>Sélectionne le passage d'une annotation et l'amène à l'écran.</summary>
        public void GoToAnnotation(string id)
        {
            if (ComposedActive)
            {
                _composed.GoToAnnotation(id);
                _composed.Focus();
                return;
            }
            Run first = null, last = null;
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                FindAnnotationRuns(paragraph.Inlines, id, ref first, ref last);
            if (first == null) return;
            _box.Selection.Select(first.ContentStart, last.ContentEnd);
            _box.Focus(); // EnsureCaretVisible suit le SelectionChanged
        }

        private void FindAnnotationRuns(InlineCollection inlines, string id,
            ref Run first, ref Run last)
        {
            foreach (var inline in inlines)
            {
                var span = inline as Span;
                if (span != null) { FindAnnotationRuns(span.Inlines, id, ref first, ref last); continue; }
                var run = inline as Run;
                if (run == null || AnnotationIdOf(run) != id) continue;
                if (first == null) first = run;
                last = run;
            }
        }

        /// <summary>Résout/rouvre : la teinte s'éteint ou revient, l'ancre reste.</summary>
        private void ToggleAnnotationResolved(Annotation annotation)
        {
            annotation.Resolved = !annotation.Resolved;
            if (ComposedActive) _composed.RefreshAnnotation(annotation.Id);
            else RetintClassicAnnotation(annotation.Id, !annotation.Resolved);
            NotifyEdited();
            RebuildAnnotationsPanel();
        }

        /// <summary>Supprime l'annotation : commentaire ET ancres.</summary>
        private void DeleteAnnotation(Annotation annotation)
        {
            if (_activeBubbleId == annotation.Id) _activeBubbleId = null;
            _item.Document.Annotations.Remove(annotation);
            if (ComposedActive) _composed.ClearAnnotation(annotation.Id);
            else ClearClassicAnnotation(annotation.Id);
            NotifyEdited();
            RebuildAnnotationsPanel();
        }

        private void RetintClassicAnnotation(string id, bool tint)
        {
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                RetintRuns(paragraph.Inlines, id, tint, false);
        }

        private void ClearClassicAnnotation(string id)
        {
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                RetintRuns(paragraph.Inlines, id, false, true);
        }

        private void RetintRuns(InlineCollection inlines, string id, bool tint, bool clearTag)
        {
            foreach (var inline in inlines)
            {
                var span = inline as Span;
                if (span != null) { RetintRuns(span.Inlines, id, tint, clearTag); continue; }
                var run = inline as Run;
                if (run == null || AnnotationIdOf(run) != id) continue;
                // Le fond cosmétique seulement — un vrai surlignage (opaque)
                // n'est jamais touché.
                var background = run.Background as SolidColorBrush;
                var cosmetic = background == null || background.Color.A < 0xFF;
                if (tint && cosmetic) run.Background = Chrome.AnnotationTint;
                else if (!tint && cosmetic) run.Background = null;
                if (clearTag)
                {
                    // L'ancre part, l'approche et « ne pas corriger » restent.
                    string previousId;
                    double? tracking;
                    bool noProof;
                    FlowConverter.ParseRunTag(run.Tag, out previousId, out tracking, out noProof);
                    run.Tag = FlowConverter.ComposeRunTag(null, tracking, noProof);
                }
            }
        }

        /// <summary>Applique le réglage « annotations visibles » : teintes du
        /// classique (fond cosmétique, jamais persisté), bulles, panneau — et
        /// recomposition en mode Composition (la teinte y vient du moteur).</summary>
        private void ApplyAnnotationVisibility()
        {
            if (_item == null) return;
            if (!ComposedActive)
            {
                var wasLoading = _loading;
                _loading = true; // reteinte cosmétique : le projet reste propre
                try
                {
                    foreach (var annotation in _item.Document.Annotations)
                        RetintClassicAnnotation(annotation.Id,
                            Settings.AppSettings.ShowAnnotations && !annotation.Resolved);
                }
                finally { _loading = wasLoading; }
            }
            else _composed.RefreshComposition();
            RebuildAnnotationsPanel();
        }

        /// <summary>Reprogramme les bulles d'annotation (le panneau du bas a
        /// disparu au B.4 : l'édition vit dans les bulles, deux surfaces).</summary>
        public void RebuildAnnotationsPanel()
        {
            // Depuis les bulles portées (B.4, batch 26), le panneau Révision
            // du bas a entièrement disparu : les annotations s'éditent dans
            // leurs bulles sur les DEUX surfaces. Cette méthode — appelée par
            // tous les chemins historiques — reprogramme les bulles après le
            // layout (la géométrie des tranches/pages doit être posée).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(RebuildAnnotationBubbles));
        }

        // -------------------------------------------------- bulles façon Word

        // Bulle « dépliée » : son commentaire s'édite sur place, avec les
        // commandes Résoudre/Supprimer — les autres n'affichent qu'un aperçu.
        private string _activeBubbleId;
        private bool _focusBubbleRequested; // création : champ prêt à la frappe
        private TextBox _activeBubbleEditor;

        private void RebuildAnnotationBubbles()
        {
            RebuildAnnotationBubbles(false);
        }

        /// <summary>Reconstruit les bulles de commentaire à droite des pages
        /// (mode classique). Chaque bulle est posée à la hauteur de son
        /// passage (coordonnées boîte → tranche du miroir), empilée sans
        /// chevauchement, reliée à la page par un filet or. force : passe
        /// outre la garde anti-vol de focus (dépliage/repli volontaire).</summary>
        private void RebuildAnnotationBubbles(bool force)
        {
            if (_bubbleLayer == null) return;
            // La couche active : celle du composé (bulles portées, B.4 batch
            // 26) ou celle du miroir classique.
            var layer = ComposedActive && _composed != null && _composed.HasItem
                ? _composed.AnnotationBubbleLayer : _bubbleLayer;
            // Ne pas voler le focus d'une bulle en cours de frappe.
            if (!force)
                foreach (var child in layer.Children)
                {
                    var host = child as Border;
                    if (host == null) continue;
                    var panel = host.Child as StackPanel;
                    if (panel == null) continue;
                    foreach (var inner in panel.Children)
                    {
                        var focused = inner as TextBox;
                        if (focused != null && focused.IsKeyboardFocused) return;
                    }
                }
            // Les DEUX couches se vident : une bascule de surface ne laisse
            // pas de bulles orphelines derrière elle.
            _bubbleLayer.Children.Clear();
            _bubbleLayer.Width = 0;
            if (_composed != null)
            {
                _composed.AnnotationBubbleLayer.Children.Clear();
                _composed.AnnotationBubbleLayer.Width = 0;
            }
            _activeBubbleEditor = null;
            if (_item == null || _calm
                || !Settings.AppSettings.ShowAnnotations) return;
            var order = AnnotationOrderLive();
            if (order.Count == 0) return;

            const double bubbleWidth = 190;
            var gold = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
            double pageRight, pageTop;
            if (ComposedActive)
            {
                pageRight = _composed.PagesRightX();
                pageTop = 0; // AnnotationAnchorY parle déjà en colonne
                if (pageRight <= 0) return;
            }
            else
                try
                {
                    // TranslatePoint rend l'origine POST-marge du _page : les
                    // cadres du miroir commencent exactement là.
                    var origin = _page.TranslatePoint(new Point(0, 0), _surface);
                    pageRight = origin.X + _pageSetup.PageWidthPx;
                    pageTop = origin.Y;
                }
                catch { return; }

            var lastBottom = 0.0;
            foreach (var id in order)
            {
                var annotation = _item.Document.FindAnnotation(id);
                if (annotation == null) continue;
                double y;
                if (ComposedActive)
                {
                    y = _composed.AnnotationAnchorY(id);
                    if (y < 0) continue;
                }
                else
                {
                    Run first = null, last = null;
                    foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                        FindAnnotationRuns(paragraph.Inlines, id, ref first, ref last);
                    if (first == null) continue;
                    Rect anchor;
                    try { anchor = first.ContentStart.GetCharacterRect(LogicalDirection.Forward); }
                    catch { continue; }
                    if (anchor.IsEmpty) continue;

                    // Coordonnées boîte → miroir : la tranche qui porte la ligne.
                    var slice = 0;
                    for (var k = 0; k < _sliceTops.Count; k++)
                        if (anchor.Top >= _sliceTops[k] - 0.5) slice = k;
                    var topMargin = _pageSetup.MarginTopMm * PageSetup.PxPerMm;
                    var pageHeight = _pageSetup.PageHeightMm * PageSetup.PxPerMm;
                    var frameTop = slice * (pageHeight + PageGap);
                    y = pageTop + frameTop + topMargin
                        + (anchor.Top - (_sliceTops.Count > slice ? _sliceTops[slice] : 0));
                }
                y = Math.Max(y, lastBottom + 6);

                var bubble = BuildAnnotationBubble(annotation, gold, bubbleWidth, layer);
                Canvas.SetLeft(bubble, pageRight + 16);
                Canvas.SetTop(bubble, y);
                // Filet de liaison, du bord de page à la bulle.
                var link = new System.Windows.Shapes.Line
                {
                    X1 = pageRight - 2,
                    Y1 = y + 12,
                    X2 = pageRight + 16,
                    Y2 = y + 12,
                    Stroke = gold,
                    StrokeThickness = 1,
                    Opacity = annotation.Resolved ? 0.4 : 0.8
                };
                layer.Children.Add(link);
                layer.Children.Add(bubble);

                bubble.Measure(new Size(bubbleWidth, double.PositiveInfinity));
                lastBottom = y + Math.Max(34, bubble.DesiredSize.Height);
            }
            // La surface s'élargit pour que les bulles comptent dans l'étendue
            // de défilement.
            layer.Width = pageRight + 16 + bubbleWidth + 12;

            // Après création : le champ de la bulle neuve est prêt à la frappe.
            if (_focusBubbleRequested && _activeBubbleEditor != null)
            {
                _focusBubbleRequested = false;
                var editor = _activeBubbleEditor;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
                {
                    editor.Focus();
                    editor.CaretIndex = editor.Text.Length;
                }));
            }
        }

        /// <summary>Une bulle façon Word. Repliée : extrait + aperçu du
        /// commentaire. Dépliée (clic, ou création) : champ d'édition et
        /// commandes Résoudre/Supprimer sur place ; elle se replie quand le
        /// focus la quitte. layer : la couche qui la portera (miroir
        /// classique ou colonne composée — B.4).</summary>
        private Border BuildAnnotationBubble(Annotation annotation, Brush gold,
            double width, Canvas layer)
        {
            var active = annotation.Id == _activeBubbleId;
            var panel = new StackPanel();
            var excerptText = AnnotatedTextLive(annotation.Id).Trim();
            if (excerptText.Length > 36) excerptText = excerptText.Substring(0, 36) + "…";
            var excerpt = new TextBlock
            {
                Text = "« " + excerptText + " »",
                Foreground = Chrome.SoftText,
                FontStyle = FontStyles.Italic,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = Cursors.Hand,
                ToolTip = "Aller au passage"
            };
            excerpt.MouseLeftButtonDown += delegate { GoToAnnotation(annotation.Id); };
            panel.Children.Add(excerpt);

            if (active)
            {
                var comment = new TextBox
                {
                    Text = annotation.Text,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    MinHeight = 36,
                    Margin = new Thickness(0, 4, 0, 0),
                    ToolTip = "Le commentaire de révision"
                };
                comment.TextChanged += delegate
                {
                    if (_loading) return;
                    annotation.Text = comment.Text;
                    NotifyEdited();
                };
                panel.Children.Add(comment);
                _activeBubbleEditor = comment;

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                var resolve = SmallButton(annotation.Resolved ? "Rouvrir" : "Résoudre",
                    delegate { ToggleAnnotationResolved(annotation); });
                resolve.FontSize = 10;
                resolve.ToolTip = annotation.Resolved
                    ? "Réactiver l'annotation (la teinte revient)"
                    : "Marquer comme traitée (la teinte s'éteint, le commentaire reste)";
                buttons.Children.Add(resolve);
                var remove = SmallButton("Supprimer",
                    delegate { DeleteAnnotation(annotation); });
                remove.FontSize = 10;
                remove.ToolTip = "Supprimer le commentaire et son ancre";
                buttons.Children.Add(remove);
                panel.Children.Add(buttons);
            }
            else if (annotation.Text.Trim().Length > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = annotation.Text.Trim(),
                    Foreground = Chrome.Ink,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 58,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            else
                panel.Children.Add(new TextBlock
                {
                    Text = "(cliquer pour commenter)",
                    Foreground = Chrome.SoftText,
                    FontStyle = FontStyles.Italic,
                    FontSize = 10,
                    Margin = new Thickness(0, 3, 0, 0)
                });

            var bubble = new Border
            {
                Width = width,
                Background = Chrome.BarBgLight,
                BorderBrush = gold, // liseré or, épais côté page (façon Word)
                BorderThickness = new Thickness(3, 1, 1, 1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 5, 8, 6),
                Opacity = annotation.Resolved ? (active ? 0.75 : 0.5) : 1.0,
                Cursor = active ? null : Cursors.Hand,
                Child = panel,
                Tag = annotation.Id
            };
            bubble.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_activeBubbleId == annotation.Id) return;
                _activeBubbleId = annotation.Id;
                _focusBubbleRequested = true;
                RebuildAnnotationBubbles(true);
                e.Handled = true;
            };
            bubble.IsKeyboardFocusWithinChanged += delegate
            {
                if (bubble.IsKeyboardFocusWithin || _activeBubbleId != annotation.Id)
                    return;
                // Une bulle détachée par une reconstruction n'est pas un vrai
                // départ de focus — seule la bulle encore affichée se replie.
                if (!layer.Children.Contains(bubble)) return;
                _activeBubbleId = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(delegate { RebuildAnnotationBubbles(true); }));
            };
            return bubble;
        }

        /// <summary>Ouvre le champ de commentaire d'une annotation après sa
        /// création — les bulles viennent d'être programmées
        /// (RebuildAnnotationsPanel) : celle-ci naîtra dépliée, champ
        /// focalisé, sur l'une ou l'autre surface (B.4).</summary>
        private void FocusAnnotation(string id)
        {
            _activeBubbleId = id;
            _focusBubbleRequested = true;
        }

        private void BuildPage()
        {
            _box = new RichTextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Chrome.PaperInk,
                // PIÈGE (élucidé au batch 23) : le caret natif vit dans un
                // AdornerLayer INTERNE au template du TextBox — il est donc
                // reflété par le VisualBrush du miroir, superposé à notre
                // caret maison… jusqu'à une frontière de format (run annoté)
                // où les deux divergent : « deux carets ». Le natif s'éteint,
                // le caret maison fait foi.
                CaretBrush = Brushes.Transparent,
                AcceptsTab = true,
                // The sheet grows with its content; scrolling belongs to the
                // outer viewer so the page keeps its physical size on screen.
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
            };
            // The native selection highlight paints one continuous band across
            // paragraph margins — over our page gaps. We draw it ourselves,
            // line by line, under the text (Word look), and it survives focus
            // moves to the toolbars.
            _box.SelectionBrush = Brushes.Transparent;
            _box.IsInactiveSelectionHighlightEnabled = true;

            _box.TextChanged += delegate
            {
                // _paginating: the engine's own margin pushes must neither dirty
                // the project nor reschedule themselves.
                if (_loading || _paginating) return;
                NotifyEdited();
                ScheduleMarks();
            };
            _box.SelectionChanged += delegate
            {
                SyncToolbar();
                if (!_loading)
                {
                    UpdateClassicCaret(); // our caret — the native one is unmirrored
                    EnsureCaretVisible();
                    RaisePageInfo();
                    ScheduleOverlay(); // redraw the hand-drawn selection
                }
            };
            _box.PreviewMouseLeftButtonDown += OnEditorMouseDown;

            _sheets = new Canvas { IsHitTestVisible = false };
            _pageMarks = new Canvas { IsHitTestVisible = false };
            _classicCaret = new System.Windows.Shapes.Rectangle
            {
                Width = 1.4,
                Fill = Chrome.PaperInk,
                Visibility = Visibility.Collapsed
            };
            _pageMarks.Children.Add(_classicCaret);
            _pageHost = new Grid { Width = new PageSetup().PageWidthPx };
            _pageHost.Children.Add(_sheets); // selection under the text
            _pageHost.Children.Add(_box);
            _pageHost.Children.Add(_pageMarks);

            // Hidden measuring host: rendered (the VisualBrush needs it) but
            // clipped to nothing. A Canvas gives the host free height.
            var measure = new Canvas();
            measure.Children.Add(_pageHost);
            var hiddenClip = new Border
            {
                Width = 0,
                Height = 0,
                ClipToBounds = true,
                Child = measure
            };
            // The hidden editor must never hijack the scroll viewer: its own
            // bring-into-view requests (caret moves, focus) are swallowed —
            // EnsureCaretVisible scrolls to the PAGE carrying the caret.
            hiddenClip.RequestBringIntoView += delegate(object sender, RequestBringIntoViewEventArgs e)
            {
                e.Handled = true;
            };

            _mirror = new StackPanel();
            _page = new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Child = _mirror
            };
            var surface = new Grid();
            surface.Children.Add(hiddenClip);
            surface.Children.Add(_page);
            // Bulles de commentaire (révision) à droite des pages, façon Word.
            _bubbleLayer = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            surface.Children.Add(_bubbleLayer);
            _surface = surface;
            _scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                Content = surface
            };

            _classicBlink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _classicBlink.Tick += delegate
            {
                if (_item == null || !_box.IsKeyboardFocused)
                { _classicCaret.Visibility = Visibility.Collapsed; return; }
                _classicCaret.Visibility = _classicCaret.Visibility == Visibility.Visible
                    ? Visibility.Hidden : Visibility.Visible;
            };
            _classicBlink.Start();
            _scroller.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                // ¶ marks, line numbers and the hand-drawn selection are
                // viewport-limited: fast redraw (no repagination) after moves.
                if ((_showMarks || _pageSetup.LineNumbers || !_box.Selection.IsEmpty)
                    && Math.Abs(e.VerticalChange) > 0.5)
                    ScheduleOverlay();
            };
            _composed = new ComposedView { Visibility = Visibility.Collapsed };
            _composed.ExitRequested += delegate { SetComposition(false); };
            _composed.Edited += delegate { NotifyEdited(); };
            _composed.FindingIgnoreHere += delegate(Correction.Finding finding)
            {
                _checkHost.IgnoreHere(finding);
                RunCheck();
            };
            _composed.FindingIgnoreProject += delegate(Correction.Finding finding)
            {
                _checkHost.IgnoreInProject(finding.Word);
                NotifyEdited(); // la liste d'ignorés du projet est persistée
                RunCheck();
            };
            _composed.LinkClicked += delegate(string title)
            {
                var handler = LinkClicked;
                if (handler != null) handler(title);
            };
            _composed.PageInfoChanged += delegate(int page, int total)
            {
                var handler = PageInfoChanged;
                if (handler != null && _item != null) handler(page, total);
            };
            _composed.SelectionStateChanged += delegate
            {
                SyncTrackingBox();
                SyncToolbarComposed();
            };

            var centerHost = new Grid();
            centerHost.Children.Add(_scroller);
            centerHost.Children.Add(_composed);
            // Règles cm (Ctrl+R) : bandes fixes au bord du viewport, nourries
            // des rectangles de pages à l'écran ; la verticale repart à zéro
            // à chaque page.
            _rulerV = new RulerView(true)
            {
                Width = RulerView.Thickness,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed
            };
            _rulerH = new RulerView(false)
            {
                Height = RulerView.Thickness,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };
            centerHost.Children.Add(_rulerV);
            centerHost.Children.Add(_rulerH);
            _scroller.ScrollChanged += delegate { UpdateRulers(); };
            _composed.ScrollChanged += delegate { UpdateRulers(); };
            Children.Add(centerHost); // last child fills the remaining space

            _marksTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _marksTimer.Tick += delegate { _marksTimer.Stop(); UpdatePagination(); };
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _overlayTimer.Tick += delegate { _overlayTimer.Stop(); RefreshOverlay(); };

            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                e.Handled = true;
                var handler = ZoomStepRequested;
                if (handler != null) handler(e.Delta > 0 ? 10 : -10);
            };
        }

        /// <summary>Zoom factor of the page surface (1.0 = 100 %).</summary>
        public void SetZoom(double factor)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRulers));
            _zoom = Math.Max(0.5, Math.Min(3.0, factor));
            _page.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001
                ? null : new ScaleTransform(_zoom, _zoom);
            if (_composed != null) _composed.SetZoom(_zoom);
            ScheduleOverlay(); // marks & line numbers follow the new viewport
        }

        private void ScheduleMarks()
        {
            _marksTimer.Stop();
            _marksTimer.Start();
        }

        private void ScheduleOverlay()
        {
            _overlayTimer.Stop();
            _overlayTimer.Start();
        }

        /// <summary>The pagination engine. Word model, paragraph granularity:
        /// a paragraph that no longer fits on the current page is pushed to the
        /// next one by a presentation margin (paused undo — see UndoGate), so
        /// every page carries its own top/bottom margins and the sheets are
        /// truly separate. A paragraph taller than a page stretches its page
        /// (line-level splitting is the 4b refinement). One extra verification
        /// pass runs after the relayout; the algorithm is stable because
        /// vertical margins never change line wrapping.</summary>
        /// <summary>The paged mirror's pagination: walks the laid-out lines of
        /// the CONTINUOUS RichTextBox and cuts page slices between them — a
        /// line that no longer fits opens the next page, manual breaks force
        /// one. No document mutation at all: the pages are pure presentation
        /// (VisualBrush slices), so undo/redo stay native and no hole can ever
        /// appear at a page bottom.</summary>
        private void UpdatePagination()
        {
            if (_pageMarks == null || _paginating || _pageHost == null) return;
            _paginating = true;
            try
            {
                var setup = _pageSetup;
                var pageHeight = setup.PageHeightMm * PageSetup.PxPerMm;
                var top = setup.MarginTopMm * PageSetup.PxPerMm;
                var bottom = setup.MarginBottomMm * PageSetup.PxPerMm;
                var contentHeight = pageHeight - top - bottom;
                if (contentHeight < 60) return;

                _sliceTops.Clear();
                _sliceHeights.Clear();
                var sliceTop = top;
                double lastBottom = top;

                if (_item != null && _box.Document != null)
                {
                    try
                    {
                        var line = _box.Document.ContentStart
                            .GetInsertionPosition(LogicalDirection.Forward);
                        var start = line.GetLineStartPosition(0);
                        if (start != null) line = start;
                        var guard = 0;
                        while (line != null && guard++ < 100000)
                        {
                            var rect = line.GetCharacterRect(LogicalDirection.Forward);
                            if (!rect.IsEmpty)
                            {
                                var forced = IsForcedBreakLine(line, rect);
                                if ((rect.Bottom > sliceTop + contentHeight + 0.5 || forced)
                                    && rect.Top > sliceTop + 0.5)
                                {
                                    _sliceTops.Add(sliceTop);
                                    _sliceHeights.Add(Math.Max(8, lastBottom - sliceTop));
                                    sliceTop = rect.Top;
                                }
                                lastBottom = Math.Max(lastBottom, rect.Bottom);
                            }
                            var next = line.GetLineStartPosition(1);
                            if (next == null || next.CompareTo(line) <= 0) break;
                            line = next;
                        }
                    }
                    catch { return; } // layout not ready: the debounce returns
                }
                _sliceTops.Add(sliceTop);
                _sliceHeights.Add(Math.Max(8, Math.Max(lastBottom - sliceTop, 12)));

                _pageHost.Width = setup.PageWidthPx;
                _pageHost.MinHeight = lastBottom + bottom;
                RebuildMirror();
                RefreshOverlay();
                RaisePageInfo();
                // Les bulles d'annotation suivent la nouvelle pagination.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(RebuildAnnotationBubbles));
            }
            finally
            {
                _paginating = false;
            }
        }

        /// <summary>True when this line is the first of a paragraph carrying a
        /// manual page break.</summary>
        private static bool IsForcedBreakLine(TextPointer line, Rect rect)
        {
            var paragraph = line.Paragraph;
            if (paragraph == null || !paragraph.BreakPageBefore) return false;
            var first = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            return !first.IsEmpty && Math.Abs(first.Top - rect.Top) < 1.5;
        }

        /// <summary>Builds/updates the visible page frames, each mirroring its
        /// slice of the hidden continuous surface.</summary>
        private void RebuildMirror()
        {
            var setup = _pageSetup;
            var pageWidth = setup.PageWidthPx;
            var pageHeight = setup.PageHeightMm * PageSetup.PxPerMm;
            var top = setup.MarginTopMm * PageSetup.PxPerMm;
            var bottom = setup.MarginBottomMm * PageSetup.PxPerMm;
            var left = setup.MarginLeftMm * PageSetup.PxPerMm;
            var right = setup.MarginRightMm * PageSetup.PxPerMm;

            while (_mirror.Children.Count > _sliceTops.Count)
                _mirror.Children.RemoveAt(_mirror.Children.Count - 1);
            while (_mirror.Children.Count < _sliceTops.Count)
                _mirror.Children.Add(BuildMirrorFrame(_mirror.Children.Count));

            for (var k = 0; k < _sliceTops.Count; k++)
            {
                var frame = (Border)_mirror.Children[k];
                frame.Width = pageWidth;
                frame.Height = pageHeight;
                frame.Margin = new Thickness(0, k == 0 ? 0 : PageGap, 0, 0);
                var grid = (Grid)frame.Child;
                var slice = (System.Windows.Shapes.Rectangle)grid.Children[0];
                var guides = (System.Windows.Shapes.Rectangle)grid.Children[1];
                var folio = (TextBlock)grid.Children[2];

                // Marges en miroir : l'hôte compose au petit fond à gauche ;
                // les versos décalent leur tranche pour que le petit fond
                // passe côté reliure (droite). Les overlays vivent DANS
                // l'hôte : ils suivent, et les clics sont relatifs à la
                // tranche — tout reste aligné.
                var isRecto = (k + 1 + FolioOffset) % 2 == 1;
                var shift = isRecto ? 0 : (right - left);
                slice.Width = pageWidth;
                slice.Height = Math.Min(_sliceHeights[k], pageHeight - top - 2);
                slice.Margin = new Thickness(shift, top, 0, 0);
                var brush = slice.Fill as VisualBrush;
                if (brush == null)
                {
                    brush = new VisualBrush(_pageHost)
                    {
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top
                    };
                    slice.Fill = brush;
                }
                brush.Viewbox = new Rect(0, _sliceTops[k], pageWidth,
                    Math.Max(8, slice.Height));

                guides.Visibility = setup.ShowMarginGuides ? Visibility.Visible : Visibility.Collapsed;
                guides.Margin = isRecto
                    ? new Thickness(left, top, right, bottom)
                    : new Thickness(right, top, left, bottom); // verso : petit fond à droite

                folio.Visibility = setup.FooterPageNumbers
                    && (Decor == null || !Decor.SuppressFolio)
                    ? Visibility.Visible : Visibility.Collapsed;
                folio.Text = (k + 1 + FolioOffset).ToString();
                folio.FontFamily = new FontFamily(setup.FooterFont ?? "Times New Roman");
                folio.Margin = new Thickness(0, 0, 0, Math.Max(2, bottom / 2 - 8));
            }
        }

        private Border BuildMirrorFrame(int index)
        {
            var slice = new System.Windows.Shapes.Rectangle
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                SnapsToDevicePixels = true,
                // The mirror frame is the visible writing surface; the hidden
                // RichTextBox's own IBeam never shows through the VisualBrush.
                Cursor = Cursors.IBeam
            };
            var guides = new System.Windows.Shapes.Rectangle
            {
                // Cyan continu légèrement transparent, façon PAO — même
                // pinceau que la Composition (ComposedRenderer.MarginPen).
                Stroke = ComposedRenderer.MarginPen.Brush,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            var folio = new TextBlock
            {
                FontSize = 11,
                Foreground = Chrome.PaperSoftInk,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            var grid = new Grid();
            grid.Children.Add(slice);
            grid.Children.Add(guides);
            grid.Children.Add(folio);
            var frame = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                ClipToBounds = true, // les tranches décalées (miroir) ne débordent pas
                Child = grid
            };
            WireMirrorInput(frame, slice);
            return frame;
        }

        /// <summary>Forwards mouse gestures from a page frame to the hidden
        /// continuous editor (caret, drag selection, double-click word).</summary>
        private void WireMirrorInput(Border frame, System.Windows.Shapes.Rectangle slice)
        {
            frame.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_item == null) return;
                var index = _mirror.Children.IndexOf(frame);
                if (index < 0 || index >= _sliceTops.Count) return;
                var position = SourcePosition(e.GetPosition(slice), index);
                if (position == null) return;
                _box.Focus();
                // Une bulle d'annotation restée dépliée se replie au clic dans
                // la page (elle n'a pas toujours le focus à perdre).
                if (_activeBubbleId != null)
                {
                    _activeBubbleId = null;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(delegate { RebuildAnnotationBubbles(true); }));
                }
                if (e.ClickCount == 2)
                {
                    // Façon Word (et Composition) : PAS de capture après le
                    // double-clic — sinon le micro-mouvement du relâchement
                    // remplaçait le mot par une sélection ancre→pointeur.
                    SelectWordAtPointer(position);
                    _mirrorAnchor = _box.Selection.Start;
                    RedrawSelectionNow();
                    e.Handled = true;
                    return;
                }
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    _box.Selection.Select(_mirrorAnchor ?? _box.Selection.Start, position);
                }
                else
                {
                    _mirrorAnchor = position;
                    _box.Selection.Select(position, position);
                }
                RedrawSelectionNow();
                frame.CaptureMouse();
                e.Handled = true;
            };
            frame.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!frame.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                var index = _mirror.Children.IndexOf(frame);
                if (index < 0 || index >= _sliceTops.Count) return;
                // Hors du cadre, la position serait projetée LOIN dans le
                // document et chaque mouvement faisait défiler d'un bond
                // (« vitesse folle »). On borne la sélection au cadre, et un
                // défilement RYTHMÉ (une ligne par tic) prend le relais.
                var local = e.GetPosition(slice);
                var sliceHeight = slice.ActualHeight;
                _dragScrollDirection = local.Y < -2 ? -1
                    : local.Y > sliceHeight + 2 ? 1 : 0;
                // Vitesse proportionnelle au dépassement (façon Word) : plus le
                // pointeur s'éloigne du cadre, plus la sélection défile vite.
                _dragScrollOvershoot = _dragScrollDirection < 0
                    ? -local.Y : local.Y - sliceHeight;
                var clamped = new Point(local.X,
                    Math.Max(0, Math.Min(sliceHeight, local.Y)));
                var position = SourcePosition(clamped, index);
                if (position != null && _mirrorAnchor != null)
                {
                    _box.Selection.Select(_mirrorAnchor, position);
                    // Redessin IMMÉDIAT : le debounce de l'overlay ne se
                    // déclenche jamais tant que la souris bouge.
                    RedrawSelectionNow();
                }
                if (_dragScrollDirection != 0)
                {
                    if (_dragScrollTimer == null)
                    {
                        _dragScrollTimer = new DispatcherTimer
                        { Interval = TimeSpan.FromMilliseconds(60) };
                        _dragScrollTimer.Tick += delegate { DragScrollStep(); };
                    }
                    _dragScrollTimer.Start();
                }
                else if (_dragScrollTimer != null) _dragScrollTimer.Stop();
            };
            frame.MouseLeftButtonUp += delegate
            {
                frame.ReleaseMouseCapture();
                if (_dragScrollTimer != null) _dragScrollTimer.Stop();
                _dragScrollDirection = 0;
            };
        }

        private DispatcherTimer _dragScrollTimer;
        private int _dragScrollDirection;
        private double _dragScrollOvershoot; // px au-delà du cadre (vitesse)

        /// <summary>Un tic de défilement de sélection : l'extrémité mobile
        /// avance d'une à cinq lignes selon le dépassement du pointeur —
        /// EnsureCaretVisible suit en douceur.</summary>
        private void DragScrollStep()
        {
            if (_dragScrollDirection == 0 || _mirrorAnchor == null || _item == null)
            {
                if (_dragScrollTimer != null) _dragScrollTimer.Stop();
                return;
            }
            try
            {
                var selection = _box.Selection;
                var moving = selection.Start.CompareTo(_mirrorAnchor) == 0
                    ? selection.End : selection.Start;
                var steps = 1 + Math.Min(4, (int)(_dragScrollOvershoot / 40));
                var next = moving.GetLineStartPosition(_dragScrollDirection * steps)
                    ?? moving.GetLineStartPosition(_dragScrollDirection);
                if (next == null) { _dragScrollTimer.Stop(); return; }
                _box.Selection.Select(_mirrorAnchor, next);
                RedrawSelectionNow();
            }
            catch { _dragScrollTimer.Stop(); }
        }

        /// <summary>Redessine la sélection SEULE, tout de suite (pendant un
        /// cliquer-glisser) : _sheets ne porte que ses rectangles, les marques
        /// et numéros de ligne attendent le debounce de l'overlay.</summary>
        private void RedrawSelectionNow()
        {
            if (_sheets == null || _item == null || ComposedActive) return;
            for (var i = _sheets.Children.Count - 1; i >= 0; i--)
                _sheets.Children.RemoveAt(i);
            UpdateClassicCaret();
            DrawSelection();
        }

        private TextPointer SourcePosition(Point local, int sliceIndex)
        {
            try
            {
                var source = new Point(local.X, _sliceTops[sliceIndex] + local.Y);
                return _box.GetPositionFromPoint(source, true);
            }
            catch { return null; }
        }

        private void SelectWordAtPointer(TextPointer position)
        {
            var start = position;
            var end = position;
            while (true)
            {
                var previous = start.GetNextInsertionPosition(LogicalDirection.Backward);
                if (previous == null) break;
                var range = new TextRange(previous, start);
                // Un pas VIDE est une frontière de run (les annotations
                // scindent les runs) : on la franchit, le mot continue.
                if (range.Text.Length == 0) { start = previous; continue; }
                if (range.Text.Length != 1 || !char.IsLetterOrDigit(range.Text[0])) break;
                start = previous;
            }
            while (true)
            {
                var next = end.GetNextInsertionPosition(LogicalDirection.Forward);
                if (next == null) break;
                var range = new TextRange(end, next);
                if (range.Text.Length == 0) { end = next; continue; }
                if (range.Text.Length != 1 || !char.IsLetterOrDigit(range.Text[0])) break;
                end = next;
            }
            _box.Selection.Select(start, end);
        }

        /// <summary>Our own caret in the mirrored layer — the RichTextBox's
        /// native caret lives in the adorner layer, which VisualBrush does not
        /// reflect.</summary>
        private void UpdateClassicCaret()
        {
            if (_item == null || ComposedActive)
            {
                _classicCaret.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                var rect = _box.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty) { _classicCaret.Visibility = Visibility.Collapsed; return; }
                Canvas.SetLeft(_classicCaret, rect.X);
                Canvas.SetTop(_classicCaret, rect.Top + 1);
                _classicCaret.Height = Math.Max(8, rect.Height - 2);
                _classicCaret.Visibility = _box.IsKeyboardFocused
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        /// <summary>Fast redraw of everything painted around the text: sheets,
        /// margin guides, folios, line numbers, ¶ marks. Never re-measures the
        /// pagination itself.</summary>
        private void RefreshOverlay()
        {
            if (_pageMarks == null || _sliceTops.Count == 0) return;
            for (var i = _sheets.Children.Count - 1; i >= 0; i--) _sheets.Children.RemoveAt(i);
            for (var i = _pageMarks.Children.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(_pageMarks.Children[i], _classicCaret))
                    _pageMarks.Children.RemoveAt(i);
            UpdateClassicCaret();
            DrawSelection(); // under the text, mirrored into the pages
            if (_pageSetup.LineNumbers) DrawLineNumbers();
            if (_showMarks) DrawFormattingMarks();
        }

        /// <summary>Word-style selection: one rectangle per selected line,
        /// drawn beneath the text and inside the page text blocks (line boxes
        /// never live in the inter-page gaps), viewport-limited. Being ours,
        /// it stays visible when focus moves to a toolbar or a tab.</summary>
        private void DrawSelection()
        {
            if (_item == null) return;
            var selection = _box.Selection;
            if (selection == null || selection.IsEmpty) return;
            double viewTop, viewBottom;
            ViewportBounds(out viewTop, out viewBottom);

            var brush = new SolidColorBrush(SystemColors.HighlightColor) { Opacity = 0.45 };
            brush.Freeze();
            var start = selection.Start;
            var end = selection.End;
            try
            {
                var line = start.GetLineStartPosition(0) ?? start;
                // Jump ahead when the selection begins far above the viewport.
                var probe = _box.GetPositionFromPoint(new Point(5, Math.Max(0, viewTop)), true);
                if (probe != null && probe.CompareTo(start) > 0)
                    line = probe.GetLineStartPosition(0) ?? probe;

                var guard = 0;
                while (line != null && guard++ < 3000)
                {
                    if (line.CompareTo(end) > 0) break;
                    var next = line.GetLineStartPosition(1);
                    // Last position ON this line: one insertion position back
                    // from the next line's start (its start itself measures on
                    // the following line and would produce sliver rectangles).
                    var lineEnd = next != null
                        ? (next.GetNextInsertionPosition(LogicalDirection.Backward) ?? next)
                        : end;
                    if (lineEnd.CompareTo(line) < 0) lineEnd = next ?? end;

                    var segmentStart = start.CompareTo(line) > 0 ? start : line;
                    var segmentEnd = end.CompareTo(lineEnd) < 0 ? end : lineEnd;
                    // <=: an empty selected line still shows its newline sliver.
                    if (segmentStart.CompareTo(segmentEnd) <= 0)
                    {
                        var r1 = segmentStart.GetCharacterRect(LogicalDirection.Forward);
                        var r2 = segmentEnd.GetCharacterRect(LogicalDirection.Backward);
                        if (!r1.IsEmpty && !r2.IsEmpty)
                        {
                            if (r1.Top > viewBottom) break;
                            if (r2.Bottom >= viewTop)
                            {
                                // Selection running past the line end shows the
                                // newline, Word-style.
                                var extend = end.CompareTo(lineEnd) >= 0 && next != null ? 5 : 0;
                                var rect = new System.Windows.Shapes.Rectangle
                                {
                                    Width = Math.Max(2, r2.Right - r1.X + extend),
                                    Height = Math.Max(2, Math.Max(r1.Height, r2.Bottom - r1.Top)),
                                    Fill = brush
                                };
                                Canvas.SetLeft(rect, r1.X);
                                Canvas.SetTop(rect, Math.Min(r1.Top, r2.Top));
                                _sheets.Children.Add(rect);
                            }
                        }
                    }
                    if (next == null) break;
                    line = next;
                }
            }
            catch { } // layout raced an edit: the next overlay pass redraws
        }

        /// <summary>Line numbers in the left margin, restarting on each page
        /// (matches the docx export), drawn for the visible pages only.</summary>
        private void DrawLineNumbers()
        {
            if (_item == null) return;
            var setup = _pageSetup;
            var top = setup.MarginTopMm * PageSetup.PxPerMm;
            var bottom = setup.MarginBottomMm * PageSetup.PxPerMm;
            var left = setup.MarginLeftMm * PageSetup.PxPerMm;
            double viewTop, viewBottom;
            ViewportBounds(out viewTop, out viewBottom);

            try
            {
                for (var k = 0; k < _sliceTops.Count; k++)
                {
                    var contentTop = _sliceTops[k];
                    var contentBottom = _sliceTops[k] + _sliceHeights[k];
                    if (contentBottom < viewTop || contentTop > viewBottom) continue;

                    var pointer = _box.GetPositionFromPoint(new Point(left + 2, contentTop + 2), true);
                    if (pointer == null) continue;
                    var line = pointer.GetLineStartPosition(0) ?? pointer;
                    var number = 0;
                    while (line != null)
                    {
                        var rect = line.GetCharacterRect(LogicalDirection.Forward);
                        if (rect.IsEmpty) break;
                        if (rect.Top > contentBottom - 1 || rect.Top > viewBottom) break;
                        number++;
                        if (rect.Bottom >= viewTop && rect.Top >= contentTop - 1)
                        {
                            // Right-aligned column, vertically centered on its
                            // line so counting reads at a glance.
                            var label = new TextBlock
                            {
                                Text = number.ToString(),
                                FontSize = 9,
                                Foreground = Chrome.PaperSoftInk,
                                Width = 26,
                                TextAlignment = TextAlignment.Right
                            };
                            Canvas.SetLeft(label, Math.Max(2, left - 34));
                            Canvas.SetTop(label, rect.Top + Math.Max(0, (rect.Height - 12) / 2));
                            _pageMarks.Children.Add(label);
                        }
                        var next = line.GetLineStartPosition(1);
                        if (next == null || next.CompareTo(line) <= 0) break;
                        line = next;
                    }
                }
            }
            catch { } // layout raced an edit: the next overlay pass redraws
        }

        /// <summary>Visible SOURCE range (hidden-surface coordinates): the
        /// union of the slices whose page frames intersect the viewport.</summary>
        private void ViewportBounds(out double viewTop, out double viewBottom)
        {
            var zoom = Math.Max(0.1, _zoom);
            var pageHeight = _pageSetup.PageHeightMm * PageSetup.PxPerMm;
            var stride = pageHeight + PageGap;
            var offset = (_scroller.VerticalOffset - _page.Margin.Top * zoom) / zoom;
            var first = Math.Max(0, Math.Min(_sliceTops.Count - 1, (int)(offset / stride)));
            var last = Math.Max(first, Math.Min(_sliceTops.Count - 1,
                (int)((offset + _scroller.ViewportHeight / zoom) / stride)));
            if (_sliceTops.Count == 0) { viewTop = 0; viewBottom = 0; return; }
            viewTop = _sliceTops[first] - 20;
            viewBottom = _sliceTops[last] + _sliceHeights[last] + 40;
        }

        /// <summary>The caret's page (1-based) from the slice table.</summary>
        private int CaretPage()
        {
            try
            {
                var rect = _box.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty) return 1;
                var page = 1;
                for (var k = 0; k < _sliceTops.Count; k++)
                    if (rect.Top >= _sliceTops[k] - 0.5) page = k + 1;
                return page;
            }
            catch { }
            return Math.Max(1, _sliceTops.Count);
        }

        private void RaisePageInfo()
        {
            var handler = PageInfoChanged;
            if (handler != null && _item != null)
                handler(CaretPage() + FolioOffset,
                    Math.Max(1, _sliceTops.Count) + FolioOffset);
        }

        // ============================================================= ¶ formatting marks

        /// <summary>Draws the printing characters (¶ end of paragraph, · space,
        /// ° non-breaking space, → tab, ↵ line break), viewport-limited so big
        /// chapters stay fluid.</summary>
        private void DrawFormattingMarks()
        {
            if (!_showMarks || _item == null) return;
            double viewTop, viewBottom;
            ViewportBounds(out viewTop, out viewBottom);

            try
            {
                foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
                {
                    var startRect = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (startRect.IsEmpty || startRect.Top > viewBottom) break;
                    var endRect = paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                    if (endRect.IsEmpty || endRect.Bottom < viewTop) continue;

                    AddMark("¶", endRect.Right + 1, endRect.Top, endRect.Height);
                    DrawInlineMarks(paragraph.Inlines, viewTop, viewBottom);
                }
            }
            catch { } // layout raced an edit: next debounce redraws
        }

        private void DrawInlineMarks(InlineCollection inlines, double viewTop, double viewBottom)
        {
            foreach (var inline in inlines)
            {
                var lineBreak = inline as LineBreak;
                if (lineBreak != null)
                {
                    var rect = lineBreak.ElementStart.GetCharacterRect(LogicalDirection.Forward);
                    if (!rect.IsEmpty && rect.Bottom >= viewTop && rect.Top <= viewBottom)
                        AddMark("↵", rect.Right + 1, rect.Top, rect.Height);
                    continue;
                }
                var run = inline as Run;
                if (run != null)
                {
                    var text = run.Text;
                    if (text.IndexOf(' ') < 0 && text.IndexOf('\u00A0') < 0 && text.IndexOf('\t') < 0)
                        continue;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var c = text[i];
                        if (c != ' ' && c != '\u00A0' && c != '\t') continue;
                        var pointer = run.ContentStart.GetPositionAtOffset(i);
                        if (pointer == null) continue;
                        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
                        if (rect.IsEmpty || rect.Bottom < viewTop) continue;
                        if (rect.Top > viewBottom) return;
                        if (c == ' ')
                            AddMark("·", rect.X + 0.5, rect.Top, rect.Height);
                        else if (c == '\u00A0')
                            AddMark("°", rect.X, rect.Top, rect.Height);
                        else
                            AddMark("→", rect.X + 1, rect.Top, rect.Height);
                    }
                    continue;
                }
                var span = inline as Span;
                if (span != null) DrawInlineMarks(span.Inlines, viewTop, viewBottom);
            }
        }

        private void AddMark(string glyph, double x, double y, double lineHeight)
        {
            var mark = new TextBlock
            {
                Text = glyph,
                FontSize = Math.Max(8, Math.Min(13, lineHeight * 0.62)),
                // Accent ink: clearly visible, clearly not body text.
                Foreground = Chrome.Accent
            };
            Canvas.SetLeft(mark, x);
            Canvas.SetTop(mark, y + lineHeight * 0.12);
            _pageMarks.Children.Add(mark);
        }

        /// <summary>Keeps the caret in view: source coordinates are mapped to
        /// the page frame carrying the caret's slice.</summary>
        private void EnsureCaretVisible()
        {
            if (_scroller == null || _item == null || _sliceTops.Count == 0) return;
            try
            {
                var rect = _box.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty) return;
                var slice = 0;
                for (var k = 0; k < _sliceTops.Count; k++)
                    if (rect.Top >= _sliceTops[k] - 0.5) slice = k;
                var setup = _pageSetup;
                var pageHeight = setup.PageHeightMm * PageSetup.PxPerMm;
                var top = setup.MarginTopMm * PageSetup.PxPerMm;
                var zoom = Math.Max(0.1, _zoom);
                var screenTop = (_page.Margin.Top + slice * (pageHeight + PageGap)
                    + top + (rect.Top - _sliceTops[slice])) * zoom;
                var screenBottom = screenTop + rect.Height * zoom;
                if (screenTop < _scroller.VerticalOffset + 8)
                    _scroller.ScrollToVerticalOffset(Math.Max(0, screenTop - 60));
                else if (screenBottom > _scroller.VerticalOffset + _scroller.ViewportHeight - 8)
                    _scroller.ScrollToVerticalOffset(screenBottom - _scroller.ViewportHeight + 60);
            }
            catch { }
        }

        // ============================================================= page setup

        /// <summary>Applies the project's page setup to the editing surface:
        /// paper width, real margins, optional margin guides, hyphenation.</summary>
        public void ApplyPageSetup(PageSetup setup)
        {
            if (setup != null) _pageSetup = setup;
            ApplyPageVisuals();
            if (ComposedActive) _composed.RefreshComposition();
        }

        private void ApplyPageVisuals()
        {
            var setup = _pageSetup;
            _pageHost.Width = Math.Max(200, setup.PageWidthPx);
            var margins = new Thickness(
                setup.MarginLeftMm * PageSetup.PxPerMm,
                setup.MarginTopMm * PageSetup.PxPerMm,
                setup.MarginRightMm * PageSetup.PxPerMm,
                setup.MarginBottomMm * PageSetup.PxPerMm);
            var flow = _box.Document;
            if (flow != null)
            {
                flow.PagePadding = margins;
                flow.IsHyphenationEnabled = setup.Hyphenation;
                if (setup.Columns > 1)
                {
                    // Honored by print/export; WPF's RichTextBox itself always
                    // renders a single column.
                    flow.ColumnGap = 20;
                    flow.ColumnWidth = Math.Max(60,
                        (setup.ContentWidthPx - (setup.Columns - 1) * 20) / setup.Columns);
                }
                else
                    flow.ColumnWidth = double.PositiveInfinity;

                // The RichTextBox silently coerces PagePadding to (5,0,5,0)
                // during its own layout pass (probed) — the cause of "text
                // glued to the edges". Re-assert once layout settled, then
                // paginate right away: no debounce lag on load or page-setup
                // changes, the flash of unformatted text stays subliminal.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    if (_box.Document != flow) return;
                    flow.PagePadding = margins;
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                        new Action(UpdatePagination));
                }));
            }
            _pageHost.MinHeight = setup.PageHeightMm * PageSetup.PxPerMm;
            SyncPageTab();
        }

        /// <summary>Toggles a manual page break above the caret's paragraph.</summary>
        public void InsertPageBreak()
        {
            if (_item == null) return;
            if (ComposedActive) { _composed.TogglePageBreak(); return; }
            var paragraph = _box.CaretPosition.Paragraph;
            if (paragraph == null) return;
            FlowConverter.MarkPageBreak(paragraph, !paragraph.BreakPageBefore);
            NotifyEdited();
            _box.Focus();
        }

        // ============================================================= item lifecycle

        public void SetStyleSheet(StyleSheet styles)
        {
            _styles = styles;
            _syncing = true;
            _styleCombo.Items.Clear();
            foreach (var style in styles.Styles)
                _styleCombo.Items.Add(new ComboBoxItem
                {
                    // Each entry previews its style's font — nothing else.
                    Content = new TextBlock
                    {
                        Text = style.Name,
                        FontFamily = new FontFamily(style.FontFamily)
                    },
                    Tag = style.Id
                });
            _syncing = false;
        }

        /// <summary>Binds the editor to the open project (image store).</summary>
        public void SetProject(Project project)
        {
            _project = project;
            // « Ignorer dans ce projet » vit et se sauve avec le projet.
            _checkHost.ProjectIgnored = project != null
                ? project.ProofIgnored : new List<string>();
            // Le dictionnaire personnel du projet aussi — et le cache des
            // verdicts repart de zéro (autre projet, autre connaissance).
            if (_spellChecker != null)
            {
                _spellChecker.ProjectWords = project != null
                    ? project.LearnedWords : new List<string>();
                _spellChecker.InvalidateLearned();
            }
            _checkHost.InvalidateCache();
        }

        public void LoadItem(BinderItem item)
        {
            _item = item;
            _loading = true;
            _appliedExtra.Clear(); // fresh document, fresh pagination
            _box.Document = FlowConverter.ToFlow(item.Document, _styles,
                _project, Settings.AppSettings.ShowAnnotations);
            _loading = false;
            ApplyPageVisuals();
            RebuildNotesPanel();
            RebuildAnnotationsPanel();
            HideSearch();
            // Le composé est LA surface d'édition (gel du batch 26) ; seul le
            // mode de compatibilité des Préférences rend le repli classique.
            if (!Settings.AppSettings.ClassicCompatibility) SetComposition(true);
            else if (ComposedActive) SetComposition(false);
            // Les combos du ruban (style, police, taille) reflètent le caret
            // dès l'ouverture — la synchro au chargement était avalée par le
            // garde _loading (dropdowns « vides »).
            SyncToolbar();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRulers));
        }

        /// <summary>Flushes the FlowDocument back into the pivot. Call before any
        /// save, item switch or style-sheet edit. In Composition mode the pivot
        /// IS the live model — flushing the dormant RichTextBox would wipe the
        /// composed edits, so it is skipped.</summary>
        public void Commit()
        {
            if (_item == null || ComposedActive) return;
            var annotations = _item.Document.Annotations;
            _item.Document = FlowConverter.FromFlow(_box.Document, _styles,
                _item.Document.Footnotes, _project);
            // Les annotations vivent à part des runs : la liste survit au
            // Commit, puis les orphelines (passage supprimé) sont purgées.
            _item.Document.Annotations = annotations;
            _item.Document.AnnotationOrder(true);
        }

        /// <summary>Re-renders the current item (after the style sheet changed).</summary>
        public void Reload()
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.FolioOffset = FolioOffset;
                _composed.Decor = Decor;
                _composed.Attach(_item, _styles, _pageSetup, _project); // recompose
                return;
            }
            Commit();
            LoadItem(_item);
        }

        /// <summary>Detaches the editor from its item (selection moved to a
        /// folder, or the item was undone out of existence).</summary>
        public void Clear()
        {
            _item = null;
            _loading = true;
            _box.Document = new FlowDocument();
            _loading = false;
            if (_composed != null)
            {
                _composed.Detach();
                _composed.Visibility = Visibility.Collapsed;
                _scroller.Visibility = Visibility.Visible;
            }
            if (_checkTimer != null) _checkTimer.Stop();
            _findings = new List<Correction.Finding>();
            RebuildCorrectionPanel();
            ApplyPageVisuals();
            RebuildNotesPanel();
            RebuildAnnotationsPanel();
            HideSearch();
        }

        /// <summary>Cheap periodic notes refresh: renumbers markers and rebuilds
        /// the panel only when the marker set changed, never while a note is
        /// being typed in (that would steal focus).</summary>
        public void SyncNotes()
        {
            if (_item == null) return;
            foreach (DockPanel row in _notesList.Children)
                foreach (var child in row.Children)
                {
                    var box = child as TextBox;
                    if (box != null && box.IsKeyboardFocused) return;
                }
            var ordered = ComposedActive ? PivotFootnoteOrder()
                : FlowConverter.RenumberFootnotes(_box.Document);
            var changed = ordered.Count != _notesList.Children.Count;
            if (!changed)
            {
                var i = 0;
                foreach (DockPanel row in _notesList.Children)
                {
                    foreach (var child in row.Children)
                    {
                        var box = child as TextBox;
                        if (box != null && (string)box.Tag != ordered[i]) { changed = true; break; }
                    }
                    if (changed) break;
                    i++;
                }
            }
            if (changed) RebuildNotesPanel();
        }

        public void FocusEditor()
        {
            _box.Focus();
        }

        /// <summary>Text undo/redo, claimed only when the writer is typing here
        /// (keyboard focus inside the box). Lets the window's Ctrl+Z / Ctrl+Y
        /// route to the text first and to the Binder history otherwise.</summary>
        public bool TryUndo()
        {
            if (_item == null) return false;
            if (ComposedActive) return _composed.Undo();
            if (!_box.IsKeyboardFocusWithin || !_box.CanUndo) return false;
            _box.Undo();
            return true;
        }

        public bool TryRedo()
        {
            if (_item == null) return false;
            if (ComposedActive) return _composed.Redo();
            if (!_box.IsKeyboardFocusWithin || !_box.CanRedo) return false;
            _box.Redo();
            return true;
        }

        public string PlainText()
        {
            if (_item == null) return "";
            if (ComposedActive) return _item.Document.ToPlainText();
            return new TextRange(_box.Document.ContentStart, _box.Document.ContentEnd).Text;
        }

        private void NotifyEdited()
        {
            var handler = Edited;
            if (handler != null) handler();
            ScheduleCheck(); // la correction suit l'édition, au debounce
        }

        private void AfterFormat()
        {
            NotifyEdited();
            SyncToolbar();
            _box.Focus();
        }

        // ============================================================= formatting

        private void OnStyleComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null) return;
            var chosen = _styleCombo.SelectedItem as ComboBoxItem;
            if (chosen == null) return;
            var style = _styles.Find((string)chosen.Tag);
            if (ComposedActive) { _composed.ApplyStyle(style.Id); _composed.Focus(); return; }

            var paragraph = _box.Selection.Start.Paragraph;
            var last = _box.Selection.End.Paragraph;
            while (paragraph != null)
            {
                FlowConverter.ApplyParagraphStyle(paragraph, style);
                if (paragraph == last) break;
                Block next = paragraph.NextBlock;
                while (next != null && !(next is Paragraph)) next = next.NextBlock;
                paragraph = next as Paragraph;
            }
            AfterFormat();
        }

        private void OnFontComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _fontCombo.SelectedItem == null) return;
            if (ComposedActive)
            {
                _composed.ApplyFont((string)_fontCombo.SelectedItem);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
                new FontFamily((string)_fontCombo.SelectedItem));
            AfterFormat();
        }

        /// <summary>Police tapée à la main dans la combo éditable.</summary>
        private void ApplyTypedFont(string name)
        {
            if (_item == null) return;
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            if (ComposedActive)
            {
                _composed.ApplyFont(name);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
                new FontFamily(name));
            AfterFormat();
        }

        /// <summary>Taille personnalisée tapée dans la combo éditable (pt).</summary>
        private void ApplyTypedSize(string text)
        {
            if (_item == null) return;
            double sizePt;
            if (!double.TryParse((text ?? "").Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out sizePt)) return;
            sizePt = Math.Max(4, Math.Min(200, sizePt));
            if (ComposedActive)
            {
                _composed.ApplySizePx(sizePt * 4.0 / 3.0);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, sizePt * 4.0 / 3.0);
            AfterFormat();
        }

        private void OnSizeComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _sizeCombo.SelectedItem == null) return;
            if (ComposedActive)
            {
                _composed.ApplySizePx((int)_sizeCombo.SelectedItem * 4.0 / 3.0);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,
                (int)_sizeCombo.SelectedItem * 4.0 / 3.0); // pt -> px
            AfterFormat();
        }

        /// <summary>Checkbox "list": a text prefix, so it stays plain text in
        /// every export. Cycles ☐ → ☑ → none on each selected paragraph.</summary>
        private void ToggleChecklist()
        {
            if (_item == null) return;
            var paragraph = _box.Selection.Start.Paragraph;
            var last = _box.Selection.End.Paragraph;
            while (paragraph != null)
            {
                ToggleCheckboxPrefix(paragraph);
                if (paragraph == last) break;
                Block next = paragraph.NextBlock;
                while (next != null && !(next is Paragraph)) next = next.NextBlock;
                paragraph = next as Paragraph;
            }
            AfterFormat();
        }

        private static void ToggleCheckboxPrefix(Paragraph paragraph)
        {
            Run run = null;
            foreach (var inline in paragraph.Inlines)
            {
                var candidate = inline as Run;
                if (candidate == null) break;
                var tag = candidate.Tag as string;
                if (tag != null && tag.StartsWith("fn:")) break; // never touch markers
                run = candidate;
                break;
            }
            if (run == null)
            {
                if (paragraph.Inlines.FirstInline == null)
                    paragraph.Inlines.Add(new Run("☐ "));
                else
                    paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, new Run("☐ "));
                return;
            }
            var text = run.Text;
            if (text.StartsWith("☐", StringComparison.Ordinal))
                run.Text = "☑" + text.Substring(1);
            else if (text.StartsWith("☑", StringComparison.Ordinal))
                run.Text = text.Substring(1).TrimStart(' ');
            else
                run.Text = "☐ " + text;
        }

        /// <summary>Inserts a horizontal rule on its own paragraph, below the
        /// caret's one; the caret lands on a fresh paragraph after it.</summary>
        public void InsertRule()
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.InsertElementAtCaret(new TextRun { IsRule = true });
                return;
            }
            var rule = new Paragraph();
            FlowConverter.ApplyParagraphStyle(rule, _styles.Body);
            rule.TextAlignment = TextAlignment.Center;
            rule.TextIndent = 0;
            rule.Inlines.Add(FlowConverter.MakeRuleInline(_project));
            InsertBlockBelowCaret(rule);
        }

        /// <summary>Inserts the project's scene separator ("***" by default,
        /// centered; text/font/size live in the project settings).</summary>
        public void InsertSeparator()
        {
            if (_item == null) return;
            var text = _project == null || string.IsNullOrEmpty(_project.SeparatorText)
                ? "***" : _project.SeparatorText;
            var font = _project != null && _project.SeparatorFont != null
                ? _project.SeparatorFont : _styles.Body.FontFamily;
            var sizePt = _project != null ? _project.SeparatorSizePt : 12;

            if (ComposedActive)
            {
                // Its own centered paragraph, then a clean continuation one.
                _composed.InsertParagraphBreak();
                int paragraph, offset;
                _composed.GetCaret(out paragraph, out offset);
                _composed.TypeText(text);
                _composed.PlaceCaret(paragraph, 0, false);
                _composed.PlaceCaret(paragraph, text.Length, true);
                _composed.ApplyFont(font);
                _composed.ApplySizePx(Math.Max(6, sizePt * 4.0 / 3.0));
                _composed.PlaceCaret(paragraph, text.Length, false);
                _composed.ApplyAlign("center");
                _composed.InsertParagraphBreak();
                int after, afterOffset;
                _composed.GetCaret(out after, out afterOffset);
                var style = _styles.Find(_item.Document.Paragraphs[after].StyleId);
                _composed.ApplyAlign(style.Align); // clears the inherited centering
                _composed.Focus();
                return;
            }

            var separatorParagraph = new Paragraph();
            FlowConverter.ApplyParagraphStyle(separatorParagraph, _styles.Body);
            separatorParagraph.TextAlignment = TextAlignment.Center;
            separatorParagraph.TextIndent = 0;
            separatorParagraph.Inlines.Add(new Run(text)
            {
                FontFamily = new FontFamily(font),
                FontSize = Math.Max(6, sizePt * 4.0 / 3.0)
            });
            InsertBlockBelowCaret(separatorParagraph);
        }

        /// <summary>Inserts a block after the caret's paragraph (after its list
        /// when the caret is inside one) and moves the caret to a fresh body
        /// paragraph below the inserted block.</summary>
        private void InsertBlockBelowCaret(Block block)
        {
            var current = _box.CaretPosition.Paragraph;
            Block anchor = current;
            if (current != null)
            {
                var listItem = current.Parent as ListItem;
                var list = listItem == null ? null : listItem.Parent as System.Windows.Documents.List;
                if (list != null) anchor = list;
                var section = current.Parent as Section;
                if (section != null) anchor = section;
            }
            if (anchor != null && anchor.Parent is FlowDocument)
                _box.Document.Blocks.InsertAfter(anchor, block);
            else
                _box.Document.Blocks.Add(block);

            var following = block.NextBlock as Paragraph;
            if (following == null)
            {
                following = new Paragraph();
                FlowConverter.ApplyParagraphStyle(following, _styles.Body);
                _box.Document.Blocks.InsertAfter(block, following);
            }
            _box.CaretPosition = following.ContentStart;
            NotifyEdited();
            ScheduleMarks();
            _box.Focus();
        }

        /// <summary>Inserts an image at the caret; bytes go to the project
        /// image store (saved inside the .plot).</summary>
        public void InsertImage()
        {
            if (_item == null || _project == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                if (info.Length > 20 * 1024 * 1024)
                    throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                var id = _project.AddImage(bytes, System.IO.Path.GetExtension(dialog.FileName));
                if (ComposedActive)
                {
                    _composed.InsertElementAtCaret(new TextRun { ImageId = id });
                    _composed.Focus();
                    return;
                }
                var stored = _project.FindImage(id);
                var caret = _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
                var container = new InlineUIContainer(FlowConverter.MakeImageElement(stored), caret)
                {
                    Tag = "img:" + id,
                    BaselineAlignment = BaselineAlignment.Bottom
                };
                _box.CaretPosition = container.ElementEnd;
                NotifyEdited();
                _box.Focus();
            }
            catch (Exception error)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Impossible d'insérer l'image :\n" + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ToggleDecoration(TextDecorationLocation location)
        {
            var current = _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                as TextDecorationCollection;
            var has = false;
            if (current != null)
                foreach (var decoration in current)
                    if (decoration.Location == location) { has = true; break; }

            var next = new TextDecorationCollection();
            if (current != null)
                foreach (var decoration in current)
                    if (decoration.Location != location) next.Add(decoration);
            if (!has)
                next.Add(location == TextDecorationLocation.Underline
                    ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
            _box.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
            AfterFormat();
        }

        private List<string> PivotFootnoteOrder()
        {
            var ordered = new List<string>();
            foreach (var paragraph in _item.Document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null) ordered.Add(run.FootnoteId);
            return ordered;
        }

        /// <summary>Reflects the selection's formatting in the format bar.</summary>
        private void SyncToolbar()
        {
            if (_loading || _item == null || ComposedActive) return;
            _syncing = true;
            try
            {
                var weight = _box.Selection.GetPropertyValue(TextElement.FontWeightProperty);
                _boldBtn.IsChecked = weight is FontWeight && (FontWeight)weight >= FontWeights.Bold;

                var fontStyle = _box.Selection.GetPropertyValue(TextElement.FontStyleProperty);
                _italicBtn.IsChecked = fontStyle is FontStyle && (FontStyle)fontStyle == FontStyles.Italic;

                var decorations = _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                    as TextDecorationCollection;
                var under = false;
                var strike = false;
                if (decorations != null)
                    foreach (var decoration in decorations)
                    {
                        if (decoration.Location == TextDecorationLocation.Underline) under = true;
                        if (decoration.Location == TextDecorationLocation.Strikethrough) strike = true;
                    }
                _underBtn.IsChecked = under;
                _strikeBtn.IsChecked = strike;

                var family = _box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                _fontCombo.SelectedItem = family == null ? null : (object)family.Source;
                if (family != null && _fontCombo.SelectedItem == null)
                    _fontCombo.Text = family.Source; // police hors liste (combo éditable)

                var size = _box.Selection.GetPropertyValue(TextElement.FontSizeProperty);
                _sizeCombo.SelectedItem = size is double
                    ? (object)(int)Math.Round((double)size * 0.75) : null; // px -> pt
                if (size is double && _sizeCombo.SelectedItem == null)
                    _sizeCombo.Text = ((double)size * 0.75).ToString("0.#",
                        System.Globalization.CultureInfo.CurrentCulture);

                var paragraph = _box.Selection.Start.Paragraph;
                if (paragraph != null)
                {
                    var styleId = paragraph.Tag as string ?? "body";
                    ComboBoxItem match = null;
                    foreach (ComboBoxItem candidate in _styleCombo.Items)
                        if ((string)candidate.Tag == styleId) { match = candidate; break; }
                    _styleCombo.SelectedItem = match;

                    var align = paragraph.TextAlignment;
                    _alignLeft.IsChecked = align == TextAlignment.Left;
                    _alignCenter.IsChecked = align == TextAlignment.Center;
                    _alignRight.IsChecked = align == TextAlignment.Right;
                    _alignJustify.IsChecked = align == TextAlignment.Justify;

                    var listItem = paragraph.Parent as ListItem;
                    var list = listItem == null ? null : listItem.Parent as System.Windows.Documents.List;
                    var numbered = list != null
                        && (list.MarkerStyle == TextMarkerStyle.Decimal
                            || list.MarkerStyle == TextMarkerStyle.LowerLatin
                            || list.MarkerStyle == TextMarkerStyle.UpperLatin
                            || list.MarkerStyle == TextMarkerStyle.LowerRoman
                            || list.MarkerStyle == TextMarkerStyle.UpperRoman);
                    _bulletBtn.IsChecked = list != null && !numbered;
                    _numberBtn.IsChecked = numbered;

                    var firstRun = paragraph.Inlines.FirstInline as Run;
                    var firstText = firstRun == null ? "" : firstRun.Text;
                    _checkBtn.IsChecked = firstText.StartsWith("☐", StringComparison.Ordinal)
                        || firstText.StartsWith("☑", StringComparison.Ordinal);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        // ============================================================= find & replace

        /// <summary>Ctrl+F. La recherche vit sur le PIVOT (batch 26, lot B.3) :
        /// elle ne force plus JAMAIS la sortie du mode composé — la barre
        /// s'ouvre au-dessus de la surface active, quelle qu'elle soit.</summary>
        public void ShowSearch()
        {
            if (_item == null) return;
            _searchBar.Visibility = Visibility.Visible;
            _searchInfo.Text = "";
            _searchCurrent = null;
            // « Mot entier » vit sur la recherche pivot — le repli classique
            // (gelé) garde son ancienne recherche telle quelle.
            _wholeWordCheck.IsEnabled = ComposedActive;
            _wholeWordCheck.ToolTip = ComposedActive ? null
                : "Disponible dans les pages composées";
            if (!ComposedActive && !_box.Selection.IsEmpty
                && _box.Selection.Text.Length < 80
                && !_box.Selection.Text.Contains("\n"))
                _searchBox.Text = _box.Selection.Text;
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public void HideSearch()
        {
            if (_searchBar.Visibility == Visibility.Collapsed) return;
            _searchBar.Visibility = Visibility.Collapsed;
            _searchCurrent = null;
            if (ComposedActive) _composed.Focus();
            else _box.Focus();
        }

        private StringComparison Comparison()
        {
            return _caseCheck.IsChecked == true
                ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        }

        /// <summary>A paragraph's text plus a map from character offsets to runs,
        /// so a hit index can be turned back into TextPointers.</summary>
        private sealed class ParagraphMap
        {
            public Paragraph Paragraph;
            public string Text;
            public List<int> Offsets = new List<int>();
            public List<Run> Runs = new List<Run>();

            public TextPointer PointerAt(int index)
            {
                for (var i = Offsets.Count - 1; i >= 0; i--)
                    if (Offsets[i] <= index)
                        return Runs[i].ContentStart.GetPositionAtOffset(index - Offsets[i]);
                return Paragraph.ContentStart;
            }
        }

        private List<ParagraphMap> MapParagraphs()
        {
            var maps = new List<ParagraphMap>();
            foreach (var paragraph in FlowConverter.EnumerateParagraphs(_box.Document))
            {
                var map = new ParagraphMap { Paragraph = paragraph };
                var sb = new System.Text.StringBuilder();
                AppendInlines(paragraph.Inlines, sb, map);
                map.Text = sb.ToString();
                maps.Add(map);
            }
            return maps;
        }

        private static void AppendInlines(InlineCollection inlines, System.Text.StringBuilder sb, ParagraphMap map)
        {
            foreach (var inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    var tag = run.Tag as string;
                    if (tag != null && tag.StartsWith("fn:")) continue; // markers are not text
                    map.Offsets.Add(sb.Length);
                    map.Runs.Add(run);
                    sb.Append(run.Text);
                    continue;
                }
                if (inline is LineBreak) { sb.Append('\n'); continue; }
                var span = inline as Span;
                if (span != null) AppendInlines(span.Inlines, sb, map);
            }
        }

        private void FindNext()
        {
            if (ComposedActive) TryFindNextComposed(true);
            else TryFindNext(true);
        }

        /// <summary>La recherche pivot du mode composé : PivotSearch trouve,
        /// la vue sélectionne — le composé reste actif du début à la fin.</summary>
        private bool TryFindNextComposed(bool wrap)
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null) return false;
            var matches = PivotSearch.FindAll(_item.Document, needle,
                _caseCheck.IsChecked == true, _wholeWordCheck.IsChecked == true);
            if (matches.Count == 0)
            {
                _searchCurrent = null;
                _searchInfo.Text = "Aucun résultat";
                return false;
            }
            int paragraph, offset;
            _composed.CaretLocation(out paragraph, out offset);
            PivotSearch.Match next = null;
            foreach (var match in matches)
                if (match.ParagraphIndex > paragraph
                    || (match.ParagraphIndex == paragraph && match.Start >= offset))
                { next = match; break; }
            if (next == null)
            {
                if (!wrap) { _searchInfo.Text = "Aucun résultat"; return false; }
                next = matches[0];
                _searchInfo.Text = "Reprise au début";
            }
            else _searchInfo.Text = "";
            _searchCurrent = next;
            _composed.SelectRange(next.ParagraphIndex, next.Start,
                next.Start + next.Length);
            return true;
        }

        private bool TryFindNext(bool wrap)
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null) return false;
            var comparison = Comparison();
            var caret = _box.Selection.End;

            var maps = MapParagraphs();
            TextPointer firstStart = null, firstEnd = null; // first hit in the document (wrap target)
            foreach (var map in maps)
            {
                var index = 0;
                while ((index = map.Text.IndexOf(needle, index, comparison)) >= 0)
                {
                    var start = map.PointerAt(index);
                    var end = map.PointerAt(index + needle.Length);
                    if (start != null && end != null)
                    {
                        if (firstStart == null) { firstStart = start; firstEnd = end; }
                        if (start.CompareTo(caret) > 0)
                        {
                            SelectResult(start, end);
                            return true;
                        }
                    }
                    index += 1;
                }
            }
            if (wrap && firstStart != null)
            {
                SelectResult(firstStart, firstEnd);
                _searchInfo.Text = "Reprise au début";
                return true;
            }
            _searchInfo.Text = "Aucun résultat";
            return false;
        }

        private void SelectResult(TextPointer start, TextPointer end)
        {
            _box.Selection.Select(start, end);
            _searchInfo.Text = "";
            var element = start.Parent as FrameworkContentElement;
            if (element != null) element.BringIntoView();
            _box.Focus();
        }

        private void ReplaceCurrent()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle)) return;
            if (ComposedActive)
            {
                // Le résultat courant est vérifié contre le texte VIVANT
                // avant remplacement (le document a pu bouger).
                if (_searchCurrent != null && _item != null
                    && _searchCurrent.ParagraphIndex < _item.Document.Paragraphs.Count)
                {
                    var text = PivotEdit.FlatText(
                        _item.Document.Paragraphs[_searchCurrent.ParagraphIndex]);
                    if (_searchCurrent.Start + _searchCurrent.Length <= text.Length
                        && string.Equals(text.Substring(_searchCurrent.Start,
                            _searchCurrent.Length), needle, Comparison()))
                        _composed.ReplaceRange(_searchCurrent.ParagraphIndex,
                            _searchCurrent.Start, _searchCurrent.Length,
                            _replaceBox.Text);
                    _searchCurrent = null;
                }
                TryFindNextComposed(true);
                return;
            }
            if (!_box.Selection.IsEmpty
                && string.Equals(_box.Selection.Text, needle, Comparison()))
            {
                _box.Selection.Text = _replaceBox.Text;
                NotifyEdited();
            }
            FindNext();
        }

        private void ReplaceAll()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null) return;
            if (ComposedActive)
            {
                var matches = PivotSearch.FindAll(_item.Document, needle,
                    _caseCheck.IsChecked == true, _wholeWordCheck.IsChecked == true);
                var replaced = _composed.ReplaceAll(matches, _replaceBox.Text);
                _searchCurrent = null;
                _searchInfo.Text = replaced == 0 ? "Aucun résultat"
                    : replaced == 1 ? "1 remplacement" : replaced + " remplacements";
                return;
            }
            var count = 0;
            _box.CaretPosition = _box.Document.ContentStart;
            _box.Selection.Select(_box.Document.ContentStart, _box.Document.ContentStart);
            while (count < 10000 && TryFindNext(false))
            {
                _box.Selection.Text = _replaceBox.Text;
                _box.CaretPosition = _box.Selection.End;
                count++;
            }
            if (count > 0) NotifyEdited();
            _searchInfo.Text = count == 0 ? "Aucun résultat"
                : count == 1 ? "1 remplacement" : count + " remplacements";
        }

        // ============================================================= wiki links

        private void OnEditorMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var pointer = _box.GetPositionFromPoint(e.GetPosition(_box), true);
            if (pointer == null) return;
            var run = pointer.Parent as Run;
            if (run == null || (run.Tag as string) != "wikilink") return;
            var text = run.Text.Trim();
            if (!text.StartsWith("[[") || !text.EndsWith("]]")) return;
            var handler = LinkClicked;
            if (handler != null) handler(text.Substring(2, text.Length - 4).Trim());
            e.Handled = true;
        }

        /// <summary>Inserts a styled [[link]] at the caret, immediately clickable.</summary>
        public void InsertWikiLink(string title)
        {
            if (_item == null || string.IsNullOrEmpty(title)) return;
            if (ComposedActive)
            {
                _composed.TypeText("[[" + title + "]]");
                _composed.Focus();
                return;
            }
            var caret = _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var link = new Run("[[" + title + "]]", caret)
            {
                Tag = "wikilink",
                Foreground = Chrome.Accent,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _box.CaretPosition = link.ElementEnd;
            _box.Selection.Select(link.ElementEnd, link.ElementEnd);
            _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)Chrome.PaperInk);
            NotifyEdited();
            _box.Focus();
        }

        // ============================================================= footnotes

        public void InsertFootnote()
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.InsertFootnoteAtCaret();
                RebuildNotesPanel();
                if (_item.Document.Footnotes.Count > 0)
                    FocusNote(_item.Document.Footnotes[_item.Document.Footnotes.Count - 1].Id);
                return;
            }
            var note = new Footnote();
            _item.Document.Footnotes.Add(note);

            var caret = _box.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var paragraph = caret.Paragraph;
            var size = paragraph != null ? paragraph.FontSize : _styles.Body.FontSize;
            var marker = new Run("?", caret)
            {
                Tag = "fn:" + note.Id,
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = Math.Max(8, size * 0.65),
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.Accent
            };

            // Move the caret past the marker and neutralize its spring-loaded
            // formatting so typing resumes with normal text.
            _box.CaretPosition = marker.ElementEnd;
            _box.Selection.Select(marker.ElementEnd, marker.ElementEnd);
            _box.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
            _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            _box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)Chrome.PaperInk);

            RebuildNotesPanel();
            NotifyEdited();
            FocusNote(note.Id);
        }

        /// <summary>Renumbers markers, prunes orphaned notes and rebuilds the
        /// bottom panel. Called after load, insert and edits that may have
        /// removed a marker.</summary>
        public void RebuildNotesPanel()
        {
            _notesList.Children.Clear();
            if (_item == null) { _notesBar.Visibility = Visibility.Collapsed; return; }

            var ordered = ComposedActive ? PivotFootnoteOrder()
                : FlowConverter.RenumberFootnotes(_box.Document);
            _notesBar.Visibility = ordered.Count == 0 || _calm
                ? Visibility.Collapsed : Visibility.Visible;

            var number = 0;
            foreach (var id in ordered)
            {
                number++;
                var note = _item.Document.FindFootnote(id);
                if (note == null)
                {
                    // A marker without its note (should not happen): recreate the
                    // note rather than lose the marker silently.
                    note = new Footnote { Id = id };
                    _item.Document.Footnotes.Add(note);
                }

                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var label = new TextBlock
                {
                    Text = number + ".",
                    Foreground = Chrome.SoftText,
                    Width = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                var noteRef = note;
                var box = new TextBox
                {
                    Text = note.Text,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = false,
                    Tag = id
                };
                box.TextChanged += delegate
                {
                    noteRef.Text = box.Text;
                    if (ComposedActive) _composed.RefreshNotes();
                    NotifyEdited();
                };
                row.Children.Add(box);
                _notesList.Children.Add(row);
            }
        }

        private void FocusNote(string id)
        {
            foreach (DockPanel row in _notesList.Children)
                foreach (var child in row.Children)
                {
                    var box = child as TextBox;
                    if (box != null && (string)box.Tag == id) { box.Focus(); return; }
                }
        }
    }
}
