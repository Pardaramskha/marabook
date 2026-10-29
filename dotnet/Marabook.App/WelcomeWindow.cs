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
    /// principale quand elle bouge ou change de taille. Au pied, le statut de
    /// mise à jour (02/10) : « vérification… », « Vous êtes à jour » ou
    /// « Nouvelle version X disponible » — cliquable, vers les nouveautés.</summary>
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
        private TextBlock _updateText;
        private Border _updateDot;
        private TextBlock _betaText; // la seconde ligne (13/10) : la beta en cours
        private Border _betaDot;

        public WelcomeWindow(MainWindow shell) : this(shell, !Profiles.Chosen && !shell.IsolatedLaunch)
        {
        }

        /// <param name="profilePage">« Qui êtes-vous ? » à la place des projets :
        /// le premier lancement (jamais en sonde ni en capture — la sonde le demande elle-même).</param>
        public WelcomeWindow(MainWindow shell, bool profilePage)
        {
            _shell = shell;
            Title = AppInfo.Name;
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            // Au premier lancement (1.1.0), l'accueil montre « Qui êtes-vous ? »
            // à la place des projets ; « Commencer » le rend aux projets.
            _paper = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = profilePage ? BuildProfilePage() : Build()
            };
            Content = _paper;
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
            Action refresh = RefreshUpdateStatus;
            shell.UpdateStateChanged += refresh;
            Closed += delegate { shell.UpdateStateChanged -= refresh; };
            RefreshUpdateStatus();
        }

        /// <summary>Le statut de mise à jour du pied (02/10), relu à chaque pas
        /// de la coquille : le verdict de GitHub, cliquable quand il y a plus
        /// récent (la fenêtre des nouveautés : nom de la version, patch notes,
        /// installer). Ce que dit la vérification en cas d'échec (pas de
        /// réseau…) s'affiche tel quel, sans bruit.</summary>
        private void RefreshUpdateStatus()
        {
            if (_updateText == null) return;
            var check = _shell.LastUpdateCheck;
            var available = check != null && check.Available && check.Latest != null;
            string text;
            IBrush color = Chrome.FaintText;
            IBrush dot = Chrome.Border;
            if (available)
            {
                text = "Nouvelle version " + check.Latest.Version + " disponible"
                    + (_shell.UpdatePrepared ? ", prête à installer" : "") + " — voir les nouveautés";
                color = Chrome.Accent;
                dot = Chrome.Accent;
            }
            else if (_shell.UpdateCheckSkipped)
                text = "Mise à jour : non vérifiée dans ce mode de lancement";
            else if (check == null)
                text = _shell.UpdateCheckRunning ? "Mise à jour : vérification…" : "Mise à jour : vérification au lancement…";
            else if (check.Latest == null)
                text = "Mise à jour : " + check.Message;
            else
            {
                text = "Vous êtes à jour — Marabook " + AppInfo.Version;
                color = Chrome.SoftText;
                dot = Chrome.Ok;
            }
            _updateText.Text = text;
            _updateText.Foreground = color;
            _updateText.TextDecorations = available ? TextDecorations.Underline : null;
            _updateText.Cursor = new Cursor(available ? StandardCursorType.Hand : StandardCursorType.Arrow);
            _updateDot.Background = dot;
            ToolTip.SetTip(_updateText, available
                ? "Le nom de la nouvelle version et ses nouveautés — et le bouton pour l'installer"
                : null);
            RefreshBetaStatus();
        }

        /// <summary>La seconde ligne (13/10) : la beta en cours — cliquable
        /// quand elle est téléchargeable pour ce système (la fenêtre d'avertissement
        /// et des notes, « Télécharger la beta et redémarrer »). Sinon ce que
        /// dit la vérification, en retrait ; jamais rien en lancement isolé.</summary>
        private void RefreshBetaStatus()
        {
            if (_betaText == null) return;
            var check = _shell.LastBetaCheck;
            var available = check != null && check.Available && check.Latest != null;
            string text;
            IBrush color = Chrome.FaintText;
            IBrush dot = Chrome.Border;
            if (available)
            {
                text = check.Message + " — voir l'avertissement et les nouveautés";
                color = Chrome.Warn;
                dot = Chrome.Warn;
            }
            else if (_shell.UpdateCheckSkipped)
                text = "Beta : non vérifiée dans ce mode de lancement";
            else if (check == null)
                text = _shell.LastUpdateCheck == null ? "Beta : vérification au lancement…" : "Beta : vérification…";
            else
                text = "Beta : " + char.ToLowerInvariant(check.Message[0]) + check.Message.Substring(1);
            _betaText.Text = text;
            _betaText.Foreground = color;
            _betaText.TextDecorations = available ? TextDecorations.Underline : null;
            _betaText.Cursor = new Cursor(available ? StandardCursorType.Hand : StandardCursorType.Arrow);
            _betaDot.Background = dot;
            ToolTip.SetTip(_betaText, available
                ? "Une version d'essai, réservée aux tests : Windows, Mac Apple Silicon et Linux — l'avertissement, les notes et le bouton pour la télécharger"
                : "Les versions beta accompagnent les gros patchs ; elles disparaissent quand la version stable sort");
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
            // Le coin haut-gauche de l'accueil, calculé dans la zone client de
            // la coquille (unités logiques) et converti par PointToScreen dans
            // l'unité des positions d'écran DU SYSTÈME : pixels physiques sur
            // Windows, points sur macOS — où multiplier soi-même par
            // RenderScaling (2 sur Retina) poussait l'accueil hors de l'écran,
            // en bas à droite (1.0.3-patch-b).
            var offset = new Point((shellWidth - Width) / 2, (shellHeight - Height) / 2);
            try { Position = _shell.PointToScreen(offset); }
            catch
            {
                var scale = _shell.DesktopScaling;
                Position = new PixelPoint(
                    _shell.Position.X + (int)Math.Round(offset.X * scale),
                    _shell.Position.Y + (int)Math.Round(offset.Y * scale));
            }
        }

        private Border _paper;

        /// <summary>Sonde : l'accueil montre le choix du profil (premier lancement).</summary>
        public bool ShowsProfileChoice { get { return _profileChooser != null; } }
        private ProfileChooser _profileChooser;

        /// <summary>La page du premier lancement (1.1.0) : le même en-tête, puis le
        /// sélecteur de profil avec le nom de plume, « Commencer » au pied. Pour
        /// « Moi-même », la fenêtre des fonctionnalités s'ouvre ensuite, puis
        /// l'accueil reprend ses projets.</summary>
        private Control BuildProfilePage()
        {
            var root = new DockPanel { Margin = new Thickness(40, 32, 40, 28) };
            root.Children.Add(Head());

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var start = Buttons.Text("Commencer", "Garder ce profil et passer aux projets", Buttons.Bar, Buttons.Look.Primary);
            start.MinWidth = 120;
            start.IsEnabled = false;
            start.IsDefault = true;
            start.Tag = "profile-start"; // sonde
            buttons.Children.Add(start);
            root.Children.Add(buttons);

            _profileChooser = new ProfileChooser(false) { Margin = new Thickness(0, 18, 0, 0) };
            _profileChooser.SelectionChanged += delegate { start.IsEnabled = _profileChooser.Selected != null; };
            start.Click += async delegate
            {
                if (_profileChooser == null || _profileChooser.Selected == null) return;
                _profileChooser.CommitPenName();
                var custom = _profileChooser.Selected == Profiles.Custom;
                _profileChooser = null;
                if (custom) await FeaturesWindow.Ask(this);
                _paper.Child = Build();
                RefreshUpdateStatus();
            };
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _profileChooser
            });
            return root;
        }

        /// <summary>L'en-tête de l'accueil : le nom de l'application, la version.</summary>
        private static StackPanel Head()
        {
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
            return head;
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

            // ---- en pied : le statut de mise à jour (02/10), rempli par
            // RefreshUpdateStatus — et dessous la ligne de la beta (13/10)
            var footRows = new StackPanel();
            DockPanel.SetDock(footRows, Dock.Bottom);
            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            _updateDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Chrome.Border,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 8, 0)
            };
            foot.Children.Add(_updateDot);
            _updateText = new TextBlock
            {
                FontSize = 12,
                Foreground = Chrome.FaintText,
                VerticalAlignment = VerticalAlignment.Center
            };
            _updateText.PointerReleased += delegate
            {
                var check = _shell.LastUpdateCheck;
                if (check != null && check.Available) _shell.ShowUpdateNotes(this);
            };
            foot.Children.Add(_updateText);
            footRows.Children.Add(foot);
            var betaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            _betaDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Chrome.Border,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 8, 0)
            };
            betaRow.Children.Add(_betaDot);
            _betaText = new TextBlock
            {
                FontSize = 12,
                Foreground = Chrome.FaintText,
                VerticalAlignment = VerticalAlignment.Center
            };
            _betaText.PointerReleased += delegate
            {
                var check = _shell.LastBetaCheck;
                if (check != null && check.Available) _shell.ShowBetaNotes(this);
            };
            betaRow.Children.Add(_betaText);
            footRows.Children.Add(betaRow);
            root.Children.Add(footRows);

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
            // Le tutoriel (1.1.0, 29/10, Rémi) : au-dessus des projets, un
            // projet d'exemple temporaire et la visite guidée.
            var tutorialRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 14) };
            var tutorial = Buttons.IconText("play-fill", "Démarrer le tutoriel",
                "Ouvre un projet d'exemple temporaire et vous guide à travers les grandes fonctionnalités de Marabook (le succès « Tour du proprio » à la clé)",
                Buttons.Bar, Buttons.Look.Primary);
            tutorial.Tag = "welcome-tutorial"; // sonde
            tutorial.Click += delegate
            {
                if (_opening) return;
                if (_shell.StartTutorial()) Release();
            };
            tutorialRow.Children.Add(tutorial);
            tutorialRow.Children.Add(new TextBlock
            {
                Text = "Dix minutes pour faire le tour du propriétaire, sur un projet d'exemple.",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });
            center.Children.Add(tutorialRow);
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
            // Fond de CHAMP (blanc en clair, carte en sombre), pas de papier :
            // en sombre avec « papier blanc », PaperBg reste blanc alors que
            // l'encre (Ink) devient claire — les titres des tuiles
            // disparaissaient (1.0.3).
            var tile = new Border
            {
                Background = Chrome.FieldBg,
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
