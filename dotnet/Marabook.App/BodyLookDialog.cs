using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>PERSONNALISER L'ASTRE (lot 14, 16/11/2026, Rémi) : l'allure d'un soleil, d'un satellite ou
    /// du monde lui-même, telle que la vue Globe la dessine.
    ///
    /// — une COULEUR D'ACCENT : le halo d'un soleil, l'atmosphère du monde ;
    /// — pour tout astre qui n'est pas le monde, sa TAILLE ET SON ORBITE (lot 16, 17/11) : son diamètre,
    ///   la distance qui le sépare de l'astre autour duquel il tourne, l'inclinaison de son orbite — et un
    ///   CALCULATEUR ORBITAL qui part des valeurs de notre Lune (ou de notre Soleil) : « 1,2 fois » ;
    /// — des ANNEAUX : leur couleur, leur ampleur, leur écart à l'astre, leur épaisseur, leur angle sur
    ///   l'équateur, leur grain (la texture, et son échelle) — avec, à droite de la fenêtre, un APERÇU de
    ///   l'astre annelé, qu'on fait tourner à la souris.
    ///
    /// L'axe du monde, lui, reste dans « Taille du monde » : c'est de lui que viennent les saisons.</summary>
    internal sealed class BodyLookDialog : Window
    {
        public const int SunKind = 0, MoonKind = 1, WorldKind = 2;
        private static readonly string[] Palette = MapShapeDialog.HexOf(ColorPalette.Bodies); // le nuancier des astres, nommé dans la palette (22/11)
        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private readonly BodyLook _look;
        private readonly int _kind;
        private readonly StackPanel _accents, _ringColors;
        private readonly TextBox _distance, _diameter, _tilt;
        private readonly TextBox _quotientDiameter, _quotientDistance, _quotientTilt;
        private readonly TextBlock _resultDiameter, _resultDistance, _resultTilt;
        private readonly Button _useCalculator;
        private readonly Accordion _calculator;
        private readonly Slider _ringSize, _ringGap, _ringThickness, _ringAngle, _ringGrain, _ringDensity;
        internal double DensityForProbe { get { return _ringDensity.Value; } }
        internal bool DensityShownForProbe { get { return _ringDensity.IsEffectivelyVisible; } }
        internal void SetDensityForProbe(double density) { _ringDensity.Value = density; }
        private readonly CheckBox _rings, _texture;
        private readonly StackPanel _ringBox;
        private readonly Control _grainRow;
        private readonly TextBlock _distanceEcho, _diameterEcho, _problem;
        private readonly BodyPreview _preview;
        private readonly Border _previewHost;
        private bool _building = true;
        private BodyLook _result;

        // Les valeurs de notre système, d'où part le calculateur orbital.
        private double ReferenceDiameter { get { return _kind == SunKind ? Astro.SunDiameter : BodyLook.MoonDiameter; } }
        private double ReferenceDistance { get { return _kind == SunKind ? BodyLook.SunDistance : BodyLook.MoonDistance; } }
        private double ReferenceTilt { get { return _kind == SunKind ? BodyLook.SunTilt : BodyLook.MoonTilt; } }
        private string ReferenceName { get { return _kind == SunKind ? "notre Soleil" : "notre Lune"; } }

        private BodyLookDialog(Window owner, int kind, string name, BodyLook look, string bodyColor)
        {
            _kind = kind;
            _look = look;
            TimelineUi.Dress(this, owner, "Personnaliser — " + name);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = kind == WorldKind ? "L'allure du monde dans la vue planétaire de ses cartes."
                    : kind == SunKind ? "L'allure de ce soleil dans le ciel de la vue planétaire."
                    : "L'allure de ce satellite, et sa place autour du monde, dans la vue planétaire.",
                Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(TimelineUi.Label(kind == SunKind ? "Couleur d'accent (son halo) :" : "Couleur d'accent (son atmosphère) :"));
            _accents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "look-accents" };
            panel.Children.Add(_accents);

            // ---------------------------------------------------- taille et orbite (lot 16)
            var diameter = kind == SunKind ? (look.Diameter > 0 ? look.Diameter : Astro.SunDiameter) : look.DiameterKm;
            _diameter = new TextBox { Text = diameter.ToString("0.##", French), Tag = "look-diameter" };
            _diameterEcho = Echo("look-diameter-echo");
            _distance = new TextBox { Text = look.Distance.ToString("0.##", French), Tag = "look-distance" };
            _distanceEcho = Echo("look-distance-echo");
            _tilt = new TextBox { Text = look.Tilt.ToString("0.##", French), Tag = "look-tilt" };
            _quotientDiameter = Quotient("look-quotient-diameter");
            _quotientDistance = Quotient("look-quotient-distance");
            _quotientTilt = Quotient("look-quotient-tilt");
            _resultDiameter = Result();
            _resultDistance = Result();
            _resultTilt = Result();
            _useCalculator = Buttons.Text("Reporter ces valeurs", "Écrire les résultats dans « Taille et orbite », au-dessus", Buttons.Compact, Buttons.Look.Outline);
            _useCalculator.Tag = "look-calculator-use";
            _useCalculator.HorizontalAlignment = HorizontalAlignment.Left;
            _useCalculator.Margin = new Thickness(0, 10, 0, 0);
            _useCalculator.IsVisible = false;
            _useCalculator.Click += delegate { UseCalculator(); };
            if (kind != WorldKind)
            {
                var orbit = new Grid { ColumnDefinitions = new ColumnDefinitions("160,8,110,8,Auto,12,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
                OrbitRow(orbit, 0, "Diamètre :", _diameter, "km", _diameterEcho);
                OrbitRow(orbit, 1, kind == SunKind ? "Distance à son astre :" : "Distance à son astre :", _distance, "km", _distanceEcho);
                OrbitRow(orbit, 2, kind == SunKind ? "Inclinaison :" : "Inclinaison orbitale :", _tilt, "degrés", null);
                var orbitBody = new StackPanel();
                orbitBody.Children.Add(orbit);
                orbitBody.Children.Add(TimelineUi.Hint(kind == MoonKind
                    ? "La distance est celle qui le sépare de l'astre autour duquel il tourne : d'office, celle de la Terre à la Lune (sur le globe, l'orbite est resserrée pour tenir dans la vue — plus loin reste plus loin). Inclinaison 0 : il tourne dans le plan de l'orbite du monde ; incliné, il monte au-dessus et descend en dessous à chaque tour."
                    : "La distance est celle qui le sépare de l'astre autour duquel il tourne — pour un soleil centrique, celle du monde à lui : plus il est loin, plus il est petit dans le ciel. Son diamètre décide de ses éclipses : entre deux soleils qui se croisent, le plus large cache l'autre en entier. L'inclinaison penche son axe, et l'orbite d'un soleil qui tourne autour de lui."));
                panel.Children.Add(Framed("Taille et orbite", orbitBody, "look-orbit-section"));

                // LE CALCULATEUR ORBITAL : les valeurs de notre système, fois un quotient — comme la
                // circonférence du monde se calcule en « tant de fois la Terre ».
                var calc = new Grid { ColumnDefinitions = new ColumnDefinitions("92,8,70,8,Auto,10,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
                CalculatorRow(calc, 0, "Diamètre :", _quotientDiameter, "×  " + ReferenceDiameter.ToString("#,0.##", French) + " km", _resultDiameter);
                CalculatorRow(calc, 1, "Distance :", _quotientDistance, "×  " + ReferenceDistance.ToString("#,0.##", French) + " km", _resultDistance);
                CalculatorRow(calc, 2, "Inclinaison :", _quotientTilt, "×  " + ReferenceTilt.ToString("0.###", French) + "°", _resultTilt);
                var calcBody = new StackPanel();
                calcBody.Children.Add(new TextBlock { Text = "Combien de fois " + ReferenceName + " ? Un quotient par ligne (1,2 = une fois et un cinquième) — laissez vide ce que vous ne voulez pas changer.", Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
                calcBody.Children.Add(calc);
                calcBody.Children.Add(_useCalculator);
                _calculator = new Accordion("Calculateur orbital", "en nombre de fois " + ReferenceName, calcBody, false, "look-calculator", "calculator-fill", true);
                _calculator.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(_calculator);
            }
            else panel.Children.Add(TimelineUi.Hint("L'axe du monde se règle dans « Taille du monde » (inclinaison de l'axe) : c'est de lui que viennent les saisons. Sans angle, ses anneaux sont dans le plan de son équateur."));

            // ---------------------------------------------------- les anneaux
            _rings = new CheckBox { Content = "Des anneaux", IsChecked = look.Rings, Tag = "look-rings" };
            _ringBox = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _ringBox.Children.Add(TimelineUi.Label("Leur couleur :", 2));
            _ringColors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "look-ring-colors" };
            _ringBox.Children.Add(_ringColors);
            var sliders = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            _ringSize = SliderRow(sliders, "Ampleur", 0.2, 4, look.RingSize, "look-ring-size", delegate(double v) { return "× " + v.ToString("0.0#", French); }, "Agrandit ou réduit l'ensemble des anneaux (1 = comme ceux de Saturne)");
            _ringGap = SliderRow(sliders, "Écart à l'astre", 0, 3, look.RingGap, "look-ring-gap", delegate(double v) { return v < 0.02 ? "au ras" : "× " + v.ToString("0.0#", French); }, "Rapproche les anneaux de l'astre, ou les en éloigne");
            _ringThickness = SliderRow(sliders, "Épaisseur", 0.1, 3, look.RingThickness, "look-ring-thickness", delegate(double v) { return "× " + v.ToString("0.0#", French); }, "La largeur de la bande, de son bord intérieur à son bord extérieur");
            _ringAngle = SliderRow(sliders, "Angle", -90, 90, look.RingAngle, "look-ring-angle", delegate(double v) { return Math.Round(v).ToString("0", French) + "°"; }, "Penche le plan des anneaux sur l'équateur de l'astre (0 = dans le plan de l'équateur)");
            // Lot 18 : la densité de la ceinture — l'ombre qu'elle porte sur son astre.
            _ringDensity = SliderRow(sliders, "Densité", 0, 1, look.RingDensity, "look-ring-density", delegate(double v) { return v < 0.005 ? "sans ombre" : v.ToString("0.00", French); },
                "La densité de la ceinture, de 0 à 1 : à 0, les anneaux se voient sans assombrir l'astre ; à 1, leur ombre vaut une nuit");
            // (Sur le globe, seul le monde est assez grand pour qu'on y voie l'ombre de ses anneaux.)
            ((Control)_ringDensity.Parent).IsVisible = kind == WorldKind;
            _ringBox.Children.Add(sliders);
            _texture = new CheckBox { Content = "Texture (du grain dans les anneaux)", IsChecked = look.RingTexture, Margin = new Thickness(0, 6, 0, 0), Tag = "look-ring-texture" };
            _ringBox.Children.Add(_texture);
            var grain = new StackPanel();
            _ringGrain = SliderRow(grain, "Échelle du grain", 0.25, 4, look.RingGrain, "look-ring-grain", delegate(double v) { return "× " + v.ToString("0.0#", French); }, "Agrandit ou diminue le détail de la texture");
            _grainRow = grain;
            _ringBox.Children.Add(grain);
            var ringBody = new StackPanel();
            ringBody.Children.Add(_rings);
            ringBody.Children.Add(_ringBox);
            panel.Children.Add(Framed(null, ringBody, "look-ring-section"));

            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
            panel.Children.Add(_problem);
            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var reset = Buttons.Text("Remettre d'office", "Sans accent, sans anneaux, sans inclinaison, à la taille et à la distance de " + (kind == SunKind ? "notre Soleil" : "notre Lune"), Buttons.Compact, Buttons.Look.Calm);
            reset.Click += delegate { _result = kind == SunKind ? BodyLook.ForSun() : new BodyLook(); Close(); };
            DockPanel.SetDock(reset, Dock.Left);
            buttons.Children.Add(reset);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { if (Accept()) Close(); };
            right.Children.Add(cancel);
            right.Children.Add(ok);
            Dialogs.Arrange(right, ok);
            buttons.Children.Add(right);
            panel.Children.Add(buttons);

            // ---------------------------------------------------- l'aperçu, à droite (lot 16)
            Color body;
            if (string.IsNullOrEmpty(bodyColor) || !Color.TryParse(bodyColor, out body))
                body = kind == WorldKind ? Color.FromRgb(0x3F, 0x7C, 0xB5) : kind == SunKind ? Color.FromRgb(0xF5, 0xC5, 0x42) : Color.FromRgb(0xB9, 0xBF, 0xCC);
            _preview = new BodyPreview(kind, body) { Tag = "look-preview" };
            var previewStack = new StackPanel { Width = 320 };
            previewStack.Children.Add(new TextBlock { Text = "APERÇU", Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 0, 0, 6) });
            previewStack.Children.Add(_preview);
            previewStack.Children.Add(TimelineUi.Hint("Glissez sur l'astre pour tourner autour de lui. Les anneaux tournent lentement sur eux-mêmes quand la vue planétaire est animée."));
            _previewHost = new Border { Child = previewStack, Margin = new Thickness(0, 14, 18, 14), VerticalAlignment = VerticalAlignment.Top, Tag = "look-preview-host" };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = panel };
            var available = Dialogs.AvailableHeight(owner);
            if (available > 300) scroll.MaxHeight = available - 40;
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
            Grid.SetColumn(_previewHost, 1);
            layout.Children.Add(scroll);
            layout.Children.Add(_previewHost);
            Content = layout;

            _rings.IsCheckedChanged += delegate { Live(); };
            _texture.IsCheckedChanged += delegate { Live(); };
            _distance.TextChanged += delegate { Describe(); };
            _diameter.TextChanged += delegate { Describe(); };
            foreach (var box in new[] { _quotientDiameter, _quotientDistance, _quotientTilt }) box.TextChanged += delegate { Calculate(); };
            FillSwatches(_accents, true);
            FillSwatches(_ringColors, false);
            _building = false;
            Describe();
            Calculate();
            Live();
        }

        // ------------------------------------------------------------ briques

        private static TextBlock Echo(string tag)
        {
            return new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = tag };
        }

        private static TextBox Quotient(string tag) { return new TextBox { Watermark = "ex. 1,2", Tag = tag }; }
        private static TextBlock Result() { return new TextBlock { Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; }

        private static void Place(Grid grid, Control control, int row, int column)
        {
            if (control == null) return;
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        private static void OrbitRow(Grid grid, int row, string label, TextBox field, string unit, TextBlock echo)
        {
            field.Margin = new Thickness(0, 3, 0, 3);
            Place(grid, new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Place(grid, field, row, 2);
            Place(grid, new TextBlock { Text = unit, Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, row, 4);
            Place(grid, echo, row, 6);
        }

        private static void CalculatorRow(Grid grid, int row, string label, TextBox quotient, string times, TextBlock result)
        {
            quotient.Margin = new Thickness(0, 3, 0, 3);
            Place(grid, new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Place(grid, quotient, row, 2);
            Place(grid, new TextBlock { Text = times, Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, row, 4);
            Place(grid, result, row, 6);
        }

        /// <summary>Une section encadrée d'une bordure arrondie (lot 16, Rémi) ; un titre, s'il y en a un.</summary>
        private static Border Framed(string title, Control body, string tag)
        {
            var stack = new StackPanel();
            if (title != null) stack.Children.Add(new TextBlock { Text = title, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
            stack.Children.Add(body);
            return new Border
            {
                Child = stack, Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(10),
                BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7), Tag = tag
            };
        }

        /// <summary>Un réglage au curseur : son nom, le curseur, sa valeur dite à droite. Chaque mouvement se voit dans l'aperçu.</summary>
        private Slider SliderRow(Panel host, string label, double minimum, double maximum, double value, string tag, Func<double, string> show, string tip)
        {
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = Math.Max(minimum, Math.Min(maximum, value)), Tag = tag, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(slider, tip);
            var echo = new TextBlock { Text = show(slider.Value), Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("118,8,*,8,58") };
            Place(row, new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            Place(row, slider, 0, 2);
            Place(row, echo, 0, 4);
            slider.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != RangeBase.ValueProperty) return;
                echo.Text = show(slider.Value);
                Live();
            };
            host.Children.Add(row);
            return slider;
        }

        private void FillSwatches(StackPanel host, bool accent)
        {
            host.Children.Clear();
            var current = accent ? _look.Accent : _look.RingColor;
            var none = Buttons.TextToggle("D'office", accent ? "La teinte de l'astre" : "La teinte de l'astre, pâlie", Buttons.Compact);
            none.IsChecked = string.IsNullOrEmpty(current);
            none.Click += delegate { if (accent) _look.Accent = null; else _look.RingColor = null; FillSwatches(host, accent); Live(); };
            host.Children.Add(none);
            var colors = new List<string>(Palette);
            foreach (var custom in ColorMenus.Customs(null)) if (!colors.Exists(delegate(string c) { return string.Equals(c, custom, StringComparison.OrdinalIgnoreCase); })) colors.Add(custom); // la palette de l'auteur (22/11)
            if (!string.IsNullOrEmpty(current) && !colors.Exists(delegate(string c) { return string.Equals(c, current, StringComparison.OrdinalIgnoreCase); })) colors.Add(current);
            foreach (var value in colors)
            {
                var color = value;
                Color parsed;
                if (!Color.TryParse(color, out parsed)) continue;
                var chosen = string.Equals(color, current, StringComparison.OrdinalIgnoreCase);
                var swatch = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(parsed), VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = chosen ? Chrome.Ink : Chrome.Border, BorderThickness = new Thickness(chosen ? 2.5 : 1), Cursor = new Cursor(StandardCursorType.Hand),
                    [ToolTip.TipProperty] = ColorPalette.Describe(color)
                };
                swatch.PointerPressed += delegate { if (accent) _look.Accent = color; else _look.RingColor = color; FillSwatches(host, accent); Live(); };
                host.Children.Add(swatch);
            }
            var other = Buttons.Text("Autre…", "Une autre couleur", Buttons.Compact, Buttons.Look.Calm);
            other.Click += async delegate
            {
                var picked = await ColorDialog.Ask(this);
                if (string.IsNullOrEmpty(picked)) return;
                if (accent) _look.Accent = picked; else _look.RingColor = picked;
                FillSwatches(host, accent);
                Live();
            };
            host.Children.Add(other);
        }

        private static bool Number(string text, out double value)
        {
            text = (text ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace("°", "").Replace("−", "-").Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Km(double value) { return value.ToString(value >= 100 ? "#,0" : "#,0.##", French) + " km"; }

        /// <summary>Ce que valent la distance et le diamètre saisis, en fois ceux de notre système.</summary>
        private void Describe()
        {
            double km;
            _distanceEcho.Text = !Number(_distance.Text, out km) || km <= 0 ? ""
                : _kind == SunKind ? "soit " + BodyLook.TimesLabel(km, BodyLook.SunDistance, "Terre–Soleil") : "soit " + BodyLook.TimesLabel(km, BodyLook.MoonDistance, "Terre–Lune");
            ToolTip.SetTip(_distanceEcho, _distanceEcho.Text.Length == 0 ? null : _kind == SunKind ? "En nombre de fois la distance de la Terre au Soleil" : "En nombre de fois la distance de la Terre à la Lune");
            _diameterEcho.Text = Number(_diameter.Text, out km) && km > 0 ? "soit " + BodyLook.TimesLabel(km, ReferenceDiameter, ReferenceName) : "";
        }

        // ------------------------------------------------------------ le calculateur orbital

        private static bool QuotientOf(TextBox box, out double quotient)
        {
            quotient = 0;
            return (box.Text ?? "").Trim().Length > 0 && Number(box.Text, out quotient) && quotient > 0 && quotient <= 1e6;
        }

        private void Calculate()
        {
            if (_building) return;
            double q;
            var any = false;
            foreach (var line in new[] { new object[] { _quotientDiameter, _resultDiameter, ReferenceDiameter, true }, new object[] { _quotientDistance, _resultDistance, ReferenceDistance, true }, new object[] { _quotientTilt, _resultTilt, ReferenceTilt, false } })
            {
                var box = (TextBox)line[0];
                var result = (TextBlock)line[1];
                var typed = (box.Text ?? "").Trim().Length > 0;
                if (QuotientOf(box, out q))
                {
                    var value = q * (double)line[2];
                    result.Text = "=  " + ((bool)line[3] ? Km(value) : value.ToString("0.##", French) + "°");
                    result.Foreground = Chrome.Ink;
                    any = true;
                }
                else
                {
                    result.Text = typed ? "quotient illisible" : "";
                    result.Foreground = Chrome.Danger;
                }
            }
            _useCalculator.IsVisible = any;
        }

        private void UseCalculator()
        {
            double q;
            if (QuotientOf(_quotientDiameter, out q)) _diameter.Text = (q * ReferenceDiameter).ToString("0.##", French);
            if (QuotientOf(_quotientDistance, out q)) _distance.Text = (q * ReferenceDistance).ToString("0.##", French);
            if (QuotientOf(_quotientTilt, out q)) _tilt.Text = Math.Min(90, q * ReferenceTilt).ToString("0.###", French);
            Describe();
        }

        // ------------------------------------------------------------ l'aperçu

        /// <summary>Les réglages des anneaux, lus dans l'allure : l'aperçu les suit à chaque geste.</summary>
        private void Live()
        {
            if (_building) return;
            _look.Rings = _rings.IsChecked == true;
            _look.RingSize = _ringSize.Value;
            _look.RingGap = _ringGap.Value;
            _look.RingThickness = _ringThickness.Value;
            _look.RingAngle = Math.Round(_ringAngle.Value);
            _look.RingTexture = _texture.IsChecked == true;
            _look.RingGrain = _ringGrain.Value;
            _look.RingDensity = Math.Round(_ringDensity.Value, 2);
            _ringBox.IsVisible = _look.Rings;
            _grainRow.IsVisible = _look.RingTexture;
            _previewHost.IsVisible = _look.Rings; // l'extension de la fenêtre : elle paraît avec les anneaux
            _preview.Show(_look);
        }

        /// <summary>Lit les champs dans l'allure ; dit le premier qui ne va pas.</summary>
        private bool Accept()
        {
            double value;
            string problem = null;
            if (_kind != WorldKind)
            {
                var sun = _kind == SunKind;
                if (Number(_diameter.Text, out value) && value >= (sun ? 1000 : 1) && value <= (sun ? 1.4e11 : 1e7)) _look.Diameter = !sun && Math.Abs(value - BodyLook.MoonDiameter) < 1e-6 ? 0 : value;
                else problem = sun ? "Le diamètre d'un soleil est un nombre de kilomètres (1 392 700 pour le nôtre)." : "Le diamètre est un nombre de kilomètres, de 1 à dix millions (3 474,8 pour notre Lune).";
                if (Number(_distance.Text, out value) && value >= 1000 && value <= (sun ? 1e13 : 1e9)) _look.Distance = value;
                else if (problem == null) problem = sun ? "La distance est un nombre de kilomètres (149 597 871 de la Terre au Soleil)." : "La distance est un nombre de kilomètres, de 1 000 à un milliard (384 400 pour notre Lune).";
                if (Number(_tilt.Text, out value) && value >= -90 && value <= 90) _look.Tilt = value;
                else if (problem == null) problem = "L'inclinaison est un nombre de degrés, de −90 à 90.";
            }
            Live();
            if (problem != null) { _problem.Text = problem; _problem.IsVisible = true; return false; }
            _result = _look;
            return true;
        }

        internal static BodyLookDialog ForProbe(Window owner, int kind, string name, BodyLook look) { return new BodyLookDialog(owner, kind, name, look, null); }
        internal void SetForProbe(string distance, string tilt, bool rings, string ringSize)
        {
            if (distance != null) _distance.Text = distance;
            if (tilt != null) _tilt.Text = tilt;
            _rings.IsChecked = rings;
            double size;
            if (ringSize != null && Number(ringSize, out size)) _ringSize.Value = size;
            Describe();
        }
        internal void SetRingsForProbe(double gap, double thickness, double angle, bool texture, double grain)
        {
            _ringGap.Value = gap;
            _ringThickness.Value = thickness;
            _ringAngle.Value = angle;
            _texture.IsChecked = texture;
            _ringGrain.Value = grain;
        }
        internal void SetDiameterForProbe(string diameter) { _diameter.Text = diameter; }
        internal void SetQuotientsForProbe(string diameter, string distance, string tilt) { _quotientDiameter.Text = diameter; _quotientDistance.Text = distance; _quotientTilt.Text = tilt; }
        internal bool UseCalculatorShownForProbe { get { return _useCalculator.IsVisible; } }
        internal string CalculatorResultsForProbe { get { return (_resultDiameter.Text ?? "") + " | " + (_resultDistance.Text ?? "") + " | " + (_resultTilt.Text ?? ""); } }
        internal void UseCalculatorForProbe() { UseCalculator(); }
        internal bool CalculatorOpenForProbe { get { return _calculator != null && _calculator.IsOpen; } set { if (_calculator != null) _calculator.IsOpen = value; } }
        internal string FieldsForProbe { get { return (_diameter.Text ?? "") + " | " + (_distance.Text ?? "") + " | " + (_tilt.Text ?? ""); } }
        internal string DiameterEchoForProbe { get { return _diameterEcho.Text ?? ""; } }
        internal bool PreviewShownForProbe { get { return _previewHost.IsVisible; } }
        internal bool GrainRowShownForProbe { get { return _grainRow.IsVisible; } }
        internal BodyPreview PreviewForProbe { get { return _preview; } }
        internal bool RingSizeIsSliderForProbe { get { return _ringSize != null && Equals(_ringSize.Tag, "look-ring-size"); } }
        internal bool FramedForProbe(string tag)
        {
            foreach (var control in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this))
            {
                var box = control as Border;
                if (box != null && Equals(box.Tag, tag)) return box.CornerRadius.TopLeft > 0 && box.BorderThickness.Left > 0;
            }
            return false;
        }
        internal void SetColorsForProbe(string accent, string ring) { _look.Accent = accent; _look.RingColor = ring; FillSwatches(_accents, true); FillSwatches(_ringColors, false); Live(); }
        internal BodyLook AcceptForProbe() { return Accept() ? _result : null; }
        internal string ProblemForProbe { get { return _problem.IsVisible ? _problem.Text : null; } }
        internal string DistanceEchoForProbe { get { return _distanceEcho.Text ?? ""; } }
        internal bool Has(string tag)
        {
            foreach (var control in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this))
            {
                var c = control as Control;
                if (c != null && Equals(c.Tag, tag)) return true;
            }
            return false;
        }

        /// <summary>L'allure telle que validée, ou null si l'auteur renonce. `bodyColor` : la teinte de l'astre, pour l'aperçu.</summary>
        public static async Task<BodyLook> Ask(Window owner, int kind, string name, BodyLook look, string bodyColor = null)
        {
            var dialog = new BodyLookDialog(owner, kind, name, look, bodyColor);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }

    /// <summary>L'APERÇU DE L'ASTRE (lot 16, 17/11, Rémi : « un mockup interactif de la planète ») : une
    /// sphère à la teinte de l'astre, son halo d'accent, ses anneaux tels que la vue planétaire les
    /// dessinera (le même RingArt), sur un fond de ciel. Glisser tourne autour de l'astre.</summary>
    internal sealed class BodyPreview : Control
    {
        private readonly int _kind;
        private readonly Color _body;
        private BodyLook _look = new BodyLook();
        private double _lon = 0.5, _lat = 0.36;
        private bool _dragging;
        private Point _last;
        private int _renders;

        public BodyPreview(int kind, Color body)
        {
            _kind = kind;
            _body = body;
            Width = 320;
            Height = 320;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public void Show(BodyLook look)
        {
            _look = look ?? new BodyLook();
            InvalidateVisual();
        }

        internal int RendersForProbe { get { return _renders; } }
        internal double LongitudeForProbe { get { return _lon; } }
        internal void RotateForProbe(double lon, double lat) { _lon = lon; _lat = lat; InvalidateVisual(); }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _last = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var at = e.GetPosition(this);
            _lon -= (at.X - _last.X) * 0.012;
            _lat = Math.Max(-1.5, Math.Min(1.5, _lat + (at.Y - _last.Y) * 0.012));
            _last = at;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = false;
            e.Pointer.Capture(null);
        }

        /// <summary>Un point de l'astre (longitude, latitude) vers l'écran ; faux sur la face cachée.</summary>
        private bool Project(double lon, double lat, Point centre, double radius, out Point at)
        {
            var l = lon - _lon;
            double sinP = Math.Sin(_lat), cosP = Math.Cos(_lat);
            var x = Math.Cos(lat) * Math.Sin(l);
            var y = cosP * Math.Sin(lat) - sinP * Math.Cos(lat) * Math.Cos(l);
            var z = sinP * Math.Sin(lat) + cosP * Math.Cos(lat) * Math.Cos(l);
            at = new Point(centre.X + radius * x, centre.Y - radius * y);
            return z > 0.02;
        }

        public override void Render(DrawingContext context)
        {
            _renders++;
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            using (context.PushGeometryClip(new RectangleGeometry(bounds) { RadiusX = 12, RadiusY = 12 }))
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1C)), null, bounds);
                var random = new Random(1711);
                for (var i = 0; i < 70; i++)
                {
                    var size = 0.5 + Math.Pow(random.NextDouble(), 3) * 1.4;
                    context.DrawEllipse(new SolidColorBrush(Colors.White, 0.25 + random.NextDouble() * 0.55), null, new Point(random.NextDouble() * bounds.Width, random.NextDouble() * bounds.Height), size, size);
                }
                var centre = new Point(bounds.Width / 2, bounds.Height / 2);
                double inner = 1, outer = 1;
                if (_look.Rings) _look.RingSpan(out inner, out outer);
                // L'astre garde une taille lisible ; des anneaux très amples le rapetissent pour tenir dans la vue.
                var radius = Math.Min(bounds.Width, bounds.Height) / 2 * 0.9 / Math.Max(1.9, outer);

                Color accent;
                if (!string.IsNullOrEmpty(_look.Accent) && Color.TryParse(_look.Accent, out accent))
                {
                    var halo = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Radius = 0.5 };
                    var reach = _kind == BodyLookDialog.SunKind ? 1.9 : 1.22;
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), _kind == BodyLookDialog.SunKind ? 0.3 : 0.74));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(130, accent.R, accent.G, accent.B), _kind == BodyLookDialog.SunKind ? 0.5 : 0.83));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
                    context.DrawEllipse(halo, null, centre, radius * reach, radius * reach);
                }

                // La normale des anneaux : l'axe de l'astre, penché de leur angle — vue d'où l'on regarde.
                Color ring;
                if (string.IsNullOrEmpty(_look.RingColor) || !Color.TryParse(_look.RingColor, out ring))
                    ring = _kind == BodyLookDialog.WorldKind ? Color.FromRgb(0xD9, 0xC9, 0xA6) : Color.FromRgb((byte)(_body.R + (255 - _body.R) * 0.45), (byte)(_body.G + (255 - _body.G) * 0.45), (byte)(_body.B + (255 - _body.B) * 0.45));
                var lean = _look.RingAngle * Math.PI / 180;
                double sinLat = Math.Cos(lean), cosLat = Math.Sin(lean), l = -_lon, sinP = Math.Sin(_lat), cosP = Math.Cos(_lat);
                double nx = cosLat * Math.Sin(l), ny = cosP * sinLat - sinP * cosLat * Math.Cos(l), nz = sinP * sinLat + cosP * cosLat * Math.Cos(l);
                // Lot 17 : les anneaux suivent l'astre quand on le tourne ; éclairé d'en haut à gauche, il y
                // porte son ombre (un soleil, lui, n'en a pas).
                var turn = Math.PI / 2 - _lon;
                var scene = new RingArt.Scene { HasRef = true, Rx = Math.Sin(turn), Ry = -sinP * Math.Cos(turn), Rz = cosP * Math.Cos(turn) };
                if (_kind != BodyLookDialog.SunKind) { scene.HasSun = true; scene.Sx = -0.62; scene.Sy = 0.30; scene.Sz = 0.72; }
                RingArt.Draw(context, centre, radius, nx, ny, nz, _look, ring, 0, false, 512, scene);

                // La sphère : éclairée d'en haut à gauche ; un soleil, lui, brille de partout.
                var ball = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(_kind == BodyLookDialog.SunKind ? 0.5 : 0.36, _kind == BodyLookDialog.SunKind ? 0.5 : 0.32, RelativeUnit.Relative), Radius = 0.5 };
                Func<Color, double, Color> shade = delegate(Color c, double k) { return k >= 1 ? Color.FromRgb((byte)Math.Min(255, c.R + (255 - c.R) * (k - 1)), (byte)Math.Min(255, c.G + (255 - c.G) * (k - 1)), (byte)Math.Min(255, c.B + (255 - c.B) * (k - 1))) : Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)); };
                if (_kind == BodyLookDialog.SunKind)
                {
                    ball.GradientStops.Add(new GradientStop(shade(_body, 1.75), 0));
                    ball.GradientStops.Add(new GradientStop(shade(_body, 1.2), 0.6));
                    ball.GradientStops.Add(new GradientStop(_body, 1));
                }
                else
                {
                    ball.GradientStops.Add(new GradientStop(shade(_body, 1.35), 0));
                    ball.GradientStops.Add(new GradientStop(_body, 0.5));
                    ball.GradientStops.Add(new GradientStop(shade(_body, 0.42), 1));
                }
                context.DrawEllipse(ball, null, centre, radius, radius);
                // Lot 18 : l'ombre des anneaux sur l'astre, selon leur densité.
                if (_look.Rings && scene.HasSun && _look.RingDensity > 0.001) context.DrawImage(RingShade(radius, nx, ny, nz, scene), new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2));

                // Un quadrillage discret : sans lui, on ne verrait pas l'astre tourner.
                var line = new Pen(new SolidColorBrush(Colors.White, _kind == BodyLookDialog.SunKind ? 0.16 : 0.13), 1);
                for (var parallel = -60; parallel <= 60; parallel += 30) Curve(context, line, centre, radius, true, parallel * Math.PI / 180);
                for (var meridian = 0; meridian < 360; meridian += 30) Curve(context, line, centre, radius, false, meridian * Math.PI / 180);

                RingArt.Draw(context, centre, radius, nx, ny, nz, _look, ring, 0, true, 512, scene);
            }
        }

        private WriteableBitmap _shade;
        private int[] _shadeBuffer;

        /// <summary>L'ombre que les anneaux portent sur l'astre de l'aperçu : un voile noir, plus ou moins
        /// opaque selon leur densité, là où ils passent entre sa surface et la lumière.</summary>
        private WriteableBitmap RingShade(double radius, double nx, double ny, double nz, RingArt.Scene scene)
        {
            var side = Math.Max(16, (int)Math.Ceiling(radius * 2));
            if (_shade == null || _shade.PixelSize.Width != side)
            {
                _shade = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
                _shadeBuffer = new int[side * side];
            }
            double inner, outer;
            _look.RingSpan(out inner, out outer);
            var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            float fx = (float)(nx / length), fy = (float)(ny / length), fz = (float)(nz / length);
            float sx = (float)scene.Sx, sy = (float)scene.Sy, sz = (float)scene.Sz, density = (float)Math.Min(1, _look.RingDensity);
            var bands = RingArt.Bands();
            var half = side / 2.0;
            for (var py = 0; py < side; py++)
                for (var px = 0; px < side; px++)
                {
                    float x = (float)((px + 0.5 - half) / half), y = (float)((half - py - 0.5) / half);
                    var d = x * x + y * y;
                    var veil = 0f;
                    if (d < 1)
                    {
                        var z = (float)Math.Sqrt(1 - d);
                        var facing = x * sx + y * sy + z * sz;
                        // (Vers le côté que la lumière n'atteint pas, l'ombre s'efface : pas de bord franc.)
                        if (facing > 0) veil = (1 - RingArt.ShadowOn(x, y, z, sx, sy, sz, fx, fy, fz, (float)inner, (float)outer, bands, density)) * 0.62f * Math.Min(1f, facing / 0.35f);
                    }
                    _shadeBuffer[py * side + px] = (int)(veil * 255) << 24;
                }
            using (var frame = _shade.Lock())
            {
                if (frame.RowBytes == side * 4) System.Runtime.InteropServices.Marshal.Copy(_shadeBuffer, 0, frame.Address, _shadeBuffer.Length);
                else for (var py = 0; py < side; py++) System.Runtime.InteropServices.Marshal.Copy(_shadeBuffer, py * side, frame.Address + py * frame.RowBytes, side);
            }
            return _shade;
        }

        private void Curve(DrawingContext context, Pen pen, Point centre, double radius, bool parallel, double fixedAngle)
        {
            Point previous = default(Point);
            var has = false;
            const int steps = 72;
            for (var i = 0; i <= steps; i++)
            {
                Point at;
                var visible = parallel ? Project(-Math.PI + i * 2 * Math.PI / steps, fixedAngle, centre, radius, out at) : Project(fixedAngle, -Math.PI / 2 + i * Math.PI / steps, centre, radius, out at);
                if (visible && has) context.DrawLine(pen, previous, at);
                previous = at;
                has = visible;
            }
        }
    }
}
