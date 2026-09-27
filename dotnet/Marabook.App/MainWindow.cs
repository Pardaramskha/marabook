using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La coquille (P1) : barre de menus, Pile | centre | inspecteur |
    /// rail, barre d'état — la géométrie de la fenêtre WPF, sur les trois OS.
    /// Ouvre un vrai .plot (PlotFile du cœur) et montre sa Pile ; le centre,
    /// l'inspecteur et le rail se peupleront lot après lot (P2, P3). Sans
    /// projet, la fenêtre reste voilée sous l'écran d'accueil (WelcomeWindow).</summary>
    public class MainWindow : Window
    {
        private const double RailWidth = 44;

        private readonly Launch _launch;
        private Project _project;
        private string _projectPath;
        private bool _dirty;

        private DockPanel _shellRoot;
        private Border _welcomeVeil;
        private WelcomeWindow _welcome;
        private TreeView _binder;
        private Grid _center;
        private TextBlock _placeholder;
        private EditorView _editor; // P2 : l'éditeur composé et son ruban
        private double _zoom = 1.0;
        private TextBlock _inspectorTitle, _inspectorKind, _inspectorDetail;
        private TextBlock _statusLeft, _statusRight;
        private MenuItem _recentMenu;
        private StackPanel _railStack;
        private PreferencesDialog _preferences;

        // Exposé aux sondes.
        public Project Project { get { return _project; } }
        public TreeView Binder { get { return _binder; } }
        public WelcomeWindow Welcome { get { return _welcome; } }
        public string InspectorTitle { get { return _inspectorTitle.Text; } }
        public string InspectorKind { get { return _inspectorKind.Text; } }
        public string InspectorDetail { get { return _inspectorDetail.Text; } }
        public string StatusText { get { return _statusLeft.Text; } }
        public string StatusRightText { get { return _statusRight.Text ?? ""; } }
        public bool HasProjectPath { get { return _projectPath != null; } }

        public MainWindow(Launch launch)
        {
            _launch = launch ?? new Launch();
            Title = AppInfo.Name;
            Width = 1400;
            Height = 860;
            MinWidth = 800;
            MinHeight = 500;
            Background = Chrome.WindowBg;
            Foreground = Chrome.Ink;
            if (!_launch.Isolated) WindowState = WindowState.Maximized;
            try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Marabook/Assets/app.ico"))); }
            catch { }

            _shellRoot = new DockPanel();
            var menu = BuildMenuBar();
            DockPanel.SetDock(menu, Dock.Top);
            _shellRoot.Children.Add(menu);
            var status = BuildStatusBar();
            DockPanel.SetDock(status, Dock.Bottom);
            _shellRoot.Children.Add(status);
            _shellRoot.Children.Add(BuildContent());
            // Le voile de l'accueil : à l'ouverture sans projet, la fenêtre
            // reste vide — blanc cassé — sous la fenêtre d'accueil.
            _welcomeVeil = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF9, 0xF6)),
                IsVisible = false
            };
            var shell = new Grid();
            shell.Children.Add(_shellRoot);
            shell.Children.Add(_welcomeVeil);
            Content = shell;

            Opened += async delegate
            {
                if (_launch.PlotPath != null) await OpenProject(_launch.PlotPath);
                else if (_launch.Demo || _launch.Probe) await ShowProject(SampleProject(), null, new List<string>());
                if (_launch.Demo && !_launch.Probe) { await Task.Delay(200); SelectFirstText(); }
                else ShowWelcome();
                if (_launch.Probe) { await Probes.Run(this); Close(); return; }
                if (_launch.CapturePath != null) await CaptureAndQuit(_launch.CapturePath);
                else if (_launch.Lab) BuildLab().Show(this);
            };
            Closing += delegate
            {
                if (_welcome != null) _welcome.Release();
            };
        }

        // ============================================================ menus

        private sealed class DelegateCommand : ICommand
        {
            private readonly Action _run;
            public DelegateCommand(Action run) { _run = run; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) { return true; }
            public void Execute(object parameter) { _run(); }
        }

        private Menu BuildMenuBar()
        {
            var menu = new Menu { Background = Chrome.BarBg };
            var file = new MenuItem { Header = "_Fichier" };
            file.Items.Add(Entry("new", "Nouveau projet", delegate { var _ = NewProject(); }));
            file.Items.Add(Entry("open", "Ouvrir…", delegate { var _ = OpenProjectDialog(); }));
            _recentMenu = new MenuItem { Header = "Projets récents" };
            file.Items.Add(_recentMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry("save", "Enregistrer", delegate { var _ = SaveProject(false); }));
            file.Items.Add(Entry("save-as", "Enregistrer sous…", delegate { var _ = SaveProject(true); }));
            file.Items.Add(new Separator());
            file.Items.Add(Entry("preferences", "Préférences…", OpenPreferences));
            file.Items.Add(new Separator());
            file.Items.Add(Entry("print", "Imprimer…", PrintCurrent));
            file.Items.Add(new Separator());
            var importMenu = new MenuItem { Header = "Importer" };
            importMenu.Items.Add(Later("import-docs", "Des documents…", "les vues (lot P3)"));
            importMenu.Items.Add(Later("import-scrivener", "Un projet Scrivener…", "les vues (lot P3)"));
            file.Items.Add(importMenu);
            var exportMenu = new MenuItem { Header = "Exporter" };
            exportMenu.Items.Add(Later("export-item", "L'écrit sélectionné…", "les vues (lot P3)"));
            exportMenu.Items.Add(Later("compile", "Compiler les écrits…", "les vues (lot P3)"));
            exportMenu.Items.Add(Entry("export-pdf", "PDF prêt à imprimer…", ExportPdf));
            exportMenu.Items.Add(Later("export-epub", "EPUB du livre ou de l'écrit…", "les vues (lot P3)"));
            file.Items.Add(exportMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry("close-project", "Fermer le projet", CloseProject));
            file.Items.Add(Entry(null, "Quitter", delegate { Close(); }));
            menu.Items.Add(file);

            var edit = new MenuItem { Header = "É_dition" };
            edit.Items.Add(Entry("undo", "Annuler", delegate { if (_editor.IsVisible) _editor.TryUndo(); }));
            edit.Items.Add(Entry("redo", "Rétablir", delegate { if (_editor.IsVisible) _editor.TryRedo(); }));
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("find", "Rechercher dans l'écrit…", delegate { if (_editor.IsVisible) _editor.ShowSearch(); }));
            edit.Items.Add(Later("project-search", "Rechercher dans le projet…", "les vues (lot P3)"));
            edit.Items.Add(Later("versions-panel", "Versions de l'écrit…", "les vues (lot P3)"));
            edit.Items.Add(new Separator());
            edit.Items.Add(Entry("new-text", "Nouvel écrit", delegate { var _ = NewItem(ItemKind.Text); }));
            edit.Items.Add(Entry("new-sheet", "Nouvelle fiche", delegate { var _ = NewItem(ItemKind.Sheet); }));
            edit.Items.Add(Entry("new-folder", "Nouveau dossier", delegate { var _ = NewItem(ItemKind.Folder); }));
            edit.Items.Add(Entry("rename", "Renommer…", delegate { var _ = RenameSelected(); }));
            edit.Items.Add(Entry("delete", "Supprimer", delegate { var _ = DeleteSelected(); }));
            menu.Items.Add(edit);

            var format = new MenuItem { Header = "F_ormat" };
            format.Items.Add(Later("styles", "Gérer les styles…", "les vues (lot P3)"));
            format.Items.Add(Later("templates", "Modèles de fiches…", "les vues (lot P3)"));
            format.Items.Add(new Separator());
            format.Items.Add(Entry("insert-footnote", "Note de bas de page", delegate { if (_editor.IsVisible) _editor.InsertFootnote(); }));
            format.Items.Add(Later("insert-link", "Lien vers une fiche…", "l'éditeur (lot P2)"));
            format.Items.Add(Entry("insert-image", "Insérer une image…", delegate { if (_editor.IsVisible) _editor.InsertImage(); }));
            menu.Items.Add(format);

            var view = new MenuItem { Header = "_Affichage" };
            view.Items.Add(Entry("toggle-binder", "Pile", ToggleBinder));
            view.Items.Add(Entry("toggle-inspector", "Général", ToggleInspector));
            view.Items.Add(new Separator());
            view.Items.Add(Entry("dark-theme", "Thème sombre", ToggleDarkTheme));
            menu.Items.Add(view);

            var help = new MenuItem { Header = "Aid_e" };
            help.Items.Add(Later(null, "Vérifier les mises à jour…", "la livraison (lot P4)"));
            help.Items.Add(new Separator());
            help.Items.Add(Entry(null, "À propos de Marabook…", delegate { var _ = About(); }));
            menu.Items.Add(help);
            RebuildRecentMenu();
            return menu;
        }

        /// <summary>Une entrée de menu liée à une action de la table des
        /// raccourcis : le geste affiché et posé sur la fenêtre.</summary>
        private MenuItem Entry(string actionId, string header, Action handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += delegate { handler(); };
            if (actionId != null)
            {
                var gesture = AppSettings.Gesture(actionId);
                if (!string.IsNullOrEmpty(gesture))
                {
                    try
                    {
                        var parsed = KeyGesture.Parse(gesture);
                        item.InputGesture = parsed;
                        KeyBindings.Add(new KeyBinding { Gesture = parsed, Command = new DelegateCommand(handler) });
                    }
                    catch { }
                }
            }
            return item;
        }

        /// <summary>Une entrée qui attend son lot : grisée, l'infobulle dit lequel.</summary>
        private MenuItem Later(string actionId, string header, string when)
        {
            var item = new MenuItem { Header = header, IsEnabled = false };
            ToolTip.SetTip(item, "Arrive avec " + when);
            return item;
        }

        private void RebuildRecentMenu()
        {
            if (_recentMenu == null) return;
            _recentMenu.Items.Clear();
            foreach (var path in AppSettings.RecentFiles)
            {
                if (!File.Exists(path)) continue;
                var captured = path;
                var entry = new MenuItem { Header = Path.GetFileNameWithoutExtension(path) };
                ToolTip.SetTip(entry, path);
                entry.Click += delegate { var _ = OpenProject(captured); };
                _recentMenu.Items.Add(entry);
            }
            _recentMenu.IsEnabled = _recentMenu.Items.Count > 0;
        }

        // ============================================================ contenu

        private Border _binderHost, _inspectorHost;

        private Control BuildContent()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(AppSettings.BinderWidth, GridUnitType.Pixel) { MinWidth = 0 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 300 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(AppSettings.InspectorWidth, GridUnitType.Pixel) { MinWidth = 0 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(RailWidth, GridUnitType.Pixel));

            // La Pile
            _binderHost = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var binderDock = new DockPanel();
            var binderHeader = new TextBlock
            {
                Text = "Pile",
                Foreground = Chrome.FaintText,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(12, 10, 12, 6)
            };
            DockPanel.SetDock(binderHeader, Dock.Top);
            binderDock.Children.Add(binderHeader);
            _binder = new TreeView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(4, 0, 4, 4),
                ItemTemplate = new FuncTreeDataTemplate<BinderItem>(BinderRow, item => item.Children)
            };
            _binder.SelectionChanged += delegate { ShowInspector(_binder.SelectedItem as BinderItem); ShowCurrent(); };
            binderDock.Children.Add(_binder);
            _binderHost.Child = binderDock;
            Grid.SetColumn(_binderHost, 0);
            grid.Children.Add(_binderHost);

            var binderSplit = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, Background = Chrome.WindowBg };
            Grid.SetColumn(binderSplit, 1);
            grid.Children.Add(binderSplit);

            // Le centre
            _center = new Grid { Background = Chrome.WindowBg };
            _placeholder = new TextBlock
            {
                Text = "Sélectionnez un écrit dans la Pile,\nou créez-en un (Ctrl+T).",
                Foreground = Chrome.SoftText,
                FontSize = 15,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _center.Children.Add(_placeholder);
            _editor = new EditorView { IsVisible = false };
            _editor.Edited += delegate { _dirty = true; RefreshTitle(); };
            _editor.PageInfoChanged += delegate(int page, int count) { _statusRight.Text = "page " + page + " / " + count; };
            _editor.PdfRequested += ExportPdf;
            _editor.PrintRequested += PrintCurrent;
            _editor.PreviewRequested += PrintCurrent; // l'aperçu = le PDF dans la visionneuse (P2)
            _editor.ZoomStepRequested += delegate(int step) { _editor.SetZoom(Math.Max(0.5, Math.Min(2.0, _zoom + step / 100.0))); _zoom = Math.Max(0.5, Math.Min(2.0, _zoom + step / 100.0)); };
            _center.Children.Add(_editor);
            Grid.SetColumn(_center, 2);
            grid.Children.Add(_center);

            var inspectorSplit = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, Background = Chrome.WindowBg };
            Grid.SetColumn(inspectorSplit, 3);
            grid.Children.Add(inspectorSplit);

            _inspectorHost = BuildInspector();
            Grid.SetColumn(_inspectorHost, 4);
            grid.Children.Add(_inspectorHost);

            var rail = BuildRail();
            Grid.SetColumn(rail, 5);
            grid.Children.Add(rail);
            return grid;
        }

        /// <summary>Une ligne de la Pile : l'icône de la nature de l'élément,
        /// puis son titre.</summary>
        private Control BinderRow(BinderItem item, INameScope scope)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var brush = item.IsCategory ? Chrome.SoftText : Chrome.Ink;
            var glyph = Icons.Make(IconFor(item), 14, brush);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(glyph);
            row.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = item.IsCategory ? Chrome.SoftText : Chrome.Ink,
                FontWeight = item.IsCategory ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private static string IconFor(BinderItem item)
        {
            if (item.IsCategory)
            {
                switch (item.CategoryKey)
                {
                    case Project.KeyHome: return "apercu";
                    case Project.KeyWritings: return "ecrits";
                    case Project.KeyResearch: return "files-bold";
                    case Project.KeySheets: return "fiches-menu";
                    case Project.KeyPlans: return "blueprint-bold";
                    case Project.KeyMindMaps: return "connection";
                    case Project.KeyDictionary: return "book-open-text-bold";
                    case Project.KeyTrash: return "folder-bold";
                    default: return "folder-bold";
                }
            }
            switch (item.Kind)
            {
                case ItemKind.Folder: return "folder-bold";
                case ItemKind.Sheet: return "fiche-individual";
                case ItemKind.Media: return "image-square-bold";
                case ItemKind.Book: return "book-bold";
                case ItemKind.PageTemplate: return "article-bold";
                case ItemKind.Plan: return "blueprint-bold";
                case ItemKind.MindMap: return "connection";
                default: return "file-text-bold";
            }
        }

        private Border BuildInspector()
        {
            var panel = new StackPanel { Margin = new Thickness(14) };
            _inspectorTitle = new TextBlock { Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_inspectorTitle);
            _inspectorKind = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 2, 0, 12) };
            panel.Children.Add(_inspectorKind);
            _inspectorDetail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_inspectorDetail);
            return new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer { Content = panel }
            };
        }

        private Border BuildRail()
        {
            _railStack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _railStack.Children.Add(RailTab("Général", "fiche-individual", true));
            _railStack.Children.Add(RailTab("Recherche", "file-magnifying-glass", false));
            _railStack.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(9, 2, 9, 6) });
            _railStack.Children.Add(RailTab("Correction", "check-square-bold", false));
            _railStack.Children.Add(RailTab("Versions", "git-branch", false));
            return new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = _railStack
            };
        }

        private static Control RailTab(string label, string icon, bool active)
        {
            var tab = Buttons.IconToggle(icon, label, Buttons.Bar, Buttons.Look.Calm);
            tab.Width = Buttons.Bar;
            tab.Margin = new Thickness(6, 0, 6, 4);
            tab.IsChecked = active;
            return tab;
        }

        private Border BuildStatusBar()
        {
            var dock = new DockPanel();
            _statusRight = new TextBlock
            {
                Text = AppInfo.Version + " · " + AppPlatform.OsName + " · .NET " + Environment.Version.ToString(2) + " · Avalonia",
                Foreground = Chrome.FaintText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_statusRight, Dock.Right);
            dock.Children.Add(_statusRight);
            _statusLeft = new TextBlock { Text = "Aucun projet ouvert", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            dock.Children.Add(_statusLeft);
            return new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
                Child = dock
            };
        }

        // ============================================================ l'Accueil

        /// <summary>Sans projet : la coquille se voile et se désactive, la
        /// fenêtre d'accueil se pose dessus.</summary>
        private void ShowWelcome()
        {
            _welcomeVeil.IsVisible = true;
            _shellRoot.IsEnabled = false;
            if (_welcome != null) return;
            _welcome = new WelcomeWindow(this);
            _welcome.Closed += delegate { _welcome = null; };
            _welcome.Show(this);
        }

        private void HideWelcome()
        {
            _welcomeVeil.IsVisible = false;
            _shellRoot.IsEnabled = true;
            if (_welcome != null) _welcome.Release();
            _welcome = null;
        }

        /// <summary>Le sélecteur de fichier .plot (l'accueil et le menu).</summary>
        public async Task<string> AskProjectFile(Window owner)
        {
            var files = await (owner ?? this).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Ouvrir un projet Marabook",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { new FilePickerFileType("Projet Marabook") { Patterns = new[] { "*.plot" } } }
            });
            if (files == null || files.Count == 0) return null;
            return files[0].TryGetLocalPath();
        }

        /// <summary>Un projet neuf, enregistré tout de suite ; vrai s'il est ouvert.</summary>
        public async Task<bool> NewProjectWithSaveDialog(Window owner)
        {
            var file = await (owner ?? this).StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le nouveau projet",
                SuggestedFileName = "Nouveau roman",
                DefaultExtension = "plot",
                FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("Projet Marabook") { Patterns = new[] { "*.plot" } } }
            });
            if (file == null) return false;
            var path = file.TryGetLocalPath();
            if (path == null) return false;
            try
            {
                var project = Project.CreateNew();
                PlotFile.Save(project, path);
                await OpenProject(path);
                return _projectPath == path;
            }
            catch (Exception error)
            {
                await MessageDialog.Show(owner ?? this, "Le projet n'a pas pu être créé :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
        }

        /// <summary>La lecture du fichier en fond (l'accueil montre l'attente),
        /// puis le projet posé sur la coquille ; done(vrai) s'il est ouvert.</summary>
        public void OpenFileInBackground(string path, Action<bool> done)
        {
            Task.Run(delegate
            {
                var warnings = new List<string>();
                Project project = null;
                Exception failure = null;
                try { project = PlotFile.Load(path, warnings); }
                catch (Exception error) { failure = error; }
                Dispatcher.UIThread.Post(async delegate
                {
                    if (failure != null)
                    {
                        await MessageDialog.Show(_welcome != null ? (Window)_welcome : this, "Le projet n'a pas pu être ouvert :\n" + failure.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
                        done(false);
                        return;
                    }
                    await ShowProject(project, path, warnings);
                    done(_projectPath == path);
                });
            });
        }

        // ============================================================ projet

        private async Task OpenProjectDialog()
        {
            var path = await AskProjectFile(this);
            if (path != null) await OpenProject(path);
        }

        private async Task NewProject()
        {
            await NewProjectWithSaveDialog(this);
        }

        private async Task OpenProject(string path)
        {
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(path, warnings);
                await ShowProject(project, path, warnings);
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Le projet n'a pas pu être ouvert :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }

        /// <summary>Un projet d'exemple, en mémoire : ce que montre la fenêtre
        /// pour les captures et les sondes, sans toucher au disque.</summary>
        public static Project SampleProject()
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var book = new BinderItem { Title = "Le marabout et la mer", Kind = ItemKind.Book, Parent = writings };
            writings.Children.Add(book);
            var chapters = new[]
            {
                "Chapitre premier — Le vent des pins",
                "Chapitre deux — La maison aux volets",
                "Chapitre trois — L'odeur de la marée"
            };
            foreach (var title in chapters)
            {
                var text = new BinderItem { Title = title, Kind = ItemKind.Text, Parent = book };
                text.Document = TextDocument.FromPlainText(
                    "Le vent portait l'odeur des pins jusqu'au village, et personne ne songeait encore à fermer les volets.\n\n" +
                    "La mer, au loin, avait la couleur d'une ardoise mouillée.");
                book.Children.Add(text);
            }
            var research = project.Category(Project.KeyResearch);
            research.Children.Add(new BinderItem { Title = "Carte du littoral", Kind = ItemKind.Media, Parent = research });
            research.Children.Add(new BinderItem { Title = "Notes sur les marées", Kind = ItemKind.Text, Parent = research });
            var sheets = project.Category(Project.KeySheets);
            var folder = new BinderItem { Title = "Personnages", Kind = ItemKind.Folder, Parent = sheets };
            sheets.Children.Add(folder);
            folder.Children.Add(new BinderItem { Title = "Keira Varenh", Kind = ItemKind.Sheet, Parent = folder });
            folder.Children.Add(new BinderItem { Title = "Le marabout", Kind = ItemKind.Sheet, Parent = folder });
            var plans = project.Category(Project.KeyPlans);
            plans.Children.Add(new BinderItem { Title = "Plan en trois actes", Kind = ItemKind.Plan, Parent = plans });
            return project;
        }

        private async Task ShowProject(Project project, string path, List<string> warnings)
        {
            try
            {
                _project = project;
                _projectPath = path;
                _dirty = false;
                HideWelcome();
                _pageCountCache.Clear();
                _editor.SetStyleSheet(project.Styles);
                _editor.SetProject(project);
                _editor.ApplyPageSetup(project.Page);
                _binder.ItemsSource = null;
                _binder.ItemsSource = project.Roots;
                Dispatcher.UIThread.Post(delegate
                {
                    foreach (var root in project.Roots)
                    {
                        var container = _binder.TreeContainerFromItem(root) as TreeViewItem;
                        if (container != null) container.IsExpanded = true;
                    }
                }, DispatcherPriority.Loaded);
                if (path != null)
                {
                    AppSettings.RecentFiles.Remove(path);
                    AppSettings.RecentFiles.Insert(0, path);
                    while (AppSettings.RecentFiles.Count > 5) AppSettings.RecentFiles.RemoveAt(AppSettings.RecentFiles.Count - 1);
                    if (!_launch.Isolated) AppSettings.Save();
                    RebuildRecentMenu();
                }
                RefreshTitle();
                ShowInspector(null);
                if (warnings.Count > 0)
                    await MessageDialog.Show(this, string.Join("\n", warnings), "Réserves à l'ouverture", MessageButtons.OK, MessageIcon.Warning);
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Le projet n'a pas pu être affiché :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }

        private void RefreshTitle()
        {
            if (_project == null)
            {
                Title = AppInfo.Name;
                _statusLeft.Text = "Aucun projet ouvert";
                return;
            }
            var name = _projectPath != null ? Path.GetFileNameWithoutExtension(_projectPath) : "Projet d'exemple";
            Title = name + (_dirty ? " *" : "") + " — " + AppInfo.Name;
            var items = 0;
            foreach (var item in _project.AllItems()) items++;
            _statusLeft.Text = (_projectPath ?? "projet d'exemple, en mémoire") + "  ·  " + items + " éléments" + (_dirty ? "  ·  modifié" : "");
        }

        private void MarkDirty()
        {
            _dirty = true;
            RefreshTitle();
        }

        private async Task SaveProject(bool ask)
        {
            if (_project == null) return;
            var path = _projectPath;
            if (ask || path == null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Enregistrer le projet",
                    SuggestedFileName = path != null ? Path.GetFileName(path) : "Nouveau roman.plot",
                    DefaultExtension = "plot",
                    FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("Projet Marabook") { Patterns = new[] { "*.plot" } } }
                });
                if (file == null) return;
                path = file.TryGetLocalPath();
                if (path == null) return;
            }
            try
            {
                PlotFile.Save(_project, path);
                _projectPath = path;
                _dirty = false;
                RefreshTitle();
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Le projet n'a pas pu être enregistré :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }

        private void CloseProject()
        {
            _project = null;
            _projectPath = null;
            _dirty = false;
            _binder.ItemsSource = null;
            ShowCurrent();
            RefreshTitle();
            ShowInspector(null);
            ShowWelcome();
        }

        public void CloseProjectPublic() { CloseProject(); }

        // ------------------------------------------------------------ la Pile (P1 : le minimum)

        private BinderItem SelectedItem { get { return _binder.SelectedItem as BinderItem; } }

        /// <summary>Le centre suit la sélection : un écrit s'ouvre dans la
        /// surface composée, le reste laisse l'invite.</summary>
        private void ShowCurrent()
        {
            var item = _project == null ? null : SelectedItem;
            if (item != null && item.Kind == ItemKind.Text)
            {
                if (_editor.ShowsItem(item)) return;
                _editor.FolioOffset = ComputeFolioOffset(item);
                _editor.Decor = PageDecor.For(item, _project);
                _editor.LoadItem(item);
                _editor.IsVisible = true;
                _placeholder.IsVisible = false;
                _editor.FocusEditor();
            }
            else
            {
                if (_editor.HasItem) _editor.Clear();
                _editor.IsVisible = false;
                _placeholder.IsVisible = true;
            }
        }

        // ============================================================ PDF et impression (P2)

        /// <summary>Le document à mettre en pages : l'écrit ou la fiche
        /// sélectionnée (marques de [[liens]] retirées), ou un dossier / livre
        /// compilé. Null (et un message) sinon.</summary>
        private TextDocument BuildPrintable(out string name, out PageSetup setup)
        {
            name = null;
            setup = _project == null ? new PageSetup() : _project.Page;
            var current = SelectedItem;
            if (_project == null || current == null)
            {
                var _ = MessageDialog.Show(this, "Sélectionnez un écrit, une fiche ou un dossier à mettre en pages.",
                    AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
                return null;
            }
            if (current.Kind == ItemKind.Text || current.Kind == ItemKind.Sheet)
            {
                name = current.Title;
                if (current.Page != null) setup = current.Page;
                return Links.Strip(current.Document);
            }
            if (current.IsContainer)
            {
                name = current.Title;
                if (current.Kind == ItemKind.Book && current.Book != null) setup = current.Book.Template;
                return Links.Strip(Exchange.Compiler.Build(_project, current, new Exchange.CompileOptions
                {
                    TitlePage = false,
                    ChapterHeadings = false,
                    PageBreakPerText = true
                }));
            }
            var __ = MessageDialog.Show(this, "Sélectionnez un écrit, une fiche ou un dossier à mettre en pages.",
                AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
            return null;
        }

        private Print.Composition ComposeFor(TextDocument document, PageSetup setup, PageDecor decor, int folioOffset)
        {
            var current = SelectedItem;
            var composition = Print.Composer.Compose(document, _project.Styles.EffectiveFor(current), setup, _project, new AvaloniaFontEngine());
            composition.DefaultDecor = decor;
            composition.FolioOffset = folioOffset;
            return composition;
        }

        /// <summary>Fichier › Exporter › PDF prêt à imprimer : les options,
        /// puis le fichier.</summary>
        public async void ExportPdf()
        {
            string name;
            PageSetup setup;
            var document = BuildPrintable(out name, out setup);
            if (document == null) return;
            var current = SelectedItem;
            var decor = current != null && current.Kind == ItemKind.Text ? PageDecor.For(current, _project) : null;
            var offset = current != null && current.Kind == ItemKind.Text ? ComputeFolioOffset(current) : 0;
            var options = await PdfExportDialog.Ask(this, name, false, 0,
                delegate(Print.PdfExportOptions o) { return PreviewPdf(document, setup, name, o, decor, offset); });
            if (options == null) return;
            await WritePdf(document, setup, name, options, decor, offset);
        }

        /// <summary>Le BAT : le PDF exact dans un fichier temporaire, ouvert
        /// dans la visionneuse du système.</summary>
        private bool PreviewPdf(TextDocument document, PageSetup setup, string name,
            Print.PdfExportOptions options, PageDecor decor, int folioOffset)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "marabook-bat-" + SafeFileName(name) + ".pdf");
                Print.PdfWriter.Write(path, ComposeFor(document, setup, decor, folioOffset), options);
                AppPlatform.OpenWithShell(path);
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> WritePdf(TextDocument document, PageSetup setup, string name,
            Print.PdfExportOptions options, PageDecor decor, int folioOffset)
        {
            var path = await Ui.PickSaveFile(this, "PDF prêt à imprimer", "PDF (*.pdf)|*.pdf", SafeFileName(name) + ".pdf");
            if (path == null) return false;
            try
            {
                Print.PdfWriter.Write(path, ComposeFor(document, setup, decor, folioOffset), options);
                await MessageDialog.Show(this, "Export terminé :\n" + path, AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
                return true;
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Export PDF impossible :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
        }

        /// <summary>Fichier › Imprimer (P2, trois systèmes) : Marabook n'a pas
        /// de dialogue d'impression natif hors WPF — les pages composées
        /// partent en PDF dans la visionneuse du système, qui imprime.</summary>
        public void PrintCurrent()
        {
            string name;
            PageSetup setup;
            var document = BuildPrintable(out name, out setup);
            if (document == null) return;
            var current = SelectedItem;
            var decor = current != null && current.Kind == ItemKind.Text ? PageDecor.For(current, _project) : null;
            var offset = current != null && current.Kind == ItemKind.Text ? ComputeFolioOffset(current) : 0;
            if (!PreviewPdf(document, setup, name, new Print.PdfExportOptions(), decor, offset))
            {
                var _ = MessageDialog.Show(this, "Impression impossible : le PDF n'a pas pu être produit.", AppInfo.Name, MessageButtons.OK, MessageIcon.Warning);
            }
        }

        private static string SafeFileName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name ?? "")
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "document" : sb.ToString();
        }

        // ============================================================ folio de livre

        private readonly Dictionary<string, int> _pageCountCache = new Dictionary<string, int>();

        /// <summary>Les pages du livre qui précèdent cet écrit ; chaque écrit
        /// ouvre sur un recto (un compte impair reçoit un verso blanc).</summary>
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
                if (offset % 2 == 1) offset++;
            }
            return 0;
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
                    _project.Styles.EffectiveFor(text), text.Page ?? _project.Page, _project, new AvaloniaFontEngine());
                pages = Math.Max(1, composition.Pages.Count);
            }
            catch { pages = 1; }
            _pageCountCache[text.Id] = pages;
            return pages;
        }

        /// <summary>Exposés aux sondes.</summary>
        public EditorView Editor { get { return _editor; } }
        public ComposedView Composed { get { return _editor.Composed; } }

        /// <summary>La démo ouvre le premier écrit du projet (la capture
        /// montre la surface composée, pas l'invite).</summary>
        public void SelectFirstText()
        {
            if (_project == null) return;
            var first = FirstText(_project.Roots);
            if (first != null) _binder.SelectedItem = first;
        }

        private static BinderItem FirstText(IEnumerable<BinderItem> items)
        {
            foreach (var item in items)
            {
                if (item.Kind == ItemKind.Text && item.Document != null && item.Document.ToPlainText().Trim().Length > 0) return item;
                var inner = FirstText(item.Children);
                if (inner != null) return inner;
            }
            return null;
        }

        private async Task NewItem(ItemKind kind)
        {
            if (_project == null) return;
            var selected = SelectedItem;
            BinderItem parent;
            if (kind == ItemKind.Sheet)
                parent = selected != null && (selected.CategoryKey == Project.KeySheets || (selected.Kind == ItemKind.Folder && RootOf(selected).CategoryKey == Project.KeySheets))
                    ? selected : _project.Category(Project.KeySheets);
            else
                parent = selected != null && (selected.Kind == ItemKind.Folder || selected.Kind == ItemKind.Book || selected.CategoryKey == Project.KeyWritings)
                    ? selected : _project.Category(Project.KeyWritings);
            var label = kind == ItemKind.Sheet ? "Nouvelle fiche" : kind == ItemKind.Folder ? "Nouveau dossier" : "Nouvel écrit";
            var title = await InputDialog.Ask(this, label, "Titre :", label);
            if (title == null) return;
            var item = new BinderItem { Title = title, Kind = kind, Parent = parent };
            parent.Children.Add(item);
            RefreshBinder();
            _binder.SelectedItem = item;
            MarkDirty();
        }

        private static BinderItem RootOf(BinderItem item)
        {
            while (item.Parent != null) item = item.Parent;
            return item;
        }

        private async Task RenameSelected()
        {
            var item = SelectedItem;
            if (item == null || item.IsCategory) return;
            var title = await InputDialog.Ask(this, "Renommer", "Nouveau titre :", item.Title);
            if (title == null || title == item.Title) return;
            item.Title = title;
            RefreshBinder();
            _binder.SelectedItem = item;
            ShowInspector(item);
            MarkDirty();
        }

        private async Task DeleteSelected()
        {
            var item = SelectedItem;
            if (item == null || item.IsCategory || item.Parent == null || _project == null) return;
            var trash = _project.Trash;
            var inTrash = RootOf(item) == trash;
            if (inTrash)
            {
                var answer = await MessageDialog.Show(this, "Supprimer définitivement « " + item.Title + " » ?", "Corbeille", MessageButtons.YesNo, MessageIcon.Warning);
                if (answer != MessageResult.Yes) return;
                item.Parent.Children.Remove(item);
            }
            else
            {
                item.Parent.Children.Remove(item);
                item.Parent = trash;
                trash.Children.Add(item);
            }
            RefreshBinder();
            MarkDirty();
        }

        /// <summary>L'arbre se rebâtit sur la même liste (les racines).</summary>
        private void RefreshBinder()
        {
            if (_project == null) return;
            var roots = _project.Roots;
            _binder.ItemsSource = null;
            _binder.ItemsSource = roots;
            Dispatcher.UIThread.Post(delegate
            {
                foreach (var root in roots)
                {
                    var container = _binder.TreeContainerFromItem(root) as TreeViewItem;
                    if (container != null) container.IsExpanded = true;
                }
            }, DispatcherPriority.Loaded);
        }

        private void ShowInspector(BinderItem item)
        {
            if (item == null)
            {
                _inspectorTitle.Text = _project == null ? "" : _projectPath != null ? Path.GetFileNameWithoutExtension(_projectPath) : "Projet d'exemple";
                _inspectorKind.Text = _project == null ? "" : "Projet";
                _inspectorDetail.Text = _project == null ? "" : _projectPath ?? "";
                return;
            }
            _inspectorTitle.Text = item.Title;
            _inspectorKind.Text = KindLabel(item);
            var detail = "";
            if (item.Kind == ItemKind.Text && item.Document != null)
            {
                var words = Correction.TextStats.Compute(item.Document.ToPlainText());
                detail = words.Words + " mots · " + item.Document.Paragraphs.Count + " paragraphes";
            }
            else if (item.Children.Count > 0)
                detail = item.Children.Count + " éléments";
            _inspectorDetail.Text = detail;
        }

        private static string KindLabel(BinderItem item)
        {
            switch (item.Kind)
            {
                case ItemKind.Category: return "Racine de la Pile";
                case ItemKind.Folder: return "Dossier";
                case ItemKind.Text: return "Écrit";
                case ItemKind.Sheet: return "Fiche";
                case ItemKind.Media: return "Document de recherche";
                case ItemKind.Book: return "Livre";
                case ItemKind.PageTemplate: return "Gabarit de pages";
                case ItemKind.Plan: return "Plan";
                case ItemKind.MindMap: return "Carte mentale";
                default: return "";
            }
        }

        // ============================================================ affichage, préférences, à propos

        private void ToggleBinder()
        {
            _binderHost.IsVisible = !_binderHost.IsVisible;
        }

        private void ToggleInspector()
        {
            _inspectorHost.IsVisible = !_inspectorHost.IsVisible;
        }

        private void ToggleDarkTheme()
        {
            AppSettings.DarkTheme = !AppSettings.DarkTheme;
            if (!_launch.Isolated) AppSettings.Save();
            App.ApplyTheme(AppSettings.DarkTheme);
        }

        private void OpenPreferences()
        {
            if (_preferences != null) { _preferences.Activate(); return; }
            _preferences = new PreferencesDialog(this, _project);
            _preferences.AppearanceChanged += delegate { App.ApplyTheme(AppSettings.DarkTheme); };
            _preferences.Closed += delegate { _preferences = null; };
            _preferences.Show(this);
        }

        private async Task About()
        {
            await MessageDialog.Show(this,
                AppInfo.Name + " " + AppInfo.Version + "\n" +
                "Traitement de texte et construction narrative.\n\n" +
                AppPlatform.OsName + " · .NET " + Environment.Version.ToString(2) + " · Avalonia 11.3",
                "À propos de " + AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
        }

        /// <summary>La fenêtre de diagnostic du rendu (« --lab ») : le même
        /// texte dans des conteneurs différents, pour voir lequel l'abîme.</summary>
        private Window BuildLab()
        {
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "1. TextBlock nu : Boutons, sélections, liens" });
            panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = "2. Dans un ScrollViewer : Boutons, sélections, liens" }, Height = 30 });
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "Onglet", Content = new TextBlock { Text = "3. Dans un TabControl : Boutons, sélections, liens" } });
            panel.Children.Add(tabs);
            panel.Children.Add(new CheckBox { Content = "4. Libellé de case : Boutons, sélections, liens" });
            panel.Children.Add(new TextBlock { Text = "5. TextBlock Wrap : Boutons, sélections, liens", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "6. TextBlock FontSize 12 SoftText : Boutons, sélections, liens", FontSize = 12, Foreground = Chrome.SoftText });
            var stackInGrid = new Grid();
            stackInGrid.Children.Add(new TextBlock { Text = "7. Dans un Grid : Boutons, sélections, liens" });
            panel.Children.Add(stackInGrid);
            var dock = new DockPanel();
            dock.Children.Add(new TextBlock { Text = "8. Dans un DockPanel : Boutons, sélections, liens" });
            panel.Children.Add(dock);
            return new Window { Title = "Lab", Width = 700, Height = 420, Background = Chrome.RaisedBg, Content = panel };
        }

        /// <summary>La sonde visuelle : la fenêtre (ou l'accueil posé dessus)
        /// rendue en PNG une fois la mise en page posée, puis l'application quitte.</summary>
        private async Task CaptureAndQuit(string path)
        {
            if (_launch.Prefs)
            {
                OpenPreferences();
                await Task.Delay(300);
                if (_launch.PrefsTab >= 0 && _preferences != null && _launch.PrefsTab < _preferences.Tabs.Items.Count)
                {
                    _preferences.Tabs.SelectedIndex = _launch.PrefsTab;
                    await Task.Delay(400);
                }
            }
            Window lab = null;
            if (_launch.Lab) { lab = BuildLab(); lab.Show(this); await Task.Delay(300); }
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Render);
            try
            {
                Control content = lab != null ? (Control)lab.Content
                    : _preferences != null ? (Control)_preferences.Content
                    : _welcome != null ? (Control)_welcome.Content : (Control)Content;
                var scale = _launch.Scale > 0 ? _launch.Scale : 1;
                var size = new PixelSize(Math.Max(1, (int)(content.Bounds.Width * scale)), Math.Max(1, (int)(content.Bounds.Height * scale)));
                using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
                {
                    bitmap.Render(content);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    bitmap.Save(path);
                }
                Console.WriteLine("capture : " + path + " (" + size.Width + "×" + size.Height + ")");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("capture impossible : " + error);
            }
            Close();
        }
    }
}
