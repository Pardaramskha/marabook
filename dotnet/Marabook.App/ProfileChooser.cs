using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE CHOIX DU PROFIL (1.1.0, 27/10/2026) : « Qui êtes-vous ? », et,
    /// au premier lancement seulement, le nom de plume (il préremplit
    /// l'auteur·ice par défaut des Préférences — inutile de le redemander dans
    /// Préférences › Profil). « Moi-même » règle ses fonctionnalités dans une
    /// FENÊTRE À PART (FeaturesWindow : la table dans la page passait sous
    /// l'écran). Le même contrôle sert à l'écran d'accueil du premier lancement
    /// (WelcomeWindow) et à Préférences › Profil ; ses choix s'écrivent aussitôt
    /// dans les réglages et préviennent les vues.
    ///
    /// UN CHOIX DE DIFFICULTÉ (2.0.0, 25/11/2026, Rémi) : Écrivain·e, Éditeur·ice
    /// et Cinglé·e sont trois COLONNES, comme l'écran de difficulté d'un jeu vidéo
    /// — une banderole de couleur (le dégradé des tuiles) qui dit la difficulté,
    /// l'image du profil collée aux bords de la colonne et à la banderole, le nom,
    /// la description, la liste des fonctionnalités. « Moi-même » est une
    /// tuile-ligne de toute la largeur, dessous. Une tuile survolée s'agrandit ;
    /// sous un skin premium (série « Museum »), un filet en orbite en fait le tour
    /// tant qu'on la survole, et la banderole porte la tranche en S à la place du
    /// dégradé. Cliquer « Moi-même » depuis un autre profil en hérite les cases
    /// (Profiles.Choose).</summary>
    public class ProfileChooser : StackPanel
    {
        /// <summary>Une tuile et ce qui change en elle : le cadre, l'anneau de la
        /// tuile choisie, le filet en orbite, la banderole, l'agrandissement.</summary>
        private sealed class Tile
        {
            public Profiles.Profile Profile;
            public Border Frame, Ring, Clip, Banner;
            public OrbitLine Orbit;
            public Image Picture;
            public PictureCrop Crop;
            public ScaleTransform Scale;
            public double Grow;
            public bool Hovered;
        }

        /// <summary>Le cadre de l'image d'une colonne : l'image y tient toute la LARGEUR, son sommet
        /// en haut ; quand la place manque en hauteur (FitTo), c'est son bas qui est rogné — jamais
        /// ses côtés, jamais son sommet.</summary>
        private sealed class PictureCrop : Panel
        {
            public double Ratio = 1; // hauteur montrée / largeur

            public PictureCrop() { ClipToBounds = true; }

            protected override Size MeasureOverride(Size availableSize)
            {
                var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
                foreach (var child in Children) child.Measure(new Size(width, width));
                return new Size(0, Math.Floor(width * Ratio));
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Width));
                return finalSize;
            }
        }

        private const double ColumnGrow = 1.03, RowGrow = 1.012, MinPictureRatio = 0.6;
        private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(150);
        private static readonly Dictionary<string, Bitmap> Pictures = new Dictionary<string, Bitmap>();

        private readonly Dictionary<string, Tile> _tiles = new Dictionary<string, Tile>();
        private readonly TextBox _penName;
        private readonly Button _featuresButton;
        private string _selected;

        /// <summary>Le profil choisi (null tant que rien n'est cliqué).</summary>
        public string Selected { get { return _selected; } }

        /// <summary>Dans les Préférences : cliquer « Moi-même » ouvre aussitôt la
        /// fenêtre des fonctionnalités (à l'accueil, elle vient après « Commencer »).</summary>
        public bool OpenFeaturesOnChoice;

        public event Action SelectionChanged;

        /// <summary>La ligne du nom de plume (premier lancement ; null dans les Préférences) : à
        /// poser par l'hôte — l'accueil la met au pied de la page, à côté de « Commencer »,
        /// toujours visible quoi que fasse le défilement (25/11).</summary>
        public Control PenNameRow { get; private set; }

        internal bool FeaturesButtonShownForProbe { get { return _featuresButton.IsVisible; } }

        /// <param name="compact">Dans les Préférences : titre en intertitre, textes
        /// plus petits, pas de nom de plume.</param>
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

            // « Moi-même » : le bouton vers la fenêtre des fonctionnalités, au bout de sa tuile-ligne
            // (25/11 : sous les tuiles, il poussait la page d'une ligne dès qu'on choisissait ce profil).
            _featuresButton = Buttons.IconText("personnalise", "Choisir les fonctionnalités…",
                "Choisissez ce que vous voulez voir de Marabook.", Buttons.Bar, Buttons.Look.Outline);
            _featuresButton.VerticalAlignment = VerticalAlignment.Center;
            _featuresButton.Margin = new Thickness(12, 0, 0, 0);
            _featuresButton.IsVisible = false;
            _featuresButton.Tag = "features-button"; // sonde
            _featuresButton.Click += async delegate { await FeaturesWindow.Ask(Ui.OwnerOf(this)); };

            // Les trois colonnes, puis « Moi-même » sur toute la largeur. Une seule
            // grille : la tuile survolée passe DEVANT ses voisines (ZIndex).
            var tiles = new Grid { Margin = new Thickness(-5, 0, -5, 0) };
            tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var column = 0;
            foreach (var profile in Profiles.All)
            {
                var isRow = profile.Image == null;
                var tile = isRow ? BuildRowTile(profile, compact) : BuildColumnTile(profile, compact);
                _tiles[profile.Key] = tile;
                if (isRow) Grid.SetRow(tile.Frame, 1);
                else
                {
                    tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(tile.Frame, column++);
                }
                tiles.Children.Add(tile.Frame);
            }
            foreach (var tile in _tiles.Values) if (tile.Profile.Image == null) Grid.SetColumnSpan(tile.Frame, Math.Max(1, column));
            Children.Add(tiles);

            if (!compact)
            {
                // Le nom de plume : l'auteur·ice par défaut (Préférences › Auteur).
                var penRow = new DockPanel();
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
                ToolTip.SetTip(_penName, "Préremplit le nom d'auteur·ice : Le nom par défaut qui sera inscrit comme auteur·ice dans les pages liminaires ou les fichiers. Modifiable par écrit ou livre.");
                _penName.LostFocus += delegate { CommitPenName(); };
                penRow.Children.Add(_penName);
                PenNameRow = penRow;
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

        /// <summary>Tenir dans cette hauteur, si possible sans défiler : les images sont rognées par
        /// le bas (elles gardent au moins 60 % de leur hauteur) ; la place revenue, elles se montrent entières.</summary>
        public void FitTo(double height)
        {
            PictureCrop sample = null;
            foreach (var tile in _tiles.Values) if (tile.Crop != null) { sample = tile.Crop; break; }
            if (sample == null || height <= 0 || sample.Bounds.Width <= 0) return;
            var width = sample.Bounds.Width;
            var wanted = (sample.Bounds.Height - (DesiredSize.Height - height) - 3) / width; // 3 px de jeu : les arrondis de la mise en page
            var ratio = Math.Max(MinPictureRatio, Math.Min(1, wanted));
            if (Math.Abs(ratio - sample.Ratio) < 0.006) return; // moins de 2 px : on ne bouge plus
            foreach (var tile in _tiles.Values)
                if (tile.Crop != null) { tile.Crop.Ratio = ratio; tile.Crop.InvalidateMeasure(); }
        }

        // ---------------------------------------------------------------- les tuiles

        /// <summary>Une colonne : la banderole de difficulté, l'image, le nom, la
        /// description, la liste des fonctionnalités.</summary>
        private Tile BuildColumnTile(Profiles.Profile profile, bool compact)
        {
            var tile = new Tile { Profile = profile, Grow = ColumnGrow };
            var body = new StackPanel();

            // — la banderole : « Difficulté : Abordable », sur le dégradé des tuiles.
            var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = "Difficulté :", Foreground = Chrome.SoftText, FontSize = compact ? 10.5 : 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            words.Children.Add(new TextBlock { Text = profile.Difficulty, Foreground = Chrome.Ink, FontSize = compact ? 11.5 : 12.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
            tile.Banner = new Border
            {
                Child = words,
                Height = compact ? 28 : 32,
                Padding = new Thickness(compact ? 10 : 14, 0, 30, 0),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Tag = "profile-banner" // sonde
            };
            body.Children.Add(tile.Banner);

            // — l'image : toute la largeur de la colonne, son sommet contre la banderole.
            tile.Picture = new Image
            {
                Source = Picture(profile.Image),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = "profile-picture" // sonde
            };
            RenderOptions.SetBitmapInterpolationMode(tile.Picture, BitmapInterpolationMode.HighQuality);
            tile.Crop = new PictureCrop();
            tile.Crop.Children.Add(tile.Picture);
            body.Children.Add(tile.Crop);

            // — le nom, la description, la liste.
            var text = new StackPanel { Margin = new Thickness(compact ? 10 : 14, compact ? 8 : 10, compact ? 10 : 14, compact ? 12 : 16) };
            var name = new TextBlock
            {
                Text = profile.Name,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = compact ? 14 : 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Skin.Title(name);
            text.Children.Add(name);
            text.Children.Add(new TextBlock
            {
                Text = profile.Help,
                Foreground = Chrome.SoftText,
                FontSize = compact ? 11 : 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, compact ? 8 : 10)
            });
            foreach (var perk in profile.Perks)
            {
                var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Tag = "profile-perk" }; // sonde
                var bullet = new TextBlock { Text = "•", Foreground = Chrome.Accent, FontSize = compact ? 11 : 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(bullet, Dock.Left);
                line.Children.Add(bullet);
                line.Children.Add(new TextBlock { Text = perk, Foreground = Chrome.Ink, FontSize = compact ? 11 : 12, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(line);
            }
            body.Children.Add(text);
            Frame(tile, body);
            return tile;
        }

        /// <summary>« Moi-même » : une tuile-ligne — l'icône, le nom, l'aide, et, une fois choisie,
        /// le bouton vers ses fonctionnalités.</summary>
        private Tile BuildRowTile(Profiles.Profile profile, bool compact)
        {
            var tile = new Tile { Profile = profile, Grow = RowGrow };
            var row = new DockPanel { Margin = new Thickness(compact ? 12 : 16, compact ? 10 : 12, compact ? 12 : 16, compact ? 10 : 12) };
            var icon = Icons.Make(profile.Icon, compact ? 28 : 34, Chrome.Ink);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, compact ? 12 : 16, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            DockPanel.SetDock(_featuresButton, Dock.Right);
            row.Children.Add(_featuresButton);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = profile.Name, Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, FontSize = compact ? 14 : 16 };
            Skin.Title(name);
            text.Children.Add(name);
            text.Children.Add(new TextBlock { Text = profile.Help, Foreground = Chrome.SoftText, FontSize = compact ? 11 : 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            row.Children.Add(text);
            Frame(tile, row);
            return tile;
        }

        /// <summary>Le cadre d'une tuile autour de son contenu. Trois couches : le
        /// contenu, ROGNÉ aux coins du cadre (l'image va jusqu'aux bords) ; l'anneau
        /// d'accent de la tuile choisie ; le filet en orbite des skins premium. Le
        /// cadre lui-même ne rogne rien et ne change jamais d'épaisseur : choisir une
        /// tuile ne déplace pas son contenu d'un pixel.</summary>
        private void Frame(Tile tile, Control content)
        {
            var key = tile.Profile.Key;
            tile.Clip = new Border { Child = content, ClipToBounds = true };
            tile.Ring = new Border { BorderBrush = Chrome.Accent, BorderThickness = new Thickness(2), Margin = new Thickness(-1), IsHitTestVisible = false, IsVisible = false };
            var frame = tile.Frame = new Border
            {
                Margin = new Thickness(5),
                CornerRadius = new CornerRadius(10),
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "profile:" + key // sonde
            };
            // Les coins du dedans SUIVENT ceux du cadre, que le skin et « Arrondi des
            // angles » retouchent après coup (comme l'hôte d'ombre de CardLift).
            Skin.KeepCorners(tile.Clip);
            Skin.KeepCorners(tile.Ring);
            Action follow = delegate
            {
                var radius = frame.CornerRadius.TopLeft;
                tile.Clip.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
                tile.Ring.CornerRadius = new CornerRadius(radius);
            };
            follow();
            frame.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Border.CornerRadiusProperty) follow();
            };
            // Le filet en orbite : au survol seulement (il dort hors des skins premium).
            tile.Orbit = (OrbitLine)Skin.OrbitInside(frame);
            tile.Orbit.Gate = delegate { return tile.Hovered; };
            var layers = new Panel();
            layers.Children.Add(tile.Clip);
            layers.Children.Add(tile.Ring);
            layers.Children.Add(tile.Orbit);
            frame.Child = layers;
            TileFrame.Dress(frame); // « Ascension », lot 2 : la double bordure des tuiles

            // L'agrandissement au survol, autour du centre : le pointeur reste dedans.
            tile.Scale = new ScaleTransform(1, 1);
            frame.RenderTransform = tile.Scale;
            frame.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            ToolTip.SetTip(frame, tile.Profile.Help);
            frame.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Select(key, true);
            };
            frame.PointerEntered += delegate { Hover(tile, true); };
            frame.PointerExited += delegate { Hover(tile, false); };
            // La banderole se refait quand la tuile revient à l'écran : le skin a pu changer.
            frame.AttachedToVisualTree += delegate { DressBanner(tile); };
            DressBanner(tile);
        }

        /// <summary>La banderole : le dégradé des tuiles colorées (transparent
        /// jusqu'à la moitié, la couleur pleine au bord droit) — ou, sous un skin
        /// premium, leur tranche en S.</summary>
        private static void DressBanner(Tile tile)
        {
            if (tile.Banner == null) return;
            var color = Ink.Parse(tile.Profile.Color).ToColor();
            var fade = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
            };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.5));
            fade.GradientStops.Add(new GradientStop(color, 1.0));
            tile.Banner.Background = Skin.TileFade(color) ?? fade;
        }

        /// <summary>Survolée, la tuile s'agrandit et passe devant ses voisines ;
        /// le filet en orbite s'allume (skins premium).</summary>
        private void Hover(Tile tile, bool hovered)
        {
            tile.Hovered = hovered;
            // « Animations » décoché (Préférences) : la taille change d'un coup.
            tile.Scale.Transitions = !AppSettings.Animations ? null : new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = GrowTime, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = GrowTime, Easing = new CubicEaseOut() }
            };
            tile.Scale.ScaleX = tile.Scale.ScaleY = hovered ? tile.Grow : 1;
            tile.Frame.ZIndex = hovered ? 1 : 0;
            if (_selected != tile.Profile.Key) tile.Frame.BorderBrush = hovered ? Chrome.SoftText : (IBrush)Chrome.Border;
            tile.Orbit.Nudge();
        }

        /// <summary>L'image embarquée d'un profil (profils/&lt;nom&gt;.png), décodée
        /// une fois, ou null.</summary>
        private static Bitmap Picture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Bitmap bitmap;
            if (Pictures.TryGetValue(name, out bitmap)) return bitmap;
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("profils/" + name + ".png"))
                    bitmap = stream == null ? null : new Bitmap(stream);
            }
            catch (Exception) { bitmap = null; }
            Pictures[name] = bitmap;
            return bitmap;
        }

        /// <summary>Choisir une tuile : l'anneau d'accent, le bouton des
        /// fonctionnalités pour « Moi-même », et, si c'est un clic, le réglage
        /// écrit et les vues prévenues — « Moi-même » hérite alors du profil que
        /// l'on quitte (Profiles.Choose).</summary>
        public void Select(string key, bool commit)
        {
            _selected = key;
            foreach (var kv in _tiles)
            {
                var chosen = kv.Key == key;
                var tile = kv.Value;
                tile.Frame.Background = chosen ? Chrome.AccentTint : (IBrush)Chrome.CardBg;
                tile.Frame.BorderBrush = chosen ? Chrome.Accent : tile.Hovered ? Chrome.SoftText : (IBrush)Chrome.Border;
                tile.Ring.IsVisible = chosen;
                // « Ascension », lot 2 : la tuile choisie s'allume par sa bordure galbée (TileFrame lit le
                // filet du cadre), l'anneau d'accent s'efface — un cadre de trop dans la double bordure.
                if (AscensionArt.On) { tile.Ring.Opacity = 0; tile.Frame.BorderBrush = chosen ? (IBrush)Chrome.Accent : Chrome.Border; }
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

        // ---------------------------------------------------------------- sonde

        internal void HoverForProbe(string key, bool hovered) { Tile tile; if (_tiles.TryGetValue(key, out tile)) Hover(tile, hovered); }
        internal double ScaleForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Scale.ScaleX : 0; }
        internal OrbitLine OrbitForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Orbit : null; }
        internal Border BannerForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Banner : null; }
        internal Border FrameForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Frame : null; }
        internal Border ClipForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Clip : null; }
        internal Control CropForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Crop : null; }
        internal Image PictureForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) ? tile.Picture : null; }
        internal bool RingShownForProbe(string key) { Tile tile; return _tiles.TryGetValue(key, out tile) && tile.Ring.IsVisible; }
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
