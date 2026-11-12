using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde des FRISES (1.2.0) sur le projet d'exemple : la racine
    /// « Frises » et sa page, une frise ouverte (onglets des ères, filtres,
    /// zoom, minimap, Général du rail), la fenêtre d'une brique (assignation
    /// en deux temps), « Temporalité par segments », le lien de date (Ctrl+!)
    /// dans une fiche, l'Époque de la bibliothèque et le crâne des morts.</summary>
    public static partial class Probes
    {
        private static async Task ProbeTimelines(MainWindow shell)
        {
            try
            {
                var project = shell.Project;
                var root = project.Category(Project.KeyTimelines);
                var timeline = Timelines.FindByTitle(project, TimelineSamples.TimelineTitle);
                Check(root != null && timeline != null && Timelines.All(project).Count == 2, "le projet d'exemple a sa racine « Frises » et ses deux frises");
                if (root == null || timeline == null) return;
                Check(shell.Binder.TopRowsForProbe().Contains("Frises"), "la Pile montre « Frises » (section Structure)");
                Check(Icons.Has("pile-frises") && Icons.Has("frise") && Icons.Has("death-skull")
                    && ItemIcons.DefaultSvg(root) == "pile-frises" && ItemIcons.DefaultSvg(timeline) == "frise", "les icônes : l'astrolabe pour la racine, le diagramme pour une frise, le crâne");

                // — La page des frises : pas un tableau de tuiles.
                shell.Binder.SelectItem(root.Id);
                await Settle();
                var hub = shell.TimelineHubForProbe;
                Check(hub.IsVisible && hub.TimelineRowsForProbe == 2 && hub.SystemCardsForProbe == 2, "la racine ouvre la page des frises : deux frises, deux systèmes temporels (" + hub.TimelineRowsForProbe + ", " + hub.SystemCardsForProbe + ")");
                Shot(hub, "frises-1-page.png");

                // — Une frise ouverte.
                shell.Binder.SelectItem(timeline.Id);
                await Settle();
                var view = shell.TimelineForProbe;
                Check(view.IsVisible && view.ShowsItem(timeline), "une frise s'ouvre dans sa vue");
                Check(view.TabCountForProbe == 4 && view.TabForProbe == -1, "quatre onglets : « Tout » et les trois ères (" + view.TabCountForProbe + ")");
                Check(view.PlacedCountForProbe == 15, "les quinze briques sont posées : douze de la frise, trois nées des fiches (" + view.PlacedCountForProbe + ")");
                Check(view.MinimapShownForProbe, "la minimap est là");
                var labels = view.RulerLabelsForProbe();
                Check(labels.Count >= 4 && labels.Any(delegate(string l) { return l.StartsWith("E2."); }) && labels.Any(delegate(string l) { return l.StartsWith("E1."); }),
                    "la règle est graduée dans la notation, par ère (" + string.Join(", ", labels.Take(8).ToArray()) + "…)");
                var general = view.InspectorTextsForProbe();
                Check(general.Any(delegate(string s) { return s.Contains("15 événements"); }), "Général du rail : le nombre d'événements");
                Check(general.Any(delegate(string s) { return s.Contains("Durée totale de la temporalité") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("2 154 ans"); }), "…la durée totale de la temporalité (2 154 ans)");
                Check(general.Any(delegate(string s) { return s.Contains("Ère des Phares") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("1 200 ans"); }), "…et la durée de chaque ère");
                Check(shell.InspectorKindForProbe.StartsWith("Frise"), "…sous le genre « Frise — son système » (" + shell.InspectorKindForProbe + ")");
                Shot(view, "frises-2-tout.png");

                // — Un onglet d'ère.
                view.SelectTabForProbe(2);
                await Settle();
                Check(view.TabForProbe == 2 && view.PlacedCountForProbe == 6, "l'onglet de la troisième ère : ses six briques (" + view.PlacedCountForProbe + ")");
                Check(view.ViewStartForProbe > 1999.2 && view.ViewStartForProbe <= 2000 && view.ViewEndForProbe >= 2034 && view.ViewEndForProbe < 2034.8,
                    "…l'ère seule occupe toute la frise, de son début à sa fin (" + view.ViewStartForProbe.ToString("0.##") + " → " + view.ViewEndForProbe.ToString("0.##") + ")");
                view.ZoomForProbe(0.2);
                await Settle();
                Check(view.ViewStartForProbe > 1999.2 && view.ViewStartForProbe <= 2000 && view.ViewEndForProbe >= 2034 && view.ViewEndForProbe < 2034.8, "…et dézoomer n'y fait pas entrer les autres ères");
                view.ZoomForProbe(8);
                await Settle();
                Check(view.ViewStartForProbe > 1999.2 && view.ViewEndForProbe < 2034.8 && view.ViewEndForProbe - view.ViewStartForProbe < 6, "…mais on peut y sur-zoomer (" + (view.ViewEndForProbe - view.ViewStartForProbe).ToString("0.#") + " ans à l'écran)");
                view.FitAll();
                await Settle();
                general = view.InspectorTextsForProbe();
                Check(general.Any(delegate(string s) { return s.StartsWith("Ère des Grandes Marées") && s.Contains("34 ans"); }), "…et le Général dit la durée de l'ère ouverte (34 ans, en cours)");
                Check(view.RulerLabelsForProbe().Count >= 3 && view.RulerLabelsForProbe().All(delegate(string l) { return l.Contains("E3."); }), "…la règle ne compte plus que dans cette ère (" + string.Join(", ", view.RulerLabelsForProbe().Take(6).ToArray()) + "…)");
                Shot(view, "frises-3-ere.png");
                view.SelectTabForProbe(-1);
                await Settle();

                // — Filtrer par type, zoomer, se déplacer par la minimap.
                view.ToggleTypeForProbe("type-3");
                await Settle();
                Check(view.PlacedCountForProbe == 14, "masquer le type « Personnage » retire sa brique (" + view.PlacedCountForProbe + ")");
                view.ToggleTypeForProbe("type-3");
                await Settle();
                Check(view.PlacedCountForProbe == 15, "…et le remontrer la rend");
                // — Lot 2 : les filtres « Naissance » et « Décès », l'escalier, la ligne de vie, pas d'infobulle.
                view.ToggleTypeForProbe(TimelineInfo.BirthTypeId);
                view.ToggleTypeForProbe(TimelineInfo.DeathTypeId);
                await Settle();
                Check(view.PlacedCountForProbe == 12, "masquer « Naissance » et « Décès » retire les trois briques nées des fiches (" + view.PlacedCountForProbe + ")");
                view.ToggleTypeForProbe(TimelineInfo.BirthTypeId);
                view.ToggleTypeForProbe(TimelineInfo.DeathTypeId);
                await Settle();
                Check(!view.SurfaceHasTipForProbe, "survoler la frise ne pose aucune infobulle");
                var lanes = view.LanesForProbe();
                var stairs = true;
                var previousLane = -1;
                foreach (var lane in lanes)
                {
                    if (lane.Key.EndsWith(":deces")) continue; // un décès se pose sur la ligne de sa naissance
                    if (lane.Value != 0 && lane.Value <= previousLane) stairs = false;
                    previousLane = lane.Value;
                }
                Check(stairs && lanes.Any(delegate(KeyValuePair<string, int> l) { return l.Value >= 3; }), "les briques qui se gênent descendent en escalier, de la plus ancienne à la plus récente (" + string.Join(" ", lanes.Select(delegate(KeyValuePair<string, int> l) { return l.Value.ToString(); }).ToArray()) + ")");
                var maraboutItem = project.AllItems().FirstOrDefault(delegate(BinderItem i) { return i.Kind == ItemKind.Sheet && i.Title == "Le marabout"; });
                var bornLane = lanes.Where(delegate(KeyValuePair<string, int> l) { return l.Key == "vie:" + maraboutItem.Id + ":naissance"; }).Select(delegate(KeyValuePair<string, int> l) { return l.Value; }).DefaultIfEmpty(-1).First();
                var diedLane = lanes.Where(delegate(KeyValuePair<string, int> l) { return l.Key == "vie:" + maraboutItem.Id + ":deces"; }).Select(delegate(KeyValuePair<string, int> l) { return l.Value; }).DefaultIfEmpty(-2).First();
                Check(bornLane >= 0 && bornLane == diedLane, "le décès du marabout est sur la ligne de sa naissance (couloir " + bornLane + ")");
                // — Le cadre de l'événement choisi, puis une suppression défaite par Ctrl+Z.
                var lighthouse = timeline.Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == "Le premier phare"; });
                view.SelectForProbe(lighthouse.Id);
                await Settle();
                Check(view.InspectorHasTagForProbe("timeline-selected-card"), "Général du rail : l'événement choisi est dans un cadre arrondi");
                Shot(view.Inspector, "frises-13-rail-evenement-choisi.png");
                view.DeleteSelectedForProbe();
                await Settle();
                Check(view.PlacedCountForProbe == 14 && timeline.Timeline.Events.Count == 11, "retirer une brique : quatorze à l'écran");
                shell.HistoryForProbe.Undo();
                await Settle();
                Check(view.PlacedCountForProbe == 15 && timeline.Timeline.Events.Exists(delegate(TimelineEvent e) { return e.Title == "Le premier phare"; }), "Ctrl+Z la rend");
                // — Renommer la frise : son écran suit tout de suite.
                shell.HistoryForProbe.Run(new Marabook.History.RenameItemAction(timeline, "Annales du Littoral", project));
                await Settle();
                Check(view.TitleForProbe == "Annales du Littoral", "frise renommée : le titre de son écran suit (" + view.TitleForProbe + ")");
                Check(view.PlacedCountForProbe == 15, "…et les naissances liées suivent le nouveau nom");
                shell.HistoryForProbe.Undo();
                await Settle();
                Check(view.TitleForProbe == TimelineSamples.TimelineTitle, "…et revient avec Ctrl+Z");
                view.FitAll();
                await Settle();
                Shot(view, "frises-14-escalier-et-vies.png");
                var scale = view.ScaleForProbe;
                view.ZoomForProbe(6);
                await Settle();
                Check(view.ScaleForProbe > scale * 5.9, "le zoom grossit l'axe (× " + (view.ScaleForProbe / scale).ToString("0.0") + ")");
                var before = view.ViewStartForProbe;
                view.MinimapClickForProbe(0.97);
                await Settle();
                Check(view.ViewStartForProbe > before + 100, "cliquer dans la minimap déplace la vue (de " + before.ToString("0") + " à " + view.ViewStartForProbe.ToString("0") + ")");
                Shot(view, "frises-4-zoom-minimap.png");

                // — La fenêtre d'une brique : l'assignation en deux temps.
                var draft = new TimelineEvent { Start = new TimePoint(2029), TypeId = "type-4" };
                var eventTask = TimelineEventDialog.Ask(shell, project, view.System, timeline.Timeline, draft, true);
                await Settle();
                var eventDialog = OpenWindow<TimelineEventDialog>();
                Check(eventDialog != null, "la fenêtre d'un nouvel événement s'ouvre");
                if (eventDialog != null)
                {
                    Check(eventDialog.LinkKindCountForProbe == 7 && !eventDialog.LinkItemEnabledForProbe, "assigner : d'abord la SORTE (rien, fiche, écrit, livre, plan, carte, document) — l'élément attend");
                    var keira = FirstTitled(project, "Keira");
                    eventDialog.SetLinkForProbe(TimelineLinks.Sheet, keira == null ? null : keira.Id);
                    await Settle();
                    Check(eventDialog.LinkItemEnabledForProbe && eventDialog.LinkItemCountForProbe == TimelineLinks.ItemsOf(project, TimelineLinks.Sheet).Count,
                        "…puis l'élément, parmi les seules fiches (" + eventDialog.LinkItemCountForProbe + ")");
                    eventDialog.FillForProbe("Le départ (sonde)", "n'importe quoi", null);
                    eventDialog.AcceptForProbe();
                    await Settle();
                    Check(eventDialog.IsVisible && eventDialog.ProblemShownForProbe, "une date illisible est refusée, la fenêtre reste");
                    eventDialog.FillForProbe("Le départ (sonde)", "5 Brisants E3.30", "9 Brisants E3.30");
                    await Settle();
                    Shot(eventDialog, "frises-5-evenement.png");
                    eventDialog.AcceptForProbe();
                }
                var made = await eventTask;
                Check(made != null && !made.Deleted && made.Event.IsSpan && made.Event.Start.Day == 5 && made.Event.End.Day == 9
                    && made.Event.LinkKind == TimelineLinks.Sheet, "l'événement sur la durée est rendu : du 5 au 9 Brisants E3.30, assigné à une fiche");

                // — « Temporalité par segments ».
                var systemTask = TimeSystemDialog.Edit(shell, project, view.System, false);
                await Settle();
                var systemDialog = OpenWindow<TimeSystemDialog>();
                Check(systemDialog != null && (systemDialog.Title ?? "").StartsWith("Temporalité par segments"), "la fenêtre « Temporalité par segments » s'ouvre");
                if (systemDialog != null)
                {
                    Check(systemDialog.ExampleForProbe.Contains("E2.880") && systemDialog.ExampleForProbe.Contains("E1.800 puis E2.1"), "chaque segment repart de zéro : E1.800 puis E2.1 (" + systemDialog.ExampleForProbe + ")");
                    systemDialog.TryForProbe("12 Sel E3.8");
                    await Settle();
                    Check(systemDialog.TryEchoForProbe.Contains("Ère des Grandes Marées") && systemDialog.TryEchoForProbe.Contains("jusant") == false || systemDialog.TryEchoForProbe.Contains("12 Sel E3.8"), "« essayer une date » relit la notation (" + systemDialog.TryEchoForProbe + ")");
                    Shot(systemDialog, "frises-6-temporalite-par-segments.png");
                    systemDialog.SetCumulativeForProbe(true);
                    await Settle();
                    Check(systemDialog.ExampleForProbe.Contains("E1.800 puis E2.801"), "années cumulatives entre segments : E1.800 puis E2.801 (" + systemDialog.ExampleForProbe + ")");
                    systemDialog.SetCumulativeForProbe(false);
                    systemDialog.SetYearPatternForProbe("an {a}");
                    await Settle();
                    Check(systemDialog.ProblemShownForProbe, "une notation qui ne dit pas le segment est signalée");
                    systemDialog.CancelForProbe();
                }
                Check(await systemTask == null && view.System.YearPattern == "E{s}.{a}" && !view.System.Cumulative, "annuler laisse le système tel quel");

                // — Lot 2 : changer la durée d'une ère. Les briques gardent leur date ; celles qui n'en ont plus attendent.
                var longer = view.System.Clone();
                longer.Segments[0].Length = 7500;
                shell.HistoryForProbe.Run(new Marabook.History.TimeSystemEditAction(project, TimeSystemChange.Build(project, view.System, longer), false));
                await Settle();
                var book = timeline.Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == "Le livre des marées"; });
                Check(ReferenceEquals(view.System, longer) && view.System.Format(book.Start) == "E2.905" && book.Start.Year == 8404 && view.PendingCountForProbe == 0,
                    "l'ère 1 portée à 7 500 ans : « Le livre des marées » reste en E2.905 et suit sur l'axe (année " + book.Start.Year + ")");
                Check(view.PlacedCountForProbe == 15, "…toutes les briques sont replacées, naissances comprises (" + view.PlacedCountForProbe + ")");
                Shot(view, "frises-15-ere-de-7500-ans.png");
                shell.HistoryForProbe.Undo();
                await Settle();
                Check(view.System.Segments[0].Length == 800 && view.PendingCountForProbe == 0, "Ctrl+Z rend l'ère de 800 ans");
                var shorter = view.System.Clone();
                shorter.Segments[1].Length = 1000;
                shell.HistoryForProbe.Run(new Marabook.History.TimeSystemEditAction(project, TimeSystemChange.Build(project, view.System, shorter), false));
                await Settle();
                Check(view.PendingCountForProbe == 1 && view.InspectorHasTagForProbe("timeline-problems") && view.InspectorHasTagForProbe("timeline-pending") && view.InspectorHasTagForProbe("timeline-problem"),
                    "l'ère 2 raccourcie : « La Grande Tempête » attend dans « Problèmes › Événements en attente » du rail (" + view.PendingCountForProbe + ")");
                Check(view.PlacedCountForProbe == 13, "…elle a quitté l'axe, et la naissance devenue illisible aussi (" + view.PlacedCountForProbe + ")");
                Shot(view.Inspector, "frises-16-rail-problemes.png");
                var waiting = timeline.Timeline.Events.Find(delegate(TimelineEvent e) { return e.IsPending; });
                var pendingTask = TimelineEventDialog.Ask(shell, project, view.System, timeline.Timeline, waiting.Clone(), false);
                await Settle();
                var pendingDialog = OpenWindow<TimelineEventDialog>();
                Check(pendingDialog != null && pendingDialog.GetVisualDescendants().OfType<Border>().Any(delegate(Border b) { return Equals(b.Tag, "event-pending-notice"); }),
                    "rouvrir une brique en attente : la fenêtre dit ce qui ne se lit plus et redemande une date");
                if (pendingDialog != null)
                {
                    Shot(pendingDialog, "frises-17-evenement-en-attente.png");
                    pendingDialog.FillForProbe("La Grande Tempête", "E2.988", "E2.1000");
                    pendingDialog.AcceptForProbe();
                }
                var redated = await pendingTask;
                Check(redated != null && !redated.Deleted && !redated.Event.IsPending && view.System.Format(redated.Event.Start) == "E2.988", "une nouvelle date la sort de l'attente");
                shell.HistoryForProbe.Undo();
                await Settle();
                Check(view.PendingCountForProbe == 0 && view.PlacedCountForProbe == 15 && !view.InspectorHasTagForProbe("timeline-problems"), "Ctrl+Z : plus rien en attente, la section Problèmes disparaît");

                // — Le lien de date (Ctrl+!) dans une fiche : la notation sélectionnée est lue d'office.
                var sheet = FirstTitled(project, "Keira");
                if (sheet != null)
                {
                    shell.Binder.SelectItem(sheet.Id);
                    await Settle();
                    var sheetView = shell.Sheet;
                    var box = sheetView.BodyBox;
                    var kept = box.Text;
                    box.Text = "Le livre des marées date de E2.905, dit-on.";
                    box.Focus();
                    Ui.Select(box, 28, 6); // « E2.905 »
                    shell.InsertTimeLinkPublic();
                    await Settle();
                    var linkDialog = OpenWindow<TimeLinkDialog>();
                    Check(linkDialog != null, "fiche : Ctrl+! ouvre la fenêtre du lien de date");
                    if (linkDialog != null)
                    {
                        Check(linkDialog.DateForProbe == "E2.905" && linkDialog.DateReadForProbe && linkDialog.TimelineIndexForProbe == 0,
                            "…la notation sélectionnée y est déjà, lue, sur sa frise (« " + linkDialog.DateForProbe + " »)");
                        Shot(linkDialog, "frises-7-lien-de-date.png");
                        linkDialog.AcceptForProbe();
                        await Settle();
                        await Settle();
                    }
                    Check(box.Text == "Le livre des marées date de [[date:Chronique du Littoral@E2.905|E2.905]], dit-on.", "…et le lien remplace la sélection (« " + box.Text + " »)");
                    await Settle();
                    Check(shell.LinksPanelTextsForProbe().Any(delegate(string s) { return s.Contains("E2.905") && s.Contains("Chronique du Littoral"); }), "le panneau Liens du rail dit la date et sa frise");
                    // Suivre le lien : la frise s'ouvre.
                    shell.FollowLinkForProbe("date:Chronique du Littoral@E2.905");
                    await Settle();
                    await Settle();
                    Check(view.IsVisible && view.ShowsItem(timeline), "cliquer un lien de date ouvre sa frise");
                    Shot(view, "frises-8-lien-suivi.png");
                    shell.Binder.SelectItem(sheet.Id);
                    await Settle();
                    box.Text = kept;
                    await Settle();
                }

                // — L'Époque de la bibliothèque des fiches.
                var sheetsRoot = project.Category(Project.KeySheets);
                shell.Binder.SelectItem(sheetsRoot.Id);
                await Settle();
                var library = shell.SheetLibraryForProbe;
                Check(library.IsVisible && !library.EpochOnForProbe, "la bibliothèque a son bouton « Époque », éteint");
                shell.ToggleEpochForProbe(false);
                await Settle();
                Check(library.EpochOnForProbe && (library.EpochChipForProbe ?? "").StartsWith("E3.30"), "allumée : la puce dit la date et la frise (" + library.EpochChipForProbe + ")");
                var keiraSheet = FirstTitled(project, "Keira");
                var maraboutSheet = project.AllItems().FirstOrDefault(delegate(BinderItem i) { return i.Kind == ItemKind.Sheet && i.Title == "Le marabout"; });
                Func<BinderItem, List<string>> cardTexts = delegate(BinderItem item)
                {
                    var card = item == null ? null : library.CardForProbe(item.Id);
                    return card == null ? new List<string>() : card.GetVisualDescendants().OfType<TextBlock>().Select(delegate(TextBlock b) { return b.Text ?? ""; }).ToList();
                };
                Func<BinderItem, string, bool> tagged = delegate(BinderItem item, string tag)
                {
                    var card = item == null ? null : library.CardForProbe(item.Id);
                    return card != null && card.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return Equals(c.Tag, tag); });
                };
                Check(cardTexts(keiraSheet).Contains("22 ans") && !tagged(keiraSheet, "epoch-dimmed"), "en E3.30, la tuile de Keira dit « 22 ans »");
                Check(cardTexts(maraboutSheet).Contains("89 ans") && !tagged(maraboutSheet, "epoch-skull"), "…celle du marabout « 89 ans », sans crâne");
                Shot(library, "frises-9-epoque-E3-30.png");
                project.Epoch.Notation = "E3.40";
                library.RefreshEpoch();
                await Settle();
                Check(tagged(maraboutSheet, "epoch-dimmed") && tagged(maraboutSheet, "epoch-skull") && cardTexts(maraboutSheet).Any(delegate(string s) { return s.StartsWith("Mort·e à 93 ans"); }),
                    "en E3.40, la tuile du marabout est grisée, porte le crâne et dit son âge de mort");
                Check(cardTexts(keiraSheet).Contains("32 ans"), "…Keira a 32 ans");
                Check(Epochs.AgeNote(project, keiraSheet) == "(32 ans en E3.40)", "vue wiki : à côté de l'âge, « (32 ans en E3.40) » (" + Epochs.AgeNote(project, keiraSheet) + ")");
                Shot(library, "frises-10-epoque-E3-40.png");
                project.Epoch.Notation = "E3.2";
                library.RefreshEpoch();
                await Settle();
                Check(tagged(keiraSheet, "epoch-dimmed") && !tagged(keiraSheet, "epoch-skull") && cardTexts(keiraSheet).Contains("Naît dans 6 ans"), "en E3.2, Keira n'est pas née : grisée, sans crâne");
                project.Epoch.Notation = "E3.40";
                if (maraboutSheet != null)
                {
                    shell.Binder.SelectItem(maraboutSheet.Id);
                    await Settle();
                    Check(shell.Sheet.EpochSkullShownForProbe && (shell.Sheet.EpochNoteForProbe ?? "").Contains("Mort·e à 93 ans"), "la fiche d'un mort porte le crâne dans son titre (" + shell.Sheet.EpochNoteForProbe + ")");
                    Shot(shell.Sheet, "frises-11-fiche-mort.png");
                }
                if (keiraSheet != null)
                {
                    shell.Binder.SelectItem(keiraSheet.Id);
                    await Settle();
                    Check(!shell.Sheet.EpochSkullShownForProbe && (shell.Sheet.EpochNoteForProbe ?? "").Contains("32 ans"), "…celle d'une vivante, son âge, sans crâne (" + shell.Sheet.EpochNoteForProbe + ")");
                }
                // L'époque s'éteint, tout revient.
                shell.Binder.SelectItem(sheetsRoot.Id);
                await Settle();
                shell.ToggleEpochForProbe(false);
                await Settle();
                Check(!library.EpochOnForProbe && !tagged(maraboutSheet, "epoch-dimmed") && !cardTexts(keiraSheet).Contains("32 ans"), "éteinte : plus d'âge ni de tuile grisée");
                project.Epoch.Notation = "E3.30";

                // — Le thème sombre.
                shell.Binder.SelectItem(timeline.Id);
                await Settle();
                var wasDark = Chrome.Dark;
                Chrome.Toggle(true);
                App.ApplyTheme(true);
                view.OnThemeChanged();
                await Settle();
                Shot(view, "frises-12-sombre.png");
                Chrome.Toggle(wasDark);
                App.ApplyTheme(wasDark);
                view.OnThemeChanged();
                await Settle();
            }
            catch (Exception error)
            {
                Check(false, "la sonde des frises va au bout (" + error.GetType().Name + " : " + error.Message + ")");
                Console.Error.WriteLine(error);
            }
        }
    }
}
