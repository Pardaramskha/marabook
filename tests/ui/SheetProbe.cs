using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde du batch 31 — les fiches refondues, sur vraie
    /// MainWindow hors écran : la catégorie « Fiches » ouvre la
    /// BIBLIOTHÈQUE (rangées par catégorie, cartes, recherche) ; une fiche
    /// s'ouvre sur son corps MARKDOWN ; l'aperçu wiki rend le markdown ;
    /// et l'aller-retour disque conserve catégorie et source. Règle du
    /// batch 11 : settings.json est l'affaire de l'appelant (A1Probe).</summary>
    public static class SheetProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b31");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b31.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE FICHES EN ÉCHEC : " + error);
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
            // — Un projet neuf (7 catégories semées) + deux fiches :
            // Kaladin (Personnage) et Kholinar (Lieu, mais au modèle
            // PERSONNAGE — la puce « modèle différent » doit apparaître).
            var project = Project.CreateNew();
            var character = project.SheetCategories[0]; // Personnage
            var place = project.SheetCategories[1];     // Lieu
            var kaladin = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Kaladin",
                CategoryId = character.Id,
                TemplateId = character.TemplateId
            };
            kaladin.Document = TextDocument.FromPlainText(
                "# Chef de pont\n\n- [ ] retrouver [[Syl]]\n\n**Quatrième** pont.");
            var kholinar = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Kholinar",
                CategoryId = place.Id,
                TemplateId = character.TemplateId // divergent à dessein
            };
            var sheetsRoot = project.Category(Project.KeySheets);
            sheetsRoot.Children.Add(kaladin);
            sheetsRoot.Children.Add(kholinar);
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
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

            var opened = (Project)GetField(window, "_project");
            Check(opened.SheetCategories.Count == 7,
                "les 7 catégories ont survécu à l'aller-retour disque (v11)");

            // — La catégorie « Fiches » ouvre la bibliothèque.
            Invoke(window, "OnBinderSelection",
                new object[] { opened.Category(Project.KeySheets) });
            DoEvents();
            var library = (SheetLibraryView)GetField(window, "_sheetLibrary");
            Check(library.Visibility == Visibility.Visible,
                "la catégorie Fiches ouvre la bibliothèque");
            Check(CountCards(library) == 2,
                "deux cartes-fiches affichées (obtenu : " + CountCards(library) + ")");

            // — La recherche filtre par nom, toutes catégories confondues.
            var search = (TextBox)GetField(library, "_searchBox");
            search.Text = "khol";
            DoEvents();
            Check(CountCards(library) == 1,
                "la recherche « khol » ne garde que Kholinar");
            search.Text = "";
            DoEvents();

            // — Une fiche s'ouvre sur sa SOURCE markdown.
            BinderItem sheet = null;
            foreach (var item in opened.AllItems())
                if (item.Title == "Kaladin") sheet = item;
            Invoke(window, "OnBinderSelection", new object[] { sheet });
            DoEvents();
            var sheetView = (SheetView)GetField(window, "_sheetView");
            Check(sheetView.Visibility == Visibility.Visible,
                "la fiche s'ouvre dans la vue fiche");
            var body = (TextBox)GetField(sheetView, "_bodyBox");
            Check(body.Text.StartsWith("# Chef de pont"),
                "le corps montre la source markdown");

            // — Le modèle Personnage groupé : les intertitres Infos/Physique.
            var fields = (StackPanel)GetField(sheetView, "_fieldsPanel");
            var infos = false;
            var physique = false;
            foreach (var child in fields.Children)
            {
                var text = child as TextBlock;
                if (text == null) continue;
                if (text.Text == "Infos") infos = true;
                if (text.Text == "Physique") physique = true;
            }
            Check(infos && physique,
                "les groupes Infos et Physique s'affichent en intertitres");

            // — L'aperçu wiki rend le markdown (le bouton réel).
            var toggle = (ToggleButton)GetField(sheetView, "_previewToggle");
            toggle.IsChecked = true;
            DoEvents();
            var preview = (ScrollViewer)GetField(sheetView, "_preview");
            Check(preview.Visibility == Visibility.Visible && preview.Content != null,
                "l'aperçu wiki s'affiche, markdown rendu");
            toggle.IsChecked = false;
            DoEvents();

            // — Frappe + Ctrl+S : la source et la catégorie sur le disque.
            body.CaretIndex = body.Text.Length;
            body.SelectedText = "\nSONDE-B31";
            DoEvents();
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloaded = PlotFile.Load(path);
            BinderItem back = null;
            foreach (var item in reloaded.AllItems())
                if (item.Title == "Kaladin") back = item;
            Check(back != null
                && back.Document.ToPlainText().Contains("SONDE-B31"),
                "la source markdown éditée est sur le disque");
            Check(back != null && back.CategoryId == character.Id,
                "la catégorie de la fiche a survécu à l'enregistrement");

            window.Close();
            DoEvents();
        }

        /// <summary>Compte les cartes de la bibliothèque (les Border cliquables
        /// des WrapPanel de rangées).</summary>
        private static int CountCards(SheetLibraryView library)
        {
            var rows = (StackPanel)GetField(library, "_rows");
            var count = 0;
            foreach (var child in rows.Children)
            {
                var wrap = child as WrapPanel;
                if (wrap == null) continue;
                foreach (var card in wrap.Children)
                    if (card is Border) count++;
            }
            return count;
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
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
