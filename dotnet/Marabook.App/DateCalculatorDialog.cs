using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE CALCULATEUR DE DATES d'un système temporel (1.2.0,
    /// 14/11/2026, Rémi) : une date plus ou moins une durée (« 40 jours après
    /// le 3/Sel/E2.880 »), et le temps entre deux dates. Une fenêtre à part,
    /// sans modalité : elle reste ouverte pendant qu'on travaille. Elle
    /// s'ouvre depuis la tuile du système (Astrolabe) ou par clic droit sur
    /// un point de la frise — alors à cette date. Tout le calcul est dans
    /// Model/TimeMath.cs.</summary>
    public class DateCalculatorDialog : Window
    {
        private readonly TimeSystem _system;
        private readonly DateField _from, _first, _second;
        private readonly ComboBox _direction;
        private readonly TextBox _years, _months, _weeks, _days;
        private readonly TextBlock _sum, _sumDetail, _between, _betweenDetail;

        private DateCalculatorDialog(Window owner, TimeSystem system, TimePoint at)
        {
            _system = system.Clone(); // le système peut changer pendant que la fenêtre vit
            TimelineUi.Dress(this, owner, "Calculateur de dates — " + system.Name);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var start = at == null ? "" : _system.Format(TimeMath.Full(at));
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 16), Width = 460 };

            // ------------------------------------------------ une date et une durée
            panel.Children.Add(Title("Ajouter ou retirer une durée", 0));
            panel.Children.Add(TimelineUi.Label("Date de départ :", 6));
            _from = new DateField(_system, start);
            panel.Children.Add(_from);
            var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("110,10,*,8,*,8,*,8,*"), Margin = new Thickness(0, 10, 0, 0) };
            _direction = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom };
            _direction.Items.Add("Ajouter");
            _direction.Items.Add("Retirer");
            _direction.SelectedIndex = 0;
            amounts.Children.Add(_direction);
            _years = Amount(amounts, 2, "Années");
            _months = Amount(amounts, 4, "Mois");
            _weeks = Amount(amounts, 6, "Semaines");
            _days = Amount(amounts, 8, "Jours");
            _weeks.IsEnabled = _system.WeekDays.Count > 0;
            if (!_weeks.IsEnabled) _weeks.Watermark = "—";
            panel.Children.Add(amounts);
            _sum = new TextBlock { Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "calculator-sum" };
            _sumDetail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            panel.Children.Add(Result(_sum, _sumDetail));

            // ------------------------------------------------ entre deux dates
            panel.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 16, 0, 0) });
            panel.Children.Add(Title("Durée entre deux dates", 14));
            var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 2, 0, 0) };
            var left = new StackPanel();
            left.Children.Add(TimelineUi.Label("Du :", 4));
            _first = new DateField(_system, start);
            left.Children.Add(_first);
            var right = new StackPanel();
            right.Children.Add(TimelineUi.Label("Au :", 4));
            _second = new DateField(_system, "");
            right.Children.Add(_second);
            Grid.SetColumn(right, 2);
            pair.Children.Add(left);
            pair.Children.Add(right);
            panel.Children.Add(pair);
            _between = new TextBlock { Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "calculator-between" };
            _betweenDetail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            panel.Children.Add(Result(_between, _betweenDetail));
            panel.Children.Add(TimelineUi.Hint("Une date sans mois ou sans jour compte pour le premier jour de sa période. Les heures ne sont pas comptées."));

            var close = new Button { Content = "Fermer", IsCancel = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            close.Click += delegate { Close(); };
            panel.Children.Add(close);
            Content = panel;

            _from.Changed += Compute;
            _first.Changed += Compute;
            _second.Changed += Compute;
            _direction.SelectionChanged += delegate { Compute(); };
            foreach (var box in new[] { _years, _months, _weeks, _days }) box.TextChanged += delegate { Compute(); };
            Compute();
            Loaded += delegate
            {
                if (_from.Point == null) { _from.Box.Focus(); return; }
                _days.Focus();
            };
        }

        private static TextBlock Title(string text, double top)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, top, 0, 0) };
        }

        private static TextBox Amount(Grid grid, int column, string label)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(1, 0, 0, 3) });
            var box = new TextBox { Watermark = "0" };
            stack.Children.Add(box);
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
            return string.Join("  ·  ", parts.ToArray());
        }

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private void Compute()
        {
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

        /// <summary>Ouvre le calculateur du système ; at : la date de départ (un point de la frise), null sinon.</summary>
        public static DateCalculatorDialog Open(Window owner, TimeSystem system, TimePoint at)
        {
            if (system == null) return null;
            var dialog = new DateCalculatorDialog(owner, system, at);
            if (owner != null) dialog.Show(owner);
            else dialog.Show();
            return dialog;
        }

        // ---- sonde
        internal void SetSumForProbe(string from, bool remove, string years, string months, string weeks, string days)
        {
            _from.Text = from;
            _direction.SelectedIndex = remove ? 1 : 0;
            _years.Text = years; _months.Text = months; _weeks.Text = weeks; _days.Text = days;
        }
        internal void SetBetweenForProbe(string first, string second) { _first.Text = first; _second.Text = second; }
        internal string FromForProbe { get { return _from.Text; } }
        internal string SumForProbe { get { return _sum.Text ?? ""; } }
        internal string SumDetailForProbe { get { return _sumDetail.Text ?? ""; } }
        internal string BetweenForProbe { get { return _between.Text ?? ""; } }
        internal string BetweenDetailForProbe { get { return _betweenDetail.Text ?? ""; } }
    }
}
