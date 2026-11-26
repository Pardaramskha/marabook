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
    /// <summary>La sonde des petits rattrapages du comparatif (2.0.0,
    /// 20/11/2026) : un fichier par écrit (l'option, le modèle de nom, les
    /// fichiers écrits), les rétroliens d'une fiche (le paper, le wiki, le
    /// clic qui mène à l'événement et au repère), le filtre de frise par
    /// fiche et la vue Liste, la carte et le globe en PNG, les cartes dans
    /// l'ampleur de l'export et dans l'Encyclopédie, le conjugueur. Avec
    /// MARABOOK_PROBE_SHOTS, elle photographie et garde les fichiers.</summary>
    public static partial class Probes
    {
        private static async Task ProbeCatchUp(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- les rattrapages (2.0.0, 20/11)");
            var shots = Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOTS");
            var folder = string.IsNullOrEmpty(shots) ? Path.Combine(Path.GetTempPath(), "marabook-sonde-rattrapages-" + Guid.NewGuid().ToString("N")) : Path.Combine(shots, "rattrapages");
            Directory.CreateDirectory(folder);
            var added = new List<BinderItem>();
            var addedEvents = new List<TimelineEvent>();
            var timeline = Timelines.FindByTitle(project, TimelineSamples.TimelineTitle);
            var keira = FirstTitled(project, "Keira");
            var keiraDocument = keira == null ? null : keira.Document;
            try
            {
                // ================================================== un fichier par écrit
                BinderItem book = null;
                foreach (var item in ExportPlan.ReadingOrder(project)) if (item.Kind == ItemKind.Book && book == null) book = item;
                var scope = shell.ScopeForProbe(book);
                var task = ExportManagerDialog.Ask(shell, project, scope, ExportPresets.WordA4, null);
                await Settle();
                var dialog = OpenWindow<ExportManagerDialog>();
                Check(dialog != null, "le gestionnaire d'export s'ouvre sur le livre");
                if (dialog != null)
                {
                    dialog.TabsForProbe.SelectedItem = dialog.ControlForProbe<TabItem>("export-tab-structure");
                    await Settle();
                    var oneFile = dialog.ControlForProbe<CheckBox>("export-one-file");
                    var pattern = dialog.ControlForProbe<TextBox>("export-file-pattern");
                    var sample = dialog.ControlForProbe<TextBlock>("export-file-sample");
                    Check(oneFile != null && pattern != null && oneFile.IsChecked != true && oneFile.IsEnabled, "onglet Structure : « Un fichier par écrit », décochée d'office, modifiable dans tous les formats");
                    Check(pattern != null && !pattern.IsEnabled && pattern.Text == ExportFiles.DefaultPattern && (dialog.TipForProbe("export-file-pattern") ?? "").StartsWith("Cochez d'abord"), "le modèle de nom attend la case, et son infobulle le dit");
                    Check(!dialog.FooterForProbe.Contains("fichiers"), "le pied n'annonce qu'un fichier");
                    oneFile.IsChecked = true;
                    await Settle();
                    Check(pattern.IsEnabled && (dialog.TipForProbe("export-file-pattern") ?? "").Contains("{titre}") && sample.IsVisible && sample.Text.StartsWith("→ 01 - ") && sample.Text.Contains(".docx"),
                        "case cochée : le modèle se modifie, son infobulle liste les jetons, un exemple montre les noms (" + sample.Text + ")");
                    Check(dialog.FooterForProbe.Contains("(" + dialog.PlanForProbe.Count + " fichiers)") && dialog.FooterForProbe.Contains("modifié"), "le pied annonce autant de fichiers que d'éléments, et le préréglage modifié (" + dialog.FooterForProbe + ")");
                    pattern.Text = "{livre} — {n} — {titre}";
                    await Settle();
                    Check(sample.Text.Contains(" — 01 — "), "le modèle saisi change l'exemple aussitôt (" + sample.Text + ")");
                    Shot(dialog, "rattrapages-1-un-fichier-par-ecrit.png");
                    var request = dialog.RequestForProbe();
                    Check(request.Preset.OneFilePerText && request.Preset.FileNamePattern == "{livre} — {n} — {titre}", "la demande d'export porte l'option et le modèle");
                    dialog.Close();
                    await Settle();
                    await task;

                    foreach (var format in new[] { ExportFormats.Docx, ExportFormats.Pdf, ExportFormats.Epub, ExportFormats.Markdown })
                    {
                        var preset = request.Preset.Clone();
                        preset.Format = format;
                        preset.FileNamePattern = "{n} - {titre}";
                        var per = new ExportRequest { Preset = preset, Meta = ExportMeta.For(project, book, null) };
                        per.ItemIds.AddRange(scope);
                        var plan = ExportPlan.Build(project, per.ItemIds, preset, per.Meta);
                        var files = ExportFiles.Split(project, plan, preset, per.Meta, DateTime.Today);
                        var target = Path.Combine(folder, "par-ecrit-" + format);
                        Directory.CreateDirectory(target);
                        var notes = new List<string>();
                        var faults = new List<string>();
                        var written = await shell.WriteExportFiles(per, files, target, notes, faults);
                        var onDisk = Directory.GetFiles(target, "*." + format).Length;
                        Check(written == files.Count && onDisk == files.Count && files.Count == plan.Count && faults.Count == 0,
                            "un fichier par écrit en " + ExportFormats.Short(format) + " : " + onDisk + " fichiers pour " + plan.Count + " éléments" + (faults.Count > 0 ? " — " + faults[0] : ""));
                        Check(File.Exists(Path.Combine(target, files[0].Name)) && new FileInfo(Path.Combine(target, files[0].Name)).Length > 0 && files[0].Name.StartsWith("01 - "), "…le premier s'appelle « " + files[0].Name + " »");
                        if (format == ExportFormats.Epub) Check(notes.Count > 0 && (notes[0].StartsWith("Structure de l'EPUB vérifiée") || notes[0].StartsWith("Validé par EPUBCheck")), "…chaque EPUB est contrôlé (" + (notes.Count > 0 ? notes[0] : "-") + ")");
                    }
                }

                // Avant un EPUB, les vérifications disent qui le contrôlera.
                task = ExportManagerDialog.Ask(shell, project, scope, ExportPresets.Epub, null);
                await Settle();
                dialog = OpenWindow<ExportManagerDialog>();
                if (dialog != null)
                {
                    var said = dialog.PlanForProbe.Warnings.Any(delegate(ExportWarning w) { return w.Text.Contains("EPUBCheck") || w.Text.Contains("contrôle de structure de Marabook"); });
                    Check(said, "avant un EPUB, les Vérifications disent qui le contrôlera : EPUBCheck s'il est installé, sinon Marabook (" + (EpubCheckTool.Find() == null ? "absent ici" : "trouvé : " + EpubCheckTool.Find().Describe()) + ")");
                    dialog.Close();
                    await Settle();
                    await task;
                }

                // ================================================== les cartes : PNG, ampleur, Encyclopédie
                var mapItem = shell.ImportMapForProbe("Carte des rattrapages", MapImport.Synthetic(1600, 800));
                await Settle();
                await Settle();
                added.Add(mapItem);
                var mapView = shell.MapForProbe;
                Check(mapItem != null && mapView.ShowsItem(mapItem), "une carte d'essai est importée et ouverte");
                if (mapItem != null && keira != null)
                {
                    var marker = new MapMarker { Name = "Tour de Keira", LinkId = keira.Id, X = 400, Y = 300 };
                    mapItem.Map.Markers.Add(marker);
                    mapItem.Map.Zones.Add(new MapZone { Name = "Les Marches", Points = new List<Pos> { new Pos(900, 200), new Pos(1300, 220), new Pos(1250, 600), new Pos(950, 560) } });
                    shell.Binder.SelectItem(mapItem.Id);
                    await Settle();
                    var exportButton = mapView.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button b) { return Equals(b.Tag, "map-export-image"); });
                    Check(exportButton != null && exportButton.IsEffectivelyVisible, "la carte a son bouton « Image… »");
                    var choices = mapView.ExportChoicesForProbe();
                    Check(choices.Count == 2 && choices[0] == "La vue à l'écran" && choices[1] == "La carte entière", "il offre la vue à l'écran ou la carte entière (" + string.Join(" | ", choices.ToArray()) + ")");
                    var whole = mapView.ExportPng(true);
                    int w, h;
                    Check(whole != null && PngSize(whole, out w, out h) && w == 1600 && h == 800, "la carte entière : un PNG à la taille de l'image d'origine (" + (whole == null ? "-" : whole.Length / 1024 + " Ko") + ")");
                    if (whole != null) File.WriteAllBytes(Path.Combine(folder, "carte-entiere.png"), whole);
                    var canvas = mapView.CanvasForProbe;
                    var view = mapView.ExportPng(false);
                    Check(view != null && PngSize(view, out w, out h) && Math.Abs(w - 2 * canvas.Bounds.Width) <= 2 && Math.Abs(h - 2 * canvas.Bounds.Height) <= 2, "la vue à l'écran : un PNG deux fois plus fin que la page");
                    if (view != null) File.WriteAllBytes(Path.Combine(folder, "carte-vue.png"), view);
                    Check(whole != null && view != null && Distinct(whole) && Distinct(view), "les images ne sont pas vides");
                    var stateBefore = string.Join("/", canvas.ViewState.Select(delegate(double d) { return d.ToString("R"); }).ToArray());
                    mapView.ExportPng(true);
                    Check(stateBefore == string.Join("/", canvas.ViewState.Select(delegate(double d) { return d.ToString("R"); }).ToArray()), "exporter ne déplace pas la vue");

                    // Le globe.
                    var world = project.Worlds.Count > 0 ? project.Worlds[0] : null;
                    if (world != null)
                    {
                        mapView.SetWorldForProbe(world.Id, true);
                        mapView.SetViewForProbe(Maps.ViewGlobe);
                        await Settle();
                        await Settle();
                        choices = mapView.ExportChoicesForProbe();
                        Check(choices[0] == "Le globe, tel qu'il est tourné" && choices[1] == "La carte entière (à plat)", "en vue planétaire : le globe tel qu'il est tourné, ou la carte à plat");
                        var globe = mapView.ExportPng(false);
                        Check(globe != null && PngSize(globe, out w, out h) && w > 400 && Distinct(globe), "le globe : un PNG");
                        if (globe != null) File.WriteAllBytes(Path.Combine(folder, "globe.png"), globe);
                        var flat = mapView.ExportPng(true);
                        Check(flat != null && PngSize(flat, out w, out h) && w == 1600 && h == 800 && mapItem.Map.View == Maps.ViewGlobe, "depuis le globe, la carte entière sort à plat — et la vue planétaire reste");
                        mapView.SetViewForProbe(Maps.ViewFlat);
                        mapView.SetWorldForProbe(null, false);
                        await Settle();
                    }

                    // L'ampleur de l'export.
                    task = ExportManagerDialog.Ask(shell, project, new List<string> { mapItem.Id }, ExportPresets.PdfA4, null);
                    await Settle();
                    dialog = OpenWindow<ExportManagerDialog>();
                    if (dialog != null)
                    {
                        Check(dialog.PlanForProbe.Maps.Count == 1 && dialog.PlanForProbe.Summary() == "1 carte", "le gestionnaire d'export compte la carte dans l'ampleur (" + dialog.PlanForProbe.Summary() + ")");
                        Check(dialog.ToggleForProbe("Cartes") || dialog.ToggleForProbe(mapItem.Id), "l'ampleur a sa rubrique « Cartes »");
                        dialog.Close();
                        await Settle();
                        await task;
                    }
                    var pdfPath = Path.Combine(folder, "carte.pdf");
                    var donePlan = shell.ExportForProbe(HousePreset(ExportPresets.PdfA4), new[] { mapItem.Id }, pdfPath);
                    var pdf = Latin1(pdfPath);
                    Check(donePlan.Maps.Count == 1 && pdf.Contains("/Subtype /Image") && Occurrences(pdf, "/Type /Page /Parent") == 1, "une carte dans un PDF : une page-image");
                    var box = pdf.IndexOf("/MediaBox [", StringComparison.Ordinal);
                    Check(box >= 0 && pdf.Substring(box, 40).Contains("841.89"), "…à ses proportions, grand côté d'un A4 (" + (box < 0 ? "-" : pdf.Substring(box, pdf.IndexOf(']', box) - box + 1)) + ")");

                    // L'Encyclopédie.
                    var site = Path.Combine(folder, "encyclopedie");
                    shell.ExportEncyclopediaForProbe(site);
                    var mapsPage = Path.Combine(site, HtmlEncyclopedia.MapsFile);
                    Check(File.Exists(mapsPage) && File.Exists(Path.Combine(site, HtmlEncyclopedia.ImagesFolder, "carte-1.png")), "l'Encyclopédie a sa page « Cartes » et l'image de la carte");
                    if (File.Exists(mapsPage))
                    {
                        var html = File.ReadAllText(mapsPage, Encoding.UTF8);
                        Check(html.Contains("Carte des rattrapages") && html.Contains(">Tour de Keira</a>") && html.Contains("<li>Les Marches</li>"), "…ses lieux y sont, le repère lié mène à la fiche de Keira");
                        int ew, eh;
                        Check(PngSize(File.ReadAllBytes(Path.Combine(site, HtmlEncyclopedia.ImagesFolder, "carte-1.png")), out ew, out eh) && ew == 1600, "…l'image est la carte entière");
                        Check(File.ReadAllText(Path.Combine(site, HtmlEncyclopedia.IndexFile), Encoding.UTF8).Contains("cartes.html"), "…et l'accueil y mène");
                    }
                }

                // ================================================== les rétroliens
                if (keira != null && timeline != null && mapItem != null)
                {
                    var crowned = new TimelineEvent { Title = "Le sacre (sonde)", Start = timeline.Timeline.Events.Count > 0 && timeline.Timeline.Events[0].Start != null ? timeline.Timeline.Events[0].Start : new TimePoint(1, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = keira.Id };
                    timeline.Timeline.Events.Add(crowned);
                    addedEvents.Add(crowned);
                    var citing = new BinderItem { Kind = ItemKind.Text, Title = "Écrit qui cite (sonde)", Document = TextDocument.FromPlainText("Alors [[" + keira.Title + "]] entra.") };
                    var writings = project.Category(Project.KeyWritings);
                    citing.Parent = writings;
                    writings.Children.Add(citing);
                    added.Add(citing);
                    shell.Binder.Rebuild();
                    await Settle();
                    var expected = Backlinks.Of(project, keira);
                    shell.Binder.SelectItem(writings.Id);
                    await Settle();
                    shell.Binder.SelectItem(keira.Id);
                    await Settle();
                    var sheetView = shell.Sheet;
                    if (sheetView.PreviewOnForProbe) { sheetView.HandleFunctionKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.F4 }); await Settle(); }
                    sheetView.ShowGeneralTabForProbe();
                    await Settle();
                    var lines = sheetView.BacklinksForProbe();
                    Check(sheetView.BacklinksShownForProbe && lines.Count > 0, "la fiche de Keira a son paper « Rétroliens »");
                    Check(sheetView.BacklinkRulesForProbe() == lines.Count(delegate(string l) { return l.StartsWith("# "); }) && lines.Any(delegate(string l) { return l.StartsWith("# "); }), "…chaque genre de rétrolien est suivi d'un filet jusqu'au bord (24/11)");
                    Check(lines.Contains("# Écrits") && lines.Contains("# Événements") && lines.Contains("# Cartes"), "rangés par genre : écrits, événements, cartes… (" + string.Join(" / ", lines.Where(delegate(string l) { return l.StartsWith("# "); }).ToArray()) + ")");
                    Check(lines.Any(delegate(string l) { return l.StartsWith("Le sacre (sonde) (assignée"); }) && lines.Any(delegate(string l) { return l.StartsWith("Écrit qui cite (sonde) (lien)"); })
                        && lines.Any(delegate(string l) { return l.StartsWith("Carte des rattrapages (repère « Tour de Keira »)"); }), "chaque ligne dit qui cite, et par quoi");
                    Check(lines.Count(delegate(string l) { return !l.StartsWith("# "); }) == expected.Count, "autant de lignes que de rétroliens (" + expected.Count + " : " + Backlinks.Summary(expected) + ")");
                    Shot(sheetView, "rattrapages-2-retroliens.png");

                    // Le clic mène à l'endroit précis.
                    var toEvent = expected.Find(delegate(Backlink l) { return l.Group == Backlink.Events && l.RefId == crowned.Id; });
                    sheetView.OpenBacklinkForProbe(toEvent);
                    await Settle();
                    await Settle();
                    Check(shell.VisibleView == "timeline" && shell.TimelineForProbe.SelectedEventForProbe != null && shell.TimelineForProbe.SelectedEventForProbe.Id == crowned.Id, "cliquer l'événement ouvre sa frise, l'événement choisi");
                    shell.Binder.SelectItem(keira.Id);
                    await Settle();
                    var toMap = expected.Find(delegate(Backlink l) { return l.Group == Backlink.Maps; });
                    shell.Sheet.OpenBacklinkForProbe(toMap);
                    await Settle();
                    await Settle();
                    Check(shell.VisibleView == "map" && mapView.SelectedMarker != null && mapView.SelectedMarker.Name == "Tour de Keira", "cliquer la carte l'ouvre, le repère choisi");
                    shell.Binder.SelectItem(keira.Id);
                    await Settle();
                    var toText = expected.Find(delegate(Backlink l) { return l.Group == Backlink.Texts && l.Item == citing; });
                    shell.Sheet.OpenBacklinkForProbe(toText);
                    await Settle();
                    Check(shell.VisibleView == "editor" || shell.Binder.SelectedItem == citing, "cliquer l'écrit l'ouvre");

                    // Le mode wiki les montre aussi.
                    var template = project.TemplateOf(keira);
                    Backlink clicked = null;
                    var wiki = SheetWiki.Build(keira, template, project, "", true, delegate { }, delegate { }, delegate { }, delegate(Backlink l) { clicked = l; });
                    var wikiPaper = wiki.GetLogicalDescendants().OfType<StackPanel>().FirstOrDefault(delegate(StackPanel p) { return Equals(p.Tag, "wiki-backlinks"); });
                    Check(wikiPaper != null && wikiPaper.Children.OfType<TextBlock>().Any(delegate(TextBlock b) { return b.Text == "Rétroliens"; }), "le mode wiki a son paper « Rétroliens »");
                    var lone = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche que rien ne cite (sonde)", TemplateId = keira.TemplateId, Document = new TextDocument() };
                    var wikiLone = SheetWiki.Build(lone, template, project, "", true, delegate { }, delegate { }, delegate { });
                    Check(!wikiLone.GetLogicalDescendants().OfType<StackPanel>().Any(delegate(StackPanel p) { return Equals(p.Tag, "wiki-backlinks"); }), "une fiche que rien ne cite n'a pas ce paper");
                }

                // ================================================== frises : le filtre par fiche, la vue Liste
                if (timeline != null && keira != null)
                {
                    shell.Binder.SelectItem(timeline.Id);
                    await Settle();
                    var view = shell.TimelineForProbe;
                    view.SetCalendarMode(false);
                    view.SetListMode(false);
                    view.SelectTabForProbe(-1);
                    view.ShowAllTypesForProbe();
                    view.SetSheetFilter(null);
                    await Settle();
                    var allPlaced = view.PlacedCountForProbe;
                    Check(view.SheetFilterLabelForProbe == "Fiche : toutes", "la frise a son filtre par fiche, à côté des types : « Fiche : toutes »");
                    var menu = view.SheetMenuForProbe();
                    Check(menu.Any(delegate(string m) { return m.StartsWith(keira.Title + "   "); }), "le menu propose les fiches que des événements concernent, avec leur compte (" + string.Join(" | ", menu.ToArray()) + ")");
                    view.SetSheetFilter(keira);
                    await Settle();
                    var concerned = 0;
                    foreach (var entry in view.ListForProbe.Events()) if (Timelines.Concerns(entry, keira)) concerned++;
                    Check(view.SheetFilterLabelForProbe == "Fiche : " + keira.Title && view.PlacedCountForProbe < allPlaced && view.PlacedCountForProbe > 0, "« tout ce qui concerne Keira » : la frise ne garde que ses événements (" + view.PlacedCountForProbe + " sur " + allPlaced + ")");
                    Check(view.PlacedCountForProbe == concerned, "…ceux qui lui sont assignés, sa naissance comprise (" + concerned + ")");
                    Shot(view, "rattrapages-3-filtre-par-fiche.png");

                    // La vue Liste.
                    var modes = view.ModeChoicesForProbe();
                    Check(modes.Count == 3 && modes[2] == "Liste (icône)" && Icons.Has("list-bullets"), "le menu « Mode » offre aussi la Liste (" + string.Join(", ", modes.ToArray()) + ")");
                    view.SetListMode(true);
                    await Settle();
                    var list = view.ListForProbe;
                    Check(view.ListMode && list.IsVisible && !view.SurfaceShownForProbe && view.ModeLabelForProbe == "Liste", "« Liste » remplace la frise et sa minimap ; le bouton dit le mode");
                    Check(list.RowCountForProbe == concerned && list.TitlesForProbe().Contains("Le sacre (sonde)"), "la liste respecte le filtre par fiche (" + list.RowCountForProbe + " lignes)");
                    view.SetSheetFilter(null);
                    await Settle();
                    var total = list.RowCountForProbe;
                    Check(total == Math.Min(TimelineList.MaxRows, allPlaced) && list.CountForProbe.StartsWith(allPlaced + " "), "sans filtre, tous les événements, un par ligne (" + list.CountForProbe + ")");
                    var first = list.RowTextForProbe(0);
                    Check(first != null && first.Split('|').Length == 4, "chaque ligne : la date, le type, le titre, l'élément assigné (" + first + ")");
                    var sorted = true;
                    var shown = Timelines.InOrder(view.System, list.Events());
                    for (var i = 1; i < shown.Count; i++) if (Timelines.StartOf(view.System, shown[i - 1]) > Timelines.StartOf(view.System, shown[i])) sorted = false;
                    Check(sorted && list.TitlesForProbe()[0] == shown[0].Title, "dans l'ordre du temps");
                    Shot(view, "rattrapages-4-liste.png");
                    list.Query = "SACRE";
                    await Settle();
                    Check(list.RowCountForProbe == 1 && list.CountForProbe.StartsWith("1 sur ") && (list.RowTextForProbe(0) ?? "").Contains(keira.Title), "la recherche ignore la casse et réduit la liste ; la fiche assignée se lit sur la ligne (" + list.RowTextForProbe(0) + ")");
                    list.ClickForProbe(0, false);
                    await Settle();
                    Check(view.SelectedEventForProbe != null && view.SelectedEventForProbe.Title == "Le sacre (sonde)" && view.InspectorHasTagForProbe("timeline-selected-card"), "cliquer une ligne choisit l'événement : il paraît au rail");
                    list.Query = "zzz-rien";
                    await Settle();
                    Check(list.RowCountForProbe == 0 && list.GetLogicalDescendants().OfType<TextBlock>().Any(delegate(TextBlock b) { return Equals(b.Tag, "timeline-list-empty"); }), "rien ne répond : la liste le dit");
                    list.Query = "";
                    view.SetCalendarMode(true);
                    await Settle();
                    Check(view.CalendarMode && !view.ListMode && !list.IsVisible, "passer au calendrier quitte la liste");
                    view.SetCalendarMode(false);
                    await Settle();
                    Check(view.SurfaceShownForProbe && !list.IsVisible, "« Frise » ramène la frise");
                }

                // ================================================== le conjugueur
                var definition = AppSettings.Definition("conjugator");
                MenuItem entryMenu = null;
                foreach (var item in shell.GetLogicalDescendants().OfType<MenuItem>())
                    if ((item.Header as string) == "Conjugueur…") { entryMenu = item; break; }
                Check(definition != null && definition.Category == "Édition" && entryMenu != null, "Édition › Conjugueur… existe, et son raccourci se règle dans les Préférences");
                var conjugator = shell.OpenConjugatorForProbe("mangeaient");
                await Settle();
                Check(conjugator != null && conjugator.IsVisible && OpenWindow<ConjugatorDialog>() == conjugator, "le conjugueur s'ouvre, dans sa fenêtre");
                if (conjugator != null)
                {
                    // Grammalecte démarre (Python) : on lui laisse le temps.
                    for (var i = 0; i < 120 && !conjugator.ShownForProbe.Found && !conjugator.HeadForProbe.StartsWith("Le conjugueur ne répond pas"); i++) await Task.Delay(250);
                    await Settle();
                    var verb = conjugator.ShownForProbe;
                    // Sans Python à côté de l'exécutable (un build de développement), Grammalecte ne part pas : la fenêtre doit le dire.
                    var absent = conjugator.HeadForProbe.StartsWith("Le conjugueur ne répond pas");
                    if (absent) Check(conjugator.InfoForProbe.Contains("Grammalecte") && conjugator.MoodSectionsForProbe == 0, "Grammalecte absent de ce build : le conjugueur le dit, sans tableau (" + conjugator.InfoForProbe + ")");
                    else Check(verb.Found && verb.Verb == "manger" && conjugator.HeadForProbe == "manger", "« mangeaient » : le conjugueur retrouve « manger » (" + conjugator.HeadForProbe + " — " + conjugator.InfoForProbe + ")");
                    if (verb.Found)
                    {
                        Check(conjugator.MoodSectionsForProbe == 5 && verb.Moods[0].Title == "Indicatif" && verb.Moods[0].Tenses.Count == 4 && verb.Moods[0].Tenses[1].Forms.Contains("ils mangeaient"), "cinq blocs : indicatif (quatre temps), conditionnel, subjonctif, impératif, infinitif et participes");
                        Check(conjugator.InfoForProbe.Contains("groupe"), "le groupe du verbe est dit (" + conjugator.InfoForProbe + ")");
                        conjugator.SetOptionForProbe("conjugator-compound", true);
                        for (var i = 0; i < 40 && conjugator.ShownForProbe.Moods[0].Tenses[0].Title != "Passé composé"; i++) await Task.Delay(250);
                        Check(conjugator.ShownForProbe.Moods[0].Tenses[0].Title == "Passé composé" && conjugator.ShownForProbe.Moods[0].Tenses[0].Forms[0] == "j’ai mangé", "« Temps composés » : le présent devient le passé composé (" + conjugator.ShownForProbe.Moods[0].Tenses[0].Forms[0] + ")");
                        Shot(conjugator, "rattrapages-5-conjugueur.png");
                        conjugator.SetOptionForProbe("conjugator-compound", false);
                        var again = shell.OpenConjugatorForProbe("suis");
                        for (var i = 0; i < 40 && conjugator.ShownForProbe.Lemmas.Count < 2; i++) await Task.Delay(250);
                        await Settle();
                        Check(again == conjugator && conjugator.LemmaButtonsForProbe == 2 && conjugator.ShownForProbe.Lemmas.Contains("être") && conjugator.ShownForProbe.Lemmas.Contains("suivre"), "« suis » : la même fenêtre propose « suivre » et « être »");
                        shell.OpenConjugatorForProbe("marabout");
                        for (var i = 0; i < 40 && conjugator.ShownForProbe.Found; i++) await Task.Delay(250);
                        await Settle();
                        Check(!conjugator.ShownForProbe.Found && conjugator.HeadForProbe.Contains("n'est pas un verbe connu") && conjugator.MoodSectionsForProbe == 0, "un mot qui n'est pas un verbe : le conjugueur le dit, sans tableau");
                    }
                    conjugator.Close();
                    await Settle();
                }
                Check(Correction.Wiktionary.UrlFor("marabout") == "https://fr.wiktionary.org/wiki/marabout", "le lien du Wiktionnaire mène à la page du mot");
            }
            finally
            {
                // Le projet d'exemple est rendu tel qu'il était.
                if (timeline != null) foreach (var entry in addedEvents) timeline.Timeline.Events.Remove(entry);
                if (keira != null && keiraDocument != null) keira.Document = keiraDocument;
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
                    shell.TimelineForProbe.SetListMode(false);
                }
                if (string.IsNullOrEmpty(shots)) { try { Directory.Delete(folder, true); } catch (Exception) { } }
            }
        }

        /// <summary>Les dimensions d'un PNG, lues dans son en-tête.</summary>
        private static bool PngSize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 24 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return false;
            width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return width > 0 && height > 0;
        }

        /// <summary>Une image qui porte autre chose qu'un aplat : ses octets compressés varient.</summary>
        private static bool Distinct(byte[] png)
        {
            if (png == null || png.Length < 2000) return false;
            var seen = new HashSet<byte>();
            for (var i = 100; i < png.Length; i += 37) seen.Add(png[i]);
            return seen.Count > 40;
        }
    }
}
