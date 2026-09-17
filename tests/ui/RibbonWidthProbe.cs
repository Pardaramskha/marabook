using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde des patchs du 17/09/2026, sur une vraie MainWindow hors
    /// écran :
    ///  - le ruban à plusieurs largeurs de fenêtre (1280 → 640 px) : aucun
    ///    bouton d'aucun onglet ne doit être rogné, le sélecteur d'affichage
    ///    ne doit recouvrir aucun bouton ; un PNG du ruban par onglet et par
    ///    largeur dans %TEMP% ;
    ///  - les zones de texte multilignes : un clic sous les lignes écrites
    ///    tombe bien dans l'hôte du texte (curseur I, focus), pas sur le cadre ;
    ///  - la règle avec une bulle d'annotation : la largeur mesurée reste
    ///    celle du papier.
    /// À lancer à la main (/main:Marabook.Tests.Ui.RibbonWidthProbe) ;
    /// settings.json est sauvegardé puis restauré.</summary>
    public static class RibbonWidthProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-1709");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "patchs.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("SONDE EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            Console.WriteLine(_failures == 0 ? "SONDE VERTE" : _failures + " ÉCHEC(S)");
            return _failures;
        }

        private static void Probe(string path)
        {
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Title = "Le marabout";
            chapter.Document = TextDocument.FromPlainText(
                "Le marabout dort tranquillement sur la rive du marais, une patte repliée sous le ventre.\n"
                + "Un marabout veille toujours : nul ne l'a jamais vu y déroger.\n"
                + "La nuit tombe lentement sur les roseaux ; les grenouilles se taisent une à une.");
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.RightPanel = RightPanel.Inspector;
            AppSettings.ShowRulers = true;
            AppSettings.ShowAnnotations = true;
            AppSettings.DarkTheme = false;
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
            try
            {
                window.Show();
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                BinderItem item = null;
                foreach (var candidate in opened.AllItems()) if (candidate.Title == "Le marabout") item = candidate;
                Invoke(window, "OnBinderSelection", new object[] { item });
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                var bar = (Border)GetField(editor, "_ribbonBar");
                var tabs = FindChild<TabControl>(bar);
                Check(tabs != null, "le ruban a ses onglets");

                // ---------- 1. zones multilignes : l'inspecteur (synopsis, 90 px)
                var synopsis = (TextBox)GetField(window, "_synopsisBox");
                if (synopsis != null && synopsis.IsVisible)
                {
                    synopsis.Text = "Une ligne.";
                    DoEvents();
                    var hit = synopsis.InputHitTest(new Point(20, synopsis.ActualHeight - 8)) as DependencyObject;
                    var host = FindChild<ScrollViewer>(synopsis);
                    Check(host != null && host.ActualHeight >= synopsis.ActualHeight - 12,
                        "synopsis : l'hôte du texte couvre toute la zone (" + (host == null ? -1 : host.ActualHeight)
                        + " / " + synopsis.ActualHeight + ")");
                    Check(hit != null && host != null && IsDescendant(hit, host),
                        "synopsis : un clic sous la ligne écrite tombe dans l'hôte du texte (" + Describe(hit) + ")");
                }
                else Console.WriteLine("  (synopsis non visible : hit-test sauté)");

                // ---------- 2. la règle avec une bulle
                var composed = (ComposedView)GetField(editor, "_composed");
                var rulerH = (FrameworkElement)GetField(editor, "_rulerH");
                composed.SelectAll();
                var annotation = new Annotation { Text = "Bulle de test", Created = "2026-09-17 12:00" };
                Check(composed.AnnotateSelection(annotation.Id), "annotation ancrée sur la sélection");
                item.Document.Annotations.Add(annotation);
                Invoke(editor, "RebuildAnnotationsPanel", null);
                DoEvents();
                Thread.Sleep(150);
                DoEvents();
                var rects = composed.PageRects(rulerH);
                var composition = composed.CurrentComposition;
                Check(rects.Count > 0 && composition != null, "des rectangles de page pour la règle");
                if (rects.Count > 0 && composition != null)
                {
                    // À l'écran, le papier est à l'échelle du zoom (LayoutTransform de la colonne).
                    var column = (FrameworkElement)GetField(composed, "_column");
                    var scale = column == null ? null : column.LayoutTransform as ScaleTransform;
                    var zoom = scale == null ? 1 : scale.ScaleX;
                    var expected = composition.PageWidthPx * zoom;
                    Check(Math.Abs(rects[0].Width - expected) < 1.5,
                        "règle : largeur mesurée = papier × zoom (" + rects[0].Width.ToString("0.0") + " / "
                        + expected.ToString("0.0") + ", zoom " + zoom.ToString("0.00") + ") malgré la bulle");
                    var bubbles = (Canvas)GetField(composed, "_bubbleLayer");
                    Check(bubbles != null && bubbles.Children.Count > 0, "la bulle est bien posée");
                }
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-1709-regle.png"));

                // ---------- 3. le ruban à plusieurs largeurs
                window.MinWidth = 300;
                // Pile et inspecteur ouverts, le ruban fait « fenêtre − 588 » :
                // 1188 → 600 px (la cible), 900 → 312 px (le plancher de la colonne).
                foreach (var width in new[] { 1280, 1188, 1000, 900 })
                {
                    window.Width = width;
                    DoEvents();
                    Thread.Sleep(100);
                    DoEvents();
                    Console.WriteLine("-- fenêtre " + width + " px : ruban " + bar.ActualWidth.ToString("0")
                        + " × " + bar.ActualHeight.ToString("0") + " px");
                    var views = (FrameworkElement)tabs.Tag;
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        var tab = tabs.Items[i] as TabItem;
                        if (tab == null) continue;
                        tabs.SelectedIndex = i;
                        DoEvents();
                        Thread.Sleep(60);
                        DoEvents();
                        var content = tab.Content as FrameworkElement;
                        var clipped = 0;
                        var covered = 0;
                        var total = 0;
                        var viewsRect = views == null ? Rect.Empty
                            : new Rect(views.TranslatePoint(new Point(0, 0), bar), views.RenderSize);
                        foreach (var control in Controls(content))
                        {
                            if (!control.IsVisible || control.ActualWidth < 1) continue;
                            total++;
                            var origin = control.TranslatePoint(new Point(0, 0), bar);
                            var rect = new Rect(origin, control.RenderSize);
                            if (rect.Right > bar.ActualWidth + 0.5 || rect.Left < -0.5) clipped++;
                            if (!viewsRect.IsEmpty && rect.IntersectsWith(viewsRect)) covered++;
                        }
                        Check(clipped == 0, "  " + tab.Header + " @" + width + " : " + total + " commandes, "
                            + clipped + " rognée(s), ruban " + bar.ActualHeight.ToString("0") + " px de haut");
                        Check(covered == 0, "  " + tab.Header + " @" + width + " : " + covered
                            + " commande(s) sous le sélecteur d'affichage");
                        RenderPng(bar, Path.Combine(Path.GetTempPath(),
                            "marabook-1709-ruban-" + width + "-" + i + ".png"));
                    }
                }
            }
            finally
            {
                try { window.Close(); } catch { }
                DoEvents();
            }
        }

        private static IEnumerable<FrameworkElement> Controls(DependencyObject root)
        {
            if (root == null) yield break;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ButtonBase || child is ComboBox || child is TextBox || child is Slider)
                {
                    yield return (FrameworkElement)child;
                    continue;
                }
                foreach (var nested in Controls(child)) yield return nested;
            }
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var found = root as T;
            if (found != null) return found;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = FindChild<T>(VisualTreeHelper.GetChild(root, i));
                if (child != null) return child;
            }
            return null;
        }

        private static bool IsDescendant(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (node == ancestor) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private static string Describe(DependencyObject node)
        {
            if (node == null) return "rien";
            var element = node as FrameworkElement;
            return node.GetType().Name + (element != null && element.Name != "" ? " " + element.Name : "");
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            if (width < 1 || height < 1) return;
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK  " : "  KO  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null, args == null ? Type.EmptyTypes : Array.ConvertAll(args, a => a.GetType()), null);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
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
