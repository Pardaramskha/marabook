using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LA LUNE (série « Museum », 24/11/2026 au soir — RETOUCHES.md, pièce 4, d'après la
    /// maquette test-menu.jpg de Rémi, 977 × 783 px) : la silhouette des GRANDS dialogues
    /// (Préférences, Sprint) sous Gardien du Feu, Bois Blanc et Assermenté, à la place de la
    /// silhouette galbée (qu'« Ascention » garde). Un CORPS rectangulaire où se pose le contenu,
    /// une grande LUNE qui déborde à droite, coupée en haut et en bas, et une FENTE en croissant
    /// creusée dans la lune. Tout se déduit de la hauteur : k = h / 783.
    ///  · la lune : cercle de centre (W − 147k, 395k), rayon 421k ;
    ///  · la fente : anneau de centre (W − 145k, 255k), rayons 232k → 283k, à droite du corps ;
    ///  · la largeur totale : W + 274k.
    /// Un filet de métal, un second filet plus pâle à 5 px en dedans. Le fond reste de VERRE (le
    /// réglage du skin) : le planétarium n'est donc pas caché par le corps, il est rogné à la fente.</summary>
    internal sealed class MoonFrame : Control
    {
        public const double Reference = 783, Overhang = 274;

        public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<MoonFrame, IBrush>("Fill");
        public static readonly StyledProperty<double> GlassProperty = AvaloniaProperty.Register<MoonFrame, double>("Glass", 1.0);

        static MoonFrame() { AffectsRender<MoonFrame>(FillProperty, GlassProperty); }

        public MoonFrame() { IsHitTestVisible = false; }

        public IBrush Fill { get { return GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public double Glass { get { return GetValue(GlassProperty); } set { SetValue(GlassProperty, value); } }

        /// <summary>Ce que la lune ajoute à droite du corps, pour un dialogue de cette hauteur.</summary>
        public static double Extra(double height) { return Overhang * Math.Max(0, height) / Reference; }

        /// <summary>La largeur du corps d'une silhouette de cette taille.</summary>
        public static double BodyWidth(Size size) { return Math.Max(40, size.Width - Extra(size.Height)); }

        /// <summary>Le corps et la lune réunis, rentrés de <paramref name="inner"/> pixels.</summary>
        private static Geometry Whole(double bodyWidth, double h, double inner)
        {
            double k = h / Reference, corner = Math.Max(0, Math.Min(14, h * 0.03) - inner * 0.5);
            var body = new RectangleGeometry(new Rect(inner + 0.6, inner + 0.6, Math.Max(1, bodyWidth - 2 * inner - 1.2), Math.Max(1, h - 2 * inner - 1.2))) { RadiusX = corner, RadiusY = corner };
            var radius = Math.Max(1, 421 * k - inner);
            var moon = new EllipseGeometry(new Rect(bodyWidth - 147 * k - radius, 395 * k - radius, 2 * radius, 2 * radius));
            var whole = new CombinedGeometry(GeometryCombineMode.Union, body, moon);
            // Coupée en haut et en bas.
            return new CombinedGeometry(GeometryCombineMode.Intersect, whole, new RectangleGeometry(new Rect(-2, inner + 0.6, bodyWidth * 3 + 400, Math.Max(1, h - 2 * inner - 1.2))));
        }

        /// <summary>La fente : l'anneau, à droite du corps, élargi de <paramref name="inner"/> pixels.</summary>
        internal static Geometry Slot(double bodyWidth, double h, double inner)
        {
            double k = h / Reference, cx = bodyWidth - 145 * k, cy = 255 * k, outer = 283 * k + inner, hole = Math.Max(1, 232 * k - inner);
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Rect(cx - outer, cy - outer, 2 * outer, 2 * outer)),
                new EllipseGeometry(new Rect(cx - hole, cy - hole, 2 * hole, 2 * hole)));
            return new CombinedGeometry(GeometryCombineMode.Intersect, ring, new RectangleGeometry(new Rect(bodyWidth - inner, -2, bodyWidth + 400, h + 4)));
        }

        /// <summary>La silhouette entière (corps et lune, moins la fente).</summary>
        internal static Geometry Outline(Size size, double inner)
        {
            var body = BodyWidth(size);
            return new CombinedGeometry(GeometryCombineMode.Exclude, Whole(body, size.Height, inner), Slot(body, size.Height, inner));
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width < 200 || size.Height < 120) return;
            var metal = Chrome.Accent.Color;
            var shape = Outline(size, 0);
            if (Fill != null)
                using (context.PushOpacity(Math.Max(0, Math.Min(1, Glass))))
                    context.DrawGeometry(Fill, null, shape);
            context.DrawGeometry(null, new Pen(Chrome.Accent, 1.2), shape);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x52, metal.R, metal.G, metal.B)), 1), Outline(size, 5));
        }
    }

    /// <summary>LE PLANÉTARIUM de la fente (24/11/2026 au soir) : un ciel sombre, soixante-dix étoiles
    /// qui dérivent d'un tour en 400 s, trois orbites en pointillé très pâle et trois astres pâles
    /// qui tournent lentement (130, 80 et 55 s par tour ; le premier porte un anneau). Tout est
    /// rogné à la fente. Posé sur la seule boîte de la fente (ModalChrome le place) : ses vingt
    /// images par seconde ne repeignent que ce coin du dialogue. Il se fige quand l'auteur coupe
    /// les animations, et s'arrête quand le dialogue se ferme.</summary>
    internal sealed class Planetarium : Control
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly double[] Stars = MakeStars(70);
        private static readonly double[] OrbitAt = { 0.24, 0.50, 0.80 }, Period = { 130, 80, 55 }, StartAt = { 150, 0, 250 }, Shine = { 0.42, 0.36, 0.30 }, Body = { 1.0, 0.72, 0.55 };
        private DispatcherTimer _timer;
        private double _bodyWidth, _height;

        public Planetarium() { IsHitTestVisible = false; ClipToBounds = true; Tag = "modal-planetarium"; }

        /// <summary>Trois nombres par étoile : sa place dans l'épaisseur de l'anneau, son angle, son éclat.</summary>
        private static double[] MakeStars(int count)
        {
            var random = new Random(1124);
            var stars = new double[count * 3];
            for (var i = 0; i < stars.Length; i++) stars[i] = random.NextDouble();
            return stars;
        }

        /// <summary>La silhouette qui le porte : où tombe le bord droit de son corps dans notre repère (une fraction de pixel), et sa hauteur.</summary>
        public void Place(double bodyWidth, double height)
        {
            if (Math.Abs(bodyWidth - _bodyWidth) < 0.01 && Math.Abs(height - _height) < 0.01) return;
            _bodyWidth = bodyWidth;
            _height = height;
            InvalidateVisual();
        }

        internal static bool Animated { get { return AppSettings.Animations; } }
        internal bool RunningForProbe { get { return _timer != null && _timer.IsEnabled; } }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += delegate { if (IsEffectivelyVisible && Animated) InvalidateVisual(); };
            }
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_timer != null) _timer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        public override void Render(DrawingContext context)
        {
            if (_height < 120) return;
            double k = _height / MoonFrame.Reference, hole = 232 * k, outer = 283 * k, width = outer - hole;
            // Notre origine est le bord droit du corps : le centre de la fente est à gauche de nous.
            var center = new Point(_bodyWidth - 145 * k, 255 * k);
            var seconds = Animated ? Clock.Elapsed.TotalSeconds : 0;
            var slot = new CombinedGeometry(GeometryCombineMode.Intersect,
                new CombinedGeometry(GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Rect(center.X - outer, center.Y - outer, 2 * outer, 2 * outer)),
                    new EllipseGeometry(new Rect(center.X - hole, center.Y - hole, 2 * hole, 2 * hole))),
                new RectangleGeometry(new Rect(0, -2, Math.Max(1, Bounds.Width), _height + 4)));
            using (context.PushGeometryClip(slot))
            {
                context.DrawGeometry(new SolidColorBrush(Chrome.Blend(Chrome.WindowBg.Color, Colors.Black, 0.45)), null, slot);
                var ink = Chrome.Dark ? Chrome.Ink.Color : Colors.White;
                var glow = Chrome.Dark ? Chrome.Glow.Color : Chrome.Blend(Chrome.Accent.Color, Colors.White, 0.7);
                // Les étoiles : elles dérivent toutes ensemble.
                var drift = seconds / 400 * 2 * Math.PI;
                for (var i = 0; i < Stars.Length; i += 3)
                {
                    var radius = hole + width * (0.04 + 0.92 * Stars[i]);
                    var angle = Stars[i + 1] * 2 * Math.PI + drift;
                    var point = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                    if (point.X < -2) continue;
                    var size = 0.4 + 0.8 * Stars[i + 2];
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(255 * (0.15 + 0.35 * Stars[i + 2])), ink.R, ink.G, ink.B)), null, point, size, size);
                }
                // Les orbites, puis les astres.
                var dashes = new Pen(new SolidColorBrush(Color.FromArgb(0x12, glow.R, glow.G, glow.B)), 1) { DashStyle = new DashStyle(new double[] { 2, 5 }, 0) };
                for (var i = 0; i < OrbitAt.Length; i++)
                {
                    var radius = hole + width * OrbitAt[i];
                    context.DrawEllipse(null, dashes, center, radius, radius);
                    var angle = (StartAt[i] + 360 * seconds / Period[i]) * Math.PI / 180;
                    var point = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                    if (point.X < -30 * k) continue;
                    var reach = 15 * k * Body[i];
                    var halo = new RadialGradientBrush();
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * Shine[i]), glow.R, glow.G, glow.B), 0));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * Shine[i] * 0.3), glow.R, glow.G, glow.B), 0.4));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
                    context.DrawEllipse(halo, null, point, reach, reach);
                    var heart = Math.Max(1.2, 2.6 * k * Body[i]);
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD0, glow.R, glow.G, glow.B)), null, point, heart, heart);
                    if (i == 0) context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, glow.R, glow.G, glow.B)), 0.8), point, heart * 2.6, heart * 0.9);
                }
            }
        }
    }
}
