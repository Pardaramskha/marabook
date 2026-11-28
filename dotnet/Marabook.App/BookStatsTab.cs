using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Marabook.Correction;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'onglet « Statistiques » de la page livre (27/11, 2.0.0),
    /// entre Styles et Publication : mots, signes et pages de CHAQUE texte du
    /// livre — l'ancien accordéon de Publication (18/10), ici toujours
    /// déplié, il a son onglet. À côté de l'ordre, la bascule « Graphique »
    /// remplace la liste par la courbe des signes (BookStatsChart, à la
    /// manière du graphique d'intensité d'un plan) ; un texte s'en écarte
    /// par la croix sous son point, et y revient par sa pastille « Écartés ».
    /// Les pages sont comptées à l'affichage de l'onglet (Refresh).</summary>
    public class BookStatsTab : StackPanel
    {
        private BinderItem _item;
        private Project _project;
        private Func<BinderItem, int> _pageCounter;
        private readonly StackPanel _stats;       // un texte par ligne, surlignée au survol (24/10)
        private readonly ComboBox _statsOrder;    // l'ordre des lignes (29/10)
        private readonly ToggleButton _chartToggle;
        private readonly BookStatsChart _chart;
        private readonly WrapPanel _excluded;     // les textes écartés du graphique, à remettre d'un clic
        private readonly StackPanel _tools;
        private readonly TextBlock _empty;
        private readonly List<StatsEntry> _rows = new List<StatsEntry>(); // dans l'ordre des pages
        private static bool _chartMode;           // liste ou graphique : d'un livre à l'autre
        private bool _syncing;
        private static readonly string[] StatsOrders =
        {
            "Ordre d'apparition", "Mots, croissant", "Mots, décroissant",
            "Signes, croissant", "Signes, décroissant", "Pages, croissant", "Pages, décroissant"
        };

        /// <summary>Un texte écarté du graphique, ou remis : le livre a changé (le fichier est à enregistrer).</summary>
        public event Action Changed;

        public BookStatsTab()
        {
            Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 24);

            // L'ordre des lignes (29/10, Rémi) : ordre d'apparition (défaut),
            // ou mots / signes / pages croissants ou décroissants — et la
            // bascule « Graphique » à sa droite (27/11).
            _tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 8) };
            _tools.Children.Add(new TextBlock { Text = "Ordre", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _statsOrder = new ComboBox { Width = 190, Tag = "stats-order", VerticalAlignment = VerticalAlignment.Center };
            foreach (var label in StatsOrders) _statsOrder.Items.Add(label);
            _statsOrder.SelectedIndex = 0;
            _statsOrder.SelectionChanged += delegate { if (_item != null) Render(); };
            _tools.Children.Add(_statsOrder);
            _chartToggle = Buttons.IconTextToggle("chart-bars", "Graphique",
                "Remplace la liste par le graphique des signes de chaque texte, dans l'ordre choisi. "
                + "La croix sous un point écarte son texte du graphique (une page de titre, un entre-chapitre).",
                Buttons.Bar, Buttons.Look.Outline);
            _chartToggle.Margin = new Thickness(8, 0, 0, 0);
            _chartToggle.VerticalAlignment = VerticalAlignment.Center;
            _chartToggle.Tag = "stats-chart-toggle"; // sonde
            _chartToggle.IsChecked = _chartMode;
            _chartToggle.IsCheckedChanged += delegate
            {
                if (_syncing) return;
                _chartMode = _chartToggle.IsChecked == true;
                if (_item != null) Render();
            };
            _tools.Children.Add(_chartToggle);
            Children.Add(_tools);

            _stats = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Children.Add(_stats);

            _excluded = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6), IsVisible = false, Tag = "stats-excluded" };
            Children.Add(_excluded);
            _chart = new BookStatsChart { IsVisible = false };
            _chart.ExcludeRequested += delegate(BinderItem text) { SetExcluded(text, true); };
            Children.Add(_chart);

            _empty = new TextBlock { Text = "Aucun texte dans ce livre.", Foreground = Chrome.SoftText, Margin = new Thickness(6, 0, 0, 0), IsVisible = false };
            Children.Add(_empty);
        }

        public void Load(BinderItem book, Project project, Func<BinderItem, int> pageCounter)
        {
            _item = book;
            _project = project;
            _pageCounter = pageCounter;
            if (book != null && book.Book == null) book.Book = new BookInfo();
        }

        public void Clear()
        {
            _item = null;
            _rows.Clear();
            _chart.Show(null, null);
        }

        /// <summary>Recompte chaque texte (à l'affichage de l'onglet), puis affiche.</summary>
        public void Refresh()
        {
            _rows.Clear();
            if (_item == null || _item.Book == null) { Render(); return; }
            var ordered = new List<BinderItem>();
            BookFlow.Texts(_item, ordered);
            for (var i = 0; i < ordered.Count; i++)
            {
                var text = ordered[i];
                _rows.Add(new StatsEntry
                {
                    Text = text,
                    Order = i,
                    Stats = TextStats.Compute(text.Document.ToPlainText()),
                    Pages = _pageCounter == null ? -1 : _pageCounter(text)
                });
            }
            // Un texte écarté qui a quitté le livre s'oublie (sans marquer le fichier : rien de visible n'a changé).
            var texts = new List<BinderItem>();
            foreach (var row in _rows) texts.Add(row.Text);
            BookChart.Split(_item, texts, new List<BinderItem>(), new List<BinderItem>());
            Render();
        }

        /// <summary>La liste ou le graphique, dans l'ordre du sélecteur :
        /// apparition (l'ordre des pages), ou mots, signes, pages — croissant
        /// ou décroissant, à égalité l'ordre des pages. Ne recompte rien.</summary>
        private void Render()
        {
            _stats.Children.Clear();
            _excluded.Children.Clear();
            var any = _item != null && _rows.Count > 0;
            _tools.IsVisible = any;
            _empty.IsVisible = !any;
            _syncing = true;
            _chartToggle.IsChecked = _chartMode;
            _syncing = false;
            var chart = any && _chartMode;
            _stats.IsVisible = any && !chart;
            _chart.IsVisible = chart;
            _excluded.IsVisible = false;
            if (!any) { _chart.Show(null, null); return; }

            var rows = new List<StatsEntry>(_rows);
            var mode = Math.Max(0, _statsOrder.SelectedIndex);
            if (mode > 0)
                rows.Sort(delegate(StatsEntry a, StatsEntry b)
                {
                    var ka = StatsKey(a, mode);
                    var kb = StatsKey(b, mode);
                    var by = mode % 2 == 1 ? ka.CompareTo(kb) : kb.CompareTo(ka);
                    return by != 0 ? by : a.Order.CompareTo(b.Order);
                });

            if (!chart)
            {
                _chart.Show(null, null);
                StatsHeader();
                foreach (var row in rows) StatsRow(row.Text, row.Stats, row.Pages);
                return;
            }

            // Le graphique : les signes des textes gardés, dans l'ordre choisi ;
            // les écartés en pastilles au-dessus, dans l'ordre des pages.
            var texts = new List<BinderItem>();
            var values = new List<long>();
            foreach (var row in rows)
            {
                if (BookChart.IsExcluded(_item, row.Text)) continue;
                texts.Add(row.Text);
                values.Add(row.Stats.Sec);
            }
            var away = new List<BinderItem>();
            foreach (var row in _rows) if (BookChart.IsExcluded(_item, row.Text)) away.Add(row.Text);
            if (away.Count > 0)
            {
                _excluded.IsVisible = true;
                _excluded.Children.Add(new TextBlock { Text = "Écartés du graphique :", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) });
                foreach (var text in away)
                {
                    var back = text;
                    var chip = Buttons.IconText("plus-bold", Title(text), "Remettre « " + Title(text) + " » dans le graphique", Buttons.Compact, Buttons.Look.Outline);
                    chip.Margin = new Thickness(0, 0, 6, 4);
                    chip.Tag = "stats-excluded-chip"; // sonde
                    chip.Click += delegate { SetExcluded(back, false); };
                    _excluded.Children.Add(chip);
                }
            }
            _chart.Show(texts, values);
        }

        private void SetExcluded(BinderItem text, bool excluded)
        {
            if (_item == null || !BookChart.SetExcluded(_item, text, excluded)) return;
            Render();
            var handler = Changed;
            if (handler != null) handler();
        }

        private static string Title(BinderItem text)
        {
            return string.IsNullOrEmpty(text.Title) ? "(sans titre)" : text.Title;
        }

        private sealed class StatsEntry { public BinderItem Text; public int Order; public TextStats Stats; public int Pages; }

        private static long StatsKey(StatsEntry entry, int mode)
        {
            switch ((mode + 1) / 2)
            {
                case 1: return entry.Stats.Words;
                case 2: return entry.Stats.Sec;
                default: return entry.Pages;
            }
        }

        /// <summary>Une ligne de la liste : quatre colonnes, dans un cadre qui
        /// se surligne au survol (24/10) — la ligne entière, pour la lire d'un trait.</summary>
        private static Grid StatsLine()
        {
            var grid = new Grid();
            for (var c = 0; c < 4; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(254) : new GridLength(110) });
            return grid;
        }

        private void StatsHeader()
        {
            var line = StatsLine();
            string[] heads = { "Texte", "Mots", "Signes", "Pages" };
            for (var c = 0; c < 4; c++)
                StatsCell(line, c, new TextBlock { Text = heads[c], Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right });
            _stats.Children.Add(new Border { Child = line, Padding = new Thickness(6, 0, 6, 0) });
        }

        private void StatsRow(BinderItem text, TextStats stats, int pages)
        {
            var culture = CultureInfo.CurrentCulture;
            var line = StatsLine();
            StatsCell(line, 0, new TextBlock { Text = text.Title, Foreground = Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 8, 2) });
            StatsCell(line, 1, Number(stats.Words.ToString("N0", culture)));
            StatsCell(line, 2, Number(stats.Sec.ToString("N0", culture)));
            StatsCell(line, 3, Number(pages < 0 ? "—" : pages.ToString("N0", culture)));
            var row = new Border
            {
                Child = line,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Tag = "stats-row" // sonde
            };
            row.PointerEntered += delegate { HoverStatsRow(row, true); };
            row.PointerExited += delegate { HoverStatsRow(row, false); };
            _stats.Children.Add(row);
        }

        private static TextBlock Number(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
        }

        private static void HoverStatsRow(Border row, bool on)
        {
            row.Background = on ? (IBrush)Chrome.AccentTint : Brushes.Transparent;
        }

        private static void StatsCell(Grid line, int column, Control element)
        {
            Grid.SetColumn(element, column);
            line.Children.Add(element);
        }

        // — Sonde (18/10, 24/10, 29/10, 27/11).
        internal void SetStatsOrderForProbe(int index) { _statsOrder.SelectedIndex = index; }
        internal List<string> StatsTitlesForProbe()
        {
            var titles = new List<string>();
            foreach (var child in _stats.Children)
            {
                var row = child as Border;
                if (row == null || (row.Tag as string) != "stats-row") continue;
                var line = row.Child as Grid;
                var title = line == null || line.Children.Count == 0 ? null : line.Children[0] as TextBlock;
                if (title != null) titles.Add(title.Text);
            }
            return titles;
        }
        internal int StatsRowsForProbe()
        {
            var count = 0;
            foreach (var child in _stats.Children) if ((child.Tag as string) == "stats-row") count++;
            return count;
        }
        internal bool HoverStatsRowForProbe(int index, bool on)
        {
            var seen = 0;
            foreach (var child in _stats.Children)
            {
                var row = child as Border;
                if (row == null || (row.Tag as string) != "stats-row") continue;
                if (seen++ != index) continue;
                HoverStatsRow(row, on);
                return ReferenceEquals(row.Background, Chrome.AccentTint) == on;
            }
            return false;
        }
        internal ToggleButton ChartToggleForProbe { get { return _chartToggle; } }
        internal BookStatsChart ChartForProbe { get { return _chart; } }
        internal bool ListShownForProbe { get { return _stats.IsVisible; } }
        internal bool HasExpanderForProbe
        {
            get
            {
                foreach (var child in Children) if (child is Expander) return true;
                return false;
            }
        }
        internal List<string> ExcludedChipsForProbe()
        {
            var titles = new List<string>();
            if (!_excluded.IsVisible) return titles;
            foreach (var child in _excluded.Children)
            {
                var chip = child as Button;
                if (chip != null && (chip.Tag as string) == "stats-excluded-chip") titles.Add(ToolTip.GetTip(chip) as string);
            }
            return titles;
        }
        internal bool RestoreForProbe(int index)
        {
            var seen = 0;
            foreach (var child in _excluded.Children)
            {
                var chip = child as Button;
                if (chip == null || (chip.Tag as string) != "stats-excluded-chip") continue;
                if (seen++ != index) continue;
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return true;
            }
            return false;
        }
    }

    /// <summary>Le graphique des signes d'un livre (27/11, 2.0.0) — à la
    /// manière du graphique d'intensité d'un plan (PlanChart) : l'échelle à
    /// gauche, fixe ; la courbe, un point par texte, DÉFILE à l'horizontale
    /// dès que les textes sont trop nombreux pour la largeur (un pas minimal
    /// par texte, jamais resserré) ; les titres tournés à −90° sous leur
    /// point. L'échelle va de 0 au premier nombre rond au-dessus du plus long
    /// texte (BookChart.Ceiling), en cinq divisions ; un filet pointillé
    /// marque la moyenne des textes tracés. Sous chaque point, une petite
    /// croix écarte le texte du graphique (ExcludeRequested).</summary>
    public sealed class BookStatsChart : Grid
    {
        private const double AxisWidth = 84;
        private const double MinStep = 64;
        private const double Top = 34;
        private const double CrossBand = 24;
        private const double CaptionBand = 160;
        private const double ChartHeight = 500;

        private readonly Canvas _axis = new Canvas { Width = AxisWidth };
        private readonly Canvas _plot = new Canvas();
        private readonly ScrollViewer _scroll;
        private List<BinderItem> _texts;
        private List<long> _values;

        public event Action<BinderItem> ExcludeRequested;

        public BookStatsChart()
        {
            Height = ChartHeight; // une grille de Canvas ne mesure rien d'elle-même
            Margin = new Thickness(0, 6, 0, 0);
            ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _scroll = new ScrollViewer
            {
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Content = _plot,
                Tag = "stats-chart-scroll"
            };
            _scroll.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                // La molette seule fait défiler la courbe QUAND elle déborde
                // (comme le graphique d'intensité) ; sinon elle reste à la page.
                if (!Overflows || Ui.HasCommand(e.KeyModifiers) || (e.KeyModifiers & KeyModifiers.Shift) != 0) return;
                if (Math.Abs(e.Delta.Y) < 0.001) return; // un geste horizontal du pavé : le défilement natif
                _scroll.Offset = new Vector(Math.Max(0, Math.Min(_plot.Width - _scroll.Viewport.Width, _scroll.Offset.X - Ui.Wheel(e))), _scroll.Offset.Y);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Grid.SetColumn(_scroll, 1);
            Children.Add(_axis);
            Children.Add(_scroll);
            Ui.OnSizeChanged(this, Draw);
        }

        /// <summary>Trace ces textes et leurs signes (null : rien).</summary>
        public void Show(List<BinderItem> texts, List<long> values)
        {
            _texts = texts;
            _values = values;
            Draw();
        }

        private bool Overflows { get { return _plot.Width > Math.Max(0, Bounds.Width - AxisWidth) + 0.5; } }

        // — Sonde.
        internal int PointsForProbe { get; private set; }
        internal int CrossesForProbe { get; private set; }
        internal bool OverflowsForProbe { get { return Overflows; } }
        internal double PlotWidthForProbe { get { return _plot.Width; } }
        internal double CeilingForProbe { get; private set; }
        internal bool ScrollsForProbe { get { return _scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto; } }
        internal bool LaidOutForProbe { get { return Bounds.Width - AxisWidth >= 50; } }
        internal void ExcludeForProbe(int index)
        {
            if (_texts == null || index < 0 || index >= _texts.Count) return;
            var handler = ExcludeRequested;
            if (handler != null) handler(_texts[index]);
        }

        public void Draw()
        {
            _axis.Children.Clear();
            _plot.Children.Clear();
            PointsForProbe = 0;
            CrossesForProbe = 0;
            var height = ChartHeight - Marabook.App.Theme.ScrollBarSize;
            var viewport = Bounds.Width - AxisWidth;
            if (_texts == null || _values == null || viewport < 50) return;
            var culture = CultureInfo.CurrentCulture;
            var count = _texts.Count;
            var bottom = height - CrossBand - CaptionBand;
            var step = count == 0 ? viewport : Math.Max(MinStep, viewport / count);
            var width = Math.Max(viewport, step * count);
            _plot.Width = width;
            _plot.Height = height;
            _axis.Height = height;

            long max = 0; double sum = 0;
            foreach (var value in _values) { if (value > max) max = value; sum += value; }
            var ceiling = BookChart.Ceiling(max);
            CeilingForProbe = ceiling;

            // L'échelle : cinq divisions, leur nombre à gauche et leur filet de fond.
            var unit = new TextBlock { Text = "signes", FontSize = 10, Foreground = Chrome.FaintText, Width = AxisWidth - 14, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(unit, 0);
            Canvas.SetTop(unit, 0);
            _axis.Children.Add(unit);
            for (var division = 0; division <= BookChart.Divisions; division++)
            {
                var level = ceiling * division / BookChart.Divisions;
                var y = ValueY(level, ceiling, bottom);
                _plot.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(width, y), Stroke = division == 0 ? Chrome.BorderStrong : Chrome.Border, StrokeThickness = 1 });
                var label = new TextBlock { Text = level.ToString("N0", culture), FontSize = 11, Foreground = Chrome.SoftText, Width = AxisWidth - 14, TextAlignment = TextAlignment.Right };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                _axis.Children.Add(label);
                _axis.Children.Add(new Line { StartPoint = new Point(AxisWidth - 8, y), EndPoint = new Point(AxisWidth, y), Stroke = Chrome.BorderStrong, StrokeThickness = 1 });
            }
            if (count == 0)
            {
                var none = new TextBlock { Text = "Tous les textes sont écartés : remettez-en un depuis « Écartés du graphique ».", Foreground = Chrome.SoftText };
                Canvas.SetLeft(none, 12);
                Canvas.SetTop(none, Top);
                _plot.Children.Add(none);
                return;
            }

            // La moyenne des textes tracés : un filet pointillé et son nombre.
            if (count > 1)
            {
                var mean = sum / count;
                var meanY = ValueY(mean, ceiling, bottom);
                _plot.Children.Add(new Line { StartPoint = new Point(0, meanY), EndPoint = new Point(width, meanY), Stroke = Chrome.FaintText, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 }, IsHitTestVisible = false });
                var meanLabel = new TextBlock { Text = "moyenne · " + Math.Round(mean).ToString("N0", culture), FontSize = 10, Foreground = Chrome.SoftText, IsHitTestVisible = false };
                Canvas.SetLeft(meanLabel, 6);
                Canvas.SetTop(meanLabel, meanY + 2);
                _plot.Children.Add(meanLabel);
            }

            var points = new Points();
            var area = new Points();
            for (var i = 0; i < count; i++)
            {
                var text = _texts[i];
                var value = _values[i];
                var x = step * (i + 0.5);
                var y = ValueY(value, ceiling, bottom);
                if (i == 0) area.Add(new Point(x, bottom));
                points.Add(new Point(x, y));
                area.Add(new Point(x, y));
                if (i == count - 1) area.Add(new Point(x, bottom));
                var title = string.IsNullOrEmpty(text.Title) ? "(sans titre)" : text.Title;
                var signs = value.ToString("N0", culture);
                _plot.Children.Add(new Line { StartPoint = new Point(x, bottom), EndPoint = new Point(x, bottom + 5), Stroke = Chrome.BorderStrong, StrokeThickness = 1 });
                // Le trait du socle au point, au survol de la colonne (comme le graphique d'intensité).
                var guide = new Line { StartPoint = new Point(x, bottom), EndPoint = new Point(x, y), Stroke = Chrome.Accent, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 }, IsVisible = false, IsHitTestVisible = false };
                var zone = new Rectangle { Width = step, Height = Math.Max(0, bottom - Top), Fill = Brushes.Transparent, [ToolTip.TipProperty] = title + " — " + signs + " signes" };
                Canvas.SetLeft(zone, x - step / 2);
                Canvas.SetTop(zone, Top);
                var guideRef = guide;
                zone.PointerEntered += delegate { guideRef.IsVisible = true; };
                zone.PointerExited += delegate { guideRef.IsVisible = false; };
                _plot.Children.Add(zone);
                _plot.Children.Add(guide);
                var dot = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Chrome.Accent,
                    Stroke = Chrome.WindowBg,
                    StrokeThickness = 2,
                    [ToolTip.TipProperty] = title + " — " + signs + " signes"
                };
                Canvas.SetLeft(dot, x - 6);
                Canvas.SetTop(dot, y - 6);
                dot.PointerEntered += delegate { guideRef.IsVisible = true; };
                dot.PointerExited += delegate { guideRef.IsVisible = false; };
                _plot.Children.Add(dot);
                PointsForProbe++;
                // Le nombre de signes au-dessus du point.
                var tag = new TextBlock { Text = signs, FontSize = 10, Foreground = Chrome.SoftText, Width = step, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
                Canvas.SetLeft(tag, x - step / 2);
                Canvas.SetTop(tag, y - 24);
                _plot.Children.Add(tag);

                // La petite croix, entre l'axe et le titre : écarter ce texte du graphique.
                var cross = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = Icons.Make("x-bold", 9, Chrome.SoftText),
                    Tag = "stats-chart-exclude", // sonde
                    [ToolTip.TipProperty] = "Écarter « " + title + " » du graphique"
                };
                var crossRef = cross;
                var target = text;
                cross.PointerEntered += delegate { crossRef.Background = Chrome.AccentTint; };
                cross.PointerExited += delegate { crossRef.Background = Brushes.Transparent; };
                cross.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    if (!e.GetCurrentPoint(crossRef).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    var handler = ExcludeRequested;
                    if (handler != null) handler(target);
                };
                Canvas.SetLeft(cross, x - 9);
                Canvas.SetTop(cross, bottom + 6);
                _plot.Children.Add(cross);
                CrossesForProbe++;

                // Le titre tourné à −90° sous la croix : sa fin la touche, il se lit de bas en haut.
                var caption = new LayoutTransformControl
                {
                    LayoutTransform = new RotateTransform(-90),
                    Child = new TextBlock
                    {
                        Text = title,
                        FontSize = 11,
                        Foreground = Chrome.Ink,
                        Width = CaptionBand - 12,
                        TextAlignment = TextAlignment.Right,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        [ToolTip.TipProperty] = title
                    }
                };
                Canvas.SetLeft(caption, x - 8);
                Canvas.SetTop(caption, bottom + CrossBand + 4);
                _plot.Children.Add(caption);
            }
            var accent = ((SolidColorBrush)Chrome.Accent).Color;
            var first = BookChart.Divisions + 1; // après les filets de fond
            _plot.Children.Insert(first, new Polygon { Points = area, Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)), IsHitTestVisible = false });
            _plot.Children.Insert(first + 1, new Polyline { Points = points, Stroke = Chrome.Accent, StrokeThickness = 2.4, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false });
        }

        /// <summary>L'ordonnée d'un nombre de signes : 0 sur l'axe, le plafond en haut.</summary>
        private static double ValueY(double value, double ceiling, double bottom)
        {
            return bottom - value / ceiling * (bottom - Top);
        }
    }
}
