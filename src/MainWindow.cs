using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Correction;
using UniversSale.History;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale
{
    /// <summary>The shell window: Binder (left) | rich editor (center) |
    /// inspector (right), menu bar on top, status bar below.</summary>
    public class MainWindow : Window
    {
        public const string AppName = "Marabook";
        public const string AppVersion = "0.23.0-alpha";

        private Project _project;
        private string _path;
        private bool _dirty;
        private readonly HistoryManager _history = new HistoryManager();

        private BinderView _binder;
        private ColumnDefinition _binderCol, _inspectorCol;
        private GridSplitter _binderSplit, _inspectorSplit;

        private EditorView _editor;
        private SheetView _sheetView;
        private CorkboardView _corkboard;
        private View.BookView _bookView;
        private View.TemplateView _templateView;
        private bool _navigating; // garde anti-sélection-fantôme (voir OnBinderSelection)
        private MediaView _mediaView;
        private JournalView _journalView;
        private bool _journalOpen; // le journal masque l'inspecteur
        private Grid _centerHost; // hôte du toast de célébration
        private UIElement _menuBar, _statusBar;
        private Border _calmExit;  // bouton flottant de sortie du mode calme
        private bool _calmMode;
        private TextBlock _placeholder;
        private BinderItem _current;

        private Border _inspector;
        private TextBlock _inspTitle, _inspKind, _inspStats, _inspDates;
        private StackPanel _statsSection;
        private System.Windows.Shapes.Path _statsChevron;
        private StackPanel _statusSection;   // état du texte + couleur de carte
        private ComboBox _statusCombo;
        private WrapPanel _colorSwatches;
        private TextBlock _statusLabel, _colorLabel;
        private TextBlock _synopsisLabel, _notesLabel;
        private TextBox _synopsisBox, _notesBox;
        private StackPanel _linksPanel;
        private bool _loadingInspector;

        private TextBlock _statusLeft, _statusRight, _statusPages, _zoomLabel;
        private DispatcherTimer _statsTimer, _autosaveTimer;

        private MenuItem _undoMenu, _redoMenu, _darkMenu, _binderMenu, _inspectorMenu, _recentMenu, _rulersMenu;

        // Bandeau « lecture seule » : projet écrit par un format plus récent
        // que celui que cette version sait réécrire sans perte (A2, batch 24).
        private Border _readOnlyBanner;

        // Session goal: words written since the goal was set, project-wide.
        // The same cache feeds the writing journal: each recount of the OPEN
        // document yields a net delta, credited to today — imports, purges and
        // moves never touch the journal (see UpdateStats).
        private readonly Dictionary<string, int> _wordCache = new Dictionary<string, int>();
        private int _sessionGoal, _sessionBaseWords;

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
            Content = root;
            _editor.CalmRequested += ToggleCalmMode;
            _sheetView.CalmRequested += ToggleCalmMode;
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

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _statsTimer.Tick += delegate
            {
                _statsTimer.Stop();
                UpdateStats();
                _editor.SyncNotes();
            };
            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _autosaveTimer.Tick += delegate { Autosave(); };
            _autosaveTimer.Start();

            Closing += OnClosingWindow;

            LoadProject(Project.CreateNew(), null);
        }

        // ============================================================= layout

        private UIElement BuildReadOnlyBanner()
        {
            _readOnlyBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xB4, 0x5B, 0x00)),
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
            file.Items.Add(Entry("project-settings", "Paramètres du projet…", OpenProjectSettings));
            file.Items.Add(Entry("preferences", "Préférences…", OpenPreferences));
            file.Items.Add(new Separator());
            file.Items.Add(Entry("print-preview", "Aperçu des pages", ShowPrintPreview));
            file.Items.Add(Entry("print", "Imprimer…", PrintCurrent));
            file.Items.Add(new Separator());
            var importMenu = new MenuItem { Header = "Importer" };
            importMenu.Items.Add(Entry("import-docs", "Des documents…", ImportDocuments));
            importMenu.Items.Add(Entry("import-scrivener", "Un projet Scrivener…", ImportScrivener));
            file.Items.Add(importMenu);
            var exportMenu = new MenuItem { Header = "Exporter" };
            exportMenu.Items.Add(Entry("export-item", "L'écrit sélectionné…", ExportCurrentItem));
            exportMenu.Items.Add(Entry("compile", "Compiler le manuscrit…", CompileManuscript));
            exportMenu.Items.Add(Entry("export-pdf", "PDF prêt à imprimer…", ExportPdf));
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
            edit.Items.Add(Entry("find", "Rechercher dans l'écrit…", ShowSearchInActive));
            edit.Items.Add(Entry("project-search", "Rechercher dans le projet", delegate { _binder.FocusSearch(); }));
            edit.Items.Add(Entry("session-goal", "Objectif de session…", SetSessionGoal));
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("new-text", "Nouvel écrit", delegate { _binder.NewText(null); }));
            edit.Items.Add(Entry("new-sheet", "Nouvelle fiche", delegate { _binder.NewSheet(null); }));
            edit.Items.Add(Entry("new-folder", "Nouveau dossier", delegate { _binder.NewFolder(null); }));
            edit.Items.Add(Entry("new-book", "Nouveau livre", delegate { _binder.NewBook(null); }));
            edit.Items.Add(Entry("import-media", "Importer dans Recherche…", delegate { _binder.ImportMediaDialog(null); }));
            edit.Items.Add(Entry("rename", "Renommer…", delegate { _binder.Rename(null); }));
            edit.Items.Add(Entry("delete", "Supprimer", delegate { _binder.Delete(null); }));
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("empty-trash", "Vider la corbeille", delegate { _binder.EmptyTrash(); }));
            menu.Items.Add(edit);

            // --- Format ---
            var format = new MenuItem { Header = "F_ormat" };
            format.Items.Add(Entry("styles", "Gérer les styles…", OpenStylesDialog));
            format.Items.Add(Entry("templates", "Modèles de fiches…", OpenTemplatesDialog));
            format.Items.Add(new Separator());
            format.Items.Add(Entry("insert-footnote", "Note de bas de page", InsertFootnoteInActive));
            format.Items.Add(Entry("insert-link", "Lien vers une fiche…", InsertLinkInActive));
            format.Items.Add(Entry("insert-image", "Insérer une image…", InsertImageInActive));
            format.Items.Add(Entry("insert-rule", "Ligne horizontale", delegate { RouteToActiveEditor("rule"); }));
            format.Items.Add(Entry("insert-separator", "Séparateur de scène", delegate { RouteToActiveEditor("separator"); }));
            menu.Items.Add(format);

            // « Mise en page » lives as a ribbon tab in the editor now; only
            // its shortcut survives at the window level.
            AddGesture("page-break", InsertPageBreakInActive);

            // --- Affichage ---
            var view = new MenuItem { Header = "_Affichage" };
            _binderMenu = Entry("toggle-binder", "Pile", ToggleBinder);
            _binderMenu.IsCheckable = true;
            _inspectorMenu = Entry("toggle-inspector", "Inspecteur", ToggleInspector);
            _inspectorMenu.IsCheckable = true;
            _darkMenu = Entry("dark-theme", "Thème sombre", ToggleDarkTheme);
            _darkMenu.IsCheckable = true;
            _darkMenu.IsChecked = AppSettings.DarkTheme;
            _rulersMenu = Entry("toggle-rulers", "Règles", ToggleRulers);
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
            help.Items.Add(Entry(null, "À propos de Marabook…", ShowAbout));
            menu.Items.Add(help);

            bar.Child = menu;
            return bar;
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
            var item = new MenuItem { Header = header };
            item.Click += delegate { handler(); };
            if (actionId != null)
            {
                var gesture = AppSettings.Gesture(actionId);
                item.InputGestureText = AppSettings.DisplayGesture(gesture);
                Key key;
                ModifierKeys modifiers;
                if (AppSettings.ParseGesture(gesture, out key, out modifiers))
                    InputBindings.Add(new KeyBinding(new DelegateCommand(handler), key, modifiers));
            }
            return item;
        }

        private UIElement BuildContent()
        {
            var grid = new Grid();
            _binderCol = new ColumnDefinition { Width = new GridLength(AppSettings.BinderWidth), MinWidth = 0 };
            var binderSplitCol = new ColumnDefinition { Width = GridLength.Auto };
            var centerCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 };
            var inspectorSplitCol = new ColumnDefinition { Width = GridLength.Auto };
            _inspectorCol = new ColumnDefinition { Width = new GridLength(AppSettings.InspectorWidth), MinWidth = 0 };
            grid.ColumnDefinitions.Add(_binderCol);
            grid.ColumnDefinitions.Add(binderSplitCol);
            grid.ColumnDefinitions.Add(centerCol);
            grid.ColumnDefinitions.Add(inspectorSplitCol);
            grid.ColumnDefinitions.Add(_inspectorCol);

            _binder = new BinderView();
            _binder.SelectionChanged += OnBinderSelection;
            _binder.JournalRequested += ShowJournal;
            _binder.StructureChanged += delegate
            {
                MarkDirty();
                _pageCountCache.Clear(); // moves change book folio offsets
                // Chauffe le cache de mots : un document importé entre au cache
                // à sa taille réelle, sans jamais créditer le journal.
                ProjectWords();
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
            _editor.ZoomStepRequested += delegate(int step) { ApplyZoom(AppSettings.Zoom + step); };
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
            _editor.CompileRequested += CompileManuscript;
            _editor.PdfRequested += ExportPdf;
            center.Children.Add(_editor);

            _sheetView = new SheetView { Visibility = Visibility.Collapsed };
            _sheetView.Edited += OnEditorEdited;
            _sheetView.LinkClicked += NavigateToTitle;
            _sheetView.ZoomStepRequested += delegate(int step) { ApplyZoom(AppSettings.Zoom + step); };
            _sheetView.PageSetupChanged += delegate
            {
                _editor.ApplyPageSetup(_project.Page);
                MarkDirty();
            };
            _sheetView.MarksToggled += OnMarksToggled;
            _sheetView.StylesRequested += OpenStylesDialog;
            _sheetView.PreviewRequested += ShowPrintPreview;
            _sheetView.PrintRequested += PrintCurrent;
            _sheetView.ExportRequested += ExportCurrentItem;
            _sheetView.CompileRequested += CompileManuscript;
            _sheetView.PdfRequested += ExportPdf;
            center.Children.Add(_sheetView);

            _corkboard = new CorkboardView { Visibility = Visibility.Collapsed };
            _corkboard.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _corkboard.Changed += delegate { MarkDirty(); UpdateInspector(); _binder.Rebuild(); };
            _corkboard.ExportRequested += ExportItem;
            _corkboard.DeleteRequested += delegate(BinderItem item) { _binder.Delete(item); };
            _corkboard.ApplyTemplateRequested += ApplyPageTemplateTo;
            center.Children.Add(_corkboard);

            _bookView = new BookView { Visibility = Visibility.Collapsed };
            _bookView.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _bookView.Changed += delegate { MarkDirty(); UpdateInspector(); _binder.Rebuild(); };
            _bookView.PublishRequested += PublishBook;
            _bookView.ExportRequested += ExportItem;
            _bookView.DeleteRequested += delegate(BinderItem item) { _binder.Delete(item); };
            _bookView.ApplyTemplateRequested += ApplyPageTemplateTo;
            _bookView.NewTemplateRequested += NewPageTemplate;
            _bookView.ExportTemplateRequested += ExportPageTemplate;
            _bookView.ImportTemplateRequested += ImportPageTemplate;
            _bookView.CopyTemplateRequested += CopyPageTemplate;
            _bookView.NewDocumentRequested += NewBookDocument;
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

            ApplyPanelVisibility();
            return grid;
        }

        private Border BuildInspector()
        {
            var panel = new StackPanel { Margin = new Thickness(14) };

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
            _colorSwatches = new WrapPanel();
            _statusSection.Children.Add(_colorSwatches);
            panel.Children.Add(_statusSection);

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

            panel.Children.Add(new TextBlock
            {
                Text = "Liens",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 4)
            });
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
            var zoomOut = SmallZoomButton("−", -10);
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
            var zoomIn = SmallZoomButton("+", 10);
            zoomPanel.Children.Add(zoomOut);
            zoomPanel.Children.Add(_zoomLabel);
            zoomPanel.Children.Add(zoomIn);
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

        private Button SmallZoomButton(string label, int step)
        {
            var button = new Button
            {
                Content = label,
                Width = 22,
                Padding = new Thickness(0),
                Focusable = false
            };
            button.Click += delegate { ApplyZoom(AppSettings.Zoom + step); };
            return button;
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
            AppSettings.Save();
        }

        // ============================================================= project lifecycle

        private void LoadProject(Project project, string path)
        {
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
            _editor.SetStyleSheet(project.Styles);
            _editor.SetProject(project);
            _editor.ApplyPageSetup(project.Page);
            _editor.Clear();
            _sheetView.SetStyleSheet(project.Styles);
            _sheetView.SetProject(project);
            _sheetView.ApplyPageSetup(project.Page);
            _sheetView.Clear();
            _mediaView.Clear();
            _corkboard.Clear();
            _journalView.Clear();
            _binder.LoadProject(project, _history);
            // Chauffe le cache de mots : les comptes existants deviennent la
            // référence des deltas du journal (rien n'est crédité à l'ouverture).
            ProjectWords();
            ShowItem(null);
            UpdateRecentMenu();
            UpdateTitle();
            UpdateStats();
            UpdateInspector();
        }

        public void OpenFile(string path)
        {
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(path, warnings);
                project.Name = Path.GetFileNameWithoutExtension(path);
                LoadProject(project, path);
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
                    MessageBox.Show(this,
                        "Le projet s'est ouvert, avec des réserves :\n\n— "
                        + string.Join("\n— ", warnings.ToArray()),
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception error)
            {
                TryRecoverFromBackup(path, error);
            }
        }

        /// <summary>The .plot itself is unreadable: offer the rolling .bak
        /// (written at every successful save) before giving up.</summary>
        private void TryRecoverFromBackup(string path, Exception error)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak))
            {
                MessageBox.Show(this,
                    "Impossible d'ouvrir le projet :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var stamp = File.GetLastWriteTime(bak).ToString("dd/MM/yyyy HH:mm");
            var answer = MessageBox.Show(this,
                "Impossible d'ouvrir le projet :\n" + error.Message + "\n\n"
                + "Une copie de secours existe (dernier enregistrement réussi, "
                + "du " + stamp + ").\nL'ouvrir à la place ?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(bak, warnings);
                project.Name = Path.GetFileNameWithoutExtension(path);
                // Le projet vit sous son chemin normal : le prochain Ctrl+S
                // remplacera le .plot corrompu. Marqué modifié pour que la
                // fermeture propose cet enregistrement.
                LoadProject(project, path);
                _dirty = true;
                UpdateTitle();
                if (warnings.Count > 0)
                    MessageBox.Show(this,
                        "La copie de secours s'est ouverte, avec des réserves :\n\n— "
                        + string.Join("\n— ", warnings.ToArray()),
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception bakError)
            {
                MessageBox.Show(this,
                    "La copie de secours est illisible elle aussi :\n" + bakError.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoNew()
        {
            if (!ConfirmDiscard()) return;
            LoadProject(Project.CreateNew(), null);
        }

        private void DoOpen()
        {
            if (!ConfirmDiscard()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = PlotFile.OpenFilter };
            if (dialog.ShowDialog(this) == true)
                OpenFile(dialog.FileName);
        }

        private void DoSave()
        {
            if (_path == null) { DoSaveAs(); return; }
            if (_project.ReadOnlyNewerFormat)
            {
                MessageBox.Show(this,
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
                CommitActive();
                PlotFile.Save(_project, _path);
                _dirty = false;
                AppSettings.AddRecentFile(_path);
                AppSettings.Save();
                UpdateRecentMenu();
                UpdateTitle();
                UpdateInspector();
            }
            catch (Exception error)
            {
                MessageBox.Show(this,
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
                MessageBox.Show(this,
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
            _project.Name = Path.GetFileNameWithoutExtension(_path);
            DoSave();
        }

        private void Autosave()
        {
            if (_project.ReadOnlyNewerFormat) return; // never write a newer format
            if (_dirty && _path != null) DoSave();
        }

        /// <summary>True when it is safe to drop the current project.</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            if (_project.ReadOnlyNewerFormat)
            {
                // Saving is impossible in this state: offer to leave anyway.
                var leave = MessageBox.Show(this,
                    "Ce projet est ouvert en lecture seule (format plus récent) :\n"
                    + "les modifications ne peuvent pas être enregistrées.\n"
                    + "Continuer et les abandonner ?",
                    AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return leave == MessageBoxResult.Yes;
            }
            var answer = MessageBox.Show(this,
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
            AppSettings.BinderWidth = _binderCol.Width.Value > 0 ? _binderCol.Width.Value : AppSettings.BinderWidth;
            AppSettings.InspectorWidth = _inspectorCol.Width.Value > 0 ? _inspectorCol.Width.Value : AppSettings.InspectorWidth;
            AppSettings.Save();
        }

        // ============================================================= editing

        private void CommitActive()
        {
            if (_editor.HasItem) _editor.Commit();
            if (_sheetView.HasItem) _sheetView.Commit();
            // Template zones normally commit on focus loss, but a pending
            // debounced edit must not be lost by a save that races it.
            _templateView.CommitZones();
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
            // The phantom may have moved the tree's selection while we were
            // opening: pull it back onto the item actually shown — sans
            // BringIntoView : le défilement déplaçait la ligne sous la souris
            // entre les deux clics d'un double-clic (renommage cassé).
            if (item != null) _binder.SelectItem(item.Id, false);
            UpdateInspector();
            UpdateStats();
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
            return _corkboard.Visibility == Visibility.Visible && _corkboard.ShowsItem(item);
        }

        private void ShowItem(BinderItem item)
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
                if (item.IsToc) RegenerateToc(item); // table des matières à jour
                _editor.FolioOffset = ComputeFolioOffset(item);
                _editor.Decor = PageDecor.For(item, _project);
                _editor.ApplyPageSetup(item.Page ?? _project.Page);
                _editor.LoadItem(item);
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
                if (_inspectorCol.Width.Value > 0)
                    AppSettings.InspectorWidth = _inspectorCol.Width.Value;
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
            var answer = MessageBox.Show(this,
                "Aucun élément ne s'intitule « " + title + " ».\nCréer une fiche à ce nom ?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            var sheets = _project.Category(Project.KeySheets);
            var sheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                TemplateId = _project.Templates.Count > 0 ? _project.Templates[0].Id : null
            };
            _history.Run(new History.AddItemAction(sheets, sheet, -1));
            MarkDirty();
            _binder.SelectItem(sheet.Id);
        }

        private void OnEditorEdited()
        {
            MarkDirty();
            // The edited document's page count is stale (book folio offsets).
            if (_current != null) _pageCountCache.Remove(_current.Id);
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
        }

        private void MarkDirty()
        {
            if (_dirty) return;
            _dirty = true;
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
                MessageBox.Show(this, "Documents non importés :\n\n" + string.Join("\n", errors.ToArray()),
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private TextDocument ImportOneDocument(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx") return Exchange.Docx.Import(path, _project.Styles);
            if (ext == ".odt") return Exchange.Odt.Import(path, _project.Styles);
            if (ext == ".rtf") return Exchange.Rtf.Import(path, _project.Styles);
            if (ext == ".md" || ext == ".markdown")
                return Exchange.MarkdownExchange.Import(File.ReadAllText(path));
            if (ext == ".txt") return TextDocument.FromPlainText(File.ReadAllText(path));
            if (ext == ".doc")
                return Exchange.Docx.Import(Exchange.ExternalBridge.DocToDocx(path), _project.Styles);
            if (ext == ".gdoc")
            {
                MessageBox.Show(this, Exchange.ExternalBridge.GdocGuidance,
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
                MessageBox.Show(this,
                    "Projet Scrivener importé. Pensez à l'enregistrer au format .plot (Ctrl+S).",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Import Scrivener impossible :\n" + error.Message,
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
                MessageBox.Show(this, "Sélectionnez d'abord un écrit (ou une fiche) à exporter.",
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
            MarkDirty(); // the dialog may have set the author
            var manuscript = Exchange.Compiler.Build(_project, request.Root, request.Options);
            ExportDocument(manuscript, _project.Name);
        }

        private void ExportDocument(TextDocument document, string defaultName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = DocumentExportFilter,
                FileName = SafeFileName(defaultName)
            };
            if (dialog.ShowDialog(this) != true) return;
            var path = dialog.FileName;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".docx") Exchange.Docx.Export(document, _project.Styles, path, _project.Page);
                else if (ext == ".odt")
                    Exchange.Odt.Export(Exchange.Compiler.FlattenLists(document), _project.Styles, path);
                else if (ext == ".rtf")
                    Exchange.Rtf.Export(Exchange.Compiler.FlattenRules(document), _project.Styles, path, _project);
                else if (ext == ".md")
                    File.WriteAllText(path, Exchange.MarkdownExchange.Export(document), new System.Text.UTF8Encoding(false));
                else
                    File.WriteAllText(path, Exchange.Compiler.FlattenLists(document).ToPlainText(),
                        new System.Text.UTF8Encoding(false));
                MessageBox.Show(this, "Export terminé :\n" + path,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Export impossible :\n" + error.Message,
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
            var edited = StylesDialog.Show(this, _project.Styles);
            if (edited == null) return;
            _project.Styles = edited;
            _editor.SetStyleSheet(edited);
            _editor.Reload();
            _sheetView.SetStyleSheet(edited);
            _sheetView.ReloadBody();
            MarkDirty();
        }

        private void OpenTemplatesDialog()
        {
            CommitActive();
            var edited = TemplatesDialog.Show(this, _project.Templates);
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

        private void OpenProjectSettings()
        {
            if (View.ProjectSettingsDialog.Show(this, _project)) MarkDirty();
        }

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
                return _current.Document;
            }
            if (_current != null && _current.IsContainer)
            {
                name = _current.Title;
                // A book's pages follow its gabarit, whatever the project says.
                if (_current.Kind == ItemKind.Book && _current.Book != null)
                    setup = _current.Book.Template;
                return Exchange.Compiler.Build(_project, _current, new Exchange.CompileOptions
                {
                    TitlePage = false,
                    ChapterHeadings = false,
                    PageBreakPerText = true
                });
            }
            MessageBox.Show(this,
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
                Print.Printing.ShowPreview(this, document, _project.Styles, _project,
                    name, setup, offset, decor);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Aperçu impossible :\n" + error.Message,
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
                Print.Printing.Print(document, _project.Styles, _project,
                    AppName + " — " + name, setup, offset, decor);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Impression impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>4b-2 : the home-grown print-ready PDF (embedded subset
        /// fonts, trim/bleed boxes, crop marks) — no printer driver involved.</summary>
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
            Print.PdfExportOptions options, PageDecor decor, int folioOffset)
        {
            try
            {
                var composition = Print.Composer.Compose(
                    document, _project.Styles, setup, _project);
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
                var composition = Print.Composer.Compose(text.Document,
                    _project.Styles, text.Page ?? _project.Page, _project);
                pages = Math.Max(1, composition.Pages.Count);
            }
            catch { pages = 1; }
            _pageCountCache[text.Id] = pages;
            return pages;
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
        private void NewBookDocument(BinderItem book, string kind)
        {
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
            _history.Run(new AddItemAction(book, item, -1));
            if (item.IsToc) RegenerateToc(item);
            MarkDirty();
            _pageCountCache.Clear();
            _binder.Rebuild();
            if (_current == book) _bookView.Load(book, _history, _project);
        }

        /// <summary>Rebuilds a dynamic table of contents: every non-extra
        /// document of the book, in order, with its opening folio.</summary>
        private void RegenerateToc(BinderItem toc)
        {
            var book = toc.EnclosingBook();
            if (book == null || book.Book == null) return;
            var texts = new List<BinderItem>();
            CollectBookTexts(book, texts);
            var entries = new List<ExtraPages.TocEntry>();
            foreach (var text in texts)
            {
                if (text.IsExtraPage || text == toc) continue;
                entries.Add(new ExtraPages.TocEntry
                {
                    Title = text.Title,
                    Folio = ComputeFolioOffset(text) + 1
                });
            }
            ExtraPages.EnsureStyle(_project);
            ExtraPages.FillToc(toc.Document, entries,
                ExtraPages.LinesFor(book.Book.Template, _project));
            _pageCountCache.Remove(toc.Id); // son nombre de pages a pu changer
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
                MessageBox.Show(this, "Gabarit exporté :\n" + dialog.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Export impossible :\n" + error.Message,
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
                MessageBox.Show(this, "Import impossible :\n" + error.Message,
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
                MessageBox.Show(this, "Aucun autre livre dans ce projet.",
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
                MessageBox.Show(this,
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
            // Tables des matières à jour avant compilation.
            foreach (var item in _project.AllItems())
                if (item.IsToc && item.EnclosingBook() == book) RegenerateToc(item);
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
                { return PreviewPdf(document, book.Book.Template, book.Title, o, null, 0); });
            if (options == null) return;
            WritePdf(document, book.Book.Template, book.Title, options);
        }

        private void WritePdf(TextDocument document, PageSetup setup, string name,
            Print.PdfExportOptions options, PageDecor decor = null, int folioOffset = 0)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = SafeFileName(name) + ".pdf",
                Title = "PDF prêt à imprimer"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var composition = Print.Composer.Compose(
                    document, _project.Styles, setup, _project);
                composition.DefaultDecor = decor;
                composition.FolioOffset = folioOffset;
                Print.PdfWriter.Write(dialog.FileName, composition, options);
                MessageBox.Show(this, "Export terminé :\n" + dialog.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Export PDF impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================= session goal

        private void SetSessionGoal()
        {
            var answer = View.InputDialog.Ask(this, "Objectif de session",
                "Nombre de mots à écrire (0 pour désactiver) :",
                _sessionGoal > 0 ? _sessionGoal.ToString() : "500");
            if (answer == null) return;
            int goal;
            if (!int.TryParse(answer.Trim(), out goal) || goal <= 0)
            {
                _sessionGoal = 0;
                UpdateStats();
                return;
            }
            _sessionGoal = goal;
            _sessionBaseWords = ProjectWords();
            UpdateStats();
        }

        // ============================================================= journal perso

        /// <summary>Credits a net word delta to today's journal entry, refreshes
        /// the journal view when visible, and fires the goal fanfare when the
        /// daily target is crossed.</summary>
        private void AddJournalWords(int delta)
        {
            _project.Journal.Add(WritingJournal.Today(), delta);
            if (_journalView.Visibility == Visibility.Visible) _journalView.Refresh();
            if (delta > 0) CheckDailyGoal();
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
            AppSettings.InspectorVisible = !AppSettings.InspectorVisible;
            if (!AppSettings.InspectorVisible && _inspectorCol.Width.Value > 0)
                AppSettings.InspectorWidth = _inspectorCol.Width.Value;
            ApplyPanelVisibility();
            AppSettings.Save();
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

            // Le Journal perso vit sans inspecteur (pas de synopsis à montrer).
            var inspectorOn = AppSettings.InspectorVisible && !_journalOpen && !_calmMode;
            _inspector.Visibility = inspectorOn ? Visibility.Visible : Visibility.Collapsed;
            _inspectorSplit.Visibility = _inspector.Visibility;
            _inspectorCol.Width = inspectorOn ? new GridLength(AppSettings.InspectorWidth) : new GridLength(0);
            _inspectorMenu.IsChecked = inspectorOn;
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
        }

        private void OpenPreferences()
        {
            var dialog = new PreferencesDialog(this);
            dialog.AppearanceChanged += ApplyAppearance;
            dialog.ShowDialog();
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
                        MessageBox.Show(this, "Ce fichier n'existe plus :\n" + path,
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
                               : "Écrit";
                _synopsisBox.Text = _current.Synopsis ?? "";
                _synopsisBox.IsEnabled = !_current.IsCategory;
                _notesBox.Text = _current.Notes ?? "";
                _notesBox.IsEnabled = !_current.IsCategory;
                // Sheets have no synopsis (their cards show notes only); a page
                // gabarit has neither; notes exist on texts and sheets.
                SetInspectorFieldVisibility(
                    _current.Kind != ItemKind.Sheet && _current.Kind != ItemKind.PageTemplate,
                    _current.Kind == ItemKind.Text || _current.Kind == ItemKind.Sheet);

                // État (textes seulement) + couleur de carte (documents,
                // fiches, médias — et dossiers, dont les boîtes de livre).
                var showStatus = _current.Kind == ItemKind.Text;
                var showColor = _current.Kind == ItemKind.Text
                    || _current.Kind == ItemKind.Sheet
                    || _current.Kind == ItemKind.Media
                    || _current.Kind == ItemKind.Folder;
                _statusSection.Visibility = showStatus || showColor
                    ? Visibility.Visible : Visibility.Collapsed;
                _statusLabel.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
                _statusCombo.Visibility = _statusLabel.Visibility;
                _colorLabel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
                _colorSwatches.Visibility = _colorLabel.Visibility;
                if (showStatus)
                {
                    var index = Array.IndexOf(TextStatus.Keys, _current.Status);
                    _statusCombo.SelectedIndex = index < 0 ? 0 : index + 1;
                }
                if (showColor) RebuildColorSwatches();
            }
            _loadingInspector = false;

            UpdateLinksPanel();

            _inspDates.Text = string.IsNullOrEmpty(_project.CreatedAt) ? ""
                : "Créé le " + _project.CreatedAt + "\nModifié le " + _project.ModifiedAt;
        }

        /// <summary>Reconstruit la rangée de pastilles de couleur pour
        /// l'élément courant (coche = couleur active).</summary>
        private void RebuildColorSwatches()
        {
            _colorSwatches.Children.Clear();
            if (_current == null) return;
            foreach (var swatch in View.ItemIcons.TintSwatches)
            {
                var value = swatch;
                var active = _current.CardColor == value;
                var chip = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(0, 0, 5, 4),
                    Background = value == null
                        ? Brushes.Transparent
                        : new SolidColorBrush(View.FlowConverter.ParseColor(value)),
                    BorderBrush = active ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(active ? 2.2 : 1),
                    ToolTip = value == null ? "Aucune couleur" : value,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                chip.MouseLeftButtonUp += delegate
                {
                    if (_current == null) return;
                    _current.CardColor = value;
                    MarkDirty();
                    RebuildColorSwatches();
                    RefreshOpenCorkboards();
                };
                _colorSwatches.Children.Add(chip);
            }
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

            // Outgoing: parse [[...]] in this item's own text.
            var outgoing = new List<string>();
            var text = _current.SearchText();
            var cursor = 0;
            while (true)
            {
                var open = text.IndexOf("[[", cursor, StringComparison.Ordinal);
                var close = open < 0 ? -1 : text.IndexOf("]]", open + 2, StringComparison.Ordinal);
                if (open < 0 || close < 0) break;
                var title = text.Substring(open + 2, close - open - 2).Trim();
                if (title.Length > 0 && title.Length < 120 && !outgoing.Contains(title))
                    outgoing.Add(title);
                cursor = close + 2;
            }
            foreach (var title in outgoing)
                _linksPanel.Children.Add(LinkRow("→ " + title, title, _project.FindByTitle(title) != null));

            // Incoming: items whose text contains [[this title]].
            var marker = "[[" + _current.Title + "]]";
            foreach (var item in _project.AllItems())
            {
                if (item == _current || item.IsCategory) continue;
                if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) continue;
                if (item.SearchText().IndexOf(marker, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _linksPanel.Children.Add(LinkRow("← " + item.Title, item.Title, true));
            }

            if (_linksPanel.Children.Count == 0)
                _linksPanel.Children.Add(new TextBlock
                {
                    Text = "Aucun lien",
                    Foreground = Chrome.SoftText,
                    FontSize = 12
                });
        }

        private UIElement LinkRow(string label, string targetTitle, bool resolved)
        {
            var row = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = resolved ? (System.Windows.Media.Brush)Chrome.Accent : Chrome.SoftText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 1),
                ToolTip = resolved ? "Ouvrir" : "Cible inexistante (Ctrl+clic dans le texte pour la créer)"
            };
            if (resolved)
            {
                row.Cursor = System.Windows.Input.Cursors.Hand;
                row.MouseLeftButtonDown += delegate { NavigateToTitle(targetTitle); };
            }
            return row;
        }

        private void UpdateStats()
        {
            if (_current != null && _current.Kind == ItemKind.Text && _editor.HasItem)
            {
                var stats = TextStats.Compute(_editor.PlainText());
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
                var stats = TextStats.Compute(_sheetView.BodyPlainText());
                _statusRight.Text = stats.ShortLabel();
                _inspStats.Text = stats.LongLabel();
            }
            else if (_current != null && _current.Kind == ItemKind.Book)
            {
                _statusRight.Text = "";
                _inspStats.Text = BookStatsLabel(_current);
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
        /// textes, répartition par état, mots et signes — les liminaires et la
        /// table des matières restent hors du compte.</summary>
        private string BookStatsLabel(BinderItem book)
        {
            var all = new List<BinderItem>();
            CollectBookTexts(book, all);
            var texts = new List<BinderItem>();
            foreach (var text in all)
                if (!text.IsExtraPage && !text.IsToc) texts.Add(text);
            var words = 0;
            var signs = 0;
            foreach (var text in texts)
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
                  + "\nSignes : " + signs.ToString("N0", culture);
            return label;
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                AppName + " " + AppVersion + "\n\n" +
                "Traitement de texte et construction narrative.\n" +
                "Alpha : éditeur riche paginé, fiches wiki, corkboard, échanges\n" +
                "docx/odt/RTF/Markdown/Scrivener, aperçu des pages et impression.",
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
