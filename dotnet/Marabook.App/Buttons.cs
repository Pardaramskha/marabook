using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>LE seul endroit où l'on fabrique un bouton (porté de
    /// View/Buttons.cs) : trois formes — icône seule (carré), texte, icône +
    /// texte — à deux hauteurs (32 px dans les barres principales, 26 px dans
    /// les barres compactes), rayon 6 px, trois apparences : calme, contour,
    /// principal. L'icône suit la couleur du texte (liée au Foreground du
    /// bouton), donc l'état.</summary>
    public static class Buttons
    {
        public const double Bar = 32;
        public const double Compact = 26;
        public const double IconSize = 16;

        public enum Look { Calm, Outline, Primary }

        public static Button Icon(string icon, string tooltip, double height, Look look)
        {
            var button = new Button { Padding = new Thickness(0), Width = height };
            button.Content = IconContent(button, icon);
            Dress(button, tooltip, height, look);
            return button;
        }

        public static Button Text(string label, string tooltip, double height, Look look)
        {
            var button = new Button { Content = TextContent(label), Padding = new Thickness(10, 0, 10, 0) };
            Dress(button, tooltip, height, look);
            return button;
        }

        public static Button IconText(string icon, string label, string tooltip, double height, Look look)
        {
            var button = new Button { Padding = new Thickness(8, 0, 10, 0) };
            button.Content = IconTextContent(button, icon, label);
            Dress(button, tooltip, height, look);
            return button;
        }

        /// <summary>Le GRAND CARRÉ du ruban : icône en haut au centre, libellé
        /// dessous, toute la hauteur du ruban.</summary>
        public static Button Big(string icon, string label, string tooltip, double size, Look look)
        {
            var button = new Button();
            button.Content = BigContent(button, icon, label);
            DressBig(button, tooltip, size, look);
            return button;
        }

        public static ToggleButton BigToggle(string icon, string label, string tooltip, double size, Look look)
        {
            var button = new ToggleButton();
            button.Content = BigContent(button, icon, label);
            DressBig(button, tooltip, size, look);
            return button;
        }

        public static ToggleButton IconToggle(string icon, string tooltip, double height)
        {
            return IconToggle(icon, tooltip, height, Look.Calm);
        }

        public static ToggleButton IconToggle(string icon, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Padding = new Thickness(0), Width = height };
            button.Content = IconContent(button, icon);
            Dress(button, tooltip, height, look);
            return button;
        }

        public static ToggleButton TextToggle(string label, string tooltip, double height)
        {
            return TextToggle(label, tooltip, height, Look.Calm);
        }

        public static ToggleButton TextToggle(string label, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Content = TextContent(label), Padding = new Thickness(10, 0, 10, 0) };
            Dress(button, tooltip, height, look);
            return button;
        }

        public static ToggleButton IconTextToggle(string icon, string label, string tooltip, double height)
        {
            return IconTextToggle(icon, label, tooltip, height, Look.Calm);
        }

        public static ToggleButton IconTextToggle(string icon, string label, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Padding = new Thickness(8, 0, 10, 0) };
            button.Content = IconTextContent(button, icon, label);
            Dress(button, tooltip, height, look);
            return button;
        }

        // ---- habillage

        private static void Dress(TemplatedControl button, string tooltip, double height, Look look)
        {
            button.Height = height;
            button.MinHeight = height;
            button.FontSize = 13;
            button.Focusable = false; // le clavier reste au texte
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(button, tooltip);
            ApplyLook(button, look);
        }

        private static void DressBig(TemplatedControl button, string tooltip, double size, Look look)
        {
            button.Height = size;
            button.MinWidth = size;
            button.Padding = new Thickness(8, 4, 8, 4);
            button.FontSize = 11;
            button.Focusable = false;
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(button, tooltip);
            ApplyLook(button, look);
        }

        private static void ApplyLook(TemplatedControl button, Look look)
        {
            if (!button.Classes.Contains(Theme.Owned)) button.Classes.Add(Theme.Owned);
            button.Classes.Remove(Theme.Calm);
            button.Classes.Remove(Theme.Primary);
            if (look == Look.Calm) button.Classes.Add(Theme.Calm);
            else if (look == Look.Primary) button.Classes.Add(Theme.Primary);
        }

        // ---- contenus

        /// <summary>L'icône dans la couleur du texte du bouton (liaison au
        /// Foreground : elle suit l'état actif, principal, désactivé).</summary>
        private static Control IconContent(TemplatedControl owner, string icon)
        {
            var glyph = Icons.Make(icon, IconSize, owner.Foreground);
            var path = glyph as Path;
            if (path != null) path.Bind(Shape.FillProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            return glyph;
        }

        private static Control TextContent(string label)
        {
            return new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        }

        private static Control IconTextContent(TemplatedControl owner, string icon, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = IconContent(owner, icon);
            glyph.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(glyph);
            row.Children.Add(TextContent(label));
            return row;
        }

        private static Control BigContent(TemplatedControl owner, string icon, string label)
        {
            var column = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var glyph = Icons.Make(icon, 24, owner.Foreground);
            var path = glyph as Path;
            if (path != null) path.Bind(Shape.FillProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.Margin = new Thickness(0, 2, 0, 4);
            column.Children.Add(glyph);
            column.Children.Add(new TextBlock
            {
                Text = label,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 84
            });
            return column;
        }
    }
}
