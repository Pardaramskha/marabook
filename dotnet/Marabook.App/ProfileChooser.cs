using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE CHOIX DU PROFIL (1.1.0, 27/10/2026) : « Qui êtes-vous ? »,
    /// quatre tuiles — Écrivain·e, Éditeur·ice, Cinglé·e, Moi-même — avec leur
    /// icône et leur aide, et, au premier lancement seulement, le nom de plume
    /// (il préremplit l'auteur·ice par défaut des Préférences — inutile de le
    /// redemander dans Préférences › Profil). « Moi-même » règle ses
    /// fonctionnalités dans une FENÊTRE À PART (FeaturesWindow : la table dans
    /// la page passait sous l'écran). Le même contrôle sert à l'écran d'accueil
    /// du premier lancement (WelcomeWindow) et à Préférences › Profil ; ses
    /// choix s'écrivent aussitôt dans les réglages et préviennent les vues.</summary>
    public class ProfileChooser : StackPanel
    {
        private readonly Dictionary<string, Border> _tiles = new Dictionary<string, Border>();
        private readonly TextBox _penName;
        private readonly Button _featuresButton;
        private string _selected;

        /// <summary>Le profil choisi (null tant que rien n'est cliqué).</summary>
        public string Selected { get { return _selected; } }

        /// <summary>Dans les Préférences : cliquer « Moi-même » ouvre aussitôt la
        /// fenêtre des fonctionnalités (à l'accueil, elle vient après « Commencer »).</summary>
        public bool OpenFeaturesOnChoice;

        public event Action SelectionChanged;

        internal bool FeaturesButtonShownForProbe { get { return _featuresButton.IsVisible; } }

        /// <param name="compact">Dans les Préférences : titre en intertitre, deux
        /// colonnes, pas de nom de plume.</param>
        public ProfileChooser(bool compact)
        {
            Spacing = 0;
            Children.Add(new TextBlock
            {
                Text = "Qui êtes-vous ?",
                Foreground = Chrome.Ink,
                FontSize = compact ? 15 : 22,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            Children.Add(new TextBlock
            {
                Text = "Choisissez votre mode d'utilisation de Marabook pour vous simplifier la tâche. Vous pourrez modifier ce profil à tout moment depuis Préférences › Profil.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var tiles = new UniformGrid { Columns = compact ? 2 : 4, Margin = new Thickness(-5, 0, -5, 0) };
            foreach (var profile in Profiles.All)
            {
                var tile = BuildTile(profile, compact);
                _tiles[profile.Key] = tile;
                tiles.Children.Add(tile);
            }
            Children.Add(tiles);

            // « Moi-même » : le bouton vers la fenêtre des fonctionnalités.
            _featuresButton = Buttons.IconText("personnalise", "Choisir les fonctionnalités…",
                "Cocher et décocher, une à une, ce que Marabook vous montre", Buttons.Bar, Buttons.Look.Outline);
            _featuresButton.HorizontalAlignment = HorizontalAlignment.Left;
            _featuresButton.Margin = new Thickness(0, 14, 0, 0);
            _featuresButton.IsVisible = false;
            _featuresButton.Tag = "features-button"; // sonde
            _featuresButton.Click += async delegate { await FeaturesWindow.Ask(Ui.OwnerOf(this)); };
            Children.Add(_featuresButton);

            if (!compact)
            {
                // Le nom de plume : l'auteur·ice par défaut (Préférences › Auteur).
                var penRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
                var penLabel = new TextBlock
                {
                    Text = "Nom de plume",
                    Foreground = Chrome.SoftText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 110
                };
                DockPanel.SetDock(penLabel, Dock.Left);
                penRow.Children.Add(penLabel);
                _penName = new TextBox
                {
                    Text = AppSettings.DefaultAuthor ?? "",
                    Watermark = "Le nom signé sur vos écrits",
                    MinWidth = 260,
                    MaxWidth = 360,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = "pen-name" // sonde
                };
                ToolTip.SetTip(_penName, "Préremplit le nom d'auteur·ice : compilation d'écrits, pages liminaires, commentaires exportés. Chaque livre peut nommer le sien.");
                _penName.LostFocus += delegate { CommitPenName(); };
                penRow.Children.Add(_penName);
                Children.Add(penRow);
            }

            if (Profiles.Chosen) Select(AppSettings.Profile, false);
        }

        /// <summary>Le nom de plume tel que tapé (l'accueil l'écrit en partant).</summary>
        public void CommitPenName()
        {
            if (_penName == null) return;
            var text = (_penName.Text ?? "").Trim();
            if (text == (AppSettings.DefaultAuthor ?? "")) return;
            AppSettings.DefaultAuthor = text;
            AppSettings.PublishDefaults();
            AppSettings.Save();
        }

        private Border BuildTile(Profiles.Profile profile, bool compact)
        {
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            var icon = Icons.Make(profile.Icon, compact ? 32 : 44, Chrome.Ink);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, 4, 0, 8);
            column.Children.Add(icon);
            column.Children.Add(new TextBlock
            {
                Text = profile.Name,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            column.Children.Add(new TextBlock
            {
                Text = profile.Help,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var tile = new Border
            {
                Child = column,
                Padding = new Thickness(12, 12, 12, 14),
                Margin = new Thickness(5),
                CornerRadius = new CornerRadius(10),
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "profile:" + profile.Key // sonde
            };
            ToolTip.SetTip(tile, profile.Help);
            var key = profile.Key;
            tile.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Select(key, true);
            };
            tile.PointerEntered += delegate { if (_selected != key) tile.BorderBrush = Chrome.SoftText; };
            tile.PointerExited += delegate { if (_selected != key) tile.BorderBrush = Chrome.Border; };
            return tile;
        }

        /// <summary>Choisir une tuile : la pastille d'accent, le bouton des
        /// fonctionnalités pour « Moi-même », et, si c'est un clic, le réglage
        /// écrit et les vues prévenues.</summary>
        public void Select(string key, bool commit)
        {
            _selected = key;
            foreach (var kv in _tiles)
            {
                var chosen = kv.Key == key;
                kv.Value.Background = chosen ? Chrome.AccentTint : (IBrush)Chrome.RaisedBg;
                kv.Value.BorderBrush = chosen ? Chrome.Accent : (IBrush)Chrome.Border;
                kv.Value.BorderThickness = new Thickness(chosen ? 2 : 1);
                kv.Value.Padding = new Thickness(chosen ? 11 : 12, chosen ? 11 : 12, chosen ? 11 : 12, chosen ? 13 : 14);
            }
            _featuresButton.IsVisible = key == Profiles.Custom;
            if (commit)
            {
                Profiles.Choose(key);
                AppSettings.Save();
                if (key == Profiles.Custom && OpenFeaturesOnChoice) FeaturesWindow.Ask(Ui.OwnerOf(this));
            }
            var handler = SelectionChanged;
            if (handler != null) handler();
        }
    }

    /// <summary>La fenêtre des fonctionnalités de « Moi-même » (1.1.0) : une case
    /// par fonctionnalité masquable, par groupe, avec son détail — décochée, la
    /// fonctionnalité disparaît de l'interface, les projets la gardent. Chaque
    /// case s'applique aussitôt.</summary>
    public class FeaturesWindow : Window
    {
        public FeaturesWindow()
        {
            Title = "Ce que Marabook vous montre";
            Width = 600;
            MaxHeight = 720;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Chrome.RaisedBg;

            var layout = new DockPanel();
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 14)
            };
            var close = Buttons.Text("Fermer", null, Buttons.Bar, Buttons.Look.Primary);
            close.MinWidth = 90;
            close.IsDefault = true;
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            Dialogs.Arrange(buttons, close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);

            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Ce que Marabook vous montre",
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Décochez ce dont vous n'avez pas besoin : la fonctionnalité disparaît de l'interface, vos projets la gardent telle quelle. Modifiable à tout moment depuis Préférences › Profil.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            string group = null;
            foreach (var feature in Profiles.Features)
            {
                if (feature.Group != group)
                {
                    group = feature.Group;
                    panel.Children.Add(new TextBlock
                    {
                        Text = group,
                        Foreground = Chrome.SoftText,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 10, 0, 2)
                    });
                }
                panel.Children.Add(BuildRow(feature));
            }
            layout.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            });
            Content = layout;
        }

        private static Control BuildRow(Profiles.Feature feature)
        {
            var box = new CheckBox
            {
                IsChecked = !AppSettings.HiddenFeatures.Contains(feature.Key),
                Margin = new Thickness(0, 3, 0, 0),
                Tag = "feature:" + feature.Key // sonde
            };
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = feature.Label, Foreground = Chrome.Ink });
            label.Children.Add(new TextBlock { Text = feature.Help, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 });
            box.Content = label;
            var key = feature.Key;
            box.IsCheckedChanged += delegate
            {
                var hidden = box.IsChecked != true;
                if (hidden == AppSettings.HiddenFeatures.Contains(key)) return;
                Profiles.SetHidden(key, hidden);
                AppSettings.Save();
            };
            return box;
        }

        /// <summary>La fenêtre, modale sur son propriétaire (null : posée seule).</summary>
        public static Task Ask(Window owner)
        {
            var window = new FeaturesWindow();
            if (owner == null) { window.Show(); return Task.CompletedTask; }
            return window.ShowDialog(owner);
        }
    }
}
