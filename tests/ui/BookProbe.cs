using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde du batch 32 — le livre dans l'inspecteur, sur vraie
    /// MainWindow hors écran : la barre d'objectif (orange = chapitres
    /// présents, vert = terminés) au-dessus des statistiques ; les deux
    /// boutons Métadonnées / Publication sous les dates, chacun dépliant son
    /// panneau (et repliant l'autre) ; une frappe dans le panneau atteint le
    /// modèle sans repasser sous le curseur ; l'action « Options du livre »
    /// est annulable ; le tout survit à l'aller-retour disque (v12). Règle du
    /// batch 11 : settings.json est l'affaire de l'appelant (A1Probe).
    /// Bonus : l'inspecteur est rendu en PNG dans %TEMP% (marabook-b32-
    /// inspector.png) pour un contrôle visuel.</summary>
    public static class BookProbe
    {
        private static int _failures;

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-b32");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "b32.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE LIVRE EN ÉCHEC : " + error);
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
            // — Un livre à objectif 5 : quatre chapitres (deux terminés, dont
            // un dans une partie), un liminaire (ne compte pas).
            var project = Project.CreateNew();
            var book = new BinderItem
            {
                Kind = ItemKind.Book,
                Title = "Roman",
                Book = new BookInfo { ChapterGoal = 5 }
            };
            book.Children.Add(Chapter("Chapitre 1", "done"));
            book.Children.Add(Chapter("Chapitre 2", "draft"));
            book.Children.Add(Chapter("Chapitre 3", "done"));
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie II" };
            part.Children.Add(Chapter("Chapitre 4", "todo"));
            book.Children.Add(part);
            var extra = Chapter("Faux-titre", "done");
            extra.IsExtraPage = true;
            book.Children.Add(extra);
            project.Category(Project.KeyWritings).Children.Add(book);
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
            BinderItem target = null;
            foreach (var item in opened.AllItems())
                if (item.Kind == ItemKind.Book) target = item;
            Check(target != null && target.Book.ChapterGoal == 5,
                "l'objectif de chapitres a survécu à l'aller-retour disque (v12)");

            // — Le livre : vue corkboard pleine largeur, barre d'objectif.
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            var bookView = (BookView)GetField(window, "_bookView");
            Check(bookView.Visibility == Visibility.Visible && bookView.ColumnDefinitions.Count == 0,
                "la vue livre est le corkboard seul (plus de colonne de formulaire)");

            var progress = (StackPanel)GetField(window, "_progressSection");
            var label = (TextBlock)GetField(window, "_progressLabel");
            Check(progress.Visibility == Visibility.Visible,
                "la barre d'objectif s'affiche pour un livre");
            Check(label.Text.Contains("4 / 5") && label.Text.Contains("2 terminés"),
                "l'étiquette compte 4 / 5 chapitres, 2 terminés (obtenu : " + label.Text + ")");
            var present = (ColumnDefinition)GetField(window, "_progPresent");
            var rest = (ColumnDefinition)GetField(window, "_progRest");
            var done = (ColumnDefinition)GetField(window, "_progDone");
            Check(Near(present.Width.Value, 0.8) && Near(rest.Width.Value, 0.2),
                "l'orange couvre 4/5 de la piste");
            Check(Near(done.Width.Value, 0.5),
                "le vert couvre la moitié de l'orange (2 terminés sur 4 présents)");

            // — La barre précède les statistiques dans le panneau.
            var stats = (StackPanel)GetField(window, "_statsSection");
            var parent = (Panel)VisualTreeHelper.GetParent(progress);
            Check(parent != null && parent.Children.IndexOf(progress) == parent.Children.IndexOf(stats) - 1,
                "la barre d'objectif est juste au-dessus des statistiques");

            // — Édition, Gabarits & Format, Styles, Publication : les onglets
            //   de la page livre (22/09) ; le rail n'a plus d'onglet de livre.
            var tabs = (System.Collections.Generic.Dictionary<Marabook.Settings.RightPanel, Border>)GetField(window, "_railTabs");
            Check(tabs.ContainsKey(Marabook.Settings.RightPanel.Inspector) && tabs.ContainsKey(Marabook.Settings.RightPanel.Search)
                && !tabs.ContainsKey(Marabook.Settings.RightPanel.Correction) && !tabs.ContainsKey(Marabook.Settings.RightPanel.Versions),
                "sur un livre, le rail offre Général et Recherche (et pas Correction) — Métadonnées et Publication sont dans la page");
            var pageTabs = (TabControl)GetField(bookView, "_tabs");
            Check(pageTabs.Items.Count == 5, "la page livre a cinq onglets : Textes, Édition, Gabarits & Format, Styles, Publication");

            // — Édition : frappe → modèle, sans resynchronisation.
            bookView.SelectedTab = 1;
            DoEvents();
            var meta = (BookEditionTab)GetField(bookView, "_edition");
            Check(pageTabs.SelectedIndex == 1 && meta.IsVisible, "« Édition » montre les métadonnées et la présentation du livre");
            var subtitle = (TextBox)GetField(meta, "_subtitle");
            subtitle.Focus();
            subtitle.Text = "Tome";
            subtitle.CaretIndex = subtitle.Text.Length;
            subtitle.SelectedText = " premier";
            DoEvents();
            Check(target.Book.Subtitle == "Tome premier",
                "la frappe dans le panneau atteint le modèle (obtenu : " + target.Book.Subtitle + ")");
            // Une resynchronisation (Text réaffecté) ramènerait la sélection
            // à 0/0 ; après la frappe elle reste en fin de texte.
            Check(subtitle.SelectionStart + subtitle.SelectionLength == subtitle.Text.Length
                && subtitle.Text.Length > 0,
                "le curseur n'a pas été déplacé par une resynchronisation");
            Check((bool)GetField(window, "_dirty"), "le projet est marqué modifié");

            // — Gabarits & Format : le fond perdu tapé atteint le modèle.
            bookView.SelectedTab = 2;
            DoEvents();
            var format = (BookFormatPanel)GetField(bookView, "_format");
            var bleed = (TextBox)GetField(format, "_bleed");
            bleed.Text = "4";
            DoEvents();
            Check(Near(target.Book.BleedMm, 4), "le fond perdu tapé atteint le modèle");

            // — Publication : la check-list et les tailles se bâtissent à l'affichage.
            bookView.SelectedTab = 4;
            DoEvents();
            var publication = (BookPublicationTab)GetField(bookView, "_publication");
            var checklist = (StackPanel)GetField(publication, "_checklist");
            Check(checklist.Children.Count >= 6, "la check-list de publication est bâtie (" + checklist.Children.Count + " lignes)");

            // — Sur un écrit, la barre d'objectif disparaît ; de retour sur le
            //   livre, l'onglet où l'on était est retenu.
            Invoke(window, "OnBinderSelection", new object[] { target.Children[0] });
            DoEvents();
            Check(progress.Visibility == Visibility.Collapsed && bookView.Visibility != Visibility.Visible,
                "sur un écrit : ni page livre ni barre d'objectif");
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Check(bookView.Visibility == Visibility.Visible && bookView.SelectedTab == 4,
                "de retour sur le livre, l'onglet Publication est retenu");
            bookView.SelectedTab = 0;
            DoEvents();

            // — « Options du livre » : une action, annulable ; la barre suit.
            var history = (HistoryManager)GetField(window, "_history");
            history.Run(new BookOptionsAction(target, "Roman, tome I", "svg:books-bold", 8));
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Check(target.Title == "Roman, tome I" && target.Icon == "svg:books-bold"
                && target.Book.ChapterGoal == 8,
                "les options du livre s'appliquent d'un bloc");
            Check(label.Text.Contains("4 / 8") && Near(present.Width.Value, 0.5),
                "la barre suit le nouvel objectif (obtenu : " + label.Text + ")");
            history.Undo();
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Check(target.Title == "Roman" && target.Icon == null && target.Book.ChapterGoal == 5,
                "Ctrl+Z rend nom, icône et objectif");
            history.Redo();

            // — Objectif atteint : tout vert.
            foreach (var item in opened.AllItems())
                if (item.Kind == ItemKind.Text && item.EnclosingBook() == target && !item.IsExtraPage)
                    item.Status = "done";
            target.Book.ChapterGoal = 4;
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Check(label.Text.Contains("atteint") && Near(present.Width.Value, 1) && Near(done.Width.Value, 1),
                "objectif atteint : piste pleine et verte");

            // — Sans objectif : l'invite, piste vide.
            target.Book.ChapterGoal = 0;
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Check(label.Text.Contains("aucun") && Near(present.Width.Value, 0),
                "sans objectif : l'invite « Options du livre » et une piste vide");
            target.Book.ChapterGoal = 8;
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();

            // — Les trois sections de pages extra (b49) : une préface se place
            //   en tête, des remerciements après le corps, un index en queue ;
            //   l'index dynamique se bâtit sans planter sur un projet sans fiche.
            Invoke(window, "NewBookDocument", new object[] { target, ExtraPages.KindPreface });
            Invoke(window, "NewBookDocument", new object[] { target, ExtraPages.KindThanks });
            Invoke(window, "NewBookDocument", new object[] { target, ExtraPages.KindIndex });
            DoEvents();
            var order = new System.Collections.Generic.List<string>();
            foreach (var child in target.Children) order.Add(child.Title);
            Check(string.Join(",", order.ToArray()) == "Préface,Chapitre 1,Chapitre 2,Chapitre 3,Partie II,Faux-titre,Remerciements,Index",
                "préface en tête, remerciements après le corps, index en queue (obtenu : " + string.Join(",", order.ToArray()) + ")");
            var indexPage = target.Children[target.Children.Count - 1];
            Check(indexPage.IsExtraPage && indexPage.ExtraSection == ExtraPages.SectionAnnex
                && indexPage.Document.ToPlainText().Contains("INDEX"),
                "l'index est une annexe, page extra, bâtie dynamiquement");
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();
            Snapshot((FrameworkElement)GetField(window, "_bookView"),
                Path.Combine(Path.GetTempPath(), "marabook-2109-livre-sections.png"));

            // — Contrôle visuel : l'inspecteur puis les onglets de la page livre en PNG.
            Snapshot((FrameworkElement)GetField(window, "_inspector"),
                Path.Combine(Path.GetTempPath(), "marabook-b32-inspector.png"));
            for (var tab = 1; tab <= 4; tab++)
            {
                bookView.SelectedTab = tab;
                DoEvents();
                Snapshot(bookView, Path.Combine(Path.GetTempPath(), "marabook-2209-livre-onglet-" + tab + ".png"));
            }
            bookView.SelectedTab = 0;
            DoEvents();

            // — Enregistrement : objectif, sous-titre, fond perdu sur le disque.
            Invoke(window, "DoSave", null);
            DoEvents();
            var reloaded = PlotFile.Load(path);
            BinderItem back = null;
            foreach (var item in reloaded.AllItems())
                if (item.Kind == ItemKind.Book) back = item;
            Check(back != null && back.Book.ChapterGoal == 8 && back.Book.Subtitle == "Tome premier"
                && Near(back.Book.BleedMm, 4) && back.Title == "Roman, tome I",
                "objectif, métadonnées et gabarit édités sont sur le disque");

            window.Close();
            DoEvents();
        }

        private static BinderItem Chapter(string title, string status)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title, Status = status };
            item.Document = TextDocument.FromPlainText(title + ".");
            return item;
        }

        /// <summary>Le clic réel bascule IsChecked AVANT de lever Click ; la
        /// sonde reproduit les deux temps.</summary>
        private static void Click(ToggleButton toggle, bool isChecked)
        {
            toggle.IsChecked = isChecked;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private static bool Near(double value, double expected)
        {
            return Math.Abs(value - expected) < 0.001;
        }

        private static void Snapshot(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                // Piège connu : Render(element) garde le décalage de l'élément
                // dans sa fenêtre (colonne de droite = hors cadre) ; on passe
                // par un VisualBrush dessiné à l'origine.
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
                Console.WriteLine("  (inspecteur rendu : " + path + ")");
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
