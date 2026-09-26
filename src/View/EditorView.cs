using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>L'éditeur d'écrits : le ruban, la barre de recherche, le
    /// panneau de correction et LA surface d'édition — les pages composées
    /// (ComposedView), qui écrivent directement dans le pivot. L'ancien
    /// RichTextBox (le « mode classique », gelé au batch 26) a été retiré le
    /// 13/09 : plus de Commit, le pivot est toujours le modèle vivant.
    /// L'annulation de texte est celle de la surface ; l'historique de la
    /// Pile est à part.</summary>
    public partial class EditorView : DockPanel
    {
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private BinderItem _item;
        private Project _project; // image store; null until a project is loaded
        private PageSetup _pageSetup = new PageSetup();
        private bool _syncing;

        private ComposedView _composed;        // la surface d'édition (pages composées)
        private RulerView _rulerH, _rulerV;    // règles cm (Ctrl+R)
        private TextBox _trackingBox;          // champ d'approche (em/1000)
        // L'axe d'affichage (batch 26, lot B.2) : Pages / Brouillon / Calme —
        // même moteur, même pivot, seule la présentation change.
        private ToggleButton _pagesViewBtn, _draftViewBtn, _calmViewBtn;
        private bool _draftView;
        private double _zoom = 1.0;
        private bool _showMarks; // ¶ formatting marks

        public event Action<int> ZoomStepRequested; // +10 / -10 (percent)
        public event Action PageSetupChanged;       // edited from the Mise en page tab
        public event Action<int, int> PageInfoChanged; // caret page, page count

        /// <summary>Pages du livre précédant ce document (0 hors livre) :
        /// folio affiché et parité des marges miroir de la composition.</summary>
        public int FolioOffset;

        /// <summary>Décor en-tête/pied du document (fixé par la coquille à
        /// l'ouverture, recalculé après édition via le menu Gabarit).</summary>
        public PageDecor Decor;
        public event Action<bool> MarksToggled;     // ¶ button
        public event Action StylesRequested;        // « Gestion des styles » button

        private ComboBox _styleCombo, _sizeCombo;
        private FontPicker _fontCombo;          // le sélecteur de police partagé (0.50.0)
        private ToggleButton _smallCapsBtn;     // petites majuscules (0.50.0)
        private System.Windows.Controls.Primitives.Popup _specialDrawer; // le tiroir des caractères spéciaux (0.50.0)
        private bool _sizeArrowNav, _sizeDropDownChoice; // la taille : flèches sans ouvrir, choix dans la liste
        private string _lastAppliedFont;        // l'aperçu puis le choix définitif n'appliquent qu'une fois
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

        private ToggleButton _annVisibleBtn; // « Visibles » de l'onglet Révision

        // ---- correction (batch 26) : le pilote, ses signalements, son panneau
        private readonly Correction.CheckerHost _checkHost = new Correction.CheckerHost();
        private Correction.SpellChecker _spellChecker; // null sans dictionnaire
        private readonly Correction.RepetitionChecker _repetitionChecker = new Correction.RepetitionChecker();
        // b45 : les verbes de dialogue (incises), et la racine des mots pour
        // les répétitions, les incises et le bilan (moteur d'orthographe).
        private readonly Correction.DialogueChecker _dialogueChecker = new Correction.DialogueChecker();
        private Func<string, string> _lemma;
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
        public event Action<LexiconEntry, bool> DefinitionRequested; // clic droit › « Afficher la définition » (18/09)

        public EditorView()
        {
            _draftView = Settings.AppSettings.DraftView; // avant le ruban
            BuildFormatBar();
            BuildSearchBar();
            BuildCorrectionBar();
            BuildPage();

            // Le pilote de correction (batch 26) : les vérificateurs actifs,
            // les ignorés globaux, et la cadence — un debounce de 600 ms,
            // jamais à chaque touche. La passe complète coûte 46 ms sur
            // 50 000 mots (mesure C5) : le fil UI suffit largement.
            // Options du correcteur (batch 33) : le style (répétitions) ne
            // s'allume plus qu'à la demande.
            _repetitionChecker.Radius = Settings.AppSettings.RepetitionRadius;
            if (Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleRepetitions)
                _checkHost.Add(_repetitionChecker);
            if (Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleDialogue)
                _checkHost.Add(_dialogueChecker);
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
                // La racine des mots (b45) : la plus courte des entrées du
                // dictionnaire qui acceptent la forme — cheval pour chevaux.
                _lemma = delegate(string word)
                {
                    var stems = spellEngine.Stems(word);
                    return stems.Count > 0 ? stems[0] : null;
                };
                _repetitionChecker.Lemma = _lemma;
                _dialogueChecker.Lemma = _lemma;
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
            // La typographie à la frappe (b45) : les règles retenues, ou rien.
            _composed.LiveTypography = Settings.AppSettings.TypographyLiveEnabled
                ? Settings.AppSettings.TypographyLive : null;
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

        /// <summary>Vrai quand un écrit est attaché à la surface composée
        /// (faux sans écrit, ou le temps d'une recomposition ratée).</summary>
        public bool ComposedActive
        {
            get { return _composed != null && _composed.HasItem
                    && _composed.Visibility == Visibility.Visible; }
        }

        /// <summary>(Ré)attache l'écrit ouvert à la surface composée avec le
        /// réglage de page de l'AFFICHAGE choisi (Pages / Brouillon / Calme) —
        /// même moteur, même pivot, seule la présentation change.</summary>
        private void AttachComposed()
        {
            if (_item == null) return;
            try
            {
                _composed.SetZoom(_zoom);
                // Brouillon : même moteur, réglage de page dérivé — colonne
                // continue sans décor ni folio (voir DraftSetup).
                // Mode calme (13/09) : toujours la même feuille — A4 nue,
                // ni décor, ni folio, ni guides — quel que soit l'axe choisi.
                var plain = _draftView || _calm;
                _composed.FolioOffset = plain ? 0 : FolioOffset;
                _composed.Decor = plain ? null : Decor;
                _composed.Attach(_item, _styles.EffectiveFor(_item), // le séparateur du livre, s'il en a un (22/09)
                    _calm ? CalmSetup(_pageSetup) : _draftView ? DraftSetup(_pageSetup) : _pageSetup,
                    _project);
                _composed.Visibility = Visibility.Visible;
                _composed.SetFormattingMarks(_showMarks); // l'état du ¶ suit la surface
                _composed.FocusSurface();
                RebuildAnnotationsPanel();
                RunCheck(); // la surface composée s'ouvre vérifiée
            }
            catch (Exception error)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "Composition impossible :\n" + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
                _composed.Detach();
                RunCheck(); // sans surface : panneau et ondulés s'éteignent
            }
        }

        /// <summary>Reflects the page setup in the « Mise en page » tab.</summary>
        private void SyncPageTab()
        {
            if (_marginsCombo == null) return;
            _syncingPage = true;
            try
            {
                if (_leadingCombo != null)
                {
                    var current = _item != null && _item.Document != null ? _item.Document.LineSpacing : 1;
                    var best = 0;
                    for (var i = 1; i < LeadingFactors.Length; i++)
                        if (Math.Abs(LeadingFactors[i] - current) < Math.Abs(LeadingFactors[best] - current)) best = i;
                    _leadingCombo.SelectedIndex = best;
                }
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
                if (ComposedActive) pages = _composed.PageRects(_rulerH);
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
            if (_composed != null) _composed.SetFormattingMarks(visible);
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
                "Insère un [[lien]] vers une fiche ou un écrit (Ctrl+K) : l'expression "
                + "sélectionnée reste le texte du lien — Ctrl+clic sur le lien pour l'ouvrir");
            link.Click += delegate
            {
                var handler = LinkRequested;
                if (handler != null) handler();
            };
            // Afficher/Masquer les liens (18/09) : montrés, les marques
            // [[…]] apparaissent et le texte du lien est en évidence, un
            // clic l'ouvre ; masqués (défaut), le texte seul, Ctrl+clic.
            _linksBtn = OneLineToggle("apercu", "Afficher les liens",
                "Montrer les marques [[…]] des liens et leur texte en évidence — "
                + "un clic sur un lien l'ouvre ; masqués, seul Ctrl+clic l'ouvre");
            _linksBtn.IsChecked = Settings.AppSettings.ShowLinks;
            _linksBtn.Click += delegate { SetShowLinks(_linksBtn.IsChecked == true); };
            panel.Children.Add(Stacked(link, _linksBtn));
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

        /// <summary>Ouvre une note pour édition, en place au bas de sa page.</summary>
        private void OpenNote(string id)
        {
            _lastNoteId = id;
            if (ComposedActive) _composed.EditNote(id);
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
            if (_item == null || !ComposedActive) return;
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

            _corrDetailsBtn = OneLineToggle("file-magnifying-glass", "Détails de correction",
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
                if (ComposedActive && !_composed.ToggleNoProofSelection())
                    MessageDialog.Show(Window.GetWindow(this),
                        "Sélectionnez d'abord le passage à soustraire.",
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
            var report = OneLine("exam-bold", "Bilan de style",
                "Un compte rendu de l'écrit ouvert, en mots simples : longueur des phrases, "
                + "rythme, dialogue, variété des mots, tics, paragraphes à revoir");
            report.Click += delegate { ShowStyleReport(); };
            panel.Children.Add(Stacked(options, report));
            return panel;
        }

        /// <summary>Le bilan de style (b45) de l'écrit ouvert : les mesures
        /// du texte, les relevés de la dernière passe, l'inventaire des
        /// incises — puis la fenêtre qui raconte tout ça.</summary>
        private void ShowStyleReport()
        {
            if (_item == null) return;
            var report = Correction.StyleReport.Compute(_item.Document, _findings, _lemma,
                _dialogueChecker.Inventory(_item.Document));
            report.DeferredPending = _checkHost.PendingDeferred > 0;
            StyleReportWindow.Show(Window.GetWindow(this), _item.Title, report);
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
            _repetitionChecker.Radius = Settings.AppSettings.RepetitionRadius;
            SetCheckerPresent(_repetitionChecker,
                Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleRepetitions);
            SetCheckerPresent(_dialogueChecker,
                Settings.AppSettings.StyleEnabled && Settings.AppSettings.StyleDialogue);
            _composed.LiveTypography = Settings.AppSettings.TypographyLiveEnabled
                ? Settings.AppSettings.TypographyLive : null;
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

        /// <summary>Le correcteur travaille (0.50.0, l'anneau d'activité de
        /// la fenêtre) : Grammalecte s'initialise ou analyse en différé, des
        /// synonymes ou des suggestions d'orthographe se préparent — le même
        /// prédicat que la ligne d'état du panneau Correction.</summary>
        public bool IsProofingBusy
        {
            get
            {
                if (_grammarBridge != null && _grammarBridge.State == Correction.Grammalecte.BridgeState.Starting) return true;
                if (_checkHost != null && _checkHost.PendingDeferred > 0) return true;
                if (_synonyms != null && _synonyms.PendingCount > 0) return true;
                return PendingSuggestions > 0;
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

        /// <summary>Ids d'annotations dans l'ordre du texte (le pivot est
        /// toujours vivant).</summary>
        private List<string> AnnotationOrderLive()
        {
            return _item == null ? new List<string>() : _item.Document.AnnotationOrder(false);
        }

        /// <summary>Le passage annoté tel qu'affiché (extrait de la bulle).</summary>
        private string AnnotatedTextLive(string id)
        {
            return _item.Document.AnnotatedText(id);
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
            var anchored = ComposedActive && _composed.AnnotateSelection(annotation.Id);
            if (!anchored)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "Sélectionnez d'abord le passage à annoter — ou cliquez une image.",
                    "Révision", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _item.Document.Annotations.Add(annotation);
            NotifyEdited();
            RebuildAnnotationsPanel();
            FocusAnnotation(annotation.Id);
        }

        /// <summary>Navigation ruban : va à l'annotation suivante/précédente
        /// après celle du caret (ou la première/dernière).</summary>
        private void NavigateAnnotation(int direction)
        {
            var order = AnnotationOrderLive();
            if (order.Count == 0) return;
            var current = ComposedActive ? _composed.AnnotationAtCaret() : null;
            var index = current == null ? -1 : order.IndexOf(current);
            index = index < 0
                ? (direction > 0 ? 0 : order.Count - 1)
                : (index + direction + order.Count) % order.Count;
            GoToAnnotation(order[index]);
        }

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
        /// composée (une occurrence de la recherche projet, b37).</summary>
        public void GoToRange(int paragraph, int start, int end)
        {
            if (_item == null || !ComposedActive) return;
            _composed.SelectRange(paragraph, start, end);
        }

        /// <summary>Sélectionne le passage d'une annotation et l'amène à l'écran.</summary>
        public void GoToAnnotation(string id)
        {
            if (!ComposedActive) return;
            _composed.GoToAnnotation(id);
            _composed.FocusSurface();
        }

        /// <summary>Résout/rouvre : la teinte s'éteint ou revient, l'ancre reste.</summary>
        private void ToggleAnnotationResolved(Annotation annotation)
        {
            annotation.Resolved = !annotation.Resolved;
            if (ComposedActive) _composed.RefreshAnnotation(annotation.Id);
            NotifyEdited();
            RebuildAnnotationsPanel();
        }

        /// <summary>Supprime l'annotation : commentaire ET ancres.</summary>
        private void DeleteAnnotation(Annotation annotation)
        {
            if (_activeBubbleId == annotation.Id) _activeBubbleId = null;
            _item.Document.Annotations.Remove(annotation);
            if (ComposedActive) _composed.ClearAnnotation(annotation.Id);
            NotifyEdited();
            RebuildAnnotationsPanel();
        }

        /// <summary>Applique le réglage « annotations visibles » : recomposition
        /// (la teinte vient du moteur) et bulles.</summary>
        private void ApplyAnnotationVisibility()
        {
            if (_item == null || !ComposedActive) return;
            _composed.RefreshComposition();
            RebuildAnnotationsPanel();
        }

        /// <summary>Reprogramme les bulles d'annotation (le panneau du bas a
        /// disparu au B.4 : l'édition vit dans les bulles).</summary>
        public void RebuildAnnotationsPanel()
        {
            // Les annotations s'éditent dans leurs bulles, à droite des pages.
            // Cette méthode — appelée par tous les chemins historiques —
            // reprogramme les bulles après le layout (la géométrie des pages
            // doit être posée).
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

        /// <summary>Reconstruit les bulles de commentaire à droite des pages.
        /// Chaque bulle est posée à la hauteur de son passage, empilée sans
        /// chevauchement, reliée à la page par un filet or. force : passe
        /// outre la garde anti-vol de focus (dépliage/repli volontaire).</summary>
        private void RebuildAnnotationBubbles(bool force)
        {
            if (_composed == null) return;
            var layer = _composed.AnnotationBubbleLayer;
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
            layer.Children.Clear();
            layer.Width = 0;
            _activeBubbleEditor = null;
            if (_item == null || _calm || !ComposedActive
                || !Settings.AppSettings.ShowAnnotations) return;
            var order = AnnotationOrderLive();
            if (order.Count == 0) return;

            const double bubbleWidth = 190;
            var gold = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
            var pageRight = _composed.PagesRightX();
            if (pageRight <= 0) return;

            var lastBottom = 0.0;
            foreach (var id in order)
            {
                var annotation = _item.Document.FindAnnotation(id);
                if (annotation == null) continue;
                var y = _composed.AnnotationAnchorY(id); // en colonne
                if (y < 0) continue;
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
        /// focus la quitte. layer : la couche de la colonne composée.</summary>
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
            _composed = new ComposedView { Visibility = Visibility.Collapsed };
            _composed.Edited += delegate { NotifyEdited(); };
            _composed.MarksRequested += delegate
            {
                // Le raccourci « Caractères d'impression » (22/09) : même
                // chemin que le clic sur le bouton du ruban.
                if (_marksBtn == null) return;
                _marksBtn.IsChecked = !(_marksBtn.IsChecked == true);
                var handler = MarksToggled;
                if (handler != null) handler(_marksBtn.IsChecked == true);
            };
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
            _composed.DefinitionRequested += delegate(LexiconEntry entry, bool projectScope)
            {
                var handler = DefinitionRequested;
                if (handler != null) handler(entry, projectScope);
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
                SyncImageTab(); // l'onglet Image suit l'image sélectionnée (0.50.0)
            };
            _composed.ImageSelectionChanged += delegate
            {
                SyncImageTab();
                var handler = ImageSelectionChanged;
                if (handler != null) handler();
            };

            var centerHost = new Grid();
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
            _composed.ScrollChanged += delegate { UpdateRulers(); };
            Children.Add(centerHost); // last child fills the remaining space

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
            if (_composed != null) _composed.SetZoom(_zoom);
        }

        // ============================================================= page setup

        /// <summary>Applies the project's page setup to the editing surface:
        /// paper size, real margins, optional margin guides, hyphenation.</summary>
        public void ApplyPageSetup(PageSetup setup)
        {
            if (setup != null) _pageSetup = setup;
            SyncPageTab();
            if (ComposedActive) _composed.RefreshComposition();
        }

        /// <summary>Toggles a manual page break above the caret's paragraph.</summary>
        public void InsertPageBreak()
        {
            if (_item == null || !ComposedActive) return;
            _composed.TogglePageBreak();
        }

        // ============================================================= item lifecycle

        public void SetStyleSheet(StyleSheet styles)
        {
            _styles = styles;
            RefreshStyleCombo();
        }

        /// <summary>Le combo des styles pour l'écrit ouvert (22/09) : les
        /// globaux, ceux de son livre, les siens — jamais les séparateurs —
        /// chacun dans sa police, avec l'icône de sa portée.</summary>
        private void RefreshStyleCombo()
        {
            if (_styles == null || _styleCombo == null) return;
            _syncing = true;
            _styleCombo.Items.Clear();
            foreach (var style in _styles.VisibleFor(_item))
            {
                var label = new TextBlock
                {
                    Text = style.Name,
                    FontFamily = new FontFamily(style.FontFamily),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = StylesPanel.ScopeIcon(style, 10);
                if (icon != null) row.Children.Add(icon);
                row.Children.Add(label);
                _styleCombo.Items.Add(new ComboBoxItem { Content = row, Tag = style.Id });
            }
            _syncing = false;
        }

        /// <summary>Le libellé d'une entrée du combo des styles.</summary>
        private static TextBlock StyleLabelOf(ComboBoxItem entry)
        {
            var row = entry == null ? null : entry.Content as StackPanel;
            if (row == null) return null;
            foreach (var child in row.Children)
            {
                var label = child as TextBlock;
                if (label != null) return label;
            }
            return null;
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
            RefreshStyleCombo(); // les styles de CET écrit (portées, 22/09)
            SyncPageTab();
            HideSearch();
            AttachComposed();
            // Les combos du ruban (style, police, taille) reflètent le caret
            // dès l'ouverture.
            SyncToolbarComposed();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRulers));
        }

        /// <summary>Re-renders the current item (after the style sheet changed).</summary>
        public void Reload()
        {
            if (_item == null || !ComposedActive) return;
            _composed.FolioOffset = FolioOffset;
            _composed.Decor = Decor;
            _composed.Attach(_item, _styles, _pageSetup, _project); // recompose
        }

        /// <summary>Detaches the editor from its item (selection moved to a
        /// folder, or the item was undone out of existence).</summary>
        public void Clear()
        {
            _item = null;
            if (_composed != null)
            {
                _composed.Detach();
                _composed.Visibility = Visibility.Collapsed;
            }
            if (_checkTimer != null) _checkTimer.Stop();
            _findings = new List<Correction.Finding>();
            RebuildCorrectionPanel();
            SyncPageTab();
            RebuildAnnotationsPanel();
            HideSearch();
        }

        public void FocusEditor()
        {
            if (ComposedActive) _composed.FocusSurface();
        }

        /// <summary>Text undo/redo, claimed by the composed surface when it
        /// holds the item. Lets the window's Ctrl+Z / Ctrl+Y route to the
        /// text first and to the Binder history otherwise.</summary>
        public bool TryUndo()
        {
            return _item != null && ComposedActive && _composed.Undo();
        }

        public bool TryRedo()
        {
            return _item != null && ComposedActive && _composed.Redo();
        }

        public string PlainText()
        {
            return _item == null ? "" : _item.Document.ToPlainText();
        }

        /// <summary>Les statistiques de l'écrit ouvert depuis sa composition
        /// (22/09) : incrémentales — un paragraphe modifié se recompte, les
        /// autres gardent leur compte. Null si rien n'est composé.</summary>
        public Correction.TextStats CompositionStats()
        {
            if (_item == null || !ComposedActive) return null;
            var composition = _composed.CurrentComposition;
            return composition == null ? null : composition.Stats();
        }

        /// <summary>Le nombre de pages de l'écrit ouvert tel qu'il s'imprime
        /// (22/09) : celui de la composition à l'écran, sauf en Brouillon ou
        /// en mode calme (une autre feuille) — null alors.</summary>
        public int? PrintPageCount
        {
            get
            {
                if (_item == null || !ComposedActive || _draftView || _calm) return null;
                var composition = _composed.CurrentComposition;
                return composition == null ? (int?)null : composition.Pages.Count;
            }
        }

        private void NotifyEdited()
        {
            var handler = Edited;
            if (handler != null) handler();
            ScheduleCheck(); // la correction suit l'édition, au debounce
        }

        // ============================================================= formatting

        private void OnStyleComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null) return;
            var chosen = _styleCombo.SelectedItem as ComboBoxItem;
            if (chosen == null) return;
            var style = _styles.Find((string)chosen.Tag);
            if (!ComposedActive) return;
            _composed.ApplyStyle(style.Id);
            _composed.FocusSurface();
        }

        /// <summary>Une police choisie au sélecteur (0.50.0) : appliquée à la
        /// sélection (ou au format d'insertion) une seule fois par nom —
        /// l'aperçu des flèches puis le choix définitif ne font qu'un cran
        /// d'annulation ; le clavier ne repart au texte qu'au choix définitif.</summary>
        private void OnFontChosen(string name, bool preview)
        {
            if (_syncing || _item == null || !ComposedActive) return;
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            if (name != _lastAppliedFont)
            {
                _lastAppliedFont = name;
                _composed.ApplyFont(name);
            }
            if (!preview) _composed.FocusSurface();
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
            if (!ComposedActive) return;
            _composed.ApplySizePx(sizePt * 4.0 / 3.0);
            _composed.FocusSurface();
        }

        /// <summary>La taille (0.50.0) : la liste ouverte ou les flèches
        /// appliquent ; l'autocomplétion de la frappe ne fait rien, Entrée
        /// décide (ApplyTypedSize).</summary>
        private void OnSizeComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _sizeCombo.SelectedItem == null || !ComposedActive) return;
            if (_sizeArrowNav)
            {
                _sizeArrowNav = false; // aperçu : le clavier reste dans le champ
                _composed.ApplySizePx((int)_sizeCombo.SelectedItem * 4.0 / 3.0); // pt -> px
                return;
            }
            if (_sizeCombo.IsDropDownOpen) { _sizeDropDownChoice = true; return; } // tranché à la fermeture
        }

        /// <summary>Inserts a horizontal rule on its own paragraph, below the
        /// caret's one; the caret lands on a fresh paragraph after it.</summary>
        public void InsertRule()
        {
            if (_item == null || !ComposedActive) return;
            _composed.InsertElementAtCaret(new TextRun { IsRule = true });
        }

        /// <summary>Insère le séparateur de scène (22/09) : un paragraphe
        /// portant le STYLE « separator » (celui du livre s'il en a un, sinon
        /// le global) et son contenu, puis un paragraphe de suite dans le
        /// style d'avant. Rien n'est posé en écart local : changer le style
        /// change tous les séparateurs déjà insérés.</summary>
        public void InsertSeparator()
        {
            if (_item == null || !ComposedActive) return;
            var separator = _styles.SeparatorFor(_item);
            var text = separator.Content ?? "***";
            int before, beforeOffset;
            _composed.GetCaret(out before, out beforeOffset);
            var origin = before < _item.Document.Paragraphs.Count ? _item.Document.Paragraphs[before] : null;
            var previous = origin != null ? origin.StyleId : "body";
            if (previous == StyleSheet.SeparatorId) previous = "body";
            // Les écarts locaux du paragraphe coupé (alignement, décalage)
            // reviennent sur sa suite — ApplyStyle les efface (revue 22/09).
            var align = origin == null ? null : origin.AlignOverride;
            var indent = origin == null ? null : origin.Indent;
            var firstIndent = origin == null ? null : origin.FirstIndent;

            _composed.InsertParagraphBreak();
            _composed.ApplyStyle(StyleSheet.SeparatorId);
            _composed.TypeText(text);
            _composed.InsertParagraphBreak();
            _composed.ApplyStyle(previous);
            _composed.RestoreOverrides(align, indent, firstIndent);
            _composed.FocusSurface();
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
                if (!ComposedActive) return;
                // L'image naît attachée à sa ligne, centrée, réduite à la
                // colonne, et sélectionnée (0.50.0) : poignées et onglet Image.
                _composed.InsertImageAtCaret(new TextRun
                {
                    ImageId = id,
                    Image = new ImageLayout { Name = System.IO.Path.GetFileName(dialog.FileName) }
                });
                _composed.FocusSurface();
            }
            catch (Exception error)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "Impossible d'insérer l'image :\n" + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<string> PivotFootnoteOrder()
        {
            var ordered = new List<string>();
            foreach (var paragraph in _item.Document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null) ordered.Add(run.FootnoteId);
            return ordered;
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
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public void HideSearch()
        {
            if (_searchBar.Visibility == Visibility.Collapsed) return;
            _searchBar.Visibility = Visibility.Collapsed;
            _searchCurrent = null;
            if (ComposedActive) _composed.FocusSurface();
        }

        private StringComparison Comparison()
        {
            return _caseCheck.IsChecked == true
                ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        }

        private void FindNext()
        {
            TryFindNextComposed(true);
        }

        /// <summary>La recherche pivot : PivotSearch trouve, la vue sélectionne.</summary>
        private bool TryFindNextComposed(bool wrap)
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null || !ComposedActive) return false;
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

        private void ReplaceCurrent()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || !ComposedActive) return;
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
        }

        private void ReplaceAll()
        {
            var needle = _searchBox.Text;
            if (string.IsNullOrEmpty(needle) || _item == null || !ComposedActive) return;
            var matches = PivotSearch.FindAll(_item.Document, needle,
                _caseCheck.IsChecked == true, _wholeWordCheck.IsChecked == true);
            var replaced = _composed.ReplaceAll(matches, _replaceBox.Text);
            _searchCurrent = null;
            _searchInfo.Text = replaced == 0 ? "Aucun résultat"
                : replaced == 1 ? "1 remplacement" : replaced + " remplacements";
        }

        // ============================================================= wiki links

        /// <summary>Insère un [[lien]] au curseur : l'expression sélectionnée
        /// reste le texte du lien (« [[Cible|expression]] »), jamais
        /// remplacée par le nom de la fiche. Insérer un lien les montre.</summary>
        public void InsertWikiLink(string title)
        {
            if (_item == null || string.IsNullOrEmpty(title) || !ComposedActive) return;
            _composed.TypeText(Links.Markup(title, _composed.SelectedPlainText()));
            if (!Settings.AppSettings.ShowLinks) SetShowLinks(true);
            _composed.FocusSurface();
        }

        private ToggleButton _linksBtn;

        /// <summary>Afficher/Masquer les liens (18/09) : réglage de session,
        /// bouton du ruban, recomposition de la surface.</summary>
        public void SetShowLinks(bool shown)
        {
            Settings.AppSettings.ShowLinks = shown;
            if (_linksBtn != null) _linksBtn.IsChecked = shown;
            if (ComposedActive) _composed.RefreshComposition();
        }

        public bool ShowLinks { get { return Settings.AppSettings.ShowLinks; } }

        // ============================================================= footnotes

        public void InsertFootnote()
        {
            if (_item == null || !ComposedActive) return;
            _composed.InsertFootnoteAtCaret();
            if (_item.Document.Footnotes.Count > 0)
                OpenNote(_item.Document.Footnotes[_item.Document.Footnotes.Count - 1].Id);
        }

    }
}
