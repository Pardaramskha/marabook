using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Marabook.View
{
    /// <summary>L'écran d'accueil (13/09/2026) : à l'ouverture de l'app SANS
    /// projet, la fenêtre principale reste vide — un voile blanc cassé, pas
    /// d'interface — et cette fenêtre, posée dessus, prend la majeure partie
    /// de la place. Titre, version cliquable (page GitHub), les cinq
    /// derniers projets en tuiles sur une ligne, la tuile pointillée « + »
    /// (un projet n'existe qu'enregistré quelque part : elle ouvre tout de
    /// suite le dialogue d'enregistrement), et l'indicateur de mise à jour.
    /// Elle n'est PAS fermable : elle disparaît quand un projet s'ouvre, ou
    /// avec l'app si on la ferme avant (Release, appelé par la coquille).
    /// Suit la fenêtre principale quand elle bouge ou change de taille.</summary>
    public class WelcomeWindow : Window
    {
        private readonly MainWindow _shell;
        private bool _release;
        private TextBlock _updateText;
        private Button _updateButton;
        private Updater.Info _latest;

        // L'indicateur de chargement (14/09) : un projet long à ouvrir ne
        // doit pas passer pour un plantage — le texte dit ce qui s'ouvre,
        // le curseur va et vient sur son rail tant que ça dure.
        private bool _opening;
        private UIElement _loading;
        private TextBlock _loadingText;
        private Border _runner;
        private UniformGrid _tiles;
        private Button _openButton;

        public WelcomeWindow(MainWindow shell)
        {
            _shell = shell;
            Owner = shell;
            Title = MainWindow.AppName;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Coins arrondis (13/09) : une fenêtre sans chrome est carrée —
            // fenêtre transparente, et c'est la bordure du contenu qui
            // dessine le panneau, rayon 16.
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            // Les toasts à boutons (18/09 : rapport d'arrêt brutal) vivent
            // ici tant que l'accueil couvre la fenêtre principale.
            _noticeHost = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 20, 20)
            };
            var root = new Grid();
            root.Children.Add(new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = Build()
            });
            root.Children.Add(_noticeHost);
            Content = root;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!_release) e.Cancel = true; // jamais fermée à la main
            };
            Loaded += delegate { Fit(); StartUpdateCheck(); ModuleStore.CheckOnline(Dispatcher); };
            shell.SizeChanged += delegate { Fit(); };
            shell.LocationChanged += delegate { Fit(); };
            shell.StateChanged += delegate { Fit(); };
        }

        private readonly StackPanel _noticeHost;

        /// <summary>Un toast à boutons (NoticeToast) en bas à droite de l'accueil.</summary>
        public void ShowNotice(Border toast)
        {
            NoticeToast.Show(_noticeHost, toast);
        }

        /// <summary>Un projet est ouvert (ou l'app se ferme) : l'accueil se
        /// retire — la seule façon de le fermer.</summary>
        public void Release()
        {
            if (_release) return;
            _release = true;
            try { Close(); }
            catch { }
        }

        /// <summary>80 % de la fenêtre principale, centrée dessus — en
        /// unités DIP, quel que soit l'écran (le PointToScreen rend des
        /// pixels physiques : converti).</summary>
        private void Fit()
        {
            if (_shell.ActualWidth <= 0 || _shell.ActualHeight <= 0) return;
            if (_shell.WindowState == WindowState.Minimized) return;
            Width = Math.Max(640, _shell.ActualWidth * 0.8);
            Height = Math.Max(420, _shell.ActualHeight * 0.8);
            var origin = new Point(0, 0);
            try
            {
                origin = _shell.PointToScreen(new Point(0, 0));
                var source = PresentationSource.FromVisual(_shell);
                if (source != null && source.CompositionTarget != null)
                    origin = source.CompositionTarget.TransformFromDevice.Transform(origin);
            }
            catch { }
            Left = origin.X + (_shell.ActualWidth - Width) / 2;
            Top = origin.Y + (_shell.ActualHeight - Height) / 2;
        }

        private UIElement Build()
        {
            var root = new DockPanel { Margin = new Thickness(40, 32, 40, 28) };

            // ---- en tête : le titre, la version cliquable
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = MainWindow.AppName + " - C'est parti pour nerder",
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.Ink
            });
            var link = new Hyperlink(new Run("Version " + MainWindow.AppVersion))
            {
                Foreground = Chrome.SoftText
            };
            link.Click += delegate
            {
                try { Process.Start(Updater.RepositoryUrl); }
                catch { }
            };
            var subtitle = new TextBlock { FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
            subtitle.Inlines.Add(link);
            head.Children.Add(subtitle);
            root.Children.Add(head);

            // ---- en pied : la mise à jour
            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(foot, Dock.Bottom);
            _updateText = new TextBlock
            {
                Text = "Recherche d'une mise à jour…",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            foot.Children.Add(_updateText);
            _updateButton = Buttons.Text("Mettre à jour et redémarrer",
                "Télécharge la version publiée, remplace les fichiers de l'application et la relance",
                Buttons.Compact, Buttons.Look.Primary);
            _updateButton.Margin = new Thickness(12, 0, 0, 0);
            _updateButton.Visibility = Visibility.Collapsed;
            _updateButton.Click += delegate { InstallUpdate(); };
            foot.Children.Add(_updateButton);
            root.Children.Add(foot);

            // ---- au centre : les tuiles, sur UNE ligne
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // Le titre, et à côté « Ouvrir un projet… » (demande de Rémi,
            // 13/09) : un projet absent des récents s'ouvre d'ici, sans
            // passer par la coquille voilée.
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 0, 6)
            };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Derniers projets",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            });
            var open = Buttons.Text("Ouvrir un projet…",
                "Choisir un fichier .plot sur le disque",
                Buttons.Compact, Buttons.Look.Outline);
            open.Margin = new Thickness(12, 0, 0, 0);
            open.Click += delegate
            {
                if (_opening) return;
                var chosen = _shell.AskProjectFile(this);
                if (chosen != null) BeginOpen(chosen);
            };
            _openButton = open;
            titleRow.Children.Add(open);
            center.Children.Add(titleRow);
            var tiles = new UniformGrid { Rows = 1, Columns = 6, Height = 150 };
            var recents = Settings.AppSettings.RecentFiles;
            var shown = 0;
            foreach (var path in recents)
            {
                if (shown >= 5) break;
                tiles.Children.Add(RecentTile(path));
                shown++;
            }
            tiles.Children.Add(NewTile());
            for (var i = shown + 1; i < 6; i++)
                tiles.Children.Add(new Border { Visibility = Visibility.Hidden });
            _tiles = tiles;
            center.Children.Add(tiles);
            center.Children.Add(BuildLoading());
            center.Children.Add(BuildModules());
            root.Children.Add(center);
            return root;
        }

        // ------------------------------------------------------- modules (DLC)

        private WrapPanel _modules;

        /// <summary>Le bloc « DLC » sous les projets (22/09) : une tuile par
        /// module du catalogue — nom, une ligne, et l'état : installé (sa
        /// version), disponible, ou ce qui empêche de le savoir. Cliquer
        /// ouvre la fiche du module (ce qu'il apporte, Installer).</summary>
        private UIElement BuildModules()
        {
            var block = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6) };
            titleRow.Children.Add(new TextBlock
            {
                Text = "DLC",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "des modules qui ajoutent des fonctions à Marabook — installés ici, gérés dans Préférences › DLC",
                FontSize = 11,
                Foreground = Chrome.FaintText,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            block.Children.Add(titleRow);
            _modules = new WrapPanel();
            block.Children.Add(_modules);
            RefreshModules();
            ModuleStore.Changed += RefreshModules;
            Closed += delegate { ModuleStore.Changed -= RefreshModules; };
            return block;
        }

        private void RefreshModules()
        {
            if (_modules == null) return;
            _modules.Children.Clear();
            foreach (var state in ModuleStore.Refresh())
            {
                var stateRef = state;
                var tile = Tile();
                tile.Width = 236;
                tile.Cursor = Cursors.Hand;
                tile.ToolTip = state.Source.Title.Length > 0 ? state.Source.Title : state.Source.Name;
                var column = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
                var head = new DockPanel();
                var dot = new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius = new CornerRadius(4.5),
                    Background = state.IsInstalled ? Chrome.Accent : Chrome.Border,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 1, 0, 0),
                    ToolTip = state.IsInstalled ? "Installé" : "Non installé"
                };
                DockPanel.SetDock(dot, Dock.Right);
                head.Children.Add(dot);
                head.Children.Add(new TextBlock
                {
                    Text = state.Source.Name,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Chrome.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                column.Children.Add(head);
                column.Children.Add(new TextBlock
                {
                    Text = state.Source.Features.Count > 0 ? state.Source.Features[0] : state.Source.Title,
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 30,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                column.Children.Add(new TextBlock
                {
                    Text = state.Label,
                    FontSize = 11,
                    Foreground = state.IsInstalled ? Chrome.Accent : Chrome.FaintText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                tile.Child = column;
                tile.MouseLeftButtonUp += delegate { ModuleDialog.Show(this, stateRef); };
                _modules.Children.Add(tile);
            }
        }

        // ------------------------------------------------------- chargement

        /// <summary>La ligne d'attente sous les tuiles : un rail et son
        /// curseur, le nom du projet qui s'ouvre. Cachée (mais sa place
        /// réservée, rien ne saute) tant qu'on n'ouvre rien.</summary>
        private UIElement BuildLoading()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 14, 0, 0),
                Visibility = Visibility.Hidden
            };
            var rail = new Canvas
            {
                Width = 120,
                Height = 4,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };
            rail.Children.Add(new Border
            {
                Width = 120,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Chrome.Border
            });
            _runner = new Border
            {
                Width = 40,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Chrome.Accent
            };
            rail.Children.Add(_runner);
            row.Children.Add(rail);
            _loadingText = new TextBlock
            {
                FontSize = 12,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            row.Children.Add(_loadingText);
            _loading = row;
            return row;
        }

        /// <summary>Ouvre un projet en montrant l'attente : les tuiles et le
        /// bouton se désactivent, le curseur court, la lecture du fichier se
        /// fait en fond (MainWindow.OpenFileInBackground). Réussite : l'accueil
        /// est relâché par la coquille. Échec (fichier illisible, secours
        /// refusé) : tout revient, l'accueil reste.</summary>
        private void BeginOpen(string path)
        {
            if (_opening) return;
            _opening = true;
            _tiles.IsEnabled = false;
            _openButton.IsEnabled = false;
            _loadingText.Text = "Ouverture de « "
                + System.IO.Path.GetFileNameWithoutExtension(path) + " »…";
            _loading.Visibility = Visibility.Visible;
            var sweep = new DoubleAnimation(0, 80, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            _runner.BeginAnimation(Canvas.LeftProperty, sweep);
            _shell.OpenFileInBackground(path, delegate
            {
                if (_shell.HasProjectPath) { Release(); return; }
                _runner.BeginAnimation(Canvas.LeftProperty, null);
                _loading.Visibility = Visibility.Hidden;
                _tiles.IsEnabled = true;
                _openButton.IsEnabled = true;
                _opening = false;
            });
        }

        /// <summary>Une tuile de projet récent : le nom, le dossier, la date
        /// de dernière modification ; un clic l'ouvre.</summary>
        /// <summary>L'icône des fichiers .plot (17/09/2026) : assets\plot-file.png
        /// à côté de l'exe — la même image que assets\plot.ico posée sur
        /// l'association dans l'Explorateur. Null si absente ou illisible.</summary>
        internal static FrameworkElement PlotFileIcon(double size)
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                System.IO.Path.Combine("assets", "plot-file.png"));
            if (!File.Exists(path)) return null;
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = (int)Math.Ceiling(size * 2);
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                var element = new Image { Source = image, Width = size, Height = size };
                RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.HighQuality);
                return element;
            }
            catch { return null; }
        }

        private UIElement RecentTile(string path)
        {
            var exists = File.Exists(path);
            var tile = Tile();
            tile.Cursor = exists ? Cursors.Hand : Cursors.Arrow;
            tile.Opacity = exists ? 1 : 0.55;
            var column = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            var icon = PlotFileIcon(26) ?? Icons.Make("folder-bold", 22, Chrome.Accent) as FrameworkElement;
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                icon.Margin = new Thickness(0, 0, 0, 8);
                column.Children.Add(icon);
            }
            column.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(path),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36
            });
            column.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetDirectoryName(path),
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            });
            column.Children.Add(new TextBlock
            {
                Text = exists
                    ? "Modifié le " + Dates.Display(File.GetLastWriteTime(path))
                    : "Fichier introuvable",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 2, 0, 0)
            });
            tile.Child = column;
            tile.ToolTip = path;
            if (exists)
            {
                // L'ouverture passe par l'attente (14/09) ; si elle échoue
                // (fichier illisible), l'accueil reste.
                tile.MouseLeftButtonUp += delegate { BeginOpen(path); };
            }
            return tile;
        }

        /// <summary>La tuile « + » : contour pointillé, elle crée un projet
        /// EN L'ENREGISTRANT — pas de projet fantôme.</summary>
        private UIElement NewTile()
        {
            var grid = new Grid { Margin = new Thickness(6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var dashes = new Rectangle
            {
                Stroke = Chrome.BorderStrong,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
                RadiusX = 8,
                RadiusY = 8
            };
            grid.Children.Add(dashes);
            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var plus = Icons.Make("plus-bold", 30, Chrome.SoftText) as FrameworkElement;
            if (plus != null)
            {
                plus.HorizontalAlignment = HorizontalAlignment.Center;
                plus.Margin = new Thickness(0, 0, 0, 8);
                column.Children.Add(plus);
            }
            column.Children.Add(new TextBlock
            {
                Text = "Nouveau projet",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center
            });
            grid.Children.Add(column);
            grid.ToolTip = "Créer un projet — il est enregistré tout de suite, là où vous le dites";
            grid.MouseEnter += delegate { dashes.Stroke = Chrome.Accent; };
            grid.MouseLeave += delegate { dashes.Stroke = Chrome.BorderStrong; };
            grid.MouseLeftButtonUp += delegate
            {
                if (_shell.NewProjectWithSaveDialog()) Release();
            };
            return grid;
        }

        private static Border Tile()
        {
            var tile = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(6)
            };
            tile.MouseEnter += delegate { tile.BorderBrush = Chrome.Accent; };
            tile.MouseLeave += delegate { tile.BorderBrush = Chrome.Border; };
            return tile;
        }

        // ------------------------------------------------------ mise à jour

        /// <summary>La vérification, en fond — la fenêtre ne l'attend pas ;
        /// sans release publiée, l'indicateur le dit et c'est tout.</summary>
        private void StartUpdateCheck()
        {
            var version = MainWindow.AppVersion;
            Task.Factory.StartNew(delegate { return Updater.Run(version); })
                .ContinueWith(delegate(Task<Updater.Check> done)
                {
                    var check = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        if (check == null)
                        {
                            _updateText.Text = "Mise à jour : vérification impossible";
                            return;
                        }
                        _latest = check.Latest;
                        _updateText.Text = check.Available
                            ? check.Message
                            : check.Message + " (" + version + ")";
                        _updateButton.Visibility = check.Available && check.Latest != null
                            ? Visibility.Visible : Visibility.Collapsed;
                    }));
                });
        }

        private void InstallUpdate()
        {
            if (_latest == null) return;
            _updateButton.IsEnabled = false;
            _updateText.Text = "Téléchargement de la version " + _latest.Version + "…";
            var info = _latest;
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            Task.Factory.StartNew(delegate { Updater.Install(info, appDir); })
                .ContinueWith(delegate(Task done)
                {
                    var failure = done.Exception == null ? null : done.Exception.GetBaseException();
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        if (failure != null)
                        {
                            _updateButton.IsEnabled = true;
                            _updateText.Text = "Mise à jour impossible : " + failure.Message;
                            return;
                        }
                        _updateText.Text = "Prête. Marabook redémarre…";
                        Release();
                        Application.Current.Shutdown();
                    }));
                });
        }
    }
}
