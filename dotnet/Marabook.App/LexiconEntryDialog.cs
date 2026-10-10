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
        // Le genre d'un prénom (1.0.3) : masculin, féminin, neutre (défaut).
        private readonly List<RadioButton> _firstNameRadios = new List<RadioButton>();
        private string _firstNameGender = "";
        private readonly Dictionary<string, CheckBox> _traitBoxes = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, StackPanel> _naturePanels = new Dictionary<string, StackPanel>();
        private readonly StackPanel _natureHost, _flexion;
        private readonly CheckBox _invariable;
        private readonly LexiconEntry _initial;
        private StackPanel _demonymPanel;
        private CheckBox _demonymCheck;
        private TextBox _demonymSuffix, _demonymForm;
        // Les pratiquants d'une religion ou doctrine (1.0.5) : même mécanique.
        private StackPanel _adherentPanel;
        private CheckBox _adherentCheck;
        private TextBox _adherentSuffix, _adherentForm;
        private bool _accepted, _syncing;
        // Le choix de chaque groupe de boutons radio, tenu à jour par le
        // bouton qui vient d'être coché (02/10). Avalonia lève
        // IsCheckedChanged sur le nouveau bouton AVANT de décocher l'ancien :
        // relire le groupe à cet instant rendait encore l'ancien type, et la
        // nature, la flexion, l'aperçu restaient ceux d'avant — « Nom » vers
        // « Nom propre » gardait les cases d'un nom.
        private string _class, _genders, _properKind = "";

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
            _class = initialClass;
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
                radio.IsCheckedChanged += delegate
                {
                    if (radio.IsChecked != true) return;
                    _class = keyRef;
                    OnClassChanged(keyRef);
                };
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
            // Une entrée neuve part au masculin (01/10) : un nom, le cas le
            // plus fréquent, ne demande plus un clic de plus.
            var initialGenders = initial == null ? LexiconEntry.GendersMasculine : initial.EffectiveGenders();
            _genders = initialGenders;
            foreach (var pair in new[] { new[] { LexiconEntry.GendersBoth, "Masculin et féminin" }, new[] { LexiconEntry.GendersMasculine, "Masculin" }, new[] { LexiconEntry.GendersFeminine, "Féminin" } })
            {
                var radio = new RadioButton
                {
                    Content = pair[1],
                    GroupName = "lexicon-genders",
                    IsChecked = pair[0] == initialGenders,
                    Margin = new Thickness(0, 0, 16, 2)
                };
                var gendersRef = pair[0];
                radio.IsCheckedChanged += delegate
                {
                    if (radio.IsChecked == true) _genders = gendersRef;
                    if (!_syncing) RefreshForms();
                };
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
                box.IsCheckedChanged += delegate
                {
                    UpdateChildren(traitRef.Key);
                    if (_syncing || _forms[3] == null) return;
                    // La nature change les formes (01/10) : une personne, une
                    // fonction, un animal se déclinent aux deux genres (le
                    // masculin seul posé par défaut cède) ; une entité non
                    // comptable n'a pas de pluriel ; un nom d'habitant prend
                    // aussi sa majuscule — RefreshForms relit tout.
                    if (box.IsChecked == true
                        && (traitRef.Key == "person" || traitRef.Key == "role" || traitRef.Key == "animal")
                        && SelectedGenders() == LexiconEntry.GendersMasculine)
                        _genderRadios[LexiconEntry.GendersBoth].IsChecked = true;
                    RefreshForms();
                };
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
            _properKind = initialKind ?? "";
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
                var kindRef = kind;
                radio.IsCheckedChanged += delegate
                {
                    if (radio.IsChecked == true) _properKind = kindRef;
                    UpdateProperChildren();
                };
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
                    box.IsCheckedChanged += delegate { if (!_syncing) RefreshForms(); };
                    children.Children.Add(box);
                }
                if (kind == LexiconEntry.ProperFirstName)
                {
                    // Le genre d'un prénom (1.0.3) : neutre par défaut — le
                    // correcteur n'accorde que sur un prénom genré.
                    var initialFirstName = initial != null && initial.ProperKind == LexiconEntry.ProperFirstName ? initial.Genders : "";
                    _firstNameGender = initialFirstName == LexiconEntry.GendersMasculine || initialFirstName == LexiconEntry.GendersFeminine ? initialFirstName : "";
                    foreach (var pair in new[] { new[] { LexiconEntry.GendersMasculine, "Masculin" }, new[] { LexiconEntry.GendersFeminine, "Féminin" }, new[] { "", "Neutre" } })
                    {
                        var genderRadio = new RadioButton
                        {
                            Content = pair[1],
                            GroupName = "lexicon-firstname",
                            IsChecked = pair[0] == _firstNameGender,
                            Margin = new Thickness(0, 2, 14, 0)
                        };
                        var genderRef = pair[0];
                        genderRadio.IsCheckedChanged += delegate
                        {
                            if (genderRadio.IsChecked == true) _firstNameGender = genderRef;
                            if (!_syncing) RefreshForms();
                        };
                        _firstNameRadios.Add(genderRadio);
                        children.Children.Add(genderRadio);
                    }
                }
                if (children.Children.Count > 0) host.Children.Add(children);
            }
            host.Children.Add(BuildDemonymPanel(initial));
            host.Children.Add(BuildAdherentPanel(initial));
            UpdateProperChildren();
            return host;
        }

        /// <summary>Le gentilé dérivé d'un lieu (01/10) : une case, le suffixe
        /// (tapé, ou l'une des préconfigurations en chips), la forme masculine
        /// posée si la règle se trompe. Les formes entrent dans l'aperçu.</summary>
        private StackPanel BuildDemonymPanel(LexiconEntry initial)
        {
            _demonymPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var initialSuffix = initial == null ? "" : initial.DemonymSuffix.Trim();
            _demonymCheck = new CheckBox
            {
                Content = "Dériver le gentilé (les habitants et l'adjectif : Mànis → Mànisien, mànisienne…)",
                IsChecked = initialSuffix.Length > 0
            };
            _demonymCheck.IsCheckedChanged += delegate
            {
                if (_syncing) return;
                if (_demonymCheck.IsChecked == true && (_demonymSuffix.Text ?? "").Trim().Length == 0)
                    _demonymSuffix.Text = LexiconEntry.DemonymSuffixes[0];
                UpdateDemonymState();
                RefreshForms();
            };
            _demonymPanel.Children.Add(_demonymCheck);

            var row = new WrapPanel { Margin = new Thickness(26, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = "Suffixe :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _demonymSuffix = new TextBox
            {
                Width = 64,
                Text = initialSuffix,
                Watermark = "ien",
                Margin = new Thickness(0, 0, 8, 4),
                [ToolTip.TipProperty] = "Le suffixe collé au nom du lieu — l'une des préconfigurations, ou le vôtre"
            };
            _demonymSuffix.TextChanged += delegate { if (!_syncing) RefreshForms(); };
            row.Children.Add(_demonymSuffix);
            foreach (var preset in LexiconEntry.DemonymSuffixes)
            {
                var suffix = preset;
                var chip = Buttons.Text("-" + preset, "Suffixe « -" + preset + " »", Buttons.Compact, Buttons.Look.Outline);
                chip.Margin = new Thickness(0, 0, 4, 4);
                chip.Click += delegate { _demonymSuffix.Text = suffix; };
                row.Children.Add(chip);
            }
            _demonymPanel.Children.Add(row);

            var formRow = new DockPanel { Margin = new Thickness(26, 2, 0, 0) };
            formRow.Children.Add(new TextBlock { Text = "Forme masculine :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), [DockPanel.DockProperty] = Dock.Left });
            _demonymForm = new TextBox
            {
                Text = initial == null ? "" : initial.DemonymForm,
                [ToolTip.TipProperty] = "Vide : la forme dérivée par la règle (en filigrane) ; tapez-la si la règle se trompe (Bordeaux → bordelais)"
            };
            _demonymForm.TextChanged += delegate { if (!_syncing) RefreshForms(); };
            formRow.Children.Add(_demonymForm);
            _demonymPanel.Children.Add(formRow);
            UpdateDemonymState();
            return _demonymPanel;
        }

        /// <summary>Les pratiquants d'une religion, croyance ou doctrine
        /// (1.0.5, Rémi) : d'un nom commun (« rhétannisme ») ou d'un nom
        /// propre « autre », le nom et l'adjectif de ceux qui la suivent
        /// (rhétanniste, rhétannistes…) par un suffixe choisi ; la forme
        /// masculine se pose si la règle se trompe.</summary>
        private StackPanel BuildAdherentPanel(LexiconEntry initial)
        {
            _adherentPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var initialSuffix = initial == null ? "" : initial.AdherentSuffix.Trim();
            _adherentCheck = new CheckBox
            {
                Content = "Dériver les pratiquants (religion, croyance, doctrine : rhétannisme → rhétanniste, rhétannistes…)",
                IsChecked = initialSuffix.Length > 0
            };
            _adherentCheck.IsCheckedChanged += delegate
            {
                if (_syncing) return;
                if (_adherentCheck.IsChecked == true && (_adherentSuffix.Text ?? "").Trim().Length == 0)
                    _adherentSuffix.Text = LexiconEntry.AdherentSuffixes[0];
                UpdateAdherentState();
                RefreshForms();
            };
            _adherentPanel.Children.Add(_adherentCheck);

            var row = new WrapPanel { Margin = new Thickness(26, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = "Suffixe :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _adherentSuffix = new TextBox
            {
                Width = 64,
                Text = initialSuffix,
                Watermark = "iste",
                Margin = new Thickness(0, 0, 8, 4),
                [ToolTip.TipProperty] = "Le suffixe qui remplace -isme (ou s'ajoute au mot) — l'une des préconfigurations, ou le vôtre"
            };
            _adherentSuffix.TextChanged += delegate { if (!_syncing) RefreshForms(); };
            row.Children.Add(_adherentSuffix);
            foreach (var preset in LexiconEntry.AdherentSuffixes)
            {
                var suffix = preset;
                var chip = Buttons.Text("-" + preset, "Suffixe « -" + preset + " »", Buttons.Compact, Buttons.Look.Outline);
                chip.Margin = new Thickness(0, 0, 4, 4);
                chip.Click += delegate { _adherentSuffix.Text = suffix; };
                row.Children.Add(chip);
            }
            _adherentPanel.Children.Add(row);

            var formRow = new DockPanel { Margin = new Thickness(26, 2, 0, 0) };
            formRow.Children.Add(new TextBlock { Text = "Forme masculine :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), [DockPanel.DockProperty] = Dock.Left });
            _adherentForm = new TextBox
            {
                Text = initial == null ? "" : initial.AdherentForm,
                [ToolTip.TipProperty] = "Vide : la forme dérivée par la règle (en filigrane) ; tapez-la si la règle se trompe (christianisme → chrétien)"
            };
            _adherentForm.TextChanged += delegate { if (!_syncing) RefreshForms(); };
            formRow.Children.Add(_adherentForm);
            _adherentPanel.Children.Add(formRow);
            UpdateAdherentState();
            return _adherentPanel;
        }

        private void UpdateAdherentState()
        {
            if (_adherentPanel == null) return;
            var on = _adherentCheck.IsChecked == true;
            _adherentSuffix.IsEnabled = on;
            _adherentForm.IsEnabled = on;
            foreach (var child in ((WrapPanel)_adherentPanel.Children[1]).Children)
            {
                var chip = child as Button;
                if (chip != null) chip.IsEnabled = on;
            }
        }

        private void UpdateDemonymState()
        {
            if (_demonymPanel == null) return;
            var on = _demonymCheck.IsChecked == true;
            _demonymSuffix.IsEnabled = on;
            _demonymForm.IsEnabled = on;
            foreach (var child in ((WrapPanel)_demonymPanel.Children[1]).Children)
            {
                var chip = child as Button;
                if (chip != null) chip.IsEnabled = on;
            }
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
            if (_demonymPanel != null) _demonymPanel.IsVisible = LexiconEntry.AllowsDemonym(kind) ? true : false;
            if (_adherentPanel != null) _adherentPanel.IsVisible = SelectedClass() == LexiconEntry.ClassNoun || kind == LexiconEntry.ProperOther;
            foreach (var genderRadio in _firstNameRadios) genderRadio.IsEnabled = kind == LexiconEntry.ProperFirstName;
            // Un gentilé se décline aux deux genres (Mànisien, Mànisienne) ;
            // un autre nom propre ne se fléchit pas (01/10).
            if (kind == LexiconEntry.ProperDemonym && !_syncing && _forms[3] != null)
            {
                _syncing = true;
                try
                {
                    if (SelectedGenders().Length == 0 || SelectedGenders() == LexiconEntry.GendersMasculine)
                        _genderRadios[LexiconEntry.GendersBoth].IsChecked = true;
                    _invariable.IsChecked = false;
                }
                finally { _syncing = false; }
            }
            if (_forms[3] != null)
            {
                UpdateVisibility();
                RefreshForms();
            }
        }

        private string SelectedProperKind()
        {
            return _properKind;
        }

        // ------------------------------------------------------------ type et flexion

        private string SelectedClass()
        {
            return _class ?? LexiconEntry.ClassOther;
        }

        private void SelectClass(string cls)
        {
            RadioButton radio;
            if (_classRadios.TryGetValue(cls, out radio)) radio.IsChecked = true;
        }

        private string SelectedGenders()
        {
            return _genders ?? "";
        }

        private void OnClassChanged(string cls)
        {
            _syncing = true;
            try
            {
                // Un adjectif se décline aux deux genres, un nom part au
                // masculin : des départs, modifiables ensuite. Un nom propre
                // ne se fléchit plus (01/10), sauf le gentilé (UpdateProperChildren).
                if (cls == LexiconEntry.ClassAdjective && SelectedGenders().Length == 0) _genderRadios[LexiconEntry.GendersBoth].IsChecked = true;
                if (cls == LexiconEntry.ClassNoun && SelectedGenders().Length == 0) _genderRadios[LexiconEntry.GendersMasculine].IsChecked = true;
            }
            finally { _syncing = false; }
            if (cls == LexiconEntry.ClassProper) UpdateProperChildren();
            UpdateVisibility();
            RefreshForms();
        }

        /// <summary>La flexion s'affiche pour un nom, un adjectif, un gentilé —
        /// pas pour un nom de famille, un prénom, un lieu… (01/10).</summary>
        private bool FlexionApplies()
        {
            var cls = SelectedClass();
            return cls == LexiconEntry.ClassNoun || cls == LexiconEntry.ClassAdjective
                || (cls == LexiconEntry.ClassProper && SelectedProperKind() == LexiconEntry.ProperDemonym);
        }

        private void UpdateVisibility()
        {
            var cls = SelectedClass();
            var hasNature = _naturePanels.ContainsKey(cls);
            _natureHost.IsVisible = hasNature ? true : false;
            foreach (var pair in _naturePanels) pair.Value.IsVisible = pair.Key == cls ? true : false;
            _flexion.IsVisible = FlexionApplies() ? true : false;
            // Les pratiquants (1.0.5) : un nom commun, ou un nom propre « autre ».
            if (_adherentPanel != null)
                _adherentPanel.IsVisible = cls == LexiconEntry.ClassNoun
                    || (cls == LexiconEntry.ClassProper && SelectedProperKind() == LexiconEntry.ProperOther);
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
            // Une entité non comptable n'a pas de pluriel : la case le dit.
            _invariable.IsEnabled = !(probe.Class == LexiconEntry.ClassNoun && probe.HasTrait("uncountable"));
            if (_demonymForm != null)
            {
                probe.DemonymForm = "";
                _demonymForm.Watermark = probe.DemonymBase() ?? "";
            }
            if (_adherentForm != null)
            {
                probe.AdherentForm = "";
                _adherentForm.Watermark = probe.AdherentBase() ?? "";
            }
            RefreshPreview();
        }

        private LexiconEntry Build()
        {
            var cls = SelectedClass();
            var flexion = FlexionApplies();
            var entry = new LexiconEntry
            {
                Word = (_word.Text ?? "").Trim(),
                Class = cls,
                Genders = flexion ? SelectedGenders()
                    : cls == LexiconEntry.ClassProper && SelectedProperKind() == LexiconEntry.ProperFirstName ? _firstNameGender : "",
                Plural = !flexion ? "" : _invariable.IsChecked == true ? LexiconEntry.PluralInvariable : (_initial != null && _initial.Plural == LexiconEntry.PluralX ? LexiconEntry.PluralX : ""),
                MascSg = flexion ? (_forms[0].Text ?? "").Trim() : "",
                MascPl = flexion ? (_forms[1].Text ?? "").Trim() : "",
                FemSg = flexion ? (_forms[2].Text ?? "").Trim() : "",
                FemPl = flexion ? (_forms[3].Text ?? "").Trim() : "",
                Definition = (_definition.Text ?? "").Trim(),
                Note = (_note.Text ?? "").Trim(),
                ProperKind = cls == LexiconEntry.ClassProper ? SelectedProperKind() : ""
            };
            if (cls == LexiconEntry.ClassProper && LexiconEntry.AllowsDemonym(entry.ProperKind)
                && _demonymCheck != null && _demonymCheck.IsChecked == true)
            {
                entry.DemonymSuffix = (_demonymSuffix.Text ?? "").Trim().TrimStart('-');
                entry.DemonymForm = (_demonymForm.Text ?? "").Trim();
            }
            if (entry.AllowsAdherents && _adherentCheck != null && _adherentCheck.IsChecked == true)
            {
                entry.AdherentSuffix = (_adherentSuffix.Text ?? "").Trim().TrimStart('-');
                entry.AdherentForm = (_adherentForm.Text ?? "").Trim();
            }
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
