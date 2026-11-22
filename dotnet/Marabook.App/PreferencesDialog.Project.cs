using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.App
{
    /// <summary>L'ONGLET « PROJET » DES PRÉFÉRENCES (2.0.0, 21/11/2026, Rémi),
    /// le premier : ce qu'on sait du projet ouvert (son fichier, ses dates, ce
    /// qu'il contient), « Trouver le projet dans l'explorateur », l'Analyseur
    /// de .plot, et les modèles de projet — « Enregistrer en tant que modèle »
    /// et la liste de ceux qui existent.</summary>
    public partial class PreferencesDialog
    {
        private StackPanel _templateList;
        private TextBlock _templateStatus;

        /// <summary>Le nom du gestionnaire de fichiers du système, tel qu'on le dit.</summary>
        internal static string RevealLabel
        {
            get
            {
                return AppPlatform.IsMac ? "Trouver le projet dans le Finder"
                    : AppPlatform.IsWindows ? "Trouver le projet dans l'explorateur"
                    : "Trouver le projet dans le gestionnaire de fichiers";
            }
        }

        private Control BuildProjectTab(Window owner)
        {
            var shell = owner as MainWindow;
            var path = shell == null ? null : shell.ProjectPathForPreferences;
            var saved = !string.IsNullOrEmpty(path) && File.Exists(path);
            var panel = TabPanel();

            panel.Children.Add(Caption("Ce projet"));
            var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(0, 2, 0, 10), Tag = "project-facts" };
            Fact(facts, "Nom", saved ? Path.GetFileNameWithoutExtension(path) : (_project == null ? "" : _project.Name));
            Fact(facts, "Fichier", saved ? path : "Pas encore enregistré");
            if (saved)
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Fact(facts, "Taille", Size(size));
            }
            if (_project != null)
            {
                Fact(facts, "Créé le", string.IsNullOrEmpty(_project.CreatedAt) ? "—" : Dates.Display(_project.CreatedAt));
                Fact(facts, "Modifié le", string.IsNullOrEmpty(_project.ModifiedAt) ? "—" : Dates.Display(_project.ModifiedAt));
                Fact(facts, "Contenu", Contents(_project));
            }
            panel.Children.Add(facts);

            var actions = new WrapPanel();
            var reveal = Buttons.IconText("folder-open", RevealLabel, saved ? "Ouvre le dossier du projet et y montre son fichier .plot" : "Le projet n'a pas encore de fichier : enregistrez-le d'abord", Buttons.Bar, Buttons.Look.Outline);
            reveal.Tag = "project-reveal";
            reveal.IsEnabled = saved;
            reveal.Margin = new Thickness(0, 0, 8, 6);
            reveal.Click += delegate
            {
                try { AppPlatform.RevealInFolder(path); }
                catch (Exception error) { MessageDialog.Show(this, "Le dossier n'a pas pu être ouvert : " + error.Message, "Marabook", MessageButtons.OK, MessageIcon.Warning); }
            };
            actions.Children.Add(reveal);
            var analyzer = Buttons.IconText("info-bold", "Analyseur de .plot…", "Ce que pèse chaque partie du projet dans son fichier", Buttons.Bar, Buttons.Look.Outline);
            analyzer.Tag = "project-analyzer";
            analyzer.Margin = new Thickness(0, 0, 8, 6);
            analyzer.Click += delegate { PlotAnalyzerDialog.Show(this, path, _project, shell != null && shell.DirtyForProbe); };
            actions.Children.Add(analyzer);
            panel.Children.Add(actions);

            BuildBackupsSection(panel); // dans cet onglet depuis le 22/11 (elles étaient dans « Général »)

            panel.Children.Add(Caption("Modèles de projet", 18));
            panel.Children.Add(Note("Un modèle garde le cadre d'un projet pour en commencer d'autres : ses styles, sa mise en page, ses modèles et catégories de fiches. Il ne garde rien de ce qui y est écrit : ni écrits ni leurs titres, ni parties, ni fiches, ni recherche, ni frises, ni cartes. Si le projet a un livre, le modèle part avec un « Nouveau livre » au même format, avec ses gabarits. « Nouveau projet » propose ensuite vos modèles."));
            var save = Buttons.IconText("copy-simple-bold", "Enregistrer en tant que modèle…", "Garde le cadre de ce projet comme point de départ de futurs projets. Ce projet n'est pas modifié.", Buttons.Bar, Buttons.Look.Primary);
            save.Tag = "project-save-template";
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.IsEnabled = _project != null;
            save.Click += async delegate
            {
                var name = await InputDialog.Ask(this, "Enregistrer en tant que modèle", "Nom du modèle :", saved ? Path.GetFileNameWithoutExtension(path) : "Mon modèle");
                if (name == null) return;
                if (ProjectTemplates.CleanName(name).Length == 0) { Status("Un modèle a besoin d'un nom.", true); return; }
                if (ProjectTemplates.Exists(name)
                    && await MessageDialog.Show(this, "Un modèle « " + ProjectTemplates.CleanName(name) + " » existe déjà. Le remplacer ?", "Modèles de projet", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return;
                SaveTemplate(name, shell);
            };
            panel.Children.Add(save);
            _keepLexicon = new CheckBox { Content = "Garder le dictionnaire du projet dans le modèle", IsChecked = false, Margin = new Thickness(0, 8, 0, 0), Tag = "project-template-lexicon" };
            ToolTip.SetTip(_keepLexicon, "Les mots enseignés au correcteur dans ce projet (noms propres, néologismes) suivent dans le modèle,\net donc dans les projets qui en naîtront. Décochée : le modèle part sans dictionnaire.");
            panel.Children.Add(_keepLexicon);
            _templateStatus = Note("", 8, 4);
            _templateStatus.Tag = "project-template-status";
            _templateStatus.IsVisible = false;
            panel.Children.Add(_templateStatus);
            _templateList = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Tag = "project-templates" };
            panel.Children.Add(_templateList);
            RebuildTemplates();
            return new ScrollViewer { Content = panel, [ScrollViewer.VerticalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        }

        private CheckBox _keepLexicon;
        internal void KeepLexiconForProbe(bool keep) { _keepLexicon.IsChecked = keep; }

        internal bool SaveTemplate(string name, MainWindow shell)
        {
            try
            {
                if (shell != null) shell.CommitForPreferences(); // ce qui est en cours de frappe rejoint le projet
                var path = ProjectTemplates.Save(_project, name, _keepLexicon != null && _keepLexicon.IsChecked == true);
                Status("Modèle « " + Path.GetFileNameWithoutExtension(path) + " » enregistré : " + ProjectTemplates.Describe(ProjectTemplates.Instantiate(path)) + ".", false);
                RebuildTemplates();
                return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException) && !(error is InvalidDataException)) throw;
                Status("Le modèle n'a pas pu être enregistré : " + error.Message, true);
                return false;
            }
        }

        private void Status(string text, bool problem)
        {
            _templateStatus.Text = text;
            _templateStatus.Foreground = problem ? Chrome.Danger : Chrome.Ok;
            _templateStatus.IsVisible = true;
        }

        private void RebuildTemplates()
        {
            _templateList.Children.Clear();
            var entries = ProjectTemplates.All();
            if (entries.Count == 0)
            {
                _templateList.Children.Add(new TextBlock { Text = "Aucun modèle pour l'instant.", Foreground = Chrome.SoftText, FontSize = 12 });
                return;
            }
            foreach (var entry in entries)
            {
                var captured = entry;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 420 };
                var remove = Buttons.Icon("x-bold", "Supprimer ce modèle (les projets qui en sont nés ne changent pas)", Buttons.Compact, Buttons.Look.Calm);
                remove.Tag = "project-template-remove";
                remove.Click += async delegate
                {
                    if (await MessageDialog.Show(this, "Supprimer le modèle « " + captured.Name + " » ?", "Modèles de projet", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return;
                    try { ProjectTemplates.Delete(captured.Path); }
                    catch (Exception error) { if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw; Status("Le modèle n'a pas pu être supprimé : " + error.Message, true); }
                    RebuildTemplates();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var show = Buttons.Icon("folder-open", "Montrer le fichier de ce modèle", Buttons.Compact, Buttons.Look.Calm);
                show.Click += delegate { try { AppPlatform.RevealInFolder(captured.Path); } catch (Exception) { } };
                DockPanel.SetDock(show, Dock.Right);
                row.Children.Add(show);
                var date = new TextBlock { Text = Dates.Display(captured.SavedAt), Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
                DockPanel.SetDock(date, Dock.Right);
                row.Children.Add(date);
                row.Children.Add(new TextBlock { Text = captured.Name, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "project-template-name" });
                _templateList.Children.Add(row);
            }
        }

        private static void Fact(Grid grid, string label, string value)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var caption = new TextBlock { Text = label, Foreground = Chrome.SoftText, Margin = new Thickness(0, 3, 0, 3) };
            var text = new SelectableTextBlock { Text = value ?? "", Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), Tag = "project-fact:" + label };
            Grid.SetRow(caption, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(caption);
            grid.Children.Add(text);
        }

        private static string Size(long bytes)
        {
            var culture = CultureInfo.CurrentCulture;
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0", culture) + " Mo";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0", culture) + " Ko";
            return bytes + " octets";
        }

        private static string Contents(Project project)
        {
            int books = 0, texts = 0, sheets = 0;
            foreach (var item in project.AllItems())
            {
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                if (item.Kind == ItemKind.Book) books++;
                else if (item.Kind == ItemKind.Text && !item.IsExtraPage && root != null && root.CategoryKey == Project.KeyWritings) texts++;
                else if (item.Kind == ItemKind.Sheet) sheets++;
            }
            return (books == 1 ? "1 livre" : books + " livres") + ", " + (texts == 1 ? "1 écrit" : texts + " écrits") + ", " + (sheets == 1 ? "1 fiche" : sheets + " fiches");
        }

        // — Sonde.
        internal int TemplateRowsForProbe { get { return ProjectTemplates.All().Count; } }
        internal string TemplateStatusForProbe { get { return _templateStatus == null ? "" : _templateStatus.Text ?? ""; } }
    }
}
