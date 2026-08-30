using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// vraie MainWindow hors écran : présent, onglets selon la nature de
    /// l'élément courant, clic → panneau ouvert, clic sur l'actif → colonne
    /// repliée, onglet grisé sans élément courant, pastille de correction à
    /// jour et dans l'onglet, infobulle à gauche, repli vers Général quand
    /// la nature change, rail absent en mode calme. Jamais de clic
    /// synthétique (règle du batch 3) : le gestionnaire du clic
    /// (ClickRailTab) est appelé par réflexion. Règle du batch 11 :
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

            // — Le rail est là, à droite de tout ; sans sélection, Général et Recherche.
            Check(rail.Visibility == Visibility.Visible && rail.ActualWidth == 40 && Grid.GetColumn(rail) == 5,
                "le rail est présent, 40 px, dernière colonne de la grille");
            Check(tabs.Count == 2 && tabs.ContainsKey(RightPanel.Inspector) && tabs.ContainsKey(RightPanel.Search),
                "rien de sélectionné : deux onglets, Général et Recherche");
            var searchTip = tabs[RightPanel.Search].ToolTip as ToolTip;
            Check(searchTip != null && searchTip.Placement == PlacementMode.Left
                && ToolTipService.GetPlacement(tabs[RightPanel.Search]) == PlacementMode.Left,
                "l'infobulle d'un onglet se pose à GAUCHE de l'onglet");
            Check(searchTip != null && ((string)searchTip.Content).Contains("Ctrl+Maj+F"),
                "…et enseigne le raccourci (« " + (searchTip == null ? "" : searchTip.Content) + " »)");

            // — Depuis le batch 41 l'ouverture atterrit sur l'Accueil : on
            //   revient explicitement au niveau projet (rien de sélectionné).
            Invoke(window, "OnBinderSelection", new object[] { null });
            DoEvents();

            // — Sans élément courant : Général grisé, Recherche vive.
            Check(tabs[RightPanel.Inspector].Opacity < 1 && tabs[RightPanel.Search].Opacity == 1,
                "sans élément courant, Général est grisé, Recherche disponible");
            Check(inspectorCol.Width.Value == 0, "…et la colonne est repliée (Général n'a rien à dire)");
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Inspector });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Inspector && inspectorCol.Width.Value == 0,
                "cliquer un onglet grisé ne fait rien");
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Inspector && inspectorCol.Width.Value == 0,
                "un panneau que le contexte n'offre pas ne devient jamais actif");

            // — Cliquer Recherche : la colonne s'ouvre, l'onglet porte l'accent.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Search });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Search && searchHost.Visibility == Visibility.Visible && inspectorCol.Width.Value > 0,
                "cliquer Recherche ouvre la colonne sur le panneau de recherche");
            Check(ReferenceEquals(tabs[RightPanel.Search].Background, Chrome.Accent)
                && !ReferenceEquals(tabs[RightPanel.Inspector].Background, Chrome.Accent),
                "l'onglet actif porte l'accent, les autres non");

            // — Cliquer l'actif : la colonne se replie.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Search });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.None && inspectorCol.Width.Value == 0 && searchHost.Visibility != Visibility.Visible,
                "cliquer l'onglet actif replie la colonne");
            var anyAccent = false;
            foreach (var pair in tabs) if (ReferenceEquals(pair.Value.Background, Chrome.Accent)) anyAccent = true;
            Check(!anyAccent, "…et plus aucun onglet ne porte l'accent");

            // — Un écrit : quatre onglets, Général et Correction disponibles.
            var opened = (Project)GetField(window, "_project");
            BinderItem item = null;
            foreach (var candidate in opened.AllItems()) if (candidate.Title == "Chapitre du rail") item = candidate;
            Invoke(window, "OnBinderSelection", new object[] { item });
            DoEvents();
            Check(tabs.Count == 4 && tabs.ContainsKey(RightPanel.Correction) && tabs.ContainsKey(RightPanel.Versions),
                "sur un écrit : quatre onglets (Général, Correction, Recherche, Versions)");
            Check(tabs[RightPanel.Inspector].Opacity == 1 && tabs[RightPanel.Correction].Opacity == 1,
                "avec un élément courant, Général et Correction sont disponibles");
            Check(inspectorCol.Width.Value == 0, "…la colonne reste repliée tant qu'on n'a rien demandé");

            // — Versions puis Correction : on change de panneau, pas de colonne.
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Versions });
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Versions && versionsHost.Visibility == Visibility.Visible,
                "cliquer Versions ouvre le panneau Versions");
            Invoke(window, "ClickRailTab", new object[] { RightPanel.Correction });
            DoEvents();
            var editor = (EditorView)GetField(window, "_editor");
            Invoke(editor, "RunCheck", null);
            DoEvents();
            Check(AppSettings.RightPanel == RightPanel.Correction && correctionHost.Visibility == Visibility.Visible && versionsHost.Visibility != Visibility.Visible,
                "cliquer Correction remplace Versions dans la même colonne");
            var badge = (Border)GetField(window, "_railBadge");
            var badgeText = (TextBlock)GetField(window, "_railBadgeText");
            var count = editor.FindingCount;
            Check(count > 0, "le pilote a des signalements (" + count + ")");
            Check(badge.Visibility == Visibility.Visible && badgeText.Text == count.ToString(),
                "la pastille de Correction affiche le nombre de signalements (« " + badgeText.Text + " »)");
            var tab = tabs[RightPanel.Correction];
            var origin = badge.TransformToAncestor(tab).Transform(new Point(0, 0));
            Check(origin.X >= 0 && origin.Y >= 0 && origin.X + badge.ActualWidth <= tab.ActualWidth + 0.5
                && origin.Y + badge.ActualHeight <= tab.ActualHeight + 0.5 && badge.ActualHeight >= 16,
                "…entière dans l'onglet, jamais rognée (" + badge.ActualWidth + "×" + badge.ActualHeight + " à " + origin.X + "," + origin.Y + ")");
            Check(ReferenceEquals(badge.Background, Chrome.PaperBg) && ReferenceEquals(badgeText.Foreground, Chrome.Danger),
                "…en danger (pas en accent), couleurs inversées sur l'onglet actif");

            RenderPng(rail, Path.Combine(Path.GetTempPath(), "marabook-b39-rail.png"), "rail");
            RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b39-fenetre.png"), "fenêtre");

            // — Une racine de la Pile : le rail rétrécit et Correction cède la place au Général.
            Invoke(window, "OnBinderSelection", new object[] { opened.Category(Project.KeyWritings) });
            DoEvents();
            Check(tabs.Count == 2 && !tabs.ContainsKey(RightPanel.Correction),
                "sur une racine : Général et Recherche seulement");
            Check(AppSettings.RightPanel == RightPanel.Inspector && inspector.Visibility == Visibility.Visible && correctionHost.Visibility != Visibility.Visible,
                "Correction, plus offerte, cède la place au Général");
            Invoke(window, "OnBinderSelection", new object[] { item });
            DoEvents();
            Check(tabs.Count == 4 && inspector.Visibility == Visibility.Visible,
                "de retour sur l'écrit, les quatre onglets reviennent, Général reste");

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
