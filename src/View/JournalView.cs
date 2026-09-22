using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Le « Journal perso » : les statistiques d'écriture du projet
    /// (mots nets du jour, 7 jours, 30 jours, série, total), un graphique des
    /// quatorze derniers jours et le réglage de l'objectif journalier. Les
    /// données viennent de Project.Journal, nourries par MainWindow au fil des
    /// éditions. Deux onglets au style du ruban (14/09) : « Général » (les
    /// chiffres, le graphique, l'objectif) et « Succès » (la liste des
    /// succès) — chacun défile pour son compte, l'en-tête et la barre
    /// d'onglets restent en place.</summary>
    public class JournalView : DockPanel
    {
        /// <summary>La largeur de la colonne de lecture — l'en-tête, la barre
        /// d'onglets et les pages s'y alignent.</summary>
        private const double ColumnWidth = 680;
        private Project _project;
        private bool _loading;

        private TextBlock _dateLabel, _todayNumber, _todayCaption;
        private Border _progressTrack, _progressFill, _goalChip;
        private TextBlock _tile7, _tile30, _tileStreak, _tileTotal;
        private Canvas _chart;
        private int _chartDays = 14;             // 14, 30 ou 90 (b48)
        private readonly List<ToggleButton> _rangeChips = new List<ToggleButton>();
        private StackPanel _sprintsPanel;        // les derniers sprints (b48)
        private SpinnerField _goalField;
        private double _progressRatio;

        /// <summary>L'objectif journalier a changé (le projet est sale).</summary>
        public event Action Changed;
        /// <summary>« Démarrer un sprint » depuis la carte Sprints (14/09).</summary>
        public event Action SprintRequested;

        public JournalView()
        {
            Background = Chrome.WindowBg;
            LastChildFill = true;

            // ---- entête (hors onglets, ne défile pas) ----
            // L'en-tête et la barre d'onglets laissent à droite la place de
            // l'ascenseur (toujours réservé dans les pages) : les trois
            // colonnes tombent au même endroit.
            var head = new StackPanel
            {
                MaxWidth = ColumnWidth,
                Margin = new Thickness(36, 28, 36 + SystemParameters.VerticalScrollBarWidth, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            SetDock(head, Dock.Top);
            Children.Add(head);
            var page = head;

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("book-open-text-bold", 22, Chrome.Accent) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 10, 0);
                header.Children.Add(icon);
            }
            header.Children.Add(new TextBlock
            {
                Text = "Journal perso",
                Foreground = Chrome.Ink,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            page.Children.Add(header);

            _dateLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 10)
            };
            page.Children.Add(_dateLabel);

            // ---- les onglets « Général » / « Succès » (14/09) ----
            // Le TabControl prend le reste ; sa barre d'onglets (le style
            // chip du ruban, posé par Theme sur tout TabItem) est alignée
            // sur la colonne de lecture, chaque page défile pour elle-même.
            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Template = ColumnTabTemplate()
            };
            Children.Add(tabs);

            page = Page();
            tabs.Items.Add(new TabItem { Header = "Général", Content = Scroll(page) });

            // ---- aujourd'hui (héros) ----
            var todayCard = Card();
            var today = new StackPanel();
            today.Children.Add(new TextBlock
            {
                Text = "Aujourd'hui",
                Foreground = Chrome.SoftText,
                FontSize = 12
            });
            var heroRow = new StackPanel { Orientation = Orientation.Horizontal };
            _todayNumber = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 40,
                FontWeight = FontWeights.SemiBold
            };
            heroRow.Children.Add(_todayNumber);
            heroRow.Children.Add(new TextBlock
            {
                Text = "mots",
                Foreground = Chrome.SoftText,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 8)
            });
            _goalChip = new Border
            {
                Background = Chrome.Accent,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 3, 9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 6, 0, 0),
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "Objectif atteint ✓",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
            heroRow.Children.Add(_goalChip);
            today.Children.Add(heroRow);

            _progressTrack = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Chrome.Border,
                Margin = new Thickness(0, 8, 0, 0),
                Child = _progressFill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = Chrome.Accent,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0
                }
            };
            _progressTrack.SizeChanged += delegate { UpdateProgressFill(); };
            today.Children.Add(_progressTrack);
            _todayCaption = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            };
            today.Children.Add(_todayCaption);
            todayCard.Child = today;
            page.Children.Add(todayCard);

            // ---- tuiles ----
            var tiles = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(-4, 10, -4, 0) };
            tiles.Children.Add(Tile("7 derniers jours", out _tile7));
            tiles.Children.Add(Tile("30 derniers jours", out _tile30));
            tiles.Children.Add(Tile("Série", out _tileStreak));
            tiles.Children.Add(Tile("Total consigné", out _tileTotal));
            page.Children.Add(tiles);

            // ---- graphique 14 jours ----
            var chartCard = Card();
            chartCard.Margin = new Thickness(0, 10, 0, 0);
            var chartPanel = new StackPanel();
            // L'en-tête du graphique (b48) : le titre, et à droite trois chips
            // 14 / 30 / 90 jours — la même courbe, plus loin en arrière.
            var chartHead = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var days in new[] { 14, 30, 90 })
            {
                var daysRef = days;
                var chip = new ToggleButton
                {
                    Content = days + " j",
                    FontSize = 11,
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    IsChecked = days == _chartDays,
                    Focusable = false,
                    ToolTip = "Les " + days + " derniers jours"
                };
                chip.Checked += delegate
                {
                    _chartDays = daysRef;
                    foreach (var other in _rangeChips) if (other != chip && other.IsChecked == true) other.IsChecked = false;
                    DrawChart();
                };
                chip.Unchecked += delegate { if (_chartDays == daysRef) chip.IsChecked = true; }; // toujours un chip actif
                _rangeChips.Add(chip);
                chips.Children.Add(chip);
            }
            DockPanel.SetDock(chips, Dock.Right);
            chartHead.Children.Add(chips);
            chartHead.Children.Add(new TextBlock
            {
                Text = "Derniers jours — mots nets par jour, la moyenne en pointillé",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            chartPanel.Children.Add(chartHead);
            _chart = new Canvas { Height = 130, ClipToBounds = false };
            _chart.SizeChanged += delegate { DrawChart(); };
            chartPanel.Children.Add(_chart);
            chartCard.Child = chartPanel;
            page.Children.Add(chartCard);

            // ---- objectif ----
            var goalCard = Card();
            goalCard.Margin = new Thickness(0, 10, 0, 0);
            var goal = new StackPanel();
            goal.Children.Add(new TextBlock
            {
                Text = "Mots journaliers à écrire",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold
            });
            var goalRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _goalField = new SpinnerField(0, 0, 20000, 50,
                "Objectif de mots par jour — 0 pour désactiver");
            _goalField.ValueChanged += delegate(double value)
            {
                if (_loading || _project == null) return;
                _project.Journal.DailyGoal = (int)value;
                Refresh();
                var handler = Changed;
                if (handler != null) handler();
            };
            goalRow.Children.Add(_goalField);
            goalRow.Children.Add(new TextBlock
            {
                Text = "mots par jour (0 = désactivé)",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            goal.Children.Add(goalRow);
            goal.Children.Add(new TextBlock
            {
                Text = "Seuls les mots réellement tapés comptent : les imports et "
                    + "déplacements n'alimentent jamais le journal.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            goalCard.Child = goal;
            page.Children.Add(goalCard);

            // ---- sprints (b48) ----
            var sprintsCard = Card();
            sprintsCard.Margin = new Thickness(0, 10, 0, 0);
            var sprints = new StackPanel();
            var sprintsHead = new DockPanel();
            var startSprint = Buttons.IconText("play-fill", "Démarrer un sprint",
                "Une durée, un objectif de mots — la pastille suit, le journal consigne", Buttons.Compact, Buttons.Look.Primary);
            startSprint.Click += delegate { var h = SprintRequested; if (h != null) h(); };
            DockPanel.SetDock(startSprint, Dock.Right);
            sprintsHead.Children.Add(startSprint);
            sprintsHead.Children.Add(new TextBlock
            {
                Text = "Sprints",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            sprints.Children.Add(sprintsHead);
            sprints.Children.Add(new TextBlock
            {
                Text = "Les derniers sprints — Édition › Lancer un sprint… : une durée, un objectif de mots, un bandeau discret.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            });
            _sprintsPanel = new StackPanel();
            sprints.Children.Add(_sprintsPanel);
            sprintsCard.Child = sprints;
            page.Children.Add(sprintsCard);

            // ---- succès (12/09/2026), dans leur propre onglet (14/09) ----
            page = Page();
            tabs.Items.Add(new TabItem { Header = "Succès", Content = Scroll(page) });
            var achievementsCard = Card();
            var achievements = new StackPanel();
            var achievementsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            _achievementsCount = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_achievementsCount, Dock.Right);
            achievementsHeader.Children.Add(_achievementsCount);
            achievementsHeader.Children.Add(new TextBlock
            {
                Text = "Succès",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            achievements.Children.Add(achievementsHeader);
            achievements.Children.Add(new TextBlock
            {
                Text = "Ils se gagnent une fois pour toutes, sur tous vos projets. "
                    + "Les boutons « Obtenir / Retirer » sont là pour le développement.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            _achievementsPanel = new StackPanel();
            achievements.Children.Add(_achievementsPanel);
            achievementsCard.Child = achievements;
            page.Children.Add(achievementsCard);
        }

        // ---------------------------------------------------- mise en page

        /// <summary>Une page d'onglet : la colonne de lecture — étirée puis
        /// bornée, donc centrée et toujours pleine (elle ne se cale plus sur
        /// son texte le plus long).</summary>
        private static StackPanel Page()
        {
            return new StackPanel
            {
                MaxWidth = ColumnWidth,
                Margin = new Thickness(36, 6, 36, 36),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        /// <summary>L'ascenseur est toujours réservé : la colonne ne bouge pas
        /// d'un onglet à l'autre et reste alignée sur l'en-tête.</summary>
        private static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                Content = content
            };
        }

        /// <summary>Le gabarit du TabControl : la barre d'onglets en haut,
        /// limitée à la colonne de lecture (étirée puis bornée par MaxWidth,
        /// WPF la centre — comme les pages), le contenu sélectionné dessous
        /// sur toute la largeur. Les onglets eux-mêmes gardent le style chip
        /// du ruban posé par Theme.</summary>
        private static ControlTemplate ColumnTabTemplate()
        {
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(LastChildFillProperty, true);

            var strip = new FrameworkElementFactory(typeof(TabPanel));
            strip.SetValue(Panel.IsItemsHostProperty, true);
            strip.SetValue(DockProperty, Dock.Top);
            strip.SetValue(MaxWidthProperty, ColumnWidth);
            strip.SetValue(MarginProperty, new Thickness(33, 0, 36 + SystemParameters.VerticalScrollBarWidth, 4));
            strip.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            root.AppendChild(strip);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
            root.AppendChild(content);

            return new ControlTemplate(typeof(TabControl)) { VisualTree = root };
        }

        // --------------------------------------------------------- succès

        private StackPanel _achievementsPanel;
        private TextBlock _achievementsCount;

        /// <summary>Bouton de développement « Obtenir / Retirer » : (id, obtenir).</summary>
        public event Action<string, bool> AchievementToggleRequested;

        /// <summary>Redessine la liste depuis les réglages globaux : grisés
        /// tant que verrouillés, en couleurs (avec la date) une fois obtenus.</summary>
        public void RefreshAchievements()
        {
            if (_achievementsPanel == null) return;
            _achievementsPanel.Children.Clear();
            var unlocked = Settings.AppSettings.Achievements;
            _achievementsCount.Text = Achievements.CountKnown(unlocked.Keys) + " / " + Achievements.All.Length;
            foreach (var achievement in Achievements.All)
            {
                string date;
                var earned = unlocked.TryGetValue(achievement.Id, out date);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var badge = AchievementBadge.Build(achievement, earned, 44);
                badge.Margin = new Thickness(0, 0, 12, 0);
                badge.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var idRef = achievement.Id;
                var toggle = Buttons.Text(earned ? "Retirer" : "Obtenir",
                    earned ? "Développement : reverrouiller ce succès" : "Développement : débloquer ce succès (avec l'animation)",
                    Buttons.Compact, Buttons.Look.Outline);
                toggle.Margin = new Thickness(12, 0, 0, 0);
                var earnedRef = earned;
                toggle.Click += delegate
                {
                    var handler = AchievementToggleRequested;
                    if (handler != null) handler(idRef, !earnedRef);
                };
                buttons.Children.Add(toggle);
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = achievement.Name,
                    Foreground = earned ? Chrome.Ink : Chrome.SoftText,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                text.Children.Add(new TextBlock
                {
                    Text = achievement.Description,
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
                if (earned)
                    text.Children.Add(new TextBlock
                    {
                        Text = "Obtenu le " + Dates.Display(date),
                        Foreground = Chrome.Accent,
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                row.Children.Add(text);
                _achievementsPanel.Children.Add(row);
            }
        }

        private static Border Card()
        {
            return new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 14, 18, 14)
            };
        }

        private static UIElement Tile(string caption, out TextBlock value)
        {
            var card = Card();
            card.Margin = new Thickness(4, 0, 4, 0);
            var panel = new StackPanel();
            value = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            panel.Children.Add(value);
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            card.Child = panel;
            return card;
        }

        // ------------------------------------------------------------- data

        public void Load(Project project)
        {
            _project = project;
            Refresh();
        }

        public void Clear()
        {
            _project = null;
        }

        public bool ShowsJournal(Project project)
        {
            return ReferenceEquals(_project, project);
        }

        /// <summary>Recomputes every figure from the journal. Cheap — called at
        /// each word delta while the view is visible.</summary>
        public void Refresh()
        {
            if (_project == null) return;
            _loading = true;
            var journal = _project.Journal;
            var culture = CultureInfo.CurrentCulture;
            var today = journal.WordsOn(WritingJournal.Today());

            _dateLabel.Text = Cap(DateTime.Now.ToString("dddd d MMMM yyyy", culture));
            _todayNumber.Text = today.ToString("N0", culture);
            _tile7.Text = journal.WordsOverDays(7).ToString("N0", culture);
            _tile30.Text = journal.WordsOverDays(30).ToString("N0", culture);
            var streak = journal.Streak();
            _tileStreak.Text = streak + (streak > 1 ? " jours" : " jour");
            _tileTotal.Text = journal.TotalWords().ToString("N0", culture);

            if (journal.DailyGoal > 0)
            {
                _progressRatio = Math.Min(1.0, today / (double)journal.DailyGoal);
                _progressTrack.Visibility = Visibility.Visible;
                UpdateProgressFill();
                _todayCaption.Text = "Objectif : " + today.ToString("N0", culture)
                    + " / " + journal.DailyGoal.ToString("N0", culture) + " mots"
                    + (today >= journal.DailyGoal ? " — bravo le veau !" : "");
                _goalChip.Visibility = today >= journal.DailyGoal
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _progressRatio = 0;
                _progressTrack.Visibility = Visibility.Collapsed;
                _goalChip.Visibility = Visibility.Collapsed;
                _todayCaption.Text = "Aucun objectif journalier — fixez-en un ci-dessous.";
            }

            _goalField.Value = journal.DailyGoal;
            _loading = false;
            DrawChart();
            RebuildSprints();
            RefreshAchievements();
        }

        /// <summary>Les huit derniers sprints, le plus récent en tête (b48).</summary>
        private void RebuildSprints()
        {
            _sprintsPanel.Children.Clear();
            if (_project == null) return;
            var culture = CultureInfo.CurrentCulture;
            var last = _project.Journal.LastSprints(8);
            if (last.Count == 0)
            {
                _sprintsPanel.Children.Add(new TextBlock { Text = "Aucun sprint pour l'instant.", Foreground = Chrome.SoftText, FontSize = 11 });
                return;
            }
            foreach (var sprint in last)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var verdict = new TextBlock
                {
                    Text = sprint.Goal <= 0 ? "" : sprint.Reached ? "objectif atteint ✓" : "objectif " + sprint.Goal.ToString("N0", culture),
                    Foreground = sprint.Reached ? Chrome.Ok : Chrome.SoftText,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(verdict, Dock.Right);
                row.Children.Add(verdict);
                DateTime when;
                var date = DateTime.TryParseExact(sprint.Date, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)
                    ? when.ToString("dd/MM HH:mm", culture) : sprint.Date;
                row.Children.Add(new TextBlock
                {
                    Text = date + " — " + (sprint.Minutes > 0 ? sprint.Minutes + " min" : "libre, " + sprint.Elapsed + " min")
                        + " · " + sprint.Words.ToString("N0", culture) + (sprint.Words > 1 ? " mots" : " mot"),
                    Foreground = Chrome.Ink,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _sprintsPanel.Children.Add(row);
            }
        }

        private void UpdateProgressFill()
        {
            var track = _progressTrack.ActualWidth;
            _progressFill.Width = track > 2 ? Math.Max(0, (track - 2) * _progressRatio) : 0;
        }

        /// <summary>Le graphique : une barre par jour (14), l'accent plein pour
        /// aujourd'hui, atténué pour les jours passés ; jour vide = talon ;
        /// filet pointillé à l'objectif ; détail par barre en infobulle.</summary>
        private void DrawChart()
        {
            if (_project == null) return;
            _chart.Children.Clear();
            var width = _chart.ActualWidth;
            if (width < 60) return;
            var journal = _project.Journal;
            var culture = CultureInfo.CurrentCulture;

            var count = _chartDays;
            const double labelBand = 16;
            var plotHeight = _chart.Height - labelBand;

            var days = new int[count];
            var dates = new DateTime[count];
            var max = 0;
            for (var i = 0; i < count; i++)
            {
                dates[i] = DateTime.Now.Date.AddDays(i - (count - 1));
                days[i] = journal.WordsOn(dates[i].ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
                if (days[i] > max) max = days[i];
            }
            var scale = Math.Max(max, journal.DailyGoal > 0 ? journal.DailyGoal : 0);
            if (scale == 0) scale = 100; // page vide : talons seuls, échelle neutre

            var gap = count > 30 ? 1.0 : 2.0;
            var barWidth = Math.Max(2, Math.Min(30, (width - gap * (count - 1)) / count));
            var every = count <= 14 ? 1 : count <= 30 ? 5 : 15; // une étiquette de jour sur « every »
            var span = barWidth * count + gap * (count - 1);
            var left = (width - span) / 2;

            // Filet de l'objectif, sous les barres, discret.
            if (journal.DailyGoal > 0)
            {
                var y = plotHeight - plotHeight * journal.DailyGoal / scale;
                var line = new System.Windows.Shapes.Line
                {
                    X1 = left,
                    X2 = left + span,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Chrome.SoftText,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new double[] { 3, 3 })
                };
                _chart.Children.Add(line);
            }

            // La moyenne de la période (b48) : un pointillé accent, son chiffre à droite.
            var mean = journal.AverageOverDays(count);
            if (mean > 0)
            {
                var y = plotHeight - plotHeight * mean / scale;
                _chart.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = left, X2 = left + span, Y1 = y, Y2 = y,
                    Stroke = Chrome.Accent, StrokeThickness = 1, Opacity = 0.7,
                    StrokeDashArray = new DoubleCollection(new double[] { 2, 3 })
                });
                var meanLabel = new TextBlock
                {
                    Text = "moy. " + Math.Round(mean).ToString("N0", culture),
                    Foreground = Chrome.Accent,
                    FontSize = 9
                };
                Canvas.SetLeft(meanLabel, left + span + 4);
                Canvas.SetTop(meanLabel, y - 7);
                _chart.Children.Add(meanLabel);
            }

            for (var i = 0; i < count; i++)
            {
                var x = left + i * (barWidth + gap);
                var isToday = i == count - 1;
                var height = days[i] > 0
                    ? Math.Max(3, plotHeight * days[i] / scale)
                    : 2; // jour à zéro : talon, le jour reste présent
                var bar = new Border
                {
                    Width = barWidth,
                    Height = height,
                    Background = Chrome.Accent,
                    Opacity = isToday ? 1.0 : (days[i] > 0 ? 0.55 : 0.25),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    ToolTip = Cap(dates[i].ToString("ddd d MMM", culture)) + " — "
                        + days[i].ToString("N0", culture)
                        + (days[i] > 1 ? " mots" : " mot")
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, plotHeight - height);
                _chart.Children.Add(bar);

                if ((count - 1 - i) % every != 0) continue; // étiquettes espacées sur 30 et 90 jours
                var label = new TextBlock
                {
                    Text = count > 14 ? dates[i].ToString("d/M", culture) : dates[i].Day.ToString(),
                    Foreground = Chrome.SoftText,
                    FontSize = 9,
                    Width = Math.Max(barWidth, 26),
                    TextAlignment = TextAlignment.Center,
                    FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal
                };
                Canvas.SetLeft(label, x + barWidth / 2 - Math.Max(barWidth, 26) / 2);
                Canvas.SetTop(label, plotHeight + 3);
                _chart.Children.Add(label);
            }
        }

        private static string Cap(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}
