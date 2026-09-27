using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'écran « Dictionnaire » (batch 33) — la vue de la racine du
    /// même nom dans la Pile, entre Fiches et Corbeille : les entrées du
    /// dictionnaire personnel du PROJET, puis celles de TOUS LES PROJETS,
    /// chacune avec sa nature grammaticale et les formes que le correcteur
    /// en accepte. Ajouter, modifier, retirer ; le correcteur est prévenu
    /// par Changed (portée projet = vrai).</summary>
    public class DictionaryView : DockPanel
    {
        private Project _project;
        private readonly StackPanel _sections;
        private readonly TextBox _searchBox;

        public event Action<bool> Changed; // projectScope

        public DictionaryView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;

            // La rangée du haut, alignée sur celle d'Écrits (14/09) : pas de
            // barre, « Nouvelle entrée » en principal à gauche, la recherche
            // contre le bord droit.
            var toolbar = new DockPanel { Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0) };
            SetDock(toolbar, Dock.Top);
            _searchBox = new TextBox { [ToolTip.TipProperty] = "Filtrer les entrées (mot ou forme acceptée)" };
            _searchBox.TextChanged += delegate { Rebuild(); };
            var search = SheetLibraryView.SearchField(_searchBox);
            DockPanel.SetDock(search, Dock.Right);
            toolbar.Children.Add(search);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var newEntry = Buttons.IconText("plus-bold", "Nouvelle entrée",
                "Ajouter un mot au dictionnaire personnel avec sa nature grammaticale",
                Buttons.Bar, Buttons.Look.Primary);
            newEntry.Click += delegate { NewEntry(true); };
            left.Children.Add(newEntry);
            // Le menu options (18/09) : l'épingle du Lexique au rail, et
            // l'import/export de dictionnaires (fichiers d'autres apps).
            var options = Buttons.Icon("dots-three-vertical-bold",
                "Options : Lexique au rail, importer ou exporter un dictionnaire",
                Buttons.Bar, Buttons.Look.Calm);
            options.Margin = new Thickness(6, 0, 0, 0);
            options.Click += delegate { OpenOptionsMenu(options); };
            left.Children.Add(options);
            toolbar.Children.Add(left);
            Children.Add(toolbar);

            _sections = new StackPanel { Margin = new Thickness(16, 12, 16, 24), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
            Children.Add(new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Content = _sections
            });
        }

        public void Load(Project project)
        {
            _project = project;
            Rebuild();
        }

        public void Refresh() { Rebuild(); }

        /// <summary>Ouvre le dialogue d'entrée (mot vide) dans la portée
        /// donnée — aussi le chemin du menu de la Pile.</summary>
        public async void NewEntry(bool projectScope)
        {
            var choice = await LexiconEntryDialog.Ask(Ui.OwnerOf(this), null, projectScope);
            if (choice == null) return;
            AddEntry(choice.Entry, choice.ProjectScope);
        }

        /// <summary>Ajoute (ou remplace, même mot) une entrée dans la portée.</summary>
        public void AddEntry(LexiconEntry entry, bool projectScope)
        {
            var list = Target(projectScope);
            if (list == null) return;
            var existing = LexiconEntry.Find(list, entry.Word);
            if (existing != null) list.Remove(existing);
            list.Add(entry);
            Rebuild();
            RaiseChanged(projectScope);
        }

        private List<LexiconEntry> Target(bool projectScope)
        {
            if (projectScope) return _project == null ? null : _project.Lexicon;
            return AppSettings.Lexicon;
        }

        // ---------------------------------------------------------- rendu

        // Rangées du projet par entrée, pour la navigation d'une occurrence (b37).
        private readonly Dictionary<LexiconEntry, Border> _entryRows = new Dictionary<LexiconEntry, Border>();

        /// <summary>Amène l'entrée n° index du lexique du projet à l'écran,
        /// contour d'accent un instant — recherche projet.</summary>
        public void GoTo(int index)
        {
            if (_project == null || index < 0 || index >= _project.Lexicon.Count) return;
            Border row;
            if (!_entryRows.TryGetValue(_project.Lexicon[index], out row)) return;
            row.BringIntoView();
            var previous = row.BorderBrush;
            row.BorderBrush = Chrome.Accent;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += delegate { timer.Stop(); row.BorderBrush = previous; };
            timer.Start();
        }

        private void Rebuild()
        {
            _entryRows.Clear();
            _sections.Children.Clear();
            _sections.Children.Add(new TextBlock
            {
                Text = "Dictionnaire personnel",
                Foreground = Chrome.Ink,
                FontSize = 20,
                FontWeight = FontWeight.SemiBold
            });
            _sections.Children.Add(new TextBlock
            {
                Text = "Les mots enseignés au correcteur — noms du roman, peuples, lieux, néologismes — "
                    + "avec leur nature : le correcteur accepte le mot ET ses formes (pluriel, féminin, "
                    + "conjugaison). « Ajouter au dictionnaire » du clic droit sur un mot souligné arrive ici.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 12)
            });
            BuildSection("Ce projet", _project == null ? null : _project.Lexicon, true);
            BuildSection("Tous les projets", AppSettings.Lexicon, false);
        }

        private void BuildSection(string caption, List<LexiconEntry> entries, bool projectScope)
        {
            var header = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
            var add = new Button
            {
                Content = Icons.Label("plus-bold", "Entrée", 10, Chrome.Ink),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11,
                [ToolTip.TipProperty] = "Nouvelle entrée dans « " + caption + " »"
            };
            add.Click += delegate { NewEntry(projectScope); };
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);
            header.Children.Add(new TextBlock
            {
                Text = caption + (entries == null ? " (aucun projet ouvert)"
                    : " — " + entries.Count + (entries.Count > 1 ? " entrées" : " entrée")),
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            _sections.Children.Add(header);
            _sections.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, 6) });
            if (entries == null) return;

            var filter = (_searchBox.Text ?? "").Trim();
            var sorted = new List<LexiconEntry>(entries);
            sorted.Sort(delegate(LexiconEntry a, LexiconEntry b)
            { return string.Compare(a.Word, b.Word, StringComparison.CurrentCultureIgnoreCase); });
            var shown = 0;
            foreach (var entry in sorted)
            {
                if (filter.Length > 0 && !Matches(entry, filter)) continue;
                shown++;
                _sections.Children.Add(BuildRow(entry, entries, projectScope));
            }
            if (shown == 0)
                _sections.Children.Add(new TextBlock
                {
                    Text = filter.Length > 0 ? "Aucune entrée ne correspond." : "Aucune entrée.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 0, 6)
                });
        }

        private static bool Matches(LexiconEntry entry, string filter)
        {
            var needle = Correction.FrenchTokenizer.Fold(filter);
            foreach (var form in entry.Forms())
                if (Correction.FrenchTokenizer.Fold(form).Contains(needle)) return true;
            return false;
        }

        private Control BuildRow(LexiconEntry entry, List<LexiconEntry> list, bool projectScope)
        {
            var row = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            if (projectScope) _entryRows[entry] = row;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = entry.Word,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            left.Children.Add(new TextBlock
            {
                Text = entry.Summary(),
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var forms = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            if (entry.Definition.Length > 0)
                forms.Children.Add(new TextBlock
                {
                    Text = entry.Definition,
                    Foreground = Chrome.Ink,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            forms.Children.Add(new TextBlock
            {
                Text = LexiconInflector.Preview(entry, 12),
                Foreground = entry.Definition.Length > 0 ? Chrome.SoftText : Chrome.Ink,
                FontSize = entry.Definition.Length > 0 ? 11 : 12,
                TextWrapping = TextWrapping.Wrap,
                [ToolTip.TipProperty] = string.Join("\n", entry.Forms().ToArray())
            });
            if (entry.Note.Length > 0)
                forms.Children.Add(new TextBlock
                {
                    Text = entry.Note,
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                });
            Grid.SetColumn(forms, 1);
            grid.Children.Add(forms);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edit = new Button { Content = "Modifier…", Padding = new Thickness(8, 2, 8, 2), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            edit.Click += async delegate
            {
                var choice = await LexiconEntryDialog.Ask(Ui.OwnerOf(this), entry, projectScope);
                if (choice == null) return;
                var edited = choice.Entry;
                var scope = choice.ProjectScope;
                list.Remove(entry);
                AddEntry(edited, scope);
                if (scope != projectScope) RaiseChanged(projectScope); // l'ancienne portée a changé aussi
            };
            actions.Children.Add(edit);
            var remove = new Button { Content = "Retirer", Padding = new Thickness(8, 2, 8, 2), FontSize = 11 };
            remove.Click += delegate
            {
                list.Remove(entry);
                Rebuild();
                RaiseChanged(projectScope);
            };
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            row.Child = grid;
            return row;
        }

        private void RaiseChanged(bool projectScope)
        {
            var handler = Changed;
            if (handler != null) handler(projectScope);
        }

        // ---------------------------------------------------------- options (18/09)

        public event Action<bool> LexiconPinToggled; // le Lexique au rail, depuis le menu options

        /// <summary>Édite une entrée (le crayon du panneau Lexique) : rend
        /// l'entrée éditée et la portée choisie, ou null si annulé.</summary>
        public async Task<LexiconEntryDialog.Choice> Edit(LexiconEntry entry, bool projectScope)
        {
            var choice = await LexiconEntryDialog.Ask(Ui.OwnerOf(this), entry, projectScope);
            if (choice == null) return null;
            var edited = choice.Entry;
            var scope = choice.ProjectScope;
            var list = Target(projectScope);
            if (list != null) list.Remove(entry);
            AddEntry(edited, scope);
            if (scope != projectScope) RaiseChanged(projectScope); // l'ancienne portée a changé aussi
            return choice;
        }

        private void OpenOptionsMenu(Button anchor)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom
            };
            var pin = new MenuItem
            {
                Header = "Épingler le Lexique au rail",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = AppSettings.LexiconPinned,
                [ToolTip.TipProperty] = "Un onglet permanent dans la colonne de droite : la définition "
                    + "du mot choisi par clic droit › « Afficher la définition »"
            };
            pin.Click += delegate
            {
                var handler = LexiconPinToggled;
                if (handler != null) handler(pin.IsChecked);
            };
            menu.Items.Add(pin);
            menu.Items.Add(new Separator());
            var import = new MenuItem
            {
                Header = "Importer un dictionnaire…",
                [ToolTip.TipProperty] = "Un fichier Marabook (.json) ou une liste de mots (.txt, .dic de Word, "
                    + "LibreOffice, Hunspell, Scrivener…) — les mots déjà présents sont gardés"
            };
            var importProject = new MenuItem { Header = "Dans le dictionnaire du projet" };
            importProject.Click += delegate { ImportInto(true); };
            var importAll = new MenuItem { Header = "Dans le dictionnaire de tous les projets" };
            importAll.Click += delegate { ImportInto(false); };
            import.Items.Add(importProject);
            import.Items.Add(importAll);
            menu.Items.Add(import);
            var export = new MenuItem
            {
                Header = "Exporter un dictionnaire…",
                [ToolTip.TipProperty] = "Fichier Marabook (.json, natures et définitions), liste de mots (.txt) "
                    + "ou dictionnaire personnel Word (.dic)"
            };
            var exportProject = new MenuItem { Header = "Le dictionnaire du projet" };
            exportProject.Click += delegate { ExportFrom(true); };
            var exportAll = new MenuItem { Header = "Le dictionnaire de tous les projets" };
            exportAll.Click += delegate { ExportFrom(false); };
            export.Items.Add(exportProject);
            export.Items.Add(exportAll);
            menu.Items.Add(export);
            menu.Open();
        }

        private async void ImportInto(bool projectScope)
        {
            var list = Target(projectScope);
            if (list == null) return;
            var owner = Ui.OwnerOf(this);
            var dialogPath = await Ui.PickOpenFile(this, projectScope ? "Importer dans le dictionnaire du projet"
                    : "Importer dans le dictionnaire de tous les projets", LexiconExchange.ImportFilter);
            if (dialogPath == null) return;
            try
            {
                var read = LexiconExchange.Read(dialogPath);
                var added = LexiconExchange.Import(list, read);
                Rebuild();
                if (added > 0) RaiseChanged(projectScope);
                var kept = read.Count - added;
                MessageDialog.Show(owner,
                    (added == 0 ? "Aucun mot nouveau" : added == 1 ? "1 mot ajouté" : added + " mots ajoutés")
                    + (projectScope ? " au dictionnaire du projet" : " au dictionnaire de tous les projets")
                    + (kept > 0 ? " (" + kept + " déjà présent" + (kept > 1 ? "s" : "") + ", gardé" + (kept > 1 ? "s" : "") + " tel" + (kept > 1 ? "s" : "") + " quel" + (kept > 1 ? "s" : "") + ")" : "")
                    + ".", AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(owner, "Import impossible :\n" + error.Message,
                    AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }

        private async void ExportFrom(bool projectScope)
        {
            var list = Target(projectScope);
            if (list == null) return;
            var owner = Ui.OwnerOf(this);
            var dialogPath = await Ui.PickSaveFile(this, projectScope ? "Exporter le dictionnaire du projet"
                    : "Exporter le dictionnaire de tous les projets", LexiconExchange.ExportFilter, (projectScope && _project != null ? _project.Name : "Marabook") + " - dictionnaire.json");
            if (dialogPath == null) return;
            try
            {
                LexiconExchange.Export(list, dialogPath);
                MessageDialog.Show(owner, "Export terminé :\n" + dialogPath,
                    AppInfo.Name, MessageButtons.OK, MessageIcon.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(owner, "Export impossible :\n" + error.Message,
                    AppInfo.Name, MessageButtons.OK, MessageIcon.Error);
            }
        }
    }
}
