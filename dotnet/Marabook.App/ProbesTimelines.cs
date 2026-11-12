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
                Check(view.PlacedCountForProbe == 14, "les quatorze briques sont posées (" + view.PlacedCountForProbe + ")");
                Check(view.MinimapShownForProbe, "la minimap est là");
                var labels = view.RulerLabelsForProbe();
                Check(labels.Count >= 4 && labels.Any(delegate(string l) { return l.StartsWith("E2."); }) && labels.Any(delegate(string l) { return l.StartsWith("E1."); }),
                    "la règle est graduée dans la notation, par ère (" + string.Join(", ", labels.Take(8).ToArray()) + "…)");
                var general = view.InspectorTextsForProbe();
                Check(general.Any(delegate(string s) { return s.Contains("14 événements"); }), "Général du rail : le nombre d'événements");
                Check(general.Any(delegate(string s) { return s.Contains("Durée totale de la temporalité") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("2 154 ans"); }), "…la durée totale de la temporalité (2 154 ans)");
                Check(general.Any(delegate(string s) { return s.Contains("Ère des Phares") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("1 200 ans"); }), "…et la durée de chaque ère");
                Check(shell.InspectorKindForProbe.StartsWith("Frise"), "…sous le genre « Frise — son système » (" + shell.InspectorKindForProbe + ")");
                Shot(view, "frises-2-tout.png");

                // — Un onglet d'ère.
                view.SelectTabForProbe(2);
                await Settle();
                Check(view.TabForProbe == 2 && view.PlacedCountForProbe == 5, "l'onglet de la troisième ère : ses cinq briques (" + view.PlacedCountForProbe + ")");
                general = view.InspectorTextsForProbe();
                Check(general.Any(delegate(string s) { return s.StartsWith("Ère des Grandes Marées") && s.Contains("34 ans"); }), "…et le Général dit la durée de l'ère ouverte (34 ans, en cours)");
                Check(view.RulerLabelsForProbe().Count(delegate(string l) { return l.Contains("E3."); }) >= 3, "…la règle compte dans cette ère (" + string.Join(", ", view.RulerLabelsForProbe().Take(6).ToArray()) + "…)");
                Shot(view, "frises-3-ere.png");
                view.SelectTabForProbe(-1);
                await Settle();

                // — Filtrer par type, zoomer, se déplacer par la minimap.
                view.ToggleTypeForProbe("type-3");
                await Settle();
                Check(view.PlacedCountForProbe == 11, "masquer le type « Personnage » retire ses trois briques (" + view.PlacedCountForProbe + ")");
                view.ToggleTypeForProbe("type-3");
                await Settle();
                Check(view.PlacedCountForProbe == 14, "…et le remontrer les rend");
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
