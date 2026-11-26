using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
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
        private TabControl _tabs; // Général / Succès — le tutoriel y navigue (29/10)

        /// <summary>Tutoriel (29/10) : l'onglet Général (0) ou Succès (1).</summary>
        internal void SelectTabForTutorial(int index) { if (_tabs != null && index >= 0 && index < _tabs.Items.Count) _tabs.SelectedIndex = index; }
        internal int SelectedTabForProbe { get { return _tabs == null ? -1 : _tabs.SelectedIndex; } }
        private int _chartDays = 14;             // 14, 30 ou 90 (b48)
        private readonly List<ToggleButton> _rangeChips = new List<ToggleButton>();
        private StackPanel _sprintsPanel;        // les derniers sprints (b48)
        private SpinnerField _goalField;
        private double _progressRatio;

        /// <summary>L'objectif journalier a changé (le projet est sale).</summary>
        public event Action Changed;
        /// <summary>« Démarrer un sprint » depuis la carte Sprints (14/09).</summary>
        public event Action SprintRequested;
        public event Action<string> SecretAchievementClicked; // la ligne d'un succès secret cliquée (31/10)

        public JournalView()
        {
            Skin.Ground(this); // Chrome.WindowBg, ou transparent sur le fond d'un skin (21/11)
            LastChildFill = true;
            Build();
        }

        /// <summary>« Ascension », lot 2 (25/11 au soir) : la vue est bâtie pour le skin en cours — ses
        /// boîtes changent d'allure sous « Ascension » (cadres enluminés, cartels, nimbe, losanges). La
        /// coquille la garde d'une ouverture à l'autre : si le skin a changé entre-temps, on la rebâtit.</summary>
        private bool _ascent;
        private string _look = "";
        internal bool BuiltForAscension { get { return _ascent; } }

        private void EnsureSkin()
        {
            // Sous « Ascension », ses boîtes figent des teintes à leur fabrication (fond translucide, ors) :
            // le thème ou le métal qui change la fait rebâtir aussi.
            var look = AscensionArt.On ? (Chrome.Dark ? "sombre" : "clair") + Chrome.Hex(Chrome.Accent.Color) : "";
            if (_ascent == AscensionArt.On && _look == look) return;
            _look = look;
            var tab = _tabs == null ? 0 : _tabs.SelectedIndex;
            Children.Clear();
            _rangeChips.Clear();
            _nimbe = null;
            _streakHost = null;
            _sub7 = _sub30 = _subTotal = _legendGoal = _legendMean = _medalCount = null;
            _medalBar = null;
            Build();
            if (tab > 0) _tabs.SelectedIndex = tab;
        }

        private GoalRing _nimbe;          // « Ascension » : le chiffre du jour dans son nimbe
        private Border _streakHost;       // …la série en losanges
        private TextBlock _sub7, _sub30, _subTotal, _legendGoal, _legendMean, _medalCount;
        private Grid _medalBar;

        /// <summary>Un libellé de boîte sous « Ascension » : petites capitales d'or, espacées.</summary>
        private static TextBlock Caps(string text)
        {
            return new TextBlock
            {
                Text = (text ?? "").ToUpper(CultureInfo.CurrentCulture),
                Foreground = new SolidColorBrush(AscensionArt.Gold),
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 1.6,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Le titre d'une boîte : en capitales d'or sous « Ascension », le titre de toujours ailleurs.</summary>
        private TextBlock BoxTitle(string text)
        {
            if (_ascent) return Caps(text);
            return new TextBlock { Text = text, Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>Pose le contenu d'une boîte ; sous « Ascension », avec son cadre enluminé (coins, filet
        /// qui luit ; « riche » et à cimier pour la boîte du jour).</summary>
        private void Fill(Border card, Control content, bool framed, bool rich = false, bool crest = false)
        {
            if (!_ascent || !framed) { card.Child = content; return; }
            var layers = new Panel();
            layers.Children.Add(content);
            layers.Children.Add(IlluminatedFrame.Inside(card, rich, crest));
            card.BorderBrush = Brushes.Transparent;
            Skin.Illuminated(card);
            card.Child = layers;
            card.Tag = "journal-framed";
        }

        private void Build()
        {
            var ascent = _ascent = AscensionArt.On;
            _look = ascent ? (Chrome.Dark ? "sombre" : "clair") + Chrome.Hex(Chrome.Accent.Color) : "";

            // ---- entête (hors onglets, ne défile pas) ----
            // L'en-tête et la barre d'onglets laissent à droite la place de
            // l'ascenseur (toujours réservé dans les pages) : les trois
            // colonnes tombent au même endroit.
            var head = new StackPanel
            {
                MaxWidth = ColumnWidth,
                Margin = new Thickness(36, 28, 36 + 18.0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            SetDock(head, Dock.Top);
            Children.Add(head);
            var page = head;

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("book-open-text-bold", 22, Chrome.Accent) as Control;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 10, 0);
                header.Children.Add(icon);
            }
            var headTitle = new TextBlock
            {
                Text = "Journal perso",
                Foreground = Chrome.Ink,
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (ascent)
            {
                // Le titre dans la police des titres, en capitales.
                headTitle.Text = "JOURNAL PERSO";
                headTitle.FontFamily = Skin.TitleFont ?? FontFamily.Default;
                headTitle.FontWeight = FontWeight.Normal;
                headTitle.FontSize = 25;
                headTitle.LetterSpacing = 1.5;
            }
            header.Children.Add(headTitle);
            page.Children.Add(header);

            _dateLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 10)
            };
            page.Children.Add(_dateLabel);
            if (ascent) page.Children.Add(new SeparatorLine { Margin = new Thickness(0, -4, 0, 6) }); // le séparateur à volutes

            // ---- les onglets « Général » / « Succès » (14/09) ----
            // Le TabControl prend le reste ; sa barre d'onglets (le style
            // chip du ruban, posé par Theme sur tout TabItem) est alignée
            // sur la colonne de lecture, chaque page défile pour elle-même.
            var tabs = _tabs = new TabControl
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
                FontWeight = FontWeight.SemiBold
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
                IsVisible = false,
                Child = new TextBlock
                {
                    Text = "Objectif atteint ✓",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
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
            Ui.OnSizeChanged(_progressTrack, delegate { UpdateProgressFill(); });
            Skin.Meter(_progressFill, _progressTrack, delegate { return Chrome.Accent.Color; }); // dégradé fixe (25/11)
            today.Children.Add(_progressTrack);
            _todayCaption = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            };
            today.Children.Add(_todayCaption);
            if (ascent)
            {
                // « Ascension » : le chiffre du jour dans son NIMBE, la boîte dans un cadre riche à cimier.
                // (Le chiffre, la barre et leur rangée restent tenus à jour, hors de l'arbre.)
                heroRow.Children.Remove(_goalChip);
                today.Children.Remove(_todayCaption);
                _nimbe = new GoalRing { Width = 170, Height = 170, HorizontalAlignment = HorizontalAlignment.Center, Tag = "journal-nimbe" };
                var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var label = Caps("Aujourd'hui");
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Margin = new Thickness(0, 4, 0, 6);
                hero.Children.Add(label);
                hero.Children.Add(_nimbe);
                _todayCaption.FontSize = 13;
                _todayCaption.HorizontalAlignment = HorizontalAlignment.Center;
                _todayCaption.TextAlignment = TextAlignment.Center;
                _todayCaption.Margin = new Thickness(0, 8, 0, 0);
                hero.Children.Add(_todayCaption);
                _goalChip.Background = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.10));
                _goalChip.BorderBrush = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.6));
                _goalChip.BorderThickness = new Thickness(1);
                _goalChip.HorizontalAlignment = HorizontalAlignment.Center;
                _goalChip.Margin = new Thickness(0, 8, 0, 0);
                ((TextBlock)_goalChip.Child).Foreground = new SolidColorBrush(AscensionArt.GoldPale);
                hero.Children.Add(_goalChip);
                todayCard.Padding = new Thickness(18, 20, 18, 20);
                Fill(todayCard, hero, true, true, true);
            }
            else todayCard.Child = today;
            page.Children.Add(todayCard);

            // ---- tuiles ----
            var tiles = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(-4, 10, -4, 0) };
            if (ascent)
            {
                // Quatre CARTELS de musée : libellé en capitales d'or, chiffre dans la police des titres, une précision dessous.
                _streakHost = new Border { Margin = new Thickness(0, 5, 0, 1), HorizontalAlignment = HorizontalAlignment.Left };
                tiles.Children.Add(Cartel("7 derniers jours", out _tile7, _sub7 = SubLine()));
                tiles.Children.Add(Cartel("30 derniers jours", out _tile30, _sub30 = SubLine()));
                tiles.Children.Add(Cartel("Série", out _tileStreak, _streakHost));
                tiles.Children.Add(Cartel("Total consigné", out _tileTotal, _subTotal = SubLine()));
            }
            else
            {
            tiles.Children.Add(Tile("7 derniers jours", out _tile7));
            tiles.Children.Add(Tile("30 derniers jours", out _tile30));
            tiles.Children.Add(Tile("Série", out _tileStreak));
            tiles.Children.Add(Tile("Total consigné", out _tileTotal));
            }
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
                    Classes = { Marabook.App.Theme.Owned },
                    Content = days + " j",
                    FontSize = 11,
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    IsChecked = days == _chartDays,
                    Focusable = false,
                    [ToolTip.TipProperty] = "Les " + days + " derniers jours"
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
            if (ascent)
            {
                // La légende vit dans l'en-tête, jamais sur les barres.
                var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                legend.Children.Add(Caps("Derniers jours"));
                legend.Children.Add(new TextBlock { Text = "mots nets par jour", Foreground = Chrome.SoftText, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                _legendGoal = new TextBlock { Foreground = new SolidColorBrush(AscensionArt.Gold), FontSize = 11.5, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                _legendMean = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11.5, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                legend.Children.Add(_legendGoal);
                legend.Children.Add(_legendMean);
                chartHead.Children.Add(legend);
                chartHead.Margin = new Thickness(0, 2, 0, 14);
            }
            else
            chartHead.Children.Add(new TextBlock
            {
                Text = "Derniers jours — mots nets par jour, la moyenne en pointillé",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            chartPanel.Children.Add(chartHead);
            _chart = new Canvas { Height = ascent ? 170 : 130, ClipToBounds = false };
            Ui.OnSizeChanged(_chart, delegate { DrawChart(); });
            chartPanel.Children.Add(_chart);
            Fill(chartCard, chartPanel, true);
            page.Children.Add(chartCard);

            // ---- objectif ----
            var goalCard = Card();
            goalCard.Margin = new Thickness(0, 10, 0, 0);
            var goal = new StackPanel();
            goal.Children.Add(BoxTitle("Mots journaliers à écrire"));
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
            _sprintsCard = sprintsCard;
            sprintsCard.Margin = new Thickness(0, 10, 0, 0);
            sprintsCard.IsVisible = Profiles.Enabled(Profiles.Sprints);
            var sprints = new StackPanel();
            var sprintsHead = new DockPanel();
            var startSprint = Buttons.IconText("play-fill", "Démarrer un sprint",
                "Une durée, un objectif de mots", Buttons.Compact, Buttons.Look.Primary);
            startSprint.Classes.Add(AscensionArt.MagicClass); // ce qui LANCE : la magie bleue d'« Ascension »
            startSprint.Click += delegate { var h = SprintRequested; if (h != null) h(); };
            DockPanel.SetDock(startSprint, Dock.Right);
            sprintsHead.Children.Add(startSprint);
            sprintsHead.Children.Add(BoxTitle("Sprints"));
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
            if (ascent)
            {
                // Le MÉDAILLIER : le compte dans la police des titres, et une barre de magie.
                _achievementsCount.IsVisible = false;
                var cabinet = new DockPanel();
                var name = Caps("Médaillier");
                name.Margin = new Thickness(0, 0, 14, 0);
                DockPanel.SetDock(name, Dock.Left);
                cabinet.Children.Add(name);
                _medalCount = new TextBlock { Foreground = Chrome.Ink, FontFamily = Skin.TitleFont ?? FontFamily.Default, FontSize = 22, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(_medalCount, Dock.Left);
                cabinet.Children.Add(_medalCount);
                _medalBar = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Tag = "journal-medal-bar" };
                cabinet.Children.Add(_medalBar);
                achievementsHeader.Children.Add(cabinet);
            }
            else
            achievementsHeader.Children.Add(new TextBlock
            {
                Text = "Succès",
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            achievements.Children.Add(achievementsHeader);
            achievements.Children.Add(new TextBlock
            {
                Text = "Ils se gagnent une fois pour toutes, sur tous vos projets. "
                    + "Aide › Réinitialiser les succès pour tout reverrouiller.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            _achievementsPanel = new StackPanel();
            achievements.Children.Add(_achievementsPanel);
            Fill(achievementsCard, achievements, true);
            page.Children.Add(achievementsCard);
        }

        /// <summary>La précision d'un cartel (« 1 813 par jour », « depuis le 3 mars 2026 »).</summary>
        private static TextBlock SubLine()
        {
            return new TextBlock { Foreground = Chrome.SoftText, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        }

        /// <summary>Un CARTEL (« Ascension ») : un filet d'or pâle, une barre d'or qui s'efface à gauche, le
        /// libellé en capitales, le chiffre dans la police des titres, et une précision dessous.</summary>
        private static Control Cartel(string caption, out TextBlock value, Control below)
        {
            var card = Card();
            card.Margin = new Thickness(4, 0, 4, 0);
            card.Padding = new Thickness(14, 10, 10, 10);
            card.Tag = "journal-cartel";
            var panel = new StackPanel();
            var label = Caps(caption);
            label.FontSize = 9.5;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(label);
            value = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontFamily = Skin.TitleFont ?? FontFamily.Default,
                FontSize = 22,
                Margin = new Thickness(0, 3, 0, 0)
            };
            panel.Children.Add(value);
            if (below != null) panel.Children.Add(below);
            var bar = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            bar.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0), 0));
            bar.GradientStops.Add(new GradientStop(AscensionArt.Gold, 0.5));
            bar.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0), 1));
            var layers = new Panel();
            layers.Children.Add(new Border { Width = 2, Background = bar, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-14, 4, 0, 4), IsHitTestVisible = false });
            layers.Children.Add(panel);
            card.Child = layers;
            return card;
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
        private static ScrollViewer Scroll(Control content)
        {
            return new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Visible,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                Content = content
            };
        }

        /// <summary>Le gabarit du TabControl : la barre d'onglets en haut,
        /// limitée à la colonne de lecture (étirée puis bornée par MaxWidth,
        /// WPF la centre — comme les pages), le contenu sélectionné dessous
        /// sur toute la largeur. Les onglets eux-mêmes gardent le style chip
        /// du ruban posé par Theme.</summary>
        private static IControlTemplate ColumnTabTemplate()
        {
            return new FuncControlTemplate<TabControl>(delegate(TabControl tabs, INameScope scope)
            {
                var root = new DockPanel { LastChildFill = true };
                var strip = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    MaxWidth = ColumnWidth,
                    Margin = new Thickness(33, 0, 36 + 18, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                strip.RegisterInNameScope(scope);
                strip.Bind(ItemsPresenter.ItemsPanelProperty, new TemplateBinding(ItemsControl.ItemsPanelProperty), BindingPriority.Template);
                DockPanel.SetDock(strip, Dock.Top);
                root.Children.Add(strip);
                var content = new ContentPresenter { Name = "PART_SelectedContentHost" };
                content.RegisterInNameScope(scope);
                content.Bind(ContentPresenter.ContentProperty, new TemplateBinding(TabControl.SelectedContentProperty), BindingPriority.Template);
                content.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(TabControl.SelectedContentTemplateProperty), BindingPriority.Template);
                root.Children.Add(content);
                return root;
            });
        }

        // --------------------------------------------------------- succès

        private StackPanel _achievementsPanel;
        private TextBlock _achievementsCount;

        /// <summary>Série « Museum » (24/11) : un succès OBTENU est posé sur une plaque de métal
        /// (Skin.Plaque). Sous un autre skin, la ligne est rendue telle quelle.</summary>
        private static Control Earned(DockPanel row, bool earned)
        {
            if (AscensionArt.On)
            {
                // « Ascension », lot 2 : chaque succès dans sa boîte — filet d'or franc s'il est obtenu, pâle
                // sinon ; son nom en capitales dans la police des titres.
                var ground = Chrome.CardBg.Color;
                var outer = row.Margin;
                row.Margin = new Thickness(0);
                foreach (var child in row.Children)
                {
                    var texts = child as StackPanel;
                    var name = texts == null || texts.Children.Count == 0 ? null : texts.Children[0] as TextBlock;
                    if (name == null) continue;
                    name.Text = (name.Text ?? "").ToUpper(CultureInfo.CurrentCulture);
                    name.FontFamily = Skin.TitleFont ?? FontFamily.Default;
                    name.FontWeight = FontWeight.Normal;
                    name.FontSize = 14;
                    name.LetterSpacing = 0.6;
                    if (earned) name.Foreground = new SolidColorBrush(AscensionArt.GoldPale);
                }
                return new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(earned ? (byte)0xE6 : (byte)0x99, ground.R, ground.G, ground.B)),
                    BorderBrush = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, earned ? 0.55 : 0.22)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 12, 8),
                    Margin = outer,
                    Child = row,
                    Tag = earned ? "plaque" : "journal-locked"
                };
            }
            var plaque = earned ? Skin.Plaque() : null;
            if (plaque == null) return row;
            var margin = row.Margin;
            row.Margin = new Thickness(0);
            return new Border
            {
                Background = plaque,
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 10, 6),
                Margin = margin,
                Child = row,
                Tag = "plaque"
            };
        }

        /// <summary>Redessine la liste depuis les réglages globaux : grisés
        /// tant que verrouillés, en couleurs (avec la date) une fois obtenus.</summary>
        public void RefreshAchievements()
        {
            if (_achievementsPanel == null) return;
            _achievementsPanel.Children.Clear();
            var unlocked = Settings.AppSettings.Achievements;
            _achievementsCount.Text = Achievements.CountKnown(unlocked.Keys) + " / " + Achievements.All.Length;
            if (_medalCount != null)
            {
                var known = Achievements.CountKnown(unlocked.Keys);
                var all = Math.Max(1, Achievements.All.Length);
                _medalCount.Text = known + " / " + Achievements.All.Length;
                _medalBar.Children.Clear();
                _medalBar.ColumnDefinitions.Clear();
                var ratio = Math.Max(0, Math.Min(1, known / (double)all));
                _medalBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, ratio), GridUnitType.Star) });
                _medalBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star) });
                var track = new Border { Background = new SolidColorBrush(AscensionArt.WithAlpha(Chrome.Ink.Color, 0.12)), BorderBrush = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.35)), BorderThickness = new Thickness(1) };
                Grid.SetColumnSpan(track, 2);
                _medalBar.Children.Add(track);
                if (ratio > 0) _medalBar.Children.Add(new Border { Margin = new Thickness(1), Background = AscensionArt.MagicFill(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative)) });
            }
            // Les succès des DLC À PART (26/11/2026, Rémi) : sous ceux de Marabook, sous leur propre titre — comme
            // les secrets. Ils comptent toujours dans le total et pour les paliers.
            var ordered = new System.Collections.Generic.List<Achievement>();
            foreach (var achievement in Achievements.All) if (achievement.ModuleId == null) ordered.Add(achievement);
            var ownCount = ordered.Count;
            foreach (var achievement in Achievements.All) if (achievement.ModuleId != null) ordered.Add(achievement);
            for (var index = 0; index < ordered.Count; index++)
            {
                var achievement = ordered[index];
                if (index == ownCount)
                    _achievementsPanel.Children.Add(new TextBlock { Text = "Succès DLC", Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 8), Tag = "achievements-dlc" });
                string date;
                var earned = unlocked.TryGetValue(achievement.Id, out date);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var badge = AchievementBadge.Build(achievement, earned, 44);
                badge.Margin = new Thickness(0, 0, 12, 0);
                badge.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = achievement.Name,
                    Foreground = earned ? Chrome.Ink : Chrome.SoftText,
                    FontWeight = FontWeight.SemiBold,
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
                _achievementsPanel.Children.Add(Earned(row, earned));
            }
            // Les SUCCÈS SECRETS (31/10, Rémi) : sous les autres, le nom se voit,
            // la description reste cachée tant qu'ils ne sont pas obtenus ; la
            // ligne se clique (la fenêtre décide de ce qu'un clic fait).
            if (Achievements.Secrets.Length > 0)
            {
                _achievementsPanel.Children.Add(new TextBlock
                {
                    Text = "Succès secrets",
                    Foreground = Chrome.Ink,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 12, 0, 8),
                    Tag = "achievements-secrets"
                });
                foreach (var secret in Achievements.Secrets)
                {
                    string date;
                    var earned = unlocked.TryGetValue(secret.Id, out date);
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Tag = "secret:" + secret.Id };
                    var badge = AchievementBadge.Build(secret, earned, 44);
                    badge.Margin = new Thickness(0, 0, 12, 0);
                    badge.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(badge, Dock.Left);
                    row.Children.Add(badge);
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    text.Children.Add(new TextBlock { Text = secret.Name, Foreground = earned ? Chrome.Ink : Chrome.SoftText, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                    text.Children.Add(new TextBlock
                    {
                        Text = earned ? secret.Description : "???", // (1/11, Rémi)
                        Foreground = earned ? Chrome.SoftText : Chrome.FaintText,
                        FontStyle = earned ? FontStyle.Normal : FontStyle.Italic,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap
                    });
                    // Ce que le succès a débloqué (25/11) : à la ligne, en gras — une fois obtenu seulement.
                    if (earned && !string.IsNullOrEmpty(secret.Reward))
                        text.Children.Add(new TextBlock { Text = secret.Reward, Foreground = Chrome.Ink, FontWeight = FontWeight.Bold, FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "achievement-reward" });
                    if (earned)
                        text.Children.Add(new TextBlock { Text = "Obtenu le " + Dates.Display(date), Foreground = Chrome.Accent, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                    row.Children.Add(text);
                    var idRef = secret.Id;
                    row.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
                    {
                        if (e.InitialPressMouseButton != MouseButton.Left) return;
                        var handler = SecretAchievementClicked;
                        if (handler != null) handler(idRef);
                    };
                    _achievementsPanel.Children.Add(Earned(row, earned));
                }
            }
        }

        private static Border Card()
        {
            // « Ascension » : la boîte laisse deviner le ciel du planétarium, et son filet est d'or pâle.
            if (AscensionArt.On)
            {
                var ground = Chrome.CardBg.Color;
                return new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, ground.R, ground.G, ground.B)),
                    BorderBrush = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.35)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 14, 18, 14)
                };
            }
            return new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 14, 18, 14)
            };
        }

        private static Control Tile(string caption, out TextBlock value)
        {
            var card = Card();
            card.Margin = new Thickness(4, 0, 4, 0);
            var panel = new StackPanel();
            value = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 20,
                FontWeight = FontWeight.SemiBold
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
            EnsureSkin();
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
                _progressTrack.IsVisible = true;
                UpdateProgressFill();
                _todayCaption.Text = "Objectif : " + today.ToString("N0", culture)
                    + " / " + journal.DailyGoal.ToString("N0", culture) + " mots"
                    + (today >= journal.DailyGoal ? " — bravo le veau !" : "");
                _goalChip.IsVisible = today >= journal.DailyGoal
                    ? true : false;
            }
            else
            {
                _progressRatio = 0;
                _progressTrack.IsVisible = false;
                _goalChip.IsVisible = false;
                _todayCaption.Text = "Aucun objectif journalier — fixez-en un ci-dessous.";
            }

            if (_ascent)
            {
                // Le nimbe, les précisions des cartels, la série en losanges.
                var left = journal.DailyGoal - today;
                if (journal.DailyGoal > 0)
                    _todayCaption.Text = "Objectif " + journal.DailyGoal.ToString("N0", culture)
                        + (left > 0 ? " · encore " + left.ToString("N0", culture) + (left > 1 ? " mots" : " mot") : " · atteint — bravo le veau !");
                if (_nimbe != null) _nimbe.Show(journal.DailyGoal > 0 ? _progressRatio : 0, today.ToString("N0", culture), Math.Abs(today) > 1 ? "MOTS" : "MOT");
                if (_sub7 != null) _sub7.Text = Math.Round(journal.WordsOverDays(7) / 7.0).ToString("N0", culture) + " par jour";
                if (_sub30 != null) _sub30.Text = Math.Round(journal.WordsOverDays(30) / 30.0).ToString("N0", culture) + " par jour";
                if (_subTotal != null)
                {
                    string first = null;
                    foreach (var day in journal.Days)
                        if (day.Words != 0 && !string.IsNullOrEmpty(day.Date) && (first == null || string.CompareOrdinal(day.Date, first) < 0)) first = day.Date;
                    _subTotal.Text = first == null ? "rien de consigné" : "depuis le " + Dates.Display(first);
                }
                if (_streakHost != null)
                {
                    // Sept losanges : les six derniers jours tenus en or pâle, aujourd'hui en magie.
                    var held = new bool[7];
                    for (var i = 0; i < 7; i++)
                        held[i] = journal.WordsOn(DateTime.Now.Date.AddDays(i - 6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0;
                    _streakHost.Child = new DiamondRow(7, delegate(int i) { return held[i]; }, 9, 6);
                }
            }

            _goalField.Value = journal.DailyGoal;
            _loading = false;
            DrawChart();
            RebuildSprints();
            RefreshAchievements();
            ApplyProfile();
        }

        private Border _sprintsCard;

        /// <summary>Le profil (1.1.0) sans sprints : la carte Sprints se cache.</summary>
        public void ApplyProfile()
        {
            if (_sprintsCard != null) _sprintsCard.IsVisible = Profiles.Enabled(Profiles.Sprints);
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
            var track = _progressTrack.Bounds.Width;
            _progressFill.Width = track > 2 ? Math.Max(0, (track - 2) * _progressRatio) : 0;
        }

        /// <summary>Le graphique : une barre par jour (14), l'accent plein pour
        /// aujourd'hui, atténué pour les jours passés ; jour vide = talon ;
        /// filet pointillé à l'objectif ; détail par barre en infobulle.</summary>
        private void DrawChart()
        {
            if (_project == null) return;
            if (_ascent) { DrawChartAscent(); return; }
            _chart.Children.Clear();
            var width = _chart.Bounds.Width;
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
                var line = new Line
                {
                    StartPoint = new Point(left, y), EndPoint = new Point(left + span, y),
                    Stroke = Chrome.SoftText,
                    StrokeThickness = 1,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(new double[] { 3, 3 })
                };
                _chart.Children.Add(line);
            }

            // La moyenne de la période (b48) : un pointillé accent, son chiffre à droite.
            var mean = journal.AverageOverDays(count);
            if (mean > 0)
            {
                var y = plotHeight - plotHeight * mean / scale;
                _chart.Children.Add(new Line
                {
                    StartPoint = new Point(left, y), EndPoint = new Point(left + span, y),
                    Stroke = Chrome.Accent, StrokeThickness = 1, Opacity = 0.7,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(new double[] { 2, 3 })
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
                    Background = Skin.Museum ? Skin.MeterBrushUp(Chrome.Accent.Color, height, plotHeight) : Chrome.Accent, // dégradé fixe (25/11)
                    Opacity = isToday ? 1.0 : (days[i] > 0 ? (Skin.Museum ? 0.80 : 0.55) : 0.25),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    [ToolTip.TipProperty] = Cap(dates[i].ToString("ddd d MMM", culture)) + " — "
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
                    FontWeight = isToday ? FontWeight.SemiBold : FontWeight.Normal
                };
                Canvas.SetLeft(label, x + barWidth / 2 - Math.Max(barWidth, 26) / 2);
                Canvas.SetTop(label, plotHeight + 3);
                _chart.Children.Add(label);
            }
        }

        /// <summary>LE GRAPHIQUE SOUS « ASCENSION » (lot 2) : des COLONNES DORÉES à chapiteau — pleines
        /// au-dessus de l'objectif (un petit losange les coiffe), atténuées en dessous ; aujourd'hui en
        /// magie ; un losange creux au pied d'un jour à zéro ; la ligne d'objectif en filet d'or, un
        /// losange à chaque bout ; la moyenne en pointillé. À 90 jours : ni chapiteaux ni losanges.</summary>
        private void DrawChartAscent()
        {
            _chart.Children.Clear();
            var width = _chart.Bounds.Width;
            if (width < 60) return;
            var journal = _project.Journal;
            var culture = CultureInfo.CurrentCulture;
            var count = _chartDays;
            const double labelBand = 18, headRoom = 10;
            var plotHeight = _chart.Height - labelBand;
            var days = new int[count];
            var dates = new DateTime[count];
            var max = 0;
            for (var i = 0; i < count; i++)
            {
                dates[i] = DateTime.Now.Date.AddDays(i - (count - 1));
                days[i] = journal.WordsOn(dates[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                if (days[i] > max) max = days[i];
            }
            var goal = journal.DailyGoal;
            var scale = Math.Max(max, goal > 0 ? goal : 0);
            if (scale == 0) scale = 100;
            var reach = plotHeight - headRoom; // la place du losange au-dessus de la plus haute colonne
            var fine = count <= 30;
            var gap = count > 30 ? 2.0 : count > 14 ? 6.0 : 14.0;
            var barWidth = Math.Max(2, Math.Min(34, (width - 16 - gap * (count - 1)) / count));
            var every = count <= 14 ? 1 : count <= 30 ? 5 : 15;
            var span = barWidth * count + gap * (count - 1);
            var left = (width - span) / 2;
            Color gold = AscensionArt.Gold, pale = Chrome.Blend(AscensionArt.GoldPale, Colors.White, 0.4), soft = Chrome.Blend(AscensionArt.GoldPale, gold, 0.5), deep = Chrome.Blend(AscensionArt.GoldLo, Colors.Black, 0.45);
            var goldBrush = new SolidColorBrush(gold);
            var paleBrush = new SolidColorBrush(pale);
            Func<double, double, double, IBrush, IBrush, Control> diamond = delegate(double cx, double cy, double half, IBrush fill, IBrush stroke)
            {
                var shape = new Avalonia.Controls.Shapes.Path
                {
                    Data = new PolylineGeometry(new[] { new Point(0, half), new Point(half, 0), new Point(2 * half, half), new Point(half, 2 * half) }, true),
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = stroke == null ? 0 : 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(shape, cx - half);
                Canvas.SetTop(shape, cy - half);
                return shape;
            };

            // Deux lignes de repère, très pâles.
            foreach (var part in new[] { 0.5, 1.0 })
                _chart.Children.Add(new Line { StartPoint = new Point(left - 8, plotHeight - reach * part), EndPoint = new Point(left + span + 8, plotHeight - reach * part), Stroke = new SolidColorBrush(AscensionArt.WithAlpha(Chrome.Ink.Color, 0.06)), StrokeThickness = 1 });

            var gilt = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            gilt.GradientStops.Add(new GradientStop(pale, 0));
            gilt.GradientStops.Add(new GradientStop(soft, 0.22));
            gilt.GradientStops.Add(new GradientStop(gold, 0.55));
            gilt.GradientStops.Add(new GradientStop(deep, 1));
            var magic = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            magic.GradientStops.Add(new GradientStop(AscensionArt.MagicHi, 0));
            magic.GradientStops.Add(new GradientStop(AscensionArt.Magic, 0.18));
            magic.GradientStops.Add(new GradientStop(Chrome.Blend(AscensionArt.MagicDeep, Colors.Black, 0.35), 1));

            for (var i = 0; i < count; i++)
            {
                var x = left + i * (barWidth + gap);
                var isToday = i == count - 1;
                var tip = Cap(dates[i].ToString("ddd d MMM", culture)) + " — " + days[i].ToString("N0", culture) + (days[i] > 1 ? " mots" : " mot");
                if (days[i] > 0)
                {
                    var height = Math.Max(4, reach * days[i] / scale);
                    var met = goal > 0 && days[i] >= goal;
                    var bar = new Border
                    {
                        Width = barWidth,
                        Height = height,
                        Background = isToday ? magic : gilt,
                        BorderBrush = isToday ? new SolidColorBrush(AscensionArt.MagicHi) : paleBrush,
                        BorderThickness = new Thickness(0, fine ? 1.5 : 0, 0, 0),
                        Opacity = isToday || met || goal <= 0 ? 1.0 : 0.62,
                        Tag = isToday ? "journal-bar-today" : "journal-bar",
                        [ToolTip.TipProperty] = tip
                    };
                    Skin.KeepCorners(bar);
                    Canvas.SetLeft(bar, x);
                    Canvas.SetTop(bar, plotHeight - height);
                    _chart.Children.Add(bar);
                    if (met && fine) _chart.Children.Add(diamond(x + barWidth / 2, plotHeight - height - 7, 3.5, isToday ? new SolidColorBrush(AscensionArt.MagicHi) : paleBrush, null));
                }
                else if (fine) _chart.Children.Add(diamond(x + barWidth / 2, plotHeight - 5, 3.5, null, new SolidColorBrush(AscensionArt.WithAlpha(gold, 0.6))));

                if ((count - 1 - i) % every != 0) continue;
                var label = new TextBlock
                {
                    Text = count > 14 ? dates[i].ToString("d/M", culture) : Short(dates[i].ToString("ddd", culture)),
                    Foreground = isToday ? Chrome.Ink : Chrome.SoftText,
                    FontSize = 10,
                    Width = Math.Max(barWidth, 30),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, x + barWidth / 2 - Math.Max(barWidth, 30) / 2);
                Canvas.SetTop(label, plotHeight + 4);
                _chart.Children.Add(label);
            }

            // La moyenne, puis l'objectif par-dessus.
            var mean = journal.AverageOverDays(count);
            if (mean > 0)
            {
                var y = plotHeight - reach * mean / scale;
                _chart.Children.Add(new Line
                {
                    StartPoint = new Point(left - 8, y), EndPoint = new Point(left + span + 8, y),
                    Stroke = new SolidColorBrush(AscensionArt.WithAlpha(Chrome.Ink.Color, 0.55)), StrokeThickness = 1,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(new double[] { 2, 4 }), IsHitTestVisible = false
                });
            }
            if (goal > 0)
            {
                var y = plotHeight - reach * goal / scale;
                _chart.Children.Add(new Line { StartPoint = new Point(left - 8, y), EndPoint = new Point(left + span + 8, y), Stroke = goldBrush, StrokeThickness = 1.2, IsHitTestVisible = false, Tag = "journal-goal-line" });
                _chart.Children.Add(diamond(left - 8, y, 4.5, goldBrush, null));
                _chart.Children.Add(diamond(left + span + 8, y, 4.5, goldBrush, null));
            }
            if (_legendGoal != null)
            {
                _legendGoal.Text = goal > 0 ? "◆ objectif " + goal.ToString("N0", culture) : "";
                _legendGoal.IsVisible = goal > 0;
            }
            if (_legendMean != null)
            {
                _legendMean.Text = mean > 0 ? "· · ·  moyenne " + Math.Round(mean).ToString("N0", culture) : "";
                _legendMean.IsVisible = mean > 0;
            }
        }

        /// <summary>« lun. » → « lu » : les deux premières lettres du jour.</summary>
        private static string Short(string day)
        {
            day = (day ?? "").Trim().TrimEnd('.');
            return day.Length > 2 ? day.Substring(0, 2) : day;
        }

        private static string Cap(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}
