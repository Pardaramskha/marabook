using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Le jeu d'icônes embarqué, dessiné par Avalonia depuis les
    /// chemins du cœur (IconPaths) : teintable, sans dépendance.</summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Geometry> _cache = new Dictionary<string, Geometry>();

        public static bool Has(string name)
        {
            return name != null && IconPaths.Data.ContainsKey(name);
        }

        public static IEnumerable<string> Names { get { return IconPaths.Data.Keys; } }

        public static Geometry Get(string name)
        {
            Geometry cached;
            if (_cache.TryGetValue(name, out cached)) return cached;
            string data;
            if (!IconPaths.Data.TryGetValue(name, out data)) return null;
            var geometry = Geometry.Parse(data);
            _cache[name] = geometry;
            return geometry;
        }

        /// <summary>Une icône teintable, rendue depuis son chemin.</summary>
        public static Control Make(string name, double size, IBrush fill)
        {
            var geometry = Get(name);
            if (geometry == null)
                return new TextBlock { Text = "?", FontSize = size };
            return new Path
            {
                Data = geometry,
                Fill = fill,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size
            };
        }

        /// <summary>Le contenu d'un bouton « icône + libellé » ; sans libellé,
        /// l'icône seule.</summary>
        public static Control Label(string name, string label, double size, IBrush fill)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Make(name, size, fill);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, string.IsNullOrEmpty(label) ? 0 : 6, 0);
            row.Children.Add(glyph);
            if (!string.IsNullOrEmpty(label))
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }
    }
}
