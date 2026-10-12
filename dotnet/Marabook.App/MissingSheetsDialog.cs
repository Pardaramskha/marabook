using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Correction;
using Marabook.Model;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>LES FICHES MANQUANTES (1.0.5-a, Rémi) — le pendant des mots
    /// inconnus pour les fiches : les noms propres récurrents du spectre
    /// choisi (projet actif, tous les écrits, un livre, un dossier), avec
    /// le compte, le premier endroit, un bout de phrase, une case et la
    /// CATÉGORIE de la fiche à créer. Deux pastilles disent ce qui est déjà
    /// fait ailleurs : « fiche éponyme créée », « présent dans le
    /// dictionnaire ».</summary>
    public class MissingSheetsDialog : Window
    {
        public sealed class Choice
        {
            public string Name;
            public SheetCategory Category;
        }

        private readonly StackPanel _rows = new StackPanel();
        private readonly List<Row> _boxes = new List<Row>();
        private readonly TextBlock _status;
        private readonly Button _create;
        private readonly ComboBox _where;
        private readonly ComboBox _defaultCategory;
        private readonly List<Scope> _scopes;
        private readonly List<SheetCategory> _categories;
        private readonly Project _project;
        private List<MissingName> _names = new List<MissingName>();
        private bool _accepted;

        private sealed class Row
        {
            public CheckBox Box;
            public ComboBox Category;
            public MissingName Name;
        }

        private MissingSheetsDialog(Window owner, Project project)
        {
            _project = project;
            Title = "Fiches manquantes";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 760;
            Height = 580;
            MinWidth = 560;
            MinHeight = 380;
            CanResize = true;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            _categories = project.OrderedCategories();

            var root = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock
            {
                Text = "Les noms propres qui reviennent dans les écrits (trois fois au moins, dont deux hors début de phrase). "
                    + "Cochez ceux qui méritent une fiche et choisissez sa catégorie : les fiches se créent d'un coup, vides, à remplir.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            var tools = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            _scopes = Scopes.Build(project);
            _where = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), [ToolTip.TipProperty] = "Où relever : le projet actif, tous les écrits, un livre ou un dossier" };
            foreach (var scope in _scopes) _where.Items.Add(scope.Label);
            _where.SelectedIndex = Scopes.DefaultIndex(_scopes);
            _where.SelectionChanged += delegate { Collect(); };
            left.Children.Add(_where);
            var all = Buttons.Text("Tout cocher", "Cocher tous les noms sans fiche", Buttons.Compact, Buttons.Look.Outline);
            all.Click += delegate { SetAll(true); };
            var none = Buttons.Text("Tout décocher", "Décocher tous les noms", Buttons.Compact, Buttons.Look.Outline);
            none.Margin = new Thickness(6, 0, 0, 0);
            none.Click += delegate { SetAll(false); };
            left.Children.Add(all);
            left.Children.Add(none);
            _status = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            left.Children.Add(_status);
            tools.Children.Add(left);
            head.Children.Add(tools);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _create = new Button { Content = "Créer les fiches", IsDefault = true, MinWidth = 140 };
            _create.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Fermer", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            right.Children.Add(_create);
            right.Children.Add(cancel);
            Dialogs.Arrange(right, _create);
            DockPanel.SetDock(right, Dock.Right);
            buttons.Children.Add(right);
            var defaultRow = new StackPanel { Orientation = Orientation.Horizontal };
            defaultRow.Children.Add(new TextBlock { Text = "Catégorie proposée :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _defaultCategory = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = "La catégorie posée sur toutes les lignes ; chaque ligne peut en changer" };
            foreach (var category in _categories) _defaultCategory.Items.Add((category.IsSub ? "   › " : "") + category.Name);
            _defaultCategory.SelectedIndex = PreferredIndex();
            _defaultCategory.SelectionChanged += delegate
            {
                foreach (var row in _boxes) row.Category.SelectedIndex = _defaultCategory.SelectedIndex;
            };
            defaultRow.Children.Add(_defaultCategory);
            buttons.Children.Add(defaultRow);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            root.Children.Add(new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer { [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto, Content = _rows }
            });
            Content = root;
            _create.IsEnabled = false;
            Collect();
        }

        /// <summary>« Personnage » d'office, sinon la première catégorie.</summary>
        private int PreferredIndex()
        {
            for (var i = 0; i < _categories.Count; i++)
                if (string.Equals(_categories[i].Name, "Personnage", StringComparison.CurrentCultureIgnoreCase)) return i;
            return _categories.Count > 0 ? 0 : -1;
        }

        private async void Collect()
        {
            var project = _project;
            var index = _where.SelectedIndex;
            var scope = index >= 0 && index < _scopes.Count ? _scopes[index].Texts : null;
            _rows.Children.Clear();
            _rows.Children.Add(new TextBlock { Text = "Relevé en cours…", Foreground = Chrome.SoftText, Margin = new Thickness(12) });
            var projectLexicon = project.Lexicon;
            var globalLexicon = AppSettings.Lexicon;
            Func<string, bool> inLexicon = delegate(string word)
            {
                return LexiconEntry.FindByForm(projectLexicon, word) != null || LexiconEntry.FindByForm(globalLexicon, word) != null;
            };
            List<MissingName> names;
            try { names = await Task.Run(delegate { return MissingNames.Collect(project, scope, inLexicon); }); }
            catch (Exception error) { names = new List<MissingName>(); Console.Error.WriteLine("[fiches manquantes] " + error); }
            _names = names;
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rows.Children.Clear();
            _boxes.Clear();
            foreach (var name in _names)
            {
                var row = new DockPanel { Margin = new Thickness(8, 3, 8, 3) };
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0), IsEnabled = !name.HasSheet };
                box.IsCheckedChanged += delegate { UpdateStatus(); };
                DockPanel.SetDock(box, Dock.Left);
                row.Children.Add(box);
                var category = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0), IsEnabled = !name.HasSheet, [ToolTip.TipProperty] = "La catégorie de la fiche à créer" };
                foreach (var candidate in _categories) category.Items.Add((candidate.IsSub ? "   › " : "") + candidate.Name);
                category.SelectedIndex = _defaultCategory.SelectedIndex;
                var boxRef = box;
                category.SelectionChanged += delegate { if (boxRef.IsEnabled && boxRef.IsChecked != true) boxRef.IsChecked = true; };
                DockPanel.SetDock(category, Dock.Right);
                row.Children.Add(category);
                var text = new StackPanel();
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new TextBlock { Text = name.Name, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, FontSize = 13 });
                line.Children.Add(new TextBlock
                {
                    Text = (name.Count == 1 ? "1 fois" : name.Count + " fois") + (name.Where.Length > 0 ? " · d'abord " + name.Where : ""),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                });
                if (name.HasSheet) line.Children.Add(UnknownWordsDialog.Pill("fiche éponyme créée", Chrome.Ok));
                if (name.InLexicon) line.Children.Add(UnknownWordsDialog.Pill("présent dans le dictionnaire", Chrome.Accent));
                text.Children.Add(line);
                text.Children.Add(new TextBlock { Text = name.Context, Foreground = Chrome.FaintText, FontSize = 11, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });
                row.Children.Add(text);
                _rows.Children.Add(row);
                _boxes.Add(new Row { Box = box, Category = category, Name = name });
            }
            if (_names.Count == 0)
                _rows.Children.Add(new TextBlock { Text = "Aucun nom propre récurrent dans ce spectre.", Foreground = Chrome.SoftText, Margin = new Thickness(12) });
            UpdateStatus();
        }

        private void SetAll(bool value)
        {
            foreach (var row in _boxes) if (row.Box.IsEnabled) row.Box.IsChecked = value;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var selected = 0;
            var done = 0;
            foreach (var row in _boxes) { if (row.Box.IsChecked == true && row.Box.IsEnabled) selected++; if (row.Name.HasSheet) done++; }
            _status.Text = _names.Count == 0 ? "" : (_names.Count == 1 ? "1 nom" : _names.Count + " noms")
                + (done > 0 ? " · " + done + (done == 1 ? " a déjà sa fiche" : " ont déjà leur fiche") : "")
                + (selected > 0 ? " · " + selected + (selected == 1 ? " coché" : " cochés") : "");
            _create.Content = selected > 0 ? "Créer les fiches (" + selected + ")" : "Créer les fiches";
            _create.IsEnabled = selected > 0;
        }

        /// <summary>Sonde : le relevé affiché et le spectre choisi.</summary>
        internal List<MissingName> NamesForProbe { get { return _names; } }
        internal string ScopeLabelForProbe { get { return _where.SelectedItem as string ?? ""; } }

        /// <summary>Les noms cochés avec leur catégorie, ou null si fermé sans créer.</summary>
        public static async Task<List<Choice>> Ask(Window owner, Project project)
        {
            var dialog = new MissingSheetsDialog(owner, project);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var choices = new List<Choice>();
            foreach (var row in dialog._boxes)
            {
                if (row.Box.IsChecked != true || !row.Box.IsEnabled) continue;
                var index = row.Category.SelectedIndex;
                choices.Add(new Choice { Name = row.Name.Name, Category = index >= 0 && index < dialog._categories.Count ? dialog._categories[index] : null });
            }
            return choices;
        }
    }
}
