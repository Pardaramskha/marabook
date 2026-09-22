using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.History;
using Marabook.Model;

namespace Marabook.View
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
        private Canvas _dropOverlay;   // indicateur d'insertion pendant le drag
        private Border _dropLine, _dropBox;
        private string _expectedSelectId; // seule sélection légitime (anti-fantôme)
        private bool _keyboardNav;        // flèches/Home/End en cours

        // Inline rename state
        private TextBox _renameBox;
        private bool _renameClosing;

        public event Action<BinderItem> SelectionChanged;
        public event Action StructureChanged; // a user-initiated, undoable change happened
        public event Action JournalRequested; // clic sur « Journal perso » (pied de Pile)
        // Épingler sur le côté (b47) : la coquille tient l'épingle ; la Pile
        // demande, et sait si l'item est déjà épinglé pour libeller le menu.
        public event Action<BinderItem> SidePinRequested;
        public Func<BinderItem, bool> IsSidePinned;

        // Fourni par MainWindow (cache de composition) : total de pages d'un
        // livre, pour le garde-fou « page finale impaire ». Null = pas d'icône.
        public Func<BinderItem, int> BookPageTotal;
        public event Action DictionaryEntryRequested; // « Nouvelle entrée… » de la racine Dictionnaire (b33)

        private TextBox _searchBox;
        private ComboBox _searchFilter;
        private ListBox _results;

        public BinderView()
        {
            Background = Chrome.BarBg; // la Pile est du chrome (batch 40)
            BorderBrush = Chrome.Border;
            BorderThickness = new Thickness(0, 0, 1, 0);

            _tree = new TreeView { AllowDrop = true };
            _tree.SelectedItemChanged += OnSelectedItemChanged;
            _tree.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            // Le clic droit sélectionne aussi (menu contextuel) : il doit être
            // « attendu » pour passer le filtre anti-fantôme. Et l'élément
            // VISÉ se surligne le temps du menu (batch 28) — sur l'interligne,
            // on sait enfin à qui le menu s'applique.
            _tree.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var node = NodeFromSource(e.OriginalSource);
                _expectedSelectId = node == null ? null : ((BinderItem)node.Tag).Id;
                ClearMenuHighlight();
                if (node == null) return;
                var header = node.Header as System.Windows.Controls.Panel;
                if (header == null) return;
                _menuTarget = header;
                header.Background = Chrome.AccentTint;
                var menu = node.ContextMenu;
                if (menu != null)
                {
                    RoutedEventHandler closed = null;
                    closed = delegate
                    {
                        menu.Closed -= closed;
                        ClearMenuHighlight();
                    };
                    menu.Closed += closed;
                }
            };
            _tree.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left
                    || e.Key == Key.Right || e.Key == Key.Home || e.Key == Key.End
                    || e.Key == Key.PageUp || e.Key == Key.PageDown)
                    _keyboardNav = true;
            };
            _tree.PreviewMouseMove += OnPreviewMouseMove;
            _tree.DragOver += OnDragOver;
            _tree.Drop += OnDrop;
            // Clicking the already-selected row fires no SelectedItemChanged;
            // re-announce it so the main window can bring its view back.
            _tree.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_rebuilding || _renameBox != null) return;
                var node = NodeFromSource(e.OriginalSource);
                if (node == null || !node.IsSelected) return;
                var item = node.Tag as BinderItem;
                if (item == null || _selectedId != item.Id) return;
                var handler = SelectionChanged;
                if (handler != null) handler(item);
            };

            var layout = new DockPanel();
            layout.Children.Add(BuildSearchBar());
            layout.Children.Add(BuildJournalRow());

            _results = new ListBox
            {
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            _results.SelectionChanged += OnResultChosen;
            // Re-clicking the already-selected result fires no SelectionChanged;
            // announce it on mouse-up so the item always opens.
            _results.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                var current = e.OriginalSource as DependencyObject;
                while (current != null && !(current is ListBoxItem))
                    current = current is Visual ? VisualTreeHelper.GetParent(current)
                                                : LogicalTreeHelper.GetParent(current);
                var entry = current as ListBoxItem;
                if (entry == null || entry.Tag == null || !entry.IsSelected) return;
                _selectedId = (string)entry.Tag;
                var handler = SelectionChanged;
                if (handler != null) handler(SelectedItem);
            };

            var host = new Grid();
            host.Children.Add(_tree);
            host.Children.Add(_results);

            // Indicateur de dépôt : filet d'insertion entre deux lignes, ou
            // cadre autour d'un conteneur qui avalera l'élément.
            _dropLine = new Border
            {
                Height = 2.5,
                CornerRadius = new CornerRadius(1.25),
                Background = Chrome.Accent,
                Visibility = Visibility.Collapsed
            };
            _dropBox = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(1.5),
                Visibility = Visibility.Collapsed
            };
            _dropOverlay = new Canvas { IsHitTestVisible = false };
            _dropOverlay.Children.Add(_dropLine);
            _dropOverlay.Children.Add(_dropBox);
            host.Children.Add(_dropOverlay);
            _tree.DragLeave += delegate { ClearDropIndicator(); };

            layout.Children.Add(host);
            Child = layout;
        }

        private void ClearDropIndicator()
        {
            _dropLine.Visibility = Visibility.Collapsed;
            _dropBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>Montre où le dépôt agira : filet accent SOUS la ligne visée
        /// (insertion après elle) ou cadre autour d'un conteneur (imbrication).</summary>
        private void ShowDropIndicator(TreeViewItem node, bool asChild)
        {
            var header = FirstBorderOf(node);
            if (header == null) { ClearDropIndicator(); return; }
            Point origin;
            try { origin = header.TranslatePoint(new Point(0, 0), _dropOverlay); }
            catch { ClearDropIndicator(); return; }
            if (asChild)
            {
                _dropLine.Visibility = Visibility.Collapsed;
                _dropBox.Width = Math.Max(20, header.ActualWidth);
                _dropBox.Height = Math.Max(8, header.ActualHeight);
                Canvas.SetLeft(_dropBox, origin.X);
                Canvas.SetTop(_dropBox, origin.Y);
                _dropBox.Visibility = Visibility.Visible;
            }
            else
            {
                _dropBox.Visibility = Visibility.Collapsed;
                _dropLine.Width = Math.Max(20, header.ActualWidth - 18);
                Canvas.SetLeft(_dropLine, origin.X + 18); // aligné sur le libellé
                Canvas.SetTop(_dropLine, origin.Y + header.ActualHeight - 1);
                _dropLine.Visibility = Visibility.Visible;
            }
        }

        private static Border FirstBorderOf(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                var border = child as Border;
                if (border != null) return border;
                var inner = FirstBorderOf(child);
                if (inner != null) return inner;
            }
            return null;
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
            _searchFilter.Items.Add("Plans");
            _searchFilter.Items.Add("Dictionnaire");
            _searchFilter.Items.Add("Médias");
            _searchFilter.SelectedIndex = 0;
            _searchFilter.SelectionChanged += delegate { RunSearch(); };
            DockPanel.SetDock(_searchFilter, Dock.Right);
            row.Children.Add(_searchFilter);

            _searchBox = new TextBox { ToolTip = "Recherche dans tout le projet (titres, textes, fiches, plans, dictionnaire — sans casse ni accents)" };
            _searchBox.TextChanged += delegate { ScheduleSearch(); };
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { _searchBox.Text = ""; e.Handled = true; }
            };
            row.Children.Add(_searchBox);
            bar.Child = row;
            return bar;
        }

        /// <summary>Pied de Pile : l'entrée fixe « Journal perso ». Hors de
        /// l'arbre (aucun BinderItem, aucune persistance d'arborescence) — un
        /// clic ouvre la vue journal au centre.</summary>
        private UIElement BuildJournalRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("journal-perso", 15, Chrome.SoftText) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(icon);
            }
            row.Children.Add(new TextBlock
            {
                Text = "Journal perso",
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            var bar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Statistiques d'écriture et objectif journalier",
                Child = row
            };
            bar.MouseEnter += delegate { bar.Background = Chrome.BarBg; };
            bar.MouseLeave += delegate { bar.Background = Brushes.Transparent; };
            bar.MouseLeftButtonUp += delegate
            {
                var handler = JournalRequested;
                if (handler != null) handler();
            };
            DockPanel.SetDock(bar, Dock.Bottom);
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
                {
                    _tree.Items.Add(BuildNode(root));
                    // Un filet sous l'Accueil (b41) : un point d'entrée, pas
                    // un dossier de travail comme les racines qui suivent.
                    if (root.IsHomeRoot)
                        _tree.Items.Add(new TreeViewItem
                        {
                            Header = new Border
                            {
                                Height = 1,
                                Background = Chrome.Border,
                                Margin = new Thickness(0, 3, 8, 3),
                                MinWidth = 120
                            },
                            IsEnabled = false,
                            Focusable = false
                        });
                }

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

        // L'en-tête surligné pendant un menu contextuel (batch 28).
        private System.Windows.Controls.Panel _menuTarget;

        private void ClearMenuHighlight()
        {
            if (_menuTarget == null) return;
            _menuTarget.ClearValue(System.Windows.Controls.Panel.BackgroundProperty);
            _menuTarget = null;
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
            if (item.Kind == ItemKind.Book)
            {
                // Les gabarits d'abord, puis un filet, puis les documents.
                var gabarits = 0;
                foreach (var child in item.Children)
                    if (child.Kind == ItemKind.PageTemplate)
                    { node.Items.Add(BuildNode(child)); gabarits++; }
                if (gabarits > 0)
                    node.Items.Add(new TreeViewItem
                    {
                        Header = new Border
                        {
                            Height = 1,
                            Background = Chrome.Border,
                            Margin = new Thickness(0, 2, 8, 2),
                            MinWidth = 120
                        },
                        IsEnabled = false,
                        Focusable = false
                    });
                foreach (var child in item.Children)
                    if (child.Kind != ItemKind.PageTemplate)
                        node.Items.Add(BuildNode(child));
            }
            else
                foreach (var child in item.Children)
                    node.Items.Add(BuildNode(child));
            _nodesById[item.Id] = node;
            return node;
        }

        private UIElement BuildHeader(BinderItem item)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            // Gabarits de pages : blueprint teinté de leur couleur.
            if (item.Kind == ItemKind.PageTemplate)
            {
                var brush = item.TemplateColor != null
                    ? (Brush)new SolidColorBrush(FlowConverter.ParseColor(item.TemplateColor))
                    : Chrome.SoftText;
                var icon = Icons.Make("blueprint-bold", 12, brush) as FrameworkElement;
                if (icon != null)
                {
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    icon.Margin = new Thickness(0, 0, 6, 0);
                    panel.Children.Add(icon);
                }
            }
            else
                panel.Children.Add(ItemIcons.Render(item, 12,
                    item.IsCategory ? (Brush)Chrome.Accent : Chrome.SoftText));

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

            // Books: alert chip when a document strays from the gabarit.
            if (item.Kind == ItemKind.Book && BookHasDivergentDocs(item))
                panel.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(Color.FromRgb(230, 126, 34)),
                    ToolTip = "Des documents de ce livre ne suivent pas son gabarit "
                        + "(clic droit → Appliquer le gabarit à tous les documents)"
                });

            // Livres : garde-fou d'imposition — icône danger quand la page
            // finale n'est pas impaire (erreur de mise en page courante).
            if (item.Kind == ItemKind.Book && BookPageTotal != null)
            {
                var total = BookPageTotal(item);
                if (total > 0 && total % 2 == 0)
                {
                    var danger = Icons.Make("warning-fill", 12,
                        new SolidColorBrush(Color.FromRgb(241, 196, 15))) as FrameworkElement;
                    if (danger != null)
                    {
                        danger.VerticalAlignment = VerticalAlignment.Center;
                        danger.Margin = new Thickness(5, 0, 0, 0);
                        danger.ToolTip = "La page finale de ce livre (" + total
                            + ") n'est pas impaire — ajoutez ou retirez une page "
                            + "pour une imposition correcte.";
                        panel.Children.Add(danger);
                    }
                }
            }

            // Double-click renames in place (folders included: expansion is on
            // the chevron, Scrivener-style rename wins on the label).
            if (!item.IsCategory)
            {
                var itemRef = item;
                panel.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    if (e.ClickCount != 2) return;
                    e.Handled = true;
                    BeginInlineRename(itemRef, panel, title);
                };
            }
            return panel;
        }

        // ------------------------------------------------------- inline rename

        /// <summary>Swaps the header label for a TextBox. Enter or clicking
        /// elsewhere commits (undoable), Escape cancels.</summary>
        private void BeginInlineRename(BinderItem item, StackPanel header, TextBlock title)
        {
            if (_renameBox != null) return; // one rename at a time
            var box = new TextBox
            {
                Text = item.Title,
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center
            };
            _renameBox = box;
            _renameClosing = false;
            var index = header.Children.IndexOf(title);
            header.Children.RemoveAt(index);
            header.Children.Insert(index, box);

            Action<bool> finish = delegate(bool commit)
            {
                if (_renameClosing) return;
                _renameClosing = true;
                _renameBox = null;
                var newTitle = box.Text.Trim();
                if (commit && newTitle.Length > 0 && newTitle != item.Title)
                    RunAndSelect(new RenameItemAction(item, newTitle), item.Id, null);
                else
                    Rebuild(); // restore the plain label
            };
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; finish(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; finish(false); }
            };
            box.LostKeyboardFocus += delegate { finish(true); };
            box.Loaded += delegate { box.Focus(); box.SelectAll(); };
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
            var id = node == null || !(node.Tag is BinderItem)
                ? null : ((BinderItem)node.Tag).Id;

            // WPF selects a TreeViewItem the moment it RECEIVES the keyboard
            // focus (TreeViewItem.OnGotFocus → Select) — and masquer une vue
            // qui portait le focus le fait retomber sur un nœud de l'arbre.
            // Toute sélection qui ne vient ni d'un clic sur CE nœud, ni du
            // clavier, ni de SelectItem, est un fantôme : révoquée.
            var allowed = id == null || _keyboardNav || id == _expectedSelectId;
            _keyboardNav = false;
            if (!allowed)
            {
                RestoreSelection(node);
                return;
            }

            _selectedId = id;
            var handler = SelectionChanged;
            if (handler != null) handler(node == null ? null : (BinderItem)node.Tag);
        }

        /// <summary>Revokes a phantom selection: the previously selected node
        /// takes the selection back (silently), the phantom is deselected.</summary>
        private void RestoreSelection(TreeViewItem phantom)
        {
            _rebuilding = true;
            try
            {
                TreeViewItem node;
                if (_selectedId != null && _nodesById.TryGetValue(_selectedId, out node))
                    node.IsSelected = true;
                else if (phantom != null)
                    phantom.IsSelected = false;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        public void SelectItem(string id)
        {
            SelectItem(id, true);
        }

        /// <summary>bringIntoView false = re-sélection silencieuse (le rappel
        /// anti-fantôme) : NE PAS faire défiler l'arbre, sinon la ligne bouge
        /// sous la souris entre les deux clics d'un double-clic (renommage).</summary>
        public void SelectItem(string id, bool bringIntoView)
        {
            _selectedId = id;
            _expectedSelectId = id; // sélection programmée = légitime
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
                if (bringIntoView) node.BringIntoView();
            }
        }

        // ------------------------------------------------------- context menus

        /// <summary>Le menu contextuel d'un item — public depuis le 14/09 :
        /// les tuiles de la bibliothèque de fiches offrent le même.</summary>
        public ContextMenu BuildContextMenu(BinderItem item)
        {
            return BuildContextMenu(item, false, true);
        }

        /// <summary>Le même menu pour les TUILES des corkboards, de la
        /// bibliothèque et de l'Accueil (14/09) : « Supprimer » demande
        /// confirmation, et les créations « au même niveau » de la Pile
        /// (Nouvelle fiche, Nouvel import, Nouveau plan) n'y sont pas.</summary>
        public ContextMenu BuildContextMenu(BinderItem item, bool confirmDelete)
        {
            return BuildContextMenu(item, confirmDelete, false);
        }

        private ContextMenu BuildContextMenu(BinderItem item, bool confirmDelete, bool fromPile)
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
            // L'Accueil (batch 41) : rien à créer, rien à renommer, rien à
            // supprimer — pas de menu du tout.
            if (item.IsHomeRoot) return null;
            // La racine Dictionnaire (batch 33) n'a pas d'enfants dans la
            // Pile : ses entrées vivent dans son écran.
            if (item.IsCategory && item.CategoryKey == Project.KeyDictionary)
            {
                AddMenu(menu, "Nouvelle entrée…", delegate
                {
                    var handler = DictionaryEntryRequested;
                    if (handler != null) handler();
                });
                return menu;
            }
            // La racine Plans (batch 35) ne reçoit que des plans.
            if (item.IsCategory && item.CategoryKey == Project.KeyPlans)
            {
                AddMenu(menu, "Nouveau plan", delegate { NewPlan(item); });
                return menu;
            }
            // La racine Cartes mentales (22/09) ne reçoit que des cartes.
            if (item.IsCategory && item.CategoryKey == Project.KeyMindMaps)
            {
                AddMenu(menu, "Nouvelle carte mentale", delegate { NewMindMap(item); });
                AddMenu(menu, "Importer une carte (.tea)…", delegate { ImportMindMapDialog(item); });
                return menu;
            }

            if (item.CanHaveChildren)
            {
                var rootKey = item.RootCategory().CategoryKey;
                // Chaque racine ne propose que son contenu natif (pack de
                // correctifs du 12/09/2026) : Fiches = fiches et dossiers,
                // Recherche = import seulement, Écrits = écrits, livres,
                // dossiers. Le dépôt par glisser-déposer reste libre.
                if (rootKey == Project.KeySheets)
                {
                    AddMenu(menu, "Nouvelle fiche", delegate { NewSheet(item); });
                    AddMenu(menu, "Nouveau dossier", delegate { NewFolder(item); });
                }
                else if (rootKey == Project.KeyResearch)
                {
                    AddMenu(menu, "Importer des fichiers…", delegate { ImportMediaDialog(item); });
                }
                else
                {
                    AddMenu(menu, "Nouvel écrit", delegate { NewText(item); });
                    // Un Livre se crée dans Écrits uniquement, jamais dans un
                    // autre livre.
                    if (rootKey == Project.KeyWritings && item.EnclosingBook() == null)
                        AddMenu(menu, "Nouveau livre", delegate { NewBook(item); });
                    AddMenu(menu, "Nouveau dossier", delegate { NewFolder(item); });
                }
            }
            // Depuis la Pile seulement (14/09) : créer AU MÊME NIVEAU que
            // l'item cliqué — une fiche à côté d'une fiche, un import à côté
            // d'un document de Recherche, un plan à côté d'un plan.
            if (fromPile && !item.IsCategory)
            {
                var parent = item.Parent;
                if (item.Kind == ItemKind.Sheet)
                    AddMenu(menu, "Nouvelle fiche", delegate { NewSheet(parent); });
                else if (item.Kind == ItemKind.Media)
                    AddMenu(menu, "Nouvel import…", delegate { ImportMediaDialog(parent); });
                else if (item.Kind == ItemKind.Plan)
                    AddMenu(menu, "Nouveau plan", delegate { NewPlan(null); });
                else if (item.Kind == ItemKind.MindMap)
                {
                    AddMenu(menu, "Nouvelle carte mentale", delegate { NewMindMap(null); });
                    AddMenu(menu, "Exporter la carte (.tea)…", delegate { ExportMindMap(item); });
                }
            }
            if (!item.IsCategory)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator()); // pas de filet en tête (fiche, écrit sans enfant — 14/09)
                // Épingler sur l'Accueil (batch 41) : une bascule annulable.
                AddMenu(menu, item.Pinned ? "Ne plus épingler à l'accueil" : "Épingler à l'accueil", delegate { TogglePin(item); });
                if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                {
                    var sidePinned = IsSidePinned != null && IsSidePinned(item);
                    AddMenu(menu, sidePinned ? "Retirer du rail" : "Épingler au rail", delegate
                    {
                        var handler = SidePinRequested;
                        if (handler != null) handler(item);
                    });
                }
                if (item.Kind == ItemKind.Book)
                    AddMenu(menu, "Options du livre…", delegate { BookOptions(item); });
                AddMenu(menu, "Renommer…", delegate { Rename(item); });
                AddMenu(menu, "Changer l'icône…", delegate { ChangeIcon(item); });
                if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Book)
                {
                    AddMenu(menu, item.ImageId == null ? "Image de la carte…" : "Changer l'image de la carte…",
                        delegate { ChangeCardImage(item); });
                    if (item.ImageId != null)
                        AddMenu(menu, "Retirer l'image de la carte", delegate { RemoveCardImage(item); });
                }
                AddMenu(menu, "Supprimer", delegate
                {
                    if (confirmDelete && !ConfirmTrash(item)) return;
                    Delete(item);
                });
            }
            return menu;
        }

        /// <summary>« Supprimer » depuis une tuile (14/09) : on demande —
        /// l'item part à la corbeille, d'où on le restaure, mais une tuile
        /// se clique vite.</summary>
        public bool ConfirmTrash(BinderItem item)
        {
            if (item == null) return false;
            var what = item.Kind == ItemKind.Book ? "le livre" : item.Kind == ItemKind.Folder ? "le dossier"
                : item.Kind == ItemKind.Sheet ? "la fiche" : item.Kind == ItemKind.Plan ? "le plan" : item.Kind == ItemKind.MindMap ? "la carte"
                : item.Kind == ItemKind.PageTemplate ? "le gabarit" : item.Kind == ItemKind.Media ? "le document" : "l'écrit";
            var answer = MessageDialog.Show(Window.GetWindow(this),
                "Envoyer " + what + " « " + item.Title + " » à la corbeille ?"
                + (item.Children.Count > 0 ? "\nSon contenu part avec." : ""),
                MainWindow.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return answer == MessageBoxResult.Yes;
        }

        private static void AddMenu(ContextMenu menu, string label, RoutedEventHandler onClick)
        {
            var entry = new MenuItem { Header = label };
            entry.Click += onClick;
            menu.Items.Add(entry);
        }

        // ------------------------------------------------------- operations

        /// <summary>The parent that receives a new item, given the current
        /// selection. Texts can carry children, but new items land as their
        /// siblings — nesting under a text is an explicit act (Ctrl+drop).</summary>
        private BinderItem TargetParent()
        {
            var selected = SelectedItem;
            if (selected == null || IsSpecialRoot(selected))
                return _project.Category(Project.KeyWritings);
            return selected.IsContainer ? selected : selected.Parent;
        }

        /// <summary>Corbeille et Dictionnaire : des racines qui ne reçoivent
        /// rien par création, import ou dépôt.</summary>
        private static bool IsSpecialRoot(BinderItem item)
        {
            var key = item.RootCategory().CategoryKey;
            return key == Project.KeyTrash || key == Project.KeyDictionary || key == Project.KeyPlans
                || key == Project.KeyHome; // l'Accueil (b41) : un point d'entrée, pas un dossier
        }

        /// <summary>Un nouveau plan (batch 35), toujours dans la racine Plans.</summary>
        public void NewPlan(BinderItem parent)
        {
            var root = _project.Category(Project.KeyPlans);
            if (root == null) return;
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouveau plan", "Nom du plan :", "Nouveau plan");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.Plan, Title = title, Plan = new PlanInfo() };
            RunAndSelect(new AddItemAction(root, item, -1), item.Id, root.Id);
        }

        // ============================================================ cartes mentales (22/09)

        /// <summary>Une carte neuve : le module Mental-o fabrique le .tea vierge.</summary>
        public void NewMindMap(BinderItem parent)
        {
            var root = _project.Category(Project.KeyMindMaps);
            if (root == null) return;
            var provider = Extensions.ModuleRegistry.MindMaps;
            if (provider == null)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "Les cartes mentales demandent le module Mental-o : Préférences › DLC.",
                    "Cartes mentales", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouvelle carte mentale", "Nom de la carte :", "Nouvelle carte");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.MindMap, Title = title, MapBytes = provider.NewMap(title) };
            RunAndSelect(new AddItemAction(root, item, -1), item.Id, root.Id);
        }

        /// <summary>Un .tea de Mental-o entre tel quel dans le projet.</summary>
        public void ImportMindMapDialog(BinderItem parent)
        {
            var root = _project.Category(Project.KeyMindMaps);
            if (root == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = MindMaps.OpenFilter };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var items = new List<BinderItem>();
            foreach (var path in dialog.FileNames)
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (!MindMaps.Inspect(bytes).Readable) throw new InvalidDataException("ce n'est pas une carte Mental-o");
                    items.Add(new BinderItem { Kind = ItemKind.MindMap, Title = Path.GetFileNameWithoutExtension(path), MapBytes = bytes });
                }
                catch (Exception error)
                {
                    MessageDialog.Show(Window.GetWindow(this), "Import impossible de « " + Path.GetFileName(path) + "» : " + error.Message,
                        "Cartes mentales", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            if (items.Count == 0) return;
            RunAndSelect(new AddItemsAction(root, items), items[items.Count - 1].Id, root.Id);
        }

        /// <summary>La carte redevient un .tea ouvrable dans Mental-o.</summary>
        public void ExportMindMap(BinderItem item)
        {
            if (item == null || item.Kind != ItemKind.MindMap || item.MapBytes == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = MindMaps.SaveFilter, FileName = SafeName(item.Title) + MindMaps.Extension };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try { File.WriteAllBytes(dialog.FileName, item.MapBytes); }
            catch (Exception error)
            {
                MessageDialog.Show(Window.GetWindow(this), "Export impossible : " + error.Message, "Cartes mentales", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SafeName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name ?? "")
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "carte" : sb.ToString();
        }

        public void NewText(BinderItem parent)
        {
            if (parent == null) parent = TargetParent();
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouvel écrit", "Titre de l'écrit :", "Nouvel écrit");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.Text, Title = title };
            item.Document = TextDocument.FromPlainText(""); // jamais zéro paragraphe
            // Created inside a book: the document inherits the gabarit.
            var book = parent.EnclosingBook();
            if (book != null && book.Book != null)
                item.Page = book.Book.Template.Clone();
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

        /// <summary>Books live in Écrits only (never nested in another book):
        /// out-of-scope parents fall back to the Écrits category.</summary>
        public void NewBook(BinderItem parent)
        {
            if (parent == null) parent = TargetParent();
            if (parent.RootCategory().CategoryKey != Project.KeyWritings
                || parent.EnclosingBook() != null)
                parent = _project.Category(Project.KeyWritings);
            var title = InputDialog.Ask(Window.GetWindow(this), "Nouveau livre", "Titre du livre :", "Nouveau livre");
            if (title == null) return;
            var item = new BinderItem { Kind = ItemKind.Book, Title = title, Book = new BookInfo() };
            Defaults.Seed(item.Book); // éditeur et collection par défaut (Préférences › Auteur, 22/09)
            RunAndSelect(new AddItemAction(parent, item, -1), item.Id, parent.Id);
        }

        private string TemplateColorOf(string id)
        {
            var gabarit = _project.FindById(id);
            return gabarit == null ? null : gabarit.TemplateColor;
        }

        /// <summary>True when a text of the book does not follow its gabarit —
        /// the alert chip next to the book's name.</summary>
        public bool BookHasDivergentDocs(BinderItem book)
        {
            if (book.Book == null || _project == null) return false;
            return DivergesRecursive(book, book.Book.Template);
        }

        private bool DivergesRecursive(BinderItem item, Model.PageSetup template)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text)
                {
                    var effective = child.Page ?? _project.Page;
                    if (!effective.SameLayout(template)) return true;
                }
                if (DivergesRecursive(child, template)) return true;
            }
            return false;
        }

        /// <summary>Renames in place when the item's row is on screen (F2, menu,
        /// context menu); falls back to a dialog otherwise (search mode).</summary>
        /// <summary>Épingler / ne plus épingler (batch 41) — par l'historique,
        /// comme toute mutation de la Pile ; jamais une racine.</summary>
        public void TogglePin(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;
            RunAndSelect(new PinItemAction(item), null, null);
        }

        public void Rename(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;

            TreeViewItem node;
            if (_tree.Visibility == Visibility.Visible
                && _nodesById.TryGetValue(item.Id, out node))
            {
                SelectItem(item.Id);
                var header = node.Header as StackPanel;
                TextBlock title = null;
                if (header != null)
                    foreach (var child in header.Children)
                        if (child is TextBlock && !(child is TextBox)) title = (TextBlock)child;
                if (header != null && title != null)
                {
                    BeginInlineRename(item, header, title);
                    return;
                }
            }
            var answer = InputDialog.Ask(Window.GetWindow(this), "Renommer", "Nouveau titre :", item.Title);
            if (answer == null || answer == item.Title) return;
            RunAndSelect(new RenameItemAction(item, answer), item.Id, null);
        }

        /// <summary>Renommage par dialogue SANS déplacer la sélection — le
        /// « Renommer… » d'une carte de corkboard (b43) reste sur le tableau.</summary>
        public void RenameQuiet(BinderItem item)
        {
            if (item == null || item.IsCategory) return;
            var answer = InputDialog.Ask(Window.GetWindow(this), "Renommer", "Nouveau titre :", item.Title);
            if (answer == null || answer == item.Title) return;
            RunAndSelect(new RenameItemAction(item, answer), null, null);
        }

        /// <summary>« Options du livre » (batch 32) : nom, icône, objectif de
        /// chapitres — un dialogue, une action annulable.</summary>
        public void BookOptions(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.Kind != ItemKind.Book) return;
            var result = BookOptionsDialog.Ask(Window.GetWindow(this), item);
            if (result == null) return;
            var action = new BookOptionsAction(item, result.Title, result.Icon, result.ChapterGoal,
                result.Deadline, result.SizeGoal, result.SizeUnit);
            if (action.IsNoOp) return;
            RunAndSelect(action, item.Id, null);
        }

        /// <summary>L'image de la tuile d'un écrit ou d'un livre (pack du
        /// 12/09/2026) : au tableau, elle remplace l'extrait du texte ou les
        /// notes. La sélection ne bouge pas (on l'appelle depuis une carte).</summary>
        public void ChangeCardImage(BinderItem item)
        {
            if (item == null || item.IsCategory || _project == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            string imageId;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                if (info.Length > 20 * 1024 * 1024)
                    throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                imageId = _project.AddImage(bytes, System.IO.Path.GetExtension(dialog.FileName));
            }
            catch (Exception error)
            {
                MessageDialog.Show(Window.GetWindow(this), "Image refusée : " + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RunAndSelect(new ChangeImageAction(item, imageId), null, null);
        }

        public void RemoveCardImage(BinderItem item)
        {
            if (item == null || item.ImageId == null) return;
            RunAndSelect(new ChangeImageAction(item, null), null, null);
        }

        public void ChangeIcon(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;
            var chosen = IconPickerDialog.Ask(Window.GetWindow(this));
            if (chosen == null) return; // cancelled
            var icon = chosen.Length == 0 ? null : chosen;
            if (icon == item.Icon) return;
            RunAndSelect(new ChangeIconAction(item, icon), item.Id, null);
        }

        public void Delete(BinderItem item)
        {
            if (item == null) item = SelectedItem;
            if (item == null || item.IsCategory) return;
            if (item.RootCategory().CategoryKey == Project.KeyTrash) return; // already in trash
            // « Ooh la boulette ! » (12/09) : un livre d'au moins cinq chapitres.
            var blunder = item.Kind == ItemKind.Book && Achievements.ChapterCount(item) >= 5;
            RunAndSelect(new DeleteToTrashAction(_project.Trash, item), null, _project.Trash.Id);
            if (blunder) RaiseAchievement(Achievements.Blunder);
        }

        /// <summary>Un succès à événement gagné depuis la Pile (12/09).</summary>
        public event Action<string> AchievementEvent;

        private void RaiseAchievement(string id)
        {
            var handler = AchievementEvent;
            if (handler != null) handler(id);
        }

        public void EmptyTrash()
        {
            if (_project.Trash.Children.Count == 0) return;
            var answer = MessageDialog.Show(Window.GetWindow(this),
                "Vider définitivement la corbeille ?", "Marabook",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            // « Terre brûlée » / « Masochiste » : les suppressions DÉFINITIVES,
            // descendants compris (12/09).
            Settings.AppSettings.PermanentlyDeleted += Achievements.CountAll(_project.Trash.Children);
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
            _expectedSelectId = node == null ? null : ((BinderItem)node.Tag).Id;
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
            DragDrop.DoDragDrop(_tree, new DataObject("MarabookItem", dragged.Id), DragDropEffects.Move);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy; // Explorer files -> media import
                e.Handled = true;
                return;
            }
            var target = DropTarget(e);
            e.Effects = target == null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            if (target == null) { ClearDropIndicator(); return; }
            var node = NodeFromSource(e.OriginalSource);
            if (node == null) { ClearDropIndicator(); return; }
            // Même règle que le dépôt : conteneur = imbrication, sinon
            // insertion après la ligne (Ctrl force l'imbrication).
            var asChild = target.IsContainer
                || (target.CanHaveChildren
                    && (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey);
            ShowDropIndicator(node, asChild);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            ClearDropIndicator();
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
            var dragged = _project.FindById((string)e.Data.GetData("MarabookItem"));
            if (dragged == null) return;

            // Containers swallow the drop; on a document the default is sibling
            // reordering (insert right after) and Ctrl makes it a child.
            var asChild = target.IsContainer
                || (target.CanHaveChildren
                    && (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey);

            BinderItem newParent;
            int newIndex;
            if (asChild)
            {
                newParent = target;
                newIndex = -1;
            }
            else
            {
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
            if (!e.Data.GetDataPresent("MarabookItem")) return null;
            var dragged = _project.FindById((string)e.Data.GetData("MarabookItem"));
            var node = NodeFromSource(e.OriginalSource);
            var target = node == null ? null : node.Tag as BinderItem;
            if (dragged == null || target == null || dragged == target) return null;
            if (target == dragged.Parent && target.CanHaveChildren) return null; // no-op move
            if (target.IsDescendantOf(dragged)) return null;
            if (target.RootCategory().CategoryKey == Project.KeyTrash) return null; // deletion has its own path
            if (target.RootCategory().CategoryKey == Project.KeyDictionary) return null; // pas un conteneur (b33)
            if (target.RootCategory().CategoryKey == Project.KeyHome) return null; // l'Accueil non plus (b41)
            // La racine Plans n'accepte que des plans, et un plan ne sort pas de sa racine (b35).
            if ((target.RootCategory().CategoryKey == Project.KeyPlans) != (dragged.Kind == ItemKind.Plan)) return null;
            if (dragged.Kind == ItemKind.Plan && !target.IsCategory) return null;
            // Même règle pour les cartes mentales (22/09).
            if ((target.RootCategory().CategoryKey == Project.KeyMindMaps) != (dragged.Kind == ItemKind.MindMap)) return null;
            if (dragged.Kind == ItemKind.MindMap && !target.IsCategory) return null;
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

        // Recherche de la Pile (batch 37) : anti-rebond de 200 ms sur la
        // frappe, moteur ProjectSearch (champs en cache, sans casse ni
        // accents) sur un FIL DE FOND annulable — une nouvelle frappe annule
        // la précédente, un résultat périmé (génération) est jeté (motif du
        // pipeline différé b29).
        private System.Windows.Threading.DispatcherTimer _searchDebounce;
        private System.Threading.CancellationTokenSource _searchCancel;
        private int _searchGeneration;

        private void ScheduleSearch()
        {
            if (_searchDebounce == null)
            {
                _searchDebounce = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _searchDebounce.Tick += delegate { _searchDebounce.Stop(); RunSearch(); };
            }
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private static SearchKind KindOfFilter(int index)
        {
            switch (index)
            {
                case 1: return SearchKind.Texts;
                case 2: return SearchKind.Sheets;
                case 3: return SearchKind.Plans;
                case 4: return SearchKind.Dictionary;
                case 5: return SearchKind.Media;
                default: return SearchKind.All;
            }
        }

        private void RunSearch()
        {
            if (_project == null || _results == null) return;
            if (_searchCancel != null) { _searchCancel.Cancel(); _searchCancel = null; }
            var query = _searchBox.Text.Trim();
            if (query.Length == 0)
            {
                _results.Visibility = Visibility.Collapsed;
                _tree.Visibility = Visibility.Visible;
                return;
            }
            _tree.Visibility = Visibility.Collapsed;
            _results.Visibility = Visibility.Visible;

            var targets = ProjectSearch.Collect(_project, SearchScope.Project, SelectedItem,
                KindOfFilter(_searchFilter.SelectedIndex), false);
            var compiled = SearchQuery.Create(query, false, false, true, false);
            var cancel = new System.Threading.CancellationTokenSource();
            _searchCancel = cancel;
            var generation = ++_searchGeneration;
            var dispatcher = Dispatcher;
            System.Threading.Tasks.Task.Factory.StartNew<SearchResult>(delegate
            {
                return ProjectSearch.Run(targets, compiled, ProjectSearch.DefaultCap, ProjectSearch.DefaultBudget, cancel.Token);
            }, cancel.Token).ContinueWith(delegate(System.Threading.Tasks.Task<SearchResult> done)
            {
                var ignored = done.Exception; // observée : une recherche en faute se tait
                if (done.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || done.Result.Cancelled) return;
                dispatcher.BeginInvoke(new Action(delegate
                {
                    if (generation != _searchGeneration) return; // périmé
                    ShowSearchResult(done.Result);
                }));
            });
        }

        private void ShowSearchResult(SearchResult result)
        {
            _results.Items.Clear();
            var counts = new Dictionary<string, int>();
            var order = new List<BinderItem>();
            foreach (var hit in result.Hits)
            {
                if (!counts.ContainsKey(hit.Item.Id)) { counts[hit.Item.Id] = 0; order.Add(hit.Item); }
                counts[hit.Item.Id]++;
            }
            foreach (var item in order) _results.Items.Add(BuildResultRow(item, counts[item.Id]));
            var summary = result.Total == 0 ? "Aucun résultat" : result.Summary();
            if (!string.IsNullOrEmpty(result.Message)) summary += "\n" + result.Message;
            _results.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = summary, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap },
                IsEnabled = false
            });
        }

        private ListBoxItem BuildResultRow(BinderItem item, int count)
        {
            var panel = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(ItemIcons.Render(item, 11, Chrome.SoftText));
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
            var occurrences = count == 1 ? "1 occurrence" : count + " occurrences";
            panel.Children.Add(new TextBlock
            {
                Text = path.Length > 0 ? path + " · " + occurrences : occurrences,
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
            if (selected == null || IsSpecialRoot(selected))
                return _project.Category(Project.KeyWritings);
            return selected.IsContainer ? selected : selected.Parent;
        }

        public void NewSheet(BinderItem parent)
        {
            if (parent == null)
            {
                var selected = SelectedItem;
                parent = selected != null && selected.IsContainer
                    && !IsSpecialRoot(selected)
                    ? selected : _project.Category(Project.KeySheets);
            }
            string title, categoryId;
            if (!NewSheetDialog.Ask(Window.GetWindow(this), _project, out title, out categoryId))
                return;
            // La fiche naît dans sa catégorie, avec le modèle de base de
            // celle-ci (batch 31) — sans catégorie : champs libres seuls.
            var category = _project.FindSheetCategory(categoryId);
            var item = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                CategoryId = category != null ? category.Id : null,
                TemplateId = category != null ? category.TemplateId : null
            };
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
            if (parent == null || !parent.CanHaveChildren || IsSpecialRoot(parent))
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
                MessageDialog.Show(Window.GetWindow(this),
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
