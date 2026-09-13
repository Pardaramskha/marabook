using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde de l'écran d'accueil (13/09) : une MainWindow lancée
    /// SANS projet, hors écran — l'accueil apparaît, la fenêtre reste voilée
    /// et désactivée dessous, l'accueil refuse de se fermer à la main, puis
    /// Release le retire et l'interface revient. Rendus PNG de l'accueil et
    /// de la fenêtre voilée. settings.json n'est pas touché (sauvegardé
    /// puis restauré). À lancer à la main (/main:UniversSale.Tests.Ui.WelcomeProbe).</summary>
    public static class WelcomeProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            // Règle du batch 11 : settings.json est sauvegardé puis restauré
            // (OpenFile enregistre les récents — la sonde ne doit rien laisser).
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            try { Probe(); }
            catch (Exception error)
            {
                Console.WriteLine("SONDE ACCUEIL EN ÉCHEC : " + error);
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
            }
            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "SONDE ACCUEIL OK" : _failures + " ÉCHEC(S)");
            return _failures == 0 ? 0 : 1;
        }

        private static void Probe()
        {
            AppSettings.Load();
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
                Thread.Sleep(300);
                DoEvents();
                var welcome = (Window)Field(window, "_welcome");
                Check(welcome != null, "lancée sans projet : l'accueil est là");
                var veil = (FrameworkElement)Field(window, "_welcomeVeil");
                var shellRoot = (UIElement)Field(window, "_shellRoot");
                Check(veil.Visibility == Visibility.Visible, "la fenêtre est voilée (blanc cassé)");
                Check(!shellRoot.IsEnabled, "l'interface est désactivée dessous");
                Check(!window.HasProjectPath, "aucun projet enregistré n'est ouvert");
                if (welcome != null)
                {
                    Check(welcome.ActualWidth >= window.ActualWidth * 0.75 && welcome.ActualHeight >= window.ActualHeight * 0.75,
                        "l'accueil prend la majeure partie de la fenêtre (" + (int)welcome.ActualWidth + "×" + (int)welcome.ActualHeight + ")");
                    RenderPng(welcome, Path.Combine(Path.GetTempPath(), "marabook-b44-accueil.png")); // la fenêtre entière : un Content rendu seul garde son décalage de marge
                    RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b44-accueil-fond.png"));
                    // Fermeture à la main : refusée.
                    welcome.Close();
                    DoEvents();
                    Check(Field(window, "_welcome") != null, "Close() à la main : l'accueil reste");
                    // Release : il se retire, l'interface revient.
                    welcome.GetType().GetMethod("Release").Invoke(welcome, null);
                    DoEvents();
                    Check(Field(window, "_welcome") == null, "Release : l'accueil est parti");
                    Check(veil.Visibility == Visibility.Collapsed, "le voile est levé");
                    Check(shellRoot.IsEnabled, "l'interface est réactivée");
                }
                // Un projet ouvert avant l'affichage : pas d'accueil (le cas
                // du .plot en argument) — vérifié sur une seconde fenêtre.
                var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-accueil");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "accueil.plot");
                var project = UniversSale.Model.Project.CreateNew();
                UniversSale.Persistence.PlotFile.Save(project, path);
                var second = new MainWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -2600,
                    Top = 0,
                    Width = 1000,
                    Height = 700,
                    ShowInTaskbar = false
                };
                try
                {
                    second.OpenFile(path);
                    second.Show();
                    DoEvents();
                    Thread.Sleep(200);
                    DoEvents();
                    Check(Field(second, "_welcome") == null, "lancée AVEC un .plot : pas d'accueil");
                }
                finally
                {
                    try { second.Close(); } catch { }
                    DoEvents();
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            finally
            {
                try { window.Close(); } catch { }
                DoEvents();
            }
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            if (width <= 0 || height <= 0) { Console.WriteLine("  (rendu vide : " + path + ")"); return; }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("  (rendu : " + path + ")");
        }

        private static object Field(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }
    }
}
