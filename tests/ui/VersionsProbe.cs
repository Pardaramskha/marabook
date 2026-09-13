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
    /// <summary>Sonde du batch 38 — les versions d'écrits, sur vraie
    /// MainWindow hors écran : le panneau Versions dans la colonne de droite,
    /// la prise d'un instantané, la frappe, la comparaison rendue par la
    /// surface composée (lecture seule, rendu PNG), la restauration
    /// (précédée d'un instantané automatique, avertissement du document
    /// ouvert) puis Ctrl+Z. Règle du batch 11 : settings.json est l'affaire
    /// de l'appelant (A1Probe).</summary>
    public static class VersionsProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b38");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b38.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE VERSIONS EN ÉCHEC : " + error);
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
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Title = "Chapitre versionné";
            chapter.Document = TextDocument.FromPlainText("Le marabout dort.\nUn marabout veille sur le marais.\nLa nuit tombe.");
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.RightPanel = RightPanel.Inspector;
            AppSettings.DailySnapshot = false; // la quotidienne est testée en console ; ici, des comptes exacts
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
            BinderItem item = null;
            foreach (var candidate in opened.AllItems()) if (candidate.Title == "Chapitre versionné") item = candidate;
            Invoke(window, "OnBinderSelection", new object[] { item });
            DoEvents();

            // — Le panneau Versions s'ouvre dans la colonne de droite, devant l'inspecteur.
            Invoke(window, "OpenVersionsPanel", null);
            DoEvents();
            var host = (Border)GetField(window, "_versionsHost");
            var inspector = (Border)GetField(window, "_inspector");
            Check(host.Visibility == Visibility.Visible && inspector.Visibility != Visibility.Visible && AppSettings.RightPanel == RightPanel.Versions,
                "le panneau Versions s'ouvre à droite, devant l'inspecteur, et le réglage s'en souvient");
            var panel = (VersionsPanel)GetField(window, "_versionsPanel");
            var hint = (TextBlock)GetField(panel, "_hint");
            Check(hint.Text.StartsWith("Aucune version"), "sans instantané, le panneau le dit");

            // — Prendre un instantané : une rangée, le poids, le projet marqué modifié.
            var snapshot = panel.TakeSnapshot("Départ");
            DoEvents();
            Check(snapshot != null && opened.Snapshots.Count == 1 && panel.Shown.Count == 1, "prendre un instantané l'ajoute au projet et à la liste");
            var weight = (TextBlock)GetField(panel, "_weight");
            Check(weight.Text.StartsWith("1 version · ") && weight.Text.Contains("projet : 1 version"), "le poids est visible (« " + weight.Text + " »)");
            Check((bool)GetField(window, "_dirty"), "…et le projet est marqué modifié");
            Check(panel.TakeSnapshot("Doublon") == null && opened.Snapshots.Count == 1, "reprendre sans changement ne crée rien");

            // — Écrire dessus : le composé, une frappe.
            var editor = (EditorView)GetField(window, "_editor");
            var composed = (ComposedView)GetField(editor, "_composed");
            composed.Focus();
            var composition = new TextComposition(InputManager.Current, composed, "X");
            var textArgs = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = UIElement.PreviewTextInputEvent };
            Invoke(composed, "OnTextInput", new object[] { composed, textArgs });
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[0]).StartsWith("XLe marabout"), "la frappe est dans le document");
            panel.Refresh();
            var rows = (StackPanel)GetField(panel, "_list");
            Check(rows.Children.Count == 1, "la liste montre la version");

            // — Comparer à l'état actuel : le paper flottant, rendu par la surface composée, lecture seule.
            Invoke(window, "CompareSnapshots", new object[] { snapshot, null });
            DoEvents();
            var compare = (CompareWindow)GetField(window, "_compareWindow");
            Check(compare != null && compare.IsVisible && compare.Shows(item), "la comparaison s'ouvre dans un paper flottant");
            Check(compare.Delta != null && compare.Delta.Modified == 1 && compare.Delta.Unchanged == 2, "le diff : un paragraphe modifié, deux inchangés (« " + compare.Delta.Summary() + " »)");
            var surface = compare.Surface;
            Check(surface.ReadOnly && surface.HasItem, "la surface composée est en lecture seule et porte le document synthétique");
            var synthetic = (BinderItem)GetField(surface, "_item");
            Check(synthetic != item && opened.FindById(synthetic.Id) == null && !ReferenceEquals(synthetic.Document, item.Document),
                "le porteur est synthétique : hors du projet, jamais le document de l'item");
            var underlined = 0;
            foreach (var run in synthetic.Document.Paragraphs[0].Runs) if (run.Underline == true && run.Text == "X") underlined++;
            Check(underlined == 1, "l'ajout est souligné dans le rendu");
            var before = PivotEdit.FlatText(synthetic.Document.Paragraphs[0]);
            var typing = new TextComposition(InputManager.Current, surface, "Z");
            Invoke(surface, "OnTextInput", new object[] { surface, new TextCompositionEventArgs(Keyboard.PrimaryDevice, typing) { RoutedEvent = UIElement.PreviewTextInputEvent } });
            DoEvents();
            Check(PivotEdit.FlatText(synthetic.Document.Paragraphs[0]) == before && PivotEdit.FlatText(item.Document.Paragraphs[0]).StartsWith("XLe"),
                "taper dans la comparaison ne change ni le rendu ni l'item (lecture seule)");
            surface.UpdateLayout();
            DoEvents();
            var pages = (Panel)GetField(surface, "_pages");
            var pageInfo = pages.Children.Count + " page(s)";
            if (pages.Children.Count > 0)
            {
                var page = pages.Children[0] as FrameworkElement;
                pageInfo += ", 1re : " + (page == null ? "?" : page.GetType().Name + " " + Math.Round(page.ActualWidth) + "×" + Math.Round(page.ActualHeight));
            }
            Check(pages.Children.Count > 0, "la surface composée a composé des pages (" + pageInfo + ", surface " + Math.Round(surface.ActualWidth) + "×" + Math.Round(surface.ActualHeight) + ")");
            Snapshot((FrameworkElement)compare.Content, Path.Combine(Path.GetTempPath(), "marabook-b38-comparaison.png"));
            Snapshot(composed, Path.Combine(Path.GetTempPath(), "marabook-b38-editeur.png"));
            var position = (TextBlock)GetField(compare, "_position");
            Check(position.Text == "Différence 1 / 1", "la navigation compte les différences (« " + position.Text + " »)");

            // — L'avertissement du document ouvert, puis la restauration (sans modale), annulable.
            var warning = (string)Invoke2(window, "RestoreWarning", new object[] { item, snapshot });
            Check(warning.Contains("historique d'annulation") && warning.Contains("Départ"), "l'avertissement du document ouvert est prêt (« " + warning + " »)");
            var history = (UniversSale.History.HistoryManager)GetField(window, "_history");
            var historyBefore = history.Count;
            Invoke(window, "RestoreSnapshot", new object[] { snapshot, false });
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[0]) == "Le marabout dort.", "restaurer remet le document dans l'état de l'instantané");
            Check(opened.Snapshots.Count == 2 && SnapshotStore.Latest(opened, item.Id).Origin == SnapshotOrigin.Restore,
                "…après un instantané automatique « avant restauration » (obtenu : " + opened.Snapshots.Count + ")");
            Check(history.Count == historyBefore + 1, "…en une action d'historique");
            composed = (ComposedView)GetField(editor, "_composed");
            var localUndo = (System.Collections.IList)GetField(composed, "_undo");
            Check(localUndo.Count == 0 && GetField(window, "_current") == item, "la vue du document ouvert a été rechargée : pile locale VIDÉE");
            Check(compare.Delta != null && compare.Delta.IsIdentical, "la comparaison ouverte s'est rafraîchie : plus de différence");
            Invoke(window, "DoUndo", null);
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[0]).StartsWith("XLe marabout"), "Ctrl+Z annule la restauration : la frappe est de retour");
            Invoke(window, "DoRedo", null);
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[0]) == "Le marabout dort.", "Ctrl+Y la rejoue");
            Invoke(window, "DoUndo", null);
            DoEvents();

            // — Restauration partielle depuis la comparaison : un seul paragraphe.
            item.Document.Paragraphs[2].Runs[0].Text = "La nuit tombe vite.";
            Invoke(window, "CompareSnapshots", new object[] { snapshot, null });
            DoEvents();
            Check(compare.Delta.Modified == 2, "deux paragraphes modifiés avant la restauration partielle");
            compare.Next(); // la 2e différence : « La nuit tombe vite. »
            DoEvents();
            var change = compare.CurrentChange;
            Check(change != null && change.NewIndex == 2, "la différence courante est le 3e paragraphe");
            Invoke(window, "RestoreParagraph", new object[] { item, change });
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[2]) == "La nuit tombe." && PivotEdit.FlatText(item.Document.Paragraphs[0]).StartsWith("XLe"),
                "restaurer ce paragraphe seul : le 3e revient, le 1er garde sa frappe");
            Check(compare.Delta.Modified == 1, "…et la comparaison ne montre plus qu'une différence");
            Invoke(window, "DoUndo", null);
            DoEvents();
            Check(PivotEdit.FlatText(item.Document.Paragraphs[2]) == "La nuit tombe vite.", "…annulable");

            compare.Close();
            DoEvents();
            Invoke(panel, "OnCloseRequested", null);
            DoEvents();
            Check(host.Visibility != Visibility.Visible && inspector.Visibility == Visibility.Visible, "fermer le panneau rend l'inspecteur");
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloaded = PlotFile.Load(path);
            Check(reloaded.Snapshots.Count == 2, "les versions sont sur le disque (v17)");
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

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            Invoke2(target, name, args);
        }

        private static object Invoke2(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}
