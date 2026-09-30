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
    /// <summary>Le dialogue d'une entrée du dictionnaire (batch 33, refondu le
    /// 30/09 à la manière d'Antidote) : le mot, son TYPE (Nom, Adjectif,
    /// Adverbe, Nom propre, Verbe), sa NATURE — des cases propres au type,
    /// des boutons radio pour la sorte d'un nom propre —, sa FLEXION
    /// (masculin et féminin, masculin, féminin) avec les quatre formes en
    /// filigrane, dérivées par la règle et modifiables à la main, la
    /// définition, la note, la portée (projet / tous les projets) — et
    /// l'aperçu VIVANT des formes que le correcteur acceptera. Une entrée
    /// migrée de l'ancien format porte la pastille orange « migration
    /// nécessaire » ; la validation la lève. Rend l'entrée validée, ou null.</summary>
    public class LexiconEntryDialog : Window
    {
        private readonly TextBox _word, _definition, _note;
        private readonly TextBox[] _forms = new TextBox[4]; // masc. sg., masc. pl., fém. sg., fém. pl.
        private readonly ComboBox _scope;
        private readonly TextBlock _preview;
        private readonly Border _reviewPill;
        private readonly Dictionary<string, RadioButton> _classRadios = new Dictionary<string, RadioButton>();
        private readonly Dictionary<string, RadioButton> _genderRadios = new Dictionary<string, RadioButton>();
        private readonly Dictionary<string, RadioButton> _properRadios = new Dictionary<string, RadioButton>();
        private readonly Dictionary<string, CheckBox> _traitBoxes = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, StackPanel> _naturePanels = new Dictionary<string, StackPanel>();
        private readonly StackPanel _natureHost, _flexion;
        private readonly CheckBox _invariable;
        private readonly LexiconEntry _initial;
        private bool _accepted, _syncing;

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
            _initial = initial;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 600 };

            // Les suggestions (29/09) : depuis une fiche, chaque mot du titre
            // et — pour un personnage — le nom, le prénom et l'alias, en chips
            // tout en haut ; un clic préremplit le mot (et devine son type).
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
                        SelectClass(LexiconInflector.GuessClass(wordRef));
                        _word.Focus();
                        _word.SelectAll();
                    };
                    chips.Children.Add(chip);
                }
                panel.Children.Add(chips);
            }

            // La pastille « migration nécessaire » (30/09) : l'entrée vient de
            // l'ancien dictionnaire ; type, nature et flexion sont à confirmer.
            _reviewPill = new Border
            {
                Background = Chrome.Warn,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = initial != null && initial.NeedsReview,
                Child = new TextBlock
                {
                    Text = "Migration nécessaire — cette entrée vient de l'ancien dictionnaire : précisez son type, sa nature et sa flexion ; Valider lève la pastille.",
                    Foreground = Chrome.PrintPaper,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            panel.Children.Add(_reviewPill);

            panel.Children.Add(Label("Mot :"));
            _word = new TextBox { Text = initial == null ? "" : initial.Word };
            _word.TextChanged += delegate { RefreshForms(); };
            panel.Children.Add(_word);

            // ---- le type
            panel.Children.Add(Label("Type de mot :"));
            var classes = new WrapPanel();
            var initialClass = initial == null ? LexiconEntry.ClassNoun : initial.Class;
            foreach (var key in LexiconEntry.Classes)
            {
                // « Autre » n'est offert qu'à une entrée qui l'est déjà (mot
                // importé ou migré) : on choisit un vrai type pour les autres.
                if (key == LexiconEntry.ClassOther && initialClass != LexiconEntry.ClassOther) continue;
                var radio = new RadioButton
                {
                    Content = LexiconEntry.ClassLabel(key),
                    GroupName = "lexicon-class",
                    IsChecked = key == initialClass,
                    Margin = new Thickness(0, 0, 16, 2)
                };
                var keyRef = key;
                radio.IsCheckedChanged += delegate { if (radio.IsChecked == true) OnClassChanged(keyRef); };
                _classRadios[key] = radio;
                classes.Children.Add(radio);
            }
            panel.Children.Add(classes);

            // ---- la nature : un panneau par type, un seul visible
            _natureHost = new StackPanel();
            _natureHost.Children.Add(Label("Nature :"));
            _naturePanels[LexiconEntry.ClassNoun] = BuildTraitPanel(LexiconEntry.ClassNoun, initial);
            _naturePanels[LexiconEntry.ClassAdjective] = BuildTraitPanel(LexiconEntry.ClassAdjective, initial);
            _naturePanels[LexiconEntry.ClassAdverb] = BuildTraitPanel(LexiconEntry.ClassAdverb, initial);
            _naturePanels[LexiconEntry.ClassProper] = BuildProperPanel(initial);
            foreach (var pair in _naturePanels) _natureHost.Children.Add(pair.Value);
            panel.Children.Add(_natureHost);

            // ---- la flexion : un choix, puis les quatre formes
            _flexion = new StackPanel();
            _flexion.Children.Add(Label("Flexion :"));
            var genders = new WrapPanel();
            var initialGenders = initial == null ? "" : initial.EffectiveGenders();
            foreach (var pair in new[] { new[] { LexiconEntry.GendersBoth, "Masculin et féminin" }, new[] { LexiconEntry.GendersMasculine, "Masculin" }, new[] { LexiconEntry.GendersFeminine, "Féminin" } })
            {
                var radio = new RadioButton
                {
                    Content = pair[1],
                    GroupName = "lexicon-genders",
                    IsChecked = pair[0] == initialGenders,
                    Margin = new Thickness(0, 0, 16, 2)
                };
                radio.IsCheckedChanged += delegate { if (!_syncing) RefreshForms(); };
                _genderRadios[pair[0]] = radio;
                genders.Children.Add(radio);
            }
            _invariable = new CheckBox
            {
                Content = "Invariable au pluriel",
                IsChecked = initial != null && initial.Plural == LexiconEntry.PluralInvariable,
                Margin = new Thickness(8, 0, 0, 2)
            };
            _invariable.IsCheckedChanged += delegate { if (!_syncing) RefreshForms(); };
            genders.Children.Add(_invariable);
            _flexion.Children.Add(genders);
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var captions = new[] { "masc. sg.", "masc. pl.", "fém. sg.", "fém. pl." };
            var initialForms = initial == null ? new string[4] : new[] { initial.MascSg, initial.MascPl, initial.FemSg, initial.FemPl };
            for (var i = 0; i < 4; i++)
            {
                var cell = new StackPanel();
                cell.Children.Add(new TextBlock { Text = captions[i], Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
                var box = new TextBox { Text = initialForms[i] ?? "", [ToolTip.TipProperty] = "Vide : la forme dérivée par la règle (en filigrane) ; tapez pour la remplacer" };
                box.TextChanged += delegate { if (!_syncing) RefreshPreview(); };
                cell.Children.Add(box);
                _forms[i] = box;
                Grid.SetRow(cell, i / 2);
                Grid.SetColumn(cell, (i % 2) * 2);
                cell.Margin = new Thickness(0, i / 2 == 1 ? 6 : 0, 0, 0);
                grid.Children.Add(cell);
            }
            _flexion.Children.Add(grid);
            panel.Children.Add(_flexion);

            panel.Children.Add(Label("Définition :"));
            _definition = new TextBox
            {
                Text = initial == null ? "" : initial.Definition,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Height = 64
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

            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 820,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled
            };
            UpdateVisibility();
            RefreshForms();
            Loaded += delegate { _word.Focus(); _word.SelectAll(); };
        }

        // ------------------------------------------------------------ natures

        /// <summary>Les cases d'un type : une par nature, les sous-natures en
        /// retrait et actives seulement quand la mère est cochée.</summary>
        private StackPanel BuildTraitPanel(string cls, LexiconEntry initial)
        {
            var host = new StackPanel();
            foreach (var trait in LexiconEntry.TraitsFor(cls))
            {
                if (trait.ProperKind != null) continue;
                var box = new CheckBox
                {
                    Content = trait.Label,
                    IsChecked = initial != null && initial.HasTrait(trait.Key),
                    Margin = new Thickness(trait.Parent != null ? 26 : 0, 2, 0, 0)
                };
                _traitBoxes[trait.Key] = box;
                var traitRef = trait;
                box.IsCheckedChanged += delegate { UpdateChildren(traitRef.Key); };
                host.Children.Add(box);
            }
            foreach (var trait in LexiconEntry.TraitsFor(cls)) UpdateChildren(trait.Key);
            return host;
        }

        /// <summary>Les sous-natures d'une nature : actives si elle est cochée,
        /// décochées sinon.</summary>
        private void UpdateChildren(string parentKey)
        {
            CheckBox parent;
            if (!_traitBoxes.TryGetValue(parentKey, out parent)) return;
            foreach (var trait in LexiconEntry.TraitCatalog)
            {
                if (trait.Parent != parentKey) continue;
                CheckBox child;
                if (!_traitBoxes.TryGetValue(trait.Key, out child)) continue;
                child.IsEnabled = parent.IsChecked == true;
                if (parent.IsChecked != true) child.IsChecked = false;
            }
        }

        /// <summary>Le nom propre : sa sorte en boutons radio ; sous « Lieu »,
        /// les cases de ce qu'il est ; sous « Gentilé », la case « Langue ».</summary>
        private StackPanel BuildProperPanel(LexiconEntry initial)
        {
            var host = new StackPanel();
            var initialKind = initial == null ? "" : initial.ProperKind;
            foreach (var kind in LexiconEntry.ProperKinds)
            {
                var radio = new RadioButton
                {
                    Content = LexiconEntry.ProperKindLabel(kind),
                    GroupName = "lexicon-proper",
                    IsChecked = kind == initialKind,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                _properRadios[kind] = radio;
                radio.IsCheckedChanged += delegate { UpdateProperChildren(); };
                host.Children.Add(radio);
                var children = new WrapPanel { Margin = new Thickness(26, 0, 0, 0) };
                foreach (var trait in LexiconEntry.TraitsFor(LexiconEntry.ClassProper))
                {
                    if (trait.ProperKind != kind) continue;
                    var box = new CheckBox
                    {
                        Content = trait.Label,
                        IsChecked = initial != null && initial.HasTrait(trait.Key),
                        Margin = new Thickness(0, 2, 14, 0)
                    };
                    _traitBoxes[trait.Key] = box;
                    children.Children.Add(box);
                }
                if (children.Children.Count > 0) host.Children.Add(children);
            }
            UpdateProperChildren();
            return host;
        }

        private void UpdateProperChildren()
        {
            var kind = SelectedProperKind();
            foreach (var trait in LexiconEntry.TraitsFor(LexiconEntry.ClassProper))
            {
                CheckBox box;
                if (trait.ProperKind == null || !_traitBoxes.TryGetValue(trait.Key, out box)) continue;
                box.IsEnabled = trait.ProperKind == kind;
                if (trait.ProperKind != kind) box.IsChecked = false;
            }
        }

        private string SelectedProperKind()
        {
            foreach (var pair in _properRadios) if (pair.Value.IsChecked == true) return pair.Key;
            return "";
        }

        // ------------------------------------------------------------ type et flexion

        private string SelectedClass()
        {
            foreach (var pair in _classRadios) if (pair.Value.IsChecked == true) return pair.Key;
            return LexiconEntry.ClassOther;
        }

        private void SelectClass(string cls)
        {
            RadioButton radio;
            if (_classRadios.TryGetValue(cls, out radio)) radio.IsChecked = true;
        }

        private string SelectedGenders()
        {
            foreach (var pair in _genderRadios) if (pair.Value.IsChecked == true) return pair.Key;
            return "";
        }

        private void OnClassChanged(string cls)
        {
            _syncing = true;
            try
            {
                // Un nom propre est invariable d'office (29/09), un adjectif se
                // décline aux deux genres : des départs, modifiables ensuite.
                if (cls == LexiconEntry.ClassProper) _invariable.IsChecked = true;
                if (cls == LexiconEntry.ClassAdjective && SelectedGenders().Length == 0) _genderRadios[LexiconEntry.GendersBoth].IsChecked = true;
                if (cls == LexiconEntry.ClassNoun && SelectedGenders().Length == 0) _genderRadios[LexiconEntry.GendersMasculine].IsChecked = true;
            }
            finally { _syncing = false; }
            UpdateVisibility();
            RefreshForms();
        }

        private void UpdateVisibility()
        {
            var cls = SelectedClass();
            var hasNature = _naturePanels.ContainsKey(cls);
            _natureHost.IsVisible = hasNature ? true : false;
            foreach (var pair in _naturePanels) pair.Value.IsVisible = pair.Key == cls ? true : false;
            var flexion = cls == LexiconEntry.ClassNoun || cls == LexiconEntry.ClassAdjective || cls == LexiconEntry.ClassProper;
            _flexion.IsVisible = flexion ? true : false;
        }

        /// <summary>Les quatre champs : ceux que la flexion choisie n'a pas
        /// sont éteints ; les autres montrent la forme dérivée en filigrane.</summary>
        private void RefreshForms()
        {
            if (_forms[3] == null || _preview == null) return;
            var probe = Build();
            probe.MascSg = probe.MascPl = probe.FemSg = probe.FemPl = "";
            var derived = probe.DerivedForms();
            var genders = probe.EffectiveGenders();
            for (var i = 0; i < 4; i++)
            {
                var masculine = i < 2;
                var enabled = probe.HasFlexion && (masculine ? genders != LexiconEntry.GendersFeminine : genders != LexiconEntry.GendersMasculine && genders.Length > 0);
                _forms[i].IsEnabled = enabled;
                _forms[i].Watermark = !enabled ? "" : derived[i] ?? (i % 2 == 1 ? "(pas de pluriel)" : "");
            }
            RefreshPreview();
        }

        private LexiconEntry Build()
        {
            var cls = SelectedClass();
            var entry = new LexiconEntry
            {
                Word = (_word.Text ?? "").Trim(),
                Class = cls,
                Genders = cls == LexiconEntry.ClassNoun || cls == LexiconEntry.ClassAdjective || cls == LexiconEntry.ClassProper ? SelectedGenders() : "",
                Plural = _invariable.IsChecked == true ? LexiconEntry.PluralInvariable : (_initial != null && _initial.Plural == LexiconEntry.PluralX ? LexiconEntry.PluralX : ""),
                MascSg = (_forms[0].Text ?? "").Trim(),
                MascPl = (_forms[1].Text ?? "").Trim(),
                FemSg = (_forms[2].Text ?? "").Trim(),
                FemPl = (_forms[3].Text ?? "").Trim(),
                Definition = (_definition.Text ?? "").Trim(),
                Note = (_note.Text ?? "").Trim(),
                ProperKind = cls == LexiconEntry.ClassProper ? SelectedProperKind() : ""
            };
            foreach (var trait in LexiconEntry.TraitCatalog)
            {
                CheckBox box;
                if (trait.Class != cls || !_traitBoxes.TryGetValue(trait.Key, out box)) continue;
                if (box.IsChecked == true && box.IsEnabled) entry.Traits.Add(trait.Key);
            }
            // La pastille se lève à la validation d'une entrée complète ; une
            // entrée restée « autre » ou sans flexion la garde.
            entry.NeedsReview = _initial != null && _initial.NeedsReview && !entry.IsComplete();
            return entry;
        }

        private void RefreshPreview()
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
        /// pré-rempli, type deviné, portée fixée par le sous-menu.</summary>
        public static Task<LexiconEntry> AskForWord(Window owner, string word, bool projectScope)
        {
            return AskForWord(owner, word, projectScope, null);
        }

        /// <summary>Même dialogue, avec des chips de suggestions en tête
        /// (les mots d'une fiche, 29/09) — un clic préremplit le mot.</summary>
        public static async Task<LexiconEntry> AskForWord(Window owner, string word, bool projectScope, IList<string> suggestions)
        {
            var guessed = LexiconInflector.GuessClass(word);
            var initial = new LexiconEntry
            {
                Word = word ?? "",
                Class = guessed,
                Genders = guessed == LexiconEntry.ClassAdjective ? LexiconEntry.GendersBoth : guessed == LexiconEntry.ClassNoun || guessed == LexiconEntry.ClassProper ? LexiconEntry.GendersMasculine : "",
                Plural = guessed == LexiconEntry.ClassProper ? LexiconEntry.PluralInvariable : ""
            };
            var dialog = new LexiconEntryDialog(owner, initial, projectScope, false, suggestions);
            dialog.Title = "Ajouter au dictionnaire";
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted ? dialog.Build() : null;
        }
    }
}
