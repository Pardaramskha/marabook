using System;
using System.Linq;
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
        // Le filtre par TAG (1.1.0) : « Tous les tags », puis chaque tag des
        // fiches du projet ; null = tous.
        private readonly ComboBox _tagBox;
        private string _filterTag;
        private bool _syncingTags;
        // Le glisser des tuiles (1.1.0) : la tuile pressée, prête à partir dès
        // que le pointeur bouge — vers une boîte de sous-catégorie, un en-tête
        // de catégorie, une pastille, un dossier, ou la Pile (mêmes formats).
        private BinderItem _dragCandidate;
        private Point _dragStart;
        /// <summary>L'icône d'une catégorie qui n'en a ni par son nom ni par choix.</summary>
        public const string GenericCategoryIcon = "pile-fiches";

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
        private readonly List<string> _cardOrder = new List<string>(); // l'ordre des tuiles posées (MAJ+clic, 1.0.5)
        private string _anchorId; // la dernière tuile cliquée (l'ancre du MAJ+clic)
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

        /// <summary>MAJ+clic (1.0.5) : toutes les tuiles entre l'ancre (la
        /// dernière tuile cliquée, sinon la première sélectionnée) et
        /// celle-ci, dans l'ordre d'affichage. Faux sans ancre.</summary>
        private bool RangeSelect(string id)
        {
            var anchor = _anchorId;
            if (anchor == null || !_cardOrder.Contains(anchor))
                foreach (var candidate in _cardOrder) if (_selected.Contains(candidate)) { anchor = candidate; break; }
            if (anchor == null) return false;
            var a = _cardOrder.IndexOf(anchor);
            var b = _cardOrder.IndexOf(id);
            if (a < 0 || b < 0) return false;
            for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selected.Add(_cardOrder[i]);
            return true;
        }

        /// <summary>Sonde (1.0.5) : le MAJ+clic sur cette tuile, l'ancre étant la dernière cliquée.</summary>
        internal bool RangeSelectForProbe(string anchorId, string id)
        {
            _anchorId = anchorId;
            var ok = RangeSelect(id);
            AnnounceSelection(null);
            return ok;
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
            // Le tri (1.0.4) — comme les sélecteurs du Dictionnaire : nom,
            // couleur, portrait. Depuis la 1.1.0 sur la ligne de la recherche.
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
            // Les fiches manquantes (1.0.5-a, Rémi) : les noms propres récurrents
            // des écrits sans fiche, à créer en lot — le pendant des mots inconnus.
            var missing = Buttons.IconText("magnifying-glass-bold", "Fiches manquantes…",
                "Relever les noms propres récurrents des écrits qui n'ont pas de fiche, et en créer plusieurs d'un coup",
                Buttons.Bar, Buttons.Look.Outline);
            missing.Margin = new Thickness(8, 0, 0, 0);
            missing.Click += delegate { ShowMissingSheets(); };
            left.Children.Add(missing);
            toolbar.Children.Add(left);
            Children.Add(toolbar);

            // La ligne de la RECHERCHE (1.1.0, Rémi) : sous les boutons, sur
            // sa propre ligne — le champ, le filtre par tag, puis le tri (ils
            // filtrent et ordonnent la même chose).
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 10, 24, 0) };
            SetDock(searchRow, Dock.Top);
            searchRow.Children.Add(SearchField(_searchBox));
            _tagBox = new ComboBox
            {
                FontSize = 12,
                MinWidth = 150,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = "Ne montrer que les fiches qui portent ce tag (les tags se posent dans le Général du rail)"
            };
            _tagBox.SelectionChanged += delegate
            {
                if (_syncingTags) return;
                var chosen = _tagBox.SelectedItem as ComboBoxItem;
                var tag = chosen == null ? null : chosen.Tag as string;
                if (tag == _filterTag) return;
                _filterTag = tag;
                // Après le tour du ComboBox (27/10) : rebâtir ses choix PENDANT son
                // changement de sélection faisait énumérer une liste déjà vidée.
                Ui.Post(DispatcherPriority.Background, new Action(RebuildRows));
            };
            searchRow.Children.Add(_tagBox);
            _sortBox.Margin = new Thickness(8, 0, 0, 0);
            searchRow.Children.Add(_sortBox);
            Children.Add(searchRow);

            // Un filet entre les commandes et les pastilles de filtre (1.1.0).
            var divider = new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(24, 12, 24, 0) };
            SetDock(divider, Dock.Top);
            Children.Add(divider);

            // La rangée des pastilles (1.0.4), fixe au-dessus du défilement :
            // « Toutes », puis une pastille par catégorie — des icônes seules
            // depuis la 1.1.0, le compte sur la pastille choisie.
            _chips = new WrapPanel { Margin = new Thickness(24, 10, 24, 0) };
            SetDock(_chips, Dock.Top);
            Children.Add(_chips);

            _rows = new StackPanel { Margin = new Thickness(16, 6, 16, 24) };
            _scroll = new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Content = _rows
            };
            // Le défilement mène l'entrée des tuiles (1.0.6) : ce qui arrive à l'écran entre.
            _scroll.ScrollChanged += delegate { Sweep(false); };
            _scroll.SizeChanged += delegate { PostSweep(false); };
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
            _entranceOn = true; // les tuiles entrent en fondu à l'ouverture du panneau (1.0.6)
            RebuildRows();
        }

        // ---------------------------------------------------- l'entrée des tuiles (1.0.6)

        private bool _entranceOn;   // vrai le temps d'une reconstruction après Load
        private const double EntranceDurationMs = 170;
        private const int EntranceStepMs = 26;   // le décalage d'une tuile à la suivante dans un même passage
        // Plus de plafond (1.1.0, Rémi) : toutes les tuiles à l'écran entrent
        // l'une après l'autre, aucune ne se pose d'un coup.
        private const double EntranceRise = 12;  // la montée, en px
        private const double LeaveMargin = 48;   // sortie franche de l'écran avant de s'effacer (pas de clignotement au bord)
        public static bool EntranceAnimationsForProbe = true;

        private enum EntranceState { Pending, Entering, Done }

        private sealed class EntranceEntry
        {
            public Border Wrapper;
            public TranslateTransform Slide;
            public EntranceState State;
            public DateTime Start; // le départ, décalage compris
        }

        private readonly List<EntranceEntry> _entrances = new List<EntranceEntry>();
        private bool _entranceLoop;
        private bool _entranceSweepPosted;

        /// <summary>La tuile arrive en fondu, de bas en haut, chacune un peu
        /// après la précédente — des cartes présentées une à une (demande de
        /// Rémi, 14/10). L'entrée est menée par L'ÉCRAN (retour du 14/10) :
        /// une tuile hors de la zone visible reste effacée et n'entre qu'en y
        /// arrivant, au défilement ; une tuile qui en sort franchement s'efface
        /// et rejouera à son retour. Rien si Préférences › Général › Animations
        /// est décoché. Après Load, tout est à entrer ; après un rafraîchissement
        /// ou une recherche, ce qui est à l'écran se pose d'un coup, le reste
        /// attend son tour. L'enveloppe (sans fond) porte l'opacité et la
        /// translation : la tuile garde son RenderTransform de soulèvement.</summary>
        private Control Entrance(Control card)
        {
            if (!Marabook.Settings.AppSettings.Animations || !EntranceAnimationsForProbe) return card;
            var wrapper = new Border { Child = card, Tag = "entrance" };
            var slide = new TranslateTransform(0, 0);
            wrapper.RenderTransform = slide;
            var entry = new EntranceEntry { Wrapper = wrapper, Slide = slide, State = EntranceState.Done };
            if (_entranceOn) Hide(entry);
            _entrances.Add(entry);
            PostSweep(!_entranceOn);
            return wrapper;
        }

        private static void Hide(EntranceEntry entry)
        {
            entry.State = EntranceState.Pending;
            entry.Wrapper.Opacity = 0;
            entry.Slide.Y = EntranceRise;
        }

        private static void Land(EntranceEntry entry)
        {
            entry.State = EntranceState.Done;
            entry.Wrapper.Opacity = 1;
            entry.Slide.Y = 0;
        }

        /// <summary>Un passage après la prochaine mise en page (les Bounds sont
        /// alors posés). settle : ce qui est à l'écran se pose sans entrer
        /// (rafraîchissement), le reste est effacé pour entrer plus tard.</summary>
        private void PostSweep(bool settle)
        {
            if (_entranceSweepPosted) return;
            _entranceSweepPosted = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                _entranceSweepPosted = false;
                Sweep(settle);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>Le passage sur toutes les enveloppes : ce qui entre à
        /// l'écran démarre (décalé dans l'ordre des tuiles), ce qui en est
        /// franchement sorti s'efface.</summary>
        private void Sweep(bool settle)
        {
            if (_entrances.Count == 0 || _scroll == null) return;
            var height = _scroll.Viewport.Height;
            if (height <= 0) return;
            var started = 0;
            var now = DateTime.Now;
            foreach (var entry in _entrances)
            {
                var at = entry.Wrapper.TranslatePoint(new Point(0, 0), _scroll);
                if (at == null) continue;
                var top = at.Value.Y;
                var bottom = top + entry.Wrapper.Bounds.Height;
                var visible = bottom > 0 && top < height;
                var gone = bottom < -LeaveMargin || top > height + LeaveMargin;
                switch (entry.State)
                {
                    case EntranceState.Pending:
                        if (!visible) break;
                        if (settle) { Land(entry); break; }
                        entry.State = EntranceState.Entering;
                        entry.Start = now + TimeSpan.FromMilliseconds(started++ * EntranceStepMs);
                        break;
                    case EntranceState.Entering:
                        if (gone) Hide(entry);
                        break;
                    case EntranceState.Done:
                        if (settle && !visible) Hide(entry);
                        else if (gone) Hide(entry);
                        break;
                }
            }
            if (started > 0) EnsureEntranceLoop();
        }

        /// <summary>Une seule montre pour toutes les tuiles qui entrent : le
        /// temps fait le fondu (opacité) et la montée (translation) image par
        /// image, cubique sortante — comme la Pile.</summary>
        private void EnsureEntranceLoop()
        {
            if (_entranceLoop) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                foreach (var entry in _entrances) if (entry.State == EntranceState.Entering) Land(entry);
                return;
            }
            _entranceLoop = true;
            Action frame = null;
            frame = delegate
            {
                var now = DateTime.Now;
                var pending = false;
                foreach (var entry in _entrances)
                {
                    if (entry.State != EntranceState.Entering) continue;
                    var t = Math.Max(0, Math.Min(1, (now - entry.Start).TotalMilliseconds / EntranceDurationMs));
                    var eased = 1 - Math.Pow(1 - t, 3);
                    entry.Wrapper.Opacity = eased;
                    entry.Slide.Y = EntranceRise * (1 - eased);
                    if (t >= 1) entry.State = EntranceState.Done;
                    else pending = true;
                }
                if (pending) top.RequestAnimationFrame(delegate { frame(); });
                else _entranceLoop = false;
            };
            frame();
        }

        /// <summary>Le nombre de tuiles en train d'entrer, et celles qui
        /// attendent hors de l'écran (sonde).</summary>
        internal int EnteringCardsForProbe()
        {
            var count = 0;
            foreach (var entry in _entrances) if (entry.State == EntranceState.Entering) count++;
            return count;
        }

        internal int PendingCardsForProbe()
        {
            var count = 0;
            foreach (var entry in _entrances) if (entry.State == EntranceState.Pending) count++;
            return count;
        }

        internal bool ScrollableForProbe { get { return _scroll.Extent.Height > _scroll.Viewport.Height + 60; } }
        internal void ScrollToForProbe(double offset) { _scroll.Offset = new Vector(0, Math.Max(0, Math.Min(offset, _scroll.Extent.Height - _scroll.Viewport.Height))); }
        internal double ScrollExtentForProbe { get { return _scroll.Extent.Height; } }

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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _entrances.Clear(); // une reconstruction oublie les enveloppes (elles meurent avec les rangées)
            try { RebuildRowsCore(); }
            finally
            {
                _entranceOn = false;
                // MARABOOK_LIBRARY_TIMING=1 : le temps de reconstruction sur la console (1.0.6).
                if (Environment.GetEnvironmentVariable("MARABOOK_LIBRARY_TIMING") == "1")
                    Console.WriteLine("[bibliothèque] " + _cardOrder.Count + " tuiles en " + watch.ElapsedMilliseconds + " ms");
            }
        }

        private void RebuildRowsCore()
        {
            _rows.Children.Clear();
            _cardsById.Clear();
            _cardOrder.Clear();
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
            RebuildTagChoices();
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
                foreach (var folder in folders) wrap.Children.Add(Entrance(BuildFolderCard(folder)));
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
                    Text = searching ? "Aucune fiche ne porte ce nom."
                        : _filterTag != null ? "Aucune fiche ne porte le tag « " + _filterTag + " »" + (_filterCategoryId != null ? " dans cette catégorie." : ".")
                        : "Aucune fiche dans cette catégorie.",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(8, 16, 0, 0)
                });
            }
        }

        /// <summary>Le sélecteur des tags (1.1.0) : « Tous les tags », puis
        /// chaque tag du projet ; un tag choisi qui a disparu retombe sur tous.</summary>
        private void RebuildTagChoices()
        {
            var tags = SheetTags.All(_project);
            if (_filterTag != null)
            {
                var still = false;
                foreach (var tag in tags) if (SheetTags.Same(tag, _filterTag)) { still = true; break; }
                if (!still) _filterTag = null;
            }
            _syncingTags = true;
            try
            {
                _tagBox.SelectedIndex = -1; // vider la sélection AVANT les éléments : le modèle de sélection énumère pendant Clear() (plantage du 26/10)
                _tagBox.Items.Clear();
                var all = new ComboBoxItem { Content = tags.Count == 0 ? "Aucun tag" : "Tous les tags", Tag = null };
                _tagBox.Items.Add(all);
                ComboBoxItem chosen = null;
                foreach (var tag in tags)
                {
                    var entry = new ComboBoxItem { Content = tag, Tag = tag };
                    _tagBox.Items.Add(entry);
                    if (_filterTag != null && SheetTags.Same(tag, _filterTag)) chosen = entry;
                }
                _tagBox.SelectedItem = chosen ?? all;
                _tagBox.IsEnabled = tags.Count > 0;
            }
            finally { _syncingTags = false; }
        }

        /// <summary>Sonde (1.1.0) : poser le filtre par tag (null = tous).</summary>
        internal void FilterByTagForProbe(string tag) { _filterTag = tag; RebuildRows(); }
        internal int TagChoicesForProbe { get { return _tagBox.Items.Count; } }
        internal int CardsCountForProbe { get { return _cardOrder.Count; } }

        /// <summary>Les fiches dont le nom contient la recherche (accents et
        /// casse pliés) et qui portent le tag choisi ; toutes sans l'un ni l'autre.</summary>
        private List<BinderItem> Matching(List<BinderItem> sheets, string needle)
        {
            if (needle.Length == 0 && _filterTag == null) return new List<BinderItem>(sheets);
            var list = new List<BinderItem>();
            foreach (var sheet in sheets)
            {
                if (needle.Length > 0 && !Correction.FrenchTokenizer.Fold(sheet.Title ?? "").Contains(needle)) continue;
                if (_filterTag != null && !SheetTags.Has(sheet, _filterTag)) continue;
                list.Add(sheet);
            }
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
            _chips.Children.Add(Chip("all", "Toutes", total, _filterCategoryId == null, false, false,
                "Toutes les fiches, catégorie par catégorie — " + Plural(total, "fiche", "fiches"),
                delegate { if (_filterCategoryId != null) { _filterCategoryId = null; RebuildRows(); } }, null));
            foreach (var category in _project.TopCategories()) // une pastille par catégorie d'ensemble, sous-catégories comptées (1.0.5)
            {
                var categoryRef = category;
                var count = CountWithSubs(byCategory, category);
                var selected = _filterCategoryId == category.Id;
                var chip = Chip(CategoryIcon(category, _project) ?? GenericCategoryIcon, category.Name, count, selected, count == 0,
                    !string.IsNullOrEmpty(category.Icon),
                    category.Name + " — " + (count == 0 ? "aucune fiche" : Plural(count, "fiche", "fiches")), // sans mode d'emploi (27/10, Rémi)
                    delegate
                    {
                        if (count == 0) { NewSheet(categoryRef.Id); return; }
                        _filterCategoryId = selected ? null : categoryRef.Id;
                        RebuildRows();
                    },
                    delegate(Control anchor) { ShowCategoryMenu(anchor, categoryRef); });
                MakeDropZone(chip, categoryRef, selected ? Chrome.Accent : (IBrush)Chrome.Border); // une tuile lâchée sur la pastille change de catégorie (1.1.0)
                _chips.Children.Add(chip);
            }
            if (uncategorized > 0)
            {
                var selected = _filterCategoryId == UncategorizedKey;
                _chips.Children.Add(Chip("file-dashed-bold", "Sans catégorie", uncategorized, selected, false, false,
                    "Sans catégorie — " + Plural(uncategorized, "fiche", "fiches"),
                    delegate { _filterCategoryId = selected ? null : UncategorizedKey; RebuildRows(); }, null));
            }
        }

        private static string Plural(int count, string one, string many)
        {
            return count + " " + (count <= 1 ? one : many);
        }

        /// <summary>Une pastille de filtre (1.1.0) : l'ICÔNE seule — le nom
        /// est dans l'infobulle —, le compte sur la pastille choisie (« + »
        /// sur une catégorie vide, qui crée), une astérisque en haut à droite
        /// quand l'icône est un choix de l'utilisateur.</summary>
        private static Border Chip(string icon, string name, int count, bool selected, bool empty, bool custom, string tip,
            Action click, Action<Control> rightClick)
        {
            var ink = selected ? Brushes.White : (IBrush)Chrome.Ink;
            var soft = selected ? Brushes.White : (IBrush)Chrome.SoftText;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var made = Icons.Make(icon, 15, empty ? soft : ink) as Control;
            if (made != null) { made.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(made); }
            if (selected || empty)
                content.Children.Add(new TextBlock
                {
                    Text = empty ? "+" : count.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = soft,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "count" // sonde
                });
            var host = new Grid { Children = { content } };
            if (custom)
                host.Children.Add(new TextBlock
                {
                    Text = "*",
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = selected ? Brushes.White : (IBrush)Chrome.Accent,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -7, -6, 0),
                    Tag = "custom-icon" // sonde
                });
            var chip = new Border
            {
                Child = host,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = selected ? Chrome.Accent : (IBrush)Chrome.BarBgLight,
                BorderBrush = selected ? Chrome.Accent : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "chip:" + name, // sonde
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

        /// <summary>Sonde (1.1.0) : les pastilles posées — « chip:Nom », et si
        /// chacune montre un compte ou une astérisque.</summary>
        internal List<string> ChipsForProbe()
        {
            var list = new List<string>();
            foreach (var child in _chips.Children)
            {
                var chip = child as Border;
                if (chip == null) continue;
                var label = chip.Tag as string ?? "";
                foreach (var block in chip.GetVisualDescendants().OfType<TextBlock>())
                    if ((block.Tag as string) == "count") label += "#" + block.Text;
                    else if ((block.Tag as string) == "custom-icon") label += "*";
                list.Add(label);
            }
            return list;
        }

        // ---------------------------------------------- glisser-déposer (1.1.0)

        /// <summary>Les fiches d'un glisser (les formats de la Pile : une ou
        /// plusieurs), hors corbeille, qui ne sont pas déjà dans la catégorie
        /// visée ; vide si rien à faire.</summary>
        private List<BinderItem> DroppableSheets(DragEventArgs e, SheetCategory target)
        {
            var result = new List<BinderItem>();
            if (_project == null) return result;
            var ids = new List<string>();
            if (e.Data.Contains("MarabookItems"))
            {
                var raw = e.Data.Get("MarabookItems") as string;
                if (raw != null) ids.AddRange(raw.Split('\n'));
            }
            else if (e.Data.Contains("MarabookItem")) ids.Add(e.Data.Get("MarabookItem") as string);
            foreach (var id in ids)
            {
                var item = _project.FindById(id);
                if (item == null || item.Kind != ItemKind.Sheet) continue;
                if (item.RootCategory() != null && item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                if (target != null && _project.SheetCategoryOf(item) == target) continue;
                result.Add(item);
            }
            return result;
        }

        /// <summary>Une zone qui reçoit des tuiles de fiches : sa bordure
        /// prend l'accent pendant le survol d'un glisser recevable, et le
        /// dépôt fait passer les fiches dans la catégorie (annulable).</summary>
        private void MakeDropZone(Border zone, SheetCategory target, IBrush rest)
        {
            zone[DragDrop.AllowDropProperty] = true;
            zone.AddHandler(DragDrop.DragOverEvent, delegate(object sender, DragEventArgs e)
            {
                if (e.Data.Contains(DataFormats.Files)) return;
                var sheets = DroppableSheets(e, target);
                e.DragEffects = sheets.Count > 0 ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                zone.BorderBrush = sheets.Count > 0 ? Chrome.Accent : rest;
            });
            zone.AddHandler(DragDrop.DragLeaveEvent, delegate { zone.BorderBrush = rest; });
            zone.AddHandler(DragDrop.DropEvent, delegate(object sender, DragEventArgs e)
            {
                zone.BorderBrush = rest;
                if (e.Data.Contains(DataFormats.Files)) return;
                var sheets = DroppableSheets(e, target);
                if (sheets.Count == 0) return;
                e.Handled = true;
                MoveToCategory(sheets, target);
            });
        }

        /// <summary>Les fiches passent dans la catégorie (ou sous-catégorie) :
        /// la même action annulable que « Changer de catégorie », en un cran
        /// pour un lot.</summary>
        private void MoveToCategory(List<BinderItem> sheets, SheetCategory target)
        {
            if (_project == null || target == null) return;
            var actions = new List<IUndoableAction>();
            foreach (var sheet in sheets)
            {
                if (sheet.CategoryId == target.Id) continue;
                var before = SheetSnapshot.Capture(_project, sheet);
                sheet.CategoryId = target.Id;
                actions.Add(new SheetEditAction(_project, sheet, before, SheetSnapshot.Capture(_project, sheet), "category"));
            }
            if (actions.Count == 0) return;
            _history.Push(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
            NotifyChanged();
            RebuildRows();
        }

        /// <summary>Sonde (1.1.0) : le dépôt de ces fiches sur cette catégorie.</summary>
        internal void DropOnCategoryForProbe(IEnumerable<string> ids, string categoryId)
        {
            var sheets = new List<BinderItem>();
            foreach (var id in ids) { var item = _project.FindById(id); if (item != null) sheets.Add(item); }
            MoveToCategory(sheets, _project.FindSheetCategory(categoryId));
        }

        /// <summary>Les fiches lâchées sur une tuile de dossier y entrent (la
        /// même action que la Pile, en un cran pour un lot).</summary>
        private void MoveIntoFolder(List<BinderItem> sheets, BinderItem folder)
        {
            var actions = new List<IUndoableAction>();
            foreach (var sheet in sheets)
            {
                if (sheet.Parent == folder || sheet == folder || folder.IsDescendantOf(sheet)) continue;
                var move = new MoveItemAction(sheet, folder, -1);
                move.Do();
                actions.Add(move);
            }
            if (actions.Count == 0) return;
            _history.Push(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
            NotifyChanged();
            RebuildRows();
        }

        /// <summary>Le départ d'un glisser depuis une tuile : la tuile seule,
        /// ou tout le lot si elle en fait partie — les formats de la Pile, qui
        /// reçoit donc aussi ces tuiles.</summary>
        private void OnCardMoved(object sender, PointerEventArgs e)
        {
            if (_dragCandidate == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragStart.X) < 4.0 && Math.Abs(position.Y - _dragStart.Y) < 4.0) return;
            var dragged = _dragCandidate;
            _dragCandidate = null;
            if (_selected.Count > 1 && _selected.Contains(dragged.Id))
            {
                var ids = new List<string>();
                foreach (var item in SelectedItems()) ids.Add(item.Id);
                DragDrop.DoDragDrop(e, Ui.DataOf("MarabookItems", string.Join("\n", ids.ToArray())), DragDropEffects.Move);
                return;
            }
            DragDrop.DoDragDrop(e, Ui.DataOf("MarabookItem", dragged.Id), DragDropEffects.Move);
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
                edit.Cursor = new Cursor(StandardCursorType.Arrow); // la flèche sur les trois points, la main sur la tuile (27/10, Rémi)
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
            if (category != null) title.Children.Add(PlusButton(category)); // « + » à côté du compte (1.1.0)
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
            if (category == null) { _rows.Children.Add(header); return; }
            // L'en-tête reçoit les tuiles lâchées (1.1.0) : un cadre discret
            // qui prend l'accent au survol d'un glisser.
            var zone = new Border
            {
                Child = header,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(-6, 0, 0, 0)
            };
            MakeDropZone(zone, category, Brushes.Transparent);
            _rows.Children.Add(zone);
        }

        /// <summary>Le « + » d'une catégorie ou d'une sous-catégorie (1.1.0,
        /// Rémi) : une fiche neuve dedans, sur son modèle de base — le nom
        /// seul est demandé.</summary>
        private Button PlusButton(SheetCategory category)
        {
            var categoryRef = category;
            // Petit (27/10, Rémi) : une croix de 11 px dans un carré de 20, calme, après le compte.
            var plus = new Button
            {
                Content = Icons.Make("plus-bold", 11, Chrome.SoftText),
                Width = 20,
                Height = 20,
                MinHeight = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                Classes = { Marabook.App.Theme.Owned, Marabook.App.Theme.Calm },
                [ToolTip.TipProperty] = "Nouvelle fiche dans « " + _project.CategoryPath(category) + " », sur son modèle de base"
            };
            plus.Tag = "plus:" + category.Id; // sonde
            plus.Click += delegate { NewSheetIn(categoryRef); };
            return plus;
        }

        private async void NewSheetIn(SheetCategory category)
        {
            if (_project == null || category == null) return;
            var name = await InputDialog.Ask(Ui.OwnerOf(this), "Nouvelle fiche",
                "Nom de la fiche, dans « " + _project.CategoryPath(category) + " » :", "");
            if (name == null || name.Trim().Length == 0) return;
            CreateSheetIn(category, name.Trim());
        }

        /// <summary>La fiche naît dans la portée courante, dans la catégorie,
        /// sur son modèle de base (hérité pour une sous-catégorie), puis s'ouvre.</summary>
        internal BinderItem CreateSheetIn(SheetCategory category, string title)
        {
            var item = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                TemplateId = _project.BaseTemplateIdOf(category),
                CategoryId = category.Id
            };
            _history.Run(new AddItemAction(_scope ?? _project.Category(Project.KeySheets), item, -1));
            NotifyChanged();
            RebuildRows();
            var handler = Navigate;
            if (handler != null) handler(item);
            return item;
        }

        // ------------------------------------------------------------ cartes

        /// <summary>La largeur de décodage des vignettes : deux fois la tuile
        /// (132 px), net sur un écran Retina.</summary>
        public const int ThumbnailWidth = 264;

        private void AddCards(List<BinderItem> sheets, SheetCategory category)
        {
            var wrap = new WrapPanel();
            foreach (var sheet in sheets)
                wrap.Children.Add(Entrance(BuildCard(sheet, category)));
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
            // Des tuiles de fiches lâchées sur le dossier y entrent (1.1.0).
            card[DragDrop.AllowDropProperty] = true;
            card.AddHandler(DragDrop.DragOverEvent, delegate(object sender, DragEventArgs e)
            {
                if (e.Data.Contains(DataFormats.Files)) return;
                var sheets = DroppableSheets(e, null);
                var any = false;
                foreach (var sheet in sheets) if (sheet.Parent != folderRef) { any = true; break; }
                e.DragEffects = any ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                card.BorderBrush = any ? Chrome.Accent : Chrome.Border;
            });
            card.AddHandler(DragDrop.DragLeaveEvent, delegate { card.BorderBrush = Chrome.Border; });
            card.AddHandler(DragDrop.DropEvent, delegate(object sender, DragEventArgs e)
            {
                card.BorderBrush = Chrome.Border;
                if (e.Data.Contains(DataFormats.Files)) return;
                var sheets = DroppableSheets(e, null);
                if (sheets.Count == 0) return;
                e.Handled = true;
                MoveIntoFolder(sheets, folderRef);
            });
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
            // 1.0.6 (14/10) : la vignette vient du cache (ImageCache.Thumbnail,
            // décodée une fois par image, pas à chaque reconstruction) et
            // elle est le FOND de la bordure arrondie — Avalonia remplit un
            // rectangle arrondi avec un ImageBrush, centré et rogné : plus de
            // Clip par chemin ni de Rectangle, qui coûtaient à chaque image
            // du défilement pour chaque tuile visible.
            var picture = new Border
            {
                Height = 108,
                Background = Chrome.BarBgLight,
                CornerRadius = new CornerRadius(3, 3, 0, 0)
            };
            var image = _project.FindImage(sheet.ImageId);
            var source = image == null ? null : ImageCache.Thumbnail(image, ThumbnailWidth);
            if (source != null)
                picture.Background = new ImageBrush(source)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            else
                picture.Child = CategoryPlaceholder(category, 44, _project);

            var grid = new Grid();
            grid.Children.Add(picture);
            // — la puce « modèle différent du modèle de base »
            // Pour une sous-catégorie, le modèle de base est celui de l'ensemble
            // (1.0.5-a : la puce marquait toutes ses fiches).
            if (category != null && sheet.TemplateId != _project.BaseTemplateIdOf(category))
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
            _cardOrder.Add(sheet.Id);
            if (_selected.Contains(sheet.Id)) PaintSelection(card, true);
            // Le DOUBLE-clic ouvre (29/09) ; le clic simple choisit la tuile
            // (Ctrl = plusieurs, 1.0.3), et le Général du rail montre la fiche
            // — comme les cartes des écrits. Le droit ne fait que le menu.
            card.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                // La tuile pressée est prête à glisser (1.1.0) ; le double-clic ouvre.
                _dragCandidate = sheetRef;
                _dragStart = e.GetPosition(this);
                if (e.ClickCount != 2) return;
                _dragCandidate = null;
                e.Handled = true;
                var handler = Navigate;
                if (handler != null) handler(sheetRef);
            };
            card.PointerMoved += OnCardMoved;
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (_dragCandidate != sheetRef) return; // un glisser est parti
                _dragCandidate = null;
                if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift && !Ui.HasCommand(e.KeyModifiers) && RangeSelect(sheetRef.Id)) { }
                else if (Ui.HasCommand(e.KeyModifiers)) { if (!_selected.Remove(sheetRef.Id)) _selected.Add(sheetRef.Id); _anchorId = sheetRef.Id; }
                else { _selected.Clear(); _selected.Add(sheetRef.Id); _anchorId = sheetRef.Id; }
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

        /// <summary>L'icône d'une catégorie : celle CHOISIE (1.1.0, Icon),
        /// sinon celle de son nom, sinon celle de sa catégorie d'ensemble
        /// pour une sous-catégorie sans icône à elle (1.0.5).</summary>
        public static string CategoryIcon(SheetCategory category, Project project)
        {
            if (category != null && !string.IsNullOrEmpty(category.Icon) && Icons.Has(category.Icon)) return category.Icon;
            var own = CategoryIcon(category);
            if (own != null || project == null) return own;
            return CategoryIcon(project.ParentOf(category));
        }

        /// <summary>Les icônes offertes au choix d'une catégorie (1.1.0) : les
        /// « fiche-* » du jeu, dans l'ordre alphabétique.</summary>
        public static List<string> ChoosableCategoryIcons()
        {
            var names = new List<string>();
            foreach (var name in Icons.Names) if (name.StartsWith("fiche-", StringComparison.Ordinal)) names.Add(name);
            names.Sort(StringComparer.Ordinal);
            return names;
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
                // Annulable (1.0.4) : la même action que les champs de la fiche.
                entry.Click += delegate { MoveToCategory(new List<BinderItem> { sheet }, categoryRef); };
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
                // La couleur (1.0.5-a) : le nuancier des dossiers, annulable.
                var colour = new MenuItem { Header = "Couleur", Icon = ColorMenus.Dot(category.Color, 1, Chrome.Border) };
                ColorMenus.Fill(colour, Ui.OwnerOf(this), _project, category.Color, delegate(string value)
                {
                    var previous = category.Color;
                    _history.Run(new SheetStructureAction("Couleur de la sous-catégorie", delegate { category.Color = value; }, delegate { category.Color = previous; }));
                    NotifyChanged();
                    RebuildRows();
                });
                menu.Items.Add(colour);
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

            // L'icône de la pastille (1.1.0) : une des icônes du jeu « fiche-* »,
            // marquée d'une astérisque ; « Selon le nom » rend celle d'origine.
            var iconMenu = new MenuItem
            {
                Header = "Icône de la catégorie",
                Icon = Icons.Make(CategoryIcon(category, _project) ?? GenericCategoryIcon, 14, Chrome.Ink),
                [ToolTip.TipProperty] = "L'icône de la pastille de filtre et des fiches sans image — une astérisque signale un choix"
            };
            var byName = new MenuItem { Header = "Selon le nom (par défaut)", IsChecked = string.IsNullOrEmpty(category.Icon) };
            byName.Click += delegate { SetCategoryIcon(category, null); };
            iconMenu.Items.Add(byName);
            iconMenu.Items.Add(new Separator());
            foreach (var name in ChoosableCategoryIcons())
            {
                var nameRef = name;
                var choice = new MenuItem
                {
                    Header = name.Substring("fiche-".Length),
                    Icon = Icons.Make(name, 14, Chrome.Ink),
                    IsChecked = category.Icon == name
                };
                choice.Click += delegate { SetCategoryIcon(category, nameRef); };
                iconMenu.Items.Add(choice);
            }
            menu.Items.Add(iconMenu);

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

        /// <summary>L'icône choisie d'une catégorie (1.1.0), annulable.</summary>
        internal void SetCategoryIcon(SheetCategory category, string icon)
        {
            if (category == null || category.Icon == icon) return;
            var previous = category.Icon;
            _history.Run(new SheetStructureAction("Icône de la catégorie",
                delegate { category.Icon = icon; }, delegate { category.Icon = previous; }));
            NotifyChanged();
            RebuildRows();
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
            var project = _project;
            // Le modèle de la catégorie (1.1.0, Rémi) : une case propose de le
            // supprimer avec elle — seulement s'il existe et qu'aucune autre
            // catégorie ni aucune fiche ne l'emploie.
            var template = sub ? null : project.FindTemplate(category.TemplateId);
            if (template != null && TemplateUsedElsewhere(template, category)) template = null;
            bool? withTemplate;
            if (template != null)
                withTemplate = await ConfirmCheckDialog.Ask(Ui.OwnerOf(this), "Supprimer la catégorie",
                    "Supprimer la catégorie « " + category.Name + " » ?",
                    "Supprimer aussi son modèle « " + template.Name + " » (aucune autre catégorie ni fiche ne l'utilise)", true);
            else
            {
                var answer = await MessageDialog.Show(Ui.OwnerOf(this),
                    "Supprimer " + (sub ? "la sous-catégorie « " : "la catégorie « ") + category.Name + " » ?\n"
                    + (sub ? "Ses champs propres disparaissent avec elle." : "Son modèle reste dans l'éditeur de modèles."),
                    "Marabook", MessageButtons.YesNo, MessageIcon.Question);
                withTemplate = answer == MessageResult.Yes ? (bool?)false : null;
            }
            if (withTemplate == null) return;
            DeleteCategoryNow(category, withTemplate == true ? template : null);
        }

        /// <summary>Le modèle est-il le modèle de base d'une autre catégorie,
        /// ou celui d'une fiche (corbeille comprise) ?</summary>
        private bool TemplateUsedElsewhere(SheetTemplate template, SheetCategory except)
        {
            foreach (var other in _project.SheetCategories)
                if (other != except && other.TemplateId == template.Id) return true;
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && item.TemplateId == template.Id) return true;
            return false;
        }

        /// <summary>La suppression, annulable en un cran : la catégorie, et
        /// son modèle si demandé, reprennent leur place au Ctrl+Z.</summary>
        internal void DeleteCategoryNow(SheetCategory category, SheetTemplate template)
        {
            var project = _project;
            var sub = category.IsSub && project.ParentOf(category) != null;
            var index = project.SheetCategories.IndexOf(category);
            var templateIndex = template == null ? -1 : project.Templates.IndexOf(template);
            _history.Run(new SheetStructureAction(sub ? "Sous-catégorie supprimée" : template != null ? "Catégorie et modèle supprimés" : "Catégorie supprimée",
                delegate
                {
                    project.SheetCategories.Remove(category);
                    if (template != null) project.Templates.Remove(template);
                },
                delegate
                {
                    project.SheetCategories.Insert(Math.Min(index, project.SheetCategories.Count), category);
                    if (template != null && !project.Templates.Contains(template))
                        project.Templates.Insert(Math.Max(0, Math.Min(templateIndex, project.Templates.Count)), template);
                }));
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
        // Les sous-catégories REPLIÉES (1.0.5-a, Rémi : « gagner de la place ») —
        // pour la session, partagé avec la Pile, par id de catégorie.
        internal static readonly HashSet<string> CollapsedCategories = new HashSet<string>();

        private void AddSubBox(SheetCategory sub, List<BinderItem> sheets, SheetCategory top)
        {
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(6, 0, 2, 4) };
            var subRef = sub;
            var collapsed = CollapsedCategories.Contains(sub.Id);
            var accent = sub.Color != null ? (Color?)Ink.Parse(sub.Color).ToColor() : null; // la couleur, comme un dossier (1.0.5-a)
            var edit = Buttons.Icon("dots-three-vertical-bold", "Modifier la sous-catégorie…", Buttons.Compact, Buttons.Look.Calm);
            edit.Cursor = new Cursor(StandardCursorType.Arrow);
            edit.Click += delegate { ShowCategoryMenu(edit, subRef); };
            DockPanel.SetDock(edit, Dock.Right);
            head.Children.Add(edit);
            var title = new StackPanel { Orientation = Orientation.Horizontal, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, [ToolTip.TipProperty] = collapsed ? "Déplier la sous-catégorie" : "Replier la sous-catégorie" };
            var caret = Icons.Make(collapsed ? "caret-right-bold" : "caret-down-bold", 11, Chrome.SoftText) as Control;
            if (caret != null) { caret.VerticalAlignment = VerticalAlignment.Center; caret.Margin = new Thickness(0, 0, 6, 0); title.Children.Add(caret); }
            title.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (!CollapsedCategories.Remove(subRef.Id)) CollapsedCategories.Add(subRef.Id);
                RebuildRows();
            };
            var icon = Icons.Make("folder-bold", 13, accent.HasValue ? (IBrush)new SolidColorBrush(accent.Value) : Chrome.SoftText) as Control;
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
            title.Children.Add(PlusButton(sub)); // « + » juste après le compte (27/10, Rémi) — le bouton consomme son clic, le repli ne bouge pas
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
            var wrap = new WrapPanel { Margin = new Thickness(2, 0, 2, 0), IsVisible = !collapsed };
            foreach (var sheet in sheets) wrap.Children.Add(Entrance(BuildCard(sheet, sub)));
            if (sheets.Count == 0)
                wrap.Children.Add(new TextBlock
                {
                    Text = "Rien ici — « + Nouvelle fiche », ou clic droit sur une carte → Changer de catégorie.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(6, 2, 0, 6)
                });
            dock.Children.Add(wrap);
            var rest = accent.HasValue ? (IBrush)new SolidColorBrush(accent.Value) : Chrome.Border;
            var box = new Border
            {
                BorderBrush = rest,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Background = accent.HasValue ? (IBrush)new SolidColorBrush(Chrome.Blend(accent.Value, Chrome.WindowBg.Color, 0.88)) : Brushes.Transparent,
                Margin = new Thickness(0, 10, 8, 0),
                Padding = new Thickness(6, collapsed ? 4 : 8, 6, collapsed ? 2 : 4),
                Child = dock,
                Tag = sub.Id // sonde
            };
            MakeDropZone(box, sub, rest); // une tuile lâchée dans la boîte bascule dans la sous-catégorie (1.1.0)
            _rows.Children.Add(box);
        }

        /// <summary>Sonde (1.0.5-a) : la boîte de cette sous-catégorie est-elle repliée (cartes cachées) ?</summary>
        internal bool SubBoxCollapsedForProbe(string id)
        {
            foreach (var child in _rows.Children)
            {
                var box = child as Border;
                if (box == null || (box.Tag as string) != id) continue;
                var dock = box.Child as DockPanel;
                return dock != null && dock.Children.Count > 1 && !dock.Children[1].IsVisible;
            }
            return false;
        }

        /// <summary>Les fiches manquantes (1.0.5-a) : les noms cochés deviennent
        /// des fiches vides dans la catégorie choisie, en un cran d'annulation.</summary>
        private async void ShowMissingSheets()
        {
            if (_project == null) return;
            var choices = await MissingSheetsDialog.Ask(Ui.OwnerOf(this), _project);
            if (choices == null || choices.Count == 0) return;
            var items = new List<BinderItem>();
            foreach (var choice in choices)
                items.Add(new BinderItem
                {
                    Kind = ItemKind.Sheet,
                    Title = choice.Name,
                    CategoryId = choice.Category != null ? choice.Category.Id : null,
                    TemplateId = _project.BaseTemplateIdOf(choice.Category)
                });
            _history.Run(new AddItemsAction(_scope ?? _project.Category(Project.KeySheets), items));
            NotifyChanged();
            RebuildRows();
        }

        private void NotifyChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
