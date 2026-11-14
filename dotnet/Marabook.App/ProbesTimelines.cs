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
                Check(shell.Binder.TopRowsForProbe().Contains("Astrolabe") && !shell.Binder.TopRowsForProbe().Contains("Frises"), "la Pile montre « Astrolabe » (ex-« Frises », section Structure)");
                Check(Icons.Has("pile-frises") && Icons.Has("frise") && Icons.Has("death-skull")
                    && ItemIcons.DefaultSvg(root) == "pile-frises" && ItemIcons.DefaultSvg(timeline) == "frise", "les icônes : l'astrolabe pour la racine, le diagramme pour une frise, le crâne");

                // — La page des frises : pas un tableau de tuiles.
                shell.Binder.SelectItem(root.Id);
                await Settle();
                var hub = shell.TimelineHubForProbe;
                Check(hub.IsVisible && hub.TimelineRowsForProbe == 2 && hub.SystemCardsForProbe == 2, "la racine ouvre la page des frises : deux frises, deux systèmes temporels (" + hub.TimelineRowsForProbe + ", " + hub.SystemCardsForProbe + ")");
                Check(hub.GetVisualDescendants().OfType<TextBlock>().Any(delegate(TextBlock b) { return b.Text == "Astrolabe" && b.FontSize >= 16; }), "la page s'appelle « Astrolabe »");
                Check(hub.GetVisualDescendants().OfType<Button>().Count(delegate(Button b) { return Equals(b.Tag, "hub-system-calculator"); }) == 2, "chaque tuile de système a son bouton « Calculateur de dates »");
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
                Check(general.Any(delegate(string s) { return s.Contains("Années dans les segments") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("2 034 ans"); }),
                    "…les années dans les segments seuls, sans les années négatives (2 034 ans : 800 + 1 200 + 34)");
                Check(general.Any(delegate(string s) { return s.Contains("Ère des Phares") && s.Replace(' ', ' ').Replace(' ', ' ').Contains("1 200 ans"); }), "…et la durée de chaque ère");
                Check(shell.InspectorKindForProbe.StartsWith("Frise"), "…sous le genre « Frise — son système » (" + shell.InspectorKindForProbe + ")");
                Shot(view, "frises-2-tout.png");

                // — Lot 4 (14/11) : le repère du survol.
                {
                    var byTitle = new Func<string, string>(delegate(string title) { var found = view.Item.Timeline.Events.Find(delegate(TimelineEvent e) { return e.Title == title; }); return found == null ? "?" : found.Id; });
                    Check(view.PointedLabelForProbe == null && view.PointedEventsForProbe().Count == 0, "sans curseur sur la frise : ni repère ni brique éclairée");
                    view.PointAtForProbe(800 + 904.5);
                    await Settle();
                    Check(view.PointedLabelForProbe == "E2.905", "le curseur sur la frise dit l'année pointée (" + view.PointedLabelForProbe + ")");
                    Check(view.PointedEventsForProbe().Count == 1 && view.PointedEventsForProbe()[0] == byTitle("Le livre des marées"), "…et l'événement de cette année s'éclaire");
                    Shot(view, "frises-23-survol.png");
                    view.PointAtForProbe(800 + 440.2);
                    await Settle();
                    Check(view.PointedLabelForProbe == "E2.441" && view.PointedEventsForProbe().Contains(byTitle("Les Quarante Hivers")), "une année au milieu d'une période éclaire la période (" + view.PointedLabelForProbe + ")");
                    view.PointAtForProbe(800 + 300.5);
                    await Settle();
                    Check(view.PointedLabelForProbe == "E2.301" && view.PointedEventsForProbe().Count == 0, "une année sans rien : le repère seul");
                    Check(!view.SurfaceHasTipForProbe, "…une étiquette dessinée, pas une infobulle à flèche");
                    view.SelectTabForProbe(2);
                    await Settle();
                    view.PointAtForProbe(2000 + 20.5);
                    await Settle();
                    Check(view.PointedBandWidthForProbe > 8, "le repère couvre toute la largeur de l'année pointée (" + view.PointedBandWidthForProbe.ToString("0.#") + " px dans l'onglet d'une ère)");
                    Shot(view, "frises-27-survol-annee.png");
                    view.SelectTabForProbe(-1);
                    await Settle();
                    view.PointAtForProbe(null);
                    await Settle();
                }

                // — Lot 4 puis 5 : le calculateur de dates, UN onglet du rail (à la demande, épinglable).
                {
                    var panelBefore = AppSettings.RightPanel;
                    var pinnedBefore = AppSettings.CalculatorPinned;
                    shell.PinCalculatorForProbe(false);
                    await Settle();
                    Check(!shell.HasCalculatorForProbe && !shell.RailHasTabForProbe(RightPanel.Calculator), "tant qu'on ne l'a pas demandé, le rail n'a pas d'onglet Calculateur");
                    TimePoint clicked;
                    view.System.TryParse("14 Noroît E3.30", out clicked);
                    var menu = view.MenuForProbe(clicked);
                    Check(menu.Items.OfType<MenuItem>().Any(delegate(MenuItem m) { return Equals(m.Tag, "timeline-menu-calculator"); }), "clic droit sur un point de la frise : « Calculateur de dates… »");
                    view.RequestCalculatorForProbe(clicked);
                    await Settle();
                    var calculator = shell.CalculatorPanelForProbe;
                    Check(shell.HasCalculatorForProbe && shell.RailHasTabForProbe(RightPanel.Calculator) && AppSettings.RightPanel == RightPanel.Calculator && calculator.IsEffectivelyVisible,
                        "demandé : l'onglet paraît au rail et le Calculateur prend la colonne");
                    Check(shell.RailTabRankForProbe(RightPanel.Calculator) == shell.RailTabRankForProbe(RightPanel.Notebook) + 1, "…sous le Carnet");
                    Check(calculator.FromForProbe == "14 Noroît E3.30" && calculator.SystemForProbe == view.System.Name && calculator.SystemChoicesForProbe == 2, "il s'ouvre à la date pointée, sur le système de la frise (" + calculator.FromForProbe + ")");
                    Check(shell.OwnedWindows.OfType<Window>().All(delegate(Window w) { return !(w.Title ?? "").StartsWith("Calculateur"); }), "…et ce n'est plus une fenêtre : un seul calculateur");
                    calculator.SetSumForProbe("1 Aubemer E3.1", false, "", "", "", "36");
                    await Settle();
                    Check(calculator.SumForProbe == "1 Vent-Neuf E3.1", "1 Aubemer + 36 jours = 1 Vent-Neuf (" + calculator.SumForProbe + ")");
                    calculator.SetSumForProbe("1 Aubemer E3.1", false, "2", "1", "1", "3");
                    await Settle();
                    Check(calculator.SumForProbe == "10 Vent-Neuf E3.3", "+ 2 ans, 1 mois, 1 semaine (6 jours) et 3 jours = 10 Vent-Neuf E3.3 (" + calculator.SumForProbe + ")");
                    Check(calculator.SumDetailForProbe.Contains("Ère des Grandes Marées") && calculator.SumDetailForProbe.Contains("jour 46 de l'année"), "…avec le jour de la semaine, l'ère et le rang du jour (" + calculator.SumDetailForProbe + ")");
                    calculator.SetSumForProbe("1 Aubemer E3.1", true, "", "", "", "1");
                    await Settle();
                    Check(calculator.SumForProbe == "36 Longue-Nuit E2.1200", "retirer un jour au premier jour d'une ère : la veille, dans l'ère d'avant (" + calculator.SumForProbe + ")");
                    calculator.SetBetweenForProbe("3 Brisants E3.30", "20 Noroît E3.30");
                    await Settle();
                    Check(calculator.BetweenForProbe == "4 mois et 17 jours" && calculator.BetweenDetailForProbe.Contains("161 jours") && calculator.BetweenDetailForProbe.Contains("26 semaines et 5 jours"),
                        "du 3 Brisants au 20 Noroît : 4 mois et 17 jours, 161 jours (" + calculator.BetweenForProbe + " — " + calculator.BetweenDetailForProbe + ")");
                    calculator.SetSumForProbe("n'importe quoi", false, "", "", "", "1");
                    await Settle();
                    Check(calculator.SumForProbe == "—", "une date illisible : pas de résultat");
                    calculator.SetSumForProbe("1 Aubemer E3.1", false, "2", "1", "1", "3");
                    await Settle();
                    Shot(calculator, "frises-24-calculateur.png");
                    // Demandé une seconde fois : le même panneau, à la nouvelle date.
                    view.RequestCalculatorForProbe(new TimePoint(2000 + 7, 4, 12));
                    await Settle();
                    Check(ReferenceEquals(calculator, shell.CalculatorPanelForProbe) && calculator.FromForProbe == "12 Sel E3.8", "redemandé ailleurs : le même calculateur, à la nouvelle date (" + calculator.FromForProbe + ")");
                    // Fermé sans épingle : l'onglet s'en va. Épinglé : il reste.
                    shell.CloseCalculatorForProbe();
                    await Settle();
                    Check(!shell.HasCalculatorForProbe && !shell.RailHasTabForProbe(RightPanel.Calculator) && AppSettings.RightPanel != RightPanel.Calculator, "fermé sans épingle : l'onglet quitte le rail");
                    view.RequestCalculatorForProbe(clicked);
                    await Settle();
                    shell.PinCalculatorForProbe(true);
                    await Settle();
                    shell.CloseCalculatorForProbe();
                    await Settle();
                    Check(calculator.PinnedForProbe && shell.HasCalculatorForProbe && shell.RailHasTabForProbe(RightPanel.Calculator), "épinglé puis fermé : l'onglet reste au rail");
                    shell.PinCalculatorForProbe(pinnedBefore);
                    shell.SetRightPanelForProbe(panelBefore);
                    await Settle();
                }

                // — Lot 4 : la vue Calendrier et ses orphelins.
                {
                    var calendar = view.CalendarForProbe;
                    Func<string, TimePoint> date = delegate(string notation) { TimePoint point; view.System.TryParse(notation, out point); return point; };
                    Check(view.ModeLabelForProbe == "Frise" && view.ModeChoicesForProbe().Count == 2 && view.ModeChoicesForProbe()[0] == "Frise (icône)" && view.ModeChoicesForProbe()[1] == "Calendrier (icône)"
                        && Icons.Has("frise") && Icons.Has("fiche-evenement"), "le menu « Mode » offre Frise et Calendrier, chacun avec son icône (" + string.Join(", ", view.ModeChoicesForProbe().ToArray()) + ")");
                    view.SetCalendarMode(true);
                    await Settle();
                    Check(view.CalendarMode && calendar.IsVisible && !view.SurfaceShownForProbe && view.ModeLabelForProbe == "Calendrier", "« Calendrier » remplace la frise et sa minimap ; le bouton dit le mode");
                    Check(calendar.TitleForProbe == "Aubemer · E2.1" && calendar.EventsOfDayForProbe(1).Contains("Serment des Gardiens"), "il s'ouvre au premier événement daté au jour (" + calendar.TitleForProbe + ")");
                    Check(calendar.ColumnsForProbe == 6 && calendar.DayCellsForProbe == 36, "une colonne par jour de la semaine (6), une case par jour du mois (36)");
                    Check(calendar.OrphansForProbe("calendar-orphans-month").Count == 0 && calendar.OrphansForProbe("calendar-orphans-elsewhere").Contains("Guerre des Brumes")
                        && calendar.OrphansForProbe("calendar-orphans-elsewhere").Contains("Le livre des marées"), "les événements datés à l'année sont des orphelins, rangés « ailleurs dans la frise »");
                    calendar.JumpForProbe(1);
                    await Settle();
                    Check(calendar.TitleForProbe == "Aubemer · E2.430" && calendar.OrphansForProbe("calendar-orphans-year").Contains("Les Quarante Hivers"), "« Événement suivant » mène à l'année du prochain ; l'orphelin y est « cette année » (" + calendar.TitleForProbe + ")");
                    view.GoTo(date("14 Noroît E3.30"));
                    await Settle();
                    Check(calendar.TitleForProbe == "Noroît · E3.30", "« Aller à » déplace le calendrier (" + calendar.TitleForProbe + ")");
                    Check(calendar.EventsOfDayForProbe(14).Contains("La grande marée") && calendar.EventsOfDayForProbe(14).Contains("Le marabout et la mer"), "le 14 Noroît : l'événement du jour et la période qui le couvre");
                    Check(calendar.EventsOfDayForProbe(20).Contains("Le marabout et la mer") && calendar.EventsOfDayForProbe(21).Count == 0, "la période s'arrête à son dernier jour (le 20)");
                    Check(calendar.ColumnOfDayForProbe(14) == view.System.WeekDays.IndexOf(view.System.WeekDayOf(date("14 Noroît E3.30"))), "chaque jour tombe dans la colonne de son jour de semaine");
                    Check(calendar.OrphansForProbe("calendar-orphans-year").Count == 0 && calendar.OrphansForProbe("calendar-orphans-month").Count == 0, "rien d'orphelin en E3.30");
                    Shot(view, "frises-25-calendrier.png");
                    calendar.ShiftMonthForProbe(1);
                    await Settle();
                    Check(calendar.TitleForProbe == "Longue-Nuit · E3.30" && calendar.DayCellsForProbe == 41, "le mois suivant : Longue-Nuit, 41 jours cette année bissextile (" + calendar.DayCellsForProbe + ")");
                    calendar.ShiftMonthForProbe(1);
                    await Settle();
                    Check(calendar.TitleForProbe == "Aubemer · E3.31", "…puis l'année suivante (" + calendar.TitleForProbe + ")");
                    view.GoTo(date("12 Sel E3.8"));
                    await Settle();
                    Check(calendar.EventsOfDayForProbe(12).Any(delegate(string title) { return title.Contains("Keira"); }), "une naissance née d'une fiche a sa case (12 Sel E3.8)");
                    view.GoTo(date("E2.1141"));
                    await Settle();
                    Check(calendar.OrphansForProbe("calendar-orphans-year").Any(delegate(string title) { return title.Contains("marabout"); }), "…et une naissance datée à l'année est orpheline");
                    Shot(view, "frises-26-calendrier-orphelins.png");
                    view.SetCalendarMode(false);
                    await Settle();
                    Check(!view.CalendarMode && view.SurfaceShownForProbe && !calendar.IsVisible, "« Frise » ramène la frise");
                }

                // — Lot 5 (14/11) : un MONDE — sa taille, ses saisons, ses lunes — et ce qu'il donne au calendrier.
                {
                    Func<string, string> plain = delegate(string text) { return (text ?? "").Replace('\u202f', ' ').Replace('\u00a0', ' '); };
                    var hubNow = shell.TimelineHubForProbe;
                    Check(project.Worlds.Count == 0 && !view.InspectorHasTagForProbe("timeline-world"), "un projet sans monde : rien au Général de la frise");
                    var worldTask = WorldDialog.Edit(shell, project, shell.NewWorldDraftForProbe(), true);
                    await Settle();
                    var worldDialog = OpenWindow<WorldDialog>();
                    Check(worldDialog != null && (worldDialog.Title ?? "") == "Nouveau monde", "la fenêtre d'un nouveau monde s'ouvre");
                    WorldChoice choice = null;
                    if (worldDialog != null)
                    {
                        worldDialog.CheckSystemForProbe(0, true); // le Calendrier des Marées vit dans ce monde
                        worldDialog.SetSizeForProbe("40 075", "23,4");
                        await Settle();
                        var facts = worldDialog.FactsForProbe().ConvertAll(delegate(string f) { return plain(f); });
                        Check(facts.Count == 6 && facts[0].Contains("Rayon : 6 378 km") && facts[2].Contains("le jour en compte 20") && facts[2].Contains("2 004 km") && facts[4].Contains("Tropiques à 23,4°"),
                            "la taille du monde : rayon, horizon, heure de soleil (dans les heures du calendrier), tropiques (" + (facts.Count > 0 ? facts[0] : "-") + ")");
                        Shot(worldDialog, "astrolabe-1-monde-taille.png");
                        worldDialog.SetSizeForProbe("beaucoup", "23,4");
                        await Settle();
                        Check(worldDialog.ProblemForProbe != null, "une circonférence illisible est signalée");
                        worldDialog.SetSizeForProbe("40 075", "23,4");
                        worldDialog.FourSeasonsForProbe();
                        await Settle();
                        Check(worldDialog.SeasonRowsForProbe == 4 && worldDialog.SeasonEchoForProbe(1) == "→ 19 Hautes-Eaux", "« Quatre saisons égales » : la deuxième commence le jour 91, dit dans le calendrier (" + worldDialog.SeasonEchoForProbe(1) + ")");
                        worldDialog.SetSolarYearForProbe("360,25");
                        await Settle();
                        Check(worldDialog.DriftForProbe.Contains("plus longue") && worldDialog.DriftForProbe.Contains("plus tôt"), "une durée réelle de l'année : la dérive du calendrier est dite (" + worldDialog.DriftForProbe + ")");
                        worldDialog.ShowTabForProbe(1);
                        await Settle();
                        Shot(worldDialog, "astrolabe-2-monde-saisons.png");
                        worldDialog.SetSolarYearForProbe("");
                        worldDialog.AddMoonForProbe("Sélène", "30", "1 Aubemer E3.1");
                        worldDialog.AddMoonForProbe("Nyx", "45", "1 Aubemer E3.1");
                        await Settle();
                        Check(plain(worldDialog.MoonsEchoForProbe).Contains("Sélène : pleine lune suivante le 31 Aubemer E3.1") && plain(worldDialog.MoonsEchoForProbe).Contains("Toutes pleines ensemble") && plain(worldDialog.MoonsEchoForProbe).Contains("90 jours plus tard"),
                            "deux lunes de 30 et 45 jours : la pleine lune suivante, et leur conjonction tous les 90 jours (" + plain(worldDialog.MoonsEchoForProbe).Replace("\n", " / ") + ")");
                        worldDialog.ShowTabForProbe(2);
                        await Settle();
                        Shot(worldDialog, "astrolabe-3-monde-lunes.png");
                        worldDialog.AddMoonForProbe("Fausse", "30", "le jour d'après");
                        await Settle();
                        Check(worldDialog.ProblemForProbe != null && worldDialog.ProblemForProbe.Contains("Fausse"), "une date de pleine lune illisible est signalée (" + worldDialog.ProblemForProbe + ")");
                        worldDialog.CancelForProbe();
                        Check(await worldTask == null && project.Worlds.Count == 0, "annulé : aucun monde créé");

                        // Le même monde, validé cette fois.
                        worldTask = WorldDialog.Edit(shell, project, shell.NewWorldDraftForProbe(), true);
                        await Settle();
                        worldDialog = OpenWindow<WorldDialog>();
                        worldDialog.CheckSystemForProbe(0, true);
                        worldDialog.SetSizeForProbe("40 075", "23,4");
                        worldDialog.FourSeasonsForProbe();
                        worldDialog.AddMoonForProbe("Sélène", "30", "1 Aubemer E3.1");
                        worldDialog.AddMoonForProbe("Nyx", "45", "1 Aubemer E3.1");
                        await Settle();
                        worldDialog.AcceptForProbe();
                        choice = await worldTask;
                    }
                    Check(choice != null && choice.SystemIds.Count == 1 && choice.World.Seasons.Count == 4 && choice.World.Moons.Count == 2, "validé : le monde, ses quatre saisons, ses deux lunes, son calendrier");
                    if (choice != null)
                    {
                        shell.ApplyWorldForProbe(null, choice);
                        await Settle();
                        Check(project.Worlds.Count == 1 && view.System.WorldId == choice.World.Id && view.InspectorHasTagForProbe("timeline-world"), "le calendrier de la frise vit dans le monde ; le Général de la frise le dit");
                        hubNow.Load(project);
                        Check(hubNow.WorldCardsForProbe == 1, "l'Astrolabe a la tuile du monde");
                        var calendar = view.CalendarForProbe;
                        TimePoint first;
                        view.System.TryParse("1 Aubemer E3.1", out first);
                        view.SetCalendarMode(true);
                        view.GoTo(first);
                        await Settle();
                        Check(calendar.SubtitleForProbe.EndsWith("· Printemps") && calendar.DayHasSeasonForProbe(1), "le calendrier dit la saison du mois, chaque case porte son liseré (" + calendar.SubtitleForProbe + ")");
                        Check(calendar.MoonsOfDayForProbe(1).Count == 2 && calendar.MoonsOfDayForProbe(1).TrueForAll(delegate(int phase) { return phase == Astro.Full; }), "1 Aubemer E3.1 : les deux lunes sont pleines");
                        Check(calendar.MoonsOfDayForProbe(16).Count == 1 && calendar.MoonsOfDayForProbe(16)[0] == Astro.New && calendar.MoonsOfDayForProbe(2).Count == 0, "le 16 : la nouvelle lune de Sélène ; le 2 : rien");
                        Check(calendar.MoonsOfDayForProbe(31).Contains(Astro.Full), "le 31 : Sélène de nouveau pleine");
                        Shot(view, "astrolabe-4-calendrier-saisons-lunes.png");
                        calendar.ShiftMonthForProbe(2);
                        await Settle();
                        Check(calendar.SubtitleForProbe.Contains("Printemps → Été"), "un mois à cheval sur deux saisons le dit (" + calendar.SubtitleForProbe + ")");
                        view.SetCalendarMode(false);
                        await Settle();
                        // Le Calculateur dit aussi le ciel du jour.
                        view.RequestCalculatorForProbe(first);
                        await Settle();
                        var sky = shell.CalculatorPanelForProbe;
                        sky.SetSumForProbe("1 Aubemer E3.1", false, "", "", "", "15");
                        await Settle();
                        Check(sky.SumDetailForProbe.Contains("Printemps") && sky.SumDetailForProbe.Contains("Sélène : nouvelle lune"), "le Calculateur dit la saison et les lunes du jour trouvé (" + sky.SumDetailForProbe + ")");
                        shell.PinCalculatorForProbe(false);
                        shell.CloseCalculatorForProbe();
                        await Settle();
                        // Ctrl+Z rend le projet sans monde.
                        Check(shell.UndoForProbe() && project.Worlds.Count == 0 && view.System.WorldId == null, "Ctrl+Z : le monde s'en va, le calendrier est délié");
                        await Settle();
                        Check(!view.InspectorHasTagForProbe("timeline-world"), "…et le Général de la frise ne le dit plus");
                    }
                }

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
                    // Lot 3 (14/11) : la date d'exemple, le testeur encadré, l'an 0 des segments, les jours en pastilles.
                    Check(systemDialog.DateExampleForProbe.StartsWith("→ 12 ") && systemDialog.DateExampleForProbe.Contains("E2.880"), "sous la notation d'une date, une date d'exemple mise en forme (" + systemDialog.DateExampleForProbe + ")");
                    Check(systemDialog.TryBoxFramedForProbe, "le testeur de dates est dans un cadre aux bords arrondis");
                    Check(systemDialog.SegmentYearZeroEnabledForProbe, "découpé et chaque segment repart de zéro : la case « an 0 de chaque segment » est active");
                    systemDialog.SetSegmentYearZeroForProbe(true);
                    await Settle();
                    Check(systemDialog.ExampleForProbe.Contains("E1.800 puis E2.0"), "…cochée : E1.800 puis E2.0 (" + systemDialog.ExampleForProbe + ")");
                    systemDialog.TryForProbe("3 Sel E2.0");
                    await Settle();
                    Check(systemDialog.TryEchoForProbe.Contains("3 Sel E2.0"), "…et « E2.0 » se lit (" + systemDialog.TryEchoForProbe + ")");
                    systemDialog.SetSegmentYearZeroForProbe(false);
                    systemDialog.TryForProbe("12 Sel E3.8");
                    await Settle();
                    Check(systemDialog.DayRowsForProbe == 6 && systemDialog.PendingDayForProbe == "", "les six jours de la semaine sont une liste, la zone d'ajout est vide");
                    Check(!systemDialog.DayArrowEnabledForProbe(0, true) && systemDialog.DayArrowEnabledForProbe(0, false) && !systemDialog.DayArrowEnabledForProbe(5, false),
                        "le premier jour ne monte pas, le dernier ne descend pas");
                    systemDialog.TypeDayForProbe("septième");
                    await Settle();
                    Check(systemDialog.DayRowsForProbe == 6 && systemDialog.PendingDayForProbe == "septième", "un nom sans virgule reste dans la zone");
                    systemDialog.TypeDayForProbe("septième, huitième,");
                    await Settle();
                    Check(systemDialog.DayRowsForProbe == 8 && systemDialog.PendingDayForProbe == "" && systemDialog.DaysForProbe.EndsWith("|septième|huitième"),
                        "la virgule ajoute à la liste (deux d'un coup) et vide la zone (" + systemDialog.DaysForProbe + ")");
                    systemDialog.ClickDayArrowForProbe(7, true);
                    await Settle();
                    Check(systemDialog.DaysForProbe.EndsWith("|huitième|septième"), "la flèche haut monte le jour d'un rang (" + systemDialog.DaysForProbe + ")");
                    systemDialog.ClickDayArrowForProbe(0, false);
                    await Settle();
                    Check(systemDialog.DaysForProbe.StartsWith("flot|lunemer|"), "la flèche bas le descend (" + systemDialog.DaysForProbe + ")");
                    systemDialog.RemoveDayForProbe(7);
                    await Settle();
                    Check(systemDialog.DayRowsForProbe == 7 && systemDialog.DaysForProbe.EndsWith("|huitième"), "la croix efface le jour (" + systemDialog.DaysForProbe + ")");
                    // Répartir automatiquement : 5 semaines de 7 jours = 35 jours par mois.
                    Check(systemDialog.DistributeEnabledForProbe, "avec une semaine, « Répartir » est offert");
                    systemDialog.DistributeForProbe("5");
                    await Settle();
                    Check(systemDialog.MonthDaysForProbe == "35,35,35,35,35,35,35,35,35,35" && systemDialog.DistributeEchoForProbe.Contains("35 jours par mois") && systemDialog.DistributeEchoForProbe.Contains("350 par an"),
                        "« Répartir automatiquement les jours » : 5 semaines de 7 jours, 35 jours par mois (" + systemDialog.MonthDaysForProbe + " — " + systemDialog.DistributeEchoForProbe + ")");
                    systemDialog.ShowTabForProbe(1);
                    await Settle();
                    Shot(systemDialog, "frises-22-repartir-les-jours.png");
                    systemDialog.ShowTabForProbe(2);
                    await Settle();
                    Shot(systemDialog, "frises-19-semaine-liste.png");
                    systemDialog.ShowTabForProbe(0);
                    await Settle();
                    Shot(systemDialog, "frises-6-temporalite-par-segments.png");
                    systemDialog.SetCumulativeForProbe(true);
                    await Settle();
                    Check(!systemDialog.SegmentYearZeroEnabledForProbe, "années cumulatives : la case « an 0 de chaque segment » est grisée");
                    Check(systemDialog.ExampleForProbe.Contains("E1.800 puis E2.801"), "années cumulatives entre segments : E1.800 puis E2.801 (" + systemDialog.ExampleForProbe + ")");
                    systemDialog.SetCumulativeForProbe(false);
                    systemDialog.SetYearPatternForProbe("an {a}");
                    await Settle();
                    Check(systemDialog.ProblemShownForProbe, "une notation qui ne dit pas le segment est signalée");
                    systemDialog.CancelForProbe();
                }
                Check(await systemTask == null && view.System.YearPattern == "E{s}.{a}" && !view.System.Cumulative && view.System.WeekDays.Count == 6, "annuler laisse le système tel quel");

                // — Lot 3 : un système NEUF — pas de découpage, « Âge », A{s}-{a}.
                var draftTask = TimeSystemDialog.Edit(shell, project, TimeSystemDialog.Template(project), true);
                await Settle();
                var draftDialog = OpenWindow<TimeSystemDialog>();
                Check(draftDialog != null, "la fenêtre d'un nouveau système s'ouvre");
                if (draftDialog != null)
                {
                    Check(!draftDialog.SegmentedForProbe && draftDialog.WordForProbe == "Âge" && draftDialog.YearPatternForProbe == "{a}" && draftDialog.DatePatternForProbe == "{j}/{M}/{année}",
                        "un système neuf : pas de découpage, année « {a} », date « {j}/{M}/{année} » (" + draftDialog.YearPatternForProbe + ", " + draftDialog.DatePatternForProbe + ")");
                    Check(!draftDialog.SegmentYearZeroEnabledForProbe, "…sans découpage, la case « an 0 de chaque segment » est grisée");
                    Check(draftDialog.ExampleForProbe.Contains("880") && !draftDialog.ExampleForProbe.Contains("A1") && draftDialog.DateExampleForProbe == "→ 12/Mois 3/880" && !draftDialog.ProblemShownForProbe,
                        "…l'exemple se lit sans erreur (" + draftDialog.ExampleForProbe + " ; " + draftDialog.DateExampleForProbe + ")");
                    Shot(draftDialog, "frises-20-nouveau-systeme.png");
                    draftDialog.SegmentedForProbe = true;
                    await Settle();
                    Check(draftDialog.YearPatternForProbe == "A{s}.{a}" && draftDialog.ExampleForProbe.Contains("A1.1000 puis A2.1") && draftDialog.SegmentYearZeroEnabledForProbe && !draftDialog.ProblemShownForProbe,
                        "…découpage coché : la notation passe à A{s}.{a}, A1.1000 puis A2.1 (" + draftDialog.YearPatternForProbe + " — " + draftDialog.ExampleForProbe + ")");
                    draftDialog.SetSegmentYearZeroForProbe(true);
                    await Settle();
                    Check(draftDialog.ExampleForProbe.Contains("A1.1000 puis A2.0") && draftDialog.DateExampleForProbe == "→ 12/Mois 3/A2.879", "…an 0 par segment : A1.1000 puis A2.0 (" + draftDialog.ExampleForProbe + " ; " + draftDialog.DateExampleForProbe + ")");
                    draftDialog.TryForProbe("3/Mois 2/A1.5");
                    await Settle();
                    Check(draftDialog.TryEchoForProbe.Contains("3/Mois 2/A1.5"), "…une date jour/mois/année se relit (" + draftDialog.TryEchoForProbe + ")");
                    Shot(draftDialog, "frises-21-nouveau-systeme-ages.png");
                    draftDialog.SegmentedForProbe = false;
                    await Settle();
                    Check(draftDialog.YearPatternForProbe == "{a}", "…découpage décoché : la notation revient à {a}");
                    draftDialog.CancelForProbe();
                }
                Check(await draftTask == null && project.TimeSystems.Count == 2, "annulé : le projet garde ses deux systèmes");

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
                Check(tagged(maraboutSheet, "epoch-dimmed") && tagged(maraboutSheet, "epoch-skull") && cardTexts(maraboutSheet).Any(delegate(string s) { return s.StartsWith("Décédé·e à 93 ans"); }),
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
                    Check(shell.Sheet.EpochSkullShownForProbe && (shell.Sheet.EpochNoteForProbe ?? "").Contains("Décédé·e à 93 ans"), "la fiche d'un mort porte le crâne dans son titre (" + shell.Sheet.EpochNoteForProbe + ")");
                    Shot(shell.Sheet, "frises-11-fiche-mort.png");
                }
                if (keiraSheet != null)
                {
                    shell.Binder.SelectItem(keiraSheet.Id);
                    await Settle();
                    Check(!shell.Sheet.EpochSkullShownForProbe && (shell.Sheet.EpochNoteForProbe ?? "").Contains("32 ans"), "…celle d'une vivante, son âge, sans crâne (" + shell.Sheet.EpochNoteForProbe + ")");
                }
                // — Les dissonances (12/11) : le bouton à droite du champ « Âge », sa fenêtre, l'âge qui saute.
                if (keiraSheet != null)
                {
                    shell.Sheet.ShowGeneralTabForProbe();
                    await Settle();
                    Check(shell.Sheet.GetVisualDescendants().OfType<Button>().Any(delegate(Button b) { return Equals(b.Tag, "sheet-dissonances") && b.IsEffectivelyVisible; }),
                        "la fiche d'un personnage a le bouton « Dissonances » à droite de son âge");
                    var bare = Project.CreateNew();
                    Check(!DissonanceDialog.ForProbe(bare, keiraSheet).CanAddForProbe, "sans système temporel, rien ne s'ajoute dans la fenêtre Dissonances");
                    var dissonances = DissonanceDialog.ForProbe(project, keiraSheet);
                    Check(dissonances.CanAddForProbe && dissonances.RowsForProbe == 0, "avec un système, les deux ajouts sont offerts");
                    dissonances.AddForProbe(Dissonance.Reference, "n'importe quand", null, "19");
                    Check(!dissonances.AcceptForProbe() && dissonances.ErrorForProbe != null, "une date illisible est refusée (" + dissonances.ErrorForProbe + ")");
                    dissonances = DissonanceDialog.ForProbe(project, keiraSheet);
                    dissonances.AddForProbe(Dissonance.Exclusion, "E3.20", "E3.30", null);
                    dissonances.AddForProbe(Dissonance.Reference, "E3.60", null, "19");
                    dissonances.Show();
                    await Settle();
                    Shot((Control)dissonances.Content, "frises-13-dissonances.png");
                    {
                        // Lot 3 (14/11) : en sombre avec « papier blanc », les cartes restaient blanches sous une encre claire.
                        var cards = ((Control)dissonances.Content).GetVisualDescendants().OfType<Border>().Where(delegate(Border b) { return Equals(b.Tag, "dissonance-row"); }).ToList();
                        var darkBefore = Chrome.Dark;
                        var whiteBefore = AppSettings.WhitePaperInDark;
                        AppSettings.WhitePaperInDark = true;
                        Chrome.Toggle(true);
                        App.ApplyTheme(true);
                        await Settle();
                        Func<Avalonia.Media.IBrush, double> light = delegate(Avalonia.Media.IBrush brush)
                        {
                            var solid = brush as Avalonia.Media.ISolidColorBrush;
                            return solid == null ? -1 : (0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B) / 255.0;
                        };
                        Check(cards.Count == 2 && cards.All(delegate(Border b) { return light(b.Background) >= 0 && light(b.Background) < 0.35; }) && light(Chrome.Ink) > 0.7,
                            "mode sombre (papier blanc compris) : les cartes des dissonances sont sombres sous leur encre claire");
                        Shot((Control)dissonances.Content, "frises-13-dissonances-sombre.png");
                        AppSettings.WhitePaperInDark = whiteBefore;
                        Chrome.Toggle(darkBefore);
                        App.ApplyTheme(darkBefore);
                        await Settle();
                    }
                    Check(dissonances.AcceptForProbe() && keiraSheet.Dissonances.Count == 2, "une période d'exclusion et un point de référence validés");
                    await Settle();
                    shell.Sheet.RefreshEpoch();
                    Check((shell.Sheet.EpochNoteForProbe ?? "").Contains("22 ans (excl.) en E3.40"), "E3.40, après un saut de dix ans : « 22 ans (excl.) » (" + shell.Sheet.EpochNoteForProbe + ")");
                    project.Epoch.Notation = "E3.25";
                    shell.Sheet.RefreshEpoch();
                    Check((shell.Sheet.EpochNoteForProbe ?? "").Contains("Hors-temps en E3.25 · 12 ans en E3.20"), "E3.25, pendant le saut : hors-temps (" + shell.Sheet.EpochNoteForProbe + ")");
                    await Settle();
                    Shot(shell.Sheet, "frises-14-fiche-hors-temps.png");
                    shell.Binder.SelectItem(sheetsRoot.Id);
                    await Settle();
                    library.RefreshEpoch();
                    await Settle();
                    Check(cardTexts(keiraSheet).Contains("Hors-temps") && tagged(keiraSheet, "epoch-dimmed"), "…et sa tuile est en retrait, « Hors-temps »");
                    Shot(library, "frises-15-epoque-hors-temps.png");
                    keiraSheet.Dissonances.Clear();
                    project.Epoch.Notation = "E3.40";
                    library.RefreshEpoch();
                    await Settle();
                }
                // L'époque s'éteint, tout revient.
                shell.Binder.SelectItem(sheetsRoot.Id);
                await Settle();
                shell.ToggleEpochForProbe(false);
                await Settle();
                Check(!library.EpochOnForProbe && !tagged(maraboutSheet, "epoch-dimmed") && !cardTexts(keiraSheet).Contains("32 ans"), "éteinte : plus d'âge ni de tuile grisée");
                project.Epoch.Notation = "E3.30";

                // — Lot 3 (14/11) : le Carnet, toujours au rail, sous le Lexique.
                {
                    var panelBefore = AppSettings.RightPanel;
                    var notebook = shell.NotebookPanelForProbe;
                    Check(Icons.Has("notebook") && shell.RailHasTabForProbe(RightPanel.Notebook), "le rail a l'onglet du Carnet (icône « notebook »)");
                    Check(!shell.RailHasTabForProbe(RightPanel.Lexicon) || shell.RailTabRankForProbe(RightPanel.Notebook) == shell.RailTabRankForProbe(RightPanel.Lexicon) + 1, "…juste sous le Lexique");
                    shell.SetRightPanelForProbe(RightPanel.Notebook);
                    await Settle();
                    Check(AppSettings.RightPanel == RightPanel.Notebook && notebook.IsEffectivelyVisible, "un clic : le Carnet occupe la colonne de droite");
                    Check(notebook.WritingForProbe && notebook.NoteCountForProbe == 1 && notebook.TitleForProbe == Notebook.Untitled, "un carnet neuf : une page vide, la plume prête");
                    notebook.TypeForProbe("# Courses du marabout\n\nNe pas oublier :\n\n- [ ] du **sel**\n- [x] des *plumes*\n\n> Voir [[Keira]].");
                    await Settle();
                    notebook.ReadForProbe();
                    await Settle();
                    var rendered = notebook.RenderedForProbe;
                    var renderedTexts = rendered == null ? new List<TextBlock>() : rendered.GetVisualDescendants().OfType<TextBlock>().ToList();
                    Check(notebook.ReadingForProbe && !notebook.WritingForProbe && notebook.TitleForProbe == "Courses du marabout", "la plume posée, la note se montre mise en forme ; son titre est sa première ligne (" + notebook.TitleForProbe + ")");
                    Check(renderedTexts.Any(delegate(TextBlock b) { return b.FontSize >= 20; }) && !renderedTexts.Any(delegate(TextBlock b) { return (b.Text ?? "").Contains("**") || (b.Text ?? "").StartsWith("#"); }),
                        "…le titre est un vrai titre, plus de marques Markdown à l'écran");
                    notebook.ToggleTaskForProbe(0);
                    await Settle();
                    Check(Notebook.Current.Text.Contains("- [x] du **sel**"), "une case cochée dans la note mise en forme s'écrit dans la note");
                    Shot(notebook, "carnet-1-lecture.png");
                    notebook.Flush();
                    Check(System.IO.File.Exists(Notebook.FilePath()) && !notebook.SaveProblemForProbe, "le carnet s'enregistre à côté des réglages (carnet.json)");
                    notebook.WriteForProbe();
                    await Settle();
                    Check(notebook.WritingForProbe, "un clic dans la note rend la plume");
                    Shot(notebook, "carnet-2-ecriture.png");
                    notebook.AddForProbe();
                    await Settle();
                    Check(notebook.NoteCountForProbe == 2 && notebook.WritingForProbe && notebook.TitleForProbe == Notebook.Untitled, "une nouvelle note : deux pages, la neuve sous les yeux");
                    notebook.SelectForProbe(0);
                    await Settle();
                    Check(notebook.ReadingForProbe && notebook.TitleForProbe == "Courses du marabout", "le sélecteur ramène la première note");
                    shell.Binder.SelectItem(sheetsRoot.Id);
                    await Settle();
                    Check(AppSettings.RightPanel == RightPanel.Notebook && notebook.IsEffectivelyVisible && notebook.TitleForProbe == "Courses du marabout", "le Carnet reste sous les yeux quand on change d'élément");
                    notebook.SelectForProbe(1);
                    notebook.RemoveForProbe();
                    notebook.RemoveForProbe();
                    await Settle();
                    notebook.Flush();
                    Check(notebook.NoteCountForProbe == 1 && Notebook.Current.Text == "", "les notes retirées, le carnet garde une page vide");
                    shell.SetRightPanelForProbe(panelBefore);
                    await Settle();
                }

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
