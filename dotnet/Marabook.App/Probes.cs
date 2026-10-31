using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;
using SkiaSharp;

namespace Marabook.App
{
    /// <summary>La sonde de l'application (P1, « --probe ») : de vraies
    /// fenêtres, comme les sondes WPF — la coquille sur le projet d'exemple,
    /// la Pile, l'inspecteur, les Préférences, la boîte de message, le thème.
    /// Chaque vérification écrit OK ou ÉCHEC ; le code de retour du processus
    /// est le nombre d'échecs. Tourne sur Windows et sous WSLg.</summary>
    public static class Probes
    {
        public static int Failures;
        private static int _checks;

        private static void Check(bool condition, string label)
        {
            _checks++;
            if (!condition) Failures++;
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
        }

        /// <summary>La fenêtre ouverte de ce type, ou null (dialogues modaux de la sonde).</summary>
        private static T OpenWindow<T>() where T : Window
        {
            var lifetime = Avalonia.Application.Current.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime == null) return null;
            foreach (var window in lifetime.Windows)
                if (window is T && window.IsVisible) return (T)window;
            return null;
        }

        /// <summary>Le premier bouton visible dont le texte contient ce libellé.</summary>
        private static Button FindButton(Visual root, string label)
        {
            foreach (var button in root.GetVisualDescendants().OfType<Button>())
            {
                var text = button.Content as string;
                if (text == null)
                    foreach (var block in button.GetVisualDescendants().OfType<TextBlock>())
                        if (block.Text != null && block.Text.Contains(label)) return button;
                if (text != null && text.Contains(label)) return button;
            }
            return null;
        }

        /// <summary>Une petite image PNG valide (12 × 8, rouge) pour les sondes
        /// d'images (1.0.5-b).</summary>
        private static byte[] ProbePng()
        {
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(12, 8)))
            {
                using (var context = bitmap.CreateDrawingContext())
                    context.FillRectangle(Avalonia.Media.Brushes.Red, new Rect(0, 0, 12, 8));
                using (var stream = new System.IO.MemoryStream())
                {
                    bitmap.Save(stream);
                    return stream.ToArray();
                }
            }
        }

        private static async Task Settle()
        {
            await Task.Delay(120);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Background);
        }

        public static async Task Run(MainWindow shell)
        {
            Console.WriteLine("== Sonde Avalonia P1-P2 (" + AppPlatform.OsName + ")");
            try
            {
                await Settle();
                // — La coquille sur le projet d'exemple.
                Check(shell.Project != null, "le projet d'exemple est ouvert");
                var roots = shell.Project.Roots;
                Check(roots != null && roots.Count == 8, "la Pile montre les huit racines (" + (roots == null ? "-" : roots.Count.ToString()) + ")");
                var writings = shell.Project.Category(Project.KeyWritings);
                var book = writings.Children.Count > 1 ? writings.Children[1] : null;
                Check(book != null && book.Kind == ItemKind.Book && book.Children.Count == 3, "le livre d'exemple a trois chapitres");
                var chapter = book == null ? null : book.Children[0];
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();
                Check(shell.InspectorTitle == (chapter == null ? "" : chapter.Title), "sélectionner un écrit : l'inspecteur montre son titre (" + shell.InspectorTitle + ")");
                Check(shell.InspectorKind == "Écrit" && shell.InspectorDetail.Contains("Mots"), "…sa nature et ses mots (" + shell.InspectorDetail.Replace("\n", " · ") + ")");
                Check(shell.Title.Contains("Marabook"), "le titre de la fenêtre nomme l'application (" + shell.Title + ")");
                Check(shell.StatusText.Length > 0, "la barre d'état dit quelque chose (" + shell.StatusText + ")");

                // — Le caret de repli de la Pile (07/10) : la Pile resserrée à
                //   96 px, il reste posé au bord droit de la ligne Accueil, dans
                //   la fenêtre (avant, l'arbre mesuré à largeur infinie le
                //   laissait filer sous le bord).
                {
                    var wideBefore = AppSettings.BinderWidth;
                    shell.SetBinderWidthForProbe(96);
                    await Settle();
                    Border toggle = null;
                    foreach (var border in shell.Binder.GetVisualDescendants().OfType<Border>())
                        if ((ToolTip.GetTip(border) as string) == "Replier la Pile") { toggle = border; break; }
                    var binderWidth = shell.Binder.Bounds.Width;
                    var rightEdge = -1.0;
                    if (toggle != null)
                    {
                        var corner = toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), shell.Binder);
                        if (corner != null) rightEdge = corner.Value.X;
                    }
                    Check(toggle != null && toggle.Bounds.Width > 8 && rightEdge > binderWidth - 40 && rightEdge <= binderWidth + 0.5,
                        "Pile resserrée à 96 px : le caret de repli reste visible au bord droit (bord à " + rightEdge.ToString("0") + " px sur " + binderWidth.ToString("0") + ")");
                    shell.SetBinderWidthForProbe(wideBefore);
                    await Settle();
                }

                // — P2 : l'éditeur composé sur l'écrit sélectionné.
                var editor = shell.Editor;
                Check(editor.IsVisible && chapter != null && editor.ShowsItem(chapter), "l'écrit s'ouvre dans l'éditeur composé");
                var composed = shell.Composed;
                Check(composed != null && composed.HasItem && composed.IsVisible, "la surface composée est attachée");
                Check(composed != null && composed.Extent.Height > composed.Viewport.Height + 1,
                    "…et elle défile : la page dépasse la fenêtre (" + (composed == null ? "-" : composed.Extent.Height.ToString("0") + " > " + composed.Viewport.Height.ToString("0")) + ")");
                Check(shell.StatusPagesText.Contains("page"), "la barre d'état donne les pages de l'écrit (" + shell.StatusPagesText + ")");
                Check(shell.StatusText.Contains("signes EC") && !shell.StatusText.Contains("feuillet") && !shell.StatusText.Contains("min"), "…et mots · signes EC, sans feuillets ni temps de lecture (" + shell.StatusText + ")");
                Check(shell.ZoomPanelVisibleForProbe, "le curseur de zoom est montré dans l'éditeur");
                // — Lignes lâches (09/10) : CACHÉES par défaut (13/10, opt-in), le bouton du ruban Mise en page les montre et les cache.
                Check(!AppSettings.ShowLooseLines || AppSettings.LoadedFromFile, "les lignes lâches sont cachées par défaut (sauf réglage enregistré)");
                if (AppSettings.ShowLooseLines) editor.ToggleLooseLinesForProbe();
                Check(!editor.LooseLinesShownForProbe && !ComposedRenderer.ShowLooseLines, "cachées : rien n'est signalé");
                editor.ToggleLooseLinesForProbe();
                Check(editor.LooseLinesShownForProbe && ComposedRenderer.ShowLooseLines, "Mise en page › Lignes lâches les montre");
                editor.ToggleLooseLinesForProbe();
                Check(!editor.LooseLinesShownForProbe, "…et les cache à nouveau");
                // — Ctrl+F (09/10) : une seconde après la frappe, la première occurrence est sélectionnée.
                editor.TypeSearchForProbe("volets");
                await Task.Delay(1300);
                await Settle();
                Check(editor.SearchInfoForProbe.StartsWith("1/"), "Ctrl+F : une seconde après la frappe, la première occurrence est sélectionnée (" + editor.SearchInfoForProbe + ")");
                editor.HideSearch();
                // — Double-clic sur un résultat de la Pile (09/10) : l'occurrence elle-même.
                shell.OpenOccurrenceForProbe(chapter, "volets");
                await Settle();
                await Settle();
                Check(editor.ShowsItem(chapter) && editor.SelectedPlainText() == "volets", "la Pile : un double-clic sur un résultat sélectionne l'occurrence (« " + editor.SelectedPlainText() + " »)");
                // — Le Bilan de style au rail (09/10) : un onglet le temps du bilan.
                var railBefore = AppSettings.RightPanel;
                editor.ShowStyleReportForProbe();
                await Settle();
                Check(shell.HasStyleReportForProbe && AppSettings.RightPanel == RightPanel.StyleReport, "le Bilan de style ouvre un onglet du rail, pas une fenêtre");
                Check(OpenWindow<Window>() == null || !(OpenWindow<Window>().Title ?? "").StartsWith("Bilan"), "…aucune fenêtre « Bilan de style »");
                shell.CloseStyleReportForProbe();
                await Settle();
                Check(!shell.HasStyleReportForProbe && AppSettings.RightPanel == RightPanel.None, "la croix du bilan ferme l'onglet et replie le rail");
                editor.ShowStyleReportForProbe();
                await Settle();
                shell.OpenByTitle("Chapitre deux — La maison aux volets");
                await Settle();
                await Settle();
                Check(!shell.HasStyleReportForProbe && AppSettings.RightPanel != RightPanel.StyleReport, "changer d'écrit ferme le bilan et son onglet");
                shell.OpenByTitle(chapter.Title);
                await Settle();
                await Settle();
                // — Le PANNEAU DES RELEVÉS refait (30/10, Rémi) : un bouton
                //   « Options des relevés » en tête, un papier replié par
                //   catégorie, « Ignorer » = l'occurrence, « Ignorer partout »
                //   = le mot dans cet écrit seul (BinderItem.ProofIgnored),
                //   « Ajouter au dictionnaire » sur un nom propre.
                {
                    var seeded = new TextParagraph();
                    seeded.Runs.Add(new TextRun { Text = "Un tezte avec une fôte, où Keira Varenh marchait vers la citadelle." });
                    chapter.Document.Paragraphs.Add(seeded);
                    editor.Reload();
                    editor.RefreshProofing(); // la passe tout de suite (Reload recompose sans vérifier)
                    await Task.Delay(1500);
                    await Settle();
                    shell.SetRightPanelPublic(RightPanel.Correction);
                    await Settle();
                    var panel = editor.CorrectionPanel;
                    var tagged = panel.GetVisualDescendants().OfType<Control>().Where(delegate(Control c) { return c.Tag is string; }).ToList();
                    Check(editor.FindingCount >= 3, "l'écrit garni relève tezte, fôte et Varenh (" + editor.FindingCount + ")");
                    Check(tagged.Exists(delegate(Control c) { return (string)c.Tag == "corr-options"; }), "un bouton « Options des relevés » en tête du panneau");
                    Check(!panel.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Any(delegate(Avalonia.Controls.Primitives.ToggleButton b) { return (b.Content as string) == "Orthographe"; }), "…plus de bascules de catégorie");
                    var papers = tagged.Count(delegate(Control c) { return (string)c.Tag == "corr-paper"; });
                    Check(papers >= 1, "chaque catégorie relevée a son papier (" + papers + ")");
                    Check(tagged.Exists(delegate(Control c) { return (string)c.Tag == "corr-expand-all"; }) && tagged.Exists(delegate(Control c) { return (string)c.Tag == "corr-collapse-all"; }), "« Tout développer » et « Tout fermer » au-dessus");
                    var learn = tagged.OfType<Button>().FirstOrDefault(delegate(Button b) { return (string)b.Tag == "corr-learn"; });
                    Check(learn != null, "« Ajouter au dictionnaire » proposé pour « Varenh » (nom propre), pas pour « tezte »");
                    var learnCount = tagged.Count(delegate(Control c) { return (string)c.Tag == "corr-learn"; });
                    Check(learnCount == 1, "…une seule fois (" + learnCount + ")");
                    var collapse = tagged.OfType<Button>().First(delegate(Button b) { return (string)b.Tag == "corr-collapse-all"; });
                    collapse.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Settle();
                    var findingsVisible = editor.CorrectionPanel.GetVisualDescendants().OfType<Control>().Count(delegate(Control c) { return (c.Tag as string) == "corr-finding" && c.IsEffectivelyVisible; });
                    Check(findingsVisible == 0, "« Tout fermer » replie tous les papiers (" + findingsVisible + " fiche(s) visible(s))");
                    var expand = editor.CorrectionPanel.GetVisualDescendants().OfType<Button>().First(delegate(Button b) { return (b.Tag as string) == "corr-expand-all"; });
                    expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Settle();
                    findingsVisible = editor.CorrectionPanel.GetVisualDescendants().OfType<Control>().Count(delegate(Control c) { return (c.Tag as string) == "corr-finding" && c.IsEffectivelyVisible; });
                    Check(findingsVisible >= 3, "« Tout développer » les rouvre (" + findingsVisible + ")");
                    var countBefore = editor.FindingCount;
                    var everywhere = editor.CorrectionPanel.GetVisualDescendants().OfType<Button>().First(delegate(Button b) { return (b.Tag as string) == "corr-ignore-item"; });
                    everywhere.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(400);
                    await Settle();
                    Check(chapter.ProofIgnored.Count == 1 && chapter.ProofIgnored[0] == "tezte", "« Ignorer partout » inscrit « tezte » dans la liste de CET écrit (" + string.Join(", ", chapter.ProofIgnored) + ")");
                    Check(shell.Project.ProofIgnored.Count == 0 && !AppSettings.ProofIgnored.Contains("tezte"), "…ni dans le projet, ni dans l'application");
                    Check(editor.FindingCount == countBefore - 1, "…et le relevé disparaît (" + countBefore + " → " + editor.FindingCount + ")");
                    chapter.ProofIgnored.Clear();
                    chapter.Document.Paragraphs.Remove(seeded);
                    editor.Reload();
                    await Settle();
                }
                shell.SetRightPanelForProbe(railBefore);
                await Settle();
                // — Le choix d'un gabarit a sa propre fenêtre (09/10), plus celle du lien.
                {
                    var pickTask = PickDialog.Ask(shell, "Gabarit de pages", "Quel gabarit appliquer ?", new List<string> { "(aucun gabarit)", "Sonde" }, "Appliquer");
                    await Settle();
                    var pick = OpenWindow<PickDialog>();
                    Check(pick != null && pick.Title == "Gabarit de pages", "le choix d'un gabarit ouvre « Gabarit de pages », pas « Lien vers une fiche »");
                    if (pick != null)
                    {
                        var cancel = FindButton(pick, "Annuler");
                        if (cancel != null) cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else pick.Close();
                    }
                    var picked = await pickTask;
                    Check(picked == null, "…Annuler ne choisit rien");
                }
                // — Actions groupées : « Appliquer le gabarit du livre » sur des écrits d'un livre (09/10).
                {
                    var demoBook = chapter == null ? null : chapter.EnclosingBook();
                    if (demoBook != null && demoBook.Children.Count >= 2)
                    {
                        var labels = shell.BatchLabelsForProbe(new List<BinderItem> { demoBook.Children[0], demoBook.Children[1] });
                        Check(labels.Contains("Appliquer le gabarit du livre") && labels.Contains("Appliquer un gabarit…"), "les actions groupées proposent « Appliquer le gabarit du livre » (" + labels.Count + " actions)");
                    }
                }
                Check(AppSettings.Gesture("loose-lines") == "Ctrl+L", "le raccourci par défaut est Ctrl+L / ⌘L (" + AppSettings.Gesture("loose-lines") + ")");
                Check(composed != null && composed.RunEditorAction("loose-lines") && editor.LooseLinesShownForProbe, "l'action de l'éditeur « loose-lines » bascule le bouton du ruban (cachées par défaut → montrées)");
                composed.RunEditorAction("loose-lines");
                Check(!editor.LooseLinesShownForProbe, "…dans les deux sens");
                Check(shell.StatusBookText.StartsWith("Livre : ") && shell.StatusBookText.Contains("page"), "…et la pagination totale du livre (" + shell.StatusBookText + ")");
                // — Le panneau Publication et « Publier » comptent les MÊMES
                //   pages (09/10) : le cache compte sur la page du livre.
                {
                    var demoBook = chapter == null ? null : chapter.EnclosingBook();
                    if (demoBook != null)
                    {
                        var compiled = shell.CompileForPublishForProbe(demoBook);
                        var publishSetup = demoBook.Book != null && demoBook.Book.Template != null ? demoBook.Book.Template : shell.Project.Page;
                        var merged = Marabook.Print.Composer.Compose(compiled, shell.Project.Styles.EffectiveFor(demoBook), publishSetup, shell.Project, new AvaloniaFontEngine());
                        var panelTotal = shell.BookPageTotalForProbe(demoBook);
                        Check(panelTotal == merged.Pages.Count, "le panneau Publication compte les pages du PDF publié (" + panelTotal + " = " + merged.Pages.Count + ")");
                        shell.BookPageTotalForProbe(demoBook); // le cache repart propre
                    }
                }
                // — L'enchaînement des pages (18/10, 1.0.6) : pastille sur le
                //   chapitre qui finit sur une page impaire, « Unifier » au
                //   menu ajoute la page blanche, l'option se débraye par livre.
                {
                    var flowBook = chapter == null ? null : chapter.EnclosingBook();
                    if (flowBook != null)
                    {
                        Check(Marabook.Model.BookFlow.Enabled(flowBook), "les avertissements d'enchaînement sont activés par défaut");
                        if (flowBook.Book == null) flowBook.Book = new BookInfo();
                        var report = Marabook.Model.BookFlow.Analyse(flowBook, shell.PageCountForProbe);
                        Check(report.OddChapters.Count > 0, "le livre de démonstration a au moins un chapitre finissant sur une page impaire (" + report.OddChapters.Count + ")");
                        if (report.OddChapters.Count > 0)
                        {
                            var odd = report.OddChapters[0];
                            shell.Binder.Rebuild();
                            await Settle();
                            Check(shell.ChapterOddEndForProbe(odd) % 2 == 1, "la coquille relève le folio impair de sa dernière page (p. " + shell.ChapterOddEndForProbe(odd) + ")");
                            Check(shell.Binder.HasFlowDotForProbe(odd.Id), "la pastille d'enchaînement est posée sur sa ligne dans la Pile");
                            Check(shell.Binder.MenuHasUnifyForProbe(odd.Id), "…et son menu propose « Unifier »");
                            var pagesBefore = shell.PageCountForProbe(odd);
                            var paragraphsBefore = odd.Document.Paragraphs.Count;
                            shell.UnifyForProbe(odd);
                            await Settle();
                            var lastParagraph = odd.Document.Paragraphs[odd.Document.Paragraphs.Count - 1];
                            Check(odd.Document.Paragraphs.Count == paragraphsBefore + 1 && lastParagraph.PageBreakBefore && shell.PageCountForProbe(odd) == pagesBefore + 1,
                                "« Unifier » ajoute une page blanche à la fin du chapitre (" + pagesBefore + " → " + shell.PageCountForProbe(odd) + " pages)");
                            Check(shell.ChapterOddEndForProbe(odd) == 0 && !shell.Binder.HasFlowDotForProbe(odd.Id), "…la pastille s'éteint");
                            Check(!shell.Binder.MenuHasUnifyForProbe(odd.Id), "…et « Unifier » quitte le menu");
                            Check(shell.UndoForProbe() && odd.Document.Paragraphs.Count == paragraphsBefore && shell.PageCountForProbe(odd) == pagesBefore, "Ctrl+Z retire la page blanche");
                            await Settle();
                            // Le livre : la pastille sur sa ligne quand il finit impair.
                            var bookOdd = Marabook.Model.BookFlow.Analyse(flowBook, shell.PageCountForProbe).BookEndsOdd;
                            Check(shell.Binder.HasFlowDotForProbe(flowBook.Id) == bookOdd, "la ligne du livre porte la pastille si et seulement si le livre finit sur une page impaire (" + (bookOdd ? "oui" : "non") + ")");
                            // Débrayé : plus rien, ni pastille ni menu.
                            flowBook.Book.FlowWarnings = false;
                            shell.Binder.Rebuild();
                            await Settle();
                            Check(shell.ChapterOddEndForProbe(odd) == 0 && !shell.Binder.HasFlowDotForProbe(odd.Id) && !shell.Binder.MenuHasUnifyForProbe(odd.Id) && !shell.Binder.HasFlowDotForProbe(flowBook.Id),
                                "avertissement débrayé sur le livre : ni pastille, ni « Unifier »");
                            flowBook.Book.FlowWarnings = true;
                            shell.Binder.Rebuild();
                            await Settle();
                            // La page livre : la case de Gabarits & Format, la check-list.
                            shell.Binder.SelectItem(flowBook.Id, true);
                            await Settle();
                            var bookView = shell.BookViewPublic;
                            Check(bookView.FlowWarningsBoxForProbe.IsChecked == true, "Gabarits & Format › Contenu & Fichiers : la case suit le livre (cochée)");
                            bookView.SelectedTab = 4;
                            await Settle();
                            var publication = bookView.PublicationForProbe;
                            Check(publication.RowIsRedForProbe("flow-row") == true, "check-list : la ligne d'enchaînement est là, en rouge (critique)");
                            Check(publication.RowIsRedForProbe("template-row") != null, "check-list : la ligne du gabarit est là");
                            Check(publication.DividersForProbe() == publication.RowsForProbe() - 1, "les lignes sont séparées par des filets (" + publication.DividersForProbe() + " filets pour " + publication.RowsForProbe() + " lignes)");
                            var ordered = new List<BinderItem>();
                            Marabook.Model.BookFlow.Texts(flowBook, ordered);
                            Check(publication.StatsRowsForProbe() == ordered.Count, "Tailles › Statistiques : une ligne par texte (" + publication.StatsRowsForProbe() + ")");
                            Check(publication.HoverStatsRowForProbe(0, true) && publication.HoverStatsRowForProbe(0, false), "…la ligne entière se surligne au survol, et s'éteint");
                            // L'ordre des lignes (29/10) : apparition d'office, puis mots décroissants.
                            var byPages = new List<string>();
                            foreach (var t in ordered) byPages.Add(t.Title);
                            Check(string.Join("|", publication.StatsTitlesForProbe().ToArray()) == string.Join("|", byPages.ToArray()), "…dans l'ordre d'apparition d'office");
                            publication.SetStatsOrderForProbe(2); // Mots, décroissant
                            await Settle();
                            var byWords = new List<BinderItem>(ordered);
                            var wordCounts = new Dictionary<string, int>();
                            foreach (var t in ordered) wordCounts[t.Id] = Marabook.Correction.TextStats.Compute(t.Document.ToPlainText()).Words;
                            byWords.Sort(delegate(BinderItem a, BinderItem b) { var by = wordCounts[b.Id].CompareTo(wordCounts[a.Id]); return by != 0 ? by : ordered.IndexOf(a).CompareTo(ordered.IndexOf(b)); });
                            var expected = new List<string>();
                            foreach (var t in byWords) expected.Add(t.Title);
                            Check(string.Join("|", publication.StatsTitlesForProbe().ToArray()) == string.Join("|", expected.ToArray()), "…« Mots, décroissant » réordonne les lignes (" + string.Join(", ", expected.ToArray()) + ")");
                            publication.SetStatsOrderForProbe(0);
                            await Settle();
                            bookView.FlowWarningsBoxForProbe.IsChecked = false;
                            await Settle();
                            Check(!flowBook.Book.FlowWarnings && publication.RowIsRedForProbe("flow-row") == null, "décocher la case débraye l'avertissement : la ligne quitte la check-list");
                            bookView.FlowWarningsBoxForProbe.IsChecked = true;
                            await Settle();
                            Check(flowBook.Book.FlowWarnings && publication.RowIsRedForProbe("flow-row") == true, "…et la recocher la ramène");
                            bookView.SelectedTab = 0;
                            shell.Binder.SelectItem(chapter.Id, true);
                            await Settle();
                        }
                    }
                }
                // — Le total du livre est STABLE (07/10 soir) : ouvrir les
                //   chapitres l'un après l'autre ne l'additionne pas en boucle,
                //   et chaque chapitre dit SES pages, sans le folio du livre.
                {
                    var bookTotal = shell.StatusBookText;
                    var ownPages = shell.StatusPagesText;
                    for (var round = 0; round < 3; round++)
                        foreach (var text in book.Children)
                        {
                            shell.Binder.SelectItem(text.Id, true);
                            await Settle();
                        }
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    Check(shell.StatusBookText == bookTotal, "le total du livre ne bouge pas en parcourant ses chapitres trois fois (" + bookTotal + " → " + shell.StatusBookText + ")");
                    Check(shell.StatusPagesText == ownPages && shell.CachedPageCountForProbe(book.Children[2]) == 1, "chaque chapitre compte ses propres pages, sans le folio (chap. 3 : " + shell.CachedPageCountForProbe(book.Children[2]) + ")");
                }
                Check(FontCatalog.Entries.Count > 10, "le catalogue de polices énumère les polices installées (" + FontCatalog.Entries.Count + ")");
                Check(!string.IsNullOrEmpty(editor.CurrentFontName), "le sélecteur de police montre la police du caret (" + editor.CurrentFontName + ")");
                var engine = new AvaloniaFontEngine();
                var face = engine.Resolve("Times New Roman", 400, false);
                Check(face.HasGlyphs && face.Baseline > 0.7 && face.Baseline < 1.0, "le moteur de polices résout Times New Roman (ligne de base " + face.Baseline.ToString("0.000") + ")");
                var advance = engine.AdvanceWidth("Times New Roman", 12, 400, false, 'a');
                Check(advance > 4 && advance < 8, "…et mesure l'avance d'un « a » à 12 px (" + advance.ToString("0.00") + ")");
                int faceIndex; string fallbackName;
                var fontFile = engine.FontFile(face, out faceIndex, out fallbackName);
                Check(fontFile != null && fontFile.Length > 100000, "…et lit le fichier de la police pour le PDF (" + (fontFile == null ? "aucun" : fontFile.Length + " octets") + ")");

                // — Frapper, annuler, mettre en gras.
                var document = chapter.Document;
                var original = document.Paragraphs[0].ToPlainText();
                composed.PlaceCaret(0, 0, false);
                composed.TypeText("Sonde ");
                await Settle();
                Check(document.Paragraphs[0].ToPlainText() == "Sonde " + original, "la frappe entre dans le pivot");
                Check(composed.Undo() && document.Paragraphs[0].ToPlainText() == original, "Ctrl+Z la retire");
                // — Les touches mortes (15/10, 1.0.6) : l'éditeur déclare un
                //   client de saisie ; le « ^ » en composition s'affiche au
                //   caret sans entrer dans le pivot, la lettre composée entre
                //   seule ensuite.
                composed.PlaceCaret(0, 0, false);
                Check(composed.ImeClientForProbe != null && composed.ImeClientForProbe.SupportsPreedit, "l'éditeur déclare un client de saisie (touches mortes mac)");
                composed.SetPreeditForProbe("^");
                await Settle();
                Check(composed.PreeditShownForProbe && document.Paragraphs[0].ToPlainText() == original, "« ^ » en composition s'affiche au caret sans entrer dans le pivot");
                Check(composed.CaretLeftForProbe > composed.PreeditLeftForProbe + 2, "…et le caret se tient après lui");
                composed.SetPreeditForProbe(null);
                composed.TypeText("ê");
                await Settle();
                Check(!composed.PreeditShownForProbe && document.Paragraphs[0].ToPlainText() == "ê" + original, "…puis la lettre composée entre seule (ê, pas ^e)");
                Check(composed.Undo() && document.Paragraphs[0].ToPlainText() == original, "…et Ctrl+Z la retire");
                // — La majuscule automatique (24/10, 1.0.6) : « élan » en tête
                //   de paragraphe prend sa capitale à l'espace ; Ctrl+Z la
                //   refuse et la suite de la frappe ne la remet pas.
                {
                    var wasOn = composed.AutoCapitalize;
                    composed.AutoCapitalize = true;
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("élan");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText().StartsWith("élan"), "« élan » en cours de frappe reste tel quel");
                    composed.TypeText(" ");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText().StartsWith("Élan "), "…l'espace pose la capitale accentuée (" + document.Paragraphs[0].ToPlainText().Substring(0, 5) + ")");
                    Check(composed.Undo() && document.Paragraphs[0].ToPlainText().StartsWith("élan "), "Ctrl+Z rend la minuscule sans effacer la frappe, espace compris");
                    composed.TypeText("x ");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText().StartsWith("élan x "), "…et la suite de la frappe ne la remet pas (refus)");
                    // Entrée termine le mot comme un espace.
                    for (var round = 0; round < 6 && document.Paragraphs[0].ToPlainText() != original; round++) composed.Undo();
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("ému");
                    composed.InsertParagraphBreak();
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText() == "Ému", "Entrée après « ému » pose la capitale avant de couper (" + document.Paragraphs[0].ToPlainText() + ")");
                    for (var round = 0; round < 6 && document.Paragraphs[0].ToPlainText() != original; round++) composed.Undo();
                    Check(document.Paragraphs[0].ToPlainText() == original, "la frappe s'annule entièrement");
                    composed.AutoCapitalize = false;
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("élan ");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText().StartsWith("élan "), "réglage éteint : rien ne change");
                    for (var round = 0; round < 6 && document.Paragraphs[0].ToPlainText() != original; round++) composed.Undo();
                    composed.AutoCapitalize = wasOn;
                    Check(document.Paragraphs[0].ToPlainText() == original, "…et le texte est rendu");
                }
                // — Le caret d'un paragraphe VIDE (15/10) : Entrée en tête du
                //   chapitre ouvre un paragraphe vide ; le caret s'y pose à
                //   l'alinéa, là où le texte du voisin commence, pas à la marge.
                composed.PlaceCaret(0, 0, false);
                composed.InsertParagraphBreak();
                await Settle();
                Check(document.Paragraphs[0].ToPlainText() == "" && document.Paragraphs[1].ToPlainText() == original, "Entrée en tête : un paragraphe vide devant le texte");
                composed.PlaceCaret(1, 0, false);
                await Settle();
                var textLeft = composed.CaretLeftForProbe;
                composed.PlaceCaret(0, 0, false);
                await Settle();
                var emptyLeft = composed.CaretLeftForProbe;
                var indentPx = emptyLeft - composed.PageLeftForProbe;
                Check(Math.Abs(emptyLeft - textLeft) < 0.5 && indentPx > 5, "le caret du paragraphe vide est à l'alinéa, comme le texte du voisin (" + indentPx.ToString("0.0") + " px de la marge)");
                // — Un article de liste VIDE : le caret après la puce et son
                //   écart (espaces rognés en bout de ligne compris), pas
                //   contre la marge ni collé au point.
                composed.ApplyList("bullet");
                await Settle();
                var bulletLeft = composed.CaretLeftForProbe;
                Check(document.Paragraphs[0].ListKind == "bullet" && bulletLeft > emptyLeft + 8, "article de liste vide : le caret après « • » et son écart (+" + (bulletLeft - emptyLeft).ToString("0.0") + " px)");
                // — Les niveaux de liste (15/10) : Tab descend d'un cran
                //   (sous-puce), Maj+Tab remonte ; Entrée sur un article
                //   vide le referme, Retour arrière en tête retire la puce.
                Check(AppSettings.EditorActionFor("Tab", Settings.KeyModifiers.None) == "list-indent"
                    && AppSettings.EditorActionFor("Tab", Settings.KeyModifiers.Shift) == "list-outdent", "Tab et Maj+Tab sont les gestes « niveau suivant / précédent » de la table des raccourcis");
                Check(composed.RunEditorAction("list-indent") && document.Paragraphs[0].ListLevel == 1, "Tab sur la puce : niveau 1 (sous-puce)");
                await Settle();
                Check(composed.CaretLeftForProbe > bulletLeft + 20, "…la sous-puce est décalée d'un cran de plus (+" + (composed.CaretLeftForProbe - bulletLeft).ToString("0.0") + " px)");
                composed.TypeText("Sous-point");
                await Settle();
                composed.InsertParagraphBreak();
                await Settle();
                Check(document.Paragraphs.Count >= 3 && document.Paragraphs[1].ListKind == "bullet" && document.Paragraphs[1].ListLevel == 1 && document.Paragraphs[1].ToPlainText() == "", "Entrée sur un article plein : un article vide de même niveau");
                composed.InsertParagraphBreak();
                await Settle();
                Check(document.Paragraphs[1].ListKind == "bullet" && document.Paragraphs[1].ListLevel == 0 && document.Paragraphs[1].ToPlainText() == "", "Entrée sur l'article vide de niveau 1 : il remonte au niveau 0, sans paragraphe de plus");
                composed.InsertParagraphBreak();
                await Settle();
                Check(document.Paragraphs[1].ListKind == null && document.Paragraphs[1].ToPlainText() == "" && document.Paragraphs[2].ToPlainText() == original, "Entrée sur l'article vide de niveau 0 : la liste se referme, le paragraphe reste (ordinaire)");
                Check(!composed.RunEditorAction("list-indent"), "Tab hors liste : le geste ne consomme pas la touche");
                composed.PlaceCaret(0, 0, false);
                Check(composed.RunEditorAction("list-outdent") && document.Paragraphs[0].ListLevel == 0 && document.Paragraphs[0].ListKind == "bullet", "Maj+Tab sur la sous-puce : retour au niveau 0, toujours une puce");
                Check(!composed.RunEditorAction("list-outdent"), "Maj+Tab au niveau 0 : rien à remonter, la touche n'est pas consommée");
                composed.BackspaceForProbe();
                await Settle();
                Check(document.Paragraphs[0].ListKind == null && document.Paragraphs[0].ToPlainText() == "Sous-point", "Retour arrière en tête d'une puce : la puce s'en va, le texte reste");
                composed.BackspaceForProbe();
                await Settle();
                Check(document.Paragraphs[0].ToPlainText() == "Sous-point", "…et en tête du premier paragraphe, rien de plus ne bouge");
                for (var round = 0; round < 12 && document.Paragraphs[0].ToPlainText() != original; round++) composed.Undo();
                Check(document.Paragraphs[0].ToPlainText() == original && document.Paragraphs[0].ListKind == null, "…et Ctrl+Z referme le paragraphe");
                composed.SelectAll();
                composed.ToggleBold();
                await Settle();
                var bold = document.Paragraphs[0].Runs.Count > 0 && document.Paragraphs[0].Runs[0].Bold == true;
                Check(bold, "tout sélectionner + gras : le premier run est en gras");
                Check(composed.Undo() && !(document.Paragraphs[0].Runs.Count > 0 && document.Paragraphs[0].Runs[0].Bold == true), "…et Ctrl+Z le rend");
                composed.PlaceCaret(0, 0, false);

                // — Les liens refondus (07/10) : l'expression sélectionnée
                //   devient le texte du lien avec la cible choisie ; sous le
                //   caret, le lien se relit, se réécrit (cible, texte) et se
                //   retire en gardant les mots ; à l'export rien ne dépasse.
                {
                    composed.TypeText("Voir elle ici. ");
                    composed.SelectRange(0, 5, 9); // « elle »
                    composed.ApplyWikiLink("Keira Varenh", "elle");
                    await Settle();
                    var flat = PivotEdit.FlatText(document.Paragraphs[0]);
                    Check(flat.StartsWith("Voir [[Keira Varenh|elle]] ici. "), "éditeur : la sélection devient le texte d'un lien à cible (« " + flat.Substring(0, Math.Min(34, flat.Length)) + " »)");
                    composed.PlaceCaret(0, 8, false);
                    int linkParagraph;
                    var atCaret = composed.LinkAtCaret(out linkParagraph);
                    Check(atCaret != null && atCaret.Target == "Keira Varenh" && atCaret.Text == "elle", "éditeur : le lien sous le caret se relit (cible et texte)");
                    composed.ApplyWikiLink("Keira Varenh", "la capitaine");
                    await Settle();
                    flat = PivotEdit.FlatText(document.Paragraphs[0]);
                    Check(flat.StartsWith("Voir [[Keira Varenh|la capitaine]] ici. "), "éditeur : le lien sous le caret est réécrit avec son nouveau texte");
                    Check(Links.Strip(flat).StartsWith("Voir la capitaine ici. "), "éditeur : à l'export seul le texte choisi reste");
                    composed.PlaceCaret(0, 8, false);
                    var removed = composed.RemoveLinkAtCaret();
                    await Settle();
                    flat = PivotEdit.FlatText(document.Paragraphs[0]);
                    Check(removed && flat.StartsWith("Voir la capitaine ici. "), "éditeur : retirer le lien garde les mots");
                    composed.PlaceCaret(0, 2, false);
                    composed.SelectRange(0, 0, 4); // « Voir », pas un lien
                    int noParagraph;
                    Check(composed.LinkAtCaret(out noParagraph) == null, "éditeur : hors d'un lien, rien à modifier");
                    composed.ReplaceRange(0, 0, "Voir la capitaine ici. ".Length, "");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText() == original, "éditeur : le texte de la sonde est retiré (« " + document.Paragraphs[0].ToPlainText().Substring(0, Math.Min(20, document.Paragraphs[0].ToPlainText().Length)) + "… »)");
                    composed.PlaceCaret(0, 0, false);

                    // — Le VRAI dialogue (07/10) : l'expression « Keira Varenh »
                    //   sélectionnée, Ctrl+K propose la fiche, « Insérer » remplace
                    //   l'expression (et non « [[Keira Varenh]]Keira Varenh »), et
                    //   le rail Général liste le lien aussitôt.
                    composed.TypeText("Voir Keira Varenh ici. ");
                    composed.SelectRange(0, 5, 17);
                    shell.InsertLinkPublic();
                    await Settle();
                    var linkDialog = OpenWindow<LinkDialog>();
                    Check(linkDialog != null, "éditeur : Ctrl+K ouvre le dialogue du lien");
                    if (linkDialog != null)
                    {
                        var combo = linkDialog.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
                        var textBox = linkDialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(delegate(TextBox b) { return b.Watermark != null; });
                        Check(combo != null && combo.Text == "Keira Varenh" && textBox != null && textBox.Text == "Keira Varenh", "éditeur : la cible est proposée d'office, le texte est l'expression (« " + (combo == null ? "?" : combo.Text) + " » / « " + (textBox == null ? "?" : textBox.Text) + " »)");
                        var insert = FindButton(linkDialog, "Insérer");
                        if (insert != null) insert.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else linkDialog.Close();
                        await Settle();
                        await Settle();
                    }
                    flat = PivotEdit.FlatText(document.Paragraphs[0]);
                    Check(flat.StartsWith("Voir [[Keira Varenh]] ici. "), "éditeur : « Insérer » remplace l'expression par le lien (« " + flat.Substring(0, Math.Min(30, flat.Length)) + " »)");
                    Check(shell.LinksPanelTextsForProbe().Contains("Keira Varenh"), "éditeur : le rail Général liste le lien aussitôt");
                    composed.ReplaceRange(0, 0, "Voir [[Keira Varenh]] ici. ".Length, "");
                    await Settle();
                    Check(document.Paragraphs[0].ToPlainText() == original, "éditeur : le texte de la sonde est retiré");
                    composed.PlaceCaret(0, 0, false);
                }

                // — La géométrie suit la disposition (hotfix 1.0.3-a) : sur un
                // écrit de dizaines de pages, le caret et la sélection posés à
                // la dernière page sont à la hauteur de leur ligne DISPOSÉE. La
                // hauteur A4 n'est pas entière (1 122,52 px) et l'arrondi de
                // disposition d'Avalonia pose chaque page au pixel : la
                // géométrie supposée dérivait d'un demi-pixel par page.
                {
                    var countBefore = document.Paragraphs.Count;
                    var filler = new List<TextParagraph>();
                    for (var i = 0; i < 240; i++)
                    {
                        var paragraph = new TextParagraph();
                        paragraph.Runs.Add(new TextRun { Text = "Paragraphe de remplissage numéro " + i + " : il pleuvait sur la ville et les toits luisaient sous les réverbères, tandis que les passants pressaient le pas vers des portes closes, et que la nuit tombait sans bruit sur les quais déserts." });
                        document.Paragraphs.Add(paragraph);
                        filler.Add(paragraph);
                    }
                    composed.RefreshComposition();
                    await Settle();
                    var pagesNow = composed.CurrentComposition.Pages.Count;
                    composed.PlaceCaret(document.Paragraphs.Count - 1, 10, false);
                    await Settle();
                    var drift = composed.CaretDriftPx();
                    Check(pagesNow >= 12 && drift >= 0 && drift < 0.5,
                        "sur " + pagesNow + " pages, le caret de la dernière page est à la hauteur de sa ligne disposée (écart " + drift.ToString("0.00") + " px)");
                    composed.PlaceCaret(document.Paragraphs.Count - 1, 0, false);
                    composed.PlaceCaret(document.Paragraphs.Count - 1, 10, true);
                    await Settle();
                    drift = composed.SelectionDriftPx();
                    Check(drift >= 0 && drift < 0.5, "…et la sélection aussi (écart " + drift.ToString("0.00") + " px)");
                    composed.PlaceCaret(0, 0, false);
                    foreach (var paragraph in filler) document.Paragraphs.Remove(paragraph);
                    composed.RefreshComposition();
                    await Settle();
                    Check(document.Paragraphs.Count == countBefore, "le remplissage est retiré (" + document.Paragraphs.Count + " paragraphes)");
                }

                // — La recherche simple (hotfix 1.0.3-a) : occurrence précédente
                // et compteur « X/total ».
                {
                    var needle = original.Length >= 2 ? original.Substring(0, 2) : "e";
                    var total = Marabook.Model.PivotSearch.FindAll(document, needle, false, false).Count;
                    composed.PlaceCaret(0, 0, false);
                    var first = editor.SearchForProbe(needle, false);
                    var second = editor.SearchForProbe(needle, false);
                    var backOne = editor.SearchForProbe(needle, true);
                    var wrapped = editor.SearchForProbe(needle, true);
                    Check(total >= 2 && first == "1/" + total && second == "2/" + total,
                        "recherche : le compteur suit les occurrences (" + first + ", " + second + " ; " + total + " attendues)");
                    Check(backOne == "1/" + total && wrapped == "Reprise à la fin — " + total + "/" + total,
                        "recherche : « occurrence précédente » recule puis reprend à la fin (" + backOne + ", " + wrapped + ")");
                    editor.HideSearch();
                    composed.PlaceCaret(0, 0, false);
                    await Settle();
                }

                // — Le miroir épinglé (hotfix 1.0.3-a) : un bloc sélectionnable,
                // copiable au clic droit, les runs gras et italique gardés.
                {
                    var mirrorStack = Ui.PlainDocument(document, 12.5, Chrome.Ink) as StackPanel;
                    var mirror = mirrorStack == null || mirrorStack.Children.Count == 0 ? null : mirrorStack.Children[0] as SelectableTextBlock;
                    Check(mirror != null && mirror.Inlines != null && mirror.Inlines.Count > 0 && mirror.ContextMenu != null,
                        "le miroir épinglé est fait de blocs sélectionnables avec leur menu Copier (" + (mirror == null ? "-" : mirror.Inlines.Count.ToString()) + " inlines)");
                }

                // — Glisser-déposer de la sélection (1.0.3) : les cinq premiers
                // caractères déplacés après le douzième, en une étape d'annulation ;
                // un dépôt dans la sélection ne fait rien.
                if (original.Length > 14)
                {
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, 5, true);
                    Check(!composed.MoveSelectionTo(0, 3), "déposer la sélection sur elle-même ne fait rien");
                    var moved = composed.MoveSelectionTo(0, 12);
                    await Settle();
                    var expected = original.Substring(5, 7) + original.Substring(0, 5) + original.Substring(12);
                    Check(moved && document.Paragraphs[0].ToPlainText() == expected, "glisser-déposer : la sélection se déplace après le douzième caractère");
                    Check(composed.Undo() && document.Paragraphs[0].ToPlainText() == original, "…et Ctrl+Z la ramène");
                    composed.PlaceCaret(0, 0, false);
                }

                // — Copier avec mise en forme, coller (28/09) : le gras voyage
                // par le presse-papiers ; sans mise en forme, il ne voyage pas.
                // Sous Xvfb (CI Ubuntu) le presse-papiers ne rend rien : la
                // vérification est sautée quand un texte nu ne fait pas
                // l'aller-retour (01/10).
                var clipboardWorks = false;
                try
                {
                    var top = TopLevel.GetTopLevel(composed);
                    if (top != null && top.Clipboard != null)
                    {
                        await top.Clipboard.SetTextAsync("sonde-presse-papiers");
                        await Settle();
                        clipboardWorks = await top.Clipboard.GetTextAsync() == "sonde-presse-papiers";
                    }
                }
                catch (Exception) { clipboardWorks = false; }
                if (!clipboardWorks) Console.WriteLine("  (presse-papiers indisponible ici : copier/coller sauté)");
                else try
                {
                    var paragraphs = document.Paragraphs.Count;
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true); // les cinq premiers caractères
                    composed.ToggleBold();
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true);
                    composed.Copy(true);
                    await Settle();
                    var end = document.Paragraphs[paragraphs - 1];
                    composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                    await composed.PasteAsync();
                    await Settle();
                    var pasted = end.Runs.Count > 0 ? end.Runs[end.Runs.Count - 1] : null;
                    Check(pasted != null && pasted.Bold == true && pasted.Text == original.Substring(0, Math.Min(5, original.Length)),
                        "copier avec mise en forme puis coller : le gras voyage (" + (pasted == null ? "rien collé" : pasted.Text + ", gras=" + pasted.Bold) + ")");
                    composed.Undo();
                    end = document.Paragraphs[paragraphs - 1]; // l'annulation a pu remplacer le paragraphe
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true);
                    composed.Copy(false);
                    await Settle();
                    composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                    await composed.PasteAsync();
                    await Settle();
                    pasted = end.Runs.Count > 0 ? end.Runs[end.Runs.Count - 1] : null;
                    Check(pasted != null && pasted.Bold != true && end.ToPlainText().EndsWith(original.Substring(0, Math.Min(5, original.Length))),
                        "copier sans mise en forme puis coller : le texte seul voyage");
                    composed.Undo(); // le collage
                    composed.Undo(); // le gras
                    composed.PlaceCaret(0, 0, false);

                    // — Coller PLUSIEURS paragraphes (hotfix 1.0.3-a) : une
                    // sélection qui court du début du premier paragraphe au
                    // quatrième caractère du deuxième, copiée mise en forme puis
                    // collée à la fin de l'écrit — un paragraphe de plus, le
                    // compositeur en phase, aucune exception (« Index was out of
                    // range » avant, dès qu'un fragment avait plusieurs paragraphes).
                    var second = 1; // le premier paragraphe non vide après le premier
                    while (second < paragraphs - 1 && PivotEdit.FlatLength(document.Paragraphs[second]) == 0) second++;
                    if (paragraphs >= 2)
                    {
                        var secondText = document.Paragraphs[second].ToPlainText();
                        var cut = Math.Min(4, secondText.Length);
                        composed.PlaceCaret(0, 0, false);
                        composed.PlaceCaret(second, cut, true);
                        composed.Copy(true);
                        await Settle();
                        end = document.Paragraphs[paragraphs - 1];
                        composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                        await composed.PasteAsync();
                        await Settle();
                        var lastText = document.Paragraphs[document.Paragraphs.Count - 1].ToPlainText();
                        Check(document.Paragraphs.Count == paragraphs + second && lastText == secondText.Substring(0, cut),
                            "coller " + (second + 1) + " paragraphes à la fin : " + second + " de plus, le dernier morceau termine l'écrit (" + document.Paragraphs.Count + " paragraphes, « " + lastText + " »)");
                        Check(composed.CurrentComposition.Paragraphs.Count == document.Paragraphs.Count,
                            "…et le compositeur a autant de paragraphes que le document (" + composed.CurrentComposition.Paragraphs.Count + ")");
                        Check(composed.Undo() && document.Paragraphs.Count == paragraphs, "…et Ctrl+Z retire le collage d'un coup");
                        composed.PlaceCaret(0, 0, false);
                    }

                    // — Coller du texte venu d'AILLEURS (texte plat de trois
                    // lignes posé au presse-papiers, comme depuis un autre
                    // programme) : deux paragraphes de plus, appris d'un coup.
                    {
                        var top = TopLevel.GetTopLevel(composed);
                        await top.Clipboard.SetTextAsync("ligne un" + Environment.NewLine + "ligne deux" + Environment.NewLine + "ligne trois");
                        await Settle();
                        end = document.Paragraphs[paragraphs - 1];
                        composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                        await composed.PasteAsync();
                        await Settle();
                        var lastText = document.Paragraphs[document.Paragraphs.Count - 1].ToPlainText();
                        Check(document.Paragraphs.Count == paragraphs + 2 && lastText == "ligne trois"
                            && composed.CurrentComposition.Paragraphs.Count == document.Paragraphs.Count,
                            "coller trois lignes de texte plat : deux paragraphes de plus, le compositeur en phase (" + document.Paragraphs.Count + " paragraphes, « " + lastText + " »)");
                        Check(composed.Undo() && document.Paragraphs.Count == paragraphs, "…et Ctrl+Z les retire d'un coup");
                        composed.PlaceCaret(0, 0, false);
                    }

                    // — Coller TOUT ce qui est sélectionné (hotfix 1.0.3-a) : une
                    // note, une annotation et une image posées dans le premier
                    // paragraphe, copiées puis collées à la fin de l'écrit → une
                    // note et une annotation de plus (identifiants neufs), un run
                    // d'image qui cite une image connue du magasin du projet.
                    if (original.Length > 6)
                    {
                        var notesBefore = document.Footnotes.Count;
                        var annotationsBefore = document.Annotations.Count;
                        var imagesBefore = shell.Project.Images.Count;
                        composed.PlaceCaret(0, 2, false);
                        composed.InsertFootnoteAtCaret(); // « Le¹ » : l'appel à l'offset 2
                        await Settle();
                        var note = document.Footnotes[document.Footnotes.Count - 1];
                        note.Text = "note de la sonde";
                        composed.PlaceCaret(0, 0, false);
                        composed.PlaceCaret(0, 2, true);
                        var annotation = new Annotation { Text = "commentaire de la sonde", Created = "2026-10-05 12:00" };
                        if (composed.AnnotateSelection(annotation.Id)) document.Annotations.Add(annotation);
                        composed.PlaceCaret(0, 4, false);
                        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
                        var imageId = shell.Project.AddImage(png, ".png");
                        composed.InsertElementAtCaret(new TextRun { ImageId = imageId, Image = new ImageLayout { Name = "sonde.png", Width = 24, Height = 24 } });
                        await Settle();
                        Check(document.Footnotes.Count == notesBefore + 1 && document.Annotations.Count == annotationsBefore + 1,
                            "une note, une annotation et une image posées dans le premier paragraphe");
                        composed.PlaceCaret(0, 0, false);
                        composed.PlaceCaret(0, 7, true); // « Le¹ v▣e » : l'appel, l'ancre et l'image
                        composed.Copy(true);
                        await Settle();
                        var endIndex = document.Paragraphs.Count - 1;
                        composed.PlaceCaret(endIndex, PivotEdit.FlatLength(document.Paragraphs[endIndex]), false);
                        await composed.PasteAsync();
                        await Settle();
                        string pastedNote = null, pastedAnchor = null, pastedImage = null;
                        foreach (var run in document.Paragraphs[endIndex].Runs)
                        {
                            if (run.FootnoteId != null) pastedNote = run.FootnoteId;
                            if (run.AnnotationId != null) pastedAnchor = run.AnnotationId;
                            if (run.ImageId != null) pastedImage = run.ImageId;
                        }
                        Check(pastedNote != null && pastedNote != note.Id && document.FindFootnote(pastedNote) != null
                            && document.FindFootnote(pastedNote).Text == "note de la sonde" && document.Footnotes.Count == notesBefore + 2,
                            "coller : l'appel de note collé cite une note neuve au même texte (" + document.Footnotes.Count + " notes)");
                        Check(pastedAnchor != null && pastedAnchor != annotation.Id && document.FindAnnotation(pastedAnchor) != null
                            && document.FindAnnotation(pastedAnchor).Text == "commentaire de la sonde" && document.Annotations.Count == annotationsBefore + 2,
                            "coller : l'ancre collée cite une annotation neuve au même texte (" + document.Annotations.Count + " annotations)");
                        Check(pastedImage == imageId && shell.Project.FindImage(pastedImage) != null && shell.Project.Images.Count == imagesBefore + 1,
                            "coller : l'image collée cite l'image du magasin, sans doublon (" + shell.Project.Images.Count + " images)");
                        Check(composed.CurrentComposition.Paragraphs.Count == document.Paragraphs.Count, "…et le compositeur est en phase");
                        // Retour à l'état d'avant : le collage, l'image, l'annotation, la note.
                        for (var back = 0; back < 6 && (document.Paragraphs[0].ToPlainText() != original || document.Footnotes.Count > notesBefore); back++)
                            if (!composed.Undo()) break;
                        document.Annotations.Remove(annotation);
                        document.AnnotationOrder(true); // les annotations vivent hors du flux d'annulation : purge des orphelines
                        PivotEdit.PurgeFootnotes(document);
                        Check(document.Paragraphs[0].ToPlainText() == original && document.Footnotes.Count == notesBefore && document.Annotations.Count == annotationsBefore,
                            "…et Ctrl+Z rend l'écrit d'avant (" + document.Footnotes.Count + " notes, " + document.Annotations.Count + " annotations)");
                        composed.PlaceCaret(0, 0, false);
                    }
                }
                catch (Exception error) { Check(false, "copier/coller mis en forme : " + error.Message); }

                // — L'annulation par écrit (28/09) : A modifié, B modifié, A
                // modifié ; deux Ctrl+Z sur A rendent A, B reste tel quel ;
                // un rechargement (styles) ne vide pas la pile.
                try
                {
                    var chapterB = book.Children[1];
                    var originalA = document.Paragraphs[0].ToPlainText();
                    var originalB = chapterB.Document.Paragraphs[0].ToPlainText();
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("Un ");
                    await Settle();
                    shell.Binder.SelectItem(chapterB.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("Bé ");
                    await Settle();
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, PivotEdit.FlatLength(document.Paragraphs[0]), false);
                    composed.TypeText(" deux");
                    await Settle();
                    editor.Reload(); // la feuille de styles a « changé »
                    await Settle();
                    // Une correction automatique (b45) peut demander un Ctrl+Z
                    // de plus (le premier la refuse) : on recule jusqu'à l'état.
                    var stepsOne = 0;
                    while (stepsOne < 3 && document.Paragraphs[0].ToPlainText() != "Un " + originalA && composed.Undo()) stepsOne++;
                    var afterOne = document.Paragraphs[0].ToPlainText();
                    var stepsTwo = 0;
                    while (stepsTwo < 3 && document.Paragraphs[0].ToPlainText() != originalA && composed.Undo()) stepsTwo++;
                    var afterTwo = document.Paragraphs[0].ToPlainText();
                    Check(stepsOne > 0 && afterOne == "Un " + originalA && stepsTwo > 0 && afterTwo == originalA,
                        "annuler sur A rend « phrase 2 » puis « phrase 1 » (" + stepsOne + " + " + stepsTwo + " Ctrl+Z, après un rechargement)");
                    Check(chapterB.Document.Paragraphs[0].ToPlainText() == "Bé " + originalB, "…et B n'a pas bougé");
                    shell.Binder.SelectItem(chapterB.Id, true);
                    await Settle();
                    Check(composed.Undo() && chapterB.Document.Paragraphs[0].ToPlainText() == originalB, "Ctrl+Z sur B rend B");
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "annulation par écrit : " + error.Message); }

                // — L'annulation d'un changement de STYLE (28/09) : la taille
                // des notes de bas de page change, Ctrl+Z la rend, Ctrl+Y la
                // remet ; entre les deux, le texte n'a pas bougé.
                try
                {
                    var note = shell.Project.Styles.FootnoteStyle();
                    var sizeBefore = note.FontSize;
                    var textBefore = document.Paragraphs[0].ToPlainText();
                    note.FontSize = sizeBefore + 4;
                    shell.StylesEditedInPlace();
                    await Settle();
                    Check(Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - (sizeBefore + 4)) < 0.01, "la taille des notes est changée dans la feuille");
                    var undone = composed.Undo();
                    await Settle();
                    Check(undone && Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - sizeBefore) < 0.01,
                        "Ctrl+Z rend la taille des notes d'avant (" + shell.Project.Styles.FootnoteStyle().FontSize + ")");
                    Check(document.Paragraphs[0].ToPlainText() == textBefore, "…sans toucher au texte");
                    var redone = composed.Redo();
                    await Settle();
                    Check(redone && Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - (sizeBefore + 4)) < 0.01, "Ctrl+Y remet le changement de style");
                    composed.Undo();
                    await Settle();
                    Check(Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - sizeBefore) < 0.01, "…et Ctrl+Z le rend encore");
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "annulation d'un style : " + error.Message); }

                // — La mise en forme d'une note depuis le ruban (28/09) : la
                // note ouverte passe en gras (toute la note sans sélection),
                // Ctrl+Z la rend ; le texte n'a pas bougé.
                try
                {
                    var noteIds = composed.MarkerOrder();
                    if (noteIds.Count == 0)
                    {
                        composed.PlaceCaret(0, 0, false);
                        composed.InsertFootnoteAtCaret();
                        await Settle();
                        noteIds = composed.MarkerOrder();
                    }
                    Check(noteIds.Count > 0, "l'écrit a une note de bas de page (" + noteIds.Count + ")");
                    var noteId = noteIds[0];
                    var textBefore = document.Paragraphs[0].ToPlainText();
                    composed.EditNote(noteId);
                    composed.TypeText("note sonde");
                    await Settle();
                    composed.ToggleBold();
                    await Settle();
                    var footnote = document.FindFootnote(noteId);
                    var allBold = footnote != null && footnote.Runs.Count > 0;
                    if (footnote != null) foreach (var run in footnote.Runs) if (!PivotEdit.IsElement(run) && run.Bold != true) allBold = false;
                    Check(allBold, "gras depuis le ruban : toute la note passe en gras (" + (footnote == null ? "note absente" : footnote.Text) + ")");
                    Check(document.Paragraphs[0].ToPlainText() == textBefore, "…sans toucher au texte");
                    composed.Undo();
                    await Settle();
                    footnote = document.FindFootnote(noteId);
                    var anyBold = false;
                    if (footnote != null) foreach (var run in footnote.Runs) if (run.Bold == true) anyBold = true;
                    Check(!anyBold, "Ctrl+Z rend la note sans gras");
                    composed.CloseNoteEditor(true);
                    composed.Undo(); composed.Undo(); // la frappe, puis la note insérée s'il a fallu l'insérer
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "mise en forme d'une note : " + error.Message); }

                // — Le PDF avec le moteur Avalonia : police embarquée.
                var pdfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-p2.pdf");
                try
                {
                    var composition = Marabook.Print.Composer.Compose(document, shell.Project.Styles.EffectiveFor(chapter), shell.Project.Page, shell.Project, engine);
                    Marabook.Print.PdfWriter.Write(pdfPath, composition, new Marabook.Print.PdfExportOptions());
                    var pdf = System.IO.File.ReadAllBytes(pdfPath);
                    var text = System.Text.Encoding.Latin1.GetString(pdf);
                    Check(pdf.Length > 5000 && text.StartsWith("%PDF") && text.Contains("/FontFile"), "le PDF s'écrit avec la police TrueType embarquée (" + pdf.Length + " octets)");
                }
                catch (Exception error) { Check(false, "le PDF s'écrit : " + error.Message); }
                finally { try { System.IO.File.Delete(pdfPath); } catch { } }

                // — Glisser-déposer de fichiers du système (29/09) : un .docx
                // et un .md lâchés sur le livre deviennent ses écrits ; un
                // fichier quelconque lâché dans Recherche devient un média.
                var dropDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-drop-" + Guid.NewGuid().ToString("N"));
                try
                {
                    System.IO.Directory.CreateDirectory(dropDir);
                    var docxPath = System.IO.Path.Combine(dropDir, "Chapitre déposé.docx");
                    var mdPath = System.IO.Path.Combine(dropDir, "Note déposée.md");
                    var binPath = System.IO.Path.Combine(dropDir, "Pièce déposée.dat");
                    Exchange.Docx.Export(document, shell.Project.Styles, docxPath, shell.Project.Page, null, shell.Project);
                    System.IO.File.WriteAllText(mdPath, "# Une note\n\nDéposée depuis l'Explorateur.");
                    System.IO.File.WriteAllBytes(binPath, new byte[] { 1, 2, 3, 4 });
                    var bookBefore = book.Children.Count;
                    shell.ImportDroppedFiles(book, new[] { docxPath, mdPath });
                    await Settle();
                    var droppedDocx = book.Children.FirstOrDefault(c => c.Title == "Chapitre déposé");
                    var droppedMd = book.Children.FirstOrDefault(c => c.Title == "Note déposée");
                    Check(book.Children.Count == bookBefore + 2 && droppedDocx != null && droppedMd != null
                        && droppedDocx.Kind == ItemKind.Text && droppedMd.Kind == ItemKind.Text,
                        "deux documents déposés sur le livre deviennent ses écrits (" + (book.Children.Count - bookBefore) + ")");
                    Check(droppedDocx != null && droppedDocx.Document.ToPlainText().Contains(document.Paragraphs[0].ToPlainText().Substring(0, 12)),
                        "…le .docx a gardé son texte");
                    Check(droppedMd != null && droppedMd.Document.ToPlainText().Contains("Déposée depuis"), "…le .md aussi");
                    var research = shell.Project.Category(Project.KeyResearch);
                    var researchBefore = research.Children.Count;
                    shell.ImportDroppedFiles(research, new[] { binPath });
                    await Settle();
                    var media = research.Children.FirstOrDefault(c => c.Title == "Pièce déposée");
                    Check(research.Children.Count == researchBefore + 1 && media != null && media.Kind == ItemKind.Media && media.MediaExtension == ".dat",
                        "un fichier déposé dans Recherche devient un média");
                }
                catch (Exception error) { Check(false, "glisser-déposer de fichiers : " + error.Message); }
                finally { try { System.IO.Directory.Delete(dropDir, true); } catch { } }

                // — P3 : le tour des vues — chaque racine, chaque élément, les
                // panneaux de droite ; une vue qui lève une exception fait
                // tomber le processus, c'est le verdict.
                var visited = 0;
                foreach (var root in shell.Project.Roots)
                {
                    shell.Binder.SelectItem(root.Id, true);
                    await Settle();
                    visited++;
                }
                Check(visited == shell.Project.Roots.Count, "chaque racine de la Pile s'ouvre (" + visited + ")");

                // — Le verrou du rail (hotfix 1.0.3-a) : le cadenas est en bas du
                // rail ; verrouillé, la colonne de droite ouverte sur le Général
                // reste ouverte sur une racine (où le Général est indisponible) ;
                // déverrouillé, elle se replie comme avant.
                {
                    var lockTab = shell.GetVisualDescendants().OfType<Border>()
                        .FirstOrDefault(b => (ToolTip.GetTip(b) as string ?? "").Contains("errouill"));
                    Check(lockTab != null && lockTab.IsEffectivelyVisible, "le cadenas du rail est là");
                    var lockedBefore = AppSettings.RailLocked;
                    var panelBefore = AppSettings.RightPanel;
                    var chapterForLock = shell.Project.AllItems().FirstOrDefault(i => i.Kind == ItemKind.Text);
                    var rootForLock = shell.Project.Roots.FirstOrDefault(r => !r.IsHomeRoot); // une racine SANS Général (l'accueil en a un)
                    if (chapterForLock != null && rootForLock != null)
                    {
                        AppSettings.RailLocked = true;
                        shell.Binder.SelectItem(chapterForLock.Id, true);
                        await Settle();
                        shell.SetRightPanelPublic(RightPanel.Inspector);
                        await Task.Delay(400); await Settle();
                        var openOnText = shell.RightColumnWidth;
                        shell.Binder.SelectItem(rootForLock.Id, true);
                        await Task.Delay(400); await Settle();
                        var lockedOnRoot = shell.RightColumnWidth;
                        Check(openOnText > 100 && lockedOnRoot > 100 && AppSettings.RightPanel == RightPanel.Inspector,
                            "rail verrouillé : la colonne reste ouverte sur une racine et le Général reste choisi (" + openOnText.ToString("0") + " → " + lockedOnRoot.ToString("0") + " px)");
                        // Épingler sous verrou, colonne repliée : l'épingle se pose, la colonne reste repliée.
                        shell.SetRightPanelPublic(RightPanel.None);
                        await Task.Delay(500); await Settle();
                        shell.PinByTitle(chapterForLock.Title);
                        await Task.Delay(400); await Settle();
                        Check(shell.RightColumnWidth < 1 && AppSettings.RightPanel == RightPanel.None,
                            "rail verrouillé : épingler un écrit n'ouvre pas la colonne repliée (" + shell.RightColumnWidth.ToString("0") + " px)");
                        AppSettings.RailLocked = false;
                        shell.Binder.SelectItem(chapterForLock.Id, true);
                        await Task.Delay(400); await Settle();
                        shell.Binder.SelectItem(rootForLock.Id, true);
                        await Task.Delay(600); await Settle();
                        var freeOnRoot = shell.RightColumnWidth;
                        Check(freeOnRoot < 1, "rail déverrouillé : la colonne se replie sur une racine (" + freeOnRoot.ToString("0") + " px)");
                        // Le miroir épinglé tient dans sa colonne (hotfix 1.0.3-a) :
                        // le premier bloc ne déborde ni du cadre ni du viseur.
                        shell.Binder.SelectItem(chapterForLock.Id, true);
                        await Settle();
                        shell.SetRightPanelPublic(RightPanel.Pinned);
                        await Task.Delay(500); await Settle();
                        bool mirrorFits;
                        var mirrorReport = shell.PinnedPanelForProbe.MirrorLayoutReport(out mirrorFits);
                        Check(mirrorFits, "le miroir épinglé tient dans sa colonne (" + mirrorReport + ")");
                        // …même long (ascenseur vertical) : le texte ne passe pas sous l'ascenseur.
                        var pinFiller = new List<TextParagraph>();
                        for (var i = 0; i < 80; i++)
                        {
                            var paragraph = new TextParagraph();
                            paragraph.Runs.Add(new TextRun { Text = "Paragraphe de remplissage numéro " + i + " : il pleuvait sur la ville et les toits luisaient sous les réverbères, tandis que les passants pressaient le pas vers des portes closes." });
                            chapterForLock.Document.Paragraphs.Add(paragraph);
                            pinFiller.Add(paragraph);
                        }
                        shell.PinnedPanelForProbe.Refresh();
                        await Task.Delay(300); await Settle();
                        mirrorReport = shell.PinnedPanelForProbe.MirrorLayoutReport(out mirrorFits);
                        Check(mirrorFits, "…et un long miroir avec son ascenseur aussi (" + mirrorReport + ")");
                        foreach (var paragraph in pinFiller) chapterForLock.Document.Paragraphs.Remove(paragraph);
                        shell.PinnedPanelForProbe.Refresh();
                        await Settle();
                    }
                    AppSettings.RailLocked = lockedBefore;
                    shell.SetRightPanelPublic(panelBefore);
                    await Settle();
                }
                var kinds = new HashSet<ItemKind>();
                var wrong = new List<string>();
                foreach (var item in shell.Project.AllItems())
                {
                    if (item.IsCategory) continue;
                    shell.Binder.SelectItem(item.Id, true);
                    await Settle();
                    kinds.Add(item.Kind);
                    var expected = item.Kind == ItemKind.Text ? "editor" : item.Kind == ItemKind.Sheet ? "sheet"
                        : item.Kind == ItemKind.Book ? "book" : item.Kind == ItemKind.Plan ? "plan" : item.Kind == ItemKind.Media ? "media"
                        : item.Kind == ItemKind.MindMap ? "mindmap" : item.Kind == ItemKind.PageTemplate ? "template"
                        : item.Kind == ItemKind.Folder && item.RootCategory().CategoryKey == Project.KeySheets ? "library" : "corkboard";
                    if (shell.VisibleView != expected) wrong.Add(item.Title + " : " + shell.VisibleView + " au lieu de " + expected);
                }
                Check(wrong.Count == 0, "chaque élément ouvre sa vue" + (wrong.Count == 0 ? "" : " — " + string.Join(" ; ", wrong.ToArray())));
                Check(kinds.Contains(ItemKind.Sheet) && kinds.Contains(ItemKind.Book) && kinds.Contains(ItemKind.Plan)
                    && kinds.Contains(ItemKind.Folder) && kinds.Contains(ItemKind.Media) && kinds.Contains(ItemKind.Text),
                    "écrit, fiche, livre, dossier, plan et média s'affichent (" + kinds.Count + " natures)");
                // — Les onglets du livre, le mode wiki d'une fiche.
                if (book != null)
                {
                    shell.Binder.SelectItem(book.Id, true);
                    await Settle();
                    var tabsSeen = 0;
                    foreach (var tabs in shell.GetVisualDescendants().OfType<TabControl>())
                    {
                        if (!tabs.IsEffectivelyVisible) continue;
                        for (var i = 0; i < tabs.Items.Count; i++) { tabs.SelectedIndex = i; await Settle(); tabsSeen++; }
                        tabs.SelectedIndex = 0;
                    }
                    Check(shell.VisibleView == "book" && tabsSeen >= 5, "le livre ouvre ses onglets (" + tabsSeen + " onglets parcourus)");
                }
                // — Les plans (hotfix 1.0.3-a) : vingt colonnes de plus, chacune
                // en moins de 300 ms (la Pile n'est plus rebâtie, la colonne
                // seule est ajoutée, le combo des écrits se remplit à l'ouverture) ;
                // le graphique « Tout » fait tenir les colonnes dans la fenêtre.
                var planItem = shell.Project.AllItems().FirstOrDefault(i => i.Kind == ItemKind.Plan);
                if (planItem != null && planItem.Plan != null)
                {
                    shell.Binder.SelectItem(planItem.Id, true);
                    await Settle();
                    var planView = shell.PlanForProbe;
                    var columnsBefore = planItem.Plan.Columns.Count;
                    var slowest = 0.0;
                    for (var i = 0; i < 20; i++)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        planView.ProbeAddColumn();
                        await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Render);
                        slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
                    }
                    Check(planItem.Plan.Columns.Count == columnsBefore + 20 && planView.ColumnControls == columnsBefore + 20,
                        "plan : vingt colonnes ajoutées une à une (" + planView.ColumnControls + " à l'écran)");
                    Check(slowest < 300, "plan : la colonne la plus lente s'ajoute en moins de 300 ms (" + slowest.ToString("0") + " ms)");
                    planView.ProbeShowChart(true);
                    await Settle();
                    planView.Chart.FitAll();
                    await Settle();
                    var chartLaidOut = planView.Chart.ChartLaidOut;
                    Check(!chartLaidOut || planView.Chart.PlotFitsViewport, "graphique : « Tout » fait tenir " + planItem.Plan.Columns.Count + " colonnes dans la fenêtre (" + planView.Chart.FitReport + (chartLaidOut ? "" : " — pas de place pour le tracer ici, non jugé") + ")");
                    planView.Chart.ZoomStep(1);
                    await Settle();
                    Check(!chartLaidOut || !planView.Chart.PlotFitsViewport || planItem.Plan.Columns.Count < 8, "graphique : un cran de zoom écarte les colonnes (" + planView.Chart.FitReport + ")");
                    planView.ProbeShowChart(false);
                    planItem.Plan.Columns.RemoveRange(columnsBefore, 20);
                    planView.Refresh();
                    await Settle();
                    Check(planItem.Plan.Columns.Count == columnsBefore && planView.ColumnControls == Math.Max(1, columnsBefore), "plan : les colonnes de la sonde sont retirées");
                }

                BinderItem sheet = null;
                foreach (var item in shell.Project.AllItems()) if (item.Kind == ItemKind.Sheet) { sheet = item; break; }
                if (sheet != null)
                {
                    shell.Binder.SelectItem(sheet.Id, true);
                    await Settle();
                    if (shell.VisibleView != "sheet") { Console.WriteLine("  [sonde] vue après sélection de la fiche : " + shell.VisibleView + " ; fenêtres ouvertes : " + ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows.Count); }
                    var wiki = FindButton(shell, "Mode wiki");
                    if (wiki != null) { wiki.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Settle(); }
                    Check(shell.VisibleView == "sheet" && wiki != null, "la fiche passe en mode wiki et revient (" + shell.VisibleView + ", bouton " + (wiki == null ? "introuvable parmi " + shell.GetVisualDescendants().OfType<Button>().Count() + " boutons" : "trouvé") + ")");
                    var back = FindButton(shell, "Mode fiche") ?? FindButton(shell, "Mode wiki");
                    if (back != null) { back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Settle(); }

                    // — L'éditeur Markdown de la fiche (hotfix 1.0.3-a) : une
                    // sélection tirée de la fin vers le début se met en gras sans
                    // perdre le passage ; Entrée dans une liste continue la liste,
                    // Entrée sur une puce vide la retire.
                    var sheetView = shell.Sheet;
                    if (sheetView != null && sheetView.HasItem && sheetView.BodyBox != null)
                    {
                        var box = sheetView.BodyBox;
                        var kept = box.Text;
                        box.Text = "abc def";
                        box.SelectionStart = 7; box.SelectionEnd = 4; // « def » à rebours
                        sheetView.Wrap("**", "**");
                        Check(box.Text == "abc **def**", "Markdown : le gras d'une sélection à rebours garde le passage (« " + box.Text + " »)");
                        box.Text = "* item";
                        Ui.Select(box, box.Text.Length, 0);
                        var continued = sheetView.ContinueList();
                        Check(continued && box.Text == "* item\n* " && box.CaretIndex == box.Text.Length, "Markdown : Entrée dans une liste à puces ajoute la puce suivante (« " + box.Text.Replace("\n", "⏎") + " »)");
                        continued = sheetView.ContinueList();
                        Check(continued && box.Text == "* item\n" && box.CaretIndex == box.Text.Length, "Markdown : Entrée sur la puce vide la retire (« " + box.Text.Replace("\n", "⏎") + " »)");
                        // — Les liens refondus (07/10) dans le corps de la fiche.
                        box.Text = "Voir Keira Varenh ici.";
                        Ui.Select(box, 5, 12); // « Keira Varenh »
                        var sheetTitles = shell.Project.AllItems().Where(delegate(BinderItem i) { return i.Kind == ItemKind.Sheet; }).Select(delegate(BinderItem i) { return i.Title; }).ToList();
                        Check(Links.MatchTitle(sheetView.SelectedBodyText(), sheetTitles) == "Keira Varenh", "fiche : l'expression sélectionnée désigne la fiche Keira (cible proposée)");
                        sheetView.ApplyWikiLink("Keira Varenh", "elle");
                        Check(box.Text == "Voir [[Keira Varenh|elle]] ici.", "fiche : la sélection devient le texte du lien (« " + box.Text + " »)");
                        Ui.Select(box, 10, 0);
                        var sheetLink = sheetView.LinkAtCaret();
                        Check(sheetLink != null && sheetLink.Target == "Keira Varenh" && sheetLink.Text == "elle", "fiche : le lien sous le caret se relit");
                        sheetView.ApplyWikiLink("Keira Varenh", "la capitaine");
                        Check(box.Text == "Voir [[Keira Varenh|la capitaine]] ici.", "fiche : le lien sous le caret est réécrit (« " + box.Text + " »)");
                        Ui.Select(box, 10, 0);
                        Check(sheetView.RemoveLinkAtCaret() && box.Text == "Voir la capitaine ici.", "fiche : retirer le lien garde les mots (« " + box.Text + " »)");
                        Ui.Select(box, 0, 4);
                        Check(sheetView.LinkAtCaret() == null, "fiche : hors d'un lien, rien à modifier");
                        Check(!shell.ZoomPanelVisibleForProbe, "fiche : le curseur de zoom est caché hors de l'éditeur");

                        // — Le VRAI dialogue sur le corps (07/10) : la sélection
                        //   relevée avant le modal est remplacée, pas doublée.
                        box.Text = "Voir Keira Varenh ici.";
                        box.Focus();
                        Ui.Select(box, 5, 12);
                        shell.InsertLinkPublic();
                        await Settle();
                        var sheetDialog = OpenWindow<LinkDialog>();
                        Check(sheetDialog != null, "fiche : Ctrl+K ouvre le dialogue du lien");
                        if (sheetDialog != null)
                        {
                            var combo = sheetDialog.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
                            Check(combo != null && combo.Text == "Keira Varenh", "fiche : la cible est proposée d'office");
                            var insert = FindButton(sheetDialog, "Insérer");
                            if (insert != null) insert.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else sheetDialog.Close();
                            await Settle();
                            await Settle();
                        }
                        Check(box.Text == "Voir [[Keira Varenh]] ici.", "fiche : « Insérer » remplace l'expression (« " + box.Text + " »)");
                        Check(shell.LinksPanelTextsForProbe().Contains("Keira Varenh"), "fiche : le rail Général liste le lien aussitôt (" + string.Join(" | ", shell.LinksPanelTextsForProbe().ToArray()) + " ; inspecté : " + shell.InspectorTitle + " ; fiche : " + (sheet == null ? "?" : sheet.Title) + " ; corps : " + box.Text + ")");

                        // — Les champs cross-fiche (07/10) : un champ texte du
                        //   modèle accepte un lien par le même dialogue.
                        var fieldEntry = sheetView.FirstTextFieldForProbe();
                        if (fieldEntry != null)
                        {
                            sheetView.ShowGeneralTabPublic(); // le champ doit être posé à l'écran pour prendre le clavier
                            await Settle();
                            var fieldBox = fieldEntry.Value.Value;
                            var fieldKept = fieldBox.Text;
                            fieldBox.Text = "Amie de Keira Varenh";
                            fieldBox.Focus();
                            await Settle();
                            Ui.Select(fieldBox, 8, 12);
                            Check(sheetView.CaptureLinkContext() == null && sheetView.SelectedBodyText() == "Keira Varenh", "champ : la zone qui a le clavier est servie, l'expression relevée");
                            sheetView.ApplyWikiLink("Keira Varenh", "elle");
                            await Settle();
                            Check(fieldBox.Text == "Amie de [[Keira Varenh|elle]]", "champ : le lien remplace l'expression dans le champ (« " + fieldBox.Text + " »)");
                            string fieldValue;
                            sheet.FieldValues.TryGetValue(fieldEntry.Value.Key, out fieldValue);
                            Check(fieldValue == fieldBox.Text, "champ : la valeur de la fiche suit");
                            Ui.Select(fieldBox, 12, 0);
                            var fieldLink = sheetView.LinkAtCaret();
                            Check(fieldLink != null && fieldLink.Target == "Keira Varenh", "champ : le lien sous le caret se relit");
                            Check(sheetView.RemoveLinkAtCaret() && fieldBox.Text == "Amie de elle", "champ : retirer le lien garde les mots");
                            fieldBox.Text = fieldKept ?? "";
                            await Task.Delay(1600); // la fenêtre de fusion des frappes (1,5 s) : la sonde Ctrl+Z qui suit compte ses propres actions
                            await Settle();
                        }
                        else Console.WriteLine("  [sonde] pas de champ texte dans le modèle : champs cross-fiche sautés");

                        // — Un champ LISTE (07/10 soir) : Ctrl+K pose le lien dans
                        //   la liste, jamais dans le Texte libre, et l'onglet ne bouge pas.
                        // Le modèle d'exemple n'a pas de liste : un champ est ajouté
                        // le temps de la sonde, la fiche rechargée, puis retiré.
                        var probeTemplate = shell.Project.FindTemplate(sheet.TemplateId);
                        SheetField probeList = null;
                        if (probeTemplate != null && sheetView.FirstFieldBoxForProbe(FieldKinds.List) == null)
                        {
                            probeList = new SheetField { Id = "sonde-liste", Name = "Sonde liste", Kind = FieldKinds.List, Group = probeTemplate.Fields.Count > 0 ? probeTemplate.Fields[0].Group : "" };
                            probeTemplate.Fields.Add(probeList);
                            sheetView.LoadItem(sheet, probeTemplate);
                            await Settle();
                        }
                        var listEntry = sheetView.FirstFieldBoxForProbe(FieldKinds.List);
                        if (listEntry != null)
                        {
                            sheetView.ShowGeneralTabPublic();
                            await Settle();
                            // La liste refondue (07/10) : la zone de saisie
                            // ne porte que l'élément EN COURS, Entrée le pose
                            // dans la liste (la valeur du champ), le lien se
                            // pose dans la zone avant.
                            var listId = listEntry.Value.Key;
                            string listKept;
                            sheet.FieldValues.TryGetValue(listId, out listKept);
                            // Le champ repart VIDE (le projet d'exemple a « Talents » garni) :
                            // les comptes de pastilles qui suivent sont ceux de la sonde.
                            sheet.FieldValues[listId] = "";
                            sheetView.LoadItem(sheet, probeTemplate);
                            await Settle();
                            sheetView.ShowGeneralTabPublic();
                            await Settle();
                            listEntry = sheetView.FirstFieldBoxForProbe(FieldKinds.List);
                            var listBox = listEntry.Value.Value;
                            var bodyBefore = box.Text;
                            listBox.Text = "escrime, Keira Varenh";
                            listBox.Focus();
                            await Settle();
                            Ui.Select(listBox, 9, 12);
                            shell.InsertLinkPublic();
                            await Settle();
                            var listDialog = OpenWindow<LinkDialog>();
                            if (listDialog != null)
                            {
                                var insert = FindButton(listDialog, "Insérer");
                                if (insert != null) insert.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else listDialog.Close();
                                await Settle();
                                await Settle();
                            }
                            Check(listDialog != null && listBox.Text == "escrime, [[Keira Varenh]]", "liste : Ctrl+K pose le lien dans la zone de saisie de la liste (« " + listBox.Text + " »)");
                            Check(box.Text == bodyBefore, "liste : le Texte libre n'a pas bougé");
                            Check(sheetView.GeneralTabShownForProbe, "liste : l'onglet Général reste ouvert");
                            listBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = listBox });
                            await Settle();
                            string listValue;
                            sheet.FieldValues.TryGetValue(listId, out listValue);
                            Check(listValue == "escrime, [[Keira Varenh]]" && (listBox.Text ?? "").Length == 0,
                                "liste : Entrée pose les éléments dans la liste et vide la zone (« " + listValue + " »)");
                            var crosses = 0;
                            var stripped = false;
                            var marks = false;
                            TextBlock cross = null;
                            foreach (var block in sheetView.GetVisualDescendants().OfType<TextBlock>())
                            {
                                if (block.Text == "×") { crosses++; if (cross == null) cross = block; }
                                if (block.Text == "Keira Varenh") stripped = true;
                                if (block.Text == "[[Keira Varenh]]") marks = true;
                            }
                            Check(crosses == 2 && stripped && !marks, "liste : deux pastilles à croix, le lien sans ses marques (" + crosses + " croix)");
                            if (cross != null)
                            {
                                cross.RaiseEvent(new PointerPressedEventArgs(cross, new Avalonia.Input.Pointer(1, PointerType.Mouse, true), cross, new Point(1, 1), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None));
                                await Settle();
                                sheet.FieldValues.TryGetValue(listId, out listValue);
                                Check(listValue == "[[Keira Varenh]]", "liste : la croix retire l'élément (« " + listValue + " »)");
                            }
                            if (listKept == null) sheet.FieldValues.Remove(listId); else sheet.FieldValues[listId] = listKept;
                            sheetView.LoadItem(sheet, probeTemplate);
                            await Task.Delay(1600);
                            await Settle();
                        }
                        else Console.WriteLine("  [sonde] pas de champ liste dans le modèle : lien dans une liste sauté");
                        if (probeList != null)
                        {
                            probeTemplate.Fields.Remove(probeList);
                            sheet.FieldValues.Remove(probeList.Id);
                            sheetView.LoadItem(sheet, probeTemplate);
                            await Settle();
                        }

                        // — Éditeur de modèles (1.0.4) : les flèches haut / bas
                        //   réordonnent les champs dans leur section. Le dialogue
                        //   travaille sur des copies : Annuler ne change rien au projet.
                        if (probeTemplate != null && probeTemplate.Fields.Count >= 2)
                        {
                            var orderBefore = string.Join("|", probeTemplate.Fields.Select(f => f.Id));
                            var templatesTask = TemplatesDialog.Show(shell, shell.Project.Templates, shell.Project);
                            await Settle();
                            await Settle();
                            var templates = OpenWindow<TemplatesDialog>();
                            Check(templates != null, "l'éditeur de modèles s'ouvre");
                            if (templates != null)
                            {
                                var editing = templates.CurrentForProbe;
                                var ups = templates.FieldsPanelForProbe.GetVisualDescendants().OfType<Button>().Where(b => (ToolTip.GetTip(b) as string) == "Monter ce champ").ToList();
                                var downs = templates.FieldsPanelForProbe.GetVisualDescendants().OfType<Button>().Where(b => (ToolTip.GetTip(b) as string) == "Descendre ce champ").ToList();
                                Check(editing != null && ups.Count == editing.Fields.Count && downs.Count == editing.Fields.Count, "chaque champ a ses flèches haut et bas (" + ups.Count + ")");
                                var first = editing == null ? null : editing.Fields[0];
                                var second = editing == null ? null : editing.NeighbourField(first, +1) >= 0 ? editing.Fields[editing.NeighbourField(first, +1)] : null;
                                Check(ups.Count > 0 && !ups[0].IsEnabled && downs.Count > 0 && downs[0].IsEnabled, "le premier champ : « monter » éteint, « descendre » allumé");
                                if (downs.Count > 0 && second != null)
                                {
                                    downs[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                                    await Settle();
                                    Check(editing.Fields.IndexOf(second) + 1 == editing.Fields.IndexOf(first), "« descendre » passe le champ sous son voisin de section (" + first.Name + " ↔ " + second.Name + ")");
                                    var ups2 = templates.FieldsPanelForProbe.GetVisualDescendants().OfType<Button>().Where(b => (ToolTip.GetTip(b) as string) == "Monter ce champ").ToList();
                                    var row = ups2.Count > 1 ? ups2[1] : null;
                                    if (row != null) row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                                    await Settle();
                                    Check(editing.Fields.IndexOf(first) + 1 == editing.Fields.IndexOf(second), "« monter » le ramène à sa place");
                                }
                                var cancel = FindButton(templates, "Annuler");
                                if (cancel != null) cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else templates.Close();
                                await Settle();
                            }
                            try { await templatesTask; } catch { }
                            Check(string.Join("|", probeTemplate.Fields.Select(f => f.Id)) == orderBefore, "Annuler : l'ordre des champs du projet n'a pas bougé");
                        }
                        box.Text = "2. deux";
                        Ui.Select(box, box.Text.Length, 0);
                        sheetView.ContinueList();
                        Check(box.Text == "2. deux\n3. ", "Markdown : la liste numérotée compte (« " + box.Text.Replace("\n", "⏎") + " »)");
                        box.Text = "texte";
                        Ui.Select(box, 2, 0);
                        Check(!sheetView.ContinueList() && box.Text == "texte", "Markdown : hors liste, Entrée reste une Entrée ordinaire");
                        box.Text = kept;
                        await Settle();
                    }

                    // — Ctrl+Z sur les champs (1.0.4) : une valeur tapée dans un
                    // champ du modèle est rendue par l'annulation (modèle ET
                    // zone, la vue rechargée), puis refaite ; les frappes
                    // successives ne font qu'UNE action.
                    var field = sheetView == null || !sheetView.HasItem ? null : sheetView.FirstTextFieldForProbe();
                    if (field != null)
                    {
                        var fieldId = field.Value.Key;
                        string initial;
                        sheet.FieldValues.TryGetValue(fieldId, out initial);
                        initial = initial ?? "";
                        var pending = shell.HistoryCountForProbe;
                        field.Value.Value.Text = "Sonde";
                        await Settle();
                        field.Value.Value.Text = "Sonde 1.0.4";
                        await Settle();
                        string typed;
                        sheet.FieldValues.TryGetValue(fieldId, out typed);
                        Check(typed == "Sonde 1.0.4" && shell.HistoryCountForProbe == pending + 1,
                            "la frappe dans un champ de fiche écrit la valeur et pose UNE action (" + (shell.HistoryCountForProbe - pending) + ")");
                        var undone = shell.UndoPublic();
                        await Settle();
                        string restored;
                        sheet.FieldValues.TryGetValue(fieldId, out restored);
                        var reloaded = sheetView.FirstTextFieldForProbe();
                        Check(undone && (restored ?? "") == initial && reloaded != null && (reloaded.Value.Value.Text ?? "") == initial,
                            "Ctrl+Z rend le champ de fiche : modèle « " + (restored ?? "") + " », zone « " + (reloaded == null ? "?" : reloaded.Value.Value.Text) + " »");
                        var redone = shell.RedoPublic();
                        await Settle();
                        sheet.FieldValues.TryGetValue(fieldId, out typed);
                        Check(redone && typed == "Sonde 1.0.4", "Ctrl+Y refait la frappe (« " + typed + " »)");
                        shell.UndoPublic();
                        await Settle();
                    }

                    // — Le correcteur du corps (1.0.4) : le Markdown masqué à
                    // longueur constante, puis, dictionnaire présent, deux mots
                    // inconnus soulignés et pas le nom de fiche entre [[ ]].
                    var masked = SheetView.MaskMarkdown("Voir [[Kaladinn]] et `kode` sur https://exemple.fr/x puis [lien](https://a.b) fin");
                    Check(masked.Length == "Voir [[Kaladinn]] et `kode` sur https://exemple.fr/x puis [lien](https://a.b) fin".Length
                        && !masked.Contains("Kaladinn") && !masked.Contains("kode") && !masked.Contains("exemple") && !masked.Contains("a.b") && masked.Contains("Voir") && masked.Contains("fin"),
                        "le masque Markdown efface liens wiki, code et adresses sans bouger les offsets");
                    if (sheetView != null && sheetView.HasItem && sheetView.BodyBox != null)
                    {
                        var kept = sheetView.BodyBox.Text;
                        sheetView.BodyBox.Text = "Un tezte avec une fôte et [[Kaladinn]] dedans.";
                        await Settle();
                        var count = sheetView.SpellNowForProbe();
                        if (count < 0) Console.WriteLine("  [sonde] dictionnaire absent à côté de l'exécutable : correcteur des fiches sauté");
                        else Check(count == 2, "le corps de la fiche souligne « tezte » et « fôte », pas le lien wiki (" + count + " signalement(s))");
                        // Le menu du clic droit unifié (10/10) : édition, puis le
                        // signalement sous le clic avec ses suggestions.
                        if (count == 2)
                        {
                            var menu = sheetView.BuildBodyMenuForProbe("Un ".Length + 1);
                            var headers = new List<string>();
                            foreach (var entry in menu.Items) { var item = entry as MenuItem; if (item != null) headers.Add(item.Header as string ?? ""); }
                            Check(headers.Contains("Couper") && headers.Contains("Coller") && headers.Contains("Tout sélectionner"),
                                "clic droit dans le corps Markdown : Couper / Copier / Coller / Tout sélectionner (" + string.Join(" · ", headers.ToArray()) + ")");
                            Check(headers.Contains("texte") && headers.Contains("Ignorer ici") && headers.Exists(delegate(string h) { return h.StartsWith("Ajouter « tezte »"); }),
                                "…puis la suggestion « texte », Ignorer ici, Ajouter au dictionnaire");
                            var plain = sheetView.BuildBodyMenuForProbe("Un tezte avec une fôte et [[Kaladinn]] dedans.".Length - 2);
                            var plainHeaders = new List<string>();
                            foreach (var entry in plain.Items) { var item = entry as MenuItem; if (item != null) plainHeaders.Add(item.Header as string ?? ""); }
                            Check(plainHeaders.Count == 4 && !plainHeaders.Contains("Ignorer ici"), "…et rien de plus sur un mot sain");
                        }
                        // 1.0.5-b : le clic droit au-delà de la première ligne —
                        // l'index sous le point d'un [[lien]] de la seconde ligne
                        // (HitTestPoint le jugeait « dehors »).
                        sheetView.BodyBox.Text = "Première ligne du corps.\nSeconde ligne avec [[Kaladinn]] dedans.";
                        await Settle();
                        var linkAt = sheetView.BodyBox.Text.IndexOf("Kaladinn", StringComparison.Ordinal);
                        var found = sheetView.BodyIndexRoundTripForProbe(linkAt + 2);
                        Check(found >= 0 && Links.At(sheetView.BodyBox.Text, found) != null,
                            "le clic droit sur la seconde ligne retrouve le lien sous le pointeur (index " + found + ")");
                        // 1.0.5-b : une image du magasin insérée dans le corps —
                        // « ![nom](image:ID) » seule sur sa ligne, gardée par la
                        // sauvegarde, rendue en vraie image par le miroir.
                        var imageId = shell.Project.AddImage(ProbePng(), ".png");
                        sheetView.BodyBox.Text = "Avant";
                        Ui.Select(sheetView.BodyBox, 5, 0);
                        sheetView.InsertStoredImage(imageId, "Le phare");
                        await Settle();
                        var bodyText = sheetView.BodyBox.Text ?? "";
                        Check(bodyText == "Avant\n![Le phare](image:" + imageId + ")",
                            "l'image insérée au caret cite le magasin sur sa propre ligne (« " + bodyText.Replace("\n", "⏎") + " »)");
                        Check(shell.Project.UsedImageIds().Contains(imageId), "…et la sauvegarde la garde");
                        var rendered = MarkdownRender.Build(bodyText, delegate { }, delegate { }, SheetWiki.ImagesOf(shell.Project)) as StackPanel;
                        var pictures = 0;
                        if (rendered != null)
                            foreach (var child in rendered.Children)
                            {
                                var block = child as TextBlock;
                                if (block == null || block.Inlines == null) continue;
                                foreach (var inline in block.Inlines)
                                {
                                    var container = inline as Avalonia.Controls.Documents.InlineUIContainer;
                                    if (container != null && container.Child is Image) pictures++;
                                }
                            }
                        Check(pictures == 1, "…et le rendu Markdown la montre en vraie image (" + pictures + ")");
                        shell.Project.Images.Remove(imageId);
                        sheetView.BodyBox.Text = kept;
                        await Settle();
                    }
                }

                // — Un lien du Texte libre sur une ligne RENVOYÉE répond sous
                // la souris (TextLayout.HitTestPoint d'Avalonia le jugeait
                // « dehors » au-delà de la première ligne — 29/09).
                {
                    var source = "Texte assez long pour être renvoyé sur plusieurs lignes dans une colonne étroite, "
                        + "avec du **gras** et de l'*italique* avant le lien, et enfin [[Le marabout]]";
                    var body = MarkdownRender.Build(source, delegate { }, delegate { });
                    var lab = new Window { Width = 360, Height = 240, Content = new Border { Width = 320, Child = body } };
                    lab.Show();
                    await Settle();
                    TextBlock paragraph = null;
                    foreach (var block in lab.GetVisualDescendants().OfType<TextBlock>())
                        if (block.Tag != null) { paragraph = block; break; }
                    var lines = paragraph == null || paragraph.TextLayout == null ? 0 : paragraph.TextLayout.TextLines.Count;
                    var onLink = false; var offLink = true;
                    if (lines >= 2)
                    {
                        var last = paragraph.TextLayout.TextLines[lines - 1];
                        var y = paragraph.TextLayout.Height - last.Height / 2;
                        onLink = MarkdownRender.HasLinkAt(paragraph, new Point(last.Start + last.Width - 6, y));
                        var first = paragraph.TextLayout.TextLines[0];
                        offLink = MarkdownRender.HasLinkAt(paragraph, new Point(first.Start + 6, first.Height / 2));
                    }
                    Check(lines >= 2 && onLink && !offLink, "le lien d'une ligne renvoyée du Texte libre répond sous la souris (" + lines + " lignes, lien " + onLink + ", texte " + offLink + ")");
                    lab.Close();
                }

                // — Le mode wiki rend les [[liens]] des champs (07/10) : texte
                //   du lien en accent souligné, cliquable.
                {
                    var keira = shell.Project.FindByTitle("Keira Varenh");
                    var template = keira == null ? null : shell.Project.FindTemplate(keira.TemplateId);
                    SheetField textField = null;
                    if (template != null)
                        foreach (var f in template.Fields)
                            if (FieldKinds.Normalize(f.Kind) == FieldKinds.Text) { textField = f; break; }
                    if (keira != null && textField != null)
                    {
                        string keptValue;
                        var had = keira.FieldValues.TryGetValue(textField.Id, out keptValue);
                        keira.FieldValues[textField.Id] = "Élève du [[Le marabout|vieux marabout]]";
                        string clicked = null;
                        var wiki = SheetWiki.Build(keira, template, shell.Project, "", true, delegate { }, delegate(string t) { clicked = t; }, delegate { });
                        var lab = new Window { Width = 420, Height = 500, Content = wiki };
                        lab.Show();
                        await Settle();
                        Avalonia.Controls.Documents.Run linkRun = null;
                        foreach (var block in lab.GetVisualDescendants().OfType<TextBlock>())
                            foreach (var inline in block.Inlines ?? new Avalonia.Controls.Documents.InlineCollection())
                            {
                                var run = inline as Avalonia.Controls.Documents.Run;
                                if (run != null && run.Text == "vieux marabout") linkRun = run;
                            }
                        Check(linkRun != null && linkRun.TextDecorations != null && ReferenceEquals(linkRun.Foreground, Chrome.Accent), "mode wiki : le lien d'un champ se rend en accent souligné, marques cachées");
                        lab.Close();
                        if (had) keira.FieldValues[textField.Id] = keptValue; else keira.FieldValues.Remove(textField.Id);
                    }
                    else Console.WriteLine("  [sonde] pas de champ texte sur Keira : rendu wiki des champs sauté");
                }

                // — Sous-catégories de fiches (1.0.5) : une boîte dans la
                //   bibliothèque, le modèle hérité plus les champs propres sur
                //   la fiche, le chemin « Personnage › Héros » en tête.
                {
                    var project = shell.Project;
                    var keira = project.FindByTitle("Keira Varenh");
                    var top = keira == null ? null : project.TopSheetCategoryOf(keira);
                    if (keira != null && top != null && !keira.IsDescendantOf(project.Trash))
                    {
                        var keptCategory = keira.CategoryId;
                        var heroes = project.AddSubCategory(top, "Héros (sonde)");
                        var oath = new SheetField { Name = "Serment (sonde)", Kind = FieldKinds.Multiline };
                        heroes.ExtraFields.Add(oath);
                        keira.CategoryId = heroes.Id;
                        shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                        await Settle();
                        var library = shell.SheetLibraryForProbe;
                        Check(library != null && library.IsVisible && library.SubBoxIdsForProbe().Contains(heroes.Id),
                            "bibliothèque : la sous-catégorie a sa boîte dans la rangée de « " + top.Name + " »");
                        // — L'entrée des tuiles (1.0.6) : à l'ouverture du panneau, les
                        //   tuiles arrivent en fondu décalé ; une demi-seconde plus tard
                        //   tout est posé (opacité 1), et un rebâti sur recherche n'anime pas.
                        if (library != null)
                        {
                            var entering = library.EnteringCardsForProbe();
                            Check(entering > 0, "à l'ouverture, les tuiles entrent en fondu (" + entering + " en cours)");
                            await Task.Delay(900); await Settle();
                            Check(library.EnteringCardsForProbe() == 0, "…et sont toutes posées après la course");
                            library.Refresh();
                            await Settle();
                            Check(library.EnteringCardsForProbe() == 0, "un rafraîchissement ne rejoue pas l'entrée");
                            // — L'entrée menée par l'écran (retour de Rémi, 14/10) : hors de la zone
                            //   visible, les tuiles attendent effacées ; le défilement les fait
                            //   entrer, une sortie franche les efface pour qu'elles rejouent.
                            // La bibliothèque de démonstration est courte : soixante fiches de
                            // passage la font défiler, retirées à la fin.
                            var fillerHome = project.Category(Project.KeySheets);
                            var fillers = new List<BinderItem>();
                            for (var f = 0; f < 60; f++)
                            {
                                var filler = new BinderItem { Kind = ItemKind.Sheet, Title = "Figurant " + (f + 1) + " (sonde)", Parent = fillerHome, CategoryId = top.Id, TemplateId = top.TemplateId };
                                fillerHome.Children.Add(filler);
                                fillers.Add(filler);
                            }
                            shell.Binder.SelectItem(project.Category(Project.KeyWritings).Id);
                            await Settle();
                            shell.Binder.SelectItem(fillerHome.Id);
                            await Settle();
                            library = shell.SheetLibraryForProbe;
                            await Task.Delay(900); await Settle();
                            if (library.ScrollableForProbe)
                            {
                                Check(library.PendingCardsForProbe() > 0, "les tuiles hors de l'écran attendent, effacées (" + library.PendingCardsForProbe() + ")");
                                library.ScrollToForProbe(library.ScrollExtentForProbe);
                                await Settle();
                                Check(library.EnteringCardsForProbe() > 0 || library.PendingCardsForProbe() == 0, "défiler jusqu'au bout les fait entrer (" + library.EnteringCardsForProbe() + " en cours)");
                                await Task.Delay(900); await Settle();
                                Check(library.EnteringCardsForProbe() == 0, "…et elles se posent");
                                library.ScrollToForProbe(0);
                                await Settle();
                                Check(library.PendingCardsForProbe() > 0, "revenu en haut : les tuiles du bas sorties de l'écran sont effacées, prêtes à rejouer (" + library.PendingCardsForProbe() + ")");
                                await Task.Delay(900); await Settle();
                            }
                            else Check(false, "la bibliothèque garnie de soixante figurants défile");
                            foreach (var filler in fillers) fillerHome.Children.Remove(filler);
                            shell.Binder.Rebuild();
                            shell.Binder.SelectItem(fillerHome.Id);
                            await Settle();
                            var saved = AppSettings.Animations;
                            AppSettings.Animations = false;
                            shell.Binder.SelectItem(project.Category(Project.KeyWritings).Id);
                            await Settle();
                            shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                            await Settle();
                            Check(shell.SheetLibraryForProbe.EnteringCardsForProbe() == 0, "Animations décoché : les tuiles se posent d'un coup");
                            AppSettings.Animations = saved;
                        }
                        // — L'optimisation des images à l'import (1.0.6) : une grande image
                        //   opaque devient un JPEG de 2 048 px de côté ; avec transparence, un
                        //   PNG ; une petite reste intacte ; le quart de tour et le recadrage
                        //   donnent leurs dimensions ; l'illisible passe tel quel.
                        var big = ImageImport.Synthetic(3000, 2000, false, true);
                        var optimized = ImageImport.Optimize(big, ".png");
                        Check(optimized.Changed && optimized.Width == 2048 && optimized.Height == 1365 && optimized.Extension == ".jpg" && optimized.Bytes.Length < big.Length,
                            "une image opaque de 3 000 px devient un JPEG de 2 048 px (" + optimized.Width + "×" + optimized.Height + " " + optimized.Extension + ", " + optimized.Bytes.Length / 1024 + " Ko contre " + big.Length / 1024 + ")");
                        var alpha = ImageImport.Optimize(ImageImport.Synthetic(3000, 2000, true, true), ".png");
                        Check(alpha.Changed && alpha.Extension == ".png" && alpha.Width == 2048, "avec de la transparence, elle reste un PNG réduit (" + alpha.Extension + ")");
                        var small = ImageImport.Synthetic(400, 300, false, true);
                        var kept = ImageImport.Optimize(small, ".png");
                        Check(!kept.Changed && ReferenceEquals(kept.Bytes, small) && kept.Extension == ".png", "une petite image reste intacte, octet pour octet");
                        var turned = ImageImport.Apply(small, ".png", 1, SKRectI.Empty);
                        Check(turned != null && turned.Width == 300 && turned.Height == 400, "un quart de tour échange largeur et hauteur (" + (turned == null ? "-" : turned.Width + "×" + turned.Height) + ")");
                        var cropped = ImageImport.Apply(small, ".png", 0, new SKRectI(10, 20, 210, 120));
                        Check(cropped != null && cropped.Width == 200 && cropped.Height == 100, "le recadrage donne ses dimensions (" + (cropped == null ? "-" : cropped.Width + "×" + cropped.Height) + ")");
                        Check(ImageImport.Optimize(new byte[] { 1, 2, 3 }, ".png").Bytes.Length == 3 && !ImageImport.Optimize(new byte[] { 1, 2, 3 }, ".png").Changed, "une image illisible passe telle quelle");
                        string viaCore;
                        Check(Marabook.Model.Platform.Images.Optimize(big, ".png", out viaCore).Length < big.Length && viaCore == ".jpg", "le codec du cœur optimise aussi (imports .docx et Markdown)");
                        // — La fenêtre d'import : pivoter, recadrer, importer ; ou annuler.
                        var editTask = ImageEditDialog.Show(shell, big, ".png");
                        await Settle();
                        ImageEditDialog imageEditor = null;
                        foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                            if (window is ImageEditDialog) imageEditor = (ImageEditDialog)window;
                        Check(imageEditor != null && imageEditor.IsVisible && imageEditor.Decodable && imageEditor.CropForProbe.Width == 3000, "la fenêtre d'import s'ouvre sur toute l'image (" + (imageEditor == null ? "-" : imageEditor.CropForProbe.Width + "×" + imageEditor.CropForProbe.Height) + ")");
                        if (imageEditor != null)
                        {
                            Check(FindButton(imageEditor, "Pivoter à gauche") != null && FindButton(imageEditor, "Pivoter à droite") != null && FindButton(imageEditor, "Toute l'image") != null && FindButton(imageEditor, "Importer") != null, "…avec ses boutons : pivoter à gauche et à droite, toute l'image, importer");
                            imageEditor.TurnForProbe(1);
                            await Settle();
                            Check(imageEditor.TurnsForProbe == 1 && imageEditor.CropForProbe.Width == 2000 && imageEditor.CropForProbe.Height == 3000, "pivoter à droite : le cadre suit l'image tournée (" + imageEditor.CropForProbe.Width + "×" + imageEditor.CropForProbe.Height + ")");
                            imageEditor.SetCropForProbe(new SKRectI(100, 200, 1100, 1700));
                            await Settle();
                            // MARABOOK_PROBE_SHOT_DIR : la fenêtre rendue en PNG (vérification à l'œil, 1.0.6)
                            var shotDir = Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOT_DIR");
                            if (!string.IsNullOrEmpty(shotDir))
                                try
                                {
                                    var content = (Control)imageEditor.Content;
                                    using (var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width * 2, (int)content.Bounds.Height * 2), new Vector(192, 192)))
                                    {
                                        shot.Render(content);
                                        shot.Save(System.IO.Path.Combine(shotDir, "image-edit-dialog.png"));
                                    }
                                }
                                catch (Exception error) { Console.WriteLine("  (capture de la fenêtre d'import impossible : " + error.Message + ")"); }
                            imageEditor.ImportForProbe();
                        }
                        var imported = await editTask;
                        Check(imported != null && imported.Width == 1000 && imported.Height == 1500 && imported.Extension == ".jpg", "« Importer » applique rotation, recadrage et optimisation (" + (imported == null ? "-" : imported.Width + "×" + imported.Height + " " + imported.Extension) + ")");
                        var cancelTask = ImageEditDialog.Show(shell, small, ".png");
                        await Settle();
                        ImageEditDialog second = null;
                        foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                            if (window is ImageEditDialog) second = (ImageEditDialog)window;
                        var cancel = second == null ? null : FindButton(second, "Annuler");
                        if (cancel != null) cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else if (second != null) second.Close(false);
                        Check(await cancelTask == null, "« Annuler » n'importe rien");
                        shell.Binder.SelectItem(keira.Id);
                        await Settle();
                        var sheetView = shell.Sheet;
                        Check(sheetView != null && sheetView.HasItem && sheetView.HasFieldBoxForProbe(oath.Id),
                            "fiche d'une sous-catégorie : le champ propre s'ajoute au modèle hérité");
                        Check(sheetView != null && sheetView.CategoryLabelForProbe.Contains(top.Name + " › Héros (sonde)"),
                            "…et la tête dit le chemin (" + (sheetView == null ? "-" : sheetView.CategoryLabelForProbe) + ")");
                        Check(project.TemplateOf(keira).Fields.Count == project.FindTemplate(keira.TemplateId).Fields.Count + 1
                            && project.BaseTemplateIdOf(heroes) == top.TemplateId,
                            "le modèle effectif = le modèle de base de l'ensemble + 1 champ propre");
                        keira.CategoryId = keptCategory;
                        project.SheetCategories.Remove(heroes);
                        shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                        await Settle();
                        shell.Binder.SelectItem(keira.Id);
                        await Settle();
                        Check(shell.Sheet != null && !shell.Sheet.HasFieldBoxForProbe(oath.Id), "sous-catégorie retirée : le champ propre disparaît de la fiche");
                    }
                    else Console.WriteLine("  [sonde] Keira introuvable : sous-catégories sautées");
                }

                // — Hygiène des liens (1.0.5) : renommer une fiche recible ses
                //   [[liens]], Ctrl+Z rend les deux.
                {
                    var project = shell.Project;
                    var keira = project.FindByTitle("Keira Varenh");
                    if (keira != null)
                    {
                        var writingsRoot = project.Category(Project.KeyWritings);
                        var scratch = new BinderItem { Kind = ItemKind.Text, Title = "Brouillon (sonde liens)" };
                        scratch.Document.Paragraphs.Clear();
                        var paragraph = new TextParagraph();
                        paragraph.Runs.Add(new TextRun { Text = "Avec [[Keira Varenh]] et [[keira varenh|elle]]." });
                        scratch.Document.Paragraphs.Add(paragraph);
                        scratch.Parent = writingsRoot;
                        writingsRoot.Children.Add(scratch);
                        var linksBefore = LinkHygiene.CountLinksTo(project, "Keira Varenh");
                        var history = shell.HistoryForProbe;
                        history.Run(new History.RenameItemAction(keira, "Keira Varenh (sonde)", project));
                        await Settle();
                        var flat = PivotEdit.FlatText(scratch.Document.Paragraphs[0]);
                        Check(keira.Title == "Keira Varenh (sonde)" && flat == "Avec [[Keira Varenh (sonde)]] et [[Keira Varenh (sonde)|elle]].",
                            "renommer la fiche recible ses liens (« " + flat + " »)");
                        Check(LinkHygiene.CountLinksTo(project, "Keira Varenh (sonde)") == linksBefore, "…tous les liens du projet (" + linksBefore + ")");
                        history.Undo();
                        await Settle();
                        Check(keira.Title == "Keira Varenh" && PivotEdit.FlatText(scratch.Document.Paragraphs[0]) == "Avec [[Keira Varenh]] et [[keira varenh|elle]].",
                            "Ctrl+Z rend le titre et les liens tels quels");
                        writingsRoot.Children.Remove(scratch);
                        shell.Binder.Rebuild();
                        await Settle();
                    }
                }

                // — Casser le dossier (1.0.5) : un dossier de Fiches avec deux
                //   fiches ; cassé, les fiches sont à la racine, le dossier à
                //   la corbeille ; Ctrl+Z remet tout.
                {
                    var project = shell.Project;
                    var sheetsHome = project.Category(Project.KeySheets);
                    var folder = new BinderItem { Kind = ItemKind.Folder, Title = "Dossier (sonde)", Parent = sheetsHome };
                    var one = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche une (sonde)", Parent = folder, CategoryId = project.SheetCategories[0].Id, TemplateId = project.SheetCategories[0].TemplateId };
                    var two = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche deux (sonde)", Parent = folder, CategoryId = project.SheetCategories[0].Id, TemplateId = project.SheetCategories[0].TemplateId };
                    folder.Children.Add(one);
                    folder.Children.Add(two);
                    sheetsHome.Children.Add(folder);
                    shell.Binder.Rebuild();
                    await Settle();
                    var trashBefore = project.Trash.Children.Count;
                    shell.Binder.BreakFolderForProbe(folder.Id);
                    await Settle();
                    Check(one.Parent == sheetsHome && two.Parent == sheetsHome && sheetsHome.Children.IndexOf(one) < sheetsHome.Children.IndexOf(two),
                        "casser le dossier : ses fiches rejoignent la racine Fiches, dans l'ordre");
                    Check(folder.Parent == project.Trash && folder.Children.Count == 0, "…et le dossier vide part à la corbeille");
                    shell.HistoryForProbe.Undo();
                    await Settle();
                    Check(one.Parent == folder && folder.Parent == sheetsHome && project.Trash.Children.Count == trashBefore, "Ctrl+Z : le dossier et ses fiches reviennent");
                    sheetsHome.Children.Remove(folder);
                    shell.Binder.Rebuild();
                    await Settle();
                }

                // — MAJ+clic (1.0.5) : la plage entre la ligne choisie et la
                //   ligne cliquée dans la Pile ; même chose sur les tuiles.
                {
                    var project = shell.Project;
                    var bookItem = project.FindByTitle(book == null ? "" : book.Title);
                    if (book != null && book.Children.Count >= 3)
                    {
                        shell.Binder.SelectItem(book.Children[0].Id);
                        await Settle();
                        var ok = shell.Binder.RangeMultiForProbe(book.Children[2].Id);
                        Check(ok && shell.Binder.MultiCountForProbe == 3, "Pile : clic sur le chapitre 1 puis MAJ+clic sur le 3 = trois lignes (" + shell.Binder.MultiCountForProbe + ")");
                        shell.Binder.ClearMultiSelection();
                    }
                    var sheetsHome = project.Category(Project.KeySheets);
                    shell.Binder.SelectItem(sheetsHome.Id);
                    await Settle();
                    var library = shell.SheetLibraryForProbe;
                    var ids = new List<string>();
                    foreach (var item in sheetsHome.Children) if (item.Kind == ItemKind.Sheet) ids.Add(item.Id);
                    if (library != null && library.IsVisible && ids.Count >= 2)
                    {
                        var ok = library.RangeSelectForProbe(ids[0], ids[ids.Count - 1]);
                        Check(ok && library.SelectedItems().Count >= 2, "bibliothèque : MAJ+clic d'une tuile à l'autre choisit la plage (" + library.SelectedItems().Count + ")");
                        library.ClearSelection();
                    }
                }

                // — Macros de styles (1.0.5) : un style qui porte Ctrl+Maj+9
                //   s'applique au paragraphe par la touche ; sans modificateur,
                //   rien ; retiré, plus rien.
                {
                    var project = shell.Project;
                    if (chapter != null && project != null)
                    {
                        shell.Binder.SelectItem(chapter.Id);
                        await Settle();
                        var composedView = shell.Composed;
                        var sheetStyles = project.Styles;
                        var macro = new ParagraphStyle { Name = "Macro (sonde)", Shortcut = "Ctrl+Shift+D9", FontFamily = sheetStyles.Styles[0].FontFamily }; // la police du Corps : pas une « manquante » de plus
                        sheetStyles.Styles.Add(macro);
                        var paragraphBefore = chapter.Document.Paragraphs[0].StyleId;
                        var applied = composedView != null && composedView.RunStyleShortcutForProbe("D9", Settings.KeyModifiers.Control | Settings.KeyModifiers.Shift);
                        await Settle();
                        Check(applied && chapter.Document.Paragraphs[0].StyleId == macro.Id, "macro de style : Ctrl+Maj+9 applique le style au paragraphe");
                        Check(!composedView.RunStyleShortcutForProbe("D9", Settings.KeyModifiers.None), "…une touche nue ne fait rien");
                        sheetStyles.Styles.Remove(macro);
                        chapter.Document.Paragraphs[0].StyleId = paragraphBefore;
                        shell.RefreshFontAlert(); // le style de passage ne compte plus
                        Check(!composedView.RunStyleShortcutForProbe("D9", Settings.KeyModifiers.Control | Settings.KeyModifiers.Shift), "…le style retiré, la touche ne fait plus rien");
                        // La combinaison réservée dépend du système (release dev 11/10 : ⌘Q n'est réservé que sur mac).
                        var reserved = AppPlatform.IsMac ? "Ctrl+Q" : "Alt+F4";
                        Check(ShortcutConflicts.Describe(reserved, null, null, null) != null && ShortcutConflicts.Describe("Ctrl+B", null, null, null) != null
                            && ShortcutConflicts.Describe("Ctrl+Shift+Alt+F12", null, null, null) == null,
                            "doublons : " + AppSettings.DisplayGesture(reserved) + " (système) et Ctrl+B (Gras) sont signalés, une combinaison libre non");
                        shell.Binder.SelectItem(chapter.Id);
                        await Settle();
                    }
                }

                // — 1.0.5-a : projet actif (pastille dans la Pile, spectre par
                //   défaut), catégories repliables, couleur d'une sous-catégorie,
                //   puce de modèle juste, corps Markdown sans second menu,
                //   recherche du Lexique.
                {
                    var project = shell.Project;
                    if (book != null && project != null)
                    {
                        shell.Binder.SetActiveScope(book);
                        await Settle();
                        Check(project.ActiveScope() == book && shell.Binder.HasActiveDotForProbe(book.Id), "« Projet actif » : la pastille verte sur le livre dans la Pile");
                        var scopes = Scopes.Build(project);
                        Check(scopes.Count >= 2 && scopes[Scopes.DefaultIndex(scopes)].IsActive && scopes[0].Label.StartsWith("Projet actif"), "…et le spectre par défaut des relevés (" + scopes[0].Label + ")");
                        shell.HistoryForProbe.Undo();
                        await Settle();
                        Check(project.ActiveScope() == null && !shell.Binder.HasActiveDotForProbe(book.Id), "Ctrl+Z retire le projet actif");
                    }
                    var keira = project == null ? null : project.FindByTitle("Keira Varenh");
                    var top = keira == null ? null : project.TopSheetCategoryOf(keira);
                    if (keira != null && top != null && !keira.IsDescendantOf(project.Trash))
                    {
                        var keptCategory = keira.CategoryId;
                        var heroes = project.AddSubCategory(top, "Héros (sonde)");
                        heroes.Color = "#2E9E6B";
                        keira.CategoryId = heroes.Id;
                        shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                        await Settle();
                        var library = shell.SheetLibraryForProbe;
                        Check(library != null && !library.SubBoxCollapsedForProbe(heroes.Id), "la boîte d'une sous-catégorie est dépliée d'abord");
                        SheetLibraryView.CollapsedCategories.Add(heroes.Id);
                        library.Refresh();
                        await Settle();
                        Check(library.SubBoxCollapsedForProbe(heroes.Id), "…repliée d'un clic sur son titre (cartes cachées)");
                        var rowsBefore = shell.Binder.SheetRowsForProbe();
                        shell.Binder.Rebuild();
                        await Settle();
                        Check(shell.Binder.SheetRowsForProbe() < rowsBefore || rowsBefore == 0, "la Pile suit : la catégorie repliée cache ses fiches (" + rowsBefore + " → " + shell.Binder.SheetRowsForProbe() + ")");
                        // 1.0.5-b : la tuile double-cliquée d'une fiche dont la
                        // catégorie est repliée dans la Pile — SelectItem ne
                        // trouvait pas de ligne et la fiche ne s'ouvrait pas.
                        shell.Binder.SelectItem(keira.Id);
                        await Settle();
                        var opened = shell.Binder.SelectedItem;
                        Check(opened != null && opened.Id == keira.Id && !SheetLibraryView.CollapsedCategories.Contains(heroes.Id) && shell.Sheet != null && shell.Sheet.IsVisible,
                            "sélectionner une fiche d'une catégorie repliée dans la Pile la déplie et l'ouvre (1.0.5-b)");
                        Check(shell.Binder.SelectedRowsForProbe() == 1, "…une seule ligne surlignée dans la Pile (" + shell.Binder.SelectedRowsForProbe() + ")");
                        // La double sélection (1.0.5-b) : Fiches puis Keira par
                        // programme (lien wiki, Retour) laissait les deux lignes
                        // surlignées.
                        shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                        await Settle();
                        shell.Binder.SelectItem(keira.Id);
                        await Settle();
                        Check(shell.Binder.SelectedRowsForProbe() == 1 && shell.Binder.SelectedItem != null && shell.Binder.SelectedItem.Id == keira.Id,
                            "Fiches puis Keira par programme : une seule ligne surlignée (" + shell.Binder.SelectedRowsForProbe() + ")");
                        shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
                        await Settle();
                        SheetLibraryView.CollapsedCategories.Remove(heroes.Id);
                        library.Refresh();
                        await Settle();
                        Check(!library.SubBoxCollapsedForProbe(heroes.Id), "…et se redéplie");
                        keira.CategoryId = keptCategory;
                        project.SheetCategories.Remove(heroes);
                        shell.Binder.Rebuild();
                        library.Refresh();
                        await Settle();
                    }
                    var sheetView = shell.Sheet;
                    Check(sheetView == null || sheetView.BodyBox == null || sheetView.BodyBox.ContextFlyout == null, "le corps Markdown n'a plus le menu du thème en double");
                    var lexiconRail = shell.LexiconPanelForProbe;
                    if (lexiconRail != null && project != null)
                    {
                        var probeEntry = LexiconEntry.Simple("Zelandra (sonde)");
                        project.Lexicon.Add(probeEntry);
                        var hits = lexiconRail.SearchForProbe("zelan");
                        var none = lexiconRail.SearchForProbe("xqzw");
                        Check(hits == 1 && none == 1, "Lexique : la recherche en tête du rail trouve « Zelandra (sonde) » (" + hits + "), et dit « Aucune entrée » sinon");
                        lexiconRail.SearchForProbe("");
                        project.Lexicon.Remove(probeEntry);
                    }
                    // L'épingle par défaut (1.0.5-a) se vérifie sur des réglages NEUFS : le fichier
                    // de la sonde porte l'ancien choix — c'est le défaut du code qui compte.
                    Check(AppSettings.LexiconPinned || AppSettings.LoadedFromFile, "le Lexique est épinglé au rail par défaut (sauf réglage enregistré)");
                }

                // — Les polices manquantes (07/10) : un style qui demande une
                //   police inconnue allume la pastille rouge ; un remplacement
                //   global l'apaise et le moteur sert la remplaçante ; le
                //   dialogue écrit le remplacement dans les réglages.
                {
                    // Le runner Linux n'a ni Times New Roman ni les polices du
                    // projet d'exemple (release dev 07/10 : cinq échecs) : la
                    // sonde part de ce qui manque DÉJÀ (baseline) et choisit sa
                    // remplaçante parmi les polices que le dialogue propose.
                    shell.RefreshFontAlert();
                    var baseline = new List<string>(shell.MissingFontsForProbe);
                    var baselineUnresolved = 0;
                    foreach (var family in baseline)
                        if (string.Equals(AppSettings.SubstituteFont(family), family, StringComparison.OrdinalIgnoreCase)) baselineUnresolved++;
                    var ghost = new ParagraphStyle { Id = "sonde-ghost", Name = "Fantôme", FontFamily = "Police Imaginaire XYZ" };
                    shell.Project.Styles.Styles.Add(ghost);
                    AppSettings.FontSubstitutions.Remove("Police Imaginaire XYZ");
                    shell.RefreshFontAlert();
                    await Settle();
                    Check(shell.MissingFontsForProbe.Contains("Police Imaginaire XYZ"), "une police inconnue du catalogue est signalée manquante");
                    var alert = shell.FontAlertForProbe; // plus d'infobulle sur la barre (10/10, Rémi)
                    Check(alert != null && alert.IsVisible && shell.FontAlertTextForProbe.Contains("manquante") && ToolTip.GetTip(alert) == null,
                        "la barre d'état montre la pastille rouge « " + (baselineUnresolved + 1) + " police(s) manquante(s) », sans infobulle (« " + shell.FontAlertTextForProbe + " »)");
                    string replacement = null;
                    var substitutionTask = FontSubstitutionDialog.Show(shell, new List<string> { "Police Imaginaire XYZ" }, shell.Project);
                    await Settle();
                    var substitution = OpenWindow<FontSubstitutionDialog>();
                    Check(substitution != null, "le dialogue des polices manquantes s'ouvre");
                    if (substitution != null)
                    {
                        // L'accordéon « où elle manque » (09/10) : replié, le style Fantôme dedans.
                        TextBlock where = null; TextBlock ghostUser = null;
                        foreach (var block in substitution.GetVisualDescendants().OfType<TextBlock>())
                        {
                            if (block.Text != null && block.Text.EndsWith("emplacement")) where = block;
                            if (block.Text == "Style « Fantôme »") ghostUser = block;
                        }
                        Check(where != null && where.Text == "› 1 emplacement", "…avec l'accordéon « 1 emplacement » replié sous la police");
                        if (where != null)
                        {
                            where.RaiseEvent(new PointerPressedEventArgs(where, new Avalonia.Input.Pointer(1, PointerType.Mouse, true), where, new Point(1, 1), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None));
                            await Settle();
                        }
                        ghostUser = null;
                        foreach (var block in substitution.GetVisualDescendants().OfType<TextBlock>())
                            if (block.Text == "Style « Fantôme »" && block.IsEffectivelyVisible) ghostUser = block;
                        Check(ghostUser != null && where != null && where.Text.StartsWith("⌄"), "…qui se déplie sur « Style « Fantôme » »");
                        var combo = substitution.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
                        var index = -1;
                        if (combo != null)
                            for (var i = 0; i < combo.Items.Count; i++)
                                if (string.Equals(combo.Items[i] as string, "Times New Roman", StringComparison.OrdinalIgnoreCase)) index = i;
                        if (combo != null && index < 0 && combo.Items.Count > 1) index = 1; // la première police installée, quelle qu'elle soit (Linux)
                        if (combo != null && index >= 0) { combo.SelectedIndex = index; replacement = combo.Items[index] as string; }
                        Check(combo != null && index > 0 && !string.IsNullOrEmpty(replacement), "…avec les polices installées à choisir (« " + replacement + " »)");
                        var apply = FindButton(substitution, "Appliquer");
                        if (apply != null) apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else substitution.Close();
                    }
                    var applied = await substitutionTask;
                    Check(applied && replacement != null && AppSettings.SubstituteFont("Police Imaginaire XYZ") == replacement, "« Appliquer » écrit le remplacement dans les réglages");
                    shell.ApplyFontSubstitutions();
                    await Settle();
                    var ghostFace = new AvaloniaFontEngine().Resolve("Police Imaginaire XYZ", 400, false);
                    Check(replacement != null && string.Equals(ghostFace.Family, replacement, StringComparison.OrdinalIgnoreCase) && ghostFace.HasGlyphs, "le moteur de polices sert la remplaçante partout (" + ghostFace.Family + ")");
                    var calm = baselineUnresolved > 0 ? shell.FontAlertTextForProbe.Contains("manquante") : shell.FontAlertTextForProbe == "Tout va bien"; // d'autres manquent encore (Linux) : la pastille reste rouge
                    Check(alert != null && alert.IsVisible && calm, "la pastille s'apaise, toujours cliquable (" + baselineUnresolved + " autre(s) manquante(s))");
                    Check(baselineUnresolved > 0 || shell.FontAlertTextForProbe == "Tout va bien", "…et dit « Tout va bien » quand tout est remplacé (« " + shell.FontAlertTextForProbe + " »)");
                    AppSettings.FontSubstitutions.Remove("Police Imaginaire XYZ");
                    shell.Project.Styles.Styles.Remove(ghost);
                    shell.ApplyFontSubstitutions();
                    await Settle();
                    Check(alert != null && alert.IsVisible == (baseline.Count > 0), "sans police manquante, rien dans la barre (" + baseline.Count + " manquante(s) hors sonde)");
                }

                // — Enregistrer puis rouvrir : le .plot fait l'aller-retour.
                var plotPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-p3.plot");
                try
                {
                    var count = shell.Project.AllItems().Count();
                    Persistence.PlotFile.Save(shell.Project, plotPath);
                    shell.OpenFile(plotPath);
                    await Settle();
                    await Settle();
                    Check(shell.HasProjectPath && shell.Project.AllItems().Count() == count && shell.Title.Contains("marabook-sonde-p3"),
                        "le projet enregistré se rouvre avec ses " + count + " éléments (" + shell.Title + ")");
                    // Fichier › Enregistrer sur le projet rouvert : le vrai
                    // chemin de la fenêtre (rinçage des vues, écriture,
                    // récents, inspecteur, toast) — celui qui tombait sur
                    // « Object reference not set » (27/09).
                    var doSave = typeof(MainWindow).GetMethod("DoSave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var stampBefore = System.IO.File.GetLastWriteTimeUtc(plotPath);
                    await Task.Delay(30);
                    doSave.Invoke(shell, null);
                    await Settle();
                    Check(shell.LastSaveError == null && System.IO.File.GetLastWriteTimeUtc(plotPath) > stampBefore,
                        "Fichier › Enregistrer réécrit le projet rouvert" + (shell.LastSaveError == null ? "" : " : " + shell.LastSaveError.Split('\n')[0]));
                    // — L'écriture DIFFÉRENTIELLE et la COPIE HORODATÉE (1.1.0,
                    //   30/10) : le projet rouvert porte l'index du fichier lu,
                    //   l'enregistrement recopie les entrées inchangées (seul
                    //   le manifeste change) et pose une première copie datée
                    //   dans « nom.sauvegardes » à côté du projet.
                    await shell.SaveCompletion();
                    Check(shell.LastSaveCopied > 0 && shell.LastSavePacked == 1,
                        "l'enregistrement recopie les entrées inchangées sans les recompresser (" + shell.LastSaveCopied + " recopiées, " + shell.LastSavePacked + " compressée)");
                    var copies = Persistence.Backups.List(plotPath);
                    var copyFolder = Persistence.Backups.FolderFor(plotPath);
                    Check(shell.LastBackupPath != null && copies.Count == 1 && Persistence.Backups.IsBackupName(copies[0].Path)
                        && System.IO.Path.GetDirectoryName(copies[0].Path) == copyFolder
                        && copyFolder.StartsWith(Persistence.Backups.RootFolder(), StringComparison.OrdinalIgnoreCase)
                        && !copyFolder.StartsWith(System.IO.Path.GetDirectoryName(plotPath), StringComparison.OrdinalIgnoreCase),
                        "une copie horodatée est posée dans le dossier de données de Marabook, pas à côté du projet (" + (shell.LastBackupPath == null ? "aucune" : copyFolder) + ")");
                    shell.MarkDirtyPublic();
                    doSave.Invoke(shell, null);
                    await shell.SaveCompletion();
                    Check(Persistence.Backups.List(plotPath).Count == 1, "un second enregistrement dans la foulée n'en fait pas une autre (intervalle)");
                    // — Le .bak roulant est CACHÉ et non indexé (30/10, Rémi).
                    var bakAttributes = System.IO.File.Exists(plotPath + ".bak") ? System.IO.File.GetAttributes(plotPath + ".bak") : 0;
                    Check(!OperatingSystem.IsWindows() || ((bakAttributes & System.IO.FileAttributes.Hidden) != 0 && (bakAttributes & System.IO.FileAttributes.NotContentIndexed) != 0),
                        "le .plot.bak est caché et hors indexation (" + bakAttributes + ")");
                    // — La garde du .plot (1.0.3) : le verrou est posé à côté
                    //   du projet ouvert ; le fichier tel qu'écrit est « à
                    //   jour » ; remplacé par sa version d'avant (.bak = une
                    //   autre empreinte, comme une synchronisation), il est vu
                    //   « modifié » ; une écriture gardée le refuse sans l'écraser.
                    await shell.SaveCompletion();
                    Check(shell.LockHeld && System.IO.File.Exists(plotPath + ".lock"), "le verrou « .plot.lock » est posé à côté du projet ouvert");
                    Check(shell.DiskStatePublic == "Same", "après l'écriture, le fichier sur le disque est la référence (" + shell.DiskStatePublic + ")");
                    if (System.IO.File.Exists(plotPath + ".bak"))
                    {
                        System.IO.File.Copy(plotPath + ".bak", plotPath, true);
                        System.IO.File.SetAttributes(plotPath, System.IO.FileAttributes.Normal); // la copie hérite du « caché » du .bak
                        Check(shell.DiskStatePublic == "Changed", "remplacé par une autre version : vu comme modifié en dehors de Marabook (" + shell.DiskStatePublic + ")");
                        var bytesBefore = new System.IO.FileInfo(plotPath).Length;
                        var foreignId = Persistence.PlotFile.ReadSaveId(plotPath);
                        shell.MarkDirtyPublic();
                        var autosave = typeof(MainWindow).GetMethod("Autosave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        autosave.Invoke(shell, null);
                        await shell.SaveCompletion();
                        await Settle();
                        Check(Persistence.PlotFile.ReadSaveId(plotPath) == foreignId && new System.IO.FileInfo(plotPath).Length == bytesBefore,
                            "la sauvegarde automatique se suspend : le fichier de l'autre n'est pas écrasé");
                    }
                }
                catch (Exception error) { Check(false, "aller-retour .plot : " + error.Message); }
                finally
                {
                    try { System.IO.File.Delete(plotPath); } catch { }
                    try { System.IO.Directory.Delete(Persistence.Backups.FolderFor(plotPath), true); } catch { }
                    try { if (System.IO.File.Exists(plotPath + ".bak")) { System.IO.File.SetAttributes(plotPath + ".bak", System.IO.FileAttributes.Normal); System.IO.File.Delete(plotPath + ".bak"); } } catch { }
                }
                book = null;
                foreach (var item in shell.Project.AllItems()) if (item.Kind == ItemKind.Book) { book = item; break; }
                chapter = book == null ? null : book.Children[0];
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();

                // — La colonne de droite ne s'ouvre que si elle a quelque chose
                //   à montrer (1.0.3) : plus de « Rien à montrer ici » sur une
                //   racine sans Général ; elle revient sur un écrit.
                var sheetsRoot = shell.Project.Category(Project.KeySheets);
                if (sheetsRoot != null && chapter != null)
                {
                    shell.Binder.SelectItem(sheetsRoot.Id, true);
                    await Settle();
                    Check(shell.RightColumnWidth < 1, "racine Fiches sans sélection : la colonne de droite est repliée (" + shell.RightColumnWidth.ToString("0") + " ; " + shell.RightStateForProbe + ")");
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    Check(shell.RightColumnWidth > 1, "…et revient sur un écrit (" + shell.RightColumnWidth.ToString("0") + ")");
                }

                // — La sélection multiple (1.0.3) : deux chapitres du livre
                //   choisis sur le tableau → le Général passe en mode lot ; la
                //   couleur s'applique aux deux ; UN Ctrl+Z la retire des deux.
                if (book != null && book.Children.Count >= 2 && chapter != null)
                {
                    shell.Binder.SelectItem(book.Id, true);
                    await Settle();
                    var board = shell.BookViewPublic.TextsBoard;
                    var first = book.Children[0];
                    var second = book.Children[1];
                    board.SelectForProbe(new[] { first.Id, second.Id });
                    await Settle();
                    Check(shell.InspectedGroupCount == 2 && shell.InspectorTitle.StartsWith("2 "), "deux cartes choisies : le Général passe en mode lot (" + shell.InspectorTitle + ")");
                    var colorFirst = first.CardColor;
                    var colorSecond = second.CardColor;
                    shell.BatchColorPublic("#AA3366");
                    await Settle();
                    Check(first.CardColor == "#AA3366" && second.CardColor == "#AA3366", "la couleur du lot s'applique aux deux cartes");
                    shell.UndoPublic();
                    await Settle();
                    Check(first.CardColor == colorFirst && second.CardColor == colorSecond, "…et UN Ctrl+Z la retire des deux");
                    board.ClearSelection();
                    await Settle();
                    Check(shell.InspectedGroupCount == 0, "plus de sélection : le Général revient à l'élément");
                    // La Pile : Ctrl+clic sur un second écrit forme un lot ; une
                    // fiche d'une autre famille le fait tomber.
                    shell.Binder.SelectItem(first.Id, true);
                    await Settle();
                    shell.Binder.ToggleMultiForProbe(second.Id);
                    await Settle();
                    Check(shell.Binder.MultiItems().Count == 2 && shell.InspectedGroupCount == 2, "Pile : Ctrl+clic sur un second écrit forme un lot de deux");
                    var anySheet = shell.Project.AllItems().FirstOrDefault(i => i.Kind == ItemKind.Sheet);
                    if (anySheet != null)
                    {
                        shell.Binder.ToggleMultiForProbe(anySheet.Id);
                        await Settle();
                        Check(shell.Binder.MultiItems().Count == 0 && shell.InspectedGroupCount == 0, "…une fiche hors de la famille fait tomber le lot");
                    }
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                }

                shell.ShowJournalPublic();
                await Settle();
                Check(true, "le Journal perso s'ouvre");
                foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
                {
                    shell.SetRightPanelPublic(panel);
                    await Settle();
                }
                Check(true, "chaque panneau de droite s'ouvre (" + Enum.GetValues(typeof(RightPanel)).Length + ")");
                shell.SetRightPanelPublic(RightPanel.Inspector);
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();


                // — Le thème bascule et revient.
                var before = Chrome.Ink.Color;
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color != before, "basculer le thème recolore l'encre");
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color == before, "…et revient");

                // — Les Préférences : neuf onglets (Profil depuis la 1.1.0), chacun un contenu.
                var prefs = new PreferencesDialog(shell, shell.Project);
                prefs.Show(shell);
                await Settle();
                // …plus ceux des modules chargés (le filet « DLC » et l'onglet de chaque DLC, 26/10 soir).
                Check(prefs.Tabs.Items.Count == 9 + prefs.ModuleTabCount, "les Préférences ont neuf onglets (" + prefs.Tabs.Items.Count + ", dont " + prefs.ModuleTabCount + " des modules)");
                Check(prefs.Tabs.TabStripPlacement == Dock.Left && prefs.Tabs.Classes.Contains("column"), "…en colonne à gauche (24/10)");
                Check(prefs.Height >= 420 && Math.Abs(prefs.Height - Math.Max(420, Dialogs.AvailableHeight(shell))) < 1 && prefs.Bounds.Height <= Dialogs.AvailableHeight(shell) + 1, "…et la fenêtre prend toute la hauteur disponible de l'écran sans en sortir (" + prefs.Bounds.Height.ToString("0") + " px sur " + Dialogs.AvailableHeight(shell).ToString("0") + ")");
                var titles = new List<string>();
                foreach (var item in prefs.Tabs.Items) titles.Add(((TabItem)item).Header as string);
                Check(titles.Contains("Raccourcis") && titles.Contains("DLC") && titles.Contains("Catalogue de polices"),
                    "…Raccourcis, DLC et Catalogue de polices en font partie");
                // — 1.0.6 : le premier onglet s'appelle « Général » (« Personnalisation »
                //   avant) et porte la case « Animations », cochée par défaut.
                Check(titles.Count > 0 && titles[0] == "Général" && !titles.Contains("Personnalisation"), "le premier onglet est « Général » (" + (titles.Count > 0 ? titles[0] : "-") + ")");
                prefs.Tabs.SelectedIndex = 0;
                await Settle();
                CheckBox animationsBox = null;
                foreach (var box in prefs.GetVisualDescendants().OfType<CheckBox>())
                    if ("animations".Equals(box.Tag)) animationsBox = box;
                Check(animationsBox != null && animationsBox.IsChecked == AppSettings.Animations && (AppSettings.Animations || AppSettings.LoadedFromFile), "la case « Animations » est là, cochée par défaut (sauf réglage enregistré)");
                for (var i = 0; i < prefs.Tabs.Items.Count; i++)
                {
                    prefs.Tabs.SelectedIndex = i;
                    await Settle();
                }
                Check(prefs.Tabs.SelectedIndex == prefs.Tabs.Items.Count - 1, "chaque onglet s'ouvre sans erreur");
                prefs.Tabs.SelectedIndex = 0;
                await Settle();
                // Diagnostic de rendu : la graisse et la police que voit un
                // texte courant de l'onglet (le contenu ne doit pas hériter
                // de la graisse de la chip active).
                TextBlock sample = null;
                foreach (var text in prefs.GetVisualDescendants().OfType<TextBlock>())
                    if (text.Text != null && (text.Text ?? "").StartsWith("Boutons, sélections")) { sample = text; break; }
                Check(sample != null && sample.FontWeight == Avalonia.Media.FontWeight.Normal,
                    "un texte courant des Préférences reste en graisse normale (" + (sample == null ? "introuvable" : sample.FontWeight + ", " + sample.FontFamily.Name + " " + sample.FontSize) + ")");
                prefs.Close();
                await Settle();

                // — La boîte de message se montre et se ferme.
                var dialogTask = MessageDialog.Show(shell, "Sonde : cette boîte se ferme toute seule.", "Sonde", MessageButtons.OKCancel, MessageIcon.Information);
                await Settle();
                MessageDialog open = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is MessageDialog) open = (MessageDialog)window;
                Check(open != null && open.IsVisible, "la boîte de message est ouverte");
                if (open != null) open.Close();
                var result = await dialogTask;
                Check(result == MessageResult.Cancel, "fermée par la croix : le refus le plus sûr (Annuler)");

                // — Le dialogue de saisie : Entrée valide.
                var askTask = InputDialog.Ask(shell, "Sonde", "Un nom :", "Chapitre neuf");
                await Settle();
                InputDialog input = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is InputDialog) input = (InputDialog)window;
                Check(input != null, "le dialogue de saisie est ouvert");
                if (input != null) input.Close();
                var answer = await askTask;
                Check(answer == null, "fermé sans valider : null");

                // — Le dialogue du dictionnaire (01/10) : un nom part au
                //   masculin ; un nom propre « Lieu » cache la flexion et
                //   propose le gentilé ; « Gentilé » rend la flexion.
                var lexiconTask = LexiconEntryDialog.AskForWord(shell, "mànis", true);
                await Settle();
                LexiconEntryDialog lexicon = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is LexiconEntryDialog) lexicon = (LexiconEntryDialog)window;
                Check(lexicon != null, "le dialogue du dictionnaire est ouvert");
                if (lexicon != null)
                {
                    Func<string, RadioButton> radio = delegate(string content)
                    {
                        foreach (var candidate in lexicon.GetVisualDescendants().OfType<RadioButton>())
                            if ((candidate.Content as string) == content) return candidate;
                        return null;
                    };
                    Func<string, CheckBox> checkBox = delegate(string prefix)
                    {
                        foreach (var candidate in lexicon.GetVisualDescendants().OfType<CheckBox>())
                            if ((candidate.Content as string ?? "").StartsWith(prefix)) return candidate;
                        return null;
                    };
                    var masculine = lexicon.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(r => (r.Content as string) == "Masculin" && r.GroupName == "lexicon-genders");
                    var flexionLabel = lexicon.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Flexion :");
                    Check(masculine != null && masculine.IsChecked == true, "un nom neuf part au masculin");
                    var place = radio("Lieu");
                    Check(place != null && !place.IsEffectivelyVisible, "un nom : les natures du nom propre sont cachées");
                    // (02/10) Avalonia coche le nouveau bouton avant de décocher
                    // l'ancien : la nature doit suivre le type dès ce changement.
                    var proper = radio("Nom propre");
                    if (proper != null) proper.IsChecked = true;
                    await Settle();
                    Check(place != null && place.IsEffectivelyVisible, "« Nom propre » : ses natures apparaissent aussitôt");
                    if (place != null) place.IsChecked = true;
                    await Settle();
                    Check(flexionLabel != null && !flexionLabel.IsEffectivelyVisible, "nom propre « Lieu » : la flexion est masquée");
                    var demonym = checkBox("Dériver le gentilé");
                    Check(demonym != null && demonym.IsEffectivelyVisible, "…et le gentilé est proposé");
                    // (1.0.3) Un prénom se genre : neutre par défaut, Masculin / Féminin à côté.
                    var firstName = radio("Prénom");
                    if (firstName != null) firstName.IsChecked = true;
                    await Settle();
                    var neutral = radio("Neutre");
                    Check(neutral != null && neutral.IsEffectivelyVisible && neutral.IsEnabled && neutral.IsChecked == true, "« Prénom » : le genre est proposé, neutre par défaut");
                    var gentile = radio("Gentilé");
                    if (gentile != null) gentile.IsChecked = true;
                    await Settle();
                    Check(flexionLabel != null && flexionLabel.IsEffectivelyVisible, "« Gentilé » : la flexion revient");
                    Check(demonym != null && !demonym.IsEffectivelyVisible, "…et le gentilé dérivé disparaît");
                    lexicon.Close();
                }
                var lexiconAnswer = await lexiconTask;
                Check(lexiconAnswer == null, "fermé sans valider : null");

                // — Fermer le projet : l'Accueil se pose sur la coquille.
                shell.CloseProjectPublic();
                await Settle();
                Check(shell.Welcome != null && shell.Welcome.IsVisible, "projet fermé : l'accueil est posé sur la coquille");
                Check(shell.Welcome.Width >= 640 && shell.Welcome.Height >= 420, "l'accueil fait au moins 640×420 (" + shell.Welcome.Width.ToString("0") + "×" + shell.Welcome.Height.ToString("0") + ")");
                // — Le statut de mise à jour au pied (02/10) : en sonde, la
                //   vérification est sautée et le pied le dit ; l'ancien texte
                //   « la vérification arrive » n'existe plus.
                TextBlock foot = null;
                foreach (var block in shell.Welcome.GetVisualDescendants().OfType<TextBlock>())
                    if (block.Text != null && block.Text.StartsWith("Mise à jour")) foot = block;
                Check(foot != null && foot.Text.Contains("non vérifiée"), "l'accueil dit que la mise à jour n'est pas vérifiée en sonde (" + (foot == null ? "-" : foot.Text) + ")");
                // — La seconde ligne (13/10) : la beta en cours, sous la mise à
                //   jour ; en sonde, non vérifiée elle aussi.
                TextBlock betaLine = null;
                foreach (var block in shell.Welcome.GetVisualDescendants().OfType<TextBlock>())
                    if (block.Text != null && block.Text.StartsWith("Beta")) betaLine = block;
                Check(betaLine != null && betaLine.Text.Contains("non vérifiée"), "la seconde ligne de l'accueil porte la beta, non vérifiée en sonde (" + (betaLine == null ? "-" : betaLine.Text) + ")");
                var footTop = foot == null ? null : foot.TranslatePoint(new Point(0, 0), shell.Welcome);
                var betaTop = betaLine == null ? null : betaLine.TranslatePoint(new Point(0, 0), shell.Welcome);
                Check(footTop != null && betaTop != null && betaTop.Value.Y > footTop.Value.Y, "…et elle est bien SOUS la ligne de mise à jour (" + (footTop == null ? "-" : footTop.Value.Y.ToString("0")) + " → " + (betaTop == null ? "-" : betaTop.Value.Y.ToString("0")) + ")");
                // — L'ordre des versions avec la beta (13/10) : la stable aux
                //   mêmes nombres prime, le correctif à chaud aussi, jamais l'inverse.
                Check(Updater.IsBeta("1.1.0-beta") && !Updater.IsBeta("1.1.0") && !Updater.IsBeta("1.0.5-patch-b"), "« 1.1.0-beta » est une beta, pas « 1.1.0 » ni un correctif");
                Check(Updater.IsNewer("1.1.0-beta", "1.0.5-patch-b"), "la beta 1.1.0 est plus récente que la 1.0.5-patch-b");
                Check(Updater.IsNewer("1.1.0", "1.1.0-beta") && !Updater.IsNewer("1.1.0-beta", "1.1.0"), "la stable 1.1.0 prime sur sa beta, jamais l'inverse");
                Check(Updater.IsNewer("1.1.0-patch-a", "1.1.0-beta") && !Updater.IsNewer("1.1.0-beta", "1.1.0-beta"), "le correctif prime sur la beta ; la même beta n'est pas plus récente");
                Check(!Updater.IsNewer("1.0.5-patch-c", "1.1.0-beta"), "un correctif de la 1.0.5 ne rétrograde pas un testeur de la 1.1.0-beta");
                // — La lecture de la liste des releases (13/10) : la stable
                //   « latest » et un brouillon sont ignorés, la pré-release
                //   -beta est prise avec SON asset (pas celui de la stable),
                //   son commit et ses notes.
                var mine = Updater.PortableZip;
                var list = "[{\"url\": \"https://api.github.com/repos/x/marabook/releases/3\", \"html_url\": \"https://github.com/x/marabook/releases/tag/v1.1.0-beta\", \"tag_name\": \"v1.1.0-beta\", \"target_commitish\": \"abcdef0123456789\", \"draft\": true, \"prerelease\": true, \"body\": \"brouillon\", \"assets\": []},"
                    + " {\"url\": \"https://api.github.com/repos/x/marabook/releases/2\", \"html_url\": \"https://github.com/x/marabook/releases/tag/v1.0.5-patch-b\", \"tag_name\": \"v1.0.5-patch-b\", \"target_commitish\": \"1111111\", \"draft\": false, \"prerelease\": false, \"body\": \"stable\", \"published_at\": \"2026-10-12T18:15:48Z\", \"assets\": [{\"url\": \"https://api.github.com/repos/x/marabook/releases/assets/20\", \"browser_download_url\": \"https://github.com/x/marabook/releases/download/v1.0.5-patch-b/" + mine + "\", \"uploader\": {\"url\": \"https://api.github.com/users/x\"}}]},"
                    + " {\"url\": \"https://api.github.com/repos/x/marabook/releases/1\", \"html_url\": \"https://github.com/x/marabook/releases/tag/v1.1.0-beta\", \"tag_name\": \"v1.1.0-beta\", \"target_commitish\": \"2222222\", \"draft\": false, \"prerelease\": true, \"body\": \"## Beta\\n\\n* essai\", \"published_at\": \"2026-10-13T09:00:00Z\", \"assets\": [{\"url\": \"https://api.github.com/repos/x/marabook/releases/assets/10\", \"browser_download_url\": \"https://github.com/x/marabook/releases/download/v1.1.0-beta/" + mine + "\", \"uploader\": {\"url\": \"https://api.github.com/users/x\"}}]}]";
                var betaFound = Updater.BetaFrom(list);
                Check(betaFound != null && betaFound.Version == "1.1.0-beta" && betaFound.Beta, "la pré-release -beta est trouvée dans la liste, le brouillon ignoré (" + (betaFound == null ? "-" : betaFound.Version) + ")");
                Check(betaFound != null && betaFound.ZipUrl.Contains("/v1.1.0-beta/" + mine) && betaFound.AssetApiUrl.EndsWith("/assets/10"), "…avec l'asset de ce système, celui de la beta (" + (betaFound == null ? "-" : betaFound.ZipUrl) + ")");
                Check(betaFound != null && betaFound.Commit == "2222222" && betaFound.Notes.Contains("* essai") && betaFound.PublishedAt.StartsWith("2026-10-13"), "…son commit, ses notes et sa date");
                Check(Updater.BetaFrom("[{\"url\": \"https://api.github.com/repos/x/marabook/releases/2\", \"tag_name\": \"v1.0.5\", \"draft\": false, \"prerelease\": false, \"assets\": []}]") == null && Updater.BetaFrom("[]") == null, "sans pré-release -beta : rien");

                // — La fenêtre des nouveautés (02/10) : le nom de la version,
                //   les patch notes rendues depuis le Markdown, le bouton
                //   d'installation ; « Plus tard » ferme sans installer.
                var installed = false;
                var info = new Updater.Info
                {
                    Version = "9.9.9",
                    PageUrl = Updater.RepositoryUrl + "/releases/tag/v9.9.9",
                    PublishedAt = "2026-10-02T09:12:33Z",
                    Notes = "## Fonctionnalités\n\n* Une **fenêtre** des nouveautés\n  * et sa sous-liste\n\n## Correctifs\n\n* Le pied de l'accueil"
                };
                var notesTask = UpdateNotesDialog.Show(shell.Welcome, info, "1.0.1", false, delegate { installed = true; });
                await Settle();
                UpdateNotesDialog notes = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is UpdateNotesDialog) notes = (UpdateNotesDialog)window;
                Check(notes != null && notes.IsVisible, "la fenêtre des nouveautés est ouverte");
                if (notes != null)
                {
                    var texts = notes.GetVisualDescendants().OfType<TextBlock>().Select(delegate(TextBlock b) { return b.Text ?? ""; }).ToList();
                    var inlineTexts = notes.GetVisualDescendants().OfType<TextBlock>()
                        .SelectMany(delegate(TextBlock b) { return b.Inlines == null ? new string[0] : b.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(delegate(Avalonia.Controls.Documents.Run r) { return r.Text ?? ""; }); }).ToList();
                    Check(texts.Any(delegate(string t) { return t == "Marabook 9.9.9"; }), "le nom de la version est en titre");
                    Check(texts.Any(delegate(string t) { return t.Contains("02/10/2026") && t.Contains("vous avez la 1.0.1"); }), "la date de publication et la version locale sont dites");
                    Check(inlineTexts.Any(delegate(string t) { return t.Contains("sous-liste"); }) && inlineTexts.Any(delegate(string t) { return t == "fenêtre"; }), "les patch notes sont rendues depuis le Markdown (liste imbriquée, gras)");
                    Check(FindButton(notes, "Installer") != null, "le bouton « Installer et redémarrer » est là");
                    var later = FindButton(notes, "Plus tard");
                    Check(later != null, "le bouton « Plus tard » est là");
                    if (later != null) later.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else notes.Close();
                }
                var chosen = await notesTask;
                Check(!chosen && !installed, "« Plus tard » ferme sans installer");
                // — La même fenêtre pour une beta (13/10) : l'avertissement de
                //   Rémi au-dessus des notes, le bouton de téléchargement nommé.
                var betaInfo = new Updater.Info { Version = "9.9.9-beta", Beta = true, Notes = "* une beta", PublishedAt = "2026-10-13T09:00:00Z" };
                var betaTask = UpdateNotesDialog.Show(shell.Welcome, betaInfo, "1.0.1", false, delegate { installed = true; });
                await Settle();
                UpdateNotesDialog betaDialog = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is UpdateNotesDialog) betaDialog = (UpdateNotesDialog)window;
                Check(betaDialog != null && betaDialog.Title.StartsWith("Version beta"), "la fenêtre d'une beta se nomme ainsi (" + (betaDialog == null ? "-" : betaDialog.Title) + ")");
                if (betaDialog != null)
                {
                    TextBlock warning = null;
                    foreach (var block in betaDialog.GetVisualDescendants().OfType<TextBlock>())
                        if ("beta-warning".Equals(block.Tag)) warning = block;
                    Check(warning != null && warning.Text == UpdateNotesDialog.BetaWarning && warning.Text.Contains("merci, by the way"), "l'avertissement beta est posé, tel quel");
                    Check(FindButton(betaDialog, "Télécharger la beta") != null, "le bouton dit « Télécharger la beta et redémarrer »");
                    var betaLater = FindButton(betaDialog, "Plus tard");
                    if (betaLater != null) betaLater.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else betaDialog.Close();
                }
                var betaChosen = await betaTask;
                Check(!betaChosen && !installed, "« Plus tard » ferme la beta sans rien télécharger");

                // — Aide › Nouveautés (07/10) : les patch notes embarquées de la
                //   version installée, « Fermer » pour seule issue, pas de lien
                //   de release ; la zone des notes n'est plus une feuille de
                //   papier (illisible en sombre avec le papier blanc).
                // Les notes de la VERSION INSTALLÉE (AppInfo.Version) : un bump
                // sans son fichier patchnotes/<version>.md tombe ici.
                var embedded = PatchNotes.ForVersion(AppInfo.Version);
                Check(embedded != null && embedded.Contains("## " + AppInfo.Version), "les patch notes de la " + AppInfo.Version + " sont embarquées dans l'assembly");
                var older = PatchNotes.ForVersion("1.0.3-patch-b");
                Check(older != null && older.Contains("Mac"), "…et celles d'avant (1.0.3-patch-b) aussi");
                Check(PatchNotes.ForVersion("0.0.0-inconnue") == null, "une version sans notes : null, sans plantage");
                var currentTask = UpdateNotesDialog.ShowCurrent(shell.Welcome, AppInfo.Version, embedded);
                await Settle();
                UpdateNotesDialog current = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is UpdateNotesDialog) current = (UpdateNotesDialog)window;
                Check(current != null && current.IsVisible, "Aide › Nouveautés : la fenêtre s'ouvre");
                if (current != null)
                {
                    var texts = current.GetVisualDescendants().OfType<TextBlock>().Select(delegate(TextBlock b) { return b.Text ?? ""; }).ToList();
                    Check(texts.Any(delegate(string t) { return t == "Marabook " + AppInfo.Version; }) && texts.Any(delegate(string t) { return t == "La version installée"; }), "…pour la version installée");
                    Check(!texts.Any(delegate(string t) { return t.Contains("GitHub"); }), "…sans lien vers la release");
                    Check(FindButton(current, "Installer") == null && FindButton(current, "Plus tard") == null, "…sans « Installer » ni « Plus tard »");
                    Check(!current.GetVisualDescendants().OfType<Border>().Any(delegate(Border b) { return ReferenceEquals(b.Background, Chrome.PaperBg); }), "…la zone des notes suit le thème de la fenêtre (pas de papier)");
                    var close = FindButton(current, "Fermer");
                    Check(close != null, "…et « Fermer » est là");
                    if (close != null) close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else current.Close();
                }
                await currentTask;
                Check(current == null || !current.IsVisible, "« Fermer » ferme la fenêtre des nouveautés");
                // — Les fenêtres tiennent dans l'écran (18/10, 1.0.6) : une
                //   fenêtre dimensionnée par un contenu trop haut est plafonnée
                //   et son contenu défile ; une fenêtre à hauteur fixe trop
                //   haute est réduite.
                {
                    var tall = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = new StackPanel { Width = 300, Height = 2000 }, ShowInTaskbar = false };
                    Dialogs.Fit(tall, 600);
                    var scroller = tall.Content as ScrollViewer;
                    Check(scroller != null && tall.MaxHeight == 600, "fenêtre dimensionnée par son contenu : plafond posé et contenu enveloppé dans un ScrollViewer");
                    tall.Show(shell);
                    await Settle();
                    Check(tall.Bounds.Height <= 600.5 && scroller != null && scroller.Extent.Height >= 1999, "…montrée : au plus 600 px de haut, les 2 000 px du contenu défilent (" + tall.Bounds.Height.ToString("0") + " px, étendue " + (scroller == null ? 0 : scroller.Extent.Height).ToString("0") + ")");
                    tall.Close();
                    var fixedTall = new Window { Width = 300, Height = 1500, Content = new Border(), ShowInTaskbar = false };
                    Dialogs.Fit(fixedTall, 600);
                    Check(fixedTall.Height == 600 && fixedTall.MaxHeight == 600 && !(fixedTall.Content is ScrollViewer), "fenêtre à hauteur fixe : réduite au plafond, sans ScrollViewer ajouté");
                    var lexiconDialog = LexiconEntryDialog.ForProbe(shell);
                    Dialogs.FitToScreen(lexiconDialog, shell);
                    Check(lexiconDialog.MaxHeight > 200 && !double.IsInfinity(lexiconDialog.MaxHeight) && lexiconDialog.Content is ScrollViewer, "le dialogue d'entrée du dictionnaire est plafonné à l'écran (" + lexiconDialog.MaxHeight.ToString("0") + " px) et défile");
                }

                // ================================================== 1.1.0 (26/10)
                await Probe110(shell);
                // ================================================== 1.1.0 : les profils (27/10)
                await ProbeProfiles(shell);
                // ================================================== 1.1.0 : le diagramme relationnel (27/10)
                await ProbeRelations(shell);
                // ================================================== 1.1.0 : les onglets (28/10)
                await ProbeTabs(shell);
                // ================================================== 1.1.0 : le graphe relationnel (28/10)
                await ProbeGraph(shell);
                // ================================================== 1.1.0 : la passe des dialogues (29/10)
                await ProbeDialogues(shell);
                // ================================================== 1.1.0 : le lot de correctifs du 29/10
                await ProbeFixes2910(shell);
                // ================================================== 1.1.0 : le tutoriel (29/10) — en dernier : il change de projet
                await ProbeTutorial(shell);
            }
            catch (Exception error)
            {
                Failures++;
                Console.WriteLine("  ÉCHEC  exception : " + error);
            }
            Console.WriteLine(Failures == 0 ? "SONDE OK (" + _checks + " vérifications)" : "*** SONDE : " + Failures + " échec(s) sur " + _checks + " ***");
        }

        /// <summary>Les correctifs de la 1.1.0 (26/10) : la Pile en sections,
        /// les pastilles en icônes, le « + » des catégories, les tags, le
        /// filtre par tag, le dépôt sur une catégorie, la suppression d'une
        /// catégorie avec son modèle, F1/F2 et le bouton Généalogie, l'objectif
        /// et la limite d'un écrit (liseré rouge), le catalogue de polices
        /// (comptes de styles sans plantage, exclues en bas), les filets des
        /// Préférences et les facettes du panneau des styles.</summary>
        private static async Task Probe110(MainWindow shell)
        {
            var project = shell.Project;

            // — La Pile en sections.
            var rows = shell.Binder.TopRowsForProbe();
            var joined = string.Join(" | ", rows.ToArray());
            Check(rows.IndexOf("Écriture") >= 0 && rows.IndexOf("Encyclopédie") > rows.IndexOf("Écriture")
                && rows.IndexOf("Structure") > rows.IndexOf("Encyclopédie") && rows.IndexOf("Outils") > rows.IndexOf("Structure"),
                "Pile : les sections Écriture, Encyclopédie, Structure, Outils dans l'ordre (" + joined + ")");
            Check(rows.IndexOf("Écrits") == rows.IndexOf("Écriture") + 1 && rows.IndexOf("Recherche") == rows.IndexOf("Écrits") + 1, "…Écrits puis Recherche sous Écriture");
            Check(rows.IndexOf("Fiches") == rows.IndexOf("Encyclopédie") + 1 && rows.IndexOf("Dictionnaire") == rows.IndexOf("Fiches") + 1, "…Fiches puis Dictionnaire sous Encyclopédie");
            Check(rows.IndexOf("Plans") == rows.IndexOf("Structure") + 1 && rows.IndexOf(BinderView.UnderConstruction) == rows.IndexOf("Outils") + 1, "…Plans sous Structure, « En construction » sous Outils");
            Check(rows.Count >= 2 && rows[rows.Count - 1] == "Corbeille" && rows[rows.Count - 2] == "—", "…la Corbeille en dernier, derrière un simple filet");
            shell.Binder.ToggleSectionForProbe("Encyclopédie");
            await Settle();
            rows = shell.Binder.TopRowsForProbe();
            Check(!rows.Contains("Fiches") && !rows.Contains("Dictionnaire") && rows.Contains("Encyclopédie  (2)"), "section repliée : ses racines disparaissent, son compte s'affiche (" + string.Join(" | ", rows.ToArray()) + ")");
            shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
            await Settle();
            rows = shell.Binder.TopRowsForProbe();
            Check(rows.Contains("Fiches") && !BinderView.CollapsedSections.Contains("Encyclopédie") && shell.SheetLibraryForProbe.IsVisible, "sélectionner une racine d'une section repliée la déplie et ouvre la vue");

            // — La bibliothèque : pastilles en icônes, « + », icône choisie.
            var library = shell.SheetLibraryForProbe;
            var chips = library.ChipsForProbe();
            Check(chips.Count > 1 && chips[0].StartsWith("chip:Toutes#") && Icons.Has("all"), "bibliothèque : « Toutes » porte l'icône « all » et son compte (" + string.Join(" ", chips.ToArray()) + ")");
            var plainChips = 0;
            foreach (var chip in chips) if (!chip.Contains("#") && !chip.EndsWith("*") || chip.EndsWith("#+")) plainChips++;
            Check(plainChips == chips.Count - 1, "…les autres pastilles sont des icônes seules, sans compte ni nom (« + » sur les vides)");
            var top = project.TopCategories()[0];
            var second = project.TopCategories()[1];
            library.SetCategoryIcon(top, "fiche-objet");
            await Settle();
            chips = shell.SheetLibraryForProbe.ChipsForProbe();
            Check(chips.Contains("chip:" + top.Name + "*") && SheetLibraryView.CategoryIcon(top, project) == "fiche-objet", "une icône choisie marque la pastille d'une astérisque (" + string.Join(" ", chips.ToArray()) + ")");
            Check(shell.UndoPublic() && top.Icon == null, "…annulable");
            await Settle();
            library = shell.SheetLibraryForProbe;
            var plusCount = 0;
            foreach (var button in library.GetVisualDescendants().OfType<Button>())
                if ((button.Tag as string ?? "").StartsWith("plus:")) plusCount++;
            Check(plusCount > 0, "chaque rangée de catégorie et chaque boîte de sous-catégorie ont leur « + » (" + plusCount + ")");
            var created = library.CreateSheetIn(top, "Fiche du plus (sonde)");
            await Settle();
            Check(created.CategoryId == top.Id && created.TemplateId == top.TemplateId && shell.Sheet.HasItem, "le « + » crée la fiche dans la catégorie, sur son modèle de base, et l'ouvre");

            // — Les tags, dans le Général du rail, puis le filtre de la bibliothèque.
            Check(shell.TagsSectionVisibleForProbe, "fiche ouverte : la section Tags du Général est là");
            shell.AddTagForProbe("héros");
            shell.AddTagForProbe("  Héros ");
            shell.AddTagForProbe("mage noir");
            await Settle();
            Check(created.Tags.Count == 2 && created.Tags[0] == "héros" && created.Tags[1] == "mage noir", "les tags s'ajoutent un par un, sans doublon (" + string.Join(", ", created.Tags.ToArray()) + ")");
            Check(shell.TagChipsForProbe().Count == 2 && shell.TagChipsForProbe()[1] == "tag:mage noir", "…en puces dans le Général");
            Check(shell.TagInputForProbe.ItemCount == 2, "la flèche du champ des tags liste les tags du projet (" + shell.TagInputForProbe.ItemCount + ", 31/10)");
            // Le succès secret (31/10) : un clic le gagne, un autre le perd.
            {
                var hadCurious = AppSettings.Achievements.ContainsKey(Achievements.Curious);
                shell.ClickSecretAchievementForProbe(Achievements.Curious);
                await Settle();
                var afterFirst = AppSettings.Achievements.ContainsKey(Achievements.Curious);
                shell.ClickSecretAchievementForProbe(Achievements.Curious);
                await Settle();
                var afterSecond = AppSettings.Achievements.ContainsKey(Achievements.Curious);
                Check(afterFirst != hadCurious && afterSecond == hadCurious, "« Il suffit d'être curieux » se gagne puis se perd au clic (" + hadCurious + " → " + afterFirst + " → " + afterSecond + ")");
            }
            // Le champ des tags propose les tags du projet (31/10, Rémi : « ne propose pas les tags déjà présents »).
            var tagBox = shell.TagInputForProbe;
            {
                // Une frappe réelle dans le champ interne, la liste native « ouverte »
                // comme après un clic dans un ComboBox vide (31/10) : le menu paraît quand même.
                var inner = tagBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (inner != null)
                {
                    tagBox.IsDropDownOpen = true;
                    inner.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.M, Source = inner });
                    inner.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "ma", Source = inner });
                    await Settle();
                    Check(tagBox.Text == "ma" && Suggestions.IsOpenFor(tagBox) && !tagBox.IsDropDownOpen, "une frappe dans le champ des tags, sans élément natif, propose les tags même si la liste vide s'était « ouverte » au clic (31/10)");
                    inner.Text = "";
                    await Settle();
                }
            }
            Suggestions.AssumeFocusForProbe = true;
            tagBox.Text = "mag";
            await Settle();
            Check(Suggestions.IsOpenFor(tagBox), "taper « mag » dans le champ des tags propose « mage noir » (autocomplète, 31/10)");
            tagBox.Text = "";
            await Settle();
            Suggestions.AssumeFocusForProbe = false;
            Check(shell.UndoPublic() && created.Tags.Count == 1, "…annulable");
            await Settle();
            shell.Binder.SelectItem(project.Category(Project.KeySheets).Id);
            await Settle();
            library = shell.SheetLibraryForProbe;
            Check(library.TagChoicesForProbe == 2, "le filtre par tag propose « Tous les tags » puis « héros » (" + library.TagChoicesForProbe + ")");
            library.FilterByTagForProbe("héros");
            await Settle();
            Check(library.CardsCountForProbe == 1, "filtré par tag : la seule fiche taguée reste (" + library.CardsCountForProbe + ")");
            library.FilterByTagForProbe(null);
            await Settle();
            Check(library.CardsCountForProbe > 1, "sans filtre : toutes reviennent");

            // — Le dépôt d'une tuile sur une catégorie.
            library.DropOnCategoryForProbe(new[] { created.Id }, second.Id);
            await Settle();
            Check(created.CategoryId == second.Id, "une tuile lâchée sur une catégorie en change (" + second.Name + ")");
            Check(shell.UndoPublic() && created.CategoryId == top.Id, "…annulable");
            await Settle();

            // — Supprimer une catégorie avec son modèle, en un cran.
            var probeTemplate = new SheetTemplate { Name = "Modèle sonde" };
            var probeCategory = new SheetCategory { Name = "Catégorie sonde", TemplateId = probeTemplate.Id };
            project.Templates.Add(probeTemplate);
            project.SheetCategories.Add(probeCategory);
            shell.SheetLibraryForProbe.DeleteCategoryNow(probeCategory, probeTemplate);
            await Settle();
            Check(!project.SheetCategories.Contains(probeCategory) && !project.Templates.Contains(probeTemplate), "supprimer la catégorie avec son modèle retire les deux");
            Check(shell.UndoPublic() && project.SheetCategories.Contains(probeCategory) && project.Templates.Contains(probeTemplate), "…et Ctrl+Z rend les deux");
            project.SheetCategories.Remove(probeCategory);
            project.Templates.Remove(probeTemplate);
            created.Parent.Children.Remove(created);
            shell.Binder.Rebuild();
            await Settle();

            // — F1 / F2 et le bouton Généalogie. Le projet d'exemple n'a que
            //   des personnages : un lieu de passage sert de fiche sans relations.
            BinderItem character = null, other = null;
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet || item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                var template = project.TemplateOf(item);
                if (template == null) continue;
                if (template.Relations && character == null) character = item;
                if (!template.Relations && item.Relations.Count == 0 && other == null) other = item;
            }
            BinderItem placeOfPassage = null;
            if (other == null)
            {
                SheetCategory placeCategory = null;
                foreach (var category in project.TopCategories())
                {
                    var template = project.FindTemplate(category.TemplateId);
                    if (template != null && !template.Relations) { placeCategory = category; break; }
                }
                if (placeCategory != null)
                {
                    placeOfPassage = new BinderItem { Kind = ItemKind.Sheet, Title = "Lieu de passage (sonde)", CategoryId = placeCategory.Id, TemplateId = placeCategory.TemplateId, Parent = project.Category(Project.KeySheets) };
                    project.Category(Project.KeySheets).Children.Add(placeOfPassage);
                    shell.Binder.Rebuild();
                    await Settle();
                    other = placeOfPassage;
                }
            }
            if (character != null)
            {
                shell.Binder.SelectItem(character.Id);
                await Settle();
                Check(shell.Sheet.GenealogyButtonVisibleForProbe, "fiche à relations : le bouton « Généalogie (F2) » est là");
                shell.Sheet.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F1, Source = shell.Sheet });
                await Settle();
                Check(shell.Sheet.PreviewOnForProbe, "F1 passe en mode wiki");
                shell.Sheet.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F1, Source = shell.Sheet });
                await Settle();
                Check(!shell.Sheet.PreviewOnForProbe, "…et F1 en sort");
                // Le clavier dans la Pile (29/10, Rémi) : F1 et F2 atteignent
                // quand même la fiche à l'écran, et F2 ne renomme plus rien.
                shell.Binder.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F1, Source = shell.Binder });
                await Settle();
                Check(shell.Sheet.PreviewOnForProbe, "F1 frappé dans la Pile bascule quand même le mode wiki de la fiche à l'écran");
                shell.Binder.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F1, Source = shell.Binder });
                await Settle();
                shell.Binder.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F2, Source = shell.Binder });
                await Settle();
                Check(shell.Sheet.GenealogyOnForProbe, "F2 frappé dans la Pile ouvre la généalogie (" + shell.Sheet.GenealogyStateForProbe + ")");
                shell.Binder.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F2, Source = shell.Binder });
                await Settle();
                Check(!shell.Sheet.GenealogyOnForProbe, "…et la referme");
                Check(string.IsNullOrEmpty(AppSettings.Gesture("rename")), "« Renommer » n'a plus de raccourci d'office (" + (AppSettings.Gesture("rename") ?? "aucun") + ")");
            }
            else Check(false, "une fiche à relations existe dans le projet d'exemple");
            if (other != null)
            {
                shell.Binder.SelectItem(other.Id);
                await Settle();
                Check(!shell.Sheet.GenealogyButtonVisibleForProbe, "fiche sans relations : pas de bouton Généalogie");
                Check(!SheetView.GenealogyAllowed(project, other) && SheetView.GenealogyAllowed(project, character), "une relation généalogique ne peut viser qu'une fiche à relations");
            }
            else Check(false, "une fiche sans relations existe dans le projet d'exemple");
            if (placeOfPassage != null)
            {
                shell.Binder.SelectItem(project.Category(Project.KeyWritings).Id);
                await Settle();
                project.Category(Project.KeySheets).Children.Remove(placeOfPassage);
                shell.Binder.Rebuild();
                await Settle();
            }

            // — L'objectif et la limite d'un écrit, le liseré rouge au tableau.
            var writings = project.Category(Project.KeyWritings);
            BinderItem loose = null;
            foreach (var child in writings.Children) if (child.Kind == ItemKind.Text && loose == null) loose = child;
            if (loose != null)
            {
                shell.Binder.SelectItem(loose.Id);
                await Settle();
                Check(shell.GoalSectionVisibleForProbe, "écrit ouvert : l'objectif et la limite sont dans le Général");
                var words = Marabook.Correction.TextStats.Compute(loose.Document.ToPlainText()).Words;
                shell.SetGoalForProbe(words + 10, TextGoal.Words, Math.Max(1, words - 1), TextGoal.Words);
                await Settle();
                Check(loose.Goal == words + 10 && loose.Limit == Math.Max(1, words - 1), "objectif et limite posés depuis le Général");
                Check(shell.GoalLabelForProbe.Contains("/ " + (words + 10).ToString("N0", CultureInfo.CurrentCulture)) && shell.GoalLabelForProbe.Contains("%"), "l'objectif se dit en part atteinte (" + shell.GoalLabelForProbe + ")");
                Check(words > 1 == shell.LimitLabelForProbe.Contains("dépassée"), "la limite dépassée se dit (" + shell.LimitLabelForProbe + ")");
                shell.Binder.SelectItem(writings.Id);
                await Settle();
                Border looseCard = null;
                foreach (var border in shell.Corkboard.GetVisualDescendants().OfType<Border>())
                    if (ReferenceEquals(border.Tag, loose)) { looseCard = border; break; }
                Check(looseCard != null && (words > 1) == ReferenceEquals(looseCard.BorderBrush, Chrome.Danger), "au tableau, la carte d'un écrit à limite dépassée est cerclée de rouge");
                loose.Goal = 0; loose.Limit = 0;
                shell.Corkboard.Refresh();
                await Settle();
                looseCard = null;
                foreach (var border in shell.Corkboard.GetVisualDescendants().OfType<Border>())
                    if (ReferenceEquals(border.Tag, loose)) { looseCard = border; break; }
                Check(looseCard != null && !ReferenceEquals(looseCard.BorderBrush, Chrome.Danger), "…sans limite, le liseré rouge disparaît");
            }
            else Check(false, "un écrit libre existe dans Écrits");

            // — Le catalogue de polices : les comptes de styles sans plantage, les exclues en bas.
            var crashed = false;
            var styled = 0;
            try { foreach (var entry in FontCatalog.Entries) if (FontCatalog.StyleCount(entry.Family) > 0) styled++; }
            catch (Exception) { crashed = true; }
            Check(!crashed && styled > 0, "les styles de chaque police se comptent sans faire tomber Skia (" + styled + " familles à styles)");
            var arial = FontCatalog.Find("Arial");
            Check(arial == null || FontCatalog.StyleCount(arial.Family) >= 2, "Arial compte au moins deux styles (" + (arial == null ? "absente" : FontCatalog.StyleCount(arial.Family).ToString()) + ")");
            var catalog = new FontCatalogTab();
            var victim = FontCatalog.Entries[0].Name;
            var wasExcluded = AppSettings.IsExcludedFont(victim);
            catalog.ToggleExcludeForProbe(victim);
            var names = catalog.RowNamesForProbe();
            var lastIndex = names.LastIndexOf(victim);
            Check(!wasExcluded ? lastIndex == names.Count - 1 || AppSettings.IsExcludedFont(names[names.Count - 1]) : lastIndex < names.Count - 1,
                "une police exclue passe en bas de la liste (" + victim + " en " + lastIndex + " sur " + names.Count + ")");
            catalog.ToggleExcludeForProbe(victim);
            Check(AppSettings.IsExcludedFont(victim) == wasExcluded, "…et revient à sa place une fois réintégrée");

            // — Les Préférences : un filet contrasté entre les sous-sections ; les facettes des styles.
            var prefs = new PreferencesDialog(shell, project);
            prefs.Show(shell);
            await Settle();
            prefs.Tabs.SelectedIndex = 0;
            await Settle();
            var dividers = 0;
            foreach (var border in prefs.GetVisualDescendants().OfType<Border>())
                if ("caption-divider".Equals(border.Tag)) dividers++;
            Check(dividers >= 2, "Préférences › Général : un fin filet sépare les sous-sections (" + dividers + ")");
            StylesPanel panel = null;
            for (var i = 0; i < prefs.Tabs.Items.Count && panel == null; i++)
            {
                prefs.Tabs.SelectedIndex = i;
                await Settle();
                panel = prefs.GetVisualDescendants().OfType<StylesPanel>().FirstOrDefault();
            }
            Check(panel != null && panel.FacetButtonsForProbe == 5 && panel.FacetForProbe == "Caractère", "styles globaux : cinq facettes en boutons, Caractère d'abord (" + (panel == null ? "panneau absent" : panel.FacetForProbe) + ")");
            if (panel != null)
            {
                panel.ShowFacetForProbe("Paragraphe");
                await Settle();
                Check(panel.FacetForProbe == "Paragraphe", "…un bouton montre sa facette");
                var scroller = panel.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(delegate(ScrollViewer s) { return s.Content is Border; });
                Check(scroller != null, "…dans une zone qui défile (rien ne passe sous le bord)");
            }
            prefs.Close();
            await Settle();
        }

        /// <summary>Les profils (1.1.0, 27/10) : sans profil tout est actif ;
        /// Éditeur·ice cache Fiches, Plans et sprints ; Écrivain·e cache l'onglet
        /// Gabarits & Format, les détails d'édition, les pages éditeur et les
        /// facettes PAO des styles ; Moi-même coche au détail. Le profil est
        /// remis tel qu'il était en sortant.</summary>
        private static async Task ProbeProfiles(MainWindow shell)
        {
            var before = AppSettings.Profile;
            var hiddenBefore = new List<string>(AppSettings.HiddenFeatures);
            try
            {
                Check(!Profiles.Chosen && Profiles.Enabled(Profiles.Sheets) && Profiles.Enabled(Profiles.BookFormat), "sans profil choisi, tout est actif");
                Check(Profiles.HiddenBy(Profiles.Writer).Length == 9 && Profiles.HiddenBy(Profiles.Publisher).Length == 4 && Profiles.HiddenBy(Profiles.Everything).Length == 0,
                    "les profils prédéfinis cachent 9, 4 et 0 fonctionnalités");

                Profiles.Choose(Profiles.Publisher);
                await Settle();
                var rows = shell.Binder.TopRowsForProbe();
                var joined = string.Join(" | ", rows.ToArray());
                Check(!rows.Contains("Fiches") && !rows.Contains("Plans") && rows.Contains("Dictionnaire") && rows.Contains("Encyclopédie") && rows.Contains("Structure"),
                    "profil Éditeur·ice : la Pile cache Fiches et Plans, garde Dictionnaire et ses sections (" + joined + ")");
                Check(!shell.SprintMenuShownForProbe && !shell.NewSheetMenuShownForProbe, "…Lancer un sprint et Nouvelle fiche sortent des menus");
                var sheet = FirstOfKind(shell.Project, ItemKind.Sheet);
                Check(sheet != null && !ItemTiles.IsLinkable(sheet), "…une fiche n'est plus liable (modules, tuiles de l'Accueil)");

                Profiles.Choose(Profiles.Writer);
                await Settle();
                rows = shell.Binder.TopRowsForProbe();
                Check(rows.Contains("Fiches") && rows.Contains("Plans") && shell.SprintMenuShownForProbe, "profil Écrivain·e : Fiches, Plans et sprint reviennent");
                var writings = shell.Project.Category(Project.KeyWritings);
                var book = writings.Children.Count > 1 ? writings.Children[1] : null;
                if (book != null) shell.Binder.SelectItem(book.Id, true);
                await Settle();
                var bookView = shell.BookViewPublic;
                Check(bookView.IsVisible && !bookView.FormatTabShownForProbe, "…l'onglet Gabarits & Format est caché");
                Check(!bookView.EditionDetailsShownForProbe, "…les détails de publication, de destination et commerciaux sont repliés");
                Check(!bookView.ExtraPageButtonsShownForProbe, "…plus de Nouvelle liminaire / page de fin / annexe");
                Check(bookView.StylesFacetCountForProbe == 2, "…les styles n'ont que Caractère et Paragraphe (" + bookView.StylesFacetCountForProbe + " facettes)");
                Check(!BookFlow.Enabled(book), "…les enchaînements de pages se taisent (le réglage du livre reste " + (book.Book == null || book.Book.FlowWarnings) + ")");
                Check(!ComposedRenderer.ShowWidowMarks, "…les marqueurs de veuves et d'orphelines se taisent");
                Check(!shell.Editor.WidowMarksButtonVisibleForProbe, "…et le bouton Veuves/Orphelines du ruban se cache (29/10)");

                Profiles.Choose(Profiles.Custom);
                Profiles.SetHidden(Profiles.Sprints, true);
                await Settle();
                Check(bookView.FormatTabShownForProbe && !shell.SprintMenuShownForProbe && shell.NewSheetMenuShownForProbe && ComposedRenderer.ShowWidowMarks,
                    "profil Moi-même : décocher Sprints cache le sprint seul, le livre retrouve ses onglets, les marqueurs reviennent");
                Check(shell.Editor.WidowMarksButtonVisibleForProbe, "…et le bouton Veuves/Orphelines aussi");
                var chooser = new ProfileChooser(true);
                Check(chooser.Selected == Profiles.Custom && chooser.FeaturesButtonShownForProbe, "le sélecteur reprend le profil et propose « Choisir les fonctionnalités… » pour Moi-même");
                chooser.Select(Profiles.Everything, false);
                Check(!chooser.FeaturesButtonShownForProbe, "…le bouton se cache pour les autres profils");
                var features = new FeaturesWindow();
                features.Show();
                await Settle();
                var boxes = 0;
                foreach (var box in features.GetVisualDescendants().OfType<CheckBox>()) if (box.Tag is string && ((string)box.Tag).StartsWith("feature:")) boxes++;
                Check(boxes == Profiles.Features.Length, "la fenêtre des fonctionnalités liste une case par fonctionnalité (" + boxes + ")");
                features.Close();
                await Settle();

                // L'accueil du premier lancement : « Qui êtes-vous ? » à la place des projets, « Commencer » éteint tant que rien n'est choisi.
                var welcome = new WelcomeWindow(shell, true);
                welcome.Show();
                await Settle();
                var start = welcome.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "profile-start"));
                var tiles = welcome.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is string && ((string)b.Tag).StartsWith("profile:"));
                Check(welcome.ShowsProfileChoice && start != null && !start.IsEnabled && tiles == 4, "l'accueil du premier lancement montre les quatre profils et « Commencer » éteint");
                welcome.Release();
                await Settle();
            }
            finally
            {
                AppSettings.Profile = before;
                Profiles.LoadHidden(hiddenBefore);
                Profiles.Notify();
                await Settle();
            }
        }

        /// <summary>Le diagramme relationnel (1.1.0, 27/10) : le menu Édition › Fiches,
        /// le bouton de la fiche, la fenêtre et ses tuiles, déplacer une famille,
        /// créer une relation, fixer une réciproque, appliquer — le projet suit
        /// (génération de Mère, nature neuve au sélecteur), le livré revient.
        /// MARABOOK_RELATIONS_PNG : une photo de la fenêtre.</summary>
        private static async Task ProbeRelations(MainWindow shell)
        {
            var project = shell.Project;
            Check(shell.SheetsMenuShownForProbe, "le menu Édition › Fiches est là");
            BinderItem sheet = null;
            foreach (var item in project.AllItems())
                if (item.Kind == ItemKind.Sheet && SheetView.GenealogyAllowed(project, item)) { sheet = item; break; }
            if (sheet != null) shell.Binder.SelectItem(sheet.Id);
            await Settle();
            Check(sheet != null && shell.Sheet.RelationsEditorButtonVisibleForProbe, "la fiche montre le bouton du diagramme relationnel à côté de « Ajouter une relation »");

            var editor = new RelationsEditorWindow(project);
            editor.Show();
            await Settle();
            Check(editor.TileCountForProbe == 15, "le diagramme montre une tuile par famille (" + editor.TileCountForProbe + ")");
            var self = editor.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "self-tile"));
            Check(self != null && self.IsVisible, "…et le Personnage au centre");
            editor.MoveFamily("parent", RelationLane.Direct, 1, -2);
            Check(editor.SchemaForProbe.Find("Mère").Generation == -2 && editor.SchemaForProbe.Find("Père").Slot == 1, "déplacer la famille parent : Père et Mère changent de génération et de case");
            editor.MoveFamily("uncle", RelationLane.Direct, 1, -2);
            Check(editor.SchemaForProbe.Find("Oncle").Generation == -2 && editor.SchemaForProbe.Find("Père").Lane == RelationLane.Side && editor.SchemaForProbe.Find("Père").Generation == -1,
                "poser une famille sur une case occupée : les deux s'échangent");
            var family = editor.CreateFamily(RelationLane.Other, 0, 1);
            Check(editor.SchemaForProbe.OfFamily(family).Count == 1 && editor.SelectedForProbe == family && editor.TileCountForProbe == 16, "une relation neuve : une tuile de plus, choisie");
            editor.SetReciprocal(family, "cousin");
            Check(editor.SchemaForProbe.OfFamily(family)[0].Reciprocal == "cousin" && editor.SchemaForProbe.Find("Cousin").Reciprocal == family, "la réciproque se fixe dans les deux sens");
            editor.SetReciprocal(family, family);
            Check(editor.SchemaForProbe.Find("Cousin").Reciprocal == "cousin", "…et se défait : Cousin se répond de nouveau à lui-même");
            var png = Environment.GetEnvironmentVariable("MARABOOK_RELATIONS_PNG");
            if (!string.IsNullOrEmpty(png))
            {
                await Settle(); // les tuiles redessinées veulent une passe de mise en page avant la photo
                try
                {
                    var size = new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height);
                    using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96)))
                    {
                        bitmap.Render(editor);
                        bitmap.Save(png);
                    }
                }
                catch (Exception error) { Console.WriteLine("  (photo du diagramme : " + error.Message + ")"); }
            }
            var applied = await editor.ApplyForProbe();
            await Settle();
            Check(applied && project.RelationSchema.Find("Oncle") != null && project.RelationSchema.Find("Oncle").Generation == -2
                && project.RelationSchema.Find("Mère").Lane == RelationLane.Side, "appliquer : le schéma du projet suit (Oncle en -2, Mère collatérale)");
            shell.AdoptRelationSchemaForProbe();
            Check(RelationKinds.GenerationOf("Tante") == -2 && RelationKinds.LaneOf("Père") == RelationLane.Side, "…et les statiques du projet ouvert répondent avec lui");
            Check(new List<string>(project.AllRelationKinds()).Contains("Nouvelle relation"), "la relation neuve se propose au sélecteur des fiches");
            project.RelationSchema = RelationSchema.Defaults();
            shell.AdoptRelationSchemaForProbe();
            Check(RelationKinds.GenerationOf("Mère") == -1, "le schéma livré remis : Mère en -1");
            if (!editor.IsVisible) { } else editor.Close();
            await Settle();

            // — Les bandes extensibles (27/10 soir) : une colonne de plus, puis de moins.
            var editor2 = new RelationsEditorWindow(project);
            editor2.Show();
            await Settle();
            var sideBefore = editor2.BandSizeForProbe(RelationLane.Side);
            editor2.AddColumn(RelationLane.Side);
            Check(editor2.BandSizeForProbe(RelationLane.Side) == sideBefore + 1, "« + » sur une bande : une colonne de plus (" + editor2.BandSizeForProbe(RelationLane.Side) + ")");
            editor2.MoveFamily("double", RelationLane.Side, sideBefore, 0);
            editor2.RemoveColumn(RelationLane.Side);
            Check(editor2.BandSizeForProbe(RelationLane.Side) == sideBefore + 1, "…occupée, la colonne ne se retire pas");
            editor2.MoveFamily("double", RelationLane.Side, 0, 2);
            editor2.RemoveColumn(RelationLane.Side);
            Check(editor2.BandSizeForProbe(RelationLane.Side) == sideBefore, "…vide, elle se retire");
            editor2.Close();
            await Settle();

            // — La généalogie en vue (27/10 soir) : F2 remplace les papers, les
            // parents joints par un trait, les enfants pendus à un bus.
            if (sheet != null)
            {
                var added = new List<SheetRelation>
                {
                    new SheetRelation { Kind = "Père", Name = "Lirin (sonde)" },
                    new SheetRelation { Kind = "Mère", Name = "Hesina (sonde)" },
                    new SheetRelation { Kind = "Frère", Name = "Tien (sonde)" },
                    new SheetRelation { Kind = "Partenaire", Name = "Syl (sonde)" },
                    new SheetRelation { Kind = "Fils", Name = "Oroden (sonde)" },
                    new SheetRelation { Kind = "Fille", Name = "Rock (sonde)" },
                    new SheetRelation { Kind = "Mentor", Name = "Dalinar (sonde)" }
                };
                sheet.Relations.AddRange(added);
                shell.Binder.SelectItem(sheet.Id);
                await Settle();
                shell.Sheet.ToggleGenealogyForProbe();
                await Settle();
                Check(shell.Sheet.GenealogyOnForProbe, "F2 : la généalogie prend la place des papers de la fiche (" + shell.Sheet.GenealogyStateForProbe + ")");
                var view = shell.Sheet.GenealogyForProbe;
                var tiles = view.GetVisualDescendants().OfType<Border>().Count(b => Equals(b.Tag, "genealogy-node"));
                var lines = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Line>().Count();
                Check(tiles == sheet.Relations.Count && view.GetVisualDescendants().OfType<Border>().Any(b => Equals(b.Tag, "genealogy-self")), "…une tuile par relation et le personnage (" + tiles + ")");
                // Père–Mère : union, branche, bus, deux pieds (le personnage, Frère) = 5 ; personnage–partenaire : union double, branche, bus, deux pieds (Fils, Fille) = 6.
                Check(lines >= 11, "…les traits d'union, branches, bus et pieds sont dessinés (" + lines + " traits)");
                var genealogyPng = Environment.GetEnvironmentVariable("MARABOOK_GENEALOGY_PNG");
                if (!string.IsNullOrEmpty(genealogyPng))
                {
                    try
                    {
                        var size = new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height);
                        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96)))
                        {
                            bitmap.Render(view);
                            bitmap.Save(genealogyPng);
                        }
                    }
                    catch (Exception error) { Console.WriteLine("  (photo de la généalogie : " + error.Message + ")"); }
                }
                shell.Sheet.ToggleGenealogyForProbe();
                await Settle();
                Check(!shell.Sheet.GenealogyOnForProbe, "F2 encore : les papers reviennent");
                foreach (var relation in added) sheet.Relations.Remove(relation);
                shell.Binder.SelectItem(sheet.Id);
                await Settle();
            }
        }

        /// <summary>Les ONGLETS de la 1.1.0 (28/10) : la rangée visible à la
        /// hauteur de la barre de recherche de la Pile, l'onglet actif qui
        /// suit la Pile, Ctrl+T (Accueil dans un onglet neuf), activer,
        /// « Ouvrir dans un nouvel onglet », suivant/précédent cycliques,
        /// Ctrl+W, le plafond (le « + » s'éteint, Ctrl+T se tait, la Pile ne
        /// propose plus), la rangée retenue par le projet, la bascule du
        /// réglage. MARABOOK_TABS_CAPTURE=<png> enregistre la fenêtre pleine
        /// d'onglets.</summary>
        private static async Task ProbeTabs(MainWindow shell)
        {
            var enabledBefore = AppSettings.TabsEnabled;
            var maxBefore = AppSettings.MaxTabs;
            try
            {
                AppSettings.TabsEnabled = true;
                AppSettings.MaxTabs = 10;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                var strip = shell.TabStripForProbe;
                var tabs = shell.TabsForProbe;
                var project = shell.Project;
                Check(strip != null && strip.IsVisible, "la rangée d'onglets est visible quand le réglage est actif");
                var bar = shell.Binder.SearchBarForTabs;
                Check(bar != null && Math.Abs(strip.Bounds.Height - bar.Bounds.Height) < 1.5,
                    "…à la hauteur de la barre de recherche de la Pile (" + strip.Bounds.Height.ToString("0") + " / " + (bar == null ? 0 : bar.Bounds.Height).ToString("0") + " px)");
                var text = FirstTitled(project, "Chapitre premier") ?? FirstOfKind(project, ItemKind.Text); // un écrit à deux paragraphes (la position gardée)
                shell.Binder.SelectItem(text.Id, true);
                await Settle();
                Check(tabs.Count >= 1 && tabs.ActiveItemId == text.Id, "l'onglet actif suit ce que la Pile ouvre (" + tabs.Count + " onglet(s))");
                Check(strip.TitlesForProbe().Count == tabs.Count && strip.ActiveIndexForProbe == tabs.ActiveIndex && strip.TitlesForProbe()[tabs.ActiveIndex] == text.Title,
                    "la rangée reflète le modèle, titre compris (" + string.Join(" | ", strip.TitlesForProbe().ToArray()) + ")");
                var baseline = tabs.Count;
                var home = project.Category(Project.KeyHome);
                shell.OpenNewTabForProbe();
                await Settle();
                Check(tabs.Count == baseline + 1 && tabs.ActiveIndex == tabs.Count - 1 && tabs.ActiveItemId == home.Id && shell.CurrentForProbe == home,
                    "Ctrl+T ouvre l'Accueil dans un onglet neuf, en bout de rangée, actif, et la vue le montre");
                shell.ActivateTabForProbe(0);
                await Settle();
                Check(tabs.ActiveIndex == 0 && shell.CurrentForProbe == text && strip.ActiveIndexForProbe == 0, "activer un onglet rouvre son élément dans la vue, la Pile suit");
                // La position gardée par onglet (28/10, Rémi) : le caret posé
                // loin du début revient au même endroit après un aller-retour.
                var composed = shell.Composed;
                composed.PlaceCaret(0, 5, false);
                await Settle();
                shell.ActivateTabForProbe(1);
                await Settle();
                Check(tabs[0].HasPosition && tabs[0].Paragraph == 0 && tabs[0].Offset == 5, "quitter un onglet retient le caret de son écrit (" + tabs[0].Paragraph + ":" + tabs[0].Offset + ")");
                shell.ActivateTabForProbe(0);
                await Settle();
                await Settle();
                int backParagraph, backOffset;
                composed.GetCaret(out backParagraph, out backOffset);
                Check(shell.CurrentForProbe == text && backParagraph == 0 && backOffset == 5, "…et le retrouve au retour (" + backParagraph + ":" + backOffset + ")");
                shell.Binder.SelectItem(home.Id, true);
                await Settle();
                Check(!tabs[0].HasPosition, "la Pile ouvre autre chose dans l'onglet : la position retenue tombe");
                shell.Binder.SelectItem(text.Id, true);
                await Settle();
                var sheet = FirstOfKind(project, ItemKind.Sheet);
                shell.OpenInNewTabForProbe(sheet);
                await Settle();
                Check(tabs.Count == baseline + 2 && tabs.ActiveIndex == tabs.Count - 1 && shell.CurrentForProbe == sheet,
                    "« Ouvrir dans un nouvel onglet » ajoute en bout de rangée et montre l'élément");
                shell.NextTabForProbe();
                await Settle();
                Check(tabs.ActiveIndex == 0 && shell.CurrentForProbe == text, "Onglet suivant depuis le dernier revient au premier et le montre");
                shell.PreviousTabForProbe();
                await Settle();
                Check(tabs.ActiveIndex == tabs.Count - 1 && shell.CurrentForProbe == sheet, "Onglet précédent depuis le premier va au dernier");
                // Ctrl+Maj+Droite / Gauche (31/10, Rémi) : l'onglet voisin, en tunnel sur la fenêtre.
                shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, Source = shell });
                await Settle();
                Check(tabs.ActiveIndex == 0 && shell.CurrentForProbe == text, "Ctrl+Maj+Droite passe à l'onglet suivant (31/10)");
                shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left, KeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, Source = shell });
                await Settle();
                Check(tabs.ActiveIndex == tabs.Count - 1 && shell.CurrentForProbe == sheet, "…Ctrl+Maj+Gauche à l'onglet précédent");
                // Le glisser-déposer (31/10) : le premier onglet déposé en fin de rangée.
                var rowStrip = shell.TabStripForProbe;
                var titlesBefore = rowStrip.TitlesForProbe();
                var activeBefore = tabs.ActiveItemId;
                rowStrip.MoveForProbe(0, titlesBefore.Count);
                await Settle();
                var titlesAfter = rowStrip.TitlesForProbe();
                Check(titlesAfter.Count == titlesBefore.Count && titlesAfter[titlesAfter.Count - 1] == titlesBefore[0] && tabs.ActiveItemId == activeBefore && shell.CurrentForProbe == sheet && project.OpenTabs[project.OpenTabs.Count - 1] == tabs[tabs.Count - 1].ItemId,
                    "déposer le premier onglet en fin de rangée le déplace, l'actif reste l'actif, le projet retient l'ordre (" + string.Join(" | ", titlesAfter.ToArray()) + ")");
                rowStrip.MoveForProbe(titlesAfter.Count - 1, 0);
                await Settle();
                Check(string.Join("|", rowStrip.TitlesForProbe().ToArray()) == string.Join("|", titlesBefore.ToArray()), "…et le ramener devant rend l'ordre initial");
                shell.CloseActiveTabForProbe();
                await Settle();
                Check(tabs.Count == baseline + 1 && tabs.ActiveItemId == home.Id && shell.CurrentForProbe == home, "Ctrl+W ferme l'actif ; son voisin de gauche prend la vue (l'Accueil)");
                Check(project.OpenTabs.Count == tabs.Count && project.ActiveTab == tabs.ActiveIndex && project.OpenTabs[project.ActiveTab] == home.Id,
                    "le projet retient la rangée et l'actif, sans attendre (.plot v42)");

                AppSettings.MaxTabs = 5;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                Check(tabs.Limit == 5 && strip.PlusEnabledForProbe, "le plafond suit le réglage (5), le « + » reste allumé sous le plafond");
                var guard = 0;
                while (tabs.CanOpen && guard++ < 10) shell.OpenInNewTabForProbe(text);
                await Settle();
                Check(tabs.Count == 5 && !strip.PlusEnabledForProbe, "au plafond : cinq onglets, le « + » s'éteint");
                shell.OpenNewTabForProbe();
                await Settle();
                Check(tabs.Count == 5, "…Ctrl+T n'ouvre plus rien (un mot le dit)");
                Check(shell.Binder.CanOpenTab != null && !shell.Binder.CanOpenTab(), "…et la Pile ne propose plus « Ouvrir dans un nouvel onglet »");
                Check(strip.TabWidthForProbe >= TabStrip.TabMinWidth && strip.TabWidthForProbe <= TabStrip.TabMaxWidth, "les onglets se partagent la place entre 48 et 220 px (" + strip.TabWidthForProbe.ToString("0") + " px)");
                var capture = Environment.GetEnvironmentVariable("MARABOOK_TABS_CAPTURE");
                if (!string.IsNullOrEmpty(capture)) shell.CaptureForProbe(capture);
                AppSettings.MaxTabs = 30;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                Check(tabs.Count == 5 && strip.PlusEnabledForProbe, "relever le plafond rallume le « + » sans toucher à la rangée");
                shell.ActivateTabForProbe(4);
                AppSettings.MaxTabs = 5; // 30 → 5 : rien à fermer (5 ouverts) ; puis une rangée serrée
                shell.ApplyTabsSettingsForProbe();
                shell.OpenInNewTabForProbe(sheet); // refusé : plein
                AppSettings.MaxTabs = 30;
                shell.ApplyTabsSettingsForProbe();
                shell.OpenInNewTabForProbe(sheet); // 6 onglets, l'actif est le dernier
                await Settle();
                AppSettings.MaxTabs = 5;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                Check(tabs.Count == 5 && tabs.ActiveItemId == sheet.Id && shell.CurrentForProbe == sheet, "abaisser le plafond sous la rangée ferme par la droite en épargnant l'actif");

                AppSettings.TabsEnabled = false;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                Check(!strip.IsVisible && tabs.Count == 0 && project.OpenTabs.Count == 0, "réglage éteint : la rangée se retire, plus d'onglet, rien d'écrit dans le projet");
                shell.OpenNewTabForProbe();
                await Settle();
                Check(tabs.Count == 0 && shell.CurrentForProbe == sheet, "…et Ctrl+T se tait");
                shell.Binder.SelectItem(text.Id, true);
                await Settle();
                Check(tabs.Count == 0 && shell.CurrentForProbe == text, "…la Pile ouvre toujours, sans onglet");
                AppSettings.TabsEnabled = true;
                shell.ApplyTabsSettingsForProbe();
                await Settle();
                Check(strip.IsVisible && tabs.Count == 1 && tabs.ActiveItemId == text.Id, "rallumé : un onglet, celui de l'élément ouvert");
            }
            finally
            {
                AppSettings.TabsEnabled = enabledBefore;
                AppSettings.MaxTabs = maxBefore;
                shell.ApplyTabsSettingsForProbe();
            }
        }

        /// <summary>Le GRAPHE RELATIONNEL (1.1.0, 28/10) : l'entrée de la Pile
        /// et du menu, l'ouverture sous le voile puis le calcul (nœuds, liens),
        /// les filtres par nature, la recherche, la sélection (le Général
        /// suit) et le voisinage, un nœud tiré (position épinglée, projet
        /// sale), la matrice des présences et l'hygiène, l'export SVG, la Pile
        /// qui reprend la main, le profil Éditeur·ice qui cache tout.
        /// MARABOOK_GRAPH_CAPTURE=<png> enregistre la fenêtre sur la carte.</summary>
        private static async Task ProbeGraph(MainWindow shell)
        {
            var before = AppSettings.Profile;
            var hiddenBefore = new List<string>(AppSettings.HiddenFeatures);
            try
            {
                AppSettings.Profile = null;
                Profiles.Notify();
                await Settle();
                var project = shell.Project;
                Check(shell.Binder.GraphRowShownForProbe, "la Pile offre « Gros Graph » dans Encyclopédie");
                Check(shell.GraphMenuShownForProbe, "…et le menu Édition › Fiches aussi");
                var view = shell.GraphViewForProbe;
                // La démo ne nomme pas ses personnages dans ses chapitres : un
                // paragraphe de plus dans un écrit fermé, pour avoir des présences.
                var chapterTwo = FirstTitled(project, "Chapitre deux");
                if (chapterTwo != null)
                {
                    var paragraph = new TextParagraph();
                    paragraph.Runs.Add(new TextRun { Text = "Keira Varenh croisa Le marabout sur la grève. Keira ne dit rien." });
                    chapterTwo.Document.Paragraphs.Add(paragraph);
                }
                // Un portrait sur Keira : sa pastille doit le montrer (28/10).
                var keiraSheet = FirstTitled(project, "Keira");
                if (keiraSheet != null && keiraSheet.ImageId == null) keiraSheet.ImageId = project.AddImage(ProbePng(), ".png");
                shell.ShowGraphForProbe();
                Check(view.IsVisible && view.LoaderShownForProbe, "ouvrir le graphe montre la vue et son voile de chargement");
                await view.LoadingForProbe;
                await Settle();
                Check(!view.LoaderShownForProbe && view.NodeCountForProbe > 0 && view.EdgeCountForProbe > 0,
                    "le calcul finit, le voile tombe : " + view.NodeCountForProbe + " nœuds, " + view.EdgeCountForProbe + " liens");
                var keira = FirstTitled(project, "Keira");
                var graph = view.GraphForProbe;
                Check(keira != null && graph.Node(keira.Id) != null && graph.Node(keira.Id).Presence > 0, "Keira est un nœud, présente dans au moins un écrit");
                Check(keira != null && view.NodeShowsPortraitForProbe(keira.Id), "la pastille d'une fiche à image montre son portrait (28/10)");
                var marabout = FirstTitled(project, "Le marabout");
                Check(marabout != null && !view.NodeShowsPortraitForProbe(marabout.Id), "…une fiche sans image garde son symbole ou l'icône d'office");
                Check(shell.CurrentForProbe != null && shell.CurrentForProbe.Id == MainWindow.GraphItemId && shell.TabsForProbe.ActiveItemId == MainWindow.GraphItemId
                    && shell.TabStripForProbe.TitlesForProbe()[shell.TabsForProbe.ActiveIndex] == "Gros Graph",
                    "le graphe est une page (28/10) : élément courant, sélection de la Pile, onglet actif à son nom (« Gros Graph »)");

                // Les défauts (31/10, Rémi) : la Structure seule (relations,
                // liens, tags) entre fiches ; l'Écriture (écrits, présences…) se coche.
                Check(shell.InspectorAvailableForProbe, "sur la page du graphe, l'onglet Général du rail est disponible (31/10, Rémi : il restait grisé comme une racine)");
                Check(shell.GraphToolsInInspectorForProbe && view.TypeFilterCountForProbe == project.SheetCategories.Count,
                    "les outils de la Carte sont dans le Général du rail, avec une case de filtre par catégorie de fiche (" + view.TypeFilterCountForProbe + ")");
                var sheetsOnly = view.ShownNodeCountForProbe;
                var shownStructure = view.ShownEdgeCountForProbe;
                view.ToggleTextsForProbe(true);
                view.ToggleKindForProbe(GraphEdgeKind.Presence, true);
                await Settle();
                var shownAll = view.ShownEdgeCountForProbe;
                Check(view.ShownNodeCountForProbe > sheetsOnly && shownAll > shownStructure, "cocher Écrits et Présences ajoute les écrits et leurs liens de présence (" + shownAll + " > " + shownStructure + ")");
                view.ToggleKindForProbe(GraphEdgeKind.Presence, false);
                await Settle();
                Check(view.ShownEdgeCountForProbe < shownAll, "décocher Présences retire ses liens de la carte (" + view.ShownEdgeCountForProbe + " < " + shownAll + ")");
                view.ToggleKindForProbe(GraphEdgeKind.Presence, true);
                await Settle();
                Check(view.ShownEdgeCountForProbe == shownAll, "…recocher les rend");
                // Le filtre par type de fiche (31/10) : décocher la catégorie de Keira la cache.
                var withKeira = view.ShownNodeCountForProbe;
                view.ShowCategoryForProbe(keira.CategoryId, false);
                await Settle();
                Check(view.ShownNodeCountForProbe < withKeira && !view.NodeShownForProbe(keira.Id), "décocher une catégorie dans « Filtre par type de fiche » cache ses fiches (" + view.ShownNodeCountForProbe + " < " + withKeira + ")");
                view.ShowCategoryForProbe(keira.CategoryId, true);
                await Settle();
                Check(view.ShownNodeCountForProbe == withKeira && view.NodeShownForProbe(keira.Id), "…la recocher les rend");

                view.SearchForProbe("Keira");
                await Settle();
                Check(view.LitNodeCountForProbe >= 1 && view.LitNodeCountForProbe < view.ShownNodeCountForProbe, "la recherche garde en lumière les nœuds qui répondent (" + view.LitNodeCountForProbe + " sur " + view.ShownNodeCountForProbe + ")");
                view.SearchForProbe("");
                await Settle();

                view.SelectForProbe(keira.Id);
                await Settle();
                Check(view.SelectedForProbe == keira.Id && shell.InspectorTitle == keira.Title, "cliquer un nœud le sélectionne et le Général le montre (" + shell.InspectorTitle + ")");
                Check(!shell.GraphToolsInInspectorForProbe, "…à la place des outils de la carte, pas par-dessus (31/10)");
                view.SelectForProbe(null);
                await Settle();
                Check(shell.GraphToolsInInspectorForProbe && shell.InspectorTitle == "Gros Graph", "…désélectionner rend les outils de la carte (" + shell.InspectorTitle + ")");
                view.SelectForProbe(keira.Id);
                await Settle();
                var all = view.ShownNodeCountForProbe;
                view.DepthForProbe(1);
                await Settle();
                var hop = view.ShownNodeCountForProbe;
                Check(hop >= 2 && hop < all, "le voisinage à un saut ne montre que le nœud et ses voisins (" + hop + " sur " + all + ")");
                view.DepthForProbe(0);
                await Settle();
                Check(view.ShownNodeCountForProbe == all, "« Tout » rend le graphe entier");

                var dirtyBefore = shell.DirtyForProbe;
                view.DragForProbe(keira.Id, 123, -45);
                await Settle();
                GraphPosition at;
                Check(project.GraphLayout.TryGetValue(keira.Id, out at) && at.Pinned && Math.Abs(at.X - 123) < 0.01 && shell.DirtyForProbe,
                    "un nœud tiré : sa position est épinglée dans le projet (.plot v43) et le projet est sale (avant : " + dirtyBefore + ")");
                var capture = Environment.GetEnvironmentVariable("MARABOOK_GRAPH_CAPTURE");
                Console.WriteLine("  [graphe] " + view.DebugForProbe);
                if (!string.IsNullOrEmpty(capture)) shell.CaptureForProbe(capture);

                view.ShowFacetForProbe("Présences");
                await Settle();
                var matrix = view.MatrixForProbe;
                Check(view.FacetForProbe == "Présences" && matrix.ColumnCountForProbe == graph.Writings.Count && matrix.RowCountForProbe >= 1,
                    "la matrice : une colonne par écrit du récit (" + matrix.ColumnCountForProbe + "), une ligne par fiche présente (" + matrix.RowCountForProbe + ")");
                Check(matrix.HasRowForProbe(keira.Id), "…Keira a sa ligne");
                // L'ordre des lignes (29/10) : apparition d'office, fréquence sur demande.
                var rowsByFirst = matrix.RowIdsForProbe();
                view.OrderByFrequency = true;
                await Settle();
                var rowsByFrequency = matrix.RowIdsForProbe();
                var columns = matrix.ColumnIdsForProbe();
                var nonIncreasing = rowsByFrequency.Count == rowsByFirst.Count;
                var previous = int.MaxValue;
                foreach (var rowId in rowsByFrequency)
                {
                    var total = 0;
                    foreach (var colId in columns) total += graph.PresenceCount(colId, rowId);
                    if (total > previous) nonIncreasing = false;
                    previous = total;
                }
                Check(view.OrderByFrequency && nonIncreasing, "Ordre › Fréquence : les fiches les plus présentes d'abord (" + rowsByFrequency.Count + " lignes)");
                view.OrderByFrequency = false;
                await Settle();
                Check(string.Join("|", matrix.RowIdsForProbe().ToArray()) == string.Join("|", rowsByFirst.ToArray()), "…Ordre d'apparition : l'ordre initial revient");
                Check(view.ScopeChoicesForProbe >= 3 && view.TypeLabelForProbe.StartsWith("Personnage"), "les filtres (28/10) : l'amplitude (" + view.ScopeChoicesForProbe + " choix) et la nature des fiches, Personnages d'office (" + view.TypeLabelForProbe + ")");
                var matrixCapture = Environment.GetEnvironmentVariable("MARABOOK_MATRIX_CAPTURE");
                if (!string.IsNullOrEmpty(matrixCapture)) shell.CaptureForProbe(matrixCapture);
                // L'amplitude en autocomplète strict (31/10, Rémi) : une frappe
                // sans réponse passe en rouge ; Entrée prend la première proposition.
                var scopeBox = view.ScopeBoxForProbe;
                Suggestions.AssumeFocusForProbe = true; // Focus() ne prend pas ici (fenêtre sans activation réelle)
                scopeBox.Text = "zzz-rien-de-tel";
                await Settle();
                Check(scopeBox.IsEditable && ReferenceEquals(scopeBox.Foreground, Chrome.Danger), "Amplitude : une frappe qui ne répond à aucun choix passe en rouge");
                var lastLabel = (scopeBox.Items[scopeBox.Items.Count - 1] as string ?? "").Trim();
                scopeBox.Text = lastLabel.Length > 6 ? lastLabel.Substring(2, 5) : lastLabel;
                await Settle();
                Check(!ReferenceEquals(scopeBox.Foreground, Chrome.Danger) && Suggestions.IsOpenFor(scopeBox), "…une frappe qui répond redevient noire et ouvre les propositions");
                scopeBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = scopeBox });
                await Settle();
                Check(scopeBox.SelectedIndex >= 0 && !Suggestions.IsOpenFor(scopeBox) && (scopeBox.Text ?? "").Trim() == (scopeBox.SelectedItem as string ?? "").Trim() && scopeBox.Text.Trim().Contains(lastLabel.Length > 6 ? lastLabel.Substring(2, 5) : lastLabel),
                    "…Entrée prend la première proposition et la sélectionne (« " + scopeBox.Text + " »)");
                Suggestions.AssumeFocusForProbe = false;
                view.ScopeForProbe(0);
                await Settle();
                var columnsBefore = matrix.ColumnCountForProbe;
                view.ScopeForProbe(view.ScopeChoicesForProbe - 1); // le dernier écrit du récit
                await Settle();
                Check(matrix.ColumnCountForProbe == 1 && matrix.ColumnCountForProbe < columnsBefore, "limiter l'amplitude à un écrit ne garde que sa colonne");
                // Les filtres survivent au recalcul (28/10) : le projet change,
                // on revient au graphe — même amplitude, même nature, même facette.
                view.TypeForProbe(0);
                await Settle();
                var scopeChosen = view.ScopeLabelForProbe;
                view.Stale = true;
                view.Open(project);
                await view.LoadingForProbe;
                await Settle();
                Check(view.FacetForProbe == "Présences" && view.ScopeLabelForProbe == scopeChosen && view.TypeLabelForProbe == "Toutes les fiches" && matrix.ColumnCountForProbe == 1,
                    "après un recalcul, les filtres de la matrice reviennent tels quels (« " + view.ScopeLabelForProbe + " », " + view.TypeLabelForProbe + ")");
                view.ScopeForProbe(0);
                view.TypeForProbe(0);
                await Settle();
                Check(matrix.ColumnCountForProbe == columnsBefore && matrix.RowCountForProbe >= 2, "tout le récit et toutes les fiches : la matrice entière revient");
                Check(!shell.GraphToolsInInspectorForProbe, "devant les Présences, le Général ne montre pas les outils de la Carte");
                view.ShowFacetForProbe("Carte");
                view.SelectForProbe(null); // Keira était encore choisie : sa fiche tenait le Général
                await Settle();
                Check(shell.GraphToolsInInspectorForProbe && view.FacetButtonCountForProbe == 2, "…de retour à la Carte, ils reviennent ; deux facettes seulement (l'Hygiène est retirée, 31/10)");
                var svg = view.Svg();
                Check(svg.StartsWith("<svg") && svg.Contains("<circle") && svg.Contains("Keira"), "l'export SVG dessine les nœuds et leurs titres");
                var png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-graphe-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    view.ExportPng(png);
                    Check(System.IO.File.Exists(png) && new System.IO.FileInfo(png).Length > 1000, "l'export PNG écrit une image");
                }
                finally { try { System.IO.File.Delete(png); } catch (System.IO.IOException) { } }

                shell.Binder.SelectItem(keira.Id, true);
                await Settle();
                Check(!view.IsVisible && shell.CurrentForProbe == keira, "un clic dans la Pile reprend la main : le graphe se range, la fiche s'ouvre");
                shell.GoBackForProbe();
                await Settle();
                Check(view.IsVisible && shell.CurrentForProbe != null && shell.CurrentForProbe.Id == MainWindow.GraphItemId && !view.LoaderShownForProbe, "« Précédent » rouvre le graphe — sans recalcul, rien n'a changé");
                shell.Binder.SelectItem(keira.Id, true);
                await Settle();

                AppSettings.Profile = Profiles.Publisher;
                Profiles.Notify();
                await Settle();
                Check(!shell.Binder.GraphRowShownForProbe && !shell.GraphMenuShownForProbe, "profil Éditeur·ice : plus de ligne dans la Pile ni d'entrée de menu");
                shell.ShowGraphForProbe();
                await Settle();
                Check(!view.IsVisible, "…et la vue ne s'ouvre pas");
            }
            finally
            {
                AppSettings.Profile = before;
                Profiles.LoadHidden(hiddenBefore);
                Profiles.Notify();
                await Settle();
            }
        }

        /// <summary>Le premier item dont le titre commence ainsi, où qu'il soit dans la Pile ; null sinon.</summary>
        private static BinderItem FirstTitled(Project project, string prefix)
        {
            foreach (var root in project.Roots)
            {
                var found = FirstTitledUnder(root, prefix);
                if (found != null) return found;
            }
            return null;
        }

        private static BinderItem FirstTitledUnder(BinderItem item, string prefix)
        {
            if (item.Title != null && item.Title.StartsWith(prefix, StringComparison.Ordinal)) return item;
            foreach (var child in item.Children)
            {
                var found = FirstTitledUnder(child, prefix);
                if (found != null) return found;
            }
            return null;
        }

        private static BinderItem FirstOfKind(Project project, ItemKind kind)
        {
            foreach (var item in project.AllItems()) if (item.Kind == kind) return item;
            return null;
        }

        /// <summary>La passe des dialogues (29/10) : le grand bouton
        /// « Dialogues » du ruban Formatage, et la passe elle-même sur un
        /// écrit tout au cadratin (le cas moonlight) selon la norme des
        /// réglages — sans ouvrir la fenêtre comparative (modale).
        /// MARABOOK_RIBBON_CAPTURE=<png> enregistre la fenêtre, onglet
        /// Formatage ouvert.</summary>
        private static async Task ProbeDialogues(MainWindow shell)
        {
            var project = shell.Project;
            var text = FirstTitled(project, "Chapitre premier") ?? FirstOfKind(project, ItemKind.Text);
            if (text != null) shell.Binder.SelectItem(text.Id, true);
            await Settle();
            var editor = shell.Editor;
            Check(editor != null && editor.SelectRibbonTabForProbe("Formatage"), "le ruban de l'éditeur a un onglet Formatage");
            await Settle();
            Check(FindButton(shell, "Dialogues") != null, "…qui offre le grand bouton « Dialogues » (29/10)");
            Check(FindButton(shell, "Typographie") != null && FindButton(shell, "Options") != null, "…à côté de Typographie et Options");
            var capture = Environment.GetEnvironmentVariable("MARABOOK_RIBBON_CAPTURE"); // la fenêtre, onglet Formatage ouvert
            if (!string.IsNullOrEmpty(capture)) shell.CaptureForProbe(capture);
            editor.SelectRibbonTabForProbe("Texte");

            // Le cas moonlight : des répliques au cadratin, aucun guillemet, norme « guillemets ».
            var document = new TextDocument();
            foreach (var line in new[] { "Elle demanda :", "— Moi, c’est Alicia et toi ?", "— A…nna.", "Son prénom tomba." })
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = line });
                document.Paragraphs.Add(paragraph);
            }
            var options = new Marabook.Correction.TypographyOptions();
            var result = Marabook.Correction.DialoguePass.Run(document, options);
            Check(result.Blocks == 1 && result.Lines == 2 && result.Wrong == 1 && result.Changes.Count == 2,
                "la passe voit un dialogue de deux répliques hors norme, deux paragraphes à reprendre (" + result.Blocks + "/" + result.Lines + "/" + result.Changes.Count + ")");
            Check(PivotEdit.FlatText(result.Paragraphs[1]).StartsWith("« ") && PivotEdit.FlatText(result.Paragraphs[2]).EndsWith(" »"),
                "…guillemet ouvrant sur la première, fermant sur la dernière");
            options.DialogueOpen = "cadratin";
            result = Marabook.Correction.DialoguePass.Run(document, options);
            Check(result.Blocks == 1 && result.Changes.Count == 0, "norme cadratin : le même écrit est en règle");
            var compare = new TypographyCompareWindowProbe(shell, Marabook.Correction.DialoguePass.Run(document, new Marabook.Correction.TypographyOptions()));
            Check(compare.Title.StartsWith("Dialogues — "), "la fenêtre comparative porte le nom de la passe (" + compare.Title + ")");
            compare.Close();
        }

        /// <summary>Le lot de correctifs du 29/10 (Rémi) : ruban (Prépresse en
        /// dernier, approche et lignes lâches dans Formatage, Gabarit et
        /// marges grisés pour un écrit de livre, Veuves/Orphelines), croix de
        /// la barre Ctrl+F, vignettes de portée des styles, recherche de la
        /// Pile par nom, palette « Ouvrir par le nom », dialogue Nouveau
        /// livre, gabarits .marag.</summary>
        private static async Task ProbeFixes2910(MainWindow shell)
        {
            var project = shell.Project;
            var editor = shell.Editor;
            shell.HideVeilForProbe(); // la démo garde le voile de l'accueil : coquille désactivée, états « grisé » illisibles
            await Settle();
            // — Le ruban.
            var heads = editor.RibbonTabHeadersForProbe;
            Check(heads.EndsWith("|Prépresse") && !heads.Contains("Composition"), "le ruban : Composition est devenu Prépresse, en dernier (" + heads + ")");
            Check(editor.TrackingInFormatTabForProbe, "l'approche et les lignes lâches vivent dans Formatage");
            editor.SelectRibbonTabForProbe("Formatage");
            await Settle();
            Check(FindButton(shell, "Lignes lâches") != null, "…le bouton Lignes lâches s'y voit");
            var formatCapture = Environment.GetEnvironmentVariable("MARABOOK_FORMAT_CAPTURE");
            if (!string.IsNullOrEmpty(formatCapture)) shell.CaptureForProbe(formatCapture);
            // — Un écrit de livre : Gabarit, marges, taille et folio grisés ; hors livre, tout est actif.
            BinderItem inBook = null, outside = null;
            foreach (var item in project.AllItems())
            {
                if (item.Kind != ItemKind.Text || item.Document == null || item.RootCategory().CategoryKey != Project.KeyWritings) continue;
                if (inBook == null && !item.IsInsideOutOfBook() && item.EnclosingBook() != null) inBook = item;
                if (outside == null && item.EnclosingBook() == null) outside = item;
            }
            if (inBook != null)
            {
                shell.Binder.SelectItem(inBook.Id, true);
                await Settle();
                Check(!editor.DecorTabEnabledForProbe && !editor.PageGridEnabledForProbe, "un écrit de livre (« " + inBook.Title + " ») : l'onglet Gabarit, les marges et la taille sont grisés");
                // Le libellé de l'onglet grisé se distingue (29/10, Rémi) : encre pâle, opacité réduite ; Texte reste tel quel.
                var tabs = shell.GetVisualDescendants().OfType<TabItem>().ToList();
                var decor = tabs.FirstOrDefault(t => (t.Header as string) == "Gabarit");
                var texte = tabs.FirstOrDefault(t => (t.Header as string) == "Texte");
                var decorBg = decor == null ? null : decor.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Bg");
                var texteBg = texte == null ? null : texte.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Bg");
                Check(decorBg != null && ReferenceEquals(Avalonia.Controls.Documents.TextElement.GetForeground(decorBg), Chrome.FaintText) && decorBg.Opacity < 1,
                    "…le libellé « Gabarit » passe en gris clair (encre pâle, opacité " + (decorBg == null ? "?" : decorBg.Opacity.ToString("0.00")) + ")");
                Check(texteBg != null && texte.IsEffectivelyEnabled && texteBg.Opacity >= 1 && !ReferenceEquals(Avalonia.Controls.Documents.TextElement.GetForeground(texteBg), Chrome.FaintText),
                    "…« Texte » garde son encre (actif : " + (texte == null ? "?" : texte.IsEffectivelyEnabled.ToString()) + ", opacité " + (texteBg == null ? "?" : texteBg.Opacity.ToString("0.00")) + ")");
                var typographyBtn = FindButton(shell, "Typographie");
                Check(typographyBtn != null && typographyBtn.IsEffectivelyEnabled, "…les boutons du ruban restent actifs (Typographie : " + (typographyBtn == null ? "absent" : typographyBtn.IsEffectivelyEnabled.ToString()) + ")");
            }
            else Console.WriteLine("  (aucun écrit de livre dans la démo : grisage non vérifié)");
            if (outside != null)
            {
                shell.Binder.SelectItem(outside.Id, true);
                await Settle();
                Check(editor.DecorTabEnabledForProbe && editor.PageGridEnabledForProbe, "un écrit hors livre (« " + outside.Title + " ») : Gabarit, marges et taille restent actifs");
            }
            else Console.WriteLine("  (aucun écrit hors livre dans la démo : l'inverse n'est pas vérifié)");
            // — Veuves/Orphelines.
            editor.SelectRibbonTabForProbe("Mise en page");
            await Settle();
            Check(editor.WidowMarksButtonVisibleForProbe && editor.WidowMarksShownForProbe, "Mise en page › Veuves/Orphelines : visible et actif d'office");
            editor.ToggleWidowMarksForProbe();
            Check(!ComposedRenderer.ShowWidowMarks && !AppSettings.ShowWidowMarks, "…décoché : les marqueurs se taisent (la composition ne bouge pas)");
            editor.ToggleWidowMarksForProbe();
            Check(ComposedRenderer.ShowWidowMarks && AppSettings.ShowWidowMarks, "…recoché : ils reviennent");
            editor.SelectRibbonTabForProbe("Texte");
            await Settle();
            // — Les vignettes de portée des styles, dans le combo du ruban.
            Check(editor.StyleComboEntriesForProbe > 0 && editor.StyleComboScopeIconsForProbe == editor.StyleComboEntriesForProbe,
                "chaque style du ruban porte sa vignette de portée (" + editor.StyleComboScopeIconsForProbe + "/" + editor.StyleComboEntriesForProbe + ")");
            var stylesCapture = Environment.GetEnvironmentVariable("MARABOOK_STYLES_CAPTURE"); // les lignes de la liste des styles, rendues à part
            if (!string.IsNullOrEmpty(stylesCapture))
            {
                var rows = new StackPanel { Margin = new Thickness(8) };
                var sheet = project.Styles;
                var bookStyle = new ParagraphStyle { Id = "sonde-livre", Name = "Style du livre (sonde)", Scope = ParagraphStyle.ScopeBook };
                var docStyle = new ParagraphStyle { Id = "sonde-ecrit", Name = "Style de l'écrit (sonde)", Scope = ParagraphStyle.ScopeDocument };
                foreach (var style in sheet.Styles) rows.Children.Add(StylesPanel.RowContent(style));
                rows.Children.Add(StylesPanel.RowContent(bookStyle));
                rows.Children.Add(StylesPanel.RowContent(docStyle));
                var frame = new Border { Background = Chrome.RaisedBg, Child = rows, Width = 260 };
                frame.Measure(new Size(260, 400));
                frame.Arrange(new Rect(0, 0, 260, frame.DesiredSize.Height));
                using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(260, Math.Max(1, (int)frame.DesiredSize.Height)), new Vector(96, 96)))
                {
                    bitmap.Render(frame);
                    bitmap.Save(stylesCapture);
                }
            }
            // — La croix de la barre Ctrl+F.
            editor.ShowSearch();
            await Settle();
            var close = shell.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (ToolTip.GetTip(b) as string ?? "") == "Fermer (Échap)");
            Check(editor.SearchBarShownForProbe && close != null, "la barre Ctrl+F a sa croix de fermeture");
            if (close != null) close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Check(!editor.SearchBarShownForProbe, "…qui la referme");
            // — La recherche de la Pile par nom.
            var sample = FirstTitled(project, "Chapitre") ?? FirstOfKind(project, ItemKind.Text);
            if (sample != null)
            {
                var byName = BinderView.NameMatches(project, sample.Title, SearchKind.All, 50);
                Check(byName.Count >= 1 && byName.Contains(sample), "la recherche de la Pile trouve « " + sample.Title + " » par son nom (" + byName.Count + ")");
                var bodyWord = "xyzzyquux"; // un mot qui n'est dans aucun titre
                Check(BinderView.NameMatches(project, bodyWord, SearchKind.All, 50).Count == 0, "…et ne cherche que les noms (un mot absent des titres ne rend rien)");
            }
            // — La palette « Ouvrir par le nom » (Ctrl+,).
            var palette = shell.QuickOpenForProbe;
            await Settle();
            Check(palette != null && palette.IsOpen && palette.BackdropShownForProbe, "Ctrl+, ouvre la palette « Ouvrir par le nom » au milieu de la fenêtre, sur un voile qui couvre le reste");
            if (sample != null && palette != null)
            {
                palette.TypeForProbe(sample.Title.Substring(0, Math.Min(5, sample.Title.Length)));
                await Settle();
                var paletteCapture = Environment.GetEnvironmentVariable("MARABOOK_PALETTE_CAPTURE");
                if (!string.IsNullOrEmpty(paletteCapture)) shell.CaptureForProbe(paletteCapture);
                var firstId = palette.FirstResultIdForProbe;
                Check(palette.ResultCountForProbe >= 1 && firstId != null, "…taper un début de nom liste les éléments (" + palette.ResultCountForProbe + ")");
                palette.ChooseForProbe();
                await Settle();
                Check(!palette.IsOpen && shell.Binder.SelectedItem != null && shell.Binder.SelectedItem.Id == firstId, "…choisir un résultat le sélectionne dans la Pile et referme la palette");
            }
            else if (palette != null) palette.Close();
            Check(AppSettings.DisplayGesture("Ctrl+OemComma") == "Ctrl+,", "le raccourci s'affiche « Ctrl+, » (" + AppSettings.DisplayGesture("Ctrl+OemComma") + ")");
            // — Le dialogue Nouveau livre (bâti sans être montré).
            var withFormat = NewBookDialog.BuildForProbe(true);
            var template = withFormat.TemplateForProbe;
            Check(withFormat.HasFormatForProbe && (withFormat.FormatForProbe ?? "").StartsWith("A5") && withFormat.MirrorForProbe,
                "Nouveau livre : format A5 et marges miroir d'office (" + withFormat.FormatForProbe + ")");
            Check(Math.Abs(template.PageWidthMm - 148) < 0.1 && Math.Abs(template.MarginLeftMm - 30) < 0.1 && Math.Abs(template.MarginRightMm - 20) < 0.1,
                "…le gabarit qui en sort : 148 mm de large, petit fond 30, grand fond 20");
            var writer = NewBookDialog.BuildForProbe(false);
            var plain = writer.TemplateForProbe;
            Check(!writer.HasFormatForProbe && Math.Abs(plain.PageWidthMm - 210) < 0.1 && Math.Abs(plain.MarginLeftMm - 25) < 0.1 && Math.Abs(plain.MarginRightMm - 25) < 0.1,
                "…sans « Gabarits et format » au profil : le titre seul, A4, marges uniformes de 25 mm");
            withFormat.Close();
            writer.Close();
            // — Les gabarits .marag (l'ancien .usgab encore lu).
            var gabarit = FirstOfKind(project, ItemKind.PageTemplate);
            if (gabarit != null)
            {
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-marag");
                System.IO.Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder, "sonde" + Marabook.Persistence.GabaritFile.Extension);
                Marabook.Persistence.GabaritFile.Export(gabarit, path);
                var back = Marabook.Persistence.GabaritFile.Import(path);
                Check(path.EndsWith(".marag") && back != null && back.Title == gabarit.Title, "un gabarit s'exporte en .marag et se relit (« " + back.Title + " »)");
                var legacy = System.IO.Path.Combine(folder, "ancien.usgab");
                System.IO.File.WriteAllText(legacy, System.IO.File.ReadAllText(path).Replace("\"marag\"", "\"usgab\""), new System.Text.UTF8Encoding(false));
                var old = Marabook.Persistence.GabaritFile.Import(legacy);
                Check(old != null && old.Title == gabarit.Title, "…et un .usgab d'avant se lit encore");
                Check(Marabook.Persistence.GabaritFile.Filter.Contains("*.marag;*.usgab"), "le filtre de fichiers propose les deux extensions");
            }
            else Console.WriteLine("  (aucun gabarit dans la démo : export .marag non vérifié)");
        }

        /// <summary>Le tutoriel (1.1.0, 29/10) : démarre sur le projet d'exemple
        /// embarqué, parcourt toutes les étapes par « Suivant » (chaque consigne
        /// s'exécute sans faute), vérifie les mises en valeur, qu'aucun succès
        /// ne tombe en route, et qu'au « Terminer » l'accueil revient avec
        /// « Tour du proprio ». Les consignes incomprises sont listées.</summary>
        private static async Task ProbeTutorial(MainWindow shell)
        {
            var achievementsBefore = AppSettings.Achievements.Count;
            Check(MainWindow.TutorialAsset("PROJET-TEST.plot") != null && MainWindow.TutorialAsset("Tutoriel.md") != null, "le projet d'exemple et le script du tutoriel sont trouvés (assets/tutoriel)");
            Check(shell.StartTutorial() && shell.TutorialActive, "« Démarrer le tutoriel » ouvre le projet d'exemple et pose le tuto");
            await Settle();
            var window = shell.TutorialWindowForProbe;
            var veil = shell.TutorialVeilForProbe;
            Check(window != null && window.IsVisible && veil != null && veil.IsVisible, "la fenêtre du tuto est là, le voile posé sur la coquille");
            Check(shell.Project != null && shell.Project.Name == "Tutoriel" && (shell.TutorialPathForProbe ?? "").Contains("tutoriel"), "le projet ouvert est la copie temporaire « Tutoriel » (" + shell.TutorialPathForProbe + ")");
            Check(!AppSettings.RecentFiles.Contains(shell.TutorialPathForProbe), "…absente des projets récents");
            var total = shell.TutorialStepCountForProbe;
            Check(total >= 40 && window.ChapterForProbe.StartsWith("Bienvenue") && !window.PreviousEnabledForProbe, "l'accueil du script d'abord (" + total + " étapes, Précédent inerte)");
            foreach (var gap in shell.TutorialGapsForProbe) Console.WriteLine("  [tutoriel] consigne incomprise : " + gap);
            Check(shell.TutorialGapsForProbe.Count == 0, "aucune consigne incomprise dans le script embarqué (" + shell.TutorialGapsForProbe.Count + ")");

            window.ClickNextForProbe();
            await Settle();
            Check(window.ChapterForProbe.StartsWith("1.") && shell.TutorialIndexForProbe == 1, "Suivant : le chapitre 1 (" + window.ChapterForProbe + ")");
            Check(veil.HoleCount == 1, "« highlight pile » : un trou dans le voile (" + veil.HoleCount + ")");
            Check(veil.HoleCount == 1 && shell.TutorialVeilCatchesForProbe(veil.Holes[0].Center), "…ce qui est mis en valeur se regarde sans se toucher : le voile attrape le clic dans le trou");
            Check(shell.TutorialWindowTopmostForProbe, "la fenêtre du tuto est toujours au-dessus (Topmost)");
            var capture = Environment.GetEnvironmentVariable("MARABOOK_TUTORIAL_CAPTURE");
            if (!string.IsNullOrEmpty(capture)) shell.CaptureForProbe(capture);
            var windowCapture = Environment.GetEnvironmentVariable("MARABOOK_TUTORIAL_WINDOW_CAPTURE"); // la fenêtre du tuto, à part
            if (!string.IsNullOrEmpty(windowCapture)) window.CaptureForProbe(windowCapture);
            window.ClickPreviousForProbe();
            await Settle();
            Check(shell.TutorialIndexForProbe == 0 && veil.HoleCount == 0, "Précédent : retour à l'accueil du script, plus de trou");
            window.ClickNextChapterForProbe();
            await Settle();
            Check(window.ChapterForProbe.StartsWith("1."), "la flèche de chapitre depuis l'accueil : le chapitre 1");
            window.ClickNextChapterForProbe();
            await Settle();
            Check(window.ChapterForProbe.StartsWith("2."), "…puis le chapitre 2 (les étapes internes sautées)");

            // Tout le parcours : chaque étape s'exécute sans faute ; quelques
            // jalons vérifiés au passage.
            var faults = 0;
            var seenRibbon = false; var seenSheetWiki = false; var seenTemplates = false; var seenJournal = false; var seenChart = false;
            var seenEntryDialog = false; var seenUnknownDialog = false; var seenBrick = false;
            var guard = 0;
            while (window.NextLabelForProbe != "Terminer" && guard++ < 200)
            {
                window.ClickNextForProbe();
                await Settle();
                var actions = shell.TutorialCurrentActionsForProbe;
                if (actions.Contains("ribbon-tab:Prépresse") && shell.Editor.IsVisible && veil.HoleCount >= 1) seenRibbon = true;
                if (actions.Contains("sheet:wiki") && shell.Sheet.IsVisible && shell.Sheet.PreviewOnForProbe) seenSheetWiki = true;
                if (actions.Contains("templates-tab:Champs") && shell.TemplatesOpenForProbe) seenTemplates = true;
                if (actions.Contains("plan:chart") && shell.PlanChartOnForProbe) seenChart = true;
                if (actions.Contains("journal:achievements") && shell.JournalAchievementsShownForProbe) seenJournal = true;
                if (actions.Contains("dictionary:new") && shell.TutorialOpenWindowsForProbe >= 1) seenEntryDialog = true;
                if (actions.Contains("dictionary:unknown") && shell.TutorialOpenWindowsForProbe >= 1) seenUnknownDialog = true;
                if (actions.Contains("plan:entry") && shell.TutorialOpenWindowsForProbe >= 1) seenBrick = true;
                if (shell.TutorialWindowForProbe == null) { faults++; break; }
            }
            Check(seenEntryDialog, "jalon : « Nouvelle entrée » ouverte sans bloquer, inerte");
            Check(seenUnknownDialog, "jalon : « Mots inconnus » ouverte sans bloquer, inerte");
            Check(seenBrick, "jalon : la fiche d'une brique existante du plan, ouverte sans bloquer, inerte");
            Check(shell.TutorialOpenWindowsForProbe == 0, "au dernier pas, les fenêtres ouvertes par le tuto sont refermées (" + shell.TutorialOpenWindowsForProbe + ")");
            Check(window.NextLabelForProbe == "Terminer" && shell.TutorialIndexForProbe == total - 1, "le parcours entier par Suivant mène au « Terminer » (" + shell.TutorialIndexForProbe + "/" + (total - 1) + ")");
            var endCapture = Environment.GetEnvironmentVariable("MARABOOK_TUTORIAL_END_CAPTURE"); // la fenêtre du tuto à l'avant-dernière étape (les blocs de raccourcis)
            if (!string.IsNullOrEmpty(endCapture))
            {
                window.ClickPreviousForProbe();
                await Settle();
                window.CaptureForProbe(endCapture);
                window.ClickNextForProbe();
                await Settle();
            }
            Check(seenRibbon, "jalon : l'onglet Prépresse du ruban, mis en valeur");
            Check(seenSheetWiki, "jalon : le mode wiki de la fiche");
            Check(seenTemplates, "jalon : l'éditeur de modèles ouvert sur Champs");
            Check(seenChart, "jalon : le graphique d'intensité du plan");
            Check(seenJournal, "jalon : les succès du journal");
            Check(AppSettings.Achievements.Count == achievementsBefore, "aucun succès débloqué pendant le tutoriel (" + AppSettings.Achievements.Count + ")");
            window.ClickNextForProbe(); // Terminer
            await Settle();
            Check(!shell.TutorialActive && shell.TutorialWindowForProbe == null && !veil.IsVisible, "Terminer : le tuto se range, le voile se lève");
            Check(shell.WelcomeShownForProbe, "…l'accueil revient");
            Check(AppSettings.Achievements.ContainsKey(Achievements.Tutorial), "…et « Tour du proprio » est débloqué");
            Check(!shell.TemplatesOpenForProbe, "l'éditeur de modèles ouvert par le tuto est refermé");
            shell.CloseWelcomeForProbe();
            await Settle();
            // Fermer sans terminer : pas de succès de plus.
            AppSettings.Achievements.Remove(Achievements.Tutorial);
            Check(shell.StartTutorial(), "le tuto se relance");
            await Settle();
            shell.TutorialWindowForProbe.ClickCloseForProbe();
            await Settle();
            Check(!shell.TutorialActive && shell.WelcomeShownForProbe && !AppSettings.Achievements.ContainsKey(Achievements.Tutorial), "Fermer : fin du tuto, accueil, sans le succès");
            shell.CloseWelcomeForProbe();
            await Settle();
        }

        /// <summary>La fenêtre comparative bâtie sans être montrée (son titre).</summary>
        private sealed class TypographyCompareWindowProbe
        {
            public readonly string Title;
            private readonly Window _window;
            public TypographyCompareWindowProbe(MainWindow shell, Marabook.Correction.TypographyPassResult result)
            {
                _window = TypographyCompareWindow.BuildForProbe(shell, result, "Sonde", "Dialogues");
                Title = _window.Title;
            }
            public void Close() { _window.Close(); }
        }
    }
}
