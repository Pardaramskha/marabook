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
    /// sauf ce qui demande un ciel (jour et nuit, saisons, part de la surface du monde).</summary>
    public class MapView : DockPanel
    {
        private readonly TextBlock _title;
        private readonly MapCanvas _canvas;
        private readonly StackPanel _side;
        private readonly TextBlock _hint, _zoomLabel;
        private readonly ToggleButton _flat, _globe;
        private readonly Dictionary<MapCanvas.Tool, ToggleButton> _tools = new Dictionary<MapCanvas.Tool, ToggleButton>();
        private readonly TextBlock _empty;
        private BinderItem _item;
        private Project _project;
        private HistoryManager _history;
        private string _selectedZone, _selectedMarker;
        private string _measure;

        // Le jour et la nuit : l'état d'une session (rien de cela ne s'enregistre).
        private bool _night;
        private double _dayOfYear = 1, _dayFraction = 0.5;

        public event Action Edited;
        public event Action BackRequested;
        public event Action RenameRequested;
        public event Action ReplaceImageRequested;
        public event Action<BinderItem> NavigateRequested;
        public event Action<World, double> WorldSizeRequested;   // donner au monde le tour que la carte implique

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
            bar.Children.Add(new TextBlock { Text = "Rendu :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _flat = Buttons.TextToggle("Planisphère", "L'image à plat : c'est ici qu'on trace les zones, les repères, l'échelle", Buttons.Compact);
            _flat.Tag = "map-view-flat";
            _flat.Click += delegate { SetViewMode(Maps.ViewFlat); };
            _globe = Buttons.TextToggle("Globe", "L'image enroulée sur une planète, qu'on fait tourner à la souris", Buttons.Compact);
            _globe.Tag = "map-view-globe";
            _globe.Margin = new Thickness(4, 0, 0, 0);
            _globe.Click += delegate { SetViewMode(Maps.ViewGlobe); };
            bar.Children.Add(_flat);
            bar.Children.Add(_globe);
            bar.Children.Add(new Border { Width = 1, Background = Chrome.Border, Margin = new Thickness(12, 2, 12, 2) });
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

            // ================================================== le panneau de la carte
            _side = new StackPanel { Margin = new Thickness(14, 12, 14, 16) };
            var sideBox = new Border
            {
                Width = 286, Background = Chrome.BarBgLight, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _side }
            };
            SetDock(sideBox, Dock.Right);
            Children.Add(sideBox);

            // ================================================== la toile
            _canvas = new MapCanvas(this);
            _empty = new TextBlock
            {
                Text = "Cette carte n'a pas d'image.\n« Remplacer l'image… », dans le panneau, lui en donne une.",
                Foreground = Chrome.SoftText, FontSize = 13, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, IsVisible = false
            };
            var center = new Grid();
            center.Children.Add(_canvas);
            center.Children.Add(_empty);
            Children.Add(center);
        }

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
            _item = item;
            _project = project;
            _history = history;
            if (item.Map == null) item.Map = new MapInfo();
            if (!same)
            {
                _selectedZone = _selectedMarker = null;
                _measure = null;
                _night = false;
                _dayOfYear = 1;
                _dayFraction = 0.5;
                _canvas.Attach();
                _canvas.ResetView();
            }
            Refresh();
        }

        public void Clear()
        {
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
            _flat.IsChecked = map.View != Maps.ViewGlobe;
            _globe.IsChecked = map.View == Maps.ViewGlobe;
            // Le tracé se fait à plat : au globe, les outils se grisent.
            foreach (var pair in _tools)
            {
                pair.Value.IsEnabled = map.HasImage && (pair.Key == MapCanvas.Tool.Pan || map.View != Maps.ViewGlobe);
                pair.Value.IsChecked = pair.Key == _canvas.CurrentTool;
            }
            if (map.View == Maps.ViewGlobe && _canvas.CurrentTool != MapCanvas.Tool.Pan) _canvas.SetTool(MapCanvas.Tool.Pan);
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
            _zoomLabel.Text = map.View == Maps.ViewGlobe ? "Zoom × " + _canvas.ZoomRatio.ToString("0.#", fr)
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
        }

        private void DescribeTool()
        {
            _hint.Foreground = Chrome.SoftText;
            var map = Map;
            if (map == null || !map.HasImage) { _hint.Text = ""; return; }
            if (map.View == Maps.ViewGlobe) { _hint.Text = "Glisser fait tourner le globe ; la molette l'approche. Le tracé se fait au planisphère."; return; }
            switch (_canvas.CurrentTool)
            {
                case MapCanvas.Tool.Zone: _hint.Text = "Un clic par sommet ; double-clic, Entrée ou un clic sur le premier point ferme la zone. Retour arrière retire le dernier point, Échap abandonne."; break;
                case MapCanvas.Tool.Marker: _hint.Text = "Cliquez à l'endroit du repère. Échap abandonne."; break;
                case MapCanvas.Tool.Scale: _hint.Text = "Cliquez deux points dont vous connaissez l'écart — en kilomètres ou en jours de voyage."; break;
                case MapCanvas.Tool.Measure: _hint.Text = _measure ?? "Cliquez deux points : la distance s'affiche."; break;
                default: _hint.Text = "Glisser promène la carte, la molette zoome. Un clic choisit une zone ou un repère ; un double-clic ouvre sa fiche."; break;
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
            _side.Children.Clear();
            var map = Map;
            if (map == null) return;
            var world = World;
            var fr = CultureInfo.GetCultureInfo("fr-FR");

            // ---- La carte : ce qu'elle pèse.
            var first = Caption("CARTE");
            first.Margin = new Thickness(0, 0, 0, 5);
            _side.Children.Add(first);
            if (map.HasImage)
            {
                var bytes = Maps.Bytes(map);
                _side.Children.Add(Pair("Image", map.Width.ToString("N0", fr) + " × " + map.Height.ToString("N0", fr) + " px"));
                _side.Children.Add(Pair("Zoom", TimeSystem.Count(map.Levels, "niveau", "niveaux") + ", " + TimeSystem.Count(map.Tiles.Count, "tuile", "tuiles")));
                var weight = Pair("Poids dans le .plot", Maps.SizeLabel(bytes));
                weight.Tag = "map-weight";
                _side.Children.Add(weight);
                if (map.SourceBytes > 0)
                    _side.Children.Add(Line("Fichier d'origine" + (string.IsNullOrEmpty(map.SourceName) ? "" : " « " + map.SourceName + " »") + " : " + Maps.SizeLabel(map.SourceBytes)
                        + ". Le plein format est gardé pixel pour pixel ; les niveaux réduits font le reste."));
            }
            var replace = Buttons.Text(map.HasImage ? "Remplacer l'image…" : "Choisir l'image…", "Importer une autre image : les zones et les repères suivent, à l'échelle de la nouvelle", Buttons.Compact, Buttons.Look.Outline);
            replace.HorizontalAlignment = HorizontalAlignment.Left;
            replace.Margin = new Thickness(0, 8, 0, 0);
            replace.Tag = "map-replace";
            replace.Click += delegate { var h = ReplaceImageRequested; if (h != null) h(); };
            _side.Children.Add(replace);
            if (!map.HasImage) return;

            // ---- Le monde.
            _side.Children.Add(Caption("MONDE"));
            var worlds = new List<World>();
            var names = new List<string> { "Aucun (carte autonome)" };
            if (_project != null) foreach (var w in _project.Worlds) { worlds.Add(w); names.Add(w.Name); }
            var worldBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = names, SelectedIndex = world == null ? 0 : worlds.IndexOf(world) + 1, Tag = "map-world" };
            worldBox.SelectionChanged += delegate
            {
                var index = worldBox.SelectedIndex;
                var id = index <= 0 || index > worlds.Count ? null : worlds[index - 1].Id;
                if (id == Map.WorldId) return;
                SetWorld(id);
            };
            _side.Children.Add(worldBox);
            if (worlds.Count == 0) _side.Children.Add(Line("Aucun monde dans le projet : ils se créent dans l'Astrolabe. Une carte s'en passe très bien."));
            var whole = new CheckBox { Content = "C'est le planisphère du monde entier", IsChecked = map.WholeWorld, Margin = new Thickness(0, 6, 0, 0), Tag = "map-whole" };
            ToolTip.SetTip(whole, "L'image couvre tout le monde : 360° de large, d'un pôle à l'autre (idéalement deux fois plus large que haute). Les distances suivent alors la courbure de la sphère.");
            whole.IsCheckedChanged += delegate
            {
                if ((whole.IsChecked == true) == Map.WholeWorld) return;
                SetWholeWorld(whole.IsChecked == true);
            };
            _side.Children.Add(whole);
            if (map.WholeWorld && Math.Abs((double)map.Width / map.Height - 2) > 0.15)
                _side.Children.Add(Line("L'image n'est pas deux fois plus large que haute : sur le globe et dans les mesures, elle sera étirée d'un pôle à l'autre."));

            // ---- L'échelle et ce qui s'en déduit.
            _side.Children.Add(Caption("ÉCHELLE ET SUPERFICIES"));
            var canMeasure = Maps.CanMeasure(map, world);
            if (map.HasScale) _side.Children.Add(Pair("Entre les deux points", map.ScaleNote != null ? map.ScaleNote + " (" + Maps.KmLabel(map.ScaleKm) + ")" : Maps.KmLabel(map.ScaleKm)));
            if (!canMeasure)
                _side.Children.Add(Line(map.WholeWorld && world != null
                    ? "« " + world.Name + " » n'a pas de taille : posez une échelle (deux points, une distance ou un temps de trajet) et la carte calculera le tour et la surface du monde."
                    : "Sans échelle, la carte ne mesure rien. Outil « Échelle » : deux points, puis la distance — ou le temps de trajet — qui les sépare."));
            else if (map.WholeWorld)
            {
                var radius = Maps.WorldRadius(map, world);
                var around = Pair("Tour du monde", Maps.KmLabel(radius * 2 * Math.PI));
                around.Tag = "map-around";
                _side.Children.Add(around);
                _side.Children.Add(Pair("Surface du monde", Maps.AreaLabel(Maps.MapArea(map, world))));
                if (!map.HasScale) _side.Children.Add(Line("D'après la taille de « " + world.Name + " » (Astrolabe)."));
                var implied = Maps.ImpliedCircumference(map);
                if (implied > 0 && world != null && Math.Abs(implied - world.Circumference) > 0.5)
                {
                    _side.Children.Add(Line(world.Circumference > 0
                        ? "L'Astrolabe donne à « " + world.Name + " » un tour de " + Maps.KmLabel(world.Circumference) + " : la carte et le monde ne disent pas la même chose."
                        : "« " + world.Name + " » n'a pas encore de taille dans l'Astrolabe."));
                    var give = Buttons.Text("Donner cette taille au monde", "Écrire ce tour du monde dans « " + world.Name + " » (Astrolabe). Ctrl+Z l'annule.", Buttons.Compact, Buttons.Look.Outline);
                    give.HorizontalAlignment = HorizontalAlignment.Left;
                    give.Margin = new Thickness(0, 6, 0, 0);
                    give.Tag = "map-give-size";
                    give.Click += delegate { var h = WorldSizeRequested; if (h != null) h(world, implied); };
                    _side.Children.Add(give);
                }
            }
            else
            {
                var perPixel = Maps.KmPerPixel(map);
                _side.Children.Add(Pair("Un pixel", Maps.KmLabel(perPixel)));
                _side.Children.Add(Pair("La carte couvre", Maps.KmLabel(map.Width * perPixel) + " × " + Maps.KmLabel(map.Height * perPixel)));
                var area = Maps.MapArea(map, world);
                var areaRow = Pair("Superficie", Maps.AreaLabel(area));
                areaRow.Tag = "map-area";
                _side.Children.Add(areaRow);
                var surface = Maps.WorldSurface(world);
                if (surface > 0)
                    _side.Children.Add(Line("Soit " + (area / surface).ToString(area / surface < 0.01 ? "0.00 %" : "0.0 %", fr) + " de la surface de « " + world.Name + " »."
                        + (area > surface ? " Plus que le monde entier : l'échelle ou la taille du monde est à revoir." : "")));
                else if (world != null) _side.Children.Add(Line("« " + world.Name + " » n'a pas de taille dans l'Astrolabe : la part du monde que couvre la carte ne se calcule pas."));
            }
            var scaleButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var setScale = Buttons.Text(map.HasScale ? "Reposer l'échelle" : "Poser l'échelle", "Cliquer deux points de la carte, puis dire ce qui les sépare", Buttons.Compact, Buttons.Look.Outline);
            setScale.IsEnabled = map.View != Maps.ViewGlobe;
            setScale.Click += delegate { ChooseTool(MapCanvas.Tool.Scale); };
            scaleButtons.Children.Add(setScale);
            if (map.HasScale)
            {
                var drop = Buttons.Text("Retirer", null, Buttons.Compact, Buttons.Look.Calm);
                drop.Click += delegate { var before = Map.Clone(); Map.ScaleKm = 0; Map.ScaleNote = null; Commit(before); };
                scaleButtons.Children.Add(drop);
            }
            _side.Children.Add(scaleButtons);

            // ---- Le ciel : jour et nuit, saisons.
            if (world != null)
            {
                _side.Children.Add(Caption("JOUR, NUIT ET SAISONS"));
                if (!map.WholeWorld) _side.Children.Add(Line("Le jour et la nuit se dessinent sur le planisphère du monde entier : cochez la case ci-dessus si cette image en est un."));
                else AddSky(world, fr);
            }

            // ---- La sélection.
            var zone = SelectedZone;
            var marker = SelectedMarker;
            if (zone != null || marker != null)
            {
                _side.Children.Add(Caption(zone != null ? "ZONE CHOISIE" : "REPÈRE CHOISI"));
                _side.Children.Add(new TextBlock { Text = zone != null ? zone.Name : marker.Name, Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "map-selection" });
                var linkId = zone != null ? zone.LinkId : marker.LinkId;
                var linked = linkId == null ? null : _project.FindById(linkId);
                _side.Children.Add(Line(linked != null ? "Fiche : " + linked.Title : "Aucune fiche liée."));
                _side.Children.Add(Line("Visible : " + Maps.LevelNames[zone != null ? zone.Level : marker.Level].ToLower(fr) + "."));
                if (zone != null)
                {
                    if (canMeasure)
                    {
                        var area = Maps.Area(map, world, zone.Points);
                        var zoneArea = Pair("Superficie", Maps.AreaLabel(area));
                        zoneArea.Tag = "map-zone-area";
                        _side.Children.Add(zoneArea);
                        var surface = map.WholeWorld ? Maps.MapArea(map, world) : Maps.WorldSurface(world);
                        if (surface > 0) _side.Children.Add(Line("Soit " + (area / surface).ToString(area / surface < 0.01 ? "0.00 %" : "0.0 %", fr) + " de la surface du monde."));
                    }
                    _side.Children.Add(Line(TimeSystem.Count(zone.Points.Count, "sommet", "sommets") + " : tirez une poignée pour la déplacer, cliquez un demi-point pour en ajouter une, clic droit pour en retirer une."));
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
                _side.Children.Add(actions);
            }

            // ---- Les zones et les repères.
            _side.Children.Add(Caption("ZONES ET REPÈRES"));
            if (map.Zones.Count == 0 && map.Markers.Count == 0)
                _side.Children.Add(Line("Rien encore. Outil « Zone » pour tracer un pays, une région, une mer ; outil « Repère » pour une ville ou un lieu. Chacun peut se lier à une fiche."));
            foreach (var entry in map.Zones) _side.Children.Add(ShapeRow(entry.Id, entry.Name, entry.Color, false, canMeasure ? Maps.AreaLabel(Maps.Area(map, world, entry.Points)) : null, entry.LinkId));
            foreach (var entry in map.Markers) _side.Children.Add(ShapeRow(entry.Id, entry.Name, entry.Color, true, null, entry.LinkId));
        }

        private void AddSky(World world, CultureInfo fr)
        {
            var show = new CheckBox { Content = "Montrer le jour et la nuit", IsChecked = _night, Tag = "map-night" };
            _side.Children.Add(show);
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
            };
            show.IsCheckedChanged += delegate { _night = show.IsChecked == true; _canvas.InvalidateVisual(); };
            day.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != RangeBase.ValueProperty) return;
                _dayOfYear = Math.Round(day.Value);
                describe();
                _canvas.InvalidateVisual();
            };
            hour.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != RangeBase.ValueProperty) return;
                _dayFraction = hour.Value;
                describe();
                _canvas.InvalidateVisual();
            };
            describe();
            _side.Children.Add(dayLabel);
            _side.Children.Add(day);
            _side.Children.Add(hourLabel);
            _side.Children.Add(hour);
            _side.Children.Add(seasons);
        }

        private Control ShapeRow(string id, string name, string color, bool marker, string measure, string linkId)
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
                Background = chosen ? Chrome.AccentTint : Brushes.Transparent, Tag = "map-shape-row"
            };
            box.PointerPressed += delegate
            {
                var map = Map;
                if (map == null) return;
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
            Commit(before);
        }

        // ================================================== la sonde

        internal MapCanvas CanvasForProbe { get { return _canvas; } }
        internal string HintForProbe { get { return _hint.Text; } }
        internal string ZoomLabelForProbe { get { return _zoomLabel.Text; } }
        internal void SetWorldForProbe(string id, bool whole) { var before = Map.Clone(); Map.WorldId = id; Map.WholeWorld = whole; Commit(before); }
        internal void SetViewForProbe(string view) { SetViewMode(view); }
        internal void SetNightForProbe(bool on, double day, double fraction) { _night = on; _dayOfYear = day; _dayFraction = fraction; RefreshSide(); _canvas.InvalidateVisual(); }
        internal bool ToolEnabledForProbe(MapCanvas.Tool tool) { return _tools[tool].IsEnabled; }

        internal List<string> SideTextsForProbe()
        {
            var texts = new List<string>();
            Collect(_side, texts);
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
