using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;

namespace Marabook.App
{
    /// <summary>La position de texte sous un point d'un TextBlock (liens
    /// natifs du wiki et du Texte libre — 29/09). TextLayout.HitTestPoint
    /// d'Avalonia 11.3 juge « dedans » avec l'ordonnée ABSOLUE du point
    /// contre la hauteur de la seule ligne touchée : tout ce qui est au-delà
    /// de la première ligne d'un paragraphe renvoyé sort « dehors », et les
    /// liens des lignes suivantes étaient injoignables. On cherche la ligne
    /// soi-même.</summary>
    public static class TextHit
    {
        /// <summary>L'index du caractère sous le point (coordonnées du
        /// TextBlock), ou -1 hors du texte.</summary>
        public static int PositionAt(TextBlock block, Point point)
        {
            if (block.TextLayout == null) return -1;
            return PositionAt(block.TextLayout, new Point(point.X - block.Padding.Left, point.Y - block.Padding.Top));
        }

        /// <summary>L'index du caractère sous le point (coordonnées de la mise
        /// en page elle-même — celle du TextPresenter d'un TextBox, par
        /// exemple), ou -1 hors du texte. Le clic droit du corps Markdown des
        /// fiches passait par HitTestPoint : au-delà de la première ligne,
        /// ni lien ni signalement sous le clic (1.0.5-b).</summary>
        public static int PositionAt(TextLayout layout, Point point)
        {
            if (layout == null) return -1;
            var x = point.X;
            var y = point.Y;
            if (x < 0 || y < 0) return -1;
            var top = 0.0;
            foreach (var line in layout.TextLines)
            {
                if (y < top + line.Height)
                {
                    var left = line.Start;
                    if (x < left || x > left + line.WidthIncludingTrailingWhitespace) return -1;
                    // Le caractère touché, par sa moitié gauche ou droite
                    // (TrailingLength ignoré : un lien en fin de ligne
                    // répond jusqu'à son dernier pixel).
                    return line.GetCharacterHitFromDistance(x - left).FirstCharacterIndex;
                }
                top += line.Height;
            }
            return -1;
        }
    }
}
