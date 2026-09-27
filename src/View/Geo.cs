using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.View
{
    /// <summary>Les conversions aux bords de la vue (portage Avalonia, P0) :
    /// le cœur parle Pos, Box et Ink ; WPF parle Point, Rect et Brush. Ici,
    /// et nulle part ailleurs, l'un devient l'autre.</summary>
    public static class Geo
    {
        public static Rect ToRect(this Box box)
        {
            return new Rect(box.X, box.Y, box.Width, box.Height);
        }

        public static Box ToBox(this Rect rect)
        {
            return new Box(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static Point ToPoint(this Pos pos)
        {
            return new Point(pos.X, pos.Y);
        }

        public static Pos ToPos(this Point point)
        {
            return new Pos(point.X, point.Y);
        }

        public static Color ToColor(this Ink ink)
        {
            return Color.FromArgb(ink.A, ink.R, ink.G, ink.B);
        }

        public static Ink ToInk(this Color color)
        {
            return Ink.Argb(color.A, color.R, color.G, color.B);
        }

        // Un pinceau figé par couleur : les pièces composées en réclament des
        // milliers par page, toujours les mêmes.
        private static readonly Dictionary<Ink, SolidColorBrush> _brushes = new Dictionary<Ink, SolidColorBrush>();

        public static SolidColorBrush ToBrush(this Ink ink)
        {
            SolidColorBrush brush;
            lock (_brushes)
            {
                if (_brushes.TryGetValue(ink, out brush)) return brush;
                brush = new SolidColorBrush(ink.ToColor());
                brush.Freeze();
                _brushes[ink] = brush;
            }
            return brush;
        }

        /// <summary>L'encre d'une pièce à l'écran : les rôles du thème en
        /// direct (les pinceaux de Chrome changent de couleur avec le thème),
        /// sinon la valeur figée dans la pièce.</summary>
        public static Brush InkBrush(ComposedPiece piece)
        {
            switch (piece.InkRole)
            {
                case InkRole.Faint: return Chrome.FaintText;
                case InkRole.Accent: return Chrome.AccentStrong;
                default: return piece.Ink.ToBrush();
            }
        }

        /// <summary>Le surlignage d'une pièce à l'écran, ou null.</summary>
        public static Brush HighlightBrush(ComposedPiece piece)
        {
            switch (piece.HighlightRole)
            {
                case HighlightRole.Annotation: return Chrome.AnnotationTint;
                case HighlightRole.Accent: return Chrome.AccentTint;
                case HighlightRole.Explicit: return piece.Highlight.HasValue ? piece.Highlight.Value.ToBrush() : null;
                default: return null;
            }
        }

        /// <summary>Les modificateurs WPF vers ceux du cœur (raccourcis).</summary>
        public static Settings.KeyModifiers ToCore(ModifierKeys modifiers)
        {
            var result = Settings.KeyModifiers.None;
            if ((modifiers & ModifierKeys.Control) != 0) result |= Settings.KeyModifiers.Control;
            if ((modifiers & ModifierKeys.Shift) != 0) result |= Settings.KeyModifiers.Shift;
            if ((modifiers & ModifierKeys.Alt) != 0) result |= Settings.KeyModifiers.Alt;
            return result;
        }

        public static ModifierKeys ToWpf(Settings.KeyModifiers modifiers)
        {
            var result = ModifierKeys.None;
            if ((modifiers & Settings.KeyModifiers.Control) != 0) result |= ModifierKeys.Control;
            if ((modifiers & Settings.KeyModifiers.Shift) != 0) result |= ModifierKeys.Shift;
            if ((modifiers & Settings.KeyModifiers.Alt) != 0) result |= ModifierKeys.Alt;
            return result;
        }

        /// <summary>Le nom d'une touche (celui des réglages) vers la touche WPF ;
        /// false si le nom n'en est pas une.</summary>
        public static bool TryKey(string name, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(name)) return false;
            try { key = (Key)System.Enum.Parse(typeof(Key), name.Trim(), true); }
            catch { return false; }
            return key != Key.None;
        }
    }
}
