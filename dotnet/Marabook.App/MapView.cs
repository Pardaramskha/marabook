using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.History;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'ÉCRAN D'UNE CARTE (2.0.0, 16/11/2026, Rémi : « LE gros morceau »).
    /// En tête : le rendu (planisphère ou globe), les outils (déplacer, zone, repère,
    /// échelle, mesurer), le zoom. Au centre : la toile (MapCanvas). À droite : ce que
    /// la carte sait d'elle-même — son poids dans le .plot, son monde, son échelle et
    /// les superficies qui en découlent, le jour et la nuit, ses zones et ses repères.
    ///
    /// Une carte PEUT être liée à un monde de l'Astrolabe ; sans monde, tout marche
    /// sauf ce qui demande un ciel (jour et nuit, saisons, part de la surface du monde).
    ///
    /// LOT 12 (16/11, Rémi) : le volet de droite a quitté l'écran de la carte pour le
    /// RAIL. La vue bâtit trois panneaux que la fenêtre loge : GeneralPanel (image,
    /// monde, échelle et superficies) dans l'onglet Général ; SkyPanel (jour, nuit et
    /// saisons) et ShapesPanel (zones et repères) dans deux onglets propres à la carte.</summary>
    public class MapView : DockPanel
    {
        private readonly TextBlock _title;
        private readonly MapCanvas _canvas;
        private readonly StackPanel _general, _sky, _shapes, _nav;
        private readonly TextBlock _hint, _zoomLabel, _viewLabel;
        private readonly Border _viewRule;
        private readonly ToggleButton _flat, _globe;
        private readonly Dictionary<MapCanvas.Tool, ToggleButton> _tools = new Dictionary<MapCanvas.Tool, ToggleButton>();
        private readonly TextBlock _empty;
        private BinderItem _item;
        private Project _project;
        private HistoryManager _history;
        private string _selectedZone, _selectedMarker;
        private string _measure;

        // Le jour et la nuit, les saisons, les filtres : l'état d'une session (rien de cela ne s'enregistre).
        private bool _night;
        private bool _seasonHeat;                              // le voile des saisons : éteint d'office (lot 13)
        private bool _showGuides;                              // les repères de navigation : équateur, tropiques… (lot 13)
        private bool _latLon;                                  // la latitude et la longitude du curseur (lot 13)
        private TextBlock _geoText;
        private Avalonia.Threading.DispatcherTimer _stateTimer;
        /// <summary>Le ciel du globe s'anime (faux en sonde : rien ne doit bouger tout seul).</summary>
        public bool AnimateSky = true;
        public event Action<World> GuidesRequested;            // « Définir les marqueurs » : ceux du monde lié
        private bool _showZones = true, _showMarkers = true;   // les filtres (lot 12)
        private double _dayOfYear = 1, _dayFraction = 0.5;

        public event Action Edited;
        public event Action BackRequested;
        public event Action RenameRequested;
        public event Action ReplaceImageRequested;
        public event Action<BinderItem> NavigateRequested;
        public event Action<World, double> WorldSizeRequested;   // donner au monde le tour que la carte implique
        public event Action SelectionChanged;                    // une zone ou un repère vient d'être choisi sur la carte

        public MapView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;

            // ================================================== en-tête
            var head = new Border { Background = Chrome.BarBgLight, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8, 16, 8) };
            SetDock(head, Dock.Top);
            var row = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
            _zoomLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Tag = "map-zoom" };
            right.Children.Add(_zoomLabel);
            var zoomOut = Buttons.Text("−", "Dézoomer", Buttons.Compact, Buttons.Look.Outline);
            zoomOut.Click += delegate { _canvas.ZoomBy(1 / 1.5, new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2)); };
            var zoomIn = Buttons.Text("+", "Zoomer", Buttons.Compact, Buttons.Look.Outline);
            zoomIn.Click += delegate { _canvas.ZoomBy(1.5, new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2)); };
            var fit = Buttons.Text("Tout voir", "Ramener toute la carte dans la page", Buttons.Compact, Buttons.Look.Outline);
            fit.Click += delegate { _canvas.FitAll(); };
            right.Children.Add(zoomOut);
            right.Children.Add(zoomIn);
            right.Children.Add(fit);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var back = new Button
            {
                Content = Icons.Make("arrow-left-bold", 12, Chrome.Ink), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 12, 0),
                [ToolTip.TipProperty] = "Revenir aux Cartes", VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            left.Children.Add(back);
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis };
            left.Children.Add(_title);
            var rename = Buttons.Icon("pencil-simple-line", "Renommer la carte", Buttons.Compact, Buttons.Look.Calm);
            rename.Margin = new Thickness(6, 0, 0, 0);
            rename.VerticalAlignment = VerticalAlignment.Center;
            rename.Click += delegate { var h = RenameRequested; if (h != null) h(); };
            left.Children.Add(rename);
            row.Children.Add(left);
            head.Child = row;
            Children.Add(head);

            // ================================================== la barre du rendu et des outils
            var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _viewLabel = new TextBlock { Text = "Rendu :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            bar.Children.Add(_viewLabel);
            _flat = Buttons.TextToggle("Planisphère", "L'image à plat : c'est ici qu'on trace les zones, les repères, l'échelle", Buttons.Compact);
            _flat.Tag = "map-view-flat";
            _flat.Click += delegate { SetViewMode(Maps.ViewFlat); };
            _globe = Buttons.TextToggle("Globe", "L'image enroulée sur une planète, qu'on fait tourner à la souris", Buttons.Compact);
            _globe.Tag = "map-view-globe";
            _globe.Margin = new Thickness(4, 0, 0, 0);
            _globe.Click += delegate { SetViewMode(Maps.ViewGlobe); };
            bar.Children.Add(_flat);
            bar.Children.Add(_globe);
            _viewRule = new Border { Width = 1, Background = Chrome.Border, Margin = new Thickness(12, 2, 12, 2) };
            bar.Children.Add(_viewRule);
            bar.Children.Add(new TextBlock { Text = "Outil :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            AddTool(bar, MapCanvas.Tool.Pan, "Déplacer", "Promener la carte, choisir une zone ou un repère, tirer ses poignées");
            AddTool(bar, MapCanvas.Tool.Zone, "Zone", "Tracer une zone : un clic par sommet, double-clic (ou Entrée) pour fermer");
            AddTool(bar, MapCanvas.Tool.Marker, "Repère", "Poser un repère : une ville, un lieu");
            AddTool(bar, MapCanvas.Tool.Scale, "Échelle", "Poser l'échelle : deux points, puis la distance ou le temps de trajet qui les sépare");
            AddTool(bar, MapCanvas.Tool.Measure, "Mesurer", "Mesurer une distance entre deux points (il faut une échelle)");
            _hint = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "map-hint" };
            var barRow = new DockPanel();
            DockPanel.SetDock(bar, Dock.Left);
            barRow.Children.Add(bar);
            barRow.Children.Add(_hint);
            var barBox = new Border { Padding = new Thickness(16, 6, 16, 6), BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), Background = Chrome.BarBg, Child = barRow };
            SetDock(barBox, Dock.Top);
            Children.Add(barBox);

            // ================================================== les panneaux du rail (lot 12)
            _general = new StackPanel { Tag = "map-general" };
            _sky = new StackPanel { Tag = "map-sky" };
            _shapes = new StackPanel { Tag = "map-shapes" };
            _nav = new StackPanel { Tag = "map-navigation" };


            // ================================================== la toile
            _canvas = new MapCanvas(this);
            _empty = new TextBlock
            {
                Text = "Cette carte n'a pas d'image.\n« Choisir l'image… », dans l'onglet Général du rail, lui en donne une.",
                Foreground = Chrome.SoftText, FontSize = 13, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, IsVisible = false
            };
            var center = new Grid();
            center.Children.Add(_canvas);
            center.Children.Add(_empty);
            Children.Add(center);

            // ESPACE = la main (lot 12) : écouté à la fenêtre, pour valoir même quand le clavier est au rail.
            AttachedToVisualTree += delegate
            {
                _top = TopLevel.GetTopLevel(this);
                if (_top == null) return;
                _top.AddHandler(KeyDownEvent, OnTopKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                _top.AddHandler(KeyUpEvent, OnTopKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            };
            DetachedFromVisualTree += delegate
            {
                if (_top == null) return;
                _top.RemoveHandler(KeyDownEvent, OnTopKeyDown);
                _top.RemoveHandler(KeyUpEvent, OnTopKeyUp);
                _top = null;
            };
        }

        private TopLevel _top;

        private bool TypingSomewhere()
        {
            var focused = _top == null || _top.FocusManager == null ? null : _top.FocusManager.GetFocusedElement();
            return focused is TextBox;
        }

        private void OnTopKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || !IsVisible || _item == null || !_canvas.IsPointerOver || TypingSomewhere()) return;
            _canvas.SetSpace(true);
            e.Handled = true; // pas de bouton « cliqué » par la barre d'espace pendant qu'on promène la carte
        }

        private void OnTopKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || !_canvas.SpaceHeld) return;
            _canvas.SetSpace(false);
            e.Handled = true;
        }

        /// <summary>Les trois panneaux que la fenêtre loge dans le rail.</summary>
        public Control GeneralPanel { get { return _general; } }
        public Control SkyPanel { get { return _sky; } }
        public Control ShapesPanel { get { return _shapes; } }
        public Control NavigationPanel { get { return _nav; } }

        // ---- Les repères de navigation et la mesure lat/lon (lot 13).
        internal List<WorldGuide> Guides { get { return Astro.GuidesOf(World); } }
        internal bool GuidesShown { get { var map = Map; return _showGuides && map != null && map.HasImage && map.WholeWorld; } }
        internal bool LatLonShown { get { var map = Map; return _latLon && map != null && map.HasImage && map.WholeWorld; } }

        /// <summary>La toile dit où pointe le curseur : le panneau le répète.</summary>
        internal void CursorGeo(bool known, double latitude, double longitude)
        {
            if (_geoText == null) return;
            _geoText.Text = known ? Maps.GeoLabel(latitude, longitude) : "Survolez la carte : la latitude et la longitude du curseur s'affichent ici, et à côté de lui.";
            _geoText.Foreground = known ? Chrome.Ink : Chrome.SoftText;
            _geoText.FontWeight = known ? FontWeight.SemiBold : FontWeight.Normal;
        }

        /// <summary>Les astres du monde lié, là où ils sont au jour et à l'heure choisis (pour le ciel du globe) :
        /// le premier soleil à l'aplomb du point où il est midi ; un compagnon qui orbite, selon sa phase ;
        /// une lune à l'opposé du soleil quand elle est pleine, auprès de lui quand elle est nouvelle.</summary>
        internal List<MapCanvas.SkyDot> SkyBodies()
        {
            var list = new List<MapCanvas.SkyDot>();
            var world = World;
            if (world == null) return list;
            var noon = Maps.NoonLongitude(_dayFraction);
            var declination = Declination;
            var day = _dayOfYear + _dayFraction;
            Func<string, Color, Color> tint = delegate(string hex, Color fallback) { Color parsed; return Color.TryParse(hex, out parsed) ? parsed : fallback; };
            var gold = Color.FromRgb(0xF5, 0xC5, 0x42);
            if (world.Suns.Count == 0) list.Add(new MapCanvas.SkyDot { Longitude = noon, Latitude = declination, Color = gold, Size = 11, Sun = true });
            for (var i = 0; i < world.Suns.Count; i++)
            {
                var sun = world.Suns[i];
                var size = 11 * Math.Max(0.6, Math.Min(2, sun.Size <= 0 ? 1 : Math.Sqrt(sun.Size)));
                var dot = new MapCanvas.SkyDot { Color = tint(Astro.ColorOf(sun, i), gold), Size = size, Sun = true, Name = sun.Name };
                if (i == 0) { dot.Longitude = noon; dot.Latitude = declination; }
                else if (sun.Period > 0) { dot.Longitude = noon + Math.PI + 2 * Math.PI * Astro.SunPhase(sun, day); dot.Latitude = -declination * Math.Cos(2 * Math.PI * Astro.SunPhase(sun, day)); }
                else { dot.Longitude = noon + 0.17 * i; dot.Latitude = declination + 0.05 * i; }
                list.Add(dot);
            }
            for (var i = 0; i < world.Moons.Count; i++)
            {
                var moon = world.Moons[i];
                var phase = Astro.Phase(moon, day);
                list.Add(new MapCanvas.SkyDot
                {
                    Longitude = noon + Math.PI + 2 * Math.PI * phase, Latitude = Math.Max(-1.4, Math.Min(1.4, -declination * Math.Cos(2 * Math.PI * phase) + 0.07 * i)),
                    Color = tint(Astro.ColorOf(moon, i), Color.FromRgb(0xD8, 0xDC, 0xE6)), Size = 6.5, Lit = Astro.Illumination(phase), Name = moon.Name
                });
            }
            return list;
        }

        // ---- La mémoire de la carte (lot 13, Rémi : « que le logiciel se souvienne des outils et réglages
        // de la carte entre les visites ») : l'outil, les filtres, le ciel, la vue — par carte, dans les
        // réglages de l'application (ce n'est pas le contenu du projet : rien ne devient « modifié »).

        private Dictionary<string, object> State()
        {
            var state = new Dictionary<string, object>();
            state["tool"] = _canvas.CurrentTool.ToString();
            state["night"] = _night;
            state["seasons"] = _seasonHeat;
            state["day"] = _dayOfYear;
            state["hour"] = _dayFraction;
            state["zones"] = _showZones;
            state["markers"] = _showMarkers;
            state["guides"] = _showGuides;
            state["latLon"] = _latLon;
            var view = new List<object>();
            foreach (var value in _canvas.ViewState) view.Add(value);
            state["view"] = view;
            return state;
        }

        /// <summary>Retient l'état de la carte ouverte, tout de suite (on la quitte) ou dans un instant (un réglage vient de bouger).</summary>
        internal void RememberState(bool now = true)
        {
            if (_item == null) return;
            if (!now)
            {
                if (_stateTimer == null)
                {
                    _stateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    _stateTimer.Tick += delegate { _stateTimer.Stop(); RememberState(); };
                }
                _stateTimer.Stop();
                _stateTimer.Start();
                return;
            }
            if (_stateTimer != null) _stateTimer.Stop();
            Marabook.Settings.AppSettings.RememberMapState(_item.Id, State());
            Marabook.Settings.AppSettings.Save();
        }

        private void RestoreState()
        {
            Dictionary<string, object> state;
            if (_item == null || !Marabook.Settings.AppSettings.MapStates.TryGetValue(_item.Id, out state) || state == null) return;
            Func<string, bool, bool> flag = delegate(string key, bool fallback) { object value; return state.TryGetValue(key, out value) && value is bool ? (bool)value : fallback; };
            Func<string, double, double> number = delegate(string key, double fallback)
            {
                object value;
                if (!state.TryGetValue(key, out value) || value == null) return fallback;
                try { var read = Convert.ToDouble(value, CultureInfo.InvariantCulture); return double.IsNaN(read) || double.IsInfinity(read) ? fallback : read; }
                catch (Exception) { return fallback; }
            };
            _night = flag("night", false);
            _seasonHeat = flag("seasons", false);
            _showZones = flag("zones", true);
            _showMarkers = flag("markers", true);
            _showGuides = flag("guides", false);
            _latLon = flag("latLon", false);
            _dayOfYear = Math.Max(1, number("day", 1));
            _dayFraction = Math.Max(0, Math.Min(1, number("hour", 0.5)));
            object raw;
            var list = state.TryGetValue("view", out raw) ? raw as List<object> : null;
            if (list != null && list.Count >= 6)
            {
                var view = new double[6];
                var sound = true;
                for (var i = 0; i < 6; i++)
                {
                    try { view[i] = Convert.ToDouble(list[i], CultureInfo.InvariantCulture); }
                    catch (Exception) { sound = false; }
                }
                if (sound) _canvas.RestoreView(view);
            }
            MapCanvas.Tool tool;
            object name;
            var map = Map;
            if (state.TryGetValue("tool", out name) && name is string && Enum.TryParse((string)name, out tool) && map != null && map.HasImage && !GlobeShown
                && (tool != MapCanvas.Tool.Measure || Maps.CanMeasure(map, World))) _canvas.SetTool(tool);
        }

        internal bool ShowZones { get { return _showZones; } }
        internal bool ShowMarkers { get { return _showMarkers; } }
        internal double DayOfYear { get { return _dayOfYear; } }

        /// <summary>Le globe ne vaut que pour la carte d'un monde entier.</summary>
        internal bool GlobeShown { get { var map = Map; return map != null && map.WholeWorld && map.View == Maps.ViewGlobe; } }

        private void AddTool(Panel bar, MapCanvas.Tool tool, string label, string tip)
        {
            var button = Buttons.TextToggle(label, tip, Buttons.Compact);
            button.Tag = "map-tool-" + tool.ToString().ToLowerInvariant();
            button.Margin = new Thickness(_tools.Count == 0 ? 0 : 4, 0, 0, 0);
            button.Click += delegate { ChooseTool(tool); };
            _tools[tool] = button;
            bar.Children.Add(button);
        }

        // ================================================== cycle de vie

        public bool HasItem { get { return _item != null; } }
        public bool ShowsItem(BinderItem item) { return _item == item; }
        public BinderItem Item { get { return _item; } }
        internal MapInfo Map { get { return _item == null ? null : _item.Map; } }
        internal World World { get { return Maps.WorldOf(_project, Map); } }
        internal MapZone SelectedZone { get { var map = Map; return map == null || _selectedZone == null ? null : map.FindZone(_selectedZone); } }
        internal MapMarker SelectedMarker { get { var map = Map; return map == null || _selectedMarker == null ? null : map.FindMarker(_selectedMarker); } }

        public void Load(BinderItem item, Project project, HistoryManager history = null)
        {
            var same = _item == item;
            var previous = same ? null : _item;
            _item = item;
            _project = project;
            _history = history;
            if (item.Map == null) item.Map = new MapInfo();
            if (!same)
            {
                // On quitte une carte pour une autre : la première garde ses réglages.
                if (previous != null) { _item = previous; RememberState(); _item = item; }
                _selectedZone = _selectedMarker = null;
                _measure = null;
                _night = false;
                _seasonHeat = false;
                _showZones = _showMarkers = true;
                _showGuides = _latLon = false;
                _dayOfYear = 1;
                _dayFraction = 0.5;
                _canvas.Attach();
                _canvas.ResetView();
                RestoreState();
            }
            Refresh();
        }

        public void Clear()
        {
            RememberState();
            _item = null;
            _canvas.Detach();
        }

        /// <summary>Le modèle a changé (une zone, l'échelle, le monde, une annulation, le titre) : tout se relit.</summary>
        public void Refresh()
        {
            if (_item == null) return;
            var map = Map;
            _canvas.Attach();
            _title.Text = _item.Title;
            _empty.IsVisible = !map.HasImage;
            if (SelectedZone == null) _selectedZone = null;
            if (SelectedMarker == null) _selectedMarker = null;
            if (!NightPossible) _night = false;
            var globe = GlobeShown;
            // Le choix du rendu n'existe que pour la carte d'un monde entier (lot 12) : sinon, pas de bouton Globe.
            _viewLabel.IsVisible = _flat.IsVisible = _globe.IsVisible = _viewRule.IsVisible = map.WholeWorld && map.HasImage;
            _flat.IsChecked = !globe;
            _globe.IsChecked = globe;
            // Le tracé se fait à plat : au globe, les outils se grisent.
            foreach (var pair in _tools)
            {
                pair.Value.IsEnabled = map.HasImage && (pair.Key == MapCanvas.Tool.Pan || !globe);
                pair.Value.IsChecked = pair.Key == _canvas.CurrentTool;
            }
            if (globe && _canvas.CurrentTool != MapCanvas.Tool.Pan) _canvas.SetTool(MapCanvas.Tool.Pan);
            DescribeTool();
            ViewChanged();
            RefreshSide();
            _canvas.InvalidateVisual();
        }

        internal void ViewChanged()
        {
            var map = Map;
            if (map == null || !map.HasImage) { _zoomLabel.Text = ""; return; }
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            _zoomLabel.Text = GlobeShown ? "Zoom × " + _canvas.ZoomRatio.ToString("0.#", fr)
                : "Zoom × " + _canvas.ZoomRatio.ToString("0.#", fr) + "  ·  niveau " + (Maps.LevelFor(map, _canvas.Scale) + 1) + " sur " + map.Levels;
        }

        public string StatusLabel
        {
            get
            {
                var map = Map;
                if (map == null || !map.HasImage) return "Carte sans image";
                return map.Width + " × " + map.Height + " px · " + TimeSystem.Count(map.Levels, "niveau de zoom", "niveaux de zoom") + " · " + Maps.SizeLabel(Maps.Bytes(map));
            }
        }

        // ================================================== le rendu, les outils

        private void SetViewMode(string view)
        {
            var map = Map;
            if (map == null) return;
            if (map.View == view) { Refresh(); return; }
            var before = map.Clone();
            map.View = view;
            Commit(before);
        }

        internal void ChooseTool(MapCanvas.Tool tool)
        {
            var map = Map;
            if (map == null || !map.HasImage) return;
            if (tool == MapCanvas.Tool.Measure && !Maps.CanMeasure(map, World))
            {
                _measure = null;
                _canvas.SetTool(MapCanvas.Tool.Pan);
                foreach (var pair in _tools) pair.Value.IsChecked = pair.Key == MapCanvas.Tool.Pan;
                _hint.Foreground = Chrome.Danger;
                _hint.Text = map.WholeWorld ? "Pour mesurer : posez une échelle, ou liez un monde dont la taille est connue."
                    : "Pour mesurer, posez d'abord une échelle (outil « Échelle »).";
                return;
            }
            _measure = null;
            _canvas.SetTool(tool);
            foreach (var pair in _tools) pair.Value.IsChecked = pair.Key == tool;
            DescribeTool();
            RefreshSide();
            RememberState(false);
        }

        private void DescribeTool()
        {
            _hint.Foreground = Chrome.SoftText;
            var map = Map;
            if (map == null || !map.HasImage) { _hint.Text = ""; return; }
            if (GlobeShown) { _hint.Text = "Glisser fait tourner le globe ; la molette l'approche. Le tracé se fait au planisphère."; return; }
            switch (_canvas.CurrentTool)
            {
                case MapCanvas.Tool.Zone: _hint.Text = "Un clic par sommet ; double-clic, Entrée ou un clic sur le premier point ferme la zone. Retour arrière retire le dernier point, Échap abandonne."; break;
                case MapCanvas.Tool.Marker: _hint.Text = "Cliquez à l'endroit du repère. Échap abandonne."; break;
                case MapCanvas.Tool.Scale: _hint.Text = "Cliquez deux points dont vous connaissez l'écart — en kilomètres ou en jours de voyage."; break;
                case MapCanvas.Tool.Measure: _hint.Text = _measure ?? "Cliquez deux points : la distance s'affiche."; break;
                default: _hint.Text = "Glisser promène la carte, la molette zoome. Un clic choisit une zone ou un repère ; un double-clic ouvre sa fiche. Espace tenu : la main, quel que soit l'outil."; break;
            }
        }

        internal void Commit(MapInfo before)
        {
            if (_item == null) return;
            if (_history != null && before != null) _history.Push(new MapEditAction(_item, before, _item.Map.Clone()));
            Refresh();
            var handler = Edited;
            if (handler != null) handler();
        }

        internal void Select(MapZone zone, MapMarker marker)
        {
            var zoneId = zone == null ? null : zone.Id;
            var markerId = marker == null ? null : marker.Id;
            if (zoneId == _selectedZone && markerId == _selectedMarker) return;
            _selectedZone = zoneId;
            _selectedMarker = markerId;
            var chosen = SelectionChanged;
            if (chosen != null && (zoneId != null || markerId != null)) chosen();
            RefreshSide();
            _canvas.InvalidateVisual();
        }

        internal async void FinishZone(List<Pos> points)
        {
            var map = Map;
            if (map == null || points.Count < 3) return;
            var name = "Zone " + (map.Zones.Count + 1);
            var draft = new MapShapeChoice { Name = name, Color = MapShapeDialog.Palette[map.Zones.Count % MapShapeDialog.Palette.Length] };
            var choice = await MapShapeDialog.Ask(Ui.OwnerOf(this), _project, "Nouvelle zone", draft, name);
            if (choice == null) { ChooseTool(MapCanvas.Tool.Zone); return; }
            AddZone(points, choice);
        }

        internal MapZone AddZone(List<Pos> points, MapShapeChoice choice)
        {
            var map = Map;
            var before = map.Clone();
            var zone = new MapZone { Name = choice.Name, Color = choice.Color, LinkId = choice.LinkId, Level = choice.Level };
            zone.Points.AddRange(points);
            map.Zones.Add(zone);
            _showZones = true; // ce qu'on vient de tracer se voit
            _selectedZone = zone.Id;
            _selectedMarker = null;
            _canvas.SetTool(MapCanvas.Tool.Pan);
            Commit(before);
            return Map.FindZone(zone.Id);
        }

        internal async void FinishMarker(Pos at)
        {
            var map = Map;
            if (map == null) return;
            var name = "Repère " + (map.Markers.Count + 1);
            var choice = await MapShapeDialog.Ask(Ui.OwnerOf(this), _project, "Nouveau repère", new MapShapeChoice { Name = name, Color = "#C0504D" }, name);
            if (choice == null) return;
            AddMarker(at, choice);
        }

        internal MapMarker AddMarker(Pos at, MapShapeChoice choice)
        {
            var map = Map;
            var before = map.Clone();
            var marker = new MapMarker { Name = choice.Name, Color = choice.Color, LinkId = choice.LinkId, Level = choice.Level, X = at.X, Y = at.Y };
            map.Markers.Add(marker);
            _showMarkers = true;
            _selectedMarker = marker.Id;
            _selectedZone = null;
            _canvas.SetTool(MapCanvas.Tool.Pan);
            Commit(before);
            return Map.FindMarker(marker.Id);
        }

        internal async void FinishScale(Pos a, Pos b)
        {
            var map = Map;
            if (map == null) return;
            var between = map.WholeWorld
                ? "Les deux points sont séparés d'un " + (Maps.CentralAngle(map, a, b) / (2 * Math.PI)).ToString("0.0 %", CultureInfo.GetCultureInfo("fr-FR")) + " de tour du monde (au plus court, sur la sphère). Quelle distance cela fait-il ?"
                : "Les deux points sont à " + Math.Round(Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y))).ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " pixels l'un de l'autre sur l'image. Quelle distance cela fait-il dans votre monde ?";
            var choice = await MapScaleDialog.Ask(Ui.OwnerOf(this), between, 0);
            if (choice == null) { ChooseTool(MapCanvas.Tool.Pan); return; }
            SetScale(a, b, choice.Km, choice.Note);
        }

        internal void SetScale(Pos a, Pos b, double km, string note)
        {
            var map = Map;
            var before = map.Clone();
            map.ScaleA = a;
            map.ScaleB = b;
            map.ScaleKm = km;
            map.ScaleNote = note;
            _canvas.SetTool(MapCanvas.Tool.Pan);
            Commit(before);
        }

        internal void Measured(Pos a, Pos b)
        {
            var km = Maps.Distance(Map, World, a, b);
            _measure = "Distance : " + Maps.KmLabel(km) + "  ·  " + Maps.TravelLabel(km);
            DescribeTool();
        }

        /// <summary>Double-clic : la fiche liée s'ouvre ; sans fiche, la fenêtre de la zone ou du repère.</summary>
        internal void OpenOrEdit(MapZone zone, MapMarker marker)
        {
            var linkId = zone != null ? zone.LinkId : marker != null ? marker.LinkId : null;
            var linked = linkId == null || _project == null ? null : _project.FindById(linkId);
            if (linked != null && linked.RootCategory().CategoryKey != Project.KeyTrash)
            {
                var handler = NavigateRequested;
                if (handler != null) handler(linked);
                return;
            }
            EditSelection();
        }

        private async void EditSelection()
        {
            var map = Map;
            var zone = SelectedZone;
            var marker = SelectedMarker;
            if (map == null || (zone == null && marker == null)) return;
            var current = zone != null
                ? new MapShapeChoice { Name = zone.Name, Color = zone.Color, LinkId = zone.LinkId, Level = zone.Level }
                : new MapShapeChoice { Name = marker.Name, Color = marker.Color, LinkId = marker.LinkId, Level = marker.Level };
            var choice = await MapShapeDialog.Ask(Ui.OwnerOf(this), _project, zone != null ? "Zone" : "Repère", current, current.Name);
            if (choice == null) return;
            // La fenêtre a rendu la main : la carte a pu être remplacée par une annulation — on reprend par l'id.
            map = Map;
            var before = map.Clone();
            zone = SelectedZone;
            marker = SelectedMarker;
            if (zone != null) { zone.Name = choice.Name; zone.Color = choice.Color; zone.LinkId = choice.LinkId; zone.Level = choice.Level; }
            else if (marker != null) { marker.Name = choice.Name; marker.Color = choice.Color; marker.LinkId = choice.LinkId; marker.Level = choice.Level; }
            else return;
            Commit(before);
        }

        internal void DeleteSelection()
        {
            var map = Map;
            if (map == null) return;
            var zone = SelectedZone;
            var marker = SelectedMarker;
            if (zone == null && marker == null) return;
            var before = map.Clone();
            if (zone != null) map.Zones.Remove(zone);
            if (marker != null) map.Markers.Remove(marker);
            _selectedZone = _selectedMarker = null;
            Commit(before);
        }

        // ================================================== le jour et la nuit

        /// <summary>Le jour et la nuit demandent un monde, et que l'image soit son planisphère entier.</summary>
        internal bool NightPossible { get { var map = Map; return map != null && map.HasImage && map.WholeWorld && World != null; } }
        internal bool NightShown { get { return _night && NightPossible; } }
        /// <summary>Les points de chaleur des saisons : même condition que la nuit, et un monde qui a des saisons.</summary>
        internal bool SeasonHeatPossible { get { var world = World; return NightPossible && world.Seasons.Count > 0; } }
        internal bool SeasonHeatShown { get { return _seasonHeat && SeasonHeatPossible; } }
        internal double DayFraction { get { return _dayFraction; } }

        private TimeSystem WorldSystem
        {
            get
            {
                var world = World;
                if (world == null || _project == null) return null;
                foreach (var system in _project.TimeSystems) if (system.Id == world.ReferenceSystemId) return system;
                var living = Astro.SystemsOf(_project, world);
                return living.Count > 0 ? living[0] : null;
            }
        }

        internal double YearDays
        {
            get
            {
                var world = World;
                var system = WorldSystem;
                if (world != null && system != null) return Math.Max(1, Astro.SolarYear(world, system));
                return world != null && world.SolarYear > 0 ? world.SolarYear : 365;
            }
        }

        internal double Declination { get { return Maps.Declination(World, _dayOfYear, YearDays); } }

        // ================================================== le panneau

        private static TextBlock Caption(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 5) };
        }

        private static TextBlock Line(string text, bool soft = true)
        {
            return new TextBlock { Text = text, Foreground = soft ? Chrome.SoftText : Chrome.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        }

        private static DockPanel Pair(string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            var left = new TextBlock { Text = label, Foreground = Chrome.SoftText, FontSize = 12 };
            DockPanel.SetDock(left, Dock.Left);
            row.Children.Add(left);
            row.Children.Add(new TextBlock { Text = value, Foreground = Chrome.Ink, FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right });
            return row;
        }

        private void RefreshSide()
        {
            _general.Children.Clear();
            _sky.Children.Clear();
            _shapes.Children.Clear();
            _nav.Children.Clear();
            var map = Map;
            if (map == null) return;
            var world = World;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            FillGeneral(map, world, fr);
            if (!map.HasImage)
            {
                _sky.Children.Add(Line("Cette carte n'a pas encore d'image."));
                _shapes.Children.Add(Line("Cette carte n'a pas encore d'image."));
                return;
            }
            FillSky(map, world, fr);
            FillShapes(map, world, fr);
            FillNavigation(map, world, fr);
        }

        // ---- Repères de navigation (onglet du rail, lot 13).
        private void FillNavigation(MapInfo map, World world, CultureInfo fr)
        {
            var side = _nav;
            _geoText = null;
            if (!map.WholeWorld)
            {
                side.Children.Add(Line("Les repères de navigation — équateur, tropiques, latitude et longitude — demandent la carte du monde entier : cochez « Carte du monde entier » dans l'onglet Général si cette image en est une."));
                return;
            }
            var show = new CheckBox { Content = "Afficher les repères de navigation", IsChecked = _showGuides, Tag = "map-guides-show" };
            ToolTip.SetTip(show, "Tracer sur la carte l'équateur, les tropiques, les cercles polaires — ou les marqueurs que vous avez définis pour ce monde.");
            show.IsCheckedChanged += delegate { SetGuides(show.IsChecked == true); };
            side.Children.Add(show);

            // Les deux grands boutons : définir les marqueurs (ceux du monde), mesurer la latitude et la longitude.
            var big = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            var define = Buttons.Big("carte-navigation", "Définir les marqueurs", world == null
                ? "Les marqueurs sont ceux d'un monde : liez-en un à la carte (onglet Général) pour les définir"
                : "Nommer et placer les marqueurs de « " + world.Name + " » : tropiques, cercles polaires, ou les vôtres", 104, Buttons.Look.Outline);
            define.Tag = "map-guides-define";
            define.IsEnabled = world != null;
            define.Click += delegate { var h = GuidesRequested; var linked = World; if (h != null && linked != null) h(linked); };
            var measure = Buttons.BigToggle("carte-boussole", "Mesures Lat/Lon", "Afficher la latitude et la longitude de l'endroit que pointe le curseur", 104, Buttons.Look.Outline);
            measure.Tag = "map-latlon";
            measure.IsChecked = _latLon;
            measure.Click += delegate { SetLatLon(measure.IsChecked == true); };
            big.Children.Add(define);
            big.Children.Add(measure);
            side.Children.Add(big);
            if (_latLon)
            {
                _geoText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Tag = "map-geo" };
                side.Children.Add(_geoText);
                CursorGeo(false, 0, 0);
                side.Children.Add(Line("La longitude se compte du méridien qui passe au centre de la carte : positive vers l'est."));
            }

            side.Children.Add(Caption("MARQUEURS"));
            var guides = Astro.GuidesOf(world);
            side.Children.Add(Line(world == null ? "Sans monde lié : l'équateur seul. Un monde apporte ses tropiques et ses cercles polaires."
                : world.CustomGuides ? "Ceux que vous avez définis pour « " + world.Name + " »."
                : "Ceux par défaut de « " + world.Name + " », déduits de l'inclinaison de son axe."));
            for (var i = 0; i < guides.Count; i++)
            {
                Color tint;
                if (!Color.TryParse(Astro.ColorOf(guides[i], i), out tint)) tint = Chrome.Accent.Color;
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Opacity = _showGuides ? 1 : 0.6, Tag = "map-guide-row" };
                var dot = new Border { Width = 11, Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var where = new TextBlock { Text = Astro.LatitudeLabel(guides[i].Latitude), Foreground = Chrome.SoftText, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(where, Dock.Right);
                row.Children.Add(where);
                row.Children.Add(new TextBlock { Text = guides[i].Name, Foreground = Chrome.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                side.Children.Add(row);
            }
            if (guides.Count == 0) side.Children.Add(Line("Aucun marqueur n'est défini pour ce monde."));
            if (Maps.Fitted(map)) side.Children.Add(Line("Proportions gardées : les deux pôles sont tracés aussi, là où vous les avez placés."));
        }

        internal void SetGuides(bool shown)
        {
            _showGuides = shown;
            RefreshSide();
            _canvas.InvalidateVisual();
            RememberState(false);
        }

        internal void SetLatLon(bool shown)
        {
            _latLon = shown;
            RefreshSide();
            _canvas.InvalidateVisual();
            RememberState(false);
        }

        // ---- Général : la carte, son monde, son échelle.
        private void FillGeneral(MapInfo map, World world, CultureInfo fr)
        {
            var side = _general;
            var first = Caption("CARTE");
            first.Margin = new Thickness(0, 0, 0, 5);
            side.Children.Add(first);
            if (map.HasImage)
            {
                var bytes = Maps.Bytes(map);
                side.Children.Add(Pair("Image", map.Width.ToString("N0", fr) + " × " + map.Height.ToString("N0", fr) + " px"));
                side.Children.Add(Pair("Zoom", TimeSystem.Count(map.Levels, "niveau", "niveaux") + ", " + TimeSystem.Count(map.Tiles.Count, "tuile", "tuiles")));
                var weight = Pair("Poids dans le .plot", Maps.SizeLabel(bytes));
                weight.Tag = "map-weight";
                side.Children.Add(weight);
                // Lot 12 : les tuiles dorment dans le .plot et ne sont lues que pour l'écran.
                var held = map.Tiles.MemoryBytes;
                var memory = Line(held <= 0 ? "Lue à la demande dans le .plot : seules les tuiles à l'écran sont en mémoire."
                    : held >= bytes ? "Pas encore enregistrée : ses tuiles sont en mémoire (" + Maps.SizeLabel(held) + "). Au premier enregistrement, elles seront lues à la demande."
                    : Maps.SizeLabel(held) + " encore en mémoire, le reste lu à la demande dans le .plot.");
                memory.Tag = "map-memory";
                side.Children.Add(memory);
                if (map.SourceBytes > 0)
                    side.Children.Add(Line("Fichier d'origine" + (string.IsNullOrEmpty(map.SourceName) ? "" : " « " + map.SourceName + " »") + " : " + Maps.SizeLabel(map.SourceBytes)
                        + ". Le plein format est gardé pixel pour pixel ; les niveaux réduits font le reste."));
            }
            var replace = Buttons.Text(map.HasImage ? "Remplacer l'image…" : "Choisir l'image…", "Importer une autre image : les zones et les repères suivent, à l'échelle de la nouvelle", Buttons.Compact, Buttons.Look.Outline);
            replace.HorizontalAlignment = HorizontalAlignment.Left;
            replace.Margin = new Thickness(0, 8, 0, 0);
            replace.Tag = "map-replace";
            replace.Click += delegate { var h = ReplaceImageRequested; if (h != null) h(); };
            side.Children.Add(replace);
            if (!map.HasImage) return;

            // ---- Le monde.
            side.Children.Add(Caption("MONDE"));
            var worlds = new List<World>();
            var names = new List<string> { "Aucun (carte autonome)" };
            if (_project != null) foreach (var w in _project.Worlds) { worlds.Add(w); names.Add(w.Name); }
            var worldBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = names, SelectedIndex = world == null ? 0 : worlds.IndexOf(world) + 1, Tag = "map-world" };
            worldBox.SelectionChanged += delegate
            {
                var index = worldBox.SelectedIndex;
                var id = index <= 0 || index > worlds.Count ? null : worlds[index - 1].Id;
                if (Map == null || id == Map.WorldId) return;
                SetWorld(id);
            };
            side.Children.Add(worldBox);
            if (worlds.Count == 0) side.Children.Add(Line("Aucun monde dans le projet : ils se créent dans l'Astrolabe. Une carte s'en passe très bien."));
            var whole = new CheckBox { Content = "Carte du monde entier", IsChecked = map.WholeWorld, Margin = new Thickness(0, 6, 0, 0), Tag = "map-whole" };
            ToolTip.SetTip(whole, "L'image est le planisphère de tout le monde. Les distances suivent alors la courbure de la sphère, et le rendu Globe devient possible.");
            whole.IsCheckedChanged += delegate
            {
                if (Map == null || (whole.IsChecked == true) == Map.WholeWorld) return;
                SetWholeWorld(whole.IsChecked == true);
            };
            side.Children.Add(whole);
            if (map.WholeWorld)
            {
                // La construction du planisphère (lot 12) : étirée, ou proportions gardées et pôles redéfinis.
                var fitted = Maps.Fitted(map);
                var layout = Line(fitted
                    ? "Construction : proportions gardées. Pôle nord à " + (map.PoleNorth * 100).ToString("0.#", fr) + " % de la hauteur, pôle sud à " + (map.PoleSouth * 100).ToString("0.#", fr)
                        + " % ; l'image couvre " + Maps.LongitudeSpan(map).ToString("0", fr) + "° de longitude" + (Maps.LongitudeSpan(map) > 360.5 ? " — plus d'un tour : elle se recouvre." : ".")
                    : "Construction : étirée — l'image couvre 360° de large, d'un pôle à l'autre."
                        + (Math.Abs((double)map.Width / map.Height - 2) > 0.15 ? " Elle n'est pas deux fois plus large que haute : elle est déformée." : ""));
                layout.Tag = "map-layout";
                side.Children.Add(layout);
                var build = Buttons.Text("Construction du planisphère…", "Étirer l'image sur tout le monde, ou garder ses proportions et dire où sont les pôles", Buttons.Compact, Buttons.Look.Outline);
                build.HorizontalAlignment = HorizontalAlignment.Left;
                build.Margin = new Thickness(0, 6, 0, 0);
                build.Tag = "map-layout-edit";
                build.Click += delegate { EditLayout(); };
                side.Children.Add(build);
            }

            // ---- L'échelle et ce qui s'en déduit.
            side.Children.Add(Caption("ÉCHELLE ET SUPERFICIES"));
            var canMeasure = Maps.CanMeasure(map, world);
            if (map.HasScale) side.Children.Add(Pair("Entre les deux points", map.ScaleNote != null ? map.ScaleNote + " (" + Maps.KmLabel(map.ScaleKm) + ")" : Maps.KmLabel(map.ScaleKm)));
            if (!canMeasure)
                side.Children.Add(Line(map.WholeWorld && world != null
                    ? "« " + world.Name + " » n'a pas de taille : posez une échelle (deux points, une distance ou un temps de trajet) et la carte calculera le tour et la surface du monde."
                    : "Sans échelle, la carte ne mesure rien. Outil « Échelle » : deux points, puis la distance — ou le temps de trajet — qui les sépare."));
            else if (map.WholeWorld)
            {
                var radius = Maps.WorldRadius(map, world);
                var around = Pair("Tour du monde", Maps.KmLabel(radius * 2 * Math.PI));
                around.Tag = "map-around";
                side.Children.Add(around);
                side.Children.Add(Pair("Surface du monde", Maps.AreaLabel(Maps.MapArea(map, world))));
                if (!map.HasScale) side.Children.Add(Line("D'après la taille de « " + world.Name + " » (Astrolabe)."));
                var implied = Maps.ImpliedCircumference(map);
                if (implied > 0 && world != null && Math.Abs(implied - world.Circumference) > 0.5)
                {
                    side.Children.Add(Line(world.Circumference > 0
                        ? "L'Astrolabe donne à « " + world.Name + " » un tour de " + Maps.KmLabel(world.Circumference) + " : la carte et le monde ne disent pas la même chose."
                        : "« " + world.Name + " » n'a pas encore de taille dans l'Astrolabe."));
                    var give = Buttons.Text("Donner cette taille au monde", "Écrire ce tour du monde dans « " + world.Name + " » (Astrolabe). Ctrl+Z l'annule.", Buttons.Compact, Buttons.Look.Outline);
                    give.HorizontalAlignment = HorizontalAlignment.Left;
                    give.Margin = new Thickness(0, 6, 0, 0);
                    give.Tag = "map-give-size";
                    give.Click += delegate { var h = WorldSizeRequested; if (h != null) h(world, implied); };
                    side.Children.Add(give);
                }
            }
            else
            {
                var perPixel = Maps.KmPerPixel(map);
                side.Children.Add(Pair("Un pixel", Maps.KmLabel(perPixel)));
                side.Children.Add(Pair("La carte couvre", Maps.KmLabel(map.Width * perPixel) + " × " + Maps.KmLabel(map.Height * perPixel)));
                var area = Maps.MapArea(map, world);
                var areaRow = Pair("Superficie", Maps.AreaLabel(area));
                areaRow.Tag = "map-area";
                side.Children.Add(areaRow);
                var surface = Maps.WorldSurface(world);
                if (surface > 0)
                    side.Children.Add(Line("Soit " + (area / surface).ToString(area / surface < 0.01 ? "0.00 %" : "0.0 %", fr) + " de la surface de « " + world.Name + " »."
                        + (area > surface ? " Plus que le monde entier : l'échelle ou la taille du monde est à revoir." : "")));
                else if (world != null) side.Children.Add(Line("« " + world.Name + " » n'a pas de taille dans l'Astrolabe : la part du monde que couvre la carte ne se calcule pas."));
            }
            var scaleButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var setScale = Buttons.Text(map.HasScale ? "Reposer l'échelle" : "Poser l'échelle", "Cliquer deux points de la carte, puis dire ce qui les sépare", Buttons.Compact, Buttons.Look.Outline);
            setScale.IsEnabled = !GlobeShown;
            setScale.Click += delegate { ChooseTool(MapCanvas.Tool.Scale); };
            scaleButtons.Children.Add(setScale);
            if (map.HasScale)
            {
                var drop = Buttons.Text("Retirer", null, Buttons.Compact, Buttons.Look.Calm);
                drop.Click += delegate { var before = Map.Clone(); Map.ScaleKm = 0; Map.ScaleNote = null; Commit(before); };
                scaleButtons.Children.Add(drop);
            }
            side.Children.Add(scaleButtons);
        }

        // ---- Jour, nuit et saisons (onglet du rail).
        private void FillSky(MapInfo map, World world, CultureInfo fr)
        {
            if (world == null) { _sky.Children.Add(Line("Le jour, la nuit et les saisons demandent un monde : liez-en un à la carte dans l'onglet Général (les mondes se créent dans l'Astrolabe).")); return; }
            if (!map.WholeWorld) { _sky.Children.Add(Line("Le jour, la nuit et les saisons se dessinent sur la carte du monde entier : cochez « Carte du monde entier » dans l'onglet Général si cette image en est une.")); return; }
            AddSky(world, fr);
        }

        // ---- Zones et repères (onglet du rail).
        private void FillShapes(MapInfo map, World world, CultureInfo fr)
        {
            var side = _shapes;
            var canMeasure = Maps.CanMeasure(map, world);

            // Les filtres (lot 12) : montrer ou masquer les zones, les repères.
            var first = Caption("AFFICHER");
            first.Margin = new Thickness(0, 0, 0, 5);
            side.Children.Add(first);
            var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var zonesFilter = Buttons.TextToggle("Zones (" + map.Zones.Count + ")", "Montrer ou masquer les zones sur la carte", Buttons.Compact);
            zonesFilter.IsChecked = _showZones;
            zonesFilter.Tag = "map-filter-zones";
            zonesFilter.Click += delegate { SetFilters(zonesFilter.IsChecked == true, _showMarkers); };
            var markersFilter = Buttons.TextToggle("Repères (" + map.Markers.Count + ")", "Montrer ou masquer les repères sur la carte", Buttons.Compact);
            markersFilter.IsChecked = _showMarkers;
            markersFilter.Tag = "map-filter-markers";
            markersFilter.Click += delegate { SetFilters(_showZones, markersFilter.IsChecked == true); };
            filters.Children.Add(zonesFilter);
            filters.Children.Add(markersFilter);
            side.Children.Add(filters);
            if (!_showZones || !_showMarkers)
                side.Children.Add(Line(!_showZones && !_showMarkers ? "Zones et repères masqués : la carte seule." : !_showZones ? "Les zones sont masquées." : "Les repères sont masqués."));

            // ---- La sélection.
            var zone = SelectedZone;
            var marker = SelectedMarker;
            if (zone != null || marker != null)
            {
                side.Children.Add(Caption(zone != null ? "ZONE CHOISIE" : "REPÈRE CHOISI"));
                side.Children.Add(new TextBlock { Text = zone != null ? zone.Name : marker.Name, Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "map-selection" });
                var linkId = zone != null ? zone.LinkId : marker.LinkId;
                var linked = linkId == null ? null : _project.FindById(linkId);
                side.Children.Add(Line(linked != null ? "Fiche : " + linked.Title : "Aucune fiche liée."));
                side.Children.Add(Line("Visible : " + Maps.LevelNames[zone != null ? zone.Level : marker.Level].ToLower(fr) + "."));
                if (zone != null)
                {
                    if (canMeasure)
                    {
                        var area = Maps.Area(map, world, zone.Points);
                        var zoneArea = Pair("Superficie", Maps.AreaLabel(area));
                        zoneArea.Tag = "map-zone-area";
                        side.Children.Add(zoneArea);
                        var surface = map.WholeWorld ? Maps.MapArea(map, world) : Maps.WorldSurface(world);
                        if (surface > 0) side.Children.Add(Line("Soit " + (area / surface).ToString(area / surface < 0.01 ? "0.00 %" : "0.0 %", fr) + " de la surface du monde."));
                    }
                    side.Children.Add(Line(TimeSystem.Count(zone.Points.Count, "sommet", "sommets") + " : tirez une poignée pour la déplacer, cliquez un demi-point pour en ajouter une, clic droit pour en retirer une."));
                }
                var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (linked != null)
                {
                    var open = Buttons.Text("Ouvrir la fiche", "Ouvrir « " + linked.Title + " »", Buttons.Compact, Buttons.Look.Primary);
                    open.Margin = new Thickness(0, 0, 6, 6);
                    open.Tag = "map-open-sheet";
                    open.Click += delegate { var h = NavigateRequested; if (h != null) h(linked); };
                    actions.Children.Add(open);
                }
                var edit = Buttons.Text("Modifier…", "Nom, couleur, fiche liée, visibilité", Buttons.Compact, Buttons.Look.Outline);
                edit.Margin = new Thickness(0, 0, 6, 6);
                edit.Click += delegate { EditSelection(); };
                actions.Children.Add(edit);
                var remove = Buttons.Text("Supprimer", "Ou la touche Suppr", Buttons.Compact, Buttons.Look.Calm);
                remove.Margin = new Thickness(0, 0, 6, 6);
                remove.Click += delegate { DeleteSelection(); };
                actions.Children.Add(remove);
                side.Children.Add(actions);
            }

            // ---- Les listes.
            side.Children.Add(Caption("ZONES"));
            if (map.Zones.Count == 0) side.Children.Add(Line("Aucune. Outil « Zone » : un pays, une région, une mer — liée au besoin à une fiche."));
            foreach (var entry in map.Zones) side.Children.Add(ShapeRow(entry.Id, entry.Name, entry.Color, false, canMeasure ? Maps.AreaLabel(Maps.Area(map, world, entry.Points)) : null, entry.LinkId, !_showZones));
            side.Children.Add(Caption("REPÈRES"));
            if (map.Markers.Count == 0) side.Children.Add(Line("Aucun. Outil « Repère » : une ville, un lieu."));
            foreach (var entry in map.Markers) side.Children.Add(ShapeRow(entry.Id, entry.Name, entry.Color, true, null, entry.LinkId, !_showMarkers));
        }

        /// <summary>Les filtres : ce qui est masqué ne se voit plus, ne se clique plus — et ne reste pas choisi.</summary>
        internal void SetFilters(bool zones, bool markers)
        {
            _showZones = zones;
            _showMarkers = markers;
            RememberState(false);
            if (!zones) _selectedZone = null;
            if (!markers) _selectedMarker = null;
            RefreshSide();
            _canvas.InvalidateVisual();
        }

        /// <summary>« Construction du planisphère… » : étirée, ou proportions gardées et pôles redéfinis.</summary>
        private async void EditLayout()
        {
            var map = Map;
            if (map == null || !map.HasImage) return;
            var choice = await MapLayoutDialog.Ask(Ui.OwnerOf(this), map);
            if (choice == null) return;
            SetLayout(choice.Fitted, choice.PoleNorth, choice.PoleSouth);
        }

        internal void SetLayout(bool fitted, double poleNorth, double poleSouth)
        {
            var map = Map;
            if (map == null) return;
            var before = map.Clone();
            map.Layout = fitted ? Maps.LayoutFitted : Maps.LayoutStretch;
            if (fitted && poleSouth - poleNorth >= 0.01) { map.PoleNorth = poleNorth; map.PoleSouth = poleSouth; }
            else Maps.ResetPoles(map);
            if (fitted) _showGuides = true; // on vient de placer les pôles : on les voit
            Commit(before);
        }


        private void AddSky(World world, CultureInfo fr)
        {
            var side = _sky;
            var show = new CheckBox { Content = "Montrer le jour et la nuit", IsChecked = _night, Tag = "map-night" };
            side.Children.Add(show);
            // Les points de chaleur des saisons (lot 12) : montrés d'office, désactivables ici.
            var heat = new CheckBox { Content = "Montrer les saisons sur la carte", IsChecked = _seasonHeat, IsEnabled = SeasonHeatPossible, Margin = new Thickness(0, 2, 0, 0), Tag = "map-season-heat" };
            ToolTip.SetTip(heat, "Un voile de la couleur de la saison qui règne sur chaque hémisphère, au jour choisi : il s'éteint là où la saison s'efface, et se fond vers la teinte de la saison qui vient.");
            heat.IsCheckedChanged += delegate { _seasonHeat = heat.IsChecked == true; RefreshLegend(); _canvas.InvalidateVisual(); RememberState(false); };
            side.Children.Add(heat);
            _legend = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Tag = "map-season-legend" };
            var yearDays = YearDays;
            _dayOfYear = Math.Max(1, Math.Min(Math.Floor(yearDays), _dayOfYear));
            var system = WorldSystem;
            var dayLabel = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "map-day" };
            var day = new Slider { Minimum = 1, Maximum = Math.Max(2, Math.Floor(yearDays)), Value = _dayOfYear, TickFrequency = 1, IsSnapToTickEnabled = true };
            var hourLabel = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            var hour = new Slider { Minimum = 0, Maximum = 1, Value = _dayFraction };
            var seasons = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "map-seasons" };
            Action describe = delegate
            {
                var date = "";
                // Le jour de l'année, redit dans le calendrier du monde.
                if (system != null) date = "  ·  " + TimeMath.ShortDayOfYear(system, (int)_dayOfYear);
                dayLabel.Text = "Jour " + _dayOfYear.ToString("0", fr) + " de l'année" + date;
                var hours = system != null && system.HoursPerDay > 0 ? system.HoursPerDay : 24;
                var clock = _dayFraction * hours;
                hourLabel.Text = "Au centre de la carte : " + Math.Floor(clock).ToString("0", fr) + " h " + Math.Floor((clock - Math.Floor(clock)) * 60).ToString("00", fr)
                    + (_dayFraction < 0.25 || _dayFraction > 0.75 ? " (nuit)" : " (jour)");
                int north = Maps.SeasonIndex(world, _dayOfYear, yearDays, false), south = Maps.SeasonIndex(world, _dayOfYear, yearDays, true);
                var text = north < 0 ? "Ce monde n'a pas de saisons (Astrolabe)." : world.Seasons[north].Name + " au nord, " + world.Seasons[south].Name + " au sud.";
                if (!world.HasTilt) text += " Sans inclinaison de l'axe, le jour vaut la nuit partout, toute l'année.";
                else
                {
                    var declination = Declination;
                    var share = Maps.DayShare(Math.PI / 4, declination);
                    text += " À mi-chemin du pôle nord, le jour dure " + (share * hours).ToString("0.#", fr) + " h sur " + hours.ToString("0.#", fr)
                        + " ; le soleil est au zénith par " + Math.Abs(declination * 180 / Math.PI).ToString("0.#", fr) + "° " + (declination >= 0 ? "nord" : "sud") + ".";
                }
                seasons.Text = text;
                RefreshLegend();
            };
            show.IsCheckedChanged += delegate { _night = show.IsChecked == true; _canvas.InvalidateVisual(); RememberState(false); };
            day.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != RangeBase.ValueProperty) return;
                _dayOfYear = Math.Round(day.Value);
                describe();
                _canvas.InvalidateVisual();
                RememberState(false);
            };
            hour.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != RangeBase.ValueProperty) return;
                _dayFraction = hour.Value;
                describe();
                _canvas.InvalidateVisual();
                RememberState(false);
            };
            describe();
            side.Children.Add(dayLabel);
            side.Children.Add(day);
            side.Children.Add(hourLabel);
            side.Children.Add(hour);
            side.Children.Add(seasons);
            side.Children.Add(_legend);
        }

        private StackPanel _legend;

        /// <summary>La légende des points de chaleur : chaque saison du monde, sa teinte, et où elle règne au jour choisi.</summary>
        private void RefreshLegend()
        {
            if (_legend == null) return;
            _legend.Children.Clear();
            var world = World;
            if (world == null || world.Seasons.Count == 0) return;
            if (!world.HasTilt || world.AxialTilt < 1e-6)
            {
                _legend.Children.Add(Line("Sans inclinaison de l'axe, aucune saison ne se marque sur la carte : il n'y a rien à montrer."));
                return;
            }
            if (!_seasonHeat) return;
            var yearDays = YearDays;
            int north = Maps.SeasonIndex(world, _dayOfYear, yearDays, false), south = Maps.SeasonIndex(world, _dayOfYear, yearDays, true);
            // Le fondu (lot 13) : la saison vers laquelle chaque hémisphère glisse, et de combien.
            int northA, northB, southA, southB;
            double northShare, southShare;
            Maps.SeasonMix(world, _dayOfYear, yearDays, false, out northA, out northB, out northShare);
            Maps.SeasonMix(world, _dayOfYear, yearDays, true, out southA, out southB, out southShare);
            for (var i = 0; i < world.Seasons.Count; i++)
            {
                Color tint;
                if (!Color.TryParse(Astro.ColorOf(world.Seasons[i], i), out tint)) tint = Chrome.Accent.Color;
                var where = i == north && i == south ? "partout" : i == north ? "au nord" : i == south ? "au sud"
                    : i == northB && northShare > 0.02 ? "au nord, à " + northShare.ToString("0 %", CultureInfo.GetCultureInfo("fr-FR"))
                    : i == southB && southShare > 0.02 ? "au sud, à " + southShare.ToString("0 %", CultureInfo.GetCultureInfo("fr-FR")) : "absente ce jour-là";
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Opacity = where == "absente ce jour-là" ? 0.55 : 1, Tag = "map-season-row" };
                var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var place = new TextBlock { Text = where, Foreground = Chrome.SoftText, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(place, Dock.Right);
                row.Children.Add(place);
                row.Children.Add(new TextBlock { Text = world.Seasons[i].Name, Foreground = Chrome.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _legend.Children.Add(row);
            }
            _legend.Children.Add(Line("Entre les tropiques (" + world.AxialTilt.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + "° de part et d'autre de l'équateur), les saisons s'effacent : le voile s'éteint vers l'équateur. D'une saison à l'autre, les teintes se fondent."));
        }

        private Control ShapeRow(string id, string name, string color, bool marker, string measure, string linkId, bool hidden)
        {
            Color tint;
            if (!Color.TryParse(color, out tint)) tint = Chrome.Accent.Color;
            var chosen = id == _selectedZone || id == _selectedMarker;
            var row = new DockPanel();
            var dot = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(marker ? 5.5 : 2.5), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            if (measure != null && measure != "—")
            {
                var value = new TextBlock { Text = measure, Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
            }
            row.Children.Add(new TextBlock
            {
                Text = (string.IsNullOrEmpty(name) ? (marker ? "Repère" : "Zone") : name) + (linkId != null ? "  ↗" : ""), Foreground = Chrome.Ink, FontSize = 12.5,
                FontWeight = chosen ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });
            var box = new Border
            {
                Child = row, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 2, 0, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Background = chosen ? Chrome.AccentTint : Brushes.Transparent, Tag = "map-shape-row", Opacity = hidden ? 0.5 : 1
            };
            box.PointerPressed += delegate
            {
                var map = Map;
                if (map == null) return;
                // Choisir dans la liste ce qui est masqué le montre de nouveau.
                if (marker && !_showMarkers) _showMarkers = true;
                if (!marker && !_showZones) _showZones = true;
                var zone = map.FindZone(id);
                var point = map.FindMarker(id);
                Select(zone, point);
                if (zone != null) { var centre = Maps.Centroid(zone.Points); _canvas.CenterOn(centre.X, centre.Y); }
                else if (point != null) _canvas.CenterOn(point.X, point.Y);
            };
            return box;
        }

        private void SetWorld(string worldId)
        {
            var map = Map;
            var before = map.Clone();
            map.WorldId = worldId;
            Commit(before);
        }

        private void SetWholeWorld(bool whole)
        {
            var map = Map;
            var before = map.Clone();
            map.WholeWorld = whole;
            if (!whole) map.View = Maps.ViewFlat; // plus de globe pour une carte qui n'est pas le monde entier
            Commit(before);
        }

        // ================================================== la sonde

        internal MapCanvas CanvasForProbe { get { return _canvas; } }
        internal string HintForProbe { get { return _hint.Text; } }
        internal string ZoomLabelForProbe { get { return _zoomLabel.Text; } }
        internal void SetWorldForProbe(string id, bool whole) { var before = Map.Clone(); Map.WorldId = id; Map.WholeWorld = whole; if (!whole) Map.View = Maps.ViewFlat; Commit(before); }
        internal bool GlobeButtonShownForProbe { get { return _globe.IsVisible; } }
        internal bool ToolCheckedForProbe(MapCanvas.Tool tool) { return _tools[tool].IsChecked == true; }
        internal void SetSeasonHeatForProbe(bool on) { _seasonHeat = on; RememberState(false); RefreshSide(); _canvas.InvalidateVisual(); }
        internal bool SpaceKeyForProbe(bool down, bool pointerOver)
        {
            // Le même chemin que la touche, sans dépendre de la souris réelle de la sonde.
            if (down) { if (!IsVisible || _item == null || !pointerOver || TypingSomewhere()) return false; _canvas.SetSpace(true); return true; }
            if (!_canvas.SpaceHeld) return false;
            _canvas.SetSpace(false);
            return true;
        }
        internal void SetViewForProbe(string view) { SetViewMode(view); }
        internal void SetNightForProbe(bool on, double day, double fraction) { _night = on; _dayOfYear = day; _dayFraction = fraction; RefreshSide(); _canvas.InvalidateVisual(); }
        internal bool ToolEnabledForProbe(MapCanvas.Tool tool) { return _tools[tool].IsEnabled; }

        internal List<string> SideTextsForProbe()
        {
            var texts = new List<string>();
            Collect(_general, texts);
            Collect(_sky, texts);
            Collect(_shapes, texts);
            Collect(_nav, texts);
            return texts;
        }

        private static void Collect(Control control, List<string> texts)
        {
            var block = control as TextBlock;
            if (block != null) { if (!string.IsNullOrEmpty(block.Text)) texts.Add(block.Text); return; }
            var panel = control as Panel;
            if (panel != null) { foreach (var child in panel.Children) Collect(child, texts); return; }
            var decorator = control as Decorator;
            if (decorator != null && decorator.Child != null) { Collect(decorator.Child, texts); return; }
            var content = control as ContentControl;
            if (content != null && content.Content is Control) Collect((Control)content.Content, texts);
            else if (content != null && content.Content is string) texts.Add((string)content.Content);
        }
    }
}
