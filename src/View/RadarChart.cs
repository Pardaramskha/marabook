using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>LE RADAR (b47 bis) : la toile d'araignée d'un modèle — un axe
    /// par RadarAxis du modèle, une valeur de 0 à RadarMax par fiche. Dessiné
    /// dans un Canvas carré : anneaux (un par niveau jusqu'à six, sinon cinq),
    /// rayons, étiquettes autour, le polygone des valeurs en accent
    /// translucide, un point par sommet. Sert à l'onglet Radar de la fiche,
    /// au mode wiki et à l'épinglé (en petit, sans étiquettes si demandé).</summary>
    public static class RadarChart
    {
        public static void Draw(Canvas canvas, SheetTemplate template, IDictionary<string, int> values, double size, bool labels)
        {
            canvas.Children.Clear();
            canvas.Width = size;
            canvas.Height = size;
            if (template == null || template.RadarAxes.Count < 3) return;
            var axes = template.RadarAxes;
            var max = Math.Max(1, template.RadarMax);
            var margin = labels ? Math.Max(34, size * 0.16) : 6;
            var radius = size / 2 - margin;
            var center = new Point(size / 2, size / 2);
            var n = axes.Count;

            // Les anneaux : un par niveau quand l'échelle est courte, cinq sinon.
            var rings = max <= 6 ? max : 5;
            for (var r = 1; r <= rings; r++)
            {
                var ring = new Polygon { Stroke = Chrome.Border, StrokeThickness = 1, Fill = Brushes.Transparent };
                var fraction = (double)r / rings;
                for (var i = 0; i < n; i++) ring.Points.Add(At(center, radius * fraction, i, n));
                canvas.Children.Add(ring);
            }
            // Les rayons et les étiquettes.
            for (var i = 0; i < n; i++)
            {
                var tip = At(center, radius, i, n);
                canvas.Children.Add(new Line { X1 = center.X, Y1 = center.Y, X2 = tip.X, Y2 = tip.Y, Stroke = Chrome.Border, StrokeThickness = 1 });
                if (!labels) continue;
                var label = new TextBlock
                {
                    Text = axes[i].Name,
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    TextAlignment = TextAlignment.Center,
                    Width = margin * 2 + 20,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var anchor = At(center, radius + margin * 0.55, i, n);
                Canvas.SetLeft(label, anchor.X - label.Width / 2);
                Canvas.SetTop(label, anchor.Y - 8);
                canvas.Children.Add(label);
            }
            // Le polygone des valeurs.
            var shape = new Polygon
            {
                Fill = new SolidColorBrush(Chrome.Accent.Color) { Opacity = 0.28 },
                Stroke = Chrome.Accent,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            var any = false;
            for (var i = 0; i < n; i++)
            {
                var value = ValueOf(values, axes[i].Id, max);
                if (value > 0) any = true;
                shape.Points.Add(At(center, radius * value / max, i, n));
            }
            if (any) canvas.Children.Add(shape);
            for (var i = 0; i < n; i++)
            {
                var value = ValueOf(values, axes[i].Id, max);
                if (value <= 0) continue;
                var at = At(center, radius * value / max, i, n);
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Chrome.Accent,
                    Stroke = Chrome.PaperBg,
                    StrokeThickness = 1.5,
                    ToolTip = axes[i].Name + " : " + value.ToString(CultureInfo.InvariantCulture) + " / " + max
                };
                Canvas.SetLeft(dot, at.X - 4);
                Canvas.SetTop(dot, at.Y - 4);
                canvas.Children.Add(dot);
            }
        }

        public static int ValueOf(IDictionary<string, int> values, string axisId, int max)
        {
            int value;
            if (values == null || !values.TryGetValue(axisId, out value)) return 0;
            return value < 0 ? 0 : value > max ? max : value;
        }

        /// <summary>Le point du i-ème axe (sur n), le premier en haut, sens horaire.</summary>
        private static Point At(Point center, double distance, int i, int n)
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * i / n;
            return new Point(center.X + distance * Math.Cos(angle), center.Y + distance * Math.Sin(angle));
        }

        /// <summary>Vrai si une fiche a au moins une valeur non nulle sur le radar.</summary>
        public static bool HasValues(SheetTemplate template, IDictionary<string, int> values)
        {
            if (template == null || values == null) return false;
            foreach (var axis in template.RadarAxes)
                if (ValueOf(values, axis.Id, Math.Max(1, template.RadarMax)) > 0) return true;
            return false;
        }
    }
}
