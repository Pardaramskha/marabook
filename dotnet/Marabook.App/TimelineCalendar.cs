using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA VUE CALENDRIER d'une frise (1.2.0, 14/11/2026, Rémi) : un
    /// mois du système temporel en cases — autant de colonnes que la semaine a
    /// de jours — avec, dans chaque case, les événements de ce jour.
    ///
    /// Beaucoup d'auteurs datent à l'année ou au mois, sans jour : ces
    /// événements-là n'ont pas de case. Ils sont les ORPHELINS, rangés dans la
    /// colonne de droite, toujours visible : ceux du mois affiché, ceux de
    /// l'année, puis tous les autres de la frise (un clic y mène). Un
    /// calendrier sans un seul jour daté reste ainsi utile.
    ///
    /// La vue ne possède rien : la frise (TimelineView) lui prête ses briques
    /// et reçoit ses gestes (choisir, modifier, créer, menu).</summary>
    internal sealed class TimelineCalendar : DockPanel
    {
        private const int MaxChips = 3, MaxElsewhere = 150;

        public Func<List<TimelineEvent>> Events;                  // les briques montrées (filtres appliqués)
        public Func<TimelineEvent, Color> ColorOf;
        public Func<TimelineEvent> Selected;
        public Action<TimelineEvent> Select, Edit;
        public Action<TimePoint> Create;
        public Func<TimelineEvent, TimePoint, ContextMenu> Menu;
        public Func<World> World;                                   // le monde du calendrier (saisons, lunes) ; null sans monde

        private readonly TextBlock _title, _subtitle;
        private readonly ComboBox _months;
        private readonly Grid _grid;
        private readonly StackPanel _orphans;
        private readonly Button _previous, _next;
        private TimeSystem _system;
        private bool _filling;

        public long Year { get; private set; }
        public int Month { get; private set; }

        public TimelineCalendar()
        {
            // ---- la barre de navigation
            var bar = new DockPanel { Margin = new Thickness(16, 10, 16, 2) };
            var jumps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            _previous = Buttons.Text("◂ Précédent", "Événement précédent", Buttons.Compact, Buttons.Look.Outline);
            _previous.Tag = "calendar-previous-event";
            _previous.Click += delegate { Jump(-1); };
            _next = Buttons.Text("Suivant ▸", "Événement suivant", Buttons.Compact, Buttons.Look.Outline);
            _next.Tag = "calendar-next-event";
            _next.Click += delegate { Jump(1); };
            jumps.Children.Add(_previous);
            jumps.Children.Add(_next);
            DockPanel.SetDock(jumps, Dock.Right);
            bar.Children.Add(jumps);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            // L'ANNÉE EN DAMIERS (16/11, Rémi) : l'icône du mode Calendrier, à gauche du
            // sélecteur de mois, bascule entre le mois en grandes cases et l'année entière.
            _yearToggle = Buttons.IconToggle("fiche-evenement", "Toute l'année : un damier par mois", Buttons.Compact, Buttons.Look.Outline);
            _yearToggle.Tag = "calendar-year-mode";
            _yearToggle.Margin = new Thickness(0, 0, 6, 0);
            _yearToggle.IsCheckedChanged += delegate { if (_filling) return; Remember(); _yearMode = _yearToggle.IsChecked == true; Rebuild(); };
            nav.Children.Add(_yearToggle);
            nav.Children.Add(Step("«", "Année précédente", delegate { Show(Year - 1, Month); }));
            _previousMonth = Step("‹", "Mois précédent", delegate { ShiftMonth(-1); });
            nav.Children.Add(_previousMonth);
            _months = new ComboBox { MinWidth = 150, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "calendar-months" };
            _months.SelectionChanged += delegate
            {
                if (_filling || _months.SelectedIndex < 0) return;
                if (_yearMode) Remember();
                LeaveYearMode(); // choisir un mois depuis l'année y mène
                Show(Year, _months.SelectedIndex + 1);
            };
            nav.Children.Add(_months);
            _nextMonth = Step("›", "Mois suivant", delegate { ShiftMonth(1); });
            nav.Children.Add(_nextMonth);
            nav.Children.Add(Step("»", "Année suivante", delegate { Show(Year + 1, Month); }));
            // Le titre : « Aubemer (1) · » puis l'année, qui se clique et se réécrit (16/11, Rémi).
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Tag = "calendar-title" };
            _yearLabel = new TextBlock { Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _yearButton = new Border
            {
                Child = _yearLabel, Padding = new Thickness(5, 1, 5, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Tag = "calendar-year"
            };
            ToolTip.SetTip(_yearButton, "Changer d'année");
            _yearButton.PointerEntered += delegate { _yearButton.Background = Chrome.FieldBg; _yearButton.BorderBrush = Chrome.BorderStrong; };
            _yearButton.PointerExited += delegate { _yearButton.Background = Brushes.Transparent; _yearButton.BorderBrush = Brushes.Transparent; };
            _yearButton.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; BeginYearEdit(); };
            _yearBox = new TextBox { MinWidth = 110, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Tag = "calendar-year-box" };
            _yearBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; CommitYear(false); }
                else if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; EndYearEdit(); }
            };
            _yearBox.LostFocus += delegate { CommitYear(true); };
            // Le segment et la longueur du mois : sous la barre (à côté du titre, ils passaient sous les boutons d'une page étroite).
            _subtitle = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            // Les saisons du mois, chacune avec la pastille de sa couleur — celle du liseré des cases (16/11, Rémi).
            _seasonLegend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "calendar-seasons" };
            var under = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 16, 8) };
            under.Children.Add(_subtitle);
            under.Children.Add(_seasonLegend);
            nav.Children.Add(_title);
            nav.Children.Add(_yearButton);
            nav.Children.Add(_yearBox);
            bar.Children.Add(nav);
            var top = new StackPanel();
            top.Children.Add(bar);
            top.Children.Add(under);
            SetDock(top, Dock.Top);
            Children.Add(top);

            // ---- les orphelins : une colonne à droite, toujours là
            _orphans = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            var side = new Border
            {
                Width = 270, Margin = new Thickness(0, 0, 16, 12), CornerRadius = new CornerRadius(8),
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "calendar-orphans",
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _orphans }
            };
            SetDock(side, Dock.Right);
            Children.Add(side);

            // ---- le mois
            _grid = new Grid { Margin = new Thickness(14, 0, 10, 12) };
            // ---- l'année : un petit damier par mois
            _yearPanel = new WrapPanel { Margin = new Thickness(10, 0, 6, 12), IsVisible = false, Tag = "calendar-year-grid" };
            var surface = new Panel();
            surface.Children.Add(_grid);
            surface.Children.Add(_yearPanel);
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = surface });
        }

        private readonly ToggleButton _yearToggle;
        private readonly Button _previousMonth, _nextMonth;
        private readonly TextBlock _yearLabel;
        private readonly Border _yearButton;
        private readonly TextBox _yearBox;
        private readonly StackPanel _seasonLegend;
        private readonly WrapPanel _yearPanel;
        private readonly List<string> _seasonNames = new List<string>(); // la légende, pour la sonde
        private bool _yearMode;

        private void LeaveYearMode()
        {
            if (!_yearMode) return;
            _yearMode = false;
            var was = _filling;
            _filling = true;
            _yearToggle.IsChecked = false;
            _filling = was;
        }

        // ------------------------------------------------------- l'année réécrite

        private void BeginYearEdit()
        {
            if (_system == null) return;
            _yearBox.Text = _system.FormatYear(Year);
            _yearBox.Foreground = Chrome.Ink;
            _yearButton.IsVisible = false;
            _yearBox.IsVisible = true;
            _yearBox.Focus();
            _yearBox.SelectAll();
        }

        private void EndYearEdit()
        {
            if (!_yearBox.IsVisible) return;
            _yearBox.IsVisible = false;
            _yearButton.IsVisible = true;
        }

        /// <summary>Lit l'année écrite (dans la notation du calendrier ; une date
        /// entière mène aussi à son mois) et y va. Illisible : le champ rougit et
        /// attend — sauf si l'on en sort (leaving), où l'année d'avant reste.</summary>
        private void CommitYear(bool leaving)
        {
            if (!_yearBox.IsVisible || _system == null) return;
            var text = (_yearBox.Text ?? "").Trim();
            TimePoint point;
            if (text.Length > 0 && _system.TryParse(text, out point))
            {
                EndYearEdit();
                Show(point.Year, point.HasMonth && !_yearMode ? point.Month : Month);
            }
            else if (leaving || text.Length == 0) EndYearEdit();
            else _yearBox.Foreground = Chrome.Danger;
        }

        private static Button Step(string glyph, string tip, Action click)
        {
            var button = Buttons.Text(glyph, tip, Buttons.Compact, Buttons.Look.Outline);
            button.Padding = new Thickness(9, 0, 9, 0);
            button.Click += delegate { click(); };
            return button;
        }

        // ------------------------------------------------------------ navigation

        public void SetSystem(TimeSystem system)
        {
            if (!ReferenceEquals(_system, system)) _trail.Clear(); // une autre frise : ses pas ne sont plus les nôtres
            _system = system;
            if (Month < 1 || Month > MonthCount) Month = 1;
        }

        private int MonthCount { get { return _system == null ? 1 : Math.Max(1, _system.Months.Count); } }

        public void Show(long year, int month)
        {
            Year = year;
            Month = Math.Max(1, Math.Min(MonthCount, month));
            Rebuild();
        }

        public void Show(TimePoint point)
        {
            if (point != null) Show(point.Year, Math.Max(1, point.Month));
        }

        private void ShiftMonth(int delta)
        {
            var index = Month - 1 + delta;
            Show(Year + LeapRule.FloorDiv(index, MonthCount), (int)LeapRule.Mod(index, MonthCount) + 1);
        }

        private static long Key(long year, int month) { return year * 100000L + Math.Max(1, month); }

        /// <summary>Le mois le plus proche (avant ou après) où un événement commence.</summary>
        private bool Neighbour(int direction, out long year, out int month)
        {
            year = 0;
            month = 1;
            var current = Key(Year, Month);
            var found = false;
            long best = 0;
            foreach (var entry in Events())
            {
                if (entry.Start == null) continue;
                var key = Key(entry.Start.Year, entry.Start.Month);
                if (direction < 0 ? key >= current : key <= current) continue;
                if (found && (direction < 0 ? key <= best : key >= best)) continue;
                found = true;
                best = key;
                year = entry.Start.Year;
                month = Math.Max(1, entry.Start.Month);
            }
            return found;
        }

        private void Jump(int direction)
        {
            long year;
            int month;
            if (Neighbour(direction, out year, out month)) Show(year, month);
        }

        // ---------------------------------------------------------------- dessin

        public void Refresh() { if (_system != null) Rebuild(); }

        private void Rebuild()
        {
            if (_system == null || Events == null) return;
            var events = Events();
            var selected = Selected == null ? null : Selected();

            _filling = true;
            if (_months.Items.Count != _system.Months.Count || !SameMonths())
            {
                _months.SelectedIndex = -1;
                var names = new List<string>();
                foreach (var month in _system.Months) names.Add(month.Name);
                _months.ItemsSource = names; // une liste neuve : jamais Items.Clear() sur une sélection (Avalonia)
            }
            _months.SelectedIndex = Month - 1;
            _filling = false;
            // Le mois, son numéro entre parenthèses, puis l'année ; en vue d'année, l'année seule.
            _title.Text = _system.Months.Count > 0 ? _system.Months[Month - 1].Name + " (" + Month + ") · " : "";
            _title.IsVisible = !_yearMode && _system.Months.Count > 0;
            _yearLabel.Text = _system.FormatYear(Year);
            _yearButton.Margin = new Thickness(_title.IsVisible ? 0 : 9, 0, 0, 0);
            EndYearEdit();
            _previousMonth.IsVisible = !_yearMode;
            _nextMonth.IsVisible = !_yearMode;
            _grid.IsVisible = !_yearMode;
            _yearPanel.IsVisible = _yearMode;
            _world = World == null ? null : World();
            var yearDays = 0;
            for (var m = 1; m <= MonthCount; m++) yearDays += Math.Max(1, _system.DaysInMonth(Year, m));
            _subtitle.Text = (_system.HasSegments && Year >= 0 ? _system.SegmentName(_system.SegmentIndexOf(Year)) + " · " : "")
                + (_yearMode ? TimeSystem.Count(MonthCount, "mois", "mois") + ", " + TimeSystem.Count(yearDays, "jour", "jours") : TimeSystem.Count(_system.DaysInMonth(Year, Month), "jour", "jours"));
            RebuildSeasonLegend();
            long ignoredYear;
            int ignoredMonth;
            _previous.IsEnabled = Neighbour(-1, out ignoredYear, out ignoredMonth);
            _next.IsEnabled = Neighbour(1, out ignoredYear, out ignoredMonth);

            _yearPanel.Children.Clear();
            if (_yearMode)
            {
                _grid.Children.Clear();
                RebuildYear(events);
                RebuildOrphans(events, selected);
                return;
            }

            // — Les cases : une colonne par jour de la semaine (sept sans semaine).
            var week = _system.WeekDays.Count;
            var columns = week > 0 ? week : 7;
            var days = Math.Max(1, _system.DaysInMonth(Year, Month));
            var firstNumber = _system.DayNumber(new TimePoint(Year, Month, 1));
            var offset = week > 0 ? (int)LeapRule.Mod(firstNumber, week) : 0;
            var rows = (int)Math.Ceiling((offset + days) / (double)columns);
            _grid.Children.Clear();
            _grid.ColumnDefinitions.Clear();
            _grid.RowDefinitions.Clear();
            for (var c = 0; c < columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var r = 0; r < rows; r++) _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (week > 0)
                for (var c = 0; c < columns; c++)
                {
                    var head = new TextBlock
                    {
                        Text = _system.WeekDays[c], Foreground = Chrome.SoftText, FontSize = 11.5, FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(8, 0, 4, 4), TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(head, c);
                    _grid.Children.Add(head);
                }
            // — LES PÉRIODES (16/11, Rémi) : une brique CONTINUE par ligne de semaine,
            //   son titre une fois en début de ligne, façon agenda — plus une brique
            //   par jour. Chaque période prend un couloir libre de la ligne ; au-delà
            //   de MaxLanes, elle compte dans le « + n autres » des cases qu'elle couvre.
            var spans = events.FindAll(delegate(TimelineEvent e) { return e.IsSpan && e.Start != null; });
            spans.Sort(delegate(TimelineEvent a, TimelineEvent b)
            {
                var order = _system.AxisStart(a.Start).CompareTo(_system.AxisStart(b.Start));
                return order != 0 ? order : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            });
            var lanesOfRow = new int[rows];
            var hiddenOfDay = new int[days + 2];
            var bars = new List<Border>();
            _dayTitles.Clear();
            for (var day = 1; day <= days; day++) _dayTitles[day] = new List<string>();
            for (var r = 0; r < rows; r++)
            {
                var firstDay = Math.Max(1, r * columns - offset + 1);
                var lastDay = Math.Min(days, (r + 1) * columns - offset);
                var taken = new List<bool[]>(); // par couloir : les jours pris
                foreach (var entry in spans)
                {
                    int from = 0, to = 0;
                    for (var day = firstDay; day <= lastDay; day++)
                        if (TimeMath.CoversDay(_system, entry, firstNumber + day - 1)) { if (from == 0) from = day; to = day; }
                    if (from == 0) continue;
                    var lane = 0;
                    for (; lane < taken.Count; lane++)
                    {
                        var free = true;
                        for (var day = from; day <= to && free; day++) free = !taken[lane][day];
                        if (free) break;
                    }
                    if (lane >= MaxLanes)
                    {
                        for (var day = from; day <= to; day++) hiddenOfDay[day]++;
                        continue;
                    }
                    if (lane == taken.Count) taken.Add(new bool[days + 2]);
                    for (var day = from; day <= to; day++) { taken[lane][day] = true; _dayTitles[day].Add(TitleOf(entry)); }
                    var starts = !TimeMath.CoversDay(_system, entry, firstNumber + from - 2);
                    var ends = !TimeMath.CoversDay(_system, entry, firstNumber + to);
                    bars.Add(SpanBar(entry, r, offset + from - 1 - r * columns, to - from + 1, lane, starts, ends, selected, new TimePoint(Year, Month, from)));
                }
                lanesOfRow[r] = taken.Count;
            }
            for (var day = 1; day <= days; day++)
            {
                var slot = offset + day - 1;
                var cell = Cell(day, firstNumber + day - 1, events, selected, lanesOfRow[slot / columns], hiddenOfDay[day]);
                Grid.SetColumn(cell, slot % columns);
                Grid.SetRow(cell, 1 + slot / columns);
                _grid.Children.Add(cell);
            }
            foreach (var bar in bars) _grid.Children.Add(bar); // après les cases : par-dessus

            RebuildOrphans(events, selected);
        }

        private const int MaxLanes = 2;
        private const double LaneHeight = 19, BarTop = 27; // sous le numéro du jour d'une case
        private readonly Dictionary<int, List<string>> _dayTitles = new Dictionary<int, List<string>>(); // ce que chaque jour montre (sonde)

        /// <summary>La brique d'une période sur une ligne de semaine : d'un bord
        /// à l'autre de ses jours, arrondie là où elle commence et là où elle
        /// finit, droite là où elle continue sur la ligne d'avant ou d'après.</summary>
        private Border SpanBar(TimelineEvent entry, int row, int column, int span, int lane, bool starts, bool ends, TimelineEvent selected, TimePoint date)
        {
            var color = ColorOf == null ? Chrome.Accent.Color : ColorOf(entry);
            var chosen = selected != null && selected.Id == entry.Id;
            var bar = new Border
            {
                Height = LaneHeight - 2, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(starts ? 7 : 0, BarTop + lane * LaneHeight, ends ? 7 : 0, 0),
                CornerRadius = new CornerRadius(starts ? 4 : 0, ends ? 4 : 0, ends ? 4 : 0, starts ? 4 : 0),
                Background = new SolidColorBrush(color, 0.32), BorderBrush = chosen ? (IBrush)Chrome.Ink : new SolidColorBrush(color, 0.9),
                BorderThickness = chosen ? new Thickness(1.5) : new Thickness(starts ? 3 : 0, 0, 0, 0),
                Padding = new Thickness(6, 0, 4, 0), Cursor = new Cursor(StandardCursorType.Hand), Tag = "calendar-span",
                Child = new TextBlock { Text = TitleOf(entry), Foreground = Chrome.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            Grid.SetRow(bar, 1 + row);
            Grid.SetColumn(bar, column);
            Grid.SetColumnSpan(bar, span);
            Wire(bar, entry, date, false);
            return bar;
        }

        // ------------------------------------------------ le pas en arrière (souris)

        private struct Step_ { public bool YearMode; public long Year; public int Month; }
        private readonly List<Step_> _trail = new List<Step_>();

        /// <summary>Retient la vue d'où l'on part (mois ou année) avant d'en changer.</summary>
        private void Remember()
        {
            _trail.Add(new Step_ { YearMode = _yearMode, Year = Year, Month = Month });
            if (_trail.Count > 30) _trail.RemoveAt(0);
        }

        /// <summary>Le bouton « précédent » de la souris : revient à la vue
        /// d'avant (du mois à l'année, de l'année au mois). Faux s'il n'y en a pas.</summary>
        public bool TryStepBack()
        {
            if (_trail.Count == 0 || _system == null) return false;
            var step = _trail[_trail.Count - 1];
            _trail.RemoveAt(_trail.Count - 1);
            _yearMode = step.YearMode;
            var was = _filling;
            _filling = true;
            _yearToggle.IsChecked = _yearMode;
            _filling = was;
            Show(step.Year, step.Month);
            return true;
        }

        private World _world;

        /// <summary>Les saisons du mois affiché, dans l'ordre où il les traverse
        /// (leur index dans le monde) ; en vue d'année, celles de toute l'année.</summary>
        private List<int> SeasonsShown()
        {
            var indexes = new List<int>();
            if (_world == null || _world.Seasons.Count == 0) return indexes;
            for (var month = _yearMode ? 1 : Month; month <= (_yearMode ? MonthCount : Month); month++)
            {
                var days = Math.Max(1, _system.DaysInMonth(Year, month));
                for (var day = 1; day <= days; day++)
                {
                    var index = Astro.SeasonIndexAt(_world, _system, new TimePoint(Year, month, day));
                    if (index >= 0 && (indexes.Count == 0 || indexes[indexes.Count - 1] != index)) indexes.Add(index);
                }
            }
            return indexes;
        }

        /// <summary>La légende des saisons, à la suite du sous-titre : pour
        /// chacune, la pastille de sa couleur (celle du liseré des cases) et son
        /// nom ; une flèche de l'une à l'autre quand le mois en change.</summary>
        private void RebuildSeasonLegend()
        {
            _seasonLegend.Children.Clear();
            _seasonNames.Clear();
            var indexes = SeasonsShown();
            for (var i = 0; i < indexes.Count; i++)
            {
                var season = _world.Seasons[indexes[i]];
                _seasonLegend.Children.Add(new TextBlock { Text = i == 0 ? "  ·  " : "  →  ", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                Color tint;
                if (Color.TryParse(Astro.ColorOf(season, indexes[i]), out tint))
                    _seasonLegend.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0), Tag = "calendar-season-dot" });
                var name = string.IsNullOrEmpty(season.Name) ? "Saison " + (indexes[i] + 1) : season.Name;
                _seasonNames.Add(name);
                _seasonLegend.Children.Add(new TextBlock { Text = name, Foreground = Chrome.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            }
        }

        // ---------------------------------------------------------------- l'année

        /// <summary>L'ANNÉE EN DAMIERS (16/11, Rémi) : un petit calendrier par
        /// mois — son nom et son numéro, ses jours en cases serrées. Un jour qui
        /// porte un événement est teinté ; la saison garde son liseré. Un clic
        /// sur un mois ou sur l'un de ses jours l'ouvre en grand.</summary>
        private void RebuildYear(List<TimelineEvent> events)
        {
            var week = _system.WeekDays.Count;
            var columns = week > 0 ? week : 7;
            var dated = events.FindAll(delegate(TimelineEvent e) { return e.Start != null && TimeMath.IsDayPrecise(e); });
            for (var month = 1; month <= MonthCount; month++)
            {
                var days = Math.Max(1, _system.DaysInMonth(Year, month));
                var firstNumber = _system.DayNumber(new TimePoint(Year, month, 1));
                var offset = week > 0 ? (int)LeapRule.Mod(firstNumber, week) : 0;
                var grid = new Grid();
                for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(26, GridUnitType.Pixel));
                var rows = (int)Math.Ceiling((offset + days) / (double)columns);
                for (var r = 0; r <= rows; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (week > 0)
                    for (var c = 0; c < columns; c++)
                    {
                        var name = _system.WeekDays[c] ?? "";
                        var head = new TextBlock { Text = name.Length == 0 ? "" : name.Substring(0, 1).ToUpper(CultureInfo.CurrentCulture), Foreground = Chrome.FaintText, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
                        Grid.SetColumn(head, c);
                        grid.Children.Add(head);
                    }
                var captured = month;
                for (var day = 1; day <= days; day++)
                {
                    var dayNumber = firstNumber + day - 1;
                    var busy = dated.Exists(delegate(TimelineEvent e) { return TimeMath.CoversDay(_system, e, dayNumber); });
                    var cell = new Border
                    {
                        Width = 24, Height = 22, Margin = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand), Tag = "calendar-mini-day",
                        Background = busy ? new SolidColorBrush(Chrome.Accent.Color, 0.30) : Chrome.CanvasBg,
                        Child = new TextBlock
                        {
                            Text = day.ToString(CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = busy ? Chrome.Ink : Chrome.SoftText, FontWeight = busy ? FontWeight.SemiBold : FontWeight.Normal,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    Color tint;
                    var seasonIndex = _world == null ? -1 : Astro.SeasonIndexAt(_world, _system, new TimePoint(Year, month, day));
                    if (seasonIndex >= 0 && Color.TryParse(Astro.ColorOf(_world.Seasons[seasonIndex], seasonIndex), out tint))
                    {
                        cell.BorderBrush = new SolidColorBrush(tint, 0.85);
                        cell.BorderThickness = new Thickness(0, 2, 0, 0);
                    }
                    cell.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; OpenMonth(captured); };
                    var slot = offset + day - 1;
                    Grid.SetColumn(cell, slot % columns);
                    Grid.SetRow(cell, 1 + slot / columns);
                    grid.Children.Add(cell);
                }
                var title = new TextBlock
                {
                    Text = (_system.Months.Count > 0 ? _system.Months[month - 1].Name : "Mois") + " (" + month + ")", Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(2, 0, 0, 6), Cursor = new Cursor(StandardCursorType.Hand), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "calendar-mini-title"
                };
                title.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; OpenMonth(captured); };
                var body = new StackPanel();
                body.Children.Add(title);
                body.Children.Add(grid);
                _yearPanel.Children.Add(new Border
                {
                    Child = body, Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(4), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
                    Background = Chrome.CardBg, BorderBrush = month == Month ? Chrome.BorderStrong : Chrome.Border, BorderThickness = new Thickness(1), Tag = "calendar-mini-month"
                });
            }
        }

        private void OpenMonth(int month)
        {
            if (_yearMode) Remember();
            LeaveYearMode();
            Show(Year, month);
        }

        // ------------------------------------------------------------ les astres

        /// <summary>L'infobulle d'une lune du calendrier (16/11, Rémi) : sa phase,
        /// la dernière et la prochaine fois, en notation abrégée (jour/mois/année).</summary>
        private string MoonTip(Moon moon, int phase, long dayNumber)
        {
            var feminine = phase == Astro.Full || phase == Astro.New;
            return (string.IsNullOrEmpty(moon.Name) ? "Lune" : moon.Name) + " : " + Astro.MainPhaseName(phase)
                + "\n" + (feminine ? "Précédente : " : "Précédent : ") + TimeMath.ShortDate(_system, TimeMath.FromDayNumber(_system, Astro.MainPhaseDay(moon, dayNumber, -1)))
                + "\n" + (feminine ? "Prochaine : " : "Prochain : ") + TimeMath.ShortDate(_system, TimeMath.FromDayNumber(_system, Astro.MainPhaseDay(moon, dayNumber, 1)));
        }

        private string SunTip(Sun sun, long dayNumber)
        {
            return (string.IsNullOrEmpty(sun.Name) ? "Second soleil" : sun.Name) + " : au plus haut à minuit"
                + "\nPrécédent : " + TimeMath.ShortDate(_system, TimeMath.FromDayNumber(_system, Astro.HighDay(sun, dayNumber, -1)))
                + "\nProchain : " + TimeMath.ShortDate(_system, TimeMath.FromDayNumber(_system, Astro.HighDay(sun, dayNumber, 1)));
        }

        /// <summary>LA ZONE D'UN GLYPHE DU CIEL (16/11 soir, Rémi : « il faut être très précis ! ») :
        /// chaque lune, soleil ou étoile occupe une case de 19 × 19 — toute la hauteur de
        /// l'en-tête du jour — peinte en transparent : l'infobulle répond sur toute la case,
        /// et sans délai, plus seulement sur les quelques pixels du dessin.</summary>
        private static class Sky
        {
            public const double Box = 19;

            public static void Dress(Control glyph)
            {
                glyph.Width = Box;
                glyph.Height = Box;
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Cursor = new Cursor(StandardCursorType.Help);
                ToolTip.SetShowDelay(glyph, 120);
            }

            /// <summary>Peint la case (transparente : elle reçoit la souris), puis centre le dessin de côté « size ».</summary>
            public static IDisposable Area(DrawingContext context, Control glyph, double size)
            {
                context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, glyph.Bounds.Width, glyph.Bounds.Height));
                return context.PushTransform(Matrix.CreateTranslation((glyph.Bounds.Width - size) / 2, (glyph.Bounds.Height - size) / 2));
            }
        }

        /// <summary>Le petit disque d'une lune, à l'une de ses quatre phases principales.</summary>
        private sealed class MoonGlyph : Control
        {
            private readonly Color _color;
            private readonly int _phase;

            public MoonGlyph(Color color, int phase)
            {
                _color = color;
                _phase = phase;
                Sky.Dress(this);
            }

            public int Phase { get { return _phase; } }

            public override void Render(DrawingContext context)
            {
                using (Sky.Area(context, this, 11))
                {
                    var center = new Point(5.5, 5.5);
                    var brush = new SolidColorBrush(_color);
                    var pen = new Pen(brush, 1.2);
                    if (_phase == Astro.Full) { context.DrawEllipse(brush, pen, center, 4.4, 4.4); return; }
                    context.DrawEllipse(null, pen, center, 4.4, 4.4);
                    if (_phase == Astro.New) return;
                    // Un quartier : la moitié du disque — la droite pour le premier, la gauche pour le dernier.
                    using (context.PushClip(_phase == Astro.FirstQuarter ? new Rect(5.5, 0, 5.5, 11) : new Rect(0, 0, 5.5, 11)))
                        context.DrawEllipse(brush, null, center, 4.4, 4.4);
                }
            }
        }

        /// <summary>Le petit soleil d'un compagnon : un disque plein et ses huit rayons.</summary>
        private sealed class SunGlyph : Control
        {
            private readonly Color _color;

            public SunGlyph(Color color)
            {
                _color = color;
                Sky.Dress(this);
            }

            public override void Render(DrawingContext context)
            {
                using (Sky.Area(context, this, 13))
                {
                    var center = new Point(6.5, 6.5);
                    var brush = new SolidColorBrush(_color);
                    var pen = new Pen(brush, 1.1);
                    context.DrawEllipse(brush, null, center, 2.8, 2.8);
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * Math.PI / 4;
                        context.DrawLine(pen, new Point(center.X + Math.Cos(angle) * 4.3, center.Y + Math.Sin(angle) * 4.3), new Point(center.X + Math.Cos(angle) * 6.2, center.Y + Math.Sin(angle) * 6.2));
                    }
                }
            }
        }

        /// <summary>L'étoile d'un événement du ciel (16/11) : éclipse, lune bleue, phénomène personnalisé.</summary>
        private sealed class StarGlyph : Control
        {
            private readonly Color _color;

            public StarGlyph(Color color)
            {
                _color = color;
                Sky.Dress(this);
            }

            public override void Render(DrawingContext context)
            {
                // Une étoile à quatre branches, pleine.
                var center = new Point(6, 6);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var radius = i % 2 == 0 ? 5.6 : 1.9;
                        var angle = i * Math.PI / 4 - Math.PI / 2;
                        var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                        if (i == 0) path.BeginFigure(point, true); else path.LineTo(point);
                    }
                    path.EndFigure(true);
                }
                using (Sky.Area(context, this, 12)) context.DrawGeometry(new SolidColorBrush(_color), null, geometry);
            }
        }

        /// <summary>Sonde : la largeur de la zone sensible du premier glyphe du ciel de la case d'un jour (0 sans glyphe).</summary>
        internal double SkyGlyphBoxForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || !Equals(cell.Tag, "calendar-day") || ++n != day) continue;
                var top = ((StackPanel)cell.Child).Children[0] as DockPanel;
                if (top == null) continue;
                foreach (var item in top.Children)
                {
                    var sky = item as StackPanel;
                    if (sky != null && sky.Children.Count > 0) return Math.Min(sky.Children[0].Bounds.Width, sky.Children[0].Bounds.Height);
                }
            }
            return 0;
        }

        /// <summary>Sonde : l'infobulle de la case d'un jour (sa date abrégée).</summary>
        internal string DayTipForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell != null && Equals(cell.Tag, "calendar-day") && ++n == day) return ToolTip.GetTip(cell) as string;
            }
            return null;
        }

        /// <summary>Sonde : les infobulles des étoiles (événements du ciel) de la case d'un jour.</summary>
        internal List<string> StarTipsForProbe(int day)
        {
            var tips = new List<string>();
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || !Equals(cell.Tag, "calendar-day") || ++n != day) continue;
                var top = ((StackPanel)cell.Child).Children[0] as DockPanel;
                if (top == null) continue;
                foreach (var item in top.Children)
                {
                    var sky = item as StackPanel;
                    if (sky == null) continue;
                    foreach (var glyph in sky.Children) if (glyph is StarGlyph) tips.Add(ToolTip.GetTip((Control)glyph) as string);
                }
            }
            return tips;
        }

        private bool SameMonths()
        {
            var names = _months.ItemsSource as List<string>;
            if (names == null || names.Count != _system.Months.Count) return false;
            for (var i = 0; i < names.Count; i++) if (names[i] != _system.Months[i].Name) return false;
            return true;
        }

        private Border Cell(int day, long dayNumber, List<TimelineEvent> events, TimelineEvent selected, int lanes, int hiddenSpans)
        {
            var date = new TimePoint(Year, Month, day);
            var body = new StackPanel();
            var number = new TextBlock { Text = day.ToString(CultureInfo.InvariantCulture), Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(2, 0, 0, 3) };
            // L'en-tête de la case : le numéro du jour ; à droite, les lunes qui changent de phase ce jour-là.
            // Hauteur fixe : les briques des périodes se posent juste dessous, à la même hauteur sur toute la ligne.
            var top = new DockPanel { Height = 19 };
            Color seasonTint = default(Color);
            var hasSeason = false;
            if (_world != null)
            {
                var moons = new StackPanel { Orientation = Orientation.Horizontal, Tag = "calendar-moons" };
                for (var i = 0; i < _world.Moons.Count; i++)
                {
                    if (Astro.IsSubMoon(_world, _world.Moons[i])) continue; // une lune de lune : dite par ses passages (les étoiles, plus bas)
                    var phase = Astro.MainPhaseOn(_world.Moons[i], dayNumber);
                    Color tint;
                    if (phase < 0 || !Color.TryParse(Astro.ColorOf(_world.Moons[i], i), out tint)) continue;
                    var glyph = new MoonGlyph(tint, phase);
                    ToolTip.SetTip(glyph, MoonTip(_world.Moons[i], phase, dayNumber));
                    moons.Children.Add(glyph);
                }
                // Un soleil compagnon qui culmine à minuit ce jour-là (15/11) : son disque rayonnant.
                var wanderers = Astro.Wanderers(_world); // un compagnon en orbite rapprochée ne culmine pas : ses éclipses sont des étoiles
                for (var i = 0; i < _world.Suns.Count; i++)
                {
                    Color tint;
                    if (!wanderers.Contains(_world.Suns[i]) || !Astro.SunHighOn(_world.Suns[i], dayNumber) || !Color.TryParse(Astro.ColorOf(_world.Suns[i], i), out tint)) continue;
                    var glyph = new SunGlyph(tint);
                    ToolTip.SetTip(glyph, SunTip(_world.Suns[i], dayNumber));
                    moons.Children.Add(glyph);
                }
                // CE QUE LE CIEL FAIT DE CE JOUR (16/11, Rémi) : éclipses, lune rousse, lune bleue,
                // lunes pleines ensemble, phénomènes personnalisés — une étoile par événement.
                var notes = Astro.NotesOn(_world, _system, dayNumber);
                if (notes.Count > 2)
                {
                    // Un jour chargé : UNE étoile, qui dit tout — l'en-tête de la case reste lisible.
                    var all = new List<string>();
                    foreach (var note in notes) all.Add(note.Text);
                    var glyph = new StarGlyph(Chrome.Accent.Color);
                    ToolTip.SetTip(glyph, string.Join("\n", all.ToArray()));
                    moons.Children.Add(glyph);
                }
                else
                    foreach (var note in notes)
                    {
                        Color tint;
                        if (!Color.TryParse(note.Color, out tint)) tint = Chrome.Accent.Color;
                        var glyph = new StarGlyph(tint);
                        ToolTip.SetTip(glyph, note.Text + (note.Detail != null ? "\n" + note.Detail : ""));
                        moons.Children.Add(glyph);
                    }
                if (moons.Children.Count > 0) { DockPanel.SetDock(moons, Dock.Right); top.Children.Add(moons); }
                var seasonIndex = Astro.SeasonIndexAt(_world, _system, date);
                hasSeason = seasonIndex >= 0 && Color.TryParse(Astro.ColorOf(_world.Seasons[seasonIndex], seasonIndex), out seasonTint);
            }
            top.Children.Add(number);
            body.Children.Add(top);
            // La place des périodes de la ligne (leurs briques sont posées par-dessus, d'une case à l'autre).
            if (lanes > 0) body.Children.Add(new Panel { Height = lanes * LaneHeight });
            var count = 0;
            var chips = 0;
            var room = Math.Max(1, MaxChips - lanes);
            foreach (var entry in events)
            {
                if (!TimeMath.CoversDay(_system, entry, dayNumber)) continue;
                count++;
                if (entry.IsSpan) continue; // dessinée en brique continue
                chips++;
                if (chips > room) continue;
                body.Children.Add(Chip(entry, date, selected));
                _dayTitles[day].Add(TitleOf(entry));
            }
            var more = Math.Max(0, chips - room) + hiddenSpans;
            if (more > 0)
                body.Children.Add(new TextBlock { Text = "+ " + more + (more > 1 ? " autres" : " autre"), Foreground = Chrome.SoftText, FontSize = 10.5, Margin = new Thickness(4, 1, 0, 0) });
            if (count > 0) { number.Foreground = Chrome.Ink; number.FontWeight = FontWeight.SemiBold; }
            var cell = new Border
            {
                Child = body, MinHeight = 88, Margin = new Thickness(2), Padding = new Thickness(5, 4, 5, 5), CornerRadius = new CornerRadius(6),
                Background = Chrome.CanvasBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "calendar-day"
            };
            // La saison du jour : un liseré de sa couleur en haut de la case.
            if (hasSeason)
            {
                cell.BorderBrush = new SolidColorBrush(seasonTint, 0.85);
                cell.BorderThickness = new Thickness(0, 3, 0, 0);
                cell.Padding = new Thickness(6, 2, 6, 6);
            }
            // L'INFOBULLE D'UN JOUR (16/11, Rémi) : sa date en notation abrégée (« 05/11/2026 ») —
            // tant qu'on ne survole pas un événement de la case (ses pastilles la font taire).
            var dayTip = TimeMath.ShortDate(_system, date);
            ToolTip.SetTip(cell, dayTip);
            foreach (var child in body.Children)
            {
                var chip = child as Border;
                if (chip == null || !Equals(chip.Tag, "calendar-event")) continue;
                chip.PointerEntered += delegate { ToolTip.SetIsOpen(cell, false); ToolTip.SetTip(cell, null); };
                chip.PointerExited += delegate { ToolTip.SetTip(cell, dayTip); };
            }
            cell.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (e.Handled) return;
                var properties = e.GetCurrentPoint(cell).Properties;
                if (properties.IsRightButtonPressed && Menu != null) { Ui.ShowMenu(Menu(null, date), cell); e.Handled = true; }
                else if (properties.IsLeftButtonPressed && e.ClickCount >= 2 && Create != null) { Create(date); e.Handled = true; }
                else if (properties.IsLeftButtonPressed && Select != null && selected != null) { Select(null); e.Handled = true; }
            };
            return cell;
        }

        private Border Chip(TimelineEvent entry, TimePoint date, TimelineEvent selected)
        {
            var color = ColorOf == null ? Chrome.Accent.Color : ColorOf(entry);
            var chosen = selected != null && selected.Id == entry.Id;
            var chip = new Border
            {
                Background = new SolidColorBrush(color, 0.22), BorderBrush = chosen ? (IBrush)Chrome.Ink : new SolidColorBrush(color, 0.9),
                BorderThickness = new Thickness(chosen ? 1.5 : 3, chosen ? 1.5 : 0, chosen ? 1.5 : 0, chosen ? 1.5 : 0),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 4, 2), Margin = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = "calendar-event",
                Child = new TextBlock { Text = TitleOf(entry), Foreground = Chrome.Ink, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            Wire(chip, entry, date, false);
            return chip;
        }

        private static string TitleOf(TimelineEvent entry)
        {
            return string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title;
        }

        /// <summary>Les gestes d'une brique du calendrier : clic = choisir (et, pour
        /// un orphelin d'ailleurs, y aller), double-clic = modifier, clic droit = menu.</summary>
        private void Wire(Border control, TimelineEvent entry, TimePoint date, bool travel)
        {
            control.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                var properties = e.GetCurrentPoint(control).Properties;
                e.Handled = true;
                if (properties.IsRightButtonPressed)
                {
                    if (Select != null) Select(entry);
                    if (Menu != null) Ui.ShowMenu(Menu(entry, date), this);
                    return;
                }
                if (!properties.IsLeftButtonPressed) return;
                if (e.ClickCount >= 2) { if (Edit != null) Edit(entry); return; }
                if (travel && entry.Start != null && (entry.Start.Year != Year || (entry.Start.HasMonth && entry.Start.Month != Month)))
                {
                    Year = entry.Start.Year;
                    Month = Math.Max(1, Math.Min(MonthCount, entry.Start.Month));
                }
                if (Select != null) Select(entry); // la frise relit tout : le calendrier se redessine
            };
        }

        // ------------------------------------------------------------- orphelins

        private void RebuildOrphans(List<TimelineEvent> events, TimelineEvent selected)
        {
            _orphans.Children.Clear();
            _orphans.Children.Add(new TextBlock { Text = "Événements orphelins", Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold });
            _orphans.Children.Add(new TextBlock
            {
                Text = "Datés à l'année ou au mois, sans jour précis : ils n'ont pas de case. Double-clic pour leur donner une date.",
                Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
            });
            var inMonth = new List<TimelineEvent>();
            var inYear = new List<TimelineEvent>();
            var elsewhere = new List<TimelineEvent>();
            foreach (var entry in events)
            {
                if (entry.Start == null || TimeMath.IsDayPrecise(entry)) continue;
                if (!_yearMode && TimeMath.OrphanIn(entry, Year, Month)) inMonth.Add(entry); // en vue d'année, ceux du mois sont « cette année »
                else if (TimeMath.OrphanIn(entry, Year, 0) || TimeMath.TouchesYear(entry, Year)) inYear.Add(entry);
                else elsewhere.Add(entry);
            }
            Comparison<TimelineEvent> byDate = delegate(TimelineEvent a, TimelineEvent b)
            {
                var order = _system.AxisStart(a.Start).CompareTo(_system.AxisStart(b.Start));
                return order != 0 ? order : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            };
            inMonth.Sort(byDate);
            inYear.Sort(byDate);
            elsewhere.Sort(byDate);
            if (inMonth.Count + inYear.Count + elsewhere.Count == 0)
            {
                _orphans.Children.Add(new TextBlock { Text = "Aucun : chaque événement a son jour.", Foreground = Chrome.FaintText, FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
                return;
            }
            var monthName = _system.Months.Count > 0 ? _system.Months[Month - 1].Name : "Ce mois";
            if (!_yearMode) Group("CE MOIS — " + monthName.ToUpper(CultureInfo.CurrentCulture), inMonth, selected, false, "Aucun ce mois-ci.", "calendar-orphans-month");
            Group("CETTE ANNÉE — " + _system.FormatYear(Year), inYear, selected, false, "Aucun cette année.", "calendar-orphans-year");
            if (elsewhere.Count > 0) Group("AILLEURS DANS LA FRISE", elsewhere, selected, true, null, "calendar-orphans-elsewhere");
        }

        private void Group(string caption, List<TimelineEvent> entries, TimelineEvent selected, bool travel, string none, string tag)
        {
            _orphans.Children.Add(new TextBlock
            {
                Text = caption + (entries.Count > 0 ? "  ·  " + entries.Count : ""), Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis, Tag = tag
            });
            if (entries.Count == 0)
            {
                _orphans.Children.Add(new TextBlock { Text = none, Foreground = Chrome.FaintText, FontSize = 12 });
                return;
            }
            for (var i = 0; i < entries.Count && i < MaxElsewhere; i++) _orphans.Children.Add(OrphanRow(entries[i], selected, travel));
            if (entries.Count > MaxElsewhere)
                _orphans.Children.Add(new TextBlock { Text = "… et " + (entries.Count - MaxElsewhere) + " autres.", Foreground = Chrome.FaintText, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) });
        }

        private Border OrphanRow(TimelineEvent entry, TimelineEvent selected, bool travel)
        {
            var color = ColorOf == null ? Chrome.Accent.Color : ColorOf(entry);
            var chosen = selected != null && selected.Id == entry.Id;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = TitleOf(entry), Foreground = Chrome.Ink, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Timelines.WhenLabel(_system, entry), Foreground = Chrome.SoftText, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var row = new Border
            {
                Child = text, Padding = new Thickness(8, 4, 6, 5), Margin = new Thickness(0, 0, 0, 3), CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color, chosen ? 0.26 : 0.10), BorderBrush = new SolidColorBrush(color, 0.9), BorderThickness = new Thickness(3, 0, 0, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = "calendar-orphan"
            };
            Wire(row, entry, entry.Start, travel);
            return row;
        }

        // ---- sonde
        internal string TitleForProbe { get { return (_title.IsVisible ? _title.Text ?? "" : "") + (_yearLabel.Text ?? ""); } }
        internal bool YearModeForProbe { get { return _yearMode; } }
        internal void SetYearModeForProbe(bool on) { _yearToggle.IsChecked = on; }
        internal int MiniMonthsForProbe { get { return _yearPanel.IsVisible ? _yearPanel.Children.Count : 0; } }
        internal string MiniTitleForProbe(int month) { return ((TextBlock)((StackPanel)((Border)_yearPanel.Children[month - 1]).Child).Children[0]).Text; }
        internal int MiniBusyDaysForProbe(int month)
        {
            var n = 0;
            foreach (var child in ((Grid)((StackPanel)((Border)_yearPanel.Children[month - 1]).Child).Children[1]).Children)
            {
                var cell = child as Border;
                if (cell != null && ((TextBlock)cell.Child).FontWeight == FontWeight.SemiBold) n++;
            }
            return n;
        }
        internal void OpenMiniMonthForProbe(int month) { OpenMonth(month); }
        internal bool MonthStepsShownForProbe { get { return _previousMonth.IsVisible && _nextMonth.IsVisible; } }
        internal bool EditingYearForProbe { get { return _yearBox.IsVisible; } }
        internal void EditYearForProbe(string text) { BeginYearEdit(); _yearBox.Text = text; CommitYear(false); }
        internal void CancelYearEditForProbe() { EndYearEdit(); }
        internal int SeasonDotsForProbe { get { var n = 0; foreach (var child in _seasonLegend.Children) if (child is Border) n++; return n; } }
        /// <summary>Sonde : l'infobulle de la première lune (ou du premier soleil) dessinée dans la case d'un jour.</summary>
        internal string SkyTipForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || ++n != day) continue;
                var top = ((StackPanel)cell.Child).Children[0] as DockPanel;
                if (top == null) continue;
                foreach (var item in top.Children)
                {
                    var sky = item as StackPanel;
                    if (sky != null && sky.Children.Count > 0) return ToolTip.GetTip(sky.Children[0]) as string;
                }
            }
            return null;
        }
        internal int ColumnsForProbe { get { return _grid.ColumnDefinitions.Count; } }
        internal int DayCellsForProbe { get { var n = 0; foreach (var child in _grid.Children) if (child is Border && Equals(((Border)child).Tag, "calendar-day")) n++; return n; } }
        internal int ColumnOfDayForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
                if (child is Border && ++n == day) return Grid.GetColumn((Control)child);
            return -1;
        }
        internal List<string> EventsOfDayForProbe(int day)
        {
            // Ce que le jour montre : les périodes (briques continues de sa ligne) puis ses événements.
            List<string> titles;
            return _dayTitles.TryGetValue(day, out titles) ? new List<string>(titles) : new List<string>();
        }
        internal int SpanBarsForProbe { get { var n = 0; foreach (var child in _grid.Children) if (child is Border && Equals(((Border)child).Tag, "calendar-span")) n++; return n; } }
        /// <summary>Sonde : les briques de période du mois — « titre:colonnes » dans l'ordre des lignes.</summary>
        internal List<string> SpanBarsDetailForProbe()
        {
            var bars = new List<string>();
            foreach (var child in _grid.Children)
            {
                var bar = child as Border;
                if (bar != null && Equals(bar.Tag, "calendar-span")) bars.Add(((TextBlock)bar.Child).Text + ":" + Grid.GetColumnSpan(bar));
            }
            return bars;
        }
        internal bool TryStepBackForProbe() { return TryStepBack(); }
        internal int TrailForProbe { get { return _trail.Count; } }
        internal List<string> OrphansForProbe(string groupTag)
        {
            var titles = new List<string>();
            var inside = false;
            foreach (var child in _orphans.Children)
            {
                var caption = child as TextBlock;
                if (caption != null && caption.Tag is string && ((string)caption.Tag).StartsWith("calendar-orphans-")) { inside = (string)caption.Tag == groupTag; continue; }
                var row = child as Border;
                if (inside && row != null) titles.Add(((TextBlock)((StackPanel)row.Child).Children[0]).Text);
            }
            return titles;
        }
        internal string SubtitleForProbe { get { return (_subtitle.Text ?? "") + (_seasonNames.Count == 0 ? "" : " · " + string.Join(" → ", _seasonNames.ToArray())); } }
        /// <summary>Sonde : les phases des lunes dessinées dans la case d'un jour (Astro.Full…), dans l'ordre.</summary>
        internal List<int> MoonsOfDayForProbe(int day)
        {
            var phases = new List<int>();
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || ++n != day) continue;
                var top = ((StackPanel)cell.Child).Children[0] as DockPanel;
                if (top == null) continue;
                foreach (var item in top.Children)
                {
                    var moons = item as StackPanel;
                    if (moons == null) continue;
                    foreach (var glyph in moons.Children) if (glyph is MoonGlyph) phases.Add(((MoonGlyph)glyph).Phase);
                }
            }
            return phases;
        }
        /// <summary>Sonde : le nombre de soleils compagnons dessinés dans la case d'un jour (ils y culminent à minuit).</summary>
        internal int SunsOfDayForProbe(int day)
        {
            var count = 0;
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || ++n != day) continue;
                var top = ((StackPanel)cell.Child).Children[0] as DockPanel;
                if (top == null) continue;
                foreach (var item in top.Children)
                {
                    var sky = item as StackPanel;
                    if (sky == null) continue;
                    foreach (var glyph in sky.Children) if (glyph is SunGlyph) count++;
                }
            }
            return count;
        }
        internal bool DayHasSeasonForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell != null && ++n == day) return cell.BorderThickness.Top >= 3;
            }
            return false;
        }
        internal void JumpForProbe(int direction) { Jump(direction); }
        internal void ShiftMonthForProbe(int delta) { ShiftMonth(delta); }
        internal bool CanJumpForProbe(int direction) { return direction < 0 ? _previous.IsEnabled : _next.IsEnabled; }
    }
}
