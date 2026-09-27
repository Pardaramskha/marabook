using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La coquille (P1) : barre de menus, Pile | centre | inspecteur |
    /// rail, barre d'état — la géométrie de la fenêtre WPF, sur les trois OS.
    /// Ouvre un vrai .plot (PlotFile du cœur) et montre sa Pile ; le centre,
    /// l'inspecteur et le rail se peupleront lot après lot (P2, P3). Sans
    /// projet, l'Accueil : titre, version, projets récents, Ouvrir.</summary>
    public class MainWindow : Window
    {
        private const double RailWidth = 44;

        private readonly Launch _launch;
        private Project _project;
        private string _projectPath;

        private TreeView _binder;
        private TextBlock _binderHeader;
        private Grid _center;
        private Control _welcome;
        private TextBlock _placeholder;
        private TextBlock _inspectorTitle, _inspectorKind, _inspectorDetail;
        private TextBlock _statusLeft, _statusRight;
        private MenuItem _recentMenu;
        private StackPanel _railStack;

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
            if (_launch.CapturePath == null) WindowState = WindowState.Maximized;
            try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Marabook/Assets/app.ico"))); }
            catch { }

            var root = new DockPanel();
            var menu = BuildMenuBar();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);
            var status = BuildStatusBar();
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);
            root.Children.Add(BuildContent());
            Content = root;

            Opened += async delegate
            {
                if (_launch.PlotPath != null) await OpenProject(_launch.PlotPath);
                else if (_launch.Demo) await ShowProject(SampleProject(), null, new List<string>());
                else ShowWelcome();
                if (_launch.CapturePath != null) await CaptureAndQuit(_launch.CapturePath);
            };
        }

        // ============================================================ menus

        private Menu BuildMenuBar()
        {
            var menu = new Menu { Background = Chrome.BarBg };
            var file = new MenuItem { Header = "_Fichier" };
            file.Items.Add(Entry("Nouveau projet…", "Ctrl+N", delegate { var _ = NewProject(); }));
            file.Items.Add(Entry("Ouvrir…", "Ctrl+O", delegate { var _ = OpenProjectDialog(); }));
            _recentMenu = new MenuItem { Header = "Projets récents" };
            file.Items.Add(_recentMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry("Fermer le projet", null, CloseProject));
            file.Items.Add(new Separator());
            file.Items.Add(Entry("Quitter", "Alt+F4", delegate { Close(); }));
            menu.Items.Add(file);

            var view = new MenuItem { Header = "_Affichage" };
            view.Items.Add(Entry("Basculer le thème sombre", null, delegate { SwitchTheme(!Chrome.Dark); }));
            menu.Items.Add(view);

            var help = new MenuItem { Header = "Aid_e" };
            help.Items.Add(Entry("À propos de Marabook", null, delegate { var _ = About(); }));
            menu.Items.Add(help);
            RebuildRecentMenu();
            return menu;
        }

        private static MenuItem Entry(string header, string gesture, Action handler)
        {
            var item = new MenuItem { Header = header };
            if (gesture != null) item.InputGesture = Avalonia.Input.KeyGesture.Parse(gesture);
            item.Click += delegate { handler(); };
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
            var binderHost = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var binderDock = new DockPanel();
            _binderHeader = new TextBlock
            {
                Text = "Pile",
                Foreground = Chrome.FaintText,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(12, 10, 12, 6)
            };
            DockPanel.SetDock(_binderHeader, Dock.Top);
            binderDock.Children.Add(_binderHeader);
            _binder = new TreeView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(4, 0, 4, 4),
                ItemTemplate = new FuncTreeDataTemplate<BinderItem>(BinderRow, item => item.Children)
            };
            _binder.SelectionChanged += delegate { ShowInspector(_binder.SelectedItem as BinderItem); };
            binderDock.Children.Add(_binder);
            binderHost.Child = binderDock;
            Grid.SetColumn(binderHost, 0);
            grid.Children.Add(binderHost);

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
            Grid.SetColumn(_center, 2);
            grid.Children.Add(_center);

            var inspectorSplit = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, Background = Chrome.WindowBg };
            Grid.SetColumn(inspectorSplit, 3);
            grid.Children.Add(inspectorSplit);

            // L'inspecteur (le Général)
            var inspector = BuildInspector();
            Grid.SetColumn(inspector, 4);
            grid.Children.Add(inspector);

            // Le rail
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
            _inspectorTitle = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_inspectorTitle);
            _inspectorKind = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 12)
            };
            panel.Children.Add(_inspectorKind);
            _inspectorDetail = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
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
            _statusLeft = new TextBlock
            {
                Text = "Aucun projet ouvert",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
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

        /// <summary>Sans projet : le titre, la version, les projets récents et
        /// les deux gestes — au centre, à la place de l'écrit.</summary>
        private void ShowWelcome()
        {
            if (_welcome != null) _center.Children.Remove(_welcome);
            var card = new StackPanel { MinWidth = 320, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            card.Children.Add(new TextBlock
            {
                Text = AppInfo.Name,
                FontSize = 34,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            card.Children.Add(new TextBlock
            {
                Text = "version " + AppInfo.Version + " · portage Avalonia, lot P1",
                FontSize = 12,
                Foreground = Chrome.FaintText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 22)
            });
            var recents = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var any = false;
            foreach (var path in AppSettings.RecentFiles)
            {
                if (!File.Exists(path)) continue;
                any = true;
                var captured = path;
                var tile = Buttons.IconText("book-bold", Path.GetFileNameWithoutExtension(path), path, Buttons.Bar, Buttons.Look.Outline);
                tile.HorizontalAlignment = HorizontalAlignment.Stretch;
                tile.HorizontalContentAlignment = HorizontalAlignment.Left;
                tile.Margin = new Thickness(0, 0, 0, 6);
                tile.Click += delegate { var _ = OpenProject(captured); };
                recents.Children.Add(tile);
            }
            if (any)
            {
                card.Children.Add(new TextBlock { Text = "Projets récents", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
                card.Children.Add(recents);
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var open = Buttons.IconText("folder-open", "Ouvrir un projet…", "Un fichier .plot", Buttons.Bar, Buttons.Look.Outline);
            open.Click += delegate { var _ = OpenProjectDialog(); };
            var create = Buttons.IconText("file-dashed-bold", "Nouveau projet", "Un projet neuf, enregistré tout de suite", Buttons.Bar, Buttons.Look.Primary);
            create.Margin = new Thickness(10, 0, 0, 0);
            create.Click += delegate { var _ = NewProject(); };
            row.Children.Add(open);
            row.Children.Add(create);
            card.Children.Add(row);
            _welcome = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(36, 32, 36, 32),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = card
            };
            _placeholder.IsVisible = false;
            _center.Children.Add(_welcome);
        }

        private void HideWelcome()
        {
            if (_welcome != null) { _center.Children.Remove(_welcome); _welcome = null; }
            _placeholder.IsVisible = true;
        }

        // ============================================================ projet

        private async Task OpenProjectDialog()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Ouvrir un projet Marabook",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Projet Marabook") { Patterns = new[] { "*.plot" } }
                }
            });
            if (files == null || files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path != null) await OpenProject(path);
        }

        private async Task NewProject()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le nouveau projet",
                SuggestedFileName = "Nouveau roman",
                DefaultExtension = "plot",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Projet Marabook") { Patterns = new[] { "*.plot" } }
                }
            });
            if (file == null) return;
            var path = file.TryGetLocalPath();
            if (path == null) return;
            try
            {
                var project = Project.CreateNew();
                PlotFile.Save(project, path);
                await OpenProject(path);
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Le projet n'a pas pu être créé :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
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
        private static Project SampleProject()
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
                HideWelcome();
                _binder.ItemsSource = project.Roots;
                // Les racines s'ouvrent une fois leurs conteneurs bâtis.
                Dispatcher.UIThread.Post(delegate
                {
                    foreach (var root in project.Roots)
                    {
                        var container = _binder.TreeContainerFromItem(root) as TreeViewItem;
                        if (container != null) container.IsExpanded = true;
                    }
                }, DispatcherPriority.Loaded);
                Title = (path != null ? Path.GetFileNameWithoutExtension(path) : "Projet d'exemple") + " — " + AppInfo.Name;
                var items = 0;
                foreach (var item in project.AllItems()) items++;
                _statusLeft.Text = (path ?? "projet d'exemple, en mémoire") + "  ·  " + items + " éléments";
                ShowInspector(null);
                if (warnings.Count > 0)
                    await MessageDialog.Show(this, string.Join("\n", warnings), "Réserves à l'ouverture", MessageButtons.OK, MessageIcon.Warning);
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Le projet n'a pas pu être affiché :\n" + error.Message, AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }

        private void CloseProject()
        {
            _project = null;
            _projectPath = null;
            _binder.ItemsSource = null;
            Title = AppInfo.Name;
            _statusLeft.Text = "Aucun projet ouvert";
            ShowInspector(null);
            ShowWelcome();
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

        // ============================================================ thème, à propos, capture

        private void SwitchTheme(bool dark)
        {
            Chrome.Toggle(dark);
            Application.Current.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        }

        private async Task About()
        {
            await MessageDialog.Show(this,
                AppInfo.Name + " " + AppInfo.Version + "\n" +
                "Traitement de texte et construction narrative.\n\n" +
                AppPlatform.OsName + " · .NET " + Environment.Version.ToString(2) + " · Avalonia 11.3",
                "À propos de " + AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
        }

        /// <summary>La sonde visuelle : la fenêtre rendue en PNG une fois la
        /// mise en page posée, puis l'application quitte.</summary>
        private async Task CaptureAndQuit(string path)
        {
            await Task.Delay(400);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Render);
            try
            {
                var content = (Control)Content;
                var size = new PixelSize(Math.Max(1, (int)content.Bounds.Width), Math.Max(1, (int)content.Bounds.Height));
                using (var bitmap = new RenderTargetBitmap(size, new Vector(96, 96)))
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
