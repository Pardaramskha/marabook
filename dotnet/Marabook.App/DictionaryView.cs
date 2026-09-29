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
        private readonly ComboBox _classFilter, _pageSize; // nature affichée, entrées par page (29/09)
        private static readonly int[] PageSizes = { 25, 50, 100, 250 };
        private readonly int[] _pages = { 0, 0 }; // page courante : [0] ce projet, [1] tous les projets

        private void ResetPages() { _pages[0] = 0; _pages[1] = 0; }

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
            _searchBox.TextChanged += delegate { ResetPages(); Rebuild(); };
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
            // Le filtre par nature (29/09) : toutes, ou une seule (nom propre,
            // nom commun, adjectif…) ; et la taille de page — 25 par défaut.
            _classFilter = new ComboBox { Margin = new Thickness(14, 0, 0, 0), MinWidth = 150, VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = "N'afficher qu'une nature grammaticale" };
            _classFilter.Items.Add("Toutes natures");
            foreach (var key in LexiconEntry.Classes) _classFilter.Items.Add(LexiconEntry.ClassLabel(key));
            _classFilter.SelectedIndex = 0;
            _classFilter.SelectionChanged += delegate { ResetPages(); Rebuild(); };
            left.Children.Add(_classFilter);
            _pageSize = new ComboBox { Margin = new Thickness(8, 0, 0, 0), MinWidth = 110, VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = "Entrées par page" };
            foreach (var size in PageSizes) _pageSize.Items.Add(size + " par page");
            _pageSize.SelectedIndex = 0;
            _pageSize.SelectionChanged += delegate { ResetPages(); Rebuild(); };
            left.Children.Add(_pageSize);
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
            var classIndex = _classFilter == null ? 0 : _classFilter.SelectedIndex;
            var wantedClass = classIndex <= 0 ? null : LexiconEntry.Classes[classIndex - 1];
            var sorted = new List<LexiconEntry>(entries);
            sorted.Sort(delegate(LexiconEntry a, LexiconEntry b)
            { return string.Compare(a.Word, b.Word, StringComparison.CurrentCultureIgnoreCase); });
            var matching = new List<LexiconEntry>();
            foreach (var entry in sorted)
            {
                if (wantedClass != null && entry.Class != wantedClass) continue;
                if (filter.Length > 0 && !Matches(entry, filter)) continue;
                matching.Add(entry);
            }
            if (matching.Count == 0)
            {
                _sections.Children.Add(new TextBlock
                {
                    Text = filter.Length > 0 || wantedClass != null ? "Aucune entrée ne correspond." : "Aucune entrée.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 0, 6)
                });
                return;
            }
            // La pagination (29/09) : « n par page », un sélecteur ‹ 1 / N ›
            // par section — chaque section garde sa page.
            var slot = projectScope ? 0 : 1;
            var pageSize = PageSizes[Math.Max(0, _pageSize == null ? 0 : _pageSize.SelectedIndex)];
            var pageCount = (matching.Count + pageSize - 1) / pageSize;
            if (_pages[slot] >= pageCount) _pages[slot] = pageCount - 1;
            if (_pages[slot] < 0) _pages[slot] = 0;
            var page = _pages[slot];
            for (var i = page * pageSize; i < Math.Min(matching.Count, (page + 1) * pageSize); i++)
                _sections.Children.Add(BuildRow(matching[i], entries, projectScope));
            if (pageCount > 1) _sections.Children.Add(BuildPager(slot, page, pageCount, matching.Count));
        }

        /// <summary>Le sélecteur de page d'une section : ‹, « page x sur N »
        /// (le compte des entrées filtrées), ›.</summary>
        private Control BuildPager(int slot, int page, int pageCount, int total)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 8) };
            var previous = Buttons.Text("‹ Précédente", "Page précédente", Buttons.Compact, Buttons.Look.Calm);
            previous.IsEnabled = page > 0;
            previous.Click += delegate { _pages[slot] = Math.Max(0, _pages[slot] - 1); Rebuild(); };
            row.Children.Add(previous);
            row.Children.Add(new TextBlock
            {
                Text = "page " + (page + 1) + " sur " + pageCount + " — " + total + (total > 1 ? " entrées" : " entrée"),
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            });
            var next = Buttons.Text("Suivante ›", "Page suivante", Buttons.Compact, Buttons.Look.Calm);
            next.IsEnabled = page < pageCount - 1;
            next.Click += delegate { _pages[slot] = Math.Min(pageCount - 1, _pages[slot] + 1); Rebuild(); };
            row.Children.Add(next);
            return row;
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
                TextWrapping = TextWrapping.Wrap
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
            var remove = new Button { Content = "Retirer", Padding = new Thickness(8, 2, 8, 2), FontSize = 11 };
            remove.Click += async delegate
            {
                // Retirer une entrée ne se rattrape pas (29/09) : on demande.
                var answer = await MessageDialog.Show(Ui.OwnerOf(this),
                    "Retirer « " + entry.Word + " » du dictionnaire ?\n\nLe correcteur soulignera de nouveau ce mot et ses formes.",
                    "Dictionnaire", MessageButtons.YesNo, MessageIcon.Question);
                if (answer != MessageResult.Yes) return;
                list.Remove(entry);
                Rebuild();
                RaiseChanged(projectScope);
            };
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            // Un clic sur la rangée ouvre l'entrée (29/09) — plus de bouton
            // « Modifier… » ; « Retirer » garde son bouton.
            row.Cursor = new Cursor(StandardCursorType.Hand); // sans infobulle (29/09) : le survol suffit
            row.PointerEntered += delegate { row.BorderBrush = Chrome.Accent; };
            row.PointerExited += delegate { row.BorderBrush = Chrome.Border; };
            row.PointerReleased += async delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (e.Source is Visual && IsInside((Visual)e.Source, actions)) return; // le bouton Retirer
                var choice = await LexiconEntryDialog.Ask(Ui.OwnerOf(this), entry, projectScope);
                if (choice == null) return;
                var edited = choice.Entry;
                var scope = choice.ProjectScope;
                list.Remove(entry);
                AddEntry(edited, scope);
                if (scope != projectScope) RaiseChanged(projectScope); // l'ancienne portée a changé aussi
            };

            row.Child = grid;
            return row;
        }

        private static bool IsInside(Visual source, Visual ancestor)
        {
            var current = source;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.GetVisualParent();
            }
            return false;
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
            Ui.ShowMenu(menu, anchor); // une cible, sinon Avalonia lève (28/09)
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
