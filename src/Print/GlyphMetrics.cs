using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Marabook.Print
{
    /// <summary>Vertical metrics of a face at a given em size (px).</summary>
    public class FontMetrics
    {
        public double Ascent;   // baseline height above the line top
        public double Descent;  // below the baseline
        public double LineGap;  // extra leading built into the face

        public double LineHeight { get { return Ascent + Descent + LineGap; } }
    }

    /// <summary>The composition engine's ONLY road to character advances and
    /// face metrics (batch 24). Behind this seam, composition — line breaking,
    /// hyphenation, pagination — runs headless and deterministic in console
    /// tests (StubGlyphMetrics in tests/); the app injects WpfGlyphMetrics.
    /// « weight » is an OpenType weight (400 normal, 700 bold) : le pivot
    /// porte des graisses fines (TextRun.Weight), un booléen les écraserait
    /// et fausserait les largeurs mesurées.</summary>
    public interface IGlyphMetrics
    {
        double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, char c);
        double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, string s);
        bool HasGlyph(string fontFamily, int weight, bool italic, char c);
        FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic);
    }

    /// <summary>A resolved face: the glyph typeface for exact advances and
    /// piece building, the Typeface for the FormattedText fallback (characters
    /// outside the face). Cached — see FontCache.</summary>
    internal sealed class FontInfo
    {
        public GlyphTypeface Glyphs;
        public Typeface Typeface;
        public double Baseline;
        public string Family;
        public int WeightValue; // OpenType weight
        public bool Italic;
    }

    /// <summary>The measuring cache the composer always had (résolution
    /// GlyphTypeface une fois par famille|graisse|italique), shared by the
    /// engine's piece building and by WpfGlyphMetrics.</summary>
    internal static class FontCache
    {
        private static readonly Dictionary<string, FontInfo> _fonts =
            new Dictionary<string, FontInfo>();

        internal static FontInfo Resolve(string family, FontWeight weight, bool italic)
        {
            var key = family + "|" + weight + "|" + italic;
            FontInfo info;
            lock (_fonts)
            {
                if (_fonts.TryGetValue(key, out info)) return info;
            }
            info = new FontInfo();
            info.Typeface = new Typeface(new FontFamily(family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                weight,
                FontStretches.Normal);
            GlyphTypeface glyphs;
            info.Glyphs = info.Typeface.TryGetGlyphTypeface(out glyphs) ? glyphs : null;
            info.Baseline = info.Glyphs != null ? info.Glyphs.Baseline : 0.8;
            info.Family = family;
            info.WeightValue = weight.ToOpenTypeWeight();
            info.Italic = italic;
            lock (_fonts)
            {
                _fonts[key] = info;
            }
            return info;
        }
    }

    /// <summary>WPF implementation: GlyphTypeface advances, FormattedText
    /// fallback for characters outside the face — the exact algorithm the
    /// composer always used, cache included. No behavior change.</summary>
    public sealed class WpfGlyphMetrics : IGlyphMetrics
    {
        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, char c)
        {
            return AdvanceWidth(fontFamily, emSize, weight, italic, c.ToString());
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, string s)
        {
            var font = FontCache.Resolve(fontFamily,
                FontWeight.FromOpenTypeWeight(weight), italic);
            if (font.Glyphs != null)
            {
                double width = 0;
                var complete = true;
                foreach (var c in s)
                {
                    ushort glyph;
                    if (font.Glyphs.CharacterToGlyphMap.TryGetValue(c, out glyph))
                        width += font.Glyphs.AdvanceWidths[glyph] * emSize;
                    else { complete = false; break; }
                }
                if (complete) return width;
            }
            // Un seul caractère hors police fait basculer TOUTE la chaîne sur
            // FormattedText (même règle que le rendu : la pièce entière est
            // rendue en repli, les largeurs doivent suivre le même chemin).
            return new FormattedText(s, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, font.Typeface, emSize, Brushes.Black, 1.0)
                .WidthIncludingTrailingWhitespace;
        }

        public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
        {
            var font = FontCache.Resolve(fontFamily,
                FontWeight.FromOpenTypeWeight(weight), italic);
            return font.Glyphs != null && font.Glyphs.CharacterToGlyphMap.ContainsKey(c);
        }

        public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic)
        {
            var font = FontCache.Resolve(fontFamily,
                FontWeight.FromOpenTypeWeight(weight), italic);
            // Historique du compositeur : ascendante = Baseline × em, hauteur
            // de ligne = 1,25 × em. Le découpage Ascent/Descent reproduit ces
            // deux valeurs à l'identique (non-régression byte à byte du PDF).
            var ascent = font.Baseline * emSize;
            return new FontMetrics
            {
                Ascent = ascent,
                Descent = emSize * 1.25 - ascent,
                LineGap = 0
            };
        }
    }
}
