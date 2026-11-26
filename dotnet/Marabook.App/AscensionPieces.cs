using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>ASCENSION · ART NOUVEAU, LOT 2 (2.0.0, 25/11/2026 au soir — ce que Rémi a retenu du
    /// cahier « 06-ascension-lot2 ») :
    ///  · la DOUBLE BORDURE des tuiles (TileFrame) : un rectangle droit et un cadre aux angles rentrés,
    ///    qui se croisent — sans animation, il y en a des dizaines à l'écran ;
    ///  · la MÉDAILLE FPDM en losange, à cheval sur le bord haut d'une tuile de fiche (FpdmMedal) ;
    ///  · la ROSACE derrière l'icône d'une fiche sans image (Rosace) ;
    ///  · la RANGÉE DE LOSANGES (DiamondRow) : l'intensité d'un élément de plan, la série du Journal ;
    ///  · le CIEL DE PLANÉTARIUM (PlanetariumSky) : le fond des Préférences et du Journal perso ;
    ///  · le contenu d'une INFOBULLE (TipText) : titre, corps, raccourci en cartouche.
    /// Rien d'ici ne peint sous un autre skin.</summary>
    internal sealed class TileFrame : Control
    {
        /// <summary>Le rayon des angles rentrés, le retrait du rectangle droit, celui du contenu.</summary>
        internal const double Notch = 12, Straight = 5, Room = 7;

        private readonly Border _card;
        private readonly bool _back;
        private IBrush _fill;
        private bool _lit, _danger, _syncing;

        private TileFrame(Border card, bool back)
        {
            _card = card;
            _back = back;
            IsHitTestVisible = false;
            Tag = back ? "tile-back" : "tile-frame";
        }

        internal bool LitForProbe { get { return _lit; } }

        /// <summary>Habille une tuile : son contenu rentre de 7 px, son fond et son filet deviennent ceux
        /// du cadre (le filet de la tuile ne sert plus qu'à DIRE l'état : survolée, choisie, en défaut).
        /// À appeler quand la tuile a son contenu, AVANT CardLift.Attach. Rend le plan des couches —
        /// on y épingle la médaille — ou null hors d'« Ascension ».</summary>
        public static Panel Dress(Border card)
        {
            if (!AscensionArt.On || card == null || card.Child == null) return null;
            var content = card.Child;
            card.Child = null;
            var pad = card.Padding;
            var hold = new Border
            {
                Child = content,
                Padding = new Thickness(Math.Max(0, Room - pad.Left), Math.Max(0, Room - pad.Top), Math.Max(0, Room - pad.Right), Math.Max(0, Room - pad.Bottom))
            };
            var back = new TileFrame(card, true) { _fill = card.Background ?? Chrome.CardBg };
            var front = new TileFrame(card, false);
            card.Background = Brushes.Transparent; // la tuile reste cliquable partout
            var layers = new Panel();
            layers.Children.Add(back);
            layers.Children.Add(hold);
            layers.Children.Add(front);
            card.Child = layers;
            front.Sync();
            card.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Border.BorderBrushProperty) front.Sync();
                else if (e.Property == Border.BorderThicknessProperty || e.Property == BoundsProperty) { back.InvalidateVisual(); front.InvalidateVisual(); }
                else if (e.Property == Border.BackgroundProperty && !front._syncing)
                {
                    // Un fond reposé après coup (une couleur de carte) passe au cadre.
                    var brush = card.Background as ISolidColorBrush;
                    if (brush != null && brush.Color.A == 0) return;
                    back._fill = card.Background;
                    front._syncing = true;
                    card.Background = Brushes.Transparent;
                    front._syncing = false;
                    back.InvalidateVisual();
                }
            };
            return layers;
        }

        /// <summary>Épingle un ornement à cheval sur le milieu du bord haut d'une tuile habillée.</summary>
        public static void PinTop(Panel layers, Control ornament, double above)
        {
            if (layers == null || ornament == null) return;
            ornament.HorizontalAlignment = HorizontalAlignment.Center;
            ornament.VerticalAlignment = VerticalAlignment.Top;
            ornament.Margin = new Thickness(0, -above - 1, 0, 0);
            layers.Children.Add(ornament);
        }

        /// <summary>Le filet de la tuile dit son état ; le cadre le lit, puis l'efface.</summary>
        private void Sync()
        {
            if (_syncing) return;
            var brush = _card.BorderBrush as ISolidColorBrush;
            if (brush == null || brush.Color.A == 0) return;
            _lit = brush.Color == Chrome.Accent.Color;
            _danger = brush.Color == Chrome.Danger.Color;
            _syncing = true;
            _card.BorderBrush = Brushes.Transparent;
            _syncing = false;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        /// <summary>Le cadre aux angles rentrés, pour une tuile de cette taille (rentré de 1 px).</summary>
        internal static Geometry Outline(double w, double h, double r)
        {
            var geometry = new StreamGeometry();
            var size = new Size(r, r);
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(1 + r, 1), true);
                path.LineTo(new Point(w - 1 - r, 1));
                path.ArcTo(new Point(w - 1, 1 + r), size, 0, false, SweepDirection.CounterClockwise);
                path.LineTo(new Point(w - 1, h - 1 - r));
                path.ArcTo(new Point(w - 1 - r, h - 1), size, 0, false, SweepDirection.CounterClockwise);
                path.LineTo(new Point(1 + r, h - 1));
                path.ArcTo(new Point(1, h - 1 - r), size, 0, false, SweepDirection.CounterClockwise);
                path.LineTo(new Point(1, 1 + r));
                path.ArcTo(new Point(1 + r, 1), size, 0, false, SweepDirection.CounterClockwise);
                path.EndFigure(true);
            }
            return geometry;
        }

        public override void Render(DrawingContext context)
        {
            if (!AscensionArt.On) return;
            double w = _card.Bounds.Width, h = _card.Bounds.Height;
            if (w < 40 || h < 30) return;
            var line = _card.BorderThickness;
            var pad = _card.Padding;
            var r = Math.Min(Notch, Math.Min(w, h) / 4);
            using (context.PushTransform(Matrix.CreateTranslation(-(line.Left + pad.Left), -(line.Top + pad.Top))))
            {
                var outline = Outline(w, h, r);
                if (_back) { if (_fill != null) context.DrawGeometry(_fill, null, outline); return; }
                Color gold = AscensionArt.Gold, low = AscensionArt.GoldLo, pale = Chrome.Blend(AscensionArt.GoldPale, Colors.White, 0.4);
                // La droite : un rectangle rentré de 5 px.
                var straight = _danger ? Chrome.Danger.Color : Chrome.Blend(gold, low, 0.45);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(AscensionArt.WithAlpha(straight, 0.85)), 1), new Rect(Straight + 0.5, Straight + 0.5, w - 2 * Straight - 1, h - 2 * Straight - 1));
                // La galbée : fixe (pas de reflet qui balaie), plus claire et plus forte quand la tuile est survolée ou choisie.
                IBrush metal;
                if (_danger) metal = Chrome.Danger;
                else if (_lit) metal = new SolidColorBrush(pale);
                else
                {
                    var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
                    brush.GradientStops.Add(new GradientStop(low, 0));
                    brush.GradientStops.Add(new GradientStop(gold, 0.30));
                    brush.GradientStops.Add(new GradientStop(pale, 0.46));
                    brush.GradientStops.Add(new GradientStop(gold, 0.62));
                    brush.GradientStops.Add(new GradientStop(low, 1));
                    metal = brush;
                }
                context.DrawGeometry(null, new Pen(metal, _lit || _danger ? 2 : 1.3), outline);
                // Les huit croisements : là où l'arc d'un angle coupe la droite.
                var reach = Straight + 0.5 - 1;
                if (r <= reach + 1) return;
                var k = 1 + Math.Sqrt(r * r - reach * reach);
                var dot = new SolidColorBrush(AscensionArt.GoldPale);
                var s = Straight + 0.5;
                foreach (var x in new[] { s, w - s })
                    foreach (var y in new[] { k, h - k })
                    {
                        context.DrawEllipse(dot, null, new Point(x, y), 1.9, 1.9);
                        context.DrawEllipse(dot, null, new Point(x == s ? k : w - k, y == k ? s : h - s), 1.9, 1.9);
                    }
            }
        }
    }

    /// <summary>LA MÉDAILLE D'UNE FICHE DE MODULE (FPDM) : un losange de 22 px. Incomplète : face bleu
    /// nuit, listel d'or, remplie de MAGIE depuis le bas au prorata des champs remplis. Complète
    /// (100 %) : face d'or, losange gravé, étoile pâle, halo.</summary>
    internal sealed class FpdmMedal : Control
    {
        public const double Side = 22;
        private readonly double _ratio;
        private readonly bool _complete;

        public FpdmMedal(int filled, int total, bool complete)
        {
            Width = Height = Side;
            _complete = complete;
            _ratio = complete ? 1 : total <= 0 ? 0 : Math.Max(0, Math.Min(1, filled / (double)total));
            Tag = "fpdm-medal";
        }

        internal bool CompleteForProbe { get { return _complete; } }
        internal double RatioForProbe { get { return _ratio; } }

        private static readonly Geometry Star = Geometry.Parse("M0 -2.6L.9-.9 2.6 0 .9.9 0 2.6-.9.9-2.6 0-.9-.9z");

        public override void Render(DrawingContext context)
        {
            // Toute la surface prend la souris (l'infobulle).
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            var zero = new Point(0, 0);
            using (context.PushTransform(Matrix.CreateScale(Side / 24, Side / 24) * Matrix.CreateTranslation(Side / 2, Side / 2)))
            {
                if (_complete)
                {
                    var halo = new RadialGradientBrush();
                    halo.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0.55), 0));
                    halo.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0), 1));
                    context.DrawEllipse(halo, null, zero, 14, 14);
                    AscensionArt.Diamond(context, zero, 10.5, new SolidColorBrush(AscensionArt.Gold), new Pen(new SolidColorBrush(Color.Parse("#FBEFC8")), 1.3));
                    AscensionArt.Diamond(context, zero, 6, null, new Pen(new SolidColorBrush(AscensionArt.GoldLo), 1));
                    context.DrawGeometry(new SolidColorBrush(Color.Parse("#FFF6DC")), null, Star);
                    return;
                }
                AscensionArt.Diamond(context, zero, 11.4, new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), null); // l'ombre
                AscensionArt.Diamond(context, zero, 10.5, new SolidColorBrush(Color.Parse("#0E2034")), new Pen(new SolidColorBrush(AscensionArt.Gold), 1.3));
                if (_ratio <= 0) return;
                var top = 8 - 16 * _ratio;
                var inside = new PolylineGeometry(new[] { new Point(-8, 0), new Point(0, -8), new Point(8, 0), new Point(0, 8) }, true);
                using (context.PushGeometryClip(inside))
                {
                    context.FillRectangle(new SolidColorBrush(AscensionArt.Magic), new Rect(-8, top, 16, 8 - top));
                    context.FillRectangle(new SolidColorBrush(AscensionArt.MagicHi), new Rect(-8, top, 16, 1));
                }
            }
        }
    }

    /// <summary>LA ROSACE : un seul dessin, très pâle, derrière l'icône d'une fiche sans image — douze
    /// pétales, deux cercles, un cœur à double cercle, douze petits losanges entre les pétales.</summary>
    internal sealed class Rosace : Control
    {
        public Rosace() { IsHitTestVisible = false; Tag = "rosace"; }

        private static readonly Geometry Lozenge = Geometry.Parse("M50 6l3 4-3 4-3-4z");

        public override void Render(DrawingContext context)
        {
            if (!AscensionArt.On) return;
            var side = Math.Min(Bounds.Width, Bounds.Height);
            if (side < 30) return;
            var ink = new SolidColorBrush(AscensionArt.VitrailInk(Chrome.Dark));
            var pen = new Pen(ink, 0.8);
            var k = side / 100;
            using (context.PushOpacity(Chrome.Dark ? 0.17 : 0.13))
            using (context.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2)))
            {
                var center = new Point(50, 50);
                context.DrawEllipse(null, pen, center, 47, 47);
                context.DrawEllipse(null, pen, center, 42, 42);
                context.DrawEllipse(null, pen, center, 12, 12);
                context.DrawEllipse(null, pen, center, 7, 7);
                for (var i = 0; i < 12; i++)
                {
                    using (context.PushTransform(Matrix.CreateTranslation(-50, -50) * Matrix.CreateRotation(i * Math.PI / 6) * Matrix.CreateTranslation(50, 50)))
                        context.DrawEllipse(null, pen, new Point(50, 27), 7, 17);
                    using (context.PushTransform(Matrix.CreateTranslation(-50, -50) * Matrix.CreateRotation((i + 0.5) * Math.PI / 6) * Matrix.CreateTranslation(50, 50)))
                        context.DrawGeometry(ink, null, Lozenge);
                }
            }
        }

        /// <summary>L'icône d'une fiche sans image, posée sur sa rosace (« Ascension » seulement).</summary>
        public static Control Behind(Control icon, double iconSize)
        {
            if (!AscensionArt.On || icon == null || iconSize < 30) return icon;
            var side = Math.Round(iconSize * 2.3);
            var panel = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new Rosace { Width = side, Height = side });
            panel.Children.Add(icon);
            return panel;
        }
    }

    /// <summary>LA RANGÉE DE LOSANGES : une progression par crans. Les crans tenus sont d'or pâle, les
    /// autres creux ; un cran peut être de MAGIE (aujourd'hui, dans la série du Journal).</summary>
    internal sealed class DiamondRow : Control
    {
        private readonly int _count, _magicAt;
        private readonly Func<int, bool> _held;
        private readonly double _half, _gap;

        /// <param name="count">le nombre de crans</param>
        /// <param name="held">vrai pour un cran tenu (de 0 à count − 1)</param>
        /// <param name="size">la diagonale d'un losange</param>
        /// <param name="magicAt">le cran de magie, ou −1</param>
        public DiamondRow(int count, Func<int, bool> held, double size, int magicAt = -1)
        {
            _count = count;
            _held = held;
            _half = size / 2;
            _gap = Math.Max(2, Math.Round(size / 3));
            _magicAt = magicAt;
            IsHitTestVisible = false;
            Tag = "diamond-row";
            Width = count * size + (count - 1) * _gap + 2;
            Height = size + 2;
        }

        internal int CountForProbe { get { return _count; } }

        public override void Render(DrawingContext context)
        {
            var y = Bounds.Height / 2;
            var gold = new SolidColorBrush(AscensionArt.Gold);
            for (var i = 0; i < _count; i++)
            {
                var center = new Point(1 + _half + i * (2 * _half + _gap), y);
                var held = _held != null && _held(i);
                if (i == _magicAt) AscensionArt.Diamond(context, center, _half, new SolidColorBrush(held ? AscensionArt.Magic : AscensionArt.WithAlpha(AscensionArt.Magic, 0.25)), new Pen(new SolidColorBrush(AscensionArt.MagicHi), 0.8));
                else if (held) AscensionArt.Diamond(context, center, _half, new SolidColorBrush(AscensionArt.GoldPale), new Pen(gold, 0.8));
                else AscensionArt.Diamond(context, center, _half - 0.5, null, new Pen(new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.55)), 0.9));
            }
        }
    }

    /// <summary>LE CIEL DE PLANÉTARIUM (le fond des Préférences et du Journal perso sous « Ascension ») :
    /// un ciel qui s'éclaircit vers un PÔLE hors champ (0,80 L ; 1,08 H), 520 étoiles qui tournent
    /// autour de lui en dix minutes, quatre constellations, une SPHÈRE ARMILLAIRE dorée très pâle qui
    /// tourne à l'envers en quinze minutes, trois orbites inclinées et leurs astres. Tout est lent.
    /// Dix images par seconde, seulement quand les ornements d'« Ascension » peuvent bouger (animations
    /// permises, fenêtre active) et que l'auteur ne fait rien (AscensionArt.Idle) ; figé à l'instant zéro
    /// quand les animations sont coupées ou sous une sonde. Au clair (non maquetté), le même dessin sur un ciel d'aube : le papier du skin, les
    /// astres en bronze.</summary>
    internal sealed class PlanetariumSky : Control
    {
        private const int StarCount = 520;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly double[] Stars = MakeStars(); // rayon, angle, éclat, phase
        private static readonly double[][] Orbits =
        {
            // rayon (en hauteurs), écrasement, période (s), taille, anneau
            new[] { 0.42, 0.32, 120, 3.2, 1 },
            new[] { 0.62, 0.28, 180, 2.4, 0 },
            new[] { 0.98, 0.22, 260, 2.8, 0 }
        };
        private DispatcherTimer _timer;
        private double _time, _lastTick = -1; // le temps que CE ciel a tourné, en secondes : une pause ne fait rien sauter
        private List<int[]> _chains;
        private Size _chainsFor;
        private readonly Dictionary<uint, IBrush> _inks = new Dictionary<uint, IBrush>();

        public PlanetariumSky() { IsHitTestVisible = false; ClipToBounds = true; Tag = "planetarium-sky"; }

        /// <summary>Le générateur de la maquette (graine 1124) : le même ciel partout.</summary>
        private static double[] MakeStars()
        {
            long seed = 1124;
            Func<double> next = delegate { seed = seed * 16807 % 2147483647; return seed / 2147483647.0; };
            var stars = new double[StarCount * 4];
            for (var i = 0; i < StarCount; i++)
            {
                stars[i * 4] = Math.Sqrt(next()) * 1.25;
                stars[i * 4 + 1] = next() * Math.PI * 2;
                stars[i * 4 + 2] = next();
                stars[i * 4 + 3] = next() * 6.28;
            }
            return stars;
        }

        /// <summary>Vrai quand le ciel tourne (sinon il est figé à l'instant zéro).</summary>
        internal static bool Turning { get { var launch = App.Launch; return AppSettings.Animations && (launch == null || !launch.Unattended || AscensionArt.ForcedForBench); } }
        internal bool RunningForProbe { get { return _timer != null && _timer.IsEnabled; } }
        internal int ChainsForProbe { get { return _chains == null ? 0 : _chains.Count; } }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_timer == null)
            {
                // Patch performance (26/11) : dix images par seconde (tout y est lent), et RIEN pendant que
                // l'auteur agit — un ciel qui se repeint fait redessiner toute la fenêtre posée dessus.
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, delegate
                {
                    var now = Clock.Elapsed.TotalSeconds;
                    if (!IsEffectivelyVisible || !AscensionArt.Moving || !AscensionArt.Idle) { _lastTick = -1; return; }
                    if (_lastTick >= 0) _time += Math.Min(0.5, now - _lastTick);
                    _lastTick = now;
                    InvalidateVisual();
                });
            }
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_timer != null) _timer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private IBrush Ink(Color color, double alpha)
        {
            var a = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, alpha)) / 8) * 8;
            if (a > 255) a = 255;
            var key = (uint)(a << 24 | color.R << 16 | color.G << 8 | color.B);
            IBrush brush;
            if (_inks.TryGetValue(key, out brush)) return brush;
            brush = new ImmutableSolidColorBrush(Color.FromArgb((byte)a, color.R, color.G, color.B));
            if (_inks.Count > 400) _inks.Clear();
            _inks[key] = brush;
            return brush;
        }

        /// <summary>Quatre chaînes de 4 ou 5 étoiles voisines (moins de 95 px entre deux), tirées une fois par taille.</summary>
        private void MakeChains(double w, double h, double px, double py, double d)
        {
            _chains = new List<int[]>();
            _chainsFor = new Size(w, h);
            var seen = new List<int>();
            for (var i = 0; i < StarCount; i++)
            {
                double x = px + Math.Cos(Stars[i * 4 + 1]) * Stars[i * 4] * d, y = py + Math.Sin(Stars[i * 4 + 1]) * Stars[i * 4] * d;
                if (x > 40 && x < w - 80 && y > 30 && y < h - 30 && Stars[i * 4 + 2] > 0.55) seen.Add(i);
            }
            Func<int, Point> at = delegate(int i) { return new Point(px + Math.Cos(Stars[i * 4 + 1]) * Stars[i * 4] * d, py + Math.Sin(Stars[i * 4 + 1]) * Stars[i * 4] * d); };
            seen.Sort(delegate(int a, int b) { return at(a).X.CompareTo(at(b).X); });
            var used = new HashSet<int>();
            var plan = new[] { new[] { 5.0, 0.15 }, new[] { 4.0, 0.45 }, new[] { 5.0, 0.70 }, new[] { 4.0, 0.90 } };
            foreach (var step in plan)
            {
                if (seen.Count == 0) break;
                var current = seen[Math.Min(seen.Count - 1, (int)Math.Floor(seen.Count * step[1]))];
                if (used.Contains(current)) continue;
                var chain = new List<int> { current };
                used.Add(current);
                for (var j = 1; j < (int)step[0]; j++)
                {
                    var best = -1;
                    var nearest = 1e9;
                    var from = at(current);
                    foreach (var other in seen)
                    {
                        if (used.Contains(other)) continue;
                        var to = at(other);
                        var distance = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
                        if (distance > 18 && distance < nearest) { nearest = distance; best = other; }
                    }
                    if (best < 0 || nearest > Math.Max(95, d * 0.14)) break; // la maquette disait 95 px : trop court pour un grand ciel, aucune chaîne n'y tenait
                    chain.Add(best);
                    used.Add(best);
                    current = best;
                }
                if (chain.Count > 2) _chains.Add(chain.ToArray());
            }
        }

        public override void Render(DrawingContext context)
        {
            if (!AscensionArt.On) return;
            double w = Bounds.Width, h = Bounds.Height;
            if (w < 80 || h < 80) return;
            var dark = Chrome.Dark;
            var t = Turning ? _time : 0;
            double px = w * 0.80, py = h * 1.08, d = Math.Sqrt(w * w + h * h) * 1.05;
            // Le ciel.
            Color c0, c1, c2, starInk, gold = AscensionArt.Gold, magicHi = dark ? AscensionArt.MagicHi : AscensionArt.MagicDeep;
            if (dark) { c0 = Color.Parse("#163250"); c1 = Color.Parse("#0A1A2E"); c2 = Color.Parse("#030B15"); starInk = Color.Parse("#F0E6D2"); }
            else { c0 = Chrome.Blend(Chrome.RaisedBg.Color, Colors.White, 0.5); c1 = Chrome.WindowBg.Color; c2 = Chrome.Blend(Chrome.WindowBg.Color, Chrome.Ink.Color, 0.10); starInk = AscensionArt.GoldLo; }
            var sky = new RadialGradientBrush
            {
                Center = new RelativePoint(px, py, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(px, py, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(d, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(d, RelativeUnit.Absolute)
            };
            sky.GradientStops.Add(new GradientStop(c0, 0));
            sky.GradientStops.Add(new GradientStop(c1, 0.45));
            sky.GradientStops.Add(new GradientStop(c2, 1));
            context.FillRectangle(sky, new Rect(0, 0, w, h));

            var rot = t / 600 * Math.PI * 2;
            // Les constellations.
            if (_chains == null || _chainsFor != new Size(w, h)) MakeChains(w, h, px, py, d);
            var thread = new Pen(Ink(gold, dark ? 0.20 : 0.30), 0.8);
            foreach (var chain in _chains)
                for (var j = 1; j < chain.Length; j++)
                {
                    int a = chain[j - 1], b = chain[j];
                    context.DrawLine(thread,
                        new Point(px + Math.Cos(Stars[a * 4 + 1] + rot) * Stars[a * 4] * d, py + Math.Sin(Stars[a * 4 + 1] + rot) * Stars[a * 4] * d),
                        new Point(px + Math.Cos(Stars[b * 4 + 1] + rot) * Stars[b * 4] * d, py + Math.Sin(Stars[b * 4 + 1] + rot) * Stars[b * 4] * d));
                }
            // Les étoiles.
            var halo = Ink(magicHi, 0.12);
            for (var i = 0; i < StarCount; i++)
            {
                double radius = Stars[i * 4] * d, angle = Stars[i * 4 + 1] + rot, m = Stars[i * 4 + 2];
                double x = px + Math.Cos(angle) * radius, y = py + Math.Sin(angle) * radius;
                if (x < -4 || y < -4 || x > w + 4 || y > h + 4) continue;
                var twinkle = 0.75 + 0.25 * Math.Sin(t * 0.8 + Stars[i * 4 + 3] * 6);
                var size = 0.5 + 1.1 * m;
                context.DrawEllipse(Ink(starInk, (0.18 + 0.55 * m * m) * twinkle), null, new Point(x, y), size, size);
                if (m > 0.93) context.DrawEllipse(halo, null, new Point(x, y), 4, 4);
            }
            // La sphère armillaire : elle tourne à l'envers.
            var ar = h * 0.78;
            var ring = new Pen(Ink(gold, dark ? 0.16 : 0.26), 1);
            using (context.PushTransform(Matrix.CreateRotation(-t / 900 * Math.PI * 2) * Matrix.CreateTranslation(px, py)))
            {
                var zero = new Point(0, 0);
                context.DrawEllipse(null, ring, zero, ar, ar);
                context.DrawEllipse(null, ring, zero, ar - 14, ar - 14);
                for (var i = 0; i < 144; i++)
                {
                    double a = i / 144.0 * Math.PI * 2, reach = i % 6 == 0 ? 10 : 4, cos = Math.Cos(a), sin = Math.Sin(a);
                    context.DrawLine(ring, new Point(cos * (ar - 14), sin * (ar - 14)), new Point(cos * (ar - 14 + reach), sin * (ar - 14 + reach)));
                }
                var lozenge = Ink(gold, dark ? 0.22 : 0.32);
                for (var i = 0; i < 12; i++)
                {
                    var a = i / 12.0 * Math.PI * 2;
                    AscensionArt.Diamond(context, new Point(Math.Cos(a) * (ar + 12), Math.Sin(a) * (ar + 12)), 3.5, lozenge, null);
                }
            }
            // Les orbites inclinées et leurs astres.
            var dashes = new Pen(Ink(magicHi, dark ? 0.10 : 0.22), 1) { DashStyle = new DashStyle(new double[] { 2, 5 }, 0) };
            var bodies = new[] { starInk, magicHi, dark ? Color.Parse("#E3C47A") : gold };
            using (context.PushTransform(Matrix.CreateRotation(-0.42) * Matrix.CreateTranslation(px - w * 0.32, py - h * 0.62)))
                for (var i = 0; i < Orbits.Length; i++)
                {
                    double rr = h * Orbits[i][0], squash = Orbits[i][1], size = Orbits[i][3];
                    context.DrawEllipse(null, dashes, new Point(0, 0), rr, rr * squash);
                    var a = t / Orbits[i][2] * Math.PI * 2 + rr;
                    var body = new Point(Math.Cos(a) * rr, Math.Sin(a) * rr * squash);
                    var glow = new RadialGradientBrush();
                    glow.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(magicHi, 0.18), 0));
                    glow.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(magicHi, 0), 1));
                    context.DrawEllipse(glow, null, body, size * 5, size * 5);
                    var ink = Ink(bodies[i], 0.55);
                    context.DrawEllipse(ink, null, body, size, size);
                    if (Orbits[i][4] > 0)
                        using (context.PushTransform(Matrix.CreateRotation(-0.3) * Matrix.CreateTranslation(body.X, body.Y)))
                            context.DrawEllipse(null, new Pen(ink, 0.8), new Point(0, 0), size * 2.4, size * 0.8);
                }
            // Le voile du haut : le titre reste lisible.
            if (!dark) return;
            var veil = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            veil.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, 1, 10, 19), 0));
            veil.GradientStops.Add(new GradientStop(Color.FromArgb(0, 1, 10, 19), 0.3));
            context.FillRectangle(veil, new Rect(0, 0, w, h));
        }
    }

    /// <summary>LE SÉPARATEUR À VOLUTES, en ligne : deux filets, des volutes aux bouts, un losange au centre.</summary>
    internal sealed class SeparatorLine : Control
    {
        public SeparatorLine() { IsHitTestVisible = false; Height = 18; Tag = "separator-line"; }

        public override void Render(DrawingContext context)
        {
            if (AscensionArt.On) AscensionArt.Separator(context, 4, Bounds.Width - 4, Math.Floor(Bounds.Height / 2) + 0.5);
        }
    }

    /// <summary>LES ORNEMENTS D'UNE INFOBULLE (« Ascension ») : l'arête haute dorée et quatre losanges à
    /// cheval sur les angles de la plaque. Posé par-dessus la plaque, dans le gabarit (Theme.TipFace).</summary>
    internal sealed class TipOrnaments : Control
    {
        public TipOrnaments() { IsHitTestVisible = false; Tag = "tip-ornaments"; }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w < 8 || h < 8) return;
            Color gold = AscensionArt.Gold, low = AscensionArt.GoldLo, pale = Color.Parse("#F0E6D2");
            var edge = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(w, 0, RelativeUnit.Absolute) };
            edge.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(low, 0), 0));
            edge.GradientStops.Add(new GradientStop(low, 0.25));
            edge.GradientStops.Add(new GradientStop(pale, 0.5));
            edge.GradientStops.Add(new GradientStop(low, 0.75));
            edge.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(low, 0), 1));
            context.DrawLine(new Pen(edge, 1), new Point(0, 0.5), new Point(w, 0.5));
            var fill = new SolidColorBrush(gold);
            foreach (var x in new[] { 0.5, w - 0.5 })
                foreach (var y in new[] { 0.5, h - 0.5 })
                    AscensionArt.Diamond(context, new Point(x, y), 3.5, fill, null);
        }
    }

    /// <summary>LE TEXTE D'UNE INFOBULLE (« Ascension ») : un titre facultatif en capitales dans la police
    /// des titres, suivi d'un filet qui s'efface ; le corps ; le raccourci clavier, s'il finit le texte
    /// entre parenthèses, sorti dans un petit cartouche ; les lignes suivantes en plus pâle.</summary>
    internal static class TipText
    {
        /// <summary>La marque d'un titre : un texte « marque + titre + saut de ligne + corps ».</summary>
        private const char TitleMark = '⁠';

        /// <summary>Un texte d'infobulle à TITRE. Hors d'« Ascension », le corps seul.</summary>
        public static string Titled(string title, string body)
        {
            return AscensionArt.On && !string.IsNullOrEmpty(title) ? TitleMark + title + "\n" + body : body;
        }

        private static readonly Regex Shortcut = new Regex(
            @"\s*\(((?:(?:Ctrl|Cmd|Alt|Maj|Shift|Option|Opt|⌘|⌥|⇧|⌃)\s*\+?\s*)+[^\s()+]{1,8}|F\d{1,2}|Échap|Entrée|Suppr|Tab|Espace)\)\s*$",
            RegexOptions.CultureInvariant);

        private static readonly Color Pale = Color.Parse("#F0E6D2"), Soft = Color.Parse("#A9A08C");

        /// <summary>Le contenu d'une infobulle : un texte devient sa mise en forme ; tout autre contenu passe tel quel.</summary>
        public static object Build(object content)
        {
            var text = content as string;
            if (text == null || text.Length == 0) return content;
            string title = null;
            if (text[0] == TitleMark)
            {
                var cut = text.IndexOf('\n');
                if (cut > 0) { title = text.Substring(1, cut - 1); text = text.Substring(cut + 1); }
                else text = text.Substring(1);
            }
            // Sous un titre, la première ligne est le corps ; les suivantes, des précisions.
            string more = null;
            var line = text.IndexOf('\n');
            if (title != null && line > 0) { more = text.Substring(line + 1).Trim(); text = text.Substring(0, line); }
            string keys = null;
            var match = Shortcut.Match(text);
            if (match.Success && match.Index > 0) { keys = match.Groups[1].Value; text = text.Substring(0, match.Index); }

            var stack = new StackPanel { Tag = "tip-text" };
            if (title != null)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var name = new TextBlock
                {
                    Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                    FontFamily = Skin.TitleFont ?? FontFamily.Default,
                    FontSize = 11.5,
                    LetterSpacing = 1.6,
                    Foreground = new SolidColorBrush(AscensionArt.Gold),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "tip-title"
                };
                DockPanel.SetDock(name, Dock.Left);
                head.Children.Add(name);
                var fade = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
                fade.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0.7), 0));
                fade.GradientStops.Add(new GradientStop(AscensionArt.WithAlpha(AscensionArt.Gold, 0), 1));
                head.Children.Add(new Border { Height = 1, MinWidth = 24, Background = fade, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                stack.Children.Add(head);
            }
            var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Pale), VerticalAlignment = VerticalAlignment.Center, Text = text };
            if (keys == null) stack.Children.Add(body);
            else
            {
                var row = new DockPanel();
                var plate = new Border
                {
                    BorderBrush = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.5)),
                    BorderThickness = new Thickness(1, 1, 1, 2),
                    Background = new SolidColorBrush(AscensionArt.WithAlpha(AscensionArt.Gold, 0.08)),
                    Padding = new Thickness(5, 0, 5, 0),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Tag = "tip-keys",
                    Child = new TextBlock { Text = keys, FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Pale) }
                };
                DockPanel.SetDock(plate, Dock.Right);
                row.Children.Add(plate);
                row.Children.Add(body);
                stack.Children.Add(row);
            }
            if (!string.IsNullOrEmpty(more))
                stack.Children.Add(new TextBlock { Text = more, TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Foreground = new SolidColorBrush(Soft), Margin = new Thickness(0, 2, 0, 0) });
            return stack;
        }
    }
}
