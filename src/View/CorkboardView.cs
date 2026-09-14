using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Correction;
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
        private StackPanel _planActions; // racine Plans : « + Nouveau plan » (b35)
        private StackPanel _bookActions;   // livre : Nouvel écrit / Nouvelle partie / Nouvelle liminaire (14/09)
        private StackPanel _folderActions; // dossier d'Écrits : Nouvel écrit / Nouveau sous-dossier (14/09)
        private StackPanel _writingsActions; // racine Écrits : Nouvel écrit / dossier / livre (12/09)
        private StackPanel _researchActions; // racine Recherche : Importer des fichiers (12/09)
        private BinderItem _folder;
        private HistoryManager _history;
        private Project _project; // image store lookups

        private BinderItem _dragCandidate;
        private Point _dragStart;

        // Cartes sélectionnables (multi avec Ctrl) — le double-clic ouvre.
        private readonly HashSet<string> _selected = new HashSet<string>();

        // LES FILTRES (batch 28) : tri signes/pages asc-desc, statut,
        // annotations, extras — affichage seulement, le modèle ne bouge pas.
        private ToggleButton _filterToggle;
        private Border _filterBar;
        private ComboBox _filterSort, _filterStatus, _filterAnnotations, _filterExtras;
        private bool _filterSyncing;

        /// <summary>Compte de pages d'un texte (tri « Pages ») — câblé par la
        /// coquille (cache de composition de MainWindow) ; null = tri neutre.</summary>
        public Func<BinderItem, int> PageCounter;

        private readonly WrapPanel _templateCards; // section gabarits (livres)
        private readonly Border _templateSeparator;
        private readonly StackPanel _documentActions; // « Nouveau document ▾ »

        public event Action<BinderItem> Navigate;
        public event Action Changed; // synopsis edited or cards reordered
        public event Action<BinderItem> ExportRequested;      // menu ⋮
        public event Action<BinderItem> DeleteRequested;      // menu ⋮ (corbeille)
        // Le menu de la Pile pour le même item (14/09) : les tuiles offrent
        // épingler à l'accueil / au rail, renommer, icône, image, supprimer
        // (avec confirmation) — le corkboard n'ajoute que ce qui est à lui.
        public Func<BinderItem, ContextMenu> MenuProvider;
        public event Action<BinderItem> RenameRequested;      // menu ⋮ (b43)
        public event Action<BinderItem, bool> CardImageRequested; // image de tuile (12/09) : (élément, retirer)
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
            _templateCards = new WrapPanel { Margin = new Thickness(16, 12, 16, 4), Visibility = Visibility.Collapsed };
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
            // Livre (14/09) : « Nouvel écrit » (principal), « Nouvelle partie »,
            // « Nouvelle liminaire » (le menu des pages extra) — tout en haut.
            _bookActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0),
                Visibility = Visibility.Collapsed
            };
            var bookText = Buttons.IconText("plus-bold", "Nouvel écrit",
                "Un écrit à la fin du livre, au gabarit du livre", Buttons.Bar, Buttons.Look.Primary);
            bookText.Click += delegate { RequestNewDocument(null); };
            _bookActions.Children.Add(bookText);
            var bookPart = Buttons.IconText("folder-bold", "Nouvelle partie",
                "Une partie : un dossier indicatif, traversé par la compilation et les folios", Buttons.Bar, Buttons.Look.Outline);
            bookPart.Margin = new Thickness(8, 0, 0, 0);
            bookPart.Click += delegate { RequestNewDocument("folder"); };
            _bookActions.Children.Add(bookPart);
            var bookExtra = Buttons.IconText("file-dashed-bold", "Nouvelle liminaire",
                "Pages extra : vierge, pages de titre, table des matières, page éditeur…", Buttons.Bar, Buttons.Look.Outline);
            bookExtra.Margin = new Thickness(8, 0, 0, 0);
            bookExtra.Click += delegate { OpenExtraMenu(bookExtra); };
            _bookActions.Children.Add(bookExtra);
            // Dossier d'Écrits (14/09) : « Nouvel écrit » (principal), « Nouveau sous-dossier ».
            _folderActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0),
                Visibility = Visibility.Collapsed
            };
            var folderText = Buttons.IconText("plus-bold", "Nouvel écrit",
                "Un écrit dans ce dossier", Buttons.Bar, Buttons.Look.Primary);
            folderText.Click += delegate { RequestNewDocument("root-text"); };
            _folderActions.Children.Add(folderText);
            var subFolder = Buttons.IconText("folder-bold", "Nouveau sous-dossier",
                "Un dossier dans ce dossier", Buttons.Bar, Buttons.Look.Outline);
            subFolder.Margin = new Thickness(8, 0, 0, 0);
            subFolder.Click += delegate { RequestNewDocument("root-folder"); };
            _folderActions.Children.Add(subFolder);
            _planActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0),
                Visibility = Visibility.Collapsed
            };
            var newPlan = Buttons.IconText("plus-bold", "Nouveau plan",
                "Un plan : colonnes, briques d'intensité, notes", Buttons.Bar, Buttons.Look.Primary); // principal (14/09)
            newPlan.Click += delegate { RequestNewDocument("plan"); };
            _planActions.Children.Add(newPlan);
            // Racine Écrits (12/09) : un bouton principal et deux secondaires.
            _writingsActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0),
                Visibility = Visibility.Collapsed
            };
            var newText = Buttons.IconText("plus-bold", "Nouvel écrit",
                "Un nouvel écrit, hors livre", Buttons.Bar, Buttons.Look.Primary);
            newText.Click += delegate { RequestNewDocument("root-text"); };
            _writingsActions.Children.Add(newText);
            var newFolder = Buttons.IconText("folder-bold", "Nouveau dossier",
                "Un dossier pour ranger des écrits", Buttons.Bar, Buttons.Look.Outline);
            newFolder.Margin = new Thickness(8, 0, 0, 0);
            newFolder.Click += delegate { RequestNewDocument("root-folder"); };
            _writingsActions.Children.Add(newFolder);
            var newBook = Buttons.IconText("book-bold", "Nouveau livre",
                "Un livre : gabarit, chapitres, compilation", Buttons.Bar, Buttons.Look.Outline);
            newBook.Margin = new Thickness(8, 0, 0, 0);
            newBook.Click += delegate { RequestNewDocument("root-book"); };
            _writingsActions.Children.Add(newBook);
            // Racine Recherche (12/09) : l'import en bouton principal.
            _researchActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0),
                Visibility = Visibility.Collapsed
            };
            var import = Buttons.IconText("file-arrow-down-bold", "Importer des fichiers",
                "Ajouter des fichiers (images, PDF, documents…) dans Recherche", Buttons.Bar, Buttons.Look.Primary);
            import.Click += delegate { RequestNewDocument("import"); };
            _researchActions.Children.Add(import);
            _cards = new WrapPanel { Margin = new Thickness(16, 8, 16, 16) };
            var layout = new StackPanel();
            layout.Children.Add(_bookActions);
            layout.Children.Add(_folderActions);
            layout.Children.Add(_planActions);
            layout.Children.Add(_writingsActions);
            layout.Children.Add(_researchActions);
            layout.Children.Add(BuildFilterHeader());
            layout.Children.Add(BuildFilterBar());
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

        /// <summary>Vrai si le clic est parti d'une CARTE (Border à Tag
        /// BinderItem) — faux sur le fond, Y COMPRIS le fond d'une boîte de
        /// partie : son Grid hôte porte aussi le BinderItem et avalait la
        /// désélection (la « bordure qui persiste » du batch 28).</summary>
        private static bool IsWithinCard(DependencyObject source)
        {
            while (source != null)
            {
                var element = source as Border;
                if (element != null && element.Tag is BinderItem) return true;
                source = source is Visual
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        // ============================================================= filtres

        /// <summary>Le bouton « Filtres », au bord droit, au-dessus des
        /// cartes — visible dès que le dossier contient des textes.</summary>
        private UIElement BuildFilterHeader()
        {
            // Icône + libellé (batch 40) : le tri seul ne dit pas « filtrer ».
            _filterToggle = Buttons.IconTextToggle("sort-descending-bold", "Filtres",
                "Trier et filtrer les textes de ce tableau "
                    + "(affichage seulement — l'ordre réel ne bouge pas)", Buttons.Bar);
            _filterToggle.HorizontalAlignment = HorizontalAlignment.Right;
            _filterToggle.Margin = new Thickness(24, 8, 24, 0);
            _filterToggle.Visibility = Visibility.Collapsed;
            _filterToggle.Click += delegate
            {
                _filterBar.Visibility = _filterToggle.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            return _filterToggle;
        }

        /// <summary>Le bandeau déroulé : tri, statut, annotations, extras, et
        /// le bouton qui efface tout.</summary>
        private UIElement BuildFilterBar()
        {
            _filterBar = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(24, 6, 24, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Visibility = Visibility.Collapsed
            };
            var row = new WrapPanel();

            row.Children.Add(FilterLabel("Tri :"));
            _filterSort = FilterCombo(new[]
            {
                "—", "Signes ↑", "Signes ↓", "Pages ↑", "Pages ↓"
            }, 92);
            row.Children.Add(_filterSort);

            row.Children.Add(FilterLabel("Statut :"));
            _filterStatus = new ComboBox
            {
                Width = 110,
                Margin = new Thickness(0, 0, 12, 0)
            };
            _filterStatus.Items.Add("Tous");
            foreach (var key in TextStatus.Keys)
                _filterStatus.Items.Add(TextStatus.Label(key));
            _filterStatus.SelectedIndex = 0;
            _filterStatus.SelectionChanged += OnFilterChanged;
            row.Children.Add(_filterStatus);

            row.Children.Add(FilterLabel("Annotations :"));
            _filterAnnotations = FilterCombo(new[]
            { "Peu importe", "Avec", "Sans" }, 96);
            row.Children.Add(_filterAnnotations);

            row.Children.Add(FilterLabel("Extras :"));
            _filterExtras = FilterCombo(new[]
            { "Peu importe", "Extras", "Hors extras" }, 96);
            row.Children.Add(_filterExtras);

            var clear = new Button
            {
                Content = "Effacer les filtres",
                Padding = new Thickness(8, 1, 8, 1),
                Focusable = false,
                ToolTip = "Tout remettre à neutre"
            };
            clear.Click += delegate
            {
                _filterSyncing = true;
                _filterSort.SelectedIndex = 0;
                _filterStatus.SelectedIndex = 0;
                _filterAnnotations.SelectedIndex = 0;
                _filterExtras.SelectedIndex = 0;
                _filterSyncing = false;
                Rebuild();
            };
            row.Children.Add(clear);

            _filterBar.Child = row;
            return _filterBar;
        }

        private static TextBlock FilterLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
        }

        private ComboBox FilterCombo(string[] entries, double width)
        {
            var combo = new ComboBox { Width = width, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var entry in entries) combo.Items.Add(entry);
            combo.SelectedIndex = 0;
            combo.SelectionChanged += OnFilterChanged;
            return combo;
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filterSyncing && _folder != null) Rebuild();
        }

        private bool FiltersActive
        {
            get
            {
                return _filterSort != null
                    && (_filterSort.SelectedIndex > 0
                        || _filterStatus.SelectedIndex > 0
                        || _filterAnnotations.SelectedIndex > 0
                        || _filterExtras.SelectedIndex > 0);
            }
        }

        private bool PassesFilters(BinderItem text)
        {
            if (_filterStatus.SelectedIndex > 0
                && text.Status != TextStatus.Keys[_filterStatus.SelectedIndex - 1])
                return false;
            if (_filterAnnotations.SelectedIndex > 0)
            {
                var annotated = text.Document.AnnotationOrder(false).Count > 0;
                if (annotated != (_filterAnnotations.SelectedIndex == 1)) return false;
            }
            if (_filterExtras.SelectedIndex > 0)
            {
                var extra = text.IsExtraPage || text.IsToc;
                if (extra != (_filterExtras.SelectedIndex == 1)) return false;
            }
            return true;
        }

        /// <summary>Applique filtres et tri aux TEXTES d'un conteneur — les
        /// dossiers, fiches et médias passent tels quels. Avec un tri actif,
        /// les textes se rangent après le reste, triés entre eux ; sans tri,
        /// l'ordre du modèle est respecté (le modèle ne bouge JAMAIS).</summary>
        private List<BinderItem> ArrangeChildren(IEnumerable<BinderItem> children)
        {
            var texts = new List<BinderItem>();
            var others = new List<BinderItem>();
            var mixed = new List<BinderItem>();
            foreach (var child in children)
            {
                if (child.Kind == ItemKind.PageTemplate) continue;
                if (child.Kind == ItemKind.Text)
                {
                    if (!PassesFilters(child)) continue;
                    texts.Add(child);
                    mixed.Add(child);
                }
                else
                {
                    others.Add(child);
                    mixed.Add(child);
                }
            }
            var sort = _filterSort == null ? 0 : _filterSort.SelectedIndex;
            if (sort == 0) return mixed;
            var ascending = sort == 1 || sort == 3;
            var byPages = sort >= 3;
            var measures = new Dictionary<string, int>();
            foreach (var text in texts)
                measures[text.Id] = byPages
                    ? (PageCounter != null ? PageCounter(text) : 1)
                    : TextStats.Compute(text.Document.ToPlainText()).Sec;
            texts.Sort(delegate(BinderItem a, BinderItem b)
            {
                var compare = measures[a.Id].CompareTo(measures[b.Id]);
                return ascending ? compare : -compare;
            });
            var result = new List<BinderItem>(others);
            result.AddRange(texts);
            return result;
        }

        /// <summary>Chaque boîte de partie occupe la largeur du tableau : dans
        /// le WrapPanel, elle force ainsi son propre retour à la ligne.</summary>
        private void UpdateFolderBoxWidths()
        {
            foreach (var child in _cards.Children)
            {
                var host = child as Grid;
                // Les boîtes de partie, et les titres de section de la racine
                // Écrits (12/09), occupent toute la largeur.
                if (host == null || !(host.Tag is BinderItem || (host.Tag as string) == "section")) continue;
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
            // Toute NAVIGATION repart sans sélection (batch 28) : revenir sur
            // le même dossier gardait des bordures accent fantômes. Les
            // rebuilds internes (Refresh) préservent, eux, la sélection.
            _selected.Clear();
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
                // PIÈGE (batch 33) : la divergence se RECALCULE depuis le
                // modèle — la sonder à la couleur de la bordure confondait
                // l'accent « Orange » des préférences avec le liseré, et la
                // carte précédente gardait son contour pour toujours.
                if (!IsDivergent(item))
                    card.BorderBrush = selected ? (Brush)Chrome.Accent : Chrome.Border;
                card.BorderThickness = new Thickness(selected ? 2 : 1);
                card.Margin = new Thickness(selected ? 7 : 8);
            }
        }

        /// <summary>Vrai quand un texte du livre ne suit pas le gabarit du
        /// livre (liseré orange, menu « Appliquer le gabarit »).</summary>
        private bool IsDivergent(BinderItem item)
        {
            if (item == null || item.Kind != ItemKind.Text || _project == null || _folder == null)
                return false;
            var book = _folder.EnclosingBook();
            if (book == null || book.Book == null) return false;
            var effective = item.Page ?? _project.Page;
            return !effective.SameLayout(book.Book.Template);
        }

        /// <summary>The ⋮ options menu, top right of every card.</summary>
        private UIElement BuildCardMenu(BinderItem item)
        {
            var button = Buttons.Icon("dots-three-vertical-bold", "Options de la carte", Buttons.Compact, Buttons.Look.Calm);
            button.Margin = new Thickness(4, 0, 0, 0);
            button.Foreground = Chrome.SoftText;
            DockPanel.SetDock(button, Dock.Right);
            var itemRef = item;
            button.Click += delegate
            {
                var menu = BuildCardOptionsMenu(itemRef);
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            };
            return button;
        }

        /// <summary>Le menu d'options d'une carte — servi par le bouton ⋮ ET
        /// par le clic droit sur la carte (batch 28).</summary>
        private ContextMenu BuildCardOptionsMenu(BinderItem itemRef)
        {
            {
                var menu = new ContextMenu();
                var provided = MenuProvider == null ? null : MenuProvider(itemRef);
                if (provided == null)
                {
                    var rename = new MenuItem { Header = "Renommer…" };
                    rename.Click += delegate
                    {
                        var handler = RenameRequested;
                        if (handler != null) handler(itemRef);
                    };
                    menu.Items.Add(rename);
                }
                if (provided == null && (itemRef.Kind == ItemKind.Text || itemRef.Kind == ItemKind.Book))
                {
                    // L'image de tuile (12/09) : elle remplace l'extrait.
                    var picture = new MenuItem
                    {
                        Header = itemRef.ImageId == null ? "Image de la carte…" : "Changer l'image de la carte…"
                    };
                    picture.Click += delegate
                    {
                        var handler = CardImageRequested;
                        if (handler != null) handler(itemRef, false);
                    };
                    menu.Items.Add(picture);
                    if (itemRef.ImageId != null)
                    {
                        var clear = new MenuItem { Header = "Retirer l'image de la carte" };
                        clear.Click += delegate
                        {
                            var handler = CardImageRequested;
                            if (handler != null) handler(itemRef, true);
                        };
                        menu.Items.Add(clear);
                    }
                }
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
                if (IsDivergent(itemRef))
                {
                    var bookRef = _folder.EnclosingBook();
                    var applyBook = new MenuItem { Header = "Appliquer le gabarit du livre au document" };
                    applyBook.Click += delegate
                    {
                        if (itemRef.Page == null) itemRef.Page = bookRef.Book.Template.Clone();
                        else itemRef.Page.ApplyLayout(bookRef.Book.Template);
                        Rebuild();
                        var changed = Changed;
                        if (changed != null) changed();
                    };
                    menu.Items.Add(applyBook);
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
                if (provided != null)
                {
                    // Les entrées de la Pile, à la suite (un MenuItem n'a qu'un parent).
                    var items = new List<object>();
                    foreach (var entry in provided.Items) items.Add(entry);
                    provided.Items.Clear();
                    if (menu.Items.Count > 0 && items.Count > 0) menu.Items.Add(new Separator());
                    foreach (var entry in items)
                        if (!(entry is Separator && (menu.Items.Count == 0 || menu.Items[menu.Items.Count - 1] is Separator)))
                            menu.Items.Add(entry);
                    return menu;
                }
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                var delete = new MenuItem { Header = "Supprimer" };
                delete.Click += delegate
                {
                    var handler = DeleteRequested;
                    if (handler != null) handler(itemRef);
                };
                menu.Items.Add(delete);
                return menu;
            }
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
            if (_folder == null) return;
            // Une carte supprimée (corbeille) ne reste pas « sélectionnée ».
            _selected.RemoveWhere(delegate(string id)
            {
                var item = _project == null ? null : _project.FindById(id);
                return item == null || !item.IsDescendantOf(_folder);
            });
            Rebuild();
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
            if (_folder == null) return;
            _bookActions.Visibility = _folder.Kind == ItemKind.Book ? Visibility.Visible : Visibility.Collapsed;
            _folderActions.Visibility = _folder.Kind == ItemKind.Folder && _folder.RootCategory().CategoryKey == Project.KeyWritings
                ? Visibility.Visible : Visibility.Collapsed;
            _planActions.Visibility = IsRoot(Project.KeyPlans) ? Visibility.Visible : Visibility.Collapsed;
            _writingsActions.Visibility = IsRoot(Project.KeyWritings) ? Visibility.Visible : Visibility.Collapsed;
            _researchActions.Visibility = IsRoot(Project.KeyResearch) ? Visibility.Visible : Visibility.Collapsed;

            // Livres (14/09) : deux SECTIONS titrées comme les catégories de
            // fiches — « Gabarits » (les cartes de gabarit et leurs boutons),
            // puis « Écrits » (les documents et les parties).
            if (_folder.Kind == ItemKind.Book)
            {
                var gabarits = 0;
                foreach (var child in _folder.Children)
                    if (child.Kind == ItemKind.PageTemplate) gabarits++;
                AddSectionHeader("Gabarits", gabarits);
                foreach (var child in _folder.Children)
                    if (child.Kind == ItemKind.PageTemplate)
                        _cards.Children.Add(BuildTemplateCard(child));
                _cards.Children.Add(BuildTemplateActions());
                var texts = 0;
                foreach (var child in _folder.Children)
                    if (child.Kind != ItemKind.PageTemplate) texts++;
                AddSectionHeader("Écrits", texts);
            }

            // Le bouton Filtres n'apparaît que devant des textes (récursif :
            // les parties d'un livre comptent).
            var hasTexts = ContainsTexts(_folder);
            _filterToggle.Visibility = hasTexts ? Visibility.Visible : Visibility.Collapsed;
            if (!hasTexts) _filterBar.Visibility = Visibility.Collapsed;
            else if (_filterToggle.IsChecked == true)
                _filterBar.Visibility = Visibility.Visible;

            var documents = 0;
            var arranged = ArrangeChildren(_folder.Children);
            if (IsRoot(Project.KeyWritings))
            {
                // La racine Écrits (12/09) : trois sections — les livres, les
                // dossiers, les écrits seuls (affichage seulement : l'ordre du
                // modèle, et donc de la Pile, ne bouge pas).
                var books = new List<BinderItem>();
                var folders = new List<BinderItem>();
                var loose = new List<BinderItem>();
                foreach (var child in arranged)
                {
                    if (child.Kind == ItemKind.Book) books.Add(child);
                    else if (child.Kind == ItemKind.Folder) folders.Add(child);
                    else loose.Add(child);
                }
                documents += AddSection("Livres", books);
                documents += AddSection("Dossiers", folders);
                documents += AddSection("Écrits", loose);
            }
            else foreach (var child in arranged)
            {
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
                    Text = FiltersActive
                        ? "(aucun texte ne passe les filtres)"
                        : _folder.Kind == ItemKind.Book
                            ? "(livre sans document)"
                        : IsRoot(Project.KeyPlans)
                            ? "(aucun plan — clic droit sur « Plans » dans la Pile, ou « + Nouveau plan »)"
                        : IsRoot(Project.KeyWritings)
                            ? "(rien encore — « Nouvel écrit » ou « Nouveau livre » pour commencer)"
                        : IsRoot(Project.KeyResearch)
                            ? "(rien encore — « Importer des fichiers » pour commencer)"
                            : "(dossier vide)",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(12)
                });
            UpdateFolderBoxWidths();
        }

        private bool IsRoot(string key)
        {
            return _folder != null && _folder.IsCategory && _folder.CategoryKey == key;
        }

        /// <summary>Une section de la racine Écrits : un titre sur toute la
        /// largeur (retour à la ligne forcé, cf. UpdateFolderBoxWidths), puis
        /// les cartes. Rien si la section est vide. Rend le nombre de cartes.</summary>
        private int AddSection(string title, List<BinderItem> items)
        {
            if (items.Count == 0) return 0;
            AddSectionHeader(title, items.Count);
            foreach (var item in items) _cards.Children.Add(BuildCard(item));
            return items.Count;
        }

        /// <summary>Le titre d'une section (racine Écrits, livre) : sur toute
        /// la largeur, le compte à côté, un filet dessous.</summary>
        private void AddSectionHeader(string title, int count)
        {
            var header = new Grid { Tag = "section", Margin = new Thickness(8, 12, 8, 4) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var line = new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 6, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            header.Children.Add(line);
            header.Children.Add(row);
            _cards.Children.Add(header);
        }

        private static bool ContainsTexts(BinderItem folder)
        {
            foreach (var child in folder.Children)
            {
                if (child.Kind == ItemKind.Text) return true;
                if (child.Kind == ItemKind.Folder && ContainsTexts(child)) return true;
            }
            return false;
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
            foreach (var child in ArrangeChildren(folder.Children))
            {
                inner.Children.Add(BuildCard(child));
                count++;
            }
            if (count == 0)
                inner.Children.Add(new TextBlock
                {
                    Text = FiltersActive
                        ? "(aucun texte de cette partie ne passe les filtres)"
                        : "(glissez des documents dans cette partie)",
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
            // Les LISIÈRES de la boîte (bande haute, bande basse) déposent
            // AVANT ou APRÈS la partie dans le livre — une carte peut donc
            // passer devant une partie (batch 35) ; le fond de la boîte,
            // lui, fait entrer la carte dans la partie.
            box.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent("UniversSaleCard")
                    ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                if (e.Effects != DragDropEffects.Move) return;
                var edge = BoxEdge(box, e.GetPosition(box));
                if (edge == 0) HideDropBar();
                else ShowDropBar(box, edge > 0);
            };
            box.Drop += delegate(object sender, DragEventArgs e)
            {
                HideDropBar();
                var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
                if (dragged == null || dragged == folderRef
                    || folderRef.IsDescendantOf(dragged)) return;
                var edge = BoxEdge(box, e.GetPosition(box));
                if (edge != 0) MoveBeside(dragged, folderRef, edge > 0);
                else
                {
                    // Dépôt sur le fond de la boîte : la carte rejoint la partie.
                    _history.Run(new MoveItemAction(dragged, folderRef, -1));
                    Rebuild();
                    var handler = Changed;
                    if (handler != null) handler();
                }
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
            // La légende (le nom de la partie) dépose AVANT la partie.
            legend.AllowDrop = true;
            legend.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent("UniversSaleCard") ? DragDropEffects.Move : DragDropEffects.None;
                if (e.Effects == DragDropEffects.Move) ShowDropBar(box, false);
                e.Handled = true;
            };
            legend.Drop += delegate(object sender, DragEventArgs e)
            {
                HideDropBar();
                var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
                if (dragged == null || dragged == folderRef || folderRef.IsDescendantOf(dragged)) return;
                MoveBeside(dragged, folderRef, false);
                e.Handled = true;
            };

            var host = new Grid { Margin = new Thickness(8, 4, 8, 6), Tag = folder };
            host.Children.Add(box);
            host.Children.Add(legend);
            return host;
        }

        /// <summary>-1 = bande haute (avant la partie), +1 = bande basse
        /// (après), 0 = le fond (dans la partie).</summary>
        private static int BoxEdge(Border box, Point position)
        {
            const double band = 16;
            if (position.Y < band) return -1;
            if (position.Y > box.ActualHeight - band) return 1;
            return 0;
        }

        /// <summary>Place la carte juste avant ou juste après la partie, dans
        /// le parent de celle-ci (public par réflexion : la sonde s'en sert).</summary>
        private void MoveBeside(BinderItem dragged, BinderItem folder, bool after)
        {
            var parent = folder.Parent;
            if (parent == null || dragged == null || dragged == folder || folder.IsDescendantOf(dragged)) return;
            var index = parent.Children.IndexOf(folder) + (after ? 1 : 0);
            var oldIndex = dragged.Parent == parent ? parent.Children.IndexOf(dragged) : -1;
            if (oldIndex >= 0 && oldIndex < index) index--;
            if (oldIndex == index && dragged.Parent == parent) return;
            _history.Run(new MoveItemAction(dragged, parent, index));
            Rebuild();
            var handler = Changed;
            if (handler != null) handler();
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
            CardLift.Attach(card); // soulèvement au survol (b35)
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
            arrow.Click += delegate { OpenExtraMenu(arrow); };
            _documentActions.Children.Add(arrow);
        }

        /// <summary>Le menu des pages extra (« Nouvelle liminaire », 14/09) :
        /// partie, document vierge, pages de titre, TdM, page éditeur…</summary>
        private void OpenExtraMenu(UIElement anchor)
        {
            {
                var menu = new ContextMenu { PlacementTarget = anchor };
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
            }
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
            else if ((item.Kind == ItemKind.Sheet || item.Kind == ItemKind.Text
                || item.Kind == ItemKind.Book) && _project != null)
            {
                var image = _project.FindImage(item.ImageId);
                if (image != null) pictureBytes = image.Bytes;
            }
            // L'image de tuile d'un écrit ou d'un livre (12/09) REMPLACE
            // l'extrait du texte et les notes ; la fiche garde image + notes.
            var imageOnly = false;
            if (pictureBytes != null)
            {
                var thumb = MediaView.TryImage(pictureBytes, 200);
                if (thumb != null)
                {
                    imageOnly = item.Kind == ItemKind.Text || item.Kind == ItemKind.Book;
                    body.Children.Add(new Image
                    {
                        Source = thumb,
                        Stretch = Stretch.Uniform,
                        MaxHeight = imageOnly ? 130 : 90,
                        Margin = new Thickness(8, 8, 8, imageOnly ? 8 : 0)
                    });
                }
            }

            // Card text, read-only: notes first; else the beginning of the text
            // for written documents; sheets without notes stay blank.
            var text = imageOnly ? "" : (item.Notes ?? "").Trim();
            if (item.Kind == ItemKind.Plan)
            {
                // Une carte de plan (batch 35) : sa couleur et son étendue.
                var columns = item.Plan == null ? 0 : item.Plan.Columns.Count;
                var bricks = 0;
                if (item.Plan != null) foreach (var column in item.Plan.Columns) bricks += column.Entries.Count;
                text = columns == 0 ? "Plan vide"
                    : columns + (columns > 1 ? " colonnes" : " colonne") + " · " + bricks + (bricks > 1 ? " briques" : " brique");
            }
            if (text.Length == 0 && item.Kind == ItemKind.Text && !imageOnly)
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
            // Clic droit : les mêmes options que le bouton ⋮ (batch 28). Une
            // carte divergente garde son ContextMenu propre (« appliquer le
            // gabarit du livre »), qui prime.
            if (!item.IsCategory)
                card.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    if (card.ContextMenu != null) return;
                    var menu = BuildCardOptionsMenu(item);
                    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    menu.IsOpen = true;
                    e.Handled = true;
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
                if (IsDivergent(item))
                {
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 126, 34));
                    card.ToolTip = "Ce document ne suit pas le gabarit du livre.";
                    // « Appliquer le gabarit du livre » vit désormais dans le
                    // menu commun de la carte (14/09) : un ContextMenu propre
                    // cachait les épingles et le reste.
                }
            }
            CardLift.Attach(card); // soulèvement au survol (b35)
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
