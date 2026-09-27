using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'écran d'accueil (porté de View/WelcomeWindow.cs) : à
    /// l'ouverture SANS projet, la fenêtre principale reste voilée et cette
    /// fenêtre, posée dessus, prend la majeure partie de la place. Titre,
    /// version, les cinq derniers projets en tuiles sur une ligne, la tuile
    /// pointillée « + », les DLC. Elle n'est PAS fermable : elle disparaît
    /// quand un projet s'ouvre, ou avec l'app (Release). Suit la fenêtre
    /// principale quand elle bouge ou change de taille. La vérification de
    /// mise à jour arrive avec la livraison (P4).</summary>
    public class WelcomeWindow : Window
    {
        private readonly MainWindow _shell;
        private bool _release;
        private bool _opening;
        private Control _loading;
        private TextBlock _loadingText;
        private Border _runner;
        private UniformGrid _tiles;
        private Button _openButton;
        private DispatcherTimer _sweep;

        public WelcomeWindow(MainWindow shell)
        {
            _shell = shell;
            Title = AppInfo.Name;
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Content = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = Build()
            };
            Closing += delegate(object sender, WindowClosingEventArgs e)
            {
                // Jamais fermée à la main (Alt+F4 sur elle) ; mais quand la
                // coquille se ferme ou l'application quitte, elle suit — un
                // enfant qui refuse bloquerait son propriétaire.
                if (!_release && e.CloseReason == WindowCloseReason.WindowClosing) e.Cancel = true;
            };
            Opened += delegate { Fit(); };
            shell.PositionChanged += delegate { Fit(); };
            shell.SizeChanged += delegate { Fit(); };
        }

        /// <summary>Un projet est ouvert (ou l'app se ferme) : l'accueil se
        /// retire — la seule façon de le fermer.</summary>
        private StackPanel _noticeHost;

        /// <summary>Un toast à boutons (NoticeToast) en bas à droite de l'accueil :
        /// l'hôte se glisse par-dessus le contenu à la première demande.</summary>
        public void ShowNotice(Border toast)
        {
            if (_noticeHost == null)
            {
                var content = Content as Control;
                Content = null;
                var grid = new Grid();
                if (content != null) grid.Children.Add(content);
                _noticeHost = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 24, 24)
                };
                grid.Children.Add(_noticeHost);
                Content = grid;
            }
            NoticeToast.Show(_noticeHost, toast);
        }

        public void Release()
        {
            if (_release) return;
            _release = true;
            if (_sweep != null) _sweep.Stop();
            try { Close(); }
            catch { }
        }

        /// <summary>80 % de la fenêtre principale, centrée dessus.</summary>
        private void Fit()
        {
            var shellWidth = _shell.ClientSize.Width;
            var shellHeight = _shell.ClientSize.Height;
            if (shellWidth <= 0 || shellHeight <= 0) return;
            if (_shell.WindowState == WindowState.Minimized) return;
            Width = Math.Max(640, shellWidth * 0.8);
            Height = Math.Max(420, shellHeight * 0.8);
            var scale = _shell.RenderScaling;
            var origin = _shell.Position;
            try
            {
                // Le coin haut-gauche de la zone client, en pixels physiques.
                var client = _shell.PointToScreen(new Point(0, 0));
                origin = client;
            }
            catch { }
            Position = new PixelPoint(
                origin.X + (int)Math.Round((shellWidth - Width) / 2 * scale),
                origin.Y + (int)Math.Round((shellHeight - Height) / 2 * scale));
        }

        private Control Build()
        {
            var root = new DockPanel { Margin = new Thickness(40, 32, 40, 28) };

            // ---- en tête : le titre, la version
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = AppInfo.Name + " - C'est parti pour nerder",
                FontSize = 26,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink
            });
            head.Children.Add(new TextBlock
            {
                Text = "Version " + AppInfo.Version + " · " + AppPlatform.OsName,
                FontSize = 13,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 4, 0, 0)
            });
            root.Children.Add(head);

            // ---- en pied : la mise à jour (P4)
            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(foot, Dock.Bottom);
            foot.Children.Add(new TextBlock
            {
                Text = "Mise à jour : la vérification arrive avec la livraison sur trois systèmes.",
                FontSize = 12,
                Foreground = Chrome.FaintText,
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(foot);

            // ---- au centre : les tuiles, sur UNE ligne
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6) };
            titleRow.Children.Add(new TextBlock
            {
                Text = "Derniers projets",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            });
            var open = Buttons.Text("Ouvrir un projet…", "Choisir un fichier .plot sur le disque", Buttons.Compact, Buttons.Look.Outline);
            open.Margin = new Thickness(12, 0, 0, 0);
            open.Click += async delegate
            {
                if (_opening) return;
                var chosen = await Ui.PickOpenFile(this, "Ouvrir un projet", Persistence.PlotFile.OpenFilter);
                if (chosen != null) BeginOpen(chosen);
            };
            _openButton = open;
            titleRow.Children.Add(open);
            center.Children.Add(titleRow);
            var tiles = new UniformGrid { Rows = 1, Columns = 6, Height = 150 };
            var shown = 0;
            foreach (var path in AppSettings.RecentFiles)
            {
                if (shown >= 5) break;
                tiles.Children.Add(RecentTile(path));
                shown++;
            }
            tiles.Children.Add(NewTile());
            for (var i = shown + 1; i < 6; i++)
                tiles.Children.Add(new Border { IsVisible = false });
            _tiles = tiles;
            center.Children.Add(tiles);
            center.Children.Add(BuildLoading());
            center.Children.Add(BuildModules());
            root.Children.Add(center);
            return root;
        }

        // ------------------------------------------------------- modules (DLC)

        /// <summary>Le bloc « DLC » sous les projets : une tuile par module du
        /// catalogue — nom, une ligne, installé ou non.</summary>
        private Control BuildModules()
        {
            var block = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6) };
            titleRow.Children.Add(new TextBlock
            {
                Text = "DLC",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "des modules qui ajoutent des fonctions à Marabook — gérés dans Préférences › DLC",
                FontSize = 11,
                Foreground = Chrome.FaintText,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            block.Children.Add(titleRow);
            var modules = new WrapPanel();
            foreach (var source in Modules.Catalogue)
            {
                var installed = Modules.Find(source.Id);
                var tile = Tile();
                tile.Width = 236;
                ToolTip.SetTip(tile, source.Title.Length > 0 ? source.Title : source.Name);
                var column = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
                var head = new DockPanel();
                var dot = new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius = new CornerRadius(4.5),
                    Background = installed != null ? Chrome.Accent : Chrome.Border,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 1, 0, 0)
                };
                ToolTip.SetTip(dot, installed != null ? "Installé" : "Non installé");
                DockPanel.SetDock(dot, Dock.Right);
                head.Children.Add(dot);
                head.Children.Add(new TextBlock
                {
                    Text = source.Name,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Chrome.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                column.Children.Add(head);
                column.Children.Add(new TextBlock
                {
                    Text = source.Features.Count > 0 ? source.Features[0] : source.Title,
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 30,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                column.Children.Add(new TextBlock
                {
                    Text = installed != null ? "Installé" + (installed.Version.Length > 0 ? " · " + installed.Version : "") : "Disponible avec la livraison",
                    FontSize = 11,
                    Foreground = installed != null ? Chrome.Accent : Chrome.FaintText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                tile.Child = column;
                modules.Children.Add(tile);
            }
            block.Children.Add(modules);
            return block;
        }

        // ------------------------------------------------------- chargement

        private Control BuildLoading()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 14, 0, 0),
                Opacity = 0 // la place est réservée, rien ne saute
            };
            var rail = new Canvas { Width = 120, Height = 4, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            rail.Children.Add(new Border { Width = 120, Height = 4, CornerRadius = new CornerRadius(2), Background = Chrome.Border });
            _runner = new Border { Width = 40, Height = 4, CornerRadius = new CornerRadius(2), Background = Chrome.Accent };
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

        /// <summary>Ouvre un projet en montrant l'attente ; la lecture se fait
        /// en fond (MainWindow.OpenFileInBackground). Réussite : l'accueil est
        /// relâché par la coquille ; échec : tout revient.</summary>
        private void BeginOpen(string path)
        {
            if (_opening) return;
            _opening = true;
            _tiles.IsEnabled = false;
            _openButton.IsEnabled = false;
            _loadingText.Text = "Ouverture de « " + System.IO.Path.GetFileNameWithoutExtension(path) + " »…";
            _loading.Opacity = 1;
            var phase = 0.0;
            _sweep = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _sweep.Tick += delegate
            {
                phase += 0.045;
                Canvas.SetLeft(_runner, 40 + 40 * Math.Sin(phase));
            };
            _sweep.Start();
            _shell.OpenFileInBackground(path, delegate
            {
                // Ouvert : la fenêtre a relâché l'accueil (InstallOpened). Sinon
                // l'accueil reprend la main.
                if (_release) return;
                _sweep.Stop();
                _loading.Opacity = 0;
                _tiles.IsEnabled = true;
                _openButton.IsEnabled = true;
                _opening = false;
            });
        }

        // ------------------------------------------------------- tuiles

        private Control RecentTile(string path)
        {
            var exists = File.Exists(path);
            var tile = Tile();
            tile.Cursor = new Cursor(exists ? StandardCursorType.Hand : StandardCursorType.Arrow);
            tile.Opacity = exists ? 1 : 0.55;
            var column = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            var icon = Icons.Make("book-bold", 22, Chrome.Accent);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(0, 0, 0, 8);
            column.Children.Add(icon);
            column.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(path),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
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
                Text = exists ? "Modifié le " + Dates.Display(File.GetLastWriteTime(path)) : "Fichier introuvable",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 2, 0, 0)
            });
            tile.Child = column;
            ToolTip.SetTip(tile, path);
            if (exists) tile.PointerReleased += delegate { BeginOpen(path); };
            return tile;
        }

        /// <summary>La tuile « + » : contour pointillé, elle crée un projet EN
        /// L'ENREGISTRANT — pas de projet fantôme.</summary>
        private Control NewTile()
        {
            var grid = new Grid { Margin = new Thickness(6), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            var dashes = new Rectangle
            {
                Stroke = Chrome.BorderStrong,
                StrokeThickness = 1.5,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                RadiusX = 8,
                RadiusY = 8
            };
            grid.Children.Add(dashes);
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var plus = Icons.Make("plus-bold", 30, Chrome.SoftText);
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.Margin = new Thickness(0, 0, 0, 8);
            column.Children.Add(plus);
            column.Children.Add(new TextBlock
            {
                Text = "Nouveau projet",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center
            });
            grid.Children.Add(column);
            ToolTip.SetTip(grid, "Créer un projet — il est enregistré tout de suite, là où vous le dites");
            grid.PointerEntered += delegate { dashes.Stroke = Chrome.Accent; };
            grid.PointerExited += delegate { dashes.Stroke = Chrome.BorderStrong; };
            grid.PointerReleased += async delegate
            {
                if (_opening) return;
                if (await _shell.NewProjectWithSaveDialog()) Release();
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
            tile.PointerEntered += delegate { tile.BorderBrush = Chrome.Accent; };
            tile.PointerExited += delegate { tile.BorderBrush = Chrome.Border; };
            return tile;
        }
    }
}
