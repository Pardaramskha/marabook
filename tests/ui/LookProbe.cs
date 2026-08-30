using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde de contrôle visuel du batch 40 : LE MÊME ÉCRAN (un
    /// écrit ouvert, le panneau de droite ouvert) rendu en PNG dans les deux
    /// thèmes et avec « papier blanc en mode sombre », pour comparer avant et
    /// après. Fichiers : %TEMP%\marabook-b40-&lt;étiquette&gt;-clair.png,
    /// -sombre.png, -sombre-papier-blanc.png ; l'étiquette vient de la
    /// variable d'environnement MARABOOK_LOOK (défaut « apres »). Aucune
    /// vérification : un rendu, pour les yeux. Règle du batch 11 :
    /// settings.json est l'affaire de l'appelant (A1Probe) ; le thème et
    /// l'option papier blanc sont remis en place à la sortie.</summary>
    public static class LookProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            // Lancement isolé (rendu « avant » sur un dépôt remisé) : même
            // règle settings.json qu'A1Probe.
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            try { Run(); }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
                if (Application.Current != null) Application.Current.Shutdown();
            }
            return _failures == 0 ? 0 : 1;
        }

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b40");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b40.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE VISUELLE EN ÉCHEC : " + error);
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
            var label = Environment.GetEnvironmentVariable("MARABOOK_LOOK");
            if (string.IsNullOrEmpty(label)) label = "apres";

            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Title = "Le marabout";
            chapter.Synopsis = "Où l'on apprend que le marabout ne dort jamais tout à fait.";
            chapter.Document = TextDocument.FromPlainText(
                "Le marabout dort tranquilement sur la rive du marais, une patte repliée sous le ventre, l'oeil mi-clos.\n"
                + "Un marabout veille toujours : c'est la règle du marais, et nul ne l'a jamais vu y déroger.\n"
                + "La nuit tombe lentement sur les roseaux ; les grenouilles se taisent une à une.");
            var second = new BinderItem { Kind = ItemKind.Text, Title = "La rive" };
            second.Document = TextDocument.FromPlainText("Le second chapitre attend son heure.");
            project.Category(Project.KeyWritings).Children.Add(second);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            var savedDark = AppSettings.DarkTheme;
            var savedWhite = AppSettings.WhitePaperInDark;
            AppSettings.ClassicCompatibility = false;
            AppSettings.RightPanel = RightPanel.Inspector;
            AppSettings.ProofEnabled = true;
            AppSettings.SpellEnabled = true;
            AppSettings.DarkTheme = false;
            AppSettings.WhitePaperInDark = false;
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
                Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                Invoke(editor, "RunCheck", null);
                DoEvents();

                var content = (FrameworkElement)window.Content;
                Settle();
                RenderPng(content, Path.Combine(Path.GetTempPath(), "marabook-b40-" + label + "-clair.png"));

                AppSettings.DarkTheme = true;
                Invoke(window, "ApplyAppearance", null);
                Settle();
                RenderPng(content, Path.Combine(Path.GetTempPath(), "marabook-b40-" + label + "-sombre.png"));

                AppSettings.WhitePaperInDark = true;
                Invoke(window, "ApplyAppearance", null);
                Settle();
                RenderPng(content, Path.Combine(Path.GetTempPath(), "marabook-b40-" + label + "-sombre-papier-blanc.png"));
            }
            finally
            {
                AppSettings.DarkTheme = savedDark;
                AppSettings.WhitePaperInDark = savedWhite;
                Invoke(window, "ApplyAppearance", null);
                DoEvents();
                window.Close();
                DoEvents();
            }
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                // La racine de la fenêtre est à l'origine : rendu direct (un
                // VisualBrush perd le tracé des pages composées).
                element.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                if (element.Parent == null || element.Parent is Window) bitmap.Render(element);
                else
                {
                    // Un élément posé ailleurs qu'à l'origine : par un VisualBrush.
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                        dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
                    bitmap.Render(visual);
                }
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  (rendu : " + path + ")");
            }
            catch (Exception error)
            {
                Console.WriteLine("  (rendu PNG impossible : " + error.Message + ")");
            }
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            method.Invoke(target, args);
        }

        /// <summary>Laisse passer les minuteries différées (composition,
        /// correction) : quelques tours de pompe espacés.</summary>
        private static void Settle()
        {
            for (var i = 0; i < 12; i++)
            {
                System.Threading.Thread.Sleep(60);
                DoEvents();
            }
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}
