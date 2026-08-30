using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shapes;

namespace UniversSale.View
{
    /// <summary>LE seul endroit où l'on fabrique un bouton (batch 40). Trois
    /// formes — icône seule (carré), texte, icône + texte — à deux hauteurs
    /// seulement : 32 px dans les barres principales, 26 px dans les barres
    /// compactes de panneau. Rayon 6 px partout. Trois apparences : calme
    /// (transparent au repos, accent-tint au survol — ce qui rend une barre
    /// de vingt outils supportable), contour (raised + line-strong, le style
    /// implicite des dialogues) et principal (accent plein, un seul par
    /// zone) ; l'état « actif » est la bascule cochée (accent-tint, bordure
    /// accent-soft, texte accent-strong). L'icône suit la couleur du texte
    /// (liée au Foreground du bouton), donc l'état. Pas de classe de jeton,
    /// pas de fabrique de variantes : des méthodes statiques.</summary>
    public static class Buttons
    {
        public const double Bar = 32;     // barres principales
        public const double Compact = 26; // barres compactes de panneau
        public const double IconSize = 16;

        public enum Look { Calm, Outline, Primary }

        // ---- boutons

        /// <summary>Icône seule : carré, icône centrée. Infobulle obligatoire
        /// (avec le raccourci quand il y en a un).</summary>
        public static Button Icon(string icon, string tooltip, double height, Look look)
        {
            var button = new Button { Content = IconContent(icon), Width = height, Padding = new Thickness(0) };
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
            var button = new Button { Content = IconTextContent(icon, label), Padding = new Thickness(8, 0, 10, 0) };
            Dress(button, tooltip, height, look);
            return button;
        }

        // ---- bascules (calmes ; cochées = actives)

        public static ToggleButton IconToggle(string icon, string tooltip, double height)
        {
            var button = new ToggleButton { Content = IconContent(icon), Width = height, Padding = new Thickness(0) };
            Dress(button, tooltip, height, Look.Calm);
            return button;
        }

        public static ToggleButton TextToggle(string label, string tooltip, double height)
        {
            var button = new ToggleButton { Content = TextContent(label), Padding = new Thickness(10, 0, 10, 0) };
            Dress(button, tooltip, height, Look.Calm);
            return button;
        }

        public static ToggleButton IconTextToggle(string icon, string label, string tooltip, double height)
        {
            var button = new ToggleButton { Content = IconTextContent(icon, label), Padding = new Thickness(8, 0, 10, 0) };
            Dress(button, tooltip, height, Look.Calm);
            return button;
        }

        // ---- la seule mise en forme

        private static void Dress(ButtonBase button, string tooltip, double height, Look look)
        {
            button.Height = height;
            button.MinWidth = height;
            button.Focusable = false;
            button.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrEmpty(tooltip)) button.ToolTip = tooltip;
            // Référence DYNAMIQUE : le dictionnaire du thème est remplacé à
            // chaque bascule clair/sombre, le style doit être re-résolu.
            var key = look == Look.Calm
                ? (button is ToggleButton ? "CalmToggle" : "CalmButton")
                : look == Look.Primary ? "PrimaryButton" : null;
            if (key != null) button.SetResourceReference(FrameworkElement.StyleProperty, key);
        }

        private static UIElement IconContent(string icon)
        {
            var glyph = Icons.Make(icon, IconSize, Chrome.Ink) as FrameworkElement;
            if (glyph == null) return new TextBlock { Text = "?" };
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            FollowForeground(glyph);
            return glyph;
        }

        private static UIElement TextContent(string label)
        {
            return new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        }

        private static UIElement IconTextContent(string icon, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Icons.Make(icon, IconSize, Chrome.Ink) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 6, 0);
                FollowForeground(glyph);
                row.Children.Add(glyph);
            }
            row.Children.Add(TextContent(label));
            return row;
        }

        /// <summary>L'icône prend la couleur du texte du bouton — encre au
        /// repos, accent-strong active, papier sur le principal.</summary>
        private static void FollowForeground(FrameworkElement glyph)
        {
            var path = glyph as Path;
            if (path == null) return;
            path.SetBinding(Shape.FillProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ButtonBase), 1)
            });
        }
    }
}
