using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>The embedded icon set (Phosphor, bold weight): SVG path data
    /// compiled into the exe by tools/make-icons-cs.ps1 from assets/icons —
    /// zero runtime dependency, tintable at will (256×256 viewbox).</summary>
    public static class Icons
    {
        private static readonly Dictionary<string, string> _paths = IconPaths.Data; // les chemins vivent dans le cœur (P1)

        private static readonly Dictionary<string, Geometry> _cache = new Dictionary<string, Geometry>();

        public static bool Has(string name)
        {
            return name != null && _paths.ContainsKey(name);
        }

        public static IEnumerable<string> Names { get { return _paths.Keys; } }

        public static Geometry Get(string name)
        {
            Geometry cached;
            if (_cache.TryGetValue(name, out cached)) return cached;
            string data;
            if (!_paths.TryGetValue(name, out data)) return null;
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            _cache[name] = geometry;
            return geometry;
        }

        /// <summary>Le contenu d'un bouton « icône + libellé » (batch 36) : la
        /// flèche ou le plus livrés, puis le texte — remplace les « ← », « ↗ »
        /// et « + » typographiques des boutons. Sans libellé : l'icône seule.</summary>
        public static UIElement Label(string name, string label, double size, Brush fill)
        {
            var row = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var glyph = Make(name, size, fill) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, string.IsNullOrEmpty(label) ? 0 : 6, 0);
                row.Children.Add(glyph);
            }
            if (!string.IsNullOrEmpty(label))
                row.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center
                });
            return row;
        }

        /// <summary>A tintable icon element, rendered from the 256-viewbox path.</summary>
        public static UIElement Make(string name, double size, Brush fill)
        {
            var geometry = Get(name);
            if (geometry == null)
                return new System.Windows.Controls.TextBlock { Text = "?", FontSize = size };
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = fill,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                SnapsToDevicePixels = true
            };
            return path;
        }
    }
}
