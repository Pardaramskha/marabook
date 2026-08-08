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
            CollectSelected(_folder, result); // parties comprises
            return result;
        }

        private void CollectSelected(BinderItem parent, List<BinderItem> result)
        {
            foreach (var child in parent.Children)
            {
                if (_selected.Contains(child.Id)) result.Add(child);
                CollectSelected(child, result);
            }
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

            // Barre d'insertion pendant le glisser : un trait vertical accent
            // là où la carte sera déposée (avant la carte survolée).
            _dropBar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = Chrome.Accent,
                Visibility = Visibility.Collapsed
            };
            _dropOverlay = new Canvas { IsHitTestVisible = false };
            _dropOverlay.Children.Add(_dropBar);
            var host = new Grid();
            host.Children.Add(layout);
            host.Children.Add(_dropOverlay);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = host
            };
            Child = scroll;
            AllowDrop = true;
            DragOver += OnBoardDragOver;
            Drop += OnBoardDrop;
            DragLeave += delegate { HideDropBar(); };

            // Un clic dans le vide (ou Échap) désélectionne les cartes.
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Focus();
                if (_selected.Count == 0) return;
                if (IsWithinCard(e.OriginalSource as DependencyObject)) return;
                _selected.Clear();
                RefreshSelectionVisuals();
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape || _selected.Count == 0) return;
                _selected.Clear();
                RefreshSelectionVisuals();
                e.Handled = true;
            };
            _cards.SizeChanged += delegate { UpdateFolderBoxWidths(); };
        }

        /// <summary>Vrai si le clic est parti d'une carte ou d'une boîte de
        /// partie (leur Tag porte le BinderItem) — faux sur le fond.</summary>
        private static bool IsWithinCard(DependencyObject source)
        {
            while (source != null)
            {
                var element = source as FrameworkElement;
                if (element != null && element.Tag is BinderItem) return true;
                source = source is Visual
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        /// <summary>Chaque boîte de partie occupe la largeur du tableau : dans
        /// le WrapPanel, elle force ainsi son propre retour à la ligne.</summary>
        private void UpdateFolderBoxWidths()
        {
            foreach (var child in _cards.Children)
            {
                var host = child as Grid;
                if (host == null || !(host.Tag is BinderItem)) continue;
                var width = _cards.ActualWidth - host.Margin.Left - host.Margin.Right;
                if (width > 240) host.Width = width;
            }
        }

        private Canvas _dropOverlay;
        private Border _dropBar;

        private void HideDropBar()
        {
            _dropBar.Visibility = Visibility.Collapsed;
        }

        /// <summary>Trait d'insertion à GAUCHE de la carte visée (le dépôt
        /// insère avant elle), ou à droite de la dernière (dépôt en fin).</summary>
        private void ShowDropBar(Border card, bool after)
        {
            Point origin;
            try
            {
                origin = card.TranslatePoint(
                    new Point(after ? card.ActualWidth + card.Margin.Right + 1
                                    : -card.Margin.Left + 1, 0), _dropOverlay);
            }
            catch { HideDropBar(); return; }
            _dropBar.Height = Math.Max(24, card.ActualHeight);
            Canvas.SetLeft(_dropBar, origin.X);
            Canvas.SetTop(_dropBar, origin.Y);
            _dropBar.Visibility = Visibility.Visible;
        }

        /// <summary>La dernière carte de la rangée des documents, pour marquer
        /// le dépôt « en fin de liste » sur l'espace vide.</summary>
        private Border LastCard()
        {
            var cards = AllCards();
            return cards.Count == 0 ? null : cards[cards.Count - 1];
        }

        public void Load(BinderItem folder, HistoryManager history, Project project)
        {
            if (!ReferenceEquals(_folder, folder)) _selected.Clear();
            _folder = folder;
            _history = history;
            _project = project;
            Rebuild();
        }

        /// <summary>Toutes les cartes du tableau, y compris CELLES DES BOÎTES
        /// de partie (les livres imbriquent des dossiers).</summary>
        private List<Border> AllCards()
        {
            var cards = new List<Border>();
            foreach (var top in _cards.Children)
            {
                var card = top as Border;
                if (card != null && card.Tag is BinderItem) { cards.Add(card); continue; }
                var host = top as Grid; // boîte de partie
                if (host == null || host.Children.Count == 0) continue;
                var boxBorder = host.Children[0] as Border;
                var inner = boxBorder == null ? null : boxBorder.Child as WrapPanel;
                if (inner == null) continue;
                foreach (var child in inner.Children)
                {
                    var nested = child as Border;
                    if (nested != null && nested.Tag is BinderItem) cards.Add(nested);
                }
            }
            return cards;
        }

        private void RefreshSelectionVisuals()
        {
            foreach (var card in AllCards())
            {
                var item = card.Tag as BinderItem;
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
            _selected.Clear();
            _cards.Children.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _folder == item; }

        /// <summary>Redessine les cartes (état/couleur édités dans
        /// l'inspecteur pendant que le tableau est affiché).</summary>
        public void Refresh()
        {
            if (_folder != null) Rebuild();
        }

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
                // Dans un LIVRE, un dossier est une PARTIE : une boîte à bords
                // ronds qui contient les cartes de ses documents.
                if (_folder.Kind == ItemKind.Book && child.Kind == ItemKind.Folder)
                    _cards.Children.Add(BuildFolderBox(child));
                else
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
            UpdateFolderBoxWidths();
        }

        /// <summary>La boîte d'une partie : bordure fine à bords ronds (gris
        /// clair, ou la couleur du dossier avec un fond éclairci), le nom posé
        /// DANS la bordure, les cartes des documents à l'intérieur.</summary>
        private UIElement BuildFolderBox(BinderItem folder)
        {
            var accent = folder.CardColor != null
                ? (Color?)FlowConverter.ParseColor(folder.CardColor) : null;
            var borderBrush = accent.HasValue
                ? (Brush)new SolidColorBrush(accent.Value) : Chrome.Border;
            var fill = accent.HasValue
                ? (Brush)new SolidColorBrush(Chrome.Blend(accent.Value,
                    Chrome.WindowBg.Color, 0.88))
                : Brushes.Transparent;

            // La boîte occupe toute la largeur du tableau (retour à la ligne
            // forcé, cf. UpdateFolderBoxWidths) : les cartes s'y répartissent.
            var inner = new WrapPanel { Margin = new Thickness(2, 8, 2, 2) };
            var count = 0;
            foreach (var child in folder.Children)
            {
                if (child.Kind == ItemKind.PageTemplate) continue;
                inner.Children.Add(BuildCard(child));
                count++;
            }
            if (count == 0)
                inner.Children.Add(new TextBlock
                {
                    Text = "(glissez des documents dans cette partie)",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(14, 10, 14, 10)
                });

            var box = new Border
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Background = fill,
                Margin = new Thickness(0, 9, 0, 0), // place pour la légende
                Padding = new Thickness(4, 8, 4, 4),
                AllowDrop = true,
                Child = inner
            };
            var folderRef = folder;
            box.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent("UniversSaleCard")
                    ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            box.Drop += delegate(object sender, DragEventArgs e)
            {
                // Dépôt sur le fond de la boîte : la carte rejoint la partie.
                HideDropBar();
                var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
                if (dragged == null || dragged == folderRef
                    || folderRef.IsDescendantOf(dragged)) return;
                _history.Run(new MoveItemAction(dragged, folderRef, -1));
                Rebuild();
                var handler = Changed;
                if (handler != null) handler();
                e.Handled = true;
            };

            // Légende : le nom coupe la bordure (fond du tableau derrière).
            var legend = new Border
            {
                Background = Chrome.WindowBg,
                Padding = new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(24, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Partie « " + folder.Title + " » — double-clic pour l'ouvrir",
                Child = new TextBlock
                {
                    Text = folder.Title,
                    Foreground = accent.HasValue
                        ? (Brush)new SolidColorBrush(accent.Value) : Chrome.SoftText,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
            legend.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                var handler = Navigate;
                if (handler != null) handler(folderRef);
            };

            var host = new Grid { Margin = new Thickness(8, 4, 8, 6), Tag = folder };
            host.Children.Add(box);
            host.Children.Add(legend);
            return host;
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
                // Partie : un dossier purement indicatif — la compilation et
                // les folios l'ignorent, le corkboard l'affiche en boîte.
                AddExtraEntry(menu, "Dossier (partie)", "folder",
                    "Regroupe des documents dans une boîte du corkboard — sans "
                    + "effet sur la compilation ni les folios");
                menu.Items.Add(new Separator());
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
                // La sélection et le liseré de divergence colorent la bordure ;
                // la couleur personnelle vit en dégradé dans la barre de titre.
                BorderBrush = selected ? (Brush)Chrome.Accent : Chrome.Border,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(selected ? 7 : 8), // épaisseur compensée
                Tag = item,
                AllowDrop = true
            };

            var layout = new DockPanel();

            var titleBar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(10, 4, 4, 4)
            };
            if (item.CardColor != null)
            {
                // Couleur personnelle : dégradé plein sous le bouton ⋮ à
                // droite, fondu jusqu'à la moitié de la barre de titre.
                var accent = FlowConverter.ParseColor(item.CardColor);
                var fade = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5)
                };
                fade.GradientStops.Add(new GradientStop(
                    Color.FromArgb(0, accent.R, accent.G, accent.B), 0.5));
                fade.GradientStops.Add(new GradientStop(accent, 1.0));
                titleBar.Background = fade;
            }
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

            // Pied de carte : pastille d'état + compteur d'annotations.
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 2, 10, 5)
            };
            if (item.Status != null && TextStatus.Label(item.Status).Length > 0)
            {
                footer.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(
                        FlowConverter.ParseColor(TextStatus.ColorOf(item.Status))),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 1, 5, 0)
                });
                footer.Children.Add(new TextBlock
                {
                    Text = TextStatus.Label(item.Status),
                    Foreground = Chrome.SoftText,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });
            }
            var annotationCount = item.Kind == ItemKind.Text
                ? item.Document.AnnotationOrder(false).Count : 0;
            if (annotationCount > 0)
            {
                footer.Children.Add(new Border
                {
                    Width = 5,
                    Height = 9,
                    CornerRadius = new CornerRadius(2.5),
                    Background = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 1, 5, 0)
                });
                footer.Children.Add(new TextBlock
                {
                    Text = annotationCount + (annotationCount > 1 ? " annotations" : " annotation"),
                    Foreground = Chrome.SoftText,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Annotations de révision dans ce document"
                });
            }
            if (footer.Children.Count > 0)
            {
                DockPanel.SetDock(footer, Dock.Bottom);
                layout.Children.Add(footer);
            }
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
            card.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent("UniversSaleCard")
                    ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                if (e.Effects == DragDropEffects.Move) ShowDropBar(card, false);
            };
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
            // Espace vide : le dépôt enverra la carte en fin de liste.
            if (e.Effects == DragDropEffects.Move)
            {
                var last = LastCard();
                if (last != null) ShowDropBar(last, true);
            }
        }

        private void DropOnCard(BinderItem target, DragEventArgs e)
        {
            HideDropBar();
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null || dragged == target
                || target.IsDescendantOf(dragged)) return;
            // Le dépôt insère AVANT la carte visée, dans SON parent — une carte
            // d'une partie se réordonne dans la partie, une carte externe y entre.
            var parent = target.Parent;
            var index = parent.Children.IndexOf(target);
            var oldIndex = dragged.Parent == parent ? parent.Children.IndexOf(dragged) : -1;
            if (oldIndex >= 0 && oldIndex < index) index--;
            if (oldIndex == index && dragged.Parent == parent) { e.Handled = true; return; }
            _history.Run(new MoveItemAction(dragged, parent, index));
            Rebuild();
            var handler = Changed;
            if (handler != null) handler();
            e.Handled = true;
        }

        private void OnBoardDrop(object sender, DragEventArgs e)
        {
            HideDropBar();
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null || _folder.IsDescendantOf(dragged)) return;
            // Espace vide : en fin de liste du tableau (une carte d'une partie
            // en SORT).
            var newIndex = dragged.Parent == _folder
                ? _folder.Children.Count - 1 : -1;
            if (dragged.Parent == _folder
                && _folder.Children.IndexOf(dragged) == newIndex) return;
            _history.Run(new MoveItemAction(dragged, _folder, newIndex));
            Rebuild();
            var handler = Changed;
            if (handler != null) handler();
            e.Handled = true;
        }

        /// <summary>Retrouve un élément par id parmi les DESCENDANTS du dossier
        /// affiché (les cartes des parties comprises).</summary>
        private BinderItem FindChild(string id)
        {
            if (_folder == null || id == null) return null;
            return FindIn(_folder, id);
        }

        private static BinderItem FindIn(BinderItem parent, string id)
        {
            foreach (var child in parent.Children)
            {
                if (child.Id == id) return child;
                var nested = FindIn(child, id);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
