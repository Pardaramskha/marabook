using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Wpf
{
    /// <summary>Le moteur de polices WPF (portage Avalonia, P0 — l'ancien
    /// WpfGlyphMetrics + FontCache) : GlyphTypeface pour les avances exactes
    /// et les glyphes, FormattedText pour le repli (caractères hors police),
    /// le même algorithme que le compositeur a toujours eu, cache compris.
    /// Le cœur ne voit que FaceInfo ; la poignée WPF voyage dans Native.</summary>
    public sealed class WpfFontEngine : IFontEngine
    {
        /// <summary>Ce que le rendu WPF reprend d'une face : le GlyphTypeface
        /// (GlyphRun) et le Typeface (FormattedText).</summary>
        public sealed class Handle
        {
            public GlyphTypeface Glyphs;
            public Typeface Typeface;
        }

        // Le cache de mesure historique : résolution une fois par
        // famille|graisse|italique, partagé par toutes les instances.
        private static readonly Dictionary<string, FaceInfo> _faces = new Dictionary<string, FaceInfo>();

        public static Handle HandleOf(FaceInfo face)
        {
            return face == null ? null : face.Native as Handle;
        }

        public static GlyphTypeface GlyphsOf(FaceInfo face)
        {
            var handle = HandleOf(face);
            return handle == null ? null : handle.Glyphs;
        }

        public FaceInfo Resolve(string family, int weight, bool italic)
        {
            var key = family + "|" + weight + "|" + italic;
            FaceInfo face;
            lock (_faces)
            {
                if (_faces.TryGetValue(key, out face)) return face;
            }
            var typeface = new Typeface(new FontFamily(family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                FontWeight.FromOpenTypeWeight(weight),
                FontStretches.Normal);
            GlyphTypeface glyphs;
            if (!typeface.TryGetGlyphTypeface(out glyphs)) glyphs = null;
            face = new FaceInfo
            {
                Family = family,
                Weight = weight,
                Italic = italic,
                ActualWeight = weight,
                ActualItalic = italic,
                HasGlyphs = glyphs != null,
                Baseline = glyphs != null ? glyphs.Baseline : 0.8,
                Native = new Handle { Glyphs = glyphs, Typeface = typeface }
            };
            if (glyphs != null)
            {
                // La face servie : son fichier peut ne pas avoir la graisse
                // demandée (demi-gras → regular) — le PDF décrit ce qu'il embarque.
                face.ActualWeight = glyphs.Weight.ToOpenTypeWeight();
                face.ActualItalic = glyphs.Style != FontStyles.Normal;
                // PIÈGE : GlyphTypeface.Equals compare le FICHIER — deux
                // instances d'une fonte variable sont « égales » avec des
                // métriques différentes. Clé composite obligatoire (fichier +
                // graisse servie + style + simulations — la clé historique du PDF).
                face.Key = glyphs.FontUri + "|" + face.ActualWeight + "|" + glyphs.Style + "|" + (int)glyphs.StyleSimulations;
                face.File = glyphs.FontUri.ToString();
                face.UnderlinePosition = glyphs.UnderlinePosition;
                face.UnderlineThickness = glyphs.UnderlineThickness;
                face.StrikethroughPosition = glyphs.StrikethroughPosition;
                face.StrikethroughThickness = glyphs.StrikethroughThickness;
                face.SimulatedBold = (glyphs.StyleSimulations & StyleSimulations.BoldSimulation) != 0;
                face.SimulatedItalic = (glyphs.StyleSimulations & StyleSimulations.ItalicSimulation) != 0;
            }
            else face.Key = key;
            lock (_faces)
            {
                _faces[key] = face;
            }
            return face;
        }

        public bool TryGlyph(FaceInfo face, char c, out ushort glyph)
        {
            glyph = 0;
            var glyphs = GlyphsOf(face);
            return glyphs != null && glyphs.CharacterToGlyphMap.TryGetValue(c, out glyph);
        }

        public double GlyphAdvance(FaceInfo face, ushort glyph)
        {
            var glyphs = GlyphsOf(face);
            return glyphs == null ? 0 : glyphs.AdvanceWidths[glyph];
        }

        public IEnumerable<KeyValuePair<int, ushort>> CharacterMap(FaceInfo face)
        {
            var glyphs = GlyphsOf(face);
            if (glyphs == null) yield break;
            foreach (var pair in glyphs.CharacterToGlyphMap)
                yield return new KeyValuePair<int, ushort>(pair.Key, pair.Value);
        }

        public TextExtent Measure(FaceInfo face, string text, double emSize)
        {
            var measured = Format(face, text ?? "", emSize, Brushes.Black);
            return new TextExtent
            {
                Width = measured.WidthIncludingTrailingWhitespace,
                Height = measured.Height,
                Baseline = measured.Baseline
            };
        }

        /// <summary>Le FormattedText d'un repli — la même construction pour la
        /// mesure (ici) et le dessin (ComposedRenderer) : les deux coïncident.</summary>
        public static FormattedText Format(FaceInfo face, string text, double emSize, Brush ink)
        {
            var handle = HandleOf(face);
            var typeface = handle != null ? handle.Typeface : new Typeface(face.Family);
            return new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, emSize, ink, 1.0);
        }

        public byte[] FontFile(FaceInfo face, out int faceIndex, out string fallbackName)
        {
            faceIndex = 0;
            fallbackName = null;
            var glyphs = GlyphsOf(face);
            if (glyphs == null) return null;
            try
            {
                byte[] data;
                using (var stream = glyphs.GetFontStream())
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                try
                {
                    // Les collections adressent la face dans le fragment de l'uri (#N).
                    var fragment = glyphs.FontUri.Fragment;
                    if (!string.IsNullOrEmpty(fragment))
                        int.TryParse(fragment.TrimStart('#'), out faceIndex);
                }
                catch { }
                foreach (var name in glyphs.FamilyNames.Values) { fallbackName = name; break; }
                return data;
            }
            catch { return null; }
        }

        public byte[] RasterizeText(FaceInfo face, string text, double emSize, Ink ink, double scale,
            out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var brush = new SolidColorBrush(Color.FromArgb(ink.A, ink.R, ink.G, ink.B));
                brush.Freeze();
                var formatted = Format(face, text, emSize, brush);
                var wPx = formatted.WidthIncludingTrailingWhitespace;
                var hPx = formatted.Height;
                if (wPx < 0.1 || hPx < 0.1) return null;
                var pixelW = Math.Max(1, (int)Math.Ceiling(wPx * scale));
                var pixelH = Math.Max(1, (int)Math.Ceiling(hPx * scale));
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawText(formatted, new Point(0, 0));
                    dc.Pop();
                }
                var bitmap = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                return WpfImageCodec.Rgb24OverWhite(bitmap, out width, out height);
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ IGlyphMetrics

        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, char c)
        {
            return AdvanceWidth(fontFamily, emSize, weight, italic, c.ToString());
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight,
            bool italic, string s)
        {
            var face = Resolve(fontFamily, weight, italic);
            var glyphs = GlyphsOf(face);
            if (glyphs != null)
            {
                double width = 0;
                var complete = true;
                foreach (var c in s)
                {
                    ushort glyph;
                    if (glyphs.CharacterToGlyphMap.TryGetValue(c, out glyph))
                        width += glyphs.AdvanceWidths[glyph] * emSize;
                    else { complete = false; break; }
                }
                if (complete) return width;
            }
            // Un seul caractère hors police fait basculer TOUTE la chaîne sur
            // FormattedText (même règle que le rendu : la pièce entière est
            // rendue en repli, les largeurs doivent suivre le même chemin).
            return Measure(face, s, emSize).Width;
        }

        public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
        {
            var glyphs = GlyphsOf(Resolve(fontFamily, weight, italic));
            return glyphs != null && glyphs.CharacterToGlyphMap.ContainsKey(c);
        }

        public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic)
        {
            var face = Resolve(fontFamily, weight, italic);
            // Historique du compositeur : ascendante = Baseline × em, hauteur
            // de ligne = 1,25 × em. Le découpage Ascent/Descent reproduit ces
            // deux valeurs à l'identique (non-régression byte à byte du PDF).
            var ascent = face.Baseline * emSize;
            return new FontMetrics
            {
                Ascent = ascent,
                Descent = emSize * 1.25 - ascent,
                LineGap = 0
            };
        }
    }
}
