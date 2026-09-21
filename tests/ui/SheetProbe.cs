using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
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

            // — Le modèle Personnage groupé : Infos et Physique se répartissent
            // entre les papers Informations et Apparence (refonte batch 34).
            var sections = (System.Collections.Generic.Dictionary<string, StackPanel>)GetField(sheetView, "_sectionPanels"); // papers par section (b42)
            var infoFields = sections[""];
            var looksFields = sections[SheetDefaults.GroupLooks];
            Check(infoFields.Children.Count > 1 && looksFields.Children.Count > 1,
                "les groupes Infos et Physique remplissent Informations et Apparence");

            // — Batch 36 : deux onglets, Général (les papers) et Texte libre
            // (l'éditeur) ; la fiche s'ouvre sur Général.
            var tabs = (TabControl)GetField(sheetView, "_tabs");
            Check(tabs.Items.Count == 2
                && (string)((TabItem)tabs.Items[0]).Header == "Général"
                && (string)((TabItem)tabs.Items[1]).Header == "Texte libre",
                "deux onglets : Général, Texte libre");
            Check(tabs.SelectedIndex == 0, "la fiche s'ouvre sur l'onglet Général");
            Check(looksFields.Children.Count == SheetDefaults.CharacterLooks.Length,
                "l'apparence par défaut du personnage : " + SheetDefaults.CharacterLooks.Length
                + " champs (obtenu : " + looksFields.Children.Count + ")");

            // — Une relation vers une autre fiche se reflète sur celle-ci :
            // Kaladin (genre non renseigné) déclare Kholinar comme Père →
            // Kholinar reçoit « Enfant ». Le sélecteur de nature réel commet.
            BinderItem linked = null;
            foreach (var item in opened.AllItems())
                if (item.Title == "Kholinar") linked = item;
            var relation = new SheetRelation { Kind = "", TargetId = linked.Id };
            sheet.Relations.Add(relation);
            Invoke(sheetView, "RebuildRelations", null);
            DoEvents();
            var relationsPanel = (StackPanel)GetField(sheetView, "_relationsPanel");
            ComboBox kindBox = null;
            foreach (var child in ((DockPanel)relationsPanel.Children[0]).Children)
            {
                var combo = child as ComboBox;
                if (combo != null && combo.Items.Contains("Père")) { kindBox = combo; break; }
            }
            Check(kindBox != null && kindBox.Items.Contains("Adelphe")
                && (string)kindBox.Items[kindBox.Items.Count - 1] == "＋  Nouvelle nature…",
                "le sélecteur propose les natures livrées puis « Nouvelle nature… »");
            kindBox.Text = "père";
            Invoke(sheetView, "CommitRelationKind", new object[] { relation, kindBox });
            DoEvents();
            Check(relation.Kind == "Père", "la nature saisie est canonisée (père → Père)");
            Check(linked.Relations.Count == 1 && linked.Relations[0].Kind == "Enfant"
                && linked.Relations[0].TargetId == sheet.Id,
                "la fiche liée reçoit le reflet « Enfant » (genre inconnu)");

            // — Le paper flottant de généalogie : le bouton réel, une fenêtre,
            // des boîtes dessinées (Kaladin + Kholinar), rafraîchie à la frappe.
            var genealogyButton = (Button)GetField(sheetView, "_genealogyButton");
            Check(genealogyButton.IsEnabled, "le bouton Généalogie est actif");
            genealogyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DoEvents();
            var genealogy = (GenealogyWindow)GetField(sheetView, "_genealogy");
            Check(genealogy != null && genealogy.IsVisible && genealogy.Shows(sheet),
                "le paper flottant de généalogie s'ouvre sur la fiche");
            var canvas = (Canvas)GetField(genealogy, "_canvas");
            Check(CountBoxes(canvas) == 2, "deux boîtes dessinées : le personnage et son père (obtenu : " + CountBoxes(canvas) + ")");
            sheet.Relations.Add(new SheetRelation { Kind = "Sœur", Name = "Syl" });
            sheet.Relations.Add(new SheetRelation { Kind = "Femme", Name = "Shallan" });
            sheet.Relations.Add(new SheetRelation { Kind = "Grand-mère", Name = "Hesina" });
            sheet.Relations.Add(new SheetRelation { Kind = "Fils", Name = "Oroden" });
            sheet.Relations.Add(new SheetRelation { Kind = "Mentor", Name = "Tukks" });
            Invoke(sheetView, "NotifyEdited", null);
            DoEvents();
            Check(CountBoxes(canvas) == 7, "cinq relations de plus : l'arbre se rafraîchit (obtenu : " + CountBoxes(canvas) + ")");
            Snapshot(canvas, Path.Combine(Path.GetTempPath(), "marabook-b36-genealogie.png"));
            Snapshot(sheetView, Path.Combine(Path.GetTempPath(), "marabook-b36-fiche.png"));
            genealogy.Close();
            DoEvents();
            Check(GetField(sheetView, "_genealogy") == null, "fermée, la fenêtre est oubliée");

            // — L'aperçu wiki rend le markdown (le bouton réel).
            var toggle = (ToggleButton)GetField(sheetView, "_previewToggle");
            toggle.IsChecked = true;
            DoEvents();
            var preview = (ScrollViewer)GetField(sheetView, "_preview");
            Check(preview.Visibility == Visibility.Visible && preview.Content != null,
                "l'aperçu wiki s'affiche, markdown rendu");
            toggle.IsChecked = false;
            DoEvents();

            // — Sections extras (21/09) : désactivées par défaut, ni Suivi ni
            //   Évolution sur la fiche ; activées sur le modèle, les deux
            //   papers viennent, le filtre du suivi dit « Tout le livre », une
            //   étape libre a son champ de nom ; le wiki rend des papers
            //   séparés (Relations, Évolution et présence, graph).
            var presencePaper = (Border)GetField(sheetView, "_presencePaper");
            var evolutionPaper = (Border)GetField(sheetView, "_evolutionPaper");
            Check(presencePaper.Parent == null && evolutionPaper.Parent == null,
                "sections extras désactivées par défaut : ni Suivi ni Évolution sur la fiche");
            var characterTemplate = opened.FindTemplate(sheet.TemplateId);
            characterTemplate.Tracking = true;
            characterTemplate.Evolution = true;
            sheetView.LoadItem(sheet, characterTemplate);
            DoEvents();
            Check(presencePaper.Parent != null && evolutionPaper.Parent != null,
                "suivi et évolution activés sur le modèle : les deux papers sont là");
            var filter = (ComboBox)GetField(sheetView, "_presenceFilter");
            Check(filter.Items.Count >= 1 && filter.SelectedIndex == 0
                && (string)((ComboBoxItem)filter.Items[0]).Content == "Tout le livre",
                "le filtre du suivi propose « Tout le livre » par défaut (entrées : " + filter.Items.Count + ")");
            Invoke(sheetView, "AddStep", null);
            DoEvents();
            var evolutionPanel = (StackPanel)GetField(sheetView, "_evolutionPanel");
            Check(sheet.Evolution.Count == 1 && sheet.Evolution[0].TextId == null && CountTextBoxes(evolutionPanel) == 2,
                "une étape libre : un champ pour la nommer et un pour la note (zones : " + CountTextBoxes(evolutionPanel) + ")");
            sheet.Evolution[0].Title = "Enfance";
            sheet.Evolution[0].Note = "grandit sur les hauts plateaux";
            characterTemplate.Radar = true;
            foreach (var axisName in SheetTemplate.DefaultRadarAxes) characterTemplate.RadarAxes.Add(new RadarAxis { Name = axisName });
            sheet.RadarValues[characterTemplate.RadarAxes[0].Id] = 4;
            foreach (var field in characterTemplate.Fields)
                if (field.Name == "Prénom") sheet.FieldValues[field.Id] = "Kal"; // un champ rempli : l'infobox a de quoi paraître
            sheetView.LoadItem(sheet, characterTemplate);
            DoEvents();
            Check(tabs.Items.Count == 3 && (string)((TabItem)tabs.Items[2]).Header == SheetTemplate.DefaultRadarName,
                "le graph statistique activé : un troisième onglet « " + SheetTemplate.DefaultRadarName + " »");
            toggle.IsChecked = true;
            DoEvents();
            var wikiPage = preview.Content as FrameworkElement;
            var wikiPapers = CountFrames(wikiPage);
            Check(wikiPapers == 4, "le wiki rend quatre papers : infobox, Relations, Évolution et présence, graph (obtenu : " + wikiPapers + ")");
            Snapshot(wikiPage, Path.Combine(Path.GetTempPath(), "marabook-2109-wiki.png"));
            toggle.IsChecked = false;
            DoEvents();
            // L'éditeur de modèles, onglet « Sections extras », rendu hors écran.
            var dialogCtor = typeof(TemplatesDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var dialog = (Window)dialogCtor.Invoke(new object[] { window, opened.Templates, opened });
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -2600;
            dialog.Top = 0;
            dialog.Show();
            DoEvents();
            var dialogTabs = FindTabControl(dialog);
            Check(dialogTabs != null && dialogTabs.Items.Count == 4
                && (string)((TabItem)dialogTabs.Items[1]).Header == "Sections extras"
                && (string)((TabItem)dialogTabs.Items[3]).Header == "Graph statistique",
                "l'éditeur de modèles : Sections, Sections extras, Champs, Graph statistique");
            if (dialogTabs != null)
            {
                dialogTabs.SelectedIndex = 1;
                DoEvents();
                Snapshot((FrameworkElement)dialog.Content, Path.Combine(Path.GetTempPath(), "marabook-2109-modeles-extras.png"));
            }
            dialog.Close();
            DoEvents();

            // — Un module (DLC, 22/09) installé dans un dossier de sonde
            //   (jamais celui de l'utilisateur) : le vrai paquet FPDM du dépôt
            //   voisin s'il est là, sinon un module de démonstration. La fiche
            //   Personnage gagne le bouton, le clic crée l'onglet et ses
            //   champs ; la bibliothèque marque la fiche d'une puce.
            var moduleRoot = Path.Combine(Path.GetTempPath(), "marabook-sonde-dlc-" + Guid.NewGuid().ToString("N"));
            Modules.RootOverride = moduleRoot;
            try
            {
                var real = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\marabook-dlc-fpdm\fpdm.mdlc"));
                var package = real;
                if (!File.Exists(real))
                {
                    package = Path.Combine(moduleRoot, "demo.mdlc");
                    Directory.CreateDirectory(moduleRoot);
                    Modules.Pack("{\"id\":\"demo\",\"name\":\"Démo\",\"sheet\":{\"categories\":[\"Personnage\"],\"papers\":[{\"title\":\"Général\",\"fields\":[{\"id\":\"a\",\"label\":\"Champ A\",\"hint\":\"indication\"}]}]}}", null, package);
                }
                Modules.Load();
                Check(Modules.Installed.Count == 0, "sans module installé, rien");
                sheetView.LoadItem(sheet, characterTemplate);
                DoEvents();
                var moduleButtons = (StackPanel)GetField(sheetView, "_moduleButtons");
                Check(moduleButtons.Children.Count == 0 && tabs.Items.Count == 3, "…ni bouton ni onglet de module sur la fiche");
                var module = ModuleStore.InstallFromFile(package); // Modules.Changed → la fenêtre recharge la fiche
                DoEvents();
                Check(Modules.IsInstalled(module.Id) && moduleButtons.Children.Count == 1,
                    "module « " + module.Name + " » installé : le bouton « " + module.Button + " » est au bandeau de la fiche Personnage");
                var libraryCardsBefore = CountModuleDots(library);
                sheetView.CreateModuleSheet(module);
                DoEvents();
                Check(tabs.Items.Count == 4 && (string)((TabItem)tabs.Items[3]).Header == module.Tab && tabs.SelectedIndex == 3,
                    "le clic crée la fiche de module : un onglet « " + module.Tab + " » s'ouvre");
                Check(moduleButtons.Children.Count == 0, "…et le bouton disparaît");
                var moduleView = FindModuleView((DependencyObject)((TabItem)tabs.Items[3]).Content);
                var boxes = moduleView == null ? 0 : CountTextBoxes(moduleView);
                Check(moduleView != null && boxes == module.ValueIds().Count,
                    "l'onglet porte une zone par valeur attendue (" + boxes + " / " + module.ValueIds().Count + ")");
                var firstBox = FirstTextBox(moduleView);
                firstBox.Text = "Sonde";
                DoEvents();
                Check(Modules.FilledCount(module, sheet) == 1, "la frappe va dans la fiche de module (1 valeur remplie)");
                Snapshot((FrameworkElement)sheetView, Path.Combine(Path.GetTempPath(), "marabook-2209-fiche-module.png"));
                Invoke(window, "OnBinderSelection", new object[] { sheet.Parent });
                DoEvents();
                Check(CountModuleDots(library) == libraryCardsBefore + 1, "la bibliothèque marque la fiche d'une puce de module");
                Invoke(window, "OnBinderSelection", new object[] { sheet });
                DoEvents();
                Modules.Uninstall(module.Id);
                DoEvents();
                Check(tabs.Items.Count == 3 && sheet.ModuleValues.ContainsKey(module.Id),
                    "désinstallé : l'onglet s'en va, la fiche garde ses valeurs");
                sheet.ModuleValues.Clear();
            }
            finally
            {
                Modules.RootOverride = null;
                Modules.Load();
                try { Directory.Delete(moduleRoot, true); } catch { }
            }
            sheet.Evolution.Clear();
            sheet.RadarValues.Clear();
            characterTemplate.Tracking = false;
            characterTemplate.Evolution = false;
            characterTemplate.Radar = false;
            characterTemplate.RadarAxes.Clear();
            sheetView.LoadItem(sheet, characterTemplate);
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

        /// <summary>Les puces de module des cartes de la bibliothèque (le
        /// Border rond à gauche, infobulle « Fiche … »).</summary>
        private static int CountModuleDots(DependencyObject root)
        {
            var count = 0;
            var border = root as Border;
            if (border != null && border.ToolTip is string && ((string)border.ToolTip).StartsWith("Fiche ")) count++;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject) count += CountModuleDots((DependencyObject)child);
            return count;
        }

        private static ModuleSheetView FindModuleView(DependencyObject root)
        {
            if (root is ModuleSheetView) return (ModuleSheetView)root;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindModuleView((DependencyObject)child) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static TextBox FirstTextBox(DependencyObject root)
        {
            if (root is TextBox) return (TextBox)root;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FirstTextBox((DependencyObject)child) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static int CountTextBoxes(DependencyObject root)
        {
            var count = root is TextBox ? 1 : 0;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject) count += CountTextBoxes((DependencyObject)child);
            return count;
        }

        /// <summary>Les papers du wiki en mode page : la colonne de droite
        /// (le StackPanel de 250 px) et ce qu'elle empile.</summary>
        private static int CountFrames(DependencyObject root)
        {
            if (root == null) return 0;
            var stack = root as StackPanel;
            if (stack != null && Math.Abs(stack.Width - 250) < 0.5) return stack.Children.Count;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? CountFrames((DependencyObject)child) : 0;
                if (found > 0) return found;
            }
            return 0;
        }

        private static TabControl FindTabControl(DependencyObject root)
        {
            if (root is TabControl) return (TabControl)root;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindTabControl((DependencyObject)child) : null;
                if (found != null) return found;
            }
            return null;
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

        /// <summary>Rendu PNG d'un élément (VisualBrush à l'origine — piège
        /// b32 : Render(élément) garde son décalage dans la fenêtre).</summary>
        private static void Snapshot(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(element), null, new Rect(0, 0, width, height));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  (rendu : " + path + ")");
            }
            catch (Exception error)
            {
                Console.WriteLine("  (rendu PNG impossible : " + error.Message + ")");
            }
        }

        /// <summary>Compte les boîtes de l'arbre (les Border du canevas).</summary>
        private static int CountBoxes(Canvas canvas)
        {
            var count = 0;
            foreach (var child in canvas.Children)
                if (child is Border) count++;
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
