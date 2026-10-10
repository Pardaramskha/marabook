using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

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
                // — Lignes lâches (09/10) : signalées par défaut, le bouton du ruban Mise en page les coupe et les rallume.
                Check(editor.LooseLinesShownForProbe && AppSettings.ShowLooseLines, "les lignes lâches sont signalées par défaut");
                editor.ToggleLooseLinesForProbe();
                Check(!editor.LooseLinesShownForProbe && !ComposedRenderer.ShowLooseLines, "Mise en page › Lignes lâches les éteint");
                editor.ToggleLooseLinesForProbe();
                Check(editor.LooseLinesShownForProbe, "…et les rallume");
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
                Check(composed != null && composed.RunEditorAction("loose-lines") && !editor.LooseLinesShownForProbe, "l'action de l'éditeur « loose-lines » bascule le bouton du ruban");
                composed.RunEditorAction("loose-lines");
                Check(editor.LooseLinesShownForProbe, "…dans les deux sens");
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
                    Border alert = null;
                    foreach (var border in shell.GetVisualDescendants().OfType<Border>())
                    {
                        var tip = ToolTip.GetTip(border) as string;
                        if (tip != null && tip.StartsWith("Ce projet demande des polices")) alert = border;
                    }
                    Check(alert != null && alert.IsVisible, "la barre d'état montre la pastille rouge « " + (baselineUnresolved + 1) + " police(s) manquante(s) »");
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
                    var calmTip = baselineUnresolved > 0 ? "Ce projet demande des polices" : "Polices absentes"; // d'autres manquent encore (Linux) : la pastille reste rouge
                    Check(alert != null && alert.IsVisible && (ToolTip.GetTip(alert) as string ?? "").StartsWith(calmTip), "la pastille s'apaise, toujours cliquable (" + baselineUnresolved + " autre(s) manquante(s))");
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
                finally { try { System.IO.File.Delete(plotPath); } catch { } }
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
                    Check(shell.RightColumnWidth < 1, "racine Fiches sans sélection : la colonne de droite est repliée (" + shell.RightColumnWidth.ToString("0") + ")");
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

                // — Les Préférences : huit onglets, chacun un contenu.
                var prefs = new PreferencesDialog(shell, shell.Project);
                prefs.Show(shell);
                await Settle();
                Check(prefs.Tabs.Items.Count == 8, "les Préférences ont huit onglets (" + prefs.Tabs.Items.Count + ")");
                var titles = new List<string>();
                foreach (var item in prefs.Tabs.Items) titles.Add(((TabItem)item).Header as string);
                Check(titles.Contains("Raccourcis") && titles.Contains("DLC") && titles.Contains("Catalogue de polices"),
                    "…Raccourcis, DLC et Catalogue de polices en font partie");
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
            }
            catch (Exception error)
            {
                Failures++;
                Console.WriteLine("  ÉCHEC  exception : " + error);
            }
            Console.WriteLine(Failures == 0 ? "SONDE OK (" + _checks + " vérifications)" : "*** SONDE : " + Failures + " échec(s) sur " + _checks + " ***");
        }
    }
}
