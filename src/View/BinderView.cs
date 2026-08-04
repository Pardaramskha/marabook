using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The Binder ("la Pile"): the left-hand tree of the project.
    /// Owns tree construction, context menus and drag &amp; drop; every mutation
    /// goes through the shared HistoryManager so it is undoable. The tree is
    /// rebuilt after each change, preserving expansion and selection by id.</summary>
    public class BinderView : Border
    {
        private readonly TreeView _tree;
        private Project _project;
        private HistoryManager _history;
        private bool _subscribed;
        private readonly Dictionary<string, TreeViewItem> _nodesById = new Dictionary<string, TreeViewItem>();
        private readonly HashSet<string> _expandedIds = new HashSet<string>();
        private string _selectedId;
        private bool _rebuilding;

        // Drag & drop state
        private BinderItem _dragCandidate;
        private Point _dragStart;

        public event Action<BinderItem> SelectionChanged;
        public event Action StructureChanged; // a user-initiated, undoable change happened

        private TextBox _searchBox;
        private ComboBox _searchFilter;
        private ListBox _results;

        public BinderView()
        {
            Background = Chrome.BarBgLight;
            BorderBrush = Chrome.Border;
            BorderThickness = new Thickness(0, 0, 1, 0);

            _tree = new TreeView { AllowDrop = true };
            _tree.SelectedItemChanged += OnSelectedItemChanged;
            _tree.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            _tree.PreviewMouseMove += OnPreviewMouseMove;
            _tree.DragOver += OnDragOver;
            _tree.Drop += OnDrop;

            var layout = new DockPanel();
            layout.Children.Add(BuildSearchBar());

            _results = new ListBox
            {
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            _results.SelectionChanged += OnResultChosen;

            var host = new Grid();
            host.Children.Add(_tree);
            host.Children.Add(_results);
            layout.Children.Add(host);
            Child = layout;
        }

        private UIElement BuildSearchBar()
        {
            var bar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 6, 6, 6)
            };
            DockPanel.SetDock(bar, Dock.Top);
            var row = new DockPanel();

            _searchFilter = new ComboBox { Width = 74, Margin = new Thickness(4, 0, 0, 0) };
            _searchFilter.Items.Add("Tout");
            _searchFilter.Items.Add("Écrits");
            _searchFilter.Items.Add("Fiches");
            _searchFilter.Items.Add("Médias");
            _searchFilter.SelectedIndex = 0;
            _searchFilter.SelectionChanged += delegate { RunSearch(); };
            DockPanel.SetDock(_searchFilter, Dock.Right);
            row.Children.Add(_searchFilter);

            _searchBox = new TextBox { ToolTip = "Recherche dans tout le projet (titres, textes, fiches, synopsis)" };
            _searchBox.TextChanged += delegate { RunSearch(); };
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { _searchBox.Text = ""; e.Handled = true; }
            };
            row.Children.Add(_searchBox);
            bar.Child = row;
            return bar;
        }

        public void FocusSearch()
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public BinderItem SelectedItem
        {
            get { return _selectedId == null || _project == null ? null : _project.FindById(_selectedId); }
        }

        public void LoadProject(Project project, HistoryManager history)
        {
            _project = project;
            _history = history;
            if (!_subscribed)
            {
                _history.Changed += Rebuild;
                _subscribed = true;
            }
            _expandedIds.Clear();
            foreach (var root in project.Roots) _expandedIds.Add(root.Id); // categories start open
            _selectedId = null;
            if (_searchBox != null) _searchBox.Text = ""; // also restores the tree
            Rebuild();
        }

        // ------------------------------------------------------- tree construction

        public void Rebuild()
        {
            if (_project == null) return;
            _rebuilding = true;
            try
            {
                _tree.Items.Clear();
                _nodesById.Clear();
                foreach (var root in _project.Roots)
                    _tree.Items.Add(BuildNode(root));

                TreeViewItem selected;
                if (_selectedId != null && _nodesById.TryGetValue(_selectedId, out selected))
                    selected.IsSelected = true;
                else
                    _selectedId = null;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private TreeViewItem BuildNode(BinderItem item)
        {
            var node = new TreeViewItem
            {
                Tag = item,
                Header = BuildHeader(item),
                IsExpanded = _expandedIds.Contains(item.Id)
            };
            node.Expanded += OnNodeExpandedChanged;
            node.Collapsed += OnNodeExpandedChanged;
            node.ContextMenu = BuildContextMenu(item);
            foreach (var child in item.Children)
                node.Items.Add(BuildNode(child));
            _nodesById[item.Id] = node;
            return node;
        }

        private UIElement BuildHeader(BinderItem item)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock
            {
                Text = Glyph(item),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = item.IsCategory ? (Brush)Chrome.Accent : Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            panel.Children.Add(icon);

            var title = new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (item.IsCategory)
            {
                title.FontWeight = FontWeights.SemiBold;
                title.Foreground = Chrome.SoftText;
            }
            panel.Children.Add(title);
            return panel;
        }

        private static string Glyph(BinderItem item)
        {
            if (item.IsCategory)
            {
                if (item.CategoryKey == Project.KeyWritings) return "\uE70F"; // pencil
                if (item.CategoryKey == Project.KeyResearch) return "\uE721"; // search
                if (item.CategoryKey == Project.KeySheets) return "\uE716"; // people
                if (item.CategoryKey == Project.KeyTrash) return "\uE74D"; // trash can
            }
            if (item.Kind == ItemKind.Folder) return "\uE8B7"; // folder
            if (item.Kind == ItemKind.Sheet) return "\uE77B";  // contact card
            if (item.Kind == ItemKind.Media)
                return MediaView.IsImage(item.MediaExtension) ? "\uE722" : "\uE723"; // camera / attach
            return "\uE7C3"; // page
        }

        private void OnNodeExpandedChanged(object sender, RoutedEventArgs e)
        {
            var node = e.OriginalSource as TreeViewItem;
            if (node == null) return;
            var item = node.Tag as BinderItem;
            if (item == null) return;
            if (node.IsExpanded) _expandedIds.Add(item.Id);
            else _expandedIds.Remove(item.Id);
            e.Handled = true;
        }

        private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_rebuilding) return;
            var node = _tree.SelectedItem as TreeViewItem;
            _selectedId = node == null ? null : ((BinderItem)node.Tag).Id;
            var handler = SelectionChanged;
            if (handler != null) handler(node == null ? null : (BinderItem)node.Tag);
        }

        public void SelectItem(string id)
        {
            _selectedId = id;
            TreeViewItem node;
            if (id != null && _nodesById.TryGetValue(id, out node))
            {
                // Open the path down to the item so the selection is visible.
                var parent = node.Parent as TreeViewItem;
                while (parent != null)
                {
                    parent.IsExpanded = true;
                    parent = parent.Parent as TreeViewItem;
                }
                node.IsSelected = true;
                node.BringIntoView();
            }
        }

        // ------------------------------------------------------- context menus

        private ContextMenu BuildContextMenu(BinderItem item)
        {
            var menu = new ContextMenu();
            var inTrash = item.RootCategory().CategoryKey == Project.KeyTrash;

            if (inTrash)
            {
                if (!item.IsCategory)
                    AddMenu(menu, "Restaurer dans Écrits", delegate { Restore(item); });
                AddMenu(menu, "Vider la corbeille", delegate { EmptyTrash(); });
                return menu;
            }

            if (item.CanHaveChildren)
            {
                var rootKey = item.RootCategory().CategoryKey;
                // Every container accepts everything; the order just puts the
                // category's native content first.
                if (rootKey == Project.KeySheets)
                {
                    AddMenu(menu, "Nouvelle fiche", delegate { NewSheet(item); });
                    AddMenu(menu, "Nouvel écrit", delegate { NewText(item); });
                }
                else if (rootKey == Project.KeyResearch)
                {
                    AddMenu(menu, "Importer des fichiers…", delegate { ImportMediaDialog(item); });
                    AddMenu(menu, "Nouvel écrit", delegate { NewText(item); });
                    AddMenu(menu, "Nouvelle fiche", delegate { NewSheet(item); });
                }
                else
                {
                    AddMenu(menu, "Nouvel écrit", delegate { NewText(item); });
                    AddMenu(menu, "Nouvelle fiche", delegate { NewSheet(item); });
                }
                AddMenu(menu, "Nouveau dossier", delegate { NewFolder(item); });
            }
            if (!item.IsCategory)
            {
                menu.Items.Add(new Separator());
                AddMenu(menu, "Renommer…", delegate { Rename(item); });
                AddMenu(menu, "Supprimer", delegate { Delete(item); });
            }
            return menu;
        }

        private static void AddMenu(ContextMenu menu, string label, RoutedEventHandler onClick)
        {
            var entry = new MenuItem { Header = label };
            entry.Click += onClick;
            menu.Items.Add(entry);
        }

        // ------------------------------------------------------- operations

        /// <summary>The parent that receives a new item, given the current selection.</summary>
        private BinderItem TargetParent()
        {
            var selected = SelectedItem;
            if (selected == null || selected.RootCategory().CategoryKey == Project.KeyTrash)
                return _project.Category(Project.KeyWritings);
            return selected.CanHaveChildren ? selected : selected.Parent;
        }

        public void NewText(BinderItem parent)
        {
            if (parent == null) parent = TargetParent();
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouvel écrit", "Titre de l'écrit :", "Nouvel écrit");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.Text, Title = title };
            RunAndSelect(new AddItemAction(parent, item, -1), item.Id, parent.Id);
        }

        public void NewFolder(BinderItem parent)
        {
            if (parent == null) parent = TargetParent();
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouveau dossier", "Nom du dossier :", "Nouveau dossier");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.Folder, Title = title };
            RunAndSelect(new AddItemAction(parent, item, -1), item.Id, parent.Id);
        }

        public void Rename(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;
            var title = InputDialog.Ask(Window.GetWindow(this), "Renommer", "Nouveau titre :", item.Title);
            if (title == null || title == item.Title) return;
            RunAndSelect(new RenameItemAction(item, title), item.Id, null);
        }

        public void Delete(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;
            if (item.RootCategory().CategoryKey == Project.KeyTrash) return; // already in trash
            RunAndSelect(new DeleteToTrashAction(_project.Trash, item), null, _project.Trash.Id);
        }

        public void EmptyTrash()
        {
            if (_project.Trash.Children.Count == 0) return;
            var answer = MessageBox.Show(Window.GetWindow(this),
                "Vider définitivement la corbeille ?", "Univers Sale",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            RunAndSelect(new EmptyTrashAction(_project.Trash), null, null);
        }

        private void Restore(BinderItem item)
        {
            var writings = _project.Category(Project.KeyWritings);
            RunAndSelect(new MoveItemAction(item, writings, -1), item.Id, writings.Id);
        }

        private void RunAndSelect(IUndoableAction action, string selectId, string expandId)
        {
            if (expandId != null) _expandedIds.Add(expandId);
            if (selectId != null) _selectedId = selectId;
            _history.Run(action); // Changed fires -> Rebuild restores expansion + selection
            var handler = StructureChanged;
            if (handler != null) handler();
            if (selectId != null)
            {
                var chosen = SelectionChanged;
                if (chosen != null) chosen(SelectedItem);
            }
        }

        // ------------------------------------------------------- drag & drop

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var node = NodeFromSource(e.OriginalSource);
            _dragCandidate = node == null ? null : node.Tag as BinderItem;
            if (_dragCandidate != null && _dragCandidate.IsCategory) _dragCandidate = null;
            _dragStart = e.GetPosition(_tree);
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(_tree);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(_tree, new DataObject("UniversSaleItem", dragged.Id), DragDropEffects.Move);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy; // Explorer files -> media import
                e.Handled = true;
                return;
            }
            e.Effects = DropTarget(e) == null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    var node = NodeFromSource(e.OriginalSource);
                    var under = node == null ? null : node.Tag as BinderItem;
                    var parent = under == null ? null
                               : under.CanHaveChildren ? under : under.Parent;
                    ImportMediaFiles(parent, files);
                }
                e.Handled = true;
                return;
            }
            var target = DropTarget(e);
            if (target == null) return;
            var dragged = _project.FindById((string)e.Data.GetData("UniversSaleItem"));
            if (dragged == null) return;

            BinderItem newParent;
            int newIndex;
            if (target.CanHaveChildren)
            {
                newParent = target;
                newIndex = -1;
            }
            else
            {
                // Dropping on a text item inserts right after it: cheap sibling ordering.
                newParent = target.Parent;
                newIndex = newParent.Children.IndexOf(target) + 1;
                var oldIndex = dragged.Parent == newParent ? newParent.Children.IndexOf(dragged) : -1;
                if (oldIndex >= 0 && oldIndex < newIndex) newIndex--;
            }
            RunAndSelect(new MoveItemAction(dragged, newParent, newIndex), dragged.Id, newParent.Id);
            e.Handled = true;
        }

        /// <summary>The valid drop target under the cursor, or null.</summary>
        private BinderItem DropTarget(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("UniversSaleItem")) return null;
            var dragged = _project.FindById((string)e.Data.GetData("UniversSaleItem"));
            var node = NodeFromSource(e.OriginalSource);
            var target = node == null ? null : node.Tag as BinderItem;
            if (dragged == null || target == null || dragged == target) return null;
            if (target == dragged.Parent && target.CanHaveChildren) return null; // no-op move
            if (target.IsDescendantOf(dragged)) return null;
            if (target.RootCategory().CategoryKey == Project.KeyTrash) return null; // deletion has its own path
            if (!target.CanHaveChildren && target.Parent == null) return null;
            return target;
        }

        // ------------------------------------------------------- project search

        /// <summary>Accent- and case-insensitive contains, for French comfort.</summary>
        private static bool ContainsLoose(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return false;
            return System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                haystack, needle,
                System.Globalization.CompareOptions.IgnoreCase
                | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
        }

        private void RunSearch()
        {
            if (_project == null || _results == null) return;
            var query = _searchBox.Text.Trim();
            if (query.Length == 0)
            {
                _results.Visibility = Visibility.Collapsed;
                _tree.Visibility = Visibility.Visible;
                return;
            }
            _tree.Visibility = Visibility.Collapsed;
            _results.Visibility = Visibility.Visible;
            _results.Items.Clear();

            var filter = _searchFilter.SelectedIndex;
            foreach (var item in _project.AllItems())
            {
                if (item.IsCategory) continue;
                if (filter == 1 && item.Kind != ItemKind.Text) continue;
                if (filter == 2 && item.Kind != ItemKind.Sheet) continue;
                if (filter == 3 && item.Kind != ItemKind.Media) continue;
                if (!ContainsLoose(item.SearchText(), query)) continue;
                _results.Items.Add(BuildResultRow(item));
                if (_results.Items.Count >= 200) break;
            }
            if (_results.Items.Count == 0)
                _results.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock { Text = "Aucun résultat", Foreground = Chrome.SoftText },
                    IsEnabled = false
                });
        }

        private ListBoxItem BuildResultRow(BinderItem item)
        {
            var panel = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = Glyph(item),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(titleRow);

            var path = "";
            var parent = item.Parent;
            while (parent != null)
            {
                path = parent.Title + (path.Length > 0 ? " › " + path : "");
                parent = parent.Parent;
            }
            panel.Children.Add(new TextBlock
            {
                Text = path,
                Foreground = Chrome.SoftText,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return new ListBoxItem { Content = panel, Tag = item.Id };
        }

        private void OnResultChosen(object sender, SelectionChangedEventArgs e)
        {
            var entry = _results.SelectedItem as ListBoxItem;
            if (entry == null || entry.Tag == null) return;
            _selectedId = (string)entry.Tag;
            var handler = SelectionChanged;
            if (handler != null) handler(SelectedItem);
        }

        // ------------------------------------------------------- item creation & import

        /// <summary>The container new/imported items should land in, given the
        /// current selection (never the trash).</summary>
        public BinderItem CurrentContainer()
        {
            var selected = SelectedItem;
            if (selected == null || selected.RootCategory().CategoryKey == Project.KeyTrash)
                return _project.Category(Project.KeyWritings);
            return selected.CanHaveChildren ? selected : selected.Parent;
        }

        public void NewSheet(BinderItem parent)
        {
            if (parent == null)
            {
                var selected = SelectedItem;
                parent = selected != null && selected.CanHaveChildren
                    && selected.RootCategory().CategoryKey != Project.KeyTrash
                    ? selected : _project.Category(Project.KeySheets);
            }
            string title, templateId;
            if (!NewSheetDialog.Ask(Window.GetWindow(this), _project.Templates, out title, out templateId))
                return;
            var item = new BinderItem { Kind = ItemKind.Sheet, Title = title, TemplateId = templateId };
            RunAndSelect(new AddItemAction(parent, item, -1), item.Id, parent.Id);
        }

        public void ImportMediaDialog(BinderItem parent)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Tous les fichiers (*.*)|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            ImportMediaFiles(parent, dialog.FileNames);
        }

        /// <summary>Imports files as media cards. Default destination: the
        /// Recherche category.</summary>
        public void ImportMediaFiles(BinderItem parent, string[] paths)
        {
            if (parent == null || !parent.CanHaveChildren
                || parent.RootCategory().CategoryKey == Project.KeyTrash)
                parent = _project.Category(Project.KeyResearch);

            var items = new List<BinderItem>();
            var errors = new List<string>();
            foreach (var path in paths)
            {
                try
                {
                    var info = new System.IO.FileInfo(path);
                    if (info.Length > 200 * 1024 * 1024)
                    {
                        errors.Add(info.Name + " (plus de 200 Mo)");
                        continue;
                    }
                    items.Add(new BinderItem
                    {
                        Kind = ItemKind.Media,
                        Title = System.IO.Path.GetFileNameWithoutExtension(path),
                        MediaExtension = System.IO.Path.GetExtension(path),
                        MediaBytes = System.IO.File.ReadAllBytes(path)
                    });
                }
                catch (Exception error)
                {
                    errors.Add(System.IO.Path.GetFileName(path) + " (" + error.Message + ")");
                }
            }
            if (items.Count > 0)
                RunAndSelect(new AddItemsAction(parent, items),
                    items[items.Count - 1].Id, parent.Id);
            if (errors.Count > 0)
                MessageBox.Show(Window.GetWindow(this),
                    "Fichiers non importés :\n" + string.Join("\n", errors.ToArray()),
                    "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static TreeViewItem NodeFromSource(object source)
        {
            var current = source as DependencyObject;
            while (current != null && !(current is TreeViewItem))
                current = current is Visual ? VisualTreeHelper.GetParent(current)
                                            : LogicalTreeHelper.GetParent(current);
            return current as TreeViewItem;
        }
    }
}
