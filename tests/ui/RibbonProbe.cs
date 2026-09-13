using System;
using System.IO;
using System.Reflection;
using System.Threading;
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
    /// <summary>Sonde visuelle de l'onglet Correction (13/09) : icônes du
    /// ruban et panneau groupé par type — rendu PNG de la fenêtre entière,
    /// onglet Correction sélectionné, panneau Détails ouvert. À lancer à la
    /// main (/main:UniversSale.Tests.Ui.RibbonProbe) ; settings.json est
    /// sauvegardé puis restauré.</summary>
    public static class RibbonProbe
    {
        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b44");
            var failures = 0;
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b44.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("SONDE RUBAN EN ÉCHEC : " + error);
                failures++;
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
            return failures;
        }

        private static void Probe(string path)
        {
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Title = "Le marabout";
            chapter.Document = TextDocument.FromPlainText(
                "Le marabout dort tranquilement sur la rive du marais, une patte repliée sous le ventre.\n"
                + "Un marabout veille toujours : il faisait rapidement ce qu'il avait à faire, et nul ne l'a jamais vu y déroger.\n"
                + "La nuit tombe lentement sur les roseaux ; les grenouilles se taisent une à une.");
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.RightPanel = RightPanel.Correction;
            AppSettings.ProofEnabled = true;
            AppSettings.SpellEnabled = true;
            AppSettings.GrammarEnabled = false;
            AppSettings.StyleEnabled = true;
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
                Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                var tabs = FindTabControl(editor);
                if (tabs != null)
                {
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        var tab = tabs.Items[i] as TabItem;
                        if (tab != null && Equals(tab.Header, "Correction")) tabs.SelectedIndex = i;
                    }
                }
                Console.WriteLine("  onglets trouvés : " + (tabs != null));
                Invoke(editor, "RunCheck", null);
                // Le style est différé : on laisse le pont répondre puis on relance.
                for (var i = 0; i < 40; i++) { Thread.Sleep(100); DoEvents(); }
                Invoke(editor, "RunCheck", null);
                DoEvents();
                Thread.Sleep(300);
                DoEvents();
                var content = (FrameworkElement)window.Content;
                RenderPng(content, Path.Combine(Path.GetTempPath(), "marabook-b44-correction.png"));
                // Chaque onglet du ruban (13/09) : un rendu de la fenêtre par
                // onglet, pour vérifier hauteurs, piles et grands carrés.
                if (tabs != null)
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        var tab = tabs.Items[i] as TabItem;
                        if (tab == null) continue;
                        tabs.SelectedIndex = i;
                        DoEvents();
                        Thread.Sleep(100);
                        DoEvents();
                        RenderPng(content, Path.Combine(Path.GetTempPath(),
                            "marabook-b44-onglet-" + i + ".png"));
                    }

                // Le dialogue des options, avec ses pastilles (13/09).
                var dialog = (Window)Activator.CreateInstance(typeof(ProofOptionsDialog),
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { window }, null);
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -2600;
                dialog.Top = 0;
                dialog.Show();
                DoEvents();
                Thread.Sleep(200);
                DoEvents();
                RenderPng((FrameworkElement)dialog.Content, Path.Combine(Path.GetTempPath(), "marabook-b44-options.png"));
                dialog.Close();
                DoEvents();
            }
            finally
            {
                try { window.Close(); } catch { }
                DoEvents();
            }
        }

        private static TabControl FindTabControl(DependencyObject root)
        {
            if (root == null) return null;
            var found = root as TabControl;
            if (found != null) return found;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = FindTabControl(VisualTreeHelper.GetChild(root, i));
                if (child != null) return child;
            }
            return null;
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("  (rendu : " + path + ")");
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
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
