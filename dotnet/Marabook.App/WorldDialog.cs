using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    public sealed class WorldChoice
    {
        public World World;
        public List<string> SystemIds = new List<string>(); // les calendriers qui vivent dans ce monde
    }

    /// <summary>UN MONDE DE L'ASTROLABE (1.2.0, 14/11/2026, Rémi) : sa taille
    /// (circonférence, inclinaison — et ce qu'on en déduit : horizon, fuseaux,
    /// tour du monde, durée du jour selon la latitude), ses saisons (leur
    /// début dans l'année, la durée réelle de l'année et la dérive du
    /// calendrier) et ses lunes (période, une pleine lune de référence, la
    /// conjonction de toutes). Les calendriers cochés vivent dans ce monde :
    /// leur vue Calendrier, le survol de la frise et le Calculateur disent
    /// alors la saison et les lunes. Le premier coché sert de référence pour
    /// écrire les dates et compter les heures de cette fenêtre.</summary>
    public class WorldDialog : Window
    {
        private readonly Project _project;
        private readonly World _world;
        private readonly TextBox _name, _circumference, _tilt, _solarYear;
        private readonly StackPanel _systemChecks, _seasons, _moons, _facts;
        private readonly TextBlock _driftEcho, _moonEcho, _seasonHint, _problem, _reference;
        private readonly List<CheckBox> _checks = new List<CheckBox>();
        private readonly List<TimeSystem> _systems = new List<TimeSystem>();
        private WorldChoice _result;
        private bool _building = true;

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private WorldDialog(Window owner, Project project, World world, bool creating)
        {
            _project = project;
            _world = world;
            TimelineUi.Dress(this, owner, creating ? "Nouveau monde" : "Monde — " + world.Name);
            Width = 640;
            SizeToContent = SizeToContent.Height;
            CanResize = false;

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(TimelineUi.Label("Nom du monde :", 0));
            _name = new TextBox { Text = world.Name };
            root.Children.Add(_name);
            root.Children.Add(TimelineUi.Label("Les calendriers de ce monde :", 10));
            _systemChecks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            foreach (var system in project.TimeSystems)
            {
                var other = system.WorldId != null && system.WorldId != world.Id ? Astro.WorldOf(project, system) : null;
                var check = new CheckBox { Content = system.Name + (other != null ? " (aujourd'hui : " + other.Name + ")" : ""), IsChecked = system.WorldId == world.Id, Tag = "world-system" };
                check.IsCheckedChanged += delegate { Preview(); };
                _checks.Add(check);
                _systems.Add(system);
                _systemChecks.Children.Add(check);
            }
            if (_systems.Count == 0) _systemChecks.Children.Add(new TextBlock { Text = "Aucun système temporel : il se crée avec la première frise.", Foreground = Chrome.SoftText, FontSize = 12 });
            root.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _systemChecks });
            _reference = TimelineUi.Hint("");
            root.Children.Add(_reference);

            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), Height = 430 };

            // ------------------------------------------------------------- taille
            var size = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var sizeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,130,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            _circumference = new TextBox { Text = world.HasSize ? Show(world.Circumference) : "", Watermark = "ex. 40 075", Tag = "world-circumference" };
            _tilt = new TextBox { Text = world.HasTilt ? Show(world.AxialTilt) : "", Watermark = "ex. 23,4", Tag = "world-tilt" };
            Row(sizeGrid, 0, "Circonférence à l'équateur :", _circumference, "km");
            Row(sizeGrid, 1, "Inclinaison de l'axe :", _tilt, "degrés");
            size.Children.Add(sizeGrid);
            _facts = new StackPanel();
            size.Children.Add(new Border
            {
                Child = _facts, Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(8),
                BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Background = Chrome.FieldBg, Tag = "world-facts"
            });
            tabs.Items.Add(new TabItem { Header = "Taille du monde", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = size } });

            // ------------------------------------------------------------ saisons
            var seasons = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            seasons.Children.Add(new TextBlock { Text = "Les saisons, et le jour de l'année où chacune commence :", Foreground = Chrome.Ink });
            _seasons = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            seasons.Children.Add(new ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _seasons });
            var seasonButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            var addSeason = Buttons.IconText("plus-bold", "Ajouter une saison", null, Buttons.Compact, Buttons.Look.Outline);
            addSeason.Click += delegate { Collect(); _world.Seasons.Add(new Season { Name = "", StartDay = 1 }); RebuildSeasons(); Preview(); };
            var four = Buttons.Text("Quatre saisons égales", null, Buttons.Compact, Buttons.Look.Calm);
            four.Tag = "world-four-seasons";
            four.Click += delegate { Collect(); _world.Seasons.Clear(); _world.Seasons.AddRange(Astro.FourSeasons(Reference().BaseDays)); RebuildSeasons(); Preview(); };
            seasonButtons.Children.Add(addSeason);
            seasonButtons.Children.Add(four);
            seasons.Children.Add(seasonButtons);
            _seasonHint = TimelineUi.Hint("");
            seasons.Children.Add(_seasonHint);
            seasons.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 10, 0, 8) });
            var solar = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,130,8,*") };
            _solarYear = new TextBox { Text = world.SolarYear > 0 ? Show(world.SolarYear) : "", Watermark = "celle du calendrier", Tag = "world-solar-year" };
            Row(solar, 0, "Durée réelle de l'année :", _solarYear, "jours");
            seasons.Children.Add(solar);
            _driftEcho = TimelineUi.Hint("");
            _driftEcho.Tag = "world-drift";
            seasons.Children.Add(_driftEcho);
            tabs.Items.Add(new TabItem { Header = "Saisons", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = seasons } });

            // -------------------------------------------------------------- lunes
            var moons = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            moons.Children.Add(new TextBlock { Text = "Les lunes :", Foreground = Chrome.Ink });
            var moonHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,190,8,26"), Margin = new Thickness(0, 8, 0, 2) };
            moonHead.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.SoftText, FontSize = 11 });
            var periodHead = new TextBlock { Text = "Période (jours)", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(periodHead, 2);
            var fullHead = new TextBlock { Text = "Une pleine lune, le…", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(fullHead, 4);
            moonHead.Children.Add(periodHead);
            moonHead.Children.Add(fullHead);
            moons.Children.Add(moonHead);
            _moons = new StackPanel { Spacing = 4 };
            moons.Children.Add(new ScrollViewer { MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _moons });
            var addMoon = Buttons.IconText("plus-bold", "Ajouter une lune", null, Buttons.Compact, Buttons.Look.Outline);
            addMoon.HorizontalAlignment = HorizontalAlignment.Left;
            addMoon.Margin = new Thickness(0, 6, 0, 0);
            addMoon.Click += delegate { Collect(); _world.Moons.Add(new Moon { Name = "", Period = 29.53, FullAt = 0 }); RebuildMoons(); Preview(); };
            moons.Children.Add(addMoon);
            moons.Children.Add(TimelineUi.Hint("La période va d'une pleine lune à la suivante. La date d'une pleine lune s'écrit dans la notation du calendrier de référence : elle cale toutes les autres."));
            _moonEcho = TimelineUi.Hint("");
            _moonEcho.Tag = "world-moons-echo";
            moons.Children.Add(_moonEcho);
            tabs.Items.Add(new TabItem { Header = "Lunes", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = moons } });

            root.Children.Add(tabs);
            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
            root.Children.Add(_problem);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = creating ? "Créer le monde" : "Enregistrer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            root.Children.Add(buttons);
            Content = root;

            RebuildSeasons();
            RebuildMoons();
            _building = false;
            foreach (var box in new[] { _name, _circumference, _tilt, _solarYear }) box.TextChanged += delegate { Preview(); };
            Preview();
            Loaded += delegate { Dialogs.FitToScreen(this, owner); _name.Focus(); _name.SelectAll(); };
        }

        private static void Row(Grid grid, int row, string label, Control field, string unit)
        {
            var text = new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(text, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 2);
            field.Margin = new Thickness(0, 3, 0, 3);
            var after = new TextBlock { Text = unit, Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(after, row);
            Grid.SetColumn(after, 4);
            grid.Children.Add(text);
            grid.Children.Add(field);
            grid.Children.Add(after);
        }

        private static string Show(double value)
        {
            return Math.Round(value, 6).ToString("0.######", French);
        }

        /// <summary>Un nombre écrit à la française ou non (« 29,53 », « 29.53 », « 40 075 ») ; faux s'il n'en est pas un.</summary>
        internal static bool ReadNumber(string text, out double value)
        {
            var cleaned = (text ?? "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.').Trim();
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Le calendrier de référence : le premier coché, sinon le premier du projet, sinon celui de notre monde.</summary>
        private TimeSystem Reference()
        {
            for (var i = 0; i < _checks.Count; i++) if (_checks[i].IsChecked == true) return _systems[i];
            return _systems.Count > 0 ? _systems[0] : Fallback;
        }

        private static readonly TimeSystem Fallback = TimeSystem.Gregorian();

        // -------------------------------------------------------------- les lignes

        private void RebuildSeasons()
        {
            var was = _building;
            _building = true;
            _seasons.Children.Clear();
            var reference = Reference();
            for (var i = 0; i < _world.Seasons.Count; i++)
            {
                var season = _world.Seasons[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,8,*,8,Auto,6,70,8,150,8,26"), Tag = "world-season" };
                Color tint;
                Color.TryParse(Astro.ColorOf(season, i), out tint);
                row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBox { Text = season.Name, Watermark = "Nom de la saison" };
                Grid.SetColumn(name, 2);
                var word = new TextBlock { Text = "commence le jour", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(word, 4);
                var day = new TextBox { Text = season.StartDay.ToString(CultureInfo.InvariantCulture) };
                Grid.SetColumn(day, 6);
                var echo = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(echo, 8);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 10);
                var captured = season;
                remove.Click += delegate { Collect(); _world.Seasons.Remove(captured); RebuildSeasons(); Preview(); };
                name.TextChanged += delegate { Preview(); };
                day.TextChanged += delegate { Preview(); };
                row.Children.Add(name);
                row.Children.Add(word);
                row.Children.Add(day);
                row.Children.Add(echo);
                row.Children.Add(remove);
                _seasons.Children.Add(row);
            }
            if (_world.Seasons.Count == 0) _seasons.Children.Add(new TextBlock { Text = "Aucune saison.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
        }

        private void RebuildMoons()
        {
            var was = _building;
            _building = true;
            _moons.Children.Clear();
            var reference = Reference();
            for (var i = 0; i < _world.Moons.Count; i++)
            {
                var moon = _world.Moons[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,190,8,26"), Tag = "world-moon" };
                var name = new TextBox { Text = moon.Name, Watermark = "Nom de la lune" };
                var period = new TextBox { Text = Show(moon.Period) };
                Grid.SetColumn(period, 2);
                var full = new TextBox { Text = reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(moon.FullAt))), Watermark = reference.Format(new TimePoint(0, 1, 1)) };
                Grid.SetColumn(full, 4);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 6);
                var captured = moon;
                remove.Click += delegate { Collect(); _world.Moons.Remove(captured); RebuildMoons(); Preview(); };
                name.TextChanged += delegate { Preview(); };
                period.TextChanged += delegate { Preview(); };
                full.TextChanged += delegate { Preview(); };
                row.Children.Add(name);
                row.Children.Add(period);
                row.Children.Add(full);
                row.Children.Add(remove);
                _moons.Children.Add(row);
            }
            if (_world.Moons.Count == 0) _moons.Children.Add(new TextBlock { Text = "Aucune lune.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
        }

        // ----------------------------------------------------------------- lecture

        private string _fieldProblem;

        /// <summary>Recopie les champs dans le monde ; _fieldProblem dit ce qui ne se lit pas.</summary>
        private void Collect()
        {
            _fieldProblem = null;
            var reference = Reference();
            _world.Name = (_name.Text ?? "").Trim().Length == 0 ? "Monde" : _name.Text.Trim();
            double value;
            if ((_circumference.Text ?? "").Trim().Length == 0) _world.Circumference = 0;
            else if (ReadNumber(_circumference.Text, out value) && value > 0) _world.Circumference = value;
            else _fieldProblem = "La circonférence est un nombre de kilomètres, plus grand que zéro.";
            if ((_tilt.Text ?? "").Trim().Length == 0) _world.AxialTilt = -1;
            else if (ReadNumber(_tilt.Text, out value) && value >= 0 && value <= 90) _world.AxialTilt = value;
            else _fieldProblem = "L'inclinaison de l'axe se donne en degrés, de 0 à 90.";
            if ((_solarYear.Text ?? "").Trim().Length == 0) _world.SolarYear = 0;
            else if (ReadNumber(_solarYear.Text, out value) && value >= 1) _world.SolarYear = value;
            else _fieldProblem = "La durée réelle de l'année est un nombre de jours (ou vide : celle du calendrier).";

            var yearDays = reference.BaseDays;
            for (var i = 0; i < _seasons.Children.Count && i < _world.Seasons.Count; i++)
            {
                var row = _seasons.Children[i] as Grid;
                if (row == null) continue;
                var season = _world.Seasons[i];
                season.Name = (((TextBox)row.Children[1]).Text ?? "").Trim();
                var day = TimelineUi.ParseInt(((TextBox)row.Children[3]).Text, 0);
                var echo = (TextBlock)row.Children[4];
                if (day < 1 || day > 100000) { _fieldProblem = "Le début d'une saison est un jour de l'année : 1, 2, 3…"; echo.Text = ""; continue; }
                season.StartDay = day;
                // Où ce jour tombe dans le calendrier de référence (une année ordinaire).
                echo.Text = day > yearDays ? "au-delà de l'année (" + yearDays + " j)" : "→ " + DayOfYearLabel(reference, day);
            }
            for (var i = 0; i < _moons.Children.Count && i < _world.Moons.Count; i++)
            {
                var row = _moons.Children[i] as Grid;
                if (row == null) continue;
                var moon = _world.Moons[i];
                moon.Name = (((TextBox)row.Children[0]).Text ?? "").Trim();
                if (ReadNumber(((TextBox)row.Children[1]).Text, out value) && value >= 0.05 && value <= 1000000) moon.Period = value;
                else _fieldProblem = "La période d'une lune est un nombre de jours (29,5 pour la nôtre).";
                TimePoint full;
                var written = ((TextBox)row.Children[2]).Text ?? "";
                if (written.Trim().Length == 0) moon.FullAt = 0;
                else if (reference.TryParse(written, out full)) moon.FullAt = reference.DayNumber(TimeMath.Full(full));
                else _fieldProblem = "La date de pleine lune de « " + (moon.Name.Length == 0 ? "la lune " + (i + 1) : moon.Name) + " » ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
            }
        }

        /// <summary>Le jour N d'une année ordinaire, dit « 12 Sel ».</summary>
        private static string DayOfYearLabel(TimeSystem system, int day)
        {
            var rest = day;
            for (var m = 0; m < system.Months.Count; m++)
            {
                var inMonth = Math.Max(1, system.Months[m].Days);
                if (rest <= inMonth) return rest + " " + system.Months[m].Name;
                rest -= inMonth;
            }
            return "jour " + day;
        }

        private void Preview()
        {
            if (_building) return;
            Collect();
            var reference = Reference();
            _reference.Text = _systems.Count == 0 ? "" : "Dates et heures de cette fenêtre : « " + reference.Name + " » (" + reference.BaseDays + " jours par an, " + reference.HoursPerDay + " heures par jour).";
            _problem.Text = _fieldProblem ?? "";
            _problem.IsVisible = _fieldProblem != null;

            // — Ce que la taille permet de dire.
            _facts.Children.Clear();
            var facts = Astro.SizeFacts(_world, reference);
            if (facts.Count == 0) facts.Add("Donnez la circonférence du monde : son rayon, son horizon, la largeur d'une heure de soleil et la durée d'un tour du monde s'en déduisent. Avec l'inclinaison de l'axe : les tropiques, les cercles polaires et la durée du jour selon la latitude.");
            foreach (var fact in facts)
                _facts.Children.Add(new TextBlock { Text = fact, Foreground = _world.HasSize ? Chrome.Ink : Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });

            // — Les saisons et la dérive.
            _seasonHint.Text = _world.Seasons.Count == 0 ? "Le jour 1 est le premier jour de l'année du calendrier de référence."
                : "Le jour 1 est le premier jour de l'année de « " + reference.Name + " » ; la dernière saison de l'année se poursuit au début de la suivante.";
            _driftEcho.Text = Astro.DriftLabel(_world, reference)
                ?? "Vide : l'année solaire dure ce que dure l'année du calendrier (" + Show(TimeMath.MeanYearDays(reference)) + " jours en moyenne), et les saisons reviennent toujours aux mêmes dates. Renseignée, Marabook calcule la dérive du calendrier.";

            // — Les lunes : où elles en sont, quand elles se retrouvent.
            var lines = new List<string>();
            if (_world.Moons.Count > 0 && _fieldProblem == null)
            {
                foreach (var moon in _world.Moons)
                {
                    var next = Astro.NextFull(moon, (long)Math.Floor(moon.FullAt) + 1);
                    lines.Add((moon.Name.Length == 0 ? "Lune" : moon.Name) + " : pleine lune suivante le " + reference.Format(TimeMath.FromDayNumber(reference, next))
                        + " (" + Show(Math.Round(reference.BaseDays / moon.Period, 1)) + " lunaisons par an)");
                }
                if (_world.Moons.Count > 1)
                {
                    var start = (long)Math.Floor(_world.Moons[0].FullAt);
                    var first = Astro.NextConjunction(_world, start);
                    var second = first == null ? null : Astro.NextConjunction(_world, first.Value + 2);
                    lines.Add(first == null ? "Toutes pleines ensemble : pas dans les millénaires qui suivent."
                        : "Toutes pleines ensemble le " + reference.Format(TimeMath.FromDayNumber(reference, first.Value))
                            + (second == null ? "" : ", puis " + TimeSystem.Count(second.Value - first.Value, "jour", "jours") + " plus tard (" + reference.DurationLabel((second.Value - first.Value) / TimeMath.MeanYearDays(reference)) + ")"));
                }
            }
            _moonEcho.Text = string.Join("\n", lines.ToArray());
        }

        private void Accept()
        {
            Collect();
            if (_fieldProblem != null) { _problem.Text = _fieldProblem; _problem.IsVisible = true; return; }
            _result = new WorldChoice { World = _world };
            for (var i = 0; i < _checks.Count; i++) if (_checks[i].IsChecked == true) _result.SystemIds.Add(_systems[i].Id);
            Close();
        }

        /// <summary>Édite une COPIE du monde ; rend le monde et ses calendriers si l'auteur valide, null sinon.</summary>
        public static async Task<WorldChoice> Edit(Window owner, Project project, World world, bool creating)
        {
            var dialog = new WorldDialog(owner, project, creating ? world : world.Clone(), creating);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }

        // ---- sonde
        internal void CheckSystemForProbe(int index, bool on) { _checks[index].IsChecked = on; }
        internal void SetSizeForProbe(string circumference, string tilt) { _circumference.Text = circumference; _tilt.Text = tilt; }
        internal void SetSolarYearForProbe(string days) { _solarYear.Text = days; }
        internal List<string> FactsForProbe() { var facts = new List<string>(); foreach (var child in _facts.Children) facts.Add(((TextBlock)child).Text); return facts; }
        internal string DriftForProbe { get { return _driftEcho.Text ?? ""; } }
        internal string MoonsEchoForProbe { get { return _moonEcho.Text ?? ""; } }
        internal string ProblemForProbe { get { return _problem.IsVisible ? _problem.Text : null; } }
        internal void FourSeasonsForProbe() { Collect(); _world.Seasons.Clear(); _world.Seasons.AddRange(Astro.FourSeasons(Reference().BaseDays)); RebuildSeasons(); Preview(); }
        internal int SeasonRowsForProbe { get { var n = 0; foreach (var child in _seasons.Children) if (child is Grid) n++; return n; } }
        internal string SeasonEchoForProbe(int index) { return ((TextBlock)((Grid)_seasons.Children[index]).Children[4]).Text; }
        internal void AddMoonForProbe(string name, string period, string full)
        {
            Collect();
            _world.Moons.Add(new Moon { Name = name });
            RebuildMoons();
            var row = (Grid)_moons.Children[_moons.Children.Count - 1];
            ((TextBox)row.Children[1]).Text = period;
            ((TextBox)row.Children[2]).Text = full;
            Preview();
        }
        internal void ShowTabForProbe(int index)
        {
            foreach (var tabs in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this))
                if (tabs is TabControl) { ((TabControl)tabs).SelectedIndex = index; return; }
        }
        internal void AcceptForProbe() { Accept(); }
        internal void CancelForProbe() { Close(); }
    }
}
