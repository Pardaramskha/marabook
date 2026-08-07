using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Le « Journal perso » : les statistiques d'écriture du projet
    /// (mots nets du jour, 7 jours, 30 jours, série, total), un graphique des
    /// quatorze derniers jours et le réglage de l'objectif journalier. Les
    /// données viennent de Project.Journal, nourries par MainWindow au fil des
    /// éditions.</summary>
    public class JournalView : ScrollViewer
    {
        private Project _project;
        private bool _loading;

        private TextBlock _dateLabel, _todayNumber, _todayCaption;
        private Border _progressTrack, _progressFill, _goalChip;
        private TextBlock _tile7, _tile30, _tileStreak, _tileTotal;
        private Canvas _chart;
        private SpinnerField _goalField;
        private double _progressRatio;

        /// <summary>L'objectif journalier a changé (le projet est sale).</summary>
        public event Action Changed;

        public JournalView()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Background = Chrome.WindowBg;

            var page = new StackPanel
            {
                MaxWidth = 680,
                Margin = new Thickness(36, 28, 36, 36),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // ---- entête ----
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
                Margin = new Thickness(0, 4, 0, 18)
            };
            page.Children.Add(_dateLabel);

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
            chartPanel.Children.Add(new TextBlock
            {
                Text = "Quatorze derniers jours",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            });
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

            Content = page;
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

            const int count = 14;
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

            var gap = 2.0;
            var barWidth = Math.Min(30, (width - gap * (count - 1)) / count);
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

                var label = new TextBlock
                {
                    Text = dates[i].Day.ToString(),
                    Foreground = Chrome.SoftText,
                    FontSize = 9,
                    Width = barWidth,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal
                };
                Canvas.SetLeft(label, x);
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
