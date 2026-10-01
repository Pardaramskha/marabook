using System;
using System.Collections.Generic;
using Marabook.Model;

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
    /// tests (StubGlyphMetrics in tests/); the app injects its font engine.
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

    /// <summary>Une face résolue (portage Avalonia, P0) : ce que le compositeur
    /// et le PDF savent d'une police sans toucher à System.Windows. Native
    /// porte la poignée de la plate-forme (GlyphTypeface en WPF, SKTypeface
    /// demain) : le rendu la reprend, le cœur ne la regarde jamais.</summary>
    public sealed class FaceInfo
    {
        public string Family;
        public int Weight;            // OpenType, la graisse DEMANDÉE (mesure, cache)
        public bool Italic;           // le style demandé
        /// <summary>La graisse et le style que la face SERT : une demande de
        /// demi-gras sur une famille qui n'en a pas rend le fichier regular —
        /// c'est lui que le PDF embarque et décrit (et il fusionne alors avec
        /// la face regular, comme toujours).</summary>
        public int ActualWeight;
        public bool ActualItalic;
        /// <summary>L'identité de la face : fichier + graisse + style +
        /// simulations. PIÈGE historique : deux instances d'une fonte variable
        /// partagent le fichier mais pas les métriques — la clé les sépare.</summary>
        public string Key;
        /// <summary>L'identité du FICHIER de police seul (l'uri en WPF, le
        /// chemin demain) : l'étiquette de sous-ensemble du PDF en dérive.</summary>
        public string File;
        /// <summary>La face a des glyphes adressables (sinon tout passe par le
        /// repli de la plate-forme : mesure et dessin du texte entier).</summary>
        public bool HasGlyphs;
        public double Baseline;       // ascendante, en cadratins (0,8 sans face)
        // Décorations, en cadratins, conventions WPF (position négative = sous
        // la ligne de base) ; 0 = inconnu, le rendu prend ses ratios.
        public double UnderlinePosition, UnderlineThickness;
        public double StrikethroughPosition, StrikethroughThickness;
        public bool SimulatedBold;    // la plate-forme épaissit une face qui n'a pas la graisse
        public bool SimulatedItalic;
        public object Native;
    }

    /// <summary>La mesure d'un texte par la plate-forme (repli : caractères
    /// hors police, libellés du PDF).</summary>
    public struct TextExtent
    {
        public double Width;
        public double Height;
        public double Baseline;
    }

    /// <summary>La couture complète entre le cœur et les polices réelles :
    /// résolution des faces, glyphes et avances (pièces composées, PDF),
    /// mesure du repli, fichier de police à embarquer, rastérisation du
    /// repli. L'app WPF l'implémente sur GlyphTypeface (Wpf/WpfFontEngine),
    /// l'app Avalonia sur SkiaSharp/HarfBuzz ; un simple IGlyphMetrics
    /// (le stub des tests) est enveloppé par FallbackFontEngine.</summary>
    public interface IFontEngine : IGlyphMetrics
    {
        FaceInfo Resolve(string family, int weight, bool italic);
        /// <summary>L'index de glyphe d'un caractère dans la face ; false =
        /// hors police.</summary>
        bool TryGlyph(FaceInfo face, char c, out ushort glyph);
        /// <summary>L'avance naturelle d'un glyphe, en cadratins.</summary>
        double GlyphAdvance(FaceInfo face, ushort glyph);
        /// <summary>La table caractère → glyphe entière (ToUnicode du PDF).</summary>
        IEnumerable<KeyValuePair<int, ushort>> CharacterMap(FaceInfo face);
        /// <summary>La mesure d'un texte par le moteur de texte de la
        /// plate-forme — la route du repli, et des libellés.</summary>
        TextExtent Measure(FaceInfo face, string text, double emSize);
        /// <summary>Le fichier de police (TTF, OTF, TTC) et l'index de la face
        /// dans une collection ; null = pas embarquable.</summary>
        byte[] FontFile(FaceInfo face, out int faceIndex, out string fallbackName);
        /// <summary>Le texte rastérisé en RVB24 sur fond blanc, à
        /// <paramref name="scale"/> pixels par px ; null = impossible.</summary>
        byte[] RasterizeText(FaceInfo face, string text, double emSize, Ink ink, double scale,
            out int width, out int height);
    }

    /// <summary>Un IGlyphMetrics nu (le stub des tests) vu comme moteur de
    /// polices : aucune face n'a de glyphes, chaque pièce est un « repli »
    /// mesuré par les avances du stub — la composition reste déterministe et
    /// sans police installée.</summary>
    public sealed class FallbackFontEngine : IFontEngine
    {
        private readonly IGlyphMetrics _metrics;
        private readonly Dictionary<string, FaceInfo> _faces = new Dictionary<string, FaceInfo>();

        public FallbackFontEngine(IGlyphMetrics metrics)
        {
            _metrics = metrics;
        }

        /// <summary>Le moteur de polices derrière des métriques : lui-même
        /// s'il en est un, sinon une enveloppe.</summary>
        public static IFontEngine Wrap(IGlyphMetrics metrics)
        {
            return metrics as IFontEngine ?? new FallbackFontEngine(metrics);
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, char c)
        {
            return _metrics.AdvanceWidth(fontFamily, emSize, weight, italic, c);
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, string s)
        {
            return _metrics.AdvanceWidth(fontFamily, emSize, weight, italic, s);
        }

        public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
        {
            return _metrics.HasGlyph(fontFamily, weight, italic, c);
        }

        public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic)
        {
            return _metrics.Metrics(fontFamily, emSize, weight, italic);
        }

        public FaceInfo Resolve(string family, int weight, bool italic)
        {
            var key = family + "|" + weight + "|" + italic;
            FaceInfo face;
            if (_faces.TryGetValue(key, out face)) return face;
            var metrics = _metrics.Metrics(family, 1.0, weight, italic);
            face = new FaceInfo
            {
                Family = family,
                Weight = weight,
                Italic = italic,
                ActualWeight = weight,
                ActualItalic = italic,
                Key = key,
                HasGlyphs = false,
                Baseline = metrics != null && metrics.Ascent > 0 ? metrics.Ascent : 0.8
            };
            _faces[key] = face;
            return face;
        }

        public bool TryGlyph(FaceInfo face, char c, out ushort glyph)
        {
            glyph = 0;
            return false;
        }

        public double GlyphAdvance(FaceInfo face, ushort glyph)
        {
            return 0;
        }

        public IEnumerable<KeyValuePair<int, ushort>> CharacterMap(FaceInfo face)
        {
            return new KeyValuePair<int, ushort>[0];
        }

        public TextExtent Measure(FaceInfo face, string text, double emSize)
        {
            var metrics = _metrics.Metrics(face.Family, emSize, face.Weight, face.Italic);
            return new TextExtent
            {
                Width = _metrics.AdvanceWidth(face.Family, emSize, face.Weight, face.Italic, text ?? ""),
                Height = metrics.LineHeight,
                Baseline = metrics.Ascent
            };
        }

        public byte[] FontFile(FaceInfo face, out int faceIndex, out string fallbackName)
        {
            faceIndex = 0;
            fallbackName = null;
            return null;
        }

        public byte[] RasterizeText(FaceInfo face, string text, double emSize, Ink ink, double scale,
            out int width, out int height)
        {
            width = 0;
            height = 0;
            return null;
        }
    }
}
