using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
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
            AppSettings.ClassicCompatibility = false;
            AppSettings.SearchPanelVisible = false;
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
            Check(AppSettings.SearchPanelVisible, "…et le réglage s'en souvient");

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
            Check(host.Visibility != Visibility.Visible && inspector.Visibility == Visibility.Visible && !AppSettings.SearchPanelVisible,
                "fermer le panneau rend l'inspecteur et oublie le réglage");

            window.Close();
            DoEvents();
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
