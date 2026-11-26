using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE DÉGRADÉ DES BARRES DE PROGRESSION (skins « Museum », 25/11/2026, Rémi) : la part
    /// remplie d'une barre va du désaturé sombre au vif saturé. Le dégradé est FIXE : il est
    /// tendu sur toute la PISTE, et la part remplie n'en découvre que ce qu'elle couvre — il ne
    /// se serre ni ne s'étire quand la barre avance ; une barre pleine le montre en entier.
    /// Hors de la série « Museum », la couleur reste pleine, comme avant.
    /// Concerne : l'objectif du jour (Journal, anneau de l'Accueil), l'objectif d'un écrit, la
    /// barre d'un livre (présents, terminés), les jalons d'un livre, l'objectif d'un sprint.</summary>
    internal static partial class Skin
    {
        private sealed class MeterSpec
        {
            public Control Track;
            public Func<Color> Color;
        }

        private static readonly AttachedProperty<MeterSpec> MeterProperty =
            AvaloniaProperty.RegisterAttached<Border, Border, MeterSpec>("Meter");

        /// <summary>Le départ du dégradé : la couleur, désaturée et assombrie.</summary>
        internal static Color MeterFrom(Color color)
        {
            double h, s, l;
            Skins.ToHsl(Chrome.Hex(color), out h, out s, out l);
            return Hsl(h, s * 0.30, Math.Max(0.14, l * 0.46));
        }

        /// <summary>…et son arrivée : la même teinte, vive et saturée.</summary>
        internal static Color MeterTo(Color color)
        {
            double h, s, l;
            Skins.ToHsl(Chrome.Hex(color), out h, out s, out l);
            return Hsl(h, Math.Min(1, s * 1.25 + 0.12), Math.Max(0.50, Math.Min(0.66, l + 0.06)));
        }

        /// <summary>Le pinceau d'une part remplie qui commence au bord gauche d'une piste large de
        /// <paramref name="trackWidth"/> : en unités absolues, donc indifférent à la largeur remplie.</summary>
        public static IBrush MeterBrush(Color color, double trackWidth)
        {
            if (!Museum) return new SolidColorBrush(color);
            // « Ascension » (25/11) : la MAGIE BLEUE des progressions — fixe, elle ne suit ni le métal ni l'état.
            if (AscensionArt.On) return AscensionArt.MagicFill(new RelativePoint(0, 0, RelativeUnit.Absolute), new RelativePoint(Math.Max(1, trackWidth), 0, RelativeUnit.Absolute));
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(Math.Max(1, trackWidth), 0, RelativeUnit.Absolute)
            };
            brush.GradientStops.Add(new GradientStop(MeterFrom(color), 0));
            brush.GradientStops.Add(new GradientStop(color, 0.62));
            brush.GradientStops.Add(new GradientStop(MeterTo(color), 1));
            return brush;
        }

        /// <summary>Le pinceau d'une barre DEBOUT (le graphique du Journal) haute de <paramref name="height"/>,
        /// dans un tracé haut de <paramref name="plotHeight"/> : sombre au pied, vif au sommet du tracé —
        /// une petite barre reste donc sombre, une grande monte vers le vif.</summary>
        public static IBrush MeterBrushUp(Color color, double height, double plotHeight)
        {
            if (!Museum) return new SolidColorBrush(color);
            if (AscensionArt.On) return AscensionArt.MagicFill(new RelativePoint(0, height, RelativeUnit.Absolute), new RelativePoint(0, height - Math.Max(1, plotHeight), RelativeUnit.Absolute));
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, height, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, height - Math.Max(1, plotHeight), RelativeUnit.Absolute)
            };
            brush.GradientStops.Add(new GradientStop(MeterFrom(color), 0));
            brush.GradientStops.Add(new GradientStop(color, 0.62));
            brush.GradientStops.Add(new GradientStop(MeterTo(color), 1));
            return brush;
        }

        /// <summary>Le pinceau d'un ANNEAU : le même dégradé, enroulé une fois autour du centre,
        /// depuis le haut — fixe lui aussi.</summary>
        public static IBrush MeterRing(Color color)
        {
            if (!Museum) return new SolidColorBrush(color);
            var brush = new ConicGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Angle = 0 };
            if (AscensionArt.On)
            {
                brush.GradientStops.Add(new GradientStop(AscensionArt.MagicDeep, 0));
                brush.GradientStops.Add(new GradientStop(AscensionArt.Magic, 1));
                return brush;
            }
            brush.GradientStops.Add(new GradientStop(MeterFrom(color), 0));
            brush.GradientStops.Add(new GradientStop(color, 0.62));
            brush.GradientStops.Add(new GradientStop(MeterTo(color), 1));
            return brush;
        }

        /// <summary>Fait de <paramref name="fill"/> la part remplie d'une barre : son fond est le dégradé
        /// tendu sur <paramref name="track"/> (la piste, dont elle part du bord gauche), repris quand
        /// la piste change de largeur et quand l'apparence change. La couleur est relue à chaque fois.</summary>
        public static void Meter(Border fill, Control track, Func<Color> color)
        {
            if (fill == null || track == null || color == null) return;
            fill.SetValue(MeterProperty, new MeterSpec { Track = track, Color = color });
            Ui.OnSizeChanged(track, delegate { RefreshMeter(fill); });
            RefreshMeter(fill);
        }

        /// <summary>Repose le dégradé d'une part remplie (la couleur a changé : « atteint »).</summary>
        public static void RefreshMeter(Border fill)
        {
            var spec = fill == null ? null : fill.GetValue(MeterProperty);
            if (spec == null) return;
            fill.Background = MeterBrush(spec.Color(), spec.Track.Bounds.Width);
        }

        // — Sonde.
        internal static bool MeterIsGradientForProbe(Border fill) { return fill != null && fill.Background is LinearGradientBrush; }
        internal static double MeterSpanForProbe(Border fill)
        {
            var brush = fill == null ? null : fill.Background as LinearGradientBrush;
            return brush == null ? 0 : brush.EndPoint.Point.X;
        }
    }
}
