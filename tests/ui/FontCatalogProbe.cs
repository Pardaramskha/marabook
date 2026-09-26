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
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde du catalogue de polices (0.50.0) sur vraie MainWindow
    /// hors écran : Préférences › « Catalogue de polices » — la liste avec
    /// l'exemple de narration, le compteur, le texte d'exemple qui remplace
    /// la phrase tel quel ; un favori double la police en tête de la liste et
    /// du sélecteur du ruban (étoile) ; une exclusion la retire du sélecteur ;
    /// le clic droit propose les mêmes options, libellées selon l'état ;
    /// les choix sont dans settings.json. Rend un PNG dans %TEMP%. Règle du
    /// batch 11 : settings.json est l'affaire de l'appelant (A1Probe) — ou de
    /// Main en solo.</summary>
    public static class FontCatalogProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
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
            }
            if (Application.Current != null) Application.Current.Shutdown();
            Console.WriteLine(_failures == 0 ? "Sonde catalogue de polices OK." : "*** SONDE CATALOGUE EN ECHEC : " + _failures);
            return _failures == 0 ? 0 : 1;
        }

        public static int Run()
        {
            Console.WriteLine("== Sonde du catalogue de polices (0.50.0)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-fonts");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "fonts.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE CATALOGUE EN ÉCHEC : " + error);
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
            var text = project.Category(Project.KeyWritings).Children[0];
            text.Title = "Écrit";
            text.Document = TextDocument.FromPlainText("Un paragraphe.");
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.DraftView = false;
            AppSettings.DarkTheme = false;
            AppSettings.FavoriteFonts = new List<string>();
            AppSettings.ExcludedFonts = new List<string>();
            AppSettings.RecentFonts = new List<string>();
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
                Height = 860,
                ShowInTaskbar = false
            };
            PreferencesDialog dialog = null;
            try
            {
                window.Show();
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                BinderItem target = null;
                foreach (var item in opened.AllItems()) if (item.Title == "Écrit") target = item;
                Invoke(window, "OnBinderSelection", new object[] { target });
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                var picker = (FontPicker)GetField(editor, "_fontCombo");
                var catalogCount = FontCatalog.Entries.Count;
                Check(catalogCount > 5, "le catalogue connaît des polices (" + catalogCount + ")");

                dialog = new PreferencesDialog(window, opened)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -2600,
                    Top = 0,
                    ShowInTaskbar = false
                };
                dialog.Show();
                DoEvents();
                var tabs = FindChild<TabControl>(dialog);
                TabItem catalogTab = null;
                foreach (TabItem tab in tabs.Items) if ((string)tab.Header == "Catalogue de polices") catalogTab = tab;
                Check(catalogTab != null, "Préférences a l'onglet « Catalogue de polices »");
                tabs.SelectedItem = catalogTab;
                DoEvents();
                var panel = FindChild<FontCatalogTab>(dialog);
                Check(panel != null, "l'onglet porte le catalogue");
                var list = FindChild<ListBox>(panel);
                Check(list != null && list.Items.Count == catalogCount, "une ligne par police (" + (list == null ? -1 : list.Items.Count) + ")");
                Check(panel.CountsText.Contains("0 favori") && panel.CountsText.Contains("0 exclusion"), "le compteur part de zéro (" + panel.CountsText + ")");
                var sampleBox = FindChild<TextBox>(panel);
                Check(sampleBox != null && sampleBox.Text == FontCatalogTab.DefaultSample, "le texte d'exemple par défaut est la phrase de narration");
                DoEvents();
                var firstRow = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                if (firstRow == null) { list.ScrollIntoView(list.Items[0]); DoEvents(); firstRow = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem; }
                Check(firstRow != null, "la première ligne est réalisée");
                var sampleBlock = FindTextBlock(firstRow, FontCatalogTab.DefaultSample);
                Check(sampleBlock != null, "la première ligne montre la phrase d'exemple dans sa police");
                Check(FindTextBlockEnding(firstRow, "style") != null || FindTextBlockEnding(firstRow, "styles") != null, "la ligne dit son nombre de styles");

                // — Le texte d'exemple personnalisé, tel quel.
                sampleBox.Text = "Ceci n'est pas -- corrige \"tel quel\"";
                DoEvents();
                Check(FindTextBlock(firstRow, "Ceci n'est pas -- corrige \"tel quel\"") != null, "le texte tapé remplace l'exemple sans mise en forme automatique");
                sampleBox.Text = "";
                DoEvents();
                Check(FindTextBlock(firstRow, FontCatalogTab.DefaultSample) != null, "vide : la phrase par défaut revient");

                // — Favori sur la première police : doublon en tête ici et dans le ruban.
                var first = (FontCatalogTab.FontRow)list.Items[0];
                var firstName = first.Name;
                var favoriteButton = FindButton(firstRow, "favorite");
                Check(favoriteButton != null, "la ligne a son bouton favori");
                favoriteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                DoEvents();
                Check(AppSettings.IsFavoriteFont(firstName), "le clic met la police en favori");
                Check(list.Items.Count == catalogCount + 1 && ((FontCatalogTab.FontRow)list.Items[0]).IsCopy
                    && ((FontCatalogTab.FontRow)list.Items[0]).Name == firstName, "un doublon de la favorite ouvre la liste");
                Check(panel.CountsText.Contains("1 favori"), "le compteur dit 1 favori (" + panel.CountsText + ")");
                var pickerFirst = picker.Items[0] as FontCatalog.Entry;
                Check(pickerFirst != null && pickerFirst.IsFavoriteCopy && pickerFirst.Name == firstName,
                    "le sélecteur du ruban ouvre sur le doublon étoilé de la favorite");
                Check(picker.Items.Count == catalogCount + 2, "…suivi d'un trait puis du catalogue");

                // — Exclure la deuxième police : elle quitte le sélecteur.
                var secondRow = list.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem;
                var second = (FontCatalogTab.FontRow)list.Items[2];
                var excludeButton = FindButton(secondRow, "exclude");
                Check(excludeButton != null, "la ligne a son bouton exclure");
                excludeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                DoEvents();
                Check(AppSettings.IsExcludedFont(second.Name) && panel.CountsText.Contains("1 exclusion"), "le clic exclut la police, le compteur suit");
                var inPicker = false;
                foreach (var item in picker.Items)
                {
                    var entry = item as FontCatalog.Entry;
                    if (entry != null && entry.Name == second.Name) inPicker = true;
                }
                Check(!inPicker && picker.Items.Count == catalogCount + 1, "la police exclue a quitté le sélecteur du ruban");
                Check(second.RowOpacity < 1, "la ligne exclue est estompée");

                // — Le clic droit : les options, libellées selon l'état.
                var menu = (ContextMenu)GetField(panel, "_menu");
                menu.PlacementTarget = FindChild<DockPanel>(secondRow);
                menu.IsOpen = true;
                DoEvents();
                var headers = new List<string>();
                foreach (var item in menu.Items) { var mi = item as MenuItem; if (mi != null) headers.Add((string)mi.Header); }
                menu.IsOpen = false;
                Check(headers.Count == 2 && headers[0] == "Favoris" && headers[1] == "Ne plus exclure",
                    "clic droit sur l'exclue : « Favoris » et « Ne plus exclure » (" + string.Join(" / ", headers.ToArray()) + ")");
                RenderPng((FrameworkElement)dialog.Content, Path.Combine(Path.GetTempPath(), "marabook-catalogue-polices.png"));

                // — Retirer le favori : le doublon s'en va, ici et dans le ruban.
                var copyRow = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                FindButton(copyRow, "favorite").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                DoEvents();
                Check(!AppSettings.IsFavoriteFont(firstName) && list.Items.Count == catalogCount && !((FontCatalogTab.FontRow)list.Items[0]).IsCopy,
                    "retirer le favori depuis son doublon efface le doublon");
                var pickerHead = picker.Items[0] as FontCatalog.Entry;
                Check(pickerHead != null && !pickerHead.IsFavoriteCopy, "le sélecteur du ruban n'a plus de favorite en tête");

                // — Persistance.
                var json = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json"));
                Check(json.Contains("\"excludedFonts\"") && json.Contains(second.Name), "l'exclusion est écrite dans settings.json");

                AppSettings.ToggleExcludedFont(second.Name); // remis (le fichier est restauré par l'appelant)
                SetField(window, "_dirty", false);
            }
            finally
            {
                if (dialog != null) dialog.Close();
                SetField(window, "_dirty", false);
                window.Close();
                DoEvents();
            }
        }

        // ------------------------------------------------------------ outillage WPF

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            if (root is T) return (T)root;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindChild<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static Button FindButton(DependencyObject root, string tag)
        {
            if (root == null) return null;
            var button = root as Button;
            if (button != null && Equals(button.Tag, tag)) return button;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindButton(VisualTreeHelper.GetChild(root, i), tag);
                if (found != null) return found;
            }
            return null;
        }

        private static TextBlock FindTextBlock(DependencyObject root, string text)
        {
            if (root == null) return null;
            var block = root as TextBlock;
            if (block != null && block.Text == text) return block;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindTextBlock(VisualTreeHelper.GetChild(root, i), text);
                if (found != null) return found;
            }
            return null;
        }

        private static TextBlock FindTextBlockEnding(DependencyObject root, string suffix)
        {
            if (root == null) return null;
            var block = root as TextBlock;
            if (block != null && block.Text.EndsWith(suffix)) return block;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindTextBlockEnding(VisualTreeHelper.GetChild(root, i), suffix);
                if (found != null) return found;
            }
            return null;
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                element.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
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

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
        }

        private static void SetField(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }
            throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
        }

        private static object Invoke(object target, string name, object[] args)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var method = target.GetType().GetMethod(name, flags | BindingFlags.DeclaredOnly)
                ?? target.GetType().GetMethod(name, flags);
            if (method == null)
                throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return method.Invoke(target, args);
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
