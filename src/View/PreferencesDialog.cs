using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;
using UniversSale.Settings;

namespace UniversSale.View
{
    /// <summary>Application preferences (Fichier → Préférences…), applied live.
    /// First tab « Personnalisation » : accent color of the whole interface and
    /// the « white paper under the dark theme » option. Everything is stored in
    /// AppSettings (per user, all projects).</summary>
    public class PreferencesDialog : Window
    {
        /// <summary>Raised whenever appearance changed — the main window
        /// re-applies Chrome + Theme and refreshes what needs it.</summary>
        public event Action AppearanceChanged;

        /// <summary>Le mode de compatibilité a changé : la fenêtre principale
        /// recharge le document ouvert sur la bonne surface.</summary>
        public event Action EditingSurfaceChanged;

        /// <summary>Un dictionnaire personnel a changé : l'éditeur doit
        /// oublier ses verdicts en cache et revérifier.</summary>
        public event Action ProofingChanged;

        /// <summary>Un raccourci a changé (onglet Raccourcis, b43) : la
        /// fenêtre principale reconstruit menus et KeyBindings.</summary>
        public event Action ShortcutsChanged;

        private readonly Model.Project _project; // null : aucun projet ouvert

        private readonly WrapPanel _swatches;
        private CheckBox _whitePaper;

        // Accents proposés : l'indigo maison puis des teintes sages, toutes
        // lisibles en clair comme en sombre (le pas sombre est dérivé).
        private static readonly string[][] Accents =
        {
            new[] { Chrome.DefaultAccent, "Indigo (défaut)" },
            new[] { "#2980B9", "Bleu" },
            new[] { "#16A085", "Sarcelle" },
            new[] { "#27AE60", "Vert" },
            new[] { "#C9A227", "Or" },
            new[] { "#E67E22", "Orange" },
            new[] { "#C0392B", "Rouge" },
            new[] { "#C2185B", "Framboise" },
            new[] { "#8E44AD", "Violet" },
            new[] { "#5D6D7E", "Ardoise" },
        };

        public PreferencesDialog(Window owner, Model.Project project)
        {
            _project = project;
            Title = "Préférences";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Taille UNIQUE pour tous les onglets (b43) — fini la fenêtre qui
            // change de taille à chaque onglet.
            Width = 720;
            Height = 640;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var tabs = new TabControl { Margin = new Thickness(10) };
            _swatches = new WrapPanel { MaxWidth = 330 };
            tabs.Items.Add(new TabItem
            {
                Header = "Personnalisation",
                Content = Scrolled(BuildPersonalizationTab())
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Édition",
                Content = Scrolled(BuildEditingTab())
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Correction",
                Content = Scrolled(BuildProofingTab())
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Raccourcis",
                Content = Scrolled(BuildShortcutsTab())
            });

            var layout = new DockPanel();
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 14)
            };
            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 90 };
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(tabs);

            Content = layout;
        }

        private static ScrollViewer Scrolled(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
        }

        /// <summary>Onglet « Édition » : le mode de compatibilité — le repli
        /// classique, gelé au batch 26 (gel documenté dans PLAN.md avec ses
        /// trois conditions de suppression).</summary>
        private UIElement BuildEditingTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10), MaxWidth = 420 };
            panel.Children.Add(Caption("Mode de compatibilité"));
            var compat = new CheckBox
            {
                Content = "Écrire dans l'ancienne surface (mode de compatibilité)",
                IsChecked = AppSettings.ClassicCompatibility,
                Margin = new Thickness(0, 6, 0, 0)
            };
            compat.Click += delegate
            {
                AppSettings.ClassicCompatibility = compat.IsChecked == true;
                AppSettings.Save();
                var handler = EditingSurfaceChanged;
                if (handler != null) handler();
            };
            panel.Children.Add(compat);
            panel.Children.Add(new TextBlock
            {
                Text = "Ce qu'il apporte : la saisie IME pour les écritures non "
                    + "latines, et le correcteur orthographique de Windows.\n\n"
                    + "Ce qu'il coûte : ni correction Marabook (répétitions, et "
                    + "bientôt orthographe et grammaire), ni approche, ni bulles "
                    + "d'annotation, ni gabarits à l'écran, ni affichage "
                    + "Brouillon.\n\n"
                    + "Les pages composées sont la surface d'édition de "
                    + "Marabook ; ce repli est conservé tel quel, sans "
                    + "nouvelle fonctionnalité, en attendant que le composé "
                    + "couvre aussi ces deux besoins.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            // Les dictionnaires personnels ont quitté ce volet (batch 34) :
            // l'écran « Dictionnaire » de la Pile tient ce rôle, avec les
            // natures grammaticales et les formes acceptées.

            // — Les versions d'écrits (batch 38).
            var versions = Caption("Versions d'écrits");
            versions.Margin = new Thickness(0, 18, 0, 0);
            panel.Children.Add(versions);
            var daily = new CheckBox
            {
                Content = "Instantané automatique à la première modification du jour",
                IsChecked = AppSettings.DailySnapshot,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "L'état de l'écrit tel qu'ouvert, figé une fois par jour et par écrit — il compte dans le plafond"
            };
            daily.Click += delegate
            {
                AppSettings.DailySnapshot = daily.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(daily);
            var capRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var capBox = new TextBox { Width = 56, Text = AppSettings.SnapshotCap.ToString(), ToolTip = "Entre " + Model.SnapshotStore.MinCap + " et " + Model.SnapshotStore.MaxCap };
            DockPanel.SetDock(capBox, Dock.Right);
            capBox.LostKeyboardFocus += delegate
            {
                int value;
                if (!int.TryParse(capBox.Text.Trim(), out value)) value = AppSettings.SnapshotCap;
                value = Math.Max(Model.SnapshotStore.MinCap, Math.Min(Model.SnapshotStore.MaxCap, value));
                capBox.Text = value.ToString();
                if (value == AppSettings.SnapshotCap) return;
                AppSettings.SnapshotCap = value;
                AppSettings.Save();
            };
            capRow.Children.Add(capBox);
            capRow.Children.Add(new TextBlock
            {
                Text = "Instantanés gardés par écrit (les automatiques sont évincés d'abord)",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 0)
            });
            panel.Children.Add(capRow);
            return panel;
        }

        /// <summary>Onglet « Correction » (batch 29, lot C) : la grammaire
        /// Grammalecte — interrupteur maître et les options SOUS LEUR
        /// NOMENCLATURE À LUI (jamais la nôtre), groupées par catégorie.
        /// La politique de recouvrement est visible et honnête : une option
        /// éteinte parce que Marabook couvre déjà le terrain (répétitions,
        /// typographie du compositeur, mots composés) le dit — et reste
        /// rallumable, en connaissance de cause.</summary>
        private UIElement BuildProofingTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10), MaxWidth = 440 };
            panel.Children.Add(Caption("Grammaire (Grammalecte)"));
            var master = new CheckBox
            {
                Content = "Vérification grammaticale (Grammalecte, en différé)",
                IsChecked = AppSettings.GrammarEnabled,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Grammalecte tourne dans un sous-processus : ses "
                    + "signalements arrivent quelques instants après la frappe,\n"
                    + "sans jamais ralentir l'orthographe ni la saisie."
            };
            master.Click += delegate
            {
                AppSettings.GrammarEnabled = master.IsChecked == true;
                AppSettings.Save();
                var handler = ProofingChanged;
                if (handler != null) handler();
            };
            panel.Children.Add(master);
            panel.Children.Add(new TextBlock
            {
                Text = "Les options reprennent les noms de Grammalecte. Celles "
                    + "marquées « couvert par Marabook » sont éteintes parce "
                    + "qu'un vérificateur maison tient déjà ce terrain "
                    + "(répétitions réglées pour le roman, typographie du "
                    + "compositeur, mots composés de l'orthographe) — les "
                    + "rallumer produit des signalements en double.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 4)
            });

            var options = new StackPanel();
            string group = null;
            foreach (var option in Correction.Grammalecte.GrammalecteOptions.Catalog)
            {
                if (option.Group != group)
                {
                    group = option.Group;
                    options.Children.Add(Caption(group, 10));
                }
                var optionRef = option;
                bool overridden;
                var value = AppSettings.GrammarOptions.TryGetValue(
                    option.Name, out overridden)
                    ? overridden : option.MarabookDefault;
                var check = new CheckBox
                {
                    Content = option.Label + " (" + option.Name + ")"
                        + (option.OverriddenByPolicy
                            ? " — couvert par Marabook" : ""),
                    IsChecked = value,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                check.Click += delegate
                {
                    var chosen = check.IsChecked == true;
                    // Revenu au défaut Marabook : l'entrée disparaît (les
                    // défauts futurs de la politique s'appliqueront).
                    if (chosen == optionRef.MarabookDefault)
                        AppSettings.GrammarOptions.Remove(optionRef.Name);
                    else
                        AppSettings.GrammarOptions[optionRef.Name] = chosen;
                    AppSettings.Save();
                    var handler = ProofingChanged;
                    if (handler != null) handler();
                };
                options.Children.Add(check);
            }
            panel.Children.Add(new ScrollViewer
            {
                Content = options,
                Height = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return panel;
        }

        private UIElement BuildPersonalizationTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };

            panel.Children.Add(Caption("Couleur d'accent"));
            panel.Children.Add(new TextBlock
            {
                Text = "Boutons, sélections, liens et repères prennent cette teinte.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });

            RebuildSwatches();
            panel.Children.Add(_swatches);

            var custom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var pick = new Button { Content = "Personnalisée…", MinWidth = 110 };
            pick.Click += delegate
            {
                var hex = ColorDialog.Ask(this);
                if (hex == null) return;
                SetAccent(string.Equals(hex, Chrome.DefaultAccent,
                    StringComparison.OrdinalIgnoreCase) ? null : hex);
            };
            custom.Children.Add(pick);
            var reset = new Button
            {
                Content = "Réinitialiser",
                MinWidth = 100,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Revenir à l'indigo par défaut"
            };
            reset.Click += delegate { SetAccent(null); };
            custom.Children.Add(reset);
            panel.Children.Add(custom);

            panel.Children.Add(Caption("Mode sombre", 18));
            _whitePaper = new CheckBox
            {
                Content = "Garder le papier blanc malgré le mode sombre",
                IsChecked = AppSettings.WhitePaperInDark,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "L'interface reste sombre mais les pages (mode classique, fiches, gabarits)\n"
                    + "gardent leur papier blanc et leur encre noire, comme à l'impression."
            };
            _whitePaper.Click += delegate
            {
                AppSettings.WhitePaperInDark = _whitePaper.IsChecked == true;
                RaiseAppearanceChanged();
            };
            panel.Children.Add(_whitePaper);
            panel.Children.Add(new TextBlock
            {
                Text = "La vue Composition, l'aperçu et le PDF sont toujours noir sur blanc.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(22, 4, 0, 0)
            });

            return panel;
        }

        // ------------------------------------------------------ raccourcis (b43)

        /// <summary>L'onglet « Raccourcis » : toutes les actions de
        /// l'application, groupées par catégorie — cliquer le champ puis
        /// taper la combinaison ; Retour arrière retire le raccourci,
        /// Échap referme sans changer. Personnalisations dans
        /// AppSettings.Shortcuts (settings.json), appliquées aussitôt.</summary>
        private UIElement BuildShortcutsTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12, 10, 16, 12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Cliquez un champ puis tapez la combinaison voulue. "
                     + "Retour arrière retire le raccourci ; « Défaut » restaure celui d'origine.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var categories = new List<string>();
            foreach (var action in AppSettings.Actions)
                if (!categories.Contains(action.Category)) categories.Add(action.Category);
            foreach (var category in categories)
            {
                panel.Children.Add(Caption(category, 10));
                foreach (var action in AppSettings.Actions)
                    if (action.Category == category)
                        panel.Children.Add(ShortcutRow(action));
            }
            return panel;
        }

        private UIElement ShortcutRow(ActionDefinition action)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };

            var box = new TextBox
            {
                Width = 150,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Cliquer puis taper la combinaison — Retour arrière : aucun raccourci"
            };
            var reset = new Button
            {
                Content = "Défaut",
                FontSize = 11,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Revenir au raccourci d'origine"
                    + (string.IsNullOrEmpty(action.DefaultGesture) ? " (aucun)" : " : " + AppSettings.DisplayGesture(action.DefaultGesture))
            };
            Action sync = delegate
            {
                var gesture = AppSettings.Gesture(action.Id);
                box.Text = string.IsNullOrEmpty(gesture) ? "—" : AppSettings.DisplayGesture(gesture);
                var custom = AppSettings.Shortcuts.ContainsKey(action.Id);
                box.FontWeight = custom ? FontWeights.SemiBold : FontWeights.Normal;
                reset.Visibility = custom ? Visibility.Visible : Visibility.Hidden;
            };
            Action<string> store = delegate(string gesture)
            {
                if (gesture == (action.DefaultGesture ?? "")) AppSettings.Shortcuts.Remove(action.Id);
                else AppSettings.Shortcuts[action.Id] = gesture;
                AppSettings.Save();
                sync();
                var handler = ShortcutsChanged;
                if (handler != null) handler();
            };
            box.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift
                    || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin) return;
                if (key == Key.Escape) { Keyboard.ClearFocus(); sync(); return; }
                if (key == Key.Back) { store(""); return; }
                var gesture = "";
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) gesture += "Ctrl+";
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) gesture += "Shift+";
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) gesture += "Alt+";
                store(gesture + key);
            };
            box.GotKeyboardFocus += delegate { box.Text = "Tapez…"; };
            box.LostKeyboardFocus += delegate { sync(); };
            reset.Click += delegate
            {
                AppSettings.Shortcuts.Remove(action.Id);
                AppSettings.Save();
                sync();
                var handler = ShortcutsChanged;
                if (handler != null) handler();
            };
            sync();

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(box);
            right.Children.Add(reset);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(new TextBlock
            {
                Text = action.Name,
                Foreground = Chrome.Ink,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private TextBlock Caption(string text, double topMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
        }

        /// <summary>The swatch grid, the current accent ringed with the ink color
        /// (never the accent itself — it must read on every hue).</summary>
        private void RebuildSwatches()
        {
            _swatches.Children.Clear();
            var current = AppSettings.AccentColor ?? Chrome.DefaultAccent;
            foreach (var entry in Accents)
            {
                var hex = entry[0];
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                var swatch = new Button
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    BorderBrush = selected ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(selected ? 2.5 : 1),
                    ToolTip = entry[1] + " — " + hex
                };
                // Le gabarit de bouton du thème peint SON fond : un gabarit
                // minimal rend la couleur (règle maison : ne pas forcer le fond
                // d'un contrôle thémé, donc on remplace le gabarit entier).
                swatch.Template = SwatchTemplate();
                var hexRef = hex;
                swatch.Click += delegate
                {
                    SetAccent(string.Equals(hexRef, Chrome.DefaultAccent,
                        StringComparison.OrdinalIgnoreCase) ? null : hexRef);
                };
                _swatches.Children.Add(swatch);
            }
        }

        private static ControlTemplate SwatchTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        private void SetAccent(string hex)
        {
            AppSettings.AccentColor = hex;
            RebuildSwatches();
            RaiseAppearanceChanged();
        }

        private void RaiseAppearanceChanged()
        {
            AppSettings.Save();
            var handler = AppearanceChanged;
            if (handler != null) handler();
        }
    }
}
