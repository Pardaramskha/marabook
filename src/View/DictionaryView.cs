using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UniversSale.Model;
using UniversSale.Settings;

namespace UniversSale.View
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

            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            SetDock(bar, Dock.Top);
            var barRow = new DockPanel();
            var newEntry = new Button
            {
                Content = Icons.Label("plus-bold", "Nouvelle entrée…", 11, Chrome.Ink),
                Padding = new Thickness(10, 3, 10, 3),
                ToolTip = "Ajouter un mot au dictionnaire personnel avec sa nature grammaticale"
            };
            newEntry.Click += delegate { NewEntry(true); };
            DockPanel.SetDock(newEntry, Dock.Right);
            barRow.Children.Add(newEntry);
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var glass = new TextBlock
            {
                Text = "🔍",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(glass, Dock.Left);
            searchRow.Children.Add(glass);
            _searchBox = new TextBox
            {
                MaxWidth = 340,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 220,
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Filtrer les entrées (mot ou forme acceptée)"
            };
            _searchBox.TextChanged += delegate { Rebuild(); };
            searchRow.Children.Add(_searchBox);
            barRow.Children.Add(searchRow);
            bar.Child = barRow;
            Children.Add(bar);

            _sections = new StackPanel { Margin = new Thickness(16, 12, 16, 24), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
            Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
        public void NewEntry(bool projectScope)
        {
            var entry = LexiconEntryDialog.Ask(Window.GetWindow(this), null, ref projectScope);
            if (entry == null) return;
            AddEntry(entry, projectScope);
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

        private void Rebuild()
        {
            _sections.Children.Clear();
            _sections.Children.Add(new TextBlock
            {
                Text = "Dictionnaire personnel",
                Foreground = Chrome.Ink,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
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
                ToolTip = "Nouvelle entrée dans « " + caption + " »"
            };
            add.Click += delegate { NewEntry(projectScope); };
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);
            header.Children.Add(new TextBlock
            {
                Text = caption + (entries == null ? " (aucun projet ouvert)"
                    : " — " + entries.Count + (entries.Count > 1 ? " entrées" : " entrée")),
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            _sections.Children.Add(header);
            _sections.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, 6) });
            if (entries == null) return;

            var filter = _searchBox.Text.Trim();
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

        private UIElement BuildRow(LexiconEntry entry, List<LexiconEntry> list, bool projectScope)
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
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = entry.Word,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
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
            forms.Children.Add(new TextBlock
            {
                Text = LexiconInflector.Preview(entry, 12),
                Foreground = Chrome.Ink,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = string.Join("\n", entry.Forms().ToArray())
            });
            if (entry.Note.Length > 0)
                forms.Children.Add(new TextBlock
                {
                    Text = entry.Note,
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap
                });
            Grid.SetColumn(forms, 1);
            grid.Children.Add(forms);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edit = new Button { Content = "Modifier…", Padding = new Thickness(8, 2, 8, 2), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            edit.Click += delegate
            {
                var scope = projectScope;
                var edited = LexiconEntryDialog.Ask(Window.GetWindow(this), entry, ref scope);
                if (edited == null) return;
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
    }

    /// <summary>Le dialogue d'une entrée du dictionnaire (batch 33) : le mot,
    /// sa nature, genre, pluriel, féminin, portée (projet / tous les
    /// projets) — avec l'aperçu VIVANT des formes que le correcteur en
    /// acceptera. Rend l'entrée validée, ou null.</summary>
    public class LexiconEntryDialog : Window
    {
        private readonly TextBox _word, _feminine, _note;
        private readonly ComboBox _class, _gender, _plural, _scope;
        private readonly TextBlock _preview;
        private readonly StackPanel _nominal, _feminineRow;
        private bool _accepted;

        private LexiconEntryDialog(Window owner, LexiconEntry initial, bool projectScope, bool allowScope)
        {
            Title = initial == null ? "Nouvelle entrée du dictionnaire" : "Entrée du dictionnaire";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 380 };

            panel.Children.Add(Label("Mot :"));
            _word = new TextBox { Text = initial == null ? "" : initial.Word };
            _word.TextChanged += delegate { UpdatePreview(); };
            panel.Children.Add(_word);

            panel.Children.Add(Label("Nature :"));
            _class = new ComboBox();
            foreach (var key in LexiconEntry.Classes) _class.Items.Add(LexiconEntry.ClassLabel(key));
            _class.SelectedIndex = Array.IndexOf(LexiconEntry.Classes,
                initial == null ? LexiconEntry.ClassNoun : initial.Class);
            if (_class.SelectedIndex < 0) _class.SelectedIndex = 0;
            _class.SelectionChanged += delegate { UpdateVisibility(); UpdatePreview(); };
            panel.Children.Add(_class);

            _nominal = new StackPanel();
            var pair = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var genderCol = new StackPanel();
            genderCol.Children.Add(Label("Genre :"));
            _gender = new ComboBox();
            _gender.Items.Add("—");
            _gender.Items.Add("masculin");
            _gender.Items.Add("féminin");
            _gender.SelectedIndex = initial == null ? 0 : initial.Gender == "m" ? 1 : initial.Gender == "f" ? 2 : 0;
            _gender.SelectionChanged += delegate { UpdatePreview(); };
            genderCol.Children.Add(_gender);
            Grid.SetColumn(genderCol, 0);
            pair.Children.Add(genderCol);
            var pluralCol = new StackPanel();
            pluralCol.Children.Add(Label("Pluriel :"));
            _plural = new ComboBox();
            _plural.Items.Add("régulier (-s)");
            _plural.Items.Add("en -x (-al → -aux, -eau → -eaux)");
            _plural.Items.Add("invariable");
            _plural.SelectedIndex = initial == null ? 0
                : initial.Plural == LexiconEntry.PluralX ? 1
                : initial.Plural == LexiconEntry.PluralInvariable ? 2 : 0;
            _plural.SelectionChanged += delegate { UpdatePreview(); };
            pluralCol.Children.Add(_plural);
            Grid.SetColumn(pluralCol, 2);
            pair.Children.Add(pluralCol);
            _nominal.Children.Add(pair);
            _feminineRow = new StackPanel();
            _feminineRow.Children.Add(Label("Féminin (vide = dérivé par la règle) :"));
            _feminine = new TextBox { Text = initial == null ? "" : initial.Feminine };
            _feminine.TextChanged += delegate { UpdatePreview(); };
            _feminineRow.Children.Add(_feminine);
            _nominal.Children.Add(_feminineRow);
            panel.Children.Add(_nominal);

            panel.Children.Add(Label("Note (facultative) :"));
            _note = new TextBox { Text = initial == null ? "" : initial.Note };
            panel.Children.Add(_note);

            panel.Children.Add(Label("Portée :"));
            _scope = new ComboBox { IsEnabled = allowScope };
            _scope.Items.Add("Ce projet (enregistré dans le .plot)");
            _scope.Items.Add("Tous les projets");
            _scope.SelectedIndex = projectScope ? 0 : 1;
            panel.Children.Add(_scope);

            panel.Children.Add(Label("Formes reconnues par le correcteur :"));
            _preview = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 34,
                Margin = new Thickness(0, 2, 0, 0)
            };
            panel.Children.Add(_preview);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate
            {
                if (_word.Text.Trim().Length == 0) { _word.Focus(); return; }
                _accepted = true;
                Close();
            };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            UpdateVisibility();
            UpdatePreview();
            Loaded += delegate { _word.Focus(); _word.SelectAll(); };
        }

        private string SelectedClass()
        {
            var index = _class.SelectedIndex;
            return index < 0 ? LexiconEntry.ClassOther : LexiconEntry.Classes[index];
        }

        private void UpdateVisibility()
        {
            var cls = SelectedClass();
            var nominal = cls == LexiconEntry.ClassNoun || cls == LexiconEntry.ClassProper || cls == LexiconEntry.ClassAdjective;
            _nominal.Visibility = nominal ? Visibility.Visible : Visibility.Collapsed;
            _feminineRow.Visibility = cls == LexiconEntry.ClassAdjective || cls == LexiconEntry.ClassNoun
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private LexiconEntry Build()
        {
            return new LexiconEntry
            {
                Word = _word.Text.Trim(),
                Class = SelectedClass(),
                Gender = _gender.SelectedIndex == 1 ? "m" : _gender.SelectedIndex == 2 ? "f" : "",
                Plural = _plural.SelectedIndex == 1 ? LexiconEntry.PluralX
                       : _plural.SelectedIndex == 2 ? LexiconEntry.PluralInvariable : "",
                Feminine = _feminine.Text.Trim(),
                Note = _note.Text.Trim()
            };
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            var entry = Build();
            _preview.Text = entry.Word.Length == 0 ? "—" : LexiconInflector.Preview(entry, 14);
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 3)
            };
        }

        /// <summary>Ouvre le dialogue : initial null = nouvelle entrée ;
        /// projectScope entre en proposition et sort en choix. Null si annulé.</summary>
        public static LexiconEntry Ask(Window owner, LexiconEntry initial, ref bool projectScope)
        {
            var dialog = new LexiconEntryDialog(owner, initial, projectScope, true);
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            projectScope = dialog._scope.SelectedIndex == 0;
            return dialog.Build();
        }

        /// <summary>Variante « Ajouter au dictionnaire » : mot signalé
        /// pré-rempli, nature devinée, portée fixée par le sous-menu.</summary>
        public static LexiconEntry AskForWord(Window owner, string word, bool projectScope)
        {
            var initial = new LexiconEntry { Word = word ?? "", Class = LexiconInflector.GuessClass(word) };
            var dialog = new LexiconEntryDialog(owner, initial, projectScope, false);
            dialog.Title = "Ajouter au dictionnaire";
            dialog.ShowDialog();
            return dialog._accepted ? dialog.Build() : null;
        }
    }
}
