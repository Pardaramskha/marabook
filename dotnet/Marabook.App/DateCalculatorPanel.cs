using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE CALCULATEUR DE DATES (1.2.0, 14/11/2026, Rémi) : une date
    /// plus ou moins une durée (« 40 jours après le 3/Sel/E2.880 »), et le
    /// temps entre deux dates. D'abord une fenêtre ; Rémi (14/11 soir) : « on
    /// ne doit pouvoir ouvrir qu'un seul calculateur » — c'est désormais UN
    /// ONGLET DU RAIL, comme le Lexique : absent tant qu'on ne l'a pas demandé
    /// (tuile d'un système dans l'Astrolabe, clic droit sur la frise), épinglable,
    /// et refermé pour de bon par sa croix s'il n'est pas épinglé. Tout le
    /// calcul est dans Model/TimeMath.cs ; l'Astrolabe y ajoute la saison et
    /// les lunes du jour quand le système vit dans un monde.</summary>
    public class DateCalculatorPanel : DockPanel
    {
        private readonly ComboBox _systems;
        private readonly ToggleButton _pin;
        private readonly StackPanel _body;
        private readonly TextBlock _none;
        private DateField _from, _first, _second;
        private ComboBox _direction;
        private TextBox _years, _months, _weeks, _days;
        private TextBlock _sum, _sumDetail, _between, _betweenDetail;
        private List<TimeSystem> _list = new List<TimeSystem>();
        private List<string> _names = new List<string>();
        private TimeSystem _system;
        private bool _filling;

        public Func<List<TimeSystem>> SystemsProvider;          // les systèmes du projet ouvert
        public Func<TimeSystem, TimePoint, string> SkyProvider; // la saison et les lunes d'un jour (l'Astrolabe), null sans monde
        public event Action<bool> PinToggled;
        public event Action CloseRequested;

        public DateCalculatorPanel()
        {
            Background = Chrome.BarBgLight;
            LastChildFill = true;

            var head = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            SetDock(head, Dock.Top);
            var close = Buttons.Close("Fermer");
            close.Click += delegate { var h = CloseRequested; if (h != null) h(); };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            _pin = Buttons.IconToggle("push-pin-bold", "Épingler", Buttons.Compact, Buttons.Look.Calm);
            _pin.Margin = new Thickness(0, 0, 4, 0);
            _pin.Tag = "calculator-pin";
            _pin.Click += delegate
            {
                var h = PinToggled;
                if (h != null) h(_pin.IsChecked == true);
                RefreshPinTip();
            };
            DockPanel.SetDock(_pin, Dock.Right);
            head.Children.Add(_pin);
            var glyph = Icons.Make("pile-frises", 15, Chrome.Accent) as Control;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                head.Children.Add(glyph);
            }
            head.Children.Add(new TextBlock { Text = "Calculateur de dates", Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(head);

            _systems = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 12, 8), Tag = "calculator-systems" };
            _systems.SelectionChanged += delegate
            {
                if (_filling) return;
                var index = _systems.SelectedIndex;
                if (index >= 0 && index < _list.Count) Build(_list[index], null);
            };
            SetDock(_systems, Dock.Top);
            Children.Add(_systems);

            _none = new TextBlock
            {
                Text = "Aucun système temporel dans ce projet : il se crée avec la première frise (Astrolabe).",
                Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 4, 14, 0), IsVisible = false
            };
            SetDock(_none, Dock.Top);
            Children.Add(_none);

            _body = new StackPanel { Margin = new Thickness(12, 2, 12, 14) };
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body });
            RefreshPinTip();
        }

        public void SetPinned(bool pinned)
        {
            _pin.IsChecked = pinned;
            RefreshPinTip();
        }

        private void RefreshPinTip()
        {
            ToolTip.SetTip(_pin, _pin.IsChecked == true ? "Retirer le Calculateur du rail" : "Épingler le Calculateur au rail");
        }

        // -------------------------------------------------------------- systèmes

        /// <summary>Le calculateur d'un système, à une date (null : sans date de départ).</summary>
        public void Open(TimeSystem system, TimePoint at)
        {
            FillSystems(system);
            if (_system != null) Build(_system, at);
        }

        /// <summary>Le projet a changé (ouverture, système modifié, annulation) : la
        /// liste se relit ; le système choisi est gardé s'il existe encore, et ce
        /// qui était écrit dans les champs reste.</summary>
        public void Reload()
        {
            var id = _system == null ? null : _system.Id;
            var texts = _from == null ? null : new[] { _from.Text, _first.Text, _second.Text, _years.Text, _months.Text, _weeks.Text, _days.Text };
            var direction = _direction == null ? 0 : _direction.SelectedIndex;
            TimeSystem wanted = null;
            if (SystemsProvider != null) foreach (var system in SystemsProvider()) if (system.Id == id) wanted = system;
            FillSystems(wanted);
            if (_system == null) return;
            Build(_system, null);
            if (texts == null || _system.Id != id) return;
            _from.Text = texts[0]; _first.Text = texts[1]; _second.Text = texts[2];
            _years.Text = texts[3]; _months.Text = texts[4]; _weeks.Text = texts[5]; _days.Text = texts[6];
            _direction.SelectedIndex = direction;
        }

        private void FillSystems(TimeSystem wanted)
        {
            _list = SystemsProvider == null ? new List<TimeSystem>() : SystemsProvider();
            if (wanted != null && !_list.Contains(wanted)) _list.Insert(0, wanted); // le brouillon d'un système pas encore au projet
            _system = wanted ?? (_list.Count > 0 ? _list[0] : null);
            _filling = true;
            var names = new List<string>();
            foreach (var system in _list) names.Add(system.Name);
            var same = names.Count == _names.Count;
            for (var i = 0; same && i < names.Count; i++) same = names[i] == _names[i];
            if (!same)
            {
                _names = names;
                _systems.SelectedIndex = -1;
                _systems.ItemsSource = _names; // une liste neuve : jamais Items.Clear() sur une sélection (Avalonia)
            }
            _systems.SelectedIndex = _system == null ? -1 : _list.IndexOf(_system);
            _filling = false;
            _systems.IsVisible = _list.Count > 0;
            _none.IsVisible = _list.Count == 0;
            if (_system == null) { _body.Children.Clear(); _from = null; }
        }

        // ---------------------------------------------------------------- champs

        private void Build(TimeSystem system, TimePoint at)
        {
            _system = system;
            var start = at == null ? "" : system.Format(TimeMath.Full(at));
            _body.Children.Clear();

            // — Une date et une durée.
            _body.Children.Add(Title("Ajouter ou retirer une durée", 0));
            _body.Children.Add(TimelineUi.Label("Date de départ :", 6));
            _from = new DateField(system, start);
            _body.Children.Add(_from);
            _direction = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0) };
            _direction.Items.Add("Ajouter");
            _direction.Items.Add("Retirer");
            _direction.SelectedIndex = 0;
            _body.Children.Add(_direction);
            // Le rail est étroit : deux durées par ligne.
            var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            _years = Amount(amounts, 0, 0, "Années");
            _months = Amount(amounts, 0, 2, "Mois");
            _weeks = Amount(amounts, 1, 0, "Semaines");
            _days = Amount(amounts, 1, 2, "Jours");
            _weeks.IsEnabled = system.WeekDays.Count > 0;
            if (!_weeks.IsEnabled) _weeks.Watermark = "—";
            _body.Children.Add(amounts);
            _sum = new TextBlock { Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "calculator-sum" };
            _sumDetail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            _body.Children.Add(Result(_sum, _sumDetail));

            // — Entre deux dates.
            _body.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 16, 0, 0) });
            _body.Children.Add(Title("Durée entre deux dates", 14));
            _body.Children.Add(TimelineUi.Label("Du :", 6));
            _first = new DateField(system, start);
            _body.Children.Add(_first);
            _body.Children.Add(TimelineUi.Label("Au :", 8));
            _second = new DateField(system, "");
            _body.Children.Add(_second);
            _between = new TextBlock { Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "calculator-between" };
            _betweenDetail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            _body.Children.Add(Result(_between, _betweenDetail));
            _body.Children.Add(TimelineUi.Hint("Une date sans mois ou sans jour compte pour le premier jour de sa période. Les heures ne sont pas comptées."));

            _from.Changed += Compute;
            _first.Changed += Compute;
            _second.Changed += Compute;
            _direction.SelectionChanged += delegate { Compute(); };
            foreach (var box in new[] { _years, _months, _weeks, _days }) box.TextChanged += delegate { Compute(); };
            Compute();
        }

        private static TextBlock Title(string text, double top)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        }

        private static TextBox Amount(Grid grid, int row, int column, string label)
        {
            var stack = new StackPanel { Margin = new Thickness(0, row == 0 ? 0 : 6, 0, 0) };
            stack.Children.Add(new TextBlock { Text = label, Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(1, 0, 0, 3) });
            var box = new TextBox { Watermark = "0" };
            stack.Children.Add(box);
            Grid.SetRow(stack, row);
            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);
            return box;
        }

        /// <summary>Un résultat dans son cadre arrondi : ce n'est pas un champ.</summary>
        private static Border Result(TextBlock main, TextBlock detail)
        {
            var body = new StackPanel();
            body.Children.Add(main);
            body.Children.Add(detail);
            return new Border
            {
                Child = body, Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 10, 0, 0), CornerRadius = new CornerRadius(8),
                BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Background = Chrome.FieldBg
            };
        }

        private static bool Read(TextBox box, out long value)
        {
            value = 0;
            var text = (box.Text ?? "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Trim();
            if (text.Length == 0 || !box.IsEnabled) return true;
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= 100000000;
        }

        private string Describe(TimePoint point)
        {
            var parts = new List<string>();
            var weekDay = _system.WeekDayOf(point);
            if (weekDay != null) parts.Add(weekDay);
            if (_system.HasSegments) parts.Add(_system.SegmentName(_system.SegmentIndexOf(point.Year)));
            parts.Add("jour " + TimeMath.DayOfYear(_system, point) + " de l'année (sur " + _system.DaysInYear(point.Year) + ")");
            var sky = SkyProvider == null ? null : SkyProvider(_system, point);
            if (!string.IsNullOrEmpty(sky)) parts.Add(sky);
            return string.Join("  ·  ", parts.ToArray());
        }

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private void Compute()
        {
            if (_from == null || _system == null) return;
            // — Une date et une durée.
            var from = _from.Point;
            long years, months, weeks, days;
            var readable = Read(_years, out years) & Read(_months, out months) & Read(_weeks, out weeks) & Read(_days, out days);
            if (from == null) { _sum.Text = "—"; _sumDetail.Text = "Écrivez la date de départ dans la notation du système."; }
            else if (!readable) { _sum.Text = "—"; _sumDetail.Text = "Les durées sont des nombres entiers, zéro ou plus."; }
            else
            {
                var sign = _direction.SelectedIndex == 1 ? -1 : 1;
                var total = days + weeks * Math.Max(1, _system.WeekDays.Count);
                var result = TimeMath.Add(_system, from, sign * years, sign * months, sign * total);
                _sum.Text = _system.Format(result);
                _sumDetail.Text = Describe(result);
            }

            // — Entre deux dates.
            var first = _first.Point;
            var second = _second.Point;
            if (first == null || second == null) { _between.Text = "—"; _betweenDetail.Text = "Écrivez les deux dates."; return; }
            var distance = TimeMath.Between(_system, first, second);
            _between.Text = TimeMath.Label(distance) + (distance.Backwards ? " plus tôt" : "");
            var details = new List<string>();
            details.Add("soit " + TimeSystem.Count(distance.TotalDays, "jour", "jours"));
            var week = _system.WeekDays.Count;
            if (week > 0 && distance.TotalDays >= week)
            {
                var rest = distance.TotalDays % week;
                details.Add(TimeSystem.Count(distance.TotalDays / week, "semaine", "semaines") + (rest > 0 ? " et " + TimeSystem.Count(rest, "jour", "jours") : ""));
            }
            if (distance.TotalDays > 0) details.Add((distance.TotalDays * Math.Max(1, _system.HoursPerDay)).ToString("N0", French) + " heures");
            _betweenDetail.Text = string.Join("  ·  ", details.ToArray());
        }

        // ---- sonde
        internal void SetSumForProbe(string from, bool remove, string years, string months, string weeks, string days)
        {
            _from.Text = from;
            _direction.SelectedIndex = remove ? 1 : 0;
            _years.Text = years; _months.Text = months; _weeks.Text = weeks; _days.Text = days;
        }
        internal void SetBetweenForProbe(string first, string second) { _first.Text = first; _second.Text = second; }
        internal string FromForProbe { get { return _from == null ? null : _from.Text; } }
        internal string SumForProbe { get { return _sum == null ? "" : _sum.Text ?? ""; } }
        internal string SumDetailForProbe { get { return _sumDetail == null ? "" : _sumDetail.Text ?? ""; } }
        internal string BetweenForProbe { get { return _between == null ? "" : _between.Text ?? ""; } }
        internal string BetweenDetailForProbe { get { return _betweenDetail == null ? "" : _betweenDetail.Text ?? ""; } }
        internal string SystemForProbe { get { return _system == null ? null : _system.Name; } }
        internal int SystemChoicesForProbe { get { return _names.Count; } }
        internal bool PinnedForProbe { get { return _pin.IsChecked == true; } }
    }
}
