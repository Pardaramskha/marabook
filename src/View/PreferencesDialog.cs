using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        public PreferencesDialog(Window owner)
        {
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
