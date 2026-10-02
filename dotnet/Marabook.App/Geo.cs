using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>Les conversions aux bords de la vue (porté de View/Geo.cs) :
    /// le cœur parle Pos, Box et Ink ; Avalonia parle Point, Rect et Brush.</summary>
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

        private static readonly Dictionary<Ink, IBrush> _brushes = new Dictionary<Ink, IBrush>();

        /// <summary>Un pinceau immuable par couleur : les pièces composées en
        /// réclament des milliers par page, toujours les mêmes.</summary>
        public static IBrush ToBrush(this Ink ink)
        {
            IBrush brush;
            lock (_brushes)
            {
                if (_brushes.TryGetValue(ink, out brush)) return brush;
                brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ink.ToColor());
                _brushes[ink] = brush;
            }
            return brush;
        }

        /// <summary>L'encre d'une pièce à l'écran : les rôles du thème en
        /// direct, sinon la valeur figée dans la pièce.</summary>
        public static IBrush InkBrush(ComposedPiece piece)
        {
            switch (piece.InkRole)
            {
                case InkRole.Default: return Chrome.PaperInk; // repère : encre du papier du thème, le PDF garde le noir
                case InkRole.Faint: return Chrome.FaintText;
                case InkRole.Accent: return Chrome.AccentStrong;
                default: return piece.Ink.ToBrush();
            }
        }

        /// <summary>Le surlignage d'une pièce à l'écran, ou null.</summary>
        public static IBrush HighlightBrush(ComposedPiece piece)
        {
            switch (piece.HighlightRole)
            {
                case HighlightRole.Annotation: return Chrome.AnnotationTint;
                case HighlightRole.Accent: return Chrome.AccentTint;
                case HighlightRole.Explicit: return piece.Highlight.HasValue ? piece.Highlight.Value.ToBrush() : null;
                default: return null;
            }
        }

        /// <summary>Les modificateurs Avalonia vers ceux du cœur (raccourcis).
        /// Le « Control » du cœur est la touche de commande du système : Ctrl
        /// sur Windows et Linux, ⌘ sur macOS (Ui.Command, 02/10).</summary>
        public static Settings.KeyModifiers ToCore(KeyModifiers modifiers)
        {
            var result = Settings.KeyModifiers.None;
            if (Ui.HasCommand(modifiers)) result |= Settings.KeyModifiers.Control;
            if ((modifiers & KeyModifiers.Shift) != 0) result |= Settings.KeyModifiers.Shift;
            if ((modifiers & KeyModifiers.Alt) != 0) result |= Settings.KeyModifiers.Alt;
            return result;
        }

        /// <summary>Les modificateurs du cœur vers ceux d'Avalonia (raccourcis des menus).</summary>
        public static KeyModifiers ToAvalonia(Settings.KeyModifiers modifiers)
        {
            var result = KeyModifiers.None;
            if ((modifiers & Settings.KeyModifiers.Control) != 0) result |= Ui.Command;
            if ((modifiers & Settings.KeyModifiers.Shift) != 0) result |= KeyModifiers.Shift;
            if ((modifiers & Settings.KeyModifiers.Alt) != 0) result |= KeyModifiers.Alt;
            return result;
        }

        /// <summary>Le nom d'une touche (celui des réglages) vers la touche
        /// Avalonia ; false si le nom n'en est pas une.</summary>
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
