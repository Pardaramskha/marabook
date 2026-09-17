using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde du batch 37 — la recherche projet, sur vraie
    /// MainWindow hors écran : le panneau remplace l'inspecteur, les
    /// occurrences sont groupées par item, cliquer une occurrence ouvre
    /// l'item et sélectionne l'EMPAN EXACT dans la surface composée,
    /// suivant / précédent traversent les items, une occurrence de fiche
    /// ouvre l'onglet et le champ. Règle du batch 11 : settings.json est
    /// l'affaire de l'appelant (A1Probe).</summary>
    public static class SearchProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b37");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b37.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE RECHERCHE EN ÉCHEC : " + error);
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
            // — Un écrit nu, un livre avec un chapitre, une fiche : six
            // occurrences de « marabout » dans trois items, ordre de la Pile.
            var project = Project.CreateNew();
            var first = project.Category(Project.KeyWritings).Children[0];
            first.Title = "Premier";
            first.Document = TextDocument.FromPlainText("Le marabout dort.\nUn marabout et un marabout.");
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo() };
            var second = new BinderItem { Kind = ItemKind.Text, Title = "Second", Page = book.Book.Template.Clone() };
            second.Document = TextDocument.FromPlainText("Le marais du marabout.");
            book.Children.Add(second);
            project.Category(Project.KeyWritings).Children.Add(book);
            var character = project.SheetCategories[0];
            var template = project.FindTemplate(character.TemplateId);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche", CategoryId = character.Id, TemplateId = character.TemplateId };
            sheet.Document = TextDocument.FromPlainText("Un marabout.");
            sheet.FieldValues[template.Fields[0].Id] = "Marabout cendré";
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.RightPanel = RightPanel.Inspector;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Theme.Apply(application);
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
            BinderItem premier = null, secondItem = null, fiche = null;
            foreach (var item in opened.AllItems())
            {
                if (item.Title == "Premier") premier = item;
                if (item.Title == "Second") secondItem = item;
                if (item.Title == "Fiche") fiche = item;
            }

            // — Les raccourcis F3 / Maj+F3 sont posés sur la fenêtre.
            var f3 = false;
            var shiftF3 = false;
            foreach (InputBinding binding in window.InputBindings)
            {
                var key = binding as KeyBinding;
                if (key == null || key.Key != Key.F3) continue;
                if (key.Modifiers == ModifierKeys.None) f3 = true;
                if (key.Modifiers == ModifierKeys.Shift) shiftF3 = true;
            }
            Check(f3 && shiftF3, "F3 et Maj+F3 sont câblés (occurrence suivante / précédente)");

            // — Ouvrir le panneau : il remplace l'inspecteur dans la colonne de droite.
            Invoke(window, "OpenSearchPanel", null);
            DoEvents();
            var host = (Border)GetField(window, "_searchHost");
            var inspector = (Border)GetField(window, "_inspector");
            Check(host.Visibility == Visibility.Visible && inspector.Visibility != Visibility.Visible,
                "le panneau de recherche s'ouvre à droite, devant l'inspecteur");
            Check(AppSettings.RightPanel == RightPanel.Search, "…et le réglage s'en souvient");

            // — La requête : six occurrences dans trois items, groupées.
            var panel = (SearchPanel)GetField(window, "_searchPanel");
            panel.QueryText = "marabout";
            panel.RunNow();
            DoEvents();
            var result = panel.Result;
            Check(result != null && result.Total == 6 && result.ItemCount == 3,
                "six occurrences dans trois items (obtenu : " + (result == null ? "rien" : result.Total + " / " + result.ItemCount) + ")");
            var list = (StackPanel)GetField(panel, "_list");
            int headers = 0, rows = 0;
            foreach (var child in list.Children)
            {
                if (child is DockPanel) headers++;
                else if (child is Border) rows++;
            }
            Check(headers == 3 && rows == 6, "trois groupes d'item, six lignes d'occurrence (obtenu : " + headers + " / " + rows + ")");
            var summary = (TextBlock)GetField(panel, "_summary");
            Check(summary.Text.StartsWith("6 occurrences dans 3 items"), "le récapitulatif est honnête (« " + summary.Text + " »)");

            // — Clic = navigation : l'item s'ouvre, l'empan exact est sélectionné dans le composé.
            var editor = (EditorView)GetField(window, "_editor");
            var composed = (ComposedView)GetField(editor, "_composed");
            panel.Select(0, true);
            DoEvents();
            Snapshot(host, Path.Combine(Path.GetTempPath(), "marabook-b37-recherche.png"));
            Check(GetField(window, "_current") == premier, "la première occurrence ouvre « Premier »");
            Check(Selection(composed) == "0:3-0:11", "…et sélectionne « marabout » (¶ 1, 3-11) dans la surface composée (obtenu : " + Selection(composed) + ")");

            // — Suivant / précédent, à travers les items.
            panel.Next();
            DoEvents();
            Check(Selection(composed) == "1:3-1:11", "suivant : ¶ 2, premier « marabout »");
            panel.Next();
            DoEvents();
            Check(Selection(composed) == "1:18-1:26", "suivant : ¶ 2, second « marabout » (empans disjoints)");
            panel.Next();
            DoEvents();
            Check(GetField(window, "_current") == secondItem && Selection(composed) == "0:13-0:21",
                "suivant : change d'item (« Second ») et sélectionne l'empan (obtenu : " + Selection(composed) + ")");
            panel.Previous();
            DoEvents();
            Check(GetField(window, "_current") == premier && Selection(composed) == "1:18-1:26", "précédent : revient sur « Premier », dernier empan");
            Check(panel.CurrentIndex == 2, "la ligne courante suit (index 2)");

            // — Une occurrence de fiche : le corps (onglet Texte libre) puis un champ (onglet Général).
            var sheetView = (SheetView)GetField(window, "_sheetView");
            var tabs = (TabControl)GetField(sheetView, "_tabs");
            panel.Select(4, true);
            DoEvents();
            var body = (TextBox)GetField(sheetView, "_bodyBox");
            Check(GetField(window, "_current") == fiche && tabs.SelectedIndex == 1 && body.SelectionStart == 3 && body.SelectionLength == 8,
                "une occurrence du corps de fiche ouvre Texte libre et sélectionne l'empan (obtenu : onglet " + tabs.SelectedIndex + ", " + body.SelectionStart + "+" + body.SelectionLength + ")");
            panel.Select(5, true);
            DoEvents();
            var focused = Keyboard.FocusedElement as TextBox;
            Check(tabs.SelectedIndex == 0 && focused != null && focused.Text == "Marabout cendré" && focused.SelectionStart == 0 && focused.SelectionLength == 8,
                "une occurrence de champ ouvre Général et sélectionne dans le champ « Nom »");

            // — Suivant boucle au début : retour sur « Premier ».
            panel.Next();
            DoEvents();
            Check(GetField(window, "_current") == premier && panel.CurrentIndex == 0, "après la dernière, suivant reprend à la première");

            // — Fermer le panneau rend l'inspecteur.
            Invoke(panel, "OnCloseRequested", null);
            DoEvents();
            Check(host.Visibility != Visibility.Visible && inspector.Visibility == Visibility.Visible && AppSettings.RightPanel != RightPanel.Search,
                "fermer le panneau rend l'inspecteur et oublie le réglage");

            window.Close();
            DoEvents();

            ReplaceProbe(Path.Combine(Path.GetDirectoryName(path), "b37-remplacer.plot"));
        }

        /// <summary>Lot C — le remplacement projet : plusieurs dizaines
        /// d'items dont un OUVERT (avec un cran d'annulation local), la
        /// prévisualisation, le remplacement en une action, l'annulation
        /// intégrale d'un seul Ctrl+Z (tous les items à l'identique), le
        /// rétablissement.</summary>
        private static void ReplaceProbe(string path)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo { Subtitle = "Le marabout des marais" } };
            for (var i = 1; i <= 30; i++)
            {
                var chapter = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre " + i, Page = book.Book.Template.Clone(), Synopsis = "Le marabout " + i };
                chapter.Document = TextDocument.FromPlainText("Le marabout du chapitre " + i + ".\nEncore un marabout, puis un marabout.");
                var note = new Footnote { Text = "Note sur le marabout " + i };
                chapter.Document.Footnotes.Add(note);
                chapter.Document.Paragraphs[0].Runs.Add(new TextRun { FootnoteId = note.Id }); // ancrée : une note orpheline serait purgée au commit
                book.Children.Add(chapter);
            }
            writings.Children.Add(book);
            var character = project.SheetCategories[0];
            var template = project.FindTemplate(character.TemplateId);
            for (var i = 1; i <= 6; i++)
            {
                var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche " + i, CategoryId = character.Id, TemplateId = character.TemplateId };
                sheet.Document = TextDocument.FromPlainText("Un marabout de fiche.");
                sheet.FieldValues[template.Fields[0].Id] = "Marabout " + i;
                sheet.FreeInfo.Add(new InfoEntry { Title = "Devise", Value = "Plumer le marabout" });
                project.Category(Project.KeySheets).Children.Add(sheet);
            }
            var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            var column = new PlanColumn { Title = "Acte du marabout" };
            column.Entries.Add(new PlanEntry { Text = "Le marabout s'envole" });
            plan.Plan.Columns.Add(column);
            project.Category(Project.KeyPlans).Children.Add(plan);
            project.Lexicon.Add(new LexiconEntry { Word = "marabouter", Note = "du marabout" });
            project.RelinkParents();
            PlotFile.Save(project, path);
            var reference = PlotFile.Load(path); // l'état AVANT, rechargé du disque (indépendant)

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
            var total = 0;
            foreach (var item in opened.AllItems()) if (!item.IsCategory) total++;
            Check(total >= 38, "un projet de plusieurs dizaines d'items (" + total + ")");

            // — Ouvrir le chapitre 7 et y taper : un cran d'annulation LOCAL.
            BinderItem chapter7 = null;
            foreach (var item in opened.AllItems()) if (item.Title == "Chapitre 7") chapter7 = item;
            Invoke(window, "OnBinderSelection", new object[] { chapter7 });
            DoEvents();
            var editor = (EditorView)GetField(window, "_editor");
            var composed = (ComposedView)GetField(editor, "_composed");
            composed.Focus();
            var composition = new TextComposition(InputManager.Current, composed, "X");
            var textArgs = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = UIElement.PreviewTextInputEvent };
            Invoke(composed, "OnTextInput", new object[] { composed, textArgs }); // la frappe, comme EditorProbe
            DoEvents();
            var localUndo = (System.Collections.IList)GetField(composed, "_undo");
            Check(chapter7.Document.Paragraphs[0].Runs[0].Text.StartsWith("X") && localUndo.Count == 1,
                "une frappe dans le chapitre ouvert laisse un cran d'annulation local (obtenu : " + localUndo.Count + ")");

            // — Le panneau, la requête, la PRÉVISUALISATION.
            Invoke(window, "OpenSearchPanel", null);
            var panel = (SearchPanel)GetField(window, "_searchPanel");
            panel.QueryText = "marabout";
            panel.ReplaceText = "ibis";
            panel.RunNow();
            DoEvents();
            var query = panel.Query();
            var targets = ProjectSearch.Collect(opened, SearchScope.Project, chapter7, SearchKind.All, false);
            var full = ProjectSearch.Run(targets, query, int.MaxValue, TimeSpan.FromSeconds(30), System.Threading.CancellationToken.None);
            // 30 chapitres × (synopsis + ¶1 + 2 en ¶2 + note) + 6 fiches × (corps + champ + info) + colonne + brique + mot + note + sous-titre.
            Check(full.Total == 30 * 5 + 6 * 3 + 2 + 2 + 1 && !full.Capped && full.Hits.Count == full.Total,
                "toutes les occurrences comptées sans plafond (obtenu : " + full.Total + ")");
            var preview = new ReplacePreviewDialog(window, full, query, "ibis", chapter7);
            Check(preview.CheckedItems == full.ItemCount && preview.Summary == "Remplacer " + full.Total + " occurrences dans " + full.ItemCount + " items.",
                "la prévisualisation compte : « " + preview.Summary + " », " + preview.CheckedItems + " items cochés");
            Check(preview.WarnsCurrent, "…et prévient que l'historique du document ouvert sera réinitialisé");
            var checks = (System.Collections.Generic.List<CheckBox>)GetField(preview, "_checks");
            checks[checks.Count - 1].IsChecked = false; // épargner le dernier item (le dictionnaire)
            Check(preview.CheckedItems == full.ItemCount - 1, "décocher un item l'épargne");
            var chosen = (System.Collections.Generic.List<SearchHit>)Invoke2(preview, "Chosen");
            preview.Close();
            Check(chosen.Count == full.Total - 2, "les occurrences retenues excluent l'item décoché (obtenu : " + chosen.Count + ")");

            // — Le remplacement, en UNE action.
            var history = (Marabook.History.HistoryManager)GetField(window, "_history");
            var before = history.Count;
            Invoke(window, "RunReplace", new object[] { ReplacePlan.Build(opened, chosen, query, "ibis"), "marabout" });
            DoEvents();
            Check(history.Count == before + 1, "une seule action d'historique pour tout le projet");
            // 30 chapitres ont un document ; les fiches n'ont plus de versions
            // (b43) ; le plan et le dictionnaire, pas de document ; le livre non plus.
            Check(SnapshotStore.Count(opened, null) == 30 && SnapshotStore.Latest(opened, chapter7.Id).Origin == SnapshotOrigin.Replace
                && SnapshotStore.Latest(opened, chapter7.Id).Label == "Avant remplacement de « marabout »",
                "un instantané automatique « avant remplacement » par écrit touché (b38/b43 ; obtenu : " + SnapshotStore.Count(opened, null) + ")");
            var remaining = ProjectSearch.Run(ProjectSearch.Collect(opened, SearchScope.Project, null, SearchKind.All, false), query, int.MaxValue, TimeSpan.FromSeconds(30), System.Threading.CancellationToken.None);
            Check(remaining.Total == 2 && remaining.Hits[0].Item.IsCategory, "il ne reste que les deux occurrences du dictionnaire épargné (obtenu : " + remaining.Total + ")");
            Check(chapter7.Document.Paragraphs[0].Runs[0].Text.StartsWith("XLe ibis"), "le chapitre ouvert est remplacé, la frappe conservée");
            composed = (ComposedView)GetField(editor, "_composed");
            localUndo = (System.Collections.IList)GetField(composed, "_undo");
            Check(localUndo.Count == 0 && GetField(window, "_current") == chapter7, "sa vue a été rechargée : pile locale VIDÉE (obtenu : " + localUndo.Count + ")");
            var notice = (TextBlock)GetField(panel, "_notice");
            Check(notice.Text.StartsWith("Remplacement : " + chosen.Count + " occurrences"), "le panneau dit ce qui s'est passé (« " + notice.Text + " »)");

            // — UN Ctrl+Z (DoUndo) : tout revient à l'identique, item ouvert compris.
            Invoke(window, "DoUndo", null);
            DoEvents();
            var diffs = new System.Collections.Generic.List<string>();
            foreach (var item in ProjectSearch.PileOrder(opened))
            {
                if (item.IsCategory) continue;
                var expected = reference.FindById(item.Id);
                var expectedText = expected == null ? null : expected.SearchText();
                var actual = item == chapter7 ? item.SearchText().Replace("XLe marabout", "Le marabout") : item.SearchText();
                if (expectedText != actual) diffs.Add(item.Title);
            }
            Check(diffs.Count == 0, "après UN Ctrl+Z, tous les items sont revenus à l'identique" + (diffs.Count > 0 ? " — sauf : " + string.Join(", ", diffs.ToArray()) : ""));
            Check(chapter7.Document.Paragraphs[0].Runs[0].Text.StartsWith("XLe marabout"), "…la frappe d'avant le remplacement est toujours là");
            Check(opened.Lexicon[0].Word == "marabouter" && opened.Category(Project.KeyWritings).Children[0].Book.Subtitle == "Le marabout des marais", "…dictionnaire et métadonnées de livre compris");
            Check(notice.Text.StartsWith("Remplacement annulé"), "le panneau annonce l'annulation");

            // — Rétablir rejoue tout.
            Invoke(window, "DoRedo", null);
            DoEvents();
            remaining = ProjectSearch.Run(ProjectSearch.Collect(opened, SearchScope.Project, null, SearchKind.All, false), query, int.MaxValue, TimeSpan.FromSeconds(30), System.Threading.CancellationToken.None);
            Check(remaining.Total == 2, "Ctrl+Y rejoue le remplacement entier");
            Invoke(window, "DoUndo", null);
            DoEvents();

            // — Remplacer UNE occurrence du document ouvert : un cran local.
            panel.RunNow();
            DoEvents();
            SearchHit inOpen = null;
            for (var i = 0; i < panel.Result.Hits.Count; i++)
                if (panel.Result.Hits[i].Item == chapter7 && panel.Result.Hits[i].Field.IsParagraph) { panel.Select(i, true); inOpen = panel.Result.Hits[i]; break; }
            DoEvents();
            var historyBefore = history.Count;
            Invoke(window, "ReplaceOne", new object[] { inOpen, "ibis" });
            DoEvents();
            composed = (ComposedView)GetField(editor, "_composed");
            localUndo = (System.Collections.IList)GetField(composed, "_undo");
            Check(chapter7.Document.Paragraphs[0].Runs[0].Text.Contains("ibis") && history.Count == historyBefore && localUndo.Count >= 1,
                "remplacer une occurrence du document ouvert passe par la pile LOCALE (pas d'action projet)");

            Invoke(window, "DoSave", null); // un projet modifié demanderait sinon à enregistrer (modale)
            DoEvents();
            window.Close();
            DoEvents();
        }

        private static object Invoke2(object target, string name)
        {
            var method = target.GetType().GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null)
                throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return method.Invoke(target, null);
        }

        /// <summary>Rendu PNG d'un élément (VisualBrush à l'origine — piège b32).</summary>
        private static void Snapshot(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(element), null, new Rect(0, 0, width, height));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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

        private static string Selection(ComposedView composed)
        {
            return GetField(composed, "_anchorParagraph") + ":" + GetField(composed, "_anchorOffset")
                + "-" + GetField(composed, "_caretParagraph") + ":" + GetField(composed, "_caretOffset");
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            method.Invoke(target, args);
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
