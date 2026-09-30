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
    /// <summary>Le dialogue d'une entrée du dictionnaire (batch 33) : le mot,
    /// sa nature, genre, pluriel, féminin, portée (projet / tous les
    /// projets) — avec l'aperçu VIVANT des formes que le correcteur en
    /// acceptera. Rend l'entrée validée, ou null.</summary>
    public class LexiconEntryDialog : Window
    {
        private readonly TextBox _word, _feminine, _definition, _note;
        private readonly ComboBox _class, _gender, _plural, _scope;
        private readonly TextBlock _preview;
        private readonly StackPanel _nominal, _feminineRow;
        private bool _accepted;

        private LexiconEntryDialog(Window owner, LexiconEntry initial, bool projectScope, bool allowScope,
            IList<string> suggestions = null)
        {
            Title = initial == null ? "Nouvelle entrée du dictionnaire" : "Entrée du dictionnaire";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 520 };

            // Les suggestions (29/09) : depuis une fiche, chaque mot du titre
            // et — pour un personnage — le nom, le prénom et l'alias, en chips
            // tout en haut ; un clic préremplit le mot (et devine sa nature).
            if (suggestions != null && suggestions.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Suggestions de la fiche :",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 3)
                });
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var suggestion in suggestions)
                {
                    var wordRef = suggestion;
                    var chip = Buttons.Text(suggestion, "Préremplir « " + suggestion + " »", Buttons.Compact, Buttons.Look.Outline);
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    chip.Click += delegate
                    {
                        _word.Text = wordRef;
                        var guessed = Array.IndexOf(LexiconEntry.Classes, LexiconInflector.GuessClass(wordRef));
                        if (guessed >= 0) _class.SelectedIndex = guessed;
                        _word.Focus();
                        _word.SelectAll();
                    };
                    chips.Children.Add(chip);
                }
                panel.Children.Add(chips);
            }

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
            _class.SelectionChanged += delegate
            {
                // Un nom propre est invariable d'office (29/09) : l'accord
                // bascule aussitôt — modifiable ensuite si besoin.
                if (SelectedClass() == LexiconEntry.ClassProper && _plural != null) _plural.SelectedIndex = 2;
                UpdateVisibility();
                UpdatePreview();
            };
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

            panel.Children.Add(Label("Définition :"));
            _definition = new TextBox
            {
                Text = initial == null ? "" : initial.Definition,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Height = 72
            };
            panel.Children.Add(_definition);

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
                if ((_word.Text ?? "").Trim().Length == 0) { _word.Focus(); return; }
                _accepted = true;
                Close();
            };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok); // validation à droite, principale (30/09)
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
            _nominal.IsVisible = nominal ? true : false;
            _feminineRow.IsVisible = cls == LexiconEntry.ClassAdjective || cls == LexiconEntry.ClassNoun
                ? true : false;
        }

        private LexiconEntry Build()
        {
            return new LexiconEntry
            {
                Word = (_word.Text ?? "").Trim(),
                Class = SelectedClass(),
                Gender = _gender.SelectedIndex == 1 ? "m" : _gender.SelectedIndex == 2 ? "f" : "",
                Plural = _plural.SelectedIndex == 1 ? LexiconEntry.PluralX
                       : _plural.SelectedIndex == 2 ? LexiconEntry.PluralInvariable : "",
                Feminine = (_feminine.Text ?? "").Trim(),
                Definition = (_definition.Text ?? "").Trim(),
                Note = (_note.Text ?? "").Trim()
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

        /// <summary>Le choix du dialogue : l'entrée bâtie et la portée retenue
        /// (Avalonia : le dialogue est asynchrone, plus de paramètre ref).</summary>
        public sealed class Choice
        {
            public LexiconEntry Entry;
            public bool ProjectScope;
        }

        /// <summary>Ouvre le dialogue : initial null = nouvelle entrée ;
        /// projectScope entre en proposition, la portée choisie sort dans le
        /// Choice. Null si annulé.</summary>
        public static async Task<Choice> Ask(Window owner, LexiconEntry initial, bool projectScope)
        {
            var dialog = new LexiconEntryDialog(owner, initial, projectScope, true);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            return new Choice { Entry = dialog.Build(), ProjectScope = dialog._scope.SelectedIndex == 0 };
        }

        /// <summary>Variante « Ajouter au dictionnaire » : mot signalé
        /// pré-rempli, nature devinée, portée fixée par le sous-menu.</summary>
        public static Task<LexiconEntry> AskForWord(Window owner, string word, bool projectScope)
        {
            return AskForWord(owner, word, projectScope, null);
        }

        /// <summary>Même dialogue, avec des chips de suggestions en tête
        /// (les mots d'une fiche, 29/09) — un clic préremplit le mot.</summary>
        public static async Task<LexiconEntry> AskForWord(Window owner, string word, bool projectScope, IList<string> suggestions)
        {
            var initial = new LexiconEntry { Word = word ?? "", Class = LexiconInflector.GuessClass(word) };
            var dialog = new LexiconEntryDialog(owner, initial, projectScope, false, suggestions);
            dialog.Title = "Ajouter au dictionnaire";
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted ? dialog.Build() : null;
        }
    }
}
