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
    /// <summary>The corkboard: one index card per child of the selected folder
    /// (or category). Card text is read-only: notes first, else the start of
    /// the text for written documents, empty for sheets; sheets show their main
    /// image, media a thumbnail. Cards reorder by drag &amp; drop (undoable),
    /// double-click opens the item.</summary>
    public class CorkboardView : Border
    {
        private readonly WrapPanel _cards;
        private BinderItem _folder;
        private HistoryManager _history;
        private Project _project; // image store lookups

        private BinderItem _dragCandidate;
        private Point _dragStart;

        // Cartes sélectionnables (multi avec Ctrl) — le double-clic ouvre.
        private readonly HashSet<string> _selected = new HashSet<string>();

        private readonly WrapPanel _templateCards; // section gabarits (livres)
        private readonly Border _templateSeparator;
        private readonly StackPanel _documentActions; // « Nouveau document ▾ »

        public event Action<BinderItem> Navigate;
        public event Action Changed; // synopsis edited or cards reordered
        public event Action<BinderItem> ExportRequested;      // menu ⋮
        public event Action<BinderItem> DeleteRequested;      // menu ⋮ (corbeille)
        public event Action<List<BinderItem>> ApplyTemplateRequested; // gabarit sur la sélection
        public event Action<BinderItem> NewTemplateRequested;    // livre
        public event Action<BinderItem> ImportTemplateRequested; // livre
        public event Action<BinderItem> ExportTemplateRequested; // gabarit
        public event Action<BinderItem> CopyTemplateRequested;   // gabarit
        // Livres : « Nouveau document » et sa flèche — le second argument est
        // la sorte de page extra (ExtraPages.Kind*), null = document simple.
        public event Action<BinderItem, string> NewDocumentRequested;

        /// <summary>The selected cards, in reading order.</summary>
        public List<BinderItem> SelectedItems()
        {
            var result = new List<BinderItem>();
            if (_folder == null) return result;
            foreach (var child in _folder.Children)
                if (_selected.Contains(child.Id)) result.Add(child);
            return result;
        }

        public CorkboardView()
        {
            Focusable = true; // le focus logique quitte la Pile à l'affichage
            Background = Chrome.WindowBg;
            _templateCards = new WrapPanel { Margin = new Thickness(16, 12, 16, 4) };
            _templateSeparator = new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(24, 6, 24, 2),
                Visibility = Visibility.Collapsed
            };
            _documentActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, 8, 24, 0),
                Visibility = Visibility.Collapsed
            };
            BuildDocumentActions();
            _cards = new WrapPanel { Margin = new Thickness(16, 8, 16, 16) };
            var layout = new StackPanel();
            layout.Children.Add(_templateCards);
            layout.Children.Add(_templateSeparator);
            layout.Children.Add(_documentActions);
            layout.Children.Add(_cards);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = layout
            };
            Child = scroll;
            AllowDrop = true;
            DragOver += OnBoardDragOver;
            Drop += OnBoardDrop;
        }

        public void Load(BinderItem folder, HistoryManager history, Project project)
        {
            if (!ReferenceEquals(_folder, folder)) _selected.Clear();
            _folder = folder;
            _history = history;
            _project = project;
            Rebuild();
        }

        private void RefreshSelectionVisuals()
        {
            foreach (var child in _cards.Children)
            {
                var card = child as Border;
                var item = card == null ? null : card.Tag as BinderItem;
                if (item == null) continue;
                var selected = _selected.Contains(item.Id);
                // Le liseré orange de divergence de gabarit garde la priorité.
                var divergent = card.BorderBrush is SolidColorBrush
                    && ((SolidColorBrush)card.BorderBrush).Color == Color.FromRgb(230, 126, 34);
                if (!divergent)
                    card.BorderBrush = selected ? (Brush)Chrome.Accent : Chrome.Border;
                card.BorderThickness = new Thickness(selected ? 2 : 1);
                card.Margin = new Thickness(selected ? 7 : 8);
            }
        }

        /// <summary>The ⋮ options menu, top right of every card.</summary>
        private UIElement BuildCardMenu(BinderItem item)
        {
            var button = new Button
            {
                Content = "⋮",
                FontSize = 14,
                Width = 24,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Chrome.SoftText,
                Focusable = false,
                ToolTip = "Options"
            };
            DockPanel.SetDock(button, Dock.Right);
            var itemRef = item;
            button.Click += delegate
            {
                var menu = new ContextMenu { PlacementTarget = button };
                if (itemRef.Kind == ItemKind.Text || itemRef.Kind == ItemKind.Sheet)
                {
                    var export = new MenuItem { Header = "Exporter…" };
                    export.Click += delegate
                    {
                        var handler = ExportRequested;
                        if (handler != null) handler(itemRef);
                    };
                    menu.Items.Add(export);
                }
                if (itemRef.Kind == ItemKind.Text && _folder != null
                    && _folder.EnclosingBook() != null)
                {
                    var apply = new MenuItem { Header = "Appliquer un gabarit…" };
                    apply.Click += delegate
                    {
                        var handler = ApplyTemplateRequested;
                        if (handler == null) return;
                        // Sur une carte de la sélection : toute la sélection.
                        var targets = _selected.Contains(itemRef.Id) && _selected.Count > 1
                            ? SelectedItems() : new List<BinderItem> { itemRef };
                        handler(targets);
                    };
                    menu.Items.Add(apply);

                    // Page extra : hors du récit — sans folio, exclue de la
                    // table des matières. La coche marque l'état actif.
                    var extra = new MenuItem
                    {
                        Header = "Marquer comme page extra",
                        Icon = itemRef.IsExtraPage
                            ? Icons.Make("check-bold", 12, Chrome.Ink) : null
                    };
                    extra.Click += delegate
                    {
                        itemRef.IsExtraPage = !itemRef.IsExtraPage;
                        var changed = Changed;
                        if (changed != null) changed();
                    };
                    menu.Items.Add(extra);
                }
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                var delete = new MenuItem { Header = "Supprimer" };
                delete.Click += delegate
                {
                    var handler = DeleteRequested;
                    if (handler != null) handler(itemRef);
                };
                menu.Items.Add(delete);
                menu.IsOpen = true;
            };
            return button;
        }

        public void Clear()
        {
            _folder = null;
            _cards.Children.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _folder == item; }

        /// <summary>The page gabarit applied to a document, when any.</summary>
        private BinderItem AppliedTemplate(BinderItem item)
        {
            if (item.Kind != ItemKind.Text || item.PageTemplateId == null || _project == null)
                return null;
            var gabarit = _project.FindById(item.PageTemplateId);
            return gabarit != null && gabarit.Kind == ItemKind.PageTemplate ? gabarit : null;
        }

        private void Rebuild()
        {
            _cards.Children.Clear();
            _templateCards.Children.Clear();
            _templateSeparator.Visibility = Visibility.Collapsed;
            _documentActions.Visibility = Visibility.Collapsed;
            if (_folder == null) return;

            // Livres : la section GABARITS vit au-dessus des documents,
            // séparée par un filet — autre niveau hiérarchique.
            if (_folder.Kind == ItemKind.Book)
            {
                _templateSeparator.Visibility = Visibility.Visible;
                _documentActions.Visibility = Visibility.Visible;
                foreach (var child in _folder.Children)
                    if (child.Kind == ItemKind.PageTemplate)
                        _templateCards.Children.Add(BuildTemplateCard(child));
                _templateCards.Children.Add(BuildTemplateActions());
            }

            var documents = 0;
            foreach (var child in _folder.Children)
            {
                if (child.Kind == ItemKind.PageTemplate) continue;
                _cards.Children.Add(BuildCard(child));
                documents++;
            }
            if (documents == 0)
                _cards.Children.Add(new TextBlock
                {
                    Text = _folder.Kind == ItemKind.Book ? "(livre sans document)" : "(dossier vide)",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(12)
                });
        }

        /// <summary>A gabarit card: blueprint tinted with its color, name,
        /// ⋮ menu (Exporter, Copier vers un autre livre, Supprimer). Le
        /// double-clic ouvre la maquette.</summary>
        private UIElement BuildTemplateCard(BinderItem gabarit)
        {
            var card = new Border
            {
                Width = 170,
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(8, 4, 8, 4),
                Padding = new Thickness(10, 8, 4, 8),
                Tag = gabarit
            };
            var row = new DockPanel();
            var menu = BuildTemplateMenu(gabarit);
            row.Children.Add(menu);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var brush = gabarit.TemplateColor != null
                ? (Brush)new SolidColorBrush(FlowConverter.ParseColor(gabarit.TemplateColor))
                : Chrome.SoftText;
            var icon = Icons.Make("blueprint-bold", 14, brush) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 7, 0);
                left.Children.Add(icon);
            }
            left.Children.Add(new TextBlock
            {
                Text = gabarit.Title,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 110
            });
            row.Children.Add(left);
            card.Child = row;
            var gabaritRef = gabarit;
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                var handler = Navigate;
                if (handler != null) handler(gabaritRef);
            };
            return card;
        }

        private UIElement BuildTemplateMenu(BinderItem gabarit)
        {
            var button = new Button
            {
                Content = Icons.Make("dots-three-vertical-bold", 13, Chrome.SoftText),
                Width = 24,
                Height = 22,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = false,
                ToolTip = "Options du gabarit"
            };
            DockPanel.SetDock(button, Dock.Right);
            var gabaritRef = gabarit;
            button.Click += delegate
            {
                var menu = new ContextMenu { PlacementTarget = button };
                var export = new MenuItem { Header = "Exporter (.usgab)…" };
                export.Click += delegate
                { var h = ExportTemplateRequested; if (h != null) h(gabaritRef); };
                menu.Items.Add(export);
                var copy = new MenuItem { Header = "Copier vers un autre livre…" };
                copy.Click += delegate
                { var h = CopyTemplateRequested; if (h != null) h(gabaritRef); };
                menu.Items.Add(copy);
                menu.Items.Add(new Separator());
                var delete = new MenuItem { Header = "Supprimer" };
                delete.Click += delegate
                { var h = DeleteRequested; if (h != null) h(gabaritRef); };
                menu.Items.Add(delete);
                menu.IsOpen = true;
            };
            return button;
        }

        private UIElement BuildTemplateActions()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 8, 4)
            };
            var create = new Button
            {
                Content = "Nouveau gabarit…",
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Deux pages en vis-à-vis aux marges du livre — en-têtes et "
                    + "pieds recto/verso marqués aux outils texte"
            };
            create.Click += delegate
            { var h = NewTemplateRequested; if (h != null && _folder != null) h(_folder); };
            panel.Children.Add(create);
            var import = new Button
            {
                Content = "Importer…",
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Ajouter un gabarit .usgab à ce livre"
            };
            import.Click += delegate
            { var h = ImportTemplateRequested; if (h != null && _folder != null) h(_folder); };
            panel.Children.Add(import);
            return panel;
        }

        /// <summary>« Nouveau document » + flèche (livres) : le bouton crée un
        /// document en fin de liste ; la flèche déroule les pages extra —
        /// liminaires, table des matières, page éditeur, page de soutien.</summary>
        private void BuildDocumentActions()
        {
            var create = new Button
            {
                Content = "Nouveau document",
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Ajouter un document à la fin du livre"
            };
            create.Click += delegate { RequestNewDocument(null); };
            _documentActions.Children.Add(create);
            var arrow = new Button
            {
                Content = Icons.Make("caret-down-bold", 11, Chrome.SoftText),
                Width = 22,
                Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Pages extra : liminaires, table des matières…"
            };
            arrow.Click += delegate
            {
                var menu = new ContextMenu { PlacementTarget = arrow };
                AddExtraEntry(menu, "Document vierge", ExtraPages.KindBlank,
                    "Page vierge au gabarit intérieur du livre");
                AddExtraEntry(menu, "Pages de titre", ExtraPages.KindTitle,
                    "Deux gardes vierges, faux-titre, page de titre et copyright");
                AddExtraEntry(menu, "Page de direction d'anthologie", ExtraPages.KindDirection,
                    "Direction du recueil et auteurs participants, verso vierge");
                AddExtraEntry(menu, "Page d'avertissement", ExtraPages.KindWarning,
                    "Avertissement de contenu, verso vierge");
                AddExtraEntry(menu, "Table des matières", ExtraPages.KindToc,
                    "Collecte les documents du livre — mise à jour dynamique");
                AddExtraEntry(menu, "Page éditeur", ExtraPages.KindPublisher,
                    "Présentation de la maison d'édition");
                AddExtraEntry(menu, "Page soutien", ExtraPages.KindSupport,
                    "Mention des soutiens du livre");
                menu.IsOpen = true;
            };
            _documentActions.Children.Add(arrow);
        }

        private void AddExtraEntry(ContextMenu menu, string label, string kind, string tip)
        {
            var entry = new MenuItem { Header = label, ToolTip = tip };
            var kindRef = kind;
            entry.Click += delegate { RequestNewDocument(kindRef); };
            menu.Items.Add(entry);
        }

        private void RequestNewDocument(string kind)
        {
            var handler = NewDocumentRequested;
            if (handler != null && _folder != null) handler(_folder, kind);
        }

        private UIElement BuildCard(BinderItem item)
        {
            var selected = _selected.Contains(item.Id);
            var card = new Border
            {
                Width = 210,
                MinHeight = 130,
                Background = Chrome.CardBg,
                BorderBrush = selected ? (Brush)Chrome.Accent : Chrome.Border,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(selected ? 7 : 8), // épaisseur compensée : rien ne bouge
                Tag = item,
                AllowDrop = true
            };

            var layout = new DockPanel();

            var titleBar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 4, 4, 4)
            };
            DockPanel.SetDock(titleBar, Dock.Top);
            var titleRow = new DockPanel();
            if (!item.IsCategory) titleRow.Children.Add(BuildCardMenu(item));
            var titleLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleLeft.Children.Add(ItemIcons.Render(item, 11, Chrome.SoftText));
            // Gabarit appliqué : l'icône blueprint teintée de sa couleur, le
            // nom du gabarit en infobulle.
            var gabarit = AppliedTemplate(item);
            if (gabarit != null)
            {
                var brush = gabarit.TemplateColor != null
                    ? (Brush)new SolidColorBrush(FlowConverter.ParseColor(gabarit.TemplateColor))
                    : Chrome.SoftText;
                var badge = Icons.Make("blueprint-bold", 11, brush) as FrameworkElement;
                if (badge != null)
                {
                    badge.VerticalAlignment = VerticalAlignment.Center;
                    badge.Margin = new Thickness(0, 0, 5, 0);
                    badge.ToolTip = "Gabarit : " + gabarit.Title;
                    titleLeft.Children.Add(badge);
                }
            }
            titleLeft.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150
            });
            titleRow.Children.Add(titleLeft);
            titleBar.Child = titleRow;
            layout.Children.Add(titleBar);

            var body = new StackPanel();

            // Pictures first: media thumbnail, or a sheet's main image.
            byte[] pictureBytes = null;
            if (item.Kind == ItemKind.Media && MediaView.IsImage(item.MediaExtension))
                pictureBytes = item.MediaBytes;
            else if (item.Kind == ItemKind.Sheet && _project != null)
            {
                var image = _project.FindImage(item.ImageId);
                if (image != null) pictureBytes = image.Bytes;
            }
            if (pictureBytes != null)
            {
                var thumb = MediaView.TryImage(pictureBytes, 200);
                if (thumb != null)
                    body.Children.Add(new Image
                    {
                        Source = thumb,
                        Stretch = Stretch.Uniform,
                        MaxHeight = 90,
                        Margin = new Thickness(8, 8, 8, 0)
                    });
            }

            // Card text, read-only: notes first; else the beginning of the text
            // for written documents; sheets without notes stay blank.
            var text = (item.Notes ?? "").Trim();
            if (text.Length == 0 && item.Kind == ItemKind.Text)
            {
                text = item.Document.ToPlainText().Trim();
                if (text.Length > 220) text = text.Substring(0, 220).TrimEnd() + "…";
            }
            if (text.Length > 0)
                body.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Padding = new Thickness(8),
                    MaxHeight = 150,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            layout.Children.Add(body);

            card.Child = layout;
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                _dragCandidate = item;
                _dragStart = e.GetPosition(this);
                if (e.ClickCount == 2)
                {
                    // Le double-clic ouvre (document comme livre).
                    _dragCandidate = null;
                    e.Handled = true;
                    var handler = Navigate;
                    if (handler != null) handler(item);
                }
            };
            // Le clic simple SÉLECTIONNE (Ctrl = multi) — il n'ouvre plus.
            card.MouseLeftButtonUp += delegate
            {
                if (_dragCandidate != item) return; // un glisser est parti
                _dragCandidate = null;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    if (!_selected.Remove(item.Id)) _selected.Add(item.Id);
                }
                else
                {
                    _selected.Clear();
                    _selected.Add(item.Id);
                }
                RefreshSelectionVisuals();
            };
            card.MouseMove += OnCardMouseMove;
            card.DragOver += OnBoardDragOver;
            card.Drop += delegate(object sender, DragEventArgs e) { DropOnCard(item, e); };

            // Inside a book: a document that strays from the gabarit gets a
            // warning tint and the fix in its context menu.
            var book = _folder == null ? null : _folder.EnclosingBook();
            if (book != null && book.Book != null && item.Kind == ItemKind.Text
                && _project != null)
            {
                var effective = item.Page ?? _project.Page;
                if (!effective.SameLayout(book.Book.Template))
                {
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 126, 34));
                    card.ToolTip = "Ce document ne suit pas le gabarit du livre.";
                    var menu = new ContextMenu();
                    var apply = new MenuItem { Header = "Appliquer le gabarit du livre au document" };
                    var itemRef = item;
                    var bookRef = book;
                    apply.Click += delegate
                    {
                        if (itemRef.Page == null) itemRef.Page = bookRef.Book.Template.Clone();
                        else itemRef.Page.ApplyLayout(bookRef.Book.Template);
                        Rebuild();
                        var handler = Changed;
                        if (handler != null) handler();
                    };
                    menu.Items.Add(apply);
                    card.ContextMenu = menu;
                }
            }
            return card;
        }

        // ------------------------------------------------------- drag reorder

        private void OnCardMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(this, new DataObject("UniversSaleCard", dragged.Id), DragDropEffects.Move);
        }

        private void OnBoardDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("UniversSaleCard") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropOnCard(BinderItem target, DragEventArgs e)
        {
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null || dragged == target) return;
            var index = _folder.Children.IndexOf(target);
            var oldIndex = _folder.Children.IndexOf(dragged);
            if (oldIndex < index) index--; // account for removal before insertion
            Reorder(dragged, index);
            e.Handled = true;
        }

        private void OnBoardDrop(object sender, DragEventArgs e)
        {
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null) return;
            Reorder(dragged, _folder.Children.Count - 1); // empty space: move to end
            e.Handled = true;
        }

        private BinderItem FindChild(string id)
        {
            if (_folder == null || id == null) return null;
            foreach (var child in _folder.Children)
                if (child.Id == id) return child;
            return null;
        }

        private void Reorder(BinderItem item, int newIndex)
        {
            if (newIndex < 0 || _folder.Children.IndexOf(item) == newIndex) return;
            _history.Run(new MoveItemAction(item, _folder, newIndex));
            Rebuild();
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
