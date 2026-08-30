using System;
using System.Collections.Generic;
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
    /// <summary>Sonde du batch 39 — le rail de la colonne de droite, sur
    /// vraie MainWindow hors écran : présent, quatre onglets, clic → panneau
    /// ouvert, clic sur l'actif → colonne repliée, onglet grisé sans élément
    /// courant, pastille de correction à jour, rail absent en mode calme.
    /// Jamais de clic synthétique (règle du batch 3) : le gestionnaire du
    /// clic (ClickRailTab) est appelé par réflexion. Règle du batch 11 :
    /// settings.json est l'affaire de l'appelant (A1Probe).</summary>
    public static class RailProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b39");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b39.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE RAIL EN ÉCHEC : " + error);
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
            chapter.Title = "Chapitre du rail";
            chapter.Document = TextDocument.FromPlainText("Le marabout dort tranquilement.\nUn marabout veille sur le marais.");
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.ClassicCompatibility = false;
            AppSettings.RightPanel = RightPanel.Inspector;
            AppSettings.ProofEnabled = true;
            AppSettings.SpellEnabled = true;
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

            var rail = (Border)GetField(window, "_rail");
            var railCol = (ColumnDefinition)GetField(window, "_railCol");
            var tabs = (Dictionary<RightPanel, Border>)GetField(window, "_railTabs");
            var inspectorCol = (ColumnDefinition)GetField(window, "_inspectorCol");
            var inspector = (Border)GetField(window, "_inspector");
            var correctionHost = (Border)GetField(window, "_correctionHost");
            var searchHost = (Border)GetField(window, "_searchHost");
            var versionsHost = (Border)GetField(window, "_versionsHost");
            var badge = (Border)GetField(window, "_railBadge");
            var badgeText = (TextBlock)GetField(window, "_railBadgeText");

            // — Le rail est là, quatre onglets, à droite de tout.
            Check(rail.Visibility == Visibility.Visible && rail.ActualWidth == 40 && Grid.GetColumn(rail) == 5,
                "le rail est présent, 40 px, dernière colonne de la grille");
            Check(tabs.Count == 4 && tabs.ContainsKey(RightPanel.Inspector) && tabs.ContainsKey(RightPanel.Correction)
                && tabs.ContainsKey(RightPanel.Search) && tabs.ContainsKey(RightPanel.Versions), "quatre onglets");
            foreach (var pair in tabs)
                Check(pair.Value.ToolTip is string && ((string)pair.Value.ToolTip).Length > 0,
                    "l'onglet « " + RightPanels.Name(pair.Key) + " » a une infobulle (« " + pair.Value.ToolTip + " »)");
            Check(((string)tabs[RightPanel.Search].ToolTip).Contains("Ctrl+Maj+F"), "…qui enseigne le raccourci");

            // — Sans élément courant : Inspecteur et Correction grisés, les outils vifs.
            Check(tabs[RightPanel.Inspector].Opacity < 1 && tabs[RightPanel.Correction].Opacity < 1,
                "sans élément courant, Inspecteur et Correction sont grisés");
            Check(tabs[RightPanel.Search].Opacity == 1 && tabs[RightPanel.Versions].Opacity == 1,
                "…Recherche et Versions restent disponibles");
            Check(inspectorCol.Width.Value == 0, "…et la colonne est repliée (l'inspecteur n'a rien à dire)");
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Inspector && inspectorCol.Width.Value == 0,
                "cliquer un onglet grisé ne fait rien — un panneau indisponible ne devient jamais actif");

            // — Cliquer Recherche : la colonne s'ouvre, l'onglet porte l'accent.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Search });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Search && searchHost.Visibility == Visibility.Visible && inspectorCol.Width.Value > 0,
                "cliquer Recherche ouvre la colonne sur le panneau de recherche");
            Check(ReferenceEquals(tabs[RightPanel.Search].Background, Chrome.Accent)
                && !ReferenceEquals(tabs[RightPanel.Versions].Background, Chrome.Accent),
                "l'onglet actif porte l'accent, les autres non");

            // — Cliquer Versions : on change de panneau, pas de colonne.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Versions });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Versions && versionsHost.Visibility == Visibility.Visible && searchHost.Visibility != Visibility.Visible,
                "cliquer Versions remplace Recherche dans la même colonne");

            // — Cliquer l'actif : la colonne se replie.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Versions });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.None && inspectorCol.Width.Value == 0 && versionsHost.Visibility != Visibility.Visible,
                "cliquer l'onglet actif replie la colonne");
            var anyAccent = false;
            foreach (var pair in tabs) if (ReferenceEquals(pair.Value.Background, Chrome.Accent)) anyAccent = true;
            Check(!anyAccent, "…et plus aucun onglet ne porte l'accent");

            // — Un élément courant : Inspecteur et Correction se réveillent.
            var opened = (Project)GetField(window, "_project");
            BinderItem item = null;
            foreach (var candidate in opened.AllItems()) if (candidate.Title == "Chapitre du rail") item = candidate;
            Invoke(window, "OnBinderSelection", new object[] { item });
            DoEvents();
            Check(tabs[RightPanel.Inspector].Opacity == 1 && tabs[RightPanel.Correction].Opacity == 1,
                "avec un élément courant, Inspecteur et Correction sont disponibles");
            Check(inspectorCol.Width.Value == 0, "…la colonne reste repliée tant qu'on n'a rien demandé");

            Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
            DoEvents();
            var editor = (EditorView)GetField(window, "_editor");
            Invoke(editor, "RunCheck", null);
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Correction && correctionHost.Visibility == Visibility.Visible,
                "cliquer Correction ouvre le panneau des signalements");
            var count = editor.FindingCount;
            Check(count > 0, "le pilote a des signalements (" + count + ")");
            Check(badge.Visibility == Visibility.Visible && badgeText.Text == count.ToString(),
                "la pastille de Correction affiche le nombre de signalements (« " + badgeText.Text + " »)");

            RenderPng(rail, Path.Combine(Path.GetTempPath(), "marabook-b39-rail.png"), "rail");
            RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b39-fenetre.png"), "fenêtre");

            Invoke(window, "ClickRailTab", new object[] { RightPanel.Inspector });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Inspector && inspector.Visibility == Visibility.Visible && correctionHost.Visibility != Visibility.Visible,
                "cliquer Inspecteur rend l'inspecteur");

            // — Le mode calme emporte le rail avec le reste ; en sortir le ramène.
            Invoke(window, "SetCalmMode", new object[] { true });
            DoEvents();
            Check(rail.Visibility != Visibility.Visible && railCol.Width.Value == 0 && inspectorCol.Width.Value == 0,
                "en mode calme, le rail disparaît avec la colonne");
            Invoke(window, "SetCalmMode", new object[] { false });
            DoEvents();
            Check(rail.Visibility == Visibility.Visible && railCol.Width.Value == 40 && inspector.Visibility == Visibility.Visible,
                "en sortir ramène le rail et le panneau actif");

            // — Le réglage persisté est le nouveau champ, pas les anciens booléens.
            AppSettings.Save();
            var json = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json"));
            Check(json.Contains("\"rightPanel\"") && !json.Contains("\"inspectorVisible\"") && !json.Contains("\"searchPanel\""),
                "settings.json porte « rightPanel » et plus les anciennes clés");

            window.Close();
            DoEvents();
        }

        private static void RenderPng(FrameworkElement element, string path, string what)
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
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  (" + what + " rendu : " + path + ")");
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
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
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
