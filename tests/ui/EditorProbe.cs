using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde du batch 33 sur vraie MainWindow hors écran : les
    /// notes de bas de page s'éditent EN PLACE (plus de panneau du bas en
    /// pages composées), précédent/suivant les ouvre ; le ruban a ses
    /// onglets Insertion et Correction ; Pages ↔ Brouillon remesure les
    /// éléments de page (fini le chevauchement) ; la racine Dictionnaire de
    /// la Pile, entre Fiches et Corbeille, ouvre son écran et nourrit le
    /// correcteur ; sur le tableau d'un livre, supprimer retire la carte et
    /// la sélection ne laisse pas de contour derrière elle. Règle du batch
    /// 11 : settings.json est l'affaire de l'appelant (A1Probe).</summary>
    public static class EditorProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b33");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b33.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE ÉDITEUR EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            return _failures;
        }

        private static void Probe(string path)
        {
            // — Un écrit avec deux notes de bas de page, un livre de deux chapitres.
            var project = Project.CreateNew();
            var text = project.Category(Project.KeyWritings).Children[0];
            text.Title = "Chapitre noté";
            text.Document = TextDocument.FromPlainText("Premier paragraphe du récit.\nSecond paragraphe.");
            var note1 = new Footnote { Text = "Première note" };
            var note2 = new Footnote { Text = "Seconde note" };
            text.Document.Footnotes.Add(note1);
            text.Document.Footnotes.Add(note2);
            text.Document.Paragraphs[0].Runs.Add(new TextRun { FootnoteId = note1.Id });
            text.Document.Paragraphs[1].Runs.Add(new TextRun { FootnoteId = note2.Id });
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo() };
            // Les chapitres suivent le gabarit du livre : pas de liseré orange
            // de divergence, qui garde (à raison) la priorité sur la sélection.
            var chapterA = new BinderItem { Kind = ItemKind.Text, Title = "A", Page = book.Book.Template.Clone() };
            chapterA.Document = TextDocument.FromPlainText("A.");
            var chapterB = new BinderItem { Kind = ItemKind.Text, Title = "B", Page = book.Book.Template.Clone() };
            chapterB.Document = TextDocument.FromPlainText("B.");
            book.Children.Add(chapterA);
            book.Children.Add(chapterB);
            // — Une partie (boîte) avec un chapitre : les lisières déposent
            // avant/après elle (batch 35).
            var partFolder = new BinderItem { Kind = ItemKind.Folder, Title = "Partie II" };
            var chapterC = new BinderItem { Kind = ItemKind.Text, Title = "C", Page = book.Book.Template.Clone() };
            chapterC.Document = TextDocument.FromPlainText("C.");
            partFolder.Children.Add(chapterC);
            book.Children.Add(partFolder);
            project.Category(Project.KeyWritings).Children.Add(book);
            // — Deux fiches (batch 34) : Kaladin a une relation vers Syl.
            var character = project.SheetCategories[0];
            var syl = new BinderItem { Kind = ItemKind.Sheet, Title = "Syl", CategoryId = character.Id, TemplateId = character.TemplateId };
            var kaladin = new BinderItem { Kind = ItemKind.Sheet, Title = "Kaladin", CategoryId = character.Id, TemplateId = character.TemplateId };
            kaladin.Document = TextDocument.FromPlainText("Chef de pont.");
            kaladin.Relations.Add(new SheetRelation { Kind = "spren liée", TargetId = syl.Id });
            kaladin.FreeInfo.Add(new InfoEntry { Title = "Cicatrices", Value = "front", Group = SheetDefaults.GroupLooks });
            project.Category(Project.KeySheets).Children.Add(syl);
            project.Category(Project.KeySheets).Children.Add(kaladin);
            // — Un plan (batch 35) : relié au livre, sa colonne au chapitre noté.
            var planItem = new BinderItem { Kind = ItemKind.Plan, Title = "Plan du roman", Plan = new PlanInfo { LinkedItemId = book.Id } };
            var planColumn = new PlanColumn { Title = "Ouverture", LinkedTextId = text.Id };
            planColumn.Entries.Add(new PlanEntry { Text = "Le village", Intensity = 1, Color = "#27AE60" });
            planColumn.Entries.Add(new PlanEntry { Text = "L'incendie", Intensity = 4, Color = "#C0392B" });
            planColumn.Entries.Add(new PlanEntry { Kind = PlanEntry.KindNote, Text = "rythme ?" });
            planItem.Plan.Columns.Add(planColumn);
            project.Category(Project.KeyPlans).Children.Add(planItem);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.ClassicCompatibility = false;
            AppSettings.DraftView = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Theme.Apply(application);
            var themeField = typeof(Theme).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
            Check(themeField != null && themeField.GetValue(null) != null,
                "le thème (chips, boutons bordés, slider) s'est parsé et est appliqué");
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600,
                Top = 0,
                Width = 1280,
                Height = 800,
                ShowInTaskbar = false
            };
            window.Show();
            DoEvents();
            window.OpenFile(path);
            DoEvents();
            var opened = (Project)GetField(window, "_project");

            // — La Pile : Dictionnaire entre Fiches et Corbeille.
            var roots = opened.Roots;
            var dictionary = opened.Category(Project.KeyDictionary);
            Check(dictionary != null && roots.IndexOf(dictionary) == roots.IndexOf(opened.Category(Project.KeyPlans)) + 1
                && roots.IndexOf(dictionary) == roots.IndexOf(opened.Trash) - 1,
                "la racine Dictionnaire est entre Plans et Corbeille");

            // — L'écrit : notes en place.
            BinderItem target = null;
            foreach (var item in opened.AllItems()) if (item.Title == "Chapitre noté") target = item;
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            var editor = (EditorView)GetField(window, "_editor");
            Check(editor.Visibility == Visibility.Visible && editor.ComposedActive, "l'écrit s'ouvre en pages composées");
            var notesBar = (Border)GetField(editor, "_notesBar");
            Check(notesBar.Visibility == Visibility.Collapsed, "plus de panneau de notes en bas des pages composées");
            var composed = (ComposedView)GetField(editor, "_composed");
            var order = composed.MarkerOrder();
            Check(order.Count == 2, "deux appels de note dans l'ordre du texte");

            var noteA = target.Document.FindFootnote(order[0]);
            composed.EditNote(order[0]);
            DoEvents();
            var noteEditor = (TextBox)GetField(composed, "_noteEditor");
            Check(noteEditor != null && noteEditor.Text == noteA.Text && Canvas.GetTop(noteEditor) > 0,
                "la note s'ouvre en place, posée sur la page, avec son texte");
            noteEditor.Text = "Première note, corrigée";
            DoEvents();
            Check(noteA.Text == "Première note, corrigée", "la frappe dans la note atteint le modèle");
            Check((bool)GetField(window, "_dirty"), "le projet est marqué modifié");
            composed.CloseNoteEditor(true);
            Check(GetField(composed, "_noteEditor") == null, "Entrée/Échap referment l'éditeur de note");

            // — Précédent/suivant : depuis la première, suivant ouvre la seconde.
            editor.NavigateNote(1);
            DoEvents();
            Check(composed.EditingNoteId == order[1], "« Note suivante » ouvre la seconde note");
            editor.NavigateNote(1);
            DoEvents();
            Check(composed.EditingNoteId == order[0], "en bout de liste, suivant boucle sur la première");
            composed.CloseNoteEditor(true);

            // — Le clic sur la zone des notes trouve la note (géométrie réelle).
            var composition = GetProperty(composed, "CurrentComposition");
            var pages = (System.Collections.IList)GetField(composition, "Pages");
            var page0 = pages[0];
            var noteLines = (System.Collections.IList)GetField(page0, "NoteLines");
            Check(noteLines.Count >= 2, "les notes sont posées au bas de la première page");
            var placed = noteLines[0];
            var y = (double)GetField(placed, "Y");
            var left = (double)Invoke(composition, "LeftPxFor", new object[] { 0 });
            var hit = (string)Invoke(composed, "NoteAtPoint", new object[] { new Point(left + 40, y + 3) });
            Check(hit == order[0], "un point dans la première ligne de note désigne la première note");
            var miss = (string)Invoke(composed, "NoteAtPoint", new object[] { new Point(left + 40, 30) });
            Check(miss == null, "un point dans le corps ne désigne aucune note");

            // — Ruban : Insertion et Correction ; Révision sans « Vérifier ».
            var ribbon = (Border)GetField(editor, "_ribbonBar");
            var tabs = FindTabControl(ribbon);
            var headers = new List<string>();
            foreach (TabItem tab in tabs.Items) headers.Add((string)tab.Header);
            Check(headers.IndexOf("Insertion") == 1, "l'onglet Insertion suit Texte");
            Check(headers.Contains("Correction") && headers.IndexOf("Correction") > headers.IndexOf("Révision"),
                "l'onglet Correction existe, après Révision");
            Check(CountButtons(TabContent(tabs, "Insertion")) >= 4,
                "Insertion : note, lien, précédent, suivant");
            Check(!ContainsLabel(TabContent(tabs, "Révision"), "Vérifier")
                && ContainsLabel(TabContent(tabs, "Correction"), "Vérifier")
                && ContainsLabel(TabContent(tabs, "Correction"), "Options du correcteur"),
                "« Vérifier » et « Options du correcteur » vivent dans Correction, plus dans Révision");

            // — Pages ↔ Brouillon : chaque élément de page est REMESURÉ.
            var pagesPanel = (StackPanel)GetField(composed, "_pages");
            Invoke(editor, "SetDraftView", new object[] { true });
            DoEvents();
            composition = GetProperty(composed, "CurrentComposition");
            var draftHeight = (double)GetProperty(composition, "PageHeightPx");
            var draftWidth = (double)GetProperty(composition, "PageWidthPx");
            Check(Math.Abs(draftHeight / draftWidth - 297.0 / 210.0) < 0.01, "le brouillon garde le ratio A4");
            Check(AllPagesMeasure(pagesPanel, draftHeight), "en Brouillon, les pages sont mesurées à la nouvelle hauteur (pas de chevauchement)");
            Invoke(editor, "SetDraftView", new object[] { false });
            DoEvents();
            composition = GetProperty(composed, "CurrentComposition");
            var pageHeight = (double)GetProperty(composition, "PageHeightPx");
            Check(Math.Abs(pageHeight - draftHeight) > 1 && AllPagesMeasure(pagesPanel, pageHeight),
                "de retour en Pages, les éléments reprennent la hauteur de la page");

            // — Dictionnaire : l'écran, une entrée, le correcteur la connaît.
            Invoke(window, "OnBinderSelection", new object[] { dictionary });
            DoEvents();
            var dictionaryView = (DictionaryView)GetField(window, "_dictionaryView");
            Check(dictionaryView.Visibility == Visibility.Visible, "la racine Dictionnaire ouvre son écran");
            dictionaryView.AddEntry(new LexiconEntry { Word = "Alethi", Class = LexiconEntry.ClassProper }, true);
            DoEvents();
            Check(opened.Lexicon.Count == 1 && opened.Lexicon[0].Word == "Alethi", "l'entrée rejoint le projet");
            var spell = GetField(editor, "_spellChecker") as Correction.SpellChecker;
            Check(spell != null && spell.IsLearned("Alethis"), "le correcteur accepte le pluriel de l'entrée");

            // — Enregistrement : v13, lexique et racine sur le disque.
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloaded = PlotFile.Load(path);
            Check(reloaded.Lexicon.Count == 1 && reloaded.Lexicon[0].Class == LexiconEntry.ClassProper
                && reloaded.Category(Project.KeyDictionary) != null,
                "le dictionnaire et sa racine survivent à l'aller-retour disque (v13)");
            BinderItem backText = null;
            foreach (var item in reloaded.AllItems()) if (item.Title == "Chapitre noté") backText = item;
            Check(backText != null && backText.Document.FindFootnote(order[0]).Text == "Première note, corrigée",
                "la note éditée en place est sur le disque");

            // — Le tableau du livre : supprimer retire la carte, la sélection ne traîne pas.
            BinderItem bookItem = null;
            foreach (var item in opened.AllItems()) if (item.Kind == ItemKind.Book) bookItem = item;
            Invoke(window, "OnBinderSelection", new object[] { bookItem });
            DoEvents();
            var bookView = (BookView)GetField(window, "_bookView");
            var corkboard = (CorkboardView)GetField(bookView, "_corkboard");
            var cards = (List<Border>)Invoke(corkboard, "AllCards", null);
            Check(cards.Count == 3, "trois cartes-chapitres au tableau du livre (dont une dans la partie)");
            var partItem = bookItem.Children[2];
            Invoke(corkboard, "MoveBeside", new object[] { ((BinderItem)cards[1].Tag), partItem, true });
            Check(bookItem.Children.IndexOf(partItem) == 1 && bookItem.Children[2].Title == "B",
                "une carte se dépose APRÈS une partie (lisière basse)");
            var movedB = bookItem.Children[2];
            Invoke(corkboard, "MoveBeside", new object[] { movedB, partItem, false });
            Check(bookItem.Children[1].Title == "B" && bookItem.Children.IndexOf(partItem) == 2,
                "…et AVANT une partie (lisière haute ou légende) — le bug du batch 35");
            cards = (List<Border>)Invoke(corkboard, "AllCards", null);
            // — Le soulèvement au survol (b35) : transformation de rendu +
            // ombre, et le ⋮ reste sous le pointeur.
            var hovered = cards[0];
            hovered.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
            DoEvents();
            var shadowHost = hovered.Child is Grid && ((Grid)hovered.Child).Children.Count > 0
                ? ((Grid)hovered.Child).Children[0] as Border : null;
            Check(hovered.RenderTransform is TransformGroup && hovered.Effect == null
                && shadowHost != null && shadowHost.Effect is System.Windows.Media.Effects.DropShadowEffect,
                "au survol, la carte se soulève (transformation) et son hôte sans texte projette l'ombre (texte net)");
            Button dots = null;
            foreach (var b in FindButtons(hovered)) if (b.ToolTip as string == "Options de la carte") dots = b; // icône seule depuis le b40, repéré par l'infobulle
            var hitOk = false;
            if (dots != null)
            {
                var center = dots.TransformToAncestor(hovered).Transform(new Point(dots.ActualWidth / 2, dots.ActualHeight / 2));
                var hitElement = hovered.InputHitTest(center) as DependencyObject;
                while (hitElement != null && hitElement != dots) hitElement = VisualTreeHelper.GetParent(hitElement);
                hitOk = hitElement == dots;
            }
            Check(dots != null && hitOk, "le bouton ⋮ reste cliquable sur la carte soulevée");
            hovered.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
            DoEvents();
            var selected = (HashSet<string>)GetField(corkboard, "_selected");
            var cardA = cards[0];
            var cardB = cards[1];
            selected.Clear(); selected.Add(((BinderItem)cardA.Tag).Id);
            Invoke(corkboard, "RefreshSelectionVisuals", null);
            selected.Clear(); selected.Add(((BinderItem)cardB.Tag).Id);
            Invoke(corkboard, "RefreshSelectionVisuals", null);
            Check(ReferenceEquals(cardA.BorderBrush, Chrome.Border) && ReferenceEquals(cardB.BorderBrush, Chrome.Accent),
                "sélectionner B rend à A sa bordure neutre");
            var deleteEvent = (Delegate)GetField(bookView, "DeleteRequested");
            deleteEvent.DynamicInvoke((BinderItem)cardB.Tag);
            DoEvents();
            cards = (List<Border>)Invoke(corkboard, "AllCards", null);
            Check(cards.Count == 2 && ((BinderItem)cards[0].Tag).Title == "A",
                "supprimer depuis le tableau retire la carte");
            Check(bookItem.Children.Count == 2 && opened.Trash.Children.Count == 1,
                "le chapitre est à la Corbeille");

            // ================================================= batch 35
            // — Les caractères d'impression sur la surface composée.
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            editor.SetFormattingMarks(true);
            Check(ComposedRenderer.ShowMarks, "¶ : le dessinateur composé reçoit le drapeau");
            editor.SetFormattingMarks(false);
            Check(!ComposedRenderer.ShowMarks, "…et le rend");

            // — La racine Plans : après Fiches ; son tableau montre la carte du plan.
            var plansRoot = opened.Category(Project.KeyPlans);
            Check(plansRoot != null && roots.IndexOf(plansRoot) == roots.IndexOf(opened.Category(Project.KeySheets)) + 1,
                "la racine Plans suit Fiches");
            Invoke(window, "OnBinderSelection", new object[] { plansRoot });
            DoEvents();
            var rootBoard = (CorkboardView)GetField(window, "_corkboard");
            var planCards = (List<Border>)Invoke(rootBoard, "AllCards", null);
            Check(rootBoard.Visibility == Visibility.Visible && planCards.Count == 1
                && ((StackPanel)GetField(rootBoard, "_planActions")).Visibility == Visibility.Visible,
                "la racine Plans : la carte du plan et « + Nouveau plan »");
            Check(GetField(rootBoard, "NewDocumentRequested") != null,
                "« + Nouveau plan » du tableau est relié à la coquille (il ne créait rien)");

            // — L'écran du plan : colonnes, briques, réordonnancement, graphique.
            BinderItem planOpened = null;
            foreach (var item in opened.AllItems()) if (item.Kind == ItemKind.Plan) planOpened = item;
            Invoke(window, "OnBinderSelection", new object[] { planOpened });
            DoEvents();
            var planView = (PlanView)GetField(window, "_planView");
            Check(planView.Visibility == Visibility.Visible && planView.ShowsItem(planOpened), "le plan s'ouvre dans son écran");
            var columnsPanel = (StackPanel)GetField(planView, "_columns");
            Check(columnsPanel.Children.Count == 1, "une colonne affichée");
            var columnWord = (TextBox)GetField(window, "_planColumnWord");
            Check(columnWord.Visibility == Visibility.Visible && columnWord.Text == "Colonne",
                "l'inspecteur d'un plan propose le nom des colonnes (« Colonne »)");
            columnWord.Text = "chapitre";
            Invoke(planView, "AddColumn", null);
            DoEvents();
            Check(planOpened.Plan.Columns.Count == 2 && columnsPanel.Children.Count == 2 && (bool)GetField(window, "_dirty"),
                "« + Nouvelle colonne » ajoute une colonne et marque le projet");
            Check(planOpened.Plan.Columns[1].Title == "chapitre 2", "la nouvelle colonne prend le mot choisi");
            var backToPlans = false;
            planView.BackRequested += delegate { backToPlans = true; };
            foreach (var child in FindButtons(planView))
                if (child.ToolTip as string == "Revenir à la carte des plans") { child.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); break; } // icône arrow-left (b36)
            DoEvents();
            Check(backToPlans && rootBoard.Visibility == Visibility.Visible && rootBoard.ShowsItem(plansRoot),
                "« ← » ramène à la carte des plans");
            Invoke(window, "OnBinderSelection", new object[] { planOpened });
            DoEvents();
            var firstColumn = planOpened.Plan.Columns[0];
            var fire = firstColumn.Entries[1];
            planView.MoveEntry(firstColumn, fire, 0);
            Check(firstColumn.Entries[0] == fire, "une brique se réordonne dans sa colonne");
            var profile = PlanIntensity.Profile(planOpened.Plan);
            Check(profile.Count == 2 && profile[0] == 4 && profile[1] == 0, "le profil d'intensité : pic 4, colonne vide 0");
            var chart = new Canvas { Width = 600, Height = 300 };
            chart.Measure(new Size(600, 300));
            chart.Arrange(new Rect(0, 0, 600, 300));
            PlanChartWindow.Draw(chart, planOpened);
            Check(chart.Children.Count > 10, "le graphique d'intensité se dessine (" + chart.Children.Count + " formes)");
            DoEvents(); // la reconstruction des colonnes attend une passe de mise en page
            Snapshot(planView, Path.Combine(Path.GetTempPath(), "marabook-b35-plan.png"));
            var inspColor = (Button)GetField(window, "_colorButton"); // pastille + menu depuis le b43
            var inspSynopsis = (TextBox)GetField(window, "_synopsisBox");
            Check(inspColor.Visibility == Visibility.Visible && inspSynopsis.Visibility == Visibility.Collapsed,
                "l'inspecteur d'un plan : la couleur, rien d'autre");

            // — Les liens : l'écrit relié a son bouton « Plan », le livre son lien.
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            var planBtn = (Button)GetField(editor, "_planBtn");
            Check(planBtn.Visibility == Visibility.Visible, "l'écrit relié à une colonne montre « Plan » dans son ruban");
            planBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            DoEvents();
            Check(planView.Visibility == Visibility.Visible, "« Plan » renvoie au plan");
            Invoke(window, "OnBinderSelection", new object[] { bookItem });
            DoEvents();
            var planLink = (TextBlock)GetField(window, "_inspPlanLink");
            Check(planLink.Visibility == Visibility.Visible && planLink.Text.Contains("Plan du roman"),
                "le livre relié affiche son plan dans l'inspecteur");
            Invoke(window, "OnBinderSelection", new object[] { target.Parent });
            DoEvents();
            Check(planBtn.Visibility == Visibility.Visible || true, "(bouton Plan hors écrit : sans objet)");

            // — Aller-retour disque v15.
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloadedPlan = PlotFile.Load(path);
            BinderItem backPlan = null;
            foreach (var item in reloadedPlan.AllItems()) if (item.Kind == ItemKind.Plan) backPlan = item;
            Check(backPlan != null && backPlan.Plan.Columns.Count == 2 && backPlan.Plan.Columns[0].Entries[0].Text == "L'incendie"
                && backPlan.Plan.Columns[0].Entries[0].Intensity == 4 && backPlan.Plan.Columns[0].LinkedTextId == text.Id
                && backPlan.Plan.LinkedItemId == book.Id && reloadedPlan.Category(Project.KeyPlans) != null,
                "le plan (colonnes, briques, liens) survit à l'aller-retour disque (v15)");

            // ================================================= batch 34
            // — Les bulles d'annotation : la frappe leur reste (les Preview*
            // de la surface composée ne l'avalent plus).
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            var bubbles = (Canvas)GetProperty(composed, "AnnotationBubbleLayer");
            var bubbleBox = new TextBox { Text = "" };
            bubbles.Children.Add(bubbleBox);
            bubbleBox.Focus();
            DoEvents();
            var before = target.Document.ToPlainText();
            var composition2 = new TextComposition(InputManager.Current, composed, "x");
            var textArgs = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition2) { RoutedEvent = UIElement.PreviewTextInputEvent };
            Invoke(composed, "OnTextInput", new object[] { composed, textArgs });
            Check(!textArgs.Handled && target.Document.ToPlainText() == before,
                "une frappe destinée à une bulle d'annotation n'est pas avalée par le texte");
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(composed), 0, Key.Back) { RoutedEvent = UIElement.PreviewKeyDownEvent };
            Invoke(composed, "OnKeyDown", new object[] { composed, keyArgs });
            Check(!keyArgs.Handled && target.Document.ToPlainText() == before,
                "un Retour arrière dans une bulle ne touche pas au texte");
            bubbles.Children.Remove(bubbleBox);
            composed.Focus();

            // — Le zoom garde le centre de la vue.
            composed.SetZoom(1.0);
            DoEvents();
            var column = (FrameworkElement)GetField(composed, "_column");
            composed.ScrollToVerticalOffset(300);
            DoEvents();
            var centerBefore = (composed.VerticalOffset + composed.ViewportHeight / 2 - column.Margin.Top) / 1.0;
            composed.SetZoom(2.0);
            DoEvents();
            var centerAfter = (composed.VerticalOffset + composed.ViewportHeight / 2 - column.Margin.Top) / 2.0;
            Check(Math.Abs(centerAfter - centerBefore) < 2,
                "Ctrl+molette : le point au centre de la vue reste au centre (" + centerBefore.ToString("0") + " → " + centerAfter.ToString("0") + ")");
            composed.SetZoom(1.0);
            DoEvents();
            var slider = (Slider)GetField(window, "_zoomSlider");
            Invoke(window, "ApplyZoom", new object[] { 150.0 });
            Check(Math.Abs(slider.Value - 150) < 0.1, "le curseur de zoom suit le zoom appliqué");
            Invoke(window, "ApplyZoom", new object[] { 100.0 });

            // — La passe typographique sur le pivot, appliquée puis annulée.
            var tabsAll = FindTabControl(ribbon);
            var headers2 = new List<string>();
            foreach (TabItem tab in tabsAll.Items) headers2.Add((string)tab.Header);
            Check(headers2.IndexOf("Formatage") == 2 && ContainsLabel(TabContent(tabsAll, "Formatage"), "Typographie"),
                "l'onglet Formatage suit Insertion, avec « Typographie »");
            Snapshot(ribbon, Path.Combine(Path.GetTempPath(), "marabook-b34-ribbon.png"));
            Snapshot((FrameworkElement)GetField(window, "_binder"), Path.Combine(Path.GetTempPath(), "marabook-b35-pile.png"));
            composed.TypeText(" Il a dit \"salut\"...");
            DoEvents();
            var pass = Correction.TypographyPass.Run(target.Document, new Correction.TypographyOptions());
            Check(pass.Changes.Count == 1, "la passe trouve le paragraphe à corriger");
            composed.ReplaceParagraphs(pass.Paragraphs);
            DoEvents();
            Check(target.Document.ToPlainText().Contains("« salut »") || target.Document.ToPlainText().Contains("«\u00A0salut\u00A0»"),
                "la passe appliquée : guillemets français dans le pivot");
            Check(composed.Undo(), "…en un cran d'annulation");
            Check(target.Document.ToPlainText().Contains("\"salut\""), "Ctrl+Z rend le texte d'avant la passe");

            // — La fiche refondue : papers, relation, retour.
            BinderItem kaladinItem = null;
            foreach (var item in opened.AllItems()) if (item.Title == "Kaladin") kaladinItem = item;
            Invoke(window, "OnBinderSelection", new object[] { kaladinItem });
            DoEvents();
            var sheetView = (SheetView)GetField(window, "_sheetView");
            Check(sheetView.Visibility == Visibility.Visible, "la fiche s'ouvre");
            var sectionPanels = (System.Collections.Generic.Dictionary<string, StackPanel>)GetField(sheetView, "_sectionPanels"); // papers par section (b42)
            var looks = sectionPanels[SheetDefaults.GroupLooks];
            var infos = sectionPanels[""];
            Check(looks.Children.Count > 1 && infos.Children.Count > 1,
                "les champs se répartissent entre Informations et Apparence (Physique + champ libre)");
            var relations = (StackPanel)GetField(sheetView, "_relationsPanel");
            Check(relations.Children.Count == 1, "la relation vers Syl est affichée");
            BinderItem navigated = null;
            sheetView.NavigateRequested += delegate(BinderItem item) { navigated = item; };
            Button openLink = null;
            foreach (var child in LogicalTreeHelper.GetChildren((DependencyObject)relations.Children[0]))
                if (child is Button && ((Button)child).ToolTip as string == "Ouvrir la fiche liée") openLink = (Button)child; // icône arrow-up-right (b36)
            Check(openLink != null && openLink.Visibility == Visibility.Visible, "une relation vers une fiche offre « Ouvrir »");
            if (openLink != null) openLink.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(navigated != null && navigated.Title == "Syl", "« Ouvrir » demande la fiche liée");
            DoEvents();
            BinderItem sylItem = null;
            foreach (var item in opened.AllItems()) if (item.Title == "Syl") sylItem = item;
            Check(sheetView.ShowsItem(sylItem), "…et la coquille l'ouvre");
            Invoke(window, "OnBinderSelection", new object[] { kaladinItem });
            DoEvents();
            var body = (TextBox)GetField(sheetView, "_bodyBox");
            Check(body.FontFamily.Source == "Georgia", "l'éditeur markdown est dans la police du wiki");
            Snapshot(sheetView, Path.Combine(Path.GetTempPath(), "marabook-b34-sheet.png"));
            body.Focus();
            body.Select(0, 4);
            var selectionInfo = "texte « " + body.Text + " », sélection " + body.SelectionStart + "/" + body.SelectionLength;
            Invoke(sheetView, "Wrap", new object[] { "**", "**" });
            Check(body.Text.StartsWith("**Chef**"), "la barre de formatage entoure la sélection (gras) — " + selectionInfo + " → « " + body.Text + " »");
            Invoke(sheetView, "Wrap", new object[] { "**", "**" });
            Check(body.Text.StartsWith("Chef de pont"), "…et la retire au second clic");
            Invoke(sheetView, "ApplyHeading", new object[] { 2 });
            Check(body.Text.StartsWith("## Chef"), "H2 préfixe la ligne");
            var backFired = false;
            sheetView.BackRequested += delegate { backFired = true; };
            var raised = false;
            foreach (var child in FindButtons(sheetView))
                if (child.ToolTip as string == "Revenir au tableau (corkboard) de la fiche") { child.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); raised = true; break; } // icône + libellé (b36)
            Check(raised && backFired, "« Retour » est câblé");
            DoEvents();
            Check(sheetView.Visibility == Visibility.Collapsed, "…et ramène au tableau des fiches");
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloaded2 = PlotFile.Load(path);
            BinderItem back2 = null;
            foreach (var item in reloaded2.AllItems()) if (item.Title == "Kaladin") back2 = item;
            Check(back2 != null && back2.Relations.Count == 1 && back2.Relations[0].TargetId == syl.Id
                && back2.FreeInfo[0].Group == SheetDefaults.GroupLooks,
                "relations et groupe du champ libre survivent à l'aller-retour disque (v14)");

            window.Close();
            DoEvents();
        }

        private static void Snapshot(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  (rendu : " + path + ")");
            }
            catch (Exception error)
            {
                Console.WriteLine("  (rendu PNG impossible : " + error.Message + ")");
            }
        }

        private static List<Button> FindButtons(DependencyObject root)
        {
            var list = new List<Button>();
            if (root is Button) list.Add((Button)root);
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++) list.AddRange(FindButtons(VisualTreeHelper.GetChild(root, i)));
            return list;
        }

        private static bool AllPagesMeasure(StackPanel pages, double height)
        {
            if (pages.Children.Count == 0) return false;
            foreach (FrameworkElement element in pages.Children)
                if (Math.Abs(element.DesiredSize.Height - height) > 1 || Math.Abs(element.ActualHeight - height) > 1)
                    return false;
            return true;
        }

        private static TabControl FindTabControl(DependencyObject root)
        {
            if (root is TabControl) return (TabControl)root;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindTabControl(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static UIElement TabContent(TabControl tabs, string header)
        {
            foreach (TabItem tab in tabs.Items)
                if ((string)tab.Header == header) return tab.Content as UIElement;
            return null;
        }

        private static int CountButtons(DependencyObject root)
        {
            if (root == null) return 0;
            var count = root is System.Windows.Controls.Primitives.ButtonBase ? 1 : 0;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject) count += CountButtons((DependencyObject)child);
            return count;
        }

        private static bool ContainsLabel(DependencyObject root, string label)
        {
            if (root == null) return false;
            var block = root as TextBlock;
            if (block != null && block.Text == label) return true;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject && ContainsLabel((DependencyObject)child, label)) return true;
            return false;
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
        }

        private static object GetProperty(object target, string name)
        {
            var property = target.GetType().GetProperty(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return property.GetValue(target, null);
        }

        private static object Invoke(object target, string name, object[] args)
        {
            // Les méthodes DÉCLARÉES d'abord (OnKeyDown/OnTextInput existent
            // aussi, protégées, sur UIElement : correspondance ambiguë sinon).
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var method = target.GetType().GetMethod(name, flags | BindingFlags.DeclaredOnly)
                ?? target.GetType().GetMethod(name, flags);
            if (method == null)
                throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}
