using System;
using System.Collections.Generic;
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
    /// icône et leur aide, le nom de plume (l'auteur·ice par défaut des
    /// Préférences), et, pour « Moi-même » seulement, la table des
    /// fonctionnalités à cocher. Le même contrôle sert à la fenêtre du premier
    /// lancement (ProfileWindow) et à Préférences › Profil : ses choix
    /// s'écrivent aussitôt dans les réglages et préviennent les vues
    /// (Profiles.Notify).</summary>
    public class ProfileChooser : StackPanel
    {
        private readonly Dictionary<string, Border> _tiles = new Dictionary<string, Border>();
        private readonly StackPanel _features;
        private readonly TextBox _penName;
        private string _selected;

        /// <summary>Le profil choisi (null tant que rien n'est cliqué).</summary>
        public string Selected { get { return _selected; } }

        public event Action SelectionChanged;

        internal bool FeatureTableShownForProbe { get { return _features.IsVisible; } }

        /// <param name="compact">Dans les Préférences : moins de marges, le titre en intertitre.</param>
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

            // La table des fonctionnalités : « Moi-même » seulement.
            _features = new StackPanel { Margin = new Thickness(0, 16, 0, 0), IsVisible = false, Tag = "feature-table" };
            _features.Children.Add(new TextBlock
            {
                Text = "Ce que Marabook vous montre",
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            _features.Children.Add(new TextBlock
            {
                Text = "Décochez ce dont vous n'avez pas besoin : la fonctionnalité disparaît de l'interface, vos projets la gardent telle quelle.",
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
                    _features.Children.Add(new TextBlock
                    {
                        Text = group,
                        Foreground = Chrome.SoftText,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 8, 0, 2)
                    });
                }
                _features.Children.Add(BuildFeatureRow(feature));
            }
            Children.Add(_features);

            if (Profiles.Chosen) Select(AppSettings.Profile, false);
        }

        /// <summary>Le nom de plume tel que tapé (la fenêtre du premier lancement l'écrit en partant).</summary>
        public void CommitPenName()
        {
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

        private Control BuildFeatureRow(Profiles.Feature feature)
        {
            var box = new CheckBox
            {
                IsChecked = !AppSettings.HiddenFeatures.Contains(feature.Key),
                Margin = new Thickness(0, 2, 0, 0),
                Tag = "feature:" + feature.Key // sonde
            };
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = feature.Label, Foreground = Chrome.Ink });
            label.Children.Add(new TextBlock { Text = feature.Help, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            box.Content = label;
            var key = feature.Key;
            box.IsCheckedChanged += delegate
            {
                if (_selected != Profiles.Custom) return;
                var hidden = box.IsChecked != true;
                if (hidden == AppSettings.HiddenFeatures.Contains(key)) return;
                Profiles.SetHidden(key, hidden);
                AppSettings.Save();
            };
            return box;
        }

        /// <summary>Choisir une tuile : la pastille d'accent, la table de
        /// « Moi-même », et, si c'est un clic, le réglage écrit et les vues prévenues.</summary>
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
            _features.IsVisible = key == Profiles.Custom;
            if (commit)
            {
                Profiles.Choose(key);
                AppSettings.Save();
            }
            var handler = SelectionChanged;
            if (handler != null) handler();
        }
    }

    /// <summary>La fenêtre du premier lancement (ou tant qu'aucun profil n'est
    /// choisi) : le sélecteur de profil, « Commencer » quand une tuile est
    /// choisie. Fermée sans choix, elle reviendra au prochain lancement.</summary>
    public class ProfileWindow : Window
    {
        private readonly ProfileChooser _chooser;

        public ProfileWindow()
        {
            Title = "Qui êtes-vous ? — " + AppInfo.Name;
            Width = 920;
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
                Margin = new Thickness(20, 0, 20, 16)
            };
            var start = Buttons.Text("Commencer", "Garder ce profil et ouvrir Marabook", Buttons.Bar, Buttons.Look.Primary);
            start.MinWidth = 110;
            start.IsEnabled = false;
            start.IsDefault = true;
            start.Click += delegate { _chooser.CommitPenName(); Close(); };
            buttons.Children.Add(start);
            Dialogs.Arrange(buttons, start);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);

            _chooser = new ProfileChooser(false) { Margin = new Thickness(24, 20, 24, 12) };
            _chooser.SelectionChanged += delegate { start.IsEnabled = _chooser.Selected != null; };
            layout.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 720,
                Content = _chooser
            });
            Content = layout;
            Closing += delegate { _chooser.CommitPenName(); };
        }

        public static System.Threading.Tasks.Task Ask(Window owner)
        {
            return new ProfileWindow().ShowDialog(owner);
        }
    }
}
