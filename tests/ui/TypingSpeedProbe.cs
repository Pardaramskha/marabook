using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde de VITESSE DE FRAPPE (22/09) — un outil de mesure, pas
    /// une sonde de la campagne : la vraie MainWindow ouvre trois écrits et
    /// chronomètre vingt frappes dans chacun — (A) le « pavé » du projet de
    /// test : UN paragraphe de 630 000 signes ; (B) 280 pages en paragraphes
    /// normaux, caret en fin ; (C) le même, caret au milieu — puis isole les
    /// étages : typographie à la frappe éteinte, clone d'annulation, passe
    /// typographique seule, recomposition seule. Lance-la seule
    /// (/main:Marabook.Tests.Ui.TypingSpeedProbe) ; settings.json est
    /// sauvegardé puis restauré.</summary>
    public static class TypingSpeedProbe
    {
        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-typing-probe");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "vitesse.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("SONDE EN ÉCHEC : " + error);
            }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
            if (Application.Current != null) { Application.Current.Shutdown(); DoEvents(); }
            return 0;
        }

        private static void Probe(string path)
        {
            var sentence = "Le marabout dort sur la rive, et la rivière emporte les mots du soir. ";
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Mesures", Book = new BookInfo() };
            // (A) le pavé : un seul paragraphe de 7 000 phrases.
            var slab = new BinderItem { Kind = ItemKind.Text, Title = "Pavé (un paragraphe)", Page = book.Book.Template.Clone() };
            slab.Document = TextDocument.FromPlainText(Repeat(sentence, 7000));
            book.Children.Add(slab);
            // (B) 280 pages en paragraphes de ~2 000 signes (28 phrases) : 315 paragraphes.
            var sb = new StringBuilder();
            for (var p = 0; p < 315; p++) { sb.Append(Repeat(sentence, 28).TrimEnd()); sb.Append('\n'); }
            var normal = new BinderItem { Kind = ItemKind.Text, Title = "Normal (315 paragraphes)", Page = book.Book.Template.Clone() };
            normal.Document = TextDocument.FromPlainText(sb.ToString().TrimEnd('\n'));
            book.Children.Add(normal);
            writings.Children.Add(book);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.DarkTheme = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600, Top = 0, Width = 1280, Height = 800, ShowInTaskbar = false
            };
            try
            {
                window.Show();
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                BinderItem slabItem = null, normalItem = null;
                foreach (var item in opened.AllItems())
                {
                    if (item.Title.StartsWith("Pavé")) slabItem = item;
                    if (item.Title.StartsWith("Normal")) normalItem = item;
                }
                var editor = (EditorView)GetField(window, "_editor");
                var composed = (ComposedView)GetField(editor, "_composed");

                Console.WriteLine("== (A) Le pavé : un paragraphe de " + PivotEdit.FlatLength(slabItem.Document.Paragraphs[0]).ToString("N0") + " signes");
                Measure(window, composed, slabItem, 0, int.MaxValue);
                Console.WriteLine();
                Console.WriteLine("== (B) 280 pages en paragraphes normaux, caret en fin de document");
                Measure(window, composed, normalItem, normalItem.Document.Paragraphs.Count - 1, int.MaxValue);
                Console.WriteLine();
                Console.WriteLine("== (C) Le même, caret au milieu (paragraphe 150)");
                Measure(window, composed, normalItem, 150, 10);
                Console.WriteLine();
                Console.WriteLine("== (D) Le rendu paresseux au défilement");
                ScrollCheck(composed);
            }
            finally
            {
                SetField(window, "_dirty", false);
                window.Close();
                DoEvents();
            }
        }

        private static void Measure(MainWindow window, ComposedView composed, BinderItem item, int paragraph, int offset)
        {
            Invoke(window, "OnBinderSelection", new object[] { item });
            DoEvents();
            var pages = composed.CurrentComposition.Pages.Count;
            Console.WriteLine("   " + pages + " pages composées ; " + item.Document.Paragraphs.Count + " paragraphes");
            var length = PivotEdit.FlatLength(item.Document.Paragraphs[paragraph]);
            composed.PlaceCaret(paragraph, Math.Min(offset, length), false);
            DoEvents();

            // — Vingt frappes, tout allumé (comme l'utilisateur).
            Console.WriteLine("   frappe complète (typo à la frappe + annulation + recomposition + rendu) : " + Typing(composed, 20, true));
            // — Typographie à la frappe éteinte.
            var live = composed.LiveTypography;
            composed.LiveTypography = null;
            Console.WriteLine("   sans typographie à la frappe : " + Typing(composed, 20, true));
            composed.LiveTypography = live;
            // — Les étages, isolés (hors rendu).
            var clock = Stopwatch.StartNew();
            var clone = PivotEdit.Clone(item.Document);
            clock.Stop();
            Console.WriteLine("   clone d'annulation du document : " + clock.ElapsedMilliseconds + " ms (" + clone.Paragraphs.Count + " paragraphes)");
            var flat = PivotEdit.FlatText(item.Document.Paragraphs[paragraph]);
            clock.Restart();
            Correction.Typography.Clean(flat, AppSettings.TypographyLive, Correction.TypographyPass.NoProofSpans(item.Document.Paragraphs[paragraph]), 0);
            clock.Stop();
            Console.WriteLine("   passe typographique du paragraphe courant (" + flat.Length.ToString("N0") + " signes) : " + clock.ElapsedMilliseconds + " ms");
            var engine = GetField(composed, "_engine");
            clock.Restart();
            engine.GetType().GetMethod("RecomposeParagraph").Invoke(engine, new object[] { paragraph });
            clock.Stop();
            Console.WriteLine("   recomposition du paragraphe + repagination : " + clock.ElapsedMilliseconds + " ms");
            clock.Restart();
            DoEvents();
            clock.Stop();
            Console.WriteLine("   un tour de rendu (DoEvents) : " + clock.ElapsedMilliseconds + " ms");
            // — L'hypothèse : RefreshPages invalide TOUTES les pages après la
            //   première changée, visibles ou non, et chacune se redessine.
            var first = (int)engine.GetType().GetMethod("FirstPageOf").Invoke(engine, new object[] { paragraph });
            clock.Restart();
            composed.GetType().GetMethod("RefreshPages", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(composed, new object[] { first });
            DoEvents();
            clock.Stop();
            Console.WriteLine("   RefreshPages(" + first + ") + rendu : " + clock.ElapsedMilliseconds + " ms pour " + (pages - first) + " pages invalidées");
            clock.Restart();
            composed.GetType().GetMethod("RefreshPages", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(composed, new object[] { pages - 1 });
            DoEvents();
            clock.Stop();
            Console.WriteLine("   RefreshPages(dernière page seule) + rendu : " + clock.ElapsedMilliseconds + " ms");
            // — La frappe SANS tour de rendu (le modèle et le moteur seuls).
            Console.WriteLine("   frappe sans rendu (modèle + moteur seuls) : " + Typing(composed, 10, false));
            DoEvents();
            // — Les minuteurs qui suivent une frappe : statistiques (400 ms) et correction (600 ms).
            var cache = (Dictionary<string, int>)GetField(window, "_pageCountCache");
            cache.Remove(item.Id);
            clock.Restart();
            window.GetType().GetMethod("UpdateStats", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
            clock.Stop();
            Console.WriteLine("   UpdateStats (mots + pages, page cache vidé par la frappe) : " + clock.ElapsedMilliseconds + " ms");
            var editorView = (EditorView)GetField(window, "_editor");
            clock.Restart();
            editorView.GetType().GetMethod("RunCheck", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editorView, null);
            clock.Stop();
            var checkOnly = clock.ElapsedMilliseconds;
            clock.Restart();
            DoEvents();
            clock.Stop();
            Console.WriteLine("   RunCheck (la passe de correction synchrone) : " + checkOnly + " ms de calcul + " + clock.ElapsedMilliseconds + " ms de rendu des ondulés");
        }

        /// <summary>Les pages loin de la fenêtre sont dessinées en papier nu
        /// et notées périmées ; le défilement les redessine à l'approche.</summary>
        private static void ScrollCheck(ComposedView composed)
        {
            var pages = (System.Windows.Controls.StackPanel)GetField(composed, "_pages");
            Func<int, bool> stale = delegate(int k)
            {
                var slot = pages.Children[k];
                var page = slot.GetType().GetField("Page").GetValue(slot);
                return (bool)page.GetType().GetField("Stale").GetValue(page);
            };
            composed.ScrollToVerticalOffset(0);
            DoEvents();
            Console.WriteLine("   en haut : page 0 " + (stale(0) ? "PÉRIMÉE (anormal)" : "dessinée") + ", page 200 " + (stale(200) ? "périmée (attendu)" : "DESSINÉE (anormal)"));
            var target = composed.ExtentHeight * 200.0 / pages.Children.Count;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            composed.ScrollToVerticalOffset(target);
            DoEvents();
            clock.Stop();
            Console.WriteLine("   après un saut à la page 200 (" + clock.ElapsedMilliseconds + " ms) : page 200 " + (stale(200) ? "PÉRIMÉE (anormal)" : "dessinée") + ", page 0 " + (stale(0) ? "périmée (attendu)" : "dessinée"));
            composed.ScrollToVerticalOffset(0);
            DoEvents();
            Console.WriteLine("   de retour en haut : page 0 " + (stale(0) ? "PÉRIMÉE (anormal)" : "dessinée"));
        }

        /// <summary>N frappes d'une lettre, chacune suivie d'un tour de
        /// Dispatcher (le rendu) ; la moyenne et la pire, en ms.</summary>
        private static string Typing(ComposedView composed, int count, bool render)
        {
            var times = new List<double>();
            for (var i = 0; i < count; i++)
            {
                var clock = Stopwatch.StartNew();
                composed.TypeText(i % 7 == 6 ? " " : "a");
                if (render) DoEvents();
                clock.Stop();
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            double sum = 0, worst = 0;
            foreach (var t in times) { sum += t; if (t > worst) worst = t; }
            return "moyenne " + (sum / times.Count).ToString("0") + " ms, pire " + worst.ToString("0") + " ms";
        }

        private static string Repeat(string text, int times)
        {
            var sb = new StringBuilder(text.Length * times);
            for (var i = 0; i < times; i++) sb.Append(text);
            return sb.ToString();
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable");
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable");
            method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}
