using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var tabs = new TabControl { Margin = new Thickness(10) };
            _swatches = new WrapPanel { MaxWidth = 330 };
            tabs.Items.Add(new TabItem
            {
                Header = "Personnalisation",
                Content = BuildPersonalizationTab()
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Édition",
                Content = BuildEditingTab()
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Correction",
                Content = BuildProofingTab()
            });

            var layout = new StackPanel { MinWidth = 380 };
            layout.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 14)
            };
            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 90 };
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            layout.Children.Add(buttons);

            Content = layout;
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
