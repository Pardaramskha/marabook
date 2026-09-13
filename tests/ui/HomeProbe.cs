using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UniversSale.History;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde du batch 41 — l'Accueil, sur vraie MainWindow hors
    /// écran : un projet neuf s'ouvre sur l'Accueil (quatre blocs, invites),
    /// un écrit ouvert devient un récent cliquable qui rouvre le bon item,
    /// une épingle apparaît puis disparaît d'un Ctrl+Z, la barre d'un livre
    /// est là, relire ne salit pas le projet (A1), la réouverture atterrit
    /// sur le dernier item sans réécrire sa date (A2). Rendu PNG dans les
    /// deux thèmes. Jamais de clic synthétique. Règle du batch 11 :
    /// settings.json est l'affaire de l'appelant (A1Probe).</summary>
    public static class HomeProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b41");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b41.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE ACCUEIL EN ÉCHEC : " + error);
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
            var writings = project.Category(Project.KeyWritings);
            var chapter = writings.Children[0];
            chapter.Title = "Chapitre un";
            chapter.Document = TextDocument.FromPlainText("Le marabout dort sur la rive.");
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo { ChapterGoal = 3 } };
            var first = new BinderItem { Kind = ItemKind.Text, Title = "I", Status = "done", Page = book.Book.Template.Clone() };
            first.Document = TextDocument.FromPlainText("Premier.");
            book.Children.Add(first);
            writings.Children.Add(book);
            var category = project.SheetCategories[0];
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche héros", CategoryId = category.Id, TemplateId = category.TemplateId };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            var savedDark = AppSettings.DarkTheme;
            AppSettings.RightPanel = RightPanel.Inspector;
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
                var home = (HomeView)GetField(window, "_homeView");
                var binder = (BinderView)GetField(window, "_binder");
                var history = (HistoryManager)GetField(window, "_history");
                BinderItem chapterItem = null, sheetItem = null;
                foreach (var candidate in opened.AllItems())
                {
                    if (candidate.Title == "Chapitre un") chapterItem = candidate;
                    if (candidate.Title == "Fiche héros") sheetItem = candidate;
                }

                // — Un projet sans récent s'ouvre sur l'Accueil : trois blocs, les invites.
                var current = (BinderItem)GetField(window, "_current");
                Check(current != null && current.IsHomeRoot && home.Visibility == Visibility.Visible,
                    "un projet sans récent s'ouvre sur l'Accueil");
                var grid = (Grid)GetField(home, "_grid");
                Check(grid.Children.Count == 3, "trois blocs, pas un de plus (Commencer vit dans le rail, b43)");
                var startSection = (StackPanel)GetField(window, "_homeStartSection");
                Check(startSection.Visibility == Visibility.Visible,
                    "le Général de l'Accueil montre les raccourcis « Commencer »");
                var prompts = home.Prompts;
                Check(prompts.Count == 2 && prompts.Contains("Les écrits ouverts récemment apparaîtront ici.") && prompts.Contains("Clic droit sur un élément → Épingler."),
                    "Reprendre et Épinglés montrent leur invite ; Où j'en suis a déjà le livre");
                Check(home.ProgressBars == 1, "la barre de progression du livre est là");
                Check(opened.Recents.Count == 0 && !(bool)GetField(window, "_dirty"), "l'Accueil lui-même n'est pas un récent, et rien n'est sali");

                // — Ouvrir un écrit : un récent, sans salir le projet (A1).
                Invoke(window, "OnBinderSelection", new object[] { chapterItem });
                DoEvents();
                Check(opened.Recents.Count == 1 && opened.Recents[0].ItemId == chapterItem.Id, "ouvrir un écrit l'inscrit en tête des récents");
                Check(!(bool)GetField(window, "_dirty"), "…sans MarkDirty : relire n'est pas modifier (A1)");
                binder.SelectItem(opened.Category(Project.KeyHome).Id);
                DoEvents();
                Check(home.Visibility == Visibility.Visible && home.ResumeItems.Count == 1 && home.ResumeItems[0] == chapterItem,
                    "de retour sur l'Accueil, Reprendre montre l'écrit");

                // — Cliquer le récent rouvre le bon item.
                home.ClickRow(chapterItem);
                DoEvents();
                current = (BinderItem)GetField(window, "_current");
                Check(current == chapterItem && home.Visibility != Visibility.Visible, "cliquer un récent ouvre l'écrit");

                // — Épingler une fiche, la retrouver, l'annuler d'un Ctrl+Z.
                binder.TogglePin(sheetItem);
                DoEvents();
                Check(sheetItem.Pinned && (bool)GetField(window, "_dirty"), "épingler pose l'épingle et salit le projet (une vraie modification)");
                binder.SelectItem(opened.Category(Project.KeyHome).Id);
                DoEvents();
                Check(home.PinnedItems.Count == 1 && home.PinnedItems[0] == sheetItem, "Épinglés montre la fiche");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b41-accueil-clair.png"));
                AppSettings.DarkTheme = true;
                Invoke(window, "ApplyAppearance", null);
                DoEvents();
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-b41-accueil-sombre.png"));
                AppSettings.DarkTheme = false;
                Invoke(window, "ApplyAppearance", null);
                DoEvents();
                history.Undo();
                DoEvents();
                Check(!sheetItem.Pinned && home.PinnedItems.Count == 0 && home.Prompts.Contains("Clic droit sur un élément → Épingler."),
                    "Ctrl+Z retire l'épingle et l'Accueil affiché le reflète");

                // — A2 : à la réouverture, le dernier item ouvert est sélectionné,
                //   par le chemin normal, SANS que sa date soit réécrite.
                opened.Recents[0].Date = "2026-08-24 09:00:00";
                Invoke(window, "DoSave", null);
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var reopened = (Project)GetField(window, "_project");
                current = (BinderItem)GetField(window, "_current");
                Check(current != null && current.Title == "Chapitre un", "à la réouverture, le dernier écrit ouvert est sélectionné");
                Check(reopened.Recents.Count == 1 && reopened.Recents[0].Date == "2026-08-24 09:00:00",
                    "…sans réécrire la date du récent (A2)");
                var inspectorCol = (ColumnDefinition)GetField(window, "_inspectorCol");
                Check(inspectorCol.Width.Value > 0, "…et la colonne de droite est sortie du niveau projet");
                Check(!(bool)GetField(window, "_dirty"), "…projet propre");
            }
            finally
            {
                AppSettings.DarkTheme = savedDark;
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
