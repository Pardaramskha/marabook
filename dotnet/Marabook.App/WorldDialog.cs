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
    /// alors la saison et les lunes.
    ///
    /// 15/11 (Rémi) : un monde NEUF arrive prérempli des données du nôtre (un
    /// bandeau vert le dit) ; les calendriers du monde sont des PUCES (une
    /// liste déroulante, « Ajouter », une croix par puce) ; l'onglet Saisons
    /// choisit le calendrier dans lequel la fenêtre compte les jours et écrit
    /// les dates (la référence — plus « le premier coché ») ; un calculateur
    /// donne la circonférence en « tant de fois la Terre » ; l'onglet Soleils
    /// ouvre les systèmes à plusieurs soleils.</summary>
    public class WorldDialog : Window
    {
        private readonly Project _project;
        private readonly World _world;
        private readonly TextBox _name, _circumference, _tilt, _solarYear, _factor;
        private readonly StackPanel _seasons, _moons, _suns, _facts;
        private readonly TextBlock _driftEcho, _moonEcho, _sunEcho, _seasonHint, _problem, _reference, _factorEcho;
        private readonly Button _addSystem, _useFactor, _fixDrift;
        private readonly TextBlock _seasonEven, _fixDriftHint;
        private readonly Border _driftBox;
        private readonly ComboBox _available, _referenceBox;
        private readonly WrapPanel _chips;
        private readonly Control _referenceRow;
        private readonly Border _notice;
        private readonly List<TimeSystem> _systems = new List<TimeSystem>();          // tous ceux du projet
        private readonly List<TimeSystem> _inWorld = new List<TimeSystem>();          // les puces, dans l'ordre d'ajout
        private readonly List<TimeSystem> _availableChoices = new List<TimeSystem>(); // ce que la liste déroulante propose
        private TimeSystem _referenceSystem;
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
            if (creating)
            {
                _notice = TimelineUi.OurWorldNotice("Les données préremplies sont celles de notre monde : la Terre, ses saisons, son Soleil et sa Lune. Gardez ce qui vous convient, changez le reste.");
                root.Children.Add(_notice);
            }
            root.Children.Add(TimelineUi.Label("Nom du monde :", 0));
            _name = new TextBox { Text = world.Name };
            root.Children.Add(_name);

            // Les calendriers du monde, en PUCES : on choisit dans la liste, « Ajouter »
            // en fait une puce et le sort de la liste ; la croix de la puce l'y remet.
            root.Children.Add(TimelineUi.Label("Les calendriers de ce monde :", 10));
            foreach (var system in project.TimeSystems)
            {
                _systems.Add(system);
                if (system.WorldId == world.Id) _inWorld.Add(system);
            }
            _referenceSystem = _systems.Find(delegate(TimeSystem s) { return s.Id == world.ReferenceSystemId; });
            var pick = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
            _available = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "world-system-list" };
            _addSystem = Buttons.IconText("plus-bold", "Ajouter", null, Buttons.Bar, Buttons.Look.Outline);
            _addSystem.Tag = "world-system-add";
            _addSystem.Click += delegate { AddChosenSystem(); };
            Grid.SetColumn(_addSystem, 2);
            pick.Children.Add(_available);
            pick.Children.Add(_addSystem);
            _available.SelectionChanged += delegate { _addSystem.IsVisible = _available.SelectedIndex >= 0; };
            root.Children.Add(pick);
            _chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Tag = "world-system-chips" };
            root.Children.Add(_chips);
            if (_systems.Count == 0)
            {
                pick.IsVisible = false;
                _chips.Children.Add(new TextBlock { Text = "Aucun système temporel : il se crée avec la première frise.", Foreground = Chrome.SoftText, FontSize = 12 });
            }
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
            // LE CALCULATEUR DE CIRCONFÉRENCE (15/11, Rémi) : tant de fois la Terre —
            // « 1,2 » donne 48 090 km, qu'un bouton reporte dans le champ au-dessus.
            size.Children.Add(TimelineUi.Label("Calculateur de circonférence — combien de fois la Terre ?", 10));
            var factorRow = new StackPanel { Orientation = Orientation.Horizontal };
            _factor = new TextBox { Width = 80, Watermark = "ex. 1,2", Tag = "world-factor" };
            factorRow.Children.Add(_factor);
            factorRow.Children.Add(new TextBlock { Text = "×  " + Astro.EarthCircumference.ToString("N0", French) + " km", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            _factorEcho = new TextBlock { Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Tag = "world-factor-echo" };
            factorRow.Children.Add(_factorEcho);
            _useFactor = Buttons.Text("Utiliser cette circonférence", null, Buttons.Compact, Buttons.Look.Outline);
            _useFactor.Tag = "world-factor-use";
            _useFactor.Margin = new Thickness(10, 0, 0, 0);
            _useFactor.IsVisible = false;
            _useFactor.Click += delegate { UseFactor(); };
            factorRow.Children.Add(_useFactor);
            size.Children.Add(factorRow);
            _facts = new StackPanel();
            size.Children.Add(new Border
            {
                Child = _facts, Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(8),
                BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Background = Chrome.FieldBg, Tag = "world-facts"
            });
            tabs.Items.Add(new TabItem { Header = "Taille du monde", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = size } });

            // ------------------------------------------------------------ saisons
            var seasons = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            // LE CALENDRIER DE RÉFÉRENCE (15/11, Rémi) : celui dans lequel les jours
            // sont comptés et les dates écrites — au choix, plus « le premier coché ».
            var referenceRow = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,*"), Margin = new Thickness(0, 0, 0, 10) };
            referenceRow.Children.Add(new TextBlock { Text = "Jours comptés dans le calendrier :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            _referenceBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "world-reference" };
            Grid.SetColumn(_referenceBox, 2);
            referenceRow.Children.Add(_referenceBox);
            _referenceBox.SelectionChanged += delegate
            {
                if (_building) return;
                var choices = ReferenceChoices();
                var index = _referenceBox.SelectedIndex;
                if (index < 0 || index >= choices.Count || ReferenceEquals(choices[index], Reference())) return;
                // Sans regarnir les listes : vider cette liste depuis son propre SelectionChanged fait lever Avalonia.
                var chosen = choices[index];
                Rereference(delegate { _referenceSystem = chosen; }, false);
            };
            _referenceRow = referenceRow;
            seasons.Children.Add(referenceRow);
            seasons.Children.Add(new TextBlock { Text = "Les saisons, et le jour de l'année où chacune commence :", Foreground = Chrome.Ink });
            // Ce que durerait chaque saison si toutes duraient autant (16/11, Rémi).
            _seasonEven = TimelineUi.Hint("");
            _seasonEven.Tag = "world-season-even";
            seasons.Children.Add(_seasonEven);
            _seasons = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            seasons.Children.Add(_seasons); // l'onglet défile déjà : pas de second ascenseur
            var seasonButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            var addSeason = Buttons.IconText("plus-bold", "Ajouter une saison", null, Buttons.Compact, Buttons.Look.Outline);
            addSeason.Click += delegate { Collect(); _world.Seasons.Add(new Season { Name = "", StartDay = 1 }); RebuildSeasons(); Preview(); };
            var four = Buttons.Text("Quatre saisons égales", null, Buttons.Compact, Buttons.Look.Calm);
            four.Tag = "world-four-seasons";
            four.Click += delegate { FourSeasons(); };
            seasonButtons.Children.Add(addSeason);
            seasonButtons.Children.Add(four);
            seasons.Children.Add(seasonButtons);
            _seasonHint = TimelineUi.Hint("");
            seasons.Children.Add(_seasonHint);
            seasons.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 10, 0, 8) });
            // LE GLISSEMENT DES SAISONS (16/11, Rémi) : détecté d'avance, expliqué, corrigible.
            seasons.Children.Add(new TextBlock { Text = "Glissement des saisons au fil des années", Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var solar = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,130,8,*") };
            _solarYear = new TextBox { Text = world.SolarYear > 0 ? Show(world.SolarYear) : "", Watermark = "celle du calendrier", Tag = "world-solar-year" };
            Row(solar, 0, "Durée réelle de l'année :", _solarYear, "jours");
            seasons.Children.Add(solar);
            _driftEcho = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "world-drift" };
            _fixDrift = Buttons.Text("Supprimer le glissement", null, Buttons.Compact, Buttons.Look.Outline);
            _fixDrift.Tag = "world-fix-drift";
            _fixDrift.HorizontalAlignment = HorizontalAlignment.Left;
            _fixDrift.Margin = new Thickness(0, 8, 0, 0);
            _fixDrift.Click += delegate { _solarYear.Text = ""; }; // l'année réelle redevient celle du calendrier
            _fixDriftHint = TimelineUi.Hint("Le bouton cale l'année réelle du monde sur celle du calendrier : les saisons reviennent alors aux mêmes dates chaque année.");
            var driftBody = new StackPanel();
            driftBody.Children.Add(_driftEcho);
            driftBody.Children.Add(_fixDrift);
            driftBody.Children.Add(_fixDriftHint);
            _driftBox = new Border { Child = driftBody, Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Tag = "world-drift-box" };
            seasons.Children.Add(_driftBox);
            tabs.Items.Add(new TabItem { Header = "Saisons", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = seasons } });

            // ------------------------------------------------------------ soleils
            // D'AUTRES SYSTÈMES PLANÉTAIRES (15/11, Rémi) : le premier soleil fait le
            // jour et les saisons ; les suivants sont ses compagnons (Astro.Sun).
            var suns = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            suns.Children.Add(new TextBlock { Text = "Les soleils :", Foreground = Chrome.Ink });
            var sunHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,190,8,26"), Margin = new Thickness(0, 8, 0, 2) };
            sunHead.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.SoftText, FontSize = 11 });
            var sunPeriodHead = new TextBlock { Text = "Période (jours)", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(sunPeriodHead, 2);
            var sunHighHead = new TextBlock { Text = "Au plus haut à minuit, le…", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(sunHighHead, 4);
            sunHead.Children.Add(sunPeriodHead);
            sunHead.Children.Add(sunHighHead);
            suns.Children.Add(sunHead);
            _suns = new StackPanel { Spacing = 4 };
            suns.Children.Add(new ScrollViewer { MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _suns });
            var addSun = Buttons.IconText("plus-bold", "Ajouter un soleil", null, Buttons.Compact, Buttons.Look.Outline);
            addSun.HorizontalAlignment = HorizontalAlignment.Left;
            addSun.Margin = new Thickness(0, 6, 0, 0);
            addSun.Tag = "world-add-sun";
            addSun.Click += delegate { Collect(); _world.Suns.Add(new Sun { Name = "" }); RebuildSuns(); Preview(); };
            suns.Children.Add(addSun);
            suns.Children.Add(TimelineUi.Hint("Le premier soleil fait le jour et les saisons. Un compagnon sans période se lève et se couche avec lui : deux soleils dans le même ciel. Avec une période, il fait le tour du ciel : certaines nuits restent claires, d'autres sont noires."));
            _sunEcho = TimelineUi.Hint("");
            _sunEcho.Tag = "world-suns-echo";
            suns.Children.Add(_sunEcho);
            tabs.Items.Add(new TabItem { Header = "Soleils", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = suns } });

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

            FillSystems();
            RebuildSeasons();
            RebuildSuns();
            RebuildMoons();
            _building = false;
            foreach (var box in new[] { _name, _circumference, _tilt, _solarYear, _factor }) box.TextChanged += delegate { Preview(); };
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

        /// <summary>Les calendriers parmi lesquels la référence se choisit : ceux du monde ; sans aucun, ceux du projet.</summary>
        private List<TimeSystem> ReferenceChoices()
        {
            return _inWorld.Count > 0 ? _inWorld : _systems;
        }

        /// <summary>Le calendrier de référence : celui choisi à l'onglet Saisons ;
        /// à défaut le premier du monde, sinon le premier du projet, sinon celui de notre monde.</summary>
        private TimeSystem Reference()
        {
            var choices = ReferenceChoices();
            if (_referenceSystem != null && choices.Contains(_referenceSystem)) return _referenceSystem;
            return choices.Count > 0 ? choices[0] : Fallback;
        }

        private static readonly TimeSystem Fallback = TimeSystem.Gregorian();

        /// <summary>Change ce dont dépend la référence (les puces, le choix) : les
        /// dates écrites dans les lignes sont d'abord relues dans l'ANCIENNE
        /// référence, puis réécrites dans la nouvelle.</summary>
        private void Rereference(Action change, bool systemsChanged = true)
        {
            Collect();
            change();
            if (systemsChanged) FillSystems();
            RebuildSeasons();
            RebuildSuns();
            RebuildMoons();
            Preview();
        }

        // ---------------------------------------------------------------- les puces

        /// <summary>La liste déroulante (les calendriers pas encore dans ce
        /// monde), les puces (ceux qui y sont) et le choix de la référence.</summary>
        private void FillSystems()
        {
            var was = _building;
            _building = true;
            if (_systems.Count > 0)
            {
                _available.Items.Clear();
                _availableChoices.Clear();
                foreach (var system in _systems)
                {
                    if (_inWorld.Contains(system)) continue;
                    var other = system.WorldId != null && system.WorldId != _world.Id ? Astro.WorldOf(_project, system) : null;
                    _availableChoices.Add(system);
                    _available.Items.Add(system.Name + (other != null ? " (aujourd'hui : " + other.Name + ")" : ""));
                }
                _available.SelectedIndex = -1;
                _available.IsEnabled = _availableChoices.Count > 0;
                _available.PlaceholderText = _availableChoices.Count > 0 ? "Choisir un calendrier…" : "Tous les calendriers du projet vivent dans ce monde";
                _addSystem.IsVisible = false;

                _chips.Children.Clear();
                foreach (var system in _inWorld) _chips.Children.Add(Chip(system));
            }
            var choices = ReferenceChoices();
            _referenceBox.Items.Clear();
            foreach (var system in choices) _referenceBox.Items.Add(system.Name);
            _referenceBox.SelectedIndex = choices.IndexOf(Reference());
            _referenceRow.IsVisible = choices.Count > 1;
            _building = was;
        }

        private Border Chip(TimeSystem system)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = system.Name, Foreground = Chrome.Ink, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            var remove = Buttons.Icon("x-bold", null, 20, Buttons.Look.Calm);
            remove.Content = Icons.Make("x-bold", 10, Chrome.SoftText); // une croix à la mesure de la puce
            remove.Margin = new Thickness(5, 0, 0, 0);
            remove.Click += delegate { Rereference(delegate { _inWorld.Remove(system); }); };
            row.Children.Add(remove);
            return new Border
            {
                Child = row, Padding = new Thickness(11, 2, 3, 2), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(13),
                Background = Chrome.FieldBg, BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Tag = "world-system-chip"
            };
        }

        private void AddChosenSystem()
        {
            var index = _available.SelectedIndex;
            if (index < 0 || index >= _availableChoices.Count) return;
            var system = _availableChoices[index];
            Rereference(delegate { _inWorld.Add(system); });
        }

        // ---------------------------------------------------- calculateur, saisons

        /// <summary>Le facteur du calculateur, lu ; zéro s'il est vide ou illisible.</summary>
        private double FactorResult()
        {
            double factor;
            return ReadNumber(_factor.Text, out factor) ? Astro.ScaledCircumference(factor) : 0;
        }

        private void UseFactor()
        {
            var result = FactorResult();
            if (result > 0) _circumference.Text = Show(result);
        }

        /// <summary>« Quatre saisons égales » : des quarts d'année, la première
        /// calée sur le début de NOTRE printemps (le 20 mars, rapporté à l'année
        /// du calendrier de référence).</summary>
        private void FourSeasons()
        {
            Collect();
            var year = Reference().BaseDays;
            _world.Seasons.Clear();
            _world.Seasons.AddRange(Astro.FourSeasons(year, Astro.SpringDay(year)));
            RebuildSeasons();
            Preview();
        }

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
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,8,*,8,Auto,6,70,8,150,8,26"), RowDefinitions = new RowDefinitions("Auto,Auto"), Tag = "world-season" };
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
                // Sous la saison : ce qu'elle dure, et sa part de l'année (16/11, Rémi) — rempli par Preview.
                var detail = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11.5, Margin = new Thickness(2, 2, 0, 0), Tag = "world-season-detail" };
                Grid.SetRow(detail, 1);
                Grid.SetColumn(detail, 2);
                Grid.SetColumnSpan(detail, 7);
                row.Children.Add(detail);
                _seasons.Children.Add(row);
            }
            if (_world.Seasons.Count == 0) _seasons.Children.Add(new TextBlock { Text = "Aucune saison.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
        }

        private void RebuildSuns()
        {
            var was = _building;
            _building = true;
            _suns.Children.Clear();
            var reference = Reference();
            for (var i = 0; i < _world.Suns.Count; i++)
            {
                var sun = _world.Suns[i];
                var first = i == 0;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,190,8,26"), Tag = "world-sun" };
                var name = new TextBox { Text = sun.Name, Watermark = first ? "Nom du soleil" : "Nom du compagnon" };
                // Le premier soleil n'a ni période ni date : ses champs restent là (la lecture compte les enfants), cachés.
                var period = new TextBox { Text = sun.Period > 0 ? Show(sun.Period) : "", Watermark = "avec le premier", IsVisible = !first };
                Grid.SetColumn(period, 2);
                var high = new TextBox { Text = sun.Period > 0 ? reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(sun.HighAt))) : "", Watermark = reference.Format(new TimePoint(0, 1, 1)), IsVisible = !first };
                Grid.SetColumn(high, 4);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 6);
                var captured = sun;
                remove.Click += delegate { Collect(); _world.Suns.Remove(captured); RebuildSuns(); Preview(); };
                name.TextChanged += delegate { Preview(); };
                period.TextChanged += delegate { Preview(); };
                high.TextChanged += delegate { Preview(); };
                row.Children.Add(name);
                row.Children.Add(period);
                row.Children.Add(high);
                row.Children.Add(remove);
                if (first)
                {
                    var role = new TextBlock { Text = "fait le jour et les saisons", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(role, 2);
                    Grid.SetColumnSpan(role, 3);
                    row.Children.Add(role);
                }
                _suns.Children.Add(row);
            }
            if (_world.Suns.Count == 0) _suns.Children.Add(new TextBlock { Text = "Aucun soleil renseigné.", Foreground = Chrome.SoftText, FontSize = 12 });
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
            for (var i = 0; i < _suns.Children.Count && i < _world.Suns.Count; i++)
            {
                var row = _suns.Children[i] as Grid;
                if (row == null) continue;
                var sun = _world.Suns[i];
                sun.Name = (((TextBox)row.Children[0]).Text ?? "").Trim();
                if (i == 0) continue; // le premier soleil : un nom, rien d'autre
                var label = sun.Name.Length == 0 ? "le soleil " + (i + 1) : sun.Name;
                var period = (((TextBox)row.Children[1]).Text ?? "").Trim();
                if (period.Length == 0) { sun.Period = 0; sun.HighAt = 0; continue; }
                if (ReadNumber(period, out value) && value >= 0.05 && value <= 10000000) sun.Period = value;
                else { _fieldProblem = "La période d'un soleil est un nombre de jours (vide : il se lève et se couche avec le premier)."; continue; }
                TimePoint high;
                var written = ((TextBox)row.Children[2]).Text ?? "";
                if (written.Trim().Length == 0) sun.HighAt = 0;
                else if (reference.TryParse(written, out high)) sun.HighAt = reference.DayNumber(TimeMath.Full(high));
                else _fieldProblem = "La date où « " + label + " » culmine à minuit ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
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
            _reference.Text = _systems.Count == 0 ? ""
                : (_inWorld.Count == 0 ? "Aucun calendrier dans ce monde pour l'instant : ses saisons et ses lunes ne s'afficheront nulle part. " : "")
                    + "Dates et heures de cette fenêtre : « " + reference.Name + " » (" + reference.BaseDays + " jours par an, " + reference.HoursPerDay + " heures par jour)"
                    + (ReferenceChoices().Count > 1 ? " — se choisit à l'onglet Saisons." : ".");
            _problem.Text = _fieldProblem ?? "";
            _problem.IsVisible = _fieldProblem != null;

            // — Le calculateur de circonférence.
            var scaled = FactorResult();
            var typed = (_factor.Text ?? "").Trim().Length > 0;
            _factorEcho.Text = scaled > 0 ? "=  " + scaled.ToString("N0", French) + " km" : (typed ? "un nombre, plus grand que zéro" : "");
            _factorEcho.Foreground = scaled > 0 || !typed ? Chrome.Ink : Chrome.Danger;
            _useFactor.IsVisible = scaled > 0;

            // — Les soleils : qui fait le jour, qui éclaire les nuits.
            var sunLines = new List<string>();
            if (_world.Suns.Count > 0 && _fieldProblem == null)
            {
                var main = _world.Suns[0].Name.Length == 0 ? "le premier soleil" : _world.Suns[0].Name;
                for (var i = 1; i < _world.Suns.Count; i++)
                {
                    var sun = _world.Suns[i];
                    var label = sun.Name.Length == 0 ? "Soleil " + (i + 1) : sun.Name;
                    if (sun.Period <= 0) { sunLines.Add(label + " se lève et se couche avec " + main + " : deux soleils dans le même ciel de jour."); continue; }
                    var high = (long)Math.Floor(sun.HighAt);
                    var years = sun.Period / TimeMath.MeanYearDays(reference);
                    sunLines.Add(label + " : au plus haut à minuit le " + reference.Format(TimeMath.FromDayNumber(reference, high))
                        + " (nuits claires pendant environ " + TimeSystem.Count(Math.Max(1, (long)Math.Round(sun.Period / 4)), "jour", "jours") + "), auprès de " + main + " le "
                        + reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(sun.HighAt + sun.Period / 2))) + " (nuits noires), de retour le "
                        + reference.Format(TimeMath.FromDayNumber(reference, Astro.NextHigh(sun, high + 1)))
                        + (years >= 1 ? " — un tour en " + reference.DurationLabel(years) + "." : "."));
                }
            }
            _sunEcho.Text = string.Join("\n", sunLines.ToArray());

            // — Ce que la taille permet de dire.
            _facts.Children.Clear();
            var facts = Astro.SizeFacts(_world, reference);
            if (facts.Count == 0) facts.Add("Donnez la circonférence du monde : son rayon, son horizon, la largeur d'une heure de soleil et la durée d'un tour du monde s'en déduisent. Avec l'inclinaison de l'axe : les tropiques, les cercles polaires et la durée du jour selon la latitude.");
            foreach (var fact in facts)
                _facts.Children.Add(new TextBlock { Text = fact, Foreground = _world.HasSize ? Chrome.Ink : Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });

            // — Les saisons et la dérive.
            _seasonHint.Text = _world.Seasons.Count == 0 ? "Le jour 1 est le premier jour de l'année du calendrier de référence."
                : "Le jour 1 est le premier jour de l'année de « " + reference.Name + " » ; la dernière saison de l'année se poursuit au début de la suivante.";
            // La durée de chaque saison et sa part de l'année ; la moyenne si toutes duraient autant.
            var seasonYear = _world.SolarYear > 0 ? _world.SolarYear : reference.BaseDays;
            var lengths = Astro.SeasonLengths(_world, seasonYear);
            for (var i = 0; i < _seasons.Children.Count && i < lengths.Length; i++)
            {
                var row = _seasons.Children[i] as Grid;
                if (row != null && row.Children.Count > 6) ((TextBlock)row.Children[6]).Text = "Dure " + Astro.SeasonShare(lengths[i], seasonYear);
            }
            _seasonEven.Text = Astro.EvenSeasons(_world.Seasons.Count, seasonYear);
            _seasonEven.IsVisible = _world.Seasons.Count > 0;

            // Le glissement : ce qui le cause, ce qu'il donne, comment l'ôter.
            var report = Astro.DriftReport(_world, reference);
            var drifting = report.Count > 0;
            _driftEcho.Text = drifting ? Astro.DriftLabel(_world, reference) + "\n" + string.Join("\n", report.ToArray())
                : _world.SolarYear > 0 ? "Aucun glissement : l'année de « " + reference.Name + " » dure exactement l'année réelle du monde."
                : "Aucun glissement : sans durée réelle, l'année du monde est celle du calendrier (" + Show(TimeMath.MeanYearDays(reference)) + " jours" + (reference.Leaps.Count > 0 ? " en moyenne" : "")
                    + ") et les saisons reviennent aux mêmes dates chaque année. Donnez une durée réelle pour voir, d'avance, de combien le calendrier glisserait.";
            var tone = drifting ? Chrome.Danger.Color : Chrome.Ok.Color;
            _driftBox.Background = new SolidColorBrush(tone, 0.10);
            _driftBox.BorderBrush = new SolidColorBrush(tone, 0.55);
            _fixDrift.IsVisible = drifting;
            _fixDriftHint.IsVisible = drifting;

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
            var reference = Reference();
            _world.ReferenceSystemId = _inWorld.Contains(reference) ? reference.Id : null;
            _result = new WorldChoice { World = _world };
            foreach (var system in _inWorld) _result.SystemIds.Add(system.Id);
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
        internal void CheckSystemForProbe(int index, bool on)
        {
            var system = _systems[index];
            if (on == _inWorld.Contains(system)) return;
            Rereference(delegate { if (on) _inWorld.Add(system); else _inWorld.Remove(system); });
        }
        internal bool NoticeShownForProbe { get { return _notice != null && _notice.IsVisible; } }
        internal string SolarYearForProbe { get { return _solarYear.Text ?? ""; } }
        internal string SeasonEvenForProbe { get { return _seasonEven.IsVisible ? _seasonEven.Text ?? "" : ""; } }
        internal string SeasonDetailForProbe(int index) { return ((TextBlock)((Grid)_seasons.Children[index]).Children[6]).Text ?? ""; }
        internal bool FixDriftShownForProbe { get { return _fixDrift.IsVisible; } }
        internal void FixDriftForProbe() { _solarYear.Text = ""; }
        internal int ChipCountForProbe { get { var n = 0; foreach (var child in _chips.Children) if (child is Border) n++; return n; } }
        internal int AvailableCountForProbe { get { return _available.Items.Count; } }
        internal bool AddButtonShownForProbe { get { return _addSystem.IsVisible; } }
        internal void PickAvailableForProbe(int index) { _available.SelectedIndex = index; }
        internal void AddPickedForProbe() { AddChosenSystem(); }
        internal bool ReferenceRowShownForProbe { get { return _referenceRow.IsVisible; } }
        internal string ReferenceNameForProbe { get { return Reference().Name; } }
        internal void SetReferenceForProbe(int index) { _referenceBox.SelectedIndex = index; }
        internal string CircumferenceForProbe { get { return _circumference.Text ?? ""; } }
        internal void SetFactorForProbe(string factor) { _factor.Text = factor; }
        internal string FactorEchoForProbe { get { return _factorEcho.Text ?? ""; } }
        internal bool UseFactorShownForProbe { get { return _useFactor.IsVisible; } }
        internal void UseFactorForProbe() { UseFactor(); }
        internal int MoonRowsForProbe { get { var n = 0; foreach (var child in _moons.Children) if (child is Grid) n++; return n; } }
        internal int SunRowsForProbe { get { var n = 0; foreach (var child in _suns.Children) if (child is Grid) n++; return n; } }
        internal string SunsEchoForProbe { get { return _sunEcho.Text ?? ""; } }
        internal string SeasonDayForProbe(int index) { return ((TextBox)((Grid)_seasons.Children[index]).Children[3]).Text; }
        internal string MoonFullForProbe(int index) { return ((TextBox)((Grid)_moons.Children[index]).Children[2]).Text; }
        internal void ClearSkyForProbe() { Collect(); _world.Seasons.Clear(); _world.Moons.Clear(); RebuildSeasons(); RebuildMoons(); Preview(); }
        internal void AddSunForProbe(string name, string period, string high)
        {
            Collect();
            _world.Suns.Add(new Sun { Name = name });
            RebuildSuns();
            var row = (Grid)_suns.Children[_suns.Children.Count - 1];
            ((TextBox)row.Children[1]).Text = period;
            ((TextBox)row.Children[2]).Text = high;
            Preview();
        }
        internal void SetSizeForProbe(string circumference, string tilt) { _circumference.Text = circumference; _tilt.Text = tilt; }
        internal void SetSolarYearForProbe(string days) { _solarYear.Text = days; }
        internal List<string> FactsForProbe() { var facts = new List<string>(); foreach (var child in _facts.Children) facts.Add(((TextBlock)child).Text); return facts; }
        internal string DriftForProbe { get { return _driftEcho.Text ?? ""; } }
        internal string MoonsEchoForProbe { get { return _moonEcho.Text ?? ""; } }
        internal string ProblemForProbe { get { return _problem.IsVisible ? _problem.Text : null; } }
        internal void FourSeasonsForProbe() { FourSeasons(); }
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
