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
            _previous = Buttons.Text("◂ Événement précédent", null, Buttons.Compact, Buttons.Look.Outline);
            _previous.Tag = "calendar-previous-event";
            _previous.Click += delegate { Jump(-1); };
            _next = Buttons.Text("Événement suivant ▸", null, Buttons.Compact, Buttons.Look.Outline);
            _next.Tag = "calendar-next-event";
            _next.Click += delegate { Jump(1); };
            jumps.Children.Add(_previous);
            jumps.Children.Add(_next);
            DockPanel.SetDock(jumps, Dock.Right);
            bar.Children.Add(jumps);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            nav.Children.Add(Step("«", "Année précédente", delegate { Show(Year - 1, Month); }));
            nav.Children.Add(Step("‹", "Mois précédent", delegate { ShiftMonth(-1); }));
            _months = new ComboBox { MinWidth = 150, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "calendar-months" };
            _months.SelectionChanged += delegate { if (!_filling && _months.SelectedIndex >= 0) Show(Year, _months.SelectedIndex + 1); };
            nav.Children.Add(_months);
            nav.Children.Add(Step("›", "Mois suivant", delegate { ShiftMonth(1); }));
            nav.Children.Add(Step("»", "Année suivante", delegate { Show(Year + 1, Month); }));
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Tag = "calendar-title" };
            // Le segment et la longueur du mois : sous la barre (à côté du titre, ils passaient sous les boutons d'une page étroite).
            _subtitle = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(18, 0, 16, 8), TextTrimming = TextTrimming.CharacterEllipsis };
            nav.Children.Add(_title);
            bar.Children.Add(nav);
            var top = new StackPanel();
            top.Children.Add(bar);
            top.Children.Add(_subtitle);
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
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _grid });
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
            _title.Text = (_system.Months.Count > 0 ? _system.Months[Month - 1].Name + " · " : "") + _system.FormatYear(Year);
            _subtitle.Text = (_system.HasSegments && Year >= 0 ? _system.SegmentName(_system.SegmentIndexOf(Year)) + " · " : "")
                + TimeSystem.Count(_system.DaysInMonth(Year, Month), "jour", "jours");
            long ignoredYear;
            int ignoredMonth;
            _previous.IsEnabled = Neighbour(-1, out ignoredYear, out ignoredMonth);
            _next.IsEnabled = Neighbour(1, out ignoredYear, out ignoredMonth);

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
            for (var day = 1; day <= days; day++)
            {
                var slot = offset + day - 1;
                var cell = Cell(day, firstNumber + day - 1, events, selected);
                Grid.SetColumn(cell, slot % columns);
                Grid.SetRow(cell, 1 + slot / columns);
                _grid.Children.Add(cell);
            }

            RebuildOrphans(events, selected);
        }

        private bool SameMonths()
        {
            var names = _months.ItemsSource as List<string>;
            if (names == null || names.Count != _system.Months.Count) return false;
            for (var i = 0; i < names.Count; i++) if (names[i] != _system.Months[i].Name) return false;
            return true;
        }

        private Border Cell(int day, long dayNumber, List<TimelineEvent> events, TimelineEvent selected)
        {
            var date = new TimePoint(Year, Month, day);
            var body = new StackPanel();
            var number = new TextBlock { Text = day.ToString(CultureInfo.InvariantCulture), Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(2, 0, 0, 3) };
            body.Children.Add(number);
            var count = 0;
            foreach (var entry in events)
            {
                if (!TimeMath.CoversDay(_system, entry, dayNumber)) continue;
                count++;
                if (count <= MaxChips) body.Children.Add(Chip(entry, date, selected));
            }
            if (count > MaxChips)
                body.Children.Add(new TextBlock { Text = "+ " + (count - MaxChips) + (count - MaxChips > 1 ? " autres" : " autre"), Foreground = Chrome.SoftText, FontSize = 10.5, Margin = new Thickness(4, 1, 0, 0) });
            if (count > 0) { number.Foreground = Chrome.Ink; number.FontWeight = FontWeight.SemiBold; }
            var cell = new Border
            {
                Child = body, MinHeight = 88, Margin = new Thickness(2), Padding = new Thickness(5, 4, 5, 5), CornerRadius = new CornerRadius(6),
                Background = Chrome.CanvasBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "calendar-day"
            };
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
                if (TimeMath.OrphanIn(entry, Year, Month)) inMonth.Add(entry);
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
            Group("CE MOIS — " + monthName.ToUpper(CultureInfo.CurrentCulture), inMonth, selected, false, "Aucun ce mois-ci.", "calendar-orphans-month");
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
        internal string TitleForProbe { get { return _title.Text ?? ""; } }
        internal int ColumnsForProbe { get { return _grid.ColumnDefinitions.Count; } }
        internal int DayCellsForProbe { get { var n = 0; foreach (var child in _grid.Children) if (child is Border) n++; return n; } }
        internal int ColumnOfDayForProbe(int day)
        {
            var n = 0;
            foreach (var child in _grid.Children)
                if (child is Border && ++n == day) return Grid.GetColumn((Control)child);
            return -1;
        }
        internal List<string> EventsOfDayForProbe(int day)
        {
            var titles = new List<string>();
            var n = 0;
            foreach (var child in _grid.Children)
            {
                var cell = child as Border;
                if (cell == null || ++n != day) continue;
                foreach (var item in ((StackPanel)cell.Child).Children)
                {
                    var chip = item as Border;
                    if (chip != null) titles.Add(((TextBlock)chip.Child).Text);
                }
            }
            return titles;
        }
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
        internal void JumpForProbe(int direction) { Jump(direction); }
        internal void ShiftMonthForProbe(int delta) { ShiftMonth(delta); }
        internal bool CanJumpForProbe(int direction) { return direction < 0 ? _previous.IsEnabled : _next.IsEnabled; }
    }
}
