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
        private readonly StackPanel _seasons, _moons, _suns, _facts, _phenomena, _sunBehaviors, _moonBehaviors;
        private readonly TextBlock _phenomenonEcho;
        private readonly Control _moonHead, _phenomenonHead;
        private readonly Border _noCalendar;
        private readonly Accordion _factorAccordion;
        // LE PLANÉTARIUM (16/11 soir) : la liste du placement stellaire, la vue des orbites, son jour.
        private readonly StackPanel _bodies;
        private readonly PlanetariumView _planetarium;
        private readonly Slider _planetSlider;
        private readonly TextBlock _planetDate, _planetNotes;
        private readonly ToggleButton _planetPlay;
        private Avalonia.Threading.DispatcherTimer _planetTimer;
        private long _planetStart;      // le numéro de jour du début de l'année montrée
        private bool _planetNavigated;  // l'auteur a changé d'année : on ne recale plus la vue sur les dates saisies
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
        private GuidesEditor _guides;
        internal GuidesEditor GuidesForProbe { get { return _guides; } }
        private bool _building = true;

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private WorldDialog(Window owner, Project project, World world, bool creating)
        {
            _project = project;
            _world = world;
            TimelineUi.Dress(this, owner, creating ? "Nouveau monde" : "Monde — " + world.Name);
            Width = 720;
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
            root.Children.Add(WithLook(_name, delegate { return _world.Look; }, delegate(BodyLook look) { _world.Look = look; }, BodyLookDialog.WorldKind,
                delegate { return (_name.Text ?? "").Trim().Length == 0 ? "Monde" : _name.Text.Trim(); }));

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
                _chips.Children.Add(new TextBlock { Text = "Aucun système temporel dans le projet : créez-en un d'abord (Astrolabe, étape 1).", Foreground = Chrome.SoftText, FontSize = 12 });
            }
            _reference = TimelineUi.Hint("");
            root.Children.Add(_reference);
            _noCalendar = AstrolabeUi.Notice("Aucun calendrier ne vit encore dans ce monde : ses saisons, ses lunes et ses phénomènes ne s'afficheront nulle part. Ajoutez-en un ci-dessus — c'est lui qui montrera ce ciel dans sa vue Calendrier et sur ses frises.", false, "world-no-calendar");
            _noCalendar.Margin = new Thickness(0, 8, 0, 0);
            _noCalendar.IsVisible = false;
            root.Children.Add(_noCalendar);

            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), Height = 480 };

            // ------------------------------------------------------------- taille
            var size = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var sizeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,130,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            _circumference = new TextBox { Text = world.HasSize ? Show(world.Circumference) : "", Watermark = "ex. 40 075", Tag = "world-circumference" };
            _tilt = new TextBox { Text = world.HasTilt ? Show(world.AxialTilt) : "", Watermark = "ex. 23,4", Tag = "world-tilt" };
            Row(sizeGrid, 0, "Circonférence à l'équateur :", _circumference, "km");
            Row(sizeGrid, 1, "Inclinaison de l'axe :", _tilt, "degrés");
            size.Children.Add(AstrolabeUi.Section("Mesures", "Les deux nombres dont tout le reste se déduit.", sizeGrid, "world-section-measures"));
            // LE CALCULATEUR DE CIRCONFÉRENCE (15/11, Rémi) : tant de fois la Terre —
            // « 1,2 » donne 48 090 km, qu'un bouton reporte dans le champ au-dessus.
            // 16/11 : replié dans son accordéon — un outil d'appoint, pas un champ.
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
            var factorBody = new StackPanel();
            factorBody.Children.Add(new TextBlock { Text = "Combien de fois la Terre ? Le résultat se reporte dans la circonférence, au-dessus.", Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            factorBody.Children.Add(factorRow);
            _factorAccordion = new Accordion("Calculateur de circonférence", "en nombre de fois la Terre", factorBody, false, "world-factor-accordion");
            size.Children.Add(_factorAccordion);
            // CE QUE LA TAILLE PERMET DE DIRE (16/11, Rémi : « un unique bloc de texte massif ») :
            // des groupes titrés, et dans chacun des tuiles — un libellé, une valeur en grand, une précision.
            _facts = new StackPanel { Tag = "world-facts" };
            size.Children.Add(_facts);
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
            var seasonBody = new StackPanel();
            // Ce que durerait chaque saison si toutes duraient autant (16/11, Rémi).
            _seasonEven = TimelineUi.Hint("");
            _seasonEven.Margin = new Thickness(0, 0, 0, 2);
            _seasonEven.Tag = "world-season-even";
            seasonBody.Children.Add(_seasonEven);
            _seasons = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            seasonBody.Children.Add(_seasons); // l'onglet défile déjà : pas de second ascenseur
            var seasonButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var addSeason = Buttons.IconText("plus-bold", "Ajouter une saison", null, Buttons.Compact, Buttons.Look.Outline);
            addSeason.Click += delegate { Collect(); _world.Seasons.Add(new Season { Name = "", StartDay = 1 }); RebuildSeasons(); Preview(); };
            var four = Buttons.Text("Quatre saisons égales", null, Buttons.Compact, Buttons.Look.Calm);
            four.Tag = "world-four-seasons";
            four.Click += delegate { FourSeasons(); };
            seasonButtons.Children.Add(addSeason);
            seasonButtons.Children.Add(four);
            seasonBody.Children.Add(seasonButtons);
            _seasonHint = TimelineUi.Hint("");
            seasonBody.Children.Add(_seasonHint);
            seasons.Children.Add(AstrolabeUi.Section("Les saisons", "Chacune commence un jour de l'année ; la flèche dit où ce jour tombe dans le calendrier.", seasonBody, "world-section-seasons"));
            // LE GLISSEMENT DES SAISONS (16/11, Rémi) : détecté d'avance, expliqué, corrigible.
            var driftSection = new StackPanel();
            var solar = new Grid { ColumnDefinitions = new ColumnDefinitions("230,8,130,8,*") };
            _solarYear = new TextBox { Text = world.SolarYear > 0 ? Show(world.SolarYear) : "", Watermark = "celle du calendrier", Tag = "world-solar-year" };
            Row(solar, 0, "Durée réelle de l'année :", _solarYear, "jours");
            driftSection.Children.Add(solar);
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
            driftSection.Children.Add(_driftBox);
            seasons.Children.Add(AstrolabeUi.Section("Glissement des saisons au fil des années", "Si l'année réelle du monde ne dure pas exactement celle du calendrier, les saisons se décalent.", driftSection, "world-section-drift"));
            tabs.Items.Add(new TabItem { Header = "Saisons", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = seasons } });

            // ------------------------------------------------------------ soleils
            // D'AUTRES SYSTÈMES PLANÉTAIRES (15/11, Rémi). 16/11 : chaque soleil dit son
            // ORBITE — « centrique » (au milieu du système, comme le nôtre) ou autour d'un
            // autre soleil — et la fenêtre dit ce que cet agencement déclenche.
            var suns = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var sunBody = new StackPanel();
            _suns = new StackPanel { Spacing = 6 };
            sunBody.Children.Add(_suns);
            var addSun = Buttons.IconText("plus-bold", "Ajouter un soleil", null, Buttons.Compact, Buttons.Look.Outline);
            addSun.HorizontalAlignment = HorizontalAlignment.Left;
            addSun.Margin = new Thickness(0, 8, 0, 0);
            addSun.Tag = "world-add-sun";
            addSun.Click += delegate { Collect(); _world.Suns.Add(new Sun { Name = "" }); RebuildSuns(); Preview(); };
            sunBody.Children.Add(addSun);
            suns.Children.Add(AstrolabeUi.Section("Les soleils", "Un soleil centrique est au milieu du système : le premier fait le jour et les saisons. Un soleil peut aussi orbiter autour d'un autre — donnez-lui alors le temps qu'il met à faire le tour du ciel.", sunBody, "world-section-suns"));
            var sunSky = new StackPanel();
            _sunBehaviors = new StackPanel { Tag = "world-sun-behaviors" };
            sunSky.Children.Add(_sunBehaviors);
            _sunEcho = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "world-suns-echo" };
            sunSky.Children.Add(_sunEcho);
            suns.Children.Add(AstrolabeUi.Section("Ce que ce ciel déclenche", "Ce que le nombre et l'agencement des soleils changent dans le monde.", sunSky, "world-section-sun-sky"));
            tabs.Items.Add(new TabItem { Header = "Soleils", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = suns } });

            // -------------------------------------------------------------- lunes
            var moons = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var moonBody = new StackPanel();
            var moonHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,100,8,190,8,26"), Margin = new Thickness(13, 0, 9, 3) };
            moonHead.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.SoftText, FontSize = 11 });
            var periodHead = new TextBlock { Text = "Période (jours)", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(periodHead, 2);
            var fullHead = new TextBlock { Text = "Une pleine lune, le…", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(fullHead, 4);
            moonHead.Children.Add(periodHead);
            moonHead.Children.Add(fullHead);
            _moonHead = moonHead;
            moonBody.Children.Add(moonHead);
            _moons = new StackPanel { Spacing = 6 };
            moonBody.Children.Add(_moons);
            var addMoon = Buttons.IconText("plus-bold", "Ajouter un satellite", null, Buttons.Compact, Buttons.Look.Outline);
            addMoon.HorizontalAlignment = HorizontalAlignment.Left;
            addMoon.Margin = new Thickness(0, 8, 0, 0);
            addMoon.Click += delegate { Collect(); _world.Moons.Add(new Moon { Name = "", Period = 29.53, FullAt = 0 }); RebuildMoons(); Preview(); };
            moonBody.Children.Add(addMoon);
            moons.Children.Add(AstrolabeUi.Section("Les satellites", "Les lunes et autres satellites du monde. La période va d'une pleine lune à la suivante ; la date d'une pleine lune, écrite dans le calendrier de référence, cale toutes les autres. Une lune orbite le monde — ou une autre lune.", moonBody, "world-section-moons"));
            var moonSky = new StackPanel();
            _moonBehaviors = new StackPanel { Tag = "world-moon-behaviors" };
            moonSky.Children.Add(_moonBehaviors);
            _moonEcho = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "world-moons-echo" };
            moonSky.Children.Add(_moonEcho);
            moons.Children.Add(AstrolabeUi.Section("Ce que ce ciel déclenche", "Éclipses, lunes rousses, lunes bleues, lunes pleines ensemble : ce que vos lunes provoquent, et quand.", moonSky, "world-section-moon-sky"));
            tabs.Items.Add(new TabItem { Header = "Satellites", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = moons } });

            // --------------------------------------------------------- phénomènes
            // LES PHÉNOMÈNES PERSONNALISÉS (16/11, Rémi) : ce qui revient dans le ciel à
            // période fixe et que soleils et lunes ne suffisent pas à dire.
            var phenomena = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var phenomenaBody = new StackPanel();
            var phenomenonHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,130,8,190,8,26"), Margin = new Thickness(0, 0, 0, 3) };
            phenomenonHead.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.SoftText, FontSize = 11 });
            var everyHead = new TextBlock { Text = "Revient tous les… (jours)", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(everyHead, 2);
            var atHead = new TextBlock { Text = "Un passage, le…", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(atHead, 4);
            phenomenonHead.Children.Add(everyHead);
            phenomenonHead.Children.Add(atHead);
            _phenomenonHead = phenomenonHead;
            phenomenaBody.Children.Add(phenomenonHead);
            _phenomena = new StackPanel { Spacing = 4 };
            phenomenaBody.Children.Add(_phenomena);
            var addPhenomenon = Buttons.IconText("plus-bold", "Ajouter un phénomène", null, Buttons.Compact, Buttons.Look.Outline);
            addPhenomenon.HorizontalAlignment = HorizontalAlignment.Left;
            addPhenomenon.Margin = new Thickness(0, 8, 0, 0);
            addPhenomenon.Tag = "world-add-phenomenon";
            addPhenomenon.Click += delegate { Collect(); _world.Phenomena.Add(new SkyEvent { Name = "", Period = Math.Max(1, Reference().BaseDays), At = 0 }); RebuildPhenomena(); Preview(); };
            phenomenaBody.Children.Add(addPhenomenon);
            _phenomenonEcho = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Tag = "world-phenomena-echo" };
            phenomenaBody.Children.Add(_phenomenonEcho);
            phenomena.Children.Add(AstrolabeUi.Section("Phénomènes personnalisés", "Une comète, une pluie d'étoiles, la lune rousse d'une légende, un astre errant : un nom, le temps entre deux passages, et la date d'un passage. Le calendrier les marque d'une étoile.", phenomenaBody, "world-section-phenomena"));
            phenomena.Children.Add(TimelineUi.Hint("Les éclipses, les lunes rousses (éclipses totales de lune), les lunes bleues et les lunes pleines ensemble sont calculées d'office à partir de vos lunes : inutile de les ajouter ici."));
            // ------------------------------------------------------- planétarium
            // LE PLACEMENT STELLAIRE ET LE PLANÉTARIUM (16/11 soir, Rémi) : l'ordre des astres en
            // s'éloignant du centre (0 les soleils centriques, puis 1, 2, 3…), les planètes
            // voisines — sans calendrier : tout est vu depuis le monde — et la vue des orbites.
            var planetTab = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            _planetarium = new PlanetariumView { Tag = "world-planetarium" };
            _planetDate = new TextBlock { Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "world-planet-date" };
            _planetNotes = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Tag = "world-planet-notes" };
            _planetSlider = new Slider { Minimum = 0, Maximum = 360, Value = 0, Margin = new Thickness(0, 4, 0, 0), Tag = "world-planet-slider" };
            _planetSlider.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == Slider.ValueProperty && !_building) ShowPlanetarium(); };
            _planetPlay = Buttons.TextToggle("Faire tourner", "Laisser les jours défiler : une année en une vingtaine de secondes", Buttons.Compact, Buttons.Look.Outline);
            _planetPlay.Tag = "world-planet-play";
            _planetPlay.HorizontalAlignment = HorizontalAlignment.Left;
            _planetPlay.IsCheckedChanged += delegate { PlayPlanetarium(_planetPlay.IsChecked == true); };
            var nextYear = Buttons.Text("Année suivante", null, Buttons.Compact, Buttons.Look.Calm);
            nextYear.Click += delegate { _planetNavigated = true; _planetStart += Math.Max(1, (long)Math.Round(_planetSlider.Maximum)); ShowPlanetarium(); };
            var previousYear = Buttons.Text("Année précédente", null, Buttons.Compact, Buttons.Look.Calm);
            previousYear.Click += delegate { _planetNavigated = true; _planetStart -= Math.Max(1, (long)Math.Round(_planetSlider.Maximum)); ShowPlanetarium(); };
            var playRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            playRow.Children.Add(_planetPlay);
            playRow.Children.Add(previousYear);
            playRow.Children.Add(nextYear);
            var watch = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            watch.Children.Add(new TextBlock { Text = "LE JOUR MONTRÉ", Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 4) });
            watch.Children.Add(_planetDate);
            watch.Children.Add(_planetSlider);
            watch.Children.Add(playRow);
            watch.Children.Add(_planetNotes);
            watch.Children.Add(TimelineUi.Hint("Vu de dessus. Les distances ne sont pas à l'échelle : seul l'ordre compte. Une lune est pleine quand elle est à l'opposé du soleil ; un soleil proche s'éclipse quand il s'aligne avec le monde."));
            var sky = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(watch, 1);
            sky.Children.Add(_planetarium);
            sky.Children.Add(watch);
            planetTab.Children.Add(AstrolabeUi.Section("Le planétarium", "Le système, un jour donné : chaque astre à la place que lui donnent sa période et sa date de référence.", sky, "world-section-planetarium"));
            var placement = new StackPanel();
            _bodies = new StackPanel { Spacing = 4, Tag = "world-bodies" };
            placement.Children.Add(_bodies);
            var addPlanet = Buttons.IconText("plus-bold", "Ajouter une planète voisine", null, Buttons.Compact, Buttons.Look.Outline);
            addPlanet.HorizontalAlignment = HorizontalAlignment.Left;
            addPlanet.Margin = new Thickness(0, 8, 0, 0);
            addPlanet.Tag = "world-add-planet";
            addPlanet.Click += delegate { Collect(); _world.Planets.Add(new Planet { Name = "" }); RebuildBodies(); Preview(); };
            placement.Children.Add(addPlanet);
            placement.Children.Add(TimelineUi.Hint("Un soleil centrique reste au rang 0. Un soleil qui orbite AVANT le monde est en orbite rapprochée : vu du monde, il ne quitte jamais son soleil (pas de nuits claires, mais des éclipses solaires pures). APRÈS le monde, il fait le tour du ciel. Une planète voisine n'a pas besoin de calendrier : son année et la date d'un alignement suffisent à dater ses retours — ou rien, si elle n'est là que pour le décor."));
            planetTab.Children.Add(AstrolabeUi.Section("Placement stellaire", "Du centre vers le dehors. Les flèches montent ou descendent un astre d'un rang.", placement, "world-section-placement"));
            tabs.Items.Add(new TabItem { Header = "Planétarium", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = planetTab } });

            tabs.Items.Add(new TabItem { Header = "Phénomènes", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = phenomena } });

            // ------------------------------------------------------------ marqueurs de navigation (lot 13)
            // Les parallèles remarquables du monde : par défaut, ceux que dit l'inclinaison de l'axe.
            _guides = new GuidesEditor(_world);
            var guidesTab = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            guidesTab.Children.Add(AstrolabeUi.Section("Marqueurs de navigation", "L'équateur, les tropiques, les cercles polaires — ou les vôtres. Ils se tracent sur les cartes du monde entier (onglet « Repères de navigation » du rail).", _guides, "world-section-guides"));
            var guidesItem = new TabItem { Header = "Marqueurs", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = guidesTab }, Tag = "world-tab-guides" };
            tabs.Items.Add(guidesItem);
            // L'inclinaison a pu changer dans « Taille du monde » : les marqueurs par défaut la suivent.
            tabs.SelectionChanged += delegate { if (ReferenceEquals(tabs.SelectedItem, guidesItem) && !_building) { Collect(); _guides.Rebuild(); } };

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
            RebuildPhenomena();
            _building = false;
            foreach (var box in new[] { _name, _circumference, _tilt, _solarYear, _factor }) box.TextChanged += delegate { Preview(); };
            _name.TextChanged += delegate { if (!_building) RebuildBodies(); }; // le nom du monde figure au placement stellaire
            RebuildBodies();
            Closed += delegate { PlayPlanetarium(false); };
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
            RebuildPhenomena();
            RebuildBodies();
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

        private static TextBlock Word(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        }

        private static Border Card(Control child, object[] cells, string tag)
        {
            return new Border
            {
                Child = child, Padding = new Thickness(12, 9, 8, 10), CornerRadius = new CornerRadius(8), Background = Chrome.FieldBg,
                BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = cells
            };
        }

        /// <summary>Les cellules d'une ligne (ce que sa carte porte en Tag) ; null pour ce qui n'en est pas une.</summary>
        private static object[] Cells(object child)
        {
            var control = child as Control;
            return control == null ? null : control.Tag as object[];
        }

        private static string SunName(Sun sun, int index) { return string.IsNullOrEmpty(sun.Name) ? "Soleil " + (index + 1) : sun.Name; }
        private static string MoonName(Moon moon, int index) { return string.IsNullOrEmpty(moon.Name) ? "Satellite " + (index + 1) : moon.Name; }

        private List<Sun> OtherSuns(Sun sun)
        {
            var others = new List<Sun>();
            foreach (var other in _world.Suns) if (other != sun) others.Add(other);
            return others;
        }

        private List<Moon> OtherMoons(Moon moon)
        {
            var others = new List<Moon>();
            foreach (var other in _world.Moons) if (other != moon) others.Add(other);
            return others;
        }

        /// <summary>Les choix d'ORBITE de chaque soleil (16/11, Rémi) : « Centrique »,
        /// puis « Orbite {autre soleil} » — regarnis quand un nom change.</summary>
        private void FillSunOrbits()
        {
            var was = _building;
            _building = true;
            foreach (var child in _suns.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var sun = (Sun)cells[0];
                var combo = (ComboBox)cells[2];
                var others = OtherSuns(sun);
                combo.Items.Clear();
                combo.Items.Add("Centrique (au milieu du système)");
                foreach (var other in others) combo.Items.Add("Orbite " + SunName(other, _world.Suns.IndexOf(other)));
                var around = others.FindIndex(delegate(Sun s) { return s.Id == sun.OrbitId; });
                combo.SelectedIndex = around >= 0 ? around + 1 : 0;
                // Rapprochée par défaut : un centrique qui se met à orbiter se place d'abord en deçà du monde.
                ((ComboBox)cells[8]).SelectedIndex = sun.OrbitId == null || Astro.IsInner(_world, sun) ? 0 : 1;
            }
            _building = was;
        }

        private void FillMoonOrbits()
        {
            var was = _building;
            _building = true;
            foreach (var child in _moons.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var moon = (Moon)cells[0];
                var combo = (ComboBox)cells[4];
                var others = OtherMoons(moon);
                combo.Items.Clear();
                combo.Items.Add("Orbite le monde");
                foreach (var other in others) combo.Items.Add("Orbite " + MoonName(other, _world.Moons.IndexOf(other)));
                var around = others.FindIndex(delegate(Moon m) { return m.Id == moon.OrbitId; });
                combo.SelectedIndex = around >= 0 ? around + 1 : 0;
            }
            _building = was;
        }

        /// <summary>PERSONNALISER L'ASTRE (lot 14, Rémi : « bouton à côté du nom ») : le champ du nom, et à sa
        /// droite le bouton qui ouvre l'allure de l'astre — accent, anneaux, inclinaison, distance d'un satellite.</summary>
        private Control WithLook(TextBox name, Func<BodyLook> current, Action<BodyLook> apply, int kind, Func<string> title)
        {
            var button = Buttons.Text("Personnaliser…", kind == BodyLookDialog.WorldKind ? "L'allure du monde sur le globe : la teinte de son atmosphère, ses anneaux"
                : kind == BodyLookDialog.SunKind ? "L'allure de ce soleil : la couleur de son halo, ses anneaux, l'inclinaison de son axe"
                : "L'allure de ce satellite : sa distance, ses anneaux, l'inclinaison de son orbite", Buttons.Compact, Buttons.Look.Outline);
            button.Tag = kind == BodyLookDialog.WorldKind ? "world-look" : kind == BodyLookDialog.SunKind ? "world-sun-look" : "world-moon-look";
            button.Margin = new Thickness(6, 0, 0, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Click += async delegate
            {
                var look = current();
                if (look == null) return;
                var edited = await BodyLookDialog.Ask(this, kind, title(), look.Clone());
                if (edited != null) apply(edited);
            };
            var row = new DockPanel();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(name);
            return row;
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
                var name = new TextBox { Text = sun.Name, Watermark = "Nom du soleil" };
                var lookSun = sun;
                // SA TAILLE (16/11 soir, Rémi) : en « fois notre Soleil » — ce qui décide de ses éclipses solaires pures.
                var sizeWord = Word("Taille ×");
                sizeWord.Margin = new Thickness(0);
                Grid.SetColumn(sizeWord, 2);
                var size = new TextBox { Text = Show(sun.Size), Tag = "world-sun-size" };
                ToolTip.SetTip(size, "Sa largeur, en nombre de fois notre Soleil (1 = 1 392 700 km de diamètre). Entre deux soleils qui se croisent, le plus large cache l'autre en entier.");
                Grid.SetColumn(size, 4);
                var orbit = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "world-sun-orbit" };
                Grid.SetColumn(orbit, 6);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 8);
                var period = new TextBox { Text = sun.Period > 0 ? Show(sun.Period) : "", Watermark = "aucune", Width = 84, Margin = new Thickness(0, 0, 7, 0) };
                var high = new TextBox { Text = sun.Period > 0 ? reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(sun.HighAt))) : "", Watermark = reference.Format(new TimePoint(0, 1, 1)), Width = 180 };
                // Sous le nom, pour un soleil qui orbite : OÙ (en deçà ou au-delà du monde), puis son tour.
                var placement = new ComboBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left, Tag = "world-sun-placement" };
                placement.Items.Add("Orbite rapprochée — plus près du centre que le monde");
                placement.Items.Add("Orbite lointaine — au-delà du monde");
                var wordA = Word("");
                var wordB = Word("");
                var turn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
                turn.Children.Add(wordA);
                turn.Children.Add(period);
                turn.Children.Add(wordB);
                turn.Children.Add(high);
                var orbiting = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
                orbiting.Children.Add(placement);
                orbiting.Children.Add(turn);
                Grid.SetRow(orbiting, 1);
                Grid.SetColumnSpan(orbiting, 9);
                var role = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 6, 0, 0) };
                Grid.SetRow(role, 1);
                Grid.SetColumnSpan(role, 9);
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto,6,62,8,240,8,26"), RowDefinitions = new RowDefinitions("Auto,Auto") };
                grid.Children.Add(WithLook(name, delegate { Collect(); return _world.Suns.Contains(lookSun) ? lookSun.Look : null; }, delegate(BodyLook look) { lookSun.Look = look; Preview(); }, BodyLookDialog.SunKind, delegate { return SunName(lookSun, _world.Suns.IndexOf(lookSun)); }));
                grid.Children.Add(sizeWord);
                grid.Children.Add(size);
                grid.Children.Add(orbit);
                grid.Children.Add(remove);
                grid.Children.Add(orbiting);
                grid.Children.Add(role);
                var captured = sun;
                remove.Click += delegate
                {
                    Collect();
                    _world.Suns.Remove(captured);
                    foreach (var other in _world.Suns) if (other.OrbitId == captured.Id) { other.OrbitId = null; other.Period = 0; other.HighAt = 0; } // plus de soleil à orbiter : centrique
                    RebuildSuns();
                    Preview();
                };
                name.TextChanged += delegate { if (_building) return; Collect(); FillSunOrbits(); RebuildBodies(); Preview(); };
                // Sans regarnir la liste des orbites depuis son propre SelectionChanged (Avalonia lève).
                orbit.SelectionChanged += delegate { if (_building) return; Collect(); RebuildBodies(); Preview(); };
                placement.SelectionChanged += delegate { if (_building) return; Collect(); RebuildBodies(); Preview(); };
                size.TextChanged += delegate { Preview(); };
                period.TextChanged += delegate { Preview(); };
                high.TextChanged += delegate { Preview(); };
                _suns.Children.Add(Card(grid, new object[] { sun, name, orbit, period, high, orbiting, role, size, placement, wordA, wordB }, "sun"));
            }
            if (_world.Suns.Count == 0) _suns.Children.Add(new TextBlock { Text = "Aucun soleil renseigné.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
            FillSunOrbits();
            if (_bodies != null) RebuildBodies();
        }

        // ------------------------------------------------------- le planétarium

        /// <summary>LA LISTE DU PLACEMENT STELLAIRE : un astre par ligne, du centre
        /// vers le dehors — son rang, sa couleur, son nom, ce qu'il est, et les
        /// flèches qui le montent ou le descendent d'un rang. Une planète voisine
        /// s'y nomme et s'y date ; soleils et monde se règlent dans leurs onglets.</summary>
        private void RebuildBodies()
        {
            if (_bodies == null) return;
            var was = _building;
            _building = true;
            _bodies.Children.Clear();
            var reference = Reference();
            var layout = Astro.Layout(_world);
            var movable = layout.FindAll(delegate(SkyBody b) { return b.Movable; });
            foreach (var body in layout)
            {
                var captured = body;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,8,14,8,*,8,26,2,26,8,26"), Tag = "world-body" };
                row.Children.Add(new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = body.Kind == SkyBody.WorldKind ? Chrome.Accent : Brushes.Transparent,
                    BorderBrush = body.Kind == SkyBody.WorldKind ? Chrome.Accent : Chrome.BorderStrong, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = body.Rank.ToString(CultureInfo.InvariantCulture), Foreground = body.Kind == SkyBody.WorldKind ? Brushes.White : Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                Color tint;
                var hex = body.Kind == SkyBody.SunKind ? Astro.ColorOf(body.Sun, _world.Suns.IndexOf(body.Sun)) : body.Kind == SkyBody.PlanetKind ? Astro.ColorOf(body.Planet, _world.Planets.IndexOf(body.Planet)) : null;
                if (hex == null || !Color.TryParse(hex, out tint)) tint = Chrome.Accent.Color;
                var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(dot, 2);
                row.Children.Add(dot);
                Control middle;
                if (body.Kind == SkyBody.PlanetKind)
                {
                    var planet = body.Planet;
                    var name = new TextBox { Text = planet.Name, Watermark = "Nom de la planète", Width = 170, Margin = new Thickness(0, 0, 8, 0) };
                    var period = new TextBox { Text = planet.Period > 0 ? Show(planet.Period) : "", Watermark = "—", Width = 74, Margin = new Thickness(0, 0, 7, 0) };
                    ToolTip.SetTip(period, "La durée de son année, en jours du calendrier de référence. Vide : elle est seulement placée.");
                    var at = new TextBox { Text = planet.Period > 0 ? reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(planet.At))) : "", Watermark = reference.Format(new TimePoint(0, 1, 1)), Width = 150 };
                    ToolTip.SetTip(at, "Un jour où elle est alignée avec le monde et le soleil : au plus près si elle est au-delà du monde, devant le soleil si elle est en deçà.");
                    var fields = new StackPanel { Orientation = Orientation.Horizontal };
                    fields.Children.Add(name);
                    fields.Children.Add(Word("année :"));
                    fields.Children.Add(period);
                    fields.Children.Add(Word("j · alignée le"));
                    fields.Children.Add(at);
                    name.TextChanged += delegate { Preview(); };
                    period.TextChanged += delegate { Preview(); };
                    at.TextChanged += delegate { Preview(); };
                    row.Tag = new object[] { planet, name, period, at };
                    middle = fields;
                }
                else
                {
                    var what = body.Kind == SkyBody.WorldKind ? "votre monde — tout est vu d'ici"
                        : body.Sun.OrbitId == null ? "soleil centrique"
                        : "soleil · orbite " + (Astro.FindSun(_world, body.Sun.OrbitId) == null ? "?" : SunName(Astro.FindSun(_world, body.Sun.OrbitId), _world.Suns.IndexOf(Astro.FindSun(_world, body.Sun.OrbitId))))
                            + (Astro.IsInner(_world, body.Sun) ? " · rapprochée" : " · lointaine");
                    var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    label.Children.Add(new TextBlock { Text = body.Kind == SkyBody.SunKind ? SunName(body.Sun, _world.Suns.IndexOf(body.Sun)) : body.Name, Foreground = Chrome.Ink, FontSize = 13, FontWeight = body.Kind == SkyBody.WorldKind ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
                    label.Children.Add(new TextBlock { Text = "   " + what, Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    middle = label;
                }
                Grid.SetColumn(middle, 4);
                row.Children.Add(middle);
                if (body.Movable)
                {
                    var at = movable.IndexOf(body);
                    var up = Buttons.Icon("caret-up-bold", "Rapprocher du centre", Buttons.Compact, Buttons.Look.Calm);
                    up.IsEnabled = at > 0;
                    up.Click += delegate { MoveBody(captured.Id, -1); };
                    Grid.SetColumn(up, 6);
                    var down = Buttons.Icon("caret-down-bold", "Éloigner du centre", Buttons.Compact, Buttons.Look.Calm);
                    down.IsEnabled = at < movable.Count - 1;
                    down.Click += delegate { MoveBody(captured.Id, 1); };
                    Grid.SetColumn(down, 8);
                    row.Children.Add(up);
                    row.Children.Add(down);
                }
                if (body.Kind == SkyBody.PlanetKind)
                {
                    var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                    Grid.SetColumn(remove, 10);
                    remove.Click += delegate { Collect(); _world.Planets.Remove(captured.Planet); RebuildBodies(); Preview(); };
                    row.Children.Add(remove);
                }
                _bodies.Children.Add(row);
            }
            _building = was;
        }

        private void MoveBody(string id, int delta)
        {
            Collect();
            if (!Astro.MoveBody(_world, id, delta)) return;
            FillSunOrbits(); // le placement d'un soleil a pu changer de côté du monde
            RebuildBodies();
            Preview();
        }

        /// <summary>Le planétarium au jour du curseur : la vue, la date, ce que le ciel fait ce jour-là.</summary>
        private void ShowPlanetarium()
        {
            if (_planetarium == null) return;
            var reference = Reference();
            var year = Math.Max(1, reference.BaseDays);
            if (!_planetNavigated)
            {
                // L'année montrée d'abord : celle des dates que l'auteur a saisies — la date de référence du
                // premier soleil qui orbite, sinon la pleine lune de la première lune — depuis son premier jour.
                // (Tant qu'il n'a pas changé d'année lui-même, la vue suit ces dates.)
                var anchor = 0.0;
                var orbiting = _world.Suns.Find(delegate(Sun s) { return s.OrbitId != null && s.Period > 0; });
                if (orbiting != null) anchor = orbiting.HighAt;
                else if (_world.Moons.Count > 0) anchor = _world.Moons[0].FullAt;
                _planetStart = reference.DayNumber(new TimePoint(TimeMath.FromDayNumber(reference, (long)Math.Floor(anchor)).Year, 1, 1));
            }
            if (Math.Abs(_planetSlider.Maximum - year) > 0.5)
            {
                var was = _building;
                _building = true;
                _planetSlider.Maximum = year;
                if (_planetSlider.Value > year) _planetSlider.Value = 0;
                _building = was;
            }
            var day = _planetStart + _planetSlider.Value;
            _planetarium.Show(_world, reference, day);
            var whole = (long)Math.Floor(day);
            var date = TimeMath.FromDayNumber(reference, whole);
            var season = Astro.SeasonAt(_world, reference, date);
            _planetDate.Text = reference.Format(date) + (season != null && !string.IsNullOrEmpty(season.Name) ? "  ·  " + season.Name : "");
            var notes = new List<string>();
            foreach (var sun in Astro.Wanderers(_world)) notes.Add(SunName(sun, _world.Suns.IndexOf(sun)) + " : " + Astro.SunStateName(Astro.SunPhase(sun, whole + 0.5)));
            foreach (var note in Astro.NotesOn(_world, reference, whole)) notes.Add(note.Text);
            _planetNotes.Text = notes.Count == 0 ? "Rien de particulier dans le ciel ce jour-là." : string.Join("\n", notes.ToArray());
        }

        private void PlayPlanetarium(bool on)
        {
            if (!on)
            {
                if (_planetTimer != null) _planetTimer.Stop();
                return;
            }
            if (_planetTimer == null)
            {
                _planetTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                _planetTimer.Tick += delegate
                {
                    var year = Math.Max(1, _planetSlider.Maximum);
                    var next = _planetSlider.Value + year / 500.0;
                    if (next > year) { next -= year; _planetStart += (long)Math.Round(year); _planetNavigated = true; } // l'année suivante, sans à-coup
                    _planetSlider.Value = next;
                };
            }
            _planetTimer.Start();
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
                var name = new TextBox { Text = moon.Name, Watermark = "Nom du satellite" };
                var lookMoon = moon;
                var period = new TextBox { Text = Show(moon.Period) };
                Grid.SetColumn(period, 2);
                var full = new TextBox { Text = reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(moon.FullAt))), Watermark = reference.Format(new TimePoint(0, 1, 1)) };
                Grid.SetColumn(full, 4);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 6);
                // Seconde ligne (16/11, Rémi) : autour de quoi elle tourne, et ses éclipses.
                var orbit = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 14, 0), Tag = "world-moon-orbit" };
                var eclipses = new ComboBox { Width = 230, Tag = "world-moon-eclipses" };
                eclipses.Items.Add("Par saisons (orbite inclinée)");
                eclipses.Items.Add("À chaque lunaison");
                eclipses.Items.Add("Jamais");
                eclipses.SelectedIndex = Math.Max(0, Math.Min(2, moon.Eclipses));
                var second = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
                second.Children.Add(orbit);
                second.Children.Add(Word("Éclipses :"));
                second.Children.Add(eclipses);
                Grid.SetRow(second, 1);
                Grid.SetColumnSpan(second, 7);
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,100,8,190,8,26"), RowDefinitions = new RowDefinitions("Auto,Auto") };
                grid.Children.Add(WithLook(name, delegate { Collect(); return _world.Moons.Contains(lookMoon) ? lookMoon.Look : null; }, delegate(BodyLook look) { lookMoon.Look = look; Preview(); }, BodyLookDialog.MoonKind, delegate { return MoonName(lookMoon, _world.Moons.IndexOf(lookMoon)); }));
                grid.Children.Add(period);
                grid.Children.Add(full);
                grid.Children.Add(remove);
                grid.Children.Add(second);
                var captured = moon;
                remove.Click += delegate
                {
                    Collect();
                    _world.Moons.Remove(captured);
                    foreach (var other in _world.Moons) if (other.OrbitId == captured.Id) other.OrbitId = null; // sa lune-mère partie, elle orbite le monde
                    RebuildMoons();
                    Preview();
                };
                name.TextChanged += delegate { if (_building) return; Collect(); FillMoonOrbits(); Preview(); };
                period.TextChanged += delegate { Preview(); };
                full.TextChanged += delegate { Preview(); };
                orbit.SelectionChanged += delegate { if (!_building) Preview(); };
                eclipses.SelectionChanged += delegate { if (!_building) Preview(); };
                _moons.Children.Add(Card(grid, new object[] { moon, name, period, full, orbit, eclipses }, "moon"));
            }
            _moonHead.IsVisible = _world.Moons.Count > 0;
            if (_world.Moons.Count == 0) _moons.Children.Add(new TextBlock { Text = "Aucun satellite.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
            FillMoonOrbits();
        }

        private void RebuildPhenomena()
        {
            var was = _building;
            _building = true;
            _phenomena.Children.Clear();
            var reference = Reference();
            foreach (var item in _world.Phenomena)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,130,8,190,8,26") };
                var name = new TextBox { Text = item.Name, Watermark = "Comète, pluie d'étoiles, lune rousse…" };
                var period = new TextBox { Text = Show(item.Period) };
                Grid.SetColumn(period, 2);
                var at = new TextBox { Text = reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(item.At))), Watermark = reference.Format(new TimePoint(0, 1, 1)) };
                Grid.SetColumn(at, 4);
                var remove = Buttons.Icon("x-bold", null, Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 6);
                var captured = item;
                remove.Click += delegate { Collect(); _world.Phenomena.Remove(captured); RebuildPhenomena(); Preview(); };
                name.TextChanged += delegate { Preview(); };
                period.TextChanged += delegate { Preview(); };
                at.TextChanged += delegate { Preview(); };
                row.Children.Add(name);
                row.Children.Add(period);
                row.Children.Add(at);
                row.Children.Add(remove);
                row.Tag = new object[] { item, name, period, at };
                _phenomena.Children.Add(row);
            }
            _phenomenonHead.IsVisible = _world.Phenomena.Count > 0;
            if (_world.Phenomena.Count == 0) _phenomena.Children.Add(new TextBlock { Text = "Aucun phénomène personnalisé.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
        }

        /// <summary>Les faits de la taille, en groupes de tuiles (16/11).</summary>
        private void FillFacts(TimeSystem reference)
        {
            _facts.Children.Clear();
            var groups = Astro.SizeFactGroups(_world, reference);
            if (groups.Count == 0)
            {
                _facts.Children.Add(new Border
                {
                    Padding = new Thickness(14, 11, 14, 12), CornerRadius = new CornerRadius(10), BorderBrush = Chrome.Border, BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "Donnez la circonférence du monde : son rayon, son horizon, la largeur d'une heure de soleil et la durée d'un tour du monde s'en déduisent. Avec l'inclinaison de l'axe : les tropiques, les cercles polaires et la durée du jour selon la latitude.",
                        Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap
                    }
                });
                return;
            }
            foreach (var group in groups)
            {
                _facts.Children.Add(new TextBlock
                {
                    Text = group.Title.ToUpper(CultureInfo.CurrentCulture), Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(2, _facts.Children.Count == 0 ? 2 : 8, 0, 6), Tag = "world-fact-group"
                });
                var tiles = new WrapPanel();
                foreach (var fact in group.Facts)
                {
                    var tile = new StackPanel();
                    tile.Children.Add(new TextBlock { Text = fact.Label, Foreground = Chrome.SoftText, FontSize = 11.5 });
                    tile.Children.Add(new TextBlock { Text = fact.Value, Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 0, 0) });
                    if (!string.IsNullOrEmpty(fact.Note)) tile.Children.Add(new TextBlock { Text = fact.Note, Foreground = Chrome.FaintText, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                    tiles.Children.Add(new Border
                    {
                        Child = tile, Width = 206, Padding = new Thickness(12, 8, 10, 9), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
                        Background = Chrome.FieldBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "world-fact"
                    });
                }
                _facts.Children.Add(tiles);
            }
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
                // Où ce jour tombe dans le calendrier de référence (une année ordinaire) : « → 20/03 »,
                // jour/numéro du mois, dès que le calendrier a des mois (16/11, Rémi).
                echo.Text = day > yearDays ? "au-delà de l'année (" + yearDays + " j)" : "→ " + TimeMath.ShortDayOfYear(reference, day);
            }
            foreach (var child in _suns.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var sun = (Sun)cells[0];
                sun.Name = (((TextBox)cells[1]).Text ?? "").Trim();
                var others = OtherSuns(sun);
                var orbit = ((ComboBox)cells[2]).SelectedIndex;
                if (orbit >= 0) sun.OrbitId = orbit >= 1 && orbit - 1 < others.Count ? others[orbit - 1].Id : null;
                var sizeText = (((TextBox)cells[7]).Text ?? "").Trim();
                if (sizeText.Length == 0) sun.Size = 1;
                else if (ReadNumber(sizeText, out value) && value >= 0.001 && value <= 100000) sun.Size = value;
                else _fieldProblem = "La taille d'un soleil se donne en « fois notre Soleil » : 1, 0,5, 12…";
                if (sun.OrbitId == null) { sun.Period = 0; sun.HighAt = 0; continue; } // centrique : ni période ni date
                var placed = ((ComboBox)cells[8]).SelectedIndex;
                if (placed >= 0) Astro.SetInner(_world, sun, placed == 0);
                var label = SunName(sun, _world.Suns.IndexOf(sun));
                var period = (((TextBox)cells[3]).Text ?? "").Trim();
                if (period.Length == 0) { sun.Period = 0; sun.HighAt = 0; continue; }
                if (ReadNumber(period, out value) && value >= 0.05 && value <= 10000000) sun.Period = value;
                else { _fieldProblem = "La période d'un soleil est un nombre de jours (vide : il reste auprès du soleil qu'il orbite)."; continue; }
                TimePoint high;
                var written = ((TextBox)cells[4]).Text ?? "";
                if (written.Trim().Length == 0) sun.HighAt = 0;
                else if (reference.TryParse(written, out high)) sun.HighAt = reference.DayNumber(TimeMath.Full(high));
                else _fieldProblem = "La date de référence de « " + label + " » ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
            }
            if (_world.Suns.Count > 0 && !_world.Suns.Exists(delegate(Sun s) { return s.OrbitId == null; }))
                _fieldProblem = "Il faut un soleil centrique : c'est lui, au milieu du système, qui fait le jour et les saisons.";
            else
                foreach (var sun in _world.Suns)
                    if (Astro.OrbitLoops(_world, sun)) { _fieldProblem = "« " + SunName(sun, _world.Suns.IndexOf(sun)) + " » finit par tourner autour de lui-même : l'un des soleils de la boucle doit être centrique."; break; }
            foreach (var child in _moons.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var moon = (Moon)cells[0];
                var index = _world.Moons.IndexOf(moon);
                moon.Name = (((TextBox)cells[1]).Text ?? "").Trim();
                if (ReadNumber(((TextBox)cells[2]).Text, out value) && value >= 0.05 && value <= 1000000) moon.Period = value;
                else _fieldProblem = "La période d'une lune est un nombre de jours (29,5 pour la nôtre).";
                TimePoint full;
                var written = ((TextBox)cells[3]).Text ?? "";
                if (written.Trim().Length == 0) moon.FullAt = 0;
                else if (reference.TryParse(written, out full)) moon.FullAt = reference.DayNumber(TimeMath.Full(full));
                else _fieldProblem = "La date de pleine lune de « " + MoonName(moon, index) + " » ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
                var others = OtherMoons(moon);
                var orbit = ((ComboBox)cells[4]).SelectedIndex;
                if (orbit >= 0) moon.OrbitId = orbit >= 1 && orbit - 1 < others.Count ? others[orbit - 1].Id : null;
                var eclipses = ((ComboBox)cells[5]).SelectedIndex;
                if (eclipses >= 0) moon.Eclipses = eclipses;
            }
            foreach (var moon in _world.Moons)
                if (Astro.OrbitLoops(_world, moon)) { _fieldProblem = "« " + MoonName(moon, _world.Moons.IndexOf(moon)) + " » finit par tourner autour d'elle-même : l'une des lunes de la boucle doit orbiter le monde."; break; }
            foreach (var child in _bodies.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var planet = (Planet)cells[0];
                planet.Name = (((TextBox)cells[1]).Text ?? "").Trim();
                var planetLabel = planet.Name.Length == 0 ? "cette planète" : "« " + planet.Name + " »";
                var year = (((TextBox)cells[2]).Text ?? "").Trim();
                if (year.Length == 0) { planet.Period = 0; planet.At = 0; continue; }
                if (ReadNumber(year, out value) && value >= 0.05 && value <= 100000000) planet.Period = value;
                else { _fieldProblem = "L'année d'une planète se donne en jours (vide : elle est seulement placée)."; continue; }
                TimePoint aligned;
                var written = ((TextBox)cells[3]).Text ?? "";
                if (written.Trim().Length == 0) planet.At = 0;
                else if (reference.TryParse(written, out aligned)) planet.At = reference.DayNumber(TimeMath.Full(aligned));
                else _fieldProblem = "La date d'alignement de " + planetLabel + " ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
            }
            foreach (var child in _phenomena.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var item = (SkyEvent)cells[0];
                item.Name = (((TextBox)cells[1]).Text ?? "").Trim();
                var label = item.Name.Length == 0 ? "ce phénomène" : "« " + item.Name + " »";
                if (ReadNumber(((TextBox)cells[2]).Text, out value) && value >= 0.05 && value <= 100000000) item.Period = value;
                else _fieldProblem = "Le retour d'un phénomène se donne en jours : le temps entre deux passages.";
                TimePoint at;
                var written = ((TextBox)cells[3]).Text ?? "";
                if (written.Trim().Length == 0) item.At = 0;
                else if (reference.TryParse(written, out at)) item.At = reference.DayNumber(TimeMath.Full(at));
                else _fieldProblem = "La date d'un passage de " + label + " ne se lit pas dans « " + reference.Name + " » (ex. " + reference.Format(new TimePoint(0, 1, 1)) + ").";
            }
        }

        private void Preview()
        {
            if (_building) return;
            Collect();
            var reference = Reference();
            _reference.Text = _systems.Count == 0 ? ""
                : "Dates et heures de cette fenêtre : « " + reference.Name + " » (" + reference.BaseDays + " jours par an, " + reference.HoursPerDay + " heures par jour)"
                    + (ReferenceChoices().Count > 1 ? " — se choisit à l'onglet Saisons." : ".");
            // Un monde sans calendrier n'apparaît nulle part : le dire en bandeau, pas en petites lettres.
            _noCalendar.IsVisible = _systems.Count > 0 && _inWorld.Count == 0;
            _problem.Text = _fieldProblem ?? "";
            _problem.IsVisible = _fieldProblem != null;

            // — Le calculateur de circonférence.
            var scaled = FactorResult();
            var typed = (_factor.Text ?? "").Trim().Length > 0;
            _factorEcho.Text = scaled > 0 ? "=  " + scaled.ToString("N0", French) + " km" : (typed ? "un nombre, plus grand que zéro" : "");
            _factorEcho.Foreground = scaled > 0 || !typed ? Chrome.Ink : Chrome.Danger;
            _useFactor.IsVisible = scaled > 0;

            // — Les soleils : leur rôle, ce que leur agencement déclenche, leurs dates.
            var main = Astro.MainSun(_world);
            var mainName = main == null ? "le premier soleil" : SunName(main, _world.Suns.IndexOf(main));
            foreach (var child in _suns.Children)
            {
                var cells = Cells(child);
                if (cells == null) continue;
                var sun = (Sun)cells[0];
                var orbits = sun.OrbitId != null;
                var close = Astro.IsInner(_world, sun);
                ((TextBlock)cells[9]).Text = close ? "Tour autour de son soleil en" : "Tour du ciel en";
                ((TextBlock)cells[10]).Text = close ? "jours · passe devant lui le" : "jours · au plus haut à minuit le";
                ((Control)cells[5]).IsVisible = orbits;
                var role = (TextBlock)cells[6];
                role.IsVisible = !orbits;
                role.Text = sun == main ? "Fait le jour et les saisons." : "Au centre du système avec " + mainName + " : ils se lèvent et se couchent ensemble.";
            }
            var sunLines = new List<string>();
            if (_world.Suns.Count > 0 && _fieldProblem == null)
            {
                for (var i = 0; i < _world.Suns.Count; i++)
                {
                    var sun = _world.Suns[i];
                    if (sun == main) continue;
                    var label = SunName(sun, i);
                    if (sun.OrbitId == null) { sunLines.Add(label + " se lève et se couche avec " + mainName + " : deux soleils dans le même ciel de jour."); continue; }
                    if (sun.Period <= 0) continue; // sans période : dit par les comportements
                    if (Astro.IsInner(_world, sun))
                    {
                        // En orbite rapprochée : ses éclipses solaires pures, datées.
                        var from = (long)Math.Floor(sun.HighAt);
                        var around = Astro.FindSun(_world, sun.OrbitId);
                        var aroundName = around == null ? "son soleil" : SunName(around, _world.Suns.IndexOf(around));
                        sunLines.Add(label + " : devant " + aroundName + " le " + reference.Format(TimeMath.FromDayNumber(reference, Astro.NextSunEclipse(sun, from, Astro.SunInFront)))
                            + ", derrière lui le " + reference.Format(TimeMath.FromDayNumber(reference, Astro.NextSunEclipse(sun, from, Astro.SunBehind)))
                            + ", de nouveau devant le " + reference.Format(TimeMath.FromDayNumber(reference, Astro.NextSunEclipse(sun, from + 1, Astro.SunInFront)))
                            + ". Taille : " + Show(sun.Size) + " × notre Soleil (" + Show(Math.Round(sun.Size * Astro.SunDiameter)) + " km de large).");
                        continue;
                    }
                    var high = (long)Math.Floor(sun.HighAt);
                    var years = sun.Period / TimeMath.MeanYearDays(reference);
                    sunLines.Add(label + " : au plus haut à minuit le " + reference.Format(TimeMath.FromDayNumber(reference, high))
                        + " (nuits claires pendant environ " + TimeSystem.Count(Math.Max(1, (long)Math.Round(sun.Period / 4)), "jour", "jours") + "), auprès de " + mainName + " le "
                        + reference.Format(TimeMath.FromDayNumber(reference, (long)Math.Floor(sun.HighAt + sun.Period / 2))) + " (nuits noires), de retour le "
                        + reference.Format(TimeMath.FromDayNumber(reference, Astro.NextHigh(sun, high + 1)))
                        + (years >= 1 ? " — un tour en " + reference.DurationLabel(years) + "." : "."));
                }
            }
            _sunEcho.Text = string.Join("\n", sunLines.ToArray());
            _sunEcho.IsVisible = sunLines.Count > 0;
            if (_fieldProblem == null) AstrolabeUi.FillBehaviors(_sunBehaviors, Astro.SunBehaviors(_world, reference));

            // — Ce que la taille permet de dire.
            FillFacts(reference);

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

            // — Les lunes : où elles en sont, quand elles se retrouvent, ce qu'elles déclenchent.
            foreach (var child in _moons.Children)
            {
                var cells = Cells(child);
                if (cells != null) ((ComboBox)cells[5]).IsEnabled = !Astro.IsSubMoon(_world, (Moon)cells[0]); // une lune de lune ne fait pas d'éclipse
            }
            var lines = new List<string>();
            if (_world.Moons.Count > 0 && _fieldProblem == null)
            {
                for (var i = 0; i < _world.Moons.Count; i++)
                {
                    var moon = _world.Moons[i];
                    var next = Astro.NextFull(moon, (long)Math.Floor(moon.FullAt) + 1);
                    if (Astro.IsSubMoon(_world, moon))
                    {
                        var parent = Astro.FindMoon(_world, moon.OrbitId);
                        lines.Add(MoonName(moon, i) + " : prochain passage devant " + MoonName(parent, _world.Moons.IndexOf(parent)) + " le " + reference.Format(TimeMath.FromDayNumber(reference, next)));
                        continue;
                    }
                    lines.Add((moon.Name.Length == 0 ? "Lune" : moon.Name) + " : pleine lune suivante le " + reference.Format(TimeMath.FromDayNumber(reference, next))
                        + " (" + Show(Math.Round(reference.BaseDays / moon.Period, 1)) + " lunaisons par an)");
                }
                var start = (long)Math.Floor(_world.Moons[0].FullAt);
                var first = Astro.NextConjunction(_world, start);
                if (first != null || _world.Moons.FindAll(delegate(Moon m) { return !Astro.IsSubMoon(_world, m); }).Count > 1)
                {
                    var second = first == null ? null : Astro.NextConjunction(_world, first.Value + 2);
                    lines.Add(first == null ? "Toutes pleines ensemble : pas dans les millénaires qui suivent."
                        : "Toutes pleines ensemble le " + reference.Format(TimeMath.FromDayNumber(reference, first.Value))
                            + (second == null ? "" : ", puis " + TimeSystem.Count(second.Value - first.Value, "jour", "jours") + " plus tard (" + reference.DurationLabel((second.Value - first.Value) / TimeMath.MeanYearDays(reference)) + ")"));
                }
            }
            _moonEcho.Text = string.Join("\n", lines.ToArray());
            _moonEcho.IsVisible = lines.Count > 0;
            if (_fieldProblem == null) AstrolabeUi.FillBehaviors(_moonBehaviors, Astro.MoonBehaviors(_world, reference));

            // — Le planétarium : la vue suit tout ce qui vient d'être lu.
            ShowPlanetarium();

            // — Les phénomènes personnalisés : leur prochain passage, leur rythme.
            var passages = new List<string>();
            if (_fieldProblem == null)
                foreach (var item in _world.Phenomena)
                {
                    var next = Astro.OccurrenceDay(item, (long)Math.Floor(item.At), 1);
                    var years = item.Period / TimeMath.MeanYearDays(reference);
                    passages.Add((item.Name.Length == 0 ? "Phénomène" : item.Name) + " : passage suivant le " + reference.Format(TimeMath.FromDayNumber(reference, next))
                        + (years >= 1 ? " — un retour tous les " + reference.DurationLabel(years) + "." : "."));
                }
            _phenomenonEcho.Text = string.Join("\n", passages.ToArray());
            _phenomenonEcho.IsVisible = passages.Count > 0;
        }

        private void Accept()
        {
            Collect();
            if (_fieldProblem == null) _fieldProblem = _guides.Collect();
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
        internal int MoonRowsForProbe { get { var n = 0; foreach (var child in _moons.Children) if (Cells(child) != null) n++; return n; } }
        internal int SunRowsForProbe { get { var n = 0; foreach (var child in _suns.Children) if (Cells(child) != null) n++; return n; } }
        internal string SunsEchoForProbe { get { return _sunEcho.Text ?? ""; } }
        internal string SeasonDayForProbe(int index) { return ((TextBox)((Grid)_seasons.Children[index]).Children[3]).Text; }
        internal string MoonFullForProbe(int index) { return ((TextBox)Cells(_moons.Children[index])[3]).Text; }
        internal void ClearSkyForProbe() { Collect(); _world.Seasons.Clear(); _world.Moons.Clear(); RebuildSeasons(); RebuildMoons(); Preview(); }
        internal void AddSunForProbe(string name, string period, string high)
        {
            Collect();
            // Une période : il orbite le soleil principal ; sans : un jumeau centrique.
            var main = Astro.MainSun(_world);
            var added = new Sun { Name = name, OrbitId = period.Length > 0 && main != null ? main.Id : null };
            _world.Suns.Add(added);
            Astro.SetInner(_world, added, false); // la sonde des lots 6 à 9 parle d'un compagnon LOINTAIN (nuits claires)
            RebuildSuns();
            var row = Cells(_suns.Children[_suns.Children.Count - 1]);
            ((TextBox)row[3]).Text = period;
            ((TextBox)row[4]).Text = high;
            Preview();
        }
        internal void SetSizeForProbe(string circumference, string tilt) { _circumference.Text = circumference; _tilt.Text = tilt; }
        internal void SetSolarYearForProbe(string days) { _solarYear.Text = days; }
        internal List<string> FactsForProbe() { return Astro.SizeFacts(_world, Reference()); }
        internal int FactGroupsForProbe { get { var n = 0; foreach (var child in _facts.Children) if (child is WrapPanel) n++; return n; } }
        internal int FactTilesForProbe { get { var n = 0; foreach (var child in _facts.Children) { var tiles = child as WrapPanel; if (tiles != null) n += tiles.Children.Count; } return n; } }
        internal bool FactorOpenForProbe { get { return _factorAccordion.IsOpen; } set { _factorAccordion.IsOpen = value; } }
        internal bool NoCalendarNoticeForProbe { get { return _noCalendar.IsVisible; } }
        internal List<string> SunOrbitChoicesForProbe(int index) { var names = new List<string>(); foreach (var item in ((ComboBox)Cells(_suns.Children[index])[2]).Items) names.Add(item as string); return names; }
        internal void SetSunOrbitForProbe(int index, int choice) { ((ComboBox)Cells(_suns.Children[index])[2]).SelectedIndex = choice; }
        internal bool SunFieldsShownForProbe(int index) { return ((Control)Cells(_suns.Children[index])[5]).IsVisible; }
        internal string SunRoleForProbe(int index) { var role = (TextBlock)Cells(_suns.Children[index])[6]; return role.IsVisible ? role.Text ?? "" : ""; }
        internal List<string> MoonOrbitChoicesForProbe(int index) { var names = new List<string>(); foreach (var item in ((ComboBox)Cells(_moons.Children[index])[4]).Items) names.Add(item as string); return names; }
        internal void SetMoonOrbitForProbe(int index, int choice) { ((ComboBox)Cells(_moons.Children[index])[4]).SelectedIndex = choice; }
        internal void SetMoonEclipsesForProbe(int index, int choice) { ((ComboBox)Cells(_moons.Children[index])[5]).SelectedIndex = choice; }
        internal bool MoonEclipsesEnabledForProbe(int index) { return ((ComboBox)Cells(_moons.Children[index])[5]).IsEnabled; }
        private static string BehaviorsText(StackPanel panel)
        {
            var parts = new List<string>();
            foreach (var row in panel.Children)
            {
                var dock = row as DockPanel;
                if (dock == null) continue;
                foreach (var inner in dock.Children)
                {
                    var stack = inner as StackPanel;
                    if (stack != null) foreach (var line in stack.Children) parts.Add(((TextBlock)line).Text);
                }
            }
            return string.Join(" | ", parts.ToArray());
        }
        internal string SunBehaviorsForProbe { get { return BehaviorsText(_sunBehaviors); } }
        internal string MoonBehaviorsForProbe { get { return BehaviorsText(_moonBehaviors); } }
        internal void SetSunPlacementForProbe(int index, bool inner) { ((ComboBox)Cells(_suns.Children[index])[8]).SelectedIndex = inner ? 0 : 1; }
        internal int SunPlacementForProbe(int index) { return ((ComboBox)Cells(_suns.Children[index])[8]).SelectedIndex; }
        internal void SetSunSizeForProbe(int index, string size) { ((TextBox)Cells(_suns.Children[index])[7]).Text = size; }
        internal string SunWordsForProbe(int index) { var cells = Cells(_suns.Children[index]); return ((TextBlock)cells[9]).Text + " … " + ((TextBlock)cells[10]).Text; }
        internal List<string> BodiesForProbe()
        {
            var names = new List<string>();
            foreach (var body in Astro.Layout(_world)) names.Add(body.Rank + " " + body.Name);
            return names;
        }
        internal int BodyRowsForProbe { get { return _bodies.Children.Count; } }
        internal void MoveBodyForProbe(int rank, int delta) { foreach (var body in Astro.Layout(_world)) if (body.Rank == rank && body.Movable) { MoveBody(body.Id, delta); return; } }
        internal void AddPlanetForProbe(string name, string period, string at)
        {
            Collect();
            _world.Planets.Add(new Planet { Name = name });
            RebuildBodies();
            foreach (var child in _bodies.Children)
            {
                var cells = Cells(child);
                if (cells == null || ((Planet)cells[0]).Name != name) continue;
                ((TextBox)cells[2]).Text = period;
                ((TextBox)cells[3]).Text = at;
            }
            Preview();
        }
        internal PlanetariumView PlanetariumForProbe { get { return _planetarium; } }
        internal string PlanetDateForProbe { get { return _planetDate.Text ?? ""; } }
        internal string PlanetNotesForProbe { get { return _planetNotes.Text ?? ""; } }
        internal void SetPlanetDayForProbe(double day) { _planetSlider.Value = day; }
        internal double PlanetYearForProbe { get { return _planetSlider.Maximum; } }
        internal int PhenomenonRowsForProbe { get { var n = 0; foreach (var child in _phenomena.Children) if (Cells(child) != null) n++; return n; } }
        internal void AddPhenomenonForProbe(string name, string period, string at)
        {
            Collect();
            _world.Phenomena.Add(new SkyEvent { Name = name });
            RebuildPhenomena();
            var row = Cells(_phenomena.Children[_phenomena.Children.Count - 1]);
            ((TextBox)row[2]).Text = period;
            ((TextBox)row[3]).Text = at;
            Preview();
        }
        internal string PhenomenaEchoForProbe { get { return _phenomenonEcho.Text ?? ""; } }
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
            var row = Cells(_moons.Children[_moons.Children.Count - 1]);
            ((TextBox)row[2]).Text = period;
            ((TextBox)row[3]).Text = full;
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
