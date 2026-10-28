using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE GRAPHE RELATIONNEL (1.1.0, 28/10, Rémi) : la carte de tout
    /// ce qui se relie dans le projet — relations typées entre fiches, liens
    /// [[…]], présences des fiches dans les écrits, livres et leurs chapitres,
    /// plans et leurs écrits, tags partagés. INFORMATIF : on y regarde, on y
    /// navigue (clic = le Général, double-clic = ouvrir), on n'y modifie
    /// rien des fiches. Trois facettes : la Carte (force dirigée, filtres par
    /// nature, recherche, voisinage d'un nœud), les Présences (la matrice
    /// fiches × écrits, les silences) et l'Hygiène (orphelins, îlots, jamais
    /// présents, absences longues). Tout se calcule À L'OUVERTURE, sous un
    /// indicateur — rien ne tourne en fond quand la vue est fermée.</summary>
    public class GraphView : DockPanel
    {
        public event Action<BinderItem> NavigateRequested; // double-clic, « Ouvrir »
        public event Action<BinderItem> NewTabRequested;
        public event Action<BinderItem> NodeSelected;      // le Général montre le nœud
        public event Action LayoutChanged;                 // un nœud tiré : le projet retient sa place

        private const double MinZoom = 0.2, MaxZoom = 3.0, LabelZoom = 0.55;
        private const int Seed = 1234;

        private Project _project;
        private RelationGraph _graph;
        private Dictionary<string, GraphPosition> _positions = new Dictionary<string, GraphPosition>();
        private readonly Dictionary<string, BinderItem> _items = new Dictionary<string, BinderItem>();
        private readonly Dictionary<string, int> _categoryIndex = new Dictionary<string, int>();
        private int _generation;
        private Task _loading;
        private bool _showTexts = true;
        private readonly Dictionary<GraphEdgeKind, bool> _kinds = new Dictionary<GraphEdgeKind, bool>();
        private readonly Dictionary<GraphEdgeKind, ToggleButton> _kindToggles = new Dictionary<GraphEdgeKind, ToggleButton>();

        // Le chrome
        private readonly Border _loader;
        private readonly TextBlock _loaderText;
        private readonly BusyIndicator _busy;
        private readonly StackPanel _facetButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        private readonly List<KeyValuePair<string, Button>> _facets = new List<KeyValuePair<string, Button>>();
        private readonly Grid _mapHost = new Grid();
        private readonly ScrollViewer _matrixHost;
        private readonly ScrollViewer _hygieneHost;
        private readonly StackPanel _hygiene = new StackPanel { Margin = new Thickness(24, 12, 24, 24), Spacing = 6 };
        private readonly PresenceMatrix _matrix;
        private readonly WrapPanel _mapTools = new WrapPanel { Orientation = Orientation.Horizontal }; // à la ligne quand la fenêtre est étroite
        private bool _fitPending;
        private readonly TextBox _search;
        private readonly ComboBox _depth;
        private readonly TextBlock _summary = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private string _facet = "Carte";

        // La carte
        private readonly Border _viewport;
        private readonly Canvas _world = new Canvas { ClipToBounds = false };
        private readonly EdgeLayer _edges;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _pan = new TranslateTransform(0, 0);
        private readonly Dictionary<string, NodeView> _nodes = new Dictionary<string, NodeView>();
        private string _selectedId;
        private HashSet<string> _focus;     // le voisinage affiché (null = tout)
        private HashSet<string> _hits;      // les nœuds qui répondent à la recherche (null = pas de recherche)
        private int _focusDepth;            // 0 = tout, 1, 2 sauts
        private Point _dragOrigin;
        private double _panStartX, _panStartY;
        private bool _panning;

        public GraphView()
        {
            Background = Chrome.WindowBg;
            foreach (GraphEdgeKind kind in Enum.GetValues(typeof(GraphEdgeKind))) _kinds[kind] = kind != GraphEdgeKind.Tag;

            // L'en-tête : titre, facettes, outils de la facette.
            var header = new StackPanel { Margin = new Thickness(24, 18, 24, 8), Spacing = 8 };
            var titleRow = new DockPanel();
            var title = new StackPanel();
            title.Children.Add(new TextBlock { Text = "Graphe relationnel", Foreground = Chrome.Ink, FontSize = 20, FontWeight = FontWeight.SemiBold });
            title.Children.Add(_summary);
            titleRow.Children.Add(title);
            DockPanel.SetDock(_facetButtons, Dock.Right);
            _facetButtons.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(_facetButtons);
            header.Children.Add(titleRow);
            AddFacet("Carte", "La carte des liens : relations, liens, présences, structure");
            AddFacet("Présences", "Qui paraît dans quel écrit — et depuis quand on ne l'a plus vu");
            AddFacet("Hygiène", "Fiches orphelines, îlots, jamais présentes, absences longues");

            // Les outils de la carte : les natures d'arêtes, les écrits, la
            // recherche, le voisinage, disposer à nouveau, exporter.
            AddKindToggle(GraphEdgeKind.Relation, "Relations", "Les relations typées entre fiches (diagramme relationnel)");
            AddKindToggle(GraphEdgeKind.Wiki, "Liens", "Les liens [[…]] posés dans les fiches et les écrits");
            AddKindToggle(GraphEdgeKind.Presence, "Présences", "Les fiches dont un nom apparaît dans un écrit (trait d'autant plus fort qu'elles y sont souvent)");
            AddKindToggle(GraphEdgeKind.Structure, "Livres", "Un livre et ses chapitres");
            AddKindToggle(GraphEdgeKind.Plan, "Plans", "Un plan et les écrits reliés à ses colonnes");
            AddKindToggle(GraphEdgeKind.Tag, "Tags", "Deux fiches qui partagent un tag");
            var texts = Buttons.TextToggle("Écrits", "Montrer les écrits, livres et plans — décoché : les fiches seules", Buttons.Compact);
            texts.IsChecked = true;
            texts.IsCheckedChanged += delegate { _showTexts = texts.IsChecked == true; RefreshVisibility(); };
            texts.Tag = "graph-texts";
            Tool(new Border { Width = 1, Height = 18, Background = Chrome.Border, Margin = new Thickness(4, 0, 4, 0) });
            Tool(texts);
            _search = new TextBox { Width = 160, Watermark = "Chercher un nœud…", Tag = "graph-search" };
            ToolTip.SetTip(_search, "Les nœuds dont le titre contient ce texte restent en lumière, les autres s'estompent (Échap efface)");
            _search.TextChanged += delegate { ApplySearch(); };
            _search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { _search.Text = ""; e.Handled = true; } };
            Tool(Ui.WithClear(_search, "Effacer la recherche"));
            _depth = new ComboBox { Width = 110, Tag = "graph-depth" };
            _depth.Items.Add("Tout");
            _depth.Items.Add("1 saut");
            _depth.Items.Add("2 sauts");
            _depth.SelectedIndex = 0;
            ToolTip.SetTip(_depth, "Le voisinage du nœud sélectionné : lui seul avec ses voisins directs, ou à deux sauts");
            _depth.SelectionChanged += delegate { _focusDepth = Math.Max(0, _depth.SelectedIndex); RefreshFocus(); };
            Tool(_depth);
            var relayout = Buttons.Icon("arrow-counter-clockwise", "Disposer à nouveau (les nœuds épinglés ne bougent pas)", Buttons.Compact, Buttons.Look.Calm);
            relayout.Tag = "graph-relayout";
            relayout.Click += delegate { Relayout(); };
            Tool(relayout);
            var fit = Buttons.Icon("grid", "Tout voir", Buttons.Compact, Buttons.Look.Calm);
            fit.Click += delegate { FitToView(); };
            Tool(fit);
            var export = Buttons.Icon("file-arrow-down-bold", "Exporter la carte en PNG (ce qui est affiché) ou en SVG (tout le graphe)…", Buttons.Compact, Buttons.Look.Calm);
            export.Tag = "graph-export";
            export.Click += async delegate { await Export(); };
            Tool(export);
            header.Children.Add(_mapTools);
            DockPanel.SetDock(header, Dock.Top);
            Children.Add(header);

            // Le corps : la carte, la matrice, l'hygiène — une à la fois — et le voile de chargement.
            var body = new Grid();
            _edges = new EdgeLayer(this);
            _world.Children.Add(_edges);
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_pan);
            _world.RenderTransform = group;
            _world.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
            var dots = new Grid();
            dots.Children.Add(new DotGrid());
            dots.Children.Add(_world);
            _viewport = new Border { ClipToBounds = true, Background = Brushes.Transparent, Child = dots, Focusable = true };
            _viewport.PointerPressed += OnBackgroundPressed;
            _viewport.PointerMoved += OnBackgroundMoved;
            _viewport.PointerReleased += OnBackgroundReleased;
            _viewport.PointerWheelChanged += OnWheel;
            // Le premier cadrage attend que la fenêtre ait une taille (la vue
            // vient d'être montrée : ses Bounds sont encore vides).
            _viewport.SizeChanged += delegate { if (_fitPending && _viewport.Bounds.Width > 50) FitToView(); };
            _viewport.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Select(null); _depth.SelectedIndex = 0; e.Handled = true; }
            };
            _mapHost.Children.Add(_viewport);
            body.Children.Add(_mapHost);
            _matrix = new PresenceMatrix(this);
            _matrixHost = new ScrollViewer { Content = _matrix, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, IsVisible = false };
            body.Children.Add(_matrixHost);
            _hygieneHost = new ScrollViewer { Content = _hygiene, IsVisible = false };
            body.Children.Add(_hygieneHost);
            _busy = new BusyIndicator(26);
            _loaderText = new TextBlock { Foreground = Chrome.SoftText, FontSize = 13, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var loaderStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            loaderStack.Children.Add(_busy);
            loaderStack.Children.Add(_loaderText);
            _loader = new Border { Background = Chrome.WindowBg, IsVisible = false, Child = loaderStack, Tag = "graph-loader" };
            body.Children.Add(_loader);
            Children.Add(body);
        }

        /// <summary>Un outil de la rangée de la carte, espacé des autres.</summary>
        private void Tool(Control control)
        {
            control.Margin = new Thickness(0, 0, 4, 4);
            control.VerticalAlignment = VerticalAlignment.Center;
            _mapTools.Children.Add(control);
        }

        private void AddFacet(string name, string tip)
        {
            var button = Buttons.Text(name, tip, Buttons.Compact, name == _facet ? Buttons.Look.Primary : Buttons.Look.Calm);
            button.Tag = "graph-facet:" + name;
            var nameRef = name;
            button.Click += delegate { ShowFacet(nameRef); };
            _facets.Add(new KeyValuePair<string, Button>(name, button));
            _facetButtons.Children.Add(button);
        }

        private void AddKindToggle(GraphEdgeKind kind, string label, string tip)
        {
            var toggle = Buttons.TextToggle(label, tip, Buttons.Compact);
            toggle.IsChecked = _kinds[kind];
            toggle.Tag = "graph-kind:" + kind;
            var kindRef = kind;
            toggle.IsCheckedChanged += delegate { _kinds[kindRef] = toggle.IsChecked == true; RefreshVisibility(); };
            _kindToggles[kind] = toggle;
            Tool(toggle);
        }

        /// <summary>Bascule de facette : les boutons se recolorent, un corps se montre.</summary>
        private void ShowFacet(string name)
        {
            _facet = name;
            for (var i = 0; i < _facets.Count; i++)
            {
                var pair = _facets[i];
                var fresh = Buttons.Text(pair.Key, ToolTip.GetTip(pair.Value) as string, Buttons.Compact, pair.Key == name ? Buttons.Look.Primary : Buttons.Look.Calm);
                fresh.Tag = pair.Value.Tag;
                var nameRef = pair.Key;
                fresh.Click += delegate { ShowFacet(nameRef); };
                _facetButtons.Children[i] = fresh;
                _facets[i] = new KeyValuePair<string, Button>(pair.Key, fresh);
            }
            _mapHost.IsVisible = name == "Carte";
            _mapTools.IsVisible = name == "Carte";
            _matrixHost.IsVisible = name == "Présences";
            _hygieneHost.IsVisible = name == "Hygiène";
            if (name == "Présences") _matrix.InvalidateMeasure();
        }

        // ================================================================ chargement

        /// <summary>Ouvre le graphe du projet : la photographie se prend ici
        /// (fil d'interface, rapide), le comptage des présences et la
        /// disposition tournent sur un fil de travail SOUS LE VOILE — l'app
        /// n'a rien d'autre à faire pendant ce temps, aucun hoquet ailleurs.</summary>
        public void Load(Project project)
        {
            _project = project;
            var generation = ++_generation;
            _items.Clear();
            _nodes.Clear();
            _world.Children.Clear();
            _world.Children.Add(_edges);
            _graph = null;
            _selectedId = null;
            _focus = null;
            _loader.IsVisible = true;
            _loaderText.Text = "Lecture des fiches et des écrits…";
            _busy.Begin();
            if (project == null) { _busy.End(); _loader.IsVisible = false; _summary.Text = ""; _loading = Task.CompletedTask; return; }
            var entries = GraphInput.Capture(project);
            foreach (var entry in entries)
            {
                var item = project.FindById(entry.Id);
                if (item != null) _items[entry.Id] = item;
            }
            _categoryIndex.Clear();
            for (var i = 0; i < project.SheetCategories.Count; i++) _categoryIndex[project.SheetCategories[i].Id] = i;
            var known = new Dictionary<string, GraphPosition>();
            foreach (var pair in project.GraphLayout) known[pair.Key] = new GraphPosition { X = pair.Value.X, Y = pair.Value.Y, Pinned = pair.Value.Pinned };
            var options = new GraphOptions { Tags = true, Texts = true }; // tout : les filtres jouent à l'affichage
            var completion = new TaskCompletionSource<bool>();
            _loading = completion.Task;
            Task.Run(delegate
            {
                RelationGraph graph = null;
                Dictionary<string, GraphPosition> positions = null;
                Exception failure = null;
                try
                {
                    graph = RelationGraph.Build(entries, options);
                    var warm = known.Count >= graph.Nodes.Count * 0.9 && known.Count > 0;
                    positions = GraphLayout.Arrange(graph, known, GraphLayout.IterationsFor(graph.Nodes.Count, warm), Seed);
                }
                catch (Exception error) { failure = error; }
                Dispatcher.UIThread.Post(delegate
                {
                    try
                    {
                        if (generation != _generation) return;
                        if (failure != null)
                        {
                            _loaderText.Text = "Le graphe n'a pas pu être calculé : " + failure.Message;
                            _busy.End();
                            return;
                        }
                        Apply(graph, positions);
                    }
                    finally { completion.TrySetResult(true); }
                });
            });
        }

        private void Apply(RelationGraph graph, Dictionary<string, GraphPosition> positions)
        {
            _graph = graph;
            _positions = positions;
            // Le projet retient la disposition (sans salir : relire n'est pas
            // modifier — un nœud tiré, lui, salit).
            if (_project != null)
            {
                _project.GraphLayout.Clear();
                foreach (var pair in positions) _project.GraphLayout[pair.Key] = pair.Value;
            }
            BuildNodes();
            RefreshVisibility();
            _fitPending = true;
            FitToView();
            BuildHygiene();
            _matrix.Reload();
            _busy.End();
            _loader.IsVisible = false;
            var sheets = 0; var texts = 0;
            foreach (var node in graph.Nodes) { if (node.IsSheet) sheets++; else if (node.IsText) texts++; }
            _summary.Text = graph.Nodes.Count + " nœuds (" + sheets + " fiches, " + texts + " écrits), " + graph.Edges.Count + " liens";
        }

        /// <summary>Recalcule tout (après un changement de structure).</summary>
        public void Refresh()
        {
            if (_project != null) Load(_project);
        }

        // ================================================================ la carte

        private void BuildNodes()
        {
            _world.Children.Clear();
            _world.Children.Add(_edges);
            _nodes.Clear();
            foreach (var node in _graph.Nodes)
            {
                GraphPosition at;
                if (!_positions.TryGetValue(node.Id, out at)) continue;
                BinderItem item;
                _items.TryGetValue(node.Id, out item);
                var view = new NodeView(this, node, item, NodeBrush(node));
                _nodes[node.Id] = view;
                _world.Children.Add(view.Label);
                _world.Children.Add(view);
                view.Place(at.X, at.Y);
            }
            _edges.Invalidate();
        }

        /// <summary>La couleur d'un nœud : une teinte par catégorie de fiche
        /// (la couleur de carte de la fiche si elle en a une), gris pour un
        /// écrit, ambre pour un livre, vert pour un plan.</summary>
        private IBrush NodeBrush(GraphNode node)
        {
            if (node.Kind == ItemKind.Text) return Chrome.SoftText;
            if (node.Kind == ItemKind.Book) return Chrome.Warn;
            if (node.Kind == ItemKind.Plan) return Chrome.Ok;
            if (!string.IsNullOrEmpty(node.Color))
            {
                try { return new SolidColorBrush(Ink.Parse(node.Color).ToColor()); }
                catch (Exception) { }
            }
            int index;
            if (node.CategoryId == null || !_categoryIndex.TryGetValue(node.CategoryId, out index)) return Chrome.Accent;
            var hue = (Chrome.HueOf(Chrome.Accent.Color) + index * 47) % 360;
            return new SolidColorBrush(Chrome.Tint(hue, 0.55, Chrome.Dark ? 0.5 : 0.56));
        }

        internal static IBrush EdgeBrush(GraphEdgeKind kind)
        {
            switch (kind)
            {
                case GraphEdgeKind.Relation: return Chrome.Accent;
                case GraphEdgeKind.Wiki: return Chrome.SoftText;
                case GraphEdgeKind.Presence: return Chrome.AccentSoft;
                case GraphEdgeKind.Structure: return Chrome.Warn;
                case GraphEdgeKind.Plan: return Chrome.Ok;
                default: return Chrome.FaintText;
            }
        }

        internal static string KindName(GraphEdgeKind kind)
        {
            switch (kind)
            {
                case GraphEdgeKind.Relation: return "relation";
                case GraphEdgeKind.Wiki: return "lien";
                case GraphEdgeKind.Presence: return "présence";
                case GraphEdgeKind.Structure: return "livre";
                case GraphEdgeKind.Plan: return "plan";
                default: return "tag";
            }
        }

        /// <summary>Une arête compte-t-elle à l'affichage ? Sa nature est
        /// cochée, ses deux bouts sont des nœuds montrés.</summary>
        internal bool EdgeShown(GraphEdge edge)
        {
            if (!_kinds[edge.Kind]) return false;
            return NodeShown(edge.From) && NodeShown(edge.To);
        }

        internal bool NodeShown(string id)
        {
            var node = _graph == null ? null : _graph.Node(id);
            if (node == null) return false;
            if (!_showTexts && !node.IsSheet) return false;
            if (_focus != null && !_focus.Contains(id)) return false;
            return true;
        }

        /// <summary>Un nœud montré est-il en lumière ? Sans recherche ni
        /// sélection, tous ; avec une sélection, elle et ses voisins par les
        /// arêtes affichées ; avec une recherche, ceux qui répondent.</summary>
        internal bool NodeLit(string id)
        {
            if (_hits != null) return _hits.Contains(id);
            if (_selectedId == null) return true;
            if (id == _selectedId) return true;
            foreach (var edge in _graph.EdgesOf(_selectedId))
                if (_kinds[edge.Kind] && edge.Other(_selectedId) == id) return true;
            return false;
        }

        internal bool EdgeLit(GraphEdge edge)
        {
            if (_hits != null) return _hits.Contains(edge.From) || _hits.Contains(edge.To);
            if (_selectedId == null) return true;
            return edge.From == _selectedId || edge.To == _selectedId;
        }

        private void RefreshVisibility()
        {
            if (_graph == null) return;
            var labels = _scale.ScaleX >= LabelZoom;
            foreach (var pair in _nodes)
            {
                var shown = NodeShown(pair.Key);
                pair.Value.IsVisible = shown;
                pair.Value.Label.IsVisible = shown && labels;
                var lit = NodeLit(pair.Key);
                pair.Value.Opacity = lit ? 1 : 0.18;
                pair.Value.Label.Opacity = lit ? 1 : 0.25;
                pair.Value.SetSelected(pair.Key == _selectedId);
            }
            _edges.Invalidate();
        }

        private void RefreshFocus()
        {
            if (_graph == null) return;
            if (_focusDepth == 0 || _selectedId == null) _focus = null;
            else
            {
                var self = this;
                _focus = _graph.Neighborhood(_selectedId, _focusDepth, delegate(GraphEdge e) { return self._kinds[e.Kind]; });
            }
            RefreshVisibility();
        }

        private void ApplySearch()
        {
            var query = (_search.Text ?? "").Trim();
            if (query.Length == 0 || _graph == null) _hits = null;
            else
            {
                _hits = new HashSet<string>();
                foreach (var node in _graph.Nodes)
                    if (node.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) _hits.Add(node.Id);
            }
            RefreshVisibility();
        }

        /// <summary>Sélectionne un nœud (null = rien) : lui et ses voisins en
        /// lumière, le Général le montre, le voisinage suit la profondeur choisie.</summary>
        internal void Select(string id)
        {
            _selectedId = id;
            RefreshFocus();
            BinderItem item;
            if (id != null && _items.TryGetValue(id, out item))
            {
                var handler = NodeSelected;
                if (handler != null) handler(item);
            }
        }

        internal void Open(string id, bool newTab)
        {
            BinderItem item;
            if (id == null || !_items.TryGetValue(id, out item)) return;
            var handler = newTab ? NewTabRequested : NavigateRequested;
            if (handler != null) handler(item);
        }

        internal void ShowMenu(NodeView view)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Ouvrir" };
            open.Click += delegate { Open(view.Node.Id, false); };
            menu.Items.Add(open);
            var tab = new MenuItem { Header = "Ouvrir dans un nouvel onglet" };
            tab.Click += delegate { Open(view.Node.Id, true); };
            menu.Items.Add(tab);
            menu.Items.Add(new Separator());
            var one = new MenuItem { Header = "Voisinage à un saut" };
            one.Click += delegate { Select(view.Node.Id); _depth.SelectedIndex = 1; };
            menu.Items.Add(one);
            var two = new MenuItem { Header = "Voisinage à deux sauts" };
            two.Click += delegate { Select(view.Node.Id); _depth.SelectedIndex = 2; };
            menu.Items.Add(two);
            var all = new MenuItem { Header = "Tout le graphe", IsEnabled = _focus != null };
            all.Click += delegate { _depth.SelectedIndex = 0; };
            menu.Items.Add(all);
            GraphPosition at;
            if (_positions.TryGetValue(view.Node.Id, out at) && at.Pinned)
            {
                menu.Items.Add(new Separator());
                var free = new MenuItem { Header = "Libérer la position" };
                free.Click += delegate { at.Pinned = false; RaiseLayoutChanged(); };
                menu.Items.Add(free);
            }
            Ui.ShowMenu(menu, view);
        }

        /// <summary>Un nœud tiré : sa place est à lui (épinglée), le projet est sale.</summary>
        internal void Moved(NodeView view, double x, double y, bool done)
        {
            GraphPosition at;
            if (!_positions.TryGetValue(view.Node.Id, out at)) return;
            at.X = Math.Round(x, 1);
            at.Y = Math.Round(y, 1);
            view.Place(at.X, at.Y);
            _edges.Invalidate();
            if (done) { at.Pinned = true; RaiseLayoutChanged(); }
        }

        private void RaiseLayoutChanged()
        {
            var handler = LayoutChanged;
            if (handler != null) handler();
        }

        internal GraphPosition PositionOf(string id)
        {
            GraphPosition at;
            return _positions.TryGetValue(id, out at) ? at : null;
        }

        internal RelationGraph Graph { get { return _graph; } }
        internal double Zoom { get { return _scale.ScaleX; } }
        internal string SelectedId { get { return _selectedId; } }

        /// <summary>Disposer à nouveau : les épinglés restent, le reste se replace.</summary>
        private void Relayout()
        {
            if (_graph == null) return;
            var pinned = new Dictionary<string, GraphPosition>();
            foreach (var pair in _positions) if (pair.Value.Pinned) pinned[pair.Key] = pair.Value;
            var graph = _graph;
            var generation = _generation;
            _loader.IsVisible = true;
            _loaderText.Text = "Disposition…";
            _busy.Begin();
            Task.Run(delegate
            {
                var positions = GraphLayout.Arrange(graph, pinned, GraphLayout.IterationsFor(graph.Nodes.Count, false), Seed + 1);
                Dispatcher.UIThread.Post(delegate
                {
                    if (generation != _generation) return;
                    _positions = positions;
                    if (_project != null)
                    {
                        _project.GraphLayout.Clear();
                        foreach (var pair in positions) _project.GraphLayout[pair.Key] = pair.Value;
                    }
                    foreach (var pair in _nodes)
                    {
                        GraphPosition at;
                        if (positions.TryGetValue(pair.Key, out at)) pair.Value.Place(at.X, at.Y);
                    }
                    _edges.Invalidate();
                    FitToView();
                    _busy.End();
                    _loader.IsVisible = false;
                });
            });
        }

        /// <summary>Tout le graphe dans la fenêtre, avec une marge.</summary>
        internal void FitToView()
        {
            if (_graph == null || _positions.Count == 0) return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var pair in _positions)
            {
                if (!NodeShown(pair.Key)) continue;
                minX = Math.Min(minX, pair.Value.X); maxX = Math.Max(maxX, pair.Value.X);
                minY = Math.Min(minY, pair.Value.Y); maxY = Math.Max(maxY, pair.Value.Y);
            }
            if (minX == double.MaxValue) return;
            if (_viewport.Bounds.Width < 50 || _viewport.Bounds.Height < 50) return; // pas encore posée : SizeChanged rappellera
            _fitPending = false;
            var width = _viewport.Bounds.Width;
            var height = _viewport.Bounds.Height;
            var spanX = Math.Max(120, maxX - minX + 160);
            var spanY = Math.Max(120, maxY - minY + 160);
            var zoom = Math.Max(MinZoom, Math.Min(1.2, Math.Min(width / spanX, height / spanY)));
            SetZoom(zoom);
            _pan.X = width / 2 - (minX + maxX) / 2 * zoom;
            _pan.Y = height / 2 - (minY + maxY) / 2 * zoom;
        }

        /// <summary>Centre la vue sur un nœud (hygiène, matrice).</summary>
        internal void CenterOn(string id)
        {
            GraphPosition at;
            if (!_positions.TryGetValue(id, out at)) return;
            var zoom = Math.Max(_scale.ScaleX, 0.9);
            SetZoom(zoom);
            _pan.X = _viewport.Bounds.Width / 2 - at.X * zoom;
            _pan.Y = _viewport.Bounds.Height / 2 - at.Y * zoom;
        }

        private void SetZoom(double zoom)
        {
            var before = _scale.ScaleX >= LabelZoom;
            _scale.ScaleX = zoom;
            _scale.ScaleY = zoom;
            if (before != zoom >= LabelZoom) RefreshVisibility();
        }

        private void OnWheel(object sender, PointerWheelEventArgs e)
        {
            if (_graph == null) return;
            var at = e.GetPosition(_viewport);
            var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
            var zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _scale.ScaleX * factor));
            // Le point sous la souris reste sous la souris.
            var worldX = (at.X - _pan.X) / _scale.ScaleX;
            var worldY = (at.Y - _pan.Y) / _scale.ScaleY;
            SetZoom(zoom);
            _pan.X = at.X - worldX * zoom;
            _pan.Y = at.Y - worldY * zoom;
            e.Handled = true;
        }

        private void OnBackgroundPressed(object sender, PointerPressedEventArgs e)
        {
            _viewport.Focus();
            var point = e.GetCurrentPoint(_viewport);
            if (!point.Properties.IsLeftButtonPressed) return;
            _panning = true;
            _dragOrigin = point.Position;
            _panStartX = _pan.X; _panStartY = _pan.Y;
            e.Pointer.Capture(_viewport);
            e.Handled = true;
        }

        private void OnBackgroundMoved(object sender, PointerEventArgs e)
        {
            if (!_panning || e.Pointer.Captured != _viewport) return;
            var now = e.GetPosition(_viewport);
            _pan.X = _panStartX + now.X - _dragOrigin.X;
            _pan.Y = _panStartY + now.Y - _dragOrigin.Y;
        }

        private void OnBackgroundReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_panning) return;
            _panning = false;
            var now = e.GetPosition(_viewport);
            var moved = Math.Abs(now.X - _dragOrigin.X) + Math.Abs(now.Y - _dragOrigin.Y);
            if (e.Pointer.Captured == _viewport) e.Pointer.Capture(null);
            if (moved < 3) Select(null); // un clic dans le vide désélectionne
            e.Handled = true;
        }

        /// <summary>Les coordonnées du monde d'un point de la fenêtre.</summary>
        internal Point ToWorld(Point viewportPoint)
        {
            return new Point((viewportPoint.X - _pan.X) / _scale.ScaleX, (viewportPoint.Y - _pan.Y) / _scale.ScaleY);
        }

        internal Border Viewport { get { return _viewport; } }

        // ================================================================ l'hygiène

        private void BuildHygiene()
        {
            _hygiene.Children.Clear();
            if (_graph == null) return;
            var self = this;
            Func<GraphEdge, bool> shown = delegate(GraphEdge e) { return self._kinds[e.Kind]; };
            var orphans = _graph.Orphans(shown);
            HygieneSection("Fiches et écrits orphelins", orphans.Count,
                "Aucun lien d'aucune nature : ni relation, ni lien [[…]], ni présence dans un écrit. Souvent des fiches en friche — ou des noms mal orthographiés dans le texte.",
                orphans, null);
            var components = _graph.Components(shown);
            if (components.Count > 1)
            {
                var islands = new List<GraphNode>();
                var notes = new Dictionary<string, string>();
                for (var i = 1; i < components.Count; i++)
                    foreach (var node in components[i]) { islands.Add(node); notes[node.Id] = "îlot " + i + " (" + components[i].Count + " nœuds)"; }
                HygieneSection("Îlots à l'écart", islands.Count,
                    "Des groupes reliés entre eux mais coupés du reste : " + (components.Count - 1) + " îlot(s) à côté du grand ensemble de " + components[0].Count + " nœuds.",
                    islands, notes);
            }
            else HygieneSection("Îlots à l'écart", 0, "Tout ce qui est relié ne forme qu'un seul ensemble.", new List<GraphNode>(), null);
            var never = _graph.NeverPresent();
            HygieneSection("Fiches jamais présentes dans un écrit", never.Count,
                "Aucun de leurs noms (titre, Nom, Prénom, Alias) n'apparaît dans les écrits du récit.",
                never, null);
            var absences = _graph.Absences();
            var longOnes = new List<GraphNode>();
            var since = new Dictionary<string, string>();
            foreach (var absence in absences)
            {
                if (absence.Since < 2) continue;
                longOnes.Add(absence.Sheet);
                since[absence.Sheet.Id] = "absente depuis " + absence.Since + " écrits (vue dans " + absence.Appearances + ")";
            }
            HygieneSection("Absences longues", longOnes.Count,
                "Des fiches qui ont paru, puis plus rien depuis au moins deux écrits — dans l'ordre du récit.",
                longOnes, since);
        }

        private void HygieneSection(string caption, int count, string help, List<GraphNode> nodes, Dictionary<string, string> notes)
        {
            var head = new TextBlock { Text = caption + "  (" + count + ")", Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0), Tag = "graph-hygiene:" + caption };
            _hygiene.Children.Add(head);
            _hygiene.Children.Add(new TextBlock { Text = help, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            if (nodes.Count == 0)
            {
                _hygiene.Children.Add(new TextBlock { Text = "Rien à signaler.", Foreground = Chrome.FaintText, FontSize = 12, FontStyle = FontStyle.Italic });
                return;
            }
            var wrap = new WrapPanel();
            foreach (var node in nodes)
            {
                BinderItem item;
                _items.TryGetValue(node.Id, out item);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var icon = item == null ? null : ItemIcons.Render(item, 13, Chrome.SoftText);
                if (icon != null) { icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon); }
                row.Children.Add(new TextBlock { Text = node.Title.Length == 0 ? "Sans titre" : node.Title, Foreground = Chrome.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                string note;
                if (notes != null && notes.TryGetValue(node.Id, out note))
                    row.Children.Add(new TextBlock { Text = note, Foreground = Chrome.FaintText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                var chip = new Border
                {
                    Background = Chrome.BarBgLight,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = row
                };
                ToolTip.SetTip(chip, "Voir sur la carte (double-clic : ouvrir)");
                var idRef = node.Id;
                chip.PointerEntered += delegate { chip.Background = Chrome.AccentTint; };
                chip.PointerExited += delegate { chip.Background = Chrome.BarBgLight; };
                chip.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    if (e.ClickCount >= 2) { Open(idRef, false); e.Handled = true; return; }
                };
                chip.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    ShowFacet("Carte");
                    Select(idRef);
                    CenterOn(idRef);
                };
                wrap.Children.Add(chip);
            }
            _hygiene.Children.Add(wrap);
        }

        // ================================================================ export

        private async Task Export()
        {
            if (_graph == null) return;
            var path = await Ui.PickSaveFile(this, "Exporter le graphe", "Image PNG (*.png)|*.png|Image SVG (*.svg)|*.svg", "graphe.png");
            if (path == null) return;
            try
            {
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) System.IO.File.WriteAllText(path, Svg(), Encoding.UTF8);
                else ExportPng(path);
            }
            catch (Exception error)
            {
                await MessageDialog.Show(TopLevel.GetTopLevel(this) as Window, "L'export a échoué : " + error.Message, "Export du graphe", MessageButtons.OK, MessageIcon.Error);
            }
        }

        public void ExportPng(string path)
        {
            var size = new PixelSize(Math.Max(1, (int)_viewport.Bounds.Width), Math.Max(1, (int)_viewport.Bounds.Height));
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96)))
            {
                bitmap.Render(_viewport);
                bitmap.Save(path);
            }
        }

        /// <summary>Tout le graphe affiché, en SVG : les arêtes puis les nœuds
        /// avec leur titre — une image vectorielle pour la bible du monde.</summary>
        public string Svg()
        {
            var sb = new StringBuilder();
            if (_graph == null) return "";
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var pair in _positions)
            {
                if (!NodeShown(pair.Key)) continue;
                minX = Math.Min(minX, pair.Value.X); maxX = Math.Max(maxX, pair.Value.X);
                minY = Math.Min(minY, pair.Value.Y); maxY = Math.Max(maxY, pair.Value.Y);
            }
            if (minX == double.MaxValue) { minX = minY = 0; maxX = maxY = 100; }
            var pad = 80.0;
            var width = maxX - minX + 2 * pad;
            var height = maxY - minY + 2 * pad;
            var ci = CultureInfo.InvariantCulture;
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width.ToString("0", ci)).Append("\" height=\"").Append(height.ToString("0", ci))
              .Append("\" viewBox=\"").Append((minX - pad).ToString("0.#", ci)).Append(' ').Append((minY - pad).ToString("0.#", ci)).Append(' ').Append(width.ToString("0.#", ci)).Append(' ').Append(height.ToString("0.#", ci)).Append("\" font-family=\"sans-serif\">\n");
            sb.Append("<rect x=\"").Append((minX - pad).ToString("0.#", ci)).Append("\" y=\"").Append((minY - pad).ToString("0.#", ci)).Append("\" width=\"").Append(width.ToString("0.#", ci)).Append("\" height=\"").Append(height.ToString("0.#", ci)).Append("\" fill=\"").Append(Chrome.Hex(Chrome.WindowBg.Color)).Append("\"/>\n");
            foreach (var edge in _graph.Edges)
            {
                if (!EdgeShown(edge)) continue;
                var a = _positions[edge.From]; var b = _positions[edge.To];
                var brush = EdgeBrush(edge.Kind) as SolidColorBrush;
                var color = brush == null ? "#888888" : Chrome.Hex(brush.Color);
                var widthPx = edge.Kind == GraphEdgeKind.Presence ? Math.Min(4, 1 + Math.Log(edge.Weight)) : edge.Kind == GraphEdgeKind.Relation ? 1.8 : 1.0;
                sb.Append("<line x1=\"").Append(a.X.ToString("0.#", ci)).Append("\" y1=\"").Append(a.Y.ToString("0.#", ci)).Append("\" x2=\"").Append(b.X.ToString("0.#", ci)).Append("\" y2=\"").Append(b.Y.ToString("0.#", ci))
                  .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(widthPx.ToString("0.#", ci)).Append("\" stroke-opacity=\"0.7\"")
                  .Append(edge.Kind == GraphEdgeKind.Wiki || edge.Kind == GraphEdgeKind.Tag ? " stroke-dasharray=\"4 3\"" : "").Append("/>\n");
                if (edge.Kind == GraphEdgeKind.Relation && edge.Label.Length > 0)
                    sb.Append("<text x=\"").Append(((a.X + b.X) / 2).ToString("0.#", ci)).Append("\" y=\"").Append(((a.Y + b.Y) / 2 - 3).ToString("0.#", ci)).Append("\" font-size=\"9\" fill=\"").Append(color).Append("\" text-anchor=\"middle\">").Append(Escape(edge.Label)).Append("</text>\n");
            }
            foreach (var node in _graph.Nodes)
            {
                if (!NodeShown(node.Id)) continue;
                var at = _positions[node.Id];
                var brush = NodeBrush(node) as SolidColorBrush;
                var color = brush == null ? "#5B67D8" : Chrome.Hex(brush.Color);
                var r = NodeView.RadiusOf(node);
                sb.Append("<circle cx=\"").Append(at.X.ToString("0.#", ci)).Append("\" cy=\"").Append(at.Y.ToString("0.#", ci)).Append("\" r=\"").Append(r.ToString("0.#", ci)).Append("\" fill=\"").Append(color).Append("\" stroke=\"").Append(Chrome.Hex(Chrome.PaperBg.Color)).Append("\" stroke-width=\"2\"/>\n");
                sb.Append("<text x=\"").Append(at.X.ToString("0.#", ci)).Append("\" y=\"").Append((at.Y + r + 12).ToString("0.#", ci)).Append("\" font-size=\"11\" fill=\"").Append(Chrome.Hex(Chrome.Ink.Color)).Append("\" text-anchor=\"middle\">").Append(Escape(node.Title)).Append("</text>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            return (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // ================================================================ sonde

        public Task LoadingForProbe { get { return _loading ?? Task.CompletedTask; } }
        public RelationGraph GraphForProbe { get { return _graph; } }
        public string DebugForProbe
        {
            get
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var pair in _positions) { minX = Math.Min(minX, pair.Value.X); maxX = Math.Max(maxX, pair.Value.X); minY = Math.Min(minY, pair.Value.Y); maxY = Math.Max(maxY, pair.Value.Y); }
                return "zoom=" + _scale.ScaleX.ToString("0.00") + " pan=(" + _pan.X.ToString("0") + "," + _pan.Y.ToString("0") + ") viewport=" + _viewport.Bounds.Width.ToString("0") + "x" + _viewport.Bounds.Height.ToString("0")
                    + " world=" + _world.Bounds.Width.ToString("0") + "x" + _world.Bounds.Height.ToString("0") + " x[" + minX.ToString("0") + ".." + maxX.ToString("0") + "] y[" + minY.ToString("0") + ".." + maxY.ToString("0") + "] pending=" + _fitPending;
            }
        }
        public bool LoaderShownForProbe { get { return _loader.IsVisible; } }
        public int NodeCountForProbe { get { return _graph == null ? 0 : _graph.Nodes.Count; } }
        public int EdgeCountForProbe { get { return _graph == null ? 0 : _graph.Edges.Count; } }
        public int ShownNodeCountForProbe { get { var n = 0; foreach (var pair in _nodes) if (pair.Value.IsVisible) n++; return n; } }
        public int LitNodeCountForProbe { get { var n = 0; foreach (var pair in _nodes) if (pair.Value.IsVisible && pair.Value.Opacity > 0.5) n++; return n; } }
        public int ShownEdgeCountForProbe { get { var n = 0; if (_graph != null) foreach (var edge in _graph.Edges) if (EdgeShown(edge)) n++; return n; } }
        public string FacetForProbe { get { return _facet; } }
        public void ShowFacetForProbe(string name) { ShowFacet(name); }
        public void SelectForProbe(string id) { Select(id); }
        public void SearchForProbe(string text) { _search.Text = text; }
        public void DepthForProbe(int depth) { _depth.SelectedIndex = depth; }
        public void ToggleKindForProbe(GraphEdgeKind kind, bool on) { _kindToggles[kind].IsChecked = on; }
        public void DragForProbe(string id, double x, double y) { NodeView view; if (_nodes.TryGetValue(id, out view)) Moved(view, x, y, true); }
        public string SelectedForProbe { get { return _selectedId; } }
        public int HygieneCountForProbe(string caption)
        {
            foreach (var child in _hygiene.Children)
            {
                var block = child as TextBlock;
                if (block != null && Equals(block.Tag, "graph-hygiene:" + caption))
                {
                    var text = block.Text ?? "";
                    var open = text.LastIndexOf('(');
                    int n;
                    if (open >= 0 && int.TryParse(text.Substring(open + 1).TrimEnd(')'), out n)) return n;
                }
            }
            return -1;
        }
        public PresenceMatrix MatrixForProbe { get { return _matrix; } }

        // ================================================================ un nœud

        /// <summary>Un nœud de la carte : une pastille colorée à l'icône de
        /// l'élément, un titre dessous ; se tire, se sélectionne, s'ouvre.</summary>
        internal sealed class NodeView : Border
        {
            public readonly GraphNode Node;
            public readonly TextBlock Label;
            private readonly GraphView _owner;
            private readonly double _radius;
            private Point _pressAt;
            private double _startX, _startY;
            private bool _dragging, _moved;

            public static double RadiusOf(GraphNode node)
            {
                var r = node.IsSheet ? 11 + Math.Min(11, Math.Sqrt(node.Degree) * 2.2) : node.Kind == ItemKind.Book ? 14 : 8 + Math.Min(6, Math.Sqrt(node.Degree) * 1.5);
                return Math.Round(r, 1);
            }

            public NodeView(GraphView owner, GraphNode node, BinderItem item, IBrush fill)
            {
                _owner = owner;
                Node = node;
                _radius = RadiusOf(node);
                Width = Height = _radius * 2;
                CornerRadius = new CornerRadius(_radius);
                Background = fill;
                BorderBrush = Chrome.PaperBg;
                BorderThickness = new Thickness(2);
                Cursor = new Cursor(StandardCursorType.Hand);
                Control icon = null;
                try { icon = item == null ? null : ItemIcons.Render(item, Math.Max(8, _radius * 0.95), Chrome.PaperBg); }
                catch (Exception) { icon = null; }
                if (icon != null) { icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center; icon.IsHitTestVisible = false; Child = icon; }
                Label = new TextBlock
                {
                    Text = node.Title.Length == 0 ? "Sans titre" : (node.Title.Length > 28 ? node.Title.Substring(0, 27) + "…" : node.Title),
                    Foreground = Chrome.Ink,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    Width = 140,
                    IsHitTestVisible = false
                };
                ToolTip.SetTip(this, node.Title + " — " + node.Degree + (node.Degree > 1 ? " liens" : " lien") + (node.IsSheet ? ", présente dans " + node.Presence + (node.Presence > 1 ? " écrits" : " écrit") : ""));
                PointerPressed += OnPressed;
                PointerMoved += OnMoved;
                PointerReleased += OnReleased;
            }

            public void Place(double x, double y)
            {
                Canvas.SetLeft(this, x - _radius);
                Canvas.SetTop(this, y - _radius);
                Canvas.SetLeft(Label, x - 70);
                Canvas.SetTop(Label, y + _radius + 2);
            }

            public void SetSelected(bool selected)
            {
                BorderBrush = selected ? (IBrush)Chrome.Accent : Chrome.PaperBg;
                BorderThickness = new Thickness(selected ? 3 : 2);
                ZIndex = selected ? 2 : 1;
            }

            private void OnPressed(object sender, PointerPressedEventArgs e)
            {
                var point = e.GetCurrentPoint(this);
                if (point.Properties.IsRightButtonPressed) { e.Handled = true; _owner.ShowMenu(this); return; }
                if (!point.Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (e.ClickCount >= 2) { _owner.Open(Node.Id, false); return; }
                _pressAt = e.GetPosition(_owner.Viewport);
                var at = _owner.PositionOf(Node.Id);
                if (at == null) return;
                _startX = at.X; _startY = at.Y;
                _dragging = true; _moved = false;
                e.Pointer.Capture(this);
            }

            private void OnMoved(object sender, PointerEventArgs e)
            {
                if (!_dragging || e.Pointer.Captured != this) return;
                var now = e.GetPosition(_owner.Viewport);
                var dx = (now.X - _pressAt.X) / _owner.Zoom;
                var dy = (now.Y - _pressAt.Y) / _owner.Zoom;
                if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
                _moved = true;
                _owner.Moved(this, _startX + dx, _startY + dy, false);
            }

            private void OnReleased(object sender, PointerReleasedEventArgs e)
            {
                if (!_dragging) return;
                _dragging = false;
                if (e.Pointer.Captured == this) e.Pointer.Capture(null);
                e.Handled = true;
                if (_moved)
                {
                    var at = _owner.PositionOf(Node.Id);
                    if (at != null) _owner.Moved(this, at.X, at.Y, true);
                }
                else _owner.Select(Node.Id);
            }
        }

        // ================================================================ les arêtes

        /// <summary>Toutes les arêtes affichées, d'un seul trait de Render —
        /// pas un contrôle par arête. La nature donne la couleur et le tiret,
        /// le poids d'une présence son épaisseur ; les natures des relations
        /// s'écrivent autour du nœud sélectionné, quand on est assez près.</summary>
        internal sealed class EdgeLayer : Control
        {
            private readonly GraphView _owner;

            public EdgeLayer(GraphView owner)
            {
                _owner = owner;
                IsHitTestVisible = false;
                ZIndex = 0;
            }

            public void Invalidate() { InvalidateVisual(); }

            public override void Render(DrawingContext context)
            {
                var graph = _owner.Graph;
                if (graph == null) return;
                var pens = new Dictionary<string, Pen>();
                var selected = _owner.SelectedId;
                var labels = _owner.Zoom >= 0.8;
                var typeface = Typeface.Default;
                foreach (var edge in graph.Edges)
                {
                    if (!_owner.EdgeShown(edge)) continue;
                    var a = _owner.PositionOf(edge.From);
                    var b = _owner.PositionOf(edge.To);
                    if (a == null || b == null) continue;
                    var lit = _owner.EdgeLit(edge);
                    var thickness = edge.Kind == GraphEdgeKind.Presence ? Math.Min(4, 1 + Math.Log(edge.Weight)) : edge.Kind == GraphEdgeKind.Relation ? 1.8 : 1.0;
                    var key = edge.Kind + "|" + thickness.ToString("0.0") + "|" + lit;
                    Pen pen;
                    if (!pens.TryGetValue(key, out pen))
                    {
                        var brush = EdgeBrush(edge.Kind) as SolidColorBrush;
                        var color = brush == null ? Chrome.Border.Color : brush.Color;
                        var faded = new SolidColorBrush(Color.FromArgb((byte)(lit ? 0xB4 : 0x28), color.R, color.G, color.B));
                        pen = new Pen(faded, thickness, edge.Kind == GraphEdgeKind.Wiki || edge.Kind == GraphEdgeKind.Tag ? new DashStyle(new double[] { 4, 3 }, 0) : null);
                        pens[key] = pen;
                    }
                    context.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                    if (labels && lit && selected != null && edge.Kind == GraphEdgeKind.Relation && edge.Label.Length > 0 && (edge.From == selected || edge.To == selected))
                    {
                        var text = new FormattedText(edge.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9, Chrome.AccentStrong);
                        context.DrawText(text, new Point((a.X + b.X) / 2 - text.Width / 2, (a.Y + b.Y) / 2 - text.Height - 1));
                    }
                }
            }
        }

        // ================================================================ la matrice

        /// <summary>La MATRICE DES PRÉSENCES : une ligne par fiche qui paraît
        /// (dans l'ordre de première apparition), une colonne par écrit du
        /// récit, une case d'autant plus pleine que la fiche y est souvent ;
        /// à droite de chaque ligne, le silence (« absente depuis N »). Un
        /// seul Render, la souris dit la case sous elle.</summary>
        public sealed class PresenceMatrix : Control
        {
            private const double Cell = 22, RowHead = 170, ColHead = 96, SinceWidth = 120, Top = 8, Left = 16;
            private readonly GraphView _owner;
            private readonly List<GraphNode> _rows = new List<GraphNode>();
            private readonly List<GraphNode> _cols = new List<GraphNode>();
            private readonly Dictionary<string, int> _since = new Dictionary<string, int>();
            private int _hoverRow = -1, _hoverCol = -1;
            private readonly TextBlock _hint;

            public PresenceMatrix(GraphView owner)
            {
                _owner = owner;
                _hint = new TextBlock();
                PointerMoved += delegate(object sender, PointerEventArgs e) { Hover(e.GetPosition(this)); };
                PointerExited += delegate { _hoverRow = _hoverCol = -1; ToolTip.SetTip(this, null); InvalidateVisual(); };
                PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    var at = e.GetPosition(this);
                    var row = RowAt(at.Y);
                    if (row < 0 || row >= _rows.Count) return;
                    var col = ColAt(at.X);
                    var id = col >= 0 && col < _cols.Count && at.X >= Left + RowHead ? _cols[col].Id : _rows[row].Id;
                    if (e.ClickCount >= 2) { _owner.Open(id, false); e.Handled = true; return; }
                    _owner.Select(id);
                    e.Handled = true;
                };
            }

            public int RowCountForProbe { get { return _rows.Count; } }
            public int ColumnCountForProbe { get { return _cols.Count; } }
            public int SinceForProbe(string sheetId) { int n; return _since.TryGetValue(sheetId, out n) ? n : -1; }

            public void Reload()
            {
                _rows.Clear(); _cols.Clear(); _since.Clear();
                var graph = _owner.Graph;
                if (graph == null) { InvalidateMeasure(); InvalidateVisual(); return; }
                _cols.AddRange(graph.Writings);
                var first = new Dictionary<string, int>();
                for (var c = 0; c < _cols.Count; c++)
                    foreach (var edge in graph.EdgesOf(_cols[c].Id))
                    {
                        if (edge.Kind != GraphEdgeKind.Presence) continue;
                        var sheet = edge.Other(_cols[c].Id);
                        if (!first.ContainsKey(sheet)) first[sheet] = c;
                    }
                foreach (var node in graph.Nodes)
                    if (node.IsSheet && first.ContainsKey(node.Id)) _rows.Add(node);
                _rows.Sort(delegate(GraphNode a, GraphNode b)
                {
                    var byFirst = first[a.Id].CompareTo(first[b.Id]);
                    return byFirst != 0 ? byFirst : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                });
                foreach (var absence in graph.Absences()) _since[absence.Sheet.Id] = absence.Since;
                InvalidateMeasure();
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(Left + RowHead + _cols.Count * Cell + SinceWidth + 24, Top + ColHead + _rows.Count * Cell + 24);
            }

            private int RowAt(double y) { return (int)Math.Floor((y - Top - ColHead) / Cell); }
            private int ColAt(double x) { return (int)Math.Floor((x - Left - RowHead) / Cell); }

            private void Hover(Point at)
            {
                var row = RowAt(at.Y);
                var col = ColAt(at.X);
                if (row < 0 || row >= _rows.Count) row = -1;
                if (col < 0 || col >= _cols.Count || at.X < Left + RowHead) col = -1;
                if (row == _hoverRow && col == _hoverCol) return;
                _hoverRow = row; _hoverCol = col;
                var graph = _owner.Graph;
                if (row >= 0 && col >= 0 && graph != null)
                {
                    var count = graph.PresenceCount(_cols[col].Id, _rows[row].Id);
                    ToolTip.SetTip(this, _rows[row].Title + " — " + _cols[col].Title + " : " + (count == 0 ? "absente" : count + (count > 1 ? " occurrences" : " occurrence")));
                }
                else if (row >= 0)
                {
                    int since;
                    ToolTip.SetTip(this, _rows[row].Title + (_since.TryGetValue(_rows[row].Id, out since) ? (since == 0 ? " — présente dans le dernier écrit" : " — absente depuis " + since + (since > 1 ? " écrits" : " écrit")) : ""));
                }
                else if (col >= 0) ToolTip.SetTip(this, _cols[col].Title);
                else ToolTip.SetTip(this, null);
                InvalidateVisual();
            }

            public override void Render(DrawingContext context)
            {
                base.Render(context);
                var graph = _owner.Graph;
                if (graph == null) return;
                var typeface = Typeface.Default;
                if (_rows.Count == 0 || _cols.Count == 0)
                {
                    var empty = new FormattedText(_cols.Count == 0 ? "Aucun écrit dans le récit." : "Aucune fiche ne paraît dans les écrits (vérifiez les noms des fiches : titre, Nom, Prénom, Alias).", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 13, Chrome.SoftText);
                    context.DrawText(empty, new Point(Left + 8, Top + 8));
                    return;
                }
                var accent = Chrome.Accent.Color;
                // Les en-têtes de colonnes, inclinés.
                for (var c = 0; c < _cols.Count; c++)
                {
                    var x = Left + RowHead + c * Cell + Cell / 2;
                    var text = new FormattedText(Shorten(_cols[c].Title, 22), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, c == _hoverCol ? Chrome.Ink : Chrome.SoftText);
                    var m = Matrix.CreateRotation(-Math.PI / 3) * Matrix.CreateTranslation(x - 4, Top + ColHead - 6);
                    using (context.PushTransform(m))
                        context.DrawText(text, new Point(0, -text.Height / 2));
                }
                for (var r = 0; r < _rows.Count; r++)
                {
                    var y = Top + ColHead + r * Cell;
                    if (r == _hoverRow) context.FillRectangle(Chrome.AccentTint, new Rect(Left, y, RowHead + _cols.Count * Cell + SinceWidth, Cell));
                    int since;
                    _since.TryGetValue(_rows[r].Id, out since);
                    var muted = since >= 2;
                    var head = new FormattedText(Shorten(_rows[r].Title, 26), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, muted ? Chrome.Warn : Chrome.Ink);
                    context.DrawText(head, new Point(Left, y + (Cell - head.Height) / 2));
                    for (var c = 0; c < _cols.Count; c++)
                    {
                        var x = Left + RowHead + c * Cell;
                        var count = graph.PresenceCount(_cols[c].Id, _rows[r].Id);
                        var cell = new Rect(x + 2, y + 2, Cell - 4, Cell - 4);
                        if (count == 0)
                            context.DrawRectangle(null, new Pen(Chrome.Border, 1), cell, 3, 3);
                        else
                        {
                            var alpha = (byte)Math.Min(255, 70 + Math.Min(185, Math.Log(count + 1) * 60));
                            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)), null, cell, 3, 3);
                        }
                    }
                    var note = since == 0 ? "jusqu'au dernier" : "absente depuis " + since;
                    var tail = new FormattedText(note, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, muted ? Chrome.Warn : Chrome.FaintText);
                    context.DrawText(tail, new Point(Left + RowHead + _cols.Count * Cell + 8, y + (Cell - tail.Height) / 2));
                }
            }

            private static string Shorten(string text, int max)
            {
                if (string.IsNullOrEmpty(text)) return "Sans titre";
                return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
            }
        }
    }
}
