using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Marabook.Correction;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook
{
    /// <summary>The shell window: Binder (left) | rich editor (center) |
    /// inspector (right), menu bar on top, status bar below.</summary>
    public partial class MainWindow : Window, Extensions.IModuleHost
    {
        public const string AppName = "Marabook";
        public const string AppVersion = "0.50.0-beta";

        private Project _project;
        /// <summary>Le .plot passé en argument, ouvert au Loaded (revue 22/09).</summary>
        public string PendingOpen;
        private string _path;
        private bool _dirty;
        private readonly HistoryManager _history = new HistoryManager();

        private BinderView _binder;
        private ColumnDefinition _binderCol, _inspectorCol;
        private GridSplitter _binderSplit, _inspectorSplit;

        private EditorView _editor;
        private SheetView _sheetView;
        private View.SheetLibraryView _sheetLibrary; // catégorie « Fiches » (b31)
        private CorkboardView _corkboard;
        private View.BookView _bookView;
        private View.DictionaryView _dictionaryView; // racine « Dictionnaire » (b33)
        private View.HomeView _homeView;             // racine « Accueil » (b41)
        private bool _restoringSelection;            // sélection initiale à l'ouverture : pas de récent réécrit (A2)
        private View.PlanView _planView;             // un plan (b35)
        private Grid _mindMapHost;                   // une carte mentale : l'éditeur du module Mental-o (22/09)
        private Extensions.IMindMapEditor _mindMapEditor;
        private BinderItem _mindMapItem;             // la carte chargée dans l'éditeur
        private TextBlock _inspPlanLink;             // « Plan : … » d'un livre/dossier (b35)
        private StackPanel _planSection;             // options d'un plan (b35)
        private TextBox _planColumnWord;
        private View.TemplateView _templateView;
        private bool _navigating; // garde anti-sélection-fantôme (voir OnBinderSelection)
        private MediaView _mediaView;
        private JournalView _journalView;
        private bool _journalOpen; // le journal masque l'inspecteur
        private Grid _centerHost; // hôte du toast de célébration
        private StackPanel _toastHost;          // succès : toasts en bas à droite de la fenêtre (12/09)
        private BusyIndicator _busy;            // l'anneau d'activité de la ligne des menus (0.50.0)
        private DispatcherTimer _achievementTimer; // vérification coalescée
        private DispatcherTimer _minuteTimer;   // les succès de durée (ouverture, sauvegarde, page blanche)
        private long _lastSavedBytes;           // taille du dernier .plot écrit
        private readonly DateTime _appStart = DateTime.Now;
        private DateTime? _dirtySince;          // premier MarkDirty depuis la dernière sauvegarde manuelle
        private readonly HashSet<string> _deepCleanCandidates = new HashSet<string>(); // écrits vus à > 100 fautes
        private UIElement _menuBar, _statusBar;
        private Border _calmExit;  // bouton flottant de sortie du mode calme
        private bool _calmMode;
        private TextBlock _placeholder;
        private BinderItem _current;

        private Border _inspector;
        private Border _correctionHost; // le panneau de correction (batch 28)
        private Border _searchHost;     // le panneau de recherche du projet (batch 37)
        private View.SearchPanel _searchPanel;
        private Border _versionsHost;   // le panneau Versions (batch 38)
        private Border _pinnedHost;     // l'épinglé sur le côté (batch 47)
        private View.PinnedPanel _pinnedPanel;
        private BinderItem _sidePin;    // l'item épinglé (Project.SidePinId résolu), null = rien
        private DispatcherTimer _pinTimer; // le miroir suit la frappe, un peu après elle
        private StackPanel _presenceSection; // « Personnages présents » d'un écrit (b47)
        private StackPanel _presencePanel;
        // Le rail (batch 39) : quatre onglets à bascule, un par panneau de
        // droite, à droite de tout. Connus à la compilation — pas de registre.
        private const double RailWidth = 40;
        private Border _rail;
        private ColumnDefinition _railCol;
        private StackPanel _railStack;
        private RightPanel[] _railOffered; // les onglets bâtis (RightPanels.Offered)
        private readonly Dictionary<RightPanel, Border> _railTabs = new Dictionary<RightPanel, Border>();
        private Border _railBadge;      // pastille du nombre de signalements (Correction)
        private TextBlock _railBadgeText;
        private View.VersionsPanel _versionsPanel;
        private View.CompareWindow _compareWindow; // le paper flottant de comparaison (batch 38)
        private TextBlock _inspTitle, _inspKind, _inspStats, _inspDates;
        // Livre (batch 32) : objectif de chapitres au-dessus des statistiques,
        // panneaux Métadonnées / Publication dépliés sous les dates.
        private StackPanel _progressSection;
        private BookProgressBar _bookBar; // la barre (b32), partagée avec l'Accueil (b41)
        private TextBlock _progressLabel;
        private ColumnDefinition _progPresent, _progRest, _progDone, _progUndone;
        private StackPanel _statsSection;
        private System.Windows.Shapes.Path _statsChevron;
        private StackPanel _statusSection;   // état du texte + couleur de carte
        private ComboBox _statusCombo;
        private Button _colorButton;   // pastille + menu déroulant (b43)
        private Border _colorDot;
        private TextBlock _statusLabel, _colorLabel;
        private TextBlock _synopsisLabel, _notesLabel;
        private TextBox _synopsisBox, _notesBox;
        private StackPanel _linksPanel;
        private TextBlock _linksLabel;
        private StackPanel _homeStartSection; // raccourcis « Commencer » de l'Accueil (b43)
        private bool _loadingInspector;

        private TextBlock _statusLeft, _statusRight, _statusPages, _zoomLabel;
        private Slider _zoomSlider;   // 50–300 %, pas de 10 (batch 34)
        private bool _syncingZoom;
        private TextBlock _statusWarmup; // préchauffage de l'ouverture (b30)
        private DispatcherTimer _statsTimer, _autosaveTimer;

        private MenuItem _undoMenu, _redoMenu, _darkMenu, _binderMenu, _inspectorMenu, _recentMenu, _rulersMenu;
        private MenuItem _searchMenu, _versionsMenu; // cases à cocher du panneau actif (b39)

        // Bandeau « lecture seule » : projet écrit par un format plus récent
        // que celui que cette version sait réécrire sans perte (A2, batch 24).
        private Border _readOnlyBanner;

        // Session goal: words written since the goal was set, project-wide.
        // The same cache feeds the writing journal: each recount of the OPEN
        // document yields a net delta, credited to today — imports, purges and
        // moves never touch the journal (see UpdateStats).
        private readonly Dictionary<string, int> _wordCache = new Dictionary<string, int>();
        private int _sessionGoal, _sessionBaseWords; // l'objectif du sprint en cours (b48 : le sprint a remplacé l'objectif de session)
        // Le SPRINT (b48) : lancé pour une durée (0 = libre) et un objectif de
        // mots, suivi par une pastille discrète en haut de la zone centrale.
        private DateTime _sprintStart;
        private int _sprintMinutes;
        private bool _sprintActive, _sprintFinished;
        private DispatcherTimer _sprintTimer;
        private Border _sprintPill;
        private TextBlock _sprintText;
        private TextBlock _paceLabel;               // le rythme du livre (Général, b48)

        public MainWindow()
        {
            Title = AppName;
            Width = 1200;
            Height = 760;
            MinWidth = 800;
            MinHeight = 500;
            WindowState = WindowState.Maximized; // plein écran au lancement
            Background = Chrome.WindowBg;
            Foreground = Chrome.Ink;

            var root = new DockPanel();
            _menuBar = BuildMenuBar();
            _statusBar = BuildStatusBar();
            root.Children.Add(_menuBar);
            root.Children.Add(BuildReadOnlyBanner());
            root.Children.Add(_statusBar);
            root.Children.Add(BuildContent());
            // Les toasts de succès (12/09) : par-dessus tout, en bas à droite.
            // L'hôte reste cliquable (18/09 : les toasts à boutons du secours
            // y vivent aussi) ; un toast de succès se déclare inerte lui-même.
            _toastHost = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 44)
            };
            var shell = new Grid();
            shell.Children.Add(root);
            shell.Children.Add(_toastHost);
            // Le voile de l'accueil (13/09) : à l'ouverture SANS projet, la
            // fenêtre reste vide — blanc cassé, pas d'interface — sous la
            // fenêtre d'accueil ; l'interface est aussi désactivée dessous.
            _shellRoot = root;
            _welcomeVeil = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF9, 0xF6)),
                Visibility = Visibility.Collapsed
            };
            shell.Children.Add(_welcomeVeil);
            Content = shell;
            // Les travaux de fond qui font tourner l'anneau (0.50.0) : le
            // correcteur (Grammalecte qui s'initialise ou analyse, synonymes,
            // suggestions d'orthographe) et le préchauffage des textes après
            // l'ouverture. Les travaux du fil d'interface (chargements,
            // sauvegarde) passent par _busy.Run.
            _busy.Watch(delegate { return _editor != null && _editor.IsProofingBusy; });
            _busy.Watch(delegate { return _statusWarmup != null && _statusWarmup.Visibility == Visibility.Visible; });
            Loaded += delegate
            {
                // Le .plot reçu en argument s'ouvre ICI, la fenêtre affichée :
                // ses dialogues (réserves, secours) veulent un propriétaire
                // visible — avant Run, ils faisaient tomber l'application
                // (revue 22/09).
                if (PendingOpen != null)
                {
                    var pending = PendingOpen;
                    PendingOpen = null;
                    OpenFile(pending);
                }
                // Lancée sans .plot (ni argument, ni fichier ouvert) : l'accueil, et rien d'autre.
                if (_path == null) ShowWelcome();
                if (AppSettings.LoadNotice.Length > 0)
                {
                    var notice = AppSettings.LoadNotice;
                    AppSettings.LoadNotice = "";
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        MessageDialog.Show(this, notice, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }));
                }
                OfferRecoveries(); // un arrêt brutal la dernière fois ? (18/09)
                AppSettings.NoteUsage(DateTime.Now);
                Extensions.ModuleRegistry.Attach(this); // les modules à code reçoivent leur hôte (22/09)
                ScheduleAchievementCheck();
                _minuteTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
                _minuteTimer.Tick += delegate { ScheduleAchievementCheck(); };
                _minuteTimer.Start();
            };
            // Un module (DLC) installé ou retiré pendant la session (22/09) :
            // la fiche ouverte gagne ou perd son bouton et son onglet, la
            // bibliothèque ses puces, les succès se recomptent.
            Modules.Changed += delegate
            {
                if (_current != null && _current.Kind == ItemKind.Sheet && _sheetView.Visibility == Visibility.Visible)
                    _sheetView.LoadItem(_current, _project.FindTemplate(_current.TemplateId));
                if (_project != null) _sheetLibrary.Refresh();
                if (_journalView.Visibility == Visibility.Visible) _journalView.RefreshAchievements();
                // Une carte mentale ouverte suit l'arrivée ou le départ du module (22/09).
                if (_current != null && _current.Kind == ItemKind.MindMap) { CommitMindMap(); _mindMapEditor = null; ShowMindMap(_current); }
                RefreshOpenCorkboards();
                ScheduleAchievementCheck();
            };
            _editor.CalmRequested += ToggleCalmMode;
            // Les menus se grisent selon la vue affichée (13/09).
            _editor.IsVisibleChanged += delegate { RefreshMenuAvailability(); };
            _sheetView.IsVisibleChanged += delegate { RefreshMenuAvailability(); };
            _binder.SelectionChanged += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshMenuAvailability)); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                // Échap quitte le mode calme — en bulle, pour laisser la barre
                // de recherche de l'éditeur consommer son propre Échap.
                if (_calmMode && e.Key == Key.Escape) { SetCalmMode(false); e.Handled = true; }
            };
            ApplyZoom(AppSettings.Zoom);
            _editor.SetFormattingMarks(AppSettings.ShowFormattingMarks);
            _sheetView.SetFormattingMarks(AppSettings.ShowFormattingMarks);

            _history.Changed += OnHistoryChanged;
            _history.Applied += OnHistoryApplied;

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _statsTimer.Tick += delegate
            {
                _statsTimer.Stop();
                UpdateStats();
            };
            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _autosaveTimer.Tick += delegate { Autosave(); };
            _autosaveTimer.Start();
            StartRecoveryTimer(); // le secours, toutes les 30 s s'il y a du neuf (18/09)

            // Boutons latéraux de la souris = panneau précédent/suivant
            // (batch 28). Pas de repli clavier : le rebind arrive bientôt.
            PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.XButton1)
                { NavigateHistory(true); e.Handled = true; }
                else if (e.ChangedButton == MouseButton.XButton2)
                { NavigateHistory(false); e.Handled = true; }
            };

            Closing += OnClosingWindow;

            LoadProject(Project.CreateNew(), null);
        }

        // ============================================================= layout

        private UIElement BuildReadOnlyBanner()
        {
            _readOnlyBanner = new Border
            {
                Background = Chrome.Warn, // couleur de sens « warn » (batch 40)
                Padding = new Thickness(12, 6, 12, 6),
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Ce projet a été enregistré avec une version plus récente de Marabook. "
                         + "Il est ouvert en lecture seule pour ne rien détruire : "
                         + "l'enregistrement et la sauvegarde automatique sont désactivés."
                }
            };
            DockPanel.SetDock(_readOnlyBanner, Dock.Top);
            return _readOnlyBanner;
        }

        private UIElement BuildMenuBar()
        {
            var bar = new Border { Background = Chrome.BarBg };
            DockPanel.SetDock(bar, Dock.Top);
            var menu = new Menu { Background = Brushes.Transparent };

            // --- Fichier ---
            var file = new MenuItem { Header = "_Fichier" };
            file.Items.Add(Entry("new", "Nouveau projet", DoNew));
            file.Items.Add(Entry("open", "Ouvrir…", DoOpen));
            _recentMenu = new MenuItem { Header = "Projets récents" };
            file.Items.Add(_recentMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry("save", "Enregistrer", DoSave));
            file.Items.Add(Entry("save-as", "Enregistrer sous…", DoSaveAs));
            file.Items.Add(new Separator());
            file.Items.Add(Entry("preferences", "Préférences…", OpenPreferences));
            file.Items.Add(new Separator());
            // « Aperçu des pages » a quitté le menu (22/09) : il vit dans le
            // ruban (onglet Composition) ; son raccourci reste à la fenêtre.
            AddGesture("print-preview", delegate { if (TextOrSheetActive()) ShowPrintPreview(); });
            file.Items.Add(Entry("print", "Imprimer…", PrintCurrent, TextOrSheetActive));
            file.Items.Add(new Separator());
            var importMenu = new MenuItem { Header = "Importer" };
            importMenu.Items.Add(Entry("import-docs", "Des documents…", ImportDocuments));
            importMenu.Items.Add(Entry("import-scrivener", "Un projet Scrivener…", ImportScrivener));
            importMenu.Items.Add(new Separator());
            importMenu.Items.Add(Entry(null, "Les commentaires d'un document relu (.docx)…", ImportReviewedComments, TextActive)); // b49
            file.Items.Add(importMenu);
            var exportMenu = new MenuItem { Header = "Exporter" };
            exportMenu.Items.Add(Entry("export-item", "L'écrit sélectionné…", ExportCurrentItem, TextOrSheetActive));
            exportMenu.Items.Add(Entry("compile", "Compiler les écrits…", CompileManuscript));
            exportMenu.Items.Add(Entry("export-pdf", "PDF prêt à imprimer…", ExportPdf, TextOrSheetActive));
            exportMenu.Items.Add(Entry("export-epub", "EPUB du livre ou de l'écrit…", ExportEpubCurrent, TextOrBookActive)); // 22/09
            file.Items.Add(exportMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry(null, "Quitter", Close));
            menu.Items.Add(file);

            // --- Édition ---
            var edit = new MenuItem { Header = "É_dition" };
            _undoMenu = Entry("undo", "Annuler", DoUndo);
            _redoMenu = Entry("redo", "Rétablir", DoRedo);
            edit.Items.Add(_undoMenu);
            edit.Items.Add(_redoMenu);
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("find", "Rechercher dans l'écrit…", ShowSearchInActive, TextOrSheetActive));
            _searchMenu = Entry("project-search", "Rechercher dans le projet…", OpenSearchPanel);
            _searchMenu.IsCheckable = true;
            edit.Items.Add(_searchMenu);
            // Occurrence suivante / précédente ont quitté le menu (22/09) :
            // F3 / Maj+F3 restent des gestes de la fenêtre, au service du
            // panneau de recherche.
            AddGesture("search-next", delegate
            {
                if (AppSettings.RightPanel != RightPanel.Search) { OpenSearchPanel(); return; }
                _searchPanel.Next();
            });
            AddGesture("search-previous", delegate
            {
                if (AppSettings.RightPanel != RightPanel.Search) { OpenSearchPanel(); return; }
                _searchPanel.Previous();
            });
            _versionsMenu = Entry("versions-panel", "Versions de l'écrit…", OpenVersionsPanel, TextActive);
            _versionsMenu.IsCheckable = true;
            edit.Items.Add(_versionsMenu);
            edit.Items.Add(Entry("session-goal", "Lancer un sprint…", StartSprint));
            edit.Items.Add(Entry(null, "Indexeur de noms propres…",
                delegate { ExtractCharacters(_current); }, TextActive)); // b49, renommé le 22/09
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("new-text", "Nouvel écrit", delegate { _binder.NewText(null); }));
            edit.Items.Add(Entry("new-sheet", "Nouvelle fiche", delegate { _binder.NewSheet(null); }));
            edit.Items.Add(Entry("new-folder", "Nouveau dossier", delegate { _binder.NewFolder(null); }));
            edit.Items.Add(Entry("new-book", "Nouveau livre", delegate { _binder.NewBook(null); }));
            edit.Items.Add(Entry("import-media", "Importer dans Recherche…", delegate { _binder.ImportMediaDialog(null); }));
            edit.Items.Add(Entry("rename", "Renommer…", delegate { _binder.Rename(null); }, ItemSelected));
            edit.Items.Add(Entry("delete", "Supprimer", delegate { _binder.Delete(null); }, ItemSelected));
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("empty-trash", "Vider la corbeille", delegate { _binder.EmptyTrash(); }));
            menu.Items.Add(edit);

            // --- Format ---
            var format = new MenuItem { Header = "F_ormat" };
            format.Items.Add(Entry("styles", "Gérer les styles…", OpenStylesDialog));
            format.Items.Add(Entry("templates", "Modèles de fiches…", OpenTemplatesDialog));
            format.Items.Add(new Separator());
            format.Items.Add(Entry("insert-footnote", "Note de bas de page", InsertFootnoteInActive, TextOrSheetActive));
            format.Items.Add(Entry("insert-link", "Lien vers une fiche…", InsertLinkInActive, TextOrSheetActive));
            format.Items.Add(Entry("insert-image", "Insérer une image…", InsertImageInActive, TextOrSheetActive));
            format.Items.Add(Entry("insert-rule", "Ligne horizontale", delegate { RouteToActiveEditor("rule"); }, TextOrSheetActive));
            format.Items.Add(Entry("insert-separator", "Séparateur de scène", delegate { RouteToActiveEditor("separator"); }, TextOrSheetActive));
            menu.Items.Add(format);

            // « Mise en page » lives as a ribbon tab in the editor now; only
            // its shortcut survives at the window level.
            AddGesture("page-break", delegate { if (TextActive()) InsertPageBreakInActive(); });

            // --- Affichage ---
            var view = new MenuItem { Header = "_Affichage" };
            _binderMenu = Entry("toggle-binder", "Pile", ToggleBinder);
            _binderMenu.IsCheckable = true;
            _inspectorMenu = Entry("toggle-inspector", "Général", ToggleInspector);
            _inspectorMenu.IsCheckable = true;
            _darkMenu = Entry("dark-theme", "Thème sombre", ToggleDarkTheme);
            _darkMenu.IsCheckable = true;
            _darkMenu.IsChecked = AppSettings.DarkTheme;
            _rulersMenu = Entry("toggle-rulers", "Règles", ToggleRulers, TextOrSheetActive);
            _rulersMenu.IsCheckable = true;
            _rulersMenu.IsChecked = AppSettings.ShowRulers;
            view.Items.Add(_binderMenu);
            view.Items.Add(_inspectorMenu);
            view.Items.Add(_rulersMenu);
            view.Items.Add(new Separator());
            view.Items.Add(_darkMenu);
            menu.Items.Add(view);

            // --- Aide ---
            var help = new MenuItem { Header = "Aid_e" };
            help.Items.Add(Entry(null, "Vérifier les mises à jour…", CheckUpdates));
            help.Items.Add(Entry(null, "Rapports de plantage…", ShowCrashReports));
            help.Items.Add(new Separator());
            help.Items.Add(Entry(null, "Réinitialiser les succès…", ResetAchievements));
            help.Items.Add(new Separator());
            help.Items.Add(Entry(null, "À propos de Marabook…", ShowAbout));
            menu.Items.Add(help);

            // L'anneau d'activité (0.50.0) tient le bout droit de la ligne des
            // menus. Un seul exemplaire pour la vie de la fenêtre : la barre
            // est rebâtie après une personnalisation des raccourcis, l'anneau
            // change juste de parent.
            if (_busy == null) _busy = new BusyIndicator();
            var previousParent = _busy.Parent as Panel;
            if (previousParent != null) previousParent.Children.Remove(_busy);
            _busy.Margin = new Thickness(8, 0, 12, 0);
            DockPanel.SetDock(_busy, Dock.Right);
            var row = new DockPanel();
            row.Children.Add(_busy);
            row.Children.Add(menu);
            bar.Child = row;
            return bar;
        }

        /// <summary>Les polices à chaud (0.50.0) : Windows diffuse WM_FONTCHANGE
        /// quand une police est installée ou retirée — le catalogue se
        /// recharge et les sélecteurs suivent, sans relancer Marabook.</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
                if (source != null) source.AddHook(OnWindowMessage);
            }
            catch (Exception) { } // sans crochet, le catalogue vit jusqu'au prochain lancement
        }

        private const int WmFontChange = 0x001D;

        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmFontChange) FontCatalog.RequestRefresh();
            return IntPtr.Zero;
        }

        /// <summary>Reconstruit la barre de menus ET les KeyBindings de la
        /// fenêtre après une personnalisation des raccourcis (Préférences →
        /// Raccourcis, b43) — Entry/AddGesture posent tout à la construction,
        /// on repart donc de zéro.</summary>
        private void RefreshShortcuts()
        {
            // Content est la grille « shell » depuis les toasts (12/09) : la
            // barre vit dans _shellRoot — avec « Content as DockPanel » on
            // sortait ici sans rien rebâtir (0.50.0).
            var root = _shellRoot as DockPanel;
            if (root == null || _menuBar == null) return;
            InputBindings.Clear();
            var index = root.Children.IndexOf(_menuBar);
            root.Children.RemoveAt(index);
            _menuBar = BuildMenuBar();
            root.Children.Insert(index, _menuBar);
            UpdateRecentMenu();
            ApplyPanelVisibility();  // recoche Affichage (les MenuItem sont neufs)
            _railOffered = null;     // infobulles du rail : geste affiché à refaire
            UpdateRail();
        }

        /// <summary>Registers a window-wide key binding for an action that has
        /// no menu entry (ribbon-only commands).</summary>
        private void AddGesture(string actionId, Action handler)
        {
            Key key;
            ModifierKeys modifiers;
            if (AppSettings.ParseGesture(AppSettings.Gesture(actionId), out key, out modifiers))
                InputBindings.Add(new KeyBinding(new DelegateCommand(handler), key, modifiers));
        }

        /// <summary>Builds a menu entry wired to an action id: display shortcut
        /// from settings, click handler, and a window-wide key binding.</summary>
        private MenuItem Entry(string actionId, string header, Action handler)
        {
            return Entry(actionId, header, handler, null);
        }

        /// <summary>Une entrée de menu avec sa CONDITION (13/09) : grisée
        /// quand elle n'a pas de sens là où l'on est (une note de bas de page
        /// sur l'écran d'un livre) ; son raccourci se tait de même.</summary>
        private MenuItem Entry(string actionId, string header, Action handler, Func<bool> when)
        {
            var item = new MenuItem { Header = header };
            var guarded = when == null ? handler : delegate { if (when()) handler(); };
            item.Click += delegate { guarded(); };
            if (actionId != null)
            {
                var gesture = AppSettings.Gesture(actionId);
                item.InputGestureText = AppSettings.DisplayGesture(gesture);
                Key key;
                ModifierKeys modifiers;
                if (AppSettings.ParseGesture(gesture, out key, out modifiers))
                    InputBindings.Add(new KeyBinding(new DelegateCommand(guarded), key, modifiers));
            }
            if (when != null) _menuRules.Add(new KeyValuePair<MenuItem, Func<bool>>(item, when));
            return item;
        }

        // Les entrées conditionnelles et leur règle ; réévaluées à chaque
        // changement de vue (l'éditeur ou la fiche qui apparaît/disparaît).
        private readonly List<KeyValuePair<MenuItem, Func<bool>>> _menuRules
            = new List<KeyValuePair<MenuItem, Func<bool>>>();

        /// <summary>Un écrit ou une fiche est ouvert dans sa vue.</summary>
        private bool TextOrSheetActive()
        {
            return _editor.Visibility == Visibility.Visible || _sheetView.Visibility == Visibility.Visible;
        }

        private bool TextActive()
        {
            return _editor.Visibility == Visibility.Visible;
        }

        private bool ItemSelected()
        {
            return _current != null && !_current.IsCategory;
        }

        private void RefreshMenuAvailability()
        {
            foreach (var rule in _menuRules)
            {
                bool enabled;
                try { enabled = rule.Value(); }
                catch { enabled = true; }
                rule.Key.IsEnabled = enabled;
            }
        }

        private UIElement BuildContent()
        {
            var grid = new Grid();
            _binderCol = new ColumnDefinition { Width = new GridLength(AppSettings.BinderWidth), MinWidth = 0 };
            var binderSplitCol = new ColumnDefinition { Width = GridLength.Auto };
            var centerCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 };
            var inspectorSplitCol = new ColumnDefinition { Width = GridLength.Auto };
            _inspectorCol = new ColumnDefinition { Width = new GridLength(AppSettings.InspectorWidth), MinWidth = 0 };
            // Le rail (batch 39) : à droite de tout, séparateur compris —
            // redimensionner le panneau ne le déplace pas.
            _railCol = new ColumnDefinition { Width = new GridLength(RailWidth) };
            grid.ColumnDefinitions.Add(_binderCol);
            grid.ColumnDefinitions.Add(binderSplitCol);
            grid.ColumnDefinitions.Add(centerCol);
            grid.ColumnDefinitions.Add(inspectorSplitCol);
            grid.ColumnDefinitions.Add(_inspectorCol);
            grid.ColumnDefinitions.Add(_railCol);

            _binder = new BinderView();
            _binder.DictionaryEntryRequested += delegate
            {
                // Depuis la Pile : on ouvre l'écran, puis le dialogue.
                var root = _project.Category(Project.KeyDictionary);
                if (root != null) _binder.SelectItem(root.Id);
                _dictionaryView.NewEntry(true);
            };
            _binder.SelectionChanged += OnBinderSelection;
            _binder.JournalRequested += ShowJournal;
            _binder.AchievementEvent += UnlockAchievement; // « Ooh la boulette ! » (12/09)
            _binder.StructureChanged += delegate
            {
                MarkDirty();
                _pageCountCache.Clear(); // moves change book folio offsets
                ValidateSidePin(); // un épinglé jeté ou supprimé lâche l'épingle ; un renommé se met à jour (b47)
                // Les tuiles suivent la Pile (14/09) : un item supprimé ou
                // renommé depuis le menu d'une tuile passe par la Pile.
                RefreshOpenCorkboards();
                if (_sheetLibrary.Visibility == Visibility.Visible) _sheetLibrary.Refresh();
                // Chauffe le cache de mots : un document importé entre au cache
                // à sa taille réelle, sans jamais créditer le journal.
                ProjectWords();
                // L'Accueil suit la Pile (épingle, corbeille, livre…) s'il est affiché (b41).
                if (_homeView.Visibility == Visibility.Visible) _homeView.Refresh();
                ScheduleAchievementCheck();
            };
            _binder.BookPageTotal = BookPageTotal;
            Grid.SetColumn(_binder, 0);
            grid.Children.Add(_binder);

            _binderSplit = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(_binderSplit, 1);
            grid.Children.Add(_binderSplit);

            var center = new Grid();
            _placeholder = new TextBlock
            {
                Text = "Sélectionnez un écrit dans la Pile,\nou créez-en un (Ctrl+T).",
                Foreground = Chrome.SoftText,
                FontSize = 15,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            center.Children.Add(_placeholder);

            _editor = new EditorView { Visibility = Visibility.Collapsed };
            _editor.Edited += OnEditorEdited;
            _editor.LinkClicked += NavigateToTitle;
            _editor.DefinitionRequested += ShowDefinition; // clic droit › « Afficher la définition » (18/09)
            _editor.ZoomStepRequested += delegate(int step) { ApplyZoom(AppSettings.Zoom + step); };
            _editor.DocumentSettingChanged += MarkDirty; // l'interligne du document (22/09)
            _editor.PageSetupChanged += delegate
            {
                _sheetView.ApplyPageSetup(_project.Page);
                MarkDirty();
                _binder.Rebuild(); // book alert chips follow margin edits
            };
            _editor.PageInfoChanged += delegate(int page, int pages)
            {
                _statusPages.Text = _editor.Visibility == Visibility.Visible
                    ? "p. " + page + "/" + pages + "   ·   " : "";
            };
            _editor.MarksToggled += OnMarksToggled;
            _editor.StylesRequested += OpenStylesDialog;
            _editor.PreviewRequested += ShowPrintPreview;
            _editor.PrintRequested += PrintCurrent;
            _editor.ExportRequested += ExportCurrentItem;
            _editor.PdfRequested += ExportPdf;
            center.Children.Add(_editor);

            // Batch 31 : la fiche n'a plus de corps à ruban — seuls restent
            // l'édition, la navigation [[wiki]] et le zoom.
            _sheetView = new SheetView { Visibility = Visibility.Collapsed };
            _sheetView.Edited += OnEditorEdited;
            _editor.SnapshotsChanged += OnSnapshotsChanged;
            _sheetView.LinkClicked += NavigateToTitle;
            // Batch 34 : « ← Retour » remonte au tableau du parent (la
            // bibliothèque si la fiche vit à la racine), une relation ouvre
            // la fiche liée.
            _sheetView.BackRequested += delegate
            {
                if (_current == null || _current.Parent == null) return;
                _binder.SelectItem(_current.Parent.Id);
            };
            _sheetView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _sheetView.RenameRequested += delegate
            {
                if (_current == null) return;
                _binder.RenameQuiet(_current);
                _sheetView.RefreshTitle();
                UpdateInspector();
            };
            _sheetView.ZoomStepRequested += delegate(int step) { ApplyZoom(AppSettings.Zoom + step); };
            center.Children.Add(_sheetView);

            _corkboard = new CorkboardView { Visibility = Visibility.Collapsed };
            _corkboard.PageCounter = PageCountOf; // le tri « Pages » des filtres
            _corkboard.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _corkboard.Changed += delegate { MarkDirty(); UpdateInspector(); _binder.Rebuild(); };
            _corkboard.ExportRequested += ExportItem;
            _corkboard.RenameRequested += delegate(BinderItem item)
            { _binder.RenameQuiet(item); RefreshOpenCorkboards(); UpdateInspector(); };
            _corkboard.CardImageRequested += CardImageRequested;
            // Supprimer depuis le tableau : la Pile suit par l'historique, le
            // tableau lui-même doit être redessiné (batch 33 — la carte restait).
            _corkboard.DeleteRequested += delegate(BinderItem item)
            {
                if (!ConfirmTrash(item)) return; // confirmation depuis un corkboard (14/09)
                _binder.Delete(item); RefreshOpenCorkboards(); UpdateInspector(); UpdateStats();
            };
            _corkboard.ApplyTemplateRequested += ApplyPageTemplateTo;
            _corkboard.NewDocumentRequested += NewBookDocument; // « + Nouveau plan » de la racine Plans (b35)
            center.Children.Add(_corkboard);

            // La bibliothèque de fiches (batch 31) : la vue de la catégorie
            // « Fiches » — rangées par catégorie, cartes, recherche.
            _sheetLibrary = new View.SheetLibraryView { Visibility = Visibility.Collapsed };
            _sheetLibrary.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _sheetLibrary.AchievementEvent += UnlockAchievement; // « Crétin des alpes » (12/09)
            _sheetLibrary.Changed += delegate { MarkDirty(); UpdateInspector(); _binder.Rebuild(); };
            _sheetLibrary.MenuProvider = delegate(BinderItem item) { return _binder.BuildContextMenu(item, true); }; // les tuiles offrent le menu de la Pile (14/09)
            _corkboard.MenuProvider = delegate(BinderItem item) { return _binder.BuildContextMenu(item, true); };
            center.Children.Add(_sheetLibrary);

            _editor.LinkRequested += InsertLinkInActive; // onglet Insertion (b33)
            _editor.PlanLocator = delegate(BinderItem text) { return _project == null ? null : _project.PlanForText(text.Id); };
            _editor.PlanRequested += delegate(BinderItem plan) { _binder.SelectItem(plan.Id); };

            // Le dictionnaire personnel (batch 33) : la vue de la racine
            // « Dictionnaire » de la Pile ; toute modification prévient le
            // correcteur (clés pliées à reconstruire) et, pour le projet, le .plot.
            // L'Accueil (batch 41) : la vue de la racine « Accueil ». Ses
            // commandes sont celles des menus ; ouvrir = sélectionner dans la Pile.
            _homeView = new View.HomeView { Visibility = Visibility.Collapsed };
            _homeView.OpenRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _homeView.MenuProvider = delegate(BinderItem item) { return _binder.BuildContextMenu(item, true); };
            _homeView.RenameRequested += RenameProject;
            _homeView.Sprint = SprintStatusNow;
            _homeView.WordsOf = WordsOfItem;
            _homeView.CharsOf = CharsOfItem;
            center.Children.Add(_homeView);

            _dictionaryView = new View.DictionaryView { Visibility = Visibility.Collapsed };
            _dictionaryView.Changed += delegate(bool projectScope)
            {
                if (projectScope) MarkDirty(); else AppSettings.Save();
                _editor.RefreshProofing();
                ScheduleAchievementCheck();
                if (_lexiconPanel != null) _lexiconPanel.Refresh();
            };
            _dictionaryView.LexiconPinToggled += SetLexiconPinned; // menu options (18/09)
            _editor.LexiconChanged += delegate
            {
                if (_dictionaryView.Visibility == Visibility.Visible) _dictionaryView.Refresh();
            };
            // Le nom d'une fiche ajouté au dictionnaire du projet (12/09).
            _sheetView.LexiconChanged += delegate
            {
                MarkDirty();
                _editor.RefreshProofing();
                if (_dictionaryView.Visibility == Visibility.Visible) _dictionaryView.Refresh();
                ScheduleAchievementCheck();
            };
            center.Children.Add(_dictionaryView);

            // Les plans (batch 35) : l'écran d'un plan ; ses liens vers les
            // écrits, livres et dossiers passent par la Pile.
            _planView = new View.PlanView { Visibility = Visibility.Collapsed };
            _planView.Edited += delegate
            {
                MarkDirty();
                _editor.RefreshPlanButton();
                _binder.Rebuild();
            };
            _planView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _planView.RenameRequested += delegate // le crayon du plan (14/09)
            {
                if (_current == null || _current.Kind != ItemKind.Plan) return;
                _binder.RenameQuiet(_current);
                _planView.Refresh();
                UpdateInspector();
            };
            _planView.BackRequested += delegate
            {
                var root = _project == null ? null : _project.Category(Project.KeyPlans);
                if (root != null) _binder.SelectItem(root.Id);
            };
            center.Children.Add(_planView);
            _mindMapHost = new Grid { Visibility = Visibility.Collapsed, Background = Chrome.WindowBg };
            center.Children.Add(_mindMapHost);

            _bookView = new BookView { Visibility = Visibility.Collapsed };
            _bookView.PageCounter = PageCountOf; // filtres du corkboard du livre
            _bookView.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _bookView.Changed += delegate
            {
                MarkDirty(); UpdateInspector(); _binder.Rebuild();
            };
            // La page livre (22/09) : Publier et l'onglet Styles.
            _bookView.PublishRequested += PublishBook;
            _bookView.EpubRequested += ExportEpub;
            _bookView.StylesChanged += delegate { ApplyStyleSheet(); MarkDirty(); };
            _bookView.ExportRequested += ExportItem;
            _bookView.RenameRequested += delegate(BinderItem item)
            { _binder.RenameQuiet(item); RefreshOpenCorkboards(); UpdateInspector(); };
            _bookView.CardImageRequested += CardImageRequested;
            _bookView.DeleteRequested += delegate(BinderItem item)
            { _binder.Delete(item); RefreshOpenCorkboards(); UpdateInspector(); UpdateStats(); };
            _bookView.ApplyTemplateRequested += ApplyPageTemplateTo;
            _bookView.NewTemplateRequested += NewPageTemplate;
            _bookView.ExportTemplateRequested += ExportPageTemplate;
            _bookView.ImportTemplateRequested += ImportPageTemplate;
            _bookView.CopyTemplateRequested += CopyPageTemplate;
            _bookView.NewDocumentRequested += NewBookDocument;
            _bookView.MenuProvider = delegate(BinderItem item) { return _binder.BuildContextMenu(item, true); }; // le corkboard du livre aussi (14/09)
            center.Children.Add(_bookView);

            _templateView = new View.TemplateView { Visibility = Visibility.Collapsed };
            _templateView.Changed += delegate
            {
                MarkDirty();
                // Différé : le Changed peut arriver au beau milieu d'un clic
                // dans la Pile (perte de focus d'une zone → commit) — rebâtir
                // l'arbre à cet instant détruit le nœud cliqué sous la souris.
                Dispatcher.BeginInvoke(new Action(delegate { _binder.Rebuild(); }),
                    DispatcherPriority.Background);
            };
            center.Children.Add(_templateView);

            _mediaView = new MediaView { Visibility = Visibility.Collapsed };
            center.Children.Add(_mediaView);

            _journalView = new JournalView { Visibility = Visibility.Collapsed };
            _journalView.Changed += delegate { MarkDirty(); CheckDailyGoal(); };
            _journalView.SprintRequested += StartSprint; // « Démarrer un sprint » de la carte Sprints (14/09)
            center.Children.Add(_journalView);

            // Sortie du mode calme : une pastille discrète en haut à droite des
            // pages — s'affirme au survol.
            var calmLabel = new StackPanel { Orientation = Orientation.Horizontal };
            calmLabel.Children.Add(new TextBlock
            {
                Text = "✕",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            calmLabel.Children.Add(new TextBlock
            {
                Text = "Mode calme",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            _calmExit = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 24, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0.45,
                Visibility = Visibility.Collapsed,
                ToolTip = "Quitter le mode calme (Échap)",
                Child = calmLabel
            };
            _calmExit.MouseEnter += delegate { _calmExit.Opacity = 1.0; };
            _calmExit.MouseLeave += delegate { _calmExit.Opacity = 0.45; };
            _calmExit.MouseLeftButtonUp += delegate { SetCalmMode(false); };
            center.Children.Add(_calmExit);

            // La pastille du sprint (b48) : en haut de la zone centrale,
            // centrée, discrète — le temps qui reste, les mots écrits, une
            // croix pour arrêter ou refermer.
            var sprintRow = new StackPanel { Orientation = Orientation.Horizontal };
            _sprintText = new TextBlock { FontSize = 12, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center };
            sprintRow.Children.Add(_sprintText);
            var sprintStop = Buttons.Text("✕", "Arrêter le sprint (ou refermer la pastille)", Buttons.Compact, Buttons.Look.Calm);
            sprintStop.Margin = new Thickness(8, 0, 0, 0);
            sprintStop.Click += delegate { CloseSprint(); };
            sprintRow.Children.Add(sprintStop);
            _sprintPill = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 3, 6, 3),
                // À droite, sous l'axe d'affichage du ruban (Pages/Brouillon/
                // Calme) : jamais sur les onglets, jamais sur la sortie du calme.
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 40, 24, 0),
                Visibility = Visibility.Collapsed,
                Child = sprintRow
            };
            center.Children.Add(_sprintPill);
            // La pastille se DÉPLACE à la souris (14/09) : elle gênait parfois
            // l'accès à un bouton. Saisie sur le texte (la croix garde son clic),
            // bornée à la zone centrale ; la position tient la session.
            _sprintPill.Cursor = System.Windows.Input.Cursors.SizeAll;
            Point dragOrigin = new Point();
            Thickness dragStart = new Thickness();
            _sprintPill.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                dragOrigin = e.GetPosition(center);
                if (_sprintPill.HorizontalAlignment != HorizontalAlignment.Left)
                {
                    // Première saisie : on passe en coordonnées absolues (haut-gauche).
                    var at = _sprintPill.TranslatePoint(new Point(0, 0), center);
                    _sprintPill.HorizontalAlignment = HorizontalAlignment.Left;
                    _sprintPill.VerticalAlignment = VerticalAlignment.Top;
                    _sprintPill.Margin = new Thickness(at.X, at.Y, 0, 0);
                }
                dragStart = _sprintPill.Margin;
                _sprintPill.CaptureMouse();
                e.Handled = true;
            };
            _sprintPill.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!_sprintPill.IsMouseCaptured) return;
                var now = e.GetPosition(center);
                var x = dragStart.Left + now.X - dragOrigin.X;
                var y = dragStart.Top + now.Y - dragOrigin.Y;
                x = Math.Max(0, Math.Min(x, Math.Max(0, center.ActualWidth - _sprintPill.ActualWidth)));
                y = Math.Max(0, Math.Min(y, Math.Max(0, center.ActualHeight - _sprintPill.ActualHeight)));
                _sprintPill.Margin = new Thickness(x, y, 0, 0);
            };
            _sprintPill.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_sprintPill.IsMouseCaptured) { _sprintPill.ReleaseMouseCapture(); e.Handled = true; }
            };
            _sprintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sprintTimer.Tick += delegate { TickSprint(); };

            _centerHost = center;
            Grid.SetColumn(center, 2);
            grid.Children.Add(center);

            _inspectorSplit = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(_inspectorSplit, 3);
            grid.Children.Add(_inspectorSplit);

            _inspector = BuildInspector();
            Grid.SetColumn(_inspector, 4);
            grid.Children.Add(_inspector);
            // Métadonnées, Publication et Édition d'un livre ont quitté la
            // colonne de droite le 22/09 : ce sont les onglets de la page livre.

            // Le panneau de CORRECTION (batch 28) partage la colonne de
            // droite : ouvert, il REMPLACE l'inspecteur (bascule « Détails
            // de correction » du ruban Révision).
            _correctionHost = new Border { Child = _editor.CorrectionPanel };
            Grid.SetColumn(_correctionHost, 4);
            grid.Children.Add(_correctionHost);
            _editor.CorrectionPanelToggled += delegate(bool wanted)
            {
                SetRightPanel(wanted ? RightPanel.Correction : RightPanel.Inspector);
            };

            // Le panneau de RECHERCHE du projet (batch 37) : même colonne,
            // même famille ; ouvert, il passe devant Correction et l'inspecteur.
            _searchPanel = new View.SearchPanel();
            _searchPanel.NavigateRequested += GoToHit;
            _searchPanel.ReplaceRequested += ReplaceOne;
            _searchPanel.ReplaceAllRequested += ReplaceAll;
            // La croix d'un outil rend l'inspecteur (l'état de repos).
            _searchPanel.CloseRequested += delegate { SetRightPanel(RightPanel.Inspector); };
            _searchHost = new Border { Child = _searchPanel };
            Grid.SetColumn(_searchHost, 4);
            grid.Children.Add(_searchHost);

            // Le panneau VERSIONS (batch 38) : quatrième de la famille, même
            // colonne ; ouvert, il passe devant Recherche, Correction et l'inspecteur.
            _versionsPanel = new View.VersionsPanel();
            _versionsPanel.CompareRequested += CompareSnapshots;
            _versionsPanel.RestoreRequested += delegate(Snapshot snapshot) { RestoreSnapshot(snapshot, true); };
            _versionsPanel.SnapshotsChanged += delegate { MarkDirty(); };
            _versionsPanel.CloseRequested += delegate { SetRightPanel(RightPanel.Inspector); };
            _versionsHost = new Border { Child = _versionsPanel };
            Grid.SetColumn(_versionsHost, 4);
            grid.Children.Add(_versionsHost);

            // L'ÉPINGLÉ SUR LE CÔTÉ (batch 47) : un écrit ou une fiche lu en
            // miroir, même colonne ; un seul à la fois, posé par le menu
            // contextuel de la Pile, tenu par le projet (.plot v21).
            _pinnedPanel = new View.PinnedPanel();
            _pinnedPanel.OpenRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _pinnedPanel.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _pinnedPanel.LinkClicked += NavigateToTitle;
            _pinnedPanel.UnpinRequested += UnpinSide;
            _pinnedHost = new Border { Child = _pinnedPanel };
            Grid.SetColumn(_pinnedHost, 4);
            grid.Children.Add(_pinnedHost);
            // Le LEXIQUE (18/09) : la définition d'un mot du dictionnaire
            // personnel, même colonne (MainWindow.Lexicon.cs).
            BuildLexiconPanel(grid);
            _pinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _pinTimer.Tick += delegate { _pinTimer.Stop(); RefreshSidePin(); };
            _binder.IsSidePinned = delegate(BinderItem item) { return item != null && item == _sidePin; };
            _binder.SidePinRequested += delegate(BinderItem item)
            {
                if (item == _sidePin) UnpinSide(); else PinToSide(item);
            };

            _rail = BuildRail();
            Grid.SetColumn(_rail, 5);
            grid.Children.Add(_rail);
            _editor.FindingsChanged += UpdateRail;
            _editor.FindingsChanged += TrackDeepClean; // « Nettoyage en profondeur » (12/09)

            ApplyPanelVisibility();
            return grid;
        }

        /// <summary>« Supprimer » depuis un corkboard (14/09) : on demande —
        /// l'item part à la corbeille, d'où on le restaure, mais une tuile
        /// se clique vite.</summary>
        private bool ConfirmTrash(BinderItem item)
        {
            return _binder.ConfirmTrash(item); // la même question que le menu des tuiles
        }

        /// <summary>Ouvre (ou ramène) le panneau de recherche du projet, la
        /// requête prête à taper — Ctrl+Maj+F, menu Édition.</summary>
        private void OpenSearchPanel()
        {
            SetRightPanel(RightPanel.Search);
            _searchPanel.FocusQuery();
        }

        // ------------------------------------------------ versions (b38, lots D et E)

        /// <summary>Ouvre (ou ramène) le panneau Versions — Ctrl+Maj+H, menu Édition.</summary>
        private void OpenVersionsPanel()
        {
            SetRightPanel(RightPanel.Versions);
            _versionsPanel.SetCurrent(_current);
        }

        /// <summary>La comparaison dans son paper flottant — un seul, rechargé.</summary>
        private void CompareSnapshots(Snapshot from, Snapshot to)
        {
            if (_project == null || from == null) return;
            var item = _project.FindById(from.ItemId);
            if (item == null) return;
            CommitActive();
            if (_compareWindow == null)
            {
                _compareWindow = new View.CompareWindow(this);
                _compareWindow.RestoreParagraphRequested += RestoreParagraph;
                _compareWindow.Closed += delegate { _compareWindow = null; };
            }
            _compareWindow.Load(_project, item, from, to);
            if (!_compareWindow.IsVisible) _compareWindow.Show();
            else _compareWindow.Activate();
        }

        /// <summary>Ce que le dialogue de restauration dit : l'état actuel est
        /// d'abord figé, le remplacement s'annule en un Ctrl+Z, et — si le
        /// document est ouvert — son historique d'annulation est réinitialisé.</summary>
        private string RestoreWarning(BinderItem item, Snapshot snapshot)
        {
            var text = "Remettre « " + item.Title + " » dans l'état de la version « " + snapshot.DisplayLabel + " » ("
                + View.VersionsPanel.FormatDate(snapshot.Date) + ") ?\n\nL'état actuel est d'abord figé dans un instantané « avant restauration » ; "
                + "la restauration s'annule ensuite en un seul Ctrl+Z.";
            if (item == _current && (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet))
                text += "\n\nLe document est ouvert : son historique d'annulation sera réinitialisé.";
            return text;
        }

        /// <summary>Restaure un instantané : instantané automatique de l'état
        /// courant d'abord, puis UNE action d'historique (RestoreSnapshotAction) ;
        /// la vue du document ouvert est rechargée par OnHistoryApplied.</summary>
        private void RestoreSnapshot(Snapshot snapshot, bool confirm)
        {
            if (_project == null || snapshot == null) return;
            var item = _project.FindById(snapshot.ItemId);
            if (item == null) { _versionsPanel.SetNotice("Cet écrit n'existe plus."); return; }
            if (confirm && MessageDialog.Show(this, RestoreWarning(item, snapshot), "Restaurer une version",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            CommitActive();
            SnapshotStore.GuardBeforeRestore(_project, item, snapshot.DisplayLabel, AppSettings.SnapshotCap);
            _history.Run(new History.RestoreSnapshotAction(item, snapshot.Document, snapshot.DisplayLabel));
        }

        /// <summary>Restaure UN paragraphe depuis la comparaison (annulable).</summary>
        private void RestoreParagraph(BinderItem item, ParagraphDelta delta)
        {
            if (_project == null || item == null || delta == null || !History.RestoreParagraphAction.CanRestore(delta)) return;
            CommitActive();
            _history.Run(new History.RestoreParagraphAction(item, delta));
        }

        /// <summary>Navigation à une occurrence (batch 37) : ouvrir l'item s'il
        /// ne l'est pas, puis — une fois la vue en place — sélectionner
        /// l'empan exact dans la surface composée, le champ de la fiche, la
        /// colonne ou la brique du plan, l'entrée du dictionnaire.</summary>
        private void GoToHit(SearchHit hit)
        {
            if (hit == null || _project == null || hit.Item == null) return;
            if (_project.FindById(hit.Item.Id) == null) return; // item disparu depuis la recherche
            if (_current != hit.Item || !IsItemViewVisible(hit.Item)) _binder.SelectItem(hit.Item.Id);
            var target = hit;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                if (_current == target.Item) PositionOnHit(target);
            }));
        }

        // ------------------------------------------------ remplacement (b37, lot C)

        /// <summary>Remplace UNE occurrence : dans le document ouvert (surface
        /// composée), un cran d'annulation local — comme une frappe ; ailleurs,
        /// une action d'historique d'une seule édition.</summary>
        private void ReplaceOne(SearchHit hit, string replacement)
        {
            if (hit == null || _project == null || hit.Field == null) return;
            if (!hit.Replaceable)
            {
                _searchPanel.SetNotice(hit.NoProof
                    ? "Cette occurrence est dans un passage « ne pas corriger » : non remplacée."
                    : "Cette occurrence coupe une ligature : non remplaçable.");
                return;
            }
            if (_project.FindById(hit.Item.Id) == null) return;
            CommitActive();
            var query = _searchPanel.Query();
            var matched = hit.Start + hit.Length <= hit.Field.Text.Length ? hit.Field.Text.Substring(hit.Start, hit.Length) : "";
            if (hit.Item == _current && hit.Item.Kind == ItemKind.Text && hit.Field.IsParagraph && _editor.ComposedActive)
            {
                if (_editor.ReplaceRange(hit.Field.ParagraphIndex, hit.Start, hit.Length, query.ReplacementFor(matched, replacement)))
                {
                    MarkDirty();
                    _searchPanel.Refresh();
                    return;
                }
            }
            RunReplace(ReplacePlan.Build(_project, new List<SearchHit> { hit }, query, replacement), query.Pattern);
        }

        /// <summary>« Tout remplacer… » : la recherche complète de la portée
        /// (sans plafond), la PRÉVISUALISATION obligatoire, puis l'action.</summary>
        private void ReplaceAll(string replacement)
        {
            if (_project == null) return;
            var query = _searchPanel.Query();
            if (!query.IsValid) { _searchPanel.SetNotice(query.Error ?? "Rien à chercher."); return; }
            CommitActive();
            var targets = ProjectSearch.Collect(_project, _searchPanel.Scope, _current, _searchPanel.Kind, _searchPanel.IncludeTrash);
            var result = ProjectSearch.Run(targets, query, int.MaxValue, TimeSpan.FromSeconds(30), System.Threading.CancellationToken.None);
            if (result.Interrupted) { _searchPanel.SetNotice(result.Message); return; }
            if (result.Total == 0) { _searchPanel.SetNotice("Aucune occurrence à remplacer."); return; }
            var chosen = View.ReplacePreviewDialog.Ask(this, result, query, replacement, _current);
            if (chosen == null) return;
            RunReplace(ReplacePlan.Build(_project, chosen, query, replacement), query.Pattern);
        }

        private void RunReplace(ReplacePlan plan, string pattern)
        {
            if (plan.Edits.Count == 0)
            {
                _searchPanel.SetNotice("Rien à remplacer" + (plan.SkippedNoProof + plan.SkippedInexact > 0 ? " (occurrences écartées)." : "."));
                return;
            }
            // La ceinture (b38, lot C) : un instantané automatique de chaque
            // écrit et fiche touchés, AVANT l'action — l'annulation est de
            // session, l'instantané survit à la fermeture.
            if (SnapshotStore.GuardBeforeReplace(_project, plan.Items, pattern, AppSettings.SnapshotCap) > 0) OnSnapshotsChanged();
            _history.Run(new History.ReplaceInProjectAction(_project, plan));
            if (plan.Edits.Count > 100) UnlockAchievement(Achievements.Yolo); // (12/09)
        }

        /// <summary>Des instantanés viennent d'être pris ou retirés : le
        /// projet a changé, le panneau Versions (lot D) suit.</summary>
        private void OnSnapshotsChanged()
        {
            MarkDirty();
            _versionsPanel.Refresh();
        }

        /// <summary>Une action d'historique vient d'être posée, défaite ou
        /// refaite : un remplacement projet touchant le document ouvert
        /// RECHARGE sa vue (pile locale vidée — jamais un Ctrl+Z local qui
        /// contredirait le reste), rebâtit la Pile (titres), relance la
        /// recherche et dit ce qui s'est passé.</summary>
        private void OnHistoryApplied(History.IUndoableAction action, bool undone)
        {
            // Une épingle (b41) défaite ou refaite : l'Accueil suit s'il est affiché.
            if (action is History.PinItemAction)
            {
                if (_homeView.Visibility == Visibility.Visible) _homeView.Refresh();
                return;
            }
            // Les restaurations (b38) : même règle du document ouvert.
            var restore = action as History.RestoreSnapshotAction;
            var restoreParagraph = action as History.RestoreParagraphAction;
            if (restore != null || restoreParagraph != null)
            {
                var touched = restore != null ? restore.Item : restoreParagraph.Item;
                var conflict = restore != null ? restore.Conflict : restoreParagraph.Conflict;
                if (_current == touched) ReloadCurrentView();
                MarkDirty();
                _versionsPanel.Refresh();
                if (_compareWindow != null && _compareWindow.Shows(touched)) _compareWindow.Refresh();
                _versionsPanel.SetNotice(conflict
                    ? "Rien n'a été écrit : le texte avait changé entre-temps."
                    : restore != null
                        ? (undone ? "Restauration annulée." : "Version « " + restore.SnapshotLabel + " » restaurée — Ctrl+Z pour annuler.")
                        : (undone ? "Restauration du paragraphe annulée." : "Paragraphe restauré — Ctrl+Z pour annuler."));
                return;
            }
            // Un document remplacé d'un bloc (b49 : commentaires relus) :
            // la vue ouverte recharge depuis le pivot.
            var replaced = action as History.ReplaceDocumentAction;
            if (replaced != null)
            {
                if (_current == replaced.Item) ReloadCurrentView();
                MarkDirty();
                return;
            }
            var replace = action as History.ReplaceInProjectAction;
            if (replace == null) return;
            if (_current != null && replace.Touches(_current)) ReloadCurrentView();
            _binder.Rebuild();
            MarkDirty();
            var items = replace.Items.Count == 1 ? "1 item" : replace.Items.Count + " items";
            var occurrences = replace.Occurrences == 1 ? "1 occurrence" : replace.Occurrences + " occurrences";
            var notice = (undone ? "Remplacement annulé : " : "Remplacement : ") + occurrences + " dans " + items + ".";
            if (replace.Conflicts > 0)
                notice += " " + replace.Conflicts + (replace.Conflicts == 1 ? " édition sautée" : " éditions sautées")
                    + " (le texte avait changé entre-temps).";
            _searchPanel.Refresh();
            _searchPanel.SetNotice(notice);
        }

        /// <summary>Recharge la vue de l'item courant depuis le pivot, sans
        /// commit (le modèle vient d'être écrit par une action).</summary>
        private void ReloadCurrentView()
        {
            if (_current == null) return;
            _navigating = true;
            try { ShowItem(_current); }
            finally { _navigating = false; }
            UpdateInspector();
            UpdateStats();
        }

        private void PositionOnHit(SearchHit hit)
        {
            var item = hit.Item;
            var field = hit.Field;
            if (field == null) return;
            if (item.IsCategory)
            {
                int index;
                if (item.CategoryKey == Project.KeyDictionary && field.RefId != null && int.TryParse(field.RefId, out index))
                    _dictionaryView.GoTo(index);
                return;
            }
            if (item.Kind == ItemKind.Text)
            {
                if (field.IsParagraph) _editor.GoToRange(field.ParagraphIndex, hit.Start, hit.Start + hit.Length);
                else if (field.Kind == SearchField.KindAnnotation && field.RefId != null) _editor.GoToAnnotation(field.RefId);
            }
            else if (item.Kind == ItemKind.Sheet) _sheetView.GoTo(field, hit.Start, hit.Length);
            else if (item.Kind == ItemKind.Plan) _planView.GoTo(field, hit.Start, hit.Length);
        }

        private Border BuildInspector()
        {
            var panel = new StackPanel { Margin = new Thickness(14) };

            // À l'Accueil, le Général ne montre QUE les raccourcis
            // « Commencer » (batch 43 — sortis de la carte du même nom).
            _homeStartSection = new StackPanel { Visibility = Visibility.Collapsed };
            _homeStartSection.Children.Add(new TextBlock
            {
                Text = "Commencer",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
            _homeStartSection.Children.Add(HomeStartButton("Nouvel écrit", Buttons.Look.Primary, delegate { _binder.NewText(null); }));
            _homeStartSection.Children.Add(HomeStartButton("Nouvelle fiche", Buttons.Look.Outline, delegate { _binder.NewSheet(null); }));
            _homeStartSection.Children.Add(HomeStartButton("Nouveau plan", Buttons.Look.Outline, delegate { _binder.NewPlan(null); }));
            panel.Children.Add(_homeStartSection);

            _inspTitle = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_inspTitle);

            _inspKind = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 12)
            };
            panel.Children.Add(_inspKind);
            // Livre ou dossier relié à un plan (batch 35) : le lien, cliquable.
            _inspPlanLink = new TextBlock
            {
                Foreground = Chrome.Accent,
                FontSize = 12,
                Margin = new Thickness(0, -8, 0, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };
            _inspPlanLink.MouseLeftButtonDown += delegate
            {
                var plan = _inspPlanLink.Tag as BinderItem;
                if (plan != null) _binder.SelectItem(plan.Id);
            };
            panel.Children.Add(_inspPlanLink);

            // État d'avancement + couleur de carte, au-dessus du Synopsis.
            _statusSection = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            _statusLabel = new TextBlock
            {
                Text = "État du texte",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _statusSection.Children.Add(_statusLabel);
            _statusCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _statusCombo.Items.Add("—");
            foreach (var key in TextStatus.Keys)
                _statusCombo.Items.Add(TextStatus.Label(key));
            _statusCombo.SelectionChanged += delegate
            {
                if (_loadingInspector || _current == null) return;
                var index = _statusCombo.SelectedIndex;
                _current.Status = index <= 0 ? null : TextStatus.Keys[index - 1];
                MarkDirty();
                RefreshOpenCorkboards();
            };
            _statusSection.Children.Add(_statusCombo);
            _colorLabel = new TextBlock
            {
                Text = "Couleur",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 4),
                ToolTip = "Teinte de la barre de titre de la carte au corkboard "
                    + "— et de la boîte pour un dossier de livre"
            };
            _statusSection.Children.Add(_colorLabel);
            // La pastille dans un bouton ; le clic ouvre le menu des couleurs
            // (nuancier + couleurs personnalisées du projet) — batch 43.
            _colorDot = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            colorRow.Children.Add(_colorDot);
            colorRow.Children.Add(new TextBlock
            {
                Text = "▾",
                Foreground = Chrome.SoftText,
                FontSize = 9,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            _colorButton = new Button
            {
                Content = colorRow,
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _colorButton.Click += delegate { OpenColorMenu(); };
            _statusSection.Children.Add(_colorButton);
            panel.Children.Add(_statusSection);

            // Options d'un plan (batch 35) : le mot des nouvelles colonnes.
            _planSection = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            _planSection.Children.Add(new TextBlock
            {
                Text = "Nom des colonnes",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Le mot des nouvelles colonnes : « chapitre » donne « chapitre 1 », « chapitre 2 »…"
            });
            _planColumnWord = new TextBox();
            _planColumnWord.TextChanged += delegate
            {
                if (_loadingInspector || _current == null || _current.Kind != ItemKind.Plan || _current.Plan == null) return;
                var word = _planColumnWord.Text.Trim();
                _current.Plan.ColumnWord = word.Length == 0 ? PlanInfo.DefaultColumnWord : word;
                MarkDirty();
            };
            _planSection.Children.Add(_planColumnWord);
            panel.Children.Add(_planSection);

            // Personnages présents dans un écrit (batch 47) : les fiches
            // Personnage dont un nom apparaît, les plus présentes d'abord,
            // avec leur étape d'évolution pour cet écrit s'il y en a une.
            _presenceSection = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            _presenceSection.Children.Add(new TextBlock
            {
                Text = "Personnages présents",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Les fiches Personnage nommées dans cet écrit (titre, nom, prénom, alias), et leur étape d'évolution ici"
            });
            _presencePanel = new StackPanel();
            _presenceSection.Children.Add(_presencePanel);
            panel.Children.Add(_presenceSection);

            _synopsisLabel = new TextBlock
            {
                Text = "Synopsis",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(_synopsisLabel);

            _synopsisBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 90,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _synopsisBox.TextChanged += OnSynopsisChanged;
            panel.Children.Add(_synopsisBox);

            _notesLabel = new TextBlock
            {
                Text = "Notes",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 4)
            };
            panel.Children.Add(_notesLabel);

            _notesBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 90,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "Notes de travail — affichées en priorité sur les cartes du corkboard"
            };
            _notesBox.TextChanged += OnNotesChanged;
            panel.Children.Add(_notesBox);

            // Les stats vivent repliées dans un accordion pour ne pas
            // surcharger le panneau ; l'état ouvert/fermé est un réglage de
            // l'application (persistant).
            // Objectif du livre (batch 32) : la barre de progression — orange
            // pour les chapitres présents, vert pour ceux marqués Terminé —
            // juste au-dessus des statistiques. Clic : Options du livre.
            _progressSection = new StackPanel
            {
                Margin = new Thickness(0, 14, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Objectif du livre — clic : Options du livre…",
                Background = Brushes.Transparent
            };
            // Le dessin de la barre vit dans BookProgressBar (b41, partagé
            // avec l'Accueil) ; les colonnes restent exposées aux sondes.
            _bookBar = new BookProgressBar();
            _progressLabel = _bookBar.Label;
            _progPresent = _bookBar.Present;
            _progRest = _bookBar.Rest;
            _progDone = _bookBar.Done;
            _progUndone = _bookBar.Undone;
            _progressSection.Children.Add(_bookBar);
            _paceLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _progressSection.Children.Add(_paceLabel);
            _progressSection.MouseLeftButtonUp += delegate
            {
                if (_current != null && _current.Kind == ItemKind.Book) _binder.BookOptions(_current);
            };
            _progressSection.Visibility = Visibility.Collapsed;
            panel.Children.Add(_progressSection);

            _statsSection = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            _statsChevron = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L4,4 0,8"),
                Stroke = Chrome.SoftText,
                StrokeThickness = 1.6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 6, 0)
            };
            var statsHeader = new StackPanel { Orientation = Orientation.Horizontal };
            statsHeader.Children.Add(_statsChevron);
            statsHeader.Children.Add(new TextBlock
            {
                Text = "Statistiques",
                Foreground = Chrome.SoftText,
                FontSize = 12
            });
            var statsToggle = new Border
            {
                Background = Brushes.Transparent, // hit-test sur toute la ligne
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = statsHeader
            };
            statsToggle.MouseLeftButtonDown += delegate
            {
                AppSettings.StatsExpanded = !AppSettings.StatsExpanded;
                AppSettings.Save();
                ApplyStatsExpansion();
            };
            _statsSection.Children.Add(statsToggle);

            _inspStats = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(10, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            _statsSection.Children.Add(_inspStats);
            ApplyStatsExpansion();
            panel.Children.Add(_statsSection);

            _linksLabel = new TextBlock
            {
                Text = "Liens",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 4)
            };
            panel.Children.Add(_linksLabel);
            _linksPanel = new StackPanel();
            panel.Children.Add(_linksPanel);

            _inspDates = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_inspDates);

            // Livre : Métadonnées et Publication (batch 32) ont quitté
            // l'inspecteur pour deux onglets du rail (batch 39).

            return new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                }
            };
        }

        private UIElement BuildStatusBar()
        {
            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 2, 10, 2)
            };
            DockPanel.SetDock(bar, Dock.Bottom);

            var dock = new DockPanel();

            // Zoom control, rightmost: − 100 % + (Ctrl+molette works too).
            var zoomPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 0, 0, 0)
            };
            // Réinitialiser (flèche qui tourne — l'icône « arrow-counter-
            // clockwise » de Rémi, 14/09) + curseur 50–300 % (batch 34).
            var reset = new Button
            {
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                Focusable = false,
                ToolTip = "Revenir à 100 %",
                Content = Icons.Make("arrow-counter-clockwise", 13, Chrome.Ink)
            };
            reset.Click += delegate { ApplyZoom(100); };
            _zoomSlider = new Slider
            {
                Minimum = 50,
                Maximum = 300,
                Value = AppSettings.Zoom,
                TickFrequency = 10,
                IsSnapToTickEnabled = true,
                LargeChange = 10,
                SmallChange = 10,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0),
                ToolTip = "Zoom de la page (Ctrl+molette aussi)",
                Focusable = false
            };
            _zoomSlider.ValueChanged += delegate
            {
                if (_syncingZoom) return;
                ApplyZoom(_zoomSlider.Value);
            };
            _zoomLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 44,
                TextAlignment = TextAlignment.Center,
                ToolTip = "Zoom de la page (Ctrl+molette) — double-clic : 100 %",
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _zoomLabel.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) ApplyZoom(100);
            };
            zoomPanel.Children.Add(reset);
            zoomPanel.Children.Add(_zoomSlider);
            zoomPanel.Children.Add(_zoomLabel);
            DockPanel.SetDock(zoomPanel, Dock.Right);
            dock.Children.Add(zoomPanel);

            _statusRight = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_statusRight, Dock.Right);
            dock.Children.Add(_statusRight);

            _statusPages = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_statusPages, Dock.Right);
            dock.Children.Add(_statusPages);

            // Le préchauffage de l'ouverture (batch 30), VISIBLE MAIS
            // DISCRET — même doctrine que le différé : une ligne d'état,
            // jamais un modal, jamais un sablier.
            _statusWarmup = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(_statusWarmup, Dock.Right);
            dock.Children.Add(_statusWarmup);

            _statusLeft = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dock.Children.Add(_statusLeft);

            bar.Child = dock;
            return bar;
        }

        private void OnMarksToggled(bool visible)
        {
            AppSettings.ShowFormattingMarks = visible;
            _editor.SetFormattingMarks(visible);
            _sheetView.SetFormattingMarks(visible);
            AppSettings.Save();
        }

        /// <summary>Sets the page zoom (both editors), clamped to 50–300 %.</summary>
        private void ApplyZoom(double percent)
        {
            percent = Math.Max(50, Math.Min(300, Math.Round(percent / 10) * 10));
            AppSettings.Zoom = percent;
            _editor.SetZoom(percent / 100.0);
            _sheetView.SetZoom(percent / 100.0);
            _zoomLabel.Text = percent.ToString("0") + " %";
            if (_zoomSlider != null && Math.Abs(_zoomSlider.Value - percent) > 0.01)
            {
                _syncingZoom = true;
                try { _zoomSlider.Value = percent; } finally { _syncingZoom = false; }
            }
            AppSettings.Save();
        }

        // ============================================================= project lifecycle

        /// <summary>Installe un projet dans la fenêtre, l'anneau d'activité
        /// visible pendant (0.50.0) — le travail est synchrone.</summary>
        private void LoadProject(Project project, string path)
        {
            _busy.Run(delegate { LoadProjectCore(project, path); });
        }

        private void LoadProjectCore(Project project, string path)
        {
            // Les styles globaux (22/09) : le projet se met d'accord avec les
            // réglages avant que quiconque lise sa feuille. S'il a POUSSÉ ses
            // styles vers les réglages, il est marqué modifié : son empreinte
            // doit atteindre le .plot, sans quoi il repousserait à chaque
            // ouverture (revue 22/09). Tirer ne salit pas : relire n'est pas
            // modifier, et tirer à nouveau est sans effet.
            GlobalStyles.Sync(project);
            _project = project;
            _path = path;
            _current = null;
            _dirty = false;
            _readOnlyBanner.Visibility = project.ReadOnlyNewerFormat
                ? Visibility.Visible : Visibility.Collapsed;
            _history.Clear();
            _wordCache.Clear();
            _pageCountCache.Clear();
            _sessionGoal = 0;
            ResetSprint();
            _editor.SetStyleSheet(project.Styles);
            _editor.SetProject(project);
            _editor.ApplyPageSetup(project.Page);
            _editor.Clear();
            _sheetView.SetStyleSheet(project.Styles);
            _sheetView.SetProject(project);
            _sheetView.ApplyPageSetup(project.Page);
            _sheetView.Clear();
            _searchPanel.SetProject(project);
            _versionsPanel.SetProject(project);
            if (_compareWindow != null) _compareWindow.Close();
            _mediaView.Clear();
            _corkboard.Clear();
            _journalView.Clear();
            _pinnedPanel.SetProject(project, project.Styles);
            _binder.LoadProject(project, _history);
            _dictionaryView.Load(project); // le Lexique du rail édite par lui (18/09)
            _lexiconPanel.Show(null, true);
            _lexiconShown = false;
            ResolveSidePin();
            BeginRecovery(); // la session de secours (18/09)
            // Chauffe le cache de mots : les comptes existants deviennent la
            // référence des deltas du journal (rien n'est crédité à l'ouverture).
            ProjectWords();
            ShowItem(null);
            UpdateRecentMenu();
            UpdateTitle();
            UpdateStats();
            UpdateInspector();
            if (GlobalStyles.LastSyncPushed) MarkDirty();
            // Batch 30 : les textes se préparent dès l'ouverture, par
            // tranches oisives — le premier clic sur un chapitre est chaud.
            StartTextWarmup();
            try { _lastSavedBytes = path != null && File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (IOException) { _lastSavedBytes = 0; }
            ScheduleAchievementCheck();
            // Sélection initiale (batch 41) : le dernier item ouvert encore
            // valide (existant, hors Corbeille), sinon l'Accueil — un projet
            // neuf montre le tableau de bord, un roman rouvert son chapitre.
            // Par le chemin normal (rail, inspecteur, colonne de droite),
            // mais SANS réécrire la date du récent (A2) : le drapeau couvre
            // la restauration.
            var recents = Recents.Valid(project);
            var first = recents.Count > 0
                ? project.FindById(recents[0].ItemId)
                : project.Category(Project.KeyHome);
            if (first != null)
            {
                _restoringSelection = true;
                try { _binder.SelectItem(first.Id); }
                finally { _restoringSelection = false; }
            }
        }

        /// <summary>Renomme le projet — le crayon à côté du nom, à l'Accueil
        /// (batch 43). Le nom choisi prime désormais sur le nom du fichier.</summary>
        private void RenameProject()
        {
            if (_project == null) return;
            var answer = View.InputDialog.Ask(this, "Renommer le projet", "Nom du projet :", _project.Name);
            if (answer == null || answer.Trim().Length == 0) return;
            _project.Name = answer.Trim();
            MarkDirty();
            UpdateTitle();
            _homeView.Refresh();
            UpdateInspector();
        }

        /// <summary>Le nom du fichier ne s'impose plus au projet (batch 43) :
        /// un nom choisi par l'utilisateur (manifeste « name ») prime ; le nom
        /// du fichier ne sert que de repli au défaut « Sans titre ».</summary>
        private static void AdoptFileName(Project project, string path)
        {
            if (string.IsNullOrEmpty(project.Name) || project.Name == "Sans titre")
                project.Name = Path.GetFileNameWithoutExtension(path);
        }

        public void OpenFile(string path)
        {
            _busy.Begin();
            _busy.Pump();
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(path, warnings);
                InstallOpened(project, path, warnings);
            }
            catch (Exception error)
            {
                TryRecoverFromBackup(path, error);
            }
            finally { _busy.End(); }
        }

        /// <summary>L'ouverture depuis l'accueil (14/09) : la lecture du .plot
        /// (la part longue d'un gros projet) se fait HORS du fil d'interface,
        /// pour que l'indicateur de chargement de l'accueil vive ; le projet
        /// est ensuite installé sur le fil d'interface, comme OpenFile.
        /// PlotFile et le modèle ne touchent à rien de WPF. « finished » est
        /// toujours appelé, réussite ou non — l'accueil regarde HasProjectPath.</summary>
        public void OpenFileInBackground(string path, Action finished)
        {
            var warnings = new List<string>();
            _busy.Begin(); // rendu dans le BeginInvoke ci-dessous, réussite ou non
            System.Threading.Tasks.Task.Factory
                .StartNew(delegate { return PlotFile.Load(path, warnings); })
                .ContinueWith(delegate(System.Threading.Tasks.Task<Project> done)
                {
                    var failure = done.Exception == null ? null : done.Exception.GetBaseException();
                    var project = failure == null ? done.Result : null;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        try
                        {
                            if (failure != null) TryRecoverFromBackup(path, failure);
                            else InstallOpened(project, path, warnings);
                        }
                        catch (Exception error)
                        {
                            TryRecoverFromBackup(path, error);
                        }
                        finally { _busy.End(); }
                        if (finished != null) finished();
                    }));
                });
        }

        /// <summary>Le projet lu : installé, ajouté aux récents, l'accueil
        /// relâché, le .tmp orphelin nettoyé, les réserves montrées.</summary>
        private void InstallOpened(Project project, string path, List<string> warnings)
        {
            AdoptFileName(project, path);
            LoadProject(project, path);
            // Un projet est ouvert : l'accueil (13/09) se retire — quel
            // que soit le chemin qui a mené ici (tuile, sonde, argument).
            if (_welcome != null) _welcome.Release();
            AppSettings.AddRecentFile(path);
            AppSettings.Save();
            UpdateRecentMenu();

            // Un .tmp orphelin signale une sauvegarde interrompue (crash en
            // pleine écriture). Le .plot est resté intact — l'écriture est
            // atomique — mais le débris ne doit pas rester en silence.
            var orphan = path + ".tmp";
            if (File.Exists(orphan))
            {
                try { File.Delete(orphan); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                warnings.Insert(0, "Une sauvegarde précédente a été interrompue "
                    + "(fichier temporaire « .tmp » retrouvé, maintenant supprimé). "
                    + "Le projet ouvert est la dernière sauvegarde complète.");
            }

            if (warnings.Count > 0)
                MessageDialog.Show(this,
                    "Le projet s'est ouvert, avec des réserves :\n\n— "
                    + string.Join("\n— ", warnings.ToArray()),
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>The .plot itself is unreadable: offer the rolling .bak
        /// (written at every successful save) before giving up.</summary>
        private void TryRecoverFromBackup(string path, Exception error)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak))
            {
                MessageDialog.Show(this,
                    "Impossible d'ouvrir le projet :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var stamp = File.GetLastWriteTime(bak).ToString("dd/MM/yyyy HH:mm");
            var answer = MessageDialog.Show(this,
                "Impossible d'ouvrir le projet :\n" + error.Message + "\n\n"
                + "Une copie de secours existe (dernier enregistrement réussi, "
                + "du " + stamp + ").\nL'ouvrir à la place ?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(bak, warnings);
                AdoptFileName(project, path);
                // Le projet vit sous son chemin normal : le prochain Ctrl+S
                // remplacera le .plot corrompu. Marqué modifié pour que la
                // fermeture propose cet enregistrement.
                LoadProject(project, path);
                _dirty = true;
                UpdateTitle();
                if (warnings.Count > 0)
                    MessageDialog.Show(this,
                        "La copie de secours s'est ouverte, avec des réserves :\n\n— "
                        + string.Join("\n— ", warnings.ToArray()),
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception bakError)
            {
                MessageDialog.Show(this,
                    "La copie de secours est illisible elle aussi :\n" + bakError.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoNew()
        {
            if (!ConfirmDiscard()) return;
            LoadProject(Project.CreateNew(), null);
        }

        // ============================================================= accueil (13/09)

        private WelcomeWindow _welcome;
        private Border _welcomeVeil;
        private UIElement _shellRoot;

        /// <summary>Vrai quand un projet ENREGISTRÉ est ouvert — l'accueil
        /// s'en sert pour savoir si l'ouverture a réussi.</summary>
        public bool HasProjectPath { get { return _path != null; } }

        /// <summary>Montre l'accueil par-dessus une fenêtre vide et
        /// désactivée ; tout revient quand il se retire.</summary>
        public void ShowWelcome()
        {
            if (_welcome != null) return;
            // Un peu de couleur au démarrage (13/09) : assets/background.jpg
            // à côté de l'exe, en remplissage proportionnel derrière
            // l'accueil — son centre est blanc, l'accueil le recouvre.
            // Absent ou illisible : le voile blanc cassé, simplement.
            var background = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                System.IO.Path.Combine("assets", "background.jpg"));
            if (File.Exists(background))
            {
                try
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(background, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    _welcomeVeil.Background = new ImageBrush(image)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                }
                catch { }
            }
            _welcomeVeil.Visibility = Visibility.Visible;
            _shellRoot.IsEnabled = false;
            _welcome = new WelcomeWindow(this);
            _welcome.Closed += delegate
            {
                _welcome = null;
                _welcomeVeil.Visibility = Visibility.Collapsed;
                _shellRoot.IsEnabled = true;
            };
            _welcome.Show();
        }

        /// <summary>La tuile « + » de l'accueil : un projet n'existe qu'une
        /// fois enregistré quelque part — le dialogue d'enregistrement
        /// d'abord, le projet vide ensuite, écrit sur-le-champ.</summary>
        public bool NewProjectWithSaveDialog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = PlotFile.SaveFilter,
                FileName = "Sans titre" + PlotFile.Extension,
                Title = "Enregistrer le nouveau projet"
            };
            if (dialog.ShowDialog(this) != true) return false;
            LoadProject(Project.CreateNew(), null);
            _path = dialog.FileName;
            AdoptFileName(_project, _path);
            DoSave();
            if (!File.Exists(_path)) { _path = null; return false; }
            AppSettings.AddRecentFile(_path);
            AppSettings.Save();
            UpdateRecentMenu();
            UpdateTitle();
            return true;
        }

        /// <summary>Aide → Vérifier les mises à jour (standard de la famille
        /// Stargazer) : la vérification en fond, puis le verdict — et
        /// l'installation sur place si une version est publiée.</summary>
        private void CheckUpdates()
        {
            var version = AppVersion;
            System.Threading.Tasks.Task.Factory.StartNew(delegate { return Updater.Run(version); })
                .ContinueWith(delegate(System.Threading.Tasks.Task<Updater.Check> done)
                {
                    var check = done.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? done.Result : null;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        if (check == null)
                        {
                            MessageDialog.Show(this, "Vérification impossible.", "Mise à jour",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        if (!check.Available || check.Latest == null)
                        {
                            MessageDialog.Show(this, check.Message + " (" + version + ").", "Mise à jour",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        var install = MessageDialog.Show(this,
                            check.Message + ".\n\nL'installer et redémarrer Marabook ?\n"
                            + "(l'archive est téléchargée, l'application se ferme, les fichiers "
                            + "sont remplacés — vos projets et réglages restent — et Marabook redémarre)",
                            "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (install != MessageBoxResult.Yes) return;
                        if (!ConfirmDiscard()) return;
                        try
                        {
                            Updater.Install(check.Latest, AppDomain.CurrentDomain.BaseDirectory);
                            Application.Current.Shutdown();
                        }
                        catch (Exception failure)
                        {
                            MessageDialog.Show(this, "Mise à jour impossible : " + failure.Message,
                                "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }));
                });
        }

        private void DoOpen()
        {
            OpenProjectWithDialog(this);
        }

        /// <summary>« Ouvrir… » : le dialogue de fichier (posé sur owner —
        /// l'accueil quand il est là), puis OpenFile, qui relâche l'accueil.</summary>
        public void OpenProjectWithDialog(Window owner)
        {
            var path = AskProjectFile(owner);
            if (path != null) OpenFile(path);
        }

        /// <summary>Le dialogue de fichier seul (14/09) : le chemin choisi, ou
        /// null — l'accueil s'en sert pour ouvrir ensuite en fond, avec son
        /// indicateur de chargement.</summary>
        public string AskProjectFile(Window owner)
        {
            if (!ConfirmDiscard()) return null;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = PlotFile.OpenFilter };
            return dialog.ShowDialog(owner ?? this) == true ? dialog.FileName : null;
        }

        private bool _autosaveWarned; // un seul avertissement par panne de sauvegarde automatique

        private void DoSave()
        {
            SaveProject(false);
        }

        /// <summary>silent : la sauvegarde automatique — un échec (fichier
        /// verrouillé par OneDrive, un antivirus…) se dit une fois, en toast,
        /// pas en boîte modale toutes les deux minutes (revue 22/09).</summary>
        private void SaveProject(bool silent) // un seul « DoSave » : les sondes le cherchent par réflexion
        {
            if (_path == null) { if (!silent) DoSaveAs(); return; }
            if (_project.ReadOnlyNewerFormat)
            {
                MessageDialog.Show(this,
                    "Ce projet a été enregistré avec une version plus récente de Marabook.\n"
                    + "Il est ouvert en lecture seule pour ne rien détruire : "
                    + "l'enregistrement est désactivé.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                // Every editable view must flush before writing: the sheet body
                // (SheetView._body) is a second EditorView the plain
                // _editor.Commit() never reached — typing in a sheet then Ctrl+S
                // used to lose the text.
                _busy.Run(delegate
                {
                    CommitActive();
                    PlotFile.Save(_project, _path);
                });
                _dirty = false;
                DropRecovery(); // le .plot complet est à jour : le secours est périmé (18/09)
                AppSettings.AddRecentFile(_path);
                AppSettings.Save();
                UpdateRecentMenu();
                UpdateTitle();
                UpdateInspector();
                try { _lastSavedBytes = new FileInfo(_path).Length; } catch (IOException) { }
                _dirtySince = null;
                UnlockAchievement(Achievements.FirstProject); // le premier projet enregistré
                ScheduleAchievementCheck(); // « Damn boi, he thicc! »
                _autosaveWarned = false;
                // Le toast « enregistré » (0.50.0) : à la demande explicite
                // seulement — l'automatique reste muet.
                if (!silent) ShowSavedToast();
            }
            catch (Exception error)
            {
                if (silent)
                {
                    if (_autosaveWarned) return;
                    _autosaveWarned = true;
                    ShowNotice(NoticeToast.Build("warning-bold", "Sauvegarde automatique impossible",
                        error.Message + "\n\nLe secours continue d'écrire vos textes ; réessayez Fichier › Enregistrer quand le fichier sera libre.",
                        "Enregistrer maintenant", delegate { SaveProject(false); }, "Plus tard", null));
                    return;
                }
                MessageDialog.Show(this,
                    "Impossible d'enregistrer le projet :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoSaveAs()
        {
            if (_project.ReadOnlyNewerFormat)
            {
                // Re-writing under another name would silently drop every field
                // this version does not know — same destruction, new path.
                MessageDialog.Show(this,
                    "Ce projet a été enregistré avec une version plus récente de Marabook.\n"
                    + "L'enregistrer avec cette version détruirait les données "
                    + "qu'elle ne connaît pas : ouvrez-le avec la version qui l'a créé.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = PlotFile.SaveFilter,
                FileName = _project.Name + PlotFile.Extension
            };
            if (dialog.ShowDialog(this) != true) return;
            _path = dialog.FileName;
            AdoptFileName(_project, _path);
            DoSave();
        }

        private void Autosave()
        {
            if (_project.ReadOnlyNewerFormat) return; // never write a newer format
            if (_dirty && _path != null) SaveProject(true);
        }

        /// <summary>True when it is safe to drop the current project.</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            if (_project.ReadOnlyNewerFormat)
            {
                // Saving is impossible in this state: offer to leave anyway.
                var leave = MessageDialog.Show(this,
                    "Ce projet est ouvert en lecture seule (format plus récent) :\n"
                    + "les modifications ne peuvent pas être enregistrées.\n"
                    + "Continuer et les abandonner ?",
                    AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return leave == MessageBoxResult.Yes;
            }
            var answer = MessageDialog.Show(this,
                "Enregistrer les modifications du projet « " + _project.Name + " » ?",
                AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes)
            {
                DoSave();
                return !_dirty; // save may have been cancelled in the Save As dialog
            }
            return true;
        }

        private void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            // L'accueil se ferme avec l'app (c'est sa seule autre sortie).
            if (_welcome != null) _welcome.Release();
            // Quitter n'attend jamais la correction différée (batch 29).
            _editor.ShutdownProofing();
            EndRecovery(); // fermeture propre : témoin et secours retirés (18/09)
            AppSettings.BinderWidth = _binderCol.Width.Value > 0 ? _binderCol.Width.Value : AppSettings.BinderWidth;
            RememberRightWidth();
            AppSettings.Save();
        }

        // ============================================================= editing

        // ============================================================= cartes mentales (22/09)

        /// <summary>La carte dans l'éditeur du module — ou, sans module, une
        /// tuile qui invite à l'installer. Un seul éditeur par session.</summary>
        private void ShowMindMap(BinderItem item)
        {
            var provider = Extensions.ModuleRegistry.MindMaps;
            if (provider == null)
            {
                _mindMapEditor = null;
                _mindMapItem = null;
                _mindMapHost.Children.Clear();
                _mindMapHost.Children.Add(MindMapPlaceholder(item));
                return;
            }
            if (_mindMapEditor == null)
            {
                _mindMapEditor = provider.CreateEditor();
                _mindMapEditor.Changed += delegate { MarkDirty(); ScheduleAchievementCheck(); };
            }
            if (_mindMapHost.Children.Count != 1 || _mindMapHost.Children[0] != _mindMapEditor.View)
            {
                _mindMapHost.Children.Clear();
                _mindMapHost.Children.Add(_mindMapEditor.View);
            }
            _mindMapItem = item;
            _mindMapEditor.Load(item.Id, item.Title, item.MapBytes);
        }

        /// <summary>Les octets de la carte modifiée reviennent dans son élément
        /// (avant un enregistrement, un changement de vue, une tuile).</summary>
        private void CommitMindMap()
        {
            if (_mindMapEditor == null || _mindMapItem == null || !_mindMapEditor.IsDirty) return;
            try { _mindMapItem.MapBytes = _mindMapEditor.Save(_mindMapItem.MapBytes); }
            catch (Exception error) { MessageDialog.Show(this, "La carte n'a pas pu être enregistrée : " + error.Message, "Cartes mentales", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private UIElement MindMapPlaceholder(BinderItem item)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = MindMaps.Inspect(item.MapBytes).Label + "\n\nCette carte mentale s'ouvre avec le module Mental-o. Installez-le depuis Préférences › DLC ; la carte est gardée telle quelle en attendant.",
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            var open = Buttons.Text("Ouvrir les Préférences…", "Préférences › DLC : installer Mental-o", Buttons.Bar, Buttons.Look.Primary);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.Margin = new Thickness(0, 14, 0, 0);
            open.Click += delegate { OpenPreferences(); };
            panel.Children.Add(open);
            return panel;
        }

        // --- l'hôte que Marabook prête aux modules à code (IModuleHost)
        public event Action ThemeChanged;
        Window Extensions.IModuleHost.MainWindow { get { return this; } }
        bool Extensions.IModuleHost.DarkTheme { get { return AppSettings.DarkTheme; } }
        MessageBoxResult Extensions.IModuleHost.Message(string text, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            return MessageDialog.Show(this, text, title, buttons, image);
        }
        string Extensions.IModuleHost.Ask(string title, string prompt, string initial)
        {
            return InputDialog.Ask(this, title, prompt, initial ?? "");
        }
        void Extensions.IModuleHost.MarkDirty() { MarkDirty(); }
        void Extensions.IModuleHost.NavigateTo(string itemId)
        {
            if (!string.IsNullOrEmpty(itemId) && _project != null && _project.FindById(itemId) != null) _binder.SelectItem(itemId);
        }

        private void CommitActive()
        {
            CommitMindMap(); // les octets de la carte ouverte (22/09)
            // L'éditeur d'écrits n'a rien à rincer : les pages composées
            // écrivent directement dans le pivot (le classique et son Commit
            // ont disparu le 13/09).
            if (_sheetView.HasItem) _sheetView.Commit();
            // Template zones normally commit on focus loss, but a pending
            // debounced edit must not be lost by a save that races it.
            _templateView.CommitZones();
        }

        // Historique de PANNEAUX (batch 28) : précédent/suivant aux boutons
        // latéraux de la souris — d'un texte ouvert au panneau livre d'avant.
        private readonly List<string> _navBack = new List<string>();
        private readonly List<string> _navForward = new List<string>();
        private bool _navTravelling;

        /// <summary>Remonte (ou redescend) l'historique des panneaux ouverts.
        /// Un élément disparu entre-temps est sauté sans bruit.</summary>
        private void NavigateHistory(bool back)
        {
            var source = back ? _navBack : _navForward;
            while (source.Count > 0)
            {
                var targetId = source[source.Count - 1];
                source.RemoveAt(source.Count - 1);
                var target = targetId.Length == 0 ? null : _project.FindById(targetId);
                if (targetId.Length > 0 && target == null) continue; // disparu
                var other = back ? _navForward : _navBack;
                other.Add(_current == null ? "" : _current.Id);
                _navTravelling = true;
                try
                {
                    OnBinderSelection(target);
                }
                finally { _navTravelling = false; }
                return;
            }
        }

        private void OnBinderSelection(BinderItem item)
        {
            // Phantom selections: hiding the view that holds keyboard focus
            // makes WPF re-focus a TreeViewItem, which SELECTS ITSELF
            // (TreeViewItem.OnGotFocus → Select) — reentering here mid-open
            // and flipping the center back (the « double-clic obligatoire »).
            if (_navigating) return;

            // Re-clicking the already-open item must not reload it (that would
            // commit + reset the caret); it only re-shows its view if another
            // view took over, and refreshes the side panels.
            if (item != null && item == _current && IsItemViewVisible(item))
            {
                UpdateInspector();
                UpdateStats();
                return;
            }

            // L'historique enregistre le panneau qu'on quitte — sauf pendant
            // un voyage dans l'historique lui-même.
            if (!_navTravelling && !ReferenceEquals(_current, item))
            {
                _navBack.Add(_current == null ? "" : _current.Id);
                if (_navBack.Count > 60) _navBack.RemoveAt(0);
                _navForward.Clear();
            }
            _navigating = true;
            try
            {
                CommitActive();
                _current = item;
                ShowItem(item);
            }
            finally
            {
                _navigating = false;
            }
            // Les récents (batch 41) : l'OUVERTURE d'un item — jamais une
            // racine, jamais la sélection fantôme (garde ci-dessus), jamais
            // la restauration à l'ouverture du projet (A2). Sans MarkDirty :
            // relire n'est pas modifier, la liste part avec la prochaine
            // vraie sauvegarde (A1). Hors HistoryManager : Ctrl+Z défait une
            // épingle, jamais une visite. Rien à voir avec _navBack (A4).
            if (item != null && !item.IsCategory && !_restoringSelection && _project != null)
                Recents.Touch(_project.Recents, item.Id, Recents.Now());
            // The phantom may have moved the tree's selection while we were
            // opening: pull it back onto the item actually shown — sans
            // BringIntoView : le défilement déplaçait la ligne sous la souris
            // entre les deux clics d'un double-clic (renommage cassé).
            if (item != null) _binder.SelectItem(item.Id, false);
            UpdateInspector();
            UpdateStats();
            _searchPanel.SetCurrent(item);
            _versionsPanel.SetCurrent(item);
        }

        /// <summary>True when the center already displays THIS item's view —
        /// identity-checked, never visibility alone: a stale _current with a
        /// merely-visible view used to swallow the first sidebar click (the
        /// « double-clic obligatoire » from a book's corkboard).</summary>
        private bool IsItemViewVisible(BinderItem item)
        {
            if (item.Kind == ItemKind.Text)
                return _editor.Visibility == Visibility.Visible && _editor.ShowsItem(item);
            if (item.Kind == ItemKind.Sheet)
                return _sheetView.Visibility == Visibility.Visible && _sheetView.ShowsItem(item);
            if (item.Kind == ItemKind.Media)
                return _mediaView.Visibility == Visibility.Visible;
            if (item.Kind == ItemKind.Book)
                return _bookView.Visibility == Visibility.Visible && _bookView.ShowsItem(item);
            if (item.Kind == ItemKind.PageTemplate)
                return _templateView.Visibility == Visibility.Visible && _templateView.ShowsItem(item);
            if (item.IsCategory && item.CategoryKey == Project.KeySheets)
                return _sheetLibrary.Visibility == Visibility.Visible;
            if (item.IsCategory && item.CategoryKey == Project.KeyDictionary)
                return _dictionaryView.Visibility == Visibility.Visible;
            if (item.IsHomeRoot)
                return _homeView.Visibility == Visibility.Visible;
            if (item.Kind == ItemKind.Plan)
                return _planView.Visibility == Visibility.Visible && _planView.ShowsItem(item);
            return _corkboard.Visibility == Visibility.Visible && _corkboard.ShowsItem(item);
        }

        /// <summary>Ouvre un élément dans la vue qui lui revient, l'anneau
        /// d'activité visible pendant la composition (0.50.0).</summary>
        private void ShowItem(BinderItem item)
        {
            _busy.Run(delegate { ShowItemCore(item); });
        }

        private void ShowItemCore(BinderItem item)
        {
            // LE bug du « double-clic obligatoire », enfin élucidé : masquer la
            // vue qui porte le focus clavier fait retomber ce focus sur un
            // TreeViewItem de la Pile — et un TreeViewItem SE SÉLECTIONNE
            // quand il reçoit le focus (TreeViewItem.OnGotFocus → Select).
            // La sélection fantôme rouvrait alors la vue précédente — le
            // remède est logique, pas focal : OnBinderSelection est gardé
            // contre la réentrance pendant ShowItem, puis ramène la sélection
            // de l'arbre sur l'élément réellement ouvert.

            _editor.Visibility = Visibility.Collapsed;
            _sheetView.Visibility = Visibility.Collapsed;
            _sheetLibrary.Visibility = Visibility.Collapsed;
            _dictionaryView.Visibility = Visibility.Collapsed;
            _homeView.Visibility = Visibility.Collapsed;
            _planView.Visibility = Visibility.Collapsed;
            _planView.Clear();
            CommitMindMap(); // la carte quittée rend ses octets (22/09)
            _mindMapHost.Visibility = Visibility.Collapsed;
            _corkboard.Visibility = Visibility.Collapsed;
            _bookView.Visibility = Visibility.Collapsed;
            _bookView.Clear();
            _templateView.Visibility = Visibility.Collapsed;
            _templateView.Clear();
            _mediaView.Visibility = Visibility.Collapsed;
            _journalView.Visibility = Visibility.Collapsed;
            if (_journalOpen)
            {
                _journalOpen = false;
                ApplyPanelVisibility(); // l'inspecteur revient en quittant le journal
            }
            _placeholder.Visibility = Visibility.Collapsed;
            if (item == null || item.Kind != ItemKind.Text) _statusPages.Text = "";

            if (item != null && item.Kind == ItemKind.Text)
            {
                _sheetView.Clear();
                // The document's own page setup wins over the project default
                // (books stamp their gabarit on their documents), and a book
                // document opens at its REAL folio in the book, with its
                // header/footer decor (menu Gabarit ou gabarit de pages).
                if (ExtraPages.IsDynamic(item)) RegenerateDynamic(item); // table des matières, index, notes, glossaire à jour
                _editor.FolioOffset = ComputeFolioOffset(item);
                _editor.Decor = PageDecor.For(item, _project);
                _editor.ApplyPageSetup(item.Page ?? _project.Page);
                _editor.LoadItem(item);
                _editor.RefreshPlanButton(); // « Plan » si une colonne le raconte (b35)
                _editor.Visibility = Visibility.Visible;
                _editor.FocusEditor();
                return;
            }
            if (item != null && item.Kind == ItemKind.PageTemplate)
            {
                _editor.Clear();
                _sheetView.Clear();
                _templateView.Load(item, _project);
                _templateView.Visibility = Visibility.Visible;
                _templateView.Focus();
                return;
            }
            if (item != null && item.Kind == ItemKind.Book)
            {
                _editor.Clear();
                _sheetView.Clear();
                _bookView.Load(item, _history, _project);
                _bookView.Visibility = Visibility.Visible;
                _bookView.Focus(); // le focus logique quitte la Pile
                return;
            }
            if (item != null && item.Kind == ItemKind.MindMap)
            {
                _editor.Clear();
                _sheetView.Clear();
                ShowMindMap(item);
                _mindMapHost.Visibility = Visibility.Visible;
                return;
            }
            if (item != null && item.Kind == ItemKind.Plan)
            {
                _editor.Clear();
                _sheetView.Clear();
                _planView.Load(item, _project);
                _planView.Visibility = Visibility.Visible;
                _planView.Focus();
                return;
            }
            if (item != null && item.Kind == ItemKind.Sheet)
            {
                _editor.Clear();
                _sheetView.LoadItem(item, _project.FindTemplate(item.TemplateId));
                _sheetView.Visibility = Visibility.Visible;
                return;
            }
            if (item != null && item.Kind == ItemKind.Media)
            {
                _editor.Clear();
                _sheetView.Clear();
                _mediaView.LoadItem(item);
                _mediaView.Visibility = Visibility.Visible;
                return;
            }
            // La catégorie « Fiches » ouvre la BIBLIOTHÈQUE (batch 31) :
            // rangées par catégorie de fiches, cartes, recherche par nom.
            if (item != null && item.IsCategory
                && item.CategoryKey == Project.KeySheets)
            {
                _editor.Clear();
                _sheetView.Clear();
                _sheetLibrary.Load(_project, _history, null);
                _sheetLibrary.Visibility = Visibility.Visible;
                _sheetLibrary.Focus();
                return;
            }
            // Un DOSSIER de Fiches (14/09) : la même bibliothèque, à sa
            // portée — tuiles de fiches par catégorie, sous-dossiers.
            if (item != null && item.Kind == ItemKind.Folder
                && item.RootCategory().CategoryKey == Project.KeySheets)
            {
                _editor.Clear();
                _sheetView.Clear();
                _sheetLibrary.Load(_project, _history, item);
                _sheetLibrary.Visibility = Visibility.Visible;
                _sheetLibrary.Focus();
                return;
            }
            // La racine « Accueil » (batch 41) : les quatre blocs.
            if (item != null && item.IsHomeRoot)
            {
                _editor.Clear();
                _sheetView.Clear();
                _homeView.Load(_project);
                _homeView.Visibility = Visibility.Visible;
                _homeView.Focus();
                return;
            }
            // La racine « Dictionnaire » (batch 33) ouvre l'écran du
            // dictionnaire personnel : entrées, natures, formes acceptées.
            if (item != null && item.IsCategory
                && item.CategoryKey == Project.KeyDictionary)
            {
                _editor.Clear();
                _sheetView.Clear();
                _dictionaryView.Load(_project);
                _dictionaryView.Visibility = Visibility.Visible;
                _dictionaryView.Focus();
                return;
            }
            // Corkboard: true containers only (folders, categories). A text
            // that carries children still opens as a text, Scrivener-style.
            if (item != null && item.IsContainer)
            {
                _editor.Clear();
                _sheetView.Clear();
                _corkboard.Load(item, _history, _project);
                _corkboard.Visibility = Visibility.Visible;
                _corkboard.Focus();
                return;
            }
            _editor.Clear();
            _sheetView.Clear();
            _placeholder.Visibility = Visibility.Visible;
            _placeholder.Text = "Sélectionnez un élément dans la Pile,\nou créez un écrit (Ctrl+T).";
        }

        /// <summary>Ouvre le Journal perso au centre (entrée fixe de la Pile).
        /// Aucun élément d'arbre : la sélection courante est simplement rendue,
        /// et tout clic dans la Pile reprend la main.</summary>
        private void ShowJournal()
        {
            if (_journalView.Visibility == Visibility.Visible)
            {
                _journalView.Refresh();
                return;
            }
            _navigating = true;
            try
            {
                CommitActive();
                _current = null;
                ShowItem(null);
                _placeholder.Visibility = Visibility.Collapsed;
                _journalView.Load(_project);
                _journalView.Visibility = Visibility.Visible;
                RememberRightWidth();
                _journalOpen = true;
                ApplyPanelVisibility();
            }
            finally
            {
                _navigating = false;
            }
            UpdateInspector();
            UpdateStats();
        }

        // ----- routing to whichever editor is on screen -----

        private void ShowSearchInActive()
        {
            if (_sheetView.Visibility == Visibility.Visible) _sheetView.ShowSearch();
            else _editor.ShowSearch();
        }

        private void InsertFootnoteInActive()
        {
            if (_sheetView.Visibility == Visibility.Visible) _sheetView.InsertFootnote();
            else _editor.InsertFootnote();
        }

        private void InsertLinkInActive()
        {
            if (_editor.Visibility != Visibility.Visible
                && _sheetView.Visibility != Visibility.Visible) return;
            var titles = new List<string>();
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet) titles.Add(item.Title);
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Text && (_current == null || item != _current))
                    titles.Add(item.Title);
            var title = LinkDialog.Ask(this, titles);
            if (title == null) return;
            if (_sheetView.Visibility == Visibility.Visible) _sheetView.InsertWikiLink(title);
            else _editor.InsertWikiLink(title);
        }

        private void NavigateToTitle(string title)
        {
            var target = _project.FindByTitle(title);
            if (target != null)
            {
                _binder.SelectItem(target.Id);
                return;
            }
            var answer = MessageDialog.Show(this,
                "Aucun élément ne s'intitule « " + title + " ».\nCréer une fiche à ce nom ?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            var sheets = _project.Category(Project.KeySheets);
            // Une fiche née d'un [[lien]] rejoint la première catégorie
            // (batch 31) — déplaçable ensuite depuis la bibliothèque.
            var home = _project.SheetCategories.Count > 0
                ? _project.SheetCategories[0] : null;
            var sheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                CategoryId = home != null ? home.Id : null,
                TemplateId = home != null ? home.TemplateId : null
            };
            _history.Run(new History.AddItemAction(sheets, sheet, -1));
            MarkDirty();
            _binder.SelectItem(sheet.Id);
        }

        private void OnEditorEdited()
        {
            MarkDirty();
            // Le miroir épinglé suit l'original en cours de frappe (b47).
            if (_sidePin != null && _sidePin == _current) { _pinTimer.Stop(); _pinTimer.Start(); }
            // La capture quotidienne (b38, lot C) : à la première modification
            // du jour, l'état d'AVANT la frappe (la pile locale du composé s'en
            // souvient) — une par jour et par item, débrayable (Préférences).
            if (_current != null && _project != null && AppSettings.DailySnapshot
                && SnapshotStore.GuardDaily(_project, _current,
                    _current.Kind == ItemKind.Text ? _editor.DocumentAtOpen() : null,
                    true, AppSettings.SnapshotCap) != null)
                OnSnapshotsChanged();
            // The edited document's page count is stale (book folio offsets).
            if (_current != null)
            {
                // La composition à l'écran connaît déjà le compte (22/09) :
                // rien à recomposer plus tard pour la Pile ou le livre.
                var pages = _editor.PrintPageCount;
                if (pages.HasValue) _pageCountCache[_current.Id] = pages.Value;
                else _pageCountCache.Remove(_current.Id);
            }
            _statsTimer.Stop();
            _statsTimer.Start();
        }

        private void OnSynopsisChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingInspector || _current == null || _current.IsCategory) return;
            _current.Synopsis = _synopsisBox.Text;
            MarkDirty();
        }

        private void OnNotesChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingInspector || _current == null || _current.IsCategory) return;
            _current.Notes = _notesBox.Text;
            MarkDirty();
        }

        /// <summary>Unified Ctrl+Z / Ctrl+Y: the text editor claims the action
        /// while the writer types in it (its own native stack), the Binder
        /// history takes over otherwise. Prevents the window-level gesture from
        /// ever swallowing a text undo/redo into the (often empty) Pile stack.</summary>
        private void DoUndo()
        {
            if (_editor.Visibility == Visibility.Visible && _editor.TryUndo()) return;
            if (_sheetView.Visibility == Visibility.Visible && _sheetView.TryUndo()) return;
            if (!_history.CanUndo) return;
            _history.Undo();
            AfterHistoryJump();
        }

        private void DoRedo()
        {
            if (_editor.Visibility == Visibility.Visible && _editor.TryRedo()) return;
            if (_sheetView.Visibility == Visibility.Visible && _sheetView.TryRedo()) return;
            if (!_history.CanRedo) return;
            _history.Redo();
            AfterHistoryJump();
        }

        private void AfterHistoryJump()
        {
            MarkDirty();
            // The current item may have been removed (undone add) or brought back.
            if (_current != null && _project.FindById(_current.Id) == null)
            {
                _current = null;
                ShowItem(null);
                UpdateInspector();
                UpdateStats();
            }
        }

        private void OnHistoryChanged()
        {
            _undoMenu.IsEnabled = _history.CanUndo;
            _redoMenu.IsEnabled = _history.CanRedo;
            ScheduleAchievementCheck(); // états, épingles, fiches, plans…
        }

        private void MarkDirty()
        {
            _recoveryDirty = true; // le secours suit chaque modification (18/09)
            if (_dirty) return;
            _dirty = true;
            if (_dirtySince == null) _dirtySince = DateTime.Now; // « Vivre dangereusement »
            UpdateTitle();
        }

        // ============================================================= import / export

        private const string DocumentExportFilter =
            "Document Word (*.docx)|*.docx|Document OpenDocument (*.odt)|*.odt|" +
            "Texte enrichi (*.rtf)|*.rtf|Markdown (*.md)|*.md|Texte brut (*.txt)|*.txt";
        private const string DocumentImportFilter =
            "Documents (*.docx;*.odt;*.rtf;*.md;*.txt;*.doc;*.gdoc)|*.docx;*.odt;*.rtf;*.md;*.txt;*.doc;*.gdoc|" +
            "Tous les fichiers (*.*)|*.*";

        private void ImportDocuments()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = DocumentImportFilter,
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true) return;

            var parent = _binder.CurrentContainer();
            var items = new List<BinderItem>();
            var errors = new List<string>();
            foreach (var path in dialog.FileNames)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var document = ImportOneDocument(path);
                    if (document == null) continue; // .gdoc guidance already shown
                    items.Add(new BinderItem { Kind = ItemKind.Text, Title = name, Document = document });
                }
                catch (Exception error)
                {
                    errors.Add(name + " — " + error.Message);
                }
            }
            if (items.Count > 0)
            {
                _history.Run(new History.AddItemsAction(parent, items));
                MarkDirty();
                // Imports may have merged new named styles into the sheet.
                _editor.SetStyleSheet(_project.Styles);
                _sheetView.SetStyleSheet(_project.Styles);
                _binder.SelectItem(items[items.Count - 1].Id);
            }
            if (errors.Count > 0)
                MessageDialog.Show(this, "Documents non importés :\n\n" + string.Join("\n", errors.ToArray()),
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            // Les personnages du texte importé (b49) : proposés, jamais imposés.
            if (items.Count > 0)
                OfferCharacters(items, items.Count == 1 ? items[0].Title : items.Count + " documents importés", false);
        }

        private TextDocument ImportOneDocument(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            // Les images du document entrent dans le magasin du projet (23/09).
            if (ext == ".docx") return Exchange.Docx.Import(path, _project.Styles, _project);
            if (ext == ".odt") return Exchange.Odt.Import(path, _project.Styles, _project);
            if (ext == ".rtf") return Exchange.Rtf.Import(path, _project.Styles);
            if (ext == ".md" || ext == ".markdown")
                return Exchange.MarkdownExchange.Import(File.ReadAllText(path));
            if (ext == ".txt") return TextDocument.FromPlainText(File.ReadAllText(path));
            if (ext == ".doc")
                return Exchange.Docx.Import(Exchange.ExternalBridge.DocToDocx(path), _project.Styles, _project);
            if (ext == ".gdoc")
            {
                MessageDialog.Show(this, Exchange.ExternalBridge.GdocGuidance,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            throw new InvalidOperationException("format non pris en charge (" + ext + ")");
        }

        private void ImportScrivener()
        {
            if (!ConfirmDiscard()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Exchange.Scrivener.Filter };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var project = Exchange.Scrivener.Import(dialog.FileName);
                LoadProject(project, null);
                _dirty = true; // freshly migrated, not yet saved as .plot
                UpdateTitle();
                MessageDialog.Show(this,
                    "Projet Scrivener importé. Pensez à l'enregistrer au format .plot (Ctrl+S).",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Import Scrivener impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCurrentItem()
        {
            CommitActive();
            TextDocument document = null;
            if (_current != null && _current.Kind == ItemKind.Text) document = _current.Document;
            else if (_current != null && _current.Kind == ItemKind.Sheet) document = _current.Document;
            if (document == null)
            {
                MessageDialog.Show(this, "Sélectionnez d'abord un écrit (ou une fiche) à exporter.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ExportDocument(document, _current.Title);
        }

        /// <summary>Export from a corkboard card's ⋮ menu.</summary>
        private void ExportItem(BinderItem item)
        {
            if (item == null || (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet)) return;
            CommitActive();
            ExportDocument(item.Document, item.Title);
        }

        private void CompileManuscript()
        {
            CommitActive();
            var request = CompileDialog.Show(this, _project);
            if (request == null) return;
            var manuscript = Exchange.Compiler.Build(_project, request.Root, request.Options);
            ExportDocument(manuscript, _project.Name, request.Root);
        }

        /// <summary>scope : l'élément dont le séparateur de scène fait foi (la
        /// racine compilée) — sinon l'élément ouvert (revue 22/09).</summary>
        private void ExportDocument(TextDocument document, string defaultName, BinderItem scope = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = DocumentExportFilter,
                FileName = SafeFileName(defaultName)
            };
            if (dialog.ShowDialog(this) != true) return;
            // Les [[liens]] restent dans l'application (18/09) : à l'export,
            // les marques tombent, les mots restent tels quels.
            document = Links.Strip(document);
            var path = dialog.FileName;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var exportStyles = _project.Styles.EffectiveFor(scope ?? _current); // le séparateur du livre (22/09)
                var commentsAuthor = Defaults.Or(_project.Author, Defaults.Author); // Préférences › Auteur (22/09)
                if (ext == ".docx") Exchange.Docx.Export(document, exportStyles, path, _project.Page, commentsAuthor); // annotations → commentaires Word (b49)
                else if (ext == ".odt")
                    Exchange.Odt.Export(Exchange.Compiler.FlattenLists(document), exportStyles, path);
                else if (ext == ".rtf")
                    Exchange.Rtf.Export(Exchange.Compiler.FlattenRules(document), exportStyles, path, _project);
                else if (ext == ".md")
                    File.WriteAllText(path, Exchange.MarkdownExchange.Export(document), new System.Text.UTF8Encoding(false));
                else
                    File.WriteAllText(path, Exchange.Compiler.FlattenLists(document).ToPlainText(),
                        new System.Text.UTF8Encoding(false));
                MessageDialog.Show(this, "Export terminé :\n" + path,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Export impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SafeFileName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "document" : sb.ToString();
        }

        // ============================================================= styles

        private void OpenStylesDialog()
        {
            CommitActive();
            var edited = StylesDialog.Show(this, _project.Styles, StyleScopeContext.ForItem(_project, _current));
            if (edited == null) return;
            _project.Styles = edited;
            ApplyStyleSheet();
            if (_bookView.Visibility == Visibility.Visible) _bookView.RefreshStyles(); // feuille remplacée (revue 22/09)
            MarkDirty();
        }

        /// <summary>La feuille du projet a changé (dialogue, onglet Styles du
        /// livre, Préférences) : les styles globaux remontent aux réglages,
        /// l'éditeur et les fiches se rechargent.</summary>
        private void ApplyStyleSheet()
        {
            GlobalStyles.Push(_project);
            _editor.SetStyleSheet(_project.Styles);
            _editor.Reload();
            _sheetView.SetStyleSheet(_project.Styles);
            _sheetView.ReloadBody();
        }

        private void OpenTemplatesDialog()
        {
            CommitActive();
            var edited = TemplatesDialog.Show(this, _project.Templates, _project);
            if (edited == null) return;
            _project.Templates = edited;
            // Re-render the current sheet: its fields may have changed.
            if (_current != null && _current.Kind == ItemKind.Sheet)
                _sheetView.LoadItem(_current, _project.FindTemplate(_current.TemplateId));
            MarkDirty();
        }

        // ============================================================= page setup

        private void InsertPageBreakInActive()
        {
            if (_editor.Visibility == Visibility.Visible) _editor.InsertPageBreak();
        }

        private void InsertImageInActive()
        {
            if (_sheetView.Visibility == Visibility.Visible) _sheetView.InsertImage();
            else if (_editor.Visibility == Visibility.Visible) _editor.InsertImage();
        }

        private void RouteToActiveEditor(string what)
        {
            var sheet = _sheetView.Visibility == Visibility.Visible;
            if (!sheet && _editor.Visibility != Visibility.Visible) return;
            if (what == "rule") { if (sheet) _sheetView.InsertRule(); else _editor.InsertRule(); }
            else if (what == "separator") { if (sheet) _sheetView.InsertSeparator(); else _editor.InsertSeparator(); }
        }

        // « Paramètres du projet » a disparu le 22/09 : l'auteur vit dans
        // Préférences › Auteur (et par livre), le séparateur de scène est un
        // style (Préférences › Styles globaux, ou l'onglet Styles du livre).

        // ============================================================= printing (phase 4a)

        /// <summary>What preview/print applies to: the open text or sheet, or
        /// the selected folder/category assembled in reading order (continuous
        /// pagination across its documents).</summary>
        private TextDocument BuildPrintable(out string name)
        {
            PageSetup setup;
            return BuildPrintable(out name, out setup);
        }

        private TextDocument BuildPrintable(out string name, out PageSetup setup)
        {
            name = null;
            setup = _project.Page;
            CommitActive();
            if (_current != null
                && (_current.Kind == ItemKind.Text || _current.Kind == ItemKind.Sheet))
            {
                name = _current.Title;
                if (_current.Page != null) setup = _current.Page;
                return Links.Strip(_current.Document); // marques de [[liens]] retirées (18/09)
            }
            if (_current != null && _current.IsContainer)
            {
                name = _current.Title;
                // A book's pages follow its gabarit, whatever the project says.
                if (_current.Kind == ItemKind.Book && _current.Book != null)
                    setup = _current.Book.Template;
                return Links.Strip(Exchange.Compiler.Build(_project, _current, new Exchange.CompileOptions
                {
                    TitlePage = false,
                    ChapterHeadings = false,
                    PageBreakPerText = true
                }));
            }
            MessageDialog.Show(this,
                "Sélectionnez un écrit, une fiche ou un dossier à mettre en pages.",
                AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        private void ShowPrintPreview()
        {
            string name;
            PageSetup setup;
            var document = BuildPrintable(out name, out setup);
            if (document == null) return;
            try
            {
                var offset = _current != null && _current.Kind == ItemKind.Text
                    ? ComputeFolioOffset(_current) : 0;
                var decor = _current != null && _current.Kind == ItemKind.Text
                    ? PageDecor.For(_current, _project) : null;
                Print.Printing.ShowPreview(this, document, _project.Styles.EffectiveFor(_current), _project,
                    name, setup, offset, decor);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Aperçu impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintCurrent()
        {
            string name;
            PageSetup setup;
            var document = BuildPrintable(out name, out setup);
            if (document == null) return;
            try
            {
                var offset = _current != null && _current.Kind == ItemKind.Text
                    ? ComputeFolioOffset(_current) : 0;
                var decor = _current != null && _current.Kind == ItemKind.Text
                    ? PageDecor.For(_current, _project) : null;
                Print.Printing.Print(document, _project.Styles.EffectiveFor(_current), _project,
                    AppName + " — " + name, setup, offset, decor);
                UnlockAchievement(Achievements.OldSchool); // « À l'ancienne »
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Impression impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>4b-2 : the home-grown print-ready PDF (embedded subset
        /// fonts, trim/bleed boxes, crop marks) — no printer driver involved.</summary>
        // ============================================================= EPUB (22/09)

        private bool TextOrBookActive()
        {
            return _current != null && (_current.Kind == ItemKind.Text || _current.Kind == ItemKind.Book);
        }

        private void ExportEpubCurrent()
        {
            if (TextOrBookActive()) ExportEpub(_current);
        }

        /// <summary>« Créer un EPUB » : le plan (écrits, pages dynamiques
        /// écartées, couverture), le dialogue (titre pré-rempli, métadonnées,
        /// couverture), le fichier « Titre.epub », et le dossier ouvert.</summary>
        private void ExportEpub(BinderItem root)
        {
            if (root == null || _project == null) return;
            CommitActive();
            var plan = Exchange.Epub.Plan(_project, root);
            if (plan.Chapters.Count == 0)
            {
                MessageDialog.Show(this, "Rien à mettre dans l'EPUB : le livre n'a aucun écrit.", "EPUB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool openFolder;
            var options = View.EpubExportDialog.Ask(this, plan, Exchange.Epub.DefaultOptions(_project, root), out openFolder);
            if (options == null) return;
            var title = options.Title.Length > 0 ? options.Title : root.Title;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "EPUB (*.epub)|*.epub",
                FileName = SafeFileName(title) + ".epub",
                Title = "Créer l'EPUB"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                Exchange.Epub.Write(dialog.FileName, _project, plan, options);
                UnlockAchievement(Achievements.GenZ); // « Gen Zer »
                if (openFolder)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\""); }
                    catch { }
                }
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "EPUB impossible : " + error.Message, "EPUB", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportPdf()
        {
            string name;
            PageSetup setup;
            var document = BuildPrintable(out name, out setup);
            if (document == null) return;
            var decor = _current != null && _current.Kind == ItemKind.Text
                ? PageDecor.For(_current, _project) : null;
            var offset = _current != null && _current.Kind == ItemKind.Text
                ? ComputeFolioOffset(_current) : 0;
            var options = View.PdfExportDialog.Ask(this, name, false, 0,
                delegate(Print.PdfExportOptions o)
                { return PreviewPdf(document, setup, name, o, decor, offset); });
            if (options == null) return;
            WritePdf(document, setup, name, options, decor, offset);
        }

        /// <summary>Le BAT dans l'aperçu (4b-3) : produit le PDF EXACT dans un
        /// fichier temporaire et l'ouvre dans la visionneuse du système —
        /// boîtes, fond perdu, traits et imposition compris.</summary>
        private bool PreviewPdf(TextDocument document, PageSetup setup, string name,
            Print.PdfExportOptions options, PageDecor decor, int folioOffset, StyleSheet styles = null)
        {
            try
            {
                var composition = Print.Composer.Compose(
                    document, styles ?? _project.Styles.EffectiveFor(_current), setup, _project);
                composition.DefaultDecor = decor;
                composition.FolioOffset = folioOffset;
                var path = Path.Combine(Path.GetTempPath(),
                    "marabook-bat-" + SafeFileName(name) + ".pdf");
                Print.PdfWriter.Write(path, composition, options);
                System.Diagnostics.Process.Start(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================= folio de livre

        // Page counts per document (composition), so a book document opens at
        // its real folio without recomposing the whole book every time.
        private readonly Dictionary<string, int> _pageCountCache = new Dictionary<string, int>();

        /// <summary>Pages of the book preceding this document. Every document
        /// opens on a RECTO (odd folio) — feuillet reasoning, InDesign-style —
        /// so an odd running count gets a blank verso.</summary>
        private int ComputeFolioOffset(BinderItem item)
        {
            var book = item.EnclosingBook();
            if (book == null || book.Book == null || book == item) return 0;
            var texts = new List<BinderItem>();
            CollectBookTexts(book, texts);
            var offset = 0;
            foreach (var text in texts)
            {
                if (text == item) return offset;
                offset += PageCountOf(text);
                if (offset % 2 == 1) offset++; // le prochain document ouvre un recto
            }
            return 0; // item not found (moved?): behave like a standalone doc
        }

        private static void CollectBookTexts(BinderItem root, List<BinderItem> texts)
        {
            foreach (var child in root.Children)
            {
                if (child.Kind == ItemKind.Text) texts.Add(child);
                CollectBookTexts(child, texts);
            }
        }

        private int PageCountOf(BinderItem text)
        {
            int pages;
            if (_pageCountCache.TryGetValue(text.Id, out pages)) return pages;
            try
            {
                var composition = Print.Composer.Compose(Links.Strip(text.Document),
                    _project.Styles.EffectiveFor(text), text.Page ?? _project.Page, _project);
                pages = Math.Max(1, composition.Pages.Count);
            }
            catch { pages = 1; }
            _pageCountCache[text.Id] = pages;
            return pages;
        }

        // ============================================================= préchauffage (b30)
        // À l'ouverture d'un projet, les textes se préparent EN AVANCE, par
        // tranches à priorité oisive (jamais un gel de la saisie) : nombre de
        // pages de chaque écrit (folios, table des matières), passe locale de
        // correction paragraphe par paragraphe (cache par contenu du pilote),
        // suggestions d'orthographe en file d'arrière-plan. Le premier clic
        // sur un chapitre trouve tout prêt — mesuré : 6,6 s → ~50 ms sur un
        // chapitre de 18 000 caractères fautif.

        private List<BinderItem> _warmupItems;
        private int _warmupIndex;
        private int _warmupParagraph; // -1 = étape « nombre de pages » de l'écrit
        private int _warmupGeneration; // ouvrir un autre projet retire la pompe
        private DispatcherTimer _warmupTailTimer; // fin de la file de suggestions

        private void StartTextWarmup()
        {
            _warmupGeneration++;
            if (_warmupTailTimer != null) _warmupTailTimer.Stop();
            _warmupItems = new List<BinderItem>();
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Text) _warmupItems.Add(item);
            _warmupIndex = 0;
            _warmupParagraph = -1;
            if (_warmupItems.Count == 0)
            {
                _statusWarmup.Visibility = Visibility.Collapsed;
                return;
            }
            UpdateWarmupStatus();
            ScheduleWarmupStep();
        }

        private void ScheduleWarmupStep()
        {
            var generation = _warmupGeneration;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { WarmupStep(generation); }));
        }

        /// <summary>UNE tranche de préchauffage (quelques ms), puis la main
        /// revient au Dispatcher — la frappe et les clics passent toujours
        /// avant. Les bornes sont relues à CHAQUE tranche : l'utilisateur
        /// édite peut-être déjà pendant que la pompe tourne.</summary>
        private void WarmupStep(int generation)
        {
            if (generation != _warmupGeneration) return; // autre projet
            if (_warmupIndex >= _warmupItems.Count) { FinishWarmup(); return; }
            var item = _warmupItems[_warmupIndex];
            if (_warmupParagraph < 0)
            {
                PageCountOf(item); // folios, table des matières, tri « Pages »
                _warmupParagraph = 0;
            }
            else if (_warmupParagraph < item.Document.Paragraphs.Count)
            {
                // Vérification coupée (bouton « Vérifier ») : le comptage de
                // pages reste utile, la passe de correction n'a pas de sens.
                if (AppSettings.ProofEnabled)
                    _editor.WarmParagraph(item.Document, _warmupParagraph);
                _warmupParagraph++;
            }
            else
            {
                _warmupIndex++;
                _warmupParagraph = -1;
                UpdateWarmupStatus();
            }
            if (_warmupIndex >= _warmupItems.Count) FinishWarmup();
            else ScheduleWarmupStep();
        }

        private void UpdateWarmupStatus()
        {
            _statusWarmup.Text = "Préparation des textes… "
                + Math.Min(_warmupIndex + 1, _warmupItems.Count)
                + "/" + _warmupItems.Count;
            _statusWarmup.Visibility = Visibility.Visible;
        }

        /// <summary>La pompe a fini ses tranches ; la file de suggestions
        /// d'arrière-plan peut encore tourner — l'indicateur le dit, puis
        /// s'éteint de lui-même.</summary>
        private void FinishWarmup()
        {
            if (_editor.PendingSuggestions == 0)
            {
                _statusWarmup.Visibility = Visibility.Collapsed;
                return;
            }
            _statusWarmup.Text = "Préparation des suggestions…";
            if (_warmupTailTimer == null)
            {
                _warmupTailTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _warmupTailTimer.Tick += delegate
                {
                    if (_editor.PendingSuggestions > 0) return;
                    _warmupTailTimer.Stop();
                    _statusWarmup.Visibility = Visibility.Collapsed;
                };
            }
            _warmupTailTimer.Start();
        }

        /// <summary>Total pages of a book (recto starts included) — feeds the
        /// « page finale impaire » danger icon in the Pile.</summary>
        private int BookPageTotal(BinderItem book)
        {
            if (book == null || book.Book == null || _project == null) return 0;
            var texts = new List<BinderItem>();
            CollectBookTexts(book, texts);
            var total = 0;
            foreach (var text in texts)
            {
                if (total % 2 == 1) total++; // chaque document ouvre un recto
                total += PageCountOf(text);
            }
            return total;
        }

        // ============================================================= pages extra

        /// <summary>Corkboard « Nouveau document ▾ » : adds a document at the
        /// end of the book — plain, or one of the extra pages (liminaires,
        /// table des matières, éditeur, soutien), pre-filled from the model
        /// and marked IsExtraPage (no folio).</summary>
        /// <summary>L'image de la tuile d'un écrit ou d'un livre, depuis le
        /// menu ⋮ d'une carte (12/09) : la sélection ne bouge pas, le tableau
        /// se redessine.</summary>
        private void CardImageRequested(BinderItem item, bool remove)
        {
            if (remove) _binder.RemoveCardImage(item);
            else _binder.ChangeCardImage(item);
            RefreshOpenCorkboards();
        }

        private void NewBookDocument(BinderItem book, string kind)
        {
            if (kind == "plan") { _binder.NewPlan(book); return; } // racine Plans (b35)
            if (kind == "mindmap") { _binder.NewMindMap(book); return; } // racine Cartes mentales (22/09)
            if (kind == "mindmap-import") { _binder.ImportMindMapDialog(book); RefreshOpenCorkboards(); return; }
            // Les boutons de tête des racines Écrits et Recherche (12/09).
            if (kind == "root-text") { _binder.NewText(book); return; }
            if (kind == "root-folder") { _binder.NewFolder(book); return; }
            if (kind == "root-book") { _binder.NewBook(book); return; }
            if (kind == "import") { _binder.ImportMediaDialog(book); RefreshOpenCorkboards(); return; }
            if (book == null || book.Book == null) return;
            BinderItem item;
            if (kind == null)
            {
                var title = InputDialog.Ask(this, "Nouveau document",
                    "Titre du document :", "Nouveau document");
                if (title == null) return;
                item = new BinderItem { Kind = ItemKind.Text, Title = title };
                item.Document = TextDocument.FromPlainText("");
                item.Page = book.Book.Template.Clone(); // suit le gabarit intérieur
            }
            else if (kind == "folder")
            {
                // Une PARTIE : dossier purement indicatif — la compilation et
                // les folios le traversent (CollectBookTexts/Compiler récursifs).
                var title = InputDialog.Ask(this, "Nouvelle partie",
                    "Nom de la partie :", "Partie");
                if (title == null) return;
                item = new BinderItem { Kind = ItemKind.Folder, Title = title };
            }
            else
            {
                item = ExtraPages.Create(kind, book, _project);
                item.Page = book.Book.Template.Clone();
            }
            // Une page extra se place d'elle-même selon sa section (b49) :
            // liminaire en tête, page de fin après le corps, annexe en queue —
            // l'auteur la déplace ensuite à sa guise.
            var index = item.IsExtraPage ? ExtraPages.InsertIndex(book, ExtraPages.SectionOf(item)) : -1;
            _history.Run(new AddItemAction(book, item, index));
            if (ExtraPages.IsDynamic(item)) RegenerateDynamic(item);
            MarkDirty();
            _pageCountCache.Clear();
            _binder.Rebuild();
            if (_current == book) _bookView.Load(book, _history, _project);
        }

        /// <summary>Rebâtit une page dynamique d'après le livre (b49) : la
        /// table des matières, l'index, les notes de fin ou le glossaire.</summary>
        private void RegenerateDynamic(BinderItem page)
        {
            var book = page.EnclosingBook();
            if (book == null || book.Book == null) return;
            ExtraPages.EnsureStyle(_project);
            var lines = ExtraPages.LinesFor(book.Book.Template, _project);
            var story = new List<BinderItem>();
            BookProgress.StoryTexts(book, story); // le récit seul : ni liminaire, ni page de fin, ni annexe
            if (page.IsToc || page.ExtraKind == ExtraPages.KindToc)
            {
                var entries = new List<ExtraPages.TocEntry>();
                foreach (var text in story)
                    entries.Add(new ExtraPages.TocEntry { Title = text.Title, Folio = ComputeFolioOffset(text) + 1 });
                ExtraPages.FillToc(page.Document, entries, lines);
            }
            else if (page.ExtraKind == ExtraPages.KindIndex)
            {
                // Les fiches Personnage et Lieu, par leurs noms (Presence), avec
                // le folio d'ouverture de chaque écrit qui les nomme.
                var entries = new List<ExtraPages.IndexEntry>();
                foreach (var sheet in _project.AllItems())
                {
                    if (sheet.Kind != ItemKind.Sheet || sheet.IsDescendantOf(_project.Trash)) continue;
                    var category = _project.SheetCategoryOf(sheet);
                    if (category == null) continue;
                    var isCharacter = Achievements.IsCharacterCategory(category.Name);
                    var isPlace = category.Name.Trim().ToLowerInvariant().StartsWith("lieu");
                    if (!isCharacter && !isPlace) continue;
                    var names = Presence.NamesOf(sheet, _project.FindTemplate(sheet.TemplateId));
                    if (names.Count == 0) continue;
                    var entry = new ExtraPages.IndexEntry { Name = sheet.Title, Category = isCharacter ? "Personnages" : "Lieux" };
                    foreach (var text in story)
                        if (Presence.CountIn(text.Document.ToPlainText(), names) > 0)
                            entry.Folios.Add(ComputeFolioOffset(text) + 1);
                    if (entry.Folios.Count > 0) entries.Add(entry);
                }
                ExtraPages.FillIndex(page.Document, entries, lines);
            }
            else if (page.ExtraKind == ExtraPages.KindEndnotes)
            {
                var chapters = new List<ExtraPages.EndnoteChapter>();
                foreach (var text in story)
                {
                    var chapter = new ExtraPages.EndnoteChapter { Title = text.Title };
                    foreach (var note in text.Document.Footnotes) chapter.Notes.Add(note.Text);
                    chapters.Add(chapter);
                }
                ExtraPages.FillEndnotes(page.Document, chapters, lines);
            }
            else if (page.ExtraKind == ExtraPages.KindGlossary)
            {
                var entries = new List<ExtraPages.GlossaryEntry>();
                foreach (var word in _project.Lexicon)
                    if (!string.IsNullOrEmpty(word.Definition) && word.Definition.Trim().Length > 0)
                        entries.Add(new ExtraPages.GlossaryEntry { Word = word.Word, Definition = word.Definition.Trim() });
                ExtraPages.FillGlossary(page.Document, entries, lines);
            }
            else return;
            _pageCountCache.Remove(page.Id); // son nombre de pages a pu changer
        }

        // ============================================================= gabarits de pages

        private void NewPageTemplate(BinderItem book)
        {
            var title = View.InputDialog.Ask(this, "Nouveau gabarit",
                "Nom du gabarit (ex. « Corps de texte », « Ouverture de chapitre ») :",
                "Corps de texte");
            if (title == null) return;
            var gabarit = new BinderItem
            {
                Kind = ItemKind.PageTemplate,
                Title = title,
                TemplateColor = View.ItemIcons.TintSwatches[1] // indigo par défaut
            };
            gabarit.Parent = book;
            book.Children.Add(gabarit);
            MarkDirty();
            _binder.Rebuild();
            _binder.SelectItem(gabarit.Id); // ouvre la vue du gabarit
        }

        private void ExportPageTemplate(BinderItem gabarit)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = Persistence.GabaritFile.Filter,
                FileName = SafeFileName(gabarit.Title) + ".usgab"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                Persistence.GabaritFile.Export(gabarit, dialog.FileName);
                MessageDialog.Show(this, "Gabarit exporté :\n" + dialog.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Export impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportPageTemplate(BinderItem book)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Persistence.GabaritFile.Filter };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var gabarit = Persistence.GabaritFile.Import(dialog.FileName);
                gabarit.Parent = book;
                book.Children.Add(gabarit);
                MarkDirty();
                _binder.Rebuild();
                if (_current == book) _bookView.Load(book, _history, _project);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Import impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>« Copier vers un autre livre » : facilite les séries.</summary>
        private void CopyPageTemplate(BinderItem gabarit)
        {
            var books = new List<BinderItem>();
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Book && item != gabarit.EnclosingBook())
                    books.Add(item);
            if (books.Count == 0)
            {
                MessageDialog.Show(this, "Aucun autre livre dans ce projet.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var titles = new List<string>();
            foreach (var book in books) titles.Add(book.Title);
            var choice = View.LinkDialog.Ask(this, titles);
            if (choice == null) return;
            foreach (var book in books)
                if (book.Title == choice)
                {
                    var copy = Persistence.GabaritFile.Duplicate(gabarit);
                    copy.Parent = book;
                    book.Children.Add(copy);
                    MarkDirty();
                    _binder.Rebuild();
                    return;
                }
        }

        /// <summary>« Appliquer un gabarit » depuis le menu ⋮ d'une carte —
        /// à un document ou à toute la sélection (groupée).</summary>
        private void ApplyPageTemplateTo(List<BinderItem> targets)
        {
            if (targets == null || targets.Count == 0) return;
            var book = targets[0].EnclosingBook();
            if (book == null) return;
            var gabarits = new List<BinderItem>();
            foreach (var child in book.Children)
                if (child.Kind == ItemKind.PageTemplate) gabarits.Add(child);
            if (gabarits.Count == 0)
            {
                MessageDialog.Show(this,
                    "Ce livre n'a pas encore de gabarit de pages (vue du livre → Nouveau gabarit).",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var titles = new List<string> { "(aucun gabarit)" };
            foreach (var gabarit in gabarits) titles.Add(gabarit.Title);
            var choice = View.LinkDialog.Ask(this, titles);
            if (choice == null) return;
            string id = null;
            foreach (var gabarit in gabarits)
                if (gabarit.Title == choice) id = gabarit.Id;
            foreach (var target in targets)
                if (target.Kind == ItemKind.Text) target.PageTemplateId = id;
            MarkDirty();
            _binder.Rebuild();
            // La vue courante (livre) redessine ses cartes avec les pastilles.
            if (_current != null && _current.Kind == ItemKind.Book)
                _bookView.Load(_current, _history, _project);
        }

        /// <summary>« Publier » : the whole book compiled into one PDF —
        /// title page from its metadata, continuous pagination, its gabarit,
        /// print-shop defaults (CMJN FOGRA39, traits de coupe, fond perdu du
        /// livre).</summary>
        private void PublishBook(BinderItem book)
        {
            if (book == null || book.Book == null) return;
            CommitActive();
            // Pages dynamiques (table des matières, index, notes, glossaire) à jour avant compilation.
            foreach (var item in _project.AllItems())
                if (ExtraPages.IsDynamic(item) && item.EnclosingBook() == book) RegenerateDynamic(item);
            // Pas de page de titre générée : sa personnalisation arrive — le
            // PDF publié n'est que le contenu, chaque document sur un recto.
            var document = Exchange.Compiler.Build(_project, book, new Exchange.CompileOptions
            {
                TitlePage = false,
                ChapterHeadings = false,
                PageBreakPerText = true,
                RectoChapterStarts = true // chaque document ouvre un recto
            });
            var options = View.PdfExportDialog.Ask(this, book.Title, true, book.Book.BleedMm,
                delegate(Print.PdfExportOptions o)
                { return PreviewPdf(document, book.Book.Template, book.Title, o, null, 0, _project.Styles.EffectiveFor(book)); });
            if (options == null) return;
            // « Complétionniste » (12/09) : le PDF d'un livre fini à 100 %.
            if (WritePdf(document, book.Book.Template, book.Title, options, null, 0, _project.Styles.EffectiveFor(book)))
            {
                if (Achievements.IsBookComplete(_project, book)) UnlockAchievement(Achievements.Completionist);
                if (Achievements.IsMinimalist(book)) UnlockAchievement(Achievements.Minimalist);
            }
        }

        /// <summary>True quand le fichier a été écrit (dialogue confirmé, pas d'erreur).</summary>
        private bool WritePdf(TextDocument document, PageSetup setup, string name,
            Print.PdfExportOptions options, PageDecor decor = null, int folioOffset = 0, StyleSheet styles = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = SafeFileName(name) + ".pdf",
                Title = "PDF prêt à imprimer"
            };
            if (dialog.ShowDialog(this) != true) return false;
            try
            {
                var composition = Print.Composer.Compose(
                    document, styles ?? _project.Styles.EffectiveFor(_current), setup, _project);
                composition.DefaultDecor = decor;
                composition.FolioOffset = folioOffset;
                Print.PdfWriter.Write(dialog.FileName, composition, options);
                MessageDialog.Show(this, "Export terminé :\n" + dialog.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Export PDF impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ============================================================= session goal

        // ============================================================= sprints (b48)

        /// <summary>Édition › Lancer un sprint… : une durée (15, 25, 45 min ou
        /// libre) et un objectif de mots ; la pastille suit, le journal
        /// consigne à la fin. Un sprint en cours est d'abord arrêté.</summary>
        private void StartSprint()
        {
            if (_project == null) return;
            var choice = SprintDialog.Ask(this);
            if (choice == null) return;
            if (_sprintActive) FinishSprint();
            _sessionGoal = choice.Goal;
            _sessionBaseWords = ProjectWords();
            _sprintStart = DateTime.Now;
            _sprintMinutes = choice.Minutes;
            _sprintActive = true;
            _sprintFinished = false;
            _sprintPill.BorderBrush = Chrome.Accent;
            _sprintPill.Visibility = Visibility.Visible;
            _sprintTimer.Start();
            TickSprint();
            UpdateStats();
            RefreshHomeIfShown();
        }

        private int SprintWords()
        {
            return Math.Max(0, ProjectWords() - _sessionBaseWords);
        }

        private void TickSprint()
        {
            if (!_sprintActive) return;
            var elapsed = DateTime.Now - _sprintStart;
            var words = SprintWords();
            var culture = CultureInfo.CurrentCulture;
            if (_sprintMinutes > 0)
            {
                var left = TimeSpan.FromMinutes(_sprintMinutes) - elapsed;
                if (left <= TimeSpan.Zero) { FinishSprint(); return; }
                _sprintText.Text = "⏱ " + left.ToString(@"mm\:ss") + "   ·   " + words.ToString("N0", culture)
                    + (_sessionGoal > 0 ? " / " + _sessionGoal.ToString("N0", culture) : "") + " mots"
                    + (_sessionGoal > 0 && words >= _sessionGoal ? " — objectif atteint !" : "");
            }
            else
                _sprintText.Text = "⏱ " + elapsed.ToString(@"hh\:mm\:ss") + "   ·   " + words.ToString("N0", culture)
                    + (_sessionGoal > 0 ? " / " + _sessionGoal.ToString("N0", culture) : "") + " mots"
                    + (_sessionGoal > 0 && words >= _sessionGoal ? " — objectif atteint !" : "");
        }

        /// <summary>Le sprint s'achève (temps écoulé, ou arrêt) : consigné dans
        /// le journal, la pastille passe au vert et reste jusqu'à la croix.</summary>
        private void FinishSprint()
        {
            if (!_sprintActive) return;
            _sprintTimer.Stop();
            _sprintActive = false;
            _sprintFinished = true;
            var elapsedMinutes = (int)Math.Round((DateTime.Now - _sprintStart).TotalMinutes);
            var words = SprintWords();
            var culture = CultureInfo.CurrentCulture;
            _project.Journal.Sprints.Add(new SprintRecord
            {
                Date = _sprintStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Minutes = _sprintMinutes,
                Elapsed = Math.Max(_sprintMinutes > 0 ? Math.Min(_sprintMinutes, elapsedMinutes) : elapsedMinutes, 0),
                Words = words,
                Goal = _sessionGoal
            });
            MarkDirty();
            _sprintPill.BorderBrush = Chrome.Ok;
            _sprintText.Text = "Sprint terminé — " + words.ToString("N0", culture) + (words > 1 ? " mots" : " mot")
                + " en " + Math.Max(1, elapsedMinutes) + " min"
                + (_sessionGoal > 0 && words >= _sessionGoal ? " — objectif atteint !" : "");
            if (_journalView.Visibility == Visibility.Visible) _journalView.Refresh();
            RefreshHomeIfShown();
        }

        /// <summary>La croix : un sprint en cours s'arrête (et se consigne),
        /// une pastille finie se referme ; l'objectif de la barre d'état s'efface.</summary>
        private void CloseSprint()
        {
            if (_sprintActive) FinishSprint();
            _sprintFinished = false;
            _sprintPill.Visibility = Visibility.Collapsed;
            _sessionGoal = 0;
            UpdateStats();
            RefreshHomeIfShown();
        }

        /// <summary>Changement de projet : le sprint en cours est oublié
        /// (jamais consigné dans un autre journal).</summary>
        private void ResetSprint()
        {
            if (_sprintTimer != null) _sprintTimer.Stop();
            _sprintActive = false;
            _sprintFinished = false;
            if (_sprintPill != null) _sprintPill.Visibility = Visibility.Collapsed;
        }

        private View.SprintStatus SprintStatusNow()
        {
            if (!_sprintActive && !_sprintFinished) return null;
            var elapsed = DateTime.Now - _sprintStart;
            var status = new View.SprintStatus
            {
                Active = _sprintActive,
                Finished = _sprintFinished,
                Written = SprintWords(),
                Goal = _sessionGoal,
                Minutes = _sprintMinutes,
                Elapsed = (int)Math.Round(elapsed.TotalMinutes)
            };
            status.SecondsLeft = _sprintMinutes > 0
                ? Math.Max(0, (int)(TimeSpan.FromMinutes(_sprintMinutes) - elapsed).TotalSeconds) : -1;
            return status;
        }

        private void RefreshHomeIfShown()
        {
            if (_homeView != null && _homeView.Visibility == Visibility.Visible) _homeView.Refresh();
        }

        // ============================================================= journal perso

        /// <summary>Credits a net word delta to today's journal entry, refreshes
        /// the journal view when visible, and fires the goal fanfare when the
        /// daily target is crossed.</summary>
        private void AddJournalWords(int delta)
        {
            _project.Journal.Add(WritingJournal.Today(), delta);
            if (_journalView.Visibility == Visibility.Visible) _journalView.Refresh();
            if (delta > 0)
            {
                CheckDailyGoal();
                // Compteurs globaux des succès (12/09) : à 300 %, en mode calme.
                if (AppSettings.Zoom >= 300) AppSettings.WordsAtMaxZoom += delta;
                if (_calmMode) AppSettings.WordsInCalm += delta;
                ScheduleAchievementCheck();
            }
        }

        /// <summary>One fanfare per day: the celebration date is persisted in
        /// the journal so reopening the project stays quiet.</summary>
        private void CheckDailyGoal()
        {
            var journal = _project.Journal;
            var today = WritingJournal.Today();
            if (journal.DailyGoal <= 0) return;
            if (journal.WordsOn(today) < journal.DailyGoal) return;
            if (journal.LastCelebrated == today) return;
            journal.LastCelebrated = today;
            MarkDirty();
            ShowGoalToast(journal.DailyGoal);
        }

        /// <summary>Le petit truc visuel : un toast accent glisse au bas de la
        /// zone centrale, s'attarde, puis s'efface — l'écriture n'est jamais
        /// interrompue.</summary>
        private void ShowGoalToast(int goal)
        {
            var text = new TextBlock
            {
                Text = "🎉  Objectif du jour atteint — "
                    + goal.ToString("N0", CultureInfo.CurrentCulture) + " mots !",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            var toast = new Border
            {
                Background = Chrome.Accent,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(20, 10, 20, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 34),
                IsHitTestVisible = false,
                Opacity = 0,
                Child = text,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.3,
                    BlurRadius = 12,
                    ShadowDepth = 2
                },
                RenderTransform = new TranslateTransform(0, 16)
            };
            _centerHost.Children.Add(toast);

            var appear = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(220));
            var rise = new System.Windows.Media.Animation.DoubleAnimation(16, 0,
                TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(500))
            {
                BeginTime = TimeSpan.FromSeconds(4)
            };
            fade.Completed += delegate { _centerHost.Children.Remove(toast); };
            toast.BeginAnimation(OpacityProperty, appear);
            toast.RenderTransform.BeginAnimation(TranslateTransform.YProperty, rise);
            toast.BeginAnimation(OpacityProperty, fade, System.Windows.Media.Animation
                .HandoffBehavior.Compose);
        }

        // ============================================================= succès (12/09/2026)

        /// <summary>Une vérification coalescée : les gestes qui peuvent
        /// débloquer un succès arrivent en rafale (frappe, historique), on
        /// mesure une fois, un peu après.</summary>
        private void ScheduleAchievementCheck()
        {
            if (_achievementTimer == null)
            {
                _achievementTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(900)
                };
                _achievementTimer.Tick += delegate { _achievementTimer.Stop(); CheckAchievements(); };
            }
            _achievementTimer.Stop();
            _achievementTimer.Start();
        }

        /// <summary>Mesure les faits (projet + réglages + fenêtre) et débloque
        /// ce qui vient d'être gagné, un toast par succès.</summary>
        private void CheckAchievements()
        {
            var now = DateTime.Now;
            var facts = Achievements.Gather(_project, CachedPageCount, CachedBookPages, AppSettings.BlankSince, now);
            facts.PermanentlyDeleted = AppSettings.PermanentlyDeleted;
            facts.UsageStreak = AppSettings.UsageStreak;
            facts.PlotBytes = _lastSavedBytes;
            facts.AccentChanged = AppSettings.AccentColor != null
                && !string.Equals(AppSettings.AccentColor, Chrome.DefaultAccent, StringComparison.OrdinalIgnoreCase);
            if (_project != null) facts.LexiconEntries += AppSettings.Lexicon.Count;
            facts.AllProofOptions = AppSettings.SpellEnabled && AppSettings.GrammarEnabled
                && AppSettings.TypographyEnabled && AppSettings.StyleEnabled;
            facts.WordsAtMaxZoom = AppSettings.WordsAtMaxZoom;
            facts.WordsInCalm = AppSettings.WordsInCalm;
            facts.OpenHours = (now - _appStart).TotalHours;
            facts.DirtyHours = _dirty && _dirtySince != null ? (now - _dirtySince.Value).TotalHours : 0;
            foreach (var action in AppSettings.Actions)
            {
                string gesture;
                if (AppSettings.Shortcuts.TryGetValue(action.Id, out gesture)
                    && !string.Equals(gesture ?? "", action.DefaultGesture ?? "", StringComparison.OrdinalIgnoreCase))
                    facts.ShortcutsChanged++;
            }
            facts.ModuleHolds = Modules.Holding(_project); // succès des modules (DLC)
            var earned = Achievements.Earned(facts, AppSettings.Achievements.Keys);
            foreach (var id in earned) UnlockAchievement(id);
        }

        /// <summary>Le compte de pages déjà mesuré d'un écrit (cache du batch
        /// 30) — jamais une composition rien que pour un succès.</summary>
        private int CachedPageCount(BinderItem text)
        {
            int pages;
            return _pageCountCache.TryGetValue(text.Id, out pages) ? pages : 0;
        }

        /// <summary>Même règle pour un livre : la somme des comptes déjà
        /// mesurés (BookPageTotal composerait tout le livre).</summary>
        private int CachedBookPages(BinderItem book)
        {
            var texts = new List<BinderItem>();
            CollectBookTexts(book, texts);
            var total = 0;
            foreach (var text in texts)
            {
                if (total % 2 == 1) total++;
                total += CachedPageCount(text);
            }
            return total;
        }

        private void UnlockAchievement(string id)
        {
            var achievement = Achievements.Find(id);
            if (achievement == null || AppSettings.Achievements.ContainsKey(id)) return;
            AppSettings.Achievements[id] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            AppSettings.Save();
            ShowAchievementToast(achievement);
            if (_journalView.Visibility == Visibility.Visible) _journalView.RefreshAchievements();
            // Un palier peut tomber dans la foulée.
            if (!Achievements.IsTier(id)) ScheduleAchievementCheck();
        }

        /// <summary>Développement : reverrouille un succès (le Roi avec lui).</summary>
        /// <summary>« Nettoyage en profondeur » (12/09) : un écrit vu à plus de
        /// cent fautes d'orthographe, puis revu à zéro.</summary>
        private void TrackDeepClean()
        {
            if (_current == null || _current.Kind != ItemKind.Text || !_editor.ShowsItem(_current)) return;
            var count = _editor.SpellingFindingCount;
            if (count > 100) _deepCleanCandidates.Add(_current.Id);
            else if (count == 0 && _deepCleanCandidates.Remove(_current.Id)) UnlockAchievement(Achievements.DeepClean);
        }

        /// <summary>Aide › Réinitialiser les succès (22/09) : tous les succès
        /// reverrouillés et les compteurs qui ne servent qu'à eux remis à
        /// zéro — l'état d'origine, sur confirmation.</summary>
        private void ResetAchievements()
        {
            var answer = MessageDialog.Show(this,
                "Réinitialiser les succès ?\n\nTous les succès obtenus seront reverrouillés, sur tous vos projets, "
                + "et leurs compteurs (jours d'usage, suppressions, mots en mode calme…) repartent de zéro. "
                + "Ils se regagnent ensuite normalement.",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            AppSettings.ResetAchievements();
            AppSettings.Save();
            if (_journalView.Visibility == Visibility.Visible) _journalView.RefreshAchievements();
            MessageDialog.Show(this, "Les succès sont réinitialisés.", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RevokeAchievement(string id)
        {
            AppSettings.Achievements.Remove(id);
            if (!Achievements.IsTier(id))
                foreach (var tier in new[] { "petit-nerd", "poisson-panerd", "nerdinator", Achievements.Emperor })
                    AppSettings.Achievements.Remove(tier);
            AppSettings.Save();
            if (_journalView.Visibility == Visibility.Visible) _journalView.RefreshAchievements();
        }

        /// <summary>Le toast de succès, façon Steam : l'image, le nom, la
        /// description ; il glisse depuis la droite, s'attarde, s'efface. Les
        /// toasts s'empilent si plusieurs tombent ensemble.</summary>
        private void ShowAchievementToast(Achievement achievement)
        {
            var badge = AchievementBadge.Build(achievement, true, 48);
            ShowToast(badge, "Succès débloqué", achievement.Name, achievement.Description, 6);
        }

        /// <summary>Le toast « enregistré » (0.50.0) : la carte des succès, sans
        /// icône, après un Fichier › Enregistrer — jamais pour l'automatique.</summary>
        private void ShowSavedToast()
        {
            if (_project == null || _path == null) return;
            var detail = Path.GetFileName(_path) + " · " + DateTime.Now.ToString("HH:mm");
            ShowToast(null, "Projet enregistré", _project.Name, detail, 3);
        }

        /// <summary>La carte qui glisse depuis la droite et s'efface seule :
        /// une icône (ou rien), une amorce, un titre, un détail. Inerte : elle
        /// ne bloque jamais un clic dessous.</summary>
        private void ShowToast(FrameworkElement badge, string kicker, string title, string description, double seconds)
        {
            var row = new DockPanel();
            if (badge != null)
            {
                badge.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300 };
            text.Children.Add(new TextBlock
            {
                Text = kicker,
                Foreground = Chrome.SoftText,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Chrome.Ink,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(description))
                text.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            row.Children.Add(text);
            var toast = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 16, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0,
                IsHitTestVisible = false, // un succès ne bloque jamais un clic dessous
                Child = row,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.3,
                    BlurRadius = 14,
                    ShadowDepth = 2
                },
                RenderTransform = new TranslateTransform(60, 0)
            };
            _toastHost.Children.Add(toast);

            var appear = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
            var slide = new System.Windows.Media.Animation.DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600))
            {
                BeginTime = TimeSpan.FromSeconds(seconds)
            };
            fade.Completed += delegate { _toastHost.Children.Remove(toast); };
            toast.BeginAnimation(OpacityProperty, appear);
            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
            toast.BeginAnimation(OpacityProperty, fade, System.Windows.Media.Animation.HandoffBehavior.Compose);
        }

        private int ProjectWords()
        {
            var total = 0;
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Text) continue;
                int words;
                if (!_wordCache.TryGetValue(item.Id, out words))
                {
                    words = TextStats.Compute(item.Document.ToPlainText()).Words;
                    _wordCache[item.Id] = words;
                }
                total += words;
            }
            return total;
        }

        // ============================================================= view toggles

        private void ToggleBinder()
        {
            AppSettings.BinderVisible = !AppSettings.BinderVisible;
            if (!AppSettings.BinderVisible && _binderCol.Width.Value > 0)
                AppSettings.BinderWidth = _binderCol.Width.Value;
            ApplyPanelVisibility();
            AppSettings.Save();
        }

        private void ToggleInspector()
        {
            SetRightPanel(RightPanels.Toggle(AppSettings.RightPanel, RightPanel.Inspector));
        }

        // ============================================================= mode calme

        private void ToggleCalmMode()
        {
            SetCalmMode(!_calmMode);
        }

        /// <summary>Mode calme : plus que les pages. Menus, barre d'état, Pile,
        /// inspecteur et rubans s'effacent ; la pastille flottante (ou Échap)
        /// ramène tout. Transitoire — rien n'est persisté.</summary>
        private void SetCalmMode(bool calm)
        {
            if (_calmMode == calm) return;
            if (calm)
            {
                // Mémorise les largeurs réelles avant de replier les panneaux.
                if (_binderCol.Width.Value > 0) AppSettings.BinderWidth = _binderCol.Width.Value;
                if (_inspectorCol.Width.Value > 0) AppSettings.InspectorWidth = _inspectorCol.Width.Value;
            }
            _calmMode = calm;
            _menuBar.Visibility = calm ? Visibility.Collapsed : Visibility.Visible;
            _statusBar.Visibility = _menuBar.Visibility;
            _calmExit.Visibility = calm ? Visibility.Visible : Visibility.Collapsed;
            ApplyPanelVisibility();
            _editor.SetCalm(calm);
            _sheetView.SetCalm(calm);
        }

        private void ApplyPanelVisibility()
        {
            var binderOn = AppSettings.BinderVisible && !_calmMode;
            _binder.Visibility = binderOn ? Visibility.Visible : Visibility.Collapsed;
            _binderSplit.Visibility = _binder.Visibility;
            _binderCol.Width = binderOn ? new GridLength(AppSettings.BinderWidth) : new GridLength(0);
            _binderMenu.IsChecked = binderOn;

            // La colonne de droite (batch 39) : le panneau actif est-il
            // disponible dans le contexte courant ? Sinon, rien — les règles
            // sont dans RightPanels.Available, pas ici.
            var shown = ShownRightPanel();
            _inspector.Visibility = shown == RightPanel.Inspector ? Visibility.Visible : Visibility.Collapsed;
            if (_correctionHost != null)
                _correctionHost.Visibility = shown == RightPanel.Correction ? Visibility.Visible : Visibility.Collapsed;
            if (_searchHost != null)
                _searchHost.Visibility = shown == RightPanel.Search ? Visibility.Visible : Visibility.Collapsed;
            if (_versionsHost != null)
                _versionsHost.Visibility = shown == RightPanel.Versions ? Visibility.Visible : Visibility.Collapsed;
            if (_pinnedHost != null)
                _pinnedHost.Visibility = shown == RightPanel.Pinned ? Visibility.Visible : Visibility.Collapsed;
            if (_lexiconHost != null)
                _lexiconHost.Visibility = shown == RightPanel.Lexicon ? Visibility.Visible : Visibility.Collapsed;
            var anyRight = shown != RightPanel.None;
            _inspectorSplit.Visibility = anyRight ? Visibility.Visible : Visibility.Collapsed;
            _inspectorCol.Width = anyRight
                ? new GridLength(shown == RightPanel.Pinned ? AppSettings.PinnedWidth : AppSettings.InspectorWidth)
                : new GridLength(0);
            _inspectorMenu.IsChecked = shown == RightPanel.Inspector;
            _searchMenu.IsChecked = shown == RightPanel.Search;
            _versionsMenu.IsChecked = shown == RightPanel.Versions;
            if (_editor != null) _editor.CorrectionPanelChecked = shown == RightPanel.Correction;
            UpdateRail();
        }

        // ============================================================= colonne de droite (b39)

        /// <summary>Le panneau à montrer : l'actif s'il est disponible, sinon rien.</summary>
        private RightPanel ShownRightPanel()
        {
            return IsPanelAvailable(AppSettings.RightPanel) ? AppSettings.RightPanel : RightPanel.None;
        }

        private bool IsPanelAvailable(RightPanel panel)
        {
            return RightPanels.Available(panel, _journalOpen || _calmMode, _project != null, CurrentKind(), CurrentIsHomeRoot(), _sidePin != null, HasLexiconPanel);
        }

        /// <summary>La nature de l'élément courant, null sans sélection — ce
        /// qui décide des onglets offerts par le rail.</summary>
        private ItemKind? CurrentKind()
        {
            return _current == null ? (ItemKind?)null : _current.Kind;
        }

        /// <summary>L'élément courant est-il la racine Accueil ? Elle seule,
        /// parmi les racines, garde un panneau Général (batch 43).</summary>
        private bool CurrentIsHomeRoot()
        {
            return _current != null && _current.IsHomeRoot;
        }

        /// <summary>LE seul endroit qui change le panneau actif — rail,
        /// raccourcis, menus, bouton du ruban, croix des panneaux y passent.
        /// Un panneau indisponible dans le contexte ne devient jamais actif.</summary>
        private void SetRightPanel(RightPanel panel)
        {
            if (panel != RightPanel.None && !IsPanelAvailable(panel)) return;
            RememberRightWidth();
            AppSettings.RightPanel = panel;
            AppSettings.Save();
            ApplyPanelVisibility();
        }

        /// <summary>La largeur de la colonne de droite est retenue pour le
        /// panneau qui l'occupe : l'épinglé a la sienne (1,25 × la Pile par
        /// défaut, 14/09), les autres partagent InspectorWidth.</summary>
        private void RememberRightWidth()
        {
            var width = _inspectorCol.Width.Value;
            if (width <= 0) return;
            if (ShownRightPanel() == RightPanel.Pinned) AppSettings.PinnedWidth = width;
            else AppSettings.InspectorWidth = width;
        }

        // ============================================================= le rail (b39)

        /// <summary>Le rail : fond de barre, filet à gauche, quatre onglets de
        /// 32 px — l'inspecteur (il décrit l'élément courant), un filet, puis
        /// les trois outils. Il ne se masque pas ; il suit seulement le mode
        /// calme et le journal, comme le reste de la colonne.</summary>
        private Border BuildRail()
        {
            _railStack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            return new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = _railStack
            };
        }

        /// <summary>Les onglets suivent la nature de l'élément courant
        /// (RightPanels.Offered) : un écrit a Correction et Versions, un livre
        /// Métadonnées et Publication, le reste Général et Recherche. Ceux
        /// qui décrivent l'élément d'abord, un filet, puis les outils.</summary>
        private void RebuildRailTabs(RightPanel[] offered)
        {
            _railOffered = offered;
            _railStack.Children.Clear();
            _railTabs.Clear();
            _railBadge = null;
            _railBadgeText = null;
            var separated = false;
            foreach (var panel in offered)
            {
                if (!separated && !RightPanels.DescribesCurrent(panel))
                {
                    separated = true;
                    if (_railStack.Children.Count > 0) // pas de filet orphelin en tête (racines sans Général, b43)
                        _railStack.Children.Add(new Border
                        {
                            Height = 1,
                            Background = Chrome.Border,
                            Margin = new Thickness(9, 2, 9, 6)
                        });
                }
                _railStack.Children.Add(RailTab(panel));
            }
        }

        /// <summary>Un hôte de la colonne de droite pour un panneau-outil :
        /// même habillage que le panneau de correction (fond de barre, filet
        /// à gauche, titre, liste défilante).</summary>
        private static Border ToolHost(string title, UIElement content)
        {
            var panel = new DockPanel();
            var head = new TextBlock
            {
                Text = title,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(head, Dock.Top);
            panel.Children.Add(head);
            panel.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            });
            return new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Child = panel
            };
        }

        /// <summary>Un onglet du rail : l'icône, l'infobulle « nom — raccourci »
        /// posée à gauche, flèche vers l'onglet (c'est ainsi qu'on apprend un
        /// raccourci sans manuel), et pour Correction la pastille du nombre
        /// de signalements.</summary>
        private Border RailTab(RightPanel panel)
        {
            string icon, name, gesture = null;
            switch (panel)
            {
                case RightPanel.Inspector:
                    icon = "article-bold"; name = "Général"; gesture = AppSettings.Gesture("toggle-inspector"); break;
                case RightPanel.Correction:
                    icon = "file-magnifying-glass"; name = "Détails de correction"; break;
                case RightPanel.Search:
                    icon = "magnifying-glass-bold"; name = "Recherche dans le projet"; gesture = AppSettings.Gesture("project-search"); break;
                case RightPanel.Versions:
                    icon = "git-branch"; name = "Versions de l'écrit"; gesture = AppSettings.Gesture("versions-panel"); break;
                case RightPanel.Pinned:
                    icon = "push-pin-bold"; name = "Épinglé au rail"; break;
                case RightPanel.Lexicon:
                    icon = "pile-dictionnaire"; name = "Lexique — la définition d'un mot du dictionnaire personnel"; break;
                default:
                    icon = "book-open-text-bold"; name = "Publication du livre"; break;
            }
            var glyph = (FrameworkElement)Icons.Make(icon, 16, Chrome.Ink);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            var content = new Grid();
            content.Children.Add(glyph);
            if (panel == RightPanel.Correction)
            {
                // La pastille vit DANS l'onglet (coin bas droit, jamais
                // rognée), couleurs inversées selon que l'onglet est actif.
                _railBadgeText = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _railBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    MinWidth = 16,
                    Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = _railBadgeText,
                    Visibility = Visibility.Collapsed
                };
                content.Children.Add(_railBadge);
            }
            var tip = new ToolTip
            {
                Content = string.IsNullOrEmpty(gesture)
                    ? name : name + " — " + AppSettings.DisplayGesture(gesture),
                Placement = System.Windows.Controls.Primitives.PlacementMode.Left
            };
            var tab = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4, 0, 4, 4),
                Background = Brushes.Transparent,
                ToolTip = tip,
                Child = content
            };
            ToolTipService.SetPlacement(tab, System.Windows.Controls.Primitives.PlacementMode.Left);
            tab.MouseLeftButtonUp += delegate { ClickRailTab(panel); };
            tab.MouseEnter += delegate
            {
                if (ShownRightPanel() != panel && IsPanelAvailable(panel)) tab.Background = Chrome.Border;
            };
            tab.MouseLeave += delegate { UpdateRail(); };
            _railTabs[panel] = tab;
            return tab;
        }

        /// <summary>Un clic : l'onglet actif replie la colonne, tout autre
        /// devient le panneau actif ; un onglet grisé ne réagit pas.</summary>
        private void ClickRailTab(RightPanel panel)
        {
            if (!IsPanelAvailable(panel)) return;
            SetRightPanel(RightPanels.Toggle(AppSettings.RightPanel, panel));
            if (AppSettings.RightPanel != panel) return;
            if (panel == RightPanel.Search) _searchPanel.FocusQuery();
            if (panel == RightPanel.Versions) _versionsPanel.SetCurrent(_current);
        }

        /// <summary>L'onglet actif porte l'accent (comme les onglets du ruban,
        /// b34), un onglet indisponible est grisé sans disparaître — le rail
        /// ne change jamais de taille, on garde le repère.</summary>
        private void UpdateRail()
        {
            if (_rail == null) return;
            var railOn = !_journalOpen && !_calmMode;
            _rail.Visibility = railOn ? Visibility.Visible : Visibility.Collapsed;
            _railCol.Width = new GridLength(railOn ? RailWidth : 0);
            var offered = RightPanels.Offered(CurrentKind(), CurrentIsHomeRoot(), HasLexiconPanel);
            if (!ReferenceEquals(offered, _railOffered)) RebuildRailTabs(offered);
            UpdatePinTip();
            var shown = ShownRightPanel();
            foreach (var pair in _railTabs)
            {
                var available = IsPanelAvailable(pair.Key);
                var active = shown == pair.Key;
                var tab = pair.Value;
                tab.Background = active ? Chrome.Accent : Brushes.Transparent;
                tab.Opacity = available ? 1.0 : 0.35;
                tab.Cursor = available ? Cursors.Hand : Cursors.Arrow;
                var glyph = ((Grid)tab.Child).Children[0] as System.Windows.Shapes.Path;
                if (glyph != null) glyph.Fill = active ? Chrome.PaperBg : Chrome.Ink;
            }
            if (_railBadge == null) return;
            var count = _editor != null ? _editor.FindingCount : 0;
            _railBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _railBadgeText.Text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
            // Une pastille de compteur est en DANGER, pas en accent (b40) :
            // elle signale ce qui attend, elle ne se confond pas avec l'actif.
            var correctionActive = shown == RightPanel.Correction;
            _railBadge.Background = correctionActive ? Chrome.PaperBg : Chrome.Danger;
            _railBadgeText.Foreground = correctionActive ? Chrome.Danger : Chrome.PaperBg;
        }

        private void ToggleRulers()
        {
            AppSettings.ShowRulers = !AppSettings.ShowRulers;
            _rulersMenu.IsChecked = AppSettings.ShowRulers;
            AppSettings.Save();
            _editor.UpdateRulers();
            _sheetView.UpdateRulers();
        }

        private void ToggleDarkTheme()
        {
            AppSettings.DarkTheme = !AppSettings.DarkTheme;
            _darkMenu.IsChecked = AppSettings.DarkTheme;
            ApplyAppearance();
            AppSettings.Save();
        }

        /// <summary>Re-applies the mutable chrome brushes and the themed styles
        /// after any appearance change (dark theme, accent, white paper).</summary>
        private void ApplyAppearance()
        {
            Chrome.Toggle(AppSettings.DarkTheme);
            Theme.Switch(Application.Current, AppSettings.DarkTheme);
            if (_darkMenu != null) _darkMenu.IsChecked = AppSettings.DarkTheme; // le commutateur des Préférences (22/09)
            var themeHandler = ThemeChanged; // les modules à code suivent le thème (22/09)
            if (themeHandler != null) themeHandler();
        }

        private void OpenPreferences()
        {
            var dialog = new PreferencesDialog(this, _project);
            dialog.AppearanceChanged += ApplyAppearance;
            dialog.AppearanceChanged += ScheduleAchievementCheck; // « Pimp my write »
            dialog.ShortcutsChanged += ScheduleAchievementCheck;  // « Picky eater »
            dialog.ProofingChanged += delegate
            {
                _editor.RefreshProofing();
                if (_project != null) MarkDirty(); // la liste projet a pu changer
                ScheduleAchievementCheck(); // « Sur-stimulation »
            };
            dialog.ShortcutsChanged += RefreshShortcuts;
            // Styles globaux édités dans les Préférences (22/09) : le projet
            // ouvert les reprend aussitôt.
            dialog.GlobalStylesChanged += delegate
            {
                if (_project == null) return;
                if (GlobalStyles.PushFromSettings(_project))
                {
                    _editor.SetStyleSheet(_project.Styles);
                    _editor.Reload();
                    _sheetView.SetStyleSheet(_project.Styles);
                    _sheetView.ReloadBody();
                    if (_bookView.Visibility == Visibility.Visible) _bookView.RefreshStyles(); // styles ajoutés/retirés (revue 22/09)
                    MarkDirty();
                }
            };
            Dialogs.ShowModal(dialog);
        }

        // ============================================================= displays

        private void UpdateTitle()
        {
            Title = AppName + " — " + _project.Name + (_dirty ? " *" : "");
        }

        private void UpdateRecentMenu()
        {
            _recentMenu.Items.Clear();
            foreach (var recent in AppSettings.RecentFiles)
            {
                var path = recent;
                var entry = new MenuItem { Header = Path.GetFileNameWithoutExtension(path), ToolTip = path };
                entry.Click += delegate
                {
                    if (!File.Exists(path))
                    {
                        MessageDialog.Show(this, "Ce fichier n'existe plus :\n" + path,
                            AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!ConfirmDiscard()) return;
                    OpenFile(path);
                };
                _recentMenu.Items.Add(entry);
            }
            _recentMenu.IsEnabled = _recentMenu.Items.Count > 0;
        }

        private void UpdateInspector()
        {
            _loadingInspector = true;
            if (_current == null)
            {
                _inspTitle.Text = _project.Name;
                _inspKind.Text = "Projet";
                _synopsisBox.Text = "";
                _synopsisBox.IsEnabled = false;
                _notesBox.Text = "";
                _notesBox.IsEnabled = false;
                _statusSection.Visibility = Visibility.Collapsed;
                _planSection.Visibility = Visibility.Collapsed;
                SetInspectorFieldVisibility(true, false);
            }
            else
            {
                _inspTitle.Text = _current.Title;
                var template = _current.Kind == ItemKind.Sheet
                    ? _project.FindTemplate(_current.TemplateId) : null;
                _inspKind.Text = _current.IsCategory ? "Catégorie"
                               : _current.Kind == ItemKind.Folder ? "Dossier"
                               : _current.Kind == ItemKind.Sheet
                                    ? "Fiche" + (template != null ? " — " + template.Name : "")
                               : _current.Kind == ItemKind.Media
                                    ? "Document" + (_current.MediaExtension ?? "")
                               : _current.Kind == ItemKind.Book ? "Livre"
                               : _current.Kind == ItemKind.PageTemplate ? "Gabarit de pages"
                               : _current.Kind == ItemKind.Plan ? "Plan"
                               : _current.Kind == ItemKind.MindMap ? "Carte mentale"
                               : "Écrit";
                _synopsisBox.Text = _current.Synopsis ?? "";
                _synopsisBox.IsEnabled = !_current.IsCategory;
                _notesBox.Text = _current.Notes ?? "";
                _notesBox.IsEnabled = !_current.IsCategory;
                // Sheets have no synopsis (their cards show notes only); a page
                // gabarit has neither; notes exist on texts and sheets. Les
                // CATÉGORIES (Écrits…) non plus (batch 28) : l'encart vide ne
                // servait à rien.
                SetInspectorFieldVisibility(
                    _current.Kind != ItemKind.Sheet
                        && _current.Kind != ItemKind.PageTemplate
                        && _current.Kind != ItemKind.Plan // un plan : la couleur, rien d'autre (b35)
                        && _current.Kind != ItemKind.Book // le synopsis d'un livre vit dans Édition (b43)
                        && !_current.IsCategory,
                    _current.Kind == ItemKind.Text || _current.Kind == ItemKind.Sheet);

                // État (textes seulement) + couleur de carte (documents,
                // fiches, médias — et dossiers, dont les boîtes de livre).
                var showStatus = _current.Kind == ItemKind.Text;
                var showColor = _current.Kind == ItemKind.Text
                    || _current.Kind == ItemKind.Sheet
                    || _current.Kind == ItemKind.Media
                    || _current.Kind == ItemKind.Folder
                    || _current.Kind == ItemKind.Plan;
                _statusSection.Visibility = showStatus || showColor
                    ? Visibility.Visible : Visibility.Collapsed;
                _statusLabel.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
                _statusCombo.Visibility = _statusLabel.Visibility;
                _colorLabel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
                _colorButton.Visibility = _colorLabel.Visibility;
                if (showStatus)
                {
                    var index = Array.IndexOf(TextStatus.Keys, _current.Status);
                    _statusCombo.SelectedIndex = index < 0 ? 0 : index + 1;
                }
                if (showColor) RebuildColorSwatches();
                var isPlan = _current.Kind == ItemKind.Plan;
                _planSection.Visibility = isPlan ? Visibility.Visible : Visibility.Collapsed;
                if (isPlan && _current.Plan != null) _planColumnWord.Text = _current.Plan.ColumnWord;
            }
            _loadingInspector = false;

            // À l'Accueil, le Général s'efface derrière les raccourcis
            // « Commencer » (batch 43).
            var homeRoot = CurrentIsHomeRoot();
            _homeStartSection.Visibility = homeRoot ? Visibility.Visible : Visibility.Collapsed;
            _inspTitle.Visibility = homeRoot ? Visibility.Collapsed : Visibility.Visible;
            _inspKind.Visibility = _inspTitle.Visibility;
            _inspDates.Visibility = _inspTitle.Visibility;
            if (homeRoot)
            {
                _statusSection.Visibility = Visibility.Collapsed;
                SetInspectorFieldVisibility(false, false);
            }

            // Le plan d'un livre ou d'un dossier (batch 35).
            var linkedPlan = _current != null && (_current.Kind == ItemKind.Book || _current.Kind == ItemKind.Folder)
                ? _project.PlanForContainer(_current.Id) : null;
            _inspPlanLink.Tag = linkedPlan;
            _inspPlanLink.Text = linkedPlan == null ? "" : "⇱ Plan : " + linkedPlan.Title;
            _inspPlanLink.Visibility = linkedPlan != null ? Visibility.Visible : Visibility.Collapsed;

            // La visibilité de la barre de droite dépend du niveau courant
            // (projet = masquée) : resynchronisée à chaque navigation. Un
            // panneau que la nature du nouvel élément n'offre pas (Correction
            // sur un livre, Métadonnées sur un écrit…) cède la place au
            // Général — ce qu'on voit est l'état (batch 39).
            if (AppSettings.RightPanel != RightPanel.None
                && !IsPanelAvailable(AppSettings.RightPanel) && IsPanelAvailable(RightPanel.Inspector))
                SetRightPanel(RightPanel.Inspector);
            else
                ApplyPanelVisibility();

            UpdateLinksPanel();
            UpdatePresenceSection();

            _inspDates.Text = string.IsNullOrEmpty(_project.CreatedAt) ? ""
                : "Créé le " + Dates.Display(_project.CreatedAt) + "\nModifié le " + Dates.Display(_project.ModifiedAt);

            UpdateBookProgress();
        }

        /// <summary>Un bouton des raccourcis « Commencer » du Général de
        /// l'Accueil (b43) — pleine largeur, empilés.</summary>
        private Button HomeStartButton(string label, Buttons.Look look, Action onClick)
        {
            var button = Buttons.Text(label, null, Buttons.Bar, look);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Click += delegate { onClick(); };
            return button;
        }

        /// <summary>La barre d'objectif du livre : orange = chapitres présents,
        /// vert = présents ET terminés, sur le nombre visé.</summary>
        private void UpdateBookProgress()
        {
            if (_current == null || _current.Kind != ItemKind.Book)
            {
                _progressSection.Visibility = Visibility.Collapsed;
                return;
            }
            _progressSection.Visibility = Visibility.Visible;
            _bookBar.Show(BookProgress.Of(_current), "Objectif : aucun — définir dans les Options du livre…");
            // Le rythme (b48) : échéance et taille, réglés dans les Options du livre.
            var pace = BookPace.Of(_current, WordsOfItem, CharsOfItem, DateTime.Today);
            var text = pace.Describe(CultureInfo.CurrentCulture);
            _paceLabel.Text = text;
            _paceLabel.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Les mots d'un écrit, depuis le cache (chauffé à l'ouverture).</summary>
        private int WordsOfItem(BinderItem item)
        {
            int words;
            if (_wordCache.TryGetValue(item.Id, out words)) return words;
            words = TextStats.Compute(item.Document.ToPlainText()).Words;
            _wordCache[item.Id] = words;
            return words;
        }

        /// <summary>Les caractères (espaces comprises) d'un écrit — à la demande.</summary>
        private int CharsOfItem(BinderItem item)
        {
            return TextStats.Compute(item.Document.ToPlainText()).Sec;
        }

        /// <summary>La pastille du bouton reflète la couleur de carte de
        /// l'élément courant (batch 43 — l'ancienne rangée de pastilles).</summary>
        private void RebuildColorSwatches()
        {
            var value = _current == null ? null : _current.CardColor;
            _colorDot.Background = value == null
                ? Brushes.Transparent
                : new SolidColorBrush(View.FlowConverter.ParseColor(value));
            _colorButton.ToolTip = value == null ? "Aucune couleur" : value;
        }

        /// <summary>Le menu du bouton couleur : le nuancier, les couleurs
        /// personnalisées du projet, « Nouvelle couleur… ».</summary>
        private void OpenColorMenu()
        {
            if (_current == null) return;
            var menu = new ContextMenu();
            foreach (var swatch in View.ItemIcons.TintSwatches)
                menu.Items.Add(ColorMenuItem(swatch));
            if (_project != null && _project.CustomColors.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var hex in _project.CustomColors)
                    menu.Items.Add(ColorMenuItem(hex));
            }
            menu.Items.Add(new Separator());
            var custom = new MenuItem { Header = "Nouvelle couleur…" };
            custom.Click += delegate
            {
                var hex = View.ColorDialog.Ask(this);
                if (hex == null || _project == null) return;
                if (!_project.CustomColors.Contains(hex)) _project.CustomColors.Insert(0, hex);
                SetCardColor(hex);
            };
            menu.Items.Add(custom);
            menu.PlacementTarget = _colorButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private MenuItem ColorMenuItem(string value)
        {
            var active = _current != null && _current.CardColor == value;
            var item = new MenuItem
            {
                Header = value == null ? "Aucune couleur" : value,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = value == null
                        ? Brushes.Transparent
                        : new SolidColorBrush(View.FlowConverter.ParseColor(value)),
                    BorderBrush = active ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(active ? 2 : 1)
                }
            };
            item.Click += delegate { SetCardColor(value); };
            return item;
        }

        private void SetCardColor(string value)
        {
            if (_current == null) return;
            _current.CardColor = value;
            MarkDirty();
            RebuildColorSwatches();
            RefreshOpenCorkboards();
        }

        /// <summary>Les cartes reflètent état/couleur sans attendre une
        /// navigation : rafraîchit le corkboard ou la vue livre affichés.</summary>
        private void RefreshOpenCorkboards()
        {
            if (_corkboard.Visibility == Visibility.Visible) _corkboard.Refresh();
            if (_bookView.Visibility == Visibility.Visible) _bookView.RefreshCards();
        }

        /// <summary>Chevron + body of the « Statistiques » accordion.</summary>
        private void ApplyStatsExpansion()
        {
            var open = AppSettings.StatsExpanded;
            _statsChevron.Data = Geometry.Parse(open ? "M0,0 L4,4 8,0" : "M0,0 L4,4 0,8");
            _inspStats.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetInspectorFieldVisibility(bool synopsis, bool notes)
        {
            _synopsisLabel.Visibility = synopsis ? Visibility.Visible : Visibility.Collapsed;
            _synopsisBox.Visibility = _synopsisLabel.Visibility;
            _notesLabel.Visibility = notes ? Visibility.Visible : Visibility.Collapsed;
            _notesBox.Visibility = _notesLabel.Visibility;
        }

        /// <summary>Outgoing [[links]] of the current item and every item that
        /// references it, both clickable.</summary>
        private void UpdateLinksPanel()
        {
            _linksPanel.Children.Clear();
            // Un plan ne montre pas de liens (batch 43) : ses [[liens]] de
            // briques rendaient la liste confuse. L'Accueil non plus (son
            // Général = les raccourcis, rien d'autre).
            var hidden = _current != null && (_current.Kind == ItemKind.Plan || _current.IsHomeRoot);
            _linksLabel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            _linksPanel.Visibility = _linksLabel.Visibility;
            if (hidden) return;
            if (_current == null || _current.IsCategory)
            {
                _linksPanel.Children.Add(new TextBlock
                {
                    Text = "—",
                    Foreground = Chrome.SoftText,
                    FontSize = 12
                });
                return;
            }

            // Outgoing: the [[targets]] of this item's own text (« [[Cible|texte]] » compris, 18/09).
            foreach (var title in Links.Targets(_current.SearchText()))
                _linksPanel.Children.Add(LinkRow("arrow-right-bold", title, title, _project.FindByTitle(title) != null));

            // Incoming: items whose text links to this title.
            foreach (var item in _project.AllItems())
            {
                if (item == _current || item.IsCategory) continue;
                if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) continue;
                if (!Links.LinksTo(item.SearchText(), _current.Title)) continue;
                _linksPanel.Children.Add(LinkRow("arrow-up-left-bold", item.Title, item.Title, true));
            }

            if (_linksPanel.Children.Count == 0)
                _linksPanel.Children.Add(new TextBlock
                {
                    Text = "Aucun lien",
                    Foreground = Chrome.SoftText,
                    FontSize = 12
                });
        }

        /// <summary>Une ligne du panneau des liens : la flèche livrée (b36 —
        /// sortant → / entrant ↖) puis le titre.</summary>
        private UIElement LinkRow(string icon, string label, string targetTitle, bool resolved)
        {
            var brush = resolved ? (System.Windows.Media.Brush)Chrome.Accent : Chrome.SoftText;
            var row = new DockPanel
            {
                Margin = new Thickness(0, 1, 0, 1),
                ToolTip = resolved ? "Ouvrir" : "Cible inexistante (Ctrl+clic dans le texte pour la créer)",
                Background = System.Windows.Media.Brushes.Transparent
            };
            var arrow = Icons.Make(icon, 10, brush) as FrameworkElement;
            if (arrow != null)
            {
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(0, 0, 5, 0);
                DockPanel.SetDock(arrow, Dock.Left);
                row.Children.Add(arrow);
            }
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = brush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (resolved)
            {
                row.Cursor = System.Windows.Input.Cursors.Hand;
                row.MouseLeftButtonDown += delegate { NavigateToTitle(targetTitle); };
            }
            return row;
        }

        /// <summary>« Personnages présents » (b47) : sur un écrit seulement.</summary>
        private void UpdatePresenceSection()
        {
            var isText = _current != null && _current.Kind == ItemKind.Text && !_current.IsExtraPage;
            _presenceSection.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
            _presencePanel.Children.Clear();
            if (!isText) return;
            var project = _project;
            var rows = Presence.In(_current, project, delegate(BinderItem sheet)
            {
                var category = project.SheetCategoryOf(sheet);
                return category != null && Achievements.IsCharacterCategory(category.Name);
            });
            if (rows.Count == 0)
            {
                _presencePanel.Children.Add(new TextBlock { Text = "—", Foreground = Chrome.SoftText, FontSize = 12 });
                return;
            }
            var shown = 0;
            foreach (var row in rows)
            {
                if (shown++ >= 12)
                {
                    _presencePanel.Children.Add(new TextBlock
                    {
                        Text = "… et " + (rows.Count - 12) + " autres",
                        Foreground = Chrome.SoftText,
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    break;
                }
                var sheet = row.Text;
                var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Ouvrir la fiche" };
                var count = new TextBlock
                {
                    Text = row.Count.ToString(CultureInfo.InvariantCulture),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                DockPanel.SetDock(count, Dock.Right);
                line.Children.Add(count);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = sheet.Title, FontSize = 12, Foreground = Chrome.Accent, TextTrimming = TextTrimming.CharacterEllipsis });
                var step = Presence.StepIn(sheet, _current.Id);
                if (step != null)
                    text.Children.Add(new TextBlock
                    {
                        Text = step.Note.Trim(),
                        FontSize = 11,
                        FontStyle = FontStyles.Italic,
                        Foreground = Chrome.SoftText,
                        TextWrapping = TextWrapping.Wrap
                    });
                line.Children.Add(text);
                line.MouseLeftButtonDown += delegate { _binder.SelectItem(sheet.Id); };
                _presencePanel.Children.Add(line);
            }
        }

        // ============================================================= épinglé sur le côté (b47)

        /// <summary>Épingle un écrit ou une fiche : le projet le retient, le
        /// panneau le rend, la colonne l'affiche.</summary>
        private void PinToSide(BinderItem item)
        {
            if (item == null || (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet)) return;
            _sidePin = item;
            _project.SidePinId = item.Id;
            _pinnedPanel.Show(item);
            SetRightPanel(RightPanel.Pinned);
            UpdateRail();
        }

        private void UnpinSide()
        {
            if (_sidePin == null) return;
            _sidePin = null;
            _project.SidePinId = null;
            _pinnedPanel.Clear();
            if (AppSettings.RightPanel == RightPanel.Pinned) SetRightPanel(RightPanel.Inspector);
            else ApplyPanelVisibility();
        }

        /// <summary>À l'ouverture : l'id du projet redevient un item — s'il
        /// existe encore et ne dort pas à la corbeille.</summary>
        private void ResolveSidePin()
        {
            _sidePin = null;
            var item = string.IsNullOrEmpty(_project.SidePinId) ? null : _project.FindById(_project.SidePinId);
            if (item != null && (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                && item.RootCategory().CategoryKey != Project.KeyTrash)
                _sidePin = item;
            else _project.SidePinId = null;
            _pinnedPanel.Show(_sidePin);
            if (_sidePin == null && AppSettings.RightPanel == RightPanel.Pinned) AppSettings.RightPanel = RightPanel.Inspector;
        }

        /// <summary>Après un changement de la Pile : l'épinglé jeté ou
        /// supprimé lâche l'épingle, sinon le miroir se rafraîchit (titre…).</summary>
        private void ValidateSidePin()
        {
            if (_sidePin == null) return;
            var alive = _project.FindById(_sidePin.Id) == _sidePin
                && _sidePin.RootCategory().CategoryKey != Project.KeyTrash;
            if (!alive) { UnpinSide(); return; }
            RefreshSidePin();
        }

        private void RefreshSidePin()
        {
            if (_sidePin == null || _pinnedHost.Visibility != Visibility.Visible) return;
            _pinnedPanel.Refresh();
        }

        /// <summary>L'infobulle de l'onglet Épinglé dit ce qui est épinglé.</summary>
        private void UpdatePinTip()
        {
            Border tab;
            if (!_railTabs.TryGetValue(RightPanel.Pinned, out tab)) return;
            var tip = tab.ToolTip as ToolTip;
            if (tip == null) return;
            tip.Content = _sidePin == null
                ? "Épinglé au rail — rien pour l'instant : clic droit sur un écrit ou une fiche › « Épingler au rail »"
                : "Épinglé au rail — " + _sidePin.Title;
        }

        private void UpdateStats()
        {
            if (_current != null && _current.Kind == ItemKind.Text && _editor.HasItem)
            {
                // Incrémental (22/09) : la composition recompte le seul
                // paragraphe modifié — 200 ms de moins par pause sur 280 pages.
                var stats = _editor.CompositionStats() ?? TextStats.Compute(_editor.PlainText());
                // Journal perso : le delta net du document ouvert est crédité au
                // jour courant (cache chauffé au chargement et aux imports, donc
                // un premier passage sans référence ne crédite jamais un stock).
                int before;
                if (_wordCache.TryGetValue(_current.Id, out before) && stats.Words != before)
                    AddJournalWords(stats.Words - before);
                _wordCache[_current.Id] = stats.Words;
                _statusRight.Text = stats.ShortLabel();
                _inspStats.Text = stats.LongLabel();
            }
            else if (_current != null && _current.Kind == ItemKind.Sheet && _sheetView.HasItem)
            {
                // Batch 43 : plus de statistiques dans le Général d'une fiche —
                // la barre d'état garde son décompte discret.
                var stats = TextStats.Compute(_sheetView.BodyPlainText());
                _statusRight.Text = stats.ShortLabel();
                _inspStats.Text = "";
            }
            else if (_current != null && _current.Kind == ItemKind.MindMap)
            {
                CommitMindMap();
                var summary = MindMaps.Inspect(_current.MapBytes);
                _statusRight.Text = summary.Label;
                _inspStats.Text = summary.Label;
            }
            else if (_current != null && _current.Kind == ItemKind.Book)
            {
                _statusRight.Text = "";
                _inspStats.Text = BookStatsLabel(_current);
                UpdateBookProgress();
            }
            else
            {
                _statusRight.Text = "";
                _inspStats.Text = "";
            }
            _statsSection.Visibility = _inspStats.Text.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;

            var left = _path == null ? _project.Name + " (jamais enregistré)" : _path;
            if (_sessionGoal > 0)
            {
                var written = Math.Max(0, ProjectWords() - _sessionBaseWords);
                var culture = CultureInfo.CurrentCulture;
                left += "   ·   objectif : " + written.ToString("N0", culture)
                     + " / " + _sessionGoal.ToString("N0", culture) + " mots"
                     + (written >= _sessionGoal ? " — atteint !" : "");
            }
            _statusLeft.Text = left;
        }

        /// <summary>Statistiques d'un livre pour l'inspecteur : nombre de
        /// textes du récit, répartition par état, mots/signes (liminaires et
        /// TdM COMPRIS — tout ce qui s'imprime compte, batch 28) et le total
        /// de pages du livre (recto d'ouverture par document inclus).</summary>
        private string BookStatsLabel(BinderItem book)
        {
            var all = new List<BinderItem>();
            CollectBookTexts(book, all);
            var texts = new List<BinderItem>();
            foreach (var text in all)
                if (!text.IsExtraPage && !text.IsToc) texts.Add(text);
            var words = 0;
            var signs = 0;
            foreach (var text in all) // extras et TdM compris
            {
                var stats = TextStats.Compute(text.Document.ToPlainText());
                words += stats.Words;
                signs += stats.Sec;
            }
            var culture = CultureInfo.CurrentCulture;
            var label = "Textes : " + texts.Count.ToString("N0", culture);
            foreach (var key in TextStatus.Keys)
            {
                var count = 0;
                foreach (var text in texts) if (text.Status == key) count++;
                if (count > 0)
                    label += "\n" + TextStatus.Label(key) + " : "
                        + count.ToString("N0", culture);
            }
            label += "\nMots : " + words.ToString("N0", culture)
                  + "\nSignes : " + signs.ToString("N0", culture)
                  + "\nPages : " + BookPageTotal(book).ToString("N0", culture);
            return label;
        }

        private void ShowAbout()
        {
            UnlockAchievement(Achievements.About); // « Enfin quelqu'un qui en a quelque chose à faire ! »
            MessageDialog.Show(this,
                AppName + " " + AppVersion + "\n\n" +
                "Traitement de texte et construction narrative.\n" +
                "Bêta : pages composées, livres et gabarits, fiches wiki, plans,\n" +
                "cartes mentales (module), échanges docx/odt/RTF/Markdown/Scrivener,\n" +
                "EPUB, PDF, impression, correction (orthographe, grammaire, typographie, style),\n" +
                "versions, secours, succès.\n\n" +
                "© 2026 Rémi Escamilla — logiciel libre sous licence GNU GPL v3 ou ultérieure\n" +
                "(fichier LICENSE ; sources : " + Updater.RepositoryUrl + ").\n\n" +
                "Ressources embarquées :\n" +
                "• Dictionnaire orthographique français « toutes variantes » v7.7\n" +
                "  par Olivier R. — licence MPL-2.0 — https://grammalecte.net/\n" +
                "  (notice complète : dict\\README_dict_fr.txt)\n" +
                "• Grammalecte 2.3.0, correcteur grammatical par Olivier R.\n" +
                "  — licence GPL-3.0+ — https://grammalecte.net/\n" +
                "  (le source Python livré dans grammalecte\\ EST le source)\n" +
                "• Python " + Correction.Grammalecte.GrammalecteBridge.EmbeddedPythonVersion + " embeddable (runtime de Grammalecte)\n" +
                "  — licence PSF — https://www.python.org/\n" +
                "• Icônes Phosphor — licence MIT — https://phosphoricons.com/\n" +
                "• Icônes Flaticon — https://www.flaticon.com/ (crédit exigé)",
                "À propos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Minimal ICommand wrapper for code-built key bindings.</summary>
    public class DelegateCommand : ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { _execute(); }
    }
}
