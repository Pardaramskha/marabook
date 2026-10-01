using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Ce que la fenêtre Avalonia a EN PLUS de la fenêtre WPF portée
    /// (P1, conservé en P3) : les options de lancement (--demo, --probe,
    /// --capture, --prefs, --lab, --settings), le projet d'exemple en
    /// mémoire, la capture PNG, la fenêtre de diagnostic, et les accès que la
    /// sonde (Probes.cs) prend sur la fenêtre.</summary>
    public partial class MainWindow
    {
        private Launch _launch = new Launch();

        /// <summary>MARABOOK_TRACE=1 : la sélection et l'ouverture des vues s'écrivent sur la console (sondes).</summary>
        private static readonly bool Trace = Environment.GetEnvironmentVariable("MARABOOK_TRACE") == "1";

        public MainWindow(Launch launch) : this()
        {
            _launch = launch ?? new Launch();
            Opened += delegate { ScheduleUpdateCheck(); }; // la vérification silencieuse du lancement (01/10)
            if (_launch.Isolated)
            {
                WindowState = WindowState.Normal;
                Width = 1400;
                Height = 860;
            }
            Opened += async delegate
            {
                await Task.Delay(50);
                if (_launch.PlotPath != null)
                {
                    PendingOpen = null;
                    OpenFile(_launch.PlotPath);
                    if (_launch.SaveProbe)
                    {
                        // La sonde d'enregistrement (27/09) : le projet ouvert
                        // depuis un fichier, le vrai chemin de la fenêtre,
                        // verdict sur la sortie — pour attraper un
                        // « Object reference… » avec sa pile.
                        await Task.Delay(1500);
                        await SaveProbeSweep(null);
                    }
                }
                else if (_launch.SaveProbe)
                {
                    // Sans .plot : le parcours de l'accueil — projet neuf (ou
                    // le projet d'exemple avec --demo) enregistré sous un nom,
                    // un écrit créé, quelques mots tapés, puis le balayage.
                    await Task.Delay(500);
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-save-" + Guid.NewGuid().ToString("N") + Persistence.PlotFile.Extension);
                    LoadProject(_launch.Demo ? SampleProject() : Project.CreateNew(), null);
                    _path = path;
                    AdoptFileName(_project, _path);
                    DoSave();
                    await SaveCompletion();
                    Console.WriteLine(LastSaveError == null ? "SAVE OK    projet neuf" : "SAVE ERROR projet neuf\n" + LastSaveError);
                    _binder.NewText(null);
                    await Task.Delay(600);
                    if (Composed != null && Composed.HasItem) Composed.TypeText("Sonde d'enregistrement.");
                    await Task.Delay(300);
                    await SaveProbeSweep(path);
                }
                else if (_launch.Demo || _launch.Probe)
                {
                    if (_welcome != null) _welcome.Close();
                    LoadProject(SampleProject(), null);
                    if (_launch.Demo && !_launch.Probe) { await Task.Delay(200); if (_launch.OpenTitle != null) OpenByTitle(_launch.OpenTitle); else SelectFirstText(); }
                }
                if (_launch.Probe) { await Probes.Run(this); QuitNow(); return; }
                if (_launch.FontProbe != null) { FontProbe(_launch.FontProbe); QuitNow(); return; }
                if (_launch.UpdateProbe) { UpdateProbe(); QuitNow(); return; }
                if (_launch.UpdateRolledBack)
                    await MessageDialog.Show(this, "La mise à jour n'a pas pu démarrer : la version précédente a été remise en place.\n\nRéessayez plus tard depuis Aide › Vérifier les mises à jour, ou téléchargez la release depuis GitHub.",
                        "Mise à jour annulée", MessageButtons.OK, MessageIcon.Warning);
                if (_launch.CapturePath != null) await CaptureAndQuit(_launch.CapturePath);
                else if (_launch.Lab) BuildLab().Show(this);
            };
        }

        // ------------------------------------------------------------ exposé aux sondes
        public Project Project { get { return _project; } }
        public BinderView Binder { get { return _binder; } }
        public WelcomeWindow Welcome { get { return _welcome; } }
        public string InspectorTitle { get { return _inspTitle == null ? "" : _inspTitle.Text ?? ""; } }
        public string InspectorKind { get { return _inspKind == null ? "" : _inspKind.Text ?? ""; } }
        public string InspectorDetail { get { return _inspStats == null ? "" : _inspStats.Text ?? ""; } }
        public string StatusText { get { return _statusLeft == null ? "" : _statusLeft.Text ?? ""; } }
        public string StatusRightText { get { return _statusRight == null ? "" : _statusRight.Text ?? ""; } }
        public string StatusPagesText { get { return _statusPages == null ? "" : _statusPages.Text ?? ""; } }

        public void ShowJournalPublic() { ShowJournal(); }

        /// <summary>La vue du centre qui est visible (sondes) : editor, sheet,
        /// library, dictionary, home, plan, mindmap, corkboard, book, template,
        /// media, journal, ou « none ».</summary>
        public string VisibleView
        {
            get
            {
                if (_editor != null && _editor.IsVisible) return "editor";
                if (_sheetView != null && _sheetView.IsVisible) return "sheet";
                if (_sheetLibrary != null && _sheetLibrary.IsVisible) return "library";
                if (_dictionaryView != null && _dictionaryView.IsVisible) return "dictionary";
                if (_homeView != null && _homeView.IsVisible) return "home";
                if (_planView != null && _planView.IsVisible) return "plan";
                if (_mindMapHost != null && _mindMapHost.IsVisible) return "mindmap";
                if (_corkboard != null && _corkboard.IsVisible) return "corkboard";
                if (_bookView != null && _bookView.IsVisible) return "book";
                if (_templateView != null && _templateView.IsVisible) return "template";
                if (_mediaView != null && _mediaView.IsVisible) return "media";
                if (_journalView != null && _journalView.IsVisible) return "journal";
                return "none";
            }
        }
        public void SetRightPanelPublic(RightPanel panel) { SetRightPanel(panel); }

        /// <summary>--open : l'élément de ce titre (ou « journal ») est ouvert
        /// avant la capture.</summary>
        /// <summary>« --maj-test » (01/10) : le chemin de la mise à jour tel que
        /// le lancement le suit — GitHub interrogé, la dernière release comparée
        /// à 0.0.0 (pour toujours la trouver plus récente), l'archive de ce
        /// système téléchargée et déballée dans un dossier temporaire, le
        /// contenu compté — puis tout est jeté. Rien n'est installé.</summary>
        private static void UpdateProbe()
        {
            Console.WriteLine("== Mise à jour (" + Updater.PortableZip + ")");
            var check = Updater.Run("0.0.0");
            Console.WriteLine("  GitHub : " + check.Message + (check.Latest == null ? "" : " — " + check.Latest.ZipUrl));
            if (!check.Available || check.Latest == null) { Console.WriteLine("  MAJ ÉCHEC : rien à télécharger"); Environment.ExitCode = 1; return; }
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var prepared = Updater.Prepare(check.Latest);
                var files = System.IO.Directory.GetFiles(prepared.Content, "*", System.IO.SearchOption.AllDirectories).Length;
                Console.WriteLine("  téléchargée et déballée en " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s : " + files + " fichiers, "
                    + Updater.Exe + (System.IO.File.Exists(System.IO.Path.Combine(prepared.Content, Updater.Exe)) ? " présent" : " ABSENT")
                    + " (version " + prepared.Info.Version + ")");
                prepared.Discard();
                Console.WriteLine("  MAJ OK");
            }
            catch (Exception failure)
            {
                Console.WriteLine("  MAJ ÉCHEC : " + failure.Message);
                Environment.ExitCode = 1;
            }
        }

        /// <summary>« --police famille » (29/09) : comment le moteur résout la
        /// face pour chaque graisse — le nom réel, la graisse obtenue, les
        /// simulations (gras/oblique synthétiques). Diagnostic d'un corps
        /// rendu gras alors que la feuille dit maigre.</summary>
        private static void FontProbe(string family)
        {
            var engine = new AvaloniaFontEngine();
            Console.WriteLine("== Police « " + family + " »");
            foreach (var weight in new[] { 300, 400, 500, 600, 700 })
                foreach (var italic in new[] { false, true })
                {
                    var face = engine.Resolve(family, weight, italic);
                    Console.WriteLine("  demandé " + weight + (italic ? " italique" : "        ")
                        + " → " + (face.HasGlyphs ? face.File : "(sans glyphes)")
                        + "  graisse " + face.ActualWeight + (face.SimulatedBold ? "  GRAS SIMULÉ" : "") + (face.SimulatedItalic ? "  oblique simulé" : ""));
                }
            Console.WriteLine("  familles système contenant « " + family.Split(' ')[0] + " » :");
            foreach (var installed in Avalonia.Media.FontManager.Current.SystemFonts)
                if (installed.Name.IndexOf(family.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("    " + installed.Name);
        }

        public void OpenByTitle(string title)
        {
            if (_project == null || string.IsNullOrEmpty(title)) return;
            if (title == "journal") { ShowJournal(); return; }
            foreach (var item in _project.AllItems())
                if (string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase)) { _binder.SelectItem(item.Id, true); return; }
            foreach (var root in _project.Roots)
                if (string.Equals(root.Title, title, StringComparison.OrdinalIgnoreCase)) { _binder.SelectItem(root.Id, true); return; }
        }

        /// <summary>La sonde d'enregistrement (27/09) : enregistrer (manuel
        /// puis automatique) sur le projet tel quel, puis un élément de chaque
        /// nature ouvert au centre (le rinçage des vues change avec la vue
        /// ouverte) et l'enregistrement après chacun ; verdict par ligne sur la
        /// sortie, la pile complète après un échec. Quitte à la fin, efface le
        /// fichier temporaire s'il y en a un.</summary>
        private async Task SaveProbeSweep(string temporaryPath)
        {
            DoSave();
            await SaveCompletion();
            Console.WriteLine(LastSaveError == null ? "SAVE OK    manuel" : "SAVE ERROR manuel\n" + LastSaveError);
            MarkDirty();
            Autosave();
            await SaveCompletion();
            Console.WriteLine(LastSaveError == null ? "SAVE OK    automatique" : "SAVE ERROR automatique\n" + LastSaveError);
            var seen = new HashSet<ItemKind>();
            foreach (var item in new List<BinderItem>(_project.AllItems()))
            {
                if (!seen.Add(item.Kind)) continue;
                _binder.SelectItem(item.Id, true);
                await Task.Delay(700);
                MarkDirty();
                SaveProject(true);
                await SaveCompletion();
                Console.WriteLine((LastSaveError == null ? "SAVE OK    " : "SAVE ERROR ") + item.Kind + " « " + item.Title + " » (" + VisibleView + ")" + (LastSaveError == null ? "" : "\n" + LastSaveError));
            }
            foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
            {
                SetRightPanelPublic(panel);
                await Task.Delay(300);
                MarkDirty();
                SaveProject(true);
                await SaveCompletion();
                Console.WriteLine((LastSaveError == null ? "SAVE OK    " : "SAVE ERROR ") + "panneau " + panel + (LastSaveError == null ? "" : "\n" + LastSaveError));
            }
            if (temporaryPath != null) { try { System.IO.File.Delete(temporaryPath); } catch { } }
            QuitNow();
        }

        /// <summary>Quitter sans question (sondes, captures) : rien à enregistrer.</summary>
        public void QuitNow()
        {
            _dirty = false;
            _closeConfirmed = true;
            Close();
        }
        public EditorView Editor { get { return _editor; } }
        public ComposedView Composed { get { return _editor == null ? null : _editor.Composed; } }

        /// <summary>La fenêtre des Préférences ouverte, ou null.</summary>
        private static PreferencesDialog OpenPreferencesWindow()
        {
            var desktop = Avalonia.Application.Current == null ? null
                : Avalonia.Application.Current.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (desktop == null) return null;
            foreach (var window in desktop.Windows)
                if (window is PreferencesDialog) return (PreferencesDialog)window;
            return null;
        }

        /// <summary>La sonde ferme le projet : l'accueil revient par-dessus.</summary>
        public void CloseProjectPublic()
        {
            CommitActive();
            ShowWelcome();
        }

        /// <summary>La démo ouvre le premier écrit non vide du projet (la
        /// capture montre l'éditeur, pas l'invite).</summary>
        public void SelectFirstText()
        {
            if (_project == null) return;
            var first = FirstText(_project.Roots);
            if (first != null) _binder.SelectItem(first.Id, true);
        }

private static BinderItem FirstText(IEnumerable<BinderItem> items)
        {
            foreach (var item in items)
            {
                if (item.Kind == ItemKind.Text && item.Document != null && item.Document.ToPlainText().Trim().Length > 0) return item;
                var inner = FirstText(item.Children);
                if (inner != null) return inner;
            }
            return null;
        }

public static Project SampleProject()
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var book = new BinderItem { Title = "Le marabout et la mer", Kind = ItemKind.Book, Parent = writings };
            writings.Children.Add(book);
            var chapters = new[]
            {
                "Chapitre premier — Le vent des pins",
                "Chapitre deux — La maison aux volets",
                "Chapitre trois — L'odeur de la marée"
            };
            foreach (var title in chapters)
            {
                var text = new BinderItem { Title = title, Kind = ItemKind.Text, Parent = book };
                text.Document = TextDocument.FromPlainText(
                    "Le vent portait l'odeur des pins jusqu'au village, et personne ne songeait encore à fermer les volets.\n\n" +
                    "La mer, au loin, avait la couleur d'une ardoise mouillée.");
                book.Children.Add(text);
            }
            // Une couverture (30/09) : la tuile du livre devient le livre vu de face.
            // À côté de l'exe (publié), sinon à la racine du dépôt (Debug).
            foreach (var coverPath in new[] {
                System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "marabook.png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "marabook.png") })
                if (System.IO.File.Exists(coverPath)) { book.ImageId = project.AddImage(System.IO.File.ReadAllBytes(coverPath), ".png"); break; }
            var research = project.Category(Project.KeyResearch);
            research.Children.Add(new BinderItem { Title = "Carte du littoral", Kind = ItemKind.Media, Parent = research });
            research.Children.Add(new BinderItem { Title = "Notes sur les marées", Kind = ItemKind.Text, Parent = research });
            var sheets = project.Category(Project.KeySheets);
            var folder = new BinderItem { Title = "Personnages", Kind = ItemKind.Folder, Parent = sheets };
            sheets.Children.Add(folder);
            var keira = new BinderItem { Title = "Keira Varenh", Kind = ItemKind.Sheet, Parent = folder, CardColor = "#C0392B" };
            var marabout = new BinderItem { Title = "Le marabout", Kind = ItemKind.Sheet, Parent = folder };
            folder.Children.Add(keira);
            folder.Children.Add(marabout);
            // Une relation (29/09) : le wiki montre un lien vers une fiche (captures, sondes).
            keira.Relations.Add(new SheetRelation { Kind = "mentor", TargetId = marabout.Id });
            var plans = project.Category(Project.KeyPlans);
            // Un plan garni (30/09) : la vue Intensité a de quoi tracer.
            var plan = new BinderItem { Title = "Plan en trois actes", Kind = ItemKind.Plan, Parent = plans, Plan = new PlanInfo() };
            AddPlanColumn(plan, "Acte I — L'appel", "Le vent se lève|1", "La lettre du marabout|2");
            AddPlanColumn(plan, "Acte II — La maison aux volets clos", "Le naufrage|4", "Une nuit sans lune|3");
            AddPlanColumn(plan, "Acte III — La grande marée", "Le retour du marabout|5");
            AddPlanColumn(plan, "Épilogue", "La mer, au loin|2");
            plans.Children.Add(plan);
            return project;
        }

        private static void AddPlanColumn(BinderItem plan, string title, params string[] entries)
        {
            var column = new PlanColumn { Title = title };
            foreach (var entry in entries)
            {
                var bar = entry.LastIndexOf('|');
                column.Entries.Add(new PlanEntry { Text = entry.Substring(0, bar), Intensity = int.Parse(entry.Substring(bar + 1)) });
            }
            plan.Plan.Columns.Add(column);
        }

        // ------------------------------------------------------------ captures et laboratoire

private Window BuildLab()
        {
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "1. TextBlock nu : Boutons, sélections, liens" });
            panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = "2. Dans un ScrollViewer : Boutons, sélections, liens" }, Height = 30 });
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "Onglet", Content = new TextBlock { Text = "3. Dans un TabControl : Boutons, sélections, liens" } });
            panel.Children.Add(tabs);
            panel.Children.Add(new CheckBox { Content = "4. Libellé de case : Boutons, sélections, liens" });
            var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            choices.Children.Add(new CheckBox { Content = "4b. Case cochée", IsChecked = true });
            choices.Children.Add(new RadioButton { Content = "Radio choisie", GroupName = "lab", IsChecked = true });
            choices.Children.Add(new RadioButton { Content = "Radio au repos", GroupName = "lab" });
            panel.Children.Add(choices);
            panel.Children.Add(new TextBlock { Text = "5. TextBlock Wrap : Boutons, sélections, liens", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "6. TextBlock FontSize 12 SoftText : Boutons, sélections, liens", FontSize = 12, Foreground = Chrome.SoftText });
            var stackInGrid = new Grid();
            stackInGrid.Children.Add(new TextBlock { Text = "7. Dans un Grid : Boutons, sélections, liens" });
            panel.Children.Add(stackInGrid);
            var dock = new DockPanel();
            dock.Children.Add(new TextBlock { Text = "8. Dans un DockPanel : Boutons, sélections, liens" });
            panel.Children.Add(dock);
            return new Window { Title = "Lab", Width = 700, Height = 420, Background = Chrome.RaisedBg, Content = panel };
        }

private async Task CaptureAndQuit(string path)
        {
            if (_launch.Prefs)
            {
                OpenPreferences();
                await Task.Delay(300);
                var preferences = OpenPreferencesWindow();
                if (_launch.PrefsTab >= 0 && preferences != null && _launch.PrefsTab < preferences.Tabs.Items.Count)
                {
                    preferences.Tabs.SelectedIndex = _launch.PrefsTab;
                    await Task.Delay(400);
                }
            }
            Window lab = null;
            if (_launch.Lab) { lab = BuildLab(); lab.Show(this); await Task.Delay(300); }
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Render);
            try
            {
                Control content = lab != null ? (Control)lab.Content
                    : OpenPreferencesWindow() != null ? (Control)OpenPreferencesWindow().Content
                    : _welcome != null ? (Control)_welcome.Content : (Control)Content;
                var scale = _launch.Scale > 0 ? _launch.Scale : 1;
                var size = new PixelSize(Math.Max(1, (int)(content.Bounds.Width * scale)), Math.Max(1, (int)(content.Bounds.Height * scale)));
                using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
                {
                    bitmap.Render(content);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
                    bitmap.Save(path);
                }
                Console.WriteLine("capture : " + path + " (" + size.Width + "×" + size.Height + ")");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("capture impossible : " + error);
            }
            QuitNow();
        }
    }
}
