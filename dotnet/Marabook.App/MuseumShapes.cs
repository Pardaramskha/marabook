using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE CADRE À COINS COUPÉS (série « Museum », 24/11/2026 au soir) — celui du menu
    /// contextuel : bords droits, coin haut droit et coin bas gauche coupés en diagonale, et un
    /// SECOND filet de métal, plus transparent, écarté du premier, qui suit la découpe. Le
    /// second filet vit dans la marge du cadre (Ring) : le contenu est d'autant plus petit.</summary>
    internal sealed class CutFrame : Decorator
    {
        public const double Cut = 14, Ring = 6;

        public static readonly StyledProperty<IBrush> BackgroundProperty = AvaloniaProperty.Register<CutFrame, IBrush>("Background");
        public static readonly StyledProperty<IBrush> BorderBrushProperty = AvaloniaProperty.Register<CutFrame, IBrush>("BorderBrush");

        static CutFrame() { AffectsRender<CutFrame>(BackgroundProperty, BorderBrushProperty); }

        public IBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public IBrush BorderBrush { get { return GetValue(BorderBrushProperty); } set { SetValue(BorderBrushProperty, value); } }

        private Thickness Around { get { return new Thickness(Padding.Left + Ring + 1, Padding.Top + Ring + 1, Padding.Right + Ring + 1, Padding.Bottom + Ring + 1); } }

        protected override Size MeasureOverride(Size availableSize)
        {
            var around = Around;
            if (Child == null) return new Size(around.Left + around.Right, around.Top + around.Bottom);
            Child.Measure(availableSize.Deflate(around));
            return Child.DesiredSize.Inflate(around);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child != null) Child.Arrange(new Rect(finalSize).Deflate(Around));
            return finalSize;
        }

        /// <summary>Le contour : un rectangle aux coins haut droit et bas gauche coupés de <paramref name="cut"/>.</summary>
        internal static Geometry Outline(Rect box, double cut)
        {
            cut = Math.Max(0, Math.Min(cut, Math.Min(box.Width, box.Height) / 2));
            var points = new[]
            {
                new Point(box.Left, box.Top), new Point(box.Right - cut, box.Top), new Point(box.Right, box.Top + cut),
                new Point(box.Right, box.Bottom), new Point(box.Left + cut, box.Bottom), new Point(box.Left, box.Bottom - cut)
            };
            return new PolylineGeometry(points, true);
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width < 2 * Ring + 4 || bounds.Height < 2 * Ring + 4) return;
            var metal = Chrome.Accent.Color;
            // Le second filet : à 55 %, écarté de Ring, sa découpe parallèle à celle du cadre.
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x8C, metal.R, metal.G, metal.B)), 1), Outline(bounds.Deflate(0.5), Cut + Ring * 0.586));
            context.DrawGeometry(Background, new Pen(BorderBrush ?? Chrome.BorderStrong, 1), Outline(bounds.Deflate(Ring + 0.5), Cut));
            // Le losange du pied (25/11, Rémi) : au milieu du filet du bas, vide — le filet le contourne.
            FootDiamond.Draw(context, new Point(bounds.Width / 2, bounds.Bottom - Ring - 0.5), Background, BorderBrush ?? Chrome.BorderStrong);
        }
    }

    /// <summary>LE LOSANGE DU PIED (série « Museum », 25/11/2026, Rémi) : au milieu de la bordure
    /// inférieure de chaque menu contextuel et de chaque liste déroulante, un petit losange VIDE
    /// qui assure la continuité de la bordure — le filet s'ouvre pour le contourner, comme sur le
    /// bord gauche des Préférences sous « Ascension ». Le menu contextuel le peint lui-même
    /// (CutFrame) ; les listes déroulantes (ComboBox) et les menus déroulés (MenuItem, menu des
    /// zones de texte) gardent le cadre du thème : le losange y est un petit contrôle glissé dans
    /// le cadre à la naissance du gabarit, qui peint à cheval sur son bord bas. Le cadre reçoit du
    /// skin une marge basse (Room) : sans elle, la moitié qui dépasse sortirait de la fenêtre du menu.
    /// Il ne peint rien hors de la série « Museum ».</summary>
    internal sealed class FootDiamond : Control
    {
        public const double Half = 4.2, Room = 6;
        private static bool _installed;
        private readonly Border _frame;

        /// <summary>Peint le losange centré sur <paramref name="center"/> : le fond du cadre dedans (il
        /// efface le filet qui passait là), le filet autour.</summary>
        public static void Draw(DrawingContext context, Point center, IBrush fill, IBrush line)
        {
            var diamond = new[] { new Point(center.X - Half, center.Y), new Point(center.X, center.Y - Half), new Point(center.X + Half, center.Y), new Point(center.X, center.Y + Half) };
            var ground = fill as ISolidColorBrush;
            // Un fond de verre (translucide) laisserait voir le filet : le losange est peint plein.
            if (ground != null && ground.Color.A < 255) fill = new SolidColorBrush(Color.FromRgb(ground.Color.R, ground.Color.G, ground.Color.B));
            context.DrawGeometry(fill ?? Chrome.RaisedBg, new Pen(line ?? Chrome.BorderStrong, 1), new PolylineGeometry(diamond, true));
        }

        /// <summary>Au démarrage : chaque liste déroulante, chaque menu qui reçoit son gabarit reçoit son losange.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<ComboBox>(delegate(ComboBox box, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) { Attach(e.NameScope.Find<Avalonia.Controls.Primitives.Popup>("PART_Popup")); });
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuItem>(delegate(MenuItem item, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) { Attach(e.NameScope.Find<Avalonia.Controls.Primitives.Popup>("PART_Popup")); });
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuFlyoutPresenter>(delegate(MenuFlyoutPresenter presenter, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
            {
                foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(presenter)) { Attach(child as Border); break; }
            });
        }

        private static void Attach(Avalonia.Controls.Primitives.Popup popup) { if (popup != null) Attach(popup.Child as Border); }

        /// <summary>Glisse le losange dans le cadre : son contenu passe dans un panneau, le losange à côté.</summary>
        private static void Attach(Border frame)
        {
            if (frame == null || frame.Child == null) return;
            var held = frame.Child as Panel;
            if (held != null && "foot-host".Equals(held.Tag)) return;
            var content = frame.Child;
            frame.Child = null;
            var host = new Panel { Tag = "foot-host" };
            host.Children.Add(content);
            host.Children.Add(new FootDiamond(frame));
            frame.Child = host;
        }

        private FootDiamond(Border frame)
        {
            _frame = frame;
            IsHitTestVisible = false;
            Tag = "foot-diamond";
            frame.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.BoundsProperty || e.Property == Border.BackgroundProperty || e.Property == Border.BorderBrushProperty) InvalidateVisual();
            };
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        /// <summary>Sonde : le centre du losange, dans le repère du cadre.</summary>
        internal Point CenterInFrameForProbe { get { return new Point(_frame.Bounds.Width / 2, _frame.Bounds.Height - 0.5); } }

        public override void Render(DrawingContext context)
        {
            if (!Skin.Museum || _frame.Bounds.Width < 24) return;
            var origin = _frame.TranslatePoint(new Point(0, 0), this);
            if (!origin.HasValue) return;
            Draw(context, new Point(origin.Value.X + _frame.Bounds.Width / 2, origin.Value.Y + _frame.Bounds.Height - 0.5), _frame.Background, _frame.BorderBrush);
        }
    }

    /// <summary>LA SILHOUETTE GALBÉE (série « Museum », 24/11/2026 au soir, d'après la Pause du jeu) —
    /// celle des grands dialogues (Préférences, Sprint) : le bord droit part en arc, se renfonce,
    /// puis fait une bosse aux sept dixièmes de la hauteur ; un filet de métal, un second filet
    /// plus pâle en dedans, deux losanges sur le bord gauche. Aucune gravure. Elle ne fait que
    /// PEINDRE : le contenu du dialogue est posé par-dessus, en retrait à droite (Inset).</summary>
    internal sealed class GalbeFrame : Control
    {
        /// <summary>Le renfoncement du bord droit, et la marge du bord gauche (la place des losanges).</summary>
        public const double Dent = 36, Left = 5;
        /// <summary>Le retrait du contenu : à gauche les losanges, à droite le renfoncement et de l'air.</summary>
        public static readonly Thickness Inset = new Thickness(Left + 7, 4, Dent + 12, 6);

        public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<GalbeFrame, IBrush>("Fill");
        public static readonly StyledProperty<double> GlassProperty = AvaloniaProperty.Register<GalbeFrame, double>("Glass", 1.0);

        static GalbeFrame() { AffectsRender<GalbeFrame>(FillProperty, GlassProperty); }

        public GalbeFrame() { IsHitTestVisible = false; }

        public IBrush Fill { get { return GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public double Glass { get { return GetValue(GlassProperty); } set { SetValue(GlassProperty, value); } }

        /// <summary>Le tracé pour une boîte de cette taille, rentré de <paramref name="inner"/> pixels.</summary>
        internal static Geometry Outline(Size size, double inner)
        {
            double w = size.Width, h = size.Height;
            double d = Dent, r1 = Math.Min(h * 0.2, 70), yb = h * 0.7, b = Math.Min(h * 0.16, 64), r = 10;
            double x = Left + inner, y = inner + 0.6, right = w - inner - 0.6, bottom = h - inner - 0.6;
            // Une boîte trop basse pour la bosse : l'arc et le renfoncement se resserrent.
            if (y + r1 + d * 1.5 > yb - b) b = Math.Max(4, yb - (y + r1 + d * 1.5));
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(x + r, y), true);
                path.LineTo(new Point(right - r1, y));
                path.ArcTo(new Point(right, y + r1), new Size(r1, r1), 0, false, SweepDirection.Clockwise);
                path.CubicBezierTo(new Point(right, y + r1 + d * 0.8), new Point(right - d, y + r1 + d * 0.4), new Point(right - d, y + r1 + d * 1.5));
                path.LineTo(new Point(right - d, yb - b));
                path.ArcTo(new Point(right - d, yb + b), new Size(d, b), 0, false, SweepDirection.Clockwise);
                path.LineTo(new Point(right - d, bottom - r));
                path.ArcTo(new Point(right - d - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise);
                path.LineTo(new Point(x + r, bottom));
                path.ArcTo(new Point(x, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                path.LineTo(new Point(x, y + r));
                path.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise);
                path.EndFigure(true);
            }
            return geometry;
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width < 160 || size.Height < 120) return;
            var metal = Chrome.Accent.Color;
            var outline = Outline(size, 0);
            if (Fill != null)
                using (context.PushOpacity(Math.Max(0, Math.Min(1, Glass))))
                    context.DrawGeometry(Fill, null, outline);
            context.DrawGeometry(null, new Pen(Chrome.Accent, 1.2), outline);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x4D, metal.R, metal.G, metal.B)), 1), Outline(size, 5));
            // Les deux losanges du bord gauche.
            foreach (var y in new[] { Math.Min(64, size.Height * 0.3), size.Height - 40 })
            {
                const double R = 4.2;
                var diamond = new[] { new Point(Left - R, y), new Point(Left, y - R), new Point(Left + R, y), new Point(Left, y + R) };
                context.DrawGeometry(Chrome.RaisedBg, new Pen(Chrome.Accent, 1), new PolylineGeometry(diamond, true));
            }
        }
    }

    /// <summary>L'ANNEAU DE L'OBJECTIF DU JOUR (l'Accueil, 24/11/2026 au soir) : une piste, la part
    /// écrite par-dessus, un disque au centre avec le pourcentage. Sous tous les skins — chacun
    /// lui donne ses couleurs (Chrome).</summary>
    internal sealed class GoalRing : Control
    {
        private double _ratio;
        private string _text = "", _caption = "";

        public GoalRing() { Width = 110; Height = 110; }

        public double Ratio { get { return _ratio; } }
        public string Text { get { return _text; } }

        public void Show(double ratio, string text, string caption)
        {
            _ratio = double.IsNaN(ratio) ? 0 : Math.Max(0, Math.Min(1, ratio));
            _text = text ?? "";
            _caption = caption ?? "";
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 9;
            if (radius < 12) return;
            context.DrawEllipse(null, new Pen(Chrome.Border, 6), center, radius, radius);
            // Le dégradé fixe des barres (25/11) : enroulé une fois autour de l'anneau, sous « Museum ».
            var lit = Skin.Museum ? Skin.MeterRing(Chrome.Glow.Color) : _ratio >= 1 ? (IBrush)Chrome.Ok : Chrome.Accent;
            if (_ratio >= 0.999) context.DrawEllipse(null, new Pen(lit, 6), center, radius, radius);
            else if (_ratio > 0.004)
            {
                var angle = _ratio * 2 * Math.PI;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(center.X, center.Y - radius), false);
                    path.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, _ratio > 0.5, SweepDirection.Clockwise);
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(lit, 6) { LineCap = PenLineCap.Round }, geometry);
            }
            context.DrawEllipse(Chrome.CardBg, new Pen(Chrome.BorderStrong, 1), center, radius - 12, radius - 12);
            var title = Skin.TitleFont;
            var big = new FormattedText(_text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(title ?? FontFamily.Default, FontStyle.Normal, title == null ? FontWeight.SemiBold : FontWeight.Medium), title == null ? 17 : 19, Chrome.Ink);
            context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - big.Height / 2 - (_caption.Length > 0 ? 6 : 0)));
            if (_caption.Length == 0) return;
            var small = new FormattedText(_caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Skin.UiFont ?? FontFamily.Default), 9, Chrome.FaintText);
            context.DrawText(small, new Point(center.X - small.Width / 2, center.Y + 9));
        }
    }

    /// <summary>LA BARRE À JALONS d'un livre (l'Accueil, 24/11/2026 au soir, d'après le menu « Musée »
    /// du jeu) : une piste fine, la part des chapitres TERMINÉS dessus, un trait à chaque jalon
    /// (Skins.BookMilestones) et son libellé en petites capitales dessous. Sous tous les skins.</summary>
    internal sealed class MilestoneBar : Control
    {
        private double _ratio;

        public MilestoneBar() { Height = 30; MinWidth = 180; }

        public double Ratio { get { return _ratio; } }

        public void Show(double ratio)
        {
            _ratio = double.IsNaN(ratio) ? 0 : Math.Max(0, Math.Min(1, ratio));
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            if (width < 40) return;
            const double Top = 5, Thick = 5;
            var ink = Chrome.Ink.Color;
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x26, ink.R, ink.G, ink.B)), null, new Rect(0, Top, width, Thick), 2.5, 2.5);
            if (_ratio > 0)
            {
                // Sous « Museum », le dégradé FIXE des barres (25/11), tendu sur la piste entière.
                IBrush fill;
                if (Skin.Museum) fill = Skin.MeterBrush(Chrome.Glow.Color, width);
                else
                {
                    var plain = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
                    plain.GradientStops.Add(new GradientStop(Chrome.Accent.Color, 0));
                    plain.GradientStops.Add(new GradientStop(Chrome.AccentStrong.Color, 1));
                    fill = plain;
                }
                context.DrawRectangle(fill, null, new Rect(0, Top, Math.Max(Thick, width * _ratio), Thick), 2.5, 2.5);
            }
            var face = new Typeface(Skin.UiFont ?? FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            for (var i = 0; i < Skins.BookMilestones.Length; i++)
            {
                var share = Skins.BookMilestones[i];
                var x = Math.Round(width * share) + 0.5;
                context.DrawLine(new Pen(Chrome.SoftText, 1), new Point(x, Top - 3), new Point(x, Top + Thick + 3));
                var passed = _ratio + 1e-9 >= share;
                // À l'étroit, le libellé seul : les trois tiennent côte à côte.
                var label = new FormattedText((width >= 430 ? (share * 100).ToString("0", culture) + " % · " : "") + Skins.BookMilestoneNames[i].ToUpper(culture), culture, FlowDirection.LeftToRight, face, 9,
                    passed ? (IBrush)Chrome.SoftText : Chrome.FaintText);
                // Centré sous son trait ; le premier et le dernier restent dans la barre.
                var left = Math.Max(0, Math.Min(width - label.Width, x - label.Width / 2));
                context.DrawText(label, new Point(left, Top + Thick + 6));
            }
        }
    }

    /// <summary>LA PERLE D'UNE PILULE (série « Museum », retouches du 24/11/2026 au soir — Rémi : « trop
    /// petite et trop sur la droite, on veut aligner son axe central et la bordure ») : un losange
    /// clair et son halo, posés À CHEVAL sur le bord gauche de la ligne choisie ou survolée de la
    /// Pile et des menus. Un pinceau de fond ne peut pas déborder de l'arrondi : la perle est donc
    /// un petit contrôle glissé dans le gabarit de la ligne (à sa naissance), qui peint hors de
    /// lui-même, sur le bord de la bande. Elle ne peint rien hors des skins à pilule.</summary>
    internal sealed class PillPearl : Control
    {
        /// <summary>La place qu'il lui faut à gauche de la bande (les styles la réservent).</summary>
        public const double Room = 5;
        private const double Half = 4.2;

        private static bool _installed;
        private readonly TemplatedControl _owner;
        private readonly Control _band;

        /// <summary>Au démarrage : chaque ligne d'arbre, chaque entrée de menu qui reçoit son gabarit reçoit sa perle.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<TreeViewItem>(delegate(TreeViewItem item, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) { Attach(item, e.NameScope.Find<Border>("SelectionBorder")); });
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuItem>(delegate(MenuItem item, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) { Attach(item, e.NameScope.Find<Border>("root")); });
        }

        private static void Attach(TemplatedControl owner, Border band)
        {
            // Seulement dans une grille (les gabarits du thème) : ailleurs, un enfant de plus dérangerait la mise en place.
            var host = band == null ? null : band.Child as Grid;
            if (host == null) return;
            foreach (var child in host.Children) if (child is PillPearl) return;
            var pearl = new PillPearl(owner, band);
            Grid.SetColumnSpan(pearl, Math.Max(1, host.ColumnDefinitions.Count));
            Grid.SetRowSpan(pearl, Math.Max(1, host.RowDefinitions.Count));
            host.Children.Add(pearl);
        }

        /// <summary>La perle d'une ligne de LISTE (25/11, Rémi : « corrige la position des puces dans la
        /// recherche Ctrl+, et dans le gestionnaire de styles ») : peinte jusque-là par le fond de la
        /// ligne, elle tombait DANS la pilule, sur l'icône ou le début du texte. Elle se pose désormais
        /// à cheval sur le bord gauche, comme dans la Pile — le gabarit de la ligne la porte
        /// (Skin.PillListFace).</summary>
        internal static PillPearl ForList(ListBoxItem owner, Control band) { return new PillPearl(owner, band); }

        private PillPearl(TemplatedControl owner, Control band)
        {
            _owner = owner;
            _band = band;
            IsHitTestVisible = false;
            Tag = "pill-pearl";
            EventHandler<AvaloniaPropertyChangedEventArgs> changed = delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == InputElement.IsPointerOverProperty || e.Property == Border.BackgroundProperty || e.Property == Visual.BoundsProperty
                    || e.Property == TreeViewItem.IsSelectedProperty || e.Property == MenuItem.IsSelectedProperty || e.Property == ListBoxItem.IsSelectedProperty) InvalidateVisual();
            };
            band.PropertyChanged += changed;
            owner.PropertyChanged += changed;
        }

        private bool Selected
        {
            get
            {
                var line = _owner as TreeViewItem;
                if (line != null) return line.IsSelected;
                var row = _owner as ListBoxItem;
                if (row != null) return row.IsSelected;
                var entry = _owner as MenuItem;
                return entry != null && entry.IsSelected;
            }
        }

        /// <summary>0 : pas de perle ; 1 : celle de la ligne choisie ; entre les deux : celle du survol.</summary>
        internal double Strength
        {
            get
            {
                if (!Skins.MuseumPill(Skin.Current)) return 0;
                if (Selected) return 1;
                if (_owner is ListBoxItem) return _owner.IsPointerOver ? 0.55 : 0;
                return _band.IsPointerOver && _owner is TreeViewItem ? 0.55 : 0;
            }
        }

        /// <summary>Sonde : où la perle se pose, dans le repère de la bande (son centre doit être SUR le bord gauche).</summary>
        internal Point CenterInBandForProbe { get { return new Point(0.5, _band.Bounds.Height / 2); } }
        internal const double SizeForProbe = Half * 2;

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        public override void Render(DrawingContext context)
        {
            var strength = Strength;
            if (strength <= 0) return;
            // Le bord gauche de la bande, dans notre repère : la perle y pose son centre.
            var origin = _band.TranslatePoint(new Point(0, 0), this);
            if (!origin.HasValue) return;
            var center = new Point(origin.Value.X + 0.5, origin.Value.Y + _band.Bounds.Height / 2);
            var glow = Chrome.Glow.Color;
            using (context.PushOpacity(strength))
            {
                var halo = new RadialGradientBrush();
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, glow.R, glow.G, glow.B), 0));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, glow.R, glow.G, glow.B), 0.55));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
                context.DrawEllipse(halo, null, center, Half * 2.4, Half * 2.4);
                var diamond = new[] { new Point(center.X - Half, center.Y), new Point(center.X, center.Y - Half), new Point(center.X + Half, center.Y), new Point(center.X, center.Y + Half) };
                context.DrawGeometry(Chrome.Glow, null, new PolylineGeometry(diamond, true));
            }
        }
    }
}
