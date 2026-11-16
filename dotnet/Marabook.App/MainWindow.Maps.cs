using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Marabook.Model;

namespace Marabook.App
{
    // LES CARTES dans la fenêtre (2.0.0, 16/11/2026) : la page de la racine « Cartes »,
    // l'écran d'une carte, l'import d'une image (découpée en tuiles), et l'Analyseur de
    // .plot du menu Aide. La page de l'Astrolabe (systèmes temporels et mondes, sans les
    // frises parties en « Chronologie ») est bâtie ici aussi.
    public partial class MainWindow
    {
        private MapView _mapView;              // une carte
        private MapsHubView _mapsHub;          // la racine « Cartes »
        private TimelineHubView _astrolabeHub; // la racine « Astrolabe » : systèmes et mondes
        private ContentControl _mapSection;    // la carte dans l'onglet Général du rail (lot 12)
        private Border _mapSkyHost, _mapShapesHost; // ses deux onglets propres : jour-nuit-saisons, zones et repères

        /// <summary>Les deux onglets d'une carte ouverte (lot 12, Rémi) : leurs hôtes dans la colonne de droite.
        /// Leur contenu appartient à la vue de la carte, qui le tient à jour.</summary>
        private void BuildMapPanels(Grid grid)
        {
            _mapSkyHost = ToolHost("Jour, nuit et saisons", _mapView.SkyPanel);
            _mapShapesHost = ToolHost("Zones et repères", _mapView.ShapesPanel);
            foreach (var host in new[] { _mapSkyHost, _mapShapesHost })
            {
                host.IsVisible = false;
                Grid.SetColumn(host, 4);
                grid.Children.Add(host);
            }
        }

        /// <summary>La carte ouverte dans le Général du rail : son image, son monde, son échelle et ses superficies.</summary>
        private void RefreshMapSection(BinderItem item)
        {
            if (_mapSection == null || _mapView == null) return;
            var shown = item != null && item.Kind == ItemKind.Map && _mapView.IsVisible && _mapView.ShowsItem(item);
            var content = shown ? _mapView.GeneralPanel : null;
            if (!ReferenceEquals(_mapSection.Content, content)) _mapSection.Content = content;
            _mapSection.IsVisible = shown;
        }

        internal Border MapSkyHostForProbe { get { return _mapSkyHost; } }
        internal Border MapShapesHostForProbe { get { return _mapShapesHost; } }
        internal bool MapSectionShownForProbe { get { return _mapSection != null && _mapSection.IsVisible && ReferenceEquals(_mapSection.Content, _mapView.GeneralPanel); } }
        internal bool RailMapSeparatorForProbe
        {
            get
            {
                var index = -1;
                for (var i = 0; i < _railStack.Children.Count; i++) if (Equals(_railStack.Children[i].Tag, "rail-map-separator")) index = i;
                return index > 0 && index == _railStack.Children.Count - 3;
            }
        }

        private void BuildMapViews(Panel center)
        {
            _astrolabeHub = new TimelineHubView(false) { IsVisible = false };
            _astrolabeHub.NewSystemRequested += delegate { EditTimeSystem(null); };
            _astrolabeHub.OurSystemRequested += delegate { AddOurSystem(); };
            _astrolabeHub.EditSystemRequested += delegate(TimeSystem system) { EditTimeSystem(system); };
            _astrolabeHub.RemoveSystemRequested += delegate(TimeSystem system) { RemoveTimeSystem(system); };
            _astrolabeHub.CalculatorRequested += delegate(TimeSystem system) { ShowCalculator(system, null); };
            _astrolabeHub.EditWorldRequested += delegate(World world) { EditWorld(world); };
            _astrolabeHub.RemoveWorldRequested += delegate(World world) { RemoveWorld(world); };
            _astrolabeHub.OtherPageRequested += delegate { SelectRoot(Project.KeyTimelines); };
            center.Children.Add(_astrolabeHub);

            _mapView = new MapView { IsVisible = false };
            _mapView.Edited += delegate
            {
                MarkDirty();
                UpdateStats();
            };
            _mapView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _mapView.BackRequested += delegate { SelectRoot(Project.KeyMaps); };
            _mapView.RenameRequested += delegate
            {
                if (_current == null || _current.Kind != ItemKind.Map) return;
                _binder.RenameQuiet(_current);
                _mapView.Refresh();
                UpdateInspector();
            };
            _mapView.ReplaceImageRequested += delegate { ReplaceMapImage(); };
            _mapView.WorldSizeRequested += delegate(World world, double circumference) { GiveWorldSize(world, circumference); };
            center.Children.Add(_mapView);

            _mapsHub = new MapsHubView { IsVisible = false };
            _mapsHub.OpenRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _mapsHub.NewMapRequested += delegate { NewMap(); };
            _mapsHub.AnalyzerRequested += delegate { ShowPlotAnalyzer(); };
            center.Children.Add(_mapsHub);
            _binder.NewMapRequested += delegate { NewMap(); };
        }

        internal MapView MapForProbe { get { return _mapView; } }
        internal MapsHubView MapsHubForProbe { get { return _mapsHub; } }
        internal TimelineHubView AstrolabeHubForProbe { get { return _astrolabeHub; } }

        private void SelectRoot(string key)
        {
            var root = _project == null ? null : _project.Category(key);
            if (root != null) _binder.SelectItem(root.Id);
        }

        /// <summary>Une nouvelle carte : son image, découpée en tuiles et optimisée, puis la carte s'ouvre.</summary>
        private async void NewMap()
        {
            if (_project == null) return;
            var path = await Ui.PickOpenFile(this, "L'image de la carte", MapImport.OpenFilter);
            if (path == null) return;
            // Lot 12 : la fenêtre d'import propose aussi le monde et « Carte du monde entier ».
            var map = await MapImportDialog.Run(this, path, _project, true);
            if (map == null) return;
            AddImportedMap(Path.GetFileNameWithoutExtension(path), map);
        }

        private void AddImportedMap(string title, MapInfo map)
        {
            var item = new BinderItem { Kind = ItemKind.Map, Title = string.IsNullOrWhiteSpace(title) ? "Nouvelle carte" : title, Map = map };
            _binder.AddMap(item);
            ShowToast(null, "Carte importée", item.Title,
                map.Width + " × " + map.Height + " px · " + TimeSystem.Count(map.Levels, "niveau de zoom", "niveaux de zoom") + " · " + TimeSystem.Count(map.Tiles.Count, "tuile", "tuiles")
                + " · " + Maps.SizeLabel(Maps.Bytes(map)) + " dans le .plot (fichier d'origine : " + Maps.SizeLabel(map.SourceBytes) + ")", 8);
        }

        internal BinderItem ImportMapForProbe(string title, byte[] bytes)
        {
            var map = MapImport.Slice(bytes, title + ".jpg");
            AddImportedMap(title, map);
            return Maps.All(_project).Find(delegate(BinderItem item) { return item.Map == map || item.Title == title; });
        }

        /// <summary>« Remplacer l'image… » : une autre image pour la même carte. Les zones, les repères
        /// et l'échelle suivent, ramenés aux dimensions de la nouvelle. Ctrl+Z rend l'ancienne.</summary>
        private async void ReplaceMapImage()
        {
            var item = _current;
            if (_project == null || item == null || item.Kind != ItemKind.Map) return;
            var path = await Ui.PickOpenFile(this, "La nouvelle image de la carte", MapImport.OpenFilter);
            if (path == null) return;
            var fresh = await MapImportDialog.Run(this, path);
            if (fresh == null || item.Map == null) return;
            ReplaceMapImage(item, fresh);
        }

        internal void ReplaceMapImage(BinderItem item, MapInfo fresh)
        {
            var before = item.Map.Clone();
            var old = item.Map;
            double fx = old.Width > 0 ? (double)fresh.Width / old.Width : 1, fy = old.Height > 0 ? (double)fresh.Height / old.Height : 1;
            fresh.WorldId = old.WorldId;
            fresh.WholeWorld = old.WholeWorld;
            fresh.View = old.View;
            fresh.Layout = old.Layout;
            fresh.PoleNorth = old.PoleNorth;
            fresh.PoleSouth = old.PoleSouth;
            fresh.ScaleKm = old.ScaleKm;
            fresh.ScaleNote = old.ScaleNote;
            fresh.ScaleA = new Pos(old.ScaleA.X * fx, old.ScaleA.Y * fy);
            fresh.ScaleB = new Pos(old.ScaleB.X * fx, old.ScaleB.Y * fy);
            foreach (var zone in old.Zones)
            {
                var copy = zone.Clone();
                for (var i = 0; i < copy.Points.Count; i++) copy.Points[i] = new Pos(copy.Points[i].X * fx, copy.Points[i].Y * fy);
                fresh.Zones.Add(copy);
            }
            foreach (var marker in old.Markers)
            {
                var copy = marker.Clone();
                copy.X *= fx;
                copy.Y *= fy;
                fresh.Markers.Add(copy);
            }
            item.Map = fresh;
            _history.Push(new History.MapEditAction(item, before, fresh.Clone()));
            MarkDirty();
            RefreshMapViews();
        }

        /// <summary>« Donner cette taille au monde » : le tour que la carte et son échelle impliquent
        /// s'écrit dans le monde de l'Astrolabe. Ctrl+Z rend la taille d'avant.</summary>
        private void GiveWorldSize(World world, double circumference)
        {
            if (_project == null || world == null || circumference <= 0 || !_project.Worlds.Contains(world)) return;
            var before = History.WorldsEditAction.Snapshot(_project);
            var links = History.WorldsEditAction.Links(_project);
            world.Circumference = Math.Round(circumference);
            _history.Push(new History.WorldsEditAction(_project, before, links));
            MarkDirty();
            RefreshTimelineViews();
            RefreshMapViews();
            ShowToast(null, "Astrolabe", "« " + world.Name + " » mesure " + Maps.KmLabel(world.Circumference) + " de tour", Ui.Keys("Ctrl+Z") + " rend la taille d'avant", 6);
        }

        internal void GiveWorldSizeForProbe(World world, double circumference) { GiveWorldSize(world, circumference); }

        /// <summary>Tout ce qui montre une carte se relit (une annulation, un monde modifié, un titre).</summary>
        private void RefreshMapViews()
        {
            if (_mapView != null && _mapView.IsVisible) _mapView.Refresh();
            if (_mapsHub != null && _mapsHub.IsVisible) _mapsHub.Refresh();
            if (_astrolabeHub != null && _astrolabeHub.IsVisible) _astrolabeHub.Refresh();
            UpdateStats();
        }

        /// <summary>Aide › Analyseur de .plot : ce que pèse chaque section du projet dans son fichier.</summary>
        private void ShowPlotAnalyzer()
        {
            if (_project == null) return;
            PlotAnalyzerDialog.Show(this, _path, _project, _dirty);
        }

        /// <summary>« Parallélisme » depuis la page Chronologie : on choisit les deux frises, la première s'ouvre, la seconde dessous.</summary>
        private async void AskParallel()
        {
            if (_project == null) return;
            var pair = await ParallelDialog.Ask(this, _project);
            if (pair == null) return;
            OpenParallel(pair.Item1, pair.Item2);
        }

        internal void OpenParallel(BinderItem top, BinderItem bottom)
        {
            _binder.SelectItem(top.Id);
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, delegate
            {
                if (_timelineView.IsVisible && _timelineView.ShowsItem(top)) _timelineView.SetParallel(bottom);
            });
        }
    }
}
