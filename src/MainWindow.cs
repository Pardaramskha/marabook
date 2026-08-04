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
        public const string AppName = "Univers Sale";
        public const string AppVersion = "0.4.0-alpha";

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
        private MediaView _mediaView;
        private TextBlock _placeholder;
        private BinderItem _current;

        private Border _inspector;
        private TextBlock _inspTitle, _inspKind, _inspStats, _inspDates;
        private TextBox _synopsisBox;
        private StackPanel _linksPanel;
        private bool _loadingInspector;

        private TextBlock _statusLeft, _statusRight;
        private DispatcherTimer _statsTimer, _autosaveTimer;

        private MenuItem _undoMenu, _redoMenu, _darkMenu, _binderMenu, _inspectorMenu, _recentMenu;

        // Session goal: words written since the goal was set, project-wide.
        private readonly Dictionary<string, int> _wordCache = new Dictionary<string, int>();
        private int _sessionGoal, _sessionBaseWords;

        public MainWindow()
        {
            Title = AppName;
            Width = 1200;
            Height = 760;
            MinWidth = 800;
            MinHeight = 500;
            Background = Chrome.WindowBg;
            Foreground = Chrome.Ink;

            var root = new DockPanel();
            root.Children.Add(BuildMenuBar());
            root.Children.Add(BuildStatusBar());
            root.Children.Add(BuildContent());
            Content = root;

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
            var importMenu = new MenuItem { Header = "Importer" };
            importMenu.Items.Add(Entry("import-docs", "Des documents…", ImportDocuments));
            importMenu.Items.Add(Entry("import-scrivener", "Un projet Scrivener…", ImportScrivener));
            file.Items.Add(importMenu);
            var exportMenu = new MenuItem { Header = "Exporter" };
            exportMenu.Items.Add(Entry("export-item", "L'écrit sélectionné…", ExportCurrentItem));
            exportMenu.Items.Add(Entry("compile", "Compiler le manuscrit…", CompileManuscript));
            file.Items.Add(exportMenu);
            file.Items.Add(new Separator());
            file.Items.Add(Entry(null, "Quitter", Close));
            menu.Items.Add(file);

            // --- Édition ---
            var edit = new MenuItem { Header = "É_dition" };
            _undoMenu = Entry("undo", "Annuler (Pile)", DoUndo);
            _redoMenu = Entry("redo", "Rétablir (Pile)", DoRedo);
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
            menu.Items.Add(format);

            // --- Affichage ---
            var view = new MenuItem { Header = "_Affichage" };
            _binderMenu = Entry("toggle-binder", "Pile", ToggleBinder);
            _binderMenu.IsCheckable = true;
            _inspectorMenu = Entry("toggle-inspector", "Inspecteur", ToggleInspector);
            _inspectorMenu.IsCheckable = true;
            _darkMenu = Entry("dark-theme", "Thème sombre", ToggleDarkTheme);
            _darkMenu.IsCheckable = true;
            _darkMenu.IsChecked = AppSettings.DarkTheme;
            view.Items.Add(_binderMenu);
            view.Items.Add(_inspectorMenu);
            view.Items.Add(new Separator());
            view.Items.Add(_darkMenu);
            menu.Items.Add(view);

            // --- Aide ---
            var help = new MenuItem { Header = "Aid_e" };
            help.Items.Add(Entry(null, "À propos d'Univers Sale…", ShowAbout));
            menu.Items.Add(help);

            bar.Child = menu;
            return bar;
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
            _binder.StructureChanged += MarkDirty;
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
            center.Children.Add(_editor);

            _sheetView = new SheetView { Visibility = Visibility.Collapsed };
            _sheetView.Edited += OnEditorEdited;
            _sheetView.LinkClicked += NavigateToTitle;
            center.Children.Add(_sheetView);

            _corkboard = new CorkboardView { Visibility = Visibility.Collapsed };
            _corkboard.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _corkboard.Changed += delegate { MarkDirty(); UpdateInspector(); };
            center.Children.Add(_corkboard);

            _mediaView = new MediaView { Visibility = Visibility.Collapsed };
            center.Children.Add(_mediaView);

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

            panel.Children.Add(new TextBlock
            {
                Text = "Synopsis",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });

            _synopsisBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 110,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _synopsisBox.TextChanged += OnSynopsisChanged;
            panel.Children.Add(_synopsisBox);

            _inspStats = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_inspStats);

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
                Padding = new Thickness(10, 4, 10, 4)
            };
            DockPanel.SetDock(bar, Dock.Bottom);

            var dock = new DockPanel();
            _statusRight = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12 };
            DockPanel.SetDock(_statusRight, Dock.Right);
            dock.Children.Add(_statusRight);

            _statusLeft = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dock.Children.Add(_statusLeft);

            bar.Child = dock;
            return bar;
        }

        // ============================================================= project lifecycle

        private void LoadProject(Project project, string path)
        {
            _project = project;
            _path = path;
            _current = null;
            _dirty = false;
            _history.Clear();
            _wordCache.Clear();
            _sessionGoal = 0;
            _editor.SetStyleSheet(project.Styles);
            _editor.Clear();
            _sheetView.SetStyleSheet(project.Styles);
            _sheetView.Clear();
            _mediaView.Clear();
            _corkboard.Clear();
            _binder.LoadProject(project, _history);
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
                var project = PlotFile.Load(path);
                project.Name = Path.GetFileNameWithoutExtension(path);
                LoadProject(project, path);
                AppSettings.AddRecentFile(path);
                AppSettings.Save();
                UpdateRecentMenu();
            }
            catch (Exception error)
            {
                MessageBox.Show(this,
                    "Impossible d'ouvrir le projet :\n" + error.Message,
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
            try
            {
                _editor.Commit();
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
            if (_dirty && _path != null) DoSave();
        }

        /// <summary>True when it is safe to drop the current project.</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
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
        }

        private void OnBinderSelection(BinderItem item)
        {
            CommitActive();
            _current = item;
            ShowItem(item);
            UpdateInspector();
            UpdateStats();
        }

        private void ShowItem(BinderItem item)
        {
            _editor.Visibility = Visibility.Collapsed;
            _sheetView.Visibility = Visibility.Collapsed;
            _corkboard.Visibility = Visibility.Collapsed;
            _mediaView.Visibility = Visibility.Collapsed;
            _placeholder.Visibility = Visibility.Collapsed;

            if (item != null && item.Kind == ItemKind.Text)
            {
                _sheetView.Clear();
                _editor.LoadItem(item);
                _editor.Visibility = Visibility.Visible;
                _editor.FocusEditor();
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
            if (item != null && item.CanHaveChildren)
            {
                _editor.Clear();
                _sheetView.Clear();
                _corkboard.Load(item, _history);
                _corkboard.Visibility = Visibility.Visible;
                return;
            }
            _editor.Clear();
            _sheetView.Clear();
            _placeholder.Visibility = Visibility.Visible;
            _placeholder.Text = "Sélectionnez un élément dans la Pile,\nou créez un écrit (Ctrl+T).";
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
            _statsTimer.Stop();
            _statsTimer.Start();
        }

        private void OnSynopsisChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingInspector || _current == null || _current.IsCategory) return;
            _current.Synopsis = _synopsisBox.Text;
            MarkDirty();
        }

        private void DoUndo()
        {
            if (!_history.CanUndo) return;
            _history.Undo();
            AfterHistoryJump();
        }

        private void DoRedo()
        {
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
                if (ext == ".docx") Exchange.Docx.Export(document, _project.Styles, path);
                else if (ext == ".odt") Exchange.Odt.Export(document, _project.Styles, path);
                else if (ext == ".rtf") Exchange.Rtf.Export(document, _project.Styles, path);
                else if (ext == ".md")
                    File.WriteAllText(path, Exchange.MarkdownExchange.Export(document), new System.Text.UTF8Encoding(false));
                else
                    File.WriteAllText(path, document.ToPlainText(), new System.Text.UTF8Encoding(false));
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

        private void ApplyPanelVisibility()
        {
            var binderOn = AppSettings.BinderVisible;
            _binder.Visibility = binderOn ? Visibility.Visible : Visibility.Collapsed;
            _binderSplit.Visibility = _binder.Visibility;
            _binderCol.Width = binderOn ? new GridLength(AppSettings.BinderWidth) : new GridLength(0);
            _binderMenu.IsChecked = binderOn;

            var inspectorOn = AppSettings.InspectorVisible;
            _inspector.Visibility = inspectorOn ? Visibility.Visible : Visibility.Collapsed;
            _inspectorSplit.Visibility = _inspector.Visibility;
            _inspectorCol.Width = inspectorOn ? new GridLength(AppSettings.InspectorWidth) : new GridLength(0);
            _inspectorMenu.IsChecked = inspectorOn;
        }

        private void ToggleDarkTheme()
        {
            AppSettings.DarkTheme = !AppSettings.DarkTheme;
            _darkMenu.IsChecked = AppSettings.DarkTheme;
            Chrome.Toggle(AppSettings.DarkTheme);
            Theme.Switch(Application.Current, AppSettings.DarkTheme);
            AppSettings.Save();
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
                               : "Écrit";
                _synopsisBox.Text = _current.Synopsis ?? "";
                _synopsisBox.IsEnabled = !_current.IsCategory;
            }
            _loadingInspector = false;

            UpdateLinksPanel();

            _inspDates.Text = string.IsNullOrEmpty(_project.CreatedAt) ? ""
                : "Créé le " + _project.CreatedAt + "\nModifié le " + _project.ModifiedAt;
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
            else
            {
                _statusRight.Text = "";
                _inspStats.Text = "";
            }

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

        private void ShowAbout()
        {
            MessageBox.Show(this,
                AppName + " " + AppVersion + "\n\n" +
                "Traitement de texte et construction narrative.\n" +
                "Phase 3 — les échanges : docx, odt, RTF, Markdown, Scrivener, compilation.\n" +
                "Première version alpha.",
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
