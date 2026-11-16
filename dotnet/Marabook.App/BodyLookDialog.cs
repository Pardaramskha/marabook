using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>PERSONNALISER L'ASTRE (lot 14, 16/11/2026, Rémi) : l'allure d'un soleil, d'un satellite ou
    /// du monde lui-même, telle que la vue Globe la dessine.
    ///
    /// — une COULEUR D'ACCENT : le halo d'un soleil, l'atmosphère du monde ;
    /// — pour un satellite, la DISTANCE qui le sépare de l'astre autour duquel il tourne (par défaut :
    ///   celle de la Terre à la Lune) — elle règle le rayon de son orbite autour du globe ;
    /// — des ANNEAUX, leur couleur, leur ampleur — même autour du monde ;
    /// — une INCLINAISON : l'axe d'un soleil (ses anneaux penchent, et l'orbite d'un compagnon qui tourne
    ///   autour de lui), l'orbite d'un satellite (penchée sur celle du monde). L'axe du monde, lui, reste
    ///   dans « Taille du monde » : c'est de lui que viennent les saisons.</summary>
    internal sealed class BodyLookDialog : Window
    {
        public const int SunKind = 0, MoonKind = 1, WorldKind = 2;
        private static readonly string[] Palette = { "#F5C542", "#E8873A", "#D9503A", "#E7E3D8", "#9DB7E8", "#6FC3C0", "#B48EE0", "#8FCB7A" };
        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        private readonly BodyLook _look;
        private readonly int _kind;
        private readonly StackPanel _accents, _ringColors;
        private readonly TextBox _distance, _tilt, _ringSize;
        private readonly CheckBox _rings;
        private readonly StackPanel _ringBox;
        private readonly TextBlock _distanceEcho, _problem;
        private BodyLook _result;

        private BodyLookDialog(Window owner, int kind, string name, BodyLook look)
        {
            _kind = kind;
            _look = look;
            TimelineUi.Dress(this, owner, "Personnaliser — " + name);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = kind == WorldKind ? "L'allure du monde dans la vue Globe de ses cartes."
                    : kind == SunKind ? "L'allure de ce soleil dans le ciel de la vue Globe."
                    : "L'allure de ce satellite, et sa place autour du monde, dans la vue Globe.",
                Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(TimelineUi.Label(kind == WorldKind ? "Couleur d'accent (son atmosphère) :" : kind == SunKind ? "Couleur d'accent (son halo) :" : "Couleur d'accent :"));
            _accents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "look-accents" };
            panel.Children.Add(_accents);
            if (kind == MoonKind) panel.Children.Add(TimelineUi.Hint("Réservée pour la suite : aujourd'hui, un satellite se dessine dans sa propre couleur."));

            _distance = new TextBox { Width = 120, Text = look.Distance.ToString("0.##", French), Tag = "look-distance" };
            _distanceEcho = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Tag = "look-distance-echo" };
            if (kind == MoonKind)
            {
                panel.Children.Add(TimelineUi.Label("Distance à l'astre autour duquel il tourne :"));
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(_distance);
                row.Children.Add(new TextBlock { Text = "km", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
                row.Children.Add(_distanceEcho);
                panel.Children.Add(row);
                panel.Children.Add(TimelineUi.Hint("Par défaut, la distance de la Terre à la Lune (384 400 km). Sur le globe, l'orbite est resserrée pour tenir dans la vue : plus loin reste plus loin."));
            }

            _tilt = new TextBox { Width = 80, Text = look.Tilt.ToString("0.##", French), Tag = "look-tilt" };
            if (kind != WorldKind)
            {
                panel.Children.Add(TimelineUi.Label(kind == MoonKind ? "Inclinaison de son orbite :" : "Inclinaison de son axe :"));
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(_tilt);
                row.Children.Add(new TextBlock { Text = "degrés", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
                panel.Children.Add(row);
                panel.Children.Add(TimelineUi.Hint(kind == MoonKind
                    ? "0 : il tourne dans le plan de l'orbite du monde. Incliné, il monte au-dessus et descend en dessous à chaque tour. Ses anneaux penchent d'autant."
                    : "Ses anneaux penchent d'autant, et un soleil qui tourne autour de lui suit une orbite inclinée de même."));
            }
            else panel.Children.Add(TimelineUi.Hint("L'axe du monde se règle dans « Taille du monde » (inclinaison de l'axe) : c'est de lui que viennent les saisons. Ses anneaux sont dans le plan de son équateur."));

            _rings = new CheckBox { Content = "Des anneaux", IsChecked = look.Rings, Margin = new Thickness(0, 12, 0, 0), Tag = "look-rings" };
            panel.Children.Add(_rings);
            _ringBox = new StackPanel { Margin = new Thickness(26, 2, 0, 0) };
            _ringBox.Children.Add(TimelineUi.Label("Leur couleur :", 4));
            _ringColors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "look-ring-colors" };
            _ringBox.Children.Add(_ringColors);
            _ringBox.Children.Add(TimelineUi.Label("Leur ampleur :"));
            _ringSize = new TextBox { Width = 80, Text = look.RingSize.ToString("0.##", French), Tag = "look-ring-size" };
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            sizeRow.Children.Add(_ringSize);
            sizeRow.Children.Add(new TextBlock { Text = "× (1 = comme ceux de Saturne, de 0,2 à 4)", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            _ringBox.Children.Add(sizeRow);
            panel.Children.Add(_ringBox);

            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), IsVisible = false };
            panel.Children.Add(_problem);
            var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var reset = Buttons.Text("Remettre d'office", "Sans accent, sans anneaux, sans inclinaison, à la distance de la Terre à la Lune", Buttons.Compact, Buttons.Look.Calm);
            reset.Click += delegate { _result = new BodyLook(); Close(); };
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
            Content = panel;

            _rings.IsCheckedChanged += delegate { _ringBox.IsVisible = _rings.IsChecked == true; };
            _ringBox.IsVisible = look.Rings;
            _distance.TextChanged += delegate { DescribeDistance(); };
            FillSwatches(_accents, true);
            FillSwatches(_ringColors, false);
            DescribeDistance();
        }

        private void FillSwatches(StackPanel host, bool accent)
        {
            host.Children.Clear();
            var current = accent ? _look.Accent : _look.RingColor;
            var none = Buttons.TextToggle("D'office", accent ? "La teinte de l'astre" : "La teinte de l'astre, pâlie", Buttons.Compact);
            none.IsChecked = string.IsNullOrEmpty(current);
            none.Click += delegate { if (accent) _look.Accent = null; else _look.RingColor = null; FillSwatches(host, accent); };
            host.Children.Add(none);
            var colors = new List<string>(Palette);
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
                    BorderBrush = chosen ? Chrome.Ink : Chrome.Border, BorderThickness = new Thickness(chosen ? 2.5 : 1), Cursor = new Cursor(StandardCursorType.Hand)
                };
                swatch.PointerPressed += delegate { if (accent) _look.Accent = color; else _look.RingColor = color; FillSwatches(host, accent); };
                host.Children.Add(swatch);
            }
            var other = Buttons.Text("Autre…", "Une autre couleur", Buttons.Compact, Buttons.Look.Calm);
            other.Click += async delegate
            {
                var picked = await ColorDialog.Ask(this);
                if (string.IsNullOrEmpty(picked)) return;
                if (accent) _look.Accent = picked; else _look.RingColor = picked;
                FillSwatches(host, accent);
            };
            host.Children.Add(other);
        }

        private static bool Number(string text, out double value)
        {
            text = (text ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace("°", "").Replace("−", "-").Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void DescribeDistance()
        {
            double km;
            _distanceEcho.Text = Number(_distance.Text, out km) && km > 0 ? "soit " + BodyLook.DistanceLabel(km) : "";
        }

        /// <summary>Lit les champs dans l'allure ; dit le premier qui ne va pas.</summary>
        private bool Accept()
        {
            double value;
            string problem = null;
            if (_kind == MoonKind)
            {
                if (Number(_distance.Text, out value) && value >= 1000 && value <= 1e9) _look.Distance = value;
                else problem = "La distance est un nombre de kilomètres, de 1 000 à un milliard (384 400 pour notre Lune).";
            }
            if (_kind != WorldKind)
            {
                if (Number(_tilt.Text, out value) && value >= -90 && value <= 90) _look.Tilt = value;
                else if (problem == null) problem = "L'inclinaison est un nombre de degrés, de −90 à 90.";
            }
            _look.Rings = _rings.IsChecked == true;
            if (_look.Rings)
            {
                if (Number(_ringSize.Text, out value) && value >= 0.2 && value <= 4) _look.RingSize = value;
                else if (problem == null) problem = "L'ampleur des anneaux va de 0,2 à 4.";
            }
            if (problem != null) { _problem.Text = problem; _problem.IsVisible = true; return false; }
            _result = _look;
            return true;
        }

        internal static BodyLookDialog ForProbe(Window owner, int kind, string name, BodyLook look) { return new BodyLookDialog(owner, kind, name, look); }
        internal void SetForProbe(string distance, string tilt, bool rings, string ringSize)
        {
            if (distance != null) _distance.Text = distance;
            if (tilt != null) _tilt.Text = tilt;
            _rings.IsChecked = rings;
            if (ringSize != null) _ringSize.Text = ringSize;
            DescribeDistance();
        }
        internal void SetColorsForProbe(string accent, string ring) { _look.Accent = accent; _look.RingColor = ring; FillSwatches(_accents, true); FillSwatches(_ringColors, false); }
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

        /// <summary>L'allure telle que validée, ou null si l'auteur renonce.</summary>
        public static async Task<BodyLook> Ask(Window owner, int kind, string name, BodyLook look)
        {
            var dialog = new BodyLookDialog(owner, kind, name, look);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }
}
