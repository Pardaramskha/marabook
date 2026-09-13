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
using System.Windows.Shapes;
using System.Windows.Threading;

namespace UniversSale.View
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

        public WelcomeWindow(MainWindow shell)
        {
            _shell = shell;
            Owner = shell;
            Title = MainWindow.AppName;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Chrome.RaisedBg;
            BorderBrush = Chrome.Border;
            BorderThickness = new Thickness(1);
            Content = Build();
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!_release) e.Cancel = true; // jamais fermée à la main
            };
            Loaded += delegate { Fit(); StartUpdateCheck(); };
            shell.SizeChanged += delegate { Fit(); };
            shell.LocationChanged += delegate { Fit(); };
            shell.StateChanged += delegate { Fit(); };
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
                Foreground = Chrome.SoftText,
                ToolTip = Updater.RepositoryUrl
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
            center.Children.Add(new TextBlock
            {
                Text = "Derniers projets",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(6, 0, 0, 6)
            });
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
            center.Children.Add(tiles);
            root.Children.Add(center);
            return root;
        }

        /// <summary>Une tuile de projet récent : le nom, le dossier, la date
        /// de dernière modification ; un clic l'ouvre.</summary>
        private UIElement RecentTile(string path)
        {
            var exists = File.Exists(path);
            var tile = Tile();
            tile.Cursor = exists ? Cursors.Hand : Cursors.Arrow;
            tile.Opacity = exists ? 1 : 0.55;
            var column = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            var icon = Icons.Make("folder-bold", 22, Chrome.Accent) as FrameworkElement;
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
                tile.MouseLeftButtonUp += delegate
                {
                    _shell.OpenFile(path);
                    // OpenFile a échoué (fichier illisible) : l'accueil reste.
                    if (_shell.HasProjectPath) Release();
                };
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
