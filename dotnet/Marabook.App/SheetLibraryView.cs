using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.History;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>La bibliothèque de fiches (batch 31) — la vue de la catégorie
    /// « Fiches » de la Pile : une RANGÉE par catégorie de fiches, séparées
    /// par des filets ; dans chaque rangée, les cartes (photo en haut, nom en
    /// dessous, emplacement réservé sans photo). Depuis chaque rangée : créer
    /// une fiche, modifier la catégorie (nom, modèle de base — une fiche dont
    /// le modèle diffère porte une puce). Une barre de recherche filtre les
    /// fiches par nom, toutes catégories confondues.</summary>
    public class SheetLibraryView : DockPanel
    {
        private Project _project;
        private HistoryManager _history;
        private BinderItem _scope; // un dossier de Fiches (14/09), null = la racine
        private readonly TextBox _searchBox;
        private readonly StackPanel _rows;
        private readonly ScrollViewer _scroll;
        private readonly Button _back;
        private readonly TextBlock _scopeLabel;
        // La rangée des PASTILLES (1.0.4) : une par catégorie, avec son compte
        // — un filtre quand elle a des fiches, un raccourci « créer » quand
        // elle est vide ; les catégories vides n'ont plus de rangée. Le tri
        // vaut pour la session.
        private readonly WrapPanel _chips;
        private readonly ComboBox _sortBox;
        private string _filterCategoryId; // null = toutes ; UncategorizedKey = les sans-catégorie
        private const string UncategorizedKey = "~sans-categorie";
        private static int _sortIndex;
        private static readonly string[] SortLabels = { "Nom A → Z", "Nom Z → A", "Couleur", "Portrait d'abord" };

        /// <summary>Le menu contextuel d'une tuile : celui de la Pile pour le
        /// même item (BinderView.BuildContextMenu), posé par la coquille.</summary>
        public Func<BinderItem, ContextMenu> MenuProvider;
        // Un clic simple sur une tuile de fiche (29/09) : le Général du rail
        // la montre sans l'ouvrir (le double-clic ouvre).
        public event Action<BinderItem> CardSelected;
        // La sélection multiple des tuiles (1.0.3) : Ctrl+clic, bordure
        // d'accent ; la liste à chaque changement ; le menu du lot vient de
        // la coquille ; Suppr sur plusieurs fiches.
        private readonly HashSet<string> _selected = new HashSet<string>();
        private readonly Dictionary<string, Border> _cardsById = new Dictionary<string, Border>();
        public event Action<List<BinderItem>> SelectionChanged;
        public Func<List<BinderItem>, ContextMenu> BatchMenuProvider;
        public event Action<List<BinderItem>> DeleteManyRequested;

        public event Action<BinderItem> Navigate; // ouvrir une fiche
        public event Action Changed;              // structure/projet modifiés
        public event Action<string> AchievementEvent; // succès à événement (12/09)

        private static bool IsWithinCard(Visual source)
        {
            while (source != null)
            {
                var border = source as Border;
                if (border != null && border.Tag is BinderItem) return true;
                source = source.GetVisualParent();
            }
            return false;
        }

        /// <summary>Les fiches choisies, dans l'ordre de la Pile.</summary>
        public List<BinderItem> SelectedItems()
        {
            var result = new List<BinderItem>();
            if (_project == null || _selected.Count == 0) return result;
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && _selected.Contains(item.Id)) result.Add(item);
            return result;
        }

        private void AnnounceSelection(BinderItem single)
        {
            RefreshSelectionVisuals();
            var chosen = CardSelected;
            if (chosen != null) chosen(single);
            var changed = SelectionChanged;
            if (changed != null) changed(SelectedItems());
        }

        private void RefreshSelectionVisuals()
        {
            foreach (var pair in _cardsById) PaintSelection(pair.Value, _selected.Contains(pair.Key));
        }

        /// <summary>Choisie : bordure d'accent de 2 px, la marge compense.</summary>
        private static void PaintSelection(Border card, bool selected)
        {
            card.BorderBrush = selected ? (IBrush)Chrome.Accent : Chrome.Border;
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.Margin = selected ? new Thickness(-1, -1, 9, 9) : new Thickness(0, 0, 10, 10);
        }

        public void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            AnnounceSelection(null);
        }

        /// <summary>Sonde (1.0.5) : les ids des sous-catégories dont la boîte est posée.</summary>
        internal List<string> SubBoxIdsForProbe()
        {
            var ids = new List<string>();
            foreach (var child in _rows.Children)
            {
                var box = child as Border;
                if (box != null && box.Tag is string) ids.Add((string)box.Tag);
            }
            return ids;
        }

        /// <summary>La sonde : choisir ces fiches comme Ctrl+clic l'aurait fait.</summary>
        internal void SelectForProbe(IEnumerable<string> ids)
        {
            _selected.Clear();
            foreach (var id in ids) _selected.Add(id);
            AnnounceSelection(null);
        }

        private bool HasAnySheet()
        {
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && item.RootCategory() != null
                    && item.RootCategory().CategoryKey != Project.KeyTrash) return true;
            return false;
        }

        public SheetLibraryView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;
            // Un clic dans le vide (ou Échap) désélectionne ; Suppr envoie le
            // lot à la corbeille (1.0.3).
            PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                Focus();
                if (_selected.Count == 0 || IsWithinCard(e.Source as Visual)) return;
                _selected.Clear();
                AnnounceSelection(null);
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (_selected.Count == 0) return;
                if (e.Key == Key.Escape) { _selected.Clear(); AnnounceSelection(null); e.Handled = true; return; }
                if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && _selected.Count > 1)
                {
                    var many = DeleteManyRequested;
                    if (many != null) { e.Handled = true; many(SelectedItems()); }
                }
            };

            // — La rangée du haut, alignée sur celle d'Écrits (14/09) : pas de
            // barre, les boutons à gauche (« Nouvelle fiche » en principal,
            // puis catégorie et modèles), la recherche contre le bord droit.
            var toolbar = new DockPanel { Margin = new Thickness(24, TopGap, 24, 0) };
            SetDock(toolbar, Dock.Top);
            _searchBox = new TextBox { [ToolTip.TipProperty] = "Rechercher une fiche par nom, toutes catégories confondues" };
            _searchBox.TextChanged += delegate
            {
                RebuildRows();
                // « Crétin des alpes » (12/09) : chercher une fiche sans en avoir.
                if ((_searchBox.Text ?? "").Trim().Length > 0 && _project != null && !HasAnySheet())
                {
                    var handler = AchievementEvent;
                    if (handler != null) handler(Achievements.Cretin);
                }
            };
            var search = SearchField(_searchBox);
            DockPanel.SetDock(search, Dock.Right);
            toolbar.Children.Add(search);
            // Le tri (1.0.4), après les boutons — comme les sélecteurs du
            // Dictionnaire : nom, couleur, portrait.
            _sortBox = new ComboBox
            {
                FontSize = 12,
                MinWidth = 150,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = "L'ordre des cartes dans chaque catégorie"
            };
            foreach (var label in SortLabels) _sortBox.Items.Add(label);
            _sortBox.SelectedIndex = _sortIndex;
            _sortBox.SelectionChanged += delegate
            {
                if (_sortBox.SelectedIndex < 0 || _sortBox.SelectedIndex == _sortIndex) return;
                _sortIndex = _sortBox.SelectedIndex;
                RebuildRows();
            };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            _back = Buttons.Icon("arrow-left-bold", "Revenir à la bibliothèque", Buttons.Bar, Buttons.Look.Calm);
            _back.Margin = new Thickness(0, 0, 6, 0);
            _back.IsVisible = false;
            _back.Click += delegate
            {
                var handler = Navigate;
                if (handler != null && _project != null)
                    handler(_scope != null && _scope.Parent != null ? _scope.Parent : _project.Category(Project.KeySheets));
            };
            left.Children.Add(_back);
            _scopeLabel = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                IsVisible = false,
                MaxWidth = 320,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            left.Children.Add(_scopeLabel);
            // « Nouvelle fiche » : UN bouton principal (pack du 12/09/2026) —
            // la catégorie se choisit dans le dialogue avec le nom.
            var newSheet = Buttons.IconText("plus-bold", "Nouvelle fiche",
                "Créer une fiche — le nom et la catégorie se choisissent ensemble",
                Buttons.Bar, Buttons.Look.Primary);
            newSheet.Click += delegate { NewSheet(); };
            left.Children.Add(newSheet);
            var newCategory = Buttons.IconText("plus-bold", "Nouvelle catégorie",
                "Une catégorie de fiches, avec son modèle", Buttons.Bar, Buttons.Look.Outline);
            newCategory.Margin = new Thickness(8, 0, 0, 0);
            newCategory.Click += delegate { NewCategory(); };
            left.Children.Add(newCategory);
            var templates = Buttons.IconText("pencil-simple-line", "Éditeur de modèles",
                "Sections, champs, natures et radar des modèles de fiches", Buttons.Bar, Buttons.Look.Outline);
            templates.Margin = new Thickness(8, 0, 0, 0);
            templates.Click += delegate { EditTemplates(); };
            left.Children.Add(templates);
            toolbar.Children.Add(left);
            Children.Add(toolbar);

            // La rangée des pastilles (1.0.4), sous la barre, fixe au-dessus
            // du défilement : « Toutes », puis une pastille par catégorie.
            var filters = new DockPanel { Margin = new Thickness(24, 10, 24, 0) };
            SetDock(filters, Dock.Top);
            // Le tri à droite des pastilles (il filtre et ordonne la même chose).
            _sortBox.Margin = new Thickness(14, 0, 0, 6);
            _sortBox.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(_sortBox, Dock.Right);
            filters.Children.Add(_sortBox);
            _chips = new WrapPanel();
            filters.Children.Add(_chips);
            Children.Add(filters);

            _rows = new StackPanel { Margin = new Thickness(16, 6, 16, 24) };
            _scroll = new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Content = _rows
            };
            Children.Add(_scroll);
        }

        /// <summary>La bibliothèque de la racine Fiches (scope null) ou d'un
        /// DOSSIER de Fiches (14/09) : mêmes tuiles, mêmes catégories, les
        /// sous-dossiers en rangée « Dossiers ».</summary>
        public void Load(Project project, HistoryManager history, BinderItem scope)
        {
            _selected.Clear();
            _project = project;
            _history = history;
            _scope = scope != null && scope.Kind == ItemKind.Folder ? scope : null;
            _back.IsVisible = _scope != null ? true : false;
            _scopeLabel.IsVisible = _back.IsVisible;
            _scopeLabel.Text = _scope != null ? _scope.Title : "";
            RebuildRows();
        }

        public bool ShowsFolder(BinderItem folder) { return folder != null && _scope == folder; }

        /// <summary>L'écart entre le haut de la zone et la rangée d'actions,
        /// LE MÊME sur Écrits (corkboard), Fiches et Dictionnaire (14/09).</summary>
        public const double TopGap = 12;

        /// <summary>Le champ de recherche « contre le rebord » (14/09) : la
        /// zone de texte du thème, telle quelle (comme celle du panneau
        /// Recherche du rail), la loupe à côté, sans cadre — partagé avec le
        /// Dictionnaire.</summary>
        public static Control SearchField(TextBox box)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            box.Width = 240;
            box.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(box);
            var glass = Icons.Make("magnifying-glass-bold", 14, Chrome.SoftText) as Control;
            if (glass != null)
            {
                glass.VerticalAlignment = VerticalAlignment.Center;
                glass.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(glass);
            }
            return row;
        }

        /// <summary>Les fiches de la portée : toutes (racine), ou celles du
        /// dossier et de ses sous-dossiers.</summary>
        private IEnumerable<BinderItem> Source()
        {
            if (_scope == null) return _project.AllItems();
            var list = new List<BinderItem>();
            Collect(_scope, list);
            return list;
        }

        private static void Collect(BinderItem parent, List<BinderItem> list)
        {
            foreach (var child in parent.Children)
            {
                list.Add(child);
                Collect(child, list);
            }
        }

        public void Refresh() { RebuildRows(); }

        // ---------------------------------------------------------- rangées

        private void RebuildRows()
        {
            _rows.Children.Clear();
            _cardsById.Clear();
            if (_project == null) return;
            var needle = Correction.FrenchTokenizer.Fold((_searchBox.Text ?? "").Trim());
            var searching = needle.Length > 0;

            // Les fiches par catégorie (une passe), plus les sans-catégorie —
            // TOUTES, la recherche ne filtre qu'à l'affichage : les pastilles
            // comptent le vrai contenu.
            var byCategory = new Dictionary<string, List<BinderItem>>();
            var uncategorized = new List<BinderItem>();
            var total = 0;
            foreach (var item in Source())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                if (item.RootCategory() != null
                    && item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                total++;
                var category = _project.SheetCategoryOf(item);
                if (category == null) { uncategorized.Add(item); continue; }
                List<BinderItem> list;
                if (!byCategory.TryGetValue(category.Id, out list))
                    byCategory[category.Id] = list = new List<BinderItem>();
                list.Add(item);
            }
            // Un filtre sur une catégorie disparue (supprimée) retombe sur « Toutes ».
            if (_filterCategoryId != null && _filterCategoryId != UncategorizedKey
                && _project.FindSheetCategory(_filterCategoryId) == null) _filterCategoryId = null;
            if (_filterCategoryId == UncategorizedKey && uncategorized.Count == 0) _filterCategoryId = null;
            BuildChips(byCategory, uncategorized.Count, total);

            var first = true;
            // Les DOSSIERS (14/09) : leur propre rangée, en tête — ceux de la
            // racine Fiches, ou les sous-dossiers du dossier ouvert ; pas en
            // recherche ni sous un filtre de catégorie.
            var parent = _scope ?? _project.Category(Project.KeySheets);
            var folders = new List<BinderItem>();
            if (parent != null && !searching && _filterCategoryId == null)
                foreach (var child in parent.Children)
                    if (child.Kind == ItemKind.Folder) folders.Add(child);
            if (folders.Count > 0)
            {
                AddHeader("Dossiers", null, -1);
                var wrap = new WrapPanel();
                foreach (var folder in folders) wrap.Children.Add(BuildFolderCard(folder));
                _rows.Children.Add(wrap);
                first = false;
            }
            // Une rangée par catégorie d'ensemble QUI A DES FICHES (1.0.4 : les
            // vides ne sont plus que des pastilles), dans l'ordre du projet,
            // triée selon le sélecteur ; un filtre ne garde que sa catégorie.
            // Dans la rangée (1.0.5) : les fiches de la catégorie même, puis
            // une BOÎTE par sous-catégorie, comme les parties d'un livre —
            // une sous-catégorie vide garde sa boîte (hors recherche), c'est
            // là qu'on la renomme, qu'on lui donne ses champs, qu'on la
            // supprime. Un filtre sur une sous-catégorie ne montre qu'elle.
            var shown = 0;
            var filtered = _project.FindSheetCategory(_filterCategoryId);
            var filterTop = _project.TopOf(filtered);
            foreach (var category in _project.TopCategories())
            {
                if (filterTop != null && filterTop != category) continue;
                List<BinderItem> own;
                if (!byCategory.TryGetValue(category.Id, out own)) own = new List<BinderItem>();
                own = filtered != null && filtered != category ? new List<BinderItem>() : Matching(own, needle);
                var boxes = new List<KeyValuePair<SheetCategory, List<BinderItem>>>();
                var inBoxes = 0;
                foreach (var subCategory in _project.SubCategoriesOf(category))
                {
                    if (filtered != null && filtered != category && filtered != subCategory) continue;
                    List<BinderItem> inside;
                    if (!byCategory.TryGetValue(subCategory.Id, out inside)) inside = new List<BinderItem>();
                    inside = Matching(inside, needle);
                    if (inside.Count == 0 && searching) continue;
                    SortSheets(inside);
                    boxes.Add(new KeyValuePair<SheetCategory, List<BinderItem>>(subCategory, inside));
                    inBoxes += inside.Count;
                }
                if (own.Count == 0 && inBoxes == 0 && (searching || boxes.Count == 0)) continue;
                SortSheets(own);
                AddDivider(first);
                AddHeader(category.Name, category, own.Count + inBoxes);
                if (own.Count > 0) AddCards(own, category);
                foreach (var box in boxes) AddSubBox(box.Key, box.Value, category);
                first = false;
                shown += own.Count + inBoxes;
            }
            if (uncategorized.Count > 0 && (_filterCategoryId == null || _filterCategoryId == UncategorizedKey))
            {
                var sheets = Matching(uncategorized, needle);
                if (sheets.Count > 0)
                {
                    SortSheets(sheets);
                    AddDivider(first);
                    AddHeader("Sans catégorie", null, sheets.Count);
                    AddCards(sheets, null);
                    shown += sheets.Count;
                }
            }
            if (shown == 0 && folders.Count == 0)
            {
                if (total == 0 && !searching) _rows.Children.Add(EmptyState());
                else _rows.Children.Add(new TextBlock
                {
                    Text = searching ? "Aucune fiche ne porte ce nom." : "Aucune fiche dans cette catégorie.",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(8, 16, 0, 0)
                });
            }
        }

        /// <summary>Les fiches dont le nom contient la recherche (accents et
        /// casse pliés) ; toutes quand elle est vide.</summary>
        private static List<BinderItem> Matching(List<BinderItem> sheets, string needle)
        {
            if (needle.Length == 0) return new List<BinderItem>(sheets);
            var list = new List<BinderItem>();
            foreach (var sheet in sheets)
                if (Correction.FrenchTokenizer.Fold(sheet.Title ?? "").Contains(needle)) list.Add(sheet);
            return list;
        }

        /// <summary>L'ordre des cartes (1.0.4) : alphabétique à la française
        /// d'abord, puis, stable, la clé du sélecteur — à rebours, par
        /// couleur (les colorées en tête, groupées), ou portrait d'abord.</summary>
        private static void SortSheets(List<BinderItem> sheets)
        {
            SortByTitle(sheets);
            switch (_sortIndex)
            {
                case 1: sheets.Reverse(); break;
                case 2: StableOrder(sheets, delegate(BinderItem s) { return s.CardColor == null ? "~" : s.CardColor.ToUpperInvariant(); }); break;
                case 3: StableOrder(sheets, delegate(BinderItem s) { return s.ImageId == null ? "1" : "0"; }); break;
            }
        }

        private static void StableOrder(List<BinderItem> sheets, Func<BinderItem, string> key)
        {
            var indexed = new List<KeyValuePair<int, BinderItem>>();
            for (var i = 0; i < sheets.Count; i++) indexed.Add(new KeyValuePair<int, BinderItem>(i, sheets[i]));
            indexed.Sort(delegate(KeyValuePair<int, BinderItem> a, KeyValuePair<int, BinderItem> b)
            {
                var byKey = string.CompareOrdinal(key(a.Value), key(b.Value));
                return byKey != 0 ? byKey : a.Key.CompareTo(b.Key);
            });
            sheets.Clear();
            foreach (var pair in indexed) sheets.Add(pair.Value);
        }

        // ---------------------------------------------------------- pastilles

        /// <summary>La rangée des pastilles (1.0.4) : « Toutes » avec le
        /// total, puis chaque catégorie du projet avec son compte — cliquable
        /// pour filtrer quand elle a des fiches, pour CRÉER une fiche dedans
        /// quand elle est vide (plus de rangée « Rien ici ») ; le clic droit
        /// ouvre le menu de la catégorie. « Sans catégorie » ferme la marche.</summary>
        private void BuildChips(Dictionary<string, List<BinderItem>> byCategory, int uncategorized, int total)
        {
            _chips.Children.Clear();
            _chips.Children.Add(Chip(null, "Toutes", total, _filterCategoryId == null, false,
                "Toutes les fiches, catégorie par catégorie",
                delegate { if (_filterCategoryId != null) { _filterCategoryId = null; RebuildRows(); } }, null));
            foreach (var category in _project.TopCategories()) // une pastille par catégorie d'ensemble, sous-catégories comptées (1.0.5)
            {
                var categoryRef = category;
                var count = CountWithSubs(byCategory, category);
                var selected = _filterCategoryId == category.Id;
                _chips.Children.Add(Chip(CategoryIcon(category, _project), category.Name, count, selected, count == 0,
                    count == 0 ? "Aucune fiche — cliquer pour en créer une dans « " + category.Name + " »"
                        : selected ? "Revenir à toutes les catégories" : "Ne montrer que les fiches « " + category.Name + " »",
                    delegate
                    {
                        if (count == 0) { NewSheet(categoryRef.Id); return; }
                        _filterCategoryId = selected ? null : categoryRef.Id;
                        RebuildRows();
                    },
                    delegate(Control anchor) { ShowCategoryMenu(anchor, categoryRef); }));
            }
            if (uncategorized > 0)
            {
                var selected = _filterCategoryId == UncategorizedKey;
                _chips.Children.Add(Chip(null, "Sans catégorie", uncategorized, selected, false,
                    selected ? "Revenir à toutes les catégories" : "Ne montrer que les fiches sans catégorie",
                    delegate { _filterCategoryId = selected ? null : UncategorizedKey; RebuildRows(); }, null));
            }
        }

        private static Control Chip(string icon, string name, int count, bool selected, bool empty, string tip,
            Action click, Action<Control> rightClick)
        {
            var ink = selected ? Brushes.White : (IBrush)Chrome.Ink;
            var soft = selected ? Brushes.White : (IBrush)Chrome.SoftText;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null)
            {
                var made = Icons.Make(icon, 12, empty ? soft : ink) as Control;
                if (made != null) { made.VerticalAlignment = VerticalAlignment.Center; made.Margin = new Thickness(0, 0, 6, 0); content.Children.Add(made); }
            }
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = empty ? soft : ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = empty ? "+" : count.ToString(),
                FontSize = 11,
                Foreground = soft,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var chip = new Border
            {
                Child = content,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Background = selected ? Chrome.Accent : (IBrush)Chrome.BarBgLight,
                BorderBrush = selected ? Chrome.Accent : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                [ToolTip.TipProperty] = tip
            };
            if (empty) chip.Opacity = 0.7;
            chip.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; click(); }
                else if (e.InitialPressMouseButton == MouseButton.Right && rightClick != null) { e.Handled = true; rightClick(chip); }
            };
            chip.PointerEntered += delegate { if (!selected) chip.BorderBrush = Chrome.Accent; };
            chip.PointerExited += delegate { if (!selected) chip.BorderBrush = Chrome.Border; };
            return chip;
        }

        /// <summary>Sans aucune fiche (1.0.4) : une invite au lieu de onze
        /// rangées « Rien ici ».</summary>
        private static Control EmptyState()
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0) };
            var icon = Icons.Make("pile-fiches", 44, Chrome.FaintText) as Control;
            if (icon != null) { icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(icon); }
            panel.Children.Add(new TextBlock
            {
                Text = "Aucune fiche pour l'instant",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Cliquez une catégorie ci-dessus pour y créer votre première fiche, ou « + Nouvelle fiche ».",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Margin = new Thickness(0, 6, 0, 0)
            });
            return panel;
        }


        /// <summary>Tri alphabétique à la française (accents et casse
        /// ignorés, ordre stable pour les homonymes).</summary>
        public static void SortByTitle(List<BinderItem> sheets)
        {
            var indexed = new List<KeyValuePair<int, BinderItem>>();
            for (var i = 0; i < sheets.Count; i++)
                indexed.Add(new KeyValuePair<int, BinderItem>(i, sheets[i]));
            indexed.Sort(delegate (KeyValuePair<int, BinderItem> a, KeyValuePair<int, BinderItem> b)
            {
                var byName = string.Compare(a.Value.Title ?? "", b.Value.Title ?? "",
                    System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                    System.Globalization.CompareOptions.IgnoreCase);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });
            sheets.Clear();
            foreach (var pair in indexed) sheets.Add(pair.Value);
        }

        /// <summary>Le filet séparateur entre catégories.</summary>
        private void AddDivider(bool first)
        {
            if (first) return;
            _rows.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 16, 0, 0)
            });
        }

        /// <summary>L'éditeur de modèles (sections, champs, natures) ; les
        /// modèles édités remplacent ceux du projet, la bibliothèque se redessine.</summary>
        private async void EditTemplates()
        {
            var templates = await TemplatesDialog.Show(Ui.OwnerOf(this), _project.Templates, _project);
            if (templates == null) return; // annulé (28/09) : la liste nulle cassait le projet
            // Annulable (1.0.4) : l'ancienne liste revient d'un Ctrl+Z.
            var project = _project;
            var previous = project.Templates;
            _history.Run(new SheetStructureAction("Modèles de fiches modifiés",
                delegate { project.Templates = templates; },
                delegate { project.Templates = previous; }));
            NotifyChanged();
            RebuildRows();
        }

        private void AddHeader(string name, SheetCategory category, int count)
        {
            var header = new DockPanel { Margin = new Thickness(0, 14, 0, 8) };

            if (category != null)
            {
                // Le menu de la catégorie : un bouton à trois points, sans
                // texte (b42 bis). Le « + Nouvelle fiche » par rangée est parti
                // dans la barre du haut (pack du 12/09/2026).
                var categoryRef = category;
                var edit = Buttons.Icon("dots-three-vertical-bold", "Modifier la catégorie…", Buttons.Compact, Buttons.Look.Calm);
                edit.Click += delegate { ShowCategoryMenu(edit, categoryRef); };
                DockPanel.SetDock(edit, Dock.Right);
                header.Children.Add(edit);
            }

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (count >= 0) title.Children.Add(new TextBlock
            {
                Text = count == 0 ? "aucune fiche"
                    : count == 1 ? "1 fiche" : count + " fiches",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (category != null)
            {
                var template = _project.FindTemplate(category.TemplateId);
                if (template != null)
                    title.Children.Add(new TextBlock
                    {
                        Text = "· modèle " + template.Name,
                        FontSize = 11,
                        Foreground = Chrome.SoftText,
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
            }
            header.Children.Add(title);
            _rows.Children.Add(header);
        }

        // ------------------------------------------------------------ cartes

        private void AddCards(List<BinderItem> sheets, SheetCategory category)
        {
            var wrap = new WrapPanel();
            foreach (var sheet in sheets)
                wrap.Children.Add(BuildCard(sheet, category));
            if (sheets.Count == 0)
                wrap.Children.Add(new TextBlock
                {
                    Text = "Rien ici — « + Nouvelle fiche » pour commencer.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 0, 2)
                });
            _rows.Children.Add(wrap);
        }

        /// <summary>Une tuile de dossier (14/09) : l'icône, le nom, le compte
        /// de fiches qu'il contient ; un clic l'ouvre (même bibliothèque,
        /// à sa portée), le clic droit offre le menu de la Pile.</summary>
        private Control BuildFolderCard(BinderItem folder)
        {
            var count = 0;
            var inside = new List<BinderItem>();
            Collect(folder, inside);
            foreach (var item in inside) if (item.Kind == ItemKind.Sheet) count++;
            // Icône en haut, compte en bas, le nom CENTRÉ dans ce qui reste
            // (29/09) : le WrapPanel étire chaque tuile à la hauteur de sa
            // rangée, et le nom d'une tuile courte restait collé en haut de
            // la zone qu'un voisin au titre long avait fait grandir.
            var layout = new DockPanel { Width = 132 };
            // L'icône DU DOSSIER (29/09) : celle choisie dans la Pile, avec sa
            // couleur — le dossier bleu fixe ignorait « Changer l'icône ».
            var icon = ItemIcons.Render(folder, 34, Chrome.Accent) as Control;
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.Margin = new Thickness(0, 22, 0, 6);
                DockPanel.SetDock(icon, Dock.Top);
                layout.Children.Add(icon);
            }
            var countText = new TextBlock
            {
                Text = count == 0 ? "vide" : count == 1 ? "1 fiche" : count + " fiches",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 0, 6, 10)
            };
            DockPanel.SetDock(countText, Dock.Bottom);
            layout.Children.Add(countText);
            layout.Children.Add(new TextBlock
            {
                Text = folder.Title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                MaxLines = 2, // points de suspension en fin de 2e ligne, plutôt qu'une coupe nette (29/09)
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 2)
            });
            var card = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = layout
            };
            var folderRef = folder;
            // Seul le clic GAUCHE ouvre (29/09) : le droit ne fait que le menu.
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                var handler = Navigate; if (handler != null) handler(folderRef);
            };
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                var menu = MenuProvider == null ? null : MenuProvider(folderRef);
                if (menu == null) return;
                menu.PlacementTarget = card;
                Ui.ShowMenu(menu, card);
            };
            card.PointerEntered += delegate { card.BorderBrush = Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = Chrome.Border; };
            CardLift.Attach(card);
            return card;
        }

        /// <summary>Une carte-fiche : PHOTO EN HAUT (ou emplacement réservé),
        /// NOM EN DESSOUS (batch 31). La puce en coin signale une fiche dont
        /// le modèle diffère du modèle de base de sa catégorie.</summary>
        private Control BuildCard(BinderItem sheet, SheetCategory category)
        {
            var layout = new DockPanel { Width = 132 }; // photo en haut, nom centré dans le reste (29/09)

            // — la photo, ou son emplacement réservé. Batch 36 : l'image est
            // CENTRÉE dans son cadre (l'équivalent de background-position:
            // center center — un Image UniformToFill s'alignait en haut à
            // gauche), et le cadre est découpé selon ses coins arrondis :
            // ClipToBounds ne coupe qu'au rectangle, l'image mordait les
            // arrondis de la carte.
            Control pictureContent = null;
            var image = _project.FindImage(sheet.ImageId);
            if (image != null)
            {
                var source = MediaView.TryImage(image.Bytes, 260);
                if (source != null)
                    pictureContent = new Rectangle
                    {
                        Fill = new ImageBrush(source)
                        {
                            Stretch = Stretch.UniformToFill,
                            AlignmentX = AlignmentX.Center,
                            AlignmentY = AlignmentY.Center
                        }
                    };
            }
            if (pictureContent == null)
                pictureContent = CategoryPlaceholder(category, 44, _project);
            var picture = new Border
            {
                Height = 108,
                Background = Chrome.BarBgLight,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Child = pictureContent
            };
            Ui.OnSizeChanged(picture, delegate { picture.Clip = TopRoundedClip(picture.Bounds.Width, picture.Bounds.Height, 3); });

            var grid = new Grid();
            grid.Children.Add(picture);
            // — la puce « modèle différent du modèle de base »
            if (category != null && sheet.TemplateId != category.TemplateId)
                grid.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = Chrome.AccentSoft, // pastille calme (b40)
                    BorderBrush = Chrome.PaperBg,
                    BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 6, 0),
                    [ToolTip.TipProperty] = "Cette fiche n'utilise pas le modèle de base "
                        + "de la catégorie"
                });
            // — la puce d'une fiche de module (DLC, 22/09) : « Fiche FPDM »
            foreach (var module in Modules.ForSheet(_project, sheet))
                if (Modules.HasSheet(module, sheet))
                {
                    var complete = Modules.IsComplete(module, sheet);
                    grid.Children.Add(new Border
                    {
                        Width = 10,
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Background = Chrome.Accent,
                        BorderBrush = Chrome.PaperBg,
                        BorderThickness = new Thickness(1.5),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(6, 6, 0, 0),
                        [ToolTip.TipProperty] = "Fiche " + module.Name + (complete ? " — complète" : " — " + Modules.FilledCount(module, sheet) + " / " + module.ValueIds().Count + " champs")
                    });
                    break;
                }
            DockPanel.SetDock(grid, Dock.Top);
            layout.Children.Add(grid);

            // La couleur attribuée à la fiche (29/09) : le MÊME dégradé que la
            // barre de titre des cartes de texte du tableau — plein à droite,
            // fondu jusqu'à la moitié de la zone du nom (demande de Rémi).
            var nameZone = new Border { CornerRadius = new CornerRadius(0, 0, 3, 3) };
            if (sheet.CardColor != null)
            {
                var accent = Ink.Parse(sheet.CardColor).ToColor();
                var fade = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
                };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0.5));
                fade.GradientStops.Add(new GradientStop(accent, 1.0));
                nameZone.Background = fade;
            }
            // Le nom CENTRÉ dans la zone qui reste (29/09) : le WrapPanel
            // étire chaque tuile à la hauteur de sa rangée, et le nom d'une
            // tuile courte restait collé sous la photo quand un voisin au
            // titre long avait fait grandir la rangée.
            // Le nom seul (07/10) : l'accroche « En un mot » du premier lot de
            // la 1.0.4 est retirée — Rémi la trouvait de trop sous le titre.
            nameZone.Child = new TextBlock
            {
                Text = sheet.Title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                MaxLines = 2, // points de suspension en fin de 2e ligne, plutôt qu'une coupe nette (29/09)
                Margin = new Thickness(6, 5, 6, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            layout.Children.Add(nameZone);

            var card = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = layout
            };
            var sheetRef = sheet;
            card.Tag = sheet;
            _cardsById[sheet.Id] = card;
            if (_selected.Contains(sheet.Id)) PaintSelection(card, true);
            // Le DOUBLE-clic ouvre (29/09) ; le clic simple choisit la tuile
            // (Ctrl = plusieurs, 1.0.3), et le Général du rail montre la fiche
            // — comme les cartes des écrits. Le droit ne fait que le menu.
            card.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (e.ClickCount != 2 || !e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                var handler = Navigate;
                if (handler != null) handler(sheetRef);
            };
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (Ui.HasCommand(e.KeyModifiers)) { if (!_selected.Remove(sheetRef.Id)) _selected.Add(sheetRef.Id); }
                else { _selected.Clear(); _selected.Add(sheetRef.Id); }
                AnnounceSelection(_selected.Contains(sheetRef.Id) ? sheetRef : null);
            };
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                // Hors de la sélection : la tuile devient la sélection ; sur
                // une sélection multiple : le menu du lot (1.0.3).
                if (!_selected.Contains(sheetRef.Id)) { _selected.Clear(); _selected.Add(sheetRef.Id); AnnounceSelection(sheetRef); }
                var batch = _selected.Count > 1 && BatchMenuProvider != null ? BatchMenuProvider(SelectedItems()) : null;
                if (batch != null) { batch.PlacementTarget = card; Ui.ShowMenu(batch, card); return; }
                ShowCardMenu(card, sheetRef);
            };
            card.PointerEntered += delegate { card.BorderBrush = Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = _selected.Contains(sheetRef.Id) ? (IBrush)Chrome.Accent : Chrome.Border; };
            CardLift.Attach(card); // soulèvement au survol (b35)
            return card;
        }

        /// <summary>Le symbole d'une fiche sans image (29/09) : l'icône de sa
        /// catégorie (personnage, lieu, événement, système, peuple, bestiaire,
        /// pays, faction — les huit « fiche-* » du jeu d'icônes), sinon le
        /// cadre générique. Partagé avec la fiche ouverte.</summary>
        public static Control CategoryPlaceholder(SheetCategory category, double size, Project project = null)
        {
            var icon = CategoryIcon(category, project);
            if (icon != null)
            {
                var made = Icons.Make(icon, size, Chrome.SoftText);
                made.HorizontalAlignment = HorizontalAlignment.Center;
                made.VerticalAlignment = VerticalAlignment.Center;
                return made;
            }
            return new TextBlock
            {
                Text = "🖼",
                FontSize = size * 0.7,
                Foreground = Chrome.SoftText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>« fiche-personnage », « fiche-pays »… d'après le premier mot
        /// du nom de la catégorie, accents pliés ; null si aucune icône.</summary>
        public static string CategoryIcon(SheetCategory category)
        {
            if (category == null || string.IsNullOrEmpty(category.Name)) return null;
            var folded = Correction.FrenchTokenizer.Fold(category.Name.Trim());
            var cut = folded.IndexOfAny(new[] { ' ', '/', '-', ',' });
            var word = cut > 0 ? folded.Substring(0, cut) : folded;
            var name = "fiche-" + word;
            return Icons.Has(name) ? name : null;
        }

        /// <summary>L'icône d'une catégorie, ou celle de sa catégorie
        /// d'ensemble pour une sous-catégorie sans icône à elle (1.0.5).</summary>
        public static string CategoryIcon(SheetCategory category, Project project)
        {
            var own = CategoryIcon(category);
            if (own != null || project == null) return own;
            return CategoryIcon(project.ParentOf(category));
        }

        /// <summary>Un clip aux coins HAUTS arrondis : le rectangle déborde
        /// du bas de « radius » pour y garder des coins droits (le bas de la
        /// photo touche le nom, pas le bord de la carte).</summary>
        public static Geometry TopRoundedClip(double width, double height, double radius)
        {
            if (width <= 0 || height <= 0) return null;
            var geometry = new RectangleGeometry(new Rect(0, 0, width, height + radius), radius, radius);
            return geometry;
        }

        /// <summary>Le menu d'une tuile (14/09) : Ouvrir, Changer de catégorie,
        /// puis TOUT ce que la Pile offre sur la même fiche (épingler à
        /// l'accueil, épingler au rail, renommer, icône, supprimer…).</summary>
        private void ShowCardMenu(Control anchor, BinderItem sheet)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Ouvrir" };
            open.Click += delegate
            {
                var handler = Navigate;
                if (handler != null) handler(sheet);
            };
            menu.Items.Add(open);

            var move = new MenuItem { Header = "Changer de catégorie" };
            foreach (var category in _project.OrderedCategories()) // catégories puis leurs sous-catégories (1.0.5)
            {
                var entry = new MenuItem
                {
                    Header = (category.IsSub ? "    › " : "") + category.Name,
                    IsChecked = _project.SheetCategoryOf(sheet) == category
                };
                var categoryRef = category;
                entry.Click += delegate
                {
                    if (sheet.CategoryId == categoryRef.Id) return;
                    // Annulable (1.0.4) : la même action que les champs de la fiche.
                    var before = SheetSnapshot.Capture(_project, sheet);
                    sheet.CategoryId = categoryRef.Id;
                    _history.Push(new SheetEditAction(_project, sheet, before, SheetSnapshot.Capture(_project, sheet), "category"));
                    NotifyChanged();
                    RebuildRows();
                };
                move.Items.Add(entry);
            }
            menu.Items.Add(move);
            var provided = MenuProvider == null ? null : MenuProvider(sheet);
            if (provided != null && provided.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
                var items = new List<object>();
                foreach (var entry in provided.Items) items.Add(entry);
                provided.Items.Clear(); // un MenuItem n'a qu'un parent
                foreach (var entry in items)
                    if (!(entry is Separator && menu.Items[menu.Items.Count - 1] is Separator))
                        menu.Items.Add(entry);
            }
            menu.PlacementTarget = anchor;
            Ui.ShowMenu(menu, anchor);
        }

        // ------------------------------------------------------- catégories

        /// <summary>Nom et catégorie dans le même dialogue (le même que la
        /// Pile) ; la fiche naît dans la racine Fiches avec le modèle de base
        /// de sa catégorie, puis s'ouvre.</summary>
        private async void NewSheet(string preselectedCategoryId = null)
        {
            var choice = await NewSheetDialog.Ask(Ui.OwnerOf(this), _project, preselectedCategoryId);
            if (choice == null) return;
            var title = choice.Title;
            var categoryId = choice.CategoryId;
            var category = _project.FindSheetCategory(categoryId);
            var item = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                TemplateId = _project.BaseTemplateIdOf(category), // hérité pour une sous-catégorie (1.0.5)
                CategoryId = category != null ? category.Id : null
            };
            _history.Run(new AddItemAction(
                _scope ?? _project.Category(Project.KeySheets), item, -1));
            NotifyChanged();
            RebuildRows();
            var handler = Navigate; // la fiche neuve s'ouvre, prête à remplir
            if (handler != null) handler(item);
        }

        private void ShowCategoryMenu(Control anchor, SheetCategory category)
        {
            var menu = new ContextMenu();
            var sub = category.IsSub && _project.ParentOf(category) != null;
            var kindLabel = sub ? "la sous-catégorie" : "la catégorie";

            var rename = new MenuItem { Header = "Renommer…" };
            rename.Click += async delegate
            {
                var name = await InputDialog.Ask(Ui.OwnerOf(this),
                    "Renommer " + kindLabel, "Nom de " + kindLabel + " :", category.Name);
                if (name == null || name.Trim().Length == 0) return;
                var newName = name.Trim();
                var oldName = category.Name;
                _history.Run(new SheetStructureAction(sub ? "Sous-catégorie renommée" : "Catégorie renommée",
                    delegate { category.Name = newName; }, delegate { category.Name = oldName; }));
                NotifyChanged();
                RebuildRows();
            };
            menu.Items.Add(rename);

            if (sub)
            {
                // Une sous-catégorie (1.0.5) : son modèle est celui de
                // l'ensemble (hérité, suit ses changements), elle n'a que ses
                // champs propres à régler.
                var parent = _project.ParentOf(category);
                var inherited = _project.FindTemplate(parent.TemplateId);
                menu.Items.Add(new MenuItem
                {
                    Header = "Modèle hérité : " + (inherited != null ? inherited.Name : "aucun") + " (« " + parent.Name + " »)",
                    IsEnabled = false
                });
                var fields = new MenuItem
                {
                    Header = "Champs propres…",
                    [ToolTip.TipProperty] = "Les champs que CETTE sous-catégorie ajoute au modèle hérité, sur chacune de ses fiches"
                };
                fields.Click += delegate { EditSubFields(category); };
                menu.Items.Add(fields);
                menu.Items.Add(new Separator());
                var removeSub = new MenuItem { Header = "Supprimer la sous-catégorie" };
                removeSub.Click += delegate { DeleteCategory(category); };
                menu.Items.Add(removeSub);
                menu.PlacementTarget = anchor;
                Ui.ShowMenu(menu, anchor);
                return;
            }

            var baseTemplate = new MenuItem
            {
                Header = "Modèle de base",
                [ToolTip.TipProperty] = "Le modèle des NOUVELLES fiches de la catégorie — les "
                    + "fiches existantes gardent le leur (puce sur leur carte)"
            };
            foreach (var template in _project.Templates)
            {
                var entry = new MenuItem
                {
                    Header = template.Name,
                    IsChecked = template.Id == category.TemplateId
                };
                var templateRef = template;
                entry.Click += delegate
                {
                    if (category.TemplateId == templateRef.Id) return;
                    var oldTemplate = category.TemplateId;
                    _history.Run(new SheetStructureAction("Modèle de base changé",
                        delegate { category.TemplateId = templateRef.Id; }, delegate { category.TemplateId = oldTemplate; }));
                    NotifyChanged();
                    RebuildRows();
                };
                baseTemplate.Items.Add(entry);
            }
            menu.Items.Add(baseTemplate);

            var editTemplates = new MenuItem
            {
                Header = "Modifier les modèles…",
                [ToolTip.TipProperty] = "Champs, groupes et types — l'éditeur de modèles"
            };
            editTemplates.Click += delegate { EditTemplates(); };
            menu.Items.Add(editTemplates);

            menu.Items.Add(new Separator());
            var newSub = new MenuItem
            {
                Header = "Créer une sous-catégorie…",
                [ToolTip.TipProperty] = "Une partie de « " + category.Name + " » : même modèle (hérité), plus ses champs propres"
            };
            newSub.Click += delegate { NewSubCategory(category); };
            menu.Items.Add(newSub);

            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Supprimer la catégorie" };
            remove.Click += delegate { DeleteCategory(category); };
            menu.Items.Add(remove);

            menu.PlacementTarget = anchor;
            Ui.ShowMenu(menu, anchor);
        }

        private async void NewCategory()
        {
            var name = await InputDialog.Ask(Ui.OwnerOf(this),
                "Nouvelle catégorie", "Nom de la catégorie (ex. « Religion », "
                + "« Faction ») :", "Catégorie");
            if (name == null || name.Trim().Length == 0) return;
            // La catégorie naît avec son propre modèle, sobre — à détailler
            // dans « Modifier les modèles… ».
            var template = new SheetTemplate { Name = name.Trim() };
            template.Fields.Add(new SheetField
            {
                Name = "Description",
                Kind = "multiline"
            });
            var category = new SheetCategory { Name = name.Trim(), TemplateId = template.Id };
            var project = _project;
            _history.Run(new SheetStructureAction("Catégorie créée",
                delegate { project.Templates.Add(template); project.SheetCategories.Add(category); },
                delegate { project.SheetCategories.Remove(category); project.Templates.Remove(template); }));
            NotifyChanged();
            RebuildRows();
        }

        private async void DeleteCategory(SheetCategory category)
        {
            var sub = category.IsSub && _project.ParentOf(category) != null;
            var what = (sub ? "La sous-catégorie « " : "La catégorie « ") + category.Name + " »";
            var subs = _project.SubCategoriesOf(category).Count;
            if (subs > 0)
            {
                MessageDialog.Show(Ui.OwnerOf(this),
                    what + " contient " + subs + (subs == 1 ? " sous-catégorie" : " sous-catégories")
                    + ".\nSupprimez-les d'abord (le menu de chaque boîte).",
                    "Marabook", MessageButtons.OK, MessageIcon.Information);
                return;
            }
            var used = 0;
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet
                    && _project.SheetCategoryOf(item) == category) used++;
            if (used > 0)
            {
                MessageDialog.Show(Ui.OwnerOf(this),
                    what + " contient " + used
                    + (used == 1 ? " fiche" : " fiches") + ".\nDéplacez-les "
                    + "d'abord (clic droit sur une carte → Changer de catégorie).",
                    "Marabook", MessageButtons.OK, MessageIcon.Information);
                return;
            }
            var answer = MessageDialog.Show(Ui.OwnerOf(this),
                "Supprimer " + (sub ? "la sous-catégorie « " : "la catégorie « ") + category.Name + " » ?\n"
                + (sub ? "Ses champs propres disparaissent avec elle." : "Son modèle reste dans l'éditeur de modèles."),
                "Marabook", MessageButtons.YesNo, MessageIcon.Question);
            if (await answer != MessageResult.Yes) return;
            var project = _project;
            var index = project.SheetCategories.IndexOf(category);
            _history.Run(new SheetStructureAction(sub ? "Sous-catégorie supprimée" : "Catégorie supprimée",
                delegate { project.SheetCategories.Remove(category); },
                delegate { project.SheetCategories.Insert(Math.Min(index, project.SheetCategories.Count), category); }));
            NotifyChanged();
            RebuildRows();
        }

        // --------------------------------------------- sous-catégories (1.0.5)

        /// <summary>Une sous-catégorie naît sous sa catégorie d'ensemble :
        /// même modèle (hérité), aucun champ propre encore — « Champs
        /// propres… » dans le menu de sa boîte. Annulable.</summary>
        private async void NewSubCategory(SheetCategory parent)
        {
            var name = await InputDialog.Ask(Ui.OwnerOf(this),
                "Nouvelle sous-catégorie de « " + parent.Name + " »",
                "Nom de la sous-catégorie (ex. « Héros », « Figurants ») :", "Sous-catégorie");
            if (name == null || name.Trim().Length == 0) return;
            var project = _project;
            SheetCategory created = null;
            var index = -1;
            _history.Run(new SheetStructureAction("Sous-catégorie créée",
                delegate
                {
                    if (created == null) { created = project.AddSubCategory(parent, name); index = project.SheetCategories.IndexOf(created); }
                    else project.SheetCategories.Insert(Math.Min(index, project.SheetCategories.Count), created);
                },
                delegate { project.SheetCategories.Remove(created); }));
            NotifyChanged();
            RebuildRows();
        }

        /// <summary>Les champs propres d'une sous-catégorie : l'éditeur de
        /// modèles en mode « un seul », sur ses champs ; la liste validée
        /// remplace l'ancienne (annulable), les fiches ouvertes suivent.</summary>
        private async void EditSubFields(SheetCategory sub)
        {
            var baseTemplate = _project.FindTemplate(_project.BaseTemplateIdOf(sub));
            var fields = await TemplatesDialog.EditSubFields(Ui.OwnerOf(this), sub, baseTemplate, _project);
            if (fields == null) return;
            var previous = sub.ExtraFields;
            _history.Run(new SheetStructureAction("Champs propres modifiés",
                delegate { sub.ExtraFields = fields; },
                delegate { sub.ExtraFields = previous; }));
            NotifyChanged();
            RebuildRows();
        }

        /// <summary>Le compte d'une catégorie d'ensemble, sous-catégories comprises.</summary>
        private int CountWithSubs(Dictionary<string, List<BinderItem>> byCategory, SheetCategory top)
        {
            List<BinderItem> list;
            var count = byCategory.TryGetValue(top.Id, out list) ? list.Count : 0;
            foreach (var sub in _project.SubCategoriesOf(top))
                if (byCategory.TryGetValue(sub.Id, out list)) count += list.Count;
            return count;
        }

        /// <summary>La boîte d'une sous-catégorie, comme une partie de livre
        /// au tableau : bordure fine à bords ronds, le nom posé DANS la
        /// bordure avec son compte, son menu à trois points, les cartes à
        /// l'intérieur ; vide, elle le dit.</summary>
        private void AddSubBox(SheetCategory sub, List<BinderItem> sheets, SheetCategory top)
        {
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(6, 0, 2, 4) };
            var subRef = sub;
            var edit = Buttons.Icon("dots-three-vertical-bold", "Modifier la sous-catégorie…", Buttons.Compact, Buttons.Look.Calm);
            edit.Click += delegate { ShowCategoryMenu(edit, subRef); };
            DockPanel.SetDock(edit, Dock.Right);
            head.Children.Add(edit);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("folder-bold", 13, Chrome.SoftText) as Control;
            if (icon != null) { icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new Thickness(0, 0, 6, 0); title.Children.Add(icon); }
            title.Children.Add(new TextBlock
            {
                Text = sub.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(new TextBlock
            {
                Text = sheets.Count == 0 ? "aucune fiche" : sheets.Count == 1 ? "1 fiche" : sheets.Count + " fiches",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (sub.ExtraFields.Count > 0)
                title.Children.Add(new TextBlock
                {
                    Text = "· " + sub.ExtraFields.Count + (sub.ExtraFields.Count == 1 ? " champ propre" : " champs propres"),
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            var wrap = new WrapPanel { Margin = new Thickness(2, 0, 2, 0) };
            foreach (var sheet in sheets) wrap.Children.Add(BuildCard(sheet, sub));
            if (sheets.Count == 0)
                wrap.Children.Add(new TextBlock
                {
                    Text = "Rien ici — « + Nouvelle fiche », ou clic droit sur une carte → Changer de catégorie.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(6, 2, 0, 6)
                });
            dock.Children.Add(wrap);
            _rows.Children.Add(new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 10, 8, 0),
                Padding = new Thickness(6, 8, 6, 4),
                Child = dock,
                Tag = sub.Id // sonde
            });
        }

        private void NotifyChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
