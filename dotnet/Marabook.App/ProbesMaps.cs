using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>La sonde du lot 11 (2.0.0, 16/11/2026) : le fragment d'une frise et le
    /// parallélisme ; les CARTES — import en tuiles, planisphère, zones, repères, échelle
    /// et superficies, monde lié, jour et nuit, globe — ; l'Analyseur de .plot. Avec
    /// MARABOOK_PROBE_SHOTS et la carte d'essai de Rémi dans Téléchargements, elle
    /// l'importe pour de vrai, la mesure et la photographie.</summary>
    public static partial class Probes
    {
        private static async Task ProbeMaps(MainWindow shell)
        {
            var added = new List<BinderItem>();
            World earth = null;
            var project = shell.Project;
            try
            {
                Func<string, string> plain = delegate(string text) { return (text ?? "").Replace(' ', ' ').Replace(' ', ' '); };

                // ================================================== le fragment d'une frise
                var root = project.Category(Project.KeyTimelines);
                var timeline = Timelines.FindByTitle(project, TimelineSamples.TimelineTitle);
                shell.Binder.SelectItem(timeline.Id);
                await Settle();
                var view = shell.TimelineForProbe;
                var system = view.System;
                view.SetCalendarMode(false);
                view.SelectTabForProbe(-1);
                view.FitAll();
                await Settle();
                var wholeSpan = view.ViewEndForProbe - view.ViewStartForProbe;
                TimePoint from, to;
                var example = system.Example();
                TimePoint sample;
                system.TryParse(example, out sample);
                from = new TimePoint(sample.Year - 20);
                to = new TimePoint(sample.Year + 19);
                Check(!timeline.Timeline.HasFragment && !view.BoundedForProbe, "une frise de l'exemple couvre toute la durée de son système");
                view.SetExtent(from, to);
                await Settle();
                double min, max;
                Timelines.FragmentRange(system, timeline.Timeline, out min, out max);
                Check(timeline.Timeline.HasFragment && view.BoundedForProbe && Math.Abs(max - min - 40) < 1e-6, "« Étendue » : un fragment de quarante ans (" + Timelines.FragmentLabel(system, timeline.Timeline) + ")");
                Check(view.ViewStartForProbe <= min && view.ViewStartForProbe > min - 5 && view.ViewEndForProbe >= max && view.ViewEndForProbe < max + 5 && view.ViewEndForProbe - view.ViewStartForProbe < wholeSpan / 4,
                    "la frise ne montre plus que le fragment, d'un bord à l'autre (" + Math.Round(view.ViewStartForProbe) + " → " + Math.Round(view.ViewEndForProbe) + ")");
                view.ZoomForProbe(0.2);
                await Settle();
                Check(view.ViewStartForProbe > min - 5 && view.ViewEndForProbe < max + 5, "…et le dézoom ne sort pas de ses bornes");
                Check(view.GetVisualDescendants().OfType<Button>().Any(delegate(Button b) { return Equals(b.Tag, "timeline-extent") && Equals(b.Content, "Fragment"); }), "le bouton d'étendue dit « Fragment »");
                Shot(view, "frises-13-fragment.png");
                shell.Binder.SelectItem(root.Id);
                await Settle();
                Check(shell.TimelineHubForProbe.GetVisualDescendants().OfType<TextBlock>().Any(delegate(TextBlock b) { return (b.Text ?? "").Contains("fragment : de "); }), "la page Chronologie dit le fragment de la frise");
                shell.Binder.SelectItem(timeline.Id);
                await Settle();
                Check(shell.UndoForProbe() && !timeline.Timeline.HasFragment, "Ctrl+Z rend la frise à toute sa durée");
                await Settle();
                Check(!view.BoundedForProbe, "…et la vue n'est plus tenue");

                // La fenêtre « Nouvelle frise » : l'étendue se choisit à la création.
                {
                    var task = NewTimelineDialog.Ask(shell, project, "Essai");
                    await Settle();
                    var dialog = OpenWindow<NewTimelineDialog>();
                    Check(dialog != null && dialog.ExtentForProbe != null && !dialog.ExtentForProbe.IsFragment, "« Nouvelle frise » demande l'étendue ; par défaut, toute la durée du système");
                    if (dialog != null)
                    {
                        dialog.ExtentForProbe.SetForProbe("pas une date", system.Format(to));
                        await Settle();
                        dialog.AcceptForProbe("Essai");
                        await Settle();
                        Check(OpenWindow<NewTimelineDialog>() != null && plain(dialog.ExtentForProbe.EchoForProbe).Contains("ne se lit pas"), "un fragment illisible : la fenêtre reste, la raison est dite");
                        dialog.ExtentForProbe.SetForProbe(system.Format(from), system.Format(to));
                        await Settle();
                        Check(plain(dialog.ExtentForProbe.EchoForProbe).Contains("40 ans"), "deux dates lues : la durée du fragment est redite (" + dialog.ExtentForProbe.EchoForProbe + ")");
                        Shot(dialog, "frises-14-nouvelle-frise-fragment.png");
                        dialog.AcceptForProbe("Essai");
                        await Settle();
                        var choice = await task;
                        Check(choice != null && choice.From != null && choice.To != null && choice.From.Year == from.Year, "validé : la frise naîtra en fragment");
                    }
                }

                // ================================================== le parallélisme
                var other = new BinderItem
                {
                    Kind = ItemKind.Timeline, Title = "Guerre des Brumes", Parent = root,
                    Timeline = new TimelineInfo { SystemId = system.Id, Types = TimelineInfo.DefaultTypes(), From = from.Clone(), To = to.Clone() }
                };
                other.Timeline.Events.Add(new TimelineEvent { Title = "Siège de Port-Brume", TypeId = "type-2", Start = new TimePoint(sample.Year), End = new TimePoint(sample.Year + 6) });
                other.Timeline.Events.Add(new TimelineEvent { Title = "Traité des Marées", TypeId = "type-1", Start = new TimePoint(sample.Year + 12) });
                root.Children.Add(other);
                added.Add(other);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(root.Id);
                await Settle();
                Check(shell.TimelineHubForProbe.ParallelEnabledForProbe && ParallelDialog.Companions(project, timeline).Count == 1, "deux frises sur le même système : « Parallélisme » s'allume");
                shell.OpenParallel(timeline, other);
                await Settle();
                await Settle();
                var peer = view.PeerForProbe;
                Check(peer != null && view.ParallelItem == other && view.IsVisible && view.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, "timeline-peer") && c.IsVisible && c.Bounds.Height > 80; }),
                    "« Parallélisme » : la seconde frise se pose sous la première");
                if (peer != null)
                {
                    view.FitAll();
                    await Settle();
                    Check(Math.Abs(peer.ScaleForProbe - view.ScaleForProbe) < 1e-9 && Math.Abs(peer.ViewStartForProbe - view.ViewStartForProbe) < 1e-9 && Math.Abs(peer.ViewEndForProbe - view.ViewEndForProbe) < 1e-6,
                        "aimantées : même échelle, même bord gauche, même largeur (" + view.ScaleForProbe.ToString("0.###") + " px par an)");
                    Check(!view.BoundedForProbe && !peer.BoundedForProbe && peer.PlacedCountForProbe == 2, "l'axe commun couvre les deux étendues ; la frise du bas montre ses deux événements");
                    view.ZoomForProbe(3);
                    await Settle();
                    Check(Math.Abs(peer.ScaleForProbe - view.ScaleForProbe) < 1e-9 && Math.Abs(peer.ViewStartForProbe - view.ViewStartForProbe) < 1e-9, "le zoom de l'une est celui de l'autre");
                    peer.ZoomForProbe(0.5);
                    await Settle();
                    Check(Math.Abs(peer.ScaleForProbe - view.ScaleForProbe) < 1e-9 && Math.Abs(peer.ViewStartForProbe - view.ViewStartForProbe) < 1e-9, "…dans les deux sens");
                    var year = system.AxisMid(new TimePoint(sample.Year + 3));
                    view.GoTo(new TimePoint(sample.Year + 3));
                    await Settle();
                    view.PointAtForProbe(year);
                    await Settle();
                    Check(peer.MirrorShownForProbe && !view.MirrorShownForProbe && peer.PointedLabelForProbe == view.PointedLabelForProbe && !string.IsNullOrEmpty(peer.PointedLabelForProbe),
                        "le repère du survol paraît sur les deux frises, à la même date (" + peer.PointedLabelForProbe + ")");
                    Shot(view, "frises-15-parallelisme.png");
                    view.PointAtForProbe(null);
                    await Settle();
                    Check(!peer.MirrorShownForProbe, "le curseur parti, le repère s'efface des deux");
                    Check(!view.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, "timeline-minimap") && c.IsVisible && c.Bounds.Height > 0 && c.FindAncestorOfType<TimelineView>() == peer; }),
                        "la frise du bas n'a ni en-tête ni minimap à elle");
                    view.SetCalendarMode(true);
                    await Settle();
                    Check(view.PeerForProbe == null && view.CalendarMode, "passer au calendrier quitte le parallélisme");
                    view.SetCalendarMode(false);
                    await Settle();
                    view.SetParallel(other);
                    await Settle();
                    view.SetParallel(null);
                    await Settle();
                    Check(view.PeerForProbe == null && view.ParallelItem == null, "« Quitter le parallélisme » rend la frise seule");
                }

                // ================================================== les cartes
                var mapsRoot = project.Category(Project.KeyMaps);
                shell.Binder.SelectItem(mapsRoot.Id);
                await Settle();
                var mapsHub = shell.MapsHubForProbe;
                Check(mapsHub.IsVisible && shell.VisibleView == "maps" && mapsHub.CardsForProbe == 0 && mapsHub.EmptyShownForProbe, "la racine « Cartes » ouvre sa page ; sans carte, elle dit par où commencer");
                Shot(mapsHub, "cartes-1-page-vide.png");

                var clock = Stopwatch.StartNew();
                var item = shell.ImportMapForProbe("Planisphère d'essai", MapImport.Synthetic(2400, 1200));
                clock.Stop();
                await Settle();
                await Settle();
                added.Add(item);
                var mapView = shell.MapForProbe;
                var canvas = mapView.CanvasForProbe;
                var map = item.Map;
                Check(item != null && item.Kind == ItemKind.Map && item.Parent == mapsRoot && shell.VisibleView == "map" && mapView.ShowsItem(item), "une image importée devient une carte, ouverte aussitôt");
                Check(map.Width == 2400 && map.Height == 1200 && map.Levels == 4 && map.Tiles.Count == Maps.TileCount(2400, 1200, 512) && map.Extension == ".jpg" && map.SourceBytes > 0,
                    "découpée à l'import : 2400 × 1200 px, 4 niveaux, " + map.Tiles.Count + " tuiles (" + clock.ElapsedMilliseconds + " ms)");
                Check(ItemIcons.DefaultSvg(item) == "pile-cartes" && shell.Binder.TopRowsForProbe().Contains("Cartes"), "son icône est le monde");
                Check(plain(mapView.StatusLabel).Contains("2400 × 1200 px") && plain(mapView.StatusLabel).Contains("4 niveaux de zoom"), "la barre d'état dit ses dimensions, ses niveaux, son poids (" + mapView.StatusLabel + ")");
                // Lot 12 : les panneaux de la carte vivent au rail — on les cherche là où ils sont.
                Func<List<Control>> parts = delegate
                {
                    var found = new List<Control>();
                    foreach (var panel in new[] { mapView.GeneralPanel, mapView.SkyPanel, mapView.ShapesPanel, mapView.NavigationPanel }) found.AddRange(panel.GetLogicalDescendants().OfType<Control>());
                    return found;
                };
                Func<string, bool> tagged = delegate(string tag) { return parts().Any(delegate(Control c) { return Equals(c.Tag, tag); }); };
                var side = mapView.SideTextsForProbe();
                Check(side.Contains("Poids dans le .plot") && side.Contains(Maps.SizeLabel(Maps.Bytes(map))) && side.Exists(delegate(string s) { return s.StartsWith("Fichier d'origine"); }),
                    "le panneau dit le poids de la carte dans le .plot (" + Maps.SizeLabel(Maps.Bytes(map)) + ") et celui du fichier d'origine");
                Check(Math.Abs(canvas.ZoomRatio - 1) < 1e-6 && canvas.Scale < 1 && canvas.LevelForProbe < map.Levels - 1, "« Tout voir » : la carte entière, tirée d'un niveau réduit (niveau " + (canvas.LevelForProbe + 1) + " sur " + map.Levels + ")");
                canvas.ZoomBy(1 / canvas.Scale, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
                await Settle();
                Check(Math.Abs(canvas.Scale - 1) < 1e-6 && canvas.LevelForProbe == map.Levels - 1 && plain(mapView.ZoomLabelForProbe).Contains("niveau 4 sur 4"), "au pixel pour pixel : le dernier niveau, le plein format (" + mapView.ZoomLabelForProbe + ")");
                for (var wait = 0; wait < 20 && MapTileCache.For(map).Pending; wait++) await Task.Delay(50);
                await Settle();
                Check(MapTileCache.For(map).ReadyCount >= 3 && MapTileCache.For(map).ReadyCount < map.Tiles.Count, "seules les tuiles visibles sont décodées (" + MapTileCache.For(map).ReadyCount + " sur " + map.Tiles.Count + ")");
                canvas.FitAll();
                await Settle();

                // — Zones, repères.
                Check(mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Aucune. Outil « Zone »"); }) && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Aucun. Outil « Repère »"); }),
                    "une carte neuve : ni zone ni repère, et le panneau dit comment en tracer");

                // ---- Lot 12 : le volet a quitté l'écran de la carte pour le rail.
                Check(!mapView.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, "map-weight") || Equals(c.Tag, "map-world") || Equals(c.Tag, "map-night"); }),
                    "l'écran de la carte n'a plus de volet : la toile prend toute la place");
                Check(shell.RailHasTabForProbe(RightPanel.MapSky) && shell.RailHasTabForProbe(RightPanel.MapShapes) && shell.RailHasTabForProbe(RightPanel.Inspector)
                    && shell.RailTabRankForProbe(RightPanel.MapShapes) == shell.RailTabRankForProbe(RightPanel.MapSky) + 1 && shell.RailTabRankForProbe(RightPanel.MapSky) > shell.RailTabRankForProbe(RightPanel.Notebook)
                    && shell.RailMapSeparatorForProbe && Icons.Has("carte-ciel") && Icons.Has("carte-reperes"),
                    "le rail gagne deux onglets propres à la carte, tout en bas, sous un filet : « Jour, nuit et saisons » (soleil) et « Zones et repères » (balise)");
                shell.SetRightPanelForProbe(RightPanel.Inspector);
                await Settle();
                Check(AppSettings.RightPanel == RightPanel.Inspector && shell.MapSectionShownForProbe && mapView.GeneralPanel.IsEffectivelyVisible && tagged("map-weight") && tagged("map-world") && tagged("map-whole") && tagged("map-replace")
                    && shell.InspectorKindForProbe == "Carte", "l'onglet Général porte la carte : son image et son poids, son monde, « Carte du monde entier », son échelle");
                Check(side.Contains("CARTE") && side.Contains("MONDE") && side.Contains("ÉCHELLE ET SUPERFICIES") && !shell.MapSkyHostForProbe.IsEffectivelyVisible && !shell.MapShapesHostForProbe.IsEffectivelyVisible, "…et rien que cela : le ciel et les zones ont leurs onglets");
                Shot(mapView.GeneralPanel, "cartes-10-rail-general.png");
                shell.SetRightPanelForProbe(RightPanel.MapShapes);
                await Settle();
                Check(AppSettings.RightPanel == RightPanel.MapShapes && shell.MapShapesHostForProbe.IsEffectivelyVisible && !shell.MapSectionShownForProbe == false
                    && tagged("map-filter-zones") && tagged("map-filter-markers"), "l'onglet « Zones et repères » : les filtres, puis les listes");
                Shot(shell.MapShapesHostForProbe, "cartes-14-rail-zones.png");
                shell.SetRightPanelForProbe(RightPanel.MapSky);
                await Settle();
                Check(AppSettings.RightPanel == RightPanel.MapSky && shell.MapSkyHostForProbe.IsEffectivelyVisible && mapView.SkyPanel.IsEffectivelyVisible
                    && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Le jour, la nuit et les saisons demandent un monde"); }), "l'onglet « Jour, nuit et saisons » : sans monde, il dit ce qu'il lui faut");
                Check(!mapView.GlobeButtonShownForProbe && !mapView.GetVisualDescendants().OfType<ToggleButton>().Any(delegate(ToggleButton b) { return Equals(b.Tag, "map-view-globe") && b.IsEffectivelyVisible; }),
                    "une carte qui n'est pas celle du monde entier n'a pas de bouton « Globe »");
                shell.SetRightPanelForProbe(RightPanel.MapShapes);
                await Settle();

                // ---- Espace : la main fermée, quel que soit l'outil.
                Check(!mapView.SpaceKeyForProbe(true, false) && !canvas.SpaceHeld, "Espace hors de la carte (le pointeur ailleurs) ne fait rien");
                mapView.ChooseTool(MapCanvas.Tool.Zone);
                var origin = canvas.ViewOriginForProbe;
                Check(mapView.SpaceKeyForProbe(true, true) && canvas.SpaceHeld && canvas.GrabCursorShownForProbe && canvas.CurrentTool == MapCanvas.Tool.Zone, "Espace tenu sur la carte, outil « Zone » en main : le curseur devient la main fermée");
                canvas.GrabForProbe(new Point(300, 200), new Point(240, 170));
                await Settle();
                Check(canvas.DraftCount == 0 && Math.Abs(canvas.ViewOriginForProbe.X - (origin.X + 60 / canvas.Scale)) < 1e-6 && Math.Abs(canvas.ViewOriginForProbe.Y - (origin.Y + 30 / canvas.Scale)) < 1e-6,
                    "…glisser promène la carte, sans poser un seul sommet");
                Check(mapView.SpaceKeyForProbe(false, true) && !canvas.SpaceHeld && !canvas.GrabCursorShownForProbe && canvas.CurrentTool == MapCanvas.Tool.Zone, "Espace relâché : l'outil « Zone » reprend la main");
                mapView.ChooseTool(MapCanvas.Tool.Pan);
                canvas.FitAll();
                await Settle();
                mapView.ChooseTool(MapCanvas.Tool.Zone);
                Check(canvas.CurrentTool == MapCanvas.Tool.Zone && mapView.HintForProbe.Contains("Un clic par sommet"), "l'outil « Zone » dit son mode d'emploi");
                var sheet = project.Category(Project.KeySheets).Children.Find(delegate(BinderItem child) { return child.Kind == ItemKind.Sheet; })
                    ?? project.AllItems().First(delegate(BinderItem child) { return child.Kind == ItemKind.Sheet; });
                var zone = mapView.AddZone(new List<Pos> { new Pos(600, 300), new Pos(1000, 300), new Pos(1000, 700), new Pos(600, 700) },
                    new MapShapeChoice { Name = "Empire d'Aube", Color = "#3E9C7A", LinkId = sheet.Id, Level = Maps.LevelAlways });
                await Settle();
                Check(item.Map.Zones.Count == 1 && mapView.SelectedZone != null && mapView.SelectedZone.Name == "Empire d'Aube" && canvas.CurrentTool == MapCanvas.Tool.Pan, "une zone tracée : choisie, et l'outil revient à « Déplacer »");
                var marker = mapView.AddMarker(new Pos(1700, 760), new MapShapeChoice { Name = "Port-Brume", Color = "#C0504D", Level = Maps.LevelNear });
                await Settle();
                Check(item.Map.Markers.Count == 1 && mapView.SelectedMarker != null && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-shape-row"); }) == 2,
                    "un repère posé ; le panneau liste la zone et le repère");

                // ---- Les filtres (lot 12) : montrer ou masquer les zones, les repères.
                var zoneAt = canvas.ScreenOfForProbe(800, 500);
                Check(mapView.ShowZones && mapView.ShowMarkers && canvas.ZoneHitForProbe(zoneAt) != null, "tout est montré d'office ; la zone se clique");
                mapView.Select(mapView.Map.Zones[0], null);
                mapView.SetFilters(false, true);
                await Settle();
                Check(!mapView.ShowZones && mapView.SelectedZone == null && canvas.ZoneHitForProbe(zoneAt) == null && mapView.SideTextsForProbe().Contains("Les zones sont masquées.")
                    && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-shape-row"); }) == 2, "« Zones » décoché : elles ne se voient plus, ne se cliquent plus, ne restent pas choisies — la liste, elle, demeure");
                Shot(mapView, "cartes-11-zones-masquees.png");
                mapView.SetFilters(false, false);
                await Settle();
                Check(!mapView.ShowMarkers && mapView.SelectedMarker == null && mapView.SideTextsForProbe().Contains("Zones et repères masqués : la carte seule."), "les deux décochés : la carte seule");
                mapView.SetFilters(true, true);
                await Settle();
                Check(mapView.ShowZones && mapView.ShowMarkers && canvas.ZoneHitForProbe(zoneAt) != null && item.Map.Zones.Count == 1 && item.Map.Markers.Count == 1, "recochés : tout revient, rien n'a été touché dans la carte");
                Check(!Maps.Visible(marker.Level, canvas.ZoomRatio) && Maps.Visible(zone.Level, canvas.ZoomRatio), "un repère « de près seulement » se cache dans la vue d'ensemble (sauf choisi)");
                mapView.Select(mapView.Map.Zones[0], null);
                await Settle();
                side = mapView.SideTextsForProbe();
                Check(side.Contains("ZONE CHOISIE") && side.Contains("Fiche : " + sheet.Title) && tagged("map-open-sheet"),
                    "la zone choisie dit sa fiche liée, avec « Ouvrir la fiche »");
                mapView.OpenOrEdit(mapView.SelectedZone, null);
                await Settle();
                Check(shell.CurrentForProbe == sheet && shell.VisibleView == "sheet", "un double-clic sur une zone liée ouvre sa fiche");
                shell.Binder.SelectItem(item.Id);
                await Settle();

                // — L'échelle et les superficies (carte plane).
                Check(!Maps.CanMeasure(item.Map, null) && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Sans échelle"); }), "sans échelle, la carte ne mesure rien — et le dit");
                mapView.ChooseTool(MapCanvas.Tool.Measure);
                Check(canvas.CurrentTool == MapCanvas.Tool.Pan && mapView.HintForProbe.Contains("posez d'abord une échelle"), "« Mesurer » sans échelle : refusé, avec la marche à suivre");
                mapView.SetScale(new Pos(600, 300), new Pos(1000, 300), 800, "16 jours à cheval");
                await Settle();
                mapView.Select(mapView.Map.Zones[0], null);
                await Settle();
                side = mapView.SideTextsForProbe().ConvertAll(delegate(string s) { return plain(s); });
                Check(item.Map.HasScale && Math.Abs(Maps.KmPerPixel(item.Map) - 2) < 1e-9 && side.Contains("16 jours à cheval (800 km)") && side.Contains("2,0 km") && side.Contains("4 800 km × 2 400 km"),
                    "l'échelle posée par un temps de trajet : 2 km par pixel, la carte couvre 4 800 × 2 400 km");
                Check(side.Contains(plain(Maps.AreaLabel(4800.0 * 2400))) && side.Contains(plain(Maps.AreaLabel(640000))), "la superficie de la carte (11,52 millions de km²) et celle de la zone (640 000 km²)");
                mapView.ChooseTool(MapCanvas.Tool.Measure);
                mapView.Measured(new Pos(0, 0), new Pos(300, 400));
                Check(canvas.CurrentTool == MapCanvas.Tool.Measure && plain(mapView.HintForProbe).Contains("1 000 km") && mapView.HintForProbe.Contains("à pied"), "« Mesurer » : la distance, dite aussi en jours de voyage (" + mapView.HintForProbe + ")");
                mapView.ChooseTool(MapCanvas.Tool.Pan);
                Shot(mapView, "cartes-2-planisphere-zones.png");

                // — Le monde : part de la surface, puis le planisphère entier.
                earth = World.Earth();
                earth.Name = "Terre d'essai";
                project.Worlds.Add(earth);
                mapView.SetWorldForProbe(earth.Id, false);
                await Settle();
                side = mapView.SideTextsForProbe().ConvertAll(delegate(string s) { return plain(s); });
                Check(mapView.World == earth && side.Exists(delegate(string s) { return s.StartsWith("Soit 2,3 % de la surface de « Terre d'essai »"); }), "liée à un monde, la carte dit la part de sa surface qu'elle couvre");
                Check(!mapView.NightPossible && !mapView.SeasonHeatPossible && side.Exists(delegate(string s) { return s.StartsWith("Le jour, la nuit et les saisons se dessinent sur la carte du monde entier"); }), "le jour, la nuit et les saisons demandent la carte du monde entier");
                mapView.SetWorldForProbe(earth.Id, true);
                await Settle();
                side = mapView.SideTextsForProbe().ConvertAll(delegate(string s) { return plain(s); });
                var implied = Maps.ImpliedCircumference(item.Map);
                Check(mapView.GlobeButtonShownForProbe, "« Carte du monde entier » cochée : le bouton « Globe » paraît");
                Check(implied > 4800 && implied < 9000 && side.Contains("Tour du monde") && side.Contains(plain(Maps.KmLabel(implied))) && tagged("map-give-size"),
                    "planisphère entier + échelle : la carte calcule le tour du monde (" + Maps.KmLabel(implied) + ", au plus court sur la sphère) et propose de le donner au monde");
                shell.GiveWorldSizeForProbe(earth, implied);
                await Settle();
                Check(earth.Circumference == Math.Round(implied) && !tagged("map-give-size"), "« Donner cette taille au monde » l'écrit dans l'Astrolabe ; d'accord, le bouton disparaît");
                Check(shell.UndoForProbe() && Math.Abs(earth.Circumference - 40075) < 1 || project.Worlds.Exists(delegate(World w) { return w.Id == earth.Id && Math.Abs(w.Circumference - 40075) < 1; }), "…et Ctrl+Z rend au monde sa taille d'avant");
                earth = project.Worlds.Find(delegate(World w) { return w.Id == earth.Id; });
                await Settle();

                // — Le jour et la nuit.
                Check(mapView.NightPossible && !mapView.NightShown && tagged("map-night"), "un planisphère entier lié à un monde offre le jour et la nuit");

                // ---- Les points de chaleur des saisons (lot 12).
                shell.SetRightPanelForProbe(RightPanel.MapSky);
                await Settle();
                // Lot 13 : le voile des saisons est ÉTEINT d'office ; on l'allume dans l'onglet.
                Check(mapView.SeasonHeatPossible && !mapView.SeasonHeatShown && tagged("map-season-heat") && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-season-row"); }) == 0,
                    "les saisons ne se montrent pas d'office sur la carte : la case est là, décochée");
                mapView.SetSeasonHeatForProbe(true);
                await Settle();
                Check(mapView.SeasonHeatShown && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-season-row"); }) == earth.Seasons.Count,
                    "cochée : le voile des saisons paraît, et la légende les nomme toutes (" + earth.Seasons.Count + ")");
                Color tintNorth, tintSouth;
                Check(canvas.SeasonTint(false, out tintNorth) && canvas.SeasonTint(true, out tintSouth) && tintNorth != tintSouth, "le nord et le sud ont chacun la teinte de leur saison");
                mapView.SetNightForProbe(false, 172, 0.5);
                await Settle();
                Color tintA, tintB, tintC;
                canvas.SeasonTint(false, out tintA);
                mapView.SetNightForProbe(false, 173, 0.5);
                canvas.SeasonTint(false, out tintB);
                mapView.SetNightForProbe(false, 200, 0.5);
                canvas.SeasonTint(false, out tintC);
                Check(Math.Abs(tintA.R - tintB.R) + Math.Abs(tintA.G - tintB.G) + Math.Abs(tintA.B - tintB.B) <= 12 && tintA != tintC,
                    "d'un jour au suivant, au passage d'une saison, la teinte glisse sans saute (" + tintA + " → " + tintB + ") ; un mois plus tard elle a changé (" + tintC + ")");
                Check(mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("au nord, à ") || s.StartsWith("au sud, à "); }), "la légende dit la saison vers laquelle on glisse, et de combien");
                mapView.SetNightForProbe(false, 1, 0.5);
                await Settle();
                Check(mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Entre les tropiques (23,4"); }), "…et dit où elles s'effacent : entre les tropiques");
                Shot(mapView, "cartes-12-saisons.png");
                Shot(shell.MapSkyHostForProbe, "cartes-15-rail-ciel.png");
                mapView.SetSeasonHeatForProbe(false);
                await Settle();
                Check(!mapView.SeasonHeatShown && mapView.SeasonHeatPossible && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-season-row"); }) == 0, "« Montrer les saisons sur la carte » décoché : plus de points, plus de légende");
                mapView.SetSeasonHeatForProbe(true);
                await Settle();

                // ---- La construction du planisphère (lot 12) : étirée, ou proportions gardées et pôles placés.
                shell.SetRightPanelForProbe(RightPanel.Inspector);
                await Settle();
                Check(tagged("map-layout-edit") && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Construction : étirée"); }), "le Général dit la construction du planisphère (étirée, d'office) et offre de la changer");
                var layoutDialog = MapLayoutDialog.ForProbe(shell, item.Map);
                layoutDialog.SetForProbe(true, "80", "20");
                Check(layoutDialog.ReadForProbe() == null && layoutDialog.EchoForProbe.Contains("plus bas que le pôle nord"), "sa fenêtre refuse un pôle sud au-dessus du pôle nord, et dit pourquoi");
                layoutDialog.SetForProbe(true, "-25", "125");
                var layoutChoice = layoutDialog.ReadForProbe();
                Check(layoutChoice != null && layoutChoice.Fitted && Math.Abs(layoutChoice.PoleNorth + 0.25) < 1e-9 && Math.Abs(layoutChoice.PoleSouth - 1.25) < 1e-9 && plain(layoutDialog.EchoForProbe).Contains("60° nord"),
                    "des pôles hors de l'image : elle dit jusqu'où l'image monte (" + layoutDialog.EchoForProbe + ")");
                layoutDialog.AroundForProbe();
                layoutChoice = layoutDialog.ReadForProbe();
                Check(layoutChoice != null && Math.Abs(layoutChoice.PoleNorth) < 1e-6 && Math.Abs(layoutChoice.PoleSouth - 1) < 1e-6 && layoutDialog.EchoForProbe.Contains("un tour complet"),
                    "« Un tour complet » sur une image deux fois plus large que haute : les pôles tombent aux bords");
                mapView.SetLayout(true, 0.1, 0.9);
                await Settle();
                Check(Maps.Fitted(item.Map) && Math.Abs(Maps.LongitudeSpan(item.Map) - 450) < 1e-6 && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Construction : proportions gardées") && s.Contains("se recouvre"); }),
                    "proportions gardées, pôles à 10 % et 90 % : le Général le dit, et prévient quand l'image fait plus d'un tour");
                Shot(mapView, "cartes-13-poles.png");
                Check(shell.UndoForProbe() && !Maps.Fitted(item.Map), "…et Ctrl+Z rend la construction d'avant");
                await Settle();
                mapView.SetNightForProbe(true, 200, 0.5);
                await Settle();
                side = mapView.SideTextsForProbe().ConvertAll(delegate(string s) { return plain(s); });
                Check(mapView.NightShown && mapView.Declination > 0.3 && side.Exists(delegate(string s) { return s.StartsWith("Jour 200 de l'année"); })
                    && side.Exists(delegate(string s) { return s.StartsWith("Été au nord, Hiver au sud."); }), "au jour 200 : l'été au nord, l'hiver au sud, le soleil à l'aplomb du tropique nord");
                Shot(mapView, "cartes-3-jour-nuit.png");

                // — Le globe.
                mapView.SetViewForProbe(Maps.ViewGlobe);
                await Settle();
                Check(item.Map.View == Maps.ViewGlobe && !mapView.ToolEnabledForProbe(MapCanvas.Tool.Zone) && mapView.ToolEnabledForProbe(MapCanvas.Tool.Pan) && mapView.HintForProbe.Contains("tourner le globe"),
                    "le rendu « Globe » : les outils de tracé se grisent, le mode d'emploi change");
                // Lot 16 : la bascule « Vue planétaire » à gauche du zoom, la barre des outils qui s'efface.
                var globeToggle = mapView.GlobeToggleForProbe;
                Check(globeToggle.IsEffectivelyVisible && globeToggle.IsChecked == true && (ToolTip.GetTip(globeToggle) as string) == "Vue planétaire" && !(globeToggle.Content is TextBlock)
                    && !mapView.GetVisualDescendants().OfType<ToggleButton>().Any(delegate(ToggleButton b) { return Equals(b.Tag, "map-view-flat"); }),
                    "la vue planétaire : une bascule à icône, « Vue planétaire » en infobulle — plus de « Planisphère / Globe » dans la barre");
                var zoomLabel = mapView.GetVisualDescendants().OfType<TextBlock>().First(delegate(TextBlock t) { return Equals(t.Tag, "map-zoom"); });
                Check(globeToggle.Parent == zoomLabel.Parent && ((Panel)zoomLabel.Parent).Children.IndexOf(globeToggle) == ((Panel)zoomLabel.Parent).Children.IndexOf(zoomLabel) - 1, "…placée juste à gauche de l'indicateur de zoom");
                Check(!mapView.ToolBarShownForProbe, "en vue planétaire, la barre des outils disparaît");
                canvas.PaintGlobeForProbe();
                var globeSide = (int)Math.Max(64, Math.Min(640, Math.Min(canvas.Bounds.Width, canvas.Bounds.Height)));
                var centre = canvas.GlobePixelForProbe(globeSide / 2, globeSide / 2);
                Check(canvas.GlobePaintedForProbe && ((centre >> 24) & 0xFF) == 255 && canvas.GlobePixelForProbe(1, 1) == 0, "l'image est enroulée sur la sphère : le centre du disque est peint, le coin de la toile est vide");
                canvas.RotateGlobeForProbe(Math.PI, 0.2);
                canvas.PaintGlobeForProbe();
                var nightSide = canvas.GlobePixelForProbe(globeSide / 2, globeSide / 2);
                canvas.RotateGlobeForProbe(0, 0.2);
                canvas.PaintGlobeForProbe();
                var daySide = canvas.GlobePixelForProbe(globeSide / 2, globeSide / 2);
                Func<int, int> luma = delegate(int pixel) { return (pixel & 0xFF) + ((pixel >> 8) & 0xFF) + ((pixel >> 16) & 0xFF); };
                Check(luma(daySide) > luma(nightSide) * 2, "à midi au centre de la carte, la face opposée du globe est dans la nuit (" + luma(daySide) + " contre " + luma(nightSide) + ")");
                await Settle();
                Shot(mapView, "cartes-4-globe.png");

                // ---- Lot 13 : le ciel derrière le globe — étoiles, soleils et lunes du monde.
                canvas.RotateGlobeForProbe(0, 0.2);
                canvas.SetSkyTimeForProbe(3.2);
                await Settle();
                var starsFront = canvas.StarsDrawnForProbe;
                Point starA, starB;
                var seenA = canvas.SkyPointForProbe(Math.PI, 0, out starA);
                canvas.RotateGlobeForProbe(0.3, 0.2);
                await Settle();
                var seenB = canvas.SkyPointForProbe(Math.PI, 0, out starB);
                Check(starsFront > 80 && canvas.StarsDrawnForProbe > 80 && seenA && seenB && Math.Abs(starA.X - starB.X) > 20,
                    "le globe a un fond d'étoiles (" + starsFront + " à l'écran) qui tourne avec lui (une étoile glisse de " + Math.Round(Math.Abs(starA.X - starB.X)) + " px pour 17° de rotation)");
                Check(!canvas.SkyTimerRunningForProbe && !mapView.AnimateSky, "en sonde, le ciel ne s'anime pas tout seul (dans l'application : scintillement et étoiles filantes, si les animations sont permises)");
                var bodies = mapView.SkyBodies();
                Check(bodies.Count == earth.Suns.Count + earth.Moons.Count && bodies.Exists(delegate(MapCanvas.SkyDot d) { return d.Sun; }) && bodies.Exists(delegate(MapCanvas.SkyDot d) { return !d.Sun; }),
                    "les astres du monde lié sont dans le ciel : " + earth.Suns.Count + " soleil(s), " + earth.Moons.Count + " lune(s)");
                // À midi au centre de la carte, le soleil est derrière nous quand on regarde la face éclairée ; on passe du côté de la nuit pour le voir.
                mapView.SetNightForProbe(true, 200, 0.5);
                canvas.RotateGlobeForProbe(0.2, 0.25);
                await Settle();
                var bodiesDay = canvas.BodiesDrawnForProbe;
                Point sunAt;
                var sunSeenDay = canvas.SkyPointForProbe(bodies[0].Longitude, bodies[0].Latitude, out sunAt);
                canvas.RotateGlobeForProbe(Math.PI + 0.45, 0.1);
                await Settle();
                var sunSeenNight = canvas.SkyPointForProbe(bodies[0].Longitude, bodies[0].Latitude, out sunAt);
                Check(!sunSeenDay && sunSeenNight && canvas.BodiesDrawnForProbe >= 1, "face au jour, le soleil est dans notre dos ; passé du côté de la nuit, il paraît derrière le globe");
                Shot(mapView, "cartes-16-globe-ciel.png");

                // ---- Lot 14 : zoomer sur le globe ne finit plus sur un carré.
                canvas.RotateGlobeForProbe(0, 0.2);
                await Settle();
                canvas.PaintGlobeForProbe();
                var coverBefore = canvas.GlobeCoverForProbe;
                Check(canvas.GlobeAlphaAtForProbe(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2)) == 255 && canvas.GlobeAlphaAtForProbe(new Point(3, 3)) == 0,
                    "sans zoom : le globe au centre, le coin de la toile hors de la sphère");
                canvas.ZoomBy(8, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
                await Settle();
                canvas.PaintGlobeForProbe();
                var corners = new[] { new Point(4, 4), new Point(canvas.Bounds.Width - 5, 4), new Point(4, canvas.Bounds.Height - 5), new Point(canvas.Bounds.Width - 5, canvas.Bounds.Height - 5), new Point(canvas.Bounds.Width / 2, 4), new Point(4, canvas.Bounds.Height / 2) };
                Check(canvas.GlobeCoverForProbe >= Math.Max(canvas.Bounds.Width, canvas.Bounds.Height) && canvas.GlobeCoverForProbe > coverBefore && corners.All(delegate(Point p) { return canvas.GlobeAlphaAtForProbe(p) == 255; }),
                    "zoomé × 8 : l'image du globe couvre toute la toile, coins et bords compris — plus de carré (" + Math.Round(canvas.GlobeCoverForProbe) + " px couverts, " + canvas.GlobeBitmapPixelsForProbe + " px peints)");
                Shot(mapView, "cartes-20-globe-zoom.png");
                canvas.FitAll();
                await Settle();

                // ---- Lot 14 : les satellites tournent autour du monde ; un soleil qui orbite reste attaché au sien.
                mapView.SetNightForProbe(true, 200, 0.5);
                await Settle();
                var skyNow = mapView.SkyBodies();
                var moonDot = skyNow.Find(delegate(MapCanvas.SkyDot d) { return d.Satellite; });
                Point moonAt;
                bool moonFront;
                var globeCentre = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
                canvas.SatellitePoint(moonDot, globeCentre, 200, out moonAt, out moonFront);
                var moonReach = Math.Sqrt(Math.Pow(moonAt.X - globeCentre.X, 2) + Math.Pow(moonAt.Y - globeCentre.Y, 2));
                Check(moonDot != null && !skyNow.Exists(delegate(MapCanvas.SkyDot d) { return d.Sun && d.Satellite; }) && Math.Abs(moonDot.Orbit - 1.9) < 1e-9 && moonReach <= 200 * 1.9 + 1e-6,
                    "un satellite tourne autour du globe, à portée de vue : à la distance de la Terre à la Lune, son orbite fait 1,9 rayon");
                Check(Math.Abs(MapView.MoonOrbit(4 * 384400) - 2.5) < 1e-9 && MapView.MoonOrbit(100) >= 1.25 && MapView.MoonOrbit(100) < 1.35 && MapView.MoonOrbit(1e9) == 4.2, "plus loin reste plus loin (4 fois la distance : 2,5 rayons), dans des bornes qui tiennent à l'écran");
                // Un second soleil, en orbite RAPPROCHÉE autour du premier : il ne le quitte pas.
                var companion = new Sun { Name = "Elh", Period = 80, HighAt = 3, OrbitId = earth.Suns[0].Id };
                earth.Suns.Add(companion);
                Astro.SetInner(earth, companion, true);
                var widest = 0.0;
                for (var probeDay = 1; probeDay <= 80; probeDay += 4)
                {
                    mapView.SetNightForProbe(true, probeDay, 0.5);
                    var dots = mapView.SkyBodies();
                    var main = dots.Find(delegate(MapCanvas.SkyDot d) { return d.Name == earth.Suns[0].Name && d.Sun; });
                    var elh = dots.Find(delegate(MapCanvas.SkyDot d) { return d.Name == "Elh"; });
                    widest = Math.Max(widest, Math.Abs(elh.Longitude - main.Longitude) + Math.Abs(elh.Latitude - main.Latitude));
                    if (elh.Satellite) widest = 99;
                }
                Check(widest > 0.1 && widest < 0.4, "un soleil en orbite rapprochée autour d'un autre reste auprès de lui toute sa révolution (écart maximal " + widest.ToString("0.00") + " rad) : il ne tourne pas autour du monde");
                Astro.SetInner(earth, companion, false);
                mapView.SetNightForProbe(true, 3, 0.5);
                var farDots = mapView.SkyBodies();
                var farMain = farDots.Find(delegate(MapCanvas.SkyDot d) { return d.Name == earth.Suns[0].Name && d.Sun; });
                var farElh = farDots.Find(delegate(MapCanvas.SkyDot d) { return d.Name == "Elh"; });
                var apart = Math.Abs(Math.IEEERemainder(farElh.Longitude - farMain.Longitude, 2 * Math.PI));
                Check(Math.Abs(apart - Math.PI) < 0.2, "en orbite lointaine, le jour où il culmine à minuit, il est à l'opposé du premier soleil");
                earth.Suns.Remove(companion);
                earth.Order.Remove(companion.Id);

                // ---- Lot 14 : personnaliser l'astre.
                var lookDialog = BodyLookDialog.ForProbe(shell, BodyLookDialog.MoonKind, "Lune", earth.Moons[0].Look.Clone());
                Check(lookDialog.Has("look-distance") && lookDialog.Has("look-tilt") && lookDialog.Has("look-rings") && lookDialog.Has("look-accents") && plain(lookDialog.DistanceEchoForProbe).Contains("1 × Terre–Lune"),
                    "« Personnaliser » un satellite : accent, distance (d'office : celle de la Terre à la Lune), inclinaison de l'orbite, anneaux");
                lookDialog.SetForProbe("abc", "5", true, "1,5");
                Check(lookDialog.AcceptForProbe() == null && (lookDialog.ProblemForProbe ?? "").Contains("kilomètres"), "une distance illisible est refusée, et dite");
                lookDialog.SetForProbe("768 800", "25", true, "1,5");
                lookDialog.SetColorsForProbe(null, "#E7E3D8");
                var moonLook = lookDialog.AcceptForProbe();
                Check(moonLook != null && moonLook.Distance == 768800 && moonLook.Tilt == 25 && moonLook.Rings && moonLook.RingSize == 1.5 && moonLook.RingColor == "#E7E3D8", "validée : deux fois plus loin, orbite inclinée de 25°, des anneaux");
                var sunSeed = BodyLook.ForSun();
                sunSeed.Diameter = Astro.SunDiameter;
                var sunDialog = BodyLookDialog.ForProbe(shell, BodyLookDialog.SunKind, "Soleil", sunSeed);
                var worldDialog = BodyLookDialog.ForProbe(shell, BodyLookDialog.WorldKind, "Terre d'essai", new BodyLook());
                Check(sunDialog.Has("look-distance") && sunDialog.Has("look-diameter") && sunDialog.Has("look-tilt") && sunDialog.Has("look-accents") && !worldDialog.Has("look-distance") && !worldDialog.Has("look-diameter") && !worldDialog.Has("look-tilt") && worldDialog.Has("look-rings"),
                    "un soleil : accent du halo, diamètre, distance, inclinaison, anneaux ; le monde : accent et anneaux (son axe reste dans « Taille du monde »)");

                // ---- Lot 16 (17/11) : taille et orbite, calculateur orbital, anneaux réglables, aperçu.
                var ringDialog = BodyLookDialog.ForProbe(shell, BodyLookDialog.MoonKind, "Lune", new BodyLook());
                ringDialog.Show(shell);
                await Settle();
                Check(ringDialog.Has("look-orbit-section") && ringDialog.FramedForProbe("look-orbit-section") && ringDialog.FramedForProbe("look-ring-section") && ringDialog.Has("look-diameter") && plain(ringDialog.DiameterEchoForProbe).Contains("1 × notre Lune"),
                    "un satellite : diamètre, distance et inclinaison dans une section encadrée ; les anneaux dans la leur (" + plain(ringDialog.DiameterEchoForProbe) + ")");
                Check(ringDialog.Has("look-calculator") && !ringDialog.CalculatorOpenForProbe && !ringDialog.UseCalculatorShownForProbe && !worldDialog.Has("look-calculator"), "sous elle, le Calculateur orbital, replié ; le monde n'en a pas");
                ringDialog.CalculatorOpenForProbe = true;
                ringDialog.SetQuotientsForProbe("1,2", "2", "");
                await Settle();
                Check(plain(ringDialog.CalculatorResultsForProbe).Contains("4 170 km") && plain(ringDialog.CalculatorResultsForProbe).Contains("768 800 km") && ringDialog.UseCalculatorShownForProbe,
                    "1,2 fois le diamètre de notre Lune : 4 170 km ; deux fois sa distance : 768 800 km (" + plain(ringDialog.CalculatorResultsForProbe) + ")");
                ringDialog.UseCalculatorForProbe();
                Check(plain(ringDialog.FieldsForProbe).StartsWith("4169,76 | 768800 | 0"), "« Reporter ces valeurs » remplit le diamètre et la distance, sans toucher à l'inclinaison laissée vide (" + ringDialog.FieldsForProbe + ")");
                ringDialog.SetQuotientsForProbe("zéro", "", "");
                await Settle();
                Check(!ringDialog.UseCalculatorShownForProbe && ringDialog.ProblemForProbe == null, "un quotient illisible : pas de bouton, et la fenêtre n'en fait pas une erreur");
                Check(!ringDialog.PreviewShownForProbe && ringDialog.RingSizeIsSliderForProbe, "sans anneaux, pas d'aperçu ; l'ampleur est un curseur");
                ringDialog.SetForProbe(null, null, true, "1,5");
                ringDialog.SetRingsForProbe(2, 0.5, 30, true, 2);
                await Settle();
                var previewRenders = ringDialog.PreviewForProbe.RendersForProbe;
                Check(ringDialog.PreviewShownForProbe && ringDialog.GrainRowShownForProbe && previewRenders > 0 && ringDialog.Has("look-ring-gap") && ringDialog.Has("look-ring-thickness") && ringDialog.Has("look-ring-angle") && ringDialog.Has("look-ring-texture") && ringDialog.Has("look-ring-grain"),
                    "des anneaux : la fenêtre s'étend d'un aperçu à droite ; écart, épaisseur, angle, texture et échelle du grain se règlent");
                Shot(ringDialog, "cartes-23-personnaliser-anneaux.png");
                ringDialog.PreviewForProbe.RotateForProbe(1.4, -0.5);
                await Settle();
                Check(ringDialog.PreviewForProbe.RendersForProbe > previewRenders, "l'aperçu tourne autour de l'astre");
                Shot(ringDialog, "cartes-24-personnaliser-apercu-tourne.png");
                ringDialog.SetRingsForProbe(2, 0.5, 30, false, 2);
                await Settle();
                Check(!ringDialog.GrainRowShownForProbe, "texture décochée : plus d'échelle du grain");
                ringDialog.SetRingsForProbe(2, 0.5, 30, true, 2);
                ringDialog.SetDiameterForProbe("gros");
                Check(ringDialog.AcceptForProbe() == null && (ringDialog.ProblemForProbe ?? "").Contains("diamètre"), "un diamètre illisible est refusé, et dit");
                ringDialog.SetDiameterForProbe("4169,76");
                var ringLook = ringDialog.AcceptForProbe();
                Check(ringLook != null && Math.Abs(ringLook.Diameter - 4169.76) < 1e-6 && ringLook.Distance == 768800 && ringLook.RingGap == 2 && ringLook.RingThickness == 0.5 && ringLook.RingAngle == 30 && ringLook.RingTexture && ringLook.RingGrain == 2 && ringLook.RingSize == 1.5,
                    "validée : diamètre, distance, écart, épaisseur, angle et grain des anneaux");
                ringDialog.Close();
                double spanInner, spanOuter, thinInner, thinOuter;
                new BodyLook().RingSpan(out spanInner, out spanOuter);
                ringLook.RingSpan(out thinInner, out thinOuter);
                Check(Math.Abs(spanInner - 1.35) < 1e-9 && Math.Abs(spanOuter - 2.1) < 1e-9 && thinInner > spanInner && thinOuter - thinInner < spanOuter - spanInner, "les anneaux d'office vont de 1,35 à 2,1 rayons ; écartés et amincis, ils commencent plus loin et sont moins larges");
                var faces = RingArt.FacesBuiltForProbe;
                var face = RingArt.FaceOf(ringLook, Color.Parse("#E7E3D8"), 256);
                var again = RingArt.FaceOf(ringLook, Color.Parse("#E7E3D8"), 256);
                Check(face != null && ReferenceEquals(face, again) && RingArt.FacesBuiltForProbe == faces + 1, "le disque des anneaux est peint une fois, puis gardé tant que rien ne change");
                Check(RingArt.Band(-0.1) == 0 && RingArt.Band(1.1) == 0 && RingArt.Band(0.35) > 0.7 && RingArt.Band(0.595) < 0.2 && RingArt.Band(0.75) > 0.4, "ses bandes : le grand anneau, une division sombre, l'anneau moyen");
                Check(Math.Abs(MapView.MoonScale(BodyLook.MoonDiameter) - 1) < 1e-9 && MapView.MoonScale(4169.76) > 1 && MapView.MoonScale(1000) < 1, "sur le globe, un satellite plus large se dessine plus gros");
                var farSun = new Sun { Size = 1 };
                var nearSize = MapView.SunSize(farSun);
                farSun.Look.Distance = BodyLook.SunDistance * 2;
                Check(MapView.SunSize(farSun) < nearSize, "…et un soleil deux fois plus loin, plus petit dans le ciel");
                var lookBefore = new[] { earth.Look, earth.Suns[0].Look, earth.Moons[0].Look };
                earth.Moons[0].Look = moonLook;
                earth.Look = new BodyLook { Accent = "#6FC3C0", Rings = true, RingSize = 1.2 };
                earth.Suns[0].Look = BodyLook.ForSun();
                earth.Suns[0].Look.Accent = "#D9503A";
                earth.Suns[0].Look.Rings = true;
                earth.Suns[0].Look.Tilt = 20;
                mapView.SetNightForProbe(true, 200, 0.5);
                canvas.RotateGlobeForProbe(Math.PI + 0.45, 0.35);
                canvas.SetSkyTimeForProbe(3.2);
                await Settle();
                var dressed = mapView.SkyBodies();
                var dressedMoon = dressed.Find(delegate(MapCanvas.SkyDot d) { return d.Satellite; });
                var dressedSun = dressed.Find(delegate(MapCanvas.SkyDot d) { return d.Sun; });
                Check(Math.Abs(dressedMoon.Orbit - MapView.MoonOrbit(768800)) < 1e-9 && dressedMoon.Orbit > 1.9 && dressedMoon.Rings && dressedSun.Rings && dressedSun.Accent == Color.Parse("#D9503A") && dressedSun.Tilt == 20,
                    "le globe suit : le satellite plus loin et annelé, le soleil à l'accent rouge et aux anneaux penchés");
                Check(canvas.SatellitesDrawnForProbe >= 0, "le monde annelé et son halo se dessinent sans heurt");
                Shot(mapView, "cartes-21-astres-personnalises.png");
                // Lot 16 : de plus loin, les anneaux entiers — puis penchés de 18° sur l'équateur.
                canvas.ZoomBy(0.62, new Point(0, 0));
                await Settle();
                Shot(mapView, "cartes-22-anneaux-entiers.png");
                earth.Look.RingAngle = 18;
                earth.Look.RingGap = 0.6;
                canvas.InvalidateVisual();
                await Settle();
                Shot(mapView, "cartes-22-anneaux-penches.png");
                canvas.ZoomBy(1 / 0.62, new Point(0, 0));
                await Settle();
                earth.Look = lookBefore[0];
                earth.Suns[0].Look = lookBefore[1];
                earth.Moons[0].Look = lookBefore[2];
                canvas.SetSkyTimeForProbe(-1);

                // ---- Lot 14 : « Animer », au globe seulement.
                Check(mapView.AnimateShownForProbe && mapView.AnimationPossible && !mapView.Animating, "au globe, le bouton « Animer » paraît");
                Check(mapView.AnimateFloatsForProbe && mapView.AnimationNoteForProbe == "", "…flottant, en haut à gauche du globe (lot 16) ; au repos, sans légende");
                mapView.SetNightForProbe(false, 100, 0.25);
                mapView.SetAnimation(true);
                await Settle();
                Check(mapView.Animating && mapView.NightShown && !mapView.SkyPanel.IsEnabled && tagged("map-sky-animating") && mapView.HintForProbe.StartsWith("Animation : jour 100"),
                    "« Animer » : la nuit se montre, « Jour, nuit et saisons » se verrouille (et dit pourquoi), la barre dit le jour qui passe");
                mapView.AdvanceAnimation(3);
                Check(Math.Abs(mapView.DayFraction - 0.75) < 1e-9 && mapView.DayOfYear > 100 + 8 && mapView.DayOfYear < 100 + 10 && mapView.HintForProbe.StartsWith("Animation : jour 109"),
                    "trois secondes d'animation : une demi-journée a tourné, neuf jours ont passé (un jour en 6 s, une année en 2 min)");
                Check(mapView.AnimationNoteForProbe.StartsWith("Animation : jour 109") && Math.Abs(mapView.RingSpin - 3 * 2 * Math.PI / MapView.RingTurnSeconds) < 1e-9, "à côté du bouton flottant, le jour qui passe ; les anneaux ont tourné d'un trentième de tour (un tour en 90 s)");
                // Lot 16, bug « la lune saute à intervalles réguliers » : image par image sur deux jours
                // d'animation (l'heure repasse deux fois par zéro), aucun satellite ne recule ni ne bondit.
                Func<double> moonAngle = delegate { return mapView.SkyBodies().Find(delegate(MapCanvas.SkyDot d) { return d.Satellite; }).Angle; };
                var lastAngle = moonAngle();
                var worstJump = 0.0;
                for (var frame = 0; frame < 360; frame++)
                {
                    mapView.AdvanceAnimation(1.0 / 30);
                    var angle = moonAngle();
                    var step = angle - lastAngle;
                    step -= 2 * Math.PI * Math.Round(step / (2 * Math.PI));
                    worstJump = Math.Max(worstJump, Math.Abs(step));
                    if (step < -1e-9) worstJump = 99; // elle a reculé
                    lastAngle = angle;
                }
                Check(worstJump < 0.05, "douze secondes d'animation, image par image : la lune avance sans jamais sauter (au pire " + worstJump.ToString("0.0000") + " rad d'une image à l'autre)");
                mapView.AdvanceAnimation(-12);
                // Lot 15 : une image d'animation ne recalcule pas la sphère — seulement sa lumière.
                canvas.PaintGlobeForProbe();
                var builds = canvas.GeometryBuildsForProbe;
                var slowest = 0.0;
                for (var frame = 0; frame < 12; frame++)
                {
                    mapView.AdvanceAnimation(1.0 / 30);
                    canvas.PaintGlobeForProbe();
                    if (frame > 1) slowest = Math.Max(slowest, canvas.LastPaintMsForProbe);
                }
                Console.WriteLine("  mesure : une image du globe animé (" + canvas.GlobeBitmapPixelsForProbe + " px de côté) peinte en " + slowest.ToString("0.0") + " ms au pire");
                Check(canvas.GeometryBuildsForProbe == builds && slowest < 20, "pendant l'animation, la sphère n'est pas recalculée : une image se peint en " + slowest.ToString("0.0") + " ms au pire (33 ms disponibles à 30 images par seconde)");
                mapView.AdvanceAnimation(3 - 12.0 / 30 - 3);
                mapView.AdvanceAnimation(MapView.AnimationYearSeconds);
                Check(mapView.DayOfYear > 108 && mapView.DayOfYear < 111, "une année plus tard, on est revenu au même jour : les ans s'enchaînent sans à-coup");
                mapView.SetAnimation(false);
                await Settle();
                Check(!mapView.Animating && mapView.SkyPanel.IsEnabled && !tagged("map-sky-animating") && Math.Abs(mapView.DayOfYear - Math.Round(mapView.DayOfYear)) < 1e-9 && mapView.HintForProbe.Contains("tourner le globe"),
                    "arrêtée : le ciel se règle de nouveau à la main, sur un jour entier");
                mapView.SetAnimation(true);
                mapView.SetViewForProbe(Maps.ViewFlat);
                await Settle();
                Check(!mapView.Animating && !mapView.AnimateShownForProbe && mapView.SkyPanel.IsEnabled && mapView.ToolBarShownForProbe && mapView.GlobeToggleForProbe.IsChecked != true, "revenir au planisphère arrête l'animation, et le bouton s'en va ; la barre des outils revient");
                mapView.SetViewForProbe(Maps.ViewGlobe);
                await Settle();
                canvas.SetSkyTimeForProbe(-1);
                canvas.RotateGlobeForProbe(0, 0.2);
                mapView.SetNightForProbe(true, 200, 0.5);
                await Settle();
                mapView.SetViewForProbe(Maps.ViewFlat);
                await Settle();
                Check(item.Map.View == Maps.ViewFlat && mapView.ToolEnabledForProbe(MapCanvas.Tool.Zone), "retour au planisphère : les outils reviennent");
                // Lot 12 : décocher « Carte du monde entier » au globe ramène au planisphère, et retire le bouton.
                mapView.SetViewForProbe(Maps.ViewGlobe);
                mapView.SetWorldForProbe(earth.Id, false);
                await Settle();
                Check(item.Map.View == Maps.ViewFlat && !mapView.GlobeShown && !mapView.GlobeButtonShownForProbe && mapView.ToolEnabledForProbe(MapCanvas.Tool.Zone), "une carte qui cesse d'être celle du monde entier quitte le globe, et son bouton s'en va");
                mapView.SetWorldForProbe(earth.Id, true);
                await Settle();

                // — Annuler, supprimer.
                mapView.Select(mapView.Map.Zones[0], null);
                mapView.DeleteSelection();
                await Settle();
                Check(item.Map.Zones.Count == 0 && mapView.SelectedZone == null, "Suppr retire la zone choisie");
                Check(shell.UndoForProbe() && item.Map.Zones.Count == 1 && item.Map.Zones[0].Name == "Empire d'Aube", "Ctrl+Z la rend");
                await Settle();

                // — Remplacer l'image : zones et échelle suivent.
                var tilesBefore = item.Map.Tiles;
                shell.ReplaceMapImage(item, MapImport.Slice(MapImport.Synthetic(1200, 600), "petite.jpg"));
                await Settle();
                Check(item.Map.Width == 1200 && item.Map.Levels == 3 && !ReferenceEquals(item.Map.Tiles, tilesBefore) && Math.Abs(item.Map.Zones[0].Points[0].X - 300) < 1e-6 && Math.Abs(item.Map.ScaleB.X - 500) < 1e-6
                    && Math.Abs(Maps.ImpliedCircumference(item.Map) - implied) < 1e-6, "« Remplacer l'image » : les zones et l'échelle suivent, ramenées à la nouvelle taille");
                Check(shell.UndoForProbe() && item.Map.Width == 2400 && ReferenceEquals(item.Map.Tiles, tilesBefore), "…et Ctrl+Z rend l'ancienne image, sans avoir rien recopié");
                await Settle();

                // — La page des cartes.
                shell.Binder.SelectItem(mapsRoot.Id);
                await Settle();
                Check(mapsHub.CardsForProbe == 1 && !mapsHub.EmptyShownForProbe && plain(mapsHub.TotalForProbe).StartsWith("1 carte") && plain(mapsHub.TotalForProbe).Contains("dans le .plot")
                    && mapsHub.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, "maps-weight"); }), "la page des cartes : une tuile, son poids, et le total (" + mapsHub.TotalForProbe + ")");
                Shot(mapsHub, "cartes-5-page.png");

                // ================================================== l'analyseur de .plot
                var saveId = project.SaveId;
                var modified = project.ModifiedAt;
                var created = project.CreatedAt;
                var folder = Path.Combine(Path.GetTempPath(), "marabook-sonde-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try
                {
                    var path = Path.Combine(folder, "analyse.plot");
                    Check(item.Map.Tiles.MemoryCount == item.Map.Tiles.Count && item.Map.Tiles.MemoryBytes == Maps.Bytes(item.Map), "avant l'enregistrement, les tuiles de la carte importée sont en mémoire");
                    PlotFile.Save(project, path);
                    // ---- Lot 12 : les tuiles sont lues à la demande dans le .plot.
                    Check(item.Map.Tiles.MemoryCount == 0 && item.Map.Tiles.Count == Maps.TileCount(2400, 1200, 512), "enregistrée, la carte ne tient plus aucune tuile en mémoire (" + item.Map.Tiles.Count + " tuiles, " + Maps.SizeLabel(Maps.Bytes(item.Map)) + " rendus)");
                    var reloaded = PlotFile.Load(path);
                    var reloadedMap = Maps.All(reloaded).Find(delegate(BinderItem m) { return m.Id == item.Id; }).Map;
                    var readsBefore = PlotTiles.Reads;
                    var fresh = MapTileCache.For(reloadedMap);
                    Check(reloadedMap.Tiles.MemoryCount == 0 && fresh.Get(reloadedMap, 0, 0, 0, true) != null && PlotTiles.Reads == readsBefore + 1,
                        "un projet rouvert : la carte s'affiche en lisant UNE tuile dans le .plot (la vue d'ensemble), pas " + reloadedMap.Tiles.Count);
                    fresh.Get(reloadedMap, reloadedMap.Levels - 1, 1, 1);
                    for (var wait = 0; wait < 40 && fresh.Pending; wait++) await Task.Delay(50);
                    await Settle();
                    Check(fresh.Peek(reloadedMap.Levels - 1, 1, 1) != null && reloadedMap.Tiles.MemoryCount == 0 && PlotTiles.Reads == readsBefore + 2, "une tuile du plein format : lue et décodée hors du fil de l'interface, puis ses octets rendus");
                    shell.Binder.SelectItem(item.Id);
                    await Settle();
                    shell.SetRightPanelForProbe(RightPanel.Inspector);
                    await Settle();
                    Check(mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Lue à la demande dans le .plot"); }), "le Général le dit : « lue à la demande dans le .plot »");
                    // La fenêtre d'import d'une nouvelle carte (lot 12) : le monde et « Carte du monde entier », en option.
                    var importDialog = MapImportDialog.ForProbe(shell, project);
                    Check(importDialog.GetLogicalDescendants().OfType<ComboBox>().Any(delegate(ComboBox c) { return Equals(c.Tag, "map-import-world"); })
                        && importDialog.GetLogicalDescendants().OfType<CheckBox>().Any(delegate(CheckBox c) { return Equals(c.Tag, "map-import-whole"); })
                        && importDialog.GetLogicalDescendants().OfType<Button>().Any(delegate(Button b) { return Equals(b.Tag, "map-import-create") && !b.IsEnabled; }),
                        "la fenêtre d'import propose le monde et « Carte du monde entier » ; « Créer la carte » attend la fin du découpage");
                    importDialog.ReadyForProbe(MapImport.Slice(MapImport.Synthetic(800, 400), "import.jpg"));
                    var imported = importDialog.AcceptForProbe(project.Worlds.IndexOf(earth) + 1, true);
                    Check(imported != null && imported.WorldId == earth.Id && imported.WholeWorld && importDialog.GetLogicalDescendants().OfType<Button>().Any(delegate(Button b) { return Equals(b.Tag, "map-import-create") && b.IsEnabled; })
                        && importDialog.GetLogicalDescendants().OfType<TextBlock>().Any(delegate(TextBlock b) { return Equals(b.Tag, "map-import-status") && (b.Text ?? "").Contains("sans doute un planisphère entier"); }),
                        "…le découpage fini, elle dit le poids et la forme de l'image, et la carte naît liée à son monde, « monde entier » coché");
                    var report = PlotAnalysis.Analyze(path, project);
                    long sum = 0;
                    foreach (var section in report.Sections) sum += section.Bytes;
                    var cartes = report.Find(PlotAnalysis.Maps);
                    Check(sum == report.FileBytes && cartes.Bytes >= Maps.Bytes(item.Map) && cartes.Details.Exists(delegate(PlotAnalysis.Detail d) { return d.Name.Contains("Planisphère d'essai"); }),
                        "l'Analyseur de .plot pèse chaque section — tout le fichier, à l'octet (" + Maps.SizeLabel(report.FileBytes) + ") — et chaque carte");
                    var analyzer = PlotAnalyzerDialog.BuildForProbe(shell, path, project, true);
                    analyzer.Show(shell);
                    await Settle();
                    Check(analyzer.SectionCardsForProbe >= 5 && analyzer.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, "analyzer-dirty"); })
                        && analyzer.GetVisualDescendants().OfType<TextBlock>().Any(delegate(TextBlock b) { return b.Text == PlotAnalysis.Maps; }),
                        "sa fenêtre : une carte par section (" + analyzer.SectionCardsForProbe + "), et l'avis quand le projet n'est pas enregistré");
                    Shot(analyzer, "analyseur-1.png");
                    analyzer.Close();
                    await Settle();

                    // — La vraie carte de Rémi, si elle est là (et qu'on garde des images).
                    var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "test-map.jpg");
                    if (File.Exists(real) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOTS")))
                    {
                        var bytes = File.ReadAllBytes(real);
                        clock.Restart();
                        var realItem = shell.ImportMapForProbe("test-map", bytes);
                        clock.Stop();
                        added.Add(realItem);
                        await Settle();
                        for (var wait = 0; wait < 40 && MapTileCache.For(realItem.Map).Pending; wait++) await Task.Delay(50);
                        await Settle();
                        Console.WriteLine("  mesure : test-map.jpg " + realItem.Map.Width + " × " + realItem.Map.Height + " px, " + Maps.SizeLabel(bytes.LongLength) + " → " + realItem.Map.Levels + " niveaux, "
                            + realItem.Map.Tiles.Count + " tuiles, " + Maps.SizeLabel(Maps.Bytes(realItem.Map)) + " dans le .plot, import en " + clock.ElapsedMilliseconds + " ms");
                        Check(realItem.Map.HasImage && realItem.Map.Levels >= 4, "la carte d'essai de Rémi s'importe");
                        Shot(mapView, "cartes-6-test-map.png");
                        canvas.ZoomBy(1 / canvas.Scale, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
                        await Settle();
                        for (var wait = 0; wait < 60 && MapTileCache.For(realItem.Map).Pending; wait++) await Task.Delay(50);
                        await Settle();
                        Shot(mapView, "cartes-7-test-map-plein-format.png");
                        mapView.SetViewForProbe(Maps.ViewGlobe);
                        await Settle();
                        canvas.PaintGlobeForProbe();
                        canvas.InvalidateVisual();
                        await Settle();
                        Shot(mapView, "cartes-8-test-map-globe.png");
                        PlotFile.Save(project, path);
                        var realReport = PlotAnalysis.Analyze(path, project);
                        Console.WriteLine("  mesure : .plot de la démo avec la carte : " + Maps.SizeLabel(realReport.FileBytes) + ", dont cartes " + Maps.SizeLabel(realReport.Find(PlotAnalysis.Maps).Bytes));
                        var realAnalyzer = PlotAnalyzerDialog.BuildForProbe(shell, path, project, false);
                        realAnalyzer.Show(shell);
                        await Settle();
                        Shot(realAnalyzer, "analyseur-2-test-map.png");
                        realAnalyzer.Close();
                        await Settle();
                    }
                }
                finally
                {
                    // Le dossier d'essai va disparaître : les tuiles qui y dorment reviennent en mémoire.
                    foreach (var mapItem in Maps.All(project)) mapItem.Map.Tiles.Materialize();
                    project.SaveId = saveId;
                    project.ModifiedAt = modified;
                    project.CreatedAt = created;
                    try { Directory.Delete(folder, true); } catch (Exception) { }
                }

                // ---- Lot 12 : hors d'une carte, ses onglets quittent le rail.
                shell.Binder.SelectItem(item.Id);
                await Settle();
                shell.SetRightPanelForProbe(RightPanel.MapShapes);
                await Settle();
                shell.Binder.SelectItem(mapsRoot.Id);
                await Settle();
                Check(!shell.RailHasTabForProbe(RightPanel.MapSky) && !shell.RailHasTabForProbe(RightPanel.MapShapes) && !shell.RailHasTabForProbe(RightPanel.MapNavigation) && !shell.MapShapesHostForProbe.IsEffectivelyVisible && !shell.MapSectionShownForProbe,
                    "hors d'une carte, le rail n'a plus ses deux onglets, et leur panneau ne reste pas affiché");
                shell.Binder.SelectItem(item.Id);
                await Settle();
                Check(shell.RailHasTabForProbe(RightPanel.MapShapes) && AppSettings.RightPanel == RightPanel.MapShapes && shell.MapShapesHostForProbe.IsEffectivelyVisible, "de retour sur la carte, l'onglet choisi revient avec elle");
                shell.SetRightPanelForProbe(RightPanel.Inspector);
                await Settle();

                // ---- Lot 13 : les repères de navigation.
                shell.Binder.SelectItem(item.Id);
                await Settle();
                Check(shell.RailHasTabForProbe(RightPanel.MapNavigation) && shell.RailTabRankForProbe(RightPanel.MapNavigation) == shell.RailTabRankForProbe(RightPanel.MapShapes) + 1
                    && Icons.Has("carte-navigation") && Icons.Has("carte-boussole") && shell.RailMapSeparatorForProbe, "le rail a un troisième onglet de carte, le dernier : « Repères de navigation » (sextant)");
                shell.SetRightPanelForProbe(RightPanel.MapNavigation);
                await Settle();
                mapView.SetGuides(false);
                await Settle();
                Check(AppSettings.RightPanel == RightPanel.MapNavigation && shell.MapNavigationHostForProbe.IsEffectivelyVisible && tagged("map-guides-show") && tagged("map-guides-define") && tagged("map-latlon"),
                    "l'onglet : la case des repères, et les deux grands boutons « Définir les marqueurs » et « Mesures Lat/Lon »");
                Check(mapView.Guides.Count == 5 && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-guide-row"); }) == 5 && !mapView.GuidesShown
                    && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Ceux par défaut de « Terre d'essai »"); }),
                    "il liste les marqueurs par défaut du monde lié (équateur, deux tropiques, deux cercles polaires), masqués d'office");
                mapView.SetGuides(true);
                await Settle();
                Check(mapView.GuidesShown, "« Afficher les repères de navigation » : les parallèles se tracent sur la carte");
                mapView.SetLatLon(true);
                await Settle();
                double geoLat, geoLon;
                var geoKnown = canvas.GeoAt(canvas.ScreenOfForProbe(1200, 600), out geoLat, out geoLon);
                Check(mapView.LatLonShown && tagged("map-geo") && geoKnown && Math.Abs(geoLat) < 1e-6 && Math.Abs(geoLon) < 1e-6, "« Mesures Lat/Lon » : au centre de la carte, 0° de latitude et 0° de longitude");
                geoKnown = canvas.GeoAt(canvas.ScreenOfForProbe(1800, 300), out geoLat, out geoLon);
                mapView.CursorGeo(geoKnown, geoLat, geoLon);
                Check(geoKnown && plain(Maps.GeoLabel(geoLat, geoLon)) == "45° N · 90° E" && mapView.SideTextsForProbe().Exists(delegate(string s) { return plain(s) == "45° N · 90° E"; }),
                    "…au quart nord-est : 45° N · 90° E, redit dans le panneau");
                Check(!canvas.GeoAt(new Point(-50, -50), out geoLat, out geoLon), "hors de la carte, pas de coordonnées");
                Shot(mapView, "cartes-17-reperes.png");
                Shot(shell.MapNavigationHostForProbe, "cartes-18-rail-navigation.png");

                // « Définir les marqueurs » : ceux du MONDE, édités depuis la carte.
                var guidesDialog = GuidesDialog.ForProbe(shell, earth);
                var guidesEditor = guidesDialog.EditorForProbe;
                Check(!guidesEditor.CustomForProbe && guidesEditor.RowsForProbe == 5 && guidesEditor.NoteForProbe.Contains("23,44"), "sa fenêtre s'ouvre sur les marqueurs par défaut, et dit d'où ils viennent (l'inclinaison de l'axe)");
                guidesEditor.SetCustom(true);
                Check(guidesEditor.CustomForProbe && guidesEditor.RowsForProbe == 5, "« Marqueurs personnalisés » part de ceux par défaut : on les renomme, on en ajoute");
                guidesEditor.SetRowForProbe(1, "Tropique d'Aube", "25");
                guidesEditor.AddForProbe();
                guidesEditor.SetRowForProbe(5, "Ligne des Brumes", "abc");
                Check(guidesDialog.AcceptForProbe() == null && (guidesDialog.ProblemForProbe ?? "").Contains("Ligne des Brumes"), "une latitude illisible est refusée, et le marqueur fautif nommé");
                guidesEditor.SetRowForProbe(5, "Ligne des Brumes", "-48,5");
                var guidedWorld = guidesDialog.AcceptForProbe();
                Check(guidedWorld != null && guidedWorld.CustomGuides && guidedWorld.Guides.Count == 6 && guidedWorld.Guides[1].Name == "Tropique d'Aube" && guidedWorld.Guides[1].Latitude == 25 && guidedWorld.Guides[5].Latitude == -48.5
                    && !earth.CustomGuides, "validée : six marqueurs, dont un tropique renommé et déplacé — sur une copie, le monde n'a pas encore bougé");
                shell.ApplyGuides(earth, guidedWorld);
                await Settle();
                earth = project.Worlds.Find(delegate(World w) { return w.Id == earth.Id; });
                Check(earth.CustomGuides && mapView.Guides.Count == 6 && parts().Count(delegate(Control c) { return Equals(c.Tag, "map-guide-row"); }) == 6
                    && mapView.SideTextsForProbe().Contains("Tropique d'Aube") && mapView.SideTextsForProbe().Exists(delegate(string s) { return s.StartsWith("Ceux que vous avez définis"); }),
                    "ils s'écrivent dans le monde : la carte les trace et les liste aussitôt (" + earth.CustomGuides + mapView.Guides.Count + parts().Count(delegate(Control c) { return Equals(c.Tag, "map-guide-row"); }) + mapView.SideTextsForProbe().Contains("Tropique d'Aube") + ")");
                Shot(mapView, "cartes-19-marqueurs-personnalises.png");
                Check(shell.UndoForProbe() && !project.Worlds.Find(delegate(World w) { return w.Id == earth.Id; }).CustomGuides, "…et Ctrl+Z rend au monde ses marqueurs par défaut");
                earth = project.Worlds.Find(delegate(World w) { return w.Id == earth.Id; });
                await Settle();
                Check(mapView.Guides.Count == 5, "la carte suit");
                // La fenêtre du monde a la même section.
                var worldGuides = new GuidesEditor(earth.Clone());
                Check(worldGuides.RowsForProbe == 5 && !worldGuides.CustomForProbe, "la fenêtre du monde porte le même éditeur (onglet « Marqueurs ») : par défaut à la création");

                // ---- Lot 13 : la carte se souvient de ses outils et de ses réglages.
                mapView.SetFilters(true, false);
                mapView.SetSeasonHeatForProbe(true);
                mapView.SetNightForProbe(true, 123, 0.25);
                mapView.ChooseTool(MapCanvas.Tool.Marker);
                canvas.ZoomBy(2.5, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
                await Settle();
                var rememberedScale = canvas.Scale;
                var rememberedOrigin = canvas.ViewOriginForProbe;
                shell.Binder.SelectItem(mapsRoot.Id);
                await Settle();
                Check(AppSettings.MapStates.ContainsKey(item.Id) && (bool)AppSettings.MapStates[item.Id]["guides"] && (string)AppSettings.MapStates[item.Id]["tool"] == "Marker",
                    "en quittant la carte, ses réglages sont retenus par l'application (pas dans le projet : rien n'est « modifié »)");
                mapView.SetFilters(true, true);
                shell.Binder.SelectItem(item.Id);
                await Settle();
                Check(mapView.GuidesShown && mapView.LatLonShown && mapView.SeasonHeatShown && mapView.NightShown && mapView.ShowZones && !mapView.ShowMarkers && Math.Abs(mapView.DayOfYear - 123) < 1e-9 && Math.Abs(mapView.DayFraction - 0.25) < 1e-9,
                    "de retour : les repères, les mesures, les saisons, la nuit, les filtres, le jour et l'heure sont tels qu'on les avait laissés");
                Check(canvas.CurrentTool == MapCanvas.Tool.Marker && mapView.ToolCheckedForProbe(MapCanvas.Tool.Marker), "…l'outil aussi (« Repère »)");
                Check(Math.Abs(canvas.Scale - rememberedScale) < 1e-9 && Math.Abs(canvas.ViewOriginForProbe.X - rememberedOrigin.X) < 1e-6 && Math.Abs(canvas.ViewOriginForProbe.Y - rememberedOrigin.Y) < 1e-6,
                    "…et la vue : même zoom, même endroit");
                mapView.ChooseTool(MapCanvas.Tool.Pan);
                mapView.SetFilters(true, true);
                mapView.SetLatLon(false);
                mapView.SetGuides(false);
                mapView.SetSeasonHeatForProbe(false);
                mapView.SetNightForProbe(false, 1, 0.5);
                canvas.FitAll();
                await Settle();
                shell.SetRightPanelForProbe(RightPanel.Inspector);
                await Settle();

                // — Le thème sombre.
                shell.Binder.SelectItem(item.Id);
                await Settle();
                var wasDark = Chrome.Dark;
                Chrome.Toggle(true);
                App.ApplyTheme(true);
                await Settle();
                mapView.Refresh();
                await Settle();
                Shot(mapView, "cartes-9-sombre.png");
                Chrome.Toggle(wasDark);
                App.ApplyTheme(wasDark);
                await Settle();
            }
            catch (Exception error)
            {
                Check(false, "la sonde des cartes va au bout (" + error.GetType().Name + " : " + error.Message + ")");
                Console.Error.WriteLine(error);
            }
            finally
            {
                // Le projet d'exemple est rendu tel qu'il était : les sondes suivantes comptent ses éléments.
                var home = project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                if (earth != null) project.Worlds.RemoveAll(delegate(World w) { return w.Id == earth.Id; });
                shell.Binder.Rebuild();
                await Settle();
            }
        }
    }
}
