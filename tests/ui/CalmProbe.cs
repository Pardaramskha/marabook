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
    /// <summary>Sonde du mode calme et des menus contextuels (13/09) : un
    /// écrit ouvert en Pages (A5, guides de marges), puis le mode calme —
    /// la feuille doit être A4 nue (pas de guides, pas de marqueurs), avec
    /// de l'air en tête ; Échap dans la surface ne quitte plus la
    /// composition ; la sortie du calme rend l'axe d'avant. Les menus :
    /// « Note de bas de page » grisée sur l'écran du livre, active sur un
    /// écrit. Rendus PNG. settings.json sauvegardé/restauré.</summary>
    public static class CalmProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-calme");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "calme.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("SONDE CALME EN ÉCHEC : " + error);
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
            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "SONDE CALME OK" : _failures + " ÉCHEC(S)");
            return _failures == 0 ? 0 : 1;
        }

        private static void Probe(string path)
        {
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Title = "Le marabout";
            var text = "";
            for (var i = 0; i < 30; i++)
                text += "Le marabout veille sur le marais, patte repliée, l'œil mi-clos ; la nuit tombe et les grenouilles se taisent une à une (" + i + ").\n";
            chapter.Document = TextDocument.FromPlainText(text);
            // Pages : A5, guides de marges — ce que le calme doit IGNORER.
            project.Page.PageWidthMm = 148;
            project.Page.PageHeightMm = 210;
            project.Page.ShowMarginGuides = true;
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.DraftView = false;
            AppSettings.DarkTheme = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600, Top = 0, Width = 1280, Height = 800, ShowInTaskbar = false
            };
            try
            {
                window.OpenFile(path);
                window.Show();
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                BinderItem item = null, book = null;
                foreach (var candidate in opened.AllItems())
                {
                    if (candidate.Title == "Le marabout") item = candidate;
                    if (candidate.Kind == ItemKind.Book && book == null) book = candidate;
                }
                Invoke(window, "OnBinderSelection", new object[] { item });
                DoEvents();
                Thread.Sleep(200);
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                var composed = (ComposedView)GetField(editor, "_composed");
                var setupBefore = CurrentSetup(composed);
                Check(setupBefore != null && Math.Abs(setupBefore.PageWidthMm - 148) < 0.5,
                    "Pages : la feuille est A5 (" + (setupBefore == null ? "?" : setupBefore.PageWidthMm.ToString()) + " mm)");
                Check(setupBefore != null && setupBefore.ShowMarginGuides, "Pages : guides de marges affichés");

                // Les menus : Format → Note de bas de page active sur un écrit.
                var footnote = FindMenu(window, "Note de bas de page");
                Check(footnote != null && footnote.IsEnabled, "menu : « Note de bas de page » active sur un écrit");

                // Le mode calme.
                Invoke(window, "SetCalmMode", new object[] { true });
                DoEvents();
                Thread.Sleep(200);
                DoEvents();
                var calmSetup = CurrentSetup(composed);
                Check(calmSetup != null && Math.Abs(calmSetup.PageWidthMm - 210) < 0.5 && Math.Abs(calmSetup.PageHeightMm - 297) < 0.5,
                    "calme : la feuille est A4 (" + (calmSetup == null ? "?" : calmSetup.PageWidthMm + "×" + calmSetup.PageHeightMm) + ")");
                Check(calmSetup != null && !calmSetup.ShowMarginGuides, "calme : pas de guides de marges");
                Check(!ComposedRenderer.ShowWidowMarks, "calme : pas de marqueurs veuves/orphelines");
                Check(composed.CalmLook, "calme : ombre réelle et air en tête");
                var column = (FrameworkElement)GetField(composed, "_column");
                Check(column.Margin.Top >= 60, "calme : la première page est décollée du haut (" + column.Margin.Top + " px)");
                composed.ScrollToVerticalOffset(0);
                DoEvents();
                Thread.Sleep(100);
                DoEvents();
                Check(composed.VerticalOffset < 1, "calme : en haut du défilement (" + composed.VerticalOffset + ")");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b44-calme.png"));

                // Échap dans la surface : la composition reste (le classique est gelé).
                composed.Focus();
                composed.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(composed), 0,
                    System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
                DoEvents();
                Check(composed.Visibility == Visibility.Visible, "Échap dans la surface : la composition reste affichée");

                // Sortie du calme : retour à Pages, A5 et guides.
                Invoke(window, "SetCalmMode", new object[] { false });
                DoEvents();
                Thread.Sleep(200);
                DoEvents();
                var after = CurrentSetup(composed);
                Check(after != null && Math.Abs(after.PageWidthMm - 148) < 0.5 && after.ShowMarginGuides,
                    "sortie du calme : Pages A5 avec guides, comme avant");
                Check(ComposedRenderer.ShowWidowMarks && !composed.CalmLook, "sortie du calme : marqueurs et ombre d'avant");

                // Le Brouillon, puis Échap : le Brouillon reste.
                Invoke(editor, "SetDraftView", new object[] { true });
                DoEvents();
                Thread.Sleep(200);
                DoEvents();
                var draft = CurrentSetup(composed);
                Check(draft != null && Math.Abs(draft.PageWidthMm - 165) < 0.5, "Brouillon : tranche de 165 mm");
                composed.Focus();
                composed.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(composed), 0,
                    System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
                DoEvents();
                Check(composed.Visibility == Visibility.Visible && Math.Abs(CurrentSetup(composed).PageWidthMm - 165) < 0.5,
                    "Brouillon + Échap : le Brouillon reste");
                Invoke(editor, "SetDraftView", new object[] { false });
                DoEvents();

                // La typographie à la frappe (b45) : on tape dans la surface
                // composée, les signes se corrigent sous le curseur.
                AppSettings.TypographyLiveEnabled = true;
                AppSettings.TypographyLive = AppSettings.DefaultLiveTypography();
                Invoke(editor, "RefreshProofing", null);
                DoEvents();
                composed.Focus();
                var paragraphs = ((Project)GetField(window, "_project")).FindByTitle("Le marabout").Document.Paragraphs;
                var last = paragraphs.Count - 1;
                composed.PlaceCaret(last, PivotEdit.FlatLength(paragraphs[last]), false);
                composed.TypeText(" ");
                foreach (var ch in "Il dit \"bonjour\" et partit...")
                    composed.TypeText(ch.ToString());
                DoEvents();
                var typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.Contains("«") && typed.Contains("»"), "frappe : les guillemets droits deviennent « » (" + typed + ")");
                Check(typed.EndsWith("…"), "frappe : les trois points deviennent des points de suspension");
                int caretParagraph, caretOffset;
                composed.CaretLocation(out caretParagraph, out caretOffset);
                Check(caretOffset == typed.Length, "frappe : le curseur reste en fin de texte (" + caretOffset + "/" + typed.Length + ")");
                composed.TypeText(" ");
                composed.TypeText(" ");
                DoEvents();
                typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.EndsWith("…  "), "frappe : les espaces tapés ne sont pas effacés sous les doigts");

                // Ctrl+Z sur une correction automatique : refusée, elle ne
                // revient pas à la frappe suivante — jusqu'à ce qu'on efface
                // et retape les caractères.
                foreach (var ch in " et \"non\"") composed.TypeText(ch.ToString());
                DoEvents();
                typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.EndsWith("»") && !typed.Contains("\"non"), "frappe : une nouvelle paire devient « » (" + typed + ")");
                composed.Undo();
                DoEvents();
                typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.EndsWith("\"non\""), "Ctrl+Z : la correction est défaite, la frappe reste (" + typed + ")");
                composed.TypeText(" ");
                DoEvents();
                typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.EndsWith("\"non\" "), "un espace après Ctrl+Z : la correction ne revient PAS (" + typed + ")");
                // On efface le guillemet fermant et on le retape : corrigé.
                Invoke(composed, "Backspace", null);
                Invoke(composed, "Backspace", null);
                DoEvents();
                composed.TypeText("\"");
                DoEvents();
                typed = PivotEdit.FlatText(paragraphs[last]);
                Check(typed.EndsWith("»") && !typed.Contains("\"non"), "retapé : la correction revient (" + typed + ")");

                // Un dialogue sur deux paragraphes, tapé au clavier : l'ouvrant
                // seul en tête, le fermant seul en fin, un mot cité entre.
                composed.InsertParagraphBreak();
                foreach (var ch in "\" Hello ! Comment ça va ?") composed.TypeText(ch.ToString());
                composed.InsertParagraphBreak();
                foreach (var ch in "— Je sais pas si \"aller\" est pertinent.\"") composed.TypeText(ch.ToString());
                DoEvents();
                var firstLine = PivotEdit.FlatText(paragraphs[paragraphs.Count - 2]);
                var secondLine = PivotEdit.FlatText(paragraphs[paragraphs.Count - 1]);
                Check(firstLine.StartsWith("«"), "dialogue tapé : l'ouvrant seul en tête devient « (" + firstLine + ")");
                Check(secondLine.Contains("“aller”"), "dialogue tapé : le mot cité passe en courbes (" + secondLine + ")");
                Check(secondLine.EndsWith("»"), "dialogue tapé : le fermant seul en fin devient »");

                // L'incise après la réplique : « mon ami, » rétorqua l'autre.
                composed.InsertParagraphBreak();
                foreach (var ch in "\" Alors, continua-t-il, comment ça se passe ?") composed.TypeText(ch.ToString());
                composed.InsertParagraphBreak();
                foreach (var ch in "— On se fait chier, mon ami,\" rétorqua l'autre.") composed.TypeText(ch.ToString());
                DoEvents();
                var replyLine = PivotEdit.FlatText(paragraphs[paragraphs.Count - 1]);
                Check(replyLine.Contains("ami,\u00A0» rétorqua") && !replyLine.Contains("\""),
                    "incise tapée : « mon ami, » rétorqua (" + replyLine + ")");

                // Les menus sur l'écran du livre : Note de bas de page grisée.
                if (book != null)
                {
                    Invoke(window, "OnBinderSelection", new object[] { book });
                    DoEvents();
                    Thread.Sleep(100);
                    DoEvents();
                    Check(footnote != null && !footnote.IsEnabled, "menu : « Note de bas de page » grisée sur l'écran du livre");
                    var rename = FindMenu(window, "Renommer…");
                    Check(rename != null && rename.IsEnabled, "menu : « Renommer… » active sur un livre");
                }
                else Console.WriteLine("  (pas de livre dans le projet neuf : test du livre sauté)");
                var home = opened.Category(Project.KeyHome);
                if (home != null)
                {
                    Invoke(window, "OnBinderSelection", new object[] { home });
                    DoEvents();
                    Thread.Sleep(100);
                    DoEvents();
                    var rename = FindMenu(window, "Renommer…");
                    Check(rename != null && !rename.IsEnabled, "menu : « Renommer… » grisée sur l'Accueil (racine)");
                    Check(footnote != null && !footnote.IsEnabled, "menu : « Note de bas de page » grisée sur l'Accueil");
                }
            }
            finally
            {
                // La frappe a sali le projet : on ne veut pas le dialogue
                // « Enregistrer ? » (modal, la sonde attendrait un clic).
                try
                {
                    var dirty = typeof(MainWindow).GetField("_dirty", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (dirty != null) dirty.SetValue(window, false);
                }
                catch { }
                try { window.Close(); } catch { }
                DoEvents();
            }
        }

        private static PageSetup CurrentSetup(ComposedView composed)
        {
            var engine = GetField(composed, "_engine");
            if (engine == null) return null;
            var current = Member(engine, "Current");
            if (current == null) return null;
            return (PageSetup)Member(current, "Setup");
        }

        /// <summary>Propriété ou champ, public ou non, par son nom.</summary>
        private static object Member(object target, string name)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = target.GetType();
            while (type != null)
            {
                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(target, null);
                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            return null;
        }

        private static MenuItem FindMenu(Window window, string header)
        {
            var menu = FindMenuBar(window.Content as DependencyObject);
            if (menu == null) return null;
            foreach (var top in menu.Items)
            {
                var found = FindIn(top as MenuItem, header);
                if (found != null) return found;
            }
            return null;
        }

        private static MenuItem FindIn(MenuItem item, string header)
        {
            if (item == null) return null;
            if (Equals(item.Header, header)) return item;
            foreach (var child in item.Items)
            {
                var found = FindIn(child as MenuItem, header);
                if (found != null) return found;
            }
            return null;
        }

        private static Menu FindMenuBar(DependencyObject root)
        {
            if (root == null) return null;
            var menu = root as Menu;
            if (menu != null) return menu;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindMenuBar(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            if (width <= 0 || height <= 0) return;
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("  (rendu : " + path + ")");
        }

        private static object GetField(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            return null;
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

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }
    }
}
