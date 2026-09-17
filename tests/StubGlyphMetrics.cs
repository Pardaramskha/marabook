using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>Métriques fixes et déterministes pour tester la coupure de
    /// ligne et la pagination SANS dépendre des polices installées : chaque
    /// caractère avance d'un demi-cadratin, ascendante 0,8 em, descendante
    /// 0,2 em. Les positions attendues se calculent à la main.</summary>
    public sealed class StubGlyphMetrics : IGlyphMetrics
    {
        public const double CharFactor = 0.5;   // avance = emSize × 0,5
        public const double AscentFactor = 0.8;
        public const double DescentFactor = 0.2;

        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, char c)
        {
            return emSize * CharFactor;
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, string s)
        {
            return emSize * CharFactor * (s == null ? 0 : s.Length);
        }

        public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
        {
            return true;
        }

        public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic)
        {
            return new FontMetrics
            {
                Ascent = emSize * AscentFactor,
                Descent = emSize * DescentFactor,
                LineGap = 0
            };
        }
    }
}
