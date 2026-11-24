using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Préférences › Personnalisation : LES OPTIONS D'UN SKIN « MUSEUM » (2.0.0,
    /// 24/11/2026, Rémi) — trois, pas une de plus : la couleur d'accent (celle de
    /// l'application, réglée juste dessous ; sans choix, celle du skin), la couleur de
    /// fond et l'effet verre. Le bloc ne se montre que sous un skin de la série.</summary>
    public partial class PreferencesDialog
    {
        private StackPanel _museumRow;
        private DispatcherTimerLite _glassTimer;

        // Des fonds qui vont à la série : trois de nuit, trois de jour, puis des voisins.
        private static readonly string[][] MuseumDarkGrounds =
        {
            new[] { "#0A1A21", "Bleu canard" },
            new[] { "#1A080C", "Bordeaux" },
            new[] { "#19150B", "Noyer" },
            new[] { "#0B1A12", "Serre" },
            new[] { "#14101F", "Prune" },
            new[] { "#15171A", "Ardoise" },
        };
        private static readonly string[][] MuseumLightGrounds =
        {
            new[] { "#D9D2C2", "Parchemin" },
            new[] { "#D4E6EF", "Verrière" },
            new[] { "#EFD4D9", "Vieux rose" },
            new[] { "#D5E3D2", "Herbier" },
            new[] { "#E6DCC6", "Bois blond" },
            new[] { "#DADCE0", "Marbre" },
        };

        /// <summary>Rebâtit le bloc des options (appelé par RebuildSkins, à chaque changement d'apparence).</summary>
        private void RebuildMuseumOptions(string current)
        {
            if (_museumRow == null) return;
            // Sans accent choisi, le champ montre celui qui vaut : l'indigo de Marabook, ou le métal du skin.
            if (_accentHex != null && AppSettings.AccentColor == null) _accentHex.Text = Skins.AccentFor(current, null);
            _museumRow.Children.Clear();
            _museumRow.IsVisible = Skins.IsMuseum(current);
            if (!_museumRow.IsVisible) return;
            var dark = AppSettings.DarkTheme;

            _museumRow.Children.Add(new TextBlock { Text = "Options de « " + Skins.Label(current) + " »", Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            _museumRow.Children.Add(Note("Couleur d'accent : celle choisie ci-dessous. Tant que vous n'en choisissez pas, le skin apporte la sienne (" + Skins.MuseumAccent(current) + ") ; seule sa teinte est gardée, le skin règle son éclat.", 0, 8));

            // ---- la couleur de fond
            _museumRow.Children.Add(new TextBlock { Text = "Couleur de fond", Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 4) });
            var own = AppSettings.SkinBackground == null;
            var effective = Skins.MuseumBackgroundFor(current, dark, AppSettings.SkinBackground);
            var line = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            var ownCheck = new CheckBox { Content = "Celle du skin", IsChecked = own, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Tag = "museum-ground-own" };
            ownCheck.IsCheckedChanged += delegate
            {
                var wanted = ownCheck.IsChecked == true;
                if (wanted == (AppSettings.SkinBackground == null)) return;
                SetMuseumGround(wanted ? null : Skins.MuseumBackgroundFor(Skin.Current, AppSettings.DarkTheme, null));
            };
            line.Children.Add(ownCheck);
            foreach (var entry in dark ? MuseumDarkGrounds : MuseumLightGrounds)
            {
                var hex = entry[0];
                var chosen = !own && string.Equals(hex, AppSettings.SkinBackground, StringComparison.OrdinalIgnoreCase);
                var swatch = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Chrome.ToColor(Ink.Parse(hex))),
                    BorderBrush = chosen ? (IBrush)Chrome.Ink : Chrome.BorderStrong,
                    BorderThickness = new Thickness(chosen ? 2.5 : 1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = "museum-ground-swatch"
                };
                ToolTip.SetTip(swatch, entry[1] + " — " + hex);
                var hexRef = hex;
                swatch.PointerReleased += delegate { SetMuseumGround(hexRef); };
                line.Children.Add(swatch);
            }
            var box = new TextBox { Width = 86, Text = AppSettings.SkinBackground ?? effective, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "museum-ground-hex" };
            ToolTip.SetTip(box, "Une couleur personnalisée : #RRGGBB, puis Entrée");
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Return && e.Key != Key.Enter) return;
                var text = (box.Text ?? "").Trim().ToUpperInvariant();
                if (Skins.IsHex(text)) SetMuseumGround(text);
                e.Handled = true;
            };
            line.Children.Add(box);
            _museumRow.Children.Add(line);
            var bounded = !own && !string.Equals(effective, AppSettings.SkinBackground, StringComparison.OrdinalIgnoreCase);
            _museumRow.Children.Add(Note(
                own ? "Le fond du skin : " + effective + ". Barres, panneaux et champs s'en déduisent."
                : bounded ? "Choisie : " + AppSettings.SkinBackground + ", ramenée à " + effective + " — trop " + (dark ? "claire pour le mode sombre" : "sombre pour le mode clair") + ", le texte ne s'y lirait plus."
                : "Choisie : " + effective + ". Barres, panneaux et champs s'en déduisent.", 4, 8));

            // ---- l'effet verre
            _museumRow.Children.Add(new TextBlock { Text = "Effet verre", Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 2) });
            var glassRow = new StackPanel { Orientation = Orientation.Horizontal };
            var glass = Skins.MuseumGlassFor(current, AppSettings.SkinGlass);
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = glass, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center, Tag = "museum-glass" };
            var label = new TextBlock { Text = glass + " %", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Width = 44 };
            ToolTip.SetTip(slider, "0 % : des barres pleines. 100 % : des barres de verre, qui laissent voir le fond du skin.");
            slider.ValueChanged += delegate
            {
                var value = (int)Math.Round(slider.Value);
                label.Text = value + " %";
                // Le réglage glisse : l'apparence n'est rebâtie qu'une fois qu'il s'est posé.
                if (_glassTimer != null) _glassTimer.Stop();
                _glassTimer = DispatcherTimerLite.After(250, delegate { SetMuseumGlass(value); });
            };
            glassRow.Children.Add(slider);
            glassRow.Children.Add(label);
            _museumRow.Children.Add(glassRow);

            // ---- le flou derrière le verre (24/11, Rémi : « un backdrop-filter, également réglable »)
            _museumRow.Children.Add(new TextBlock { Text = "Flou derrière le verre", Foreground = Chrome.Ink, Margin = new Thickness(0, 8, 0, 2) });
            var blurRow = new StackPanel { Orientation = Orientation.Horizontal };
            var blur = Skins.MuseumBlurFor(current, AppSettings.SkinBlur);
            var blurSlider = new Slider { Minimum = 0, Maximum = 24, Value = blur, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center, Tag = "museum-blur" };
            var blurLabel = new TextBlock { Text = blur + " px", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Width = 44 };
            ToolTip.SetTip(blurSlider, "Ce qu'on voit au travers d'une fenêtre de verre est flouté d'autant. 0 px : net.");
            blurSlider.ValueChanged += delegate
            {
                var value = (int)Math.Round(blurSlider.Value);
                blurLabel.Text = value + " px";
                if (_blurTimer != null) _blurTimer.Stop();
                _blurTimer = DispatcherTimerLite.After(250, delegate { SetMuseumBlur(value); });
            };
            blurRow.Children.Add(blurSlider);
            blurRow.Children.Add(blurLabel);
            _museumRow.Children.Add(blurRow);
            _museumRow.Children.Add(Note("Le flou se voit là où le verre recouvre quelque chose : derrière une fenêtre ouverte par-dessus Marabook.", 4, 0));

        }

        private TextBox _accentHex;
        private Slider _cornerSlider;
        private DispatcherTimerLite _cornerTimer;

        /// <summary>Sous l'accent et les options du skin (24/11, Rémi) : « Réinitialiser les couleurs »
        /// — l'accent, la seconde couleur, le fond et le verre reviennent aux bases de Marabook et,
        /// s'il y en a un, du skin — puis « Arrondi des angles ».</summary>
        private void BuildColorsResetAndCorners(Panel panel)
        {
            var reset = Buttons.Text("Réinitialiser les couleurs", null, Buttons.Bar, Buttons.Look.Outline);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 12, 0, 0);
            reset.Tag = "colors-reset";
            ToolTip.SetTip(reset, "Rend la couleur d'accent de Marabook et, sous un skin, ses couleurs à lui : seconde couleur, fond, effet verre.");
            reset.Click += delegate { ResetColors(); };
            panel.Children.Add(reset);

            panel.Children.Add(Caption("Arrondi des angles", 18));
            panel.Children.Add(Note("Boutons, champs, cartes, menus et fenêtres : chacun garde son arrondi propre, multiplié par cette valeur. À 0 %, plus aucun angle n'est arrondi."));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _cornerSlider = new Slider { Minimum = 0, Maximum = 200, Value = Math.Round(Skin.CornerScale * 100), TickFrequency = 5, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center, Tag = "corner-scale" };
            var label = new TextBlock { Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), Width = 48 };
            Action show = delegate { label.Text = ((int)Math.Round(_cornerSlider.Value)) + " %"; };
            show();
            _cornerSlider.ValueChanged += delegate
            {
                show();
                // Le réglage glisse : l'interface n'est rebâtie qu'une fois qu'il s'est posé.
                if (_cornerTimer != null) _cornerTimer.Stop();
                var value = _cornerSlider.Value / 100.0;
                _cornerTimer = DispatcherTimerLite.After(250, delegate { SetCornerScale(value); });
            };
            var normal = Buttons.Text("100 %", "L'arrondi du dessin d'origine", Buttons.Bar, Buttons.Look.Outline);
            normal.Click += delegate { _cornerSlider.Value = 100; };
            row.Children.Add(_cornerSlider);
            row.Children.Add(label);
            row.Children.Add(normal);
            panel.Children.Add(row);
        }

        internal void SetCornerScale(double scale)
        {
            scale = Math.Max(0, Math.Min(2, Math.Round(scale * 20) / 20));
            if (Math.Abs(scale - AppSettings.CornerScale) < 0.001) return;
            AppSettings.CornerScale = scale;
            RaiseAppearanceChanged();
        }

        /// <summary>L'accent de Marabook (ou du skin), et les couleurs du skin : seconde couleur, fond, verre.</summary>
        internal void ResetColors()
        {
            AppSettings.SkinAccent2 = null;
            AppSettings.SkinBackground = null;
            AppSettings.SkinGlass = -1;
            AppSettings.SkinBlur = -1;
            if (_accentHex != null) _accentHex.Text = Skins.AccentFor(Skin.Current, null);
            if (AppSettings.AccentColor != null) { SetAccent(null); return; } // rebâtit pastilles et skins, prévient la fenêtre
            RaiseAppearanceChanged();
            RebuildSwatches();
            RebuildSkins();
        }

        internal void SetMuseumGround(string hex)
        {
            AppSettings.SkinBackground = Skins.IsHex(hex) ? hex.ToUpperInvariant() : null;
            RaiseAppearanceChanged();
            RebuildSkins();
        }

        private DispatcherTimerLite _blurTimer;

        internal void SetMuseumBlur(int value)
        {
            value = Math.Max(0, Math.Min(24, value));
            var wanted = value == Skins.MuseumBlur(Skin.Current) && AppSettings.SkinBlur < 0 ? -1 : value;
            if (wanted == AppSettings.SkinBlur) return;
            AppSettings.SkinBlur = wanted;
            RaiseAppearanceChanged();
        }

        internal void SetMuseumGlass(int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            // La valeur du skin n'est pas « un choix » : rien n'est écrit dans les réglages.
            var wanted = value == Skins.MuseumGlass(Skin.Current) && AppSettings.SkinGlass < 0 ? -1 : value;
            if (wanted == AppSettings.SkinGlass) return;
            AppSettings.SkinGlass = wanted;
            RaiseAppearanceChanged();
        }

        internal bool MuseumRowShownForProbe { get { return _museumRow != null && _museumRow.IsVisible; } }
    }
}
