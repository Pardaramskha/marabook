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
    public partial class EditorView : DockPanel
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
        public event Action LinkRequested;           // « Lien vers une fiche… » (onglet Insertion, b33)
        public event Action<bool> LexiconChanged;    // dictionnaire personnel modifié (portée projet = vrai)
        public event Action<BinderItem> PlanRequested; // « Plan » du ruban : ouvrir le plan de l'écrit (b35)
        /// <summary>Le plan dont une colonne est reliée à l'écrit, ou null — posé par la coquille.</summary>
        public Func<BinderItem, BinderItem> PlanLocator;
        private Button _planBtn;

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
        private readonly Correction.RepetitionChecker _repetitionChecker = new Correction.RepetitionChecker();
        // La grammaire (batch 29) : le pont Grammalecte, PARESSEUX — le
        // processus Python ne démarre qu'au premier paragraphe vérifié, un
        // utilisateur qui n'active jamais la grammaire ne paie rien.
        private Correction.Grammalecte.GrammalecteBridge _grammarBridge;
        private Correction.Grammalecte.GrammarChecker _grammarChecker;
        // L'étage style morphologique et les synonymes (batch 44) : le MÊME
        // pont que la grammaire — adverbes en -ment, verbes ternes, et le
        // thésaurus servi à la demande (menu contextuel, panneau).
        private Correction.Grammalecte.StyleChecker _styleChecker;
        private Correction.Grammalecte.SynonymProvider _synonyms;
        // Les mots dont les synonymes viennent d'arriver, en attente de la
        // repeinture coalescée (sous _suggestGate).
        private readonly HashSet<string> _synonymArrivals = new HashSet<string>();
        private ToggleButton _corrDetailsBtn; // « Détails de correction » (b28)

        /// <summary>Le panneau des signalements vit À DROITE depuis le batch
        /// 28 (il remplace l'inspecteur quand il est ouvert) — la coquille
        /// l'héberge et écoute cette bascule.</summary>
        public event Action<bool> CorrectionPanelToggled; // vrai = demandé, faux = rendu
        public UIElement CorrectionPanel { get { return _corrBar; } }
        /// <summary>La case « Détails de correction » suit le panneau actif
        /// (batch 39) — posée par la coquille, jamais décidée ici.</summary>
        public bool CorrectionPanelChecked
        {
            set { if (_corrDetailsBtn != null) _corrDetailsBtn.IsChecked = value; }
        }
        /// <summary>Le nombre de signalements du pilote — la pastille du rail
        /// (batch 39) ; levé à chaque reconstruction de la liste.</summary>
        public event Action FindingsChanged;
        public int FindingCount { get { return _findings.Count; } }

        /// <summary>Les fautes d'orthographe seules (succès « Nettoyage en profondeur », 12/09).</summary>
        public int SpellingFindingCount
        {
            get
            {
                var count = 0;
                foreach (var finding in _findings)
                    if (finding.Category == Correction.FindingCategory.Spelling) count++;
                return count;
            }
        }
        private List<Correction.Finding> _findings = new List<Correction.Finding>();
        private DispatcherTimer _checkTimer;
        private bool _deferredRepaintQueued; // coalescence des lots différés

        // ---- suggestions en arrière-plan (batch 30) : Suggest() est LE coût
        // dominant du clic sur un chapitre (mesuré : 6,5 s de panneau pour
        // 150 signalements à froid). Plus JAMAIS pendant la construction du
        // panneau — une file, un seul ouvrier de fond qui chauffe le mémo du
        // moteur (immuable, sûr entre fils), une repeinture coalescée.
        // Tête de file = mots du panneau affiché ; queue = préchauffage de
        // l'ouverture. _suggestSeen ne se vide jamais : traité = au mémo.
        private readonly object _suggestGate = new object();
        private readonly List<string> _suggestQueue = new List<string>();
        private readonly HashSet<string> _suggestSeen = new HashSet<string>();
        private bool _suggestWorkerRunning;
        private bool _suggestRepaintQueued;
        private Correction.Hunspell.SpellEngine _spellEngine; // null sans dico
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
            // Options du correcteur (batch 33) : le style (répétitions) ne
            // s'allume plus qu'à la demande.
            if (Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleRepetitions)
                _checkHost.Add(_repetitionChecker);
            // L'orthographe (batch 27) : moteur Hunspell maison sur le
            // dictionnaire embarqué — absent du disque, le vérificateur se
            // retire sans bruit. Les suggestions sont servies À LA DEMANDE.
            var spellEngine = Correction.SpellDictionary.Default;
            _spellEngine = spellEngine;
            if (spellEngine != null)
            {
                _spellChecker = new Correction.SpellChecker(spellEngine);
                _spellChecker.GlobalWords = Settings.AppSettings.Lexicon;
                if (Settings.AppSettings.SpellEnabled) _checkHost.Add(_spellChecker);
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
            // La grammaire (batch 29, lot B) : Grammalecte en sous-processus,
            // vérificateur DIFFÉRÉ — jamais un point de panne : pont absent
            // (Python ou grammalecte/ manquants), il se tait.
            _grammarBridge = new Correction.Grammalecte.GrammalecteBridge();
            _grammarChecker = new Correction.Grammalecte.GrammarChecker(_grammarBridge);
            _grammarChecker.UserOptions = Settings.AppSettings.GrammarOptions;
            _grammarChecker.GrammarEnabled = Settings.AppSettings.GrammarEnabled;
            _grammarChecker.TypographyEnabled = Settings.AppSettings.TypographyEnabled;
            if (Settings.AppSettings.GrammarEnabled || Settings.AppSettings.TypographyEnabled)
                _checkHost.Add(_grammarChecker);
            // Le style morphologique (batch 44) : même pont, même différé,
            // même silence si le pont manque.
            _styleChecker = new Correction.Grammalecte.StyleChecker(_grammarBridge);
            ApplyStyleSettings();
            if (Settings.AppSettings.StyleEnabled && _styleChecker.Wanted)
                _checkHost.Add(_styleChecker);
            // Les synonymes (batch 44) : demandés au clic, affichés quand ils
            // sont prêts — le menu ouvert se remplit, le panneau se repeint.
            _synonyms = Correction.Grammalecte.SynonymProvider.For(_grammarBridge);
            // Arrivées COALESCÉES (13/09) : cent répétitions au panneau font
            // cent demandes ; cent réponses rapprochées = UNE repeinture,
            // jamais cent reconstructions de 150 fiches. Les mots arrivés
            // sont gardés : seul le sous-menu qui attendait L'UN d'eux est
            // rafraîchi (revue du 13/09).
            _synonyms.Arrived += delegate(string word)
            {
                lock (_suggestGate) _synonymArrivals.Add(word);
                QueueCorrectionRepaint();
            };
            // Le sous-menu « Synonymes » : la demande part quand l'auteur
            // L'OUVRE (geste explicite — le pont peut démarrer pour lui).
            _composed.SynonymLookup = delegate(string word)
            {
                return _synonyms.Answer(word, 30, true);
            };
            _grammarBridge.StateChanged += delegate
            {
                // Pont redevenu prêt : les échecs de synonymes sont oubliés.
                if (_grammarBridge.State == Correction.Grammalecte.BridgeState.Ready)
                    _synonyms.Reset();
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(UpdateGrammarStatus));
            };
            _composed.FindingLearn += delegate(Correction.Finding finding, bool projectScope)
            {
                if (_spellChecker == null || finding.Word.Length == 0) return;
                // Batch 33 : le mot entre avec sa NATURE (dialogue à la manière
                // d'Antidote) — le correcteur acceptera ses formes.
                var entry = LexiconEntryDialog.AskForWord(Window.GetWindow(this), finding.Word, projectScope);
                if (entry == null) return;
                var list = projectScope
                    ? _spellChecker.ProjectWords : _spellChecker.GlobalWords;
                var existing = Model.LexiconEntry.Find(list, entry.Word);
                if (existing != null) list.Remove(existing);
                list.Add(entry);
                if (projectScope) NotifyEdited(); // la liste vit dans le .plot
                else Settings.AppSettings.Save();
                var lexiconHandler = LexiconChanged;
                if (lexiconHandler != null) lexiconHandler(projectScope);
                // La connaissance a changé, pas le texte : le cache des
                // vérificateurs locaux doit oublier ses verdicts — et les
                // clés pliées des appris aussi (batch 29, 0.3).
                _spellChecker.InvalidateLearned();
                _checkHost.InvalidateCache();
                RunCheck();
            };
            _composed.SuggestionProvider = delegate(Correction.Finding finding)
            {
                // Le menu contextuel montre au moment de montrer : les
                // suggestions d'orthographe se calculent ICI (batch 27) ;
                // les synonymes prêts sont listés, les autres attendent le
                // sous-menu « Synonymes » du même mot.
                if (finding.Suggests == Correction.SuggestionSource.Spelling && _spellChecker != null)
                    return _spellChecker.Suggestions(finding.Word);
                return SuggestionsFor(finding, 8, false);
            };
            _checkHost.GlobalIgnored = Settings.AppSettings.ProofIgnored;
            _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _checkTimer.Tick += delegate { _checkTimer.Stop(); RunCheck(); };
            // Le différé (batch 29, lot A) : l'événement arrive sur un thread
            // du pool — UN passage Dispatcher par LOT de réponses, coalescé
            // par le drapeau (dix réponses rapprochées = une repeinture).
            _checkHost.DeferredArrived += delegate
            {
                if (_deferredRepaintQueued) return;
                _deferredRepaintQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(delegate
                    {
                        _deferredRepaintQueued = false;
                        RunCheck(); // tout est en cache : passe quasi gratuite
                    }));
            };
        }

        public bool HasItem { get { return _item != null; } }

        /// <summary>True when THIS item is the one on screen — the shell's
        /// re-click guard must never trust visibility alone.</summary>
        public bool ShowsItem(Model.BinderItem item) { return _item == item; }

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
                // Mode calme (13/09) : toujours la même feuille — A4 nue,
                // ni décor, ni folio, ni guides — quel que soit l'axe choisi.
                var plain = _draftView || _calm;
                _composed.FolioOffset = plain ? 0 : FolioOffset;
                _composed.Decor = plain ? null : Decor;
                _composed.Attach(_item, _styles,
                    _calm ? CalmSetup(_pageSetup) : _draftView ? DraftSetup(_pageSetup) : _pageSetup,
                    _project);
                _composed.Visibility = Visibility.Visible;
                _scroller.Visibility = Visibility.Collapsed;
                _composed.SetFormattingMarks(_showMarks); // l'état du ¶ suit la surface
                _composed.Focus();
                RebuildAnnotationsPanel();
                RebuildNotesPanel(); // le panneau du bas se retire en pages composées (b33)
                RunCheck(); // la surface composée s'ouvre vérifiée
            }
            catch (Exception error)
            {
                MessageDialog.Show(Window.GetWindow(this),
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
            // Jamais de règles en mode calme (13/09) : rien que la feuille.
            var show = Settings.AppSettings.ShowRulers && _item != null && !_calm;
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
            // Les pages composées dessinent leurs propres marques (batch 35 —
            // le classique replié ne les montrait plus à personne).
            if (_composed != null) _composed.SetFormattingMarks(visible);
            RefreshOverlay();
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

            var findNext = Buttons.Icon("next", "Occurrence suivante (Entrée)", Buttons.Compact, Buttons.Look.Outline);
            findNext.Margin = new Thickness(0, 0, 6, 0);
            findNext.Click += delegate { FindNext(); };
            panel.Children.Add(findNext);
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
            // Compact, contour : dans un panneau, une suggestion doit se
            // lire comme cliquable (batch 40).
            var button = Buttons.Text(label, null, Buttons.Compact, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 6, 0);
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

        // ============================================================= insertion

        private string _lastNoteId; // dernière note visitée (précédent/suivant)

        /// <summary>Onglet « Insertion » (13/09) : la note en grand carré,
        /// la navigation entre notes superposée, le lien, le saut de page
        /// (venu de Mise en page) — chacun dans sa section.</summary>
        private UIElement BuildInsertTab()
        {
            var panel = TabPanel();
            var footnote = BigSquare("footnote", "Note de bas de page",
                "Insère un appel de note au curseur (Ctrl+Maj+N) — la note "
                + "s'édite en place, au bas de la page : cliquez-la, ou son appel");
            footnote.Click += delegate { InsertFootnote(); };
            panel.Children.Add(footnote);
            panel.Children.Add(VerticalRuleTall());

            var previous = OneLine("previous", "Note précédente", "Note de bas de page précédente");
            previous.Click += delegate { NavigateNote(-1); };
            var next = OneLine("next", "Note suivante", "Note de bas de page suivante");
            next.Click += delegate { NavigateNote(1); };
            panel.Children.Add(Stacked(previous, next));
            panel.Children.Add(VerticalRuleTall());

            var link = OneLine("connection", "Lien vers une fiche",
                "Insère un [[lien]] vers une fiche ou un écrit (Ctrl+K) — "
                + "Ctrl+clic sur le lien pour l'ouvrir");
            link.Click += delegate
            {
                var handler = LinkRequested;
                if (handler != null) handler();
            };
            panel.Children.Add(link);
            panel.Children.Add(VerticalRuleTall());

            var pageBreak = OneLine("file-arrow-down-bold", "Saut de page",
                "Commencer une nouvelle page au paragraphe du curseur (Ctrl+Entrée)");
            pageBreak.Click += delegate { InsertPageBreak(); };
            panel.Children.Add(pageBreak);
            return panel;
        }

        /// <summary>Précédent/suivant entre les notes, dans l'ordre des
        /// appels ; la note atteinte s'ouvre en place. Repart de la note
        /// ouverte ou de la dernière visitée ; boucle aux extrémités.</summary>
        public void NavigateNote(int direction)
        {
            if (_item == null) return;
            var order = PivotFootnoteOrder();
            if (order.Count == 0) return;
            var current = ComposedActive && _composed.EditingNoteId != null
                ? _composed.EditingNoteId : _lastNoteId;
            var index = current == null ? -1 : order.IndexOf(current);
            int target;
            if (index < 0) target = direction > 0 ? 0 : order.Count - 1;
            else target = (index + direction + order.Count) % order.Count;
            OpenNote(order[target]);
        }

        /// <summary>Ouvre une note pour édition : en place dans les pages
        /// composées, dans le panneau du bas en compatibilité classique.</summary>
        private void OpenNote(string id)
        {
            _lastNoteId = id;
            if (ComposedActive) _composed.EditNote(id);
            else FocusNote(id);
        }

        // ============================================================= formatage

        /// <summary>Onglet « Formatage » (batch 34 ; 13/09 : deux grands
        /// carrés) : la PASSE TYPOGRAPHIQUE — les règles de Typonanny sur
        /// tout l'écrit ouvert, une fenêtre comparative avant/après en
        /// miroir, puis « Appliquer » (annulable) ; et ses options.</summary>
        private UIElement BuildFormatTab()
        {
            var panel = TabPanel();
            var typography = BigSquare("section", "Typographie",
                "Repasse typographique de tout l'écrit (apostrophes, « », insécables, …, tirets, "
                + "ligatures, ordinaux…) — comparatif avant/après, puis application");
            typography.Click += delegate { RunTypography(); };
            panel.Children.Add(typography);
            panel.Children.Add(VerticalRuleTall());
            var options = BigSquare("gear-six-bold", "Options",
                "Préréglage (Imprimerie nationale, souple, minimal) et règles de la passe typographique");
            options.Click += delegate { TypographyOptionsDialog.Ask(Window.GetWindow(this)); };
            panel.Children.Add(options);
            return panel;
        }

        /// <summary>La passe : sur le pivot entier (jamais une sélection),
        /// comparatif, application en un cran d'annulation.</summary>
        public void RunTypography()
        {
            if (_item == null) return;
            if (!ComposedActive)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "La passe typographique s'applique dans les pages composées.",
                    "Formatage", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = Correction.TypographyPass.Run(_item.Document, Settings.AppSettings.Typography);
            if (result.Changes.Count == 0)
            {
                var message = "Rien à corriger : la typographie de cet écrit est déjà en règle.";
                if (result.Summary.Warnings.Count > 0)
                    message += "\n\nSignalements :\n• " + string.Join("\n• ", result.Summary.Warnings.ToArray());
                MessageDialog.Show(Window.GetWindow(this), message, "Formatage",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!TypographyCompareWindow.Ask(Window.GetWindow(this), result, _item.Title)) return;
            // La ceinture (b38, lot C) : un instantané automatique AVANT de
            // réécrire le document entier — l'annulation est de session,
            // l'instantané survit à la fermeture.
            if (_project != null && SnapshotStore.GuardBeforeTypography(_project, _item, Settings.AppSettings.SnapshotCap) != null)
            {
                var taken = SnapshotsChanged;
                if (taken != null) taken();
            }
            _composed.ReplaceParagraphs(result.Paragraphs);
            RebuildNotesPanel();
            RunCheck();
        }

        /// <summary>Un instantané automatique vient d'être pris ici (le
        /// projet a changé, le panneau Versions doit suivre).</summary>
        public event Action SnapshotsChanged;

        /// <summary>L'état du document tel qu'il a été ouvert, si la surface
        /// composée s'en souvient encore (sa pile locale) — sinon null.</summary>
        public TextDocument DocumentAtOpen()
        {
            return ComposedActive && _composed != null ? _composed.OldestUndoDocument() : null;
        }

        // ============================================================= révision

        /// <summary>Onglet « Révision » (13/09) : annoter en grand carré ;
        /// la navigation entre annotations superposée et la visibilité des
        /// notes dans la seconde section.</summary>
        private UIElement BuildRevisionTab()
        {
            var panel = TabPanel();
            var annotate = BigSquare("add-annotation", "Annoter la sélection",
                "Ancre un commentaire de révision au passage sélectionné "
                + "(teinte or à l'écran, jamais imprimée)");
            annotate.Click += delegate { CreateAnnotation(); };
            panel.Children.Add(annotate);
            panel.Children.Add(VerticalRuleTall());

            var previous = OneLine("previous", "Annotation précédente", "Aller à l'annotation précédente");
            previous.Click += delegate { NavigateAnnotation(-1); };
            var next = OneLine("next", "Annotation suivante", "Aller à l'annotation suivante");
            next.Click += delegate { NavigateAnnotation(1); };
            panel.Children.Add(Stacked(previous, next));

            _annVisibleBtn = OneLineTextToggle("Afficher les notes",
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
            return panel;
        }

        // ============================================================= correction (onglet)

        /// <summary>Onglet « Correction » (batch 33 ; 13/09 : grand carré et
        /// piles) : Vérifier en grand carré ; signalement précédent/suivant
        /// superposés ; Détails et Ne pas corriger superposés ; les Options
        /// du correcteur.</summary>
        private UIElement BuildCorrectionTab()
        {
            var panel = TabPanel();
            var proofToggle = BigSquareToggle("text-a-underline-bold", "Vérifier",
                "Vérification continue du texte — orthographe, grammaire, "
                + "typographie et style selon les Options du correcteur");
            proofToggle.IsChecked = Settings.AppSettings.ProofEnabled;
            proofToggle.Click += delegate
            {
                Settings.AppSettings.ProofEnabled = proofToggle.IsChecked == true;
                Settings.AppSettings.Save();
                RunCheck();
            };
            panel.Children.Add(proofToggle);
            panel.Children.Add(VerticalRuleTall());

            var previousFinding = OneLine("previous", "Signalement précédent", "Signalement de correction précédent");
            previousFinding.Click += delegate { NavigateFinding(-1); };
            var nextFinding = OneLine("next", "Signalement suivant", "Signalement de correction suivant");
            nextFinding.Click += delegate { NavigateFinding(1); };
            panel.Children.Add(Stacked(previousFinding, nextFinding));
            panel.Children.Add(VerticalRuleTall());

            _corrDetailsBtn = OneLineToggle("exam-bold", "Détails de correction",
                "Le panneau des signalements, à droite — il remplace les "
                + "détails du chapitre tant qu'il est ouvert");
            _corrDetailsBtn.IsChecked = Settings.AppSettings.RightPanel == Settings.RightPanel.Correction;
            _corrDetailsBtn.Click += delegate
            {
                // La coquille décide (batch 39) ; elle resynchronise la case.
                var handler = CorrectionPanelToggled;
                if (handler != null) handler(_corrDetailsBtn.IsChecked == true);
            };
            var noProof = OneLine("text-t-slash-bold", "Ne pas corriger",
                "Soustrait le passage sélectionné aux correcteurs "
                + "(noms inventés, langues fictives, citations étrangères) "
                + "— re-cliquer pour l'y rendre");
            noProof.Click += delegate
            {
                // Le gel du classique (batch 26) : la commande vit dans les
                // pages composées, la seule surface d'édition supportée.
                if (!ComposedActive || !_composed.ToggleNoProofSelection())
                    MessageDialog.Show(Window.GetWindow(this),
                        ComposedActive
                            ? "Sélectionnez d'abord le passage à soustraire."
                            : "« Ne pas corriger » s'applique dans les pages composées.",
                        "Révision", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            panel.Children.Add(Stacked(_corrDetailsBtn, noProof));
            panel.Children.Add(VerticalRuleTall());

            var options = OneLine("gear-six-bold", "Options du correcteur",
                "Ce que le correcteur relève : orthographe, grammaire, "
                + "typographie, style — cochez, décochez");
            options.Click += delegate
            {
                if (ProofOptionsDialog.Ask(Window.GetWindow(this))) RefreshProofing();
            };
            panel.Children.Add(options);
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
            foreach (var category in CategoryOrder) AddCorrectionFilter(filters, category);
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

        /// <summary>L'ordre des catégories partout au panneau : filtres et
        /// groupes.</summary>
        private static readonly Correction.FindingCategory[] CategoryOrder =
        {
            Correction.FindingCategory.Spelling, Correction.FindingCategory.Grammar,
            Correction.FindingCategory.Typography, Correction.FindingCategory.Style
        };

        private void AddCorrectionFilter(Panel host, Correction.FindingCategory category)
        {
            var chip = new ToggleButton
            {
                Content = ComposedRenderer.CategoryLabel(category),
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
        /// <summary>À la fermeture de l'application : annule le différé en
        /// vol et arrête le pont Grammalecte — sans JAMAIS attendre.</summary>
        public void ShutdownProofing()
        {
            _checkHost.CancelDeferred();
            if (_synonyms != null) _synonyms.Cancel();
            if (_grammarBridge != null) _grammarBridge.Dispose();
        }

        /// <summary>Les réglages de l'étage style (batch 44) → le vérificateur.</summary>
        private void ApplyStyleSettings()
        {
            if (_styleChecker == null) return;
            _styleChecker.AdverbsEnabled = Settings.AppSettings.StyleAdverbs;
            _styleChecker.DullVerbsEnabled = Settings.AppSettings.StyleDullVerbs;
            _styleChecker.DullVerbs = new List<string>(Settings.AppSettings.DullVerbs);
        }

        /// <summary>Vrai si le pont Grammalecte est déjà en service pour un
        /// vérificateur (grammaire, typographie, style morphologique) ou
        /// déjà prêt : alors le panneau peut demander des synonymes sans
        /// rien allumer. Sinon, seul le geste explicite de l'auteur (le
        /// sous-menu) démarre Python — l'invariant du pont paresseux.</summary>
        private bool SynonymsOffered
        {
            get
            {
                if (_grammarBridge == null || _synonyms == null) return false;
                return BridgeInUse
                    || _grammarBridge.State == Correction.Grammalecte.BridgeState.Ready;
            }
        }

        /// <summary>Le pont sert-il un vérificateur actif ?</summary>
        private bool BridgeInUse
        {
            get
            {
                return Settings.AppSettings.GrammarEnabled
                    || Settings.AppSettings.TypographyEnabled
                    || (Settings.AppSettings.StyleEnabled && _styleChecker != null && _styleChecker.Wanted);
            }
        }

        /// <summary>LE résolveur des suggestions d'un signalement (revue du
        /// 13/09) — panneau et menu contextuel l'appellent tous deux :
        /// - Spelling : le mémo du moteur SEULEMENT (le calcul coûte des
        ///   dizaines de ms, il se fait dans l'ouvrier de fond) ;
        /// - Synonyms : le mémo du thésaurus, une demande si le pont est en
        ///   service (requestMissing) — la rangée se remplit à l'arrivée ;
        /// - Inline : ce que le vérificateur a posé (grammaire, typographie).</summary>
        private List<string> SuggestionsFor(Correction.Finding finding, int cap, bool requestMissing)
        {
            switch (finding.Suggests)
            {
                case Correction.SuggestionSource.Spelling:
                    if (_spellChecker == null) return new List<string>();
                    var cached = _spellChecker.CachedSuggestions(finding.Word);
                    if (cached != null) return cached;
                    if (requestMissing) QueueSuggestion(finding.Word, true);
                    return new List<string>();
                case Correction.SuggestionSource.Synonyms:
                    if (_synonyms == null || finding.Word.Length == 0) return new List<string>();
                    var answer = _synonyms.Answer(finding.Word, cap, requestMissing && SynonymsOffered);
                    return answer.Words ?? new List<string>();
                default:
                    return finding.Suggestions;
            }
        }

        /// <summary>UNE repeinture coalescée du panneau pour tous les
        /// producteurs de fond (suggestions d'orthographe, synonymes) : dix
        /// arrivées rapprochées, une reconstruction ; les sous-menus de
        /// synonymes ouverts sont rafraîchis pour LEURS mots.</summary>
        private void QueueCorrectionRepaint()
        {
            lock (_suggestGate)
            {
                if (_suggestRepaintQueued) return;
                _suggestRepaintQueued = true;
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(delegate
                {
                    List<string> arrivals;
                    lock (_suggestGate)
                    {
                        _suggestRepaintQueued = false;
                        arrivals = new List<string>(_synonymArrivals);
                        _synonymArrivals.Clear();
                    }
                    foreach (var word in arrivals) _composed.RefreshSynonyms(word);
                    // Les signalements n'ont pas bougé : seules les rangées
                    // de boutons se remplissent — reconstruction directe.
                    RebuildCorrectionPanel();
                }));
        }

        /// <summary>L'état du pont (initialisation, prêt, indisponible) vit
        /// dans le panneau Correction — pas de modal, pas de sablier (lot D).</summary>
        private void UpdateGrammarStatus()
        {
            RebuildCorrectionPanel();
        }

        public void RefreshProofing()
        {
            if (_spellChecker != null) _spellChecker.InvalidateLearned();
            // Les interrupteurs des Options du correcteur (batch 33) : chaque
            // vérificateur entre ou sort du pilote ; la grammaire et la
            // typographie partagent Grammalecte (le vol est annulé quand il sort).
            if (_spellChecker != null)
                SetCheckerPresent(_spellChecker, Settings.AppSettings.SpellEnabled);
            SetCheckerPresent(_repetitionChecker,
                Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleRepetitions);
            if (_styleChecker != null)
            {
                ApplyStyleSettings();
                SetCheckerPresent(_styleChecker,
                    Settings.AppSettings.StyleEnabled && _styleChecker.Wanted);
            }
            if (_grammarChecker != null)
            {
                _grammarChecker.GrammarEnabled = Settings.AppSettings.GrammarEnabled;
                _grammarChecker.TypographyEnabled = Settings.AppSettings.TypographyEnabled;
                SetCheckerPresent(_grammarChecker,
                    Settings.AppSettings.GrammarEnabled || Settings.AppSettings.TypographyEnabled);
            }
            // Les options ont pu changer sans que le texte change : le cache
            // oublie tout ET le différé en vol change de génération (les
            // réponses parties sous les anciennes options sont jetées).
            _checkHost.InvalidateCache();
            RunCheck();
        }

        /// <summary>Un vérificateur entre ou sort du pilote ; un DIFFÉRÉ qui
        /// sort voit son vol annulé (ses réponses n'ont plus de preneur).</summary>
        private void SetCheckerPresent(Correction.IChecker checker, bool wanted)
        {
            var present = _checkHost.Checkers.Contains(checker);
            if (wanted && !present) _checkHost.Add(checker);
            else if (!wanted && present)
            {
                _checkHost.Checkers.Remove(checker);
                if (checker is Correction.IDeferredChecker) _checkHost.CancelDeferred();
            }
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

        /// <summary>L'état du différé, VISIBLE MAIS DISCRET (batch 29,
        /// lot D) : une ligne en tête du panneau Correction — jamais un
        /// modal, jamais un sablier. Dit ce qui est encore en cours, avoue
        /// l'initialisation du premier lancement, et rend l'indisponibilité
        /// lisible sans boîte d'erreur. Nul quand il n'y a rien à dire.</summary>
        private UIElement BuildGrammarStatusLine()
        {
            if (!Settings.AppSettings.ProofEnabled || !ComposedActive)
                return null;
            string text = null;
            // Le pont sert la grammaire, la typographie (règles de
            // Grammalecte), le style morphologique et les synonymes : la
            // ligne d'état parle au nom de ce qui l'attend (revue du 13/09).
            var parts = new List<string>();
            if (Settings.AppSettings.GrammarEnabled) parts.Add("Grammaire");
            if (Settings.AppSettings.TypographyEnabled) parts.Add("Typographie");
            if (Settings.AppSettings.StyleEnabled && _styleChecker != null && _styleChecker.Wanted)
                parts.Add("Style");
            if (_synonyms != null && _synonyms.PendingCount > 0) parts.Add("Synonymes");
            if (parts.Count > 0 && _grammarBridge != null)
            {
                var state = _grammarBridge.State;
                var who = parts.Count == 1 ? parts[0]
                    : string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray())
                        + " et " + parts[parts.Count - 1].ToLowerInvariant();
                if (state == Correction.Grammalecte.BridgeState.Starting)
                    text = who + " : Grammalecte s'initialise (une seconde, "
                        + "au premier besoin seulement)…";
                else if (_checkHost.PendingDeferred > 0 || (_synonyms != null && _synonyms.PendingCount > 0))
                    text = who + " : analyse en cours…";
                else if (state == Correction.Grammalecte.BridgeState.Unavailable)
                    text = who + " : Grammalecte indisponible ("
                        + _grammarBridge.StateDetail
                        + ") — l'orthographe et les répétitions continuent.";
            }
            // Les suggestions d'orthographe se préparent en fond (batch 30) —
            // même registre que le différé : visible mais discret.
            if (text == null && PendingSuggestions > 0)
                text = "Orthographe : suggestions en préparation…";
            if (text == null) return null;
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4)
            };
        }

        /// <summary>Met un mot en file pour le calcul de ses suggestions en
        /// arrière-plan (batch 30). front : les mots du panneau AFFICHÉ
        /// passent devant le préchauffage de l'ouverture — l'utilisateur
        /// regarde ce chapitre-là.</summary>
        private void QueueSuggestion(string word, bool front)
        {
            if (_spellEngine == null || string.IsNullOrEmpty(word)) return;
            var start = false;
            lock (_suggestGate)
            {
                if (_suggestSeen.Add(word))
                {
                    if (front) _suggestQueue.Insert(0, word);
                    else _suggestQueue.Add(word);
                }
                else if (front)
                {
                    // Déjà en file (préchauffage) : la demande d'affichage
                    // le fait passer en tête. Déjà traité : Remove est nul.
                    if (_suggestQueue.Remove(word)) _suggestQueue.Insert(0, word);
                }
                if (!_suggestWorkerRunning && _suggestQueue.Count > 0)
                    start = _suggestWorkerRunning = true;
            }
            if (start)
                System.Threading.Tasks.Task.Factory.StartNew(SuggestWorker);
        }

        /// <summary>L'ouvrier de fond : dépile et chauffe le mémo du moteur
        /// (immuable, verrouillé côté mémo). Une repeinture coalescée par
        /// mot prêt — dix mots rapprochés, une reconstruction.</summary>
        private void SuggestWorker()
        {
            while (true)
            {
                string word;
                lock (_suggestGate)
                {
                    if (_suggestQueue.Count == 0)
                    {
                        _suggestWorkerRunning = false;
                        return;
                    }
                    word = _suggestQueue[0];
                    _suggestQueue.RemoveAt(0);
                }
                try { _spellEngine.Suggest(word); }
                catch { } // un mot pathologique se tait, la file continue
                QueueCorrectionRepaint();
            }
        }

        /// <summary>Mots encore en attente de suggestions — l'état « en
        /// préparation » du panneau et de l'indicateur d'ouverture.</summary>
        public int PendingSuggestions
        {
            get
            {
                lock (_suggestGate)
                    return _suggestQueue.Count + (_suggestWorkerRunning ? 1 : 0);
            }
        }

        /// <summary>Préchauffage d'ouverture (batch 30), UN paragraphe : la
        /// passe locale synchrone remplit le cache du pilote (clés par
        /// contenu — le clic sur le chapitre trouvera tout prêt), et les
        /// mots signalés partent en QUEUE de file de suggestions.</summary>
        public void WarmParagraph(TextDocument document, int index)
        {
            foreach (var finding in _checkHost.WarmParagraph(document, index, _styles))
                if (finding.CheckerId == "spelling" && finding.Word.Length > 0)
                    QueueSuggestion(finding.Word, false);
        }

        private void RebuildCorrectionPanel()
        {
            var changed = FindingsChanged;
            if (changed != null) changed();
            if (_corrList == null) return;
            _corrList.Children.Clear();
            var status = BuildGrammarStatusLine();
            if (status != null) _corrList.Children.Add(status);
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
            // Groupé PAR TYPE (13/09) : orthographe, grammaire, typographie,
            // style — un en-tête avec le compte, puis les fiches dans l'ordre
            // du texte. Jamais de troncature SILENCIEUSE : la queue est
            // annoncée.
            const int cap = 150;
            var shown = 0;
            foreach (var category in CategoryOrder)
            {
                var group = new List<Correction.Finding>();
                foreach (var finding in visible)
                    if (finding.Category == category) group.Add(finding);
                if (group.Count == 0) continue;
                // L'en-tête existe TOUJOURS (revue du 13/09) : une catégorie
                // coupée par le plafond dit « 150 sur 160 », une catégorie
                // entière au-delà dit qu'elle est là sans être montrée —
                // jamais de relevés invisibles sans en-tête.
                var room = Math.Max(0, Math.Min(group.Count, cap - shown));
                _corrList.Children.Add(CategoryHeader(category, room, group.Count));
                for (var i = 0; i < room; i++)
                {
                    _corrList.Children.Add(BuildFindingRow(group[i]));
                    shown++;
                }
            }
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

        /// <summary>L'en-tête d'un groupe du panneau (13/09) : pastille de
        /// la catégorie, libellé, compte — « (n) », « (n sur N) » si le
        /// plafond a coupé, « (N, au-delà des fiches affichées) » si rien
        /// du groupe n'est montré.</summary>
        private static UIElement CategoryHeader(Correction.FindingCategory category, int shown, int count)
        {
            var label = ComposedRenderer.CategoryLabel(category);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 2)
            };
            row.Children.Add(ComposedRenderer.FindingDot(ComposedRenderer.FindingPen(category), 8));
            row.Children.Add(new TextBlock
            {
                Text = label + (shown == count ? " (" + count + ")"
                    : shown == 0 ? " (" + count + ", au-delà des fiches affichées)"
                    : " (" + shown + " sur " + count + ")"),
                Foreground = Chrome.SoftText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        /// <summary>Le passage couvert par un signalement SANS Word (la
        /// grammaire vise une plage, pas un mot) — extrait du document
        /// vivant, borné à 40 caractères.</summary>
        private string ExcerptOf(Correction.Finding finding)
        {
            if (_item == null || finding.ParagraphIndex < 0
                || finding.ParagraphIndex >= _item.Document.Paragraphs.Count)
                return "…";
            var text = PivotEdit.FlatText(
                _item.Document.Paragraphs[finding.ParagraphIndex]);
            if (finding.Start < 0 || finding.Length <= 0
                || finding.End > text.Length)
                return "…";
            return text.Substring(finding.Start, Math.Min(finding.Length, 40));
        }

        /// <summary>Une fiche de signalement du panneau de droite (batch 28) :
        /// pastille + mot cliquable, message enroulé, puis suggestions et
        /// « Ignorer » — la colonne est étroite, tout s'empile.</summary>
        private UIElement BuildFindingRow(Correction.Finding finding)
        {
            var row = new StackPanel { Margin = new Thickness(0, 3, 0, 6) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(ComposedRenderer.FindingDot(ComposedRenderer.FindingPen(finding), 8));
            var excerpt = new TextBlock
            {
                Text = "« " + (finding.Word.Length > 0
                    ? finding.Word : ExcerptOf(finding)) + " »",
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
                Margin = new Thickness(14, 1, 0, 2),
                // Le détail long du signalement (Finding.Detail) se lit au
                // survol — le panneau reste court.
                ToolTip = string.IsNullOrEmpty(finding.Detail) ? null : finding.Detail
            });

            var buttons = new WrapPanel { Margin = new Thickness(14, 0, 0, 0) };
            // Suggestions PRÊTES seulement (batch 30) : le calcul se fait en
            // fond, la rangée se remplit à la repeinture coalescée. Les
            // synonymes prennent la casse du mot remplacé ; les autres
            // suggestions s'appliquent TELLES QUELLES (un mot appris
            // « d'Artagnan » garde sa casse — revue du 13/09).
            var suggestions = SuggestionsFor(finding, 3, true);
            var synonyms = finding.Suggests == Correction.SuggestionSource.Synonyms;
            for (var i = 0; i < suggestions.Count && i < 3; i++)
            {
                var findingRef = finding;
                var replacement = synonyms
                    ? Correction.Typography.KeepCase(finding.Word, suggestions[i]) : suggestions[i];
                var apply = SmallButton(replacement,
                    delegate { _composed.ApplySuggestion(findingRef, replacement); });
                apply.FontSize = 11;
                apply.Margin = new Thickness(0, 0, 4, 2);
                apply.ToolTip = "Remplacer par « " + replacement + " »";
                buttons.Children.Add(apply);
            }
            // « Ignorer » : un MOT (orthographe, répétition) se tait dans tout
            // le projet ; un indice de style morphologique (adverbe, verbe
            // terne) se tait ICI seulement — faire taire « vraiment » partout
            // éteindrait aussi son orthographe et ses répétitions (revue du
            // 13/09) ; sans mot, ici aussi.
            var ignoreRef = finding;
            var projectWide = finding.Word.Length > 0 && finding.CheckerId != "style";
            var ignore = SmallButton("Ignorer", delegate
            {
                if (projectWide)
                {
                    _checkHost.IgnoreInProject(ignoreRef.Word);
                    NotifyEdited(); // la liste du projet est persistée
                }
                else _checkHost.IgnoreHere(ignoreRef);
                RunCheck();
            });
            ignore.FontSize = 11;
            ignore.Margin = new Thickness(0, 0, 4, 2);
            ignore.ToolTip = projectWide
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
                MessageDialog.Show(Window.GetWindow(this),
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
        /// <summary>Remplace une plage plate dans la surface composée (une
        /// occurrence du document ouvert, b37) — un cran d'annulation LOCAL,
        /// comme une frappe. Rend faux si le composé n'est pas la surface.</summary>
        public bool ReplaceRange(int paragraph, int start, int length, string text)
        {
            if (_item == null || !ComposedActive) return false;
            _composed.ReplaceRange(paragraph, start, length, text);
            return true;
        }

        /// <summary>Sélectionne une plage plate du pivot dans la surface
        /// composée (une occurrence de la recherche projet, b37) — le composé
        /// est réveillé s'il dormait (sauf compatibilité classique).</summary>
        public void GoToRange(int paragraph, int start, int end)
        {
            if (_item == null) return;
            if (!ComposedActive && !Settings.AppSettings.ClassicCompatibility) SetComposition(true);
            if (!ComposedActive) return;
            _composed.SelectRange(paragraph, start, end);
        }

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

            // Élargir la couche décale les pages (colonnes centrées) : les
            // règles doivent suivre APRÈS la passe de mise en page — sinon
            // elles mentent jusqu'au prochain défilement (b43).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRulers));

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
            _composed.Edited += delegate { NotifyEdited(); };
            _composed.NoteEditingStarted += delegate(string id) { _lastNoteId = id; };
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
            _composed.FindingIgnoreRule += delegate(Correction.Finding finding)
            {
                _checkHost.IgnoreRule(finding.RuleId);
                NotifyEdited(); // Project.IgnoredRules vit dans le .plot (v10)
                RunCheck();     // filtré après cache : aucune invalidation
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
            // ClipToBounds (13/09) : une règle dessine au-delà de sa bande
            // quand on défile (ses graduations suivaient la page jusque sur
            // le ruban) — WPF ne rogne pas OnRender de lui-même.
            _rulerV = new RulerView(true)
            {
                Width = RulerView.Thickness,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed,
                ClipToBounds = true
            };
            _rulerH = new RulerView(false)
            {
                Height = RulerView.Thickness,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                ClipToBounds = true
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
            var oldZoom = _zoom;
            _zoom = Math.Max(0.5, Math.Min(3.0, factor));
            _page.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001
                ? null : new ScaleTransform(_zoom, _zoom);
            // Miroir classique : même ancrage au centre de la vue (b34).
            if (_scroller != null && !ComposedActive && _scroller.ViewportHeight > 0
                && Math.Abs(oldZoom - _zoom) > 0.0001)
            {
                var anchorY = _scroller.ViewportHeight / 2;
                var contentY = (_scroller.VerticalOffset + anchorY) / oldZoom;
                _scroller.UpdateLayout();
                _scroller.ScrollToVerticalOffset(Math.Max(0, contentY * _zoom - anchorY));
            }
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
            // Fermer ou changer de projet n'attend jamais le différé (lot A).
            _checkHost.CancelDeferred();
            // « Ignorer cette règle » vit et se sauve avec le projet (v10).
            _checkHost.IgnoredRules = project != null
                ? project.IgnoredRules : new List<string>();
            // Le dictionnaire personnel du projet aussi — et le cache des
            // verdicts repart de zéro (autre projet, autre connaissance).
            if (_spellChecker != null)
            {
                _spellChecker.ProjectWords = project != null
                    ? project.Lexicon : new List<Model.LexiconEntry>();
                _spellChecker.InvalidateLearned();
            }
            _checkHost.InvalidateCache();
        }

        /// <summary>Le bouton « Plan » suit l'écrit ouvert (batch 35).</summary>
        public void RefreshPlanButton()
        {
            if (_planBtn == null) return;
            var plan = _item == null || PlanLocator == null ? null : PlanLocator(_item);
            _planBtn.Visibility = plan != null ? Visibility.Visible : Visibility.Collapsed;
            if (plan != null) _planBtn.ToolTip = "Ouvrir le plan « " + plan.Title + " » (une colonne raconte cet écrit)";
        }

        public void LoadItem(BinderItem item)
        {
            // Changer d'écrit annule le différé en vol : les indices de
            // paragraphes de l'ancien document n'ont plus de sens (lot A).
            _checkHost.CancelDeferred();
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
                MessageDialog.Show(Window.GetWindow(this),
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
                    OpenNote(_item.Document.Footnotes[_item.Document.Footnotes.Count - 1].Id);
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
            // Pages composées (batch 33) : les notes s'éditent EN PLACE au bas
            // de leur page — le panneau du bas ne sert plus qu'au classique.
            _notesBar.Visibility = ordered.Count == 0 || _calm || ComposedActive
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
