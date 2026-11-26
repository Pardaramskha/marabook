using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde du lot 23 (2.0.0, 21/11/2026) : la fenêtre d'un
    /// événement à plusieurs éléments, avec lieu et tags ; sa fiche dans le
    /// rail (âges, lieu, tags), le filtre par tag, le lieu qui ouvre la
    /// carte et la carte qui ramène à l'événement ; la bibliothèque des
    /// fiches en liste et son tri par catégorie ; les quatre natures de
    /// champ ; la famille « Champs » et les forces du graphe ; les fiches en
    /// Markdown et en CSV ; la lecture à voix haute (sans son).</summary>
    public static partial class Probes
    {
        private static async Task ProbeLot23(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- le lot 23 (2.0.0, 21/11)");
            var shots = Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOTS");
            var folder = string.IsNullOrEmpty(shots) ? Path.Combine(Path.GetTempPath(), "marabook-sonde-lot23-" + Guid.NewGuid().ToString("N")) : Path.Combine(shots, "lot23");
            Directory.CreateDirectory(folder);
            var added = new List<BinderItem>();
            var addedEvents = new List<TimelineEvent>();
            var addedFields = new List<SheetField>();
            var timeline = Timelines.FindByTitle(project, TimelineSamples.TimelineTitle);
            var keira = FirstTitled(project, "Keira");
            var template = keira == null ? null : project.TemplateOf(keira);
            var sortsBefore = new Dictionary<string, int>(AppSettings.LibrarySorts);
            var listBefore = AppSettings.LibraryList;
            var dryBefore = ReadAloud.Dry;
            try
            {
                // ================================================== un événement : éléments, lieu, tags
                if (timeline != null && keira != null)
                {
                    BinderItem other = null;
                    foreach (var item in TimelineLinks.ItemsOf(project, TimelineLinks.Sheet)) if (item != keira && other == null) other = item;
                    shell.Binder.SelectItem(timeline.Id);
                    await Settle();
                    var view = shell.TimelineForProbe;
                    view.SetCalendarMode(false);
                    view.SetListMode(false);
                    view.SelectTabForProbe(-1);
                    view.ShowAllTypesForProbe();
                    view.SetSheetFilter(null);
                    view.SetTagFilter(null);
                    await Settle();
                    var start = timeline.Timeline.Events.Count > 0 && timeline.Timeline.Events[0].Start != null ? timeline.Timeline.Events[0].Start.Clone() : new TimePoint(1, 1, 1);

                    // Sans lieu sur aucune carte, la fenêtre ne montre pas la ligne « Lieu ».
                    if (Timelines.Places(project).Count == 0)
                    {
                        var bareTask = TimelineEventDialog.Ask(shell, project, view.System, timeline.Timeline, new TimelineEvent { Start = start.Clone() }, true);
                        await Settle();
                        var bare = OpenWindow<TimelineEventDialog>();
                        Check(bare != null && !bare.PlaceShownForProbe && bare.LinkChipsForProbe == 0, "sans repère ni zone sur une carte, la fenêtre d'un événement n'a pas de ligne « Lieu », ni de puce");
                        if (bare != null) bare.Close();
                        await Settle();
                        await bareTask;
                    }

                    var mapItem = shell.ImportMapForProbe("Carte du lot 23", MapImport.Synthetic(800, 400));
                    await Settle();
                    await Settle();
                    added.Add(mapItem);
                    MapMarker marker = null;
                    if (mapItem != null && mapItem.Map != null)
                    {
                        marker = new MapMarker { Name = "Gué des Saules", X = 200, Y = 150 };
                        mapItem.Map.Markers.Add(marker);
                        mapItem.Map.Zones.Add(new MapZone { Name = "Plaine d'Orme", Points = { new Pos(100, 60), new Pos(320, 60), new Pos(320, 260), new Pos(100, 260) } }); // elle contient le repère
                    }
                    shell.Binder.SelectItem(timeline.Id);
                    await Settle();

                    var draft = new TimelineEvent { Start = start.Clone() };
                    var task = TimelineEventDialog.Ask(shell, project, view.System, timeline.Timeline, draft, true);
                    await Settle();
                    var dialog = OpenWindow<TimelineEventDialog>();
                    Check(dialog != null, "la fenêtre d'un nouvel événement s'ouvre");
                    if (dialog != null && marker != null && other != null)
                    {
                        Check(dialog.LinkChipsForProbe == 0, "un événement neuf ne relie rien : aucune puce");
                        // L'autocomplète (21/11) : tout le projet d'office, chaque nom avec sa sorte.
                        var labels = dialog.LinkLabelsForProbe;
                        var keiraLabel = labels.Find(delegate(string l) { return l.StartsWith(keira.Title + "  —  Fiche"); });
                        Check(dialog.LinkSearchEditableForProbe && keiraLabel != null && labels.Exists(delegate(string l) { return l.EndsWith("—  Écrit"); }) && Suggestions.Matches("keir", labels) && !Suggestions.Matches("zzzz", labels),
                            "« Relier à » est un champ à autocomplétion : taper « keir » propose Keira parmi tout le projet, chaque nom avec sa sorte (" + labels.Count + " éléments)");
                        dialog.PickLabelForProbe(keiraLabel);
                        await Settle();
                        Check(dialog.LinkChipsForProbe == 1, "choisir une proposition relie l'élément : une puce");
                        dialog.RemoveLinkForProbe(0);
                        dialog.AddLinkForProbe(TimelineLinks.Sheet, keira.Id);
                        await Settle();
                        dialog.AddLinkForProbe(TimelineLinks.Sheet, other.Id);
                        await Settle();
                        dialog.AddLinkForProbe(TimelineLinks.Sheet, keira.Id); // un doublon ne s'ajoute pas
                        await Settle();
                        Check(dialog.LinkChipsForProbe == 2, "choisir un élément l'ajoute en puce ; un second s'ajoute, un doublon non (" + dialog.LinkChipsForProbe + ")");
                        Check(dialog.PlaceShownForProbe && dialog.PlaceCountForProbe == 2, "la ligne « Lieu » propose les repères et les zones des cartes (" + dialog.PlaceCountForProbe + ")");
                        dialog.SetPlaceForProbe(marker.Id);
                        // Les tags en puces (21/11) : la virgule pose le tag, le reste attend dans le champ.
                        dialog.TypeTagsForProbe("hiver, nor");
                        await Settle();
                        Check(dialog.TagChipsForProbe == 1 && dialog.PendingTagForProbe == "nor", "une virgule pose le tag en puce ; ce qui suit reste à taper (" + dialog.TagChipsForProbe + " puce, « " + dialog.PendingTagForProbe + " »)");
                        dialog.TypeTagsForProbe("");
                        dialog.SetTagsForProbe("sonde, #guerre ; Sonde");
                        Check(dialog.TagChipsForProbe == 3 && dialog.PendingTagForProbe == "", "…et les doublons ne font qu'une puce (" + dialog.TagChipsForProbe + ")");
                        dialog.FillForProbe("La bataille du gué (sonde)", view.System.Format(start), null);
                        await Settle();
                        Shot(dialog, "lot23-1-evenement.png");
                        dialog.AcceptForProbe();
                    }
                    else if (dialog != null) dialog.Close();
                    await Settle();
                    var made = await task;
                    Check(made != null && made.Event != null && Timelines.LinksOf(made.Event).Count == 2 && made.Event.LinkId == keira.Id, "l'événement rendu relie les deux fiches, la première en tête");
                    Check(made != null && made.Event != null && marker != null && made.Event.PlaceShapeId == marker.Id && made.Event.PlaceMapId == mapItem.Id, "…porte son lieu");
                    Check(made != null && made.Event != null && Timelines.TagsText(made.Event) == "hiver, sonde, guerre", "…et ses tags, nettoyés (" + (made == null || made.Event == null ? "-" : Timelines.TagsText(made.Event)) + ")");
                    if (made != null && made.Event != null && other != null)
                    {
                        var battle = made.Event;
                        timeline.Timeline.Events.Add(battle);
                        addedEvents.Add(battle);
                        var untagged = new TimelineEvent { Title = "Sans tag (sonde)", Start = start.Clone() };
                        timeline.Timeline.Events.Add(untagged);
                        addedEvents.Add(untagged);
                        view.Refresh();
                        await Settle();
                        view.SelectEvent(battle.Id);
                        await Settle();
                        var card = view.CardTextsForProbe();
                        Check(card.Any(delegate(string l) { return l.Contains(keira.Title); }) && card.Any(delegate(string l) { return l.Contains(other.Title); }),
                            "la fiche de l'événement, au rail, a un bouton par élément relié");
                        Check(card.Any(delegate(string l) { return l.StartsWith("Lieu : Gué des Saules (Carte du lot 23)"); }), "…un bouton pour son lieu");
                        Check(card.Any(delegate(string l) { return l == "#hiver  #sonde  #guerre"; }), "…et ses tags");
                        Shot(view, "lot23-2-fiche-evenement.png");

                        // Le filtre par tag, rangé dans le menu « Types ».
                        var tagMenu = view.TagMenuForProbe();
                        Check(tagMenu.Count == 3 && tagMenu.Any(delegate(string m) { return m.StartsWith("#guerre   1"); }), "le menu « Types » a un sous-menu des tags, avec leur compte (" + string.Join(" | ", tagMenu.ToArray()) + ")");
                        var allPlaced = view.PlacedCountForProbe;
                        view.SetTagFilter("GUERRE");
                        await Settle();
                        Check(view.PlacedCountForProbe == 1 && (view.TypesLabelForProbe ?? "").Contains("#GUERRE"), "« ne montrer qu'un tag » : la frise ne garde que ses événements, le bouton le dit (" + view.TypesLabelForProbe + ")");
                        view.SetListMode(true);
                        await Settle();
                        var row = view.ListForProbe.RowTextForProbe(0) ?? "";
                        Check(view.ListForProbe.RowCountForProbe == 1 && row.Contains(keira.Title + ", " + other.Title) && row.Contains("lieu : Gué des Saules"), "la Liste suit le filtre et dit les éléments et le lieu (" + row + ")");
                        view.ListForProbe.Query = "saules";
                        await Settle();
                        Check(view.ListForProbe.RowCountForProbe == 1, "la recherche de la Liste lit le lieu");
                        view.ListForProbe.Query = "";
                        view.SetListMode(false);
                        view.SetTagFilter(null);
                        await Settle();
                        Check(view.PlacedCountForProbe == allPlaced && !(view.TypesLabelForProbe ?? "").Contains("#"), "« Tous les tags » rend la frise entière");

                        // Le filtre par fiche voit le deuxième élément.
                        view.SetSheetFilter(other);
                        await Settle();
                        Check(view.ListForProbe.Events().Any(delegate(TimelineEvent e) { return e.Id == battle.Id; }), "le filtre par fiche garde l'événement dont la fiche est le DEUXIÈME élément");
                        view.SetSheetFilter(null);
                        await Settle();

                        // Rouvrir : les puces sont là ; en retirer une.
                        var editTask = TimelineEventDialog.Ask(shell, project, view.System, timeline.Timeline, battle.Clone(), false);
                        await Settle();
                        var edit = OpenWindow<TimelineEventDialog>();
                        Check(edit != null && edit.LinkChipsForProbe == 2, "rouvert, l'événement montre ses deux puces");
                        if (edit != null)
                        {
                            edit.RemoveLinkForProbe(0);
                            await Settle();
                            Check(edit.LinkChipsForProbe == 1, "la croix d'une puce retire l'élément");
                            edit.AcceptForProbe();
                        }
                        await Settle();
                        var edited = await editTask;
                        Check(edited != null && edited.Event != null && edited.Event.LinkId == other.Id && edited.Event.More.Count == 0 && edited.Event.PlaceShapeId == marker.Id && Timelines.TagsText(edited.Event) == "hiver, sonde, guerre",
                            "le second élément devient le premier ; le lieu et les tags n'ont pas bougé");

                        // Le lieu ouvre la carte, et la carte ramène à l'événement.
                        var mapView = shell.MapForProbe;
                        shell.Binder.SelectItem(mapItem.Id);
                        await Settle();
                        mapView.SetFilters(false, false); // tout masqué : le lieu doit quand même se montrer
                        mapView.CanvasForProbe.FitAll();
                        await Settle();
                        shell.Binder.SelectItem(timeline.Id);
                        await Settle();
                        view.RequestPlaceForProbe(battle);
                        await Settle();
                        await Settle();
                        Check(shell.VisibleView == "map" && mapView.SelectedMarker != null && mapView.SelectedMarker.Id == marker.Id, "« Lieu : … » ouvre la carte, le repère choisi");
                        Check(mapView.CanvasForProbe.ZoomRatio >= 1.9, "…ZOOMÉE sur le lieu (× " + mapView.CanvasForProbe.ZoomRatio.ToString("0.0") + " la vue « Tout voir »)");
                        Check(mapView.ShowZones && mapView.ShowMarkers, "…les zones et les repères masqués sont rallumés");
                        Check(mapView.HighlightedCountForProbe == 1, "…et la zone qui contient le repère est mise en lumière");
                        Shot(mapView, "lot23-3b-lieu-zoome.png");
                        await Settle();
                        var here = shell.GetLogicalDescendants().OfType<Button>().Where(delegate(Button b) { return Equals(b.Tag, "map-event-here"); }).ToList();
                        Check(here.Count == 1, "le repère choisi liste « Événements ici » (" + here.Count + ")");
                        Shot(shell, "lot23-3-evenements-ici.png");
                        shell.OpenEventOfPlace(timeline, battle.Id);
                        await Settle();
                        await Settle();
                        Check(shell.VisibleView == "timeline" && shell.TimelineForProbe.SelectedEventForProbe != null && shell.TimelineForProbe.SelectedEventForProbe.Id == battle.Id, "…et un clic y ramène, l'événement choisi dans sa frise");
                    }
                }

                // ================================================== la bibliothèque en liste, le tri par catégorie
                var sheetsRoot = project.Category(Project.KeySheets);
                if (sheetsRoot != null && keira != null)
                {
                    AppSettings.LibrarySorts.Clear();
                    shell.Binder.SelectItem(sheetsRoot.Id);
                    await Settle();
                    var library = shell.SheetLibraryForProbe;
                    library.SetListMode(false);
                    await Settle();
                    var tiles = library.CardsCountForProbe;
                    var tile = library.CardForProbe(keira.Id);
                    Check(tile != null && !tile.Classes.Contains("library-row"), "la bibliothèque s'ouvre en tuiles");
                    library.SetListMode(true);
                    await Settle();
                    var line = library.CardForProbe(keira.Id);
                    Check(library.ListModeForProbe && line != null && line.Classes.Contains("library-row") && library.CardsCountForProbe == tiles, "« Vue en liste » : les mêmes fiches, une ligne chacune (" + library.CardsCountForProbe + ")");
                    Check(line != null && tile != null && line.Bounds.Height > 0 && line.Bounds.Height < 60, "une ligne est bien plus basse qu'une tuile (" + (line == null ? 0 : Math.Round(line.Bounds.Height)) + " px)");
                    var blank = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche vide (sonde)", TemplateId = keira.TemplateId, CategoryId = keira.CategoryId, Document = new TextDocument(), ImageId = keira.ImageId };
                    blank.Parent = keira.Parent;
                    keira.Parent.Children.Add(blank);
                    added.Add(blank);
                    library.Refresh();
                    await Settle();
                    var blankRow = library.CardForProbe(blank.Id);
                    var keiraRow = library.CardForProbe(keira.Id);
                    Func<Border, bool> marked = delegate(Border row) { return row != null && row.GetLogicalDescendants().OfType<Border>().Any(delegate(Border b) { return Equals(b.Tag, "library-row-empty"); }); };
                    Check(SheetContent.IsEmpty(blank) && !SheetContent.IsEmpty(keira) && marked(blankRow) && !marked(keiraRow), "en liste, une fiche où rien n'est écrit hors de son nom porte la mention « Vide » — sa photo ne compte pas ; une fiche remplie ne la porte pas");
                    Shot(library, "lot23-4-bibliotheque-liste.png");
                    var category = project.SheetCategoryOf(keira);
                    var top = category == null ? null : project.TopOf(category);
                    if (top != null)
                    {
                        Check(library.SortIndexForProbe == 0, "le tri d'office : nom A → Z");
                        library.FilterCategoryForProbe(top.Id);
                        await Settle();
                        var ascending = library.CardTitlesForProbe();
                        library.SetSortForProbe(1);
                        await Settle();
                        var descending = library.CardTitlesForProbe();
                        Check(AppSettings.LibrarySortFor(top.Id) == 1 && descending.Count == ascending.Count && (ascending.Count < 2 || descending[0] != ascending[0]), "choisi sous une catégorie filtrée, le tri vaut pour elle (" + (descending.Count > 0 ? descending[0] : "-") + " en tête)");
                        library.FilterCategoryForProbe(null);
                        await Settle();
                        Check(library.SortIndexForProbe == 0 && AppSettings.LibrarySortFor(null) == 0, "…et le filtre levé, le sélecteur revient au tri d'office");
                        library.FilterCategoryForProbe(top.Id);
                        await Settle();
                        Check(library.SortIndexForProbe == 1, "…la catégorie retrouvée, son tri aussi");
                        library.FilterCategoryForProbe(null);
                        await Settle();
                    }
                    library.SetListMode(false);
                    await Settle();
                    Check(!library.ListModeForProbe && library.CardForProbe(keira.Id) != null && !library.CardForProbe(keira.Id).Classes.Contains("library-row"), "le bouton rend les tuiles");
                }

                // ================================================== quatre natures de champ
                if (keira != null && template != null)
                {
                    var group = template.Fields.Count > 0 ? template.Fields[0].Group : "";
                    var yes = new SheetField { Id = "sonde-bool", Name = "Initiée (sonde)", Kind = FieldKinds.Bool, Group = group };
                    var tint = new SheetField { Id = "sonde-couleur", Name = "Couleur des yeux (sonde)", Kind = FieldKinds.Color, Group = group };
                    var multi = new SheetField { Id = "sonde-multi", Name = "Éléments (sonde)", Kind = FieldKinds.Multi, Group = group, Options = new List<string> { "feu", "glace", "ombre" } };
                    var coords = new SheetField { Id = "sonde-coords", Name = "Position (sonde)", Kind = FieldKinds.Coords, Group = group };
                    foreach (var field in new[] { yes, tint, multi, coords }) { template.Fields.Add(field); addedFields.Add(field); }
                    Check(FieldKinds.All.Length == 12, "douze natures de champ sont proposées");
                    shell.Binder.SelectItem(project.Category(Project.KeyWritings).Id);
                    await Settle();
                    shell.Binder.SelectItem(keira.Id);
                    await Settle();
                    var sheetView = shell.Sheet;
                    if (sheetView.PreviewOnForProbe) { sheetView.HandleFunctionKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.F4 }); await Settle(); }
                    sheetView.ShowGeneralTabForProbe();
                    await Settle();
                    var box = sheetView.FieldControlForProbe(yes.Id) as CheckBox;
                    Check(box != null && box.IsChecked != true, "« Oui / non » : une case, décochée tant qu'on n'y touche pas");
                    if (box != null)
                    {
                        box.IsChecked = true;
                        await Settle();
                        string value;
                        Check(keira.FieldValues.TryGetValue(yes.Id, out value) && value == FieldKinds.Yes && (box.Content as string) == "Oui", "cochée, la fiche garde « oui »");
                        box.IsChecked = false;
                        await Settle();
                        Check(keira.FieldValues.TryGetValue(yes.Id, out value) && value == FieldKinds.No, "décochée, « non » — et plus « pas renseigné »");
                    }
                    var hex = sheetView.FieldControlForProbe(tint.Id) as TextBox;
                    Check(hex != null, "« Couleur » : une pastille et son code");
                    if (hex != null)
                    {
                        hex.Text = "a1b2c3";
                        await Settle();
                        string value;
                        Check(keira.FieldValues.TryGetValue(tint.Id, out value) && value == "#A1B2C3", "un code tapé est gardé en « #A1B2C3 » (" + (keira.FieldValues.ContainsKey(tint.Id) ? keira.FieldValues[tint.Id] : "-") + ")");
                    }
                    var boxes = sheetView.FieldControlForProbe(multi.Id) as Panel;
                    var choices = boxes == null ? new List<CheckBox>() : boxes.Children.OfType<CheckBox>().ToList();
                    Check(choices.Count == 3, "« Choix multiple » : une case par option du modèle (" + choices.Count + ")");
                    if (choices.Count == 3)
                    {
                        choices[2].IsChecked = true;
                        choices[0].IsChecked = true;
                        await Settle();
                        string value;
                        Check(keira.FieldValues.TryGetValue(multi.Id, out value) && value == "feu, ombre", "les options cochées, dans l'ordre du modèle (" + (keira.FieldValues.ContainsKey(multi.Id) ? keira.FieldValues[multi.Id] : "-") + ")");
                    }
                    var latitude = sheetView.FieldControlForProbe(coords.Id) as TextBox;
                    var longitude = latitude == null || latitude.Parent == null ? null : ((Panel)latitude.Parent).Children.OfType<TextBox>().FirstOrDefault(delegate(TextBox b) { return Equals(b.Tag, "field-coords-lon"); });
                    Check(latitude != null && longitude != null, "« Coordonnées » : latitude et longitude");
                    if (latitude != null && longitude != null)
                    {
                        latitude.Text = "33,9 S";
                        longitude.Text = "18,4";
                        await Settle();
                        string value;
                        Check(keira.FieldValues.TryGetValue(coords.Id, out value) && value == "-33.9;18.4", "« 33,9 S » et « 18,4 » sont gardées en degrés (" + (keira.FieldValues.ContainsKey(coords.Id) ? keira.FieldValues[coords.Id] : "-") + ")");
                        Check(FieldKinds.Display(FieldKinds.Coords, keira.FieldValues[coords.Id], project) == "33,9° S · 18,4° E", "…et se lisent « 33,9° S · 18,4° E »");
                    }
                    Shot(sheetView, "lot23-5-natures-de-champ.png");
                    var wiki = SheetWiki.Build(keira, template, project, "", true, delegate { }, delegate { }, delegate { });
                    Check(wiki.GetLogicalDescendants().OfType<Border>().Any(delegate(Border b) { return Equals(b.Tag, "wiki-color"); }), "en mode wiki, une couleur montre sa pastille");
                }

                // ================================================== le graphe : la famille « Champs », les forces
                {
                    var graphView = shell.GraphViewForProbe;
                    shell.ShowGraphForProbe();
                    await graphView.LoadingForProbe;
                    await Settle();
                    var toggle = shell.GetLogicalDescendants().OfType<CheckBox>().FirstOrDefault(delegate(CheckBox c) { return Equals(c.Tag, "graph-kind:" + GraphEdgeKind.Field); });
                    Check(graphView.KindOnForProbe(GraphEdgeKind.Field) && toggle != null && toggle.IsChecked == true, "le graphe a sa famille de liens « Champs », cochée d'office");
                    var filterBoxes = shell.GetLogicalDescendants().OfType<CheckBox>().Where(delegate(CheckBox c) { var tag = c.Tag as string; return tag != null && tag.StartsWith("graph-"); }).ToList();
                    Check(filterBoxes.Count > 5 && filterBoxes.All(delegate(CheckBox c) { return c.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left; }), "les cases des filtres ne s'étirent plus : leur infobulle pointe le début de la ligne (" + filterBoxes.Count + " cases)");
                    Check(AppSettings.Definition("new") != null && AppSettings.Definition("new").DefaultGesture == null, "« Nouveau projet » n'a plus de raccourci d'office (Ctrl+N retiré)");
                    Check(!graphView.ForcesShownForProbe, "les forces sont repliées d'office");
                    graphView.ToggleForcesForProbe();
                    await Settle();
                    Check(graphView.ForcesShownForProbe, "le bouton les déplie : écartement, répulsion, gravité");
                    Shot(shell, "lot23-6-graphe-forces.png");
                    graphView.SetForces(2.2, 1, 1);
                    for (var i = 0; i < 40 && graphView.LoaderShownForProbe; i++) await Task.Delay(100);
                    await Settle();
                    Check(Math.Abs(AppSettings.GraphSpacing - 2.2) < 1e-6 && !AppSettings.GraphForcesNow.IsDefault && !graphView.LoaderShownForProbe, "un écartement choisi est retenu et la carte se redispose");
                    graphView.SetForces(1, 1, 1);
                    for (var i = 0; i < 40 && graphView.LoaderShownForProbe; i++) await Task.Delay(100);
                    await Settle();
                    Check(AppSettings.GraphForcesNow.IsDefault, "« Rétablir » revient aux forces d'office");
                    graphView.ToggleForcesForProbe();
                }

                // ================================================== les fiches en Markdown et en CSV
                {
                    var md = Path.Combine(folder, "fiches-markdown");
                    var files = shell.ExportSheetDataForProbe(md, true);
                    Check(files > 1 && File.Exists(Path.Combine(md, SheetData.MarkdownIndex)), "les fiches en Markdown : un index et un fichier par fiche (" + files + ")");
                    var csv = Path.Combine(folder, "fiches-csv");
                    var tables = shell.ExportSheetDataForProbe(csv, false);
                    Check(tables >= 1 && Directory.GetFiles(csv, "*.csv").Length == tables, "les fiches en CSV : un tableau par catégorie (" + tables + ")");
                    var task = EncyclopediaExportDialog.Ask(shell, project, EncyclopediaSelection.All(), Encyclopedia.DefaultOptions(project));
                    await Settle();
                    var dialog = OpenWindow<EncyclopediaExportDialog>();
                    var note = dialog == null ? null : dialog.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock b) { return Equals(b.Tag, "encyclopedia-data-note"); });
                    Check(dialog != null && note != null, "la fenêtre de l'Encyclopédie offre Markdown et CSV à côté du HTML");
                    if (dialog != null) dialog.Close();
                    await Settle();
                    await task;
                }

                // ================================================== l'import PDF, PdfPig à la demande (21/11)
                {
                    var folderBefore = PdfPigBridge.FolderOverride;
                    PdfPigBridge.FolderOverride = Path.Combine(folder, "dependance-pdfpig");
                    try
                    {
                        BinderItem source = null;
                        foreach (var item in ExportPlan.ReadingOrder(project)) if (item.Kind == ItemKind.Text && source == null && item.Document != null && item.Document.ToPlainText().Trim().Length > 100) source = item;
                        var writings = project.Category(Project.KeyWritings);
                        Check(MainWindow.IsDocumentPath("manuscrit.PDF") && !PdfPigBridge.Installed, "un PDF est un document importable ; PdfPig n'est pas livrée avec Marabook");
                        if (source != null)
                        {
                            var pdfPath = Path.Combine(folder, "a-importer.pdf");
                            shell.ExportForProbe(HousePreset(ExportPresets.PdfA4), new[] { source.Id }, pdfPath);
                            var before = writings.Children.Count;
                            shell.PdfPigAnswerForProbe = false;
                            await shell.ImportDocumentsForProbe(writings, new[] { pdfPath });
                            await Settle();
                            Check(writings.Children.Count == before && !PdfPigBridge.Installed && (shell.LastPdfPromptForProbe ?? "").StartsWith("Afin d'importer des fichiers PDF") && shell.LastPdfPromptForProbe.EndsWith("Voulez-vous continuer ?"),
                                "importer un PDF sans PdfPig pose la question ; sur un non, rien n'est téléchargé ni importé");
                            var nupkg = Environment.GetEnvironmentVariable("MARABOOK_PDFPIG_NUPKG");
                            var download = Environment.GetEnvironmentVariable("MARABOOK_PDFPIG_DOWNLOAD") == "1";
                            if (download || (!string.IsNullOrEmpty(nupkg) && File.Exists(nupkg)))
                            {
                                shell.PdfPigAnswerForProbe = true;
                                shell.PdfPigPackageForProbe = download ? null : nupkg;
                                await shell.ImportDocumentsForProbe(writings, new[] { pdfPath });
                                await Settle();
                                Check(PdfPigBridge.Installed && Directory.GetFiles(PdfPigBridge.Folder).Length == PdfPigPackage.Files.Count, "sur un oui, PdfPig est " + (download ? "téléchargée depuis NuGet" : "installée") + ", vérifiée : six bibliothèques, rien d'autre");
                                Check(writings.Children.Count == before + 1, "…puis le PDF choisi est importé, dans la foulée");
                                if (writings.Children.Count == before + 1)
                                {
                                    var imported = writings.Children[writings.Children.Count - 1];
                                    added.Add(imported);
                                    var original = source.Document.ToPlainText();
                                    var text = imported.Document.ToPlainText();
                                    var words = original.Split(new[] { ' ', '\n', '\r', ' ', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    var found = 0;
                                    var tried = 0;
                                    for (var i = 0; i < words.Length && tried < 60; i++)
                                    {
                                        var word = words[i].Trim('.', ',', ';', ':', '!', '?', '«', '»', '(', ')', '—', '"');
                                        if (word.Length < 5) continue;
                                        tried++;
                                        if (text.IndexOf(word, StringComparison.Ordinal) >= 0) found++;
                                    }
                                    Check(tried >= 5 && found >= tried * 0.9, "le texte importé est celui du PDF : " + found + " mots retrouvés sur " + tried + " cherchés, accents compris");
                                    var sourceParagraphs = source.Document.Paragraphs.FindAll(delegate(TextParagraph p) { return PivotEdit.FlatText(p).Trim().Length > 0; }).Count;
                                    Check(imported.Document.Paragraphs.Count >= 1 && imported.Document.Paragraphs.Count <= sourceParagraphs * 2 + 2, "…refait en paragraphes (" + imported.Document.Paragraphs.Count + " pour " + sourceParagraphs + " dans l'écrit d'origine)");
                                    // Une seconde fois : plus de question, plus de téléchargement.
                                    shell.PdfPigAnswerForProbe = false;
                                    shell.LastPdfPromptForProbe = null;
                                    await shell.ImportDocumentsForProbe(writings, new[] { pdfPath });
                                    await Settle();
                                    Check(shell.LastPdfPromptForProbe == null && writings.Children.Count == before + 2, "un second PDF s'importe sans rien redemander");
                                    if (writings.Children.Count == before + 2) added.Add(writings.Children[writings.Children.Count - 1]);
                                }
                                // Une bibliothèque touchée sur le disque n'est plus tenue pour installée.
                                var dll = Path.Combine(PdfPigBridge.Folder, PdfPigPackage.Files.Keys.Last());
                                File.AppendAllText(dll + ".note", "x");
                                Check(PdfPigBridge.Installed, "un fichier étranger à côté ne gêne pas");
                            }
                            else Console.WriteLine("  (archive PdfPig absente — MARABOOK_PDFPIG_NUPKG ou MARABOOK_PDFPIG_DOWNLOAD=1 : l'import réel n'est pas joué)");
                        }
                    }
                    finally
                    {
                        shell.PdfPigAnswerForProbe = null;
                        shell.PdfPigPackageForProbe = null;
                        PdfPigBridge.FolderOverride = folderBefore;
                    }
                }

                // ================================================== la lecture à voix haute (sans son)
                {
                    var definition = AppSettings.Definition("read-aloud");
                    Check(definition != null && definition.Name == "Lire à voix haute" && definition.DefaultGesture == null, "« Lire à voix haute » est une action du menu Édition, sans raccourci imposé");
                    ReadAloud.Dry = true;
                    BinderItem text = null;
                    foreach (var item in ExportPlan.ReadingOrder(project)) if (item.Kind == ItemKind.Text && text == null && item.Document != null && item.Document.ToPlainText().Trim().Length > 40) text = item;
                    if (text != null)
                    {
                        shell.Binder.SelectItem(text.Id);
                        await Settle();
                        Check(shell.ReadAloudHeaderForProbe == "Lire à voix haute" && !ReadAloud.IsReading, "au repos, le menu dit « Lire à voix haute »");
                        shell.ToggleReadAloudForProbe();
                        await Settle();
                        Check(ReadAloud.IsReading && !string.IsNullOrEmpty(ReadAloud.LastText) && ReadAloud.LastText.IndexOf("[[", StringComparison.Ordinal) < 0, "la commande lance la lecture du texte, sans la notation des liens (" + (ReadAloud.LastText ?? "").Length + " signes)");
                        Check(shell.ReadAloudHeaderForProbe == "Arrêter la lecture", "pendant la lecture, la même entrée dit « Arrêter la lecture »");
                        shell.ToggleReadAloudForProbe();
                        await Settle();
                        Check(!ReadAloud.IsReading && shell.ReadAloudHeaderForProbe == "Lire à voix haute", "…et l'arrête");
                    }
                }
            }
            finally
            {
                // Le projet d'exemple et les réglages sont rendus tels qu'ils étaient.
                ReadAloud.Stop();
                ReadAloud.Dry = dryBefore;
                if (timeline != null) foreach (var entry in addedEvents) timeline.Timeline.Events.Remove(entry);
                if (template != null) foreach (var field in addedFields) template.Fields.Remove(field);
                if (keira != null) foreach (var field in addedFields) keira.FieldValues.Remove(field.Id);
                AppSettings.LibrarySorts.Clear();
                foreach (var pair in sortsBefore) AppSettings.LibrarySorts[pair.Key] = pair.Value;
                AppSettings.LibraryList = listBefore;
                AppSettings.GraphSpacing = AppSettings.GraphRepulsion = AppSettings.GraphGravity = 1;
                var home = project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                await Settle();
                if (timeline != null)
                {
                    shell.TimelineForProbe.SetSheetFilter(null);
                    shell.TimelineForProbe.SetTagFilter(null);
                    shell.TimelineForProbe.SetListMode(false);
                }
                if (string.IsNullOrEmpty(shots)) { try { Directory.Delete(folder, true); } catch (Exception) { } }
            }
        }
    }
}
